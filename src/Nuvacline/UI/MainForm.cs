// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nuvacline.Core;

namespace Nuvacline.UI
{
    /// <summary>
    /// Finestra principale di Nuvacline.
    /// La UI non blocca mai il thread di esecuzione: le operazioni lunghe girano in background
    /// e sono annullabili in modo cooperativo.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const int ProgressThrottleMs = 120;
        private const int GridTopScan = 238;
        private const int GridTopClean = 350;
        private const int ReportTopScan = 216;
        private const int ReportTopClean = 326;
        private const int BottomReserved = 46;
        private const int DetailsOpenHeight = 160;
        private const int CleanResultsHeight = 160;

        private readonly TempFolderScanner _scanner = new TempFolderScanner();
        private readonly TempFileCleaner _cleaner = new TempFileCleaner();
        private readonly EmptyFolderCleaner _folderCleaner = new EmptyFolderCleaner();

        private Label _titleLabel;
        private Button _btnAnalyze;
        private Button _btnClean;
        private Button _btnCancel;
        private Button _btnReport;
        private Button _btnSelectAll;
        private Button _btnDeselectAll;
        private Button _btnInfo;
        private Button _btnSettings;
        private Button _btnDetails;
        private Label _lblSelection;
        private Label _statusLabel;
        private ProgressBar _progress;
        private Label _summaryLabel;
        private Label _reportLabel;
        private DataGridView _grid;
        private Label _footnoteLabel;
        private ComboBox _cboThreshold;
        private Label _lblThreshold;
        private ComboBox _cboTheme;
        private Label _lblTheme;
        private ToolTip _thresholdToolTip;

        private Panel _cleanResultsPanel;
        private Label _cleanStatusLabel;
        private Label _spaceValueLabel;
        private Label _spaceCaptionLabel;
        private FlowLayoutPanel _statsFlow;
        private Label _statDeletedCaption;
        private Label _statDeletedValue;
        private Label _statSkippedCaption;
        private Label _statSkippedValue;
        private Label _statErrorCaption;
        private Label _statErrorValue;
        private Panel _statFolderCell;
        private Label _statFolderCaption;
        private Label _statFolderValue;
        private Panel _detailsPanel;
        private TextBox _detailsTextBox;
        private bool _detailsOpen;

        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _loadingGrid;
        private bool _loadingThreshold;
        private int _lastProgressTick;
        private ScanResult _lastScan;
        private CleanResult _lastClean;
        private EmptyCleanResult _lastEmptyClean;
        private string _currentRoot;
        private string _lastReportPath;
        private ScanThresholdOption _currentThreshold;
        private TimeSpan _lastScanMinAge;

        private ContextMenuStrip _rowMenu;
        private FileItem _rowMenuTarget;
        private bool[] _rowMenuCheckSnapshot;
        private ContextMenuStrip _settingsMenu;
        private ToolStripMenuItem _miKeepReports;
        private ToolStripMenuItem _miRemoveEmptyFolders;
        private ToolStripSeparator _settingsSeparator;
        private ToolStripMenuItem _miOpenReportsFolder;
        private bool _keepReports = ScanSettings.DefaultKeepReports;
        private bool _removeEmptyFolders = ScanSettings.DefaultRemoveEmptyFolders;
        private Action<string> _notifyUser;
        private Action<string> _setClipboard;
        private Action<ProcessStartInfo> _startProcess;
        private Func<ScanThresholdOption> _loadSettings;
        private Action<int> _saveSettings;
        private Func<bool> _loadKeepReports;
        private Func<bool, bool> _saveKeepReports;
        private Func<bool> _loadRemoveEmptyFolders;
        private Func<bool, bool> _saveRemoveEmptyFolders;
        private Func<ThemeChoice> _loadThemeChoice;
        private Func<ThemeChoice, bool> _saveThemeChoice;
        private Func<string> _reportsFolder;
        private Action _showInfoDialog;
        private Action _onDetailsToggle;
        private Func<AppTheme> _detectTheme;
        private Func<string, bool> _confirmClean;
        private Func<int, bool> _confirmRemoveEmptyFolders;
        private Func<TempPathResult> _getAuthorizedRoot;
        private Func<string, string, MessageBoxIcon, bool> _showMessage;
        private ThemePalette _palette = ThemePalette.For(AppTheme.Light);
        private AppTheme _currentTheme = AppTheme.Light;
        private ThemeChoice _themeChoice = ScanSettings.DefaultThemeChoice;
        private bool _loadingTheme;

        public MainForm()
        {
            BuildUi();
            _notifyUser = delegate(string text)
            {
                ShowMessageOwner(text, "Nuvacline", MessageBoxIcon.Warning);
            };
            _setClipboard = delegate(string text) { Clipboard.SetText(text); };
            _startProcess = delegate(ProcessStartInfo info) { Process.Start(info); };
            _loadSettings = delegate { return ScanSettings.Load(); };
            _saveSettings = delegate(int hours) { ScanSettings.Save(hours); };
            _loadKeepReports = delegate { return ScanSettings.LoadKeepReports(); };
            _saveKeepReports = delegate(bool keep) { return ScanSettings.SaveKeepReports(keep); };
            _loadRemoveEmptyFolders = delegate { return ScanSettings.LoadRemoveEmptyFolders(); };
            _saveRemoveEmptyFolders = delegate(bool remove) { return ScanSettings.SaveRemoveEmptyFolders(remove); };
            _loadThemeChoice = delegate { return ScanSettings.LoadThemeChoice(); };
            _saveThemeChoice = delegate(ThemeChoice choice) { return ScanSettings.SaveThemeChoice(choice); };
            _reportsFolder = delegate { return ReportsFolderDefault(); };
            _showInfoDialog = delegate { ShowInfoDialog(); };
            _onDetailsToggle = delegate { ToggleDetailsPanel(); };
            _detectTheme = delegate { return WindowsTheme.Detect(); };
            _getAuthorizedRoot = delegate { return TempPathProvider.GetAuthorizedRoot(); };
            // I dialoghi di conferma restano reali (default identico alla 0.3.6):
            // i test li sostituiscono con seam deterministici, mai con timeout.
            _confirmClean = delegate(string question)
            {
                return MessageBox.Show(this, question, "Conferma pulizia",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            };
            _confirmRemoveEmptyFolders = delegate(int candidateCount)
            {
                return MessageBox.Show(this, BuildEmptyFolderQuestion(candidateCount), "Conferma cartelle vuote",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            };
            _showMessage = delegate(string messageText, string title, MessageBoxIcon icon)
            {
                MessageBox.Show(this, messageText, title, MessageBoxButtons.OK, icon);
                return true;
            };
            LoadThemeChoiceSetting();
            ApplyTheme();
            LoadThresholdSetting();
            LoadKeepReportsSetting();
            LoadRemoveEmptyFoldersSetting();
            UpdateSummaryLabel();
            UpdateReportLabel();
            UpdateSelectionState();
            SetBusy(false);
            SetStatus("Pronto.");
        }

        #region Costruzione interfaccia

        private void BuildUi()
        {
            Text = "Nuvacline " + AppInfo.VersionText;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(800, 600);
            Font = SystemFonts.MessageBoxFont;
            KeyPreview = false;

            // Icona della finestra (e del taskbar): e' la stessa icona dell'eseguibile,
            // incorporata in fase di compilazione con /win32icon. Uso puramente cosmetico.
            Bitmap logoBitmap = null;
            try
            {
                Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null)
                {
                    Icon = appIcon;
                    logoBitmap = appIcon.ToBitmap();
                }
            }
            catch (Exception)
            {
                // L'assenza dell'icona non deve mai impedire l'avvio.
            }

            PictureBox logoBox = new PictureBox();
            logoBox.Name = "picLogo";
            logoBox.Location = new Point(14, 8);
            logoBox.Size = new Size(32, 32);
            logoBox.SizeMode = PictureBoxSizeMode.Normal;
            if (logoBitmap != null) logoBox.Image = logoBitmap;
            Controls.Add(logoBox);

            _titleLabel = new Label();
            _titleLabel.Name = "lblTitle";
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(54, 13);
            _titleLabel.Font = new Font(Font.FontFamily, 14f, FontStyle.Bold);
            _titleLabel.Text = "Nuvacline " + AppInfo.VersionText;
            Controls.Add(_titleLabel);

            // Comandi di accesso: Informazioni (?) e Impostazioni (ingranaggio).
            // Sono collocati in alto a destra, fuori dalla riga dei comandi di analisi,
            // e restano agganciati al bordo destro durante il ridimensionamento.
            _btnInfo = CreateIconButton("btnInfo", "?", 864, 10, OnInfoClick);
            _btnSettings = CreateIconButton("btnSettings", "⚙", 898, 10, OnSettingsClick);

            // Selettore del tema: segue Windows in automatico oppure forza il
            // chiaro/scuro dell'applicazione. La preferenza e' persistita in
            // ScanSettings e viene caricata nel costruttore prima del primo
            // ApplyTheme, cosi' l'avvio rispetta gia' la scelta dell'utente.
            _lblTheme = new Label();
            _lblTheme.Name = "lblTheme";
            _lblTheme.AutoSize = false;
            _lblTheme.Location = new Point(620, 12);
            _lblTheme.Size = new Size(44, 18);
            _lblTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblTheme.TextAlign = ContentAlignment.MiddleLeft;
            _lblTheme.Text = "Tema:";
            Controls.Add(_lblTheme);

            _cboTheme = new ComboBox();
            _cboTheme.Name = "cboTheme";
            _cboTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboTheme.Location = new Point(668, 8);
            _cboTheme.Size = new Size(140, 24);
            _cboTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _cboTheme.Items.Add("Automatico");
            _cboTheme.Items.Add("Chiaro");
            _cboTheme.Items.Add("Scuro");
            _cboTheme.SelectedIndex = 0;
            Controls.Add(_cboTheme);

            _btnAnalyze = CreateButton("btnAnalyze", "Analizza", 14, 78, 110, OnAnalyzeClick);
            _btnClean = CreateButton("btnClean", "PULISCI SELEZIONATI", 132, 78, 160, OnCleanClick);
            _btnClean.Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold);
            _btnCancel = CreateButton("btnCancel", "Annulla", 300, 78, 100, OnCancelClick);
            _btnReport = CreateButton("btnReport", "Apri report", 408, 78, 120, OnReportClick);

            // Comandi di selezione massiva: agiscono solo sugli spunti della griglia,
            // mai sui file su disco e mai bypassando la conferma di pulizia.
            _btnSelectAll = CreateButton("btnSelectAll", "Seleziona tutto", 536, 78, 110, OnSelectAllClick);
            _btnDeselectAll = CreateButton("btnDeselectAll", "Deseleziona tutto", 654, 78, 120, OnDeselectAllClick);

            _lblSelection = new Label();
            _lblSelection.Name = "lblSelection";
            _lblSelection.AutoSize = false;
            _lblSelection.Location = new Point(782, 78);
            _lblSelection.Size = new Size(144, 30);
            _lblSelection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblSelection.AutoEllipsis = true;
            _lblSelection.TextAlign = ContentAlignment.MiddleLeft;
            _lblSelection.Text = "Nessun file selezionato";
            Controls.Add(_lblSelection);

            _statusLabel = new Label();
            _statusLabel.Name = "lblStatus";
            _statusLabel.AutoSize = false;
            _statusLabel.Location = new Point(14, 112);
            _statusLabel.Size = new Size(ClientSize.Width - 28 - 260, 20);
            _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _statusLabel.Text = "Pronto.";
            Controls.Add(_statusLabel);

            // Soglia d'età minima: modificabile dall'utente, persistita in ScanSettings.
            // La modifica NON avvia analisi né pulizia; vale dalla prossima scansione.
            _lblThreshold = new Label();
            _lblThreshold.Name = "lblThreshold";
            _lblThreshold.AutoSize = false;
            _lblThreshold.Location = new Point(ClientSize.Width - 254, 114);
            _lblThreshold.Size = new Size(80, 18);
            _lblThreshold.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblThreshold.Text = "Età minima:";
            Controls.Add(_lblThreshold);

            _cboThreshold = new ComboBox();
            _cboThreshold.Name = "cboThreshold";
            _cboThreshold.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboThreshold.Location = new Point(ClientSize.Width - 170, 111);
            _cboThreshold.Size = new Size(156, 24);
            _cboThreshold.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(_cboThreshold);

            _thresholdToolTip = new ToolTip();
            _thresholdToolTip.SetToolTip(_lblThreshold,
                "Età minima richiesta perché un file sia proposto dalla scansione. "
                + "\"Nessuna soglia temporale\" include anche i file appena creati.");
            _thresholdToolTip.SetToolTip(_cboThreshold,
                "Età minima richiesta perché un file sia proposto dalla scansione. "
                + "Modificare l'impostazione non avvia alcuna operazione: il nuovo valore "
                + "entra in vigore alla prossima analisi. \"Nessuna soglia temporale\" "
                + "include anche i file appena creati.");
            _thresholdToolTip.SetToolTip(_cboTheme,
                "Tema dell'interfaccia: Automatico segue le impostazioni di Windows "
                + "aggiornandosi in tempo reale, Chiaro e Scuro forzano il tema "
                + "dell'applicazione e restano salvati.");
            _cboThreshold.SelectedIndexChanged += OnThresholdChanged;
            _cboTheme.SelectedIndexChanged += OnThemeChanged;

            // L'ingranaggio usa un carattere tipografico dedicato quando disponibile;
            // il fallback del sistema non deve impedire la creazione della finestra.
            try { _btnSettings.Font = new Font("Segoe UI Symbol", 12f); }
            catch (Exception) { }
            _thresholdToolTip.SetToolTip(_btnInfo,
                "Informazioni su Nuvacline: scopo, privacy e avvertenze.");
            _thresholdToolTip.SetToolTip(_btnSettings,
                "Impostazioni: conservazione dei report e apertura della cartella dei report.");
            BuildSettingsMenu();

            _progress = new ProgressBar();
            _progress.Name = "progress";
            _progress.Location = new Point(14, 136);
            _progress.Size = new Size(ClientSize.Width - 28, 20);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Value = 0;
            Controls.Add(_progress);

            _summaryLabel = new Label();
            _summaryLabel.Name = "lblSummary";
            _summaryLabel.AutoSize = false;
            _summaryLabel.Location = new Point(14, 162);
            _summaryLabel.Size = new Size(ClientSize.Width - 28, 52);
            _summaryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _summaryLabel.AutoEllipsis = true;
            _summaryLabel.Text = "Nessuna analisi eseguita.";
            Controls.Add(_summaryLabel);

            BuildCleanResultsPanel();

            _reportLabel = new Label();
            _reportLabel.Name = "lblReportPath";
            _reportLabel.AutoSize = false;
            _reportLabel.Location = new Point(14, ReportTopScan);
            _reportLabel.Size = new Size(ClientSize.Width - 28, 16);
            _reportLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _reportLabel.AutoEllipsis = true;
            _reportLabel.Text = "Report: nessuno generato.";
            Controls.Add(_reportLabel);

            _grid = new DataGridView();
            _grid.Name = "gridResults";
            _grid.AccessibleName = "Nessun elemento in elenco";
            _grid.Text = "Nessun elemento in elenco";
            _grid.Location = new Point(14, GridTopScan);
            _grid.Size = new Size(ClientSize.Width - 28, ClientSize.Height - GridTopScan - BottomReserved);
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = 26;
            BuildGridColumns();
            _grid.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
            _grid.CellValueChanged += OnGridCellValueChanged;
            _grid.CellMouseDown += OnGridCellMouseDown;
            BuildRowContextMenu();
            Controls.Add(_grid);

            BuildDetailsPanel();

            _footnoteLabel = new Label();
            _footnoteLabel.Name = "lblFootnote";
            _footnoteLabel.AutoSize = false;
            _footnoteLabel.Location = new Point(14, ClientSize.Height - 40);
            _footnoteLabel.Size = new Size(ClientSize.Width - 28, 34);
            _footnoteLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _footnoteLabel.Text = "Prima della pulizia puoi deselezionare qualsiasi file. "
                + "La cancellazione richiede una conferma e i file eliminati non sono recuperabili.";
            Controls.Add(_footnoteLabel);
        }

