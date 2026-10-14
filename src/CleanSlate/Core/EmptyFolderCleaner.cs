using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;

namespace CleanSlate.Core
{
    /// <summary>
    /// Fase opzionale (disattivata di default) di rimozione prudente delle cartelle
    /// rimaste vuote sotto la radice autorizzata. Modulo separato dal motore dei file:
    /// la fase si attiva solo dopo una pulizia confermata e non annullata, con una
    /// conferma dedicata che elenca il numero di candidate.
    ///
    /// Regole:
    ///  - Survey è in sola lettura: propone solo directory vuote reali sotto la radice;
    ///  - la radice stessa non è mai candidata e non viene mai rimossa;
    ///  - i reparse point (junction, symlink, mount point) non vengono mai seguiti né
    ///    rimossi: la cartella collegamento è saltata e registrata;
    ///  - Remove ricalcola e riverifica ogni percorso subito prima di agire
    ///    (normalizzazione, radice, containment, esistenza, attributi, anti-TOCTOU,
    ///    enumerazione vuota ripetuta);
    ///  - la cancellazione è sempre non ricorsiva: Directory.Delete(path, false);
    ///  - nessuna soglia d'età per le cartelle: una cartella vuota non contiene dati e
    ///    su NTFS la cancellazione dei file ne aggiorna il timestamp;
    ///  - annullamento cooperativo; gli errori vengono conteggiati e non interrompono
    ///    l'operazione; nessuna shell, nessun registro, nessuna rete.
    /// </summary>
    public sealed class EmptyFolderCleaner
    {
        private enum DirectoryState
        {
            Empty = 0,
            NotEmpty,
            Missing,
            ReadError
        }

        /// <summary>
        /// Sondaggio in sola lettura: elenca le sottocartelle della radice che, al termine
        /// della rimozione in post-order delle proprie sottocartelle candidate, resterebbero
        /// vuote. Non cancella nulla.
        ///
        /// Criterio di candidatura: nessun file diretto e tutti i discendenti directory
        /// (esclusi i reparse point, che disqualificano) anch'essi candidati. Una
        /// cartella che contiene un file non può svuotarsi e non viene proposta; una
        /// cartella che contiene una junction non la contiene mai nemmeno dopo la fase.
        /// La re-verifica rigorosa ("nessun elemento") avviene in Remove, subito prima
        /// di ogni cancellazione: in post-order la figlia è già stata rimossa, quindi il
        /// padre risulta effettivamente vuoto nel momento dell'azione.
        /// </summary>
        public EmptyFolderSurvey Survey(string root, CancellationToken token)
        {
            EmptyFolderSurvey survey = new EmptyFolderSurvey();

            string normalizedRoot;
            string rootError;
            if (!PathSafety.ValidateCleaningRoot(root, out normalizedRoot, out rootError))
            {
                survey.ErrorMessage = rootError;
                return survey;
            }
            survey.Root = normalizedRoot;

            Walk(normalizedRoot, normalizedRoot, survey, token);

            survey.Cancelled = token.IsCancellationRequested;
            survey.Success = !survey.Cancelled;
            return survey;
        }

