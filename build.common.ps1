# Funzioni condivise di compilazione per CleanSlate.
# Si basa esclusivamente sul compilatore C# in-box di .NET Framework:
# nessun SDK, nessun ripristino di pacchetti, nessuna dipendenza di terze parti.

function Get-CscPath {
    $path = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Compilatore C# in-box non trovato: $path"
    }
    return $path
}

function Get-AppSources([string]$root) {
    $dir = Join-Path $root 'src\CleanSlate'
    if (-not (Test-Path -LiteralPath $dir)) { throw "Cartella sorgenti non trovata: $dir" }
    @(Get-ChildItem -Path $dir -Filter '*.cs' -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
}

function Get-TestSources([string]$root) {
    $dir = Join-Path $root 'src\CleanSlate.Tests'
    if (-not (Test-Path -LiteralPath $dir)) { throw "Cartella test non trovata: $dir" }
    @(Get-ChildItem -Path $dir -Filter '*.cs' -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
}

function Invoke-AppBuild([string]$root, [string]$configuration) {
    $csc = Get-CscPath
    $outDir = Join-Path $root "bin\$configuration"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $exe = Join-Path $outDir 'CleanSlate.exe'
    $sources = Get-AppSources $root
    if ($sources.Count -eq 0) { throw 'Nessuna sorgente applicativa trovata.' }

    $manifest = Join-Path $root 'src\CleanSlate\app.manifest'
    $cscArgs = @(
        '/nologo',
        '/noconfig',
        '/target:winexe',
        '/platform:x64',
        '/codepage:65001',
        "/out:$exe",
        '/r:System.dll',
        '/r:System.Core.dll',
        '/r:System.Drawing.dll',
        '/r:System.Windows.Forms.dll',
        "/win32manifest:$manifest"
    )
    if ($configuration -eq 'Release') {
        $cscArgs += '/optimize+'
    } else {
        $cscArgs += '/optimize-'
        $cscArgs += '/debug+'
    }
    $cscArgs += $sources

    Write-Host "Compilazione applicazione ($configuration)..."
    & $csc @cscArgs 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Compilazione applicazione non riuscita (codice $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $exe)) { throw "Output non trovato: $exe" }
    return $exe
}

function Invoke-TestBuild([string]$root, [string]$configuration, [string]$appExe) {
    $csc = Get-CscPath
    $outDir = Join-Path $root 'tests\bin'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $exe = Join-Path $outDir 'CleanSlate.Tests.exe'
    $sources = Get-TestSources $root
    if ($sources.Count -eq 0) { throw 'Nessun sorgente di test trovato.' }

    $cscArgs = @(
        '/nologo',
        '/noconfig',
        '/target:exe',
        '/platform:x64',
        '/codepage:65001',
        '/optimize+',
        "/out:$exe",
        "/r:$appExe",
        '/r:System.dll',
        '/r:System.Core.dll',
        '/r:System.Drawing.dll',
        '/r:System.Windows.Forms.dll'
    ) + $sources

    Write-Host "Compilazione test..."
    & $csc @cscArgs 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Compilazione dei test non riuscita (codice $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $exe)) { throw "Output non trovato: $exe" }

    # L'assembly dell'applicazione deve trovarsi accanto al test per essere caricato.
    Copy-Item -LiteralPath $appExe -Destination (Join-Path $outDir 'CleanSlate.exe') -Force

    return $exe
}
