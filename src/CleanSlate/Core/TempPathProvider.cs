// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.IO;

namespace CleanSlate.Core
{
    /// <summary>
    /// Risultato della determinazione del percorso temporaneo autorizzato.
    /// </summary>
    public sealed class TempPathResult
    {
        public TempPathResult()
        {
            Success = false;
            Root = null;
            ErrorMessage = null;
        }

        public bool Success { get; private set; }
        public string Root { get; private set; }
        public string ErrorMessage { get; private set; }

        public static TempPathResult Ok(string root)
        {
            TempPathResult r = new TempPathResult();
            r.Success = true;
            r.Root = root;
            return r;
        }

        public static TempPathResult Fail(string errorMessage)
        {
            TempPathResult r = new TempPathResult();
            r.Success = false;
            r.ErrorMessage = errorMessage;
            return r;
        }
    }

    /// <summary>
    /// Individua la cartella dei file temporanei dell'utente corrente usando esclusivamente
    /// il percorso restituito dall'ambiente Windows. Non contiene percorsi relativi al nome
    /// dell'utente e non propone mai alternative: se il percorso non è disponibile o non è
    /// sicuro, viene segnalato un errore.
    /// </summary>
    public static class TempPathProvider
    {
        public static TempPathResult GetAuthorizedRoot()
        {
            string raw;
            try
            {
                raw = Path.GetTempPath();
            }
            catch (IOException ex)
            {
                return TempPathResult.Fail("Non è possibile determinare la cartella dei file temporanei: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return TempPathResult.Fail("Non è possibile determinare la cartella dei file temporanei: " + ex.Message);
            }

            if (string.IsNullOrEmpty(raw))
                return TempPathResult.Fail("La cartella dei file temporanei non è disponibile.");

            string normalized;
            string error;
            if (!PathSafety.ValidateCleaningRoot(raw, out normalized, out error))
                return TempPathResult.Fail(error);

            return TempPathResult.Ok(normalized);
        }
    }
}
