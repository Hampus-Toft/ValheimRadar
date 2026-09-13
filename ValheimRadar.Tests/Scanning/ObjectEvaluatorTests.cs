using System;
using System.IO;
using BepInEx.Configuration;
using Xunit;

namespace ValheimRadar.Tests.Scanning
{
    // RadarConfig.Initialize populates static ConfigEntry<T> fields shared process-wide, so every
    // test that reads RadarConfig state needs it initialized exactly once, from one throwaway
    // ConfigFile backed by a temp file that gets cleaned up afterwards.
    public sealed class RadarConfigFixture : IDisposable
    {
        private readonly string configFilePath;

        public RadarConfigFixture()
        {
            configFilePath = Path.Combine(Path.GetTempPath(), $"ValheimRadar.Tests.{Guid.NewGuid():N}.cfg");
            var configFile = new ConfigFile(configFilePath, false);
            RadarConfig.Initialize(configFile);
        }

        public void Dispose()
        {
            if (File.Exists(configFilePath)) File.Delete(configFilePath);
        }
    }

    [CollectionDefinition("RadarConfig")]
    public class RadarConfigCollection : ICollectionFixture<RadarConfigFixture>
    {
    }

    // All tests that touch RadarConfig's shared static state live in this one collection so xUnit
    // never runs them in parallel with each other - see RadarConfigFixture.
    [Collection("RadarConfig")]
    public class ObjectEvaluatorTests
    {
        [Theory]
        [InlineData("pickable_dandelion", "dandelion")]
        [InlineData("item_flint(Clone)", "flint")]
        [InlineData("piece_beehive", "beehive")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void StripKnownPrefixes_RemovesEngineNoiseAndCloneSuffix(string input, string expected)
        {
            Assert.Equal(expected, ObjectEvaluator.StripKnownPrefixes(input));
        }

        [Fact]
        public void IsCategoryEnabled_NullOrEmptyKey_ReturnsFalse()
        {
            Assert.False(ObjectEvaluator.IsCategoryEnabled(null));
            Assert.False(ObjectEvaluator.IsCategoryEnabled(string.Empty));
        }

        [Fact]
        public void IsCategoryEnabled_ResourceCategory_ReflectsBothGroupAndItemToggle()
        {
            RadarConfig.Group_Berries.Value = true;
            RadarConfig.TrackRaspberry.Value = true;
            Assert.True(ObjectEvaluator.IsCategoryEnabled("resource:Raspberry"));

            RadarConfig.TrackRaspberry.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled("resource:Raspberry"));
            RadarConfig.TrackRaspberry.Value = true;

            RadarConfig.Group_Berries.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled("resource:Raspberry"));
            RadarConfig.Group_Berries.Value = true;
        }

