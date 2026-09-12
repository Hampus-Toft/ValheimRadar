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
            public string RawName;
            public Minimap.PinType PinType;
        }

        private static readonly Dictionary<string, PinEntry> activeClusterPins = new Dictionary<string, PinEntry>();
        private static bool isDirty;

        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "MoreMapPins");
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
                    entry.Pin = minimap.AddPin(entry.Position, entry.PinType, entry.Label, save: false, isChecked: false);
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

                UpdateOrCreatePin(minimap, key, centerPos, label, cluster.RawName, cluster.PinType, cluster.IsPersistent, cluster.CategoryKey);
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

        public static Minimap.PinType ResolvePerObjectPin(string rawName, string categoryDefaultPng, string vanillaItemPrefab = null)
        {
            string cleanKey = ObjectEvaluator.StripKnownPrefixes(rawName).ToLower();

            // 1. User-supplied PNG for this exact type (e.g. MoreMapPins/wolf.png).
            string specificPath = Path.Combine(ConfigIconFolder, $"{cleanKey}.png");
            if (File.Exists(specificPath))
            {
                return CustomPinLoader.RegisterPngAsPin(specificPath, Minimap.PinType.Icon3);
            }

            // 2. User-supplied PNG for the whole category (e.g. MoreMapPins/monster.png).
            string categoryPath = Path.Combine(ConfigIconFolder, categoryDefaultPng);
            if (File.Exists(categoryPath))
            {
                return CustomPinLoader.RegisterPngAsPin(categoryPath, Minimap.PinType.Icon3);
            }

            // 3. No custom PNG anywhere - fall back to the vanilla game's own icon for this type
            // (creature Trophy icon / resource pickup icon) so distinct types still look distinct.
            if (VanillaIconResolver.TryResolveIcon(cleanKey, vanillaItemPrefab, out Sprite vanillaSprite))
            {
                string cacheKey = VanillaIconResolver.GetCacheKey(cleanKey, vanillaItemPrefab);
                return CustomPinLoader.RegisterSpriteAsPin(cacheKey, vanillaSprite, Minimap.PinType.Icon3);
            }

            return Minimap.PinType.Icon3;
        }

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, string rawName, Minimap.PinType pinType, bool isPersistent, string categoryKey)
        {
            bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);

            if (activeClusterPins.TryGetValue(clusterKey, out PinEntry existing))
            {
                existing.Position = pos;
                existing.Label = name;
                existing.RawName = rawName;
                existing.PinType = pinType;
                existing.CategoryKey = categoryKey;

                if (existing.Pin != null)
                {
                    existing.Pin.m_pos = pos;
                    existing.Pin.m_name = name;
                }

                if (isPersistent && !existing.IsPersistent) isDirty = true;
                existing.IsPersistent = isPersistent;

                if (categoryEnabled && existing.Pin == null)
                {
                    existing.Pin = minimap.AddPin(pos, pinType, name, save: false, isChecked: false);
                }
                else if (!categoryEnabled && existing.Pin != null)
                {
                    minimap.RemovePin(existing.Pin);
                    existing.Pin = null;
                }
            }
            else
            {
                Minimap.PinData newPin = categoryEnabled
                    ? minimap.AddPin(pos, pinType, name, save: false, isChecked: false)
                    : null;

                activeClusterPins.Add(clusterKey, new PinEntry
                {
                    Pin = newPin,
                    IsPersistent = isPersistent,
                    CategoryKey = categoryKey,
                    Position = pos,
                    Label = name,
                    RawName = rawName,
                    PinType = pinType
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
        public static void SaveWorldPins(string worldName)
        {
            if (!isDirty || string.IsNullOrEmpty(worldName)) return;

            List<string> lines = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!kvp.Value.IsPersistent) continue;

                PinEntry e = kvp.Value;
                lines.Add(string.Join("|",
                    Escape(kvp.Key),
                    e.Position.x.ToString(CultureInfo.InvariantCulture),
                    e.Position.y.ToString(CultureInfo.InvariantCulture),
                    e.Position.z.ToString(CultureInfo.InvariantCulture),
                    Escape(e.Label),
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
        public static void LoadWorldPins(string worldName, Minimap minimap)
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
                    if (parts.Length != 7) continue;

                    string key = Unescape(parts[0]);
                    if (string.IsNullOrEmpty(key) || activeClusterPins.ContainsKey(key)) continue;

                    if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                    if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

                    string label = Unescape(parts[4]);
                    string rawName = Unescape(parts[5]);
                    string categoryKey = Unescape(parts[6]);

                    Vector3 pos = new Vector3(x, y, z);

                    // Custom pin icon indices are only valid for the Minimap instance they were
                    // registered against, so re-resolve the PinType for the current session instead
                    // of persisting the raw enum value.
                    string iconPng = ObjectEvaluator.GetDefaultIconForCategory(categoryKey);
                    Minimap.PinType pinType = string.IsNullOrEmpty(iconPng)
                        ? Minimap.PinType.Icon3
                        : ResolvePerObjectPin(rawName, iconPng);

                    bool categoryEnabled = ObjectEvaluator.IsCategoryEnabled(categoryKey);
                    Minimap.PinData pin = categoryEnabled
                        ? minimap.AddPin(pos, pinType, label, save: false, isChecked: false)
                        : null;

                    activeClusterPins[key] = new PinEntry
                    {
                        Pin = pin,
                        IsPersistent = true,
                        CategoryKey = categoryKey,
                        Position = pos,
                        Label = label,
                        RawName = rawName,
                        PinType = pinType
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
