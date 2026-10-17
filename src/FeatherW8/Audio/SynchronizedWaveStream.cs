using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// Serializes decoder reads with UI-thread seek operations.
/// </summary>
internal sealed class SynchronizedWaveStream : WaveStream
{
    private readonly WaveStream _inner;
    private readonly object _gate = new();
    private bool _disposed;

    public SynchronizedWaveStream(WaveStream inner) => _inner = inner;

    /// <summary>The wrapped stream (the prefetch buffer, when there is one).</summary>
    public WaveStream Inner => _inner;

    /// <summary>The actual decoder behind any prefetch buffer; the output layer uses it to recognise DSD sources.</summary>
    public WaveStream Decoder => _inner is PrefetchWaveStream prefetch ? prefetch.Source : _inner;

    public override WaveFormat WaveFormat => _inner.WaveFormat;

    public override long Length
    {
        get
        {
            lock (_gate)
                return _inner.Length;
        }
    }

    public override long Position
    {
        get
        {
            lock (_gate)
                return _inner.Position;
        }
        set
        {
            lock (_gate)
                _inner.Position = Math.Clamp(value, 0, _inner.Length);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_gate)
            return _inner.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_gate)
            return _inner.Seek(offset, origin);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _inner.Dispose();
                    _disposed = true;
                }
            }
        }

        base.Dispose(disposing);
    }
}
