param(
    [string]$Version = '2.6.0',
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
    # The launcher and configurator share Avalonia and the .NET runtime. Keep
    # them together so those identical files are stored once per platform.
    & dotnet publish $configuratorProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) {
        throw "Configurator publish failed for $($runtime.Id)."
    }

    $maintenanceDestination = Join-Path $staging (Join-Path 'System\Maintenance' $runtime.Folder)
    & dotnet publish $updaterProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $maintenanceDestination
    if ($LASTEXITCODE -ne 0) {
        throw "Updater publish failed for $($runtime.Id)."
    }

    $hostDestination = Join-Path $staging (Join-Path 'System\CartMonitor' $runtime.Folder)
    & dotnet publish $hostProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $hostDestination
    if ($LASTEXITCODE -ne 0) {
        throw "CLC-Cart Monitor publish failed for $($runtime.Id)."
    }
    & dotnet publish $hostCleanupProject -c Release -r $runtime.Id --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=true `
        -p:DebugType=None -p:DebugSymbols=false -o $hostDestination
    if ($LASTEXITCODE -ne 0) {
        throw "CLC-Cart Monitor cleanup publish failed for $($runtime.Id)."
    }
}

# VideoLAN's Windows package carries x64, x86, and ARM64 native trees. The
# Windows release is x64-only; Windows native VLC files are unusable on Linux.
$windowsVlc = Join-Path $staging 'System\Windows-x64\libvlc'
foreach ($architecture in @('win-x86', 'win-arm64')) {
    $path = Join-Path $windowsVlc $architecture
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
$linuxVlc = Join-Path $staging 'System\Linux-x64\libvlc'
if (Test-Path -LiteralPath $linuxVlc) { Remove-Item -LiteralPath $linuxVlc -Recurse -Force }

foreach ($folder in @('Assets', 'Schemas')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) `
        -Destination (Join-Path $staging 'System') -Recurse -Force
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

New-Item -ItemType Directory -Path (Join-Path $staging 'Logs') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging 'System\Cache') -Force | Out-Null

# Some native runtime packages carry symbols even when DebugSymbols is disabled.
# Portable releases intentionally contain no debugging symbol files.
Get-ChildItem -LiteralPath (Join-Path $staging 'System') -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force
Get-ChildItem -LiteralPath (Join-Path $staging 'System\CartMonitor') -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force

$documentationDestination = Join-Path $staging 'System\Documentation'
New-Item -ItemType Directory -Path $documentationDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NOTICE') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'COMMERCIAL-LICENSE.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SECURITY.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Game-Configurator.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Launcher-ID-Guide.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Emulator-Launch-Guide.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Updater-Security.md') -Destination $documentationDestination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentation\Physical-Cart-Hardware-Test-Checklist.md') -Destination $documentationDestination

# Generated concept drafts are development assets; portable releases include only final collection artwork.
Get-ChildItem -LiteralPath (Join-Path $staging 'System\Assets\Collections') -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'Concepts' } |
    Remove-Item -Recurse -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build\WindowsLaunchers\Start Cart Launch Companion.bat') `
    -Destination $staging

$linuxLauncher = @'
#!/usr/bin/env sh
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR/System/Linux-x64" || exit 1
exec ./CartLaunchCompanion.Desktop "$@"
'@
$linuxLauncherPath = Join-Path $staging 'Start Cart Launch Companion.sh'
[IO.File]::WriteAllText($linuxLauncherPath, $linuxLauncher, [Text.UTF8Encoding]::new($false))

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build\WindowsLaunchers\Game Configurator.bat') `
    -Destination $staging

$linuxConfigurator = @'
#!/usr/bin/env sh
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR/System/Linux-x64" || exit 1
exec ./CartLaunchCompanion.Configurator "$@"
'@
$linuxConfiguratorPath = Join-Path $staging 'Game Configurator.sh'
[IO.File]::WriteAllText($linuxConfiguratorPath, $linuxConfigurator, [Text.UTF8Encoding]::new($false))

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build\WindowsLaunchers\Updater.bat') `
    -Destination $staging

$linuxUpdater = @'
#!/usr/bin/env sh
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$SCRIPT_DIR/System/Linux-x64" || exit 1
exec ./CartLaunchCompanion.Desktop --check-for-updates "$@"
'@
$linuxUpdaterPath = Join-Path $staging 'Updater.sh'
[IO.File]::WriteAllText($linuxUpdaterPath, $linuxUpdater, [Text.UTF8Encoding]::new($false))

$windowsStage = Join-Path $OutputRoot 'windows\CartLaunchCompanion'
$linuxStage = Join-Path $OutputRoot 'linux\CartLaunchCompanion'
Copy-Item -LiteralPath $staging -Destination $windowsStage -Recurse
Copy-Item -LiteralPath $staging -Destination $linuxStage -Recurse
Remove-Item -LiteralPath (Join-Path $windowsStage 'System\Linux-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'System\Maintenance\Linux-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'System\CartMonitor\Linux-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Start Cart Launch Companion.sh') -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Game Configurator.sh') -Force
Remove-Item -LiteralPath (Join-Path $windowsStage 'Updater.sh') -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'System\Windows-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'System\Maintenance\Windows-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'System\CartMonitor\Windows-x64') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Start Cart Launch Companion.bat') -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Game Configurator.bat') -Force
Remove-Item -LiteralPath (Join-Path $linuxStage 'Updater.bat') -Force

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

& (Join-Path $PSScriptRoot 'Build\Test-ReleasePackages.ps1') `
    -PackagesPath $packages -Version $version
if ($LASTEXITCODE -ne 0) {
    throw 'Release package audit failed.'
}

Write-Host "$version packages created in $packages" -ForegroundColor Green
