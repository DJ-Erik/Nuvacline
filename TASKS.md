# TASKS — CleanSlate 0.1.0-alpha

Checklist operativa. Ogni voce riporta lo **stato reale** verificato, non l'intenzione.

Legenda: `[ ]` da fare · `[~]` in corso · `[x]` fatto e verificato · `[!]` limite dichiarato

---

## FASE 1 — Ricognizione e architettura

- [x] Ispezione directory corrente e repository Git
- [x] Verifica che la directory non contenga contenuti da sovrascrivere
- [x] Rilevamento SO, architettura, SDK, strumenti di compilazione
- [x] Individuazione di progetti/file preesistenti da preservare
- [x] Creazione di questa checklist
- [x] Scelta architettura/stack (vedi sotto)

### Esito ricognizione (2026-10-09)

| Elemento | Risultato |
|---|---|
| Directory di lavoro | `C:\Users\User\Documents\Experiments` |
| Contenuti preesistenti | `guardiasito/` (Node.js), `slow-pc-rescue-kit/` (script/asset) → **non modificati** |
| Cartella `cleanslate/` | non esisteva → creata da zero, nessuna sovrascrittura |
| Repository Git | repo `main` in `C:\Users\User` **senza alcun commit**; creato repo Git **annidato e isolato** in `cleanslate/` |
| SO | Windows NT 10.0.29671 (build 26xxx serie), 64 bit |
| PowerShell | 5.1.29671.1000 |
| .NET SDK | **NON INSTALLATO** (nessuna cartella `sdk`, nessun vswhere, nessun NuGet cache) |
| Runtime .NET | Microsoft.NETCore.App 8.0.22 e 10.0.11; Microsoft.WindowsDesktop.App 8.0.22 e 10.0.11 |
| .NET Framework | **4.8.1** installato (Release 534289) |
| Compilatore | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` — "Visual C# Compiler 14.8.10001.0 **for C# 5**" |
| MSBuild | solo MSBuild 4.0 in-box (senza targeting pack v4.8) |
| Git | 2.54.0.windows.1 |
| Altri strumenti | Python 3.14.5, `Get-FileHash` (SHA256) |

### Stack scelto e motivazione

- **Target: .NET Framework 4.8, Windows Forms, x64, C# 5.**
- Motivo: è l'unico stack **già installato e supportato** nell'ambiente in grado di produrre una GUI WinForms.
  Non è stato usato né installato alcun SDK: la missione vieta di usare SDK non installati e di installare
  software senza autorizzazione. I runtime .NET 8/10 presenti **non** sono compilabili perché privi di SDK
  e di compilatore Roslyn.
- **Nessuna dipendenza di terze parti, nessun ripristino pacchetti NuGet.** Vantaggio per la privacy:
  l'assenza di rete è verificabile staticamente.
- **xUnit non disponibile** (richiede NuGet) → adottato un **runner di test automatico equivalente**
  scritto in C# (`src/CleanSlate.Tests`), come consentito dal punto 3 dello stack.
- Compilazione tramite `build.ps1` → `csc.exe` in-box (MSBuild 4.0 non può buildare senza targeting pack).

---

## FASE 2 — Progetto e GUI iniziale

- [x] Struttura cartelle `src/CleanSlate`, `src/CleanSlate.Tests`
- [x] `AssemblyInfo` con versione 0.1.0.0 e cultura it-IT
- [x] Manifest applicativo `asInvoker` (nessun privilegio amministrativo) + `longPathAware`
- [x] Finestra principale con nome prodotto, versione, pulsanti, stato, avanzamento, totali
- [x] Operazioni lunghe asincrone con `async/await` e annullamento cooperativo

## FASE 3 — Scanner in sola lettura

- [x] `TempPathProvider`: percorso temporaneo preso dall'ambiente Windows, mai hardcoded
- [x] `PathSafety`: normalizzazione, containment, `..`, reparse point, validazione radice
- [x] `TempFolderScanner`: ricorsione prudente, nessuna scrittura, cancellazione cooperativa
- [x] Esclusione file modificati nelle ultime 24 ore (configurabile)
- [x] Contatori: file analizzati, eleggibili, saltati, cartelle saltate, errori, byte stimati
- [x] Messaggi di errore comprensibili in italiano

## FASE 4 — Anteprima e selezione

- [x] Tabella con Nome, Percorso, Dimensione, Ultima modifica, Categoria, Selezione
- [x] Selezione/deselezione singola con checkbox
- [x] Pulsante pulizia disabilitato senza selezione
- [x] Nessuna categoria fuori ambito preselezionata (nessuna categoria fuori ambito presente)

## FASE 5 — Motore di cancellazione prudente

- [x] Modulo separato dalla scansione (`TempFileCleaner`)
- [x] Finestra di conferma con conteggio e dimensione prima di cancellare
- [x] Ricalcolo percorso + containment + reparse + età + non-radice a ogni elemento
- [x] Nessun comando shell distruttivo, nessuna cancellazione ricorsiva di directory
- [x] File bloccati saltati e conteggiati; riepilogo in memoria

## FASE 6 — Report e gestione errori

- [x] Riepilogo finale: eliminati, byte effettivi, saltati, errori, tempo, non recuperato
- [x] Report TXT generato automaticamente al termine di ogni analisi e di ogni pulizia
      (`%LOCALAPPDATA%\CleanSlate\reports\cleanslate-<timestamp>.txt`)
- [x] Percorso del report mostrato nella finestra (`lblReportPath`); "Apri report" lo apre
- [x] Sanitizzazione percorsi (sostituzione radice con `%TEMP%`), nessun username
- [x] Assenza di scritture durante la costruzione del report in memoria (test automatico)

## FASE 7 — Test di sicurezza e regressione

- [x] Runner di test automatico (equivalente a xUnit, zero dipendenze)
- [x] Test 1 — directory vuota
- [x] Test 2 — file di dimensioni note
- [x] Test 3 — file più recente del limite
- [x] Test 4 — file più vecchio del limite
- [x] Test 5 — directory temporanea inesistente
- [x] Test 6 — file bloccato / non accessibile
- [x] Test 7 — file eliminato tra scansione e pulizia
- [x] Test 8 — percorso esterno alla radice autorizzata
- [x] Test 9 — traversal con `..`
- [x] Test 10 — collegamento simbolico / reparse point
- [x] Test 11 — directory annidata
- [x] Test 12 — cancellazione parzialmente fallita
- [x] Test 13 — annullamento scansione
- [x] Test 14 — annullamento pulizia cooperativo
- [x] Test 15 — scansione in sola lettura non modifica il fixture
- [x] Test 16 — la cancellazione non raggiunge directory esterne
- [x] Test 17 — esportazione report esplicita e locale
- [x] Test extra — statica privacy (nessuna API di rete/registro/telemetria nelle fonti)
- [x] Test extra — assembly referenziati senza assembly di rete
- [x] Test audit (FASE 10) — file toccato dopo la scansione → saltato
- [x] Test audit (FASE 10) — file sostituito da directory dopo la scansione → saltato
- [x] Test audit (FASE 10) — sottocartella sostituita da junction dopo la scansione → saltato
- [x] Test audit (FASE 10) — solo gli elementi passati al motore vengono cancellati
- [x] Test audit (FASE 10) — GUI: pulsante pulizia disabilitato senza selezione
- [x] Test audit (FASE 10) — GUI: l'elemento deselezionato non arriva al motore e sopravvive
- [x] Test audit (FASE 10) — GUI: la selezione resta corretta dopo l'ordinamento
- [x] Test audit (FASE 10) — percorsi con spazio/punto finale, prefissi `\\?\` e `\\.\`, ADS

**Totale suite: 73 test, 73 superati su 3 esecuzioni consecutive, nessun residuo di fixture.**

## FASE 8 — Build Release portable

- [x] Build x64 Release in `bin/Release/CleanSlate.exe`
- [x] Pubblicazione in `artifacts/alpha-0.1/` (exe singolo autonomo)
- [x] `README.txt` nella build
- [x] `SHA256SUMS.txt`
- [x] Nota licenza/dipendenze nella build

## FASE 9 — Verifica finale e documentazione

- [x] `README.md` — requisiti, uso, limiti, comandi di build e test, stato verificato
- [x] `SECURITY.md` — modello di minaccia, 8 difese, limiti dichiarati
- [x] `PRIVACY.md` — dati trattati, verifiche statiche ed eseguite in esecuzione, limiti
- [x] `CHANGELOG.md` — 0.1.0-alpha
- [x] Documentazione test e comandi di build
- [x] Avvio eseguibile verificato nell'ambiente di sviluppo
- [x] Verifica statica privacy (sorgenti + assembly)
- [x] Verifica end-to-end GUI automatizzata (`tests/verify.ps1`): **31/31 controlli** — avvio
      finestra, stato iniziale (elenco vuoto e pulsante pulizia disabilitato), pressione
      "Analizza", stato terminale, riepilogo letto, tabella con lo stesso numero di elementi
      proposti e pulsante pulizia abilitato, percorso report letto, file di report presente e
      conforme, pulsanti presenti, chiusura, nessun processo residente, registro invariato,
      servizi invariati, attività invariate, nessun avvio automatico, 0 connessioni TCP in uscita
- [~] Verifica traffico di rete in esecuzione: **campionamento runtime** (`Get-NetTCPConnection`
      per PID, almeno 40 campionamenti per esecuzione, 0 connessioni) + verifica statica.
      Non è una cattura pacchetti a livello di rete → limite dichiarato in `PRIVACY.md`

## FASE 10 — Audit di sicurezza pre-distribuzione (2026-10-09)

Revisione indipendente del percorso scansione → cancellazione. La build precedente è stata
conservata in `artifacts/audit-baseline/` prima di ogni modifica.

- [x] Ispezione dei 4 commit recenti, di `TASKS.md` e della build pubblicata
- [x] Analisi del percorso completo scansione → cancellazione (`TempFolderScanner`,
      `PathSafety`, `TempFileCleaner`, `MainForm`)
- [x] Verifica della ricontrollazione immediatamente prima di ogni cancellazione
- [x] Caccia a traversal, reparse point, junction, symlink, file sostituiti, radice,
      percorsi non autorizzati, `\\?\`, `\\.\`, ADS, spazi e punti finali, nome breve 8.3
- [x] **Problema trovato e corretto**: il controllo reparse point era eseguito *prima* della
      lettura di dimensione/età, con una finestra TOCTOU verso `File.Delete`. Il controllo è
      stato spostato a essere l'ultimo prima della cancellazione
      (`TempFileCleaner.ProcessOne`)
- [x] Test di regressione sullo scenario junction sostituita dopo la scansione
- [x] Verifica che gli elementi deselezionati non possano essere cancellati (test GUI + test
      motore) e che la selezione sia visibile e stabile anche dopo l'ordinamento
- [x] Verifica file recenti e file modificati dopo la scansione → saltati secondo le regole
- [x] Verifica file bloccati, accesso negato, cancellazioni parziali, annullamento cooperativo
- [x] Ispezione della build pubblicata: assembly referenziati, tipi pubblici, nomi di metadati
      di rete e di registro, stringhe `http://`, file di configurazione, manifest `asInvoker`
