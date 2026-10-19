# Nuvacline Release Manager - Report dei test

Data: 2026-10-16. Ambiente: Windows, Windows PowerShell 5.1, git di sistema,
`gh` 2.102.0 (usata SOLO manualmente nel workflow 0.5.1; la suite usa uno stub).

## Metodo

- La suite `tools\tests\release-manager.Tests.ps1` crea fixture complete in
  `%TEMP%\opencode\rm-tests\<test>\`: repository git reale con remote bare
  (`remote.git`), commit baseline e tag `v1.0.0` pushati, file di progetto
  coerenti con il repo vero (10 file gestiti, 7 file di pacchetto, artefatti
  protetti, build con `csc` in-box).
- La pubblicazione e' simulata con uno stub `gh` (`.cmd` + `.ps1`) iniettato
  via variabile `NUVACLINE_GH_BIN`: nessuna rete, nessuna credenziale, ma
  stesso protocollo di chiamate del `gh` reale.
- Ogni test esegue il vero `tools\release.ps1` in un processo PowerShell
  separato e verifica stato JSON, journal, refs locali/remote, commit, ZIP,
  hash e messaggi.

Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\tests\release-manager.Tests.ps1`

## Esito: 16/16 superati, exit code 0

```
TEST OK   T01 parametri non validi: exit 1
TEST OK   T02 dry-run nuova release: nessuna scrittura
TEST OK   T03 flusso completo al gate, nessun commit/push estraneo
TEST OK   T04 resume senza approvazione: nulla pubblicato
TEST OK   T05 candidata modificata dopo il gate: blocco
TEST OK   T06 test falliti: blocco e resume da checkpoint
TEST OK   T07 stato corrotto: ripristino dal backup
TEST OK   T08 pubblicazione simulata dopo approvazione + ripetizione
TEST OK   T09 modalita senza pubblicazione, poi pubblicazione
TEST OK   T10 tag conflittuale: blocco senza sovrascritture
TEST OK   T11 resume dry-run: sola lettura
TEST OK   T12 macchina a stati: transizioni validate
TEST OK   T13 preflight con file staged: blocco e indice intatto
TEST OK   T14 file dirty non gestiti: preservati e non committati
TEST OK   T15 artefatto protetto modificato: blocco preflight
TEST OK   T16 includeFiles: file non gestito incluso nel commit di release

RISULTATO: 16 superati, 0 falliti
SUITE_EXIT=0
```

Copertura dei requisiti richiesti:

| Scenario richiesto | Test |
|--------------------|------|
| Transizioni di stato valide/invalide | T12 |
| Dry-run senza modifiche | T02 |
| Preflight con file in staging | T13 |
| Protezione file dirty | T14 |
| Test automatici falliti | T06 |
| Resume da checkpoint | T03/T04/T06/T07 |
| Ripresa da JSON corrotto (backup verificato) | T07 |
| Candidata modificata dopo il gate | T05 |
| Approvazione umana mancante | T04 |
| Tag esistente/conflittuale | T10 |
| Pubblicazione simulata con tool stub | T08 |
| Ripetizione operazione completata | T08 (idempotente) |
| Modalita' senza pubblicazione remota | T09 |
| Nessun commit/push estraneo | T03/T14 |
| file non gestiti inclusi esplicitamente nel commit (`-IncludeFiles`) | T16 |

## Difetti reali trovati (e corretti) durante lo sviluppo della suite

1. **`gh release view` senza `--json`** (`release-publish.ps1`): l'output non
   sarebbe stato JSON; aggiunto `--json tagName,isPrerelease,url,assets`.
2. **Journal scritto anche in dry-run** (`release-resume.ps1`): l'append al
   journal ora avviene solo dopo il controllo `-DryRun`.
