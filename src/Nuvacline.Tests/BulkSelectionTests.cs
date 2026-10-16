// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Nuvacline.Core;
using Nuvacline.UI;

namespace Nuvacline.Tests
{
    /// <summary>
    /// Test dei comandi di selezione massiva (Seleziona tutto / Deseleziona tutto)
    /// e della sincronizzazione checkbox → conteggio → motore di pulizia.
    ///
    /// Coprono i requisiti obbligatori: selezione di tutti i file validi, azzeramento,
    /// selezione parziale, conteggio, nuova scansione, righe non-file, compatibilità
    /// con il menu contestuale, esclusione dei file non selezionati dal motore,
    /// assenza di pulizia automatica e conferma ancora obbligatoria.
    ///
    /// Nessun test tocca il vero percorso temporaneo dell'utente né avvia Esplora file.
    /// </summary>
    public sealed class BulkSelectionTests
    {
        #region Comandi base

        [Test]
        public void SelectAll_SelectsEveryValidFileRow()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 4);
                    Invoke(form, "PopulateGrid", new object[] { items });

                    SetChecks(form, new bool[] { false, false, false, false });
                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });

                    Assert.Equal(4, CountChecked(form),
                        "Seleziona tutto deve spuntare tutte le righe.");
                    for (int i = 0; i < items.Count; i++)
                        Assert.True(items[i].Selected,
                            "FileItem.Selected deve essere sincronizzato dopo Seleziona tutto.");
                    Assert.True(GetField<Button>(form, "_btnClean").Enabled,
                        "Con almeno un file selezionato il pulsante Pulisci deve essere abilitato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void DeselectAll_ClearsEveryCheckedRow()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 3);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });
                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });

                    Assert.Equal(0, CountChecked(form),
                        "Deseleziona tutto deve azzerare tutti gli spunti.");
                    for (int i = 0; i < items.Count; i++)
                        Assert.False(items[i].Selected,
                            "FileItem.Selected deve essere false dopo Deseleziona tutto.");
                    Assert.False(GetField<Button>(form, "_btnClean").Enabled,
                        "Con nessun file selezionato il pulsante Pulisci deve essere disabilitato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void BulkCommands_DoNotStartCleaningOrTouchFiles()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 3);
                    Invoke(form, "PopulateGrid", new object[] { items });

                    Dictionary<string, string> before = Fixture.Snapshot(fx.Root);
                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });
                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });
                    Dictionary<string, string> after = Fixture.Snapshot(fx.Root);

                    Fixture.AssertSameSnapshot(before, after,
                        "I comandi di selezione massiva non devono modificare alcun file.");
                    Assert.Null(GetField<object>(form, "_lastClean"),
                        "I comandi di selezione massiva non devono produrre un CleanResult.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Selezione parziale e conteggio

        [Test]
        public void PartialSelection_KeepsOnlyCheckedItems()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 4);
                    Invoke(form, "PopulateGrid", new object[] { items });

                    SetChecks(form, new bool[] { true, false, true, false });
                    List<FileItem> selected = InvokeGetSelected(form);

                    Assert.Equal(2, selected.Count,
                        "La selezione parziale deve includere solo gli elementi spuntati.");
                    Assert.Equal(items[0].FullPath, selected[0].FullPath,
                        "Il primo elemento spuntato deve restare il primo.");
                    Assert.Equal(items[2].FullPath, selected[1].FullPath,
                        "Il terzo elemento spuntato deve restare il terzo.");
                    Assert.False(items[1].Selected, "Il secondo elemento non deve essere selezionato.");
                    Assert.False(items[3].Selected, "Il quarto elemento non deve essere selezionato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void SelectionCount_IsCorrectAfterBulkAndManualChanges()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 5);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });

                    Label label = GetField<Label>(form, "_lblSelection");
                    Assert.Equal("Nessun file selezionato", label.Text,
                        "Con zero spunti l'etichetta deve riportare nessuna selezione.");

                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });
                    Assert.True(label.Text.StartsWith("5 file selezionati"),
                        "Con 5 file l'etichetta deve riportare il conteggio: " + label.Text);

                    SetChecks(form, new bool[] { true, true, false, false, false });
                    Assert.True(label.Text.StartsWith("2 file selezionati"),
                        "Dopo una deselezione manuale l'etichetta deve riportare 2: " + label.Text);

                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });
                    Assert.Equal("Nessun file selezionato", label.Text,
                        "Dopo Deseleziona tutto l'etichetta deve tornare a nessuna selezione.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void SelectionLabel_ReportsTotalSelectedSize()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 2);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });

                    Label label = GetField<Label>(form, "_lblSelection");
                    long total = items[0].Length + items[1].Length;
                    string expectedFragment = ByteFormatter.Format(total);
                    Assert.Contains(expectedFragment, label.Text,
                        "L'etichetta deve includere la dimensione totale selezionata: " + label.Text);
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Nuova scansione e righe non-file

        [Test]
        public void NewScan_DoesNotReuseStaleSelections()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> first = CreateItems(fx, 3);
                    Invoke(form, "PopulateGrid", new object[] { first });
                    SetChecks(form, new bool[] { false, false, false });

                    List<FileItem> second = new List<FileItem>();
                    second.Add(MakeItem(fx.CreateFile("nuovo.tmp", 10)));
                    second.Add(MakeItem(fx.CreateFile("nuovo2.tmp", 20)));
                    Invoke(form, "PopulateGrid", new object[] { second });

                    Assert.Equal(2, CountChecked(form),
                        "La nuova scansione deve mostrare solo le nuove righe con lo stato del modello.");
                    Assert.True(second[0].Selected && second[1].Selected,
                        "Gli elementi nuovi devono mantenere lo stato Selected del modello.");
                    Assert.Equal(2, CountRows(form),
                        "Le righe della scansione precedente non devono restare in griglia.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void RowsWithoutFileItem_AreNotSelectedByBulkCommands()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 2);
                    Invoke(form, "PopulateGrid", new object[] { items });

                    DataGridView grid = GetField<DataGridView>(form, "_grid");
                    int bare = grid.Rows.Add(false, "riga senza modello", "C:\\fake\\x.tmp", "0 KB", "", "");
                    grid.Rows[bare].Tag = null;

                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });

                    Assert.True((bool)grid.Rows[0].Cells["colSelected"].Value,
                        "La prima riga con FileItem deve essere selezionata.");
                    Assert.True((bool)grid.Rows[1].Cells["colSelected"].Value,
                        "La seconda riga con FileItem deve essere selezionata.");
                    Assert.False(grid.Rows[bare].Cells["colSelected"].Value is bool
                            && (bool)grid.Rows[bare].Cells["colSelected"].Value,
                        "La riga senza FileItem non deve essere selezionata da Seleziona tutto.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Motore di pulizia e conferma

        [Test]
        public void Engine_ReceivesOnlyExplicitlySelectedItems()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string keepA = fx.CreateFile("tenere.tmp", 10);
                    string deleteMe = fx.CreateFile("cancellare.tmp", 20);
                    string keepB = fx.CreateFile("tenere2.tmp", 30);

                    List<FileItem> items = new List<FileItem>();
                    items.Add(MakeItem(keepA));
                    items.Add(MakeItem(deleteMe));
                    items.Add(MakeItem(keepB));
                    Invoke(form, "PopulateGrid", new object[] { items });

                    SetChecks(form, new bool[] { false, true, false });
                    List<FileItem> selected = InvokeGetSelected(form);
                    Assert.Equal(1, selected.Count, "Solo un file deve essere selezionato.");
                    Assert.Equal(deleteMe, selected[0].FullPath,
                        "Il motore deve ricevere esclusivamente il file spuntato.");

                    TempFileCleaner cleaner = new TempFileCleaner();
                    CleanOptions options = new CleanOptions();
                    options.Root = fx.Root;
                    options.Items = selected;
                    CleanResult result = cleaner.Clean(options);

                    Assert.Equal(1, result.DeletedCount, "Deve essere eliminato solo il file selezionato.");
                    Assert.False(File.Exists(deleteMe), "Il file selezionato deve essere stato eliminato.");
                    Assert.True(File.Exists(keepA), "Il primo file non selezionato deve restare.");
                    Assert.True(File.Exists(keepB), "Il secondo file non selezionato deve restare.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void Clean_WithNoSelection_DoesNothingAndKeepsFiles()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 2);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });

                    Dictionary<string, string> before = Fixture.Snapshot(fx.Root);
                    Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                    Dictionary<string, string> after = Fixture.Snapshot(fx.Root);

                    Fixture.AssertSameSnapshot(before, after,
                        "Con nessuna selezione la pulizia non deve eliminare alcun file.");
                    Assert.Null(GetField<object>(form, "_lastClean"),
                        "Con nessuna selezione non deve essere prodotto alcun risultato di pulizia.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void CleanConfirmation_UsesOnlySelectedCountAndBytes()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 3);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    SetChecks(form, new bool[] { true, false, true });

                    TempPathResult path = TempPathProvider.GetAuthorizedRoot();
                    Assert.True(path.Success,
                        "Il percorso autorizzato deve essere disponibile per il test di conferma.");

                    string question = (string)Invoke(form, "BuildCleanQuestion",
                        new object[] { InvokeGetSelected(form) });
                    Assert.Contains("2 file", question,
                        "Il dialogo di conferma deve riferirsi ai 2 file selezionati: " + question);
                    Assert.Contains(ByteFormatter.Format(items[0].Length + items[2].Length), question,
                        "Il dialogo di conferma deve riferirsi alla dimensione dei soli file selezionati.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Compatibilità menu contestuale

        [Test]
        public void RightClick_PreservesPartialSelectionAcrossMenuShowAndClose()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 3);
                    Invoke(form, "PopulateGrid", new object[] { items });
                    SetChecks(form, new bool[] { true, false, true });

                    bool[] before = SnapshotChecks(form);
                    Invoke(form, "ShowRowMenuFor", new object[] { 0 });
                    Invoke(form, "OnRowMenuClosed",
                        new object[] { null, new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.CloseCalled) });

                    bool[] after = SnapshotChecks(form);
                    for (int i = 0; i < before.Length; i++)
                        Assert.Equal(before[i], after[i],
                            "L'apertura e la chiusura del menu non devono alterare la selezione parziale.");
                    Assert.Equal(2, CountChecked(form),
                        "La selezione parziale deve sopravvivere al menu contestuale.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void BulkCommands_WorkAfterContextMenuClose()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    List<FileItem> items = CreateItems(fx, 2);
                    Invoke(form, "PopulateGrid", new object[] { items });

                    Invoke(form, "ShowRowMenuFor", new object[] { 0 });
                    Invoke(form, "OnRowMenuClosed",
                        new object[] { null, new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.CloseCalled) });

                    Invoke(form, "OnDeselectAllClick", new object[] { null, EventArgs.Empty });
                    Assert.Equal(0, CountChecked(form),
                        "Deseleziona tutto deve funzionare anche dopo la chiusura del menu.");

                    Invoke(form, "OnSelectAllClick", new object[] { null, EventArgs.Empty });
                    Assert.Equal(2, CountChecked(form),
                        "Seleziona tutto deve funzionare anche dopo la chiusura del menu.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Utilità

        private static List<FileItem> CreateItems(Fixture fx, int count)
        {
            List<FileItem> items = new List<FileItem>();
            for (int i = 0; i < count; i++)
                items.Add(MakeItem(fx.CreateFile("file" + i + ".tmp", 10 * (i + 1))));
            return items;
        }

        private static FileItem MakeItem(string path)
        {
            return Fixture.MakeItem(path);
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

        private static int CountRows(MainForm form)
        {
            return GetField<DataGridView>(form, "_grid").Rows.Count;
        }

        private static void SetChecks(MainForm form, bool[] state)
        {
            DataGridView grid = GetField<DataGridView>(form, "_grid");
            for (int i = 0; i < state.Length && i < grid.Rows.Count; i++)
                grid.Rows[i].Cells["colSelected"].Value = state[i];
        }

        private static bool[] SnapshotChecks(MainForm form)
        {
            DataGridView grid = GetField<DataGridView>(form, "_grid");
            bool[] state = new bool[grid.Rows.Count];
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                object value = grid.Rows[i].Cells["colSelected"].Value;
                state[i] = value is bool && (bool)value;
            }
            return state;
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
