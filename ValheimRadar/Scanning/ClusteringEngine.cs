using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    public static class ClusteringEngine
    {
        // Clusters items from scratch with exactly the result of calling AddItem for each item in order,
        // but in roughly O(items) instead of AddItem's O(items * same-named items) - see ClusterGrid.
        public static List<ItemCluster> ClusterItems(List<TrackedItem> items, float maxDistance)
        {
            List<ItemCluster> clusters = new List<ItemCluster>();
            var grid = new ClusterGrid(maxDistance);

            foreach (var item in items)
            {
                ItemCluster cluster = grid.Add(item);
                if (cluster.Items.Count == 1) clusters.Add(cluster); // newly created - keeps creation order
            }

            return clusters;
        }

        // Adds a single item to whichever existing cluster it falls within (by DisplayName + distance
        // to that cluster's current centroid), or starts a new one. Order-dependent and non-optimal by
        // design (two clusters formed far apart in the insertion order never get merged after the fact
        // even if they end up close). This is the reference definition of the greedy rule; ClusterGrid
        // implements the same rule with an index and is what PinManager actually uses.
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

        internal static ItemCluster NewCluster(TrackedItem item, float maxDistance, bool standalone)
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
    }

    // An indexed, incrementally maintained set of clusters that applies exactly AddItem's greedy rule:
    // an item joins the earliest-created cluster of the same DisplayName whose centroid is within
    // maxDistance, else starts a new cluster. Removing items keeps the remaining clusters' relative
    // order, just like removing from AddItem's list does.
    //
    // AddItem walks every cluster and recomputes each same-named one's centroid from all its items,
    // which made reclustering O(points^2) per name - ~15 s (far more under Unity's Mono) for a
    // 38k-point world with 20k stones, long enough on connect for a dedicated server to drop the
    // client. Here each cluster keeps a running position sum (summed in the same order as
    // ItemCluster.GetCentroid, so centroids are bit-identical) and sits in a per-name grid bucket of
    // its current centroid. With cells at least maxDistance wide, any centroid within maxDistance of an
    // item is in the item's cell or one of its 8 neighbours.
    //
    // A second, coarse grid (ViewCellSize) answers "which clusters have their centroid inside this map
    // rectangle" for pin virtualization (see PinManager.Viewport.cs).
    public sealed class ClusterGrid
    {
        internal const float ViewCellSize = 256f;

        // Dictionary keys can't be null, but a null DisplayName must still only match other nulls.
        private const string NullNameKey = "\0null";

        private sealed class Entry
        {
            public ItemCluster Cluster;
            public long Order;
            public Vector3 Sum;
            public long MatchCell;
            public long ViewCell;
        }

        private readonly float cellSize;
        private readonly Dictionary<string, Dictionary<long, List<Entry>>> matchGrids = new Dictionary<string, Dictionary<long, List<Entry>>>();
        private readonly Dictionary<long, List<Entry>> viewGrid = new Dictionary<long, List<Entry>>();
        private readonly Dictionary<ItemCluster, Entry> entries = new Dictionary<ItemCluster, Entry>();
        private readonly Dictionary<TrackedItem, ItemCluster> clusterOfItem = new Dictionary<TrackedItem, ItemCluster>();
        private long nextOrder;

        public ClusterGrid(float maxDistance)
        {
            MaxDistance = maxDistance;
            cellSize = Mathf.Max(maxDistance, 1f);
        }

        public float MaxDistance { get; }

        public int Count => entries.Count;

        public bool Contains(ItemCluster cluster) => cluster != null && entries.ContainsKey(cluster);

        public ItemCluster ClusterOf(TrackedItem item) => item != null && clusterOfItem.TryGetValue(item, out ItemCluster cluster) ? cluster : null;

        // Same value as cluster.GetCentroid(), without re-summing its items.
        public Vector3 GetCentroid(ItemCluster cluster) =>
            entries.TryGetValue(cluster, out Entry entry) ? entry.Sum / cluster.Items.Count : cluster.GetCentroid();

        // Adds item (which must not already be in the grid) and returns the cluster it joined or started.
        public ItemCluster Add(TrackedItem item)
        {
            bool standalone = ObjectEvaluator.IsOreDepositCategory(item.CategoryKey);

            if (!standalone)
            {
                Dictionary<long, List<Entry>> grid = MatchGridFor(item.DisplayName);
                int cx = Cell(item.Position.x, cellSize);
                int cz = Cell(item.Position.z, cellSize);

                Entry best = null;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue(CellKey(cx + dx, cz + dz), out var bucket)) continue;
                        foreach (Entry entry in bucket)
                        {
                            if (best != null && entry.Order > best.Order) continue;
                            if (Vector3.Distance(entry.Sum / entry.Cluster.Items.Count, item.Position) <= MaxDistance) best = entry;
                        }
                    }
                }

                if (best != null)
                {
                    best.Cluster.Items.Add(item);
                    best.Sum += item.Position;
                    clusterOfItem[item] = best.Cluster;
                    Reindex(best);
                    return best.Cluster;
                }
            }

            ItemCluster created = ClusteringEngine.NewCluster(item, MaxDistance, standalone);
            var newEntry = new Entry { Cluster = created, Order = nextOrder++, Sum = Vector3.zero + item.Position };
            newEntry.MatchCell = MatchCellOf(newEntry);
            newEntry.ViewCell = ViewCellOf(newEntry);
            if (!standalone) AddToBucket(MatchGridFor(created.DisplayName), newEntry.MatchCell, newEntry);
            AddToBucket(viewGrid, newEntry.ViewCell, newEntry);
            entries[created] = newEntry;
            clusterOfItem[item] = created;
            return created;
        }

        // Takes item out of its cluster. Returns that cluster (now possibly empty, in which case it has
        // left the grid too), or null when item wasn't in the grid.
        public ItemCluster Remove(TrackedItem item)
        {
            if (item == null || !clusterOfItem.TryGetValue(item, out ItemCluster cluster)) return null;
            clusterOfItem.Remove(item);

            Entry entry = entries[cluster];
            cluster.Items.Remove(item);

            if (cluster.Items.Count == 0)
            {
                Unindex(entry);
                entries.Remove(cluster);
                return cluster;
            }

            // Re-summed rather than subtracted, so the centroid stays bit-identical to GetCentroid.
            Vector3 sum = Vector3.zero;
            foreach (TrackedItem member in cluster.Items) sum += member.Position;
            entry.Sum = sum;
            Reindex(entry);
            return cluster;
        }

        // Removes a whole cluster and all its items from the grid.
        public bool RemoveCluster(ItemCluster cluster)
        {
            if (cluster == null || !entries.TryGetValue(cluster, out Entry entry)) return false;

            foreach (TrackedItem member in cluster.Items) clusterOfItem.Remove(member);
            Unindex(entry);
            entries.Remove(cluster);
            return true;
        }

        // Adds every cluster whose centroid lies inside the x/z rectangle to results.
        public void Query(float minX, float minZ, float maxX, float maxZ, List<ItemCluster> results)
        {
            int x0 = Cell(minX, ViewCellSize), x1 = Cell(maxX, ViewCellSize);
            int z0 = Cell(minZ, ViewCellSize), z1 = Cell(maxZ, ViewCellSize);

            // A rectangle spanning more cells than exist (zoomed fully out) is cheaper to answer by
            // walking the occupied cells instead.
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > viewGrid.Count)
            {
                foreach (var bucket in viewGrid.Values) AddInside(bucket, minX, minZ, maxX, maxZ, results);
                return;
            }

            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (viewGrid.TryGetValue(CellKey(x, z), out var bucket)) AddInside(bucket, minX, minZ, maxX, maxZ, results);
                }
            }
        }

        private static void AddInside(List<Entry> bucket, float minX, float minZ, float maxX, float maxZ, List<ItemCluster> results)
        {
            foreach (Entry entry in bucket)
            {
                Vector3 c = entry.Sum / entry.Cluster.Items.Count;
                if (c.x >= minX && c.x <= maxX && c.z >= minZ && c.z <= maxZ) results.Add(entry.Cluster);
            }
        }

        private Dictionary<long, List<Entry>> MatchGridFor(string displayName)
        {
            string nameKey = displayName ?? NullNameKey;
            if (!matchGrids.TryGetValue(nameKey, out var grid))
            {
                grid = new Dictionary<long, List<Entry>>();
                matchGrids[nameKey] = grid;
            }

            return grid;
        }

        private long MatchCellOf(Entry entry)
        {
            Vector3 c = entry.Sum / entry.Cluster.Items.Count;
            return CellKey(Cell(c.x, cellSize), Cell(c.z, cellSize));
        }

        private static long ViewCellOf(Entry entry)
        {
            Vector3 c = entry.Sum / entry.Cluster.Items.Count;
            return CellKey(Cell(c.x, ViewCellSize), Cell(c.z, ViewCellSize));
        }

        // Moves entry to the buckets of its (possibly changed) centroid.
        private void Reindex(Entry entry)
        {
            if (!entry.Cluster.Standalone)
            {
                long matchCell = MatchCellOf(entry);
                if (matchCell != entry.MatchCell)
                {
                    var grid = MatchGridFor(entry.Cluster.DisplayName);
                    RemoveFromBucket(grid, entry.MatchCell, entry);
                    entry.MatchCell = matchCell;
                    AddToBucket(grid, matchCell, entry);
                }
            }

            long viewCell = ViewCellOf(entry);
            if (viewCell != entry.ViewCell)
            {
                RemoveFromBucket(viewGrid, entry.ViewCell, entry);
                entry.ViewCell = viewCell;
                AddToBucket(viewGrid, viewCell, entry);
            }
        }

        private void Unindex(Entry entry)
        {
            if (!entry.Cluster.Standalone) RemoveFromBucket(MatchGridFor(entry.Cluster.DisplayName), entry.MatchCell, entry);
            RemoveFromBucket(viewGrid, entry.ViewCell, entry);
        }

        private static int Cell(float v, float size) => Mathf.FloorToInt(v / size);

        private static long CellKey(int x, int z) => ((long)x << 32) | (uint)z;

        private static void AddToBucket(Dictionary<long, List<Entry>> grid, long cellKey, Entry entry)
        {
            if (!grid.TryGetValue(cellKey, out var bucket))
            {
                bucket = new List<Entry>();
                grid[cellKey] = bucket;
            }

            bucket.Add(entry);
        }

        private static void RemoveFromBucket(Dictionary<long, List<Entry>> grid, long cellKey, Entry entry)
        {
            if (!grid.TryGetValue(cellKey, out var bucket)) return;
            bucket.Remove(entry);
            if (bucket.Count == 0) grid.Remove(cellKey);
        }
    }
}
