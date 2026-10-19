using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BPGPUpdater
{
    /// <summary>
    /// The app itself: owns the state (WoW folder, installed and latest version, raid schedule, settings
    /// backups), the tray icon, the 6-hour check and the raid reminder. The window is only a view on it and
    /// can be closed and reopened. Created from Application.Startup, so async work continues on the UI thread.
    /// </summary>
    internal sealed class TrayApp
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        /// <summary>The raid schedule changes more often than BPGP: people mark when they can't come</summary>
        private static readonly TimeSpan ScheduleInterval = TimeSpan.FromMinutes(30);
        /// <summary>The tray reminds this long before a raid starts</summary>
        private static readonly TimeSpan ReminderLead = TimeSpan.FromHours(2);
        /// <summary>The changelog shows at most this many releases</summary>
        private const int ChangelogLength = 10;

        private readonly Settings settings = Settings.Load();
        private readonly bool preReleaseSwitch;
        private readonly string testZip;
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer minuteTimer;
        private List<Release> addonHistory;
        private DateTime scheduleCheckedAt = DateTime.MinValue;
        private MainWindow window;

        public string RetailFolder { get; private set; }
        public InstalledAddon Installed { get; private set; }
        public Release Latest { get; private set; }
        public string Status { get; private set; }
        public bool StatusIsError { get; private set; }
        public bool Busy { get; private set; }

        /// <summary>Null until the sheet has been read, and when no link is set</summary>
        public RaidSchedule Schedule { get; private set; }
        public string ScheduleStatus { get; private set; }
        public bool ScheduleStatusIsError { get; private set; }
        public bool ScheduleBusy { get; private set; }

        /// <summary>The settings backups, newest first</summary>
        public List<BackupInfo> Backups { get; private set; }
        /// <summary>The result of the last restore, shown under Settings</summary>
        public string BackupStatus { get; private set; }
        public bool BackupStatusIsError { get; private set; }

        /// <summary>Raised when anything the window shows has changed</summary>
        public event Action Changed;

        public TrayApp(bool startInTray, bool preReleases, string testZip, string screenshot)
        {
            preReleaseSwitch = preReleases;
            this.testZip = testZip;
            SelfUpdater.CleanUp();
            Autostart.Refresh();
            RefreshBackups();

            RetailFolder = WowLocator.Find(settings.RetailFolder);
            if (RetailFolder != null && RetailFolder != settings.RetailFolder)
            {
                settings.RetailFolder = RetailFolder;
                settings.Save();
            }
            RefreshInstalled();

            // WPF has no tray icon of its own: use the Windows Forms one
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open BPGP Updater", null, (s, e) => ShowWindow());
            menu.Items.Add("Check for updates", null, async (s, e) => await CheckAsync(true));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Exit());
            tray = new Forms.NotifyIcon
            {
                Icon = AppIcon.Get(),
                Text = "BPGP Updater",
                ContextMenuStrip = menu,
                Visible = startInTray || Autostart.IsEnabled,
            };
            tray.DoubleClick += (s, e) => ShowWindow();
            tray.BalloonTipClicked += (s, e) => ShowWindow();

            timer = new DispatcherTimer { Interval = CheckInterval };
            timer.Tick += async (s, e) => await CheckAsync(true);
            timer.Start();
            minuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            minuteTimer.Tick += (s, e) => EveryMinute();
            minuteTimer.Start();

            if (!startInTray) ShowWindow();
            var schedule = RefreshScheduleAsync();
            var check = CheckAndContinue(startInTray, screenshot);
        }

        private async Task CheckAndContinue(bool notify, string screenshot)
        {
            await CheckAsync(notify);
            if (screenshot == null) return;
            while (ScheduleBusy) await Task.Delay(100);
            window.SaveScreenshots(screenshot);
            Exit();
        }

        public bool UpdateAvailable
        {
            get
            {
                if (Latest == null || Latest.AddonZip == null || RetailFolder == null) return false;
                switch (Installed.State)
                {
                    case AddonState.Linked: return false;
                    case AddonState.Installed: return SemVersion.IsNewer(Latest.Version, Installed.Version);
                    default: return true; // not installed, or a copy of the source
                }
            }
        }

        /// <summary>The --prerelease switch: betas are on and the setting can't turn them off</summary>
        public bool PreReleaseSwitch
        {
            get { return preReleaseSwitch; }
        }

        /// <summary>Whether alpha and beta releases of BPGP count (the beta channel under Settings, or --prerelease)</summary>
        public bool PreReleases
        {
            get { return preReleaseSwitch || settings.BetaChannel; }
        }

        public async Task SetBetaChannelAsync(bool enabled)
        {
            settings.BetaChannel = enabled;
            settings.Save();
            Changed?.Invoke();
            await CheckAsync(false);
        }

        /// <summary>
        /// The release notes to show: every release between the installed version and the latest, newest
        /// first; or just the latest (not installed yet), or the installed one (up to date).
        /// </summary>
        public List<Release> Changelog
        {
            get
            {
                if (Latest == null) return new List<Release>();
                var history = addonHistory ?? new List<Release> { Latest };
                var installed = Installed.State == AddonState.Installed ? Installed.Version : null;
                if (installed == null) return new List<Release> { Latest };
                if (UpdateAvailable)
                {
                    return history
                        .Where(r => SemVersion.IsNewer(r.Version, installed) && r.Version.CompareTo(Latest.Version) <= 0)
                        .Take(ChangelogLength)
                        .ToList();
                }
                var current = history.FirstOrDefault(r => r.Version.CompareTo(installed) == 0);
                return new List<Release> { current ?? Latest };
            }
        }

        private bool WindowShown
        {
            get { return window != null && !window.IsClosed && window.IsVisible; }
        }

        public void ShowWindow()
        {
            if (window == null || window.IsClosed) window = new MainWindow(this);
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            // The tray icon only stays while the window is open when "Start with Windows" is on
            tray.Visible = Autostart.IsEnabled;
            // Opening the window shows a fresh schedule, without reading the sheet on every click
            if (DateTime.Now - scheduleCheckedAt > TimeSpan.FromMinutes(5))
            {
                var schedule = RefreshScheduleAsync();
            }
        }

        /// <summary>The window was minimized: it always goes to the tray instead of the taskbar</summary>
        public void WindowMinimized()
        {
            tray.Visible = true;
            if (!settings.ShownTrayHint)
            {
                tray.ShowBalloonTip(5000, "BPGP Updater", "BPGP Updater is still running here. Double-click the icon to open it.", Forms.ToolTipIcon.Info);
                settings.ShownTrayHint = true;
                settings.Save();
            }
        }

        /// <summary>The window was closed: stay in the tray with "Start with Windows" on, otherwise exit</summary>
        public void WindowClosed()
        {
            if (Autostart.IsEnabled)
            {
                tray.Visible = true;
            }
            else
            {
                window = null; // it's closing itself
                Exit();
            }
        }

        public void SetAutostart(bool enabled)
        {
            Autostart.Set(enabled);
            tray.Visible = enabled || !WindowShown;
            Changed?.Invoke();
        }

        public bool SetRetailFolder(string picked)
        {
            var retail = WowLocator.ToRetailFolder(picked);
            if (retail == null) return false;
            RetailFolder = settings.RetailFolder = retail;
            settings.Save();
            RefreshInstalled();
            SetStatus(Latest != null ? CheckResult() : Status);
            return true;
        }

        public async Task CheckAsync(bool notify)
        {
            if (Busy) return;
            Busy = true;
            SetStatus("Checking for updates…");
            try
            {
                Release updater = null;
                DateTime? limitedUntil = null;
                if (testZip != null)
                {
                    Latest = await Task.Run(() => GitHubReleases.FromLocalZip(testZip));
                    addonHistory = null;
                }
                else
                {
                    var preReleases = PreReleases;
                    var latest = await Task.Run(() => GitHubReleases.GetLatest(preReleases));
                    Latest = latest.Addon;
                    addonHistory = latest.AddonHistory;
                    updater = latest.Updater;
                    limitedUntil = latest.RateLimitedUntil;
                }
                RefreshInstalled();
                if (SelfUpdater.HasUpdate(updater))
                {
                    await UpdateSelfAsync(updater);
                    return;
                }
                // GitHub's limit was hit: say so, but keep working with the last known release
                SetStatus(CheckResult() + (limitedUntil != null
                    ? " (GitHub's limit is reached: this is the last known release. New releases show up after " + limitedUntil.Value.ToString("HH:mm") + ".)"
                    : ""));
                if (notify && UpdateAvailable && settings.NotifiedVersion != Latest.Tag)
                {
                    tray.Visible = true;
                    tray.ShowBalloonTip(10000, "BPGP update", "BPGP " + Latest.Tag + " is available. Click here to update.", Forms.ToolTipIcon.Info);
                    settings.NotifiedVersion = Latest.Tag;
                    settings.Save();
                }
            }
            catch (RateLimitException e)
            {
                SetStatus(e.Message + " Try again after that.", true);
            }
            catch (Exception e) when (e is WebException || e is IOException || e is SerializationException)
            {
                SetStatus(testZip != null ? "Couldn't read the test zip: " + e.Message : "Couldn't reach GitHub: " + e.Message, true);
            }
            catch (Exception e) when (testZip != null && (e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException))
            {
                SetStatus("Couldn't read the test zip: " + e.Message, true);
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        private string CheckResult()
        {
            if (Latest == null) return "No BPGP release published yet.";
            if (RetailFolder == null) return "World of Warcraft wasn't found. Pick its folder under Settings.";
            if (Installed.State == AddonState.Linked) return "AddOns\\BPGP is linked to a git clone (developer setup). The updater leaves it alone.";
            if (UpdateAvailable) return "BPGP " + Latest.Tag + " is available.";
            // The beta channel was turned off: the beta stays until a newer release replaces it
            if (Installed.Version != null && Installed.Version.IsPreRelease && !PreReleases) return "You have a beta of BPGP; the next release replaces it.";
            return "BPGP is up to date.";
        }

        private async Task UpdateSelfAsync(Release release)
        {
            SetStatus("Updating BPGP Updater to " + release.Version + "…");
            try
            {
                bool inTray = !WindowShown;
                await Task.Run(() => SelfUpdater.Apply(release, inTray));
                Exit();
            }
            catch (Exception e) when (e is WebException || e is IOException || e is UnauthorizedAccessException)
            {
                // Not fatal: the addon update still works with this version
                SetStatus("Couldn't update BPGP Updater itself (" + e.Message + "). Download the new one from the releases page.", true);
            }
        }

        public async Task InstallAsync()
        {
            if (Busy || !UpdateAvailable) return;
            Busy = true;
            var release = Latest;
            var zip = Path.Combine(Path.GetTempPath(), "BPGP-" + Guid.NewGuid().ToString("N") + ".zip");
            SetStatus("Downloading BPGP " + release.Tag + "…");
            try
            {
                var addOns = WowLocator.AddOnsFolder(RetailFolder);
                var retail = RetailFolder;
                var reason = (Installed.State == AddonState.Installed ? "before updating to " : "before installing ") + release.Tag;
                string backupError = null;
                await Task.Run(() =>
                {
                    GitHubReleases.Download(release.AddonZip.DownloadUrl, zip);
                    // A new version may convert the settings when WoW loads it: keep a copy of the old ones
                    backupError = TryBackup(retail, reason);
                    AddonInstaller.Install(zip, addOns);
                });
                RefreshInstalled();
                RefreshBackups();
                SetStatus("BPGP " + release.Tag + " is installed." + (IsWowRunning() ? " Restart WoW to load it." : "") + BackupWarning(backupError));
            }
            catch (UnauthorizedAccessException)
            {
                SetStatus("No permission to write to the AddOns folder. Close WoW, or run BPGP Updater as administrator.", true);
            }
            catch (Exception e) when (e is WebException || e is IOException || e is InvalidDataException || e is InvalidOperationException)
            {
                SetStatus("Install failed: " + e.Message, true);
            }
            finally
            {
                try { File.Delete(zip); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                Busy = false;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Uninstall is possible when BPGP is a normal folder (installed by us or a copy of the source),
        /// or when it's gone but its settings are still there. Never for a linked folder.
        /// </summary>
        public bool CanUninstall
        {
            get
            {
                if (RetailFolder == null) return false;
                switch (Installed.State)
                {
                    case AddonState.Installed:
                    case AddonState.DevBuild: return true;
                    case AddonState.NotInstalled: return SettingsFileCount() > 0;
                    default: return false;
                }
            }
        }

        public int SettingsFileCount()
        {
            try { return RetailFolder != null ? AddonInstaller.FindSettings(RetailFolder).Length : 0; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return 0; }
        }

        public async Task UninstallAsync(bool removeSettings)
        {
            if (Busy || !CanUninstall) return;
            if (removeSettings && IsWowRunning())
            {
                SetStatus("Close World of Warcraft first: it writes the BPGP settings back when you log out.", true);
                return;
            }
            Busy = true;
            SetStatus("Removing BPGP…");
            try
            {
                var addOns = WowLocator.AddOnsFolder(RetailFolder);
                var retail = RetailFolder;
                string backupError = null;
                int removed = await Task.Run(() =>
                {
                    backupError = TryBackup(retail, "before uninstalling");
                    AddonInstaller.Uninstall(addOns);
                    return removeSettings ? AddonInstaller.DeleteSettings(retail) : 0;
                });
                RefreshInstalled();
                RefreshBackups();
                SetStatus((removeSettings
                    ? "BPGP and its settings are removed (" + removed + " settings file" + (removed == 1 ? "" : "s") + ")." + (backupError == null && removed > 0 ? " A backup of them is under Settings." : "")
                    : "BPGP is uninstalled. Your BPGP settings and data (WTF folder) are kept." + (IsWowRunning() ? " Restart WoW to unload it." : ""))
                    + BackupWarning(backupError));
            }
            catch (UnauthorizedAccessException)
            {
                SetStatus("No permission to remove BPGP from the AddOns folder. Close WoW, or run BPGP Updater as administrator.", true);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException)
            {
                SetStatus("Uninstall failed: " + e.Message, true);
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        /// <summary>Puts a backup's settings files back; the current ones are backed up first</summary>
        public async Task RestoreAsync(BackupInfo backup)
        {
            if (Busy || backup == null || RetailFolder == null) return;
            if (IsWowRunning())
            {
                SetBackupStatus("Close World of Warcraft first: it writes the BPGP settings back when you log out.", true);
                return;
            }
            Busy = true;
            SetBackupStatus("Restoring the backup…", false);
            try
            {
                var retail = RetailFolder;
                int restored = await Task.Run(() =>
                {
                    SettingsBackup.Create(retail, "before restoring a backup", SettingsBackup.DefaultFolder);
                    int count = SettingsBackup.Restore(backup.Folder, retail);
                    SettingsBackup.Prune(SettingsBackup.DefaultFolder, SettingsBackup.Keep);
                    return count;
                });
                RefreshBackups();
                SetBackupStatus("Restored " + restored + " settings file" + (restored == 1 ? "" : "s") + " from " + backup.Time.ToString("d MMM, HH:mm", CultureInfo.GetCultureInfo("en-GB")) + ".", restored == 0);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SetBackupStatus("Restore failed: " + e.Message, true);
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        /// <summary>Backs up the settings and prunes old backups. Returns the error, or null; a failed backup never stops an install.</summary>
        private static string TryBackup(string retailFolder, string reason)
        {
            try
            {
                SettingsBackup.Create(retailFolder, reason, SettingsBackup.DefaultFolder);
                SettingsBackup.Prune(SettingsBackup.DefaultFolder, SettingsBackup.Keep);
                return null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return e.Message;
            }
        }

        private static string BackupWarning(string backupError)
        {
            return backupError != null ? " (Backing up the settings failed: " + backupError + ")" : "";
        }

        private void RefreshBackups()
        {
            try { Backups = SettingsBackup.List(SettingsBackup.DefaultFolder); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Backups = new List<BackupInfo>(); }
        }

        private void SetBackupStatus(string text, bool isError)
        {
            BackupStatus = text;
            BackupStatusIsError = isError;
            Changed?.Invoke();
        }

        public string ScheduleLink
        {
            get { return settings.RaidScheduleUrl; }
        }

        public string CharacterName
        {
            get { return settings.CharacterName; }
        }

        /// <summary>Saves the raid schedule link (empty removes it) and reads it. False when it isn't a Google Sheets link.</summary>
        public bool SetScheduleLink(string link)
        {
            link = (link ?? "").Trim();
            if (link.Length > 0 && RaidSchedule.ToCsvUrl(link) == null) return false;
            settings.RaidScheduleUrl = link.Length > 0 ? link : null;
            settings.Save();
            Schedule = null;
            var schedule = RefreshScheduleAsync();
            return true;
        }

        public void SetCharacterName(string name)
        {
            settings.CharacterName = string.IsNullOrWhiteSpace(name) ? null : name;
            settings.Save();
            Changed?.Invoke();
        }

        /// <summary>The raids that haven't ended yet, in date order</summary>
        public List<Raid> UpcomingRaids
        {
            get
            {
                if (Schedule == null) return new List<Raid>();
                var now = DateTime.Now;
                return Schedule.Raids.Where(r => r.End > now).OrderBy(r => r.Start).ToList();
            }
        }

        /// <summary>Whether the picked name has a 1 (true) or a 0 (false) for this raid; null when unknown</summary>
        public bool? IsAttending(Raid raid)
        {
            return raid.IsAttending(settings.CharacterName);
        }

        public async Task RefreshScheduleAsync()
        {
            if (ScheduleBusy) return;
            var url = RaidSchedule.ToCsvUrl(settings.RaidScheduleUrl);
            if (url == null)
            {
                Schedule = null;
                SetScheduleStatus(null, false);
                return;
            }
            ScheduleBusy = true;
            scheduleCheckedAt = DateTime.Now;
            SetScheduleStatus("Reading the raid schedule…", false);
            try
            {
                var context = new FetchContext();
                var today = DateTime.Today;
                var schedule = await Task.Run(() => FetchSchedule(url, context, today));
                // The link may have changed meanwhile: then the new one is read below
                if (url == RaidSchedule.ToCsvUrl(settings.RaidScheduleUrl))
                {
                    Schedule = schedule;
                    SetScheduleStatus(context.UsedSavedCopy
                        ? "Google Sheets can't be reached: this is the schedule from the last time."
                        : "Raid schedule read at " + DateTime.Now.ToString("HH:mm") + ".", context.UsedSavedCopy);
                    RemindOfRaid();
                }
            }
            catch (InvalidDataException e)
            {
                SetScheduleStatus(e.Message, true);
            }
            catch (Exception e) when (e is WebException || e is IOException || e is UnauthorizedAccessException)
            {
                SetScheduleStatus("Couldn't read the raid schedule: " + e.Message, true);
            }
            finally
            {
                ScheduleBusy = false;
                Changed?.Invoke();
            }
            if (url != RaidSchedule.ToCsvUrl(settings.RaidScheduleUrl)) await RefreshScheduleAsync();
        }

        private static RaidSchedule FetchSchedule(string url, FetchContext context, DateTime today)
        {
            byte[] csv;
            try
            {
                csv = ApiCache.Get(url, context, "text/csv");
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException("Google didn't hand out the sheet. Share it as \"Anyone with the link can view\" (Share → General access).");
            }
            return RaidSchedule.Parse(Encoding.UTF8.GetString(csv), today);
        }

        private void SetScheduleStatus(string text, bool isError)
        {
            ScheduleStatus = text;
            ScheduleStatusIsError = isError;
            Changed?.Invoke();
        }

        private void EveryMinute()
        {
            if (settings.RaidScheduleUrl != null && DateTime.Now - scheduleCheckedAt > ScheduleInterval)
            {
                var schedule = RefreshScheduleAsync(); // reminds when it's done
            }
            else
            {
                RemindOfRaid();
            }
        }

        /// <summary>
        /// A balloon once, ReminderLead before a raid, unless the picked name is marked absent. It says
        /// whether BPGP is ready, so nobody finds out about an update when the raid starts.
        /// </summary>
        private void RemindOfRaid()
        {
            if (Busy) return; // a check or install is running: its result goes in the reminder, next minute
            var next = UpcomingRaids.FirstOrDefault();
            if (next == null) return;
            var until = next.Start - DateTime.Now;
            if (until <= TimeSpan.Zero || until > ReminderLead) return;
            var key = next.Start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (settings.NotifiedRaid == key || IsAttending(next) == false) return;
            settings.NotifiedRaid = key;
            settings.Save();

            var text = next.Note.Length > 0 ? next.Note + ". " : "";
            if (UpdateAvailable)
                text += "Click here to " + (Installed.State == AddonState.Installed ? "update to" : "install") + " BPGP " + Latest.Tag + " before the raid.";
            else if (Latest != null && Installed.State == AddonState.Installed)
                text += "BPGP is up to date.";
            tray.Visible = true;
            tray.ShowBalloonTip(10000, "Raid tonight at " + next.Start.ToString("HH:mm"), text.Length > 0 ? text : "See you there.", Forms.ToolTipIcon.Info);
        }

        private void RefreshInstalled()
        {
            Installed = RetailFolder != null
                ? AddonInstaller.Inspect(WowLocator.AddOnsFolder(RetailFolder))
                : new InstalledAddon { State = AddonState.NotInstalled };
        }

        public static bool IsWowRunning()
        {
            return new[] { "Wow", "WowT", "WowB" }.Any(name => Process.GetProcessesByName(name).Length > 0);
        }

        private void SetStatus(string text, bool isError = false)
        {
            Status = text;
            StatusIsError = isError;
            Changed?.Invoke();
        }

        private void Exit()
        {
            timer.Stop();
            minuteTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            if (window != null && !window.IsClosed) window.CloseForExit();
            Application.Current.Shutdown();
        }
    }

    internal static class AppIcon
    {
        private static Stream Open()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("BPGPUpdater.app.ico");
        }

        /// <summary>For the tray icon (Windows Forms)</summary>
        public static Drawing.Icon Get()
        {
            var stream = Open();
            return stream != null ? new Drawing.Icon(stream) : Drawing.SystemIcons.Application;
        }

        /// <summary>For the window and taskbar (WPF): the largest frame of the icon</summary>
        public static ImageSource GetImage()
        {
            using (var stream = Open())
            {
                if (stream == null) return null;
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            }
        }
    }
}
