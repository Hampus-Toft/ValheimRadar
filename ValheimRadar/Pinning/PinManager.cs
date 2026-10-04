using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using System.IO;
using UnityEngine;

namespace ValheimRadar
{
    public static partial class PinManager
    {
        private class PinEntry
        {
            public Minimap.PinData Pin; // null while hidden (category currently disabled)
            public bool IsPersistent;
            public string CategoryKey;
            public Vector3 Position;
            public string Label;
            public string DisplayName;
            public string RawName;
            public Sprite Icon;
        }

        private static readonly Dictionary<string, PinEntry> activeClusterPins = new Dictionary<string, PinEntry>();

        // Every persistent (resource/structure) point discovered and still believed to exist, keyed by
        // its ZDOID (a hint, not proof of identity - see PersistedPointRules/TryStoreRawPoint). Source
        // of truth for the persistent clusters (clusterGrid), which is what pins are drawn from (see
        // RecordRawPoints and PinManager.Viewport.cs). Points are recorded regardless of whether their
        // category is currently enabled (see ObjectEvaluator) so re-enabling a category later
        // immediately repopulates already-scanned ground instead of requiring the player to walk it
        // again. Depletable points leave the store once a rescan confirms they're gone (see
        // RecordScannedCells). Only ever modified through AddRawPoint/RemoveRawPoint/ClearRawPoints
        // so rawPointsByCell stays in step.
        private static readonly Dictionary<string, TrackedItem> rawPersistentPoints = new Dictionary<string, TrackedItem>();

        // The same points bucketed by scan cell (ScanGeometry's 64 m zone grid), so duplicate checks
        // and rescan reconciliation only look at the few points near a position instead of the whole
        // world's history - rescans re-report every object in a cell, so this runs far more often
        // than when each cell was only ever scanned once.
        private static readonly Dictionary<ScanCellKey, Dictionary<string, TrackedItem>> rawPointsByCell = new Dictionary<ScanCellKey, Dictionary<string, TrackedItem>>();

        // Every world Location ever discovered this session via LocationScanner, keyed by
        // "loc:" + TrackedLocation.LocationKey. A completely separate store from
        // rawPersistentPoints/PinData save file above - Locations have no ZDOID, so they can't go
        // through RawPointKey (see
        // Models/TrackedLocation.cs). Each entry is already a single unique point (one Location = one
        // pin), so unlike rawPersistentPoints there's no clustering pass here either.
        private static readonly Dictionary<string, TrackedLocation> rawLocationPoints = new Dictionary<string, TrackedLocation>();

        // maxDistance the persistent clusters (clusterGrid, see PinManager.Viewport.cs) were built for.
        // A mismatch (ClusterDistance config change, or reset to -1 to force it) starts a
        // RebuildPersistentClusters on the next RecordRawPoints call.
        private static float clusteredMaxDistance = -1f;

        // A newly-recorded point is treated as "already known" if it lands within
        // PersistedPointRules.DuplicatePointRadius of an existing point of the same DisplayName, even
        // when its ZDOID doesn't match any recorded key - ZDOIDs aren't stable across game/server
        // sessions (observed with wild Beehives; see PersistedPointRules for why), so position is
        // the identity that actually persists.

        // Pins the player dismissed by right-clicking them (see TryDismissPinAt). Recorded per point,
        // not per cluster, and consulted whenever raw points are (re)clustered or Locations are drawn, so
        // a dismissed pin never comes back on a later scan tick, recluster or relog. Persisted in the
        // world database's dismissed table (see PinManager.Persistence.cs).
        private static readonly DismissedPinStore dismissedPins = new DismissedPinStore();

        // Regrowing pickables the local player picked, hidden until they grow back (see MarkPickedAt /
        // UpdateRespawnTimers). Like dismissals they're excluded from clustering while hidden, but the
        // raw point stays recorded. Persisted in the world database's respawn_timers table.
        private static readonly RespawnTimerStore respawnTimers = new RespawnTimerStore();

        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "ValheimRadar");

        // Removes every live pin from the minimap and drops all in-memory tracking state. Called on
        // disconnect/world unload so state never leaks across sessions/worlds; flushes and closes the
        // world's database first (see CloseWorld).
        public static void ClearAllPins()
        {
            CloseWorld();

            if (activeClusterPins.Count > 0 || resourcePins.Count > 0)
            {
                RadarLog.Diag($"[ValheimRadar] pin-removed-all count={activeClusterPins.Count + resourcePins.Count}");
            }

            var pins = new List<Minimap.PinData>();
            foreach (var entry in activeClusterPins.Values)
            {
                if (entry.Pin != null) pins.Add(entry.Pin);
            }

            if (Minimap.instance != null) MinimapPinBulk.RemovePins(Minimap.instance, pins);

            activeClusterPins.Clear();
            ResetPersistentClusters(Minimap.instance);
            ClearRawPoints();
            rawLocationPoints.Clear();
            dismissedPins.Clear();
            respawnTimers.Clear();
        }

