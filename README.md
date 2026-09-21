# PoE2 Stash Pricer

A small Windows app that scans the special stash tabs of Path of Exile 2 (Currency, Essence, Runes...) on screen and prices the items with [poe.ninja](https://poe.ninja/docs/api). It shows the value of every tab and of your whole stash, and writes the prices over the items in the game.

## Download

**[⬇ Download the latest release](https://github.com/tugayilik/poe2-stash-pricing/releases/latest)**. Get `PoeStashPricer-vX.Y.Z.zip` under *Assets*, unzip it anywhere and run `PoeStashPricer.exe`.

No installation needed: it runs on the .NET Framework 4.8 that comes with Windows 10/11.

> The exe is not signed, so Windows may show "Windows protected your PC" the first time: click **More info → Run anyway**.

Everyone starts from a clean setup: the download contains only the exe and this README. Each user's saved tabs, scan results and learned digits are kept on their own PC in `%APPDATA%\PoeStashPricer\`. Screenshots of your stash are only used while saving a tab and are never stored. To start over, click **Delete all** in the app; to remove the app, delete the exe and that folder.

## Game settings

- Display mode must be **Windowed Fullscreen / Borderless**. In exclusive fullscreen, screenshots can come out black.
- Game language must be **English**. Item names are matched with poe.ninja in English.
- Any resolution works: the stash panel is found on screen automatically.

## How to use

1. Open the stash in the game, open a tab and press **F7**. The app finds the stash on screen, moves the mouse over each item and reads it with **Ctrl+C**.
2. Don't touch the mouse while it scans. **Esc** or **F7** again stops the scan.
3. Prices appear over the items. **F8** hides or shows the price overlay.

**Tabs are learned automatically.** The first scan of a special tab saves it: where its slots are, how it looks, and a name guessed from its items (Currency, Essence, Runes...). From then on the tab is recognised, its value counts in the total stash value, and its prices come back when you return to it. If a name is wrong, pick the tab in the list and click **Rename**.

Special tabs are the ones with fixed slots: **Currency, Fragments, Expedition, Breach, Abyss, Essence, Delirium, Runes** (Runes, Kalguuran Runes, Soul Cores, Idols, Ancient Augments) and **Ritual**. Normal and quad tabs can be scanned too, but they are not saved and not added to the total (they can hold two stacks of the same item, which the saved-tab logic would count once).

**Delete** removes a saved tab and its last scan; its next scan learns it again. **Delete all** removes all saved tabs, scan results and learned digits, so the app starts over as if freshly installed (league and currency choices are kept).

The last scan of every tab is kept:

- When you switch to another tab the prices are hidden; when you come back to a scanned tab they come back **without pressing F7**.
- Press F7 again whenever you want to rescan a tab.
- Results survive closing the app.
- Prices are fetched from poe.ninja when the app starts and every 15 minutes while it is open (**Refresh prices** fetches them right away). If poe.ninja can't be reached or asks to slow down, the app keeps the last prices and tries again a few minutes later. Saved scans are always valued at the current prices.

### The app window

- **Top:** the total value of all scanned tabs.
- **Left:** your tabs with their value and last scan time. The tab open in the game is bold and marked with ▶.
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

## For developers

The source is in `src\`. It builds with the C# compiler that ships with Windows, nothing to install:

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`tools\detect-test.ps1` runs panel detection, slot learning and tab recognition on full-screen stash screenshots in `samples\` without the game, and writes annotated images to `samples\out\`.

## License

[MIT](LICENSE). Not affiliated with or endorsed by Grinding Gear Games or poe.ninja.
