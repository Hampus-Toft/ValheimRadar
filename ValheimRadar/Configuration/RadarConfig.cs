using System;
using System.Collections.Generic;
using System.Reflection;
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

            // Only tameable species (those with a Tameable component in the game: Boar, Wolf, Lox,
            // Hen, Asksvin, Moose) keep a per-species Min Stars filter and get a Tamed/Wild filter -
            // see CreatureConfigEntry.MinStars/Tamed. Every other creature still displays its rolled star
            // level in the pin label, but can no longer be filtered by it (a 2-star Greyling is
            // exactly as dangerous/relevant as a 0-star one from a scouting perspective).
            public readonly bool Tameable;

            public CreatureDefinition(string canonicalKey, string[] aliases, string displayName, string section, bool isMonster, bool defaultEnabled = true, bool tameable = false)
            {
                CanonicalKey = canonicalKey;
                Aliases = aliases;
                DisplayName = displayName;
                Section = section;
                IsMonster = isMonster;
                DefaultEnabled = defaultEnabled;
                Tameable = tameable;
            }
        }

        public sealed class CreatureConfigEntry
        {
            public bool IsMonster;
            public ConfigEntry<bool> Enabled;

            // Null for every non-tameable creature (see CreatureDefinition.Tameable) - not bound at
            // all, so no Min Stars entry appears in the config UI for them. CreatureEvaluator treats
            // a null MinStars as "no filtering" (0), while the rolled star level is still appended
            // to the pin's display name regardless.
            public ConfigEntry<int> MinStars;

            // Tameable species only (null otherwise): show wild ones, tamed ones, or both.
            public ConfigEntry<TameFilter> Tamed;
        }

        // Which of a tameable species' creatures get pinned. Spirit-caller summons count as tamed.
        public enum TameFilter { Both, WildOnly, TamedOnly }

        internal static bool PassesTameFilter(TameFilter filter, bool isTamed)
        {
            switch (filter)
            {
                case TameFilter.WildOnly: return !isTamed;
                case TameFilter.TamedOnly: return isTamed;
                default: return true;
            }
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

            // True only for the Start Temple and the Black Forest Trader - these are always pinned
            // unconditionally (no group/section, no Enabled ConfigEntry bound at all - see the
            // Locations.Clear() loop in Initialize and LocationScanner.IsLocationCategoryEnabled),
            // since they're each unique-per-world landmarks players always want visible.
            public readonly bool AlwaysEnabled;

            public LocationDefinition(string canonicalKey, string[] prefabNames, string displayName, string section,
                LocationGroup group, string iconPng, string vanillaIcon = null, bool defaultEnabled = true, bool alwaysEnabled = false)
            {
                CanonicalKey = canonicalKey;
                PrefabNames = prefabNames;
                DisplayName = displayName;
                Section = section;
                Group = group;
                IconPng = iconPng;
                VanillaIcon = vanillaIcon;
                DefaultEnabled = defaultEnabled;
                AlwaysEnabled = alwaysEnabled;
            }
        }

        public sealed class LocationConfigEntry
        {
            public ConfigEntry<bool> Enabled;
        }

        private const string SecMeadows = "04 - Creatures (Meadows)";
        private const string SecBlackForest = "05 - Creatures (Black Forest)";
        private const string SecSwamp = "06 - Creatures (Swamp)";
        private const string SecMountain = "07 - Creatures (Mountain)";
        private const string SecPlains = "08 - Creatures (Plains)";
        private const string SecMistlands = "09 - Creatures (Mistlands)";
        private const string SecBosses = "03b - Bosses & Notable Creatures";
        private const string SecFish = "09b - Creatures (Fish)";
        private const string SecAshlands = "09c - Creatures (Ashlands)";
        private const string SecDeepNorth = "09d - Creatures (Deep North)";

        // Section 19 ("Locations (Landmarks)") was removed - the Start Temple and Black Forest
        // Trader are now always shown unconditionally (see LocationDefinition.AlwaysEnabled) rather
        // than gated by their own barely-used toggle group, so remaining Location sections were
        // renumbered down to close the gap.
        private const string SecBossLocations = "17 - Locations (Boss Altars)";
        private const string SecDungeonLocations = "18 - Locations (Dungeon Entrances)";
        private const string SecRunestoneLocations = "19 - Locations (Runestones)";
        private const string SecRuinLocations = "20 - Locations (Ruins & Structures)";
        private const string SecSpawnersAndLandmarks = "16 - Spawners & Landmarks";

        public static readonly CreatureDefinition[] CreatureDefinitions =
        {
            // MEADOWS
            // "boar_piggy" is the baby boar (loca "Piggy") that grows into a Boar once tamed and fed -
            // it must share the adult's definition (toggle, star filter, TrophyBoar icon) rather than
            // fall through to the generic bucket, where it has no icon of its own.
            new CreatureDefinition("boar", new[] { "boar", "boar_piggy", "boar_spiritcaller" }, "Boar", SecMeadows, isMonster: false, tameable: true),
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
            // Real prefab is "BlobElite" (loca $enemy_blobelite -> "Oozer") - "blob_elite" is not a
            // real prefab name, which is why this never matched in-game. The canonical key and the
            // "Poison Blob" display name are deliberately unchanged: DisplayName is the user-visible
            // config key, so renaming it would silently reset existing users' toggle.
            new CreatureDefinition("blob_elite", new[] { "blobelite" }, "Poison Blob", SecSwamp, isMonster: true),
            new CreatureDefinition("leech", new[] { "leech" }, "Leech", SecSwamp, isMonster: true),
            new CreatureDefinition("wraith", new[] { "wraith" }, "Wraith", SecSwamp, isMonster: true),
            // Bog Witch-related undead ("defeated_writhan" global key, loca $enemy_writhan). Distinct
            // prefab from "Wraith" despite the similar name; has its own TrophyWrithan sprite.
            new CreatureDefinition("writhan", new[] { "writhan" }, "Writhan", SecSwamp, isMonster: true),
            new CreatureDefinition("abomination", new[] { "abomination" }, "Abomination", SecSwamp, isMonster: true),

            // MOUNTAIN
            new CreatureDefinition("wolf", new[] { "wolf", "wolf_cub", "wolf_spiritcaller" }, "Wolf", SecMountain, isMonster: true, tameable: true),
            // Real prefab is "Bjorn" - "bear" never appears in it, which is why Bear silently
            // failed classification (and therefore its icon) entirely under the old
            // nameLower.Contains("bear") substring check. Bjorn_ragdoll (corpse) is deliberately
            // excluded - it's not a live creature to pin.
            new CreatureDefinition("bear", new[] { "bjorn", "bjorn_sleeping", "bjorn_spiritcaller" }, "Bear", SecMountain, isMonster: false),
            new CreatureDefinition("stonegolem", new[] { "stonegolem" }, "Stone Golem", SecMountain, isMonster: true),
            // Real prefab is "Hatchling" (loca $enemy_drake -> "Drake") - "drake" itself is not a
            // real prefab name at all, which is why it never matched in-game.
            new CreatureDefinition("drake", new[] { "hatchling" }, "Drake", SecMountain, isMonster: true),
            // Previously only reachable via the generic hostile fallback (no CreatureDefinitions
            // entry existed), so individually un-toggleable and stuck with the generic monster icon
            // instead of its own verified trophy sprite (see VanillaIconResolver).
            new CreatureDefinition("surtling", new[] { "surtling" }, "Surtling", SecMountain, isMonster: true),

            // PLAINS
            new CreatureDefinition("lox", new[] { "lox", "lox_calf" }, "Lox", SecPlains, isMonster: false, tameable: true),
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
            // Hatches from a Mistlands egg as "Chicken" and grows into a tameable "Hen". No trophy sprite.
            new CreatureDefinition("hen", new[] { "hen", "chicken" }, "Hen", SecMistlands, isMonster: false, tameable: true),

            // ASHLANDS
            new CreatureDefinition("asksvin", new[] { "asksvin", "asksvin_hatchling" }, "Asksvin", SecAshlands, isMonster: true, tameable: true),

            // DEEP NORTH
            new CreatureDefinition("moose", new[] { "moose", "moose_calf", "moose_spiritcaller" }, "Moose", SecDeepNorth, isMonster: false, tameable: true),

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

            // LANDMARKS - always shown unconditionally (AlwaysEnabled), not gated by any group/
            // toggle and not bound as a config entry at all (see Locations.Clear() loop below and
            // LocationScanner.IsLocationCategoryEnabled) - these are unique-per-world landmarks
            // players always want visible on the map.
            new LocationDefinition("landmark_starttemple", new[] { "starttemple" }, "Start Temple", "Always Shown", LocationGroup.Landmark, "landmark.png", alwaysEnabled: true),
            new LocationDefinition("landmark_trader", new[] { "vendor_blackforest" }, "Black Forest Trader", "Always Shown", LocationGroup.Landmark, "landmark.png", alwaysEnabled: true),

            // DUNGEON ENTRANCES
            new LocationDefinition("dungeon_blackforestcrypt", new[] { "crypt2", "crypt3", "crypt4" }, "Black Forest Dungeon", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_sunkencrypt", new[] { "sunkencrypt4" }, "Sunken Crypt", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_trollcave", new[] { "trollcave02" }, "Troll Cave", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition(MountainCaveKey, new[] { "mountaincave02" }, "Mountain Cave", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            // Not a Location of its own: a Mountain Cave whose generated rooms include a Tetra pond is
            // re-tagged with this key once its interior has been seen (see LocationScanner.ScanCaveVariants),
            // so it can be toggled apart from ordinary caves. No prefab names - never matched directly.
            new LocationDefinition(MountainCaveTetraKey, new string[0], "Mountain Cave (Tetra Pond)", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            // Hildir's quest dungeons (map pin names from hud_pin_hildir1/2/3). Rare, one-off Locations whose
            // roots have no ZNetView, so the physics-scan fallback never caught them.
            new LocationDefinition("dungeon_hildircrypt", new[] { "hildir_crypt" }, "Smouldering Tomb", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_hildircave", new[] { "hildir_cave" }, "Howling Cavern", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            new LocationDefinition("dungeon_hildirtower", new[] { "hildir_plainsfortress" }, "Sealed Tower", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
            // Black Forest bear den. Its Location root has no ZNetView either, so the physics-scan
            // fallback's "bearcave" alias never fired in practice.
            new LocationDefinition("dungeon_bearcave", new[] { "bearcave" }, "Bear Cave", SecDungeonLocations, LocationGroup.DungeonEntrance, "dungeon.png"),
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

        // Canonical keys of the plain Mountain Cave and its Tetra-pond variant (see LocationScanner.ScanCaveVariants).
        public const string MountainCaveKey = "dungeon_mountaincave";
        public const string MountainCaveTetraKey = "dungeon_mountaincave_tetra";

        // Used by ObjectEvaluator to guard its remaining generic/component-based rules (Runestones,
        // Dungeons' component fallback) so they only act as a defense-in-depth net for prefabs the
        // curated LocationDefinitions table doesn't already own, instead of double-pinning the same
        // POI through two independent pipelines.
        public static bool IsKnownLocationPrefab(string nameLower) =>
            !string.IsNullOrEmpty(nameLower) && LocationPrefabLookup.ContainsKey(nameLower);

        // Not called for AlwaysEnabled Locations (Start Temple/Trader) - LocationScanner.
        // IsLocationCategoryEnabled checks AlwaysEnabled first and never reaches this method for
        // them, so the LocationGroup.Landmark case below always returning true is effectively
        // unreachable, kept only so the switch stays exhaustive over every LocationGroup value.
        public static bool IsLocationGroupEnabled(LocationGroup group)
        {
            switch (group)
            {
                case LocationGroup.BossAltar: return Group_BossLocations.Value;
                case LocationGroup.Landmark: return true;
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
        public static ConfigEntry<float> CreatureClusterDistance;
        public static ConfigEntry<int> ScanBatchCount;
        public static ConfigEntry<float> LocationScanInterval;

        // Resource rescans and depleted-resource removal (see PermanentSpatialScanner,
        // PinManager.RecordScannedCells/MarkDepletedAt, Pinning/DepletionPatches.cs).
        public static ConfigEntry<float> ResourceRescanInterval;
        public static ConfigEntry<bool> RemoveDepletedResources;

        // Hiding picked regrowing pickables until they grow back (see PinManager.MarkPickedAt).
        public static ConfigEntry<bool> HidePickedUntilRespawn;

        // Manual pin removal (see PinManager.TryDismissPinAt / Pinning/MinimapPatches.cs). ClearDismissedPins
        // acts as a button: setting it true un-dismisses everything and RadarPlugin resets it to false.
        public static ConfigEntry<bool> EnablePinRemoval;
        public static ConfigEntry<bool> ClearDismissedPins;

        // Left-click cross-out (see PinManager.TryToggleCheckedAt / Pinning/MinimapPatches.cs).
        public static ConfigEntry<bool> EnablePinCrossOut;

        // Troubleshooting switches. RaisePlayerMarker gates the one place the plugin reorders vanilla map UI
        // (see Pinning/MinimapMarkerOrder.cs); DiagnosticLogging enables the throttled pin-name and
        // unmapped-Location log lines (PinManager.LogNameDiagnostics, LocationScanner).
        public static ConfigEntry<bool> RaisePlayerMarker;
        public static ConfigEntry<bool> DiagnosticLogging;

        // Master Group Toggles
        public static ConfigEntry<bool> Group_Creatures;
        public static ConfigEntry<bool> Group_Berries;
        public static ConfigEntry<bool> Group_Mushrooms;
        public static ConfigEntry<bool> Group_FlowersAndCrops;
        public static ConfigEntry<bool> Group_RocksAndFlint;
        public static ConfigEntry<bool> Group_Ores;
        public static ConfigEntry<bool> Group_FunctionalStructures;
        public static ConfigEntry<bool> Group_SpawnersAndLandmarks;
        public static ConfigEntry<bool> Group_BossLocations;
        public static ConfigEntry<bool> Group_DungeonLocations;
        public static ConfigEntry<bool> Group_RunestoneLocations;
        public static ConfigEntry<bool> Group_RuinLocations;

        // Creature Filters (fallback used for any creature without a specific entry above). No Min
        // Stars entries here - only tameable species (Boar/Wolf/Lox/Hen/Asksvin/Moose, see
        // CreatureDefinition.Tameable) get a star filter; unlisted monsters/animals never do.
        public static ConfigEntry<bool> EnableMonsters;
        public static ConfigEntry<bool> EnableAnimals;

        // When false (default) a creature pin whose icon identifies it drops the species name from its
        // label and shows only count/stars - see ItemCluster.ShouldHideCreatureName.
        public static ConfigEntry<bool> ShowCreatureNames;

        // Fish quality 1..5 (index 0 = quality 1); each one can be hidden separately. See IsFishQualityShown.
        public const int MaxFishQuality = 5;
        public static readonly ConfigEntry<bool>[] FishQualities = new ConfigEntry<bool>[MaxFishQuality];

        // Fish quality as the game stores it is 1-based; anything outside 1..MaxFishQuality is clamped.
        internal static int ClampFishQuality(int quality) => Math.Max(1, Math.Min(MaxFishQuality, quality));

        internal static bool IsFishQualityShown(int quality)
        {
            ConfigEntry<bool> entry = FishQualities[ClampFishQuality(quality) - 1];
            return entry == null || entry.Value;
        }

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

        // Ores (each gates its deposit/node rule only - see ResourceEvaluator.Rules). Guck
        // Sack lives here too, not under Spawners & Landmarks - it's a harvestable Swamp resource
        // players look for alongside ores, even though it's still physics-scan detected via
        // PoiEvaluator rather than a MineRock-based ResourceRule (see PoiEvaluator.Rules).
        public static ConfigEntry<bool> TrackCopper;
        public static ConfigEntry<bool> TrackTin;
        public static ConfigEntry<bool> TrackIron;
        public static ConfigEntry<bool> TrackSilver;
        public static ConfigEntry<bool> TrackObsidian;
        public static ConfigEntry<bool> TrackGuck;

        // Functional Structures (chests, beehives, the Bog Witch's camp). Dungeon entrances used to
        // have a "Dungeons" toggle here too - moved under Group_DungeonLocations/SecDungeonLocations
        // below since it's the same "dungeon entrance" concept as the curated Location group, just
        // its physics-scan fallback for entrances not in that curated table.
        public static ConfigEntry<bool> TrackChests;
        public static ConfigEntry<bool> TrackBuriedChests;
        public static ConfigEntry<bool> TrackBeehives;
        public static ConfigEntry<bool> TrackTrader;

        // Unlisted/modded fallback toggles, physics-scan detected (PoiEvaluator) rather than
        // ZoneSystem-discovered - each now lives under its matching curated Location group's section
        // instead of the old standalone "Ruins & Locations" master toggle, since they're really just
        // that group's catch-all for prefabs the curated LocationDefinitions table doesn't own
        // (StoneRings/TarPits toggles were removed outright - fully superseded by the ZoneSystem
        // -based LocationDefinitions "Stone Circle"/"Stonehenge"/"Tar Pit" entries).
        public static ConfigEntry<bool> TrackAbandonedRuins;
        public static ConfigEntry<bool> TrackRunestones;
        public static ConfigEntry<bool> TrackUnlistedDungeons;

        // Spawners & Landmarks (monster-spawner landmarks - was named "Points of Interest", which
        // said nothing about what it actually gates. DrakeNest/DecorativeStatues/MistlandsPOI
        // toggles were removed outright - fully superseded by the ZoneSystem-based
        // LocationDefinitions "Drake Nest"/"Mistlands Statue"/Ruins & Structures group entries, see
        // Group_RuinLocations. Guck Sack moved to the Ores group above - see TrackGuck.)
        public static ConfigEntry<bool> TrackGreydwarfNest;
        public static ConfigEntry<bool> TrackBodyPile;
        public static ConfigEntry<bool> TrackBonePile;

        private static int _order;

        private static ConfigEntry<T> Bind<T>(ConfigFile config, string section, string key, T defaultValue, string description, AcceptableValueBase acceptableValues = null)
        {
            var attrs = new ConfigurationManagerAttributes { Order = _order-- };
            var entry = config.Bind(section, key, defaultValue, new ConfigDescription(description, acceptableValues, attrs));
            MigrateLegacySection(config, entry, section, key);
            return entry;
        }

        // Section names are zero-padded ("01 - General") because BepInEx writes sections in
        // ordinal string order, so the old unpadded "10 - ..." sorted before "2 - ...". A value
        // saved under the old unpadded name is left behind by BepInEx as an orphaned entry; copy
        // it onto the renamed entry and drop the orphan so the old section disappears from the file.
        internal static void MigrateLegacySection(ConfigFile config, ConfigEntryBase entry, string section, string key)
        {
            string legacySection = GetLegacySectionName(section);
            if (legacySection == null) return;

            Dictionary<ConfigDefinition, string> orphans = GetOrphanedEntries(config);
            var legacyDefinition = new ConfigDefinition(legacySection, key);
            if (orphans == null || !orphans.TryGetValue(legacyDefinition, out string serialized)) return;

            orphans.Remove(legacyDefinition);
            entry.SetSerializedValue(serialized);
            config.Save();
        }

        // "01 - General" -> "1 - General", "03b - Bosses ..." -> "3b - Bosses ...". Null when the
        // section was never renamed (two-digit numbers were already sorted correctly).
        internal static string GetLegacySectionName(string section)
        {
            if (section == null || section.Length < 2 || section[0] != '0' || !char.IsDigit(section[1])) return null;
            return section.Substring(1);
        }

        private static PropertyInfo _orphanedEntriesProperty;

        // ConfigFile.OrphanedEntries is private in BepInEx 5 - holds every key read from the .cfg
        // that no Bind call has claimed yet.
        private static Dictionary<ConfigDefinition, string> GetOrphanedEntries(ConfigFile config)
        {
            try
            {
                if (_orphanedEntriesProperty == null)
                {
                    _orphanedEntriesProperty = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                return _orphanedEntriesProperty?.GetValue(config) as Dictionary<ConfigDefinition, string>;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Initialize(ConfigFile config)
        {
            _order = 0;

            ScanRadius = Bind(config, "01 - General", "ScanRadius", 100f, "Scan radius around player.", new AcceptableValueRange<float>(10f, 300f));
            UpdateInterval = Bind(config, "01 - General", "UpdateInterval", 1.0f, "Scan interval in seconds.", new AcceptableValueRange<float>(0.1f, 10f));
            ClusterDistance = Bind(config, "01 - General", "ClusterDistance", 15.0f, "Max distance between resources/points of interest to group into one pin. Creatures use Creature Cluster Distance instead.", new AcceptableValueRange<float>(1f, 50f));
            CreatureClusterDistance = Bind(config, "01 - General", "Creature Cluster Distance", 15.0f, "Max distance between creatures (animals, monsters, fish) of the same kind to group into one pin. Lower it to see creatures individually.", new AcceptableValueRange<float>(1f, 50f));
            ScanBatchCount = Bind(config, "01 - General", "ScanBatchCount", 4, "Creatures only: splits each full-radius scan into this many spatial batches, spread across successive update ticks, so a large ScanRadius doesn't cause a lag spike on any single tick. Higher values reduce per-tick cost but make moving creatures take longer to refresh (1 = scan the whole radius every tick).", new AcceptableValueRange<int>(1, 20));
            LocationScanInterval = Bind(config, "01 - General", "LocationScanInterval", 5f, "How often (seconds) to poll Valheim's own zone/location system for newly-generated world Locations (dungeons, ruins, runestones, boss altars, etc.). Independent of UpdateInterval since new Locations only appear as unexplored zones generate.", new AcceptableValueRange<float>(1f, 30f));
            ResourceRescanInterval = Bind(config, "01 - General", "ResourceRescanInterval", 30f, "How often (seconds) already-scanned ground in the loaded area around you is scanned again for resources and physics-detected points of interest. Rescans pick up anything missed the first time and notice resources that are gone (see Remove Depleted Resources). Only a couple of map cells are rescanned per update tick. 0 = never rescan.", new AcceptableValueRange<float>(0f, 600f));
            RemoveDepletedResources = Bind(config, "01 - General", "Remove Depleted Resources", true, "Remove pins for resources that don't grow back (ore deposits, obsidian, muddy scrap piles, flint, stones, branches, Guck Sacks): immediately when you hit or pick one yourself, and after rescans confirm it's gone when someone else cleared it. Berries, mushrooms, flowers and crops regrow and are never removed (see Hide Picked Until Respawn).");
            HidePickedUntilRespawn = Bind(config, "01 - General", "Hide Picked Until Respawn", true, "When berries, mushrooms, flowers or crops near you are picked (by you or another player), hide that pin until it grows back (each pickable's own respawn time, in in-game world time) instead of leaving it on the map. Turning this off shows every currently hidden pin again.");

            EnablePinRemoval = Bind(config, "01 - General", "Enable Pin Removal", true, "Right-click a ValheimRadar resource or location pin on the large map (same as removing a normal map pin) to dismiss it. Dismissed pins stay hidden across scans and sessions for that world, and only that pin is affected - the rest of its category keeps showing. Creature pins are live and can't be dismissed.");
            ClearDismissedPins = Bind(config, "01 - General", "Restore Dismissed Pins", false, "Set to true to bring back every pin dismissed with right-click in the current world. Resets itself to false.");
            EnablePinCrossOut = Bind(config, "01 - General", "Enable Pin Cross-Out", true, "Left-click a ValheimRadar resource or location pin on the large map to cross it out (an X, like checking off a normal map pin) - e.g. a dungeon you've already looted. Left-click it again to clear the X. Crossed-out pins are remembered per world. Creature pins are live and can't be crossed out.");
            RaisePlayerMarker = Bind(config, "01 - General", "Raise Player Marker", false, "Draw your own map marker (and the ship marker) above all ValheimRadar pins. Applied when you connect to a world. Turn off to leave Valheim's map layering untouched if it conflicts with another map mod.");
            DiagnosticLogging = Bind(config, "01 - General", "Diagnostic Logging", false, "Write verbose troubleshooting lines (pin created/updated/removed, Location scan summaries, pin name state while the large map is open, unmapped Location prefabs) to the BepInEx log. Leave off unless troubleshooting.");

            Group_Creatures = Bind(config, "02 - Master Groups", "Enable Creatures Group", true, "Master toggle for all creatures, bosses, and fish.");
            Group_Berries = Bind(config, "02 - Master Groups", "Enable Berries Group", true, "Master toggle for all berry bushes.");
            Group_Mushrooms = Bind(config, "02 - Master Groups", "Enable Mushrooms Group", true, "Master toggle for all mushroom types.");
            Group_FlowersAndCrops = Bind(config, "02 - Master Groups", "Enable Flowers and Crops Group", true, "Master toggle for wild plants, seeds, and crops.");
            Group_RocksAndFlint = Bind(config, "02 - Master Groups", "Enable Ground Pickables Group", true, "Master toggle for loose rocks, flint, wood.");
            Group_Ores = Bind(config, "02 - Master Groups", "Enable Ores Group", true, "Master toggle for ore deposits (copper, tin, silver, obsidian, iron scrap piles) and harvestable Guck Sacks. Loose ore and ingots lying on the ground are never tracked.");
            Group_FunctionalStructures = Bind(config, "02 - Master Groups", "Enable Functional Structures", true, "Master toggle for chests (buried and unburied), beehives, and the Bog Witch's camp.");
            Group_SpawnersAndLandmarks = Bind(config, "02 - Master Groups", "Enable Spawners & Landmarks Group", true, "Master toggle for monster-spawner landmarks (Greydwarf Nest, Body Pile, Bone Pile).");
            Group_BossLocations = Bind(config, "02 - Master Groups", "Enable Boss Altars Group", true, "Master toggle for boss summoning altars (Eikthyr, Elder, Bonemass, Moder, Yagluth, the Queen).");
            Group_DungeonLocations = Bind(config, "02 - Master Groups", "Enable Dungeon Entrances Group", true, "Master toggle for dungeon/cave Location entrances (crypts, sunken crypt, troll cave, mountain caves, Hildir's Smouldering Tomb/Howling Cavern/Sealed Tower, Bear Cave, Dvergr town) plus any unlisted dungeon-plane entrance caught by the physics-scan fallback.");
            Group_RunestoneLocations = Bind(config, "02 - Master Groups", "Enable Runestones Group", true, "Master toggle for every biome's runestone Locations, plus any unlisted/modded runestone caught by the physics-scan fallback.");
            Group_RuinLocations = Bind(config, "02 - Master Groups", "Enable Ruins & Structures Group", true, "Master toggle for the full ZoneSystem-based ruins/structures roster (ruined houses, stone towers, shipwrecks, Mistlands structures, tar pits, etc.), plus any unlisted/modded ruin caught by the physics-scan fallback. The Start Temple and Black Forest Trader are always shown and have no toggle.");

            EnableMonsters = Bind(config, "03 - Creatures (Defaults)", "Hostile Monsters (Unlisted)", true, "Show hostile creatures that have no specific entry in the sections below.");
            EnableAnimals = Bind(config, "03 - Creatures (Defaults)", "Passive Animals (Unlisted)", true, "Show passive/tameable creatures that have no specific entry in the sections below.");
            ShowCreatureNames = Bind(config, "03 - Creatures (Defaults)", "Show Creature Names", false, "Show the creature's name in its pin label. When off (default), a creature pin whose icon identifies it shows only the count and star rating (a count like '2x' plus one star character per rolled star). Creatures without a specific icon always keep their name.");

            Creatures.Clear();
            foreach (var def in CreatureDefinitions)
            {
                var entry = new CreatureConfigEntry
                {
                    IsMonster = def.IsMonster,
                    Enabled = Bind(config, def.Section, def.DisplayName, def.DefaultEnabled, $"Show {def.DisplayName}.")
                };

                // Only tameable species get a star filter and a tamed/wild filter - every other creature
                // still shows its rolled star level in the pin label (see CreatureEvaluator), it just
                // can't be filtered out by it anymore.
                if (def.Tameable)
                {
                    entry.MinStars = Bind(config, def.Section, $"{def.DisplayName} - Min Stars", 0, $"Minimum star level for {def.DisplayName} (0 = All, requires the toggle above to also be on).", new AcceptableValueRange<int>(0, 3));
                    entry.Tamed = Bind(config, def.Section, $"{def.DisplayName} - Tamed or Wild", TameFilter.Both, $"Which {def.DisplayName} to show: Both, WildOnly (not tamed) or TamedOnly (tamed, including summoned ones). Requires the toggle above to also be on.");
                }

                Creatures[def.CanonicalKey] = entry;
            }

            for (int quality = 1; quality <= MaxFishQuality; quality++)
            {
                FishQualities[quality - 1] = Bind(config, SecFish, $"Fish Quality {quality}", true, $"Show fish of quality {quality} (1 = common, {MaxFishQuality} = rarest). The quality is shown as Q{quality} under the fish's pin. Requires the fish's own toggle above to also be on.");
            }

            Locations.Clear();
            foreach (var def in LocationDefinitions)
            {
                // AlwaysEnabled Locations (Start Temple, Black Forest Trader) are never bound - they
                // have no toggle and no section, and are always shown (see
                // LocationScanner.IsLocationCategoryEnabled).
                if (def.AlwaysEnabled) continue;

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

            TrackCopper = Bind(config, "14 - Resources (Ores)", "Copper", true, "Show Copper deposits.");
            TrackTin = Bind(config, "14 - Resources (Ores)", "Tin", true, "Show Tin deposits.");
            TrackIron = Bind(config, "14 - Resources (Ores)", "Iron", true, "Show Muddy scrap piles (iron).");
            TrackSilver = Bind(config, "14 - Resources (Ores)", "Silver", true, "Show Silver deposits.");
            TrackObsidian = Bind(config, "14 - Resources (Ores)", "Obsidian", true, "Show Obsidian deposits.");
            TrackGuck = Bind(config, "14 - Resources (Ores)", "Guck Sack", true, "Show Guck Sacks on Swamp trees.");

            TrackChests = Bind(config, "15 - Structures (Functional)", "Chests (Above-Ground)", true, "Show natural/world-spawn treasure chests that are not buried (player-built chests are never tracked). Buried chests have their own toggle below.");
            TrackBuriedChests = Bind(config, "15 - Structures (Functional)", "Buried Chests", true, "Show buried/hidden treasure chests (e.g. the Meadows burial-mound chest, the Ashlands memorial chest).");
            TrackBeehives = Bind(config, "15 - Structures (Functional)", "Beehives", true, "Show wild Beehives (player-built beehives are never tracked).");
            TrackTrader = Bind(config, "15 - Structures (Functional)", "Bog Witch Camp", true, "Show the Bog Witch's camp. This is a different trader from the Black Forest Trader/Haldor, who is always shown and has no toggle.");

            TrackGreydwarfNest = Bind(config, SecSpawnersAndLandmarks, "Greydwarf Nest", true, "Show Greydwarf Nests (Black Forest monster spawner).");
            TrackBodyPile = Bind(config, SecSpawnersAndLandmarks, "Body Pile", true, "Show Body Piles (Swamp Draugr spawner).");
            TrackBonePile = Bind(config, SecSpawnersAndLandmarks, "Bone Pile", true, "Show Bone Piles (Swamp Skeleton spawner).");

            // Unlisted/modded fallback toggles - each lives in its matching curated Location group's
            // own section now instead of the old standalone "Ruins & Locations" master group, since
            // they're really just that group's physics-scan catch-all (see Group_RuinLocations/
            // Group_RunestoneLocations/Group_DungeonLocations and PoiEvaluator's Runestones/
            // AbandonedRuins/Dungeons rules).
            TrackAbandonedRuins = Bind(config, SecRuinLocations, "Abandoned Ruins (Unlisted/Modded)", true, "Show ruins not covered by the curated list above (Combat Ruin, Meadows Village 2).");
            TrackRunestones = Bind(config, SecRunestoneLocations, "Runestones (Unlisted/Modded)", true, "Show any runestone not covered by the curated list above.");
            TrackUnlistedDungeons = Bind(config, SecDungeonLocations, "Dungeon Entrances (Unlisted)", true, "Show dungeon-plane entrances not covered by the curated list above (any Teleport-based entrance), detected by component signature rather than name.");
        }
    }
}
