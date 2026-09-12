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
   Valheim already ships, via [Jotunn](https://valheim-modding.github.io/Jotunn/)'s
   `GUIManager.GetSprite(name)`: a creature's Trophy item icon (Wolf ->
   `TrophyWolf`, Bear -> `TrophyBjorn`), or a resource's pickup item icon
   (Raspberry bush -> `raspberry`, Copper deposit -> `copperore`). See
   `Pinning/VanillaIconResolver.cs`. Every name used there is an exact,
   verified sprite name from Valheim's own icon atlas (cross-checked against
   Jotunn's generated
   [sprite list](https://valheim-modding.github.io/Jotunn/data/gui/sprite-list.html)),
   not a guessed naming convention - this is what makes species/resources
   look distinct *and correct* out of the box, with no PNGs supplied at all.
4. **Built-in fallback** - the vanilla `Minimap.PinType.Icon3` pin's own
   default sprite, if nothing above resolved.

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

Icons loaded this way are plain Unity `Sprite`s assigned directly to each
pin's `Minimap.PinData.m_icon` - unlike the old approach, there's no shared
`Minimap.m_icons` list to register into or invalidate, so nothing can collide
with icons another mod registers, and nothing needs to be re-registered across
world reloads/reconnects.

## Vanilla icon mapping

`VanillaIconResolver` maps:

- **Creatures**: cleaned prefab key -> exact vanilla Trophy sprite name (e.g.
  `troll` -> `TrophyForestTroll`, `bear` -> `TrophyBjorn`, `fuling` ->
  `TrophyGoblin`), via an explicit table.
- **Resources**: an explicit vanilla sprite name attached to each
  `ObjectEvaluator.ResourceRule` (e.g. raspberries -> `raspberry`, flint ->
  `flint`, copper -> `copperore`, tin -> `TinOre`).

Every name in these tables was verified against Jotunn's generated
[sprite list](https://valheim-modding.github.io/Jotunn/data/gui/sprite-list.html)
(itself generated directly from live game data), not guessed from a naming
convention - guessing is what previously produced wrong icons silently (e.g.
`"Trophy" + Capitalize("bear")` guesses `TrophyBear`, which doesn't exist -
the real sprite is `TrophyBjorn`). A wrong or missing entry is still harmless:
`GUIManager.GetSprite` just returns `null` and resolution falls through to the
next tier (category PNG / built-in fallback). If a specific creature/resource
isn't showing the icon you'd expect, this table is the first place to check
and correct - and the sprite list linked above is the source of truth, not
memory or convention.

`VanillaIconResolver.TryResolveIcon` also guards the `GetSprite` call itself:
on some client/mod-list combinations, Jotunn's own lazy `AssetManager` init
(triggered by the first `GetSprite` call) can throw internally (a Harmony
transpiler failure inside Jotunn, unrelated to this codebase). That's caught
and latched so vanilla icon lookups are disabled for the rest of the session
instead of taking the whole scan loop down with them - pins still get placed,
just with category-default/user-PNG icons instead of vanilla ones.

## Dependency on Jotunn

ValheimRadar has a **hard** dependency on
[Jotunn, the Valheim Library](https://valheim-modding.github.io/Jotunn/)
(`[BepInDependency(Jotunn.Main.ModGuid)]` in `RadarPlugin.cs` - install it via
Thunderstore/r2modman like any other plugin, or the game refuses to load
ValheimRadar). Jotunn is the standard developer-facing modding library for
Valheim and ships with the full game icon atlas already extracted and
addressable by name:

- `Pinning/IconLoader.cs` uses `Jotunn.Utils.AssetUtils.LoadTexture` to turn a
  user-supplied PNG into a `Sprite` (tiers 1-2 above).
- `Pinning/VanillaIconResolver.cs` uses `Jotunn.Managers.GUIManager.GetSprite`
  to pull an existing vanilla icon out of that atlas by exact name (tier 3).

Both return a plain `Sprite` that's set directly on `Minimap.PinData.m_icon` -
no index bookkeeping into a shared icon list, so no icon-mapping drift across
sessions or between mods.
