using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    /// <summary>
    /// Derives a default per-species/per-resource pin icon from sprites Valheim already ships,
    /// instead of requiring a custom PNG for every tracked type. Creatures resolve to their Trophy
    /// item's icon (e.g. Wolf -> TrophyWolf); resources resolve to their pickup item's icon (e.g.
    /// Raspberry bush -> the Raspberry item icon) when a caller supplies that item name explicitly.
    ///
    /// Mappings below are best-effort, based on established Valheim modding conventions - not
    /// verified against a live game instance. A wrong/missing entry is harmless: ObjectDB.GetItemPrefab
    /// simply returns null and the caller falls through to its next icon tier (category PNG / Icon3).
    /// </summary>
    public static class VanillaIconResolver
    {
        // Creature prefab keys (lowercased Unity object name) whose Trophy prefab doesn't follow the
        // "Trophy<Name>" convention, or where casing information was lost during lowercasing upstream.
        private static readonly Dictionary<string, string> CreatureTrophyOverrides = new Dictionary<string, string>
        {
            ["troll"] = "TrophyForestTroll",
            ["greydwarf_elite"] = "TrophyGreydwarfBrute",
            ["greydwarf_shaman"] = "TrophyGreydwarfShaman",
            ["ghost"] = "TrophyWraith",
            ["seekerbrute"] = "TrophySeekerBrute",
            ["gd_king"] = "TrophyTheElder",
            ["dragon"] = "TrophyModer",
            ["goblinking"] = "TrophyGoblinKing",
            ["seekerqueen"] = "TrophyQueen",
            ["charred_melee"] = "TrophyCharredMelee",
            ["charred_archer"] = "TrophyCharredArcher",
            ["charred_mage"] = "TrophyCharredMage",
        };

        // Sprite==null is a cached "confirmed miss" entry, distinct from "not yet looked up".
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// Attempts to resolve a vanilla icon for a tracked object.
        /// </summary>
        /// <param name="creatureKey">Cleaned/lowercased prefab name, used to resolve a creature's Trophy icon when <paramref name="explicitItemPrefab"/> is null.</param>
        /// <param name="explicitItemPrefab">Known vanilla item prefab name to use directly (for resources), bypassing the creature/Trophy lookup.</param>
        public static bool TryResolveIcon(string creatureKey, string explicitItemPrefab, out Sprite sprite)
        {
            string prefabName = !string.IsNullOrEmpty(explicitItemPrefab)
                ? explicitItemPrefab
                : ResolveCreatureTrophyPrefab(creatureKey);

            sprite = null;
            if (string.IsNullOrEmpty(prefabName)) return false;

            if (SpriteCache.TryGetValue(prefabName, out sprite))
            {
                return sprite != null;
            }

            sprite = LookupItemIcon(prefabName);
            if (sprite != null)
            {
                SpriteCache[prefabName] = sprite;
                return true;
            }

            // Only cache a miss once ObjectDB is actually loaded - otherwise a lookup attempted
            // before the world/ObjectDB is ready would be cached as a permanent miss for the session.
            if (ObjectDB.instance != null)
            {
                SpriteCache[prefabName] = null;
            }

            return false;
        }

        /// <summary>Resolved prefab name for the given lookup, for use as a stable Minimap icon cache key.</summary>
        public static string GetCacheKey(string creatureKey, string explicitItemPrefab)
        {
            return !string.IsNullOrEmpty(explicitItemPrefab) ? explicitItemPrefab : ResolveCreatureTrophyPrefab(creatureKey);
        }

        private static string ResolveCreatureTrophyPrefab(string creatureKey)
        {
            if (string.IsNullOrEmpty(creatureKey)) return null;
            if (CreatureTrophyOverrides.TryGetValue(creatureKey, out string overridden)) return overridden;

            // Falls back to the "Trophy<Name>" convention most vanilla creatures follow
            // (Wolf -> TrophyWolf, Boar -> TrophyBoar, Deer -> TrophyDeer, ...). Only safe for
            // single-word keys - casing for multi-word prefab names can't be reconstructed once
            // lowercased, so those must go through CreatureTrophyOverrides instead.
            if (creatureKey.Contains("_") || creatureKey.Contains(" ")) return null;
            return "Trophy" + char.ToUpperInvariant(creatureKey[0]) + creatureKey.Substring(1);
        }

        private static Sprite LookupItemIcon(string prefabName)
        {
            if (ObjectDB.instance == null) return null;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab == null) return null;

            ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
            return itemDrop != null ? itemDrop.m_itemData?.GetIcon() : null;
        }
    }
}