3. **Confronto percorsi git su Windows** (`release-phases.ps1`,
   `release-prep.ps1`): normalizzazione separatori `\`/`/` nei confronti di
   spec, diff, file in staging e ricostruzione audit.
4. **Collisione di sottostringa tra versioni** (`release-prep.ps1`): il check
   strict dei residui segnalava falso positivo quando la nuova versione
   contiene la vecchia come sottostringa (es. `1.1.0.0` contiene `1.0.0`).
   Corretto mascherando i token della nuova versione prima della ricerca.
   Senza la fixture 1.0.0 -> 1.1.0 il bug sarebbe rimasto latente fino alla
   prima release 1.x.
5. **Stub CHANGELOG con CRLF in file LF** (`release-prep.ps1`): inserire
   `[Environment]::NewLine` in un file LF faceva fallire `git diff --check`
   in AUDITING. Ora lo stub usa il fine riga dominante del file esistente.
6. **Proprieta' `versionUpdate.residuals` mancante** (`release-phases.ps1`):
   impostata solo in AUDITING su un oggetto che non la prevedeva; aggiunta
   alla creazione in BUILDING (entrambi i percorsi, nuova release e resume).
7. **Helper di test ricorsivo**: una funzione `Git` che invocava `& git`
   veniva risolta su se stessa da PowerShell (le funzioni hanno precedenza
   sugli eseguibili); rinominata `Git-Cli`.
8. **Lettura blob git troncata** (`release-phases.ps1`, `Get-GitFileText`):
   la cattura dell'output di `git show` via pipeline PowerShell perdeva il
   newline finale dei file della baseline; la ricostruzione dell'audit
   differiva quindi da file identici e la release reale 0.5.2 e' stata
   bloccata in AUDITING su tutti i 9 file gestiti (blocco non ripristinabile
   per design). Le fixture non lo rilevavano perche' i loro file non
   terminavano con newline. Corretto leggendo il blob byte-esatto con
   `ProcessStartInfo`; le fixture ora scrivono sempre file con newline finale
   (copertura di regressione; suite estesa con T16 e ri-eseguita 16/16).
9. **Prima scrittura clipboard intermittente nel prodotto** (fallimento
   ambientale + irrobustimento): con sessione Windows bloccata i test che
   usano gli appunti falliscono (limite accettato, non aggirato). Inoltre la
   PRIMA `Clipboard.SetText` di un processo senza coda messaggi poteva
   fallire; `MainForm.cs` ora ritenta con `Application.DoEvents()` e il fix
   e' incluso nel commit di release via `-IncludeFiles`.

## Verifiche sul repository reale (read-only)

### Parsing e stile
- Parse PS 5.1 di tutti i `.ps1` in `tools\` (10 file): 0 errori.
- Nessun file non-ASCII in `tools\` (i `.ps1` senza BOM vengono letti come
  ANSI da PowerShell 5.1).

### Dry-run reale

`powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\release.ps1 -Version 0.5.2 -DryRun`
- Exit code 0.
- Output: HEAD `19c678b9`, branch `master`, staged 0 / non in staging 3 /
  non tracciati 1; versione `0.5.1 -> 0.5.2`; ultima rilasciata `0.5.1`;
  artefatti protetti **10 OK, 0 diversi, 0 assenti**; cartella prevista
  `Nuvacline-Release-Workflow\20261016-232830-0.5.2`; commit previsto
  `Release preparation: Nuvacline 0.5.2`; fasi fino al gate umano.
- **Nessuna scrittura**: `git status --short` identico prima/dopo e
  `Nuvacline-Release-Workflow\` contiene solo `20261016-153856` (0.5.1).

### Integrita' artefatti 0.5.1 (invariati)
- 8/8 hash protetti nel repo = manifest finale 0.5.1
  (`evidence\phase-g\protected-hashes-final.txt`).
- 2/2 cingelati esterni 0.5.0: ZIP `CBE66BF5...`, EXE `6F436311...`.
- ZIP 0.5.1 locale e copia scaricata da GitHub: entrambe
  `59562D1715A5F9B55186E37B64E9BEB55CD2AD0E0EB937D4E9EEB8131C12ED54`.
- Remote: tag `v0.5.1` -> oggetto `ccca0c16...`, peel `19c678b9...`;
  `master` remoto `19c678b9...` (invariati).
- Nessun tag `v0.5.2` locale o remoto.

### Stato Git finale
- HEAD `19c678b9e727fd4b9d18a0b47f70d70c08604fbe` (nessun commit nuovo).
- `git diff --check` exit 0.
- Dirty solo i 3 binari preesistenti + `tools\` (e `docs\`) non tracciati:
  nessun commit e nessun push eseguiti in questa attivita'.

## Sessione notturna 2026-10-17: release reale 0.5.2 fino al gate

Dopo la pubblicazione 0.5.1, la release reale 0.5.2 e' stata eseguita col
Release Manager e si e' fermata al gate umano come previsto:

- run1 `20261016-233346-0.5.2`: bloccata in TESTING (1 test clipboard
  fallito per sessione bloccata; log preservato in
  `evidence\tests-run1-1fail.log`).
- run2 `20261017-000037-0.5.2`: bloccata in AUDITING dal difetto 8 (non
  ripristinabile per design); nessuna modifica al repository causata dal
  blocco.
- run3 `20261017-000921-0.5.2` (con `-IncludeFiles
  'src\Nuvacline\UI\MainForm.cs'`): **exit 0**, TESTING 231/231 e audit
  superato con ricostruzione fedele.
  - Commit locale: `e5667cb01df51fd029b67f58cf6f848353ec859f`
    ("Release preparation: Nuvacline 0.5.2"), 11 file = 10 gestiti +
    `MainForm.cs`.
  - ZIP: `F1864A1F3F7056AEE3C5100ACCA5926BD5EBCDC59FF9B8DB56D8E9E552A178E0`;
    EXE: `270F8CA1E5E28AE46930F45C2E130358E6159F37EED9EFA95A58242027990D7B`
    (hash ricalcolati in verifica indipendente, coincidono).
  - `verify.ps1` end-to-end sul pacchetto: OK. Checklist:
    `HUMAN-TEST.md`.
- Verifica indipendente: il fix clipboard (riga 141, `DoEvents`) e' presente
  nel commit; `git status` finale invariato (solo i 3 binari preesistenti +
  `tools\`/`docs\` non tracciati).
- **Pubblicazione NON eseguita**: richiede il test umano e
  `.\tools\release.ps1 -Resume -ApproveHumanTest`.

### Pubblicazione 0.5.2 (autorizzata dal proprietario)

- Repository rinominato: `DJ-Erik/CleanSlate` -> **`DJ-Erik/Nuvacline`** (privato,
  branch `master`); remote `origin` aggiornato al nuovo URL.
- Controlli ripetuti prima della pubblicazione: suite release manager
  **16/16**, suite prodotto **231/231**, hash della candidata invariati
  (ZIP `F1864A1F...`, EXE `270F8CA1...`).
- Approvazione registrata dal flusso ufficiale con
  `-Resume -ApproveHumanTest -GitHubRepo DJ-Erik/Nuvacline`: exit 0,
  stato `DISTRIBUTED`.
- Tag annotato `v0.5.2` (`8c536a9c...`) -> commit `e5667cb0...`;
  `master` remoto `e5667cb0...` (fast-forward, nessun force push).
- Release GitHub (pre-release):
  `https://github.com/DJ-Erik/Nuvacline/releases/tag/v0.5.2` con asset
  `Nuvacline-0.5.2-Windows.zip` (99.150 byte, `sha256:f1864a1f...`) e
  `SHA256SUMS-0.5.2.txt`; asset scaricato e ricalcolato: hash coincidente.
