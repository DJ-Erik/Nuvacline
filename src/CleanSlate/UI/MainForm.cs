using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CleanSlate.Core;

namespace CleanSlate.UI
{
    /// <summary>
    /// Finestra principale di CleanSlate.
    /// La UI non blocca mai il thread di esecuzione: le operazioni lunghe girano in background
    /// e sono annullabili in modo cooperativo.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const int ProgressThrottleMs = 120;

        private readonly TempFolderScanner _scanner = new TempFolderScanner();
        private readonly TempFileCleaner _cleaner = new TempFileCleaner();

        private Label _titleLabel;
        private Label _subtitleLabel;
        private Button _btnAnalyze;
        private Button _btnClean;
        private Button _btnCancel;
        private Button _btnReport;
        private Label _statusLabel;
        private ProgressBar _progress;
        private Label _summaryLabel;
        private Label _reportLabel;
        private DataGridView _grid;
        private Label _footnoteLabel;

        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _loadingGrid;
        private int _lastProgressTick;
        private ScanResult _lastScan;
        private CleanResult _lastClean;
        private string _currentRoot;
        private string _lastReportPath;

        private ContextMenuStrip _rowMenu;
        private FileItem _rowMenuTarget;
        private bool[] _rowMenuCheckSnapshot;
        private Action<string> _notifyUser;
        private Action<string> _setClipboard;
        private Action<ProcessStartInfo> _startProcess;

        public MainForm()
        {
            BuildUi();
            _notifyUser = delegate(string text)
            {
                ShowMessageOwner(text, "CleanSlate", MessageBoxIcon.Warning);
            };
            _setClipboard = delegate(string text) { Clipboard.SetText(text); };
            _startProcess = delegate(ProcessStartInfo info) { Process.Start(info); };
            UpdateSummaryLabel();
            UpdateReportLabel();
            UpdateSelectionState();
            SetBusy(false);
            SetStatus("Pronto.");
        }

        #region Costruzione interfaccia

