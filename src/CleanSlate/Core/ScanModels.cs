using System;
using System.Collections.Generic;
using System.Threading;

namespace CleanSlate.Core
{
    /// <summary>Nome della unica categoria supportata da CleanSlate.</summary>
    public static class ScanCategories
    {
        public const string UserTempFiles = "File temporanei dell'utente";
    }

    /// <summary>Valori predefiniti delle regole di scansione.</summary>
    public static class ScanDefaults
    {
        /// <summary>I file modificati nelle ultime 24 ore vengono esclusi.</summary>
        public static readonly TimeSpan MinAge = TimeSpan.FromHours(24);
    }

    /// <summary>Un elemento individuato dalla scansione.</summary>
    public sealed class FileItem
    {
        public FileItem()
        {
            Selected = true;
        }

        public string Name { get; set; }
        public string FullPath { get; set; }
        public long Length { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string Category { get; set; }
        public bool Selected { get; set; }
    }

    /// <summary>Parametri di una scansione (lettura sola).</summary>
    public sealed class ScanOptions
    {
        public ScanOptions()
        {
            MinAge = ScanDefaults.MinAge;
            CancellationToken = CancellationToken.None;
            Progress = null;
        }

        /// <summary>Radice autorizzata. Deve superare <see cref="PathSafety.ValidateCleaningRoot"/>.</summary>
        public string Root { get; set; }

        /// <summary>Età minima richiesta perché un file sia considerato recuperabile.</summary>
        public TimeSpan MinAge { get; set; }

        public CancellationToken CancellationToken { get; set; }

        /// <summary>Callback opzionale invocato per ogni file esaminato (cooperativo).</summary>
        public Action<ScanProgress> Progress { get; set; }
    }

    /// <summary>Avanzamento della scansione, riportato in tempo reale.</summary>
    public sealed class ScanProgress
    {
        public int FilesScanned { get; set; }
        public int FilesFound { get; set; }
        public long BytesFound { get; set; }
        public int DirectoriesVisited { get; set; }
        public string CurrentDirectory { get; set; }
    }

    /// <summary>Esito della scansione in sola lettura.</summary>
    public sealed class ScanResult
    {
        public ScanResult()
        {
            Success = false;
            Cancelled = false;
            Items = new List<FileItem>();
            ErrorMessages = new List<string>();
            Category = ScanCategories.UserTempFiles;
        }

        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Root { get; set; }
        public string ErrorMessage { get; set; }
        public string Category { get; set; }

        /// <summary>Tutti i file incontrati durante la ricorsione.</summary>
        public int FilesScanned { get; set; }

        /// <summary>File che soddisfano tutte le regole e vengono proposti all'utente.</summary>
        public int FilesEligible { get; set; }

        /// <summary>File scartati (troppo recenti, reparse point, scomparsi, ...).</summary>
        public int FilesSkipped { get; set; }

        /// <summary>Cartelle non percorse (reparse point o errori di accesso).</summary>
        public int DirectoriesSkipped { get; set; }

        public int Errors { get; set; }
        public List<string> ErrorMessages { get; set; }
        public List<FileItem> Items { get; set; }
        public long EstimatedBytes { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
