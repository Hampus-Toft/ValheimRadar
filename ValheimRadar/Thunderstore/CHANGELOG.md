# Changelog

Versions 1.9.0, 1.12.0 and 1.12.1 were never released on their own; their changes are listed
under 1.9.1 and 1.12.2.

## 1.15.0 - 2026-10-10

- **Removed:** the **Raise Player Marker** setting. It didn't reliably draw your marker above the
  pins and could cause map display problems when turned on. The map's layering is now always
  Valheim's own. Nothing to do: the old line in your config file is simply ignored.

## 1.14.0 - 2026-10-10

- **Added:** **Creature Cluster Distance** (General) - how close creatures must be to share a pin, set
  separately from resources. `ClusterDistance` now only affects resources and points of interest.
  Both default to 15 m, so nothing changes until you move a slider.
- **Added:** fish show their quality under the pin (`Q1`-`Q5`, 5 being the rarest), and fish of
  different quality get separate pins. New **Fish Quality 1-5** toggles in the Fish section hide
  each quality level separately.
- **Added:** left-click a resource or point-of-interest pin on the large map to cross it out (an X,
  like checking off your own pins) - e.g. a dungeon you've looted. Left-click again to clear it.
  Remembered per world; turn it off with **Enable Pin Cross-Out**.
- **Added:** Mountain Caves with a Tetra pond get their own pin and toggle, **Mountain Cave (Tetra
  Pond)**. A cave is recognised once you've been near it (its interior has to load), and existing
  cave pins switch over automatically.
- **Fixed:** Hildir's quest dungeons are now pinned as dungeon entrances: the **Smouldering Tomb**
  (Black Forest), **Sealed Tower** (Plains) and **Howling Cavern** (Mountains), each with its own
  toggle. They appear as soon as their area is generated, like other dungeons.
- **Fixed:** **Bear Cave** entrances (Black Forest) are pinned again, with their own toggle.

Existing settings and saved pins carry over.

## 1.13.0 - 2026-10-04

- **Added:** a **Tamed or Wild** setting for every tameable creature - show both (default, same as
  before), only wild ones, or only tamed ones (summoned spirit-caller animals count as tamed). E.g.
  pin only wild Wolves to hunt, or only your own Lox.
- **Added:** Hen, Asksvin and Moose now have their own toggles, star filter and tamed/wild filter
  (they were only covered by "Passive Animals (Unlisted)" before), in the Mistlands section and the
  new `09c - Creatures (Ashlands)` and `09d - Creatures (Deep North)` sections. Chickens, Asksvin
  hatchlings and Moose calves count as their species.

## 1.12.5 - 2026-10-04

- **Fixed:** crops you plant with the Cultivator (carrots, turnips, onions and their seed crops,
  barley, flax, magecap) are no longer pinned - only wild ones are, including the barley and flax
  in Fuling villages. Pins already saved for planted crops are removed automatically the next time
  the area around them is rescanned.

## 1.12.4 - 2026-10-04

- **Fixed:** fish are pinned on the map again. They had stopped showing up in 1.10.0, when loose
  item drops (ore, ingots) were excluded from scanning - fish count as item drops in Valheim, so
  they were excluded too.
- **Fixed:** config sections are now listed in order in `HampusToft.ValheimRadar.cfg` (section
  10 used to come before section 2). Single-digit section numbers now have a leading zero
  (`01 - General`, `02 - Master Groups`, ...). Your existing settings are carried over to the
  renamed sections automatically - nothing to redo.

## 1.12.3 - 2026-10-04

- **Changed:** the config file is now `HampusToft.ValheimRadar.cfg` (was
  `com.yourname.valheimradar.cfg`). Your old settings are copied over automatically.
- **Fixed:** the mod is now accepted by mod hosts that require debug symbols for native
  libraries (e.g. Hexium) - the bundled SQLite library ships with its PDB.
- **Changed:** rewritten mod page README.

## 1.12.2 - 2026-10-03

- **Changed:** pin data is saved in one SQLite database per world (`PinData/<world>.db`) instead
  of several text files. Saving no longer rewrites everything every 30 seconds - changes are
  written in small batches every 2 seconds and on disconnect. Your existing pins are imported
  once automatically and the old text files removed.
