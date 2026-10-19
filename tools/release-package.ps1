# ============================================================================
# Nuvacline Release Manager - fase PACKAGING e documenti del gate umano
# Richiede: release-lib.ps1, release-prep.ps1, release-phases.ps1.
# ============================================================================

function New-ReleaseNotesContent([object]$State) {
    $ver = $State.version
    $exe = [string]$State.candidate.exeSha256
    $zip = [string]$State.candidate.zipSha256
    $tot = $State.tests.total; $pas = $State.tests.passed
    $testsLine = 'Suite di test: superata.'
    if ($null -ne $tot) { $testsLine = 'Suite di test: ' + $pas + '/' + $tot + ' superati.' }
    $nl = [Environment]::NewLine
    $t = @()
    $t += ('Nuvacline ' + $ver + ' - release Windows')
    $t += ''
    $t += ('Nuvacline e'' un''utilita Windows che individua e rimuove i file temporanei')
    $t += ('dell''utente corrente. Il pacchetto e'' portable: nessuna installazione,')
    $t += ('nessun privilegio di amministratore, nessuna connessione di rete.')
    $t += ''
    $t += 'DISCLAIMER'
    $t += '----------'
    $t += ('Il software e'' fornito "cosi'' com''e''", senza garanzie di alcun tipo.')
    $t += ('L''autore non e'' responsabile per eventuali perdite di dati, problemi di')
    $t += ('Windows o altri danni derivanti dall''uso del software. L''uso del programma')
    $t += ('e'' a proprio rischio e pericolo.')
    $t += ''
    $t += 'AMBITO DELLA PULIZIA'
    $t += '--------------------'
    $t += ('- La pulizia opera sulla cartella temporanea dell''utente corrente restituita')
    $t += ('  dall''ambiente Windows (Path.GetTempPath()).')
    $t += ('- Non pulisce indiscriminatamente gli altri account o le cartelle di sistema.')
    $t += ('- I risultati possono variare per file in uso, permessi insufficienti o altri')
    $t += ('  limiti del sistema.')
    $t += ('- Non esiste una funzione per aggiungere percorsi personalizzati.')
    $t += ''
    $t += 'COME SI USA'
    $t += '-----------'
    $t += ('1. Scaricare e estrarre Nuvacline-' + $ver + '-Windows.zip in una cartella qualsiasi.')
    $t += ('2. Avviare Nuvacline.exe (nessuna installazione, nessun privilegio di admin).')
    $t += ('3. Premere "Analizza" (analisi in sola lettura, non modifica nulla).')
    $t += ('4. Esaminare l''elenco e selezionare gli elementi da rimuovere.')
    $t += ('5. Premere "Pulisci selezionati" e confermare.')
    $t += ('6. Il report locale viene scritto automaticamente in')
    $t += ('   %LOCALAPPDATA%\Nuvacline\reports\ e il percorso appare nella finestra.')
    $t += ''
    $t += 'REQUISITI'
    $t += '---------'
    $t += ('- Windows 10 (build 1903+) o Windows 11, 64 bit.')
    $t += ('- .NET Framework 4.8 o successivo (normalmente gia'' presente in Windows).')
    $t += ('- Nessun privilegio di amministratore.')
    $t += ('- Nessuna connessione di rete necessaria o utilizzata.')
    $t += ''
    $t += 'VERIFICHE ESEGUITE SU QUESTA BUILD'
    $t += '----------------------------------'
    $t += ('- ' + $testsLine)
    $t += ('- verify.ps1 (verifica end-to-end): tutti i controlli superati.')
    $t += ('- SHA-256 dell''eseguibile: ' + $exe)
    $t += ''
    $t += 'INTEGRITA'''
    $t += '---------'
    $t += ('Lo SHA-256 dell''archivio ZIP e'':')
    $t += $zip
    $t += ''
    $t += ('All''interno dello ZIP, SHA256SUMS.txt riporta l''impronta di ogni file.')
    $t += ''
    $t += 'LIMITI'
    $t += '------'
    $t += ('- Versione alpha: unica categoria supportata "File temporanei dell''utente".')
    $t += ('- I file recenti (ultime 24 ore) e i file bloccati vengono saltati.')
    $t += ('- Lo spazio recuperabile e'' una stima.')
    $t += ('- Nessuna firma digitale, certificazione o garanzia di compatibilita'' oltre')
    $t += ('  quanto dichiarato sopra.')
    $t += ''
    $t += ('Privacy: nessuna connessione di rete, nessuna telemetria, nessuna raccolta dati.')
    $t += ('Vedere README.txt, PRIVACY.md e SECURITY.md nello ZIP.')
    return (($t -join $nl) + $nl)
}

