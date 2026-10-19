# ============================================================================
# Test suite del Release Manager Nuvacline.
# Repository temporanei e stub: nessuna rete, nessuna pubblicazione reale.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools\tests\release-manager.Tests.ps1
# Solo ASCII (PowerShell 5.1 legge i .ps1 senza BOM come ANSI).
# ============================================================================

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0
$script:Failures = @()
$script:ToolsDir = Split-Path $PSScriptRoot -Parent
$script:ReleaseScript = Join-Path $script:ToolsDir 'release.ps1'
$script:TmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'opencode\rm-tests'
. (Join-Path $PSScriptRoot 'rm-fixtures.ps1')

if (Test-Path -LiteralPath $script:TmpRoot) { Remove-Item -LiteralPath $script:TmpRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $script:TmpRoot | Out-Null
$script:GhStub = New-GhStub $script:TmpRoot

Write-Host '== Suite Release Manager Nuvacline =='
Write-Host ('Script: ' + $script:ReleaseScript)
Write-Host ('Temp:   ' + $script:TmpRoot)

Run-Test 'T01 parametri non validi: exit 1' {
    $r = Invoke-Raw @()
    Assert-True ($r.ExitCode -eq 1) ('nessun argomento: exit ' + $r.ExitCode)
    $r = Invoke-Raw @('-Resume', '-Version', '1.1.0', '-RepoRoot', (Join-Path $script:TmpRoot 'inesistente'))
    Assert-True ($r.ExitCode -eq 1) ('Version+Resume: exit ' + $r.ExitCode)
    $r = Invoke-Raw @('-ApproveHumanTest', '-RepoRoot', (Join-Path $script:TmpRoot 'inesistente'))
    Assert-True ($r.ExitCode -eq 1) ('Approve senza Resume: exit ' + $r.ExitCode)
    $r = Invoke-Raw @('-Version', 'abc', '-RepoRoot', (Join-Path $script:TmpRoot 'inesistente'))
    Assert-True ($r.ExitCode -eq 1) ('versione non valida: exit ' + $r.ExitCode)
}

Run-Test 'T02 dry-run nuova release: nessuna scrittura' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't02')
    $porcBefore = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output)
    $r = Invoke-Rel $fx @('-Version', '1.1.0', '-DryRun')
    Assert-True ($r.ExitCode -eq 0) ('dry-run: exit ' + $r.ExitCode)
    Assert-True (-not (Test-Path -LiteralPath $fx.WfRoot)) 'dry-run non deve creare la cartella workflow'
    $porcAfter = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output)
    Assert-True (($porcBefore -join '|') -eq ($porcAfter -join '|')) 'dry-run ha modificato il repository'
    $head =     (Git-Cli  $fx.Repo @('rev-parse', 'HEAD')).Output[0]
    Assert-True ($head -eq $fx.Baseline) 'HEAD cambiato durante il dry-run'
}

Run-Test 'T03 flusso completo al gate, nessun commit/push estraneo' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't03')
    $st = Run-FlowToGate $fx
    $head =     (Git-Cli  $fx.Repo @('rev-parse', 'HEAD')).Output[0]
    Assert-True ($head -eq $st.candidate.releaseCommit) 'HEAD diverso dal commit di release'
    $count =     (Git-Cli  $fx.Repo @('rev-list', '--count', 'HEAD')).Output[0]
    Assert-True ($count -eq '2') ('commit totali: ' + $count)
    $subj =     (Git-Cli  $fx.Repo @('log', '-1', '--format=%s')).Output[0]
    Assert-True ($subj -eq 'Release preparation: Nuvacline 1.1.0') ('oggetto commit: ' + $subj)
    $porc = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output | Where-Object { $_ -ne '' })
    Assert-True ($porc.Count -eq 0) ('working tree sporco: ' + ($porc -join ', '))
    $files = @(    (Git-Cli  $fx.Repo @('show', '--name-only', '--format=', 'HEAD')).Output | Where-Object { $_ -ne '' })
    Assert-True ($files.Count -eq 10) ('file nel commit: ' + $files.Count)
    $remoteMaster =     (Git-Cli  $fx.Bare @('rev-parse', 'refs/heads/master')).Output[0]
    Assert-True ($remoteMaster -eq $fx.Baseline) 'il gate non deve aver pushato su master'
    $remoteTags = @(    (Git-Cli  $fx.Bare @('tag', '--list')).Output | Where-Object { $_ -ne '' })
    Assert-True (($remoteTags -join ',') -eq 'v1.0.0') ('tag remoti: ' + ($remoteTags -join ','))
    $tags = @(    (Git-Cli  $fx.Repo @('tag', '--list', 'v1.1.0')).Output | Where-Object { $_ -ne '' })
    Assert-True ($tags.Count -eq 0) 'tag v1.1.0 non deve esistere al gate'
    $wf = Get-WfDir $fx
    Assert-True (Test-Path -LiteralPath (Join-Path $wf 'HUMAN-TEST.md')) 'HUMAN-TEST.md assente'
    Assert-True (Test-Path -LiteralPath (Join-Path $wf 'release-journal.md')) 'journal assente'
    Assert-True (Test-Path -LiteralPath $st.candidate.zipPath) 'ZIP assente'
    $zipHash = (Get-FileHash -LiteralPath $st.candidate.zipPath -Algorithm SHA256).Hash
    Assert-True ($zipHash -eq $st.candidate.zipSha256) 'hash ZIP diverso dal file'
    Assert-True (($st.tests.total -eq 3) -and ($st.tests.passed -eq 3)) 'test attesi 3/3'
    Assert-True ($st.verifications.verifyExitCode -eq 0) 'verify.ps1 deve terminare con 0'
    $ghFiles = @(Get-ChildItem -LiteralPath $fx.GhState -File)
    Assert-True ($ghFiles.Count -eq 0) 'nessuna release remota al gate'
    $script:FxT03 = $fx
}

