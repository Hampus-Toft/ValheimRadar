using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Persistence
{
    public class LegacyPinFilesTests
    {
        [Fact]
        public void ParsePoints_ReadsVersionAndUnescapesFields()
        {
            var points = LegacyPinFiles.ParsePoints(new[]
            {
                "#v4",
                "1|47225|-44.99933|77.70587|-47.59066|Blueberry%20Bush|blueberrybush|resource%3ABlueberry"
            }, out int version);

            Assert.Equal(4, version);
            var p = Assert.Single(points);
            Assert.Null(p.Key);
            Assert.Equal(1, p.UserId);
            Assert.Equal(47225u, p.Id);
            Assert.Equal(new Vector3(-44.99933f, 77.70587f, -47.59066f), p.Position);
            Assert.Equal("Blueberry Bush", p.DisplayName);
            Assert.Equal("blueberrybush", p.RawName);
            Assert.Equal("resource:Blueberry", p.CategoryKey);
        }

        [Fact]
        public void ParsePoints_NoHeader_IsPreVersioningFormat()
        {
            var points = LegacyPinFiles.ParsePoints(new[] { "1|2|0|0|0|Copper|rock4_copper|resource%3ACopper" }, out int version);

            Assert.Equal(LegacyPinFiles.PreVersioningFormat, version);
            Assert.Single(points);
        }

        [Fact]
        public void ParsePoints_SkipsMalformedLines()
        {
            var points = LegacyPinFiles.ParsePoints(new[]
            {
                "#v4",
                "",
                "too|few|fields",
                "x|2|0|0|0|Name|raw|resource%3AWood",
                "1|2|nan-ish|0|0|Name|raw|resource%3AWood",
                "1|2|0|0|0||raw|resource%3AWood",
                "1|3|1|2|3|Branch|pickable_branch|resource%3AWood"
            }, out _);

            var p = Assert.Single(points);
            Assert.Equal(3u, p.Id);
        }

        [Fact]
        public void ParseLocations_ReadsFields()
        {
            var locations = LegacyPinFiles.ParseLocations(new[]
            {
                "#locv1",
                "Crypt2%40100%2C200|100|30.5|200|Burial%20Chambers|crypt2|location%3ACrypt",
                "missing|fields"
            });

            var loc = Assert.Single(locations);
            Assert.Equal("Crypt2@100,200", loc.LocationKey);
            Assert.Equal(new Vector3(100, 30.5f, 200), loc.Position);
            Assert.Equal("Burial Chambers", loc.DisplayName);
            Assert.Equal("crypt2", loc.RawName);
            Assert.Equal("location:Crypt", loc.CategoryKey);
        }
    }
}
