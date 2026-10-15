// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.IO;
using CleanSlate.Core;

namespace CleanSlate.Tests
{
    /// <summary>Test della costruzione e dell'esportazione del report locale.</summary>
    public sealed class ReportTests
    {
        private static ScanResult SampleScan(string root)
        {
            ScanResult scan = new ScanResult();
            scan.Success = true;
            scan.Root = root;
            scan.FilesScanned = 7;
            scan.FilesEligible = 3;
            scan.FilesSkipped = 4;
            scan.DirectoriesSkipped = 1;
            scan.DirectoriesVisited = 5;
            scan.Errors = 1;
            scan.EstimatedBytes = 4096;
            scan.Duration = TimeSpan.FromSeconds(1.5);
            scan.ErrorMessages.Add("Accesso negato alla cartella " + Path.Combine(root, "protetta") + ".");
            scan.ErrorMessages.Add("Errore con \"virgolette\" e\na capo.");
            return scan;
        }

        private static CleanResult SampleClean()
        {
            CleanResult clean = new CleanResult();
            clean.Success = true;
            clean.DeletedCount = 2;
            clean.DeletedBytes = 3000;
            clean.SkippedCount = 1;
            clean.ErrorCount = 0;
            clean.EstimatedBytes = 4096;
            clean.Duration = TimeSpan.FromSeconds(0.5);
            return clean;
        }

        [Test]
        public void Report_Build_DoesNotCreateAnyFile()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("a.tmp", 10);
                Dictionary<string, string> before = Fixture.Snapshot(fx.BaseDir);

                ReportData data = ReportBuilder.Build(SampleScan(fx.Root), SampleClean(), fx.Root);
                Assert.NotNull(data, "I dati del report devono essere prodotti in memoria.");

