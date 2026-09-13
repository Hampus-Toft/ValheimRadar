using System;
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

            public ResourceRule(string id, Func<bool> enabled, Func<GameObject, string, bool> matches, string iconPng, string vanillaIcon = null)
            {
                Id = id;
                Enabled = enabled;
                Matches = matches;
                IconPng = iconPng;
                VanillaIcon = vanillaIcon;
            }
        }

        // Resource/structure categories (formerly 7 near-identical if/return blocks), evaluated in
        // this order. The first rule whose group+track toggle is enabled and whose predicate matches
        // wins - same semantics as the original blocks, just expressed as data instead of repeated code.
        // Id is a stable identifier (independent of the Enabled closure) so a category's enabled state
        // can be re-checked later from just a string, without a live GameObject - used to redraw/hide
        // pins on config toggle and to reconstruct pins loaded from disk after a relog.
        // VanillaIcon values are exact, verified names from Valheim's own icon atlas (see Jotunn's
        // generated sprite-list reference), not a guessed convention - this is what a wrong/missing
        // entry here used to silently produce an incorrect icon.
        private static readonly ResourceRule[] ResourceRules =
        {
            // BERRIES
            new ResourceRule("Raspberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackRaspberry.Value, (go, n) => n.Contains("raspberry"), "berry.png", "raspberry"),
            new ResourceRule("Blueberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackBlueberry.Value, (go, n) => n.Contains("blueberry"), "berry.png", "blueberries"),
            new ResourceRule("Cloudberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackCloudberry.Value, (go, n) => n.Contains("cloudberry"), "berry.png", "cloudberry"),

            // MUSHROOMS
            new ResourceRule("RedMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackRedMushroom.Value, (go, n) => n.Equals("pickable_mushroom"), "mushroom.png", "mushroom"),
            new ResourceRule("YellowMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackYellowMushroom.Value, (go, n) => n.Contains("yellow"), "mushroom.png", "mushroomyellow"),
            new ResourceRule("BlueMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackBlueMushroom.Value, (go, n) => n.Contains("blue"), "mushroom.png", "mushroomblue"),

            // FLOWERS & CROPS
            new ResourceRule("Dandelion", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackDandelion.Value, (go, n) => n.Contains("dandelion"), "crop.png", "dandelion"),
            new ResourceRule("Thistle", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackThistle.Value, (go, n) => n.Contains("thistle"), "crop.png", "thistle"),
            new ResourceRule("CarrotSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackCarrotSeed.Value, (go, n) => n.Contains("carrot"), "crop.png", "carrotseeds"),
            new ResourceRule("TurnipSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackTurnipSeed.Value, (go, n) => n.Contains("turnip"), "crop.png", "turnipseeds"),
            new ResourceRule("OnionSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackOnionSeed.Value, (go, n) => n.Contains("onion"), "crop.png", "onionseeds"),
            new ResourceRule("Barley", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackBarley.Value, (go, n) => n.Contains("barley"), "crop.png", "barley"),
            new ResourceRule("Flax", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackFlax.Value, (go, n) => n.Contains("flax"), "crop.png", "flax"),
            new ResourceRule("Magecap", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackMagecap.Value, (go, n) => n.Contains("magecap"), "crop.png", "mushroommagecap"),

            // GROUND PICKABLES
            new ResourceRule("Flint", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackFlint.Value, (go, n) => n.Contains("flint"), "ground.png", "flint"),
            new ResourceRule("Stone", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackStone.Value, (go, n) => n.Contains("stone") && go.GetComponent<Pickable>() != null, "ground.png", "stone"),
            new ResourceRule("Wood", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackWood.Value, (go, n) => (n.Contains("wood") || n.Contains("branch")) && go.GetComponent<Pickable>() != null, "ground.png", "wood"),

            // ORES
            new ResourceRule("Copper", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => n.Contains("copper"), "ore.png", "copperore"),
            new ResourceRule("Tin", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => n.Contains("tin"), "ore.png", "TinOre"),
            new ResourceRule("Iron", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => n.Contains("muddy") || n.Contains("iron"), "ore.png", "ironscrap"),
            new ResourceRule("Silver", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => n.Contains("silver"), "ore.png", "silverore"),

            // FUNCTIONAL STRUCTURES
            // Beehives must be checked before Chests - a wild Beehive has its own Container
            // component (for the honey), so the generic "any Container" Chests rule below would
            // otherwise swallow it first, giving it the wrong icon and putting it under the wrong
            // enable/disable toggle.
            new ResourceRule("Beehives", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackBeehives.Value, (go, n) => n.Contains("beehive"), "beehive.png", "beehive"),
            new ResourceRule("Chests", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackChests.Value, (go, n) => go.GetComponent<Container>() != null, "chest.png", "chest_wood"),
            new ResourceRule("Portals", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackPortals.Value, (go, n) => go.GetComponent<TeleportWorld>() != null || n.Contains("portal"), "portal.png", "portal_wood"),
            new ResourceRule("Dungeons", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackDungeons.Value, (go, n) => n.Contains("dungeon") || n.Contains("crypt") || n.Contains("cave") || n.Contains("burial"), "dungeon.png"),

            // DECORATIVE RUINS & LOCATIONS (no vanilla icon to borrow - rely on category PNG/fallback)
            new ResourceRule("StoneRings", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackStoneRings.Value, (go, n) => n.Contains("stonering") || n.Contains("rockformation") || n.Contains("stone_ring"), "stone_ring.png"),
            new ResourceRule("AbandonedRuins", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackAbandonedRuins.Value, (go, n) => n.Contains("ruin") || n.Contains("abandoned") || n.Contains("woodhouse") || n.Contains("village"), "ruin.png"),
            new ResourceRule("Runestones", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackRunestones.Value, (go, n) => n.Contains("runestone"), "runestone.png"),
            new ResourceRule("TarPits", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackTarPits.Value, (go, n) => n.Contains("tarpit") || n.Contains("tar_pit"), "tarpit.png"),
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

            // CREATURES
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

                    icon = PinManager.ResolvePerObjectPin(nameLower, isMonsterIcon ? "monster.png" : "animal.png");
                    categoryKey = candidateCategoryKey;
                    return true;
                }
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

        // Ordered most-specific-key-first (see RadarConfig.CreatureDefinitions), so a variant like
        // "greydwarf_elite" is matched before the generic "greydwarf" entry.
        internal static RadarConfig.CreatureConfigEntry FindCreatureOverride(string nameLower, out string matchedKey)
        {
            foreach (var def in RadarConfig.CreatureDefinitions)
            {
                if (nameLower.Contains(def.Key) && RadarConfig.Creatures.TryGetValue(def.Key, out var entry))
                {
                    matchedKey = def.Key;
                    return entry;
                }
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
        /// Unity object name. Shared with <see cref="PinManager.ResolvePerObjectPin"/> so both the
        /// display-name formatter here and the icon-key lookup normalize names identically.
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
