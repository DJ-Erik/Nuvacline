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

---

## Criteri di completamento 0.1 Alpha

| # | Criterio | Stato |
|---|---|---|
| 1 | La GUI si avvia | [x] |
| 2 | L'analisi funziona | [x] |
| 3 | I risultati sono visualizzati correttamente | [x] |
| 4 | La selezione degli elementi funziona | [x] |
| 5 | La pulizia richiede conferma esplicita | [x] |
| 6 | I percorsi esterni vengono rifiutati | [x] |
| 7 | Gli errori non provocano crash non gestiti | [x] |
| 8 | La reportistica funziona | [x] |
| 9 | La suite di test obbligatori passa | [x] |
| 10 | La build Release viene generata | [x] |
| 11 | L'eseguibile viene avviato e verificato | [x] |
| 12 | La documentazione descrive il prodotto | [x] |
| 13 | Nessun bug bloccante noto non dichiarato | [x] — vedi "Bug noti" |
| 14 | `TASKS.md` riporta il risultato reale di ogni criterio | [x] |

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

## Riepilogo finale (stato reale)

| Verifica | Risultato |
|---|---|
| Compilazione Release x64 | `bin/Release/CleanSlate.exe` (build ripetuta) |
| Suite di test | **63/63** su 3 esecuzioni consecutive |
| Residui del fixture di test dopo la suite | **0** (dopo il fix del ripristino ACL con `icacls /reset`) |
| Verifica end-to-end GUI (`tests/verify.ps1`) | **31/31** |
| Connessioni TCP in uscita durante un'analisi reale | **0** / almeno 40 campionamenti per esecuzione |
| Registro, servizi, attività, avvio automatico | Invariati |
| Processo residente dopo la chiusura | Nessuno |
| Pacchetto portable | `artifacts/alpha-0.1/` (exe, README.txt, LICENSE.txt, SHA256SUMS.txt) |
| Documentazione | `README.md`, `SECURITY.md`, `PRIVACY.md`, `CHANGELOG.md` |

## Prossimo passo concreto (se la sessione dovesse interrompersi)

```powershell
cd C:\Users\User\Documents\Experiments\cleanslate
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1   # 63/63 attesi
powershell -ExecutionPolicy Bypass -File tests\verify.ps1      # 31/31 attesi
powershell -ExecutionPolicy Bypass -File build.ps1 -Package    # pacchetto portable
```
