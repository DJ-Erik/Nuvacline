# ============================================================================
# Nuvacline Release Manager - flusso principale, stato, dry-run, gate umano
# Richiede: release-lib.ps1, release-prep.ps1, release-phases.ps1,
#           release-package.ps1 (dot-source precedenti).
# ============================================================================

function Show-ReleaseUsage {
    Write-Host ''
    Write-Host 'Nuvacline Release Manager'
    Write-Host ''
    Write-Host '  .\tools\release.ps1 -Version X.Y.Z              nuova release (fasi locali + gate umano)'
    Write-Host '  .\tools\release.ps1 -Version X.Y.Z -DryRun      mostra il piano senza modificare nulla'
    Write-Host '  .\tools\release.ps1 -Resume                     riprende l''ultimo workflow o mostra i dettagli'
    Write-Host '  .\tools\release.ps1 -Resume -DryRun             riprende in sola lettura'
    Write-Host '  .\tools\release.ps1 -Resume -ApproveHumanTest   registra l''approvazione e pubblica'
    Write-Host ''
    Write-Host 'Opzioni:'
    Write-Host '  -NoPublish            con -ApproveHumanTest: salta tag/push/release (resta locale)'
    Write-Host '  -IncludeFiles a,b     file extra da includere nel commit di release'
    Write-Host '  -NotesFile path       note di release personalizzate (default: generate)'
    Write-Host '  -GitHubRepo owner/repo   override del repository GitHub'
    Write-Host '  -RepoRoot path        radice del repository (default: rilevata)'
    Write-Host '  -WorkflowRoot path    radice dei workflow (default: <repo>..\Nuvacline-Release-Workflow)'
    Write-Host '  -StateFile path       stato da riprendere (default: il piu'' recente)'
    Write-Host ''
    Write-Host 'Codici di uscita: 0 = completato o in attesa di test umano; 1 = errore di uso; 2 = bloccato o errore.'
}

function New-ReleaseState([string]$Repo, [string]$WorkflowRoot, [string]$Version, [string[]]$IncludeFiles) {
    $stamp = Get-RmStampDir
    $dir = Join-Path $WorkflowRoot ($stamp + '-' + $Version)
    $n = 1
    while (Test-Path -LiteralPath $dir) {
        $dir = Join-Path $WorkflowRoot ($stamp + '-' + $Version + '-r' + $n)
        $n++
    }
    Ensure-Directory $dir
    $remoteUrl = ''
    $remote = (Invoke-Git $Repo @('remote', 'get-url', 'origin')).Output
    if (@($remote).Count -gt 0 -and $remote[0]) { $remoteUrl = [string]$remote[0] }
    $state = New-Object psobject -Property @{
        schemaVersion = $script:RmSchemaVersion
        status = 'INITIALIZING'
        version = $Version
        expectedTag = 'v' + $Version
        workflowDir = $dir
        repoPath = $Repo
        remoteUrl = $remoteUrl
        createdAt = Get-RmTimestamp
        updatedAt = Get-RmTimestamp
        includeFiles = @($IncludeFiles)
        history = @()
        baseline = New-Object psobject -Property @{ head = ''; branch = '' }
        versionUpdate = $null
        candidate = $null
        tests = New-Object psobject -Property @{ exitCode = $null; total = $null; passed = $null; failed = $null; log = '' }
        verifications = New-Object psobject -Property @{ verifyExitCode = $null; verifyLog = '' }
        publish = New-Object psobject -Property @{ status = 'PENDING'; releaseUrl = ''; assetName = ''; assetDigest = ''; tagObject = ''; tagPushedTo = '' }
        humanTest = New-Object psobject -Property @{ status = 'PENDING'; approvedAt = ''; approvedZipSha256 = ''; approvedExeSha256 = '' }
        releaseNotesPath = ''
        releaseFiles = @()
        remoteLatest = ''
        protected = @()
        blocked = $null
    }
    Add-RmJournal $dir ('WORKFLOW ' + $Version + ' avviato | repo ' + $Repo)
    Add-RmJournal $dir ('FASE INITIALIZING | cartella ' + $dir)
    Save-ReleaseState $state
    return $state
}

