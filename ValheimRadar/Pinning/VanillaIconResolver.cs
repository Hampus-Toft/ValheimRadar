using System;
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
        // Jotunn's GUIManager lazily triggers its own AssetManager static initializer (a Harmony
        // transpiler patch) the first time GetSprite() is called. On some client/server/mod-list
        // combinations that internal Jotunn patch throws (observed: a HarmonyException wrapped in
        // a TypeInitializationException from an IL compile error in Jotunn's own AssetBundleLoader
        // patch - nothing in this codebase touches that code path). Once a type's static
        // constructor throws, .NET permanently marks it broken and every later call rethrows the
        // same exception, so a single bad GetSprite() call would otherwise kill vanilla icon
        // lookups - and, since ObjectEvaluator.ShouldPinGameObject calls into this every scan tick
        // for every detected object, the uncaught exception would propagate out of
        // RadarPlugin.ScanAndPinObjects and abort pin placement entirely, every tick, for the rest
        // of the session. Caught and latched here instead, so the mod falls back to default pin
        // icons (still fully functional) rather than going dark.
        private static bool jotunnIconLookupDisabled;

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
            sprite = null;
            if (jotunnIconLookupDisabled)
            {
                return false;
            }

            string spriteName = !string.IsNullOrEmpty(explicitSpriteName)
                ? explicitSpriteName
                : ResolveCreatureTrophySprite(creatureKey);

            if (string.IsNullOrEmpty(spriteName) || GUIManager.Instance == null)
            {
                return false;
            }

            try
            {
                sprite = GUIManager.Instance.GetSprite(spriteName);
            }
            catch (Exception ex)
            {
                jotunnIconLookupDisabled = true;
                Debug.LogWarning($"[ValheimRadar] Disabling vanilla icon lookups: Jotunn's GUIManager failed ({ex.GetType().Name}: {ex.Message}). Pins will use default/custom icons for the rest of this session.");
                return false;
            }

            return sprite != null;
        }

        private static string ResolveCreatureTrophySprite(string creatureKey)
        {
            if (string.IsNullOrEmpty(creatureKey)) return null;
            return CreatureTrophySprites.TryGetValue(creatureKey, out string spriteName) ? spriteName : null;
        }
    }
}
