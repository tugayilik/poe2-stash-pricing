# PoE2 Stash Pricer

A small Windows app that scans the special stash tabs of Path of Exile 2 (Currency, Essence, Runes...) on screen and prices the items with [poe.ninja](https://poe.ninja/docs/api). It shows the value of every tab and of your whole stash, and writes the prices over the items in the game.

## Download

**[⬇ Download the latest release](https://github.com/tugayilik/poe2-stash-pricing/releases/latest)**. Get `PoeStashPricer-vX.Y.Z.zip` under *Assets*, unzip it anywhere and run `PoeStashPricer.exe`.

No installation needed: it runs on the .NET Framework 4.8 that comes with Windows 10/11.

> The exe is not signed, so Windows may show "Windows protected your PC" the first time: click **More info → Run anyway**.

Settings, saved tabs and scan results are kept in `%APPDATA%\PoeStashPricer\`. To remove the app, delete the exe and that folder.

## Game settings

- Display mode must be **Windowed Fullscreen / Borderless**. In exclusive fullscreen, screenshots can come out black.
- Game language must be **English**. Item names are matched with poe.ninja in English.
- Any resolution works: the stash panel is found on screen automatically.

## How to use

### 1. Save your tabs (once)

The app recognises the open tab by comparing it with screenshots you save, so each tab has to be saved once:

1. Open the stash in the game.
2. In the app, click **Save tabs in order**.
3. The app tells you on top of the game which tab to open (e.g. *"Open the 'Currency' tab in the game and press F6"*).
   - Open that tab, **wait until it is fully shown**, then press **F6**. Keep the mouse off the stash.
   - Press **F9** to skip a tab you don't have.
4. Continue until all tabs are done.

Supported tabs: **Currency, Fragments, Expedition, Breach, Abyss, Essence, Delirium, Runes** (sub-tabs Runes, Kalguuran Runes, Soul Cores, Idols, Ancient Augments) and **Ritual**. For Fragments, saving one of its three sub-tabs is enough.

To refresh a single tab, pick it in the list, click **Save selected** and press F6 in the game. **Delete** removes a saved tab.

### 2. Scan

1. Open a tab in the game and press **F7**. The app recognises the tab, moves the mouse over each item and reads it with **Ctrl+C**.
2. Don't touch the mouse while it scans. **Esc** or **F7** again stops the scan.
3. Prices appear over the items. **F8** hides or shows the price overlay.

The last scan of every tab is kept:

- When you switch to another tab the prices are hidden; when you come back to a scanned tab they come back **without pressing F7**.
- Press F7 again whenever you want to rescan a tab.
- Results survive closing the app.
- poe.ninja updates prices hourly; saved scans are always valued at the current prices.

### The app window

- **Top:** the total value of all scanned tabs.
- **Left:** your tabs with their value and last scan time. The tab open in the game is bold and marked with ▶.
- **Right:** the items of the tab open in the game (or the one picked on the left) with quantities and prices.
- **Show in:** show prices in Divine, Exalted, Chaos or automatically.
- **Hover delay (ms):** wait between moving onto an item and pressing Ctrl+C. Increase it (60–100) if items are read wrong or missed.
- **Preview:** shows whether the open tab is recognised and which positions will be scanned.

## Hotkeys

| Key | Action |
|---|---|
| F6 | Save the open tab (while saving tabs) |
| F7 | Scan the open tab / stop scanning |
| F8 | Hide / show the price overlay |
| F9 | Skip this tab (while saving tabs) |
| Esc | Stop scanning |

## Good to know

- **ToS:** the app moves the mouse and sends Ctrl+C automatically. Nothing is sent to the game server (it only hovers and copies), but automated input is a grey area under GGG's rules. Use at your own risk.
- If the game runs **as administrator**, run this app as administrator too, otherwise Windows blocks the key and mouse input.
- **Counts read from the screen:** some items (e.g. Simulacrum, Shattered Triskelion) copy without a stack size. For those the app reads the number in the icon's corner. It learns the digits from other items it scans, so:
  - Start with tabs that have many stacked items, like Currency or Essence.
  - Counts it can't read are shown as **"1?"** in the list. Scan a few more tabs, then rescan that tab.
- Rare, Magic and Unidentified items are not priced ("no price").
- You can scan a tab that isn't saved, but its result isn't added to the total stash value.

## For developers

The source is in `src\`. It builds with the C# compiler that ships with Windows, nothing to install:

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`tools\detect-test.ps1` runs panel detection, slot learning and tab recognition on full-screen stash screenshots in `samples\` without the game, and writes annotated images to `samples\out\`.
