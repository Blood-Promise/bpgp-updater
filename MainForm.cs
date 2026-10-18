using System;
using System.Drawing;
using System.Windows.Forms;

namespace BPGPUpdater
{
    /// <summary>
    /// The window: WoW folder, installed and latest version, Install / Update, the changelog of the
    /// latest release, "Start with Windows" and a status line. All state lives in TrayApp.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly TrayApp app;
        private readonly TextBox folderBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Button browseButton = new Button { Text = "Browse…", AutoSize = true };
        private readonly Label installedLabel = ValueLabel();
        private readonly Label latestLabel = ValueLabel();
        private readonly Button installButton = new Button { Text = "Install", AutoSize = true, MinimumSize = new Size(140, 0) };
        private readonly Button checkButton = new Button { Text = "Check again", AutoSize = true };
        private readonly LinkLabel releasesLink = new LinkLabel { Text = "Releases page", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 3, 3) };
        private readonly Label changelogLabel = new Label { Text = "What's new", AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        private readonly TextBox changelogBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window };
        private readonly CheckBox autostartBox = new CheckBox { Text = "Start with Windows and check for updates in the background", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        private readonly Label statusLabel = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        private bool exiting;

        public MainForm(TrayApp app)
        {
            this.app = app;
            Text = "BPGP Updater " + SelfUpdater.CurrentVersionText;
            Icon = AppIcon.Get();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(560, 440);
            MinimumSize = new Size(480, 380);
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();

            browseButton.Click += (s, e) => Browse();
            installButton.Click += async (s, e) => await app.InstallAsync();
            checkButton.Click += async (s, e) => await app.CheckAsync(false);
            releasesLink.LinkClicked += (s, e) => app.OpenReleasesPage();
            autostartBox.CheckedChanged += (s, e) => { if (autostartBox.Checked != Autostart.IsEnabled) app.SetAutostart(autostartBox.Checked); };
            app.Changed += OnChanged;
            FormClosed += (s, e) => app.Changed -= OnChanged;

            UpdateView();
        }

        private static Label ValueLabel()
        {
            return new Label { AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
        }

        private void BuildLayout()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            grid.Controls.Add(Caption("World of Warcraft"), 0, 0);
            grid.Controls.Add(folderBox, 1, 0);
            grid.Controls.Add(browseButton, 2, 0);

            grid.Controls.Add(Caption("Installed"), 0, 1);
            grid.Controls.Add(installedLabel, 1, 1);
            grid.SetColumnSpan(installedLabel, 2);

            grid.Controls.Add(Caption("Latest"), 0, 2);
            grid.Controls.Add(latestLabel, 1, 2);
            grid.SetColumnSpan(latestLabel, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0), WrapContents = false };
            buttons.Controls.Add(installButton);
            buttons.Controls.Add(checkButton);
            buttons.Controls.Add(releasesLink);
            grid.Controls.Add(buttons, 0, 3);
            grid.SetColumnSpan(buttons, 3);

            grid.Controls.Add(changelogLabel, 0, 4);
            grid.SetColumnSpan(changelogLabel, 3);
            grid.Controls.Add(changelogBox, 0, 5);
            grid.SetColumnSpan(changelogBox, 3);
            grid.Controls.Add(autostartBox, 0, 6);
            grid.SetColumnSpan(autostartBox, 3);
            grid.Controls.Add(statusLabel, 0, 7);
            grid.SetColumnSpan(statusLabel, 3);

            for (int row = 0; row < 8; row++)
                grid.RowStyles.Add(row == 5 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));

            Controls.Add(grid);
            AcceptButton = installButton;
        }

        private void OnChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(UpdateView));
            else UpdateView();
        }

        private void UpdateView()
        {
            folderBox.Text = app.RetailFolder ?? "Not found: click Browse and pick your World of Warcraft folder";
            installedLabel.Text = InstalledText();
            latestLabel.Text = app.Latest != null ? app.Latest.Tag : (app.Busy ? "Checking…" : "Unknown");

            bool canInstall = app.UpdateAvailable && !app.Busy;
            installButton.Enabled = canInstall;
            installButton.Text = InstallText();
            checkButton.Enabled = !app.Busy;
            browseButton.Enabled = !app.Busy;

            changelogLabel.Text = app.Latest != null ? "What's new in " + app.Latest.Tag : "What's new";
            changelogBox.Text = app.Latest != null && app.Latest.Body != null
                ? app.Latest.Body.Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
                : "";

            if (autostartBox.Checked != Autostart.IsEnabled) autostartBox.Checked = Autostart.IsEnabled;
            statusLabel.Text = app.Status ?? "";
        }

        private string InstalledText()
        {
            if (app.RetailFolder == null) return "Unknown";
            var installed = app.Installed;
            switch (installed.State)
            {
                case AddonState.NotInstalled: return "Not installed";
                case AddonState.DevBuild: return "Development build (copy of the source)";
                case AddonState.Linked: return "Linked to a git clone (developer setup)";
                default: return installed.VersionText ?? "Unknown version";
            }
        }

        private string InstallText()
        {
            if (app.Latest == null || app.Installed.State == AddonState.NotInstalled || app.Installed.State == AddonState.DevBuild) return "Install";
            if (app.UpdateAvailable) return "Update to " + app.Latest.Tag;
            return "Up to date";
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Pick your World of Warcraft folder (the one with _retail_ in it).", ShowNewFolderButton = false })
            {
                if (app.RetailFolder != null) dialog.SelectedPath = app.RetailFolder;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!app.SetRetailFolder(dialog.SelectedPath))
                {
                    MessageBox.Show(this, "That folder doesn't contain _retail_\\Interface\\AddOns. Pick the World of Warcraft folder.", "BPGP Updater", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>Closes the window for real, when the app exits</summary>
        public void CloseForExit()
        {
            exiting = true;
            Close();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Minimizing always sends the app to the tray, not the taskbar
            if (WindowState == FormWindowState.Minimized && Visible)
            {
                Hide();
                app.WindowMinimized();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // With "Start with Windows" on, closing the window keeps the app in the tray
            if (!exiting && e.CloseReason == CloseReason.UserClosing && Autostart.IsEnabled)
            {
                e.Cancel = true;
                Hide();
                app.WindowClosed();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (!exiting) app.WindowClosed();
        }
    }
}
