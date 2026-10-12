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
    /// Test del riepilogo visivo di scansione: conteggi di analizzati, proposti,
    /// esclusi dalla soglia e selezionati per la pulizia.
    ///
    /// Coprono i requisiti obbligatori: coerenza dei conteggi (6.000/300/5.700),
    /// conteggio iniziale dei selezionati, aggiornamento dopo deselezione e
    /// selezione massiva, nessuna soglia, nessuna esclusione, distinzione fra
    /// esclusioni temporali ed errori, nuova scansione senza conteggi obsoleti,
    /// scansione annullata e coerenza del report.
    ///
    /// Nessun test dipende dal contenuto reale del computer: i dati sono creati
    /// nel fixture con età controllata.
    /// </summary>
    public sealed class ScanSummaryTests
    {
        #region Conteggi del motore

        [Test]
        public void Engine_6000Scanned_300Proposed_5700ExcludedByAge()
        {
            using (Fixture fx = new Fixture())
            {
                // 300 file vecchi di 48 ore (proposti) + 5.700 recenti (esclusi dalla soglia di 24h).
                for (int i = 0; i < 300; i++)
                    fx.CreateFile("vecchio-" + i + ".tmp", 1, TimeSpan.FromHours(48));
                for (int i = 0; i < 5700; i++)
                    fx.CreateFile("recente-" + i + ".tmp", 1, TimeSpan.FromHours(1));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(24);

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.True(result.Success, "La scansione deve riuscire.");
                Assert.Equal(6000, result.FilesScanned,
                    "Tutti i 6.000 file devono essere analizzati.");
                Assert.Equal(300, result.FilesEligible,
                    "Solo i 300 file vecchi devono essere proposti.");
                Assert.Equal(5700, result.FilesExcludedByAge,
                    "I 5.700 file recenti devono essere esclusi dalla soglia.");
                Assert.Equal(5700, result.FilesSkipped,
                    "Con solo esclusioni per età, FilesSkipped deve coincidere con FilesExcludedByAge.");
                Assert.Equal(300, result.Items.Count,
                    "L'elenco dei proposti deve contenere 300 elementi.");
                Assert.Equal(0, result.Errors, "Nessun errore atteso.");
            }
        }

        [Test]
        public void Engine_NoThreshold_ProposesEverythingAndExcludesNothing()
        {
            using (Fixture fx = new Fixture())
            {
                for (int i = 0; i < 50; i++)
                    fx.CreateFile("recente-" + i + ".tmp", 1, TimeSpan.Zero);
                for (int i = 0; i < 50; i++)
                    fx.CreateFile("vecchio-" + i + ".tmp", 1, TimeSpan.FromDays(5));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.Zero;

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.Equal(100, result.FilesScanned, "Tutti i 100 file devono essere analizzati.");
                Assert.Equal(100, result.FilesEligible,
                    "Con nessuna soglia tutti i file devono essere proposti.");
                Assert.Equal(0, result.FilesExcludedByAge,
                    "Con nessuna soglia nessun file deve essere escluso per età.");
                Assert.Equal(0, result.FilesSkipped, "Nessuno scartato atteso.");
            }
        }

        [Test]
        public void Engine_NoExclusionWhenAllFilesAreOld()
        {
            using (Fixture fx = new Fixture())
            {
                for (int i = 0; i < 20; i++)
                    fx.CreateFile("tutti-vecchi-" + i + ".tmp", 10, TimeSpan.FromDays(3));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(24);

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.Equal(20, result.FilesScanned, "Tutti i 20 file devono essere analizzati.");
                Assert.Equal(20, result.FilesEligible, "Tutti devono essere proposti.");
                Assert.Equal(0, result.FilesExcludedByAge, "Nessuna esclusione per età.");
                Assert.Equal(0, result.FilesSkipped, "Nessuno scartato.");
            }
        }

        [Test]
        public void Engine_AgeExclusionsAreDistinctFromOtherSkips()
        {
            using (Fixture fx = new Fixture())
            {
                // 3 file vecchi (proposti), 2 recenti (esclusi per età),
                // 1 junction che viene saltata come cartella (non come file).
                for (int i = 0; i < 3; i++)
                    fx.CreateFile("vecchio-" + i + ".tmp", 10, TimeSpan.FromDays(2));
                for (int i = 0; i < 2; i++)
                    fx.CreateFile("recente-" + i + ".tmp", 10, TimeSpan.FromMinutes(5));
                string link = Path.Combine(fx.Root, "collegamento");
                Fixture.CreateJunction(link, fx.Outside);

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(24);

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.Equal(5, result.FilesScanned,
                    "Solo i 5 file devono essere analizzati (la junction non è un file).");
                Assert.Equal(3, result.FilesEligible, "I 3 file vecchi devono essere proposti.");
                Assert.Equal(2, result.FilesExcludedByAge,
                    "Solo i 2 file recenti devono essere esclusi per età.");
                Assert.True(result.FilesSkipped >= 2,
                    "FilesSkipped deve includere almeno le 2 esclusioni per età.");
                Assert.True(result.DirectoriesSkipped >= 1,
                    "La junction deve essere saltata come cartella.");
                Assert.True(result.FilesExcludedByAge <= result.FilesSkipped,
                    "Le esclusioni per età non possono superare il totale degli scartati.");
            }
        }

        #endregion

        #region Conteggio selezionati e aggiornamento GUI

        [Test]
        public void SelectedCount_InitiallyEqualsCheckedResults()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 5);
                    // Simula una scansione completata: senza _lastScan il riepilogo
                    // mostra lo stato iniziale, non i conteggi della scansione.
                    ScanResult completed = new ScanResult();
                    completed.Success = true;
                    completed.FilesScanned = 5;
                    completed.FilesEligible = 5;
                    completed.FilesExcludedByAge = 0;
                    completed.Items = items;
                    SetField(form, "_lastScan", completed);
                    SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                    Invoke(form, "PopulateGrid", new object[] { items });

                    // FileItem.Selected è true per tutti (default dello scanner).
                    Assert.Equal(5, CountChecked(form),
                        "Tutte le 5 righe devono essere spuntate inizialmente.");
                    Assert.Contains("Selezionati per la pulizia: 5", GetSummaryText(form),
                        "Il riepilogo deve mostrare 5 selezionati: " + GetSummaryText(form));
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void SelectedCount_UpdatesAfterDeselectingARow()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 4);
                    ScanResult completed = new ScanResult();
                    completed.Success = true;
                    completed.FilesScanned = 4;
                    completed.FilesEligible = 4;
                    completed.Items = items;
                    SetField(form, "_lastScan", completed);
                    SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Assert.Contains("Selezionati per la pulizia: 4", GetSummaryText(form),
                        "Il riepilogo iniziale deve mostrare 4 selezionati.");

                    // Deseleziona la seconda riga.
                    DataGridView grid = GetField<DataGridView>(form, "_grid");
                    grid.Rows[1].Cells["colSelected"].Value = false;

                    Assert.Equal(3, CountChecked(form),
                        "Dopo la deselezione devono restare 3 spunti.");
                    Assert.Contains("Selezionati per la pulizia: 3", GetSummaryText(form),
                        "Il riepilogo deve aggiornarsi a 3 selezionati: " + GetSummaryText(form));
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void SelectedCount_UpdatesAfterBulkSelectAndDeselect()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 6);
                    ScanResult completed = new ScanResult();
                    completed.Success = true;
                    completed.FilesScanned = 6;
                    completed.FilesEligible = 6;
                    completed.Items = items;
                    SetField(form, "_lastScan", completed);
                    SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                    Invoke(form, "PopulateGrid", new object[] { items });

                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });
                    Assert.Contains("Selezionati per la pulizia: 0", GetSummaryText(form),
                        "Dopo Deseleziona tutto il riepilogo deve mostrare 0 selezionati: " + GetSummaryText(form));

                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });
                    Assert.Contains("Selezionati per la pulizia: 6", GetSummaryText(form),
                        "Dopo Seleziona tutto il riepilogo deve mostrare 6 selezionati: " + GetSummaryText(form));
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Soglia e stato iniziale

        [Test]
        public void Summary_ShowsThresholdLabelCorrectly()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    // Simula una scansione completata con soglia 24 ore.
                    ScanResult completed = new ScanResult();
                    completed.Success = true;
                    completed.FilesScanned = 2;
                    completed.FilesEligible = 1;
                    completed.FilesExcludedByAge = 1;
                    completed.FilesSkipped = 1;
                    SetField(form, "_lastScan", completed);
                    SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                    Invoke(form, "UpdateSummaryLabel", new object[] { });

                    Assert.Contains("Soglia temporale: 24 ore", GetSummaryText(form),
                        "Il riepilogo deve mostrare la soglia 24 ore: " + GetSummaryText(form));

                    // Cambia la soglia della scansione a "nessuna".
                    SetField(form, "_lastScanMinAge", TimeSpan.Zero);
                    Invoke(form, "UpdateSummaryLabel", new object[] { });

                    Assert.Contains("Soglia temporale: nessuna", GetSummaryText(form),
                        "Con soglia Zero il riepilogo deve mostrare 'nessuna': " + GetSummaryText(form));
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void InitialState_ShowsNoResultsAndCurrentThreshold()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string text = GetSummaryText(form);
                    Assert.Contains("Nessuna analisi eseguita", text,
                        "Senza scansione il riepilogo deve indicare lo stato iniziale: " + text);
                    Assert.Contains("Soglia temporale:", text,
                        "Lo stato iniziale deve mostrare la soglia corrente: " + text);
                    Assert.DoesNotContain("File analizzati:", text,
                        "Lo stato iniziale non deve presentare conteggi come risultati attuali.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void FailedScan_ShowsInitialStateNotStaleNumbers()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    // Simula una scansione fallita (Success = false).
                    ScanResult failed = new ScanResult();
                    failed.Success = false;
                    failed.FilesScanned = 0;
                    failed.FilesEligible = 0;
                    SetField(form, "_lastScan", failed);
                    Invoke(form, "UpdateSummaryLabel", new object[] { });

                    string text = GetSummaryText(form);
                    Assert.Contains("Nessuna analisi eseguita", text,
                        "Una scansione fallita deve mostrare lo stato iniziale: " + text);
                    Assert.DoesNotContain("File analizzati: 0", text,
                        "Una scansione fallita non deve presentare zero come risultato attuale.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void NewScan_DoesNotReuseStaleCounts()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    // Prima scansione: 3 proposti.
                    ScanResult first = new ScanResult();
                    first.Success = true;
                    first.FilesScanned = 3;
                    first.FilesEligible = 3;
                    first.FilesExcludedByAge = 0;
                    SetField(form, "_lastScan", first);
                    SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                    Invoke(form, "UpdateSummaryLabel", new object[] { });
                    Assert.Contains("Proposti dal filtro: 3", GetSummaryText(form),
                        "La prima scansione deve mostrare 3 proposti: " + GetSummaryText(form));

                    // Nuova scansione: i conteggi precedenti non devono persistere.
                    ScanResult second = new ScanResult();
                    second.Success = true;
                    second.FilesScanned = 1;
                    second.FilesEligible = 1;
                    second.FilesExcludedByAge = 0;
                    SetField(form, "_lastScan", second);
                    Invoke(form, "UpdateSummaryLabel", new object[] { });

                    string text = GetSummaryText(form);
                    Assert.Contains("File analizzati: 1", text,
                        "La nuova scansione deve mostrare 1 analizzato: " + text);
                    Assert.Contains("Proposti dal filtro: 1", text,
                        "La nuova scansione deve mostrare 1 proposto: " + text);
                    Assert.DoesNotContain("Proposti dal filtro: 3", text,
                        "I conteggi della scansione precedente non devono restare.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CancelledScan_ShowsPartialResultsNotStaleNumbers()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    // Simula una scansione annullata con risultati parziali.
                    ScanResult cancelled = new ScanResult();
                    cancelled.Success = true;
                    cancelled.Cancelled = true;
                    cancelled.FilesScanned = 10;
                    cancelled.FilesEligible = 4;
                    cancelled.FilesExcludedByAge = 6;
                    cancelled.FilesSkipped = 6;
                    SetField(form, "_lastScan", cancelled);
                    SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
                    Invoke(form, "UpdateSummaryLabel", new object[] { });

                    string text = GetSummaryText(form);
                    Assert.Contains("File analizzati: 10", text,
                        "L'annullamento deve mostrare i risultati parziali: " + text);
                    Assert.Contains("Proposti dal filtro: 4", text,
                        "I proposti parziali devono essere mostrati: " + text);
                    Assert.Contains("Esclusi dalla soglia: 6", text,
                        "Le esclusioni parziali devono essere mostrate: " + text);
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Report

        [Test]
        public void Report_ReadOnlyAnalysis_ShowsAllCountsAndNoDeletion()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("vecchio.tmp", 100, TimeSpan.FromHours(48));
                fx.CreateFile("recente.tmp", 100, TimeSpan.FromMinutes(5));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(24);
                ScanResult scan = new TempFolderScanner().Scan(options);

                ReportData data = ReportBuilder.Build(scan, null, fx.Root);
                data.MinAgeText = "24 ore";
                data.FilesSelected = scan.Items.Count;
                string text = ReportExporter.BuildText(data);

                Assert.Contains("File analizzati            : 2", text,
                    "Il report deve mostrare 2 file analizzati.");
                Assert.Contains("File proposti              : 1", text,
                    "Il report deve mostrare 1 file proposto.");
                Assert.Contains("File esclusi dalla soglia  : 1", text,
                    "Il report deve mostrare 1 file escluso dalla soglia.");
                Assert.Contains("Soglia temporale           : 24 ore", text,
                    "Il report deve mostrare la soglia usata.");
                Assert.Contains("Nessuna pulizia eseguita.", text,
                    "Un'analisi sola lettura non deve presentare pulizia.");
                Assert.DoesNotContain("File eliminati             : ", text,
                    "Un'analisi sola lettura non deve presentare file come già eliminati.");
            }
        }

        [Test]
        public void Report_AfterClean_KeepsScanAndCleanStatsSeparate()
        {
            using (Fixture fx = new Fixture())
            {
                string old1 = fx.CreateFile("vecchio1.tmp", 100, TimeSpan.FromHours(48));
                string old2 = fx.CreateFile("vecchio2.tmp", 200, TimeSpan.FromHours(48));
                fx.CreateFile("recente.tmp", 100, TimeSpan.FromMinutes(5));

                ScanOptions scanOptions = new ScanOptions();
                scanOptions.Root = fx.Root;
                scanOptions.MinAge = TimeSpan.FromHours(24);
                ScanResult scan = new TempFolderScanner().Scan(scanOptions);

                // Pulizia di un solo file.
                List<FileItem> toClean = new List<FileItem>();
                toClean.Add(Fixture.MakeItem(old1));
                CleanOptions cleanOptions = new CleanOptions();
                cleanOptions.Root = fx.Root;
                cleanOptions.Items = toClean;
                CleanResult clean = new TempFileCleaner().Clean(cleanOptions);

                ReportData data = ReportBuilder.Build(scan, clean, fx.Root);
                data.MinAgeText = "24 ore";
                data.FilesSelected = 1;
                string text = ReportExporter.BuildText(data);

                Assert.Contains("File analizzati            : 3", text,
                    "Il report deve mostrare 3 file analizzati (3 creati).");
                Assert.Contains("File proposti              : 2", text,
                    "Il report deve mostrare 2 proposti (2 vecchi).");
                Assert.Contains("File esclusi dalla soglia  : 1", text,
                    "Il report deve mostrare 1 escluso dalla soglia.");
                Assert.Contains("File eliminati             : 1", text,
                    "Il report deve mostrare 1 file eliminato.");
                Assert.Contains("Spazio effettivamente liberato", text,
                    "Il report deve mostrare lo spazio realmente liberato.");
                Assert.True(File.Exists(old2),
                    "Il secondo file vecchio non selezionato deve restare.");
                Assert.False(File.Exists(old1),
                    "Il file selezionato e pulito deve essere stato eliminato.");
            }
        }

        [Test]
        public void Report_Json_IncludesNewCounters()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("a.tmp", 10, TimeSpan.FromHours(48));
                fx.CreateFile("b.tmp", 10, TimeSpan.FromMinutes(5));

                ScanOptions options = new ScanOptions();
                options.Root = fx.Root;
                options.MinAge = TimeSpan.FromHours(24);
                ScanResult scan = new TempFolderScanner().Scan(options);

                ReportData data = ReportBuilder.Build(scan, null, fx.Root);
                data.MinAgeText = "24 ore";
                string json = ReportExporter.BuildJson(data);

                Assert.Contains("\"fileEsclusiDallaSoglia\": 1", json,
                    "Il JSON deve contenere il contatore delle esclusioni per età.");
                Assert.Contains("\"sogliaTemporale\": \"24 ore\"", json,
                    "Il JSON deve contenere la soglia temporale.");
            }
        }

        #endregion

        #region Utilità

        private static List<FileItem> CreateItems(Fixture fx, int count)
        {
            List<FileItem> items = new List<FileItem>();
            for (int i = 0; i < count; i++)
                items.Add(Fixture.MakeItem(fx.CreateFile("file" + i + ".tmp", 10 * (i + 1))));
            return items;
        }

        private static int CountChecked(MainForm form)
        {
            DataGridView grid = GetField<DataGridView>(form, "_grid");
            int count = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                object value = row.Cells["colSelected"].Value;
                if (value is bool && (bool)value) count++;
            }
            return count;
        }

        private static string GetSummaryText(MainForm form)
        {
            return GetField<Label>(form, "_summaryLabel").Text;
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
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
            if (!thread.Join(120000))
            {
                failure = new TimeoutException("Il test UI non è terminato entro 120 secondi.");
                return;
            }
            failure = captured;
        }

        #endregion
    }
}
