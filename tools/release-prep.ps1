# ============================================================================
# Nuvacline Release Manager - preparazione versione e preflight
# Richiede: release-lib.ps1 (dot-source precedente).
# ============================================================================

function Get-CurrentProductVersion([string]$Repo) {
    $file = Join-Path $Repo 'src\Nuvacline\AssemblyInfo.cs'
    $t = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
    $m = [regex]::Match($t, 'AssemblyInformationalVersion\("([0-9]+\.[0-9]+\.[0-9]+)"\)')
    if (-not $m.Success) { throw 'Versione corrente non determinabile da AssemblyInfo.cs.' }
    return $m.Groups[1].Value
}

# Regole di sostituzione per il bump meccanico old -> new. Ogni voce contiene
# il file, se e' "strict" (nessun riferimento storico ammesso) e le regole
# regex. L'ordine delle regole e' significativo.
function Get-VersionBumpRules([string]$Old, [string]$New) {
    $o = [regex]::Escape($Old)
    $n = $New
    $oldU = $Old.Replace('.', '_')
    $newU = $New.Replace('.', '_')
    $bt = [string][char]0x60
    $rules = @()
    $rules += (New-Object psobject -Property @{ Path = 'src\Nuvacline\AssemblyInfo.cs'; Strict = $true; Rules = @(
        @{ P = 'AssemblyVersion\("' + $o + '\.0"\)'; R = 'AssemblyVersion("' + $n + '.0")' },
        @{ P = 'AssemblyFileVersion\("' + $o + '\.0"\)'; R = 'AssemblyFileVersion("' + $n + '.0")' },
        @{ P = 'AssemblyInformationalVersion\("' + $o + '"\)'; R = 'AssemblyInformationalVersion("' + $n + '")' } ) })
    $rules += (New-Object psobject -Property @{ Path = 'src\Nuvacline\Core\AppInfo.cs'; Strict = $true; Rules = @(
        @{ P = 'return "' + $o + '";'; R = 'return "' + $n + '";' } ) })
    $rules += (New-Object psobject -Property @{ Path = 'src\Nuvacline\app.manifest'; Strict = $true; Rules = @(
        @{ P = 'version="' + $o + '\.0"'; R = 'version="' + $n + '.0"' },
        @{ P = 'Nuvacline ' + $o; R = 'Nuvacline ' + $n } ) })
    $rules += (New-Object psobject -Property @{ Path = 'tests\verify.ps1'; Strict = $true; Rules = @(
        @{ P = '\*' + $o + '\*'; R = '*' + $n + '*' },
        @{ P = 'versione ' + $o; R = 'versione ' + $n } ) })
    $rules += (New-Object psobject -Property @{ Path = 'src\Nuvacline.Tests\InfoSettingsTests.cs'; Strict = $true; Rules = @(
        @{ P = 'CanonicalVersion_Is_' + $oldU + '_AndUsedEverywhere'; R = 'CanonicalVersion_Is_' + $newU + '_AndUsedEverywhere' },
        @{ P = $o; R = $n } ) })
    foreach ($rel in @('build.ps1', 'PRIVACY.md', 'SECURITY.md')) {
        $rules += (New-Object psobject -Property @{ Path = $rel; Strict = $true; Rules = @(
            @{ P = $o; R = $n } ) })
    }
    $rules += (New-Object psobject -Property @{ Path = 'README.md'; Strict = $false; Rules = @(
        @{ P = '(?m)^# Nuvacline ' + $o + '\s*$'; R = '# Nuvacline ' + $n },
        @{ P = 'nuvacline-' + $o; R = 'nuvacline-' + $n },
        @{ P = '\(es\. ' + $bt + 'v' + $o + $bt + '\)'; R = '(es. ' + $bt + 'v' + $n + $bt + ')' } ) })
    return $rules
}

# Applica le regole. I file strict vengono aggiornati; README (soft) pure.
# Restituisce l'elenco dei file modificati e di quelli gia' alla versione
# corrente (nessuna modifica necessaria).
function Invoke-VersionFileUpdate([string]$Repo, [string]$Old, [string]$New) {
    $plan = Get-VersionBumpRules $Old $New
    $modified = @(); $unchanged = @(); $errors = @()
    foreach ($entry in $plan) {
        $full = Join-Path $Repo $entry.Path
        if (-not (Test-Path -LiteralPath $full)) { $errors += ('File gestito assente: ' + $entry.Path); continue }
        $rf = Read-TextFile $full
        $text = $rf.Text
        $newText = $text
        foreach ($rule in $entry.Rules) { $newText = [regex]::Replace($newText, $rule.P, $rule.R) }
        if ($newText -eq $text) { $unchanged += $entry.Path; continue }
        Write-TextFile $full $newText $rf.HasBom
        $modified += $entry.Path
    }
    return (New-Object psobject -Property @{ Modified = $modified; Unchanged = $unchanged; Errors = $errors })
}

