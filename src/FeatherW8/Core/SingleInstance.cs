using System.Diagnostics;

namespace FeatherW8.Core;

/// <summary>
/// Only one FeatherW8 plays at a time. A newly started player asks the one that is running to quit,
/// waits until it has released the audio device, and then takes over - so opening another file
/// replaces the current playback instead of mixing two players on the same DAC.
/// <para>
/// The running player owns a named mutex and polls a named event; a newcomer sets the event and waits
/// for the mutex. When several players are started at once (Explorer launches one process per
/// selected file) they queue up and the last one to arrive keeps playing.
/// </para>
/// Keep this object alive (and on the same thread) for as long as the player runs: the mutex is
/// released in <see cref="Dispose"/>, which must happen after the audio output has been closed.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\FeatherW8.Player";
    private const string QuitEventName = @"Local\FeatherW8.Player.Quit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _quit;
    private readonly bool _owned;

    private SingleInstance(Mutex mutex, EventWaitHandle quit, bool owned)
    {
        _mutex = mutex;
        _quit = quit;
        _owned = owned;
    }

    /// <summary>True once another player has asked this one to make room.</summary>
    public bool QuitRequested => _quit.WaitOne(0);

    /// <summary>
    /// Becomes the one running player. Waits up to <paramref name="patience"/> for a previous player
    /// to shut down cleanly; if it is stuck, ends it.
    /// </summary>
    public static SingleInstance TakeOver(TimeSpan patience)
    {
        var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
        var mutex = new Mutex(false, MutexName);

        bool owned = Acquire(mutex, 0);
        if (!owned)
        {
            DateTime deadline = DateTime.UtcNow + patience;
            while (!owned && DateTime.UtcNow < deadline)
            {
                // Re-signal on every round: a newcomer that took over a moment ago resets the event,
                // and without this a player queued behind it would wait for nothing.
                quit.Set();
                owned = Acquire(mutex, 150);
            }

            if (!owned)
            {
                EndOtherPlayers(); // it did not react in time - do not leave two players on one DAC
                owned = Acquire(mutex, 3000);
            }
        }

        if (owned)
            quit.Reset(); // our own start must not be taken for somebody else's request

        return new SingleInstance(mutex, quit, owned);
    }

    public void Dispose()
    {
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        _quit.Dispose();
        _mutex.Dispose();
    }

    private static bool Acquire(Mutex mutex, int milliseconds)
    {
        try
        {
            return mutex.WaitOne(milliseconds);
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous player was killed or crashed; the mutex is ours now
        }
    }

    private static void EndOtherPlayers()
    {
        using Process me = Process.GetCurrentProcess();
        foreach (Process other in Process.GetProcessesByName(me.ProcessName))
        {
            try
            {
                if (other.Id != me.Id)
                {
                    other.Kill();
                    other.WaitForExit(2000);
                }
            }
            catch
            {
                // already gone, or not ours to end
            }
            finally
            {
                other.Dispose();
            }
        }
    }
}