- **Fixed:** joining a well-explored world no longer freezes for many seconds (and no longer gets
  you kicked by a dedicated server's 30 second timeout). Saved pins now stream in gradually,
  nearest to you first.
- **Fixed:** the map no longer stutters with many pins - only pins in the part of the map you're
  looking at (plus a margin) are drawn, and they're added/removed as you scroll and zoom.
- If the SQLite library can't load, pins still work but nothing is saved for that session.

## 1.11.0 - 2026-10-03

- **Added:** picked berries, mushrooms, flowers and crops are hidden from the map until they grow
  back, then reappear on their own. Works for picks by any player. New setting
  **Hide Picked Until Respawn** (on by default).
- **Changed:** berry/mushroom/crop pins whose icon already shows what they are drop the name and
  keep only the count ("5x").
- **Fixed:** picked berries/mushrooms sometimes stayed pinned, or a neighbouring bush got hidden
  instead (notably with area-harvest mods).

## 1.10.0 - 2026-09-26

- **Added:** ground you've already explored is re-scanned periodically, catching resources missed
  the first time. New setting **ResourceRescanInterval** (default 30 s, 0 = never).
- **Added:** ore deposits, scrap piles, flint, stones, branches and Guck Sacks you mine or pick are
  removed from the map right away; ones other players cleared disappear after rescans confirm
  they're gone. New setting **Remove Depleted Resources** (on by default).
- **Changed:** ore deposits get their own pin each (no clustering), labelled by icon or just the
  ore name ("Silver").
- **Removed:** loose item drops (raw ore, ingots, dropped scrap) are no longer pinned; existing
  pins for them are cleaned up.
- **Fixed:** ores missing in some areas, especially on mountains and at the edge of the scan
  radius.

## 1.9.2 - 2026-09-24

- **Changed:** routine pin logging is silent unless **Diagnostic Logging** is on (now off by
  default).
- **Changed:** **Raise Player Marker** is now off by default, and no longer changes the map's
  canvas sorting (could conflict with other map mods).

## 1.9.1 - 2026-09-21

- **Added:** right-click a resource or location pin on the large map to dismiss it. Dismissed pins
  stay hidden for that world. New settings **Enable Pin Removal** and **Restore Dismissed Pins**.
- **Added:** **Raise Player Marker** - draws your own and your ship's map marker above all pins.
- **Added:** **Show Creature Names** (off by default) - creature pins with their own icon now show
  only count and stars unless this is on.
- **Added:** **Diagnostic Logging** for troubleshooting missing names/Locations.
- **Fixed:** saved pins far from your login spot were lost on every reconnect to a dedicated server.
- **Fixed:** Swamp muddy scrap piles (iron) were never pinned.
- **Fixed:** missing icons for Oozer, Writhan, piglets and other baby animals; Writhan now has its
  own toggle.
- **Fixed:** Location pins missing on dedicated servers due to an error building the Location
  lookup.

## 1.8.0 - 2026-09-18

- **Changed:** scan areas now line up exactly with Valheim's own 64 m zones, so newly explored
  ground on dedicated servers is scanned as soon as its zone has loaded, instead of waiting on up
  to four zones.

## 1.7.7 - 2026-09-18

- **Fixed:** ores, berries and other pickables were only pinned around your login/respawn point on
  dedicated servers - ground explored afterwards could be recorded as empty before its objects had
  loaded.

## 1.7.6 - 2026-09-17

- **Fixed:** on dedicated servers, most ruins and structures (villages, stone towers, ...) were
  still missing. Clients now discover every Location they come near, same as in single player.

## 1.7.5 - 2026-09-15

- **Fixed:** Location pins (dungeons, altars, ruins, ...) never appeared when playing on a
  dedicated server.

## 1.7.4 - 2026-09-14

- **Fixed:** resources and points of interest were missed while moving fast (flying, sailing,
  sprinting). Every newly in-range area is now scanned immediately.
- **Removed:** the **PermanentScanCellsPerTick** setting (no longer needed).

## 1.7.3 - 2026-09-14

- **Fixed:** Guck Sack pins now use the Guck icon.
- **Added:** Surtling as its own creature with a toggle and its trophy icon.

## 1.7.2 - 2026-09-14

- **Changed:** Guck Sack moved under the Ores group.
- **Fixed:** missing Obsidian icon.

## 1.7.1 - 2026-09-14

- **Changed:** clearer config groups: "Points of Interest" is now "Spawners & Landmarks"; the
  separate "Ruins & Locations" toggle is gone (its entries moved into the matching Location
  groups); the dungeon fallback moved into Dungeon Entrances; "Traders" is now "Bog Witch Camp".
- **Changed:** the Start Temple and Black Forest Trader are always shown.
- **Added:** chests split into **Chests (Above-Ground)** and **Buried Chests**.
- **Removed:** minimum-star filters for every creature except Boar, Wolf and Lox (star level is
  still shown on all creature pins).

## 1.7.0 - 2026-09-14

- **Changed:** creatures, resources and points of interest are scanned separately. Resources and
  points of interest are scanned once per area instead of every tick, which cuts the per-tick cost
  at large scan radii. **ScanBatchCount** now applies to creatures only; new
  **PermanentScanCellsPerTick** setting.

## 1.6.0 - 2026-09-13

- **Added:** pins for world Locations - boss altars, dungeon entrances, runestones, ruins and
  structures, the Start Temple and the Trader - about 50 toggleable types in five new groups,
  discovered from Valheim's world generation instead of a physics scan (most were never found
  before).

