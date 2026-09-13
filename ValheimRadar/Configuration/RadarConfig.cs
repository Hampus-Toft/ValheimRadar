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

        // World "Location" content (see ZoneSystem.LocationInstance/ZoneLocation, queried via
        // Scanning/LocationScanner.cs) - dungeons, ruins, boss altars, runestones, the trader,
        // shipwrecks, etc. Grouped coarsely by what kind of thing they are, mirroring the
        // Group_X master-toggle pattern used elsewhere in this file.
        public enum LocationGroup { BossAltar, Landmark, DungeonEntrance, Runestone, Ruin }

        public sealed class LocationDefinition
        {
            // Stable id used for the Locations dictionary, the "location:{key}" categoryKey, and
            // the on-disk save file. Not required to be a real prefab name.
            public readonly string CanonicalKey;

            // Exact, lowercased ZoneLocation.m_prefabName values that all count as this POI type -
            // matched via LocationPrefabLookup below, mirroring AliasLookup's flat exact-match
            // scheme for creatures.
            public readonly string[] PrefabNames;

            public readonly string DisplayName;
            public readonly string Section;
            public readonly LocationGroup Group;
            public readonly string IconPng;
            public readonly string VanillaIcon;
            public readonly bool DefaultEnabled;

            public LocationDefinition(string canonicalKey, string[] prefabNames, string displayName, string section,
                LocationGroup group, string iconPng, string vanillaIcon = null, bool defaultEnabled = true)
            {
                CanonicalKey = canonicalKey;
                PrefabNames = prefabNames;
                DisplayName = displayName;
                Section = section;
                Group = group;
                IconPng = iconPng;
                VanillaIcon = vanillaIcon;
                DefaultEnabled = defaultEnabled;
            }
        }

        public sealed class LocationConfigEntry
        {
            public ConfigEntry<bool> Enabled;
        }

        private const string SecMeadows = "4 - Creatures (Meadows)";
        private const string SecBlackForest = "5 - Creatures (Black Forest)";
        private const string SecSwamp = "6 - Creatures (Swamp)";
        private const string SecMountain = "7 - Creatures (Mountain)";
        private const string SecPlains = "8 - Creatures (Plains)";
        private const string SecMistlands = "9 - Creatures (Mistlands)";
        private const string SecBosses = "3b - Bosses & Notable Creatures";
        private const string SecFish = "9b - Creatures (Fish)";

        private const string SecBossLocations = "18 - Locations (Boss Altars)";
        private const string SecLandmarkLocations = "19 - Locations (Landmarks)";
        private const string SecDungeonLocations = "20 - Locations (Dungeon Entrances)";
        private const string SecRunestoneLocations = "21 - Locations (Runestones)";
        private const string SecRuinLocations = "22 - Locations (Ruins & Structures)";

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

        // ~115 raw ZoneLocation.m_prefabName values from Valheim's own world-gen data, merged into
        // ~53 toggleable POI types across 5 groups. Source: a datamined table of every relevant
        // Location's prefab name, biome, and placement constraints - cross-checked field-for-field
        // against the live ZoneSystem.ZoneLocation struct (m_prefabName, m_biome, m_prioritized,
        // m_unique, etc.) via reflection against the installed game's assembly_valheim.dll, so these
        // prefab names are exact, not guessed.
        public static readonly LocationDefinition[] LocationDefinitions =
        {
            // BOSS ALTARS - reuse VanillaIconResolver's existing verified trophy sprites.
            new LocationDefinition("boss_eikthyr", new[] { "eikthyrnir" }, "Eikthyr Altar", SecBossLocations, LocationGroup.BossAltar, "boss_altar.png", "TrophyEikthyr"),
            new LocationDefinition("boss_elder", new[] { "gdking" }, "Elder Altar", SecBossLocations, LocationGroup.BossAltar, "boss_altar.png", "TrophyTheElder"),
            new LocationDefinition("boss_bonemass", new[] { "bonemass" }, "Bonemass Altar", SecBossLocations, LocationGroup.BossAltar, "boss_altar.png", "TrophyBonemass"),
            new LocationDefinition("boss_moder", new[] { "dragonqueen" }, "Moder Altar", SecBossLocations, LocationGroup.BossAltar, "boss_altar.png", "TrophyDragonQueen"),
            new LocationDefinition("boss_yagluth", new[] { "goblinking" }, "Yagluth Altar", SecBossLocations, LocationGroup.BossAltar, "boss_altar.png", "TrophyGoblinKing"),
            new LocationDefinition("boss_queen", new[] { "mistlands_dvergrbossentrance1" }, "Queen Entrance", SecBossLocations, LocationGroup.BossAltar, "boss_altar.png", "TrophySeekerQueen"),

            // LANDMARKS
            new LocationDefinition("landmark_starttemple", new[] { "starttemple" }, "Start Temple", SecLandmarkLocations, LocationGroup.Landmark, "landmark.png"),
            new LocationDefinition("landmark_trader", new[] { "vendor_blackforest" }, "Black Forest Trader", SecLandmarkLocations, LocationGroup.Landmark, "landmark.png"),

            // DUNGEON ENTRANCES
            new LocationDefinition("dungeon_blackforestcrypt", new[] { "crypt2", "crypt3", "crypt4" }, "Black Forest Dungeon", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_sunkencrypt", new[] { "sunkencrypt4" }, "Sunken Crypt", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_trollcave", new[] { "trollcave02" }, "Troll Cave", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_mountaincave", new[] { "mountaincave02" }, "Mountain Cave", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_dvergrtown", new[] { "mistlands_dvergrtownentrance1", "mistlands_dvergrtownentrance2" }, "Dvergr Town", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),

            // RUNESTONES - one entry per biome variant.
            new LocationDefinition("runestone_greydwarfs", new[] { "runestone_greydwarfs" }, "Runestone (Greydwarfs)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_blackforest", new[] { "runestone_blackforest" }, "Runestone (Black Forest)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_meadows", new[] { "runestone_meadows" }, "Runestone (Meadows)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_boars", new[] { "runestone_boars" }, "Runestone (Boars)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_mistlands", new[] { "runestone_mistlands" }, "Runestone (Mistlands)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_mountains", new[] { "runestone_mountains" }, "Runestone (Mountains)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_drakelorestone", new[] { "drakelorestone" }, "Runestone (Drake Lorestone)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_draugr", new[] { "runestone_draugr" }, "Runestone (Draugr)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_swamps", new[] { "runestone_swamps" }, "Runestone (Swamps)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),
            new LocationDefinition("runestone_plains", new[] { "runestone_plains" }, "Runestone (Plains)", SecRunestoneLocations, LocationGroup.Runestone, "runestone.png"),

            // RUINS & STRUCTURES
            new LocationDefinition("ruin_blackforestruins", new[] { "ruin1", "ruin2", "stonehouse3", "stonehouse4" }, "Black Forest Ruins", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_stonetower", new[] { "stonetowerruins03", "stonetowerruins07", "stonetowerruins08", "stonetowerruins09", "stonetowerruins10", "stonetowerruins04", "stonetowerruins05" }, "Stone Tower Ruins", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_greydwarfcamp", new[] { "greydwarf_camp1" }, "Greydwarf Camp", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_shipwreck", new[] { "shipwreck01", "shipwreck02", "shipwreck03", "shipwreck04" }, "Shipwreck", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_meadowsruinedhouse", new[] { "woodhouse1", "woodhouse2", "woodhouse3", "woodhouse4", "woodhouse5", "woodhouse6", "woodhouse7", "woodhouse8", "woodhouse9", "woodhouse10", "woodhouse11", "woodhouse12", "woodhouse13" }, "Meadows Ruined House", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_meadowsfarm", new[] { "woodfarm1" }, "Meadows Farm", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_meadowsvillage", new[] { "woodvillage1" }, "Meadows Village", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_shipburialmound", new[] { "shipsetting01" }, "Ship Burial Mound", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_stonecircle", new[] { "stonecircle" }, "Stone Circle", SecRuinLocations, LocationGroup.Ruin, "stone_ring.png"),
            new LocationDefinition("ruin_dolmen", new[] { "dolmen01", "dolmen02", "dolmen03" }, "Dolmen", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mistlandsroadpost", new[] { "mistlands_roadpost1" }, "Mistlands Road Post", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mistlandsstatue", new[] { "mistlands_statuegroup1", "mistlands_statue1", "mistlands_statue2" }, "Mistlands Statue", SecRuinLocations, LocationGroup.Ruin, "ruin.png", defaultEnabled: false),
            new LocationDefinition("ruin_mistlandsharbour", new[] { "mistlands_harbour1" }, "Mistlands Harbour", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_dvergrguardtower", new[] { "mistlands_guardtower1_new", "mistlands_guardtower1_ruined_new", "mistlands_guardtower1_ruined_new2", "mistlands_guardtower2_new", "mistlands_guardtower3_new", "mistlands_guardtower3_ruined_new" }, "Dvergr Guard Tower", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mistlandslighthouse", new[] { "mistlands_lighthouse1_new" }, "Mistlands Lighthouse", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_dvergrexcavation", new[] { "mistlands_excavation1", "mistlands_excavation2", "mistlands_excavation3" }, "Dvergr Excavation", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mistlandsviaduct", new[] { "mistlands_viaduct1", "mistlands_viaduct2" }, "Mistlands Viaduct", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mistlandsrockspire", new[] { "mistlands_rockspire1" }, "Mistlands Rock Spire", SecRuinLocations, LocationGroup.Ruin, "ruin.png", defaultEnabled: false),
            new LocationDefinition("ruin_giantremains", new[] { "mistlands_giant1", "mistlands_giant2" }, "Giant Remains", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_giantswords", new[] { "mistlands_swords1", "mistlands_swords2", "mistlands_swords3" }, "Giant Swords", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_abandonedcabin", new[] { "abandonedlogcabin02", "abandonedlogcabin03", "abandonedlogcabin04" }, "Abandoned Cabin", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mountaingrave", new[] { "mountaingrave01" }, "Mountain Grave", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_mountainwell", new[] { "mountainwell1" }, "Mountain Well", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_waymarker", new[] { "waymarker01", "waymarker02" }, "Waymarker", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_drakenest", new[] { "drakenest01" }, "Drake Nest", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_goblintower", new[] { "ruin3", "stonetower1", "stonetower3" }, "Goblin Tower Ruins", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_goblincamp", new[] { "goblincamp2" }, "Goblin Camp", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_stonehenge", new[] { "stonehenge1", "stonehenge2", "stonehenge3", "stonehenge4", "stonehenge5", "stonehenge6" }, "Stonehenge", SecRuinLocations, LocationGroup.Ruin, "stone_ring.png"),
            new LocationDefinition("ruin_tarpit", new[] { "tarpit1", "tarpit2", "tarpit3" }, "Tar Pit", SecRuinLocations, LocationGroup.Ruin, "tarpit.png"),
            new LocationDefinition("ruin_swampgrave", new[] { "grave1" }, "Swamp Grave", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_swampruins", new[] { "swampruin1", "swampruin2" }, "Swamp Ruins", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_swampfirehole", new[] { "firehole" }, "Swamp Fire Hole", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_infestedtree", new[] { "infestedtree01" }, "Infested Tree", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_swamphut", new[] { "swamphut1", "swamphut2", "swamphut3", "swamphut4", "swamphut5" }, "Swamp Hut", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_swampwell", new[] { "swampwell1" }, "Swamp Well", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
            new LocationDefinition("ruin_meteorite", new[] { "meteorite" }, "Meteorite", SecRuinLocations, LocationGroup.Ruin, "ruin.png"),
        };

        // Flat exact-match lookup built once from every LocationDefinition's PrefabNames - mirrors
        // AliasLookup/BuildAliasLookup for creatures.
        public static readonly Dictionary<string, LocationDefinition> LocationPrefabLookup = BuildLocationPrefabLookup();
        public static readonly Dictionary<string, LocationDefinition> CanonicalLocationLookup = BuildCanonicalLocationLookup();

        private static Dictionary<string, LocationDefinition> BuildLocationPrefabLookup()
        {
            var map = new Dictionary<string, LocationDefinition>();
            foreach (var def in LocationDefinitions)
            {
                foreach (var prefab in def.PrefabNames)
                {
                    map[prefab] = def;
                }
            }
            return map;
        }

        private static Dictionary<string, LocationDefinition> BuildCanonicalLocationLookup()
        {
            var map = new Dictionary<string, LocationDefinition>();
            foreach (var def in LocationDefinitions)
            {
                map[def.CanonicalKey] = def;
            }
            return map;
        }

        public static readonly Dictionary<string, LocationConfigEntry> Locations = new Dictionary<string, LocationConfigEntry>();

        // Used by ObjectEvaluator to guard its remaining generic/component-based rules (Runestones,
        // Dungeons' component fallback) so they only act as a defense-in-depth net for prefabs the
        // curated LocationDefinitions table doesn't already own, instead of double-pinning the same
        // POI through two independent pipelines.
        public static bool IsKnownLocationPrefab(string nameLower) =>
            !string.IsNullOrEmpty(nameLower) && LocationPrefabLookup.ContainsKey(nameLower);

        public static bool IsLocationGroupEnabled(LocationGroup group)
        {
            switch (group)
            {
                case LocationGroup.BossAltar: return Group_BossLocations.Value;
                case LocationGroup.Landmark: return Group_LandmarkLocations.Value;
                case LocationGroup.DungeonEntrance: return Group_DungeonLocations.Value;
                case LocationGroup.Runestone: return Group_RunestoneLocations.Value;
                case LocationGroup.Ruin: return Group_RuinLocations.Value;
                default: return false;
            }
        }

        // General
        public static ConfigEntry<float> ScanRadius;
        public static ConfigEntry<float> UpdateInterval;
        public static ConfigEntry<float> ClusterDistance;
        public static ConfigEntry<int> ScanBatchCount;
        public static ConfigEntry<float> LocationScanInterval;

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
        public static ConfigEntry<bool> Group_BossLocations;
        public static ConfigEntry<bool> Group_LandmarkLocations;
        public static ConfigEntry<bool> Group_DungeonLocations;
        public static ConfigEntry<bool> Group_RunestoneLocations;
        public static ConfigEntry<bool> Group_RuinLocations;

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

        // Ruins & Locations (StoneRings/TarPits toggles removed - fully superseded by the
        // ZoneSystem-based LocationDefinitions "Stone Circle"/"Stonehenge"/"Tar Pit" entries, see
        // Group_RuinLocations)
        public static ConfigEntry<bool> TrackAbandonedRuins;
        public static ConfigEntry<bool> TrackRunestones;

        // Points of Interest (monster spawners and harvestable landmarks - split from "Ruins &
        // Locations" since it's a newer, separately-toggleable batch of additions. DrakeNest/
        // DecorativeStatues/MistlandsPOI toggles removed - fully superseded by the ZoneSystem-based
        // LocationDefinitions "Drake Nest"/"Mistlands Statue"/Ruins & Structures group entries, see
        // Group_RuinLocations)
        public static ConfigEntry<bool> TrackGreydwarfNest;
        public static ConfigEntry<bool> TrackBodyPile;
        public static ConfigEntry<bool> TrackBonePile;
        public static ConfigEntry<bool> TrackGuck;

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
            LocationScanInterval = Bind(config, "1 - General", "LocationScanInterval", 5f, "How often (seconds) to poll Valheim's own zone/location system for newly-generated world Locations (dungeons, ruins, runestones, boss altars, etc.). Independent of UpdateInterval since new Locations only appear as unexplored zones generate.", new AcceptableValueRange<float>(1f, 30f));

            Group_Creatures = Bind(config, "2 - Master Groups", "Enable Creatures Group", true, "Master toggle for all creatures, bosses, and fish.");
            Group_Berries = Bind(config, "2 - Master Groups", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = Bind(config, "2 - Master Groups", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = Bind(config, "2 - Master Groups", "Enable Flowers and Crops Group", true, "Master toggle for wild plants, seeds, and crops.");
            Group_RocksAndFlint = Bind(config, "2 - Master Groups", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = Bind(config, "2 - Master Groups", "Enable Ores Group", true, "Master toggle for ore deposits, raw ore, and ingots.");
            Group_FunctionalStructures = Bind(config, "2 - Master Groups", "Enable Functional Structures", true, "Master toggle for chests, dungeon entrances, beehives.");
            Group_RuinsAndLocations = Bind(config, "2 - Master Groups", "Enable Ruins & Locations", true, "Master toggle for abandoned ruins and runestones not covered by the dedicated Location groups below.");
            Group_PointsOfInterest = Bind(config, "2 - Master Groups", "Enable Points of Interest", true, "Master toggle for monster spawners and harvestable landmarks.");
            Group_BossLocations = Bind(config, "2 - Master Groups", "Enable Boss Altars Group", true, "Master toggle for boss summoning altars (Eikthyr, Elder, Bonemass, Moder, Yagluth, the Queen).");
            Group_LandmarkLocations = Bind(config, "2 - Master Groups", "Enable Landmarks Group", true, "Master toggle for the Start Temple and the Black Forest Trader.");
            Group_DungeonLocations = Bind(config, "2 - Master Groups", "Enable Dungeon Entrances Group", true, "Master toggle for dungeon/cave Location entrances (crypts, sunken crypt, troll cave, mountain cave, Dvergr town).");
            Group_RunestoneLocations = Bind(config, "2 - Master Groups", "Enable Runestones Group", true, "Master toggle for every biome's runestone Locations.");
            Group_RuinLocations = Bind(config, "2 - Master Groups", "Enable Ruins & Structures Group", true, "Master toggle for the full ZoneSystem-based ruins/structures roster (ruined houses, stone towers, shipwrecks, Mistlands structures, tar pits, etc.).");

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

            Locations.Clear();
            foreach (var def in LocationDefinitions)
            {
                Locations[def.CanonicalKey] = new LocationConfigEntry
                {
                    Enabled = Bind(config, def.Section, def.DisplayName, def.DefaultEnabled, $"Show {def.DisplayName}.")
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
            TrackTrader = Bind(config, "15 - Structures (Functional)", "Traders", true, "Show the Bog Witch's camp (the Black Forest Trader has its own toggle - see the Landmarks group).");

            TrackAbandonedRuins = Bind(config, "16 - Structures (Ruins & World)", "Abandoned Farms & Ruins (Other)", true, "Show ruins not covered by the dedicated Location groups below (Combat Ruin, Meadows Village 2).");
            TrackRunestones = Bind(config, "16 - Structures (Ruins & World)", "Runestones (Other)", true, "Show any runestone not covered by the dedicated Runestones group below (unlisted/modded variants).");

            TrackGreydwarfNest = Bind(config, "17 - Points of Interest", "Greydwarf Nest", true, "Show Greydwarf Nests (Black Forest monster spawner).");
            TrackBodyPile = Bind(config, "17 - Points of Interest", "Body Pile", true, "Show Body Piles (Swamp Draugr spawner).");
            TrackBonePile = Bind(config, "17 - Points of Interest", "Bone Pile", true, "Show Bone Piles (Swamp Skeleton spawner).");
            TrackGuck = Bind(config, "17 - Points of Interest", "Guck Sack", true, "Show Guck Sacks on Swamp trees.");
        }
    }
}
