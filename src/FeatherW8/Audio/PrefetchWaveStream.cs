using FeatherW8.Core;
using NAudio.Wave;

namespace FeatherW8.Audio;

/// <summary>
/// Decodes ahead of the audio device. A dedicated thread reads the wrapped decoder (disk I/O included)
/// into a ring buffer worth a few seconds of audio; <see cref="Read"/> - which runs inside the
/// real-time callback of ASIO / WASAPI - only copies bytes that are already there.
/// <para>
/// Without this, every refill of a decoder's file buffer happened inside the callback. On a spinning
/// disk, or with anything else touching it, such a read can take longer than one device buffer
/// (about 12 ms) and the music stutters.
/// </para>
/// <para>
/// The wrapped stream is only ever touched by the worker thread (and by <see cref="Dispose"/> after the
/// worker has stopped). Position is the logical position of the consumer, so seeking, pausing and
/// the time display behave exactly as before. A seek discards the buffered audio and the worker
/// repositions the decoder; <see cref="Read"/> waits for the new data.
/// </para>
/// </summary>
internal sealed class PrefetchWaveStream : WaveStream
{
    private const double BufferSeconds = 4.0;
    private const double ChunkSeconds = 0.125;

    private readonly WaveStream _source;
    private readonly object _gate = new();
    private readonly byte[] _ring;
    private readonly int _chunk;
    private readonly int _blockAlign;
    private readonly long _length;
    private readonly Thread _worker;

    private int _head;            // index of the oldest buffered byte
    private int _count;           // buffered bytes
    private long _position;       // bytes handed to the consumer (the logical position)
    private long _generation;     // bumped by every seek; stale chunks from the worker are dropped
    private long _seekTarget = -1;
    private bool _eof;
    private bool _disposed;
    private Exception? _error;

    public PrefetchWaveStream(WaveStream source)
    {
        _source = source;
        _blockAlign = Math.Max(1, source.WaveFormat.BlockAlign);
        _length = source.Length;

        int bytesPerSecond = Math.Max(_blockAlign, source.WaveFormat.AverageBytesPerSecond);
        _chunk = Align((int)Math.Clamp(bytesPerSecond * ChunkSeconds, 4096, 1 << 20));
        int capacity = (int)Math.Clamp(bytesPerSecond * BufferSeconds, 4 * _chunk, 32 << 20);
        _ring = new byte[Align(capacity)];

        _worker = new Thread(Run) { IsBackground = true, Name = "FeatherW8 prefetch" };
        _worker.Start();
    }

    /// <summary>The decoder being prefetched.</summary>
    public WaveStream Source => _source;

    public override WaveFormat WaveFormat => _source.WaveFormat;
    public override long Length => _length;

    public override long Position
    {
        get
        {
            lock (_gate)
                return _position;
        }
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    /// Waits until about <paramref name="seconds"/> of audio is buffered (or the end of the stream is
    /// reached) so playback does not start on an empty buffer. Never waits longer than the timeout.
    /// </summary>
    public void WaitUntilBuffered(double seconds, int timeoutMs)
    {
        int wanted = (int)Math.Min(_ring.Length / 2, Math.Max(_chunk, WaveFormat.AverageBytesPerSecond * seconds));
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        lock (_gate)
        {
            while (_count < wanted && !_eof && !_disposed)
            {
                double left = (deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0)
                    return;
                Monitor.Wait(_gate, (int)Math.Min(left, 100));
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            long requested = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            long target = Math.Clamp(requested, 0, _length);
            target -= target % _blockAlign;

            _head = 0;
            _count = 0;
            _eof = false;
            _error = null;
            _position = target;
            _seekTarget = target;
            _generation++;
            Monitor.PulseAll(_gate);
            return _position;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
            throw new ArgumentException(Texts.Get("BufferRangeInvalid"));

        int want = count - count % _blockAlign;
        if (want == 0)
            return 0;

        lock (_gate)
        {
            // The worker keeps the buffer full, so this normally returns at once. It only waits if the
            // source cannot keep up (or right after a seek, until the decoder has repositioned).
            while (_count < want && !_eof && !_disposed)
                Monitor.Wait(_gate, 100);

            if (_disposed)
                return 0;

            int n = Math.Min(want, _count);
            n -= n % _blockAlign;

            if (n == 0 && _error is not null)
            {
                Exception error = _error;
                _error = null;
                throw new IOException(error.Message, error);
            }

            int first = Math.Min(n, _ring.Length - _head);
            Buffer.BlockCopy(_ring, _head, buffer, offset, first);
            if (n > first)
                Buffer.BlockCopy(_ring, 0, buffer, offset + first, n - first);

            _head = (_head + n) % _ring.Length;
            _count -= n;
            _position += n;
            Monitor.PulseAll(_gate); // room for the worker
            return n;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    base.Dispose(disposing);
                    return;
                }

                _disposed = true;
                Monitor.PulseAll(_gate);
            }

            // Let the worker finish its current read before the decoder goes away.
            if (!_worker.Join(3000))
            {
                // A read is stuck (a dead disk, say). Leave the decoder to the finaliser rather than
                // closing it under the worker's feet.
                base.Dispose(disposing);
                return;
            }

            _source.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ worker

    private void Run()
    {
        byte[] chunk = new byte[_chunk];
        try
        {
            while (true)
            {
                long generation;
                long seek;
                lock (_gate)
                {
                    // Sleep while the buffer is full (or the stream ended) and nothing was asked for.
                    while (!_disposed && _seekTarget < 0 && (_eof || _ring.Length - _count < _chunk))
                        Monitor.Wait(_gate);

                    if (_disposed)
                        return;

                    generation = _generation;
                    seek = _seekTarget;
                    _seekTarget = -1;
                }

                // All decoder access happens here, outside the lock: it may take as long as the disk needs.
                if (seek >= 0)
                    _source.Position = seek;

                int n = _source.Read(chunk, 0, chunk.Length);

                lock (_gate)
                {
                    if (_disposed)
                        return;
                    if (generation != _generation)
                        continue; // a seek arrived meanwhile: this data is from the old position

                    if (n <= 0)
                    {
                        _eof = true;
                    }
                    else
                    {
                        int tail = (_head + _count) % _ring.Length;
                        int first = Math.Min(n, _ring.Length - tail);
                        Buffer.BlockCopy(chunk, 0, _ring, tail, first);
                        if (n > first)
                            Buffer.BlockCopy(chunk, first, _ring, 0, n - first);
                        _count += n;
                    }

                    Monitor.PulseAll(_gate);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _error = ex;
                    _eof = true;
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    private int Align(int bytes) => bytes - bytes % _blockAlign;
}
