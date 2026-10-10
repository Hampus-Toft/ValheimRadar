using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimRadar
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class RadarPlugin : BaseUnityPlugin
    {
        // Author "Hampus Toft"; the ID follows the Thunderstore namespace (HampusToft) + package name.
        public const string PluginGUID = "HampusToft.ValheimRadar";
        // GUID before v1.12.3 - its .cfg is carried over once by MigrateLegacyConfig.
        private const string LegacyPluginGUID = "com.yourname.valheimradar";
        public const string PluginName = "ValheimRadar";
        public const string PluginVersion = "1.15.0";

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
        private static float CreatureClusterDistance => RadarConfig.CreatureClusterDistance != null ? RadarConfig.CreatureClusterDistance.Value : 15.0f;

        private void Awake()
        {
            MigrateLegacyConfig();
            RadarConfig.Initialize(Config);
            Config.SettingChanged += OnConfigurationChanged;

            try
            {
                harmony = new Harmony(PluginGUID);
                harmony.PatchAll(typeof(MinimapPatches));
            }
            catch (Exception ex)
            {
                // Only manual pin removal/cross-out depend on these patches - the rest of the plugin works without them.
                Logger.LogError($"Failed to apply Minimap patches (right-click pin removal and left-click cross-out disabled): {ex}");
            }

            // Guards each target itself - see DepletionPatches.Patch.
            if (harmony != null) DepletionPatches.Apply(harmony);

            Logger.LogInfo($"{PluginName} initialized!");
        }

        // The config file is named after the GUID, so the GUID change would otherwise reset everyone's
        // settings. Copy the old file over once (it stays in place) and reload before anything is bound.
        private void MigrateLegacyConfig()
        {
            try
            {
                string legacyPath = Path.Combine(Paths.ConfigPath, LegacyPluginGUID + ".cfg");
                if (File.Exists(Config.ConfigFilePath) || !File.Exists(legacyPath)) return;

                File.Copy(legacyPath, Config.ConfigFilePath);
                Config.Reload();
                Logger.LogInfo($"Imported settings from {LegacyPluginGUID}.cfg");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not import settings from {LegacyPluginGUID}.cfg: {ex.Message}");
            }
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
                // Freshly connected/reloaded - open this world's saved pins. Locations are drawn right
                // away; resource points stream in over the next frames, nearest first, through
                // PinManager.Tick (see PinManager.OpenWorld), so a large save never stalls the connect.
                currentWorldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
                diagnosticTimer = 0f;
                diagnosticLogCount = 0;
                PinManager.OpenWorld(currentWorldName, Player.m_localPlayer.transform.position, ClusterDistance);
                PinManager.DrawLoadedLocationPins(Minimap.instance);
                wasActive = true;
            }

            // Loads/reclusters saved points and keeps resource pins in step with the visible map, within a
            // small per-frame time budget.
            PinManager.Tick(Minimap.instance, Player.m_localPlayer.transform.position);

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

                // Re-tags Mountain Caves whose (loaded) interior has a Tetra pond - see LocationScanner.FindTetraPondDungeons.
                List<Vector3> pondDungeons = LocationScanner.FindTetraPondDungeons();
                if (pondDungeons.Count > 0) PinManager.MarkTetraPondCaves(Minimap.instance, pondDungeons);
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
            List<ItemCluster> creatureClusters = ClusteringEngine.ClusterItems(creatures, CreatureClusterDistance);
            PinManager.SyncTransientClusters(minimap, creatureClusters);

            // Type #2/#3 (semi-permanent) - resources and physics-detected points of interest don't
            // move, so each scanner only queries cells that are new, newly loaded, or due for a
            // budgeted rescan (see PermanentSpatialScanner) - usually none once the surroundings are
            // explored. New points merge into the durable raw store and are clustered incrementally,
            // so a session's full discovery history never gets reclustered from scratch on a normal
            // tick (a ClusterDistance change is handled separately - see PinManager.RecordRawPoints);
            // depletable points a verified rescan no longer finds are removed. The next PinManager.Tick
            // pushes pin updates for whatever clusters actually changed.
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
        }
    }
}
