using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ValheimRadar
{
    // Makes the player's own marker (and the ship marker) render above every pin and pin label on the
    // Minimap, so ValheimRadar's many pins no longer cover it.
    //
    // uGUI draws in hierarchy order (depth-first, later siblings on top). Minimap creates pins as children of
    // m_pinRoot*/m_pinNameRoot*, so whichever of those hierarchy branches comes after the marker's branch
    // draws over it. The fix is therefore purely a re-ordering of the marker's branch - no per-frame work,
    // no patch on game code - applied once each time a Minimap comes up (RadarPlugin, on connect):
    //  1. Preferred: move the marker's branch to be the last child of the common ancestor it shares with the
    //     pin roots, but only when that branch is a small widget (never a panel that also holds the map
    //     image, or the map would then draw over the pins).
    //  2. Otherwise: give the marker its own override-sorting Canvas, which draws above the shared canvas
    //     regardless of hierarchy order.
    // Everything is null-guarded and wrapped in try/catch; a failure just leaves vanilla ordering.
    public static class MinimapMarkerOrder
    {
        // A marker branch bigger than this is assumed to be a container, not a marker widget.
        private const int MaxMarkerBranchTransforms = 8;

        public static void Apply(Minimap minimap)
        {
            if (minimap == null) return;

            try
            {
                Raise(minimap.m_smallMarker, minimap.m_pinRootSmall, minimap.m_pinNameRootSmall);
                Raise(minimap.m_smallShipMarker, minimap.m_pinRootSmall, minimap.m_pinNameRootSmall);
                Raise(minimap.m_largeMarker, minimap.m_pinRootLarge, minimap.m_pinNameRootLarge);
                Raise(minimap.m_largeShipMarker, minimap.m_pinRootLarge, minimap.m_pinNameRootLarge);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ValheimRadar] marker-order failed, leaving vanilla ordering: {ex.Message}");
            }
        }

        // Pure: compares where two elements draw in a uGUI hierarchy, given each one's path of sibling
        // indices from the shared root down to the element. Returns > 0 if a draws after (on top of) b,
        // < 0 if before, 0 if they are the same element. An ancestor draws before its descendants.
        public static int CompareDrawOrder(IList<int> a, IList<int> b)
        {
            int shared = System.Math.Min(a.Count, b.Count);
            for (int i = 0; i < shared; i++)
            {
                if (a[i] != b[i]) return a[i] > b[i] ? 1 : -1;
            }

            return a.Count.CompareTo(b.Count);
        }

        private static void Raise(RectTransform marker, params RectTransform[] references)
        {
            if (marker == null) return;

            var pending = new List<Transform>();
            foreach (var reference in references)
            {
                if (reference == null) continue;
                if (marker.root != reference.root) continue; // not comparable

                if (CompareDrawOrder(SiblingPath(marker), SiblingPath(reference)) <= 0) pending.Add(reference);
            }

            if (pending.Count == 0)
            {
                Debug.Log($"[ValheimRadar] marker-order marker={PathOf(marker)} already above pins");
                return;
            }

            if (TryReorderBranch(marker, pending))
            {
                Debug.Log($"[ValheimRadar] marker-order marker={PathOf(marker)} raised by sibling order");
                return;
            }

            if (TryOverrideSorting(marker))
            {
                Debug.Log($"[ValheimRadar] marker-order marker={PathOf(marker)} raised by canvas override");
                return;
            }

            Debug.LogWarning($"[ValheimRadar] marker-order could not raise marker={PathOf(marker)} pins={PathOf(pending[0])}");
        }

        private static bool TryReorderBranch(Transform marker, List<Transform> above)
        {
            foreach (var reference in above)
            {
                Transform ancestor = CommonAncestor(marker, reference);

                // Marker nested inside a pin root (or vice versa), or no shared ancestor: sibling order
                // between two branches can't express "on top".
                if (ancestor == null || ancestor == marker || ancestor == reference) return false;

                Transform branch = marker;
                while (branch.parent != ancestor) branch = branch.parent;

                if (branch.GetComponentsInChildren<Transform>(true).Length > MaxMarkerBranchTransforms) return false;

                branch.SetAsLastSibling();
            }

            foreach (var reference in above)
            {
                if (CompareDrawOrder(SiblingPath(marker), SiblingPath(reference)) <= 0) return false;
            }

            return true;
        }

        private static bool TryOverrideSorting(Transform marker)
        {
            Canvas canvas = marker.GetComponent<Canvas>();
            if (canvas == null) canvas = marker.gameObject.AddComponent<Canvas>();

            Canvas parentCanvas = marker.parent != null ? marker.parent.GetComponentInParent<Canvas>() : null;
            canvas.overrideSorting = true;
            canvas.sortingOrder = (parentCanvas != null ? parentCanvas.sortingOrder : 0) + 1;
            return true;
        }

        private static Transform CommonAncestor(Transform a, Transform b)
        {
            for (Transform x = a; x != null; x = x.parent)
            {
                for (Transform y = b; y != null; y = y.parent)
                {
                    if (x == y) return x;
                }
            }

            return null;
        }

        private static List<int> SiblingPath(Transform t)
        {
            var path = new List<int>();
            for (; t != null; t = t.parent) path.Add(t.GetSiblingIndex());
            path.Reverse();
            return path;
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t != null ? t.name : "<null>");
            for (Transform p = t != null ? t.parent : null; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
