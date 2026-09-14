using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Type #3 - points of interest discovered via physics scan: dungeon entrances, runestones,
    // abandoned ruins, monster-spawner landmarks, the trader, and natural loot chests. Mostly
    // permanent like resources - see PoiScanner, which shares ResourceScanner's "scan each cell
    // once" model (PermanentSpatialScanner).
    //
    // World Locations proper (the ZoneSystem-driven POI roster - curated dungeons/ruins/boss altars
    // /runestones) are discovered separately and far more cheaply via LocationScanner.cs, which
    // reads directly from Valheim's own ZoneSystem instead of a physics scan. This evaluator only
    // exists as a defense-in-depth net for POI-shaped objects that curated table doesn't already own
    // (see RadarConfig.IsKnownLocationPrefab) - most Location prefab roots have no ZNetView of their
    // own, so a physics scan alone would miss them anyway; this only catches the ones that do.
    //
    // categoryKey values here keep the "resource:" prefix these rules used before this file existed
    // (when they lived in the same table as ResourceEvaluator's rules) - that prefix is what's
    // persisted to disk (see PinManager.SaveWorldPins) and checked by config lookups, and changing
    // it would require a save-file migration for no behavioral benefit.
    internal static class PoiEvaluator
    {
        internal sealed class PoiRule
        {
            public readonly string Id;
            public readonly Func<bool> Enabled;
            public readonly Func<GameObject, string, bool> Matches;
            public readonly string IconPng;
            public readonly string VanillaIcon;
            public readonly Func<string, string> DisplayNameOverride;

            public PoiRule(string id, Func<bool> enabled, Func<GameObject, string, bool> matches, string iconPng, string vanillaIcon = null, Func<string, string> displayNameOverride = null)
            {
                Id = id;
                Enabled = enabled;
                Matches = matches;
                IconPng = iconPng;
                VanillaIcon = vanillaIcon;
                DisplayNameOverride = displayNameOverride;
            }
        }

        private static Func<string, string> Const(string label) => _ => label;
        private static bool IsExactAlias(string nameLower, params string[] aliases) => NameFormatting.IsExactAlias(nameLower, aliases);

        // Prefab name (lowercase) -> friendly dungeon label, for entrance prefabs NOT already owned
        // by RadarConfig.LocationDefinitions. What's left here is entrance content the curated
        // Location table doesn't cover. Anything with the right Teleport+DungeonGenerator signature
        // but not listed here still gets pinned via the "Dungeons" rule below, just as a generic
        // "Dungeon Entrance" - nothing is silently dropped, only unlabeled until confirmed.
        private static readonly Dictionary<string, string> DungeonEntranceNames = new Dictionary<string, string>
        {
            ["halfburried_forestcrypt"] = "Burial Chambers",
            ["hildir_crypt"] = "Burial Chambers (Hildir)",
            ["hildir_cave"] = "Frost Caves (Hildir)",
            ["bearcave"] = "Bear Cave",
        };

        // Every prefab name DungeonEntranceNames knows about, lowercased - matched directly (in
        // addition to the Teleport+DungeonGenerator component check below) because BearCave is
        // confirmed to carry only a Teleport component, no DungeonGenerator, so the component check
        // alone would never catch it.
        private static readonly string[] KnownDungeonEntranceAliases =
        {
            "halfburried_forestcrypt", "hildir_crypt", "hildir_cave", "bearcave",
        };

        internal static readonly PoiRule[] Rules =
        {
            // vendor_blackforest (Haldor) is owned by RadarConfig.LocationDefinitions' Landmarks
            // group (AlwaysEnabled - see RadarConfig) - discovered reliably via ZoneSystem instead of
            // this physics-scan fallback. This rule is the Bog Witch's camp, a distinct trader NPC.
            new PoiRule("Trader", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackTrader.Value, (go, n) => IsExactAlias(n, "bogwitch_camp"), "ruin.png", null, Const("Bog Witch Camp")),

            // CHESTS: exact whitelist of natural/world-spawn loot containers, split into above-ground
            // (Chests) vs buried/hidden (BuriedChests) per their own toggles. Player-buildable chests
            // (piece_chest*) are structurally IDENTICAL (same Container+Piece+WearNTear signature) so
            // there is no component-based way to exclude them - naming is the only signal, which is
            // exactly why this had to become a whitelist instead of "any Container component".
            new PoiRule("Chests", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackChests.Value, (go, n) => IsExactAlias(n,
                "treasurechest_meadows", "treasurechest_meadows_01", "treasurechest_meadows_02", "treasurechest_meadows_combat",
                "treasurechest_blackforest", "treasurechest_forestcrypt", "treasurechest_forestcrypt_hildir",
                "treasurechest_swamp", "treasurechest_sunkencrypt",
                "treasurechest_heath", "treasurechest_heath_hildir", "treasurechest_plains_stone", "treasurechest_plainsfortress_hildir",
                "treasurechest_mountains", "treasurechest_mountaincave", "treasurechest_mountaincave_hildir",
                "treasurechest_dvergrtower", "treasurechest_dvergrtown", "treasurechest_dvergr_loose_stone", "treasurechest_fcrypt",
                "treasurechest_charredfortress", "treasurechest_ashland_stone", "treasurechest_deepnorth_village", "treasurechest_morkhalla",
                "treasurechest_trollcave", "loot_chest_stone", "loot_chest_wood", "stonechest",
                "shipwreck_karve_chest", "shipwreck_vikingship_chest", "crypt_skeleton_chest", "morkhalla_chestancient"
            ), "chest.png", "chest_wood", Const("Chest")),

            new PoiRule("BuriedChests", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackBuriedChests.Value, (go, n) => IsExactAlias(n,
                "treasurechest_meadows_buried", "treasurechest_memorial_buried"
            ), "chest.png", "chest_wood", Const("Buried Chest")),

            // DUNGEON ENTRANCES (fallback only) - matched primarily by exact known prefab name
            // (KnownDungeonEntranceAliases), falling back to component-signature detection
            // (Teleport+DungeonGenerator, present on every real dungeon-plane entrance) for anything
            // not in that table. Component-fallback branch guarded against
            // RadarConfig.IsKnownLocationPrefab so it acts only as a defense-in-depth net for
            // entrances the curated LocationDefinitions table doesn't already own, not a duplicate
            // source for the ones it does. Gated by Group_DungeonLocations (not FunctionalStructures)
            // since this is the same "dungeon entrance" category as that curated Location group, just
            // its physics-scan catch-all.
            new PoiRule("Dungeons", () => RadarConfig.Group_DungeonLocations.Value && RadarConfig.TrackUnlistedDungeons.Value,
                (go, n) => IsExactAlias(n, KnownDungeonEntranceAliases) || (!RadarConfig.IsKnownLocationPrefab(n) && go.GetComponent<Teleport>() != null && go.GetComponent<DungeonGenerator>() != null),
                "dungeon.png", null, n => DungeonEntranceNames.TryGetValue(n, out string label) ? label : "Dungeon Entrance"),

            // RUNESTONES (fallback only) - every biome runestone carries a RuneStone component, so
            // this stays component-based, but guarded against RadarConfig.IsKnownLocationPrefab so
            // it only catches runestones NOT already owned by RadarConfig.LocationDefinitions'
            // Runestones group (which discovers every biome variant reliably via ZoneSystem). Gated
            // by Group_RunestoneLocations, the same group that governs the curated list.
            new PoiRule("Runestones", () => RadarConfig.Group_RunestoneLocations.Value && RadarConfig.TrackRunestones.Value, (go, n) => !RadarConfig.IsKnownLocationPrefab(n) && go.GetComponent<RuneStone>() != null, "runestone.png"),

            // ABANDONED RUINS (fallback only) - combatruin01/woodvillage2 have no equivalent in
            // RadarConfig.LocationDefinitions - everything else this rule used to match is now owned
            // by the Ruins & Structures group, discovered reliably via ZoneSystem instead of this
            // physics-scan fallback. Gated by Group_RuinLocations, the same group that governs the
            // curated list.
            new PoiRule("AbandonedRuins", () => RadarConfig.Group_RuinLocations.Value && RadarConfig.TrackAbandonedRuins.Value, (go, n) => IsExactAlias(n, "combatruin01", "woodvillage2"), "ruin.png"),

            // SPAWNERS & LANDMARKS (monster spawners / harvestable Guck Sacks)
            new PoiRule("GreydwarfNest", () => RadarConfig.Group_SpawnersAndLandmarks.Value && RadarConfig.TrackGreydwarfNest.Value, (go, n) => IsExactAlias(n, "spawner_greydwarfnest"), "ruin.png", null, Const("Greydwarf Nest")),
            new PoiRule("BodyPile", () => RadarConfig.Group_SpawnersAndLandmarks.Value && RadarConfig.TrackBodyPile.Value, (go, n) => IsExactAlias(n, "spawner_draugrpile"), "ruin.png", null, Const("Body Pile")),
            new PoiRule("BonePile", () => RadarConfig.Group_SpawnersAndLandmarks.Value && RadarConfig.TrackBonePile.Value, (go, n) => IsExactAlias(n, "bonepilespawner", "bonepilespawner_swamp"), "ruin.png", null, Const("Bone Pile")),
            // "guck" is the vanilla pickup-item icon for the Guck material these sacks drop -
            // verified against Jotunn's sprite atlas (see VanillaIconResolver's header comment for
            // why verified names, not guesses, matter here).
            new PoiRule("Guck", () => RadarConfig.Group_SpawnersAndLandmarks.Value && RadarConfig.TrackGuck.Value, (go, n) => IsExactAlias(n, "gucksack", "gucksack_small"), "ruin.png", "guck", Const("Guck Sack")),
        };

        internal static bool TryClassify(GameObject go, string nameLower, out string displayName, out Sprite icon, out string categoryKey)
        {
            displayName = string.Empty;
            icon = null;
            categoryKey = null;

            foreach (var rule in Rules)
            {
                if (!rule.Matches(go, nameLower)) continue;

                displayName = rule.DisplayNameOverride != null ? rule.DisplayNameOverride(nameLower) : NameFormatting.DeriveDisplayName(go, null);
                icon = PinManager.ResolvePerObjectPin(nameLower, rule.IconPng, rule.VanillaIcon);
                categoryKey = $"resource:{rule.Id}";
                return true;
            }

            return false;
        }

        internal static bool TryGetRule(string id, out PoiRule rule)
        {
            foreach (var r in Rules)
            {
                if (r.Id != id) continue;
                rule = r;
                return true;
            }

            rule = null;
            return false;
        }
    }
}
