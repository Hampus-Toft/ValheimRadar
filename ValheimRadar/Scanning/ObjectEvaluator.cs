using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimRadar
{
    public static class ObjectEvaluator
    {
        public static bool ShouldPinGameObject(GameObject go, string nameLower, out string displayName, out Minimap.PinType pinType)
        {
            displayName = string.Empty;
            pinType = Minimap.PinType.Icon3;

            HoverText hover = go.GetComponent<HoverText>();
            if (hover != null && !string.IsNullOrEmpty(hover.m_text))
            {
                displayName = hover.m_text;
            }
            else
            {
                Character character = go.GetComponent<Character>();
                if (character != null) displayName = character.GetHoverName();
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = go.name;
            }

            displayName = FormatHumanFriendlyName(displayName);

            // 1. CREATURES
            if (RadarConfig.Group_Creatures.Value)
            {
                Character character = go.GetComponent<Character>();
                if (character != null && !character.IsDead() && !character.IsPlayer())
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
            }

            // 2. BERRIES
            if (RadarConfig.Group_Berries.Value)
            {
                if ((RadarConfig.TrackRaspberry.Value && nameLower.Contains("raspberry")) ||
                    (RadarConfig.TrackBlueberry.Value && nameLower.Contains("blueberry")) ||
                    (RadarConfig.TrackCloudberry.Value && nameLower.Contains("cloudberry")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "berry.png");
                    return true;
                }
            }

            // 3. MUSHROOMS
            if (RadarConfig.Group_Mushrooms.Value)
            {
                if ((RadarConfig.TrackRedMushroom.Value && nameLower.Equals("pickable_mushroom")) ||
                    (RadarConfig.TrackYellowMushroom.Value && nameLower.Contains("yellow")) ||
                    (RadarConfig.TrackBlueMushroom.Value && nameLower.Contains("blue")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "mushroom.png");
                    return true;
                }
            }

            // 4. FLOWERS & CROPS
            if (RadarConfig.Group_FlowersAndCrops.Value)
            {
                if ((RadarConfig.TrackDandelion.Value && nameLower.Contains("dandelion")) ||
                    (RadarConfig.TrackThistle.Value && nameLower.Contains("thistle")) ||
                    (RadarConfig.TrackCarrotSeed.Value && nameLower.Contains("carrot")) ||
                    (RadarConfig.TrackTurnipSeed.Value && nameLower.Contains("turnip")) ||
                    (RadarConfig.TrackOnionSeed.Value && nameLower.Contains("onion")) ||
                    (RadarConfig.TrackBarley.Value && nameLower.Contains("barley")) ||
                    (RadarConfig.TrackFlax.Value && nameLower.Contains("flax")) ||
                    (RadarConfig.TrackMagecap.Value && nameLower.Contains("magecap")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "crop.png");
                    return true;
                }
            }

            // 5. GROUND PICKABLES
            if (RadarConfig.Group_RocksAndFlint.Value)
            {
                if ((RadarConfig.TrackFlint.Value && nameLower.Contains("flint")) ||
                    (RadarConfig.TrackStone.Value && nameLower.Contains("stone") && go.GetComponent<Pickable>() != null) ||
                    (RadarConfig.TrackWood.Value && (nameLower.Contains("wood") || nameLower.Contains("branch")) && go.GetComponent<Pickable>() != null))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "ground.png");
                    return true;
                }
            }

            // 6. ORES
            if (RadarConfig.Group_Ores.Value)
            {
                if ((RadarConfig.TrackCopper.Value && nameLower.Contains("copper")) ||
                    (RadarConfig.TrackTin.Value && nameLower.Contains("tin")) ||
                    (RadarConfig.TrackIron.Value && (nameLower.Contains("muddy") || nameLower.Contains("iron"))) ||
                    (RadarConfig.TrackSilver.Value && nameLower.Contains("silver")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "ore.png");
                    return true;
                }
            }

            // 7. FUNCTIONAL STRUCTURES
            if (RadarConfig.Group_FunctionalStructures.Value)
            {
                if (RadarConfig.TrackChests.Value && go.GetComponent<Container>() != null)
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "chest.png");
                    return true;
                }

                if (RadarConfig.TrackPortals.Value && (go.GetComponent<TeleportWorld>() != null || nameLower.Contains("portal")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "portal.png");
                    return true;
                }

                if (RadarConfig.TrackDungeons.Value && (nameLower.Contains("dungeon") || nameLower.Contains("crypt") || nameLower.Contains("cave") || nameLower.Contains("burial")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "dungeon.png");
                    return true;
                }

                if (RadarConfig.TrackBeehives.Value && nameLower.Contains("beehive"))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "beehive.png");
                    return true;
                }
            }

            // 8. DECORATIVE RUINS & LOCATIONS
            if (RadarConfig.Group_RuinsAndLocations.Value)
            {
                if (RadarConfig.TrackStoneRings.Value && (nameLower.Contains("stonering") || nameLower.Contains("rockformation") || nameLower.Contains("stone_ring")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "stone_ring.png");
                    return true;
                }

                if (RadarConfig.TrackAbandonedRuins.Value && (nameLower.Contains("ruin") || nameLower.Contains("abandoned") || nameLower.Contains("woodhouse") || nameLower.Contains("village")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "ruin.png");
                    return true;
                }

                if (RadarConfig.TrackRunestones.Value && nameLower.Contains("runestone"))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "runestone.png");
                    return true;
                }

                if (RadarConfig.TrackTarPits.Value && (nameLower.Contains("tarpit") || nameLower.Contains("tar_pit")))
                {
                    pinType = PinManager.ResolvePerObjectPin(nameLower, "tarpit.png");
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
            if (ai != null && ai.IsEnemy(Player.m_localPlayer)) return true;

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

        private static string FormatHumanFriendlyName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            string clean = Regex.Replace(rawName, @"(?i)^(pickable_|item_|piece_|vfx_|sfx_)", "");
            clean = clean.Replace("(Clone)", "").Trim();
            clean = clean.Replace('_', ' ');
            clean = Regex.Replace(clean, @"(?<=[a-z])(?=[A-Z])", " ");
            clean = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clean.ToLower());

            return clean;
        }
    }
}