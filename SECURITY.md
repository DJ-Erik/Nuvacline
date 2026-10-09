# Sicurezza di CleanSlate

Documento relativo alla versione **0.1.0-alpha**.

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
- assenza di componenti di reparse point;
- età minima (24 ore di impostazione predefinita);
- che il percorso non sia la radice stessa.

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

## Segnalazione di vulnerabilità

Progetto alpha locale, senza canale pubblico dedicato. Per segnalare un problema aprire un
issue indicando:

- versione (`CleanSlate 0.1.0-alpha`);
- passi per riprodurre;
- percorso interessato (senza dati personali);
- cosa ci si aspettava e cosa è successo.

## Limiti dichiarati

1. **Nessuna firma del codice**: l'eseguibile non è firmato digitalmente.
2. **Nessuna verifica automatica dell'integrità a runtime**: l'integrità del pacchetto è
   verificabile con `SHA256SUMS.txt`, non dall'applicazione stessa.
3. **La conferma è un avviso umano, non una protezione contro un attaccante locale**: chi può
   modificare il processo può anche modificarne il comportamento.
4. **La cancellazione è definitiva**: non esiste cestino né ripristino.
5. **Nessun controllo sulla proprietà dei file**: "temporaneo" significa che il file si trova
   nella cartella temporanea dell'utente e ha più di 24 ore.
