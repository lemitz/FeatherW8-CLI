using System.Buffers.Binary;
using FeatherW8.Core;
using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// Raw DSD from a .dsf (DSF) or .dff (DSDIFF, uncompressed) file as a seekable byte stream.
/// The bytes are channel-interleaved (one byte per channel per frame, 8 DSD samples each) with
/// the first DSD sample in the most significant bit - the layout of ASIO's DSDInt8MSB1 and of
/// DoP. DSF files store the bits LSB-first; they are reversed here, which is lossless.
/// <para>
/// This is not PCM. <see cref="WaveFormat"/> is a stand-in (8-bit, DSD rate / 8) so the player's
/// position and duration arithmetic works unchanged; the real DSD rate is <see cref="DsdSampleRate"/>.
/// </para>
/// </summary>
internal sealed class DsdWaveStream : WaveStream
{
    /// <summary>DSD "silence" - a bit pattern that decodes to a zero-level signal.</summary>
    public const byte SilenceByte = 0x69;

    private enum Container
    {
        Dsf,
        Dff
    }

    private static readonly byte[] BitReverse = BuildBitReverse();

    private readonly FileStream _file;
    private readonly Container _container;
    private readonly bool _reverseBits;
    private readonly int _channels;
    private readonly long _bytesPerChannel;
    private readonly long _dataStart;
    private readonly int _dsfBlockSize;
    private readonly WaveFormat _waveFormat;

    private byte[]? _block;       // DSF: one decoded block group, interleaved
    private long _blockIndex = -1;
    private long _position;       // interleaved bytes
    private bool _disposed;

