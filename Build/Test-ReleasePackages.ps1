param(
    [Parameter(Mandatory)]
    [string]$PackagesPath,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$packages = (Resolve-Path -LiteralPath $PackagesPath).Path

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (!$Condition) {
        throw "Release package audit failed: $Message"
    }
}

function Get-ZipEntries {
    param([string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
        $archive.Dispose()
    }
}

function Get-TarEntries {
    param([string]$Path)
    $entries = @(& tar -tzf $Path)
    Assert-True ($LASTEXITCODE -eq 0) "Could not read $([IO.Path]::GetFileName($Path))."
    return $entries
}

function Assert-ArchiveContents {
    param(
        [string]$Path,
        [bool]$ExpectWindows,
        [bool]$ExpectLinux
    )

    $name = [IO.Path]::GetFileName($Path)
    $entries = if ($Path.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
        @(Get-ZipEntries $Path)
    }
    else {
        @(Get-TarEntries $Path)
    }

    Assert-True ($entries.Count -gt 0) "$name is empty."
    foreach ($required in @('README.md', 'SECURITY.md', 'Physical-Cart-Hardware-Test-Checklist.md')) {
        Assert-True (@($entries | Where-Object { $_ -like "*/$required" }).Count -gt 0) "$name is missing $required."
    }

    $hasWindows = @($entries | Where-Object { $_ -like '*/System/Windows-x64/*' }).Count -gt 0
    $hasLinux = @($entries | Where-Object { $_ -like '*/System/Linux-x64/*' }).Count -gt 0
    $hasWindowsHost = @($entries | Where-Object { $_ -like '*/System/Host/Windows-x64/*' }).Count -gt 0
    $hasLinuxHost = @($entries | Where-Object { $_ -like '*/System/Host/Linux-x64/*' }).Count -gt 0
    Assert-True ($hasWindows -eq $ExpectWindows) "$name has incorrect Windows runtime contents."
    Assert-True ($hasLinux -eq $ExpectLinux) "$name has incorrect Linux runtime contents."
    Assert-True ($hasWindowsHost -eq $ExpectWindows) "$name has incorrect Windows Host contents."
    Assert-True ($hasLinuxHost -eq $ExpectLinux) "$name has incorrect Linux Host contents."
    if ($ExpectWindows) {
        Assert-True (@($entries | Where-Object { $_ -like '*/Updater.bat' }).Count -gt 0) "$name is missing Updater.bat."
        Assert-True (@($entries | Where-Object { $_ -like '*/System/Windows-x64/CartLaunchCompanion.Configurator.exe' }).Count -gt 0) "$name is missing the Windows configurator."
        Assert-True (@($entries | Where-Object { $_ -like '*/System/Windows-x64/libvlc/win-x64/*' }).Count -gt 0) "$name is missing x64 LibVLC."
        Assert-True (@($entries | Where-Object { $_ -like '*/System/Windows-x64/libvlc/win-x86/*' -or $_ -like '*/System/Windows-x64/libvlc/win-arm64/*' }).Count -eq 0) "$name contains unused Windows LibVLC architectures."
    }
    if ($ExpectLinux) {
        Assert-True (@($entries | Where-Object { $_ -like '*/Updater.sh' }).Count -gt 0) "$name is missing Updater.sh."
        Assert-True (@($entries | Where-Object { $_ -like '*/System/Linux-x64/CartLaunchCompanion.Configurator' }).Count -gt 0) "$name is missing the Linux configurator."
        Assert-True (@($entries | Where-Object { $_ -like '*/System/Linux-x64/libvlc/*' }).Count -eq 0) "$name contains unusable Windows LibVLC files in its Linux runtime."
    }

    Assert-True (@($entries | Where-Object { $_ -like '*/System/Maintenance/Configurator/*' }).Count -eq 0) "$name contains the obsolete duplicate configurator runtime."

    $leaks = @($entries | Where-Object {
        $_ -match '(^|/)(bin|obj|\.git|Concepts)(/|$)' -or
        $_ -match '\.(cs|csproj|sln|slnx|pdb)$'
    })
    Assert-True ($leaks.Count -eq 0) "$name contains development files: $($leaks[0])."
}

$expectedPackages = @{
    "CartLaunchCompanion-$Version-win-x64.zip" = @{ Windows = $true; Linux = $false }
    "CartLaunchCompanion-$Version-linux-x64.tar.gz" = @{ Windows = $false; Linux = $true }
    "CartLaunchCompanion-$Version-portable.zip" = @{ Windows = $true; Linux = $true }
}

foreach ($entry in $expectedPackages.GetEnumerator()) {
    $path = Join-Path $packages $entry.Key
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$($entry.Key) is missing."
    Assert-True ((Get-Item -LiteralPath $path).Length -gt 0) "$($entry.Key) is empty."
    Assert-ArchiveContents $path $entry.Value.Windows $entry.Value.Linux
}

$checksumPath = Join-Path $packages 'SHA256SUMS.txt'
Assert-True (Test-Path -LiteralPath $checksumPath -PathType Leaf) 'SHA256SUMS.txt is missing.'
$checksumLines = @(Get-Content -LiteralPath $checksumPath | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
$packageFiles = @(Get-ChildItem -LiteralPath $packages -File | Where-Object Name -ne 'SHA256SUMS.txt')
Assert-True ($checksumLines.Count -eq $packageFiles.Count) 'SHA256SUMS.txt does not cover every release asset exactly once.'

$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in $checksumLines) {
    Assert-True ($line -match '^([0-9a-fA-F]{64})\s{2}(.+)$') "Invalid checksum line: $line"
    $expectedHash = $Matches[1].ToLowerInvariant()
    $fileName = $Matches[2]
    Assert-True ($seen.Add($fileName)) "Duplicate checksum entry for $fileName."
    Assert-True ([IO.Path]::GetFileName($fileName) -eq $fileName) "Unsafe checksum filename: $fileName"
    $filePath = Join-Path $packages $fileName
    Assert-True (Test-Path -LiteralPath $filePath -PathType Leaf) "Checksum references missing file $fileName."
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True ($actualHash -eq $expectedHash) "Checksum mismatch for $fileName."
}

$linuxPackage = Join-Path $packages "CartLaunchCompanion-$Version-linux-x64.tar.gz"
$modeLines = @(& tar -tvzf $linuxPackage)
Assert-True ($LASTEXITCODE -eq 0) 'Could not inspect Linux file permissions.'
$linuxExecutables = @(
    'CartLaunchCompanion.Desktop',
    'CartLaunchCompanion.Configurator',
    'CartLaunchCompanion.Updater',
    'CartLaunchCompanion.Host',
    'CartLaunchCompanion.HostCleanup',
    'Start Cart Launch Companion.sh',
    'Game Configurator.sh'
    'Updater.sh'
)
foreach ($executable in $linuxExecutables) {
    $line = $modeLines | Where-Object { $_ -like "*$executable" } | Select-Object -First 1
    Assert-True (![string]::IsNullOrWhiteSpace($line)) "Linux package is missing $executable."
    Assert-True ($line -match '^-rwxr-xr-x\s') "$executable is not packaged with mode 0755."
}

Write-Host "Release package audit passed for $Version ($($packageFiles.Count) assets)." -ForegroundColor Green
