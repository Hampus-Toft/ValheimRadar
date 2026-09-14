@AGENTS.md
# ValheimRadar - Development & Architecture Guide

ValheimRadar is a BepInEx 5 plugin for Valheim that scans the nearby environment for entities
(creatures, resources, structures) and world Locations (dungeons, altars, ruins, etc.), then
places clustered pins on the in-game Minimap.

## Build & Test Commands

- **Run unit tests (always safe):** `dotnet test`
- **Build the plugin (agent-safe, no game side effects):** `dotnet build -c Release`
- **Build with local auto-deploy + game relaunch (human interactive use only):**
  `dotnet build` (plain Debug build - see `AGENTS.md` Critical Gotchas before running this as
  an agent, it kills and relaunches the live Steam Valheim process)
- **Clean build:** `dotnet clean && dotnet build -c Release`
- **Pack a Thunderstore zip:** `dotnet build -c Release -t:ThunderstorePack` (from
  `ValheimRadar/`)

> **Note:** Building requires a real Valheim install with Jotunn installed as a BepInEx
> plugin - `assembly_valheim.dll`, `UnityEngine*.dll`, `BepInEx.dll`, and `Jotunn.dll` are all
> resolved via `HintPath` from `ValheimInstallDir` (default: the standard Steam install path).
> Override with the `VALHEIM_INSTALL_DIR` env var or `-p:ValheimInstallDir="..."` if Valheim
> lives elsewhere. There are no NuGet substitutes for these assemblies, so `dotnet test` (which
> doesn't trigger the game-deploy/relaunch side effects) is the fastest safe feedback loop for
> an agent.

---

## Architecture & File Structure

All code resides within the `ValheimRadar` root namespace. `Scanning/` tracks three distinct
kinds of content, each with its own evaluator + scanner pair, plus shared helpers so none of
the three need to depend on each other:

```text
ValheimRadar/
├── Configuration/
│   └── RadarConfig.cs           # ConfigEntry bindings, CreatureDefinition/LocationDefinition tables, master toggles
├── Models/
│   ├── ItemCluster.cs           # Centroid, label, and cluster key computation
│   ├── TrackedItem.cs           # DTO representing a scanned creature/resource/POI (ZDOID, Position, Icon)
│   └── TrackedLocation.cs       # DTO representing a scanned world Location/POI (no ZDOID)
├── Pinning/
│   ├── IconLoader.cs            # PNG-to-Sprite loading (via Jotunn AssetUtils) for user icon overrides
│   ├── VanillaIconResolver.cs   # Verified vanilla icon sprites (via Jotunn GUIManager) for creatures/resources
│   └── PinManager.cs            # Minimap pin sync, updates, removals, and icon resolution order
├── Scanning/
│   ├── ClusteringEngine.cs        # Spatial distance-based point-clustering logic
│   ├── ObjectEvaluator.cs         # Thin composition root: categoryKey -> enabled-state/icon, dispatches to the 3 evaluators below
│   ├── NameFormatting.cs          # Shared name/display-text helpers (exact-alias matching, title-casing, prefix stripping)
│   ├── ScanFilters.cs             # Shared pre-filter (debris names, dungeon-interior objects) applied before any evaluator
│   ├── ScanGeometry.cs            # Shared Physics.OverlapBox cell geometry/query + ScanCellKey
│   ├── SpatialCellScanner.cs      # Rotating per-cell cache scanner - Type #1 (ephemeral: creatures)
│   ├── PermanentSpatialScanner.cs # "Scan each cell once, ever" scanner - Types #2/#3 (semi-permanent: resources/physics-POI)
│   ├── CreatureEvaluator.cs + CreatureScanner.cs   # Type #1: creatures/fish/Leviathan
│   ├── ResourceEvaluator.cs + ResourceScanner.cs   # Type #2: trees/ores/berries/ground pickables
│   ├── PoiEvaluator.cs + PoiScanner.cs             # Type #3: physics-detected POI fallback
│   └── LocationScanner.cs         # Type #3 (preferred): ZoneSystem.GetLocationList()-based POI discovery (dungeons, altars, ruins, etc.)
├── docs/ICONS.md                  # Icon resolution order & override naming
├── Thunderstore/                  # ThunderstorePack packaging assets (manifest template, Pack.ps1, README, icon)
└── RadarPlugin.cs                 # Plugin lifecycle, update loop, and per-scanner Reset()/scan orchestration

ValheimRadar.Tests/                # xunit tests mirroring the folders above (Models/, Pinning/, Scanning/)
```

See `AGENTS.md` for build gotchas, safety boundaries, and step-by-step playbooks.
