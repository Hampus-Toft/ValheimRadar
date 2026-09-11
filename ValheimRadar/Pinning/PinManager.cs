using BepInEx;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimRadar
{
    public static class PinManager
    {
        private static readonly Dictionary<string, Minimap.PinData> activeClusterPins = new Dictionary<string, Minimap.PinData>();
        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "MoreMapPins");

        public static void ClearAllPins()
        {
            if (Minimap.instance == null) return;

            foreach (var kvp in activeClusterPins)
            {
                if (kvp.Value != null) Minimap.instance.RemovePin(kvp.Value);
            }
            activeClusterPins.Clear();
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

                UpdateOrCreatePin(minimap, key, centerPos, label, cluster.PinType);
            }

            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!currentScanKeys.Contains(kvp.Key))
                {
                    if (kvp.Value != null) minimap.RemovePin(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove) activeClusterPins.Remove(key);
        }

        public static Minimap.PinType ResolvePerObjectPin(string rawName, string categoryDefaultPng)
        {
            string cleanKey = ObjectEvaluator.StripKnownPrefixes(rawName).ToLower();

            string specificPath = Path.Combine(ConfigIconFolder, $"{cleanKey}.png");
            if (File.Exists(specificPath))
            {
                return CustomPinLoader.RegisterPngAsPin(specificPath, Minimap.PinType.Icon3);
            }

            string categoryPath = Path.Combine(ConfigIconFolder, categoryDefaultPng);
            if (File.Exists(categoryPath))
            {
                return CustomPinLoader.RegisterPngAsPin(categoryPath, Minimap.PinType.Icon3);
            }

            return Minimap.PinType.Icon3;
        }

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, Minimap.PinType pinType)
        {
            if (activeClusterPins.TryGetValue(clusterKey, out Minimap.PinData existingPin))
            {
                if (existingPin != null)
                {
                    existingPin.m_pos = pos;
                    existingPin.m_name = name;
                }
            }
            else
            {
                Minimap.PinData newPin = minimap.AddPin(pos, pinType, name, save: false, isChecked: false);
                activeClusterPins.Add(clusterKey, newPin);
            }
        }
    }
}
