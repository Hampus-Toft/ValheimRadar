using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimRadar
{
    // Persistence half of PinManager: loads a world's pin state from its SQLite database
    // (PinData/<world>.db, see Persistence/PinDatabase.cs) on connect, queues every change the rest of
    // PinManager makes, and writes the queue in one small transaction per FlushPersistence call
    // (RadarPlugin calls it every couple of seconds and on disconnect). Only stationary state is
    // persisted - creature positions are transient and re-discovered by scanning each session.
    //
    // Worlds saved by v1.11.0 and older used four pipe-delimited .txt files; OpenWorld imports any it
    // finds into the database once (through the same dedup/migration rules as everything else) and
    // deletes them after the import is committed.
    public static partial class PinManager
    {
        // Bumped whenever saved points need explicit migration/validation on load (see
        // MigrateLegacyCategoryKey), rather than just "new optional field". Stored as the database's
        // PinDatabase.PointsFormatKey meta value (formerly line 0 of <world>.txt; a text file with no
        // version line is LegacyPinFiles.PreVersioningFormat).
        // Bumped to 3 for the Chests -> Chests/BuriedChests split (see MigrateLegacyCategoryKey's
        // "Chests" case) so saves written by older builds get re-migrated even though their categoryKey
        // ("resource:Chests") is still a currently-valid id on its own.
        // Bumped to 4 when loose item drops (raw ore, ingots, dropped iron scrap) stopped being
        // tracked, so their saved points get dropped (see MigrateLegacyCategoryKey).
        private const int SaveFormatVersion = 4;

        private static string PinDataFolder => Path.Combine(Paths.ConfigPath, "ValheimRadar", "PinData");

        // Open database for the current world, or null when disconnected or when SQLite couldn't be
        // loaded (then pins work for the session but nothing is saved).
        private static PinDatabase database;

        // Changes waiting for the next FlushPersistence. A point saved then removed before a flush
        // (or vice versa) only keeps its latest state.
        private static readonly Dictionary<string, TrackedItem> pendingPointSaves = new Dictionary<string, TrackedItem>();
        private static readonly HashSet<string> pendingPointDeletes = new HashSet<string>();
        private static readonly Dictionary<string, TrackedLocation> pendingLocationSaves = new Dictionary<string, TrackedLocation>();
        private static bool dismissedSaveQueued;
        private static bool respawnSaveQueued;

        // Set while OpenWorld fills the in-memory stores, so loading doesn't queue every row it read.
        private static bool loadingWorld;

        private static bool loggedFlushFailure;

        // Loads everything saved for worldName (database first, then any legacy .txt files) into the
        // in-memory stores. Must run before RebuildPersistentClusters/DrawLoadedLocationPins on connect,
        // and does not touch the minimap itself - loaded points are drawn through the same path a live
        // scan uses.
        //
        // Deliberately does NOT check whether each point's ZDO still exists in ZDOMan (an earlier
        // version did): on a dedicated-server client ZDOMan only holds the sectors streamed so far, so
        // that check discarded - and then permanently deleted - every persisted point further away
        // (issue #42), and ZDOIDs are re-assigned whenever a server reloads its world anyway. Pins for
        // objects that no longer exist are the lesser evil next to losing valid ones; see
        // PersistedPointRules.
        public static void OpenWorld(string worldName)
        {
            CloseWorld();
            ClearRawPoints();
            rawLocationPoints.Clear();
            dismissedPins.Clear();
            respawnTimers.Clear();

            if (string.IsNullOrEmpty(worldName)) return;

            database = TryOpenDatabase(worldName);

            bool rewritePoints = false, rewriteLocations = false, rewriteDismissed = false, rewriteRespawn = false;
            var importedFiles = new List<string>();

            loadingWorld = true;
            try
            {
                if (database != null && !TryLoadDatabase(out rewritePoints))
                {
                    // Never write over a database we couldn't read - it's left as-is for next time.
                    database.Dispose();
                    database = null;
                }

                ImportLegacyFiles(worldName, importedFiles, ref rewritePoints, ref rewriteLocations, ref rewriteDismissed, ref rewriteRespawn);
            }
            finally
            {
                loadingWorld = false;
            }

            if (database == null) return; // legacy files stay untouched; nothing is saved this session

            try
            {
                database.InTransaction(() =>
                {
                    if (rewritePoints) database.ReplaceAllPoints(AllPointRecords());
                    database.SetMeta(PinDatabase.PointsFormatKey, SaveFormatVersion.ToString());
                    if (rewriteLocations) database.UpsertLocations(rawLocationPoints.Values);
                    if (rewriteDismissed) database.ReplaceDismissed(dismissedPins.Entries);
                    if (rewriteRespawn) database.ReplaceRespawnTimers(respawnTimers.Entries);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to write the pin database for '{worldName}' (old save files were kept): {ex.Message}");
                return;
            }

            foreach (string path in importedFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ValheimRadar] Imported '{path}' into the pin database but couldn't delete it: {ex.Message}");
                }
            }

            if (importedFiles.Count > 0)
            {
                Debug.Log($"[ValheimRadar] Moved {importedFiles.Count} old pin save file(s) for '{worldName}' into {Path.GetFileName(GetSaveFilePath(worldName, ".db"))} ({rawPersistentPoints.Count} resource points, {rawLocationPoints.Count} locations).");
            }
        }

        // Writes every queued change in one transaction. Cheap when nothing changed; a failure keeps
        // the queue for the next attempt.
        public static void FlushPersistence()
        {
            if (database == null) return;
            if (pendingPointSaves.Count == 0 && pendingPointDeletes.Count == 0 && pendingLocationSaves.Count == 0 && !dismissedSaveQueued && !respawnSaveQueued) return;

            try
            {
                database.InTransaction(() =>
                {
                    if (pendingPointDeletes.Count > 0) database.DeletePoints(pendingPointDeletes);
                    if (pendingPointSaves.Count > 0) database.UpsertPoints(ToRecords(pendingPointSaves));
                    if (pendingLocationSaves.Count > 0) database.UpsertLocations(pendingLocationSaves.Values);
                    if (dismissedSaveQueued) database.ReplaceDismissed(dismissedPins.Entries);
                    if (respawnSaveQueued) database.ReplaceRespawnTimers(respawnTimers.Entries);
                });

                ClearPersistenceQueue();
            }
            catch (Exception ex)
            {
                if (loggedFlushFailure) return;
                loggedFlushFailure = true;
                Debug.LogError($"[ValheimRadar] Failed to save pins (will keep retrying): {ex.Message}");
            }
        }

        // Flushes and closes the current world's database. Safe to call when none is open.
        public static void CloseWorld()
        {
            FlushPersistence();
            database?.Dispose();
            database = null;
            ClearPersistenceQueue();
        }

        private static void QueuePointSave(string key, TrackedItem item)
        {
            if (loadingWorld || database == null) return;
            pendingPointDeletes.Remove(key);
            pendingPointSaves[key] = item;
        }

        private static void QueuePointDelete(string key)
        {
            if (loadingWorld || database == null) return;
            pendingPointSaves.Remove(key);
            pendingPointDeletes.Add(key);
        }

        private static void QueueLocationSave(TrackedLocation loc)
        {
            if (loadingWorld || database == null) return;
            pendingLocationSaves[loc.LocationKey] = loc;
        }

        private static void QueueDismissedSave()
        {
            if (!loadingWorld && database != null) dismissedSaveQueued = true;
        }

        private static void QueueRespawnSave()
        {
            if (!loadingWorld && database != null) respawnSaveQueued = true;
        }

        private static void ClearPersistenceQueue()
        {
            pendingPointSaves.Clear();
            pendingPointDeletes.Clear();
            pendingLocationSaves.Clear();
            dismissedSaveQueued = false;
            respawnSaveQueued = false;
        }

        private static PinDatabase TryOpenDatabase(string worldName)
        {
            try
            {
                SqliteNative.Preload(Path.GetDirectoryName(typeof(PinManager).Assembly.Location));
                Directory.CreateDirectory(PinDataFolder);
                return PinDatabase.Open(GetSaveFilePath(worldName, ".db"));
            }
            catch (Exception ex)
            {
                // DllNotFoundException/EntryPointNotFoundException land here when the native library is
                // missing or the wrong build for this platform.
                Debug.LogError($"[ValheimRadar] Couldn't open the pin database, so pins won't be saved this session (existing save files are left untouched). The SQLite library (e_sqlite3.dll on Windows, libe_sqlite3.so on Linux) must sit next to ValheimRadar.dll. {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // Reads the open database into the in-memory stores. rewritePoints comes back true when loading
        // changed any point (migration, rename, duplicate dropped), so the points table gets rewritten.
        private static bool TryLoadDatabase(out bool rewritePoints)
        {
            rewritePoints = false;
            try
            {
                foreach (var entry in database.LoadDismissed()) dismissedPins.Add(entry.Key, entry.Value);
                foreach (var entry in database.LoadRespawnTimers()) respawnTimers.Add(entry.CategoryKey, entry.Position, entry.PickedAt, entry.RespawnAt);
                foreach (var loc in database.LoadLocations()) rawLocationPoints["loc:" + loc.LocationKey] = loc;

                int formatVersion = database.GetMetaInt(PinDatabase.PointsFormatKey, SaveFormatVersion);
                rewritePoints = IngestPoints(database.LoadPoints(), formatVersion);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to read the pin database (it was left untouched; pins won't be saved this session): {ex.Message}");
                ClearRawPoints();
                rawLocationPoints.Clear();
                dismissedPins.Clear();
                respawnTimers.Clear();
                return false;
            }
        }

        // Folds a world's v1.11.0-and-older .txt save files into the in-memory stores (deduped against
        // what the database already had) and lists the ones read, to be deleted once committed.
        private static void ImportLegacyFiles(string worldName, List<string> imported, ref bool rewritePoints, ref bool rewriteLocations, ref bool rewriteDismissed, ref bool rewriteRespawn)
        {
            string path = GetSaveFilePath(worldName, LegacyPinFiles.PointsSuffix);
            if (TryReadLegacyFile(path, out string[] lines))
            {
                IngestPoints(LegacyPinFiles.ParsePoints(lines, out int formatVersion), formatVersion);
                rewritePoints = true;
                imported.Add(path);
            }

            path = GetSaveFilePath(worldName, LegacyPinFiles.LocationsSuffix);
            if (TryReadLegacyFile(path, out lines))
            {
                foreach (TrackedLocation loc in LegacyPinFiles.ParseLocations(lines))
                {
                    string key = "loc:" + loc.LocationKey;
                    if (!rawLocationPoints.ContainsKey(key)) rawLocationPoints[key] = loc;
                }

                rewriteLocations = true;
                imported.Add(path);
            }

            path = GetSaveFilePath(worldName, LegacyPinFiles.DismissedSuffix);
            if (TryReadLegacyFile(path, out lines))
            {
                var legacy = new DismissedPinStore();
                legacy.Load(lines);
                foreach (var entry in legacy.Entries) dismissedPins.Add(entry.Key, entry.Value);

                rewriteDismissed = true;
                imported.Add(path);
            }

            path = GetSaveFilePath(worldName, LegacyPinFiles.RespawnSuffix);
            if (TryReadLegacyFile(path, out lines))
            {
                var legacy = new RespawnTimerStore();
                legacy.Load(lines);
                foreach (var entry in legacy.Entries) respawnTimers.Add(entry.CategoryKey, entry.Position, entry.PickedAt, entry.RespawnAt);

                rewriteRespawn = true;
                imported.Add(path);
            }
        }

        private static bool TryReadLegacyFile(string path, out string[] lines)
        {
            lines = null;
            if (!File.Exists(path)) return false;

            try
            {
                lines = File.ReadAllLines(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to read old pin save file '{path}' (left in place): {ex.Message}");
                return false;
            }
        }

        // Stores saved points through TryStoreRawPoint, so a reload applies exactly the identity rules a
        // live scan does. Returns true when what's stored differs from what was read: a category key
        // migrated or dropped (formatVersion older than SaveFormatVersion), a label re-derived, a
        // duplicate dropped, or a point stored under a different key.
        //
        // Duplicates come from saves written before position-based dedup (see IsDuplicatePosition): e.g.
        // a wild Beehive re-recorded on every relog because its ZDOID isn't session-stable. They're
        // dropped here and the rewrite heals the save.
        private static bool IngestPoints(List<PointRecord> records, int formatVersion)
        {
            bool changed = false;
            int migratedCount = 0;
            int unmigratableCount = 0;

            foreach (PointRecord record in records)
            {
                string categoryKey = record.CategoryKey;
                string displayName = record.DisplayName;
                if (string.IsNullOrEmpty(displayName)) { changed = true; continue; }

                if (formatVersion < SaveFormatVersion)
                {
                    changed = true;
                    if (!MigrateLegacyCategoryKey(categoryKey, record.RawName, out categoryKey))
                    {
                        unmigratableCount++;
                        continue;
                    }

                    migratedCount++;
                }

                // Labels fixed by a rule (e.g. ore deposits, renamed "Silver Deposit" -> "Silver") are
                // re-derived, so saved points keep deduping against rescanned ones by name.
                string currentName = ObjectEvaluator.GetResourceDisplayNameOverride(categoryKey, record.RawName);
                if (!string.IsNullOrEmpty(currentName) && currentName != displayName)
                {
                    displayName = currentName;
                    changed = true;
                }

                TrackedItem candidate = new TrackedItem
                {
                    Zdoid = new ZDOID(record.UserId, record.Id),
                    Position = record.Position,
                    DisplayName = displayName,
                    RawName = record.RawName,
                    IsPersistent = true,
                    CategoryKey = categoryKey
                };

                if (!TryStoreRawPoint(candidate, out string key))
                {
                    changed = true;
                    continue;
                }

                if (key != record.Key) changed = true;

                string iconPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
                string vanillaIcon = ObjectEvaluator.GetVanillaIconForCategory(categoryKey);
                candidate.Icon = string.IsNullOrEmpty(iconPng) ? null : ResolvePerObjectPin(record.RawName, iconPng, vanillaIcon);
            }

            if (migratedCount > 0 || unmigratableCount > 0)
            {
                RadarLog.Diag($"[ValheimRadar] pin-save-migrated fromVersion={formatVersion} migrated={migratedCount} dropped={unmigratableCount}");
            }

            return changed;
        }

        private static IEnumerable<PointRecord> AllPointRecords() => ToRecords(rawPersistentPoints);

        private static IEnumerable<PointRecord> ToRecords(Dictionary<string, TrackedItem> points)
        {
            foreach (var kvp in points)
            {
                TrackedItem item = kvp.Value;
                yield return new PointRecord
                {
                    Key = kvp.Key,
                    UserId = item.Zdoid.UserID,
                    Id = item.Zdoid.ID,
                    Position = item.Position,
                    DisplayName = item.DisplayName,
                    RawName = item.RawName,
                    CategoryKey = item.CategoryKey
                };
            }
        }

        private static string GetSaveFilePath(string worldName, string suffix)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] safeChars = new char[worldName.Length];
            for (int i = 0; i < worldName.Length; i++)
            {
                char c = worldName[i];
                safeChars[i] = Array.IndexOf(invalid, c) >= 0 ? '_' : c;
            }

            return Path.Combine(PinDataFolder, $"{new string(safeChars)}{suffix}");
        }


        // Re-maps a categoryKey from a save file written before the exact-prefab-whitelist rewrite
        // (see ObjectEvaluator.ResourceRules) onto its current equivalent, using the point's own
        // RawName as the tie-breaker wherever an old id fanned out into several new ones. Returns
        // false for anything with no sensible current equivalent (that point is dropped, not carried
        // forward as a stale/incorrect pin) - most notably every old "resource:Portals" point, since
        // player-built portals are no longer tracked at all, and "resource:Dungeons" (dungeon
        // detection changed from name-substring to component-signature, which can't be re-derived
        // from a saved RawName string alone without the live GameObject).
        private static bool MigrateLegacyCategoryKey(string oldCategoryKey, string rawName, out string newCategoryKey)
        {
            newCategoryKey = oldCategoryKey;

            if (string.IsNullOrEmpty(oldCategoryKey) || !oldCategoryKey.StartsWith("resource:", StringComparison.Ordinal))
            {
                // Creature keys ("creature:*") and anything already on the current resource id scheme
                // pass through unchanged - CreatureDefinitions gained entries (bosses/fish) but never
                // removed/renamed any existing canonical key, so no creature key can go stale here.
                return true;
            }

            string oldId = oldCategoryKey.Substring("resource:".Length);
            string cleanRaw = string.IsNullOrEmpty(rawName) ? string.Empty : rawName.ToLowerInvariant();

            switch (oldId)
            {
                // Each old single-bucket ore id covered both the world node and loose items (raw ore,
                // ingots). Only the node survives - use the saved raw prefab name (still exact) to
                // tell them apart, and drop everything else.
                case "Copper":
                    if (cleanRaw == "minerock_copper" || cleanRaw == "rock4_copper" || cleanRaw == "rock4_copper_frac") { newCategoryKey = "resource:CopperDeposit"; return true; }
                    return false;
                case "Tin":
                    if (cleanRaw == "minerock_tin") { newCategoryKey = "resource:TinDeposit"; return true; }
                    return false;
                case "Iron":
                    if (cleanRaw == "mudpile" || cleanRaw == "mudpile2") { newCategoryKey = "resource:IronScrap"; return true; }
                    return false;
                case "Silver":
                    if (cleanRaw == "silvervein" || cleanRaw == "silvervein_frac" || cleanRaw == "rock3_silver" || cleanRaw == "rock3_silver_frac") { newCategoryKey = "resource:SilverDeposit"; return true; }
                    return false;

                // Loose item drops (raw ore, ingots) are no longer tracked at all - only resource
                // nodes are (save format 4).
                case "CopperOre":
                case "CopperIngot":
                case "TinOre":
                case "TinIngot":
                case "IronIngot":
                case "SilverOre":
                case "SilverIngot":
                    return false;

                // Same id as the swamp scrap-pile node, but a dropped "IronScrap" item landed here too.
                case "IronScrap":
                    return cleanRaw != "ironscrap";

                // Chests split into above-ground (Chests) vs buried (BuriedChests) - re-derive from
                // the saved raw prefab name, which is still exact.
                case "Chests":
                    if (cleanRaw == "treasurechest_meadows_buried" || cleanRaw == "treasurechest_memorial_buried")
                    {
                        newCategoryKey = "resource:BuriedChests";
                    }
                    return true;

                // No natural equivalent exists - these points can't be carried forward.
                case "Portals":
                    return false;

                // Dungeon detection is now component-signature based (Teleport+DungeonGenerator),
                // which can't be re-derived from a saved RawName string without the live GameObject -
                // drop the stale point; a fresh scan of the same entrance re-adds it correctly.
                case "Dungeons":
                    return false;

                // Fully superseded by the ZoneSystem-based LocationDefinitions roster (see
                // Scanning/LocationScanner.cs) - these old resource-pipeline points are dropped here;
                // the next location-scan tick re-adds the same POIs correctly under "location:*" keys
                // in the separate rawLocationPoints store instead.
                case "DecorativeStatues":
                case "DrakeNest":
                case "TarPits":
                case "StoneRings":
                case "MistlandsPOI":
                    return false;

                default:
                    // Every other id (berries, mushrooms, crops, ground pickables, chests, beehives,
                    // runestones, stone rings, abandoned ruins, tar pits) kept its old id unchanged -
                    // pass through as-is.
                    return true;
            }
        }
    }
}