        // A recorded point that's kept but not drawn: dismissed by the player, or picked and regrowing.
        private static bool IsHidden(TrackedItem point) =>
            dismissedPins.Contains(point.CategoryKey, point.Position) || respawnTimers.Contains(point.CategoryKey, point.Position);

        // Merges newly-scanned persistent points into the durable raw store, keyed by ZDOID so the
        // same stationary world object is never recorded twice, and folds each genuinely-new point
        // into the persistent clusters incrementally. Transient (creature) items are ignored here - see
        // the class comment on rawPersistentPoints.
        public static void RecordRawPoints(List<TrackedItem> items, float maxDistance)
        {
            if (maxDistance != clusteredMaxDistance)
            {
                RebuildPersistentClusters(maxDistance);
            }

            foreach (var item in items)
            {
                if (!item.IsPersistent) continue;

                if (!TryStoreRawPoint(item)) continue;

                // Still recorded above (so it's never re-discovered as "new"), just never clustered/pinned.
                if (IsHidden(item)) continue;

                AddToClusters(item);
            }
        }

        // Records one PermanentSpatialScanner pass: every item found goes through RecordRawPoints
        // (already-known points dedup there), then - when removeDepleted is on - every depletable
        // recorded point inside a verified cell that this pass did NOT find gets a miss, and is
        // removed for good once DepletionRules says the misses are conclusive. That's how resources
        // mined out by other players, or picked up by this one, disappear from the map.
        internal static void RecordScannedCells(List<ScannedCell> cells, float maxDistance, float now, bool removeDepleted)
        {
            List<TrackedItem> found = new List<TrackedItem>();
            foreach (var cell in cells) found.AddRange(cell.Items);

            // Called even with nothing found - RecordRawPoints also picks up ClusterDistance changes.
            RecordRawPoints(found, maxDistance);

            if (removeDepleted) RemoveMissingDepletedPoints(cells, now);
            RemovePlantedCropPoints(cells);
        }

        // Crop points recorded before planted crops were filtered out (see
        // ResourceEvaluator.SaplingGrownPrefabs): drop any point of a sapling-grown prefab in a
        // verified cell that sits on cultivated ground. A verified
        // cell is loaded, so its terrain - and the cultivation paint - is there to check.
        private static void RemovePlantedCropPoints(List<ScannedCell> cells)
        {
            List<string> planted = null;

            foreach (var cell in cells)
            {
                if (!cell.Verified) continue;
                if (!rawPointsByCell.TryGetValue(cell.Key, out var bucket)) continue;

                foreach (var kvp in bucket)
                {
                    if (!ResourceEvaluator.IsSaplingGrown(kvp.Value.RawName)) continue;
                    if (!ScanFilters.IsOnCultivatedGround(kvp.Value.Position)) continue;
                    (planted ?? (planted = new List<string>())).Add(kvp.Key);
                }
            }

            if (planted == null) return;

            bool dismissalsChanged = false;
            foreach (string key in planted)
            {
                TrackedItem point = rawPersistentPoints[key];
                RemoveRawPoint(key);
                DetachFromCluster(point, "planted");
                dismissalsChanged |= dismissedPins.Remove(point.CategoryKey, point.Position);

                RadarLog.Diag($"[ValheimRadar] point-planted key={key} name={point.DisplayName} pos={point.Position.x:F1},{point.Position.y:F1},{point.Position.z:F1}");
            }

            if (dismissalsChanged) QueueDismissedSave();
        }

        private static void RemoveMissingDepletedPoints(List<ScannedCell> cells, float now)
        {
            List<string> gone = null;

            foreach (var cell in cells)
            {
                if (!cell.Verified) continue;
                if (!rawPointsByCell.TryGetValue(cell.Key, out var bucket)) continue;

                foreach (var kvp in bucket)
                {
                    TrackedItem point = kvp.Value;
                    if (!ObjectEvaluator.IsCategoryDepletable(point.CategoryKey)) continue;
                    if (!DepletionRules.IsWithinVerifiedHeight(point.Position.y, cell.MinY, cell.MaxY)) continue;

                    DepletionRules.RecordObservation(point, DepletionRules.IsPresent(point.CategoryKey, point.Position, cell.Items), now);
                    if (DepletionRules.ShouldRemove(point, now)) (gone ?? (gone = new List<string>())).Add(kvp.Key);
                }
            }

            if (gone == null) return;

            bool dismissalsChanged = false;
            foreach (string key in gone)
            {
                TrackedItem point = rawPersistentPoints[key];
                RemoveRawPoint(key);
                DetachFromCluster(point, "depleted");

                // A dismissal (manual, or from mining it - see MarkDepletedAt) has nothing left to hide.
                dismissalsChanged |= dismissedPins.Remove(point.CategoryKey, point.Position);

                RadarLog.Diag($"[ValheimRadar] point-depleted key={key} name={point.DisplayName} pos={point.Position.x:F1},{point.Position.y:F1},{point.Position.z:F1}");
            }

            if (dismissalsChanged) QueueDismissedSave();
        }

