// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
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
    /// Impostazioni locali di CleanSlate: soglia d'età minima dei file proposti dalla
    /// scansione, conservazione dei report generati e rimozione delle cartelle vuote.
    ///
    /// Persistenza: un singolo file di testo in
    /// %LOCALAPPDATA%\CleanSlate\settings.txt con le righe "MinAgeHours=&lt;ore&gt;",
    /// "KeepReports=&lt;0|1&gt;" e "RemoveEmptyFolders=&lt;0|1&gt;". Le chiavi sono
    /// indipendenti: la scrittura di una preserva tutte le altre.
    /// Nessuna dipendenza esterna, nessuna rete, nessun registro di Windows.
    ///
    /// Tutte le letture falliscono in modo sicuro sui valori predefiniti (24 ore,
    /// conservazione attiva e rimozione cartelle vuote disattivata): file mancante,
    /// corrotto, non leggibile o con valore non previsto.
    /// </summary>
    public static class ScanSettings
    {
        public const int DefaultHours = 24;

        /// <summary>Conservazione dei report attiva finché l'utente non la disattiva.</summary>
        public const bool DefaultKeepReports = true;

        /// <summary>
        /// Rimozione delle cartelle vuote disattivata finché l'utente non la attiva:
        /// il comportamento predefinito resta quello della 0.3.6 (solo file).
        /// </summary>
        public const bool DefaultRemoveEmptyFolders = false;

        /// <summary>
        /// Tema predefinito: automatico, cioè segue le impostazioni di Windows.
        /// </summary>
        public const ThemeChoice DefaultThemeChoice = ThemeChoice.Automatic;

        private const string KeyMinAgeHours = "MinAgeHours";
        private const string KeyKeepReports = "KeepReports";
        private const string KeyRemoveEmptyFolders = "RemoveEmptyFolders";
        private const string KeyTheme = "Theme";

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
        /// Le altre chiavi già presenti nel file (es. KeepReports) vengono preservate.
        /// Restituisce false se la scrittura non riesce (l'app continua comunque).
        /// </summary>
        public static bool SaveTo(string path, int hours)
        {
            if (!IsAllowedHours(hours)) hours = DefaultHours;
            return WriteSetting(path, KeyMinAgeHours,
                hours.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Carica la conservazione dei report dal percorso predefinito.</summary>
        public static bool LoadKeepReports()
        {
            return LoadKeepReportsFrom(SettingsPath);
        }

        /// <summary>
        /// Carica la conservazione dei report dal percorso indicato. Qualsiasi problema
        /// (file assente, riga malformata, valore non valido, errori di I/O) produce il
        /// valore predefinito: conservazione attiva.
        /// </summary>
        public static bool LoadKeepReportsFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return DefaultKeepReports;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (!string.Equals(key, KeyKeepReports, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                        return false;

                    // Valore presente ma non valido: fallback sulla conservazione attiva.
                    return true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }

            return DefaultKeepReports;
        }

        /// <summary>Salva la conservazione dei report nel percorso predefinito.</summary>
        public static bool SaveKeepReports(bool keepReports)
        {
            return SaveKeepReportsTo(SettingsPath, keepReports);
        }

        /// <summary>
        /// Salva la conservazione dei report nel percorso indicato, preservando le
        /// altre chiavi già presenti nel file. Restituisce false se la scrittura
        /// non riesce.
        /// </summary>
        public static bool SaveKeepReportsTo(string path, bool keepReports)
        {
            return WriteSetting(path, KeyKeepReports, keepReports ? "1" : "0");
        }

        /// <summary>Carica la rimozione delle cartelle vuote dal percorso predefinito.</summary>
        public static bool LoadRemoveEmptyFolders()
        {
            return LoadRemoveEmptyFoldersFrom(SettingsPath);
        }

        /// <summary>
        /// Carica la rimozione delle cartelle vuote dal percorso indicato. Qualsiasi
        /// problema (file assente, riga malformata, valore non valido, errori di I/O)
        /// produce il valore predefinito: rimozione disattivata. A differenza della
        /// conservazione dei report, il fallback è spento: l'azione distruttiva nuova
        /// deve essere scelta esplicitamente dall'utente.
        /// </summary>
        public static bool LoadRemoveEmptyFoldersFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return DefaultRemoveEmptyFolders;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (!string.Equals(key, KeyRemoveEmptyFolders, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                        return true;

                    // Valore presente ma non valido: fallback sulla disattivazione.
                    return false;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }

            return DefaultRemoveEmptyFolders;
        }

        /// <summary>Salva la rimozione delle cartelle vuote nel percorso predefinito.</summary>
        public static bool SaveRemoveEmptyFolders(bool removeEmptyFolders)
        {
            return SaveRemoveEmptyFoldersTo(SettingsPath, removeEmptyFolders);
        }

        /// <summary>Carica la preferenza del tema dal percorso predefinito.</summary>
        public static ThemeChoice LoadThemeChoice()
        {
            return LoadThemeChoiceFrom(SettingsPath);
        }

        /// <summary>
        /// Carica la preferenza del tema dal percorso indicato. Qualsiasi problema
        /// (file assente, riga malformata, valore non valido, errori di I/O)
        /// produce il valore predefinito: tema automatico.
        /// </summary>
        public static ThemeChoice LoadThemeChoiceFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return DefaultThemeChoice;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (!string.Equals(key, KeyTheme, StringComparison.OrdinalIgnoreCase)) continue;

                    return ThemeChoiceFromText(value);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }

            return DefaultThemeChoice;
        }

        /// <summary>Salva la preferenza del tema nel percorso predefinito.</summary>
        public static bool SaveThemeChoice(ThemeChoice choice)
        {
            return SaveThemeChoiceTo(SettingsPath, choice);
        }

        /// <summary>
        /// Salva la preferenza del tema nel percorso indicato, preservando le
        /// altre chiavi già presenti nel file. Valori fuori dall'enum vengono
        /// normalizzati su automatico. Restituisce false se la scrittura non riesce.
        /// </summary>
        public static bool SaveThemeChoiceTo(string path, ThemeChoice choice)
        {
            return WriteSetting(path, KeyTheme, ThemeChoiceToText(choice));
        }

        /// <summary>
        /// Converte il testo persistito in una preferenza: "light" chiaro,
        /// "dark" scuro, qualsiasi altro valore (compreso "auto" e il vuoto)
        /// produce il tema automatico.
        /// </summary>
        public static ThemeChoice ThemeChoiceFromText(string value)
        {
            if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
                return ThemeChoice.Light;
            if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
                return ThemeChoice.Dark;
            return ThemeChoice.Automatic;
        }

        /// <summary>
        /// Converte la preferenza nel testo persistito. Valori fuori
        /// dall'enum producono "auto" (fail-safe sul tema di sistema).
        /// </summary>
        public static string ThemeChoiceToText(ThemeChoice choice)
        {
            if (choice == ThemeChoice.Light) return "light";
            if (choice == ThemeChoice.Dark) return "dark";
            return "auto";
        }

        /// <summary>
        /// Salva la rimozione delle cartelle vuote nel percorso indicato, preservando
        /// le altre chiavi già presenti nel file. Restituisce false se la scrittura
        /// non riesce.
        /// </summary>
        public static bool SaveRemoveEmptyFoldersTo(string path, bool removeEmptyFolders)
        {
            return WriteSetting(path, KeyRemoveEmptyFolders, removeEmptyFolders ? "1" : "0");
        }

        /// <summary>
        /// Sostituisce (o aggiunge) la prima occorrenza della chiave indicata,
        /// mantenendo intatte tutte le altre righe del file. La mancanza del file
        /// produce un file contente la sola chiave scritta.
        /// </summary>
        private static bool WriteSetting(string path, string key, string value)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                List<string> lines = new List<string>();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string[] existing = File.ReadAllLines(path);
                    for (int i = 0; i < existing.Length; i++) lines.Add(existing[i]);
                }

                bool replaced = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;

                    string existingKey = lines[i].Substring(0, separator).Trim();
                    if (!string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase)) continue;

                    lines[i] = key + "=" + value;
                    replaced = true;
                    break;
                }
                if (!replaced) lines.Add(key + "=" + value);

                File.WriteAllLines(path, lines.ToArray());
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            catch (ArgumentException) { return false; }
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
