# GroundClear 1.0.0

For Vintage Story 1.22.7. View and selectively delete loose item drops within a 16-block sphere around your player.

## Install

1. Exit the world and close Vintage Story.
2. Put **GroundClear-1.0.0.zip**, still zipped, into:
   `%APPDATA%\VintagestoryData\Mods`
   (or use **Mods > Open Mods Folder** in the game).
3. Start the game, enable GroundClear if needed, and reopen your world.
4. Open chat and enter `.groundclear` (with the leading dot).

No extra library mod is required. Do not install the source ZIP as a mod. The release contains no game DLLs. Remove the mod ZIP while the game is closed to uninstall.

## Clear quartz and keep silver

Enter `.groundclear`, search for **quartz**, and check only the quartz-chunk row (`game:ore-quartz`). Silver-bearing quartz ore is a separate type; leave it unchecked. Click **Clear selected**, review the listed types and quantities, then click **Delete permanently**. Click **Cancel** to keep everything.

**Clear all** previews every loose drop within range, including valuable items, even when the search field hides some rows. Nothing is selected by default. **Refresh** rescans and resets selection. Selecting an item type selects all previewed stacks of that type, including different attribute/durability variants.

## Commands for heavy lag

```text
/groundclear list
/groundclear preview game:ore-quartz
/groundclear confirm
```

Other commands:

```text
/groundclear preview all
/groundclear cancel
```

`list` displays the exact item codes, quantities, and loose-stack counts. `preview` accepts one exact code, not a substring or wildcard. The `game:` prefix may be omitted for vanilla items. For modded items use their full domain and code from the list.

## Behavior

- Range includes drops above/below you and through walls, within the same dimension. The boundary is 16 blocks measured from the player's position.
- Only loose `EntityItem` drops are removed. Inventory, chest contents, placed ground-storage piles, placed blocks, creatures, and players are unaffected.
- Confirmation permanently deletes items; there is no undo.
- A preview expires after 60 real seconds. Make a new preview if it expires.
- Only the entities captured by the preview are eligible. New drops, changed/merged stacks, collected drops, and drops outside your current range are skipped. The result reports the actual removed quantities and skipped stacks. Refresh and preview again if needed.
- Cleanup processes at most 128 captured entities per server tick. Repeated cleanup requests are blocked until completion.
- Scans run on request, not continuously. There is no automatic deletion or mining-drop filter.
- The single-player owner has access. In multiplayer the server must install GroundClear and the user needs `controlserver` permission; clients need it for the window, while the server commands also work without the client mod.

The mod was not installed into your existing game and your existing world was not modified during development. Installation is the step above.

## Build from source

Install the **.NET 10 SDK**, then run in PowerShell:

```powershell
.\build.ps1 -VintageStoryPath 'E:\Games\Vintagestory'
```

The script builds the mod, runs the deterministic core checks, and writes `dist\GroundClear-1.0.0.zip`. The game must already be installed; game assemblies are reference-only and are not redistributed. The runtime test project additionally uses the local game path in its project file; adjust that path if your install is elsewhere.

The `tests/runtime` sources are test-only mods for disposable worlds and are excluded from the release. The server fixture spawns thousands of drops and shuts its test server down. Never install the runtime fixtures into a world you want to keep.

## Validation limits

Tested against the installed 1.22.7 game with deterministic core tests, 2,000 real quartz entities, persistence after reload, automated off-screen GUI composition, and interactive single-player window checks. Search, selection, confirmation, cancellation and quartz-only deletion were checked with real quartz and silver-bearing ore drops. Compatibility with every mod in your existing modpack has not been tested.

## Source repository

[GroundClear](https://github.com/tannerjriddle97/GroundClear) contains the addon source, authoring files and available test sources. No open-source license has been selected for this source. Game/mod dependencies, local profiles, saves, recovery provenance and generated packages are excluded. Historical audit/probe scripts require a separate deliberate runtime-testing decision; publication does not run them. Interactive acceptance limits remain as documented.
