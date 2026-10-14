@echo off
setlocal
for /f "delims=" %%L in ('powershell.exe -NoProfile -Command "[System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName" 2^>nul') do set "UI_LANG=%%L"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Unregister-Integration.ps1"
if errorlevel 1 (
    echo.
    if /i "%UI_LANG%"=="ru" (echo Не удалось удалить пункты контекстного меню.) else (echo Could not remove the context-menu entries.)
    pause
    exit /b 1
)
echo.
pause
