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
            public readonly Func<bool> Enabled;
            public readonly Func<GameObject, string, bool> Matches;
            public readonly string IconPng;

            public ResourceRule(Func<bool> enabled, Func<GameObject, string, bool> matches, string iconPng)
            {
                Enabled = enabled;
                Matches = matches;
                IconPng = iconPng;
            }
        }

        // Resource/structure categories (formerly 7 near-identical if/return blocks), evaluated in
        // this order. The first rule whose group+track toggle is enabled and whose predicate matches
        // wins - same semantics as the original blocks, just expressed as data instead of repeated code.
        private static readonly ResourceRule[] ResourceRules =
        {
            // BERRIES
            new ResourceRule(() => RadarConfig.Group_Berries.Value && RadarConfig.TrackRaspberry.Value, (go, n) => n.Contains("raspberry"), "berry.png"),
            new ResourceRule(() => RadarConfig.Group_Berries.Value && RadarConfig.TrackBlueberry.Value, (go, n) => n.Contains("blueberry"), "berry.png"),
            new ResourceRule(() => RadarConfig.Group_Berries.Value && RadarConfig.TrackCloudberry.Value, (go, n) => n.Contains("cloudberry"), "berry.png"),

            // MUSHROOMS
            new ResourceRule(() => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackRedMushroom.Value, (go, n) => n.Equals("pickable_mushroom"), "mushroom.png"),
            new ResourceRule(() => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackYellowMushroom.Value, (go, n) => n.Contains("yellow"), "mushroom.png"),
            new ResourceRule(() => RadarConfig.Group_Mushrooms.Value && RadarConfig.TrackBlueMushroom.Value, (go, n) => n.Contains("blue"), "mushroom.png"),

            // FLOWERS & CROPS
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackDandelion.Value, (go, n) => n.Contains("dandelion"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackThistle.Value, (go, n) => n.Contains("thistle"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackCarrotSeed.Value, (go, n) => n.Contains("carrot"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackTurnipSeed.Value, (go, n) => n.Contains("turnip"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackOnionSeed.Value, (go, n) => n.Contains("onion"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackBarley.Value, (go, n) => n.Contains("barley"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackFlax.Value, (go, n) => n.Contains("flax"), "crop.png"),
            new ResourceRule(() => RadarConfig.Group_FlowersAndCrops.Value && RadarConfig.TrackMagecap.Value, (go, n) => n.Contains("magecap"), "crop.png"),

            // GROUND PICKABLES
            new ResourceRule(() => RadarConfig.Group_GroundPickables.Value && RadarConfig.TrackFlint.Value, (go, n) => n.Contains("flint"), "ground.png"),
            new ResourceRule(() => RadarConfig.Group_GroundPickables.Value && RadarConfig.TrackStone.Value, (go, n) => n.Contains("stone") && go.GetComponent<Pickable>() != null, "ground.png"),
            new ResourceRule(() => RadarConfig.Group_GroundPickables.Value && RadarConfig.TrackWood.Value, (go, n) => (n.Contains("wood") || n.Contains("branch")) && go.GetComponent<Pickable>() != null, "ground.png"),

            // ORES
            new ResourceRule(() => RadarConfig.Group_Ores.Value && RadarConfig.TrackCopper.Value, (go, n) => n.Contains("copper"), "ore.png"),
            new ResourceRule(() => RadarConfig.Group_Ores.Value && RadarConfig.TrackTin.Value, (go, n) => n.Contains("tin"), "ore.png"),
            new ResourceRule(() => RadarConfig.Group_Ores.Value && RadarConfig.TrackIron.Value, (go, n) => n.Contains("muddy") || n.Contains("iron"), "ore.png"),
            new ResourceRule(() => RadarConfig.Group_Ores.Value && RadarConfig.TrackSilver.Value, (go, n) => n.Contains("silver"), "ore.png"),

            // FUNCTIONAL STRUCTURES
            new ResourceRule(() => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackChests.Value, (go, n) => go.GetComponent<Container>() != null, "chest.png"),
            new ResourceRule(() => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackPortals.Value, (go, n) => go.GetComponent<TeleportWorld>() != null || n.Contains("portal"), "portal.png"),
            new ResourceRule(() => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackDungeons.Value, (go, n) => n.Contains("dungeon") || n.Contains("crypt") || n.Contains("cave") || n.Contains("burial"), "dungeon.png"),
            new ResourceRule(() => RadarConfig.Group_FunctionalStructures.Value && RadarConfig.TrackBeehives.Value, (go, n) => n.Contains("beehive"), "beehive.png"),

            // DECORATIVE RUINS & LOCATIONS
            new ResourceRule(() => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackStoneRings.Value, (go, n) => n.Contains("stonering") || n.Contains("rockformation") || n.Contains("stone_ring"), "stone_ring.png"),
            new ResourceRule(() => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackAbandonedRuins.Value, (go, n) => n.Contains("ruin") || n.Contains("abandoned") || n.Contains("woodhouse") || n.Contains("village"), "ruin.png"),
            new ResourceRule(() => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackRunestones.Value, (go, n) => n.Contains("runestone"), "runestone.png"),
            new ResourceRule(() => RadarConfig.Group_RuinsAndLocations.Value && RadarConfig.TrackTarPits.Value, (go, n) => n.Contains("tarpit") || n.Contains("tar_pit"), "tarpit.png"),
        };

        public static bool ShouldPinGameObject(GameObject go, string nameLower, out string displayName, out Minimap.PinType pinType)
        {
            displayName = string.Empty;
            pinType = Minimap.PinType.Icon3;

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
                bool isMonster = IsHostileMonster(character);
                bool isAnimal = IsPassiveAnimal(character);

                if (isMonster && RadarConfig.EnableMonsters.Value)
                {
                    if (starLevel < RadarConfig.MinMonsterStars.Value) return false;
                    if (starLevel > 0) displayName += $" ({new string('★', starLevel)})";

                    pinType = PinManager.ResolvePerObjectPin(nameLower, "monster.png");
                    return true;
                }

                if (isAnimal && RadarConfig.EnableAnimals.Value)
                {
                    if (starLevel < RadarConfig.MinAnimalStars.Value) return false;
                    if (starLevel > 0) displayName += $" ({new string('★', starLevel)})";

                    pinType = PinManager.ResolvePerObjectPin(nameLower, "animal.png");
                    return true;
                }
            }

            // RESOURCES & STRUCTURES (berries through ruins/locations)
            foreach (var rule in ResourceRules)
            {
                if (rule.Enabled() && rule.Matches(go, nameLower))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, rule.IconPng);
                    return true;
                }
            }

            return false;
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
