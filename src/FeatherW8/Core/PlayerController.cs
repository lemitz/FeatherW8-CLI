namespace FeatherW8.Core;

internal enum MessageLevel
{
    Info,
    Warning,
    Error
}

internal sealed class PlayerController : IDisposable
{
    private readonly IReadOnlyList<string> _tracks;
    private PlaybackSession? _session;
    private bool _sharedAcceptedForQueue;
    private bool _awaitingFallback;
    private bool _fallbackOffersShared = true;
    private long _fallbackPosition;

    public PlayerController(IReadOnlyList<string> tracks)
    {
        if (tracks.Count == 0)
            throw new ArgumentException(Texts.Get("QueueEmpty"), nameof(tracks));

        _tracks = tracks;
    }

    public int CurrentIndex { get; private set; }
    public string CurrentPath => _tracks[CurrentIndex];
    public int TrackCount => _tracks.Count;
    public RepeatMode Repeat { get; private set; }
    public bool ExitRequested { get; private set; }
    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            _message = value;
            MessageLevel = MessageLevel.Info;
        }
    }

    /// <summary>How the UI should colour <see cref="Message"/>.</summary>
    public MessageLevel MessageLevel { get; private set; }
    public string? Prompt { get; private set; }
    public string OutputLabel { get; private set; } = string.Empty;
    public OutputKind OutputKind { get; private set; }
    public bool AwaitingFallback => _awaitingFallback;
    public bool IsPlaying => _session?.IsPlaying ?? false;
    public bool IsPaused => _session?.IsPaused ?? false;
    public TimeSpan CurrentTime => _session?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _session?.Duration ?? TimeSpan.Zero;

    public void Start() => PlayAt(0);

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_awaitingFallback)
        {
            HandleFallbackKey(key);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                PreviousTrack();
                break;
            case ConsoleKey.DownArrow:
                NextTrack(manual: true);
                break;
            case ConsoleKey.LeftArrow:
                Seek(-SeekSeconds(key));
                break;
            case ConsoleKey.RightArrow:
                Seek(SeekSeconds(key));
                break;
            case ConsoleKey.Spacebar:
                _session?.TogglePause();
                break;
            case ConsoleKey.C:
                Repeat = (RepeatMode)(((int)Repeat + 1) % 3);
                Message = Texts.Format("RepeatMessage", RepeatLabel(Repeat));
                break;
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                ExitRequested = true;
                break;
        }
    }

    public void Update()
    {
        if (_awaitingFallback || _session is null || !_session.Ended)
            return;

        if (_session.Failure is Exception failure)
        {
            if (_session.IsStrictOutput && (!_sharedAcceptedForQueue || !_session.OffersShared))
            {
                bool offersShared = _session.OffersShared;
                _fallbackPosition = _session.Position;
                StopCurrent();
                BeginFallbackPrompt(Texts.Format("AudioModeStrictUnavailable", failure.Message), offersShared);
                return;
            }

            Fail(Texts.Format("PlaybackError", failure.Message));
            StopCurrent();
            return;
        }

        if (Repeat == RepeatMode.Track)
        {
            PlayAt(CurrentIndex);
            return;
        }

        if (CurrentIndex + 1 < _tracks.Count)
        {
            PlayAt(CurrentIndex + 1);
            return;
        }

        if (Repeat == RepeatMode.Queue)
        {
            PlayAt(0);
            return;
        }

        StopCurrent();
        Message = Texts.Get("QueueEnded");
    }

    public void Dispose() => StopCurrent();

    private void HandleFallbackKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter && _fallbackOffersShared)
        {
            _sharedAcceptedForQueue = true;
            _awaitingFallback = false;
            Prompt = null;
            string acceptedMessage = Texts.Get("AudioModeFallbackEnabled");
            PlayAt(CurrentIndex, _fallbackPosition, forceShared: true, messageAfterStart: acceptedMessage);
            return;
        }

        if (key.Key == ConsoleKey.N)
        {
            _awaitingFallback = false;
            Prompt = null;
            SkipCurrentTrack();
            return;
        }

        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
            ExitRequested = true;
    }

    private void PreviousTrack()
    {
        if (_tracks.Count == 1)
        {
            PlayAt(0);
            return;
        }

        if (CurrentIndex > 0)
            PlayAt(CurrentIndex - 1);
        else if (Repeat == RepeatMode.Queue)
            PlayAt(_tracks.Count - 1);
        else
            PlayAt(0);
    }

    private void NextTrack(bool manual)
    {
        if (CurrentIndex + 1 < _tracks.Count)
        {
            PlayAt(CurrentIndex + 1);
            return;
        }

        if (Repeat == RepeatMode.Queue)
        {
            PlayAt(0);
            return;
        }

        if (_tracks.Count == 1 && Repeat == RepeatMode.Track)
        {
            PlayAt(0);
            return;
        }

        StopCurrent();
        Message = Texts.Get(manual ? "LastTrack" : "QueueEndedShort");
    }

    private void Seek(int seconds)
    {
        if (_session is null)
            return;

        try
        {
            _session.SeekBy(TimeSpan.FromSeconds(seconds));
            Message = null;
        }
        catch (Exception ex)
        {
            Fail(Texts.Format("SeekError", ex.Message));
        }
    }

    private void PlayAt(
        int index,
        long initialPosition = 0,
        bool forceShared = false,
        string? messageAfterStart = null)
    {
        StopCurrent();
        _awaitingFallback = false;
        Prompt = null;
        OutputLabel = string.Empty;
        OutputKind = OutputKind.None;
        CurrentIndex = Math.Clamp(index, 0, _tracks.Count - 1);
        Message = null;

        try
        {
            bool useShared = forceShared || _sharedAcceptedForQueue;
            _session = new PlaybackSession(CurrentPath, useShared, initialPosition);
            OutputLabel = _session.OutputLabel;
            OutputKind = _session.OutputKind;
            if (!_session.IsStrictOutput)
                Warn(messageAfterStart ?? Texts.Get("AudioModeSharedActive"));
            else
            {
                Message = messageAfterStart;
                if (messageAfterStart is null && _session.OutputNote is string note)
                    Warn(note); // e.g. why ASIO could not start and WASAPI Exclusive is used instead
            }
        }
        catch (AudioOutputUnavailableException ex)
        {
            if (!ex.OffersShared || (AppSettings.AudioOutput != AudioOutputPreference.Shared && !_sharedAcceptedForQueue))
            {
                BeginFallbackPrompt(Texts.Format("AudioModeStrictUnavailable", ex.Message), ex.OffersShared);
            }
            else
            {
                Fail(Texts.Format("OpenError", Path.GetFileName(CurrentPath), ex.Message));
            }
        }
        catch (Exception ex)
        {
            Fail(Texts.Format("OpenError", Path.GetFileName(CurrentPath), ex.Message));
        }
    }

    private void Warn(string text)
    {
        Message = text;
        MessageLevel = MessageLevel.Warning;
    }

    private void Fail(string text)
    {
        Message = text;
        MessageLevel = MessageLevel.Error;
    }

    private void BeginFallbackPrompt(string reason, bool offersShared = true)
    {
        _awaitingFallback = true;
        _fallbackOffersShared = offersShared;
        Fail(reason);
        Prompt = Texts.Get(offersShared ? "AudioModeFallbackPrompt" : "AudioModeFallbackPromptNoShared");
    }

    private void SkipCurrentTrack()
    {
        if (CurrentIndex + 1 < _tracks.Count)
        {
            PlayAt(CurrentIndex + 1);
            return;
        }

        if (Repeat == RepeatMode.Queue)
        {
            PlayAt(0);
            return;
        }

        StopCurrent();
        Warn(Texts.Get("AudioModeTrackSkipped"));
    }

    private void StopCurrent()
    {
        PlaybackSession? old = _session;
        _session = null;
        old?.Dispose();
    }

    private static int SeekSeconds(ConsoleKeyInfo key) =>
        key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? 30 : 5;

    public static string RepeatLabel(RepeatMode mode) => mode switch
    {
        RepeatMode.Track => Texts.Get("RepeatTrack"),
        RepeatMode.Queue => Texts.Get("RepeatQueue"),
        _ => Texts.Get("RepeatOff")
    };
}
