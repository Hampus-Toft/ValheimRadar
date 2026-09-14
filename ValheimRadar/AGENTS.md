# Agent Instructions - ValheimRadar

This document defines operating guidelines, safety boundaries, and workflows for autonomous AI agents working on `ValheimRadar`.

---

## Project Context

- **Environment:** BepInEx 5 / .NET C# / Unity 2022+ / Valheim API.
- **Primary Goal:** Perform fast spatial scans around the local player, group entities into clusters, and display/update custom pins on the Valheim minimap.

---

## Safety & Modification Boundaries

- **Do NOT** change the `ValheimRadar` namespace or break module boundaries.
- **Do NOT** move scanning logic back into `RadarPlugin.cs`. Keep classification in the relevant type-specific evaluator (`CreatureEvaluator`/`ResourceEvaluator`/`PoiEvaluator`) and clustering in `ClusteringEngine`. `ObjectEvaluator` is only a thin dispatcher (categoryKey -> enabled-state/icon) shared by `PinManager`/`RadarPlugin` - don't add matching rules to it directly.
- **Do NOT** hardcode absolute file paths. Always use relative paths or BepInEx utilities like `Paths.ConfigPath`.
- **Do NOT** create persistent static state that leaks memory across server reconnects or world reloads.

---

## Agent Playbooks

### Playbook 1: Adding a New Trackable Resource/Entity

1. **Add Config Entry:** Add a `ConfigEntry<bool>` in `Configuration/RadarConfig.cs` under the appropriate master group section.
2. **Pick the right type/file** - ValheimRadar tracks three distinct kinds of content, each with its
   own scanner and evaluator (see `Scanning/`):
   - **Type #1 - ephemeral** (creatures, fish, the Leviathan): add a rule/alias to
     `Scanning/CreatureEvaluator.cs` (or `RadarConfig.CreatureDefinitions`). Scanned fresh every
     discovery tick via `CreatureScanner` - never persisted to disk.
   - **Type #2 - semi-permanent resources** (trees/ores/berries/ground pickables, wild beehives):
     add a `ResourceRule` to `Scanning/ResourceEvaluator.cs`. Scanned once per map cell, ever, via
     `ResourceScanner`, then persisted forever in `PinManager`'s raw point store.
   - **Type #3 - points of interest** (dungeons, boss altars, runestones, villages, ruins,
     chests, the trader): prefer adding a `LocationDefinition` to `RadarConfig.cs` (discovered
     cheaply via `Scanning/LocationScanner.cs`'s ZoneSystem query) when the object is a proper
     Location; only add a `PoiRule` to `Scanning/PoiEvaluator.cs` (physics-scan fallback, via
     `PoiScanner`) for POI-shaped objects with their own `ZNetView` that ZoneSystem doesn't expose
     as a Location.
   - Match the prefab or clean entity name (`nameLower`) using exact aliases, not substrings.
   - Assign the appropriate `displayName` (or `DisplayNameOverride`) and default PNG fallback icon.
3. **Verify Build:** Run `dotnet build` to ensure no compiler errors.

### Playbook 2: Modifying Clustering Logic

1. **Location:** Edits must be isolated strictly to `Scanning/ClusteringEngine.cs` and `Models/ItemCluster.cs`.
2. **Centroid Computation:** Ensure `GetCentroid()` handles zero-count lists safely to prevent `Vector3.zero` division errors.
3. **Keying:** Ensure `GetClusterKey()` produces unique keys per cluster to avoid pin flickering in `PinManager`.

### Playbook 3: Refactoring / Adding Extensions

1. Check for `ZNetView` validity on all target objects.
2. ValheimRadar has a **hard** dependency on Jotunn (`[BepInDependency(Jotunn.Main.ModGuid)]` in
   `RadarPlugin.cs`) for minimap icon sprites - `Pinning/IconLoader.cs` uses
   `Jotunn.Utils.AssetUtils` to load user-supplied PNGs, and `Pinning/VanillaIconResolver.cs` uses
   `Jotunn.Managers.GUIManager` to pull verified built-in icons straight from Valheim's own icon
   atlas. Don't reintroduce a standalone PNG/sprite loader or a bespoke `Minimap.m_icons`
   registration path - route new icon sources through these two files.

---

## Versioning Policy

ValheimRadar follows semantic versioning (`MAJOR.MINOR.PATCH`). `PluginVersion` in `RadarPlugin.cs`
is the **single source of truth** - `Thunderstore/Pack.ps1` reads that constant directly at pack
time, so there is nowhere else to update by hand (no `manifest.json` version to keep in sync).

**Every change that touches code under `ValheimRadar/` (anything other than a docs-only edit) must
bump `PluginVersion` as part of the same change.** Bump exactly one segment - whichever tier matches
the *most significant* part of the diff - and follow standard semver rollover (bumping MINOR resets
PATCH to 0; bumping MAJOR resets MINOR and PATCH to 0):

- **MAJOR** - fully new functionality, or the removal of core functionality. Example: adding a new
  scanner/evaluator type, dropping an entire tracked category outright, a save-format change with no
  migration path.
- **MINOR** - performance changes, and smaller feature additions or removals. Example: adding a new
  trackable entity to an existing category, reorganizing/renaming config toggles, splitting or
  merging an existing toggle (e.g. Chests -> Chests + Buried Chests), tuning scan performance.
- **PATCH** - documentation, cleanup/refactors with no behavior change, small bug fixes.

When opening a PR for a change that bumps the version, **include the new version number in the PR
title** (e.g. `Split chest tracking into above-ground/buried (v1.8.0)`), so the version bump is
visible without opening the diff.

---

## Verification Steps After Changes

Before submitting changes, ensure:
1. `dotnet build` passes with 0 errors and 0 warnings.
2. All new configuration keys are initialized inside `RadarConfig.Initialize()`.
3. No Unity game logic is invoked off the main thread (Unity API calls must stay on the main thread).
4. `PluginVersion` in `RadarPlugin.cs` has been bumped per the Versioning Policy above (unless the
   change is docs-only), and the new version number is in the PR title.