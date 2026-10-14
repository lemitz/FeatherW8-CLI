using Concentus.Oggfile;
using FeatherW8.Core;
using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// Minimal seekable PCM wrapper for mono/stereo Opus streams in an Ogg container.
/// Seeking restarts decoding from the beginning, then discards samples up to the target.
/// That keeps the first version small and avoids relying on container-specific seek indexes.
/// </summary>
internal sealed class OggOpusWaveStream : WaveStream
{
    private const int OutputSampleRate = 48_000;
    private readonly string _path;
    private readonly int _channels;
    private readonly WaveFormat _waveFormat;

    private FileStream? _file;
    private OpusOggReadStream? _reader;
    private short[]? _packet;
    private int _packetSampleOffset;
    private long _position;
    private long _length;
    private bool _disposed;

    public OggOpusWaveStream(string path)
    {
        _path = Path.GetFullPath(path);
        _channels = ReadChannelCount(_path);
        if (_channels is not (1 or 2))
            throw new NotSupportedException(Texts.Format("OpusChannels", _channels));

        _waveFormat = new WaveFormat(OutputSampleRate, 16, _channels);
        OpenFromStart();
    }

    public override WaveFormat WaveFormat => _waveFormat;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        long requested = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        long target = Math.Clamp(requested, 0, _length);
        target -= target % _waveFormat.BlockAlign;
        ReopenAndDecodeTo(target);
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException(Texts.Get("BufferRangeInvalid"));

        int alignedCount = count - count % _waveFormat.BlockAlign;
        int written = 0;

        while (written < alignedCount)
        {
            if (_packet is null || _packetSampleOffset >= _packet.Length)
            {
                _packet = _reader!.DecodeNextPacket();
                _packetSampleOffset = 0;
                if (_packet is null || _packet.Length == 0)
                {
                    break;
                }
            }

            int samplesAvailable = _packet.Length - _packetSampleOffset;
            int samplesToCopy = Math.Min(samplesAvailable, (alignedCount - written) / sizeof(short));
            int bytesToCopy = samplesToCopy * sizeof(short);
            Buffer.BlockCopy(_packet, _packetSampleOffset * sizeof(short), buffer, offset + written, bytesToCopy);
            _packetSampleOffset += samplesToCopy;
            written += bytesToCopy;
        }

        _position = Math.Min(_length, _position + written);
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            CloseReader();
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    public static bool IsOpusFile(string path)
    {
        byte[] firstPacket = ReadFirstOggPacket(path);
        return firstPacket.Length >= 8 &&
               firstPacket[0] == (byte)'O' && firstPacket[1] == (byte)'p' &&
               firstPacket[2] == (byte)'u' && firstPacket[3] == (byte)'s' &&
               firstPacket[4] == (byte)'H' && firstPacket[5] == (byte)'e' &&
               firstPacket[6] == (byte)'a' && firstPacket[7] == (byte)'d';
    }

    private static int ReadChannelCount(string path)
    {
        byte[] packet = ReadFirstOggPacket(path);
        if (packet.Length < 10 || !IsOpusHead(packet))
            throw new InvalidDataException(Texts.Get("OpusHeadMissing"));
        return packet[9];
    }

    private static bool IsOpusHead(byte[] packet) =>
        packet.Length >= 8 &&
        packet[0] == (byte)'O' && packet[1] == (byte)'p' &&
        packet[2] == (byte)'u' && packet[3] == (byte)'s' &&
        packet[4] == (byte)'H' && packet[5] == (byte)'e' &&
        packet[6] == (byte)'a' && packet[7] == (byte)'d';

    private static byte[] ReadFirstOggPacket(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var packet = new MemoryStream();
        byte[] pageHeader = new byte[27];

        while (true)
        {
            ReadExactly(input, pageHeader);
            if (pageHeader[0] != (byte)'O' || pageHeader[1] != (byte)'g' ||
                pageHeader[2] != (byte)'g' || pageHeader[3] != (byte)'S')
            {
                throw new InvalidDataException(Texts.Get("InvalidOgg"));
            }

            int segmentCount = pageHeader[26];
            byte[] lacing = new byte[segmentCount];
            ReadExactly(input, lacing);

            foreach (byte segmentLength in lacing)
            {
                if (segmentLength != 0)
                {
                    byte[] segment = new byte[segmentLength];
                    ReadExactly(input, segment);
                    packet.Write(segment, 0, segment.Length);
                }

                if (packet.Length > 1_048_576)
                    throw new InvalidDataException(Texts.Get("OggHeaderTooLarge"));

                if (segmentLength < 255)
                    return packet.ToArray();
            }
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int amount = stream.Read(buffer, read, buffer.Length - read);
            if (amount == 0)
                throw new EndOfStreamException(Texts.Get("TruncatedOgg"));
            read += amount;
        }
    }

    private void OpenFromStart()
    {
        _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = Concentus.OpusCodecFactory.CreateDecoder(OutputSampleRate, _channels, messageLogger: null);
        _reader = new OpusOggReadStream(decoder, _file);
        if (!string.IsNullOrWhiteSpace(_reader.LastError))
            throw new InvalidDataException(_reader.LastError);

        _length = Math.Max(0, (long)Math.Round(_reader.TotalTime.TotalSeconds * _waveFormat.AverageBytesPerSecond));
        _position = 0;
        _packet = null;
        _packetSampleOffset = 0;
    }

    private void ReopenAndDecodeTo(long targetBytes)
    {
        CloseReader();
        OpenFromStart();

        long targetFrames = targetBytes / _waveFormat.BlockAlign;
        long decodedFrames = 0;

        while (decodedFrames < targetFrames)
        {
            short[]? next = _reader!.DecodeNextPacket();
            if (next is null || next.Length == 0)
            {
                _position = decodedFrames * _waveFormat.BlockAlign;
                return;
            }

            int framesInPacket = next.Length / _channels;
            long packetEnd = decodedFrames + framesInPacket;
            if (packetEnd > targetFrames)
            {
                _packet = next;
                _packetSampleOffset = checked((int)(targetFrames - decodedFrames) * _channels);
                decodedFrames = targetFrames;
                break;
            }

            decodedFrames = packetEnd;
        }

        _position = targetBytes;
    }

    private void CloseReader()
    {
        try
        {
            _reader?.Close();
        }
        finally
        {
            _reader = null;
            _file?.Dispose();
            _file = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
