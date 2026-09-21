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
        public void GetCreatureTrophySprite_Surtling_ResolvesToVerifiedTrophySprite()
        {
            // Surtling previously had no RadarConfig.CreatureDefinitions entry at all, so it only
            // ever matched the generic hostile-monster fallback and got the generic monster icon
            // instead of its own vanilla trophy sprite.
            Assert.True(RadarConfig.AliasLookup.ContainsKey("surtling"));
            Assert.Equal("TrophySurtling", VanillaIconResolver.GetCreatureTrophySprite("surtling"));
        }

        [Fact]
        public void GetCreatureTrophySprite_UnknownKey_ReturnsNull()
        {
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite("not_a_real_creature"));
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite(null));
            Assert.Null(VanillaIconResolver.GetCreatureTrophySprite(string.Empty));
        }

        [Fact]
        public void GetCreatureTrophySprite_Writhan_ResolvesToOwnTrophySprite()
        {
            // Writhan has its own TrophyWrithan sprite; before it had no definition or icon entry it
            // matched only the generic hostile-monster bucket and showed the bare default pin.
            Assert.True(RadarConfig.AliasLookup.ContainsKey("writhan"));
            Assert.Equal("TrophyWrithan", VanillaIconResolver.GetCreatureTrophySprite("writhan"));
        }

        [Fact]
        public void GetCreatureTrophySprite_Oozer_ReusesBlobTrophy()
        {
            // "Oozer" is the in-game name of the BlobElite prefab, which has no trophy sprite of its
            // own - it reuses the Blob trophy.
            Assert.Equal("TrophyBlob", VanillaIconResolver.GetCreatureTrophySprite("blob_elite"));
        }

        // Baby -> adult pairs taken from the game's own Growup components (baby prefab grows into
        // adult prefab). Each baby must resolve to the same vanilla sprite as its adult so a
        // juvenile tamed animal never renders icon-less. Species with a RadarConfig definition
        // resolve through their canonical key; the rest through the raw prefab-name fallback.
        [Theory]
        [InlineData("boar_piggy", "boar", "TrophyBoar")]
        [InlineData("wolf_cub", "wolf", "TrophyWolf")]
        [InlineData("lox_calf", "lox", "TrophyLox")]
        public void BabyPrefab_ResolvesToSameSpriteAsAdult_ViaCreatureDefinition(string babyPrefab, string adultPrefab, string expectedSprite)
        {
            Assert.True(RadarConfig.AliasLookup.TryGetValue(babyPrefab, out var babyDef));
            Assert.True(RadarConfig.AliasLookup.TryGetValue(adultPrefab, out var adultDef));
            Assert.Same(adultDef, babyDef);
            Assert.Equal(expectedSprite, VanillaIconResolver.GetCreatureTrophySprite(babyDef.CanonicalKey));
        }

        [Theory]
        [InlineData("asksvin_hatchling", "asksvin", "TrophyAsksvin")]
        [InlineData("moose_calf", "moose", "TrophyMoose")]
        public void BabyPrefab_ResolvesToSameSpriteAsAdult_ViaRawPrefabName(string babyPrefab, string adultPrefab, string expectedSprite)
        {
            Assert.Equal(expectedSprite, VanillaIconResolver.GetCreatureTrophySprite(adultPrefab));
            Assert.Equal(VanillaIconResolver.GetCreatureTrophySprite(adultPrefab), VanillaIconResolver.GetCreatureTrophySprite(babyPrefab));
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
