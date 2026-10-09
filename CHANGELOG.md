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
  C# (nessuna dipendenza di terze parti), **63 test** su percorsi, scanner, cancellazione,
  report e privacy statica, con fixture autoconsistente in `%LOCALAPPDATA%\CleanSlateTests\`.
- **Script di build** `build.ps1` / `build.common.ps1` che compilano con `csc.exe` in-box di
  .NET Framework, eseguono i test e producono il pacchetto portable in
  `artifacts\alpha-0.1\` con `CleanSlate.exe`, `README.txt`, `LICENSE.txt`, `SHA256SUMS.txt`.
- **Verifica end-to-end** `tests\verify.ps1`: avvia l'eseguibile reale, preme *Analizza*
  tramite automazione, legge stato, riepilogo e percorso del report, controlla la chiusura del
  processo, l'assenza di connessioni TCP in uscita e l'invariabilità di registro, servizi,
  attività pianificate e avvio automatico. **31 controlli**.
- **Documentazione**: `README.md`, `SECURITY.md`, `PRIVACY.md`, `TASKS.md`.

### Dettagli tecnici

- Target **.NET Framework 4.8.1 + Windows Forms + C# 5**, x64, `asInvoker`, `longPathAware`.
- Zero dipendenze di terze parti, zero pacchetti NuGet, zero scaricamenti.
- I sorgenti sono UTF-8 senza BOM; la compilazione usa `/codepage:65001`.

### Limiti

- Una sola categoria di pulizia: file temporanei dell'utente corrente.
- File recenti (< 24 ore) e file bloccati saltati.
- Percorsi oltre 260 caratteri gestiti a livello di manifest ma non garantiti dal runtime.
- Applicazione non firmata digitalmente.
- Verifica della rete in esecuzione basata su campionamento, non su cattura pacchetti
  (vedi `PRIVACY.md`).

### Nota

Nessuna correzione di bug precedente: è la prima pubblicazione.
