<p align="center">
  <img src="icons/FeatherW8-logo.png" width="128" alt="FeatherW8 logo">
</p>

<h1 align="center">FeatherW8</h1>

<p align="center">
  A lightweight, standalone audio player for Windows 10/11 that lives in the terminal.<br>
  Plays <code>MP3</code>, <code>WAV</code>, <code>FLAC</code>, <code>Ogg Vorbis</code>, <code>Opus</code> and <code>DSD</code> (native over ASIO) bit-perfect through ASIO or WASAPI Exclusive. No library, no telemetry, no nonsense.
</p>

<p align="center">
  <a href="https://github.com/lemitz/FeatherW8-CLI/releases"><img src="https://img.shields.io/badge/version-1.1-8b8cff" alt="Version 1.1"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20x64-0078d4" alt="Windows 10 / 11 x64">
  <img src="https://img.shields.io/badge/built%20with-.NET%2010-512bd4" alt=".NET 10">
  <img src="https://img.shields.io/badge/output-ASIO%20%7C%20WASAPI%20Exclusive-e8590c" alt="ASIO / WASAPI Exclusive">
  <img src="https://img.shields.io/badge/telemetry-none-2ea043" alt="No telemetry">
</p>

<p align="center">
  <b>English</b> · <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  Looking for the Android music player? See <a href="https://github.com/lemitz/FeatherW8">lemitz/FeatherW8</a>.
</p>

---

FeatherW8 does one thing: it takes the files or folders you hand it and plays them, in order, as faithfully as the hardware allows. There is no library, no playlists, no tag editor — just a queue, a progress bar and a handful of keys. It was built around a FiiO K11 desktop DAC, so it prefers the FiiO ASIO driver when one is installed and falls back to WASAPI Exclusive for any other USB DAC. In both cases the decoded audio reaches the device untouched: no software resampling, no software volume, no mixer. If strict output is impossible for a track, the player says so and asks before degrading to the shared Windows mixer — it never does that silently.

This is the whole interface:

```
FeatherW8 1.1  |  MP3 · WAV · FLAC · Ogg Vorbis · Opus · DSD
Track 3/12: 03 - Blue in Green.flac
[=========================>-------------------------------------] 02:14 / 05:37
Status: playing   Repeat: off
Output: ASIO — FiiO ASIO Driver
↑/↓ track   ←/→ ±5 s (Shift ±30 s)   Space pause   C repeat   Q/Esc quit
```

## Features

**Playback**
- MP3, WAV, **FLAC** (4–24 bit, up to 768 kHz), Ogg Vorbis, Opus-in-Ogg and **DSD** (`.dsf`, `.dff`; DSD64 / DSD128 / DSD256), mono and stereo. `.ogg` files are sniffed, so Opus inside `.ogg` plays just as well as `.opus`
- Takes any mix of files and folders — from the command line, from the Explorer context menu or dropped onto the launcher
- Folders are scanned one level deep and sorted by file name; files you pass explicitly keep their order
- Seek by 5 s or 30 s, pause / resume, repeat off → track → queue
- Progress bar stretched across the whole window with elapsed / total time, redrawn every 80 ms
- Interface in English, or in Russian when the Windows display language is Russian (can be forced in settings)

**Bit-perfect output**
- `auto` mode tries the FiiO ASIO driver first, then WASAPI Exclusive — on a FiiO endpoint, else on the only USB/DAC endpoint, else on the Windows default device
- WASAPI Exclusive is opened in raw, event-driven mode with "Pro Audio" MMCSS priority and a 120 ms buffer — past the Windows mixer, APO "enhancements" and any software volume
- FeatherW8 itself never touches the samples: no resampling, no volume, no DSP. The device has to accept the track's sample rate and format as-is, and the player never converts behind your back
- When strict output can't start — unsupported sample rate, missing driver, device busy, DAC unplugged mid-track — playback stops and the player asks: `Enter` to continue through WASAPI Shared from the same position (clearly labelled **NOT bit-perfect**), `N` to skip the track, `Q` to quit
- The active path is always on screen: `Output: ASIO — …`, `ASIO — … — native DSD128`, `ASIO — … — DSD64 via DoP`, `WASAPI Exclusive — …` or `WASAPI Shared — … — NOT bit-perfect`
- The output line is colour-coded, so the path can be read at a glance: **green** = ASIO, **cyan** = WASAPI Exclusive (both bit-perfect), **red** = WASAPI Shared (NOT bit-perfect). Warnings (e.g. ASIO could not start, Shared enabled) are gold, errors red, prompts that need an answer gold
- If ASIO was tried but could not start, the reason is shown under the output line instead of being swallowed

