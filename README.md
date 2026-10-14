# FeatherW8

Лёгкий самостоятельный Windows-плеер с терминальным интерфейсом.

## Русский

### Возможности

- Windows 10/11 x64.
- Терминальный экран с прогресс-баром на всю ширину окна; время воспроизведения расположено справа.
- Форматы: MP3, WAV, Ogg Vorbis и Opus-in-Ogg (mono/stereo).
- Автоматический bit-perfect-путь: ASIO-драйвер FiiO для K11, если он установлен и доступен; иначе — WASAPI Exclusive для обнаруженного USB DAC или устройства вывода Windows по умолчанию.
- В строгом режиме плеер не меняет частоту дискретизации программно и не применяет программную громкость/эффекты. Если устройство не поддерживает формат трека, плеер не переключается незаметно: предлагает Shared с явным предупреждением, что это уже не bit-perfect, либо пропустить трек.
- Для других USB DAC режим WASAPI Exclusive использует единственный явно распознанный USB/DAC endpoint; если таких устройств несколько — устройство Windows по умолчанию. При необходимости назначь нужный DAC устройством вывода по умолчанию в Windows.
- Файлы и папки открываются из Проводника или перетаскиванием на launcher. Папки сканируются только на верхнем уровне; очередь сортируется по имени. Файлового браузера, M3U и ввода команд внутри плеера нет.

ASIO/WASAPI Exclusive обходят общий системный микшер и предотвращают программное ресемплирование в FeatherW8, но не могут гарантировать отсутствие обработки внутри конкретного драйвера или DAC. Для сжатых форматов плеер сначала декодирует звук в PCM.

### Язык и настройки

Рядом с `FeatherW8.exe` находится `FeatherW8.properties`:

```properties
language=auto
audio.output=auto
```

Допустимые значения:

- `language=auto` — язык интерфейса Windows: русская Windows → русский, любой другой язык → английский.
- `language=ru` или `language=en` — принудительный язык. Переключателя языка в плеере нет.
- `audio.output=auto` — ASIO FiiO/K11, затем WASAPI Exclusive; Shared предлагается только при невозможности строгого режима.
- `audio.output=asio`, `wasapi-exclusive` или `shared` — ручное переопределение режима. Shared всегда помечается как не-bit-perfect.

Файл настроек поставляется рядом с EXE и одинаково работает в portable-версии и установке.

### Управление

- `↑` / `↓` — предыдущий / следующий трек.
- `←` / `→` — назад / вперёд на 5 секунд; `Shift` + стрелка — на 30 секунд.
- `Space` — пауза / продолжить.
- `C` — цикл повтора: выкл. → текущий трек → очередь → выкл.
- При недоступном строгом выводе: `Enter` — подтвердить WASAPI Shared (не bit-perfect), `N` — пропустить трек, `Q` / `Esc` — выход.
- `Q` или `Esc` — выход.

### Portable и Проводник

Portable-папку можно распаковать в любое место и перемещать. Драйвер ASIO FiiO должен быть установлен отдельно; FeatherW8 не включает и не устанавливает драйвер FiiO.

- `FeatherW8-Launcher.bat` запускает плеер с переданными путями; на него можно перетащить файл или папку.
- `Register-Integration.bat` добавляет для текущего пользователя контекстное меню файлов MP3/WAV/Ogg/Opus и папок. Админ-права не нужны.
- После переноса portable-папки запусти `Register-Integration.bat` повторно: Windows хранит абсолютный путь к EXE.
- `Unregister-Integration.bat` удаляет добавленные пункты меню.
- Контекстное меню не меняет приложение по умолчанию.

Установщик для текущего пользователя устанавливает программу в `%LocalAppData%\Programs\FeatherW8` и не требует прав администратора. Portable-пакет можно получить через `scripts\publish.ps1`; установщик собирается Inno Setup 6.

### Сборка и ограничения

Нужен .NET 10 SDK. Сборка .NET проверяется на Linux, но выбор ASIO endpoint, эксклюзивный доступ и реальное воспроизведение требуют Windows 10/11 и проверки с конкретным DAC/драйвером. Перед выпуском ASIO-сборки нужно проверить условия распространения ASIO SDK; FiiO-драйвер в комплект не включается.

