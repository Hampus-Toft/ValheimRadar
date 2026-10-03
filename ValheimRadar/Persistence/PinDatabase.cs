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
        internal const int SchemaVersion = 1;

        // Meta key holding the PinManager.SaveFormatVersion the points table was last written with,
        // so category-key migrations keep working exactly as they did for the text format.
        internal const string PointsFormatKey = "points_format";

        private readonly SqliteConnection connection;

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
                connection.Execute("CREATE TABLE IF NOT EXISTS points (key TEXT PRIMARY KEY, user_id INTEGER NOT NULL, zdo_id INTEGER NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, display_name TEXT NOT NULL, raw_name TEXT NOT NULL, category TEXT NOT NULL)");
                connection.Execute("CREATE TABLE IF NOT EXISTS locations (location_key TEXT PRIMARY KEY, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, display_name TEXT NOT NULL, raw_name TEXT NOT NULL, category TEXT NOT NULL)");
                connection.Execute("CREATE TABLE IF NOT EXISTS dismissed (category TEXT NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL)");
                connection.Execute("CREATE TABLE IF NOT EXISTS respawn_timers (category TEXT NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, picked_at REAL NOT NULL, respawn_at REAL NOT NULL)");

                var db = new PinDatabase(connection);
                if (db.GetMeta("schema_version") == null) db.SetMeta("schema_version", SchemaVersion.ToString());
                return db;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public void Dispose() => connection.Dispose();

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

        internal List<PointRecord> LoadPoints()
        {
            var result = new List<PointRecord>();
            using (var s = connection.Prepare("SELECT key, user_id, zdo_id, x, y, z, display_name, raw_name, category FROM points"))
            {
                while (s.Step())
                {
                    result.Add(new PointRecord
                    {
                        Key = s.GetText(0),
                        UserId = s.GetInt64(1),
                        Id = (uint)s.GetInt64(2),
                        Position = new Vector3((float)s.GetDouble(3), (float)s.GetDouble(4), (float)s.GetDouble(5)),
                        DisplayName = s.GetText(6),
                        RawName = s.GetText(7),
                        CategoryKey = s.GetText(8)
                    });
                }
            }

            return result;
        }

        internal void UpsertPoints(IEnumerable<PointRecord> points)
        {
            using (var s = connection.Prepare("INSERT OR REPLACE INTO points (key, user_id, zdo_id, x, y, z, display_name, raw_name, category) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)"))
            {
                foreach (PointRecord p in points)
                {
                    s.Bind(1, p.Key).Bind(2, p.UserId).Bind(3, (long)p.Id)
                        .Bind(4, (double)p.Position.x).Bind(5, (double)p.Position.y).Bind(6, (double)p.Position.z)
                        .Bind(7, p.DisplayName).Bind(8, p.RawName).Bind(9, p.CategoryKey);
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
