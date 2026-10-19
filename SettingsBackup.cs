using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BPGPUpdater
{
    internal sealed class BackupInfo
    {
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

        public string Folder { get; set; }
        public DateTime Time { get; set; }
        /// <summary>1, or 2 and up for a second backup in the same second</summary>
        public int Sequence { get; set; }
        /// <summary>Why it was made, e.g. "before updating to v1.0.2"</summary>
        public string Reason { get; set; }

        /// <summary>Shown in the list under Settings</summary>
        public override string ToString()
        {
            return Time.ToString("d MMM yyyy, HH:mm", English) + (string.IsNullOrEmpty(Reason) ? "" : " (" + Reason + ")");
        }
    }

    /// <summary>
    /// Copies of BPGP's SavedVariables (BPGP.lua and .lua.bak, see AddonInstaller.FindSettings), made before the
    /// updater installs, updates, uninstalls or restores. Each copy is a folder backups\yyyyMMdd-HHmmss with the
    /// paths below WTF kept (Account\&lt;account&gt;\...\SavedVariables\BPGP.lua) and a reason.txt.
    /// </summary>
    internal static class SettingsBackup
    {
        /// <summary>How many backups are kept</summary>
        public const int Keep = 5;
        public static readonly string DefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPGPUpdater", "backups");

        private const string TimeFormat = "yyyyMMdd-HHmmss";
        private const string ReasonFile = "reason.txt";

        /// <summary>Copies the settings files into a new backup. Returns its folder, or null when there's nothing to copy.</summary>
        public static string Create(string retailFolder, string reason, string backupsFolder)
        {
            var files = AddonInstaller.FindSettings(retailFolder);
            if (files.Length == 0) return null;
            var wtf = Path.GetFullPath(Path.Combine(retailFolder, "WTF"));
            var name = DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
            var folder = Path.Combine(backupsFolder, name);
            for (int sequence = 2; Directory.Exists(folder); sequence++) folder = Path.Combine(backupsFolder, name + "-" + sequence);

            foreach (var file in files)
            {
                var target = Path.Combine(folder, RelativePath(wtf, Path.GetFullPath(file)));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
            File.WriteAllText(Path.Combine(folder, ReasonFile), reason ?? "", Encoding.UTF8);
            return folder;
        }

        /// <summary>The backups, newest first</summary>
        public static List<BackupInfo> List(string backupsFolder)
        {
            if (!Directory.Exists(backupsFolder)) return new List<BackupInfo>();
            return Directory.GetDirectories(backupsFolder)
                .Select(Read)
                .Where(b => b != null)
                .OrderByDescending(b => b.Time)
                .ThenByDescending(b => b.Sequence)
                .ToList();
        }

        /// <summary>Removes all but the newest backups</summary>
        public static void Prune(string backupsFolder, int keep)
        {
            foreach (var backup in List(backupsFolder).Skip(keep))
            {
                // Our own folders, but never delete recursively through a link
                if ((File.GetAttributes(backup.Folder) & FileAttributes.ReparsePoint) != 0) continue;
                Directory.Delete(backup.Folder, true);
            }
        }

        /// <summary>
        /// Puts the backup's files back in WTF, over the current ones. Only BPGP.lua and BPGP.lua.bak in a
        /// SavedVariables folder below WTF\Account are written, whatever else the backup folder holds.
        /// The caller makes sure WoW isn't running. Returns how many files were restored.
        /// </summary>
        public static int Restore(string backupFolder, string retailFolder)
        {
            var source = Path.GetFullPath(backupFolder).TrimEnd(Path.DirectorySeparatorChar);
            var wtf = Path.GetFullPath(Path.Combine(retailFolder, "WTF"));
            var accounts = Path.Combine(wtf, "Account") + Path.DirectorySeparatorChar;
            int restored = 0;
            foreach (var file in SettingsFiles(source))
            {
                var target = Path.GetFullPath(Path.Combine(wtf, RelativePath(source, file)));
                if (!target.StartsWith(accounts, StringComparison.OrdinalIgnoreCase) || !AddonInstaller.IsSettingsFile(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                restored++;
            }
            return restored;
        }

        private static BackupInfo Read(string folder)
        {
            var name = Path.GetFileName(folder);
            DateTime time;
            if (name.Length < TimeFormat.Length || !DateTime.TryParseExact(name.Substring(0, TimeFormat.Length), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return null;
            int sequence = 1;
            if (name.Length > TimeFormat.Length + 1 && name[TimeFormat.Length] == '-' && !int.TryParse(name.Substring(TimeFormat.Length + 1), out sequence))
                return null;
            string reason = null;
            try
            {
                var file = Path.Combine(folder, ReasonFile);
                if (File.Exists(file)) reason = File.ReadAllText(file, Encoding.UTF8).Trim();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return new BackupInfo { Folder = folder, Time = time, Sequence = sequence, Reason = reason };
        }

        private static IEnumerable<string> SettingsFiles(string backupFolder)
        {
            var accounts = Path.Combine(backupFolder, "Account");
            if (!Directory.Exists(accounts)) return new string[0];
            return Directory.EnumerateFiles(accounts, AddonInstaller.AddonName + ".lua*", SearchOption.AllDirectories).Where(AddonInstaller.IsSettingsFile);
        }

        private static string RelativePath(string folder, string file)
        {
            if (!file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException(file + " isn't inside " + folder + ".");
            return file.Substring(folder.Length + 1);
        }
    }
}
