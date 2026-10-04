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
    //
    //  LEFTOVER ITEM 4 (LOTS v2, 2026-10-03): the aisles are OSM's own
    //  (service=parking_aisle, paved even where no lot polygon reaches) and
    //  each stall row stands along its aisle (a run's own direction); a lot
    //  has HOLES - the buildings inside it and OSM's inner rings (the ground
    //  shows) - and ISLANDS: raised curbed grass (0.15 m) where OSM maps one
    //  and at a row's end against the lot's edge. An island's top is the
    //  lattice's own pieces lifted, in the ground mesh's grass (its paving
    //  where the tile has no grass); its curb is a vertical face round it in
    //  the pavement concrete (or the structural concrete where the tile has
    //  no pavement): at most +1 draw a tile. A lot piece under a junction's
    //  fan or on a road's drawn pavement is left to the road (cut along the
    //  fan's ring and the ribbon's edge, LotRoadCutOn).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool LotsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LOTS") != "0";
        /// <summary>Leftover item 4: a lot piece under a junction fan or on a
        /// road's drawn pavement (its aux lanes and turn bays too, which the
        /// export's road band does not know) is the road's: the lot is cut
        /// along the fan's ring and the ribbon's edge (PSX_CITY_LOTROADCUT=0:
        /// laid as L8 did, under them).</summary>
        public static bool LotRoadCutOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LOTROADCUT") != "0";
        public const float StallW = 2.74f, StallD = 5.49f, StallLineW = 0.10f;
        /// <summary>An island's top over the lot (a 6 in curb).</summary>
        public const float IslandH = 0.15f;

        public static class LotStats
        {
            public static int lots, earcutFailed, skippedProp, aprons, islands;
            public static long pieces, stallPieces, fanCutPieces;
            public static float lotM2, stallM2, islandM2, fanCutM2;
            /// <summary>Stopwatch ticks in the lots' own work (cells, bands, islands).</summary>
            public static long ticks;
            public static void Reset() { lots = earcutFailed = skippedProp = aprons = islands = 0; pieces = stallPieces = fanCutPieces = ticks = 0; lotM2 = stallM2 = islandM2 = fanCutM2 = 0f; }
        }

        static CityMap lotMapFor;
        static Dictionary<long, List<int>> lotsByTile, apronsByTile;
        static int[][] lotTris;
        static Vector2[][] lotStalls;     // 4 corners per separator, anticlockwise
        static Vector2[][] apronQuads;    // per entrance: the apron's trapezoid (4 corners)
        static Vector4[][] lotHoleBox;    // per lot, per hole: x0, z0, x1, z1
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
            lotHoleBox = new Vector4[nl][];
            for (int i = 0; i < nl; i++)
            {
                var hs = map.lots[i].holes;
                var hb = new Vector4[hs.Length];
                for (int h = 0; h < hs.Length; h++)
                {
                    float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                    foreach (var q in hs[h]) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
                    hb[h] = new Vector4(x0, z0, x1, z1);
                }
                lotHoleBox[i] = hb;
            }
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
            var list = new List<Vector2>();
            for (int r = 0; r < lot.runFoot.Length; r++)
                for (int k = 0; k <= lot.runN[r]; k++)
                {
                    var u = lot.runU != null ? lot.runU[r] : lot.u; var v = new Vector2(-u.y, u.x);
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
            foreach (var pp in islandTops) FreePoly(pp);
            islandTops.Clear();
            tileBands.Clear(); lotFans.Clear();
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
            if (tileLots.Count > 0 && LotRoadCutOn && fanFloorTrims != null)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                BuildLotBands(map, fanFloorTrims, min, max);
                // only the bands and fans that meet one of this tile's lots
                tileBands.RemoveAll(bd => !MeetsTileLot(map, bd.x0, bd.z0, bd.x1, bd.z1));
                lotFans.Clear();
                foreach (var rec in fanFloors) if (MeetsTileLot(map, rec.x0, rec.z0, rec.x1, rec.z1)) lotFans.Add(rec);
                LotStats.ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            }
        }
        static readonly List<FanFloorRec> lotFans = new List<FanFloorRec>(32);
        static bool MeetsTileLot(CityMap map, float x0, float z0, float x1, float z1)
        {
            foreach (int li in tileLots)
            {
                var b = map.lots[li].box;
                if (b.z >= x0 && b.x <= x1 && b.w >= z0 && b.y <= z1) return true;
            }
            return false;
        }

        // ---- the roads' drawn pavement, for cutting the lots (leftover item 4)
        struct LotBand { public Vector2[] poly; public float x0, z0, x1, z1; }
        static readonly List<LotBand> tileBands = new List<LotBand>(256);
        static readonly HashSet<int> bandSegs = new HashSet<int>();
        static readonly SortedDictionary<int, List<Vector2>> bandRanges = new SortedDictionary<int, List<Vector2>>();
        const float BandStepM = 2f, BandPadM = 0.15f; const int BandChunk = 10;

        /// <summary>Every grounded ribbon near the box as chunks of its drawn
        /// pavement (<see cref="LineModel.Extents"/> either side + 15 cm,
        /// between the edge's trims, every 2 m, ten steps a chunk) into
        /// <see cref="tileBands"/>.</summary>
        static void BuildLotBands(CityMap map, Trims trims, Vector2 min, Vector2 max)
        {
            tileBands.Clear();
            bandSegs.Clear(); bandRanges.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 24f, max + Vector2.one * 24f, bandSegs);
            foreach (int packed in bandSegs)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel || si + 1 >= e.pts.Length || trims.Internal(ei)) continue;
                if (!bandRanges.TryGetValue(ei, out var rl)) bandRanges[ei] = rl = new List<Vector2>(4);
                rl.Add(new Vector2(e.s[si], e.s[si + 1]));
            }
            var L = new List<Vector2>(BandChunk + 1); var R = new List<Vector2>(BandChunk + 1);
            foreach (var kv in bandRanges)
            {
                var e = map.edges[kv.Key];
                float sA = trims.atA[kv.Key], sB = e.length - trims.atB[kv.Key];
                var rl = kv.Value;
                rl.Sort((a, b) => a.x.CompareTo(b.x));
                // merge the touching ranges, then sample each
                int i = 0;
                while (i < rl.Count)
                {
                    float r0 = rl[i].x, r1 = rl[i].y;
                    int j = i + 1;
                    while (j < rl.Count && rl[j].x <= r1 + 1e-3f) { r1 = Mathf.Max(r1, rl[j].y); j++; }
                    i = j;
                    r0 = Mathf.Max(r0, sA); r1 = Mathf.Min(r1, sB);
                    if (r1 - r0 < 0.25f) continue;
                    int n = Mathf.Max(1, Mathf.CeilToInt((r1 - r0) / BandStepM));
                    L.Clear(); R.Clear();
                    for (int k = 0; k <= n; k++)
                    {
                        float sk = r0 + (r1 - r0) * k / n;
                        bool up = e.ElevatedAt(sk);
                        if (!up)
                        {
                            var pk = e.PointAt(sk); var tk = e.TangentAt(sk); var left = new Vector2(-tk.y, tk.x);
                            LineModel.Extents(e, sk, out float eM, out float eP);
                            L.Add(pk + left * (eP + BandPadM)); R.Add(pk - left * (eM + BandPadM));
                        }
                        if ((up || L.Count > BandChunk || k == n) && L.Count >= 2)
                        {
                            var poly = new Vector2[2 * L.Count];
                            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                            for (int q = 0; q < L.Count; q++) { poly[q] = L[q]; poly[2 * L.Count - 1 - q] = R[q]; }
                            foreach (var v in poly) { x0 = Mathf.Min(x0, v.x); z0 = Mathf.Min(z0, v.y); x1 = Mathf.Max(x1, v.x); z1 = Mathf.Max(z1, v.y); }
                            tileBands.Add(new LotBand { poly = poly, x0 = x0, z0 = z0, x1 = x1, z1 = z1 });
                            var lastL = L[L.Count - 1]; var lastR = R[R.Count - 1];
                            L.Clear(); R.Clear();
                            if (!up) { L.Add(lastL); R.Add(lastR); }
                        }
                        else if (up) { L.Clear(); R.Clear(); }
                    }
                }
            }
        }

        static bool OnBand(Vector2 q)
        {
            foreach (var b in tileBands)
            {
                if (q.x < b.x0 || q.x > b.x1 || q.y < b.z0 || q.y > b.z1) continue;
                if (InRing(b.poly, q)) return true;
            }
            return false;
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
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool r = LotCellBody(map, tm, p00, p01, p11, p10, paved);
            LotStats.ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            return r;
        }
        static bool LotCellBody(CityMap map, TileMeshes tm, Vector3 p00, Vector3 p01, Vector3 p11, Vector3 p10, bool paved)
        {
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
                var o2 = new Vector2(o.x, o.z);
                // a junction's fan and a road's pavement take what lies under
                // them: the pieces cut along every fan ring and ribbon band
                // through the cell (classified per lot)
                bool fanCut = false;
                if (LotRoadCutOn)
                {
                    foreach (var rec in lotFans)
                    {
                        if (rec.x1 < cx0 || rec.x0 > cx1 || rec.z1 < cz0 || rec.z0 > cz1) continue;
                        if (CutPiecesAlong(rec.ring, cx0, cz0, cx1, cz1, o2)) fanCut = true;
                    }
                    foreach (var bd in tileBands)
                    {
                        if (bd.x1 < cx0 || bd.x0 > cx1 || bd.z1 < cz0 || bd.z0 > cz1) continue;
                        if (CutPiecesAlong(bd.poly, cx0, cz0, cx1, cz1, o2)) fanCut = true;
                    }
                }
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
                    // all lot or none of it, by its middle - and so are its
                    // holes' and islands' edges
                    bool crosses = CutPiecesAlong(ring, cx0, cz0, cx1, cz1, o2) | fanCut;
                    var hbs = lotHoleBox[li];
                    for (int h = 0; h < hbs.Length; h++)
                    {
                        var hb = hbs[h];
                        if (hb.z < cx0 || hb.x > cx1 || hb.w < cz0 || hb.y > cz1) continue;
                        if (CutPiecesAlong(lot.holes[h], cx0, cz0, cx1, cz1, o2)) crosses = true;
                    }
                    lotTmp.Clear();
                    if (!crosses)
                    {
                        int cls = LotClass(map, li, new Vector2(0.5f * (cx0 + cx1), 0.5f * (cz0 + cz1)), LotRoadCutOn);
                        if (cls == 0) continue;
                        foreach (var piece in lotOut) TakeLotPiece(piece, cls, o);
                        lotOut.Clear();
                    }
                    else
                    {
                        foreach (var piece in lotOut)
                        {
                            var m = Vector3.zero; foreach (var q in piece) m += q; m /= piece.Count;
                            int cls = LotClass(map, li, new Vector2(m.x, m.z) + o2, fanCut);
                            if (cls == 0) lotTmp.Add(piece); else TakeLotPiece(piece, cls, o);
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
                            var ru = lot.runU != null ? lot.runU[r] : lot.u;
                            var foot = lot.runFoot[r] - new Vector2(o.x, o.z);
                            float len = lot.runN[r] * StallW;
                            var far = foot + ru * len + new Vector2(-ru.y, ru.x) * StallD;
                            if (Mathf.Max(foot.x, far.x) + StallD < p00.x || Mathf.Min(foot.x, far.x) - StallD > p11.x ||
                                Mathf.Max(foot.y, far.y) + StallD < p00.z || Mathf.Min(foot.y, far.y) - StallD > p11.z) continue;
                            lotStallIn.Clear(); lotStallRest.Clear();
                            CutRun(lotAcc, foot, ru, lot.runN[r], lotStallIn, lotStallRest);
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
        /// <summary>The islands' tops cut this tile (tile frame, lifted),
        /// drawn with their curbs at the end of the build (<see cref="FlushLotIslands"/>).</summary>
        static readonly List<List<Vector3>> islandTops = new List<List<Vector3>>(64);

        /// <summary>The pieces of <see cref="lotOut"/> cut along every edge of
        /// <paramref name="ring"/> that meets the cell (world plan; the pieces
        /// in the tile frame, <paramref name="o2"/> its origin). True when one did.</summary>
        static bool CutPiecesAlong(Vector2[] ring, float cx0, float cz0, float cx1, float cz1, Vector2 o2)
        {
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
            return crosses;
        }

        /// <summary>What a plan point of lot <paramref name="li"/> is: 0 not
        /// the lot's (outside its ring, in a hole, or - <paramref name="fans"/> -
        /// under a junction fan), 1 the lot's pavement, 2 an island's top.</summary>
        static int LotClass(CityMap map, int li, Vector2 q, bool fans)
        {
            var lot = map.lots[li];
            if (!InRing(lot.ring, q)) return 0;
            var hbs = lotHoleBox[li];
            for (int h = 0; h < hbs.Length; h++)
            {
                var hb = hbs[h];
                if (q.x < hb.x || q.x > hb.z || q.y < hb.y || q.y > hb.w) continue;
                if (InRing(lot.holes[h], q)) return lot.holeKind[h] == 1 ? 2 : 0;
            }
            if (fans && (OnFan(q) || OnBand(q))) { LotStats.fanCutPieces++; return 0; }
            return 1;
        }

        /// <summary>Is the plan point on a junction fan near this tile
        /// (<see cref="fanFloors"/>, PrepareFanFloor's)?</summary>
        static bool OnFan(Vector2 q)
        {
            foreach (var rec in fanFloors)
            {
                if (q.x < rec.x0 || q.x > rec.x1 || q.y < rec.z0 || q.y > rec.z1) continue;
                if (InRing(rec.ring, q)) return true;
            }
            return false;
        }

        static void TakeLotPiece(List<Vector3> piece, int cls, Vector3 o)
        {
            if (cls == 1) { lotAcc.Add(piece); return; }
            // an island's top: the lattice's own piece, lifted
            for (int k = 0; k < piece.Count; k++) piece[k] += new Vector3(0f, IslandH, 0f);
            LotStats.islandM2 += PolyArea(piece);
            islandTops.Add(piece);
        }

        /// <summary>
        /// The islands, once the tile's other surfaces are in (leftover item
        /// 4): their tops into the ground's grass (its paving where this tile
        /// has no grass), and the curbs - a face from 5 cm under the lattice
        /// to 1 cm over the top, every 0.5 m along the island's ring - into
        /// the pavement concrete (the structural concrete where the tile has
        /// no pavement, the pavement where it has neither). An island's curb
        /// is drawn by the tile that holds its middle.
        /// </summary>
        static void FlushLotIslands(CityMap map, TileMeshes tm, Vector2 min)
        {
            if (!LotsOn || map.lots == null) return;
            var o = tm.origin;
            bool grass = buckets[(int)Slot.Ground].Count > 0 || buckets[(int)Slot.Pavement].Count == 0;
            var top = buckets[(int)(grass ? Slot.Ground : Slot.Pavement)];
            foreach (var piece in islandTops)
            {
                int v0 = top.v.Count;
                foreach (var p in piece) { top.v.Add(p); top.uv.Add(GroundUV(!grass, p.x + o.x, p.z + o.z)); }
                for (int k = 1; k + 1 < piece.Count; k++) { top.t.Add(v0); top.t.Add(v0 + k + 1); top.t.Add(v0 + k); }
                FreePoly(piece);
            }
            islandTops.Clear();
            var curbSlot = buckets[(int)Slot.Pavement].Count > 0 ? Slot.Pavement : buckets[(int)Slot.Concrete].Count > 0 ? Slot.Concrete : Slot.Pavement;
            var curb = buckets[(int)curbSlot];
            foreach (int li in tileLots)
            {
                var lot = map.lots[li];
                for (int h = 0; h < lot.holes.Length; h++)
                {
                    if (lot.holeKind[h] != 1) continue;
                    var hb = lotHoleBox[li][h];
                    float mx = 0.5f * (hb.x + hb.z), mz = 0.5f * (hb.y + hb.w);
                    if (mx < min.x || mx >= min.x + TileSize || mz < min.y || mz >= min.y + TileSize) continue;
                    EmitIslandCurb(map, curb, lot.holes[h], o);
                    LotStats.islands++;
                }
            }
        }

        static void EmitIslandCurb(CityMap map, Bucket bk, Vector2[] ring, Vector3 o)
        {
            int n = ring.Length;
            float area = 0f;
            for (int i = 0; i < n; i++) { var p = ring[i]; var q = ring[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            bool ccw = area > 0f;
            float sAcc = 0f;
            for (int i = 0; i < n; i++)
            {
                // anticlockwise: the island on the left, the face looking out (right)
                Vector2 a = ccw ? ring[i] : ring[(n - i) % n], b = ccw ? ring[(i + 1) % n] : ring[(2 * n - i - 1) % n];
                float len = (b - a).magnitude;
                if (len < 1e-3f) continue;
                int k = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
                for (int j = 0; j < k; j++)
                {
                    var p = Vector2.Lerp(a, b, j / (float)k); var q = Vector2.Lerp(a, b, (j + 1) / (float)k);
                    float yp = LatticeY(map, p.x, p.y), yq = LatticeY(map, q.x, q.y);
                    float s0 = sAcc + len * j / k, s1 = sAcc + len * (j + 1) / k;
                    int v0 = bk.v.Count;
                    bk.v.Add(new Vector3(p.x - o.x, yp - 0.05f, p.y - o.z)); bk.uv.Add(new Vector2(s0 / 6f, 0f));
                    bk.v.Add(new Vector3(p.x - o.x, yp + IslandH + 0.01f, p.y - o.z)); bk.uv.Add(new Vector2(s0 / 6f, 0.035f));
                    bk.v.Add(new Vector3(q.x - o.x, yq + IslandH + 0.01f, q.y - o.z)); bk.uv.Add(new Vector2(s1 / 6f, 0.035f));
                    bk.v.Add(new Vector3(q.x - o.x, yq - 0.05f, q.y - o.z)); bk.uv.Add(new Vector2(s1 / 6f, 0f));
                    bk.t.Add(v0); bk.t.Add(v0 + 1); bk.t.Add(v0 + 2);
                    bk.t.Add(v0); bk.t.Add(v0 + 2); bk.t.Add(v0 + 3);
                }
                sAcc += len;
            }
        }
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

        // ---- THE LOT AUDIT (leftover item 4) ----------------------------------
        public sealed class LotAuditResult
        {
            public int lots, aisleLots, islands, holes, runs, stalls, lotsWithOverlap;
            /// <summary>m2: lot pavement as drawn; under a fan (cut out when
            /// <see cref="LotRoadCutOn"/>, else drawn under it); on a road's
            /// ribbon; inside a real footprint; inside a placed (procedural or
            /// prop) building; on another lot's pavement; island tops.</summary>
            public double lotM2, fanM2, ribbonM2, bldM2, procM2, lotLotM2, islandM2, bandCutM2;
            public readonly List<string> worst = new List<string>();
            /// <summary>Drawn lot pavement over a road, a fan, a building or another lot.</summary>
            public double OverlapM2(bool fanCut) => (fanCut ? 0.0 : fanM2) + ribbonM2 + bldM2 + procM2 + lotLotM2;
        }

        /// <summary>
        /// THE LOT AUDIT: every lot meeting <paramref name="box"/> (not one under
        /// a restaurant's own lot: not drawn) sampled every 0.5 m inside the box
        /// as the tile builder classifies it - its ring less its holes, islands
        /// apart - and each pavement sample tested against what else is there:
        /// a junction fan (PrepareFanFloor's rings), a road's drawn ribbon (the
        /// edge's centreline, its trims and LineModel.Extents either side; not
        /// a tunnel, not over a deck), a real footprint (its polygon; a gabled
        /// house's box too), a placed building (CityBuildings' oriented box)
        /// and an earlier lot's pavement. Gate: drawn overlaps 0.
        /// </summary>
        public static LotAuditResult LotOverlapAudit(CityMap map, Trims trims, Rect box, Dictionary<long, List<CityBuildings.B>> proc)
        {
            var A = new LotAuditResult();
            if (!LotsOn || map.lots == null || map.lots.Length == 0) return A;
            EnsureLots(map);
            var prevLamp = lampBuildings;
            if (proc != null) lampBuildings = proc;
            var segs = new HashSet<int>(); var segList = new List<int>();
            var foots = new List<int>(); var procs = new List<CityBuildings.B>(); var others = new List<int>();
            var rows = new List<KeyValuePair<double, string>>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            const float G = 0.5f; const double CellA = G * G;
            for (int li = 0; li < map.lots.Length; li++)
            {
                var L = map.lots[li]; var b = L.box;
                if (b.z < box.xMin || b.x > box.xMax || b.w < box.yMin || b.y > box.yMax) continue;
                if (OverPropLot(L)) continue;
                A.lots++; if (L.aisleOnly) A.aisleLots++;
                foreach (var k in L.holeKind) { if (k == 1) A.islands++; else A.holes++; }
                A.runs += L.runN.Length; foreach (var c in L.runN) A.stalls += c;
                var min = new Vector2(Mathf.Max(b.x, box.xMin), Mathf.Max(b.y, box.yMin));
                var max = new Vector2(Mathf.Min(b.z, box.xMax), Mathf.Min(b.w, box.yMax));
                PrepareFanFloor(map, trims, min, max);
                if (LotRoadCutOn) BuildLotBands(map, trims, min, max); else tileBands.Clear();
                segs.Clear(); map.EdgeSegsInRect(min - Vector2.one * 40f, max + Vector2.one * 40f, segs);
                segList.Clear(); segList.AddRange(segs);
                foots.Clear(); procs.Clear();
                for (int tx = Mathf.FloorToInt((min.x - 80f) / TileSize); tx <= Mathf.FloorToInt((max.x + 80f) / TileSize); tx++)
                    for (int tz = Mathf.FloorToInt((min.y - 80f) / TileSize); tz <= Mathf.FloorToInt((max.y + 80f) / TileSize); tz++)
                    {
                        var fl = map.FootprintsInTile(tx, tz);
                        if (fl != null)
                            foreach (int fi in fl)
                            {
                                var f = map.footprints[fi];
                                float r = Mathf.Max(f.hu, f.hv) * 1.5f + 1f;
                                if (f.centre.x + r < min.x || f.centre.x - r > max.x || f.centre.y + r < min.y || f.centre.y - r > max.y) continue;
                                foots.Add(fi);
                            }
                        if (proc != null && proc.TryGetValue(((long)tx << 24) ^ (tz & 0xFFFFFF), out var pl))
                            foreach (var pb in pl)
                            {
                                float r = 0.5f * (pb.w + pb.d) + 1f;
                                if (pb.pos.x + r < min.x || pb.pos.x - r > max.x || pb.pos.y + r < min.y || pb.pos.y - r > max.y) continue;
                                procs.Add(pb);
                            }
                    }
                others.Clear();
                for (int lj = 0; lj < li; lj++)
                {
                    var ob = map.lots[lj].box;
                    if (ob.z < min.x || ob.x > max.x || ob.w < min.y || ob.y > max.y) continue;
                    if (OverPropLot(map.lots[lj])) continue;
                    others.Add(lj);
                }
                double lm = 0, fm = 0, rm = 0, bm = 0, pm = 0, llm = 0;
                for (float z = (Mathf.Floor(min.y / G) + 0.5f) * G; z < max.y; z += G)
                    for (float x = (Mathf.Floor(min.x / G) + 0.5f) * G; x < max.x; x += G)
                    {
                        var q = new Vector2(x, z);
                        int cls = LotClass(map, li, q, false);
                        if (cls == 2) { A.islandM2 += CellA; continue; }
                        if (cls != 1) continue;
                        if (OnFan(q)) { fm += CellA; if (LotRoadCutOn) continue; }
                        if (LotRoadCutOn && OnBand(q)) { A.bandCutM2 += CellA; continue; }
                        lm += CellA;
                        if (OnRibbon(map, trims, segList, q)) rm += CellA;
                        foreach (int fi in foots)
                        {
                            var f = map.footprints[fi];
                            if (InRing(f.pts, q)) { bm += CellA; break; }
                            if (f.gable)
                            {
                                var d = q - f.centre; var v = new Vector2(-f.u.y, f.u.x);
                                if (Mathf.Abs(Vector2.Dot(d, f.u)) < f.hu && Mathf.Abs(Vector2.Dot(d, v)) < f.hv) { bm += CellA; break; }
                            }
                        }
                        foreach (var pb in procs)
                        {
                            float cy = Mathf.Cos(pb.yaw), sy = Mathf.Sin(pb.yaw);
                            var d = q - pb.pos;
                            if (Mathf.Abs(d.x * cy - d.y * sy) < 0.5f * pb.w && Mathf.Abs(d.x * sy + d.y * cy) < 0.5f * pb.d) { pm += CellA; break; }
                        }
                        foreach (int lj in others)
                            if (LotClass(map, lj, q, LotRoadCutOn) == 1) { llm += CellA; break; }
                    }
                A.lotM2 += lm; A.fanM2 += fm; A.ribbonM2 += rm; A.bldM2 += bm; A.procM2 += pm; A.lotLotM2 += llm;
                double over = (LotRoadCutOn ? 0 : fm) + rm + bm + pm + llm;
                if (over > 0)
                {
                    A.lotsWithOverlap++;
                    rows.Add(new KeyValuePair<double, string>(over, string.Format(inv,
                        "lot {0} ({1:0.0}, {2:0.0}){3}: {4:0.0} m2 over - fan {5:0.0}{6}, ribbon {7:0.0}, footprint {8:0.0}, placed building {9:0.0}, another lot {10:0.0}",
                        li, 0.5f * (b.x + b.z), 0.5f * (b.y + b.w), L.aisleOnly ? " (aisle only)" : "", over, fm, LotRoadCutOn ? " (cut: the fan's)" : "", rm, bm, pm, llm)));
                }
            }
            rows.Sort((p, q) => q.Key.CompareTo(p.Key));
            for (int i = 0; i < Mathf.Min(8, rows.Count); i++) A.worst.Add(rows[i].Value);
            lampBuildings = prevLamp;
            fanFloors.Clear(); tileBands.Clear();
            return A;
        }

        /// <summary>On a road's drawn ribbon (the lot audit): within the
        /// edge's extents either side, between its trims, on the ground.</summary>
        static bool OnRibbon(CityMap map, Trims trims, List<int> segList, Vector2 q)
        {
            foreach (int packed in segList)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel || si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-8f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, d) / L2);
                float dist = (q - (a + d * t)).magnitude;
                if (dist > e.HalfMax + 3f) continue;
                float s = e.s[si] + Mathf.Sqrt(L2) * t;
                if (s < trims.atA[ei] || s > e.length - trims.atB[ei]) continue;
                if (trims.Internal(ei) || e.ElevatedAt(s)) continue;
                LineModel.Extents(e, s, out float eM, out float eP);
                float side = d.x * (q.y - a.y) - d.y * (q.x - a.x);
                if (dist < (side >= 0f ? eP : eM)) return true;
            }
            return false;
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