        /// <summary>
        /// Costruisce il riquadro dei risultati della pulizia: stato sintetico,
        /// spazio recuperato in evidenza, statistiche secondarie separate e il
        /// pulsante «Mostra dettagli» in basso a sinistra. Il riquadro resta
        /// nascosto finché non viene eseguita almeno una pulizia.
        /// </summary>
        private void BuildCleanResultsPanel()
        {
            _cleanResultsPanel = new Panel();
            _cleanResultsPanel.Name = "pnlCleanResults";
            _cleanResultsPanel.Location = new Point(14, 162);
            _cleanResultsPanel.Size = new Size(ClientSize.Width - 28, CleanResultsHeight);
            _cleanResultsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cleanResultsPanel.Visible = false;
            Controls.Add(_cleanResultsPanel);

            _cleanStatusLabel = new Label();
            _cleanStatusLabel.Name = "lblCleanStatus";
            _cleanStatusLabel.AutoSize = false;
            _cleanStatusLabel.Location = new Point(8, 6);
            _cleanStatusLabel.Size = new Size(_cleanResultsPanel.Width - 16, 22);
            _cleanStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cleanStatusLabel.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
            _cleanStatusLabel.Text = string.Empty;
            _cleanResultsPanel.Controls.Add(_cleanStatusLabel);

            _spaceValueLabel = new Label();
            _spaceValueLabel.Name = "lblSpaceValue";
            _spaceValueLabel.AutoSize = false;
            _spaceValueLabel.Location = new Point(8, 30);
            _spaceValueLabel.Size = new Size(260, 36);
            _spaceValueLabel.Font = new Font(Font.FontFamily, 20f, FontStyle.Bold);
            _spaceValueLabel.Text = string.Empty;
            _cleanResultsPanel.Controls.Add(_spaceValueLabel);

            _spaceCaptionLabel = new Label();
            _spaceCaptionLabel.Name = "lblSpaceCaption";
            _spaceCaptionLabel.AutoSize = false;
            _spaceCaptionLabel.Location = new Point(8, 66);
            _spaceCaptionLabel.Size = new Size(260, 16);
            _spaceCaptionLabel.Text = "Spazio recuperato";
            _cleanResultsPanel.Controls.Add(_spaceCaptionLabel);

            _statsFlow = new FlowLayoutPanel();
            _statsFlow.Name = "flowStats";
            _statsFlow.FlowDirection = FlowDirection.LeftToRight;
            _statsFlow.WrapContents = false;
            _statsFlow.AutoSize = false;
            _statsFlow.Location = new Point(8, 84);
            _statsFlow.Size = new Size(_cleanResultsPanel.Width - 16, 36);
            _statsFlow.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _statsFlow.Padding = new Padding(0);
            _cleanResultsPanel.Controls.Add(_statsFlow);

            _statDeletedCaption = new Label();
            _statDeletedCaption.Name = "lblStatDeletedCaption";
            _statDeletedCaption.Text = "File eliminati";
            _statDeletedValue = new Label();
            _statDeletedValue.Name = "lblStatDeletedValue";
            _statsFlow.Controls.Add(CreateStatCell("pnlStatDeleted",
                _statDeletedCaption, _statDeletedValue, 110));

            _statSkippedCaption = new Label();
            _statSkippedCaption.Name = "lblStatSkippedCaption";
            _statSkippedCaption.Text = "Elementi saltati";
            _statSkippedValue = new Label();
            _statSkippedValue.Name = "lblStatSkippedValue";
            _statsFlow.Controls.Add(CreateStatCell("pnlStatSkipped",
                _statSkippedCaption, _statSkippedValue, 110));

            _statErrorCaption = new Label();
            _statErrorCaption.Name = "lblStatErrorCaption";
            _statErrorCaption.Text = "Errori";
            _statErrorValue = new Label();
            _statErrorValue.Name = "lblStatErrorValue";
            _statsFlow.Controls.Add(CreateStatCell("pnlStatError",
                _statErrorCaption, _statErrorValue, 90));

            _statFolderCaption = new Label();
            _statFolderCaption.Name = "lblStatFolderCaption";
            _statFolderCaption.Text = "Cartelle vuote";
            _statFolderValue = new Label();
            _statFolderValue.Name = "lblStatFolderValue";
            _statFolderCell = CreateStatCell("pnlStatFolder",
                _statFolderCaption, _statFolderValue, 120);
            _statFolderCell.Visible = false;
            _statsFlow.Controls.Add(_statFolderCell);

            // Riga separata sotto le statistiche, con margine inferiore di 6 px
            // dal bordo del riquadro: prima finiva esattamente sul bordo
            // (bordo del pulsante tagliato) e si sovrapponeva ai contatori.
            _btnDetails = CreateButton("btnDetails", "Mostra dettagli", 8, 124, 140, OnDetailsClick);
            _btnDetails.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnDetails.Enabled = false;
            _cleanResultsPanel.Controls.Add(_btnDetails);
        }