function Set-NotesFileIfAny([object]$State, [string]$NotesFile) {
    if (-not $NotesFile) { return $true }
    if (-not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
        Write-RmFail ('File di note non trovato: ' + $NotesFile)
        return $false
    }
    $State.releaseNotesPath = (Resolve-Path -LiteralPath $NotesFile).Path
    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ('NOTE | ' + $State.releaseNotesPath)
    return $true
}

function Find-StatePath([string]$WorkflowRoot, [string]$StateFile) {
    if ($StateFile) {
        if (-not (Test-Path -LiteralPath $StateFile -PathType Leaf)) { throw ('File di stato non trovato: ' + $StateFile) }
        return $StateFile
    }
    if (-not (Test-Path -LiteralPath $WorkflowRoot)) { throw ('Nessun workflow trovato in: ' + $WorkflowRoot) }
    $dirs = @(Get-ChildItem -LiteralPath $WorkflowRoot -Directory | Sort-Object Name -Descending)
    foreach ($d in $dirs) {
        $sf = Join-Path $d.FullName 'release-state.json'
        if (Test-Path -LiteralPath $sf) { return $sf }
    }
    throw ('Nessuno stato di release trovato in: ' + $WorkflowRoot)
}

function Show-GateSummary([object]$State) {
    Write-RmPhase 'CANDIDATA PRONTA - TEST UMANO RICHIESTO'
    Write-RmInfo ('Versione: ' + $State.version + ' | commit: ' + $State.candidate.releaseCommit)
    Write-RmInfo ('Pacchetto: ' + $State.candidate.zipPath)
    Write-RmInfo ('SHA-256 ZIP: ' + $State.candidate.zipSha256)
    Write-RmInfo ('SHA-256 exe: ' + $State.candidate.exeSha256)
    if ($null -ne $State.tests.total) {
        Write-RmInfo ('Test: ' + $State.tests.passed + '/' + $State.tests.total + ' superati (exit ' + $State.tests.exitCode + ')')
    }
    Write-RmInfo ('Checklist: ' + (Join-Path $State.workflowDir 'HUMAN-TEST.md'))
    Write-RmInfo 'Nessuna pubblicazione verra'' eseguita senza approvazione esplicita.'
    Write-RmOk 'Approvare con: .\tools\release.ps1 -Resume -ApproveHumanTest'
}

function Show-NewDryRun([string]$Repo, [string]$WorkflowRoot, [string]$Version, [string[]]$IncludeFiles) {
    Write-RmPhase ('DRY RUN per Nuvacline ' + $Version + ' - nessuna modifica verra'' eseguita')
    $snap = Get-GitSnapshot $Repo
    Write-RmInfo ('Repository: ' + $Repo)
    Write-RmInfo ('Branch: ' + $snap.Branch + ' | HEAD: ' + $snap.Head)
    Write-RmInfo ('Staged: ' + $snap.Staged.Count + ' | non in staging: ' + $snap.Unstaged.Count + ' | non tracciati: ' + $snap.Untracked.Count)
    $old = Get-CurrentProductVersion $Repo
    Write-RmInfo ('Versione corrente: ' + $old + ' -> target: ' + $Version)
    $latest = Get-LatestReleasedVersion $Repo
    if ($latest.Version) {
        Write-RmInfo ('Ultima versione rilasciata (tag locali/remoti): ' + $latest.Version)
        if (-not ([version]$Version -gt [version]$latest.Version)) {
            Write-RmFail ('La versione target non e'' maggiore dell''ultima rilasciata (' + $latest.Version + ').')
        }
    } elseif (-not $latest.RemoteOk) {
        Write-RmWarn 'ls-remote non raggiungibile: controllo limitato ai tag locali.'
    } else {
        Write-RmInfo 'Nessuna versione precedente trovata.'
    }
    $protected = Get-ProtectedArtifactReport $Repo
    $okCount = @($protected | Where-Object { $_.Status -eq 'OK' }).Count
    $bad = @($protected | Where-Object { $_.Status -eq 'DIVERSO' })
    $missing = @($protected | Where-Object { $_.Status -eq 'ASSENTE' }).Count
    Write-RmInfo ('Artefatti protetti: ' + $okCount + ' OK, ' + $bad.Count + ' diversi, ' + $missing + ' assenti')
    foreach ($b in $bad) { Write-RmFail ('Artefatto protetto modificato: ' + $b.Path) }
    $conflicts = Get-ActiveWorkflowConflict $WorkflowRoot $Version $null
    foreach ($c in $conflicts) { Write-RmFail ('Workflow attivo per la stessa versione: ' + $c) }
    Write-RmInfo ('Cartella prevista: ' + (Join-Path $WorkflowRoot ((Get-RmStampDir) + '-' + $Version)))
    Write-RmInfo 'Fasi: PREFLIGHT -> BUILDING -> TESTING -> AUDITING -> PACKAGING -> gate umano'
    Write-RmInfo ('File gestiti: ' + $script:ManagedReleaseFiles.Count + ' | inclusi extra: ' + (@($IncludeFiles) -join ', '))
    Write-RmInfo ('Commit previsto: Release preparation: ' + $script:ProductName + ' ' + $Version)
    Write-RmInfo 'Poi HUMAN-TEST.md + pacchetto verificato; pubblicazione solo con -Resume -ApproveHumanTest.'
    Write-RmInfo 'Nessuna modifica e'' stata eseguita.'
    return 0
}

