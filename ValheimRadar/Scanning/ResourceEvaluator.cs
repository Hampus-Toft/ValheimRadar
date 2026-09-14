using System;
using UnityEngine;

namespace ValheimRadar
{
    // Type #2 - semi-permanent resources: berries, mushrooms, wild flowers & crops, ground
    // pickables (flint/stone/wood), ore deposits/raw ore/ingots, and wild beehives. These rarely
    // move; once discovered they're recorded forever in PinManager's raw point store (see
    // PinManager.RecordRawPoints/rawPersistentPoints) regardless of whether the ground they sit on
    // is ever physically re-queried again - see ResourceScanner/PermanentSpatialScanner, which only
    // ever scans map cells that have never been scanned before.
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

            public ResourceRule(string id, Func<bool> enabled, Func<GameObject, string, bool> matches, string iconPng, string vanillaIcon = null, Func<string, string> displayNameOverride = null)
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
            new ResourceRule("Raspberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackRaspberry.Value, (go, n) => IsExactAlias(n, "raspberrybush"), "berry.png", "raspberry"),
            new ResourceRule("Blueberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackBlueberry.Value, (go, n) => IsExactAlias(n, "blueberrybush"), "berry.png", "blueberries"),
            new ResourceRule("Cloudberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackCloudberry.Value, (go, n) => IsExactAlias(n, "cloudberrybush"), "berry.png", "cloudberry"),

            // MUSHROOMS (wild only - no plantable equivalent exists in vanilla Valheim)
            new ResourceRule("RedMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackRedMushroom.Value, (go, n) => IsExactAlias(n, "pickable_mushroom"), "mushroom.png", "mushroom"),
            new ResourceRule("YellowMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackYellowMushroom.Value, (go, n) => IsExactAlias(n, "pickable_mushroom_yellow"), "mushroom.png", "mushroomyellow"),
            new ResourceRule("BlueMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackBlueMushroom.Value, (go, n) => IsExactAlias(n, "pickable_mushroom_blue"), "mushroom.png", "mushroomblue"),

            // FLOWERS & CROPS. Dandelion/Thistle are wild-only. Carrot/Turnip/Onion/Barley/Flax/
            // Magecap are ALSO plantable via the Cultivator, but confirmed as genuinely separate
            // prefabs from their wild Pickable_* counterparts - the planted piece is a Plant+Piece
            // "sapling_*" object that is harvested in place and never spawns a Pickable_*
            // GameObject - so matching only the Pickable_* names below permanently excludes
            // anything player-planted.
            new ResourceRule("Dandelion", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackDandelion.Value, (go, n) => IsExactAlias(n, "pickable_dandelion"), "crop.png", "dandelion"),
            new ResourceRule("Thistle", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackThistle.Value, (go, n) => IsExactAlias(n, "pickable_thistle"), "crop.png", "thistle"),
            new ResourceRule("CarrotSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackCarrotSeed.Value, (go, n) => IsExactAlias(n, "pickable_carrot", "pickable_seedcarrot"), "crop.png", "carrotseeds"),
            new ResourceRule("TurnipSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackTurnipSeed.Value, (go, n) => IsExactAlias(n, "pickable_turnip", "pickable_seedturnip"), "crop.png", "turnipseeds"),
            new ResourceRule("OnionSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackOnionSeed.Value, (go, n) => IsExactAlias(n, "pickable_onion", "pickable_seedonion"), "crop.png", "onionseeds"),
            new ResourceRule("Barley", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackBarley.Value, (go, n) => IsExactAlias(n, "pickable_barley", "pickable_barley_wild"), "crop.png", "barley"),
            new ResourceRule("Flax", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackFlax.Value, (go, n) => IsExactAlias(n, "pickable_flax", "pickable_flax_wild"), "crop.png", "flax"),
            new ResourceRule("Magecap", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackMagecap.Value, (go, n) => IsExactAlias(n, "pickable_mushroom_magecap"), "crop.png", "mushroommagecap"),

            // GROUND PICKABLES. Exact match specifically excludes "placeable_stone" - a player
            // hammer-placeable decoration that (surprisingly) also carries a live Pickable
            // component, so a Contains("stone")+Pickable-component guard would have matched it too.
            new ResourceRule("Flint", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackFlint.Value, (go, n) => IsExactAlias(n, "pickable_flint"), "ground.png", "flint"),
            new ResourceRule("Stone", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackStone.Value, (go, n) => IsExactAlias(n, "pickable_stone", "pickable_stonerock"), "ground.png", "stone"),
            new ResourceRule("Wood", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackWood.Value, (go, n) => IsExactAlias(n, "pickable_branch", "pickable_branch_snow"), "ground.png", "wood"),

            // ORES - each metal split into Deposit (uncollected world vein/node) / Ore (dropped or
            // picked raw ore item) / Ingot (smelted bar) buckets, so they can never again render as
            // one conflated pin. DisplayNameOverride guarantees the three buckets stay visually
            // distinct on the map regardless of whether Valheim's own hover text/localization
            // cooperates.
            new ResourceRule("CopperDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "minerock_copper", "rock4_copper", "rock4_copper_frac"), "ore.png", "copperore", Const("Copper Deposit")),
            new ResourceRule("CopperOre", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "copperore"), "ore.png", "copperore", Const("Copper Ore")),
            new ResourceRule("CopperIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "copper"), "ore.png", "bar_copper_stack", Const("Copper")),

            // Tin is a vanilla quirk: MineRock_Tin (Destructible, no MineRock component) IS the
            // deposit object itself, unlike Copper's separate MineRock_Copper vein + rock4_copper
            // surface node.
            new ResourceRule("TinDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "minerock_tin"), "ore.png", "TinOre", Const("Tin Deposit")),
            new ResourceRule("TinOre", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "tinore"), "ore.png", "TinOre", Const("Tin Ore")),
            new ResourceRule("TinIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "tin"), "ore.png", "bar_tin_stack", Const("Tin")),

            // Iron has no confirmed surface "deposit" node in vanilla world-gen - the classic source
            // is digging mudpile/mudpile2 in Sunken Crypts, which directly drops IronScrap (no
            // separate vein object). Exact match on "iron" excludes every false-positive that broke
            // this before (fire_pit_iron, piece_cookingstation_iron, ArmorIronChest, SwordIron,
            // iron_grate, ...) by construction, since none of those raw names equal "iron".
            // minerock_iron is a real, registered prefab (MineRock component, matching the
            // MineRock_Copper/_Tin pattern) but unconfirmed whether vanilla world-gen ever actually
            // places it - included defensively since a name that's never placed simply never
            // matches, at no cost.
            new ResourceRule("IronScrap", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => IsExactAlias(n, "ironscrap", "mudpile", "mudpile2", "pickable_bogironore", "minerock_iron"), "ore.png", "ironscrap", Const("Iron Scrap")),
            new ResourceRule("IronIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => IsExactAlias(n, "iron"), "ore.png", null, Const("Iron")),

            new ResourceRule("SilverDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silvervein", "silvervein_frac", "rock3_silver", "rock3_silver_frac"), "ore.png", "silverore", Const("Silver Deposit")),
            new ResourceRule("SilverOre", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silverore"), "ore.png", "silverore", Const("Silver Ore")),
            new ResourceRule("SilverIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silver"), "ore.png", null, Const("Silver")),

            // Obsidian is used directly as a mined material - no smelting step, so no separate
            // Ore/Ingot split (confirmed no ObsidianOre/ObsidianIngot prefab exists).
            new ResourceRule("ObsidianDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackObsidian.Value, (go, n) => IsExactAlias(n, "minerock_obsidian"), "ore.png", null, Const("Obsidian Deposit")),

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
                if (!rule.Matches(go, nameLower)) continue;

                displayName = rule.DisplayNameOverride != null ? rule.DisplayNameOverride(nameLower) : NameFormatting.DeriveDisplayName(go, null);
                icon = PinManager.ResolvePerObjectPin(nameLower, rule.IconPng, rule.VanillaIcon);
                categoryKey = $"resource:{rule.Id}";
                return true;
            }

            return false;
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
