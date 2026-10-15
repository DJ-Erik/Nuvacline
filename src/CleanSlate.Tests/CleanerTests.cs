// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CleanSlate.Core;

namespace CleanSlate.Tests
{
    /// <summary>Test del motore di cancellazione prudente.</summary>
    public sealed class CleanerTests
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

        [Test]
        public void Clean_EligibleFiles_AreDeletedAndBytesCounted()
        {
            using (Fixture fx = new Fixture())
            {
                string a = fx.CreateFile("a.tmp", 1000);
                string b = fx.CreateFile("b.tmp", 2048);

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(a, b)));

                Assert.True(result.Success, "La pulizia deve riuscire.");
                Assert.False(result.Cancelled, "Non deve risultare annullata.");
                Assert.Equal(2, result.DeletedCount, "Due file eliminati attesi.");
                Assert.Equal(1000L + 2048L, result.DeletedBytes, "Byte eliminati attesi.");
                Assert.Equal(0, result.ErrorCount, "Nessun errore atteso.");
                Assert.Equal(0, result.SkippedCount, "Nessun elemento saltato atteso.");
                Assert.Equal(0L, result.EstimatedNotRecovered, "Nulla deve restare non recuperato.");
                Assert.False(File.Exists(a), "Il file a deve essere stato eliminato.");
                Assert.False(File.Exists(b), "Il file b deve essere stato eliminato.");
            }
        }

        [Test]
        public void Clean_FileDeletedBetweenScanAndClean_IsSkipped()
        {
            using (Fixture fx = new Fixture())
            {
                string stillThere = fx.CreateFile("rimane.tmp", 500);
                string vanishes = fx.CreateFile("scompare.tmp", 500);

                ScanResult scan = new TempFolderScanner().Scan(new ScanOptions { Root = fx.Root });
                Assert.Equal(2, scan.Items.Count, "La scansione deve trovare due file.");

                File.Delete(vanishes);

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, scan.Items));

                Assert.True(result.Success, "La pulizia deve riuscire.");
                Assert.Equal(1, result.DeletedCount, "Solo il file rimasto deve essere eliminato.");
                Assert.Equal(1, result.SkippedCount, "Il file scomparso deve essere saltato.");
                Assert.Equal(0, result.ErrorCount, "Uno file scomparso non è un errore.");
                Assert.False(File.Exists(stillThere), "Il file rimasto deve essere eliminato.");
                Assert.Equal(CleanOutcome.SkippedNotFound, FindOutcome(result, vanishes), "Esito atteso.");
            }
        }

        [Test]
        public void Clean_PathOutsideRoot_IsRejectedAndFileSurvives()
        {
            using (Fixture fx = new Fixture())
            {
                string outside = fx.CreateOutsideFile("esterno.tmp", 321, TimeSpan.FromDays(2));
                string inside = fx.CreateFile("interno.tmp", 10);

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(outside, inside)));

                Assert.Equal(1, result.DeletedCount, "Solo il file interno deve essere eliminato.");
                Assert.Equal(1, result.SkippedCount, "Il file esterno deve essere saltato.");
                Assert.Equal(CleanOutcome.SkippedOutsideRoot, FindOutcome(result, outside), "Esito atteso.");
                Assert.True(File.Exists(outside), "Il file esterno deve restare intatto.");
                Assert.False(File.Exists(inside), "Il file interno deve essere eliminato.");
            }
        }

        [Test]
        public void Clean_PathWithDotDotEscape_IsRejectedAndFileSurvives()
        {
            using (Fixture fx = new Fixture())
            {
                string outside = fx.CreateOutsideFile("fuga.tmp", 111, TimeSpan.FromDays(2));
                FileItem item = new FileItem();
                item.Name = "fuga.tmp";
                item.FullPath = Path.Combine(fx.Root, "..", "outside", "fuga.tmp");
                item.Length = 111;
                item.Category = ScanCategories.UserTempFiles;

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsWith(item)));

                Assert.Equal(0, result.DeletedCount, "Nessuna cancellazione ammessa fuori radice.");
                Assert.Equal(CleanOutcome.SkippedOutsideRoot, result.Items[0].Outcome, "Esito atteso.");
                Assert.True(File.Exists(outside), "Il file deve restare intatto.");
            }
        }

        [Test]
        public void Clean_PathThroughJunction_IsRejectedAndTargetSurvives()
        {
            using (Fixture fx = new Fixture())
            {
                string target = fx.CreateOutsideFile("protetto.tmp", 64, TimeSpan.FromDays(2));
                string link = Path.Combine(fx.Root, "collegamento");
                Fixture.CreateJunction(link, fx.Outside);

                FileItem item = new FileItem();
                item.Name = "protetto.tmp";
                item.FullPath = Path.Combine(link, "protetto.tmp");
                item.Length = 64;
                item.Category = ScanCategories.UserTempFiles;

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsWith(item)));

                Assert.Equal(0, result.DeletedCount, "Nessuna cancellazione attraverso un collegamento.");
                Assert.Equal(CleanOutcome.SkippedReparse, result.Items[0].Outcome, "Esito atteso.");
                Assert.True(File.Exists(target), "Il file di destinazione deve restare intatto.");
            }
        }

        [Test]
        public void Clean_ReadOnlyFile_FailsGracefullyAndOthersSucceed()
        {
            using (Fixture fx = new Fixture())
            {
                string ok1 = fx.CreateFile("ok1.tmp", 300);
                string readOnly = fx.CreateFile("protetto.tmp", 400);
                string ok2 = fx.CreateFile("ok2.tmp", 500);
                File.SetAttributes(readOnly, FileAttributes.ReadOnly);

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(ok1, readOnly, ok2)));

                Assert.True(result.Success, "Un errore singolo non deve far fallire l'intera operazione.");
                Assert.Equal(2, result.DeletedCount, "I due file normali devono essere eliminati.");
                Assert.Equal(1, result.ErrorCount, "Il file protetto deve produrre un errore.");
                Assert.Equal(300L + 500L, result.DeletedBytes, "Conteggiati solo i byte realmente rimossi.");
                Assert.Equal(400L, result.EstimatedNotRecovered, "Lo spazio non recuperato deve essere dichiarato.");
                Assert.True(File.Exists(readOnly), "Il file protetto deve restare inalterato.");
                Assert.False(File.Exists(ok1), "Il file ok1 deve essere eliminato.");
                Assert.False(File.Exists(ok2), "Il file ok2 deve essere eliminato.");
            }
        }

        [Test]
        public void Clean_LockedFile_FailsGracefullyAndOthersSucceed()
        {
            using (Fixture fx = new Fixture())
            {
                string ok1 = fx.CreateFile("ok1.tmp", 200);
                string locked = fx.CreateFile("bloccato.tmp", 250);
                string ok2 = fx.CreateFile("ok2.tmp", 300);

                using (FileStream stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(ok1, locked, ok2)));

                    Assert.True(result.Success, "Un file bloccato non deve far fallire l'operazione.");
                    Assert.Equal(2, result.DeletedCount, "Gli altri due file devono essere eliminati.");
                    Assert.Equal(1, result.ErrorCount, "Il file bloccato deve produrre un errore.");
                    Assert.Equal(200L + 300L, result.DeletedBytes, "Conteggiati solo i byte realmente rimossi.");
                    Assert.True(File.Exists(locked), "Il file bloccato deve restare inalterato.");
                }
            }
        }

        [Test]
        public void Clean_CancellationIsCooperativeAndLeavesRemainingFiles()
        {
            using (Fixture fx = new Fixture())
            {
                string[] paths = new string[4];
                for (int i = 0; i < paths.Length; i++) paths[i] = fx.CreateFile("f" + i + ".tmp", 100);

                CancellationTokenSource cts = new CancellationTokenSource();
                CleanOptions options = OptionsFor(fx.Root, ItemsFor(paths));
                options.CancellationToken = cts.Token;
                options.Progress = delegate(CleanProgress progress)
                {
                    if (progress.Deleted >= 1) cts.Cancel();
                };

                CleanResult result = new TempFileCleaner().Clean(options);

                Assert.True(result.Cancelled, "Il risultato deve indicare l'annullamento.");
                Assert.Equal(1, result.DeletedCount, "Deve essere completato solo il primo elemento.");
                Assert.Equal(3, result.SkippedCount, "Gli elementi restanti devono essere saltati.");

                int survivors = 0;
                for (int i = 0; i < paths.Length; i++) if (File.Exists(paths[i])) survivors++;
                Assert.Equal(3, survivors, "I file non ancora processati devono restare intatti.");
                Assert.True(File.Exists(paths[1]) || !File.Exists(paths[0]), "Conteggio dei superstiti incoerente.");
                Assert.False(File.Exists(paths[0]), "Il file già eliminato deve restare eliminato.");
                for (int i = 1; i < paths.Length; i++)
                    Assert.Equal(CleanOutcome.SkippedCancelled, FindOutcome(result, paths[i]), "Esito di annullamento atteso.");
                cts.Dispose();
            }
        }

        [Test]
        public void Clean_FileNewerThanLimit_IsSkipped()
        {
            using (Fixture fx = new Fixture())
            {
                string recent = fx.CreateFile("recente.tmp", 999, TimeSpan.FromHours(2));

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(recent)));

                Assert.Equal(0, result.DeletedCount, "Il file recente non deve essere eliminato.");
                Assert.Equal(1, result.SkippedCount, "Il file recente deve essere saltato.");
                Assert.Equal(CleanOutcome.SkippedTooRecent, result.Items[0].Outcome, "Esito atteso.");
                Assert.Equal(0L, result.DeletedBytes, "Nessun byte eliminato.");
                Assert.True(File.Exists(recent), "Il file recente deve restare intatto.");
            }
        }

        [Test]
        public void Clean_RootPath_IsNeverDeleted()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("sotto.tmp", 10);
                FileItem item = new FileItem();
                item.Name = Path.GetFileName(fx.Root);
                item.FullPath = fx.Root;
                item.Category = ScanCategories.UserTempFiles;

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsWith(item)));

                Assert.Equal(0, result.DeletedCount, "La radice non può essere eliminata.");
                Assert.Equal(CleanOutcome.SkippedRoot, result.Items[0].Outcome, "Esito atteso.");
                Assert.True(Directory.Exists(fx.Root), "La cartella radice deve esistere ancora.");
            }
        }

        [Test]
        public void Clean_DirectoryPath_IsSkippedAndContentsSurvive()
        {
            using (Fixture fx = new Fixture())
            {
                string directory = Path.Combine(fx.Root, "cartella");
                Directory.CreateDirectory(directory);
                string inner = Path.Combine(directory, "interno.tmp");
                File.WriteAllBytes(inner, new byte[50]);
                File.SetLastWriteTimeUtc(inner, DateTime.UtcNow - TimeSpan.FromDays(3));

                FileItem item = new FileItem();
                item.Name = "cartella";
                item.FullPath = directory;
                item.Category = ScanCategories.UserTempFiles;

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsWith(item)));

                Assert.Equal(0, result.DeletedCount, "Nessuna directory deve essere eliminata.");
                Assert.NotEqual(CleanOutcome.Deleted, result.Items[0].Outcome, "La directory non può risultare eliminata.");
                Assert.True(Directory.Exists(directory), "La directory deve esistere ancora.");
                Assert.True(File.Exists(inner), "Il contenuto della directory deve sopravvivere.");
            }
        }

        [Test]
        public void Clean_InvalidPath_IsSkippedWithoutCrash()
        {
            using (Fixture fx = new Fixture())
            {
                FileItem item = new FileItem();
                item.Name = "invalido";
                item.FullPath = "\0/non-valido";
                item.Category = ScanCategories.UserTempFiles;

                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsWith(item)));

                Assert.True(result.Success, "Un percorso invalido non deve generare un'eccezione non gestita.");
                Assert.Equal(0, result.DeletedCount, "Nessuna cancellazione.");
                Assert.Equal(1, result.SkippedCount, "L'elemento deve essere saltato.");
            }
        }

        [Test]
        public void Clean_EndToEnd_ScansThenDeletesOnlyScannedItems()
        {
            using (Fixture fx = new Fixture())
            {
                string old1 = fx.CreateFile("vecchio1.tmp", 100);
                string old2 = fx.CreateFile("vecchio2.tmp", 200);
                string recent = fx.CreateFile("recente.tmp", 400, TimeSpan.FromHours(1));
                string outside = fx.CreateOutsideFile("fuori.tmp", 800, TimeSpan.FromDays(5));

                ScanResult scan = new TempFolderScanner().Scan(new ScanOptions { Root = fx.Root });
                Assert.Equal(2, scan.Items.Count, "Solo i due file vecchi devono essere proposti.");

                CleanResult clean = new TempFileCleaner().Clean(OptionsFor(fx.Root, scan.Items));

                Assert.Equal(2, clean.DeletedCount, "I due file scansionati devono essere eliminati.");
                Assert.Equal(300L, clean.DeletedBytes, "Byte eliminati attesi.");
                Assert.False(File.Exists(old1), "vecchio1 deve essere eliminato.");
                Assert.False(File.Exists(old2), "vecchio2 deve essere eliminato.");
                Assert.True(File.Exists(recent), "Il file recente non deve essere toccato.");
                Assert.True(File.Exists(outside), "Il file esterno non deve essere toccato.");
            }
        }

        [Test]
        public void Clean_ReportsOnlyBytesActuallyRemoved()
        {
            using (Fixture fx = new Fixture())
            {
                string deleted = fx.CreateFile("via.tmp", 1234);
                string survivor = fx.CreateFile("resta.tmp", 4321);

                // Lo stesso file compare due volte: la seconda occorrenza non deve
                // essere conteggiata come spazio liberato due volte.
                CleanResult result = new TempFileCleaner().Clean(OptionsFor(fx.Root, ItemsFor(deleted, deleted)));

                Assert.Equal(1, result.DeletedCount, "Il file deve essere contato una volta sola.");
                Assert.Equal(1, result.SkippedCount, "La seconda occorrenza deve essere saltata.");
                Assert.Equal(1234L, result.DeletedBytes, "Devono essere dichiarati solo i byte realmente rimossi.");
                Assert.True(File.Exists(survivor), "Il file non selezionato deve sopravvivere.");
            }
        }

        private static List<FileItem> ItemsWith(FileItem item)
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
            return CleanOutcome.SkippedInvalidPath;
        }
    }
}
