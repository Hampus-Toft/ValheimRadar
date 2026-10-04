# Agent Instructions - ValheimRadar

This document defines operating guidelines, safety boundaries, and workflows for autonomous AI agents working on `ValheimRadar`. `CLAUDE.md` imports this file; keep all shared guidance here.

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
   `VALHEIM_INSTALL_DIR` env var or `-p:ValheimInstallDir="..."`). There are no NuGet
   substitutes for these assemblies. A `CheckJotunnReference` target fails the build early with
   a clear error if `Jotunn.dll` isn't present under `<ValheimInstallDir>\BepInEx\plugins\...`
   (override its location with `-p:JotunnDllPath="..."` if your mod manager uses a different
   plugin folder name). If a build fails this way, that's an environment problem, not something
   to "fix" by deleting the guard target.
3. **The `PostBuild` target also copies the built DLL into `<ValheimInstallDir>\BepInEx\plugins`
   and errors if `ValheimInstallDir` doesn't exist.** This is intentional (auto-deploy for local
   testing), not a bug. It happens for every configuration, including the Debug build that
   `dotnet test` triggers - expect the deployed DLL to be overwritten.
4. **`dotnet test` never kills or relaunches the game.** `ValheimRadar.Tests.csproj` forces
   `AutoLaunchValheim=false` on its `ProjectReference` to `ValheimRadar.csproj`. It does still
   perform the DLL copy from gotcha #3.
5. **`.claude/worktrees/` holds full checkouts of other branches and is not gitignored.** Exclude
   it from searches (Glob/Grep from the repo root return duplicate hits) and never `git add -A`.

---

## Build & Test Commands

- **Run unit tests (safe for the game process):** `dotnet test` (from repo root or
  `ValheimRadar.Tests/`).
  - One test class: `dotnet test --filter "FullyQualifiedName~ScanGeometryTests"`
  - One test: `dotnet test --filter "FullyQualifiedName~ClusteringEngineTests.<TestName>"`
- **Build the plugin (agent-safe):** `dotnet build -c Release` from `ValheimRadar/`, or
  `dotnet build ValheimRadar.slnx -c Release` from the repo root.
- **Build + auto-deploy for local interactive testing (human use only):**
  `dotnet build` (Debug) from `ValheimRadar/` - deploys the DLL and relaunches Valheim.
- **Clean build:** `dotnet clean && dotnet build -c Release`.
- **Pack a Thunderstore-importable zip:** `dotnet build -c Release -t:ThunderstorePack`
  from `ValheimRadar/`. Produces a zip under `ValheimRadar/bin/Thunderstore` containing
  `manifest.json` + `icon.png` + `README.md` + `CHANGELOG.md` + the plugin DLL + the native SQLite libraries, via `Thunderstore/Pack.ps1`.
  The pack fails if `Thunderstore/CHANGELOG.md` has no `## <PluginVersion>` entry.
  `manifest.json`'s `version_number` is generated from `RadarPlugin.PluginVersion` at pack
  time - never hand-edit a version number in `Thunderstore/manifest.template.json`; bump the
  constant in `RadarPlugin.cs` instead. Override the namespace with
  `-p:ThunderstoreNamespace="YourTeamName"`.
- There is no linter/formatter config.

---

## Architecture & File Structure

All code resides within the `ValheimRadar` root namespace. `Scanning/` tracks three distinct
kinds of content, each with its own evaluator + scanner pair, plus shared helpers extracted so
none of the three need to depend on each other. Paths below and in the playbooks are relative to
the `ValheimRadar/` plugin project unless prefixed with `ValheimRadar.Tests/`.

