# Compatibility entry point for the Version 2 release-candidate publisher.
& (Join-Path $PSScriptRoot 'Publish-RC1.ps1') @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
