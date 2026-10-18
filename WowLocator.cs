using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace BPGPUpdater
{
    /// <summary>
    /// Finds the retail WoW folder (the one with _retail_\Interface\AddOns).
    /// Order: the saved folder, the registry, common install locations on every drive.
    /// </summary>
    internal static class WowLocator
    {
        private const string RetailFolder = "_retail_";

        public static string AddOnsFolder(string retailFolder)
        {
            return Path.Combine(retailFolder, "Interface", "AddOns");
        }

        public static string Find(string saved)
        {
            return Candidates(saved).Select(ToRetailFolder).FirstOrDefault(found => found != null);
        }

        /// <summary>
        /// The _retail_ folder for a folder the user picked: the WoW folder itself,
        /// _retail_, Interface or AddOns. Null when it isn't a retail WoW install.
        /// </summary>
        public static string ToRetailFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var folder = new DirectoryInfo(path.Trim().TrimEnd('\\', '/'));
                for (var current = folder; current != null; current = current.Parent)
                {
                    if (string.Equals(current.Name, RetailFolder, StringComparison.OrdinalIgnoreCase) && Directory.Exists(AddOnsFolder(current.FullName)))
                        return current.FullName;
                    var retail = Path.Combine(current.FullName, RetailFolder);
                    if (Directory.Exists(AddOnsFolder(retail))) return retail;
                    // Only look up from inside a WoW folder (AddOns → Interface → _retail_), not up to the drive root
                    if (!IsInsideRetail(current.Name)) break;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
            }
            return null;
        }

        private static bool IsInsideRetail(string name)
        {
            return string.Equals(name, "AddOns", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Interface", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> Candidates(string saved)
        {
            yield return saved;
            foreach (var path in FromRegistry()) yield return path;
            foreach (var path in CommonLocations()) yield return path;
        }

        private static IEnumerable<string> FromRegistry()
        {
            var results = new List<string>();
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (var key = hklm.OpenSubKey(@"SOFTWARE\Blizzard Entertainment\World of Warcraft"))
                        {
                            if (key != null) results.Add(key.GetValue("InstallPath") as string);
                        }
                        using (var uninstall = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (var name in uninstall.GetSubKeyNames())
                            {
                                using (var app = uninstall.OpenSubKey(name))
                                {
                                    if (app != null && string.Equals(app.GetValue("DisplayName") as string, "World of Warcraft", StringComparison.OrdinalIgnoreCase))
                                        results.Add(app.GetValue("InstallLocation") as string);
                                }
                            }
                        }
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
                {
                }
            }
            return results;
        }

        private static IEnumerable<string> CommonLocations()
        {
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName);
            foreach (var root in drives)
            {
                yield return Path.Combine(root, "Program Files (x86)", "World of Warcraft");
                yield return Path.Combine(root, "Program Files", "World of Warcraft");
                yield return Path.Combine(root, "World of Warcraft");
                yield return Path.Combine(root, "Blizzard", "World of Warcraft");
                yield return Path.Combine(root, "Games", "World of Warcraft");
                // One level below Games, e.g. E:\Games\Blizzard\World of Warcraft
                foreach (var folder in SafeSubfolders(Path.Combine(root, "Games")))
                    yield return Path.Combine(folder, "World of Warcraft");
            }
        }

        private static IEnumerable<string> SafeSubfolders(string path)
        {
            try
            {
                return Directory.Exists(path) ? Directory.GetDirectories(path) : new string[0];
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }
    }
}
