using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace BPGPUpdater
{
    /// <summary>
    /// The window, in the addon's style: tabs BPGP (install / update), Settings and About.
    /// All state lives in TrayApp; the window can be closed and opened again.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly TrayApp app;
        private bool exiting;

        public bool IsClosed { get; private set; }

        internal MainWindow(TrayApp app)
        {
            this.app = app;
            InitializeComponent();
            Icon = AppIcon.GetImage();
            TitleVersion.Text = "v" + SelfUpdater.CurrentVersionText;
            AboutVersion.Text = "Version " + SelfUpdater.CurrentVersionText;

            MinimizeButton.Click += (s, e) => WindowState = WindowState.Minimized;
            CloseButton.Click += (s, e) => Close();
            InstallButton.Click += async (s, e) => await app.InstallAsync();
            CheckButton.Click += async (s, e) => await app.CheckAsync(false);
            UninstallButton.Click += async (s, e) => await Uninstall();
            BrowseButton.Click += (s, e) => Browse();
            AutostartBox.Click += (s, e) => app.SetAutostart(AutostartBox.IsChecked == true);
            RepoLink.Click += (s, e) => Open("https://github.com/" + GitHubReleases.AddonRepository);
            ReleasesLink.Click += (s, e) => Open(GitHubReleases.AddonReleasesPage);
            UpdaterLink.Click += (s, e) => Open("https://github.com/" + GitHubReleases.UpdaterRepository);
            IssuesLink.Click += (s, e) => Open("https://github.com/" + GitHubReleases.UpdaterRepository + "/issues");

            app.Changed += OnChanged;
            Closed += (s, e) => { IsClosed = true; app.Changed -= OnChanged; };
            UpdateView();
        }

        private void OnChanged()
        {
            if (IsClosed) return;
            if (Dispatcher.CheckAccess()) UpdateView();
            else Dispatcher.BeginInvoke(new Action(UpdateView));
        }

        private void UpdateView()
        {
            var installed = app.Installed;
            InstalledValue.Text = InstalledVersionText(installed);
            InstalledNote.Text = InstalledNoteText(installed);
            LatestValue.Text = app.Latest != null ? app.Latest.Tag : (app.Busy ? "…" : "–");
            LatestNote.Text = app.Latest != null
                ? (app.UpdateAvailable ? "Update available" : "")
                : (app.Busy ? "Checking GitHub" : "No release found");

            InstallButton.Content = InstallText();
            InstallButton.IsEnabled = app.UpdateAvailable && !app.Busy;
            CheckButton.IsEnabled = !app.Busy;
            UninstallButton.IsEnabled = app.CanUninstall && !app.Busy;
            BrowseButton.IsEnabled = !app.Busy;

            ChangelogHeading.Text = app.Latest != null ? "What's new in " + app.Latest.Tag : "What's new";
            ChangelogText.Text = app.Latest != null && !string.IsNullOrWhiteSpace(app.Latest.Body)
                ? app.Latest.Body.Replace("\r\n", "\n")
                : "Nothing to show yet.";

            FolderBox.Text = app.RetailFolder ?? "Not found: click Browse and pick your World of Warcraft folder";
            AutostartBox.IsChecked = Autostart.IsEnabled;
            StatusText.Text = app.Status ?? "";
            StatusText.Foreground = (Brush)FindResource(app.StatusIsError ? "BadBrush" : "MutedBrush");
        }

        private string InstalledVersionText(InstalledAddon installed)
        {
            if (app.RetailFolder == null) return "?";
            switch (installed.State)
            {
                case AddonState.NotInstalled: return "None";
                case AddonState.DevBuild: return "Dev";
                case AddonState.Linked: return "Git";
                default: return installed.VersionText ?? "?";
            }
        }

        private string InstalledNoteText(InstalledAddon installed)
        {
            if (app.RetailFolder == null) return "World of Warcraft not found (Settings)";
            switch (installed.State)
            {
                case AddonState.NotInstalled: return "BPGP isn't installed yet";
                case AddonState.DevBuild: return "A copy of the source code";
                case AddonState.Linked: return "Linked to a git clone, left alone";
                default: return app.UpdateAvailable ? "An update is ready" : "Up to date";
            }
        }

        private string InstallText()
        {
            var state = app.Installed.State;
            if (app.Latest == null || state == AddonState.NotInstalled || state == AddonState.DevBuild) return "Install";
            if (app.UpdateAvailable) return "Update to " + app.Latest.Tag;
            return "Up to date";
        }

        private async System.Threading.Tasks.Task Uninstall()
        {
            var dialog = new UninstallDialog(app.SettingsFileCount(), TrayApp.IsWowRunning()) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            await app.UninstallAsync(dialog.Choice == UninstallChoice.AddonAndSettings);
        }

        private void Browse()
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "Pick your World of Warcraft folder (the one with _retail_ in it).", ShowNewFolderButton = false })
            {
                if (app.RetailFolder != null) dialog.SelectedPath = app.RetailFolder;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                if (!app.SetRetailFolder(dialog.SelectedPath))
                {
                    MessageBox.Show(this, "That folder doesn't contain _retail_\\Interface\\AddOns. Pick the World of Warcraft folder.", "BPGP Updater", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void Open(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary>Test switch (--screenshot): saves every tab as a PNG, for checking the look</summary>
        public void SaveScreenshots(string basePath)
        {
            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                Tabs.SelectedIndex = i;
                UpdateLayout();
                Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Render);
                SavePng(Root, basePath + "-" + i + ".png");
            }
            Tabs.SelectedIndex = 0;

            // The uninstall dialog, as it looks with settings found
            var dialog = new UninstallDialog(2, false) { Owner = this };
            dialog.Show();
            dialog.UpdateLayout();
            SavePng((FrameworkElement)dialog.Content, basePath + "-uninstall.png");
            dialog.Close();
        }

        private static void SavePng(FrameworkElement element, string path)
        {
            var dpi = VisualTreeHelper.GetDpi(element);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        /// <summary>Closes the window for real, when the app exits</summary>
        public void CloseForExit()
        {
            exiting = true;
            Close();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            // Minimizing always sends the app to the tray, not the taskbar
            if (WindowState == WindowState.Minimized && IsVisible)
            {
                Hide();
                WindowState = WindowState.Normal;
                app.WindowMinimized();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // With "Start with Windows" on, closing the window keeps the app in the tray
            if (!exiting && Autostart.IsEnabled)
            {
                e.Cancel = true;
                Hide();
                app.WindowClosed();
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (!exiting) app.WindowClosed();
        }
    }
}