**DSD**
- **Native DSD** through an ASIO driver that supports it (ASIO 2.2 DSD mode, `DSDInt8MSB1`) — the 1-bit stream reaches the driver untouched. Verified with the FiiO ASIO driver (K11): DSD64, DSD128 and DSD256
- **DoP** (DSD over PCM, 24-bit with the `0x05`/`0xFA` markers) over ASIO or WASAPI Exclusive when native DSD is not available — for DACs and drivers without ASIO DSD. DoP needs the PCM path to be bit-perfect and a rate of DSD rate ÷ 16 (176.4 kHz for DSD64, 352.8 kHz for DSD128)
- DSF (LSB-first bits) and DSDIFF (`.dff`, uncompressed) are both read; the bit order is normalised losslessly. DST-compressed DSDIFF is not supported
- DSD is **never** sent through WASAPI Shared (it would turn into noise): if no strict path works the player offers only `N` skip / `Q` quit
- DSD cannot be attenuated in software, so it is played at the level stored in the file — keep the DAC's volume low when you try a new file

**Everything else**
- One executable and one plain-text settings file next to it; the portable build and the installed one behave identically
- Optional Explorer context menu — *Play in FeatherW8* for `.mp3` / `.wav` / `.flac` / `.ogg` / `.opus` / `.dsf` / `.dff` and *Open folder in FeatherW8* for folders — registered for the current user only, without admin rights and without touching default apps
- `--help`, meaningful exit codes (`0` ok, `1` playback error, `2` wrong usage or nothing to play), environment variables in paths are expanded
- No telemetry, no network access, no background service; the player itself never writes to disk or the registry (the only registry entries are the optional context-menu keys under `HKCU`)

## Keyboard

| Action | Keys |
|---|---|
| Previous / next track | `↑` / `↓` |
| Seek back / forward 5 s | `←` / `→` |
| Seek back / forward 30 s | `Shift+←` / `Shift+→` |
| Pause / resume | `Space` |
| Cycle repeat: off → track → queue | `C` |
| Quit | `Q` or `Esc` |

When strict output is unavailable and the player asks what to do:

| Action | Keys |
|---|---|
| Continue through WASAPI Shared (not bit-perfect) | `Enter` |
| Skip this track | `N` |
| Quit | `Q` or `Esc` |

For DSD files the `Enter` option is never offered - DSD cannot be played through the shared mixer - only skip and quit.

`↑` on the first track restarts it (or wraps to the last one when repeat is set to *queue*); `↓` on the last track stops with a message unless repeat is *queue*. Once you accept Shared with `Enter`, it stays on for the rest of the queue — restart the player to get back to strict mode.

## Installation

