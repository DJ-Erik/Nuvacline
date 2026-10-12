# Regole di esecuzione autonoma per OpenCode

## Obiettivo
Eseguire le attività autorizzate in modo autonomo, affidabile e sicuro, anche quando l'utente non è presente davanti al computer.

## Esecuzione in background
- Preferire test, build, verifiche e attività che non richiedono interazione con il desktop.
- Non dipendere dalla clipboard, dalla finestra attiva o da input GUI quando esiste un'alternativa deterministica.
- Non tentare di aggirare la schermata di blocco, i controlli di accesso, le protezioni del sistema operativo o i permessi dell'utente.
- Non simulare uno sblocco della sessione e non tentare di acquisire input interattivi mentre la sessione è bloccata.
- Non disabilitare blocco automatico, timeout di sicurezza o altre protezioni per mantenere attivo il lavoro.

## Gestione dei test bloccati dall'ambiente
- Se un test fallisce per una risorsa esterna non disponibile, raccogliere l'errore e verificare se il problema è riproducibile senza modificare il comportamento del prodotto.
- Distinguere chiaramente i fallimenti ambientali dai difetti del codice.
- Non saltare, falsificare o contrassegnare come superati i test falliti.
- Non modificare i test per nascondere un problema ambientale.
- Se una verifica richiede una sessione interattiva sbloccata, sospendere soltanto quella verifica, completare le attività indipendenti e riportare con precisione ciò che resta da eseguire.

## Sicurezza e autorizzazioni
- Non eseguire push, pubblicazioni, distribuzioni remote o operazioni distruttive senza autorizzazione esplicita.
- Non cancellare dati dell'utente per risolvere problemi di test.
- Prima di modificare impostazioni di sistema, servizi, attività pianificate o configurazioni di accesso, chiedere conferma.
- Conservare log diagnostici essenziali senza includere segreti o dati personali non necessari.

## Rapporto finale
Indicare le attività completate, i test superati e falliti, le cause ambientali confermate o soltanto sospette e le verifiche che richiedono ancora l'intervento dell'utente.
