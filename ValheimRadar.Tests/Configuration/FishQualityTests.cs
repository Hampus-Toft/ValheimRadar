using Xunit;

namespace ValheimRadar.Tests.Configuration
{
    // Shares the RadarConfig collection with ObjectEvaluatorTests - see RadarConfigFixture there.
    [Collection("RadarConfig")]
    public class FishQualityTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(3, 3)]
        [InlineData(5, 5)]
        [InlineData(9, 5)]
        public void ClampFishQuality_KeepsOneToFive(int quality, int expected)
        {
            Assert.Equal(expected, RadarConfig.ClampFishQuality(quality));
        }

        [Fact]
        public void IsFishQualityShown_EachLevelTogglesSeparately()
        {
            for (int quality = 1; quality <= RadarConfig.MaxFishQuality; quality++)
            {
                Assert.True(RadarConfig.IsFishQualityShown(quality));
            }

            RadarConfig.FishQualities[2].Value = false; // quality 3
            try
            {
                Assert.False(RadarConfig.IsFishQualityShown(3));
                Assert.True(RadarConfig.IsFishQualityShown(2));
                Assert.True(RadarConfig.IsFishQualityShown(4));
            }
            finally
            {
                RadarConfig.FishQualities[2].Value = true;
            }
        }

        [Fact]
        public void IsFishQualityShown_OutOfRangeUsesNearestLevel()
        {
            RadarConfig.FishQualities[RadarConfig.MaxFishQuality - 1].Value = false;
            try
            {
                Assert.False(RadarConfig.IsFishQualityShown(7));
            }
            finally
            {
                RadarConfig.FishQualities[RadarConfig.MaxFishQuality - 1].Value = true;
            }
        }
    }
}
