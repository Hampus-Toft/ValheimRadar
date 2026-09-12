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
        private static bool isDirty;

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
            isDirty = false;
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

        public static void SyncClusterPins(Minimap minimap, List<ItemCluster> clusters)
        {
            HashSet<string> currentScanKeys = new HashSet<string>();

            foreach (var cluster in clusters)
            {
                string key = cluster.GetClusterKey();
                if (string.IsNullOrEmpty(key)) continue;

                currentScanKeys.Add(key);
                Vector3 centerPos = cluster.GetCentroid();
                string label = cluster.GetLabel();

                UpdateOrCreatePin(minimap, key, centerPos, label, cluster.DisplayName, cluster.RawName, cluster.Icon, cluster.IsPersistent, cluster.CategoryKey);
            }

            // Persistent (resource/structure) pins stay on the map after their cluster leaves scan
            // range, since they're stationary - only transient (creature) pins get cleaned up here.
            // A disabled-category entry is left alone even if absent from this scan (it's filtered out
            // by ObjectEvaluator at the source regardless of whether it's still nearby) so its cached
            // position survives being toggled off, instead of being evicted as "out of range".
            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!currentScanKeys.Contains(kvp.Key) && !kvp.Value.IsPersistent && ObjectEvaluator.IsCategoryEnabled(kvp.Value.CategoryKey))
                {
                    if (kvp.Value.Pin != null) minimap.RemovePin(kvp.Value.Pin);
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove) activeClusterPins.Remove(key);
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

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, string displayName, string rawName, Sprite icon, bool isPersistent, string categoryKey)
        {
            bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);

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

                if (isPersistent && !existing.IsPersistent) isDirty = true;
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

                if (isPersistent) isDirty = true;
            }
        }

        // Only stationary resource/structure clusters are written to disk - creature positions are
        // transient by nature (they move or die) and would just go stale, so they're re-discovered by
        // scanning each session instead of being persisted.
        //
        // Serialized as one pipe-delimited line per cluster (Unity's JsonUtility needs an assembly
        // this project doesn't reference, and the data is simple enough not to warrant adding one).
        //
        // The cluster key and display label are deliberately NOT persisted - both are re-derived from
        // (position, DisplayName) via ItemCluster on load, the same way a live scan computes them, so
        // a reloaded pin lines up with whatever a fresh scan produces instead of drifting into a
        // separate, overlapping duplicate.
        public static void SaveWorldPins(string worldName)
        {
            if (!isDirty || string.IsNullOrEmpty(worldName)) return;

            List<string> lines = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!kvp.Value.IsPersistent) continue;

                PinEntry e = kvp.Value;
                lines.Add(string.Join("|",
                    e.Position.x.ToString(CultureInfo.InvariantCulture),
                    e.Position.y.ToString(CultureInfo.InvariantCulture),
                    e.Position.z.ToString(CultureInfo.InvariantCulture),
                    Escape(e.DisplayName),
                    Escape(e.RawName),
                    Escape(e.CategoryKey)));
            }

            try
            {
                Directory.CreateDirectory(PinDataFolder);
                File.WriteAllLines(GetSaveFilePath(worldName), lines);
                isDirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to save persisted pins: {ex.Message}");
            }
        }

        // Restores previously discovered resource/structure pins for this world so the map doesn't
        // start blank after a relog. Called once right after connecting, before the first scan tick.
        //
        // clusterDistance must match the value the live scan clusters with (RadarConfig.ClusterDistance)
        // so a reloaded pin's re-derived key lands in the same spatial bucket a fresh scan of the same
        // spot would compute - otherwise the loaded pin and the next scan's pin for the same resource
        // patch would carry different keys and stack as two overlapping pins instead of one being updated.
        public static void LoadWorldPins(string worldName, Minimap minimap, float clusterDistance)
        {
            activeClusterPins.Clear();
            isDirty = false;

            if (string.IsNullOrEmpty(worldName) || minimap == null) return;

            string path = GetSaveFilePath(worldName);
            if (!File.Exists(path)) return;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split('|');
                    if (parts.Length != 6) continue;

                    if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                    if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

                    string displayName = Unescape(parts[3]);
                    string rawName = Unescape(parts[4]);
                    string categoryKey = Unescape(parts[5]);

                    if (string.IsNullOrEmpty(displayName)) continue;

                    Vector3 pos = new Vector3(x, y, z);

                    // Re-derive the key and label the same way a live scan would, from a single-item
                    // cluster at this position - see the comment above.
                    ItemCluster syntheticCluster = new ItemCluster { DisplayName = displayName, MaxDistance = clusterDistance };
                    syntheticCluster.Items.Add(new TrackedItem { Position = pos, DisplayName = displayName });
                    string key = syntheticCluster.GetClusterKey();
                    string label = syntheticCluster.GetLabel();

                    if (string.IsNullOrEmpty(key) || activeClusterPins.ContainsKey(key)) continue;

                    string iconPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
                    string vanillaIcon = ObjectEvaluator.GetVanillaIconForCategory(categoryKey);
                    Sprite icon = string.IsNullOrEmpty(iconPng)
                        ? null
                        : ResolvePerObjectPin(rawName, iconPng, vanillaIcon);

                    bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);
                    Minimap.PinData pin = null;
                    if (categoryEnabled)
                    {
                        pin = minimap.AddPin(pos, Minimap.PinType.Icon3, label, save: false, isChecked: false);
                        pin.m_icon = icon;
                    }

                    activeClusterPins[key] = new PinEntry
                    {
                        Pin = pin,
                        IsPersistent = true,
                        CategoryKey = categoryKey,
                        Position = pos,
                        Label = label,
                        DisplayName = displayName,
                        RawName = rawName,
                        Icon = icon
                    };
                }
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
