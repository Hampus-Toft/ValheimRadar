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

        [Theory]
        [InlineData("Boar", 1, "Boar")]
        [InlineData("Boar", 3, "3x Boar")]
        [InlineData("Boar (★★)", 1, "Boar (★★)")]
        [InlineData("Boar (★★)", 2, "2x Boar (★★)")]
        [InlineData(null, 1, "")]
        public void BuildLabel_NamesShown_MatchesHistoricLabel(string displayName, int count, string expected)
        {
            Assert.Equal(expected, ItemCluster.BuildLabel(displayName, count, hideName: false));
        }

        [Theory]
        [InlineData("Boar", 1, "")]
        [InlineData("Boar", 2, "2x")]
        [InlineData("Boar (★★)", 1, "★★")]
        [InlineData("Boar (★★)", 2, "2x ★★")]
        [InlineData("Greydwarf Shaman (★)", 5, "5x ★")]
        [InlineData("Greydwarf Brute", 1, "")]
        public void BuildLabel_NamesHidden_KeepsCountAndStarsOnly(string displayName, int count, string expected)
        {
            Assert.Equal(expected, ItemCluster.BuildLabel(displayName, count, hideName: true));
        }

        [Fact]
        public void GetLabel_HideNameOverload_UsesItemCount()
        {
            var cluster = new ItemCluster { DisplayName = "Wolf (★★)" };
            cluster.Items.Add(Item(Vector3.zero));
            cluster.Items.Add(Item(Vector3.zero));

            Assert.Equal("2x ★★", cluster.GetLabel(hideName: true));
            Assert.Equal("2x Wolf (★★)", cluster.GetLabel(hideName: false));
        }

        [Theory]
        [InlineData("Boar (★★)", true, "Boar", "★★")]
        [InlineData("Boar", false, "Boar", "")]
        [InlineData("Boar ()", false, "Boar ()", "")]
        [InlineData("Boar (Elite)", false, "Boar (Elite)", "")]
        [InlineData("(★)", false, "(★)", "")]
        [InlineData("", false, "", "")]
        public void TrySplitStarSuffix_OnlySplitsStarSuffix(string displayName, bool expectedResult, string expectedBase, string expectedStars)
        {
            bool result = ItemCluster.TrySplitStarSuffix(displayName, out string baseName, out string stars);

            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedBase, baseName);
            Assert.Equal(expectedStars, stars);
        }

        [Theory]
        [InlineData("creature:boar", true, false, true)]
        [InlineData("creature:fish_perch", true, false, true)]
        [InlineData("creature:boar", false, false, false)]      // no icon -> keep name
        [InlineData("creature:boar", true, true, false)]        // user opted in to names
        [InlineData("creature:_monster", true, false, false)]   // generic fallback icon -> keep name
        [InlineData("creature:_animal", true, false, false)]
        [InlineData("resource:Dandelion", true, false, false)]  // never touch resources
        [InlineData("location:crypt", true, false, false)]
        [InlineData(null, true, false, false)]
        [InlineData("", true, false, false)]
        public void ShouldHideCreatureName_OnlyForIdentifiableCreaturePins(string categoryKey, bool hasIcon, bool showNames, bool expected)
        {
            Assert.Equal(expected, ItemCluster.ShouldHideCreatureName(categoryKey, hasIcon, showNames));
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
