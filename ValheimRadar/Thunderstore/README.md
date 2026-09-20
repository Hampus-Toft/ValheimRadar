# ValheimRadar

Scans the area around you for nearby creatures, resources, and structures, and
places clustered pins on the in-game minimap so you always know what's close by.

## Features

- Configurable scan radius, scan interval, and clustering distance.
- Per-creature toggles and minimum star-level filters, grouped by biome
  (Meadows, Black Forest, Swamp, Mountain, Plains, Mistlands).
- Master group toggles for creatures, berries, mushrooms, flowers & crops,
  ground pickables, ores, functional structures, and ruins/locations.
- Resource and structure pins persist per-world between sessions, so
  previously discovered locations reappear immediately after reconnecting.
- Right-click a resource or location pin on the large map to dismiss it (e.g.
  to mark a dungeon as explored). Dismissed pins stay hidden for that world
  across sessions; use the "Restore Dismissed Pins" setting to bring them back.
  Live creature pins can't be dismissed.
- Creature pins show only count and star rating by default (the icon already
  identifies the creature); enable "Show Creature Names" to restore names.
- Your own map marker is drawn on top of all pins.
- All settings are exposed as standard BepInEx config entries, so they work
  out of the box with [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn, the Valheim Library](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) - required for minimap icon sprites.

## Installation

Install with a mod manager (r2modman / Thunderstore Mod Manager) - dependencies
are pulled in automatically. For manual installs, extract this package's DLL
into `BepInEx/plugins/` alongside Jotunn.

## Configuration

After the first run, edit `BepInEx/config/com.yourname.valheimradar.cfg`, or
use Configuration Manager in-game (F1 by default) to tweak scan settings and
per-creature/resource toggles live.

## Source

https://github.com/Hampus-Toft/ValheimRadar
