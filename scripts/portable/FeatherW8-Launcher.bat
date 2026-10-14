@echo off
setlocal
if not exist "%~dp0FeatherW8.exe" (
    for /f "delims=" %%L in ('powershell.exe -NoProfile -Command "[System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName" 2^>nul') do set "UI_LANG=%%L"
    if /i "%UI_LANG%"=="ru" (echo Не найден FeatherW8.exe рядом с этим BAT-файлом.) else (echo FeatherW8.exe was not found beside this BAT file.)
    pause
    exit /b 1
)
"%~dp0FeatherW8.exe" %*