function New-HumanTestContent([object]$State) {
    $ver = $State.version
    $nl = [Environment]::NewLine
    $t = @()
    $t += ('# Test umano - Nuvacline ' + $ver)
    $t += ''
    $t += ('Pacchetto: ' + $State.candidate.zipPath)
    $t += ('SHA-256 ZIP: ' + $State.candidate.zipSha256)
    $t += ('SHA-256 eseguibile: ' + $State.candidate.exeSha256)
    $t += ('Commit di release: ' + $State.candidate.releaseCommit)
    $t += ''
    $t += ('Estrarre lo ZIP in una cartella nuova e avviare Nuvacline.exe (nessun')
    $t += ('privilegio di amministratore richiesto; nessuna installazione).')
    $t += ''
    $t += 'Checklist:'
    $t += ('1. Avvio e chiusura: l''applicazione si apre e si chiude senza errori.')
    $t += ('2. Finestra Informazioni: il pulsante "?" apre la finestra Informazioni.')
    $t += ('3. Versione: la finestra mostra "Nuvacline ' + $ver + '" (titolo e Informazioni).')
    $t += ('4. Ambito della pulizia: la sezione e'' presente, leggibile e corretta')
    $t += ('   (cartella temporanea dell''utente corrente; nessun percorso')
    $t += ('   personalizzabile; nessuna pulizia di altri account o cartelle di sistema).')
    $t += ('5. Controllo visivo: nessun problema grafico evidente (tema chiaro/scuro);')
    $t += ('   nessun errore inatteso.')
    $t += ''
    $t += ('Non eseguire pulizie reali distruttive solo per il test: l''analisi e'' in sola')
    $t += ('lettura; la pulizia e'' opzionale e a proprio rischio.')
    $t += ''
    $t += 'Approvazione (solo se tutti i punti sono OK):'
    $t += '  .\tools\release.ps1 -Resume -ApproveHumanTest'
    $t += ''
    $t += ('Il comando verifica che la candidata non sia cambiata (commit, ZIP e')
    $t += ('eseguibile) e solo allora autorizza la pubblicazione.')
    return (($t -join $nl) + $nl)
}