Run-Test 'T04 resume senza approvazione: nulla pubblicato' {
    $fx = $script:FxT03
    $r = Invoke-Rel $fx @('-Resume')
    Assert-True ($r.ExitCode -eq 0) ('resume: exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'AWAITING_HUMAN_APPROVAL') ('stato: ' + $st.status)
    $tags = @(    (Git-Cli  $fx.Repo @('tag', '--list', 'v1.1.0')).Output | Where-Object { $_ -ne '' })
    Assert-True ($tags.Count -eq 0) 'tag creato senza approvazione'
    $ghFiles = @(Get-ChildItem -LiteralPath $fx.GhState -File)
    Assert-True ($ghFiles.Count -eq 0) 'release remota creata senza approvazione'
}

Run-Test 'T05 candidata modificata dopo il gate: blocco' {
    $fx = $script:FxT03
    $wf = Get-WfDir $fx
    $lic = Join-Path $wf 'package\Nuvacline-1.1.0-Windows\LICENSE'
    Add-Content -LiteralPath $lic -Value 'manomissione'
    $r = Invoke-Rel $fx @('-Resume', '-ApproveHumanTest')
    Assert-True ($r.ExitCode -eq 2) ('approvazione su candidata manomessa: exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'BLOCKED') ('stato: ' + $st.status)
    Assert-True (-not $st.blocked.recoverable) 'il blocco deve essere non ripristinabile'
    $tags = @(    (Git-Cli  $fx.Repo @('tag', '--list', 'v1.1.0')).Output | Where-Object { $_ -ne '' })
    Assert-True ($tags.Count -eq 0) 'tag creato nonostante il blocco'
    $ghFiles = @(Get-ChildItem -LiteralPath $fx.GhState -File)
    Assert-True ($ghFiles.Count -eq 0) 'release creata nonostante il blocco'
}

Run-Test 'T06 test falliti: blocco e resume da checkpoint' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't06')
    $env:NUVACLINE_TEST_FIXTURE_FAIL = '1'
    $r = Invoke-Rel $fx @('-Version', '1.1.0')
    Remove-Item Env:\NUVACLINE_TEST_FIXTURE_FAIL -ErrorAction SilentlyContinue
    Assert-True ($r.ExitCode -eq 2) ('atteso blocco, exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'BLOCKED') ('stato: ' + $st.status)
    Assert-True ($st.blocked.phase -eq 'TESTING') ('fase bloccata: ' + $st.blocked.phase)
    Assert-True ($st.blocked.recoverable) 'il blocco dei test deve essere ripristinabile'
    $r2 = Invoke-Rel $fx @('-Resume')
    Assert-True ($r2.ExitCode -eq 0) ('resume: exit ' + $r2.ExitCode)
    $st2 = Get-State $fx
    Assert-True ($st2.status -eq 'AWAITING_HUMAN_APPROVAL') ('stato dopo resume: ' + $st2.status)
    Assert-True ($st2.tests.passed -eq 3) 'la suite deve essere rieseguita con successo'
    $script:FxT06 = $fx
}

Run-Test 'T07 stato corrotto: ripristino dal backup' {
    $fx = $script:FxT06
    $wf = Get-WfDir $fx
    $main = Join-Path $wf 'release-state.json'
    [System.IO.File]::WriteAllText($main, '{ corrotto', (New-Object System.Text.UTF8Encoding($false)))
    $r = Invoke-Rel $fx @('-Resume')
    Assert-True ($r.ExitCode -eq 0) ('resume con stato corrotto: exit ' + $r.ExitCode)
    $out = $r.Output -join ' '
    Assert-True ($out.Contains('corrotto')) 'atteso avviso di ripristino dal backup'
    $st = Get-State $fx
    Assert-True (($st.status -eq 'AWAITING_HUMAN_APPROVAL') -or ($st.status -eq 'READY_FOR_HUMAN_TEST')) ('stato dopo il ripristino: ' + $st.status)
}

Run-Test 'T08 pubblicazione simulata dopo approvazione + ripetizione' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't08')
    $st = Run-FlowToGate $fx
    $zipSha = [string]$st.candidate.zipSha256
    $r = Invoke-Rel $fx @('-Resume', '-ApproveHumanTest')
    Assert-True ($r.ExitCode -eq 0) ('pubblicazione: exit ' + $r.ExitCode)
    $st2 = Get-State $fx
    Assert-True ($st2.status -eq 'DISTRIBUTED') ('stato: ' + $st2.status)
    Assert-True ($st2.publish.status -eq 'DONE') ('publish.status: ' + $st2.publish.status)
    $tagLocal =     (Git-Cli  $fx.Repo @('rev-parse', 'v1.1.0^{commit}')).Output[0]
    Assert-True ($tagLocal -eq $st2.candidate.releaseCommit) 'tag locale errato'
    $tagRemote =     (Git-Cli  $fx.Bare @('rev-parse', 'v1.1.0^{commit}')).Output[0]
    Assert-True ($tagRemote -eq $st2.candidate.releaseCommit) 'tag remoto errato'
    $masterRemote =     (Git-Cli  $fx.Bare @('rev-parse', 'refs/heads/master')).Output[0]
    Assert-True ($masterRemote -eq $st2.candidate.releaseCommit) 'master remoto errato'
    $count =     (Git-Cli  $fx.Repo @('rev-list', '--count', 'HEAD')).Output[0]
    Assert-True ($count -eq '2') ('commit totali dopo la pubblicazione: ' + $count)
    $relFile = Join-Path $fx.GhState 'release-v1.1.0.json'
    Assert-True (Test-Path -LiteralPath $relFile) 'release remota non creata'
    $ro = ((Get-Content -LiteralPath $relFile -Raw) | ConvertFrom-Json)
    $asset = @($ro.assets)[0]
    Assert-True ($asset.digest -eq ('sha256:' + $zipSha)) 'digest remoto diverso dal ZIP candidato'
    $report = Join-Path (Get-WfDir $fx) 'reports\final-report.md'
    Assert-True (Test-Path -LiteralPath $report) 'report finale assente'
    $r2 = Invoke-Rel $fx @('-Resume', '-ApproveHumanTest')
    Assert-True ($r2.ExitCode -eq 0) ('ripetizione: exit ' + $r2.ExitCode)
    $st3 = Get-State $fx
    Assert-True ($st3.status -eq 'DISTRIBUTED') 'stato non deve cambiare alla ripetizione'
    $script:FxT08 = $fx
}

