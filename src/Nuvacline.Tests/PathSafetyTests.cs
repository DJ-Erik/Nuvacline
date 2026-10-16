// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.IO;
using Nuvacline.Core;

namespace Nuvacline.Tests
{
    /// <summary>Test delle regole di sicurezza sui percorsi.</summary>
    public sealed class PathSafetyTests
    {
        [Test]
        public void IsInsideRoot_FileInside_ReturnsTrue()
        {
            string root = @"C:\Temp\Authorized";
            string candidate = Path.Combine(root, "sub", "file.tmp");
            Assert.True(PathSafety.IsInsideRoot(root, candidate), "Un file dentro la radice deve essere accettato.");
        }

        [Test]
        public void IsInsideRoot_RootItself_ReturnsFalse()
        {
            string root = @"C:\Temp\Authorized";
            Assert.False(PathSafety.IsInsideRoot(root, root), "La radice stessa non può essere un bersaglio.");
        }

        [Test]
        public void IsInsideRoot_PrefixTrick_ReturnsFalse()
        {
            // "C:\Temp\AuthorizedEvil" inizia con "C:\Temp\Authorized" ma non è dentro.
            string root = @"C:\Temp\Authorized";
            string candidate = @"C:\Temp\AuthorizedEvil\file.tmp";
            Assert.False(PathSafety.IsInsideRoot(root, candidate), "Il trucco del prefisso comune deve essere rifiutato.");
        }

        [Test]
        public void IsInsideRoot_SiblingDirectory_ReturnsFalse()
        {
            string root = @"C:\Temp\Authorized";
            string candidate = @"C:\Temp\Other\file.tmp";
            Assert.False(PathSafety.IsInsideRoot(root, candidate), "Una cartella sorella è fuori radice.");
        }

        [Test]
        public void IsInsideRoot_TraversalEscapingRoot_ReturnsFalse()
        {
            using (Fixture fx = new Fixture())
            {
                string candidate = Path.Combine(fx.Root, "..", "outside", "file.tmp");
                Assert.False(PathSafety.IsInsideRoot(fx.Root, candidate),
                    "Un percorso con '..' che esce dalla radice deve essere rifiutato.");
            }
        }

        [Test]
        public void IsInsideRoot_TraversalStayingInside_ReturnsTrue()
        {
            using (Fixture fx = new Fixture())
            {
                string candidate = Path.Combine(fx.Root, "sub", "..", "file.tmp");
                Assert.True(PathSafety.IsInsideRoot(fx.Root, candidate),
                    "Un percorso con '..' che resta dentro la radice è ammesso.");
            }
        }

        [Test]
        public void IsInsideRoot_EmptyOrNull_ReturnsFalse()
        {
            Assert.False(PathSafety.IsInsideRoot(null, @"C:\Temp\x"), "Radice nulla deve essere rifiutata.");
            Assert.False(PathSafety.IsInsideRoot("", @"C:\Temp\x"), "Radice vuota deve essere rifiutata.");
            Assert.False(PathSafety.IsInsideRoot(@"C:\Temp", null), "Candidato nullo deve essere rifiutato.");
            Assert.False(PathSafety.IsInsideRoot(@"C:\Temp", ""), "Candidato vuoto deve essere rifiutato.");
        }

        [Test]
        public void IsInsideRoot_DifferentCase_IsStillInside()
        {
            string root = @"C:\Temp\Authorized";
            string candidate = @"c:\temp\AUTHORIZED\file.tmp";
            Assert.True(PathSafety.IsInsideRoot(root, candidate),
                "Su Windows il confronto dei percorsi non distingue le maiuscole.");
        }

        [Test]
        public void ValidateCleaningRoot_FixtureRoot_IsAccepted()
        {
            using (Fixture fx = new Fixture())
            {
                string normalized;
                string error;
                bool ok = PathSafety.ValidateCleaningRoot(fx.Root, out normalized, out error);
                Assert.True(ok, "La radice di test deve essere valida. Errore: " + error);
                Assert.Null(error, "Non deve esserci un messaggio di errore.");
                Assert.Equal(Path.GetFullPath(fx.Root), normalized, "Il percorso normalizzato deve corrispondere.");
            }
        }

        [Test]
        public void ValidateCleaningRoot_DriveRoot_IsRejected()
        {
            string normalized;
            string error;
            bool ok = PathSafety.ValidateCleaningRoot(@"C:\", out normalized, out error);
            Assert.False(ok, "L'unità di disco non può essere una radice di pulizia.");
            Assert.NotNull(error, "Deve essere prodotto un messaggio di errore.");
            Assert.Null(normalized, "Non deve essere restituita alcuna radice.");
        }

        [Test]
        public void ValidateCleaningRoot_NonexistentDirectory_IsRejected()
        {
            using (Fixture fx = new Fixture())
            {
                string missing = Path.Combine(fx.BaseDir, "non-esistente-" + Guid.NewGuid().ToString("N"));
                string normalized;
                string error;
                bool ok = PathSafety.ValidateCleaningRoot(missing, out normalized, out error);
                Assert.False(ok, "Una cartella inesistente deve essere rifiutata.");
                Assert.NotNull(error, "Deve essere prodotto un messaggio di errore in italiano.");
            }
        }

        [Test]
        public void ValidateCleaningRoot_UncPath_IsRejected()
        {
            string normalized;
            string error;
            bool ok = PathSafety.ValidateCleaningRoot(@"\\server\share\temp", out normalized, out error);
            Assert.False(ok, "Un percorso di rete non può essere una radice di pulizia.");
            Assert.Contains("rete", error, "Il messaggio deve segnalare il problema di rete.");
        }

        [Test]
        public void ValidateCleaningRoot_Empty_IsRejected()
        {
            string normalized;
            string error;
            Assert.False(PathSafety.ValidateCleaningRoot(null, out normalized, out error), "Percorso nullo rifiutato.");
            Assert.False(PathSafety.ValidateCleaningRoot("", out normalized, out error), "Percorso vuoto rifiutato.");
        }

        [Test]
        public void Reparse_NoJunctionInsideRoot_ReturnsFalse()
        {
            using (Fixture fx = new Fixture())
            {
                string file = fx.CreateFile("normale.tmp", 10);
                Assert.False(PathSafety.HasReparseComponentInsideRoot(fx.Root, file),
                    "Un file in una cartella normale non deve essere segnalato.");
            }
        }

        [Test]
        public void Reparse_JunctionBelowRoot_IsDetected()
        {
            using (Fixture fx = new Fixture())
            {
                string target = fx.CreateOutsideFile("target.txt", 10, TimeSpan.FromDays(2));
                string link = Path.Combine(fx.Root, "link");
                Fixture.CreateJunction(link, target);

                string throughLink = Path.Combine(link, "target.txt");
                Assert.True(PathSafety.HasReparseComponentInsideRoot(fx.Root, throughLink),
                    "Un percorso che attraversa una junction deve essere rilevato come non sicuro.");
            }
        }

        [Test]
        public void Reparse_OutsideRoot_IsTreatedAsUnsafe()
        {
            using (Fixture fx = new Fixture())
            {
                string outside = fx.CreateOutsideFile("fuori.txt", 10, TimeSpan.FromDays(2));
                Assert.True(PathSafety.HasReparseComponentInsideRoot(fx.Root, outside),
                    "Un percorso fuori radice deve essere trattato come non verificabile.");
            }
        }
    }
}
