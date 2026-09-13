using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Splits a full-radius scan into spatial batches so a large ScanRadius doesn't force one huge
    // Physics query (and all the ZNetView/ObjectEvaluator work behind it) into a single frame. The
    // area around the player is divided into a fixed-size grid; each ScanBatch call only re-queries a
    // slice of the currently in-range cells (a handful of Physics.OverlapBox calls instead of one
    // Physics.OverlapSphere spanning the whole radius), cycling through the remaining cells over
    // subsequent ticks. Cells not re-scanned this tick are served from cache, so a given cell's data
    // is at most one full rotation (batchCount ticks) stale - the tradeoff that keeps the per-tick
    // cost to roughly 1/batchCount of a full scan instead of the whole radius every tick.
    public static class BatchScanner
    {
        // Fixed rather than derived from ScanRadius: per-tick scanned AREA already scales down with
        // batchCount regardless of cell size (cellsPerBatch * cellArea == totalArea / batchCount), so
        // cell size only trades off Physics call COUNT vs call SIZE, not the scan budget itself. 40m
        // keeps that count reasonable across the whole 10-300m ScanRadius range.
        private const float CellSize = 40f;

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly int X;
            public readonly int Z;

            public CellKey(int x, int z)
            {
                X = x;
                Z = z;
            }

            public bool Equals(CellKey other) => X == other.X && Z == other.Z;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);
            public override int GetHashCode() => (X * 397) ^ Z;
        }

        private static readonly Dictionary<CellKey, List<TrackedItem>> cellCache = new Dictionary<CellKey, List<TrackedItem>>();
        private static readonly LinkedList<CellKey> rotation = new LinkedList<CellKey>();
        private static readonly Dictionary<CellKey, LinkedListNode<CellKey>> rotationNodes = new Dictionary<CellKey, LinkedListNode<CellKey>>();

        // Drops all cached cell data and rotation state. Called on disconnect/world unload (mirrors
        // PinManager.ClearAllPins) and on relevant config changes, so stale positions from one
        // world/setting never leak into the next.
        public static void Reset()
        {
            cellCache.Clear();
            rotation.Clear();
            rotationNodes.Clear();
        }

        // Re-scans a rotating slice of the cells currently within scanRadius of playerPos and returns
        // the combined (cached + freshly-scanned) items for every cell still in range. batchCount is
        // the target number of ticks a full rotation should take; the actual per-tick slice is
        // recomputed every call from the current cell count so it adapts immediately to ScanRadius or
        // player-movement changes without needing a reset.
        public static List<TrackedItem> ScanBatch(Vector3 playerPos, float scanRadius, int batchCount)
        {
            HashSet<CellKey> activeCells = ComputeActiveCells(playerPos, scanRadius);

            PruneInactiveCells(activeCells);
            AddNewCells(activeCells);

            int cellsPerBatch = Math.Max(1, (int)Math.Ceiling(rotation.Count / (double)Math.Max(1, batchCount)));

            for (int i = 0; i < cellsPerBatch && rotation.Count > 0; i++)
            {
                CellKey key = rotation.First.Value;
                rotation.RemoveFirst();

                cellCache[key] = ScanCell(key, playerPos.y);

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

        private static HashSet<CellKey> ComputeActiveCells(Vector3 playerPos, float scanRadius)
        {
            HashSet<CellKey> result = new HashSet<CellKey>();

            int minX = Mathf.FloorToInt((playerPos.x - scanRadius) / CellSize);
            int maxX = Mathf.FloorToInt((playerPos.x + scanRadius) / CellSize);
            int minZ = Mathf.FloorToInt((playerPos.z - scanRadius) / CellSize);
            int maxZ = Mathf.FloorToInt((playerPos.z + scanRadius) / CellSize);

            // Half-diagonal padding so a cell whose center falls just outside the radius, but that
            // still partially overlaps the scan circle, isn't skipped entirely.
            float inclusionRadius = scanRadius + (CellSize * 0.70711f);
            float inclusionRadiusSq = inclusionRadius * inclusionRadius;

            for (int ix = minX; ix <= maxX; ix++)
            {
                for (int iz = minZ; iz <= maxZ; iz++)
                {
                    float centerX = (ix + 0.5f) * CellSize;
                    float centerZ = (iz + 0.5f) * CellSize;
                    float dx = centerX - playerPos.x;
                    float dz = centerZ - playerPos.z;

                    if ((dx * dx) + (dz * dz) <= inclusionRadiusSq)
                    {
                        result.Add(new CellKey(ix, iz));
                    }
                }
            }

            return result;
        }

        private static void PruneInactiveCells(HashSet<CellKey> activeCells)
        {
            List<CellKey> toRemove = null;
            foreach (var key in rotationNodes.Keys)
            {
                if (activeCells.Contains(key)) continue;
                (toRemove ?? (toRemove = new List<CellKey>())).Add(key);
            }

            if (toRemove == null) return;

            foreach (var key in toRemove)
            {
                rotation.Remove(rotationNodes[key]);
                rotationNodes.Remove(key);
                cellCache.Remove(key);
            }
        }

        private static void AddNewCells(HashSet<CellKey> activeCells)
        {
            foreach (var key in activeCells)
            {
                if (rotationNodes.ContainsKey(key)) continue;

                // Newly in-range cells are queued at the FRONT so freshly explored ground populates
                // within the next batch or two, instead of waiting behind cells that were already due.
                rotationNodes[key] = rotation.AddFirst(key);
                cellCache[key] = new List<TrackedItem>();
            }
        }

        // Vertical half-extent of a scan cell's collision box, independent of ScanRadius. ScanRadius
        // is a horizontal (XZ) distance, so reusing it for box height meant a 300m ScanRadius produced
        // a 600m-tall query column per cell - capturing every collider from deep underground to high
        // above the skybox, layer-unfiltered. A fixed vertical range comfortably covers Valheim's real
        // terrain variance (caves through mountain peaks) around the player without that blowup.
        private const float CellHeight = 150f;

        private static List<TrackedItem> ScanCell(CellKey key, float playerY)
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

                if (ObjectEvaluator.ShouldPinGameObject(netView.gameObject, rawName, out string displayName, out Sprite icon, out bool isPersistent, out string categoryKey))
                {
                    items.Add(new TrackedItem
                    {
                        Zdoid = zdoid,
                        Position = netView.transform.position,
                        RawName = rawName,
                        DisplayName = displayName,
                        Icon = icon,
                        IsPersistent = isPersistent,
                        CategoryKey = categoryKey
                    });
                }
            }

            return items;
        }
    }
}
