param(
    [string]$Version = '2.3.0',
    [string]$OutputRoot = (Join-Path $PSScriptRoot "artifacts\$Version")
)

$ErrorActionPreference = 'Stop'
$version = $Version
$launcherProject = Join-Path $PSScriptRoot 'Source\CartLaunchCompanion.Desktop\CartLaunchCompanion.Desktop.csproj'
$configuratorProject = Join-Path $PSScriptRoot 'Source\CartLaunchCompanion.Configurator\CartLaunchCompanion.Configurator.csproj'
$updaterProject = Join-Path $PSScriptRoot 'Source\CartLaunchCompanion.Updater\CartLaunchCompanion.Updater.csproj'
$hostProject = Join-Path $PSScriptRoot 'Source\CartLaunchCompanion.Host\CartLaunchCompanion.Host.csproj'
$hostCleanupProject = Join-Path $PSScriptRoot 'Source\CartLaunchCompanion.HostCleanup\CartLaunchCompanion.HostCleanup.csproj'
$staging = Join-Path $OutputRoot 'staging\CartLaunchCompanion'
$packages = Join-Path $OutputRoot 'packages'

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $packages -Force | Out-Null

$runtimes = @(
    @{ Id = 'win-x64'; Folder = 'Windows-x64' },
    @{ Id = 'linux-x64'; Folder = 'Linux-x64' }
)

foreach ($runtime in $runtimes) {
    $destination = Join-Path $staging (Join-Path 'System' $runtime.Folder)
    & dotnet publish $launcherProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $($runtime.Id)."
    }
    & dotnet publish $configuratorProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) {
        throw "Configurator publish failed for $($runtime.Id)."
    }

    $maintenanceDestination = Join-Path $staging (Join-Path 'Maintenance' $runtime.Folder)
    & dotnet publish $updaterProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=true `
        -p:DebugType=None -p:DebugSymbols=false -o $maintenanceDestination
    if ($LASTEXITCODE -ne 0) {
        throw "Updater publish failed for $($runtime.Id)."
    }

    $hostDestination = Join-Path $staging (Join-Path 'Host' $runtime.Folder)
    & dotnet publish $hostProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $hostDestination
    if ($LASTEXITCODE -ne 0) {
        throw "Cart Launch Host publish failed for $($runtime.Id)."
    }
    & dotnet publish $hostCleanupProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=true `
        -p:DebugType=None -p:DebugSymbols=false -o $hostDestination
    if ($LASTEXITCODE -ne 0) {
        throw "Cart Launch Host cleanup publish failed for $($runtime.Id)."
    }
}

foreach ($folder in @('Assets', 'Schemas')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) `
        -Destination (Join-Path $staging $folder) -Recurse -Force
}

$configDestination = Join-Path $staging 'Config'
New-Item -ItemType Directory -Path $configDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Config\Launchers.json') `
    -Destination $configDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Config\metadata.example.json') `
    -Destination $configDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Config\collection.example.json') `
    -Destination $configDestination

$gamesDestination = Join-Path $staging 'Games'
New-Item -ItemType Directory -Path $gamesDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Games\Examples') `
    -Destination (Join-Path $gamesDestination 'Examples') -Recurse -Force

foreach ($folder in @('Logs', 'Cache')) {
    New-Item -ItemType Directory -Path (Join-Path $staging $folder) -Force | Out-Null
}

# Some native runtime packages carry symbols even when DebugSymbols is disabled.
# Portable releases intentionally contain no debugging symbol files.
Get-ChildItem -LiteralPath (Join-Path $staging 'System') -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force
Get-ChildItem -LiteralPath (Join-Path $staging 'Host') -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NOTICE') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'COMMERCIAL-LICENSE.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Docs\2.0\ReleaseCandidate1.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Docs\2.0\UpgradeGuide.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Game-Configurator.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Emulator-Launch-Guide.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Updater-Security.md') -Destination $staging

# Generated concept drafts are development assets; portable releases include only final collection artwork.
Get-ChildItem -LiteralPath (Join-Path $staging 'Assets\Collections') -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'Concepts' } |
    Remove-Item -Recurse -Force

$windowsLauncher = @'
@echo off
setlocal
cd /d "%~dp0System\Windows-x64"
CartLaunchCompanion.Desktop.exe
exit /b %ERRORLEVEL%
'@
Set-Content -LiteralPath (Join-Path $staging 'Start Cart Launch Companion.bat') `
    -Value $windowsLauncher -Encoding ASCII

$linuxLauncher = @'
#!/usr/bin/env sh
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR/System/Linux-x64" || exit 1
exec ./CartLaunchCompanion.Desktop "$@"
'@
$linuxLauncherPath = Join-Path $staging 'Start Cart Launch Companion.sh'
Set-Content -LiteralPath $linuxLauncherPath -Value $linuxLauncher -Encoding utf8NoBOM

$windowsConfigurator = @'
@echo off
setlocal
cd /d "%~dp0System\Windows-x64"
CartLaunchCompanion.Configurator.exe
exit /b %ERRORLEVEL%
'@
Set-Content -LiteralPath (Join-Path $staging 'Game Configurator.bat') `
    -Value $windowsConfigurator -Encoding ASCII

$linuxConfigurator = @'
#!/usr/bin/env sh
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR/System/Linux-x64" || exit 1
exec ./CartLaunchCompanion.Configurator "$@"
'@
Set-Content -LiteralPath (Join-Path $staging 'Game Configurator.sh') `
    -Value $linuxConfigurator -Encoding utf8NoBOM

$windowsStage = Join-Path $OutputRoot 'windows\CartLaunchCompanion'
$linuxStage = Join-Path $OutputRoot 'linux\CartLaunchCompanion'
Copy-Item -LiteralPath $staging -Destination $windowsStage -Recurse
Copy-Item -LiteralPath $staging -Destination $linuxStage -Recurse
Remove-Item -LiteralPath (Join-Path $windowsStage 'System\Linux-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Maintenance\Linux-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Host\Linux-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Start Cart Launch Companion.sh') -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Game Configurator.sh') -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'System\Windows-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Maintenance\Windows-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Host\Windows-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Start Cart Launch Companion.bat') -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Game Configurator.bat') -Force

$windowsZip = Join-Path $packages "CartLaunchCompanion-$version-win-x64.zip"
$portableZip = Join-Path $packages "CartLaunchCompanion-$version-portable.zip"
Compress-Archive -LiteralPath $windowsStage -DestinationPath $windowsZip -CompressionLevel Optimal
& python (Join-Path $PSScriptRoot 'Build\CreatePortableZip.py') $staging $portableZip
if ($LASTEXITCODE -ne 0) {
    throw 'Combined portable archive creation failed.'
}

$linuxArchive = Join-Path $packages "CartLaunchCompanion-$version-linux-x64.tar.gz"
& python (Join-Path $PSScriptRoot 'Build\CreateLinuxTar.py') $linuxStage $linuxArchive
if ($LASTEXITCODE -ne 0) {
    throw 'Linux archive creation failed.'
}

$checksums = foreach ($file in Get-ChildItem -LiteralPath $packages -File | Sort-Object Name) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($file.Name)"
}
Set-Content -LiteralPath (Join-Path $packages 'SHA256SUMS.txt') `
    -Value $checksums -Encoding ASCII

Write-Host "$version packages created in $packages" -ForegroundColor Green
