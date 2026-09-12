using System.Collections.Generic;
using BepInEx.Configuration;

namespace ValheimRadar
{
    public static class RadarConfig
    {
        /// <summary>
        /// Duck-typed for BepInEx.ConfigurationManager's reflection-based tag reader (no hard
        /// dependency on that plugin). Setting Order keeps entries displayed in the same order
        /// they're declared here instead of being re-sorted alphabetically.
        /// </summary>
        public sealed class ConfigurationManagerAttributes
        {
            public int? Order;
        }

        public sealed class CreatureDefinition
        {
            // Key doubles as the substring matched against the lowercased prefab name, so more
            // specific keys (e.g. "greydwarf_elite") must be declared before shorter ones they
            // overlap with (e.g. "greydwarf").
            public readonly string Key;
            public readonly string DisplayName;
            public readonly string Section;
            public readonly bool IsMonster;
            public readonly bool DefaultEnabled;

            public CreatureDefinition(string key, string displayName, string section, bool isMonster, bool defaultEnabled = true)
            {
                Key = key;
                DisplayName = displayName;
                Section = section;
                IsMonster = isMonster;
                DefaultEnabled = defaultEnabled;
            }
        }

        public sealed class CreatureConfigEntry
        {
            public bool IsMonster;
            public ConfigEntry<bool> Enabled;
            public ConfigEntry<int> MinStars;
        }

        private const string SecMeadows = "4 - Creatures (Meadows)";
        private const string SecBlackForest = "5 - Creatures (Black Forest)";
        private const string SecSwamp = "6 - Creatures (Swamp)";
        private const string SecMountain = "7 - Creatures (Mountain)";
        private const string SecPlains = "8 - Creatures (Plains)";
        private const string SecMistlands = "9 - Creatures (Mistlands)";

        public static readonly CreatureDefinition[] CreatureDefinitions =
        {
            // MEADOWS
            new CreatureDefinition("boar", "Boar", SecMeadows, isMonster: false),
            new CreatureDefinition("neck", "Neck", SecMeadows, isMonster: false),
            new CreatureDefinition("deer", "Deer", SecMeadows, isMonster: false),
            new CreatureDefinition("greyling", "Greyling", SecMeadows, isMonster: true),

            // BLACK FOREST
            new CreatureDefinition("greydwarf_shaman", "Greydwarf Shaman", SecBlackForest, isMonster: true),
            new CreatureDefinition("greydwarf_elite", "Greydwarf Brute", SecBlackForest, isMonster: true),
            new CreatureDefinition("greydwarf", "Greydwarf", SecBlackForest, isMonster: true),
            new CreatureDefinition("skeleton", "Skeleton", SecBlackForest, isMonster: true),
            new CreatureDefinition("troll", "Troll", SecBlackForest, isMonster: true),

            // SWAMP
            new CreatureDefinition("draugr_elite", "Draugr Elite", SecSwamp, isMonster: true),
            new CreatureDefinition("draugr", "Draugr", SecSwamp, isMonster: true),
            new CreatureDefinition("blob_elite", "Poison Blob", SecSwamp, isMonster: true),
            new CreatureDefinition("blob", "Blob", SecSwamp, isMonster: true),
            new CreatureDefinition("leech", "Leech", SecSwamp, isMonster: true),
            new CreatureDefinition("wraith", "Wraith", SecSwamp, isMonster: true),
            new CreatureDefinition("abomination", "Abomination", SecSwamp, isMonster: true),

            // MOUNTAIN
            new CreatureDefinition("wolf", "Wolf", SecMountain, isMonster: true),
            new CreatureDefinition("bear", "Bear", SecMountain, isMonster: false),
            new CreatureDefinition("stonegolem", "Stone Golem", SecMountain, isMonster: true),
            new CreatureDefinition("drake", "Drake", SecMountain, isMonster: true),

            // PLAINS
            new CreatureDefinition("lox", "Lox", SecPlains, isMonster: false),
            new CreatureDefinition("deathsquito", "Deathsquito", SecPlains, isMonster: true),
            new CreatureDefinition("fuling_berserker", "Fuling Berserker", SecPlains, isMonster: true),
            new CreatureDefinition("fuling_shaman", "Fuling Shaman", SecPlains, isMonster: true),
            new CreatureDefinition("fuling", "Fuling", SecPlains, isMonster: true),
            new CreatureDefinition("growth", "Growth (Lox Spawn)", SecPlains, isMonster: true),

            // MISTLANDS
            new CreatureDefinition("seeker_brood", "Seeker Brood", SecMistlands, isMonster: true),
            new CreatureDefinition("seeker", "Seeker", SecMistlands, isMonster: true),
            new CreatureDefinition("gjall", "Gjall", SecMistlands, isMonster: true),
            new CreatureDefinition("tick", "Tick", SecMistlands, isMonster: true),
            new CreatureDefinition("dvergrmage", "Dvergr Mage", SecMistlands, isMonster: true),
            new CreatureDefinition("dvergr", "Dvergr", SecMistlands, isMonster: false),
            new CreatureDefinition("fenring", "Fenring", SecMistlands, isMonster: true),
        };

