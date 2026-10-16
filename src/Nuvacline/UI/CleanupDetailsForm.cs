// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System.Text;
using Nuvacline.Core;

namespace Nuvacline.UI
{
    /// <summary>
    /// Testo completo dei dettagli dell'ultima pulizia. Usato dal pannello
    /// integrato «Mostra dettagli» della finestra principale: la versione
    /// 0.4.5 ha sostituito la finestra modale con un pannello richiudibile.
    /// Nessuna azione, nessuna rete: solo costruzione di testo.
    /// </summary>
    public static class CleanupDetailsForm
    {
        /// <summary>
        /// Testo completo mostrato nel pannello dei dettagli: il riepilogo
        /// (identico al messaggio storico della 0.3.6/0.4.0, quindi la sezione
        /// cartelle compare solo se la fase è stata davvero eseguita) seguito
        /// dall'elenco esteso di tutti gli esiti con nome esito e messaggio.
        /// </summary>
        public static string BuildDetailsText(
            CleanResult cleanResult,
            EmptyCleanResult emptyFolders)
        {
            StringBuilder sb = new StringBuilder(4096);
            sb.Append(MainForm.BuildCleanSummaryText(cleanResult, emptyFolders));

            AppendFileItems(sb, cleanResult);
            AppendFolderItems(sb, emptyFolders);
            return sb.ToString();
        }

        /// <summary>
        /// Elenco di tutti gli esiti dei file con nome esito e messaggio.
        /// Nessuna scorciatoia: anche un solo elemento comparisce.
        /// </summary>
        private static void AppendFileItems(StringBuilder sb, CleanResult cleanResult)
        {
            if (cleanResult == null || cleanResult.Items == null
                || cleanResult.Items.Count == 0) return;

            sb.Append("\r\n\r\n--- Esiti dei file ---\r\n");
            for (int i = 0; i < cleanResult.Items.Count; i++)
            {
                CleanItemResult item = cleanResult.Items[i];
                sb.Append("- ");
                sb.Append(string.IsNullOrEmpty(item.Name) ? "(senza nome)" : item.Name);
                sb.Append(": ");
                sb.Append(item.Outcome.ToString());
                if (!string.IsNullOrEmpty(item.Message))
                    sb.Append(" — ").Append(item.Message);
                sb.Append("\r\n");
            }
        }

        /// <summary>
        /// Elenco di tutti gli esiti delle cartelle vuote con nome esito e messaggio.
        /// La sezione compare solo se la fase è stata davvero eseguita.
        /// </summary>
        private static void AppendFolderItems(StringBuilder sb, EmptyCleanResult emptyFolders)
        {
            if (emptyFolders == null || emptyFolders.Items == null
                || emptyFolders.Items.Count == 0) return;

            sb.Append("\r\n\r\n--- Esiti delle cartelle vuote ---\r\n");
            for (int i = 0; i < emptyFolders.Items.Count; i++)
            {
                EmptyDirectoryItemResult item = emptyFolders.Items[i];
                sb.Append("- ");
                sb.Append(string.IsNullOrEmpty(item.Name) ? "(senza nome)" : item.Name);
                sb.Append(": ");
                sb.Append(item.Outcome.ToString());
                if (!string.IsNullOrEmpty(item.Message))
                    sb.Append(" — ").Append(item.Message);
                sb.Append("\r\n");
            }
        }
    }
}
