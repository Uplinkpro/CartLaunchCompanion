# Stable Version 2 portable release publisher.
& (Join-Path $PSScriptRoot 'Publish-RC1.ps1') @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
