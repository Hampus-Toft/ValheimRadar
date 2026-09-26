using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    public static class ClusteringEngine
    {
        public static List<ItemCluster> ClusterItems(List<TrackedItem> items, float maxDistance)
        {
            List<ItemCluster> clusters = new List<ItemCluster>();
            foreach (var item in items) AddItem(clusters, item, maxDistance);
            return clusters;
        }

        // Adds a single item to whichever existing cluster it falls within (by DisplayName + distance
        // to that cluster's current centroid), or starts a new one - the same greedy rule ClusterItems
        // applies per item, exposed separately so PinManager can fold newly-discovered persistent
        // points into an already-built cluster list without reclustering everything from scratch (see
        // PinManager.RecordRawPoints). Order-dependent and non-optimal by design (two clusters formed
        // far apart in the insertion order never get merged after the fact even if they end up close),
        // but that's the exact behavior ClusterItems already had - preserving it here means incremental
        // and from-scratch clustering agree as long as items are processed in the same order.
        //
        // Ore deposits (ObjectEvaluator.IsOreDepositCategory) never join or accept others: each one
        // always becomes its own Standalone single-item cluster, i.e. its own pin.
        public static ItemCluster AddItem(List<ItemCluster> clusters, TrackedItem item, float maxDistance)
        {
            bool standalone = ObjectEvaluator.IsOreDepositCategory(item.CategoryKey);

            if (!standalone)
            {
                foreach (var cluster in clusters)
                {
                    if (cluster.Standalone) continue;

                    if (cluster.DisplayName == item.DisplayName && Vector3.Distance(cluster.GetCentroid(), item.Position) <= maxDistance)
                    {
                        cluster.Items.Add(item);
                        return cluster;
                    }
                }
            }

            ItemCluster newCluster = new ItemCluster
            {
                DisplayName = item.DisplayName,
                Icon = item.Icon,
                IsPersistent = item.IsPersistent,
                CategoryKey = item.CategoryKey,
                RawName = item.RawName,
                MaxDistance = maxDistance,
                Standalone = standalone
            };
            newCluster.Items.Add(item);
            clusters.Add(newCluster);
            return newCluster;
        }
    }
}
