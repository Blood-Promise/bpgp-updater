# BPGP Updater

A small Windows app that installs and updates the [BPGP](https://github.com/jurrebakker/bpgp) World of Warcraft addon from its GitHub releases, so raiders don't need an addon manager. By [Blood Promise](https://github.com/Blood-Promise).

## Install

1. Download **BPGPUpdater.exe** from the [latest release](https://github.com/Blood-Promise/bpgp-updater/releases/latest) and run it. There's nothing to install: it's one exe, and Windows 10/11 already has what it needs (.NET Framework 4.8).
2. Windows may show "Windows protected your PC" the first time, because the exe isn't signed. Click **More info → Run anyway**.
3. It finds your World of Warcraft folder; if not, pick it under **Settings**. Click **Install** on the **BPGP** tab.

Run it again to update, or tick **Start with Windows** under **Settings**. It then waits next to the clock and checks for a new BPGP every 6 hours. The app also updates itself from this repository's releases.

**Uninstall** removes BPGP from the AddOns folder. You choose whether to keep your BPGP settings (the `WTF` SavedVariables) or remove them too.

## How it works

- **Addon releases** come from [jurrebakker/bpgp](https://github.com/jurrebakker/bpgp/releases): every `v*` tag there has a `BPGP-<tag>.zip` built by the BigWigs packager. The repository is set in `GitHubReleases.AddonRepository`.
- **Updater releases** come from this repository: every `v*` tag here has `BPGPUpdater.exe`. Installed updaters replace themselves with the newest one.
- **Pre-releases** (tags with alpha or beta) are skipped, unless the app is started with `--prerelease`.
- **Installing:** the zip is extracted next to `AddOns`, then swapped in with a rename. The old folder comes back if anything fails.
- **Developer setup:** a junction or symlinked `AddOns\BPGP` is never touched.

## Development

- C# **WPF on .NET Framework 4.8**, one exe without NuGet runtime dependencies. Windows Forms is used only for the tray icon and the folder picker.
- The look follows the addon: `Theme.xaml` (colours and styles), `MainWindow.xaml` (tabs **BPGP**, **Settings**, **About**), `UninstallDialog.xaml`. The title font is Marcellus (SIL Open Font License, `Fonts/OFL.txt`).
- Build with the .NET 8 SDK:
  ```
  dotnet publish BPGPUpdater.csproj -c Release -o out
  ```
- Test switches:
  - `--test-zip <zip>`: a local addon zip acts as the latest release.
  - `--prerelease`: also use alpha and beta releases.
  - `--screenshot <path>`: saves every tab and the uninstall dialog as PNGs, then exits.
- CI: `.github/workflows/build.yml` builds every pull request and push to `main` (the exe is kept as an artifact for 14 days).
- **Releasing:** push a tag like `v1.0.0`. `.github/workflows/release.yml` builds the exe with that version and creates the release.

## License

Apache License 2.0, see [LICENSE](LICENSE). Marcellus font: SIL Open Font License 1.1, see [Fonts/OFL.txt](Fonts/OFL.txt).
