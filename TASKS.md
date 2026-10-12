# TASKS — CleanSlate 0.3.5

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

## FASE 11 — Validazione pratica pre-beta (2026-10-10)

Esecuzione del percorso reale **analisi → conferma → (cancellazione)** con l'eseguibile
pubblicato (`artifacts/alpha-0.1/CleanSlate.exe`), su vero `%TEMP%` e su un sandbox usato
come TMP del processo GUI, con **solo file sintetici** identificabili (prefisso
`cleanslate-qa-<id>`, marker `CLEANSLATE-QA-<id>`, dimensioni distinte). Script:
`tests/qa-practical.ps1` (eseguito via `tests/qa-run.ps1`, log in `tests/qa-run.log`).

**Id esecuzione di riferimento: `f0382398` — esito: OK (tutti i controlli superati).**

### Test superati

| Area | Criterio (A–I) | Evidenza |
|---|---|---|
| A | Analisi reale del vero `%TEMP%` senza cancellare nulla | 3234 file preesistenti → 0 scomparsi; report scritto; finestra chiusa senza residui |
| B | Fixture sintetiche con nomi univoci e contenuto noto | 8 file nel vero `%TEMP%`, 5 nel sandbox, 1 vittima esterna, 1 junction; tutti con marker verificato prima della rimozione |
| C | Regole di età (≥24 h) | Scanner reale propone solo i 5 vecchi della fixture; il recente (500 B) risulta "Scartati"; delta GUI reale ≥ +5 proposti / +1 scartato |
| D | Percorso analisi → conferma → cancellazione | GUI sandbox: analisi (4 proposti, 1 scartato), conferma "Eliminare 4 file … 1000 B", risposta **No** → nessuna cancellazione; cancellazione poi eseguita dal **motore di produzione** su vero `%TEMP%` con lista esplicita (stesso codice che la GUI alimenta con `GetSelectedItems`) |
| E | Solo il selezionato eliminato, gli altri intatti | Motore: `selected.txt` eliminato; `old`, `unselected`, `modified`, `locked`, recente e vittima fuori radice **invariati** (hash) |
| F | File bloccato (`FileShare.None`) | `locked.txt` sopravvissuto con esito `Failed` in entrambi gli ambienti (vero `%TEMP%` e sandbox) |
| G | Regola dei 24 h | `recent.txt` mai proposto né toccato |
| H | Report/riepilogo con soli conteggi | Report reale scritto e privo di profilo utente; riepilogo pulizia motore con conteggi attesi (1 eliminato, 1 errore); e2e **31/31**, suite **73/73** |
| I | Nessun residuo, build conservata | Snapshot `%TEMP%` 3234 → 3234; 0 residui `cleanslate-qa-*`/`CleanSlateQa*`; 0 processi; SHA-256 pacchetto invariato (`0F74C687…`) |

### Test non eseguiti (e perché)

| Test | Motivo |
|---|---|
| Deselezione della singola riga nella GUI reale | Sessione Windows bloccata (schermata di blocco in primo piano): i click del mouse vengono intercettati dal lock screen e non raggiungono la griglia; i messaggi sintetici (`WM_LBUTTONDOWN`, spazio, focus) non attivano il toggle della checkbox WinForms. La selezione parziale è verificata dai test `SecurityAuditTests.Ui_*` e dal motore con lista esplicita |
| Clic "Sì" sulla conferma GUI | Conseguenza del punto precedente: senza deselezione, premere Sì avrebbe cancellato anche `unselected.txt` del sandbox, falsando la prova di sopravvivenza. Il gate "No" è comunque verificato; la cancellazione è verificata al motore di produzione |

### Limiti residui confermati

- Finestra TOCTOU fino a `File.Delete` (come già dichiarato in `SECURITY.md`);
- Campionamento rete ≠ cattura pacchetti (come già dichiarato in `PRIVACY.md`);
- Deselezione GUI non automatable in questo ambiente (sessione bloccata + griglia non esposta).

---

## FASE 12 — Tema chiaro/scuro automatico (0.3.5, 2026-10-12)

Allineamento dell'interfaccia alle preferenze di Windows, senza dipendenze, senza thread
aggiuntivi e senza modifiche al motore di scansione/cancellazione.

- [x] `AppTheme` (enum Light/Dark) e `WindowsTheme`: lettura sola lettura del valore
      `AppsUseLightTheme` (`HKCU\...\Themes\Personalize`) via P/Invoke `RegGetValue`
      (advapi32, nessuna API di registro gestita, nessuna assembly aggiuntiva); seam
      `AppsUseLightThemeReader` per i test; errore/valore assente → tema chiaro
