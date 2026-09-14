using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // "Scan each cell once, ever" spatial scanner for type #2/#3 (semi-permanent) content -
    // resources and physics-detected points of interest don't move, and once a point is found it's
    // recorded forever in PinManager's raw point store regardless of whether its cell is ever
    // physically re-queried again (see PinManager.RecordRawPoints/rawPersistentPoints). So unlike
    // SpatialCellScanner (creatures), a cell that has already been physically scanned once this
    // world/session never needs scanning again - only genuinely new ground entering the scan radius
    // (new exploration, not just the passage of time) does. This means the physics cost of this
    // scanner shrinks over a session as the map gets explored, instead of paying a steady per-tick
    // cost forever like a rotating cache would.
    //
    // maxNewCellsPerTick bounds how many brand-new cells get physically queried on any single
    // discovery tick, so a sudden burst of newly-explored ground (teleporting, fast boat travel, a
    // ScanRadius increase) can't spike a single frame - the rest is simply picked up over the next
    // few ticks.
    internal sealed class PermanentSpatialScanner
    {
        private readonly Func<ZNetView, GameObject, string, TrackedItem> classify;
        private readonly HashSet<ScanCellKey> scannedCells = new HashSet<ScanCellKey>();

        internal PermanentSpatialScanner(Func<ZNetView, GameObject, string, TrackedItem> classify)
        {
            this.classify = classify;
        }

        // Called only on world unload/disconnect - deliberately NOT on ordinary config changes (e.g.
        // ScanRadius), since a larger radius simply brings more never-before-scanned cells into range
        // on its own; it never invalidates ground already covered, so resetting on every config
        // change would just force wasteful re-scanning of already-explored ground.
        internal void Reset()
        {
            scannedCells.Clear();
        }

        // Physically scans up to maxNewCellsPerTick cells that are within scanRadius of playerPos
        // and have never been scanned before, and returns whatever matched in them. Already-scanned
        // cells contribute nothing here (their finds are already permanently recorded by the
        // caller), so - unlike SpatialCellScanner.ScanBatch - this only ever returns a delta of
        // brand-new points, never the full accumulated set.
        internal List<TrackedItem> ScanNewCells(Vector3 playerPos, float scanRadius, int maxNewCellsPerTick)
        {
            List<ScanCellKey> newCells = ComputeUnscannedActiveCells(playerPos, scanRadius, maxNewCellsPerTick);
            if (newCells.Count == 0) return new List<TrackedItem>();

            List<TrackedItem> found = new List<TrackedItem>();

            foreach (var key in newCells)
            {
                scannedCells.Add(key);
                found.AddRange(ScanGeometry.ScanCell(key, playerPos.y, classify));
            }

            return found;
        }

        private List<ScanCellKey> ComputeUnscannedActiveCells(Vector3 playerPos, float scanRadius, int maxNewCellsPerTick)
        {
            List<ScanCellKey> result = new List<ScanCellKey>();
            if (maxNewCellsPerTick <= 0) return result;

            int minX = Mathf.FloorToInt((playerPos.x - scanRadius) / ScanGeometry.CellSize);
            int maxX = Mathf.FloorToInt((playerPos.x + scanRadius) / ScanGeometry.CellSize);
            int minZ = Mathf.FloorToInt((playerPos.z - scanRadius) / ScanGeometry.CellSize);
            int maxZ = Mathf.FloorToInt((playerPos.z + scanRadius) / ScanGeometry.CellSize);

            // Half-diagonal padding so a cell whose center falls just outside the radius, but that
            // still partially overlaps the scan circle, isn't skipped entirely - mirrors
            // SpatialCellScanner's own inclusion test.
            float inclusionRadius = scanRadius + (ScanGeometry.CellSize * 0.70711f);
            float inclusionRadiusSq = inclusionRadius * inclusionRadius;

            for (int ix = minX; ix <= maxX && result.Count < maxNewCellsPerTick; ix++)
            {
                for (int iz = minZ; iz <= maxZ && result.Count < maxNewCellsPerTick; iz++)
                {
                    ScanCellKey key = new ScanCellKey(ix, iz);
                    if (scannedCells.Contains(key)) continue;

                    float centerX = (ix + 0.5f) * ScanGeometry.CellSize;
                    float centerZ = (iz + 0.5f) * ScanGeometry.CellSize;
                    float dx = centerX - playerPos.x;
                    float dz = centerZ - playerPos.z;

                    if ((dx * dx) + (dz * dz) <= inclusionRadiusSq)
                    {
                        result.Add(key);
                    }
                }
            }

            return result;
        }
    }
}
