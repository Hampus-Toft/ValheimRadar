using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Type #1 (ephemeral) discovery entry point - creatures, fish, and the Leviathan. Wraps a
    // SpatialCellScanner (rotating per-cell cache - see RadarConfig.ScanBatchCount) configured to
    // classify via CreatureEvaluator. Called every discovery tick; RadarPlugin reclusters/re-syncs
    // its result fresh each time since positions can't be assumed stable between ticks, unlike
    // ResourceScanner/PoiScanner's permanently-cached points (see RadarPlugin.ScanAndPinObjects).
    public static class CreatureScanner
    {
        private static readonly SpatialCellScanner scanner = new SpatialCellScanner(Classify);

        // Called on world unload/disconnect and on relevant config changes (ScanRadius/
        // ScanBatchCount especially), so stale positions/rotation state from one world or setting
        // never leak into the next.
        public static void Reset() => scanner.Reset();

        public static List<TrackedItem> ScanBatch(Vector3 playerPos, float scanRadius, int batchCount) =>
            scanner.ScanBatch(playerPos, scanRadius, batchCount);

        private static TrackedItem Classify(ZNetView netView, GameObject go, string nameLower)
        {
            if (ScanFilters.ShouldReject(go, nameLower)) return null;
            if (!CreatureEvaluator.TryClassify(go, nameLower, out string displayName, out Sprite icon, out string categoryKey)) return null;

            return new TrackedItem
            {
                DisplayName = displayName,
                Icon = icon,
                IsPersistent = false,
                CategoryKey = categoryKey
            };
        }
    }
}
