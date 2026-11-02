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
    ///   OSM classes 4-5): two staggered rows of tall grass at the clear
    ///   zone's outer edge (low grass may stand in a clear zone; the mown
    ///   shoulder runs up to it), fescue and switchgrass clumps beyond them,
    ///   and THE SHRUB BELT behind them in the clear zone's outer half (low
    ///   breakaway shrubs) - clusters of two to four about every 13 m a side,
    ///   mountain laurel where the canopy map says the
    ///   edge is wooded (<see cref="CityCanopy"/>), inkberry and azalea where
    ///   it is open.
    ///   THE MEDIANS AND INFIELDS: free ground lying between two opposed
    ///   carriageways of a secondary road or bigger - a divided road's planted
    ///   median (inside its clear zone on an arterial, beyond it on a freeway),
    ///   an interchange's loop - gets inkberry and azalea.
    ///   THE ARTERIALS' ROADSIDES: a shrub (a third of the time a pair) about
    ///   every 18 m a side on free ground 2.5-6 m off the kerb of a secondary
    ///   or primary road.
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
        /// <summary>Medians: the chance a 4 m cell plants a shrub.</summary>
        const float MedianPerCell = 0.5f;
        /// <summary>The freeway shrub belt: the chance a verge step (2.3 m)
        /// starts a cluster - about one every 13 m a side.</summary>
        const float BeltPerStep = 0.18f;
        /// <summary>The arterials' accents: a step along the road, and the
        /// chance it plants a shrub or a pair a side (about every 18 m).</summary>
        const float AccentStepM = 8f, AccentPerStep = 0.45f;
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
        public static int StatBeltTried, StatAccentTried, StatRejRoad;
        /// <summary>The tools' list of placed roadside accents (null: not kept).</summary>
        public static List<Vector2> DebugAccents;
        static readonly int[] statRejBit = new int[8];
        static void Reject(byte bits) { for (int i = 0; i < 8; i++) if ((bits & (1 << i)) != 0) { statRejBit[i]++; return; } }
        public static string Stats
        {
            get
            {
                var sb = new System.Text.StringBuilder($"pieces {StatPieces}, strips {StatStrips}, clumps {StatClumps}, shrubs {StatShrubs}; strip refusals: other tile {StatNotMine}, mask {StatForbid}, on a road {StatOnRoad}; shrub tries belt {StatBeltTried} accent {StatAccentTried}, refused near a road {StatRejRoad}, by mask:");
                for (int i = 0; i < 8; i++) if (statRejBit[i] > 0) sb.Append(' ').Append(RoadsideOccupancy.BitNames[i]).Append(' ').Append(statRejBit[i]);
                return sb.ToString();
            }
        }

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
                        // a second row behind it, half a step along (the owner: a single
                        // strip reads as a pixel line; two staggered rows read as a band)
                        {
                            var mid2 = c + d * (StripStepM * 0.5f) + nrm * (side * (off + 1.3f));
                            var pa2 = mid2 - half; var pb2 = mid2 + half;
                            if (Mine(mid2, min) && Free(occ, pa2) && Free(occ, mid2) && Free(occ, pb2) && !OnAnotherRoad(occ, mid2))
                            {
                                StripCard(map, origin, pa2, pb2, -side * nrm, 0.75f + 0.3f * CityTrees.Hash01(pc.edge, st, 13 + salt));
                                ut.strips++;
                            }
                        }
                        // THE SHRUB BELT (the owner, 2026-11-02: the shrubs and bushes
                        // were "the main addition I wanted"): a cluster of two to four
                        // behind the grass about every 13 m a side - mountain
                        // laurel where the edge is wooded, inkberry and some azalea
                        // where it is open
                        if (CityTrees.Hash01(pc.edge, st, 9 + salt) < BeltPerStep * density)
                        {
                            float canopy = CityCanopy.Loaded ? CityCanopy.CellFraction(c.x, c.y) : 0f;
                            int k = 2 + Mathf.FloorToInt(CityTrees.Hash01(pc.edge, st, 10 + salt) * 2.99f);
                            float along = (CityTrees.Hash01(pc.edge, st, 11 + salt) - 0.5f) * StripStepM;
                            // right behind the grass, in the clear zone's outer half at least
                            // (low breakaway shrubs; Charlotte's freeway verges are often all
                            // clear zone up to the frontage road or the fence)
                            float back = Mathf.Max(off + 2.2f, pc.hw + pc.clear * 0.5f) + 2.5f * CityTrees.Hash01(pc.edge, st, 12 + salt);
                            for (int j = 0; j < k; j++)
                            {
                                var q = c + d * (along + (j - (k - 1) * 0.5f) * (1.6f + 0.8f * CityTrees.Hash01(pc.edge, st, 20 + j + salt)))
                                      + nrm * (side * (back + 1.2f * CityTrees.Hash01(pc.edge, st, 30 + j + salt)));
                                StatBeltTried++;
                                if (!Mine(q, min)) continue;
                                byte qb = occ.At(q);
                                if ((qb & Forbid) != 0) { Reject(qb); continue; }
                                if (OnAnotherRoad(occ, q)) { StatRejRoad++; continue; }
                                float r = CityTrees.Hash01(pc.edge, st, 40 + j + salt);
                                int cell = canopy >= 0.25f ? (r < 0.7f ? Laurel : Inkberry)
                                         : r < 0.65f ? Inkberry : r < 0.85f ? Azalea : Laurel;
                                Crossed(map, origin, q, cell, 0.8f + 0.4f * CityTrees.Hash01(pc.edge, st, 50 + j + salt), CityTrees.Hash01(pc.edge, st, 60 + j + salt) * 180f);
                                ut.shrubs++;
                            }
                        }
                    }
                }
            }

            // THE ARTERIALS' ROADSIDES: a shrub now and then on free ground a few
            // metres off the kerb of a secondary or primary road (a landscaped
            // frontage, a business's bed)
            foreach (var pc in occ.pieces)
            {
                if (pc.deck) continue;
                int cls = map.edges[pc.edge].cls;
                if (cls < 2 || cls > 3) continue;
                var d = pc.b - pc.a;
                float L = d.magnitude;
                if (L < 0.1f) continue;
                d /= L;
                var nrm = new Vector2(-d.y, d.x);
                for (float s = Mathf.Ceil(pc.sa / AccentStepM) * AccentStepM; s < pc.sb; s += AccentStepM)
                {
                    int st = Mathf.RoundToInt(s / AccentStepM);
                    var c = pc.a + d * (s - pc.sa);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int salt = side < 0 ? 300 : 400;
                        if (CityTrees.Hash01(pc.edge, st, salt) >= AccentPerStep * density) continue;
                        var q = c + nrm * (side * (pc.hw + 2.5f + 3.5f * CityTrees.Hash01(pc.edge, st, 1 + salt)));
                        StatAccentTried++;
                        if (!Mine(q, min)) continue;
                        byte qb = occ.At(q);
                        if ((qb & Forbid) != 0) { Reject(qb); continue; }
                        if (occ.RoadEdgeDistance(q, 4f, out _, out _, out _) < 2.5f) { StatRejRoad++; continue; }
                        if (DebugAccents != null && DebugAccents.Count < 50000) DebugAccents.Add(q);
                        int cell = CityTrees.Hash01(pc.edge, st, 2 + salt) < 0.55f ? Inkberry : Azalea;
                        Crossed(map, origin, q, cell, 0.7f + 0.4f * CityTrees.Hash01(pc.edge, st, 3 + salt), CityTrees.Hash01(pc.edge, st, 4 + salt) * 180f);
                        ut.shrubs++;
                        // a pair, a third of the time: a bed, not a lone plant
                        if (CityTrees.Hash01(pc.edge, st, 5 + salt) < 0.35f)
                        {
                            var q2 = q + d * (1.6f + 0.8f * CityTrees.Hash01(pc.edge, st, 6 + salt));
                            if (Mine(q2, min) && (occ.At(q2) & Forbid) == 0 && occ.RoadEdgeDistance(q2, 4f, out _, out _, out _) >= 2.5f)
                            {
                                Crossed(map, origin, q2, cell, 0.7f + 0.4f * CityTrees.Hash01(pc.edge, st, 7 + salt), CityTrees.Hash01(pc.edge, st, 8 + salt) * 180f);
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
                    if (CityTrees.Hash01(gx, gz, 50) >= MedianPerCell * density) continue;
                    var p = min + new Vector2((x + CityTrees.Hash01(gx, gz, 51)) * MedianCellM, (z + CityTrees.Hash01(gx, gz, 52)) * MedianCellM);
                    int between = BetweenRoads(map, occ, p);
                    if (between == 0) continue;
                    // a planted median on an arterial (45 mph and under) has its shrubs
                    // inside the clear zone; a freeway's stay beyond it
                    byte bits = occ.At(p);
                    if (between == 2 ? bits != 0 : (bits & Forbid) != 0) continue;
                    if (OnAnotherRoad(occ, p)) continue;
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

        /// <summary>A median or an infield: two grounded carriageways of a
        /// secondary road or bigger within reach, on opposite sides of the
        /// point - 0 none, 1 arterials only, 2 a freeway or ramp among them.</summary>
        static int BetweenRoads(CityMap map, RoadsideOccupancy occ, Vector2 p)
        {
            near.Clear();
            occ.NearRoads(p, MedianReachM, near);
            int found = 0;
            for (int i = 0; i < near.Count; i++)
            {
                var a = near[i];
                int ca = map.edges[a.edge].cls;
                if (a.deck || ca < 2) continue;
                for (int j = i + 1; j < near.Count; j++)
                {
                    var b = near[j];
                    int cb = map.edges[b.edge].cls;
                    if (b.deck || b.edge == a.edge || cb < 2) continue;
                    if (Vector2.Dot(a.away, b.away) >= -0.5f) continue;
                    found = Mathf.Max(found, ca >= 4 || cb >= 4 ? 2 : 1);
                }
            }
            return found;
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
