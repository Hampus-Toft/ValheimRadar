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
        public const string PluginVersion = "1.6.0";

        // How often to flush newly-discovered persistent (resource/structure) pin positions to
        // disk while connected, so a crash/alt-F4 doesn't lose more than this much progress.
        private const float PersistSaveInterval = 30f;

        private float timer = 0f;
        private float locationTimer = 0f;
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
            PinManager.SaveLocationPins(currentWorldName);
            PinManager.ClearAllPins();
            BatchScanner.Reset();
            LocationScanner.Reset();
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
                    PinManager.SaveLocationPins(currentWorldName);
                    PinManager.ClearAllPins();
                    BatchScanner.Reset();
                    LocationScanner.Reset();
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
                // through the exact same RebuildPersistentClusters + SyncPersistentClusters path a
                // ClusterDistance change uses, so a reloaded pin is never out of step with what the
                // next real scan would produce.
                currentWorldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
                PinManager.LoadWorldPins(currentWorldName);
                PinManager.RebuildPersistentClusters(ClusterDistance);
                PinManager.SyncPersistentClusters(Minimap.instance);
                PinManager.LoadLocationPins(currentWorldName);
                PinManager.DrawLoadedLocationPins(Minimap.instance);
                wasActive = true;
            }

            timer += Time.deltaTime;
            if (timer >= RadarConfig.UpdateInterval.Value)
            {
                timer = 0f;
                ScanAndPinObjects(Minimap.instance);
            }

            locationTimer += Time.deltaTime;
            if (locationTimer >= RadarConfig.LocationScanInterval.Value)
            {
                locationTimer = 0f;
                List<TrackedLocation> newLocations = LocationScanner.ScanLocations();
                if (newLocations.Count > 0) PinManager.RecordAndSyncLocations(Minimap.instance, newLocations);
            }

            saveTimer += Time.deltaTime;
            if (saveTimer >= PersistSaveInterval)
            {
                saveTimer = 0f;
                PinManager.SaveWorldPins(currentWorldName);
                PinManager.SaveLocationPins(currentWorldName);
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

            // Persistent (resource/structure) points merge into the durable raw store and are
            // clustered incrementally here - only newly-discovered points touch existing clusters, so
            // a session's full discovery history never gets reclustered from scratch on a normal tick
            // (a ClusterDistance change is handled separately - see RecordRawPoints). Pin updates are
            // then pushed only for whatever clusters actually changed.
            PinManager.RecordRawPoints(detectedItems, ClusterDistance);
            PinManager.SyncPersistentClusters(minimap);

            // Transient (creature) clusters are still fully rebuilt every tick, but the input here is
            // just this tick's in-range detections - bounded by ScanRadius, not the ever-growing
            // discovery history - so redoing it in full stays cheap.
            List<ItemCluster> transientClusters = ClusteringEngine.ClusterItems(transientItems, ClusterDistance);
            PinManager.SyncTransientClusters(minimap, transientClusters);
        }
    }
}