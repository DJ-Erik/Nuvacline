// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace Nuvacline.Core
{
    /// <summary>
    /// Motore di cancellazione prudente. Modulo separato dalla scansione:
    /// non si fida dei percorsi memorizzati nella GUI né dei risultati della scansione,
    /// ma ricalcola e riverifica ogni elemento prima di agire.
    ///
    /// Regole:
    ///  - ricalcola il percorso effettivo e verifica che sia dentro la radice autorizzata;
    ///  - rifiuta la radice stessa, le directory e i reparse point;
    ///  - rifiuta file non più conformi al limite temporale;
    ///  - salta gli elementi che non superano le verifiche, senza interrompere l'operazione;
    ///  - non usa shell, PowerShell o cancellazioni ricorsive;
    ///  - dichiara solo i byte effettivamente rimossi.
    /// </summary>
    public sealed class TempFileCleaner
    {
        public CleanResult Clean(CleanOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");

            Stopwatch stopwatch = Stopwatch.StartNew();
            CleanResult result = new CleanResult();

            string root;
            string rootError;
            if (!PathSafety.ValidateCleaningRoot(options.Root, out root, out rootError))
            {
                result.Success = false;
                result.ErrorMessage = rootError;
                result.Duration = stopwatch.Elapsed;
                return result;
            }
            result.Root = root;

            List<FileItem> items = options.Items ?? new List<FileItem>();
            DateTime cutoffUtc = DateTime.UtcNow - options.MinAge;
            int total = items.Count;

            for (int i = 0; i < total; i++)
            {
                if (options.CancellationToken.IsCancellationRequested)
                {
                    // Cancellazione cooperativa: l'elemento corrente è già stato gestito
                    // per intero; quelli restanti non vengono toccati.
                    result.Cancelled = true;
                    for (int j = i; j < total; j++)
                    {
                        CleanItemResult pending = new CleanItemResult();
                        pending.Name = items[j].Name;
                        pending.FullPath = items[j].FullPath;
                        pending.Outcome = CleanOutcome.SkippedCancelled;
                        pending.Message = "Operazione annullata dall'utente.";
                        result.Items.Add(pending);
                        result.SkippedCount++;
                    }
                    break;
                }

                ProcessOne(root, items[i], cutoffUtc, result);
                ReportProgress(options, result, i + 1, total, items[i]);
            }

            result.EstimatedBytes = 0;
            for (int i = 0; i < items.Count; i++) result.EstimatedBytes += items[i].Length;
            result.Success = true;
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        private void ProcessOne(string root, FileItem item, DateTime cutoffUtc, CleanResult result)
        {
            CleanItemResult outcome = new CleanItemResult();
            outcome.Name = item != null ? item.Name : null;
            outcome.FullPath = item != null ? item.FullPath : null;
            result.Items.Add(outcome);

            if (item == null || string.IsNullOrEmpty(item.FullPath))
            {
                outcome.Outcome = CleanOutcome.SkippedInvalidPath;
                outcome.Message = "Percorso non valido.";
                result.SkippedCount++;
                return;
            }

            string full;
            if (!PathSafety.TryNormalize(item.FullPath, out full))
            {
                outcome.Outcome = CleanOutcome.SkippedInvalidPath;
                outcome.Message = "Il percorso non è valido e l'elemento è stato ignorato.";
                result.SkippedCount++;
                return;
            }
            outcome.FullPath = full;
            outcome.Name = Path.GetFileName(full);

            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                outcome.Outcome = CleanOutcome.SkippedRoot;
                outcome.Message = "Non è possibile eliminare la cartella radice.";
                result.SkippedCount++;
                return;
            }

            if (!PathSafety.IsInsideRoot(root, full))
            {
                outcome.Outcome = CleanOutcome.SkippedOutsideRoot;
                outcome.Message = "Il file si trova al di fuori della cartella autorizzata ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }

            if (!File.Exists(full))
            {
                outcome.Outcome = CleanOutcome.SkippedNotFound;
                outcome.Message = "Il file non esiste più ed è stato ignorato.";
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
                outcome.Outcome = CleanOutcome.SkippedNotFound;
                outcome.Message = "Il file non esiste più ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }
            catch (DirectoryNotFoundException)
            {
                outcome.Outcome = CleanOutcome.SkippedNotFound;
                outcome.Message = "Il file non esiste più ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Accesso negato: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (IOException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Impossibile accedere al file: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (SecurityException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Permesso insufficiente: " + ex.Message;
                result.ErrorCount++;
                return;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                outcome.Outcome = CleanOutcome.SkippedDirectory;
                outcome.Message = "Gli elementi non sono file: la cartella è stata ignorata.";
                result.SkippedCount++;
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                outcome.Outcome = CleanOutcome.SkippedReparse;
                outcome.Message = "Il file è un collegamento ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }

            long length;
            DateTime lastWriteUtc;
            try
            {
                FileInfo info = new FileInfo(full);
                if (!info.Exists)
                {
                    outcome.Outcome = CleanOutcome.SkippedNotFound;
                    outcome.Message = "Il file non esiste più ed è stato ignorato.";
                    result.SkippedCount++;
                    return;
                }
                length = info.Length;
                lastWriteUtc = info.LastWriteTimeUtc;
            }
            catch (IOException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Impossibile leggere il file: " + ex.Message;
                result.ErrorCount++;
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Accesso negato: " + ex.Message;
                result.ErrorCount++;
                return;
            }

            if (lastWriteUtc > cutoffUtc)
            {
                outcome.Outcome = CleanOutcome.SkippedTooRecent;
                outcome.Message = "Il file è stato modificato di recente ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }

            // Ultimo controllo strutturale, subito prima della cancellazione.
            // Se una directory del percorso venisse sostituita da una junction nel frattempo,
            // File.Delete risolverebbe il percorso attraverso il collegamento: ridurre al
            // minimo la finestra tra questo controllo e la cancellazione limita il rischio.
            if (PathSafety.HasReparseComponentInsideRoot(root, full))
            {
                outcome.Outcome = CleanOutcome.SkippedReparse;
                outcome.Message = "Il file si trova attraverso un collegamento ed è stato ignorato.";
                result.SkippedCount++;
                return;
            }

            outcome.Bytes = length;

            try
            {
                File.Delete(full);
            }
            catch (UnauthorizedAccessException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Il file è protetto o in uso: " + ex.Message;
                result.ErrorCount++;
                outcome.Bytes = 0;
                return;
            }
            catch (IOException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Il file è in uso o bloccato: " + ex.Message;
                result.ErrorCount++;
                outcome.Bytes = 0;
                return;
            }
            catch (SecurityException ex)
            {
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Permesso insufficiente: " + ex.Message;
                result.ErrorCount++;
                outcome.Bytes = 0;
                return;
            }

            if (File.Exists(full))
            {
                // Il file non è stato realmente rimosso: non se ne dichiara la liberazione.
                outcome.Outcome = CleanOutcome.Failed;
                outcome.Message = "Il file non è stato rimosso.";
                result.ErrorCount++;
                outcome.Bytes = 0;
                return;
            }

            outcome.Outcome = CleanOutcome.Deleted;
            outcome.Message = "Eliminato.";
            result.DeletedCount++;
            result.DeletedBytes += length;
        }

        private static void ReportProgress(CleanOptions options, CleanResult result, int processed, int total, FileItem item)
        {
            if (options.Progress == null) return;
            CleanProgress progress = new CleanProgress();
            progress.Processed = processed;
            progress.Total = total;
            progress.Deleted = result.DeletedCount;
            progress.DeletedBytes = result.DeletedBytes;
            progress.CurrentName = item != null ? item.Name : null;
            try { options.Progress(progress); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
