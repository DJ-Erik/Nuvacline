# Changelog

Formato aderente a [Keep a Changelog](https://keepachangelog.com/it/1.1.0/).

## [0.3.5] — 2026-10-12

Tema chiaro/scuro automatico. L'interfaccia segue le preferenze di Windows e si aggiorna
anche al cambio di tema mentre il programma è aperto; la versione ufficiale è 0.3.5.

### Aggiunto

- **Tema chiaro/scuro automatico**: al avvio e a ogni cambio di tema di Windows, CleanSlate
  legge l'impostazione `AppsUseLightTheme` (`HKCU\...\Themes\Personalize`) in sola lettura
  tramite P/Invoke `RegGetValue` (nessuna API di registro gestita, nessuna assembly
  aggiuntiva, nessuna chiave creata) e applica la palette corrispondente. Qualsiasi errore
  o valore assente **ricade sul tema chiaro**, che resta identico a tutte le versioni
  precedenti (zero regressione).
- **Palette centralizzata** (`ThemePalette`): ogni colore dell'applicazione è definito una
  sola volta per tema e applicato dall'unico punto `MainForm.ApplyTheme` (finestra, testi,
  tabella e intestazioni, pulsanti, menu, dialogo Informazioni, etichetta del report). Il
  tema chiaro usa i colori di sistema; il tema scuro è un'antracite **fissa** ad alto
  contrasto, indipendente dal sistema, per test deterministici. Un test sui sorgenti
  verifica che nessun colore sia hardcoded fuori dalla palette.
- **Aggiornamento in tempo reale**: `WndProc` riconosce `WM_SETTINGCHANGE` (area
  `ImmersiveColorSet`) e `WM_THEMECHANGED`, rilegge il tema **una sola volta** per messaggio
  e riapplica la palette solo quando il valore è effettivamente cambiato. Messaggi di altre
  aree di sistema non innescano alcuna lettura.
- **13 test `ThemeTests`**: mappatura del valore di tema, seam di lettura e fallback chiaro
  (iniettato, mai dipendente dal tema reale del PC), palette chiara storica e scura fissa,
  contrasto leggibile in entrambi i temi, applicazione/ripristino su `MainForm`, finestra
  Informazioni, etichetta del report, messaggi di sistema via `WndProc`, centralizzazione
  dei colori. La suite passa da **145** a **158**.

### Aggiornato

- Versione ufficiale del prodotto portata da `0.3.0` a `0.3.5`:
  `AssemblyVersion`/`AssemblyFileVersion` `0.3.5.0`, `AssemblyInformationalVersion` `0.3.5`,
  identità e descrizione del manifest, documentazione, test di versione e verifica.
- Percorso del pacchetto portable: `artifacts/alpha-0.3.5/` (il pacchetto `alpha-0.3` del
  0.3.0 resta conservato intatto).
- Finestra Informazioni: la sezione Privacy dichiara che l'unica lettura di registro è
  l'impostazione di tema Windows, senza scritture.
- `PRIVACY.md`: la garanzia "nessuna lettura o scrittura del registro" è stata sostituita
  dalla dichiarazione precisa (scritture zero; lettura sola del valore di tema) e la riga
  dell'audit dell'eseguibile riporta la presenza della sola importazione `RegGetValue`.
- Limite noto dichiarato in `README.md`: i controlli nativi di Windows (MessageBox,
  ComboBox, barra del titolo) restano ai colori di sistema anche in tema scuro.

### Limiti

- Nessun selettore manuale del tema: l'unica sorgente è l'impostazione di Windows.
- Il tema scuro non ridisegna i controlli nativi del sistema (vedi limite noto in
  `README.md`); la copertura verificata è finestra, testi, tabella, pulsanti, menu,
  dialogo Informazioni ed etichette.

## [0.3.0] — 2026-10-12

Terza versione. Aggiunge la finestra Informazioni, il menu Impostazioni con la
conservazione dei report configurabile e porta la versione ufficiale a 0.3.0.

### Aggiunto

- **Finestra Informazioni** (pulsante `?` in alto a destra): finestra modale
  *Informazioni su CleanSlate* con scopo, nota *Gratuito ed essenziale*, privacy
  (*Nessuna connessione di rete, nessun dato raccolto, nessuna modifica al registro,
  nessun avvio automatico*), funzionamento e avvertenza. Nessuna azione, nessuna rete:
  solo testo e il pulsante OK. Il contenuto è costruito in memoria
  (`BuildInfoForm`/`BuildInfoText` in `MainForm`) e il pulsante passa dal seam
  `_showInfoDialog`, così i test non aprono mai finestre reali.
