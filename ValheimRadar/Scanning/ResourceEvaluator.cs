using System;
using UnityEngine;

namespace ValheimRadar
{
    // Type #2 - semi-permanent resources: berries, mushrooms, wild flowers & crops, ground
    // pickables (flint/stone/wood), ore deposits, and wild beehives - world resource NODES only,
    // never loose item drops (see the ORES section and ScanFilters). These rarely
    // move; once discovered they're recorded in PinManager's raw point store (see
    // PinManager.RecordScannedCells/rawPersistentPoints) and kept until a Depletable one is mined
    // out or found missing by a rescan - see ResourceScanner/PermanentSpatialScanner.
    //
    // categoryKey values here keep the "resource:" prefix used before this file existed, even
    // though POI-shaped rules formerly living in this same table (dungeons, runestones, chests,
    // etc.) have moved to PoiEvaluator - that prefix is what's persisted to disk (see
    // PinManager.SaveWorldPins) and checked by config lookups, and changing it would require a save
    // -file migration for no behavioral benefit.
    internal static class ResourceEvaluator
    {
        internal sealed class ResourceRule
        {
            public readonly string Id;
            public readonly Func<bool> Enabled;
            public readonly Func<GameObject, string, bool> Matches;
            public readonly string IconPng;
            public readonly string VanillaIcon;
            public readonly Func<string, string> DisplayNameOverride; // nameLower -> label; null = keep hover-text/go.name derived name

            // True for resource nodes that never grow back once taken (ore deposits/veins/scrap
            // piles, flint/stone/branches - their Pickable has no respawn timer, so picking destroys it).
            // Only these have their pins removed when the player mines/picks them or a rescan finds
            // them gone (see PinManager.MarkDepletedAt / RecordScannedCells). Regrowing pickables
            // (berries, mushrooms, flowers, crops) must stay false: a picked one hides its visuals
            // and colliders, so a rescan would wrongly see it as gone.
            public readonly bool Depletable;

            // Ore deposits (copper/tin/silver/obsidian deposits, iron scrap piles): every deposit gets
            // its own pin - never clustered (see ClusteringEngine.AddItem) - and the pin carries no
            // label when its icon already identifies the ore (see PinManager.UpdateOrCreatePin).
            // Their DisplayNameOverride is just the ore's name ("Silver"), used when no such icon resolved.
            public readonly bool OreDeposit;

            // The pin drops the type name when its icon already identifies the type (Valheim's own item
            // icon or a per-prefab PNG - see PinManager.IconIdentifiesType), keeping only a cluster's
            // count ("5x"). Set for regrowing pickables (berries, mushrooms, flowers, crops); always true
            // for OreDeposit rules.
            public readonly bool IconOnlyLabel;

            // Crops the Cultivator can also plant. Every Cultivator sapling grows into one of these exact
            // Pickable_* prefabs (sapling_onion -> Pickable_Onion, sapling_seedonion -> Pickable_SeedOnion,
            // sapling_magecap -> Pickable_Mushroom_Magecap, ...) and can only be planted on cultivated
            // ground (Piece.m_cultivatedGroundOnly, verified in the game's asset bundles), so the prefab
            // name can't tell a wild crop from a player's farm - only the ground under it can. A
            // Plantable match standing on cultivated ground is never pinned (see MatchesWild).
            public readonly bool Plantable;

            public ResourceRule(string id, Func<bool> enabled, Func<GameObject, string, bool> matches, string iconPng, string vanillaIcon = null, Func<string, string> displayNameOverride = null, bool depletable = false, bool oreDeposit = false, bool iconOnlyLabel = false, bool plantable = false)
            {
                IconOnlyLabel = iconOnlyLabel || oreDeposit;
                Id = id;
                Enabled = enabled;
                Matches = matches;
                IconPng = iconPng;
                VanillaIcon = vanillaIcon;
                DisplayNameOverride = displayNameOverride;
                Depletable = depletable;
                OreDeposit = oreDeposit;
                Plantable = plantable;
            }
        }

        private static Func<string, string> Const(string label) => _ => label;
        private static bool IsExactAlias(string nameLower, params string[] aliases) => NameFormatting.IsExactAlias(nameLower, aliases);