        public static readonly Dictionary<string, CreatureConfigEntry> Creatures = new Dictionary<string, CreatureConfigEntry>();

        // General
        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<float> ClusterDistance;
        public static ConfigEntry<int> ScanBatchCount;

        // Master Group Toggles
        public static ConfigEntry<bool> Group_Creatures;
        public static ConfigEntry<bool> Group_Berries;
        public static ConfigEntry<bool> Group_Mushrooms;
        public static ConfigEntry<bool> Group_FlowersAndCrops;
        public static ConfigEntry<bool> Group_RocksAndFlint;
        public static ConfigEntry<bool> Group_Ores;
        public static ConfigEntry<bool> Group_FunctionalStructures;
        public static ConfigEntry<bool> Group_RuinsAndLocations;

        // Creature Filters (fallback used for any creature without a specific entry above)
        public static ConfigEntry<bool> EnableMonsters;
        public static ConfigEntry<bool> EnableAnimals;
        public static ConfigEntry<int> MinMonsterStars;
        public static ConfigEntry<int> MinAnimalStars;

        // Berries
        public static ConfigEntry<bool> TrackRaspberry;
        public static ConfigEntry<bool> TrackBlueberry;
        public static ConfigEntry<bool> TrackCloudberry;

        // Mushrooms
        public static ConfigEntry<bool> TrackRedMushroom;
        public static ConfigEntry<bool> TrackYellowMushroom;
        public static ConfigEntry<bool> TrackBlueMushroom;

        // Plants & Crops
        public static ConfigEntry<bool> TrackDandelion;
        public static ConfigEntry<bool> TrackThistle;
        public static ConfigEntry<bool> TrackCarrotSeed;
        public static ConfigEntry<bool> TrackTurnipSeed;
        public static ConfigEntry<bool> TrackOnionSeed;
        public static ConfigEntry<bool> TrackBarley;
        public static ConfigEntry<bool> TrackFlax;
        public static ConfigEntry<bool> TrackMagecap;

        // Ground Pickables
        public static ConfigEntry<bool> TrackFlint;
        public static ConfigEntry<bool> TrackStone;
        public static ConfigEntry<bool> TrackWood;

        // Ores
        public static ConfigEntry<bool> TrackCopper;
        public static ConfigEntry<bool> TrackTin;
        public static ConfigEntry<bool> TrackIron;
        public static ConfigEntry<bool> TrackSilver;

        // Functional Structures
        public static ConfigEntry<bool> TrackChests;
        public static ConfigEntry<bool> TrackDungeons;
        public static ConfigEntry<bool> TrackPortals;
        public static ConfigEntry<bool> TrackBeehives;