        [Fact]
        public void IsCategoryEnabled_UnlistedMonster_FallsBackToMonsterDefaultToggle()
        {
            RadarConfig.Group_Creatures.Value = true;
            RadarConfig.EnableMonsters.Value = true;
            Assert.True(ObjectEvaluator.IsCategoryEnabled("creature:_monster"));

            RadarConfig.EnableMonsters.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled("creature:_monster"));
            RadarConfig.EnableMonsters.Value = true;
        }

        [Fact]
        public void IsCategoryEnabled_SpecificListedCreature_UsesItsOwnEntry()
        {
            RadarConfig.Group_Creatures.Value = true;
            RadarConfig.Creatures["wolf"].Enabled.Value = true;
            Assert.True(ObjectEvaluator.IsCategoryEnabled("creature:wolf"));

            RadarConfig.Creatures["wolf"].Enabled.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled("creature:wolf"));
            RadarConfig.Creatures["wolf"].Enabled.Value = true;
        }

        [Fact]
        public void GetDefaultIconForCategory_ResourceKey_ReturnsRuleIconPng()
        {
            Assert.Equal("berry.png", ObjectEvaluator.GetDefaultIconForCategory("resource:Raspberry"));
        }

        [Fact]
        public void GetDefaultIconForCategory_NonResourceOrUnknownKey_ReturnsNull()
        {
            Assert.Null(ObjectEvaluator.GetDefaultIconForCategory("creature:_monster"));
            Assert.Null(ObjectEvaluator.GetDefaultIconForCategory(null));
            Assert.Null(ObjectEvaluator.GetDefaultIconForCategory("resource:NotARealRule"));
        }

        [Fact]
        public void GetVanillaIconForCategory_ResourceKey_ReturnsVerifiedIconName()
        {
            Assert.Equal("raspberry", ObjectEvaluator.GetVanillaIconForCategory("resource:Raspberry"));
        }

        [Fact]
        public void GetVanillaIconForCategory_RuleWithoutVanillaIcon_ReturnsNull()
        {
            // "Dungeons" has no VanillaIcon set (see ResourceRules) - relies on category PNG/fallback.
            Assert.Null(ObjectEvaluator.GetVanillaIconForCategory("resource:Dungeons"));
        }

        [Fact]
        public void CopperOreAndIngot_AreDistinctRulesWithDistinctVerifiedIcons()
        {
            // The bug this whitelist rewrite exists to fix: ore and ingot used to be one
            // conflated "resource:Copper" rule/pin. They are now two distinct rule ids with
            // distinct verified vanilla icons.
            Assert.Equal("ore.png", ObjectEvaluator.GetDefaultIconForCategory("resource:CopperOre"));
            Assert.Equal("ore.png", ObjectEvaluator.GetDefaultIconForCategory("resource:CopperIngot"));
            Assert.Equal("copperore", ObjectEvaluator.GetVanillaIconForCategory("resource:CopperOre"));
            Assert.Equal("bar_copper_stack", ObjectEvaluator.GetVanillaIconForCategory("resource:CopperIngot"));

            // The old conflated id no longer exists.
            Assert.Null(ObjectEvaluator.GetDefaultIconForCategory("resource:Copper"));
        }

        [Fact]
        public void OreDeposits_ReuseTheirMetalsOreIcon()
        {
            // Deposits (the uncollected world vein/node) reuse their metal's raw-ore vanilla icon
            // rather than falling back to no vanilla icon at all - there's no separate confirmed
            // "deposit" sprite, but a distinct icon still beats the built-in default.
            Assert.Equal("copperore", ObjectEvaluator.GetVanillaIconForCategory("resource:CopperDeposit"));
            Assert.Equal("TinOre", ObjectEvaluator.GetVanillaIconForCategory("resource:TinDeposit"));
            Assert.Equal("silverore", ObjectEvaluator.GetVanillaIconForCategory("resource:SilverDeposit"));
        }

        [Fact]
        public void NewPointsOfInterestRules_AreRegisteredWithExpectedIcons()
        {
            Assert.Equal("ore.png", ObjectEvaluator.GetDefaultIconForCategory("resource:ObsidianDeposit"));
            Assert.Equal("ruin.png", ObjectEvaluator.GetDefaultIconForCategory("resource:GreydwarfNest"));
            Assert.Equal("ruin.png", ObjectEvaluator.GetDefaultIconForCategory("resource:BodyPile"));
            Assert.Equal("ruin.png", ObjectEvaluator.GetDefaultIconForCategory("resource:Guck"));
            Assert.Equal("ruin.png", ObjectEvaluator.GetDefaultIconForCategory("resource:Trader"));
        }

        [Fact]
        public void FormatHumanFriendlyName_StripsPrefixesCloneSuffixAndTitleCases()
        {
            Assert.Equal("Red Mushroom", ObjectEvaluator.FormatHumanFriendlyName("pickable_RedMushroom(Clone)"));
        }

        [Fact]
        public void FormatHumanFriendlyName_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, ObjectEvaluator.FormatHumanFriendlyName(null));
            Assert.Equal(string.Empty, ObjectEvaluator.FormatHumanFriendlyName(string.Empty));
        }

        [Fact]
        public void ContainsAny_MatchesAnyMarkerAsSubstring()
        {
            Assert.True(ObjectEvaluator.ContainsAny("stone_frac_01", new[] { "_frac", "debris" }));
            Assert.True(ObjectEvaluator.ContainsAny("wood_debris", new[] { "_frac", "debris" }));
            Assert.False(ObjectEvaluator.ContainsAny("stone_01", new[] { "_frac", "debris" }));
        }

        [Fact]
        public void FindCreatureOverride_RealPrefabAliasMatchesCanonicalSpeciesKey()
        {
            // Bear's real prefab is "Bjorn" - under the old Contains("bear") substring check this
            // never matched at all, so Bear silently failed classification (and therefore its
            // icon) entirely. This is the concrete bug this whitelist rewrite fixes.
            var entry = ObjectEvaluator.FindCreatureOverride("bjorn", out string matchedKey);

            Assert.Equal("bear", matchedKey);
            Assert.NotNull(entry);
        }

        [Fact]
        public void FindCreatureOverride_ExactCleanKey_ResolvesCorrectCanonicalEntry()
        {
            var elite = ObjectEvaluator.FindCreatureOverride("greydwarf_elite", out string eliteKey);
            Assert.Equal("greydwarf_elite", eliteKey);
            Assert.NotNull(elite);

            var basic = ObjectEvaluator.FindCreatureOverride("greydwarf", out string basicKey);
            Assert.Equal("greydwarf", basicKey);
            Assert.NotNull(basic);
        }

        [Fact]
        public void FindCreatureOverride_PartialSubstringNoLongerMatches()
        {
            // Exact-alias semantics: a string that merely CONTAINS a real alias must not match -
            // this is the deliberate behavior change from the old Contains()-based lookup (see
            // RadarConfig.CreatureDefinitions - declaration order no longer matters either, since
            // there's no more "greydwarf_elite before greydwarf" ordering requirement).
            var entry = ObjectEvaluator.FindCreatureOverride("greydwarf_elite_attack", out string matchedKey);

            Assert.Null(entry);
            Assert.Null(matchedKey);
        }

        [Fact]
        public void FindCreatureOverride_NoMatch_ReturnsNullAndNullKey()
        {
            var entry = ObjectEvaluator.FindCreatureOverride("some_unrelated_prefab", out string matchedKey);

            Assert.Null(entry);
            Assert.Null(matchedKey);
        }

        [Fact]
        public void FindCreatureOverride_Fish_ResolvesLikeAnyOtherAlias()
        {
            // Fish share the same AliasLookup/Creatures dictionary as Character-based creatures -
            // see ObjectEvaluator's dedicated Fish-component branch in ShouldPinGameObject.
            var entry = ObjectEvaluator.FindCreatureOverride("fish1", out string matchedKey);

            Assert.Equal("fish_perch", matchedKey);
            Assert.NotNull(entry);
        }

        [Fact]
        public void FindCreatureOverride_Drake_ResolvesViaRealHatchlingPrefab()
        {
            // "drake" is not a real prefab name at all - the real prefab is "Hatchling" (loca
            // resolves to "Drake"), which is why nothing matched in-game before this fix.
            var entry = ObjectEvaluator.FindCreatureOverride("hatchling", out string matchedKey);

            Assert.Equal("drake", matchedKey);
            Assert.NotNull(entry);
        }

        [Theory]
        [InlineData("goblin", "fuling")]
        [InlineData("goblinarcher", "fuling")]
        [InlineData("goblinbrute", "fuling_berserker")]
        [InlineData("goblinshaman", "fuling_shaman")]
        public void FindCreatureOverride_Fuling_ResolvesViaRealGoblinPrefabs(string prefabName, string expectedCanonicalKey)
        {
            // "fuling"/"fuling_berserker"/"fuling_shaman" are not real prefab names - the real
            // prefabs use "Goblin*" naming, which is why none of these matched in-game before.
            var entry = ObjectEvaluator.FindCreatureOverride(prefabName, out string matchedKey);

            Assert.Equal(expectedCanonicalKey, matchedKey);
            Assert.NotNull(entry);
        }

        [Fact]
        public void FindCreatureOverride_Leviathan_ResolvesLikeAnyOtherAlias()
        {
            var entry = ObjectEvaluator.FindCreatureOverride("leviathan", out string matchedKey);

            Assert.Equal("leviathan", matchedKey);
            Assert.NotNull(entry);
        }

        [Fact]
        public void FindCreatureOverride_Boss_ResolvesToOwnCanonicalKey()
        {
            // Bosses previously had no RadarConfig.CreatureDefinitions entry at all and were only
            // ever reachable via the generic hostile-monster fallback.
            var entry = ObjectEvaluator.FindCreatureOverride("bonemass", out string matchedKey);

            Assert.Equal("bonemass", matchedKey);
            Assert.NotNull(entry);
        }
    }
}
