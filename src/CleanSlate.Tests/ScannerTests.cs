using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using CleanSlate.Core;

namespace CleanSlate.Tests
{
    /// <summary>Test del motore di analisi in sola lettura.</summary>
    public sealed class ScannerTests
    {
        private static ScanOptions OptionsFor(string root)
        {
            ScanOptions options = new ScanOptions();
            options.Root = root;
            return options;
        }

        [Test]
        public void Scan_EmptyDirectory_ReturnsZeroItems()
        {
            using (Fixture fx = new Fixture())
            {
                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                Assert.True(result.Success, "La scansione di una cartella vuota deve riuscire.");
                Assert.False(result.Cancelled, "Non deve risultare annullata.");
                Assert.Equal(0, result.Items.Count, "Nessun file atteso.");
                Assert.Equal(0L, result.EstimatedBytes, "Nessun byte atteso.");
                Assert.Equal(0, result.Errors, "Nessun errore atteso.");
                Assert.Equal(0, result.FilesScanned, "Nessun file da analizzare.");
            }
        }

        [Test]
        public void Scan_FilesWithKnownSizes_AreMeasuredExactly()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("a.tmp", 1000);
                fx.CreateFile("b.tmp", 2048);
                fx.CreateFile(Path.Combine("sotto", "c.tmp"), 512);

                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                Assert.True(result.Success, "La scansione deve riuscire.");
                Assert.Equal(3, result.Items.Count, "Tre file attesi.");
                Assert.Equal(3, result.FilesEligible, "Tre file eleggibili attesi.");
                Assert.Equal(1000L + 2048L + 512L, result.EstimatedBytes, "Somma dei byte attesa.");
                Assert.Equal(0, result.Errors, "Nessun errore atteso.");
            }
        }

        [Test]
        public void Scan_FileNewerThanLimit_IsExcluded()
        {
            using (Fixture fx = new Fixture())
            {
                string recent = fx.CreateFile("recente.tmp", 500, TimeSpan.FromHours(1));

                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                Assert.True(result.Success, "La scansione deve riuscire.");
                Assert.Equal(0, result.Items.Count, "Il file recente non deve essere proposto.");
                Assert.Equal(1, result.FilesScanned, "Il file deve essere stato analizzato.");
                Assert.Equal(1, result.FilesSkipped, "Il file recente deve essere contato come scartato.");
                Assert.Equal(0L, result.EstimatedBytes, "Nessuno spazio recuperabile.");
                Assert.True(File.Exists(recent), "Il file recente deve restare intatto.");
            }
        }

        [Test]
        public void Scan_FileOlderThanLimit_IsIncluded()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("vecchio.tmp", 700, TimeSpan.FromHours(48));

                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                Assert.Equal(1, result.Items.Count, "Il file vecchio deve essere proposto.");
                Assert.Equal(700L, result.EstimatedBytes, "Dimensione attesa.");
                Assert.Equal(ScanCategories.UserTempFiles, result.Items[0].Category, "Categoria attesa.");
            }
        }

        [Test]
        public void Scan_ExactBoundary_IsExcluded()
        {
            using (Fixture fx = new Fixture())
            {
                // Appena dentro la finestra di 24 ore: deve essere escluso.
                fx.CreateFile("quasi.tmp", 100, TimeSpan.FromHours(23));
                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));
                Assert.Equal(0, result.Items.Count, "Un file di 23 ore non è ancora recuperabile.");
            }
        }

        [Test]
        public void Scan_NonexistentDirectory_ReturnsErrorWithoutCrash()
        {
            using (Fixture fx = new Fixture())
            {
                string missing = Path.Combine(fx.BaseDir, "assente-" + Guid.NewGuid().ToString("N"));

                ScanResult result = new TempFolderScanner().Scan(OptionsFor(missing));

                Assert.False(result.Success, "Una cartella inesistente deve produrre un errore.");
                Assert.NotNull(result.ErrorMessage, "Deve essere presente un messaggio di errore.");
                Assert.Equal(0, result.Items.Count, "Nessun file può essere trovato.");
                Assert.Null(result.Root, "Non deve essere adottata alcuna radice alternativa.");
            }
        }

        [Test]
        public void Scan_DoesNotModifyFixtureContents()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("uno.tmp", 111);
                fx.CreateFile(Path.Combine("sotto", "due.tmp"), 222);
                fx.CreateFile(Path.Combine("sotto", "tre", "tre.tmp"), 333);

                Dictionary<string, string> before = Fixture.Snapshot(fx.Root);
                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));
                Dictionary<string, string> after = Fixture.Snapshot(fx.Root);

                Assert.True(result.Success, "La scansione deve riuscire.");
                Assert.Equal(3, result.Items.Count, "Tre file attesi.");
                Fixture.AssertSameSnapshot(before, after, "La scansione ha modificato il fixture");
            }
        }

        [Test]
        public void Scan_NestedDirectories_AreIncluded()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("livello1.tmp", 10);
                fx.CreateFile(Path.Combine("a", "livello2.tmp"), 20);
                fx.CreateFile(Path.Combine("a", "b", "c", "livello4.tmp"), 30);

                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                Assert.Equal(3, result.Items.Count, "Tutti i livelli devono essere raggiunti.");
                Assert.Equal(60L, result.EstimatedBytes, "Somma dei byte attesa.");
            }
        }

        [Test]
        public void Scan_DoesNotFollowDirectoryJunction()
        {
            using (Fixture fx = new Fixture())
            {
                string secret = fx.CreateOutsideFile("segreto.tmp", 4096, TimeSpan.FromDays(3));
                string link = Path.Combine(fx.Root, "collegamento");
                Fixture.CreateJunction(link, fx.Outside);

                fx.CreateFile("normale.tmp", 10);

                ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                Assert.True(result.Success, "La scansione deve riuscire.");
                Assert.Equal(1, result.Items.Count, "Solo il file normale deve essere trovato.");
                Assert.Equal("normale.tmp", result.Items[0].Name, "File atteso.");
                Assert.True(result.DirectoriesSkipped >= 1, "La cartella collegamento deve essere saltata.");
                Assert.True(File.Exists(secret), "Il file fuori dalla radice deve restare intatto.");
            }
        }

        [Test]
        public void Scan_CancellationBeforeStart_ReturnsCancelledImmediately()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("x.tmp", 10);
                CancellationTokenSource cts = new CancellationTokenSource();
                cts.Cancel();

                ScanOptions options = OptionsFor(fx.Root);
                options.CancellationToken = cts.Token;
                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.True(result.Success, "L'annullamento preventivo non è un errore.");
                Assert.True(result.Cancelled, "Il risultato deve indicare l'annullamento.");
                Assert.Equal(0, result.Items.Count, "Nessun file deve essere analizzato.");
                cts.Dispose();
            }
        }

        [Test]
        public void Scan_CancellationDuringScan_ReturnsPartialResults()
        {
            using (Fixture fx = new Fixture())
            {
                for (int i = 0; i < 6; i++) fx.CreateFile("f" + i + ".tmp", 100);

                CancellationTokenSource cts = new CancellationTokenSource();
                ScanOptions options = OptionsFor(fx.Root);
                options.CancellationToken = cts.Token;

                bool cancelledOnce = false;
                options.Progress = delegate(ScanProgress progress)
                {
                    if (cancelledOnce) return;
                    cancelledOnce = true;
                    cts.Cancel();
                };

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.True(cancelledOnce, "Il callback di avanzamento deve essere stato invocato.");
                Assert.True(result.Success, "L'annullamento non è un errore.");
                Assert.True(result.Cancelled, "Il risultato deve indicare l'annullamento.");
                Assert.True(result.Items.Count < 6, "La scansione deve fermarsi prima della fine.");
                Assert.True(result.Items.Count >= 1, "I risultati parziali devono essere conservati.");
                cts.Dispose();
            }
        }

        [Test]
        public void Scan_LockedFile_IsStillScannedWithoutCrash()
        {
            using (Fixture fx = new Fixture())
            {
                string locked = fx.CreateFile("bloccato.tmp", 256);
                using (FileStream stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));
                    Assert.True(result.Success, "Un file bloccato non deve far fallire la scansione.");
                    Assert.Equal(1, result.Items.Count, "Il file bloccato deve essere analizzato.");
                    Assert.Equal(256L, result.EstimatedBytes, "La dimensione deve essere letta.");
                }
            }
        }

        [Test]
        public void Scan_AccessDeniedDirectory_RecordsErrorAndContinues()
        {
            using (Fixture fx = new Fixture())
            {
                string lockedDir = Path.Combine(fx.Root, "protetta");
                Directory.CreateDirectory(lockedDir);
                File.WriteAllText(Path.Combine(lockedDir, "nascosto.tmp"), "contenuto");
                fx.MakeOld(Path.Combine(lockedDir, "nascosto.tmp"), TimeSpan.FromDays(3));
                fx.CreateFile("visibile.tmp", 64);

                string currentUser = WindowsIdentity.GetCurrent().User.Value;
                DirectorySecurity modified = Directory.GetAccessControl(lockedDir);
                modified.SetAccessRuleProtection(true, false);
                modified.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(currentUser), FileSystemRights.ReadData, AccessControlType.Deny));
                modified.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(currentUser), FileSystemRights.FullControl, AccessControlType.Allow));
                Directory.SetAccessControl(lockedDir, modified);

                try
                {
                    ScanResult result = new TempFolderScanner().Scan(OptionsFor(fx.Root));

                    Assert.True(result.Success, "Un errore di accesso non deve far fallire l'analisi.");
                    Assert.True(result.Errors >= 1, "L'errore di accesso deve essere contato.");
                    Assert.True(result.ErrorMessages.Count >= 1, "Il messaggio di errore deve essere conservato.");
                    Assert.Contains("Accesso negato", result.ErrorMessages[0], "Il messaggio deve essere comprensibile.");
                    Assert.Equal(1, result.Items.Count, "Il file accessibile deve comunque essere trovato.");
                }
                finally
                {
                    // Ripristino dei permessi ereditari prima della distruzione del fixture.
                    Fixture.ResetAccessControl(lockedDir);
                }
            }
        }

        [Test]
        public void Scan_ProgressCallbackReceivesRealCounters()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("p1.tmp", 100);
                fx.CreateFile("p2.tmp", 200);

                ScanOptions options = OptionsFor(fx.Root);
                int calls = 0;
                int lastFound = -1;
                options.Progress = delegate(ScanProgress progress)
                {
                    calls++;
                    lastFound = progress.FilesFound;
                };

                ScanResult result = new TempFolderScanner().Scan(options);

                Assert.True(calls >= 2, "Il callback deve essere invocato per ogni file.");
                Assert.Equal(2, lastFound, "Il contatore dei file trovati deve essere reale.");
                Assert.Equal(2, result.Items.Count, "Due file attesi.");
            }
        }
    }
}
