$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $root 'tests\qa-run.log'
& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'tests\qa-practical.ps1') 2>&1 | Out-File -LiteralPath $log -Encoding UTF8
