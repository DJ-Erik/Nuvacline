# ============================================================================
# Nuvacline Release Manager - fasi BUILDING, TESTING, AUDITING
# Richiede: release-lib.ps1, release-prep.ps1 (dot-source precedenti).
# ============================================================================

function Apply-VersionRulesToText([string]$Text, [object]$Entry) {
    $t = $Text
    foreach ($rule in $Entry.Rules) { $t = [regex]::Replace($t, $rule.P, $rule.R) }
    return $t
}

function Get-GitFileText([string]$Repo, [string]$Commit, [string]$RelPath) {
    $spec = $Commit + ':' + ($RelPath -replace '\\', '/')
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = '-C "' + $Repo + '" show ' + $spec
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $null = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { return $null }
    return (ConvertTo-NormalizedLines $out)
}

function Get-SetEqual([string[]]$Left, [string[]]$Right) {
    $l = @($Left | Sort-Object -Unique)
    $r = @($Right | Sort-Object -Unique)
    if ($l.Count -ne $r.Count) { return $false }
    for ($i = 0; $i -lt $l.Count; $i++) { if ($l[$i] -ne $r[$i]) { return $false } }
    return $true
}

function Get-BaselineProductVersion([string]$Repo, [string]$Commit) {
    if (-not $Commit) { return '' }
    $t = Get-GitFileText $Repo $Commit 'src\Nuvacline\AssemblyInfo.cs'
    if (-not $t) { return '' }
    $m = [regex]::Match($t, 'AssemblyInformationalVersion\("([0-9]+\.[0-9]+\.[0-9]+)"\)')
    if ($m.Success) { return $m.Groups[1].Value }
    return ''
}

# Riconosce come e' stato modificato il CHANGELOG rispetto alla baseline: se la
# differenza e' esattamente la sezione della nuova versione, ne restituisce il
# testo (modalita' 'stub', ricostruibile); altrimenti 'preauthored'.
function Detect-ChangelogStub([string]$Repo, [string]$Commit, [string]$Version) {
    $base = Get-GitFileText $Repo $Commit 'CHANGELOG.md'
    if ($null -eq $base) { return (New-Object psobject -Property @{ Mode = 'unknown'; Stub = '' }) }
    $full = Join-Path $Repo 'CHANGELOG.md'
    $cur = ConvertTo-NormalizedLines ([System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8))
    $m = [regex]::Match($cur, ('(?m)^## \[' + [regex]::Escape($Version) + '\]'))
    if (-not $m.Success) { return (New-Object psobject -Property @{ Mode = 'unknown'; Stub = '' }) }
    $start = $m.Index
    $next = [regex]::Match($cur.Substring($start + 3), '(?m)^## \[')
    if ($next.Success) { $end = $start + 3 + $next.Index } else { $end = $cur.Length }
    $stubText = $cur.Substring($start, $end - $start)
    $without = $cur.Remove($start, $end - $start)
    if ($without -eq $base) { return (New-Object psobject -Property @{ Mode = 'stub'; Stub = $stubText }) }
    return (New-Object psobject -Property @{ Mode = 'preauthored'; Stub = '' })
}

# ----------------------------------------------------------------------------
# BUILDING: bump metadati + copia isolata + compilazione
# ----------------------------------------------------------------------------

