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
    /// Test di regressione per l'assegnazione del valore della barra di
    /// avanzamento durante la pulizia (0.4.5).
    ///
    /// Difetto corretto: OnCleanClick configura la barra con
    /// Maximum = numero di elementi selezionati, mentre l'aggiornamento UI
    /// assegnava una percentuale 0-100. Con meno di 100 file selezionati, oltre
    /// la soglia percentuale > Maximum, il setter di Value lanciava
    /// ArgumentOutOfRangeException sul thread UI (callback BeginInvoke), mostrato
    /// dal gestore delle eccezioni non gestite anche a pulizia già completata.
    ///
    /// Coprono: il caso riportato (40 file, 50%), 0/1/50/molti elementi,
    /// clamp difensivo, totale zero, stato non occupato, flusso reale con 50
    /// file e con errore parziale (file bloccato), ripristino della barra.
    /// </summary>
    public sealed class CleanProgressTests
    {
        #region Scrittura UI del progresso (riproduzione del difetto)

        [Test]
        public void ProgressUi_FiftyPercentOfFortyFiles_NeverExceedsTheBarMaximum()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    // Configurazione identica a OnCleanClick con 40 selezionati.
                    SetField(form, "_busy", true);
                    ProgressBar bar = GetField<ProgressBar>(form, "_progress");
                    bar.Minimum = 0;
                    bar.Maximum = 40;
                    bar.Value = 0;

                    // 50% di 40 elementi: il vecchio codice calcolava il 50% (50)
                    // e lo assegnava a Value con Maximum=40 → eccezione.
                    CleanProgress progress = new CleanProgress();
                    progress.Processed = 20;
                    progress.Total = 40;
                    progress.Deleted = 18;
                    Invoke(form, "UpdateCleanProgressUi", new object[] { progress });

                    Assert.Equal(20, bar.Value,
                        "La barra deve mostrare gli elementi elaborati, entro Maximum.");

                    progress.Processed = 40;
                    progress.Deleted = 40;
                    Invoke(form, "UpdateCleanProgressUi", new object[] { progress });

                    Assert.Equal(40, bar.Value,
                        "Al termine la barra deve raggiungere esattamente Maximum.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ProgressUi_SingleItem_ClampsToOne()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_busy", true);
                    ProgressBar bar = GetField<ProgressBar>(form, "_progress");
                    bar.Minimum = 0;
                    bar.Maximum = Math.Max(1, 1);
                    bar.Value = 0;

                    CleanProgress progress = new CleanProgress();
                    progress.Processed = 0;
                    progress.Total = 1;
                    Invoke(form, "UpdateCleanProgressUi", new object[] { progress });
                    Assert.Equal(0, bar.Value, "Con un solo elemento l'avvio deve restare a 0.");

                    progress.Processed = 1;
                    progress.Deleted = 1;
                    Invoke(form, "UpdateCleanProgressUi", new object[] { progress });
                    Assert.Equal(1, bar.Value,
                        "Con un solo elemento il completamento deve portare la barra a 1.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ProgressUi_ManyItems_StaysWithinLimitsAtEveryStep()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_busy", true);
                    ProgressBar bar = GetField<ProgressBar>(form, "_progress");
                    bar.Minimum = 0;
                    bar.Maximum = 250;
                    bar.Value = 0;

                    int[] steps = new int[] { 0, 1, 50, 125, 249, 250, 300 };
                    for (int i = 0; i < steps.Length; i++)
                    {
                        CleanProgress progress = new CleanProgress();
                        progress.Processed = steps[i];
                        progress.Total = 250;
                        progress.Deleted = steps[i];
                        Invoke(form, "UpdateCleanProgressUi", new object[] { progress });

                        int expected = steps[i] > 250 ? 250 : steps[i];
                        Assert.Equal(expected, bar.Value,
                            "Il passo " + steps[i] + " deve restare entro i limiti della barra.");
                        Assert.True(bar.Value >= bar.Minimum && bar.Value <= bar.Maximum,
                            "Il valore deve rispettare sempre minimum/maximum.");
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ProgressUi_ZeroTotal_LeavesTheBarUntouched()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_busy", true);
                    ProgressBar bar = GetField<ProgressBar>(form, "_progress");
                    bar.Minimum = 0;
                    bar.Maximum = 10;
                    bar.Value = 3;

                    CleanProgress progress = new CleanProgress();
                    progress.Processed = 0;
                    progress.Total = 0;
                    Invoke(form, "UpdateCleanProgressUi", new object[] { progress });

                    Assert.Equal(3, bar.Value,
                        "Con Total=0 la barra non deve essere toccata.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ProgressUi_WhenNotBusy_IsIgnored()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_busy", false);
                    ProgressBar bar = GetField<ProgressBar>(form, "_progress");
                    bar.Minimum = 0;
                    bar.Maximum = 10;
                    bar.Value = 0;

                    CleanProgress progress = new CleanProgress();
                    progress.Processed = 8;
                    progress.Total = 10;
                    Invoke(form, "UpdateCleanProgressUi", new object[] { progress });

                    Assert.Equal(0, bar.Value,
                        "Fuori dalla pulizia il callback non deve aggiornare la barra.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Flusso reale di pulizia

        [Test]
        public void CleanFlow_FiftyFiles_AllDeletedAndBarReset()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("progress-50-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);

                        List<FileItem> items = new List<FileItem>();
                        for (int i = 0; i < 50; i++)
                        {
                            string path = fx.CreateFile("cinquanta-" + i + ".tmp", 64);
                            items.Add(Fixture.MakeItem(path));
                        }
                        Invoke(form, "PopulateGrid", new object[] { items });
                        Invoke(form, "SetAllChecks", new object[] { true });

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        CleanResult clean = (CleanResult)GetFieldValue(form, "_lastClean");
                        Assert.NotNull(clean, "Il risultato della pulizia deve esistere.");
                        Assert.True(clean.Success, "La pulizia di 50 file deve riuscire.");
                        Assert.Equal(50, clean.DeletedCount,
                            "Tutti e 50 i file devono essere stati eliminati.");

                        ProgressBar bar = GetField<ProgressBar>(form, "_progress");
                        Assert.Equal(0, bar.Value,
                            "Al termine la barra deve essere azzerata senza eccezioni.");

                        Label status = GetField<Label>(form, "_cleanStatusLabel");
                        Assert.Equal("Pulizia completata.", status.Text,
                            "Il riepilogo deve dichiarare il completamento.");
                    }
                    finally
                    {
                        DeleteDirectoryIfPresent(reports);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CleanFlow_OneFile_SuccessAndBarReset()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("progress-1-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);
                        string file = fx.CreateFile("uno.tmp", 128);
                        List<FileItem> items = new List<FileItem>();
                        items.Add(Fixture.MakeItem(file));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        Invoke(form, "SetAllChecks", new object[] { true });

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        CleanResult clean = (CleanResult)GetFieldValue(form, "_lastClean");
                        Assert.Equal(1, clean.DeletedCount, "Il singolo file deve essere eliminato.");
                        Assert.Equal(0, GetField<ProgressBar>(form, "_progress").Value,
                            "La barra deve essere azzerata al termine.");
                    }
                    finally
                    {
                        DeleteDirectoryIfPresent(reports);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CleanFlow_PartialFailure_BarResetsAndSummaryShowsWarnings()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("progress-partial-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);

                        string ok = fx.CreateFile("ok.tmp", 128);
                        string locked = fx.CreateFile("bloccato.tmp", 128);
                        List<FileItem> items = new List<FileItem>();
                        items.Add(Fixture.MakeItem(ok));
                        items.Add(Fixture.MakeItem(locked));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        Invoke(form, "SetAllChecks", new object[] { true });

                        using (FileStream hold = new FileStream(
                            locked, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                            WaitUntilIdle(form);
                        }

                        CleanResult clean = (CleanResult)GetFieldValue(form, "_lastClean");
                        Assert.Equal(1, clean.DeletedCount,
                            "Il file sbloccato deve essere eliminato.");
                        Assert.Equal(1, clean.ErrorCount,
                            "Il file bloccato deve essere conteggiato come errore.");
                        Assert.Equal(0, GetField<ProgressBar>(form, "_progress").Value,
                            "Anche con errore parziale la barra deve essere azzerata.");

                        Label status = GetField<Label>(form, "_cleanStatusLabel");
                        Assert.Equal("Pulizia completata con avvisi.", status.Text,
                            "L'errore parziale deve distinguersi dal successo pieno.");
                    }
                    finally
                    {
                        DeleteDirectoryIfPresent(reports);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Utilità

        private static void PrepareFlowForm(MainForm form, string root, string reportsFolder)
        {
            SetField(form, "_getAuthorizedRoot",
                new Func<TempPathResult>(delegate { return TempPathResult.Ok(root); }));
            SetField(form, "_confirmClean",
                new Func<string, bool>(delegate(string question) { return true; }));
            SetField(form, "_confirmRemoveEmptyFolders",
                new Func<int, bool>(delegate(int count) { return true; }));
            SetField(form, "_showMessage",
                new Func<string, string, MessageBoxIcon, bool>(delegate(string text, string title, MessageBoxIcon icon)
                {
                    return true;
                }));
            SetField(form, "_notifyUser",
                new Action<string>(delegate(string text) { }));
            SetField(form, "_reportsFolder",
                new Func<string>(delegate { return reportsFolder; }));
            SetField(form, "_currentRoot", root);
            SetField(form, "_keepReports", true);
            SetField(form, "_lastScanMinAge", TimeSpan.FromHours(24));
        }

        private static void WaitUntilIdle(MainForm form)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while ((bool)GetFieldValue(form, "_busy") && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
            Application.DoEvents();
            Assert.False((bool)GetFieldValue(form, "_busy"),
                "L'operazione non è terminata entro 20 secondi.");
        }

        private static string TempDirectory(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "cleanslate-" + prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception) { }
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static object GetFieldValue(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static object Invoke(object target, string name, object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
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
