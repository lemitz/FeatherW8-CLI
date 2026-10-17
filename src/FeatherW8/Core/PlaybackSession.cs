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

    public PlaybackSession(string path, bool forceShared = false, long initialPosition = 0)
    {
        WaveStream rawSource = AudioSourceFactory.Open(path);
        var synchronizedSource = new SynchronizedWaveStream(rawSource);
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
            selection.Player.Play();
        }
        catch (Exception ex)
        {
            bool strictOutputWasSelected = selection?.IsStrict == true;
            if (selection is not null)
            {
                try { selection.Player.PlaybackStopped -= OnPlaybackStopped; } catch { }
                try { selection.Dispose(); } catch { }
            }
            try { synchronizedSource.Dispose(); } catch { }

            if (strictOutputWasSelected && ex is not AudioOutputUnavailableException)
                throw new AudioOutputUnavailableException(Texts.Get("AudioModeDeviceUnavailable"), ex);
            throw;
        }

        _source = synchronizedSource;
        _selection = selection;
        _output = selection.Player;
    }

    public bool Ended => Volatile.Read(ref _ended) != 0;
    public Exception? Failure => _failure;
    public bool IsStrictOutput => _selection.IsStrict;
    public string OutputLabel => _selection.Label;
    public string? OutputNote => _selection.Note;
    public bool IsPaused => _output.PlaybackState == PlaybackState.Paused;
    public bool IsPlaying => _output.PlaybackState == PlaybackState.Playing;
    public long Position => _source.Position;
    public long Length => _source.Length;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public TimeSpan CurrentTime => BytesToTime(Position);
    public TimeSpan Duration => BytesToTime(Length);

    public void TogglePause()
    {
        if (_output.PlaybackState == PlaybackState.Playing)
            _output.Pause();
        else if (_output.PlaybackState == PlaybackState.Paused)
            _output.Play();
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

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _failure = e.Exception;

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
