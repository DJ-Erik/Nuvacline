// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System.Drawing;
using System.Windows.Forms;

namespace CleanSlate.Core
{
    /// <summary>
    /// Palette cromatica centralizzata: ogni colore usato dall'applicazione e'
    /// definito qui, una sola volta per tema, e applicato dalla finestra
    /// principale tramite ApplyTheme. Nessun colore e' assegnato altrove.
    ///
    /// Il tema chiaro riproduce esattamente l'aspetto storico (colori di sistema),
    /// quindi zero regressione rispetto alle versioni precedenti. Il tema scuro e'
    /// una palette antracite fissa, indipendente dalle impostazioni grafiche del
    /// sistema, cosi' che i test possano verificarla in modo deterministico su
    /// qualunque macchina.
    /// </summary>
    public sealed class ThemePalette
    {
        public readonly AppTheme Theme;
        public readonly Color Surface;
        public readonly Color Text;
        public readonly Color TextSecondary;
        public readonly Color TextMuted;
        public readonly Color GridBack;
        public readonly Color GridText;
        public readonly Color GridHeaderBack;
        public readonly Color GridHeaderText;
        public readonly Color ButtonBack;
        public readonly Color ButtonText;
        public readonly bool NativeButtons;
        public readonly Color MenuBack;
        public readonly Color MenuText;
        public readonly Color AccentSuccess;
        public readonly Color AccentWarning;
        public readonly Color AccentError;

        private ThemePalette(AppTheme theme, Color surface, Color text,
            Color textSecondary, Color textMuted, Color gridBack, Color gridText,
            Color gridHeaderBack, Color gridHeaderText, Color buttonBack,
            Color buttonText, bool nativeButtons, Color menuBack, Color menuText,
            Color accentSuccess, Color accentWarning, Color accentError)
        {
            Theme = theme;
            Surface = surface;
            Text = text;
            TextSecondary = textSecondary;
            TextMuted = textMuted;
            GridBack = gridBack;
            GridText = gridText;
            GridHeaderBack = gridHeaderBack;
            GridHeaderText = gridHeaderText;
            ButtonBack = buttonBack;
            ButtonText = buttonText;
            NativeButtons = nativeButtons;
            MenuBack = menuBack;
            MenuText = menuText;
            AccentSuccess = accentSuccess;
            AccentWarning = accentWarning;
            AccentError = accentError;
        }

        /// <summary>Palette del tema richiesto: il tema scuro e' fisso, il chiaro di sistema.</summary>
        public static ThemePalette For(AppTheme theme)
        {
            if (theme == AppTheme.Dark) return DarkPalette();
            return LightPalette();
        }

        /// <summary>
        /// Tema chiaro: valori identici all'interfaccia di tutte le versioni
        /// precedenti (colori di sistema e grigio per i testi secondari),
        /// con pulsanti lasciati al rendering nativo di Windows.
        /// </summary>
        public static ThemePalette LightPalette()
        {
            return new ThemePalette(
                AppTheme.Light,
                SystemColors.Control,
                SystemColors.ControlText,
                Color.DimGray,
                Color.Gray,
                SystemColors.Window,
                SystemColors.WindowText,
                Color.FromArgb(240, 240, 240),
                SystemColors.ControlText,
                SystemColors.Control,
                SystemColors.ControlText,
                true,
                SystemColors.Menu,
                SystemColors.MenuText,
                Color.FromArgb(0, 110, 0),
                Color.FromArgb(160, 100, 0),
                Color.FromArgb(170, 30, 30));
        }

        /// <summary>
        /// Tema scuro: antracite fisso ad alto contrasto, non derivato dal sistema.
        /// </summary>
        public static ThemePalette DarkPalette()
        {
            return new ThemePalette(
                AppTheme.Dark,
                Color.FromArgb(32, 32, 36),
                Color.FromArgb(236, 236, 240),
                Color.FromArgb(172, 172, 180),
                Color.FromArgb(140, 140, 148),
                Color.FromArgb(42, 42, 48),
                Color.FromArgb(236, 236, 240),
                Color.FromArgb(56, 56, 64),
                Color.FromArgb(236, 236, 240),
                Color.FromArgb(60, 60, 68),
                Color.FromArgb(236, 236, 240),
                false,
                Color.FromArgb(48, 48, 54),
                Color.FromArgb(236, 236, 240),
                Color.FromArgb(110, 210, 130),
                Color.FromArgb(235, 185, 90),
                Color.FromArgb(235, 110, 110));
        }
    }
}
