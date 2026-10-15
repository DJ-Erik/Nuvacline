// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Windows.Forms;
using CleanSlate.UI;

namespace CleanSlate
{
    /// <summary>
    /// Punto di ingresso di CleanSlate.
    /// Non registra servizi, attività pianificate, chiavi di registro né avvio automatico.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnUiThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            Application.Run(new MainForm());
        }

        private static void OnUiThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            HandleUnexpected(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleUnexpected(e.ExceptionObject as Exception);
        }

        private static void HandleUnexpected(Exception exception)
        {
            string detail = exception != null ? exception.Message : "Errore sconosciuto.";
            try
            {
                MessageBox.Show(
                    "Si è verificato un errore imprevisto: " + detail
                    + "\r\n\r\nL'operazione è stata interrotta. Nessun dato è stato trasmesso.",
                    "CleanSlate",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // Nessuna registrazione su file: in caso di errore anche la finestra di errore
                // potrebbe non essere disponibile.
            }
        }
    }
}
