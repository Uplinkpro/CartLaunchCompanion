# Stable portable release publisher.
& (Join-Path $PSScriptRoot 'Publish-Release.ps1') @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
