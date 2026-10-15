// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
namespace CleanSlate.Core
{
    /// <summary>
    /// Preferenza del tema dell'interfaccia scelta dall'utente.
    /// I valori numerici corrispondono all'ordine delle voci della
    /// combo box «Tema» della finestra principale.
    /// </summary>
    public enum ThemeChoice
    {
        /// <summary>Segue automaticamente il tema chiaro/scuro impostato in Windows.</summary>
        Automatic = 0,

        /// <summary>Forza il tema chiaro dell'applicazione.</summary>
        Light = 1,

        /// <summary>Forza il tema scuro dell'applicazione.</summary>
        Dark = 2
    }
}
