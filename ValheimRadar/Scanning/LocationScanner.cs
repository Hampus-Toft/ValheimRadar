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
    // gate, just server-authoritative world-gen. The only Location data a pure client actually
    // receives is a server-pushed subset (locations flagged m_iconAlways, or m_iconPlaced once
    // placed) via the "LocationIcons" RPC, exposed client-side through the public
    // ZoneSystem.GetLocationIcons(Dictionary<Vector3,string>) method - smaller than the host's full
    // GetLocationList(), but it's what we fall back to so dedicated-server clients still get pins
    // for the Locations vanilla itself would show as map icons.
    public static class LocationScanner
    {
        // GetLocationList()'s backing dictionary only ever grows (zones are never un-generated), and
        // the client-side icon dictionary is re-broadcast in full each time a new iconPlaced Location
        // is placed nearby, so it grows too - either way, a call whose count hasn't changed since the
        // last one is guaranteed to have nothing new to offer - skip the full iteration on those ticks.
        private static int lastLocationDictCount = -1;

        // Reused across ticks on the client fallback path to avoid a per-scan allocation;
        // ZoneSystem.GetLocationIcons() only ever adds entries, so it must be cleared before each call.
        private static readonly Dictionary<Vector3, string> locationIconsBuffer = new Dictionary<Vector3, string>();

        // Called on world unload/disconnect (see RadarPlugin) so a stale count from a previous world
        // never suppresses a real scan of the next one.
        public static void Reset()
        {
            lastLocationDictCount = -1;
        }

        public static List<TrackedLocation> ScanLocations()
        {
            var results = new List<TrackedLocation>();

            if (ZoneSystem.instance == null) return results;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            return isServer ? ScanFromLocationList(results) : ScanFromLocationIcons(results);
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
                if (!RadarConfig.LocationPrefabLookup.TryGetValue(prefabLower, out var def)) continue;

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

            Debug.Log($"[ValheimRadar] location-scan matched={results.Count} totalKnown={allLocations.Count}");

            return results;
        }

        private static List<TrackedLocation> ScanFromLocationIcons(List<TrackedLocation> results)
        {
            locationIconsBuffer.Clear();
            ZoneSystem.instance.GetLocationIcons(locationIconsBuffer);

            if (locationIconsBuffer.Count == lastLocationDictCount) return results;
            lastLocationDictCount = locationIconsBuffer.Count;

            foreach (var icon in locationIconsBuffer)
            {
                string prefabLower = icon.Value?.ToLowerInvariant();
                if (string.IsNullOrEmpty(prefabLower)) continue;
                if (!RadarConfig.LocationPrefabLookup.TryGetValue(prefabLower, out var def)) continue;

                string locationKey = $"{def.CanonicalKey}_{Mathf.RoundToInt(icon.Key.x)}_{Mathf.RoundToInt(icon.Key.z)}";

                results.Add(new TrackedLocation
                {
                    LocationKey = locationKey,
                    Position = icon.Key,
                    RawName = prefabLower,
                    DisplayName = def.DisplayName,
                    CategoryKey = $"location:{def.CanonicalKey}"
                });
            }

            Debug.Log($"[ValheimRadar] location-icon-scan (client fallback) matched={results.Count} totalKnown={locationIconsBuffer.Count}");

            return results;
        }

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