    public DsdWaveStream(string path)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.RandomAccess);
        try
        {
            Span<byte> magic = stackalloc byte[4];
            _file.ReadExactly(magic);
            _file.Position = 0;

            if (magic.SequenceEqual("DSD "u8))
            {
                _container = Container.Dsf;
                ParseDsf(out int sampleRate, out _channels, out _bytesPerChannel, out _dsfBlockSize,
                    out _reverseBits, out _dataStart);
                DsdSampleRate = sampleRate;
            }
            else if (magic.SequenceEqual("FRM8"u8))
            {
                _container = Container.Dff;
                ParseDff(out int sampleRate, out _channels, out _bytesPerChannel, out _dataStart);
                DsdSampleRate = sampleRate;
            }
            else
            {
                throw new InvalidDataException(Texts.Get("DsdInvalid"));
            }

            if (_channels is < 1 or > 2)
                throw new NotSupportedException(Texts.Format("DsdChannels", _channels));
            if (DsdSampleRate < 2_000_000 || DsdSampleRate % 8 != 0)
                throw new InvalidDataException(Texts.Get("DsdInvalid"));

            _waveFormat = new WaveFormat(DsdSampleRate / 8, 8, _channels);
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    /// <summary>One-bit sample rate in Hz, e.g. 2 822 400 for DSD64.</summary>
    public int DsdSampleRate { get; }

    public int Channels => _channels;

    /// <summary>"DSD64", "DSD128", ... for the 44.1 kHz family, "DSD64 (48k)" ... for the 48 kHz family.</summary>
    public string RateLabel
    {
        get
        {
            if (DsdSampleRate % 44_100 == 0)
                return $"DSD{DsdSampleRate / 44_100}";
            if (DsdSampleRate % 48_000 == 0)
                return $"DSD{DsdSampleRate / 48_000} (48k)";
            return $"DSD {DsdSampleRate / 1_000_000.0:0.####} MHz";
        }
    }

    public override WaveFormat WaveFormat => _waveFormat;
    public override long Length => _bytesPerChannel * _channels;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long requested = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        long target = Math.Clamp(requested, 0, Length);
        _position = target - target % _channels;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException(Texts.Get("BufferRangeInvalid"));

        long remaining = Length - _position;
        int want = (int)Math.Min(count - count % _channels, remaining);
        if (want <= 0)
            return 0;

        int done = _container == Container.Dsf
            ? ReadDsf(buffer, offset, want)
            : ReadDff(buffer, offset, want);

        _position += done;
        return done;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _file.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ DSF

    private void ParseDsf(out int sampleRate, out int channels, out long bytesPerChannel,
        out int blockSize, out bool reverseBits, out long dataStart)
    {
        Span<byte> head = stackalloc byte[28];
        _file.ReadExactly(head);
        long firstChunk = 28;

        // "fmt " chunk
        _file.Position = firstChunk;
        Span<byte> fmtHead = stackalloc byte[12];
        _file.ReadExactly(fmtHead);
        if (!fmtHead[..4].SequenceEqual("fmt "u8))
            throw new InvalidDataException(Texts.Get("DsdInvalid"));
        long fmtSize = BinaryPrimitives.ReadInt64LittleEndian(fmtHead[4..]);
        if (fmtSize < 52)
            throw new InvalidDataException(Texts.Get("DsdInvalid"));

        // The chunk size includes the 12-byte chunk header; the body starts with the format version:
        // version(4) formatId(4) channelType(4) channels(4) sampleRate(4) bits(4) sampleCount(8) blockSize(4) reserved(4)
        byte[] fmt = new byte[fmtSize - 12];
        _file.ReadExactly(fmt);
        int formatId = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4, 4));
        channels = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(12, 4));
        sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(16, 4));
        int bitsPerSample = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(20, 4));
        long sampleCount = BinaryPrimitives.ReadInt64LittleEndian(fmt.AsSpan(24, 8));
        blockSize = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(32, 4));

        if (formatId != 0)
            throw new NotSupportedException(Texts.Get("DsdCompressed"));
        if (bitsPerSample is not (1 or 8) || blockSize <= 0 || blockSize > (1 << 20) || channels <= 0 || sampleCount <= 0)
            throw new InvalidDataException(Texts.Get("DsdInvalid"));

        reverseBits = bitsPerSample == 1; // 1 = LSB first, 8 = MSB first
        bytesPerChannel = (sampleCount + 7) / 8;

        // "data" chunk
        _file.Position = firstChunk + fmtSize;
        Span<byte> dataHead = stackalloc byte[12];
        _file.ReadExactly(dataHead);
        if (!dataHead[..4].SequenceEqual("data"u8))
            throw new InvalidDataException(Texts.Get("DsdInvalid"));
        dataStart = firstChunk + fmtSize + 12;
    }

    private int ReadDsf(byte[] buffer, int offset, int count)
    {
        int ch = _channels;
        int written = 0;
        while (written < count)
        {
            long frame = (_position + written) / ch;
            long blockIndex = frame / _dsfBlockSize;
            int inBlock = (int)(frame % _dsfBlockSize);

            if (blockIndex != _blockIndex)
                LoadDsfBlock(blockIndex);

            int available = (_dsfBlockSize - inBlock) * ch;
            int take = Math.Min(count - written, available);
            Buffer.BlockCopy(_block!, inBlock * ch, buffer, offset + written, take);
            written += take;
        }

        return written;
    }

    private void LoadDsfBlock(long blockIndex)
    {
        int ch = _channels;
        int size = _dsfBlockSize;
        byte[] raw = new byte[size * ch];
        _file.Position = _dataStart + blockIndex * size * ch;

        int got = 0;
        while (got < raw.Length)
        {
            int n = _file.Read(raw, got, raw.Length - got);
            if (n <= 0)
                break;
            got += n;
        }

        if (got < raw.Length) // truncated file: pad with DSD silence instead of noise
            Array.Fill(raw, SilenceByte, got, raw.Length - got);

        _block ??= new byte[size * ch];
        for (int c = 0; c < ch; c++)
        {
            int src = c * size;
            for (int i = 0; i < size; i++)
            {
                byte b = raw[src + i];
                _block[i * ch + c] = _reverseBits ? BitReverse[b] : b;
            }
        }

        _blockIndex = blockIndex;
    }

    // ------------------------------------------------------------------ DSDIFF

    private void ParseDff(out int sampleRate, out int channels, out long bytesPerChannel, out long dataStart)
    {
        sampleRate = 0;
        channels = 0;
        bytesPerChannel = 0;
        dataStart = 0;

        Span<byte> head = stackalloc byte[16];
        _file.ReadExactly(head);
        long formSize = BinaryPrimitives.ReadInt64BigEndian(head[4..]);
        if (!head[12..16].SequenceEqual("DSD "u8))
            throw new InvalidDataException(Texts.Get("DsdInvalid"));

        long end = Math.Min(_file.Length, 12 + formSize);
        long pos = 16;
        bool gotProp = false;
        bool gotData = false;
        bool compressed = false;
        long dataSize = 0;

        Span<byte> chunk = stackalloc byte[12];
        while (pos + 12 <= end && !(gotProp && gotData))
        {
            _file.Position = pos;
            _file.ReadExactly(chunk);
            long size = BinaryPrimitives.ReadInt64BigEndian(chunk[4..]);
            long body = pos + 12;

            if (chunk[..4].SequenceEqual("PROP"u8))
            {
                byte[] prop = new byte[size];
                _file.ReadExactly(prop);
                ParseDffProp(prop, ref sampleRate, ref channels, ref compressed);
                gotProp = true;
            }
            else if (chunk[..4].SequenceEqual("DSD "u8))
            {
                dataStart = body;
                dataSize = size;
                gotData = true;
            }
            else if (chunk[..4].SequenceEqual("DST "u8))
            {
                compressed = true;
                gotData = true;
            }

            if (size < 0)
                throw new InvalidDataException(Texts.Get("DsdInvalid"));
            pos = body + size + (size & 1); // chunks are padded to an even length
        }

        if (compressed)
            throw new NotSupportedException(Texts.Get("DsdCompressed"));
        if (!gotProp || !gotData || sampleRate <= 0 || channels <= 0)
            throw new InvalidDataException(Texts.Get("DsdInvalid"));

        long available = Math.Min(dataSize, _file.Length - dataStart);
        bytesPerChannel = available / channels;
        if (bytesPerChannel <= 0)
            throw new InvalidDataException(Texts.Get("DsdInvalid"));
    }

    private static void ParseDffProp(byte[] prop, ref int sampleRate, ref int channels, ref bool compressed)
    {
        if (prop.Length < 4 || !prop.AsSpan(0, 4).SequenceEqual("SND "u8))
            return;

        int pos = 4;
        while (pos + 12 <= prop.Length)
        {
            ReadOnlySpan<byte> id = prop.AsSpan(pos, 4);
            long size = BinaryPrimitives.ReadInt64BigEndian(prop.AsSpan(pos + 4, 8));
            int body = pos + 12;
            if (size < 0 || body + size > prop.Length)
                break;

            if (id.SequenceEqual("FS  "u8) && size >= 4)
                sampleRate = (int)BinaryPrimitives.ReadUInt32BigEndian(prop.AsSpan(body, 4));
            else if (id.SequenceEqual("CHNL"u8) && size >= 2)
                channels = BinaryPrimitives.ReadUInt16BigEndian(prop.AsSpan(body, 2));
            else if (id.SequenceEqual("CMPR"u8) && size >= 4)
                compressed = !prop.AsSpan(body, 4).SequenceEqual("DSD "u8);

            pos = body + (int)size + (int)(size & 1);
        }
    }

    private int ReadDff(byte[] buffer, int offset, int count)
    {
        _file.Position = _dataStart + _position;
        int total = 0;
        while (total < count)
        {
            int n = _file.Read(buffer, offset + total, count - total);
            if (n <= 0)
                break;
            total += n;
        }

        if (total < count) // truncated file
            Array.Fill(buffer, SilenceByte, offset + total, count - total);

        return count;
    }

    private static byte[] BuildBitReverse()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int v = i, r = 0;
            for (int b = 0; b < 8; b++)
            {
                r = (r << 1) | (v & 1);
                v >>= 1;
            }

            table[i] = (byte)r;
        }

        return table;
    }
}
