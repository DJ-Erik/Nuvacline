# ============================================================================
# Nuvacline Release Manager - ripristino (resume), approvazione, ingresso
# Richiede tutti i moduli precedenti (dot-source in tools\release.ps1).
# ============================================================================

function Resume-PreflightChecks([object]$State, [string]$Repo) {
    $root = Split-Path $State.workflowDir -Parent
    $old = Get-CurrentProductVersion $Repo
    $pf = Invoke-PreflightChecks $State $Repo $root $old
    if (-not $State.baseline.head) {
        $State.baseline = New-Object psobject -Property @{ head = $pf.Snapshot.Head; branch = $pf.Snapshot.Branch }
    } elseif ($pf.Snapshot.Head -ne $State.baseline.head) {
        Set-ReleaseBlocked $State 'PREFLIGHT' ('HEAD del repository cambiato dopo l''avvio del workflow (' + $State.baseline.head + ' -> ' + $pf.Snapshot.Head + ').') $false
        return $false
    }
    Save-ReleaseState $State
    if ($pf.Fails.Count -gt 0) {
        Set-ReleaseBlocked $State 'PREFLIGHT' ($pf.Fails -join '; ') $true
        return $false
    }
    Write-RmOk 'Preflight superato.'
    return $true
}

function Invoke-PublishResume([object]$State, [string]$Repo, [string]$GitHubRepo) {
    if ($State.status -eq 'HUMAN_TEST_APPROVED') {
        Set-ReleaseStatus $State 'PUBLISHING' 'fase' 'Pubblicazione controllata.'
    } elseif ($State.status -ne 'PUBLISHING') {
        Write-RmFail ('Stato non pubblicabile: ' + $State.status)
        return 2
    }
    if (Invoke-PublishPhase $State $Repo $GitHubRepo) { return 0 }
    return 2
}

function Invoke-NewRelease([string]$Version, [string]$Repo, [string]$WorkflowRoot, [string[]]$IncludeFiles, [string]$NotesFile) {
    $state = New-ReleaseState $Repo $WorkflowRoot $Version $IncludeFiles
    Write-RmOk ('Cartella workflow: ' + $state.workflowDir)
    if (-not (Set-NotesFileIfAny $state $NotesFile)) { return 2 }
    Set-ReleaseStatus $state 'PREFLIGHT' 'fase' 'Verifiche preliminari.'
    $old = Get-CurrentProductVersion $Repo
    $pf = Invoke-PreflightChecks $state $Repo $WorkflowRoot $old
    $state.baseline = New-Object psobject -Property @{ head = $pf.Snapshot.Head; branch = $pf.Snapshot.Branch }
    Save-ReleaseState $state
    if ($pf.Fails.Count -gt 0) {
        Set-ReleaseBlocked $state 'PREFLIGHT' ($pf.Fails -join '; ') $true
        return 2
    }
    Write-RmOk 'Preflight superato.'
    if (-not (Invoke-PhaseChain $state $Repo 'BUILDING')) { return 2 }
    return 0
}

