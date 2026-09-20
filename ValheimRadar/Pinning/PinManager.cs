using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using System.IO;
using UnityEngine;

namespace ValheimRadar
{
    public static class PinManager
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

        // Every persistent (resource/structure) point ever discovered, keyed by its ZDOID (a hint, not
        // proof of identity - see PersistedPointRules/TryStoreRawPoint). Source of truth for persistentClusters below, which is what pins are actually
        // synced from (see RecordRawPoints/SyncPersistentClusters). Points are recorded regardless of
        // whether their category is currently enabled (see ObjectEvaluator) so re-enabling a category
        // later immediately repopulates already-scanned ground instead of requiring the player to walk
        // it again.
        private static readonly Dictionary<string, TrackedItem> rawPersistentPoints = new Dictionary<string, TrackedItem>();
        private static bool rawPointsDirty;

        // Every world Location ever discovered this session via LocationScanner, keyed by
        // "loc:" + TrackedLocation.LocationKey. A completely separate store from
        // rawPersistentPoints/PinData save file above - Locations have no ZDOID, so they can't go
        // through RawPointKey/the ZDO-liveness check LoadWorldPins performs (see
        // Models/TrackedLocation.cs). Each entry is already a single unique point (one Location = one
        // pin), so unlike rawPersistentPoints there's no clustering pass here either.
        private static readonly Dictionary<string, TrackedLocation> rawLocationPoints = new Dictionary<string, TrackedLocation>();
        private static bool locationPointsDirty;

        // Live persistent clusters, maintained incrementally: a newly-recorded raw point is folded
        // into an existing cluster (or starts a new one) via ClusteringEngine.AddItem, rather than
        // reclustering the full discovery history from scratch every tick (see RecordRawPoints). Only
        // rebuilt wholesale when ClusterDistance changes (clusteredMaxDistance no longer matches) or on
        // world load, since the greedy insertion-order clustering ClusteringEngine uses is only valid
        // for a given maxDistance and a given item ordering.
        private static readonly List<ItemCluster> persistentClusters = new List<ItemCluster>();

        // Clusters touched since the last SyncPersistentClusters call - either just-created/appended-to
        // by RecordRawPoints, or every cluster at once after a full rebuild. Draining this each sync
        // means a normal tick only pushes pin updates for the handful of clusters that actually
        // changed, instead of walking every persistent cluster discovered this session.
        private static readonly List<ItemCluster> dirtyPersistentClusters = new List<ItemCluster>();

        // maxDistance persistentClusters was last built/incrementally maintained against. A mismatch
        // (ClusterDistance config change) triggers a full RebuildPersistentClusters on the next
        // RecordRawPoints call.
        private static float clusteredMaxDistance = -1f;

        // Set by RebuildPersistentClusters. A full rebuild replaces every ItemCluster with a fresh
        // instance (LastSyncedKey == null), so the per-cluster stale-key eviction SyncPersistentClusters
        // normally relies on can't see what the *old* clustering's keys were - without this, pins left
        // over from before the rebuild (e.g. every persistent pin, after a ClusterDistance change)
        // would never get evicted. Tells the next SyncPersistentClusters call to instead reconcile
        // activeClusterPins against the full new key set, once.
        private static bool persistentFullResyncPending;

        // A newly-recorded point is treated as "already known" if it lands within
        // PersistedPointRules.DuplicatePointRadius of an existing point of the same DisplayName, even
        // when its ZDOID doesn't match any recorded key - ZDOIDs aren't stable across game/server
        // sessions (observed with wild Beehives; see PersistedPointRules for why), so position is
        // the identity that actually persists.

