using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimRadar
{
    // Client-side hooks that hide a depletable resource's pin the moment the local player starts
    // taking it (issue: mined ore/obsidian kept its pin until the whole map was re-explored). Same
    // idea as the Wishbone ignoring a silver vein after its first hit.
    //
    // Confirmed by decompiling assembly_valheim.dll: Destructible/MineRock/MineRock5.Damage(HitData)
    // run on the ATTACKER's client and only forward the hit as an RPC to the object's owner (often the
    // server, or another player), which applies it. So the owner-side outcome isn't observable here;
    // instead each prefix re-checks the two things that make the owner reject a hit - the tool tier
    // (HitData.CheckToolTier) and whether any damage survives the object's resistances - so e.g.
    // swinging an antler pickaxe at obsidian ("too hard") doesn't hide its pin. Pickable.Interact
    // likewise runs on the picking player's client. Everything is read-only prefixes/postfixes: the
    // vanilla methods always run unchanged, and any exception is swallowed.
    //
    // Pickables that grow back (berries, mushrooms, flowers, crops - m_respawnTimeMinutes > 0) aren't
    // depleted: picking one instead hides its pin for that same respawn time (PinManager.MarkPickedAt).
    internal static class DepletionPatches
    {
        private static bool loggedFailure;

        internal static void Apply(Harmony harmony)
        {
            Patch(harmony, typeof(Destructible), nameof(Destructible.Damage), nameof(DestructibleDamagePrefix), isPrefix: true);
            Patch(harmony, typeof(MineRock), nameof(MineRock.Damage), nameof(MineRockDamagePrefix), isPrefix: true);
            Patch(harmony, typeof(MineRock5), nameof(MineRock5.Damage), nameof(MineRock5DamagePrefix), isPrefix: true);
            PatchPickableInteract(harmony);
        }

        // Prefix and postfix in one call, so the postfix never runs without the prefix's __state.
        private static void PatchPickableInteract(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Pickable), nameof(Pickable.Interact)),
                    prefix: new HarmonyMethod(typeof(DepletionPatches), nameof(PickableInteractPrefix)),
                    postfix: new HarmonyMethod(typeof(DepletionPatches), nameof(PickableInteractPostfix)));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to patch Pickable.Interact (picked resources will only be removed by rescans, regrowing ones won't be hidden): {ex}");
            }
        }

        // Each target patched on its own so one failing (e.g. a game update renaming a method) only
        // loses that trigger - rescans still remove the pin later.
        private static void Patch(Harmony harmony, Type target, string method, string patchMethod, bool isPrefix)
        {
            try
            {
                var original = AccessTools.Method(target, method);
                var patch = new HarmonyMethod(typeof(DepletionPatches), patchMethod);
                if (isPrefix) harmony.Patch(original, prefix: patch);
                else harmony.Patch(original, postfix: patch);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ValheimRadar] Failed to patch {target.Name}.{method} (mined resources will only be removed by rescans): {ex}");
            }
        }

        private static void DestructibleDamagePrefix(Destructible __instance, HitData hit)
        {
            Guard(() =>
            {
                if (IsEffectiveLocalHit(hit, __instance.m_damages, __instance.m_minToolTier)) Report(__instance);
            });
        }

        private static void MineRockDamagePrefix(MineRock __instance, HitData hit)
        {
            Guard(() =>
            {
                if (IsEffectiveLocalHit(hit, __instance.m_damageModifiers, __instance.m_minToolTier)) Report(__instance);
            });
        }

        private static void MineRock5DamagePrefix(MineRock5 __instance, HitData hit)
        {
            Guard(() =>
            {
                if (IsEffectiveLocalHit(hit, __instance.m_damageModifiers, __instance.m_minToolTier)) Report(__instance);
            });
        }

        // Interact still runs (and re-sends RPC_Pick, which the owner ignores) on an already-picked
        // pickable, so remember whether this one was actually available - otherwise interacting with
        // an emptied berry bush would restart its respawn timer.
        private static void PickableInteractPrefix(Pickable __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = !__instance.GetPicked();
            }
            catch
            {
                // The postfix then treats it as already picked - never let a radar bug break picking.
            }
        }

        private static void PickableInteractPostfix(Pickable __instance, Humanoid character, bool __state)
        {
            Guard(() =>
            {
                if (character == null || character != Player.m_localPlayer) return;

                if (__instance.m_respawnTimeMinutes > 0f)
                {
                    if (__state) ReportPicked(__instance);
                }
                else
                {
                    Report(__instance);
                }
            });
        }

        private static bool IsEffectiveLocalHit(HitData hit, HitData.DamageModifiers modifiers, int minToolTier)
        {
            if (hit == null || Player.m_localPlayer == null) return false;
            if (hit.m_attacker != Player.m_localPlayer.GetZDOID()) return false;
            if (!hit.CheckToolTier(minToolTier)) return false;

            // ApplyResistance mutates the hit, and the real one still has to reach the owner intact.
            HitData probe = hit.Clone();
            probe.ApplyResistance(modifiers, out _);
            return probe.GetTotalDamage() > 0f;
        }

        private static void Report(Component component)
        {
            if (RadarConfig.RemoveDepletedResources != null && !RadarConfig.RemoveDepletedResources.Value) return;

            ZNetView netView = component.GetComponentInParent<ZNetView>();
            if (netView == null || !netView.IsValid()) return;

            GameObject root = netView.gameObject;
            string nameLower = ScanGeometry.GetRawName(root);

            if (!ObjectEvaluator.TryGetDepletableCategory(root, nameLower, out string categoryKey))
            {
                // "_frac" remains of a deposit someone else already broke open still mean that deposit
                // is being mined out - resolve it to whatever depletable point sits at the same spot.
                if (!ScanFilters.IsDebrisName(nameLower)) return;
                categoryKey = null;
            }

            PinManager.MarkDepletedAt(categoryKey, root.transform.position);
        }

        private static void ReportPicked(Pickable pickable)
        {
            if (RadarConfig.HidePickedUntilRespawn != null && !RadarConfig.HidePickedUntilRespawn.Value) return;
            if (ZNet.instance == null) return;

            ZNetView netView = pickable.GetComponentInParent<ZNetView>();
            if (netView == null || !netView.IsValid()) return;

            GameObject root = netView.gameObject;
            if (!ObjectEvaluator.TryGetRespawningCategory(root, ScanGeometry.GetRawName(root), out string categoryKey)) return;

            // Same clock and duration Pickable.ShouldRespawn uses.
            double now = ZNet.instance.GetTimeSeconds();
            PinManager.MarkPickedAt(categoryKey, root.transform.position, now, RespawnTimerStore.GetRespawnAt(now, pickable.m_respawnTimeMinutes));
        }

        private static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Never let a radar bug break mining; log the first failure only (these fire per hit).
                if (loggedFailure) return;
                loggedFailure = true;
                Debug.LogError($"[ValheimRadar] Failed to process a mined/picked resource: {ex}");
            }
        }
    }
}
