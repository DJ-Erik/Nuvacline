using System;
using System.Globalization;
using System.IO;
using System.Security;

namespace CleanSlate.Core
{
    /// <summary>Una soglia d'età selezionabile dall'utente.</summary>
    public sealed class ScanThresholdOption
    {
        public ScanThresholdOption(string label, int hours)
        {
            Label = label;
            Hours = hours;
        }

        /// <summary>Testo visualizzato nell'interfaccia.</summary>
        public string Label { get; private set; }

        /// <summary>Valore in ore; 0 significa nessuna soglia temporale.</summary>
        public int Hours { get; private set; }

        /// <summary>Equivalente TimeSpan; Zero per "nessuna soglia".</summary>
        public TimeSpan Value
        {
            get { return Hours <= 0 ? TimeSpan.Zero : TimeSpan.FromHours(Hours); }
        }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Impostazione locale della soglia d'età minima dei file proposti dalla scansione.
    ///
    /// Persistenza: un singolo file di testo in
    /// %LOCALAPPDATA%\CleanSlate\settings.txt con la riga "MinAgeHours=&lt;ore&gt;".
    /// Nessuna dipendenza esterna, nessuna rete, nessun registro di Windows.
    ///
    /// Tutte le letture falliscono in modo sicuro sul valore predefinito (24 ore):
    /// file mancante, corrotto, non leggibile o con valore non previsto.
    /// </summary>
    public static class ScanSettings
    {
        public const int DefaultHours = 24;
        private const string KeyMinAgeHours = "MinAgeHours";

        /// <summary>Opzioni disponibili, in ordine di visualizzazione.</summary>
        public static ScanThresholdOption[] CreateOptions()
        {
            return new ScanThresholdOption[]
            {
                new ScanThresholdOption("1 ora", 1),
                new ScanThresholdOption("6 ore", 6),
                new ScanThresholdOption("12 ore", 12),
                new ScanThresholdOption("24 ore", 24),
                new ScanThresholdOption("48 ore", 48),
                new ScanThresholdOption("7 giorni", 168),
                new ScanThresholdOption("Nessuna soglia temporale", 0)
            };
        }

        /// <summary>Opzione predefinita: 24 ore.</summary>
        public static ScanThresholdOption DefaultOption()
        {
            return new ScanThresholdOption("24 ore", DefaultHours);
        }

        /// <summary>
        /// Ricava l'opzione corrispondente a un numero di ore.
        /// Valori non previsti => predefinita (24 ore).
        /// </summary>
        public static ScanThresholdOption FromHours(int hours)
        {
            ScanThresholdOption[] options = CreateOptions();
            for (int i = 0; i < options.Length; i++)
                if (options[i].Hours == hours) return options[i];
            return DefaultOption();
        }

        public static string SettingsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CleanSlate");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, "settings.txt"); }
        }

        /// <summary>Carica la preferenza dal percorso predefinito.</summary>
        public static ScanThresholdOption Load()
        {
            return LoadFrom(SettingsPath);
        }

        /// <summary>
        /// Carica la preferenza dal percorso indicato. Qualsiasi problema
        /// (file assente, riga malformata, valore non valido, errori di I/O)
        /// produce il valore predefinito di 24 ore.
        /// </summary>
        public static ScanThresholdOption LoadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return DefaultOption();

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (!string.Equals(key, KeyMinAgeHours, StringComparison.OrdinalIgnoreCase)) continue;

                    int hours;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hours)
                        && IsAllowedHours(hours))
                    {
                        return FromHours(hours);
                    }

                    // Valore presente ma non valido: fallback immediato a 24 ore.
                    return DefaultOption();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }

            return DefaultOption();
        }

        /// <summary>Salva la preferenza nel percorso predefinito.</summary>
        public static bool Save(int hours)
        {
            return SaveTo(SettingsPath, hours);
        }

        /// <summary>
        /// Salva la preferenza nel percorso indicato. Ore non previste
        /// vengono normalizzate al predefinito prima della scrittura.
        /// Restituisce false se la scrittura non riesce (l'app continua comunque).
        /// </summary>
        public static bool SaveTo(string path, int hours)
        {
            if (!IsAllowedHours(hours)) hours = DefaultHours;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path,
                    KeyMinAgeHours + "=" + hours.ToString(CultureInfo.InvariantCulture));
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        private static bool IsAllowedHours(int hours)
        {
            ScanThresholdOption[] options = CreateOptions();
            for (int i = 0; i < options.Length; i++)
                if (options[i].Hours == hours) return true;
            return false;
        }
    }
}
