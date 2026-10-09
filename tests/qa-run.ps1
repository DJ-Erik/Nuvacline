$ErrorActionPreference = 'Continue'
$log = 'C:\Users\User\Documents\Experiments\cleanslate\tests\qa-run.log'
& powershell -ExecutionPolicy Bypass -File 'C:\Users\User\Documents\Experiments\cleanslate\tests\qa-practical.ps1' 2>&1 | Out-File -LiteralPath $log -Encoding UTF8
