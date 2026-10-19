# ============================================================================
# Fixture e utilita' per la suite del Release Manager Nuvacline.
# Solo ASCII (PowerShell 5.1 legge i .ps1 senza BOM come ANSI).
# ============================================================================

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw ('ASSERT fallita: ' + $Message) }
}

function Run-Test([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:Passed++
        Write-Host ('TEST OK   ' + $Name) -ForegroundColor Green
    } catch {
        $script:Failed++
        $script:Failures += ($Name + ' -> ' + $_.Exception.Message)
        Write-Host ('TEST FAIL ' + $Name + ' -> ' + $_.Exception.Message) -ForegroundColor Red
    }
}

function Git-Cli([string]$Repo, [string[]]$Arguments) {
    $out = & git -C $Repo @Arguments 2>&1
    return (New-Object psobject -Property @{ ExitCode = $LASTEXITCODE; Output = @($out | ForEach-Object { $_.ToString() }) })
}

function Invoke-Rel([object]$Fx, [string[]]$Extra) {
    $env:NUVACLINE_GH_BIN = $script:GhStub
    $env:NUVACLINE_GH_STATE = $Fx.GhState
    $cmdArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:ReleaseScript,
                 '-RepoRoot', $Fx.Repo, '-WorkflowRoot', $Fx.WfRoot) + @($Extra)
    $out = & powershell @cmdArgs 2>&1
    return (New-Object psobject -Property @{ ExitCode = $LASTEXITCODE; Output = @($out | ForEach-Object { $_.ToString() }) })
}

function Invoke-Raw([string[]]$CmdArgs) {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $script:ReleaseScript @CmdArgs 2>&1
    return (New-Object psobject -Property @{ ExitCode = $LASTEXITCODE; Output = @($out | ForEach-Object { $_.ToString() }) })
}

function Get-WfDir([object]$Fx) {
    if (-not (Test-Path -LiteralPath $Fx.WfRoot)) { return $null }
    $dirs = @(Get-ChildItem -LiteralPath $Fx.WfRoot -Directory | Sort-Object Name -Descending)
    if ($dirs.Count -eq 0) { return $null }
    return $dirs[0].FullName
}

function Get-State([object]$Fx) {
    $wf = Get-WfDir $Fx
    if (-not $wf) { return $null }
    return ((Get-Content -LiteralPath (Join-Path $wf 'release-state.json') -Raw -Encoding UTF8) | ConvertFrom-Json)
}

function Run-FlowToGate([object]$Fx, [string]$Version = '1.1.0') {
    $r = Invoke-Rel $Fx @('-Version', $Version)
    Assert-True ($r.ExitCode -eq 0) ('flusso verso il gate: exit ' + $r.ExitCode + ' | ' + (@($r.Output | Select-Object -Last 8) -join ' / '))
    $st = Get-State $Fx
    Assert-True ($st.status -eq 'AWAITING_HUMAN_APPROVAL') ('stato atteso AWAITING_HUMAN_APPROVAL, trovato ' + $st.status)
    return $st
}