- **Menu Impostazioni** (pulsante `⚙` in alto a destra): `ContextMenuStrip` con la
  voce a spunta *Conserva i report generati* (`CheckOnClick`, valore persistito) e la
  voce *Apri cartella dei report*, visibile — insieme al separatore — solo quando la
  cartella esiste. Un salvataggio non riuscito riporta la spunta al valore effettivo e
  informa l'utente con un messaggio; l'apertura della cartella usa il seam
  `_startProcess` con `UseShellExecute`, senza argomenti di shell.
- **Preferenza `KeepReports`** (`ScanSettings`): nuova chiave `KeepReports=<0|1>` in
  `%LOCALAPPDATA%\CleanSlate\settings.txt`, indipendente da `MinAgeHours`; la
  scrittura di una chiave preserva tutte le altre righe del file (nuovo helper
  `WriteSetting`). Letture difettose (file assente, riga malformata, valore non
  valido, errori di I/O) ricadono sul predefinito, che è la **conservazione attiva**:
  il comportamento di 0.2.0 resta quindi invariato. Nuove API
  `LoadKeepReports`/`LoadKeepReportsFrom`/`SaveKeepReports`/`SaveKeepReportsTo` e
  seam dedicati in `MainForm` (`_loadKeepReports`, `_saveKeepReports`,
  `_reportsFolder`) per i test.

### Aggiornato

- **Generazione dei report**: `GenerateReport` distingue ora la scrittura automatica
  (a fine analisi o pulizia) da quella esplicita. In automatico il file viene scritto
  solo se la conservazione è attiva; disattivandola non viene scritto nulla e **nessun
  report già presente viene cancellato**. *Apri report* genera comunque un report su
  richiesta anche con la conservazione disattivata, e l'etichetta riporta
  *Report: conservazione disattivata…*. Con la conservazione attiva (predefinita)
  l'etichetta resta identica a 0.2.0.
- **Interfaccia alleggerita**: rimossa l'etichetta descrittiva sotto il titolo e
  accorciata la nota a piè di pagina; i contenuti informativi sono ora nella finestra
  Informazioni, mentre la nota operativa resta visibile (deselezione libera,
  conferma obbligatoria, file eliminati non recuperabili). I due nuovi pulsanti sono
  agganciati al bordo destro della finestra.
- Versione ufficiale del prodotto portata da `0.2.0` a `0.3.0`:
  `AssemblyVersion`/`AssemblyFileVersion` `0.3.0.0`, `AssemblyInformationalVersion` `0.3.0`,
  identità e descrizione del manifest, documentazione e percorsi degli artefatti.
- Percorso del pacchetto portable: `artifacts/alpha-0.3/` (era `artifacts/alpha-0.2/`).
- La suite di test è passata da **126** a **145** con i 19 test introdotti in 0.3.0
  (`InfoSettingsTests`: finestra Informazioni, menu Impostazioni, persistenza
  `KeepReports`, generazione condizionata dei report ed errori di salvataggio).
- **Verifica end-to-end** `tests\verify.ps1`: da **31** a **41 controlli**, con la
  sezione dedicata ai due pulsanti di servizio (presenza, apertura e chiusura della
  finestra Informazioni con lettura del contenuto, apertura del menu Impostazioni e
  chiusura con Esc). Il clic che apre una modale è inviato da un thread in background,
  perché un `SendMessage` diretto resterebbe bloccato fino alla chiusura della finestra.

## [0.2.0] — 2026-10-12

Seconda versione. Aggiunge il logo ufficiale, il menu contestuale dei risultati, la
selezione massiva con conteggio e la soglia di scansione configurabile dall'interfaccia.

### Aggiunto

- **Logo ufficiale integrato** (`assets/logo/`): icona multirisoluzione `CleanSlate.ico`
  (16/32/48/256 px, generata dal logo quadrato originale con trasparenza), alta risoluzione
  `CleanSlate.png` (1600×1600, trasparente), varianti orizzontale e monocromatica originali.
  L'icona è incorporata nell'eseguibile (`/win32icon`), impostata come icona della finestra
  principale e mostrata accanto al titolo; il README visualizza la versione orizzontale.
  Nessuna dipendenza aggiuntiva, nessuna risorsa esterna a runtime.
