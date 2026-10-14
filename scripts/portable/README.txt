FEATHERW8 — PORTABLE BUILD / PORTABLE-ВЕРСИЯ
Windows 10/11 x64

РУССКИЙ

БЫСТРЫЙ СТАРТ
1. Распакуй всю папку в любое удобное место. Папку можно перемещать.
2. Открой FeatherW8.exe с файлом/папкой или перетащи их на FeatherW8-Launcher.bat.
3. Запусти Register-Integration.bat, чтобы добавить контекстное меню файлов MP3/WAV/Ogg/Opus и папок. Админ-права не нужны.
4. Для удаления меню запусти Unregister-Integration.bat.

После переноса папки повторно запусти Register-Integration.bat. В Windows 11 пункт может находиться в «Показать дополнительные параметры».

НАСТРОЙКИ И АУДИО
Файл FeatherW8.properties лежит рядом с EXE. language=auto выбирает русский для русской Windows и английский для остальных; также можно указать language=ru или language=en. Переключателя языка в плеере нет.

audio.output=auto предпочитает FiiO ASIO для K11, затем WASAPI Exclusive. Если строгий режим невозможен, плеер спросит разрешение включить WASAPI Shared и явно предупредит, что он не bit-perfect. Enter — принять Shared; N — пропустить трек; Q/Esc — выход.

Для других USB DAC плеер использует единственный явно распознанный USB/DAC endpoint; если устройств несколько, используется устройство Windows по умолчанию. При необходимости назначь DAC устройством вывода Windows. Драйвер ASIO FiiO должен быть установлен отдельно; он не входит в архив.

КЛАВИШИ
Up/Down — трек; Left/Right — перемотка на 5 секунд; Shift+Left/Right — на 30 секунд; Space — пауза; C — повтор выкл./трек/очередь; Q/Esc — выход.

ENGLISH

QUICK START
1. Extract the whole folder anywhere; it can be moved.
2. Open FeatherW8.exe with a file/folder, or drag one onto FeatherW8-Launcher.bat.
3. Run Register-Integration.bat to add MP3/WAV/Ogg/Opus file and folder context-menu entries. No admin rights are needed.
4. Run Unregister-Integration.bat to remove the entries.

After moving the folder, run Register-Integration.bat again. On Windows 11 the entry may be under “Show more options”.

SETTINGS AND AUDIO
FeatherW8.properties sits beside the EXE. language=auto selects Russian for Russian Windows and English otherwise; you can also set language=ru or language=en. There is no language switch in the player.

audio.output=auto prefers FiiO ASIO for the K11, then WASAPI Exclusive. If strict output is unavailable, the player asks before enabling WASAPI Shared and clearly warns that it is not bit-perfect. Enter — accept Shared; N — skip the track; Q/Esc — quit.

For other USB DACs the player uses a uniquely identified USB/DAC endpoint; if several devices are found, it uses the Windows default. Set your preferred DAC as the Windows default when needed. Install the FiiO ASIO driver separately; it is not included in this archive.

KEYS
Up/Down — track; Left/Right — seek 5 seconds; Shift+Left/Right — seek 30 seconds; Space — pause; C — cycle repeat off/track/queue; Q/Esc — quit.
