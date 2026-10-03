using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // An axis-aligned world x/z rectangle.
    internal struct WorldRect
    {
        public float MinX, MinZ, MaxX, MaxZ;

        public WorldRect(float minX, float minZ, float maxX, float maxZ)
        {
            MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
        }

        public float Width => MaxX - MinX;
        public float Height => MaxZ - MinZ;
        public float Area => Width * Height;
        public Vector3 Center => new Vector3((MinX + MaxX) / 2f, 0f, (MinZ + MaxZ) / 2f);

        public bool Contains(Vector3 p) => p.x >= MinX && p.x <= MaxX && p.z >= MinZ && p.z <= MaxZ;

        public bool Contains(WorldRect r) => r.MinX >= MinX && r.MaxX <= MaxX && r.MinZ >= MinZ && r.MaxZ <= MaxZ;

        public WorldRect Expand(float dx, float dz) => new WorldRect(MinX - dx, MinZ - dz, MaxX + dx, MaxZ + dz);
    }

    // Pure decisions behind pin virtualization (PinManager.Viewport.cs): only the persistent clusters
    // inside the part of the map currently shown, plus a margin, exist as Minimap pins. Vanilla's
    // Minimap.UpdatePins walks every pin whenever the map moves or zooms (and Minimap.RemovePin is a
    // linear List.Remove), so tens of thousands of always-present pins made dragging the map stutter.
    internal static class PinViewport
    {
        // Pins are kept for the view plus this fraction of its width/height on every side, so a drag
        // of up to half a screen needs no work at all and pins are already there when they scroll in.
        internal const float Margin = 0.5f;

        // Zooming in far enough that the kept area is this many times what a fresh reload would keep
        // triggers a reload too, so pins far outside a zoomed-in view get dropped.
        internal const float ShrinkReloadFactor = 4f;

        // Minimap.WorldToMapPoint inverted: map UV (0..1 over the whole map texture) to world x/z.
        internal static WorldRect UvToWorld(float uMin, float vMin, float uMax, float vMax, int textureSize, float pixelSize)
        {
            float half = textureSize / 2f;
            return new WorldRect(
                (uMin * textureSize - half) * pixelSize,
                (vMin * textureSize - half) * pixelSize,
                (uMax * textureSize - half) * pixelSize,
                (vMax * textureSize - half) * pixelSize);
        }

        // The area to keep pins for while view is shown.
        internal static WorldRect KeptRectFor(WorldRect view) => view.Expand(view.Width * Margin, view.Height * Margin);

        // Whether the kept area has to be recomputed for the current view: the view scrolled or zoomed
        // out past it, or zoomed in so far that most of it is wasted.
        internal static bool NeedsReload(WorldRect kept, WorldRect view)
        {
            if (!kept.Contains(view)) return true;

            float freshArea = KeptRectFor(view).Area;
            return freshArea > 0f && kept.Area > freshArea * ShrinkReloadFactor;
        }

        // Sorts items by x/z distance from origin, farthest first, so callers can pop the nearest off the
        // end of the list cheaply.
        internal static void SortNearestLast<T>(List<T> items, Func<T, Vector3> position, Vector3 origin)
        {
            var keys = new float[items.Count];
            var array = items.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                Vector3 p = position(array[i]);
                float dx = p.x - origin.x, dz = p.z - origin.z;
                keys[i] = -(dx * dx + dz * dz);
            }

            Array.Sort(keys, array);
            items.Clear();
            items.AddRange(array);
        }
    }
}
