using UnityEngine;

namespace ValheimRadar
{
    public class TrackedItem
    {
        public ZDOID Zdoid;
        public Vector3 Position;
        public string RawName;
        public string DisplayName;
        public Sprite Icon;
        public bool IsPersistent;
        public string CategoryKey;

        // In-memory only (never saved): consecutive verified rescans that didn't find this recorded
        // point, and when the first of them happened - see DepletionRules.
        public int MissedScans;
        public float FirstMissTime;
    }
}
