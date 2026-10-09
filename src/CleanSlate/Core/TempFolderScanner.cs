using System;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace CleanSlate.Core
{
    /// <summary>
    /// Motore di analisi in sola lettura.
    ///
    /// Regole:
    ///  - non cancella, non sposta, non modifica alcun file;
    ///  - percorre soltanto la radice autorizzata e non segue junction, collegamenti
    ///    simbolici o altri reparse point;
    ///  - esclude per impostazione predefinita i file modificati nelle ultime 24 ore;
    ///  - non esce mai dal percorso autorizzato;
    ///  - è cancellabile in modo cooperativo.
    /// </summary>
    public sealed class TempFolderScanner
    {
        private const int MaxStoredErrorMessages = 50;

        public ScanResult Scan(ScanOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");

            Stopwatch stopwatch = Stopwatch.StartNew();
            ScanResult result = new ScanResult();

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

            if (options.CancellationToken.IsCancellationRequested)
            {
                result.Success = true;
                result.Cancelled = true;
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            DateTime cutoffUtc = DateTime.UtcNow - options.MinAge;
            ScanDirectory(root, root, cutoffUtc, options, result);

            // result.EstimatedBytes è mantenuto in aggiornamento man mano che i file vengono aggiunti.
            result.FilesEligible = result.Items.Count;
            result.Cancelled = options.CancellationToken.IsCancellationRequested;
            result.Success = true;
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        private void ScanDirectory(string root, string directory, DateTime cutoffUtc, ScanOptions options, ScanResult result)
        {
            if (options.CancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                return;
            }

            ScanFiles(root, directory, cutoffUtc, options, result);
            if (result.Cancelled) return;

            ScanSubDirectories(root, directory, cutoffUtc, options, result);
        }

        private void ScanFiles(string root, string directory, DateTime cutoffUtc, ScanOptions options, ScanResult result)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (options.CancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        return;
                    }

                    result.FilesScanned++;
                    ExamineFile(root, file, cutoffUtc, result);
                    ReportProgress(options, result, directory);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                result.DirectoriesSkipped++;
                AddError(result, "Accesso negato alla cartella " + directory + ".", ex);
            }
            catch (IOException ex)
            {
                result.DirectoriesSkipped++;
                AddError(result, "Impossibile leggere la cartella " + directory + ".", ex);
            }
            catch (SecurityException ex)
            {
                result.DirectoriesSkipped++;
                AddError(result, "Permesso insufficiente per la cartella " + directory + ".", ex);
            }
        }

        private void ScanSubDirectories(string root, string directory, DateTime cutoffUtc, ScanOptions options, ScanResult result)
        {
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(directory))
                {
                    if (options.CancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        return;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(sub);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        result.DirectoriesSkipped++;
                        AddError(result, "Accesso negato alla cartella " + sub + ".", ex);
                        continue;
                    }
                    catch (IOException ex)
                    {
                        result.DirectoriesSkipped++;
                        AddError(result, "Impossibile leggere la cartella " + sub + ".", ex);
                        continue;
                    }
                    catch (SecurityException ex)
                    {
                        result.DirectoriesSkipped++;
                        AddError(result, "Permesso insufficiente per la cartella " + sub + ".", ex);
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Junction / collegamento simbolico: non viene seguito.
                        result.DirectoriesSkipped++;
                        continue;
                    }

                    ScanDirectory(root, sub, cutoffUtc, options, result);
                    if (result.Cancelled) return;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                result.DirectoriesSkipped++;
                AddError(result, "Accesso negato alla cartella " + directory + ".", ex);
            }
            catch (IOException ex)
            {
                result.DirectoriesSkipped++;
                AddError(result, "Impossibile leggere la cartella " + directory + ".", ex);
            }
            catch (SecurityException ex)
            {
                result.DirectoriesSkipped++;
                AddError(result, "Permesso insufficiente per la cartella " + directory + ".", ex);
            }
        }

        private void ExamineFile(string root, string file, DateTime cutoffUtc, ScanResult result)
        {
            try
            {
                FileInfo info = new FileInfo(file);

                if (!info.Exists)
                {
                    // Rimosso da un altro processo durante la scansione.
                    result.FilesSkipped++;
                    return;
                }

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    result.FilesSkipped++;
                    return;
                }

                if (!PathSafety.IsInsideRoot(root, info.FullName))
                {
                    // Difensivo: la scansione non deve mai uscire dalla radice autorizzata.
                    result.FilesSkipped++;
                    return;
                }

                if (info.LastWriteTimeUtc > cutoffUtc)
                {
                    result.FilesSkipped++;
                    return;
                }

                FileItem item = new FileItem();
                item.Name = info.Name;
                item.FullPath = info.FullName;
                item.Length = info.Length;
                item.LastWriteTimeUtc = info.LastWriteTimeUtc;
                item.Category = ScanCategories.UserTempFiles;
                item.Selected = true;
                result.Items.Add(item);
                result.EstimatedBytes += item.Length;
            }
            catch (FileNotFoundException)
            {
                result.FilesSkipped++;
            }
            catch (DirectoryNotFoundException)
            {
                result.FilesSkipped++;
            }
            catch (UnauthorizedAccessException ex)
            {
                AddError(result, "Accesso negato al file " + file + ".", ex);
            }
            catch (IOException ex)
            {
                AddError(result, "Impossibile leggere il file " + file + ".", ex);
            }
            catch (SecurityException ex)
            {
                AddError(result, "Permesso insufficiente per il file " + file + ".", ex);
            }
        }

        private static void ReportProgress(ScanOptions options, ScanResult result, string directory)
        {
            if (options.Progress == null) return;
            ScanProgress progress = new ScanProgress();
            progress.FilesScanned = result.FilesScanned;
            progress.FilesFound = result.Items.Count;
            progress.BytesFound = result.EstimatedBytes;
            progress.DirectoriesVisited = 0;
            progress.CurrentDirectory = directory;
            try { options.Progress(progress); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private static void AddError(ScanResult result, string message, Exception exception)
        {
            result.Errors++;
            if (result.ErrorMessages.Count >= MaxStoredErrorMessages) return;
            string detail = exception != null ? exception.Message : string.Empty;
            result.ErrorMessages.Add(string.IsNullOrEmpty(detail) ? message : message + " " + detail);
        }
    }
}
