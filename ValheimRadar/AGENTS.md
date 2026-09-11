# Agent Instructions - ValheimRadar

This document defines operating guidelines, safety boundaries, and workflows for autonomous AI agents working on `ValheimRadar`.

---

## Project Context

- **Environment:** BepInEx 5 / .NET C# / Unity 2022+ / Valheim API.
- **Primary Goal:** Perform fast spatial scans around the local player, group entities into clusters, and display/update custom pins on the Valheim minimap.

---

## Safety & Modification Boundaries

- **Do NOT** change the `ValheimRadar` namespace or break module boundaries.
- **Do NOT** move scanning logic back into `RadarPlugin.cs`. Keep evaluation in `ObjectEvaluator` and clustering in `ClusteringEngine`.
- **Do NOT** hardcode absolute file paths. Always use relative paths or BepInEx utilities like `Paths.ConfigPath`.
- **Do NOT** create persistent static state that leaks memory across server reconnects or world reloads.

---

## Agent Playbooks

### Playbook 1: Adding a New Trackable Resource/Entity

1. **Add Config Entry:** Add a `ConfigEntry<bool>` in `Configuration/RadarConfig.cs` under the appropriate master group section.
2. **Update Evaluation Logic:** In `Scanning/ObjectEvaluator.cs`:
   - Match the prefab or clean entity name (`nameLower`).
   - Assign the appropriate `displayName` and default PNG fallback icon.
3. **Verify Build:** Run `dotnet build` to ensure no compiler errors.

### Playbook 2: Modifying Clustering Logic

1. **Location:** Edits must be isolated strictly to `Scanning/ClusteringEngine.cs` and `Models/ItemCluster.cs`.
2. **Centroid Computation:** Ensure `GetCentroid()` handles zero-count lists safely to prevent `Vector3.zero` division errors.
3. **Keying:** Ensure `GetClusterKey()` produces unique keys per cluster to avoid pin flickering in `PinManager`.

### Playbook 3: Refactoring / Adding Extensions

1. Check for `ZNetView` validity on all target objects.
2. Ensure soft dependencies (`KGvalheim.MoreMapPins`, `Arielle.MoreMapPins`) are handled without introducing hard assembly crash dependencies.

---

## Verification Steps After Changes

Before submitting changes, ensure:
1. `dotnet build` passes with 0 errors and 0 warnings.
2. All new configuration keys are initialized inside `RadarConfig.Initialize()`.
3. No Unity game logic is invoked off the main thread (Unity API calls must stay on the main thread).