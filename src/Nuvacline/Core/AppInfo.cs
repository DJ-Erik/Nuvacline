// Nuvacline - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Reflection;

namespace Nuvacline.Core
{
    /// <summary>Informazioni di versione del prodotto, lette dagli attributi dell'assembly.</summary>
    public static class AppInfo
    {
        public const string ProductName = "Nuvacline";

        public static readonly string VersionText = ResolveVersion();

        private static string ResolveVersion()
        {
            try
            {
                Assembly entry = typeof(AppInfo).Assembly;
                object[] attributes = entry.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (attributes != null && attributes.Length > 0)
                {
                    AssemblyInformationalVersionAttribute info =
                        (AssemblyInformationalVersionAttribute)attributes[0];
                    if (!string.IsNullOrEmpty(info.InformationalVersion)) return info.InformationalVersion;
                }
                return entry.GetName().Version.ToString(3);
            }
            catch (Exception)
            {
                return "0.5.0";
            }
        }
    }
}
