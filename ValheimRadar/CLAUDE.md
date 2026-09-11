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
│   └── TrackedItem.cs       # DTO representing a scanned entity (ZDOID, Position, PinType)
├── Pinning/
│   ├── CustomPinLoader.cs   # PNG texture loading and custom pin registration
│   └── PinManager.cs        # Minimap pin sync, updates, removals, and icon routing
├── Scanning/
│   ├── ClusteringEngine.cs  # Spatial distance-based point-clustering logic
│   └── ObjectEvaluator.cs   # Entity classification, star rating parsing, and filtering
└── RadarPlugin.cs           # Plugin lifecycle, update loop, and overlap scanning