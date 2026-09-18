using Xunit;

namespace ValheimRadar.Tests.Scanning
{
    // Locks ScanGeometry's cell grid to Valheim's zone grid (ZoneSystem.GetZone = floor((v + 32) / 64),
    // ZoneSystem.GetZonePos = index * 64), verified by decompiling assembly_valheim.dll - if these ever
    // drift, cells start straddling multiple zones again and IsCellReady waits on the slowest of them.
    public class ScanGeometryTests
    {
        [Fact]
        public void CellSize_MatchesValheimZoneSize()
        {
            Assert.Equal(64f, ScanGeometry.CellSize);
        }

        [Theory]
        [InlineData(-96.01f, -2)]
        [InlineData(-32.01f, -1)]
        [InlineData(-32f, 0)]
        [InlineData(0f, 0)]
        [InlineData(31.99f, 0)]
        [InlineData(32f, 1)]
        [InlineData(96f, 2)]
        public void GetCellIndex_MatchesZoneBoundaries(float world, int expectedIndex)
        {
            Assert.Equal(expectedIndex, ScanGeometry.GetCellIndex(world));
        }

        [Theory]
        [InlineData(-3, -192f)]
        [InlineData(0, 0f)]
        [InlineData(1, 64f)]
        [InlineData(5, 320f)]
        public void GetCellCenter_IsZoneCenter(int index, float expectedCenter)
        {
            Assert.Equal(expectedCenter, ScanGeometry.GetCellCenter(index));
        }

        [Theory]
        [InlineData(-4)]
        [InlineData(0)]
        [InlineData(7)]
        public void CellSpan_MapsEntirelyToItsOwnIndex(int index)
        {
            float center = ScanGeometry.GetCellCenter(index);
            float half = ScanGeometry.CellSize / 2f;

            // Inclusive lower edge, just-inside upper edge - the cell must never straddle a zone.
            Assert.Equal(index, ScanGeometry.GetCellIndex(center - half));
            Assert.Equal(index, ScanGeometry.GetCellIndex(center));
            Assert.Equal(index, ScanGeometry.GetCellIndex(center + half - 0.01f));
        }
    }
}