- Nota: lo stato del workflow registra ancora `remoteUrl` storico
  (CleanSlate); le operazioni git/gh successive usano il nuovo nome
  (GitHub reindirizza i vecchi URL).
- Landing page: preparata in `Nuvacline-Public-Site\site\` (screenshot
  reale della build, logo, ZIP hash-verificato, licenza GPL-3.0). Non
  pubblicata: GitHub Pages su repo privato richiede piano a pagamento
  (API 422); visibilita' del repository invariata. Istruzioni in
  `Nuvacline-Public-Site\PUBLISHING.md`.
- Nota post-pubblicazione (richiesta del proprietario): rimosso il flag
  pre-release da v0.5.2 con `gh release edit --latest`; la release appare
  ora come "Latest" nella sidebar del repository (endpoint
  `releases/latest`). Le release precedenti restano pre-release. Tag,
  artefatti e hash invariati.
- Aggiornamento successivo (sempre su richiesta del proprietario):
  disclaimer legale inserito in README.txt del pacchetto e nelle note di
  rilascio; asset della release sostituiti (ZIP `C4EB6706...`, 99.285
  byte; eseguibile invariato `270F8CA1...`); copia dell'originale
  approvato in `evidence\superseded\` del workflow. Generatori aggiornati
  per le release future (`build.ps1`, `release-package.ps1`) e README.md
  del repository arricchito con la sezione Disclaimer; commit `dae84d1`
  su master (push fast-forward, HEAD=origin).
- Landing page pubblicata (2026-10-18): repo pubblico solo-sito
  `DJ-Erik/nuvacline-site` (commit `2928622`, Opzione 2 di PUBLISHING.md),
  GitHub Pages su `https://dj-erik.github.io/nuvacline-site/` verificato
  (HTTP 200, disclaimer visibile, ZIP scaricabile con hash `C4EB6706...`
  coincidente con l'asset GitHub); sorgenti sempre privati.
- Variante scura del logo pubblicata anche sul sito (2026-10-18): commit
  `0beea08c352267d9524fc9a30029303d4f3e728d` ('Add dark-mode logo variant')
  su `main` di `DJ-Erik/nuvacline-site` (push fast-forward
  `2928622..0beea08`); nuova `assets/logo-dark.png` (12064 byte, sha256
  `673E924B7B6A7A569F6C6A497143D15E72E219EA0966DAB21C26CDECCD1D2EE6`) e
  `<picture>` con `prefers-color-scheme: dark` in `index.html`; Pages
  ridespiegato e verificato (asset HTTP 200, hash coincidente; riferimenti
  `logo-dark` nella pagina live = 2).

## Limiti

- La suite non usa la rete: la pubblicazione reale (gh autenticata) e'
  coperta dallo stub e dal collaudo 0.5.1 gia' completato.
- Il dry-run reale copre preflight/piano ma non esegue le fasi; l'esecuzione
  completa reale e' verificata solo dalle fixture.
- La fixture usa 1.0.0 -> 1.1.0: combinazione preziosa per il bug 4, ma le
  release reali future potrebbero avere combinazioni numeriche diverse.
