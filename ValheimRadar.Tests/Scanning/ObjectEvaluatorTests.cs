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
        public void FindCreatureOverride_MatchesMostSpecificKeyFirst()
        {
            // "greydwarf_elite" must win over the shorter "greydwarf" entry it overlaps with - see
            // the ordering comment on RadarConfig.CreatureDefinitions.
            var entry = ObjectEvaluator.FindCreatureOverride("greydwarf_elite_attack", out string matchedKey);

            Assert.Equal("greydwarf_elite", matchedKey);
            Assert.NotNull(entry);
        }

        [Fact]
        public void FindCreatureOverride_NoMatch_ReturnsNullAndNullKey()
        {
            var entry = ObjectEvaluator.FindCreatureOverride("some_unrelated_prefab", out string matchedKey);

            Assert.Null(entry);
            Assert.Null(matchedKey);
        }
    }
}
