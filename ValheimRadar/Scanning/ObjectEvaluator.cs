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
            public readonly string VanillaItemPrefab;

            public ResourceRule(string id, Func<bool> enabled, Func<GameObject, string, bool> matches, string iconPng)
            {
                Id = id;
                Enabled = enabled;
                Matches = matches;
                IconPng = iconPng;
                VanillaItemPrefab = vanillaItemPrefab;
            }
        }

        // Resource/structure categories (formerly 7 near-identical if/return blocks), evaluated in
        // this order. The first rule whose group+track toggle is enabled and whose predicate matches
        // wins - same semantics as the original blocks, just expressed as data instead of repeated code.
        // Id is a stable identifier (independent of the Enabled closure) so a category's enabled state
        // can be re-checked later from just a string, without a live GameObject - used to redraw/hide
        // pins on config toggle and to reconstruct pins loaded from disk after a relog.
        private static readonly ResourceRule[] ResourceRules =
        {
            // BERRIES
            new ResourceRule("Raspberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackRaspberry.Value, (go, n) => n.Contains("raspberry"), "berry.png"),
            new ResourceRule("Blueberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackBlueberry.Value, (go, n) => n.Contains("blueberry"), "berry.png"),
            new ResourceRule("Cloudberry", () => RadarConfig.Group_Berries.Value && RadarConfig.TrackCloudberry.Value, (go, n) => n.Contains("cloudberry"), "berry.png"),

            // MUSHROOMS
            new ResourceRule("RedMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackRedMushroom.Value, (go, n) => n.Equals("pickable_mushroom"), "mushroom.png"),
            new ResourceRule("YellowMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackYellowMushroom.Value, (go, n) => n.Contains("yellow"), "mushroom.png"),
            new ResourceRule("BlueMushroom", () => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackBlueMushroom.Value, (go, n) => n.Contains("blue"), "mushroom.png"),

            // FLOWERS & CROPS
            new ResourceRule("Dandelion", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackDandelion.Value, (go, n) => n.Contains("dandelion"), "crop.png"),
            new ResourceRule("Thistle", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackThistle.Value, (go, n) => n.Contains("thistle"), "crop.png"),
            new ResourceRule("CarrotSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackCarrotSeed.Value, (go, n) => n.Contains("carrot"), "crop.png"),
            new ResourceRule("TurnipSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackTurnipSeed.Value, (go, n) => n.Contains("turnip"), "crop.png"),
            new ResourceRule("OnionSeed", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackOnionSeed.Value, (go, n) => n.Contains("onion"), "crop.png"),
            new ResourceRule("Barley", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackBarley.Value, (go, n) => n.Contains("barley"), "crop.png"),
            new ResourceRule("Flax", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackFlax.Value, (go, n) => n.Contains("flax"), "crop.png"),
            new ResourceRule("Magecap", () => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackMagecap.Value, (go, n) => n.Contains("magecap"), "crop.png"),

            // GROUND PICKABLES
            new ResourceRule("Flint", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackFlint.Value, (go, n) => n.Contains("flint"), "ground.png"),
            new ResourceRule("Stone", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackStone.Value, (go, n) => n.Contains("stone") && go.GetComponent<Pickable>() != null, "ground.png"),
            new ResourceRule("Wood", () => RadarConfig.Group_RocksAndFlint.Value && RadarConfig.TrackWood.Value, (go, n) => (n.Contains("wood") || n.Contains("branch")) && go.GetComponent<Pickable>() != null, "ground.png"),

            // ORES
            new ResourceRule("Copper", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => n.Contains("copper"), "ore.png"),
            new ResourceRule("Tin", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => n.Contains("tin"), "ore.png"),
            new ResourceRule("Iron", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => n.Contains("muddy") || n.Contains("iron"), "ore.png"),
            new ResourceRule("Silver", () => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => n.Contains("silver"), "ore.png"),

            // FUNCTIONAL STRUCTURES
            new ResourceRule("Chests", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackChests.Value, (go, n) => go.GetComponent<Container>() != null, "chest.png"),
            new ResourceRule("Portals", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackPortals.Value, (go, n) => go.GetComponent<TeleportWorld>() != null || n.Contains("portal"), "portal.png"),
            new ResourceRule("Dungeons", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackDungeons.Value, (go, n) => n.Contains("dungeon") || n.Contains("crypt") || n.Contains("cave") || n.Contains("burial"), "dungeon.png"),
            new ResourceRule("Beehives", () => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackBeehives.Value, (go, n) => n.Contains("beehive"), "beehive.png"),

            // DECORATIVE RUINS & LOCATIONS
            new ResourceRule("StoneRings", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackStoneRings.Value, (go, n) => n.Contains("stonering") || n.Contains("rockformation") || n.Contains("stone_ring"), "stone_ring.png"),
            new ResourceRule("AbandonedRuins", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackAbandonedRuins.Value, (go, n) => n.Contains("ruin") || n.Contains("abandoned") || n.Contains("woodhouse") || n.Contains("village"), "ruin.png"),
            new ResourceRule("Runestones", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackRunestones.Value, (go, n) => n.Contains("runestone"), "runestone.png"),
            new ResourceRule("TarPits", () => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackTarPits.Value, (go, n) => n.Contains("tarpit") || n.Contains("tar_pit"), "tarpit.png"),
        };

        public static bool ShouldPinGameObject(GameObject go, string nameLower, out string displayName, out Minimap.PinType pinType, out bool isPersistent, out string categoryKey)
        {
            displayName = string.Empty;
            pinType = Minimap.PinType.Icon3;
            isPersistent = false;
            categoryKey = null;

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

                    pinType = PinManager.ResolvePerObjectPin(nameLower, isMonsterIcon ? "monster.png" : "animal.png");
                    categoryKey = candidateCategoryKey;
                    return true;
                }
            }

            // RESOURCES & STRUCTURES (berries through ruins/locations) - these are stationary, so
            // their pins persist on the minimap even after the player leaves scan range.
            foreach (var rule in ResourceRules)
            {
                if (rule.Enabled() && rule.Matches(go, nameLower))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, rule.IconPng, rule.VanillaItemPrefab);
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
        // needs to resolve icons for "resource:" keys - used to regenerate a valid PinType for the
        // current Minimap instance after loading cached pin positions from a previous session.
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

        // Ordered most-specific-key-first (see RadarConfig.CreatureDefinitions), so a variant like
        // "greydwarf_elite" is matched before the generic "greydwarf" entry.
        private static RadarConfig.CreatureConfigEntry FindCreatureOverride(string nameLower, out string matchedKey)
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

        private static string FormatHumanFriendlyName(string rawName)
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
