using System.Globalization;
using UnityEngine;

namespace ValheimRadar
{
    // Pure decision logic behind PinManager's raw persistent point store (see
    // PinManager.rawPersistentPoints), kept free of ZDOID/Minimap/ZDOMan so it can be unit tested.
    //
    // The identity model this encodes: a persisted point's ZDOID is only a *hint*, never proof of
    // identity. Confirmed by decompiling assembly_valheim.dll (ZDO.Load): every ZDO loaded from a
    // world save gets a fresh sequential ID (`m_uid.SetID(++ZDOID.m_loadID)`) each time the world
    // is loaded, so the same physical ore/tree can carry a different ZDOID after a server restart -
    // and an unrelated object can end up carrying a saved point's old ZDOID. Position (plus the
    // DisplayName) is the only identity that is genuinely stable across sessions for stationary
    // world objects.
    //
    // Just as important, a saved point's ZDO is NOT expected to be resolvable on a client when the
    // save is loaded: on a dedicated-server client ZDOMan only holds the ZDOs of sectors the server
    // has streamed so far, i.e. essentially just the area around the player right after connecting.
    // Dropping persisted points whose ZDO can't be found (which LoadWorldPins used to do) therefore
    // discarded every point outside that area on each reconnect and - because it also marked the
    // store dirty - permanently rewrote the save file without them (issue #42).
    internal static class PersistedPointRules
    {
        // A point rediscovered within this radius of an existing point of the same DisplayName is the
        // same physical object, whatever its ZDOID says. Deliberately far smaller than
        // ClusterDistance (which merges genuinely distinct nearby objects into one pin) - it only
        // exists to catch the same object being seen twice.
        internal const float DuplicatePointRadius = 0.25f;

        // True when two positions are close enough to be the same physical object.
        internal static bool IsSamePosition(Vector3 a, Vector3 b) =>
            Vector3.Distance(a, b) <= DuplicatePointRadius;

        // True when a candidate point is the same physical object as an already-recorded one,
        // judged by name + position only (ZDOID deliberately not consulted - see class comment).
        internal static bool IsDuplicate(string existingDisplayName, Vector3 existingPosition, string candidateDisplayName, Vector3 candidatePosition) =>
            existingDisplayName == candidateDisplayName && IsSamePosition(existingPosition, candidatePosition);

        // What to do with a candidate point whose ZDOID-derived key may already be in the store.
        internal enum KeyResolution
        {
            // Key is free - store the candidate under it as-is.
            UseKey,
            // Key is taken by the very same physical object - the candidate is already known.
            AlreadyKnown,
            // Key is taken by a DIFFERENT physical object (ZDOIDs are re-assigned when a server
            // reloads its world, so an old ID can now belong to something else) - the candidate is
            // a genuinely new point and must be stored under a disambiguated key, not skipped.
            KeyCollision,
        }

        internal static KeyResolution ResolveKey(bool keyTaken, Vector3 existingPositionAtKey, Vector3 candidatePosition)
        {
            if (!keyTaken) return KeyResolution.UseKey;
            return IsSamePosition(existingPositionAtKey, candidatePosition) ? KeyResolution.AlreadyKnown : KeyResolution.KeyCollision;
        }

        // Store key for a point whose ZDOID-derived key collided with a different object's. Derived
        // from the position so it is deterministic (the same object re-recorded lands on the same
        // key) and can't itself collide with a plain "<userId>:<id>" key.
        internal static string DisambiguateKey(string baseKey, Vector3 position) =>
            string.Format(CultureInfo.InvariantCulture, "{0}@{1:F1},{2:F1},{3:F1}", baseKey, position.x, position.y, position.z);
    }
}
