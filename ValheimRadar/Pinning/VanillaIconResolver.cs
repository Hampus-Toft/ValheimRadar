using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimRadar
{
    /// <summary>
    /// Resolves a default per-species pin icon from Valheim's own icon atlas via Jotunn's
    /// GUIManager, instead of requiring a custom PNG for every tracked creature. Names below are
    /// exact, verified sprite names (see Jotunn's generated sprite atlas reference at
    /// valheim-modding.github.io/Jotunn/data/gui/sprite-list.html) rather than a guessed
    /// "Trophy" + Capitalize(prefab) convention - that guesswork is what produced wrong icons
    /// before (e.g. Bear's real trophy sprite is "TrophyBjorn", not "TrophyBear").
    /// </summary>
    public static class VanillaIconResolver
    {
        private static readonly Dictionary<string, string> CreatureTrophySprites = new Dictionary<string, string>
        {
            ["boar"] = "TrophyBoar",
            ["neck"] = "TrophyNeck",
            ["deer"] = "TrophyDeer",
            ["greydwarf_shaman"] = "TrophyGreydwarfShaman",
            ["greydwarf_elite"] = "TrophyGreydwarfBrute",
            ["greydwarf"] = "TrophyGreydwarf",
            ["skeleton"] = "TrophySkeleton",
            ["troll"] = "TrophyForestTroll",
            ["draugr_elite"] = "TrophyDraugrElite",
            ["draugr"] = "TrophyDraugr",
            ["blob"] = "TrophyBlob",
            ["leech"] = "TrophyLeech",
            ["wraith"] = "TrophyWraith",
            ["ghost"] = "TrophyGhost",
            ["abomination"] = "TrophyAbomination",
            ["wolf"] = "TrophyWolf",
            ["bear"] = "TrophyBjorn",
            ["stonegolem"] = "TrophySGolem",
            ["drake"] = "TrophyHatchling",
            ["lox"] = "TrophyLox",
            ["deathsquito"] = "TrophyDeathSquito",
            ["fuling_berserker"] = "TrophyGoblinBrute",
            ["fuling_shaman"] = "TrophyGoblinShaman",
            ["fuling"] = "TrophyGoblin",
            ["growth"] = "TrophyGrowth",
            ["seeker"] = "TrophySeeker",
            ["seekerbrute"] = "TrophySeekerBrute",
            ["gjall"] = "TrophyGjall",
            ["tick"] = "TrophyTick",
            ["dvergrmage"] = "TrophyDvergr",
            ["dvergr"] = "TrophyDvergr",
            ["fenring"] = "TrophyFenring",

            // Bosses (matched via the generic hostile-monster bucket, since they have no
            // dedicated RadarConfig.CreatureDefinitions entry).
            ["gd_king"] = "TrophyTheElder",
            ["dragon"] = "TrophyDragonQueen",
            ["goblinking"] = "TrophyGoblinKing",
            ["seekerqueen"] = "TrophySeekerQueen",
            ["charred_melee"] = "TrophyCharredMelee",
            ["charred_archer"] = "TrophyCharredArcher",
            ["charred_mage"] = "TrophyCharredMage",
        };

        /// <summary>
        /// Attempts to resolve a vanilla icon sprite for a tracked object.
        /// </summary>
        /// <param name="creatureKey">Cleaned/lowercased prefab name, used to look up a creature's trophy sprite when <paramref name="explicitSpriteName"/> is null.</param>
        /// <param name="explicitSpriteName">Exact Jotunn/vanilla sprite atlas name to use directly (for resources), bypassing the creature/trophy lookup.</param>
        public static bool TryResolveIcon(string creatureKey, string explicitSpriteName, out Sprite sprite)
        {
            string spriteName = !string.IsNullOrEmpty(explicitSpriteName)
                ? explicitSpriteName
                : ResolveCreatureTrophySprite(creatureKey);

            sprite = null;
            if (string.IsNullOrEmpty(spriteName) || GUIManager.Instance == null)
            {
                return false;
            }

            sprite = GUIManager.Instance.GetSprite(spriteName);
            return sprite != null;
        }

        private static string ResolveCreatureTrophySprite(string creatureKey)
        {
            if (string.IsNullOrEmpty(creatureKey)) return null;
            return CreatureTrophySprites.TryGetValue(creatureKey, out string spriteName) ? spriteName : null;
        }
    }
}
