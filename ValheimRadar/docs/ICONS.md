# Pin Icons

How ValheimRadar picks a minimap icon for each tracked creature, resource, and
structure, and how to override it.

## Resolution order

For every trackable object, `PinManager.ResolvePerObjectPin` (in
`Pinning/PinManager.cs`) resolves an icon in this order, stopping at the first
one that produces a result:

1. **Specific PNG override** - `<BepInEx config>/ValheimRadar/<key>.png`, where
   `<key>` is the object's cleaned prefab name (see [Naming key](#naming-key)
   below). Lets you give a single species/structure its own icon, e.g.
   `wolf.png` vs `boar.png`, or `crypt.png`.
2. **Category PNG override** - `<BepInEx config>/ValheimRadar/<category>.png`,
   e.g. `monster.png`, `animal.png`, `berry.png`, `dungeon.png`. Used when no
   type-specific PNG exists; applies to every object in that category.
3. **Vanilla game icon** - if neither PNG exists, the mod pulls an icon
   Valheim already ships: a creature's Trophy item icon (Wolf ->
   `TrophyWolf`'s icon), or a resource's pickup item icon (a Raspberry bush ->
   the `Raspberry` item icon). See `Pinning/VanillaIconResolver.cs`. This is
   what makes species/resources look distinct out of the box, with no PNGs
   supplied at all.
4. **Built-in fallback** - `Minimap.PinType.Icon3`, if nothing above resolved.

Structures (chests, portals, dungeons/crypts, beehives, stone rings, ruins,
runestones, tar pits) don't have a vanilla pickup icon to borrow, so step 3 is
a no-op for them today - they rely on steps 1/2/4. Drop a PNG for these if you
want them visually distinct from their category default.

## Naming key

The "key" used for both the specific-PNG lookup and the vanilla-icon lookup is
the object's raw Unity prefab name, with the `(Clone)` suffix and any of the
prefixes `pickable_`, `item_`, `piece_`, `vfx_`, `sfx_` stripped, then
lowercased (`ObjectEvaluator.StripKnownPrefixes`). For most creatures this is
just their prefab name as-is: `Wolf` -> `wolf`, `Boar` -> `boar`, `Greydwarf`
-> `greydwarf`.

If you're not sure what key a given creature/object uses, the simplest way to
find out is to try a PNG named after its obvious in-game name first (e.g.
`wolf.png`) - Unity/Valheim prefab names usually match the display name
closely.

## Adding a custom icon

1. Find (or create) `<BepInEx config>/ValheimRadar/` next to your config file
   (`BepInEx/config/ValheimRadar/`).
2. Drop in a PNG named either:
   - `<key>.png` for one specific type (e.g. `wolf.png`), or
   - `<category>.png` to reskin a whole category (e.g. `monster.png`).
3. No restart needed - the file is picked up (and cached) the next scan tick
   after it appears on disk.

Icons registered this way share Valheim's own `Minimap.m_icons` sprite list;
they're invalidated and re-registered automatically whenever `Minimap.instance`
changes (world reload/reconnect), so stale icons never leak across sessions.

## Vanilla icon mapping

`VanillaIconResolver` maps:

- **Creatures**: cleaned prefab key -> Trophy item prefab name, via an
  explicit override table for the ones that don't follow the
  `Trophy<Name>` convention (e.g. `troll` -> `TrophyForestTroll`, `ghost` ->
  `TrophyWraith`, `dragon` -> `TrophyModer`), and a `"Trophy" + Capitalize(key)`
  guess for anything else.
- **Resources**: an explicit vanilla item prefab name attached to each
  `ObjectEvaluator.ResourceRule` (e.g. raspberries -> `Raspberry`, flint ->
  `Flint`, copper -> `CopperOre`).

These mappings are **best-effort** - built from established Valheim modding
conventions, not verified against a live game instance. A wrong or missing
entry is harmless: `ObjectDB.GetItemPrefab` just returns `null` and resolution
falls through to the next tier (category PNG / Icon3), so nothing crashes or
breaks. If a specific creature/resource isn't showing the icon you'd expect,
that mapping is the first place to check and correct.

## Why this doesn't depend on another pin mod

`RadarPlugin` declares `KGvalheim.MoreMapPins` and `Arielle.MoreMapPins` as
soft BepInEx dependencies (see `AGENTS.md` Playbook 3), but nothing in this
codebase actually calls into either mod's API. A `[BepInDependency(...,
SoftDependency)]` attribute only affects BepInEx's plugin load order and
prevents a hard crash if the named mod is missing - it does **not** give
access to that mod's types, data, or icons. Actually consuming another mod's
icons would require an assembly reference to its DLL and code written against
its specific API/schema.

ValheimRadar doesn't need that: `CustomPinLoader` registers icons directly
into Valheim's own `Minimap.m_icons` list via Unity's `Sprite`/`Texture2D`
APIs, and `VanillaIconResolver` pulls existing icons straight from the game's
`ObjectDB`. No external pin-icon mod is required for any of this to work.
