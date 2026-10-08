# Peak Overhead Stats

> Teammate stamina bars, status numbers, and inventory — **all shown above players' heads** instead of the bottom-left corner.

Combine the full HUD of **PeakStatsEx** with the overhead positioning of **OverheadStamina**.

## Features

### Teammate overhead display
Everything PeakStatsEx showed in the bottom-left party list is now floating directly above the teammate it belongs to:

- **Player name** — colored by player color, left-aligned with the stamina bar
- **Main stamina bar** — bright green fill with numeric value
- **Extra stamina bar** — thin blue bar below the main one
- **Affliction bars** — weight / hunger / cold / poison / etc. with correct colors
- **Inventory slots** — items shown next to the status bar
- **Airport weight support** — weight bar renders correctly in airport scene

### Climb session stats (PeakStatsEx HUD)
- **Climb time** — total elapsed time
- **Climb height** — current altitude
- **Map & biome route** — current map and biome progression with built-in Chinese localization
- All HUD elements match PeakStatsEx layout exactly

## Screenshots

![Overhead teammate display](https://raw.githubusercontent.com/linling000I/PeakOverheadStats/main/screenshot1.jpg)

![Airport weight display](https://raw.githubusercontent.com/linling000I/PeakOverheadStats/main/screenshot2.jpg)

## Configuration

Launch the game once, then edit `BepInEx/config/com.yls.peakoverheadstats.cfg`:

| Setting | Default | Description |
|---|---|---|
| ShowInventorySlots | true | Show teammate inventory items |
| ShowExtraStaminaBar | true | Show extra stamina bar |
| MaxShowDistance | 30 | World units before overhead info hides |

## Installation

1. Install **[BepInExPack PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/)** (required dependency)
2. Install this mod via **r2modman** / **Thunderstore Mod Manager**, or manually:
   - Extract the ZIP into `PEAK/BepInEx/plugins/`
3. Launch the game

## Building from source

```powershell
dotnet build src\PeakOverheadStats\PeakOverheadStats.csproj -c Release
```

## Credits

- **LengSword / yls-peak-mods** — original [PeakStatsEx](https://thunderstore.io/c/peak/p/LengSword/PeakStatsEx/) concept and full HUD feature set
- **PatchNote** — [OverheadStamina](https://thunderstore.io/c/peak/p/PatchNote/OverheadStamina/) overhead rendering approach
- **妧妧** — playtesting and feedback
- **咸鱼** — playtesting and feedback
- PEAK modding community

## License

MIT
