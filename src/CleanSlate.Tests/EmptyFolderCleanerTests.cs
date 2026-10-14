using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using CleanSlate.Core;
using CleanSlate.UI;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Test della fase 0.4.0: rimozione prudente delle cartelle vuote.
    ///
    /// Ogni test di cancellazione gira su directory temporanee isolate create dal
    /// fixture (%LOCALAPPDATA%\CleanSlateTests\&lt;guid&gt;), mai sul vero %TEMP% e mai
    /// sul profilo utente. Copertura: candidatura in sola lettura, post-order,
    /// protezione della radice, radice esterna, traversal, invalidi, reparse point,
    /// race (contenuto/junction), ACL negate, annullamento, integrazione
    /// scansione → pulizia → cartelle → report, impostazione (default disattivato,
    /// persistenza indipendente) e wiring GUI (menu, conferma dedicata, riepilogo,
    /// comportamento invariato quando disabilitata).
    /// </summary>
    public sealed class EmptyFolderCleanerTests
    {
        #region Sondaggio (sola lettura)

        [Test]
        public void Survey_EmptyDirectory_IsProposedAndNonEmptyIsNot()
        {
            using (Fixture fx = new Fixture())
            {
                string vuota = Path.Combine(fx.Root, "vuota");
                Directory.CreateDirectory(vuota);
                string piena = Path.Combine(fx.Root, "piena");
                Directory.CreateDirectory(piena);
                File.WriteAllText(Path.Combine(piena, "contenuto.tmp"), "x");

                EmptyFolderSurvey survey = new EmptyFolderCleaner().Survey(fx.Root, CancellationToken.None);

                Assert.True(survey.Success, "Il sondaggio deve riuscire.");
                Assert.Equal(1, survey.Candidates.Count,
                    "Solo la cartella senza file può essere proposta.");
                Assert.Equal(Path.GetFullPath(vuota), survey.Candidates[0],
                    "La candidata deve essere la cartella vuota.");
                Assert.True(Directory.Exists(piena), "La cartella con un file deve restare intatta.");
            }
        }

        [Test]
        public void Survey_NestedEmptyTree_ProposesDaughtersBeforeMothers()
        {
            using (Fixture fx = new Fixture())
            {
                string a = Path.Combine(fx.Root, "a");
                string b = Path.Combine(a, "b");
                string c = Path.Combine(b, "c");
                Directory.CreateDirectory(c);

                EmptyFolderSurvey survey = new EmptyFolderCleaner().Survey(fx.Root, CancellationToken.None);

                Assert.True(survey.Success, "Il sondaggio deve riuscire.");
                Assert.Equal(3, survey.Candidates.Count,
                    "L'intero albero vuoto deve essere proposto.");
                Assert.Equal(Path.GetFullPath(c), survey.Candidates[0], "La figlia più profonda viene prima.");
                Assert.Equal(Path.GetFullPath(b), survey.Candidates[1], "Il livello intermedio viene dopo.");
                Assert.Equal(Path.GetFullPath(a), survey.Candidates[2], "La madre viene per ultima (post-order).");
            }
        }

        [Test]
        public void Survey_Junction_IsNotFollowedNorProposed_AndParentKeepsItsLink()
        {
            using (Fixture fx = new Fixture())
            {
                string marker = fx.CreateOutsideFile("segreto.tmp", 64, TimeSpan.FromDays(3));
                byte[] before = File.ReadAllBytes(marker);

                string padre = Path.Combine(fx.Root, "padre");
                string figlia = Path.Combine(padre, "figlia");
                Directory.CreateDirectory(figlia);
                string link = Path.Combine(padre, "link");
                Fixture.CreateJunction(link, fx.Outside);

                EmptyFolderSurvey survey = new EmptyFolderCleaner().Survey(fx.Root, CancellationToken.None);

                Assert.True(survey.Success, "Il sondaggio deve riuscire.");
                Assert.Equal(1, survey.Candidates.Count,
                    "Solo la figlia vuota è proponibile: la madre contiene una junction che sopravvivrebbe.");
                Assert.Equal(Path.GetFullPath(figlia), survey.Candidates[0], "La candidata deve essere la figlia.");
                Assert.False(survey.Candidates.Contains(link),
                    "La junction non deve mai essere proposta.");

                bool reparseRecorded = false;
                for (int i = 0; i < survey.Items.Count; i++)
                {
                    EmptyDirectoryItemResult item = survey.Items[i];
                    if (item.Outcome == EmptyDirectoryOutcome.SkippedReparse
                        && string.Equals(item.FullPath, link, StringComparison.OrdinalIgnoreCase))
                        reparseRecorded = true;
                }
                Assert.True(reparseRecorded, "La junction deve essere registrata come SkippedReparse.");

                // Anche una rimozione "forzata" con la junction come candidata la conserva.
                EmptyCleanResult forced = new EmptyFolderCleaner().Remove(
                    fx.Root, new List<string>(new string[] { link }), CancellationToken.None);
                Assert.Equal(1, forced.SkippedCount, "La junction deve essere saltata.");
                Assert.Equal(EmptyDirectoryOutcome.SkippedReparse, forced.Items[0].Outcome,
                    "L'esito deve dichiarare il reparse point.");
                Assert.True(Directory.Exists(link), "La junction deve restare sul disco.");
                byte[] after = File.ReadAllBytes(marker);
                Assert.Equal(before.Length, after.Length, "Il contenuto del link target non deve cambiare.");
                for (int i = 0; i < before.Length; i++)
                    Assert.Equal(before[i], after[i], "Il contenuto del link target deve restare identico.");
            }
        }

        [Test]
        public void Survey_NonexistentRoot_FailsWithClearMessage()
        {
            using (Fixture fx = new Fixture())
            {
                string missing = Path.Combine(fx.BaseDir, "mai-creata");
                EmptyFolderSurvey survey = new EmptyFolderCleaner().Survey(missing, CancellationToken.None);

                Assert.False(survey.Success, "Una radice inesistente deve far fallire il sondaggio.");
                Assert.NotNull(survey.ErrorMessage, "Il motivo deve essere dichiarato.");
                Assert.Contains("non esiste", survey.ErrorMessage,
                    "Il messaggio deve spiegare che la cartella non esiste.");
                Assert.Equal(0, survey.Candidates.Count, "Nessuna candidata con radice inesistente.");
            }
        }

        #endregion

        #region Rimozione prudente

        [Test]
        public void Remove_EmptyTree_PostOrder_DeletesDaughtersBeforeMothersAndKeepsFiles()
        {
            using (Fixture fx = new Fixture())
            {
                string resta = fx.CreateFile("resta.tmp", 128);
                string a = Path.Combine(fx.Root, "a");
                string b = Path.Combine(a, "b");
                string c = Path.Combine(b, "c");
                Directory.CreateDirectory(c);

                EmptyFolderCleaner cleaner = new EmptyFolderCleaner();
                EmptyFolderSurvey survey = cleaner.Survey(fx.Root, CancellationToken.None);
                EmptyCleanResult result = cleaner.Remove(fx.Root, survey.Candidates, CancellationToken.None);

                Assert.True(result.Success, "La rimozione deve riuscire.");
                Assert.Equal(3, result.RemovedCount, "Le tre cartelle vuote devono essere rimosse.");
                Assert.False(Directory.Exists(a), "La madre deve essere rimossa dopo le figlie.");
                Assert.False(Directory.Exists(b), "Il livello intermedio deve essere rimosso.");
                Assert.False(Directory.Exists(c), "La figlia più profonda deve essere rimossa.");
                Assert.True(File.Exists(resta), "I file alla radice non devono essere toccati.");
                Assert.True(Directory.Exists(fx.Root), "La radice deve restare.");

                int ic = IndexOfOutcome(result, c);
                int ib = IndexOfOutcome(result, b);
                int ia = IndexOfOutcome(result, a);
                Assert.True(ic < ib && ib < ia,
                    "L'ordine di rimozione deve essere post-order (figlia prima della madre).");
            }
        }

        [Test]
        public void Remove_RootPath_IsNeverDeleted()
        {
            using (Fixture fx = new Fixture())
            {
                string file = fx.CreateFile("sotto-radice.tmp", 64);
                List<string> candidates = new List<string>();
                candidates.Add(fx.Root);
                candidates.Add(fx.Root + Path.DirectorySeparatorChar);

                EmptyCleanResult result = new EmptyFolderCleaner().Remove(
                    fx.Root, candidates, CancellationToken.None);

                Assert.True(result.Success, "L'operazione non deve fallire.");
                Assert.Equal(0, result.RemovedCount, "La radice non deve mai essere rimossa.");
                Assert.Equal(2, result.SkippedCount, "Entrambe le varianti della radice devono essere saltate.");
                Assert.Equal(EmptyDirectoryOutcome.SkippedRoot, result.Items[0].Outcome,
                    "L'esito deve dichiarare SkippedRoot.");
                Assert.True(Directory.Exists(fx.Root), "La radice deve sopravvivere.");
                Assert.True(File.Exists(file), "I file sotto la radice devono sopravvivere.");
            }
        }

        [Test]
        public void Remove_OutsideAndTraversalPaths_AreRejectedAndSurvivorsIntact()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("dentro.tmp", 50);
                string fuoriSub = Path.Combine(fx.Outside, "sotto");
                Directory.CreateDirectory(fuoriSub);
                File.WriteAllText(Path.Combine(fuoriSub, "vittima.tmp"), "vittima");
                Dictionary<string, string> before = Fixture.Snapshot(fx.Outside);

                List<string> candidates = new List<string>();
                candidates.Add(fx.Outside);
                candidates.Add(fuoriSub);
                candidates.Add(Path.Combine(fx.Root, "..", "outside"));
                candidates.Add(Path.Combine(fx.Root, "..", "outside", "sotto"));

                EmptyCleanResult result = new EmptyFolderCleaner().Remove(
                    fx.Root, candidates, CancellationToken.None);

                Assert.True(result.Success, "L'operazione non deve fallire.");
                Assert.Equal(0, result.RemovedCount, "Nessuna cartella esterna può essere rimossa.");
                Assert.Equal(4, result.SkippedCount, "Tutti i percorsi esterni devono essere saltati.");
                for (int i = 0; i < result.Items.Count; i++)
                    Assert.Equal(EmptyDirectoryOutcome.SkippedOutsideRoot, result.Items[i].Outcome,
                        "Ogni esito deve dichiarare SkippedOutsideRoot.");
                Dictionary<string, string> after = Fixture.Snapshot(fx.Outside);
                Fixture.AssertSameSnapshot(before, after,
                    "La cartella esterna deve restare intatta.");
            }
        }

        [Test]
        public void Remove_InvalidPaths_AreSkippedWithoutCrash()
        {
            using (Fixture fx = new Fixture())
            {
                string file = fx.CreateFile("normale.tmp", 32);

                List<string> candidates = new List<string>();
                candidates.Add(null);
                candidates.Add(string.Empty);
                candidates.Add("\0non-valido");
                candidates.Add(file);

                EmptyCleanResult result = new EmptyFolderCleaner().Remove(
                    fx.Root, candidates, CancellationToken.None);

                Assert.True(result.Success, "Percorsi invalidi non devono far fallire l'operazione.");
                Assert.Equal(0, result.RemovedCount, "Nessuna rimozione da percorsi invalidi.");
                Assert.Equal(4, result.SkippedCount, "Tutti i percorsi invalidi devono essere saltati.");
                for (int i = 0; i < result.Items.Count; i++)
                    Assert.Equal(EmptyDirectoryOutcome.SkippedInvalidPath, result.Items[i].Outcome,
                        "Ogni esito deve dichiarare SkippedInvalidPath.");
                Assert.True(File.Exists(file), "Un percorso che punta a un file non deve toccare il file.");
            }
        }

        [Test]
        public void Remove_DirectoryGainingContentBetweenSurveyAndRemove_IsKeptAndReported()
        {
            using (Fixture fx = new Fixture())
            {
                string a = Path.Combine(fx.Root, "a");
                string b = Path.Combine(a, "b");
                Directory.CreateDirectory(b);

                EmptyFolderCleaner cleaner = new EmptyFolderCleaner();
                EmptyFolderSurvey survey = cleaner.Survey(fx.Root, CancellationToken.None);
                Assert.Equal(2, survey.Candidates.Count, "Al sondaggio l'albero è tutto vuoto.");

                // Race: un file compare nella figlia tra sondaggio e rimozione.
                string intruso = Path.Combine(b, "intruso.tmp");
                File.WriteAllText(intruso, "presenza recente");

                EmptyCleanResult result = cleaner.Remove(fx.Root, survey.Candidates, CancellationToken.None);

                Assert.True(result.Success, "L'operazione deve riuscire comunque.");
                Assert.Equal(0, result.RemovedCount, "Nessuna cartella deve essere rimossa.");
                Assert.Equal(EmptyDirectoryOutcome.SkippedNotEmpty, FindOutcome(result, b).Outcome,
                    "La figlia deve essere conservata perché non è più vuota.");
                Assert.Equal(EmptyDirectoryOutcome.SkippedNotEmpty, FindOutcome(result, a).Outcome,
                    "Anche la madre deve essere conservata.");
                Assert.True(File.Exists(intruso), "Il file comparso deve restare intatto.");
                Assert.True(Directory.Exists(b) && Directory.Exists(a),
                    "Entrambe le cartelle devono restare sul disco.");
            }
        }

        [Test]
        public void Remove_DirectoryReplacedByJunction_IsSkippedAndTargetIntact()
        {
            using (Fixture fx = new Fixture())
            {
                string dir = Path.Combine(fx.Root, "sostituita");
                Directory.CreateDirectory(dir);

                EmptyFolderCleaner cleaner = new EmptyFolderCleaner();
                EmptyFolderSurvey survey = cleaner.Survey(fx.Root, CancellationToken.None);
                Assert.Equal(1, survey.Candidates.Count, "La cartella vuota deve essere candidata.");

                // Race: la cartella viene sostituita da una junction (creata da altri).
                Directory.Delete(dir);
                string target = Path.Combine(fx.BaseDir, "target-junction");
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "contenuto.tmp"), "dato importante");
                Fixture.CreateJunction(dir, target);

                EmptyCleanResult result = cleaner.Remove(fx.Root, survey.Candidates, CancellationToken.None);

                Assert.True(result.Success, "L'operazione deve riuscire comunque.");
                Assert.Equal(0, result.RemovedCount, "La junction non deve essere rimossa.");
                Assert.Equal(EmptyDirectoryOutcome.SkippedReparse, result.Items[0].Outcome,
                    "L'esito deve dichiarare SkippedReparse.");
                Assert.True(Directory.Exists(dir), "La junction deve restare sul disco.");
                Assert.True(File.Exists(Path.Combine(target, "contenuto.tmp")),
                    "Il contenuto del target deve restare intatto.");
            }
        }

        [Test]
        public void Remove_AccessDeniedDirectory_FailsAndOtherDirectoriesProceed()
        {
            using (Fixture fx = new Fixture())
            {
                string negata = Path.Combine(fx.Root, "negata");
                Directory.CreateDirectory(negata);
                string ok = Path.Combine(fx.Root, "ok");
                Directory.CreateDirectory(ok);

                string currentUser = WindowsIdentity.GetCurrent().User.Value;
                DirectorySecurity modified = Directory.GetAccessControl(negata);
                modified.SetAccessRuleProtection(true, false);
                modified.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(currentUser), FileSystemRights.ReadData, AccessControlType.Deny));
                modified.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(currentUser), FileSystemRights.FullControl, AccessControlType.Allow));
                Directory.SetAccessControl(negata, modified);

                try
                {
                    List<string> candidates = new List<string>();
                    candidates.Add(negata);
                    candidates.Add(ok);

                    EmptyCleanResult result = new EmptyFolderCleaner().Remove(
                        fx.Root, candidates, CancellationToken.None);

                    Assert.True(result.Success, "Un errore di accesso non deve far fallire l'operazione.");
                    Assert.Equal(1, result.ErrorCount, "L'errore di accesso deve essere contato.");
                    Assert.Equal(1, result.RemovedCount, "La cartella accessibile deve essere rimossa.");
                    Assert.Equal(EmptyDirectoryOutcome.Failed, FindOutcome(result, negata).Outcome,
                        "La cartella protetta deve essere registrata come Failed.");
                    Assert.Contains("Accesso negato", FindOutcome(result, negata).Message,
                        "Il messaggio deve essere comprensibile.");
                    Assert.True(Directory.Exists(negata), "La cartella protetta deve restare.");
                    Assert.False(Directory.Exists(ok), "La cartella accessibile deve essere rimossa.");
                }
                finally
                {
                    Fixture.ResetAccessControl(negata);
                }
            }
        }

        [Test]
        public void Remove_Cancellation_MarksRemainingAsPreserved()
        {
            using (Fixture fx = new Fixture())
            {
                List<string> candidates = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    string dir = Path.Combine(fx.Root, "d" + i);
                    Directory.CreateDirectory(dir);
                    candidates.Add(dir);
                }

                CancellationTokenSource cts = new CancellationTokenSource();
                cts.Cancel();

                EmptyCleanResult result = new EmptyFolderCleaner().Remove(
                    fx.Root, candidates, cts.Token);

                Assert.True(result.Success, "L'operazione deve terminare in modo controllato.");
                Assert.True(result.Cancelled, "L'annullamento deve essere dichiarato.");
                Assert.Equal(0, result.RemovedCount, "Nessuna cartella può essere rimossa dopo l'annullamento.");
                Assert.Equal(3, result.SkippedCount, "Tutti gli elementi devono essere saltati.");
                for (int i = 0; i < result.Items.Count; i++)
                    Assert.Equal(EmptyDirectoryOutcome.SkippedCancelled, result.Items[i].Outcome,
                        "Ogni esito deve dichiarare SkippedCancelled.");
                for (int i = 0; i < candidates.Count; i++)
                    Assert.True(Directory.Exists(candidates[i]),
                        "Le cartelle devono restare intatte dopo l'annullamento: " + candidates[i]);
            }
        }

        [Test]
        public void Remove_DeletedCandidate_ReportsSkippedNotFound()
        {
            using (Fixture fx = new Fixture())
            {
                List<string> candidates = new List<string>();
                candidates.Add(Path.Combine(fx.Root, "mai-esistita"));

                EmptyCleanResult result = new EmptyFolderCleaner().Remove(
                    fx.Root, candidates, CancellationToken.None);

                Assert.True(result.Success, "L'operazione deve riuscire.");
                Assert.Equal(0, result.RemovedCount, "Niente da rimuovere.");
                Assert.Equal(1, result.SkippedCount, "Il candidato mancante deve essere saltato.");
                Assert.Equal(EmptyDirectoryOutcome.SkippedNotFound, result.Items[0].Outcome,
                    "L'esito deve dichiarare SkippedNotFound.");
            }
        }

        #endregion

        #region Integrazione scansione → pulizia → cartelle → report

        [Test]
        public void Integration_ScanCleanFoldersAndReport_StayCoherent()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("vecchio1.tmp", 100);
                fx.CreateFile("vecchio2.tmp", 200);
                Directory.CreateDirectory(Path.Combine(fx.Root, "cart1"));
                Directory.CreateDirectory(Path.Combine(fx.Root, "cart2"));

                ScanOptions scanOptions = new ScanOptions();
                scanOptions.Root = fx.Root;
                scanOptions.MinAge = TimeSpan.FromHours(24);
                scanOptions.CancellationToken = CancellationToken.None;
                ScanResult scan = new TempFolderScanner().Scan(scanOptions);
                Assert.Equal(2, scan.Items.Count, "La scansione deve trovare i due file vecchi.");

                CleanOptions cleanOptions = new CleanOptions();
                cleanOptions.Root = fx.Root;
                cleanOptions.Items = scan.Items;
                cleanOptions.MinAge = TimeSpan.FromHours(24);
                cleanOptions.CancellationToken = CancellationToken.None;
                CleanResult clean = new TempFileCleaner().Clean(cleanOptions);
                Assert.Equal(2, clean.DeletedCount, "I due file devono essere eliminati.");

                EmptyFolderCleaner cleaner = new EmptyFolderCleaner();
                EmptyFolderSurvey survey = cleaner.Survey(fx.Root, CancellationToken.None);
                Assert.Equal(2, survey.Candidates.Count,
                    "Le due cartelle diventate vuote devono essere proposte.");
                EmptyCleanResult empty = cleaner.Remove(fx.Root, survey.Candidates, CancellationToken.None);
                Assert.Equal(2, empty.RemovedCount, "Le due cartelle vuote devono essere rimosse.");
                Assert.False(Directory.Exists(Path.Combine(fx.Root, "cart1")), "cart1 deve essere rimossa.");
                Assert.False(Directory.Exists(Path.Combine(fx.Root, "cart2")), "cart2 deve essere rimossa.");
                Assert.True(Directory.Exists(fx.Root), "La radice deve restare.");

                ReportData data = ReportBuilder.Build(scan, clean, empty, fx.Root);
                Assert.True(data.EmptyCleanAvailable, "La fase cartelle deve comparire nel report.");
                Assert.Equal(2, data.EmptyDirectoriesRemoved, "Il conteggio deve essere reale.");
                Assert.Equal(0, data.EmptyDirectoriesErrors, "Nessun errore atteso.");

                string txt = ReportExporter.BuildText(data);
                Assert.Contains("Cartelle vuote rimosse", txt, "La riga TXT deve esserci.");
                Assert.DoesNotContain(fx.Root, txt, "Il report non deve contenere il percorso radice reale.");

                string json = ReportExporter.BuildJson(data);
                Assert.Contains("\"cartelleVuoteRimosse\": 2", json,
                    "La chiave JSON deve essere presente con il conteggio.");

                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                {
                    Assert.DoesNotContain(profile, txt, "Il report TXT non deve contenere il profilo utente.");
                    Assert.DoesNotContain(profile, json, "Il report JSON non deve contenere il profilo utente.");
                }

                // Comportamento invariato quando la fase non viene eseguita (opzione spenta):
                // l'uscita con la vecchia firma è identica all'uscita con empty = null.
                ReportData off = ReportBuilder.Build(scan, clean, fx.Root);
                ReportData offAgain = ReportBuilder.Build(scan, clean, null, fx.Root);
                Assert.False(off.EmptyCleanAvailable, "Senza fase non deve esserci sezione cartelle.");
                DateTime stamp = new DateTime(2026, 1, 1, 0, 0, 0);
                off.GeneratedLocal = stamp;
                offAgain.GeneratedLocal = stamp;
                Assert.Equal(ReportExporter.BuildText(off), ReportExporter.BuildText(offAgain),
                    "Il report senza fase cartelle deve essere identico alla 0.3.6.");
                Assert.Equal(ReportExporter.BuildJson(off), ReportExporter.BuildJson(offAgain),
                    "Il JSON senza fase cartelle deve essere identico alla 0.3.6.");
                Assert.DoesNotContain("cartelleVuote", ReportExporter.BuildJson(off),
                    "Nessuna chiave cartelle quando la fase non gira.");
            }
        }

        #endregion

        #region Impostazione RemoveEmptyFolders

        [Test]
        public void RemoveEmptyFolders_MissingOrInvalidValue_FallsBackToDisabled()
        {
            string path = TempFile("empty-missing");
            try
            {
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Un file assente deve produrre la rimozione disattivata.");
                Assert.False(ScanSettings.DefaultRemoveEmptyFolders,
                    "Il default del prodotto deve essere disattivato.");

                File.WriteAllText(path, string.Empty);
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Un file vuoto deve produrre la rimozione disattivata.");

                File.WriteAllText(path, "# commento\r\nMinAgeHours=24");
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Un file senza la chiave deve produrre la rimozione disattivata.");

                File.WriteAllText(path, "RemoveEmptyFolders=forse");
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Un valore non valido deve ricadere sulla disattivazione.");

                File.WriteAllText(path, "RemoveEmptyFolders=0");
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Il valore 0 deve mantenere la rimozione disattiva.");

                File.WriteAllText(path, "RemoveEmptyFolders=FALSE");
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Il valore false deve mantenere la rimozione disattiva (maiuscole ammesse).");

                File.WriteAllText(path, "RemoveEmptyFolders=1");
                Assert.True(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Il valore 1 deve attivare la rimozione.");

                File.WriteAllText(path, "RemoveEmptyFolders=true");
                Assert.True(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "Il valore true deve attivare la rimozione.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void RemoveEmptyFolders_RoundTrip_AndPreservesOtherKeys()
        {
            string path = TempFile("empty-roundtrip");
            try
            {
                Assert.True(ScanSettings.SaveTo(path, 6), "Il salvataggio della soglia deve riuscire.");
                Assert.True(ScanSettings.SaveKeepReportsTo(path, false),
                    "Il salvataggio della conservazione deve riuscire.");
                Assert.True(ScanSettings.SaveRemoveEmptyFoldersTo(path, true),
                    "Il salvataggio della rimozione cartelle deve riuscire.");

                Assert.Equal(6, ScanSettings.LoadFrom(path).Hours, "La soglia deve restare 6 ore.");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "La conservazione deve restare disattiva.");
                Assert.True(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "La rimozione cartelle deve risultare attiva.");

                string content = File.ReadAllText(path);
                Assert.Contains("MinAgeHours=6", content, "La chiave soglia deve essere presente.");
                Assert.Contains("KeepReports=0", content, "La chiave conservazione deve essere presente.");
                Assert.Contains("RemoveEmptyFolders=1", content, "La chiave cartelle deve essere presente.");

                Assert.True(ScanSettings.SaveRemoveEmptyFoldersTo(path, false),
                    "Il secondo salvataggio della rimozione deve riuscire.");
                Assert.False(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "La preferenza deve tornare disattiva.");
                Assert.Equal(6, ScanSettings.LoadFrom(path).Hours,
                    "Salvare la rimozione non deve toccare la soglia.");
                Assert.False(ScanSettings.LoadKeepReportsFrom(path),
                    "Salvare la rimozione non deve toccare la conservazione.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        #endregion

        #region GUI: conferma dedicata, riepilogo, menu, flusso completo

        [Test]
        public void EmptyFolderQuestion_StatesCountScopeAndSafety()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string question = (string)Invoke(form, "BuildEmptyFolderQuestion", new object[] { 7 });
                    Assert.Contains("7", question, "La domanda deve riportare il conteggio reale.");
                    Assert.Contains("cartelle", question, "La domanda deve parlare di cartelle.");
                    Assert.Contains("radice", question, "La domanda deve chiarire che la radice è protetta.");
                    Assert.Contains("Vuole continuare?", question, "La domanda deve chiedere conferma esplicita.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void TogglingRemoveEmptyFolders_PersistsAndRevertsOnFailure()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    string settings = TempFile("empty-toggle");
                    try
                    {
                        SetField(form, "_loadRemoveEmptyFolders",
                            new Func<bool>(delegate { return ScanSettings.LoadRemoveEmptyFoldersFrom(settings); }));
                        SetField(form, "_saveRemoveEmptyFolders",
                            new Func<bool, bool>(delegate(bool value) { return ScanSettings.SaveRemoveEmptyFoldersTo(settings, value); }));
                        Invoke(form, "LoadRemoveEmptyFoldersSetting", new object[] { });

                        ToolStripMenuItem item = GetField<ToolStripMenuItem>(form, "_miRemoveEmptyFolders");
                        Assert.NotNull(item, "La voce di menu deve esistere.");
                        Assert.False(item.Checked, "Di default la voce non deve essere spuntata.");

                        item.Checked = true;
                        Invoke(form, "OnRemoveEmptyFoldersClick", new object[] { null, EventArgs.Empty });

                        Assert.True((bool)GetFieldValue(form, "_removeEmptyFolders"),
                            "Lo stato interno deve seguire la spunta.");
                        Assert.True(ScanSettings.LoadRemoveEmptyFoldersFrom(settings),
                            "La preferenza attiva deve essere persistita su disco.");

                        // Salvataggio non riuscito: spunta riportata al valore effettivo + notifica.
                        string notified = null;
                        SetField(form, "_saveRemoveEmptyFolders",
                            new Func<bool, bool>(delegate(bool value) { return false; }));
                        SetField(form, "_notifyUser",
                            new Action<string>(delegate(string message) { notified = message; }));

                        item.Checked = false;
                        Invoke(form, "OnRemoveEmptyFoldersClick", new object[] { null, EventArgs.Empty });

                        Assert.True((bool)GetFieldValue(form, "_removeEmptyFolders"),
                            "Con salvataggio fallito lo stato deve restare attivo.");
                        Assert.True(item.Checked, "La spunta deve essere riportata al valore effettivo.");
                        Assert.NotNull(notified, "L'utente deve essere informato del salvataggio fallito.");
                        Assert.Contains("cartelle vuote", notified,
                            "Il messaggio deve parlare della rimozione delle cartelle vuote.");
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
        public void SummaryText_MentionsFolderSectionOnlyWhenThePhaseRan()
        {
            MethodInfo method = typeof(MainForm).GetMethod("BuildCleanSummaryText",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Il costruttore del riepilogo deve esistere.");

            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 2;
            clean.DeletedBytes = 100;

            string without = (string)method.Invoke(null, new object[] { clean, null });
            Assert.Contains("File eliminati: 2", without, "Il riepilogo dei file deve restare.");
            Assert.DoesNotContain("Cartelle vuote", without,
                "Senza fase eseguita non deve esserci alcuna sezione cartelle.");

            EmptyCleanResult empty = new EmptyCleanResult();
            empty.Success = true;
            empty.RemovedCount = 1;
            empty.SkippedCount = 2;
            empty.ErrorCount = 0;

            string with = (string)method.Invoke(null, new object[] { clean, empty });
            Assert.Contains("Cartelle vuote (fase separata e facoltativa):", with,
                "La sezione cartelle deve essere distinta e dichiarata come separata.");
            Assert.Contains("Rimosse: 1", with, "Il conteggio delle rimosse deve comparire.");
            Assert.Contains("Saltate: 2", with, "Il conteggio delle saltate deve comparire.");
        }

        [Test]
        public void FullFlow_OptionDisabled_KeepsFoldersAndReportUnchanged()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string vecchio = fx.CreateFile("vecchio.tmp", 512);
                    string vuota = Path.Combine(fx.Root, "vuota");
                    Directory.CreateDirectory(vuota);
                    string alberoA = Path.Combine(fx.Root, "albero", "a");
                    Directory.CreateDirectory(alberoA);

                    string reports = TempDirectory("emptyflow-off-reports");
                    try
                    {
                        List<string> messages = new List<string>();
                        List<int> folderConfirmCounts = new List<int>();
                        PrepareFlowForm(form, fx.Root, messages, folderConfirmCounts, true, reports);
                        SetField(form, "_removeEmptyFolders", false);
                        PopulateAndSelect(form, vecchio);

                        // Opzione disattivata (default 0.3.6): nessuna fase cartelle.
                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Assert.False(File.Exists(vecchio), "Il file selezionato deve essere eliminato.");
                        Assert.True(Directory.Exists(vuota), "Con opzione spenta la cartella vuota deve restare.");
                        Assert.True(Directory.Exists(alberoA), "Con opzione spenta l'albero deve restare.");
                        Assert.Null(GetFieldValue(form, "_lastEmptyClean"),
                            "Con opzione spenta non deve esistere alcun risultato cartelle.");
                        Assert.Equal(0, folderConfirmCounts.Count,
                            "Con opzione spenta la conferma cartelle non deve nemmeno essere mostrata.");

                        string report = ReadSingleReport(reports);
                        Assert.Contains("File eliminati", report, "Il report dei file deve restare identico.");
                        Assert.DoesNotContain("Cartelle vuote rimosse", report,
                            "Con opzione spenta il report non deve avere la sezione cartelle.");
                        Assert.DoesNotContain("cartelleVuote", report,
                            "Con opzione spenta il report non deve avere chiavi cartelle.");

                        string summary = LastMessageContaining(messages, "Pulizia completata.");
                        Assert.NotNull(summary, "Il riepilogo di pulizia deve essere mostrato.");
                        Assert.DoesNotContain("Cartelle vuote", summary,
                            "Con opzione spenta il riepilogo non deve avere la sezione cartelle.");
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
        public void FullFlow_OptionEnabled_RemovesFoldersAfterDedicatedConfirmation()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string vecchio = fx.CreateFile("vecchio.tmp", 512);
                    string vuota = Path.Combine(fx.Root, "vuota");
                    Directory.CreateDirectory(vuota);
                    string alberoA = Path.Combine(fx.Root, "albero", "a");
                    Directory.CreateDirectory(alberoA);

                    string reports = TempDirectory("emptyflow-on-reports");
                    try
                    {
                        List<string> messages = new List<string>();
                        List<int> folderConfirmCounts = new List<int>();
                        PrepareFlowForm(form, fx.Root, messages, folderConfirmCounts, true, reports);
                        SetField(form, "_removeEmptyFolders", true);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Assert.False(File.Exists(vecchio), "Il file selezionato deve essere eliminato.");
                        Assert.False(Directory.Exists(vuota), "La cartella vuota deve essere rimossa.");
                        Assert.False(Directory.Exists(alberoA),
                            "L'albero annidato tutto vuoto deve essere rimosso in un'unica esecuzione.");
                        Assert.False(Directory.Exists(Path.Combine(fx.Root, "albero")),
                            "La madre dell'albero deve essere rimossa dopo la figlia.");
                        Assert.True(Directory.Exists(fx.Root), "La radice non deve mai essere rimossa.");

                        Assert.Equal(1, folderConfirmCounts.Count,
                            "La conferma dedicata deve essere mostrata esattamente una volta.");
                        Assert.Equal(3, folderConfirmCounts[0],
                            "Il conteggio comunicato deve coprire tutte le candidate: vuota, a, albero.");

                        EmptyCleanResult last = (EmptyCleanResult)GetFieldValue(form, "_lastEmptyClean");
                        Assert.NotNull(last, "Il risultato della fase cartelle deve essere conservato.");
                        Assert.Equal(3, last.RemovedCount, "Tutte e tre le cartelle devono essere rimosse.");

                string report = ReadSingleReport(reports);
                Assert.Contains("Cartelle vuote rimosse", report,
                    "Il report deve dichiarare la rimozione delle cartelle.");
                Assert.Contains(": 3", report,
                    "Il conteggio delle cartelle rimosse deve essere reale.");

                        string summary = LastMessageContaining(messages, "Pulizia completata.");
                        Assert.NotNull(summary, "Il riepilogo di pulizia deve essere mostrato.");
                        Assert.Contains("Cartelle vuote (fase separata e facoltativa):", summary,
                            "Il riepilogo deve includere la sezione cartelle.");
                        Assert.Contains("Rimosse: 3", summary, "Il conteggio deve essere nel riepilogo.");
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
        public void FullFlow_DedicatedConfirmationDeclined_KeepsEveryFolder()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (Fixture fx = new Fixture())
                using (MainForm form = new MainForm())
                {
                    string vecchio = fx.CreateFile("vecchio.tmp", 512);
                    string vuota = Path.Combine(fx.Root, "vuota");
                    Directory.CreateDirectory(vuota);
                    string alberoA = Path.Combine(fx.Root, "albero", "a");
                    Directory.CreateDirectory(alberoA);

                    string reports = TempDirectory("emptyflow-declined-reports");
                    try
                    {
                        List<string> messages = new List<string>();
                        List<int> folderConfirmCounts = new List<int>();
                        // Conferma cartelle rifiutata: la fase non deve toccare nulla.
                        PrepareFlowForm(form, fx.Root, messages, folderConfirmCounts, false, reports);
                        SetField(form, "_removeEmptyFolders", true);
                        PopulateAndSelect(form, vecchio);

                        Invoke(form, "OnCleanClick", new object[] { null, EventArgs.Empty });
                        WaitUntilIdle(form);

                        Assert.False(File.Exists(vecchio), "Il file selezionato deve essere eliminato.");
                        Assert.True(Directory.Exists(vuota), "La cartella vuota deve essere conservata.");
                        Assert.True(Directory.Exists(alberoA), "L'albero deve essere conservato.");
                        Assert.Equal(1, folderConfirmCounts.Count,
                            "La conferma dedicata deve essere stata mostrata.");
                        Assert.Equal(3, folderConfirmCounts[0],
                            "Il conteggio comunicato deve essere reale anche se rifiutato.");
                        Assert.Null(GetFieldValue(form, "_lastEmptyClean"),
                            "Con conferma rifiutata non deve esserci risultato cartelle.");

                        string report = ReadSingleReport(reports);
                        Assert.Contains("File eliminati", report, "Il report dei file deve restare.");
                        Assert.DoesNotContain("Cartelle vuote rimosse", report,
                            "Con conferma rifiutata il report non deve avere la sezione cartelle.");

                        string summary = LastMessageContaining(messages, "Pulizia completata.");
                        Assert.NotNull(summary, "Il riepilogo di pulizia deve essere mostrato.");
                        Assert.DoesNotContain("Cartelle vuote", summary,
                            "Con conferma rifiutata il riepilogo non deve avere la sezione cartelle.");
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

        private static void PrepareFlowForm(MainForm form, string root, List<string> messages,
            List<int> folderConfirmCounts, bool folderConfirmAnswer, string reportsFolder)
        {
            SetField(form, "_getAuthorizedRoot",
                new Func<TempPathResult>(delegate { return TempPathResult.Ok(root); }));
            SetField(form, "_confirmClean",
                new Func<string, bool>(delegate(string question) { messages.Add(question); return true; }));
            SetField(form, "_confirmRemoveEmptyFolders",
                new Func<int, bool>(delegate(int count)
                {
                    folderConfirmCounts.Add(count);
                    return folderConfirmAnswer;
                }));
            SetField(form, "_showMessage",
                new Func<string, string, MessageBoxIcon, bool>(delegate(string text, string title, MessageBoxIcon icon)
                {
                    messages.Add(text);
                    return true;
                }));
            SetField(form, "_notifyUser",
                new Action<string>(delegate(string text) { messages.Add(text); }));
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

        /// <summary>
        /// Attende la fine dell'operazione asincrona pompando la coda dei messaggi
        /// (le continuazioni WinForms arrivano tramite WindowsFormsSynchronizationContext).
        /// Se un dialogo reale non stubbato restasse aperto, il timeout produce un
        /// errore chiaro invece di bloccare la suite.
        /// </summary>
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
                "L'operazione non è terminata entro 20 secondi: dialogo non stubbato o blocco.");
        }

        private static string ReadSingleReport(string reportsFolder)
        {
            string[] files = Directory.GetFiles(reportsFolder, "cleanslate-*.txt");
            Assert.Equal(1, files.Length, "Deve essere scritto esattamente un report.");
            return File.ReadAllText(files[0]);
        }

        private static string LastMessageContaining(List<string> messages, string token)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
                if (messages[i] != null && messages[i].IndexOf(token, StringComparison.Ordinal) >= 0)
                    return messages[i];
            return null;
        }

        private static int IndexOfOutcome(EmptyCleanResult result, string fullPath)
        {
            for (int i = 0; i < result.Items.Count; i++)
                if (string.Equals(result.Items[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static EmptyDirectoryItemResult FindOutcome(EmptyCleanResult result, string fullPath)
        {
            int index = IndexOfOutcome(result, fullPath);
            if (index < 0)
                throw new AssertException("Nessun esito registrato per: " + fullPath);
            return result.Items[index];
        }

        private static string TempFile(string prefix)
        {
            return Path.Combine(Path.GetTempPath(),
                "cleanslate-" + prefix + "-" + Guid.NewGuid().ToString("N") + ".txt");
        }

        private static string TempDirectory(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "cleanslate-" + prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteIfPresent(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (Exception) { }
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
