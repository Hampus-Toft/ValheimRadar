using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Scanning
{
    public class ClusteringEngineTests
    {
        private static TrackedItem Item(string displayName, Vector3 position) => new TrackedItem
        {
            DisplayName = displayName,
            Position = position
        };

        [Fact]
        public void ClusterItems_EmptyList_ReturnsEmptyList()
        {
            var result = ClusteringEngine.ClusterItems(new List<TrackedItem>(), maxDistance: 10f);

            Assert.Empty(result);
        }

        [Fact]
        public void ClusterItems_SameNameWithinDistance_MergeIntoOneCluster()
        {
            var items = new List<TrackedItem>
            {
                Item("Dandelion", new Vector3(0, 0, 0)),
                Item("Dandelion", new Vector3(2, 0, 0))
            };

            var result = ClusteringEngine.ClusterItems(items, maxDistance: 5f);

            Assert.Single(result);
            Assert.Equal(2, result[0].Items.Count);
        }

        [Fact]
        public void ClusterItems_SameNameBeyondDistance_FormsTwoClusters()
        {
            var items = new List<TrackedItem>
            {
                Item("Dandelion", new Vector3(0, 0, 0)),
                Item("Dandelion", new Vector3(20, 0, 0))
            };

            var result = ClusteringEngine.ClusterItems(items, maxDistance: 5f);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void ClusterItems_DifferentNamesSamePosition_NeverMerge()
        {
            var items = new List<TrackedItem>
            {
                Item("Dandelion", new Vector3(0, 0, 0)),
                Item("Thistle", new Vector3(0, 0, 0))
            };

            var result = ClusteringEngine.ClusterItems(items, maxDistance: 5f);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void AddItem_Incremental_MatchesFromScratchClustering()
        {
            // This is the exact invariant PinManager.RecordRawPoints/RebuildPersistentClusters
            // depend on: folding points in one at a time must agree with clustering the same set
            // from scratch, as long as both process items in the same order.
            var items = new List<TrackedItem>
            {
                Item("Copper", new Vector3(0, 0, 0)),
                Item("Copper", new Vector3(3, 0, 0)),
                Item("Copper", new Vector3(30, 0, 0)),
                Item("Tin", new Vector3(0, 0, 0))
            };

            var fromScratch = ClusteringEngine.ClusterItems(items, maxDistance: 5f);

            var incremental = new List<ItemCluster>();
            foreach (var item in items)
            {
                ClusteringEngine.AddItem(incremental, item, 5f);
            }

            Assert.Equal(fromScratch.Count, incremental.Count);
            for (int i = 0; i < fromScratch.Count; i++)
            {
                Assert.Equal(fromScratch[i].DisplayName, incremental[i].DisplayName);
                Assert.Equal(fromScratch[i].Items.Count, incremental[i].Items.Count);
            }
        }

        // ClusterItems uses a grid + running sums instead of calling AddItem per item; it must still
        // produce exactly the same clusters, in the same order, with the same members.
        [Theory]
        [InlineData(1, 5f)]
        [InlineData(2, 15f)]
        [InlineData(3, 1f)]
        [InlineData(4, 50f)]
        public void ClusterItems_MatchesAddItemLoop_OnDenseRandomData(int seed, float maxDistance)
        {
            var rng = new System.Random(seed);
            string[] names = { "Stone", "Branch", "Dandelion", null };
            var items = new List<TrackedItem>();
            for (int i = 0; i < 3000; i++)
            {
                bool deposit = rng.Next(20) == 0;
                items.Add(new TrackedItem
                {
                    DisplayName = deposit ? "Silver" : names[rng.Next(names.Length)],
                    CategoryKey = deposit ? "resource:SilverDeposit" : null,
                    Position = new Vector3((float)(rng.NextDouble() * 400 - 200), (float)(rng.NextDouble() * 20), (float)(rng.NextDouble() * 400 - 200))
                });
            }

            var expected = new List<ItemCluster>();
            foreach (var item in items) ClusteringEngine.AddItem(expected, item, maxDistance);

            var actual = ClusteringEngine.ClusterItems(items, maxDistance);

            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Standalone, actual[i].Standalone);
                Assert.Equal(expected[i].Items, actual[i].Items);
                Assert.Equal(expected[i].GetClusterKey(), actual[i].GetClusterKey());
            }
        }

        [Fact]
        public void AddItem_GreedyInsertionOrder_AssignsToFirstMatchingClusterInListOrder()
        {
            // maxDistance = 5. Cluster A forms at x=0. Cluster B forms at x=8 (8 > 5 from A, so it
            // can't join A). A later point at x=4 is within 5 of BOTH clusters' centroids - but
            // because clusters are checked in list order and A was created first, it always joins
            // A, never B. This is the order-dependent, non-optimal-by-design behavior documented on
            // AddItem - locking it in as a regression test so a future "optimization" doesn't
            // silently start re-clustering after the fact.
            var clusters = new List<ItemCluster>();
            ClusteringEngine.AddItem(clusters, Item("Stone", new Vector3(0, 0, 0)), 5f);
            ClusteringEngine.AddItem(clusters, Item("Stone", new Vector3(8, 0, 0)), 5f);
            ClusteringEngine.AddItem(clusters, Item("Stone", new Vector3(4, 0, 0)), 5f);

            Assert.Equal(2, clusters.Count);
            Assert.Equal(2, clusters[0].Items.Count); // joined cluster A
            Assert.Single(clusters[1].Items);          // cluster B untouched
        }

        private static TrackedItem Deposit(string categoryKey, string displayName, Vector3 position) => new TrackedItem
        {
            CategoryKey = categoryKey,
            DisplayName = displayName,
            Position = position
        };

        // Ore deposits are never grouped: each is its own pin, even right next to another.
        [Fact]
        public void ClusterItems_OreDepositsNearEachOther_EachGetOwnCluster()
        {
            var items = new List<TrackedItem>
            {
                Deposit("resource:SilverDeposit", "Silver", new Vector3(0, 0, 0)),
                Deposit("resource:SilverDeposit", "Silver", new Vector3(1, 0, 0)),
                Deposit("resource:SilverDeposit", "Silver", new Vector3(0, 0, 2))
            };

            var result = ClusteringEngine.ClusterItems(items, maxDistance: 15f);

            Assert.Equal(3, result.Count);
            Assert.All(result, c => Assert.True(c.Standalone));
            Assert.All(result, c => Assert.Single(c.Items));
        }

        // The ClusterDistance grid key would put deposits a few meters apart on the same key (one pin).
        [Fact]
        public void ClusterItems_OreDepositsNearEachOther_HaveDistinctKeys()
        {
            var items = new List<TrackedItem>
            {
                Deposit("resource:CopperDeposit", "Copper", new Vector3(100, 0, 100)),
                Deposit("resource:CopperDeposit", "Copper", new Vector3(101, 0, 100))
            };

            var result = ClusteringEngine.ClusterItems(items, maxDistance: 15f);

            Assert.NotEqual(result[0].GetClusterKey(), result[1].GetClusterKey());
        }

        [Fact]
        public void AddItem_NonDepositWithSameName_NeverJoinsADepositCluster()
        {
            var clusters = new List<ItemCluster>();
            ClusteringEngine.AddItem(clusters, Deposit("resource:CopperDeposit", "Copper", Vector3.zero), 15f);
            ClusteringEngine.AddItem(clusters, Item("Copper", new Vector3(1, 0, 0)), 15f);

            Assert.Equal(2, clusters.Count);
            Assert.Single(clusters[0].Items);
        }

        [Fact]
        public void ClusterItems_NonDepositResources_StillCluster()
        {
            var items = new List<TrackedItem>
            {
                Deposit("resource:Flint", "Flint", new Vector3(0, 0, 0)),
                Deposit("resource:Flint", "Flint", new Vector3(2, 0, 0))
            };

            var result = ClusteringEngine.ClusterItems(items, maxDistance: 15f);

            Assert.Single(result);
            Assert.False(result[0].Standalone);
        }
    }
}