Run-Test 'T09 modalita senza pubblicazione, poi pubblicazione' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't09')
    $null = Run-FlowToGate $fx
    $r = Invoke-Rel $fx @('-Resume', '-ApproveHumanTest', '-NoPublish')
    Assert-True ($r.ExitCode -eq 0) ('NoPublish: exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'HUMAN_TEST_APPROVED') ('stato: ' + $st.status)
    Assert-True ($st.publish.status -eq 'SKIPPED_LOCAL') ('publish.status: ' + $st.publish.status)
    $tags = @(    (Git-Cli  $fx.Bare @('tag', '--list', 'v1.1.0')).Output | Where-Object { $_ -ne '' })
    Assert-True ($tags.Count -eq 0) 'tag remoto creato con NoPublish'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fx.GhState 'release-v1.1.0.json'))) 'release creata con NoPublish'
    $r2 = Invoke-Rel $fx @('-Resume', '-ApproveHumanTest')
    Assert-True ($r2.ExitCode -eq 0) ('pubblicazione successiva: exit ' + $r2.ExitCode)
    $st2 = Get-State $fx
    Assert-True ($st2.status -eq 'DISTRIBUTED') ('stato finale: ' + $st2.status)
}

Run-Test 'T10 tag conflittuale: blocco senza sovrascritture' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't10')
    $null = Run-FlowToGate $fx
    & git -C $fx.Repo tag v1.1.0 $fx.Baseline | Out-Null
    $r = Invoke-Rel $fx @('-Resume', '-ApproveHumanTest')
    Assert-True ($r.ExitCode -eq 2) ('conflitto tag: exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'BLOCKED') ('stato: ' + $st.status)
    Assert-True ($st.blocked.phase -eq 'PUBLISHING') ('fase bloccata: ' + $st.blocked.phase)
    $goal =     (Git-Cli  $fx.Repo @('rev-parse', 'v1.1.0^{commit}')).Output[0]
    Assert-True ($goal -eq $fx.Baseline) 'il tag locale non deve essere stato spostato'
    $tags = @(    (Git-Cli  $fx.Bare @('tag', '--list', 'v1.1.0')).Output | Where-Object { $_ -ne '' })
    Assert-True ($tags.Count -eq 0) 'tag non deve essere stato pushato'
}