function Invoke-PackagePhase([object]$State, [string]$Repo) {
    Write-RmPhase 'PACKAGING - pacchetto Windows e verifiche di integrita'''
    $ver = $State.version
    $cand = $State.candidate.repoCopy
    $pkgRoot = Join-Path $State.workflowDir 'package'
    $stage = Join-Path $pkgRoot ('Nuvacline-' + $ver + '-Windows')
    $zipPath = Join-Path $pkgRoot ('Nuvacline-' + $ver + '-Windows.zip')
    $extract = Join-Path $pkgRoot 'extract-verify'
    Ensure-Directory $pkgRoot
    foreach ($d in @($stage, $extract)) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force } }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

    $pkgLog = Join-Path $State.workflowDir 'candidate\qa-logs\package-build.log'
    $res = Invoke-PowerShellScript (Join-Path $cand 'build.ps1') @('-SkipTests', '-Package') $pkgLog $cand
    if ($res.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'PACKAGING' ('Generazione pacchetto fallita (codice ' + $res.ExitCode + '). Log in candidate\qa-logs\package-build.log.') $true
        return $false
    }
    $art = Join-Path $cand ('artifacts\nuvacline-' + $ver)
    if (-not (Test-Path -LiteralPath $art)) {
        Set-ReleaseBlocked $State 'PACKAGING' ('Cartella artifacts non prodotta: ' + $art) $true
        return $false
    }
    $files = @(Get-ChildItem -LiteralPath $art -File | Sort-Object Name)
    $names = @($files | ForEach-Object { $_.Name })
    if (-not (Get-SetEqual $names $script:RequiredPackageFiles)) {
        Set-ReleaseBlocked $State 'PACKAGING' ('Contenuto del pacchetto inatteso: ' + ($names -join ', ')) $false
        return $false
    }

    $sumsPath = Join-Path $art 'SHA256SUMS.txt'
    $sumLines = @(Get-Content -LiteralPath $sumsPath -Encoding ASCII)
    $sumOk = 0
    foreach ($line in $sumLines) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
            Set-ReleaseBlocked $State 'PACKAGING' ('Riga SHA256SUMS non valida: ' + $line) $false
            return $false
        }
        $h = $Matches[1]; $n = $Matches[2]
        $real = (Get-Sha256 (Join-Path $art $n)).ToLowerInvariant()
        if ($real -ne $h) {
            Set-ReleaseBlocked $State 'PACKAGING' ('Checksum non valido per ' + $n + ' nel pacchetto.') $false
            return $false
        }
        $sumOk++
    }
    if ($sumOk -ne ($script:RequiredPackageFiles.Count - 1)) {
        Set-ReleaseBlocked $State 'PACKAGING' ('SHA256SUMS.txt copre ' + $sumOk + ' file invece di ' + ($script:RequiredPackageFiles.Count - 1) + '.') $false
        return $false
    }

    Ensure-Directory $stage
    Copy-Item -Path (Join-Path $art '*') -Destination $stage -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Ensure-Directory $extract
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force
    $exNames = @(Get-ChildItem -LiteralPath $extract -File | Sort-Object Name | ForEach-Object { $_.Name })
    if (-not (Get-SetEqual $exNames $script:RequiredPackageFiles)) {
        Set-ReleaseBlocked $State 'PACKAGING' 'Elenco dei file estratti dallo ZIP inatteso.' $false
        return $false
    }
    foreach ($line in @(Get-Content -LiteralPath (Join-Path $extract 'SHA256SUMS.txt') -Encoding ASCII)) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
            Set-ReleaseBlocked $State 'PACKAGING' 'Riga SHA256SUMS non valida nella copia estratta.' $false
            return $false
        }
        $real = (Get-Sha256 (Join-Path $extract $Matches[2])).ToLowerInvariant()
        if ($real -ne $Matches[1]) {
            Set-ReleaseBlocked $State 'PACKAGING' ('Checksum dell''estrazione non valido per ' + $Matches[2] + '.') $false
            return $false
        }
    }
    $exeStage = Join-Path $stage 'Nuvacline.exe'
    $exeExtract = Join-Path $extract 'Nuvacline.exe'
    $hStage = Get-Sha256 $exeStage
    $hExtract = Get-Sha256 $exeExtract
    if ($hStage -ne $hExtract) {
        Set-ReleaseBlocked $State 'PACKAGING' 'Hash dell''eseguibile diverso tra pacchetto ed estrazione.' $false
        return $false
    }
    $vi = (Get-Item -LiteralPath $exeExtract).VersionInfo
    $need1 = $ver + '.0'
    if (($vi.FileVersion -notlike ($need1 + '*')) -or ($vi.ProductVersion -notlike ($ver + '*'))) {
        Set-ReleaseBlocked $State 'PACKAGING' ('Versione del file non coerente: FileVersion=' + $vi.FileVersion + ' ProductVersion=' + $vi.ProductVersion) $false
        return $false
    }

    Write-RmInfo 'Verifica end-to-end (verify.ps1) sull''eseguibile impacchettato...'
    $vlog = Join-Path $State.workflowDir 'candidate\qa-logs\verify-packaged.log'
    $vres = Invoke-PowerShellScript (Join-Path $cand 'tests\verify.ps1') @('-Exe', $exeStage) $vlog $cand
    $State.verifications.verifyExitCode = $vres.ExitCode
    $State.verifications.verifyLog = $vlog
    if ($vres.ExitCode -ne 0) {
        Set-ReleaseBlocked $State 'PACKAGING' ('verify.ps1 non superata (codice ' + $vres.ExitCode + '). Log in candidate\qa-logs\verify-packaged.log.') $true
        return $false
    }

    $packageFiles = @()
    foreach ($f in @(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name)) {
        $packageFiles += (New-Object psobject -Property @{ name = $f.Name; bytes = $f.Length; sha256 = (Get-Sha256 $f.FullName) })
    }
    $zipItem = Get-Item -LiteralPath $zipPath
    Add-Member -InputObject $State.candidate -NotePropertyName exeSha256 -NotePropertyValue $hStage -Force
    Add-Member -InputObject $State.candidate -NotePropertyName zipPath -NotePropertyValue $zipPath -Force
    Add-Member -InputObject $State.candidate -NotePropertyName zipSha256 -NotePropertyValue (Get-Sha256 $zipPath) -Force
    Add-Member -InputObject $State.candidate -NotePropertyName zipBytes -NotePropertyValue $zipItem.Length -Force
    Add-Member -InputObject $State.candidate -NotePropertyName packageFiles -NotePropertyValue @($packageFiles) -Force
    Save-ReleaseState $State

    $notesPath = $State.releaseNotesPath
    if (-not $notesPath) {
        $notesPath = Join-Path $pkgRoot ('release-notes-v' + $ver + '.md')
        [System.IO.File]::WriteAllText($notesPath, (New-ReleaseNotesContent $State), (New-Object System.Text.UTF8Encoding($false)))
        Add-Member -InputObject $State -NotePropertyName releaseNotesPath -NotePropertyValue $notesPath -Force
    } else {
        if (-not (Test-Path -LiteralPath $notesPath)) {
            Set-ReleaseBlocked $State 'PACKAGING' ('File note specificato ma assente: ' + $notesPath) $false
            return $false
        }
    }
    $htPath = Join-Path $State.workflowDir 'HUMAN-TEST.md'
    [System.IO.File]::WriteAllText($htPath, (New-HumanTestContent $State), (New-Object System.Text.UTF8Encoding($false)))
    Save-ReleaseState $State
    Add-RmJournal $State.workflowDir ('PACKAGING | zip ' + $State.candidate.zipSha256 + ' (' + $zipItem.Length + ' byte) | exe ' + $hStage)
    Write-RmOk ('Pacchetto verificato: ' + $zipPath)
    Write-RmOk ('SHA-256 ZIP: ' + $State.candidate.zipSha256)
    Write-RmOk ('SHA-256 exe: ' + $hStage)
    return $true
}
