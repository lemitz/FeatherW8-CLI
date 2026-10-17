using System.Numerics;
using FeatherW8.Core;
using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// Self-contained, seekable FLAC decoder (mono/stereo, 4-24 bit, any sample rate up to 655 kHz).
/// The decoded samples are delivered exactly as stored: up to 16 bits as 16-bit PCM, 17-24 bits as
/// 24-bit PCM. Narrower depths (8/12/20 bit) are only left-aligned into the next container, which
/// is lossless. Frames are verified with CRC-8 / CRC-16.
/// Seeking bisects the file on the sample numbers in the frame headers, so it works without a
/// SEEKTABLE and takes a few milliseconds even on very long hi-res files.
/// </summary>
internal sealed class FlacWaveStream : WaveStream
{
    private const int ReadAheadBytes = 2 * 1024 * 1024;
    private const int BufferBytes = 4 * 1024 * 1024;

    private readonly FileStream _file;
    private readonly byte[] _buf = new byte[BufferBytes];
    private readonly WaveFormat _waveFormat;
    private readonly int _channels;
    private readonly int _sourceBits;
    private readonly int _outBytesPerSample;
    private readonly int _outShift;
    private readonly int _sampleRate;
    private long _totalSamples;
    private int _fixedBlockSize;   // block size of a fixed-size stream; 0 when headers carry sample numbers
    private readonly long _audioStart;
    private readonly long _fileLength;
    private readonly int _maxBlockSize;

    private long _bufFileStart;   // file offset of _buf[0]
    private int _bufEnd;          // valid bytes in _buf
    private int _pos;             // read cursor in _buf (byte aligned between frames)
    private int _bit;             // bits already consumed in _buf[_pos]

    private int[][] _samples = [new int[4096], new int[4096]];
    private int[] _residual = new int[4096];
    private byte[] _pcm = new byte[4096 * 2 * 3];
    private int _pcmOffset;
    private int _pcmCount;
    private long _nextSample;     // index of the next sample that Read() will deliver
    private bool _eof;
    private bool _disposed;