function Write-FixtureFile([string]$Root, [string]$RelPath, [string]$Content) {
    $full = Join-Path $Root $RelPath
    $dir = Split-Path $full -Parent
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    if (-not $Content.EndsWith("`n")) { $Content += "`n" }
    [System.IO.File]::WriteAllText($full, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function New-GhStub([string]$Root) {
    $ps1 = Join-Path $Root 'gh-stub.ps1'
    $cmd = Join-Path $Root 'gh-stub.cmd'
    $ps1Content = @'
$state = [string]$env:NUVACLINE_GH_STATE
$a = @($args)
if ($a.Count -gt 0 -and $a[0] -eq '--version') { Write-Output 'gh version 2.0.0 (fixture)'; exit 0 }
if ($a.Count -ge 2 -and $a[0] -eq 'auth' -and $a[1] -eq 'status') { Write-Output 'Logged in (fixture)'; exit 0 }
if ($a.Count -ge 3 -and $a[0] -eq 'release' -and $a[1] -eq 'view') {
    $tag = $a[2]
    $f = Join-Path $state ('release-' + $tag + '.json')
    if (Test-Path -LiteralPath $f) { Write-Output (Get-Content -LiteralPath $f -Raw); exit 0 }
    [Console]::Error.WriteLine('release not found'); exit 1
}
if ($a.Count -ge 3 -and $a[0] -eq 'release' -and $a[1] -eq 'create') {
    $tag = $a[2]
    $zip = $null
    foreach ($x in $a) { if ($x -like '*.zip') { $zip = $x } }
    if (-not $zip) { [Console]::Error.WriteLine('zip assente'); exit 1 }
    $h = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $len = (Get-Item -LiteralPath $zip).Length
    $obj = [pscustomobject]@{
        tagName = $tag
        isPrerelease = $true
        url = ('https://example.invalid/releases/tag/' + $tag)
        assets = @([pscustomobject]@{
            name = (Split-Path $zip -Leaf)
            size = $len
            digest = ('sha256:' + $h)
            url = ('https://example.invalid/assets/' + (Split-Path $zip -Leaf))
        })
    }
    [System.IO.File]::WriteAllText((Join-Path $state ('release-' + $tag + '.json')), ($obj | ConvertTo-Json -Depth 6))
    Write-Output ('https://example.invalid/releases/tag/' + $tag)
    exit 0
}
[Console]::Error.WriteLine('gh stub: argomenti non gestiti: ' + ($a -join ' '))
exit 1
'@
    $cmdContent = "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File ""%~dp0gh-stub.ps1"" %*`r`nexit /b %ERRORLEVEL%`r`n"
    [System.IO.File]::WriteAllText($ps1, $ps1Content, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($cmd, $cmdContent, ([System.Text.ASCIIEncoding]::new()))
    return $cmd
}

function New-Fixture([string]$Root) {
    if (Test-Path -LiteralPath $Root) { Remove-Item -LiteralPath $Root -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    $bare = Join-Path $Root 'remote.git'
    & git init --bare --quiet $bare | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'git init --bare fallito' }
    $repo = Join-Path $Root 'repo'
    & git init -b master --quiet $repo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & git init --quiet $repo | Out-Null
        & git -C $repo checkout -b master | Out-Null
    }
    & git -C $repo config user.email 'fixture@example.invalid' | Out-Null
    & git -C $repo config user.name 'Fixture' | Out-Null
    & git -C $repo config commit.gpgsign false | Out-Null
    & git -C $repo config core.autocrlf false | Out-Null

    Write-FixtureFile $repo 'build.ps1' @'
param([switch]$SkipTests, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = '1.0.0'
$ai = Get-Content -LiteralPath (Join-Path $root 'src\Nuvacline\AssemblyInfo.cs') -Raw
if ($ai -match 'AssemblyInformationalVersion\("([0-9.]+)"\)') { $ver = $Matches[1] }
$bin = Join-Path $root 'bin\Release'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$prog = Join-Path $bin 'Program.cs'
$asm = Join-Path $bin 'AsmInfo.cs'
Set-Content -LiteralPath $prog -Value 'public static class P { public static void Main() { System.Console.WriteLine("Nuvacline fixture"); } }' -Encoding ASCII
Set-Content -LiteralPath $asm -Value ('using System.Reflection; [assembly: AssemblyVersion("' + $ver + '.0")] [assembly: AssemblyFileVersion("' + $ver + '.0")] [assembly: AssemblyInformationalVersion("' + $ver + '")]') -Encoding ASCII
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe ('/out:' + (Join-Path $bin 'Nuvacline.exe')) /r:System.dll $prog $asm | Out-Null
if ($LASTEXITCODE -ne 0) { exit 1 }
if (-not $SkipTests) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\run-tests.ps1')
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
if ($Package) {
    $art = Join-Path $root ('artifacts\nuvacline-' + $ver)
    if (Test-Path -LiteralPath $art) { Remove-Item -LiteralPath $art -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $art | Out-Null
    Copy-Item -LiteralPath (Join-Path $bin 'Nuvacline.exe') -Destination (Join-Path $art 'Nuvacline.exe')
    Set-Content -LiteralPath (Join-Path $art 'LICENSE') -Value 'Licenza fixture (generata).' -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $root 'PRIVACY.md') -Destination (Join-Path $art 'PRIVACY.md')
    Copy-Item -LiteralPath (Join-Path $root 'SECURITY.md') -Destination (Join-Path $art 'SECURITY.md')
    Set-Content -LiteralPath (Join-Path $art 'README.txt') -Value ('Nuvacline ' + $ver) -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $art 'THIRD-PARTY.txt') -Value 'Componenti di terze parti (fixture).' -Encoding ASCII
    $names = @('LICENSE', 'Nuvacline.exe', 'PRIVACY.md', 'README.txt', 'SECURITY.md', 'THIRD-PARTY.txt')
    $lines = @()
    foreach ($n in $names) {
        $h = (Get-FileHash -LiteralPath (Join-Path $art $n) -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines += ($h + '  ' + $n)
    }
    Set-Content -LiteralPath (Join-Path $art 'SHA256SUMS.txt') -Value $lines -Encoding ASCII
}
exit 0
'@

    Write-FixtureFile $repo 'tests\run-tests.ps1' @'
$total = 3
$passed = 3
$failed = 0
if ($env:NUVACLINE_TEST_FIXTURE_FAIL -eq '1') { $passed = 1; $failed = 2 }
Write-Host ('Totale: ' + $total + '  Superati: ' + $passed + '  Falliti: ' + $failed)
if ($failed -gt 0) { Write-Host 'TEST: FAIL'; exit 1 }
Write-Host 'TEST: OK'
exit 0
'@

    Write-FixtureFile $repo 'tests\verify.ps1' @'
param([string]$Exe)
Write-Host 'verifica fixture versione 1.0.0 (attesa *1.0.0*)'
if ($env:NUVACLINE_VERIFY_FIXTURE_FAIL -eq '1') { Write-Host 'VERIFY: FAIL'; exit 1 }
Write-Host 'VERIFY: OK'
exit 0
'@

    Write-FixtureFile $repo 'src\Nuvacline\AssemblyInfo.cs' @'
using System.Reflection;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
'@

    Write-FixtureFile $repo 'src\Nuvacline\Core\AppInfo.cs' @'
namespace Nuvacline.Core
{
    public static class AppInfo
    {
        public static string ProductVersion()
        {
            return "1.0.0";
        }
    }
}
'@

    Write-FixtureFile $repo 'src\Nuvacline\app.manifest' @'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity name="Nuvacline" version="1.0.0.0" />
  <description>Nuvacline 1.0.0</description>
</assembly>
'@

    Write-FixtureFile $repo 'src\Nuvacline.Tests\InfoSettingsTests.cs' @'
namespace Nuvacline.Tests
{
    public class InfoSettingsTests
    {
        public string CanonicalVersion_Is_1_0_0_AndUsedEverywhere()
        {
            return "1.0.0";
        }
    }
}
'@

    Write-FixtureFile $repo 'README.md' @'
# Nuvacline 1.0.0

Pacchetto nuvacline-1.0.0 per Windows (es. `v1.0.0`).
'@

    Write-FixtureFile $repo 'CHANGELOG.md' @'
# Changelog

## [1.0.0] - 2026-01-01

- Versione iniziale (fixture).
'@

    Write-FixtureFile $repo 'PRIVACY.md' @'
# Privacy

Fixture versione 1.0.0: nessun dato raccolto.
'@

    Write-FixtureFile $repo 'SECURITY.md' @'
# Security

Fixture versione 1.0.0.
'@

    & git -C $repo add -A | Out-Null
    & git -C $repo commit --quiet -m 'Baseline fixture' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'commit baseline fallito' }
    & git -C $repo tag -a v1.0.0 -m 'Fixture 1.0.0' | Out-Null
    & git -C $repo remote add origin $bare | Out-Null
    & git -C $repo push --quiet -u origin master --tags | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'push baseline fallito' }
    $head = (& git -C $repo rev-parse HEAD).Trim()
    $ghState = Join-Path $Root 'ghstate'
    New-Item -ItemType Directory -Force -Path $ghState | Out-Null
    return (New-Object psobject -Property @{
        Root = $Root; Repo = $repo; Bare = $bare; WfRoot = (Join-Path $Root 'workflows')
        GhState = $ghState; Baseline = $head
    })
}
