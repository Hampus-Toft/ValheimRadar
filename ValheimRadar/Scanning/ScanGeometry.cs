using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Grid coordinate of one spatial scan cell - shared shape used by both SpatialCellScanner
    // (creatures) and PermanentSpatialScanner (resources/POI), even though each maintains its own
    // independent cache. Not required for correctness, but keeps world-space cell boundaries
    // aligned between the two scanning strategies.
    internal readonly struct ScanCellKey : IEquatable<ScanCellKey>
    {
        internal readonly int X;
        internal readonly int Z;

        internal ScanCellKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(ScanCellKey other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ScanCellKey other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Z;
    }

    // Physics.OverlapBox cell geometry and the low-level per-cell query shared by every spatial
    // scanner. Extracted from the original BatchScanner so SpatialCellScanner and
    // PermanentSpatialScanner don't each carry their own copy of the same Physics query logic.
    internal static class ScanGeometry
    {
        // Fixed rather than derived from ScanRadius: per-tick scanned AREA already scales with the
        // caller's own batching strategy (rotation slice / new-cells-per-tick) regardless of cell
        // size, so cell size only trades off Physics call COUNT vs call SIZE, not the scan budget
        // itself. 40m keeps that count reasonable across the whole 10-300m ScanRadius range.
        internal const float CellSize = 40f;

        // Vertical half-extent of a scan cell's collision box, independent of ScanRadius. ScanRadius
        // is a horizontal (XZ) distance, so reusing it for box height would mean a 300m ScanRadius
        // produces a 600m-tall query column per cell - capturing every collider from deep
        // underground to high above the skybox, layer-unfiltered. A fixed vertical range comfortably
        // covers Valheim's real terrain variance (caves through mountain peaks) around the player
        // without that blowup.
        internal const float CellHeight = 150f;

        // Physically queries one cell's world-space box and hands every live ZNetView-backed object
        // found inside it to classify, which returns either null (no match for this scanner's type)
        // or a TrackedItem with its type-specific fields already populated (DisplayName/Icon/
        // IsPersistent/CategoryKey) - this method fills in the remaining identity fields (Zdoid/
        // Position/RawName) that are the same regardless of which type matched.
        internal static List<TrackedItem> ScanCell(ScanCellKey key, float playerY, Func<ZNetView, GameObject, string, TrackedItem> classify)
        {
            List<TrackedItem> items = new List<TrackedItem>();
            HashSet<ZDOID> processedZdoids = new HashSet<ZDOID>();

            Vector3 center = new Vector3((key.X + 0.5f) * CellSize, playerY, (key.Z + 0.5f) * CellSize);
            Vector3 halfExtents = new Vector3(CellSize / 2f, CellHeight, CellSize / 2f);

            Collider[] hitColliders = Physics.OverlapBox(center, halfExtents);
            foreach (var hit in hitColliders)
            {
                if (hit == null) continue;

                GameObject obj = hit.gameObject;
                ZNetView netView = obj.GetComponentInParent<ZNetView>();

                if (netView == null || !netView.IsValid() || netView.GetZDO() == null) continue;

                ZDOID zdoid = netView.GetZDO().m_uid;
                if (!processedZdoids.Add(zdoid)) continue;

                string rawName = netView.gameObject.name.Replace("(Clone)", "").Trim().ToLower();

                TrackedItem item = classify(netView, netView.gameObject, rawName);
                if (item == null) continue;

                item.Zdoid = zdoid;
                item.Position = netView.transform.position;
                item.RawName = rawName;
                items.Add(item);
            }

            return items;
        }
    }
}
