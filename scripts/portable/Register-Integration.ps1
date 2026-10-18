$ErrorActionPreference = 'Stop'

$player = Join-Path $PSScriptRoot 'FeatherW8.exe'
if (-not (Test-Path -LiteralPath $player -PathType Leaf)) {
    $language = [System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName
    $message = if ($language -eq 'ru') {
        "Не найден FeatherW8.exe рядом со скриптом: $player"
    } else {
        "FeatherW8.exe was not found beside this script: $player"
    }
    Write-Error $message
    exit 1
}

# Prefer the player's configured language; otherwise follow the Windows UI language.
$languageSetting = 'auto'
$settingsPath = Join-Path $PSScriptRoot 'FeatherW8.properties'
if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    $languageLine = Get-Content -LiteralPath $settingsPath | Where-Object { $_ -match '^\s*language\s*=' } | Select-Object -First 1
    if ($languageLine -match '^\s*language\s*=\s*(\S+)') {
        $languageSetting = $Matches[1].ToLowerInvariant()
    }
}

$language = if ($languageSetting -in @('ru', 'en')) {
    $languageSetting
} else {
    [System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName
}
$isRussian = $language -eq 'ru'
$playLabel = if ($isRussian) { 'Проиграть в FeatherW8' } else { 'Play in FeatherW8' }
$folderLabel = if ($isRussian) { 'Открыть папку в FeatherW8' } else { 'Open folder in FeatherW8' }
$verb = 'FeatherW8.Play'
$extensions = @('.mp3', '.wav', '.flac', '.ogg', '.opus', '.dsf', '.dff')
$command = '"' + $player + '" "%1"'
$icon = '"' + $player + '",0'   # the menu entry shows the player's icon

foreach ($extension in $extensions) {
    $key = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\$verb"
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $playLabel
    Set-ItemProperty -Path $key -Name 'Icon' -Value $icon
    New-Item -Path "$key\command" -Force | Out-Null
    Set-Item -Path "$key\command" -Value $command
}

$folderKey = "HKCU:\Software\Classes\Directory\shell\$verb"
New-Item -Path $folderKey -Force | Out-Null
Set-Item -Path $folderKey -Value $folderLabel
Set-ItemProperty -Path $folderKey -Name 'Icon' -Value $icon
New-Item -Path "$folderKey\command" -Force | Out-Null
Set-Item -Path "$folderKey\command" -Value $command

if ($isRussian) {
    Write-Host 'Готово: пункты FeatherW8 добавлены в контекстное меню MP3/WAV/FLAC/Ogg/Opus/DSD и папок.'
    Write-Host "Плеер: $player"
    Write-Host 'Запись сделана только для текущего пользователя (HKCU), админ-права не нужны.'
    Write-Host 'Если переместишь portable-папку, запусти Register-Integration.bat ещё раз.'
} else {
    Write-Host 'Done: FeatherW8 context-menu entries were added for MP3/WAV/FLAC/Ogg/Opus/DSD and folders.'
    Write-Host "Player: $player"
    Write-Host 'The entries were added for the current user only (HKCU); no administrator rights are needed.'
    Write-Host 'If you move the portable folder, run Register-Integration.bat again.'
}
