using System;
using System.Runtime.InteropServices;

namespace CleanSlate.Core
{
    /// <summary>
    /// Rilevamento del tema chiaro/scuro impostato in Windows.
    ///
    /// Legge in sola lettura un singolo valore DWORD (AppsUseLightTheme) nella chiave
    /// personale dell'utente, tramite una chiamata P/Invoke documentata: nessuna
    /// scrittura, nessuna chiave creata, nessuna assembly aggiuntiva referenziata.
    /// Valore assente, tipo inatteso o errore qualsiasi ricadono sul tema chiaro,
    /// che resta il comportamento storico dell'applicazione.
    ///
    /// La lettura passa dal seam AppsUseLightThemeReader, sostituibile dai test:
    /// la suite non dipende mai dal tema realmente impostato sulla macchina.
    /// </summary>
    public static class WindowsTheme
    {
        public const string PersonalizeSubKey =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
        public const string AppsUseLightThemeValue = "AppsUseLightTheme";

        private const uint RrfTypeDword = 0x00000010;
        private const uint RegTypeDword = 4;
        private const int ErrorSuccess = 0;

        private static readonly IntPtr HkeyCurrentUser =
            new IntPtr(unchecked((int)0x80000001));

        /// <summary>
        /// Seam di lettura: restituisce il valore intero dell'impostazione di tema
        /// (1 = chiaro, 0 = scuro) oppure null quando non e' leggibile. Il valore
        /// predefinito legge il registro di Windows; i test lo sostituiscono.
        /// </summary>
        public static Func<int?> AppsUseLightThemeReader = ReadAppsUseLightThemeNative;

        /// <summary>Rileva il tema corrente; qualsiasi fallimento produce il tema chiaro.</summary>
        public static AppTheme Detect()
        {
            try
            {
                Func<int?> reader = AppsUseLightThemeReader;
                int? raw = reader != null ? reader() : null;
                return FromReaderValue(raw);
            }
            catch (Exception)
            {
                return AppTheme.Light;
            }
        }

        /// <summary>
        /// Converte il valore grezzo del rilevamento: 0 significa scuro,
        /// qualsiasi altro valore (o assenza) significa chiaro.
        /// </summary>
        public static AppTheme FromReaderValue(int? raw)
        {
            if (raw.HasValue && raw.Value == 0) return AppTheme.Dark;
            return AppTheme.Light;
        }

        private static int? ReadAppsUseLightThemeNative()
        {
            uint size = 4;
            uint type;
            uint value;
            int result = RegGetValue(HkeyCurrentUser, PersonalizeSubKey,
                AppsUseLightThemeValue, RrfTypeDword, out type, out value, ref size);
            if (result != ErrorSuccess) return null;
            if (type != RegTypeDword) return null;
            return unchecked((int)value);
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegGetValue(IntPtr hKey, string subKey, string value,
            uint flags, out uint type, out uint data, ref uint dataSize);
    }
}