        // The local player just mined/picked a depletable object at position (see DepletionPatches) -
        // hide the recorded point for it straight away rather than waiting for rescans to confirm it's
        // gone. categoryKey is the object's own category, or null when the hit object was the "_frac"
        // debris a deposit turns into (then any depletable category matches). Recorded as a
        // dismissal, not a deletion: a MineRock such as an obsidian deposit keeps existing after the
        // first hit, and a rescan re-finding it must not bring the pin back. The raw point itself is
        // kept until a rescan confirms the object is gone, which then also drops the dismissal.
        internal static void MarkDepletedAt(string categoryKey, Vector3 position)
        {
            var candidates = new List<TrackedItem>();
            var positions = new List<Vector3>();
            foreach (TrackedItem point in RawPointsNear(position, DepletionRules.HitMatchRadius))
            {
                if (categoryKey != null ? point.CategoryKey != categoryKey : !ObjectEvaluator.IsCategoryDepletable(point.CategoryKey)) continue;
                if (dismissedPins.Contains(point.CategoryKey, point.Position)) continue;

                candidates.Add(point);
                positions.Add(point.Position);
            }

            int index = PinDismissal.IndexOfClosest(positions, position, DepletionRules.HitMatchRadius);
            if (index < 0) return;

            TrackedItem mined = candidates[index];
            dismissedPins.Add(mined.CategoryKey, mined.Position);
            DetachFromCluster(mined, "mined");
            QueueDismissedSave();

            RadarLog.Diag($"[ValheimRadar] point-mined name={mined.DisplayName} pos={mined.Position.x:F1},{mined.Position.y:F1},{mined.Position.z:F1}");
        }

        // The recorded point of this category closest to position (the picked object's root), or null.
        // Already-hidden points are deliberately candidates too: regrowing pickables often grow less
        // than a metre apart, and skipping a hidden one would hand the pick to its neighbour.
        private static TrackedItem FindPickedPoint(string categoryKey, Vector3 position)
        {
            var candidates = new List<TrackedItem>();
            var positions = new List<Vector3>();
            foreach (TrackedItem point in RawPointsNear(position, DepletionRules.HitMatchRadius))
            {
                if (point.CategoryKey != categoryKey) continue;

                candidates.Add(point);
                positions.Add(point.Position);
            }

            int index = PinDismissal.IndexOfClosest(positions, position, DepletionRules.HitMatchRadius);
            return index < 0 ? null : candidates[index];
        }

        // A regrowing pickable at position was just picked, by anyone (see DepletionPatches) - hide its
        // recorded point until respawnAt (world time, see RespawnTimerStore), when UpdateRespawnTimers
        // or MarkRegrownAt puts it back on the map. Points not recorded yet are ignored.
        internal static void MarkPickedAt(string categoryKey, Vector3 position, double pickedAt, double respawnAt)
        {
            TrackedItem picked = FindPickedPoint(categoryKey, position);
            if (picked == null || IsHidden(picked)) return;

            respawnTimers.Add(picked.CategoryKey, picked.Position, pickedAt, respawnAt);
            DetachFromCluster(picked, "picked");
            QueueRespawnSave();

            RadarLog.Diag($"[ValheimRadar] point-picked name={picked.DisplayName} respawnInSeconds={respawnAt - pickedAt:F0} pos={picked.Position.x:F1},{picked.Position.y:F1},{picked.Position.z:F1}");
        }

        // Puts every picked pickable whose respawn time has passed (world time now) back into the
        // persistent clusters; the next Tick redraws it. Called every scan tick.
        internal static void UpdateRespawnTimers(double now)
        {
            if (respawnTimers.Count == 0) return;

            List<RespawnTimerStore.Entry> expired = respawnTimers.TakeExpired(now);
            if (expired == null) return;

            foreach (var entry in expired)
            {
                foreach (TrackedItem point in RawPointsNear(entry.Position, RespawnTimerStore.MatchRadius))
                {
                    if (point.CategoryKey != entry.CategoryKey || DepletionRules.DistanceXZ(point.Position, entry.Position) > RespawnTimerStore.MatchRadius) continue;
                    ShowRegrownPoint(point);
                }
            }

            QueueRespawnSave();
        }

        // The owner reported the pickable at position grew back (Pickable.SetPicked(false)) - show it
        // now, even if our own timer hasn't run out yet.
        internal static void MarkRegrownAt(string categoryKey, Vector3 position)
        {
            TrackedItem point = FindPickedPoint(categoryKey, position);
            if (point == null || !respawnTimers.Remove(point.CategoryKey, point.Position)) return;

            ShowRegrownPoint(point);
            QueueRespawnSave();
        }

        private static void ShowRegrownPoint(TrackedItem point)
        {
            if (IsHidden(point)) return;

            // No clustering yet (a rebuild is pending) - that rebuild picks the point up.
            if (clusteredMaxDistance >= 0f) AddToClusters(point);

            RadarLog.Diag($"[ValheimRadar] point-respawned name={point.DisplayName} pos={point.Position.x:F1},{point.Position.y:F1},{point.Position.z:F1}");
        }

