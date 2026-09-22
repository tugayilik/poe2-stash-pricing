# Security policy

## Supported versions

Only the [latest release](https://github.com/tugayilik/poe2-stash-pricing/releases/latest) gets fixes. Please check that a problem still happens there before reporting it.

## Reporting a vulnerability

Please **don't open a public issue** for a security problem.

Report it privately instead: open the repository's **Security** tab and click **Report a vulnerability** ([direct link](https://github.com/tugayilik/poe2-stash-pricing/security/advisories/new)). Describe what an attacker could do, and how to reproduce it (the app version, and the steps or a sample file).

If that button is not available, open an issue titled "Security contact" **without any details**, and a private way to send them will be arranged.

You can expect a first answer within a week. Once a fix is released, the report is credited in the release notes unless you prefer otherwise.

## What the app does (scope)

Knowing what the app touches helps judge what counts as a vulnerability:

- **Network:** it only talks to `https://poe.ninja` (prices and leagues) and GitHub (`api.github.com` for this repository's latest release; the release's own files when you click **Update**), over TLS with normal certificate checks. It sends nothing about you or your stash; the only thing in the requests is the league name and the app version (in the User-Agent).
- **Updates:** only files of this repository's release are downloaded. The zip and the exe inside it must match the release's `SHA256SUMS.txt` before the exe is swapped; the previous exe is kept as `PoeStashPricer.exe.old` until the new one has started.
- **Input:** while scanning it moves the mouse and presses Ctrl+C, and only while the Path of Exile 2 window is in front. It stops as soon as another window comes to the front, so the input can't reach another program.
- **Clipboard:** it reads what the game copies for each item, then puts back the text that was on the clipboard before, kept out of the Windows clipboard history and the cloud clipboard.
- **Screen:** it takes screenshots of the game window to find the stash. They are kept in memory only and never written to disk or sent anywhere.
- **Files:** it only writes to `%APPDATA%\PoeStashPricer\` (settings, learned tabs, last scans, learned digits, a diagnostic log). The log holds the app's own messages and the process name of the window in front, never window titles or clipboard contents.
- **Global hotkeys:** it registers the scan and overlay keys (F7/F8 by default) with Windows.

Examples of what is in scope: the app sending input to a window other than the game, data from poe.ninja or GitHub or from the files in `%APPDATA%\PoeStashPricer\` making the app run code or write outside that folder, and anything that leaks clipboard contents or screenshots.

Out of scope: Grinding Gear Games' rules on automated input (see the README), problems in poe.ninja itself, and anything that needs an attacker who can already run programs as you on your PC.

## Verifying a download

The exe is not code-signed. Every release lists the SHA-256 of its files (also in `SHA256SUMS.txt` for releases built by GitHub Actions). Check yours in PowerShell:

```powershell
Get-FileHash .\PoeStashPricer-vX.Y.Z.zip
```

Releases from 1.3.2 on are built by [GitHub Actions](.github/workflows/build.yml) from the tagged source and carry a signed build provenance attestation. With the [GitHub CLI](https://cli.github.com/) you can check that a zip or exe was built by this repository's workflow:

```powershell
gh attestation verify .\PoeStashPricer-vX.Y.Z.zip --repo tugayilik/poe2-stash-pricing
```
