$ErrorActionPreference = 'Stop'

$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks -or -not ($sdks | Select-String '^10\.0\.')) {
    throw ".NET 10 SDK was not found. Install it with: winget install Microsoft.DotNet.SDK.10"
}

$project = Join-Path $PSScriptRoot 'CartLaunchCompanion.csproj'
$output = Join-Path $PSScriptRoot 'Portable'
$systemOutput = Join-Path $output 'System'
$log = Join-Path $PSScriptRoot 'Publish.log'

Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $log -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $systemOutput -Force | Out-Null

Write-Host "Publishing reliable x64 portable build (untrimmed)..." -ForegroundColor Cyan

& dotnet publish $project `
    -c Release `
    -p:Platform=x64 `
    -r win-x64 `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:SatelliteResourceLanguages=en-US `
    -o $systemOutput 2>&1 | Tee-Object -FilePath $log -Append | Out-Host

[int]$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "The x64 portable publish failed with exit code $exitCode. See Publish.log for details."
}

# Move user-facing content out of System so the portable root has exactly three folders.
foreach ($folderName in @('Games', 'Assets')) {
    $source = Join-Path $systemOutput $folderName
    $destination = Join-Path $output $folderName
    if (Test-Path $source) {
        Move-Item $source $destination -Force
    } else {
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
    }
}

# Config and generated Data remain under System so the root stays uncluttered.
New-Item -ItemType Directory -Path (Join-Path $systemOutput 'Data') -Force | Out-Null

# Create script-based launchers at the portable root. The real WinUI executable
# remains beside all of its native dependencies inside System.
$powerShellLauncher = @'
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$systemDirectory = Join-Path $root 'System'
$application = Join-Path $systemDirectory 'CartLaunchCompanion.exe'
$dataDirectory = Join-Path $systemDirectory 'Data'
$logPath = Join-Path $dataDirectory 'Launcher.log'

try {
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
        throw "The internal application was not found:`r`n$application"
    }

    $env:CARTLAUNCHCOMPANION_PORTABLE_ROOT = $root.TrimEnd([IO.Path]::DirectorySeparatorChar)

    Push-Location $systemDirectory
    try {
        & $application
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($null -eq $exitCode) { $exitCode = 0 }
    if ($exitCode -ne 0) {
        throw "The internal application exited with code $exitCode."
    }
}
catch {
    $message = $_ | Out-String
    try {
        Add-Content -LiteralPath $logPath -Value "[$(Get-Date -Format o)] $message"
    }
    catch { }

    Add-Type -AssemblyName PresentationFramework -ErrorAction SilentlyContinue
    if ('System.Windows.MessageBox' -as [type]) {
        [System.Windows.MessageBox]::Show(
            "$($_.Exception.Message)`r`n`r`nDetails were written to System\Data\Launcher.log.",
            'Cart Launch Companion',
            'OK',
            'Error') | Out-Null
    }
    else {
        Write-Host $_.Exception.Message -ForegroundColor Red
        Write-Host 'Details were written to System\Data\Launcher.log.' -ForegroundColor Yellow
        Read-Host 'Press Enter to close'
    }
    exit 1
}
'@
Set-Content -Path (Join-Path $output 'Launch.ps1') -Value $powerShellLauncher -Encoding UTF8

$commandLauncher = @'
@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch.ps1"
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" pause
exit /b %EXITCODE%
'@
Set-Content -Path (Join-Path $output 'CartLaunchCompanion.cmd') -Value $commandLauncher -Encoding ASCII

Get-ChildItem $systemOutput -Recurse -File -Include *.pdb,*.xml |
    Remove-Item -Force -ErrorAction SilentlyContinue

$buildMode = 'untrimmed self-contained'
$publishedFiles = @(Get-ChildItem $output -Recurse -File)
$totalBytes = ($publishedFiles | Measure-Object -Property Length -Sum).Sum
if ($null -eq $totalBytes) { $totalBytes = 0 }
$totalSizeMb = [Math]::Round($totalBytes / 1MB, 1)

Write-Host ''
Write-Host 'Portable publish completed successfully.' -ForegroundColor Green
Write-Host "Build mode : $buildMode" -ForegroundColor Green
Write-Host "Files      : $($publishedFiles.Count)" -ForegroundColor Green
Write-Host "Size       : $totalSizeMb MB" -ForegroundColor Green
Write-Host "Output     : $output" -ForegroundColor Green
Write-Host 'Root layout: CartLaunchCompanion.cmd, Launch.ps1, System, Games, Assets' -ForegroundColor Green