        /// <summary>
        /// Rimuove le cartelle vuote elencate, una a una, in ordine (post-order per
        /// costruzione). Ogni percorso viene re-verificato immediatamente prima dell'azione:
        /// se uno qualsiasi dei controlli fallisce la cartella viene conservata e
        /// l'esito registrato, senza interrompere le successive.
        /// </summary>
        public EmptyCleanResult Remove(string root, List<string> candidates, CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            EmptyCleanResult result = new EmptyCleanResult();

            string normalizedRoot;
            string rootError;
            if (!PathSafety.ValidateCleaningRoot(root, out normalizedRoot, out rootError))
            {
                result.ErrorMessage = rootError;
                result.Duration = stopwatch.Elapsed;
                return result;
            }
            result.Root = normalizedRoot;

            if (candidates == null || candidates.Count == 0)
            {
                result.Success = true;
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            int total = candidates.Count;
            for (int i = 0; i < total; i++)
            {
                if (token.IsCancellationRequested)
                {
                    // Cancellazione cooperativa: gli elementi restanti non vengono toccati.
                    result.Cancelled = true;
                    for (int j = i; j < total; j++)
                    {
                        EmptyDirectoryItemResult pending = new EmptyDirectoryItemResult();
                        pending.FullPath = candidates[j];
                        pending.Name = SafeFileName(candidates[j]);
                        pending.Outcome = EmptyDirectoryOutcome.SkippedCancelled;
                        pending.Message = "Operazione annullata dall'utente.";
                        result.Items.Add(pending);
                        result.SkippedCount++;
                    }
                    break;
                }

                ProcessOne(normalizedRoot, candidates[i], result);
            }

            result.Success = true;
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        /// <summary>
        /// Visita ricorsiva post-order. Restituisce true se la cartella visitata
        /// (o il suo sottoalbero) può diventare vuota rimuovendo solo cartelle candidate:
        /// in tal caso la cartella stessa viene proposta (tranne la radice).
        /// </summary>
        private bool Walk(string root, string current, EmptyFolderSurvey survey, CancellationToken token)
        {
            if (token.IsCancellationRequested) return false;

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch (DirectoryNotFoundException)
            {
                // Cartella scomparsa durante il sondaggio: sola lettura, nessun rischio.
                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Accesso negato: " + ex.Message);
                return false;
            }
            catch (IOException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Impossibile leggere la cartella: " + ex.Message);
                return false;
            }
            catch (SecurityException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Permesso insufficiente: " + ex.Message);
                return false;
            }
            catch (ArgumentException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Percorso non valido: " + ex.Message);
                return false;
            }

            // true se, rimuovendo solo le sottocartelle candidate, di current non
            // resterebbe nulla. Un solo elemento sopravvissuto (file, junction, figlia
            // non qualificata) lo disqualifica.
            bool qualifies = true;

            for (int i = 0; i < subdirectories.Length; i++)
            {
                if (token.IsCancellationRequested) return false;

                string child = subdirectories[i];
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch (DirectoryNotFoundException)
                {
                    continue;
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException ex)
                {
                    AddSurveyItem(survey, child, EmptyDirectoryOutcome.Failed, "Accesso negato: " + ex.Message);
                    qualifies = false;
                    continue;
                }
                catch (IOException ex)
                {
                    AddSurveyItem(survey, child, EmptyDirectoryOutcome.Failed, "Impossibile leggere la cartella: " + ex.Message);
                    qualifies = false;
                    continue;
                }
                catch (SecurityException ex)
                {
                    AddSurveyItem(survey, child, EmptyDirectoryOutcome.Failed, "Permesso insufficiente: " + ex.Message);
                    qualifies = false;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // Mai seguire: la cartella collegamento non viene esplorata né
                    // proposta, e sopravviverebbe alla fase → disqualifica il padre.
                    AddSurveyItem(survey, child, EmptyDirectoryOutcome.SkippedReparse,
                        "La cartella è un collegamento: non è stata percorsa né proposta.");
                    qualifies = false;
                    continue;
                }

                if (!Walk(root, child, survey, token)) qualifies = false;
                if (token.IsCancellationRequested) return false;
            }

            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                return false; // La radice non è mai candidata.

            survey.DirectoriesExamined++;

            // File diretti: non verrebbero eliminati dalla fase cartelle, quindi la
            // cartella non può diventare vuota nemmeno dopo le sottocartelle.
            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Accesso negato: " + ex.Message);
                return false;
            }
            catch (IOException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Impossibile leggere la cartella: " + ex.Message);
                return false;
            }
            catch (SecurityException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Permesso insufficiente: " + ex.Message);
                return false;
            }
            catch (ArgumentException ex)
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.Failed, "Percorso non valido: " + ex.Message);
                return false;
            }
            if (files.Length > 0) qualifies = false;

            if (!qualifies) return false;

