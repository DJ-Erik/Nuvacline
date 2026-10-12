# Sicurezza di CleanSlate

Documento relativo alla versione **0.3.0**.

## Modello di minaccia

CleanSlate è uno strumento desktop che cancella file. Il rischio principale non è un attaccante
esterno ma lo **stesso strumento**: un percorso sbagliato, un collegamento simbolico o un errore
di logica potrebbero portare alla cancellazione di file che non dovevano essere toccati.

Di conseguenza le difese sono concentrate sul lato della cancellazione.

## Difese implementate

### 1. Nessun privilegio

- Manifest applicativo con `requestedExecutionLevel level="asInvoker"`.
- L'applicazione non chiede mai di essere eseguita come amministratore.
- Nessuna operazione richiede diritti oltre quelli dell'utente corrente.

### 2. Radice autorizzata unica e ricalcolata

- L'unica cartella su cui si può operare è quella restituita dall'ambiente Windows
  (`TempPathProvider`), mai un percorso scritto nel codice.
- `PathSafety` normalizza i percorsi e verifica che ogni elemento da eliminare sia
  **all'interno** della radice autorizzata.
- La verifica avviene sia in fase di analisi sia **di nuovo, per ogni singolo file, nel momento
  esatto in cui viene cancellato**: l'elemento mostrato in elenco può essere cambiato nel
  frattempo e la cancellazione non si fida dell'elenco.
- I collegamenti fisici e i reparse point non vengono seguiti: se un percorso contiene un
  componente di reparse point, la cartella viene saltata.

### 3. Nessuna cancellazione di directory

- Il motore di cancellazione **non contiene codice che cancelli directory**: opera solo su
  singoli file.
- Non viene usato alcun comando shell o PowerShell per le cancellazioni.
- Nessuna cancellazione ricorsiva.

### 4. Verifica per ogni singolo file

Prima di ogni cancellazione `TempFileCleaner` ricalcola:

- normalizzazione del percorso;
- containment dentro la radice autorizzata;
- età minima (24 ore di impostazione predefinita);
- che il percorso non sia la radice stessa;
- che né il file né alcuna componente del suo percorso sotto la radice sia un reparse point.

Il controllo sul reparse point è **l'ultimo controllo strutturale**, eseguito subito prima di
`File.Delete`: la finestra temporale in cui una directory potrebbe essere sostituita da una
junction è stata ridotta al minimo (vedi "Audit pre-distribuzione").

Un file che non supera uno di questi controlli viene **saltato e contato**, mai eliminato.

### 5. Conferma esplicita

- La pulizia non parte mai da sola: il pulsante è disabilitato senza selezione e la
  cancellazione avviene solo dopo una finestra di conferma con numero di file e dimensione totale.
- La finestra di conferma propone di default **No**.

### 6. Analisi in sola lettura

- Lo scanner non scrive, non rinomina, non sposta e non elimina nulla.
- Un test verifica che il contenuto della cartella di test sia identico prima e dopo l'analisi.

### 7. Nessuna shell, nessun comando distruttivo

- Nessuna chiamata a `cmd.exe`, `powershell.exe` o interfacce simili nell'applicazione.
- Coperto da un test statico sulle sorgenti (`PrivacyTests`).

### 8. Gestione degli errori

- Un errore su una cartella o su un file viene registrato e l'operazione continua.
- Un errore imprevisto produce un messaggio in italiano, non un crash.
- Un file bloccato viene saltato e contato.

## Audit di sicurezza pre-distribuzione (2026-10-09)

Revisione indipendente del percorso **scansione → cancellazione** prima di consegnare l'alpha
ai beta tester. La build precedente è stata conservata prima di ogni modifica
(`artifacts/audit-baseline/`, SHA-256 `bf33e9cb…`).

### Pericolo identificato e corretto

**Finestra TOCTOU tra il controllo reparse point e la cancellazione.**
Il controllo sulle junction era eseguito *prima* della lettura di dimensione ed età, quindi
prima di `File.Delete`. Se una componente del percorso fosse stata sostituita da una junction
in quell'intervallo, `File.Delete` avrebbe risolto il percorso attraverso il collegamento e
avrebbe potuto rimuovere un file fuori dalla radice autorizzata.

- **Correzione**: il controllo `HasReparseComponentInsideRoot` è stato spostato in ultima
  posizione, immediatamente prima della chiamata a `File.Delete`.
- **Test di regressione**:
  `SecurityAuditTests.Clean_PathComponentReplacedByJunctionAfterScan_IsSkippedAndTargetSurvives`
  (sottocartella sostituita da una junction dopo la scansione → elemento saltato, file di
  destinazione intatto).

### Classificazione dei rischi esaminati