```text
ValheimRadar/
├── Configuration/
│   └── RadarConfig.cs           # ConfigEntry bindings, CreatureDefinition/LocationDefinition tables, master toggles
├── Models/
│   ├── ItemCluster.cs           # Centroid, label, and cluster key computation
│   ├── TrackedItem.cs           # DTO representing a scanned creature/resource/POI (ZDOID, Position, Icon)
│   └── TrackedLocation.cs       # DTO representing a scanned world Location/POI (no ZDOID)
├── Persistence/
│   ├── SqliteNative.cs          # P/Invoke to native e_sqlite3 + loading it from the plugin folder
│   ├── SqliteConnection.cs      # Thin connection/statement wrapper (UTF-8, transactions)
│   ├── PinDatabase.cs           # Per-world SQLite schema and queries (PinData/<world>.db)
│   └── LegacyPinFiles.cs        # Parsers for the pre-v1.12 .txt saves, used only for the one-time import
├── Pinning/
│   ├── IconLoader.cs            # PNG-to-Sprite loading (via Jotunn AssetUtils) for user icon overrides
│   ├── VanillaIconResolver.cs   # Verified vanilla icon sprites (via Jotunn GUIManager) for creatures/resources
│   ├── PinManager.cs            # Minimap pin sync, updates, removals, and icon resolution order
│   ├── PinManager.Persistence.cs # Partial: streams in/queues/flushes the world database, imports legacy .txt saves
│   ├── PinManager.Viewport.cs   # Partial: per-frame Tick - budgeted loading/reclustering, resource pins only for the visible map area
│   ├── PinViewport.cs           # Pure view-rect math for pin virtualization (map UV -> world, margin, reload hysteresis)
│   ├── MinimapPinBulk.cs        # Removes many minimap pins in one pass (Minimap.RemovePin is a linear List.Remove)
│   ├── PersistedPointRules.cs   # Pure identity rules for saved raw points (ZDOID key is a hint, position confirms)
│   ├── DismissedPins.cs         # Pure store + selection logic for pins the player dismissed via right-click
│   ├── MinimapPatches.cs        # Harmony postfix on Minimap.RemovePin so radar pins can be right-click dismissed
│   ├── DepletionPatches.cs      # Harmony hooks (Destructible/MineRock/MineRock5.Damage, Pickable.Interact): hide a depletable pin on the local player's first effective hit/pick; Pickable.SetPicked hides a regrowing pickable's pin until it respawns
│   ├── RespawnTimers.cs         # Pure store of picked regrowing pickables (berries/mushrooms/crops) hidden until their respawn world time
│   ├── DepletionRules.cs        # Pure rules for when a rescan's "not found" is conclusive enough to remove a depletable point
│   └── MinimapMarkerOrder.cs    # Raises the player/ship map markers above all pins (sibling order only)
├── Scanning/
│   ├── ClusteringEngine.cs        # Greedy distance-based clustering + ClusterGrid (indexed, incremental, rect queries)
│   ├── ObjectEvaluator.cs         # Thin composition root: categoryKey -> enabled-state/icon, dispatches to the 3 evaluators below
│   ├── NameFormatting.cs          # Shared name/display-text helpers (exact-alias matching, title-casing, prefix stripping)
│   ├── ScanFilters.cs             # Shared pre-filter (debris names, dungeon-interior objects) applied before any evaluator
│   ├── ScanGeometry.cs            # Shared Physics.OverlapBox cell geometry/query + ScanCellKey
│   ├── SpatialCellScanner.cs      # Rotating per-cell cache scanner - Type #1 (ephemeral: creatures)
│   ├── PermanentSpatialScanner.cs # Scan-new-cells-immediately + budgeted-rescan scanner - Types #2/#3 (semi-permanent: resources/physics-POI)
│   ├── CreatureEvaluator.cs       # Type #1 classification: creatures/fish/Leviathan (used by CreatureScanner)
│   ├── CreatureScanner.cs         # Type #1 scan loop, wraps SpatialCellScanner
│   ├── ResourceEvaluator.cs       # Type #2 classification: trees/ores/berries/ground pickables (used by ResourceScanner)
│   ├── ResourceScanner.cs         # Type #2 scan loop, wraps PermanentSpatialScanner
│   ├── PoiEvaluator.cs            # Type #3 classification: physics-detected POI fallback (used by PoiScanner)
│   ├── PoiScanner.cs              # Type #3 (physics fallback) scan loop, wraps PermanentSpatialScanner
│   └── LocationScanner.cs         # Type #3 (preferred): ZoneSystem-based POI discovery (dungeons, altars, ruins, etc.)
├── docs/
│   └── ICONS.md                  # Icon resolution order & override naming, for reference when touching Pinning/
├── Thunderstore/
│   └── Pack.ps1, manifest.template.json, README.md, CHANGELOG.md, icon.png   # ThunderstorePack packaging assets
├── RadarLog.cs                  # RadarLog.Diag: routine logging, silent unless DiagnosticLogging is on
└── RadarPlugin.cs               # Plugin lifecycle, update loop, and per-scanner Reset()/scan orchestration

ValheimRadar.Tests/              # xunit tests mirroring the folders above (Models/, Pinning/, Scanning/)
```

