using BepInEx;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class RadarPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.yourname.valheimradar";
        public const string PluginName = "ValheimRadar";
        public const string PluginVersion = "1.5.1";

        // How often to flush newly-discovered persistent (resource/structure) pin positions to
        // disk while connected, so a crash/alt-F4 doesn't lose more than this much progress.
        private const float PersistSaveInterval = 30f;

        private float timer = 0f;
        private float saveTimer = 0f;
        private bool wasActive = false;
        private string currentWorldName;

        private static float ClusterDistance => RadarConfig.ClusterDistance != null ? RadarConfig.ClusterDistance.Value : 15.0f;

        private void Awake()
        {
            RadarConfig.Initialize(Config);
            Config.SettingChanged += OnConfigurationChanged;
            Logger.LogInfo($"{PluginName} initialized!");
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnConfigurationChanged;
            PinManager.SaveWorldPins(currentWorldName);
            PinManager.ClearAllPins();
            BatchScanner.Reset();
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            // Re-derive which categories should be visible and add/remove their minimap pins
            // accordingly - cached cluster positions are never discarded here, so re-enabling a
            // category instantly redraws its pins at their last known location instead of waiting
            // for the player to walk back into scan range.
            PinManager.RefreshCategoryVisibility(Minimap.instance);

            // Any config change (ScanRadius/ScanBatchCount especially) can invalidate the current
            // batch rotation, so drop it and let ScanBatch rebuild cleanly from the next tick.
            BatchScanner.Reset();
        }

        private void Update()
        {
            if (Player.m_localPlayer == null || Minimap.instance == null)
            {
                // Player disconnected or the world unloaded - persist what we've found so far and
                // drop in-memory pin state now, since it belongs to a Minimap instance that's about
                // to become invalid anyway.
                if (wasActive)
                {
                    PinManager.SaveWorldPins(currentWorldName);
                    PinManager.ClearAllPins();
                    BatchScanner.Reset();
                    wasActive = false;
                    currentWorldName = null;
                    saveTimer = 0f;
                }
                return;
            }

            if (!wasActive)
            {
                // Freshly connected/reloaded - reload this world's previously discovered
                // resource/structure points and draw them immediately (rather than waiting for the
                // first scan tick) so the map doesn't start blank after a relog. Clustering runs
                // through the exact same ClusterItems + SyncClusterPins path a live scan uses, so a
                // reloaded pin is never out of step with what the next real scan would produce.
                currentWorldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
                PinManager.LoadWorldPins(currentWorldName);
                List<ItemCluster> loadedClusters = ClusteringEngine.ClusterItems(PinManager.GetAllRawPersistentPoints(), ClusterDistance);
                PinManager.SyncClusterPins(Minimap.instance, loadedClusters);
                wasActive = true;
            }

            timer += Time.deltaTime;
            if (timer < RadarConfig.UpdateInterval.Value) return;
            timer = 0f;

            ScanAndPinObjects(Minimap.instance);

            saveTimer += Time.deltaTime;
            if (saveTimer >= PersistSaveInterval)
            {
                saveTimer = 0f;
                PinManager.SaveWorldPins(currentWorldName);
            }
        }

        private void ScanAndPinObjects(Minimap minimap)
        {
            Vector3 playerPos = Player.m_localPlayer.transform.position;

            // Only a rotating slice of the scan area's cells is physically re-queried this tick - see
            // BatchScanner. detectedItems is the combined (cached + freshly-scanned) set for every
            // cell currently in range, so it always represents the full radius, just not all of it
            // freshly re-scanned on every single tick.
            List<TrackedItem> detectedItems = BatchScanner.ScanBatch(playerPos, RadarConfig.ScanRadius.Value, RadarConfig.ScanBatchCount.Value);

            List<TrackedItem> transientItems = new List<TrackedItem>();
            foreach (var item in detectedItems)
            {
                if (!item.IsPersistent) transientItems.Add(item);
            }

            // Persistent (resource/structure) points merge into the durable raw store here, then get
            // clustered below from that FULL history - not just what's in range this tick - so a
            // ClusterDistance change or a newly-discovered nearby point reclusters the whole known
            // area consistently instead of leaving a stale, differently-counted duplicate pin behind.
            PinManager.RecordRawPoints(detectedItems);

            List<TrackedItem> clusterInput = PinManager.GetAllRawPersistentPoints();
            clusterInput.AddRange(transientItems);

            List<ItemCluster> clusters = ClusteringEngine.ClusterItems(clusterInput, ClusterDistance);

            PinManager.SyncClusterPins(minimap, clusters);
        }
    }
}