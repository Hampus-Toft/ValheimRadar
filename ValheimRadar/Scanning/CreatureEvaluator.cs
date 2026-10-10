using System;
using UnityEngine;

namespace ValheimRadar
{
    // Type #1 - ephemeral entities: creatures (Character-based, includes regular creatures and
    // bosses), fish, and the Leviathan "kraken" structure. These move and die, so unlike
    // resources/POI they are never recorded into a permanent store - see CreatureScanner, which
    // re-evaluates the active scan radius fresh on every discovery tick instead of caching
    // positions long-term.
    internal static class CreatureEvaluator
    {
        internal static bool TryClassify(GameObject go, string nameLower, out string displayName, out Sprite icon, out string categoryKey)
        {
            displayName = string.Empty;
            icon = null;
            categoryKey = null;

            if (!RadarConfig.Group_Creatures.Value) return false;

            Character character = go.GetComponent<Character>();

            if (character != null)
            {
                return TryClassifyCharacter(go, character, nameLower, out displayName, out icon, out categoryKey);
            }

            if (go.GetComponent<Fish>() != null)
            {
                return TryClassifyFish(go, nameLower, out displayName, out icon, out categoryKey);
            }

            // LEVIATHAN - the giant ocean turtle-shell structure players commonly call a "kraken"
            // (Valheim has no creature literally named that). Not a Character (no AI, not directly
            // attackable) and not a Fish - it carries its own "Leviathan" + MineRock components, so
            // it needs its own branch here too.
            if (go.GetComponent<Leviathan>() != null)
            {
                return TryClassifyAliasOnly(nameLower, "animal.png", out displayName, out icon, out categoryKey);
            }

            return false;
        }

        // CREATURES (Character-based - includes regular creatures and bosses, both matched via the
        // same exact-alias RadarConfig.AliasLookup).
        private static bool TryClassifyCharacter(GameObject go, Character character, string nameLower, out string displayName, out Sprite icon, out string categoryKey)
        {
            displayName = string.Empty;
            icon = null;
            categoryKey = null;

            if (character.IsDead() || character.IsPlayer()) return false;

            displayName = NameFormatting.DeriveDisplayName(go, character);

            int starLevel = Math.Max(0, character.GetLevel() - 1);

            RadarConfig.CreatureConfigEntry specific = FindCreatureOverride(nameLower, out string specificKey);
            bool enabled;
            int minStars;
            bool isMonsterIcon;
            string candidateCategoryKey;

            if (specific != null)
            {
                enabled = specific.Enabled.Value;
                // Null for every non-tameable species (see CreatureDefinition.Tameable) - only
                // tameable species get a star filter, everyone else is unfiltered (0) but still shows
                // its rolled star level in the display name below.
                minStars = specific.MinStars?.Value ?? 0;
                isMonsterIcon = specific.IsMonster;
                candidateCategoryKey = $"creature:{specificKey}";
            }
            else if (IsHostileMonster(character))
            {
                enabled = RadarConfig.EnableMonsters.Value;
                minStars = 0;
                isMonsterIcon = true;
                candidateCategoryKey = "creature:_monster";
            }
            else if (IsPassiveAnimal(character))
            {
                enabled = RadarConfig.EnableAnimals.Value;
                minStars = 0;
                isMonsterIcon = false;
                candidateCategoryKey = "creature:_animal";
            }
            else
            {
                return false;
            }

            if (!enabled || starLevel < minStars) return false;
            if (specific?.Tamed != null && !RadarConfig.PassesTameFilter(specific.Tamed.Value, character.IsTamed())) return false;

            if (starLevel > 0) displayName += $" ({new string('★', starLevel)})";

            // Symmetric with resources/POI (rule.VanillaIcon): pass the matched species' verified
            // trophy sprite name explicitly instead of letting PinManager re-derive a lookup key
            // from the raw prefab name - that mismatch (e.g. "bjorn" vs a dict keyed "bear") is
            // exactly what made Bear's icon (and its classification) silently fail before.
            string vanillaIcon = specificKey != null ? VanillaIconResolver.GetCreatureTrophySprite(specificKey) : null;
            icon = PinManager.ResolvePerObjectPin(nameLower, isMonsterIcon ? "monster.png" : "animal.png", vanillaIcon);
            categoryKey = candidateCategoryKey;
            return true;
        }

        // FISH - every live fish also carries the ItemDrop it becomes once caught, whose item quality
        // (1-5, synced through the ZDO) is the fish's rarity. Filtered per quality level, and shown in the
        // label as a " (Q3)" suffix - so fish of different quality also land in separate pins.
        private static bool TryClassifyFish(GameObject go, string nameLower, out string displayName, out Sprite icon, out string categoryKey)
        {
            ItemDrop itemDrop = go.GetComponent<ItemDrop>();
            int quality = RadarConfig.ClampFishQuality(itemDrop != null && itemDrop.m_itemData != null ? itemDrop.m_itemData.m_quality : 1);

            if (!RadarConfig.IsFishQualityShown(quality) || !TryClassifyAliasOnly(nameLower, "animal.png", out displayName, out icon, out categoryKey))
            {
                displayName = string.Empty;
                icon = null;
                categoryKey = null;
                return false;
            }

            displayName += ItemCluster.FishQualitySuffix(quality);
            return true;
        }

        // Shared by FISH and LEVIATHAN - neither has a HoverText/Character.GetHoverName() to derive
        // a real species name from, so the label always comes from the matched CreatureDefinition's
        // own DisplayName instead. Matched via the same exact-alias AliasLookup as regular
        // creatures, gated by the same Group_Creatures toggle (already checked by the caller).
        private static bool TryClassifyAliasOnly(string nameLower, string defaultIconPng, out string displayName, out Sprite icon, out string categoryKey)
        {
            displayName = string.Empty;
            icon = null;
            categoryKey = null;

            if (string.IsNullOrEmpty(nameLower) || !RadarConfig.AliasLookup.TryGetValue(nameLower, out var def) ||
                !RadarConfig.Creatures.TryGetValue(def.CanonicalKey, out var entry) || !entry.Enabled.Value)
            {
                return false;
            }

            displayName = def.DisplayName;
            icon = PinManager.ResolvePerObjectPin(nameLower, defaultIconPng, VanillaIconResolver.GetCreatureTrophySprite(def.CanonicalKey));
            categoryKey = $"creature:{def.CanonicalKey}";
            return true;
        }

        // Exact-alias lookup against the cleaned prefab name - see RadarConfig.AliasLookup. Flat
        // dictionary means no ordering requirement, unlike an old Contains()-based scan (a variant
        // like "greydwarf_elite" doesn't need to be declared before "greydwarf").
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

        internal static bool IsEnabled(string categoryKey)
        {
            if (!RadarConfig.Group_Creatures.Value) return false;

            string sub = categoryKey.Substring("creature:".Length);
            if (sub == "_monster") return RadarConfig.EnableMonsters.Value;
            if (sub == "_animal") return RadarConfig.EnableAnimals.Value;
            return RadarConfig.Creatures.TryGetValue(sub, out var entry) && entry.Enabled.Value;
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
    }
}
