using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BPGPUpdater
{
    internal enum AddonState
    {
        NotInstalled,
        Installed,
        /// <summary>A copy of the source (version @project-version@): a release replaces it</summary>
        DevBuild,
        /// <summary>A junction or symlink to a git clone (developer setup): never touched</summary>
        Linked,
    }

    internal sealed class InstalledAddon
    {
        public AddonState State { get; set; }
        public SemVersion Version { get; set; }
        public string VersionText { get; set; }
    }

    /// <summary>Reads and replaces Interface\AddOns\BPGP. SavedVariables live in WTF and are never touched.</summary>
    internal static class AddonInstaller
    {
        public const string AddonName = "BPGP";
        private const string VersionTag = "## Version:";

        public static InstalledAddon Inspect(string addOnsFolder)
        {
            var folder = Path.Combine(addOnsFolder, AddonName);
            if (!Directory.Exists(folder)) return new InstalledAddon { State = AddonState.NotInstalled };
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                return new InstalledAddon { State = AddonState.Linked, VersionText = ReadVersion(folder) };
            var text = ReadVersion(folder);
            if (text != null && text.Contains("@project-version@"))
                return new InstalledAddon { State = AddonState.DevBuild, VersionText = text };
            return new InstalledAddon { State = AddonState.Installed, VersionText = text, Version = SemVersion.Parse(text) };
        }

        private static string ReadVersion(string folder)
        {
            var toc = Path.Combine(folder, AddonName + ".toc");
            if (!File.Exists(toc)) return null;
            var line = File.ReadLines(toc).FirstOrDefault(l => l.StartsWith(VersionTag, StringComparison.OrdinalIgnoreCase));
            return line == null ? null : line.Substring(VersionTag.Length).Trim();
        }

        /// <summary>
        /// Replaces AddOns\BPGP with the BPGP folder from the release zip. The zip is extracted next to
        /// AddOns first (same drive, so the swap is a rename), and the old folder is restored on failure.
        /// </summary>
        public static void Install(string zipPath, string addOnsFolder)
        {
            var target = Path.Combine(addOnsFolder, AddonName);
            if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("AddOns\\BPGP is linked to a git clone. The updater leaves it alone.");

            var interfaceFolder = Directory.GetParent(addOnsFolder).FullName;
            var staging = Path.Combine(interfaceFolder, "BPGPUpdater.tmp");
            var backup = target + ".old";
            DeleteIfExists(staging);
            DeleteIfExists(backup);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, staging);
                var extracted = Path.Combine(staging, AddonName);
                if (!File.Exists(Path.Combine(extracted, AddonName + ".toc")))
                    throw new InvalidDataException("The download doesn't contain BPGP\\BPGP.toc.");

                bool hadOld = Directory.Exists(target);
                if (hadOld) Directory.Move(target, backup);
                try
                {
                    Directory.Move(extracted, target);
                }
                catch
                {
                    if (hadOld && !Directory.Exists(target)) Directory.Move(backup, target);
                    throw;
                }
                TryDelete(backup);
            }
            finally
            {
                TryDelete(staging);
            }
        }

        private static void DeleteIfExists(string folder)
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        private static void TryDelete(string folder)
        {
            try { DeleteIfExists(folder); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