## 1.5.4 - 2026-09-13

- **Changed:** exact prefab-name matching for everything tracked, fixing misclassified objects
  (e.g. an iron fire pit pinned as iron).
- **Added:** all bosses and 12 fish species with their own toggles and icons, the Leviathan,
  Obsidian deposits, and a Points of Interest group (Greydwarf Nests, Body/Bone Piles, Guck Sacks,
  Drake Nests, Mistlands structures, traders).
- **Fixed:** Bear, Drake and Fuling were never recognized; Troll Cave and Bear Cave entrances were
  never pinned; missing icons for fish, Greyling and ore deposits.
- **Removed:** portal tracking, and pins for player-built chests and planted crops.

## 1.5.3 - 2026-09-13

- **Fixed:** objects above or below you (hills, valleys) were missed by the scan.
- **Changed:** debris fragments and objects inside dungeon interiors are no longer pinned.

## 1.5.2 - 2026-09-12

- **Fixed:** scanning got slower the more of the world you explored - resource pins are now
  clustered incrementally.

## 1.5.1 - 2026-09-12

- **Added:** **ScanBatchCount** - spreads each scan over several ticks to avoid lag spikes at a
  large scan radius.

## 1.5.0 - 2026-09-12

- **Changed:** now requires Jotunn. Built-in icons come straight from Valheim's icon atlas, fixing
  wrong icons (e.g. Bear, Fuling, Moder).
- **Fixed:** duplicate resource pins stacking up at the same spot ("Dandelion", "2x Dandelion",
  ...) and after relogging; beehives miscategorized as chests; some pins showing no icon after
  connecting.
- **Fixed:** the scan stopping entirely when Jotunn's icon loading fails on some mod lists.
- **Added:** first Thunderstore package.

## 1.4.0 - 2026-09-12

- **Added:** resource and structure pins are saved per world and survive relogging; toggling a
  category off and on no longer loses its pins.
- **Added:** creatures and resources get their own Valheim icon (trophy or item) by default.
- **Added:** per-creature toggles and star filters.
- **Changed:** custom icon PNGs now go in a `ValheimRadar` folder (was `MoreMapPins`).
- **Fixed:** pins from a previous world lingering after disconnecting.

## 1.3.0 - 2026-09-11

- **Changed:** internal restructure, no change in behaviour.

## 1.2.0 - 2026-09-11

- **Added:** per-object custom icons (a PNG named after the object, falling back to one per
  category).
- **Added:** Ruins & World group (abandoned farms and ruins, stone rings, runestones, tar pits);
  structures split into Functional and Ruins & World. Config sections are numbered.

## 1.1.0 - 2026-09-11

- **Added:** creature star levels shown on pins, with minimum-star filters for monsters and
  animals.

## 1.0.0 - 2026-09-10

- Initial version: scans around the player and pins creatures, berries, mushrooms, plants and
  crops, ground pickables, ores and structures on the minimap, grouping nearby ones into one pin.
  Configurable scan radius, interval and cluster distance, with a toggle per group and per type.
  Custom PNG pin icons.