        /// <summary>
        /// Crea una cella statistica compatta: didascalia sopra, valore sotto.
        /// </summary>
        private static Panel CreateStatCell(string name, Label caption, Label value, int width)
        {
            Panel cell = new Panel();
            cell.Name = name;
            cell.Size = new Size(width, 36);
            cell.Margin = new Padding(0, 0, 12, 0);

            caption.AutoSize = false;
            caption.Location = new Point(0, 0);
            caption.Size = new Size(width, 14);
            cell.Controls.Add(caption);

            value.AutoSize = false;
            value.Location = new Point(0, 16);
            value.Size = new Size(width, 18);
            value.Font = new Font(caption.Font.FontFamily, 10f, FontStyle.Bold);
            cell.Controls.Add(value);
            return cell;
        }

        /// <summary>
        /// Costruisce il pannello espandibile dei dettagli: TextBox sola
        /// lettura con barra di scorrimento, richiudibile, senza nuove
        /// scansioni né pulizie.
        /// </summary>
        private void BuildDetailsPanel()
        {
            _detailsPanel = new Panel();
            _detailsPanel.Name = "pnlDetails";
            _detailsPanel.Location = new Point(14, GridTopClean + 4);
            _detailsPanel.Size = new Size(ClientSize.Width - 28, DetailsOpenHeight);
            _detailsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _detailsPanel.Visible = false;
            _detailsPanel.Padding = new Padding(0);
            Controls.Add(_detailsPanel);

            _detailsTextBox = new TextBox();
            _detailsTextBox.Name = "txtDetails";
            _detailsTextBox.Multiline = true;
            _detailsTextBox.ReadOnly = true;
            _detailsTextBox.ScrollBars = ScrollBars.Both;
            _detailsTextBox.WordWrap = false;
            _detailsTextBox.Location = new Point(0, 0);
            _detailsTextBox.Size = new Size(_detailsPanel.Width, _detailsPanel.Height);
            _detailsTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                | AnchorStyles.Left | AnchorStyles.Right;
            _detailsPanel.Controls.Add(_detailsTextBox);
        }

        /// <summary>
        /// Mantiene la griglia e il pannello dettagli allineati durante il
        /// ridimensionamento: la griglia si estende fino al pannello (quando
        /// aperto) o fino alla riga delle note a piè di pagina.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_grid == null || _cleanResultsPanel == null) return;

