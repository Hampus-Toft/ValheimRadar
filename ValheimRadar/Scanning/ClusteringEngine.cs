using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    public static class ClusteringEngine
    {
        public static List<ItemCluster> ClusterItems(List<TrackedItem> items, float maxDistance)
        {
            List<ItemCluster> clusters = new List<ItemCluster>();

            foreach (var item in items)
            {
                bool addedToCluster = false;

                foreach (var cluster in clusters)
                {
                    if (cluster.DisplayName == item.DisplayName)
                    {
                        if (Vector3.Distance(cluster.GetCentroid(), item.Position) <= maxDistance)
                        {
                            cluster.Items.Add(item);
                            addedToCluster = true;
                            break;
                        }
                    }
                }

                if (!addedToCluster)
                {
                    ItemCluster newCluster = new ItemCluster
                    {
                        DisplayName = item.DisplayName,
                        PinType = item.PinType
                    };
                    newCluster.Items.Add(item);
                    clusters.Add(newCluster);
                }
            }

            return clusters;
        }
    }
}