using System.Windows;
using System.Windows.Input;

namespace BPGPUpdater
{
    internal enum UninstallChoice
    {
        Cancel,
        AddonOnly,
        AddonAndSettings,
    }

    /// <summary>
    /// Asks how to uninstall: only the addon (settings kept), the addon and its settings, or cancel.
    /// Removing the settings needs WoW closed: it writes them back when you log out or reload.
    /// </summary>
    public partial class UninstallDialog : Window
    {
        internal UninstallChoice Choice { get; private set; }

        internal UninstallDialog(int settingsFiles, bool wowRunning)
        {
            InitializeComponent();
            SettingsText.Text = settingsFiles > 0
                ? "Remove BPGP and settings also deletes your BPGP settings and local data on this PC (" + settingsFiles + " file" + (settingsFiles == 1 ? "" : "s") +
                  " in the WTF folder). Standings come back by syncing from the officers; an officer's changes that haven't synced to anyone yet are lost."
                : "No BPGP settings were found in the WTF folder.";
            RemoveAllButton.IsEnabled = settingsFiles > 0 && !wowRunning;
            if (wowRunning && settingsFiles > 0)
            {
                WarningText.Text = "Close World of Warcraft to remove the settings: WoW writes them back when you log out or reload.";
                WarningText.Visibility = Visibility.Visible;
            }

            RemoveAllButton.Click += (s, e) => Done(UninstallChoice.AddonAndSettings);
            RemoveButton.Click += (s, e) => Done(UninstallChoice.AddonOnly);
            CancelButton.Click += (s, e) => Done(UninstallChoice.Cancel);
            TitleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        }

        private void Done(UninstallChoice choice)
        {
            Choice = choice;
            DialogResult = choice != UninstallChoice.Cancel;
        }
    }
}
