using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>Writes the "silence" of a particular stream (DoP idle frames, for instance) instead of zeros.</summary>
internal interface ISilenceSource
{
    void WriteSilence(Span<byte> buffer);
}

/// <summary>
/// Gives the output device what it always wants - a full buffer on every read - and finds the end of
/// the track by itself.
/// <para>
/// When the decoder runs out, the rest of the buffer and a short tail (0.3 s, longer than any device
/// buffer here) are filled with silence; once the tail has been handed over, <see cref="Finished"/> is
/// raised so the player can move on to the next track.
/// </para>
/// <para>
/// Why not rely on the output class: NAudio's ASIO output never stops by itself at the end of a stream
/// (the queue froze on the last second of every track), and WASAPI Exclusive rejects the final, partly
/// filled buffer with "A buffer size error occurred".
/// </para>
/// </summary>
internal sealed class TailPaddedProvider : IWaveProvider
{
    private const double TailSeconds = 0.3;

    private readonly IWaveProvider _inner;
    private readonly ISilenceSource? _silence;
    private readonly byte _silenceByte;
    private readonly long _tailBytes;
    private long _silentBytes;
    private int _finished;

    public TailPaddedProvider(IWaveProvider inner)
    {
        _inner = inner;
        _silence = inner as ISilenceSource;

        WaveFormat format = inner.WaveFormat;
        // 8-bit PCM is unsigned: its silence is 0x80, and zeros would be a full-scale click.
        _silenceByte = format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 8 ? (byte)0x80 : (byte)0;

        int block = Math.Max(1, format.BlockAlign);
        long tail = (long)(format.AverageBytesPerSecond * TailSeconds);
        _tailBytes = Math.Max(block, tail - tail % block);
    }

    /// <summary>Raised on the audio thread, once, after the last real sample and the silent tail have been delivered.</summary>
    public event EventHandler? Finished;

    public WaveFormat WaveFormat => _inner.WaveFormat;

    public int Read(Span<byte> buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int n = _inner.Read(buffer[filled..]);
            if (n <= 0)
                break;
            filled += n;
        }

        if (filled > 0)
        {
            // Real audio again (e.g. the user sought back after the end): the track is not over.
            _silentBytes = 0;
            Volatile.Write(ref _finished, 0);
        }

        if (filled < buffer.Length)
        {
            Span<byte> rest = buffer[filled..];
            if (_silence is not null)
                _silence.WriteSilence(rest);
            else
                rest.Fill(_silenceByte);

            _silentBytes += rest.Length;
            if (_silentBytes >= _tailBytes && Interlocked.Exchange(ref _finished, 1) == 0)
                Finished?.Invoke(this, EventArgs.Empty);
        }

        return buffer.Length;
    }
}
