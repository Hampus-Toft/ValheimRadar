# Agent Instructions - ValheimRadar

This document defines operating guidelines, safety boundaries, and workflows for autonomous AI agents working on `ValheimRadar`.

---

## Project Context

- **Environment:** BepInEx 5 / Harmony / Unity / Valheim API. Main plugin targets `netstandard2.1`; the test project targets `net8.0`.
- **Primary Goal:** Perform fast spatial scans around the local player, group entities into clusters, and display/update custom pins on the Valheim minimap. Also scans world `ZoneSystem` Locations (dungeons, altars, ruins, the trader, etc.) as a separate pinning pipeline.
- **Solution layout:** `ValheimRadar.slnx` (repo root) contains two projects:
  - `ValheimRadar/` - the plugin itself (`ValheimRadar.csproj`).
  - `ValheimRadar.Tests/` - xunit unit tests (`ValheimRadar.Tests.csproj`).

---

## Critical Gotchas (read before running any build)

1. **`dotnet build` on `ValheimRadar.csproj` with the default `Debug` config kills and
   relaunches the live Steam Valheim process.** The project's `PostBuild` target runs
   `taskkill /F /IM valheim.exe` and then relaunches via `steam://rungameid/...` whenever
   `AutoLaunchValheim` is true, which it is by default for any `Debug` build - this is meant
   for the human's interactive dev loop, not for an agent. **Always build with
   `dotnet build -c Release` or pass `-p:AutoLaunchValheim=false` when working as an agent**,
   unless the user explicitly asked you to relaunch the game to test something live.
2. **This project cannot be built in a sandbox without a real Valheim + Jotunn install.**
   `ValheimRadar.csproj` references `assembly_valheim.dll`, `UnityEngine*.dll`, `BepInEx.dll`,
   and `Jotunn.dll` via `HintPath`, resolved from `ValheimInstallDir` (default: the standard
   Steam path `C:\Program Files (x86)\Steam\steamapps\common\Valheim`, overridable via the
   `VALHEIM_INSTALL_DIR` env var or `-p:ValheimInstallDir="..."`). A `CheckJotunnReference`
   target fails the build early with a clear error if `Jotunn.dll` isn't present under
   `<ValheimInstallDir>\BepInEx\plugins\...` (override its location with
   `-p:JotunnDllPath="..."` if your mod manager uses a different plugin folder name). If a
   build fails this way, that's an environment problem, not something to "fix" by deleting the
   guard target.
3. **The `PostBuild` target also copies the built DLL into `<ValheimInstallDir>\BepInEx\plugins`
   and errors if `ValheimInstallDir` doesn't exist.** This is intentional (auto-deploy for local
   testing), not a bug.
4. **`dotnet test` is always safe.** `ValheimRadar.Tests.csproj` forces
   `AutoLaunchValheim=false` on its `ProjectReference` to `ValheimRadar.csproj`, so running the
   test suite never touches the live game process.

---

## Build & Test Commands

- **Run unit tests (safe, no side effects):** `dotnet test` (from repo root or
  `ValheimRadar.Tests/`).
- **Build the plugin (agent-safe):** `dotnet build -c Release` from `ValheimRadar/`, or
  `dotnet build ValheimRadar.slnx -c Release` from the repo root.
- **Build + auto-deploy for local interactive testing (human use only):**
  `dotnet build` (Debug) from `ValheimRadar/` - deploys the DLL and relaunches Valheim.
- **Clean build:** `dotnet clean && dotnet build -c Release`.
- **Pack a Thunderstore-importable zip:** `dotnet build -c Release -t:ThunderstorePack`
  from `ValheimRadar/`. Produces a zip under `ValheimRadar/bin/Thunderstore` containing
  `manifest.json` + `icon.png` + `README.md` + the plugin DLL, via `Thunderstore/Pack.ps1`.
  `manifest.json`'s `version_number` is generated from `RadarPlugin.PluginVersion` at pack
  time - never hand-edit a version number in `Thunderstore/manifest.template.json`; bump the
  constant in `RadarPlugin.cs` instead. Override the namespace with
  `-p:ThunderstoreNamespace="YourTeamName"`.

---

## Architecture & File Structure

All code resides within the `ValheimRadar` root namespace.

```text
ValheimRadar/
├── Configuration/
│   └── RadarConfig.cs         # ConfigEntry bindings, CreatureDefinition/LocationDefinition tables, master toggles
├── Models/
│   ├── ItemCluster.cs         # Centroid, label, and cluster key computation
│   ├── TrackedItem.cs         # DTO representing a scanned creature/resource (ZDOID, Position, Icon)
│   └── TrackedLocation.cs     # DTO representing a scanned world Location/POI
├── Pinning/
│   ├── IconLoader.cs          # PNG-to-Sprite loading (via Jotunn AssetUtils) for user icon overrides
│   ├── VanillaIconResolver.cs # Verified vanilla icon sprites (via Jotunn GUIManager) for creatures/resources
│   └── PinManager.cs          # Minimap pin sync, updates, removals, and icon resolution order
├── Scanning/
│   ├── ClusteringEngine.cs    # Spatial distance-based point-clustering logic
│   ├── ObjectEvaluator.cs     # Creature/resource classification, star rating parsing, and filtering
│   ├── BatchScanner.cs        # Incremental/batched scene scan for creatures & resources
│   └── LocationScanner.cs     # ZoneSystem.LocationInstance-based scan for world Locations/POIs
├── docs/
│   └── ICONS.md                # Icon resolution order & override naming, for reference when touching Pinning/
├── Thunderstore/
│   ├── Pack.ps1, manifest.template.json, README.md, icon.png   # ThunderstorePack packaging assets
└── RadarPlugin.cs             # Plugin lifecycle, update loop, and overlap scanning

ValheimRadar.Tests/            # xunit tests mirroring the folders above (Models/, Pinning/, Scanning/)
```

