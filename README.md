# BPGP Updater

A small Windows app that installs and updates the [BPGP](https://github.com/jurrebakker/bpgp) World of Warcraft addon from its GitHub releases, so raiders don't need an addon manager. By [Blood Promise](https://github.com/Blood-Promise).

## Install

1. Download **BPGPUpdater.exe** from the [latest release](https://github.com/Blood-Promise/bpgp-updater/releases/latest) and run it. There's nothing to install: it's one exe, and Windows 10/11 already has what it needs (.NET Framework 4.8).
2. Windows may show "Windows protected your PC" the first time, because the exe isn't signed. Click **More info → Run anyway**.
3. It finds your World of Warcraft folder; if not, pick it under **Settings**. Click **Install** on the **BPGP** tab.

Run it again to update, or tick **Start with Windows** under **Settings**. It then waits next to the clock, checks for a new BPGP every 6 hours and reminds you before a raid. The app also updates itself from this repository's releases.

**Uninstall** removes BPGP from the AddOns folder. You choose whether to keep your BPGP settings (the `WTF` SavedVariables) or remove them too.

## What's in it

- **BPGP**: the installed and the latest version, **Install** / **Update**, and the release notes of every version since the one you have.
- **Raids**: the guild's raid schedule from Google Sheets. The next raid with a countdown, how many are coming and who isn't, and the raids after it. Paste the sheet's link under **Settings** and pick your name. Two hours before a raid (20:00) the tray reminds you, and tells you to update BPGP first if there's a new version. There's no reminder when you're marked absent.
- **Settings**:
  - the WoW folder and the raid schedule link;
  - **Start with Windows**;
  - **Get beta versions of BPGP**, for officers who test a release before the raiders;
  - **Backups of your BPGP settings**: before every install, update, uninstall and restore, the app copies `BPGP.lua` (and `.lua.bak`) from the `WTF` folder to `%APPDATA%\BPGPUpdater\backups`. It keeps the last 5; **Restore…** puts one back (with WoW closed).

## How it works

- **Addon releases** come from [jurrebakker/bpgp](https://github.com/jurrebakker/bpgp/releases): every `v*` tag there has a `BPGP-<tag>.zip` built by the BigWigs packager. The repository is set in `GitHubReleases.AddonRepository`.
- **Updater releases** come from this repository: every `v*` tag here has `BPGPUpdater.exe`. Installed updaters replace themselves with the newest one.
- **Pre-releases** (tags with alpha or beta) are skipped, unless **Get beta versions of BPGP** is on under Settings, or the app is started with `--prerelease`.
- **Raid schedule:** the sheet is read through its CSV export (`RaidSchedule.ToCsvUrl`), so it has to be shared as "Anyone with the link can view". Only `docs.google.com/spreadsheets` links are accepted. The sheet is a grid: a row per raid with a note, the date (`21 Oct`, no year) and a count, then a column per player with `1` (coming) or `0` (not). The names come from the header row above the raids. Columns are found by their contents, not their letters. Raids start at 20:00 (`RaidSchedule.StartTime`). The sheet is read at start, every 30 minutes and when the window opens; offline, the last copy is used.
- **Installing:** the zip is extracted next to `AddOns`, then swapped in with a rename. The old folder comes back if anything fails.
- **Developer setup:** a junction or symlinked `AddOns\BPGP` is never touched.

## Development

- C# **WPF on .NET Framework 4.8**, one exe without NuGet runtime dependencies. Windows Forms is used only for the tray icon and the folder picker.
- The look follows the addon: `Theme.xaml` (colours and styles), `MainWindow.xaml` (tabs **BPGP**, **Raids**, **Settings**, **About**), `UninstallDialog.xaml`. The title font is Marcellus (SIL Open Font License, `Fonts/OFL.txt`).
- Build with the .NET 8 SDK:
  ```
  dotnet publish BPGPUpdater.csproj -c Release -o out
  ```
- Tests (MSTest, in `Tests/`): versions, finding WoW, install/uninstall, the raid schedule parser (`Tests/Fixtures/raid-schedule.csv` is built like the guild's sheet), release notes and settings backups:
  ```
  dotnet test Tests/BPGPUpdater.Tests.csproj
  ```
- Test switches:
  - `--test-zip <zip>`: a local addon zip acts as the latest release.
  - `--prerelease`: also use alpha and beta releases.
  - `--screenshot <path>`: saves every tab and the uninstall dialog as PNGs, then exits.
- CI: `.github/workflows/build.yml` runs the tests and builds every pull request and push to `main`. The exe and a test MSIX are kept as artifacts for 14 days.
- **Releasing:** push a tag like `v1.0.0`. `.github/workflows/release.yml` builds the exe and the Store package (`BPGPUpdater-<version>.msix`) with that version and creates the release.

### Microsoft Store

The app is also packaged for the Microsoft Store (identity `PatrickHopman.BPGPUpdater`, Store ID `9NXVNH273470`). The Store signs the package, so antivirus and SmartScreen trust it.

- `Package/AppxManifest.xml` holds the package identity from Partner Center; `Package/build-msix.ps1` builds the MSIX with `makeappx` from the Windows SDK. Store versions have four numbers ending in 0: v1.0.1 becomes 1.0.1.0.
- The Store install is detected with `Packaging.IsStoreInstall`:
  - it never replaces itself, because the Store updates it;
  - "Start with Windows" is hidden there, because Windows ignores the Run key for packaged apps.
- **First submission:** upload the `.msix` from a release by hand in Partner Center. Updates through GitHub Actions only work once the app is live in the Store.
- **Updates:** every normal `v*` tag also submits the MSIX to the Store (`msstore publish` in `release.yml`); alpha/beta tags don't.
  - It uses four repository secrets: `AZURE_AD_TENANT_ID`, `AZURE_AD_APPLICATION_CLIENT_ID`, `AZURE_AD_APPLICATION_SECRET` and `SELLER_ID`. They belong to the Entra app `bpgp-updater-github`, which has the Manager role in Partner Center.
  - **Store check** (Actions → Store check → Run workflow) tests the connection without publishing.

## Privacy

BPGP Updater collects no personal data: no accounts, analytics or tracking. It only talks to GitHub (releases) and, if you set a raid schedule link, Google Sheets. See the [privacy policy](PRIVACY.md).

## License

Apache License 2.0, see [LICENSE](LICENSE). Marcellus font: SIL Open Font License 1.1, see [Fonts/OFL.txt](Fonts/OFL.txt).
