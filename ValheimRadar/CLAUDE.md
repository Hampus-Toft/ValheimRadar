@AGENTS.md
# ValheimRadar - Development & Architecture Guide

ValheimRadar is a BepInEx 5 plugin for Valheim that scans the nearby environment for entities (creatures, resources, structures, locations) and places clustered pins on the in-game Minimap.

## Build Commands

- **Build Project:** `dotnet build`
- **Build Release:** `dotnet build -c Release`
- **Clean Build:** `dotnet clean && dotnet build`

> **Note:** Building requires Valheim assemblies (`assembly_valheim.dll`, `assembly_gui.dll`, `UnityEngine.dll`, BepInEx libraries) referenced in the `.csproj` or provided via a local environment variable/reference path.

---

## Architecture & File Structure

All code resides within the `ValheimRadar` root namespace.

```text
ValheimRadar/
├── Configuration/
│   └── RadarConfig.cs       # BepInEx ConfigEntry bindings and master toggles
├── Models/
│   ├── ItemCluster.cs       # Centroid, label, and cluster key computation
│   └── TrackedItem.cs       # DTO representing a scanned entity (ZDOID, Position, Icon)
├── Pinning/
│   ├── IconLoader.cs        # PNG-to-Sprite loading (via Jotunn AssetUtils) for user icon overrides
│   ├── VanillaIconResolver.cs # Verified vanilla icon sprites (via Jotunn GUIManager) for creatures/resources
│   └── PinManager.cs        # Minimap pin sync, updates, removals, and icon routing
├── Scanning/
│   ├── ClusteringEngine.cs        # Spatial distance-based point-clustering logic
│   ├── ObjectEvaluator.cs         # Thin dispatcher: categoryKey -> enabled-state/icon lookups
│   ├── NameFormatting.cs          # Shared name/display-text helpers (alias matching, title-casing)
│   ├── ScanFilters.cs             # Shared pre-filter (debris names, dungeon-interior objects)
│   ├── ScanGeometry.cs            # Shared Physics.OverlapBox cell geometry/query
│   ├── SpatialCellScanner.cs      # Rotating per-cell cache scanner (type #1: ephemeral)
│   ├── PermanentSpatialScanner.cs # "Scan each cell once" scanner (types #2/#3: semi-permanent)
│   ├── CreatureEvaluator.cs + CreatureScanner.cs   # Type #1: creatures/fish/Leviathan
│   ├── ResourceEvaluator.cs + ResourceScanner.cs   # Type #2: trees/ores/berries/pickables
│   ├── PoiEvaluator.cs + PoiScanner.cs             # Type #3: physics-detected POI fallback
│   └── LocationScanner.cs         # Type #3: ZoneSystem-based POI discovery (dungeons, ruins, etc.)
└── RadarPlugin.cs           # Plugin lifecycle, update loop, and overlap scanning