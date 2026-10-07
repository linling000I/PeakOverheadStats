# Development Notes

## Building

1. Install .NET SDK (netstandard2.1 target)
2. Copy `Config.Build.user.props.template` to `Config.Build.user.props` and set `GameDir`
3. Run `dotnet build src\PeakOverheadStats\PeakOverheadStats.csproj -c Release`
4. DLL is copied to `BepInEx/plugins/` automatically

## Key Architecture

- `Plugin.cs` - BepInEx plugin entry, Harmony patches
- `OverheadNameplate.cs` - Attaches to PlayerName, creates overhead stamina bar
- `CharacterStaminaBar.cs` - PeakStatsEx full teammate stamina bar component
- `CharacterBarAffliction.cs` - Affliction status bar (weight, petrify, etc.)
- `TimerHeightStats.cs` - Climb timer, height, day/night HUD
- `MapStats.cs` - Map route, level, biomes HUD
- `StaminaInfoPatch.cs` - Numeric values on stamina bars
- `GameRef.cs` - Reflection helpers for private/internal game APIs

## Game API Notes

- `CharacterAfflictions.GetCurrentStatus(STATUSTYPE)` returns base status only
- `CharacterAfflictions.GetIncrementalStatus(STATUSTYPE)` returns item weight etc.
- Weight total = GetCurrentStatus(Weight) + GetIncrementalStatus(Weight)
- `UIPlayerNames.UpdateName` is patched to attach overhead nameplates

## Thunderstore Packaging

Package structure:
```
manifest.json
README.md
CHANGELOG.md
LICENSE
icon.png (256x256)
screenshot1.jpg
screenshot2.jpg
plugins/
  PeakOverheadStats.dll
```

Dependency: `BepInEx-BepInExPack_PEAK-5.4.75301`