# I file strict non devono contenere riferimenti alla versione precedente e
# devono contenere la versione corrente.
function Get-StrictVersionViolations([string]$Repo, [string]$Version, [string]$Old) {
    $violations = @()
    foreach ($rel in $script:StrictVersionFiles) {
        $full = Join-Path $Repo $rel
        if (-not (Test-Path -LiteralPath $full)) { $violations += ($rel + ': file assente'); continue }
        $t = [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
        $masked = $t.Replace($Version + '.0', '').Replace($Version, '')
        if ($Old -and $masked.Contains($Old)) { $violations += ($rel + ': contiene ancora ' + $Old) }
        if (-not $t.Contains($Version)) { $violations += ($rel + ': non contiene ' + $Version) }
    }
    return $violations
}

# Riferimenti residui alla versione precedente nei file tracciati (esclusi i
# file strict, gia' coperti). Vengono registrati come avvisi da rivedere.
function Get-OldVersionResiduals([string]$Repo, [string]$Old, [string]$New) {
    $warnings = @()
    $g = Invoke-Git $Repo @('grep', '-n', '-F', '-e', $Old)
    if ($g.ExitCode -ne 0 -and $g.ExitCode -ne 1) { return $warnings }
    foreach ($line in $g.Output) {
        $masked = $line
        if ($New) { $masked = $masked.Replace($New + '.0', '').Replace($New, '') }
        if (-not $masked.Contains($Old)) { continue }
        $isStrict = $false
        foreach ($rel in $script:StrictVersionFiles) {
            if (($line.StartsWith($rel + ':')) -or ($line.StartsWith(($rel -replace '\\', '/') + ':'))) { $isStrict = $true; break }
        }
        if (-not $isStrict) { $warnings += $line }
    }
    return $warnings
}

# Assicura una sezione del CHANGELOG per la nuova versione. Se l'utente l'ha
# gia' scritta, non viene toccata (modalita' 'preauthored'); altrimenti viene
# inserito uno stub minimale da rivedere (modalita' 'stub').
function Ensure-ChangelogSection([string]$Repo, [string]$Version, [string]$Old) {
    $full = Join-Path $Repo 'CHANGELOG.md'
    $rf = Read-TextFile $full
    $text = $rf.Text
    if ($text -match ('(?m)^## \[' + [regex]::Escape($Version) + '\]')) {
        return (New-Object psobject -Property @{ Mode = 'preauthored'; Stub = '' })
    }
    $nl = "`n"
    if ($text.Contains("`r`n")) { $nl = "`r`n" }
    $stub = '## [' + $Version + '] - ' + (Get-Date).ToString('yyyy-MM-dd') + $nl + $nl +
            '### Cambiato' + $nl + $nl + '- **Versione**: ' + $Old + ' -> ' + $Version + '.' + $nl + $nl
    $m = [regex]::Match($text, '(?m)^## \[')
    if ($m.Success) {
        $newText = $text.Substring(0, $m.Index) + $stub + $text.Substring($m.Index)
    } else {
        $newText = $text + $nl + $stub
    }
    Write-TextFile $full $newText $rf.HasBom
    return (New-Object psobject -Property @{ Mode = 'stub'; Stub = $stub })
}

# ----------------------------------------------------------------------------
# Preflight
# ----------------------------------------------------------------------------

function Get-ActiveWorkflowConflict([string]$WorkflowRoot, [string]$Version, [string]$ExcludeDir) {
    $conflicts = @()
    if (-not (Test-Path -LiteralPath $WorkflowRoot)) { return $conflicts }
    $dirs = @(Get-ChildItem -LiteralPath $WorkflowRoot -Directory -ErrorAction SilentlyContinue)
    foreach ($d in $dirs) {
        if ($ExcludeDir -and ($d.FullName -eq $ExcludeDir)) { continue }
        $sf = Join-Path $d.FullName 'release-state.json'
        if (-not (Test-Path -LiteralPath $sf)) { continue }
        try {
            $st = (Get-Content -LiteralPath $sf -Raw -Encoding UTF8 | ConvertFrom-Json)
            if ($null -eq $st.status -or $null -eq $st.version) { continue }
            if ($st.version -eq $Version -and ($script:NonTerminalStatuses -contains $st.status)) {
                $conflicts += ($d.FullName + ' (status ' + $st.status + ')')
            }
        } catch { }
    }
    return $conflicts
}

function Invoke-PreflightChecks([object]$State, [string]$Repo, [string]$WorkflowRoot, [string]$OldVersion) {
    $fails = @(); $warns = @(); $notes = @()
    $ev = Join-Path $State.workflowDir 'evidence\preflight'
    Ensure-Directory $ev

    $snap = Get-GitSnapshot $Repo
    $lines = @()
    $lines += ('branch: ' + $snap.Branch)
    $lines += ('head: ' + $snap.Head)
    $lines += ('staged: ' + ($snap.Staged.Count))
    $lines += ('unstaged: ' + ($snap.Unstaged.Count))
    foreach ($p in $snap.Unstaged) { $lines += ('  unstaged: ' + $p) }
    $lines += ('untracked: ' + ($snap.Untracked.Count))
    foreach ($p in $snap.Untracked) { $lines += ('  untracked: ' + $p) }
    [System.IO.File]::WriteAllLines((Join-Path $ev 'git-state.txt'), $lines, (New-Object System.Text.UTF8Encoding($false)))

    if ($snap.Branch -ne 'master') { $fails += ('Branch corrente non valido per una release: ' + $snap.Branch + ' (atteso master).') }
    if ($snap.Staged.Count -gt 0) { $fails += 'Indice Git non vuoto: file staged presenti prima della release. Rimuoverli esplicitamente prima di procedere.' }

    $check = Invoke-Git $Repo @('diff', '--check')
    if ($check.ExitCode -ne 0) { $fails += 'git diff --check ha segnalato problemi di whitespace/conflict marker.' }

    $latest = Get-LatestReleasedVersion $Repo
    if (-not $latest.RemoteOk) { $warns += 'ls-remote non raggiungibile: monotonia versione verificata solo sui tag locali.' }
    if ($latest.Version) {
        if (-not ([version]$State.version -gt [version]$latest.Version)) {
            $fails += ('La versione target ' + $State.version + ' non e'' maggiore dell''ultima rilasciata ' + $latest.Version + '.')
        }
    } else { $notes += 'Nessuna versione precedente trovata (prima release).' }

    $tagLocal = (Invoke-Git $Repo @('tag', '--list', $State.expectedTag)).Output
    if ($tagLocal.Count -gt 0) { $fails += ('Il tag ' + $State.expectedTag + ' esiste gia'' localmente.') }
    $remoteTags = (Invoke-Git $Repo @('ls-remote', '--tags', 'origin', $State.expectedTag)).Output
    if ($remoteTags -and $remoteTags.Count -gt 0) { $fails += ('Il tag ' + $State.expectedTag + ' esiste gia'' sul remote.') }

    $toolsLines = @()
    $gitOk = ((Invoke-Native 'git' @('--version') $null $null).ExitCode -eq 0)
    $toolsLines += ('git: ' + $gitOk)
    if (-not $gitOk) { $fails += 'git non disponibile.' }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $cscOk = Test-Path -LiteralPath $csc
    $toolsLines += ('csc in-box: ' + $cscOk + ' (' + $csc + ')')
    if (-not $cscOk) { $fails += 'Compilatore C# in-box non trovato.' }
    $ghProbe = Invoke-Gh @('--version') $null
    $ghOk = ($ghProbe.ExitCode -eq 0)
    $toolsLines += ('gh: ' + $ghOk)
    if (-not $ghOk) { $warns += 'GitHub CLI non disponibile: la pubblicazione remota sara'' impossibile (le fasi locali proseguono).' }
    [System.IO.File]::WriteAllLines((Join-Path $ev 'tools.txt'), $toolsLines, (New-Object System.Text.UTF8Encoding($false)))

    $protected = Get-ProtectedArtifactReport $Repo
    $protected | ConvertTo-Json -Depth 6 | ForEach-Object { [System.IO.File]::WriteAllText((Join-Path $ev 'protected-artifacts.json'), $_, (New-Object System.Text.UTF8Encoding($false))) }
    $State.protected = @($protected)
    foreach ($row in $protected) {
        if ($row.Status -eq 'DIVERSO') { $fails += ('Artefatto protetto modificato: ' + $row.Path) }
    }

    foreach ($inc in @($State.includeFiles)) {
        if (-not $inc) { continue }
        $full = Join-Path $Repo $inc
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { $fails += ('File incluso assente: ' + $inc); continue }
        foreach ($p in $script:ProtectedArtifacts) {
            if ($p.Path -eq $inc) { $fails += ('Un file protetto non puo'' essere incluso nel commit di release: ' + $inc) }
        }
    }

    $conflicts = Get-ActiveWorkflowConflict $WorkflowRoot $State.version $State.workflowDir
    foreach ($c in $conflicts) { $fails += ('Workflow attivo per la stessa versione: ' + $c) }

    $State.remoteLatest = if ($latest.Version) { $latest.Version } else { '' }
    $report = @()
    $report += ('Versione target: ' + $State.version + ' (ultima rilasciata: ' + ($State.remoteLatest -as [string]) + ', remote ok: ' + $latest.RemoteOk + ')')
    $report += ('File da includere oltre ai gestiti: ' + (@($State.includeFiles) -join ', '))
    $report += ('Artefatti protetti OK: ' + (@($protected | Where-Object { $_.Status -eq 'OK' }).Count) + ' / ' + @($protected).Count)
    foreach ($f in $fails) { $report += ('FAIL: ' + $f) }
    foreach ($w in $warns) { $report += ('WARN: ' + $w) }
    [System.IO.File]::WriteAllLines((Join-Path $ev 'checks.txt'), $report, (New-Object System.Text.UTF8Encoding($false)))

    foreach ($w in $warns) { Write-RmWarn $w }
    foreach ($n in $notes) { Write-RmInfo $n }
    return (New-Object psobject -Property @{ Fails = @($fails); Warns = @($warns); Protected = @($protected); Snapshot = $snap; Latest = $latest })
}