### How the pieces fit together

**Three content types, three lifecycles.** `RadarPlugin.Update` runs two independent timers
(`UpdateInterval` -> `ScanAndPinObjects`; `LocationScanInterval` -> `LocationScanner`), and the
content types behave differently:

| Type | Scanner | Behavior | Pin store |
|---|---|---|---|
| #1 creatures | `CreatureScanner` -> `SpatialCellScanner` | Rotating per-cell cache, re-queried forever; fully reclustered every tick | `PinManager.SyncTransientClusters` - in memory only |
| #2 resources, #3 physics-POI | `ResourceScanner`/`PoiScanner` -> `PermanentSpatialScanner` | New cells scanned immediately, re-scanned once inside the loaded area, then every `ResourceRescanInterval` (max 2 cells/tick); depletable points (`Rule.Depletable`) are hidden when the local player mines them (`DepletionPatches`) and removed when verified rescans miss them (`DepletionRules`); regrowing pickables are hidden once picked (`Pickable.SetPicked`, any player) until their `Pickable.m_respawnTimeMinutes` of world time passes or the owner reports them regrown (`RespawnTimerStore`, persisted in the world database) | `rawPersistentPoints` (keyed by ZDOID, bucketed by cell), clustered incrementally in a `ClusterGrid`, saved to disk; only clusters in the visible map area (+ margin) get Minimap pins |
| #3 Locations (dungeons, altars, ruins...) | `LocationScanner` | Reads `ZoneSystem`, no physics | `rawLocationPoints`, one pin per Location, no clustering, saved to disk |

**State and persistence.** `PinManager` is a static class holding all pin state; its persistence
half (`Pinning/PinManager.Persistence.cs`) keeps each world in one SQLite file at
`BepInEx/config/ValheimRadar/PinData/<world>.db` (`Persistence/PinDatabase.cs`: points, locations,
dismissed pins, respawn timers). Changes are queued and flushed as one small transaction every 2 s
and on disconnect - never rewrite whole tables from the scan/pin loops. SQLite is called through
our own P/Invoke layer (`Persistence/SqliteNative.cs`); the native libraries must ship next to the
plugin DLL (the build and `Pack.ps1` copy them). `libe_sqlite3.so` comes from the
`SQLitePCLRaw.lib.e_sqlite3` NuGet package; `e_sqlite3.dll` + `e_sqlite3.pdb` are built from the
official SQLite source by `Native/Build-ESqlite3.ps1` and committed under `Native/win-x64/`, because
mod hosts (Hexium) reject a native DLL without its PDB and the NuGet DLL ships none - to update
SQLite, bump the pinned version/hash in that script and rerun it. If the library can't load, pins still work but nothing is saved
that session. The pre-v1.12 `.txt` files are imported once by `OpenWorld` and then deleted
(`Persistence/LegacyPinFiles.cs`). Schema changes go through `PinDatabase.Migrate` (in place, one
transaction, bump `PinDatabase.SchemaVersion`). Every `Reset()`/`ClearAllPins()` must run on
disconnect so static state never leaks between worlds - when adding a new scanner or cache, wire it
into both reset sites in `RadarPlugin`.

**Nothing scales with the save size in a single frame.** A world can hold hundreds of thousands of
points, and a dedicated server drops a client that stalls ~30 s. So `OpenWorld` loads only the
small tables; resource points stream in per scan cell (`points.cell_x/cell_z`), nearest to the
player first, and a `ClusterDistance` change reclusters nearest-first too - both inside
`PinManager.Tick`'s per-frame time budget (`PinManager.Viewport.cs`). Resource pins are virtualized:
vanilla `Minimap.UpdatePins` walks every pin whenever the map moves, so only clusters inside the
shown map area plus a margin (`PinViewport`) exist as Minimap pins, added/removed as the map scrolls
or zooms. Keep new per-point or per-pin work incremental or budgeted the same way.

**`categoryKey` is the join key.** Scanners emit `creature:<id>`, `resource:<id>` (also used by
`PoiEvaluator` rules) or `location:<canonicalKey>`. Only the key is persisted, so
`ObjectEvaluator` re-derives enabled-state and icon from it after a relog or config toggle
without a live GameObject. Any new category must be resolvable through
`ObjectEvaluator.IsCategoryEnabled`/`GetDefaultIconForCategory`/`GetVanillaIconForCategory`, or
its pins will vanish or lose their icon on reload.

