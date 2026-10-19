# Nuvacline Release Manager

Strumento di release end-to-end per Nuvacline: bump versione, build isolata,
suite di test, audit fedelta', packaging, gate di test umano e pubblicazione
GitHub, con checkpoint ripristinabili e vincoli di sicurezza espliciti.

## Requisiti

- Windows PowerShell 5.1 (nessun modulo esterno).
- `git` nel PATH.
- `gh` CLI autenticata (serve solo per la pubblicazione GitHub; il resto
  funziona senza).
- Nessuna dipendenza Python/Node/NuGet; la build usa `csc` in-box.

## Avvio rapido

```powershell
.\tools\release.ps1 -Version 0.5.2              # nuova release, si ferma al gate umano
.\tools\release.ps1 -Version 0.5.2 -DryRun      # solo piano, nessuna scrittura
.\tools\release.ps1 -Resume                     # riprende un flusso interrotto/bloccato
.\tools\release.ps1 -Resume -ApproveHumanTest   # approva il gate e pubblica
.\tools\release.ps1 -Resume -NoPublish          # chiude senza pubblicare (SKIPPED_LOCAL)
```

## Parametri (tools\release.ps1)

| Parametro            | Effetto |
|----------------------|---------|
| `-Version x.y.z`     | Versione target per una nuova release (obbligatoria senza `-Resume`). |
| `-Resume`            | Riprende il flusso: dalla fase successiva, o dalla fase bloccata se il blocco e' ripristinabile. |
| `-ApproveHumanTest`  | Conferma esplicita del test umano al gate; senza questo switch il resume NON pubblica. |
| `-DryRun`            | Stampa il piano (e per `-Resume` lo stato) e termina senza scrivere nulla, nemmeno nel journal. |
| `-NoPublish`         | Chiude il flusso senza pubblicazione remota (`publish.status = SKIPPED_LOCAL`). |
| `-RepoRoot`          | Root del repository (default: rilevata dallo script). |
| `-WorkflowRoot`      | Radice dei flussi (default: `..\Nuvacline-Release-Workflow` accanto al repo). |
| `-StateFile`         | Percorso esplicito di `release-state.json` per il resume. |
| `-IncludeFiles`      | Percorsi extra ammessi nel commit di release con modifiche manuali autorizzate (utile per file strict). |
| `-NotesFile`         | File markdown con note di release personalizzate, al posto di quelle generate. |
| `-GitHubRepo`        | `owner/repo` per `gh` (default: dal remote `origin`). |

Exit code: `0` successo o stop al gate; `1` errore di uso/parametri;
`2` workflow BLOCKED o errore di fase.

## Fasi

1. **PREFLIGHT** - verifica repo pulito (staged vuoti), `git diff --check`,
   artefatti protetti invariati, versione target valida e non gia' rilasciata,
   nessun conflitto di flusso attivo per la stessa versione.
2. **BUILDING** - bump meccanico dei 10 file gestiti + sezione CHANGELOG
   (preautorata se presente, altrimenti stub da rivedere), verifica strict
   "nessun residuo della versione precedente", build isolata in copia di
   lavoro (mai nel repo).
3. **TESTING** - suite `tests\run-tests.ps1` nella copia isolata; risultati
   registrati nello stato.
4. **AUDITING** - ricostruisce ogni file gestito dal bump e lo confronta con
   il contenuto reale (nessuna modifica a sorpresa), controlla i residui
   storici della vecchia versione (avvisi), poi crea il commit locale
   `Release preparation: Nuvacline <version>`. Niente push in questa fase.
5. **PACKAGING** - build ufficiale, pacchetto ZIP con i 7 file previsti
   (`LICENSE`, `Nuvacline.exe`, `PRIVACY.md`, `README.txt`, `SECURITY.md`,
   `SHA256SUMS.txt`, `THIRD-PARTY.txt`), verifica eseguibile pacchettizzato,
   ancoraggio hash, `HUMAN-TEST.md`.
