using System;
using System.Collections.Generic;
using System.Threading;

namespace CleanSlate.Core
{
    /// <summary>Motivo per cui un elemento non è stato eliminato (o lo è stato).</summary>
    public enum CleanOutcome
    {
        Deleted = 0,
        SkippedNotFound,
        SkippedOutsideRoot,
        SkippedInvalidPath,
        SkippedRoot,
        SkippedDirectory,
        SkippedReparse,
        SkippedTooRecent,
        SkippedCancelled,
        Failed
    }

    /// <summary>Esito dell'operazione su un singolo elemento.</summary>
    public sealed class CleanItemResult
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public CleanOutcome Outcome { get; set; }
        public string Message { get; set; }
        public long Bytes { get; set; }
    }

    /// <summary>Parametri della pulizia.</summary>
    public sealed class CleanOptions
    {
        public CleanOptions()
        {
            MinAge = ScanDefaults.MinAge;
            Items = new List<FileItem>();
            CancellationToken = CancellationToken.None;
            Progress = null;
        }

        /// <summary>Radice autorizzata: deve essere la stessa usata per la scansione.</summary>
        public string Root { get; set; }

        public TimeSpan MinAge { get; set; }
        public List<FileItem> Items { get; set; }
        public CancellationToken CancellationToken { get; set; }
        public Action<CleanProgress> Progress { get; set; }
    }

    public sealed class CleanProgress
    {
        public int Processed { get; set; }
        public int Total { get; set; }
        public int Deleted { get; set; }
        public long DeletedBytes { get; set; }
        public string CurrentName { get; set; }
    }

    /// <summary>Riepilogo della pulizia.</summary>
    public sealed class CleanResult
    {
        public CleanResult()
        {
            Success = false;
            Cancelled = false;
            Items = new List<CleanItemResult>();
        }

        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Root { get; set; }
        public string ErrorMessage { get; set; }

        public int DeletedCount { get; set; }
        public long DeletedBytes { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }

        /// <summary>Totale dei byte proposti dall'utente per la cancellazione.</summary>
        public long EstimatedBytes { get; set; }

        public List<CleanItemResult> Items { get; set; }
        public TimeSpan Duration { get; set; }

        /// <summary>Spazio stimato che non è stato effettivamente recuperato.</summary>
        public long EstimatedNotRecovered
        {
            get { return EstimatedBytes - DeletedBytes; }
        }
    }
}
