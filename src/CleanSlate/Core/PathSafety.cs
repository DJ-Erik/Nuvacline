// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.IO;
using System.Security;

namespace CleanSlate.Core
{
    /// <summary>
    /// Regole di sicurezza sui percorsi.
    /// Ogni operazione che può rimuovere dati deve transitare da questi controlli.
    /// Nessuna cancellazione viene eseguita in questa classe: è pura verifica.
    /// </summary>
    public static class PathSafety
    {
        /// <summary>Normalizza un percorso (rimuove "." e ".." in modo testuale).</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Percorso non valido: percorso vuoto.", "path");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>Normalizzazione che non genera eccezioni.</summary>
        public static bool TryNormalize(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                normalized = Normalize(path);
                return normalized.Length > 0;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
            catch (SecurityException) { return false; }
            catch (IOException) { return false; }
        }

        /// <summary>
        /// True se <paramref name="candidateFullPath"/> è rigorosamente all'interno di
        /// <paramref name="rootFullPath"/>. Il confronto è testuale dopo normalizzazione:
        /// un candidato identico alla radice NON è interno (non si può cancellare la radice).
        /// </summary>
        public static bool IsInsideRoot(string rootFullPath, string candidateFullPath)
        {
            string root;
            string candidate;
            if (!TryNormalize(rootFullPath, out root)) return false;
            if (!TryNormalize(candidateFullPath, out candidate)) return false;
            if (candidate.Length <= root.Length) return false;
            if (string.Compare(candidate, 0, root, 0, root.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            char boundary = candidate[root.Length];
            return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
        }

        /// <summary>
        /// True se un qualsiasi componente strettamente sotto la radice (incluso il file stesso)
        /// è un reparse point (junction, collegamento simbolico, ...).
        /// I componenti sopra la radice non vengono considerati: la radice è il percorso autorizzato.
        /// Restituisce true (non sicuro) se l'attributo non è leggibile.
        /// </summary>
        public static bool HasReparseComponentInsideRoot(string rootFullPath, string candidateFullPath)
        {
            string root;
            string candidate;
            if (!TryNormalize(rootFullPath, out root)) return true;
            if (!TryNormalize(candidateFullPath, out candidate)) return true;
            if (!IsInsideRoot(root, candidate)) return true;

            string relative = candidate.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length == 0) return false;

            string[] parts = relative.Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
                }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                catch (UnauthorizedAccessException) { return true; }
                catch (IOException) { return true; }
                catch (SecurityException) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Valida una radice ammessa alla scansione/pulizia.
        /// Restituisce false (con messaggio in italiano) invece di inventare percorsi alternativi.
        /// </summary>
        public static bool ValidateCleaningRoot(string candidatePath, out string normalizedRoot, out string errorMessage)
        {
            normalizedRoot = null;
            errorMessage = null;

            if (string.IsNullOrEmpty(candidatePath))
            {
                errorMessage = "Il percorso temporaneo non è disponibile.";
                return false;
            }

            string normalized;
            if (!TryNormalize(candidatePath, out normalized))
            {
                errorMessage = "Il percorso temporaneo non è valido.";
                return false;
            }

            if (IsDriveRoot(normalized))
            {
                errorMessage = "Il percorso temporaneo punta a un'unità di disco e non può essere usato.";
                return false;
            }

            if (normalized.StartsWith("\\\\", StringComparison.Ordinal))
            {
                errorMessage = "Il percorso temporaneo si trova su una risorsa di rete e non è ammesso.";
                return false;
            }

            if (!Directory.Exists(normalized))
            {
                errorMessage = "La cartella dei file temporanei non esiste o non è raggiungibile.";
                return false;
            }

            string driveRoot = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(driveRoot))
            {
                errorMessage = "Il percorso temporaneo non è valido.";
                return false;
            }

            try
            {
                DriveInfo drive = new DriveInfo(driveRoot);
                if (!drive.IsReady)
                {
                    errorMessage = "L'unità del percorso temporaneo non è disponibile.";
                    return false;
                }
                if (drive.DriveType != DriveType.Fixed)
                {
                    errorMessage = "Il percorso temporaneo non si trova su un disco locale fisso.";
                    return false;
                }
            }
            catch (ArgumentException)
            {
                errorMessage = "Il percorso temporaneo non è valido.";
                return false;
            }
            catch (IOException)
            {
                errorMessage = "L'unità del percorso temporaneo non è disponibile.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = "Non è possibile accedere all'unità del percorso temporaneo.";
                return false;
            }

            normalizedRoot = normalized;
            return true;
        }

        private static bool IsDriveRoot(string normalizedPath)
        {
            string root = Path.GetPathRoot(normalizedPath);
            if (string.IsNullOrEmpty(root)) return false;
            string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(trimmedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
