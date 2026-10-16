# Compila Nuvacline e, su richiesta, prepara il pacchetto portable.
# Uso:
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Package
#   powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTests -Package   (solo per lavori interni)

param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$SkipTests,

    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
. (Join-Path $Root 'build.common.ps1')

$appExe = Invoke-AppBuild $Root $Configuration
Write-Host "Output: $appExe"

if (-not $SkipTests) {
    Write-Host ''
    Write-Host '--- Esecuzione dei test ---'
    & (Join-Path $Root 'tests\run-tests.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw 'La suite di test non superata: nessun pacchetto è stato generato o aggiornato.'
    }
    $appExe = Join-Path $Root "bin\$Configuration\Nuvacline.exe"
}

if ($Package) {
    $artifacts = Join-Path (Join-Path $Root 'artifacts') 'nuvacline-0.5.2'
    if (-not $artifacts.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Percorso degli artefatti non valido.'
    }

    if (Test-Path -LiteralPath $artifacts) {
        # Rimozione del solo output di build generato da questo script.
        Remove-Item -LiteralPath $artifacts -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

    Copy-Item -LiteralPath $appExe -Destination (Join-Path $artifacts 'Nuvacline.exe') -Force

    $readme = @"
Nuvacline 0.5.2
===============

COSA FA
-------
Nuvacline individua e rimuove i file temporanei dell'utente corrente
(cartella restituita dall'ambiente Windows), escludendo per impostazione
predefinita i file modificati nelle ultime 24 ore. L'interfaccia segue
automaticamente il tema chiaro/scuro impostato in Windows.

AMBITO DELLA PULIZIA
--------------------
La pulizia opera sulla cartella temporanea dell'utente Windows corrente
(la cartella restituita dall'ambiente Windows). Non pulisce gli altri
account o le cartelle di sistema e non e' possibile aggiungere percorsi
personalizzati. I risultati possono variare in presenza di file in uso,
permessi insufficienti o altri limiti del sistema.

COME SI USA
-----------
1. Avviare Nuvacline.exe.
2. Premere "Analizza": l'analisi e' in sola lettura e non modifica nulla.
3. Esaminare l'elenco, selezionare o deselezionare gli elementi desiderati.
4. Premere "Pulisci selezionati" e confermare nella finestra di conferma.
5. Il report locale viene scritto in automatico in
   %LOCALAPPDATA%\Nuvacline\reports\ e il percorso appare nella finestra.
   "Apri report" lo apre nel lettore di testo predefinito.
6. Il pulsante "?" mostra le informazioni sul prodotto; il pulsante con
   l'ingranaggio consente di attivare o disattivare la conservazione dei
   report e di aprire la cartella che li contiene.

REQUISITI
---------
- Windows 10 (build 1903 o successive) oppure Windows 11, 64 bit.
- .NET Framework 4.8 o successivo, normalmente gia' presente in Windows.
- Nessun privilegio di amministratore.
- Nessuna connessione di rete necessaria o utilizzata.

NON FA
------
- Non si collega a Internet, non raccoglie e non trasmette dati.
- Non modifica il registro di Windows.
- Non installa servizi, attivita' pianificate, avvio automatico o processi residenti.
- Non contiene pubblicita, telemetria, account o aggiornamenti automatici.
- Non promette di accelerare il computer.
- Non elimina file senza una conferma esplicita.
- Non puo' recuperare i file che sono stati eliminati.

LIMITI CONOSCIUTI
-----------------
- Versione alpha: l'unica categoria supportata e' "File temporanei dell'utente".
- I file recenti (ultime 24 ore) e i file bloccati vengono saltati.
- I percorsi molto lunghi (> 260 caratteri) possono non essere gestiti:
  in tal caso l'elemento viene saltato e conteggiato, mai eliminato a caso.
- Lo spazio indicato come recuperabile e' una stima: parte puo' gia' essere
  libero o non immediatamente disponibile.

CONTROLLI DI INTEGRITA'
-----------------------
I file SHA256SUMS.txt riportano l'impronta SHA-256 dei file di questa cartella.
Verifica con:
  certutil -hashfile Nuvacline.exe SHA256

LICENZA
-------
Codice sorgente: GNU General Public License v3.0 or later
(GPL-3.0-or-later). Il testo integrale e' nel file LICENSE di
questo pacchetto e nella root del repository sorgente.
Identificatore SPDX: GPL-3.0-or-later.
Il nome e il logo non sono concessi per uso di marchio dalla licenza.
Il logo e' stato generato con strumenti di intelligenza artificiale:
non viene rivendicata titolarita' di copyright sull'immagine.

Sorgente corrispondente: repository Git del progetto, branch master,
tag della versione rilasciata.

DIPENDENZE
----------
Nessun componente di terze parti e' incluso. Vedere THIRD-PARTY.txt.
"@
    Set-Content -LiteralPath (Join-Path $artifacts 'README.txt') -Value $readme -Encoding UTF8

    # Testo integrale della licenza GNU GPL v3 dalla root del repository.
    Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination (Join-Path $artifacts 'LICENSE') -Force

    # Documenti privacy e sicurezza della versione (copia della root del repository).
    Copy-Item -LiteralPath (Join-Path $Root 'PRIVACY.md') -Destination (Join-Path $artifacts 'PRIVACY.md') -Force
    Copy-Item -LiteralPath (Join-Path $Root 'SECURITY.md') -Destination (Join-Path $artifacts 'SECURITY.md') -Force

    $thirdParty = @"
Nuvacline 0.5.2 - note su licenze e dipendenze
==============================================

Dipendenze di terze parti incluse nella distribuzione: NESSUNA.

Nuvacline e' un singolo eseguibile compilato con il compilatore C#
fornito con .NET Framework e non include librerie di terze parti.

Requisito a runtime: .NET Framework 4.8 (o successivo), componente
Microsoft gia' presente in Windows 10 (build 1903+) e Windows 11.
Tale componente NON viene ridistribuito con questa cartella e resta
soggetto alla licenza Microsoft di Windows.

Gli strumenti usati SOLO per la compilazione (csc.exe in-box di
.NET Framework) non vengono ridistribuiti.

I file di questa cartella non contengono codice di terze parti,
pubblicita, telemetria o risorse scaricate da Internet.

La licenza del codice sorgente di Nuvacline e' la GNU GPL-3.0-or-later:
vedere il file LICENSE di questa cartella.
"@
    Set-Content -LiteralPath (Join-Path $artifacts 'THIRD-PARTY.txt') -Value $thirdParty -Encoding UTF8

    $files = Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name
    $lines = @()
    foreach ($file in $files) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $lines += ($hash + '  ' + $file.Name)
    }
    Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Value $lines -Encoding ASCII

    Write-Host ''
    Write-Host "Pacchetto generato: $artifacts"
    Get-ChildItem -LiteralPath $artifacts -File | ForEach-Object { Write-Host ('  ' + $_.Name.PadRight(20) + $_.Length + ' byte') }
}
