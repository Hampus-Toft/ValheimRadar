using UnityEngine;

namespace ValheimRadar
{
    // Routine, per-event chatter (pin created/updated/removed, scan summaries, troubleshooting snapshots).
    // Silent unless RadarConfig.DiagnosticLogging is on, so normal play doesn't spam the BepInEx log.
    // Errors and one-off warnings still go straight through Debug.LogError/LogWarning.
    internal static class RadarLog
    {
        public static void Diag(string message)
        {
            if (RadarConfig.DiagnosticLogging != null && RadarConfig.DiagnosticLogging.Value)
            {
                Debug.Log(message);
            }
        }
    }
}
