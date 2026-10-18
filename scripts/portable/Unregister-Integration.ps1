$ErrorActionPreference = 'Stop'

$verb = 'FeatherW8.Play'
$extensions = @('.mp3', '.wav', '.flac', '.ogg', '.opus', '.dsf', '.dff')
# Each path is built inside its own parentheses: in PowerShell a comma binds tighter than +, so
# @('a' + $x, 'b' + $x) would not be a list of two paths.
$paths = @($extensions | ForEach-Object { "HKCU:\Software\Classes\SystemFileAssociations\$_\shell\$verb" })
$paths += "HKCU:\Software\Classes\Directory\shell\$verb"
foreach ($path in $paths) {
    Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
}

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

if ($language -eq 'ru') {
    Write-Host 'Пункт FeatherW8 удалён из контекстного меню текущего пользователя.'
} else {
    Write-Host "FeatherW8 was removed from the current user's context menu."
}