            bool cleanVisible = _cleanResultsPanel.Visible;
            int gridTop = cleanVisible ? GridTopClean : GridTopScan;
            bool detailsOpen = _detailsOpen;
            int bottom = ClientSize.Height - BottomReserved
                - (detailsOpen ? DetailsOpenHeight + 4 : 0);
            _grid.Height = Math.Max(40, bottom - gridTop);
            if (detailsOpen) _detailsPanel.Top = gridTop + _grid.Height + 2;
        }

        private Button CreateButton(string name, string text, int x, int y, int width, EventHandler onClick)
        {
            Button button = new Button();
            button.Name = name;
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 30);
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            button.UseVisualStyleBackColor = true;
            button.Click += onClick;
            Controls.Add(button);
            return button;
        }

        /// <summary>
        /// Pulsante quadrato di servizio (Informazioni e Impostazioni): resta
        /// agganciato al bordo destro della finestra durante il ridimensionamento.
        /// </summary>
        private Button CreateIconButton(string name, string text, int x, int y, EventHandler onClick)
        {
            Button button = new Button();
            button.Name = name;
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(28, 28);
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.UseVisualStyleBackColor = true;
            button.Click += onClick;
            Controls.Add(button);
            return button;
        }

        private void BuildGridColumns()
        {
            DataGridViewCheckBoxColumn check = new DataGridViewCheckBoxColumn();
            check.HeaderText = "Selez.";
            check.Name = "colSelected";
            check.Width = 55;
            check.SortMode = DataGridViewColumnSortMode.Automatic;
            _grid.Columns.Add(check);

            _grid.Columns.Add(CreateTextColumn("colName", "Nome", 170));
            _grid.Columns.Add(CreateTextColumn("colPath", "Percorso", 320));
            _grid.Columns.Add(CreateTextColumn("colSize", "Dimensione", 110));
            _grid.Columns.Add(CreateTextColumn("colDate", "Ultima modifica", 135));
            _grid.Columns.Add(CreateTextColumn("colCategory", "Categoria", 190));

            _grid.Columns["colSize"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = header;
            column.Name = name;
            column.Width = width;
            column.ReadOnly = true;
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            return column;
        }

        #endregion

        #region Stato e riepiloghi

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _btnAnalyze.Enabled = !busy;
            _btnReport.Enabled = !busy;
            _btnCancel.Enabled = busy;
            _btnSelectAll.Enabled = !busy;
            _btnDeselectAll.Enabled = !busy;
            _grid.Enabled = !busy;
            UpdateSelectionState();
        }

        private int CountSelected()
        {
            int count = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                object value = row.Cells["colSelected"].Value;
                if (value is bool && (bool)value) count++;
            }
            return count;
        }

        private long SumSelectedBytes()
        {
            long total = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                object value = row.Cells["colSelected"].Value;
                if (!(value is bool) || !(bool)value) continue;
                FileItem item = row.Tag as FileItem;
                if (item != null) total += item.Length;
            }
            return total;
        }

        private void UpdateSelectionState()
        {
            if (_loadingGrid) return;
            int selected = CountSelected();
            _btnClean.Enabled = !_busy && selected > 0;
            UpdateSelectionLabel(selected);
            UpdateSummaryLabel();
        }

        private void UpdateSelectionLabel(int selected)
        {
            if (_lblSelection == null) return;
            if (selected <= 0)
            {
                _lblSelection.Text = "Nessun file selezionato";
                return;
            }
            long bytes = SumSelectedBytes();
            _lblSelection.Text = selected.ToString(CultureInfo.InvariantCulture)
                + " file selezionati (" + ByteFormatter.Format(bytes) + ")";
        }

        private void UpdateSummaryLabel()
        {
            if (_lastClean != null)
            {
                UpdateCleanResults();
                return;
            }

            ShowScanSummary();
        }

        /// <summary>
        /// Mostra il riepilogo della scansione nella normale etichetta di testo
        /// e nasconde il riquadro dei risultati della pulizia.
        /// </summary>
        private void ShowScanSummary()
        {
            if (_cleanResultsPanel != null) _cleanResultsPanel.Visible = false;
            _detailsOpen = false;
            if (_detailsPanel != null) _detailsPanel.Visible = false;
            if (_btnDetails != null)
            {
                _btnDetails.Enabled = false;
                _btnDetails.Text = "Mostra dettagli";
            }
            if (_grid != null)
            {
                _grid.Top = GridTopScan;
                _grid.Height = Math.Max(40,
                    ClientSize.Height - GridTopScan - BottomReserved);
            }
            if (_reportLabel != null) _reportLabel.Top = ReportTopScan;

            if (_lastScan != null && _lastScan.Success)
            {
                _summaryLabel.Visible = true;
                int selected = CountSelected();
                string errorText;
                if (_lastScan.Errors > 0)
                {
                    string detail = _lastScan.ErrorMessages.Count > 0 ? _lastScan.ErrorMessages[0] : "accesso negato";
                    errorText = string.Format("{0} ({1})", _lastScan.Errors, detail);
                }
                else
                {
                    errorText = "0";
                }

                _summaryLabel.Text = string.Format(
                    "File analizzati: {0} · Proposti dal filtro: {1} · Esclusi dalla soglia: {2} · Errori di lettura: {3}\r\n"
                    + "Selezionati per la pulizia: {4} · Spazio recuperabile stimato: {5} · Soglia temporale: {6}",
                    _lastScan.FilesScanned,
                    _lastScan.FilesEligible,
                    _lastScan.FilesExcludedByAge,
                    errorText,
                    selected,
                    ByteFormatter.Format(_lastScan.EstimatedBytes),
                    FormatThreshold(_lastScanMinAge));
                return;
            }

            _summaryLabel.Visible = true;
            _summaryLabel.Text = "Nessuna analisi eseguita. Soglia temporale: " + FormatThreshold(CurrentThreshold.Value) + ".";
        }

        /// <summary>
        /// Aggiorna il riquadro dei risultati della pulizia con lo stato reale
        /// dell'ultima operazione. Il riquadro sostituisce l'etichetta del
        /// riepilogo della scansione e resta visibile fino alla prossima analisi.
        /// </summary>
        private void UpdateCleanResults()
        {
            if (_lastClean == null)
            {
                ShowScanSummary();
                return;
            }

            _summaryLabel.Visible = false;
            _cleanResultsPanel.Visible = true;
            _btnDetails.Enabled = !_busy;
            _btnDetails.Text = _detailsOpen
                ? "Nascondi dettagli"
                : "Mostra dettagli";

            string status = BuildCleanStatusText(_lastClean, _lastEmptyClean);
            _cleanStatusLabel.Text = status;
            _cleanStatusLabel.ForeColor = ResolveStatusColor(_lastClean, _lastEmptyClean);

            _spaceValueLabel.Text = ByteFormatter.Format(_lastClean.DeletedBytes);
            _spaceValueLabel.ForeColor = _palette.Text;

            _spaceCaptionLabel.Text = "Spazio recuperato";
            _spaceCaptionLabel.ForeColor = _palette.TextSecondary;

            _statDeletedValue.Text = _lastClean.DeletedCount.ToString(CultureInfo.InvariantCulture);
            _statSkippedValue.Text = _lastClean.SkippedCount.ToString(CultureInfo.InvariantCulture);
            _statErrorValue.Text = _lastClean.ErrorCount.ToString(CultureInfo.InvariantCulture);

            bool folderPhaseRan = _lastEmptyClean != null && _lastEmptyClean.Success;
            _statFolderCell.Visible = folderPhaseRan;
            if (folderPhaseRan)
                _statFolderValue.Text = _lastEmptyClean.RemovedCount.ToString(CultureInfo.InvariantCulture);

            if (_detailsOpen)
                _detailsTextBox.Text = CleanupDetailsForm.BuildDetailsText(_lastClean, _lastEmptyClean);

            // Fascia visibile tra il riquadro risultati e la griglia, come in
            // modalità analisi: l'etichetta del report non deve mai stare
            // dietro la griglia opaca.
            _reportLabel.Top = ReportTopClean;
            _grid.Top = GridTopClean;
            _grid.Height = Math.Max(40,
                ClientSize.Height - GridTopClean - BottomReserved
                - (_detailsOpen ? DetailsOpenHeight + 4 : 0));
            if (_detailsOpen)
                _detailsPanel.Top = GridTopClean + _grid.Height + 2;
        }

        /// <summary>
        /// Stato sintetico della pulizia. Non dichiara un successo completo
        /// quando ci sono errori o quando l'operazione non è terminata.
        /// </summary>
        internal static string BuildCleanStatusText(CleanResult result, EmptyCleanResult emptyFolders)
        {
            if (result == null) return string.Empty;
            if (result.Cancelled) return "Pulizia annullata.";
            if (!result.Success) return "Pulizia non completata.";

            bool folderWarning = emptyFolders != null
                && (!emptyFolders.Success || emptyFolders.ErrorCount > 0);
            if (result.ErrorCount > 0 || folderWarning)
                return "Pulizia completata con avvisi.";
            return "Pulizia completata.";
        }

        /// <summary>
        /// Colore dello stato secondo il tema: successo, avviso o errore.
        /// </summary>
        private Color ResolveStatusColor(CleanResult result, EmptyCleanResult emptyFolders)
        {
            if (result == null) return _palette.Text;
            if (result.Cancelled || !result.Success) return _palette.AccentWarning;
            bool folderWarning = emptyFolders != null
                && (!emptyFolders.Success || emptyFolders.ErrorCount > 0);
            if (result.ErrorCount > 0 || folderWarning) return _palette.AccentWarning;
            return _palette.AccentSuccess;
        }

        private static void ShowMessage(string text, string title, MessageBoxIcon icon)
        {
            MessageBox.Show(text, title, MessageBoxButtons.OK, icon);
        }

        private void ShowMessageOwner(string text, string title, MessageBoxIcon icon)
        {
            if (_showMessage != null) _showMessage(text, title, icon);
            else MessageBox.Show(this, text, title, MessageBoxButtons.OK, icon);
        }

        #endregion

        #region Analisi

        private async void OnAnalyzeClick(object sender, EventArgs e)
        {
            if (_busy) return;

            TempPathResult path = _getAuthorizedRoot != null
                ? _getAuthorizedRoot()
                : TempPathProvider.GetAuthorizedRoot();
            if (!path.Success)
            {
                SetStatus("Analisi non riuscita.");
                _lastScan = null;
                UpdateSummaryLabel();
                PopulateGrid(null);
                ShowMessageOwner(path.ErrorMessage, "Nuvacline", MessageBoxIcon.Error);
                return;
            }

            _currentRoot = path.Root;
            _lastScan = null;
            _lastClean = null;
            _lastEmptyClean = null;
            _lastReportPath = null;
            UpdateReportLabel();
            PopulateGrid(null);

            CancellationTokenSource cts = new CancellationTokenSource();
            _cts = cts;

            ScanThresholdOption threshold = CurrentThreshold;
            _lastScanMinAge = threshold.Value;

            ScanOptions options = new ScanOptions();
            options.Root = path.Root;
            options.MinAge = threshold.Value;
            options.CancellationToken = cts.Token;
            options.Progress = CreateScanProgressReporter();

            SetBusy(true);
            SetStatus("Analisi in corso...");
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;

            try
            {
                ScanResult result = await Task.Run(() => _scanner.Scan(options));
                _lastScan = result;
                DisplayScanResult(result);
            }
            catch (Exception ex)
            {
                _lastScan = null;
                SetStatus("Analisi non riuscita.");
                ShowMessageOwner("Si è verificato un errore durante l'analisi: " + ex.Message, "Nuvacline", MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.MarqueeAnimationSpeed = 0;
                _progress.Value = 0;
                if (ReferenceEquals(_cts, cts)) _cts = null;
                try { cts.Dispose(); }
                catch (ObjectDisposedException) { }
                SetBusy(false);
                UpdateSummaryLabel();
                if (_lastScan != null) GenerateReport(false);
            }
        }

        private Action<ScanProgress> CreateScanProgressReporter()
        {
            return delegate(ScanProgress progress)
            {
                int now = Environment.TickCount;
                if (unchecked(now - _lastProgressTick) < ProgressThrottleMs) return;
                _lastProgressTick = now;
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action<ScanProgress>(UpdateScanProgressUi), new object[] { progress });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };
        }

        private void UpdateScanProgressUi(ScanProgress progress)
        {
            if (_busy)
            {
                SetStatus(string.Format("Analisi in corso... file trovati: {0} ({1}) · cartelle esplorate: {2}",
                    progress.FilesFound, ByteFormatter.Format(progress.BytesFound), progress.DirectoriesVisited));
            }
        }

        private void DisplayScanResult(ScanResult result)
        {
            PopulateGrid(result.Items);

            if (!result.Success)
            {
                SetStatus("Analisi non riuscita.");
                ShowMessageOwner(result.ErrorMessage ?? "Errore sconosciuto durante l'analisi.", "Nuvacline", MessageBoxIcon.Error);
                return;
            }

            if (result.Cancelled)
                SetStatus("Analisi annullata.");
            else
                SetStatus("Analisi completata.");

            if (result.Errors > 0 && result.Items.Count == 0)
            {
                string details = string.Empty;
                int limit = Math.Min(result.ErrorMessages.Count, 8);
                for (int i = 0; i < limit; i++) details += "\r\n- " + result.ErrorMessages[i];
                ShowMessageOwner(
                    "L'analisi non ha trovato file recuperabili e ha riscontrato problemi di accesso:"
                    + details
                    + "\r\n\r\nControllare i permessi sulle cartelle temporanee interessate.",
                    "Nuvacline", MessageBoxIcon.Warning);
            }
        }

        private void PopulateGrid(List<FileItem> items)
        {
            _loadingGrid = true;
            try
            {
                _grid.Rows.Clear();
                if (items == null) return;
                for (int i = 0; i < items.Count; i++)
                {
                    FileItem item = items[i];
                    int index = _grid.Rows.Add(
                        item.Selected,
                        item.Name,
                        item.FullPath,
                        ByteFormatter.Format(item.Length),
                        item.LastWriteTimeUtc.ToLocalTime().ToString("g"),
                        item.Category);
                    _grid.Rows[index].Tag = item;
                }
            }
            finally
            {
                _loadingGrid = false;
                UpdateGridAccessibleName();
                UpdateSelectionState();
            }
        }

        /// <summary>
        /// Rende il contenuto della tabella leggibile anche dai lettori di schermo e dagli
        /// strumenti di automazione, che sulla griglia WinForms non vedono le righe.
        /// </summary>
        private void UpdateGridAccessibleName()
        {
            int count = _grid.Rows.Count;
            string text = count == 0
                ? "Nessun elemento in elenco"
                : count.ToString(CultureInfo.InvariantCulture) + " elementi in elenco";
            _grid.AccessibleName = text;
            // La griglia WinForms espone i controlli tramite il bridge MSAA, che legge la
            // didascalia della finestra: senza questo assegnamento il contenuto non risulta
            // leggibile ne' dai lettori di schermo ne' dagli strumenti di automazione.
            _grid.Text = text;
        }

        #endregion

        #region Pulizia

        private string BuildCleanQuestion(List<FileItem> selected)
        {
            long bytes = 0;
            for (int i = 0; i < selected.Count; i++) bytes += selected[i].Length;
            return string.Format(
                "Eliminare {0} file per un totale stimato di {1}?\r\n\r\nI file eliminati non potranno essere ripristinati.\r\n\r\nVuole continuare?",
                selected.Count,
                ByteFormatter.Format(bytes));
        }

        /// <summary>
        /// Domanda della conferma dedicata (e separata da quella dei file) per la
        /// rimozione delle cartelle vuote. Il conteggio deriva dal sondaggio in sola
        /// lettura eseguito subito prima: l'utente decide su un'anteprima reale.
        /// </summary>
        private string BuildEmptyFolderQuestion(int candidateCount)
        {
            return string.Format(
                "Rimuovere {0} cartelle rimaste vuote sotto la cartella temporanea?\r\n\r\n"
                + "Vengono toccate solo cartelle vuote già presenti sotto la radice: la radice "
                + "non viene mai eliminata e i collegamenti (junction o simbolici) non vengono "
                + "seguiti né rimossi. I file eliminati in questa pulizia non c'entrano: questa "
                + "è una fase separata e facoltativa.\r\n\r\nVuole continuare?",
                candidateCount);
        }

        private async void OnCleanClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<FileItem> selected = GetSelectedItems();
            if (selected.Count == 0) return;

            TempPathResult path = _getAuthorizedRoot != null
                ? _getAuthorizedRoot()
                : TempPathProvider.GetAuthorizedRoot();
            if (!path.Success)
            {
                ShowMessageOwner(path.ErrorMessage, "Nuvacline", MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(_currentRoot)
                || !string.Equals(path.Root, _currentRoot, StringComparison.OrdinalIgnoreCase))
            {
                ShowMessageOwner(
                    "La cartella dei file temporanei è cambiata dall'analisi. Eseguire di nuovo l'analisi prima della pulizia.",
                    "Nuvacline", MessageBoxIcon.Warning);
                return;
            }

            string question = BuildCleanQuestion(selected);

            bool confirmed = _confirmClean != null
                ? _confirmClean(question)
                : MessageBox.Show(this, question, "Conferma pulizia",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (!confirmed) return;

            CancellationTokenSource cts = new CancellationTokenSource();
            _cts = cts;

            CleanOptions options = new CleanOptions();
            options.Root = path.Root;
            options.Items = selected;
            // Il motore ricalcola il cutoff: usa la stessa soglia della scansione
            // che ha prodotto questi risultati, non l'eventuale valore corrente
            // della combo (che vale solo dalla prossima analisi).
            options.MinAge = _lastScanMinAge;
            options.CancellationToken = cts.Token;
            options.Progress = CreateCleanProgressReporter();

            SetBusy(true);
            SetStatus("Pulizia in corso...");
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Minimum = 0;
            _progress.Maximum = Math.Max(1, selected.Count);
            _progress.Value = 0;

            try
            {
                CleanResult result = await Task.Run(() => _cleaner.Clean(options));
                _lastClean = result;
                ApplyCleanResult(result);

                // Fase opzionale delle cartelle vuote: solo con l'opzione attiva e solo
                // dopo una pulizia non annullata. Con l'opzione disattivata (default)
                // nessuna cartella viene toccata e il report resta quello della 0.3.6.
                _lastEmptyClean = null;
                if (!result.Cancelled && _removeEmptyFolders)
                {
                    EmptyCleanResult folders = await RunEmptyFolderPhaseAsync(cts.Token);
                    // Conserva il risultato anche se non riuscito: il riepilogo
                    // compatto deve poter mostrare l'errore della fase cartelle.
                    if (folders != null) _lastEmptyClean = folders;
                }

                bool folderCancelled = _lastEmptyClean != null && _lastEmptyClean.Cancelled;
                if (result.Cancelled || folderCancelled)
                    SetStatus("Pulizia annullata.");
                else
                    SetStatus("Pulizia completata.");
                ShowCleanSummary(result);
            }
            catch (Exception ex)
            {
                SetStatus("Pulizia non riuscita.");
                ShowMessageOwner("Si è verificato un errore durante la pulizia: " + ex.Message, "Nuvacline", MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Value = 0;
                if (ReferenceEquals(_cts, cts)) _cts = null;
                try { cts.Dispose(); }
                catch (ObjectDisposedException) { }
                SetBusy(false);
                UpdateSummaryLabel();
                if (_lastClean != null) GenerateReport(false);
            }
        }

        /// <summary>
        /// Fase opzionale (sempre separata dalla pulizia dei file): sondaggio in sola
        /// lettura delle cartelle vuote, conferma dedicata con il conteggio, quindi
        /// rimozione prudente. Restituisce null quando non c'è nulla da fare o
        /// l'utente ha rifiutato la conferma: in quel caso nessuna cartella viene
        /// toccata e nessuna voce di report viene aggiunta.
        /// </summary>
        private async Task<EmptyCleanResult> RunEmptyFolderPhaseAsync(CancellationToken token)
        {
            try
            {
                TempPathResult path = _getAuthorizedRoot != null
                    ? _getAuthorizedRoot()
                    : TempPathProvider.GetAuthorizedRoot();
                if (!path.Success
                    || string.IsNullOrEmpty(_currentRoot)
                    || !string.Equals(path.Root, _currentRoot, StringComparison.OrdinalIgnoreCase))
                {
                    EmptyCleanResult failed = new EmptyCleanResult();
                    failed.ErrorMessage =
                        "La cartella dei file temporanei è cambiata: la rimozione delle cartelle vuote non è stata eseguita.";
                    return failed;
                }

                SetStatus("Ricerca delle cartelle vuote in corso...");
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 30;

                EmptyFolderSurvey survey = await Task.Run(() => _folderCleaner.Survey(path.Root, token));

                if (survey.Cancelled)
                {
                    EmptyCleanResult cancelled = new EmptyCleanResult();
                    cancelled.Success = true;
                    cancelled.Cancelled = true;
                    cancelled.Root = survey.Root;
                    return cancelled;
                }
                if (!survey.Success)
                {
                    EmptyCleanResult failed = new EmptyCleanResult();
                    failed.ErrorMessage = string.IsNullOrEmpty(survey.ErrorMessage)
                        ? "Il sondaggio delle cartelle vuote non è stato completato."
                        : survey.ErrorMessage;
                    return failed;
                }
                if (survey.Candidates.Count == 0) return null;

                // Conferma dedicata ed esplicita, con il conteggio reale delle candidate.
                // Se il seam non è disponibile non viene eseguita alcuna rimozione
                // (fail-closed: mai cancellare senza conferma).
                if (_confirmRemoveEmptyFolders == null) return null;
                if (!_confirmRemoveEmptyFolders(survey.Candidates.Count)) return null;

                SetStatus("Rimozione delle cartelle vuote in corso...");
                return await Task.Run(() => _folderCleaner.Remove(path.Root, survey.Candidates, token));
            }
            catch (Exception ex)
            {
                EmptyCleanResult failed = new EmptyCleanResult();
                failed.ErrorMessage = ex.Message;
                return failed;
            }
            finally
            {
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.MarqueeAnimationSpeed = 0;
                _progress.Value = 0;
            }
        }

        private Action<CleanProgress> CreateCleanProgressReporter()
        {
            return delegate(CleanProgress progress)
            {
                int now = Environment.TickCount;
                if (unchecked(now - _lastProgressTick) < ProgressThrottleMs) return;
                _lastProgressTick = now;
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action<CleanProgress>(UpdateCleanProgressUi), new object[] { progress });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };
        }

        private void UpdateCleanProgressUi(CleanProgress progress)
        {
            if (!_busy) return;
            if (progress.Total > 0)
            {
                // La barra è configurata su conteggio elementi (Maximum = numero
                // di selezionati): si assegna Processed limitato ai bounds reali
                // del controllo. Assegnare una percentuale 0-100 con meno di 100
                // file selezionati supererebbe Maximum e lancierebbe
                // ArgumentOutOfRangeException sul thread UI.
                int value = progress.Processed;
                if (value < _progress.Minimum) value = _progress.Minimum;
                if (value > _progress.Maximum) value = _progress.Maximum;
                _progress.Value = value;
            }
            SetStatus(string.Format("Pulizia in corso... {0}/{1} · eliminati: {2}",
                progress.Processed, progress.Total, progress.Deleted));
        }

        private void ApplyCleanResult(CleanResult result)
        {
            HashSet<string> removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < result.Items.Count; i++)
            {
                CleanItemResult item = result.Items[i];
                if (item.Outcome == CleanOutcome.Deleted && !string.IsNullOrEmpty(item.FullPath))
                    removed.Add(item.FullPath);
            }

            _loadingGrid = true;
            try
            {
                for (int i = _grid.Rows.Count - 1; i >= 0; i--)
                {
                    FileItem tag = _grid.Rows[i].Tag as FileItem;
                    if (tag != null && removed.Contains(tag.FullPath))
                        _grid.Rows.RemoveAt(i);
                }
            }
            finally
            {
                _loadingGrid = false;
            }
            UpdateGridAccessibleName();
            UpdateSelectionState();
        }

        private void ShowCleanSummary(CleanResult result)
        {
            // Il riquadro dei risultati resta nella finestra principale; i
            // dettagli completi si aprono solo su richiesta tramite il
            // pannello integrato «Mostra dettagli».
            UpdateCleanResults();
        }

        /// <summary>
        /// Testo del riepilogo di pulizia, incluso la sezione della fase cartelle vuote
        /// quando la fase è stata eseguita. Metodo puro: con <paramref name="emptyFolders"/>
        /// null il testo è identico alla 0.3.6.
        /// </summary>
        internal static string BuildCleanSummaryText(CleanResult result, EmptyCleanResult emptyFolders)
        {
            string header = result.Cancelled
                ? "Pulizia annullata prima del termine."
                : "Pulizia completata.";

            string text = header
                + "\r\n\r\nFile eliminati: " + result.DeletedCount
                + "\r\nSpazio effettivamente liberato: " + ByteFormatter.Format(result.DeletedBytes)
                + " (" + result.DeletedBytes + " byte)"
                + "\r\nFile saltati: " + result.SkippedCount
                + "\r\nErrori: " + result.ErrorCount
                + "\r\nTempo impiegato: " + result.Duration.TotalSeconds.ToString("N1") + " s"
                + "\r\nSpazio stimato non recuperato: " + ByteFormatter.Format(result.EstimatedNotRecovered);

            if (result.ErrorCount > 0)
            {
                int shown = 0;
                string details = string.Empty;
                for (int i = 0; i < result.Items.Count && shown < 6; i++)
                {
                    CleanItemResult item = result.Items[i];
                    if (item.Outcome != CleanOutcome.Failed) continue;
                    details += "\r\n- " + item.Name + ": " + item.Message;
                    shown++;
                }
                if (details.Length > 0) text += "\r\n\r\nDettaglio degli errori:" + details;
            }

            if (result.SkippedCount > 0)
                text += "\r\n\r\nGli elementi saltati non sono stati modificati.";

            if (emptyFolders != null)
            {
                text += "\r\n\r\nCartelle vuote (fase separata e facoltativa):"
                    + "\r\nRimosse: " + emptyFolders.RemovedCount
                    + "\r\nSaltate: " + emptyFolders.SkippedCount
                    + "\r\nErrori: " + emptyFolders.ErrorCount;

                if (emptyFolders.Cancelled)
                    text += "\r\nEsito: rimozione cartelle vuote annullata.";
                if (!string.IsNullOrEmpty(emptyFolders.ErrorMessage))
                    text += "\r\nErrore della fase cartelle: " + emptyFolders.ErrorMessage;

                if (emptyFolders.ErrorCount > 0)
                {
                    int shown = 0;
                    string details = string.Empty;
                    for (int i = 0; i < emptyFolders.Items.Count && shown < 6; i++)
                    {
                        EmptyDirectoryItemResult item = emptyFolders.Items[i];
                        if (item.Outcome != EmptyDirectoryOutcome.Failed) continue;
                        details += "\r\n- " + item.Name + ": " + item.Message;
                        shown++;
                    }
                    if (details.Length > 0) text += "\r\n\r\nDettaglio degli errori (cartelle):" + details;
                }

                if (emptyFolders.SkippedCount > 0)
                    text += "\r\n\r\nLe cartelle saltate non sono state modificate.";
            }

            return text;
        }

        private List<FileItem> GetSelectedItems()
        {
            List<FileItem> selected = new List<FileItem>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                object value = row.Cells["colSelected"].Value;
                if (!(value is bool) || !(bool)value) continue;
                FileItem item = row.Tag as FileItem;
                if (item != null) selected.Add(item);
            }
            return selected;
        }

        #endregion

        #region Annullamento

        private void OnCancelClick(object sender, EventArgs e)
        {
            CancellationTokenSource cts = _cts;
            if (cts == null) return;
            SetStatus("Annullamento in corso...");
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        #endregion

        #region Report

        private void OnReportClick(object sender, EventArgs e)
        {
            if (_busy) return;

            if (string.IsNullOrEmpty(_lastReportPath) || !File.Exists(_lastReportPath))
            {
                // La richiesta dell'utente genera comunque un report, anche quando
                // la conservazione automatica è stata disattivata.
                GenerateReport(true);
                if (string.IsNullOrEmpty(_lastReportPath))
                {
                    ShowMessageOwner(
                        "Non ci sono ancora risultati da riportare. Eseguire prima un'analisi.",
                        "Nuvacline", MessageBoxIcon.Information);
                    return;
                }
            }

            OpenReportFile(_lastReportPath);
        }

        /// <summary>
        /// Apre o richiude il pannello integrato dei dettagli dell'ultima
        /// pulizia. Non esegue mai nuove scansioni né pulizie: mostra solo i
        /// dati già prodotti.
        /// </summary>
        private void OnDetailsClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_lastClean == null) return;
            if (_onDetailsToggle != null) _onDetailsToggle();
            else ToggleDetailsPanel();
        }

        /// <summary>
        /// Commuta la visibilità del pannello dettagli integrato. Il testo
        /// completo degli esiti viene calcolato al momento dell'apertura e non
        /// viene mai modificato dalla chiusura.
        /// </summary>
        private void ToggleDetailsPanel()
        {
            if (_lastClean == null || _detailsPanel == null) return;

            // Stato di logica nel campo booleano: il getter di Visible risale la
            // catena dei genitori e sarebbe sempre falso su una finestra non
            // ancora mostrata. Visible resta solo la proprietà di rendering.
            bool opening = !_detailsOpen;
            _detailsOpen = opening;
            _detailsPanel.Visible = opening;
            _btnDetails.Text = opening ? "Nascondi dettagli" : "Mostra dettagli";

            if (opening)
            {
                _detailsTextBox.Text = CleanupDetailsForm.BuildDetailsText(
                    _lastClean, _lastEmptyClean);
                _detailsPanel.Height = DetailsOpenHeight;
            }

            // La griglia si riallinea sotto il pannello quando aperto e
            // torna a piena altezza quando chiuso. I risultati restano intatti.
            _grid.Height = Math.Max(40,
                ClientSize.Height - GridTopClean - BottomReserved
                - (opening ? DetailsOpenHeight + 4 : 0));
            if (opening)
                _detailsPanel.Top = GridTopClean + _grid.Height + 2;
        }

        /// <summary>
        /// Scrive il report locale al termine di un'analisi o di una pulizia e ne mostra il
        /// percorso nella finestra. Il file resta sulla macchina dell'utente: non viene inviato
        /// a nessuno e non viene scritto in cartelle condivise o sincronizzate.
        ///
        /// La scrittura automatica avviene solo se la conservazione dei report è attiva
        /// (impostazione predefinita); una richiesta esplicita dell'utente genera invece
        /// sempre un file. La disattivazione non cancella mai i report già scritti.
        /// </summary>
        private void GenerateReport(bool explicitRequest)
        {
            if (_lastScan == null && _lastClean == null) return;

            if (!explicitRequest && !_keepReports)
            {
                // Conservazione disattivata: nessuna scrittura e nessuna cancellazione.
                _lastReportPath = null;
                UpdateReportLabel();
                return;
            }

            try
            {
                string folder = _reportsFolder != null
                    ? _reportsFolder()
                    : ReportsFolderDefault();
                Directory.CreateDirectory(folder);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string candidate = Path.Combine(folder, "nuvacline-" + stamp + ".txt");
                int suffix = 1;
                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(folder,
                        "nuvacline-" + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".txt");
                    suffix++;
                }

                ReportData data = ReportBuilder.Build(_lastScan, _lastClean, _lastEmptyClean, _currentRoot);
                if (_lastScan != null)
                {
                    data.MinAgeText = FormatThreshold(_lastScanMinAge);
                    data.FilesSelected = CountSelected();
                }
                ReportExporter.Export(candidate, data);
                _lastReportPath = candidate;
                UpdateReportLabel();
            }
            catch (Exception ex)
            {
                _lastReportPath = null;
                UpdateReportLabel();
                SetStatus("Report non generato: " + ex.Message);
            }
        }

        /// <summary>Cartella predefinita dei report locali.</summary>
        private static string ReportsFolderDefault()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nuvacline", "reports");
        }

        private void UpdateReportLabel()
        {
            if (_reportLabel == null) return;

            if (!string.IsNullOrEmpty(_lastReportPath) && File.Exists(_lastReportPath))
            {
                _reportLabel.Text = "Report: " + _lastReportPath;
                _reportLabel.ForeColor = _palette.TextSecondary;
            }
            else if (!_keepReports)
            {
                _reportLabel.Text = "Report: conservazione disattivata. "
                    + "Il pulsante \u00abApri report\u00bb genera un report su richiesta.";
                _reportLabel.ForeColor = _palette.TextMuted;
            }
            else
            {
                _reportLabel.Text = "Report: nessuno generato. Viene scritto al termine di un'analisi o di una pulizia.";
                _reportLabel.ForeColor = _palette.TextMuted;
            }
        }

        private void OpenReportFile(string path)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path))
            {
                _lastReportPath = null;
                UpdateReportLabel();
                ShowMessageOwner("Il report selezionato non esiste più.", "Nuvacline", MessageBoxIcon.Warning);
                return;
            }

            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(path);
                info.UseShellExecute = true;
                System.Diagnostics.Process.Start(info);
            }
            catch (Exception ex)
            {
                ShowMessageOwner("Impossibile aprire il report: " + ex.Message, "Nuvacline", MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Informazioni e impostazioni

        /// <summary>
        /// Carica la preferenza di conservazione dei report tramite il seam dedicato,
        /// così i test non toccano mai il file reale dell'utente. Qualsiasi errore
        /// ricade sul valore predefinito: conservazione attiva.
        /// </summary>
        private void LoadKeepReportsSetting()
        {
            bool keep = ScanSettings.DefaultKeepReports;
            try
            {
                if (_loadKeepReports != null) keep = _loadKeepReports();
            }
            catch (Exception)
            {
                keep = ScanSettings.DefaultKeepReports;
            }

            _keepReports = keep;
            if (_miKeepReports != null) _miKeepReports.Checked = keep;
            UpdateReportLabel();
        }

        /// <summary>
        /// Carica la preferenza di rimozione delle cartelle vuote tramite il seam
        /// dedicato, così i test non toccano mai il file reale dell'utente. Qualsiasi
        /// errore ricade sul valore predefinito: rimozione disattivata.
        /// </summary>
        private void LoadRemoveEmptyFoldersSetting()
        {
            bool remove = ScanSettings.DefaultRemoveEmptyFolders;
            try
            {
                if (_loadRemoveEmptyFolders != null) remove = _loadRemoveEmptyFolders();
            }
            catch (Exception)
            {
                remove = ScanSettings.DefaultRemoveEmptyFolders;
            }

            _removeEmptyFolders = remove;
            if (_miRemoveEmptyFolders != null) _miRemoveEmptyFolders.Checked = remove;
        }

        /// <summary>
        /// Carica la preferenza del tema tramite il seam dedicato, così i test
        /// non toccano mai il file reale dell'utente. Qualsiasi errore o valore
        /// fuori dall'enum ricade sul predefinito: tema automatico. Il valore
        /// caricato viene applicato subito dopo con ApplyTheme(), mentre la
        /// selezione della combo e' protetta dal flag _loadingTheme per non
        /// riscrivere la stessa preferenza durante l'avvio.
        /// </summary>
        private void LoadThemeChoiceSetting()
        {
            ThemeChoice choice = ScanSettings.DefaultThemeChoice;
            try
            {
                if (_loadThemeChoice != null) choice = _loadThemeChoice();
            }
            catch (Exception)
            {
                choice = ScanSettings.DefaultThemeChoice;
            }

            if (choice != ThemeChoice.Automatic
                && choice != ThemeChoice.Light
                && choice != ThemeChoice.Dark)
            {
                choice = ScanSettings.DefaultThemeChoice;
            }

            _themeChoice = choice;
            _loadingTheme = true;
            try
            {
                if (_cboTheme != null)
                {
                    int index = (int)choice;
                    if (index < 0 || index >= _cboTheme.Items.Count) index = 0;
                    _cboTheme.SelectedIndex = index;
                }
            }
            finally
            {
                _loadingTheme = false;
            }
        }

        /// <summary>
        /// Applica e persiste la scelta del tema. La modifica non avvia analisi
        /// né pulizia; se il salvataggio fallisce il tema resta comunque
        /// applicato alla sessione corrente e alla prossima apertura varrà
        /// l'ultima preferenza salvata.
        /// </summary>
        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (_loadingTheme) return;

            int index = _cboTheme.SelectedIndex;
            if (index < 0 || index >= _cboTheme.Items.Count) return;

            _themeChoice = (ThemeChoice)index;
            try
            {
                if (_saveThemeChoice != null) _saveThemeChoice(_themeChoice);
            }
            catch (Exception)
            {
                // Nessun errore a schermo: il tema resta applicato in sessione.
            }

            ApplyTheme();
        }

        private void OnInfoClick(object sender, EventArgs e)
        {
            if (_showInfoDialog != null) _showInfoDialog();
            else ShowInfoDialog();
        }

        private void ShowInfoDialog()
        {
            using (Form dialog = BuildInfoForm())
            {
                dialog.ShowDialog(this);
            }
        }

        /// <summary>
        /// Finestra modale con le informazioni sul prodotto: nessuna rete, nessuna
        /// azione, solo testo e il pulsante OK.
        /// </summary>
        private Form BuildInfoForm()
        {
            Form dialog = new Form();
            dialog.Name = "dlgInfo";
            dialog.Text = "Informazioni su Nuvacline";
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.Font = Font;
            dialog.BackColor = _palette.Surface;
            dialog.ForeColor = _palette.Text;

            Label body = new Label();
            body.Name = "lblInfoBody";
            body.AutoSize = true;
            body.MaximumSize = new Size(440, 0);
            body.Location = new Point(16, 16);
            body.ForeColor = _palette.Text;
            body.Text = BuildInfoText();
            dialog.Controls.Add(body);

            Button ok = new Button();
            ok.Name = "btnInfoOk";
            ok.Text = "OK";
            ok.Size = new Size(90, 30);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.DialogResult = DialogResult.OK;
            dialog.Controls.Add(ok);
            ApplyButtonTheme(ok);

            int width = Math.Max(440, body.Width) + 32;
            int height = body.Bottom + 16 + ok.Height + 16;
            dialog.ClientSize = new Size(width, height);
            ok.Location = new Point(width - ok.Width - 16, height - ok.Height - 16);

            dialog.AcceptButton = ok;
            dialog.CancelButton = ok;
            return dialog;
        }

        /// <summary>Testo della finestra Informazioni.</summary>
        private string BuildInfoText()
        {
            return "Nuvacline " + AppInfo.VersionText + "\r\n\r\n"
                + "Scopo\r\n"
                + "Nuvacline analizza in sola lettura la cartella temporanea dell'utente corrente "
                + "e propone la rimozione dei file più vecchi della soglia scelta. Nessuna modifica "
                + "viene apportata senza una conferma esplicita.\r\n\r\n"
                + "Gratuito ed essenziale\r\n"
                + "Un singolo file eseguibile, senza installazione, senza componenti aggiuntivi, "
                + "senza servizi e senza dipendenze di terze parti.\r\n\r\n"
                + "Privacy\r\n"
                + "Nessuna connessione di rete, nessun dato raccolto, nessuna modifica al registro, "
                + "nessun avvio automatico. Analisi, riepilogo e report restano su questo computer. "
                + "Per adattare l'interfaccia viene letta soltanto l'impostazione di tema Windows "
                + "(chiaro/scuro): nessuna scrittura.\r\n\r\n"
                + "Funzionamento\r\n"
                + "Analisi, filtro temporale, anteprima dei risultati, selezione riga per riga e "
                + "conferma prima di ogni pulizia. Il report locale viene scritto nella cartella "
                + "dei report solo se la conservazione è attiva oppure su richiesta. Inoltre, "
                + "se l'opzione «Rimuovi anche le cartelle diventate vuote» del menu "
                + "impostazioni è attiva (disattivata di default), al termine di una pulizia "
                + "confermata viene chiesta una conferma separata per rimuovere le sole "
                + "cartelle rimaste vuote sotto la cartella temporanea: la radice non viene "
                + "mai eliminata e i collegamenti non vengono toccati.\r\n\r\n"
                + "Licenza\r\n"
                + "GNU General Public License v3.0 or later (GPL-3.0-or-later). "
                + "Il testo completo è nel file LICENSE del sorgente. "
                + "Il nome e il logo non sono concessi per uso di marchio dalla licenza.\r\n\r\n"
                + "Avvertenza\r\n"
                + "I file eliminati non sono recuperabili.";
        }

        private void BuildSettingsMenu()
        {
            _settingsMenu = new ContextMenuStrip();
            _settingsMenu.Name = "menuSettings";

            _miKeepReports = new ToolStripMenuItem("Conserva i report generati");
            _miKeepReports.Name = "miKeepReports";
            _miKeepReports.CheckOnClick = true;
            _miKeepReports.Checked = _keepReports;
            _miKeepReports.ToolTipText =
                "Se attiva, ogni analisi o pulizia scrive un report nella cartella dei report. "
                + "Se disattiva nessun report viene scritto automaticamente: \"Apri report\" "
                + "ne genera uno su richiesta.";
            _miKeepReports.Click += OnKeepReportsClick;

            _miRemoveEmptyFolders = new ToolStripMenuItem("Rimuovi anche le cartelle diventate vuote");
            _miRemoveEmptyFolders.Name = "miRemoveEmptyFolders";
            _miRemoveEmptyFolders.CheckOnClick = true;
            _miRemoveEmptyFolders.Checked = _removeEmptyFolders;
            _miRemoveEmptyFolders.ToolTipText =
                "Se attiva, al termine di una pulizia confermata e non annullata Nuvacline "
                + "esegue un sondaggio in sola lettura, chiede una conferma separata con il "
                + "conteggio delle cartelle vuote e le rimuove sotto la cartella temporanea. "
                + "La radice non viene mai eliminata e i collegamenti non vengono toccati. "
                + "Disattivata di default.";
            _miRemoveEmptyFolders.Click += OnRemoveEmptyFoldersClick;

            _settingsSeparator = new ToolStripSeparator();
            _settingsSeparator.Name = "sepSettingsReports";

            _miOpenReportsFolder = new ToolStripMenuItem("Apri cartella dei report");
            _miOpenReportsFolder.Name = "miOpenReportsFolder";
            _miOpenReportsFolder.Click += OnOpenReportsFolderClick;

            _settingsMenu.Items.Add(_miKeepReports);
            _settingsMenu.Items.Add(_miRemoveEmptyFolders);
            _settingsMenu.Items.Add(_settingsSeparator);
            _settingsMenu.Items.Add(_miOpenReportsFolder);
        }

        /// <summary>
        /// Allinea il menu allo stato corrente: spunta della conservazione e visibilità
        /// della voce "Apri cartella dei report" (presente solo se la cartella esiste).
        /// La proprietà usata è Available, che resta leggibile anche con il menu chiuso.
        /// </summary>
        private void RefreshSettingsMenu()
        {
            if (_miKeepReports != null) _miKeepReports.Checked = _keepReports;
            if (_miRemoveEmptyFolders != null) _miRemoveEmptyFolders.Checked = _removeEmptyFolders;

            bool folderExists = ReportsFolderExists();
            if (_miOpenReportsFolder != null)
            {
                _miOpenReportsFolder.Available = folderExists;
                _miOpenReportsFolder.Enabled = folderExists;
            }
            if (_settingsSeparator != null) _settingsSeparator.Available = folderExists;
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            if (_settingsMenu == null) return;
            RefreshSettingsMenu();
            if (IsHandleCreated)
                _settingsMenu.Show(_btnSettings, new Point(0, _btnSettings.Height));
        }

        private void OnKeepReportsClick(object sender, EventArgs e)
        {
            if (_miKeepReports == null) return;

            // CheckOnClick ha già commutato la spunta: questo è il valore richiesto.
            bool requested = _miKeepReports.Checked;
            bool saved = false;
            try
            {
                if (_saveKeepReports != null) saved = _saveKeepReports(requested);
            }
            catch (Exception)
            {
                saved = false;
            }

            if (saved)
            {
                _keepReports = requested;
                UpdateReportLabel();
                return;
            }

            // Salvataggio non riuscito: la spunta viene riportata al valore effettivo
            // e l'utente viene informato. Nessun cambio di stato silenzioso.
            _miKeepReports.Checked = !requested;
            NotifyUser("Impossibile salvare la preferenza per la conservazione dei report.");
        }

        private void OnRemoveEmptyFoldersClick(object sender, EventArgs e)
        {
            if (_miRemoveEmptyFolders == null) return;

            // CheckOnClick ha già commutato la spunta: questo è il valore richiesto.
            bool requested = _miRemoveEmptyFolders.Checked;
            bool saved = false;
            try
            {
                if (_saveRemoveEmptyFolders != null) saved = _saveRemoveEmptyFolders(requested);
            }
            catch (Exception)
            {
                saved = false;
            }

            if (saved)
            {
                _removeEmptyFolders = requested;
                return;
            }

            // Salvataggio non riuscito: la spunta viene riportata al valore effettivo
            // e l'utente viene informato. Nessun cambio di stato silenzioso.
            _miRemoveEmptyFolders.Checked = !requested;
            NotifyUser("Impossibile salvare la preferenza per la rimozione delle cartelle vuote.");
        }

        private void OnOpenReportsFolderClick(object sender, EventArgs e)
        {
            string folder = null;
            try
            {
                folder = _reportsFolder != null ? _reportsFolder() : ReportsFolderDefault();
            }
            catch (Exception)
            {
                folder = null;
            }

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                NotifyUser("La cartella dei report non esiste ancora: viene creata al termine "
                    + "di un'analisi o di una pulizia.");
                return;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(folder);
                info.UseShellExecute = true;
                _startProcess(info);
            }
            catch (Exception ex)
            {
                NotifyUser("Impossibile aprire la cartella dei report: " + ex.Message);
            }
        }

        private bool ReportsFolderExists()
        {
            try
            {
                string folder = _reportsFolder != null ? _reportsFolder() : ReportsFolderDefault();
                return !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Tema chiaro/scuro

        private const int WmSettingChange = 0x001A;
        private const int WmThemeChanged = 0x031A;

        /// <summary>
        /// Applica al tema scelto dall'utente tutti i controlli della finestra.
        /// La preferenza "Automatico" segue il rilevamento del seam _detectTheme;
        /// "Chiaro" e "Scuro" forzano il tema indipendentemente da Windows.
        /// Questa e' l'unica sede di assegnazione cromatica dell'applicazione:
        /// ogni valore proviene centralmente da ThemePalette, dove il tema chiaro
        /// riproduce i colori di sistema e il tema scuro la palette antracite
        /// fissa. Un tema non riconosciuto o un errore di lettura cadono sempre
        /// sul chiaro.
        /// </summary>
        private void ApplyTheme()
        {
            AppTheme theme = AppTheme.Light;
            try
            {
                if (_themeChoice == ThemeChoice.Dark) theme = AppTheme.Dark;
                else if (_themeChoice == ThemeChoice.Light) theme = AppTheme.Light;
                else if (_detectTheme != null) theme = _detectTheme();
            }
            catch (Exception)
            {
                theme = AppTheme.Light;
            }

            ApplyThemeCore(theme);
        }

        /// <summary>Applica un tema gia' rilevato a tutti i controlli della finestra.</summary>
        private void ApplyThemeCore(AppTheme theme)
        {
            _currentTheme = theme;
            _palette = ThemePalette.For(theme);
            ThemePalette palette = _palette;

            BackColor = palette.Surface;
            ForeColor = palette.Text;

            _titleLabel.ForeColor = palette.Text;
            _statusLabel.ForeColor = palette.Text;
            _summaryLabel.ForeColor = palette.Text;
            _lblSelection.ForeColor = palette.TextSecondary;
            _lblThreshold.ForeColor = palette.TextSecondary;
            _lblTheme.ForeColor = palette.TextSecondary;
            _cboTheme.BackColor = palette.Surface;
            _cboTheme.ForeColor = palette.Text;
            _footnoteLabel.ForeColor = palette.TextSecondary;

            ApplyButtonTheme(_btnAnalyze);
            ApplyButtonTheme(_btnClean);
            ApplyButtonTheme(_btnCancel);
            ApplyButtonTheme(_btnReport);
            ApplyButtonTheme(_btnSelectAll);
            ApplyButtonTheme(_btnDeselectAll);
            ApplyButtonTheme(_btnInfo);
            ApplyButtonTheme(_btnSettings);
            ApplyButtonTheme(_btnDetails);

            if (_cleanResultsPanel != null)
            {
                _cleanResultsPanel.BackColor = palette.Surface;
                _cleanStatusLabel.ForeColor = _lastClean != null
                    ? ResolveStatusColor(_lastClean, _lastEmptyClean)
                    : palette.Text;
                _spaceValueLabel.ForeColor = palette.Text;
                _spaceCaptionLabel.ForeColor = palette.TextSecondary;
                _statDeletedCaption.ForeColor = palette.TextSecondary;
                _statDeletedValue.ForeColor = palette.Text;
                _statSkippedCaption.ForeColor = palette.TextSecondary;
                _statSkippedValue.ForeColor = palette.Text;
                _statErrorCaption.ForeColor = palette.TextSecondary;
                _statErrorValue.ForeColor = palette.Text;
                _statFolderCaption.ForeColor = palette.TextSecondary;
                _statFolderValue.ForeColor = palette.Text;
            }
            if (_detailsPanel != null)
            {
                _detailsPanel.BackColor = palette.Surface;
                _detailsTextBox.BackColor = palette.GridBack;
                _detailsTextBox.ForeColor = palette.Text;
            }

            _grid.BackgroundColor = palette.GridBack;
            _grid.DefaultCellStyle.BackColor = palette.GridBack;
            _grid.DefaultCellStyle.ForeColor = palette.GridText;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = palette.GridHeaderBack;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.GridHeaderText;

            if (_rowMenu != null)
            {
                _rowMenu.BackColor = palette.MenuBack;
                _rowMenu.ForeColor = palette.MenuText;
            }
            if (_settingsMenu != null)
            {
                _settingsMenu.BackColor = palette.MenuBack;
                _settingsMenu.ForeColor = palette.MenuText;
            }

            // L'etichetta del report cambia colore in base allo stato: si riallinea
            // all'ultima impostazione testuale dopo il cambio di tema.
            UpdateReportLabel();
        }

        /// <summary>
        /// Colore di un pulsante secondo il tema attivo. Nel tema chiaro il
        /// pulsante torna al rendering nativo di Windows (assegnazione di sistema
        /// piu' attivazione visiva per ultima, cosi' da annullare un eventuale
        /// tema scuro precedente); nel tema scuro i colori arrivano dalla palette.
        /// </summary>
        private void ApplyButtonTheme(Button button)
        {
            if (button == null) return;

            button.BackColor = _palette.ButtonBack;
            button.ForeColor = _palette.ButtonText;
            button.UseVisualStyleBackColor = _palette.NativeButtons;
        }

        /// <summary>
        /// Aggiornamento in tempo reale del tema. Quando Windows cambia tema o
        /// schema colori invia WM_SETTINGCHANGE (area "ImmersiveColorSet") oppure
        /// WM_THEMECHANGED alle finestre: qui il messaggio viene riconosciuto, il
        /// tema riletto tramite il seam e la palette riapplicata solo se il valore
        /// effettivamente cambiato. Nessuna registrazione, nessun thread, nessuna
        /// spesa quando i messaggi non riguardano il tema.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmThemeChanged)
            {
                ApplyThemeIfChanged();
                return;
            }
            if (m.Msg != WmSettingChange) return;

            string area = null;
            if (m.LParam != IntPtr.Zero) area = Marshal.PtrToStringUni(m.LParam);
            if (area != null
                && !string.Equals(area, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
                return;

            ApplyThemeIfChanged();
        }

        /// <summary>
        /// Rilegge il tema e riapplica la palette solo quando il valore e'
        /// cambiato: un errore di lettura lascia invariato il tema attuale.
        /// La lettura e' una sola per messaggio (nessuna doppia rilevazione).
        /// </summary>
        private void ApplyThemeIfChanged()
        {
            // Tema forzato dall'utente: i messaggi di sistema non devono
            // sovrascriverlo. Il ritorno al rilevamento avviene solo quando
            // la preferenza torna "Automatico".
            if (_themeChoice != ThemeChoice.Automatic) return;

            AppTheme detected;
            try
            {
                detected = _detectTheme != null ? _detectTheme() : AppTheme.Light;
            }
            catch (Exception)
            {
                return;
            }
            if (detected == _currentTheme) return;
            ApplyThemeCore(detected);
        }

        #endregion

        #region Selezione

        private void OnGridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (!_grid.IsCurrentCellDirty) return;
            if (_grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loadingGrid) return;
            if (e.ColumnIndex != 0 || e.RowIndex < 0) return;

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            FileItem item = row.Tag as FileItem;
            object value = row.Cells[0].Value;
            bool selected = value is bool && (bool)value;
            if (item != null) item.Selected = selected;
            UpdateSelectionState();
        }

        /// <summary>
        /// Seleziona tutti e soli i file validi attualmente visualizzati.
        /// Non avvia pulizie, non tocca i file su disco, non aggira la conferma.
        /// </summary>
        private void OnSelectAllClick(object sender, EventArgs e)
        {
            SetAllChecks(true);
        }

        /// <summary>
        /// Deseleziona tutti i file attualmente selezionati. Non cancella alcun file.
        /// </summary>
        private void OnDeselectAllClick(object sender, EventArgs e)
        {
            SetAllChecks(false);
        }

        private void SetAllChecks(bool state)
        {
            _loadingGrid = true;
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    FileItem item = row.Tag as FileItem;
                    if (item == null) continue;
                    DataGridViewCheckBoxCell cell = row.Cells["colSelected"] as DataGridViewCheckBoxCell;
                    if (cell == null) continue;
                    cell.Value = state;
                    item.Selected = state;
                }
            }
            finally
            {
                _loadingGrid = false;
                UpdateSelectionState();
            }
        }

        #endregion

        #region Soglia età minima

        private void LoadThresholdSetting()
        {
            _loadingThreshold = true;
            try
            {
                _currentThreshold = _loadSettings();
                _lastScanMinAge = _currentThreshold.Value;
                PopulateThresholdCombo();
            }
            finally
            {
                _loadingThreshold = false;
            }
        }

        private void PopulateThresholdCombo()
        {
            _cboThreshold.Items.Clear();
            ScanThresholdOption[] options = ScanSettings.CreateOptions();
            int selected = 0;
            for (int i = 0; i < options.Length; i++)
            {
                _cboThreshold.Items.Add(options[i]);
                if (options[i].Hours == _currentThreshold.Hours) selected = i;
            }
            _cboThreshold.SelectedIndex = selected;
        }

        private void OnThresholdChanged(object sender, EventArgs e)
        {
            if (_loadingThreshold) return;

            ScanThresholdOption option = _cboThreshold.SelectedItem as ScanThresholdOption;
            if (option == null) return;

            _currentThreshold = option;
            _saveSettings(option.Hours);
            UpdateSummaryLabel();
            // Nessuna analisi né pulizia avviata: il nuovo valore vale dalla prossima scansione.
        }

        private ScanThresholdOption CurrentThreshold
        {
            get { return _currentThreshold ?? ScanSettings.DefaultOption(); }
        }

        private string FormatThreshold(TimeSpan minAge)
        {
            if (minAge <= TimeSpan.Zero) return "nessuna";
            if (minAge.TotalHours == 1) return "1 ora";
            if (minAge.TotalHours < 24) return ((int)minAge.TotalHours) + " ore";
            if (minAge.TotalDays == 1) return "24 ore";
            if (minAge.TotalDays == 7) return "7 giorni";
            return ((int)minAge.TotalDays) + " giorni";
        }

        #endregion

        #region Menu contestuale

        private void BuildRowContextMenu()
        {
            _rowMenu = new ContextMenuStrip();
            _rowMenu.Name = "menuRowContext";

            ToolStripMenuItem copyName = new ToolStripMenuItem("Copia nome file");
            copyName.Name = "miCopyName";
            copyName.Click += OnCopyNameClick;

            ToolStripMenuItem copyFolder = new ToolStripMenuItem("Copia percorso cartella");
            copyFolder.Name = "miCopyFolder";
            copyFolder.Click += OnCopyFolderClick;

            ToolStripMenuItem openFolder = new ToolStripMenuItem("Apri cartella e seleziona file");
            openFolder.Name = "miOpenFolder";
            openFolder.Click += OnOpenFolderClick;

            _rowMenu.Items.Add(copyName);
            _rowMenu.Items.Add(copyFolder);
            _rowMenu.Items.Add(openFolder);
            _rowMenu.Closed += OnRowMenuClosed;
        }

        /// <summary>
        /// Il menu compare solo su clic destro di una riga valida che rappresenta un file:
        /// intestazioni, spazi vuoti e righe senza modello vengono ignorati.
        /// </summary>
        private void OnGridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ShowRowMenuFor(e.RowIndex);
        }

        private bool ShouldShowRowMenu(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return false;
            return ScanRowActions.IsValidTarget(_grid.Rows[rowIndex].Tag);
        }

        private void ShowRowMenuFor(int rowIndex)
        {
            _rowMenuTarget = null;
            if (!ShouldShowRowMenu(rowIndex)) return;

            _rowMenuTarget = _grid.Rows[rowIndex].Tag as FileItem;
            // Fotografia degli spunti prima dell'apertura: alla chiusura il menu deve
            // garantire che la selezione destinata alla pulizia sia rimasta intatta.
            _rowMenuCheckSnapshot = SnapshotCheckState();

            if (IsHandleCreated)
            {
                Point location = _grid.PointToClient(Cursor.Position);
                _rowMenu.Show(_grid, location);
            }
        }

        private void OnRowMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            RestoreCheckState(_rowMenuCheckSnapshot);
            _rowMenuCheckSnapshot = null;
            // Il Click della voce puo' essere distribuito DOPO la chiusura del menu:
            // il target non viene azzerato qui (verra' sostituito alla prossima apertura)
            // affinche' le azioni leggano comunque il percorso della riga che ha aperto
            // il menu, qualunque sia l'ordine degli eventi.
        }

        private bool[] SnapshotCheckState()
        {
            bool[] state = new bool[_grid.Rows.Count];
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                object value = _grid.Rows[i].Cells["colSelected"].Value;
                state[i] = value is bool && (bool)value;
            }
            return state;
        }

        private void RestoreCheckState(bool[] state)
        {
            if (state == null || state.Length != _grid.Rows.Count) return;
            for (int i = 0; i < state.Length; i++)
            {
                object value = _grid.Rows[i].Cells["colSelected"].Value;
                bool current = value is bool && (bool)value;
                if (current == state[i]) continue;
                // IlValueChanged propagherà lo stato a FileItem.Selected e al pulsante.
                _grid.Rows[i].Cells["colSelected"].Value = state[i];
            }
        }

        private string RowMenuPath()
        {
            return _rowMenuTarget != null ? _rowMenuTarget.FullPath : null;
        }

        private void OnCopyNameClick(object sender, EventArgs e)
        {
            string path = RowMenuPath();
            if (string.IsNullOrEmpty(path))
            {
                NotifyUser(ScanRowActions.InvalidTargetMessage);
                return;
            }
            CopyRowText(ScanRowActions.GetFileName(path));
        }

        private void OnCopyFolderClick(object sender, EventArgs e)
        {
            string path = RowMenuPath();
            if (string.IsNullOrEmpty(path))
            {
                NotifyUser(ScanRowActions.InvalidTargetMessage);
                return;
            }
            CopyRowText(ScanRowActions.GetFolder(path));
        }

        private void CopyRowText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                NotifyUser(ScanRowActions.InvalidTargetMessage);
                return;
            }
            try { _setClipboard(text); }
            catch (Exception ex)
            {
                NotifyUser(ScanRowActions.ClipboardErrorMessage + " " + ex.Message);
            }
        }

        private void OnOpenFolderClick(object sender, EventArgs e)
        {
            string path = RowMenuPath();
            string errorMessage;
            if (!ScanRowActions.CanOpenFolder(path, out errorMessage))
            {
                NotifyUser(errorMessage);
                return;
            }
            try { _startProcess(ScanRowActions.BuildExplorerSelectStartInfo(path)); }
            catch (Exception ex)
            {
                NotifyUser(ScanRowActions.ExplorerErrorMessage + " " + ex.Message);
            }
        }

        private void NotifyUser(string text)
        {
            if (_notifyUser != null) _notifyUser(text);
            else ShowMessageOwner(text, "Nuvacline", MessageBoxIcon.Warning);
        }

        #endregion
    }
}
