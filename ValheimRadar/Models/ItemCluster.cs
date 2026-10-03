using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    public class ItemCluster
    {
        public string DisplayName;
        public Sprite Icon;
        public bool IsPersistent;
        public string CategoryKey;
        public string RawName;
        public float MaxDistance = 1f;
        public List<TrackedItem> Items = new List<TrackedItem>();

        // Set by ClusteringEngine for categories that are never grouped (ore deposits): the cluster
        // only ever holds its one item, and GetClusterKey keys it by that item's exact position.
        public bool Standalone;

        public Vector3 GetCentroid()
        {
            if (Items == null || Items.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var item in Items) sum += item.Position;
            return sum / Items.Count;
        }

        public string GetLabel()
        {
            return Items.Count > 1 ? $"{Items.Count}x {DisplayName}" : DisplayName;
        }

        // Label variant used by PinManager: when hideName is true the species/type name is dropped and
        // only the count and rolled star rating remain (see BuildLabel).
        public string GetLabel(bool hideName) => BuildLabel(DisplayName, Items.Count, hideName);

        // Pure label builder. With hideName == false this is exactly the historic label ("Boar (★★)",
        // "2x Boar (★★)"). With hideName == true the name is removed but the count and star rating are
        // preserved: "2x ★★" for a 2-pin cluster of 2-star creatures, "★★" for a single one, "2x" for two
        // unstarred, and an empty string for a single unstarred creature (the icon alone identifies it).
        public static string BuildLabel(string displayName, int count, bool hideName)
        {
            if (string.IsNullOrEmpty(displayName)) displayName = string.Empty;

            if (!hideName)
            {
                return count > 1 ? $"{count}x {displayName}" : displayName;
            }

            TrySplitStarSuffix(displayName, out _, out string stars);
            if (count > 1)
            {
                return stars.Length > 0 ? $"{count}x {stars}" : $"{count}x";
            }

            return stars;
        }

        // CreatureEvaluator appends " (★★)" (one ★ per rolled star) to a starred creature's display name.
        // Splits that suffix back off. Returns false (baseName == displayName, stars == "") when there is
        // no star suffix - including a name that merely ends in some other parenthesised text.
        public static bool TrySplitStarSuffix(string displayName, out string baseName, out string stars)
        {
            baseName = displayName ?? string.Empty;
            stars = string.Empty;

            if (baseName.Length < 5 || baseName[baseName.Length - 1] != ')') return false;

            int open = baseName.LastIndexOf(" (", System.StringComparison.Ordinal);
            if (open < 0) return false;

            string inner = baseName.Substring(open + 2, baseName.Length - open - 3);
            if (inner.Length == 0) return false;

            foreach (char c in inner)
            {
                if (c != '★') return false;
            }

            stars = inner;
            baseName = baseName.Substring(0, open);
            return true;
        }

        // True when a pin's name should be dropped from its label (issue: icons already identify
        // creatures, so names are clutter). Only species-specific creature categories qualify, and only
        // when an icon actually resolved - a pin with no icon, or one of the generic unlisted-creature
        // fallbacks ("creature:_monster"/"creature:_animal", which share one generic icon), would
        // otherwise be unidentifiable. Resources and Locations always keep their names.
        public static bool ShouldHideCreatureName(string categoryKey, bool hasIcon, bool showCreatureNames)
        {
            if (showCreatureNames || !hasIcon || string.IsNullOrEmpty(categoryKey)) return false;
            if (!categoryKey.StartsWith("creature:", System.StringComparison.Ordinal)) return false;

            return !categoryKey.StartsWith("creature:_", System.StringComparison.Ordinal);
        }

        // Keyed on the cluster's spatial location (quantized to the clustering distance), not the
        // exact set of member ZDOIDs. Which individual items land inside a stationary resource
        // cluster on any given scan tick is volatile - it depends on scan-radius timing, ZDO load
        // order after a relog, etc. - while the cluster's location is not. A membership-based key
        // changes every time that composition shifts, and PinManager (correctly) treats a changed
        // key as a brand-new cluster, permanently stacking duplicate persistent pins ("Dandelion",
        // "2x Dandelion", "3x Dandelion", ...) for what is really a single spot.
        //
        // A Standalone cluster is one object that never moves, so its own position (to 0.1 m) is a
        // stable key - and, unlike the ClusterDistance grid, keeps two deposits a few meters apart
        // from sharing a key (and so a single pin).
        public string GetClusterKey()
        {
            if (Items.Count == 0) return string.Empty;

            if (Standalone)
            {
                Vector3 p = Items[0].Position;
                return $"{DisplayName}@{Mathf.RoundToInt(p.x * 10f)}_{Mathf.RoundToInt(p.z * 10f)}";
            }

            Vector3 centroid = GetCentroid();
            float gridSize = MaxDistance > 0f ? MaxDistance : 1f;
            int gx = Mathf.RoundToInt(centroid.x / gridSize);
            int gz = Mathf.RoundToInt(centroid.z / gridSize);

            return $"{DisplayName}_{gx}_{gz}";
        }
    }
}