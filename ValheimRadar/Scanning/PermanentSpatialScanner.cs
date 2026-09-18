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
    // Every unscanned cell currently within scanRadius is scanned as soon as it's safe to - as early
    // as the SAME tick it enters range, deliberately not trickled in over several ticks on its own.
    // A cell only ever gets one chance to be recorded scanned (once it leaves scanRadius unscanned,
    // it's not revisited unless the player physically returns), so a budget that spreads discovery
    // across multiple ticks can outright miss ground the player only passed through briefly (flying,
    // boating, a fast mount) - the cell enters and leaves scanRadius within a single tick and never
    // gets queried at all. Scanning everything in range immediately guarantees that can't happen. This
    // is safe to do unconditionally because the "once ever" model already bounds the worst case: a
    // genuinely large single-tick burst only happens on a first join, a teleport, or a ScanRadius
    // increase, and even then it's a one-time cost against ground that would otherwise need scanning
    // eventually anyway.
    //
    // The one deliberate exception: a cell is skipped (not scanned, not marked) for as long as
    // ScanGeometry.IsCellReady says its GameObjects haven't finished instantiating yet - see that
    // method for why this matters specifically on dedicated servers. This trades a small chance of
    // missing ground passed through VERY briefly while its objects are still mid-load (same class of
    // edge case as the fast-mount case above, just gated on load state instead of only on distance)
    // for correctness on the much more common case: a player exploring at normal speed, where the cell
    // simply gets picked up a tick or two later once ready, instead of being permanently recorded as
    // empty.
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

        // Physically scans every cell within scanRadius of playerPos that has never been scanned
        // before, and returns whatever matched in them. Already-scanned cells contribute nothing here
        // (their finds are already permanently recorded by the caller), so - unlike
        // SpatialCellScanner.ScanBatch - this only ever returns a delta of brand-new points, never the
        // full accumulated set.
        internal List<TrackedItem> ScanNewCells(Vector3 playerPos, float scanRadius)
        {
            List<ScanCellKey> newCells = ComputeUnscannedActiveCells(playerPos, scanRadius);
            if (newCells.Count == 0) return new List<TrackedItem>();

            List<TrackedItem> found = new List<TrackedItem>();

            foreach (var key in newCells)
            {
                // A cell whose objects haven't finished loading in yet (see ScanGeometry.IsCellReady -
                // notably on a dedicated server, where ZDOs sync over the network before their
                // GameObjects get instantiated) is deliberately left OUT of scannedCells rather than
                // scanned-and-marked here, so it's simply retried on a later tick once it's ready -
                // still within the same tick it entered range if already loaded, matching this
                // scanner's "every cell gets scanned the moment it's safe to" model rather than
                // silently recording an empty result forever.
                if (!ScanGeometry.IsCellReady(key, playerPos.y)) continue;

                scannedCells.Add(key);
                found.AddRange(ScanGeometry.ScanCell(key, playerPos.y, classify));
            }

            return found;
        }

        private List<ScanCellKey> ComputeUnscannedActiveCells(Vector3 playerPos, float scanRadius)
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
                    ScanCellKey key = new ScanCellKey(ix, iz);
                    if (scannedCells.Contains(key)) continue;

                    float centerX = ScanGeometry.GetCellCenter(ix);
                    float centerZ = ScanGeometry.GetCellCenter(iz);
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
