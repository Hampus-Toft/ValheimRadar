using UnityEngine;

namespace ValheimRadar
{
    // Represents one world Location discovered via ZoneSystem.instance.GetLocationList() (see
    // Scanning/LocationScanner.cs) - dungeons, ruins, boss altars, runestones, etc. Deliberately NOT
    // a TrackedItem: TrackedItem.Zdoid is a non-nullable ZDOID struct with no safe "no id" sentinel
    // (its default value collides with Valheim's own invalid-id sentinel), and Locations have no ZDO
    // of their own to begin with - only some of their child objects do, which is exactly why the old
    // physics-based scan missed most of them. LocationKey is derived deterministically from the
    // location's own seeded world-gen position instead, so it's stable across sessions without
    // needing any ZDO at all (see PinManager's location pin pipeline).
    public class TrackedLocation
    {
        public string LocationKey;
        public Vector3 Position;
        public string RawName;
        public string DisplayName;
        public string CategoryKey;
    }
}
