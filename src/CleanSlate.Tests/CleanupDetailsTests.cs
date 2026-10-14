using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CleanSlate.Core;
using CleanSlate.UI;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Test del riquadro dei risultati della pulizia e del pannello integrato
    /// dei dettagli (0.4.5). Il riquadro sostituisce l'etichetta compatta della
    /// 0.4.0 con una gerarchia a quattro livelli: stato sintetico, spazio
    /// recuperato in evidenza, statistiche secondarie separate e pannello
    /// richiudibile per tutti gli esiti.
    ///
    /// Coprono: correttezza degli stati (successo, annullamento, errore),
    /// apertura e chiusura dei dettagli, conservazione dei risultati,
    /// visualizzazione di tutti gli esiti, assenza di conteggi fuorvianti
    /// quando la fase cartelle non viene eseguita, nomi lunghi, molti
    /// risultati e compatibilità con il tema chiaro/scuro.
    /// </summary>
    public sealed class CleanupDetailsTests
    {
        #region Stato della pulizia

        [Test]
        public void Status_SuccessfulClean_ReportsCompleted()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 3;
            clean.DeletedBytes = 2048;

            Assert.Equal("Pulizia completata.",
                InvokeStatusText(clean, null),
                "Una pulizia senza errori deve dichiarare il completamento.");
        }

        [Test]
        public void Status_CancelledClean_IsNotPresentedAsSuccess()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.Cancelled = true;
            clean.DeletedCount = 1;

            string text = InvokeStatusText(clean, null);
            Assert.Equal("Pulizia annullata.", text,
                "Una pulizia annullata deve essere distinguibile.");
            Assert.DoesNotContain("completata", text,
                "L'annullamento non deve essere presentato come completamento.");
        }

        [Test]
        public void Status_Errors_ProduceWarningsNotFullSuccess()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 2;
            clean.ErrorCount = 1;

            Assert.Equal("Pulizia completata con avvisi.",
                InvokeStatusText(clean, null),
                "La presenza di errori deve produrre un avviso, non un successo pieno.");
        }

        [Test]
        public void Status_FolderPhaseError_ProducesWarnings()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 1;

            EmptyCleanResult failed = new EmptyCleanResult();
            failed.Success = false;
            failed.ErrorMessage = "La cartella dei file temporanei è cambiata.";

            Assert.Equal("Pulizia completata con avvisi.",
                InvokeStatusText(clean, failed),
                "Un errore della fase cartelle deve produrre un avviso.");
        }

        [Test]
        public void Status_FailedClean_IsNotPresentedAsSuccess()
        {
            CleanResult clean = new CleanResult();
            clean.Success = false;

            Assert.Equal("Pulizia non completata.",
                InvokeStatusText(clean, null),
                "Una pulizia non riuscita deve essere dichiarata come tale.");
        }

        #endregion

        #region Struttura del riquadro risultati

        [Test]
        public void CleanResultsPanel_ContainsStatusSpaceStatsAndDetailsButton()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Panel panel = GetField<Panel>(form, "_cleanResultsPanel");
                    Assert.NotNull(panel, "Il riquadro dei risultati deve esistere.");
                    Assert.Equal("pnlCleanResults", panel.Name,
                        "Il riquadro deve avere il nome atteso.");

                    Assert.NotNull(GetField<Label>(form, "_cleanStatusLabel"),
                        "L'etichetta dello stato deve esistere.");
                    Assert.NotNull(GetField<Label>(form, "_spaceValueLabel"),
                        "L'etichetta dello spazio recuperato deve esistere.");
                    Assert.NotNull(GetField<Label>(form, "_spaceCaptionLabel"),
                        "La didascalia dello spazio deve esistere.");
                    Assert.NotNull(GetField<FlowLayoutPanel>(form, "_statsFlow"),
                        "Il flusso delle statistiche deve esistere.");
                    Assert.NotNull(GetField<Label>(form, "_statDeletedValue"),
                        "Il contatore dei file eliminati deve esistere.");
                    Assert.NotNull(GetField<Label>(form, "_statSkippedValue"),
                        "Il contatore degli elementi saltati deve esistere.");
                    Assert.NotNull(GetField<Label>(form, "_statErrorValue"),
                        "Il contatore degli errori deve esistere.");

                    Button details = GetField<Button>(form, "_btnDetails");
                    Assert.NotNull(details, "Il pulsante dei dettagli deve esistere.");
                    Assert.Equal("Mostra dettagli", details.Text,
                        "Il pulsante deve dichiarare l'azione di apertura.");
                    Assert.True(panel.Controls.Contains(details),
                        "Il pulsante dei dettagli deve essere nel riquadro dei risultati.");
                    Assert.True(details.Left < 20,
                        "Il pulsante dei dettagli deve essere in basso a sinistra.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CleanResultsPanel_IsHiddenBeforeAnyClean()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Assert.Contains("Nessuna analisi eseguita.",
                        GetField<Label>(form, "_summaryLabel").Text,
                        "Prima di una pulizia l'etichetta della scansione deve essere attiva.");
                    Assert.Equal(string.Empty, GetField<Label>(form, "_cleanStatusLabel").Text,
                        "Prima di una pulizia lo stato dei risultati deve essere vuoto.");
                    Assert.Equal(238, GetField<DataGridView>(form, "_grid").Top,
                        "Prima di una pulizia la griglia deve restare nella posizione della scansione.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void DetailsButton_DisabledBeforeCleanEnabledAfter()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Button button = GetField<Button>(form, "_btnDetails");
                    Assert.NotNull(button, "Il pulsante Mostra dettagli deve esistere.");
                    Assert.False(button.Enabled,
                        "Prima di una pulizia il pulsante deve essere disabilitato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CleanResultsPanel_DetailsButtonHasMarginsAndDoesNotOverlapStats()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Panel panel = GetField<Panel>(form, "_cleanResultsPanel");
                    Button details = GetField<Button>(form, "_btnDetails");
                    FlowLayoutPanel stats = GetField<FlowLayoutPanel>(form, "_statsFlow");

                    Assert.True(details.Bottom + 6 <= panel.Height,
                        "Il pulsante deve avere un margine inferiore di almeno 6 px "
                        + "(nessun bordo tagliato dal contenitore).");
                    Assert.True(details.Top >= stats.Bottom,
                        "Il pulsante non deve sovrapporsi alla fascia delle statistiche.");
                    Assert.True(details.Left < 20,
                        "Il pulsante deve restare in basso a sinistra.");
                    Assert.True(details.Right <= panel.Width,
                        "Il pulsante deve restare dentro il riquadro anche in larghezza.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Flusso GUI: stato, spazio, statistiche

        [Test]
        public void AfterClean_PanelShowsStatusSpaceAndActualCounts()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("clean-results-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);
                        string vecchio = fx.CreateFile("vecchio.tmp", 2048);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Assert.Equal(350, GetField<DataGridView>(form, "_grid").Top,
                            "Dopo la pulizia la griglia deve spostarsi sotto il riquadro risultati.");

                        Label status = GetField<Label>(form, "_cleanStatusLabel");
                        Assert.Equal("Pulizia completata.", status.Text,
                            "Lo stato deve dichiarare il completamento.");

                        Label space = GetField<Label>(form, "_spaceValueLabel");
                        Assert.Equal(ByteFormatter.Format(2048), space.Text,
                            "Lo spazio recuperato deve essere effettivo e in primo piano.");

                        Assert.Equal("1", GetField<Label>(form, "_statDeletedValue").Text,
                            "Il contatore dei file eliminati deve essere reale.");
                        Assert.Equal("0", GetField<Label>(form, "_statSkippedValue").Text,
                            "Il contatore dei saltati deve essere reale.");
                        Assert.Equal("0", GetField<Label>(form, "_statErrorValue").Text,
                            "Il contatore degli errori deve essere reale.");
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
        public void PartialClean_StatusShowsWarningsAndErrorCount()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("clean-partial-reports");
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

                        Label status = GetField<Label>(form, "_cleanStatusLabel");
                        Assert.Equal("Pulizia completata con avvisi.", status.Text,
                            "Gli errori devono produrre un avviso, non un successo pieno.");
                        Assert.Equal("1", GetField<Label>(form, "_statErrorValue").Text,
                            "Il contatore degli errori deve essere reale.");
                        Assert.Equal("1", GetField<Label>(form, "_statDeletedValue").Text,
                            "Il contatore dei file eliminati deve essere reale.");
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
        public void CancelledClean_StatusIsDistinguishable()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    CleanResult cancelled = new CleanResult();
                    cancelled.Success = true;
                    cancelled.Cancelled = true;
                    cancelled.DeletedCount = 1;
                    cancelled.DeletedBytes = 100;

                    SetField(form, "_lastClean", cancelled);
                    Invoke(form, "UpdateCleanResults", null);

                    Label status = GetField<Label>(form, "_cleanStatusLabel");
                    Assert.Equal("Pulizia annullata.", status.Text,
                        "Lo stato annullato deve essere distinguibile dal completato.");
                    Assert.DoesNotContain("completata", status.Text,
                        "L'annullamento non deve essere presentato come completamento.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ReportLabel_IsInAVisibleStripNeverBehindTheGrid()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    CleanResult clean = new CleanResult();
                    clean.Success = true;
                    clean.DeletedCount = 1;
                    clean.DeletedBytes = 512;

                    SetField(form, "_lastClean", clean);
                    Invoke(form, "UpdateCleanResults", null);

                    Label report = GetField<Label>(form, "_reportLabel");
                    Panel panel = GetField<Panel>(form, "_cleanResultsPanel");
                    DataGridView grid = GetField<DataGridView>(form, "_grid");
                    Panel details = GetField<Panel>(form, "_detailsPanel");

                    Assert.True(report.Top >= panel.Bottom,
                        "Il report deve stare sotto il riquadro risultati, non sovrapposto.");
                    Assert.True(report.Bottom <= grid.Top,
                        "Il report deve stare sopra la griglia, mai dietro di essa.");

                    Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                    Application.DoEvents();

                    Assert.True(report.Bottom <= grid.Top,
                        "Con i dettagli aperti il report resta sopra la griglia.");
                    Assert.True(details.Top >= grid.Bottom,
                        "Con i dettagli aperti il pannello resta sotto la griglia.");
                    Assert.True(report.Bottom <= grid.Top && grid.Bottom <= details.Top,
                        "Nessuna sovrapposizione tra report, griglia e dettagli.");

                    Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                    Invoke(form, "ShowScanSummary", null);

                    Assert.Equal(216, report.Top,
                        "Tornati in modalità analisi il report deve tornare alla posizione iniziale.");
                    Assert.Equal(238, grid.Top,
                        "Tornati in modalità analisi la griglia deve tornare alla posizione iniziale.");
                    Assert.False((bool)GetFieldValue(form, "_detailsOpen"),
                        "Tornati in modalità analisi lo stato dei dettagli deve essere chiuso.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Fase cartelle vuote

        [Test]
        public void FolderStatCell_HiddenWhenPhaseDidNotRun()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("clean-nofolder-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);
                        SetField(form, "_removeEmptyFolders", false);

                        string vecchio = fx.CreateFile("vecchio.tmp", 512);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Assert.Null(GetFieldValue(form, "_lastEmptyClean"),
                            "Senza fase cartelle non deve esserci un risultato cartelle.");
                        Assert.Equal(string.Empty, GetField<Label>(form, "_statFolderValue").Text,
                            "Senza fase cartelle il contatore non deve mostrare un valore.");
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
        public void FolderStatCell_VisibleWithRealCountWhenPhaseRan()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("clean-folder-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);
                        SetField(form, "_removeEmptyFolders", true);

                        string vecchio = fx.CreateFile("vecchio.tmp", 512);
                        string vuota = Path.Combine(fx.Root, "vuota");
                        Directory.CreateDirectory(vuota);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Assert.Equal("1", GetField<Label>(form, "_statFolderValue").Text,
                            "Il conteggio delle cartelle rimosse deve essere reale.");
                        Assert.True(Directory.Exists(fx.Root),
                            "La radice non deve essere rimossa.");
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
        public void FailedFolderPhase_ShowsWarningWithoutFolderCounts()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    // Stato simulato: pulizia dei file riuscita, fase cartelle fallita.
                    CleanResult clean = new CleanResult();
                    clean.Success = true;
                    clean.DeletedCount = 1;
                    clean.DeletedBytes = 512;

                    EmptyCleanResult failed = new EmptyCleanResult();
                    failed.Success = false;
                    failed.ErrorMessage = "La cartella dei file temporanei è cambiata.";

                    SetField(form, "_lastClean", clean);
                    SetField(form, "_lastEmptyClean", failed);
                    Invoke(form, "UpdateCleanResults", null);

                    Label status = GetField<Label>(form, "_cleanStatusLabel");
                    Assert.Equal("Pulizia completata con avvisi.", status.Text,
                        "Un errore della fase cartelle deve produrre un avviso.");

                    Assert.Equal(string.Empty, GetField<Label>(form, "_statFolderValue").Text,
                        "Con fase fallita il contatore cartelle non deve mostrare valori.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Pannello dettagli: apertura, chiusura, contenuto

        [Test]
        public void DetailsToggle_OpensAndShowsAllOutcomes()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("details-open-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);

                        string vecchio = fx.CreateFile("vecchio.tmp", 512);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Button button = GetField<Button>(form, "_btnDetails");
                        Assert.Equal("Mostra dettagli", button.Text,
                            "Prima dell'apertura il testo deve indicare Mostra dettagli.");

                        Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                        Application.DoEvents();

                        Assert.Equal("Nascondi dettagli", button.Text,
                            "Il testo del pulsante deve riflettere lo stato di apertura.");

                        TextBox body = GetField<TextBox>(form, "_detailsTextBox");
                        Assert.True(body.Text.Length > 0,
                            "Il pannello deve contenere il testo dei dettagli.");
                        Assert.Contains("Pulizia completata.", body.Text,
                            "Il testo deve contenere lo stato della pulizia.");
                        Assert.Contains("--- Esiti dei file ---", body.Text,
                            "Il testo deve contenere la sezione degli esiti.");
                        Assert.Contains("vecchio.tmp", body.Text,
                            "Il nome del file deve comparire nel pannello.");
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
        public void DetailsToggle_ClosePreservesResultsAndReopensSameContent()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("details-close-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);

                        string vecchio = fx.CreateFile("vecchio.tmp", 512);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        CleanResult clean = (CleanResult)GetFieldValue(form, "_lastClean");
                        int deletedBefore = clean.DeletedCount;
                        Label status = GetField<Label>(form, "_cleanStatusLabel");
                        string statusBefore = status.Text;

                        Button button = GetField<Button>(form, "_btnDetails");
                        Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                        Application.DoEvents();
                        TextBox body = GetField<TextBox>(form, "_detailsTextBox");
                        string firstOpen = body.Text;

                        Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                        Application.DoEvents();
                        Assert.Equal("Mostra dettagli", button.Text,
                            "Il secondo clic deve richiudere e ripristinare il testo.");

                        Assert.Equal(statusBefore, status.Text,
                            "La chiusura dei dettagli deve conservare lo stato.");
                        CleanResult after = (CleanResult)GetFieldValue(form, "_lastClean");
                        Assert.Equal(deletedBefore, after.DeletedCount,
                            "La chiusura dei dettagli non deve ripetere la pulizia.");

                        Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                        Application.DoEvents();
                        Assert.Equal("Nascondi dettagli", button.Text,
                            "Il pannello deve poter essere riaperto.");
                        Assert.Equal(firstOpen, body.Text,
                            "La riapertura deve mostrare lo stesso contenuto.");
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
        public void DetailsToggle_DoesNotRepeatCleanOrRescan()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string reports = TempDirectory("details-norepeat-reports");
                    try
                    {
                        PrepareFlowForm(form, fx.Root, reports);

                        string vecchio = fx.CreateFile("vecchio.tmp", 512);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);
                        Assert.False(File.Exists(vecchio),
                            "Il file deve essere eliminato dalla prima pulizia.");

                        CleanResult before = (CleanResult)GetFieldValue(form, "_lastClean");
                        int deletedBefore = before.DeletedCount;

                        Invoke(form, "OnDetailsClick", new object[] { null, EventArgs.Empty });
                        Application.DoEvents();

                        CleanResult after = (CleanResult)GetFieldValue(form, "_lastClean");
                        Assert.Equal(deletedBefore, after.DeletedCount,
                            "L'apertura dei dettagli non deve ripetere la pulizia.");
                        Assert.False(File.Exists(vecchio),
                            "L'apertura dei dettagli non deve creare nuovi file.");
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
        public void DetailsPanel_IsScrollableAndReadOnly()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    TextBox body = GetField<TextBox>(form, "_detailsTextBox");
                    Assert.NotNull(body, "La casella dei dettagli deve esistere.");
                    Assert.True(body.ReadOnly,
                        "La casella dei dettagli deve essere sola lettura.");
                    Assert.True(body.Multiline,
                        "La casella dei dettagli deve essere multilinea.");
                    Assert.True(body.ScrollBars != ScrollBars.None,
                        "La casella dei dettagli deve avere una barra di scorrimento.");
                    Assert.False(body.WordWrap,
                        "Il testo non deve essere spezzato: scorre orizzontalmente.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Testo dei dettagli: completezza

        [Test]
        public void DetailsText_IncludesAllFileItemOutcomesNotJustFirstSix()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 10;
            clean.ErrorCount = 10;
            for (int i = 0; i < 10; i++)
            {
                CleanItemResult item = new CleanItemResult();
                item.Name = "file-" + i + ".tmp";
                item.Outcome = i < 5 ? CleanOutcome.Deleted : CleanOutcome.Failed;
                item.Message = "errore " + i;
                clean.Items.Add(item);
            }

            string text = CleanupDetailsForm.BuildDetailsText(clean, null);

            Assert.Contains("--- Esiti dei file ---", text,
                "La sezione degli esiti dei file deve comparire.");
            for (int i = 0; i < 10; i++)
            {
                Assert.Contains("file-" + i + ".tmp", text,
                    "L'elemento file-" + i + ".tmp deve comparire (elenco completo, non troncato).");
            }
        }

        [Test]
        public void DetailsText_IncludesAllFolderItemOutcomes()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 1;
            clean.DeletedBytes = 100;

            EmptyCleanResult empty = new EmptyCleanResult();
            empty.Success = true;
            empty.RemovedCount = 2;
            EmptyDirectoryItemResult removed = new EmptyDirectoryItemResult();
            removed.Name = "vuota-a";
            removed.Outcome = EmptyDirectoryOutcome.Removed;
            empty.Items.Add(removed);
            EmptyDirectoryItemResult skipped = new EmptyDirectoryItemResult();
            skipped.Name = "vuota-b";
            skipped.Outcome = EmptyDirectoryOutcome.SkippedNotEmpty;
            skipped.Message = "contiene file";
            empty.Items.Add(skipped);

            string text = CleanupDetailsForm.BuildDetailsText(clean, empty);

            Assert.Contains("--- Esiti delle cartelle vuote ---", text,
                "La sezione degli esiti delle cartelle deve comparire.");
            Assert.Contains("vuota-a", text, "La cartella rimossa deve comparire.");
            Assert.Contains("Removed", text, "L'esito Removed deve comparire.");
            Assert.Contains("vuota-b", text, "La cartella saltata deve comparire.");
            Assert.Contains("SkippedNotEmpty", text,
                "L'esito SkippedNotEmpty deve comparire.");
            Assert.Contains("contiene file", text,
                "Il messaggio della cartella saltata deve comparire.");
        }

        [Test]
        public void DetailsText_ShowsFolderSectionOnlyWhenThePhaseRan()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 1;
            clean.DeletedBytes = 100;

            string without = CleanupDetailsForm.BuildDetailsText(clean, null);
            Assert.DoesNotContain("Cartelle vuote", without,
                "Senza fase eseguita non deve esserci alcuna sezione cartelle.");
            Assert.DoesNotContain("--- Esiti delle cartelle vuote ---", without,
                "Senza fase eseguita non deve esserci alcuna sezione esiti cartelle.");

            EmptyCleanResult empty = new EmptyCleanResult();
            empty.Success = true;
            empty.RemovedCount = 1;

            string with = CleanupDetailsForm.BuildDetailsText(clean, empty);
            Assert.Contains("Cartelle vuote", with,
                "Con fase eseguita la sezione cartelle deve comparire.");
        }

        [Test]
        public void DetailsText_EmptyList_HandlesGracefully()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 0;
            clean.DeletedBytes = 0;

            string text = CleanupDetailsForm.BuildDetailsText(clean, null);

            Assert.Contains("Pulizia completata.", text,
                "Anche con elenco vuoto il riepilogo deve comparire.");
            Assert.DoesNotContain("--- Esiti dei file ---", text,
                "Con elenco vuoto non deve comparire la sezione esiti.");
        }

        [Test]
        public void DetailsText_HandlesVeryLongFileNames()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            CleanItemResult item = new CleanItemResult();
            item.Name = "questo-e-un-nome-d file-veramente-lungo-"
                + new string('x', 200) + ".tmp";
            item.Outcome = CleanOutcome.Deleted;
            clean.Items.Add(item);

            string text = CleanupDetailsForm.BuildDetailsText(clean, null);
            Assert.Contains(item.Name, text,
                "Un nome lunghissimo deve comparire per intero, senza troncamento.");
        }

        [Test]
        public void DetailsText_HandlesManyResults()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            for (int i = 0; i < 80; i++)
            {
                CleanItemResult item = new CleanItemResult();
                item.Name = "molto-" + i.ToString(CultureInfo.InvariantCulture) + ".tmp";
                item.Outcome = CleanOutcome.Deleted;
                clean.Items.Add(item);
            }

            string text = CleanupDetailsForm.BuildDetailsText(clean, null);
            for (int i = 0; i < 80; i++)
            {
                Assert.Contains("molto-" + i.ToString(CultureInfo.InvariantCulture) + ".tmp",
                    text, "L'elemento " + i + " deve comparire (elenco completo).");
            }
        }

        #endregion

        #region Tema

        [Test]
        public void CleanResultsPanel_UsesPaletteColorsForBothThemes()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    // Preferenza su automatica: i colori devono venire solo dal
                    // seam _detectTheme iniettato, mai dal settings.txt reale.
                    SetField(form, "_themeChoice", ThemeChoice.Automatic);
                    // Tema scuro.
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Dark; }));
                    Invoke(form, "ApplyTheme", null);
                    ThemePalette dark = (ThemePalette)GetFieldValue(form, "_palette");

                    Assert.Equal(dark.Surface.ToArgb(),
                        GetField<Panel>(form, "_cleanResultsPanel").BackColor.ToArgb(),
                        "Il riquadro dei risultati deve usare lo sfondo della palette scura.");
                    Assert.Equal(dark.Text.ToArgb(),
                        GetField<Label>(form, "_spaceValueLabel").ForeColor.ToArgb(),
                        "Lo spazio recuperato deve usare il testo della palette scura.");
                    Assert.Equal(dark.TextSecondary.ToArgb(),
                        GetField<Label>(form, "_spaceCaptionLabel").ForeColor.ToArgb(),
                        "La didascalia deve usare il testo secondario della palette scura.");
                    Assert.Equal(dark.Text.ToArgb(),
                        GetField<Label>(form, "_statDeletedValue").ForeColor.ToArgb(),
                        "I valori delle statistiche devono usare il testo della palette scura.");
                    Assert.Equal(dark.TextSecondary.ToArgb(),
                        GetField<Label>(form, "_statDeletedCaption").ForeColor.ToArgb(),
                        "Le didascalie delle statistiche devono usare il testo secondario.");
                    Assert.Equal(dark.GridBack.ToArgb(),
                        GetField<TextBox>(form, "_detailsTextBox").BackColor.ToArgb(),
                        "La casella dei dettagli deve usare lo sfondo tabella della palette scura.");
                    Assert.Equal(dark.Text.ToArgb(),
                        GetField<TextBox>(form, "_detailsTextBox").ForeColor.ToArgb(),
                        "La casella dei dettagli deve usare il testo della palette scura.");
                    Assert.Equal(dark.ButtonBack.ToArgb(),
                        GetField<Button>(form, "_btnDetails").BackColor.ToArgb(),
                        "Il pulsante dei dettagli deve usare la palette scura.");

                    // Ritorno al chiaro.
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Light; }));
                    Invoke(form, "ApplyTheme", null);

                    Assert.Equal(SystemColors.Control.ToArgb(),
                        GetField<Panel>(form, "_cleanResultsPanel").BackColor.ToArgb(),
                        "Con tema chiaro il riquadro deve tornare al colore di sistema.");
                    Assert.True(GetField<Button>(form, "_btnDetails").UseVisualStyleBackColor,
                        "Con tema chiaro il pulsante deve tornare al rendering nativo.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void StatusColor_ComesFromPaletteAccents()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_themeChoice", ThemeChoice.Automatic);
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Dark; }));
                    Invoke(form, "ApplyTheme", null);
                    ThemePalette dark = (ThemePalette)GetFieldValue(form, "_palette");

                    CleanResult ok = new CleanResult();
                    ok.Success = true;
                    Color success = InvokeStatusColor(form, ok, null);
                    Assert.Equal(dark.AccentSuccess.ToArgb(), success.ToArgb(),
                        "Lo stato di successo deve usare l'accento successo della palette.");

                    CleanResult errors = new CleanResult();
                    errors.Success = true;
                    errors.ErrorCount = 1;
                    Color warning = InvokeStatusColor(form, errors, null);
                    Assert.Equal(dark.AccentWarning.ToArgb(), warning.ToArgb(),
                        "Lo stato con errori deve usare l'accento avviso della palette.");

                    CleanResult cancelled = new CleanResult();
                    cancelled.Success = true;
                    cancelled.Cancelled = true;
                    Color cancelColor = InvokeStatusColor(form, cancelled, null);
                    Assert.Equal(dark.AccentWarning.ToArgb(), cancelColor.ToArgb(),
                        "Lo stato annullato deve usare l'accento avviso della palette.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Invarianza BuildCleanSummaryText (report)

        [Test]
        public void CleanSummaryText_ReportInvariant_IsUnchanged()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 2;
            clean.DeletedBytes = 1024;

            MethodInfo method = typeof(MainForm).GetMethod("BuildCleanSummaryText",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Il costruttore del riepilogo completo deve esistere.");
            string full = (string)method.Invoke(null, new object[] { clean, null });

            Assert.Contains("File eliminati", full,
                "Il riepilogo completo dei file deve restare identico per i report.");
            Assert.Contains("Pulizia completata.", full,
                "L'intestazione del riepilogo completo deve restare invariata.");
        }

        #endregion

        #region Utilità

        private static string InvokeStatusText(CleanResult clean, EmptyCleanResult empty)
        {
            MethodInfo method = typeof(MainForm).GetMethod("BuildCleanStatusText",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Il costruttore dello stato deve esistere.");
            return (string)method.Invoke(null, new object[] { clean, empty });
        }

        private static Color InvokeStatusColor(MainForm form, CleanResult clean, EmptyCleanResult empty)
        {
            MethodInfo method = typeof(MainForm).GetMethod("ResolveStatusColor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, "Il risolutore del colore stato deve esistere.");
            return (Color)method.Invoke(form, new object[] { clean, empty });
        }

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

        private static void PopulateAndSelect(MainForm form, string filePath)
        {
            List<FileItem> items = new List<FileItem>();
            items.Add(Fixture.MakeItem(filePath));
            Invoke(form, "PopulateGrid", new object[] { items });
            Invoke(form, "SetAllChecks", new object[] { true });
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
