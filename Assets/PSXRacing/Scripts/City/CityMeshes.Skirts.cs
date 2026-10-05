using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // =====================================================================
        //  NO SHEET ENDS IN THE AIR (owner 2026-10-04, W Trade St at Graham
        //  St: "a thin layer of dirt and I can see under the dirt and the road
        //  to the right"). The ground is many sheets - the lattice, every
        //  road's verges, seams and half strips, the fans' chord verges and
        //  corner fills - and so are the roads. Wherever one ended over lower
        //  ground (a verge stopping at a deck's end, at a squeezed span, at a
        //  junction trim; a deck's corner past its approach; a fan's envelope
        //  between two mouths) its edge stood in the air: the PSX shaders cull
        //  back faces, so the driver saw under it, and the collider's edge
        //  stopped the car like a wall nobody built. The audit
        //  (Editor/CityGroundEdges) found 1,441 open metres in the OwnerBox and
        //  1,302 on the race routes' tiles; 1,000 of them were verge ends.
        // =====================================================================

        /// <summary>An open edge standing more than this over the lattice
        /// beneath it gets its face (the lattice is pinned 10 cm under a road's
        /// profile and a verge starts 2 cm under its edge, so an ordinary
        /// verge's inner edge stays under it).</summary>
        public const float SkirtMinM = 0.10f;
        /// <summary>The longest stretch of one face quad: the lattice under it
        /// bends at its 8 m corners.</summary>
        const float SkirtStepM = 2f;
        /// <summary>Past this over the lattice a road's open edge is a deck's
        /// (a fascia a deck deep), not a step in the ground.</summary>
        const float StructureDropM = 2.5f;
        /// <summary>A closing face taller than this is drawn as a retaining
        /// wall (concrete); a lower one is the cut edge of its own sheet.</summary>
        const float RetainFaceM = 0.5f;
        /// <summary>A closing face of a step no deeper than this under the land
        /// within a metre out (<see cref="LandOut"/>) is drawn render-only
        /// with the kerbs: RoadsideRules.OpenDropM, where the roadside rules
        /// stop calling it a step or a ledge and call it a drop.</summary>
        const float SoftStepM = RoadsideRules.OpenDropM;
        /// <summary>PSX_CITY_SKIRTS=0: no closing faces (the audit's before-count).</summary>
        static readonly bool SkirtsOff = System.Environment.GetEnvironmentVariable("PSX_CITY_SKIRTS") == "0";
        /// <summary>PSX_CITY_ENVELOPE_VERGE=0: a fan's envelope stretches stay bare (the before-count).</summary>
        static readonly bool EnvelopeVergeOff = System.Environment.GetEnvironmentVariable("PSX_CITY_ENVELOPE_VERGE") == "0";

        /// <summary>The tile's last build (for the audit): faces laid, metres,
        /// deck fascias, the pass's ms.</summary>
        public static int skirtCount; public static float skirtMetres, fasciaMetres; public static double skirtMs;
        /// <summary>The tile's last build: of those faces, the steps a car
        /// rolls over, drawn render-only with the kerbs.</summary>
        public static int skirtSoftCount; public static float skirtSoftMetres;
        /// <summary>Probe only: ms summed per phase of the pass (sets, ground weld, ground faces, roads weld, grid, roads faces).</summary>
        public static readonly double[] skirtPhase = new double[6];
        /// <summary>Probe only: road open edges seen, past the verge test, past the drop test, paved past, deck, step.</summary>
        public static readonly long[] skirtRoadCounts = new long[6];

        // ---- plain open-addressing tables: the pass runs on every tile build,
        // and Dictionary<long, ...> cost it 50-150 ms on uptown's tiles --------
        const long EmptyKey = long.MinValue;
        static long[] tKeys = new long[1 << 16]; static int[] tVal = new int[1 << 16]; static int tMask;
        static long[] eKeys = new long[1 << 17]; static int[] eCnt = new int[1 << 17], eThird = new int[1 << 17]; static byte[] eBk = new byte[1 << 17]; static int eMask;
        static long[] xKeys = new long[1 << 16]; static int xMask;
        static Vector3[] skirtPos = new Vector3[1 << 15];
        static int[] skirtId = new int[1 << 15];
        static int[] skirtTri = new int[1 << 16];      // the roads' triangles, global vertex indices
        static int skirtTriCount;
        /// <summary>The ground buckets' vertex and index counts when BuildGround
        /// ends (the lattice, the lots, the driveways: laid on the lattice).</summary>
        static readonly int[] groundBaseV = new int[2], groundBaseT = new int[2];

        static int Cap(int n) { int c = 1 << 12; while (c < n * 2) c <<= 1; return c; }
        static int Hash(long k, int mask) => (int)(((ulong)k * 0x9E3779B97F4A7C15UL) >> 33) & mask;
        static long Q(float x) => (long)((x + 16f) * 500f + 0.5f);
        static long Key3(Vector3 v) => (Q(v.x) << 38) | ((Q(v.z) & 0x3FFFF) << 20) | ((long)((v.y + 200f) * 500f + 0.5f) & 0xFFFFF);
        static long KeyXZ(Vector3 v) => (Q(v.x) << 32) | (Q(v.z) & 0xFFFFFFFF);

        static void ClearTable(ref long[] keys, int cap)
        {
            if (keys.Length < cap) keys = new long[cap];
            for (int i = 0; i < cap; i++) keys[i] = EmptyKey;
        }

        static void CloseOpenGround(CityMap map, Trims trims, TileMeshes tm)
        {
            skirtCount = 0; skirtMetres = 0f; fasciaMetres = 0f; skirtMs = 0; skirtSoftCount = 0; skirtSoftMetres = 0f;
            if (SkirtsOff) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { CloseOpenGroundIn(map, trims, tm); } finally { skirtMs = clock.Elapsed.TotalMilliseconds; }
        }

        /// <summary>
        /// THE CLOSING FACES. After every sheet of the tile is laid (before
        /// the lots' islands, whose curbs are their own faces):
        /// <list type="bullet">
        /// <item>The ground's sheets are welded by position (2 mm); every edge
        /// one triangle uses, off the tile's border, that is not a strip's
        /// inner edge (under its own road) and stands more than
        /// <see cref="SkirtMinM"/> over the lattice, gets a face straight down
        /// to the lattice (tucked RoadsideRules.ToeTuckM under), turned away
        /// from its sheet, in its material.</item>
        /// <item>The roads' sheets the same way; an open road edge that a
        /// ground sheet starts from (a verge) or that pavement carries on past
        /// at its height (a mitre, a clipped branch, a mouth) is no step. One
        /// over a road that passes under it, or more than
        /// <see cref="StructureDropM"/> over the ground, is a deck's edge: a
        /// fascia a deck deep, in the concrete, where no standing concrete
        /// face is drawn along it already. Any other is a step in the ground:
        /// its face to the lattice (from a kerb's foot where a kerb is drawn).</item>
        /// </list>
        /// A face over a drop is in the ground or road mesh, so
        /// its collider is exactly what is drawn: a retaining face, never a
        /// see-through edge and never an invisible blocker. A step, not a drop
        /// (<see cref="SoftStepM"/> under the land <see cref="LandOut"/> finds),
        /// is drawn render-only with the kerbs, in their concrete, as the kerb's
        /// own inch is: closed to the eye, and no wall for the body box coming
        /// back onto a grounded edge (the sheet's edge is what the car meets).
        /// </summary>
        static void CloseOpenGroundIn(CityMap map, Trims trims, TileMeshes tm)
        {
            foreach (var b in buckets) { b.passV = b.v.Count; b.passT = b.t.Count; }
            var g = buckets[(int)Slot.Ground];
            var pv = buckets[(int)Slot.Pavement];
            var o = tm.origin;

            // where the strips' inner edges run: each segment chained into
            // the half-metre cells it crosses, so a road edge a verge (seam,
            // half strip, chord verge) starts from finds it within 8 cm
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int samples = 0;
            foreach (var (s0, s1) in stripInnerSegs) samples += 2 + (int)(Vector2.Distance(new Vector2(s0.x, s0.z), new Vector2(s1.x, s1.z)) / InnerStepM);
            int xc = Cap(samples); ClearTable(ref xKeys, xc); xMask = xc - 1;
            if (xHead.Length < xc) xHead = new int[xc];
            if (nodeSeg.Length < samples) { nodeSeg = new int[samples + samples / 2]; nodeNext = new int[samples + samples / 2]; }
            int nodes = 0;
            for (int si = 0; si < stripInnerSegs.Count; si++)
            {
                var (s0, s1) = stripInnerSegs[si];
                int m = 1 + (int)(Vector2.Distance(new Vector2(s0.x, s0.z), new Vector2(s1.x, s1.z)) / InnerStepM);
                for (int j = 0; j <= m; j++)
                {
                    var v = Vector3.Lerp(s0, s1, (float)j / m);
                    long k = CellKey(v.x, v.z);
                    int h = Hash(k, xMask);
                    while (xKeys[h] != EmptyKey && xKeys[h] != k) h = (h + 1) & xMask;
                    if (xKeys[h] == EmptyKey) { xKeys[h] = k; xHead[h] = -1; }
                    if (xHead[h] >= 0 && nodeSeg[xHead[h]] == si) continue;   // this segment is in this cell already
                    nodeSeg[nodes] = si; nodeNext[nodes] = xHead[h]; xHead[h] = nodes++;
                }
            }
            // standing concrete faces' edges in plan: a fascia already along a deck edge
            skirtConEdges.Clear();
            var cb = buckets[(int)Slot.Concrete];
            foreach (var sb in new[] { cb, barrierBucket })   // the deck's concrete and the parapets standing on its edges
                for (int i = 0, tEnd = sb == cb ? cb.passT : sb.t.Count; i + 2 < tEnd; i += 3)
                {
                    Vector3 c0 = sb.v[sb.t[i]], c1 = sb.v[sb.t[i + 1]], c2 = sb.v[sb.t[i + 2]];
                    var n3 = Vector3.Cross(c1 - c0, c2 - c0);
                    if (Mathf.Abs(n3.y) > 0.3f * n3.magnitude) continue;   // a soffit shares the deck's plan
                    float lo = Mathf.Min(c0.y, Mathf.Min(c1.y, c2.y));   // how far down the face hangs
                    ConFace(PairXZ(c0, c1), lo); ConFace(PairXZ(c1, c2), lo); ConFace(PairXZ(c2, c0), lo);
                }
            skirtKerb.Clear();
            foreach (var v in kerbBucket.v) skirtKerb.Add(KeyXZ(v));

            // the ground's own sheets: a face to the lattice, in their material
            skirtPhase[0] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            // the roads' lying triangles first: both passes ask what pavement
            // lies past or under an edge
            roadSheets.Clear();
            foreach (var s in RoadAndStructureSlots) roadSheets.Add(buckets[(int)s]);
            roadGrid.Build(roadSheets);
            skirtSheets.Clear(); skirtSheets.Add(g); skirtSheets.Add(pv);
            groundGrid.Build(skirtSheets, groundBaseT);
            OpenEdges(skirtSheets, false, groundBaseV, groundBaseT);
            skirtPhase[1] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            foreach (var (A, B, nrm, bi) in skirtOpen)
            {
                // a strip's inner edge: under its own road's edge, never seen
                if (stripInner.Contains(KeyXZ(A)) && stripInner.Contains(KeyXZ(B))) continue;
                // over another road: never a face down through its lanes
                if (OverRoad(map, trims, o, A, B, nrm)) continue;
                bool paved = bi == 1;
                SkirtDown(map, o, paved ? pv : g, paved, A, B, nrm);
            }

            skirtPhase[2] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            // the roads' sheets (ribbons, fans, decks and their concrete)
            skirtSheets.Clear();
            foreach (var s in RoadAndStructureSlots) skirtSheets.Add(buckets[(int)s]);
            OpenEdges(skirtSheets, true);
            skirtPhase[3] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            skirtPhase[4] += sw.Elapsed.TotalMilliseconds; sw.Restart();
            var con = cb;
            foreach (var (A0, B0, nrm, bi) in skirtOpen)
            {
            // in pieces of 2 m at most: a deck's end runs on past its approach's
            // pavement at both corners, and only the corners stand over the drop
            int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(A0.x, A0.z), new Vector2(B0.x, B0.z)) / SkirtStepM));
            for (int pc = 0; pc < pieces; pc++)
            {
                var A = Vector3.Lerp(A0, B0, (float)pc / pieces); var B = Vector3.Lerp(A0, B0, (float)(pc + 1) / pieces);
                skirtRoadCounts[0]++;
                // a verge (or seam, or half strip) starts from it (cheapest first)
                if (Verged(A, B, nrm)) continue;
                skirtRoadCounts[1]++;
                var M = (A + B) * 0.5f;
                float lM = LatticeY(map, M.x + o.x, M.z + o.z);
                if (M.y - lM < SkirtMinM && A.y - LatticeY(map, A.x + o.x, A.z + o.z) < SkirtMinM && B.y - LatticeY(map, B.x + o.x, B.z + o.z) < SkirtMinM) continue;
                skirtRoadCounts[2]++;
                // other pavement carries on past it at its height
                if (PavedPast(A, B, nrm)) continue;
                skirtRoadCounts[3]++;
                // a road under it: only a drop of a metre or more can clear one
                bool deck = M.y - lM > StructureDropM || (M.y - lM > 1f && RoadUnder(map, new Vector2(M.x + o.x + nrm.x * 0.5f, M.z + o.z + nrm.y * 0.5f), M.y));
                if (deck)
                {
                    // a standing face hangs along it already, below the deck (a fascia;
                    // a parapet's end cap stands ON the deck and closes nothing under it)
                    if (skirtConEdges.TryGetValue(PairXZ(A0, B0), out float hangs) && hangs < Mathf.Min(A0.y, B0.y) - 0.2f) continue;
                    float dk = Mathf.Min(CityElevation.DeckThick, M.y - lM);
                    float u = Vector2.Distance(new Vector2(A.x, A.z), new Vector2(B.x, B.z));
                    con.Face(new Vector3(A.x, A.y - dk, A.z), A, B, new Vector3(B.x, B.y - dk, B.z), new Vector3(nrm.x, 0f, nrm.y),
                             new Vector2(0f, 0f), new Vector2(0f, dk), new Vector2(u, dk), new Vector2(u, 0f));
                    fasciaMetres += u;
                    skirtLog?.Add((M + o, $"deck fascia top {M.y + o.y:0.00} dk {dk:0.00} lattice {lM + o.y:0.00} nrm ({nrm.x:0.00},{nrm.y:0.00})"));
                    continue;
                }
                bool paved = PavedAt(map, M.x + o.x, M.z + o.z);
                // under a kerb's drawn inch the face starts at the kerb's foot
                if (OverRoad(map, trims, o, A, B, nrm)) continue;   // over another road: never a face down through its lanes
                float kerb = skirtKerb.Contains(KeyXZ(A0)) && skirtKerb.Contains(KeyXZ(B0)) ? KerbFaceM : 0f;
                SkirtDown(map, o, paved ? pv : g, paved, A + Vector3.down * kerb, B + Vector3.down * kerb, nrm);
            }
            }
            skirtPhase[5] += sw.Elapsed.TotalMilliseconds;
        }

        static readonly List<Bucket> skirtSheets = new List<Bucket>(96), roadSheets = new List<Bucket>(96);
        static readonly List<(Vector3 a, Vector3 b, Vector2 nrm, int bucket)> skirtOpen = new List<(Vector3, Vector3, Vector2, int)>(1024);
        static readonly HashSet<long> skirtKerb = new HashSet<long>();
        static readonly Dictionary<(long, long), float> skirtConEdges = new Dictionary<(long, long), float>();
        static void ConFace((long, long) k, float lo) { if (!skirtConEdges.TryGetValue(k, out float was) || lo < was) skirtConEdges[k] = lo; }
        /// <summary>Every strip profile's first point this tile build (plan, 2 mm):
        /// its inner edge, under its own road's edge.</summary>
        static readonly HashSet<long> stripInner = new HashSet<long>();
        /// <summary>Every strip quad's inner edge this tile build (tile-local).</summary>
        static readonly List<(Vector3, Vector3)> stripInnerSegs = new List<(Vector3, Vector3)>(4096);
        static readonly HashSet<int> skirtNear = new HashSet<int>();
        static (long, long) PairXZ(Vector3 a, Vector3 b) { long ka = KeyXZ(a), kb = KeyXZ(b); return ka < kb ? (ka, kb) : (kb, ka); }

        const float InnerCellM = 0.5f, InnerStepM = 0.25f;
        static long CellKey(float x, float z) =>
            ((long)Mathf.FloorToInt((x + 16f) / InnerCellM) << 32) | (uint)Mathf.FloorToInt((z + 16f) / InnerCellM);

        /// <summary>Does a strip start from the open road edge A..B: at its
        /// quarter points (or a quarter metre in under the pavement, where a
        /// half strip starts), a strip's inner edge from 15 cm under the edge
        /// to 2 cm over.</summary>
        static bool Verged(Vector3 A, Vector3 B, Vector2 nrm)
        {
            for (int k = 1; k <= 3; k++)
            {
                var p = Vector3.Lerp(A, B, k * 0.25f);
                if (!InnerAt(p.x, p.z, p.y) && !InnerAt(p.x - nrm.x * 0.25f, p.z - nrm.y * 0.25f, p.y)) return false;
            }
            return true;
        }

        static int[] xHead = new int[1 << 16], nodeSeg = new int[1 << 15], nodeNext = new int[1 << 15];

        /// <summary>A strip's inner edge passes within 8 cm of this plan
        /// point, from 15 cm under <paramref name="y"/> to 2 cm over.</summary>
        static bool InnerAt(float x, float z, float y)
        {
            long k = CellKey(x, z);
            int h = Hash(k, xMask);
            while (xKeys[h] != EmptyKey && xKeys[h] != k) h = (h + 1) & xMask;
            if (xKeys[h] != k) return false;
            for (int nd = xHead[h]; nd >= 0; nd = nodeNext[nd])
            {
                var (s0, s1) = stripInnerSegs[nodeSeg[nd]];
                float dx = s1.x - s0.x, dz = s1.z - s0.z, L2 = dx * dx + dz * dz;
                float t = L2 > 1e-8f ? Mathf.Clamp01(((x - s0.x) * dx + (z - s0.z) * dz) / L2) : 0f;
                float px = s0.x + dx * t - x, pz = s0.z + dz * t - z;
                if (px * px + pz * pz > 0.08f * 0.08f) continue;
                float sy = s0.y + (s1.y - s0.y) * t;
                if (sy <= y + 0.02f && sy >= y - 0.15f) return true;
            }
            return false;
        }

        /// <summary>The open edges of these buckets taken as ONE sheet, welded
        /// by position (2 mm): each edge one triangle uses, off the tile's
        /// border, with its outward unit (away from its triangle) and the index
        /// of its bucket. Into <see cref="skirtOpen"/>; <paramref name="keepTris"/>
        /// keeps the triangles (global vertex indices) for <see cref="roadGrid"/>.</summary>
        static void OpenEdges(List<Bucket> sheets, bool keepTris, int[] sheetV0 = null, int[] sheetT0 = null)
        {
            skirtOpen.Clear();
            skirtTriCount = 0;
            // each sheet's [start, end): from after the lattice (ground) to
            // what the pass found there (never the faces it adds itself)
            int n = 0, nEff = 0, ntEff = 0;
            for (int bi = 0; bi < sheets.Count; bi++)
            {
                int v0 = sheetV0 != null ? sheetV0[bi] : 0, t0 = sheetT0 != null ? sheetT0[bi] : 0;
                int v1 = PassV(sheets[bi]), t1 = PassT(sheets[bi]);
                n += v1; nEff += Mathf.Max(0, v1 - v0); ntEff += Mathf.Max(0, t1 - t0);
            }
            if (n == 0) return;
            if (skirtPos.Length < n) { skirtPos = new Vector3[n + n / 2]; skirtId = new int[n + n / 2]; }
            int tc = Cap(nEff); ClearTable(ref tKeys, tc); tMask = tc - 1;
            if (tVal.Length < tc) tVal = new int[tc];
            int off = 0;
            for (int bi = 0; bi < sheets.Count; bi++)
            {
                var vs = sheets[bi].v;
                int v1 = PassV(sheets[bi]);
                for (int i = sheetV0 != null ? sheetV0[bi] : 0; i < v1; i++)
                {
                    int gi = off + i;
                    var v = vs[i];
                    skirtPos[gi] = v;
                    long key = Key3(v);
                    int h = Hash(key, tMask);
                    while (tKeys[h] != EmptyKey && tKeys[h] != key) h = (h + 1) & tMask;
                    if (tKeys[h] == EmptyKey) { tKeys[h] = key; tVal[h] = gi; }
                    skirtId[gi] = tVal[h];
                }
                off += v1;
            }
            int ec = Cap(ntEff); ClearTable(ref eKeys, ec); eMask = ec - 1;
            if (eCnt.Length < ec) { eCnt = new int[ec]; eThird = new int[ec]; eBk = new byte[ec]; }
            if (keepTris && skirtTri.Length < ntEff) skirtTri = new int[ntEff + ntEff / 2];
            off = 0;
            for (int bi = 0; bi < sheets.Count; bi++)
            {
                var t = sheets[bi].t;
                int v0 = sheetV0 != null ? sheetV0[bi] : 0, v1 = PassV(sheets[bi]), t1 = PassT(sheets[bi]);
                for (int i = sheetT0 != null ? sheetT0[bi] - sheetT0[bi] % 3 : 0; i + 2 < t1; i += 3)
                {
                    int ta = t[i], tb = t[i + 1], tcv = t[i + 2];
                    if (ta < v0 || tb < v0 || tcv < v0 || ta >= v1 || tb >= v1 || tcv >= v1) continue;
                    if (keepTris) { skirtTri[skirtTriCount++] = ta + off; skirtTri[skirtTriCount++] = tb + off; skirtTri[skirtTriCount++] = tcv + off; }
                    for (int k = 0; k < 3; k++)
                    {
                        int a = skirtId[t[i + k] + off], c = skirtId[t[i + (k + 1) % 3] + off];
                        if (a == c) continue;
                        long ek = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                        int h = Hash(ek, eMask);
                        while (eKeys[h] != EmptyKey && eKeys[h] != ek) h = (h + 1) & eMask;
                        if (eKeys[h] == EmptyKey) { eKeys[h] = ek; eCnt[h] = 1; eThird[h] = t[i + (k + 2) % 3] + off; eBk[h] = (byte)Mathf.Min(bi, 255); }
                        else eCnt[h]++;
                    }
                }
                off += v1;
            }
            float T = TileSize;
            for (int h = 0; h <= eMask; h++)
            {
                if (eKeys[h] == EmptyKey || eCnt[h] != 1) continue;
                long ek = eKeys[h];
                Vector3 A = skirtPos[(int)(ek >> 32)], B = skirtPos[(int)(ek & 0xFFFFFFFF)], C = skirtPos[eThird[h]];
                if (OnTileBorder(A, T) && OnTileBorder(B, T) && (Mathf.Abs(A.x - B.x) < 0.02f || Mathf.Abs(A.z - B.z) < 0.02f)) continue;
                var d2 = new Vector2(B.x - A.x, B.z - A.z);
                float len = d2.magnitude;
                if (len < 0.02f) continue;
                var nrm = new Vector2(-d2.y, d2.x) / len;
                if (Vector2.Dot(new Vector2(C.x - A.x, C.z - A.z), nrm) > 0f) nrm = -nrm;   // away from its sheet
                skirtOpen.Add((A, B, nrm, eBk[h]));
            }
        }

        static int PassV(Bucket b) => b.passV;
        static int PassT(Bucket b) => b.passT;

        static bool OnTileBorder(Vector3 q, float T) =>
            Mathf.Abs(q.x) < 0.02f || Mathf.Abs(q.z) < 0.02f || Mathf.Abs(q.x - T) < 0.02f || Mathf.Abs(q.z - T) < 0.02f;

        // ---- a set of sheets' lying triangles in 4 m cells (counts, then a
        // flat index): "does pavement carry on past this edge at its height",
        // "what land lies a metre out from this face" --------------------------
        const float GridM = 4f, GridPadM = 8f;
        const int GridN = (int)((TileSize + 2 * GridPadM) / GridM) + 1;
        static int Cell(float x) => Mathf.Clamp(Mathf.FloorToInt((x + GridPadM) / GridM), 0, GridN - 1);

        sealed class TriGrid
        {
            readonly int[] start = new int[GridN * GridN + 1];
            readonly int[] fillAt = new int[GridN * GridN];
            int[] tris = new int[1 << 15];
            Vector3[] p = new Vector3[1 << 15];   // the lying triangles, three corners each (tile-local)
            int count;

            /// <summary>The sheets' lying triangles as the pass found them
            /// (each from <paramref name="t0"/>'s index on, when given).</summary>
            public void Build(List<Bucket> sheets, int[] t0 = null)
            {
                count = 0;
                int nt = 0;
                for (int bi = 0; bi < sheets.Count; bi++) nt += Mathf.Max(0, sheets[bi].passT - (t0 != null ? t0[bi] : 0)) / 3;
                if (p.Length < 3 * nt) p = new Vector3[3 * nt + nt];
                for (int bi = 0; bi < sheets.Count; bi++)
                {
                    var b = sheets[bi];
                    for (int i = t0 != null ? t0[bi] - t0[bi] % 3 : 0; i + 2 < b.passT; i += 3)
                    {
                        Vector3 p0 = b.v[b.t[i]], p1 = b.v[b.t[i + 1]], p2 = b.v[b.t[i + 2]];
                        float d = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                        if (Mathf.Abs(d) < 1e-6f) continue;               // standing faces carry nothing
                        p[3 * count] = p0; p[3 * count + 1] = p1; p[3 * count + 2] = p2;
                        count++;
                    }
                }
                System.Array.Clear(start, 0, start.Length);
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1)
                    {
                        int sum = 0;
                        for (int c = 0; c < GridN * GridN; c++) { int k = start[c]; start[c] = sum; sum += k; }
                        start[GridN * GridN] = sum;
                        if (tris.Length < sum) tris = new int[sum + sum / 2];
                        System.Array.Copy(start, fillAt, GridN * GridN);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 p0 = p[3 * i], p1 = p[3 * i + 1], p2 = p[3 * i + 2];
                        int x0 = Cell(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), x1 = Cell(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)));
                        int z0 = Cell(Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z))), z1 = Cell(Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z)));
                        for (int cz = z0; cz <= z1; cz++)
                            for (int cx = x0; cx <= x1; cx++)
                            {
                                int c = cz * GridN + cx;
                                if (pass == 0) start[c]++;
                                else tris[fillAt[c]++] = i;
                            }
                    }
                }
            }

            /// <summary>The heights of the lying triangles over a plan point
            /// (tile-local) between <paramref name="lo"/> and <paramref name="hi"/>:
            /// with <paramref name="any"/>, true at the first (returns hi); else
            /// the highest, or negative infinity.</summary>
            public float Between(float qx, float qz, float lo, float hi, bool any = false)
            {
                float best = float.NegativeInfinity;
                int c = Cell(qz) * GridN + Cell(qx);
                for (int j = start[c]; j < start[c + 1]; j++)
                {
                    int i = tris[j];
                    Vector3 p0 = p[3 * i], p1 = p[3 * i + 1], p2 = p[3 * i + 2];
                    float d = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                    float w0 = ((p1.z - p2.z) * (qx - p2.x) + (p2.x - p1.x) * (qz - p2.z)) / d;
                    float w1 = ((p2.z - p0.z) * (qx - p2.x) + (p0.x - p2.x) * (qz - p2.z)) / d;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    float h = w0 * p0.y + w1 * p1.y + w2 * p2.y;
                    if (h < lo || h > hi || h <= best) continue;
                    if (any) return hi;
                    best = h;
                }
                return best;
            }
        }

        /// <summary>The roads' lying triangles (ribbons, fans, decks).</summary>
        static readonly TriGrid roadGrid = new TriGrid();
        /// <summary>The ground's own sheets past the lattice (verges, seams,
        /// half strips, chord verges, corner fills).</summary>
        static readonly TriGrid groundGrid = new TriGrid();

        /// <summary>The heights of the lying road triangles over a plan point
        /// (tile-local): one within 0.15 m of <paramref name="y"/>, or (with
        /// <paramref name="below"/>) one more than 0.15 m under it.</summary>
        static bool RoadsAt(float qx, float qz, float y, bool below) =>
            below ? roadGrid.Between(qx, qz, float.NegativeInfinity, y - 0.15f, true) > float.NegativeInfinity
                  : roadGrid.Between(qx, qz, y - 0.15f, y + 0.15f, true) > float.NegativeInfinity;

        /// <summary>
        /// THE LAND A CAR COMES BACK FROM: the highest surface within a metre
        /// out from a face's middle (tile-local; <see cref="LandOutM"/>) - the
        /// lattice, the ground's sheets, the roads - up to a hand over the
        /// face's top. The roadside audit's body box is a ray
        /// RoadsideRules.CarClearanceFloorM over the ground a metre out from a
        /// grounded edge, cast back at it, so a face standing anywhere in that
        /// metre meets it: a face of a step no deeper than
        /// <see cref="SoftStepM"/> under that land is drawn render-only. Past the tile's border the
        /// next tile's sheets are not built yet: there the land is not known and
        /// the face is taken as a step (the collider stays as it was before the
        /// pass: the sheet's own edge).
        /// </summary>
        static float LandOut(CityMap map, Vector3 o, Vector3 M, Vector2 nrm, float top)
        {
            float hi = top + 0.3f, best = float.NegativeInfinity;
            for (int k = 0; k < LandOutM.Length; k++)
            {
                float qx = M.x + nrm.x * LandOutM[k], qz = M.z + nrm.y * LandOutM[k];
                if (qx < 0f || qz < 0f || qx > TileSize || qz > TileSize) { landSeen[k] = float.PositiveInfinity; return top; }
                float land = LatticeY(map, qx + o.x, qz + o.z);
                land = Mathf.Max(land, roadGrid.Between(qx, qz, land, hi));
                land = Mathf.Max(land, groundGrid.Between(qx, qz, land, hi));
                landSeen[k] = land;
                best = Mathf.Max(best, land);
            }
            return best;
        }
        static readonly float[] LandOutM = { 0.25f, 0.5f, 1f };
        static readonly float[] landSeen = new float[3];
        /// <summary>Probe only (null in the game): every closing face's
        /// decision, world plan point first.</summary>
        public static List<(Vector3 at, string what)> skirtLog;

        /// <summary>Does a road surface carry on past the open road edge A..B
        /// (tile-local) at its height: 0.6 m out at its quarter points, a lying
        /// road triangle within 0.15 m of the edge's height.</summary>
        static bool PavedPast(Vector3 A, Vector3 B, Vector2 nrm)
        {
            for (int k = 1; k <= 3; k += 2)   // its quarter points
            {
                var q = Vector3.Lerp(A, B, k * 0.25f);
                if (!RoadsAt(q.x + nrm.x * 0.6f, q.z + nrm.y * 0.6f, q.y, false)) return false;
            }
            return true;
        }

        /// <summary>The face under one open edge (tile-local A..B), straight
        /// down to the lattice tucked RoadsideRules.ToeTuckM under it, in
        /// <paramref name="bk"/>, turned to <paramref name="nrm"/>; in 2 m
        /// pieces, each only where an end stands SkirtMinM over the lattice.</summary>
        static void SkirtDown(CityMap map, Vector3 o, Bucket bk, bool paved, Vector3 A, Vector3 B, Vector2 nrm)
        {
            float len = Vector2.Distance(new Vector2(A.x, A.z), new Vector2(B.x, B.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / SkirtStepM));
            var facing = new Vector3(nrm.x, 0f, nrm.y);
            Vector3 P = A;
            float lP = LatticeY(map, A.x + o.x, A.z + o.z);
            for (int s = 1; s <= steps; s++)
            {
                var Q = s == steps ? B : Vector3.Lerp(A, B, (float)s / steps);
                float lQ = LatticeY(map, Q.x + o.x, Q.z + o.z);
                if (P.y - lP >= SkirtMinM || Q.y - lQ >= SkirtMinM)
                {
                    float yP = Mathf.Min(P.y, lP - RoadsideRules.ToeTuckM), yQ = Mathf.Min(Q.y, lQ - RoadsideRules.ToeTuckM);
                    float hP = P.y - yP, hQ = Q.y - yQ;
                    float top = Mathf.Max(P.y, Q.y);
                    // a step, not a drop (the land within a metre out no more than
                    // RoadsideRules.OpenDropM under its top): drawn with the kerbs,
                    // RENDER-ONLY, as the kerb's own inch is. The sheet's own edge
                    // stands inside every car's body there, so the body meets it
                    // where it would meet the face and no car slips under it; in
                    // the collider the face was a vertical wall the body box met
                    // coming back onto 142 grounded edges, most ~9 cm over the verge
                    // beyond (2026-10-04). Past OpenDropM the face collides: a car
                    // could pass under the edge it closes.
                    for (int k = 0; k < landSeen.Length; k++) landSeen[k] = float.NaN;
                    float landOut = LandOut(map, o, (P + Q) * 0.5f, nrm, top);
                    bool rolls = top - landOut <= SoftStepM;
                    skirtLog?.Add(((P + Q) * 0.5f + o, $"skirt {(rolls ? "ROLLS" : "solid")} {(bk == buckets[(int)Slot.Ground] ? "ground" : "paved")} top {top + o.y:0.00} lattice {Mathf.Min(lP, lQ) + o.y:0.00} land {landOut + o.y:0.00} [{landSeen[0] + o.y:0.00} {landSeen[1] + o.y:0.00} {landSeen[2] + o.y:0.00}] h {Mathf.Max(hP, hQ):0.00} nrm ({nrm.x:0.00},{nrm.y:0.00})"));
                    if (rolls || Mathf.Max(hP, hQ) > RetainFaceM)
                    {
                        // a retaining face: concrete, like the walls and fascias round it
                        var con = rolls ? kerbBucket : buckets[(int)Slot.Concrete];
                        float u0 = (P.x + P.z) * 0.5f, u1 = u0 + Vector2.Distance(new Vector2(P.x, P.z), new Vector2(Q.x, Q.z));
                        con.Face(new Vector3(P.x, yP, P.z), P, Q, new Vector3(Q.x, yQ, Q.z), facing,
                                 new Vector2(u0, 0f), new Vector2(u0, hP), new Vector2(u1, hQ), new Vector2(u1, 0f));
                        if (rolls) { skirtSoftCount++; skirtSoftMetres += Vector2.Distance(new Vector2(P.x, P.z), new Vector2(Q.x, Q.z)); }
                    }
                    else
                        bk.Face(new Vector3(P.x, yP, P.z), P, Q, new Vector3(Q.x, yQ, Q.z), facing,
                                GroundUV(paved, P.x + o.x, P.z + o.z + hP), GroundUV(paved, P.x + o.x, P.z + o.z),
                                GroundUV(paved, Q.x + o.x, Q.z + o.z), GroundUV(paved, Q.x + o.x, Q.z + o.z + hQ));
                    skirtCount++; skirtMetres += Vector2.Distance(new Vector2(P.x, P.z), new Vector2(Q.x, Q.z));
                }
                P = Q; lP = lQ;
            }
        }

        /// <summary>A car's height and a hand: the least clearance a fascia
        /// hung over another road leaves under it.</summary>
        const float CarClearM = 1.6f;

        /// <summary>
        /// OVER ANOTHER ROAD. Where pavement lies under an open edge (just past
        /// it, at its ends and middle; this tile's road triangles, and for a
        /// face a metre or more tall the map's roads, whose pavement may belong
        /// to the next tile), a face down to the lattice would stand in that
        /// road's lanes (North Tryon Street under the ramp e2382, 6 m down). The
        /// edge gets a fascia instead - a deck deep, never closer than
        /// <see cref="CarClearM"/> to the road under it - or, under a lower
        /// clearance, nothing (the edge is the overhang the car meets already).
        /// True when the edge is handled so.
        /// </summary>
        static bool OverRoad(CityMap map, Trims trims, Vector3 o, Vector3 A, Vector3 B, Vector2 nrm)
        {
            var M = (A + B) * 0.5f;
            // the tile's own pavement under it first (cheap); the map's lanes
            // decide, so a road whose pavement belongs to the next tile counts
            float under = float.NegativeInfinity;
            for (int k = 0; k <= 2; k++)
            {
                var q = Vector3.Lerp(A, B, k * 0.5f);
                under = Mathf.Max(under, RoadTopUnder(q.x + nrm.x * 0.1f, q.z + nrm.y * 0.1f, q.y));
            }
            // the drawn pavement of this tile decides; the map only within 3 m
            // of the tile's border, where the pavement under may be the next
            // tile's (North Tryon Street under the ramp e2382, 1.4 m over it)
            float T = TileSize;
            bool nearBorder = M.x < 3f || M.z < 3f || M.x > T - 3f || M.z > T - 3f;
            if (float.IsNegativeInfinity(under) && !nearBorder) return false;
            float lane = float.NegativeInfinity;
            for (int k = 0; k <= 2; k++)
            {
                var q = Vector3.Lerp(A, B, k * 0.5f);
                lane = Mathf.Max(lane, LanesUnder(map, trims, new Vector2(q.x + o.x + nrm.x * 0.1f, q.z + o.z + nrm.y * 0.1f), q.y));
            }
            // over a shoulder, a fan, a gore - not a lane: the face stands
            // where a retaining face would
            if (float.IsNegativeInfinity(lane)) return false;
            float dk = Mathf.Min(CityElevation.DeckThick, Mathf.Min(A.y, B.y) - lane - CarClearM);
            if (dk > 0.05f)
            {
                float u = Vector2.Distance(new Vector2(A.x, A.z), new Vector2(B.x, B.z));
                buckets[(int)Slot.Concrete].Face(new Vector3(A.x, A.y - dk, A.z), A, B, new Vector3(B.x, B.y - dk, B.z), new Vector3(nrm.x, 0f, nrm.y),
                    new Vector2(0f, 0f), new Vector2(0f, dk), new Vector2(u, dk), new Vector2(u, 0f));
                fasciaMetres += u;
                skirtLog?.Add((M + o, $"lane fascia top {M.y + o.y:0.00} dk {dk:0.00} lane {lane + o.y:0.00} nrm ({nrm.x:0.00},{nrm.y:0.00})"));
            }
            return true;
        }

        /// <summary>The highest LANE of a map road (its designed lanes,
        /// LineModel.CentreAt) under this world plan point, more than
        /// 0.15 m under <paramref name="y"/>, or negative infinity.</summary>
        static float LanesUnder(CityMap map, Trims trims, Vector2 p, float y)
        {
            float best = float.NegativeInfinity;
            skirtNear.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 20f, p + Vector2.one * 20f, skirtNear);
            foreach (int packed in skirtNear)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-8f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / L2);
                var foot = a + d * t;
                if (Vector2.Distance(p, foot) > e.width * 0.5f + 4f) continue;
                float s = e.s[si] + Mathf.Sqrt(L2) * t;
                float h = e.YAt(s);
                if (h >= y - 0.15f || h <= best) continue;
                var tan = d / Mathf.Sqrt(L2);
                float lat = Vector2.Dot(p - foot, new Vector2(-tan.y, tan.x));
                LineModel.CentreAt(e, s, out float c, out float hw);   // the designed lanes (cheap; no squeeze)
                if (lat >= c - hw && lat <= c + hw) best = h;
            }
            return best;
        }

        /// <summary>The highest lying road triangle of this tile more than
        /// 0.15 m under <paramref name="y"/> at a plan point (tile-local), or
        /// negative infinity.</summary>
        static float RoadTopUnder(float qx, float qz, float y) =>
            roadGrid.Between(qx, qz, float.NegativeInfinity, y - 0.15f - 1e-6f);

        /// <summary>Does a road pass under this plan point, more than a
        /// metre below <paramref name="y"/>, within its paved half width?</summary>
        static bool RoadUnder(CityMap map, Vector2 p, float y)
        {
            skirtNear.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 25f, p + Vector2.one * 25f, skirtNear);
            foreach (int packed in skirtNear)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dist = Vector2.Distance(p, a + d * t);
                if (dist > e.width * 0.5f + 1f) continue;
                float at = e.s[si] + Mathf.Sqrt(L2) * t;
                if (e.YAt(at) < y - 1f) return true;
            }
            return false;
        }

        /// <summary>A fan's ring stretch flagged a mouth (<see cref="FanCorner.mouthNext"/>)
        /// that is the ENVELOPE between two different arms' corners and has
        /// no pavement beyond it at its height: open ground, a free edge that
        /// takes its chord verge like any other (W Trade St at Graham St: the
        /// envelope from e5932's median corner to e5784's stood 0.53 m over
        /// the lattice, a slot to the ground between the junction and the
        /// median).</summary>
        static bool EnvelopeOverGround(CityMap map, Trims trims, int n, FanCorner k0, FanCorner k1, Vector3 origin)
        {
            if (EnvelopeVergeOff || k0.edge == k1.edge) return false;
            var a = new Vector2(k0.pos.x + origin.x, k0.pos.z + origin.z);
            var b = new Vector2(k1.pos.x + origin.x, k1.pos.z + origin.z);
            var chord = b - a;
            float len = chord.magnitude;
            if (len < 1f) return false;
            var outw = new Vector2(chord.y, -chord.x) / len;   // the ring runs anticlockwise: outward is its right
            if (PavedOnward(map, trims, a, b, k0.pos.y, k1.pos.y, outw, n)) return false;
            // ...and no road passes under it (a ramp's fan over the street below)
            var mid = (a + b) * 0.5f + outw * 0.5f;
            return !RoadUnder(map, mid, 0.5f * (k0.pos.y + k1.pos.y));
        }
    }
}
