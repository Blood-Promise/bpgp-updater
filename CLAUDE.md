# BPGP Updater: notes for Claude

Windows app that installs and updates the BPGP addon (repo `jurrebakker/bpgp`, cloned at `C:\Users\patri\dev\bpgp`) from its GitHub releases. Split out of the bpgp repo's `updater/` folder on 2026-10-18, with its history. See [README.md](README.md) for what it does.

## Layout

- `Program.cs`: entry point, single-instance mutex, command-line switches.
- `TrayApp.cs`: all state and logic (WoW folder, installed and latest version, check, install, uninstall, self-update, tray icon, 6-hour timer).
- `MainWindow.xaml(.cs)`: the window, a view on TrayApp, with tabs BPGP, Settings and About. Add new tabs here.
- `UninstallDialog.xaml(.cs)`: Remove BPGP / Remove BPGP and settings / Cancel.
- `Theme.xaml`: the addon's look (bronze frames, gold Marcellus titles, red buttons, tabs hanging below the frame).
- `GitHubReleases.cs`: release lookup. `AddonRepository` is the addon's repo (change it when the addon moves), `UpdaterRepository` is this repo.
- `AddonInstaller.cs`: inspect, install (extract next to AddOns, swap, roll back), uninstall, and find/delete the settings (`WTF\Account\...\SavedVariables\BPGP.lua` and `.lua.bak` only).
- `WowLocator.cs`, `SelfUpdater.cs`, `Settings.cs` (`%APPDATA%\BPGPUpdater\settings.json` and the Start with Windows Run key), `SemVersion.cs`.

## Rules

- C# 7.3 on .NET Framework 4.8; no NuGet runtime dependencies, so it stays a single exe. Warnings are errors.
- Never touch a junction/symlinked `AddOns\BPGP`, and never recursively delete through a link.
- Removing settings is refused while WoW runs (WoW writes them back).
- UI text is English, like the addon.

## Git and releases

- Repo: `Blood-Promise/bpgp-updater`, default branch `main`. Work on a branch and open a PR; commit or push only when the user asks.
- Commit messages: a short title, a body listing what changed, and the Co-Authored-By line.
- Release: push a tag `v<version>` (e.g. `v1.0.1`). `.github/workflows/release.yml` builds the exe and the Store MSIX (`Package/build-msix.ps1`) and creates the release. Installed exes pick it up as a self-update.
- **Microsoft Store:** package identity `PatrickHopman.BPGPUpdater`, publisher `CN=B7FBC900-829F-4221-9B58-0222876AC75C`, Store ID `9NXVNH273470` (in `Package/AppxManifest.xml`; must match Partner Center). `Packaging.IsStoreInstall` turns off the self-update and hides Start with Windows in the Store version. The first submission was manual (2026-10-18). Since then `release.yml` submits the MSIX of every non-alpha/beta tag with `microsoft/microsoft-store-apppublisher` + `msstore publish`, using the secrets `AZURE_AD_TENANT_ID`, `AZURE_AD_APPLICATION_CLIENT_ID`, `AZURE_AD_APPLICATION_SECRET` and `SELLER_ID` (Entra app `bpgp-updater-github`, Manager in Partner Center; the user sets the client secret, never Claude). `store-check.yml` (manual or on PRs that change it) signs in and shows the submission status without publishing.

## Checking changes (this machine)

- Build: `dotnet publish BPGPUpdater.csproj -c Release -o out` (.NET 8 SDK installed). Stop a running BPGPUpdater first if you build into `out`, or publish to a temp folder.
- Look: `out\BPGPUpdater.exe --screenshot <path>` and view the PNGs.
- Install/update without a release: `--test-zip <zip>`, where the zip has `BPGP/BPGP.toc` with `## Version: v…`.
- PowerShell 5.1: pass commit messages and PR bodies through a file (`-F`, `--body-file`). Quotes in arguments get mangled.
