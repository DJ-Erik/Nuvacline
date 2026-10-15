// CleanSlate - Copyright (C) 2026 DJ-Erik
// SPDX-License-Identifier: GPL-3.0-or-later
// Testo completo della licenza: file LICENSE alla root del repository.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CleanSlate.Core;

namespace CleanSlate.Tests
{
    /// <summary>
    /// Fixture di test autoconsistente.
    ///
    /// Tutti i file vengono creati dal test stesso dentro una cartella dedicata
    /// (%LOCALAPPDATA%\CleanSlateTests\<guid>), MAI dentro la cartella temporanea reale
    /// dell'utente. Anche la "cartella esterna" usata per i tentativi di fuga si trova
    /// sempre dentro la stessa cartella di test: un eventuale errore di sicurezza non
    /// potrebbe quindi danneggiare dati dell'utente.
    ///
    /// La fixture è cancellabile e idempotente: i test possono girare ripetutamente.
    /// </summary>
    public sealed class Fixture : IDisposable
    {
        private readonly string _baseDir;

        public Fixture()
        {
            string basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CleanSlateTests");
            _baseDir = Path.Combine(basePath, Guid.NewGuid().ToString("N"));
            Root = Path.Combine(_baseDir, "root");
            Outside = Path.Combine(_baseDir, "outside");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Outside);
        }

        /// <summary>Radice autorizzata alla scansione/pulizia.</summary>
        public string Root { get; private set; }

        /// <summary>Cartella che si trova sempre FUORI dalla radice autorizzata.</summary>
        public string Outside { get; private set; }

        public string BaseDir
        {
            get { return _baseDir; }
        }

        public string CreateFile(string relativePath, int sizeBytes)
        {
            return CreateFile(relativePath, sizeBytes, TimeSpan.FromDays(2));
        }

        public string CreateFile(string relativePath, int sizeBytes, TimeSpan age)
        {
            string full = Path.Combine(Root, relativePath);
            string directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            byte[] data = new byte[sizeBytes];
            using (FileStream stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(data, 0, data.Length);
            }
            File.SetLastWriteTimeUtc(full, DateTime.UtcNow - age);
            return full;
        }

        public string CreateOutsideFile(string fileName, int sizeBytes, TimeSpan age)
        {
            string full = Path.Combine(Outside, fileName);
            byte[] data = new byte[sizeBytes];
            using (FileStream stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(data, 0, data.Length);
            }
            File.SetLastWriteTimeUtc(full, DateTime.UtcNow - age);
            return full;
        }

        public void MakeOld(string path, TimeSpan age)
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        }

        /// <summary>Crea un collegamento (junction) che non richiede privilegi amministrativi.</summary>
        public static void CreateJunction(string linkPath, string targetPath)
        {
            RunCommand("mklink", "/J \"" + linkPath + "\" \"" + targetPath + "\"", true);
        }

        public static void RemoveJunction(string linkPath)
        {
            if (!Directory.Exists(linkPath) && !File.Exists(linkPath)) return;
            RunCommand("rmdir", "\"" + linkPath + "\"", false);
        }

        /// <summary>
        /// Ripristina l'ereditarietà dei permessi su una cartella.
        ///
        /// Serve perché .NET Framework non persiste la modifica di un oggetto di sicurezza
        /// che non risulta modificato in memoria: ripristinare un oggetto "pulito" preso con
        /// GetAccessControl non ha effetto. Il ripristino tramite icacls /reset è affidabile
        /// ed è usato esclusivamente per la pulizia del fixture di test.
        /// </summary>
        public static void ResetAccessControl(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            try { RunCommand("icacls", "\"" + directory + "\" /reset /t /q", false); }
            catch (Exception) { }
        }

        /// <summary>Crea un elemento modello coerente con quelli prodotti dalla scansione.</summary>
        public static FileItem MakeItem(string fullPath)
        {
            FileInfo info = new FileInfo(fullPath);
            FileItem item = new FileItem();
            item.Name = info.Name;
            item.FullPath = info.FullName;
            item.Length = info.Exists ? info.Length : 0;
            item.LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.UtcNow;
            item.Category = ScanCategories.UserTempFiles;
            item.Selected = true;
            return item;
        }

        /// <summary>Istantane di nome, dimensione, data e attributi di tutti i file.</summary>
        public static Dictionary<string, string> Snapshot(string directory)
        {
            Dictionary<string, string> snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(directory)) return snapshot;

            string root = Path.GetFullPath(directory);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                FileInfo info = new FileInfo(file);
                string key = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                snapshot[key] = info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Attributes;
            }
            return snapshot;
        }

        public static void AssertSameSnapshot(Dictionary<string, string> expected, Dictionary<string, string> actual, string message)
        {
            Assert.Equal(expected.Count, actual.Count, message + " (conteggio diverso)");
            foreach (KeyValuePair<string, string> pair in expected)
            {
                string current;
                Assert.True(actual.TryGetValue(pair.Key, out current),
                    message + " (elemento mancante: " + pair.Key + ")");
                Assert.Equal(pair.Value, current, message + " (elemento modificato: " + pair.Key + ")");
            }
        }

        private static void RunCommand(string command, string arguments, bool throwOnError)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "cmd.exe";
            info.Arguments = "/c " + command + " " + arguments;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            using (Process process = Process.Start(info))
            {
                process.WaitForExit(20000);
                if (throwOnError && process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException(
                        "Comando '" + command + "' non riuscito (codice " + process.ExitCode + "): " + error);
                }
            }
        }

        public void Dispose()
        {
            try { ClearAttributes(_baseDir); }
            catch (Exception) { }

            try { RemoveJunctions(_baseDir); }
            catch (Exception) { }

            try
            {
                if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
            }
            catch (Exception)
            {
                // Qualche cartella ha permessi non ereditari (test sugli ACL):
                // si ripristina l'ereditarietà e si riprova una sola volta.
                ResetAccessControl(_baseDir);
                try
                {
                    if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true);
                }
                catch (Exception) { }
            }

            try
            {
                string parent = Path.GetDirectoryName(_baseDir);
                if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
                    Directory.Delete(parent, false);
            }
            catch (Exception) { }
        }

        private static void ClearAttributes(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Rimuove i reparse point SENZA scorrerli, così la cancellazione ricorsiva
        /// successiva non può mai raggiungere il loro contenuto.
        /// </summary>
        private static void RemoveJunctions(string directory)
        {
            if (!Directory.Exists(directory)) return;

            string[] entries;
            try { entries = Directory.GetDirectories(directory); }
            catch (Exception) { return; }

            for (int i = 0; i < entries.Length; i++)
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(entries[i]);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        RemoveJunction(entries[i]);
                    else
                        RemoveJunctions(entries[i]);
                }
                catch (Exception) { }
            }
        }
    }
}
