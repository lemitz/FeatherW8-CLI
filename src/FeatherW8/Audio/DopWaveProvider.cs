using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// DSD over PCM (DoP 1.1): 16 DSD samples per channel are packed into one 24-bit PCM sample whose top
/// byte is an alternating marker (0x05 / 0xFA). A DoP-aware DAC recognises the markers and plays the
/// payload as native DSD; the PCM path in between (ASIO or WASAPI Exclusive) must be bit-perfect.
/// The PCM rate is the DSD rate / 16: 176.4 kHz for DSD64, 352.8 kHz for DSD128, 705.6 kHz for DSD256.
/// </summary>
internal sealed class DopWaveProvider : IWaveProvider, ISilenceSource
{
    private const byte MarkerA = 0x05;
    private const byte MarkerB = 0xFA;

    private readonly WaveStream _dsd;
    private readonly int _channels;
    private byte[] _scratch = new byte[4096];
    private bool _nextIsMarkerA = true;
    private long _expectedPosition;

    public DopWaveProvider(WaveStream dsd, int dsdSampleRate, int channels)
    {
        _dsd = dsd;
        _channels = channels;
        WaveFormat = new WaveFormat(dsdSampleRate / 16, 24, channels);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// DoP idle: valid DoP frames carrying DSD silence, with the marker sequence continuing. After the last
    /// sample the DAC stays in DSD mode instead of seeing markerless zeros (which would click).
    /// </summary>
    public void WriteSilence(Span<byte> buffer)
    {
        int frames = buffer.Length / (3 * _channels);
        int o = 0;
        for (int f = 0; f < frames; f++)
        {
            byte marker = _nextIsMarkerA ? MarkerA : MarkerB;
            _nextIsMarkerA = !_nextIsMarkerA;
            for (int c = 0; c < _channels; c++)
            {
                buffer[o++] = DsdWaveStream.SilenceByte;
                buffer[o++] = DsdWaveStream.SilenceByte;
                buffer[o++] = marker;
            }
        }

        buffer[o..].Clear();
    }

    public int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public int Read(Span<byte> buffer)
    {
        int ch = _channels;
        int frames = buffer.Length / (3 * ch);
        if (frames <= 0)
            return 0;

        // A DoP frame consumes two DSD bytes per channel.
        int dsdBytes = frames * 2 * ch;
        if (_scratch.Length < dsdBytes)
            _scratch = new byte[dsdBytes];

        long start = _dsd.Position;
        if (start != _expectedPosition)
            _nextIsMarkerA = true; // the position jumped (seek): restart the marker sequence

        int got = 0;
        while (got < dsdBytes)
        {
            int n = _dsd.Read(_scratch, got, dsdBytes - got);
            if (n <= 0)
                break;
            got += n;
        }

        _expectedPosition = start + got;
        if (got == 0)
            return 0;

        // A trailing odd DSD byte is completed with DSD silence so no audio is dropped.
        int usableFrames = (got + 2 * ch - 1) / (2 * ch);
        for (int i = got; i < usableFrames * 2 * ch; i++)
            _scratch[i] = DsdWaveStream.SilenceByte;

        int o = 0;
        int s = 0;
        for (int f = 0; f < usableFrames; f++)
        {
            byte marker = _nextIsMarkerA ? MarkerA : MarkerB;
            _nextIsMarkerA = !_nextIsMarkerA;

            // channel-interleaved input: [c0 b1][c1 b1][c0 b2][c1 b2]
            for (int c = 0; c < ch; c++)
            {
                byte earlier = _scratch[s + c];
                byte later = _scratch[s + ch + c];
                buffer[o++] = later;   // bits 7..0
                buffer[o++] = earlier; // bits 15..8
                buffer[o++] = marker;  // bits 23..16
            }

            s += 2 * ch;
        }

        return usableFrames * 3 * ch;
    }
}
