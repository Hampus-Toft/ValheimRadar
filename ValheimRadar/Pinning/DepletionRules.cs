using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Pure decision logic for removing pins of depletable resources (ore deposits, scrap piles,
    // flint/stone/branches, Guck Sacks - see ResourceRule.Depletable), kept free of Physics/Minimap
    // so it can be unit tested. Two ways a point goes:
    //  - the local player mines/picks it (Pinning/DepletionPatches.cs -> PinManager.MarkDepletedAt),
    //    matched to the recorded point by position within HitMatchRadius;
    //  - a rescan of a verified cell (see ScannedCell) no longer finds it - which is also how
    //    resources other players cleared get removed. Absence is only trusted after
    //    MissesBeforeRemoval consecutive misses spanning at least MinMissSpanSeconds, so a single
    //    scan that ran while the server was still streaming the zone's objects can't wipe pins.
    internal static class DepletionRules
    {
        // Same object = same root position; this only absorbs float round-tripping through the save
        // file (same tolerance DismissedPinStore uses).
        internal const float PresenceMatchRadius = 0.5f;

        // A hit/picked object's root vs a recorded point. Slightly looser than PresenceMatchRadius so
        // the "_frac" remains a Destructible deposit is swapped for on its first hit (same transform)
        // still resolve to the deposit's point.
        internal const float HitMatchRadius = 1f;

        internal const int MissesBeforeRemoval = 2;
        internal const float MinMissSpanSeconds = 10f;

        // Points this close to the top/bottom of a cell's queried range aren't judged - a collider
        // hanging just outside the box could have been missed without the object being gone.
        internal const float VerticalMargin = 5f;

        internal static bool IsWithinVerifiedHeight(float y, float minY, float maxY) =>
            y >= minY + VerticalMargin && y <= maxY - VerticalMargin;

        // True when this scan found an object of the same category at the recorded point's position.
        internal static bool IsPresent(string categoryKey, Vector3 position, IList<TrackedItem> found)
        {
            foreach (TrackedItem item in found)
            {
                if (item.CategoryKey == categoryKey && DistanceXZ(item.Position, position) <= PresenceMatchRadius) return true;
            }

            return false;
        }

        // Updates a recorded point's miss streak with one verified observation.
        internal static void RecordObservation(TrackedItem point, bool present, float now)
        {
            if (present)
            {
                point.MissedScans = 0;
                return;
            }

            if (point.MissedScans == 0) point.FirstMissTime = now;
            point.MissedScans++;
        }

        internal static bool ShouldRemove(TrackedItem point, float now) =>
            point.MissedScans >= MissesBeforeRemoval && now - point.FirstMissTime >= MinMissSpanSeconds;

        internal static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
