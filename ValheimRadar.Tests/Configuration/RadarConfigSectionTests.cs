using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Xunit;

namespace ValheimRadar.Tests.Configuration
{
    public class RadarConfigSectionTests
    {
        [Theory]
        [InlineData("01 - General", "1 - General")]
        [InlineData("03b - Bosses & Notable Creatures", "3b - Bosses & Notable Creatures")]
        [InlineData("09b - Creatures (Fish)", "9b - Creatures (Fish)")]
        [InlineData("10 - Resources (Berries)", null)]
        [InlineData("20 - Locations (Ruins & Structures)", null)]
        [InlineData("", null)]
        public void GetLegacySectionName_StripsOnlyThePaddingZero(string section, string expected)
        {
            Assert.Equal(expected, RadarConfig.GetLegacySectionName(section));
        }

        // BepInEx writes sections in ordinal string order - unpadded numbers put "10 - ..." before
        // "2 - ...", so every section must start with a two-digit number.
        [Fact]
        public void AllDefinitionSections_AreZeroPadded()
        {
            var sections = RadarConfig.CreatureDefinitions.Select(d => d.Section)
                .Concat(RadarConfig.LocationDefinitions.Where(d => !d.AlwaysEnabled).Select(d => d.Section))
                .Distinct();

            foreach (string section in sections)
            {
                Assert.True(section.Length >= 2 && char.IsDigit(section[0]) && char.IsDigit(section[1]), $"Section '{section}' is not zero-padded");
            }
        }

        [Fact]
        public void MigrateLegacySection_CopiesValueFromUnpaddedSectionAndDropsOrphan()
        {
            string path = Path.Combine(Path.GetTempPath(), $"ValheimRadar.Tests.{Guid.NewGuid():N}.cfg");
            try
            {
                File.WriteAllText(path, "[1 - General]\n\nScanRadius = 150\n\n[10 - Resources (Berries)]\n\nRaspberries = false\n");

                var config = new ConfigFile(path, true);
                var radius = config.Bind("01 - General", "ScanRadius", 100f);
                RadarConfig.MigrateLegacySection(config, radius, "01 - General", "ScanRadius");

                Assert.Equal(150f, radius.Value);
                Assert.DoesNotContain("[1 - General]", File.ReadAllText(path));

                // Already two-digit sections were never renamed and bind their saved value directly.
                var raspberries = config.Bind("10 - Resources (Berries)", "Raspberries", true);
                RadarConfig.MigrateLegacySection(config, raspberries, "10 - Resources (Berries)", "Raspberries");
                Assert.False(raspberries.Value);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
