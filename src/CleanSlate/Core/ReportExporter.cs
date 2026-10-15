// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CleanSlate.Core
{
    /// <summary>
    /// Esportazione del report locale in TXT o JSON.
    /// L'oggetto del report viene sempre costruito in memoria: la scrittura su disco avviene
    /// solo su un percorso passato esplicitamente dal chiamante e nessun file viene trasmesso.
    /// </summary>
    public static class ReportExporter
    {
        public static void Export(string path, ReportData data)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("È necessario scegliere un percorso di destinazione.", "path");

            string extension = Path.GetExtension(path);
            string content;
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                content = BuildJson(data);
            else
                content = BuildText(data);

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public static string BuildText(ReportData data)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("==============================================");
            sb.AppendLine(" " + data.ProductName + " - report locale");
            sb.AppendLine("==============================================");
            sb.AppendLine("Versione     : " + data.Version);
            sb.AppendLine("Generato     : " + data.GeneratedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Operazione   : " + data.Operation);
            sb.AppendLine("Categoria    : " + data.Category);
            sb.AppendLine("Perimetro    : file temporanei dell'utente corrente");
            sb.AppendLine();

            sb.AppendLine("--- Analisi (sola lettura) ---");
            if (!data.ScanAvailable)
            {
                sb.AppendLine("Nessuna analisi eseguita.");
            }
            else
            {
                sb.AppendLine("File analizzati            : " + data.FilesScanned);
                sb.AppendLine("File proposti              : " + data.FilesEligible);
                sb.AppendLine("File esclusi dalla soglia  : " + data.FilesExcludedByAge);
                sb.AppendLine("File scartati              : " + data.FilesSkipped);
                sb.AppendLine("Cartelle non percorse      : " + data.DirectoriesSkipped);
                sb.AppendLine("Cartelle esplorate         : " + data.DirectoriesVisited);
                sb.AppendLine("Errori di lettura          : " + data.ScanErrors);
                if (data.FilesSelected > 0)
                    sb.AppendLine("File selezionati per la pulizia: " + data.FilesSelected);
                if (!string.IsNullOrEmpty(data.MinAgeText))
                    sb.AppendLine("Soglia temporale           : " + data.MinAgeText);
                sb.AppendLine("Spazio recuperabile stimato: " + ByteFormatter.Format(data.EstimatedBytes));
                sb.AppendLine("Stima in byte             : " + data.EstimatedBytes);
                if (data.ScanCancelled) sb.AppendLine("Esito: analisi annullata (risultati parziali).");
                if (data.ScanErrorMessages.Count > 0)
                {
                    sb.AppendLine("Dettaglio errori:");
                    for (int i = 0; i < data.ScanErrorMessages.Count; i++)
                        sb.AppendLine("  - " + data.ScanErrorMessages[i]);
                }
            }
            sb.AppendLine();

            sb.AppendLine("--- Pulizia ---");
            if (!data.CleanAvailable)
            {
                sb.AppendLine("Nessuna pulizia eseguita.");
            }
            else
            {
                sb.AppendLine("File eliminati             : " + data.DeletedCount);
                sb.AppendLine("Byte effettivamente rimossi: " + data.DeletedBytes);
                sb.AppendLine("Spazio effettivamente liberato: " + ByteFormatter.Format(data.DeletedBytes));
                sb.AppendLine("File saltati              : " + data.CleanSkipped);
                sb.AppendLine("Errori                    : " + data.CleanErrors);
                sb.AppendLine("Spazio stimato non recuperato: " + ByteFormatter.Format(data.EstimatedNotRecovered));
                if (data.EmptyCleanAvailable)
                {
                    // Sezione emessa solo se la fase opzionale è stata davvero eseguita.
                    sb.AppendLine("Cartelle vuote rimosse       : " + data.EmptyDirectoriesRemoved);
                    sb.AppendLine("Cartelle vuote saltate       : " + data.EmptyDirectoriesSkipped);
                    sb.AppendLine("Errori (cartelle vuote)      : " + data.EmptyDirectoriesErrors);
                }
                if (data.CleanCancelled) sb.AppendLine("Esito: pulizia annullata.");
                if (data.EmptyCleanAvailable && data.EmptyCleanCancelled)
                    sb.AppendLine("Esito: rimozione cartelle vuote annullata.");
            }
            sb.AppendLine();

            sb.AppendLine("Tempo totale: " + data.DurationText);
            sb.AppendLine();

            sb.AppendLine("--- Note ---");
            for (int i = 0; i < data.Notes.Count; i++) sb.AppendLine("- " + data.Notes[i]);
            sb.AppendLine();
            sb.AppendLine("Questo file è stato creato solo su richiesta e resta su questo computer.");
            sb.AppendLine("CleanSlate non effettua alcuna connessione di rete.");
            return sb.ToString();
        }

        public static string BuildJson(ReportData data)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.Append("{\n");
            AppendString(sb, 1, "prodotto", data.ProductName, true);
            AppendString(sb, 1, "versione", data.Version, true);
            AppendString(sb, 1, "generato", data.GeneratedLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), true);
            AppendString(sb, 1, "operazione", data.Operation, true);
            AppendString(sb, 1, "categoria", data.Category, true);
            AppendString(sb, 1, "perimetro", "file temporanei dell'utente corrente", true);

            sb.Append("  \"analisi\": ");
            if (!data.ScanAvailable)
            {
                sb.Append("null,\n");
            }
            else
            {
                sb.Append("{\n");
                AppendNumber(sb, 2, "fileAnalizzati", data.FilesScanned, true);
                AppendNumber(sb, 2, "fileProposti", data.FilesEligible, true);
                AppendNumber(sb, 2, "fileEsclusiDallaSoglia", data.FilesExcludedByAge, true);
                AppendNumber(sb, 2, "fileScartati", data.FilesSkipped, true);
                AppendNumber(sb, 2, "cartelleNonPercorse", data.DirectoriesSkipped, true);
                AppendNumber(sb, 2, "cartelleEsplorate", data.DirectoriesVisited, true);
                AppendNumber(sb, 2, "erroriLettura", data.ScanErrors, true);
                if (data.FilesSelected > 0)
                    AppendNumber(sb, 2, "fileSelezionatiPerLaPulizia", data.FilesSelected, true);
                if (!string.IsNullOrEmpty(data.MinAgeText))
                    AppendString(sb, 2, "sogliaTemporale", data.MinAgeText, true);
                AppendNumber(sb, 2, "spazioRecuperabileStimatoByte", data.EstimatedBytes, true);
                AppendBool(sb, 2, "annullata", data.ScanCancelled, true);
                sb.Append("    \"messaggiErrore\": ");
                AppendStringArray(sb, data.ScanErrorMessages, 2);
                sb.Append("\n  },\n");
            }

            sb.Append("  \"pulizia\": ");
            if (!data.CleanAvailable)
            {
                sb.Append("null,\n");
            }
            else
            {
                sb.Append("{\n");
                AppendNumber(sb, 2, "fileEliminati", data.DeletedCount, true);
                AppendNumber(sb, 2, "byteRimossi", data.DeletedBytes, true);
                AppendNumber(sb, 2, "fileSaltati", data.CleanSkipped, true);
                AppendNumber(sb, 2, "errori", data.CleanErrors, true);
                AppendNumber(sb, 2, "spazioStimatoNonRecuperatoByte", data.EstimatedNotRecovered, true);
                if (data.EmptyCleanAvailable)
                {
                    // Chiavi emesse solo se la fase opzionale è stata davvero eseguita.
                    AppendNumber(sb, 2, "cartelleVuoteRimosse", data.EmptyDirectoriesRemoved, true);
                    AppendNumber(sb, 2, "cartelleVuoteSaltate", data.EmptyDirectoriesSkipped, true);
                    AppendNumber(sb, 2, "cartelleVuoteErrori", data.EmptyDirectoriesErrors, true);
                    AppendBool(sb, 2, "cartelleVuoteAnnullata", data.EmptyCleanCancelled, true);
                }
                AppendBool(sb, 2, "annullata", data.CleanCancelled, false);
                sb.Append("  },\n");
            }

            AppendString(sb, 1, "tempoTotale", data.DurationText, true);
            sb.Append("  \"note\": ");
            AppendStringArray(sb, data.Notes, 1);
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, int indent, string key, string value, bool comma)
        {
            sb.Append(' ', indent * 2);
            sb.Append('"');
            sb.Append(Escape(key));
            sb.Append("\": \"");
            sb.Append(Escape(value ?? string.Empty));
            sb.Append('"');
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        private static void AppendNumber(StringBuilder sb, int indent, string key, long value, bool comma)
        {
            sb.Append(' ', indent * 2);
            sb.Append('"');
            sb.Append(Escape(key));
            sb.Append("\": ");
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        private static void AppendBool(StringBuilder sb, int indent, string key, bool value, bool comma)
        {
            sb.Append(' ', indent * 2);
            sb.Append('"');
            sb.Append(Escape(key));
            sb.Append("\": ");
            sb.Append(value ? "true" : "false");
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        private static void AppendStringArray(StringBuilder sb, System.Collections.Generic.List<string> values, int indent)
        {
            if (values == null || values.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append("[\n");
            for (int i = 0; i < values.Count; i++)
            {
                sb.Append(' ', (indent + 1) * 2);
                sb.Append('"');
                sb.Append(Escape(values[i] ?? string.Empty));
                sb.Append('"');
                if (i < values.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(' ', indent * 2);
            sb.Append(']');
        }

        private static string Escape(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
