using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CleanSlate.Core;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Verifiche statiche di privacy e sicurezza sulle sorgenti e sull'assembly distribuito.
    ///
    /// Non ci si limita a dire "non ho trovato chiamate HTTP": si controlla che nelle
    /// sorgenti dell'applicazione non compaiano API di rete, di registro, di telemetria,
    /// di aggiornamento automatico né comandi shell distruttivi, e che l'assembly
    /// non referenzi alcuna assembly di rete.
    /// </summary>
    public sealed class PrivacyTests
    {
        private static readonly string[] ForbiddenNetworkTokens = new string[]
        {
            "HttpClient", "HttpWebRequest", "WebRequest", "WebClient", "Socket", "TcpClient",
            "UdpClient", "HttpListener", "SmtpClient", "FtpWebRequest", "DownloadFile",
            "DownloadString", "UploadFile", "UploadString", "UploadString", "ServicePoint",
            "NetworkInformation", "System.Net", "WebSocket", "HttpClientHandler",
            "WebRequestHandler", "Dns.GetHost", "Ping.", "RestClient", "Flurl", "WebRequest.Create"
        };

        private static readonly string[] ForbiddenRegistryTokens = new string[]
        {
            "Microsoft.Win32", "RegistryKey", "Registry.CurrentUser", "Registry.LocalMachine",
            "CreateSubKey", "DeleteSubKey", "OpenSubKey", "RegistryValueKind", "RegSetValue"
        };

        private static readonly string[] ForbiddenPersistenceTokens = new string[]
        {
            "CreateService", "OpenService", "ChangeServiceConfig", "schtasks", "RegisterTaskDefinition",
            "StartupInfo", "CurrentVersion\\Run", "RunOnce", "Environment.GetCommandLineArgs"
        };

        private static readonly string[] ForbiddenTelemetryTokens = new string[]
        {
            "Telemetry", "TelemetryClient", "Analytics", "AppInsights", "ApplicationInsights",
            "TrackEvent", "TrackPageView", "Sentry", "GoogleAnalytics", "CheckForUpdate",
            "AutoUpdate", "DownloadUpdate", "ProductUpdate", "crashpad", "Bugsnag"
        };

        private static readonly string[] ForbiddenShellTokens = new string[]
        {
            "cmd.exe", "powershell", "Remove-Item", "Format-Volume", "del /f", "rmdir /s",
            "taskkill", "Stop-Process", "reg delete", "wmic process", "Start-Process",
            "ProcessStartInfo(\"cmd", "ProcessStartInfo(\"powershell"
        };

        private static readonly string[] AllowedAssemblyReferences = new string[]
        {
            "mscorlib", "System", "System.Core", "System.Drawing", "System.Windows.Forms"
        };

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

        /// <summary>
        /// Legge tutte le sorgenti dell'applicazione eliminando le righe di commento:
        /// le verifiche riguardano il codice eseguibile, non le note nei commenti.
        /// I nomi dei file sono riportati in forma relativa per non iniettare
        /// percorsi assoluti nel testo analizzato.
        /// </summary>
        private static string AllSourceText(out int fileCount)
        {
            string root = SourceRoot();
            if (string.IsNullOrEmpty(root))
                throw new AssertException(
                    "Impossibile individuare la cartella src\\CleanSlate: impostare la variabile CLEANSLATE_SRC.");

            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            fileCount = files.Length;
            if (fileCount == 0)
                throw new AssertException("Nessuna sorgente trovata.");

            string normalizedRoot = PathSafety.Normalize(root);
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(normalizedRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                builder.AppendLine("// FILE: " + relative);

                string[] lines = File.ReadAllLines(files[i]);
                for (int j = 0; j < lines.Length; j++)
                {
                    string trimmed = lines[j].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                    builder.AppendLine(lines[j]);
                }
            }
            return builder.ToString();
        }

        private static void AssertNoTokens(string[] tokens, string source, string message)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                int index = source.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;

                int line = 1;
                for (int j = 0; j < index; j++) if (source[j] == '\n') line++;
                throw new AssertException(message + " | token: " + tokens[i] + " | riga ~" + line);
            }
        }

        [Test]
        public void Sources_ContainNoNetworkApiUsage()
        {
            int files;
            string source = AllSourceText(out files);
            AssertNoTokens(ForbiddenNetworkTokens, source, "Le sorgenti contengono API di rete");
        }

        [Test]
        public void Sources_ContainNoRegistryApiUsage()
        {
            int files;
            string source = AllSourceText(out files);
            AssertNoTokens(ForbiddenRegistryTokens, source, "Le sorgenti contengono API di registro");
        }

        [Test]
        public void Sources_ContainNoAutoStartOrServicePersistence()
        {
            int files;
            string source = AllSourceText(out files);
            AssertNoTokens(ForbiddenPersistenceTokens, source, "Le sorgenti contengono meccanismi di persistenza");
        }

        [Test]
        public void Sources_ContainNoTelemetryOrAutoUpdate()
        {
            int files;
            string source = AllSourceText(out files);
            AssertNoTokens(ForbiddenTelemetryTokens, source, "Le sorgenti contengono telemetria o aggiornamento automatico");
        }

        [Test]
        public void Sources_ContainNoDestructiveShellCommands()
        {
            int files;
            string source = AllSourceText(out files);
            AssertNoTokens(ForbiddenShellTokens, source, "Le sorgenti contengono comandi shell distruttivi");
        }

        [Test]
        public void Sources_ContainNoHardcodedUserProfilePaths()
        {
            int files;
            string source = AllSourceText(out files);
            string[] tokens = new string[] { "C:\\Users\\", "\\\\Users\\", "AppData\\Local\\Temp\\", "%USERPROFILE%" };
            AssertNoTokens(tokens, source, "Le sorgenti contengono percorsi profilo hardcoded");
        }

        [Test]
        public void AppAssembly_ReferencesOnlyFrameworkAssemblies()
        {
            Assembly assembly = typeof(TempFolderScanner).Assembly;
            AssemblyName[] references = assembly.GetReferencedAssemblies();
            Assert.True(references.Length > 0, "L'assembly deve avere riferimenti.");

            for (int i = 0; i < references.Length; i++)
            {
                string name = references[i].Name;
                bool allowed = false;
                for (int j = 0; j < AllowedAssemblyReferences.Length; j++)
                {
                    if (string.Equals(name, AllowedAssemblyReferences[j], StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }
                Assert.True(allowed, "Riferimento assembly non previsto: " + name);
            }
        }

        [Test]
        public void AppAssembly_ReferencesNoNetworkAssembly()
        {
            Assembly assembly = typeof(TempFolderScanner).Assembly;
            AssemblyName[] references = assembly.GetReferencedAssemblies();
            for (int i = 0; i < references.Length; i++)
            {
                string name = references[i].Name;
                Assert.False(name.StartsWith("System.Net", StringComparison.OrdinalIgnoreCase),
                    "L'assembly referenza un'assembly di rete: " + name);
                Assert.False(string.Equals(name, "System.ServiceModel", StringComparison.OrdinalIgnoreCase),
                    "L'assembly referenza WCF: " + name);
                Assert.False(string.Equals(name, "System.Web", StringComparison.OrdinalIgnoreCase),
                    "L'assembly referenza System.Web: " + name);
            }
        }

        [Test]
        public void Manifest_RequestsNoElevation()
        {
            string root = SourceRoot();
            if (string.IsNullOrEmpty(root))
                throw new AssertException("Impossibile individuare la cartella delle sorgenti.");

            string manifestPath = Path.Combine(root, "app.manifest");
            Assert.True(File.Exists(manifestPath), "Il manifest dell'applicazione deve esistere.");

            string manifest = File.ReadAllText(manifestPath);
            Assert.Contains("level=\"asInvoker\"", manifest, "Il manifest deve richiedere esecuzione non elevata.");
            Assert.False(manifest.Contains("requireAdministrator"),
                "Il manifest non deve richiedere privilegi di amministratore.");
            Assert.False(manifest.Contains("highestAvailable"),
                "Il manifest non deve richiedere privilegi elevati.");
        }

        [Test]
        public void AppDirectory_ContainsNoNetworkOrTelemetryConfigurationFiles()
        {
            string root = SourceRoot();
            if (string.IsNullOrEmpty(root))
                throw new AssertException("Impossibile individuare la cartella delle sorgenti.");

            string[] forbidden = new string[] { "appsettings.json", "telemetry.json", "analytics.json" };
            for (int i = 0; i < forbidden.Length; i++)
            {
                Assert.False(File.Exists(Path.Combine(root, forbidden[i])),
                    "Non deve esistere il file di configurazione " + forbidden[i] + ".");
            }
        }

        [Test]
        public void SourceFiles_AreAllUnderTheApplicationFolder()
        {
            string root = SourceRoot();
            if (string.IsNullOrEmpty(root))
                throw new AssertException("Impossibile individuare la cartella delle sorgenti.");

            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Assert.True(files.Length >= 8, "Numero di sorgenti inatteso: " + files.Length);

            string normalizedRoot = PathSafety.Normalize(root);
            for (int i = 0; i < files.Length; i++)
                Assert.True(PathSafety.IsInsideRoot(normalizedRoot, files[i]),
                    "Sorgente fuori dalla cartella dell'applicazione: " + files[i]);
        }
    }
}
