using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BPGPUpdater
{
    /// <summary>
    /// The app itself: owns the state (WoW folder, installed and latest version), the tray icon and
    /// the 6-hour check. The window is only a view on it and can be closed and reopened.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private readonly Settings settings = Settings.Load();
        private readonly bool preReleases;
        private readonly string testZip;
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private MainForm form;

        public string RetailFolder { get; private set; }
        public InstalledAddon Installed { get; private set; }
        public Release Latest { get; private set; }
        public string Status { get; private set; }
        public bool Busy { get; private set; }

        /// <summary>Raised when anything the window shows has changed</summary>
        public event Action Changed;

        public TrayApp(bool startInTray, bool preReleases, string testZip)
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

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open BPGP Updater", null, (s, e) => ShowWindow());
            menu.Items.Add("Check for updates", null, async (s, e) => await CheckAsync(true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Exit());
            tray = new NotifyIcon
            {
                Icon = AppIcon.Get(),
                Text = "BPGP Updater",
                ContextMenuStrip = menu,
                Visible = startInTray || Autostart.IsEnabled,
            };
            tray.DoubleClick += (s, e) => ShowWindow();
            tray.BalloonTipClicked += (s, e) => ShowWindow();

            timer = new Timer { Interval = (int)CheckInterval.TotalMilliseconds };
            timer.Tick += async (s, e) => await CheckAsync(true);
            timer.Start();

            if (!startInTray) ShowWindow();
            var check = CheckAsync(startInTray);
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

        public void ShowWindow()
        {
            if (form == null || form.IsDisposed) form = new MainForm(this);
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            // The tray icon only stays while the window is open when "Start with Windows" is on
            tray.Visible = Autostart.IsEnabled;
        }

        /// <summary>The window was minimized: it always goes to the tray instead of the taskbar</summary>
        public void WindowMinimized()
        {
            tray.Visible = true;
            if (!settings.ShownTrayHint)
            {
                tray.ShowBalloonTip(5000, "BPGP Updater", "BPGP Updater is still running here. Double-click the icon to open it.", ToolTipIcon.Info);
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
                form = null; // it's closing itself
                Exit();
            }
        }

        public void SetAutostart(bool enabled)
        {
            Autostart.Set(enabled);
            tray.Visible = enabled || form == null || !form.Visible;
            Changed?.Invoke();
        }

        public bool SetRetailFolder(string picked)
        {
            var retail = WowLocator.ToRetailFolder(picked);
            if (retail == null) return false;
            RetailFolder = settings.RetailFolder = retail;
            settings.Save();
            RefreshInstalled();
            SetStatus(UpdateAvailable ? "Ready to install." : Status);
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
                if (testZip != null)
                {
                    Latest = await Task.Run(() => GitHubReleases.FromLocalZip(testZip));
                }
                else
                {
                    var latest = await Task.Run(() => GitHubReleases.GetLatest(preReleases));
                    Latest = latest.Addon;
                    updater = latest.Updater;
                }
                RefreshInstalled();
                if (SelfUpdater.HasUpdate(updater))
                {
                    await UpdateSelfAsync(updater);
                    return;
                }
                SetStatus(CheckResult());
                if (notify && UpdateAvailable && settings.NotifiedVersion != Latest.Tag)
                {
                    tray.Visible = true;
                    tray.ShowBalloonTip(10000, "BPGP update", "BPGP " + Latest.Tag + " is available. Click here to update.", ToolTipIcon.Info);
                    settings.NotifiedVersion = Latest.Tag;
                    settings.Save();
                }
            }
            catch (Exception e) when (e is WebException || e is IOException || e is SerializationException)
            {
                SetStatus(testZip != null ? "Couldn't read the test zip: " + e.Message : "Couldn't reach GitHub: " + e.Message);
            }
            catch (Exception e) when (testZip != null && (e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException))
            {
                SetStatus("Couldn't read the test zip: " + e.Message);
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
            if (RetailFolder == null) return "World of Warcraft wasn't found. Click Browse to pick its folder.";
            if (Installed.State == AddonState.Linked) return "AddOns\\BPGP is linked to a git clone (developer setup). The updater leaves it alone.";
            return UpdateAvailable ? "BPGP " + Latest.Tag + " is available." : "BPGP is up to date.";
        }

        private async Task UpdateSelfAsync(Release release)
        {
            SetStatus("Updating BPGP Updater to " + release.Version + "…");
            try
            {
                bool inTray = form == null || !form.Visible;
                await Task.Run(() => SelfUpdater.Apply(release, inTray));
                Exit();
            }
            catch (Exception e) when (e is WebException || e is IOException || e is UnauthorizedAccessException)
            {
                // Not fatal: the addon update still works with this version
                SetStatus("Couldn't update BPGP Updater itself (" + e.Message + "). Download the new one from the releases page.");
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
                SetStatus("No permission to write to the AddOns folder. Close WoW, or run BPGP Updater as administrator.");
            }
            catch (Exception e) when (e is WebException || e is IOException || e is InvalidDataException || e is InvalidOperationException)
            {
                SetStatus("Install failed: " + e.Message);
            }
            finally
            {
                try { File.Delete(zip); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                Busy = false;
                Changed?.Invoke();
            }
        }

        public void OpenReleasesPage()
        {
            var url = Latest != null && !string.IsNullOrEmpty(Latest.PageUrl) ? Latest.PageUrl : GitHubReleases.ReleasesPage;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void RefreshInstalled()
        {
            Installed = RetailFolder != null
                ? AddonInstaller.Inspect(WowLocator.AddOnsFolder(RetailFolder))
                : new InstalledAddon { State = AddonState.NotInstalled };
        }

        private static bool IsWowRunning()
        {
            return new[] { "Wow", "WowT", "WowB" }.Any(name => Process.GetProcessesByName(name).Length > 0);
        }

        private void SetStatus(string text)
        {
            Status = text;
            Changed?.Invoke();
        }

        private void Exit()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            if (form != null && !form.IsDisposed) form.CloseForExit();
            ExitThread();
        }
    }

    internal static class AppIcon
    {
        public static Icon Get()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BPGPUpdater.app.ico");
            return stream != null ? new Icon(stream) : SystemIcons.Application;
        }
    }
}
