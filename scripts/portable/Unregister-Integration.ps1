$ErrorActionPreference = 'Stop'

$verb = 'FeatherW8.Play'
$paths = @(
    'HKCU:\Software\Classes\SystemFileAssociations\.mp3\shell\' + $verb,
    'HKCU:\Software\Classes\SystemFileAssociations\.wav\shell\' + $verb,
    'HKCU:\Software\Classes\SystemFileAssociations\.ogg\shell\' + $verb,
    'HKCU:\Software\Classes\SystemFileAssociations\.opus\shell\' + $verb,
    'HKCU:\Software\Classes\SystemFileAssociations\.flac\shell\' + $verb,
    'HKCU:\Software\Classes\SystemFileAssociations\.dsf\shell\' + $verb,
    'HKCU:\Software\Classes\SystemFileAssociations\.dff\shell\' + $verb,
    'HKCU:\Software\Classes\Directory\shell\' + $verb
)

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
    Write-Host 'FeatherW8 was removed from the current user’s context menu.'
}