            // Contenimento difensivo: ogni percorso che entra nell'elenco delle candidate
            // deve superare esplicitamente il confine della radice autorizzata.
            if (!PathSafety.IsInsideRoot(root, current))
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.SkippedOutsideRoot,
                    "La cartella si trova al di fuori della radice autorizzata.");
                return false;
            }
            if (PathSafety.HasReparseComponentInsideRoot(root, current))
            {
                AddSurveyItem(survey, current, EmptyDirectoryOutcome.SkippedReparse,
                    "La cartella è raggiungibile solo attraverso un collegamento ed è stata ignorata.");
                return false;
            }

            survey.Candidates.Add(current);
            return true;
        }

        private void ProcessOne(string root, string candidate, EmptyCleanResult result)
        {
            EmptyDirectoryItemResult outcome = new EmptyDirectoryItemResult();
            result.Items.Add(outcome);

            if (string.IsNullOrEmpty(candidate))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedInvalidPath;
                outcome.Message = "Percorso non valido.";
                result.SkippedCount++;
                return;
            }

            string full;
            if (!PathSafety.TryNormalize(candidate, out full))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedInvalidPath;
                outcome.Message = "Il percorso non è valido e la cartella è stata ignorata.";
                result.SkippedCount++;
                return;
            }
            outcome.FullPath = full;
            outcome.Name = SafeFileName(full);

            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedRoot;
                outcome.Message = "Non è possibile eliminare la cartella radice.";
                result.SkippedCount++;
                return;
            }

            if (!PathSafety.IsInsideRoot(root, full))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedOutsideRoot;
                outcome.Message = "La cartella si trova al di fuori della cartella autorizzata ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }

            if (File.Exists(full))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedInvalidPath;
                outcome.Message = "Il percorso non indica una cartella ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }

            if (!Directory.Exists(full))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedNotFound;
                outcome.Message = "La cartella non esiste più ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(full);
            }
            catch (FileNotFoundException)
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedNotFound;
                outcome.Message = "La cartella non esiste più ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }
            catch (DirectoryNotFoundException)
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedNotFound;
                outcome.Message = "La cartella non esiste più ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "Accesso negato: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (IOException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "Impossibile accedere alla cartella: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (SecurityException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "Permesso insufficiente: " + ex.Message;
                result.ErrorCount++;
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedReparse;
                outcome.Message = "La cartella è un collegamento ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }

            // Ultimo controllo strutturale, subito prima della cancellazione.
            // Se un componente intermedio venisse sostituito da una junction nel
            // frattempo, Directory.Delete risolverebbe il percorso attraverso il
            // collegamento: ridurre al minimo la finestra limita il rischio.
            if (PathSafety.HasReparseComponentInsideRoot(root, full))
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedReparse;
                outcome.Message = "La cartella è raggiungibile solo attraverso un collegamento ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }

            // Re-verifica dell'assenza di contenuti, immediatamente prima dell'azione.
            string inspectError;
            DirectoryState state = Inspect(full, out inspectError);
            if (state == DirectoryState.Missing)
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedNotFound;
                outcome.Message = "La cartella non esiste più ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }
            if (state == DirectoryState.NotEmpty)
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedNotEmpty;
                outcome.Message = "La cartella contiene ancora elementi ed è stata conservata.";
                result.SkippedCount++;
                return;
            }
            if (state == DirectoryState.ReadError)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = inspectError;
                result.ErrorCount++;
                return;
            }

            try
            {
                // Mai ricorsivo: se la cartella non è più vuota l'operazione fallisce
                // senza toccare nulla al suo interno.
                Directory.Delete(full, false);
            }
            catch (DirectoryNotFoundException)
            {
                outcome.Outcome = EmptyDirectoryOutcome.SkippedNotFound;
                outcome.Message = "La cartella non esiste più ed è stata ignorata.";
                result.SkippedCount++;
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "La cartella è protetta o in uso: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (IOException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "Impossibile rimuovere la cartella: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (SecurityException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "Permesso insufficiente: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (ArgumentException ex)
            {
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "Percorso non valido: " + ex.Message;
                result.ErrorCount++;
                return;
            }

            if (Directory.Exists(full))
            {
                // La cartella non è stata realmente rimossa: non se ne dichiara l'esito.
                outcome.Outcome = EmptyDirectoryOutcome.Failed;
                outcome.Message = "La cartella non è stata rimossa.";
                result.ErrorCount++;
                return;
            }

            outcome.Outcome = EmptyDirectoryOutcome.Removed;
            outcome.Message = "Rimossa.";
            result.RemovedCount++;
        }

        /// <summary>
        /// Stato di contenuto della cartella. Ogni eccezione di I/O viene tradotta in
        /// uno stato: nessuna eccezione esce da questo metodo verso il chiamante.
        /// </summary>
        private static DirectoryState Inspect(string directory, out string errorMessage)
        {
            errorMessage = null;
            IEnumerator<string> entries = null;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                if (entries.MoveNext()) return DirectoryState.NotEmpty;
                return DirectoryState.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return DirectoryState.Missing;
            }
            catch (FileNotFoundException)
            {
                return DirectoryState.Missing;
            }
            catch (UnauthorizedAccessException ex)
            {
                errorMessage = "Accesso negato: " + ex.Message;
                return DirectoryState.ReadError;
            }
            catch (IOException ex)
            {
                errorMessage = "Impossibile leggere la cartella: " + ex.Message;
                return DirectoryState.ReadError;
            }
            catch (SecurityException ex)
            {
                errorMessage = "Permesso insufficiente: " + ex.Message;
                return DirectoryState.ReadError;
            }
            catch (ArgumentException ex)
            {
                errorMessage = "Percorso non valido: " + ex.Message;
                return DirectoryState.ReadError;
            }
            finally
            {
                if (entries != null) entries.Dispose();
            }
        }

        private static void AddSurveyItem(EmptyFolderSurvey survey, string path,
            EmptyDirectoryOutcome outcome, string message)
        {
            EmptyDirectoryItemResult item = new EmptyDirectoryItemResult();
            item.FullPath = path;
            item.Name = SafeFileName(path);
            item.Outcome = outcome;
            item.Message = message;
            survey.Items.Add(item);
        }

        private static string SafeFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                return Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }
    }
}
