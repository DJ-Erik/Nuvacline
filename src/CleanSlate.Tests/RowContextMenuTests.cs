// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
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
    /// Test del menu contestuale dei risultati di scansione (clic destro sulla riga).
    ///
    /// Coprono i requisiti obbligatori: contenuto copiato negli appunti, visibilità
    /// del menu solo su righe valide, invariabilità della selezione destinata alla
    /// pulizia, percorsi con spazi/Unicode, file mancanti, avvio di Esplora file senza
    /// mai eseguire il file, assenza di effetti sul motore di cancellazione e sul report.
    ///
    /// Nessun test apre Esplora file davvero: l'avvio è intercettato tramite il seam
    /// _startProcess. Nessun test tocca il vero percorso temporaneo dell'utente.
    /// </summary>
    public sealed class RowContextMenuTests
    {
        private const string UnicodeFileName = "file (copia) àèì €.tmp";
        private const string UnicodeFolder = "cartella con spazi ø";

        #region Percorsi speciali

        [Test]
        public void RowMenu_SpecialCharacterPaths_AreHandledCorrectly()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile(Path.Combine(UnicodeFolder, UnicodeFileName), 10);

                Assert.Equal(UnicodeFileName, ScanRowActions.GetFileName(path),
                    "Il nome copiato deve essere il solo file name con estensione.");
                Assert.Equal(Path.Combine(fx.Root, UnicodeFolder), ScanRowActions.GetFolder(path),
                    "Il percorso cartella deve contenere la cartella genitrice senza il file.");
                Assert.DoesNotContain(UnicodeFileName, ScanRowActions.GetFolder(path),
                    "Il percorso cartella non deve includere il nome del file.");

                ProcessStartInfo info = ScanRowActions.BuildExplorerSelectStartInfo(path);
                Assert.Equal("explorer.exe", info.FileName,
                    "L'eseguibile avviato deve essere Esplora file.");
                Assert.Equal("/select,\"" + path + "\"", info.Arguments,
                    "Gli argomenti devono essere l'equivalente /select con percorso quotato.");
                Assert.True(info.UseShellExecute, "L'avvio deve usare la shell di sistema.");

                string error;
                Assert.True(ScanRowActions.CanOpenFolder(path, out error),
                    "Un file esistente con spazi e Unicode deve essere apribile.");
                Assert.Null(error, "Nessun messaggio di errore per un file esistente.");
            }
        }

        [Test]
        public void RowMenu_ClipboardRoundTrip_KeepsUnicodeAndSpacesIntact()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile(Path.Combine(UnicodeFolder, UnicodeFileName), 10);
                FileItem item = Fixture.MakeItem(path);
                List<FileItem> items = new List<FileItem>();
                items.Add(item);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        SetField(form, "_rowMenuTarget", item);

                        Invoke(form, "OnCopyNameClick", new object[] { null, EventArgs.Empty });
                        Assert.Equal(0, warnings.Count, "Copia del nome senza avvisi: " + First(warnings));
                        Assert.Equal(UnicodeFileName, ClipboardGet(),
                            "Il nome copiato negli appunti deve conservare spazi e Unicode.");

                        Invoke(form, "OnCopyFolderClick", new object[] { null, EventArgs.Empty });
                        Assert.Equal(0, warnings.Count, "Copia della cartella senza avvisi: " + First(warnings));
                        Assert.Equal(Path.Combine(fx.Root, UnicodeFolder), ClipboardGet(),
                            "Il percorso cartella copiato deve conservare spazi e Unicode.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        #endregion

        #region Appunti

        [Test]
        public void RowMenu_CopyFileName_CopiesOnlyTheFileNameWithoutPath()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile(Path.Combine("sotto cartella", "documento esterno.tmp"), 10);
                FileItem item = Fixture.MakeItem(path);
                List<FileItem> items = new List<FileItem>();
                items.Add(item);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        SetField(form, "_rowMenuTarget", item);

                        Invoke(form, "OnCopyNameClick", new object[] { null, EventArgs.Empty });

                        string copied = ClipboardGet();
                        Assert.Equal(0, warnings.Count,
                            "La copia del nome non deve segnalare errori: " + First(warnings));
                        Assert.Equal("documento esterno.tmp", copied,
                            "Negli appunti deve finire il solo nome del file con estensione.");
                        Assert.DoesNotContain("\\", copied, "Il nome copiato non deve contenere separatore di percorso.");
                        Assert.DoesNotContain(":", copied, "Il nome copiato non deve contenere unità o percorso.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        [Test]
        public void RowMenu_CopyFolderPath_CopiesContainingFolderWithoutFileName()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile(Path.Combine("sotto cartella", "documento.tmp"), 10);
                FileItem item = Fixture.MakeItem(path);
                List<FileItem> items = new List<FileItem>();
                items.Add(item);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        SetField(form, "_rowMenuTarget", item);

                        Invoke(form, "OnCopyFolderClick", new object[] { null, EventArgs.Empty });

                        string copied = ClipboardGet();
                        Assert.Equal(0, warnings.Count,
                            "La copia della cartella non deve segnalare errori: " + First(warnings));
                        Assert.Equal(Path.GetDirectoryName(path), copied,
                            "Negli appunti deve finire la cartella contenente il file.");
                        Assert.True(Directory.Exists(copied), "La cartella copiata deve esistere.");
                        Assert.DoesNotContain("documento.tmp", copied,
                            "Il percorso cartella non deve includere il nome del file finale.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        #endregion

        #region Visibilità del menu

        [Test]
        public void RowMenu_ShouldShow_IsFalseForHeaderEmptyAndInvalidRows()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    DataGridView grid = (DataGridView)GetField(form, "_grid");

                    Assert.False((bool)Invoke(form, "ShouldShowRowMenu", new object[] { -1 }),
                        "L'intestazione (RowIndex -1) non deve mostrare il menu.");
                    Assert.False((bool)Invoke(form, "ShouldShowRowMenu", new object[] { 0 }),
                        "Su una tabella vuota non deve comparire alcun menu.");

                    int blank = grid.Rows.Add(false, "riga senza modello", "", "", "", "");
                    Assert.False((bool)Invoke(form, "ShouldShowRowMenu", new object[] { blank }),
                        "Una riga senza modello file non deve mostrare il menu.");

                    FileItem noPath = new FileItem();
                    noPath.Name = "senza percorso";
                    noPath.FullPath = string.Empty;
                    List<FileItem> items = new List<FileItem>();
                    items.Add(noPath);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Assert.False((bool)Invoke(form, "ShouldShowRowMenu", new object[] { 0 }),
                        "Una riga il cui modello non ha percorso non deve mostrare il menu.");

                    FileItem valid = new FileItem();
                    valid.Name = "valido.tmp";
                    valid.FullPath = @"C:\non\importa\valido.tmp";
                    items.Add(valid);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Assert.True((bool)Invoke(form, "ShouldShowRowMenu", new object[] { 1 }),
                        "Una riga valida con FileItem e percorso deve mostrare il menu.");
                    Assert.False((bool)Invoke(form, "ShouldShowRowMenu", new object[] { 99 }),
                        "Un indice fuori intervallo non deve mostrare il menu.");

                    // L'apertura degradata non deve mai lanciare eccezioni.
                    Invoke(form, "ShowRowMenuFor", new object[] { -1 });
                    Invoke(form, "ShowRowMenuFor", new object[] { 99 });
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void RowMenu_RightClick_DoesNotChangeCleanSelection()
        {
            using (Fixture fx = new Fixture())
            {
                string first = fx.CreateFile("primo.tmp", 100);
                string second = fx.CreateFile("secondo.tmp", 200);
                string third = fx.CreateFile("terzo.tmp", 300);

                List<FileItem> items = new List<FileItem>();
                FileItem a = Fixture.MakeItem(first); a.Selected = true;
                FileItem b = Fixture.MakeItem(second); b.Selected = false;
                FileItem c = Fixture.MakeItem(third); c.Selected = true;
                items.Add(a);
                items.Add(b);
                items.Add(c);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        DataGridView grid = (DataGridView)GetField(form, "_grid");
                        Button cleanButton = (Button)GetField(form, "_btnClean");
                        ContextMenuStrip menu = (ContextMenuStrip)GetField(form, "_rowMenu");
                        Invoke(form, "PopulateGrid", new object[] { items });
                        Invoke(form, "UpdateSelectionState", null);

                        bool[] before = (bool[])Invoke(form, "SnapshotCheckState", null);
                        int selectedBefore = (int)Invoke(form, "CountSelected", null);

                        // Clic destro simulato sulla riga 1 (quella NON spuntata).
                        DataGridViewCellMouseEventArgs args = new DataGridViewCellMouseEventArgs(
                            1, 1, 0, 0, new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0));
                        Invoke(form, "OnGridCellMouseDown", new object[] { form, args });

                        Assert.True((bool)Invoke(form, "ShouldShowRowMenu", new object[] { 1 }),
                            "La riga 1 è valida: il menu avrebbe dovuto essere considerato.");
                        FileItem target = (FileItem)GetField(form, "_rowMenuTarget");
                        Assert.NotNull(target, "Il menu deve avere come target la riga cliccata.");
                        Assert.Equal(second, target.FullPath, "Il target deve essere il file della riga cliccata.");
                        Assert.False(menu.Visible,
                            "Nei test non deve restare alcun menu aperto (nessun message loop).");

                        bool[] after = (bool[])Invoke(form, "SnapshotCheckState", null);
                        Assert.Equal(before.Length, after.Length, "Il numero di righe non deve cambiare.");
                        for (int i = 0; i < before.Length; i++)
                            Assert.Equal(before[i], after[i],
                                "Il clic destro non deve cambiare gli spunti della riga " + i + ".");

                        Assert.Equal(selectedBefore, (int)Invoke(form, "CountSelected", null),
                            "Il conteggio della selezione deve restare invariato.");
                        List<FileItem> selected = (List<FileItem>)Invoke(form, "GetSelectedItems", null);
                        Assert.Equal(2, selected.Count, "Sempre due elementi selezionati.");
                        Assert.True(cleanButton.Enabled,
                            "Lo stato del pulsante di pulizia non deve cambiare dopo il clic destro.");

                        // La chiusura del menu deve lasciare la selezione esattamente come prima.
                        Invoke(form, "OnRowMenuClosed",
                            new object[] { menu, new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.CloseCalled) });
                        bool[] restored = (bool[])Invoke(form, "SnapshotCheckState", null);
                        for (int i = 0; i < before.Length; i++)
                            Assert.Equal(before[i], restored[i],
                                "Anche dopo la chiusura del menu la selezione deve essere intatta.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        [Test]
        public void RowMenu_ActionStillWorksWithTheRealEventOrderClosedBeforeClick()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile("ordine reale.tmp", 10);
                FileItem item = Fixture.MakeItem(path);
                List<FileItem> items = new List<FileItem>();
                items.Add(item);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });

                        // L'apertura del menu imposta il target.
                        Invoke(form, "ShowRowMenuFor", new object[] { 0 });
                        Assert.NotNull(GetField(form, "_rowMenuTarget"),
                            "L'apertura deve impostare il target della riga.");

                        // Nei GUI reali la chiusura del menu viene distribuita PRIMA del
                        // Click della voce: l'azione deve funzionare comunque.
                        ContextMenuStrip menu = (ContextMenuStrip)GetField(form, "_rowMenu");
                        Invoke(form, "OnRowMenuClosed",
                            new object[] { menu, new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.CloseCalled) });
                        Invoke(form, "OnCopyNameClick", new object[] { null, EventArgs.Empty });

                        Assert.Equal(0, warnings.Count,
                            "La copia deve riuscire nonostante la chiusura sia avvenuta per prima: "
                            + First(warnings));
                        Assert.Equal("ordine reale.tmp", ClipboardGet(),
                            "Il nome deve finire negli appunti anche nell'ordine reale degli eventi.");

                        // Nessuna azione con target assente deve mai crasare.
                        SetField(form, "_rowMenuTarget", null);
                        warnings.Clear();
                        Invoke(form, "OnCopyNameClick", new object[] { null, EventArgs.Empty });
                        Assert.Equal(1, warnings.Count, "Senza target deve arrivare un messaggio chiaro.");
                        Assert.Contains("non è associata", warnings[0],
                            "Il messaggio deve indicare un target non valido.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        #endregion

        #region File mancanti ed Esplora file

        [Test]
        public void RowMenu_MissingFile_ReportsClearMessageWithoutCrashOrProcess()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile("sparito dopo la scansione.tmp", 10);
                FileItem item = Fixture.MakeItem(path);
                List<FileItem> items = new List<FileItem>();
                items.Add(item);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        List<ProcessStartInfo> starts = new List<ProcessStartInfo>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        SetField(form, "_startProcess", new Action<ProcessStartInfo>(starts.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });

                        // Il file viene eliminato DOPO la scansione, come da specifica.
                        File.Delete(path);
                        Assert.False(File.Exists(path), "Il file di test deve essere stato eliminato.");

                        SetField(form, "_rowMenuTarget", item);
                        Invoke(form, "OnOpenFolderClick", new object[] { null, EventArgs.Empty });

                        Assert.Equal(1, warnings.Count,
                            "Deve essere mostrato esattamente un messaggio chiaro, senza eccezioni.");
                        Assert.Contains("non esiste più", warnings[0],
                            "Il messaggio deve spiegare che il file non esiste più.");
                        Assert.Equal(0, starts.Count,
                            "Con il file mancante non deve essere avviato alcun processo.");

                        // Le operazioni di consultazione sugli appunti restano utilizzabili.
                        warnings.Clear();
                        SetField(form, "_rowMenuTarget", item);
                        Invoke(form, "OnCopyNameClick", new object[] { null, EventArgs.Empty });
                        Assert.Equal(0, warnings.Count, "Copia del nome su file mancante senza errori.");
                        Assert.Equal("sparito dopo la scansione.tmp", ClipboardGet(),
                            "Il nome del file deve potersi copiare anche se il file non esiste più.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        [Test]
        public void RowMenu_OpenFolder_StartsExplorerSelectAndNeverExecutesTheFile()
        {
            using (Fixture fx = new Fixture())
            {
                string path = fx.CreateFile(Path.Combine(UnicodeFolder, "da non eseguire.tmp"), 10);
                FileItem item = Fixture.MakeItem(path);
                List<FileItem> items = new List<FileItem>();
                items.Add(item);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        List<ProcessStartInfo> starts = new List<ProcessStartInfo>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        SetField(form, "_startProcess", new Action<ProcessStartInfo>(starts.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        SetField(form, "_rowMenuTarget", item);

                        Invoke(form, "OnOpenFolderClick", new object[] { null, EventArgs.Empty });

                        Assert.Equal(0, warnings.Count, "Apertura senza avvisi: " + First(warnings));
                        Assert.Equal(1, starts.Count, "Deve essere avviato un solo processo.");
                        Assert.Equal("explorer.exe", starts[0].FileName,
                            "L'unico eseguibile avviato deve essere Esplora file, mai il file stesso.");
                        Assert.Equal("/select,\"" + path + "\"", starts[0].Arguments,
                            "Il percorso deve passare come argomento /select quotato, non come eseguibile.");
                        Assert.DoesNotContain("da non eseguire.tmp", starts[0].FileName,
                            "Il file target non deve mai comparire come FileName del processo.");
                        Assert.True(File.Exists(path),
                            "Il file deve essere ancora intatto: nessuna esecuzione né cancellazione.");

                        // Verifica pura anche sul builder usato davvero dall'applicazione.
                        ProcessStartInfo built = ScanRowActions.BuildExplorerSelectStartInfo(path);
                        Assert.Equal("explorer.exe", built.FileName, "Builder: FileName explorer.exe.");
                        Assert.True(built.Arguments.StartsWith("/select,\"", StringComparison.Ordinal),
                            "Builder: gli argomenti iniziano con /select, quotato.");
                        Assert.True(built.Arguments.EndsWith("\"", StringComparison.Ordinal),
                            "Builder: il percorso è chiuso dalla virgoletta finale.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        #endregion

        #region Nessun effetto sul motore

        [Test]
        public void RowMenu_Actions_DoNotDeleteAnythingOrChangeReportState()
        {
            using (Fixture fx = new Fixture())
            {
                string keep = fx.CreateFile("da conservare.tmp", 100);
                string alsoKeep = fx.CreateFile("altro da conservare.tmp", 200);
                FileItem first = Fixture.MakeItem(keep);
                FileItem second = Fixture.MakeItem(alsoKeep);
                List<FileItem> items = new List<FileItem>();
                items.Add(first);
                items.Add(second);

                Dictionary<string, string> before = Fixture.Snapshot(fx.Root);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        List<string> warnings = new List<string>();
                        List<string> clipboard = new List<string>();
                        List<ProcessStartInfo> starts = new List<ProcessStartInfo>();
                        SetField(form, "_notifyUser", new Action<string>(warnings.Add));
                        SetField(form, "_setClipboard", new Action<string>(clipboard.Add));
                        SetField(form, "_startProcess", new Action<ProcessStartInfo>(starts.Add));
                        Invoke(form, "PopulateGrid", new object[] { items });
                        Invoke(form, "UpdateSelectionState", null);
                        SetField(form, "_rowMenuTarget", first);

                        object reportBefore = GetField(form, "_lastReportPath");
                        object cleanBefore = GetField(form, "_lastClean");
                        object scanBefore = GetField(form, "_lastScan");

                        Invoke(form, "OnCopyNameClick", new object[] { null, EventArgs.Empty });
                        Invoke(form, "OnCopyFolderClick", new object[] { null, EventArgs.Empty });
                        Invoke(form, "OnOpenFolderClick", new object[] { null, EventArgs.Empty });

                        Assert.Equal(0, warnings.Count, "Nessun errore dalle azioni: " + First(warnings));
                        Assert.Equal(2, clipboard.Count, "Due copie negli appunti (nome e cartella).");
                        Assert.Equal(1, starts.Count, "Un solo avvio: Esplora file.");
                        Assert.Equal("explorer.exe", starts[0].FileName, "Avviato solo explorer.exe.");

                        DataGridView grid = (DataGridView)GetField(form, "_grid");
                        Assert.Equal(2, grid.Rows.Count, "Il menu non deve rimuovere righe.");
                        Assert.Equal(2, (int)Invoke(form, "CountSelected", null),
                            "Il menu non deve cambiare la selezione.");
                        Assert.Equal(reportBefore, GetField(form, "_lastReportPath"),
                            "Il menu non deve generare né modificare il report.");
                        Assert.Equal(cleanBefore, GetField(form, "_lastClean"),
                            "Il menu non deve innescare la pulizia.");
                        Assert.Equal(scanBefore, GetField(form, "_lastScan"),
                            "Il menu non deve riavviare l'analisi.");
                    }
                }, out failure);
                if (failure != null) throw failure;

                Dictionary<string, string> after = Fixture.Snapshot(fx.Root);
                Fixture.AssertSameSnapshot(before, after,
                    "Le azioni del menu non devono cancellare o modificare alcun file.");

                Assert.True(File.Exists(keep), "Il primo file deve essere ancora presente.");
                Assert.True(File.Exists(alsoKeep), "Il secondo file deve essere ancora presente.");
            }
        }

        [Test]
        public void RowMenu_Logic_IsArchitecturallySeparatedFromCleaningEngine()
        {
            MethodInfo[] methods = typeof(ScanRowActions).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.True(methods.Length >= 5,
                "La logica del menu deve esporre le sue funzioni (trovate: " + methods.Length + ").");

            Type[] forbidden = new Type[]
            {
                typeof(TempFileCleaner), typeof(CleanOptions), typeof(CleanResult),
                typeof(CleanItemResult), typeof(TempFolderScanner)
            };

            for (int i = 0; i < methods.Length; i++)
            {
                Assert.False(Contains(forbidden, methods[i].ReturnType),
                    "La logica del menu non deve dipendere dal motore: " + methods[i].Name);
                ParameterInfo[] parameters = methods[i].GetParameters();
                for (int j = 0; j < parameters.Length; j++)
                    Assert.False(Contains(forbidden, parameters[j].ParameterType),
                        "La logica del menu non deve dipendere dal motore: " + methods[i].Name);
            }

            FieldInfo[] fields = typeof(ScanRowActions).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
                Assert.False(Contains(forbidden, fields[i].FieldType),
                    "La logica del menu non deve mantenere stati del motore: " + fields[i].Name);
        }

        #endregion

        #region Utilità

        private static bool Contains(Type[] types, Type candidate)
        {
            if (candidate == null) return false;
            for (int i = 0; i < types.Length; i++)
                if (types[i] == candidate) return true;
            return false;
        }

        private static string First(List<string> values)
        {
            return values.Count > 0 ? values[0] : "(nessuno)";
        }

        private static string ClipboardGet()
        {
            Exception last = null;
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.GetText(TextDataFormat.UnicodeText); }
                catch (Exception ex) { last = ex; Thread.Sleep(50); }
            }
            throw new AssertException("Impossibile leggere gli appunti: " + last.Message);
        }

        private static object GetField(object target, string name)
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
