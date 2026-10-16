// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Nuvacline.Core;
using Nuvacline.UI;

namespace Nuvacline.Tests
{
    /// <summary>
    /// Test del selettore di tema (Automatico / Chiaro / Scuro).
    ///
    /// Coprono: esistenza e ordine delle voci della combo, applicazione dei tre
    /// temi con colori presi solo dalla palette, persistenza della preferenza in
    /// ScanSettings (file temporanei), preferenza non valida o assente che ricade
    /// sull'automatico, salvataggio fallito che non blocca l'applicazione,
    /// ripristino all'avvio senza riscritture, ignoranza dei messaggi di sistema
    /// con tema forzato e ritorno all'aggiornamento in tempo reale in automatico.
    ///
    /// Nessun test tocca il vero %LOCALAPPDATA%\Nuvacline\settings.txt: la
    /// persistenza usa file temporanei e i form iniettano i seam dedicati.
    /// </summary>
    public sealed class ThemeChoiceTests
    {
        #region Persistenza della preferenza

        [Test]
        public void ThemeChoice_SaveAndLoad_RoundTripsEveryValue()
        {
            string path = TempFile("theme-roundtrip");
            try
            {
                Assert.True(ScanSettings.SaveThemeChoiceTo(path, ThemeChoice.Light),
                    "Il salvataggio del tema chiaro deve riuscire.");
                Assert.Equal(ThemeChoice.Light, ScanSettings.LoadThemeChoiceFrom(path),
                    "Il tema chiaro deve essere ripristinato dal file.");

                Assert.True(ScanSettings.SaveThemeChoiceTo(path, ThemeChoice.Dark),
                    "Il salvataggio del tema scuro deve riuscire.");
                Assert.Equal(ThemeChoice.Dark, ScanSettings.LoadThemeChoiceFrom(path),
                    "Il tema scuro deve essere ripristinato dal file.");

                Assert.True(ScanSettings.SaveThemeChoiceTo(path, ThemeChoice.Automatic),
                    "Il salvataggio dell'automatico deve riuscire.");
                Assert.Equal(ThemeChoice.Automatic, ScanSettings.LoadThemeChoiceFrom(path),
                    "L'automatico deve essere ripristinato dal file.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void ThemeChoice_MissingOrInvalidValue_FallsBackToAutomatic()
        {
            string missing = TempFile("theme-missing");
            try
            {
                Assert.False(File.Exists(missing), "Il file di partenza non deve esistere.");
                Assert.Equal(ThemeChoice.Automatic, ScanSettings.LoadThemeChoiceFrom(missing),
                    "Un file assente deve produrre il tema automatico.");
                Assert.Equal(ThemeChoice.Automatic, ScanSettings.LoadThemeChoiceFrom(null),
                    "Un percorso nullo deve produrre il tema automatico.");
            }
            finally
            {
                DeleteIfPresent(missing);
            }

            string path = TempFile("theme-invalid");
            try
            {
                File.WriteAllText(path,
                    "# commento con dentro Theme=dark\r\n"
                    + "MinAgeHours=48\r\n"
                    + "Theme=rosso\r\n");
                Assert.Equal(ThemeChoice.Automatic, ScanSettings.LoadThemeChoiceFrom(path),
                    "Un valore non riconosciuto deve produrre il tema automatico.");

                File.WriteAllText(path, "Theme=\r\n");
                Assert.Equal(ThemeChoice.Automatic, ScanSettings.LoadThemeChoiceFrom(path),
                    "Un valore vuoto deve produrre il tema automatico.");

                File.WriteAllText(path, "qualsiasi riga senza pari\r\nTheme impostato a mano\r\n");
                Assert.Equal(ThemeChoice.Automatic, ScanSettings.LoadThemeChoiceFrom(path),
                    "Righe malformate senza chiave valida devono produrre l'automatico.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void ThemeChoice_KeyAndValueMatching_IsCaseInsensitiveAndTrimsSpaces()
        {
            string path = TempFile("theme-case");
            try
            {
                File.WriteAllText(path, "theme = dark \r\n");
                Assert.Equal(ThemeChoice.Dark, ScanSettings.LoadThemeChoiceFrom(path),
                    "Chiave e valore con spazi e maiuscole devono essere riconosciuti.");

                File.WriteAllText(path, "THEME=LIGHT\r\n");
                Assert.Equal(ThemeChoice.Light, ScanSettings.LoadThemeChoiceFrom(path),
                    "Il valore LIGHT deve produrre il tema chiaro.");

                File.WriteAllText(path, "Theme=dark\r\nTheme=light\r\n");
                Assert.Equal(ThemeChoice.Dark, ScanSettings.LoadThemeChoiceFrom(path),
                    "Vince la prima occorrenza valida della chiave.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void ThemeChoice_Save_PreservesTheOtherSettings()
        {
            string path = TempFile("theme-preserves");
            try
            {
                File.WriteAllText(path,
                    "MinAgeHours=48\r\n"
                    + "KeepReports=1\r\n"
                    + "RemoveEmptyFolders=1\r\n");

                Assert.True(ScanSettings.SaveThemeChoiceTo(path, ThemeChoice.Dark),
                    "Il salvataggio del tema deve riuscire.");

                Assert.Equal(ThemeChoice.Dark, ScanSettings.LoadThemeChoiceFrom(path),
                    "Il tema salvato deve essere leggibile.");
                Assert.Equal(48, ScanSettings.LoadFrom(path).Hours,
                    "La soglia d'età esistente deve essere preservata.");
                Assert.True(ScanSettings.LoadKeepReportsFrom(path),
                    "La conservazione dei report esistente deve essere preservata.");
                Assert.True(ScanSettings.LoadRemoveEmptyFoldersFrom(path),
                    "La rimozione delle cartelle vuote esistente deve essere preservata.");
            }
            finally
            {
                DeleteIfPresent(path);
            }
        }

        [Test]
        public void ThemeChoiceText_MappingIsStableAndFailSafe()
        {
            Assert.Equal("light", ScanSettings.ThemeChoiceToText(ThemeChoice.Light),
                "Il tema chiaro deve persistersi come light.");
            Assert.Equal("dark", ScanSettings.ThemeChoiceToText(ThemeChoice.Dark),
                "Il tema scuro deve persistersi come dark.");
            Assert.Equal("auto", ScanSettings.ThemeChoiceToText(ThemeChoice.Automatic),
                "L'automatico deve persistersi come auto.");
            Assert.Equal("auto", ScanSettings.ThemeChoiceToText((ThemeChoice)99),
                "Un valore fuori dall'enum deve persistersi come auto (fail-safe).");

            Assert.Equal(ThemeChoice.Light, ScanSettings.ThemeChoiceFromText("light"),
                "Il testo light deve produrre il tema chiaro.");
            Assert.Equal(ThemeChoice.Dark, ScanSettings.ThemeChoiceFromText("Dark"),
                "Il testo Dark deve produrre il tema scuro.");
            Assert.Equal(ThemeChoice.Automatic, ScanSettings.ThemeChoiceFromText("auto"),
                "Il testo auto deve produrre l'automatico.");
            Assert.Equal(ThemeChoice.Automatic, ScanSettings.ThemeChoiceFromText(null),
                "Un testo nullo deve produrre l'automatico.");
            Assert.Equal(ThemeChoice.Automatic, ScanSettings.ThemeChoiceFromText("verde"),
                "Un testo sconosciuto deve produrre l'automatico.");
        }

        #endregion

        #region Selettore nella finestra principale

        [Test]
        public void ThemeSelector_ExistsWithTheThreeChoicesInOrder()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Control[] found = form.Controls.Find("cboTheme", true);
                    Assert.Equal(1, found.Length,
                        "La combo del tema deve esistere esattamente una volta.");
                    ComboBox combo = found[0] as ComboBox;
                    Assert.NotNull(combo, "Il controllo cboTheme deve essere una combo box.");
                    Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle,
                        "La combo del tema deve essere a selezione singola non editabile.");
                    Assert.Equal(3, combo.Items.Count,
                        "La combo del tema deve avere esattamente tre voci.");
                    Assert.Equal("Automatico", combo.Items[0],
                        "La prima voce deve essere Automatico.");
                    Assert.Equal("Chiaro", combo.Items[1],
                        "La seconda voce deve essere Chiaro.");
                    Assert.Equal("Scuro", combo.Items[2],
                        "La terza voce deve essere Scuro.");
                    Assert.True(combo.Anchor.HasFlag(AnchorStyles.Right),
                        "La combo deve restare agganciata al bordo destro.");
                    Assert.True(combo.Enabled, "La combo del tema deve essere attiva.");

                    Control[] labels = form.Controls.Find("lblTheme", true);
                    Assert.Equal(1, labels.Length,
                        "L'etichetta del tema deve esistere esattamente una volta.");
                    Label label = labels[0] as Label;
                    Assert.NotNull(label, "Il controllo lblTheme deve essere un'etichetta.");
                    Assert.Equal("Tema:", label.Text,
                        "L'etichetta deve riportare il testo atteso.");
                    Assert.True(label.Anchor.HasFlag(AnchorStyles.Right),
                        "L'etichetta deve restare agganciata al bordo destro.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_KeepsTheTopRowFreeOfOverlaps()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    ComboBox combo = GetField<ComboBox>(form, "_cboTheme");
                    Label label = GetField<Label>(form, "_lblTheme");

                    AssertNoTopRowOverlap(form, combo, label,
                        "Con la larghezza predefinita");

                    form.ClientSize = new Size(800, form.ClientSize.Height);
                    AssertNoTopRowOverlap(form, combo, label,
                        "Con la larghezza minima di 800");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_SelectingAChoice_SavesItAndAppliesTheTheme()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    PinAutomatic(form);
                    ThemeChoice saved = ThemeChoice.Automatic;
                    int saveCalls = 0;
                    SetField(form, "_saveThemeChoice",
                        new Func<ThemeChoice, bool>(delegate(ThemeChoice choice)
                        {
                            saved = choice;
                            saveCalls++;
                            return true;
                        }));

                    ComboBox combo = GetField<ComboBox>(form, "_cboTheme");

                    combo.SelectedIndex = 2;
                    Assert.Equal(ThemeChoice.Dark, saved,
                        "Selezionando Scuro deve essere salvata la preferenza scura.");
                    Assert.Equal(1, saveCalls, "La selezione deve produrre esattamente un salvataggio.");
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Selezionando Scuro il tema applicato deve essere scuro.");
                    Assert.Equal(ThemePalette.DarkPalette().Surface.ToArgb(),
                        form.BackColor.ToArgb(),
                        "Lo sfondo deve usare la palette scura.");

                    combo.SelectedIndex = 1;
                    Assert.Equal(ThemeChoice.Light, saved,
                        "Selezionando Chiaro deve essere salvata la preferenza chiara.");
                    Assert.Equal(AppTheme.Light, GetFieldValue(form, "_currentTheme"),
                        "Selezionando Chiaro il tema applicato deve essere chiaro.");
                    Assert.Equal(SystemColors.Control.ToArgb(), form.BackColor.ToArgb(),
                        "Lo sfondo deve tornare al colore di sistema.");

                    int reads = 0;
                    SetField(form, "_detectTheme", new Func<AppTheme>(delegate
                    {
                        reads++;
                        return AppTheme.Dark;
                    }));
                    combo.SelectedIndex = 0;
                    Assert.Equal(ThemeChoice.Automatic, saved,
                        "Selezionando Automatico deve essere salvata la preferenza automatica.");
                    Assert.Equal(1, reads,
                        "Tornando in automatico il tema deve essere rilegguto dal seam.");
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "In automatico il tema deve seguire la rilevazione del sistema.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_SavedPreference_IsRestoredOnStartupWithoutResaving()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    int saveCalls = 0;
                    SetField(form, "_saveThemeChoice",
                        new Func<ThemeChoice, bool>(delegate(ThemeChoice choice)
                        {
                            saveCalls++;
                            return true;
                        }));
                    SetField(form, "_loadThemeChoice",
                        new Func<ThemeChoice>(delegate { return ThemeChoice.Dark; }));
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Light; }));

                    Invoke(form, "LoadThemeChoiceSetting", null);
                    Invoke(form, "ApplyTheme", null);

                    Assert.Equal(ThemeChoice.Dark, GetFieldValue(form, "_themeChoice"),
                        "La preferenza scura caricata deve essere mantenuta.");
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "All'avvio deve valere il tema scuro salvato, non la rilevazione.");
                    Assert.Equal(2, GetField<ComboBox>(form, "_cboTheme").SelectedIndex,
                        "La combo deve mostrare la voce Scuro.");
                    Assert.Equal(0, saveCalls,
                        "Il ripristino all'avvio non deve riscrivere la preferenza.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_InvalidSavedPreference_FallsBackToAutomatic()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_loadThemeChoice",
                        new Func<ThemeChoice>(delegate { return (ThemeChoice)42; }));
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Dark; }));

                    Invoke(form, "LoadThemeChoiceSetting", null);
                    Invoke(form, "ApplyTheme", null);

                    Assert.Equal(ThemeChoice.Automatic, GetFieldValue(form, "_themeChoice"),
                        "Una preferenza fuori dall'enum deve cadere sull'automatico.");
                    Assert.Equal(0, GetField<ComboBox>(form, "_cboTheme").SelectedIndex,
                        "La combo deve tornare sulla voce Automatico.");
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Dopo il fallback deve valere la rilevazione del sistema.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_FailingLoadOrSave_DoesNotBreakTheForm()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_loadThemeChoice",
                        new Func<ThemeChoice>(delegate
                        {
                            throw new IOException("lettura non disponibile");
                        }));
                    Invoke(form, "LoadThemeChoiceSetting", null);
                    Assert.Equal(ThemeChoice.Automatic, GetFieldValue(form, "_themeChoice"),
                        "Un errore di lettura deve cadere sull'automatico.");

                    SetField(form, "_saveThemeChoice",
                        new Func<ThemeChoice, bool>(delegate(ThemeChoice choice)
                        {
                            throw new UnauthorizedAccessException("scrittura non disponibile");
                        }));

                    ComboBox combo = GetField<ComboBox>(form, "_cboTheme");
                    combo.SelectedIndex = 2;
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Un errore di salvataggio non deve impedire l'applicazione del tema.");
                    Assert.Equal(2, combo.SelectedIndex,
                        "Un errore di salvataggio non deve impedire la selezione visiva.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_ForcedTheme_IgnoresSystemChangesAndRestoresLiveUpdates()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    PinAutomatic(form);
                    int reads = 0;
                    SetField(form, "_detectTheme", new Func<AppTheme>(delegate
                    {
                        reads++;
                        return AppTheme.Light;
                    }));

                    ComboBox combo = GetField<ComboBox>(form, "_cboTheme");
                    combo.SelectedIndex = 2;
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Il tema scuro deve essere applicato dalla selezione.");

                    // Finestra creata per ricevere i messaggi (mai mostrata).
                    IntPtr window = form.Handle;
                    Assert.NotEqual(IntPtr.Zero, window,
                        "La finestra deve essere creata per ricevere i messaggi di sistema.");

                    int beforeForced = reads;
                    SendWndProcWithString(form, 0x001A, "ImmersiveColorSet");
                    Assert.Equal(beforeForced, reads,
                        "Con tema forzato i messaggi di sistema non devono rileggere il tema.");
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Con tema forzato il tema scuro deve restare applicato.");

                    combo.SelectedIndex = 0;
                    Assert.Equal(1, reads - beforeForced,
                        "Tornando in automatico deve essere riletto il tema di sistema.");

                    int beforeLive = reads;
                    SendWndProcWithString(form, 0x001A, "ImmersiveColorSet");
                    Assert.True(reads > beforeLive,
                        "Tornato automatico, il tema deve di nuovo aggiornarsi in tempo reale.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void ThemeSelector_ColorsComeOnlyFromThePalette()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    PinAutomatic(form);
                    ComboBox combo = GetField<ComboBox>(form, "_cboTheme");
                    Label label = GetField<Label>(form, "_lblTheme");

                    combo.SelectedIndex = 2;
                    ThemePalette dark = (ThemePalette)GetFieldValue(form, "_palette");
                    Assert.Equal(dark.TextSecondary.ToArgb(), label.ForeColor.ToArgb(),
                        "Con tema scuro l'etichetta deve usare il testo secondario della palette.");
                    Assert.Equal(dark.Surface.ToArgb(), combo.BackColor.ToArgb(),
                        "Con tema scuro la combo deve usare lo sfondo della palette.");
                    Assert.Equal(dark.Text.ToArgb(), combo.ForeColor.ToArgb(),
                        "Con tema scuro la combo deve usare il testo della palette.");

                    combo.SelectedIndex = 1;
                    ThemePalette light = (ThemePalette)GetFieldValue(form, "_palette");
                    Assert.Equal(light.TextSecondary.ToArgb(), label.ForeColor.ToArgb(),
                        "Con tema chiaro l'etichetta deve usare il testo secondario di sistema.");
                    Assert.Equal(light.Surface.ToArgb(), combo.BackColor.ToArgb(),
                        "Con tema chiaro la combo deve usare lo sfondo di sistema.");
                    Assert.Equal(light.Text.ToArgb(), combo.ForeColor.ToArgb(),
                        "Con tema chiaro la combo deve usare il testo di sistema.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Helper

        /// <summary>
        /// Fissa la preferenza su automatica tramite i seam, così il risultato
        /// del test dipende solo dalle iniezioni e mai dal settings.txt reale.
        /// Anche il seam di salvataggio viene puntato su un no-op: nessun test
        /// deve mai scrivere la preferenza dell'utente.
        /// </summary>
        private static void PinAutomatic(MainForm form)
        {
            SetField(form, "_loadThemeChoice",
                new Func<ThemeChoice>(delegate { return ThemeChoice.Automatic; }));
            SetField(form, "_saveThemeChoice",
                new Func<ThemeChoice, bool>(delegate(ThemeChoice choice) { return true; }));
            Invoke(form, "LoadThemeChoiceSetting", null);
        }

        /// <summary>
        /// Verifica che etichetta, combo e pulsanti di servizio della riga alta
        /// non si sovrappongano e restino dentro la finestra.
        /// </summary>
        private static void AssertNoTopRowOverlap(MainForm form, ComboBox combo, Label label, string context)
        {
            Label title = GetField<Label>(form, "_titleLabel");
            Button info = GetField<Button>(form, "_btnInfo");

            Assert.True(label.Left >= title.Right,
                context + ": l'etichetta del tema non deve coprire il titolo.");
            Assert.True(combo.Left >= label.Right,
                context + ": la combo non deve coprire la sua etichetta.");
            Assert.True(combo.Right <= info.Left,
                context + ": la combo non deve coprire il pulsante Informazioni.");
            Assert.True(combo.Right <= form.ClientSize.Width,
                context + ": la combo deve restare dentro la finestra.");
        }

        private static void SendWndProcWithString(MainForm form, int message, string text)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(text);
            try
            {
                Message m = Message.Create(form.Handle, message, IntPtr.Zero, pointer);
                MethodInfo wndProc = typeof(Control).GetMethod("WndProc",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (wndProc == null) throw new MissingMethodException("Control.WndProc");
                try
                {
                    object[] args = new object[] { m };
                    wndProc.Invoke(form, args);
                    m = (Message)args[0];
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static string TempFile(string prefix)
        {
            return Path.Combine(Path.GetTempPath(),
                "nuvacline-" + prefix + "-" + Guid.NewGuid().ToString("N") + ".txt");
        }

        private static void DeleteIfPresent(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
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
