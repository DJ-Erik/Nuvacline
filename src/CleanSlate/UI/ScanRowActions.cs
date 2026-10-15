// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Diagnostics;
using System.IO;
using CleanSlate.Core;

namespace CleanSlate.UI
{
    /// <summary>
    /// Logica pura del menu contestuale dei risultati di scansione.
    ///
    /// Tutte le funzioni operano ESCLUSIVAMENTE sul percorso memorizzato nel modello
    /// della riga (FileItem.FullPath): nessuna ricostruzione dal testo visualizzato,
    /// nessun accesso a percorsi arbitrari. Il menu è uno strumento di consultazione:
    /// qui non esiste alcun codice capace di cancellare o modificare i file.
    /// </summary>
    public static class ScanRowActions
    {
        public const string InvalidTargetMessage =
            "La riga selezionata non è associata a un file valido.";

        public const string MissingFileMessage =
            "Il file non esiste più: potrebbe essere stato spostato o eliminato dopo l'analisi.";

        public const string ClipboardErrorMessage =
            "Impossibile accedere agli appunti.";

        public const string ExplorerErrorMessage =
            "Impossibile avviare Esplora file.";

        /// <summary>True se l'oggetto della riga è un file con percorso completo utilizzabile.</summary>
        public static bool IsValidTarget(object tag)
        {
            FileItem item = tag as FileItem;
            return item != null && !string.IsNullOrEmpty(item.FullPath);
        }

        /// <summary>Solo il nome del file con estensione, mai il percorso.</summary>
        public static string GetFileName(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            try { return Path.GetFileName(fullPath); }
            catch (ArgumentException) { return string.Empty; }
        }

        /// <summary>
        /// La cartella contenente il file, senza il nome finale e senza normalizzazioni
        /// arbitrarie: è il percorso originale privo dell'ultimo segmento.
        /// </summary>
        public static string GetFolder(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            try { return Path.GetDirectoryName(fullPath); }
            catch (ArgumentException) { return string.Empty; }
        }

        /// <summary>
        /// Avvia Esplora file con l'equivalente di explorer.exe /select,"percorso-completo".
        /// Il file mai viene passato come file da eseguire: è un argomento di selezione.
        /// </summary>
        public static ProcessStartInfo BuildExplorerSelectStartInfo(string fullPath)
        {
            if (fullPath == null) throw new ArgumentNullException("fullPath");
            if (fullPath.Length == 0) throw new ArgumentException(InvalidTargetMessage, "fullPath");
            if (fullPath.IndexOf('"') >= 0)
                throw new ArgumentException("Il percorso contiene caratteri non ammessi.", "fullPath");

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "explorer.exe";
            info.Arguments = "/select,\"" + fullPath + "\"";
            info.UseShellExecute = true;
            return info;
        }

        /// <summary>
        /// Valida il percorso prima di aprire Esplora file. Se il file non esiste più
        /// (spostato o eliminato dopo la scansione) restituisce false con un messaggio
        /// chiaro invece di lanciare l'eccezione.
        /// </summary>
        public static bool CanOpenFolder(string fullPath, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(fullPath))
            {
                errorMessage = InvalidTargetMessage;
                return false;
            }

            bool rooted;
            try { rooted = Path.IsPathRooted(fullPath); }
            catch (ArgumentException) { rooted = false; }
            if (!rooted)
            {
                errorMessage = InvalidTargetMessage;
                return false;
            }

            bool exists;
            try { exists = File.Exists(fullPath); }
            catch (Exception) { exists = false; }
            if (!exists)
            {
                errorMessage = MissingFileMessage;
                return false;
            }
            return true;
        }
    }
}
