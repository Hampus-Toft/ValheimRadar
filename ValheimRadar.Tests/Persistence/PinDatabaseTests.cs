using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Persistence
{
    // Runs against the real native SQLite (SQLitePCLRaw.lib.e_sqlite3), one temp file per test.
    public class PinDatabaseTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "ValheimRadarTests", Guid.NewGuid().ToString("N"));
        private string DbPath => Path.Combine(dir, "World.db");

        public PinDatabaseTests()
        {
            Directory.CreateDirectory(dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp dir */ }
        }

        private static PointRecord Point(string key, float x, string category = "resource:Blueberry", string name = "Blueberry Bush") => new PointRecord
        {
            Key = key,
            UserId = 1,
            Id = 4000000000u,
            Position = new Vector3(x, 12.5f, -x),
            DisplayName = name,
            RawName = "blueberrybush",
            CategoryKey = category
        };

        [Fact]
        public void Open_CreatesFileWithSchemaVersion()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                Assert.Equal(PinDatabase.SchemaVersion, db.GetMetaInt("schema_version", -1));
            }

            Assert.True(File.Exists(DbPath));
        }

        [Fact]
        public void Points_RoundTripAcrossReopen()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.InTransaction(() => db.UpsertPoints(new[] { Point("1:1", 10.25f), Point("1:2", -3.5f, name: "Smörbär \"|%") }));
            }

            using (var db = PinDatabase.Open(DbPath))
            {
                var points = db.LoadPoints().OrderBy(p => p.Key).ToList();

                Assert.Equal(2, points.Count);
                Assert.Equal("1:1", points[0].Key);
                Assert.Equal(1, points[0].UserId);
                Assert.Equal(4000000000u, points[0].Id);
                Assert.Equal(new Vector3(10.25f, 12.5f, -10.25f), points[0].Position);
                Assert.Equal("blueberrybush", points[0].RawName);
                Assert.Equal("resource:Blueberry", points[0].CategoryKey);
                Assert.Equal("Smörbär \"|%", points[1].DisplayName);
            }
        }

        [Fact]
        public void UpsertPoints_SameKey_ReplacesRow()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.UpsertPoints(new[] { Point("1:1", 1f) });
                db.UpsertPoints(new[] { Point("1:1", 2f, name: "Renamed") });

                var points = db.LoadPoints();
                Assert.Single(points);
                Assert.Equal("Renamed", points[0].DisplayName);
                Assert.Equal(2f, points[0].Position.x);
            }
        }

        [Fact]
        public void DeletePoints_RemovesOnlyThoseKeys()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.UpsertPoints(new[] { Point("a", 1f), Point("b", 2f), Point("c", 3f) });
                db.DeletePoints(new[] { "a", "c", "missing" });

                Assert.Equal(new[] { "b" }, db.LoadPoints().Select(p => p.Key));
            }
        }

        [Fact]
        public void ReplaceAllPoints_DropsRowsNotGiven()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.UpsertPoints(new[] { Point("a", 1f), Point("b", 2f) });
                db.ReplaceAllPoints(new[] { Point("c", 3f) });

                Assert.Equal(new[] { "c" }, db.LoadPoints().Select(p => p.Key));
            }
        }

        [Fact]
        public void InTransaction_Throwing_RollsBack()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.UpsertPoints(new[] { Point("kept", 1f) });

                Assert.Throws<InvalidOperationException>(() => db.InTransaction(() =>
                {
                    db.UpsertPoints(new[] { Point("rolled-back", 2f) });
                    throw new InvalidOperationException("boom");
                }));

                Assert.Equal(new[] { "kept" }, db.LoadPoints().Select(p => p.Key));
            }
        }

        [Fact]
        public void Locations_UpsertAndLoad()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.UpsertLocations(new[]
                {
                    new TrackedLocation { LocationKey = "Crypt2@100,200", Position = new Vector3(100, 30, 200), DisplayName = "Burial Chambers", RawName = "crypt2", CategoryKey = "location:Crypt" }
                });

                var loc = Assert.Single(db.LoadLocations());
                Assert.Equal("Crypt2@100,200", loc.LocationKey);
                Assert.Equal(new Vector3(100, 30, 200), loc.Position);
                Assert.Equal("Burial Chambers", loc.DisplayName);
                Assert.Equal("location:Crypt", loc.CategoryKey);
            }
        }

        [Fact]
        public void Dismissed_ReplaceWholesale()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.ReplaceDismissed(new[] { new KeyValuePair<string, Vector3>("resource:Raspberry", new Vector3(1, 2, 3)), new KeyValuePair<string, Vector3>("location:Crypt", Vector3.zero) });
                db.ReplaceDismissed(new[] { new KeyValuePair<string, Vector3>("resource:Flint", new Vector3(4, 5, 6)) });

                var entry = Assert.Single(db.LoadDismissed());
                Assert.Equal("resource:Flint", entry.Key);
                Assert.Equal(new Vector3(4, 5, 6), entry.Value);
            }
        }

        [Fact]
        public void RespawnTimers_ReplaceWholesaleKeepsDoublePrecision()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.ReplaceRespawnTimers(new[] { new RespawnTimerStore.Entry { CategoryKey = "resource:Thistle", Position = new Vector3(7, 8, 9), PickedAt = 1018740.1234567, RespawnAt = 1033140.1234567 } });

                var entry = Assert.Single(db.LoadRespawnTimers());
                Assert.Equal("resource:Thistle", entry.CategoryKey);
                Assert.Equal(1018740.1234567, entry.PickedAt);
                Assert.Equal(1033140.1234567, entry.RespawnAt);
            }
        }

        [Fact]
        public void Meta_SetAndGet()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                Assert.Equal(7, db.GetMetaInt(PinDatabase.PointsFormatKey, 7));
                db.SetMeta(PinDatabase.PointsFormatKey, "4");
                Assert.Equal(4, db.GetMetaInt(PinDatabase.PointsFormatKey, 7));
            }
        }

        // The size of a well-explored world (~21.5k points) must stay cheap to write and read.
        [Fact]
        public void LargeWorld_ReplaceAndLoad_AreFast()
        {
            var points = Enumerable.Range(0, 21553).Select(i => Point("1:" + i, i * 0.1f)).ToList();

            using (var db = PinDatabase.Open(DbPath))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                db.InTransaction(() => db.ReplaceAllPoints(points));
                var write = sw.Elapsed;

                sw.Restart();
                int count = db.LoadPoints().Count;
                var read = sw.Elapsed;

                Assert.Equal(points.Count, count);
                Assert.True(write.TotalSeconds < 5, $"write took {write.TotalMilliseconds} ms");
                Assert.True(read.TotalSeconds < 5, $"read took {read.TotalMilliseconds} ms");
            }
        }

        // Coordinates around zone boundaries, including negative ones, where truncation and floor differ.
        private static readonly float[] BoundaryCoordinates = { 0f, 31.9f, 32f, -32f, -32.5f, -96f, -96.1f, 95.99f, 1000.25f, -4100.7f };

        [Fact]
        public void UpsertPoints_StoresEachPointsScanCell()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                var points = BoundaryCoordinates.Select((x, i) => Point("1:" + i, x)).ToList();
                db.InTransaction(() => db.UpsertPoints(points));

                AssertCellsMatchScanGeometry(db, points);
            }
        }

        // A v1.12.0 (schema 1) database - no cell columns - is upgraded in place on open: rows kept,
        // cells filled in exactly as ScanGeometry.GetCellIndex computes them.
        [Fact]
        public void Open_MigratesSchema1Database()
        {
            var points = BoundaryCoordinates.Select((x, i) => Point("1:" + i, x, name: "Bär " + i)).ToList();

            using (var v1 = SqliteConnection.Open(DbPath))
            {
                v1.Execute("CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                v1.Execute("CREATE TABLE points (key TEXT PRIMARY KEY, user_id INTEGER NOT NULL, zdo_id INTEGER NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, display_name TEXT NOT NULL, raw_name TEXT NOT NULL, category TEXT NOT NULL)");
                v1.Execute("INSERT INTO meta (key, value) VALUES ('schema_version', '1')");
                v1.Execute("INSERT INTO meta (key, value) VALUES ('points_format', '4')");
                foreach (var p in points)
                {
                    using (var s = v1.Prepare("INSERT INTO points (key, user_id, zdo_id, x, y, z, display_name, raw_name, category) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)"))
                    {
                        s.Bind(1, p.Key).Bind(2, p.UserId).Bind(3, (long)p.Id)
                            .Bind(4, (double)p.Position.x).Bind(5, (double)p.Position.y).Bind(6, (double)p.Position.z)
                            .Bind(7, p.DisplayName).Bind(8, p.RawName).Bind(9, p.CategoryKey);
                        s.ExecuteAndReset();
                    }
                }
            }

            using (var db = PinDatabase.Open(DbPath))
            {
                Assert.Equal(PinDatabase.SchemaVersion, db.GetMetaInt("schema_version", -1));
                Assert.Equal(4, db.GetMetaInt(PinDatabase.PointsFormatKey, -1));

                var loaded = db.LoadPoints().OrderBy(p => p.Key).ToList();
                Assert.Equal(points.Count, loaded.Count);
                foreach (var expected in points)
                {
                    var actual = loaded.Single(p => p.Key == expected.Key);
                    Assert.Equal(expected.Position, actual.Position);
                    Assert.Equal(expected.DisplayName, actual.DisplayName);
                }

                AssertCellsMatchScanGeometry(db, points);
            }

            // Reopening an already-migrated file changes nothing.
            using (var db = PinDatabase.Open(DbPath))
            {
                Assert.Equal(PinDatabase.SchemaVersion, db.GetMetaInt("schema_version", -1));
                Assert.Equal(points.Count, db.LoadPoints().Count);
            }
        }

        [Fact]
        public void LoadPointsInCell_ReturnsOnlyThatCellsPoints_AndCanBeCalledRepeatedly()
        {
            using (var db = PinDatabase.Open(DbPath))
            {
                db.InTransaction(() => db.UpsertPoints(new[] { Point("1:1", 10f), Point("1:2", 20f), Point("1:3", 200f) }));

                for (int round = 0; round < 3; round++)
                {
                    Assert.Equal(new[] { "1:1", "1:2" }, db.LoadPointsInCell(0, 0).Select(p => p.Key).OrderBy(k => k));
                    Assert.Equal(new[] { "1:3" }, db.LoadPointsInCell(3, -3).Select(p => p.Key));
                    Assert.Empty(db.LoadPointsInCell(5, 5));
                }

                var cells = db.LoadPointCells().OrderBy(c => c.X).ToList();
                Assert.Equal(2, cells.Count);
                Assert.Equal((0, 0, 2), (cells[0].X, cells[0].Z, cells[0].Count));
                Assert.Equal((3, -3, 1), (cells[1].X, cells[1].Z, cells[1].Count));
            }
        }

        private static void AssertCellsMatchScanGeometry(PinDatabase db, List<PointRecord> points)
        {
            var cells = db.LoadPointCells();
            Assert.Equal(points.Count, cells.Sum(c => c.Count));

            foreach (var p in points)
            {
                int cx = ScanGeometry.GetCellIndex(p.Position.x);
                int cz = ScanGeometry.GetCellIndex(p.Position.z);
                Assert.Contains(db.LoadPointsInCell(cx, cz), r => r.Key == p.Key);
            }
        }
    }
}
