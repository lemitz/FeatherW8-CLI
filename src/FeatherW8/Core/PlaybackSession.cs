using FeatherW8.Audio;
using NAudio.Wave;

namespace FeatherW8.Core;

internal enum RepeatMode
{
    Off,
    Track,
    Queue
}

internal sealed class PlaybackSession : IDisposable
{
    private readonly SynchronizedWaveStream _source;
    private readonly AudioOutputSelection _selection;
    private readonly IWavePlayer _output;
    private int _ended;
    private int _disposing;
    private volatile Exception? _failure;
    private volatile bool _userPaused;

    public PlaybackSession(string path, bool forceShared = false, long initialPosition = 0)
    {
        WaveStream rawSource = AudioSourceFactory.Open(path);
        // Decode ahead on a worker thread so the real-time audio callback never waits for the disk.
        var prefetch = new PrefetchWaveStream(rawSource);
        var synchronizedSource = new SynchronizedWaveStream(prefetch);
        AudioOutputSelection? selection = null;

        try
        {
            if (initialPosition > 0)
            {
                int blockAlign = Math.Max(1, synchronizedSource.WaveFormat.BlockAlign);
                long target = Math.Clamp(initialPosition, 0, synchronizedSource.Length);
                target -= target % blockAlign;
                synchronizedSource.Position = target;
            }

            selection = AudioOutputFactory.Create(synchronizedSource, AppSettings.AudioOutput, forceShared);
            selection.Player.PlaybackStopped += OnPlaybackStopped;
            selection.Finished += OnFinished;
            prefetch.WaitUntilBuffered(0.5, 3000); // do not start on an empty buffer
            selection.Player.Play();
        }
        catch (Exception ex)
        {
            bool strictOutputWasSelected = selection?.IsStrict == true;
            bool offersShared = selection?.OffersShared ?? true;
            if (selection is not null)
            {
                try { selection.Player.PlaybackStopped -= OnPlaybackStopped; } catch { }
                try { selection.Dispose(); } catch { }
            }
            try { synchronizedSource.Dispose(); } catch { }

            if (strictOutputWasSelected && ex is not AudioOutputUnavailableException)
                throw new AudioOutputUnavailableException(Texts.Get("AudioModeDeviceUnavailable"), ex, offersShared);
            throw;
        }

        _source = synchronizedSource;
        _selection = selection;
        _output = selection.Player;
    }

    public bool Ended => Volatile.Read(ref _ended) != 0;
    public Exception? Failure => _failure;
    public bool IsStrictOutput => _selection.IsStrict;
    public bool OffersShared => _selection.OffersShared;
    public string OutputLabel => _selection.Label;
    public OutputKind OutputKind => _selection.Kind;
    public string? OutputNote => _selection.Note;
    // The pause state is tracked here: NAudio's AsioOut reports a paused stream as Stopped after a moment.
    public bool IsPaused => _userPaused;
    public bool IsPlaying => !_userPaused && _output.PlaybackState == PlaybackState.Playing;
    public long Position => _source.Position;
    public long Length => _source.Length;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public TimeSpan CurrentTime => BytesToTime(Position);
    public TimeSpan Duration => BytesToTime(Length);

    public void TogglePause()
    {
        if (_userPaused)
        {
            _output.Play();
            _userPaused = false;
        }
        else if (_output.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
            _userPaused = true;
        }
    }

    public void SeekBy(TimeSpan delta)
    {
        long bytesPerSecond = _source.WaveFormat.AverageBytesPerSecond;
        if (bytesPerSecond <= 0)
            return;

        long shift = (long)(delta.TotalSeconds * bytesPerSecond);
        long target = Math.Clamp(_source.Position + shift, 0, _source.Length);
        int blockAlign = Math.Max(1, _source.WaveFormat.BlockAlign);
        target -= target % blockAlign;
        _source.Position = target;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0)
            return;

        _output.PlaybackStopped -= OnPlaybackStopped;
        try
        {
            _output.Stop();
        }
        catch
        {
            // Stop can race with a device removal; Dispose still releases the native handle.
        }
        finally
        {
            try
            {
                _selection.Dispose();
            }
            catch
            {
                // Device removal may make native cleanup fail; still release the source stream.
            }
            finally
            {
                _source.Dispose();
            }
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        // The provider delivered the last sample and a silent tail: the track is over.
        if (Volatile.Read(ref _disposing) == 0)
            Interlocked.Exchange(ref _ended, 1);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _failure = e.Exception;
        else if (_userPaused)
            return; // a driver that stops itself while paused has not reached the end of the track

        if (Volatile.Read(ref _disposing) == 0)
            Interlocked.Exchange(ref _ended, 1);
    }

    private TimeSpan BytesToTime(long bytes)
    {
        int averageBytesPerSecond = _source.WaveFormat.AverageBytesPerSecond;
        return averageBytesPerSecond > 0
            ? TimeSpan.FromSeconds((double)bytes / averageBytesPerSecond)
            : TimeSpan.Zero;
    }
}