- [x] Ripetizione della verifica end-to-end sull'eseguibile reale (**31/31**)
- [x] Aggiornamento di `SECURITY.md`, `PRIVACY.md`, `README.md`, `CHANGELOG.md` e di questa
      checklist

---

## Criteri di completamento 0.1 Alpha

| # | Criterio | Stato | Evidenza |
|---|---|---|---|
| 1 | La GUI si avvia | [x] | `verify.ps1`: finestra creata, titolo letto |
| 2 | L'analisi funziona | [x] | `verify.ps1`: stato "Analisi completata." + riepilogo letto |
| 3 | I risultati sono visualizzati correttamente | [x] | `verify.ps1`: righe in tabella (3109) = "Proposti" del riepilogo (3109) |
| 4 | La selezione degli elementi funziona | [x]* | `verify.ps1`: pulsante pulizia **disabilitato** senza selezione e **abilitato** dopo l'analisi. `SecurityAuditTests.Ui_*`: la riga deselezionata non arriva al motore e sopravvive, e la selezione resta corretta anche dopo l'ordinamento. Il clic sulla singola checkbox **non** è automatabile: la griglia WinForms non espone le righe come elementi UIA |
| 5 | La pulizia richiede conferma esplicita | [x]* | Dialogo di conferma con numero/byte e default **No**; **non eseguito in automatico** per non cancellare i file temporanei reali dell'utente. Il motore di cancellazione è coperto da `CleanerTests` e `SecurityAuditTests` su fixture dedicati |
| 6 | I percorsi esterni vengono rifiutati | [x] | `PathSafetyTests` + `SecurityAuditTests.PathSafety_*` (`..`, reparse, radice, `\\?\`, `\\.\`, ADS, spazi/punti finali) |
| 7 | Gli errori non provocano crash non gestiti | [x] | `ScannerTests`/`CleanerTests` (ACL negata, file bloccato, cartella inesistente) |
| 8 | La reportistica funziona | [x] | `ReportTests` + `verify.ps1` (file creato, contenuto conforme, percorso mostrato in GUI) |
| 9 | La suite di test obbligatori passa | [x] | **73/73** su 3 esecuzioni consecutive |
| 10 | La build Release viene generata | [x] | `artifacts/alpha-0.1/` + `SHA256SUMS.txt` ricalcolato e riverificato |
| 11 | L'eseguibile viene avviato e verificato | [x] | `verify.ps1` **31/31** |
| 12 | La documentazione descrive il prodotto | [x] | `README.md`, `SECURITY.md`, `PRIVACY.md`, `CHANGELOG.md` |
| 13 | Nessun bug bloccante noto non dichiarato | [x] | vedi "Bug noti e limiti dichiarati" |
| 14 | `TASKS.md` riporta il risultato reale di ogni criterio | [x] | Tabella con evidenza per riga |

`[x]*` = criterio soddisfatto, con una parte non automatizzabile dichiarata nel seguito.

---

## Bug noti e limiti dichiarati

1. **Nessun SDK .NET in ambiente**: la build usa `csc.exe` in-box di .NET Framework (C# 5),
   non il SDK .NET. Conseguenza: niente xUnit (runner equivalente incluso), niente
   `<Project Sdk>` moderno. Documentato in `README.md`.
2. **Percorsi molto lunghi (>260 caratteri)**: il manifest dichiara `longPathAware`, ma il
   runtime .NET Framework può comunque rifiutare alcuni percorsi lunghi. Tali elementi vengono
   **saltati e conteggiati come errori**, mai fatti fallire il programma.
3. **Verifica di rete in esecuzione non completa**: in questo ambiente non è disponibile uno
   strumento di cattura pacchetti utilizzabile senza privilegi elevati. Sono state eseguite la
   verifica statica (sorgenti + assembly) e una **verifica di runtime per campionamento** delle
   connessioni TCP del processo (almeno 40 campionamenti durante un'analisi reale, 0 connessioni).
   Una trasmissione brevissima tra due campionamenti non sarebbe rilevata: limite dichiarato
   in `PRIVACY.md`.
4. `System.dll` è referenziato (necessario per `System.Diagnostics.Stopwatch`): è una assembly
   del framework .NET che contiene anche classi di rete, ma **nessuna** di tali classi è usata
   dal codice; la cosa è verificata da test automatici sulle sorgenti.
5. `tests/verify.ps1` usa `BM_CLICK` sull'handle del controllo perché WinForms su .NET
   Framework espone i controlli tramite il bridge MSAA e non offre `InvokePattern`.
6. **Nessuna pulizia automatizzata contro il vero `%TEMP%`**: la verifica end-to-end esegue
   soltanto l'analisi. Avviare in automatico la cancellazione sui file temporanei reali
   dell'utente eliminerebbe dati dell'utente durante un test, quindi il flusso di conferma è
   dichiarato ma non automatizzato; il motore di cancellazione è invece esercitato a fondo
   dall'unità di test su fixture create apposta.
7. **Selezione riga per riga non automatabile**: la griglia WinForms non espone le righe come
   elementi di automazione, quindi il clic su una singola checkbox non è coperto dalla verifica
   end-to-end. È invece coperto da test che esercitano lo stesso codice della GUI
   (`SecurityAuditTests.Ui_*`).
8. **Finestra TOCTOU residua verso `File.Delete`**: il controllo sul reparse point è l'ultimo
   prima della cancellazione ma non è atomicamente accoppiato ad essa. Un processo concorrente
   con diritti di scrittura sulla stessa cartella potrebbe sostituire una directory con una
   junction in quell'intervallo. Eliminare del tutto la finestra richiederebbe l'apertura del
   file con `FILE_FLAG_OPEN_REPARSE_POINT` via P/Invoke: non inclusa in 0.1.
9. **Nessun contatore di selezione nella GUI**: la selezione è visibile solo tramite la
   colonna "Selez." e lo stato del pulsante; il conteggio degli elementi selezionati compare
   soltanto nella finestra di conferma. Non è una falla di sicurezza (la conferma mostra
   comunque numero e dimensione prima di cancellare) ma è una lacuna di usabilità non
   corretta in questa versione, per non introdurre funzionalità nuove durante l'audit.
10. **Build non riproducibile bit per bit**: `csc.exe` di .NET Framework incorpora un MVID
   diverso a ogni compilazione, quindi la stessa sorgente produce un'eseguibile con hash
   SHA-256 diverso. `SHA256SUMS.txt` va quindi verificato **solo rispetto al pacchetto con
   cui è stato generato**, non rispetto a una ricompilazione locale.

## Riepilogo finale (stato reale)

| Verifica | Risultato |
|---|---|
| Compilazione Release x64 | `bin/Release/CleanSlate.exe` |
| Suite di test | **73/73** su 3 esecuzioni consecutive |
| Residui del fixture di test dopo la suite | **0** |
| Verifica end-to-end GUI (`tests/verify.ps1`) | **31/31** su `bin/Release` e di nuovo **31/31** sull'exe del pacchetto |
| Connessioni TCP in uscita durante un'analisi reale | **0** / almeno 40 campionamenti per esecuzione |
| Registro, servizi, attività, avvio automatico | Invariati |
| Processo residente dopo la chiusura | Nessuno |
| Pacchetto portable | `artifacts/alpha-0.1/` (exe, README.txt, LICENSE.txt, SHA256SUMS.txt) |
| Pacchetto == `bin/Release` | SHA-256 identici nella stessa sessione di build (build non riproducibile) |
| Build pre-audit conservata | `artifacts/audit-baseline/` (SHA-256 `bf33e9cb…`) |
| Documentazione | `README.md`, `SECURITY.md`, `PRIVACY.md`, `CHANGELOG.md` |

## Tabella dell'audit pre-distribuzione

| Controllo | Metodo | Risultato | Evidenza | Limite residuo |
|---|---|---|---|---|
| Perimetro e commit | `git log --stat` sui 4 commit | 4 commit, working tree pulito | storico Git | — |
| Percorso scansione → cancellazione | Lettura di `TempPathProvider`, `PathSafety`, `TempFolderScanner`, `TempFileCleaner`, `MainForm` | Catena di verifiche completa e documentata | sorgenti | — |
| Ricontrolla ogni elemento prima di cancellare | Lettura di `TempFileCleaner.ProcessOne` | Sì: normalizzazione, radice, containment, esistenza, attributi, reparse, età | `TempFileCleaner.cs` | — |
| Traversal `..` | Test + sonda runtime | Rifiutato | `IsInsideRoot_TraversalEscapingRoot_ReturnsFalse` | — |
| Prefissi `\\?\` / `\\.\` | Test | Rifiutati (fail-safe) | `PathSafety_PrefixedDeviceOrDosDevicePath_IsRejected` | — |
| Stream dati alternativo | Test | Rifiutato senza eccezione | `PathSafety_AlternateDataStream_IsRejectedWithoutCrash` | — |
| Spazio / punto finale | Test + sonda runtime | Normalizzati dentro la radice | `PathSafety_TrailingSpaceOrDot_StillResolveInsideRoot` | — |
| Nome breve 8.3 | Sonda runtime | `GetFullPath` espande al nome lungo | sonda su volume C: | Non coperto da test automatico |
| Junction / symlink | Test | Non seguiti in scansione e rilevati in cancellazione | `Clean_PathThroughJunction_…`, `Clean_PathComponentReplacedByJunctionAfterScan_…` | Finestra residua fino a `File.Delete` |
| File sostituito dopo la scansione | Test (directory, junction, tocco, eliminazione) | Sempre saltato, contenuti intatti | 4 test `SecurityAuditTests`/`CleanerTests` | — |
| Elementi non selezionati | Test GUI + test motore | Non arrivano al motore e sopravvivono | `Ui_UncheckedItemsAreNotPassedToCleanerAndSurvive`, `Clean_OnlyItemsExplicitlyPassedToCleanerAreDeleted` | Clic su singola checkbox non automatabile |
| Visibilità della selezione | Test GUI + lettura e2e | Colonna "Selez.", pulsante abilitato/sdisabilitato, conferma con conteggio | `verify.ps1`, `SecurityAuditTests.Ui_*` | Nessun contatore "N selezionati" in finestra |
| File recenti | Test | Saltati in scansione e in cancellazione | `Scan_FileNewerThanLimit_IsExcluded`, `Clean_FileNewerThanLimit_IsSkipped` | — |
| File bloccati / ACL negate | Test | Saltati o conteggiati come errore, operazione prosegue | `Clean_LockedFile_…`, `Clean_ReadOnlyFile_…`, `Scan_AccessDeniedDirectory_…` | — |
| Cancellazione parziale / annullamento | Test | Cooperativo, nessun tocco sui residui | `Clean_CancellationIsCooperative…`, `Scan_CancellationDuringScan…` | — |
| Build pubblicata: dipendenze | Riflessione sull'exe pubblicato | Solo assembly del framework .NET | elenco assembly referenziati | — |
| Build pubblicata: rete/registro | Scansione nomi di metadati e stringhe | Nessun nome sospeto; `http://` solo namespace XML del manifest | audit build | Non è una cattura pacchetti |
| Eseguibile reale | `tests\verify.ps1` | 31/31 su `bin/Release` e 31/31 sull'exe del pacchetto | log della verifica | Pulizia reale non automatizzata |

## Prossimo passo concreto (se la sessione dovesse interrompersi)

```powershell
cd C:\Users\User\Documents\Experiments\cleanslate
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1   # 73/73 attesi
powershell -ExecutionPolicy Bypass -File tests\verify.ps1      # 31/31 attesi
powershell -ExecutionPolicy Bypass -File build.ps1 -Package    # pacchetto portable
```
