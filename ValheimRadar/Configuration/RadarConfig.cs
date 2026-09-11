using BepInEx.Configuration;

namespace ValheimRadar
{
    public static class RadarConfig
    {
        // General
        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<float> ClusterDistance;

        // Master Group Toggles
        public static ConfigEntry<bool> Group_Creatures;
        public static ConfigEntry<bool> Group_Berries;
        public static ConfigEntry<bool> Group_Mushrooms;
        public static ConfigEntry<bool> Group_FlowersAndCrops;
        public static ConfigEntry<bool> Group_RocksAndFlint;
        public static ConfigEntry<bool> Group_Ores;
        public static ConfigEntry<bool> Group_FunctionalStructures;
        public static ConfigEntry<bool> Group_RuinsAndLocations;

        // Creature Filters
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

        public static void Initialize(ConfigFile config)
        {
            ScanRadius = config.Bind("1 - General", "ScanRadius", 100f, "Scan radius around player.");
            UpdateInterval = config.Bind("1 - General", "UpdateInterval", 1.0f, "Scan interval in seconds.");
            ClusterDistance = config.Bind("1 - General", "ClusterDistance", 15.0f, "Max distance between items to group into a cluster.");

            Group_Creatures = config.Bind("2 - Master Groups", "Enable Creatures Group", true, "Master toggle for all creatures.");
            Group_Berries = config.Bind("2 - Master Groups", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = config.Bind("2 - Master Groups", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = config.Bind("2 - Master Groups", "Enable Flowers and Crops Group", true, "Master toggle for plants, seeds, and crops.");
            Group_RocksAndFlint = config.Bind("2 - Master Groups", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = config.Bind("2 - Master Groups", "Enable Ores Group", true, "Master toggle for ore veins and deposits.");
            Group_FunctionalStructures = config.Bind("2 - Master Groups", "Enable Functional Structures", true, "Master toggle for chests, dungeons, portals, beehives.");
            Group_RuinsAndLocations = config.Bind("2 - Master Groups", "Enable Ruins & Locations", true, "Master toggle for stone rings, abandoned farms, ruins, runestones.");

            EnableMonsters = config.Bind("3 - Creatures", "Hostile Monsters", true, "Show aggressive creatures.");
            EnableAnimals = config.Bind("3 - Creatures", "Passive Animals", true, "Show passive/tameable animals.");
            MinMonsterStars = config.Bind("3 - Creatures", "Min Monster Stars", 0, "Minimum star level for monsters (0 = All, 1 = 1 Star+, 2 = 2 Stars only).");
            MinAnimalStars = config.Bind("3 - Creatures", "Min Animal Stars", 0, "Minimum star level for animals (0 = All, 1 = 1 Star+, 2 = 2 Stars only).");

            TrackRaspberry = config.Bind("4 - Resources (Berries)", "Raspberries", true, "Show Raspberries.");
            TrackBlueberry = config.Bind("4 - Resources (Berries)", "Blueberries", true, "Show Blueberries.");
            TrackCloudberry = config.Bind("4 - Resources (Berries)", "Cloudberries", true, "Show Cloudberries.");

            TrackRedMushroom = config.Bind("5 - Resources (Mushrooms)", "Red Mushrooms", true, "Show standard Red Mushrooms.");
            TrackYellowMushroom = config.Bind("5 - Resources (Mushrooms)", "Yellow Mushrooms", true, "Show Yellow Cave Mushrooms.");
            TrackBlueMushroom = config.Bind("5 - Resources (Mushrooms)", "Blue Mushrooms", true, "Show Blue Mushrooms.");

            TrackDandelion = config.Bind("6 - Resources (Plants & Crops)", "Dandelion", true, "Show Dandelions.");
            TrackThistle = config.Bind("6 - Resources (Plants & Crops)", "Thistle", true, "Show Thistle.");
            TrackCarrotSeed = config.Bind("6 - Resources (Plants & Crops)", "Carrot Seeds", true, "Show wild Carrot seeds.");
            TrackTurnipSeed = config.Bind("6 - Resources (Plants & Crops)", "Turnip Seeds", true, "Show wild Turnip seeds.");
            TrackOnionSeed = config.Bind("6 - Resources (Plants & Crops)", "Onion Seeds", true, "Show wild Onion seeds.");
            TrackBarley = config.Bind("6 - Resources (Plants & Crops)", "Barley", true, "Show wild or grown Barley.");
            TrackFlax = config.Bind("6 - Resources (Plants & Crops)", "Flax", true, "Show wild or grown Flax.");
            TrackMagecap = config.Bind("6 - Resources (Plants & Crops)", "Magecap", true, "Show Magecap mushrooms/plants.");

            TrackFlint = config.Bind("7 - Resources (Ground)", "Flint", true, "Show loose Flint.");
            TrackStone = config.Bind("7 - Resources (Ground)", "Stones", false, "Show loose Stones.");
            TrackWood = config.Bind("7 - Resources (Ground)", "Wood/Branches", false, "Show loose Wood.");

            TrackCopper = config.Bind("8 - Resources (Ores)", "Copper", true, "Show Copper deposits.");
            TrackTin = config.Bind("8 - Resources (Ores)", "Tin", true, "Show Tin deposits.");
            TrackIron = config.Bind("8 - Resources (Ores)", "Muddy Scrap / Iron", true, "Show Iron scrap deposits.");
            TrackSilver = config.Bind("8 - Resources (Ores)", "Silver", true, "Show Silver veins.");

            TrackChests = config.Bind("9 - Structures (Functional)", "Chests & Containers", true, "Show treasure chests and storage.");
            TrackDungeons = config.Bind("9 - Structures (Functional)", "Dungeons / Crypts / Caves", true, "Show Dungeon and Crypt entrances.");
            TrackPortals = config.Bind("9 - Structures (Functional)", "Portals", true, "Show player and ruined portals.");
            TrackBeehives = config.Bind("9 - Structures (Functional)", "Beehives", true, "Show wild and built Beehives.");

            TrackAbandonedRuins = config.Bind("10 - Structures (Ruins & World)", "Abandoned Farms & Ruins", true, "Show ruined houses, farmsteads, and towers.");
            TrackStoneRings = config.Bind("10 - Structures (Ruins & World)", "Stone Rings", true, "Show burial stone circles and rock formations.");
            TrackRunestones = config.Bind("10 - Structures (Ruins & World)", "Runestones", true, "Show lore and vegvisir runestones.");
            TrackTarPits = config.Bind("10 - Structures (Ruins & World)", "Tar Pits", true, "Show Plains tar pits.");
        }
    }
}