function Invoke-BuildPhase([object]$State, [string]$Repo) {
    Write-RmPhase 'BUILDING - metadati di versione e build isolata'
    $oldV = Get-CurrentProductVersion $Repo
    $newV = $State.version
    Write-RmInfo ('Versione corrente: ' + $oldV + ' - target: ' + $newV)

    if ($oldV -ne $newV) {
        $upd = Invoke-VersionFileUpdate $Repo $oldV $newV
        if ($upd.Errors.Count -gt 0) {
            Set-ReleaseBlocked $State 'BUILDING' ('Aggiornamento metadati: ' + ($upd.Errors -join '; ')) $true
            return $false
        }
        $chlog = Ensure-ChangelogSection $Repo $newV $oldV
        $State.versionUpdate = New-Object psobject -Property @{
            from = $oldV; changelogMode = $chlog.Mode; changelogStub = $chlog.Stub
            modified = @($upd.Modified); unchanged = @($upd.Unchanged); residuals = @()
        }
        Write-RmInfo ('File aggiornati: ' + @($upd.Modified).Count + ' - gia'' a target: ' + @($upd.Unchanged).Count)
        Write-RmInfo ('CHANGELOG: ' + $chlog.Mode)
    } else {
        $fromV = ''
        $mode = 'unknown'; $stubText = ''
        if ($State.versionUpdate -and [string]$State.versionUpdate.from -and ([string]$State.versionUpdate.from -ne $newV)) {
            $fromV = [string]$State.versionUpdate.from
            $mode = [string]$State.versionUpdate.changelogMode
            $stubText = [string]$State.versionUpdate.changelogStub
        } else {
            $fromV = Get-BaselineProductVersion $Repo $State.baseline.head
            $det = Detect-ChangelogStub $Repo $State.baseline.head $newV
            $mode = $det.Mode; $stubText = $det.Stub
            if ($mode -eq 'unknown') {
                $null = Ensure-ChangelogSection $Repo $newV $fromV
                $det = Detect-ChangelogStub $Repo $State.baseline.head $newV
                $mode = $det.Mode; $stubText = $det.Stub
            }
        }
        $State.versionUpdate = New-Object psobject -Property @{
            from = $fromV; changelogMode = $mode; changelogStub = $stubText
            modified = @(); unchanged = @($script:ManagedReleaseFiles); residuals = @()
        }
        Write-RmInfo 'Metadati gia'' alla versione target: nessuna modifica automatica.'
    }
    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ('BUILDING | bump ' + $oldV + ' -> ' + $newV + ' | changelog ' + $State.versionUpdate.changelogMode)

    $viol = Get-StrictVersionViolations $Repo $newV $oldV
    if ($viol.Count -gt 0) {
        Set-ReleaseBlocked $State 'BUILDING' ('Verifica metadati strict fallita: ' + ($viol -join '; ')) $true
        return $false
    }

    $cand = Join-Path $State.workflowDir 'candidate\repo'
    if (Test-Path -LiteralPath $cand) { Remove-Item -LiteralPath $cand -Recurse -Force }
    Ensure-Directory (Split-Path $cand -Parent)
    $rc = Invoke-Native 'robocopy' @($Repo, $cand, '/E', '/XD', '.git', 'bin', 'obj', 'artifacts', 'releases', '/NFL', '/NDL', '/NJH', '/NJS', '/NP') (Join-Path $State.workflowDir 'evidence\build\robocopy.log') $null
    if ($rc.ExitCode -ge 8) {
        Set-ReleaseBlocked $State 'BUILDING' ('Copia isolata fallita (robocopy codice ' + $rc.ExitCode + ').') $true
        return $false
    }

    Write-RmInfo 'Compilazione nella copia isolata...'
    $build = Invoke-PowerShellScript (Join-Path $cand 'build.ps1') @('-SkipTests') (Join-Path $State.workflowDir 'candidate\qa-logs\build.log') $cand
    if ($build.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'BUILDING' ('Compilazione fallita (codice ' + $build.ExitCode + '). Log in candidate\qa-logs\build.log.') $true
        return $false
    }
    $exe = Join-Path $cand 'bin\Release\Nuvacline.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        Set-ReleaseBlocked $State 'BUILDING' 'Eseguibile non prodotto dalla build isolata.' $true
        return $false
    }
    $State.candidate = New-Object psobject -Property @{
        repoCopy = $cand; buildExe = $exe; buildExeSha256 = (Get-Sha256 $exe)
        releaseCommit = ''; exeSha256 = ''; zipPath = ''; zipSha256 = ''; zipBytes = 0; packageFiles = @()
    }
    Save-ReleaseState $State
    Write-RmOk 'Build isolata completata.'
    return $true
}

# ----------------------------------------------------------------------------
# TESTING: suite completa nella copia isolata
# ----------------------------------------------------------------------------

function Invoke-TestPhase([object]$State, [string]$Repo) {
    Write-RmPhase 'TESTING - suite di test nella copia isolata'
    $cand = $State.candidate.repoCopy
    $log = Join-Path $State.workflowDir 'candidate\qa-logs\tests.log'
    $res = Invoke-PowerShellScript (Join-Path $cand 'tests\run-tests.ps1') @() $log $cand
    $total = $null; $passed = $null; $failed = $null
    foreach ($l in $res.Output) {
        $m = [regex]::Match($l, 'Totale:\s*([0-9]+)\s+Superati:\s*([0-9]+)\s+Falliti:\s*([0-9]+)')
        if ($m.Success) {
            $total = [int]$m.Groups[1].Value; $passed = [int]$m.Groups[2].Value; $failed = [int]$m.Groups[3].Value
        }
    }
    $State.tests = New-Object psobject -Property @{
        exitCode = $res.ExitCode; total = $total; passed = $passed; failed = $failed
        log = $log; at = (Get-RmTimestamp)
    }
    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ('TESTING | exit ' + $res.ExitCode + ' | Totale ' + $total + ' Superati ' + $passed + ' Falliti ' + $failed)
    if ($res.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'TESTING' ('Suite di test non superata (codice ' + $res.ExitCode + '). Log in candidate\qa-logs\tests.log.') $true
        return $false
    }
    Write-RmOk ('Suite superata: ' + $passed + '/' + $total + ' (se riportati dalla suite).')
    return $true
}

