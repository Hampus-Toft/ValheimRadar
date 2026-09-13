using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    // Uses the same RadarConfig collection fixture as ObjectEvaluatorTests so RadarConfig.Initialize
    // has run (populating AliasLookup) before these assertions read it.
    [Collection("RadarConfig")]
    public class VanillaIconResolverTests
    {
        [Fact]
        public void GetCreatureTrophySprite_Greyling_ReusesGreydwarfIcon()
        {
            // No dedicated Greyling icon exists in the vanilla atlas - classification succeeds
            // regardless (that was the actual "never gets a pin" half of the original bug report),
            // and the icon deliberately reuses Greydwarf's trophy sprite as the closest visual
            // stand-in rather than falling all the way back to the generic monster icon.
            Assert.True(RadarConfig.AliasLookup.ContainsKey("greyling"));
            Assert.Equal("TrophyGreydwarf", VanillaIconResolver.GetCreatureTrophySprite("greyling"));
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
        public void GetCreatureTrophySprite_Fish_ResolvesToPerSpeciesItemIcon()
        {
            // Fish are ItemDrop-based, not Character trophies, so they're mapped to their own
            // per-species vanilla item icon (sprite names "fish1".."fish12") rather than a "Trophy*"
            // sprite - see ObjectEvaluator's Fish branch.
            Assert.True(RadarConfig.AliasLookup.ContainsKey("fish1"));
            Assert.Equal("fish1", VanillaIconResolver.GetCreatureTrophySprite("fish_perch"));
            Assert.Equal("fish12", VanillaIconResolver.GetCreatureTrophySprite("fish_pufferfish"));
        }
    }
}
