using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    // Uses the same RadarConfig collection fixture as ObjectEvaluatorTests so RadarConfig.Initialize
    // has run (populating AliasLookup) before these assertions read it.
    [Collection("RadarConfig")]
    public class VanillaIconResolverTests
    {
        [Fact]
        public void GetCreatureTrophySprite_Greyling_ReturnsNullAndFallsBackGracefully()
        {
            // No dedicated Greyling icon exists in the vanilla atlas - classification must still
            // succeed (that's the actual "never gets a pin" half of the original bug report); only
            // the vanilla-icon lookup is expected to come back empty, falling back to the category
            // default PNG / built-in fallback icon (see PinManager.ResolvePerObjectPin).
            Assert.True(RadarConfig.AliasLookup.ContainsKey("greyling"));
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite("greyling"));
        }

        [Fact]
        public void GetCreatureTrophySprite_Bear_ResolvesToVerifiedBjornSprite()
        {
            Assert.Equal("TrophyBjorn", VanillaIconResolver.GetCreatureTrophySprite("bear"));
        }

        [Fact]
        public void GetCreatureTrophySprite_UnknownKey_ReturnsNull()
        {
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite("not_a_real_creature"));
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite(null));
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite(string.Empty));
        }

        [Fact]
        public void GetCreatureTrophySprite_Fish_ReturnsNullAndFallsBackGracefully()
        {
            // Fish are ItemDrop-based, not Character trophies - no entry is expected for any of
            // them, and that's by design, not a bug (see ObjectEvaluator's Fish branch, which
            // always falls back to "animal.png" for fish).
            Assert.True(RadarConfig.AliasLookup.ContainsKey("fish1"));
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite("fish_perch"));
        }
    }
}
