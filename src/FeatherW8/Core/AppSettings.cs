using System.Globalization;

namespace FeatherW8.Core;

internal enum AppLanguage
{
    English,
    Russian
}

internal enum AudioOutputPreference
{
    Auto,
    Shared,
    WasapiExclusive,
    Asio
}

/// <summary>
/// Reads the portable, user-editable settings file beside FeatherW8.exe.
/// Unknown or malformed values deliberately fall back to safe defaults.
/// </summary>
internal static class AppSettings
{
    private static bool _initialized;

    public static AppLanguage Language { get; private set; } = AppLanguage.English;
    public static AudioOutputPreference AudioOutput { get; private set; } = AudioOutputPreference.Auto;
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "FeatherW8.properties");

    public static void Initialize()
    {
        if (_initialized)
            return;

        Dictionary<string, string> values = ReadProperties(SettingsPath);
        Language = ParseLanguage(values.GetValueOrDefault("language", "auto"));
        AudioOutput = ParseAudioOutput(values.GetValueOrDefault("audio.output", "auto"));
        _initialized = true;
    }

    private static Dictionary<string, string> ReadProperties(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return values;

        try
        {
            foreach (string originalLine in File.ReadLines(path))
            {
                string line = originalLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (key.Length > 0)
                    values[key] = value;
            }
        }
        catch (IOException)
        {
            // Settings are optional; do not prevent playback if they cannot be read.
        }
        catch (UnauthorizedAccessException)
        {
            // Settings are optional; do not prevent playback if they cannot be read.
        }

        return values;
    }

    private static AppLanguage ParseLanguage(string value)
    {
        if (value.Equals("ru", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.Russian;
        if (value.Equals("en", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.English;

        // Auto mode uses the Windows UI language, not the number/date formatting locale.
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Russian
            : AppLanguage.English;
    }

    private static AudioOutputPreference ParseAudioOutput(string value) => value.Trim().ToLowerInvariant() switch
    {
        "shared" => AudioOutputPreference.Shared,
        "wasapi-exclusive" or "exclusive" => AudioOutputPreference.WasapiExclusive,
        "asio" => AudioOutputPreference.Asio,
        _ => AudioOutputPreference.Auto
    };
}