6. **Gate umano** - stato `AWAITING_HUMAN_APPROVAL`: l'utente prova il
   pacchetto e approva con `-Resume -ApproveHumanTest`.
7. **PUBLISHING** - ricontrolla l'integrita' della candidata (blocca se
   modificata dopo il gate), tag annotato `v<version>`, push master e tag
   (mai force), `gh release create` prerelease con ZIP, verifica
   post-pubblicazione (tag/asset difformi bloccano).
8. **DISTRIBUTED** - flusso chiuso. Un ulteriore `-Resume -ApproveHumanTest`
   e' idempotente (nessuna azione).

## BLOCKED e ripresa

- Un blocco e' **ripristinabile** quando la causa e' ambientale/transitoria
  (test falliti, gh non disponibile, rete, push fallito): si corregge e si
  rilancia `-Resume`. Lo stato registra fase, motivo e flag `recoverable`.
- Un blocco **non ripristinabile** richiede intervento manuale (candidata
  modificata dopo il gate, tag in conflitto, master remoto divergente,
  release remota difforme). Nessuna sovrascrittura automatica.
- Lo stato e' salvato atomicamente con copia `release-state.prev.json`; se il
  file principale e' corrotto, il resume lo ripristina dal backup e lo segnala.

## Sicurezza

- 10 artefatti protetti (binari storici, LICENSE, logo, ZIP 0.4.5 + ZIP/EXE
  congelati 0.5.0 fuori repo) con hash noti: qualsiasi modifica blocca il
  preflight. I 3 binari sporchi preesistenti nel repo sono attesi e coerenti
  con gli hash noti.
- File sporchi NON gestiti: preservati e mai inclusi nel commit di release.
- Con file in staging il preflight blocca senza toccare l'indice.
- `-Version` gia' rilasciata (tag locale o remoto) viene rifiutata.
- Nessun `reset --hard`, `clean`, `restore`, `checkout --` o force push:
  il manager non riscrive mai storia ne' sovrascrive tag/release esistenti.
- La pubblicazione avviene SOLO con `-Resume -ApproveHumanTest` esplicito.

## Struttura del flusso

`<WorkflowRoot>\<yyyyMMdd-HHmmss>-<version>\` contiene:

- `release-state.json` (+ `release-state.prev.json`), `release-journal.md`,
  `RESUME.md`, `WORKFLOW-DIR.txt`;
- `candidate\` copia isolata di build/test e `qa-logs\`;
- `evidence\` baseline/preflight/building/auditing/packaging/publishing;
- `package\` ZIP e cartella estratta; `reports\final-report.md`.

## Moduli

| File | Contenuto |
|------|-----------|
| `tools\release.ps1` | Entrypoint: parametri, dot-source, exit code. |
| `tools\release-lib.ps1` | Stato, transizioni, hash protetti, log, git wrapper. |
| `tools\release-prep.ps1` | Preflight, regole bump, validazioni versione. |
| `tools\release-phases.ps1` | Fasi BUILDING/TESTING/AUDITING. |
| `tools\release-package.ps1` | PACKAGING, note di release, HUMAN-TEST. |
| `tools\release-publish.ps1` | Integrita' candidata, tag/push/release GitHub. |
| `tools\release-workflow.ps1` | Catena fasi, usage, dry-run, stato iniziale. |
| `tools\release-resume.ps1` | Preflight di ripresa, resume e approvazione. |
| `tools\tests\release-manager.Tests.ps1` | Suite end-to-end su fixture isolate. |

## Pubblicazione della 0.5.2 (quando richiesto)

```powershell
.\tools\release.ps1 -Version 0.5.2                 # fino al gate umano
# test manuale del pacchetto in package\
.\tools\release.ps1 -Resume -ApproveHumanTest      # tag + release GitHub
```

Prima della pubblicazione reale, se non ci sono state modifiche pendenti,
si puo' sempre fare un ultimo controllo con
`.\tools\release.ps1 -Version 0.5.2 -DryRun`.
