using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Type #2 (semi-permanent) discovery entry point - trees/ores/ground pickables, berries,
    // mushrooms, flowers & crops, wild beehives. Wraps a PermanentSpatialScanner: once a map cell
    // has been physically queried it is never queried again this session, since a resource found
    // there is already recorded forever in PinManager's raw point store regardless of whether the
    // cell itself gets re-scanned. Returns only newly-discovered points each tick - RadarPlugin
    // folds them into the existing persistent cluster set incrementally rather than reclustering
    // everything (see RadarPlugin.ScanAndPinObjects / PinManager.SyncPersistentClusters).
    public static class ResourceScanner
    {
        private static readonly PermanentSpatialScanner scanner = new PermanentSpatialScanner(Classify);

        // Called only on world unload/disconnect - see PermanentSpatialScanner.Reset for why
        // ordinary config changes deliberately don't reset this.
        public static void Reset() => scanner.Reset();

        public static List<TrackedItem> ScanNewCells(Vector3 playerPos, float scanRadius, int maxNewCellsPerTick) =>
            scanner.ScanNewCells(playerPos, scanRadius, maxNewCellsPerTick);

        private static TrackedItem Classify(ZNetView netView, GameObject go, string nameLower)
        {
            if (ScanFilters.ShouldReject(go, nameLower)) return null;
            if (!ResourceEvaluator.TryClassify(go, nameLower, out string displayName, out Sprite icon, out string categoryKey)) return null;

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