Перемотка в Opus-in-Ogg заново декодирует поток от начала, поэтому на длинном треке возможна задержка. У декодера Vorbis есть upstream-ограничения при seek в конец файла и возврате в начало. Нет рекурсивного обхода, M3U/CUE, shuffle, управления громкостью и чтения тегов; на экране отображается имя файла.

---

## English

FeatherW8 is a lightweight, standalone Windows audio player with a terminal interface. It is an original project, not a modification of Pure Audio Player.

### Features

- Windows 10/11 x64.
- Terminal UI with a progress bar spanning the window and elapsed/total time on the right.
- MP3, WAV, Ogg Vorbis, and Opus-in-Ogg (mono/stereo).
- Automatic strict-output path: the FiiO ASIO driver for a K11 when installed and available; otherwise WASAPI Exclusive for a detected USB DAC or the Windows default render device.
- Strict mode does not software-resample or apply software volume/effects. If the device cannot accept the track format, FeatherW8 never downgrades silently: it offers WASAPI Shared with an explicit not-bit-perfect warning, or lets you skip the track.
- For other USB DACs, WASAPI Exclusive uses a uniquely identified USB/DAC endpoint; if several candidates exist, it uses the Windows default device. Set your preferred DAC as the Windows default output when needed.
- Open files and folders from Explorer or drag them onto the launcher. Folder scans are non-recursive and tracks are sorted by name. There is no built-in file browser, M3U support, or command-input field in the player.

ASIO/WASAPI Exclusive bypass the shared system mixer and prevent software resampling by FeatherW8, but cannot guarantee that a particular driver or DAC performs no internal processing. Compressed formats are decoded to PCM first.

### Language and settings

`FeatherW8.properties` sits beside `FeatherW8.exe`:

```properties
language=auto
audio.output=auto
```

Supported values:

- `language=auto` — follow the Windows UI language: Russian Windows selects Russian; all other languages select English.
- `language=ru` or `language=en` — force a language. The player has no language switch.
- `audio.output=auto` — try FiiO/K11 ASIO, then WASAPI Exclusive; offer Shared only if strict output is unavailable.
- `audio.output=asio`, `wasapi-exclusive`, or `shared` — manually override the output mode. Shared is always labelled not bit-perfect.

The settings file is shipped beside the EXE and works the same way in portable and installed builds.

### Controls

- `↑` / `↓` — previous / next track.
- `←` / `→` — seek by 5 seconds; hold `Shift` to seek by 30 seconds.
- `Space` — pause / resume.
- `C` — cycle repeat: off → track → queue → off.
- When strict output is unavailable: `Enter` — accept WASAPI Shared (not bit-perfect), `N` — skip the track, `Q` / `Esc` — quit.
- `Q` or `Esc` — quit.

### Portable build and Explorer integration

Extract the portable folder anywhere; it can be moved. The FiiO ASIO driver must be installed separately. FeatherW8 does not bundle or install the FiiO driver.

- `FeatherW8-Launcher.bat` starts the player with supplied paths; you can drop a file or folder onto it.
- `Register-Integration.bat` adds MP3/WAV/Ogg/Opus file and folder context-menu entries for the current user. No administrator rights are required.
- After moving the portable folder, run `Register-Integration.bat` again because Windows stores the absolute EXE path.
- `Unregister-Integration.bat` removes the entries.
- Explorer integration does not change the default application.

The per-user installer targets `%LocalAppData%\Programs\FeatherW8` and does not require administrator rights. Build portable archives with `scripts\publish.ps1`; build the installer with Inno Setup 6.

### Build and limitations

The project requires the .NET 10 SDK. A .NET build can be checked on Linux, but ASIO endpoint selection, exclusive access, and real playback must be verified on Windows 10/11 with the actual DAC and driver. Before distributing a build with ASIO support, review the ASIO SDK redistribution terms. The FiiO driver is not included.

Seeking in Opus-in-Ogg re-decodes from the beginning and may take time on long tracks. The Vorbis decoder has upstream limitations when seeking to end-of-file and then back to the start. There is no recursive scan, M3U/CUE, shuffle, volume control, or tag reader; the UI shows the file name.