                Dictionary<string, string> after = Fixture.Snapshot(fx.BaseDir);
                Fixture.AssertSameSnapshot(before, after, "La costruzione del report ha scritto su disco");
            }
        }

        [Test]
        public void Report_ExportWithoutExplicitPath_Throws()
        {
            ReportData data = ReportBuilder.Build(SampleScan("C:\\qualsiasi"), null, "C:\\qualsiasi");
            Assert.Throws<ArgumentException>(
                delegate { ReportExporter.Export(null, data); },
                "Senza percorso l'esportazione deve essere rifiutata.");
            Assert.Throws<ArgumentException>(
                delegate { ReportExporter.Export(string.Empty, data); },
                "Con percorso vuoto l'esportazione deve essere rifiutata.");
        }

        [Test]
        public void Report_Export_WritesExactlyOneFileAtRequestedPath()
        {
            using (Fixture fx = new Fixture())
            {
                fx.CreateFile("a.tmp", 10);
                ReportData data = ReportBuilder.Build(SampleScan(fx.Root), SampleClean(), fx.Root);

                string exportDir = Path.Combine(fx.BaseDir, "esportazione");
                Directory.CreateDirectory(exportDir);

                Dictionary<string, string> rootBefore = Fixture.Snapshot(fx.Root);
                Dictionary<string, string> baseBefore = Fixture.Snapshot(fx.BaseDir);

                string target = Path.Combine(exportDir, "report-locale.txt");
                ReportExporter.Export(target, data);

                string[] created = Directory.GetFiles(exportDir, "*", SearchOption.AllDirectories);
                Assert.Equal(1, created.Length, "Deve essere creato un solo file.");
                Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(created[0]),
                    "Il file deve trovarsi esattamente nel percorso scelto.");

                Dictionary<string, string> rootAfter = Fixture.Snapshot(fx.Root);
                Fixture.AssertSameSnapshot(rootBefore, rootAfter,
                    "L'esportazione ha scritto dentro la cartella analizzata");

                Dictionary<string, string> baseAfter = Fixture.Snapshot(fx.BaseDir);
                Assert.Equal(baseBefore.Count + 1, baseAfter.Count,
                    "L'esportazione deve creare un solo file in tutto l'albero di test.");
                Assert.True(File.Exists(target), "Il file richiesto deve esistere.");
            }
        }

        [Test]
        public void Report_Text_ContainsVersionCountersAndNotes()
        {
            using (Fixture fx = new Fixture())
            {
                ReportData data = ReportBuilder.Build(SampleScan(fx.Root), SampleClean(), fx.Root);
                string text = ReportExporter.BuildText(data);

                Assert.Contains("CleanSlate", text, "Deve contenere il nome prodotto.");
                Assert.Contains(AppInfo.VersionText, text, "Deve contenere la versione.");
                Assert.Contains("File analizzati", text, "Deve contenere i conteggi dell'analisi.");
                Assert.Contains("Cartelle esplorate", text, "Deve dichiarare le cartelle percorse.");
                Assert.Contains("File eliminati", text, "Deve contenere i risultati della pulizia.");
                Assert.Contains("Spazio effettivamente liberato", text, "Deve dichiarare lo spazio realmente rimosso.");                Assert.Contains("connessione di rete", text, "Deve dichiarare il comportamento offline.");
            }
        }

        [Test]
        public void Report_SanitizesPathsAndDoesNotExposeUserProfile()
        {
            using (Fixture fx = new Fixture())
            {
                ReportData data = ReportBuilder.Build(SampleScan(fx.Root), SampleClean(), fx.Root);
                string text = ReportExporter.BuildText(data);
                string json = ReportExporter.BuildJson(data);

                Assert.DoesNotContain(fx.Root, text, "Il report in testo non deve contenere la radice temporanea.");
                Assert.DoesNotContain(fx.Root, json, "Il report JSON non deve contenere la radice temporanea.");
                Assert.DoesNotContain(fx.BaseDir, text, "Il report non deve contenere la cartella di test.");
                Assert.Contains(ReportBuilder.RootPlaceholder, text, "La radice deve essere sostituita dal segnaposto.");

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile) && userProfile.Length > 6)
                {
                    Assert.DoesNotContain(userProfile, text,
                        "Il report non deve contenere il percorso del profilo utente.");
                    Assert.DoesNotContain(userProfile, json,
                        "Il report JSON non deve contenere il percorso del profilo utente.");
                }
            }
        }

        [Test]
        public void Report_Json_IsValidShapeAndEscapesControlCharacters()
        {
            using (Fixture fx = new Fixture())
            {
                ReportData data = ReportBuilder.Build(SampleScan(fx.Root), SampleClean(), fx.Root);
                string json = ReportExporter.BuildJson(data);

                Assert.True(json.TrimStart().StartsWith("{", StringComparison.Ordinal), "Deve iniziare con '{'.");
                Assert.True(json.TrimEnd().EndsWith("}", StringComparison.Ordinal), "Deve terminare con '}'.");
                Assert.Contains("\"prodotto\": \"CleanSlate\"", json, "Chiave prodotto attesa.");
                Assert.Contains("\"versione\": \"" + AppInfo.VersionText + "\"", json, "Chiave versione attesa.");
                Assert.Contains("\"fileEliminati\": 2", json, "Contatore JSON atteso.");
                Assert.Contains("\"cartelleEsplorate\": 5", json, "Contatore JSON delle cartelle atteso.");
                Assert.Contains("\"spazioRecuperabileStimatoByte\": 4096", json, "Byte JSON attesi.");
                Assert.Contains("\\\"virgolette\\\"", json, "Le virgolette devono essere scappate.");
                Assert.Contains("\\n", json, "I ritorni a capo devono essere scappati.");
                Assert.False(json.Contains("\u0000"), "Il JSON non può contenere caratteri di controllo nudi.");
            }
        }

        [Test]
        public void Report_Build_ReflectsCancellationState()
        {
            ScanResult scan = SampleScan("C:\\temp");
            scan.Cancelled = true;
            CleanResult clean = SampleClean();
            clean.Cancelled = true;

            ReportData data = ReportBuilder.Build(scan, clean, "C:\\temp");
            string text = ReportExporter.BuildText(data);

            Assert.Contains("annullata", text, "Lo stato di annullamento deve essere riportato.");
            Assert.Contains("Analisi e pulizia", text, "L'operazione combinata deve essere indicata.");
        }

        [Test]
        public void Report_Build_WithNoData_ReportsNoOperation()
        {
            ReportData data = ReportBuilder.Build(null, null, null);
            Assert.Contains("Nessuna operazione", data.Operation, "Senza dati non c'è operazione.");
            string text = ReportExporter.BuildText(data);
            Assert.Contains("Nessuna analisi eseguita.", text, "Il testo deve riportare l'assenza di analisi.");
            Assert.Contains("Nessuna pulizia eseguita.", text, "Il testo deve riportare l'assenza di pulizia.");
        }
    }
}
