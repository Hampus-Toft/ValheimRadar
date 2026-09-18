using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Rotating-cache spatial scanner for type #1 (ephemeral) content - creatures move, so unlike the
    // permanent-content scanner (see PermanentSpatialScanner) every cell in range must keep being
    // re-queried periodically rather than scanned once and trusted forever. Splits a full-radius
    // scan into a rotation of spatial batches so a large ScanRadius doesn't force one huge Physics
    // query (and all the classification work behind it) into a single frame - only a slice of the
    // currently in-range cells is re-scanned per call, cycling through the remaining cells over
    // subsequent calls. A cell not re-scanned this call is served from cache, so its data is at most
    // one full rotation (batchCount calls) stale - the tradeoff that keeps the per-call cost to
    // roughly 1/batchCount of a full scan instead of the whole radius every time.
    internal sealed class SpatialCellScanner
    {
        private readonly Func<ZNetView, GameObject, string, TrackedItem> classify;

        private readonly Dictionary<ScanCellKey, List<TrackedItem>> cellCache = new Dictionary<ScanCellKey, List<TrackedItem>>();
        private readonly LinkedList<ScanCellKey> rotation = new LinkedList<ScanCellKey>();
        private readonly Dictionary<ScanCellKey, LinkedListNode<ScanCellKey>> rotationNodes = new Dictionary<ScanCellKey, LinkedListNode<ScanCellKey>>();

        internal SpatialCellScanner(Func<ZNetView, GameObject, string, TrackedItem> classify)
        {
            this.classify = classify;
        }

        // Drops all cached cell data and rotation state. Called on disconnect/world unload so stale
        // positions from one world never leak into the next.
        internal void Reset()
        {
            cellCache.Clear();
            rotation.Clear();
            rotationNodes.Clear();
        }

        // Re-scans a rotating slice of the cells currently within scanRadius of playerPos and
        // returns the combined (cached + freshly-scanned) items for every cell still in range.
        // batchCount is the target number of calls a full rotation should take; the actual per-call
        // slice is recomputed every call from the current cell count so it adapts immediately to
        // ScanRadius or player-movement changes without needing an explicit reset.
        internal List<TrackedItem> ScanBatch(Vector3 playerPos, float scanRadius, int batchCount)
        {
            HashSet<ScanCellKey> activeCells = ComputeActiveCells(playerPos, scanRadius);

            PruneInactiveCells(activeCells);
            AddNewCells(activeCells);

            int cellsPerBatch = Math.Max(1, (int)Math.Ceiling(rotation.Count / (double)Math.Max(1, batchCount)));

            for (int i = 0; i < cellsPerBatch && rotation.Count > 0; i++)
            {
                ScanCellKey key = rotation.First.Value;
                rotation.RemoveFirst();

                cellCache[key] = ScanGeometry.ScanCell(key, playerPos.y, classify);

                rotationNodes[key] = rotation.AddLast(key);
            }

            List<TrackedItem> combined = new List<TrackedItem>();
            HashSet<ZDOID> seen = new HashSet<ZDOID>();

            foreach (var key in activeCells)
            {
                if (!cellCache.TryGetValue(key, out List<TrackedItem> items)) continue;

                foreach (var item in items)
                {
                    if (seen.Add(item.Zdoid)) combined.Add(item);
                }
            }

            return combined;
        }

        private static HashSet<ScanCellKey> ComputeActiveCells(Vector3 playerPos, float scanRadius)
        {
            HashSet<ScanCellKey> result = new HashSet<ScanCellKey>();

            int minX = ScanGeometry.GetCellIndex(playerPos.x - scanRadius);
            int maxX = ScanGeometry.GetCellIndex(playerPos.x + scanRadius);
            int minZ = ScanGeometry.GetCellIndex(playerPos.z - scanRadius);
            int maxZ = ScanGeometry.GetCellIndex(playerPos.z + scanRadius);

            // Half-diagonal padding so a cell whose center falls just outside the radius, but that
            // still partially overlaps the scan circle, isn't skipped entirely.
            float inclusionRadius = scanRadius + (ScanGeometry.CellSize * 0.70711f);
            float inclusionRadiusSq = inclusionRadius * inclusionRadius;

            for (int ix = minX; ix <= maxX; ix++)
            {
                for (int iz = minZ; iz <= maxZ; iz++)
                {
                    float centerX = ScanGeometry.GetCellCenter(ix);
                    float centerZ = ScanGeometry.GetCellCenter(iz);
                    float dx = centerX - playerPos.x;
                    float dz = centerZ - playerPos.z;

                    if ((dx * dx) + (dz * dz) <= inclusionRadiusSq)
                    {
                        result.Add(new ScanCellKey(ix, iz));
                    }
                }
            }

            return result;
        }

        private void PruneInactiveCells(HashSet<ScanCellKey> activeCells)
        {
            List<ScanCellKey> toRemove = null;
            foreach (var key in rotationNodes.Keys)
            {
                if (activeCells.Contains(key)) continue;
                (toRemove ?? (toRemove = new List<ScanCellKey>())).Add(key);
            }

            if (toRemove == null) return;

            foreach (var key in toRemove)
            {
                rotation.Remove(rotationNodes[key]);
                rotationNodes.Remove(key);
                cellCache.Remove(key);
            }
        }

        private void AddNewCells(HashSet<ScanCellKey> activeCells)
        {
            foreach (var key in activeCells)
            {
                if (rotationNodes.ContainsKey(key)) continue;

                // Newly in-range cells are queued at the FRONT so freshly explored ground populates
                // within the next batch or two, instead of waiting behind cells that were already
                // due.
                rotationNodes[key] = rotation.AddFirst(key);
                cellCache[key] = new List<TrackedItem>();
            }
        }
    }
}
