using HarmonyLib;
using UnityEngine;

namespace ValheimRadar
{
    // Harmony patches on the vanilla Minimap. Kept to a single guarded postfix so it composes with other
    // mods' patches instead of replacing game behaviour.
    [HarmonyPatch(typeof(Minimap))]
    internal static class MinimapPatches
    {
        // Minimap.RemovePin(Vector3 pos, float radius) is what right-click on the large map (via
        // RemovePinUnderPointer) and the gamepad "remove pin" button both call. It only considers pins with
        // m_save == true, so ValheimRadar's pins (created with save: false) were never removable.
        // When vanilla found nothing to remove (__result == false) - i.e. no player-made pin under the
        // cursor, so no ambiguity - offer the click to PinManager, which dismisses the closest radar pin
        // in range, if any. Vanilla pins therefore always win over a radar pin at the same spot.
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Minimap.RemovePin), typeof(Vector3), typeof(float))]
        private static void RemovePinPostfix(Minimap __instance, Vector3 pos, float radius, ref bool __result)
        {
            if (__result) return;

            try
            {
                __result = PinManager.TryDismissPinAt(__instance, pos, radius);
            }
            catch (System.Exception ex)
            {
                // Never let a radar bug break vanilla pin removal.
                Debug.LogError($"[ValheimRadar] Failed to dismiss pin: {ex}");
            }
        }
    }
}
