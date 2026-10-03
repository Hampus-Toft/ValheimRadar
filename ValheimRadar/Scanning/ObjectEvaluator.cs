using UnityEngine;

namespace ValheimRadar
{
    // Thin composition root over the three type-specific evaluators (CreatureEvaluator,
    // ResourceEvaluator, PoiEvaluator) plus LocationScanner's ZoneSystem-based POI table. Each
    // scanner (CreatureScanner/ResourceScanner/PoiScanner) calls straight into its own evaluator to
    // classify a freshly-scanned object; this class exists so PinManager/RadarPlugin have one place
    // to resolve a categoryKey's enabled-state/icon without caring which evaluator originally
    // produced it - used to redraw/hide pins on config toggle, and to reconstruct pins loaded from
    // disk after a relog.
    public static class ObjectEvaluator
    {
        /// <summary>
        /// Strips known engine/asset prefixes and the "(Clone)" instantiation suffix from a raw
        /// Unity object name. Used for display-name formatting and the icon-cache/PNG-override key
        /// (see PinManager.ResolvePerObjectPin) - NOT for whitelist match decisions (see
        /// NameFormatting.IsExactAlias), since stripping "piece_" here is exactly what could make a
        /// player-built object collide with an unrelated natural one of the same base name.
        /// </summary>
        public static string StripKnownPrefixes(string rawName) => NameFormatting.StripKnownPrefixes(rawName);

        internal static string FormatHumanFriendlyName(string rawName) => NameFormatting.FormatHumanFriendlyName(rawName);

        internal static bool ContainsAny(string value, string[] markers) => NameFormatting.ContainsAny(value, markers);

        internal static RadarConfig.CreatureConfigEntry FindCreatureOverride(string nameLower, out string matchedKey) =>
            CreatureEvaluator.FindCreatureOverride(nameLower, out matchedKey);

        // Re-derives whether a category (identified by the categoryKey a scanner's TryClassify
        // produced) is currently enabled purely from live config, without needing the original
        // GameObject.
        public static bool IsCategoryEnabled(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return false;

            if (categoryKey.StartsWith("creature:")) return CreatureEvaluator.IsEnabled(categoryKey);

            if (categoryKey.StartsWith("resource:"))
            {
                string id = categoryKey.Substring("resource:".Length);
                if (ResourceEvaluator.TryGetRule(id, out var resourceRule)) return resourceRule.Enabled();
                if (PoiEvaluator.TryGetRule(id, out var poiRule)) return poiRule.Enabled();
                return false;
            }

            if (categoryKey.StartsWith("location:")) return LocationScanner.IsLocationCategoryEnabled(categoryKey);

            return false;
        }

        // True for ore-deposit categories (see ResourceRule.OreDeposit): never clustered, and unlabeled
        // when their icon identifies them.
        public static bool IsOreDepositCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("resource:")) return false;

            return ResourceEvaluator.TryGetRule(categoryKey.Substring("resource:".Length), out var rule) && rule.OreDeposit;
        }

        // True when the category's pins drop their name once the icon identifies the type (see
        // ResourceRule.IconOnlyLabel): ore deposits and regrowing pickables.
        public static bool HasIconOnlyLabel(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("resource:")) return false;

            return ResourceEvaluator.TryGetRule(categoryKey.Substring("resource:".Length), out var rule) && rule.IconOnlyLabel;
        }

        // The fixed display name a resource rule assigns from the prefab name alone, or null when the
        // rule derives it from the live object instead. Lets saved points pick up renamed labels (e.g.
        // "Silver Deposit" -> "Silver") so they still dedup against freshly scanned ones.
        internal static string GetResourceDisplayNameOverride(string categoryKey, string rawName)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("resource:")) return null;
            if (!ResourceEvaluator.TryGetRule(categoryKey.Substring("resource:".Length), out var rule)) return null;

            return rule.DisplayNameOverride?.Invoke(rawName ?? string.Empty);
        }

        // True when a persisted category never grows back once taken (see ResourceRule.Depletable),
        // i.e. its points may be removed once mined or found missing. Locations and creatures never are.
        public static bool IsCategoryDepletable(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("resource:")) return false;

            string id = categoryKey.Substring("resource:".Length);
            if (ResourceEvaluator.TryGetRule(id, out var resourceRule)) return resourceRule.Depletable;
            if (PoiEvaluator.TryGetRule(id, out var poiRule)) return poiRule.Depletable;
            return false;
        }

        // Classifies a live object (e.g. one the player just hit) and returns its categoryKey only
        // if that category is depletable. Resource rules first, then POI rules - the same split the
        // two scanners use.
        internal static bool TryGetDepletableCategory(GameObject go, string nameLower, out string categoryKey)
        {
            categoryKey = null;

            if (ResourceEvaluator.TryMatchRule(go, nameLower, out var resourceRule))
            {
                if (!resourceRule.Depletable) return false;
                categoryKey = $"resource:{resourceRule.Id}";
                return true;
            }

            if (PoiEvaluator.TryMatchRule(go, nameLower, out var poiRule) && poiRule.Depletable)
            {
                categoryKey = $"resource:{poiRule.Id}";
                return true;
            }

            return false;
        }

        // Classifies a live object (e.g. a Pickable the player just picked) and returns its categoryKey
        // only if it's a resource that grows back - i.e. matched by a non-Depletable ResourceRule
        // (berries, mushrooms, flowers, crops). The caller still checks the Pickable's own respawn timer.
        internal static bool TryGetRespawningCategory(GameObject go, string nameLower, out string categoryKey)
        {
            categoryKey = null;
            if (!ResourceEvaluator.TryMatchRule(go, nameLower, out var resourceRule) || resourceRule.Depletable) return false;

            categoryKey = $"resource:{resourceRule.Id}";
            return true;
        }

        // Only resource/POI and location categories are persisted to disk (see PinManager), so this
        // only needs to resolve icons for "resource:"/"location:" keys - used to re-resolve the icon
        // Sprite after loading cached pin positions from a previous session.
        public static string GetDefaultIconForCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return null;
            if (categoryKey.StartsWith("location:")) return LocationScanner.GetLocationIconPng(categoryKey);
            if (!categoryKey.StartsWith("resource:")) return null;

            string id = categoryKey.Substring("resource:".Length);
            if (ResourceEvaluator.TryGetRule(id, out var resourceRule)) return resourceRule.IconPng;
            if (PoiEvaluator.TryGetRule(id, out var poiRule)) return poiRule.IconPng;
            return null;
        }

        // Companion to GetDefaultIconForCategory - resolves the same rule's verified vanilla icon
        // sprite name, so pins reloaded from disk after a relog get the same per-type icon as
        // freshly-scanned ones instead of only the category PNG/fallback.
        public static string GetVanillaIconForCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return null;
            if (categoryKey.StartsWith("location:")) return LocationScanner.GetLocationVanillaIcon(categoryKey);
            if (!categoryKey.StartsWith("resource:")) return null;

            string id = categoryKey.Substring("resource:".Length);
            if (ResourceEvaluator.TryGetRule(id, out var resourceRule)) return resourceRule.VanillaIcon;
            if (PoiEvaluator.TryGetRule(id, out var poiRule)) return poiRule.VanillaIcon;
            return null;
        }
    }
}
