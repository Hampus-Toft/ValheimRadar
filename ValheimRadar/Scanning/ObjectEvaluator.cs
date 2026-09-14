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
