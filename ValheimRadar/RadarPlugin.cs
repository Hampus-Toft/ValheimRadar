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

        private float timer = 0f;
        private bool wasActive = false;

        private void Awake()
        {
            RadarConfig.Initialize(Config);
            Config.SettingChanged += OnConfigurationChanged;
            Logger.LogInfo($"{PluginName} initialized!");
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnConfigurationChanged;
            PinManager.ClearAllPins();
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            // Only drop transient (creature) pins so filter changes take effect immediately -
            // persistent resource/structure pins are left in place.
            PinManager.ClearAllPins(includePersistent: false);
        }

        private void Update()
        {
            if (Player.m_localPlayer == null || Minimap.instance == null)
            {
                // Player disconnected or the world unloaded - drop stale pin state now
                // rather than letting activeClusterPins hold onto pins from the old session.
                if (wasActive)
                {
                    PinManager.ClearAllPins(includePersistent: false);
                    wasActive = false;
                }
                return;
            }

            wasActive = true;

            timer += Time.deltaTime;
            if (timer < RadarConfig.UpdateInterval.Value) return;
            timer = 0f;

            ScanAndPinObjects(Minimap.instance);
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

                if (ObjectEvaluator.ShouldPinGameObject(netView.gameObject, rawName, out string displayName, out Minimap.PinType pinType, out bool isPersistent))
                {
                    processedZdoids.Add(zdoid);
                    detectedItems.Add(new TrackedItem
                    {
                        Zdoid = zdoid,
                        Position = netView.transform.position,
                        RawName = rawName,
                        DisplayName = displayName,
                        PinType = pinType,
                        IsPersistent = isPersistent
                    });
                }
            }

            float clusterDist = RadarConfig.ClusterDistance != null ? RadarConfig.ClusterDistance.Value : 15.0f;
            List<ItemCluster> clusters = ClusteringEngine.ClusterItems(detectedItems, clusterDist);

            PinManager.SyncClusterPins(minimap, clusters);
        }
    }
}