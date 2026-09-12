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

        // Cluster key as of the last time this cluster's pin was pushed to the minimap. Only used for
        // incrementally-maintained persistent clusters (see PinManager.SyncPersistentClusters), which -
        // unlike transient clusters - persist as the same live object across ticks: adding an item can
        // shift the centroid enough to cross a grid boundary in GetClusterKey(), and without this the
        // old key's pin entry would never get evicted, leaving an orphaned duplicate pin behind.
        public string LastSyncedKey;

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

        // Keyed on the cluster's spatial location (quantized to the clustering distance), not the
        // exact set of member ZDOIDs. Which individual items land inside a stationary resource
        // cluster on any given scan tick is volatile - it depends on scan-radius timing, ZDO load
        // order after a relog, etc. - while the cluster's location is not. A membership-based key
        // changes every time that composition shifts, and PinManager (correctly) treats a changed
        // key as a brand-new cluster, permanently stacking duplicate persistent pins ("Dandelion",
        // "2x Dandelion", "3x Dandelion", ...) for what is really a single spot.
        public string GetClusterKey()
        {
            if (Items.Count == 0) return string.Empty;

            Vector3 centroid = GetCentroid();
            float gridSize = MaxDistance > 0f ? MaxDistance : 1f;
            int gx = Mathf.RoundToInt(centroid.x / gridSize);
            int gz = Mathf.RoundToInt(centroid.z / gridSize);

            return $"{DisplayName}_{gx}_{gz}";
        }
    }
}