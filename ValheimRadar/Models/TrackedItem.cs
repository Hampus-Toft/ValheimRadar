using UnityEngine;

namespace ValheimRadar
{
    public class TrackedItem
    {
        public ZDOID Zdoid;
        public Vector3 Position;
        public string RawName;
        public string DisplayName;
        public Minimap.PinType PinType;
    }
}