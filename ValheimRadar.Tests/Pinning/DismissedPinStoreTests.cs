using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    public class DismissedPinStoreTests
    {
        [Fact]
        public void Contains_EmptyStore_ReturnsFalse()
        {
            Assert.False(new DismissedPinStore().Contains("resource:Copper", Vector3.zero));
        }

        [Fact]
        public void Contains_SameCategoryAndPosition_ReturnsTrue()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10, 5, 20));

            Assert.True(store.Contains("resource:Copper", new Vector3(10, 5, 20)));
        }

        [Fact]
        public void Contains_WithinMatchRadius_ReturnsTrue()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10, 5, 20));

            Assert.True(store.Contains("resource:Copper", new Vector3(10.2f, 99, 20.2f)));
        }

        [Fact]
        public void Contains_BeyondMatchRadius_ReturnsFalse()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10, 5, 20));

            Assert.False(store.Contains("resource:Copper", new Vector3(11, 5, 20)));
        }

        [Fact]
        public void Contains_SamePositionDifferentCategory_ReturnsFalse()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10, 5, 20));

            Assert.False(store.Contains("resource:Tin", new Vector3(10, 5, 20)));
        }

        [Fact]
        public void Add_SamePointTwice_CountsOnce()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10, 5, 20));
            store.Add("resource:Copper", new Vector3(10.1f, 5, 20));

            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Clear_RemovesEverything()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10, 5, 20));
            store.Clear();

            Assert.Equal(0, store.Count);
            Assert.False(store.Contains("resource:Copper", new Vector3(10, 5, 20)));
        }

        [Fact]
        public void SerializeThenLoad_RoundTripsPointsAndCategoryKeys()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(10.5f, 5.25f, -20.75f));
            store.Add("location:crypt|weird", new Vector3(-300, 12, 4000));

            var restored = new DismissedPinStore();
            restored.Load(store.Serialize());

            Assert.Equal(2, restored.Count);
            Assert.True(restored.Contains("resource:Copper", new Vector3(10.5f, 5.25f, -20.75f)));
            Assert.True(restored.Contains("location:crypt|weird", new Vector3(-300, 12, 4000)));
        }

        [Fact]
        public void Load_ReplacesExistingContent()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", new Vector3(1, 1, 1));

            store.Load(new List<string> { "#dismissedv1", "resource%3ATin|2|2|2" });

            Assert.Equal(1, store.Count);
            Assert.False(store.Contains("resource:Copper", new Vector3(1, 1, 1)));
            Assert.True(store.Contains("resource:Tin", new Vector3(2, 2, 2)));
        }

        [Fact]
        public void Load_SkipsMalformedLinesAndHeader()
        {
            var store = new DismissedPinStore();
            store.Load(new List<string>
            {
                "#dismissedv1",
                "",
                "resource%3ACopper|1|2",           // too few fields
                "resource%3ACopper|a|2|3",         // unparseable float
                "resource%3ACopper|1|2|3|extra",   // too many fields
                "resource%3ACopper|1|2|3"          // valid
            });

            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Load_Null_LeavesEmptyStore()
        {
            var store = new DismissedPinStore();
            store.Add("resource:Copper", Vector3.zero);
            store.Load(null);

            Assert.Equal(0, store.Count);
        }
    }

    public class PinDismissalTests
    {
        [Fact]
        public void IndexOfClosest_NoCandidates_ReturnsMinusOne()
        {
            Assert.Equal(-1, PinDismissal.IndexOfClosest(new List<Vector3>(), Vector3.zero, 10f));
        }

        [Fact]
        public void IndexOfClosest_PicksNearestInsideRadius()
        {
            var positions = new List<Vector3> { new Vector3(8, 0, 0), new Vector3(3, 0, 0), new Vector3(5, 0, 0) };

            Assert.Equal(1, PinDismissal.IndexOfClosest(positions, Vector3.zero, 10f));
        }

        [Fact]
        public void IndexOfClosest_AllOutsideRadius_ReturnsMinusOne()
        {
            var positions = new List<Vector3> { new Vector3(20, 0, 0) };

            Assert.Equal(-1, PinDismissal.IndexOfClosest(positions, Vector3.zero, 10f));
        }

        [Fact]
        public void IndexOfClosest_IgnoresHeight()
        {
            var positions = new List<Vector3> { new Vector3(1, 500, 0) };

            Assert.Equal(0, PinDismissal.IndexOfClosest(positions, Vector3.zero, 10f));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]   // category hidden / off-screen: nothing to click
        [InlineData(false, true, false)]   // creature cluster: not dismissible
        [InlineData(false, false, false)]
        public void IsDismissible_RequiresPersistentAndShown(bool persistent, bool shown, bool expected)
        {
            Assert.Equal(expected, PinDismissal.IsDismissible(persistent, shown));
        }
    }

    // Shares the "RadarConfig" collection (see Scanning/ObjectEvaluatorTests.cs) since it reads
    // RadarConfig's process-wide static ConfigEntry fields.
    [Collection("RadarConfig")]
    public class PinDisplayConfigTests
    {
        [Fact]
        public void Defaults_HideCreatureNames_AllowPinRemoval_AndDoNotAutoRestore()
        {
            Assert.False(RadarConfig.ShowCreatureNames.Value);
            Assert.True(RadarConfig.EnablePinRemoval.Value);
            Assert.False(RadarConfig.ClearDismissedPins.Value);
        }
    }

    public class MinimapMarkerOrderTests
    {
        [Fact]
        public void CompareDrawOrder_LaterSiblingDrawsAfter()
        {
            Assert.True(MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 3 }, new[] { 0, 2 }) > 0);
            Assert.True(MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 1 }, new[] { 0, 2 }) < 0);
        }

        [Fact]
        public void CompareDrawOrder_DecidedAtFirstDifferingLevel()
        {
            // Marker deep inside an earlier branch still draws before anything in a later branch.
            Assert.True(MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 1, 9, 9 }, new[] { 0, 2 }) < 0);
            Assert.True(MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 2 }, new[] { 0, 1, 9, 9 }) > 0);
        }

        [Fact]
        public void CompareDrawOrder_AncestorDrawsBeforeDescendant()
        {
            Assert.True(MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 2 }, new[] { 0, 2, 5 }) < 0);
            Assert.True(MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 2, 5 }, new[] { 0, 2 }) > 0);
        }

        [Fact]
        public void CompareDrawOrder_SameElement_ReturnsZero()
        {
            Assert.Equal(0, MinimapMarkerOrder.CompareDrawOrder(new[] { 0, 2 }, new[] { 0, 2 }));
        }
    }
}