---

## Safety & Modification Boundaries

- **Do NOT** change the `ValheimRadar` namespace or break module boundaries.
- **Do NOT** move scanning logic back into `RadarPlugin.cs`. Keep evaluation in
  `ObjectEvaluator`, clustering in `ClusteringEngine`, and Location scanning in
  `LocationScanner`.
- **Do NOT** hardcode absolute file paths. Always use relative paths or BepInEx utilities like
  `Paths.ConfigPath`.
- **Do NOT** create persistent static state that leaks memory across server reconnects or
  world reloads.
- **Do NOT** bypass the `CheckJotunnReference`/`PostBuild` MSBuild guard targets in
  `ValheimRadar.csproj` to "make the build pass" - a failure there means the environment is
  missing Valheim/Jotunn, not that the check is wrong.
- **Do NOT** hand-edit the version number in `Thunderstore/manifest.template.json` - it's
  generated from `RadarPlugin.PluginVersion` at pack time.

---

## Agent Playbooks

### Playbook 1: Adding a New Trackable Creature/Resource

1. **Add a definition:** In `Configuration/RadarConfig.cs`, add a `CreatureDefinition` (for
   creatures/fish) to `CreatureDefinitions`, or a new `ResourceRule` in
   `Scanning/ObjectEvaluator.cs` (for resources/structures). Use exact, lowercased prefab
   names as aliases (`IsExactAlias`/`AliasLookup` do flat exact matching, not substring
   scanning - declaration order doesn't matter for correctness).
2. **Assign display name, section, and icon:** Give it a human-readable `DisplayName`, put it
   under the right biome/category `Section` constant, and pick a default PNG fallback name
   (`monster.png`, `animal.png`, `berry.png`, etc.) plus, ideally, a verified vanilla icon name
   in `Pinning/VanillaIconResolver.cs` - see `docs/ICONS.md` for the full resolution order
   before touching icon code.
3. **Wire config:** Confirm the new entry is bound inside `RadarConfig.Initialize(ConfigFile
   config)` - definitions declared in the static arrays are consumed there, not registered
   ad hoc elsewhere.
4. **Add/extend a unit test** in `ValheimRadar.Tests/Scanning/ObjectEvaluatorTests.cs` covering
   the new alias/classification, then run `dotnet test`.
5. **Verify build:** `dotnet build -c Release` (see Critical Gotchas above - never a bare
   Debug `dotnet build` as an agent).

### Playbook 2: Adding a New Trackable World Location/POI

1. **Add a `LocationDefinition`** in `Configuration/RadarConfig.cs`'s Location tables, with
   exact lowercased `ZoneLocation.m_prefabName` values, a `LocationGroup` (BossAltar,
   Landmark, DungeonEntrance, Runestone, Ruin), and an icon (PNG fallback and/or vanilla icon
   name).
2. **Location matching itself lives in `Scanning/LocationScanner.cs`** - don't duplicate
   matching logic elsewhere; it already consumes the definition tables via
   `LocationPrefabLookup`.
3. **Add/extend a unit test** in `ValheimRadar.Tests/Scanning/LocationScannerTests.cs`.
4. **Verify build and tests** as in Playbook 1.

### Playbook 3: Modifying Clustering Logic

1. **Location:** Edits must be isolated strictly to `Scanning/ClusteringEngine.cs` and
   `Models/ItemCluster.cs`.
2. **Centroid Computation:** Ensure `GetCentroid()` handles zero-count lists safely to prevent
   `Vector3.zero` division errors.
3. **Keying:** Ensure `GetClusterKey()` produces unique keys per cluster to avoid pin
   flickering in `PinManager`.
4. **Add/extend a unit test** in `ValheimRadar.Tests/Models/ItemClusterTests.cs` and/or
   `ValheimRadar.Tests/Scanning/ClusteringEngineTests.cs`.

### Playbook 4: Refactoring / Adding Extensions

1. Check for `ZNetView` validity on all target objects.
2. ValheimRadar has a **hard** dependency on Jotunn (`[BepInDependency(Jotunn.Main.ModGuid)]` in
   `RadarPlugin.cs`) for minimap icon sprites - `Pinning/IconLoader.cs` uses
   `Jotunn.Utils.AssetUtils` to load user-supplied PNGs, and `Pinning/VanillaIconResolver.cs` uses
   `Jotunn.Managers.GUIManager` to pull verified built-in icons straight from Valheim's own icon
   atlas. Don't reintroduce a standalone PNG/sprite loader or a bespoke `Minimap.m_icons`
   registration path - route new icon sources through these two files. See `docs/ICONS.md` for
   the exact resolution order (`PinManager.ResolvePerObjectPin`).

---

## Verification Steps After Changes

Before submitting changes, ensure:
1. `dotnet test` passes (xunit tests in `ValheimRadar.Tests`, safe to run anytime).
2. `dotnet build -c Release` passes with 0 errors and 0 warnings (never a bare Debug
   `dotnet build` as an agent - see Critical Gotchas).
3. All new configuration keys/definitions are actually consumed inside
   `RadarConfig.Initialize()` (creatures/resources) or the Location tables + `LocationScanner`
   (Locations/POIs).
4. No Unity game logic is invoked off the main thread (Unity API calls must stay on the main
   thread).