| Rischio | Esito |
|---|---|
| Path traversal con `..` | Rifiutato: `Path.GetFullPath` risolve e `IsInsideRoot` nega (test) |
| Prefisso `\\?\` o `\\.\` | Rifiutato: il confronto con la radice fallisce, fail-safe (test) |
| Stream dati alternativo (`::$DATA`) | Rifiutato: la normalizzazione fallisce, nessuna eccezione (test) |
| Spazio o punto finale nel nome | Normalizzato dentro la radice, nessuna fuga (test) |
| Nome breve 8.3 della radice | `GetFullPath` lo espande al nome lungo: nessuna fuga (verificato a runtime) |
| Junction o symlink nella directory | Non seguiti in scansione; rilevati di nuovo in cancellazione (test) |
| File sostituito da directory dopo la scansione | Saltato, contenuto intatto (test) |
| File toccato dopo la scansione | Saltato come "non conforme all'età" (test) |
| File eliminato dopo la scansione | Saltato come "non esistente" (test) |
| Radice stessa passata come elemento | Rifiutata (test) |
| Elemento fuori radice passato come elemento | Rifiutato, file intatto (test) |
| Elementi deselezionati | Non arrivano al motore di cancellazione (test GUI + test motore) |
| Ordinamento della tabella | La selezione resta corretta (test GUI) |
| File bloccato / in sola lettura | Saltato o conteggiato come errore, operazione prosegue (test) |
| Cancellazione parziale o annullamento | Cooperativo, nessun tocco sugli elementi residui (test) |
| Cancellazione di directory | Nessun codice del genere nel motore (test) |
| Junction sostituita **nell'istante esatto** prima di `File.Delete` | **Rischio residuo non eliminabile** — vedi sotto |

### Rischi residui dichiarati

1. **Finestra TOCTOU residua**: il controllo sul reparse point è l'ultimo prima della
   cancellazione, ma non è atomicamente accoppiato ad essa. Un processo concorrente con diritti
   di scrittura sulla stessa cartella temporanea potrebbe teoricamente sostituire una directory
   con una junction nell'intervallo tra controllo e chiusura della chiamata di sistema.
   Mitigazione reale: l'attacco richiede un processo già in esecuzione come l'utente, e
   `File.Delete` cancella il collegamento (non il bersaglio) quando la componente finale è un
   file reparse point. Eliminare del tutto la finestra richiederebbe l'apertura del file con
   `FILE_FLAG_OPEN_REPARSE_POINT` via P/Invoke, non inclusa in 0.1.
2. **Nessuna firma del codice**, nessuna verifica automatica dell'integrità a runtime.
3. **La conferma è un avviso umano**, non una protezione contro un attaccante locale.
4. **La cancellazione è definitiva**: non esiste cestino né ripristino.
5. **Nessun controllo sulla proprietà dei file**: "temporaneo" significa che il file si trova
   nella cartella temporanea dell'utente e ha più di 24 ore.
6. **Deselezione GUI non automatable in ambiente con sessione bloccata**: con la schermata di
   blocco in primo piano i click fisici non raggiungono la griglia e i messaggi sintetici non
   attivano il toggle della checkbox. La semantica "solo gli elementi selezionati vengono
   cancellati" è verificata dai test `SecurityAuditTests.Ui_*` e dalla validazione pratica al
   motore di produzione con lista esplicita (`tests/qa-practical.ps1`).

### Validazione pratica pre-beta (2026-10-10)

Oltre alla revisione statica e ai test automatici, il percorso reale è stato eseguito con
l'eseguibile pubblicato su vero `%TEMP%` e su un sandbox GUI, usando solo file sintetici
identificabili (`tests/qa-practical.ps1`, esecuzione `f0382398`: **OK**):

- analisi reale del `%TEMP%` dell'utente **senza cancellare nulla** (3234 preesistenti, 0 persi);
- fixture con junction, file bloccato, file recente, file fuori radice;
- regole di età e rifiuto della junction verificati sullo scanner di produzione;
- gate di conferma "No" verificato sulla GUI reale (nessuna cancellazione dopo la risposta);
- cancellazione eseguita dal **motore di produzione** su vero `%TEMP%` con lista esplicita:
  solo il selezionato eliminato, gli altri intatti (hash), bloccato→`Failed`, junction→
  `SkippedReparse`, fuori radice→`SkippedOutsideRoot`;
- residui rimossi dopo verifica di nome/percorso/contenuto; snapshot `%TEMP%` invariato
  (3234 → 3234); SHA-256 del pacchetto invariato.

La deselezione della singola riga nella GUI **non è stata eseguita** (sessione bloccata):
vedi "Rischi residui dichiarati" punto 6.

## Segnalazione di vulnerabilità

Progetto alpha locale, senza canale pubblico dedicato. Per segnalare un problema aprire un
issue indicando:

- versione (`CleanSlate 0.3.0`);
- passi per riprodurre;
- percorso interessato (senza dati personali);
- cosa ci si aspettava e cosa è successo.
