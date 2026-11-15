using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  DECK PLATES, THE SLAB (2026-11-15, agent "spui"; CityDeckPlates
        //  holds the rule and the heights). Inside a plate every road at its
        //  level already stands at one height H (the drawn profiles), but the
        //  sheets are still each edge's own: ribbons, fans and gore fills laid
        //  over one another, each ribbon's lines across the others' lanes,
        //  and holes where the clips of a ramp's long run-out misfire between
        //  nodes 15 m apart. So, last thing before the road mesh is made:
        //   1. every upward road sheet at H inside the plate is CUT to the
        //      plate's rim (a convex polygon: each triangle is split along
        //      its edges; what lies outside stays as it was, paint and all);
        //   2. the plate is laid ONCE: the union of the pavements of every
        //      road at its level (the line model's full extents - no clip, no
        //      squeeze - cut to the rim and made disjoint piece by piece), on
        //      the unpainted junction slab, flat at H; a piece belongs to the
        //      tile its centre is in;
        //   3. what the tile had drawn inside the rim beyond those pavements
        //      (a fan's corner, a gore's fill) is kept, flat at H, less what
        //      the slab covers.
        //  One surface, drawn and solid alike (the road mesh is the collider).
        //  No new slot, material or baked field: the slab rides the junction
        //  slab's bucket. PSX_CITY_SPUI_CUT=0: step skipped.
        // ------------------------------------------------------------------
        /// <summary>For the probes: since the last reset.</summary>
        public static int DeckPlateCutTris, DeckPlateSlabTris, DeckPlateLeftTris, DeckPlateTileCuts;
        public static double DeckPlateMs;

        struct DpV { public float x, y, z, u, v; }
        static readonly List<DpV> dpRest = new List<DpV>(16), dpTmp = new List<DpV>(16), dpOut = new List<DpV>(16);
        static readonly List<List<DpV>> dpPool = new List<List<DpV>>();
        static readonly List<List<DpV>> dpOuts = new List<List<DpV>>(), dpLeft = new List<List<DpV>>(), dpFragsA = new List<List<DpV>>(), dpFragsB = new List<List<DpV>>();
        static readonly List<int> dpNewT = new List<int>(4096);
        static readonly List<Vector2[]> dpKept = new List<Vector2[]>(256);
        static readonly List<Vector4> dpKeptBB = new List<Vector4>(256);
        const int DpMaxKept = 6000;

        /// <summary>THE RUNAWAY GUARD (2026-11-15). The first cut of the slab made each piece disjoint
        /// from the FRAGMENTS of the pieces before it; the fragments bred without bound and one
        /// pictures job stood at 37 GB after eleven minutes (suspended by hand, ended by the
        /// coordinator). The union is bounded by construction now (whole pieces only, and count
        /// limits that leave a plate's ribbons alone: DpMaxFrags / DpMaxSlab / DpMaxOps). This is
        /// the last line behind those: polygon lists made new in one plate step (a count: the same
        /// on any machine) and, every few thousand steps, the managed heap. Past either the build
        /// THROWS, so a job that runs away ends itself instead of the machine.</summary>
        const int DpMaxLists = 250000;
        const long DpMaxHeapBytes = 6L << 30;
        static int dpMade, dpTick;
        static void DpGuard()
        {
            if (dpMade > DpMaxLists)
                throw new System.InvalidOperationException($"[DeckPlates] runaway guard: {dpMade} polygon lists made in one plate step (limit {DpMaxLists})");
            if ((++dpTick & 4095) == 0)
            {
                long heap = System.GC.GetTotalMemory(false);
                if (heap > DpMaxHeapBytes)
                    throw new System.InvalidOperationException($"[DeckPlates] runaway guard: managed heap {heap >> 20} MB (limit {DpMaxHeapBytes >> 20} MB)");
            }
        }
        static List<DpV> DpTake()
        {
            if (dpPool.Count == 0) { dpMade++; DpGuard(); return new List<DpV>(8); }
            var l = dpPool[dpPool.Count - 1]; dpPool.RemoveAt(dpPool.Count - 1); l.Clear(); return l;
        }
        static void DpGive(List<List<DpV>> ls) { foreach (var l in ls) dpPool.Add(l); ls.Clear(); }

        /// <summary>The part of a convex polygon with nx * x + nz * z + c >= 0.</summary>
        static void DpClip(List<DpV> src, List<DpV> dst, float nx, float nz, float c)
        {
            dst.Clear();
            int n = src.Count;
            if (n == 0) return;
            DpV prev = src[n - 1]; float dp = nx * prev.x + nz * prev.z + c;
            for (int i = 0; i < n; i++)
            {
                DpV cur = src[i]; float dc = nx * cur.x + nz * cur.z + c;
                if ((dp >= 0f) != (dc >= 0f))
                {
                    float t = dp / (dp - dc);
                    DpAdd(dst, new DpV { x = prev.x + (cur.x - prev.x) * t, y = prev.y + (cur.y - prev.y) * t, z = prev.z + (cur.z - prev.z) * t,
                                         u = prev.u + (cur.u - prev.u) * t, v = prev.v + (cur.v - prev.v) * t });
                }
                if (dc >= 0f) DpAdd(dst, cur);
                prev = cur; dp = dc;
            }
            // (and the last is not the first again)
            if (dst.Count > 1 && Mathf.Abs(dst[0].x - dst[dst.Count - 1].x) + Mathf.Abs(dst[0].z - dst[dst.Count - 1].z) < DpSameM) dst.RemoveAt(dst.Count - 1);
        }
        /// <summary>Two corners closer than this are one. A corner ON a cut line came out of the clip
        /// twice, and a polygon with a zero-length edge, used to cut another, handed the whole of it
        /// back as "outside that edge" while keeping it too: every such edge DOUBLED the piece. That,
        /// in fragments cut by fragments, is what ran the first union away.</summary>
        const float DpSameM = 2e-4f;
        static void DpAdd(List<DpV> dst, DpV q)
        {
            if (dst.Count > 0)
            {
                var l = dst[dst.Count - 1];
                if (Mathf.Abs(l.x - q.x) + Mathf.Abs(l.z - q.z) < DpSameM) return;
            }
            dst.Add(q);
        }
        /// <summary>Is a tile-local plan point on the plate's rim (within 2 mm)?</summary>
        static bool DpOnRim(Vector2[] hull, float x, float z, float ox, float oz)
        {
            for (int i = 0, j = hull.Length - 1; i < hull.Length; j = i++)
            {
                float ax = hull[j].x - ox, az = hull[j].y - oz, ex = hull[i].x - ox - ax, ez = hull[i].y - oz - az;
                float l2 = ex * ex + ez * ez;
                float t = l2 > 1e-9f ? Mathf.Clamp01(((x - ax) * ex + (z - az) * ez) / l2) : 0f;
                float dx = x - (ax + ex * t), dz = z - (az + ez * t);
                if (dx * dx + dz * dz < 4e-6f) return true;
            }
            return false;
        }
        /// <summary>Twice the signed plan area (counter-clockwise in (x, z) positive).</summary>
        static float DpArea2(List<DpV> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) a += p[j].x * p[i].z - p[i].x * p[j].z;
            return a;
        }
        static float DpArea2(Vector2[] p)
        {
            float a = 0f;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) a += p[j].x * p[i].y - p[i].x * p[j].y;
            return a;
        }

        /// <summary>Lays a convex polygon as a fan in a bucket, facing as <paramref name="upSign"/> says
        /// (the sign of Cross(b - a, c - a).y of an upward triangle in this mesh).</summary>
        static int DpEmit(Bucket bk, List<DpV> p, float upSign, bool flat, float y, Vector3 o)
        {
            if (p.Count < 3) return 0;
            // Cross(b - a, c - a).y = -(2 * plan area) of (a, b, c)
            bool flip = -DpArea2(p) * upSign < 0f;
            int i0 = bk.v.Count;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[flip ? p.Count - 1 - i : i];
                bk.v.Add(new Vector3(q.x, flat ? y : q.y, q.z));
                bk.uv.Add(flat ? new Vector2((q.x + o.x) / 12f, (q.z + o.z) / 12f) : new Vector2(q.u, q.v));
            }
            for (int i = 1; i + 1 < p.Count; i++) { bk.t.Add(i0); bk.t.Add(i0 + i); bk.t.Add(i0 + i + 1); }
            return p.Count - 2;
        }

        /// <summary>frags = frags less the convex polygon c (counter-clockwise, plan), in the frame moved by (ox, oz).</summary>
        static void DpSubtract(List<List<DpV>> frags, List<List<DpV>> scratch, Vector2[] c, float ox, float oz)
        {
            DpGuard();
            scratch.Clear();
            foreach (var f in frags)
            {
                // Does it reach into c at all? Only then is it cut along c's edges: an edge's
                // LINE runs on past c, and cutting by it split pieces c never touched (the
                // Brookshire deck's tile: 8,400 triangles of kept pavement where a few hundred do).
                dpRest.Clear(); dpRest.AddRange(f);
                for (int i = 0, j = c.Length - 1; i < c.Length && dpRest.Count >= 3; j = i++)
                {
                    float ax = c[j].x - ox, az = c[j].y - oz, bx = c[i].x - ox, bz = c[i].y - oz;
                    float nx = -(bz - az), nz = bx - ax, cc = -(nx * ax + nz * az);
                    if (nx * nx + nz * nz < 1e-8f) continue;
                    DpClip(dpRest, dpTmp, nx, nz, cc);
                    dpRest.Clear(); dpRest.AddRange(dpTmp);
                }
                if (dpRest.Count < 3 || Mathf.Abs(DpArea2(dpRest)) < 2e-4f) { scratch.Add(f); continue; }
                dpRest.Clear(); dpRest.AddRange(f);
                for (int i = 0, j = c.Length - 1; i < c.Length && dpRest.Count >= 3; j = i++)
                {
                    float ax = c[j].x - ox, az = c[j].y - oz, bx = c[i].x - ox, bz = c[i].y - oz;
                    float nx = -(bz - az), nz = bx - ax, cc = -(nx * ax + nz * az);
                    if (nx * nx + nz * nz < 1e-8f) continue;   // a zero-length edge: no line to cut by (see DpSameM)
                    DpClip(dpRest, dpOut, -nx, -nz, -cc);
                    if (dpOut.Count >= 3 && Mathf.Abs(DpArea2(dpOut)) > 2e-4f) { var k = DpTake(); k.AddRange(dpOut); scratch.Add(k); }
                    DpClip(dpRest, dpTmp, nx, nz, cc);
                    dpRest.Clear(); dpRest.AddRange(dpTmp);
                }
                dpPool.Add(f);
            }
            frags.Clear();
            frags.AddRange(scratch);
            scratch.Clear();
        }

        /// <summary>The plate's slab: the pavements of the roads at its level, cut to its rim (convex
        /// pieces, counter-clockwise, world plan: <c>cover</c>, the widest first), and the same made
        /// disjoint - each piece less the pieces before it (<c>slab</c>). A piece is only ever cut by
        /// whole pieces, never by the fragments of earlier ones (the first cut did that: the fragments
        /// bred, and one tile took minutes). Once per plate and trims.</summary>
        static void DeckPlateCover(CityMap map, Trims trims, CityDeckPlates.Plate pl)
        {
            if (pl.cover != null && ReferenceEquals(pl.coverFor, trims)) return;
            dpMade = 0;
            var pieces = new List<Vector2[]>(128);
            var hull = pl.hull;
            var poly = new List<DpV>(8);
            void AddPoly()
            {
                if (DpArea2(poly) < 0f) poly.Reverse();
                if (DpArea2(poly) < 1e-3f) return;
                dpRest.Clear(); dpRest.AddRange(poly);
                for (int i = 0, j = hull.Length - 1; i < hull.Length && dpRest.Count >= 3; j = i++)
                {
                    float nx = -(hull[i].y - hull[j].y), nz = hull[i].x - hull[j].x, cc = -(nx * hull[j].x + nz * hull[j].y);
                    DpClip(dpRest, dpTmp, nx, nz, cc);
                    dpRest.Clear(); dpRest.AddRange(dpTmp);
                }
                if (dpRest.Count < 3 || DpArea2(dpRest) < 4e-3f) return;
                var p = new Vector2[dpRest.Count];
                for (int i = 0; i < p.Length; i++) p[i] = new Vector2(dpRest[i].x, dpRest[i].z);
                pieces.Add(p);
            }
            void Tri(Vector2 a, Vector2 b, Vector2 c)
            {
                poly.Clear();
                poly.Add(new DpV { x = a.x, z = a.y }); poly.Add(new DpV { x = b.x, z = b.y }); poly.Add(new DpV { x = c.x, z = c.y });
                AddPoly();
            }
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                // convex (every turn the same way): one piece; else two triangles
                float Turn(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - q.y) - (q.y - p.y) * (r.x - q.x);
                float t0 = Turn(a, b, c), t1 = Turn(b, c, d), t2 = Turn(c, d, a), t3 = Turn(d, a, b);
                bool convex = (t0 > 1e-5f && t1 > 1e-5f && t2 > 1e-5f && t3 > 1e-5f) || (t0 < -1e-5f && t1 < -1e-5f && t2 < -1e-5f && t3 < -1e-5f);
                if (!convex) { Tri(a, b, c); Tri(a, c, d); return; }
                poly.Clear();
                poly.Add(new DpV { x = a.x, z = a.y }); poly.Add(new DpV { x = b.x, z = b.y }); poly.Add(new DpV { x = c.x, z = c.y }); poly.Add(new DpV { x = d.x, z = d.y });
                AddPoly();
            }
            var arcs = new List<float>(64);
            foreach (int ei in pl.levelEdges)
            {
                var e = map.edges[ei];
                // the polyline's vertices (none closer than 1.5 m to the last), and none further apart than 4 m
                arcs.Clear();
                arcs.Add(0f);
                for (int i = 1; i < e.s.Length; i++)
                {
                    bool lastV = i == e.s.Length - 1;
                    float prev = arcs[arcs.Count - 1];
                    if (!lastV && (e.s[i] - prev < 1.5f || e.length - e.s[i] < 1.5f)) continue;
                    float seg = e.s[i] - prev;
                    int k = Mathf.CeilToInt(seg / 4f);
                    for (int q = 1; q < k; q++) arcs.Add(prev + seg * q / k);
                    arcs.Add(e.s[i]);
                }
                Vector2 pL = default, pR = default; bool have = false; float ps = 0f;
                for (int a = 0; a < arcs.Count; a++)
                {
                    float s = arcs[a];
                    var p = e.PointAt(s);
                    Vector2 tan;
                    int vi = System.Array.BinarySearch(e.s, s);
                    if (vi > 0 && vi < e.s.Length - 1)
                    {
                        tan = (e.pts[vi] - e.pts[vi - 1]).normalized + (e.pts[vi + 1] - e.pts[vi]).normalized;
                        tan = tan.sqrMagnitude < 1e-6f ? e.TangentAt(s) : tan.normalized;
                    }
                    else tan = e.TangentAt(s);
                    var right = new Vector2(-tan.y, tan.x);
                    LineModel.Extents(e, s, out float eM, out float eP);
                    Vector2 L = p - right * eM, R = p + right * eP;
                    if (have)
                    {
                        float mid = 0.5f * (ps + s);
                        bool near = !(Mathf.Max(Mathf.Max(pL.x, pR.x), Mathf.Max(L.x, R.x)) < pl.bbMin.x || Mathf.Min(Mathf.Min(pL.x, pR.x), Mathf.Min(L.x, R.x)) > pl.bbMax.x ||
                                      Mathf.Max(Mathf.Max(pL.y, pR.y), Mathf.Max(L.y, R.y)) < pl.bbMin.y || Mathf.Min(Mathf.Min(pL.y, pR.y), Mathf.Min(L.y, R.y)) > pl.bbMax.y);
                        // only where the edge stands at the plate's height (a ramp that has left the level is no part of it)
                        if (near && Mathf.Abs(e.YAt(mid) - pl.H) < 0.12f) Quad(pL, pR, R, L);
                    }
                    pL = L; pR = R; ps = s; have = true;
                }
            }
            if (pieces.Count > DpMaxPieces)
                throw new System.InvalidOperationException($"[DeckPlates] runaway guard: plate {pl.id} has {pieces.Count} pavement pieces (limit {DpMaxPieces})");
            // the widest first: fewer cuts
            var order = new List<int>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++) order.Add(i);
            var area = new float[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) area[i] = DpArea2(pieces[i]);
            order.Sort((x, y) => area[x] != area[y] ? area[y].CompareTo(area[x]) : x.CompareTo(y));
            var cover = new List<Vector2[]>(pieces.Count);
            var bbs = new List<Vector4>(pieces.Count);
            var slab = new List<Vector2[]>(pieces.Count * 3);
            var frags = new List<List<DpV>>(); var scratch = new List<List<DpV>>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool failed = false; int ops = 0;
            foreach (int pi in order)
            {
                var pc = pieces[pi];
                var f0 = DpTake();
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (var q in pc) { f0.Add(new DpV { x = q.x, z = q.y }); x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
                frags.Clear(); frags.Add(f0);
                for (int k = 0; k < cover.Count && frags.Count > 0; k++)
                {
                    var bb = bbs[k];
                    if (bb.x > x1 || bb.z < x0 || bb.y > z1 || bb.w < z0) continue;
                    DpSubtract(frags, scratch, cover[k], 0f, 0f);
                    // bounded, whatever the map holds: a plate that cannot be made in this is not laid at all
                    if (frags.Count > DpMaxFrags || ++ops > DpMaxOps) { failed = true; break; }
                }
                if (failed || slab.Count > DpMaxSlab) { failed = true; DpGive(frags); break; }
                foreach (var f in frags)
                {
                    if (f.Count < 3 || DpArea2(f) < 4e-3f) continue;
                    var p = new Vector2[f.Count];
                    for (int i = 0; i < p.Length; i++) p[i] = new Vector2(f[i].x, f[i].z);
                    slab.Add(p);
                }
                DpGive(frags);
                cover.Add(pc); bbs.Add(new Vector4(x0, z0, x1, z1));
            }
            if (failed)
            {
                cover.Clear(); bbs.Clear(); slab.Clear();
                Debug.LogWarning($"[DeckPlates] plate {pl.id}: the slab was not made within its bounds ({pieces.Count} pieces, {clock.ElapsedMilliseconds} ms): its ribbons stay as they are, at the plate's height");
            }
            pl.cutFailed = failed;
            pl.cover = cover; pl.coverBB = bbs; pl.slab = slab; pl.coverFor = trims;
        }
        const int DpMaxFrags = 3000, DpMaxSlab = 40000, DpMaxPieces = 5000;
        /// <summary>Counts, not clocks: the same tile on any machine.</summary>
        const int DpMaxOps = 400000;

        /// <summary>The tile's last road step: every plate near it (see the block comment).</summary>
        static void DeckPlateCut(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max)
        {
            if (!CityDeckPlates.On || !CityDeckPlates.CutOn) return;
            foreach (var pl in CityDeckPlates.Plates)
            {
                if (pl.skipped != null || pl.hull == null || pl.widest < 0) continue;
                if (pl.bbMax.x < min.x - 96f || pl.bbMin.x > max.x + 96f || pl.bbMax.y < min.y - 96f || pl.bbMin.y > max.y + 96f) continue;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                DeckPlateCutOne(map, trims, tm, min, max, pl);
                DeckPlateMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        static void DeckPlateCutOne(CityMap map, Trims trims, TileMeshes tm, Vector2 min, Vector2 max, CityDeckPlates.Plate pl)
        {
            DeckPlateCover(map, trims, pl);
            if (pl.cutFailed) return;
            dpMade = 0;
            var o = tm.origin;
            var hull = pl.hull;
            float H = pl.H - o.y;
            float bx0 = pl.bbMin.x - o.x, bz0 = pl.bbMin.y - o.z, bx1 = pl.bbMax.x - o.x, bz1 = pl.bbMax.y - o.z;
            int first = (int)Slot.RoadFirst, last = first + RoadClassCount * SurfaceCount;
            const float Win = 0.07f, WinIn = 0.35f;

            // which way an upward sheet winds here: the majority of the level sheets in the plate's box
            double sign = 0;
            for (int slot = first; slot < last; slot++)
            {
                var bk = buckets[slot]; var t = bk.t;
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    Vector3 a = bk.v[t[i]], b = bk.v[t[i + 1]], c = bk.v[t[i + 2]];
                    if (Mathf.Abs(a.y - H) > Win || Mathf.Abs(b.y - H) > Win || Mathf.Abs(c.y - H) > Win) continue;
                    if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < bx0 || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > bx1 || Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < bz0 || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > bz1) continue;
                    sign += (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);
                }
            }
            float upSign = sign >= 0 ? 1f : -1f;

            dpLeft.Clear();
            int cut = 0;
            bool keepOrder = tm.tap != null;   // a tap indexes the buckets' triangles: none may move
            for (int slot = first; slot < last; slot++)
            {
                var bk = buckets[slot]; var t = bk.t;
                int tc = t.Count;
                if (tc == 0) continue;
                dpNewT.Clear();
                bool any = false;
                for (int i = 0; i + 2 < tc; i += 3)
                {
                    int i0 = t[i], i1 = t[i + 1], i2 = t[i + 2];
                    Vector3 a = bk.v[i0], b = bk.v[i1], c = bk.v[i2];
                    bool keep = true;
                    if (!(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < bx0 || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > bx1 || Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < bz0 || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > bz1)
                        && Mathf.Min(a.y, Mathf.Min(b.y, c.y)) <= H + WinIn && Mathf.Max(a.y, Mathf.Max(b.y, c.y)) >= H - WinIn)
                    {
                        var nrm = Vector3.Cross(b - a, c - a);
                        if (nrm.y * upSign > 1e-7f && Mathf.Abs(nrm.y) > 0.9f * nrm.magnitude)
                        {
                            Vector2 ua = bk.uv[i0], ub = bk.uv[i1], uc = bk.uv[i2];
                            dpRest.Clear();
                            dpRest.Add(new DpV { x = a.x, y = a.y, z = a.z, u = ua.x, v = ua.y });
                            dpRest.Add(new DpV { x = b.x, y = b.y, z = b.z, u = ub.x, v = ub.y });
                            dpRest.Add(new DpV { x = c.x, y = c.y, z = c.z, u = uc.x, v = uc.y });
                            DpGive(dpOuts);
                            for (int h = 0, j = hull.Length - 1; h < hull.Length && dpRest.Count >= 3; j = h++)
                            {
                                float ax = hull[j].x - o.x, az = hull[j].y - o.z, bxx = hull[h].x - o.x, bzz = hull[h].y - o.z;
                                float nx = -(bzz - az), nz = bxx - ax, cc = -(nx * ax + nz * az);
                                DpClip(dpRest, dpOut, -nx, -nz, -cc);
                                if (dpOut.Count >= 3 && Mathf.Abs(DpArea2(dpOut)) > 2e-5f) { var k = DpTake(); k.AddRange(dpOut); dpOuts.Add(k); }
                                DpClip(dpRest, dpTmp, nx, nz, cc);
                                dpRest.Clear(); dpRest.AddRange(dpTmp);
                            }
                            bool inside = dpRest.Count >= 3 && Mathf.Abs(DpArea2(dpRest)) > 2e-5f;
                            // a sheet of this level: inside the rim within WinIn of H (a fan whose far
                            // corners stand on an approach's ease lies a few centimetres off it there)
                            if (inside)
                                foreach (var q in dpRest) if (Mathf.Abs(q.y - H) > WinIn) { inside = false; break; }
                            if (inside)
                            {
                                keep = false; any = true; cut++;
                                var l = DpTake(); l.AddRange(dpRest); dpLeft.Add(l);
                                // what lies outside the rim stays as it was - but ON the rim it stands at H,
                                // so every sheet the rim cuts meets the slab with no step
                                int tBefore = bk.t.Count;
                                foreach (var op in dpOuts)
                                {
                                    for (int q = 0; q < op.Count; q++)
                                    {
                                        var ov = op[q];
                                        if (ov.y != H && DpOnRim(hull, ov.x, ov.z, o.x, o.z)) { ov.y = H; op[q] = ov; }
                                    }
                                    DpEmit(bk, op, upSign, false, 0f, o);
                                }
                                // (DpEmit appended to bk.t: move those to the new list when the list is rebuilt)
                                if (!keepOrder)
                                {
                                    for (int q = tBefore; q < bk.t.Count; q++) dpNewT.Add(bk.t[q]);
                                    bk.t.RemoveRange(tBefore, bk.t.Count - tBefore);
                                }
                            }
                            DpGive(dpOuts);
                        }
                    }
                    if (keepOrder) { if (!keep) { t[i + 1] = i0; t[i + 2] = i0; } }
                    else if (keep) { dpNewT.Add(i0); dpNewT.Add(i1); dpNewT.Add(i2); }
                }
                if (any && !keepOrder) { t.Clear(); t.AddRange(dpNewT); }
            }
            DeckPlateCutTris += cut;

            // the slab, and what the tile had drawn beyond it
            var cover = pl.cover;
            var slab = buckets[(int)SlotOf(JunctionProfile, SurfaceOf(map.edges[pl.widest], true))];
            var poly = dpTmp;
            int laid = 0;
            foreach (var pc in pl.slab)
            {
                Vector2 cen = Vector2.zero;
                foreach (var q in pc) cen += q;
                cen /= pc.Length;
                if (cen.x < min.x || cen.x >= max.x || cen.y < min.y || cen.y >= max.y) continue;
                poly.Clear();
                foreach (var q in pc) poly.Add(new DpV { x = q.x - o.x, y = H, z = q.y - o.z });
                laid += DpEmit(slab, poly, upSign, true, H, o);
            }
            DeckPlateSlabTris += laid;
            int left = 0;
            if (dpLeft.Count > 0)
            {
                int leftOps = 0;
                foreach (var l in dpLeft)
                {
                    float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                    foreach (var q in l) { x0 = Mathf.Min(x0, q.x + o.x); z0 = Mathf.Min(z0, q.z + o.z); x1 = Mathf.Max(x1, q.x + o.x); z1 = Mathf.Max(z1, q.z + o.z); }
                    dpFragsA.Clear();
                    var f0 = DpTake(); f0.AddRange(l); dpFragsA.Add(f0);
                    // (bounded: past the budget a sheet is laid whole - drawn twice there, never a hang)
                    for (int k = 0; k < cover.Count && dpFragsA.Count > 0 && dpFragsA.Count < 400 && leftOps < DpMaxOps; k++)
                    {
                        var bb = pl.coverBB[k];
                        if (bb.x > x1 || bb.z < x0 || bb.y > z1 || bb.w < z0) continue;
                        DpSubtract(dpFragsA, dpFragsB, cover[k], o.x, o.z); leftOps++;
                    }
                    // ...and less what this tile has already kept (a fan's corner under a gore's fill):
                    // whole kept pieces, each already disjoint from the others, so nothing breeds
                    for (int k = 0; k < dpKept.Count && dpFragsA.Count > 0 && dpFragsA.Count < 400 && leftOps < DpMaxOps; k++)
                    {
                        var bb = dpKeptBB[k];
                        if (bb.x > x1 || bb.z < x0 || bb.y > z1 || bb.w < z0) continue;
                        DpSubtract(dpFragsA, dpFragsB, dpKept[k], o.x, o.z); leftOps++;
                    }
                    foreach (var f in dpFragsA)
                    {
                        if (f.Count < 3 || Mathf.Abs(DpArea2(f)) < 4e-4f) continue;
                        left += DpEmit(slab, f, upSign, true, H, o);
                        if (dpKept.Count >= DpMaxKept) continue;
                        // world plan, counter-clockwise: what DpSubtract cuts by
                        var kp = new Vector2[f.Count];
                        bool cw = DpArea2(f) < 0f;
                        float a0 = float.MaxValue, b0 = float.MaxValue, a1 = float.MinValue, b1 = float.MinValue;
                        for (int i = 0; i < kp.Length; i++)
                        {
                            var q = f[cw ? kp.Length - 1 - i : i];
                            kp[i] = new Vector2(q.x + o.x, q.z + o.z);
                            a0 = Mathf.Min(a0, kp[i].x); b0 = Mathf.Min(b0, kp[i].y); a1 = Mathf.Max(a1, kp[i].x); b1 = Mathf.Max(b1, kp[i].y);
                        }
                        dpKept.Add(kp); dpKeptBB.Add(new Vector4(a0, b0, a1, b1));
                    }
                    DpGive(dpFragsA);
                }
                DpGive(dpLeft);
                dpKept.Clear(); dpKeptBB.Clear();
            }
            DeckPlateLeftTris += left;
            if (cut > 0 || laid > 0) DeckPlateTileCuts++;
        }
    }
}
