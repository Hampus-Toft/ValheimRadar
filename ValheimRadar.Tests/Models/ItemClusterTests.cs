using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Models
{
    public class ItemClusterTests
    {
        private static TrackedItem Item(Vector3 position) => new TrackedItem { Position = position };

        [Fact]
        public void GetCentroid_EmptyItems_ReturnsZeroWithoutThrowing()
        {
            var cluster = new ItemCluster();

            Assert.Equal(Vector3.zero, cluster.GetCentroid());
        }

        [Fact]
        public void GetCentroid_SingleItem_ReturnsItsPosition()
        {
            var cluster = new ItemCluster();
            cluster.Items.Add(Item(new Vector3(3, 4, 5)));

            Assert.Equal(new Vector3(3, 4, 5), cluster.GetCentroid());
        }

        [Fact]
        public void GetCentroid_MultipleItems_ReturnsAverage()
        {
            var cluster = new ItemCluster();
            cluster.Items.Add(Item(new Vector3(0, 0, 0)));
            cluster.Items.Add(Item(new Vector3(10, 0, 0)));

            Assert.Equal(new Vector3(5, 0, 0), cluster.GetCentroid());
        }

        [Fact]
        public void GetLabel_SingleItem_ReturnsBareDisplayName()
        {
            var cluster = new ItemCluster { DisplayName = "Dandelion" };
            cluster.Items.Add(Item(Vector3.zero));

            Assert.Equal("Dandelion", cluster.GetLabel());
        }

        [Fact]
        public void GetLabel_MultipleItems_ReturnsCountPrefixedLabel()
        {
            var cluster = new ItemCluster { DisplayName = "Dandelion" };
            cluster.Items.Add(Item(Vector3.zero));
            cluster.Items.Add(Item(Vector3.zero));
            cluster.Items.Add(Item(Vector3.zero));

            Assert.Equal("3x Dandelion", cluster.GetLabel());
        }

        [Fact]
        public void GetClusterKey_EmptyItems_ReturnsEmptyString()
        {
            var cluster = new ItemCluster();

            Assert.Equal(string.Empty, cluster.GetClusterKey());
        }

        [Fact]
        public void GetClusterKey_SameGridCell_ProducesSameKey()
        {
            var a = new ItemCluster { DisplayName = "Copper", MaxDistance = 10f };
            a.Items.Add(Item(new Vector3(1, 0, 1)));

            var b = new ItemCluster { DisplayName = "Copper", MaxDistance = 10f };
            b.Items.Add(Item(new Vector3(2, 0, 2)));

            Assert.Equal(a.GetClusterKey(), b.GetClusterKey());
        }

        [Fact]
        public void GetClusterKey_AcrossGridBoundary_ProducesDifferentKey()
        {
            // MaxDistance = 10 -> grid cell width 10. x=4 rounds to grid cell 0, x=6 rounds to grid
            // cell 1 - this boundary is exactly what SyncPersistentClusters' stale-key eviction
            // depends on to notice a cluster's centroid drifted into a new cell.
            var a = new ItemCluster { DisplayName = "Copper", MaxDistance = 10f };
            a.Items.Add(Item(new Vector3(4, 0, 0)));

            var b = new ItemCluster { DisplayName = "Copper", MaxDistance = 10f };
            b.Items.Add(Item(new Vector3(6, 0, 0)));

            Assert.NotEqual(a.GetClusterKey(), b.GetClusterKey());
        }

        [Fact]
        public void GetClusterKey_NonPositiveMaxDistance_FallsBackToGridSizeOne()
        {
            var withZero = new ItemCluster { DisplayName = "Copper", MaxDistance = 0f };
            withZero.Items.Add(Item(new Vector3(0.6f, 0, 0)));

            var withOne = new ItemCluster { DisplayName = "Copper", MaxDistance = 1f };
            withOne.Items.Add(Item(new Vector3(0.6f, 0, 0)));

            Assert.Equal(withOne.GetClusterKey(), withZero.GetClusterKey());
        }
    }
}
