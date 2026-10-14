using FeatherW8.Core;

namespace FeatherW8.UI;

internal static class TerminalUi
{
    public static void Draw(PlayerController player)
    {
        int width;
        try
        {
            width = Math.Max(40, Console.WindowWidth - 1);
            Console.SetCursorPosition(0, 0);
        }
        catch
        {
            return;
        }

        var lines = new List<string>
        {
            Texts.Get("UiHeader"),
            Texts.Format("TrackLine", player.CurrentIndex + 1, player.TrackCount, Path.GetFileName(player.CurrentPath)),
            BuildProgress(player, width),
            Texts.Format("StatusLine", StatusLabel(player), PlayerController.RepeatLabel(player.Repeat)),
            string.IsNullOrWhiteSpace(player.OutputLabel) ? string.Empty : Texts.Format("AudioModeSelected", player.OutputLabel),
            Texts.Get("Controls"),
            player.Message ?? string.Empty,
            player.Prompt ?? string.Empty
        };

        foreach (string line in lines)
            WriteRow(line, width);
    }

    private static string BuildProgress(PlayerController player, int terminalWidth)
    {
        string timeLabel = $"{FormatTime(player.CurrentTime)} / {FormatTime(player.Duration)}";
        // Let the bar occupy the whole row, leaving room for brackets, a separator and the time label.
        int barWidth = Math.Max(8, terminalWidth - timeLabel.Length - 3);
        double durationSeconds = player.Duration.TotalSeconds;
        double currentSeconds = player.CurrentTime.TotalSeconds;
        double ratio = durationSeconds > 0 ? Math.Clamp(currentSeconds / durationSeconds, 0, 1) : 0;
        int filled = (int)Math.Round(ratio * barWidth);

        string bar;
        if (filled >= barWidth)
            bar = "[" + new string('=', barWidth) + "]";
        else
            bar = "[" + new string('=', filled) + ">" + new string('-', barWidth - filled - 1) + "]";

        return $"{bar} {timeLabel}";
    }

    private static string StatusLabel(PlayerController player)
    {
        if (player.IsPlaying)
            return Texts.Get("StatusPlaying");
        if (player.IsPaused)
            return Texts.Get("StatusPaused");
        return Texts.Get("StatusStopped");
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static void WriteRow(string text, int width)
    {
        if (text.Length > width)
            text = text[..Math.Max(0, width - 1)] + "…";

        Console.Write(text.PadRight(width));
        Console.WriteLine();
    }
}
