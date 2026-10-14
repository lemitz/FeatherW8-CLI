@echo off
setlocal
for /f "delims=" %%L in ('powershell.exe -NoProfile -Command "[System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName" 2^>nul') do set "UI_LANG=%%L"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Register-Integration.ps1"
if errorlevel 1 (
    echo.
    if /i "%UI_LANG%"=="ru" (echo Не удалось добавить пункты контекстного меню.) else (echo Could not add the context-menu entries.)
    pause
    exit /b 1
)
echo.
pause
