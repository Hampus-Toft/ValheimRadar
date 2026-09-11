using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    public class ItemCluster
    {
        public string DisplayName;
        public Minimap.PinType PinType;
        public List<TrackedItem> Items = new List<TrackedItem>();

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

        public string GetClusterKey()
        {
            if (Items.Count == 0) return string.Empty;
            return $"{DisplayName}_{Items[0].Zdoid.UserID}_{Items[0].Zdoid.ID}";
        }
    }
}