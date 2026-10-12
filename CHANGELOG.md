# Changelog

Formato aderente a [Keep a Changelog](https://keepachangelog.com/it/1.1.0/).

## [0.1.0-alpha] — 2026-10-09

Prima versione alpha, portatile e offline.

### Aggiunto

- **GUI WinForms in italiano** (`MainForm`): titolo con versione, pulsanti
  *Analizza*, *Pulisci selezionati*, *Annulla*, *Apri report*, barra di avanzamento,
  riepilogo numerico, tabella dei risultati con selezione riga per riga e nota di avvertenza.
- **Analisi in sola lettura** della cartella temporanea dell'utente corrente
  (`TempPathProvider`, `TempFolderScanner`): ricorsione prudente, nessuna scrittura,
  esclusione dei file più recenti di 24 ore, salto dei reparse point, annullamento
  cooperativo, errori registrati senza interrompere l'analisi.
- **Sicurezza dei percorsi** (`PathSafety`): normalizzazione, containment dentro la radice
  autorizzata, rifiuto del traversal con `..`, rilevamento dei componenti di reparse point,
  validazione della radice.
- **Motore di pulizia prudente** (`TempFileCleaner`): ricalcolo della sicurezza per ogni
  singolo file, nessuna cancellazione di directory, nessuna shell, file bloccati saltati e
  conteggiati, riepilogo in memoria con byte effettivamente rimossi.
- **Finestra di conferma** con numero di file e dimensione totale, proposta predefinita *No*.
- **Report locale** (`ReportBuilder`, `ReportExporter`): generato automaticamente al termine
  di un'analisi o di una pulizia in `%LOCALAPPDATA%\CleanSlate\reports\`, con il percorso
  mostrato nella finestra e apribile con un pulsante; formati TXT e JSON; la radice temporanea
  è sostituita dal segnaposto `%TEMP%` e il profilo utente non compare.
- **Suite di test automatica** (`src/CleanSlate.Tests`): runner equivalente a xUnit scritto in
  C# (nessuna dipendenza di terze parti), **97 test** su percorsi, scanner, cancellazione,
  report, privacy statica, race condition, menu contestuale e selezione massiva, con fixture
  autoconsistente in `%LOCALAPPDATA%\CleanSlateTests\`.
- **Script di build** `build.ps1` / `build.common.ps1` che compilano con `csc.exe` in-box di
  .NET Framework, eseguono i test e producono il pacchetto portable in
  `artifacts\alpha-0.1\` con `CleanSlate.exe`, `README.txt`, `LICENSE.txt`, `SHA256SUMS.txt`.
- **Verifica end-to-end** `tests\verify.ps1`: avvia l'eseguibile reale, preme *Analizza*
  tramite automazione, legge stato, riepilogo e percorso del report, controlla la chiusura del
  processo, l'assenza di connessioni TCP in uscita e l'invariabilità di registro, servizi,
  attività pianificate e avvio automatico. **31 controlli**.
- **Documentazione**: `README.md`, `SECURITY.md`, `PRIVACY.md`, `TASKS.md`.
- **Logo ufficiale integrato** (`assets/logo/`): icona multirisoluzione `CleanSlate.ico`
  (16/32/48/256 px, generata dal logo quadrato originale con trasparenza), alta risoluzione
  `CleanSlate.png` (1600×1600, trasparente), varianti orizzontale e monocromatica originali.
  L'icona è incorporata nell'eseguibile (`/win32icon`), impostata come icona della finestra
  principale e mostrata accanto al titolo; il README visualizza la versione orizzontale.
  Nessuna dipendenza aggiuntiva, nessuna risorsa esterna a runtime.
  SHA-256 dell'eseguibile dopo l'integrazione: `8e14e3c0085fc2c2…`
  (aggiornato in `artifacts/alpha-0.1/SHA256SUMS.txt`; come già documentato, la build
  non è riproducibile bit per bit, quindi l'hash è valido per questo pacchetto).
- **Menu contestuale dei risultati** (clic destro su una riga): *Copia nome file*,
  *Copia percorso cartella* e *Apri cartella e seleziona file* (`ScanRowActions` + menu in
  `MainForm`). Il menu appare solo su righe collegate a un file (mai su intestazioni o spazi
  vuoti), opera esclusivamente sul percorso memorizzato nella riga, non tocca la selezione
  destinata alla pulizia né il motore, e non esegue mai il file: avvia
  `explorer.exe /select,"percorso"` con argomenti quotati. File mancanti, appunti non
  disponibili ed errori di avvio sono gestiti con messaggi in italiano. **11 test** nuovi
   (`RowContextMenuTests`, suite ora a **84**), verifiche eseguite anche sulla GUI reale del
   pacchetto (menu visibile con le tre voci, nessuna voce su intestazione/spazi, spunti
   invariati, tre azioni eseguite). SHA-256 dell'eseguibile con il menu: `d62ed875e4bed957…`
   (vedi `artifacts/alpha-0.1/SHA256SUMS.txt`).
- **Selezione massiva dei risultati**: due nuovi pulsanti *Seleziona tutto* e
  *Deseleziona tutto* nella riga dei comandi (a destra di *Apri report*), con etichetta
  *N file selezionati (dimensione)* che diventa *Nessun file selezionato* quando la
  selezione è azzerata. I comandi agiscono solo sugli spunti della griglia: non toccano i
  file su disco, non avviano pulizie, non aggirano la conferma e ignorano le righe non
  associate a un `FileItem`. Stato della selezione sincronizzato tra checkbox, modello
  (`FileItem.Selected`), etichetta e lista passata al motore (unica fonte di verità).
  Compatibili con il menu contestuale (apertura/chiusura non alterano la selezione).
  **13 test** nuovi (`BulkSelectionTests`, suite ora a **97**), verifiche eseguite anche
  sulla GUI reale del pacchetto (pulsanti visibili, deselezione → pulizia disabilitata,
  rieselezione → pulizia abilitata, conteggio e dimensione aggiornati). SHA-256
  dell'eseguibile: `b9eeeea20f29bd48…` (vedi `artifacts/alpha-0.1/SHA256SUMS.txt`).

### Sicurezza

- Audit di sicurezza pre-distribuzione eseguito prima della consegna ai beta tester.
  La build precedente è conservata in `artifacts/audit-baseline/`.
- **Correzione**: il controllo sulle componenti di reparse point nel motore di pulizia era
  eseguito prima della lettura di dimensione ed età, e quindi con una finestra TOCTOU verso
  `File.Delete`. È stato spostato a essere l'ultimo controllo strutturale prima della
  cancellazione (`TempFileCleaner.ProcessOne`).
- **Test di regressione aggiuntivi** (`SecurityAuditTests`): file toccato, sostituito da una
  directory o attraversato da una junction sostituita dopo la scansione; selezione
  deselezionata non passata al motore; selezione stabile dopo l'ordinamento della tabella;
  percorsi con prefissi `\\?\` e `\\.\`, stream dati alternativo e nomi con spazio o punto
  finale.
- **Ispezione della build pubblicata**: soltanto assembly del framework .NET, nessun nome di
  metadato di rete o di registro, `http://` presente esclusivamente come namespace XML del
  manifest, manifest `asInvoker` con `uiAccess="false"`.
- Rischi residui dichiarati in `SECURITY.md`: finestra TOCTOU residua fino a `File.Delete`,
  nessuna firma del codice, nessuna cattura pacchetti, deselezione della singola riga GUI
  non automatable in ambiente con sessione bloccata.
- **Validazione pratica pre-beta** (`tests/qa-practical.ps1`): il percorso reale analisi →
  conferma → cancellazione è stato eseguito sull'eseguibile pubblicato, su vero `%TEMP%` e
  su un sandbox GUI, con solo file sintetici identificabili. Analisi senza cancellare i
  preesistenti, regole di età, junction, file bloccato e fuori radice verificati; gate di
  conferma "No" verificato sulla GUI; cancellazione esercitata al motore di produzione con
  lista esplicita; residui 0; SHA-256 del pacchetto invariato.

### Dettagli tecnici

- Target **.NET Framework 4.8.1 + Windows Forms + C# 5**, x64, `asInvoker`, `longPathAware`.
- Zero dipendenze di terze parti, zero pacchetti NuGet, zero scaricamenti.
- I sorgenti sono UTF-8 senza BOM; la compilazione usa `/codepage:65001`.
- La compilazione **non è riproducibile bit per bit**: `csc.exe` di .NET Framework cambia
  MVID a ogni build, quindi `SHA256SUMS.txt` è valido solo per il pacchetto con cui è stato
  generato.

### Limiti

- Una sola categoria di pulizia: file temporanei dell'utente corrente.
- File recenti (< 24 ore) e file bloccati saltati.
- Percorsi oltre 260 caratteri gestiti a livello di manifest ma non garantiti dal runtime.
- Applicazione non firmata digitalmente.
- Verifica della rete in esecuzione basata su campionamento, non su cattura pacchetti
  (vedi `PRIVACY.md`).

### Nota

Nessuna correzione di bug precedente: è la prima pubblicazione.
