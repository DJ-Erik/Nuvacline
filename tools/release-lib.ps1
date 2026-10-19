# ============================================================================
# Nuvacline Release Manager - nucleo (Windows PowerShell 5.1)
# Entry point: tools\release.ps1   Documentazione: docs\RELEASE-MANAGER.md
# Sorgente in solo ASCII: PowerShell 5.1 legge i .ps1 senza BOM come ANSI.
# ============================================================================

$script:RmSchemaVersion = 1
$script:ProductName = 'Nuvacline'
$script:RmVersion = '1.0.0'

# Transizioni valide della macchina a stati. Da BLOCKED si rientra solo nella
# fase che ha fallito (verificata esplicitamente dal codice).
$script:Transitions = @{
    'INITIALIZING'            = @('PREFLIGHT', 'BLOCKED')
    'PREFLIGHT'               = @('BUILDING', 'BLOCKED')
    'BUILDING'                = @('TESTING', 'BLOCKED')
    'TESTING'                 = @('AUDITING', 'BLOCKED')
    'AUDITING'                = @('PACKAGING', 'BLOCKED')
    'PACKAGING'               = @('READY_FOR_HUMAN_TEST', 'BLOCKED')
    'READY_FOR_HUMAN_TEST'    = @('AWAITING_HUMAN_APPROVAL', 'BLOCKED')
    'AWAITING_HUMAN_APPROVAL' = @('HUMAN_TEST_APPROVED', 'BLOCKED')
    'HUMAN_TEST_APPROVED'     = @('PUBLISHING', 'BLOCKED')
    'PUBLISHING'              = @('DISTRIBUTED', 'BLOCKED')
    'DISTRIBUTED'             = @()
    'BLOCKED'                 = @('PREFLIGHT', 'BUILDING', 'TESTING', 'AUDITING', 'PACKAGING',
                                  'READY_FOR_HUMAN_TEST', 'AWAITING_HUMAN_APPROVAL',
                                  'HUMAN_TEST_APPROVED', 'PUBLISHING')
}

$script:NonTerminalStatuses = @('INITIALIZING', 'PREFLIGHT', 'BUILDING', 'TESTING', 'AUDITING',
                                'PACKAGING', 'READY_FOR_HUMAN_TEST', 'AWAITING_HUMAN_APPROVAL',
                                'HUMAN_TEST_APPROVED', 'PUBLISHING')

# Artefatti protetti nel repository. Gli hash noti provengono dalle verifiche
# del workflow 0.5.1 (10/10). Se non esiste un hash noto attendibile, il
# Release Manager registra l'hash iniziale e ne impone l'invarianza.
$script:ProtectedArtifacts = @(
    @{ Path = 'bin\Release\CleanSlate.exe';             Known = 'E6E98E5908037E344C4AFFA14E4C13A3FAAFADBEBFD8868C1ECFADB72759DB64' },
    @{ Path = 'tests\bin\CleanSlate.exe';               Known = 'E6E98E5908037E344C4AFFA14E4C13A3FAAFADBEBFD8868C1ECFADB72759DB64' },
    @{ Path = 'tests\bin\CleanSlate.Tests.exe';         Known = '73E9E70F4C3907CE5E60A5097C62CCA4C1D21F6E486EA85E3814B34431DDD724' },
    @{ Path = 'artifacts\alpha-0.4.5\CleanSlate.exe';   Known = '4CC85F12D67D8E829CAC352C1AF4A2C1D048822A76E420CF623373AC47E9B91C' },
    @{ Path = 'releases\0.4.5\CleanSlate-0.4.5-Windows.zip'; Known = '20C862F2948C42544D35A9FA832E8F4E03877ED12ED981C75D9F85FC5F3FA6AA' },
    @{ Path = 'LICENSE';                                Known = '3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986' },
    @{ Path = 'assets\logo\CleanSlate.ico';             Known = '7CBAD23EC404E01853EC9FDB7B7FBA663103C718E9E26C6F0ECEF711EC2A543F' },
    @{ Path = 'assets\logo\CleanSlate.png';             Known = 'B9A4B9FB4B45D4603834FEE094DCB556BEE7C08365B4DD366402BD4E877386D9' }
)

# Artefatti congelati esterni al repository (ricercati nella directory padre).
$script:ExternalProtectedArtifacts = @(
    @{ Glob = 'Nuvacline-Release-0.5.0\*\package\Nuvacline-0.5.0-Windows.zip'; Known = 'CBE66BF5FA34EB85F23AD9B388B6BA91967E7C155AD9D6CA03F1545CBF5CCDC1' },
    @{ Glob = 'Nuvacline-Release-0.5.0\*\build\Release\Nuvacline.exe';         Known = '6F436311485327490A2DF34049A13F41838243C2A04A4B35A056B3BD6541A7AB' }
)

