using FeatherW8.Core;
using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// Presents one CUE track as a seekable stream. All positions exposed to the player are relative
/// to INDEX 01, so progress, seeking, repeat and end-of-track detection stay inside the track.
/// The underlying audio is never resampled or copied to a temporary file.
/// </summary>
internal sealed class CueRangeWaveStream : WaveStream
{
    private readonly WaveStream _source;
    private readonly long _start;
    private readonly long _length;

    public CueRangeWaveStream(WaveStream source, CueRange range)
    {
        _source = source;
        int blockAlign = source.WaveFormat.BlockAlign;
        long totalSamples = source.Length / blockAlign;
        long startSample = checked(range.StartFrames * (long)source.WaveFormat.SampleRate / 75);
        long endSample = range.EndFrames is long endFrames
            ? checked(endFrames * (long)source.WaveFormat.SampleRate / 75)
            : totalSamples;

        startSample = Math.Clamp(startSample, 0, totalSamples);
        endSample = Math.Clamp(endSample, 0, totalSamples);
        if (endSample <= startSample)
            throw new InvalidDataException(Texts.Get("CueRangeEmpty"));

        _start = startSample * blockAlign;
        _length = (endSample - startSample) * blockAlign;
        if (_start != 0)
            _source.Position = _start;
    }

    public override WaveFormat WaveFormat => _source.WaveFormat;
    public override long Length => _length;

    public override long Position
    {
        get => Math.Clamp(_source.Position - _start, 0, _length);
        set => _source.Position = _start + Math.Clamp(value, 0, _length) / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException(Texts.Get("BufferRangeInvalid"));

        int wanted = (int)Math.Min(count, _length - Position);
        wanted -= wanted % WaveFormat.BlockAlign;
        return wanted > 0 ? _source.Read(buffer, offset, wanted) : 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _source.Dispose();
        base.Dispose(disposing);
    }
}