        // Shows every pickable currently hidden by a respawn timer again (the feature was turned off).
        public static void ClearRespawnTimers()
        {
            if (respawnTimers.Count == 0) return;

            respawnTimers.Clear();
            QueueRespawnSave();
            clusteredMaxDistance = -1f; // next RecordRawPoints rebuilds and resyncs every cluster
        }


        private static string RawPointKey(ZDOID zdoid) => $"{zdoid.UserID}:{zdoid.ID}";

        private static ScanCellKey CellOf(Vector3 position) =>
            new ScanCellKey(ScanGeometry.GetCellIndex(position.x), ScanGeometry.GetCellIndex(position.z));

        private static void AddRawPoint(string key, TrackedItem item)
        {
            rawPersistentPoints[key] = item;

            ScanCellKey cell = CellOf(item.Position);
            if (!rawPointsByCell.TryGetValue(cell, out var bucket))
            {
                bucket = new Dictionary<string, TrackedItem>();
                rawPointsByCell[cell] = bucket;
            }

            bucket[key] = item;
            QueuePointSave(key, item);
        }

        private static void RemoveRawPoint(string key)
        {
            if (!rawPersistentPoints.TryGetValue(key, out TrackedItem item)) return;
            rawPersistentPoints.Remove(key);

            ScanCellKey cell = CellOf(item.Position);
            if (rawPointsByCell.TryGetValue(cell, out var bucket))
            {
                bucket.Remove(key);
                if (bucket.Count == 0) rawPointsByCell.Remove(cell);
            }

            QueuePointDelete(key);
        }

        private static void ClearRawPoints()
        {
            rawPersistentPoints.Clear();
            rawPointsByCell.Clear();
        }

