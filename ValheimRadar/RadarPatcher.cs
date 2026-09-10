using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimRadar
{
    public class TrackedItem
    {
        public ZDOID Zdoid;
        public Vector3 Position;
        public string RawName;
        public string DisplayName;
        public Minimap.PinType PinType;
    }

    public class ItemCluster
    {
        public string DisplayName;
        public Minimap.PinType PinType;
        public List<TrackedItem> Items = new List<TrackedItem>();

        public Vector3 GetCentroid()
        {
            if (Items.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var item in Items) sum += item.Position;
            return sum / Items.Count;
        }

        public string GetLabel()
        {
            return Items.Count > 1 ? $"{Items.Count}x {DisplayName}" : DisplayName;
        }

        // Generates a unique tracking key based on the first item's ID in the cluster
        public string GetClusterKey()
        {
            return $"{DisplayName}_{Items[0].Zdoid.UserID}_{Items[0].Zdoid.ID}";
        }
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class RadarPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.yourname.valheimradar";
        public const string PluginName = "ValheimRadar";
        public const string PluginVersion = "1.0.0";

        // General
        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<float> ClusterDistance;

        // --- MASTER GROUP TOGGLES ---
        public static ConfigEntry<bool> Group_Creatures;
        public static ConfigEntry<bool> Group_Berries;
        public static ConfigEntry<bool> Group_Mushrooms;
        public static ConfigEntry<bool> Group_FlowersAndCrops;
        public static ConfigEntry<bool> Group_RocksAndFlint;
        public static ConfigEntry<bool> Group_Ores;
        public static ConfigEntry<bool> Group_Structures;

        // --- SPECIFIC ITEM TOGGLES ---
        // Creatures
        public static ConfigEntry<bool> EnableMonsters;
        public static ConfigEntry<bool> EnableAnimals;

        // Berries
        public static ConfigEntry<bool> TrackRaspberry;
        public static ConfigEntry<bool> TrackBlueberry;
        public static ConfigEntry<bool> TrackCloudberry;

        // Mushrooms
        public static ConfigEntry<bool> TrackRedMushroom;
        public static ConfigEntry<bool> TrackYellowMushroom;
        public static ConfigEntry<bool> TrackBlueMushroom;

        // Flowers & Crops (NEW)
        public static ConfigEntry<bool> TrackDandelion;
        public static ConfigEntry<bool> TrackThistle;
        public static ConfigEntry<bool> TrackCarrotSeed;
        public static ConfigEntry<bool> TrackTurnipSeed;
        public static ConfigEntry<bool> TrackOnionSeed;
        public static ConfigEntry<bool> TrackBarley;
        public static ConfigEntry<bool> TrackFlax;
        public static ConfigEntry<bool> TrackMagecap;

        // Rocks & Ground Items
        public static ConfigEntry<bool> TrackFlint;
        public static ConfigEntry<bool> TrackStone;
        public static ConfigEntry<bool> TrackWood;

        // Ores
        public static ConfigEntry<bool> TrackCopper;
        public static ConfigEntry<bool> TrackTin;
        public static ConfigEntry<bool> TrackIron;
        public static ConfigEntry<bool> TrackSilver;

        // Structures
        public static ConfigEntry<bool> TrackChests;
        public static ConfigEntry<bool> TrackDungeons;
        public static ConfigEntry<bool> TrackPortals;
        public static ConfigEntry<bool> TrackBeehives;

        // Active cluster pins tracking dictionary
        private static readonly Dictionary<string, Minimap.PinData> activeClusterPins = new Dictionary<string, Minimap.PinData>();
        private float timer = 0f;


        private void Awake()
        {
            // --- 1. GENERAL ---
            ScanRadius = Config.Bind("General", "ScanRadius", 100f, "Scan radius around player.");
            UpdateInterval = Config.Bind("General", "UpdateInterval", 1.0f, "Scan interval in seconds.");
            ClusterDistance = Config.Bind("General", "ClusterDistance", 15.0f, "Max distance between items to group into a cluster."); // FIX: Added missing config binding

            // --- 2. MASTER GROUP TOGGLES ---
            Group_Creatures = Config.Bind("Group Toggles", "Enable Creatures Group", true, "Master toggle for all creatures (hostile & passive).");
            Group_Berries = Config.Bind("Group Toggles", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = Config.Bind("Group Toggles", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = Config.Bind("Group Toggles", "Enable Flowers and Crops Group", true, "Master toggle for plants, seeds, and crops.");
            Group_RocksAndFlint = Config.Bind("Group Toggles", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = Config.Bind("Group Toggles", "Enable Ores Group", true, "Master toggle for ore veins and deposits.");
            Group_Structures = Config.Bind("Group Toggles", "Enable Structures Group", true, "Master toggle for chests, dungeons, portals, etc.");

            // --- 3. CREATURE TOGGLES ---
            EnableMonsters = Config.Bind("Creatures", "Hostile Monsters", true, "Show aggressive creatures.");
            EnableAnimals = Config.Bind("Creatures", "Passive Animals", true, "Show passive/tameable animals.");

            // --- 4. BERRY TOGGLES ---
            TrackRaspberry = Config.Bind("Resources - Berries", "Raspberries", true, "Show Raspberries.");
            TrackBlueberry = Config.Bind("Resources - Berries", "Blueberries", true, "Show Blueberries.");
            TrackCloudberry = Config.Bind("Resources - Berries", "Cloudberries", true, "Show Cloudberries.");

            // --- 5. MUSHROOM TOGGLES ---
            TrackRedMushroom = Config.Bind("Resources - Mushrooms", "Red Mushrooms", true, "Show standard Red Mushrooms.");
            TrackYellowMushroom = Config.Bind("Resources - Mushrooms", "Yellow Mushrooms", true, "Show Yellow Cave Mushrooms.");
            TrackBlueMushroom = Config.Bind("Resources - Mushrooms", "Blue Mushrooms", true, "Show Blue Mushrooms.");

            // --- 6. FLOWERS & CROPS ---
            TrackDandelion = Config.Bind("Resources - Plants & Crops", "Dandelion", true, "Show Dandelions.");
            TrackThistle = Config.Bind("Resources - Plants & Crops", "Thistle", true, "Show Thistle.");
            TrackCarrotSeed = Config.Bind("Resources - Plants & Crops", "Carrot Seeds", true, "Show wild Carrot seeds.");
            TrackTurnipSeed = Config.Bind("Resources - Plants & Crops", "Turnip Seeds", true, "Show wild Turnip seeds.");
            TrackOnionSeed = Config.Bind("Resources - Plants & Crops", "Onion Seeds", true, "Show wild Onion seeds.");
            TrackBarley = Config.Bind("Resources - Plants & Crops", "Barley", true, "Show wild or grown Barley.");
            TrackFlax = Config.Bind("Resources - Plants & Crops", "Flax", true, "Show wild or grown Flax.");
            TrackMagecap = Config.Bind("Resources - Plants & Crops", "Magecap", true, "Show Magecap mushrooms/plants.");

            // --- 7. GROUND PICKABLES ---
            TrackFlint = Config.Bind("Resources - Ground", "Flint", true, "Show loose Flint.");
            TrackStone = Config.Bind("Resources - Ground", "Stones", false, "Show loose Stones.");
            TrackWood = Config.Bind("Resources - Ground", "Wood/Branches", false, "Show loose Wood.");

            // --- 8. ORES ---
            TrackCopper = Config.Bind("Resources - Ores", "Copper", true, "Show Copper deposits.");
            TrackTin = Config.Bind("Resources - Ores", "Tin", true, "Show Tin deposits.");
            TrackIron = Config.Bind("Resources - Ores", "Muddy Scrap / Iron", true, "Show Iron scrap deposits.");
            TrackSilver = Config.Bind("Resources - Ores", "Silver", true, "Show Silver veins.");

            // --- 9. STRUCTURES ---
            TrackChests = Config.Bind("Structures", "Chests & Containers", true, "Show treasure chests and storage.");
            TrackDungeons = Config.Bind("Structures", "Dungeons / Crypts / Caves", true, "Show Dungeon and Crypt entrances.");
            TrackPortals = Config.Bind("Structures", "Portals", true, "Show player and ruined portals.");
            TrackBeehives = Config.Bind("Structures", "Beehives", true, "Show wild and built Beehives.");

            //Config.SettingChanged += OnConfigurationChanged;

            Logger.LogInfo($"{PluginName} fully loaded with granular group filtering!");
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnConfigurationChanged;
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            ClearAllPins();
        }
        private static void ClearAllPins()
        {
            if (Minimap.instance == null) return;

            foreach (var kvp in activeClusterPins)
            {
                if (kvp.Value != null)
                {
                    Minimap.instance.RemovePin(kvp.Value);
                }
            }
            activeClusterPins.Clear();
        }

        private void Update()
        {
            if (Player.m_localPlayer == null || Minimap.instance == null) return;

            timer += Time.deltaTime;
            if (timer < UpdateInterval.Value) return;
            timer = 0f;

            ScanAndPinObjects(Minimap.instance);
        }

        private void ScanAndPinObjects(Minimap minimap)
        {
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            List<TrackedItem> detectedItems = new List<TrackedItem>();
            HashSet<ZDOID> processedZdoids = new HashSet<ZDOID>();

            Collider[] hitColliders = Physics.OverlapSphere(playerPos, ScanRadius.Value);
            foreach (var hit in hitColliders)
            {
                if (hit == null) continue;

                GameObject obj = hit.gameObject;
                ZNetView netView = obj.GetComponentInParent<ZNetView>();

                if (netView == null || !netView.IsValid() || netView.GetZDO() == null) continue;

                ZDOID zdoid = netView.GetZDO().m_uid;
                if (processedZdoids.Contains(zdoid)) continue; // Skip redundant colliders on same object

                string rawName = netView.gameObject.name.Replace("(Clone)", "").Trim().ToLower();

                if (ShouldPinGameObject(netView.gameObject, rawName, out string displayName, out Minimap.PinType pinType))
                {
                    processedZdoids.Add(zdoid);
                    detectedItems.Add(new TrackedItem
                    {
                        Zdoid = zdoid,
                        Position = netView.transform.position,
                        RawName = rawName,
                        DisplayName = displayName,
                        PinType = pinType
                    });
                }
            }

            float clusterDist = ClusterDistance != null ? ClusterDistance.Value : 15.0f;
            List<ItemCluster> clusters = ClusterItems(detectedItems, clusterDist);
            HashSet<string> currentScanKeys = new HashSet<string>();

            foreach (var cluster in clusters)
            {
                string key = cluster.GetClusterKey();
                if (string.IsNullOrEmpty(key)) continue;

                currentScanKeys.Add(key);
                Vector3 centerPos = cluster.GetCentroid();
                string label = cluster.GetLabel();

                UpdateOrCreatePin(minimap, key, centerPos, label, cluster.PinType);
            }

            List<string> toRemove = new List<string>();
            foreach (var kvp in activeClusterPins)
            {
                if (!currentScanKeys.Contains(kvp.Key))
                {
                    if (kvp.Value != null) minimap.RemovePin(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove) activeClusterPins.Remove(key);
        }

        // Placeholder for filter method matching your config toggles
        private bool ShouldPinGameObject(GameObject go, string nameLower, out string displayName, out Minimap.PinType pinType)
        {
            displayName = string.Empty;
            pinType = Minimap.PinType.Icon3;

            // Extract display name via standard components
            HoverText hover = go.GetComponent<HoverText>();
            if (hover != null && !string.IsNullOrEmpty(hover.m_text))
            {
                displayName = hover.m_text;
            }
            else
            {
                Character character = go.GetComponent<Character>();
                if (character != null) displayName = character.GetHoverName();
            }

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = go.name.Replace("(Clone)", "").Trim();
            }

            // 1. CREATURES
            if (Group_Creatures.Value)
            {
                Character character = go.GetComponent<Character>();
                if (character != null && !character.IsDead())
                {
                    bool isMonster = character.IsMonsterFaction(Time.time);
                    if (EnableMonsters.Value && isMonster)
                    {
                        pinType = Minimap.PinType.Death; // Hostile icon
                        return true;
                    }
                    if (EnableAnimals.Value && !isMonster)
                    {
                        pinType = Minimap.PinType.Icon3; // Neutral icon
                        return true;
                    }
                }
            }

            // 2. BERRIES
            if (Group_Berries.Value)
            {
                if (TrackRaspberry.Value && nameLower.Contains("raspberry")) return true;
                if (TrackBlueberry.Value && nameLower.Contains("blueberry")) return true;
                if (TrackCloudberry.Value && nameLower.Contains("cloudberry")) return true;
            }

            // 3. MUSHROOMS
            if (Group_Mushrooms.Value)
            {
                if (TrackRedMushroom.Value && nameLower.Equals("pickable_mushroom")) return true;
                if (TrackYellowMushroom.Value && nameLower.Contains("yellow")) return true;
                if (TrackBlueMushroom.Value && nameLower.Contains("blue")) return true;
            }

            // 4. FLOWERS & CROPS
            if (Group_FlowersAndCrops.Value)
            {
                if (TrackDandelion.Value && nameLower.Contains("dandelion")) return true;
                if (TrackThistle.Value && nameLower.Contains("thistle")) return true;
                if (TrackCarrotSeed.Value && nameLower.Contains("carrot")) return true;
                if (TrackTurnipSeed.Value && nameLower.Contains("turnip")) return true;
                if (TrackOnionSeed.Value && nameLower.Contains("onion")) return true;
                if (TrackBarley.Value && nameLower.Contains("barley")) return true;
                if (TrackFlax.Value && nameLower.Contains("flax")) return true;
                if (TrackMagecap.Value && nameLower.Contains("magecap")) return true;
            }

            // 5. GROUND PICKABLES
            if (Group_RocksAndFlint.Value)
            {
                if (TrackFlint.Value && nameLower.Contains("flint")) return true;
                if (TrackStone.Value && nameLower.Contains("stone") && go.GetComponent<Pickable>() != null) return true;
                if (TrackWood.Value && (nameLower.Contains("wood") || nameLower.Contains("branch")) && go.GetComponent<Pickable>() != null) return true;
            }

            // 6. ORES
            if (Group_Ores.Value)
            {
                pinType = Minimap.PinType.Icon1;
                if (TrackCopper.Value && nameLower.Contains("copper")) return true;
                if (TrackTin.Value && nameLower.Contains("tin")) return true;
                if (TrackIron.Value && (nameLower.Contains("muddy") || nameLower.Contains("iron"))) return true;
                if (TrackSilver.Value && nameLower.Contains("silver")) return true;
            }

            // 7. STRUCTURES
            if (Group_Structures.Value)
            {
                if (TrackChests.Value && go.GetComponent<Container>() != null)
                {
                    pinType = Minimap.PinType.Icon3;
                    return true;
                }
                if (TrackPortals.Value && (go.GetComponent<TeleportWorld>() != null || nameLower.Contains("portal")))
                {
                    pinType = Minimap.PinType.Icon4;
                    return true;
                }
                if (TrackDungeons.Value && (nameLower.Contains("dungeon") || nameLower.Contains("crypt") || nameLower.Contains("cave")))
                {
                    pinType = Minimap.PinType.Icon2;
                    return true;
                }
                if (TrackBeehives.Value && nameLower.Contains("beehive"))
                {
                    pinType = Minimap.PinType.Icon3;
                    return true;
                }
            }

            return false;
        }

        private List<ItemCluster> ClusterItems(List<TrackedItem> items, float maxDistance)
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

        private static void UpdateOrCreatePin(Minimap minimap, string clusterKey, Vector3 pos, string name, Minimap.PinType pinType)
        {
            if (activeClusterPins.TryGetValue(clusterKey, out Minimap.PinData existingPin))
            {
                if (existingPin != null)
                {
                    existingPin.m_pos = pos;
                    existingPin.m_name = name;
                }
            }
            else
            {
                Minimap.PinData newPin = minimap.AddPin(pos, pinType, name, save: false, isChecked: false);
                activeClusterPins.Add(clusterKey, newPin);
            }
        }
    }
}