Run-Test 'T11 resume dry-run: sola lettura' {
    $fx = $script:FxT08
    $wf = Get-WfDir $fx
    $stateBefore = (Get-FileHash -LiteralPath (Join-Path $wf 'release-state.json') -Algorithm SHA256).Hash
    $journalBefore = (Get-FileHash -LiteralPath (Join-Path $wf 'release-journal.md') -Algorithm SHA256).Hash
    $porcBefore = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output)
    $r = Invoke-Rel $fx @('-Resume', '-DryRun')
    Assert-True ($r.ExitCode -eq 0) ('resume dry-run: exit ' + $r.ExitCode)
    $stateAfter = (Get-FileHash -LiteralPath (Join-Path $wf 'release-state.json') -Algorithm SHA256).Hash
    Assert-True ($stateBefore -eq $stateAfter) 'lo stato non deve cambiare in dry-run'
    $journalAfter = (Get-FileHash -LiteralPath (Join-Path $wf 'release-journal.md') -Algorithm SHA256).Hash
    Assert-True ($journalBefore -eq $journalAfter) 'il journal non deve cambiare in dry-run'
    $porcAfter = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output)
    Assert-True (($porcBefore -join '|') -eq ($porcAfter -join '|')) 'git status cambiato in dry-run'
}

Run-Test 'T12 macchina a stati: transizioni validate' {
    $dir = Join-Path $script:TmpRoot 't12-sm'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    . (Join-Path $script:ToolsDir 'release-lib.ps1')
    $state = New-Object psobject -Property @{ status = 'INITIALIZING'; workflowDir = $dir; history = @(); updatedAt = ''; blocked = $null }
    $threw = $false
    try { Set-ReleaseStatus $state 'DISTRIBUTED' 'test' 'transizione non valida' } catch { $threw = $true }
    Assert-True $threw 'INITIALIZING -> DISTRIBUTED deve fallire'
    Set-ReleaseStatus $state 'PREFLIGHT' 'test' 'ok'
    Set-ReleaseStatus $state 'BUILDING' 'test' 'ok'
    Set-ReleaseBlocked $state 'BUILDING' 'guasto simulato' $true
    Assert-True ($state.status -eq 'BLOCKED') 'stato dopo blocco'
    $threw = $false
    try { Set-ReleaseStatus $state 'DISTRIBUTED' 'test' 'BLOCKED -> DISTRIBUTED non valida' } catch { $threw = $true }
    Assert-True $threw 'BLOCKED -> DISTRIBUTED deve fallire'
    Set-ReleaseStatus $state 'BUILDING' 'test' 'ripresa'
    Assert-True ($state.status -eq 'BUILDING') 'ripresa consentita solo verso la fase fallita'
    Assert-True ($state.history.Count -ge 4) 'cronologia non registrata'
}

Run-Test 'T13 preflight con file staged: blocco e indice intatto' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't13')
    Add-Content -LiteralPath (Join-Path $fx.Repo 'PRIVACY.md') -Value 'modifica in staging'
    & git -C $fx.Repo add PRIVACY.md | Out-Null
    $r = Invoke-Rel $fx @('-Version', '1.1.0')
    Assert-True ($r.ExitCode -eq 2) ('preflight con staged: exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'BLOCKED') ('stato: ' + $st.status)
    Assert-True ($st.blocked.phase -eq 'PREFLIGHT') ('fase: ' + $st.blocked.phase)
    $stagedNow = @(    (Git-Cli  $fx.Repo @('diff', '--cached', '--name-only')).Output | Where-Object { $_ -ne '' })
    Assert-True ($stagedNow.Count -eq 1) 'l indice non deve essere stato toccato'
}