Every build produces three flavours (see [Building the archives and the installer](#building-the-archives-and-the-installer)):

- **Installer** — `FeatherW8-Setup-win-x64.exe` (Inno Setup). Installs per-user into `%LocalAppData%\Programs\FeatherW8`, no admin rights needed, setup language follows Windows (English / Russian), offers the Explorer context menu as an optional task and keeps your `FeatherW8.properties` on upgrade.
- **Portable** — `FeatherW8-Portable-win-x64.zip`: a self-contained single `FeatherW8.exe` that needs no .NET on the machine, plus the settings file and the Explorer scripts. Unzip anywhere, move it whenever you like.
- **Portable, small** — `FeatherW8-Portable-small-requires-dotnet10-win-x64.zip`: the same folder without the bundled runtime; requires the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Grab them from the [Releases](https://github.com/lemitz/FeatherW8-CLI/releases) page or build them yourself. FeatherW8 needs Windows 10/11 x64 and nothing else. For ASIO on a FiiO K11 install the FiiO USB DAC driver separately — it is not bundled. Without it the player falls back to WASAPI Exclusive, but on the K11 that path is 16-bit only, so 24-bit FLAC and DSD need the ASIO driver.

### Running it

Open a file or folder from Explorer (context menu, or drop it onto `FeatherW8-Launcher.bat` in the portable folder), or start it from a terminal:

```
FeatherW8.exe "D:\Music\Some Album"
FeatherW8.exe intro.wav "D:\Music\Some Album" bonus.opus
FeatherW8.exe --help
```

FeatherW8 needs an interactive console window (Windows Terminal and the classic console both work); if its input or output is redirected it exits with code 2.

### Explorer context menu (portable build)

- `Register-Integration.bat` adds *Play in FeatherW8* to MP3/WAV/FLAC/Ogg/Opus/DSD files and *Open folder in FeatherW8* to folders for the current user. On Windows 11 the entries live under *Show more options*.
- `Unregister-Integration.bat` removes them.
- Windows stores the absolute path to the EXE, so run `Register-Integration.bat` again after moving the folder.
- The installer does the same through its optional task and removes the entries on uninstall.

## Settings

`FeatherW8.properties` sits next to `FeatherW8.exe`; edit it and restart the player. Lines starting with `#` are comments, keys are case-insensitive, unknown values fall back to `auto`.

```properties
language=auto
audio.output=auto
dsd.mode=auto
ui.color=on
```

| Key | Value | Meaning |
|---|---|---|
| `language` | `auto` | Russian on a Russian Windows, English everywhere else |
| | `ru`, `en` | force the language (there is no switch inside the player) |
| `audio.output` | `auto` | FiiO ASIO → WASAPI Exclusive → ask before Shared |
| | `asio` | any installed ASIO driver, FiiO first; if none works you are offered Shared, never Exclusive |
| | `wasapi-exclusive` | WASAPI Exclusive on the Windows default output device (alias: `exclusive`) |
| | `shared` | WASAPI Shared on the default device — always labelled not bit-perfect (DSD cannot be played this way) |
| `ui.color` | `on` | colour-coded output line and messages (default); `off` turns the colours off. The `NO_COLOR` environment variable also disables them |
| `dsd.mode` | `auto` | native DSD on an ASIO driver → DoP over ASIO → DoP over WASAPI Exclusive |
| | `native` | native DSD only; if the driver cannot do it, the track is not played |
| | `dop` | DoP only (skip the native attempt) |

## How the output path is chosen

```
audio.output = auto
 ├─ 1. ASIO driver whose name contains "FiiO" or "K11"                                → strict
 ├─ 2. WASAPI Exclusive on: a FiiO endpoint → the only endpoint named "USB"/"DAC"
 │                          → the Windows default output                                → strict
 └─ 3. neither started → stop and ask: Shared (NOT bit-perfect) / skip the track / quit
```

- A strict path is accepted only if the device supports the track's channel count, sample rate and (for WASAPI) sample format as-is; otherwise it counts as failed and the next candidate is tried.
- If a strict session dies mid-track (the DAC was unplugged, the driver went away), the same question appears and `Enter` resumes from the position where it stopped.
- With several non-FiiO USB devices FeatherW8 does not guess and uses the Windows default device — set your DAC as the default output in Windows.
- `auto` only looks at FiiO/K11 ASIO drivers. To use another ASIO driver, set `audio.output=asio`.
- ASIO drivers are COM objects, so the whole player runs on one STA thread; this is also why a failed ASIO start is always reported rather than silently replaced by WASAPI.
- DSD files follow the same order with one more step in front: native DSD on the ASIO driver → DoP over ASIO → DoP over WASAPI Exclusive → stop and ask (skip / quit, never Shared).

What actually reaches the DAC:

| Format | Decoder | Sent to the device |
|---|---|---|
| WAV | NAudio `WaveFileReader` | exactly as stored in the file |
| MP3 | Windows Media Foundation | 16-bit PCM at the file's sample rate |
| Ogg Vorbis | NVorbis | 32-bit float at the file's sample rate |
| Opus | Concentus | 16-bit PCM at 48 kHz (Opus' native rate) |
| FLAC | built-in decoder | 16-bit PCM for 4–16 bit files, 24-bit PCM for 17–24 bit files, at the file's sample rate; narrower depths are only left-aligned (lossless) |
| DSF / DSDIFF | built-in reader | native: the raw 1-bit stream (`DSDInt8MSB1`) at the DSD rate; DoP: 24-bit PCM at DSD rate ÷ 16 |

ASIO and WASAPI Exclusive guarantee that FeatherW8 adds nothing between the decoder and the driver — no mixer, no resampler, no gain. What a particular driver or DAC does internally is outside its control, and lossy formats are of course decoded to PCM first; "bit-perfect" there means the decoder's output is delivered unchanged. Not every device accepts 32-bit float in exclusive mode — if yours doesn't, Vorbis files will trigger the Shared question.

The FLAC decoder is written from scratch and checked bit for bit: every file of the [IETF FLAC decoder test suite](https://github.com/ietf-wg-cellar/flac-test-files) that FeatherW8 can play (8/12/15/16/20/24 bit, 22.05–768 kHz, mono/stereo, fixed and variable block sizes, wasted bits, escape codes) decodes to exactly the MD5 stored in its STREAMINFO.

A note on the FiiO K11: its Windows (WASAPI) endpoint accepts only 16-bit PCM in exclusive mode (up to 384 kHz); 24-bit and 32-bit formats are offered by the ASIO driver only. 24-bit FLAC and all DSD therefore need the FiiO ASIO driver on that DAC.

## Development

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 x64 to actually run it. The project compiles on Linux/macOS too (`EnableWindowsTargeting` is on), which is handy for CI, but outside Windows the player exits immediately
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer, a FiiO K11 (or any ASIO device) to test the ASIO path

```bash
dotnet build src/FeatherW8/FeatherW8.csproj
dotnet run --project src/FeatherW8 -- "D:\Music\Some Album"
```

Run it from a real console window — an IDE output pane with redirected streams makes the player exit with code 2. `FeatherW8.properties` is copied next to the binary on build and publish.

## Building the archives and the installer

```powershell
.\scripts\publish.ps1            # both portable ZIPs → dist\
iscc installer\FeatherW8.iss     # installer → dist\FeatherW8-Setup-win-x64.exe
```

`publish.ps1` runs two `dotnet publish` passes for `win-x64` — self-contained single-file and framework-dependent — copies `FeatherW8.properties`, the Explorer scripts and `THIRD-PARTY-NOTICES.md` next to the EXE and zips both folders into `dist\`. The installer packs the self-contained output from `publish\win-x64`, so run the script first.

### Releases

Every pull request, every push to `main` and every manual run of the *Build and release* workflow builds the installer and both portable ZIPs and attaches them to the run as artifacts (Actions → the run → Artifacts) - handy for testing a change before it is merged. Pushing a version tag publishes a release from the same build:

```bash
git tag v1.2 && git push origin v1.2    # -> GitHub Release "FeatherW8-CLI v1.2" with the three files
```

The tag sets the version of the build (`v1.2` → `1.2.0`); keep `<Version>` in `FeatherW8.csproj`, the default in `installer/FeatherW8.iss`, the header text and the README badges in step with it.

Before distributing a build with ASIO support, review the ASIO SDK redistribution terms — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The FiiO driver is never bundled.

## Known limitations

- Windows 10/11 x64 only.
- FLAC and DSD are limited to mono/stereo; FLAC up to 24 bit (no 32-bit), no change of format inside one FLAC stream. DSD512, DST-compressed DSDIFF and DSD playback through WASAPI Shared are not supported. Native DSD needs an ASIO driver with DSD support; every other path uses DoP.
- No recursive folder scan, M3U/CUE, shuffle, tag reading (the file name is shown) or volume control — the last one on purpose: software volume would break bit-perfect output. Use the knob on the DAC.
- Seeking in Opus re-decodes the stream from the beginning, so a seek in a long file can take a moment. Opus is limited to mono/stereo.
- The Vorbis decoder (NVorbis) has upstream quirks when seeking to the very end of a file and back to the start.
- MP3 is decoded by Windows Media Foundation; on Windows "N" editions install the Media Feature Pack first.
- Non-FiiO DACs are matched by name ("USB", "DAC"); see above for what happens when there are several.

## Project structure

```
FeatherW8.properties           default settings, copied next to the EXE on build and publish
src/FeatherW8/
  Program.cs                   entry point: arguments, --help, console setup, 80 ms main loop
  Core/
    PlayerController.cs        queue navigation, key handling, repeat modes, the Shared fallback prompt
    PlaybackSession.cs         one track = one session: decoder + output device, seek, pause
    AudioOutputFactory.cs      ASIO / WASAPI Exclusive / Shared selection and endpoint matching, DSD path order
    NativeDsdAsioPlayer.cs     native DSD over ASIO (kAsioSetIoFormat, DSDInt8MSB1) on top of NAudio's AsioDriver
    TrackQueueBuilder.cs       files and folders from the command line → sorted queue
    AppSettings.cs             FeatherW8.properties parser
    Texts.cs                   embedded English / Russian message catalog
  Audio/
    AudioSourceFactory.cs      picks a decoder by extension, sniffs .ogg for Opus vs Vorbis
    FlacWaveStream.cs          seekable FLAC decoder (CRC-checked, bit-exact), seeks by bisecting frame headers
    DsdWaveStream.cs           DSF / DSDIFF reader: raw 1-bit stream, channel-interleaved, MSB first
    DopWaveProvider.cs         packs DSD into 24-bit DoP PCM for the PCM output paths
    OggOpusWaveStream.cs       seekable Opus-in-Ogg → PCM stream on top of Concentus
    SynchronizedWaveStream.cs  serializes decoder reads with seeks from the UI thread
  UI/
    TerminalUi.cs              draws the screen: header, track, progress bar, status, hints
scripts/
  publish.ps1                  builds both portable archives into dist/
  portable/                    launcher and Explorer context-menu scripts shipped with every build
installer/FeatherW8.iss        Inno Setup 6 script for the per-user installer
icons/                         application icon (.ico, source PNG, README logo)
THIRD-PARTY-NOTICES.md         licenses of the bundled packages and the ASIO note
```

The split is deliberate: `UI` only reads state from `PlayerController` and draws it, `Audio` only produces `WaveStream`s, and the playback thread inside NAudio's output device pulls from the decoder through `SynchronizedWaveStream`, so a seek from the UI thread never races the decoder.

## Tech stack

[.NET 10](https://dotnet.microsoft.com) · C# · [NAudio](https://github.com/naudio/NAudio) (`Wasapi`, `Asio`) · [NAudio.Vorbis](https://github.com/naudio/NAudio.Vorbis) / [NVorbis](https://github.com/NVorbis/NVorbis) · [Concentus.Oggfile](https://github.com/lostromb/concentus.oggfile) / [Concentus](https://github.com/lostromb/Concentus) · Windows Media Foundation · [Inno Setup 6](https://jrsoftware.org/isinfo.php)

Looking for the Android version? That's [lemitz/FeatherW8](https://github.com/lemitz/FeatherW8).

## License

FeatherW8 is released under the [MIT License](LICENSE). Third-party packages keep their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

<p align="center">
  <img src="https://img.shields.io/badge/Made%20with-AI-8b5cf6" alt="Made with AI"><br>
  <sub>This project was built with the help of AI tools.</sub>
</p>
