using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimRadar
{
    // Harmony patches on the vanilla Minimap. Kept to guarded postfixes so they compose with other mods'
    // patches instead of replacing game behaviour.
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

        // Private Minimap members OnMapLeftClick itself uses to find the clicked pin.
        private static readonly MethodInfo ScreenToWorldPoint = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint");
        private static readonly MethodInfo GetClosestPin = AccessTools.Method(typeof(Minimap), "GetClosestPin");
        private static readonly MethodInfo PinInteractRadius = AccessTools.PropertyGetter(typeof(Minimap), "PinInteractRadius");

        // Minimap.OnMapLeftClick (a short left-click on the large map) toggles the X on the closest pin -
        // again only pins with m_save == true. When it found no vanilla pin under the cursor, cross out
        // (or clear) the closest radar pin instead, so vanilla pins still win at the same spot. Only runs
        // on a click, so the reflection calls cost nothing per frame. The gamepad equivalent is inline in
        // Minimap.Update and isn't covered.
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Minimap.OnMapLeftClick))]
        private static void OnMapLeftClickPostfix(Minimap __instance)
        {
            try
            {
                Vector3 pos = (Vector3)ScreenToWorldPoint.Invoke(__instance, new object[] { ZInput.pointerPosition });
                float radius = (float)PinInteractRadius.Invoke(__instance, null);
                if (GetClosestPin.Invoke(__instance, new object[] { pos, radius, true }) != null) return;

                PinManager.TryToggleCheckedAt(__instance, pos, radius);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to cross out pin: {ex}");
            }
        }
    }
}
