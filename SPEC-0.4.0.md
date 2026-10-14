# Specifica tecnica — 0.4.0 Rimozione prudente delle cartelle vuote

**Stato: AUTORIZZATA e IMPLEMENTATA (2026-10-14).** Le decisioni
vincolanti qui sotto sono state confermate per iscritto: opzione **disattivata di
default**, fase **opzionale, esplicita e distinta** dalla pulizia dei file, ambito
limitato alla directory temporanea validata. Implementazione completa in
`EmptyFolderCleaner.cs` + wiring `MainForm` + 22 test `EmptyFolderCleanerTests`.
Il prodotto rilasciato alla 0.3.6 (commit `c1d0c4f`) non modifica cartelle: il motore di
pulizia rimuove solo file singoli, verificati uno a uno. Nessuna riga di questa specifica
era stata implementata prima dell'autorizzazione.

Motivazione: dopo una pulizia, le cartelle diventate vuote sotto la radice autorizzata
restano sul disco. La 0.4.0 le rimuoverbbe applicando le stesse regole di sicurezza già
validate per i file. È un **cambio di contratto** ("rimuove file" → "rimuove file e
cartelle diventate vuote"): per questo è un incremento **minor**, non un patch.

---

## 1. Comportamento atteso

- **Attivazione**: solo al termine di una pulizia esplicitamente confermata dall'utente e
  **non annullata**. Mai dopo la sola analisi (che resta in sola lettura, invariata).
- **Fase separata**: con l'opzione attiva, dopo la pulizia dei file viene eseguito un
  **sondaggio in sola lettura** delle cartelle vuote e poi una **conferma dedicata** con
  il numero di cartelle candidate (default **No**). Rifiutare la conferma (oppure avere
  l'opzione spenta) lascia il sistema esattamente come prima: nessuna rimozione, nessuna
  voce nel report e nessuna sezione nel riepilogo. Fase e pulizia dei file sono quindi
  **opzionali, esplicite e distinte** tra loro.
- **Impostazione**: voce "Rimuovi anche le cartelle diventate vuote" nel menu ⚙,
  **non spuntata di default** (decisione vincolante: il default eredita il comportamento
  0.3.6), persistita con `ScanSettings` nella chiave indipendente
  `RemoveEmptyFolders=<0|1>` (le chiavi esistenti `MinAgeHours`/`KeepReports` restano
  intatte, come già previsto dal formato; valore mancante o non valido ⇒ disattivata).
- **Selezione**: si considerano **tutte** le sottocartelle della radice che risultano
  vuote al momento della fase di rimozione, non solo quelle da cui sono stati cancellati
  file in questa esecuzione. Motivo: pulizie parziali/annullate in precedenza possono
  aver lasciato cartelle vuote.
- **Ordine**: post-order (le figlie prima dei padri); ogni rimozione è indipendente.
- **Candidatura (sondaggio)**: una sottocartella viene proposta se non contiene file
  diretti e se tutti i suoi discendenti directory sono anch'essi proposti (i reparse
  point la disqualificano, perché sopravviverebbero alla fase). Serve per rimuovere in
  una sola esecuzione un albero annidato tutto vuoto (test §5.3) senza mai toccare file.
- **Criterio "vuota" (re-verifica in Remove)**: `Directory.EnumerateFileSystemEntries`
  non restituisce nulla (né file, né cartelle, né altri elementi) al momento della
  cancellazione. In post-order la figlia è già stata rimossa, quindi il padre risulta
  effettivamente vuoto nell'istante dell'azione; se non lo è (race), la cartella viene
  conservata e l'esito registrato.
- **Soglia d'età (`MinAge`)**: **non** viene applicata alle cartelle. Su NTFS la
  cancellazione di un file aggiorna il `LastWriteTimeUtc` della cartella che lo conteneva:
  applicare la soglia dopo le cancellazioni escluderebbe sistematicamente le cartelle
  appena svuotate. La sicurezza non dipende dall'età per directory vuote, che non
  contengono dati.
- **Esiti** (modellati come per i file, enum dedicato `EmptyDirectoryOutcome`):
  `Removed`, `SkippedNotFound`, `SkippedNotEmpty`, `SkippedReparse`,
  `SkippedOutsideRoot`, `SkippedRoot`, `SkippedInvalidPath`, `SkippedCancelled`,
  `Failed`. Contatori e item risiedono in classi dedicate (`EmptyFolderSurvey` per il
  sondaggio, `EmptyCleanResult` per la rimozione): `CleanResult` dei file **non** viene
  modificato, così la 0.3.6 resta intatta quando l'opzione è spenta.
- **Riepilogo e report**: nuova riga "Cartelle vuote rimosse: N" (TXT) e chiave
  `cartelleVuoteRimosse` (JSON), insieme a saltati/errori, **emessi solo se la fase è
  stata davvero eseguita** (con l'opzione spenta il report è identico alla 0.3.6).
  Nessun percorso assoluto: la radice è sostituita dal segnaposto `%TEMP%` come per i
  file.
- **Annullamento**: cooperativo, come per i file; se l'utente annulla durante la fase
  cartelle, l'operazione termina dichiarando "pulizia annullata" e i conteggi parziali.

## 2. Limiti di percorso

- Unica radice ammessa: quella restituita da `TempPathProvider.GetAuthorizedRoot()` e
  validata da `PathSafety.ValidateCleaningRoot` (solo `%TEMP%` dell'utente corrente,
  disco locale fisso, mai unità di disco, mai risorse `\\` di rete).
- Ogni candidata deve superare `PathSafety.IsInsideRoot(root, dir)`: confronto testuale
  dopo normalizzazione, con separatore di confine obbligatorio; la **radice stessa non è
  mai candidata** (doppio controllo esplicito, come per i file: `SkippedRoot`).
- Percorsi non normalizzabili (`TryNormalize` fallisce) → `SkippedInvalidPath`, mai
  eccezione a UI.
- Solo directory reali: attributo `Directory` presente e attributo `ReparsePoint` assente.
- Lunghezza percorsi: nessuna soglia aggiuntiva; i percorsi non gestibili producono
  eccezioni I/O catturate e conteggiate come `Failed`, non interrompono l'operazione.
- Profondità: nessun limite artificiale. La ricorsione è guidata dall'enumerazione reale
  e termina quando non restano cartelle vuote; la radice è il limite intrinseco.

## 3. Gestione di junction e symlink

- **Mai seguire**: l'enumerazione dei figli controlla `FileAttributes.ReparsePoint`
  cartella per cartella (stessa regola della scansione in sola lettura).
- **Mai rimuovere**: una directory che è un reparse point (junction, symlink di
  directory, mount point) **non viene mai rimossa anche se vuota**. Rimuovere un link
  significherebbe rimuovere un collegamento creato da qualcun altro: fuori contratto,
  ambiguo e inutile per la pulizia dei temporanei. Esito: `SkippedReparse`.
- I file che sono reparse point non vengono mai toccati (già vigente in 0.3.6).
- **Re-verifica anti-TOCTOU**: subito prima di `Directory.Delete(path, false)`:
  1. `PathSafety.HasReparseComponentInsideRoot(root, dir)` (se un componente intermedio
     diventa junction nella finestra, la cancellazione viene saltata);
  2. `Directory.Exists(dir)`;
  3. enumerazione vuota ripetuta.
- `Directory.Delete` è chiamato **senza ricorsione**: se la cartella non è più vuota
  (race), l'operazione fallisce con errore contegiato e **nulla** viene perso.

## 4. Protezione delle cartelle utente

- La rimozione non può in alcun modo uscire dalla radice autorizzata: il containment è
  lo stesso testuale già sottoposto a 17 test (`PathSafetyTests`) e agli audit TOCTOU.
- Profilo utente, Documenti, AppData (al di fuori della radice), unità di sistema:
  irraggiungibili per costruzione (la radice è solo `%TEMP%` validato).
- La radice stessa non viene mai rimossa, né in caso normale né con item corrotti
  (test dedicato, come `Clean_RootPath_IsNeverDeleted` per i file).
- Nessuna operazione su percorsi relativi, con `..`, prefissi `\\?\`, ADS o device path:
  normalizzazione + containment + test esistenti in `SecurityAuditTests`.
- Ordine post-order limitato all'albero sotto la radice; nessuna ricorsione su percorsi
  ricostruiti dall'input dell'utente (la lista non include mai la radice).

## 5. Test di sicurezza previsti (bloccanti per il rilascio)

1. Cartella vuota sotto radice → rimossa; la radice resta.
2. Cartella con anche un solo file (recente o non selezionato) → **non** rimossa.
3. Albero annidato tutto vuoto → rimosso in ordine post-order (figlia prima del padre).
4. Junction vuota sotto radice → **non** rimossa, **non** seguita; il contenuto esterno
   resta intatto (verifica per hash).
5. Cartella esterna alla radice (sibling, es. `Outside`) → mai toccata (snapshot invariato).
6. Percorso con `..` che risale fuori radice → `SkippedOutsideRoot`.
7. Race: cartella sostituita da junction tra enumerazione e delete → skip reparse.
8. Race: file comparso nella cartella tra enumerazione e delete → `Directory.Delete`
   fallisce, errore conteggiato, contenuto intatto.
9. Item radice passato male al motore → `SkippedRoot`.
10. Annullamento cooperativo durante la fase cartelle → conteggi parziali, nessuna
    cancellazione oltre il punto di annullamento.
11. Cartella con ACL negate → `Failed` conteggiato, le altre procedono.
12. Disattivazione dell'impostazione → zero directory rimosse.
13. Snapshot `Fixture.Snapshot` prima/dopo: nessuna modifica fuori dall'elenco atteso.
14. Report: riga/chiave presenti, percorsi sanitizzati con `%TEMP%`, nessun profilo utente.
15. `PrivacyTests` invariati e superati (nessuna API vietata introdotta).
16. La cancellazione dei **file** resta identica: tutti i 159 test attuali devono
    continuare a superarsi senza modifiche di aspettativa (esclusi i 5 clipboard
    ambientali, già registrati).

## 6. Criteri di accettazione complessivi

- [x] Perimetro rispettato: solo directory vuote sotto la radice `%TEMP%` validata; zero
      rimozioni fuori radice; zero reparse point rimossi o seguiti.
- [x] Comportamento predefinito 0.3.6 per i file **invariato** (suite attuale invariata).
- [x] Nuovi test §5 tutti superati; suite totale **181** (159 attuali + 22 nuovi).
- [ ] `verify.ps1` 41/41 sull'exe del pacchetto; eventuale nuovo check documentato.
- [x] Scan stringhe exe: nessun token vietato aggiuntivo (PrivacyTests + audit manuale).
- [x] Riepilogo e report dichiarano i conteggi; nessun dato personale o percorso utente.
- [x] Docs aggiornate: CHANGELOG 0.4.0, README (contratto e limiti), TASKS (FASE dedicata),
      SECURITY (nuova superficie di cancellazione descritta), PRIVACY (nessun nuovo dato).
- [ ] Bump `0.4.0.0`/`0.4.0`, pacchetto `artifacts/alpha-0.4.0/` con SHA256SUMS;
      i pacchetti `alpha-0.3`, `alpha-0.3.5`, `alpha-0.3.6` restano intatti.
- [ ] Commit locale della release; **nessun push**.

## 7. Perimetro delle modifiche (file previsti)

| File | Modifica |
|---|---|
| `src/CleanSlate/Core/EmptyFolderCleaner.cs` | Nuovo: classe separata (review sicurezza dedicata) |
| `src/CleanSlate/Core/CleanModels.cs` | Enum `EmptyDirectoryOutcome` + classi `EmptyFolderSurvey`/`EmptyCleanResult` (conteggi separati, `CleanResult` invariata) |
| `src/CleanSlate/Core/ScanSettings.cs` | Chiave `RemoveEmptyFolders` (default **disattivata**) |
| `src/CleanSlate/UI/MainForm.cs` | Fase opzionale dopo pulizia non annullata, menu ⚙, riepilogo |
| `src/CleanSlate/Core/ReportBuilder.cs`, `ReportExporter.cs` | Righe TXT/JSON dei conteggi |
| `src/CleanSlate.Tests/EmptyFolderCleanerTests.cs` | Nuovo: test §5 |
| `src/CleanSlate.Tests/InfoSettingsTests.cs` | Test persistenza nuova chiave |
| `AssemblyInfo.cs`, `app.manifest`, `AppInfo.cs`, `build.ps1`, `tests/verify.ps1`, `tests/qa-practical.ps1` | Bump 0.4.0 |
| `CHANGELOG.md`, `README.md`, `TASKS.md`, `SECURITY.md` | Documentazione |

Il motore di **scansione resta in sola lettura**, senza modifiche.

## 8. Fuori perimetro in 0.4.0

- Rimozione di reparse point/junction/symlink (mai, in questa versione).
- Svuotamento o cancellazione di cartelle non vuote, "deep clean", pulizia di unità.
- Qualsiasi operazione fuori `%TEMP%` utente.
- Modifiche al motore di scansione, alla soglia d'età o al flusso di conferma.
- Funzionalità di rete, registro o telemetria (nulle, come sempre).

## 9. Rischio residuo dichiarato

- **Finestra TOCTOU** tra enumerazione vuota e `Directory.Delete`: mitigata dalla
  re-verifica immediata e dall'uso di `Directory.Delete` non ricorsivo (se la cartella
  non è più vuota, fallisce senza perdere nulla). Stesso ordine di grandezza della
  finestra già accettata per i file e documentata in `TempFileCleaner`.
- **Ricreazione simultanea**: un'app che ricrea la cartella subito dopo la rimozione la
  troverà vuota e pronta all'uso (le cartelle temporanee vengono ricreate su richiesta);
  la rimozione di una cartella **vuota** non può perdere dati.
- **Timestamp directory**: per questo la soglia d'età non si applica alle cartelle
  (§1); nessun impatto sulla sicurezza.
