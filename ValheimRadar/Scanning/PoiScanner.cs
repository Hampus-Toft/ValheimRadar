using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Type #3 (mostly-permanent) discovery entry point for POI content found via physics scan
    // (dungeon entrances, runestones, abandoned ruins, monster-spawner landmarks, the trader,
    // natural loot chests). Shares ResourceScanner's "scan each cell once" model
    // (PermanentSpatialScanner) - see PoiEvaluator for why this exists alongside the much cheaper,
    // ZoneSystem-based LocationScanner.
    public static class PoiScanner
    {
        private static readonly PermanentSpatialScanner scanner = new PermanentSpatialScanner(Classify);

        // Called only on world unload/disconnect - see PermanentSpatialScanner.Reset for why
        // ordinary config changes deliberately don't reset this.
        public static void Reset() => scanner.Reset();

        public static List<TrackedItem> ScanNewCells(Vector3 playerPos, float scanRadius) =>
            scanner.ScanNewCells(playerPos, scanRadius);

        private static TrackedItem Classify(ZNetView netView, GameObject go, string nameLower)
        {
            if (ScanFilters.ShouldReject(go, nameLower)) return null;
            if (!PoiEvaluator.TryClassify(go, nameLower, out string displayName, out Sprite icon, out string categoryKey)) return null;

            return new TrackedItem
            {
                DisplayName = displayName,
                Icon = icon,
                IsPersistent = true,
                CategoryKey = categoryKey
            };
        }
    }
}
