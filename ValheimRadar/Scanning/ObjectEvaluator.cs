using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimRadar
{
    public static class ObjectEvaluator
    {
        private sealed class ResourceRule
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

        // Exact match against the raw, lowercased, (Clone)-stripped GameObject name (exactly what
        // BatchScanner produces) - deliberately NOT run through StripKnownPrefixes first. Stripping
        // prefixes like "piece_" before matching would let a player-built object collide with an
        // unrelated natural one that happens to share the same base name (e.g. "piece_beehive"
        // stripped to "beehive" would collide with the natural "Beehive" prefab) - exactly the class
        // of bug this whitelist rewrite exists to eliminate. StripKnownPrefixes is still used, as
        // before, for display-name formatting and the icon-cache/PNG-override key - just not here.
        private static bool IsExactAlias(string nameLower, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                if (nameLower == alias) return true;
            }
            return false;
        }

        // Prefab name (lowercase) -> friendly dungeon label. High-confidence entries sourced from
        // Jotunn's generated prefab list (component-tagged) cross-checked against the Valheim wiki.
        // Anything with the right Teleport+DungeonGenerator signature but not listed here still gets
        // pinned via the "Dungeons" ResourceRule below, just as a generic "Dungeon Entrance" - nothing
        // is silently dropped, only unlabeled until confirmed.
        private static readonly Dictionary<string, string> DungeonEntranceNames = new Dictionary<string, string>
        {
            ["crypt2"] = "Burial Chambers",
            ["crypt3"] = "Burial Chambers",
            ["crypt4"] = "Burial Chambers",
            ["halfburried_forestcrypt"] = "Burial Chambers",
            ["hildir_crypt"] = "Burial Chambers (Hildir)",
            ["sunkencrypt4"] = "Sunken Crypts",
            ["mountaincave02"] = "Frost Caves",
            ["hildir_cave"] = "Frost Caves (Hildir)",
            ["mistlands_dvergrbossentrance1"] = "Infested Mines",
            ["mistlands_dvergrtownentrance1"] = "Dvergr Camp",
            ["mistlands_dvergrtownentrance2"] = "Dvergr Camp",
        };

        // Resource/structure categories, evaluated in this order. The first rule whose group+track
        // toggle is enabled and whose predicate matches wins - same semantics as the original
        // near-identical if/return blocks, just expressed as data. Id is a stable identifier
        // (independent of the Enabled closure) so a category's enabled state can be re-checked later
        // from just a string, without a live GameObject - used to redraw/hide pins on config toggle
        // and to reconstruct pins loaded from disk after a relog.
        //
        // Every predicate below matches EXACT prefab names (see IsExactAlias), not substrings - this
        // is the whole point of this rewrite. Exact names were sourced from Jotunn's generated prefab
        // list, not guessed, and VanillaIcon values are exact, verified names from Valheim's own icon
        // atlas (Jotunn's generated sprite-list reference) wherever one is confirmed to exist; a rule
        // with VanillaIcon = null falls back to its category PNG/built-in default rather than risk a
        // wrong guessed sprite name.
        private static readonly ResourceRule[] ResourceRules =
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
            // component, so the old Contains("stone")+Pickable-component guard would have matched
            // it too.
            new ResourceRule("Flint", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackFlint.Value, (go, n) => IsExactAlias(n, "pickable_flint"), "ground.png", "flint"),
            new ResourceRule("Stone", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackStone.Value, (go, n) => IsExactAlias(n, "pickable_stone", "pickable_stonerock"), "ground.png", "stone"),
            new ResourceRule("Wood", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackWood.Value, (go, n) => IsExactAlias(n, "pickable_branch", "pickable_branch_snow"), "ground.png", "wood"),

            // ORES - each metal split into Deposit (uncollected world vein/node) / Ore (dropped or
            // picked raw ore item) / Ingot (smelted bar) buckets, so they can never again render as
            // one conflated pin (the reported "copper ore and copper ingot both show as Copper"
            // bug). DisplayNameOverride guarantees the three buckets stay visually distinct on the
            // map regardless of whether Valheim's own hover text/localization cooperates - this is
            // also the fix for pins that used to show a raw, untranslated "[piece_deposit_copper]"
            // string, since the label no longer depends on hover text resolving at all.
            new ResourceRule("CopperDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "minerock_copper", "rock4_copper", "rock4_copper_frac"), "ore.png", null, Const("Copper Deposit")),
            new ResourceRule("CopperOre", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "copperore"), "ore.png", "copperore", Const("Copper Ore")),
            new ResourceRule("CopperIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => IsExactAlias(n, "copper"), "ore.png", "bar_copper_stack", Const("Copper")),

            // Tin is a vanilla quirk: MineRock_Tin (Destructible, no MineRock component) IS the
            // deposit object itself, unlike Copper's separate MineRock_Copper vein + rock4_copper
            // surface node.
            new ResourceRule("TinDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "minerock_tin"), "ore.png", null, Const("Tin Deposit")),
            new ResourceRule("TinOre", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "tinore"), "ore.png", "TinOre", Const("Tin Ore")),
            new ResourceRule("TinIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => IsExactAlias(n, "tin"), "ore.png", "bar_tin_stack", Const("Tin")),

            // Iron has no confirmed surface "deposit" node - the classic source is digging
            // mudpile/mudpile2 in Sunken Crypts, which directly drops IronScrap (no separate vein
            // object). Exact match on "iron" excludes every false-positive that broke this before
            // (fire_pit_iron, piece_cookingstation_iron, ArmorIronChest, SwordIron, iron_grate,
            // ...) by construction, since none of those raw names equal "iron".
            new ResourceRule("IronScrap", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => IsExactAlias(n, "ironscrap", "mudpile", "mudpile2", "pickable_bogironore"), "ore.png", "ironscrap", Const("Iron Scrap")),
            new ResourceRule("IronIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => IsExactAlias(n, "iron"), "ore.png", null, Const("Iron")),

            new ResourceRule("SilverDeposit", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silvervein", "silvervein_frac", "rock3_silver", "rock3_silver_frac"), "ore.png", null, Const("Silver Deposit")),
            new ResourceRule("SilverOre", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silverore"), "ore.png", "silverore", Const("Silver Ore")),
            new ResourceRule("SilverIngot", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => IsExactAlias(n, "silver"), "ore.png", null, Const("Silver")),

            // FUNCTIONAL STRUCTURES
            new ResourceRule("Beehives", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackBeehives.Value, (go, n) => IsExactAlias(n, "beehive"), "beehive.png", "beehive"),

            // Chests: exact whitelist of natural/world-spawn loot containers. Player-buildable
            // chests (piece_chest*) are structurally IDENTICAL (same Container+Piece+WearNTear
            // signature) so there is no component-based way to exclude them - naming is the only
            // signal, which is exactly why this had to become a whitelist instead of "any
            // Container component".
            new ResourceRule("Chests", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackChests.Value, (go, n) => IsExactAlias(n,
                "treasurechest_meadows", "treasurechest_meadows_01", "treasurechest_meadows_02", "treasurechest_meadows_buried", "treasurechest_meadows_combat",
                "treasurechest_blackforest", "treasurechest_forestcrypt", "treasurechest_forestcrypt_hildir",
                "treasurechest_swamp", "treasurechest_sunkencrypt",
                "treasurechest_heath", "treasurechest_heath_hildir", "treasurechest_plains_stone", "treasurechest_plainsfortress_hildir",
                "treasurechest_mountains", "treasurechest_mountaincave", "treasurechest_mountaincave_hildir",
                "treasurechest_dvergrtower", "treasurechest_dvergrtown", "treasurechest_dvergr_loose_stone", "treasurechest_fcrypt",
                "treasurechest_charredfortress", "treasurechest_ashland_stone", "treasurechest_deepnorth_village", "treasurechest_morkhalla", "treasurechest_memorial_buried",
                "treasurechest_trollcave", "loot_chest_stone", "loot_chest_wood", "stonechest",
                "shipwreck_karve_chest", "shipwreck_vikingship_chest", "crypt_skeleton_chest", "morkhalla_chestancient"
            ), "chest.png", "chest_wood"),

            // DUNGEON ENTRANCES - component-signature detection (every real dungeon-plane entrance
            // in the game carries both Teleport and DungeonGenerator) rather than a name/substring
            // guess, per the request to "look for the portal that teleports the player to the
            // dungeon plane". Friendly names are looked up by exact prefab name; anything matching
            // the component signature but not in DungeonEntranceNames still gets pinned, just as a
            // generic "Dungeon Entrance" - nothing is silently dropped.
            new ResourceRule("Dungeons", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackDungeons.Value,
                (go, n) => go.GetComponent<Teleport>() != null && go.GetComponent<DungeonGenerator>() != null,
                "dungeon.png", null, n => DungeonEntranceNames.TryGetValue(n, out string label) ? label : "Dungeon Entrance"),

            // Player-crafted portals are deliberately NOT pinned - there is no natural equivalent
            // in vanilla Valheim. The old "Portals" rule (matching any TeleportWorld component or
            // Contains("portal")) has been removed entirely, along with RadarConfig.TrackPortals.

            // RUNESTONES - every biome runestone (and the boss-summoning ones) carries a RuneStone
            // component, so this category can stay component-based rather than needing an
            // exhaustive name list.
            new ResourceRule("Runestones", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackRunestones.Value, (go, n) => go.GetComponent<RuneStone>() != null, "runestone.png"),

            new ResourceRule("StoneRings", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackStoneRings.Value, (go, n) => IsExactAlias(n, "stonecircle", "stonehenge1", "stonehenge2", "stonehenge3", "stonehenge4", "stonehenge5", "stonehenge6"), "stone_ring.png"),

            new ResourceRule("AbandonedRuins", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackAbandonedRuins.Value, (go, n) => IsExactAlias(n,
                "abandonedlogcabin02", "abandonedlogcabin03", "abandonedlogcabin04", "combatruin01",
                "stonetowerruins03", "stonetowerruins04", "stonetowerruins05", "stonetowerruins05_leet", "stonetowerruins07", "stonetowerruins07_sunk", "stonetowerruins08", "stonetowerruins08_sunk", "stonetowerruins09", "stonetowerruins09_sunk",
                "woodhouse1", "woodhouse2", "woodhouse3", "woodhouse4", "woodhouse5", "woodhouse6", "woodhouse7", "woodhouse8", "woodhouse9", "woodhouse10", "woodhouse11", "woodhouse12", "woodhouse13",
                "woodfarm1", "woodvillage1", "woodvillage2"
            ), "ruin.png"),

            new ResourceRule("TarPits", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackTarPits.Value, (go, n) => IsExactAlias(n, "tarpit1", "tarpit1_1", "tarpit2", "tarpit2_1", "tarpit3", "tarpit3_1"), "tarpit.png"),
        };

        // Destruction-fragment/debris pieces (e.g. a boss arena's stone pillars shattering apart on
        // death - see BatchScanner's scan-volume fix, which is what let a burst of these get recorded
        // as persistent "AbandonedRuins"/"StoneRings" pins in the first place) are never legitimate
        // resources or creatures in their own right. Rejected purely by name, before any other check,
        // regardless of category - a curated whitelist of exact prefab names would be more precise,
        // but Valheim's shatter system generates these dynamically, so there's no fixed list to match.
        private static readonly string[] DebrisNameMarkers = { "_frac", "debris", "rubble", "fragment", "_piece", "_chip" };

        // Dungeon/cave interiors (crypts, sunken crypts, dvergr forts, mountain caves, ...) are
        // generated at a large, fixed vertical offset from the real terrain at their entrance's X/Z -
        // "high in the sky" or "underground" relative to the actual ground - so their objects have no
        // sensible position on the 2D minimap and just show up confusingly stacked on whatever is
        // really at that spot on the surface. Generous enough that real terrain variance (cliffs,
        // mountain peaks) directly above/below a point is never mistaken for a dungeon interior -
        // genuine dungeon-generation offsets are far larger than that.
        private const float DungeonHeightOffsetThreshold = 40f;

        public static bool ShouldPinGameObject(GameObject go, string nameLower, out string displayName, out Sprite icon, out bool isPersistent, out string categoryKey)
        {
            displayName = string.Empty;
            icon = null;
            isPersistent = false;
            categoryKey = null;

            if (ContainsAny(nameLower, DebrisNameMarkers)) return false;
            if (IsInsideDungeonInterior(go.transform.position)) return false;

            Character character = go.GetComponent<Character>();

            HoverText hover = go.GetComponent<HoverText>();
            if (hover != null && !string.IsNullOrEmpty(hover.m_text))
            {
                displayName = hover.m_text;
            }
            else if (character != null)
            {
                displayName = character.GetHoverName();
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = go.name;
            }

            displayName = FormatHumanFriendlyName(displayName);

            // CREATURES (Character-based - includes regular creatures and bosses, both matched via
            // the same exact-alias RadarConfig.AliasLookup)
            if (RadarConfig.Group_Creatures.Value && character != null && !character.IsDead() && !character.IsPlayer())
            {
                int starLevel = Math.Max(0, character.GetLevel() - 1);

                RadarConfig.CreatureConfigEntry specific = FindCreatureOverride(nameLower, out string specificKey);
                bool enabled;
                int minStars;
                bool isMonsterIcon;
                string candidateCategoryKey;

                if (specific != null)
                {
                    enabled = specific.Enabled.Value;
                    minStars = specific.MinStars.Value;
                    isMonsterIcon = specific.IsMonster;
                    candidateCategoryKey = $"creature:{specificKey}";
                }
                else if (IsHostileMonster(character))
                {
                    enabled = RadarConfig.EnableMonsters.Value;
                    minStars = RadarConfig.MinMonsterStars.Value;
                    isMonsterIcon = true;
                    candidateCategoryKey = "creature:_monster";
                }
                else if (IsPassiveAnimal(character))
                {
                    enabled = RadarConfig.EnableAnimals.Value;
                    minStars = RadarConfig.MinAnimalStars.Value;
                    isMonsterIcon = false;
                    candidateCategoryKey = "creature:_animal";
                }
                else
                {
                    enabled = false;
                    minStars = 0;
                    isMonsterIcon = false;
                    candidateCategoryKey = null;
                }

                if (enabled && starLevel >= minStars)
                {
                    if (starLevel > 0) displayName += $" ({new string('★', starLevel)})";

                    // Symmetric with resources (rule.VanillaIcon): pass the matched species'
                    // verified trophy sprite name explicitly instead of letting PinManager
                    // re-derive a lookup key from the raw prefab name - that mismatch (e.g.
                    // "bjorn" vs a dict keyed "bear") is exactly what made Bear's icon (and its
                    // classification) silently fail before.
                    string vanillaIcon = specificKey != null ? VanillaIconResolver.GetCreatureTrophySprite(specificKey) : null;
                    icon = PinManager.ResolvePerObjectPin(nameLower, isMonsterIcon ? "monster.png" : "animal.png", vanillaIcon);
                    categoryKey = candidateCategoryKey;
                    return true;
                }
            }

            // FISH - Fish prefabs have no Character/Humanoid component at all (just Fish+ItemDrop),
            // so they never reach the branch above. Matched via the same exact-alias AliasLookup as
            // regular creatures, gated by the same Group_Creatures toggle. No star-level concept for
            // fish (they don't have Character.GetLevel()).
            if (character == null && RadarConfig.Group_Creatures.Value && go.GetComponent<Fish>() != null)
            {
                RadarConfig.CreatureConfigEntry fishEntry = FindCreatureOverride(nameLower, out string fishKey);
                if (fishEntry != null && fishEntry.Enabled.Value)
                {
                    icon = PinManager.ResolvePerObjectPin(nameLower, "animal.png", VanillaIconResolver.GetCreatureTrophySprite(fishKey));
                    categoryKey = $"creature:{fishKey}";
                    return true;
                }

                return false;
            }

            // RESOURCES & STRUCTURES (berries through ruins/locations) - these are stationary, so
            // their pins persist on the minimap even after the player leaves scan range.
            //
            // Matched (and recorded into PinManager's raw point store) regardless of whether the
            // category's own toggle is currently on - only IsCategoryEnabled (checked later, at pin
            // render time) decides whether a match actually gets a visible pin. This way a category
            // that's been off since before an area was ever scanned still gets its raw points
            // recorded while the player walks through, so flipping the toggle on later immediately
            // populates the map from ground already covered instead of requiring a re-scan.
            foreach (var rule in ResourceRules)
            {
                if (rule.Matches(go, nameLower))
                {
                    if (rule.DisplayNameOverride != null) displayName = rule.DisplayNameOverride(nameLower);
                    icon = PinManager.ResolvePerObjectPin(nameLower, rule.IconPng, rule.VanillaIcon);
                    isPersistent = true;
                    categoryKey = $"resource:{rule.Id}";
                    return true;
                }
            }

            return false;
        }

        // Re-derives whether a category (identified by the categoryKey produced above) is currently
        // enabled purely from live config, without needing the original GameObject - used to redraw or
        // hide already-placed pins when a toggle changes, and to reconstruct pins loaded from disk.
        public static bool IsCategoryEnabled(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return false;

            if (categoryKey.StartsWith("creature:"))
            {
                if (!RadarConfig.Group_Creatures.Value) return false;

                string sub = categoryKey.Substring("creature:".Length);
                if (sub == "_monster") return RadarConfig.EnableMonsters.Value;
                if (sub == "_animal") return RadarConfig.EnableAnimals.Value;
                return RadarConfig.Creatures.TryGetValue(sub, out var entry) && entry.Enabled.Value;
            }

            if (categoryKey.StartsWith("resource:"))
            {
                string id = categoryKey.Substring("resource:".Length);
                foreach (var rule in ResourceRules)
                {
                    if (rule.Id == id) return rule.Enabled();
                }
            }

            return false;
        }

        // Only resource/structure categories are persisted to disk (see PinManager), so this only
        // needs to resolve icons for "resource:" keys - used to re-resolve the icon Sprite after
        // loading cached pin positions from a previous session.
        public static string GetDefaultIconForCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("resource:")) return null;

            string id = categoryKey.Substring("resource:".Length);
            foreach (var rule in ResourceRules)
            {
                if (rule.Id == id) return rule.IconPng;
            }

            return null;
        }

        // Companion to GetDefaultIconForCategory - resolves the same rule's verified vanilla icon
        // sprite name (see ResourceRule.VanillaIcon), so pins reloaded from disk after a relog get
        // the same per-type icon as freshly-scanned ones instead of only the category PNG/fallback.
        public static string GetVanillaIconForCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("resource:")) return null;

            string id = categoryKey.Substring("resource:".Length);
            foreach (var rule in ResourceRules)
            {
                if (rule.Id == id) return rule.VanillaIcon;
            }

            return null;
        }

        // Exact-alias lookup against the cleaned prefab name - see RadarConfig.AliasLookup. Flat
        // dictionary means no ordering requirement, unlike the old Contains()-based scan (a variant
        // like "greydwarf_elite" no longer needs to be declared before "greydwarf").
        internal static RadarConfig.CreatureConfigEntry FindCreatureOverride(string nameLower, out string matchedKey)
        {
            if (!string.IsNullOrEmpty(nameLower) &&
                RadarConfig.AliasLookup.TryGetValue(nameLower, out var def) &&
                RadarConfig.Creatures.TryGetValue(def.CanonicalKey, out var entry))
            {
                matchedKey = def.CanonicalKey;
                return entry;
            }

            matchedKey = null;
            return null;
        }

        internal static bool ContainsAny(string value, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (value.Contains(marker)) return true;
            }

            return false;
        }

        private static bool IsInsideDungeonInterior(Vector3 position)
        {
            if (ZoneSystem.instance == null) return false;
            if (!ZoneSystem.instance.GetGroundHeight(position, out float groundHeight)) return false;

            return Mathf.Abs(position.y - groundHeight) > DungeonHeightOffsetThreshold;
        }

        private static bool IsPassiveAnimal(Character character)
        {
            if (character == null) return false;
            if (character.IsTamed()) return true;
            if (character.m_faction == Character.Faction.AnimalsVeg) return true;
            if (character.m_faction == Character.Faction.PlayerSpawned) return true;

            BaseAI ai = character.GetBaseAI();
            if (ai != null && ai.m_passiveAggresive) return true;

            return false;
        }

        private static bool IsHostileMonster(Character character)
        {
            if (character == null || character.IsTamed()) return false;
            if (IsPassiveAnimal(character)) return false;

            if (Player.m_localPlayer != null)
            {
                return BaseAI.IsEnemy(Player.m_localPlayer, character);
            }

            return character.IsMonsterFaction(Time.time);
        }

        /// <summary>
        /// Strips known engine/asset prefixes and the "(Clone)" instantiation suffix from a raw
        /// Unity object name. Used for display-name formatting and the icon-cache/PNG-override key
        /// (see <see cref="PinManager.ResolvePerObjectPin"/>) - NOT for whitelist match decisions
        /// (see <see cref="IsExactAlias"/>), since stripping "piece_" here is exactly what could make
        /// a player-built object collide with an unrelated natural one of the same base name.
        /// </summary>
        public static string StripKnownPrefixes(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            string clean = Regex.Replace(rawName, @"(?i)^(pickable_|item_|piece_|vfx_|sfx_)", "");
            return clean.Replace("(Clone)", "").Trim();
        }

        internal static string FormatHumanFriendlyName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            string clean = StripKnownPrefixes(rawName);
            clean = clean.Replace('_', ' ');
            clean = Regex.Replace(clean, @"(?<=[a-z])(?=[A-Z])", " ");
            clean = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clean.ToLower());

            return clean;
        }
    }
}
