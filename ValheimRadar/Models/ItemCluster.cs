using System.Collections.Generic;
using System.Linq;
using System.Text;
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

            var sortedIds = Items
                .Select(item => $"{item.Zdoid.UserID}:{item.Zdoid.ID}")
                .OrderBy(id => id, System.StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append(DisplayName).Append('_');
            foreach (var id in sortedIds) sb.Append(id).Append('|');

            return sb.ToString();
        }
    }
}