# ValheimRadar

*By Hampus Toft.* **Client-side mod - only players who want the radar need it; nothing to install on the server.**

ValheimRadar keeps scanning the area around you and pins what it finds on the minimap and large map, so you can see
what's nearby without walking every hill. Pins of the same kind that are close together are merged into one, with a
count (e.g. `5x Copper`).

## What gets pinned

| Kind | Examples | How it behaves |
|---|---|---|
| **Creatures** | Every vanilla creature and boss, grouped by biome; fish; the Leviathan | Live pins that follow the creatures and disappear when they leave the scan radius. Each creature has its own toggle; tameable ones (Boar, Wolf, Lox, Hen, Asksvin, Moose) also have a minimum star level and a tamed/wild filter (e.g. only wild 2-star Wolves, or only your own tamed ones). Fish show their quality (Q1-Q5) under the pin, and each quality level can be hidden separately. |
| **Resources** | Copper, tin, silver, obsidian, muddy scrap piles, Guck Sacks; berries; mushrooms; Dandelion, Thistle, Magecap and wild crops; flint, stones, branches | Remembered for the world once found, so they're back on the map the next time you log in. |
| **Points of interest** | Dungeon and cave entrances (including Bear Caves, Hildir's quest dungeons, and Mountain Caves with a Tetra pond as their own toggle), boss altars, runestones, ruins and towers, shipwrecks, tar pits, Greydwarf nests, body/bone piles, chests (above-ground and buried), wild beehives, the Bog Witch, Haldor and the Start Temple | Read directly from Valheim's world data, so they appear as soon as their area is generated. One pin each, remembered for the world. |

Pins use Valheim's own icons wherever the game has one: a creature's trophy, a resource's item icon.

Player-built structures (chests, beehives, planted crops) and loose items on the ground (dropped ore, ingots) are
never pinned.

## Keeping the map up to date

- **Mined out:** ore deposits, scrap piles, flint and other resources that don't grow back lose their pin as soon as
  you hit or pick them. If another player cleared them, the pin goes away once a rescan confirms they're gone.
- **Picked:** berries, mushrooms, flowers and crops are hidden once picked (by anyone nearby) and come back when they
  regrow, based on each plant's own respawn time in in-game time.
- **Dismissed:** right-click a resource or point-of-interest pin on the large map to hide it for good in that world,
  e.g. a dungeon you've already cleared. The *Restore Dismissed Pins* setting brings them all back.
- **Crossed out:** left-click a resource or point-of-interest pin on the large map to put an X over it (like checking
  off your own map pins), e.g. a dungeon you've looted that you still want to see. Left-click again to clear it.

Saved pins are stored per world in `BepInEx/config/ValheimRadar/PinData/<world>.db`. Only what changed gets written,
and saved pins are loaded and drawn gradually around you and in the visible map area, so a big save doesn't freeze
the game or get you dropped from a dedicated server.

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn, the Valheim Library](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) - used for the pin icons.

## Installation

Install with a mod manager (r2modman / Thunderstore Mod Manager / Gale); dependencies are installed automatically.
For a manual install, extract the whole package (`ValheimRadar.dll` plus the `e_sqlite3` / `libe_sqlite3` SQLite
files next to it) into `BepInEx/plugins/ValheimRadar/`.

## Configuration

Settings live in `BepInEx/config/HampusToft.ValheimRadar.cfg` and can be changed in-game with
[Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/) (F1). Changes apply
immediately. Settings from older versions (`com.yourname.valheimradar.cfg`) are imported automatically.

| Section | What's in it |
|---|---|
| General | Scan radius (default 100 m), scan interval, cluster distance (separately for creatures and for resources/points of interest), rescan interval, removing mined-out resources, hiding picked plants, right-click dismissal, left-click cross-out, drawing your own marker above the pins, diagnostic logging |
| Master Groups | One switch per group: creatures, berries, mushrooms, plants & crops, ground pickables, ores, functional structures, spawners, boss altars, dungeons, runestones, ruins |
| Creatures | Per-creature toggles by biome, plus minimum star level and tamed/wild filter for tameable species; whether creature pins show names (off by default - the icon already says what it is) |
| Resources / Structures / Locations | A toggle for each individual resource and point of interest |

### Custom icons

Put a PNG in `BepInEx/config/ValheimRadar/` to replace an icon: name it after one object (e.g. `wolf.png`,
`crypt.png`) or after a whole category (e.g. `monster.png`, `berry.png`, `dungeon.png`).

## AI disclaimer

This mod was mostly made using [Claude Code](https://claude.com/claude-code), Anthropic's AI coding assistant. The
design and requirements are mine; Claude Code did most of the work: analysing Valheim's game code, and writing the
code, tests and documentation, all under my direction and review. Please report any problems on
[GitHub](https://github.com/Hampus-Toft/ValheimRadar/issues).