# ----------------------------------------------------------------------------
# AUDITING: verifiche di fedelta' al bump, commit locale controllato
# ----------------------------------------------------------------------------

function Invoke-AuditPhase([object]$State, [string]$Repo) {
    Write-RmPhase 'AUDITING - verifiche e commit locale'
    $head0 = $State.baseline.head
    $headNow = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Output
    if (@($headNow).Count -gt 0 -and $headNow[0] -ne $head0) {
        $expectedMsg = 'Release preparation: ' + $script:ProductName + ' ' + $State.version
        $subj = (Invoke-Git $Repo @('log', '-1', '--format=%s')).Output
        $parent = (Invoke-Git $Repo @('rev-parse', 'HEAD^')).Output
        $files = @((Invoke-Git $Repo @('show', '--name-only', '--format=', 'HEAD')).Output | Where-Object { $_ -ne '' } | ForEach-Object { $_ -replace '/', '\' })
        $subjOk = (@($subj).Count -gt 0 -and $subj[0] -eq $expectedMsg)
        $parentOk = (@($parent).Count -gt 0 -and $parent[0] -eq $head0)
        if ($subjOk -and $parentOk -and $files.Count -gt 0) {
            $State.releaseFiles = @($files)
            Add-Member -InputObject $State.candidate -NotePropertyName releaseCommit -NotePropertyValue $headNow[0] -Force
            Save-ReleaseState $State
            Add-RmJournal $State.workflowDir ('AUDITING | commit di release gia'' presente, adottato: ' + $headNow[0])
            Write-RmOk ('Commit di release gia'' presente, adottato: ' + $headNow[0])
            return $true
        }
        Set-ReleaseBlocked $State 'AUDITING' ('HEAD e'' avanzato a ' + $headNow[0] + ' senza un commit di release riconoscibile: intervento manuale richiesto.') $false
        return $false
    }
    $violations = @(); $warnings = @()
    $ruleMap = @{}
    foreach ($e in (Get-VersionBumpRules $State.versionUpdate.from $State.version)) { $ruleMap[$e.Path] = $e }
    $incList = @($State.includeFiles)

    foreach ($rel in $script:ManagedReleaseFiles) {
        $full = Join-Path $Repo $rel
        $current = ConvertTo-NormalizedLines ([System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8))
        $oldText = Get-GitFileText $Repo $head0 $rel
        if ($null -eq $oldText) { $violations += ($rel + ': file non presente nella baseline.'); continue }
        if ($rel -eq 'CHANGELOG.md') {
            if ($State.versionUpdate.changelogMode -eq 'stub') {
                $stub = ConvertTo-NormalizedLines ([string]$State.versionUpdate.changelogStub)
                $idx = $oldText.IndexOf('## [')
                if ($idx -lt 0) { $violations += 'CHANGELOG.md: sezione di versione non trovata per lo stub.'; continue }
                $reconstructed = $oldText.Substring(0, $idx) + $stub + $oldText.Substring($idx)
                if ($reconstructed -ne $current) { $violations += 'CHANGELOG.md: contenuto diverso dallo stub automatico.' }
            } else {
                if ($current -notmatch ('(?m)^## \[' + [regex]::Escape($State.version) + '\]')) {
                    $violations += ('CHANGELOG.md: manca la sezione [' + $State.version + '].')
                }
            }
            continue
        }
        if (-not $ruleMap.ContainsKey($rel)) { continue }
        $reconstructed = Apply-VersionRulesToText $oldText $ruleMap[$rel]
        if ($reconstructed -ne $current) {
            if ($incList -contains $rel) {
                $warnings += ($rel + ': modifiche manuali oltre al bump (file incluso esplicitamente).')
            } else {
                $violations += ($rel + ': differenze rispetto al bump di versione puro.')
            }
        }
    }

    $strictViol = Get-StrictVersionViolations $Repo $State.version $State.versionUpdate.from
    $violations += $strictViol

    $check = Invoke-Git $Repo @('diff', '--check')
    if ($check.ExitCode -ne 0) { $violations += 'git diff --check segnala problemi.' }

    if ($violations.Count -gt 0) {
        Set-ReleaseBlocked $State 'AUDITING' ('Audit fallito: ' + ($violations -join '; ')) $false
        return $false
    }
    foreach ($w in $warnings) { Write-RmWarn $w }
    Write-RmOk 'Fedelta'' dei file gestiti verificata (ricostruzione dal bump).'

    $residuals = Get-OldVersionResiduals $Repo $State.versionUpdate.from $State.version
    if ($residuals.Count -gt 0) {
        $State.versionUpdate.residuals = @($residuals)
        Add-RmJournal $State.workflowDir ('AUDITING | residui versione precedente (storici): ' + $residuals.Count)
    } else {
        $State.versionUpdate.residuals = @()
    }

    $snap = Get-GitSnapshot $Repo
    if ($snap.Staged.Count -gt 0) {
        Set-ReleaseBlocked $State 'AUDITING' 'Indice Git non vuoto al momento del commit.' $false
        return $false
    }

    $commitFiles = @()
    foreach ($rel in $script:ManagedReleaseFiles) {
        $specRel = $rel -replace '\\', '/'
        $diff = Invoke-Git $Repo @('diff', '--name-only', '--', $specRel)
        if ($diff.Output.Count -gt 0) { $commitFiles += $rel }
    }
    foreach ($inc in $incList) { if ($inc -and ($commitFiles -notcontains $inc)) { $commitFiles += $inc } }
    if ($commitFiles.Count -eq 0) {
        Set-ReleaseBlocked $State 'AUDITING' 'Nessuna modifica di release da committare (versione gia'' committata?).' $false
        return $false
    }
    $State.releaseFiles = @($commitFiles)

    $addArgs = @('add', '--') + @($commitFiles | ForEach-Object { $_ -replace '\\', '/' })
    $add = Invoke-Git $Repo $addArgs
    if ($add.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'AUDITING' ('git add fallito (codice ' + $add.ExitCode + ').') $false
        return $false
    }
    $staged = @((Invoke-Git $Repo @('diff', '--cached', '--name-only')).Output | Where-Object { $_ -ne '' } | ForEach-Object { $_ -replace '/', '\' })
    if (-not (Get-SetEqual @($staged) @($commitFiles))) {
        Set-ReleaseBlocked $State 'AUDITING' ('Il set staged non corrisponde a quello atteso: ' + ((@($staged)) -join ', ')) $false
        return $false
    }

    $msgDir = Join-Path $State.workflowDir 'evidence\audit'
    Ensure-Directory $msgDir
    $msgFile = Join-Path $msgDir 'commit-message.txt'
    [System.IO.File]::WriteAllText($msgFile, ('Release preparation: ' + $script:ProductName + ' ' + $State.version),
        (New-Object System.Text.UTF8Encoding($false)))
    $commit = Invoke-Git $Repo @('commit', '-F', $msgFile)
    if ($commit.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'AUDITING' ('git commit fallito (codice ' + $commit.ExitCode + '). L''indice e'' rimasto intatto per l''ispezione.') $false
        return $false
    }
    $newHead = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Output
    $commitHash = $newHead[0]
    $showFiles = @((Invoke-Git $Repo @('show', '--name-only', '--format=', $commitHash)).Output | Where-Object { $_ -ne '' } | ForEach-Object { $_ -replace '/', '\' })
    if (-not (Get-SetEqual @($showFiles) @($commitFiles))) {
        Set-ReleaseBlocked $State 'AUDITING' 'Il commit contiene un set di file diverso da quello atteso.' $false
        return $false
    }
    Add-Member -InputObject $State.candidate -NotePropertyName releaseCommit -NotePropertyValue $commitHash -Force

    $diffOut = Invoke-Git $Repo @('diff', ($head0 + '..' + $commitHash))
    [System.IO.File]::WriteAllLines((Join-Path $msgDir 'diff-vs-baseline.txt'), @($diffOut.Output), (New-Object System.Text.UTF8Encoding($false)))
    $statOut = Invoke-Git $Repo @('diff', '--stat', ($head0 + '..' + $commitHash))
    [System.IO.File]::WriteAllLines((Join-Path $msgDir 'diff-stat.txt'), @($statOut.Output), (New-Object System.Text.UTF8Encoding($false)))

    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ('AUDITING | commit ' + $commitHash + ' | file: ' + ($commitFiles -join ', '))
    Write-RmOk ('Commit locale creato: ' + $commitHash)
    return $true
}
