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
    }
}
