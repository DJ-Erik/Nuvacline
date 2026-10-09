using System;
using System.Collections.Generic;
using System.Text;

namespace CleanSlate.Core
{
    /// <summary>
    /// Contenuto del report locale. Costruito solo in memoria: nessun file viene
    /// scritto finché l'utente non richiede esplicitamente l'esportazione.
    /// </summary>
    public sealed class ReportData
    {
        public ReportData()
        {
            Notes = new List<string>();
            ScanErrorMessages = new List<string>();
            GeneratedLocal = DateTime.Now;
        }

        public string ProductName { get; set; }
        public string Version { get; set; }
        public DateTime GeneratedLocal { get; set; }
        public string Operation { get; set; }
        public string Category { get; set; }

        public bool ScanAvailable { get; set; }
        public bool ScanCancelled { get; set; }
        public int FilesScanned { get; set; }
        public int FilesEligible { get; set; }
        public int FilesSkipped { get; set; }
        public int DirectoriesSkipped { get; set; }
        public int ScanErrors { get; set; }
        public long EstimatedBytes { get; set; }
        public List<string> ScanErrorMessages { get; set; }

        public bool CleanAvailable { get; set; }
        public bool CleanCancelled { get; set; }
        public int DeletedCount { get; set; }
        public long DeletedBytes { get; set; }
        public int CleanSkipped { get; set; }
        public int CleanErrors { get; set; }
        public long EstimatedNotRecovered { get; set; }

        public string DurationText { get; set; }
        public List<string> Notes { get; set; }
    }

    /// <summary>
    /// Costruisce il report a partire dagli esiti in memoria.
    /// I percorsi completi vengono sostituti con un segnaposto: il report non contiene
    /// nome utente, elenchi di file personali o dati non necessari.
    /// </summary>
    public static class ReportBuilder
    {
        public const string RootPlaceholder = "%TEMP%";

        public static ReportData Build(ScanResult scan, CleanResult clean, string authorizedRoot)
        {
            ReportData data = new ReportData();
            data.ProductName = "CleanSlate";
            data.Version = AppInfo.VersionText;
            data.Category = ScanCategories.UserTempFiles;

            if (scan != null)
            {
                data.ScanAvailable = true;
                data.ScanCancelled = scan.Cancelled;
                data.FilesScanned = scan.FilesScanned;
                data.FilesEligible = scan.FilesEligible;
                data.FilesSkipped = scan.FilesSkipped;
                data.DirectoriesSkipped = scan.DirectoriesSkipped;
                data.ScanErrors = scan.Errors;
                data.EstimatedBytes = scan.EstimatedBytes;
                for (int i = 0; i < scan.ErrorMessages.Count && i < 20; i++)
                    data.ScanErrorMessages.Add(Sanitize(scan.ErrorMessages[i], authorizedRoot));
            }

            if (clean != null)
            {
                data.CleanAvailable = true;
                data.CleanCancelled = clean.Cancelled;
                data.DeletedCount = clean.DeletedCount;
                data.DeletedBytes = clean.DeletedBytes;
                data.CleanSkipped = clean.SkippedCount;
                data.CleanErrors = clean.ErrorCount;
                data.EstimatedNotRecovered = clean.EstimatedNotRecovered;
            }

            if (data.ScanAvailable && data.CleanAvailable)
                data.Operation = "Analisi e pulizia";
            else if (data.ScanAvailable)
                data.Operation = "Analisi";
            else if (data.CleanAvailable)
                data.Operation = "Pulizia";
            else
                data.Operation = "Nessuna operazione";

            TimeSpan duration = TimeSpan.Zero;
            if (scan != null) duration = duration + scan.Duration;
            if (clean != null) duration = duration + clean.Duration;
            data.DurationText = duration.TotalSeconds.ToString("N1", System.Globalization.CultureInfo.CurrentCulture) + " s";

            if (data.ScanAvailable && !data.ScanCancelled && data.FilesEligible == 0 && data.ScanErrors == 0)
                data.Notes.Add("Non sono stati trovati file temporanei recuperabili secondo le regole della versione 0.1.");
            if (data.ScanAvailable && data.ScanCancelled)
                data.Notes.Add("L'analisi è stata annullata: i valori si riferiscono ai risultati parziali.");
            if (data.CleanAvailable && data.CleanCancelled)
                data.Notes.Add("La pulizia è stata annullata: alcuni elementi non sono stati considerati.");
            if (data.EstimatedBytes > 0 && data.CleanAvailable)
                data.Notes.Add("Lo spazio indicato come non recuperato è una stima e non indica spazio corrotto.");
            data.Notes.Add("Valori di dimensione stimati in base a quanto dichiarato dal filesystem.");
            data.Notes.Add("Nessun dato viene trasmesso: il report resta su questo computer.");

            return data;
        }

        /// <summary>Sostituisce la radice autorizzata con un segnaposto e rimuove elementi non necessari.</summary>
        public static string Sanitize(string text, string authorizedRoot)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string result = text;
            if (!string.IsNullOrEmpty(authorizedRoot))
                result = ReplaceIgnoreCase(result, authorizedRoot, RootPlaceholder);
            return result;
        }

        private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue)) return input;
            StringBuilder builder = new StringBuilder(input.Length);
            int index = 0;
            while (index < input.Length)
            {
                int match = input.IndexOf(oldValue, index, StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                {
                    builder.Append(input, index, input.Length - index);
                    break;
                }
                builder.Append(input, index, match - index);
                builder.Append(newValue);
                index = match + oldValue.Length;
            }
            return builder.ToString();
        }
    }
}
