using BepInEx;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("KGvalheim.MoreMapPins", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Arielle.MoreMapPins", BepInDependency.DependencyFlags.SoftDependency)]
    public class RadarPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.yourname.valheimradar";
        public const string PluginName = "ValheimRadar";
        public const string PluginVersion = "1.4.0";

        // How often to flush newly-discovered persistent (resource/structure) pin positions to
        // disk while connected, so a crash/alt-F4 doesn't lose more than this much progress.
        private const float PersistSaveInterval = 30f;

        private float timer = 0f;
        private float saveTimer = 0f;
        private bool wasActive = false;
        private string currentWorldName;

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
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            // Re-derive which categories should be visible and add/remove their minimap pins
            // accordingly - cached cluster positions are never discarded here, so re-enabling a
            // category instantly redraws its pins at their last known location instead of waiting
            // for the player to walk back into scan range.
            PinManager.RefreshCategoryVisibility(Minimap.instance);
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
                    wasActive = false;
                    currentWorldName = null;
                    saveTimer = 0f;
                }
                return;
            }

            if (!wasActive)
            {
                // Freshly connected/reloaded - reload this world's previously discovered
                // resource/structure pins so the map doesn't start blank after a relog.
                currentWorldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
                PinManager.LoadWorldPins(currentWorldName, Minimap.instance);
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
            List<TrackedItem> detectedItems = new List<TrackedItem>();
            HashSet<ZDOID> processedZdoids = new HashSet<ZDOID>();

            Collider[] hitColliders = Physics.OverlapSphere(playerPos, RadarConfig.ScanRadius.Value);
            foreach (var hit in hitColliders)
            {
                if (hit == null) continue;

                GameObject obj = hit.gameObject;
                ZNetView netView = obj.GetComponentInParent<ZNetView>();

                if (netView == null || !netView.IsValid() || netView.GetZDO() == null) continue;

                ZDOID zdoid = netView.GetZDO().m_uid;
                if (processedZdoids.Contains(zdoid)) continue;

                string rawName = netView.gameObject.name.Replace("(Clone)", "").Trim().ToLower();

                if (ObjectEvaluator.ShouldPinGameObject(netView.gameObject, rawName, out string displayName, out Minimap.PinType pinType, out bool isPersistent, out string categoryKey))
                {
                    processedZdoids.Add(zdoid);
                    detectedItems.Add(new TrackedItem
                    {
                        Zdoid = zdoid,
                        Position = netView.transform.position,
                        RawName = rawName,
                        DisplayName = displayName,
                        PinType = pinType,
                        IsPersistent = isPersistent,
                        CategoryKey = categoryKey
                    });
                }
            }

            float clusterDist = RadarConfig.ClusterDistance != null ? RadarConfig.ClusterDistance.Value : 15.0f;
            List<ItemCluster> clusters = ClusteringEngine.ClusterItems(detectedItems, clusterDist);

            PinManager.SyncClusterPins(minimap, clusters);
        }
    }
}