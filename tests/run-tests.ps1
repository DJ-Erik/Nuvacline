# Compila e esegue la suite di test di Nuvacline.
# Uso:
#   powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
#   powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 -Configuration Debug

param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $Root 'build.common.ps1')

$appExe = Invoke-AppBuild $Root $Configuration
$testExe = Invoke-TestBuild $Root $Configuration $appExe

$env:NUVACLINE_SRC = Join-Path $Root 'src\Nuvacline'
$env:NUVACLINE_BIN = Join-Path $Root "bin\$Configuration"

Write-Host ''
Write-Host "Esecuzione della suite: $testExe"
Write-Host ''

& $testExe
$code = $LASTEXITCODE

if ($code -eq 0) {
    Write-Host ''
    Write-Host 'TEST: OK (tutti i test superati)' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host "TEST: FALLITO (codice $code)" -ForegroundColor Red
}

exit $code
