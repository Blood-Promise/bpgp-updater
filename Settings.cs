using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace BPGPUpdater
{
    /// <summary>Saved in %APPDATA%\BPGPUpdater\settings.json</summary>
    [DataContract]
    internal sealed class Settings
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPGPUpdater");
        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        /// <summary>The _retail_ folder</summary>
        [DataMember(Name = "retailFolder")] public string RetailFolder { get; set; }

        /// <summary>The last version the tray announced, so it doesn't repeat itself every 6 hours</summary>
        [DataMember(Name = "notifiedVersion")] public string NotifiedVersion { get; set; }

        /// <summary>The "still running in the tray" hint was shown once after minimizing</summary>
        [DataMember(Name = "shownTrayHint")] public bool ShownTrayHint { get; set; }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var stream = File.OpenRead(FilePath))
                    {
                        return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream) ?? new Settings();
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is SerializationException || e is UnauthorizedAccessException)
            {
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                using (var stream = File.Create(FilePath))
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, this);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>"Start with Windows": a value under HKCU\...\Run that starts the app in the tray</summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "BPGPUpdater";

        private static string Command
        {
            get { return "\"" + SelfUpdater.ExePath + "\" " + Program.TrayArgument; }
        }

        /// <summary>Windows ignores the Run key for Store (MSIX) installs, so the option isn't offered there</summary>
        public static bool IsAvailable
        {
            get { return !Packaging.IsStoreInstall; }
        }

        public static bool IsEnabled
        {
            get
            {
                if (!IsAvailable) return false;
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
        }

        public static void Set(bool enabled)
        {
            if (!IsAvailable) return;
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, Command);
                else key.DeleteValue(ValueName, false);
            }
        }

        /// <summary>Keeps the Run entry pointing at this exe when the user moved it</summary>
        public static void Refresh()
        {
            if (IsEnabled) Set(true);
        }
    }
}
