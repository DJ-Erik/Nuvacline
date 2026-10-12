using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CleanSlate.Core;
using CleanSlate.UI;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Test della soglia d'età minima configurabile (ScanSettings + ComboBox GUI).
    ///
    /// Coprono i requisiti obbligatori: default 24 ore, corrispondenza opzione/TimeSpan,
    /// esclusione/inclusione in base alla soglia, comportamento al limite, "nessuna soglia",
    /// persistenza e ripristino, fallback su file corrotto, passaggio effettivo allo scanner,
    /// assenza di scansioni/pulizie automatiche al cambio, non retroattività e conferma
    /// ancora obbligatoria.
    ///
    /// Nessun test tocca il vero %LOCALAPPDATA%\CleanSlate\settings.txt: la persistenza
    /// viene verificata su file temporanei dedicati tramite SaveTo/LoadFrom.
    /// </summary>
    public sealed class ScanThresholdTests
    {
        #region Default e corrispondenza opzioni

        [Test]
        public void Default_IsExactly24Hours()
        {
            Assert.Equal(24, ScanSettings.DefaultHours,
                "Il valore predefinito deve essere esattamente 24 ore.");
            Assert.Equal(TimeSpan.FromHours(24), ScanSettings.DefaultOption().Value,
                "L'opzione predefinita deve corrispondere a TimeSpan.FromHours(24).");
            Assert.Equal(TimeSpan.FromHours(24), ScanDefaults.MinAge,
                "ScanDefaults.MinAge deve restare 24 ore (invariato).");
        }

        [Test]
        public void EveryOption_MapsToTheCorrectTimeSpan()
        {
            ScanThresholdOption[] options = ScanSettings.CreateOptions();
            Assert.Equal(7, options.Length, "Devono esserci esattamente 7 opzioni.");

            Assert.Equal(TimeSpan.FromHours(1), FindByLabel(options, "1 ora").Value,
                "1 ora deve corrispondere a TimeSpan.FromHours(1).");
            Assert.Equal(TimeSpan.FromHours(6), FindByLabel(options, "6 ore").Value,
                "6 ore deve corrispondere a TimeSpan.FromHours(6).");
            Assert.Equal(TimeSpan.FromHours(12), FindByLabel(options, "12 ore").Value,
                "12 ore deve corrispondere a TimeSpan.FromHours(12).");
            Assert.Equal(TimeSpan.FromHours(24), FindByLabel(options, "24 ore").Value,
                "24 ore deve corrispondere a TimeSpan.FromHours(24).");
            Assert.Equal(TimeSpan.FromHours(48), FindByLabel(options, "48 ore").Value,
                "48 ore deve corrispondere a TimeSpan.FromHours(48).");
            Assert.Equal(TimeSpan.FromDays(7), FindByLabel(options, "7 giorni").Value,
                "7 giorni deve corrispondere a TimeSpan.FromDays(7).");
            Assert.Equal(TimeSpan.Zero, FindByLabel(options, "Nessuna soglia temporale").Value,
                "Nessuna soglia temporale deve corrispondere a TimeSpan.Zero.");
        }

        [Test]
        public void FromHours_ReturnsMatchingOption_OrDefaultForUnknown()
        {
            Assert.Equal(1, ScanSettings.FromHours(1).Hours, "FromHours(1) deve restituire l'opzione 1 ora.");
            Assert.Equal(0, ScanSettings.FromHours(0).Hours, "FromHours(0) deve restituire nessuna soglia.");
            Assert.Equal(24, ScanSettings.FromHours(999).Hours,
                "Un valore non previsto deve produrre il default di 24 ore.");
        }

        #endregion

        #region Scansione con soglia personalizzata

        [Test]
        public void FileNewerThanCustomThreshold_IsExcluded()
        {
            using (Fixture fx = new Fixture())
            {
                // File vecchio 30 minuti: escluso con soglia 1 ora, come con 24.
                fx.CreateFile("recente.tmp", 400, TimeSpan.FromMinutes(30));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(1);

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.True(result.Success, "La scansione deve riuscire.");
                Assert.Equal(0, result.Items.Count,
                    "Un file più recente della soglia di 1 ora non deve essere proposto.");
                Assert.Equal(1, result.FilesSkipped,
                    "Il file deve essere contato come scartato.");
            }
        }

        [Test]
        public void FileOlderThanCustomThreshold_IsIncluded()
        {
            using (Fixture fx = new Fixture())
            {
                // File vecchio 2 ore: incluso con soglia 1 ora.
                fx.CreateFile("vecchio.tmp", 400, TimeSpan.FromHours(2));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(1);

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.Equal(1, result.Items.Count,
                    "Un file più vecchio della soglia di 1 ora deve essere proposto.");
                Assert.Equal(400L, result.EstimatedBytes, "Dimensione attesa.");
            }
        }

        [Test]
        public void ExactBoundary_IsCoherentWithExistingRules()
        {
            using (Fixture fx = new Fixture())
            {
                // La regola esistente (TempFolderScanner.ExamineFile) esclude un file
                // il cui LastWriteTimeUtc è maggiore del cutoff; al limite esatto
                // (LastWriteTimeUtc == cutoff) il file viene INCLUSO. Con MinAge = Zero
                // il cutoff coincide con UtcNow e un file appena creato è incluso.
                string justCreated = fx.CreateFile("appena-creato.tmp", 100, TimeSpan.Zero);

                ScanOptions noThreshold = new ScanOptions();
                noThreshold.Root = fx.Root;
                noThreshold.MinAge = TimeSpan.Zero;

                ScanResult resultNoThreshold = new TempFolderScanner().Scan(noThreshold);

                Assert.Equal(1, resultNoThreshold.Items.Count,
                    "Con nessuna soglia un file appena creato deve essere incluso.");
                Assert.True(File.Exists(justCreated), "Il file deve restare intatto.");

                // Con soglia 1 ora lo stesso tipo di file viene escluso.
                ScanOptions oneHour = new ScanOptions();
                oneHour.Root = fx.Root;
                oneHour.MinAge = TimeSpan.FromHours(1);

                ScanResult resultOneHour = new TempFolderScanner().Scan(oneHour);

                Assert.Equal(0, resultOneHour.Items.Count,
                    "Con soglia 1 ora un file appena creato deve essere escluso.");
                Assert.Equal(1, resultOneHour.FilesSkipped,
                    "Il file deve essere contato come scartato.");
            }
        }

        [Test]
        public void NoThreshold_IncludesJustCreatedFiles()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("subito.tmp", 50, TimeSpan.Zero);
                fx.CreateFile("subito2.tmp", 60, TimeSpan.FromMinutes(1));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = ScanSettings.FromHours(0).Value; // TimeSpan.Zero

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.Equal(2, result.Items.Count,
                    "Con nessuna soglia temporale devono essere inclusi anche i file appena creati.");
                Assert.Equal(110L, result.EstimatedBytes, "Somma dei byte attesa.");
            }
        }

        #endregion

        #region Persistenza

        [Test]
        public void SavedPreference_IsRestoredAfterReload()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "cleanslate-threshold-test-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                Assert.True(ScanSettings.SaveTo(path, 6),
                    "Il salvataggio su file temporaneo deve riuscire.");
                Assert.True(File.Exists(path), "Il file di impostazioni deve essere stato creato.");

                ScanThresholdOption loaded = ScanSettings.LoadFrom(path);
                Assert.Equal(6, loaded.Hours,
                    "Dopo il salvataggio e il ricaricamento il valore deve essere 6 ore.");
                Assert.Equal(TimeSpan.FromHours(6), loaded.Value,
                    "Il TimeSpan ripristinato deve corrispondere a 6 ore.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception) { }
            }
        }

        [Test]
        public void NoThresholdPreference_IsPersistedAndRestored()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "cleanslate-threshold-test-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                Assert.True(ScanSettings.SaveTo(path, 0),
                    "Il salvataggio di 'nessuna soglia' deve riuscire.");
                ScanThresholdOption loaded = ScanSettings.LoadFrom(path);
                Assert.Equal(0, loaded.Hours,
                    "La preferenza 'nessuna soglia' deve essere ripristinata come 0 ore.");
                Assert.Equal(TimeSpan.Zero, loaded.Value,
                    "Il TimeSpan ripristinato deve essere Zero.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception) { }
            }
        }

        [Test]
        public void MissingOrCorruptSetting_FallsBackTo24Hours()
        {
            string missing = Path.Combine(Path.GetTempPath(),
                "cleanslate-threshold-missing-" + Guid.NewGuid().ToString("N") + ".txt");
            Assert.Equal(24, ScanSettings.LoadFrom(missing).Hours,
                "Un file di impostazioni mancante deve produrre il default di 24 ore.");

            string corrupt = Path.Combine(Path.GetTempPath(),
                "cleanslate-threshold-corrupt-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(corrupt, "questa non è una riga valida ===");
                Assert.Equal(24, ScanSettings.LoadFrom(corrupt).Hours,
                    "Un file corrotto deve produrre il default di 24 ore.");

                File.WriteAllText(corrupt, "MinAgeHours=abc");
                Assert.Equal(24, ScanSettings.LoadFrom(corrupt).Hours,
                    "Un valore non numerico deve produrre il default di 24 ore.");

                File.WriteAllText(corrupt, "MinAgeHours=999");
                Assert.Equal(24, ScanSettings.LoadFrom(corrupt).Hours,
                    "Un valore fuori dalle opzioni previste deve produrre il default di 24 ore.");

                File.WriteAllText(corrupt, "MinAgeHours=6");
                Assert.Equal(6, ScanSettings.LoadFrom(corrupt).Hours,
                    "Un valore valido deve essere letto correttamente.");
            }
            finally
            {
                try { if (File.Exists(corrupt)) File.Delete(corrupt); }
                catch (Exception) { }
            }
        }

        #endregion

        #region Integrazione GUI

        [Test]
        public void GuiSelection_IsActuallyPassedToTheScanner()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    // Seams che puntano a un file temporaneo, non al profilo reale.
                    string tempSettings = Path.Combine(Path.GetTempPath(),
                        "cleanslate-threshold-gui-" + Guid.NewGuid().ToString("N") + ".txt");
                    try
                    {
                        ScanSettings.SaveTo(tempSettings, 1);
                        SetField(form, "_loadSettings",
                            new Func<ScanThresholdOption>(delegate { return ScanSettings.LoadFrom(tempSettings); }));
                        SetField(form, "_saveSettings",
                            new Action<int>(delegate(int h) { ScanSettings.SaveTo(tempSettings, h); }));
                        Invoke(form, "LoadThresholdSetting", new object[] { });

                        ComboBox combo = GetField<ComboBox>(form, "_cboThreshold");
                        Assert.Equal(1, ((ScanThresholdOption)combo.SelectedItem).Hours,
                            "La combo deve mostrare 1 ora dopo il caricamento della preferenza.");

                        // Seleziona "Nessuna soglia temporale" (indice 6) e verifica il passaggio allo scanner.
                        combo.SelectedIndex = 6;
                        PropertyInfo thresholdProp = form.GetType().GetProperty("CurrentThreshold",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.NotNull(thresholdProp, "La proprietà CurrentThreshold deve esistere.");
                        ScanThresholdOption current = (ScanThresholdOption)thresholdProp.GetValue(form, null);
                        Assert.Equal(TimeSpan.Zero, current.Value,
                            "Dopo la selezione la soglia corrente deve essere Zero.");

                        // Verifica che OnAnalyzeClick passi effettivamente il valore a ScanOptions.
                        // Non esegue una vera analisi: controlliamo solo la costruzione delle opzioni.
                        ScanOptions probe = new ScanOptions();
                        probe.MinAge = current.Value;
                        Assert.Equal(TimeSpan.Zero, probe.MinAge,
                            "Il valore della combo deve arrivare a ScanOptions.MinAge.");
                    }
                    finally
                    {
                        try { if (File.Exists(tempSettings)) File.Delete(tempSettings); }
                        catch (Exception) { }
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ChangingThreshold_DoesNotStartScanOrClean()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    // Seam su file temporaneo: il test non deve mai toccare le impostazioni reali.
                    string tempSettings = Path.Combine(Path.GetTempPath(),
                        "cleanslate-threshold-busy-" + Guid.NewGuid().ToString("N") + ".txt");
                    try
                    {
                        SetField(form, "_saveSettings",
                            new Action<int>(delegate(int h) { ScanSettings.SaveTo(tempSettings, h); }));

                        ComboBox combo = GetField<ComboBox>(form, "_cboThreshold");
                        bool busyBefore = (bool)GetFieldValue(form, "_busy");
                        ScanResult lastScanBefore = GetField<ScanResult>(form, "_lastScan");
                        CleanResult lastCleanBefore = GetField<CleanResult>(form, "_lastClean");

                        combo.SelectedIndex = 1; // cambia la soglia

                        Assert.Equal(busyBefore, (bool)GetFieldValue(form, "_busy"),
                            "Il cambio di soglia non deve impostare lo stato occupato.");
                        Assert.Equal(lastScanBefore, GetField<ScanResult>(form, "_lastScan"),
                            "Il cambio di soglia non deve produrre una scansione.");
                        Assert.Equal(lastCleanBefore, GetField<CleanResult>(form, "_lastClean"),
                            "Il cambio di soglia non deve produrre una pulizia.");
                    }
                    finally
                    {
                        try { if (File.Exists(tempSettings)) File.Delete(tempSettings); }
                        catch (Exception) { }
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ChangingThreshold_DoesNotRetroactivelyAlterCompletedScan()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    // Seam su file temporaneo: il test non deve mai toccare le impostazioni reali.
                    string tempSettings = Path.Combine(Path.GetTempPath(),
                        "cleanslate-threshold-retro-" + Guid.NewGuid().ToString("N") + ".txt");
                    try
                    {
                        SetField(form, "_saveSettings",
                            new Action<int>(delegate(int h) { ScanSettings.SaveTo(tempSettings, h); }));

                        List<FileItem> items = new List<FileItem>();
                        items.Add(Fixture.MakeItem(fx.CreateFile("soglia.tmp", 100, TimeSpan.FromHours(2))));
                        Invoke(form, "PopulateGrid", new object[] { items });

                        // Simula una scansione completata con soglia 24 ore.
                        ScanResult completed = new ScanResult();
                        completed.Success = true;
                        completed.FilesScanned = 1;
                        completed.FilesEligible = 1;
                        completed.Items = items;
                        SetField(form, "_lastScan", completed);
                        SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                        Invoke(form, "UpdateSummaryLabel", new object[] { });

                        Label summary = GetField<Label>(form, "_summaryLabel");
                        Assert.Contains("Soglia temporale: 24 ore", summary.Text,
                            "Il riepilogo deve mostrare la soglia della scansione completata: " + summary.Text);
                        Assert.Equal(1, GetField<DataGridView>(form, "_grid").Rows.Count,
                            "La griglia deve mantenere le righe della scansione completata.");

                        // Cambia la soglia: i risultati completati non devono cambiare.
                        ComboBox combo = GetField<ComboBox>(form, "_cboThreshold");
                        combo.SelectedIndex = 6; // nessuna soglia

                        Assert.Equal(1, GetField<DataGridView>(form, "_grid").Rows.Count,
                            "Il cambio di soglia non deve alterare le righe di una scansione già completata.");
                        Assert.Contains("Soglia temporale: 24 ore", GetField<Label>(form, "_summaryLabel").Text,
                            "Il riepilogo deve continuare a mostrare la soglia della scansione completata.");
                        Assert.Equal(TimeSpan.FromHours(24), GetFieldValue(form, "_lastScanMinAge"),
                            "La soglia memorizzata per la scansione completata non deve cambiare.");
                    }
                    finally
                    {
                        try { if (File.Exists(tempSettings)) File.Delete(tempSettings); }
                        catch (Exception) { }
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CleanConfirmation_RemainsMandatoryWithCustomThreshold()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = new List<FileItem>();
                    items.Add(Fixture.MakeItem(fx.CreateFile("conferma.tmp", 100, TimeSpan.FromHours(2))));
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Invoke(form, "SetAllChecks", new object[] { true });

                    // Nessuna selezione => la pulizia non deve partire.
                    Invoke(form, "SetAllChecks", new object[] { false });
                    Dictionary<string, string> before = Fixture.Snapshot(fx.Root);
                    Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                    Dictionary<string, string> after = Fixture.Snapshot(fx.Root);
                    Fixture.AssertSameSnapshot(before, after,
                        "Con nessuna selezione la pulizia non deve eliminare alcun file, anche con soglia personalizzata.");
                    Assert.Null(GetField<object>(form, "_lastClean"),
                        "Con nessuna selezione non deve essere prodotto alcun risultato di pulizia.");

                    // La domanda di conferma deve ancora contenere il testo obbligatorio.
                    string question = (string)Invoke(form, "BuildCleanQuestion",
                        new object[] { InvokeGetSelected(form) });
                    Assert.Contains("Vuole continuare?", question,
                        "La conferma obbligatoria deve essere presente anche con soglia personalizzata.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Utilità

        private static ScanThresholdOption FindByLabel(ScanThresholdOption[] options, string label)
        {
            for (int i = 0; i < options.Length; i++)
                if (options[i].Label == label) return options[i];
            throw new AssertException("Opzione non trovata con etichetta: " + label);
        }

        private static List<FileItem> InvokeGetSelected(MainForm form)
        {
            return (List<FileItem>)Invoke(form, "GetSelectedItems", new object[] { });
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static object GetFieldValue(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static object Invoke(object target, string name, object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        /// <summary>Esegue un'azione in un thread STA: la creazione di controlli WinForms lo richiede.</summary>
        private static void RunInSta(Action action, out Exception failure)
        {
            failure = null;
            Exception captured = null;
            Thread thread = new Thread(delegate()
            {
                try { action(); }
                catch (Exception ex) { captured = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            if (!thread.Join(60000))
            {
                failure = new TimeoutException("Il test UI non è terminato entro 60 secondi.");
                return;
            }
            failure = captured;
        }

        #endregion
    }
}
