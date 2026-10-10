using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Discovers world "Location" content (dungeons, ruins, boss altars, runestones, the trader,
    // shipwrecks, etc.) directly from Valheim's own ZoneSystem, instead of relying on BatchScanner's
    // physics-based ZNetView sweep. Many Location prefab roots have no ZNetView of their own (only
    // some child objects do), which is why most of these never got pinned before this existed.
    //
    // ZoneSystem.instance.GetLocationList() is public, verified-stable API (confirmed by reflecting
    // against the installed game's assembly_valheim.dll) that returns every Location the game has
    // generated so far this session - the dictionary backing it only grows as unexplored zones
    // generate around the player, so polling it repeatedly naturally reveals more Locations over
    // time, matching the rest of the mod's "discover as you walk" feel.
    //
    // GetLocationList() is only ever populated by ZoneSystem.GenerateLocations(), which
    // ZoneSystem.Update() gates behind ZNet.instance.IsServer() (confirmed by decompiling
    // assembly_valheim.dll) - true for a listen-server/single-player host, but false for a normal
    // client connected to a dedicated server. Such a client's own ZoneSystem never generates
    // Locations locally, so GetLocationList() stays empty for it forever - not an admin/permission
    // gate, just server-authoritative world-gen.
    //
    // The client fallback below reads LocationProxy instead - a small ZNetView-backed marker object
    // that ZoneSystem.CreateLocationProxy() spawns (server-side, alongside every Location it ever
    // places) purely so its ZDO can sync the placement to nearby clients over the normal ZDO
    // relevancy system, same as any creature/resource ZDO. Each proxy's ZDO carries only a hashed
    // prefab-name int (ZDOVars.s_location, set via string.GetStableHashCode() - see
    // LocationProxy.SetLocation) and a seed, which is all a client needs to regenerate that
    // Location's own DungeonGenerator content locally and deterministically (the "Dungeon Generator"
    // log line visible client-side even against a dedicated server). We resolve that hash back to a
    // RadarConfig.LocationDefinition via a lookup built from ZoneSystem.m_locations - the *static*
    // catalog of every Location template the game knows about, loaded identically on every peer by
    // SetupLocations() regardless of IsServer(), so hashing is never needed on our side (letting
    // ZoneLocation.Hash do it avoids ever having to guess the real prefab name's exact casing).
    // This covers every placed Location a client has come near, not just the map-icon subset
    // ZoneSystem.GetLocationIcons() would give us, so it's used unconditionally as the client path.
    public static class LocationScanner
    {
        // GetLocationList()'s backing dictionary only ever grows (zones are never un-generated), so
        // a call whose count hasn't changed since the last one is guaranteed to have nothing new to
        // offer - skip the full iteration entirely on those ticks. Not used on the client fallback
        // path: FindObjectsByType<LocationProxy>() naturally fluctuates as zones load/unload, and
        // PinManager.RecordAndSyncLocations already merges idempotently, so re-resolving every tick
        // there is simpler and just as cheap.
        private static int lastLocationDictCount = -1;

        // Hashed-prefab-name -> definition, built once per world from ZoneSystem.m_locations (see
        // class remarks above). Rebuilt lazily since ZoneSystem.instance isn't available at Reset()
        // time (world unload/disconnect).
        private static Dictionary<int, LocationHashEntry> locationHashLookup;

        // Every Location template's prefab name by hash (mapped to a definition or not), and the unmapped
        // prefab names already reported this world - troubleshooting only, see LogUnmapped.
        private static Dictionary<int, string> locationPrefabByHash;
        private static readonly HashSet<string> loggedUnmappedPrefabs = new HashSet<string>();

        private readonly struct LocationHashEntry
        {
            public readonly RadarConfig.LocationDefinition Def;
            public readonly string PrefabLower;

            public LocationHashEntry(RadarConfig.LocationDefinition def, string prefabLower)
            {
                Def = def;
                PrefabLower = prefabLower;
            }
        }

        // Called on world unload/disconnect (see RadarPlugin) so a stale count/lookup from a
        // previous world never leaks into or suppresses a real scan of the next one.
        public static void Reset()
        {
            lastLocationDictCount = -1;
            locationHashLookup = null;
            locationPrefabByHash = null;
            loggedUnmappedPrefabs.Clear();
        }

        // Reports (once per prefab per world) a placed Location the radar has no LocationDefinition for, so a
        // missing dungeon/ruin variant shows up in the BepInEx log as "location-unmapped prefab=<name>"
        // instead of silently never getting a pin. Gated by RadarConfig.DiagnosticLogging.
        private static void LogUnmapped(string prefab)
        {
            if (RadarConfig.DiagnosticLogging != null && !RadarConfig.DiagnosticLogging.Value) return;
            if (string.IsNullOrEmpty(prefab) || !loggedUnmappedPrefabs.Add(prefab)) return;

            RadarLog.Diag($"[ValheimRadar] location-unmapped prefab={prefab}");
        }

        public static List<TrackedLocation> ScanLocations()
        {
            var results = new List<TrackedLocation>();

            if (ZoneSystem.instance == null) return results;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            return isServer ? ScanFromLocationList(results) : ScanFromLocationProxies(results);
        }

        private static List<TrackedLocation> ScanFromLocationList(List<TrackedLocation> results)
        {
            var allLocations = ZoneSystem.instance.GetLocationList();
            if (allLocations.Count == lastLocationDictCount) return results;
            lastLocationDictCount = allLocations.Count;

            foreach (var inst in allLocations)
            {
                if (!inst.m_placed || inst.m_location == null) continue;

                string prefabLower = inst.m_location.m_prefabName?.ToLowerInvariant();
                if (string.IsNullOrEmpty(prefabLower)) continue;
                if (!RadarConfig.LocationPrefabLookup.TryGetValue(prefabLower, out var def))
                {
                    LogUnmapped(prefabLower);
                    continue;
                }

                string locationKey = $"{def.CanonicalKey}_{Mathf.RoundToInt(inst.m_position.x)}_{Mathf.RoundToInt(inst.m_position.z)}";

                results.Add(new TrackedLocation
                {
                    LocationKey = locationKey,
                    Position = inst.m_position,
                    RawName = prefabLower,
                    DisplayName = def.DisplayName,
                    CategoryKey = $"location:{def.CanonicalKey}"
                });
            }

            RadarLog.Diag($"[ValheimRadar] location-scan matched={results.Count} totalKnown={allLocations.Count}");

            return results;
        }

        // ZoneLocation.Hash resolves m_prefab.Name, which throws KeyNotFoundException (empty AssetID) for
        // catalog entries with no valid prefab reference. ZoneSystem.SetupLocations() itself only touches
        // Hash for entries that are enabled or have a valid prefab, so such entries never get a hash in the
        // game's own lookup and no LocationProxy can point at them - skip them. Left unguarded, the throw
        // escaped every scan tick and the lookup was never built.
        private static bool TryGetLocationHash(ZoneSystem.ZoneLocation loc, out int hash)
        {
            try
            {
                hash = loc.Hash;
                return true;
            }
            catch (System.Exception)
            {
                hash = 0;
                return false;
            }
        }

        private static Dictionary<int, LocationHashEntry> GetOrBuildHashLookup()
        {
            if (locationHashLookup != null) return locationHashLookup;

            var lookup = new Dictionary<int, LocationHashEntry>();
            var allNames = new Dictionary<int, string>();
            foreach (var loc in ZoneSystem.instance.m_locations)
            {
                string prefabLower = loc.m_prefabName?.ToLowerInvariant();
                if (string.IsNullOrEmpty(prefabLower)) continue;

                if (!TryGetLocationHash(loc, out int hash)) continue;

                allNames[hash] = prefabLower;
                if (!RadarConfig.LocationPrefabLookup.TryGetValue(prefabLower, out var def)) continue;

                lookup[hash] = new LocationHashEntry(def, prefabLower);
            }

            locationPrefabByHash = allNames;
            locationHashLookup = lookup;
            return lookup;
        }

        private static List<TrackedLocation> ScanFromLocationProxies(List<TrackedLocation> results)
        {
            var hashLookup = GetOrBuildHashLookup();

            foreach (var proxy in Object.FindObjectsByType<LocationProxy>(FindObjectsSortMode.None))
            {
                var netView = proxy.GetComponent<ZNetView>();
                if (netView == null || !netView.IsValid() || netView.GetZDO() == null) continue;

                int locationHash = netView.GetZDO().GetInt(ZDOVars.s_location, 0);
                if (locationHash == 0) continue;
                if (!hashLookup.TryGetValue(locationHash, out var entry))
                {
                    LogUnmapped(locationPrefabByHash != null && locationPrefabByHash.TryGetValue(locationHash, out string unmappedName) ? unmappedName : $"hash:{locationHash}");
                    continue;
                }

                Vector3 pos = proxy.transform.position;
                string locationKey = $"{entry.Def.CanonicalKey}_{Mathf.RoundToInt(pos.x)}_{Mathf.RoundToInt(pos.z)}";

                results.Add(new TrackedLocation
                {
                    LocationKey = locationKey,
                    Position = pos,
                    RawName = entry.PrefabLower,
                    DisplayName = entry.Def.DisplayName,
                    CategoryKey = $"location:{entry.Def.CanonicalKey}"
                });
            }

            RadarLog.Diag($"[ValheimRadar] location-proxy-scan (client fallback) matched={results.Count}");

            return results;
        }

        // Mountain Caves come in one Location prefab (MountainCave02); the rare ones with a Tetra pond
        // differ only in which rooms their DungeonGenerator placed. Those rooms are instantiated as the
        // generator's children (named after the room prefab - see DungeonGenerator.PlaceRoom) on every
        // peer once the cave's zone is loaded, even before anyone goes inside, so this works the same
        // on a host and on a dedicated-server client. The interior sits in the same zone as its
        // entrance (5000 m up), so the caller matches each returned position to a cave by scan cell.
        // Returns the positions of every loaded dungeon whose rooms include a Tetra pond.
        public static List<Vector3> FindTetraPondDungeons()
        {
            var results = new List<Vector3>();

            foreach (var generator in Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None))
            {
                foreach (Room room in generator.GetComponentsInChildren<Room>(includeInactive: true))
                {
                    if (!IsTetraPondRoom(Utils.GetPrefabName(room.gameObject))) continue;

                    results.Add(generator.transform.position);
                    break;
                }
            }

            return results;
        }

        // The cave rooms holding a Tetra pond: "cave_new_deeproom_bottom_lake" and "cave_dome_bottom_lake".
        internal static bool IsTetraPondRoom(string roomName) =>
            !string.IsNullOrEmpty(roomName) &&
            roomName.StartsWith("cave_", System.StringComparison.OrdinalIgnoreCase) &&
            roomName.EndsWith("_lake", System.StringComparison.OrdinalIgnoreCase);

        // Delegation targets for ObjectEvaluator's categoryKey-resolution methods (see
        // IsCategoryEnabled/GetDefaultIconForCategory/GetVanillaIconForCategory) - resolves a
        // "location:{key}" categoryKey back to enabled-state/icon without a live GameObject, exactly
        // like the existing "resource:"/"creature:" branches do.
        public static bool IsLocationCategoryEnabled(string categoryKey)
        {
            var def = ResolveDefinition(categoryKey);
            if (def == null) return false;

            return RadarConfig.IsLocationGroupEnabled(def.Group) &&
                   RadarConfig.Locations.TryGetValue(def.CanonicalKey, out var entry) && entry.Enabled.Value;
        }

        public static string GetLocationIconPng(string categoryKey) => ResolveDefinition(categoryKey)?.IconPng;

        public static string GetLocationVanillaIcon(string categoryKey) => ResolveDefinition(categoryKey)?.VanillaIcon;

        private static RadarConfig.LocationDefinition ResolveDefinition(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey) || !categoryKey.StartsWith("location:")) return null;

            string canonicalKey = categoryKey.Substring("location:".Length);
            return RadarConfig.CanonicalLocationLookup.TryGetValue(canonicalKey, out var def) ? def : null;
        }
    }
}