- [x] `ThemePalette`: palette centralizzata; chiaro = colori di sistema identici a 0.3.0
      (zero regressione), scuro = antracite fisso ad alto contrasto (deterministico in test)
- [x] `MainForm.ApplyTheme`: unico punto di assegnazione cromatica (finestra, testi, tabella,
      pulsanti, menu); dialogo Informazioni e etichetta report inclusi; nessun colore
      hardcoded fuori dalla palette (verificato da test sui sorgenti)
- [x] Aggiornamento live: `WndProc` riconosce `WM_SETTINGCHANGE` (area `ImmersiveColorSet`)
      e `WM_THEMECHANGED`, rilegge una sola volta e riapplica solo se il tema è cambiato
- [x] Limiti dichiarati in `README.md`: controlli nativi (MessageBox, ComboBox, barra del
      titolo) restano ai colori di Windows; nessun selettore manuale (tema = sistema)
- [x] 13 test `ThemeTests` (mappatura, seam, fallback, contrasto, applicazione/ripristino,
      finestra Informazioni, report, messaggi di sistema, centralizzazione); la suite passa
      da 145 a 158
- [x] Bump versione 0.3.5 (AssemblyInfo, manifest, AppInfo, ReportBuilder, test di
      versione, build.ps1, verify.ps1, qa-practical, docs) e pacchetto
      `artifacts/alpha-0.3.5/` (prima era `alpha-0.3`, conservato intatto)
- [x] Audit stringhe della build 0.3.5: `RegGetValue`/`advapi32` presenti (atteso),
      `RegistryKey`/`Microsoft.Win32`/token di rete assenti; `http://` solo nel manifest

---

## FASE 13 — Audit autonomo del prodotto (2026-10-13, post-0.3.5)

Revisione sistematica di ogni modulo sorgente dopo il rilascio 0.3.5 (commit `6e30184`).

