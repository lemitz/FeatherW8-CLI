$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\FeatherW8\FeatherW8.csproj'
$distDir = Join-Path $root 'dist'
$selfContainedDir = Join-Path $root 'publish\win-x64'
$frameworkDependentDir = Join-Path $root 'publish\win-x64-framework-dependent'
$selfContainedZip = Join-Path $distDir 'FeatherW8-Portable-win-x64.zip'
$smallZip = Join-Path $distDir 'FeatherW8-Portable-small-requires-dotnet10-win-x64.zip'

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
foreach ($dir in @($selfContainedDir, $frameworkDependentDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

# Self-contained: largest download, runs without a separately installed .NET runtime.
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $selfContainedDir

# Smaller alternative: requires the .NET 10 runtime to already be installed.
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $frameworkDependentDir

# Ship the Explorer helpers beside the EXE so they continue to work from any extracted folder.
$helperDir = Join-Path $PSScriptRoot 'portable'
foreach ($destination in @($selfContainedDir, $frameworkDependentDir)) {
    # Keep editable settings outside the single-file executable beside FeatherW8.exe.
    Copy-Item -Path (Join-Path $root 'FeatherW8.properties') -Destination $destination -Force
    Copy-Item -Path (Join-Path $helperDir '*') -Destination $destination -Force
    Copy-Item -Path (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
}

foreach ($zip in @($selfContainedZip, $smallZip)) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
}
Compress-Archive -Path (Join-Path $selfContainedDir '*') -DestinationPath $selfContainedZip
Compress-Archive -Path (Join-Path $frameworkDependentDir '*') -DestinationPath $smallZip

Write-Host "Self-contained portable archive: $selfContainedZip"
Write-Host "Small portable archive (requires .NET 10): $smallZip"
Write-Host 'To build the per-user installer, install Inno Setup 6 and run: iscc installer\FeatherW8.iss'
