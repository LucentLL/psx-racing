using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FALL HOLES (REAL HEIGHTS round 4, 2026-10-26; the owner on the live
    /// Uptown Loop: "drove through an open section of median, fell through the
    /// bridge, then fell through the ground below"). On every route tile the
    /// drive audit stands up (the tile and its ring, the game's own colliders
    /// through CityWorld.Attach), a ray straight down every metre of the
    /// centre tile from high above: a point where it meets NOTHING is a hole a
    /// car falls through to the respawn. Counted near a road (within
    /// <see cref="FallNearRoadM"/> of an edge's pavement) and anywhere in the
    /// tile, as distinct places (<see cref="FallSpotM"/>), with the nearest
    /// road and its drawn height. Water and building interiors are not holes
    /// the game means: a point inside a water span's plan or a building's
    /// footprint is reported apart (the ground mesh may leave them open).
    /// </summary>
    public static partial class CityAudit
    {
        const float FallStepM = 1f, FallNearRoadM = 12f, FallSpotM = 6f;
        /// <summary>(round 10) A single Roads hit this close to the drawn lattice is a surface laid
        /// on the land (a lot's pavement), not a deck. PSX_AUDIT_R10_LOTS=0: the old reading.</summary>
        const float FallOnLatticeM = 0.15f;
        static readonly bool FallLotsOn = System.Environment.GetEnvironmentVariable("PSX_AUDIT_R10_LOTS") != "0";
        static int fallRays, fallNone, fallNoneNear, fallOnLattice;
        static readonly List<(Vector2 p, bool near, string note)> fallSpots = new List<(Vector2, bool, string)>();
        static readonly HashSet<int> fallSegs = new HashSet<int>();
        static readonly Dictionary<string, int> fallByTop = new Dictionary<string, int>();

        static void FallHolesReset() { fallRays = fallNone = fallNoneNear = fallOnLattice = 0; fallSpots.Clear(); fallByTop.Clear(); }

        static void FallHoles(CityMap map, CityMeshes.Trims trims, int tx, int tz)
        {
            if (System.Environment.GetEnvironmentVariable("PSX_DRIVE_HOLES") == "0") return;
            float x0 = tx * CityMeshes.TileSize, z0 = tz * CityMeshes.TileSize;
            int n = Mathf.RoundToInt(CityMeshes.TileSize / FallStepM);
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float x = x0 + (ix + 0.5f) * FallStepM, z = z0 + (iz + 0.5f) * FallStepM;
                    fallRays++;
                    // the top surface, and then whatever is under it: a deck or a
                    // ribbon with nothing beneath is a hole for a car that leaves it
                    // over a gap (the owner fell off the deck through a median gap,
                    // then through the ground)
                    string under = "";
                    var hits = Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Length > 0)
                    {
                        // a land surface somewhere down the ray: the car lands on it
                        bool land = false; float topY = float.MinValue; string topName = "";
                        foreach (var hh in hits)
                        {
                            string nm = hh.collider.name;
                            if (nm.StartsWith("Ground") || nm.StartsWith("Pavement") || nm.StartsWith("Lot") || nm.StartsWith("Water")) land = true;
                            if (hh.point.y > topY) { topY = hh.point.y; topName = nm; }
                        }
                        if (land) continue;
                        // a road on the ground is the surface itself (the lattice is not
                        // drawn under a ribbon); only a DECK with nothing under it is a
                        // hole for a car that leaves it over a gap
                        if (hits.Length > 1 || !OnStructureNear(map, new Vector2(x, z))) continue;
                        // (round 10) ...and a surface lying ON the lattice is no deck: a parking
                        // lot's pavement (CityMeshes.Lots: the cell's own pieces, drawn in the
                        // Roads mesh) beside a bridge's end is the land itself. All 8 places
                        // the base counted (S Tryon at the I-277 bridge, Briar Creek Rd's lots
                        // by its two bridges) were lot pavement at the lattice's height, 1.7-8 m
                        // under the deck beside it. A deck stands 1.75 m+ over the land under it.
                        if (FallLotsOn && topName == "Roads" && Mathf.Abs(topY - CityMeshes.LatticeAt(map, x, z)) < FallOnLatticeM) { fallOnLattice++; continue; }
                        under = $" (nothing under the deck surface {topName} at y {topY:0.00})";
                        fallByTop[topName] = (fallByTop.TryGetValue(topName, out int fc) ? fc : 0) + 1;
                    }
                    fallNone++;
                    var p = new Vector2(x, z);
                    // the nearest road: its pavement edge distance and drawn height
                    fallSegs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * 30f, p + Vector2.one * 30f, fallSegs);
                    float bd = float.MaxValue; int bi = -1; float bs = 0f;
                    foreach (var packed in fallSegs)
                    {
                        int oi = packed >> 12, si = packed & 0xFFF;
                        var o = map.edges[oi];
                        Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                        float L2 = dq.sqrMagnitude;
                        float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, dq) / L2) : 0f;
                        float sAt = o.s[si] + Mathf.Sqrt(L2) * tt;
                        float dd = Vector2.Distance(p, q0 + dq * tt) - trims.HalfWidthAt(o, sAt);
                        if (dd < bd) { bd = dd; bi = oi; bs = sAt; }
                    }
                    bool near = bi >= 0 && bd <= FallNearRoadM;
                    if (near) fallNoneNear++;
                    bool merged = false;
                    for (int k = fallSpots.Count - 1; k >= 0 && k >= fallSpots.Count - 64; k--)
                        if ((fallSpots[k].p - p).sqrMagnitude < FallSpotM * FallSpotM) { merged = true; break; }
                    if (merged) continue;
                    string note = bi < 0 ? "no road within 30 m"
                        : $"nearest road e{bi} '{map.edges[bi].name}'{(map.edges[bi].link ? " L" : "")}{(map.edges[bi].bridge ? " B" : "")} cls{map.edges[bi].cls} {(bd < 0f ? "ON its pavement" : $"{bd:0.0} m off its pavement")} s={bs:0}/{map.edges[bi].length:0} drawn y {map.edges[bi].YAt(bs):0.00}{(map.edges[bi].ElevatedAt(bs) ? " (on structure)" : "")}";
                    fallSpots.Add((p, near, note + under));
                }
        }

        /// <summary>Is the nearest road to a plan point on structure there (within 1 m of its pavement)?</summary>
        static bool OnStructureNear(CityMap map, Vector2 p)
        {
            fallSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 25f, p + Vector2.one * 25f, fallSegs);
            foreach (var packed in fallSegs)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[oi];
                Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                float L2 = dq.sqrMagnitude;
                float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, dq) / L2) : 0f;
                float sAt = o.s[si] + Mathf.Sqrt(L2) * tt;
                if (Vector2.Distance(p, q0 + dq * tt) <= o.HalfMax + 1f && o.ElevatedAt(sAt)) return true;
            }
            return false;
        }

        static void FallHolesReport()
        {
            if (System.Environment.GetEnvironmentVariable("PSX_DRIVE_HOLES") == "0") return;
            int places = fallSpots.Count, placesNear = 0;
            foreach (var s in fallSpots) if (s.near) placesNear++;
            Line($"FALL HOLES (round 4; a ray down every {FallStepM:0} m of every route tile, the game's colliders): {fallRays} rays, {fallNone} meet nothing ({fallNoneNear} within {FallNearRoadM:0} m of a road's pavement); distinct places (6 m) {places}, {placesNear} near a road");
            var bt = new StringBuilder("  by the top surface (no land under it):");
            foreach (var kv in fallByTop) bt.Append($" {kv.Key} {kv.Value};");
            // (round 10) the rays whose one surface is a lot's pavement on the lattice by a bridge (not holes)
            bt.Append($" | a lone Roads surface on the lattice (a lot's pavement, the land itself; round 10{(FallLotsOn ? "" : ", PSX_AUDIT_R10_LOTS=0: counted as holes")}): {fallOnLattice} rays");
            Line(bt.ToString());
            int shown = 0;
            foreach (var s in fallSpots)
            {
                if (!s.near && shown >= 40) continue;
                if (shown++ >= 120) break;
                Line($"    HOLE{(s.near ? " NEAR ROAD" : "")} ({s.p.x:0.0},{s.p.y:0.0}) {LatLon(s.p.x, s.p.y)}: {s.note}");
            }
            Check(placesNear == 0, "no hole a car falls through (no collider under a ray) within 12 m of a road on the route tiles (drive audit FALL HOLES)", placesNear);
        }
    }
}
