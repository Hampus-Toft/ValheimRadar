using BepInEx.Configuration;

namespace ValheimRadar
{
    public static class RadarConfig
    {
        // Naming convention: Group_* = master category toggles (Section 2, gate an entire section),
        // Enable* = creature-type filters (Section 3), Track* = individual resource/structure toggles (Sections 4-10).

        private const string SectionGeneral = "1 - General";
        private const string SectionMasterGroups = "2 - Master Groups";
        private const string SectionCreatures = "3 - Creatures";
        private const string SectionBerries = "4 - Resources (Berries)";
        private const string SectionMushrooms = "5 - Resources (Mushrooms)";
        private const string SectionPlantsAndCrops = "6 - Resources (Plants & Crops)";
        private const string SectionGround = "7 - Resources (Ground)";
        private const string SectionOres = "8 - Resources (Ores)";
        private const string SectionFunctionalStructures = "9 - Structures (Functional)";
        private const string SectionRuinsAndWorld = "10 - Structures (Ruins & World)";

        // General
        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<float> ClusterDistance;

        // Master Group Toggles
        public static ConfigEntry<bool> Group_Creatures;
        public static ConfigEntry<bool> Group_Berries;
        public static ConfigEntry<bool> Group_Mushrooms;
        public static ConfigEntry<bool> Group_FlowersAndCrops;
        public static ConfigEntry<bool> Group_GroundPickables;
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
            ScanRadius = config.Bind(SectionGeneral, "ScanRadius", 100f, "Scan radius around player.");
            UpdateInterval = config.Bind(SectionGeneral, "UpdateInterval", 1.0f, "Scan interval in seconds.");
            ClusterDistance = config.Bind(SectionGeneral, "ClusterDistance", 15.0f, "Max distance between items to group into a cluster.");

            Group_Creatures = config.Bind(SectionMasterGroups, "Enable Creatures Group", true, "Master toggle for all creatures.");
            Group_Berries = config.Bind(SectionMasterGroups, "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = config.Bind(SectionMasterGroups, "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = config.Bind(SectionMasterGroups, "Enable Flowers and Crops Group", true, "Master toggle for plants, seeds, and crops.");
            Group_GroundPickables = config.Bind(SectionMasterGroups, "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = config.Bind(SectionMasterGroups, "Enable Ores Group", true, "Master toggle for ore veins and deposits.");
            Group_FunctionalStructures = config.Bind(SectionMasterGroups, "Enable Functional Structures", true, "Master toggle for chests, dungeons, portals, beehives.");
            Group_RuinsAndLocations = config.Bind(SectionMasterGroups, "Enable Ruins & Locations", true, "Master toggle for stone rings, abandoned farms, ruins, runestones.");

            EnableMonsters = config.Bind(SectionCreatures, "Hostile Monsters", true, "Show aggressive creatures.");
            EnableAnimals = config.Bind(SectionCreatures, "Passive Animals", true, "Show passive/tameable animals.");
            MinMonsterStars = config.Bind(SectionCreatures, "Min Monster Stars", 0, "Minimum star level for monsters (0 = All, 1 = 1 Star+, 2 = 2 Stars only).");
            MinAnimalStars = config.Bind(SectionCreatures, "Min Animal Stars", 0, "Minimum star level for animals (0 = All, 1 = 1 Star+, 2 = 2 Stars only).");

            TrackRaspberry = config.Bind(SectionBerries, "Raspberries", true, "Show Raspberries.");
            TrackBlueberry = config.Bind(SectionBerries, "Blueberries", true, "Show Blueberries.");
            TrackCloudberry = config.Bind(SectionBerries, "Cloudberries", true, "Show Cloudberries.");

            TrackRedMushroom = config.Bind(SectionMushrooms, "Red Mushrooms", true, "Show standard Red Mushrooms.");
            TrackYellowMushroom = config.Bind(SectionMushrooms, "Yellow Mushrooms", true, "Show Yellow Cave Mushrooms.");
            TrackBlueMushroom = config.Bind(SectionMushrooms, "Blue Mushrooms", true, "Show Blue Mushrooms.");

            TrackDandelion = config.Bind(SectionPlantsAndCrops, "Dandelion", true, "Show Dandelions.");
            TrackThistle = config.Bind(SectionPlantsAndCrops, "Thistle", true, "Show Thistle.");
            TrackCarrotSeed = config.Bind(SectionPlantsAndCrops, "Carrot Seeds", true, "Show wild Carrot seeds.");
            TrackTurnipSeed = config.Bind(SectionPlantsAndCrops, "Turnip Seeds", true, "Show wild Turnip seeds.");
            TrackOnionSeed = config.Bind(SectionPlantsAndCrops, "Onion Seeds", true, "Show wild Onion seeds.");
            TrackBarley = config.Bind(SectionPlantsAndCrops, "Barley", true, "Show wild or grown Barley.");
            TrackFlax = config.Bind(SectionPlantsAndCrops, "Flax", true, "Show wild or grown Flax.");
            TrackMagecap = config.Bind(SectionPlantsAndCrops, "Magecap", true, "Show Magecap mushrooms/plants.");

            TrackFlint = config.Bind(SectionGround, "Flint", true, "Show loose Flint.");
            TrackStone = config.Bind(SectionGround, "Stones", false, "Show loose Stones.");
            TrackWood = config.Bind(SectionGround, "Wood/Branches", false, "Show loose Wood.");

            TrackCopper = config.Bind(SectionOres, "Copper", true, "Show Copper deposits.");
            TrackTin = config.Bind(SectionOres, "Tin", true, "Show Tin deposits.");
            TrackIron = config.Bind(SectionOres, "Muddy Scrap / Iron", true, "Show Iron scrap deposits.");
            TrackSilver = config.Bind(SectionOres, "Silver", true, "Show Silver veins.");

            TrackChests = config.Bind(SectionFunctionalStructures, "Chests & Containers", true, "Show treasure chests and storage.");
            TrackDungeons = config.Bind(SectionFunctionalStructures, "Dungeons / Crypts / Caves", true, "Show Dungeon and Crypt entrances.");
            TrackPortals = config.Bind(SectionFunctionalStructures, "Portals", true, "Show player and ruined portals.");
            TrackBeehives = config.Bind(SectionFunctionalStructures, "Beehives", true, "Show wild and built Beehives.");

            TrackAbandonedRuins = config.Bind(SectionRuinsAndWorld, "Abandoned Farms & Ruins", true, "Show ruined houses, farmsteads, and towers.");
            TrackStoneRings = config.Bind(SectionRuinsAndWorld, "Stone Rings", true, "Show burial stone circles and rock formations.");
            TrackRunestones = config.Bind(SectionRuinsAndWorld, "Runestones", true, "Show lore and vegvisir runestones.");
            TrackTarPits = config.Bind(SectionRuinsAndWorld, "Tar Pits", true, "Show Plains tar pits.");
        }
    }
}
