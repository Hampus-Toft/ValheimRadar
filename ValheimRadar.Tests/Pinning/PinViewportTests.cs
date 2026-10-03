using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    public class PinViewportTests
    {
        [Fact]
        public void UvToWorld_IsTheInverseOfMinimapWorldToMapPoint()
        {
            // Minimap.WorldToMapPoint: u = (x / pixelSize + textureSize / 2) / textureSize.
            const int textureSize = 2048;
            const float pixelSize = 12f;

            WorldRect whole = PinViewport.UvToWorld(0f, 0f, 1f, 1f, textureSize, pixelSize);
            Assert.Equal(-12288f, whole.MinX);
            Assert.Equal(12288f, whole.MaxZ);

            float x = 1234.5f, z = -876f;
            float u = (x / pixelSize + textureSize / 2f) / textureSize;
            float v = (z / pixelSize + textureSize / 2f) / textureSize;
            WorldRect point = PinViewport.UvToWorld(u, v, u, v, textureSize, pixelSize);
            Assert.Equal(x, point.MinX, 2);
            Assert.Equal(z, point.MinZ, 2);
        }

        [Fact]
        public void KeptRectFor_AddsHalfTheViewOnEverySide()
        {
            WorldRect kept = PinViewport.KeptRectFor(new WorldRect(0f, 0f, 100f, 50f));

            Assert.Equal(-50f, kept.MinX);
            Assert.Equal(150f, kept.MaxX);
            Assert.Equal(-25f, kept.MinZ);
            Assert.Equal(75f, kept.MaxZ);
        }

        [Fact]
        public void NeedsReload_NotWhileTheViewMovesWithinTheMargin()
        {
            var view = new WorldRect(0f, 0f, 100f, 100f);
            WorldRect kept = PinViewport.KeptRectFor(view);

            Assert.False(PinViewport.NeedsReload(kept, view));
            Assert.False(PinViewport.NeedsReload(kept, new WorldRect(45f, -45f, 145f, 55f)));
        }

        [Fact]
        public void NeedsReload_WhenTheViewLeavesTheKeptArea()
        {
            WorldRect kept = PinViewport.KeptRectFor(new WorldRect(0f, 0f, 100f, 100f));

            Assert.True(PinViewport.NeedsReload(kept, new WorldRect(60f, 0f, 160f, 100f)));
        }

        [Fact]
        public void NeedsReload_WhenZoomedOutPastTheKeptArea()
        {
            WorldRect kept = PinViewport.KeptRectFor(new WorldRect(0f, 0f, 100f, 100f));

            Assert.True(PinViewport.NeedsReload(kept, new WorldRect(-100f, -100f, 200f, 200f)));
        }

        [Fact]
        public void NeedsReload_OnlyWhenZoomedInFarEnoughToWasteMostOfTheKeptArea()
        {
            WorldRect kept = PinViewport.KeptRectFor(new WorldRect(0f, 0f, 100f, 100f)); // 200 x 200

            Assert.False(PinViewport.NeedsReload(kept, new WorldRect(25f, 25f, 75f, 75f)));   // kept = 4x fresh
            Assert.True(PinViewport.NeedsReload(kept, new WorldRect(40f, 40f, 60f, 60f)));    // kept = 25x fresh
        }

        [Fact]
        public void SortNearestLast_PutsTheNearestAtTheEnd()
        {
            var points = new List<Vector3> { new Vector3(5, 0, 0), new Vector3(100, 0, 100), new Vector3(1, 50, 1), new Vector3(-30, 0, 0) };

            PinViewport.SortNearestLast(points, p => p, Vector3.zero);

            Assert.Equal(new Vector3(1, 50, 1), points[3]); // height is ignored
            Assert.Equal(new Vector3(5, 0, 0), points[2]);
            Assert.Equal(new Vector3(-30, 0, 0), points[1]);
            Assert.Equal(new Vector3(100, 0, 100), points[0]);
        }
    }
}
