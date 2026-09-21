# PoE2 Stash Pricer

A small Windows app that scans the special stash tabs of Path of Exile 2 (Currency, Essence, Runes...) on screen and prices the items with [poe.ninja](https://poe.ninja/docs/api). It shows the value of every tab and of your whole stash, and writes the prices over the items in the game.

## Features

- **One key to price a tab.** Open a stash tab and press F7. The app hovers every item, reads it the way you would (Ctrl+C in the game) and writes each stack's value over it: unit price × quantity, in Divine, Exalted or Chaos.
- **Total stash value.** Every scanned tab keeps its last result, so the app always shows what each tab and your whole stash are worth, and updates the numbers when prices move.
- **Finds the stash on its own.** No grid to draw and nothing to calibrate: the stash panel is found in a screenshot of the game, at any resolution (tested from 1280×720 to 3840×2160).
- **Learns your tabs.** The first scan of a special tab remembers where its slots are, what it looks like and what it holds, and names it (Currency, Essence, Abyss...). After that the tab is recognised whenever you open it, even the look-alike Runes sub-tabs.
- **Prices follow you around the stash.** Switch tabs and the overlay switches with you: a scanned tab shows its prices again without rescanning.
- **Always current prices.** Prices come from poe.ninja at start and every 15 minutes. If poe.ninja is down or asks to slow down, the app keeps the last prices and tries again later. When prices changed a tab's value since you scanned it, the overlay says so (e.g. "tab value 189 → 201 div, +6.3%").
- **Reads counts the game doesn't copy.** A few items (Simulacrum, Triskelion...) copy without a stack size; the app reads the number printed on the icon instead, using digits it learned from your other stacks.
- **Accurate and quick.** Every saved slot is checked, touching items are split at their real borders, and a missed spot gets a second, slower look. A full Currency tab takes about 5 seconds.
- **Your keys.** F7/F8 by default, rebindable to any function key or a Ctrl/Alt/Shift combination.
- **Private by design.** Everything stays on your PC; only price lists are downloaded. See [Privacy and security](#privacy-and-security).
- **Portable.** One small exe, no installer, runs on the .NET Framework that comes with Windows 10/11.

## Supported tabs

The app is made for the **special tabs with fixed slots**, where every item type has its own place. Each one is learned on its first scan and gets a name from the items it holds.

| Tab | What it holds | Notes |
|---|---|---|
| **Currency** | Orbs, shards, scrolls, etchers, whetstones, flux... | |
| **Fragments** | Fragments, Simulacrum, Shattered Triskelion... | Its big 2×2 slots are read as one item. Items that copy without a stack size get their count read from the icon. Tested on the *Fragments* sub-tab; the *Tablets* and *Trials* sub-tabs are learned separately if they hold priced items. |
| **Expedition** | Expedition items, Verisium and alloys | |
| **Breach** | Breach items | Learned even with a single item in it. |
| **Abyss** | Abyssal bones and omens | Named Abyss even though it also holds omens. |
| **Essence** | Essences | |
| **Delirium** | Liquid emotions (Liquid Paranoia, Diluted Liquid Ire...) | |
| **Runes** | 5 look-alike sub-tabs: **Runes, Kalguuran Runes, Soul Cores, Idols, Ancient Augments** | Each sub-tab is learned and recognised separately. Kalguuran Runes and Ancient Augments get names like "Runes 2" / "Runes 3"; use **Rename** to name them. |
| **Ritual** | Omens | |

Each of these was scanned and learned in testing at 2560×1440, from full tabs to a tab with a single item.

**Other tabs:**

- **Normal and quad tabs** can be scanned too: prices show over the items, but the tab is not saved and not added to the total stash value (a normal tab can hold two stacks of the same item in different places, which the saved-tab logic would count as one).
- **Map, Unique, Flask and other special tabs are not supported yet.** A scan works like on a normal tab (priced items get their prices, e.g. uniques), but the tab is not saved or added to the total.
- **Gem tab:** not tested. A tab full of uncut gems may be learned as "Gems".
- Items are priced when poe.ninja lists them: currency-like items by name, uniques by name and base type. Rare, magic and unidentified items show "no price".

## Download

**[⬇ Download the latest release](https://github.com/tugayilik/poe2-stash-pricing/releases/latest)**. Get `PoeStashPricer-vX.Y.Z.zip` under *Assets*, unzip it anywhere and run `PoeStashPricer.exe`.

No installation needed: it runs on the .NET Framework 4.8 that comes with Windows 10/11.

> The exe is not signed, so Windows may show "Windows protected your PC" the first time: click **More info → Run anyway**.

Everyone starts from a clean setup: the download contains only the exe, this README and the license. Each user's saved tabs, scan results and learned digits are kept on their own PC in `%APPDATA%\PoeStashPricer\`. Screenshots of your stash are only used during a scan and are never stored. To start over, click **Delete all** in the app; to remove the app, delete the exe and that folder.

## Game settings

- Display mode must be **Windowed Fullscreen / Borderless**. In exclusive fullscreen, screenshots can come out black.
- Game language must be **English**. Item names are matched with poe.ninja in English.
- Any resolution works: the stash panel is found on screen automatically.

## How to use

1. Open the stash in the game, open a tab and press **F7**. The app finds the stash on screen, moves the mouse over each item and reads it with **Ctrl+C**.
2. Don't touch the mouse while it scans. **Esc** or **F7** again stops the scan.
3. Prices appear over the items. **F8** hides or shows the price overlay.

**Tabs are learned automatically.** The first scan of a special tab saves it: where its slots are, how it looks, and a name guessed from its items (Currency, Essence, Runes...). From then on the tab is recognised, its value counts in the total stash value, and its prices come back when you return to it. If a name is wrong, pick the tab in the list and click **Rename**.

Which tabs are learned, and what happens with the others, is listed in [Supported tabs](#supported-tabs).

**Delete** removes a saved tab and its last scan; its next scan learns it again. **Delete all** removes all saved tabs, scan results and learned digits, so the app starts over as if freshly installed (league and currency choices are kept).

The last scan of every tab is kept:

- When you switch to another tab the prices are hidden; when you come back to a scanned tab they come back **without pressing F7**.
- Press F7 again whenever you want to rescan a tab.
- Results survive closing the app.
- Prices are fetched from poe.ninja when the app starts and every 15 minutes while it is open (**Refresh prices** fetches them right away). If poe.ninja can't be reached or asks to slow down, the app keeps the last prices and tries again a few minutes later. Saved scans are always valued at the current prices.

### The app window

- **Top:** the total value of all scanned tabs.
- **Left:** your tabs with their value and last scan time. The tab open in the game is highlighted in gold and marked with ▶.
- **Right:** the items of the tab open in the game (or the one picked on the left) with quantities and prices.
- **League / Show in:** the league to price for, and whether prices are shown in Divine, Exalted, Chaos or automatically. (poe.ninja prices PoE2 per league only, so there is no realm to pick.)
- **Hover delay (ms):** wait between moving onto an item and pressing Ctrl+C. Increase it (60–100) if items are read wrong or missed.
- **Preview:** shows whether the open tab is recognised and which positions will be scanned.

## Hotkeys

| Key | Action |
|---|---|
| F7 | Scan the open tab / stop scanning |
| F8 | Hide / show the price overlay |
| Esc | Stop scanning |

F7 and F8 are the defaults. To use other keys, click **Scan key** or **Overlay key** in the app and press the new key. Function keys (F1–F24) work alone; any other key needs Ctrl, Alt or Shift with it (e.g. Ctrl+Q), so typing in the game chat keeps working. The keys are saved and kept after **Delete all**. The rest of this README says F7/F8; read them as your own keys.

## Good to know

- **ToS:** the app moves the mouse and sends Ctrl+C automatically. Nothing is sent to the game server (it only hovers and copies), but automated input is a grey area under GGG's rules. Use at your own risk.
- If the game runs **as administrator**, run this app as administrator too, otherwise Windows blocks the key and mouse input.
- **Counts read from the screen:** some items (e.g. Simulacrum, Shattered Triskelion) copy without a stack size. For those the app reads the number in the icon's corner. It learns the digits from other items it scans, so:
  - Start with tabs that have many stacked items, like Currency or Essence.
  - Counts it can't read are shown as **"1?"** in the list. Scan a few more tabs, then rescan that tab.
- Rare, Magic and Unidentified items are not priced ("no price").
- A tab is learned only when the app can tell what it is: from 3 priced items, or from even one item of a kind only that tab holds (e.g. a Breach splinter). An empty tab is scanned but learned on a later scan.
- Look-alike tabs (the Runes sub-tabs: Runes, Kalguuran Runes, Soul Cores, Idols, Ancient Augments) are learned separately. Two of them can get the same guessed name, e.g. "Runes 2"; use **Rename** to tell them apart.

## Troubleshooting

- **F7 does nothing.**
  - If the app says at start that hotkeys could not be registered, another program (another PoE tool, an overlay, a recording app) already uses F7/F8. Pick other keys with **Scan key** / **Overlay key**, or close that program and restart the app.
  - If the game runs as administrator, run the app as administrator too.
- **The mouse moves but nothing shows over the game / "The game picture is black".** The game is in exclusive **Fullscreen** mode, where Windows doesn't let other programs draw over the game (Windows 11 may still allow screenshots, so scanning can seem to work while nothing appears). In the game open *Options → Graphics → Display Mode* and choose **Windowed Fullscreen**. The app warns about this when you press F7.
- **"Stash not visible".** Open the stash and keep the mouse off it while pressing F7.
- **A tab got the wrong name or was learned wrongly.** Pick it in the list and click **Rename**, or **Delete** it and scan it again.
- **Still stuck?** The app writes what it sees to `%APPDATA%\PoeStashPricer\log.txt` (paste that path into the Explorer address bar). Send that file along with your question.

## Privacy and security

- **Nothing about you leaves your PC.** The only network traffic is downloading league and price lists from poe.ninja over HTTPS. No account, no telemetry, no updates checked in the background.
- **Screenshots stay in memory.** The app looks at the game window to find the stash and the items, and forgets the pictures after the scan. They are never saved or sent.
- **Your clipboard is put back.** Scanning copies items with Ctrl+C; afterwards the text you had on the clipboard is restored, kept out of the Windows clipboard history and cloud clipboard (it could be a password from a password manager).
- **Input only goes to the game.** The mouse moves and Ctrl+C presses stop the moment another window comes to the front.
- **Everything the app keeps** is in `%APPDATA%\PoeStashPricer\`: settings, learned tabs (slot positions and a tiny greyscale thumbnail used to recognise the tab), last scans and a diagnostic log. The log never holds window titles or clipboard contents.
- **Verifiable downloads.** Each release lists the SHA-256 of its files, and releases from 1.3.2 on are built by GitHub Actions with a signed provenance attestation. See [SECURITY.md](SECURITY.md) for how to verify a download and how to report a vulnerability privately.

## For developers

The source is in `src\`. It builds with the C# compiler that ships with Windows, nothing to install:

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`tools\detect-test.ps1` runs panel detection, slot learning and tab recognition on full-screen stash screenshots in `samples\` without the game, and writes annotated images to `samples\out\`.

Every push and pull request is built on a clean Windows machine by [GitHub Actions](.github/workflows/build.yml); the zip is attached to the run as an artifact. Pushing a version tag (`git tag v1.2.3 && git push origin v1.2.3`) builds the release zip, attests it and attaches it with `SHA256SUMS.txt` to the GitHub release of that tag, creating the release if needed.

`tools\make-icon.ps1` redraws `src\app.ico`.

## License

[MIT](LICENSE). Not affiliated with or endorsed by Grinding Gear Games or poe.ninja.
