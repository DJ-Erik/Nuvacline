// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Nuvacline.Core;
using Nuvacline.UI;

namespace Nuvacline.Tests
{
    /// <summary>
    /// Test dell'audit di sicurezza pre-distribuzione.
    ///
    /// Coprono le lacune emerse dall'analisi del percorso scansione → cancellazione:
    ///  - cosa succede se un file viene sostituito DOPO la scansione (race condition);
    ///  - cosa succede se una componente del percorso diventa una junction DOPO la scansione;
    ///  - che solo gli elementi effettivamente selezionati arrivino al motore di cancellazione;
    ///  - che la GUI rifletta la selezione senza ambiguità.
    ///
    /// Tutti i file sono sintetici e vivono nella fixture isolata: nessun test tocca il
    /// vero percorso temporaneo dell'utente e nessun test cancella dati reali.
    /// </summary>
    public sealed class SecurityAuditTests
    {
        private static CleanOptions OptionsFor(string root, List<FileItem> items)
        {
            CleanOptions options = new CleanOptions();
            options.Root = root;
            options.Items = items;
            return options;
        }

        private static List<FileItem> ItemsFor(params string[] paths)
        {
            List<FileItem> items = new List<FileItem>();
            for (int i = 0; i < paths.Length; i++) items.Add(Fixture.MakeItem(paths[i]));
            return items;
        }

        private static List<FileItem> One(FileItem item)
        {
            List<FileItem> items = new List<FileItem>();
            items.Add(item);
            return items;
        }

        private static CleanOutcome FindOutcome(CleanResult result, string fullPath)
        {
            for (int i = 0; i < result.Items.Count; i++)
            {
                if (string.Equals(result.Items[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return result.Items[i].Outcome;
            }
            throw new InvalidOperationException("Nessun esito registrato per: " + fullPath);
        }

        private static bool ContainsPath(List<FileItem> items, string fullPath)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string CreateOldFile(string fullPath, int sizeBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using (FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                byte[] data = new byte[sizeBytes];
                stream.Write(data, 0, data.Length);
            }
            File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow - TimeSpan.FromDays(2));
            return fullPath;
        }

        #region Race condition sul percorso

        [Test]
        public void Clean_FileModifiedAfterScan_IsSkippedAndSurvives()
        {
            using (Fixture fx = new Fixture())
            {
                string touched = fx.CreateFile("toccatodoposcansione.tmp", 600);
                string untouched = fx.CreateFile("intatto.tmp", 400);

                ScanResult scan = new TempFolderScanner().Scan(new ScanOptions { Root = fx.Root });
                Assert.Equal(2, scan.Items.Count, "La scansione deve trovare due file.");

                // Un altro processo modifica il file dopo la scansione e prima della pulizia.
                File.SetLastWriteTimeUtc(touched, DateTime.UtcNow);

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, scan.Items));

                Assert.True(result.Success, "La pulizia deve riuscire.");
                Assert.Equal(1, result.DeletedCount, "Solo il file non toccato deve essere eliminato.");
                Assert.Equal(CleanOutcome.SkippedTooRecent, FindOutcome(result, touched),
                    "Un file toccato dopo la scansione deve essere saltato come non conforme all'età.");
                Assert.True(File.Exists(touched), "Il file toccato deve restare intatto.");
                Assert.False(File.Exists(untouched), "Il file non toccato deve essere eliminato.");
            }
        }

        [Test]
        public void Clean_FileReplacedByDirectoryAfterScan_SkipsAndContentsSurvive()
        {
            using (Fixture fx = new Fixture())
            {
                string file = CreateOldFile(Path.Combine(fx.Root, "sostituito.tmp"), 512);
                FileItem item = Fixture.MakeItem(file);

                // Dopo la scansione un altro processo sostituisce il file con una directory.
                File.Delete(file);
                Directory.CreateDirectory(file);
                string inner = Path.Combine(file, "interno", "protetto.tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(inner));
                File.WriteAllText(inner, "contenuto che deve sopravvivere");

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, One(item)));

                Assert.Equal(0, result.DeletedCount, "Una directory al posto del file non deve essere toccata.");
                Assert.True(Directory.Exists(file), "La directory sostitutiva deve restare.");
                Assert.True(File.Exists(inner), "Il contenuto della directory deve sopravvivere.");
            }
        }

        [Test]
        public void Clean_PathComponentReplacedByJunctionAfterScan_IsSkippedAndTargetSurvives()
        {
            using (Fixture fx = new Fixture())
            {
                string sub = Path.Combine(fx.Root, "sotto");
                string file = CreateOldFile(Path.Combine(sub, "vittima.tmp"), 512);
                FileItem item = Fixture.MakeItem(file);

                // Dopo la scansione la sottocartella viene sostituita da una junction:
                // il percorso testuale resta identico ma punta fuori dalla radice autorizzata.
                File.Delete(file);
                Directory.Delete(sub);
                string victim = fx.CreateOutsideFile("vittima.tmp", 999, TimeSpan.FromDays(2));
                Fixture.CreateJunction(sub, fx.Outside);

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, One(item)));

