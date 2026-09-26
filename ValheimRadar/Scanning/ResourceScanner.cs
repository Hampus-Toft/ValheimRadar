using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Type #2 (semi-permanent) discovery entry point - trees/ores/ground pickables, berries,
    // mushrooms, flowers & crops, wild beehives. Wraps a PermanentSpatialScanner: new ground is
    // scanned as soon as it's in range, and already-scanned ground in the loaded area is re-scanned
    // periodically so late-loading resources still get found and depleted ones get removed (see
    // PermanentSpatialScanner and PinManager.RecordScannedCells).
    public static class ResourceScanner
    {
        private static readonly PermanentSpatialScanner scanner = new PermanentSpatialScanner(Classify);

        // Called only on world unload/disconnect - see PermanentSpatialScanner.Reset for why
        // ordinary config changes deliberately don't reset this.
        public static void Reset() => scanner.Reset();

        internal static List<ScannedCell> Scan(Vector3 playerPos, float scanRadius, float now, float rescanInterval) =>
            scanner.Scan(playerPos, scanRadius, now, rescanInterval);

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