        private void BuildUi()
        {
            Text = "CleanSlate " + AppInfo.VersionText;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(800, 480);
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
            _titleLabel.Text = "CleanSlate " + AppInfo.VersionText;
            Controls.Add(_titleLabel);

            _subtitleLabel = new Label();
            _subtitleLabel.Name = "lblSubtitle";
            _subtitleLabel.AutoSize = false;
            _subtitleLabel.Location = new Point(14, 40);
            _subtitleLabel.Size = new Size(ClientSize.Width - 28, 32);
            _subtitleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _subtitleLabel.ForeColor = Color.DimGray;
            _subtitleLabel.Text = "Analisi in sola lettura e rimozione dei file temporanei dell'utente corrente. "
                + "Nessuna connessione di rete, nessun dato raccolto, nessuna modifica al registro, nessun avvio automatico.";
            Controls.Add(_subtitleLabel);

            _btnAnalyze = CreateButton("btnAnalyze", "Analizza", 14, 78, 110, OnAnalyzeClick);
            _btnClean = CreateButton("btnClean", "Pulisci selezionati", 132, 78, 160, OnCleanClick);
            _btnCancel = CreateButton("btnCancel", "Annulla", 300, 78, 100, OnCancelClick);
            _btnReport = CreateButton("btnReport", "Apri report", 408, 78, 120, OnReportClick);

            _statusLabel = new Label();
            _statusLabel.Name = "lblStatus";
            _statusLabel.AutoSize = false;
            _statusLabel.Location = new Point(14, 112);
            _statusLabel.Size = new Size(ClientSize.Width - 28, 20);
            _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _statusLabel.Text = "Pronto.";
            Controls.Add(_statusLabel);

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
            _summaryLabel.Size = new Size(ClientSize.Width - 28, 36);
            _summaryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _summaryLabel.AutoEllipsis = true;
            _summaryLabel.Text = "Nessuna analisi eseguita.";
            Controls.Add(_summaryLabel);

            _reportLabel = new Label();
            _reportLabel.Name = "lblReportPath";
            _reportLabel.AutoSize = false;
            _reportLabel.Location = new Point(14, 200);
            _reportLabel.Size = new Size(ClientSize.Width - 28, 16);
            _reportLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _reportLabel.AutoEllipsis = true;
            _reportLabel.ForeColor = Color.DimGray;
            _reportLabel.Text = "Report: nessuno generato.";
            Controls.Add(_reportLabel);

            _grid = new DataGridView();
            _grid.Name = "gridResults";
            _grid.AccessibleName = "Nessun elemento in elenco";
            _grid.Text = "Nessun elemento in elenco";
            _grid.Location = new Point(14, 222);
            _grid.Size = new Size(ClientSize.Width - 28, ClientSize.Height - 222 - 46);
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = 26;
            BuildGridColumns();
            _grid.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
            _grid.CellValueChanged += OnGridCellValueChanged;
            _grid.CellMouseDown += OnGridCellMouseDown;
            BuildRowContextMenu();
            Controls.Add(_grid);

            _footnoteLabel = new Label();
            _footnoteLabel.Name = "lblFootnote";
            _footnoteLabel.AutoSize = false;
            _footnoteLabel.Location = new Point(14, ClientSize.Height - 40);
            _footnoteLabel.Size = new Size(ClientSize.Width - 28, 34);
            _footnoteLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _footnoteLabel.ForeColor = Color.DimGray;
            _footnoteLabel.Text = "La pulizia è sempre preceduta da una conferma. I file eliminati non sono recuperabili. "
                + "CleanSlate non promette di accelerare il computer: rimuove soltanto file temporanei recuperabili.";
            Controls.Add(_footnoteLabel);
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

        private void UpdateSelectionState()
        {
            if (_loadingGrid) return;
            int selected = CountSelected();
            _btnClean.Enabled = !_busy && selected > 0;
        }

        private void UpdateSummaryLabel()
        {
            if (_lastClean != null)
            {
                _summaryLabel.Text = string.Format(
                    "File eliminati: {0} · Spazio effettivamente liberato: {1} · Saltati: {2} · Errori: {3} · Spazio stimato non recuperato: {4}",
                    _lastClean.DeletedCount,
                    ByteFormatter.Format(_lastClean.DeletedBytes),
                    _lastClean.SkippedCount,
                    _lastClean.ErrorCount,
                    ByteFormatter.Format(_lastClean.EstimatedNotRecovered));
                return;
            }

            if (_lastScan != null)
            {
                string errors;
                if (_lastScan.Errors > 0)
                {
                    string detail = _lastScan.ErrorMessages.Count > 0 ? _lastScan.ErrorMessages[0] : "accesso negato";
                    errors = string.Format(" · Errori di lettura: {0} ({1})", _lastScan.Errors, detail);
                }
                else
                {
                    errors = " · Errori di lettura: 0";
                }

                _summaryLabel.Text = string.Format(
                    "File analizzati: {0} · Proposti: {1} · Scartati: {2}{3} · Spazio recuperabile stimato: {4}",
                    _lastScan.FilesScanned,
                    _lastScan.FilesEligible,
                    _lastScan.FilesSkipped,
                    errors,
                    ByteFormatter.Format(_lastScan.EstimatedBytes));
                return;
            }

            _summaryLabel.Text = "Nessuna analisi eseguita.";
        }

        private static void ShowMessage(string text, string title, MessageBoxIcon icon)
        {
            MessageBox.Show(text, title, MessageBoxButtons.OK, icon);
        }

        private void ShowMessageOwner(string text, string title, MessageBoxIcon icon)
        {
            MessageBox.Show(this, text, title, MessageBoxButtons.OK, icon);
        }

        #endregion

        #region Analisi

        private async void OnAnalyzeClick(object sender, EventArgs e)
        {
            if (_busy) return;

            TempPathResult path = TempPathProvider.GetAuthorizedRoot();
            if (!path.Success)
            {
                SetStatus("Analisi non riuscita.");
                _lastScan = null;
                UpdateSummaryLabel();
                PopulateGrid(null);
                ShowMessageOwner(path.ErrorMessage, "CleanSlate", MessageBoxIcon.Error);
                return;
            }

            _currentRoot = path.Root;
            _lastScan = null;
            _lastClean = null;
            _lastReportPath = null;
            UpdateReportLabel();
            PopulateGrid(null);

            CancellationTokenSource cts = new CancellationTokenSource();
            _cts = cts;

            ScanOptions options = new ScanOptions();
            options.Root = path.Root;
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
                ShowMessageOwner("Si è verificato un errore durante l'analisi: " + ex.Message, "CleanSlate", MessageBoxIcon.Error);
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
                if (_lastScan != null) GenerateReport();
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
                SetStatus(string.Format("Analisi in corso... file trovati: {0} ({1})",
                    progress.FilesFound, ByteFormatter.Format(progress.BytesFound)));
            }
        }