        // Evaluated in this order. The first rule whose group+track toggle is enabled and whose
        // predicate matches wins. Every predicate matches EXACT prefab names (see IsExactAlias), not
        // substrings - exact names were sourced from Jotunn's generated prefab list, not guessed,
        // and VanillaIcon values are exact, verified names from Valheim's own icon atlas wherever
        // one is confirmed to exist; a rule with VanillaIcon = null falls back to its category
        // PNG/built-in default rather than risk a wrong guessed sprite name.
        internal static readonly ResourceRule[] Rules =
        {
            // BERRIES (wild bushes only - exact match excludes the hammer-placeable decoration
            // items "Raspberry"/"Blueberries"/"Cloudberry", which are different, Piece-based
            // prefabs that happen to share the same base word).
            new ResourceRule("Raspberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackRaspberry.Value, (go, n) => IsExactAlias(n, "raspberrybush"), "berry.png", "raspberry", iconOnlyLabel: true),
            new ResourceRule("Blueberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackBlueberry.Value, (go, n) => IsExactAlias(n, "blueberrybush"), "berry.png", "blueberries", iconOnlyLabel: true),
            new ResourceRule("Cloudberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackCloudberry.Value, (go, n) => IsExactAlias(n, "cloudberrybush"), "berry.png", "cloudberry", iconOnlyLabel: true),

            // MUSHROOMS (wild only - no plantable equivalent exists in vanilla Valheim)
            new ResourceRule("RedMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackRedMushroom.Value, (go, n) => IsExactAlias(n, "pickable_mushroom"), "mushroom.png", "mushroom", iconOnlyLabel: true),
            new ResourceRule("YellowMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackYellowMushroom.Value, (go, n) => IsExactAlias(n, "pickable_mushroom_yellow"), "mushroom.png", "mushroomyellow", iconOnlyLabel: true),
            new ResourceRule("BlueMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackBlueMushroom.Value, (go, n) => IsExactAlias(n, "pickable_mushroom_blue"), "mushroom.png", "mushroomblue", iconOnlyLabel: true),

            // FLOWERS & CROPS. Dandelion/Thistle are wild-only. Carrot/Turnip/Onion/Barley/Flax/
            // Magecap are ALSO plantable via the Cultivator: a grown sapling is replaced by the very
            // same Pickable_* prefabs matched below (Plant.Grow), so these rules are Plantable and
            // skip anything standing on cultivated ground - see ResourceRule.Plantable.
            new ResourceRule("Dandelion", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackDandelion.Value, (go, n) => IsExactAlias(n, "pickable_dandelion"), "crop.png", "dandelion", iconOnlyLabel: true),
            new ResourceRule("Thistle", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackThistle.Value, (go, n) => IsExactAlias(n, "pickable_thistle"), "crop.png", "thistle", iconOnlyLabel: true),
            new ResourceRule("CarrotSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackCarrotSeed.Value, (go, n) => IsExactAlias(n, "pickable_carrot", "pickable_seedcarrot"), "crop.png", "carrotseeds", iconOnlyLabel: true, plantable: true),
            new ResourceRule("TurnipSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackTurnipSeed.Value, (go, n) => IsExactAlias(n, "pickable_turnip", "pickable_seedturnip"), "crop.png", "turnipseeds", iconOnlyLabel: true, plantable: true),
            new ResourceRule("OnionSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackOnionSeed.Value, (go, n) => IsExactAlias(n, "pickable_onion", "pickable_seedonion"), "crop.png", "onionseeds", iconOnlyLabel: true, plantable: true),
            new ResourceRule("Barley", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackBarley.Value, (go, n) => IsExactAlias(n, "pickable_barley", "pickable_barley_wild"), "crop.png", "barley", iconOnlyLabel: true, plantable: true),
            new ResourceRule("Flax", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackFlax.Value, (go, n) => IsExactAlias(n, "pickable_flax", "pickable_flax_wild"), "crop.png", "flax", iconOnlyLabel: true, plantable: true),
            new ResourceRule("Magecap", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackMagecap.Value, (go, n) => IsExactAlias(n, "pickable_mushroom_magecap"), "crop.png", "mushroommagecap", iconOnlyLabel: true, plantable: true),

            // GROUND PICKABLES. Exact match specifically excludes "placeable_stone" - a player
            // hammer-placeable decoration that (surprisingly) also carries a live Pickable
            // component, so a Contains("stone")+Pickable-component guard would have matched it too.
            new ResourceRule("Flint", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackFlint.Value, (go, n) => IsExactAlias(n, "pickable_flint"), "ground.png", "flint", depletable: true),
            new ResourceRule("Stone", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackStone.Value, (go, n) => IsExactAlias(n, "pickable_stone", "pickable_stonerock"), "ground.png", "stone", depletable: true),
            new ResourceRule("Wood", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackWood.Value, (go, n) => IsExactAlias(n, "pickable_branch", "pickable_branch_snow"), "ground.png", "wood", depletable: true),

            // ORES - resource NODES only (the deposits/veins/piles players mine). Loose item drops
            // (raw ore, scrap, ingots - e.g. a smelter's output or ore a player dropped) are never
            // tracked: ScanFilters rejects anything with an ItemDrop component, and there are
            // deliberately no rules for their prefab names. The old Ore/Ingot rules were removed and
            // their saved points are dropped on load (see PinManager.MigrateLegacyCategoryKey).
            new ResourceRule("CopperDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "minerock_copper", "rock4_copper", "rock4_copper_frac"), "ore.png", "copperore", Const("Copper"), depletable: true, oreDeposit: true),

            // Tin is a vanilla quirk: MineRock_Tin (Destructible, no MineRock component) IS the
            // deposit object itself, unlike Copper's separate MineRock_Copper vein + rock4_copper
            // surface node.
            new ResourceRule("TinDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "minerock_tin"), "ore.png", "TinOre", Const("Tin"), depletable: true, oreDeposit: true),

            // Iron has no confirmed surface "deposit" node in vanilla world-gen - the source is the
            // "Muddy scrap pile" (hover text $piece_mudpile), which directly drops IronScrap (no
            // separate vein object). Confirmed by inspecting Valheim's asset bundles: the piles that
            // world-gen scatters across the SWAMP surface (ZoneSystem vegetation, biome Swamp) are the
            // prefab "mudpile_beacon" (Destructible + Beacon) - NOT "mudpile"/"mudpile2", which are only
            // placed inside Sunken Crypts (filtered as dungeon interior by ScanFilters) - so omitting
            // it left every real swamp pile unpinned (issue #38). "mudpile_old" is a further variant
            // (MineRock, same hover text). The "mudpile_frac"/"mudpile2_frac" fragments are debris and
            // stay excluded by ScanFilters' "_frac" marker. The dropped "IronScrap" item is not a node
            // and is deliberately not an alias. minerock_iron is a real, registered prefab (MineRock component, matching
            // the MineRock_Copper/_Tin pattern) but unconfirmed whether vanilla world-gen ever actually
            // places it - included defensively since a name that's never placed simply never
            // matches, at no cost.
            new ResourceRule("IronScrap", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => IsExactAlias(n, "mudpile", "mudpile2", "mudpile_old", "mudpile_beacon", "pickable_bogironore", "minerock_iron"), "ore.png", "ironscrap", Const("Iron"), depletable: true, oreDeposit: true),

            new ResourceRule("SilverDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silvervein", "silvervein_frac", "rock3_silver", "rock3_silver_frac"), "ore.png", "silverore", Const("Silver"), depletable: true, oreDeposit: true),

            new ResourceRule("ObsidianDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackObsidian.Value, (go, n) => IsExactAlias(n, "minerock_obsidian"), "ore.png", "obsidian", Const("Obsidian"), depletable: true, oreDeposit: true),

            // Wild beehives - harvestable and renewable like the berries/mushrooms/crops above,
            // rather than a fixed structure, so it lives here rather than in PoiEvaluator.
            new ResourceRule("Beehives", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackBeehives.Value, (go, n) => IsExactAlias(n, "beehive"), "beehive.png", "beehive"),
        };

        internal static bool TryClassify(GameObject go, string nameLower, out string displayName, out Sprite icon, out string categoryKey)
        {
            displayName = string.Empty;
            icon = null;
            categoryKey = null;

            foreach (var rule in Rules)
            {
                if (!MatchesWild(rule, go, nameLower)) continue;

                displayName = rule.DisplayNameOverride != null ? rule.DisplayNameOverride(nameLower) : NameFormatting.DeriveDisplayName(go, null);
                icon = PinManager.ResolvePerObjectPin(nameLower, rule.IconPng, rule.VanillaIcon);
                categoryKey = $"resource:{rule.Id}";
                return true;
            }

            return false;
        }

        // First rule matching the object (same order TryClassify uses), whatever its toggle state.
        internal static bool TryMatchRule(GameObject go, string nameLower, out ResourceRule rule)
        {
            foreach (var r in Rules)
            {
                if (!MatchesWild(r, go, nameLower)) continue;
                rule = r;
                return true;
            }

            rule = null;
            return false;
        }

        // A rule's own match, minus player-planted crops (see ResourceRule.Plantable). Name-only
        // lookups (no GameObject) have no ground to check and keep the plain name match.
        private static bool MatchesWild(ResourceRule rule, GameObject go, string nameLower)
        {
            if (!rule.Matches(go, nameLower)) return false;
            return !rule.Plantable || go == null || !ScanFilters.IsOnCultivatedGround(go.transform.position);
        }

        internal static bool TryGetRule(string id, out ResourceRule rule)
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