Run-Test 'T14 file dirty non gestiti: preservati e non committati' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't14')
    $rt = Join-Path $fx.Repo 'tests\run-tests.ps1'
    $rtText = [System.IO.File]::ReadAllText($rt)
    if (-not $rtText.EndsWith("`n")) { $rtText += "`n" }
    [System.IO.File]::WriteAllText($rt, ($rtText + '# commento locale non gestito' + "`n"))
    Write-FixtureFile $fx.Repo 'docs-nota.txt' ('nota locale ' + (Get-Random))
    $null = Run-FlowToGate $fx
    $porc = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output | Where-Object { $_ -ne '' })
    $porcJoined = $porc -join '|'
    Assert-True ($porcJoined.Contains('tests/run-tests.ps1') -or $porcJoined.Contains('tests\run-tests.ps1')) 'il file modificato non gestito deve restare sporco'
    Assert-True ($porcJoined.Contains('docs-nota.txt')) 'il file non tracciato deve restare non tracciato'
    $files = @(    (Git-Cli  $fx.Repo @('show', '--name-only', '--format=', 'HEAD')).Output | Where-Object { $_ -ne '' })
    Assert-True ($files.Count -eq 10) ('file nel commit: ' + $files.Count)
    Assert-True (($files -join '|').Contains('run-tests.ps1') -eq $false) 'file non gestito finito nel commit'
    Assert-True (($files -join '|').Contains('docs-nota.txt') -eq $false) 'file non tracciato finito nel commit'
}

Run-Test 'T15 artefatto protetto modificato: blocco preflight' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't15')
    Write-FixtureFile $fx.Repo 'bin\Release\CleanSlate.exe' 'binario protetto manomesso (fixture)'
    & git -C $fx.Repo add -f bin\Release\CleanSlate.exe | Out-Null
    & git -C $fx.Repo commit --quiet -m 'aggiunge binario protetto sporco' | Out-Null
    $r = Invoke-Rel $fx @('-Version', '1.1.0')
    Assert-True ($r.ExitCode -eq 2) ('artefatto protetto manomesso: exit ' + $r.ExitCode)
    $st = Get-State $fx
    Assert-True ($st.status -eq 'BLOCKED') ('stato: ' + $st.status)
    Assert-True ($st.blocked.phase -eq 'PREFLIGHT') ('fase: ' + $st.blocked.phase)
    $out = $r.Output -join ' '
    Assert-True ($out.Contains('Artefatto protetto modificato')) 'atteso messaggio su artefatto protetto'
}

Run-Test 'T16 includeFiles: file non gestito incluso nel commit di release' {
    $fx = New-Fixture (Join-Path $script:TmpRoot 't16')
    $rt = Join-Path $fx.Repo 'tests\run-tests.ps1'
    $rtText = [System.IO.File]::ReadAllText($rt)
    if (-not $rtText.EndsWith("`n")) { $rtText += "`n" }
    [System.IO.File]::WriteAllText($rt, ($rtText + '# modifica manuale da includere' + "`n"))
    $r = Invoke-Rel $fx @('-Version', '1.1.0', '-IncludeFiles', 'tests\run-tests.ps1')
    Assert-True ($r.ExitCode -eq 0) ('flusso con includeFiles: exit ' + $r.ExitCode + ' | ' + (@($r.Output | Select-Object -Last 8) -join ' / '))
    $st = Get-State $fx
    Assert-True ($st.status -eq 'AWAITING_HUMAN_APPROVAL') ('stato: ' + $st.status)
    Assert-True ($st.releaseFiles.Count -eq 11) ('releaseFiles: ' + $st.releaseFiles.Count)
    Assert-True (($st.releaseFiles -join '|').Contains('tests\run-tests.ps1')) 'run-tests.ps1 assente da releaseFiles'
    $files = @(    (Git-Cli  $fx.Repo @('show', '--name-only', '--format=', 'HEAD')).Output | Where-Object { $_ -ne '' })
    Assert-True ($files.Count -eq 11) ('file nel commit: ' + $files.Count)
    Assert-True (($files -join '|').Contains('run-tests.ps1')) 'run-tests.ps1 non committato'
    $porc = @(    (Git-Cli  $fx.Repo @('status', '--porcelain')).Output | Where-Object { $_ -ne '' })
    Assert-True ($porc.Count -eq 0) ('working tree sporco: ' + ($porc -join ', '))
}

Write-Host ''
Write-Host ('RISULTATO: ' + $script:Passed + ' superati, ' + $script:Failed + ' falliti')
if ($script:Failed -gt 0) {
    foreach ($f in $script:Failures) { Write-Host ('  FAIL ' + $f) -ForegroundColor Red }
    exit 1
}
exit 0
