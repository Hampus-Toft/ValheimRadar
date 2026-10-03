using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimRadar
{
    // Persistent (resource / physics-POI) clusters and how they reach the minimap, spread over frames so
    // no amount of saved data can stall the game:
    //
    //  - clusterGrid holds every visible-eligible persistent cluster (hidden points - dismissed, or
    //    picked and regrowing - are never in it). It's maintained incrementally: points join it as
    //    they're loaded from the world database (nearest cells first, see ContinueLoadingPoints) or
    //    scanned, and a ClusterDistance change refills a fresh grid from rebuildQueue, nearest points
    //    first, a few milliseconds per frame (ContinueRebuild).
    //  - Only clusters inside the part of the map currently shown, plus a margin (PinViewport), exist
    //    as Minimap pins (resourcePins). Scrolling or zooming the map past that area recomputes it:
    //    pins that left it are removed in one bulk pass (MinimapPinBulk), pins that entered it are
    //    queued and added nearest-to-the-view-centre first within a per-frame budget.
    //  - Changed clusters (dirtyClusters) have their pin added, moved, relabelled or removed on the
    //    next Tick.
    public static partial class PinManager
    {
        // Per-frame time for loading saved points and reclustering, then (on top) for adding queued pins.
        private const double WorkBudgetMs = 4.0;
        private const double PinBudgetMs = 3.0;

        private static ClusterGrid clusterGrid;
        private static readonly HashSet<ItemCluster> dirtyClusters = new HashSet<ItemCluster>();

        private static readonly Dictionary<ItemCluster, Minimap.PinData> resourcePins = new Dictionary<ItemCluster, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, ItemCluster> clusterByPin = new Dictionary<Minimap.PinData, ItemCluster>();

        // The world area pins are currently kept for (view + margin), and clusters inside it still
        // waiting for a pin, nearest to the view centre last.
        private static WorldRect keptRect;
        private static bool hasKeptRect;
        private static bool resourceViewDirty;
        private static readonly List<ItemCluster> pendingPinAdds = new List<ItemCluster>();

        // Points still to be re-added after a ClusterDistance change, nearest to the player last. A point
        // leaving the store or getting hidden meanwhile is dropped from rebuildPending, and skipped.
        private static readonly List<TrackedItem> rebuildQueue = new List<TrackedItem>();
        private static readonly HashSet<TrackedItem> rebuildPending = new HashSet<TrackedItem>();

        private static Vector3 lastPlayerPosition;
        private static readonly Stopwatch frameTimer = new Stopwatch();

        // Called every frame while in a world.
        public static void Tick(Minimap minimap, Vector3 playerPosition)
        {
            lastPlayerPosition = playerPosition;
            frameTimer.Restart();

            ContinueLoadingPoints();
            ContinueRebuild();

            if (minimap != null) SyncResourcePins(minimap);
        }

        private static bool WithinBudget(double budgetMs) => frameTimer.Elapsed.TotalMilliseconds < budgetMs;

        private static void AddToClusters(TrackedItem point)
        {
            if (clusterGrid == null || clusterGrid.ClusterOf(point) != null) return;
            dirtyClusters.Add(clusterGrid.Add(point));
        }

        // Takes one point out of whichever persistent cluster holds it (or the rebuild queue). The next
        // Tick moves/relabels the cluster's pin, or removes it when the cluster emptied.
        private static void DetachFromCluster(TrackedItem point, string reason)
        {
            rebuildPending.Remove(point);

            ItemCluster cluster = clusterGrid?.Remove(point);
            if (cluster == null) return;

            dirtyClusters.Add(cluster);
            if (cluster.Items.Count == 0) RadarLog.Diag($"[ValheimRadar] cluster-emptied name={cluster.DisplayName} reason={reason}");
        }

        // Starts reclustering every recorded point for maxDistance. Existing resource pins go at once;
        // the new clusters (and their pins) fill in over the next frames, nearest to the player first.
        public static void RebuildPersistentClusters(float maxDistance)
        {
            ClearResourcePins(Minimap.instance);
            clusterGrid = new ClusterGrid(maxDistance);
            clusteredMaxDistance = maxDistance;
            dirtyClusters.Clear();

            rebuildQueue.Clear();
            rebuildPending.Clear();
            foreach (TrackedItem point in rawPersistentPoints.Values)
            {
                if (!IsHidden(point)) rebuildQueue.Add(point);
            }

            PinViewport.SortNearestLast(rebuildQueue, p => p.Position, lastPlayerPosition);
            foreach (TrackedItem point in rebuildQueue) rebuildPending.Add(point);
        }

        private static void ContinueRebuild()
        {
            while (rebuildQueue.Count > 0 && WithinBudget(WorkBudgetMs))
            {
                TrackedItem point = rebuildQueue[rebuildQueue.Count - 1];
                rebuildQueue.RemoveAt(rebuildQueue.Count - 1);

                if (!rebuildPending.Remove(point) || IsHidden(point)) continue;
                AddToClusters(point);
            }
        }

        // --- Pin virtualization -----------------------------------------------------------------

        // The world area the minimap currently shows (the large map while it's open, else the small one).
        private static WorldRect CurrentView(Minimap minimap)
        {
            RawImage image = minimap.m_mode == Minimap.MapMode.Large ? minimap.m_mapImageLarge : minimap.m_mapImageSmall;
            Rect uv = image.uvRect;
            return PinViewport.UvToWorld(uv.xMin, uv.yMin, uv.xMax, uv.yMax, minimap.m_textureSize, minimap.m_pixelSize);
        }

        private static void SyncResourcePins(Minimap minimap)
        {
            if (clusterGrid == null) return;

            WorldRect view = CurrentView(minimap);
            if (!hasKeptRect || resourceViewDirty || PinViewport.NeedsReload(keptRect, view)) ReloadKeptArea(minimap, view);

            SyncDirtyClusters(minimap);

            double budget = frameTimer.Elapsed.TotalMilliseconds + PinBudgetMs;
            while (pendingPinAdds.Count > 0 && frameTimer.Elapsed.TotalMilliseconds < budget)
            {
                ItemCluster cluster = pendingPinAdds[pendingPinAdds.Count - 1];
                pendingPinAdds.RemoveAt(pendingPinAdds.Count - 1);

                if (resourcePins.ContainsKey(cluster) || !ShouldHavePin(cluster)) continue;
                AddResourcePin(minimap, cluster);
            }
        }

        private static bool ShouldHavePin(ItemCluster cluster) =>
            hasKeptRect && clusterGrid != null && clusterGrid.Contains(cluster) &&
            keptRect.Contains(clusterGrid.GetCentroid(cluster)) &&
            ObjectEvaluator.IsCategoryEnabled(cluster.CategoryKey);

        // Recomputes which clusters should have pins for the current view: drops pins outside the new
        // kept area at once and queues the missing ones, nearest to the view centre first.
        private static void ReloadKeptArea(Minimap minimap, WorldRect view)
        {
            keptRect = PinViewport.KeptRectFor(view);
            hasKeptRect = true;
            resourceViewDirty = false;

            var inside = new List<ItemCluster>();
            clusterGrid.Query(keptRect.MinX, keptRect.MinZ, keptRect.MaxX, keptRect.MaxZ, inside);

            var enabledByCategory = new Dictionary<string, bool>();
            var wanted = new HashSet<ItemCluster>();
            foreach (ItemCluster cluster in inside)
            {
                string category = cluster.CategoryKey ?? string.Empty;
                if (!enabledByCategory.TryGetValue(category, out bool enabled))
                {
                    enabled = ObjectEvaluator.IsCategoryEnabled(cluster.CategoryKey);
                    enabledByCategory[category] = enabled;
                }

                if (enabled) wanted.Add(cluster);
            }

            var leaving = new List<ItemCluster>();
            foreach (var kvp in resourcePins)
            {
                if (!wanted.Contains(kvp.Key)) leaving.Add(kvp.Key);
            }

            RemoveResourcePins(minimap, leaving);

            pendingPinAdds.Clear();
            foreach (ItemCluster cluster in wanted)
            {
                if (!resourcePins.ContainsKey(cluster)) pendingPinAdds.Add(cluster);
            }

            PinViewport.SortNearestLast(pendingPinAdds, c => clusterGrid.GetCentroid(c), view.Center);

            RadarLog.Diag($"[ValheimRadar] pins-view kept={keptRect.Width:F0}x{keptRect.Height:F0}m shown={resourcePins.Count} queued={pendingPinAdds.Count} removed={leaving.Count} clusters={clusterGrid.Count}");
        }

        private static void SyncDirtyClusters(Minimap minimap)
        {
            if (dirtyClusters.Count == 0) return;

            var leaving = new List<ItemCluster>();
            foreach (ItemCluster cluster in dirtyClusters)
            {
                bool want = ShouldHavePin(cluster);
                if (resourcePins.TryGetValue(cluster, out Minimap.PinData pin))
                {
                    if (want) UpdateResourcePin(pin, cluster);
                    else leaving.Add(cluster);
                }
                else if (want)
                {
                    AddResourcePin(minimap, cluster);
                }
            }

            dirtyClusters.Clear();
            RemoveResourcePins(minimap, leaving);
        }

        private static void AddResourcePin(Minimap minimap, ItemCluster cluster)
        {
            Sprite icon = ResourceIcon(cluster);
            Minimap.PinData pin = minimap.AddPin(clusterGrid.GetCentroid(cluster), Minimap.PinType.Icon3, ResourceLabel(cluster, icon), save: false, isChecked: false);
            pin.m_icon = icon;

            resourcePins[cluster] = pin;
            clusterByPin[pin] = cluster;
        }

        private static void UpdateResourcePin(Minimap.PinData pin, ItemCluster cluster)
        {
            Sprite icon = ResourceIcon(cluster);
            pin.m_pos = clusterGrid.GetCentroid(cluster);
            ApplyPinName(pin, ResourceLabel(cluster, icon));
            pin.m_icon = icon;
        }

        // A cluster's icon comes from the point that started it. When that came back null (e.g. resolved
        // before Jotunn's icon atlas was ready, right after connecting) it's retried each time the pin is
        // drawn, and kept once found.
        private static Sprite ResourceIcon(ItemCluster cluster)
        {
            if (cluster.Icon == null) cluster.Icon = TryResolveMissingIcon(cluster.CategoryKey, cluster.RawName);
            return cluster.Icon;
        }

        // An ore deposit or regrowing pickable whose icon already shows what it is needs no name - just a
        // cluster's count ("5x"), or nothing for a single one. Without such an icon it keeps its plain
        // label ("Silver", "3x Blueberry Bush").
        private static string ResourceLabel(ItemCluster cluster, Sprite icon)
        {
            if (ObjectEvaluator.HasIconOnlyLabel(cluster.CategoryKey) && IconIdentifiesType(icon, cluster.CategoryKey))
            {
                return ItemCluster.BuildLabel(cluster.DisplayName, cluster.Items.Count, hideName: true);
            }

            return LabelFor(cluster);
        }

        private static void RemoveResourcePins(Minimap minimap, List<ItemCluster> clusters)
        {
            if (clusters.Count == 0) return;

            var pins = new List<Minimap.PinData>(clusters.Count);
            foreach (ItemCluster cluster in clusters)
            {
                if (!resourcePins.TryGetValue(cluster, out Minimap.PinData pin)) continue;
                resourcePins.Remove(cluster);
                clusterByPin.Remove(pin);
                pins.Add(pin);
            }

            if (minimap != null) MinimapPinBulk.RemovePins(minimap, pins);
        }

        // Removes every resource pin from the map and forgets the kept area (the next Tick recomputes it).
        private static void ClearResourcePins(Minimap minimap)
        {
            RemoveResourcePins(minimap, new List<ItemCluster>(resourcePins.Keys));
            pendingPinAdds.Clear();
            hasKeptRect = false;
        }

        // Drops all persistent clustering state (disconnect / world change).
        private static void ResetPersistentClusters(Minimap minimap)
        {
            ClearResourcePins(minimap);
            resourcePins.Clear();
            clusterByPin.Clear();
            clusterGrid = null;
            clusteredMaxDistance = -1f;
            dirtyClusters.Clear();
            rebuildQueue.Clear();
            rebuildPending.Clear();
            resourceViewDirty = false;
        }
    }
}
