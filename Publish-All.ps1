# Compatibility entry point: portable publishing is now the default.
& (Join-Path $PSScriptRoot 'Publish-Portable.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
