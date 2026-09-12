using BepInEx;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimRadar
{
    public static class PinManager
    {
        private class PinEntry
        {
            public Minimap.PinData Pin;
            public bool IsPersistent;
        }

        private static readonly Dictionary<string, PinEntry> activeClusterPins = new Dictionary<string, PinEntry>();
        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "MoreMapPins");

        public static void ClearAllPins(bool includePersistent = true)
        {
            if (Minimap.instance == null) return;

            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!includePersistent && kvp.Value.IsPersistent) continue;

                if (kvp.Value.Pin != null) Minimap.instance.RemovePin(kvp.Value.Pin);
                toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove) activeClusterPins.Remove(key);
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

                UpdateOrCreatePin(minimap, key, centerPos, label, cluster.PinType, cluster.IsPersistent);
            }

            // Persistent (resource/structure) pins stay on the map after their cluster leaves scan
            // range, since they're stationary - only transient (creature) pins get cleaned up here.
            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!currentScanKeys.Contains(kvp.Key) && !kvp.Value.IsPersistent)
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

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, Minimap.PinType pinType, bool isPersistent)
        {
            if (activeClusterPins.TryGetValue(clusterKey, out PinEntry existing))
            {
                if (existing.Pin != null)
                {
                    existing.Pin.m_pos = pos;
                    existing.Pin.m_name = name;
                }
                existing.IsPersistent = isPersistent;
            }
            else
            {
                Minimap.PinData newPin = minimap.AddPin(pos, pinType, name, save: false, isChecked: false);
                activeClusterPins.Add(clusterKey, new PinEntry { Pin = newPin, IsPersistent = isPersistent });
            }
        }
    }
}
