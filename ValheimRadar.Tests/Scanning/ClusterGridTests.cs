using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Scanning
{
    public class ClusterGridTests
    {
        private static List<TrackedItem> RandomItems(System.Random rng, int count, float spread)
        {
            string[] names = { "Stone", "Branch", "Dandelion", null };
            var items = new List<TrackedItem>();
            for (int i = 0; i < count; i++)
            {
                bool deposit = rng.Next(20) == 0;
                items.Add(new TrackedItem
                {
                    DisplayName = deposit ? "Silver" : names[rng.Next(names.Length)],
                    CategoryKey = deposit ? "resource:SilverDeposit" : null,
                    Position = new Vector3((float)(rng.NextDouble() * spread * 2 - spread), (float)(rng.NextDouble() * 20), (float)(rng.NextDouble() * spread * 2 - spread))
                });
            }

            return items;
        }

        // The grid must behave exactly like the reference list-based rule (ClusteringEngine.AddItem, and
        // removing a point from whichever cluster holds it, dropping emptied clusters) under any
        // interleaving of adds and removes - that's what keeps live scans, loads, mining and regrowth
        // agreeing with each other.
        [Theory]
        [InlineData(1, 5f)]
        [InlineData(2, 15f)]
        [InlineData(3, 50f)]
        public void AddAndRemove_MatchReferenceListClustering(int seed, float maxDistance)
        {
            var rng = new System.Random(seed);
            var pool = RandomItems(rng, 2000, 150f);

            var reference = new List<ItemCluster>();
            var grid = new ClusterGrid(maxDistance);
            var inside = new List<TrackedItem>();
            var outside = new List<TrackedItem>(pool);

            for (int step = 0; step < 6000; step++)
            {
                bool add = outside.Count > 0 && (inside.Count == 0 || rng.Next(3) != 0);
                if (add)
                {
                    int i = rng.Next(outside.Count);
                    TrackedItem item = outside[i];
                    outside.RemoveAt(i);
                    inside.Add(item);

                    ItemCluster expected = ClusteringEngine.AddItem(reference, item, maxDistance);
                    ItemCluster actual = grid.Add(item);
                    Assert.Equal(expected.Items, actual.Items);
                }
                else
                {
                    int i = rng.Next(inside.Count);
                    TrackedItem item = inside[i];
                    inside.RemoveAt(i);
                    outside.Add(item);

                    ItemCluster holder = reference.First(c => c.Items.Contains(item));
                    holder.Items.Remove(item);
                    if (holder.Items.Count == 0) reference.Remove(holder);

                    ItemCluster actual = grid.Remove(item);
                    Assert.NotNull(actual);
                    Assert.Equal(holder.Items, actual.Items);
                    Assert.Equal(holder.Items.Count > 0, grid.Contains(actual));
                }
            }

            Assert.Equal(reference.Count, grid.Count);
            foreach (ItemCluster expected in reference)
            {
                ItemCluster actual = grid.ClusterOf(expected.Items[0]);
                Assert.Equal(expected.Items, actual.Items);
                Assert.Equal(expected.GetCentroid(), grid.GetCentroid(actual));
            }
        }

        [Fact]
        public void Remove_UnknownItem_ReturnsNull()
        {
            var grid = new ClusterGrid(10f);
            Assert.Null(grid.Remove(new TrackedItem { DisplayName = "Stone" }));
        }

        [Fact]
        public void RemoveCluster_DropsAllItsItems()
        {
            var grid = new ClusterGrid(10f);
            var a = new TrackedItem { DisplayName = "Stone", Position = new Vector3(0, 0, 0) };
            var b = new TrackedItem { DisplayName = "Stone", Position = new Vector3(3, 0, 0) };
            ItemCluster cluster = grid.Add(a);
            grid.Add(b);

            Assert.True(grid.RemoveCluster(cluster));

            Assert.Equal(0, grid.Count);
            Assert.Null(grid.ClusterOf(a));
            Assert.Null(grid.ClusterOf(b));

            // Both points can be clustered again afterwards.
            Assert.Single(grid.Add(a).Items);
        }

        [Theory]
        [InlineData(-100f, -100f, 100f, 100f)]
        [InlineData(-3000f, -3000f, 3000f, 3000f)]
        [InlineData(10f, -40f, 270f, 600f)]
        [InlineData(-1f, -1f, 1f, 1f)]
        public void Query_ReturnsExactlyTheClustersWithCentroidInside(float minX, float minZ, float maxX, float maxZ)
        {
            var rng = new System.Random(7);
            var grid = new ClusterGrid(15f);
            var clusters = new HashSet<ItemCluster>();
            foreach (TrackedItem item in RandomItems(rng, 3000, 1500f)) clusters.Add(grid.Add(item));

            var results = new List<ItemCluster>();
            grid.Query(minX, minZ, maxX, maxZ, results);

            var expected = clusters.Where(c =>
            {
                Vector3 p = grid.GetCentroid(c);
                return p.x >= minX && p.x <= maxX && p.z >= minZ && p.z <= maxZ;
            }).ToHashSet();

            Assert.Equal(expected.Count, results.Count);
            Assert.True(expected.SetEquals(results));
        }

        [Fact]
        public void Query_FollowsClustersAsTheirCentroidMoves()
        {
            var grid = new ClusterGrid(50f);
            var first = new TrackedItem { DisplayName = "Stone", Position = new Vector3(250f, 0, 0) };
            var second = new TrackedItem { DisplayName = "Stone", Position = new Vector3(290f, 0, 0) };
            ItemCluster cluster = grid.Add(first);
            grid.Add(second); // centroid 270, crossing the 256 m view cell boundary

            var results = new List<ItemCluster>();
            grid.Query(260f, -10f, 280f, 10f, results);
            Assert.Equal(new[] { cluster }, results);

            grid.Remove(second); // back to 250
            results.Clear();
            grid.Query(260f, -10f, 280f, 10f, results);
            Assert.Empty(results);
        }
    }
}