# File del pacchetto (formato del progetto, 7 file, come la 0.5.0/0.5.1).
$script:RequiredPackageFiles = @('LICENSE', 'Nuvacline.exe', 'PRIVACY.md', 'README.txt',
                                 'SECURITY.md', 'SHA256SUMS.txt', 'THIRD-PARTY.txt')

# File gestiti dal bump di versione (stessa lista del workflow 0.5.1).
$script:ManagedReleaseFiles = @(
    'src\Nuvacline\AssemblyInfo.cs',
    'src\Nuvacline\Core\AppInfo.cs',
    'src\Nuvacline\app.manifest',
    'tests\verify.ps1',
    'src\Nuvacline.Tests\InfoSettingsTests.cs',
    'build.ps1',
    'README.md',
    'CHANGELOG.md',
    'PRIVACY.md',
    'SECURITY.md'
)

# File che devono contenere la sola versione corrente (nessun riferimento
# storico ammesso). Gli altri file gestiti sono "soft" (README/CHANGELOG).
$script:StrictVersionFiles = @(
    'src\Nuvacline\AssemblyInfo.cs',
    'src\Nuvacline\Core\AppInfo.cs',
    'src\Nuvacline\app.manifest',
    'tests\verify.ps1',
    'src\Nuvacline.Tests\InfoSettingsTests.cs',
    'build.ps1',
    'PRIVACY.md',
    'SECURITY.md'
)

# ----------------------------------------------------------------------------
# Utilita di base
# ----------------------------------------------------------------------------

function Get-RmTimestamp { return (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') }
function Get-RmStampDir { return (Get-Date).ToString('yyyyMMdd-HHmmss') }

function Write-RmPhase([string]$Text) { Write-Host ''; Write-Host ('=== ' + $Text + ' ===') -ForegroundColor Cyan }
function Write-RmInfo([string]$Text) { Write-Host ('  ' + $Text) }
function Write-RmOk([string]$Text) { Write-Host ('  OK   ' + $Text) -ForegroundColor Green }
function Write-RmWarn([string]$Text) { Write-Host ('  WARN ' + $Text) -ForegroundColor Yellow }
function Write-RmFail([string]$Text) { Write-Host ('  FAIL ' + $Text) -ForegroundColor Red }

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Force -Path $Path | Out-Null }
}

function Add-RmJournal([string]$WorkflowDir, [string]$Line) {
    if (-not (Test-Path -LiteralPath $WorkflowDir)) { return }
    $file = Join-Path $WorkflowDir 'release-journal.md'
    $nl = [Environment]::NewLine
    $entry = '[' + (Get-RmTimestamp) + '] ' + $Line + $nl
    if (-not (Test-Path -LiteralPath $file)) {
        [System.IO.File]::WriteAllText($file, '# Journal di release ' + $script:ProductName + $nl + $nl,
            (New-Object System.Text.UTF8Encoding($false)))
    }
    [System.IO.File]::AppendAllText($file, $entry, (New-Object System.Text.UTF8Encoding($false)))
}

