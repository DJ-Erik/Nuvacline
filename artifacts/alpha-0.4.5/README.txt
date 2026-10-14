CleanSlate 0.4.5
==================

COSA FA
-------
CleanSlate individua e rimuove i file temporanei dell'utente corrente
(carella restituita dall'ambiente Windows), escludendo per impostazione
predefinita i file modificati nelle ultime 24 ore. L'interfaccia segue
automaticamente il tema chiaro/scuro impostato in Windows.

COME SI USA
-----------
1. Avviare CleanSlate.exe.
2. Premere "Analizza": l'analisi e' in sola lettura e non modifica nulla.
3. Esaminare l'elenco, selezionare o deselezionare gli elementi desiderati.
4. Premere "Pulisci selezionati" e confermare nella finestra di conferma.
5. Il report locale viene scritto in automatico in
   %LOCALAPPDATA%\CleanSlate\reports\ e il percorso appare nella finestra.
   "Apri report" lo apre nel lettore di testo predefinito.
6. Il pulsante "?" mostra le informazioni sul prodotto; il pulsante con
   l'ingranaggio consente di attivare o disattivare la conservazione dei
   report e di aprire la cartella che li contiene.

REQUISITI
---------
- Windows 10 (build 1903 o successive) oppure Windows 11, 64 bit.
- .NET Framework 4.8 o successivo, normalmente gia' presente in Windows.
- Nessun privilegio di amministratore.
- Nessuna connessione di rete necessaria o utilizzata.

NON FA
------
- Non si collega a Internet, non raccoglie e non trasmette dati.
- Non modifica il registro di Windows.
- Non installa servizi, attivita' pianificate, avvio automatico o processi residenti.
- Non contiene pubblicita, telemetria, account o aggiornamenti automatici.
- Non promette di accelerare il computer.
- Non elimina file senza una conferma esplicita.
- Non puo' recuperare i file che sono stati eliminati.

LIMITI CONOSCIUTI
-----------------
- Versione alpha: l'unica categoria supportata e' "File temporanei dell'utente".
- I file recenti (ultime 24 ore) e i file bloccati vengono saltati.
- I percorsi molto lunghi (> 260 caratteri) possono non essere gestiti:
  in tal caso l'elemento viene saltato e contegliato, mai eliminato a caso.
- Lo spazio indicato come recuperabile e' una stima: parte puo' gia' essere
  libero o non immediatamente disponibile.

CONTROLLI DI INTEGRITA'
-----------------------
I file SHA256SUMS.txt riportano l'impronta SHA-256 dei file di questa cartella.
Verifica con:
  certutil -hashfile CleanSlate.exe SHA256

DIPIENDENZE
-----------
Nessun componente di terze parti e' incluso. Vedere LICENSE.txt.
