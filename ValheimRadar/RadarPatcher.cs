using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
            if (Items == null || Items.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var item in Items) sum += item.Position;
            return sum / Items.Count;
        }

        public string GetLabel()
        {
            return Items.Count > 1 ? $"{Items.Count}x {DisplayName}" : DisplayName;
        }

        public string GetClusterKey()
        {
            if (Items.Count == 0) return string.Empty;
            return $"{DisplayName}_{Items[0].Zdoid.UserID}_{Items[0].Zdoid.ID}";
        }
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("KGvalheim.MoreMapPins", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Arielle.MoreMapPins", BepInDependency.DependencyFlags.SoftDependency)]
    public class RadarPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.yourname.valheimradar";
        public const string PluginName = "ValheimRadar";
        public const string PluginVersion = "1.1.0";

        // General
        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<float> ClusterDistance;

        // Group Toggles
        public static ConfigEntry<bool> Group_Creatures;
        public static ConfigEntry<bool> Group_Berries;
        public static ConfigEntry<bool> Group_Mushrooms;
        public static ConfigEntry<bool> Group_FlowersAndCrops;
        public static ConfigEntry<bool> Group_RocksAndFlint;
        public static ConfigEntry<bool> Group_Ores;
        public static ConfigEntry<bool> Group_Structures;

        // Creature Toggles & Star Filters
        public static ConfigEntry<bool> EnableMonsters;
        public static ConfigEntry<bool> EnableAnimals;
        public static ConfigEntry<int> MinMonsterStars;
        public static ConfigEntry<int> MinAnimalStars;

        // Specific Item Toggles
        public static ConfigEntry<bool> TrackRaspberry;
        public static ConfigEntry<bool> TrackBlueberry;
        public static ConfigEntry<bool> TrackCloudberry;

        public static ConfigEntry<bool> TrackRedMushroom;
        public static ConfigEntry<bool> TrackYellowMushroom;
        public static ConfigEntry<bool> TrackBlueMushroom;

        public static ConfigEntry<bool> TrackDandelion;
        public static ConfigEntry<bool> TrackThistle;
        public static ConfigEntry<bool> TrackCarrotSeed;
        public static ConfigEntry<bool> TrackTurnipSeed;
        public static ConfigEntry<bool> TrackOnionSeed;
        public static ConfigEntry<bool> TrackBarley;
        public static ConfigEntry<bool> TrackFlax;
        public static ConfigEntry<bool> TrackMagecap;

        public static ConfigEntry<bool> TrackFlint;
        public static ConfigEntry<bool> TrackStone;
        public static ConfigEntry<bool> TrackWood;

        public static ConfigEntry<bool> TrackCopper;
        public static ConfigEntry<bool> TrackTin;
        public static ConfigEntry<bool> TrackIron;
        public static ConfigEntry<bool> TrackSilver;

        public static ConfigEntry<bool> TrackChests;
        public static ConfigEntry<bool> TrackDungeons;
        public static ConfigEntry<bool> TrackPortals;
        public static ConfigEntry<bool> TrackBeehives;

        private static readonly Dictionary<string, Minimap.PinData> activeClusterPins = new Dictionary<string, Minimap.PinData>();
        private static string ConfigIconFolder => Path.Combine(Paths.ConfigPath, "MoreMapPins");
        private float timer = 0f;

        // Default Category Fallbacks
        private static Minimap.PinType CustomPin_Monster = Minimap.PinType.Icon3;
        private static Minimap.PinType CustomPin_Animal = Minimap.PinType.Icon3;
        private static Minimap.PinType CustomPin_Berry = Minimap.PinType.Icon3;
        private static Minimap.PinType CustomPin_Mushroom = Minimap.PinType.Icon3;
        private static Minimap.PinType CustomPin_Crop = Minimap.PinType.Icon3;
        private static Minimap.PinType CustomPin_Ore = Minimap.PinType.Icon3;
        private static Minimap.PinType CustomPin_Structure = Minimap.PinType.Icon3;

        private void Awake()
        {
            // --- GENERAL ---
            ScanRadius = Config.Bind("1 - General", "ScanRadius", 100f, "Scan radius around player.");
            UpdateInterval = Config.Bind("1 - General", "UpdateInterval", 1.0f, "Scan interval in seconds.");
            ClusterDistance = Config.Bind("1 - General", "ClusterDistance", 15.0f, "Max distance between items to group into a cluster.");

            // --- MASTER GROUPS ---
            Group_Creatures = Config.Bind("2 - Master Groups", "Enable Creatures Group", true, "Master toggle for all creatures.");
            Group_Berries = Config.Bind("2 - Master Groups", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = Config.Bind("2 - Master Groups", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = Config.Bind("2 - Master Groups", "Enable Flowers and Crops Group", true, "Master toggle for plants, seeds, and crops.");
            Group_RocksAndFlint = Config.Bind("2 - Master Groups", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = Config.Bind("2 - Master Groups", "Enable Ores Group", true, "Master toggle for ore veins and deposits.");
            Group_Structures = Config.Bind("2 - Master Groups", "Enable Structures Group", true, "Master toggle for chests, dungeons, portals, etc.");

            // --- CREATURES & STAR FILTERS ---
            EnableMonsters = Config.Bind("3 - Creatures", "Hostile Monsters", true, "Show aggressive creatures.");
            EnableAnimals = Config.Bind("3 - Creatures", "Passive Animals", true, "Show passive/tameable animals.");
            MinMonsterStars = Config.Bind("3 - Creatures", "Min Monster Stars", 0, "Minimum star level for monsters (0 = All, 1 = 1 Star+, 2 = 2 Stars only).");
            MinAnimalStars = Config.Bind("3 - Creatures", "Min Animal Stars", 0, "Minimum star level for animals (0 = All, 1 = 1 Star+, 2 = 2 Stars only).");

            // --- BERRIES ---
            TrackRaspberry = Config.Bind("4 - Resources (Berries)", "Raspberries", true, "Show Raspberries.");
            TrackBlueberry = Config.Bind("4 - Resources (Berries)", "Blueberries", true, "Show Blueberries.");
            TrackCloudberry = Config.Bind("4 - Resources (Berries)", "Cloudberries", true, "Show Cloudberries.");

            // --- MUSHROOMS ---
            TrackRedMushroom = Config.Bind("5 - Resources (Mushrooms)", "Red Mushrooms", true, "Show standard Red Mushrooms.");
            TrackYellowMushroom = Config.Bind("5 - Resources (Mushrooms)", "Yellow Mushrooms", true, "Show Yellow Cave Mushrooms.");
            TrackBlueMushroom = Config.Bind("5 - Resources (Mushrooms)", "Blue Mushrooms", true, "Show Blue Mushrooms.");

            // --- PLANTS & CROPS ---
            TrackDandelion = Config.Bind("6 - Resources (Plants & Crops)", "Dandelion", true, "Show Dandelions.");
            TrackThistle = Config.Bind("6 - Resources (Plants & Crops)", "Thistle", true, "Show Thistle.");
            TrackCarrotSeed = Config.Bind("6 - Resources (Plants & Crops)", "Carrot Seeds", true, "Show wild Carrot seeds.");
            TrackTurnipSeed = Config.Bind("6 - Resources (Plants & Crops)", "Turnip Seeds", true, "Show wild Turnip seeds.");
            TrackOnionSeed = Config.Bind("6 - Resources (Plants & Crops)", "Onion Seeds", true, "Show wild Onion seeds.");
            TrackBarley = Config.Bind("6 - Resources (Plants & Crops)", "Barley", true, "Show wild or grown Barley.");
            TrackFlax = Config.Bind("6 - Resources (Plants & Crops)", "Flax", true, "Show wild or grown Flax.");
            TrackMagecap = Config.Bind("6 - Resources (Plants & Crops)", "Magecap", true, "Show Magecap mushrooms/plants.");

            // --- GROUND PICKABLES ---
            TrackFlint = Config.Bind("7 - Resources (Ground)", "Flint", true, "Show loose Flint.");
            TrackStone = Config.Bind("7 - Resources (Ground)", "Stones", false, "Show loose Stones.");
            TrackWood = Config.Bind("7 - Resources (Ground)", "Wood/Branches", false, "Show loose Wood.");

            // --- ORES ---
            TrackCopper = Config.Bind("8 - Resources (Ores)", "Copper", true, "Show Copper deposits.");
            TrackTin = Config.Bind("8 - Resources (Ores)", "Tin", true, "Show Tin deposits.");
            TrackIron = Config.Bind("8 - Resources (Ores)", "Muddy Scrap / Iron", true, "Show Iron scrap deposits.");
            TrackSilver = Config.Bind("8 - Resources (Ores)", "Silver", true, "Show Silver veins.");

            // --- STRUCTURES ---
            TrackChests = Config.Bind("9 - Structures", "Chests & Containers", true, "Show treasure chests and storage.");
            TrackDungeons = Config.Bind("9 - Structures", "Dungeons / Crypts / Caves", true, "Show Dungeon and Crypt entrances.");
            TrackPortals = Config.Bind("9 - Structures", "Portals", true, "Show player and ruined portals.");
            TrackBeehives = Config.Bind("9 - Structures", "Beehives", true, "Show wild and built Beehives.");

            Logger.LogInfo($"{PluginName} initialized!");
        }

        private void UpdateCategoryPinDefaults()
        {
            CustomPin_Monster = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "monster.png"),
                                CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "81.png"), Minimap.PinType.Icon3));

            CustomPin_Animal = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "animal.png"),
                               CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "26.png"), Minimap.PinType.Icon3));

            CustomPin_Berry = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "berry.png"),
                              CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "12.png"), Minimap.PinType.Icon3));

            CustomPin_Mushroom = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "mushroom.png"),
                                 CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "17.png"), Minimap.PinType.Icon3));

            CustomPin_Crop = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "crop.png"),
                             CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "15.png"), Minimap.PinType.Icon3));

            CustomPin_Ore = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "ore.png"),
                            CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "7.png"), Minimap.PinType.Icon3));

            CustomPin_Structure = CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "structure.png"),
                                 CustomPinLoader.RegisterPngAsPin(Path.Combine(ConfigIconFolder, "24.png"), Minimap.PinType.Icon3));
        }

        private Minimap.PinType GetPinForObject(string rawName, Minimap.PinType categoryFallback)
        {
            if (string.IsNullOrEmpty(rawName)) return categoryFallback;

            string cleanKey = Regex.Replace(rawName, @"(?i)^(pickable_|item_|piece_|vfx_|sfx_)", "")
                                   .Replace("(Clone)", "")
                                   .Trim()
                                   .ToLower();

            string specificPath = Path.Combine(ConfigIconFolder, $"{cleanKey}.png");
            if (File.Exists(specificPath))
            {
                return CustomPinLoader.RegisterPngAsPin(specificPath, categoryFallback);
            }

            return categoryFallback;
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
                if (kvp.Value != null) Minimap.instance.RemovePin(kvp.Value);
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
            UpdateCategoryPinDefaults();

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
                if (processedZdoids.Contains(zdoid)) continue;

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

        private bool IsPassiveAnimal(Character character)
        {
            if (character == null) return false;
            if (character.IsTamed()) return true;
            if (character.m_faction == Character.Faction.AnimalsVeg) return true;
            if (character.m_faction == Character.Faction.PlayerSpawned) return true;

            BaseAI ai = character.GetBaseAI();
            if (ai != null && ai.m_passiveAggresive) return true;
            if (ai != null && ai.IsEnemy(Player.m_localPlayer)) return true;

            return false;
        }

        private bool IsHostileMonster(Character character)
        {
            if (character == null || character.IsTamed()) return false;
            if (IsPassiveAnimal(character)) return false;

            if (Player.m_localPlayer != null)
            {
                return BaseAI.IsEnemy(Player.m_localPlayer, character);
            }

            return character.IsMonsterFaction(Time.time);
        }

        private bool ShouldPinGameObject(GameObject go, string nameLower, out string displayName, out Minimap.PinType pinType)
        {
            displayName = string.Empty;
            pinType = Minimap.PinType.Icon3;

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
                displayName = go.name;
            }

            displayName = FormatHumanFriendlyName(displayName);

            // 1. CREATURES (Monsters & Passive Animals)
            if (Group_Creatures.Value)
            {
                Character character = go.GetComponent<Character>();
                if (character != null && !character.IsDead() && !character.IsPlayer())
                {
                    int starLevel = Math.Max(0, character.GetLevel() - 1);
                    bool isMonster = IsHostileMonster(character);
                    bool isAnimal = IsPassiveAnimal(character);

                    if (isMonster && EnableMonsters.Value)
                    {
                        if (starLevel < MinMonsterStars.Value) return false;

                        if (starLevel > 0) displayName += $" ({new string('★', starLevel)})";
                        pinType = GetPinForObject(nameLower, CustomPin_Monster);
                        return true;
                    }

                    if (isAnimal && EnableAnimals.Value)
                    {
                        if (starLevel < MinAnimalStars.Value) return false;

                        if (starLevel > 0) displayName += $" ({new string('★', starLevel)})";
                        pinType = GetPinForObject(nameLower, CustomPin_Animal);
                        return true;
                    }
                }
            }

            // 2. BERRIES
            if (Group_Berries.Value)
            {
                if ((TrackRaspberry.Value && nameLower.Contains("raspberry")) ||
                    (TrackBlueberry.Value && nameLower.Contains("blueberry")) ||
                    (TrackCloudberry.Value && nameLower.Contains("cloudberry")))
                {
                    pinType = GetPinForObject(nameLower, CustomPin_Berry);
                    return true;
                }
            }

            // 3. MUSHROOMS
            if (Group_Mushrooms.Value)
            {
                if ((TrackRedMushroom.Value && nameLower.Equals("pickable_mushroom")) ||
                    (TrackYellowMushroom.Value && nameLower.Contains("yellow")) ||
                    (TrackBlueMushroom.Value && nameLower.Contains("blue")))
                {
                    pinType = GetPinForObject(nameLower, CustomPin_Mushroom);
                    return true;
                }
            }

            // 4. FLOWERS & CROPS
            if (Group_FlowersAndCrops.Value)
            {
                if ((TrackDandelion.Value && nameLower.Contains("dandelion")) ||
                    (TrackThistle.Value && nameLower.Contains("thistle")) ||
                    (TrackCarrotSeed.Value && nameLower.Contains("carrot")) ||
                    (TrackTurnipSeed.Value && nameLower.Contains("turnip")) ||
                    (TrackOnionSeed.Value && nameLower.Contains("onion")) ||
                    (TrackBarley.Value && nameLower.Contains("barley")) ||
                    (TrackFlax.Value && nameLower.Contains("flax")) ||
                    (TrackMagecap.Value && nameLower.Contains("magecap")))
                {
                    pinType = GetPinForObject(nameLower, CustomPin_Crop);
                    return true;
                }
            }

            // 5. GROUND PICKABLES
            if (Group_RocksAndFlint.Value)
            {
                if ((TrackFlint.Value && nameLower.Contains("flint")) ||
                    (TrackStone.Value && nameLower.Contains("stone") && go.GetComponent<Pickable>() != null) ||
                    (TrackWood.Value && (nameLower.Contains("wood") || nameLower.Contains("branch")) && go.GetComponent<Pickable>() != null))
                {
                    pinType = GetPinForObject(nameLower, CustomPin_Crop);
                    return true;
                }
            }

            // 6. ORES
            if (Group_Ores.Value)
            {
                if ((TrackCopper.Value && nameLower.Contains("copper")) ||
                    (TrackTin.Value && nameLower.Contains("tin")) ||
                    (TrackIron.Value && (nameLower.Contains("muddy") || nameLower.Contains("iron"))) ||
                    (TrackSilver.Value && nameLower.Contains("silver")))
                {
                    pinType = GetPinForObject(nameLower, CustomPin_Ore);
                    return true;
                }
            }

            // 7. STRUCTURES
            if (Group_Structures.Value)
            {
                if ((TrackChests.Value && go.GetComponent<Container>() != null) ||
                    (TrackPortals.Value && (go.GetComponent<TeleportWorld>() != null || nameLower.Contains("portal"))) ||
                    (TrackDungeons.Value && (nameLower.Contains("dungeon") || nameLower.Contains("crypt") || nameLower.Contains("cave"))) ||
                    (TrackBeehives.Value && nameLower.Contains("beehive")))
                {
                    pinType = GetPinForObject(nameLower, CustomPin_Structure);
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

        private static string FormatHumanFriendlyName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            string clean = Regex.Replace(rawName, @"(?i)^(pickable_|item_|piece_|vfx_|sfx_)", "");
            clean = clean.Replace("(Clone)", "").Trim();
            clean = clean.Replace('_', ' ');
            clean = Regex.Replace(clean, @"(?<=[a-z])(?=[A-Z])", " ");
            clean = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clean.ToLower());

            return clean;
        }
    }
}