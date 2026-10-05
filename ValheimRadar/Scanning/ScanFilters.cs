using UnityEngine;

namespace ValheimRadar
{
    // Pre-filter shared by every type-specific scanner (CreatureScanner/ResourceScanner/PoiScanner)
    // - object classes that should never be pinned regardless of which of the three types they'd
    // otherwise resemble. Applied once, before dispatching to a type's own evaluator, so none of
    // CreatureEvaluator/ResourceEvaluator/PoiEvaluator need to duplicate these checks.
    internal static class ScanFilters
    {
        // Destruction-fragment/debris pieces (e.g. a boss arena's stone pillars shattering apart on
        // death) are never legitimate resources, POI, or creatures in their own right. Rejected
        // purely by name, before any other check - Valheim's shatter system generates these
        // dynamically, so there's no fixed list to match instead.
        private static readonly string[] DebrisNameMarkers = { "_frac", "debris", "rubble", "fragment", "_piece", "_chip" };

        // Dungeon/cave interiors (crypts, sunken crypts, dvergr forts, mountain caves, ...) are
        // generated at a large, fixed vertical offset from the real terrain at their entrance's X/Z -
        // "high in the sky" or "underground" relative to the actual ground - so their objects have no
        // sensible position on the 2D minimap and just show up confusingly stacked on whatever is
        // really at that spot on the surface. Generous enough that real terrain variance (cliffs,
        // mountain peaks) directly above/below a point is never mistaken for a dungeon interior -
        // genuine dungeon-generation offsets are far larger than that.
        private const float DungeonHeightOffsetThreshold = 40f;

        internal static bool ShouldReject(GameObject go, string nameLower)
        {
            if (IsDebrisName(nameLower)) return true;

            // Loose item drops (ore a player dropped, a smelter's ingots, scrap from a mined pile,
            // ...) are never tracked - only world resource nodes are. Checked structurally rather
            // than by name so no alias can ever collide with an item prefab of the same name.
            // Live fish are the exception: every Fish prefab also carries an ItemDrop (it's what
            // the player picks up once caught), so this check alone silently dropped all fish.
            if (go.GetComponent<ItemDrop>() != null && go.GetComponent<Fish>() == null) return true;

            if (IsInsideDungeonInterior(go.transform.position)) return true;

            return false;
        }

        // Player farms: every Cultivator crop can only be planted on cultivated ground, and wild ones
        // never spawn there (see ResourceEvaluator.SaplingGrownPrefabs). False when the terrain isn't loaded.
        internal static bool IsOnCultivatedGround(Vector3 position)
        {
            Heightmap heightmap = Heightmap.FindHeightmap(position);
            return heightmap != null && heightmap.IsCultivated(position);
        }

        // Name-only half of ShouldReject, split out so it can be unit tested without a live
        // GameObject/ZoneSystem.
        internal static bool IsDebrisName(string nameLower) => NameFormatting.ContainsAny(nameLower, DebrisNameMarkers);

        private static bool IsInsideDungeonInterior(Vector3 position)
        {
            if (ZoneSystem.instance == null) return false;
            if (!ZoneSystem.instance.GetGroundHeight(position, out float groundHeight)) return false;

            return Mathf.Abs(position.y - groundHeight) > DungeonHeightOffsetThreshold;
        }
    }
}
