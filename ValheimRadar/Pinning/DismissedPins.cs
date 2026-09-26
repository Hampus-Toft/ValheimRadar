using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimRadar
{
    // Pure (no Minimap/game state) record of pins the player has manually dismissed, so it can be unit
    // tested and persisted independently of PinManager. Entries are keyed by categoryKey + world
    // position rather than by cluster key or ZDOID:
    //  - cluster keys shift as clusters grow/regroup (ItemCluster.GetClusterKey), and a dismissal has to
    //    survive that;
    //  - ZDOIDs aren't stable across sessions for some world objects (see PinManager.DuplicatePointRadius),
    //    while stationary objects and Locations always sit at the same position;
    //  - a category + position match affects only the dismissed points, never the rest of the category.
    // Dismissing a persistent cluster records every member point, so "the pin" and "its points" mean the
    // same thing to the store.
    public sealed class DismissedPinStore
    {
        // Two points of the same category closer than this (XZ) count as the same object. Deliberately
        // small - it only has to absorb float round-tripping through the save file and the tiny position
        // jitter PinManager.DuplicatePointRadius already tolerates for "the same object rediscovered".
        public const float MatchRadius = 0.5f;

        private const string HeaderLine = "#dismissedv1";

        private readonly Dictionary<string, List<Vector3>> byCategory = new Dictionary<string, List<Vector3>>();

        public int Count { get; private set; }

        public void Add(string categoryKey, Vector3 position)
        {
            if (categoryKey == null) categoryKey = string.Empty;
            if (Contains(categoryKey, position)) return;

            if (!byCategory.TryGetValue(categoryKey, out List<Vector3> list))
            {
                list = new List<Vector3>();
                byCategory[categoryKey] = list;
            }

            list.Add(position);
            Count++;
        }

        public bool Contains(string categoryKey, Vector3 position)
        {
            if (Count == 0) return false;
            if (categoryKey == null) categoryKey = string.Empty;
            if (!byCategory.TryGetValue(categoryKey, out List<Vector3> list)) return false;

            foreach (Vector3 dismissed in list)
            {
                if (DistanceXZ(dismissed, position) <= MatchRadius) return true;
            }

            return false;
        }

        // Forgets every entry of this category within MatchRadius of position; returns whether any was
        // removed. Used once the dismissed object itself is confirmed gone, so the store doesn't keep
        // growing with entries for mined-out resources.
        public bool Remove(string categoryKey, Vector3 position)
        {
            if (Count == 0) return false;
            if (categoryKey == null) categoryKey = string.Empty;
            if (!byCategory.TryGetValue(categoryKey, out List<Vector3> list)) return false;

            int removed = list.RemoveAll(dismissed => DistanceXZ(dismissed, position) <= MatchRadius);
            if (removed == 0) return false;

            Count -= removed;
            if (list.Count == 0) byCategory.Remove(categoryKey);
            return true;
        }

        public void Clear()
        {
            byCategory.Clear();
            Count = 0;
        }

        // One line per dismissed point (categoryKey|x|y|z, categoryKey URI-escaped so it can never
        // contain the '|' delimiter), preceded by a version header. Same pipe-delimited style as the
        // PinData files, in its own sibling file so existing save files are untouched.
        public List<string> Serialize()
        {
            var lines = new List<string> { HeaderLine };
            foreach (var kvp in byCategory)
            {
                foreach (Vector3 p in kvp.Value)
                {
                    lines.Add(string.Join("|",
                        Uri.EscapeDataString(kvp.Key),
                        p.x.ToString(CultureInfo.InvariantCulture),
                        p.y.ToString(CultureInfo.InvariantCulture),
                        p.z.ToString(CultureInfo.InvariantCulture)));
                }
            }

            return lines;
        }

        // Replaces the store's contents with the parsed lines; malformed lines and the header are skipped.
        public void Load(IEnumerable<string> lines)
        {
            Clear();
            if (lines == null) return;

            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || line[0] == '#') continue;

                string[] parts = line.Split('|');
                if (parts.Length != 4) continue;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
                if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

                Add(Uri.UnescapeDataString(parts[0]), new Vector3(x, y, z));
            }
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    // Pure selection logic for "which pin did the player click", kept separate from the Harmony patch
    // so it can be unit tested.
    public static class PinDismissal
    {
        // Index of the candidate closest to target (XZ distance, strictly inside radius), or -1 when none
        // is in range. Mirrors how vanilla Minimap.GetClosestPin picks a pin for right-click removal.
        public static int IndexOfClosest(IList<Vector3> positions, Vector3 target, float radius)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < positions.Count; i++)
            {
                float dx = positions[i].x - target.x;
                float dz = positions[i].z - target.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < radius && dist < bestDist)
                {
                    best = i;
                    bestDist = dist;
                }
            }

            return best;
        }

        // Only persistent (resource / Location) pins can be dismissed: creature clusters are rebuilt from
        // scratch every scan tick and have no stable identity to remember.
        public static bool IsDismissible(bool isPersistent, bool isCurrentlyShown) => isPersistent && isCurrentlyShown;
    }
}
