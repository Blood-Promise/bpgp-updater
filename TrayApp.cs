using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
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
    /// The app itself: owns the state (WoW folder, installed and latest version), the tray icon and
    /// the 6-hour check. The window is only a view on it and can be closed and reopened.
    /// Created from Application.Startup, so async work continues on the UI thread.
    /// </summary>
    internal sealed class TrayApp
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private readonly Settings settings = Settings.Load();
        private readonly bool preReleases;
        private readonly string testZip;
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer timer;
        private MainWindow window;

        public string RetailFolder { get; private set; }
        public InstalledAddon Installed { get; private set; }
        public Release Latest { get; private set; }
        public string Status { get; private set; }
        public bool StatusIsError { get; private set; }
        public bool Busy { get; private set; }

        /// <summary>Raised when anything the window shows has changed</summary>
        public event Action Changed;

        public TrayApp(bool startInTray, bool preReleases, string testZip, string screenshot)
        {
            this.preReleases = preReleases;
            this.testZip = testZip;
            SelfUpdater.CleanUp();
            Autostart.Refresh();

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

            if (!startInTray) ShowWindow();
            var check = CheckAndContinue(startInTray, screenshot);
        }

        private async Task CheckAndContinue(bool notify, string screenshot)
        {
            await CheckAsync(notify);
            if (screenshot == null) return;
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
                }
                else
                {
                    var latest = await Task.Run(() => GitHubReleases.GetLatest(preReleases));
                    Latest = latest.Addon;
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
            return UpdateAvailable ? "BPGP " + Latest.Tag + " is available." : "BPGP is up to date.";
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
                await Task.Run(() =>
                {
                    GitHubReleases.Download(release.AddonZip.DownloadUrl, zip);
                    AddonInstaller.Install(zip, addOns);
                });
                RefreshInstalled();
                SetStatus("BPGP " + release.Tag + " is installed." + (IsWowRunning() ? " Restart WoW to load it." : ""));
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
                int removed = await Task.Run(() =>
                {
                    AddonInstaller.Uninstall(addOns);
                    return removeSettings ? AddonInstaller.DeleteSettings(retail) : 0;
                });
                RefreshInstalled();
                SetStatus(removeSettings
                    ? "BPGP and its settings are removed (" + removed + " settings file" + (removed == 1 ? "" : "s") + ")."
                    : "BPGP is uninstalled. Your BPGP settings and data (WTF folder) are kept." + (IsWowRunning() ? " Restart WoW to unload it." : ""));
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
