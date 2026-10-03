using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    public static class ClusteringEngine
    {
        // Clusters items from scratch with exactly the result of calling AddItem for each item in order,
        // but without AddItem's cost: AddItem walks every cluster and recomputes each same-named one's
        // centroid from all its items, which made a full rebuild O(points^2) per name - ~15 s (and far
        // more under Unity's Mono) for a 38k-point world with 20k stones, long enough on connect for a
        // dedicated server to drop the client. Here each cluster keeps a running position sum (summed in
        // the same order as GetCentroid, so centroids are bit-identical) and sits in a per-name grid
        // bucket of its current centroid. With cells at least maxDistance wide, any centroid within
        // maxDistance of an item is in the item's cell or one of its 8 neighbours, and picking the
        // lowest creation order among those matches keeps AddItem's first-match-in-list-order rule.
        public static List<ItemCluster> ClusterItems(List<TrackedItem> items, float maxDistance)
        {
            List<ItemCluster> clusters = new List<ItemCluster>();
            float cellSize = Mathf.Max(maxDistance, 1f);
            var grids = new Dictionary<string, Dictionary<long, List<GridEntry>>>();

            foreach (var item in items)
            {
                bool standalone = ObjectEvaluator.IsOreDepositCategory(item.CategoryKey);
                string nameKey = item.DisplayName ?? NullNameKey;
                int cx = Cell(item.Position.x, cellSize);
                int cz = Cell(item.Position.z, cellSize);

                if (!standalone)
                {
                    if (!grids.TryGetValue(nameKey, out var grid))
                    {
                        grid = new Dictionary<long, List<GridEntry>>();
                        grids[nameKey] = grid;
                    }

                    GridEntry best = null;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!grid.TryGetValue(CellKey(cx + dx, cz + dz), out var bucket)) continue;
                            foreach (GridEntry entry in bucket)
                            {
                                if (best != null && entry.Order > best.Order) continue;
                                if (Vector3.Distance(entry.Sum / entry.Cluster.Items.Count, item.Position) <= maxDistance) best = entry;
                            }
                        }
                    }

                    if (best != null)
                    {
                        best.Cluster.Items.Add(item);
                        best.Sum += item.Position;

                        Vector3 centroid = best.Sum / best.Cluster.Items.Count;
                        long newCell = CellKey(Cell(centroid.x, cellSize), Cell(centroid.z, cellSize));
                        if (newCell != best.CellKey)
                        {
                            grid[best.CellKey].Remove(best);
                            AddToBucket(grid, newCell, best);
                        }

                        continue;
                    }

                    ItemCluster created = NewCluster(item, maxDistance, standalone: false);
                    clusters.Add(created);
                    AddToBucket(grid, CellKey(cx, cz), new GridEntry { Cluster = created, Order = clusters.Count - 1, Sum = Vector3.zero + item.Position });
                    continue;
                }

                clusters.Add(NewCluster(item, maxDistance, standalone: true));
            }

            return clusters;
        }

        // Dictionary keys can't be null, but a null DisplayName must still only match other nulls.
        private const string NullNameKey = "\0null";

        private sealed class GridEntry
        {
            public ItemCluster Cluster;
            public int Order;
            public Vector3 Sum;
            public long CellKey;
        }

        private static int Cell(float v, float cellSize) => Mathf.FloorToInt(v / cellSize);

        private static long CellKey(int x, int z) => ((long)x << 32) | (uint)z;

        private static void AddToBucket(Dictionary<long, List<GridEntry>> grid, long cellKey, GridEntry entry)
        {
            entry.CellKey = cellKey;
            if (!grid.TryGetValue(cellKey, out var bucket))
            {
                bucket = new List<GridEntry>();
                grid[cellKey] = bucket;
            }

            bucket.Add(entry);
        }

        private static ItemCluster NewCluster(TrackedItem item, float maxDistance, bool standalone)
        {
            ItemCluster cluster = new ItemCluster
            {
                DisplayName = item.DisplayName,
                Icon = item.Icon,
                IsPersistent = item.IsPersistent,
                CategoryKey = item.CategoryKey,
                RawName = item.RawName,
                MaxDistance = maxDistance,
                Standalone = standalone
            };
            cluster.Items.Add(item);
            return cluster;
        }

        // Adds a single item to whichever existing cluster it falls within (by DisplayName + distance
        // to that cluster's current centroid), or starts a new one - the same greedy rule ClusterItems
        // applies per item, exposed separately so PinManager can fold newly-discovered persistent
        // points into an already-built cluster list without reclustering everything from scratch (see
        // PinManager.RecordRawPoints). Order-dependent and non-optimal by design (two clusters formed
        // far apart in the insertion order never get merged after the fact even if they end up close),
        // but that's the exact behavior ClusterItems already had - preserving it here means incremental
        // and from-scratch clustering agree as long as items are processed in the same order.
        //
        // Ore deposits (ObjectEvaluator.IsOreDepositCategory) never join or accept others: each one
        // always becomes its own Standalone single-item cluster, i.e. its own pin.
        public static ItemCluster AddItem(List<ItemCluster> clusters, TrackedItem item, float maxDistance)
        {
            bool standalone = ObjectEvaluator.IsOreDepositCategory(item.CategoryKey);

            if (!standalone)
            {
                foreach (var cluster in clusters)
                {
                    if (cluster.Standalone) continue;

                    if (cluster.DisplayName == item.DisplayName && Vector3.Distance(cluster.GetCentroid(), item.Position) <= maxDistance)
                    {
                        cluster.Items.Add(item);
                        return cluster;
                    }
                }
            }

            ItemCluster newCluster = NewCluster(item, maxDistance, standalone);
            clusters.Add(newCluster);
            return newCluster;
        }
    }
}
