using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BPGPUpdater
{
    /// <summary>
    /// The window, in the addon's style: tabs BPGP (install / update), Raids (the raid schedule), Settings and About.
    /// All state lives in TrayApp; the window can be closed and opened again.
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");
        /// <summary>The first entry of "I am": no name picked</summary>
        private const string NoName = "(pick your name)";

        private readonly TrayApp app;
        private readonly DispatcherTimer clock;
        private bool exiting;
        /// <summary>Set while UpdateView fills the controls, so their change events don't write back</summary>
        private bool updating;
        private string changelogShown;

        public bool IsClosed { get; private set; }

        internal MainWindow(TrayApp app)
        {
            this.app = app;
            InitializeComponent();
            Icon = AppIcon.GetImage();
            TitleVersion.Text = "v" + SelfUpdater.CurrentVersionText;
            AboutVersion.Text = "Version " + SelfUpdater.CurrentVersionText + (Packaging.IsStoreInstall ? " (Microsoft Store)" : "");
            if (!Autostart.IsAvailable)
            {
                // Store install: no Run key; the Store keeps the app itself up to date
                AutostartBox.Visibility = Visibility.Collapsed;
                AutostartNote.Margin = new Thickness(0);
                AutostartNote.Text = "Installed from the Microsoft Store, which keeps BPGP Updater itself up to date. Open it from the Start menu; minimizing sends it next to the clock, and while it runs it checks for a new BPGP every 6 hours and reminds you two hours before a raid.";
            }
            if (app.PreReleaseSwitch) BetaNote.Text = "Started with --prerelease: betas are always on.";

            MinimizeButton.Click += (s, e) => WindowState = WindowState.Minimized;
            CloseButton.Click += (s, e) => Close();
            InstallButton.Click += async (s, e) => await app.InstallAsync();
            CheckButton.Click += async (s, e) => await app.CheckAsync(false);
            UninstallButton.Click += async (s, e) => await Uninstall();
            BrowseButton.Click += (s, e) => Browse();
            AutostartBox.Click += (s, e) => app.SetAutostart(AutostartBox.IsChecked == true);
            BetaBox.Click += async (s, e) => await app.SetBetaChannelAsync(BetaBox.IsChecked == true);
            SaveLinkButton.Click += (s, e) => SaveScheduleLink();
            ScheduleLinkBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) SaveScheduleLink(); };
            CharacterBox.SelectionChanged += (s, e) =>
            {
                if (!updating && CharacterBox.SelectedItem is string name) app.SetCharacterName(name == NoName ? null : name);
            };
            RefreshScheduleButton.Click += async (s, e) => await app.RefreshScheduleAsync();
            ScheduleSettingsButton.Click += (s, e) => Tabs.SelectedItem = SettingsTab;
            RestoreButton.Click += async (s, e) => await Restore();
            OpenBackupsButton.Click += (s, e) => OpenBackups();
            RepoLink.Click += (s, e) => Open("https://github.com/" + GitHubReleases.AddonRepository);
            ReleasesLink.Click += (s, e) => Open(GitHubReleases.AddonReleasesPage);
            UpdaterLink.Click += (s, e) => Open("https://github.com/" + GitHubReleases.UpdaterRepository);
            IssuesLink.Click += (s, e) => Open("https://github.com/" + GitHubReleases.UpdaterRepository + "/issues");

            // The countdown to the next raid
            clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            clock.Tick += (s, e) => UpdateRaids();
            clock.Start();

            app.Changed += OnChanged;
            Closed += (s, e) => { IsClosed = true; clock.Stop(); app.Changed -= OnChanged; };
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
            updating = true;
            try
            {
                UpdateAddon();
                UpdateRaids();
                UpdateSettings();
            }
            finally
            {
                updating = false;
            }
        }

        private void UpdateAddon()
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

            UpdateChangelog();
            StatusText.Text = app.Status ?? "";
            StatusText.Foreground = (Brush)FindResource(app.StatusIsError ? "BadBrush" : "MutedBrush");
        }

        /// <summary>The release notes since the installed version, rebuilt only when the releases change</summary>
        private void UpdateChangelog()
        {
            var releases = app.Changelog;
            var shown = string.Join(" ", releases.Select(r => r.Tag));
            if (shown == changelogShown) return;
            changelogShown = shown;

            ChangelogHeading.Text = releases.Count > 1 ? "What's new since " + app.Installed.VersionText
                : releases.Count == 1 ? "What's new in " + releases[0].Tag
                : "What's new";
            ChangelogText.Inlines.Clear();
            if (releases.Count == 0)
            {
                ChangelogText.Inlines.Add(new Run("Nothing to show yet."));
                return;
            }
            foreach (var release in releases)
            {
                if (releases.Count > 1) MarkdownView.AddTitle(ChangelogText.Inlines, release.Tag, this);
                MarkdownView.Add(ChangelogText.Inlines, string.IsNullOrWhiteSpace(release.Body) ? "No release notes." : release.Body, this);
            }
        }

        private void UpdateRaids()
        {
            bool linked = !string.IsNullOrEmpty(app.ScheduleLink);
            NoSchedulePanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            NextRaidPanel.Visibility = RaidListPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            RefreshScheduleButton.IsEnabled = !app.ScheduleBusy;
            RaidsStatusText.Text = app.ScheduleStatus ?? "";
            RaidsStatusText.Foreground = (Brush)FindResource(app.ScheduleStatusIsError ? "BadBrush" : "MutedBrush");
            if (!linked) return;

            var now = DateTime.Now;
            var raids = app.UpcomingRaids;
            var next = raids.FirstOrDefault();
            if (next == null)
            {
                NextRaidValue.Text = "–";
                NextRaidNote.Text = app.Schedule == null ? (app.ScheduleBusy ? "Reading the sheet" : "The sheet couldn't be read") : "No raids planned";
                NextRaidCount.Text = "–";
                NextRaidMe.Text = NextRaidAbsent.Text = "";
            }
            else
            {
                NextRaidValue.Text = next.Start.ToString("ddd d MMM, HH:mm", English);
                NextRaidNote.Text = Countdown(next, now) + (next.Note.Length > 0 ? " · " + next.Note : "");
                NextRaidCount.Text = next.Attending.Count + " / " + (next.Attending.Count + next.Absent.Count);
                var me = app.IsAttending(next);
                NextRaidMe.Text = me == true ? "You're in" : me == false ? "You're marked absent"
                    : app.CharacterName == null ? "Pick your name under Settings" : "No answer from you yet";
                NextRaidMe.Foreground = (Brush)FindResource(me == true ? "GoodBrush" : me == false ? "BadBrush" : "MutedBrush");
                NextRaidAbsent.Text = next.Absent.Count > 0 ? "Not coming: " + string.Join(", ", next.Absent) : "Everyone is coming.";
            }

            RaidList.Children.Clear();
            foreach (var raid in raids) RaidList.Children.Add(RaidRow(raid));
            if (raids.Count == 0) RaidList.Children.Add(new TextBlock { Style = (Style)FindResource("Muted"), Text = "Nothing planned yet." });
        }

        /// <summary>"Tonight, in 2 h 5 min", "Tomorrow", "In 3 days", or "Now" while it runs</summary>
        private static string Countdown(Raid raid, DateTime now)
        {
            if (raid.Start <= now) return "Now, until " + raid.End.ToString("HH:mm");
            var until = raid.Start - now;
            if (until < TimeSpan.FromHours(1)) return "In " + Math.Max(1, (int)Math.Ceiling(until.TotalMinutes)) + " min";
            if (raid.Start.Date == now.Date) return "Tonight, in " + (int)until.TotalHours + " h " + until.Minutes + " min";
            if (raid.Start.Date == now.Date.AddDays(1)) return "Tomorrow";
            return "In " + (raid.Start.Date - now.Date).Days + " days";
        }

        /// <summary>One line of the raid list: date, note, how many are coming, and you</summary>
        private UIElement RaidRow(Raid raid)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            var me = app.IsAttending(raid);
            AddCell(grid, 0, raid.Start.ToString("ddd d MMM", English), "TextBrush", false);
            AddCell(grid, 1, raid.Note, "MutedBrush", false);
            AddCell(grid, 2, raid.Attending.Count + " / " + (raid.Attending.Count + raid.Absent.Count), "PaleGoldBrush", true);
            AddCell(grid, 3, me == true ? "In" : me == false ? "Out" : "", me == true ? "GoodBrush" : "BadBrush", true);
            if (raid.Absent.Count > 0) grid.ToolTip = "Not coming: " + string.Join(", ", raid.Absent);
            return new Border
            {
                BorderBrush = (Brush)FindResource("BronzeDarkBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 5, 0, 5),
                Background = Brushes.Transparent, // so the tooltip shows over the whole row
                Child = grid,
            };
        }

        private void AddCell(Grid grid, int column, string text, string brush, bool right)
        {
            var cell = new TextBlock
            {
                Text = text,
                Foreground = (Brush)FindResource(brush),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        private void UpdateSettings()
        {
            FolderBox.Text = app.RetailFolder ?? "Not found: click Browse and pick your World of Warcraft folder";
            AutostartBox.IsChecked = Autostart.IsEnabled;
            BetaBox.IsChecked = app.PreReleases;
            BetaBox.IsEnabled = !app.PreReleaseSwitch && !app.Busy;

            // Raid schedule: don't overwrite what's being typed
            if (!ScheduleLinkBox.IsKeyboardFocusWithin) ScheduleLinkBox.Text = app.ScheduleLink ?? "";
            var names = new List<string> { NoName };
            if (app.Schedule != null) names.AddRange(app.Schedule.Players);
            // A saved name stays visible while the sheet can't be read
            if (app.CharacterName != null && !names.Contains(app.CharacterName, StringComparer.OrdinalIgnoreCase)) names.Add(app.CharacterName);
            if (!names.SequenceEqual(CharacterBox.Items.Cast<object>().Select(i => i as string))) CharacterBox.ItemsSource = names;
            CharacterBox.SelectedItem = names.FirstOrDefault(n => n != NoName && string.Equals(n, app.CharacterName, StringComparison.OrdinalIgnoreCase)) ?? NoName;
            CharacterBox.IsEnabled = names.Count > 1;
            ScheduleStatusText.Text = app.ScheduleStatus ?? (string.IsNullOrEmpty(app.ScheduleLink) ? "No link set: the Raids tab stays empty." : "");
            ScheduleStatusText.Foreground = (Brush)FindResource(app.ScheduleStatusIsError ? "BadBrush" : "MutedBrush");

            // Backups: keep the picked one selected when the list is refreshed
            var backups = app.Backups;
            var picked = BackupBox.SelectedItem as BackupInfo;
            if (!backups.Select(b => b.Folder).SequenceEqual(BackupBox.Items.Cast<BackupInfo>().Select(b => b.Folder)))
            {
                BackupBox.ItemsSource = backups;
                BackupBox.SelectedItem = backups.FirstOrDefault(b => picked != null && b.Folder == picked.Folder) ?? backups.FirstOrDefault();
            }
            BackupBox.IsEnabled = backups.Count > 0 && !app.Busy;
            RestoreButton.IsEnabled = backups.Count > 0 && app.RetailFolder != null && !app.Busy;
            BackupStatusText.Text = app.BackupStatus ?? (backups.Count == 0 ? "No backups yet: the first is made at the next install, update or uninstall." : "");
            BackupStatusText.Foreground = (Brush)FindResource(app.BackupStatusIsError ? "BadBrush" : "MutedBrush");
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

        private void SaveScheduleLink()
        {
            if (!app.SetScheduleLink(ScheduleLinkBox.Text))
            {
                MessageBox.Show(this, "That isn't a Google Sheets link. Open the raid schedule in your browser and copy the address (it starts with https://docs.google.com/spreadsheets/).",
                    "BPGP Updater", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Keyboard.ClearFocus(); // the box shows the saved link again
            UpdateView();
        }

        private async System.Threading.Tasks.Task Restore()
        {
            var backup = BackupBox.SelectedItem as BackupInfo;
            if (backup == null) return;
            var answer = MessageBox.Show(this, "Put back the BPGP settings from " + backup + "?\n\nYour current BPGP settings are backed up first, so you can go back.",
                "BPGP Updater", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            await app.RestoreAsync(backup);
        }

        private static void OpenBackups()
        {
            Directory.CreateDirectory(SettingsBackup.DefaultFolder);
            Open(SettingsBackup.DefaultFolder);
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
