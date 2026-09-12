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

        // Every persistent (resource/structure) point ever discovered this session, keyed by its
        // stable ZDOID. Source of truth for persistentClusters below, which is what pins are actually
        // synced from (see RecordRawPoints/SyncPersistentClusters). Points are recorded regardless of
        // whether their category is currently enabled (see ObjectEvaluator) so re-enabling a category
        // later immediately repopulates already-scanned ground instead of requiring the player to walk
        // it again.
        private static readonly Dictionary<string, TrackedItem> rawPersistentPoints = new Dictionary<string, TrackedItem>();
        private static bool rawPointsDirty;

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

        // A newly-recorded point is treated as "already known" if it lands within this radius of an
        // existing point of the same DisplayName, even when its ZDOID doesn't match any recorded key.
        // This is deliberately much smaller than ClusterDistance (which groups genuinely distinct
        // nearby objects into one pin) - it exists only to catch the same physical object being
        // rediscovered. Some world-generated objects (observed with wild Beehives) aren't given a
        // ZDOID that's stable across game sessions, so a ZDOID-only dedup lets the same spot get
        // recorded as a brand new point on every relog, permanently inflating that cluster's count by
        // one each time. Deterministically-placed objects reappear at the exact same position, so a
        // tight proximity check catches this without risking merging two real, distinct objects.
        private const float DuplicatePointRadius = 0.25f;

        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "ValheimRadar");
        private static string PinDataFolder => Path.Combine(Paths.ConfigPath, "ValheimRadar", "PinData");

        // Removes every live pin from the minimap and drops all in-memory tracking state. Called on
        // disconnect/world unload (after SaveWorldPins) so state never leaks across sessions/worlds.
        public static void ClearAllPins()
        {
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

                string key = RawPointKey(item.Zdoid);
                if (rawPersistentPoints.ContainsKey(key)) continue;
                if (IsDuplicatePosition(item)) continue;

                rawPersistentPoints[key] = item;
                rawPointsDirty = true;

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
            persistentClusters.AddRange(ClusteringEngine.ClusterItems(new List<TrackedItem>(rawPersistentPoints.Values), maxDistance));

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
                if (existing.DisplayName == item.DisplayName && Vector3.Distance(existing.Position, item.Position) <= DuplicatePointRadius)
                {
                    return true;
                }
            }

            return false;
        }

        // Re-derives which categories should currently be visible and adds/removes minimap pins
        // accordingly. Cached cluster positions are never discarded here - disabling a category only
        // hides its pins, so re-enabling it instantly redraws them at their last known location
        // instead of waiting for the player to walk back into scan range.
        public static void RefreshCategoryVisibility(Minimap minimap)
        {
            if (minimap == null) return;

            foreach (var entry in activeClusterPins.Values)
            {
                bool shouldShow = ObjectEvaluator.IsCategoryEnabled(entry.CategoryKey);

                if (shouldShow && entry.Pin == null)
                {
                    entry.Pin = minimap.AddPin(entry.Position, Minimap.PinType.Icon3, entry.Label, save: false, isChecked: false);
                    entry.Pin.m_icon = entry.Icon;
                }
                else if (!shouldShow && entry.Pin != null)
                {
                    minimap.RemovePin(entry.Pin);
                    entry.Pin = null;
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
                UpdateOrCreatePin(minimap, key, cluster.GetCentroid(), cluster.GetLabel(), cluster.DisplayName, cluster.RawName, cluster.Icon, cluster.IsPersistent, cluster.CategoryKey);
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
                }

                UpdateOrCreatePin(minimap, newKey, cluster.GetCentroid(), cluster.GetLabel(), cluster.DisplayName, cluster.RawName, cluster.Icon, cluster.IsPersistent, cluster.CategoryKey);
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
                }

                persistentFullResyncPending = false;
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

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, string displayName, string rawName, Sprite icon, bool isPersistent, string categoryKey)
        {
            bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);

            if (icon == null)
            {
                icon = TryResolveMissingIcon(categoryKey, rawName);
            }

            if (activeClusterPins.TryGetValue(clusterKey, out PinEntry existing))
            {
                existing.Position = pos;
                existing.Label = name;
                existing.DisplayName = displayName;
                existing.RawName = rawName;
                existing.Icon = icon;
                existing.CategoryKey = categoryKey;

                if (existing.Pin != null)
                {
                    existing.Pin.m_pos = pos;
                    existing.Pin.m_name = name;
                    existing.Pin.m_icon = icon;
                }

                existing.IsPersistent = isPersistent;

                if (categoryEnabled && existing.Pin == null)
                {
                    existing.Pin = minimap.AddPin(pos, Minimap.PinType.Icon3, name, save: false, isChecked: false);
                    existing.Pin.m_icon = icon;
                }
                else if (!categoryEnabled && existing.Pin != null)
                {
                    minimap.RemovePin(existing.Pin);
                    existing.Pin = null;
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
            }
        }

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

            List<string> lines = new List<string>();
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
            bool droppedDuplicate = false;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
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

                    ZDOID zdoid = new ZDOID(userId, id);
                    string key = RawPointKey(zdoid);

                    TrackedItem candidate = new TrackedItem
                    {
                        Zdoid = zdoid,
                        Position = new Vector3(x, y, z),
                        DisplayName = displayName,
                        RawName = rawName,
                        IsPersistent = true,
                        CategoryKey = categoryKey
                    };

                    if (rawPersistentPoints.ContainsKey(key) || IsDuplicatePosition(candidate))
                    {
                        droppedDuplicate = true;
                        continue;
                    }

                    string iconPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
                    string vanillaIcon = ObjectEvaluator.GetVanillaIconForCategory(categoryKey);
                    candidate.Icon = string.IsNullOrEmpty(iconPng)
                        ? null
                        : ResolvePerObjectPin(rawName, iconPng, vanillaIcon);

                    rawPersistentPoints[key] = candidate;
                }

                rawPointsDirty = droppedDuplicate;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to load persisted pins: {ex.Message}");
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

        private static string GetSaveFilePath(string worldName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] safeChars = new char[worldName.Length];
            for (int i = 0; i < worldName.Length; i++)
            {
                char c = worldName[i];
                safeChars[i] = Array.IndexOf(invalid, c) >= 0 ? '_' : c;
            }

            return Path.Combine(PinDataFolder, $"{new string(safeChars)}.txt");
        }
    }
}
