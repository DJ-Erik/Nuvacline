# ============================================================================
# Nuvacline Release Manager - integrita' candidata, approvazione, PUBLISHING
# Richiede: release-lib.ps1, release-prep.ps1, release-phases.ps1, release-package.ps1.
# ============================================================================

function Test-CandidateIntegrity([object]$State, [string]$Repo) {
    $reasons = @()
    if (-not $State.candidate) {
        $reasons += 'candidata non preparata'
        return (New-Object psobject -Property @{ Ok = $false; Reasons = @($reasons) })
    }
    $head = (Invoke-Git $Repo @('rev-parse', 'HEAD')).Output
    if (@($head).Count -eq 0 -or $head[0] -ne [string]$State.candidate.releaseCommit) {
        $reasons += 'HEAD del repository diverso dal commit di release'
    }
    $zip = [string]$State.candidate.zipPath
    if (-not (Test-Path -LiteralPath $zip)) {
        $reasons += 'ZIP della candidata assente'
    } elseif ((Get-Sha256 $zip) -ne [string]$State.candidate.zipSha256) {
        $reasons += 'hash del ZIP diverso da quello approvabile'
    }
    $stage = Join-Path $State.workflowDir ('package\Nuvacline-' + $State.version + '-Windows')
    $exe = Join-Path $stage 'Nuvacline.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        $reasons += 'eseguibile impacchettato assente'
    } elseif ((Get-Sha256 $exe) -ne [string]$State.candidate.exeSha256) {
        $reasons += 'hash dell''eseguibile impacchettato diverso'
    }
    foreach ($pf in @($State.candidate.packageFiles)) {
        $f = Join-Path $stage $pf.name
        if (-not (Test-Path -LiteralPath $f)) { $reasons += ('file di pacchetto assente: ' + $pf.name); continue }
        if ((Get-Sha256 $f) -ne $pf.sha256) { $reasons += ('hash diverso per: ' + $pf.name) }
    }
    return (New-Object psobject -Property @{ Ok = ($reasons.Count -eq 0); Reasons = @($reasons) })
}

function New-FinalReportContent([object]$State) {
    $nl = [Environment]::NewLine
    $t = @()
    $t += ('# Report finale - Nuvacline ' + $State.version)
    $t += ''
    $t += ('Stato: ' + $State.status)
    $t += ('Workflow: ' + $State.workflowDir)
    $t += ('Creata: ' + $State.createdAt + ' - conclusa: ' + (Get-RmTimestamp))
    $t += ''
    $t += '## Identita'' della release'
    $t += ''
    $t += ('- Commit di release: ' + $State.candidate.releaseCommit)
    $t += ('- Tag atteso: ' + $State.expectedTag)
    $t += ('- SHA-256 eseguibile: ' + $State.candidate.exeSha256)
    $t += ('- SHA-256 ZIP: ' + $State.candidate.zipSha256 + ' (' + $State.candidate.zipBytes + ' byte)')
    $t += ('- Pacchetto: ' + $State.candidate.zipPath)
    $t += ''
    $t += '## Verifiche'
    $t += ''
    $tot = $State.tests.total; $pas = $State.tests.passed
    $t += ('- Suite di test: exit ' + $State.tests.exitCode + ' (Totale ' + $tot + ', Superati ' + $pas + ')')
    $t += ('- verify.ps1: exit ' + $State.verifications.verifyExitCode)
    $t += ('- Artefatti protetti: verificati prima e dopo ogni fase')
    $t += ''
    $t += '## Pubblicazione'
    $t += ''
    $t += ('- Esito: ' + $State.publish.status)
    if ($State.publish.releaseUrl) { $t += ('- Release: ' + $State.publish.releaseUrl) }
    if ($State.publish.assetDigest) { $t += ('- Asset: ' + $State.publish.assetName + ' (' + $State.publish.assetDigest + ')') }
    $t += ''
    $t += '## Cronologia'
    $t += ''
    foreach ($h in @($State.history)) { $t += ('- ' + $h.at + '  ' + $h.action + '  ' + $h.detail) }
    return (($t -join $nl) + $nl)
}

function Invoke-ApprovalStep([object]$State, [string]$Repo) {
    Write-RmPhase 'APPROVAZIONE TEST UMANO'
    $integrity = Test-CandidateIntegrity $State $Repo
    if (-not $integrity.Ok) {
        Set-ReleaseBlocked $State 'AWAITING_HUMAN_APPROVAL' ('candidata modificata dopo la preparazione: ' + ($integrity.Reasons -join '; ')) $false
        return $false
    }
    if ($State.status -eq 'READY_FOR_HUMAN_TEST') {
        Set-ReleaseStatus $State 'AWAITING_HUMAN_APPROVAL' 'gate' 'In attesa di approvazione umana.'
    }
    $State.humanTest.status = 'APPROVED'
    $State.humanTest.approvedAt = Get-RmTimestamp
    $State.humanTest.approvedZipSha256 = [string]$State.candidate.zipSha256
    $State.humanTest.approvedExeSha256 = [string]$State.candidate.exeSha256
    Set-ReleaseStatus $State 'HUMAN_TEST_APPROVED' 'approvazione' ('Approvazione esplicita; commit ' + $State.candidate.releaseCommit + ', exe ' + $State.candidate.exeSha256)
    Write-RmOk 'Approvazione registrata: la candidata corrisponde a quella preparata.'
    return $true
}

