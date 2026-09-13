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
    public static class LocationScanner
    {
        // GetLocationList()'s backing dictionary only ever grows (zones are never un-generated), so
        // a call whose count hasn't changed since the last one is guaranteed to have nothing new to
        // offer - skip the full iteration entirely on those ticks.
        private static int lastLocationDictCount = -1;

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
