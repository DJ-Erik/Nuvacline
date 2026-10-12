using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CleanSlate.Core;
using CleanSlate.UI;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Test della finestra Informazioni, del menu Impostazioni e della preferenza di
    /// conservazione dei report (KeepReports).
    ///
    /// Coprono i requisiti obbligatori: pulsante ?, finestra modale con versione e
    /// contenuti attesi, menu con spunta "Conserva i report generati", persistenza e
    /// ripristino della preferenza, valori non validi che ricadono sul predefinito
    /// (conservazione attiva), generazione automatica condizionata, generazione su
    /// richiesta sempre disponibile, nessuna cancellazione retroattiva dei report,
    /// gestione degli errori di salvataggio e regressioni dell'etichetta del report.
    ///
    /// Nessun test tocca il vero %LOCALAPPDATA%\CleanSlate\settings.txt né la reale
    /// cartella dei report: la persistenza usa file temporanei e la cartella dei report
    /// viene inoltrata tramite i seam dedicati.
    /// </summary>
    public sealed class InfoSettingsTests
    {
        private const string DefaultReportLabel =
            "Report: nessuno generato. Viene scritto al termine di un'analisi o di una pulizia.";

        #region Finestra Informazioni

        [Test]
        public void InfoText_ContainsVersionAndEverySection()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string text = (string)Invoke(form, "BuildInfoText", new object[] { });
                    Assert.Contains("CleanSlate " + AppInfo.VersionText, text,
                        "Il testo deve riportare la versione canonica.");
                    Assert.Contains("Scopo", text, "Manca la sezione Scopo.");
                    Assert.Contains("Gratuito ed essenziale", text, "Manca la sezione Gratuito ed essenziale.");
                    Assert.Contains("Privacy", text, "Manca la sezione Privacy.");
                    Assert.Contains("Funzionamento", text, "Manca la sezione Funzionamento.");
                    Assert.Contains("Avvertenza", text, "Manca la sezione Avvertenza.");
                    Assert.Contains("I file eliminati non sono recuperabili.", text,
                        "L'avvertenza sui file eliminati è obbligatoria.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void InfoText_DeclaresOfflinePrivacyGuarantees()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string text = (string)Invoke(form, "BuildInfoText", new object[] { });
                    Assert.Contains("Nessuna connessione di rete", text,
                        "Deve dichiarare l'assenza di connessioni di rete.");
                    Assert.Contains("nessun dato raccolto", text, "Deve dichiarare l'assenza di raccolta dati.");
                    Assert.Contains("nessuna modifica al registro", text,
                        "Deve dichiarare l'assenza di modifiche al registro.");
                    Assert.Contains("nessun avvio automatico", text,
                        "Deve dichiarare l'assenza di avvio automatico.");
                    Assert.DoesNotContain("telemetria", text,
                        "Il testo non deve promettere funzioni di telemetria.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void InfoForm_IsAModalDialogWithBodyTextAndOkButton()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Form dialog = (Form)Invoke(form, "BuildInfoForm", new object[] { });
                    using (dialog)
                    {
                        Assert.Equal("dlgInfo", dialog.Name, "Il dialogo deve avere il nome atteso.");
                        Assert.Equal("Informazioni su CleanSlate", dialog.Text, "Titolo del dialogo atteso.");
                        Assert.Equal(FormBorderStyle.FixedDialog, dialog.FormBorderStyle,
                            "Il dialogo deve essere modale e non ridimensionabile.");
                        Assert.False(dialog.MaximizeBox, "Il dialogo non deve essere ingrandibile.");
                        Assert.False(dialog.MinimizeBox, "Il dialogo non deve essere riducibile.");
                        Assert.Equal(FormStartPosition.CenterParent, dialog.StartPosition,
                            "Il dialogo deve aprirsi centrato sul genitore.");
                        Assert.NotNull(dialog.AcceptButton, "Il pulsante OK deve essere il pulsante di accettazione.");
                        Assert.NotNull(dialog.CancelButton, "Il pulsante OK deve chiudere anche con Esc.");

                        Control[] bodies = dialog.Controls.Find("lblInfoBody", true);
                        Assert.Equal(1, bodies.Length, "Il corpo del testo deve esistere esattamente una volta.");
                        Label body = bodies[0] as Label;
                        Assert.NotNull(body, "Il corpo del testo deve essere un'etichetta.");
                        Assert.Contains(AppInfo.VersionText, body.Text, "Il corpo deve contenere la versione.");
                        Assert.Contains("Nessuna connessione di rete", body.Text,
                            "Il corpo deve contenere la garanzia offline.");
                        Assert.True(body.Bottom <= dialog.ClientSize.Height,
                            "Il testo deve essere contenuto nella finestra (corpo: " + body.Bottom
                            + ", finestra: " + dialog.ClientSize.Height + ").");
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void InfoButton_ExistsIsWiredAndInvokesTheSeam()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Button button = GetField<Button>(form, "_btnInfo");
                    Assert.NotNull(button, "Il pulsante Informazioni deve esistere.");
                    Assert.Equal("btnInfo", button.Name, "Nome del pulsante atteso.");
                    Assert.Equal("?", button.Text, "Il pulsante deve mostrare la chiocciola.");
                    Assert.True(button.Anchor.HasFlag(AnchorStyles.Right),
                        "Il pulsante deve restare agganciato al bordo destro.");

                    bool invoked = false;
                    SetField(form, "_showInfoDialog", new Action(delegate { invoked = true; }));
                    ClickControl(form, button, "OnInfoClick");
                    Assert.True(invoked, "Il clic sul pulsante deve invocare la finestra Informazioni.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void Window_HasNoSubtitleLabelAndKeepsOperationalFootnote()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Control[] subtitles = form.Controls.Find("lblSubtitle", true);
                    Assert.Equal(0, subtitles.Length,
                        "L'etichetta descrittiva sotto il titolo deve essere rimossa.");

                    Label footnote = GetField<Label>(form, "_footnoteLabel");
                    Assert.NotNull(footnote, "La nota a piè di pagina deve esistere.");
                    Assert.Contains("Prima della pulizia puoi deselezionare", footnote.Text,
                        "La nota deve ricordare la deselezione libera.");
                    Assert.Contains("conferma", footnote.Text,
                        "La nota deve ricordare la conferma obbligatoria.");
                    Assert.Contains("non sono recuperabili", footnote.Text,
                        "La nota deve ricordare che i file eliminati non sono recuperabili.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Versione

        [Test]
        public void CanonicalVersion_Is_0_3_6_AndUsedEverywhere()
        {
            Assert.Equal("0.3.6", AppInfo.VersionText,
                "La versione canonica del prodotto deve essere 0.3.6.");

            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Assert.Equal("CleanSlate 0.3.6", form.Text,
                        "Il titolo della finestra deve riportare la versione 0.3.6.");
                    Label title = GetField<Label>(form, "_titleLabel");
                    Assert.Equal("CleanSlate 0.3.6", title.Text,
                        "L'etichetta del titolo deve riportare la versione 0.3.6.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Persistenza della preferenza

        [Test]
        public void KeepReports_MissingOrInvalidValue_FallsBackToActive()
        {
            string path = TempFile("keep-missing");
            try
            {
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Un file di impostazioni assente deve produrre la conservazione attiva.");

                File.WriteAllText(path, string.Empty);
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Un file vuoto deve produrre la conservazione attiva.");

                File.WriteAllText(path, "# soltanto un commento\r\nMinAgeHours=168");
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Un file senza la chiave KeepReports deve produrre la conservazione attiva.");

                File.WriteAllText(path, "KeepReports=forse");
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Un valore non valido deve ricadere sulla conservazione attiva.");

                File.WriteAllText(path, "KeepReports=0");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "Il valore 0 deve spegnere la conservazione.");

                File.WriteAllText(path, "KeepReports=FALSE");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "Il valore false deve spegnere la conservazione (maiuscole ammesse).");

                File.WriteAllText(path, "KeepReports=1");
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Il valore 1 deve attivare la conservazione.");

                File.WriteAllText(path, "KeepReports=true");
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Il valore true deve attivare la conservazione.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void KeepReports_RoundTrip_PersistsAndRestores()
        {
            string path = TempFile("keep-roundtrip");
            try
            {
                Assert.True(ScanSettings.SaveKeepReportsTo(path, false),
                    "Il salvataggio della preferenza deve riuscire.");
                Assert.True(File.Exists(path), "Il file di impostazioni deve essere stato creato.");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "Dopo il salvataggio la conservazione deve risultare disattiva.");

                Assert.True(ScanSettings.SaveKeepReportsTo(path, true),
                    "Il ripristino della preferenza deve riuscire.");
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "Dopo il secondo salvataggio la conservazione deve risultare attiva.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void SettingsKeys_AreWrittenIndependentlyAndPreserveOtherLines()
        {
            string path = TempFile("keep-independent");
            try
            {
                Assert.True(ScanSettings.SaveTo(path, 6), "Il salvataggio della soglia deve riuscire.");
                Assert.True(ScanSettings.SaveKeepReportsTo(path, false),
                    "Il salvataggio della conservazione deve riuscire.");

                Assert.Equal(6, ScanSettings.LoadFrom(path).Hours,
                    "La soglia salvata deve essere 6 ore.");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "La conservazione salvata deve restare disattiva.");

                // La soglia riscritta non deve azzerare la conservazione.
                Assert.True(ScanSettings.SaveTo(path, 12), "Il secondo salvataggio della soglia deve riuscire.");
                Assert.Equal(12, ScanSettings.LoadFrom(path).Hours, "La soglia deve essere 12 ore.");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "Salvare la soglia non deve azzerare la conservazione.");

                string content = File.ReadAllText(path);
                Assert.Contains("MinAgeHours=12", content, "La chiave della soglia deve essere presente.");
                Assert.Contains("KeepReports=0", content, "La chiave della conservazione deve essere presente.");
                Assert.Equal(1, CountOccurrences(content, "MinAgeHours="),
                    "La chiave della soglia non deve essere duplicata.");
                Assert.Equal(1, CountOccurrences(content, "KeepReports="),
                    "La chiave della conservazione non deve essere duplicata.");

                // Le righe sconosciute (commenti e altre chiavi) devono sopravvivere.
                File.AppendAllText(path, "# nota utente\r\ncodice=abc\r\n");
                Assert.True(ScanSettings.SaveKeepReportsTo(path, true),
                    "Il salvataggio su file con altre righe deve riuscire.");
                content = File.ReadAllText(path);
                Assert.Contains("# nota utente", content, "Il commento deve essere preservato.");
                Assert.Contains("codice=abc", content, "Una chiave sconosciuta deve essere preservata.");
                Assert.Equal(12, ScanSettings.LoadFrom(path).Hours,
                    "La soglia deve sopravvivere al salvataggio della conservazione.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void SaveKeepReports_OnUnusablePath_ReturnsFalseWithoutThrowing()
        {
            Assert.False(ScanSettings.SaveKeepReportsTo("percorso<>non|valido.txt", false),
                "Un percorso non valido deve produrre false senza eccezioni.");
        }

        #endregion

        #region Menu Impostazioni

        [Test]
        public void SettingsMenu_HasCheckableItemInitialisedFromThePreference()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string settings = TempFile("keep-menu-init");
                    try
                    {
                        ScanSettings.SaveKeepReportsTo(settings, true);
                        PointSeams(form, settings, null);
                        Invoke(form, "LoadKeepReportsSetting", new object[] { });

                        ToolStripMenuItem item = GetField<ToolStripMenuItem>(form, "_miKeepReports");
                        Assert.NotNull(item, "La voce di conservazione deve esistere.");
                        Assert.Equal("Conserva i report generati", item.Text,
                            "Testo della voce di conservazione atteso.");
                        Assert.Equal("miKeepReports", item.Name, "Nome della voce atteso.");
                        Assert.True(item.CheckOnClick, "La voce deve commutare con un clic.");
                        Assert.True(item.Checked, "La spunta deve riflettere la preferenza attiva.");
                        Assert.True((bool)GetFieldValue(form, "_keepReports"),
                            "Lo stato interno deve riflettere la preferenza attiva.");

                        ToolStripMenuItem open = GetField<ToolStripMenuItem>(form, "_miOpenReportsFolder");
                        Assert.NotNull(open, "La voce di apertura cartella deve esistere.");
                        Assert.Equal("Apri cartella dei report", open.Text,
                            "Testo della voce di apertura atteso.");
                        Assert.NotNull(GetField<ToolStripSeparator>(form, "_settingsSeparator"),
                            "Il separatore del menu deve esistere.");

                        ContextMenuStrip menu = GetField<ContextMenuStrip>(form, "_settingsMenu");
                        Assert.NotNull(menu, "Il menu delle impostazioni deve esistere.");
                        Assert.Equal("menuSettings", menu.Name, "Nome del menu atteso.");
                        Assert.Equal(3, menu.Items.Count, "Il menu deve contenere esattamente tre voci.");
                    }
                    finally
                    {
                        DeleteIfPresent(settings);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void TogglingKeepReports_PersistsAndChangesTheReportLabel()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string settings = TempFile("keep-toggle");
                    try
                    {
                        ScanSettings.SaveKeepReportsTo(settings, true);
                        PointSeams(form, settings, null);
                        Invoke(form, "LoadKeepReportsSetting", new object[] { });

                        ToolStripMenuItem item = GetField<ToolStripMenuItem>(form, "_miKeepReports");
                        Label report = GetField<Label>(form, "_reportLabel");
                        Assert.Equal(DefaultReportLabel, report.Text,
                            "Con conservazione attiva l'etichetta deve restare identica a 0.2.0.");

                        item.Checked = false;
                        Invoke(form, "OnKeepReportsClick", new object[] { null, EventArgs.Empty });

                        Assert.False((bool)GetFieldValue(form, "_keepReports"),
                            "Lo stato interno deve seguire la spunta.");
                        Assert.False(ScanSettings.LoadKeepReportsFrom(settings),
                            "La preferenza deve essere persistita su disco.");
                        Assert.Contains("conservazione disattivata", report.Text,
                            "L'etichetta deve dichiarare la conservazione disattivata: " + report.Text);

                        item.Checked = true;
                        Invoke(form, "OnKeepReportsClick", new object[] { null, EventArgs.Empty });

                        Assert.True((bool)GetFieldValue(form, "_keepReports"),
                            "Lo stato interno deve tornare attivo.");
                        Assert.True(ScanSettings.LoadKeepReportsFrom(settings),
                            "La preferenza riattivata deve essere persistita.");
                        Assert.Equal(DefaultReportLabel, report.Text,
                            "Riattivata la conservazione l'etichetta deve tornare al testo predefinito.");
                    }
                    finally
                    {
                        DeleteIfPresent(settings);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void FailedSave_RevertsTheCheckboxAndNotifiesTheUser()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string notified = null;
                    SetField(form, "_notifyUser", new Action<string>(delegate(string text) { notified = text; }));
                    SetField(form, "_saveKeepReports", new Func<bool, bool>(delegate(bool keep) { return false; }));

                    // Stato iniziale impostato esplicitamente: il test non deve dipendere
                    // dal contenuto reale del file di impostazioni dell'utente.
                    SetField(form, "_keepReports", true);
                    ToolStripMenuItem item = GetField<ToolStripMenuItem>(form, "_miKeepReports");
                    item.Checked = true;
                    item.Checked = false;
                    Invoke(form, "OnKeepReportsClick", new object[] { null, EventArgs.Empty });

                    Assert.True(item.Checked,
                        "Se il salvataggio fallisce la spusta deve tornare al valore effettivo.");
                    Assert.True((bool)GetFieldValue(form, "_keepReports"),
                        "Se il salvataggio fallisce lo stato interno non deve cambiare.");
                    Assert.NotNull(notified, "L'utente deve essere informato del fallimento.");
                    Assert.Contains("Impossibile salvare", notified,
                        "Il messaggio di errore deve indicare il salvataggio non riuscito.");

                    // Anche un'eccezione durante il salvataggio non deve cambiare lo stato.
                    notified = null;
                    SetField(form, "_saveKeepReports", new Func<bool, bool>(delegate(bool keep)
                    {
                        throw new IOException("disco non disponibile");
                    }));
                    item.Checked = false;
                    Invoke(form, "OnKeepReportsClick", new object[] { null, EventArgs.Empty });

                    Assert.True(item.Checked, "Un'eccezione deve riportare la spusta al valore effettivo.");
                    Assert.True((bool)GetFieldValue(form, "_keepReports"),
                        "Un'eccezione non deve cambiare lo stato interno.");
                    Assert.NotNull(notified, "Anche in caso di eccezione l'utente deve essere informato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void OpenReportsFolderItem_VisibleOnlyWhenTheFolderExistsAndOpensIt()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string folder = Path.Combine(fx.BaseDir, "reports");
                    PointSeams(form, null, folder);

                    ToolStripMenuItem open = GetField<ToolStripMenuItem>(form, "_miOpenReportsFolder");
                    ToolStripSeparator separator = GetField<ToolStripSeparator>(form, "_settingsSeparator");
                    Invoke(form, "RefreshSettingsMenu", new object[] { });

                    Assert.False(open.Available, "Con la cartella assente la voce non deve essere presente nel menu.");
                    Assert.False(open.Enabled, "Con la cartella assente la voce non deve essere abilitata.");
                    Assert.False(separator.Available, "Con la cartella assente il separatore non deve essere presente nel menu.");

                    Directory.CreateDirectory(folder);
                    Invoke(form, "RefreshSettingsMenu", new object[] { });
                    Assert.True(open.Available, "Con la cartella esistente la voce deve essere presente nel menu.");
                    Assert.True(open.Enabled, "Con la cartella esistente la voce deve essere abilitata.");
                    Assert.True(separator.Available, "Con la cartella esistente il separatore deve essere presente nel menu.");

                    ProcessStartInfo started = null;
                    SetField(form, "_startProcess",
                        new Action<ProcessStartInfo>(delegate(ProcessStartInfo info) { started = info; }));
                    Invoke(form, "OnOpenReportsFolderClick", new object[] { null, EventArgs.Empty });

                    Assert.NotNull(started, "La cartella dei report deve essere aperta.");
                    Assert.Equal(folder, started.FileName, "Deve essere aperta la cartella dei report.");
                    Assert.True(started.UseShellExecute, "L'apertura deve passare alla shell di Windows.");

                    // Cartella sparita: nessun avvio e messaggio all'utente.
                    Directory.Delete(folder);
                    started = null;
                    string notified = null;
                    SetField(form, "_notifyUser", new Action<string>(delegate(string text) { notified = text; }));
                    Invoke(form, "OnOpenReportsFolderClick", new object[] { null, EventArgs.Empty });

                    Assert.Null(started, "Senza cartella non deve essere avviato nulla.");
                    Assert.NotNull(notified, "Senza cartella l'utente deve essere informato.");
                    Assert.Contains("non esiste", notified,
                        "Il messaggio deve spiegare che la cartella non esiste ancora.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Generazione dei report

        [Test]
        public void TurningKeepReportsOff_CancelsNothingAlreadyWritten()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string folder = Path.Combine(fx.BaseDir, "reports");
                    Directory.CreateDirectory(folder);
                    string previous = Path.Combine(folder, "cleanslate-precedente.txt");
                    File.WriteAllText(previous, "report preesistente");

                    // La preferenza viene persistita su un file temporaneo: il file
                    // reale dell'utente non deve essere toccato da questo test.
                    string settings = TempFile("keep-off");
                    try
                    {
                        ScanSettings.SaveKeepReportsTo(settings, true);
                        PointSeams(form, settings, folder);
                        Invoke(form, "LoadKeepReportsSetting", new object[] { });

                        ToolStripMenuItem item = GetField<ToolStripMenuItem>(form, "_miKeepReports");
                        item.Checked = false;
                        Invoke(form, "OnKeepReportsClick", new object[] { null, EventArgs.Empty });

                        Assert.False((bool)GetFieldValue(form, "_keepReports"),
                            "La preferenza deve essere stata disattivata.");
                        Assert.False(ScanSettings.LoadKeepReportsFrom(settings),
                            "La preferenza disattivata deve essere persistita sul file temporaneo.");
                        Assert.True(File.Exists(previous),
                            "Disattivare la conservazione non deve cancellare i report già scritti.");
                        Assert.Equal(1, Directory.GetFiles(folder).Length,
                            "La cartella dei report deve restare intatta.");
                    }
                    finally
                    {
                        DeleteIfPresent(settings);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void AutoReport_WithConservationActive_WritesExactlyOneFile()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string folder = Path.Combine(fx.BaseDir, "reports");
                    PointSeams(form, null, folder);
                    SetField(form, "_keepReports", true);
                    SetField(form, "_lastScan", MakeScan());

                    Invoke(form, "GenerateReport", new object[] { false });

                    string path = (string)GetFieldValue(form, "_lastReportPath");
                    Assert.NotNull(path, "Il percorso del report deve essere valorizzato.");
                    Assert.True(File.Exists(path), "Il file di report deve essere stato scritto.");
                    Assert.Equal(1, Directory.GetFiles(folder).Length,
                        "Deve essere scritto esattamente un report.");

                    Label report = GetField<Label>(form, "_reportLabel");
                    Assert.Contains(path, report.Text, "L'etichetta deve mostrare il percorso scritto.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void AutoReport_WithConservationOff_WritesNothingAndKeepsExistingFiles()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string folder = Path.Combine(fx.BaseDir, "reports");
                    Directory.CreateDirectory(folder);
                    string previous = Path.Combine(folder, "cleanslate-precedente.txt");
                    File.WriteAllText(previous, "report preesistente");

                    PointSeams(form, null, folder);
                    SetField(form, "_keepReports", false);
                    SetField(form, "_lastScan", MakeScan());

                    Invoke(form, "GenerateReport", new object[] { false });

                    Assert.Null(GetFieldValue(form, "_lastReportPath"),
                        "Con conservazione disattivata nessun percorso deve essere memorizzato.");
                    Assert.Equal(1, Directory.GetFiles(folder).Length,
                        "Con conservazione disattivata non deve essere scritto alcun report.");
                    Assert.True(File.Exists(previous),
                        "I report preesistenti non devono essere cancellati.");

                    Label report = GetField<Label>(form, "_reportLabel");
                    Assert.Contains("conservazione disattivata", report.Text,
                        "L'etichetta deve dichiarare la conservazione disattivata: " + report.Text);
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ExplicitReportRequest_WritesEvenWhenConservationIsOff()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string folder = Path.Combine(fx.BaseDir, "reports");
                    PointSeams(form, null, folder);
                    SetField(form, "_keepReports", false);
                    SetField(form, "_lastScan", MakeScan());

                    Invoke(form, "GenerateReport", new object[] { true });

                    string path = (string)GetFieldValue(form, "_lastReportPath");
                    Assert.NotNull(path, "La richiesta esplicita deve produrre un percorso.");
                    Assert.True(File.Exists(path),
                        "La richiesta esplicita deve scrivere il report anche con conservazione disattiva.");
                    Assert.Equal(1, Directory.GetFiles(folder).Length,
                        "La richiesta esplicita deve produrre un solo file.");

                    Label report = GetField<Label>(form, "_reportLabel");
                    Assert.Contains(path, report.Text, "L'etichetta deve mostrare il percorso generato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ReportLabel_KeepsTheDefaultTextWhenConservationIsActive()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string settings = TempFile("keep-label");
                    try
                    {
                        ScanSettings.SaveKeepReportsTo(settings, true);
                        PointSeams(form, settings, null);
                        Invoke(form, "LoadKeepReportsSetting", new object[] { });

                        Label report = GetField<Label>(form, "_reportLabel");
                        Assert.Equal(DefaultReportLabel, report.Text,
                            "Il testo predefinito dell'etichetta deve restare invariato per compatibilità.");
                    }
                    finally
                    {
                        DeleteIfPresent(settings);
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Utilità

        /// <summary>Scansione minima valida usata per produrre un report.</summary>
        private static ScanResult MakeScan()
        {
            ScanResult scan = new ScanResult();
            scan.Success = true;
            scan.FilesScanned = 1;
            scan.FilesEligible = 1;
            scan.EstimatedBytes = 10;
            scan.Duration = TimeSpan.FromSeconds(0.1);
            return scan;
        }

        /// <summary>
        /// Puntamento dei seam di persistenza e della cartella dei report.
        /// I parametri sono opzionali: ciò che non viene indicato resta invariato.
        /// </summary>
        private static void PointSeams(MainForm form, string settings, string reportsFolder)
        {
            if (!string.IsNullOrEmpty(settings))
            {
                SetField(form, "_loadKeepReports",
                    new Func<bool>(delegate { return ScanSettings.LoadKeepReportsFrom(settings); }));
                SetField(form, "_saveKeepReports",
                    new Func<bool, bool>(delegate(bool keep) { return ScanSettings.SaveKeepReportsTo(settings, keep); }));
            }
            if (!string.IsNullOrEmpty(reportsFolder))
            {
                string folder = reportsFolder;
                SetField(form, "_reportsFolder", new Func<string>(delegate { return folder; }));
            }
        }

        private static string TempFile(string prefix)
        {
            return Path.Combine(Path.GetTempPath(),
                "cleanslate-" + prefix + "-" + Guid.NewGuid().ToString("N") + ".txt");
        }

        private static void DeleteIfPresent(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0;
            int index = 0;
            while (index < text.Length)
            {
                int found = text.IndexOf(token, index, StringComparison.Ordinal);
                if (found < 0) break;
                count++;
                index = found + token.Length;
            }
            return count;
        }

        /// <summary>
        /// Simula il clic su un pulsante senza mostrare la finestra: invoca
        /// Control.OnClick, che distribuisce esattamente gli handler registrati
        /// sull'evento Click. Se l'API non fosse disponibile si ricade sull'handler
        /// nominalmente.
        /// </summary>
        private static void ClickControl(MainForm form, Control control, string handlerName)
        {
            MethodInfo method = typeof(Control).GetMethod("OnClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method != null)
            {
                try
                {
                    method.Invoke(control, new object[] { EventArgs.Empty });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
                return;
            }
            Invoke(form, handlerName, new object[] { null, EventArgs.Empty });
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
