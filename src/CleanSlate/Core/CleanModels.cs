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

    /// <summary>Motivo per cui una cartella non è stata rimossa (o lo è stata).</summary>
    public enum EmptyDirectoryOutcome
    {
        Removed = 0,
        SkippedNotFound,
        SkippedNotEmpty,
        SkippedReparse,
        SkippedOutsideRoot,
        SkippedRoot,
        SkippedInvalidPath,
        SkippedCancelled,
        Failed
    }

    /// <summary>Esito dell'operazione su una singola cartella.</summary>
    public sealed class EmptyDirectoryItemResult
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public EmptyDirectoryOutcome Outcome { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Sondaggio in sola lettura delle cartelle vuote sotto la radice autorizzata.
    /// Non viene eseguita alcuna cancellazione in questa fase: serve solo a proporre
    /// le candidate alla conferma dedicata dell'utente.
    /// </summary>
    public sealed class EmptyFolderSurvey
    {
        public EmptyFolderSurvey()
        {
            Success = false;
            Cancelled = false;
            Candidates = new List<string>();
            Items = new List<EmptyDirectoryItemResult>();
        }

        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Root { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>Numero di sottocartelle (esclusa la radice) effettivamente esaminate.</summary>
        public int DirectoriesExamined { get; set; }

        /// <summary>Percorsi assoluti vuoti e proposti, in ordine post-order (figlie prima dei padri).</summary>
        public List<string> Candidates { get; set; }

        /// <summary>Anomalie rilevate durante il sondaggio (reparse, errori di lettura).</summary>
        public List<EmptyDirectoryItemResult> Items { get; set; }
    }

    /// <summary>
    /// Riepilogo della fase opzionale di rimozione delle cartelle vuote.
    /// Classe separata da <see cref="CleanResult"/>: quando l'opzione è disattivata
    /// questa fase non esiste e la pulizia dei file resta identica alla 0.3.6.
    /// </summary>
    public sealed class EmptyCleanResult
    {
        public EmptyCleanResult()
        {
            Success = false;
            Cancelled = false;
            Items = new List<EmptyDirectoryItemResult>();
        }

        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Root { get; set; }
        public string ErrorMessage { get; set; }

        public int RemovedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }

        public List<EmptyDirectoryItemResult> Items { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
