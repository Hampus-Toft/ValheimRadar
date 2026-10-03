using BepInEx;
using HarmonyLib;
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
        public const string PluginVersion = "1.12.0";

        // How often queued pin changes are written to the world's database (PinManager.FlushPersistence) -
        // only what changed, in one small transaction, so this can be short: a crash/alt-F4 loses at
        // most this much. ClearAllPins also flushes on disconnect.
        private const float PersistSaveInterval = 2f;

        // Troubleshooting only (RadarConfig.DiagnosticLogging): while the large map is open, log the pin-name
        // state a few times per session, never continuously.
        private const float DiagnosticInterval = 8f;
        private const int MaxDiagnosticLogs = 6;

        private float diagnosticTimer = 0f;
        private int diagnosticLogCount = 0;
        private float timer = 0f;
        private float locationTimer = 0f;
        private float saveTimer = 0f;
        private bool wasActive = false;
        private string currentWorldName;
        private Harmony harmony;

        private static float ClusterDistance => RadarConfig.ClusterDistance != null ? RadarConfig.ClusterDistance.Value : 15.0f;

        private void Awake()
        {
            RadarConfig.Initialize(Config);
            Config.SettingChanged += OnConfigurationChanged;

            try
            {
                harmony = new Harmony(PluginGUID);
                harmony.PatchAll(typeof(MinimapPatches));
            }
            catch (Exception ex)
            {
                // Only manual pin removal depends on the patch - the rest of the plugin works without it.
                Logger.LogError($"Failed to apply Minimap patches (right-click pin removal disabled): {ex}");
            }

            // Guards each target itself - see DepletionPatches.Patch.
            if (harmony != null) DepletionPatches.Apply(harmony);

            Logger.LogInfo($"{PluginName} initialized!");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Config.SettingChanged -= OnConfigurationChanged;
            PinManager.ClearAllPins();
            CreatureScanner.Reset();
            ResourceScanner.Reset();
            PoiScanner.Reset();
            LocationScanner.Reset();
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            // "Restore Dismissed Pins" acts as a button: bring back every dismissed pin, then reset the
            // toggle (which re-enters this handler once with the value false, doing nothing here).
            if (RadarConfig.ClearDismissedPins != null && RadarConfig.ClearDismissedPins.Value)
            {
                RadarConfig.ClearDismissedPins.Value = false;
                PinManager.RestoreDismissedPins(Minimap.instance);
            }

            // Re-derive which categories should be visible and add/remove their minimap pins
            // accordingly - cached cluster positions are never discarded here, so re-enabling a
            // category instantly redraws its pins at their last known location instead of waiting
            // for the player to walk back into scan range.
            PinManager.RefreshCategoryVisibility(Minimap.instance);

            // Turning "Hide Picked Until Respawn" off shows the pickables it was hiding right away.
            if (RadarConfig.HidePickedUntilRespawn != null && !RadarConfig.HidePickedUntilRespawn.Value)
            {
                PinManager.ClearRespawnTimers();
            }

            // Only the creature scanner's rotation cache is reset here (ScanRadius/ScanBatchCount
            // especially can invalidate it). ResourceScanner/PoiScanner deliberately do NOT reset on
            // config changes - already-explored ground stays valid regardless of toggles and is
            // refreshed by budgeted rescans anyway (see PermanentSpatialScanner), so resetting them
            // here would only force a wasteful burst of re-scanning it.
            CreatureScanner.Reset();
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
                    PinManager.ClearAllPins();
                    CreatureScanner.Reset();
                    ResourceScanner.Reset();
                    PoiScanner.Reset();
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
                diagnosticTimer = 0f;
                diagnosticLogCount = 0;
                if (RadarConfig.RaisePlayerMarker == null || RadarConfig.RaisePlayerMarker.Value)
                {
                    MinimapMarkerOrder.Apply(Minimap.instance);
                }
                PinManager.OpenWorld(currentWorldName);
                PinManager.RebuildPersistentClusters(ClusterDistance);
                PinManager.SyncPersistentClusters(Minimap.instance);
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

            if (Minimap.instance.m_mode == Minimap.MapMode.Large && (RadarConfig.DiagnosticLogging == null || RadarConfig.DiagnosticLogging.Value))
            {
                diagnosticTimer += Time.deltaTime;
                if (diagnosticTimer >= DiagnosticInterval && diagnosticLogCount < MaxDiagnosticLogs)
                {
                    diagnosticTimer = 0f;
                    diagnosticLogCount++;
                    PinManager.LogNameDiagnostics(Minimap.instance);
                }
            }

            saveTimer += Time.deltaTime;
            if (saveTimer >= PersistSaveInterval)
            {
                saveTimer = 0f;
                PinManager.FlushPersistence();
            }
        }

        private void ScanAndPinObjects(Minimap minimap)
        {
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            float scanRadius = RadarConfig.ScanRadius.Value;

            // Type #1 (ephemeral) - creatures/fish/Leviathan move, so the active radius is
            // re-evaluated (via a rotating per-cell cache - see CreatureScanner/SpatialCellScanner)
            // and fully reclustered every discovery tick. The clustering/pin-diff work this involves
            // only ever touches this tick's in-range detections though - bounded by ScanRadius, not
            // any ever-growing discovery history - so redoing it in full each tick stays cheap.
            List<TrackedItem> creatures = CreatureScanner.ScanBatch(playerPos, scanRadius, RadarConfig.ScanBatchCount.Value);
            List<ItemCluster> creatureClusters = ClusteringEngine.ClusterItems(creatures, ClusterDistance);
            PinManager.SyncTransientClusters(minimap, creatureClusters);

            // Type #2/#3 (semi-permanent) - resources and physics-detected points of interest don't
            // move, so each scanner only queries cells that are new, newly loaded, or due for a
            // budgeted rescan (see PermanentSpatialScanner) - usually none once the surroundings are
            // explored. New points merge into the durable raw store and are clustered incrementally,
            // so a session's full discovery history never gets reclustered from scratch on a normal
            // tick (a ClusterDistance change is handled separately - see PinManager.RecordRawPoints);
            // depletable points a verified rescan no longer finds are removed. Pin updates are then
            // pushed only for whatever clusters actually changed.
            float now = Time.time;
            float rescanInterval = RadarConfig.ResourceRescanInterval.Value;
            bool removeDepleted = RadarConfig.RemoveDepletedResources.Value;
            List<ScannedCell> resourceCells = ResourceScanner.Scan(playerPos, scanRadius, now, rescanInterval);
            List<ScannedCell> poiCells = PoiScanner.Scan(playerPos, scanRadius, now, rescanInterval);

            // Called unconditionally, even with nothing scanned - RecordRawPoints (inside) also
            // checks every call for a ClusterDistance config change and triggers a full recluster if
            // so, which must keep happening on a fully-explored map too.
            PinManager.RecordScannedCells(resourceCells, ClusterDistance, now, removeDepleted);
            PinManager.RecordScannedCells(poiCells, ClusterDistance, now, removeDepleted);

            // Picked berries/mushrooms/etc. whose respawn time has passed (world time) go back on the map.
            if (ZNet.instance != null) PinManager.UpdateRespawnTimers(ZNet.instance.GetTimeSeconds());
            PinManager.SyncPersistentClusters(minimap);
        }
    }
}