function Invoke-ResumeFlow {
    param(
        [string]$WorkflowRoot,
        [string]$StateFile,
        [switch]$ApproveHumanTest,
        [switch]$DryRun,
        [switch]$NoPublish,
        [string]$NotesFile,
        [string]$GitHubRepo
    )
    $path = Find-StatePath $WorkflowRoot $StateFile
    $loaded = Load-ReleaseStateFile $path
    $state = $loaded.State
    if ($loaded.Warning) { Write-RmWarn $loaded.Warning }
    $repo = [string]$state.repoPath
    if (-not (Test-Path -LiteralPath $repo)) {
        Write-RmFail ('Repository del workflow non trovato: ' + $repo)
        return 2
    }
    Write-RmInfo ('Stato attuale: ' + $state.status + ' | versione ' + $state.version)
    Write-RmInfo ('Workflow: ' + $state.workflowDir)
    if ($DryRun) { Show-ResumeDryRun $state; return 0 }
    Add-RmJournal $state.workflowDir ('RESUME | stato iniziale ' + $state.status)
    if (-not (Set-NotesFileIfAny $state $NotesFile)) { return 2 }

    if ($state.status -eq 'DISTRIBUTED') {
        Write-RmOk ('Release gia'' distribuita: ' + $state.publish.releaseUrl)
        return 0
    }

    if ($ApproveHumanTest) {
        if ($state.status -eq 'BLOCKED') {
            if (-not $state.blocked.recoverable) {
                Write-RmFail ('Blocco non ripristinabile: ' + $state.blocked.reason)
                return 2
            }
            Set-ReleaseStatus $state ([string]$state.blocked.phase) 'ripresa' 'Ripresa per approvazione dopo blocco.'
        }
        if (($state.status -eq 'READY_FOR_HUMAN_TEST') -or ($state.status -eq 'AWAITING_HUMAN_APPROVAL')) {
            if (-not (Invoke-ApprovalStep $state $repo)) { return 2 }
        } elseif (($state.status -ne 'HUMAN_TEST_APPROVED') -and ($state.status -ne 'PUBLISHING')) {
            Write-RmFail ('Stato non approvabile: ' + $state.status + '. Serve un workflow in attesa di test umano.')
            return 2
        }
        if ($NoPublish) {
            $state.publish.status = 'SKIPPED_LOCAL'
            Save-ReleaseState $state
            Add-RmJournal $state.workflowDir 'PUBLISHING | saltata su richiesta (-NoPublish)'
            Write-RmOk 'Approvazione registrata; pubblicazione remota saltata (-NoPublish).'
            return 0
        }
        return (Invoke-PublishResume $state $repo $GitHubRepo)
    }

    switch ($state.status) {
        'READY_FOR_HUMAN_TEST' {
            $integ = Test-CandidateIntegrity $state $repo
            if (-not $integ.Ok) {
                Set-ReleaseBlocked $state 'AWAITING_HUMAN_APPROVAL' ('candidata modificata dopo la preparazione: ' + ($integ.Reasons -join '; ')) $false
                return 2
            }
            Show-GateSummary $state
            return 0
        }
        'AWAITING_HUMAN_APPROVAL' {
            $integ = Test-CandidateIntegrity $state $repo
            if (-not $integ.Ok) {
                Set-ReleaseBlocked $state 'AWAITING_HUMAN_APPROVAL' ('candidata modificata dopo la preparazione: ' + ($integ.Reasons -join '; ')) $false
                return 2
            }
            Show-GateSummary $state
            return 0
        }
        'HUMAN_TEST_APPROVED' {
            if ($NoPublish) {
                Write-RmInfo 'Approvazione gia'' registrata; -NoPublish attivo: nessuna pubblicazione.'
                return 0
            }
            return (Invoke-PublishResume $state $repo $GitHubRepo)
        }
        'PUBLISHING' { return (Invoke-PublishResume $state $repo $GitHubRepo) }
        'BLOCKED' {
            if (-not $state.blocked.recoverable) {
                Write-RmFail ('Blocco non ripristinabile in ' + $state.blocked.phase + ': ' + $state.blocked.reason)
                return 2
            }
            $phase = [string]$state.blocked.phase
            Write-RmInfo ('Ripresa dopo blocco: si riparte dalla fase ' + $phase + '.')
            Set-ReleaseStatus $state $phase 'ripresa' ('Ripresa dopo blocco: ' + $state.blocked.reason)
            if ($phase -eq 'PUBLISHING') { return (Invoke-PublishResume $state $repo $GitHubRepo) }
            if ($phase -eq 'PREFLIGHT') {
                if (-not (Resume-PreflightChecks $state $repo)) { return 2 }
                if (-not (Invoke-PhaseChain $state $repo 'BUILDING')) { return 2 }
                return 0
            }
            if (($phase -eq 'READY_FOR_HUMAN_TEST') -or ($phase -eq 'AWAITING_HUMAN_APPROVAL')) {
                Show-GateSummary $state
                return 0
            }
            if ($phase -eq 'HUMAN_TEST_APPROVED') { return (Invoke-PublishResume $state $repo $GitHubRepo) }
            if (-not (Invoke-PhaseChain $state $repo $phase)) { return 2 }
            return 0
        }
        'INITIALIZING' {
            Set-ReleaseStatus $state 'PREFLIGHT' 'fase' 'Verifiche preliminari.'
            if (-not (Resume-PreflightChecks $state $repo)) { return 2 }
            if (-not (Invoke-PhaseChain $state $repo 'BUILDING')) { return 2 }
            return 0
        }
        'PREFLIGHT' {
            if (-not (Resume-PreflightChecks $state $repo)) { return 2 }
            if (-not (Invoke-PhaseChain $state $repo 'BUILDING')) { return 2 }
            return 0
        }
        default {
            if (-not (Invoke-PhaseChain $state $repo ([string]$state.status))) { return 2 }
            return 0
        }
    }
}

function Invoke-ReleaseWorkflow {
    param(
        [string]$Version,
        [switch]$Resume,
        [switch]$ApproveHumanTest,
        [switch]$DryRun,
        [switch]$NoPublish,
        [string]$RepoRoot,
        [string]$WorkflowRoot,
        [string]$StateFile,
        [string[]]$IncludeFiles = @(),
        [string]$NotesFile,
        [string]$GitHubRepo
    )
    if (-not $Version -and -not $Resume) { Show-ReleaseUsage; return 1 }
    if ($Version -and $Resume) { Write-RmFail 'Usare -Version oppure -Resume, non entrambi.'; Show-ReleaseUsage; return 1 }
    if ($ApproveHumanTest -and -not $Resume) { Write-RmFail '-ApproveHumanTest richiede -Resume.'; return 1 }
    if ($Version -and ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$')) {
        Write-RmFail ('Versione non valida: ' + $Version + ' (atteso X.Y.Z).')
        return 1
    }
    try {
        $repo = Resolve-RepoRoot $RepoRoot
        if (-not $WorkflowRoot) { $WorkflowRoot = Join-Path (Split-Path $repo -Parent) 'Nuvacline-Release-Workflow' }
        if ($Resume) {
            return (Invoke-ResumeFlow -WorkflowRoot $WorkflowRoot -StateFile $StateFile -ApproveHumanTest:$ApproveHumanTest -DryRun:$DryRun -NoPublish:$NoPublish -NotesFile $NotesFile -GitHubRepo $GitHubRepo)
        }
        if ($DryRun) { return (Show-NewDryRun $repo $WorkflowRoot $Version $IncludeFiles) }
        return (Invoke-NewRelease $Version $repo $WorkflowRoot $IncludeFiles $NotesFile)
    } catch {
        Write-RmFail ('Errore: ' + $_.Exception.Message)
        return 2
    }
}