- **Menu contestuale dei risultati** (clic destro su una riga): *Copia nome file*,
  *Copia percorso cartella* e *Apri cartella e seleziona file* (`ScanRowActions` + menu in
  `MainForm`). Il menu appare solo su righe collegate a un file (mai su intestazioni o spazi
  vuoti), opera esclusivamente sul percorso memorizzato nella riga, non tocca la selezione
  destinata alla pulizia né il motore, e non esegue mai il file: avvia
  `explorer.exe /select,"percorso"` con argomenti quotati. File mancanti, appunti non
  disponibili ed errori di avvio sono gestiti con messaggi in italiano. **11 test** nuovi
  (`RowContextMenuTests`).
- **Selezione massiva dei risultati**: due nuovi pulsanti *Seleziona tutto* e
  *Deseleziona tutto* nella riga dei comandi (a destra di *Apri report*), con etichetta
  *N file selezionati (dimensione)* che diventa *Nessun file selezionato* quando la
  selezione è azzerata. I comandi agiscono solo sugli spunti della griglia: non toccano i
  file su disco, non avviano pulizie, non aggirano la conferma e ignorano le righe non
  associate a un `FileItem`. Stato della selezione sincronizzato tra checkbox, modello
  (`FileItem.Selected`), etichetta e lista passata al motore (unica fonte di verità).
  Compatibili con il menu contestuale (apertura/chiusura non alterano la selezione).
  **13 test** nuovi (`BulkSelectionTests`).
- **Impostazione della soglia di scansione** (`MinAge`): nuovo controllo *Età minima*
  (ComboBox) nella riga di stato della finestra principale, con sette opzioni —
  1 ora, 6 ore, 12 ore, 24 ore (predefinita), 48 ore, 7 giorni, nessuna soglia — e
  descrizione nel tooltip. La soglia scelta viene applicata sia all'analisi
  (`ScanOptions.MinAge`) sia alla pulizia (`CleanOptions.MinAge`), così una scansione
  con soglia personalizzata non viene annullata dal ricalcolo del cutoff nel motore di
  cancellazione. La preferenza è persistita in
  `%LOCALAPPDATA%\CleanSlate\settings.txt` (`MinAgeHours=<ore>`; valori ammessi
  {0,1,6,12,24,48,168}; ogni errore o valore non valido ricade su 24 ore). Il cambio
  di soglia non avvia analisi né pulizie automatiche e non altera i risultati di una
  scansione già completata (la soglia della scansione in corso resta visibile nel
  riepilogo fino alla prossima analisi). Nuova classe `ScanSettings`/`ScanThresholdOption`
  nel Core; seam `_loadSettings`/`_saveSettings` nel costruttore di `MainForm` per i
  test (mai il profilo reale). **14 test** nuovi (`ScanThresholdTests`): mapping
  opzioni/TimeSpan, persistenza e ricaricamento, fallback su file corrotto o assente,
  esclusione/inclusione file secondo la soglia, boundary, nessuna soglia, wiring
  GUI→scanner, nessun auto-scan, non retroattività, conferma ancora obbligatoria.
- La suite di test è passata da 73 (0.1.0-alpha) a **111** con i 38 test introdotti in
  0.2.0 (11 menu contestuale + 13 selezione massiva + 14 soglia).

### Aggiornato

- Versione ufficiale del prodotto portata da `0.1.0-alpha` a `0.2.0`:
  `AssemblyVersion`/`AssemblyFileVersion` `0.2.0.0`, `AssemblyInformationalVersion` `0.2.0`,
  identità e descrizione del manifest, documentazione e percorsi degli artefatti.
- Percorso del pacchetto portable: `artifacts/alpha-0.2/` (era `artifacts/alpha-0.1/`).

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
  C# (nessuna dipendenza di terze parti), **73 test** su percorsi, scanner, cancellazione,
  report, privacy statica e race condition, con fixture autoconsistente in
  `%LOCALAPPDATA%\CleanSlateTests\`.
- **Script di build** `build.ps1` / `build.common.ps1` che compilano con `csc.exe` in-box di
  .NET Framework, eseguono i test e producono il pacchetto portable in
  `artifacts\alpha-0.1\` con `CleanSlate.exe`, `README.txt`, `LICENSE.txt`, `SHA256SUMS.txt`.
- **Verifica end-to-end** `tests\verify.ps1`: avvia l'eseguibile reale, preme *Analizza*
  tramite automazione, legge stato, riepilogo e percorso del report, controlla la chiusura del
  processo, l'assenza di connessioni TCP in uscita e l'invariabilità di registro, servizi,
  attività pianificate e avvio automatico. **31 controlli**.
- **Documentazione**: `README.md`, `SECURITY.md`, `PRIVACY.md`, `TASKS.md`.

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