        // Recorded points in every cell touched by the square of +-radius around position (callers
        // apply their own exact distance test).
        private static IEnumerable<TrackedItem> RawPointsNear(Vector3 position, float radius)
        {
            int minX = ScanGeometry.GetCellIndex(position.x - radius);
            int maxX = ScanGeometry.GetCellIndex(position.x + radius);
            int minZ = ScanGeometry.GetCellIndex(position.z - radius);
            int maxZ = ScanGeometry.GetCellIndex(position.z + radius);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!rawPointsByCell.TryGetValue(new ScanCellKey(x, z), out var bucket)) continue;
                    foreach (TrackedItem point in bucket.Values) yield return point;
                }
            }
        }

        private static bool IsDuplicatePosition(TrackedItem item)
        {
            foreach (var existing in RawPointsNear(item.Position, PersistedPointRules.DuplicatePointRadius))
            {
                if (PersistedPointRules.IsDuplicate(existing.DisplayName, existing.Position, item.DisplayName, item.Position))
                {
                    return true;
                }
            }

            return false;
        }

        // Adds item to rawPersistentPoints unless it's already known; returns whether it was added.
        // Shared by live scans (RecordRawPoints) and world loads (OpenWorld) so both apply the
        // exact same identity rules. The ZDOID-derived key is only a hint - ZDOIDs are re-assigned
        // when a server reloads its world, so a key match only means "same object" if the position
        // agrees too; otherwise the point is new and is stored under a disambiguated key instead of
        // being silently skipped. See PersistedPointRules.
        private static bool TryStoreRawPoint(TrackedItem item) => TryStoreRawPoint(item, out _);

        private static bool TryStoreRawPoint(TrackedItem item, out string key)
        {
            key = RawPointKey(item.Zdoid);
            bool keyTaken = rawPersistentPoints.TryGetValue(key, out TrackedItem atKey);

            PersistedPointRules.KeyResolution resolution = PersistedPointRules.ResolveKey(keyTaken, keyTaken ? atKey.Position : default, item.Position);
            if (resolution == PersistedPointRules.KeyResolution.AlreadyKnown) return false;
            if (IsDuplicatePosition(item)) return false;

            if (resolution == PersistedPointRules.KeyResolution.KeyCollision)
            {
                key = PersistedPointRules.DisambiguateKey(key, item.Position);
                if (rawPersistentPoints.ContainsKey(key)) return false;
            }

            AddRawPoint(key, item);
            return true;
        }

        // Re-derives which categories should currently be visible and adds/removes minimap pins
        // accordingly. Cached cluster positions are never discarded here - disabling a category only
        // hides its pins, so re-enabling it instantly redraws them at their last known location
        // instead of waiting for the player to walk back into scan range.
        public static void RefreshCategoryVisibility(Minimap minimap)
        {
            if (minimap == null) return;

            // Resource pins: the next Tick recomputes which ones the current view should have.
            resourceViewDirty = true;

            foreach (var kvp in activeClusterPins)
            {
                PinEntry entry = kvp.Value;
                bool shouldShow = ObjectEvaluator.IsCategoryEnabled(entry.CategoryKey);

                if (shouldShow && entry.Pin == null)
                {
                    entry.Pin = minimap.AddPin(entry.Position, Minimap.PinType.Icon3, entry.Label, save: false, isChecked: false);
                    entry.Pin.m_icon = entry.Icon;
                    RadarLog.Diag($"[ValheimRadar] pin-created key={kvp.Key} name={entry.DisplayName} pos={entry.Position.x:F1},{entry.Position.y:F1},{entry.Position.z:F1}");
                }
                else if (!shouldShow && entry.Pin != null)
                {
                    minimap.RemovePin(entry.Pin);
                    entry.Pin = null;
                    LogPinRemoved(kvp.Key, "category-disabled");
                }
            }
        }

        // Syncs transient (creature) clusters - rebuilt fresh every tick by the caller from just this
        // tick's in-range detections (bounded by ScanRadius, not the discovery history), so a full
        // add/remove pass here stays cheap. Persistent clusters are handled separately (see
        // PinManager.Viewport.cs).
        public static void SyncTransientClusters(Minimap minimap, List<ItemCluster> clusters)
        {
            HashSet<string> currentScanKeys = new HashSet<string>();

            foreach (var cluster in clusters)
            {
                string key = cluster.GetClusterKey();
                if (string.IsNullOrEmpty(key)) continue;

                currentScanKeys.Add(key);
                UpdateOrCreatePin(minimap, key, cluster.GetCentroid(), LabelFor(cluster), cluster.DisplayName, cluster.RawName, cluster.Icon, cluster.IsPersistent, cluster.CategoryKey, cluster.Items.Count);
            }

            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (kvp.Value.IsPersistent) continue; // Locations, never out of range
                if (currentScanKeys.Contains(kvp.Key)) continue;

                if (ObjectEvaluator.IsCategoryEnabled(kvp.Value.CategoryKey))
                {
                    // Transient (creature) cluster genuinely left scan range - clean it up. A
                    // disabled creature category is left alone even if absent from this scan, so its
                    // cached position survives being toggled off instead of being evicted outright.
                    if (kvp.Value.Pin != null) minimap.RemovePin(kvp.Value.Pin);
                    LogPinRemoved(kvp.Key, "out-of-range");
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove) activeClusterPins.Remove(key);
        }

        // Resolves and applies the icon for a just-recorded/just-loaded location point, exactly the
        // same 3-tier lookup (specific PNG -> category PNG -> vanilla sprite) UpdateOrCreatePin
        // itself falls back to for other categories - done explicitly here (rather than relying on
        // UpdateOrCreatePin's own null-icon fallback) purely so RecordAndSyncLocations/
        // DrawLoadedLocationPins can share one helper instead of duplicating the two
        // ObjectEvaluator.Get*ForCategory calls twice each.
        private static Sprite ResolveLocationIcon(TrackedLocation loc)
        {
            string iconPng = ObjectEvaluator.GetDefaultIconForCategory(loc.CategoryKey);
            string vanillaIcon = ObjectEvaluator.GetVanillaIconForCategory(loc.CategoryKey);
            return string.IsNullOrEmpty(iconPng) ? null : ResolvePerObjectPin(loc.RawName, iconPng, vanillaIcon);
        }

        // Merges newly-discovered Locations (see LocationScanner.ScanLocations) into the raw store
        // and immediately creates/updates each one's pin. A Location is discovered exactly once per
        // world (LocationKey is deterministic from its own seeded world-gen position), so unlike
        // RecordRawPoints there's no clustering pass and no "is this the same physical object
        // rediscovered" proximity check needed - the key alone is the dedup.
        public static void RecordAndSyncLocations(Minimap minimap, List<TrackedLocation> locations)
        {
            if (minimap == null) return;

            foreach (var loc in locations)
            {
                string key = "loc:" + loc.LocationKey;
                if (rawLocationPoints.ContainsKey(key)) continue;

                rawLocationPoints[key] = loc;
                QueueLocationSave(loc);

                if (dismissedPins.Contains(loc.CategoryKey, loc.Position)) continue;

                UpdateOrCreatePin(minimap, key, loc.Position, loc.DisplayName, loc.DisplayName, loc.RawName, ResolveLocationIcon(loc), isPersistent: true, loc.CategoryKey);
            }
        }

        // Pushes a pin for every Location currently in the raw store - called once, right after
        // OpenWorld, on reconnect, so previously-discovered Locations redraw immediately
        // instead of waiting for the next ScanLocations tick.
        public static void DrawLoadedLocationPins(Minimap minimap)
        {
            if (minimap == null) return;

            foreach (var kvp in rawLocationPoints)
            {
                TrackedLocation loc = kvp.Value;
                if (dismissedPins.Contains(loc.CategoryKey, loc.Position)) continue;

                UpdateOrCreatePin(minimap, kvp.Key, loc.Position, loc.DisplayName, loc.DisplayName, loc.RawName, ResolveLocationIcon(loc), isPersistent: true, loc.CategoryKey);
            }
        }
        /// <summary>
        /// Resolves the minimap icon Sprite for a tracked object, or null to use the vanilla
        /// Icon3 pin's own default sprite. See docs/ICONS.md for the full resolution order.
        /// </summary>
        public static Sprite ResolvePerObjectPin(string rawName, string categoryDefaultPng, string vanillaIconName = null)
        {
            string cleanKey = ObjectEvaluator.StripKnownPrefixes(rawName).ToLower();

            // 1. User-supplied PNG for this exact type (e.g. ValheimRadar/wolf.png).
            string specificPath = Path.Combine(ConfigIconFolder, $"{cleanKey}.png");
            Sprite specificSprite = IconLoader.LoadPng(specificPath);
            if (specificSprite != null)
            {
                return specificSprite;
            }

            // 2. User-supplied PNG for the whole category (e.g. ValheimRadar/monster.png).
            string categoryPath = Path.Combine(ConfigIconFolder, categoryDefaultPng);
            Sprite categorySprite = IconLoader.LoadPng(categoryPath);
            if (categorySprite != null)
            {
                return categorySprite;
            }

            // 3. No custom PNG anywhere - fall back to Valheim's own icon for this type (creature
            // trophy icon / resource pickup icon, via Jotunn's GUIManager) so distinct types still
            // look distinct out of the box.
            if (VanillaIconResolver.TryResolveIcon(cleanKey, vanillaIconName, out Sprite vanillaSprite))
            {
                return vanillaSprite;
            }

            return null;
        }

        // A cluster's icon is captured once, from whichever raw point first started it (see
        // ClusteringEngine), and that same TrackedItem is reused for the rest of the session rather
        // than re-evaluated - so if the very first resolution attempt happened before Jotunn's
        // GUIManager had finished loading its icon atlas (most likely right after connecting, when
        // persisted points are redrawn before the first real scan tick), the icon comes back null and
        // would otherwise stay null forever. Retried here, on every pin update, so a pin that starts
        // iconless self-heals within a tick or two once the atlas is actually ready, instead of
        // staying blank for the rest of the session. Only resource categories have a PNG/vanilla-icon
        // mapping to retry from (see GetDefaultIconForCategory) - creature icons are resolved fresh
        // every scan tick already, so they aren't subject to this staleness.
        private static Sprite TryResolveMissingIcon(string categoryKey, string rawName)
        {
            string iconPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
            if (string.IsNullOrEmpty(iconPng)) return null;

            string vanillaIcon = ObjectEvaluator.GetVanillaIconForCategory(categoryKey);
            return ResolvePerObjectPin(rawName, iconPng, vanillaIcon);
        }

        // True when icon tells this type apart from the rest of its category: Valheim's own item icon
        // or a user PNG for this exact prefab (ResolvePerObjectPin tiers 1/3) - but not the shared
        // category PNG (e.g. ore.png for every ore), which alone can't tell silver from copper.
        private static bool IconIdentifiesType(Sprite icon, string categoryKey)
        {
            if (icon == null) return false;

            string categoryPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
            if (string.IsNullOrEmpty(categoryPng)) return true;

            return icon != IconLoader.LoadPng(Path.Combine(ConfigIconFolder, categoryPng));
        }

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, string displayName, string rawName, Sprite icon, bool isPersistent, string categoryKey, int count = 1)
        {
            bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);

            if (icon == null)
            {
                icon = TryResolveMissingIcon(categoryKey, rawName);
            }

            // An ore deposit or regrowing pickable whose icon already shows what it is needs no name -
            // just a cluster's count ("5x"), or nothing for a single one. Without such an icon it keeps
            // its plain label ("Silver", "3x Blueberry Bush").
            if (ObjectEvaluator.HasIconOnlyLabel(categoryKey) && IconIdentifiesType(icon, categoryKey))
            {
                name = ItemCluster.BuildLabel(displayName, count, hideName: true);
            }

            if (activeClusterPins.TryGetValue(clusterKey, out PinEntry existing))
            {
                bool changed = existing.Position != pos || existing.DisplayName != displayName;

                existing.Position = pos;
                existing.Label = name;
                existing.DisplayName = displayName;
                existing.RawName = rawName;
                existing.Icon = icon;
                existing.CategoryKey = categoryKey;

                if (existing.Pin != null)
                {
                    existing.Pin.m_pos = pos;
                    ApplyPinName(existing.Pin, name);
                    existing.Pin.m_icon = icon;

                    // Only logged when something actually moved/renamed - UpdateOrCreatePin runs
                    // every scan tick for every live cluster, so logging unconditionally here would
                    // drown out the real events an integration test needs to assert on.
                    if (changed)
                    {
                        // RadarLog.Diag($"[ValheimRadar] pin-updated key={clusterKey} name={displayName} pos={pos.x:F1},{pos.y:F1},{pos.z:F1}");
                    }
                }

                existing.IsPersistent = isPersistent;

                if (categoryEnabled && existing.Pin == null)
                {
                    existing.Pin = minimap.AddPin(pos, Minimap.PinType.Icon3, name, save: false, isChecked: false);
                    existing.Pin.m_icon = icon;
                    RadarLog.Diag($"[ValheimRadar] pin-created key={clusterKey} name={displayName} pos={pos.x:F1},{pos.y:F1},{pos.z:F1}");
                }
                else if (!categoryEnabled && existing.Pin != null)
                {
                    minimap.RemovePin(existing.Pin);
                    existing.Pin = null;
                    LogPinRemoved(clusterKey, "category-disabled");
                }
            }
            else
            {
                Minimap.PinData newPin = null;
                if (categoryEnabled)
                {
                    newPin = minimap.AddPin(pos, Minimap.PinType.Icon3, name, save: false, isChecked: false);
                    newPin.m_icon = icon;
                }

                activeClusterPins.Add(clusterKey, new PinEntry
                {
                    Pin = newPin,
                    IsPersistent = isPersistent,
                    CategoryKey = categoryKey,
                    Position = pos,
                    Label = name,
                    DisplayName = displayName,
                    RawName = rawName,
                    Icon = icon
                });

                if (newPin != null)
                {
                    RadarLog.Diag($"[ValheimRadar] pin-created key={clusterKey} name={displayName} pos={pos.x:F1},{pos.y:F1},{pos.z:F1}");
                }
            }
        }

        // Shared by every RemovePin call site so the integration test's log-based assertions can
        // tell a real despawn (reason="out-of-range") apart from unrelated pin churn - a category
        // toggle, a ClusterDistance-triggered recluster, etc. - happening during the same test run.
        private static void LogPinRemoved(string clusterKey, string reason) =>
            RadarLog.Diag($"[ValheimRadar] pin-removed key={clusterKey} reason={reason}");

        // Pin label for a cluster: the plain "Nx Name" label, except creature pins whose icon already
        // identifies them drop the name and keep only count/stars (config ShowCreatureNames, default off;
        // decision logic lives in ItemCluster.ShouldHideCreatureName so it can be unit tested).
        private static string LabelFor(ItemCluster cluster)
        {
            bool showNames = RadarConfig.ShowCreatureNames == null || RadarConfig.ShowCreatureNames.Value;
            return cluster.GetLabel(ItemCluster.ShouldHideCreatureName(cluster.CategoryKey, cluster.Icon != null, showNames));
        }

        // Updates a live pin's label. Minimap only builds a pin's name text object when the pin is created
        // with a non-empty name (Minimap.AddPin) and reads m_name once when that object is built, so just
        // assigning m_name would leave an initially-empty label (creature pins that hide their name) blank
        // forever and any later label change (e.g. cluster count 1 -> 2) stale. Mirrors what vanilla does
        // when the player renames a pin (Minimap.OnPinTextEntered): give the pin a fresh PinNameData and let
        // Minimap.UpdatePins lazily rebuild the text object from the new m_name.
        private static void ApplyPinName(Minimap.PinData pin, string name)
        {
            if (name == null) name = string.Empty;
            if (pin.m_name == name) return;

            pin.m_name = name;
            if (name.Length == 0) return; // Minimap.UpdatePins hides the name object whenever m_name is empty

            if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameGameObject != null)
            {
                UnityEngine.Object.Destroy(pin.m_NamePinData.PinNameGameObject);
                pin.m_NamePinData = null;
            }

            if (pin.m_NamePinData == null)
            {
                pin.m_NamePinData = new Minimap.PinNameData(pin);
            }
        }

        // Troubleshooting snapshot of the name objects Minimap builds for ValheimRadar's pins (see
        // RadarPlugin's throttled call, RadarConfig.DiagnosticLogging). One summary line plus a few sample pins:
        // it says whether name objects exist, are active, and sit under the expected name root, and whether the
        // vanilla "names only when zoomed in" rule (m_showNamesZoom) is what's hiding them. Read-only.
        public static void LogNameDiagnostics(Minimap minimap)
        {
            if (minimap == null) return;

            try
            {
                int withPin = 0, withName = 0, withNameData = 0, withNameObject = 0, nameObjectActive = 0;
                var samples = new List<string>();

                var radarPins = new List<KeyValuePair<Minimap.PinData, string>>();
                foreach (var entry in activeClusterPins.Values) radarPins.Add(new KeyValuePair<Minimap.PinData, string>(entry.Pin, entry.CategoryKey));
                foreach (var kvp in resourcePins) radarPins.Add(new KeyValuePair<Minimap.PinData, string>(kvp.Value, kvp.Key.CategoryKey));

                foreach (var kvp in radarPins)
                {
                    Minimap.PinData pin = kvp.Key;
                    if (pin == null) continue;
                    withPin++;

                    bool hasName = !string.IsNullOrEmpty(pin.m_name);
                    if (hasName) withName++;

                    Minimap.PinNameData nameData = pin.m_NamePinData;
                    if (nameData != null) withNameData++;

                    GameObject nameObject = nameData != null ? nameData.PinNameGameObject : null;
                    if (nameObject != null)
                    {
                        withNameObject++;
                        if (nameObject.activeInHierarchy) nameObjectActive++;
                    }

                    if (hasName && samples.Count < 3)
                    {
                        string parent = nameObject != null && nameObject.transform.parent != null ? nameObject.transform.parent.name : "-";
                        samples.Add($"'{pin.m_name}' cat={kvp.Value} nameData={(nameData != null)} nameObj={(nameObject != null)} active={(nameObject != null && nameObject.activeInHierarchy)} parent={parent} iconShown={(pin.m_uiElement != null && pin.m_uiElement.gameObject.activeInHierarchy)}");
                    }
                }

                RectTransform nameRoot = minimap.m_pinNameRootLarge;
                string rootInfo = nameRoot != null
                    ? $"{nameRoot.name} activeInHierarchy={nameRoot.gameObject.activeInHierarchy} children={nameRoot.childCount} scale={nameRoot.lossyScale.x:F2}"
                    : "<null>";

                RadarLog.Diag($"[ValheimRadar] name-diag mode={minimap.m_mode} largeZoom={minimap.LargeZoom:F2} showNamesBelow={minimap.m_showNamesZoom:F2} radarPins={withPin} withName={withName} withNameData={withNameData} withNameObject={withNameObject} nameObjectActive={nameObjectActive} nameRoot=[{rootInfo}]");
                foreach (string sample in samples) RadarLog.Diag($"[ValheimRadar] name-diag sample {sample}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ValheimRadar] name-diag failed: {ex.Message}");
            }
        }

        // --- Manual pin removal (right-click) ---------------------------------------------------------
        //
        // Vanilla only lets the player remove pins with m_save == true (Minimap.GetClosestPin), and radar
        // pins are created with save: false, so right-click never found them. MinimapPatches hooks the
        // vanilla removal call and, when vanilla found nothing to remove, forwards to TryDismissPinAt.
        //
        // What can be dismissed: persistent (resource / Location) pins that are currently on the map. Every
        // member point of a dismissed resource cluster is remembered by category + position (see
        // DismissedPinStore) and excluded from all future clustering, so the pin stays gone across scan
        // ticks, ClusterDistance rebuilds and relogs while every other pin - including other clusters of
        // the same category - is untouched. Creature pins are excluded: they're rebuilt from live
        // detections every tick, so there's no stable thing to remember.
        public static bool TryDismissPinAt(Minimap minimap, Vector3 worldPos, float radius)
        {
            if (minimap == null) return false;
            if (RadarConfig.EnablePinRemoval != null && !RadarConfig.EnablePinRemoval.Value) return false;

            // Candidates: Location pins (keyed in activeClusterPins) and resource cluster pins.
            var keys = new List<string>();
            var clusters = new List<ItemCluster>();
            var positions = new List<Vector3>();
            foreach (var kvp in activeClusterPins)
            {
                PinEntry entry = kvp.Value;
                if (!PinDismissal.IsDismissible(entry.IsPersistent, IsShown(entry.Pin))) continue;

                keys.Add(kvp.Key);
                clusters.Add(null);
                positions.Add(entry.Position);
            }

            foreach (var kvp in resourcePins)
            {
                if (!PinDismissal.IsDismissible(true, IsShown(kvp.Value))) continue;

                keys.Add(null);
                clusters.Add(kvp.Key);
                positions.Add(kvp.Value.m_pos);
            }

            int index = PinDismissal.IndexOfClosest(positions, worldPos, radius);
            if (index < 0) return false;

            if (clusters[index] != null) DismissCluster(minimap, clusters[index]);
            else DismissPin(minimap, keys[index]);
            return true;
        }

        private static bool IsShown(Minimap.PinData pin) =>
            pin != null && pin.m_uiElement != null && pin.m_uiElement.gameObject.activeInHierarchy;

        // Dismisses a resource cluster: every member point is remembered as dismissed and the cluster
        // leaves clustering for good.
        private static void DismissCluster(Minimap minimap, ItemCluster cluster)
        {
            foreach (var item in cluster.Items) dismissedPins.Add(item.CategoryKey, item.Position);
            clusterGrid?.RemoveCluster(cluster);
            dirtyClusters.Remove(cluster);
            RemoveResourcePins(minimap, new List<ItemCluster> { cluster });
            RadarLog.Diag($"[ValheimRadar] pin-removed name={cluster.DisplayName} reason=dismissed");

            QueueDismissedSave();
        }

        // Dismisses a Location pin (or, as a fallback, any other persistent entry by its own position).
        private static void DismissPin(Minimap minimap, string pinKey)
        {
            if (!activeClusterPins.TryGetValue(pinKey, out PinEntry entry)) return;

            if (pinKey.StartsWith("loc:", StringComparison.Ordinal) && rawLocationPoints.TryGetValue(pinKey, out TrackedLocation loc))
            {
                dismissedPins.Add(loc.CategoryKey, loc.Position);
            }
            else
            {
                dismissedPins.Add(entry.CategoryKey, entry.Position);
            }

            if (entry.Pin != null) minimap.RemovePin(entry.Pin);
            activeClusterPins.Remove(pinKey);
            LogPinRemoved(pinKey, "dismissed");

            QueueDismissedSave();
        }

        // Restores every dismissed pin: forgets the dismissals and rebuilds what they had hidden. Persistent
        // clusters are rebuilt (clusteredMaxDistance reset, so the next RecordRawPoints does a full
        // RebuildPersistentClusters + resync) and dismissed Locations are redrawn right away.
        public static void RestoreDismissedPins(Minimap minimap)
        {
            if (dismissedPins.Count == 0) return;

            dismissedPins.Clear();
            QueueDismissedSave();

            clusteredMaxDistance = -1f;
            DrawLoadedLocationPins(minimap);
        }


    }
}
