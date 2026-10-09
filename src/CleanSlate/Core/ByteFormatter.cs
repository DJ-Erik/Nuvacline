using System;
using System.Globalization;

namespace CleanSlate.Core
{
    /// <summary>Formattazione leggibile delle dimensioni, con cultura italiana.</summary>
    public static class ByteFormatter
    {
        private static readonly string[] Units = new string[] { "B", "KB", "MB", "GB", "TB" };

        public static string Format(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double value = bytes;
            int unit = 0;
            while (value >= 1024.0 && unit < Units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }

            string number;
            if (unit == 0)
                number = ((long)value).ToString(CultureInfo.CurrentCulture);
            else
                number = value.ToString("N1", CultureInfo.CurrentCulture);

            return number + " " + Units[unit];
        }
    }
}