**Scan cells are Valheim zones.** `ScanGeometry.CellSize` is 64 m and cells are *centered* on
multiples of 64 (`GetCellIndex` = `floor((v+32)/64)`), mirroring `ZoneSystem.GetZone`. Don't
change either without re-checking the decompiled game - misalignment makes one cell straddle up
to four zones. `PermanentSpatialScanner` also skips a cell until `ZNetScene.IsAreaReady` says
its GameObjects have instantiated; marking a cell "scanned" before that permanently drops
resources on dedicated servers.

**Dedicated-server vs host paths in `LocationScanner`.** `ZoneSystem.GetLocationList()` is only
populated where `ZNet.IsServer()` (host/singleplayer). Plain clients must instead read
`LocationProxy` objects and map their ZDO's hashed prefab name back through
`ZoneSystem.m_locations`. Anything Location-related has to work on both paths, and neither can
be tested via `dotnet test`.

**Jotunn is a hard dependency, used only for icons** (`IconLoader`, `VanillaIconResolver`;
resolution order in `docs/ICONS.md`). Jotunn's lazy `AssetManager` init can throw on some mod
lists, so `VanillaIconResolver.TryResolveIcon` latches vanilla lookups off for the session
instead of crashing the scan loop. Keep any new Jotunn call behind that kind of guard.

**Logging.** Routine per-event output (pin created/updated/removed, scan summaries) goes through
`RadarLog.Diag`, which stays silent unless the `DiagnosticLogging` config is on. Only errors and
one-off warnings call `Debug.LogError`/`LogWarning` directly, so don't add raw `Debug.Log` calls
to the scan or pin loops.

**Tests cover pure logic only** (evaluator matching, clustering, cell geometry, config,
icon-name tables). Code that needs `Physics`, `ZNetScene`, `ZoneSystem` or `Minimap` can't run
under xunit, so keep decision logic in testable static methods and leave in-game behavior to
the manual PR checklist.

---

## Safety & Modification Boundaries

- **Do NOT** change the `ValheimRadar` namespace or break module boundaries.
- **Do NOT** move scanning logic back into `RadarPlugin.cs`. Keep classification in the
  relevant type-specific evaluator (`CreatureEvaluator`/`ResourceEvaluator`/`PoiEvaluator`) or
  `LocationScanner`, and clustering in `ClusteringEngine`. `ObjectEvaluator` is only a thin
  composition root/dispatcher (`categoryKey` -> enabled-state/icon) shared by
  `PinManager`/`RadarPlugin` - don't add matching rules to it directly.
- **Do NOT** duplicate name-formatting, exact-alias matching, or debris/dungeon-interior
  filtering logic inside a type-specific evaluator or scanner - that's what
  `NameFormatting.cs` and `ScanFilters.cs` are for; add to those shared files instead.
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

### Playbook 1: Adding a New Trackable Creature/Resource/POI