                Assert.Equal(0, result.DeletedCount,
                    "Un percorso che attraversa una junction sostituita dopo la scansione non deve essere cancellato.");
                Assert.Equal(CleanOutcome.SkippedReparse, FindOutcome(result, file),
                    "Il reparse point deve essere rilevato al momento della pulizia.");
                Assert.True(File.Exists(victim), "Il file di destinazione della junction deve sopravvivere.");
            }
        }

        #endregion

        #region Selezione

        [Test]
        public void Clean_OnlyItemsExplicitlyPassedToCleanerAreDeleted()
        {
            using (Fixture fx = new Fixture())
            {
                string selected = fx.CreateFile("selezionato.tmp", 100);
                string unselected = fx.CreateFile("nonselezionato.tmp", 200);
                string alsoSelected = fx.CreateFile("ancheesso.tmp", 300);

                // Il motore deve cancellare ESCLUSIVAMENTE la lista che riceve:
                // è questa la garanzia che un elemento deselezionato non possa essere rimosso.
                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(selected, alsoSelected)));

                Assert.Equal(2, result.DeletedCount, "Solo gli elementi passati devono essere eliminati.");
                Assert.True(File.Exists(unselected), "L'elemento non passato deve sopravvivere.");
                Assert.False(File.Exists(selected), "L'elemento passato deve essere eliminato.");
                Assert.False(File.Exists(alsoSelected), "L'altro elemento passato deve essere eliminato.");
            }
        }

        [Test]
        public void Ui_CleanButtonIsDisabledWhenNothingIsSelected()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Button button = (Button)GetField(form, "_btnClean");
                    DataGridView grid = (DataGridView)GetField(form, "_grid");

                    Assert.False(button.Enabled,
                        "Senza alcuna selezione il pulsante di pulizia deve restare disabilitato.");
                    Assert.Equal(0, grid.Rows.Count, "All'avvio la tabella deve essere vuota.");
                    Assert.Equal("Nessun elemento in elenco", grid.AccessibleName,
                        "All'avvio la tabella deve dichiarare che è vuota.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void Ui_UncheckedItemsAreNotPassedToCleanerAndSurvive()
        {
            using (Fixture fx = new Fixture())
            {
                string first = fx.CreateFile("primo.tmp", 100);
                string deselected = fx.CreateFile("deselezionato.tmp", 200);
                string last = fx.CreateFile("ultimo.tmp", 300);

                List<FileItem> items = new List<FileItem>();
                FileItem a = Fixture.MakeItem(first); a.Selected = true;
                FileItem b = Fixture.MakeItem(deselected); b.Selected = false;
                FileItem c = Fixture.MakeItem(last); c.Selected = true;
                items.Add(a);
                items.Add(b);
                items.Add(c);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        DataGridView grid = (DataGridView)GetField(form, "_grid");
                        Button button = (Button)GetField(form, "_btnClean");

                        Invoke(form, "PopulateGrid", new object[] { items });
                        Assert.Equal(3, grid.Rows.Count, "La tabella deve mostrare tre righe.");
                        Assert.True(Convert.ToBoolean(grid.Rows[0].Cells[0].Value), "La riga 0 deve apparire selezionata.");
                        Assert.False(Convert.ToBoolean(grid.Rows[1].Cells[0].Value),
                            "La riga deselezionata deve apparire NON selezionata.");
                        Assert.True(Convert.ToBoolean(grid.Rows[2].Cells[0].Value), "La riga 2 deve apparire selezionata.");

                        Invoke(form, "UpdateSelectionState", null);
                        Assert.True(button.Enabled, "Con due elementi selezionati la pulizia deve essere abilitata.");

                        int counted = (int)Invoke(form, "CountSelected", null);
                        Assert.Equal(2, counted, "Il conteggio della selezione deve essere due.");

                        List<FileItem> selected = (List<FileItem>)Invoke(form, "GetSelectedItems", null);
                        Assert.Equal(2, selected.Count, "Solo due elementi devono uscire dalla selezione.");
                        Assert.True(ContainsPath(selected, first), "Il primo file deve essere selezionato.");
                        Assert.True(ContainsPath(selected, last), "L'ultimo file deve essere selezionato.");
                        Assert.False(ContainsPath(selected, deselected),
                            "L'elemento deselezionato NON deve essere passato al motore di pulizia.");

                        CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, selected));
                        Assert.Equal(2, result.DeletedCount, "Solo i due selezionati devono essere eliminati.");
                        Assert.True(File.Exists(deselected),
                            "L'elemento deselezionato deve sopravvivere alla pulizia partita dalla GUI.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        [Test]
        public void Ui_SelectionIsPreservedWhenTheListIsSorted()
        {
            using (Fixture fx = new Fixture())
            {
                string alpha = fx.CreateFile("alfa.tmp", 100);
                string beta = fx.CreateFile("beta.tmp", 200);
                string gamma = fx.CreateFile("gamma.tmp", 300);

                List<FileItem> items = new List<FileItem>();
                FileItem a = Fixture.MakeItem(alpha); a.Selected = true;
                FileItem b = Fixture.MakeItem(beta); b.Selected = false;
                FileItem c = Fixture.MakeItem(gamma); c.Selected = true;
                items.Add(a);
                items.Add(b);
                items.Add(c);

                Exception failure = null;
                RunInSta(delegate
                {
                    using (MainForm form = new MainForm())
                    {
                        DataGridView grid = (DataGridView)GetField(form, "_grid");
                        Invoke(form, "PopulateGrid", new object[] { items });

                        grid.Sort(grid.Columns["colName"], ListSortDirection.Descending);

                        List<FileItem> selected = (List<FileItem>)Invoke(form, "GetSelectedItems", null);
                        Assert.Equal(2, selected.Count,
                            "L'ordinamento non deve cambiare quanti elementi sono selezionati.");
                        Assert.False(ContainsPath(selected, beta),
                            "L'elemento deselezionato deve restare deselezionato anche dopo l'ordinamento.");
                        Assert.True(ContainsPath(selected, alpha), "alfa.tmp deve restare selezionato.");
                        Assert.True(ContainsPath(selected, gamma), "gamma.tmp deve restare selezionato.");
                    }
                }, out failure);
                if (failure != null) throw failure;
            }
        }

        #endregion

        #region Trucchi sui percorsi

        [Test]
        public void PathSafety_TrailingSpaceOrDot_StillResolveInsideRoot()
        {
            using (Fixture fx = new Fixture())
            {
                string real = fx.CreateFile("normale.tmp", 10);
                string withSpace = real + " ";
                string withDot = real + ".";

                // .NET Framework rimuove spazi e punti finali durante GetFullPath: il percorso
                // "truccato" finisce comunque dentro la radice autorizzata, mai fuori.
                Assert.True(PathSafety.IsInsideRoot(fx.Root, withSpace),
                    "Un percorso con spazio finale deve normalizzarsi dentro la radice.");
                Assert.True(PathSafety.IsInsideRoot(fx.Root, withDot),
                    "Un percorso con punto finale deve normalizzarsi dentro la radice.");
                Assert.Equal(PathSafety.Normalize(real), PathSafety.Normalize(withSpace),
                    "Lo spazio finale non deve cambiare il file individuato.");
                Assert.Equal(PathSafety.Normalize(real), PathSafety.Normalize(withDot),
                    "Il punto finale non deve cambiare il file individuato.");
            }
        }

        [Test]
        public void PathSafety_PrefixedDeviceOrDosDevicePath_IsRejected()
        {
            using (Fixture fx = new Fixture())
            {
                string real = fx.CreateFile("prefisso.tmp", 10);

                // I percorsi \\?\ e \\.\ non vengono ricondotti alla forma normale:
                // il confronto con la radice fallisce e l'elemento viene scartato (fail-safe).
                Assert.False(PathSafety.IsInsideRoot(fx.Root, "\\\\?\\" + real),
                    "Un percorso con prefisso \\\\?\\ non deve essere accettato dentro la radice.");
                Assert.False(PathSafety.IsInsideRoot(fx.Root, "\\\\.\\" + real),
                    "Un percorso con prefisso \\\\.\\ non deve essere accettato dentro la radice.");
            }
        }

        [Test]
        public void PathSafety_AlternateDataStream_IsRejectedWithoutCrash()
        {
            using (Fixture fx = new Fixture())
            {
                string real = fx.CreateFile("ads.tmp", 10);
                string ads = real + "::$DATA";

                string normalized;
                Assert.False(PathSafety.TryNormalize(ads, out normalized),
                    "Un percorso con stream dati alternativo non deve essere normalizzato.");
                Assert.Null(normalized, "Nessun percorso deve essere prodotto per uno stream non valido.");
            }
        }

        #endregion

        #region Utilità

        private static object GetField(object target, string name)
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

        /// <summary>
        /// Esegue un'azione in un thread STA: la creazione di controlli WinForms lo richiede.
        /// I test non mostrano finestre all'utente e non toccano il percorso temporaneo reale.
        /// </summary>
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
