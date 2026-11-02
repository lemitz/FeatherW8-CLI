# Security

## What FeatherW8 promises, and what it does not

FeatherW8 is an offline audio player. It reads the files you hand it and sends the decoded audio to your sound device, nothing more:

- There is **no network code**: no telemetry, update check, account or crash reporting. It opens no ports and contacts no server.
- The player **never writes** to disk or the registry. Besides your music it reads only `FeatherW8.properties` next to `FeatherW8.exe`, and unknown values there fall back to the defaults.
- It **needs no administrator rights** and never asks for them. The installer is per-user (`%LocalAppData%\Programs\FeatherW8`), and the optional context menu only adds keys under `HKCU\Software\Classes`, which the uninstaller or `Unregister-Integration.bat` removes. There is no autostart, background service or scheduled task.
- Audio files are opened read-only. FLAC, DSF/DSDIFF and the Ogg header are parsed by FeatherW8's own managed C# code, Vorbis and Opus by NVorbis and Concentus, WAV by NAudio and MP3 by Windows Media Foundation.

What FeatherW8 does **not** do:

- A `.cue` sheet can point to **any path**, including another folder or a network share (`\\server\share\...`). FeatherW8 checks that every referenced file exists and opens the WAV/FLAC ones as you, and to do that Windows may connect to the share and offer your Windows sign-in. Open CUE sheets only from sources you trust.
- Audio files are **not sandboxed**. The decoders are managed code, so a broken file should only end in an error message, but a crafted one can make the player use a lot of memory or exit. MP3 decoding runs inside Windows Media Foundation and is as safe as your Windows updates make it.
- ASIO drivers are COM DLLs loaded **into the player's process** and run with your rights. `audio.output=auto` only uses drivers named FiiO or K11; `asio` tries every installed one. FeatherW8 cannot check what a driver does.
- Starting a second copy asks the running one to quit; if it does not within 8 s, every process named `FeatherW8` that you are allowed to end is killed. Any program in your Windows session can stop playback the same way.
- Release files are built by GitHub Actions from the tagged commit, but they are **not code-signed** and no checksums are published, so Windows SmartScreen may warn about them. Download them only from the [Releases](https://github.com/lemitz/FeatherW8-CLI/releases) page, or build them yourself.

FeatherW8 has no software volume, and DSD plays at the level stored in the file. Keep the DAC's volume low when you try a new file.

## Reporting a problem

If you find a way for a crafted audio file or CUE sheet to run code, for FeatherW8 to write files or contact the network beyond the paths it was given, or for the installer or the context-menu scripts to change anything outside the current user's account, please report it **privately** through [GitHub security advisories](https://github.com/lemitz/FeatherW8-CLI/security/advisories/new) rather than a public issue. A file that only makes the player stop with an error is an ordinary bug for a normal issue. Attach a minimal crafted file rather than music you are not allowed to share.