1. **Pick the right type/file** - ValheimRadar tracks three distinct kinds of content, each
   with its own scanner and evaluator (see `Scanning/`):
   - **Type #1 - ephemeral** (creatures, fish, the Leviathan): add a rule/alias to
     `Scanning/CreatureEvaluator.cs` (or `RadarConfig.CreatureDefinitions`). Scanned fresh every
     discovery tick via `CreatureScanner`/`SpatialCellScanner` - never persisted to disk.
   - **Type #2 - semi-permanent resources** (trees/ores/berries/ground pickables, wild
     beehives): add a `ResourceRule` to `Scanning/ResourceEvaluator.cs`. Scanned once per map
     cell, ever, via `ResourceScanner`/`PermanentSpatialScanner`, then persisted forever in
     `PinManager`'s raw point store.
   - **Type #3 - points of interest** (dungeons, boss altars, runestones, villages, ruins,
     chests, the trader): prefer adding a `LocationDefinition` to `RadarConfig.cs` (discovered
     cheaply via `Scanning/LocationScanner.cs`'s `ZoneSystem.GetLocationList()` query) when the
     object is a proper Location; only add a `PoiRule` to `Scanning/PoiEvaluator.cs`
     (physics-scan fallback, via `PoiScanner`/`PermanentSpatialScanner`) for POI-shaped objects
     with their own `ZNetView` that ZoneSystem doesn't expose as a Location.
2. **Add a Config Entry:** Add a `ConfigEntry<bool>` in `Configuration/RadarConfig.cs` under the
   appropriate master group section.
3. **Match correctly:** Use exact, lowercased prefab-name aliases via `NameFormatting.IsExactAlias`
   (through each evaluator's own `IsExactAlias` wrapper) - not substring matching. Assign the
   appropriate `displayName` (or `DisplayNameOverride`) and default PNG fallback icon; add a
   verified vanilla icon name in `Pinning/VanillaIconResolver.cs` where possible - see
   `docs/ICONS.md` for the full resolution order before touching icon code.
4. **Add/extend a unit test** in the matching `ValheimRadar.Tests/Scanning/*Tests.cs` file, then
   run `dotnet test`.
5. **Verify build:** `dotnet build -c Release` (see Critical Gotchas above - never a bare
   Debug `dotnet build` as an agent).

### Playbook 2: Modifying Clustering Logic

1. **Location:** Edits must be isolated strictly to `Scanning/ClusteringEngine.cs` and
   `Models/ItemCluster.cs`.
2. **Centroid Computation:** Ensure `GetCentroid()` handles zero-count lists safely to prevent
   `Vector3.zero` division errors.
3. **Keying:** Ensure `GetClusterKey()` produces unique keys per cluster to avoid pin
   flickering in `PinManager`.
4. **Add/extend a unit test** in `ValheimRadar.Tests/Models/ItemClusterTests.cs` and/or
   `ValheimRadar.Tests/Scanning/ClusteringEngineTests.cs`.

### Playbook 3: Refactoring / Adding Extensions

1. Check for `ZNetView` validity on all target objects.
2. ValheimRadar has a **hard** dependency on Jotunn (`[BepInDependency(Jotunn.Main.ModGuid)]` in
   `RadarPlugin.cs`) for minimap icon sprites - `Pinning/IconLoader.cs` uses
   `Jotunn.Utils.AssetUtils` to load user-supplied PNGs, and `Pinning/VanillaIconResolver.cs` uses
   `Jotunn.Managers.GUIManager` to pull verified built-in icons straight from Valheim's own icon
   atlas. Don't reintroduce a standalone PNG/sprite loader or a bespoke `Minimap.m_icons`
   registration path - route new icon sources through these two files. See `docs/ICONS.md` for
   the exact resolution order (`PinManager.ResolvePerObjectPin`).
3. If a new shared need arises across `CreatureEvaluator`/`ResourceEvaluator`/`PoiEvaluator`
   (e.g. another name-formatting helper or pre-filter rule), add it to `NameFormatting.cs` or
   `ScanFilters.cs` rather than copy-pasting into one evaluator.

---

## Versioning Policy

ValheimRadar follows semantic versioning (`MAJOR.MINOR.PATCH`). `PluginVersion` in `RadarPlugin.cs`
is the **single source of truth** - `Thunderstore/Pack.ps1` reads that constant directly at pack
time, so there is nowhere else to update by hand (no `manifest.json` version to keep in sync).

**Every version bump also adds a `## X.Y.Z - YYYY-MM-DD` entry at the top of `Thunderstore/CHANGELOG.md`**
(shown on the Thunderstore page; `Pack.ps1` refuses to pack without it). Write it for players, not
reviewers: what they'll notice, prefixed **Added/Changed/Fixed/Removed**, plus anything they need
to do (or a note that settings carry over).

**Every change that touches code under `ValheimRadar/` (anything other than a docs-only edit) must
bump `PluginVersion` as part of the same change.** Bump exactly one segment and follow standard
semver rollover (bumping MINOR resets PATCH to 0; bumping MAJOR resets MINOR and PATCH to 0; PATCH
has no upper limit, so `1.7.9` -> `1.7.10` is correct).

**PATCH is the default. Only pick MINOR or MAJOR when the change clearly meets that tier's
definition below - "it's a fairly big diff" or "it touches performance" is not enough. When in
doubt, PATCH.**

- **MAJOR** - fully new functionality, or the removal of core functionality. Example: adding a new
  scanner/evaluator type, dropping an entire tracked category outright, a save-format change with no
  migration path.
- **MINOR** - a user-facing capability or a config/save-data change that a user would need to be
  told about. It must meet at least one of these:
  - A new tracked **group/category** of content, or a new user-facing feature (e.g. a new pin
    type, a new scan mode).
  - A config key is **renamed, removed, split, or merged**, so existing users' `.cfg` files or
    saved pin data are affected (e.g. Chests -> Chests + Buried Chests).
  - A deliberate change to **what gets pinned or when**, visible to the user (e.g. changed
    default `ScanRadius`, a different discovery model), including when it is delivered as a
    performance change.
- **PATCH** - everything else, including:
  - Bug fixes of any size: missing/wrong icons, wrong names or aliases, pins that fail to appear
    or persist, dedicated-server fixes, crash/guard fixes.
  - Adding or tweaking entries **inside an existing group** (a new creature/resource/Location
    alias, its config toggle, its icon), moving a toggle between groups **without renaming its
    key**.
  - Performance work and internal alignment/optimization that doesn't change what gets pinned, when
    it's pinned, or any config key (e.g. tuning scan cell size, batching, caching).
  - Refactors, cleanup, logging, tests, and documentation.

A PR that bundles a fix or small tweak with unrelated small changes is still **PATCH**. Bump MINOR
only when a MINOR-qualifying change is the actual point of the PR, not just one line of it.

Past examples: the missing Guck Sack/Surtling icon fix (#23) and the internal scan-cell/zone-grid
alignment (#37) were both released as v1.8.0, but are PATCH under this policy.

When opening a PR for a change that bumps the version, **include the new version number in the PR
title** (e.g. `Split chest tracking into above-ground/buried (v1.8.0)`), so the version bump is
visible without opening the diff.

---

## Verification Steps After Changes

Before submitting changes, ensure:
1. `dotnet test` passes (xunit tests in `ValheimRadar.Tests`, safe to run anytime).
2. `dotnet build -c Release` passes with 0 errors and 0 warnings (never a bare Debug
   `dotnet build` as an agent - see Critical Gotchas).
3. All new configuration keys/definitions are actually consumed inside
   `RadarConfig.Initialize()` (creatures/resources/POI) or the Location tables +
   `LocationScanner` (Locations/POIs).
4. No Unity game logic is invoked off the main thread (Unity API calls must stay on the main
   thread).

---

## Opening a Pull Request

Once the Verification Steps above pass, open the PR as follows:

1. **Title** - short, specific, and states what changed (not just "fix bug" or "update
   scanner"). If the change bumps `PluginVersion` (see Versioning Policy - anything other than
   a docs-only edit), append the new version in parentheses: `<Short summary> (vX.Y.Z)`, e.g.
   `Split chest tracking into above-ground/buried (v1.8.0)`. Omit the version suffix only for
   docs-only PRs that don't touch `ValheimRadar/` code.
2. **Description** - explain both *what* changed and *why* (the problem/goal it addresses),
   not a restatement of the diff. Reference the relevant Playbook or config/scanner/evaluator
   touched if useful context for reviewers.
3. **Checklist** - always include a checklist in the PR body covering build status, unit
   tests, and any manual/in-game review steps a human still needs to perform (this project's
   Jotunn/Valheim dependency means most icon, pin, and config-toggle behavior can't be verified
   by `dotnet test` or `dotnet build` alone - see Critical Gotchas #2). Use this template,
   trimming checklist items that don't apply and adding any that do:

   ```markdown
   ## Summary
   <What changed and the problem/goal it solves - 1-3 sentences or bullets>

   ## Checklist
   - [ ] `dotnet test` passes
   - [ ] `dotnet build -c Release` passes (0 errors, 0 warnings)
   - [ ] Manual in-game verification (trim to what applies):
     - [ ] New/changed config option toggles the feature on/off correctly
     - [ ] New or changed icon renders correctly on the minimap (not a missing/fallback sprite)
     - [ ] New creature/resource/structure/POI is actually detected and pinned in-game
     - [ ] Existing pins still update/clear correctly (no flicker, no stale/duplicate pins)
   ```

   Manual review items are anything requiring a human in a live game session (launching
   Valheim, toggling a config entry, checking a pin/icon appears as expected) - agents cannot
   perform these themselves (see Critical Gotcha #1) and must leave them unchecked for the
   human reviewer.
