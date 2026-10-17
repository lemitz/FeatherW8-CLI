namespace FeatherW8.Core;

/// <summary>
/// Small embedded RU/EN message catalog. The selected language is controlled by
/// FeatherW8.properties so the portable and installed builds behave identically.
/// </summary>
internal static class Texts
{
    private static readonly IReadOnlyDictionary<string, (string En, string Ru)> Messages =
        new Dictionary<string, (string En, string Ru)>(StringComparer.Ordinal)
        {
            ["WindowsOnly"] = ("FeatherW8 is for Windows 10/11 x64.", "FeatherW8 предназначен для Windows 10/11 x64."),
            ["TerminalRequired"] = ("An interactive terminal is required; use the keyboard to control the player.", "Нужен интерактивный терминал: плеер принимает управление с клавиатуры."),
            ["NoSupportedFiles"] = ("No supported audio files were found.", "Не нашёл поддерживаемых аудиофайлов."),
            ["PlayerError"] = ("Player error: {0}", "Ошибка плеера: {0}"),
            ["UsageTitle"] = ("FeatherW8 — lightweight Windows audio player", "FeatherW8 — лёгкий Windows-плеер"),
            ["UsageLabel"] = ("Usage:", "Использование:"),
            ["UsageFile"] = ("  FeatherW8.exe <file.mp3|file.wav|file.ogg|file.opus>", "  FeatherW8.exe <файл.mp3|файл.wav|файл.ogg|файл.opus>"),
            ["UsageFolder"] = ("  FeatherW8.exe <folder> [file or folder ...]", "  FeatherW8.exe <папка> [файл или папка ...]"),
            ["UsageFolderNote"] = ("Folders are scanned one level deep; matching files are sorted by name.", "Папки сканируются только на один уровень; найденные файлы сортируются по имени."),
            ["UsageFormats"] = ("Formats: MP3, WAV, Ogg Vorbis, Opus-in-Ogg.", "Форматы: MP3, WAV, Ogg Vorbis, Opus-in-Ogg."),
            ["UsageHelp"] = ("Help: FeatherW8.exe --help", "Для справки: FeatherW8.exe --help"),
            ["UiHeader"] = ("FeatherW8 1.0  |  MP3 · WAV · Ogg Vorbis · Opus", "FeatherW8 1.0  |  MP3 · WAV · Ogg Vorbis · Opus"),
            ["TrackLine"] = ("Track {0}/{1}: {2}", "Трек {0}/{1}: {2}"),
            ["StatusLine"] = ("Status: {0}   Repeat: {1}", "Статус: {0}   Повтор: {1}"),
            ["Controls"] = ("↑/↓ track   ←/→ ±5 s (Shift ±30 s)   Space pause   C repeat   Q/Esc quit", "↑/↓ трек   ←/→ ±5 с (Shift ±30 с)   Space пауза   C повтор   Q/Esc выход"),
            ["StatusPlaying"] = ("playing", "играет"),
            ["StatusPaused"] = ("paused", "пауза"),
            ["StatusStopped"] = ("stopped", "остановлен"),
            ["RepeatOff"] = ("off", "выкл."),
            ["RepeatTrack"] = ("track", "трек"),
            ["RepeatQueue"] = ("queue", "очередь"),
            ["RepeatMessage"] = ("Repeat: {0}", "Повтор: {0}"),
            ["PlaybackError"] = ("Playback error: {0}", "Ошибка воспроизведения: {0}"),
            ["QueueEnded"] = ("Queue ended. ↑/↓ — choose a track, Q — quit.", "Очередь закончилась. ↑/↓ — выбрать трек, Q — выход."),
            ["LastTrack"] = ("This is the last track in the queue.", "Это последний трек очереди."),
            ["QueueEndedShort"] = ("Queue ended.", "Очередь закончилась."),
            ["SeekError"] = ("Could not seek: {0}", "Не удалось перемотать: {0}"),
            ["OpenError"] = ("Could not open “{0}”: {1}", "Не удалось открыть «{0}»: {1}"),
            ["QueueEmpty"] = ("The playback queue cannot be empty.", "Очередь не может быть пустой."),
            ["FolderReadError"] = ("Could not read folder “{0}”: {1}", "Не удалось прочитать папку «{0}»: {1}"),
            ["NotFound"] = ("Not found: “{0}”", "Не найдено: «{0}»"),
            ["UnsupportedFile"] = ("Skipping unsupported file: “{0}”", "Пропускаю неподдерживаемый файл: «{0}»"),
            ["UnsupportedFormat"] = ("The “{0}” format is not supported yet.", "Формат «{0}» пока не поддерживается."),
            ["OpusChannels"] = ("This version supports mono/stereo Opus only; the file has {0} channels.", "В этой версии поддерживается Opus только в mono/stereo; в файле {0} каналов."),
            ["OpusHeadMissing"] = ("The Ogg file does not contain an OpusHead header.", "В Ogg-файле не найден заголовок OpusHead."),
            ["InvalidOgg"] = ("The file is not a valid Ogg stream.", "Файл не является корректным Ogg-потоком."),
            ["OggHeaderTooLarge"] = ("The Ogg header is too large.", "Ogg-заголовок слишком большой."),
            ["TruncatedOgg"] = ("The file is truncated inside the Ogg header.", "Файл обрезан внутри заголовка Ogg."),
            ["BufferRangeInvalid"] = ("The buffer range is outside the destination array.", "Диапазон буфера выходит за границы массива назначения."),
            ["FlacInvalid"] = ("The file is not a valid FLAC stream.", "Файл не является корректным FLAC-потоком."),
            ["FlacChannels"] = ("This version supports mono/stereo FLAC only; the file has {0} channels.", "Эта версия воспроизводит FLAC только mono/stereo; в файле {0} каналов."),
            ["FlacBitDepth"] = ("FLAC with {0} bits per sample is not supported (up to 24 bit).", "FLAC с {0} бит на сэмпл не поддерживается (до 24 бит)."),
            ["FlacUnknownLength"] = ("The FLAC file does not declare its length (STREAMINFO is empty).", "FLAC-файл не содержит длительности (пустой STREAMINFO)."),
            ["AudioModeAsio"] = ("FiiO ASIO", "FiiO ASIO"),
            ["AudioModeWasapiExclusive"] = ("WASAPI Exclusive", "WASAPI Exclusive"),
            ["AudioModeShared"] = ("WASAPI Shared", "WASAPI Shared"),
            ["AudioModeSelected"] = ("Output: {0}", "Вывод: {0}"),
            ["AudioModeAsioLabel"] = ("ASIO — {0}", "ASIO — {0}"),
            ["AudioModeWasapiExclusiveLabel"] = ("WASAPI Exclusive — {0}", "WASAPI Exclusive — {0}"),
            ["AudioModeSharedLabel"] = ("WASAPI Shared — {0} — NOT bit-perfect", "WASAPI Shared — {0} — НЕ bit-perfect"),
            ["AudioModeDefaultDevice"] = ("Windows default output", "устройство вывода Windows по умолчанию"),
            ["AudioModeStrictUnavailable"] = ("Strict bit-perfect output could not start: {0}", "Строгий bit-perfect-вывод не запустился: {0}"),
            ["AudioModeFallbackPrompt"] = ("Enter — continue via Shared (not bit-perfect); N — skip track; Q/Esc — quit.", "Enter — продолжить через Shared (не bit-perfect); N — пропустить трек; Q/Esc — выход."),
            ["AudioModeFallbackEnabled"] = ("Shared output enabled for this queue — NOT bit-perfect.", "Для этой очереди включён Shared-вывод — НЕ bit-perfect."),
            ["AudioModeSharedActive"] = ("Shared output is active. This is not bit-perfect.", "Работает Shared-вывод. Это не bit-perfect."),
            ["AudioModeTrackSkipped"] = ("Track skipped because strict output was unavailable.", "Трек пропущен: строгий режим вывода недоступен."),
            ["AudioModeFormatUnsupported"] = ("The device does not support this track's sample rate/format.", "Устройство не поддерживает частоту дискретизации/формат этого трека."),
            ["AudioModeChannelsUnsupported"] = ("The ASIO driver does not expose enough output channels.", "ASIO-драйвер предоставляет недостаточно выходных каналов."),
            ["AudioModeDeviceUnavailable"] = ("The selected audio device or driver is unavailable.", "Выбранное аудиоустройство или драйвер недоступны."),
            ["AudioModeAsioSkipped"] = ("ASIO could not start ({0}); using WASAPI Exclusive.", "ASIO не запустился ({0}); используется WASAPI Exclusive."),
            ["AudioModeNoAsioDriver"] = ("No ASIO driver is installed.", "ASIO-драйвер не установлен."),
        };

    private static AppLanguage _language = AppLanguage.English;

    public static void Initialize(AppLanguage language) => _language = language;

    public static string Get(string key)
    {
        if (!Messages.TryGetValue(key, out var pair))
            return key;
        return _language == AppLanguage.Russian ? pair.Ru : pair.En;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);
}
