using Xunit;

namespace ValheimRadar.Tests.Scanning
{
    // Shares the RadarConfig collection with ObjectEvaluatorTests - see RadarConfigFixture there.
    [Collection("RadarConfig")]
    public class LocationScannerTests
    {
        [Fact]
        public void IsCategoryEnabled_LocationCategory_ReflectsBothGroupAndItemToggle()
        {
            RadarConfig.Group_BossLocations.Value = true;
            RadarConfig.Locations["boss_eikthyr"].Enabled.Value = true;
            Assert.True(ObjectEvaluator.IsCategoryEnabled("location:boss_eikthyr"));

            RadarConfig.Locations["boss_eikthyr"].Enabled.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled("location:boss_eikthyr"));
            RadarConfig.Locations["boss_eikthyr"].Enabled.Value = true;

            RadarConfig.Group_BossLocations.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled("location:boss_eikthyr"));
            RadarConfig.Group_BossLocations.Value = true;
        }

        [Fact]
        public void IsCategoryEnabled_UnknownLocationKey_ReturnsFalse()
        {
            Assert.False(ObjectEvaluator.IsCategoryEnabled("location:not_a_real_location"));
        }

        [Fact]
        public void GetDefaultIconForCategory_LocationKey_ReturnsDefinitionIconPng()
        {
            Assert.Equal("dungeon.png", ObjectEvaluator.GetDefaultIconForCategory("location:dungeon_blackforestcrypt"));
        }

        [Fact]
        public void GetVanillaIconForCategory_BossAltar_ReturnsVerifiedTrophySprite()
        {
            Assert.Equal("TrophyDragonQueen", ObjectEvaluator.GetVanillaIconForCategory("location:boss_moder"));
        }

        [Fact]
        public void GetVanillaIconForCategory_RuinWithoutVanillaIcon_ReturnsNull()
        {
            Assert.Null(ObjectEvaluator.GetVanillaIconForCategory("location:ruin_stonetower"));
        }

        [Theory]
        [InlineData("crypt2")]
        [InlineData("crypt3")]
        [InlineData("crypt4")]
        [InlineData("vendor_blackforest")]
        [InlineData("eikthyrnir")]
        [InlineData("stonehenge1")]
        [InlineData("meteorite")]
        public void IsKnownLocationPrefab_CuratedPrefab_ReturnsTrue(string prefabLower)
        {
            Assert.True(RadarConfig.IsKnownLocationPrefab(prefabLower));
        }

        [Fact]
        public void IsKnownLocationPrefab_UnlistedPrefab_ReturnsFalse()
        {
            Assert.False(RadarConfig.IsKnownLocationPrefab("some_unrelated_prefab"));
        }

        [Fact]
        public void LocationDefinitions_MergedDungeonEntrance_MapsAllAliasesToSameCanonicalKey()
        {
            Assert.True(RadarConfig.LocationPrefabLookup.TryGetValue("crypt2", out var crypt2));
            Assert.True(RadarConfig.LocationPrefabLookup.TryGetValue("crypt3", out var crypt3));
            Assert.True(RadarConfig.LocationPrefabLookup.TryGetValue("crypt4", out var crypt4));

            Assert.Equal("dungeon_blackforestcrypt", crypt2.CanonicalKey);
            Assert.Equal(crypt2.CanonicalKey, crypt3.CanonicalKey);
            Assert.Equal(crypt2.CanonicalKey, crypt4.CanonicalKey);
        }

        [Theory]
        [InlineData("hildir_crypt", "dungeon_hildircrypt")]
        [InlineData("hildir_cave", "dungeon_hildircave")]
        [InlineData("hildir_plainsfortress", "dungeon_hildirtower")]
        [InlineData("bearcave", "dungeon_bearcave")]
        public void LocationDefinitions_HildirDungeons_AreCuratedDungeonEntrances(string prefabLower, string canonicalKey)
        {
            Assert.True(RadarConfig.LocationPrefabLookup.TryGetValue(prefabLower, out var def));
            Assert.Equal(canonicalKey, def.CanonicalKey);
            Assert.Equal(RadarConfig.LocationGroup.DungeonEntrance, def.Group);
            Assert.True(ObjectEvaluator.IsCategoryEnabled($"location:{canonicalKey}"));
        }

        [Fact]
        public void TetraPondCaveVariant_HasOwnToggleButNoPrefab()
        {
            Assert.DoesNotContain(RadarConfig.LocationPrefabLookup.Values, d => d.CanonicalKey == RadarConfig.MountainCaveTetraKey);
            Assert.Equal("mountaincave02", Assert.Single(RadarConfig.CanonicalLocationLookup[RadarConfig.MountainCaveKey].PrefabNames));

            string category = "location:" + RadarConfig.MountainCaveTetraKey;
            Assert.True(ObjectEvaluator.IsCategoryEnabled(category));
            Assert.Equal("dungeon.png", ObjectEvaluator.GetDefaultIconForCategory(category));

            RadarConfig.Locations[RadarConfig.MountainCaveKey].Enabled.Value = false;
            Assert.True(ObjectEvaluator.IsCategoryEnabled(category));
            RadarConfig.Locations[RadarConfig.MountainCaveKey].Enabled.Value = true;

            RadarConfig.Locations[RadarConfig.MountainCaveTetraKey].Enabled.Value = false;
            Assert.False(ObjectEvaluator.IsCategoryEnabled(category));
            RadarConfig.Locations[RadarConfig.MountainCaveTetraKey].Enabled.Value = true;
        }

        [Theory]
        [InlineData("cave_new_deeproom_bottom_lake", true)]
        [InlineData("cave_dome_bottom_lake", true)]
        [InlineData("cave_new_deeproom_bottom", false)]
        [InlineData("cave_new_deeproom_bottom_ice", false)]
        [InlineData("cave_dome_bottom_endcap", false)]
        [InlineData("sunkencrypt_new_Room1", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsTetraPondRoom_OnlyCaveLakeRooms(string roomName, bool expected)
        {
            Assert.Equal(expected, LocationScanner.IsTetraPondRoom(roomName));
        }

        [Fact]
        public void LocationDefinitions_DecorativeRuinTypes_DefaultDisabled()
        {
            Assert.False(RadarConfig.CanonicalLocationLookup["ruin_mistlandsstatue"].DefaultEnabled);
            Assert.False(RadarConfig.CanonicalLocationLookup["ruin_mistlandsrockspire"].DefaultEnabled);
        }
    }
}