        // Ruins & Locations
        public static ConfigEntry<bool> TrackAbandonedRuins;
        public static ConfigEntry<bool> TrackStoneRings;
        public static ConfigEntry<bool> TrackRunestones;
        public static ConfigEntry<bool> TrackTarPits;

        private static int _order;

        private static ConfigEntry<T> Bind<T>(ConfigFile config, string section, string key, T defaultValue, string description, AcceptableValueBase acceptableValues = null)
        {
            var attrs = new ConfigurationManagerAttributes { Order = _order-- };
            return config.Bind(section, key, defaultValue, new ConfigDescription(description, acceptableValues, attrs));
        }

        public static void Initialize(ConfigFile config)
        {
            _order = 0;

            ScanRadius = Bind(config, "1 - General", "ScanRadius", 100f, "Scan radius around player.", new AcceptableValueRange<float>(10f, 300f));
            UpdateInterval = Bind(config, "1 - General", "UpdateInterval", 1.0f, "Scan interval in seconds.", new AcceptableValueRange<float>(0.1f, 10f));
            ClusterDistance = Bind(config, "1 - General", "ClusterDistance", 15.0f, "Max distance between items to group into a cluster.", new AcceptableValueRange<float>(1f, 50f));
            ScanBatchCount = Bind(config, "1 - General", "ScanBatchCount", 4, "Splits each full-radius scan into this many spatial batches, spread across successive update ticks, so a large ScanRadius doesn't cause a lag spike on any single tick. Higher values reduce per-tick cost but make newly-appearing/moving objects take longer to refresh (1 = scan the whole radius every tick).", new AcceptableValueRange<int>(1, 20));

            Group_Creatures = Bind(config, "2 - Master Groups", "Enable Creatures Group", true, "Master toggle for all creatures.");
            Group_Berries = Bind(config, "2 - Master Groups", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = Bind(config, "2 - Master Groups", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = Bind(config, "2 - Master Groups", "Enable Flowers and Crops Group", true, "Master toggle for plants, seeds, and crops.");
            Group_RocksAndFlint = Bind(config, "2 - Master Groups", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = Bind(config, "2 - Master Groups", "Enable Ores Group", true, "Master toggle for ore veins and deposits.");
            Group_FunctionalStructures = Bind(config, "2 - Master Groups", "Enable Functional Structures", true, "Master toggle for chests, dungeons, portals, beehives.");
            Group_RuinsAndLocations = Bind(config, "2 - Master Groups", "Enable Ruins & Locations", true, "Master toggle for stone rings, abandoned farms, ruins, runestones.");

            EnableMonsters = Bind(config, "3 - Creatures (Defaults)", "Hostile Monsters (Unlisted)", true, "Show hostile creatures that have no specific entry in the biome sections below.");
            EnableAnimals = Bind(config, "3 - Creatures (Defaults)", "Passive Animals (Unlisted)", true, "Show passive/tameable creatures that have no specific entry in the biome sections below.");
            MinMonsterStars = Bind(config, "3 - Creatures (Defaults)", "Min Monster Stars (Unlisted)", 0, "Minimum star level for unlisted monsters (0 = All).", new AcceptableValueRange<int>(0, 3));
            MinAnimalStars = Bind(config, "3 - Creatures (Defaults)", "Min Animal Stars (Unlisted)", 0, "Minimum star level for unlisted animals (0 = All).", new AcceptableValueRange<int>(0, 3));

            Creatures.Clear();
            foreach (var def in CreatureDefinitions)
            {
                Creatures[def.Key] = new CreatureConfigEntry
                {
                    IsMonster = def.IsMonster,
                    Enabled = Bind(config, def.Section, def.DisplayName, def.DefaultEnabled, $"Show {def.DisplayName}."),
                    MinStars = Bind(config, def.Section, $"{def.DisplayName} - Min Stars", 0, $"Minimum star level for {def.DisplayName} (0 = All, requires the toggle above to also be on).", new AcceptableValueRange<int>(0, 3))
                };
            }

            TrackRaspberry = Bind(config, "10 - Resources (Berries)", "Raspberries", true, "Show Raspberries.");
            TrackBlueberry = Bind(config, "10 - Resources (Berries)", "Blueberries", true, "Show Blueberries.");
            TrackCloudberry = Bind(config, "10 - Resources (Berries)", "Cloudberries", true, "Show Cloudberries.");

            TrackRedMushroom = Bind(config, "11 - Resources (Mushrooms)", "Red Mushrooms", true, "Show standard Red Mushrooms.");
            TrackYellowMushroom = Bind(config, "11 - Resources (Mushrooms)", "Yellow Mushrooms", true, "Show Yellow Cave Mushrooms.");
            TrackBlueMushroom = Bind(config, "11 - Resources (Mushrooms)", "Blue Mushrooms", true, "Show Blue Mushrooms.");

            TrackDandelion = Bind(config, "12 - Resources (Plants & Crops)", "Dandelion", true, "Show Dandelions.");
            TrackThistle = Bind(config, "12 - Resources (Plants & Crops)", "Thistle", true, "Show Thistle.");
            TrackCarrotSeed = Bind(config, "12 - Resources (Plants & Crops)", "Carrot Seeds", true, "Show wild Carrot seeds.");
            TrackTurnipSeed = Bind(config, "12 - Resources (Plants & Crops)", "Turnip Seeds", true, "Show wild Turnip seeds.");
            TrackOnionSeed = Bind(config, "12 - Resources (Plants & Crops)", "Onion Seeds", true, "Show wild Onion seeds.");
            TrackBarley = Bind(config, "12 - Resources (Plants & Crops)", "Barley", true, "Show wild or grown Barley.");
            TrackFlax = Bind(config, "12 - Resources (Plants & Crops)", "Flax", true, "Show wild or grown Flax.");
            TrackMagecap = Bind(config, "12 - Resources (Plants & Crops)", "Magecap", true, "Show Magecap mushrooms/plants.");

            TrackFlint = Bind(config, "13 - Resources (Ground)", "Flint", true, "Show loose Flint.");
            TrackStone = Bind(config, "13 - Resources (Ground)", "Stones", false, "Show loose Stones.");
            TrackWood = Bind(config, "13 - Resources (Ground)", "Wood/Branches", false, "Show loose Wood.");

            TrackCopper = Bind(config, "14 - Resources (Ores)", "Copper", true, "Show Copper deposits.");
            TrackTin = Bind(config, "14 - Resources (Ores)", "Tin", true, "Show Tin deposits.");
            TrackIron = Bind(config, "14 - Resources (Ores)", "Muddy Scrap / Iron", true, "Show Iron scrap deposits.");
            TrackSilver = Bind(config, "14 - Resources (Ores)", "Silver", true, "Show Silver veins.");

            TrackChests = Bind(config, "15 - Structures (Functional)", "Chests & Containers", true, "Show treasure chests and storage.");
            TrackDungeons = Bind(config, "15 - Structures (Functional)", "Dungeons / Crypts / Caves", true, "Show Dungeon and Crypt entrances.");
            TrackPortals = Bind(config, "15 - Structures (Functional)", "Portals", true, "Show player and ruined portals.");
            TrackBeehives = Bind(config, "15 - Structures (Functional)", "Beehives", true, "Show wild and built Beehives.");

            TrackAbandonedRuins = Bind(config, "16 - Structures (Ruins & World)", "Abandoned Farms & Ruins", true, "Show ruined houses, farmsteads, and towers.");
            TrackStoneRings = Bind(config, "16 - Structures (Ruins & World)", "Stone Rings", true, "Show burial stone circles and rock formations.");
            TrackRunestones = Bind(config, "16 - Structures (Ruins & World)", "Runestones", true, "Show lore and vegvisir runestones.");
            TrackTarPits = Bind(config, "16 - Structures (Ruins & World)", "Tar Pits", true, "Show Plains tar pits.");
        }
    }
}
