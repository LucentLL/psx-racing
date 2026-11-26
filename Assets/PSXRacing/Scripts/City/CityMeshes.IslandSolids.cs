using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  ISLAND SOLIDS (c7roads, 2026-11-25). The owner, on the University City
    //  loop (CltC7, PC68): "Some turn lanes have curbs in the middle of the
    //  turn lane. Dividers have holes and transparencies. Many of these
    //  features are lazily layered over one another when roads should be
    //  paved and combined with real thickness (still thin pixel layers)."
    //
    //  WHAT WAS DRAWN (the inventory, c7roads\INVENTORY.md): the land a
    //  junction leaves between its roads - the triangle between a slip lane
    //  and the through road, the wedge where a link leaves a carriageway, the
    //  sliver at a split node - was nobody's: each road laid its own verge
    //  (an inch down, the shoulder, the 1:4 foreslope: LayStrip / LayTucked)
    //  out to the lattice, each junction its own corner kerbs (BuildJunctions),
    //  the closing pass a skirt under every open edge (SkirtDown), the lattice
    //  cell its grass or concrete by the 8 m cell's rule. Where two roads
    //  diverge those sheets cross at their own heights: a foreslope rising
    //  over the other road's lane (the white lines run under it), a sliver
    //  of verge at the split node standing in the lane (a curb nose in the
    //  turn lane), single-sided skirts seen from behind (a hole).
    //
    //  THE RULE. After every sheet of the tile is laid (after the closing
    //  pass), the tile's DRAWN pavement - every driving-surface triangle,
    //  ribbon, fan, gore and slab - is rasterised (0.5 m). Then:
    //    1. OVERLAYS: a ground, verge, curb or skirt triangle lying over the
    //       pavement (all three corners over drawn pavement, its middle at
    //       least half a metre inside it, above the pavement there) is taken
    //       out: the road is the surface there, nothing is laid on it.
    //    2. ISLANDS: the land left enclosed by pavement (a component of
    //       non-pavement cells that does not reach the tile's border, at most
    //       IslandMaxAreaM2 and IslandMaxHalfM from its pavement anywhere, no
    //       building in it) becomes ONE CLOSED SOLID: its outline traced from
    //       the drawn pavement, a vertical curb face of CurbH (the city's 6 in)
    //       all the way round from just under the pavement, one top - grass,
    //       or concrete where the island is under MedianConcreteMaxW across
    //       (the median dress's rule: too narrow to plant) - and an asphalt
    //       apron round its foot, tucked under the roads, so no slot is left
    //       between the curb and the road. Every verge, strip, skirt, curb and
    //       top that lay inside it is taken out; the lattice under it stays.
    //    3. TIPS: where the island is narrower than IslandMinWidthM (a knife
    //       edge at a split node, where the lanes have not parted yet) it is
    //       not island but pavement: those cells are paved (the junction
    //       slab's asphalt, world UVs as a fan's) and the nose starts where
    //       the island is a real one.
    //  An island that crosses a tile's border keeps the old look (counted:
    //  IslandSkippedBorder), as does land too big or too wide to be one.
    //  PSX_CITY_ISLANDSOLID=0: the tile as before (round 1 had it opt-in; round 2 traces from the travelled way).
    // ======================================================================
    public static partial class CityMeshes
    {
        /// <summary>Round 2: default ON again (=0: the tiles as before); the islands are traced from the line
        /// model's travelled way (IsRasterBands), not the drawn pavement.</summary>
        public static bool IslandSolidsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_ISLANDSOLID") != "0";

        const float IsCell = 0.5f;
        const float IsMargin = 8f;
        static readonly int IsN = Mathf.CeilToInt((TileSize + 2f * IsMargin) / IsCell);
        /// <summary>An island is at most this big (m2) ...</summary>
        public const float IslandMaxAreaM2 = 2500f;
        /// <summary>... and no point of it further than this from its pavement (half its width).</summary>
        public const float IslandMaxHalfM = 14f;
        /// <summary>Narrower than this it is a tip: paved, not island (AASHTO: a raised island's nose is 2-4 ft wide).</summary>
        public const float IslandMinWidthM = 1.2f;
        /// <summary>The asphalt apron round an island's foot: this wide, this far under the road.</summary>
        const float IslandApronM = 0.9f, IslandApronDownM = 0.02f;
        /// <summary>The curb this far inside the traced outline.</summary>
        const float IslandInsetM = 0.35f;
        /// <summary>The curb face starts this far under the pavement.</summary>
        const float IslandFootM = 0.06f;
        /// <summary>Never within this of the tile's border (the next tile has the rest of it).</summary>
        const float IslandBorderM = 2f;

        public static int IslandGores, IslandLaneTips;
        /// <summary>A sliver narrower than a nose everywhere, at most this big and this long, is a gore: paved.</summary>
        public const float IslandGoreMaxM2 = 30f, IslandGoreMaxM = 20f;
        public static int IslandCount, IslandSkippedBorder, IslandSkippedBig, IslandTipCells, IslandOverlayTris, IslandInsideTris, IslandPokeVerts;
        public static float IslandAreaM2;
        /// <summary>The islands of the last tile builds (world plan outlines), for the gate and the pictures.</summary>
        public static readonly List<Vector2[]> IslandOutlines = new List<Vector2[]>();
        public static bool IslandRecord;
        /// <summary>The probe's trace: every land component within 30 m of this point (world plan), with what was decided.</summary>
        public static Vector2? IslandDebugAt;
        public static readonly System.Text.StringBuilder IslandDebug = new System.Text.StringBuilder();

        static float[] isY;        // pavement height per cell (lowest), NaN none
        static short[] isSlot;     // the road slot that paved it
        static int[] isDist;       // BFS distance (cells) to pavement, land only
        static int[] isDist2;      // the same, a lane's band counted as pavement
        static bool[] isBand;      // inside some road's lanes (its ribbon less its shoulders)
        static int[] isBandEdge;   // ... whose (the last one drawn)
        static bool[] isBandUp;    // under a deck's or a tunnel's lanes, or an elevated section's: never island, never paved
        static bool[] isKnife;     // the knife where two roads part (paved gore)
        static bool[] isNearJ;     // within IslandLaneTipNodeM of a node where three or more roads meet (a split, a junction)
        static float[] isNearY;    // the nearest pavement's height, land only
        static int[] isComp;       // component id of land cells
        static byte[] isFlag;      // 1 land, 2 island, 3 tip
        static readonly Queue<int> isQ = new Queue<int>(1 << 16);

        static int IsIdx(int i, int j) => j * IsN + i;

        /// <summary>The pass (TileSteps, after the closing faces).</summary>
        static IEnumerable<int> IslandSolids(CityMap map, Trims trims, TileMeshes tm)
        {
            int N = IsN, NN = N * N;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (isY == null || isY.Length != NN)
            {
                isY = new float[NN]; isSlot = new short[NN]; isDist = new int[NN]; isDist2 = new int[NN]; isBand = new bool[NN]; isBandEdge = new int[NN]; isBandUp = new bool[NN]; isKnife = new bool[NN]; isNearJ = new bool[NN]; isNearY = new float[NN]; isComp = new int[NN]; isFlag = new byte[NN];
            }
            isGone.Clear();
            for (int k = 0; k < NN; k++) { isY[k] = float.NaN; isSlot[k] = -1; isComp[k] = -1; isFlag[k] = 0; isDist[k] = int.MaxValue; isBand[k] = false; isBandEdge[k] = -1; isBandUp[k] = false; isKnife[k] = false; isNearJ[k] = false; }
            // ---- the drawn pavement ----------------------------------------------------------------
            bool any = false;
            for (int s = 0; s < buckets.Length; s++)
            {
                if (!RoadSlot(s, out _, out _)) continue;
                var bk = buckets[s];
                if (bk.t.Count == 0 && (bk.tn == null || bk.tn.Count == 0)) continue;
                any = true;
                IsRasterPave(bk, bk.t, s);
                if (bk.tn != null) IsRasterPave(bk, bk.tn, s);
            }
            IslandMs[0] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            if (!any) yield break;
            yield return 0;
            sw.Restart();
            // ---- 1. overlays: nothing is laid on the pavement ---------------------------------------
            foreach (var bk in IslandSheetBuckets()) IsDropOverlays(bk);
            IslandMs[1] += sw.Elapsed.TotalMilliseconds;
            yield return 0;
            sw.Restart();
            // ---- the lanes: every grounded road's ribbon less its shoulders, as designed -----------------
            // (a branch clipped against its host, a sliver at a split node: where the drawn pavement stops short
            // of a lane, the lane still runs there - no island may stand in it)
            IsRasterBands(map, trims, tm.origin);
            IsMarkJunctions(map, tm.origin);
            IsBuildingMask();
            IslandMs[2] += sw.Elapsed.TotalMilliseconds;
            yield return 0;
            sw.Restart();
            // ---- distance (chamfer 10 / 14 a cell) and the nearest pavement's height into the land ----
            for (int k = 0; k < NN; k++)
            {
                if (float.IsNaN(isY[k])) { isFlag[k] = 1; isDist[k] = int.MaxValue / 2; isNearY[k] = float.NaN; }
                else { isDist[k] = 0; isNearY[k] = isY[k]; }
            }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = IsIdx(i, j);
                    if (isDist[k] == 0) continue;
                    if (i > 0) IsRelax(k, k - 1, 10);
                    if (j > 0) { IsRelax(k, k - N, 10); if (i > 0) IsRelax(k, k - N - 1, 14); if (i + 1 < N) IsRelax(k, k - N + 1, 14); }
                }
            for (int j = N - 1; j >= 0; j--)
                for (int i = N - 1; i >= 0; i--)
                {
                    int k = IsIdx(i, j);
                    if (isDist[k] == 0) continue;
                    if (i + 1 < N) IsRelax(k, k + 1, 10);
                    if (j + 1 < N) { IsRelax(k, k + N, 10); if (i + 1 < N) IsRelax(k, k + N + 1, 14); if (i > 0) IsRelax(k, k + N - 1, 14); }
                }
            for (int k = 0; k < NN; k++) isDist2[k] = (isDist[k] == 0 || isBand[k]) ? 0 : int.MaxValue / 2;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = IsIdx(i, j);
                    if (isDist2[k] == 0) continue;
                    if (i > 0) IsRelax2(k, k - 1, 10);
                    if (j > 0) { IsRelax2(k, k - N, 10); if (i > 0) IsRelax2(k, k - N - 1, 14); if (i + 1 < N) IsRelax2(k, k - N + 1, 14); }
                }
            for (int j = N - 1; j >= 0; j--)
                for (int i = N - 1; i >= 0; i--)
                {
                    int k = IsIdx(i, j);
                    if (isDist2[k] == 0) continue;
                    if (i + 1 < N) IsRelax2(k, k + 1, 10);
                    if (j + 1 < N) { IsRelax2(k, k + N, 10); if (i + 1 < N) IsRelax2(k, k + N + 1, 14); if (i > 0) IsRelax2(k, k + N - 1, 14); }
                }
            IslandMs[3] += sw.Elapsed.TotalMilliseconds;
            yield return 0;
            sw.Restart();
            // ---- 2. the land's components -------------------------------------------------------------
            var o = tm.origin;
            int comp = 0;
            var cells = new List<int>(4096);
            int borderCells = Mathf.CeilToInt((IsMargin + IslandBorderM) / IsCell);
            int halfCells = Mathf.CeilToInt(10f * IslandMaxHalfM / IsCell);
            int tipCells = Mathf.Max(10, Mathf.RoundToInt(10f * 0.5f * IslandMinWidthM / IsCell));
            int growCells = Mathf.Max(1, Mathf.RoundToInt(0.5f * IslandMinWidthM / IsCell));
            for (int start = 0; start < NN; start++)
            {
                if (isFlag[start] != 1 || isComp[start] >= 0) continue;
                cells.Clear();
                bool border = false, wide = false;
                isComp[start] = comp; isQ.Enqueue(start);
                while (isQ.Count > 0)
                {
                    int k = isQ.Dequeue(); cells.Add(k);
                    int i = k % N, j = k / N;
                    if (i < borderCells || j < borderCells || i >= N - borderCells || j >= N - borderCells) border = true;
                    if (isDist[k] > halfCells) wide = true;
                    if (i > 0) IsGrow(k - 1, comp);
                    if (i + 1 < N) IsGrow(k + 1, comp);
                    if (j > 0) IsGrow(k - N, comp);
                    if (j + 1 < N) IsGrow(k + N, comp);
                    if (cells.Count > IslandMaxAreaM2 / (IsCell * IsCell) * 4) { border = true; }
                }
                comp++;
                float area = cells.Count * IsCell * IsCell;
                if (IslandDebugAt.HasValue)
                {
                    int ck = cells[0]; var cw = new Vector2((ck % N + 0.5f) * IsCell - IsMargin + o.x, (ck / N + 0.5f) * IsCell - IsMargin + o.z);
                    float best = float.MaxValue; foreach (int k in cells) { var w = new Vector2((k % N + 0.5f) * IsCell - IsMargin + o.x, (k / N + 0.5f) * IsCell - IsMargin + o.z); best = Mathf.Min(best, (w - IslandDebugAt.Value).magnitude); }
                    if (best < 30f)
                    {
                        int cn = 0; foreach (int k in cells) if (isDist[k] >= tipCells) cn++;
                        IsSpan(cells, out float sp);
                        IslandDebug.Append($"    land at {best:0.0} m: {area:0.0} m2, span {sp:0.0} m, border {border}, wide {wide}, core cells {cn}\n");
                    }
                }
                if (border) { if (area < IslandMaxAreaM2 && !wide) IslandSkippedBorder++; IsPaveLaneTips(map, cells, o, tipCells); continue; }
                if (wide || area > IslandMaxAreaM2) { IslandSkippedBig++; IsPaveLaneTips(map, cells, o, tipCells); continue; }
                if (area < 0.5f) continue;
                if (IsHasBuilding(cells)) { IsPaveLaneTips(map, cells, o, tipCells); continue; }
                // the core: cells half the minimum width from the pavement; an island is the core grown back
                int coreN = 0;
                foreach (int k in cells) if (!isBand[k] && !isBandUp[k] && isDist2[k] >= tipCells) coreN++;
                if (coreN == 0)
                {
                    // all of it narrower than a nose: a long one is a narrow median (as it was); a short one is the
                    // knife edge between two roads that part (a gore): paved
                    IsSpan(cells, out float spanM);
                    if (area <= IslandGoreMaxM2 && spanM <= IslandGoreMaxM)
                    {
                        foreach (int k in cells) { isFlag[k] = 3; IsPaveCell(k, o); }
                        IslandTipCells += cells.Count; IslandGores++;
                        foreach (var bk in IslandSheetBuckets()) IsDropCells(bk);
                    }
                    continue;
                }
                foreach (int k in cells) isFlag[k] = (byte)(isBandUp[k] ? 4 : !isBand[k] && isDist2[k] >= tipCells ? 2 : 3);
                // grow the core back over tip cells within tipCells of it (an opening)
                IsGrowCore(cells, growCells);
                IsEmitComponent(map, tm, cells, o);
                IslandMs[4] += sw.Elapsed.TotalMilliseconds;
                yield return 0;
                sw.Restart();
            }
            IslandMs[4] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            if (isTipsPending) { isTipsPending = false; foreach (var bk in IslandSheetBuckets()) IsDropCells(bk); }
            IsDropOrphans();
            IslandMs[5] += sw.Elapsed.TotalMilliseconds;
        }
        /// <summary>Milliseconds by part, summed over builds: raster, overlays, bands+knives+marks, distances,
        /// components+islands, tips+orphans (the cost split).</summary>
        public static readonly double[] IslandMs = new double[6];

        /// <summary>Beside a junction or split node, a lane's band on land this near the drawn pavement is
        /// paved whatever land it is part of (the knife edge where two roads part, a branch's lane its
        /// clipped ribbon stops short of).</summary>
        public const float IslandLaneTipNodeM = 25f, IslandLaneTipM = 1.5f;

        static void IsMarkJunctions(CityMap map, Vector3 o)
        {
            int r = Mathf.CeilToInt(IslandLaneTipNodeM / IsCell);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var ne = map.nodeEdges[n];
                if (ne == null || ne.Count < 3) continue;
                var p = map.nodes[n];
                float lx = p.x - o.x, lz = p.y - o.z;
                if (lx < -IsMargin - IslandLaneTipNodeM || lz < -IsMargin - IslandLaneTipNodeM || lx > TileSize + IsMargin + IslandLaneTipNodeM || lz > TileSize + IsMargin + IslandLaneTipNodeM) continue;
                int ci = Mathf.FloorToInt((lx + IsMargin) / IsCell), cj = Mathf.FloorToInt((lz + IsMargin) / IsCell);
                for (int dj = -r; dj <= r; dj++)
                {
                    int j = cj + dj; if (j < 0 || j >= IsN) continue;
                    for (int di = -r; di <= r; di++)
                    {
                        int i = ci + di; if (i < 0 || i >= IsN) continue;
                        if (di * di + dj * dj > r * r) continue;
                        isNearJ[IsIdx(i, j)] = true;
                    }
                }
            }
        }

        static void IsPaveLaneTips(CityMap map, List<int> cells, Vector3 o, int tipCells)
        {
            int lim = Mathf.RoundToInt(10f * IslandLaneTipM / IsCell);
            int n = 0;
            foreach (int k in cells)
            {
                if (!isNearJ[k] && !isKnife[k]) continue;
                bool lane = isBand[k] && (isDist[k] <= lim || isKnife[k]);
                // the KNIFE where two roads part (or join): land thinner than a nose with no wider land beside it,
                // between the lanes of two roads that leave one node within 60 degrees of each other - pavement,
                // the nose starts where the island is a real one (a narrow median between OPPOSING carriageways stays)
                bool knife = !lane && isDist[k] < tipCells && IsThin(k, tipCells) && IsBetweenParting(map, k);
                if (!lane && !knife) continue;
                isFlag[k] = 3; IsPaveCell(k, o); n++;
                if (knife) IslandKnifeCells++;
            }
            if (n == 0) return;
            IslandTipCells += n; IslandLaneTips += n;
            isTipsPending = true;   // their sheets go in one pass after the components (IsDropCells)
        }
        static bool isTipsPending;
        public static int IslandKnifeCells;

        /// <summary>No land a nose's half-width from the pavement within a cell of it: a thin neck, not the edge of a field.</summary>
        static bool IsThin(int k, int tipCells)
        {
            int i = k % IsN, j = k / IsN;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= IsN || jj >= IsN) continue;
                    int kk = IsIdx(ii, jj);
                    if (float.IsNaN(isY[kk]) && isDist[kk] >= tipCells) return false;
                }
            return true;
        }

        static readonly List<int> isKnifeEdges = new List<int>(8);

        /// <summary>Lanes of two roads within 1.5 m of the cell that leave a common node within 60 degrees.</summary>
        static bool IsBetweenParting(CityMap map, int k)
        {
            int i = k % IsN, j = k / IsN, r = Mathf.CeilToInt(1.5f / IsCell);
            isKnifeEdges.Clear();
            for (int dj = -r; dj <= r; dj++)
                for (int di = -r; di <= r; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= IsN || jj >= IsN) continue;
                    int e = isBandEdge[IsIdx(ii, jj)];
                    if (e >= 0 && !isKnifeEdges.Contains(e)) isKnifeEdges.Add(e);
                }
            for (int a = 0; a < isKnifeEdges.Count; a++)
                for (int b = a + 1; b < isKnifeEdges.Count; b++)
                {
                    var A = map.edges[isKnifeEdges[a]]; var B = map.edges[isKnifeEdges[b]];
                    int n = A.a == B.a || A.a == B.b ? A.a : A.b == B.a || A.b == B.b ? A.b : -1;
                    if (n < 0) continue;
                    var dA = A.a == n ? A.TangentAt(0f) : -A.TangentAt(A.length);
                    var dB = B.a == n ? B.TangentAt(0f) : -B.TangentAt(B.length);
                    if (Vector2.Angle(dA, dB) > 60f) continue;
                    // two carriageways of one road running opposite ways are a median, not a knife
                    if (A.oneway && B.oneway && A.name == B.name && !string.IsNullOrEmpty(A.name) && ((A.a == n) != (B.a == n))) continue;
                    return true;
                }
            return false;
        }

        static void IsRelax2(int k, int from, int w)
        {
            int d = isDist2[from] + w;
            if (d < isDist2[k]) isDist2[k] = d;
        }

        /// <summary>Does another arm leave this node within 60 degrees of the edge (the roads part here:
        /// a split, a branch) - its lanes then run on into the junction as drawn.</summary>
        static bool IsParting(CityMap map, CityMap.Edge e, int node)
        {
            var ne = map.nodeEdges[node];
            if (ne == null || ne.Count < 3) return false;
            var dE = e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);
            foreach (int oi in ne)
            {
                if (oi == e.index) continue;
                var O = map.edges[oi];
                if (O.a == O.b) continue;
                var dO = O.a == node ? O.TangentAt(0f) : -O.TangentAt(O.length);
                if (Vector2.Angle(dE, dO) < 60f) return true;
            }
            return false;
        }

        /// <summary>The island's curb stands this far outside the travelled way's own edge, past the road's
        /// shoulder (e.shl / e.shr, 0.3 m on the city's streets: the drawn pavement edge where a curbed street's
        /// curb stands): the line gate fails a curb more than CurbInLaneM (0.15 m) inside an edge line.</summary>
        public const float IslandCurbMarginM = 0.15f;
        static readonly HashSet<int> isSegs = new HashSet<int>();
        static readonly HashSet<int> isEdgesNear = new HashSet<int>();

        static void IsRasterBands(CityMap map, Trims trims, Vector3 o)
        {
            float x0 = o.x - IsMargin, z0 = o.z - IsMargin, x1 = o.x + TileSize + IsMargin, z1 = o.z + TileSize + IsMargin;
            isSegs.Clear(); isEdgesNear.Clear();
            map.EdgeSegsInRect(new Vector2(x0 - 30f, z0 - 30f), new Vector2(x1 + 30f, z1 + 30f), isSegs);
            foreach (int packed in isSegs) isEdgesNear.Add(packed >> 12);
            foreach (int ei in isEdgesNear)
            {
                var e = map.edges[ei];
                if (e.pts == null || e.pts.Length < 2 || e.a == e.b) continue;
                bool up = e.bridge || e.tunnel;
                float pad = e.HalfMax + 2f;
                // the ribbon's own run, not inside a fan (a junction's corners are the fan's), except where the roads
                // part (a split, a branch): there the lanes run on to the node as drawn
                float sA = trims != null && trims.atA != null && trims.patch != null && trims.patch[e.a] && !IsParting(map, e, e.a) ? trims.atA[e.index] : 0f;
                float sB = trims != null && trims.atB != null && trims.patch != null && trims.patch[e.b] && !IsParting(map, e, e.b) ? e.length - trims.atB[e.index] : e.length;
                for (float sv = sA; sv <= sB; sv += 0.4f)
                {
                    var pt = e.PointAt(sv);
                    if (pt.x < x0 - pad || pt.x > x1 + pad || pt.y < z0 - pad || pt.y > z1 + pad) continue;
                    bool upHere = up || e.ElevatedAt(sv);
                    LineModel.Extents(e, sv, out float eM, out float eP);
                    var tg = e.TangentAt(sv); var left = new Vector2(-tg.y, tg.x);
                    float lo = -eM - IslandCurbMarginM, hi = eP + IslandCurbMarginM;
                    for (float l = lo; l <= hi + 0.01f; l += 0.35f)
                    {
                        var q = pt + left * Mathf.Min(l, hi);
                        if (!IsCellAt(q.x - o.x, q.y - o.z, out int k)) continue;
                        if (upHere) { isBandUp[k] = true; continue; }
                        isBand[k] = true; isBandEdge[k] = e.index;
                    }
                }
            }
            IsRasterKnives(map, o);
        }

        // ---- B. every split: the knife between two roads that part is paved until it is nose-wide -----------
        /// <summary>A raised island's nose is at least this wide (AASHTO Green Book 9.6.3: a curbed island's
        /// approach nose 2-4 ft; the city takes 1.2 m, the island pass's own minimum).</summary>
        public static int IslandKnifePairs;

        static void IsRasterKnives(CityMap map, Vector3 o)
        {
            float x0 = o.x - IsMargin - 40f, z0 = o.z - IsMargin - 40f, x1 = o.x + TileSize + IsMargin + 40f, z1 = o.z + TileSize + IsMargin + 40f;
            var arms = new List<int>(8);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var np = map.nodes[n];
                if (np.x < x0 || np.x > x1 || np.y < z0 || np.y > z1) continue;
                var ne = map.nodeEdges[n];
                if (ne == null || ne.Count < 3) continue;
                arms.Clear();
                foreach (int ei in ne) { var E = map.edges[ei]; if (E.a != E.b && !E.bridge && !E.tunnel) arms.Add(ei); }
                for (int i = 0; i < arms.Count; i++)
                    for (int j = i + 1; j < arms.Count; j++)
                    {
                        var A = map.edges[arms[i]]; var B = map.edges[arms[j]];
                        var dA = A.a == n ? A.TangentAt(0f) : -A.TangentAt(A.length);
                        var dB = B.a == n ? B.TangentAt(0f) : -B.TangentAt(B.length);
                        if (Vector2.Angle(dA, dB) > 60f) continue;
                        // two carriageways of one road running opposite ways part at a split node: their median's nose
                        // is the median's (CurbMedian); this is for roads that part the same way
                        bool aOut = !A.oneway || A.a == n, bOut = !B.oneway || B.a == n;
                        if (aOut != bOut && A.oneway && B.oneway) continue;
                        IsKnife(A, B, n, dA, dB, o);
                    }
            }
        }

        /// <summary>The facing pavement edges of A and B (grown by the curb margin) from the node out; the strip
        /// between them is marked as lane (paved) while it is narrower than a nose.</summary>
        static void IsKnife(CityMap.Edge A, CityMap.Edge B, int n, Vector2 dA, Vector2 dB, Vector3 o)
        {
            float cross = dA.x * dB.y - dA.y * dB.x;     // > 0: B lies left of A's outward direction
            Vector2 prevA = Vector2.zero, prevB = Vector2.zero; bool havePrev = false;
            float lim = Mathf.Min(60f, Mathf.Min(A.length, B.length));
            int nb = Mathf.FloorToInt(lim / 0.5f) + 1;
            if (isKB.Length < nb) isKB = new Vector2[nb * 2];
            for (int q = 0; q < nb; q++) isKB[q] = IsFacingEdge(B, n, q * 0.5f, cross < 0f);
            for (float d = 0f; d <= lim; d += 0.5f)
            {
                var pa = IsFacingEdge(A, n, d, cross > 0f);
                // B's facing edge nearest pa
                var pb = isKB[0]; float bd = (pb - pa).sqrMagnitude;
                for (int q = 1; q < nb; q++) { float dd = (isKB[q] - pa).sqrMagnitude; if (dd < bd) { bd = dd; pb = isKB[q]; } }
                float gap = Vector2.Distance(pa, pb);
                if (havePrev) IsFillQuad(prevA, pa, pb, prevB, o);
                if (gap >= IslandMinWidthM + 2f * IslandCurbMarginM) { if (d > 0f) IslandKnifePairs++; break; }
                prevA = pa; prevB = pb; havePrev = true;
            }
        }

        static Vector2 IsFacingEdge(CityMap.Edge e, int node, float fromNode, bool leftOfOutward)
        {
            float s = e.a == node ? fromNode : e.length - fromNode;
            s = Mathf.Clamp(s, 0f, e.length);
            LineModel.Extents(e, s, out float eM, out float eP);
            var tg = e.TangentAt(s); var left = new Vector2(-tg.y, tg.x);     // left of a->b
            bool leftAB = e.a == node ? leftOfOutward : !leftOfOutward;
            return e.PointAt(s) + left * (leftAB ? eP + IslandCurbMarginM : -(eM + IslandCurbMarginM));
        }

        static Vector2[] isKB = new Vector2[256];

        static void IsFillQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector3 o)
        {
            float minx = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)) - o.x, maxx = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)) - o.x;
            float minz = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)) - o.z, maxz = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)) - o.z;
            for (float z = Mathf.Floor(minz / IsCell) * IsCell + 0.5f * IsCell; z <= maxz + IsCell; z += IsCell)
                for (float x = Mathf.Floor(minx / IsCell) * IsCell + 0.5f * IsCell; x <= maxx + IsCell; x += IsCell)
                {
                    var q = new Vector2(x + o.x, z + o.z);
                    if (!InQuad(q, a, b, c, d)) continue;
                    if (!IsCellAt(x, z, out int k) || isBandUp[k]) continue;
                    isBand[k] = true; if (isBandEdge[k] < 0) isBandEdge[k] = -2;
                    isKnife[k] = true;
                }
        }

        static bool InQuad(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d) => InTri(a, b, c, p) || InTri(a, c, d, p) || InTri(a, c, b, p) || InTri(a, d, c, p);

        static void IsRelax(int k, int from, int w)
        {
            int d = isDist[from] + w;
            if (d < isDist[k]) { isDist[k] = d; isNearY[k] = isNearY[from]; isSlot[k] = isSlot[from]; }
        }

        static void IsSpan(List<int> cells, out float spanM)
        {
            int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
            foreach (int k in cells) { int i = k % IsN, j = k / IsN; i0 = Mathf.Min(i0, i); i1 = Mathf.Max(i1, i); j0 = Mathf.Min(j0, j); j1 = Mathf.Max(j1, j); }
            spanM = Mathf.Sqrt((i1 - i0 + 1) * (i1 - i0 + 1) + (j1 - j0 + 1) * (j1 - j0 + 1)) * IsCell;
        }

        /// <summary>Take out the sheets lying wholly on cells just paved (a gore's verges, skirts, curbs).</summary>
        static void IsDropCells(Bucket bk)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var tl = pass == 0 ? bk.t : bk.tn;
                if (tl == null || tl.Count == 0) continue;
                isKeep.Clear();
                int dropped = 0;
                for (int t = 0; t + 2 < tl.Count; t += 3)
                {
                    Vector3 a = bk.v[tl[t]], b = bk.v[tl[t + 1]], c = bk.v[tl[t + 2]];
                    bool all = IsTipOrPave(a) && IsTipOrPave(b) && IsTipOrPave(c);
                    var m = (a + b + c) / 3f;
                    if (all && IsCellAt(m.x, m.z, out int km) && isFlag[km] == 3 && (m.y > isNearY[km] - 1.5f)) { dropped++; if (!ReferenceEquals(bk, kerbBucket)) IsNoteGone(a, b, c); continue; }
                    isKeep.Add(tl[t]); isKeep.Add(tl[t + 1]); isKeep.Add(tl[t + 2]);
                }
                if (dropped == 0) continue;
                IslandInsideTris += dropped;
                tl.Clear(); tl.AddRange(isKeep);
            }
        }

        static bool IsTipOrPave(Vector3 q) => IsCellAt(q.x, q.z, out int k) && (isFlag[k] == 3 || !float.IsNaN(isY[k]));

        static void IsGrow(int kk, int comp)
        {
            if (isFlag[kk] != 1 || isComp[kk] >= 0) return;
            isComp[kk] = comp; isQ.Enqueue(kk);
        }

        static IEnumerable<Bucket> IslandSheetBuckets()
        {
            yield return buckets[(int)Slot.Ground];
            yield return buckets[(int)Slot.Pavement];
            yield return buckets[(int)Slot.MedianBed];
            yield return buckets[(int)Slot.Sidewalk];
            yield return buckets[(int)Slot.WalkPaver];
            yield return kerbBucket;
        }

        static void IsRasterPave(Bucket bk, List<int> tl, int slot)
        {
            int N = IsN;
            for (int t = 0; t + 2 < tl.Count; t += 3)
            {
                Vector3 a = bk.v[tl[t]], b = bk.v[tl[t + 1]], c = bk.v[tl[t + 2]];
                float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                int i0 = Mathf.Max(0, Mathf.CeilToInt((minx + IsMargin) / IsCell - 0.5f)), i1 = Mathf.Min(N - 1, Mathf.FloorToInt((maxx + IsMargin) / IsCell - 0.5f));
                int j0 = Mathf.Max(0, Mathf.CeilToInt((minz + IsMargin) / IsCell - 0.5f)), j1 = Mathf.Min(N - 1, Mathf.FloorToInt((maxz + IsMargin) / IsCell - 0.5f));
                if (i0 > i1 || j0 > j1) continue;
                float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(den) < 1e-9f) continue;
                for (int j = j0; j <= j1; j++)
                {
                    float z = (j + 0.5f) * IsCell - IsMargin;
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = (i + 0.5f) * IsCell - IsMargin;
                        float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / den;
                        float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / den;
                        float l3 = 1f - l1 - l2;
                        if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                        float y = l1 * a.y + l2 * b.y + l3 * c.y;
                        int k = IsIdx(i, j);
                        if (float.IsNaN(isY[k]) || y < isY[k]) { isY[k] = y; isSlot[k] = (short)slot; }
                    }
                }
            }
        }

        static bool IsCellAt(float x, float z, out int k)
        {
            int i = Mathf.FloorToInt((x + IsMargin) / IsCell), j = Mathf.FloorToInt((z + IsMargin) / IsCell);
            k = -1;
            if (i < 0 || j < 0 || i >= IsN || j >= IsN) return false;
            k = IsIdx(i, j);
            return true;
        }

        /// <summary>Is the plan point over drawn pavement, and how high is it.</summary>
        static bool IsPaveAt(float x, float z, out float y)
        {
            y = float.NaN;
            if (!IsCellAt(x, z, out int k)) return false;
            y = isY[k];
            return !float.IsNaN(y);
        }

        /// <summary>Over pavement at least <paramref name="inset"/> from its edge (all eight ways).</summary>
        static bool IsPaveInside(float x, float z, float inset)
        {
            if (!IsPaveAt(x, z, out _)) return false;
            for (int q = 0; q < 8; q++)
            {
                float an = q * Mathf.PI / 4f;
                if (!IsPaveAt(x + Mathf.Cos(an) * inset, z + Mathf.Sin(an) * inset, out _)) return false;
            }
            return true;
        }

        static readonly List<int> isKeep = new List<int>(1 << 16);
        /// <summary>The corners (2 cm) of every sheet triangle the pass took out: a face hung from one goes too.</summary>
        static readonly HashSet<long> isGone = new HashSet<long>();
        static long IsQ(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 50f) * 73856093L) ^ ((long)Mathf.RoundToInt(p.y * 50f) * 19349663L) ^ ((long)Mathf.RoundToInt(p.z * 50f) * 83492791L);
        static void IsNoteGone(Vector3 a, Vector3 b, Vector3 c) { isGone.Add(IsQ(a)); isGone.Add(IsQ(b)); isGone.Add(IsQ(c)); }

        /// <summary>Faces (kerbs mesh, walk curb faces) standing on a corner of a sheet taken out: single-sided
        /// skirts turned away from a sheet that is no longer there show their back to the road.</summary>
        static void IsDropOrphans()
        {
            if (isGone.Count == 0) return;
            foreach (var bk in new[] { kerbBucket, buckets[(int)Slot.Sidewalk], buckets[(int)Slot.Ground], buckets[(int)Slot.Pavement] })
            {
                var tl = bk.t;
                isKeep.Clear();
                int dropped = 0;
                for (int t = 0; t + 2 < tl.Count; t += 3)
                {
                    Vector3 a = bk.v[tl[t]], b = bk.v[tl[t + 1]], c = bk.v[tl[t + 2]];
                    var cr = Vector3.Cross(b - a, c - a);
                    bool vertical = cr.sqrMagnitude > 1e-10f && Mathf.Abs(cr.y) < 0.5f * cr.magnitude;
                    if (vertical && (isGone.Contains(IsQ(a)) || isGone.Contains(IsQ(b)) || isGone.Contains(IsQ(c)))) { dropped++; continue; }
                    isKeep.Add(tl[t]); isKeep.Add(tl[t + 1]); isKeep.Add(tl[t + 2]);
                }
                if (dropped == 0) continue;
                IslandOrphanTris += dropped;
                tl.Clear(); tl.AddRange(isKeep);
            }
        }
        public static int IslandOrphanTris, IslandTopFails;

        static void IsDropOverlays(Bucket bk)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var tl = pass == 0 ? bk.t : bk.tn;
                if (tl == null || tl.Count == 0) continue;
                isKeep.Clear();
                int dropped = 0;
                for (int t = 0; t + 2 < tl.Count; t += 3)
                {
                    Vector3 a = bk.v[tl[t]], b = bk.v[tl[t + 1]], c = bk.v[tl[t + 2]];
                    bool drop = false;
                    if (IsPaveAt(a.x, a.z, out float ya) && IsPaveAt(b.x, b.z, out float yb) && IsPaveAt(c.x, c.z, out float yc))
                    {
                        var m = (a + b + c) / 3f;
                        if (IsPaveInside(m.x, m.z, 0.35f))
                        {
                            float rise = Mathf.Max(a.y - ya, Mathf.Max(b.y - yb, c.y - yc));
                            float low = Mathf.Min(a.y - ya, Mathf.Min(b.y - yb, c.y - yc));
                            // laid on it or standing in it (a foreslope rising over a lane, a verge sliver, a
                            // flat slab): out. A sheet well under the road (a lattice below a deck) stays.
                            if (rise > 0.003f && low > -1.5f && rise < 1.0f) drop = true;
                        }
                    }
                    if (drop) { dropped++; if (!ReferenceEquals(bk, kerbBucket)) IsNoteGone(a, b, c); continue; }
                    isKeep.Add(tl[t]); isKeep.Add(tl[t + 1]); isKeep.Add(tl[t + 2]);
                }
                if (dropped == 0) continue;
                IslandOverlayTris += dropped;
                tl.Clear(); tl.AddRange(isKeep);
            }
        }

        static bool[] isBld;
        static int[] isWalkCnt;
        static void IsBuildingMask()
        {
            int NN = IsN * IsN;
            if (isBld == null || isBld.Length != NN) { isBld = new bool[NN]; isWalkCnt = new int[NN]; }
            System.Array.Clear(isBld, 0, NN);
            foreach (var s in BuildingSlots)
            {
                var bk = buckets[(int)s];
                for (int t = 0; t + 2 < bk.t.Count; t += 9)   // a sample of its triangles is enough
                {
                    var m = (bk.v[bk.t[t]] + bk.v[bk.t[t + 1]] + bk.v[bk.t[t + 2]]) / 3f;
                    if (IsCellAt(m.x, m.z, out int k)) isBld[k] = true;
                }
            }
        }

        static bool IsHasBuilding(List<int> cells)
        {
            // a building's walls or roof inside the land: a block, not an island
            foreach (int k in cells) if (isBld[k]) return true;
            // a block with sidewalks round it (an urban street's walk, 2.4 m wide) is not an island; a
            // median's curb top (0.25 m) is
            var walk = buckets[(int)Slot.Sidewalk];
            int comp = isComp[cells[0]];
            float walkArea = 0f;
            for (int t = 0; t + 2 < walk.t.Count; t += 3)
            {
                Vector3 a = walk.v[walk.t[t]], b = walk.v[walk.t[t + 1]], c = walk.v[walk.t[t + 2]];
                var m = (a + b + c) / 3f;
                if (!IsCellAt(m.x, m.z, out int k) || isComp[k] != comp) continue;
                var cr = Vector3.Cross(b - a, c - a);
                if (Mathf.Abs(cr.y) < 0.9f * cr.magnitude) continue;
                walkArea += 0.5f * cr.magnitude;
            }
            int edgeCells = 0;
            foreach (int k in cells)
            {
                if (!float.IsNaN(isY[k - 1]) || !float.IsNaN(isY[k + 1]) || !float.IsNaN(isY[k - IsN]) || !float.IsNaN(isY[k + IsN])) edgeCells++;
            }
            return walkArea > 1.0f * edgeCells * IsCell;
        }

        static void IsGrowCore(List<int> cells, int r)
        {
            // multi-source BFS from the core over the component, r cells: those become island
            int N = IsN;
            var dist = new Dictionary<int, int>(cells.Count);
            isQ.Clear();
            foreach (int k in cells) if (isFlag[k] == 2) { dist[k] = 0; isQ.Enqueue(k); }
            while (isQ.Count > 0)
            {
                int k = isQ.Dequeue(); int d = dist[k];
                if (d >= r) continue;
                int i = k % N, j = k / N;
                for (int q = 0; q < 4; q++)
                {
                    int ii = i + (q == 0 ? 1 : q == 1 ? -1 : 0), jj = j + (q == 2 ? 1 : q == 3 ? -1 : 0);
                    if (ii < 0 || jj < 0 || ii >= N || jj >= N) continue;
                    int kk = IsIdx(ii, jj);
                    if (isFlag[kk] != 3 || isBand[kk] || dist.ContainsKey(kk)) continue;
                    dist[kk] = d + 1; isFlag[kk] = 2; isQ.Enqueue(kk);
                }
            }
        }

        // ---- emission ------------------------------------------------------------------------------------

        static readonly List<Vector2> isLoop = new List<Vector2>(1024);
        static readonly List<Vector2> isPoly = new List<Vector2>(256);

        static void IsEmitComponent(CityMap map, TileMeshes tm, List<int> cells, Vector3 o)
        {
            int N = IsN;
            // the tips: paved (the junction slab's asphalt, world UVs as a fan's)
            int tips = 0;
            foreach (int k in cells)
            {
                if (isFlag[k] != 3) continue;
                tips++;
                IsPaveCell(k, o);
            }
            IslandTipCells += tips;
            // the island cells may fall apart into several islands: each its own outline
            var seen = new HashSet<int>();
            foreach (int k0 in cells)
            {
                if (isFlag[k0] != 2 || seen.Contains(k0)) continue;
                var part = new List<int>();
                isQ.Clear(); isQ.Enqueue(k0); seen.Add(k0);
                while (isQ.Count > 0)
                {
                    int k = isQ.Dequeue(); part.Add(k);
                    int i = k % N, j = k / N;
                    for (int q = 0; q < 4; q++)
                    {
                        int ii = i + (q == 0 ? 1 : q == 1 ? -1 : 0), jj = j + (q == 2 ? 1 : q == 3 ? -1 : 0);
                        if (ii < 0 || jj < 0 || ii >= N || jj >= N) continue;
                        int kk = IsIdx(ii, jj);
                        if (isFlag[kk] == 2 && seen.Add(kk)) isQ.Enqueue(kk);
                    }
                }
                if (part.Count * IsCell * IsCell < 1f) { foreach (int k in part) IsPaveCell(k, o); IslandTipCells += part.Count; continue; }
                IsEmitIsland(map, tm, part, o);
            }
        }

        /// <summary>The junction slab's slot of the same surface (new / old asphalt, concrete) as the road
        /// that paved the cell or is nearest to it: a fill or an apron reads as the road beside it.</summary>
        static Bucket IsFillBucket(int k)
        {
            int surf = (int)Surface.AsphaltOld;
            if (k >= 0 && isSlot[k] >= 0 && RoadSlot(isSlot[k], out _, out int sf)) surf = sf;
            return buckets[(int)SlotOf(JunctionProfile, (Surface)surf)];
        }

        static void IsPaveCell(int k, Vector3 o)
        {
            int i = k % IsN, j = k / IsN;
            float x0 = i * IsCell - IsMargin, z0 = j * IsCell - IsMargin, e = 0.12f;
            float y = isNearY[k] - 0.004f;
            var bk = IsFillBucket(k);   // the surface of the pavement it was nearest
            Vector3 a = new Vector3(x0 - e, y, z0 - e), b = new Vector3(x0 + IsCell + e, y, z0 - e), c = new Vector3(x0 + IsCell + e, y, z0 + IsCell + e), d = new Vector3(x0 - e, y, z0 + IsCell + e);
            bk.Up(a, b, c, d, FanUV(a, o), FanUV(b, o), FanUV(c, o), FanUV(d, o));
        }

        static Vector2 FanUV(Vector3 p, Vector3 o) => new Vector2((p.x + o.x) / 12f, (p.z + o.z) / 12f);

        static void IsEmitIsland(CityMap map, TileMeshes tm, List<int> part, Vector3 o)
        {
            int N = IsN;
            var inPart = new HashSet<int>(part);
            // ---- the outline: the cells' boundary, inside on the left (anticlockwise) --------------------
            var next = new Dictionary<long, List<long>>();
            long C(int i, int j) => ((long)i << 32) | (uint)j;
            void Add(int ai, int aj, int bi, int bj)
            {
                long a = C(ai, aj);
                if (!next.TryGetValue(a, out var l)) next[a] = l = new List<long>(2);
                l.Add(C(bi, bj));
            }
            foreach (int k in part)
            {
                int i = k % N, j = k / N;
                if (!inPart.Contains(k - N)) Add(i, j, i + 1, j);
                if (!inPart.Contains(k + 1)) Add(i + 1, j, i + 1, j + 1);
                if (!inPart.Contains(k + N)) Add(i + 1, j + 1, i, j + 1);
                if (!inPart.Contains(k - 1)) Add(i, j + 1, i, j);
            }
            // chain into loops; at a corner with two ways on, turn left (four-connected)
            var loops = new List<List<Vector2>>();
            while (next.Count > 0)
            {
                long startC = 0; foreach (var kv in next) { startC = kv.Key; break; }
                var loop = new List<Vector2>();
                long cur = startC; long prev = -1;
                int guard = 0;
                while (guard++ < 200000)
                {
                    if (!next.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                    long pick = outs[0];
                    if (outs.Count > 1 && prev >= 0)
                    {
                        int pi = (int)(prev >> 32), pj = (int)(prev & 0xffffffff), ci = (int)(cur >> 32), cj = (int)(cur & 0xffffffff);
                        int dx = ci - pi, dz = cj - pj;
                        float best = -9f;
                        foreach (var cand in outs)
                        {
                            int ni = (int)(cand >> 32), nj = (int)(cand & 0xffffffff);
                            int ex = ni - ci, ez = nj - cj;
                            float cross = dx * ez - dz * ex;   // > 0: a left turn
                            if (cross > best) { best = cross; pick = cand; }
                        }
                    }
                    outs.Remove(pick);
                    if (outs.Count == 0) next.Remove(cur);
                    int x = (int)(cur >> 32), z = (int)(cur & 0xffffffff);
                    loop.Add(new Vector2(x * IsCell - IsMargin, z * IsCell - IsMargin));
                    prev = cur; cur = pick;
                    if (cur == startC) break;
                }
                if (loop.Count >= 4) loops.Add(loop);
            }
            if (loops.Count == 0) return;
            // the outer loop (largest anticlockwise area); a loop round a hole means pavement inside: not one island
            int outer = -1; float best2 = 0f;
            for (int q = 0; q < loops.Count; q++) { float a = SignedArea(loops[q]); if (a > best2) { best2 = a; outer = q; } }
            if (outer < 0 || loops.Count > 1) { IslandSkippedBig++; return; }
            // simplified (0.2 m), then one round of corner cutting for the noses
            isPoly.Clear();
            DouglasPeuckerClosed(loops[outer], 0.35f, isPoly);
            if (isPoly.Count < 3) return;
            var poly = Chaikin(isPoly);
            if (poly.Count < 3 || SignedArea(poly) < 0.5f) return;
            // heights: the pavement beside each corner
            int n = poly.Count;
            var yP = new float[n];
            for (int q = 0; q < n; q++) yP[q] = IsPaveYNear(poly[q]);
            // the top's material: concrete where it is too narrow to plant
            int maxD = 0; foreach (int k in part) if (isDist[k] < int.MaxValue / 2 && isDist[k] > maxD) maxD = isDist[k];
            bool concrete = 2f * 0.1f * maxD * IsCell < MedianConcreteMaxW;
            var top = buckets[(int)(concrete ? Slot.Pavement : Slot.Ground)];
            // ---- inside: every sheet that lay there goes -------------------------------------------------
            foreach (var bk in IslandSheetBuckets()) IsDropInside(bk, poly, yP);
            // the curb stands IslandInsetM inside the traced edge (the 0.5 m cells and the simplification put the
            // traced edge up to half a metre over the road's edge line); the apron covers the band between
            var inset = IsInset(poly, IslandInsetM);
            if (inset != null)
            {
                poly = inset;
                n = poly.Count;
                yP = new float[n];
                for (int q = 0; q < n; q++) yP[q] = IsPaveYNear(poly[q]);
            }
            // ---- the solid ----------------------------------------------------------------------------
            var walk = buckets[(int)Slot.Sidewalk];
            int k0 = part[0];
            var apron = IsFillBucket(k0);
            for (int q = 0; q < n; q++)
            {
                int r = (q + 1) % n;
                Vector2 a2 = poly[q], b2 = poly[r];
                var dirv = b2 - a2; float len = dirv.magnitude;
                if (len < 1e-3f) continue;
                var outw = new Vector2(dirv.y, -dirv.x) / len;   // anticlockwise: the outside is on the right
                float ya = yP[q], yb = yP[r];
                var A0 = new Vector3(a2.x, ya - IslandFootM, a2.y); var A1 = new Vector3(a2.x, ya + CurbH, a2.y);
                var B0 = new Vector3(b2.x, yb - IslandFootM, b2.y); var B1 = new Vector3(b2.x, yb + CurbH, b2.y);
                // the curb's face: the walk's curb texels (a dark joint column, CurbFaceU), u along the face
                float u0 = 0f, u1 = len / WalkTexM;
                walk.Face(A0, A1, B1, B0, new Vector3(outw.x, 0f, outw.y),
                          new Vector2(CurbFaceU, u0), new Vector2(CurbFaceU, u0 + 0.03f), new Vector2(CurbFaceU, u1 + 0.03f), new Vector2(CurbFaceU, u1));
                // the apron: asphalt from the foot out under the road
                var Ao = new Vector3(a2.x + outw.x * IslandApronM, ya - IslandApronDownM, a2.y + outw.y * IslandApronM);
                var Bo = new Vector3(b2.x + outw.x * IslandApronM, yb - IslandApronDownM, b2.y + outw.y * IslandApronM);
                var Ai = new Vector3(a2.x, ya - IslandApronDownM, a2.y); var Bi = new Vector3(b2.x, yb - IslandApronDownM, b2.y);
                apron.Up(Ai, Bi, Bo, Ao, FanUV(Ai, o), FanUV(Bi, o), FanUV(Bo, o), FanUV(Ao, o));
            }
            // the top: one sheet, ear-clipped in plan, each corner at its curb's height
            IsTopEarClip(top, poly, yP, concrete, o);
            IslandCount++; IslandAreaM2 += SignedArea(poly);
            if (IslandRecord)
            {
                var w = new Vector2[n];
                for (int q = 0; q < n; q++) w[q] = new Vector2(poly[q].x + o.x, poly[q].y + o.z);
                lock (IslandOutlines) IslandOutlines.Add(w);
            }
        }

        static float IsPaveYNear(Vector2 p)
        {
            // the lowest pavement within a metre (the cell's own when it is paved)
            float best = float.NaN, bd = float.MaxValue;
            for (int dj = -4; dj <= 4; dj++)
                for (int di = -4; di <= 4; di++)
                {
                    float x = p.x + di * IsCell, z = p.y + dj * IsCell;
                    if (!IsPaveAt(x, z, out float y)) continue;
                    float d = di * di + dj * dj;
                    if (d < bd) { bd = d; best = y; }
                }
            if (float.IsNaN(best) && IsCellAt(p.x, p.y, out int k)) best = isNearY[k];
            return float.IsNaN(best) ? 0f : best;
        }

        static bool InPoly(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static void IsDropInside(Bucket bk, List<Vector2> poly, float[] yP)
        {
            float bx0 = float.MaxValue, bz0 = float.MaxValue, bx1 = float.MinValue, bz1 = float.MinValue;
            foreach (var q in poly) { bx0 = Mathf.Min(bx0, q.x); bz0 = Mathf.Min(bz0, q.y); bx1 = Mathf.Max(bx1, q.x); bz1 = Mathf.Max(bz1, q.y); }
            float topMin = float.MaxValue; foreach (var y in yP) topMin = Mathf.Min(topMin, y);
            for (int pass = 0; pass < 2; pass++)
            {
                var tl = pass == 0 ? bk.t : bk.tn;
                if (tl == null || tl.Count == 0) continue;
                isKeep.Clear();
                int dropped = 0;
                for (int t = 0; t + 2 < tl.Count; t += 3)
                {
                    Vector3 a = bk.v[tl[t]], b = bk.v[tl[t + 1]], c = bk.v[tl[t + 2]];
                    if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < bx0 || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > bx1 || Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < bz0 || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > bz1)
                    { isKeep.Add(tl[t]); isKeep.Add(tl[t + 1]); isKeep.Add(tl[t + 2]); continue; }
                    bool ia = InPoly(poly, new Vector2(a.x, a.z)), ib = InPoly(poly, new Vector2(b.x, b.z)), ic = InPoly(poly, new Vector2(c.x, c.z));
                    if (ia && ib && ic) { dropped++; if (!ReferenceEquals(bk, kerbBucket)) IsNoteGone(a, b, c); continue; }
                    // a sheet reaching in from outside stays; one standing up through the top is counted
                    if ((ia && a.y > topMin + CurbH) || (ib && b.y > topMin + CurbH) || (ic && c.y > topMin + CurbH)) IslandPokeVerts++;
                    isKeep.Add(tl[t]); isKeep.Add(tl[t + 1]); isKeep.Add(tl[t + 2]);
                }
                if (dropped == 0) continue;
                IslandInsideTris += dropped;
                tl.Clear(); tl.AddRange(isKeep);
            }
        }

        static void IsTopEarClip(Bucket bk, List<Vector2> poly, float[] yP, bool concrete, Vector3 o)
        {
            int n = poly.Count;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int baseI = bk.v.Count;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(poly[i].x, yP[i] + CurbH, poly[i].y);
                bk.v.Add(p);
                bk.uv.Add(GroundUV(concrete, p.x + o.x, p.z + o.z));
                bk.col?.Add(Bucket.Tint);
                bk.fac?.Add(Vector4.zero);
            }
            int t0 = bk.t.Count;
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4 * n)
            {
                bool clipped = false;
                for (int q = 0; q < idx.Count; q++)
                {
                    int i0 = idx[(q + idx.Count - 1) % idx.Count], i1 = idx[q], i2 = idx[(q + 1) % idx.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y) <= 1e-7f) continue;   // reflex
                    bool holds = true;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(a, b, c, poly[j])) { holds = false; break; }
                    }
                    if (!holds) continue;
                    // anticlockwise in plan reads clockwise from above: (a, c, b) faces up
                    bk.t.Add(baseI + i0); bk.t.Add(baseI + i2); bk.t.Add(baseI + i1);
                    idx.RemoveAt(q);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { bk.t.Add(baseI + idx[0]); bk.t.Add(baseI + idx[2]); bk.t.Add(baseI + idx[1]); }
            else if (idx.Count > 3)
            {
                // the clipper found no ear (a pinched outline): the rest as a fan, so the top has no hole
                IslandTopFails++;
                for (int q = 1; q + 1 < idx.Count; q++) { bk.t.Add(baseI + idx[0]); bk.t.Add(baseI + idx[q + 1]); bk.t.Add(baseI + idx[q]); }
            }
            ProvHook?.Invoke(bk, bk.t, t0);
        }

        /// <summary>The outline moved d inward along each corner's bisector (mitre capped at 2 d); null where that
        /// turns it inside out or loses more than half its area (a narrow island keeps its traced outline).</summary>
        static List<Vector2> IsInset(List<Vector2> p, float d)
        {
            int n = p.Count;
            var r = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                var d1 = (b - a); var d2 = (c - b);
                if (d1.sqrMagnitude < 1e-8f || d2.sqrMagnitude < 1e-8f) { r.Add(b); continue; }
                d1.Normalize(); d2.Normalize();
                var n1 = new Vector2(-d1.y, d1.x); var n2 = new Vector2(-d2.y, d2.x);   // inside is on the left
                var m = n1 + n2;
                float len = m.magnitude;
                if (len < 1e-4f) { r.Add(b + n1 * d); continue; }
                m /= len;
                float k = d / Mathf.Max(0.5f, Vector2.Dot(m, n1));
                r.Add(b + m * Mathf.Min(k, 2f * d));
            }
            float a0 = SignedArea(p), a1 = SignedArea(r);
            if (a1 < 0.5f * a0) return null;
            // a corner pushed past its neighbours: any two edges crossing
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegCross(r[i], r[(i + 1) % n], r[j], r[(j + 1) % n])) return null;
                }
            return r;
        }

        static bool SegCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = IsCross2(b - a, c - a), d2 = IsCross2(b - a, d - a), d3 = IsCross2(d - c, a - c), d4 = IsCross2(d - c, b - c);
            return ((d1 > 0f) != (d2 > 0f)) && ((d3 > 0f) != (d4 > 0f));
        }
        static float IsCross2(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].x * p[i].y - p[i].x * p[j].y;
            return 0.5f * a;
        }

        static void DouglasPeuckerClosed(List<Vector2> pts, float eps, List<Vector2> outp)
        {
            // split the loop at its two furthest-apart points, simplify each half
            int n = pts.Count;
            int a = 0, b = 0; float best = -1f;
            for (int i = 0; i < n; i++) { float d = (pts[i] - pts[0]).sqrMagnitude; if (d > best) { best = d; b = i; } }
            best = -1f;
            for (int i = 0; i < n; i++) { float d = (pts[i] - pts[b]).sqrMagnitude; if (d > best) { best = d; a = i; } }
            if (a > b) { int t = a; a = b; b = t; }
            var keep = new bool[n];
            keep[a] = keep[b] = true;
            DP(pts, a, b, eps, keep);
            // the other half, wrapping
            var idx = new List<int>();
            for (int i = b; i != a; i = (i + 1) % n) idx.Add(i);
            idx.Add(a);
            DPIdx(pts, idx, 0, idx.Count - 1, eps, keep);
            for (int i = 0; i < n; i++) if (keep[i]) outp.Add(pts[i]);
        }

        static void DP(List<Vector2> p, int i0, int i1, float eps, bool[] keep)
        {
            if (i1 <= i0 + 1) return;
            float dmax = -1f; int im = -1;
            for (int i = i0 + 1; i < i1; i++) { float d = SegDist(p[i], p[i0], p[i1]); if (d > dmax) { dmax = d; im = i; } }
            if (dmax > eps) { keep[im] = true; DP(p, i0, im, eps, keep); DP(p, im, i1, eps, keep); }
        }

        static void DPIdx(List<Vector2> p, List<int> idx, int i0, int i1, float eps, bool[] keep)
        {
            if (i1 <= i0 + 1) return;
            float dmax = -1f; int im = -1;
            for (int i = i0 + 1; i < i1; i++) { float d = SegDist(p[idx[i]], p[idx[i0]], p[idx[i1]]); if (d > dmax) { dmax = d; im = i; } }
            if (dmax > eps) { keep[idx[im]] = true; DPIdx(p, idx, i0, im, eps, keep); DPIdx(p, idx, im, i1, eps, keep); }
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.sqrMagnitude;
            if (l2 < 1e-12f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return (a + ab * t - p).magnitude;
        }

        static List<Vector2> Chaikin(List<Vector2> p)
        {
            // cut each corner a quarter of the way along its two sides (at most 0.6 m): rounded noses
            var r = new List<Vector2>(p.Count * 2);
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                var d = b - a; float len = d.magnitude;
                if (len < 1e-4f) continue;
                float c = Mathf.Min(0.25f * len, 0.6f) / len;
                r.Add(a + d * c); r.Add(b - d * c);
            }
            return r;
        }
    }
}
