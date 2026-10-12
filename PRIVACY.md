# Privacy di CleanSlate

Documento relativo alla versione **0.3.6**.

## In una frase

CleanSlate non raccoglie, non calcola, non conserva e non trasmette alcun dato personale:
funziona interamente sulla macchina e senza rete.

## Dati trattati

| Dato | Trattamento |
|---|---|
| Elenco dei file temporanei | Calcolato in memoria, mostrato nella finestra, non salvato altrimenti |
| Dimensioni e date dei file | Idem |
| Percorsi | Mostrati nella finestra; nel report la radice temporanea è sostituita dal segnaposto `%TEMP%` |
| Nome utente / profilo utente | **Mai** scritto nel report |
| Impostazione di tema Windows (chiaro/scuro) | Letta in sola lettura per colorare l'interfaccia; non salvata, non trasmessa |
| Report locale | Scritto solo in `%LOCALAPPDATA%\CleanSlate\reports\` sul proprio PC |
| Dati di telemetria | **Nessuno** |

Il report contiene solo controlli numerici (file analizzati, proposti, scartati, errori, byte) e
note di testo. Un test automatico verifica che il report non contenga il profilo utente né il
percorso reale della cartella temporanea.

## Cosa non viene fatto

- Nessuna connessione di rete: nessun controllo aggiornamenti, nessuna telemetria, nessuna
  pubblicità, nessun tracciamento, nessun download.
- Nessuna scrittura nel registro di Windows. L'unica **lettura** è l'impostazione di
  tema dell'utente (`HKCU\...\Themes\Personalize`, valore `AppsUseLightTheme`), usata
  esclusivamente per scegliere la palette chiara o scura dell'interfaccia: nessuna chiave
  viene creata, aperta in scrittura o eliminata.
- Nessun servizio, nessuna attività pianificata, nessuna voce di avvio automatico.
- Nessun processo rimasto in esecuzione dopo la chiusura della finestra.
- Nessun cookie, nessun identificativo, nessun account, nessun file di configurazione nella
  cartella dell'utente oltre al report esplicito.
- Nessuna cartella condivisa o sincronizzata viene usata.

## Verifiche eseguite

### Statiche (automatiche, `PrivacyTests`)

- Le sorgenti non contengono chiamate a API di rete (`WebRequest`, `HttpClient`,
  `Socket`, `Dns`, `WebClient`).
- Le sorgenti non contengono API di registro **gestite** (`Microsoft.Win32.Registry`,
  `RegistryKey`): l'unico accesso al registro è una chiamata P/Invoke sola lettura
  (`RegGetValue` su `advapi32.dll`) del valore di tema, con seam sostituibile dai test.
- Le sorgenti non contengono telemetria né persistenza in avvio automatico.
- Le sorgenti non contengono invocazioni di shell o comandi distruttivi.
- L'assembly non referenzia assembly di rete: solo `System`, `System.Core`, `System.Drawing`,
  `System.Windows.Forms`.
- Il manifest dichiara `asInvoker`.

### In esecuzione (`tests\verify.ps1`)

Eseguita sull'eseguibile Release reale, con analisi della vera cartella temporanea:

| Controllo | Esito |
|---|---|
| Connessioni TCP in uscita del processo durante l'analisi | **0** su almeno 40 campionamenti |
| Registro di sistema (chiavi di avvio, `Run`, `RunOnce`, `.NETFramework`, `HKCU\Software\CleanSlate`) | Invariato prima/dopo |
| Elenco dei servizi | Invariato prima/dopo |
| Elenco delle attività pianificate | Invariato prima/dopo |
| Voci di avvio automatico con nome CleanSlate | Nessuna |
| Processo residente dopo la chiusura | Nessuno |
| Nuovi file creati dal programma | Solo il report in `%LOCALAPPDATA%\CleanSlate\reports\` |
| Contenuto del report | Intestazione attesa, nessun profilo utente |

### Ispezione dell'eseguibile pubblicato (audit 2026-10-09)

Sull'artefatto destinato ai beta tester, non sui sorgenti:

| Controllo | Esito |
|---|---|
| Assembly referenziati (riflessione) | Solo `mscorlib`, `System`, `System.Core`, `System.Drawing`, `System.Windows.Forms` |
| Tipi pubblici esportati | 22, tutti di CleanSlate |
| Nomi di metadati di rete (`WebRequest`, `WebClient`, `Socket`, `Dns`, `HttpClient`, `TcpClient`, `UdpClient`, `ServicePoint`, `Ftp`, `Smtp`) | Nessuno, né in ASCII né in UTF-16 |
| Nomi di metadati di registro (`RegistryKey`) | Nessuno: la 0.3.6 contiene la sola importazione P/Invoke `RegGetValue` (`advapi32.dll`, ASCII) per la lettura del tema, verificata sulle stringhe dell'exe; `Microsoft.Win32` e `RegistryKey` assenti sia in ASCII sia in UTF-16 |
| Stringhe `http://` presenti | 2, entrambe namespace XML del manifest incorporato (`schemas.microsoft.com/SMI/...`): identificativi, non indirizzi |
| File di configurazione nel pacchetto | Nessuno (solo `CleanSlate.exe`, `README.txt`, `LICENSE.txt`, `SHA256SUMS.txt`) |
| Manifest incorporato | `requestedExecutionLevel level="asInvoker" uiAccess="false"` |

## Limiti dichiarati

1. **Campione di rete**: la verifica runtime misura le connessioni TCP stabilite del processo
   durante una sessione di analisi. Non è una cattura pacchetti a livello di rete: un'ipotetica
   trasmissione ultrabreve tra due campionamenti non sarebbe rilevata. La verifica statica
   (nessuna API di rete nei sorgenti, nessuna assembly di rete referenziata, nessun nome di
   metadato di rete nell'eseguibile pubblicato) copre questa lacuna in modo indipendente.
2. **Ambiente di test**: le verifiche sono state eseguite su questo PC, con questa versione
   dell'eseguibile. Ogni nuova build andrebbe riverificata.
3. **Il report è un file di testo**: resta sul PC dell'utente ed è leggibile da chiunque abbia
   accesso al proprio profilo. Non contiene dati personali, ma va eliminato se non serve.
4. **.NET Framework può scrivere nei propri riferimenti**: l'esecuzione di un programma .NET
   può toccare chiavi di `HKCU\Software\Microsoft\.NETFramework`. Sono scritture del runtime
   Microsoft, non di CleanSlate; il confronto delle chiavi su queste aree è mostrato come nota
   nella verifica.
5. **Pulizia reale solo su fixture sintetici**: la validazione pratica pre-beta
   (`tests/qa-practical.ps1`) esegue l'analisi sul vero `%TEMP%` dell'utente senza mai
   cancellare file preesistenti; la cancellazione viene esercitata soltanto su file sintetici
   creati dallo stesso script (prefisso `cleanslate-qa-`, marker di contenuto, dimensioni
   note) e su un sandbox dedicato usato come TMP del processo GUI. Il clic "Sì" sulla GUI
   reale non è automatizzato; il motore di produzione viene invece esercitato con una lista
   esplicita di quegli elementi sintetici.

## Contatto

Per domande sulla privacy, aprire un issue nel repository con l'etichetta `privacy`.
