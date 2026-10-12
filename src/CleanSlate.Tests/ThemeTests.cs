using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CleanSlate.Core;
using CleanSlate.UI;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Test del tema chiaro/scuro automatico.
    ///
    /// Nessun test dipende dal tema realmente impostato sulla macchina: la
    /// rilevazione passa dal seam WindowsTheme.AppsUseLightThemeReader (e dal
    /// seam di istanza _detectTheme di MainForm), che i test iniettano e
    /// ripristinano sempre. Coprono: mappatura del valore di tema, fallback
    /// chiaro su errore, palette centralizzata e contrasto leggibile,
    /// applicazione e ripristino su MainForm, finestra Informazioni, etichetta
    /// del report, aggiornamento in tempo reale via WM_SETTINGCHANGE /
    /// WM_THEMECHANGED e assenza di assegnazioni colore fuori dalla palette.
    /// </summary>
    public sealed class ThemeTests
    {
        #region Rilevamento

        [Test]
        public void ReaderValue_MapsZeroToDarkAndEverythingElseToLight()
        {
            Assert.Equal(AppTheme.Dark, WindowsTheme.FromReaderValue(0),
                "Valore 0: Windows usa il tema scuro.");
            Assert.Equal(AppTheme.Light, WindowsTheme.FromReaderValue(1),
                "Valore 1: Windows usa il tema chiaro.");
            Assert.Equal(AppTheme.Light, WindowsTheme.FromReaderValue(null),
                "Valore assente: deve valere il tema chiaro.");
            Assert.Equal(AppTheme.Light, WindowsTheme.FromReaderValue(7),
                "Valore inatteso: deve valere il tema chiaro.");
        }

        [Test]
        public void Detect_OnAnyMachine_ReturnsADefinedTheme()
        {
            AppTheme theme = WindowsTheme.Detect();
            Assert.True(theme == AppTheme.Light || theme == AppTheme.Dark,
                "Il rilevamento deve produrre un tema definito, ha prodotto: " + theme);
        }

        [Test]
        public void Detect_UsesTheSeamAndFallsBackToLight()
        {
            Func<int?> original = WindowsTheme.AppsUseLightThemeReader;
            try
            {
                WindowsTheme.AppsUseLightThemeReader = delegate { return (int?)0; };
                Assert.Equal(AppTheme.Dark, WindowsTheme.Detect(),
                    "Lettura 0: il rilevamento deve produrre il tema scuro.");

                WindowsTheme.AppsUseLightThemeReader = delegate { return (int?)1; };
                Assert.Equal(AppTheme.Light, WindowsTheme.Detect(),
                    "Lettura 1: il rilevamento deve produrre il tema chiaro.");

                WindowsTheme.AppsUseLightThemeReader = delegate { return (int?)null; };
                Assert.Equal(AppTheme.Light, WindowsTheme.Detect(),
                    "Lettura senza valore: deve valere il tema chiaro.");

                WindowsTheme.AppsUseLightThemeReader = delegate
                {
                    throw new IOException("lettura del tema non disponibile");
                };
                Assert.Equal(AppTheme.Light, WindowsTheme.Detect(),
                    "Errore di lettura: deve valere il tema chiaro.");

                WindowsTheme.AppsUseLightThemeReader = null;
                Assert.Equal(AppTheme.Light, WindowsTheme.Detect(),
                    "Seam assente: deve valere il tema chiaro.");
            }
            finally
            {
                WindowsTheme.AppsUseLightThemeReader = original;
            }
        }

        #endregion

        #region Palette

        [Test]
        public void LightPalette_ReplicatesTheClassicSystemLook()
        {
            ThemePalette palette = ThemePalette.LightPalette();
            Assert.Equal(AppTheme.Light, palette.Theme, "Il tema chiaro deve dichiararsi Light.");
            Assert.Equal(SystemColors.Control.ToArgb(), palette.Surface.ToArgb(),
                "Lo sfondo chiaro deve essere il colore di sistema della finestra.");
            Assert.Equal(SystemColors.ControlText.ToArgb(), palette.Text.ToArgb(),
                "Il testo chiaro deve essere il testo di sistema.");
            Assert.Equal(Color.DimGray.ToArgb(), palette.TextSecondary.ToArgb(),
                "Il testo secondario chiaro deve restare DimGray, come in 0.3.0.");
            Assert.Equal(Color.Gray.ToArgb(), palette.TextMuted.ToArgb(),
                "Il testo smorzato chiaro deve restare Gray, come in 0.3.0.");
            Assert.Equal(SystemColors.Window.ToArgb(), palette.GridBack.ToArgb(),
                "Lo sfondo della tabella chiara deve essere quello della finestra di sistema.");
            Assert.Equal(SystemColors.WindowText.ToArgb(), palette.GridText.ToArgb(),
                "Il testo della tabella chiara deve essere quello di sistema.");
            Assert.Equal(Color.FromArgb(240, 240, 240).ToArgb(), palette.GridHeaderBack.ToArgb(),
                "L'intestazione chiara deve restare il grigio storico 240,240,240.");
            Assert.Equal(SystemColors.ControlText.ToArgb(), palette.GridHeaderText.ToArgb(),
                "Il testo delle intestazioni chiare deve essere di sistema.");
            Assert.True(palette.NativeButtons,
                "Nel tema chiaro i pulsanti devono restare al rendering nativo di Windows.");
        }

        [Test]
        public void DarkPalette_IsFixedAnthraciteAndStable()
        {
            ThemePalette palette = ThemePalette.DarkPalette();
            Assert.Equal(AppTheme.Dark, palette.Theme, "Il tema scuro deve dichiararsi Dark.");
            Assert.False(palette.NativeButtons,
                "Nel tema scuro i pulsanti devono usare i colori della palette.");
            Assert.Equal(Color.FromArgb(32, 32, 36).ToArgb(), palette.Surface.ToArgb(),
                "Lo sfondo scuro deve essere l'antracite fissato.");
            Assert.Equal(Color.FromArgb(236, 236, 240).ToArgb(), palette.Text.ToArgb(),
                "Il testo scuro deve essere il chiaro fissato.");
            Assert.Equal(Color.FromArgb(42, 42, 48).ToArgb(), palette.GridBack.ToArgb(),
                "Lo sfondo scuro della tabella deve essere fisso.");
            Assert.Equal(Color.FromArgb(56, 56, 64).ToArgb(), palette.GridHeaderBack.ToArgb(),
                "L'intestazione scura deve essere fissa.");

            ThemePalette again = ThemePalette.For(AppTheme.Dark);
            Assert.Equal(palette.Surface.ToArgb(), again.Surface.ToArgb(),
                "Il tema scuro non deve dipendere dalle impostazioni grafiche del sistema.");
            Assert.Equal(palette.GridBack.ToArgb(), again.GridBack.ToArgb(),
                "Il tema scuro deve essere stabile a ogni ricostruzione.");
        }

        [Test]
        public void Palettes_KeepReadableContrastInBothThemes()
        {
            ThemePalette[] palettes = new ThemePalette[]
            {
                ThemePalette.LightPalette(),
                ThemePalette.DarkPalette()
            };

            for (int i = 0; i < palettes.Length; i++)
            {
                ThemePalette palette = palettes[i];
                string label = "Tema " + palette.Theme + ": ";

                Assert.True(Contrast(palette.Text, palette.Surface) >= 100,
                    label + "il testo principale e' troppo vicino allo sfondo.");
                Assert.True(Contrast(palette.TextSecondary, palette.Surface) >= 100,
                    label + "il testo secondario e' troppo vicino allo sfondo.");
                Assert.True(Contrast(palette.TextMuted, palette.Surface) >= 100,
                    label + "il testo smorzato e' troppo vicino allo sfondo.");
                Assert.True(Contrast(palette.GridText, palette.GridBack) >= 100,
                    label + "il testo della tabella e' troppo vicino allo sfondo.");
                Assert.True(Contrast(palette.GridHeaderText, palette.GridHeaderBack) >= 100,
                    label + "le intestazioni della tabella hanno contrasto insufficiente.");
                Assert.True(Contrast(palette.ButtonText, palette.ButtonBack) >= 100,
                    label + "il testo dei pulsanti ha contrasto insufficiente.");
            }

            Assert.NotEqual(
                ThemePalette.LightPalette().Surface.ToArgb(),
                ThemePalette.DarkPalette().Surface.ToArgb(),
                "I due temi devono differire sullo sfondo.");
        }

        private static int Contrast(Color first, Color second)
        {
            int red = first.R - second.R;
            int green = first.G - second.G;
            int blue = first.B - second.B;
            if (red < 0) red = -red;
            if (green < 0) green = -green;
            if (blue < 0) blue = -blue;
            return (red + green + blue) / 3;
        }

        #endregion

        #region Applicazione alla finestra

        [Test]
        public void MainForm_AppliesAnInjectedDarkThemeAndRevertsToLight()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Dark; }));
                    Invoke(form, "ApplyTheme", null);

                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Il tema interno deve diventare scuro.");
                    ThemePalette dark = (ThemePalette)GetFieldValue(form, "_palette");
                    Assert.Equal(AppTheme.Dark, dark.Theme, "La palette attiva deve essere quella scura.");

                    Assert.Equal(dark.Surface.ToArgb(), form.BackColor.ToArgb(),
                        "Lo sfondo della finestra deve usare la palette scura.");
                    Assert.Equal(dark.Text.ToArgb(), form.ForeColor.ToArgb(),
                        "Il testo della finestra deve usare la palette scura.");
                    Assert.Equal(dark.Text.ToArgb(), GetField<Label>(form, "_titleLabel").ForeColor.ToArgb(),
                        "Il titolo deve usare il testo della palette scura.");
                    Assert.Equal(dark.Text.ToArgb(), GetField<Label>(form, "_statusLabel").ForeColor.ToArgb(),
                        "Lo stato deve usare il testo della palette scura.");
                    Assert.Equal(dark.Text.ToArgb(), GetField<Label>(form, "_summaryLabel").ForeColor.ToArgb(),
                        "Il riepilogo deve usare il testo della palette scura.");
                    Assert.Equal(dark.TextSecondary.ToArgb(), GetField<Label>(form, "_lblSelection").ForeColor.ToArgb(),
                        "La selezione deve usare il testo secondario della palette scura.");
                    Assert.Equal(dark.TextSecondary.ToArgb(), GetField<Label>(form, "_lblThreshold").ForeColor.ToArgb(),
                        "La soglia deve usare il testo secondario della palette scura.");
                    Assert.Equal(dark.TextSecondary.ToArgb(), GetField<Label>(form, "_footnoteLabel").ForeColor.ToArgb(),
                        "La nota a piedi deve usare il testo secondario della palette scura.");
                    Assert.Equal(dark.TextMuted.ToArgb(), GetField<Label>(form, "_reportLabel").ForeColor.ToArgb(),
                        "L'etichetta del report deve usare il testo smorzato della palette scura.");

                    DataGridView grid = GetField<DataGridView>(form, "_grid");
                    Assert.Equal(dark.GridBack.ToArgb(), grid.BackgroundColor.ToArgb(),
                        "Lo sfondo della tabella deve usare la palette scura.");
                    Assert.Equal(dark.GridBack.ToArgb(), grid.DefaultCellStyle.BackColor.ToArgb(),
                        "Le celle della tabella devono usare la palette scura.");
                    Assert.Equal(dark.GridText.ToArgb(), grid.DefaultCellStyle.ForeColor.ToArgb(),
                        "Il testo delle celle deve usare la palette scura.");
                    Assert.Equal(dark.GridHeaderBack.ToArgb(), grid.ColumnHeadersDefaultCellStyle.BackColor.ToArgb(),
                        "L'intestazione della tabella deve usare la palette scura.");
                    Assert.Equal(dark.GridHeaderText.ToArgb(), grid.ColumnHeadersDefaultCellStyle.ForeColor.ToArgb(),
                        "Il testo dell'intestazione deve usare la palette scura.");

                    Button analyze = GetField<Button>(form, "_btnAnalyze");
                    Assert.False(analyze.UseVisualStyleBackColor,
                        "Con tema scuro il pulsante non deve usare il rendering nativo.");
                    Assert.Equal(dark.ButtonBack.ToArgb(), analyze.BackColor.ToArgb(),
                        "Il pulsante deve usare lo sfondo della palette scura.");
                    Button info = GetField<Button>(form, "_btnInfo");
                    Assert.Equal(dark.ButtonBack.ToArgb(), info.BackColor.ToArgb(),
                        "Anche il pulsante di servizio deve usare la palette scura.");

                    ContextMenuStrip menu = GetField<ContextMenuStrip>(form, "_settingsMenu");
                    Assert.Equal(dark.MenuBack.ToArgb(), menu.BackColor.ToArgb(),
                        "Il menu deve usare lo sfondo della palette scura.");
                    Assert.Equal(dark.MenuText.ToArgb(), menu.ForeColor.ToArgb(),
                        "Il menu deve usare il testo della palette scura.");

                    // Ritorno al chiaro: nessun residuo dello scuro.
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Light; }));
                    Invoke(form, "ApplyTheme", null);

                    Assert.Equal(AppTheme.Light, GetFieldValue(form, "_currentTheme"),
                        "Il tema interno deve tornare chiaro.");
                    Assert.Equal(SystemColors.Control.ToArgb(), form.BackColor.ToArgb(),
                        "Lo sfondo deve tornare al colore di sistema.");
                    Assert.True(analyze.UseVisualStyleBackColor,
                        "I pulsanti devono tornare al rendering nativo di Windows.");
                    Assert.Equal(SystemColors.Window.ToArgb(), grid.BackgroundColor.ToArgb(),
                        "Lo sfondo della tabella deve tornare al colore di sistema.");
                    Assert.Equal(Color.DimGray.ToArgb(), GetField<Label>(form, "_footnoteLabel").ForeColor.ToArgb(),
                        "Il testo secondario deve tornare a DimGray.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void MainForm_UsesTheDetectedThemeByDefault()
        {
            ThemePalette expected = ThemePalette.For(WindowsTheme.Detect());

            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    Assert.Equal(expected.Theme, GetFieldValue(form, "_currentTheme"),
                        "Il tema iniziale deve corrispondere a quello rilevato.");
                    Assert.Equal(expected.Surface.ToArgb(), form.BackColor.ToArgb(),
                        "Lo sfondo iniziale deve corrispondere al tema rilevato.");
                    Assert.Equal(expected.Text.ToArgb(), form.ForeColor.ToArgb(),
                        "Il testo iniziale deve corrispondere al tema rilevato.");
                    Assert.Equal(expected.TextSecondary.ToArgb(),
                        GetField<Label>(form, "_footnoteLabel").ForeColor.ToArgb(),
                        "La nota a piedi deve corrispondere al tema rilevato.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void MainForm_DetectFailure_FallsBackToLight()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_detectTheme", new Func<AppTheme>(delegate
                    {
                        throw new IOException("lettura del tema non disponibile");
                    }));
                    Invoke(form, "ApplyTheme", null);

                    Assert.Equal(AppTheme.Light, GetFieldValue(form, "_currentTheme"),
                        "Un errore di lettura deve ricadere sul tema chiaro.");
                    Assert.Equal(SystemColors.Control.ToArgb(), form.BackColor.ToArgb(),
                        "Con lettura fallita lo sfondo deve essere di sistema.");
                    Assert.True(GetField<Button>(form, "_btnAnalyze").UseVisualStyleBackColor,
                        "Con lettura fallita i pulsanti devono tornare nativi.");
                    Assert.Equal(SystemColors.Window.ToArgb(),
                        GetField<DataGridView>(form, "_grid").BackgroundColor.ToArgb(),
                        "Con lettura fallita la tabella deve tornare al colore di sistema.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void MainForm_ReportLabelColorsComeFromThePalette()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Dark; }));
                    Invoke(form, "ApplyTheme", null);
                    ThemePalette dark = (ThemePalette)GetFieldValue(form, "_palette");
                    Label report = GetField<Label>(form, "_reportLabel");

                    Assert.Equal(dark.TextMuted.ToArgb(), report.ForeColor.ToArgb(),
                        "Con nessun report il colore deve venire dalla palette scura.");

                    SetField(form, "_keepReports", false);
                    Invoke(form, "UpdateReportLabel", null);
                    Assert.Contains("conservazione disattivata", report.Text,
                        "Il testo deve dichiarare la conservazione disattivata.");
                    Assert.Equal(dark.TextMuted.ToArgb(), report.ForeColor.ToArgb(),
                        "Anche con conservazione spenta il colore deve venire dalla palette.");

                    string path = Path.Combine(Path.GetTempPath(),
                        "cleanslate-theme-" + Guid.NewGuid().ToString("N") + ".txt");
                    try
                    {
                        File.WriteAllText(path, "report");
                        SetField(form, "_lastReportPath", path);
                        Invoke(form, "UpdateReportLabel", null);
                        Assert.Equal(dark.TextSecondary.ToArgb(), report.ForeColor.ToArgb(),
                            "Con un report esistente il colore secondario deve venire dalla palette.");
                    }
                    finally
                    {
                        try { if (File.Exists(path)) File.Delete(path); }
                        catch (Exception) { }
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        [Test]
        public void MainForm_InfoDialogUsesThePalette()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    SetField(form, "_detectTheme",
                        new Func<AppTheme>(delegate { return AppTheme.Dark; }));
                    Invoke(form, "ApplyTheme", null);
                    ThemePalette dark = (ThemePalette)GetFieldValue(form, "_palette");

                    Form dialog = (Form)Invoke(form, "BuildInfoForm", null);
                    using (dialog)
                    {
                        Assert.Equal(dark.Surface.ToArgb(), dialog.BackColor.ToArgb(),
                            "Il dialogo deve usare lo sfondo della palette scura.");
                        Assert.Equal(dark.Text.ToArgb(), dialog.ForeColor.ToArgb(),
                            "Il dialogo deve usare il testo della palette scura.");

                        Control[] bodies = dialog.Controls.Find("lblInfoBody", true);
                        Assert.Equal(1, bodies.Length, "Il corpo del testo deve esistere.");
                        Assert.Equal(dark.Text.ToArgb(), bodies[0].ForeColor.ToArgb(),
                            "Il corpo del testo deve usare la palette scura.");

                        Control[] buttons = dialog.Controls.Find("btnInfoOk", true);
                        Assert.Equal(1, buttons.Length, "Il pulsante OK deve esistere.");
                        Button ok = buttons[0] as Button;
                        Assert.NotNull(ok, "Il pulsante OK deve essere un pulsante.");
                        Assert.False(ok.UseVisualStyleBackColor,
                            "Con tema scuro il pulsante OK non deve usare il rendering nativo.");
                        Assert.Equal(dark.ButtonBack.ToArgb(), ok.BackColor.ToArgb(),
                            "Il pulsante OK deve usare la palette scura.");
                    }
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        #endregion

        #region Aggiornamento in tempo reale

        [Test]
        public void WndProc_ColorSchemeMessages_RefreshTheTheme()
        {
            Exception failure = null;
            RunInSta(delegate
            {
                using (MainForm form = new MainForm())
                {
                    int reads = 0;
                    bool dark = false;
                    SetField(form, "_detectTheme", new Func<AppTheme>(delegate
                    {
                        reads++;
                        return dark ? AppTheme.Dark : AppTheme.Light;
                    }));
                    Invoke(form, "ApplyTheme", null);
                    Assert.Equal(1, reads, "La prima applicazione deve leggere il tema una volta.");

                    // Finestra creata per ricevere i messaggi (mai mostrata all'utente).
                    // La creazione dell'handle puo' generare messaggi di sistema, quindi
                    // ogni asserzione misura il delta attorno al proprio invio.
                    IntPtr window = form.Handle;
                    Assert.NotEqual(IntPtr.Zero, window,
                        "La finestra deve essere creata per ricevere i messaggi di sistema.");

                    // 1. Cambio al tema scuro via WM_THEMECHANGED.
                    dark = true;
                    int beforeThemeChanged = reads;
                    SendWndProc(form, 0x031A, IntPtr.Zero, IntPtr.Zero);
                    Assert.Equal(beforeThemeChanged + 1, reads,
                        "WM_THEMECHANGED deve rileggere il tema una volta.");
                    Assert.Equal(AppTheme.Dark, GetFieldValue(form, "_currentTheme"),
                        "Il tema interno deve diventare scuro.");
                    Assert.Equal(ThemePalette.DarkPalette().Surface.ToArgb(), form.BackColor.ToArgb(),
                        "La palette scura deve essere applicata alla finestra.");

                    // 2. WM_SETTINGCHANGE su un'area non pertinente: nessuna lettura.
                    int beforeCursors = reads;
                    SendWndProcWithString(form, 0x001A, "Cursors");
                    Assert.Equal(beforeCursors, reads,
                        "Un'area di sistema non pertinente non deve far rileggere il tema.");

                    // 3. Cambio schema colori con tema invariato: lettura, nessun riapplico.
                    int beforeImmersive = reads;
                    object paletteBefore = GetFieldValue(form, "_palette");
                    SendWndProcWithString(form, 0x001A, "ImmersiveColorSet");
                    Assert.Equal(beforeImmersive + 1, reads,
                        "Il cambio schema colori deve rileggere il tema una volta.");
                    Assert.True(ReferenceEquals(paletteBefore, GetFieldValue(form, "_palette")),
                        "Con il tema invariato la palette non deve essere ricostruita.");

                    // 4. Ritorno al chiaro.
                    int beforeReturn = reads;
                    dark = false;
                    SendWndProcWithString(form, 0x001A, "ImmersiveColorSet");
                    Assert.Equal(beforeReturn + 1, reads,
                        "Il ritorno al chiaro deve rileggere il tema una volta.");
                    Assert.Equal(AppTheme.Light, GetFieldValue(form, "_currentTheme"),
                        "Il tema interno deve tornare chiaro.");
                    Assert.Equal(SystemColors.Control.ToArgb(), form.BackColor.ToArgb(),
                        "I colori di sistema devono essere ripristinati.");
                    Assert.True(GetField<Button>(form, "_btnAnalyze").UseVisualStyleBackColor,
                        "I pulsanti devono tornare al rendering nativo.");
                }
            }, out failure);
            if (failure != null) throw failure;
        }

        private static void SendWndProc(MainForm form, int message, IntPtr wParam, IntPtr lParam)
        {
            InvokeWndProc(form, Message.Create(form.Handle, message, wParam, lParam));
        }

        private static void SendWndProcWithString(MainForm form, int message, string text)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(text);
            try
            {
                InvokeWndProc(form, Message.Create(form.Handle, message, IntPtr.Zero, pointer));
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static void InvokeWndProc(MainForm form, Message message)
        {
            MethodInfo wndProc = typeof(Control).GetMethod("WndProc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (wndProc == null) throw new MissingMethodException("Control.WndProc");
            object boxed = message;
            try
            {
                wndProc.Invoke(form, new object[] { boxed });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        #endregion

        #region Centralizzazione dei colori

        [Test]
        public void AppSources_HardcodeNoColorsOutsideThePalette()
        {
            string root = SourceRoot();
            if (string.IsNullOrEmpty(root))
                throw new AssertException("Impossibile individuare la cartella src\\CleanSlate.");

            string palettePath = Path.Combine(root, "Core", "ThemePalette.cs");
            Assert.True(File.Exists(palettePath),
                "La palette cromatica centralizzata deve esistere.");

            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(files[i], palettePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = files[i].Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string text = File.ReadAllText(files[i]);
                Assert.DoesNotContain("Color.", text,
                    "Assegnazione colore fuori dalla palette: " + relative);
                Assert.DoesNotContain("SystemColors.", text,
                    "Assegnazione colore di sistema fuori dalla palette: " + relative);
            }

            string palette = File.ReadAllText(palettePath);
            Assert.Contains("SystemColors.", palette,
                "Il tema chiaro deve vivere nella palette centralizzata.");
            Assert.Contains("Color.FromArgb", palette,
                "Il tema scuro deve vivere nella palette centralizzata.");

            string detector = File.ReadAllText(Path.Combine(root, "Core", "WindowsTheme.cs"));
            Assert.Contains("AppsUseLightTheme", detector,
                "Il rilevamento deve leggere l'impostazione di tema di Windows.");
        }

        private static string SourceRoot()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("CLEANSLATE_SRC");
            if (!string.IsNullOrEmpty(fromEnvironment) && Directory.Exists(fromEnvironment))
                return fromEnvironment;

            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(directory); i++)
            {
                string candidate = Path.Combine(directory, "src", "CleanSlate");
                if (Directory.Exists(candidate)) return candidate;
                directory = Path.GetDirectoryName(directory);
            }
            return null;
        }

        #endregion

        #region Utilità

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static object GetFieldValue(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static object Invoke(object target, string name, object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
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
