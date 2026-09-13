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
            // Stable id used for the Creatures dictionary, the "creature:{key}" categoryKey, and
            // VanillaIconResolver's trophy-sprite table. Not required to be a real prefab name.
            public readonly string CanonicalKey;

            // Exact, lowercased raw prefab names (verbatim GameObject.name.ToLower(), no prefix
            // stripping - see ObjectEvaluator.IsExactAlias) that all count as this species/fish.
            // Matched via a flat dictionary (AliasLookup below) instead of an ordered substring
            // scan, so declaration order no longer affects correctness the way it used to (a
            // variant like "greydwarf_elite" no longer needs to be declared before "greydwarf").
            public readonly string[] Aliases;

            public readonly string DisplayName;
            public readonly string Section;
            public readonly bool IsMonster;
            public readonly bool DefaultEnabled;

            public CreatureDefinition(string canonicalKey, string[] aliases, string displayName, string section, bool isMonster, bool defaultEnabled = true)
            {
                CanonicalKey = canonicalKey;
                Aliases = aliases;
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
        private const string SecBosses = "3b - Bosses & Notable Creatures";
        private const string SecFish = "9b - Creatures (Fish)";

        public static readonly CreatureDefinition[] CreatureDefinitions =
        {
            // MEADOWS
            new CreatureDefinition("boar", new[] { "boar" }, "Boar", SecMeadows, isMonster: false),
            new CreatureDefinition("neck", new[] { "neck" }, "Neck", SecMeadows, isMonster: false),
            new CreatureDefinition("deer", new[] { "deer", "deer_white" }, "Deer", SecMeadows, isMonster: false),
            new CreatureDefinition("greyling", new[] { "greyling" }, "Greyling", SecMeadows, isMonster: true),

            // BLACK FOREST
            new CreatureDefinition("greydwarf", new[] { "greydwarf" }, "Greydwarf", SecBlackForest, isMonster: true),
            new CreatureDefinition("greydwarf_elite", new[] { "greydwarf_elite" }, "Greydwarf Brute", SecBlackForest, isMonster: true),
            new CreatureDefinition("greydwarf_shaman", new[] { "greydwarf_shaman" }, "Greydwarf Shaman", SecBlackForest, isMonster: true),
            new CreatureDefinition("skeleton", new[] { "skeleton", "skeleton_meadows", "skeleton_mountains", "skeleton_swamps", "skeleton_poison", "skeleton_hildir" }, "Skeleton", SecBlackForest, isMonster: true),
            new CreatureDefinition("troll", new[] { "troll" }, "Troll", SecBlackForest, isMonster: true),

            // SWAMP
            new CreatureDefinition("draugr", new[] { "draugr", "draugr_ranged" }, "Draugr", SecSwamp, isMonster: true),
            new CreatureDefinition("draugr_elite", new[] { "draugr_elite" }, "Draugr Elite", SecSwamp, isMonster: true),
            new CreatureDefinition("blob", new[] { "blob" }, "Blob", SecSwamp, isMonster: true),
            new CreatureDefinition("blob_elite", new[] { "blob_elite" }, "Poison Blob", SecSwamp, isMonster: true),
            new CreatureDefinition("leech", new[] { "leech" }, "Leech", SecSwamp, isMonster: true),
            new CreatureDefinition("wraith", new[] { "wraith" }, "Wraith", SecSwamp, isMonster: true),
            new CreatureDefinition("abomination", new[] { "abomination" }, "Abomination", SecSwamp, isMonster: true),

            // MOUNTAIN
            new CreatureDefinition("wolf", new[] { "wolf", "wolf_cub", "wolf_spiritcaller" }, "Wolf", SecMountain, isMonster: true),
            // Real prefab is "Bjorn" - "bear" never appears in it, which is why Bear silently
            // failed classification (and therefore its icon) entirely under the old
            // nameLower.Contains("bear") substring check. Bjorn_ragdoll (corpse) is deliberately
            // excluded - it's not a live creature to pin.
            new CreatureDefinition("bear", new[] { "bjorn", "bjorn_sleeping", "bjorn_spiritcaller" }, "Bear", SecMountain, isMonster: false),
            new CreatureDefinition("stonegolem", new[] { "stonegolem" }, "Stone Golem", SecMountain, isMonster: true),
            // Real prefab is "Hatchling" (loca $enemy_drake -> "Drake") - "drake" itself is not a
            // real prefab name at all, which is why it never matched in-game.
            new CreatureDefinition("drake", new[] { "hatchling" }, "Drake", SecMountain, isMonster: true),

            // PLAINS
            new CreatureDefinition("lox", new[] { "lox", "lox_calf" }, "Lox", SecPlains, isMonster: false),
            new CreatureDefinition("deathsquito", new[] { "deathsquito" }, "Deathsquito", SecPlains, isMonster: true),
            // Real prefabs use "Goblin*" naming (loca resolves to "Fuling*") - "fuling"/
            // "fuling_berserker"/"fuling_shaman" are not real prefab names, which is why none of
            // these ever matched in-game. GoblinArcher shares the base Fuling's in-game name/icon.
            new CreatureDefinition("fuling", new[] { "goblin", "goblinarcher" }, "Fuling", SecPlains, isMonster: true),
            new CreatureDefinition("fuling_berserker", new[] { "goblinbrute" }, "Fuling Berserker", SecPlains, isMonster: true),
            new CreatureDefinition("fuling_shaman", new[] { "goblinshaman" }, "Fuling Shaman", SecPlains, isMonster: true),
            new CreatureDefinition("growth", new[] { "growth" }, "Growth (Lox Spawn)", SecPlains, isMonster: true),

            // MISTLANDS
            new CreatureDefinition("seeker", new[] { "seeker" }, "Seeker", SecMistlands, isMonster: true),
            new CreatureDefinition("seeker_brood", new[] { "seeker_brood" }, "Seeker Brood", SecMistlands, isMonster: true),
            new CreatureDefinition("gjall", new[] { "gjall" }, "Gjall", SecMistlands, isMonster: true),
            new CreatureDefinition("tick", new[] { "tick" }, "Tick", SecMistlands, isMonster: true),
            new CreatureDefinition("dvergr", new[] { "dvergr" }, "Dvergr", SecMistlands, isMonster: false),
            new CreatureDefinition("dvergrmage", new[] { "dvergrmage" }, "Dvergr Mage", SecMistlands, isMonster: true),
            new CreatureDefinition("fenring", new[] { "fenring", "fenring_cultist" }, "Fenring", SecMistlands, isMonster: true),

            // BOSSES & NOTABLE CREATURES - previously only reachable via the generic hostile
            // fallback (no CreatureDefinitions entry existed for any of these), so individually
            // un-toggleable and dependent on VanillaIconResolver's boss-trophy entries matching
            // by accident via the raw cleaned prefab name.
            new CreatureDefinition("eikthyr", new[] { "eikthyr" }, "Eikthyr", SecBosses, isMonster: true),
            new CreatureDefinition("elder", new[] { "gd_king" }, "The Elder", SecBosses, isMonster: true),
            new CreatureDefinition("bonemass", new[] { "bonemass" }, "Bonemass", SecBosses, isMonster: true),
            new CreatureDefinition("moder", new[] { "dragon" }, "Moder", SecBosses, isMonster: true),
            new CreatureDefinition("yagluth", new[] { "goblinking" }, "Yagluth", SecBosses, isMonster: true),
            new CreatureDefinition("queen", new[] { "seekerqueen" }, "The Queen", SecBosses, isMonster: true),
            new CreatureDefinition("fader", new[] { "fader" }, "Fader", SecBosses, isMonster: true),
            new CreatureDefinition("serpent", new[] { "serpent" }, "Sea Serpent", SecBosses, isMonster: true),
            // Not a Character/Humanoid (no AI, not directly attackable) - a MineRock-based ocean
            // structure. What players commonly call a "kraken" is this - Valheim has no creature
            // literally named Kraken. Matched via its own component branch in ObjectEvaluator
            // (mirrors the Fish branch), since it has neither Character nor Fish components.
            new CreatureDefinition("leviathan", new[] { "leviathan", "leviathanlava" }, "Leviathan", SecBosses, isMonster: false),

            // FISH - Fish prefabs (Fish1..Fish12) have no Character/Humanoid component at all,
            // just Fish+ItemDrop, so they never went through ShouldPinGameObject's Character-based
            // creature branch before; see ObjectEvaluator's dedicated Fish detection path. Folded
            // into the same Group_Creatures toggle and AliasLookup as regular creatures.
            new CreatureDefinition("fish_perch", new[] { "fish1" }, "Perch", SecFish, isMonster: false),
            new CreatureDefinition("fish_pike", new[] { "fish2" }, "Pike", SecFish, isMonster: false),
            new CreatureDefinition("fish_tuna", new[] { "fish3" }, "Tuna", SecFish, isMonster: false),
            new CreatureDefinition("fish_tetra", new[] { "fish4_cave" }, "Tetra", SecFish, isMonster: false),
            new CreatureDefinition("fish_trollfish", new[] { "fish5" }, "Trollfish", SecFish, isMonster: false),
            new CreatureDefinition("fish_giantherring", new[] { "fish6" }, "Giant Herring", SecFish, isMonster: false),
            new CreatureDefinition("fish_grouper", new[] { "fish7" }, "Grouper", SecFish, isMonster: false),
            new CreatureDefinition("fish_coralcod", new[] { "fish8" }, "Coral Cod", SecFish, isMonster: false),
            new CreatureDefinition("fish_anglerfish", new[] { "fish9" }, "Anglerfish", SecFish, isMonster: false),
            new CreatureDefinition("fish_northernsalmon", new[] { "fish10" }, "Northern Salmon", SecFish, isMonster: false),
            new CreatureDefinition("fish_magmafish", new[] { "fish11" }, "Magmafish", SecFish, isMonster: false),
            new CreatureDefinition("fish_pufferfish", new[] { "fish12" }, "Pufferfish", SecFish, isMonster: false),
        };

        // Flat exact-match lookup built once from every CreatureDefinition's Aliases - replaces
        // the old ordered Contains() scan (see ObjectEvaluator.FindCreatureOverride). Declaration
        // order in CreatureDefinitions above no longer matters for correctness now that matching
        // is exact.
        public static readonly Dictionary<string, CreatureDefinition> AliasLookup = BuildAliasLookup();

        private static Dictionary<string, CreatureDefinition> BuildAliasLookup()
        {
            var map = new Dictionary<string, CreatureDefinition>();
            foreach (var def in CreatureDefinitions)
            {
                foreach (var alias in def.Aliases)
                {
                    map[alias] = def;
                }
            }
            return map;
        }

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
        public static ConfigEntry<bool> Group_PointsOfInterest;

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

        // Ores (each now gates a Deposit/Ore/Ingot trio - see ObjectEvaluator.ResourceRules)
        public static ConfigEntry<bool> TrackCopper;
        public static ConfigEntry<bool> TrackTin;
        public static ConfigEntry<bool> TrackIron;
        public static ConfigEntry<bool> TrackSilver;
        public static ConfigEntry<bool> TrackObsidian;

        // Functional Structures
        public static ConfigEntry<bool> TrackChests;
        public static ConfigEntry<bool> TrackDungeons;
        public static ConfigEntry<bool> TrackBeehives;
        public static ConfigEntry<bool> TrackTrader;

        // Ruins & Locations
        public static ConfigEntry<bool> TrackAbandonedRuins;
        public static ConfigEntry<bool> TrackStoneRings;
        public static ConfigEntry<bool> TrackRunestones;
        public static ConfigEntry<bool> TrackTarPits;

        // Points of Interest (monster spawners, harvestable landmarks, and biome-specific
        // structures - split from "Ruins & Locations" since it's a newer, separately-toggleable
        // batch of additions)
        public static ConfigEntry<bool> TrackGreydwarfNest;
        public static ConfigEntry<bool> TrackBodyPile;
        public static ConfigEntry<bool> TrackBonePile;
        public static ConfigEntry<bool> TrackGuck;
        public static ConfigEntry<bool> TrackDrakeNest;
        public static ConfigEntry<bool> TrackMistlandsPOI;
        public static ConfigEntry<bool> TrackDecorativeStatues;

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

            Group_Creatures = Bind(config, "2 - Master Groups", "Enable Creatures Group", true, "Master toggle for all creatures, bosses, and fish.");
            Group_Berries = Bind(config, "2 - Master Groups", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = Bind(config, "2 - Master Groups", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = Bind(config, "2 - Master Groups", "Enable Flowers and Crops Group", true, "Master toggle for wild plants, seeds, and crops.");
            Group_RocksAndFlint = Bind(config, "2 - Master Groups", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = Bind(config, "2 - Master Groups", "Enable Ores Group", true, "Master toggle for ore deposits, raw ore, and ingots.");
            Group_FunctionalStructures = Bind(config, "2 - Master Groups", "Enable Functional Structures", true, "Master toggle for chests, dungeon entrances, beehives.");
            Group_RuinsAndLocations = Bind(config, "2 - Master Groups", "Enable Ruins & Locations", true, "Master toggle for stone rings, abandoned ruins, runestones, tar pits.");
            Group_PointsOfInterest = Bind(config, "2 - Master Groups", "Enable Points of Interest", true, "Master toggle for monster spawners, harvestable landmarks, traders, and biome-specific structures.");

            EnableMonsters = Bind(config, "3 - Creatures (Defaults)", "Hostile Monsters (Unlisted)", true, "Show hostile creatures that have no specific entry in the sections below.");
            EnableAnimals = Bind(config, "3 - Creatures (Defaults)", "Passive Animals (Unlisted)", true, "Show passive/tameable creatures that have no specific entry in the sections below.");
            MinMonsterStars = Bind(config, "3 - Creatures (Defaults)", "Min Monster Stars (Unlisted)", 0, "Minimum star level for unlisted monsters (0 = All).", new AcceptableValueRange<int>(0, 3));
            MinAnimalStars = Bind(config, "3 - Creatures (Defaults)", "Min Animal Stars (Unlisted)", 0, "Minimum star level for unlisted animals (0 = All).", new AcceptableValueRange<int>(0, 3));

            Creatures.Clear();
            foreach (var def in CreatureDefinitions)
            {
                Creatures[def.CanonicalKey] = new CreatureConfigEntry
                {
                    IsMonster = def.IsMonster,
                    Enabled = Bind(config, def.Section, def.DisplayName, def.DefaultEnabled, $"Show {def.DisplayName}."),
                    MinStars = Bind(config, def.Section, $"{def.DisplayName} - Min Stars", 0, $"Minimum star level for {def.DisplayName} (0 = All, requires the toggle above to also be on). Not applicable to fish.", new AcceptableValueRange<int>(0, 3))
                };
            }

            TrackRaspberry = Bind(config, "10 - Resources (Berries)", "Raspberries", true, "Show wild Raspberry bushes.");
            TrackBlueberry = Bind(config, "10 - Resources (Berries)", "Blueberries", true, "Show wild Blueberry bushes.");
            TrackCloudberry = Bind(config, "10 - Resources (Berries)", "Cloudberries", true, "Show wild Cloudberry bushes.");

            TrackRedMushroom = Bind(config, "11 - Resources (Mushrooms)", "Red Mushrooms", true, "Show standard Red Mushrooms.");
            TrackYellowMushroom = Bind(config, "11 - Resources (Mushrooms)", "Yellow Mushrooms", true, "Show Yellow Cave Mushrooms.");
            TrackBlueMushroom = Bind(config, "11 - Resources (Mushrooms)", "Blue Mushrooms", true, "Show Blue Mushrooms.");

            TrackDandelion = Bind(config, "12 - Resources (Plants & Crops)", "Dandelion", true, "Show wild Dandelions.");
            TrackThistle = Bind(config, "12 - Resources (Plants & Crops)", "Thistle", true, "Show wild Thistle.");
            TrackCarrotSeed = Bind(config, "12 - Resources (Plants & Crops)", "Carrot Seeds", true, "Show wild Carrots (never player-planted ones - those are a different, Piece-based object).");
            TrackTurnipSeed = Bind(config, "12 - Resources (Plants & Crops)", "Turnip Seeds", true, "Show wild Turnips (never player-planted ones - those are a different, Piece-based object).");
            TrackOnionSeed = Bind(config, "12 - Resources (Plants & Crops)", "Onion Seeds", true, "Show wild Onions (never player-planted ones - those are a different, Piece-based object).");
            TrackBarley = Bind(config, "12 - Resources (Plants & Crops)", "Barley", true, "Show wild Barley (never player-planted ones - those are a different, Piece-based object).");
            TrackFlax = Bind(config, "12 - Resources (Plants & Crops)", "Flax", true, "Show wild Flax (never player-planted ones - those are a different, Piece-based object).");
            TrackMagecap = Bind(config, "12 - Resources (Plants & Crops)", "Magecap", true, "Show wild Magecap mushrooms (never player-planted ones - those are a different, Piece-based object).");

            TrackFlint = Bind(config, "13 - Resources (Ground)", "Flint", true, "Show loose Flint.");
            TrackStone = Bind(config, "13 - Resources (Ground)", "Stones", false, "Show loose Stones.");
            TrackWood = Bind(config, "13 - Resources (Ground)", "Wood/Branches", false, "Show loose Wood/Branches.");

            TrackCopper = Bind(config, "14 - Resources (Ores)", "Copper", true, "Show Copper deposits, raw ore, and ingots.");
            TrackTin = Bind(config, "14 - Resources (Ores)", "Tin", true, "Show Tin deposits, raw ore, and ingots.");
            TrackIron = Bind(config, "14 - Resources (Ores)", "Iron", true, "Show Iron scrap sources and ingots.");
            TrackSilver = Bind(config, "14 - Resources (Ores)", "Silver", true, "Show Silver deposits, raw ore, and ingots.");
            TrackObsidian = Bind(config, "14 - Resources (Ores)", "Obsidian", true, "Show Obsidian deposits.");

            TrackChests = Bind(config, "15 - Structures (Functional)", "Chests & Containers", true, "Show natural/world-spawn treasure chests (player-built chests are never tracked).");
            TrackDungeons = Bind(config, "15 - Structures (Functional)", "Dungeons / Crypts / Caves", true, "Show dungeon-plane entrances (crypts, caves, etc.), detected by their teleport behavior rather than name.");
            TrackBeehives = Bind(config, "15 - Structures (Functional)", "Beehives", true, "Show wild Beehives (player-built beehives are never tracked).");
            TrackTrader = Bind(config, "15 - Structures (Functional)", "Traders", true, "Show Haldor and the Bog Witch's camp.");

            TrackAbandonedRuins = Bind(config, "16 - Structures (Ruins & World)", "Abandoned Farms & Ruins", true, "Show ruined houses, farmsteads, and towers.");
            TrackStoneRings = Bind(config, "16 - Structures (Ruins & World)", "Stone Rings", true, "Show burial stone circles and rock formations.");
            TrackRunestones = Bind(config, "16 - Structures (Ruins & World)", "Runestones", true, "Show lore and vegvisir runestones.");
            TrackTarPits = Bind(config, "16 - Structures (Ruins & World)", "Tar Pits", true, "Show Plains tar pits. NOTE: reports suggest these may not currently be detectable at all - see ObjectEvaluator's TarPits rule comment.");

            TrackGreydwarfNest = Bind(config, "17 - Points of Interest", "Greydwarf Nest", true, "Show Greydwarf Nests (Black Forest monster spawner).");
            TrackBodyPile = Bind(config, "17 - Points of Interest", "Body Pile", true, "Show Body Piles (Swamp Draugr spawner).");
            TrackBonePile = Bind(config, "17 - Points of Interest", "Bone Pile", true, "Show Bone Piles (Swamp Skeleton spawner).");
            TrackGuck = Bind(config, "17 - Points of Interest", "Guck Sack", true, "Show Guck Sacks on Swamp trees.");
            TrackDrakeNest = Bind(config, "17 - Points of Interest", "Drake Nest", true, "Show Drake Nests (Mountain).");
            TrackMistlandsPOI = Bind(config, "17 - Points of Interest", "Mistlands Structures", true, "Show Dvergr guard towers, lighthouses, harbours, excavation sites, giant remains, and ancient sword markers.");
            TrackDecorativeStatues = Bind(config, "17 - Points of Interest", "Decorative Statues", false, "Show purely decorative Mistlands statues (no function - off by default).");
        }
    }
}