function Read-TextFile([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    return (New-Object psobject -Property @{ Text = $text; HasBom = $hasBom })
}

function Write-TextFile([string]$Path, [string]$Text, [bool]$HasBom) {
    $enc = New-Object System.Text.UTF8Encoding($HasBom)
    [System.IO.File]::WriteAllText($Path, $Text, $enc)
}

function ConvertTo-NormalizedLines([string]$Text) {
    return ($Text -replace "`r`n", "`n")
}

# Esegue un comando nativo catturando output ed exit code senza far scattare
# $ErrorActionPreference = 'Stop' sullo stderr di progresso (problema noto di
# Windows PowerShell 5.1, incontrato anche nel workflow 0.5.1).
function Invoke-Native([string]$File, [string[]]$Arguments, [string]$LogPath, [string]$WorkDir) {
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $prevLoc = $null
    try {
        if ($WorkDir) { $prevLoc = Get-Location; Set-Location -LiteralPath $WorkDir }
        $all = @()
        $all += ('$ ' + $File + ' ' + ($Arguments -join ' '))
        $output = & $File @Arguments 2>&1
        $code = $LASTEXITCODE
        $texts = @($output | ForEach-Object { $_.ToString() })
        $all += $texts
        if ($LogPath) {
            $dir = Split-Path $LogPath -Parent
            if ($dir) { Ensure-Directory $dir }
            [System.IO.File]::WriteAllLines($LogPath, $all, (New-Object System.Text.UTF8Encoding($false)))
        }
        return (New-Object psobject -Property @{ ExitCode = $code; Output = $texts })
    } finally {
        if ($prevLoc) { Set-Location -LiteralPath $prevLoc }
        $ErrorActionPreference = $oldEap
    }
}

function Invoke-Git([string]$Repo, [string[]]$Arguments, [string]$LogPath) {
    $full = @('-C', $Repo) + $Arguments
    return Invoke-Native 'git' $full $LogPath $null
}

function Invoke-PowerShellScript([string]$ScriptPath, [string[]]$Arguments, [string]$LogPath, [string]$WorkDir) {
    $full = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Arguments
    return Invoke-Native 'powershell' $full $LogPath $WorkDir
}

function Get-GhExecutable {
    if ($env:NUVACLINE_GH_BIN) { return $env:NUVACLINE_GH_BIN }
    return 'gh'
}

function Invoke-Gh([string[]]$Arguments, [string]$LogPath) {
    return Invoke-Native (Get-GhExecutable) $Arguments $LogPath $null
}

# ----------------------------------------------------------------------------
# Stato persistente (scrittura atomica con copia precedente recuperabile)
# ----------------------------------------------------------------------------

function Save-ReleaseState([object]$State) {
    $dir = $State.workflowDir
    Ensure-Directory $dir
    $main = Join-Path $dir 'release-state.json'
    $prev = Join-Path $dir 'release-state.prev.json'
    $tmp = Join-Path $dir ('release-state.tmp.' + $PID + '.json')
    $State.updatedAt = Get-RmTimestamp
    $json = $State | ConvertTo-Json -Depth 15
    [System.IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $main) { Copy-Item -LiteralPath $main -Destination $prev -Force }
    Move-Item -LiteralPath $tmp -Destination $main -Force
}

function Load-ReleaseStateFile([string]$Path) {
    $prev = Join-Path (Split-Path $Path -Parent) 'release-state.prev.json'
    $mainError = $null
    try {
        $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
        $state = $text | ConvertFrom-Json
        if ($null -eq $state.status) { throw 'campo status assente' }
        return (New-Object psobject -Property @{ State = $state; Source = 'main'; Warning = $null })
    } catch { $mainError = $_.Exception.Message }
    if (Test-Path -LiteralPath $prev) {
        try {
            $text = Get-Content -LiteralPath $prev -Raw -Encoding UTF8
            $state = $text | ConvertFrom-Json
            if ($null -ne $state.status) {
                Copy-Item -LiteralPath $prev -Destination $Path -Force
                $warning = 'Stato principale corrotto (' + $mainError + '); ripristinata la copia release-state.prev.json.'
                return (New-Object psobject -Property @{ State = $state; Source = 'prev'; Warning = $warning })
            }
        } catch { }
    }
    throw ('Stato non leggibile e copia di backup assente o non valida: ' + $Path + ' (' + $mainError + ')')
}

function Add-ReleaseHistory([object]$State, [string]$Action, [string]$Detail) {
    $entry = New-Object psobject -Property @{ at = (Get-RmTimestamp); action = $Action; detail = $Detail }
    $State.history = @($State.history) + @($entry)
}

function Set-ReleaseStatus([object]$State, [string]$To, [string]$Action, [string]$Detail) {
    $from = $State.status
    $allowed = $script:Transitions[$from]
    if ($allowed -notcontains $To) {
        throw ('Transizione non valida: ' + $from + ' -> ' + $To)
    }
    Add-ReleaseHistory $State $Action $Detail
    $State.status = $To
    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ($from + ' -> ' + $To + ' | ' + $Action + ' | ' + $Detail)
}

function Set-ReleaseBlocked([object]$State, [string]$Phase, [string]$Reason, [bool]$Recoverable) {
    Add-ReleaseHistory $State 'BLOCKED' ($Phase + ': ' + $Reason)
    $State.blocked = New-Object psobject -Property @{
        phase = $Phase; reason = $Reason; recoverable = $Recoverable; at = (Get-RmTimestamp)
    }
    $State.status = 'BLOCKED'
    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ('BLOCKED | ' + $Phase + ' | ' + $Reason)
    Write-RmFail ('WORKFLOW BLOCCATO in ' + $Phase + ': ' + $Reason)
}

# ----------------------------------------------------------------------------
# Repository e Git
# ----------------------------------------------------------------------------

function Resolve-RepoRoot([string]$Hint) {
    $candidates = @()
    if ($Hint) { $candidates += $Hint }
    if ($PSScriptRoot) { $candidates += (Split-Path $PSScriptRoot -Parent) }
    foreach ($c in $candidates) {
        if (-not $c) { continue }
        $full = [System.IO.Path]::GetFullPath($c)
        $ok = (Test-Path -LiteralPath (Join-Path $full '.git')) -and
              (Test-Path -LiteralPath (Join-Path $full 'build.ps1')) -and
              (Test-Path -LiteralPath (Join-Path $full 'src\Nuvacline\AssemblyInfo.cs')) -and
              (Test-Path -LiteralPath (Join-Path $full 'tests\run-tests.ps1'))
        if ($ok) { return $full }
    }
    throw 'Repository Nuvacline non trovato o non valido (attesi .git, build.ps1, src\Nuvacline\AssemblyInfo.cs, tests\run-tests.ps1). Usare -RepoRoot.'
}

function Get-GitSnapshot([string]$Repo) {
    $branch = (Invoke-Git $Repo @('rev-parse', '--abbrev-ref', 'HEAD')).Output
    $head = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Output
    $statusLines = (Invoke-Git $Repo @('status', '--porcelain')).Output
    $staged = @(); $unstaged = @(); $untracked = @()
    foreach ($line in $statusLines) {
        if ($line.Length -lt 3) { continue }
        $x = $line.Substring(0, 1); $y = $line.Substring(1, 1); $p = $line.Substring(3)
        if ($x -eq '?' -and $y -eq '?') { $untracked += $p; continue }
        if ($x -ne ' ' -and $x -ne '?') { $staged += $p }
        if ($y -ne ' ') { $unstaged += $p }
    }
    return (New-Object psobject -Property @{
        Branch = if ($branch.Count -gt 0) { $branch[0] } else { '' }
        Head = if ($head.Count -gt 0) { $head[0] } else { '' }
        Staged = $staged; Unstaged = $unstaged; Untracked = $untracked
    })
}

function Get-LatestReleasedVersion([string]$Repo) {
    $versions = @()
    $tagOut = (Invoke-Git $Repo @('tag', '--list', 'v*')).Output
    foreach ($t in $tagOut) { if ($t -match '^v[0-9]+\.[0-9]+\.[0-9]+$') { $versions += $t.Substring(1) } }
    $remote = Invoke-Git $Repo @('ls-remote', '--tags', 'origin')
    $remoteOk = ($remote.ExitCode -eq 0)
    if ($remoteOk) {
        foreach ($line in $remote.Output) {
            if ($line -match 'refs/tags/v([0-9]+\.[0-9]+\.[0-9]+)(\^\{\})?\s*$') { $versions += $Matches[1] }
        }
    }
    $result = New-Object psobject -Property @{ Version = $null; RemoteOk = $remoteOk; All = @($versions) }
    if ($versions.Count -gt 0) {
        $max = $versions | Sort-Object { [version]$_ } | Select-Object -Last 1
        $result.Version = $max
    }
    return $result
}

function Get-ProtectedArtifactReport([string]$Repo) {
    $rows = @()
    foreach ($p in $script:ProtectedArtifacts) {
        $full = Join-Path $Repo $p.Path
        $exists = Test-Path -LiteralPath $full -PathType Leaf
        $hash = $null; $status = 'ASSENTE'
        if ($exists) {
            $hash = Get-Sha256 $full
            if ($p.Known) {
                if ($hash -eq $p.Known) { $status = 'OK' } else { $status = 'DIVERSO' }
            } else { $status = 'REGISTRATO' }
        }
        $rows += New-Object psobject -Property @{ Path = $p.Path; Exists = $exists; Sha256 = $hash; Known = $p.Known; Status = $status }
    }
    $parent = Split-Path $Repo -Parent
    foreach ($e in $script:ExternalProtectedArtifacts) {
        $pattern = Join-Path $parent $e.Glob
        $found = @(Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue)
        if ($found.Count -eq 0) {
            $rows += New-Object psobject -Property @{ Path = ('(esterno) ' + $e.Glob); Exists = $false; Sha256 = $null; Known = $e.Known; Status = 'ASSENTE' }
            continue
        }
        foreach ($f in $found) {
            $hash = Get-Sha256 $f.FullName
            if ($hash -eq $e.Known) { $status = 'OK' } else { $status = 'DIVERSO' }
            $rows += New-Object psobject -Property @{ Path = $f.FullName; Exists = $true; Sha256 = $hash; Known = $e.Known; Status = $status }
        }
    }
    return $rows
}

function Get-RepoSlug([string]$RemoteUrl) {
    if (-not $RemoteUrl) { return $null }
    if ($RemoteUrl -match 'github\.com[:/]+([^/]+)/([^/]+?)(\.git)?$') {
        return ($Matches[1] + '/' + $Matches[2])
    }
    return $null
}
