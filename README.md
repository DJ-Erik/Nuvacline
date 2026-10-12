# CleanSlate 0.3.5

<img src="assets/logo/cleanslate_horizontal_light.png" width="420" alt="CleanSlate">

CleanSlate è un pulitore di file temporanei per Windows, **portatile, offline, in italiano**.

Analizza la cartella temporanea dell'utente corrente, mostra gli elementi recuperabili in una
lista dove si sceglie cosa eliminare, chiede una **conferma esplicita** prima di cancellare e
produce un **report locale** il cui percorso è mostrato nella finestra. Comandi **Seleziona
tutto** e **Deseleziona tutto** permettono di spuntare o azzerare l'intera lista con un solo
clic, mentre un **menu contestuale** (clic destro su una riga) permette di copiare il nome o
la cartella del file e di aprirlo in Esplora file selezionandolo, senza mai eseguirlo.
Il pulsante **?** apre la finestra **Informazioni** (scopo, privacy, avvertenze) e il pulsante
**⚙** apre le **Impostazioni**, dove si attiva o disattiva la conservazione dei report e si
apre la cartella che li contiene. L'interfaccia segue **automaticamente il tema chiaro/scuro
di Windows**, aggiornandosi anche al cambio di tema mentre il programma è aperto.

CleanSlate **non promette di accelerare il computer**. Rimuove soltanto file temporanei
dell'utente corrente che sono più vecchi di 24 ore (soglia configurabile da interfaccia:
1 ora, 6 ore, 12 ore, 24 ore, 48 ore, 7 giorni oppure nessuna soglia; la scelta è
persistita localmente e si applica sia all'analisi sia alla pulizia).

---

## Requisiti

| Requisito | Dettaglio |
|---|---|
| Sistema | Windows 10 (build 1903+) o Windows 11, 64 bit |
| Runtime | .NET Framework 4.8 o successivo (già presente in Windows 10/11) |
| Privilegi | Nessuno: il manifest dichiara `asInvoker` |
| Rete | Nessuna connessione necessaria o utilizzata |
| Installazione | Nessuna: si avvia `CleanSlate.exe` e basta |

## Uso

1. Avviare `CleanSlate.exe`.
2. Premere **Analizza**: l'analisi è in sola lettura e non modifica nulla.
3. Esaminare l'elenco, selezionare o deselezionare gli elementi desiderati.
4. Premere **Pulisci selezionati** e confermare nella finestra di conferma.
5. **Apri report** apre il report locale generato al termine dell'analisi o della pulizia.
6. **?** mostra le informazioni sul prodotto; **⚙** consente di attivare o disattivare la
   conservazione dei report e di aprire la cartella che li contiene.

**Annullare** è sempre disponibile: l'analisi e la pulizia sono cooperativamente annullabili.

## Cosa non fa

- Non si collega a Internet, non raccoglie e non trasmette dati.
- Non modifica il registro di Windows: l'unica lettura è l'impostazione di tema
  (chiaro/scuro) usata per colorare l'interfaccia.
- Non installa servizi, attività pianificate, voci di avvio automatico o processi residenti.
- Non contiene pubblicità, telemetria, account, login o aggiornamenti automatici.
- Non elimina file senza una conferma esplicita.
- Non può recuperare i file che sono stati eliminati.

## Limiti noti (versione alpha)

1. **Unica categoria**: "File temporanei dell'utente" (cartella temporanea indicata
   dall'ambiente Windows). Nessun browser cache, nessun registro, nessun servizio.
2. **File recenti**: gli elementi modificati nelle ultime 24 ore sono scartati per impostazione
   predefinita. La soglia è configurabile dall'interfaccia (1 ora, 6 ore, 12 ore, 24 ore,
   48 ore, 7 giorni, nessuna soglia) e viene salvata in
   `%LOCALAPPDATA%\CleanSlate\settings.txt`, dove viene conservata anche la preferenza
   di salvataggio dei report (`KeepReports`).