function Invoke-PublishPhase([object]$State, [string]$Repo, [string]$GitHubRepo) {
    Write-RmPhase 'PUBLISHING - pubblicazione controllata'
    $integrity = Test-CandidateIntegrity $State $Repo
    if (-not $integrity.Ok) {
        Set-ReleaseBlocked $State 'PUBLISHING' ('integrita'' candidata fallita: ' + ($integrity.Reasons -join '; ')) $false
        return $false
    }

    $ev = Join-Path $State.workflowDir 'evidence\publish'
    Ensure-Directory $ev

    $ghProbe = Invoke-Gh @('auth', 'status') $null
    if ($ghProbe.ExitCode -ne 0) {
        $State.publish.status = 'GH_UNAVAILABLE'
        Save-ReleaseState $State
        Set-ReleaseBlocked $State 'PUBLISHING' 'GitHub CLI non disponibile o non autenticata (gh auth status fallito). La release resta pronta localmente: rilanciare -Resume quando gh sara'' disponibile.' $true
        return $false
    }

    Write-RmInfo 'Fetch dal remote...'
    $fetch = Invoke-Git $Repo @('fetch', 'origin') (Join-Path $ev 'fetch.log')
    if ($fetch.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'PUBLISHING' 'Fetch dal remote fallito (rete o credenziali).' $true
        return $false
    }

    $commit = [string]$State.candidate.releaseCommit
    $tag = [string]$State.expectedTag
    $remoteMaster = (Invoke-Git $Repo @('rev-parse', 'origin/master')).Output
    if (@($remoteMaster).Count -gt 0 -and $remoteMaster[0] -ne '') {
        if ($remoteMaster[0] -ne $commit) {
            $anc = Invoke-Git $Repo @('merge-base', '--is-ancestor', $remoteMaster[0], $commit)
            if ($anc.ExitCode -ne 0) {
                Set-ReleaseBlocked $State 'PUBLISHING' ('Il remote master (' + $remoteMaster[0] + ') non e'' antenato del commit di release: aggiornamento manuale richiesto (nessun force push).') $false
                return $false
            }
        }
    }

    $tagLocal = (Invoke-Git $Repo @('tag', '--list', $tag)).Output
    if (@($tagLocal).Count -gt 0 -and $tagLocal[0] -ne '') {
        $tagPeel = (Invoke-Git $Repo @('rev-list', '-n', '1', $tag)).Output
        if (@($tagPeel).Count -eq 0 -or $tagPeel[0] -ne $commit) {
            Set-ReleaseBlocked $State 'PUBLISHING' ('Il tag locale ' + $tag + ' esiste ma punta altrove: conflitto, pubblicazione annullata.') $false
            return $false
        }
    } else {
        $msgFile = Join-Path $ev 'tag-message.txt'
        [System.IO.File]::WriteAllText($msgFile, ($script:ProductName + ' ' + $State.version + ' - release Windows'),
            (New-Object System.Text.UTF8Encoding($false)))
        $tagRes = Invoke-Git $Repo @('tag', '-a', $tag, $commit, '-F', $msgFile) (Join-Path $ev 'tag.log')
        if ($tagRes.ExitCode -ne 0) {
            Set-ReleaseBlocked $State 'PUBLISHING' 'Creazione del tag annotato fallita.' $false
            return $false
        }
    }
    $State.publish.tagObject = ((Invoke-Git $Repo @('rev-parse', $tag)).Output[0])
    Save-ReleaseState $State

    $remoteTag = (Invoke-Git $Repo @('ls-remote', 'origin', ('refs/tags/' + $tag), ('refs/tags/' + $tag + '^{}'))).Output
    $remoteTagPeel = $null
    foreach ($line in @($remoteTag)) {
        if ($line -match ('refs/tags/' + [regex]::Escape($tag) + '\^\{\}')) { $remoteTagPeel = ($line -split '\s+')[0] }
    }
    if ($remoteTagPeel) {
        if ($remoteTagPeel -ne $commit) {
            Set-ReleaseBlocked $State 'PUBLISHING' ('Il tag remoto ' + $tag + ' punta a un commit diverso: conflitto, nessuna sovrascrittura.') $false
            return $false
        }
    } elseif (@($remoteTag).Count -gt 0 -and @($remoteTag)[0] -ne '') {
        Set-ReleaseBlocked $State 'PUBLISHING' ('Il tag remoto ' + $tag + ' esiste (non annotato): conflitto, pubblicazione annullata.') $false
        return $false
    } else {
        Write-RmInfo 'Push del commit di release...'
        if ($remoteMaster[0] -ne $commit) {
            $push = Invoke-Git $Repo @('push', 'origin', 'refs/heads/master:refs/heads/master') (Join-Path $ev 'push.log')
            if ($push.ExitCode -ne 0) {
                Set-ReleaseBlocked $State 'PUBLISHING' 'Push del branch master fallito (nessun force push).' $true
                return $false
            }
        }
        Write-RmInfo 'Push del tag...'
        $pushTag = Invoke-Git $Repo @('push', 'origin', ('refs/tags/' + $tag)) (Join-Path $ev 'push-tag.log')
        if ($pushTag.ExitCode -ne 0) {
            Set-ReleaseBlocked $State 'PUBLISHING' 'Push del tag fallito (nessun force push).' $true
            return $false
        }
    }
    $State.publish.tagPushedTo = 'origin'
    Save-ReleaseState $State

    $notes = [string]$State.releaseNotesPath
    $sums = Join-Path (Split-Path ([string]$State.candidate.zipPath) -Parent) ('SHA256SUMS-' + $State.version + '.txt')
    $lines = @(Get-Content -LiteralPath (Join-Path (Join-Path $State.workflowDir 'package\extract-verify') 'SHA256SUMS.txt') -Encoding ASCII)
    $zipHash = ([string]$State.candidate.zipSha256).ToLowerInvariant()
    $lines += ($zipHash + '  ' + (Split-Path ([string]$State.candidate.zipPath) -Leaf))
    [System.IO.File]::WriteAllLines($sums, $lines, (New-Object System.Text.UTF8Encoding($false)))

    $slug = $GitHubRepo
    if (-not $slug) { $slug = Get-RepoSlug ([string]$State.remoteUrl) }
    $viewArgs = @('release', 'view', $tag, '--json', 'tagName,isPrerelease,url,assets')
    $createArgs = @('release', 'create', $tag)
    if ($slug) {
        $viewArgs += @('-R', $slug)
        $createArgs += @('-R', $slug)
    }
    $view = Invoke-Gh $viewArgs (Join-Path $ev 'release-view-before.log')
    if ($view.ExitCode -eq 0) {
        $rv = ($view.Output -join "`n") | ConvertFrom-Json
        $asset = $null
        foreach ($a in @($rv.assets)) { if ($a.name -eq ('Nuvacline-' + $State.version + '-Windows.zip')) { $asset = $a } }
        $digestOk = ($asset -and ($asset.digest -eq ('sha256:' + $zipHash)))
        if ($rv.tagName -eq $tag -and $rv.isPrerelease -and $digestOk) {
            Write-RmWarn 'Release remota gia'' presente e corrispondente: nessuna creazione duplicata.'
            $State.publish.status = 'DONE'
            $State.publish.releaseUrl = $rv.url
            $State.publish.assetName = $asset.name
            $State.publish.assetDigest = $asset.digest
        } else {
            Set-ReleaseBlocked $State 'PUBLISHING' 'Esiste gia'' una release remota con contenuti differenti: conflitto, nessuna sovrascrittura.' $false
            return $false
        }
    } else {
        Write-RmInfo 'Creazione release GitHub (pre-release)...'
        $createArgs += @('--title', ($script:ProductName + ' v' + $State.version), '--notes-file', $notes, '--prerelease',
                         [string]$State.candidate.zipPath, $sums)
        $create = Invoke-Gh $createArgs (Join-Path $ev 'release-create.log')
        if ($create.ExitCode -ne 0) {
            Set-ReleaseBlocked $State 'PUBLISHING' 'Creazione della release GitHub fallita. Verificare il log release-create.log.' $true
            return $false
        }
        $view2 = Invoke-Gh $viewArgs (Join-Path $ev 'release-view-after.log')
        if ($view2.ExitCode -ne 0) {
            Set-ReleaseBlocked $State 'PUBLISHING' 'Release creata ma non verificabile dal remote.' $true
            return $false
        }
        $rv = ($view2.Output -join "`n") | ConvertFrom-Json
        $asset = $null
        foreach ($a in @($rv.assets)) { if ($a.name -eq ('Nuvacline-' + $State.version + '-Windows.zip')) { $asset = $a } }
        $ok = ($rv.tagName -eq $tag) -and $rv.isPrerelease -and $asset -and
              ($asset.digest -eq ('sha256:' + $zipHash)) -and ($asset.size -eq $State.candidate.zipBytes)
        if (-not $ok) {
            Set-ReleaseBlocked $State 'PUBLISHING' 'Verifica post-pubblicazione fallita: asset o tag difformi.' $false
            return $false
        }
        $State.publish.status = 'DONE'
        $State.publish.releaseUrl = $rv.url
        $State.publish.assetName = $asset.name
        $State.publish.assetDigest = $asset.digest
    }

    Set-ReleaseStatus $State 'DISTRIBUTED' 'pubblicazione' ('Release ' + $State.publish.releaseUrl)
    $reportPath = Join-Path $State.workflowDir 'reports\final-report.md'
    Ensure-Directory (Split-Path $reportPath -Parent)
    [System.IO.File]::WriteAllText($reportPath, (New-FinalReportContent $State), (New-Object System.Text.UTF8Encoding($false)))
    Write-RmOk ('Release pubblicata: ' + $State.publish.releaseUrl)
    Write-RmOk ('Report finale: ' + $reportPath)
    return $true
}
