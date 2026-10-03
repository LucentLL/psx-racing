using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  ROADS PASS L8 (plan B9/B10 lean): PARKING LOTS ON THE ROAD LAYER.
    //
    //  The surface lots OSM maps (section LOTS, tools/city/lib/lots.mjs: the
    //  road bands already taken out, so a lot never lies under a lane) are
    //  laid INTO the ground: every lattice triangle a lot covers is cut along
    //  the lot's ring, the part inside drawn in the junction slab's asphalt in
    //  the ROADS mesh (road layer, road grip, the fans' own slot: no new draw
    //  where the tile has a junction) and the rest left as ground. Inside, the
    //  stall separators (0.10 m) are cut out of the lot again and drawn from
    //  the solid white column of a two-lane street's texture. Every piece lies
    //  on its lattice triangle's plane: the lot is flush with the land round
    //  it and with the verge's foot (critic C5: no lift, no second surface).
    //
    //  ENTRANCES (section LENT): the street's verge across a lot's entrance is
    //  poured concrete - a curb-cut apron 7.3 m wide (4.3 m off a one-way)
    //  with 1.5 m wings at 45 degrees - where it was grass.
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool LotsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LOTS") != "0";
        public const float StallW = 2.74f, StallD = 5.49f, StallLineW = 0.10f;

        public static class LotStats
        {
            public static int lots, earcutFailed, skippedProp, aprons;
            public static long pieces, stallPieces;
            public static float lotM2, stallM2;
            public static void Reset() { lots = earcutFailed = skippedProp = aprons = 0; pieces = stallPieces = 0; lotM2 = stallM2 = 0f; }
        }

        static CityMap lotMapFor;
        static Dictionary<long, List<int>> lotsByTile, apronsByTile;
        static int[][] lotTris;
        static Vector2[][] lotStalls;     // 4 corners per separator, anticlockwise
        static Vector2[][] apronQuads;    // per entrance: the apron's trapezoid (4 corners)
        static int stallSlot = -1;
        static float stallU0, stallU1;

        static long LotTileKey(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

        /// <summary>The lots' tile index, triangulations and stall lines, and
        /// the aprons, once per map.</summary>
        static void EnsureLots(CityMap map)
        {
            if (lotMapFor == map && lotsByTile != null) return;
            lotMapFor = map;
            lotsByTile = new Dictionary<long, List<int>>();
            apronsByTile = new Dictionary<long, List<int>>();
            int nl = map.lots != null ? map.lots.Length : 0;
            lotTris = new int[nl][];
            lotStalls = new Vector2[nl][];
            for (int i = 0; i < nl; i++)
            {
                var b = map.lots[i].box;
                for (int tx = Mathf.FloorToInt(b.x / TileSize); tx <= Mathf.FloorToInt(b.z / TileSize); tx++)
                    for (int tz = Mathf.FloorToInt(b.y / TileSize); tz <= Mathf.FloorToInt(b.w / TileSize); tz++)
                    {
                        long k = LotTileKey(tx, tz);
                        if (!lotsByTile.TryGetValue(k, out var l)) lotsByTile[k] = l = new List<int>(4);
                        l.Add(i);
                    }
            }
            // the stall paint: the solid white column of the first two-way
            // profile whose texture has one (tw2: the commonest street)
            stallSlot = -1;
            for (int pass = 0; pass < 2 && stallSlot < 0; pass++)
                for (int p = 0; p < RoadProfiles.Count && stallSlot < 0; p++)
                {
                    var pr = RoadProfiles.All[p];
                    if (pass == 0 && pr.key != "tw2") continue;
                    var lay = LineModel.LayoutOf(p);
                    for (int k = 0; k < lay.m.Length; k++)
                        if (lay.kind[k] == LineModel.KEdgeM || (lay.kind[k] == LineModel.KEdgeP && lay.twoWay))
                        {
                            stallSlot = p;
                            stallU0 = (lay.m[k] - lay.half * 0.6f) / lay.W; stallU1 = (lay.m[k] + lay.half * 0.6f) / lay.W;
                            break;
                        }
                }
            // the aprons
            int na = map.lotEntrances != null ? map.lotEntrances.Length : 0;
            apronQuads = new Vector2[na][];
            for (int i = 0; i < na; i++)
            {
                var en = map.lotEntrances[i];
                var e = map.edges[en.edge];
                float s = Mathf.Clamp(en.s, 0f, e.length);
                var p = e.PointAt(s);
                var t = e.TangentAt(s);
                var left = new Vector2(-t.y, t.x);
                LineModel.Extents(e, s, out float eMinus, out float ePlus);
                float edgeOff = en.side > 0 ? ePlus : eMinus;
                var side = left * en.side;
                // the road edge (+ wings) to 2.5 m out, where the verge has met the lot
                float wing = 1.5f, reach = 2.5f;
                var a = p + side * edgeOff - t * (en.half + wing);
                var b = p + side * edgeOff + t * (en.half + wing);
                var c = p + side * (edgeOff + reach) + t * en.half;
                var d = p + side * (edgeOff + reach) - t * en.half;
                apronQuads[i] = new[] { a, b, c, d };
                float x0 = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), x1 = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
                float z0 = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)), z1 = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
                for (int tx = Mathf.FloorToInt(x0 / TileSize); tx <= Mathf.FloorToInt(x1 / TileSize); tx++)
                    for (int tz = Mathf.FloorToInt(z0 / TileSize); tz <= Mathf.FloorToInt(z1 / TileSize); tz++)
                    {
                        long k = LotTileKey(tx, tz);
                        if (!apronsByTile.TryGetValue(k, out var l)) apronsByTile[k] = l = new List<int>(4);
                        l.Add(i);
                    }
            }
        }

        /// <summary>The lots whose box meets tile (tx, tz) (RoadsideOccupancy:
        /// nothing is planted or stood in a lot).</summary>
        public static List<int> LotsInTile(CityMap map, int tx, int tz)
        {
            if (!LotsOn || map.lots == null || map.lots.Length == 0) return null;
            EnsureLots(map);
            return lotsByTile.TryGetValue(LotTileKey(tx, tz), out var l) ? l : null;
        }

        /// <summary>Is the plan point on a lot entrance's apron (the verge
        /// there is poured concrete)?</summary>
        static bool ApronAt(CityMap map, float x, float z)
        {
            if (!LotsOn || apronsByTile == null || lotMapFor != map) return false;
            if (!apronsByTile.TryGetValue(LotTileKey(Mathf.FloorToInt(x / TileSize), Mathf.FloorToInt(z / TileSize)), out var l)) return false;
            var q = new Vector2(x, z);
            foreach (int i in l)
            {
                var Q = apronQuads[i];
                bool inside = true;
                // convex, either winding: same side of every edge
                float s0 = 0f;
                for (int k = 0; k < 4 && inside; k++)
                {
                    var a = Q[k]; var b = Q[(k + 1) % 4];
                    float s = (b.x - a.x) * (q.y - a.y) - (b.y - a.y) * (q.x - a.x);
                    if (k == 0) s0 = s; else if (s * s0 < 0f) inside = false;
                }
                if (inside) return true;
            }
            return false;
        }

        /// <summary>A lot's ring as triangles (ear clipping, anticlockwise);
        /// null where the ring will not clip (listed).</summary>
        static int[] LotTriangles(CityMap map, int li)
        {
            var t = lotTris[li];
            if (t != null) return t.Length == 0 ? null : t;
            var ring = map.lots[li].ring;
            var idx = new List<int>(ring.Length);
            for (int i = 0; i < ring.Length; i++) idx.Add(i);
            var tris = new List<int>(3 * ring.Length);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4 * ring.Length * ring.Length)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = ring[ia], b = ring[ib], c = ring[ic];
                    if (Cross2(b - a, c - a) <= 1e-6f) continue;          // reflex or flat
                    bool empty = true;
                    for (int j = 0; j < idx.Count && empty; j++)
                    {
                        int ij = idx[j];
                        if (ij == ia || ij == ib || ij == ic) continue;
                        var p = ring[ij];
                        if (Cross2(b - a, p - a) >= 0f && Cross2(c - b, p - b) >= 0f && Cross2(a - c, p - c) >= 0f) empty = false;
                    }
                    if (!empty) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(i);
                    cut = true;
                    break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); idx.Clear(); }
            if (idx.Count > 0) { LotStats.earcutFailed++; lotTris[li] = new int[0]; return null; }
            lotTris[li] = tris.ToArray();
            return lotTris[li];
        }

        static Vector2[] LotStallQuads(CityMap map, int li)
        {
            var q = lotStalls[li];
            if (q != null) return q;
            var lot = map.lots[li];
            var u = lot.u; var v = new Vector2(-u.y, u.x);
            var list = new List<Vector2>();
            for (int r = 0; r < lot.runFoot.Length; r++)
                for (int k = 0; k <= lot.runN[r]; k++)
                {
                    var f = lot.runFoot[r] + u * (k * StallW);
                    var h = u * (0.5f * StallLineW);
                    // anticlockwise: u then v is anticlockwise (v = u's left)
                    list.Add(f - h); list.Add(f + h); list.Add(f + h + v * StallD); list.Add(f - h + v * StallD);
                }
            return lotStalls[li] = list.ToArray();
        }

        // ---- the lattice cut ------------------------------------------------
        static readonly List<int> tileLots = new List<int>(16);
        static readonly List<List<Vector3>> lotIn = new List<List<Vector3>>(16), lotOut = new List<List<Vector3>>(16), lotTmp = new List<List<Vector3>>(16);

        /// <summary>The lots this tile's ground meets (BuildGround, once per
        /// tile): any not overlapping a prop lot by a fifth of its box.</summary>
        static void PrepareTileLots(CityMap map, Vector2 min, Vector2 max)
        {
            tileLots.Clear();
            if (!LotsOn || map.lots == null || map.lots.Length == 0) return;
            EnsureLots(map);
            int tx = Mathf.FloorToInt((min.x + 1f) / TileSize), tz = Mathf.FloorToInt((min.y + 1f) / TileSize);
            if (!lotsByTile.TryGetValue(LotTileKey(tx, tz), out var l)) return;
            foreach (int i in l)
            {
                var b = map.lots[i].box;
                if (b.z < min.x || b.x > max.x || b.w < min.y || b.y > max.y) continue;
                if (OverPropLot(map.lots[i])) { continue; }
                tileLots.Add(i);
            }
        }

        /// <summary>A restaurant's own lot (a prop lot) covers a fifth or more
        /// of this lot's box: the prop's paving is the lot there.</summary>
        static bool OverPropLot(CityMap.Lot lot)
        {
            if (lampBuildings == null) return false;
            var b = lot.box;
            float area = Mathf.Max(1f, (b.z - b.x) * (b.w - b.y));
            int bx0 = Mathf.FloorToInt(b.x / TileSize) - 1, bx1 = Mathf.FloorToInt(b.z / TileSize) + 1;
            int bz0 = Mathf.FloorToInt(b.y / TileSize) - 1, bz1 = Mathf.FloorToInt(b.w / TileSize) + 1;
            for (int bx = bx0; bx <= bx1; bx++)
                for (int bz = bz0; bz <= bz1; bz++)
                {
                    if (!lampBuildings.TryGetValue(((long)bx << 24) ^ (bz & 0xFFFFFF), out var list)) continue;
                    foreach (var pb in list)
                    {
                        if (pb.kind == 0) continue;
                        float r = 0.5f * Mathf.Max(pb.w, pb.d) + RoadsideOccupancy.LotMarginOf(pb.kind);
                        float ox = Mathf.Max(0f, Mathf.Min(b.z, pb.pos.x + r) - Mathf.Max(b.x, pb.pos.x - r));
                        float oz = Mathf.Max(0f, Mathf.Min(b.w, pb.pos.y + r) - Mathf.Max(b.y, pb.pos.y - r));
                        if (ox * oz > 0.2f * area) { LotStats.skippedProp++; return true; }
                    }
                }
            return false;
        }

        /// <summary>
        /// One lattice cell (tile frame corners, heights on the lattice) laid
        /// with the lots cut into it: false when no lot touches it (the caller
        /// lays the plain quad). The cell's two triangles share the diagonal
        /// (0,0)-(1,1), as Bucket.Up draws it.
        /// </summary>
        static bool LotCell(CityMap map, TileMeshes tm, Vector3 p00, Vector3 p01, Vector3 p11, Vector3 p10, bool paved)
        {
            if (tileLots.Count == 0) return false;
            var o = tm.origin;
            float cx0 = p00.x + o.x, cz0 = p00.z + o.z, cx1 = p11.x + o.x, cz1 = p11.z + o.z;
            bool any = false;
            foreach (int li in tileLots)
            {
                var b = map.lots[li].box;
                if (b.z < cx0 || b.x > cx1 || b.w < cz0 || b.y > cz1) continue;
                any = true; break;
            }
            if (!any) return false;
            var gb = GroundBucket(paved);
            for (int tri = 0; tri < 2; tri++)
            {
                // anticlockwise in plan: (00, 10, 11) and (00, 11, 01)
                foreach (var pp in lotOut) FreePoly(pp);
                lotOut.Clear();
                var first = NewPoly();
                if (tri == 0) { first.Add(p00); first.Add(p10); first.Add(p11); }
                else { first.Add(p00); first.Add(p11); first.Add(p01); }
                lotOut.Add(first);
                foreach (int li in tileLots)
                {
                    var lot = map.lots[li];
                    var b = lot.box;
                    if (b.z < cx0 || b.x > cx1 || b.w < cz0 || b.y > cz1) continue;
                    var ring = lot.ring;
                    lotAcc.Clear();
                    // the ring's edges through the cell cut its pieces along
                    // their lines (only the pieces each edge really crosses), so
                    // no ring edge runs through any piece; each piece is then
                    // all lot or none of it, by its middle
                    var o2 = new Vector2(o.x, o.z);
                    bool crosses = false;
                    for (int k = 0; k < ring.Length; k++)
                    {
                        Vector2 a = ring[k], c2 = ring[(k + 1) % ring.Length];
                        if (!SegHitsBox(a, c2, cx0, cz0, cx1, cz1)) continue;
                        crosses = true;
                        Vector2 la = a - o2, lb = c2 - o2;
                        lotTmp.Clear();
                        foreach (var piece in lotOut)
                        {
                            if (!SegCrossesConvex(la, lb, piece)) { lotTmp.Add(piece); continue; }
                            var d = lb - la; var nrm = new Vector2(-d.y, d.x);
                            float c = Vector2.Dot(la, nrm);
                            SplitByLine(piece, nrm, c, cutLo, cutHi);
                            if (cutLo.Count >= 3 && PolyArea(cutLo) > 1e-6f) { var q = NewPoly(); q.AddRange(cutLo); lotTmp.Add(q); }
                            if (cutHi.Count >= 3 && PolyArea(cutHi) > 1e-6f) { var q = NewPoly(); q.AddRange(cutHi); lotTmp.Add(q); }
                            FreePoly(piece);
                        }
                        lotOut.Clear(); lotOut.AddRange(lotTmp);
                    }
                    if (!crosses)
                    {
                        if (!InRing(ring, new Vector2(0.5f * (cx0 + cx1), 0.5f * (cz0 + cz1)))) continue;
                        lotAcc.AddRange(lotOut); lotOut.Clear();
                    }
                    else
                    {
                        lotTmp.Clear();
                        foreach (var piece in lotOut)
                        {
                            var m = Vector3.zero; foreach (var q in piece) m += q; m /= piece.Count;
                            if (InRing(ring, new Vector2(m.x, m.z) + o2)) lotAcc.Add(piece); else lotTmp.Add(piece);
                        }
                        lotOut.Clear(); lotOut.AddRange(lotTmp);
                    }
                    if (lotAcc.Count == 0) continue;
                    // the stall separators out of the lot's pieces: each run's
                    // band (5.49 m along v) cut by its separators' sides along u
                    var sb = stallSlot >= 0 ? buckets[(int)SlotOf(stallSlot, IsFresh(ring[0]) ? Surface.AsphaltNew : Surface.AsphaltOld)] : null;
                    if (sb != null)
                        for (int r = 0; r < lot.runFoot.Length; r++)
                        {
                            var foot = lot.runFoot[r] - new Vector2(o.x, o.z);
                            float len = lot.runN[r] * StallW;
                            var far = foot + lot.u * len + new Vector2(-lot.u.y, lot.u.x) * StallD;
                            if (Mathf.Max(foot.x, far.x) + StallD < p00.x || Mathf.Min(foot.x, far.x) - StallD > p11.x ||
                                Mathf.Max(foot.y, far.y) + StallD < p00.z || Mathf.Min(foot.y, far.y) - StallD > p11.z) continue;
                            lotStallIn.Clear(); lotStallRest.Clear();
                            CutRun(lotAcc, foot, lot.u, lot.runN[r], lotStallIn, lotStallRest);
                            lotAcc.Clear(); lotAcc.AddRange(lotStallRest);
                            foreach (var piece in lotStallIn)
                            {
                                EmitFlat(sb, piece, o, true);
                                LotStats.stallPieces++; LotStats.stallM2 += PolyArea(piece);
                                FreePoly(piece);
                            }
                        }
                    var bk = buckets[(int)SlotOf(JunctionProfile, IsFresh(ring[0]) ? Surface.AsphaltNew : Surface.AsphaltOld)];
                    foreach (var piece in lotAcc)
                    {
                        EmitFlat(bk, piece, o, false);
                        LotStats.pieces++; LotStats.lotM2 += PolyArea(piece);
                        FreePoly(piece);
                    }
                    lotAcc.Clear();
                }
                // what no lot took: ground
                foreach (var piece in lotOut)
                {
                    int v0 = gb.v.Count;
                    foreach (var p in piece) { gb.v.Add(p); gb.uv.Add(GroundUV(paved, p.x + o.x, p.z + o.z)); }
                    for (int k = 1; k + 1 < piece.Count; k++) { gb.t.Add(v0); gb.t.Add(v0 + k + 1); gb.t.Add(v0 + k); }
                    FreePoly(piece);
                }
                lotOut.Clear();
            }
            return true;
        }
        static readonly List<List<Vector3>> lotStallIn = new List<List<Vector3>>(8), lotStallRest = new List<List<Vector3>>(8), lotAcc = new List<List<Vector3>>(16);
        static readonly List<Vector3> cutLo = new List<Vector3>(16), cutHi = new List<Vector3>(16);

        /// <summary>Does segment a-b meet the box (Liang-Barsky)?</summary>
        static bool SegHitsBox(Vector2 a, Vector2 b, float x0, float z0, float x1, float z1)
        {
            float t0 = 0f, t1 = 1f;
            var d = b - a;
            bool Clip(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-12f) return q >= 0f;
                float r = q / p;
                if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
            return Clip(-d.x, a.x - x0) && Clip(d.x, x1 - a.x) && Clip(-d.y, a.y - z0) && Clip(d.y, z1 - a.y) && t0 <= t1;
        }

        /// <summary>Does segment a-b run through the interior of the convex
        /// anticlockwise piece (Cyrus-Beck; along an edge is not through)?</summary>
        static bool SegCrossesConvex(Vector2 a, Vector2 b, List<Vector3> piece)
        {
            float t0 = 0f, t1 = 1f;
            var d = b - a;
            int n = piece.Count;
            for (int i = 0; i < n; i++)
            {
                var pi = piece[i]; var pj = piece[(i + 1) % n];
                Vector2 e = new Vector2(pj.x - pi.x, pj.z - pi.z);
                float el = e.magnitude;
                if (el < 1e-6f) continue;
                // inside: cross(e, P - pi) > eps
                float num = e.x * (a.y - pi.z) - e.y * (a.x - pi.x) - 1e-4f * el, den = e.x * d.y - e.y * d.x;
                if (Mathf.Abs(den) < 1e-12f) { if (num <= 0f) return false; continue; }
                float t = -num / den;
                if (den > 0f) t0 = Mathf.Max(t0, t); else t1 = Mathf.Min(t1, t);
                if (t0 >= t1) return false;
            }
            return t1 - t0 > 1e-6f;
        }

        static bool InRing(Vector2[] ring, Vector2 q)
        {
            bool c = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Vector2 a = ring[i], b = ring[j];
                if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x) c = !c;
            }
            return c;
        }

        /// <summary>A convex polygon split by the line dot(p, dir) = c: the
        /// part at or below into <paramref name="lo"/>, above into
        /// <paramref name="hi"/> (heights carried linearly).</summary>
        static void SplitByLine(List<Vector3> poly, Vector2 dir, float c, List<Vector3> lo, List<Vector3> hi)
        {
            lo.Clear(); hi.Clear();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                float fp = p.x * dir.x + p.z * dir.y - c, fq = q.x * dir.x + q.z * dir.y - c;
                if (fp <= 0f) lo.Add(p);
                if (fp >= 0f) hi.Add(p);
                if ((fp < 0f && fq > 0f) || (fp > 0f && fq < 0f))
                {
                    var x = Vector3.Lerp(p, q, fp / (fp - fq));
                    lo.Add(x); hi.Add(x);
                }
            }
        }

        /// <summary>One stall run cut out of convex pieces: its band (from the
        /// foot's v to 5.49 m along v = u's left) split along u at each
        /// separator's two sides; the separators into <paramref name="lines"/>,
        /// everything else into <paramref name="rest"/>. Linear in the
        /// separators (parallel cuts), where cutting each separator's quad
        /// out in turn multiplied the pieces.</summary>
        static void CutRun(List<List<Vector3>> pieces, Vector2 foot, Vector2 u, int n, List<List<Vector3>> lines, List<List<Vector3>> rest)
        {
            var v = new Vector2(-u.y, u.x);
            float v0 = Vector2.Dot(foot, v), v1 = v0 + StallD, u0 = Vector2.Dot(foot, u), h = 0.5f * StallLineW;
            float uA = u0 - h, uB = u0 + n * StallW + h;
            foreach (var piece in pieces)
            {
                float pu0 = float.MaxValue, pu1 = float.MinValue, pv0 = float.MaxValue, pv1 = float.MinValue;
                foreach (var p in piece)
                {
                    float pu = p.x * u.x + p.z * u.y, pv = p.x * v.x + p.z * v.y;
                    pu0 = Mathf.Min(pu0, pu); pu1 = Mathf.Max(pu1, pu); pv0 = Mathf.Min(pv0, pv); pv1 = Mathf.Max(pv1, pv);
                }
                if (pv1 <= v0 || pv0 >= v1 || pu1 <= uA || pu0 >= uB) { rest.Add(piece); continue; }
                var cur = NewPoly(); cur.AddRange(piece); FreePoly(piece);
                // below the band, above it
                void Keep(List<Vector3> part, List<List<Vector3>> into)
                {
                    if (part.Count >= 3 && Mathf.Abs(PolyArea(part)) > 1e-6f) { var q = NewPoly(); q.AddRange(part); into.Add(q); }
                }
                SplitByLine(cur, v, v0, cutLo, cutHi); Keep(cutLo, rest); cur.Clear(); cur.AddRange(cutHi);
                if (cur.Count >= 3) { SplitByLine(cur, v, v1, cutLo, cutHi); Keep(cutHi, rest); cur.Clear(); cur.AddRange(cutLo); }
                // the separators' sides, left to right
                for (int k = 0; k <= n && cur.Count >= 3; k++)
                {
                    float c = u0 + k * StallW;
                    SplitByLine(cur, u, c - h, cutLo, cutHi); Keep(cutLo, rest); cur.Clear(); cur.AddRange(cutHi);
                    if (cur.Count < 3) break;
                    SplitByLine(cur, u, c + h, cutLo, cutHi); Keep(cutLo, lines); cur.Clear(); cur.AddRange(cutHi);
                }
                Keep(cur, rest);
                FreePoly(cur);
            }
            pieces.Clear();
        }

        /// <summary>A piece faces up (anticlockwise in plan, so emitted
        /// reversed as the fans are); lot asphalt is world-planar at 12 m,
        /// a stall line the white column along its length.</summary>
        static void EmitFlat(Bucket bk, List<Vector3> piece, Vector3 o, bool stall)
        {
            int v0 = bk.v.Count;
            foreach (var p in piece)
            {
                bk.v.Add(p);
                bk.uv.Add(stall ? new Vector2(0.5f * (stallU0 + stallU1), (p.x + p.z + o.x + o.z) / 12f)
                                : new Vector2((p.x + o.x) / 12f, (p.z + o.z) / 12f));
            }
            for (int k = 1; k + 1 < piece.Count; k++) { bk.t.Add(v0); bk.t.Add(v0 + k + 1); bk.t.Add(v0 + k); }
        }

        /// <summary>Every piece of <paramref name="pieces"/> split by the
        /// anticlockwise triangle ABC: the parts inside into
        /// <paramref name="inside"/>, the rest (convex) into
        /// <paramref name="outside"/>. The input pieces are consumed.</summary>
        static void SplitConvex(List<List<Vector3>> pieces, Vector2 A, Vector2 B, Vector2 C, List<List<Vector3>> inside, List<List<Vector3>> outside)
        {
            splitEdges.Clear(); splitEdges.Add(A); splitEdges.Add(B); splitEdges.Add(C);
            SplitBy(pieces, inside, outside);
        }
        static void SplitConvexQuad(List<List<Vector3>> pieces, Vector2 A, Vector2 B, Vector2 C, Vector2 D, List<List<Vector3>> inside, List<List<Vector3>> outside)
        {
            splitEdges.Clear(); splitEdges.Add(A); splitEdges.Add(B); splitEdges.Add(C); splitEdges.Add(D);
            SplitBy(pieces, inside, outside);
        }
        static readonly List<Vector2> splitEdges = new List<Vector2>(4);
        static readonly List<Vector3> spA = new List<Vector3>(16), spB = new List<Vector3>(16);
        static void SplitBy(List<List<Vector3>> pieces, List<List<Vector3>> inside, List<List<Vector3>> outside)
        {
            int ne = splitEdges.Count;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var e in splitEdges) { minX = Mathf.Min(minX, e.x); maxX = Mathf.Max(maxX, e.x); minZ = Mathf.Min(minZ, e.y); maxZ = Mathf.Max(maxZ, e.y); }
            foreach (var piece in pieces)
            {
                float px0 = float.MaxValue, px1 = float.MinValue, pz0 = float.MaxValue, pz1 = float.MinValue;
                foreach (var p in piece) { px0 = Mathf.Min(px0, p.x); px1 = Mathf.Max(px1, p.x); pz0 = Mathf.Min(pz0, p.z); pz1 = Mathf.Max(pz1, p.z); }
                if (px1 < minX || px0 > maxX || pz1 < minZ || pz0 > maxZ) { outside.Add(piece); continue; }
                // inside: clipped by every edge
                spA.Clear(); spA.AddRange(piece);
                for (int k = 0; k < ne && spA.Count >= 3; k++)
                {
                    ClipHalfPlane(spA, splitEdges[k], splitEdges[(k + 1) % ne], true, spB);
                    spA.Clear(); spA.AddRange(spB);
                }
                if (spA.Count < 3 || PolyArea(spA) <= 1e-5f) { outside.Add(piece); continue; }
                var inP = NewPoly(); inP.AddRange(spA); inside.Add(inP);
                // outside: beyond each edge in turn
                var rem = NewPoly(); rem.AddRange(piece);
                for (int k = 0; k < ne && rem.Count >= 3; k++)
                {
                    var outP = NewPoly();
                    ClipHalfPlane(rem, splitEdges[k], splitEdges[(k + 1) % ne], false, outP);
                    if (outP.Count >= 3 && PolyArea(outP) > 1e-6f) outside.Add(outP); else FreePoly(outP);
                    ClipHalfPlane(rem, splitEdges[k], splitEdges[(k + 1) % ne], true, spB);
                    rem.Clear(); rem.AddRange(spB);
                }
                FreePoly(rem);
                FreePoly(piece);
            }
            pieces.Clear();
        }

        /// <summary>For the LOTS report: every lot in the box laid as the tile
        /// builder lays it is not checked here; this is the data's census.</summary>
        public static void LotCensus(CityMap map, Trims trims, out int lots, out int noTris, out int stallLines, out float ringM2)
        {
            lots = noTris = stallLines = 0; ringM2 = 0f;
            if (map.lots == null) return;
            EnsureLots(map);
            for (int i = 0; i < map.lots.Length; i++)
            {
                lots++;
                if (LotTriangles(map, i) == null) noTris++;
                stallLines += LotStallQuads(map, i).Length / 4;
                var r = map.lots[i].ring;
                float a = 0f;
                for (int k = 0; k < r.Length; k++) { var p = r[k]; var q = r[(k + 1) % r.Length]; a += p.x * q.y - q.x * p.y; }
                ringM2 += 0.5f * a;
            }
        }
    }
}
