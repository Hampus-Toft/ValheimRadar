using Xunit;

namespace ValheimRadar.Tests.Scanning
{
    // Rule predicates ignore their GameObject argument (exact-alias name matching only), so they can
    // be driven directly with null - see ResourceEvaluator.Rules. Shares the RadarConfig collection
    // because rule ids/toggles resolve through RadarConfig's process-wide static entries.
    [Collection("RadarConfig")]
    public class ResourceEvaluatorTests
    {
        private static string ClassifyByName(string nameLower)
        {
            foreach (var rule in ResourceEvaluator.Rules)
            {
                if (rule.Matches(null, nameLower)) return rule.Id;
            }

            return null;
        }

        // Issue #38: the muddy scrap piles world-gen scatters over the Swamp are the prefab
        // "mudpile_beacon" (confirmed from Valheim's ZoneSystem vegetation list) - it must classify as
        // Iron Scrap alongside the older mudpile/mudpile2 (Sunken Crypt) prefabs.
        [Theory]
        [InlineData("mudpile_beacon")]
        [InlineData("mudpile_old")]
        [InlineData("mudpile")]
        [InlineData("mudpile2")]
        [InlineData("ironscrap")]
        public void MuddyScrapPilePrefabs_ClassifyAsIronScrap(string nameLower)
        {
            Assert.Equal("IronScrap", ClassifyByName(nameLower));
        }

        [Theory]
        [InlineData("mudpile_frac")]
        [InlineData("mudpile2_frac")]
        [InlineData("mud_road")]
        [InlineData("mudfloor")]
        public void MuddyScrapPileFragmentsAndUnrelatedMudPrefabs_DoNotClassifyAsIronScrap(string nameLower)
        {
            Assert.NotEqual("IronScrap", ClassifyByName(nameLower));
        }

        [Theory]
        [InlineData("mudpile_frac")]
        [InlineData("mudpile2_frac")]
        public void MuddyScrapPileFragments_AreRejectedAsDebrisBeforeClassification(string nameLower)
        {
            Assert.True(ScanFilters.IsDebrisName(nameLower));
        }

        [Theory]
        [InlineData("mudpile_beacon")]
        [InlineData("mudpile_old")]
        [InlineData("mudpile")]
        [InlineData("mudpile2")]
        public void MuddyScrapPileIntactPrefabs_AreNotTreatedAsDebris(string nameLower)
        {
            Assert.False(ScanFilters.IsDebrisName(nameLower));
        }

        [Fact]
        public void IronScrapRule_UsesIronToggleAndOreIcons()
        {
            Assert.True(ResourceEvaluator.TryGetRule("IronScrap", out var rule));
            Assert.Equal("ore.png", rule.IconPng);
            Assert.Equal("ironscrap", rule.VanillaIcon);

            RadarConfig.Group_Ores.Value = true;
            RadarConfig.TrackIron.Value = true;
            Assert.True(rule.Enabled());
            Assert.True(ObjectEvaluator.IsCategoryEnabled("resource:IronScrap"));

            RadarConfig.TrackIron.Value = false;
            Assert.False(rule.Enabled());
            Assert.False(ObjectEvaluator.IsCategoryEnabled("resource:IronScrap"));
            RadarConfig.TrackIron.Value = true;
        }
    }
}