        private void DisplayScanResult(ScanResult result)
        {
            PopulateGrid(result.Items);

            if (!result.Success)
            {
                SetStatus("Analisi non riuscita.");
                ShowMessageOwner(result.ErrorMessage ?? "Errore sconosciuto durante l'analisi.", "CleanSlate", MessageBoxIcon.Error);
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
                    "CleanSlate", MessageBoxIcon.Warning);
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

        private async void OnCleanClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<FileItem> selected = GetSelectedItems();
            if (selected.Count == 0) return;

            TempPathResult path = TempPathProvider.GetAuthorizedRoot();
            if (!path.Success)
            {
                ShowMessageOwner(path.ErrorMessage, "CleanSlate", MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(_currentRoot)
                || !string.Equals(path.Root, _currentRoot, StringComparison.OrdinalIgnoreCase))
            {
                ShowMessageOwner(
                    "La cartella dei file temporanei è cambiata dall'analisi. Eseguire di nuovo l'analisi prima della pulizia.",
                    "CleanSlate", MessageBoxIcon.Warning);
                return;
            }

            long bytes = 0;
            for (int i = 0; i < selected.Count; i++) bytes += selected[i].Length;

            string question = string.Format(
                "Eliminare {0} file per un totale stimato di {1}?\r\n\r\nI file eliminati non potranno essere ripristinati.\r\n\r\nVuole continuare?",
                selected.Count,
                ByteFormatter.Format(bytes));

            DialogResult confirmation = MessageBox.Show(
                this, question, "Conferma pulizia",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            CancellationTokenSource cts = new CancellationTokenSource();
            _cts = cts;

            CleanOptions options = new CleanOptions();
            options.Root = path.Root;
            options.Items = selected;
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
                SetStatus(result.Cancelled ? "Pulizia annullata." : "Pulizia completata.");
                ShowCleanSummary(result);
            }
            catch (Exception ex)
            {
                SetStatus("Pulizia non riuscita.");
                ShowMessageOwner("Si è verificato un errore durante la pulizia: " + ex.Message, "CleanSlate", MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Value = 0;
                if (ReferenceEquals(_cts, cts)) _cts = null;
                try { cts.Dispose(); }
                catch (ObjectDisposedException) { }
                SetBusy(false);
                UpdateSummaryLabel();
                if (_lastClean != null) GenerateReport();
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
                int value = (int)((long)progress.Processed * 100L / progress.Total);
                if (value < 0) value = 0;
                if (value > 100) value = 100;
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

            ShowMessageOwner(text, "CleanSlate", MessageBoxIcon.Information);
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
                GenerateReport();
                if (string.IsNullOrEmpty(_lastReportPath))
                {
                    ShowMessageOwner(
                        "Non ci sono ancora risultati da riportare. Eseguire prima un'analisi.",
                        "CleanSlate", MessageBoxIcon.Information);
                    return;
                }
            }

            OpenReportFile(_lastReportPath);
        }

        /// <summary>
        /// Scrive il report locale al termine di un'analisi o di una pulizia e ne mostra il
        /// percorso nella finestra. Il file resta sulla macchina dell'utente: non viene inviato
        /// a nessuno e non viene scritto in cartelle condivise o sincronizzate.
        /// </summary>
        private void GenerateReport()
        {
            if (_lastScan == null && _lastClean == null) return;

            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CleanSlate", "reports");
                Directory.CreateDirectory(folder);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string candidate = Path.Combine(folder, "cleanslate-" + stamp + ".txt");
                int suffix = 1;
                while (File.Exists(candidate))
                {
                    candidate = Path.Combine(folder,
                        "cleanslate-" + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".txt");
                    suffix++;
                }

                ReportData data = ReportBuilder.Build(_lastScan, _lastClean, _currentRoot);
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

        private void UpdateReportLabel()
        {
            if (_reportLabel == null) return;

            if (!string.IsNullOrEmpty(_lastReportPath) && File.Exists(_lastReportPath))
            {
                _reportLabel.Text = "Report: " + _lastReportPath;
                _reportLabel.ForeColor = Color.DimGray;
            }
            else
            {
                _reportLabel.Text = "Report: nessuno generato. Viene scritto al termine di un'analisi o di una pulizia.";
                _reportLabel.ForeColor = Color.Gray;
            }
        }

        private void OpenReportFile(string path)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path))
            {
                _lastReportPath = null;
                UpdateReportLabel();
                ShowMessageOwner("Il report selezionato non esiste più.", "CleanSlate", MessageBoxIcon.Warning);
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
                ShowMessageOwner("Impossibile aprire il report: " + ex.Message, "CleanSlate", MessageBoxIcon.Error);
            }
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
            else ShowMessageOwner(text, "CleanSlate", MessageBoxIcon.Warning);
        }

        #endregion
    }
}
