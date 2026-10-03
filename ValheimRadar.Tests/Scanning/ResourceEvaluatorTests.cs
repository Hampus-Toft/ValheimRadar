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
        public void MuddyScrapPilePrefabs_ClassifyAsIronScrap(string nameLower)
        {
            Assert.Equal("IronScrap", ClassifyByName(nameLower));
        }

        // Loose item drops (a player's dropped ore, a smelter's ingots, scrap from a mined pile) are
        // never tracked - only the world resource nodes above.
        [Theory]
        [InlineData("copperore")]
        [InlineData("copper")]
        [InlineData("tinore")]
        [InlineData("tin")]
        [InlineData("ironscrap")]
        [InlineData("iron")]
        [InlineData("silverore")]
        [InlineData("silver")]
        [InlineData("obsidian")]
        [InlineData("flint")]
        [InlineData("stone")]
        [InlineData("wood")]
        public void LooseItemPrefabs_AreNeverClassified(string nameLower)
        {
            Assert.Null(ClassifyByName(nameLower));
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

        // Ore deposits are labeled with just the ore's name (no "Deposit"), and saved points with the
        // old label are renamed to it on load via the same lookup.
        [Theory]
        [InlineData("resource:CopperDeposit", "rock4_copper", "Copper")]
        [InlineData("resource:TinDeposit", "minerock_tin", "Tin")]
        [InlineData("resource:IronScrap", "mudpile_beacon", "Iron")]
        [InlineData("resource:SilverDeposit", "rock3_silver", "Silver")]
        [InlineData("resource:ObsidianDeposit", "minerock_obsidian", "Obsidian")]
        public void OreDeposits_AreOreDepositCategoriesNamedAfterTheOre(string categoryKey, string rawName, string expectedName)
        {
            Assert.True(ObjectEvaluator.IsOreDepositCategory(categoryKey));
            Assert.Equal(expectedName, ObjectEvaluator.GetResourceDisplayNameOverride(categoryKey, rawName));
        }

        [Theory]
        [InlineData("resource:Flint")]
        [InlineData("resource:Raspberry")]
        [InlineData("resource:Guck")]
        [InlineData("location:Crypt")]
        [InlineData(null)]
        public void OtherCategories_AreNotOreDeposits(string categoryKey)
        {
            Assert.False(ObjectEvaluator.IsOreDepositCategory(categoryKey));
        }

        [Fact]
        public void RulesWithoutFixedName_HaveNoDisplayNameOverride()
        {
            Assert.Null(ObjectEvaluator.GetResourceDisplayNameOverride("resource:Raspberry", "raspberrybush"));
            Assert.Null(ObjectEvaluator.GetResourceDisplayNameOverride("resource:Guck", "gucksack"));
        }

        // Only resources that never grow back may have their pins removed when mined/picked or
        // missing from a rescan - a picked berry bush hides its colliders and would look "gone".
        [Theory]
        [InlineData("resource:CopperDeposit")]
        [InlineData("resource:TinDeposit")]
        [InlineData("resource:IronScrap")]
        [InlineData("resource:SilverDeposit")]
        [InlineData("resource:ObsidianDeposit")]
        [InlineData("resource:Flint")]
        [InlineData("resource:Stone")]
        [InlineData("resource:Wood")]
        [InlineData("resource:Guck")]
        public void NonRegrowingCategories_AreDepletable(string categoryKey)
        {
            Assert.True(ObjectEvaluator.IsCategoryDepletable(categoryKey));
        }

        [Theory]
        [InlineData("resource:Raspberry")]
        [InlineData("resource:Cloudberry")]
        [InlineData("resource:RedMushroom")]
        [InlineData("resource:Dandelion")]
        [InlineData("resource:Barley")]
        [InlineData("resource:Beehives")]
        [InlineData("resource:Chests")]
        [InlineData("location:Crypt")]
        [InlineData("creature:boar")]
        [InlineData("")]
        [InlineData(null)]
        public void RegrowingOrNonResourceCategories_AreNotDepletable(string categoryKey)
        {
            Assert.False(ObjectEvaluator.IsCategoryDepletable(categoryKey));
        }

        // The object a player hits is matched by prefab name - including the "_frac" pieces a deposit
        // turns into, which still belong to the deposit's pin.
        [Theory]
        [InlineData("rock4_copper", "resource:CopperDeposit")]
        [InlineData("rock4_copper_frac", "resource:CopperDeposit")]
        [InlineData("silvervein_frac", "resource:SilverDeposit")]
        [InlineData("minerock_obsidian", "resource:ObsidianDeposit")]
        [InlineData("mudpile_beacon", "resource:IronScrap")]
        [InlineData("pickable_flint", "resource:Flint")]
        public void TryGetDepletableCategory_MinedPrefabs_ResolveToTheirCategory(string nameLower, string expected)
        {
            Assert.True(ObjectEvaluator.TryGetDepletableCategory(null, nameLower, out string categoryKey));
            Assert.Equal(expected, categoryKey);
        }

        // Regrowing resources only - names no resource rule matches fall through to PoiEvaluator rules
        // that inspect live components, so they can't be driven with a null GameObject here.
        [Theory]
        [InlineData("raspberrybush")]
        [InlineData("beehive")]
        public void TryGetDepletableCategory_OtherPrefabs_AreIgnored(string nameLower)
        {
            Assert.False(ObjectEvaluator.TryGetDepletableCategory(null, nameLower, out _));
        }

        [Theory]
        [InlineData("raspberrybush", "resource:Raspberry")]
        [InlineData("pickable_mushroom_yellow", "resource:YellowMushroom")]
        [InlineData("pickable_thistle", "resource:Thistle")]
        [InlineData("pickable_barley_wild", "resource:Barley")]
        public void TryGetRespawningCategory_RegrowingPrefabs_ResolveToTheirCategory(string nameLower, string expected)
        {
            Assert.True(ObjectEvaluator.TryGetRespawningCategory(null, nameLower, out string categoryKey));
            Assert.Equal(expected, categoryKey);
        }

        // Depletable resources are removed, not timed; unmatched names aren't resources at all.
        [Theory]
        [InlineData("pickable_flint")]
        [InlineData("rock4_copper")]
        [InlineData("placeable_stone")]
        public void TryGetRespawningCategory_OtherPrefabs_AreIgnored(string nameLower)
        {
            Assert.False(ObjectEvaluator.TryGetRespawningCategory(null, nameLower, out _));
        }
    }
}