    public FlacWaveStream(string path)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.RandomAccess);
        try
        {
            _fileLength = _file.Length;
            long cursor = SkipId3v2();
            ParseStreamInfo(cursor, out _sampleRate, out _channels, out _sourceBits, out _totalSamples,
                out _maxBlockSize, out _audioStart);

            if (_channels > 2)
                throw new NotSupportedException(Texts.Format("FlacChannels", _channels));
            if (_sourceBits > 24)
                throw new NotSupportedException(Texts.Format("FlacBitDepth", _sourceBits));
            if (_totalSamples <= 0)
            {
                // STREAMINFO does not declare the length (e.g. a stream saved while encoding):
                // take it from the last frame in the file.
                _totalSamples = long.MaxValue / 4;
                _totalSamples = EstimateTotalSamples();
                if (_totalSamples <= 0)
                    throw new NotSupportedException(Texts.Get("FlacUnknownLength"));
            }


            int outBits = _sourceBits <= 16 ? 16 : 24;
            _outBytesPerSample = outBits / 8;
            _outShift = outBits - _sourceBits;
            _waveFormat = new WaveFormat(_sampleRate, outBits, _channels);

            SeekFileTo(_audioStart);
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    /// <summary>Bit depth stored in the file (the PCM container may be wider).</summary>
    public int SourceBitsPerSample => _sourceBits;

    public override WaveFormat WaveFormat => _waveFormat;
    public override long Length => _totalSamples * _waveFormat.BlockAlign;

    public override long Position
    {
        get => (_nextSample - (_pcmCount - _pcmOffset) / _waveFormat.BlockAlign) * _waveFormat.BlockAlign;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long requested = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        long target = Math.Clamp(requested, 0, Length) / _waveFormat.BlockAlign;
        SeekToSample(target);
        return Position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException(Texts.Get("BufferRangeInvalid"));

        int blockAlign = _waveFormat.BlockAlign;
        int wanted = count - count % blockAlign;
        int written = 0;

        while (written < wanted)
        {
            if (_pcmOffset >= _pcmCount && !DecodeNextFrame())
                break;

            int take = Math.Min(wanted - written, _pcmCount - _pcmOffset);
            Buffer.BlockCopy(_pcm, _pcmOffset, buffer, offset + written, take);
            _pcmOffset += take;
            written += take;
        }

        return written;
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

    // ---------------------------------------------------------------- metadata

    private long SkipId3v2()
    {
        Span<byte> head = stackalloc byte[10];
        _file.Position = 0;
        if (_file.Read(head) == 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3')
        {
            int size = (head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9];
            bool footer = (head[5] & 0x10) != 0;
            return 10 + size + (footer ? 10 : 0);
        }

        return 0;
    }

    private void ParseStreamInfo(long start, out int sampleRate, out int channels, out int bits,
        out long totalSamples, out int maxBlockSize, out long audioStart)
    {
        sampleRate = channels = bits = maxBlockSize = 0;
        totalSamples = 0;
        _file.Position = start;

        Span<byte> marker = stackalloc byte[4];
        if (_file.Read(marker) != 4 || marker[0] != 'f' || marker[1] != 'L' || marker[2] != 'a' || marker[3] != 'C')
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        bool gotStreamInfo = false;
        bool last;
        Span<byte> blockHeader = stackalloc byte[4];
        do
        {
            if (_file.Read(blockHeader) != 4)
                throw new InvalidDataException(Texts.Get("FlacInvalid"));

            last = (blockHeader[0] & 0x80) != 0;
            int type = blockHeader[0] & 0x7F;
            int length = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];

            if (type == 0 && length >= 34)
            {
                byte[] info = new byte[length];
                _file.ReadExactly(info);
                int minBlockSize = (info[0] << 8) | info[1];
                maxBlockSize = (info[2] << 8) | info[3];
                _fixedBlockSize = minBlockSize == maxBlockSize ? maxBlockSize : 0;
                ulong v = 0;
                for (int i = 10; i < 18; i++)
                    v = (v << 8) | info[i];
                sampleRate = (int)(v >> 44);
                channels = (int)((v >> 41) & 7) + 1;
                bits = (int)((v >> 36) & 0x1F) + 1;
                totalSamples = (long)(v & 0xFFFFFFFFFUL);
                gotStreamInfo = true;
            }
            else
            {
                _file.Seek(length, SeekOrigin.Current);
            }
        }
        while (!last);

        if (!gotStreamInfo || sampleRate <= 0)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        audioStart = _file.Position;
    }

    // ---------------------------------------------------------------- buffering

    private void SeekFileTo(long fileOffset)
    {
        _bufFileStart = Math.Clamp(fileOffset, 0, _fileLength);
        _file.Position = _bufFileStart;
        _bufEnd = 0;
        _pos = 0;
        _bit = 0;
        Fill();
    }

    /// <summary>Tops the buffer up so that at least <paramref name="wanted"/> bytes follow the cursor (if the file has them).</summary>
    private void EnsureAvailable(int wanted)
    {
        if (_bufEnd - _pos >= wanted)
            return;

        int keep = _bufEnd - _pos;
        if (_pos > 0)
        {
            Buffer.BlockCopy(_buf, _pos, _buf, 0, keep);
            _bufFileStart += _pos;
            _bufEnd = keep;
            _pos = 0;
        }

        Fill();
    }

    private void Fill()
    {
        while (_bufEnd < _buf.Length)
        {
            int n = _file.Read(_buf, _bufEnd, _buf.Length - _bufEnd);
            if (n <= 0)
                break;
            _bufEnd += n;
        }
    }

    // ---------------------------------------------------------------- seeking

    private long EstimateTotalSamples()
    {
        SeekFileTo(Math.Max(_audioStart, _fileLength - 16 * 1024 * 1024));
        long end = 0;
        while (ScanForHeader(out _, out FlacHeader header))
        {
            end = header.SampleNumber(_fixedBlockSize) + header.BlockSize;
            if (_pos + 1 >= _bufEnd)
                break;
            _pos++;
        }

        SeekFileTo(_audioStart);
        return end;
    }

    private void SeekToSample(long target)
    {
        _pcmOffset = _pcmCount = 0;
        _eof = false;

        if (target >= _totalSamples)
        {
            _nextSample = _totalSamples;
            _eof = true;
            return;
        }

        long lo = _audioStart;
        long hi = _fileLength;
        bool first = true;

        // Bisect on the sample numbers found in frame headers until the target frame (or a
        // short window before it) is located.
        for (int i = 0; i < 64 && hi - lo > 8192; i++)
        {
            long mid = first
                ? lo + (long)((hi - lo) * ((double)target / _totalSamples))
                : lo + (hi - lo) / 2;
            first = false;
            mid = Math.Clamp(mid, lo, hi - 1);

            if (!FindHeaderFrom(mid, out long frameOffset, out FlacHeader header))
            {
                hi = mid;
                continue;
            }

            long frameStartSample = header.SampleNumber(_fixedBlockSize);
            if (target < frameStartSample)
            {
                hi = mid;
            }
            else if (target >= frameStartSample + header.BlockSize)
            {
                lo = frameOffset + 1;
            }
            else
            {
                lo = frameOffset;
                break;
            }
        }

        SeekFileTo(lo);
        if (lo > _audioStart && FindHeaderFrom(lo, out long start, out _))
            SeekFileTo(start);
        else
            SeekFileTo(_audioStart);

        // Decode forward until the frame that contains the target.
        _nextSample = long.MinValue;
        while (DecodeNextFrame(discardUntil: target))
        {
            if (_pcmCount > 0)
                return;
        }

        _nextSample = _totalSamples;
        _eof = true;
    }

    /// <summary>Finds the first valid frame header at or after <paramref name="from"/>. Leaves the cursor unspecified.</summary>
    private bool FindHeaderFrom(long from, out long frameOffset, out FlacHeader header)
    {
        SeekFileTo(from);
        return ScanForHeader(out frameOffset, out header);
    }

    private bool ScanForHeader(out long frameOffset, out FlacHeader header)
    {
        while (true)
        {
            EnsureAvailable(ReadAheadBytes);
            int avail = _bufEnd - _pos;
            if (avail < 2)
            {
                frameOffset = 0;
                header = default;
                return false;
            }

            int limit = _bufEnd - 1;
            for (int i = _pos; i < limit; i++)
            {
                if (_buf[i] != 0xFF || (_buf[i + 1] & 0xFE) != 0xF8)
                    continue;

                if (TryParseHeader(_buf.AsSpan(i, _bufEnd - i), out header) && header.IsPlausible(_maxBlockSize, _fixedBlockSize, _totalSamples))
                {
                    _pos = i;
                    _bit = 0;
                    frameOffset = _bufFileStart + i;
                    return true;
                }
            }

            // Nothing in this window; advance (keeping a tail for headers that straddle the end).
            if (_bufEnd - _pos < ReadAheadBytes && _file.Position >= _fileLength)
            {
                frameOffset = 0;
                header = default;
                return false;
            }

            _pos = Math.Max(_pos, _bufEnd - 32);
        }
    }

    // ---------------------------------------------------------------- frame decoding

    private bool DecodeNextFrame(long discardUntil = long.MinValue)
    {
        if (_eof)
            return false;

        while (true)
        {
            if (_nextSample != long.MinValue && _nextSample >= _totalSamples)
            {
                _eof = true;
                return false;
            }

            EnsureAvailable(ReadAheadBytes);
            if (_bufEnd - _pos < 6)
            {
                _eof = true;
                return false;
            }

            int frameStart = _pos;
            FlacHeader header = default;
            bool ok = _buf[_pos] == 0xFF && (_buf[_pos + 1] & 0xFE) == 0xF8
                      && TryParseHeader(_buf.AsSpan(_pos, _bufEnd - _pos), out header)
                      && header.IsPlausible(_maxBlockSize, _fixedBlockSize, _totalSamples);

            if (ok)
            {
                try
                {
                    DecodeFrameBody(frameStart, header);
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
                {
                    ok = false;
                }
            }

            if (!ok)
            {
                // Corrupt data: resynchronise on the next valid header.
                _pos = frameStart + 1;
                _bit = 0;
                if (!ScanForHeader(out _, out _))
                {
                    _eof = true;
                    return false;
                }

                continue;
            }

            int blockSize = header.BlockSize;
            long firstSample = header.SampleNumber(_fixedBlockSize);

            int skip = 0;

            if (discardUntil != long.MinValue)
            {
                if (firstSample + blockSize <= discardUntil)
                    continue; // still before the target


                skip = (int)Math.Max(0, discardUntil - firstSample);
            }

            int usable = (int)Math.Min(blockSize, _totalSamples - firstSample);
            if (usable <= skip)
            {
                _eof = true;
                return false;
            }

            InterleaveToPcm(skip, usable);
            _nextSample = firstSample + usable;
            return true;
        }
    }

    private void DecodeFrameBody(int frameStart, FlacHeader header)
    {
        _pos += header.Length;
        _bit = 0;

        int blockSize = header.BlockSize;
        int bits = header.BitsPerSample == 0 ? _sourceBits : header.BitsPerSample;
        int chCount = header.ChannelCount;
        if (chCount != _channels || bits != _sourceBits)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        EnsureSampleBuffers(chCount, blockSize);

        for (int ch = 0; ch < chCount; ch++)
        {
            int subBits = bits;
            switch (header.ChannelAssignment)
            {
                case 8 when ch == 1:
                case 9 when ch == 0:
                case 10 when ch == 1:
                    subBits++;
                    break;
            }

            DecodeSubframe(_samples[ch], blockSize, subBits);
        }

        if (_bit != 0)
        {
            _bit = 0;
            _pos++;
        }

        // CRC-16 over header + subframes, then the stored value.
        if (_pos + 2 > _bufEnd)
            throw new EndOfStreamException();
        ushort crc = Crc16(_buf.AsSpan(frameStart, _pos - frameStart));
        int stored = (_buf[_pos] << 8) | _buf[_pos + 1];
        _pos += 2;
        if (crc != stored)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        switch (header.ChannelAssignment)
        {
            case 8:
                for (int i = 0; i < blockSize; i++)
                    _samples[1][i] = _samples[0][i] - _samples[1][i];
                break;
            case 9:
                for (int i = 0; i < blockSize; i++)
                    _samples[0][i] += _samples[1][i];
                break;
            case 10:
                for (int i = 0; i < blockSize; i++)
                {
                    int side = _samples[1][i];
                    int mid = (_samples[0][i] << 1) | (side & 1);
                    _samples[0][i] = (mid + side) >> 1;
                    _samples[1][i] = (mid - side) >> 1;
                }

                break;
        }
    }

    private void DecodeSubframe(int[] output, int blockSize, int bits)
    {
        if (ReadBits(1) != 0)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        int type = (int)ReadBits(6);
        int wasted = 0;
        if (ReadBits(1) != 0)
            wasted = ReadUnary() + 1;
        int effBits = bits - wasted;
        if (effBits <= 0 || effBits > 32)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        if (type == 0)
        {
            int v = ReadSigned(effBits);
            Array.Fill(output, v, 0, blockSize);
        }
        else if (type == 1)
        {
            for (int i = 0; i < blockSize; i++)
                output[i] = ReadSigned(effBits);
        }
        else if (type is >= 8 and <= 12)
        {
            int order = type - 8;
            if (order > blockSize)
                throw new InvalidDataException(Texts.Get("FlacInvalid"));
            for (int i = 0; i < order; i++)
                output[i] = ReadSigned(effBits);
            ReadResidual(blockSize, order);
            RestoreFixed(output, blockSize, order);
        }
        else if (type >= 32)
        {
            int order = type - 31;
            if (order > blockSize)
                throw new InvalidDataException(Texts.Get("FlacInvalid"));
            for (int i = 0; i < order; i++)
                output[i] = ReadSigned(effBits);

            int precision = (int)ReadBits(4) + 1;
            if (precision == 16)
                throw new InvalidDataException(Texts.Get("FlacInvalid"));
            int shift = ReadSigned(5);
            if (shift < 0)
                throw new InvalidDataException(Texts.Get("FlacInvalid"));

            Span<int> coef = stackalloc int[order];
            for (int i = 0; i < order; i++)
                coef[i] = ReadSigned(precision);

            ReadResidual(blockSize, order);
            RestoreLpc(output, blockSize, order, coef, shift);
        }
        else
        {
            throw new InvalidDataException(Texts.Get("FlacInvalid"));
        }

        if (wasted > 0)
        {
            for (int i = 0; i < blockSize; i++)
                output[i] <<= wasted;
        }
    }

    private void ReadResidual(int blockSize, int predictorOrder)
    {
        int method = (int)ReadBits(2);
        if (method > 1)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        int partitionOrder = (int)ReadBits(4);
        int partitions = 1 << partitionOrder;
        if ((blockSize >> partitionOrder) < predictorOrder || (blockSize & (partitions - 1)) != 0)
            throw new InvalidDataException(Texts.Get("FlacInvalid"));

        int paramBits = method == 0 ? 4 : 5;
        int escape = method == 0 ? 15 : 31;
        int[] residual = _residual;
        int index = predictorOrder;

        for (int p = 0; p < partitions; p++)
        {
            int rice = (int)ReadBits(paramBits);
            int count = (blockSize >> partitionOrder) - (p == 0 ? predictorOrder : 0);

            if (rice == escape)
            {
                int raw = (int)ReadBits(5);
                for (int i = 0; i < count; i++)
                    residual[index++] = raw == 0 ? 0 : ReadSigned(raw);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    uint q = (uint)ReadUnary();
                    uint r = rice > 0 ? ReadBits(rice) : 0;
                    uint u = (q << rice) | r;
                    residual[index++] = (int)(u >> 1) ^ -(int)(u & 1);
                }
            }
        }
    }

    private void RestoreFixed(int[] s, int n, int order)
    {
        int[] r = _residual;
        switch (order)
        {
            case 0:
                for (int i = 0; i < n; i++)
                    s[i] = r[i];
                break;
            case 1:
                for (int i = 1; i < n; i++)
                    s[i] = r[i] + s[i - 1];
                break;
            case 2:
                for (int i = 2; i < n; i++)
                    s[i] = r[i] + 2 * s[i - 1] - s[i - 2];
                break;
            case 3:
                for (int i = 3; i < n; i++)
                    s[i] = r[i] + 3 * s[i - 1] - 3 * s[i - 2] + s[i - 3];
                break;
            default:
                for (int i = 4; i < n; i++)
                    s[i] = r[i] + 4 * s[i - 1] - 6 * s[i - 2] + 4 * s[i - 3] - s[i - 4];
                break;
        }
    }

    private void RestoreLpc(int[] s, int n, int order, ReadOnlySpan<int> coef, int shift)
    {
        int[] r = _residual;
        for (int i = order; i < n; i++)
        {
            long sum = 0;
            for (int j = 0; j < order; j++)
                sum += (long)coef[j] * s[i - 1 - j];
            s[i] = r[i] + (int)(sum >> shift);
        }
    }

    private void EnsureSampleBuffers(int channels, int blockSize)
    {
        if (_samples.Length < channels)
            Array.Resize(ref _samples, channels);
        for (int c = 0; c < channels; c++)
        {
            if (_samples[c] is null || _samples[c].Length < blockSize)
                _samples[c] = new int[blockSize];
        }

        if (_residual.Length < blockSize)
            _residual = new int[blockSize];
    }

    private void InterleaveToPcm(int skip, int end)
    {
        int blockAlign = _waveFormat.BlockAlign;
        int needed = (end - skip) * blockAlign;
        if (_pcm.Length < needed)
            _pcm = new byte[needed];

        byte[] pcm = _pcm;
        int shift = _outShift;
        int o = 0;
        for (int i = skip; i < end; i++)
        {
            for (int c = 0; c < _channels; c++)
            {
                int v = _samples[c][i] << shift;
                pcm[o++] = (byte)v;
                pcm[o++] = (byte)(v >> 8);
                if (_outBytesPerSample == 3)
                    pcm[o++] = (byte)(v >> 16);
            }
        }

        _pcmOffset = 0;
        _pcmCount = o;
    }

    // ---------------------------------------------------------------- bit reader

    private uint ReadBits(int n)
    {
        uint value = 0;
        while (n > 0)
        {
            if (_pos >= _bufEnd)
                throw new EndOfStreamException();

            int free = 8 - _bit;
            int take = Math.Min(free, n);
            int chunk = (_buf[_pos] >> (free - take)) & ((1 << take) - 1);
            value = (value << take) | (uint)chunk;
            _bit += take;
            n -= take;
            if (_bit == 8)
            {
                _bit = 0;
                _pos++;
            }
        }

        return value;
    }

    private int ReadSigned(int n)
    {
        if (n == 0)
            return 0;
        uint v = ReadBits(n);
        return (int)(v << (32 - n)) >> (32 - n);
    }

    private int ReadUnary()
    {
        int count = 0;
        while (true)
        {
            if (_pos >= _bufEnd)
                throw new EndOfStreamException();

            int b = _buf[_pos] & (0xFF >> _bit);
            if (b == 0)
            {
                count += 8 - _bit;
                _bit = 0;
                _pos++;
                continue;
            }

            int lz = BitOperations.LeadingZeroCount((uint)b) - 24;
            count += lz - _bit;
            _bit = lz + 1;
            if (_bit == 8)
            {
                _bit = 0;
                _pos++;
            }

            return count;
        }
    }

    // ---------------------------------------------------------------- frame header

    private readonly struct FlacHeader
    {
        public FlacHeader(bool variable, int blockSize, int channelAssignment, int bitsPerSample, long number, int length)
        {
            IsVariableBlockSize = variable;
            BlockSize = blockSize;
            ChannelAssignment = channelAssignment;
            BitsPerSample = bitsPerSample;
            Number = number;
            Length = length;
        }

        public bool IsVariableBlockSize { get; }
        public int BlockSize { get; }
        public int ChannelAssignment { get; }
        public int BitsPerSample { get; }   // 0 = as in STREAMINFO
        public long Number { get; }
        public int Length { get; }
        public int ChannelCount => ChannelAssignment < 8 ? ChannelAssignment + 1 : 2;

        // The number is a sample number when the header says "variable" - or when STREAMINFO
        // declares different min/max block sizes (files from old encoders flag those as fixed).
        public long SampleNumber(int fixedBlockSize) =>
            IsVariableBlockSize || fixedBlockSize == 0 ? Number : Number * fixedBlockSize;

        public bool IsPlausible(int streamMaxBlockSize, int fixedBlockSize, long totalSamples)
        {
            if (BlockSize <= 0 || BlockSize > 65535)
                return false;
            if (streamMaxBlockSize > 0 && BlockSize > streamMaxBlockSize)
                return false;
            long first = SampleNumber(fixedBlockSize);
            return first >= 0 && first < totalSamples + 65536;
        }
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> s, out FlacHeader header)
    {
        header = default;
        if (s.Length < 6)
            return false;
        if (s[0] != 0xFF || (s[1] & 0xFE) != 0xF8)
            return false;

        bool variable = (s[1] & 1) != 0;
        int bsCode = s[2] >> 4;
        int srCode = s[2] & 0x0F;
        int chAssign = s[3] >> 4;
        int ssCode = (s[3] >> 1) & 7;
        if ((s[3] & 1) != 0 || bsCode == 0 || srCode == 15 || chAssign > 10 || ssCode == 3)
            return false;

        int i = 4;
        // UTF-8 style coded frame/sample number (up to 36 bits)
        int first = s[i++];
        int extra;
        long number;
        if ((first & 0x80) == 0) { extra = 0; number = first; }
        else if ((first & 0xE0) == 0xC0) { extra = 1; number = first & 0x1F; }
        else if ((first & 0xF0) == 0xE0) { extra = 2; number = first & 0x0F; }
        else if ((first & 0xF8) == 0xF0) { extra = 3; number = first & 0x07; }
        else if ((first & 0xFC) == 0xF8) { extra = 4; number = first & 0x03; }
        else if ((first & 0xFE) == 0xFC) { extra = 5; number = first & 0x01; }
        else if (first == 0xFE) { extra = 6; number = 0; }
        else return false;

        if (i + extra > s.Length)
            return false;
        for (int k = 0; k < extra; k++)
        {
            int c = s[i++];
            if ((c & 0xC0) != 0x80)
                return false;
            number = (number << 6) | (long)(c & 0x3F);
        }

        int blockSize;
        switch (bsCode)
        {
            case 1: blockSize = 192; break;
            case >= 2 and <= 5: blockSize = 576 << (bsCode - 2); break;
            case 6:
                if (i + 1 > s.Length) return false;
                blockSize = s[i++] + 1;
                break;
            case 7:
                if (i + 2 > s.Length) return false;
                blockSize = ((s[i] << 8) | s[i + 1]) + 1;
                i += 2;
                break;
            default: blockSize = 256 << (bsCode - 8); break;
        }

        i += srCode switch { 12 => 1, 13 => 2, 14 => 2, _ => 0 };
        if (i + 1 > s.Length)
            return false;

        if (Crc8(s[..i]) != s[i])
            return false;
        i++;

        int bits = ssCode switch { 0 => 0, 1 => 8, 2 => 12, 4 => 16, 5 => 20, 6 => 24, 7 => 32, _ => -1 };
        if (bits < 0)
            return false;

        header = new FlacHeader(variable, blockSize, chAssign, bits, number, i);
        return true;
    }

    private static readonly byte[] Crc8Table = BuildCrc8();
    private static readonly ushort[] Crc16Table = BuildCrc16();

    private static byte[] BuildCrc8()
    {
        var t = new byte[256];
        for (int n = 0; n < 256; n++)
        {
            int c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 0x80) != 0 ? (c << 1) ^ 0x07 : c << 1;
            t[n] = (byte)c;
        }

        return t;
    }

    private static ushort[] BuildCrc16()
    {
        var t = new ushort[256];
        for (int n = 0; n < 256; n++)
        {
            int c = n << 8;
            for (int k = 0; k < 8; k++)
                c = (c & 0x8000) != 0 ? (c << 1) ^ 0x8005 : c << 1;
            t[n] = (ushort)c;
        }

        return t;
    }

    private static byte Crc8(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte b in data)
            crc = Crc8Table[(crc ^ b) & 0xFF];
        return (byte)crc;
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte b in data)
            crc = ((crc << 8) ^ Crc16Table[((crc >> 8) ^ b) & 0xFF]) & 0xFFFF;
        return (ushort)crc;
    }
}