function Show-ResumeDryRun([object]$State) {
    Write-RmPhase ('DRY RUN - stato attuale: ' + $State.status + ' (nessuna azione)')
    Write-RmInfo ('Workflow: ' + $State.workflowDir)
    Write-RmInfo ('Versione: ' + $State.version + ' | repo: ' + $State.repoPath)
    if ($State.candidate -and $State.candidate.releaseCommit) {
        Write-RmInfo ('Commit di release: ' + $State.candidate.releaseCommit)
    }
    if ($State.candidate -and $State.candidate.zipSha256) {
        Write-RmInfo ('ZIP: ' + $State.candidate.zipPath + ' | sha256 ' + $State.candidate.zipSha256)
    }
    if ($State.status -eq 'BLOCKED') {
        Write-RmInfo ('Blocco in ' + $State.blocked.phase + ': ' + $State.blocked.reason)
        if ($State.blocked.recoverable) {
            Write-RmInfo ('Azioni possibili: correggere e rilanciare -Resume (riprende da ' + $State.blocked.phase + ').')
        } else {
            Write-RmInfo 'Blocco NON ripristinabile: richiede intervento manuale.'
        }
    } elseif ($State.status -eq 'DISTRIBUTED') {
        Write-RmInfo 'Release gia'' distribuita; nessuna azione possibile.'
    } elseif (($State.status -eq 'READY_FOR_HUMAN_TEST') -or ($State.status -eq 'AWAITING_HUMAN_APPROVAL')) {
        Write-RmInfo 'Azioni possibili: eseguire il test umano (HUMAN-TEST.md), poi -Resume -ApproveHumanTest.'
    } elseif ($State.status -eq 'HUMAN_TEST_APPROVED') {
        Write-RmInfo 'Approvazione registrata: -Resume pubblicherebbe la release.'
    } elseif ($State.status -eq 'PUBLISHING') {
        Write-RmInfo 'Pubblicazione interrotta: -Resume riprende la pubblicazione.'
    } else {
        Write-RmInfo ('Azioni possibili: -Resume riprende dalla fase ' + $State.status + '.')
    }
}

function Invoke-PhaseChain([object]$State, [string]$Repo, [string]$StartPhase) {
    $order = @('BUILDING', 'TESTING', 'AUDITING', 'PACKAGING')
    $active = $false
    foreach ($phase in $order) {
        if (-not $active) {
            if ($phase -eq $StartPhase) { $active = $true } else { continue }
        }
        if ($State.status -ne $phase) {
            Set-ReleaseStatus $State $phase 'fase' ('Avvio fase ' + $phase)
        }
        $ok = $false
        if ($phase -eq 'BUILDING') { $ok = Invoke-BuildPhase $State $Repo }
        elseif ($phase -eq 'TESTING') { $ok = Invoke-TestPhase $State $Repo }
        elseif ($phase -eq 'AUDITING') { $ok = Invoke-AuditPhase $State $Repo }
        elseif ($phase -eq 'PACKAGING') { $ok = Invoke-PackagePhase $State $Repo }
        if (-not $ok) { return $false }
    }
    Set-ReleaseStatus $State 'READY_FOR_HUMAN_TEST' 'congelamento' 'Candidata pronta; nessuna pubblicazione eseguita.'
    Set-ReleaseStatus $State 'AWAITING_HUMAN_APPROVAL' 'gate' 'In attesa di approvazione umana esplicita.'
    Show-GateSummary $State
    return $true
}
