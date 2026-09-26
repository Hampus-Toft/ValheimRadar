using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // One cell's result from a PermanentSpatialScanner pass. Verified is true only when the cell's
    // zone was inside the loaded (object-instantiated) area when it was queried - see
    // ScanGeometry.IsCellInLoadedArea - so the absence of a recorded point from Items can be trusted
    // as evidence (PinManager's depletion reconciliation only looks at verified cells). Points whose
    // height falls outside MinY..MaxY weren't covered by the query at all.
    internal sealed class ScannedCell
    {
        internal readonly ScanCellKey Key;
        internal readonly bool Verified;
        internal readonly float MinY;
        internal readonly float MaxY;
        internal readonly List<TrackedItem> Items;

        internal ScannedCell(ScanCellKey key, bool verified, float minY, float maxY, List<TrackedItem> items)
        {
            Key = key;
            Verified = verified;
            MinY = minY;
            MaxY = maxY;
            Items = items;
        }
    }

    // Spatial scanner for type #2/#3 (semi-permanent) content - resources and physics-detected
    // points of interest don't move, so unlike SpatialCellScanner (creatures) a cell doesn't need
    // re-querying every few ticks. Every point found is recorded in PinManager's raw point store
    // (see PinManager.RecordScannedCells), so a cell only has to be queried again to find something
    // new or to notice something is gone. Three triggers, in priority order:
    //
    //  1. A cell never scanned before is scanned the same tick it enters ScanRadius - never trickled
    //     in over several ticks, so ground the player only passes through briefly (flying, boating,
    //     a fast mount) can't enter and leave range unqueried.
    //  2. A cell whose only scans so far happened while it was OUTSIDE the loaded area is scanned
    //     again the moment it's inside it. Beyond that area ZNetScene instantiates nothing, so an
    //     earlier scan there could only ever see (at best) a few distant objects - under the old
    //     "scan once, ever" model this is what left resources unpinned in some regions: a cell at
    //     the edge of ScanRadius was queried before its objects existed and never looked at again.
    //  3. Every other in-range, loaded cell is re-scanned once its last scan is older than
    //     rescanInterval (config ResourceRescanInterval, 0 = never), oldest first and at most
    //     MaxRescansPerTick per call, so the steady-state cost stays a couple of cell queries per
    //     tick. This catches anything still missed (ZDOs that reached the client late, objects out
    //     of the vertical query range on an earlier pass) and is what lets PinManager notice
    //     depleted resources - mined out by this player or anyone else - and drop their pins.
    //
    // A cell is skipped (not scanned, state untouched) while ScanGeometry.IsCellReady says its
    // GameObjects haven't finished instantiating - see that method for why this matters on
    // dedicated servers.
    internal sealed class PermanentSpatialScanner
    {
        internal const int MaxRescansPerTick = 2;

        private sealed class CellState
        {
            public float LastScanTime;
            public bool ScannedWhileLoaded;
        }

        private readonly Func<ZNetView, GameObject, string, TrackedItem> classify;
        private readonly Dictionary<ScanCellKey, CellState> cells = new Dictionary<ScanCellKey, CellState>();

        internal PermanentSpatialScanner(Func<ZNetView, GameObject, string, TrackedItem> classify)
        {
            this.classify = classify;
        }

        // Called only on world unload/disconnect - deliberately NOT on ordinary config changes (e.g.
        // ScanRadius), since a larger radius simply brings more unscanned cells into range on its
        // own; it never invalidates ground already covered.
        internal void Reset()
        {
            cells.Clear();
        }

        // Scans whatever cells within scanRadius of playerPos are due (see class comment) and
        // returns one ScannedCell per cell actually queried this call - empty on most ticks once the
        // surroundings are explored and fresh. Items are everything found in those cells, not just
        // points never seen before; PinManager's raw store dedups them.
        internal List<ScannedCell> Scan(Vector3 playerPos, float scanRadius, float now, float rescanInterval)
        {
            List<ScannedCell> results = new List<ScannedCell>();
            List<ScanCellKey> due = null;
            Vector3 referencePos = ScanGeometry.GetReferencePosition(playerPos);

            foreach (ScanCellKey key in ComputeActiveCells(playerPos, scanRadius))
            {
                bool loaded = ScanGeometry.IsCellInLoadedArea(key, referencePos);
                cells.TryGetValue(key, out CellState state);

                if (state == null || (loaded && !state.ScannedWhileLoaded))
                {
                    TryScanCell(key, loaded, playerPos.y, now, results);
                }
                else if (loaded && rescanInterval > 0f && now - state.LastScanTime >= rescanInterval)
                {
                    (due ?? (due = new List<ScanCellKey>())).Add(key);
                }
            }

            if (due == null) return results;

            // Oldest first. Readiness checks (ZNetScene.IsAreaReady walks every ZDO in a 3x3 zone
            // block) are bounded too, so a stretch of not-yet-ready cells can't blow the budget.
            due.Sort((a, b) => cells[a].LastScanTime.CompareTo(cells[b].LastScanTime));
            int rescanned = 0;
            int attempts = 0;
            foreach (ScanCellKey key in due)
            {
                if (rescanned >= MaxRescansPerTick || attempts >= MaxRescansPerTick * 2) break;
                attempts++;
                if (TryScanCell(key, loaded: true, playerPos.y, now, results)) rescanned++;
            }

            return results;
        }

        private bool TryScanCell(ScanCellKey key, bool loaded, float playerY, float now, List<ScannedCell> results)
        {
            // Not ready = left exactly as it was, so it's simply retried on a later tick instead of
            // being recorded as (partially) empty.
            if (!ScanGeometry.IsCellReady(key, playerY)) return false;

            ScanGeometry.GetCellVerticalRange(key, playerY, out float minY, out float maxY);
            List<TrackedItem> items = ScanGeometry.ScanCell(key, minY, maxY, classify);

            if (!cells.TryGetValue(key, out CellState state))
            {
                state = new CellState();
                cells[key] = state;
            }

            state.LastScanTime = now;
            state.ScannedWhileLoaded |= loaded;

            results.Add(new ScannedCell(key, loaded, minY, maxY, items));
            return true;
        }

        private static List<ScanCellKey> ComputeActiveCells(Vector3 playerPos, float scanRadius)
        {
            List<ScanCellKey> result = new List<ScanCellKey>();

            int minX = ScanGeometry.GetCellIndex(playerPos.x - scanRadius);
            int maxX = ScanGeometry.GetCellIndex(playerPos.x + scanRadius);
            int minZ = ScanGeometry.GetCellIndex(playerPos.z - scanRadius);
            int maxZ = ScanGeometry.GetCellIndex(playerPos.z + scanRadius);

            // Half-diagonal padding so a cell whose center falls just outside the radius, but that
            // still partially overlaps the scan circle, isn't skipped entirely - mirrors
            // SpatialCellScanner's own inclusion test.
            float inclusionRadius = scanRadius + (ScanGeometry.CellSize * 0.70711f);
            float inclusionRadiusSq = inclusionRadius * inclusionRadius;

            for (int ix = minX; ix <= maxX; ix++)
            {
                for (int iz = minZ; iz <= maxZ; iz++)
                {
                    float dx = ScanGeometry.GetCellCenter(ix) - playerPos.x;
                    float dz = ScanGeometry.GetCellCenter(iz) - playerPos.z;

                    if ((dx * dx) + (dz * dz) <= inclusionRadiusSq)
                    {
                        result.Add(new ScanCellKey(ix, iz));
                    }
                }
            }

            return result;
        }
    }
}