3. **File bloccati**: vengono saltati e conteggiati come errori, mai forzati.
4. **Percorsi lunghi**: il manifest dichiara `longPathAware`, ma il runtime .NET Framework può
   comunque rifiutare alcuni percorsi oltre 260 caratteri: in tal caso l'elemento viene saltato
   e contato, mai eliminato a caso.
5. **Ripristino ACL nei test**: il ripristino dei permessi di test usa `icacls /reset` perché
   .NET Framework non persiste la modifica di un oggetto di sicurezza non modificato in memoria.
   È un dettaglio del fixture di test, non dell'applicazione.
6. **Nessun .NET SDK in ambiente**: la compilazione usa `csc.exe` in-box di .NET Framework
   (C# 5), non il SDK .NET. Conseguenza: niente xUnit (c'è un runner di test equivalente).
7. **La verifica end-to-end esegue solo l'analisi**, mai una pulizia reale sul `%TEMP%`:
   automatizzare la cancellazione eliminerebbe file dell'utente durante un test. Il motore di
   cancellazione e il suo percorso di conferma sono esercitati dai test su fixture dedicati.
8. **La selezione della singola riga non è automatabile**: la griglia WinForms non espone le
   righe come elementi di automazione.
9. **Controlli nativi non tematizzati**: le finestre di sistema (MessageBox), il ComboBox
   della soglia e la barra del titolo usano i colori nativi di Windows anche in tema scuro:
   WinForms su .NET Framework non espone un controllo affidabile per ridisegnarli, quindi
   restano al rendering del sistema. La palette scura copre finestra, testi, tabella,
   pulsanti, menu, dialogo Informazioni e etichette.

## Struttura del repository

```
cleanslate/
├── build.ps1              build + test + pacchetto portable
├── build.common.ps1       funzioni di compilazione condivise
├── assets/
│   └── logo/              logo ufficiale (CleanSlate.ico/png, varianti orizzontale e monocromatica)
├── tests/
│   ├── run-tests.ps1      compila ed esegue la suite di test
│   └── verify.ps1         verifica end-to-end della GUI reale
├── src/
│   ├── CleanSlate/        applicazione (Core, UI, Program, manifest)
│   └── CleanSlate.Tests/  runner di test + 158 test
├── artifacts/alpha-0.3.5/ pacchetto distribuibile
├── bin/                   output di build (non versionato)
└── TASKS.md               checklist con stato reale e limiti
```

## Compilazione e test

Tutti i comandi si eseguono dalla cartella `cleanslate` con PowerShell 5.1 e **non scaricano
nulla**.

```powershell
# compilazione Release + suite di test
powershell -ExecutionPolicy Bypass -File build.ps1

# compilazione + test + pacchetto portable in artifacts\alpha-0.3.5
powershell -ExecutionPolicy Bypass -File build.ps1 -Package

# solo la suite di test
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1

# verifica end-to-end della GUI reale (avvio, analisi, report, chiusura,
# registro, servizi, attività, connessioni TCP)
powershell -ExecutionPolicy Bypass -File tests\verify.ps1
```

### Stato verificato

| Verifica | Risultato |
|---|---|
| Suite di test | **153/158 superati** (145 di 0.3.0 invariati + 13 nuovi `ThemeTests`); i 5 residui sono `RowContextMenuTests` bloccati dall'ambiente (sessione bloccata → clipboard non disponibile), non difetti di prodotto |
| Verifica end-to-end GUI (`verify.ps1`) | **41/41 controlli superati** sull'exe del pacchetto |
| Probe GUI selezione massiva | Pulsanti visibili e funzionanti su 3108 file reali: deselezione → pulizia disabilitata, rieselezione → pulizia abilitata, etichetta conteggio/dimensione aggiornata |
| Probe GUI soglia di scansione | Controllo visibile all'avvio con default 24 ore; caricamento da disco di `MinAgeHours=0` (nessuna soglia, 3165 proposti vs 3110 con 24 ore) e `MinAgeHours=1`; riepilogo sempre coerente con la soglia applicata; nessun residuo di processo o di impostazioni |
| Rete (campionamento runtime) | **0 connessioni TCP in uscita** su almeno 40 campionamenti durante l'analisi reale |
| Registro di sistema | Invariato prima/dopo l'esecuzione |
| Servizi e attività pianificate | Invariati prima/dopo l'esecuzione |
| Avvio automatico | Nessuna voce creata |
| Processo residente | Nessuno dopo la chiusura della finestra |
| Pacchetto | `artifacts\alpha-0.3.5\` con `CleanSlate.exe`, `README.txt`, `LICENSE.txt`, `SHA256SUMS.txt` |
| Dipendenze della build pubblicata | Solo assembly del framework .NET (`mscorlib`, `System`, `System.Core`, `System.Drawing`, `System.Windows.Forms`) |
| Audit di sicurezza pre-distribuzione | Completato il 2026-10-09: 1 problema TOCTOU corretto, vedi `SECURITY.md` |

## Copertura dei test

| Area | Test |
|---|---|
| Sicurezza dei percorsi (containment, `..`, reparse point, radice, `\\?\`, ADS) | `PathSafetyTests`, `SecurityAuditTests` |
| Scanner in sola lettura, limiti d'età, dimensioni, annullamento, ACL, junction | `ScannerTests` |
| Cancellazione prudente, file bloccati, percorsi esterni, report | `CleanerTests` |
| Race condition: file toccato/sostituito dopo la scansione, junction sostituita | `SecurityAuditTests` |
| Selezione: solo gli elementi selezionati arrivano alla cancellazione, anche dopo l'ordinamento | `SecurityAuditTests` |
| Report TXT/JSON, sanitizzazione percorsi, assenza di scritture | `ReportTests` |
| Privacy statica (nessuna API di rete/telemetria/shell distruttiva; registro accessibile solo in lettura per il tema) | `PrivacyTests` |
| Menu contestuale: nome/cartella copiati, visibilità solo su righe valide, spunti invariati, Esplora `/select` senza eseguire il file | `RowContextMenuTests` |
| Selezione massiva: Seleziona/Deseleziona tutto, conteggio, dimensione, nuova scansione, righe non-file, sync con motore e menu contestuale | `BulkSelectionTests` |
| Soglia di scansione: default 24 ore, mapping opzioni, persistenza/ricaricamento, fallback, esclusione/inclusione file, wiring GUI→scanner, nessun auto-scan, non retroattività, conferma | `ScanThresholdTests` |
| Riepilogo di scansione e conteggi (scartati/esclusi dalla soglia, selezionati, report coerente) | `ScanSummaryTests` |
| Informazioni e impostazioni: finestra modale, versione, testo privacy, menu ⚙, spunta conservazione report, persistenza KeepReports, default/valori non validi, report su richiesta, errori di salvataggio, nessuna cancellazione retroattiva | `InfoSettingsTests` |
| Tema chiaro/scuro: rilevamento con seam, palette centralizzata (chiaro = colori di sistema, scuro = antracite fisso ad alto contrasto), applicazione/ripristino su `MainForm`, aggiornamento live `WM_THEMECHANGED`/`WM_SETTINGCHANGE`/`ImmersiveColorSet`, fallback chiaro su eccezione, nessun colore hardcoded fuori dalla palette, dialogo Informazioni, etichetta report | `ThemeTests` |

I test usano un fixture autoconsistente in `%LOCALAPPDATA%\CleanSlateTests\<guid>\` (mai il
reale temporaneo dell'utente) e lo rimuovono integralmente, ACL comprese.

## Sicurezza

Vedi [SECURITY.md](SECURITY.md). In sintesi: nessun privilegio, nessuna shell per le
cancellazioni, conferma sempre richiesta, confinement dell'operazione dentro la radice
autorizzata e ricalcolo della sicurezza per ogni singolo file prima della cancellazione.

## Privacy

Vedi [PRIVACY.md](PRIVACY.md).

## Storia delle versioni

Vedi [CHANGELOG.md](CHANGELOG.md).
