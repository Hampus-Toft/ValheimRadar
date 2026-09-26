using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimRadar
{
    // Grid coordinate of one spatial scan cell - shared shape used by both SpatialCellScanner
    // (creatures) and PermanentSpatialScanner (resources/POI), even though each maintains its own
    // independent cache. Not required for correctness, but keeps world-space cell boundaries
    // aligned between the two scanning strategies.
    internal readonly struct ScanCellKey : IEquatable<ScanCellKey>
    {
        internal readonly int X;
        internal readonly int Z;

        internal ScanCellKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(ScanCellKey other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is ScanCellKey other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Z;
    }

    // Physics.OverlapBox cell geometry and the low-level per-cell query shared by every spatial
    // scanner. Extracted from the original BatchScanner so SpatialCellScanner and
    // PermanentSpatialScanner don't each carry their own copy of the same Physics query logic.
    internal static class ScanGeometry
    {
        // Deliberately equal to Valheim's own zone size (ZoneSystem.m_zoneSize, hardcoded as 64 in
        // ZoneSystem.GetZone/GetZonePos - confirmed by decompiling assembly_valheim.dll) so every scan
        // cell is exactly one game zone. A smaller/unaligned cell can straddle up to 4 zones, which
        // makes IsCellReady wait on the slowest of them and multiplies its readiness checks. Fixed
        // rather than derived from ScanRadius: per-tick scanned AREA already scales with the caller's
        // own batching strategy (rotation slice / new-cells-per-tick) regardless of cell size, so cell
        // size only trades off Physics call COUNT vs call SIZE, not the scan budget itself.
        internal const float CellSize = 64f;

        // Cell index containing a world-space coordinate on one axis. Valheim's zones are CENTERED on
        // multiples of CellSize (zone 0 spans -32..+32, not 0..64), so this mirrors
        // ZoneSystem.GetZone's floor((v + 32) / 64) rather than a plain floor(v / CellSize) - which
        // would leave every cell straddling two zones per axis, defeating the alignment.
        internal static int GetCellIndex(float world) => Mathf.FloorToInt((world + (CellSize / 2f)) / CellSize);

        // World-space center of a cell on one axis (matches ZoneSystem.GetZonePos).
        internal static float GetCellCenter(int index) => index * CellSize;

        // Vertical half-extent of a scan cell's collision box, independent of ScanRadius. ScanRadius
        // is a horizontal (XZ) distance, so reusing it for box height would mean a 300m ScanRadius
        // produces a 600m-tall query column per cell - capturing every collider from deep
        // underground to high above the skybox, layer-unfiltered. A fixed vertical range comfortably
        // covers Valheim's real terrain variance (caves through mountain peaks) around the player
        // without that blowup.
        internal const float CellHeight = 150f;

        // Physically queries one cell's world-space box and hands every live ZNetView-backed object
        // found inside it to classify, which returns either null (no match for this scanner's type)
        // or a TrackedItem with its type-specific fields already populated (DisplayName/Icon/
        // IsPersistent/CategoryKey) - this method fills in the remaining identity fields (Zdoid/
        // Position/RawName) that are the same regardless of which type matched.
        internal static List<TrackedItem> ScanCell(ScanCellKey key, float playerY, Func<ZNetView, GameObject, string, TrackedItem> classify) =>
            ScanCell(key, playerY - CellHeight, playerY + CellHeight, classify);

        // Same query over an explicit vertical range - see GetCellVerticalRange.
        internal static List<TrackedItem> ScanCell(ScanCellKey key, float minY, float maxY, Func<ZNetView, GameObject, string, TrackedItem> classify)
        {
            List<TrackedItem> items = new List<TrackedItem>();
            HashSet<ZDOID> processedZdoids = new HashSet<ZDOID>();

            Vector3 center = new Vector3(GetCellCenter(key.X), (minY + maxY) / 2f, GetCellCenter(key.Z));
            Vector3 halfExtents = new Vector3(CellSize / 2f, (maxY - minY) / 2f, CellSize / 2f);

            Collider[] hitColliders = Physics.OverlapBox(center, halfExtents);
            foreach (var hit in hitColliders)
            {
                if (hit == null) continue;

                GameObject obj = hit.gameObject;
                ZNetView netView = obj.GetComponentInParent<ZNetView>();

                if (netView == null || !netView.IsValid() || netView.GetZDO() == null) continue;

                ZDOID zdoid = netView.GetZDO().m_uid;
                if (!processedZdoids.Add(zdoid)) continue;

                string rawName = GetRawName(netView.gameObject);

                TrackedItem item = classify(netView, netView.gameObject, rawName);
                if (item == null) continue;

                item.Zdoid = zdoid;
                item.Position = netView.transform.position;
                item.RawName = rawName;
                items.Add(item);
            }

            return items;
        }

        // Lowercased prefab name of a scanned/hit object's ZNetView root - the name every evaluator
        // matches against.
        internal static string GetRawName(GameObject go) => go.name.Replace("(Clone)", "").Trim().ToLower();

        // Vertical range the permanent scanners query a cell over: CellHeight around the player AND
        // around the cell's own ground height. Centering on the player alone meant a player passing
        // below a mountain (boating, walking a valley) queried a box that never reached the silver or
        // obsidian on its slopes, and the old "scan once, ever" model then recorded that cell as
        // empty for good.
        internal static void GetCellVerticalRange(ScanCellKey key, float playerY, out float minY, out float maxY)
        {
            float? groundY = null;
            if (ZoneSystem.instance != null &&
                ZoneSystem.instance.GetGroundHeight(new Vector3(GetCellCenter(key.X), 0f, GetCellCenter(key.Z)), out float ground))
            {
                groundY = ground;
            }

            CombineVerticalRange(playerY, groundY, out minY, out maxY);
        }

        // Pure half of GetCellVerticalRange, split out for unit tests.
        internal static void CombineVerticalRange(float playerY, float? groundY, out float minY, out float maxY)
        {
            float low = playerY;
            float high = playerY;
            if (groundY.HasValue)
            {
                low = Mathf.Min(low, groundY.Value);
                high = Mathf.Max(high, groundY.Value);
            }

            minY = low - CellHeight;
            maxY = high + CellHeight;
        }

        // Where the game streams/instantiates objects around - normally the local player.
        internal static Vector3 GetReferencePosition(Vector3 playerPos) =>
            ZNet.instance != null ? ZNet.instance.GetReferencePosition() : playerPos;

        // True when the cell's zone is inside the "near" simulation area around referencePos, i.e.
        // the area in which ZNetScene actually instantiates GameObjects (confirmed by decompiling
        // assembly_valheim.dll: ZNetScene.CreateDestroyObjects -> ZDOMan.FindSectorObjects with
        // ZNet.GetSyncedSimulationDistance(), which walks rings up to NearSimulationDistance and,
        // unless IsClassic, keeps only zones passing ZoneSystem.ZonesWithinRadius). Outside it a
        // physics query can never see a resource, yet ZNetScene.IsAreaReady can still say "ready"
        // there when the client simply hasn't been sent that zone's ZDOs - which is how cells near
        // the edge of ScanRadius used to be recorded as empty forever. Only a scan of a cell inside
        // this area is trusted to have seen everything that's there (see PermanentSpatialScanner).
        internal static bool IsCellInLoadedArea(ScanCellKey key, Vector3 referencePos)
        {
            if (ZNet.instance == null || ZoneSystem.instance == null) return false;

            SimulationDistance simulation = ZNet.instance.GetSyncedSimulationDistance();
            int near = simulation.NearSimulationDistance;

            int dx = key.X - GetCellIndex(referencePos.x);
            int dz = key.Z - GetCellIndex(referencePos.z);
            if (!IsWithinNearArea(dx, dz, near)) return false;
            if (simulation.IsClassic) return true;

            Vector3 cellCenter = new Vector3(GetCellCenter(key.X), 0f, GetCellCenter(key.Z));
            return ZoneSystem.instance.ZonesWithinRadius(ZoneSystem.GetZone(referencePos), ZoneSystem.GetZone(cellCenter), near);
        }

        // Pure square (Chebyshev) part of IsCellInLoadedArea, for unit tests.
        internal static bool IsWithinNearArea(int dx, int dz, int nearDistance) =>
            Math.Abs(dx) <= nearDistance && Math.Abs(dz) <= nearDistance;

        // Gates every PermanentSpatialScanner cell query against Valheim's own object
        // instantiation lag - confirmed by decompiling assembly_valheim.dll: on a dedicated server, a
        // sector's ZDOs sync over the network and ZNetScene.CreateObjectsSorted() instantiates their
        // GameObjects at a throttled rate (10/frame outside a loading screen), so a cell that just
        // entered ScanRadius can have zero colliders in place yet even though the ZDOs themselves
        // already exist. ScanCell's Physics.OverlapBox only ever sees already-instantiated
        // GameObjects, so scanning (and permanently marking scanned) a cell before its objects finish
        // loading silently drops whatever hadn't spawned in yet - this is what left ores/berries/
        // pickables unpinned outside the immediate area around login/respawn (where objects had
        // already had time to load) on a dedicated server, while listen-server/singleplayer never hit
        // it since there's no network hop to lag behind.
        //
        // ZNetScene.IsAreaReady(point) is the verified-stable public API (same decompile) the base
        // game itself uses to answer exactly this question - it checks both that the point's zone has
        // been received (ZoneSystem.IsZoneLoaded) and that every ZDO already known to be relevant to
        // that zone has a live GameObject instance (and, per the same decompile, it also covers the
        // zone's immediate neighbours via FindSectorObjects with SimulationDistance(1, 0)). Cells are
        // zone-aligned (see CellSize/GetCellIndex), so a cell is exactly one zone and a single sample
        // at its center is sufficient - no need to probe corners across several zones.
        internal static bool IsCellReady(ScanCellKey key, float y)
        {
            if (ZNetScene.instance == null) return true;

            return ZNetScene.instance.IsAreaReady(new Vector3(GetCellCenter(key.X), y, GetCellCenter(key.Z)));
        }
    }
}