- [x] **Sicurezza** — `PathSafety` (normalizzazione, confine radice stretto, cammino dei
      reparse point con fallback "non sicuro" su attributi illeggibili, rifiuto di radici
      drive/UNC/non fisse/inesistenti), `TempFolderScanner` (sola lettura, mai segue i
      reparse, verifica `IsInsideRoot` per ogni file), `TempFileCleaner` (riverifica ogni
      elemento subito prima di `File.Delete`: radice, containment, attributi, età,
      reparse; finestra TOCTOU ridotta all'istantaneo; esito verificato post-cancellazione;
      byte dichiarati solo se realmente rimossi), `TempPathProvider` (solo percorso
      dell'ambiente), `ScanRowActions` (consultazione pura, mai cancellazione).
      Nessuna API di rete, registro gestito o shell distruttiva in alcun modulo.
      Registro: sola lettura P/Invoke del tema (audit stringhe exe 0.3.5: `RegGetValue`/
      `advapi32` presenti come atteso; `RegistryKey`, `Microsoft.Win32`, token di rete assenti).
- [x] **Robustezza** — flussi `async` con `finally` di pulizia (CTS, stato busy),
      progressività throttled e marshalled su UI thread con guardie `IsDisposed`/
      `IsHandleCreated`, annullamento cooperativo, handler eccezioni a livello UI e
      AppDomain (`Program`), persistenza impostazioni che fallisce sui default, report
      non bloccante.
- [x] **Qualità** — conformità C# 5 / .NET 4.8 ovunque, zero dipendenze esterne,
      UTF-8 senza BOM, stili consistenti, messaggi in italiano.
- [x] **Esito: nessun difetto di prodotto.** Unica nota di codice morto:
      `ScanProgress.DirectoriesVisited` è sempre 0 e mai letto (candidato pulizia 0.3.6).
- [x] Verifica end-to-end `verify.ps1` sull'exe del pacchetto 0.3.5: **41/41**
      (finestra, analisi reale, report, Informazioni/Impostazioni, registro, servizi,
      attività, rete, chiusura) — eseguibile anche con sessione bloccata.
- [ ] Restano ambientali, in attesa di sessione sbloccata: 5 `RowContextMenuTests`
      (clipboard negata da LogonUI/LockApp: `OpenClipboard` E_ACCESSDENIED).

### Pianificazione release successive (semver)

Nessun difetto ⇒ nessun rilascio correttivo obbligato. Candidati per la prossima
incremento, in attesa di conferma sull'estensione di contratto:

- **0.3.6 (patch)**: rimuovere/capire `DirectoriesVisited` (completare il conteggio
  cartelle nell'avanzamento scansione). Nessun impatto utente.
- **0.4.0 (minor)**: rimozione prudente delle **cartelle vuote** residue sotto la radice
  dopo la pulizia (stesse regole di sicurezza: solo dentro radice, mai reparse, mai la
  radice, cancellabile). Cambia il contratto "rimuove file" in "rimuove file e cartelle
  vuote residue": richiede conferma esplicita.

---

## Criteri di completamento 0.1 Alpha

| # | Criterio | Stato | Evidenza |
|---|---|---|---|
| 1 | La GUI si avvia | [x] | `verify.ps1`: finestra creata, titolo letto |
| 2 | L'analisi funziona | [x] | `verify.ps1`: stato "Analisi completata." + riepilogo letto |
| 3 | I risultati sono visualizzati correttamente | [x] | `verify.ps1`: righe in tabella (3109) = "Proposti" del riepilogo (3109) |
| 4 | La selezione degli elementi funziona | [x]* | `verify.ps1`: pulsante pulizia **disabilitato** senza selezione e **abilitato** dopo l'analisi. `SecurityAuditTests.Ui_*`: la riga deselezionata non arriva al motore e sopravvive, e la selezione resta corretta anche dopo l'ordinamento. Il clic sulla singola checkbox **non** è automatabile: la griglia WinForms non espone le righe come elementi UIA |
| 5 | La pulizia richiede conferma esplicita | [x]* | Dialogo di conferma con numero/byte e default **No**; nella validazione pratica il gate **No** è verificato sulla GUI reale (nessuna cancellazione). La cancellazione effettiva è eseguita dal motore di produzione su vero `%TEMP%` con lista esplicita (stesso codice della GUI); il clic **Sì** non è automatizzato per non deselezionare la riga in ambiente bloccato |
| 6 | I percorsi esterni vengono rifiutati | [x] | `PathSafetyTests` + `SecurityAuditTests.PathSafety_*` (`..`, reparse, radice, `\\?\`, `\\.\`, ADS, spazi/punti finali) |
| 7 | Gli errori non provocano crash non gestiti | [x] | `ScannerTests`/`CleanerTests` (ACL negata, file bloccato, cartella inesistente) |
| 8 | La reportistica funziona | [x] | `ReportTests` + `verify.ps1` (file creato, contenuto conforme, percorso mostrato in GUI) |
| 9 | La suite di test obbligatori passa | [x] | **111/111** su 3 esecuzioni consecutive (era 97/97 prima della soglia, 84/84 prima della selezione massiva, 73/73 prima del menu contestuale) |
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
6. **Nessuna pulizia automatizzata via clic "Sì" nella GUI**: la validazione pratica esercita
   l'analisi reale, il gate di conferma "No" e la cancellazione al motore di produzione su
   vero `%TEMP%` con lista esplicita di elementi sintetici. Il clic "Sì" sulla GUI reale non
   è automatizzato: richiederebbe la deselezione della riga, non eseguibile in ambiente con
   sessione bloccata (vedi punto 7).
7. **Selezione riga per riga non automatabile in questo ambiente**: la griglia WinForms non
   espone le righe come elementi di automazione, e con la sessione Windows bloccata i click
   fisici vengono intercettati dal lock screen mentre i messaggi sintetici non attivano il
   toggle della checkbox. La semantica della selezione è coperta dai test
   `SecurityAuditTests.Ui_*` e dalla prova al motore con lista esplicita.
8. **Finestra TOCTOU residua verso `File.Delete`**: il controllo sul reparse point è l'ultimo
   prima della cancellazione ma non è atomicamente accoppiato ad essa. Un processo concorrente
   con diritti di scrittura sulla stessa cartella potrebbe sostituire una directory con una
   junction in quell'intervallo. Eliminare del tutto la finestra richiederebbe l'apertura del
   file con `FILE_FLAG_OPEN_REPARSE_POINT` via P/Invoke: non inclusa in 0.1.
9. ~~**Nessun contatore di selezione nella GUI**~~: corretto con la selezione massiva —
   l'etichetta accanto ai pulsanti mostra *N file selezionati (dimensione)* oppure
   *Nessun file selezionato*, aggiornata a ogni modifica degli spunti.
10. **Build non riproducibile bit per bit**: `csc.exe` di .NET Framework incorpora un MVID
   diverso a ogni compilazione, quindi la stessa sorgente produce un'eseguibile con hash
   SHA-256 diverso. `SHA256SUMS.txt` va quindi verificato **solo rispetto al pacchetto con
   cui è stato generato**, non rispetto a una ricompilazione locale.

## Riepilogo finale (stato reale)

| Verifica | Risultato |
|---|---|
| Compilazione Release x64 | `bin/Release/CleanSlate.exe` |
| Suite di test | **153/158** (145 di 0.3.0 + 13 nuovi `ThemeTests`; 5 `RowContextMenuTests` bloccati dall'ambiente sessione bloccata → clipboard non disponibile, non difetti di prodotto) |
| Residui del fixture di test dopo la suite | **0** |
| Verifica end-to-end GUI (`tests/verify.ps1`) | **41/41** su `bin/Release` e di nuovo **41/41** sull'exe del pacchetto |
| Finestra Informazioni e menu Impostazioni | **19 test** (`InfoSettingsTests`) + sezione `3b` di `verify.ps1`: apertura/chiusura della finestra Informazioni con lettura del contenuto, apertura del menu e chiusura con Esc |
| Validazione pratica pre-beta (`tests/qa-practical.ps1`) | **OK** su vero `%TEMP%` e sandbox GUI (id `f0382398`); 0 residui, 0 file preesistenti persi, hash pacchetto invariato; deselezione GUI non eseguita (sessione bloccata), verificata al motore |
| Menu contestuale risultati (clic destro) | **11 test** (`RowContextMenuTests`) + probe GUI reale sul pacchetto: menu su riga valida, tre voci eseguite (nome in appunti, cartella in appunti, `Esplora /select` su Temp), nessun menu su intestazione/spazi, spunti invariati |
| Selezione massiva (Seleziona/Deseleziona tutto) | **13 test** (`BulkSelectionTests`) + probe GUI reale sul pacchetto: pulsanti visibili e funzionanti su 3108 file reali, deselezione → pulizia disabilitata + "Nessun file selezionato", rieselezione → pulizia abilitata + conteggio/dimensione, righe non-file ignorate, nuova scansione senza selezioni obsolete |
| Soglia di scansione (Età minima) | **14 test** (`ScanThresholdTests`) + probe GUI reale sul pacchetto: controllo visibile, default 24 ore, caricamento da disco di 0/1 ore, riepilogo coerente, analisi con "nessuna soglia" che propone più file (3165 vs 3110), nessun residuo |
| Connessioni TCP in uscita durante un'analisi reale | **0** / almeno 40 campionamenti per esecuzione |
| Registro, servizi, attività, avvio automatico | Invariati |
| Processo residente dopo la chiusura | Nessuno |
| Pacchetto portable | `artifacts/alpha-0.3.5/` (exe, README.txt, LICENSE.txt, SHA256SUMS.txt) |
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
| Visibilità della selezione | Test GUI + lettura e2e | Colonna "Selez.", pulsante abilitato/sdisabilitato, conferma con conteggio, etichetta "N file selezionati (dimensione)" | `verify.ps1`, `SecurityAuditTests.Ui_*`, `BulkSelectionTests`, probe GUI | — |
| File recenti | Test | Saltati in scansione e in cancellazione | `Scan_FileNewerThanLimit_IsExcluded`, `Clean_FileNewerThanLimit_IsSkipped` | — |
| File bloccati / ACL negate | Test | Saltati o conteggiati come errore, operazione prosegue | `Clean_LockedFile_…`, `Clean_ReadOnlyFile_…`, `Scan_AccessDeniedDirectory_…` | — |
| Cancellazione parziale / annullamento | Test | Cooperativo, nessun tocco sui residui | `Clean_CancellationIsCooperative…`, `Scan_CancellationDuringScan…` | — |
| Build pubblicata: dipendenze | Riflessione sull'exe pubblicato | Solo assembly del framework .NET | elenco assembly referenziati | — |
| Build pubblicata: rete/registro | Scansione nomi di metadati e stringhe | Nessun nome sospeto; `http://` solo namespace XML del manifest | audit build | Non è una cattura pacchetti |
| Eseguibile reale | `tests\verify.ps1` | 31/31 su `bin/Release` e 31/31 sull'exe del pacchetto | log della verifica | — |
| Validazione pratica pre-beta | `tests\qa-practical.ps1` su vero `%TEMP%` + sandbox GUI | **OK**: analisi, età, junction, blocco, fuori radice, residui 0, build conservata | `tests/qa-run.log` (id `f0382398`) | Deselezione GUI non eseguita (sessione bloccata); cancellazione verificata al motore di produzione |

## Prossimo passo concreto (se la sessione dovesse interrompersi)

```powershell
cd C:\Users\User\Documents\Experiments\cleanslate
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1   # 145/145 attesi
powershell -ExecutionPolicy Bypass -File tests\verify.ps1      # 41/41 attesi
powershell -ExecutionPolicy Bypass -File build.ps1 -Package    # pacchetto portable
```
