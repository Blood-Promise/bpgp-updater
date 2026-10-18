# BPGP Updater privacy policy

*Last updated: 18 October 2026*

BPGP Updater is a free, open-source Windows app made by Patrick Hopman for the Blood Promise guild. It installs and updates the BPGP World of Warcraft addon and can show the guild's raid schedule. This policy explains what the app does with data. In short: **BPGP Updater doesn't collect, store or share any personal data about you.** It has no accounts, analytics, tracking or ads.

## What the app sends over the internet

The app only connects to these services, and only to do its job:

| Service | When | What it asks for |
|---|---|---|
| **GitHub** (`api.github.com`, `github.com`) | When it checks for updates: at start, every 6 hours while running, and when you click *Check again* | The list of BPGP and BPGP Updater releases, and the release files it installs |
| **Google Sheets** (`docs.google.com`) | Only if you paste a raid schedule link under *Settings* | That one spreadsheet, read-only, as a CSV file |

Like any internet request, these services see your IP address and a technical identifier, `BPGPUpdater/<version>`. The app sends nothing else: no name, no account, no device ID, no usage data. What GitHub and Google do with requests is covered by their own policies:
- [GitHub General Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)
- [Google Privacy Policy](https://policies.google.com/privacy)

## What the app stores on your PC

Everything stays on your PC. Nothing is sent to us.

- **Settings** in `%APPDATA%\BPGPUpdater\settings.json`:
  - your World of Warcraft folder;
  - the raid schedule link and the name you picked, if you set them;
  - which update you were last told about.
- **A cache** in `%APPDATA%\BPGPUpdater\cache`, the last answers from GitHub and the raid schedule, so the app works with fewer requests and when you're offline.
- **"Start with Windows"**, only if you turn it on: one entry in your Windows startup list (`HKEY_CURRENT_USER\...\Run`). The Microsoft Store version doesn't offer this option.
- **The BPGP addon** in your World of Warcraft `Interface\AddOns\BPGP` folder.

When you uninstall BPGP from the app, it removes `AddOns\BPGP`. Only if you choose *Remove BPGP and settings* does it also delete BPGP's own settings files, `BPGP.lua` and `BPGP.lua.bak` in the `WTF` folder. It never touches other addons or game files.

To remove everything BPGP Updater stored:
1. Turn off *Start with Windows*.
2. Uninstall the app: from the Microsoft Store or Windows *Installed apps*, or by deleting `BPGPUpdater.exe`.
3. Delete the folder `%APPDATA%\BPGPUpdater`.

## Children

The app collects no personal data from anyone, including children.

## Changes

If the app's behaviour changes in a way that affects this policy, we update this page and its date. The history of the page is visible in this repository.

## Contact

Questions or concerns? Open an issue at [github.com/Blood-Promise/bpgp-updater/issues](https://github.com/Blood-Promise/bpgp-updater/issues).
