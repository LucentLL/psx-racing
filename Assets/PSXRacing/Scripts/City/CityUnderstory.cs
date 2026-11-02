using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S ROADSIDE UNDERSTORY (2026-11-02): the owner's Codex pack
    /// (Environment_Overhaul2_Uptown/Roadside_LowVegetation) - tall grass,
    /// shrubs and an open woodland-edge bush - planted where a 1990s DOT
    /// roadside has them, on the tile's <see cref="RoadsideOccupancy"/> mask
    /// so nothing stands on pavement, in a building, water, a sight triangle
    /// or a corner spot, or under a deck:
    ///
    ///   THE VERGES of every freeway, expressway and ramp (trunk and motorway,
    ///   OSM classes 4-5): a continuous strip of tall grass at the clear
    ///   zone's outer edge (low grass may stand in a clear zone; the mown
    ///   shoulder runs up to it), fescue and switchgrass clumps scattered
    ///   beyond it, and mountain laurel where the canopy map says the edge is
    ///   wooded (<see cref="CityCanopy"/>).
    ///   THE MEDIANS AND INFIELDS: free ground lying between two opposed
    ///   carriageways of an arterial or bigger (class 3+) - a divided road's
    ///   planted median, an interchange's loop - gets inkberry and azalea.
    ///
    /// Deterministic from the map and the tile alone (every choice a hash of
    /// the road piece or the cell, and a card belongs to the tile its foot is
    /// in), like <see cref="CityTrees"/>. One merged mesh a tile (one draw),
    /// alpha-tested and two-sided, in the season's look: the grass green in
    /// spring and summer, drying in fall, dormant brown in winter and snow
    /// (tools/city/understory/understory_atlas.py; the shrubs keep their own
    /// leaves). Phones plant <see cref="PhoneDensity"/> of the clumps and
    /// shrubs (the strips stay whole: a broken verge reads as a bug).
    /// PSX_UNDERSTORY=0 plants nothing (the A/B).
    /// </summary>
    public static class CityUnderstory
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_UNDERSTORY") != "0";
        public const float PhoneDensity = 0.5f;
        public static float Density => Application.isMobilePlatform ? PhoneDensity : 1f;

        /// <summary>The atlas's cells (4 x 2 squares; Unity v from the
        /// bottom): UV rectangle, the full quad (width, height, metres, the
        /// handoff's figures: the card's transparent margins included, its
        /// pivot the bottom centre) and whether it stands as a crossed pair.</summary>
        struct Cell { public float u0, v0, u1, v1, w, h; public bool crossed; }
        const float PadU = 0.5f / 512f, PadV = 0.5f / 256f;
        static readonly Cell[] Cells =
        {
            new Cell { u0 = 0.00f, v0 = 0.5f, u1 = 0.25f, v1 = 1.0f, w = 1.11f, h = 1.11f, crossed = true },   // 0 fescue
            new Cell { u0 = 0.25f, v0 = 0.5f, u1 = 0.50f, v1 = 1.0f, w = 1.20f, h = 1.20f, crossed = true },   // 1 switchgrass
            new Cell { u0 = 0.50f, v0 = 0.5f, u1 = 0.75f, v1 = 1.0f, w = 1.72f, h = 1.72f, crossed = true },   // 2 inkberry
            new Cell { u0 = 0.75f, v0 = 0.5f, u1 = 1.00f, v1 = 1.0f, w = 2.47f, h = 2.47f, crossed = true },   // 3 mountain laurel
            new Cell { u0 = 0.00f, v0 = 0.0f, u1 = 0.25f, v1 = 0.5f, w = 1.55f, h = 1.55f, crossed = true },   // 4 azalea
            new Cell { u0 = 0.25f, v0 = 0.0f, u1 = 0.75f, v1 = 0.5f, w = 2.40f, h = 1.17f, crossed = false },  // 5 the verge strip
        };
        const int Fescue = 0, Switchgrass = 1, Inkberry = 2, Laurel = 3, Azalea = 4, Strip = 5;

        /// <summary>A verge strip's step along its road (a hair under its
        /// 2.4 m repeat, so neighbours overlap rather than gap on a curve).</summary>
        const float StripStepM = 2.3f;
        /// <summary>The strip stands this share of the clear zone out from
        /// the pavement (at least <see cref="StripMinOffM"/>).</summary>
        const float StripClearShare = 0.75f, StripMinOffM = 2.5f;
        /// <summary>Medians: the scan's cell, and how near the two opposed
        /// carriageways must both be.</summary>
        const float MedianCellM = 4f, MedianReachM = 16f;
        /// <summary>Feet sit this far into the ground (a slope's high side
        /// never shows a gap under the card).</summary>
        const float SinkM = 0.08f;
        /// <summary>The mask bits nothing low may stand on (the clear zone
        /// is allowed: low grass belongs in it).</summary>
        const byte Forbid = (byte)(RoadsideOccupancy.Pavement | RoadsideOccupancy.Sight | RoadsideOccupancy.Corner |
                                   RoadsideOccupancy.Building | RoadsideOccupancy.Water | RoadsideOccupancy.Deck |
                                   RoadsideOccupancy.Other);

        /// <summary>Totals since load, for the tools: pieces walked, cards
        /// planted, and why a verge strip was refused.</summary>
        public static int StatPieces, StatStrips, StatClumps, StatShrubs, StatNotMine, StatForbid, StatOnRoad;
        public static string Stats => $"pieces {StatPieces}, strips {StatStrips}, clumps {StatClumps}, shrubs {StatShrubs}; strip refusals: other tile {StatNotMine}, mask {StatForbid}, on a road {StatOnRoad}";

        public class UnderTile
        {
            public Mesh mesh;
            public int strips, clumps, shrubs;
        }

        static readonly List<Vector3> vs = new List<Vector3>(4096), ns = new List<Vector3>(4096);
        static readonly List<Vector2> uvs = new List<Vector2>(4096);
        static readonly List<int> ts = new List<int>(6144);
        static readonly List<RoadsideOccupancy.NearRoad> near = new List<RoadsideOccupancy.NearRoad>(16);

        /// <summary>Plant one tile on its mask; null when nothing stands.</summary>
        public static UnderTile Build(CityMap map, RoadsideOccupancy occ, int tx, int tz)
        {
            if (!Enabled || occ == null) return null;
            vs.Clear(); ns.Clear(); uvs.Clear(); ts.Clear();
            var ut = new UnderTile();
            var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            var origin = new Vector3(min.x, 0f, min.y);
            float density = Density;

            // THE VERGES: along every freeway, expressway and ramp, both sides
            foreach (var pc in occ.pieces)
            {
                if (pc.deck) continue;
                var e = map.edges[pc.edge];
                if (e.cls < 4) continue;
                StatPieces++;
                var d = pc.b - pc.a;
                float L = d.magnitude;
                if (L < 0.1f) continue;
                d /= L;
                var nrm = new Vector2(-d.y, d.x);
                float off0 = pc.hw + Mathf.Max(StripMinOffM, pc.clear * StripClearShare);
                for (float s = Mathf.Ceil(pc.sa / StripStepM) * StripStepM; s < pc.sb; s += StripStepM)
                {
                    int st = Mathf.RoundToInt(s / StripStepM);
                    var c = pc.a + d * (s - pc.sa);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int salt = side < 0 ? 0 : 100;
                        float off = off0 + CityTrees.Hash01(pc.edge, st, 1 + salt) * 0.8f;
                        var mid = c + nrm * (side * off);
                        if (!Mine(mid, min)) { StatNotMine++; continue; }
                        var half = d * (Cells[Strip].w * 0.5f);
                        var pa = mid - half; var pb = mid + half;
                        if (!Free(occ, pa) || !Free(occ, mid) || !Free(occ, pb)) { StatForbid++; continue; }
                        if (OnAnotherRoad(occ, mid)) { StatOnRoad++; continue; }
                        float hs = 0.85f + 0.3f * CityTrees.Hash01(pc.edge, st, 2 + salt);
                        // the strip faces its road (its stems' bases run along the bottom)
                        StripCard(map, origin, pa, pb, -side * nrm, hs);
                        ut.strips++;
                        // the clumps beyond it, and the laurel at a wooded edge
                        if (CityTrees.Hash01(pc.edge, st, 3 + salt) < 0.45f * density)
                        {
                            var q = c + nrm * (side * (off + 0.8f + 4.2f * CityTrees.Hash01(pc.edge, st, 4 + salt)))
                                  + d * ((CityTrees.Hash01(pc.edge, st, 5 + salt) - 0.5f) * StripStepM);
                            if (Mine(q, min) && Free(occ, q) && !OnAnotherRoad(occ, q))
                            {
                                int cell = CityTrees.Hash01(pc.edge, st, 6 + salt) < 0.3f ? Switchgrass : Fescue;
                                Crossed(map, origin, q, cell, 0.8f + 0.4f * CityTrees.Hash01(pc.edge, st, 7 + salt), CityTrees.Hash01(pc.edge, st, 8 + salt) * 180f);
                                ut.clumps++;
                            }
                        }
                        if (CityTrees.Hash01(pc.edge, st, 9 + salt) < 0.22f * density)
                        {
                            var q = c + nrm * (side * (off + 5f + 4f * CityTrees.Hash01(pc.edge, st, 10 + salt)));
                            if (Mine(q, min) && occ.At(q) == 0 && CityCanopy.Loaded && CityCanopy.CellFraction(q.x, q.y) >= 0.4f)
                            {
                                Crossed(map, origin, q, Laurel, 0.8f + 0.4f * CityTrees.Hash01(pc.edge, st, 11 + salt), CityTrees.Hash01(pc.edge, st, 12 + salt) * 180f);
                                ut.shrubs++;
                            }
                        }
                    }
                }
            }

            // THE MEDIANS AND INFIELDS: free ground between two opposed carriageways
            int n = Mathf.RoundToInt(CityMeshes.TileSize / MedianCellM);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int gx = tx * n + x, gz = tz * n + z;
                    if (CityTrees.Hash01(gx, gz, 50) >= 0.35f * density) continue;
                    var p = min + new Vector2((x + CityTrees.Hash01(gx, gz, 51)) * MedianCellM, (z + CityTrees.Hash01(gx, gz, 52)) * MedianCellM);
                    if (occ.At(p) != 0) continue;   // beyond every clear zone too: a shrub is not a grass
                    if (!BetweenRoads(map, occ, p)) continue;
                    int cell = CityTrees.Hash01(gx, gz, 53) < 0.6f ? Inkberry : Azalea;
                    Crossed(map, origin, p, cell, 0.8f + 0.4f * CityTrees.Hash01(gx, gz, 54), CityTrees.Hash01(gx, gz, 55) * 180f);
                    ut.shrubs++;
                }

            StatStrips += ut.strips; StatClumps += ut.clumps; StatShrubs += ut.shrubs;
            if (vs.Count == 0) return ut;
            var m = new Mesh { name = "understory" };
            if (vs.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(vs);
            m.SetNormals(ns);
            m.SetUVs(0, uvs);
            m.SetTriangles(ts, 0, false);
            m.RecalculateBounds();
            ut.mesh = m;
            return ut;
        }

        static bool Mine(Vector2 p, Vector2 min) =>
            p.x >= min.x && p.y >= min.y && p.x < min.x + CityMeshes.TileSize && p.y < min.y + CityMeshes.TileSize;

        static bool Free(RoadsideOccupancy occ, Vector2 p) => (occ.At(p) & Forbid) == 0;

        /// <summary>On (or within half a metre of) some OTHER carriageway's
        /// pavement - a ramp's, a crossing street's - whatever its piece.</summary>
        static bool OnAnotherRoad(RoadsideOccupancy occ, Vector2 p) =>
            occ.RoadEdgeDistance(p, 2f, out _, out _, out _) < 0.5f;

        /// <summary>A median or an infield: two grounded carriageways of an
        /// arterial or bigger within reach, on opposite sides of the point.</summary>
        static bool BetweenRoads(CityMap map, RoadsideOccupancy occ, Vector2 p)
        {
            near.Clear();
            occ.NearRoads(p, MedianReachM, near);
            for (int i = 0; i < near.Count; i++)
            {
                var a = near[i];
                if (a.deck || map.edges[a.edge].cls < 3) continue;
                for (int j = i + 1; j < near.Count; j++)
                {
                    var b = near[j];
                    if (b.deck || b.edge == a.edge || map.edges[b.edge].cls < 3) continue;
                    if (Vector2.Dot(a.away, b.away) < -0.5f) return true;
                }
            }
            return false;
        }

        static void StripCard(CityMap map, Vector3 origin, Vector2 pa, Vector2 pb, Vector2 facing, float hs)
        {
            var c = Cells[Strip];
            float ya = CityMeshes.LatticeAt(map, pa.x, pa.y) - SinkM, yb = CityMeshes.LatticeAt(map, pb.x, pb.y) - SinkM;
            float h = c.h * hs;
            var a = new Vector3(pa.x, ya, pa.y) - origin; var b = new Vector3(pb.x, yb, pb.y) - origin;
            Quad(a, b, h, LitNormal(new Vector3(facing.x, 0f, facing.y), Strip), c);
        }

        /// <summary>The normal a card is lit by, between the ground's (straight
        /// up - the handoff: "the same scene light and colour tint as the
        /// neighbouring ground") and its own face's. Measured on I-77 at noon
        /// (Docs/shots/understory): face-lit, a verge strip read as a dark
        /// band; ground-lit, it vanished into the turf it was matched to.
        /// Grass 60/40 keeps a blade's shade; a shrub 50/50 its sunny side.</summary>
        static Vector3 LitNormal(Vector3 face, int cell) =>
            cell <= Switchgrass || cell == Strip ? (Vector3.up * 0.6f + face * 0.4f).normalized
                                                 : (Vector3.up * 0.5f + face * 0.5f).normalized;

        static void Crossed(CityMap map, Vector3 origin, Vector2 p, int cell, float scale, float yawDeg)
        {
            var c = Cells[cell];
            float y = CityMeshes.LatticeAt(map, p.x, p.y) - SinkM;
            var foot = new Vector3(p.x, y, p.y) - origin;
            float w = c.w * scale, h = c.h * scale;
            for (int q = 0; q < 2; q++)
            {
                float a = (yawDeg + q * 90f) * Mathf.Deg2Rad;
                var half = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (w * 0.5f);
                Quad(foot - half, foot + half, h, LitNormal(new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a)), cell), c);
            }
        }

        /// <summary>One upright card from a to b (feet), h tall, its lighting
        /// normal handed over (<see cref="LitNormal"/>).</summary>
        static void Quad(Vector3 a, Vector3 b, float h, Vector3 nrm, Cell c)
        {
            int v = vs.Count;
            vs.Add(a); vs.Add(a + Vector3.up * h); vs.Add(b + Vector3.up * h); vs.Add(b);
            ns.Add(nrm); ns.Add(nrm); ns.Add(nrm); ns.Add(nrm);
            float u0 = c.u0 + PadU, u1 = c.u1 - PadU, v0 = c.v0 + PadV, v1 = c.v1 - PadV;
            uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
            ts.Add(v); ts.Add(v + 1); ts.Add(v + 2);
            ts.Add(v); ts.Add(v + 2); ts.Add(v + 3);
        }

        /// <summary>The kit's understory material for a season dress
        /// (Winter, Spring, Summer, Fall, Snow); null without one.</summary>
        public static Material MaterialFor(int dress)
        {
            var kit = CityKit.Get();
            if (kit == null || kit.understory == null || kit.understory.Length == 0) return null;
            dress = Mathf.Clamp(dress, 0, kit.understory.Length - 1);
            return kit.understory[dress];
        }
    }
}
