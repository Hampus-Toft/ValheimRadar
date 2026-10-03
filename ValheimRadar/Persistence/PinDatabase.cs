using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // One recorded resource/physics-POI point as stored on disk (see PinManager.rawPersistentPoints).
    // Key is PinManager's raw point key (ZDOID-derived, possibly disambiguated) - the row identity.
    internal sealed class PointRecord
    {
        public string Key;
        public long UserId;
        public uint Id;
        public Vector3 Position;
        public string DisplayName;
        public string RawName;
        public string CategoryKey;
    }

    // A scan cell that has saved points, and how many (see PinDatabase.LoadPointCells).
    internal struct PointCell
    {
        public int X;
        public int Z;
        public int Count;
    }

    // A world's persisted pin state in one SQLite file (PinData/<world>.db): recorded resource points,
    // Locations, dismissed pins and respawn timers. Replaces the four pipe-delimited .txt files,
    // which were rewritten in full on every save (1.7 MB for a well-explored world, on the main
    // thread every 30 s while exploring, and the respawn file on every single pick). Here
    // PinManager writes only what changed, batched into one transaction per flush.
    //
    // WAL journaling with synchronous=NORMAL keeps each commit to a small sequential append without
    // an fsync; a crash can lose at most the last few commits, never corrupt the file.
    internal sealed class PinDatabase : IDisposable
    {
        // 1: v1.12.0. 2: points carry their scan cell (cell_x/cell_z, ScanGeometry's 64 m zone grid)
        // with an index, so PinManager can load a world cell by cell, nearest to the player first,
        // spread over frames (see PinManager.ContinueLoadingPoints) instead of all at once on connect.
        internal const int SchemaVersion = 2;

        // Meta key holding the PinManager.SaveFormatVersion the points table was last written with,
        // so category-key migrations keep working exactly as they did for the text format.
        internal const string PointsFormatKey = "points_format";

        private readonly SqliteConnection connection;

        // Reused for every per-cell read while a world loads.
        private SqliteStatement pointsInCell;

        private PinDatabase(SqliteConnection connection)
        {
            this.connection = connection;
        }

        internal static PinDatabase Open(string path)
        {
            SqliteConnection connection = SqliteConnection.Open(path);
            try
            {
                connection.Execute("PRAGMA journal_mode=WAL");
                connection.Execute("PRAGMA synchronous=NORMAL");
                connection.Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                connection.Execute("CREATE TABLE IF NOT EXISTS points (key TEXT PRIMARY KEY, user_id INTEGER NOT NULL, zdo_id INTEGER NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, display_name TEXT NOT NULL, raw_name TEXT NOT NULL, category TEXT NOT NULL, cell_x INTEGER NOT NULL DEFAULT 0, cell_z INTEGER NOT NULL DEFAULT 0)");
                connection.Execute("CREATE TABLE IF NOT EXISTS locations (location_key TEXT PRIMARY KEY, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, display_name TEXT NOT NULL, raw_name TEXT NOT NULL, category TEXT NOT NULL)");
                connection.Execute("CREATE TABLE IF NOT EXISTS dismissed (category TEXT NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL)");
                connection.Execute("CREATE TABLE IF NOT EXISTS respawn_timers (category TEXT NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, picked_at REAL NOT NULL, respawn_at REAL NOT NULL)");

                var db = new PinDatabase(connection);
                db.Migrate();
                connection.Execute("CREATE INDEX IF NOT EXISTS points_cell ON points (cell_x, cell_z)");
                return db;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        // Brings an older database up to SchemaVersion, all in one transaction (a failure leaves it as
        // it was). A brand-new file already has the current tables and just gets stamped.
        private void Migrate()
        {
            string stored = GetMeta("schema_version");
            if (stored == null)
            {
                SetMeta("schema_version", SchemaVersion.ToString());
                return;
            }

            if (!int.TryParse(stored, out int version) || version >= SchemaVersion) return;

            InTransaction(() =>
            {
                if (version < 2)
                {
                    // v1 -> v2: add the scan cell columns and fill them in from each point's position,
                    // using ScanGeometry.GetCellIndex's floor((v + 32) / 64). SQLite builds don't all
                    // ship floor(), so it's spelled out: CAST truncates toward zero, minus one for
                    // negative non-integers.
                    if (!HasColumn("points", "cell_x")) connection.Execute("ALTER TABLE points ADD COLUMN cell_x INTEGER NOT NULL DEFAULT 0");
                    if (!HasColumn("points", "cell_z")) connection.Execute("ALTER TABLE points ADD COLUMN cell_z INTEGER NOT NULL DEFAULT 0");
                    connection.Execute($"UPDATE points SET cell_x = {SqlCellIndex("x")}, cell_z = {SqlCellIndex("z")}");
                }

                SetMeta("schema_version", SchemaVersion.ToString());
            });
        }

        private static string SqlCellIndex(string column)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string v = $"(({column} + {(ScanGeometry.CellSize / 2f).ToString("0.0", inv)}) / {ScanGeometry.CellSize.ToString("0.0", inv)})";
            return $"(CAST({v} AS INTEGER) - ({v} < CAST({v} AS INTEGER)))";
        }

        private bool HasColumn(string table, string column)
        {
            using (var s = connection.Prepare($"PRAGMA table_info({table})"))
            {
                while (s.Step())
                {
                    if (s.GetText(1) == column) return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            pointsInCell?.Dispose();
            pointsInCell = null;
            connection.Dispose();
        }

        internal void InTransaction(Action action) => connection.InTransaction(action);

        // --- meta -------------------------------------------------------------------------------

        internal string GetMeta(string key)
        {
            using (var s = connection.Prepare("SELECT value FROM meta WHERE key = ?"))
            {
                s.Bind(1, key);
                return s.Step() ? s.GetText(0) : null;
            }
        }

        internal void SetMeta(string key, string value)
        {
            using (var s = connection.Prepare("INSERT OR REPLACE INTO meta (key, value) VALUES (?, ?)"))
            {
                s.Bind(1, key).Bind(2, value);
                s.StepToEnd();
            }
        }

        internal int GetMetaInt(string key, int fallback) =>
            int.TryParse(GetMeta(key), out int value) ? value : fallback;

        // --- points -----------------------------------------------------------------------------

        private const string PointColumns = "key, user_id, zdo_id, x, y, z, display_name, raw_name, category";

        internal List<PointRecord> LoadPoints()
        {
            var result = new List<PointRecord>();
            using (var s = connection.Prepare($"SELECT {PointColumns} FROM points"))
            {
                while (s.Step()) result.Add(ReadPoint(s));
            }

            return result;
        }

        // Every scan cell holding at least one point, with its point count.
        internal List<PointCell> LoadPointCells()
        {
            var result = new List<PointCell>();
            using (var s = connection.Prepare("SELECT cell_x, cell_z, COUNT(*) FROM points GROUP BY cell_x, cell_z"))
            {
                while (s.Step()) result.Add(new PointCell { X = (int)s.GetInt64(0), Z = (int)s.GetInt64(1), Count = (int)s.GetInt64(2) });
            }

            return result;
        }

        internal List<PointRecord> LoadPointsInCell(int cellX, int cellZ)
        {
            if (pointsInCell == null) pointsInCell = connection.Prepare($"SELECT {PointColumns} FROM points WHERE cell_x = ? AND cell_z = ?");

            var result = new List<PointRecord>();
            try
            {
                pointsInCell.Bind(1, cellX).Bind(2, cellZ);
                while (pointsInCell.Step()) result.Add(ReadPoint(pointsInCell));
            }
            finally
            {
                pointsInCell.Reset();
            }

            return result;
        }

        private static PointRecord ReadPoint(SqliteStatement s) => new PointRecord
        {
            Key = s.GetText(0),
            UserId = s.GetInt64(1),
            Id = (uint)s.GetInt64(2),
            Position = new Vector3((float)s.GetDouble(3), (float)s.GetDouble(4), (float)s.GetDouble(5)),
            DisplayName = s.GetText(6),
            RawName = s.GetText(7),
            CategoryKey = s.GetText(8)
        };

        internal void UpsertPoints(IEnumerable<PointRecord> points)
        {
            using (var s = connection.Prepare("INSERT OR REPLACE INTO points (key, user_id, zdo_id, x, y, z, display_name, raw_name, category, cell_x, cell_z) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)"))
            {
                foreach (PointRecord p in points)
                {
                    s.Bind(1, p.Key).Bind(2, p.UserId).Bind(3, (long)p.Id)
                        .Bind(4, (double)p.Position.x).Bind(5, (double)p.Position.y).Bind(6, (double)p.Position.z)
                        .Bind(7, p.DisplayName).Bind(8, p.RawName).Bind(9, p.CategoryKey)
                        .Bind(10, ScanGeometry.GetCellIndex(p.Position.x)).Bind(11, ScanGeometry.GetCellIndex(p.Position.z));
                    s.ExecuteAndReset();
                }
            }
        }

        internal void DeletePoints(IEnumerable<string> keys)
        {
            using (var s = connection.Prepare("DELETE FROM points WHERE key = ?"))
            {
                foreach (string key in keys)
                {
                    s.Bind(1, key);
                    s.ExecuteAndReset();
                }
            }
        }

        internal void ReplaceAllPoints(IEnumerable<PointRecord> points)
        {
            connection.Execute("DELETE FROM points");
            UpsertPoints(points);
        }

        // --- locations --------------------------------------------------------------------------

        internal List<TrackedLocation> LoadLocations()
        {
            var result = new List<TrackedLocation>();
            using (var s = connection.Prepare("SELECT location_key, x, y, z, display_name, raw_name, category FROM locations"))
            {
                while (s.Step())
                {
                    result.Add(new TrackedLocation
                    {
                        LocationKey = s.GetText(0),
                        Position = new Vector3((float)s.GetDouble(1), (float)s.GetDouble(2), (float)s.GetDouble(3)),
                        DisplayName = s.GetText(4),
                        RawName = s.GetText(5),
                        CategoryKey = s.GetText(6)
                    });
                }
            }

            return result;
        }

        internal void UpsertLocations(IEnumerable<TrackedLocation> locations)
        {
            using (var s = connection.Prepare("INSERT OR REPLACE INTO locations (location_key, x, y, z, display_name, raw_name, category) VALUES (?, ?, ?, ?, ?, ?, ?)"))
            {
                foreach (TrackedLocation loc in locations)
                {
                    s.Bind(1, loc.LocationKey)
                        .Bind(2, (double)loc.Position.x).Bind(3, (double)loc.Position.y).Bind(4, (double)loc.Position.z)
                        .Bind(5, loc.DisplayName).Bind(6, loc.RawName).Bind(7, loc.CategoryKey);
                    s.ExecuteAndReset();
                }
            }
        }

        // --- dismissed pins / respawn timers (small sets, replaced wholesale on change) ------------

        internal List<KeyValuePair<string, Vector3>> LoadDismissed()
        {
            var result = new List<KeyValuePair<string, Vector3>>();
            using (var s = connection.Prepare("SELECT category, x, y, z FROM dismissed"))
            {
                while (s.Step())
                {
                    result.Add(new KeyValuePair<string, Vector3>(s.GetText(0), new Vector3((float)s.GetDouble(1), (float)s.GetDouble(2), (float)s.GetDouble(3))));
                }
            }

            return result;
        }

        internal void ReplaceDismissed(IEnumerable<KeyValuePair<string, Vector3>> entries)
        {
            connection.Execute("DELETE FROM dismissed");
            using (var s = connection.Prepare("INSERT INTO dismissed (category, x, y, z) VALUES (?, ?, ?, ?)"))
            {
                foreach (var e in entries)
                {
                    s.Bind(1, e.Key).Bind(2, (double)e.Value.x).Bind(3, (double)e.Value.y).Bind(4, (double)e.Value.z);
                    s.ExecuteAndReset();
                }
            }
        }

        internal List<RespawnTimerStore.Entry> LoadRespawnTimers()
        {
            var result = new List<RespawnTimerStore.Entry>();
            using (var s = connection.Prepare("SELECT category, x, y, z, picked_at, respawn_at FROM respawn_timers"))
            {
                while (s.Step())
                {
                    result.Add(new RespawnTimerStore.Entry
                    {
                        CategoryKey = s.GetText(0),
                        Position = new Vector3((float)s.GetDouble(1), (float)s.GetDouble(2), (float)s.GetDouble(3)),
                        PickedAt = s.GetDouble(4),
                        RespawnAt = s.GetDouble(5)
                    });
                }
            }

            return result;
        }

        internal void ReplaceRespawnTimers(IEnumerable<RespawnTimerStore.Entry> entries)
        {
            connection.Execute("DELETE FROM respawn_timers");
            using (var s = connection.Prepare("INSERT INTO respawn_timers (category, x, y, z, picked_at, respawn_at) VALUES (?, ?, ?, ?, ?, ?)"))
            {
                foreach (var e in entries)
                {
                    s.Bind(1, e.CategoryKey).Bind(2, (double)e.Position.x).Bind(3, (double)e.Position.y).Bind(4, (double)e.Position.z)
                        .Bind(5, e.PickedAt).Bind(6, e.RespawnAt);
                    s.ExecuteAndReset();
                }
            }
        }
    }
}
