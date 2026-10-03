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
    }
}