        // Pins the player dismissed by right-clicking them (see TryDismissPinAt). Recorded per point,
        // not per cluster, and consulted whenever raw points are (re)clustered or Locations are drawn, so
        // a dismissed pin never comes back on a later scan tick, recluster or relog. Persisted in its own
        // sibling file (<world>.dismissed.txt) so existing PinData files stay untouched.
        private static readonly DismissedPinStore dismissedPins = new DismissedPinStore();
        private static string dismissedPinsWorld;

        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "ValheimRadar");
        private static string PinDataFolder => Path.Combine(Paths.ConfigPath, "ValheimRadar", "PinData");

        // Removes every live pin from the minimap and drops all in-memory tracking state. Called on
        // disconnect/world unload (after SaveWorldPins) so state never leaks across sessions/worlds.
        public static void ClearAllPins()
        {
            if (activeClusterPins.Count > 0)
            {
                Debug.Log($"[ValheimRadar] pin-removed-all count={activeClusterPins.Count}");
            }

            if (Minimap.instance != null)
            {
                foreach (var entry in activeClusterPins.Values)
                {
                    if (entry.Pin != null) Minimap.instance.RemovePin(entry.Pin);
                }
            }

            activeClusterPins.Clear();
            rawPersistentPoints.Clear();
            rawPointsDirty = false;
            persistentClusters.Clear();
            dirtyPersistentClusters.Clear();
            clusteredMaxDistance = -1f;
            persistentFullResyncPending = false;
            rawLocationPoints.Clear();
            locationPointsDirty = false;
            dismissedPins.Clear();
            dismissedPinsWorld = null;
        }

        // Merges newly-scanned persistent points into the durable raw store, keyed by ZDOID so the
        // same stationary world object is never recorded twice, and folds each genuinely-new point
        // into persistentClusters incrementally. Transient (creature) items are ignored here - see the
        // class comment on rawPersistentPoints.
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
                rawPointsDirty = true;

                // Still recorded above (so it's never re-discovered as "new"), just never clustered/pinned.
                if (dismissedPins.Contains(item.CategoryKey, item.Position)) continue;

                ItemCluster affected = ClusteringEngine.AddItem(persistentClusters, item, maxDistance);
                if (!dirtyPersistentClusters.Contains(affected)) dirtyPersistentClusters.Add(affected);
            }
        }

        // Reclusters the full raw-point history from scratch - the only time persistent clustering
        // pays the O(points * clusters) cost of ClusteringEngine.ClusterItems. Needed because the
        // greedy insertion-order clustering it (and the incremental AddItem path) use is only valid
        // for the maxDistance and ordering it was built with; a ClusterDistance change invalidates
        // every existing cluster's boundaries. Marks every resulting cluster dirty so the next
        // SyncPersistentClusters pushes a full resync, mirroring the old key-churn eviction that used
        // to happen every tick (see ItemCluster.GetClusterKey / SyncPersistentClusters).
        public static void RebuildPersistentClusters(float maxDistance)
        {
            persistentClusters.Clear();
            var clusterable = new List<TrackedItem>(rawPersistentPoints.Count);
            foreach (var point in rawPersistentPoints.Values)
            {
                if (!dismissedPins.Contains(point.CategoryKey, point.Position)) clusterable.Add(point);
            }

            persistentClusters.AddRange(ClusteringEngine.ClusterItems(clusterable, maxDistance));

            dirtyPersistentClusters.Clear();
            dirtyPersistentClusters.AddRange(persistentClusters);

            clusteredMaxDistance = maxDistance;
            persistentFullResyncPending = true;
        }

        private static string RawPointKey(ZDOID zdoid) => $"{zdoid.UserID}:{zdoid.ID}";

        private static bool IsDuplicatePosition(TrackedItem item)
        {
            foreach (var existing in rawPersistentPoints.Values)
            {
                if (PersistedPointRules.IsDuplicate(existing.DisplayName, existing.Position, item.DisplayName, item.Position))
                {
                    return true;
                }
            }

            return false;
        }

        // Adds item to rawPersistentPoints unless it's already known; returns whether it was added.
        // Shared by live scans (RecordRawPoints) and save-file loads (LoadWorldPins) so both apply the
        // exact same identity rules. The ZDOID-derived key is only a hint - ZDOIDs are re-assigned
        // when a server reloads its world, so a key match only means "same object" if the position
        // agrees too; otherwise the point is new and is stored under a disambiguated key instead of
        // being silently skipped. See PersistedPointRules.
        private static bool TryStoreRawPoint(TrackedItem item)
        {
            string key = RawPointKey(item.Zdoid);
            bool keyTaken = rawPersistentPoints.TryGetValue(key, out TrackedItem atKey);

            PersistedPointRules.KeyResolution resolution = PersistedPointRules.ResolveKey(keyTaken, keyTaken ? atKey.Position : default, item.Position);
            if (resolution == PersistedPointRules.KeyResolution.AlreadyKnown) return false;
            if (IsDuplicatePosition(item)) return false;

            if (resolution == PersistedPointRules.KeyResolution.KeyCollision)
            {
                key = PersistedPointRules.DisambiguateKey(key, item.Position);
                if (rawPersistentPoints.ContainsKey(key)) return false;
            }

            rawPersistentPoints[key] = item;
            return true;
        }

        // Re-derives which categories should currently be visible and adds/removes minimap pins
        // accordingly. Cached cluster positions are never discarded here - disabling a category only
        // hides its pins, so re-enabling it instantly redraws them at their last known location
        // instead of waiting for the player to walk back into scan range.
        public static void RefreshCategoryVisibility(Minimap minimap)
        {
            if (minimap == null) return;

            foreach (var kvp in activeClusterPins)
            {
                PinEntry entry = kvp.Value;
                bool shouldShow = ObjectEvaluator.IsCategoryEnabled(entry.CategoryKey);

                if (shouldShow && entry.Pin == null)
                {
                    entry.Pin = minimap.AddPin(entry.Position, Minimap.PinType.Icon3, entry.Label, save: false, isChecked: false);
                    entry.Pin.m_icon = entry.Icon;
                    Debug.Log($"[ValheimRadar] pin-created key={kvp.Key} name={entry.DisplayName} pos={entry.Position.x:F1},{entry.Position.y:F1},{entry.Position.z:F1}");
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
        // add/remove pass here stays cheap. Persistent clusters are handled separately, incrementally,
        // by SyncPersistentClusters below.
        public static void SyncTransientClusters(Minimap minimap, List<ItemCluster> clusters)
        {
            HashSet<string> currentScanKeys = new HashSet<string>();

            foreach (var cluster in clusters)
            {
                string key = cluster.GetClusterKey();
                if (string.IsNullOrEmpty(key)) continue;

                currentScanKeys.Add(key);
                UpdateOrCreatePin(minimap, key, cluster.GetCentroid(), LabelFor(cluster), cluster.DisplayName, cluster.RawName, cluster.Icon, cluster.IsPersistent, cluster.CategoryKey);
            }

            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (kvp.Value.IsPersistent) continue; // managed by SyncPersistentClusters
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

        // Pushes pending persistent-cluster changes onto the minimap: clusters newly created or
        // appended to since the last call (see RecordRawPoints), or every cluster at once after a
        // ClusterDistance-triggered full rebuild (see RebuildPersistentClusters). Unlike
        // SyncTransientClusters this never walks the full discovery history - only the clusters that
        // actually changed this tick get touched, which is what keeps a large, long-explored world
        // from getting slower to scan over a session.
        public static void SyncPersistentClusters(Minimap minimap)
        {
            if (minimap == null) return;
            if (dirtyPersistentClusters.Count == 0 && !persistentFullResyncPending) return;

            // Only populated (and only consulted) after a full rebuild - see the removal pass below.
            HashSet<string> syncedKeys = persistentFullResyncPending ? new HashSet<string>() : null;

            foreach (var cluster in dirtyPersistentClusters)
            {
                string newKey = cluster.GetClusterKey();
                if (string.IsNullOrEmpty(newKey)) continue;

                if (cluster.LastSyncedKey != null && cluster.LastSyncedKey != newKey &&
                    activeClusterPins.TryGetValue(cluster.LastSyncedKey, out PinEntry stale))
                {
                    // Adding an item shifted this cluster's centroid across a GetClusterKey() grid
                    // boundary - evict the old key's entry so it doesn't linger as an orphaned
                    // duplicate pin alongside the one about to be (re)created under the new key.
                    if (stale.Pin != null) minimap.RemovePin(stale.Pin);
                    activeClusterPins.Remove(cluster.LastSyncedKey);
                    LogPinRemoved(cluster.LastSyncedKey, "recluster");
                }

                UpdateOrCreatePin(minimap, newKey, cluster.GetCentroid(), LabelFor(cluster), cluster.DisplayName, cluster.RawName, cluster.Icon, cluster.IsPersistent, cluster.CategoryKey);
                cluster.LastSyncedKey = newKey;
                syncedKeys?.Add(newKey);
            }

            dirtyPersistentClusters.Clear();

            if (persistentFullResyncPending)
            {
                // A full rebuild replaced every persistent cluster object, so per-cluster LastSyncedKey
                // tracking above can't see the pre-rebuild key set. Reconcile directly against
                // activeClusterPins instead: any persistent entry not among the keys just (re)synced
                // belongs to a cluster that no longer exists post-rebuild (e.g. a ClusterDistance
                // change merged or split it) and would otherwise linger as an orphaned duplicate pin.
                List<string> toRemove = new List<string>();
                foreach (var kvp in activeClusterPins)
                {
                    if (kvp.Value.IsPersistent && !syncedKeys.Contains(kvp.Key)) toRemove.Add(kvp.Key);
                }

                foreach (var key in toRemove)
                {
                    if (activeClusterPins.TryGetValue(key, out PinEntry entry) && entry.Pin != null) minimap.RemovePin(entry.Pin);
                    activeClusterPins.Remove(key);
                    LogPinRemoved(key, "recluster");
                }

                persistentFullResyncPending = false;
            }
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
                locationPointsDirty = true;

                if (dismissedPins.Contains(loc.CategoryKey, loc.Position)) continue;

                UpdateOrCreatePin(minimap, key, loc.Position, loc.DisplayName, loc.DisplayName, loc.RawName, ResolveLocationIcon(loc), isPersistent: true, loc.CategoryKey);
            }
        }

        // Pushes a pin for every Location currently in the raw store - called once, right after
        // LoadLocationPins, on reconnect, so previously-discovered Locations redraw immediately
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

        // Sibling save file to the resource PinData file above (<world>.locations.txt, own #locv1
        // version tag) rather than an extension of that format - the resource format is hard-coded to
        // exactly 8 fields including two ZDOID integers and a ZDOMan.GetZDO liveness check that has
        // no meaning for a Location (no ZDO exists to check - a Location's position is static and
        // never "dies"), so reusing it would mean branching the entire load loop on record type for
        // no real benefit.
        private const int LocationSaveFormatVersion = 1;

        public static void SaveLocationPins(string worldName)
        {
            if (!locationPointsDirty || string.IsNullOrEmpty(worldName)) return;

            List<string> lines = new List<string> { $"#locv{LocationSaveFormatVersion}" };
            foreach (var loc in rawLocationPoints.Values)
            {
                lines.Add(string.Join("|",
                    Escape(loc.LocationKey),
                    loc.Position.x.ToString(CultureInfo.InvariantCulture),
                    loc.Position.y.ToString(CultureInfo.InvariantCulture),
                    loc.Position.z.ToString(CultureInfo.InvariantCulture),
                    Escape(loc.DisplayName),
                    Escape(loc.RawName),
                    Escape(loc.CategoryKey)));
            }

            try
            {
                Directory.CreateDirectory(PinDataFolder);
                File.WriteAllLines(GetLocationSaveFilePath(worldName), lines);
                locationPointsDirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to save persisted location pins: {ex.Message}");
            }
        }

        // Restores previously discovered Locations for this world into the raw store. Does not touch
        // the minimap itself - the caller (RadarPlugin, on reconnect) is expected to follow up with
        // DrawLoadedLocationPins, mirroring how LoadWorldPins/RebuildPersistentClusters/
        // SyncPersistentClusters are sequenced for resource pins.
        public static void LoadLocationPins(string worldName)
        {
            rawLocationPoints.Clear();
            locationPointsDirty = false;

            if (string.IsNullOrEmpty(worldName)) return;

            string path = GetLocationSaveFilePath(worldName);
            if (!File.Exists(path)) return;

            try
            {
                string[] allLines = File.ReadAllLines(path);
                int startIndex = allLines.Length > 0 && allLines[0].StartsWith("#locv", StringComparison.Ordinal) ? 1 : 0;

                for (int i = startIndex; i < allLines.Length; i++)
                {
                    string line = allLines[i];
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length != 7) continue;

                    string locationKey = Unescape(parts[0]);
                    if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                    if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

                    string displayName = Unescape(parts[4]);
                    string rawName = Unescape(parts[5]);
                    string categoryKey = Unescape(parts[6]);

                    if (string.IsNullOrEmpty(locationKey) || string.IsNullOrEmpty(displayName)) continue;

                    rawLocationPoints["loc:" + locationKey] = new TrackedLocation
                    {
                        LocationKey = locationKey,
                        Position = new Vector3(x, y, z),
                        DisplayName = displayName,
                        RawName = rawName,
                        CategoryKey = categoryKey
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load persisted location pins: {ex.Message}");
            }
        }

        private static string GetLocationSaveFilePath(string worldName) => GetSaveFilePath(worldName, ".locations.txt");

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

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, string displayName, string rawName, Sprite icon, bool isPersistent, string categoryKey)
        {
            bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);

            if (icon == null)
            {
                icon = TryResolveMissingIcon(categoryKey, rawName);
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
                        Debug.Log($"[ValheimRadar] pin-updated key={clusterKey} name={displayName} pos={pos.x:F1},{pos.y:F1},{pos.z:F1}");
                    }
                }

                existing.IsPersistent = isPersistent;

                if (categoryEnabled && existing.Pin == null)
                {
                    existing.Pin = minimap.AddPin(pos, Minimap.PinType.Icon3, name, save: false, isChecked: false);
                    existing.Pin.m_icon = icon;
                    Debug.Log($"[ValheimRadar] pin-created key={clusterKey} name={displayName} pos={pos.x:F1},{pos.y:F1},{pos.z:F1}");
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
                    Debug.Log($"[ValheimRadar] pin-created key={clusterKey} name={displayName} pos={pos.x:F1},{pos.y:F1},{pos.z:F1}");
                }
            }
        }

        // Shared by every RemovePin call site so the integration test's log-based assertions can
        // tell a real despawn (reason="out-of-range") apart from unrelated pin churn - a category
        // toggle, a ClusterDistance-triggered recluster, etc. - happening during the same test run.
        private static void LogPinRemoved(string clusterKey, string reason) =>
            Debug.Log($"[ValheimRadar] pin-removed key={clusterKey} reason={reason}");

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

                foreach (var kvp in activeClusterPins)
                {
                    Minimap.PinData pin = kvp.Value.Pin;
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
                        samples.Add($"'{pin.m_name}' cat={kvp.Value.CategoryKey} nameData={(nameData != null)} nameObj={(nameObject != null)} active={(nameObject != null && nameObject.activeInHierarchy)} parent={parent} iconShown={(pin.m_uiElement != null && pin.m_uiElement.gameObject.activeInHierarchy)}");
                    }
                }

                RectTransform nameRoot = minimap.m_pinNameRootLarge;
                string rootInfo = nameRoot != null
                    ? $"{nameRoot.name} activeInHierarchy={nameRoot.gameObject.activeInHierarchy} children={nameRoot.childCount} scale={nameRoot.lossyScale.x:F2}"
                    : "<null>";

                Debug.Log($"[ValheimRadar] name-diag mode={minimap.m_mode} largeZoom={minimap.LargeZoom:F2} showNamesBelow={minimap.m_showNamesZoom:F2} radarPins={withPin} withName={withName} withNameData={withNameData} withNameObject={withNameObject} nameObjectActive={nameObjectActive} nameRoot=[{rootInfo}]");
                foreach (string sample in samples) Debug.Log($"[ValheimRadar] name-diag sample {sample}");
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

            var keys = new List<string>();
            var positions = new List<Vector3>();
            foreach (var kvp in activeClusterPins)
            {
                PinEntry entry = kvp.Value;
                bool shown = entry.Pin != null && entry.Pin.m_uiElement != null && entry.Pin.m_uiElement.gameObject.activeInHierarchy;
                if (!PinDismissal.IsDismissible(entry.IsPersistent, shown)) continue;

                keys.Add(kvp.Key);
                positions.Add(entry.Position);
            }

            int index = PinDismissal.IndexOfClosest(positions, worldPos, radius);
            if (index < 0) return false;

            DismissPin(minimap, keys[index]);
            return true;
        }

        private static void DismissPin(Minimap minimap, string pinKey)
        {
            if (!activeClusterPins.TryGetValue(pinKey, out PinEntry entry)) return;

            if (pinKey.StartsWith("loc:", StringComparison.Ordinal) && rawLocationPoints.TryGetValue(pinKey, out TrackedLocation loc))
            {
                dismissedPins.Add(loc.CategoryKey, loc.Position);
            }
            else
            {
                ItemCluster cluster = persistentClusters.Find(c => c.LastSyncedKey == pinKey);
                if (cluster != null)
                {
                    foreach (var item in cluster.Items) dismissedPins.Add(item.CategoryKey, item.Position);
                    persistentClusters.Remove(cluster);
                    dirtyPersistentClusters.Remove(cluster);
                }
                else
                {
                    // Shouldn't happen (every live persistent pin has a cluster), but never leave the
                    // pin un-dismissable: at least remember its own position.
                    dismissedPins.Add(entry.CategoryKey, entry.Position);
                }
            }

            if (entry.Pin != null) minimap.RemovePin(entry.Pin);
            activeClusterPins.Remove(pinKey);
            LogPinRemoved(pinKey, "dismissed");

            SaveDismissedPins();
        }

        // Restores every dismissed pin: forgets the dismissals and rebuilds what they had hidden. Persistent
        // clusters are rebuilt (clusteredMaxDistance reset, so the next RecordRawPoints does a full
        // RebuildPersistentClusters + resync) and dismissed Locations are redrawn right away.
        public static void RestoreDismissedPins(Minimap minimap)
        {
            if (dismissedPins.Count == 0) return;

            dismissedPins.Clear();
            SaveDismissedPins();

            clusteredMaxDistance = -1f;
            DrawLoadedLocationPins(minimap);
        }

        // Loads this world's dismissed pins. Must run before RebuildPersistentClusters/DrawLoadedLocationPins
        // on connect, since both consult the set. A missing file (every world before this feature, or one
        // with nothing dismissed) simply means an empty set.
        public static void LoadDismissedPins(string worldName)
        {
            dismissedPins.Clear();
            dismissedPinsWorld = worldName;

            if (string.IsNullOrEmpty(worldName)) return;

            string path = GetSaveFilePath(worldName, ".dismissed.txt");
            if (!File.Exists(path)) return;

            try
            {
                dismissedPins.Load(File.ReadAllLines(path));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load dismissed pins: {ex.Message}");
            }
        }

        // Written immediately whenever the set changes (a right-click is rare and the file tiny), so
        // nothing is lost to a crash and no separate flush is needed on disconnect.
        private static void SaveDismissedPins()
        {
            if (string.IsNullOrEmpty(dismissedPinsWorld)) return;

            string path = GetSaveFilePath(dismissedPinsWorld, ".dismissed.txt");

            try
            {
                if (dismissedPins.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                Directory.CreateDirectory(PinDataFolder);
                File.WriteAllLines(path, dismissedPins.Serialize());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to save dismissed pins: {ex.Message}");
            }
        }

        // Bumped whenever the on-disk pin save format changes shape in a way that needs explicit
        // migration/validation on load (see MigrateLegacyLine below), rather than just "new optional
        // field". Written as line 0 of the save file; a file with no version line at all (or an
        // unparseable one) is treated as PreVersioning (0) - the pipe-delimited format shipped before
        // this field existed.
        // Bumped to 3 for the Chests -> Chests/BuriedChests split (see
        // MigrateLegacyCategoryKey's "Chests" case) so saves written by older builds get re-migrated
        // even though their categoryKey ("resource:Chests") is still a currently-valid id on its own.
        private const int SaveFormatVersion = 3;
        private const int PreVersioningFormat = 0;

        // Only stationary resource/structure points are written to disk - creature positions are
        // transient by nature (they move or die) and would just go stale, so they're re-discovered by
        // scanning each session instead of being persisted.
        //
        // Raw per-object points are saved here, not clusters - see rawPersistentPoints. Serialized as
        // one pipe-delimited line per point (Unity's JsonUtility needs an assembly this project
        // doesn't reference, and the data is simple enough not to warrant adding one). The ZDOID is
        // saved so a reload can dedupe against points re-discovered by a later scan of the same spot.
        public static void SaveWorldPins(string worldName)
        {
            if (!rawPointsDirty || string.IsNullOrEmpty(worldName)) return;

            List<string> lines = new List<string> { $"#v{SaveFormatVersion}" };
            foreach (var item in rawPersistentPoints.Values)
            {
                lines.Add(string.Join("|",
                    item.Zdoid.UserID.ToString(CultureInfo.InvariantCulture),
                    item.Zdoid.ID.ToString(CultureInfo.InvariantCulture),
                    item.Position.x.ToString(CultureInfo.InvariantCulture),
                    item.Position.y.ToString(CultureInfo.InvariantCulture),
                    item.Position.z.ToString(CultureInfo.InvariantCulture),
                    Escape(item.DisplayName),
                    Escape(item.RawName),
                    Escape(item.CategoryKey)));
            }

            try
            {
                Directory.CreateDirectory(PinDataFolder);
                File.WriteAllLines(GetSaveFilePath(worldName), lines);
                rawPointsDirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to save persisted pins: {ex.Message}");
            }
        }

        // Restores previously discovered resource/structure points for this world into the raw store
        // (see rawPersistentPoints), so the map doesn't start blank after a relog. Called once right
        // after connecting, before the first scan tick. Does not touch the minimap itself - the caller
        // is expected to follow up with RebuildPersistentClusters + SyncPersistentClusters so loaded
        // points are drawn through the exact same path a live scan uses, instead of a separate one
        // that could drift out of sync with it (e.g. after a ClusterDistance change).
        //
        // Handles migrating a save file written before this whitelist rewrite (categoryKey values
        // like "resource:Copper"/"resource:Portals" that no longer exist as ResourceRule ids - see
        // ObjectEvaluator's ResourceRules, whose ids were split/renamed/removed in that change).
        // MigrateLegacyLine re-maps every old categoryKey it can (e.g. a pre-split "resource:Copper"
        // point becomes "resource:CopperIngot", since that's what a raw prefab literally named
        // "Copper" - the ingot - would classify as today) and drops (with a one-line log summary, not
        // per-point spam) anything it can't - most notably every "resource:Portals" point, since
        // player-built portals are no longer tracked at all. A file already on the current format is
        // passed through unchanged.
        public static void LoadWorldPins(string worldName)
        {
            rawPersistentPoints.Clear();
            rawPointsDirty = false;

            if (string.IsNullOrEmpty(worldName)) return;

            string path = GetSaveFilePath(worldName);
            if (!File.Exists(path)) return;

            // Save files written before position-based dedup was added (see IsDuplicatePosition) can
            // contain multiple near-identical points for what is really one physical object (e.g. a
            // wild Beehive re-recorded on every relog because its ZDOID isn't session-stable). Any
            // such duplicate encountered here is silently dropped rather than loaded, and marks the
            // store dirty so the next save rewrites the file without it - self-healing the save file
            // over time instead of carrying the old duplicates forward forever.
            //
            // Deliberately does NOT check whether each point's ZDO still exists in ZDOMan (an earlier
            // version did, to shed e.g. a boss arena's shattered stone pillars). On a dedicated-server
            // client ZDOMan only holds the sectors the server has streamed so far - essentially just the
            // area around the player right after connecting - so that check discarded every persisted
            // point further away on every reconnect, and since dropping marks the store dirty the save
            // file was then rewritten without them, permanently losing the player's whole map (issue
            // #42). ZDOIDs are also re-assigned whenever a server reloads its world, so the lookup
            // couldn't prove anything even when ZDOMan was complete. Pins for objects that no longer
            // exist are the lesser evil next to losing valid ones; see PersistedPointRules.
            bool droppedDuplicate = false;
            int migratedCount = 0;
            int unmigratableCount = 0;

            try
            {
                string[] allLines = File.ReadAllLines(path);
                int fileFormatVersion = PreVersioningFormat;
                int startIndex = 0;

                if (allLines.Length > 0 && allLines[0].StartsWith("#v", StringComparison.Ordinal) &&
                    int.TryParse(allLines[0].Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedVersion))
                {
                    fileFormatVersion = parsedVersion;
                    startIndex = 1;
                }

                for (int i = startIndex; i < allLines.Length; i++)
                {
                    string line = allLines[i];
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length != 8) continue;

                    if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long userId)) continue;
                    if (!uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id)) continue;
                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                    if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                    if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

                    string displayName = Unescape(parts[5]);
                    string rawName = Unescape(parts[6]);
                    string categoryKey = Unescape(parts[7]);

                    if (string.IsNullOrEmpty(displayName)) continue;

                    if (fileFormatVersion < SaveFormatVersion)
                    {
                        if (!MigrateLegacyCategoryKey(categoryKey, rawName, out categoryKey))
                        {
                            unmigratableCount++;
                            continue;
                        }

                        migratedCount++;
                    }

                    TrackedItem candidate = new TrackedItem
                    {
                        Zdoid = new ZDOID(userId, id),
                        Position = new Vector3(x, y, z),
                        DisplayName = displayName,
                        RawName = rawName,
                        IsPersistent = true,
                        CategoryKey = categoryKey
                    };

                    if (!TryStoreRawPoint(candidate))
                    {
                        droppedDuplicate = true;
                        continue;
                    }

                    string iconPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
                    string vanillaIcon = ObjectEvaluator.GetVanillaIconForCategory(categoryKey);
                    candidate.Icon = string.IsNullOrEmpty(iconPng)
                        ? null
                        : ResolvePerObjectPin(rawName, iconPng, vanillaIcon);
                }

                rawPointsDirty = droppedDuplicate || migratedCount > 0 || unmigratableCount > 0;

                if (migratedCount > 0 || unmigratableCount > 0)
                {
                    Debug.Log($"[ValheimRadar] pin-save-migrated fromVersion={fileFormatVersion} migrated={migratedCount} dropped={unmigratableCount}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load persisted pins: {ex.Message}");
            }
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
                // Each old single-bucket ore id fanned out into Deposit/Ore/Ingot - use the saved raw
                // prefab name (still exact from before this rewrite) to pick the right one.
                case "Copper":
                    if (cleanRaw == "copperore") { newCategoryKey = "resource:CopperOre"; return true; }
                    if (cleanRaw == "minerock_copper" || cleanRaw == "rock4_copper" || cleanRaw == "rock4_copper_frac") { newCategoryKey = "resource:CopperDeposit"; return true; }
                    newCategoryKey = "resource:CopperIngot"; // default: old rule's own VanillaIcon was "copperore", but "Copper" (the ingot) was the most common real match
                    return true;
                case "Tin":
                    if (cleanRaw == "tinore") { newCategoryKey = "resource:TinOre"; return true; }
                    if (cleanRaw == "minerock_tin") { newCategoryKey = "resource:TinDeposit"; return true; }
                    newCategoryKey = "resource:TinIngot";
                    return true;
                case "Iron":
                    if (cleanRaw == "ironscrap" || cleanRaw == "mudpile" || cleanRaw == "mudpile2") { newCategoryKey = "resource:IronScrap"; return true; }
                    newCategoryKey = "resource:IronIngot";
                    return true;
                case "Silver":
                    if (cleanRaw == "silverore") { newCategoryKey = "resource:SilverOre"; return true; }
                    if (cleanRaw == "silvervein" || cleanRaw == "silvervein_frac" || cleanRaw == "rock3_silver" || cleanRaw == "rock3_silver_frac") { newCategoryKey = "resource:SilverDeposit"; return true; }
                    newCategoryKey = "resource:SilverIngot";
                    return true;

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

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : Uri.EscapeDataString(value);
        }

        private static string Unescape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : Uri.UnescapeDataString(value);
        }

        private static string GetSaveFilePath(string worldName, string suffix = ".txt")
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
    }
}
