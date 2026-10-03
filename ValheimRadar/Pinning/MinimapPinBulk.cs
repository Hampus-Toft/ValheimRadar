using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimRadar
{
    // Removes many minimap pins at once. Minimap.RemovePin is a linear List.Remove over every pin on the
    // map, so unloading thousands of pins one by one (closing the large map, a ClusterDistance rebuild,
    // disconnecting) is quadratic. This does what RemovePin does - destroy the pin's UI marker, drop it
    // from m_pins, request a pin refresh - in one pass over the list. Falls back to plain RemovePin
    // calls if the private members can't be reached (e.g. a game update renamed them).
    internal static class MinimapPinBulk
    {
        // Below this, plain RemovePin calls are cheap enough.
        private const int BulkThreshold = 32;

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> pinsRef = TryFieldRef<List<Minimap.PinData>>("m_pins");
        private static readonly AccessTools.FieldRef<Minimap, bool> pinUpdateRequiredRef = TryFieldRef<bool>("m_pinUpdateRequired");
        private static readonly Action<Minimap, Minimap.PinData> destroyPinMarker = TryDestroyPinMarker();

        private static bool bulkDisabled = pinsRef == null || pinUpdateRequiredRef == null || destroyPinMarker == null;

        internal static void RemovePins(Minimap minimap, List<Minimap.PinData> pins)
        {
            if (minimap == null || pins.Count == 0) return;

            if (!bulkDisabled && pins.Count >= BulkThreshold)
            {
                try
                {
                    foreach (Minimap.PinData pin in pins) destroyPinMarker(minimap, pin);

                    var remove = new HashSet<Minimap.PinData>(pins);
                    pinsRef(minimap).RemoveAll(remove.Contains);
                    pinUpdateRequiredRef(minimap) = true;
                    return;
                }
                catch (Exception ex)
                {
                    // RemovePin on a pin that's already gone is harmless, so the fallback below can
                    // safely redo whatever part of this got done.
                    bulkDisabled = true;
                    Debug.LogWarning($"[ValheimRadar] Bulk pin removal failed, using Minimap.RemovePin instead: {ex.Message}");
                }
            }

            foreach (Minimap.PinData pin in pins) minimap.RemovePin(pin);
        }

        private static AccessTools.FieldRef<Minimap, T> TryFieldRef<T>(string field)
        {
            try
            {
                return AccessTools.FieldRefAccess<Minimap, T>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Action<Minimap, Minimap.PinData> TryDestroyPinMarker()
        {
            try
            {
                var method = AccessTools.Method(typeof(Minimap), "DestroyPinMarker", new[] { typeof(Minimap.PinData) });
                return method == null ? null : AccessTools.MethodDelegate<Action<Minimap, Minimap.PinData>>(method);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
