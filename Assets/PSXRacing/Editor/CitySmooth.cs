using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SMOOTHNESS GATE on the meshes CityMeshes actually builds (plan
    /// amendment A4 WP-G; gate spec scratchpad linewobble/gate_spec.md). The
    /// owner, 2026-09-28: "I circled some concerning squiggly road lines. This
    /// should never happen. Nor should any sharp angles of road or road lines."
    ///
    /// It reads each tile's roads mesh - vertices, UVs, submesh slots - through
    /// the tap (<see cref="CityMeshes.RecordTap"/>: WHERE each ribbon quad, fan
    /// and gore quad went; the geometry itself always comes from the mesh), and
    /// the painted runs out of the road PNGs themselves. Every painted line is
    /// found per TRIANGLE as the iso-line U = u (a diagonal bulge is sampled at
    /// its peak, never between samples: plan A7). Lines are chained through
    /// mitred nodes and tile seams, then checked against
    ///   A  a PaintPlan built from DATA only (the graph, RoadProfiles' layout
    ///      rules, the Trims taper TABLE) - never the builder's sections, U or
    ///      quads, so a bug in the line model cannot hide itself;
    ///   B  their own shape: B1 jitter, B2 facet sagitta, B3 radius, B4 jumps
    ///      and tile seams;
    ///   C  continuity: gaps, ends, dash and gap lengths;
    ///   D  paint inside other pavement at the same level.
    /// Every threshold is in <see cref="SmoothRules"/>; every lateral limit is V.
    ///
    /// tools/city/linecheck.mjs is the same gate offline (on the plan-view
    /// replica). The two MUST agree on every check both measure; a
    /// disagreement is a gate bug (Docs/CHARLOTTE.md, "Smoothness gate").
    ///
    /// Modes (gate spec 5.3):
    ///   FAST   inside every CityAudit.Run (BeginFast / Collect / EndFast): the
    ///          drive and roadside audits' tiles, the reference spots' rings and
    ///          one band of 1/12 of the road tiles (PSX_SMOOTH_BAND).
    ///   FULL   -executeMethod PSXRacing.EditorTools.CitySmooth.RunFull: every
    ///          road tile, raster order, three tile rows of fragments live.
    ///   SHOTS  -executeMethod PSXRacing.EditorTools.CitySmooth.RunShots: plan,
    ///          chase (the 240-line frame) and high close-ups of the worst
    ///          offenders (city_smooth.json) or of PSX_SMOOTH_SPOTS.
    /// Outputs at the project root: city_smooth.txt / .csv / .json; the
    /// baseline is read from PSX_SMOOTH_BASELINE, tools/city/baseline/
    /// smooth_baseline.json or smooth_baseline.json at the root, and written
    /// (PSX_SMOOTH_WRITE_BASELINE=1) to smooth_baseline.json at the root.
    /// </summary>
    public static class CitySmooth
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double Deg = 180.0 / Math.PI;
        static double V => SmoothRules.V;
        // the rollout switches, read through properties (no dead-code warnings while they are constant)
        static bool ReportOnly => SmoothRules.ReportOnly;
        static bool PinActive => SmoothRules.PinActive;

        // ================================================================
        //  Fragments: what each tile drew, read back out of its mesh
        // ================================================================

        sealed class Quad
        {
            public int edge, slot, tx, tz; public float sA, sB; public ushort fA, fB;
            public Vector3d AL, BL, BR, AR;              // world
            public Vector2 uAL, uBL, uBR, uAR;           // (U, V)
        }
        sealed class FanRec { public int node, tx, tz; public Vector3d centre; public Vector3d[] corners; public ulong mouths; }
        sealed class Frag { public int tx, tz; public readonly List<Quad> quads = new List<Quad>(); public readonly List<Quad> gores = new List<Quad>(); public readonly List<FanRec> fans = new List<FanRec>(); }

        struct Vector3d
        {
            public double x, y, z;
            public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        }

        static readonly Dictionary<long, Frag> frags = new Dictionary<long, Frag>();
        static long TileKey(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;
        static bool collecting;
        static int tapNotes;

        /// <summary>Read one tile's fragments out of its built meshes. Called
        /// right after a CityMeshes.Build while the gate is collecting (FAST:
        /// by CityAudit; FULL / SHOTS: here). A no-op otherwise.</summary>
        public static void Collect(int tx, int tz, CityMeshes.TileMeshes tm)
        {
            if (!collecting || tm == null) return;
            frags[TileKey(tx, tz)] = FragOf(tx, tz, tm);
        }

        static Frag FragOf(int tx, int tz, CityMeshes.TileMeshes tm)
        {
            var f = new Frag { tx = tx, tz = tz };
            var tap = tm.tap;
            if (tap == null || tm.roads == null || tap.slotBase == null) return f;
            var verts = tm.roads.vertices;
            var uvs = tm.roads.uv;
            var o = tm.origin;
            int BaseOf(int slot)
            {
                for (int i = 0; i < tm.roadSlots.Length; i++) if ((int)tm.roadSlots[i] == slot) return tap.slotBase[i];
                return -1;
            }
            Vector3d W(int i) => new Vector3d((double)o.x + verts[i].x, (double)o.y + verts[i].y, (double)o.z + verts[i].z);
            void AddQuads(List<CityMeshes.RoadTap.Span> list, List<Quad> into)
            {
                foreach (var sp in list)
                {
                    int b = BaseOf(sp.slot);
                    if (b < 0 || b + sp.bucketV + 3 >= verts.Length) { tapNotes++; continue; }
                    int i = b + sp.bucketV;
                    into.Add(new Quad
                    {
                        edge = sp.edge, slot = sp.slot, tx = tx, tz = tz, sA = sp.sA, sB = sp.sB, fA = sp.flagsA, fB = sp.flagsB,
                        AL = W(i), BL = W(i + 1), BR = W(i + 2), AR = W(i + 3),
                        uAL = uvs[i], uBL = uvs[i + 1], uBR = uvs[i + 2], uAR = uvs[i + 3],
                    });
                }
            }
            AddQuads(tap.spans, f.quads);
            AddQuads(tap.gores, f.gores);
            foreach (var fa in tap.fans)
            {
                int b = BaseOf(fa.slot);
                if (b < 0 || b + fa.bucketV + fa.count - 1 >= verts.Length) { tapNotes++; continue; }
                int i = b + fa.bucketV;
                var rec = new FanRec { node = fa.node, tx = tx, tz = tz, centre = W(i), corners = new Vector3d[fa.count - 1], mouths = fa.mouths };
                for (int k = 1; k < fa.count; k++) rec.corners[k - 1] = W(i + k);
                f.fans.Add(rec);
            }
            return f;
        }

        // ================================================================
        //  The painted runs, read from the PNGs (gate spec 3.1)
        // ================================================================

        sealed class PaintRun { public double u, du, vOn0, vOn1; public char col; public bool dashed; }
        sealed class PlanLine { public string id; public char anchor; public char col; public bool dashed; public double off, inset; }
        sealed class Layout
        {
            public string key; public double width;
            public List<PaintRun> runs = new List<PaintRun>();
            public List<PlanLine> plan = new List<PlanLine>();
            public int[] runPlan = new int[0], planRun = new int[0]; public double[] runQ = new double[0];
            public bool painted;
        }
        static readonly Dictionary<int, Layout> layouts = new Dictionary<int, Layout>();
        static readonly Color32 Yellow = new Color32(196, 160, 40, 255), White = new Color32(200, 200, 196, 255);
        static readonly List<string> textureNotes = new List<string>();

        static string SurfaceKey(int surf) => surf == 0 ? "asphalt_new" : surf == 1 ? "asphalt_old" : surf == 2 ? "concrete_new" : "concrete_old";

        static Layout LayoutOf(int slot)
        {
            if (layouts.TryGetValue(slot, out var lay)) return lay;
            lay = new Layout();
            layouts[slot] = lay;
            int rel = slot - (int)CityMeshes.Slot.RoadFirst;
            int prof = rel / CityMeshes.SurfaceCount, surf = rel % CityMeshes.SurfaceCount;
            if (rel < 0 || prof >= RoadProfiles.ProfileCount) return lay;   // the junction slab: no paint
            var pr = RoadProfiles.All[prof];
            lay.key = pr.key; lay.width = pr.Width; lay.painted = true;
            lay.plan = PlanOf(pr);
            string file = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets", "PSXRacing", "Art", "City",
                                       "city_road_" + pr.key + "_" + SurfaceKey(surf) + ".png");
            if (!File.Exists(file)) { textureNotes.Add("missing " + file); return lay; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                tex.LoadImage(File.ReadAllBytes(file));
                lay.runs = ScanRuns(tex.GetPixels32(), tex.width, tex.height);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            MatchLayout(lay);
            return lay;
        }

        /// <summary>Runs of the exact paint colours: solid when present in
        /// (Unity) row H/2, dashed when present only in part of the rows; the
        /// on-band is the rows its centre texel is painted in. GetPixels32 is
        /// bottom-up, as V is.</summary>
        static List<PaintRun> ScanRuns(Color32[] px, int W, int H)
        {
            bool At(int x, int row, Color32 c) { var p = px[row * W + x]; return p.r == c.r && p.g == c.g && p.b == c.b; }
            var runs = new List<(int x0, int x1, char col, bool dashed, int r0, int r1)>();
            var seen = new HashSet<long>();
            foreach (var (col, c) in new[] { ('Y', Yellow), ('W', White) })
            {
                int solidRow = H / 2;
                for (int pass = 0; pass < 2; pass++)
                    for (int row = pass == 0 ? solidRow : 0; row < (pass == 0 ? solidRow + 1 : H); row++)
                    {
                        int x0 = -1;
                        for (int x = 0; x <= W; x++)
                        {
                            bool on = x < W && At(x, row, c);
                            if (on && x0 < 0) x0 = x;
                            if (!on && x0 >= 0)
                            {
                                long k = ((long)x0 << 20) | (long)(x - 1) | ((long)col << 40);
                                if (seen.Add(k))
                                {
                                    int xc = (x0 + x - 1) / 2, r0 = row, r1 = row;
                                    while (r1 + 1 < H && At(xc, r1 + 1, c)) r1++;
                                    runs.Add((x0, x - 1, col, pass == 1, r0, r1));
                                }
                                x0 = -1;
                            }
                        }
                    }
            }
            runs.Sort((a, b) => a.x0.CompareTo(b.x0));
            var outp = new List<PaintRun>();
            foreach (var r in runs)
                outp.Add(new PaintRun
                {
                    u = (r.x0 + r.x1 + 1) / (2.0 * W), du = (r.x1 - r.x0 + 1) / (double)W, col = r.col, dashed = r.dashed,
                    vOn0 = r.dashed ? r.r0 / (double)H : 0, vOn1 = r.dashed ? (r.r1 + 1) / (double)H : 1,
                });
            return outp;
        }

        /// <summary>THE PLAN's lines for one profile row (gate spec 3.4),
        /// written from RoadProfiles' rules - lanes of LaneM, the shoulders,
        /// edge lines at shoulder + EdgeLineInsetM, a double yellow or a TWLTL
        /// pair on an undivided road. Offsets + = left of travel.</summary>
        static List<PlanLine> PlanOf(RoadProfiles.Profile pr)
        {
            double W = pr.Width, hw = W / 2, lane = RoadProfiles.LaneM, ph = SmoothRules.EdgeLineInsetM;
            var o = new List<PlanLine>
            {
                new PlanLine { id = "EL", anchor = 'L', col = pr.oneway ? 'Y' : 'W', inset = pr.shl + ph, off = hw - (pr.shl + ph) },
                new PlanLine { id = "ER", anchor = 'R', col = 'W', inset = pr.shr + ph, off = -(hw - (pr.shr + ph)) },
            };
            void C(double m, char col, bool dashed)
            {
                double off = hw - m;
                o.Add(new PlanLine { id = "C" + col + (dashed ? "d" : "s") + (off >= 0 ? "+" : "-") + Math.Abs(off).ToString("0.00", Inv), anchor = 'C', col = col, dashed = dashed, off = off });
            }
            if (pr.oneway) for (int i = 1; i < pr.lanes; i++) C(pr.shl + lane * i, 'W', true);
            else
            {
                double perSide = (pr.lanes - (pr.turnLane ? 1 : 0)) / 2.0, med = pr.shl + perSide * lane;
                for (int i = 1; i < perSide; i++) C(pr.shl + lane * i, 'W', true);
                if (pr.turnLane)
                {
                    C(med - 2 * ph, 'Y', false); C(med + 2 * ph, 'Y', true);
                    C(med + lane - 2 * ph, 'Y', true); C(med + lane + 2 * ph, 'Y', false);
                    for (int i = 1; i < perSide; i++) C(med + lane + lane * i, 'W', true);
                }
                else
                {
                    C(med - 2 * ph, 'Y', false); C(med + 2 * ph, 'Y', false);
                    for (int i = 1; i < perSide; i++) C(med + lane * i, 'W', true);
                }
            }
            return o;
        }

        /// <summary>Texture runs to plan lines at nominal scale: colour,
        /// pattern, nearest within StrayM. runQ is the texture's own
        /// quantisation (run centre minus plan), which the position checks
        /// subtract: V is half a texel precisely so geometry is told from it.</summary>
        static void MatchLayout(Layout lay)
        {
            var cand = new List<(int k, int j, double d)>();
            for (int k = 0; k < lay.runs.Count; k++)
                for (int j = 0; j < lay.plan.Count; j++)
                {
                    var r = lay.runs[k]; var p = lay.plan[j];
                    if (r.col != p.col || r.dashed != p.dashed) continue;
                    double d = (0.5 - r.u) * lay.width - p.off;
                    if (Math.Abs(d) <= SmoothRules.StrayM) cand.Add((k, j, d));
                }
            cand.Sort((a, b) => Math.Abs(a.d).CompareTo(Math.Abs(b.d)));
            lay.runPlan = new int[lay.runs.Count]; lay.runQ = new double[lay.runs.Count]; lay.planRun = new int[lay.plan.Count];
            for (int i = 0; i < lay.runPlan.Length; i++) lay.runPlan[i] = -1;
            for (int i = 0; i < lay.planRun.Length; i++) lay.planRun[i] = -1;
            foreach (var c in cand)
                if (lay.runPlan[c.k] < 0 && lay.planRun[c.j] < 0) { lay.runPlan[c.k] = c.j; lay.runQ[c.k] = c.d; lay.planRun[c.j] = c.k; }
            for (int k = 0; k < lay.runs.Count; k++) if (lay.runPlan[k] < 0) textureNotes.Add($"{lay.key}: texture run at u {lay.runs[k].u:0.0000} has no plan line");
            for (int j = 0; j < lay.plan.Count; j++) if (lay.planRun[j] < 0) textureNotes.Add($"{lay.key}: plan line {lay.plan[j].id} has no texture run");
        }

        // ================================================================
        //  The session
        // ================================================================

        static CityMap map;
        static CityMeshes.Trims trims;
        static double[] wayOff;
        static HashSet<long> mergePairs;
        static HashSet<int> onRoute;
        static List<Vector2> refSpots;
        static readonly List<Run> runs = new List<Run>();
        static readonly HashSet<long> analysed = new HashSet<long>();
        static readonly HashSet<uint> pinnedWays = new HashSet<uint>(SmoothRules.PinnedWays);
        static double ribbonKm, lineKm;
        static int strands, band = -1;
        static string mode = "FAST";
        static float t0;
        // SHOTS: the samples of one line near one spot, for the overlay
        static int captureEdge = -1; static string captureLine;
        static readonly List<(Vector3 p, double err)> captured = new List<(Vector3, double)>();

        sealed class Run
        {
            public string check, lineId, kind, what, reportOnly, cause, data, key;
            public int e, span = -1, node = -1; public double s, x, z, y, val, len, ratio, score, exposure = 1, rLimit;
            public int e0, e1; public double s0, s1; public char tag; public char side; public bool pinned;
        }

        static void Reset()
        {
            frags.Clear(); runs.Clear(); analysed.Clear(); layouts.Clear(); textureNotes.Clear();
            ribbonKm = lineKm = 0; strands = 0; tapNotes = 0; band = -1;
            t0 = Time.realtimeSinceStartup;
        }

        static void Setup(CityMap m, CityMeshes.Trims t)
        {
            map = m; trims = t;
            // the arc of each edge along its OSM way (the stable key)
            wayOff = new double[map.edges.Length];
            var byWay = new Dictionary<uint, List<CityMap.Edge>>();
            foreach (var e in map.edges) { if (!byWay.TryGetValue(e.wayId, out var l)) byWay[e.wayId] = l = new List<CityMap.Edge>(); l.Add(e); }
            foreach (var list in byWay.Values)
            {
                if (list.Count < 2) continue;
                var startAt = new Dictionary<int, CityMap.Edge>(); var ends = new HashSet<int>();
                foreach (var e in list) { startAt[e.a] = e; ends.Add(e.b); }
                CityMap.Edge first = list[0];
                foreach (var e in list) if (!ends.Contains(e.a)) { first = e; break; }
                var seenE = new HashSet<int>(); double off = 0;
                for (var cur = first; cur != null && seenE.Add(cur.index); startAt.TryGetValue(cur.b, out cur))
                { wayOff[cur.index] = off; off += cur.length; }
            }
            // merge zones: branch/host pairs (the trims' branch table and every BranchSeats piece)
            mergePairs = new HashSet<long>();
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                if (trims.branchA[ei] >= 0) mergePairs.Add(Pair(ei, trims.branchA[ei]));
                if (trims.branchB[ei] >= 0) mergePairs.Add(Pair(ei, trims.branchB[ei]));
            }
            foreach (var seat in CityMeshes.BranchSeats(map, trims))
                foreach (var pc in seat.pieces)
                {
                    var e = map.edges[pc.edge];
                    for (float s = pc.s0; s <= pc.s1; s += 10f)
                    {
                        int h = seat.HostAt(e.PointAt(s), out _);
                        if (h >= 0) mergePairs.Add(Pair(pc.edge, h));
                    }
                }
            onRoute = new HashSet<int>();
            foreach (var r in map.routes) foreach (var ei in r.edges) onRoute.Add(ei);
            refSpots = RefSpots();
        }
        static long Pair(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

        /// <summary>CityRefSpots' spots (its private table, read by reflection
        /// so the spot list stays in one file), in game metres.</summary>
        static List<Vector2> RefSpots()
        {
            var o = new List<Vector2>();
            try
            {
                var f = typeof(CityRefSpots).GetField("Spots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (!(f?.GetValue(null) is Array arr)) return o;
                const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
                double mLon = 111320.0 * Math.Cos(Lat0 * Math.PI / 180.0);
                foreach (var sp in arr)
                {
                    var t = sp.GetType();
                    double lat = (double)t.GetField("lat").GetValue(sp), lon = (double)t.GetField("lon").GetValue(sp);
                    o.Add(new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[CitySmooth] reference spots unavailable: " + ex.Message); }
            return o;
        }

        // ================================================================
        //  The plan's design edge (the Trims taper TABLE, the plan's shape)
        // ================================================================

        static double HwD(CityMap.Edge e, double s)
        {
            double hw = e.width * 0.5, ha = hw, hb = hw; int i = e.index;
            if (trims.taperA[i] > 0f && s < trims.taperA[i]) ha = trims.hwA[i] + (hw - trims.hwA[i]) * Shape(s / trims.taperA[i]);
            if (trims.taperB[i] > 0f && e.length - s < trims.taperB[i]) hb = trims.hwB[i] + (hw - trims.hwB[i]) * Shape((e.length - s) / trims.taperB[i]);
            return Math.Min(ha, hb);
        }
        static double Shape(double t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return SmoothRules.PlanTaperShape == 1 ? t * t * (3 - 2 * t) : t;
        }
        static double PlanOff(PlanLine L, CityMap.Edge e, double s, double hw) =>
            L.anchor == 'C' ? L.off : L.anchor == 'L' ? hw - L.inset : -(hw - L.inset);
        static bool PlanExists(PlanLine L, double hw) =>
            L.anchor == 'C' ? Math.Abs(L.off) < hw - SmoothRules.ExistInsetM : hw - L.inset > 0;

        // ================================================================
        //  The design centreline (A-family reference)
        // ================================================================

        sealed class RefLine { public double[] X, Z, S; public double off; }
        static readonly Dictionary<int, RefLine> refCache = new Dictionary<int, RefLine>();

        /// <summary>The through neighbour at a mitred node whose ribbon meets
        /// this one there, or -1.</summary>
        static int JointAt(CityMap.Edge e, int n)
        {
            if (!trims.mitre[n] || e.a == e.b) return -1;
            int o = trims.throughA[n] == e.index ? trims.throughB[n] : trims.throughB[n] == e.index ? trims.throughA[n] : -1;
            if (o < 0 || o == e.index || map.edges[o].a == map.edges[o].b) return -1;
            return o;
        }

        static RefLine RefOf(CityMap.Edge e)
        {
            if (refCache.TryGetValue(e.index, out var r)) return r;
            var X = new List<double>(); var Z = new List<double>();
            var pre = new List<Vector2>();
            int oa = JointAt(e, e.a);
            if (oa >= 0)
            {
                var o = map.edges[oa]; var P = o.pts; bool rev = o.b != e.a;
                double acc = 0;
                for (int k = P.Length - 2; k >= 0 && acc < 40; k--)
                {
                    var p0 = rev ? P[P.Length - 1 - k] : P[k]; var p1 = rev ? P[P.Length - 2 - k] : P[k + 1];
                    acc += Vector2.Distance(p0, p1); pre.Insert(0, p0);
                }
            }
            foreach (var p in pre) { X.Add(p.x); Z.Add(p.y); }
            foreach (var p in e.pts) { X.Add(p.x); Z.Add(p.y); }
            int ob = JointAt(e, e.b);
            if (ob >= 0)
            {
                var o = map.edges[ob]; var P = o.pts; bool rev = o.a != e.b;
                double acc = 0;
                for (int k = 1; k < P.Length && acc < 40; k++)
                {
                    var p0 = rev ? P[P.Length - k] : P[k - 1]; var p1 = rev ? P[P.Length - 1 - k] : P[k];
                    acc += Vector2.Distance(p0, p1); X.Add(p1.x); Z.Add(p1.y);
                }
            }
            var S = new double[X.Count];
            for (int i = 1; i < S.Length; i++) S[i] = S[i - 1] + Math.Sqrt((X[i] - X[i - 1]) * (X[i] - X[i - 1]) + (Z[i] - Z[i - 1]) * (Z[i] - Z[i - 1]));
            r = new RefLine { X = X.ToArray(), Z = Z.ToArray(), S = S, off = pre.Count > 0 ? S[pre.Count] : 0 };
            refCache[e.index] = r;
            return r;
        }

        /// <summary>Signed distance from the design centreline (nearest point,
        /// + = left of travel): an offset line's error is sd - its offset.</summary>
        static double Sd(RefLine rf, double x, double z, double sHint)
        {
            var X = rf.X; var Z = rf.Z; var S = rf.S; int n = X.Length;
            double target = sHint + rf.off;
            int lo = 0, hi = n - 2;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (S[m] <= target) lo = m; else hi = m - 1; }
            double best = double.MaxValue, bt = 0; int bi = lo;
            void Scan(int i)
            {
                double dx = X[i + 1] - X[i], dz = Z[i + 1] - Z[i], L2 = dx * dx + dz * dz;
                double t = L2 > 1e-12 ? Math.Max(0, Math.Min(1, ((x - X[i]) * dx + (z - Z[i]) * dz) / L2)) : 0;
                double qx = X[i] + dx * t - x, qz = Z[i] + dz * t - z, d = qx * qx + qz * qz;
                if (d < best) { best = d; bi = i; bt = t; }
            }
            for (int i = lo; i >= 0 && S[lo] - S[i + 1] < 30; i--) Scan(i);
            for (int i = lo + 1; i + 1 < n && S[i] - S[lo + 1] < 30; i++) Scan(i);
            double tx = X[bi + 1] - X[bi], tz = Z[bi + 1] - Z[bi];
            Norm(ref tx, ref tz);
            if (bt <= 1e-9 && bi > 0) { double px = X[bi] - X[bi - 1], pz = Z[bi] - Z[bi - 1]; Norm(ref px, ref pz); tx += px; tz += pz; Norm(ref tx, ref tz); }
            else if (bt >= 1 - 1e-9 && bi + 2 < n) { double nx = X[bi + 2] - X[bi + 1], nz = Z[bi + 2] - Z[bi + 1]; Norm(ref nx, ref nz); tx += nx; tz += nz; Norm(ref tx, ref tz); }
            double fx = X[bi] + (X[bi + 1] - X[bi]) * bt, fz = Z[bi] + (Z[bi + 1] - Z[bi]) * bt;
            double side = tx * (z - fz) - tz * (x - fx);
            return (side >= 0 ? 1 : -1) * Math.Sqrt(best);
        }
        static void Norm(ref double x, ref double z) { double m = Math.Sqrt(x * x + z * z); if (m > 0) { x /= m; z /= m; } }

        /// <summary>B2 on the design polyline within 1 m of s: the DATA /
        /// BUILDER cause hint.</summary>
        static bool DataKinkNear(CityMap.Edge e, double s)
        {
            var rf = RefOf(e); double t = s + rf.off;
            for (int i = 1; i + 1 < rf.X.Length; i++)
            {
                if (Math.Abs(rf.S[i] - t) > 1) continue;
                double th = Math.Abs(Turn(rf.X[i - 1], rf.Z[i - 1], rf.X[i], rf.Z[i], rf.X[i + 1], rf.Z[i + 1]));
                double cm = Math.Min(Math.Min(rf.S[i] - rf.S[i - 1], rf.S[i + 1] - rf.S[i]), SmoothRules.ChordCapM);
                if (cm * th / 8 > V) return true;
            }
            return false;
        }
        static double Turn(double ax, double az, double bx, double bz, double cx, double cz)
        {
            double ux = bx - ax, uz = bz - az, vx = cx - bx, vz = cz - bz;
            return Math.Atan2(ux * vz - uz * vx, ux * vx + uz * vz);
        }

        // ================================================================
        //  Runs
        // ================================================================

        /// <summary>Consecutive samples over their limit along one line,
        /// merged, streamed (no sample objects).</summary>
        sealed class RunBuilder
        {
            readonly string check, lineId, kind, reportOnly; readonly bool minIsWorse;
            Run cur; double arc, px = double.NaN, pz;
            public RunBuilder(string check, string lineId, string kind = null, string reportOnly = null, bool minIsWorse = false)
            { this.check = check; this.lineId = lineId; this.kind = kind; this.reportOnly = reportOnly; this.minIsWorse = minIsWorse; }
            public double rLimit;
            public void Push(double x, double y, double z, int e, double s, double val, bool bad, char tag = ' ', int span = -1, char side = ' ')
            {
                if (!double.IsNaN(px)) arc += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                px = x; pz = z;
                if (!bad) { Close(); return; }
                if (cur == null)
                    cur = new Run { check = check, lineId = lineId, kind = kind, reportOnly = reportOnly, e = e, s = s, x = x, y = y, z = z, val = val,
                                    e0 = e, s0 = s, e1 = e, s1 = s, tag = tag, span = span, side = side, rLimit = rLimit, len = -arc };
                cur.e1 = e; cur.s1 = s;
                cur.ratio = arc;   // scratch: the arc at the last bad sample
                if (minIsWorse ? val < cur.val : Math.Abs(val) > Math.Abs(cur.val))
                { cur.val = val; cur.e = e; cur.s = s; cur.x = x; cur.y = y; cur.z = z; cur.tag = tag; cur.span = span; cur.side = side; }
            }
            public void Close()
            {
                if (cur == null) return;
                cur.len = cur.ratio + cur.len; cur.ratio = 0;
                Keep(cur); cur = null;
            }
        }

        // the tile being analysed: a run is built over the whole 3x3 ring and kept
        // by the tile its worst sample lies in, so a run crossing a tile edge is
        // counted once, as linecheck.mjs counts it
        static double cx0, cz0, cx1, cz1;
        static bool InCentre(double x, double z) => x >= cx0 && x < cx1 && z >= cz0 && z < cz1;
        static bool NearCentre(double x, double z, double m) => x >= cx0 - m && x < cx1 + m && z >= cz0 - m && z < cz1 + m;
        static void Keep(Run r) { if (InCentre(r.x, r.z)) runs.Add(r); }

        // ================================================================
        //  One tile: chains over its 3x3 ring (gate spec 5.2)
        // ================================================================

        struct Sec { public double s, px, pz, rx, rz; public ushort f; }
        struct Pt { public double x, y, z, v, s; public int e, span, tile; public char tag, side; public bool x3, crop, gore; }

        sealed class EdgeData
        {
            public CityMap.Edge e; public List<Quad> quads; public Sec[] A, B; public Layout lay;
            public List<List<Pt>>[] lines; public List<List<double>>[] ratios;
            public List<List<Pt>> ribL = new List<List<Pt>>(), ribR = new List<List<Pt>>(), mid = new List<List<Pt>>();
            public bool fwd;
        }

        static void Analyze(int tx, int tz)
        {
            if (!analysed.Add(TileKey(tx, tz))) return;
            var byEdge = new Dictionary<int, List<Quad>>();
            var ringFans = new List<FanRec>(); var ringGores = new List<Quad>();
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!frags.TryGetValue(TileKey(tx + dx, tz + dz), out var f)) continue;
                    foreach (var q in f.quads) { if (!byEdge.TryGetValue(q.edge, out var l)) byEdge[q.edge] = l = new List<Quad>(); l.Add(q); }
                    ringFans.AddRange(f.fans); ringGores.AddRange(f.gores);
                }
            cx0 = tx * CityMeshes.TileSize; cz0 = tz * CityMeshes.TileSize; cx1 = cx0 + CityMeshes.TileSize; cz1 = cz0 + CityMeshes.TileSize;
            foreach (var l in byEdge.Values) l.Sort((a, b) => a.sA.CompareTo(b.sA));
            var grid = new PaveGrid(byEdge, ringGores, ringFans);
            // chains through mitred nodes, over the ring's edges, that touch this tile
            var used = new HashSet<int>();
            foreach (var kv in byEdge)
            {
                if (used.Contains(kv.Key)) continue;
                bool touches = false;
                foreach (var q in kv.Value) if (NearCentre((q.AL.x + q.BR.x) * 0.5, (q.AL.z + q.BR.z) * 0.5, 12)) { touches = true; break; }
                if (!touches) continue;
                var chain = ChainFrom(map.edges[kv.Key], byEdge, used);
                AnalyzeChain(chain, byEdge, grid);
            }
            // fan perimeters (curb returns) in this tile
            foreach (var fan in ringFans) if (fan.tx == tx && fan.tz == tz) FanChecks(fan);
        }

        static List<(CityMap.Edge e, bool fwd)> ChainFrom(CityMap.Edge e0, Dictionary<int, List<Quad>> byEdge, HashSet<int> used)
        {
            var cur = e0; int enter = e0.a;
            for (int g = 0; g < 100000; g++)
            {
                int o = JointAt(cur, enter);
                if (o < 0 || o == e0.index || !byEdge.ContainsKey(o)) break;
                var oe = map.edges[o]; enter = oe.a == enter ? oe.b : oe.a; cur = oe;
            }
            var list = new List<(CityMap.Edge, bool)>();
            var at = cur; int from = enter;
            while (at != null && used.Add(at.index))
            {
                bool fwd = at.a == from;
                list.Add((at, fwd));
                int exit = fwd ? at.b : at.a;
                int o = JointAt(at, exit);
                if (o < 0 || !byEdge.ContainsKey(o)) break;
                at = map.edges[o]; from = exit;
            }
            return list;
        }

        // ---- extraction of one edge, in its own direction
        static EdgeData Extract(CityMap.Edge e, List<Quad> quads)
        {
            var ed = new EdgeData { e = e, quads = quads };
            int n = quads.Count;
            ed.A = new Sec[n]; ed.B = new Sec[n];
            for (int i = 0; i < n; i++) { ed.A[i] = SecOf(e, quads[i].sA, quads[i].AL, quads[i].AR, quads[i].fA); ed.B[i] = SecOf(e, quads[i].sB, quads[i].BL, quads[i].BR, quads[i].fB); }
            ed.lay = LayoutOf(quads[0].slot);
            var lay = ed.lay;
            ed.lines = new List<List<Pt>>[lay.runs.Count]; ed.ratios = new List<List<double>>[lay.runs.Count];
            double W = lay.width > 0 ? lay.width : e.width;
            for (int k = 0; k < lay.runs.Count; k++)
            {
                var pieces = new List<List<Pt>>(); var rats = new List<List<double>>();
                List<Pt> piece = null; List<double> rat = null;
                double u = lay.runs[k].u;
                for (int i = 0; i < n; i++)
                {
                    var q = quads[i];
                    bool adj = i > 0 && Math.Abs(quads[i - 1].sB - q.sA) < 1e-3f;
                    if (!adj) piece = null;
                    foreach (var sg in QuadIso(q, u))
                    {
                        var pa = MakePt(ed, i, sg.ax, sg.ay, sg.az, sg.av, sg.ea); var pb = MakePt(ed, i, sg.bx, sg.by, sg.bz, sg.bv, sg.eb);
                        double ratio = 1.0 / (sg.grad * W);
                        if (piece != null)
                        {
                            var last = piece[piece.Count - 1];
                            double d = Math.Sqrt((last.x - pa.x) * (last.x - pa.x) + (last.z - pa.z) * (last.z - pa.z));
                            if (d <= SmoothRules.JoinM) { piece.Add(pb); rat.Add(ratio); continue; }
                            // a line that does not meet itself across a section two tiles cut: a SEAM
                            if (sg.ea == 'A' && last.tag == 'B' && quads[i - 1].tx * 100000 + quads[i - 1].tz != q.tx * 100000 + q.tz && d > SmoothRules.SeamM)
                                Keep(new Run { check = "B4s", lineId = PlanIdOf(lay, k), kind = "paint", e = e.index, s = q.sA, x = pa.x, y = pa.y, z = pa.z, val = d, e0 = e.index, s0 = q.sA, e1 = e.index, s1 = q.sA, tag = 'A', span = i });
                        }
                        piece = new List<Pt> { pa, pb }; rat = new List<double> { ratio };
                        pieces.Add(piece); rats.Add(rat);
                    }
                }
                ed.lines[k] = pieces; ed.ratios[k] = rats;
            }
            // ribbon edges and midline, per section
            foreach (char side in new[] { 'L', 'R' })
            {
                var into = side == 'L' ? ed.ribL : ed.ribR;
                List<Pt> piece = null;
                for (int i = 0; i < n; i++)
                {
                    var q = quads[i];
                    bool adj = i > 0 && Math.Abs(quads[i - 1].sB - q.sA) < 1e-3f;
                    var a = side == 'L' ? q.AL : q.AR; var b = side == 'L' ? q.BL : q.BR;
                    var pa = RibPt(ed, i, a, ed.A[i], side, q.sA); var pb = RibPt(ed, i, b, ed.B[i], side, q.sB);
                    if (piece != null && adj)
                    {
                        var last = piece[piece.Count - 1];
                        double d = Math.Sqrt((last.x - pa.x) * (last.x - pa.x) + (last.z - pa.z) * (last.z - pa.z));
                        if (d <= SmoothRules.JoinM) { piece.Add(pb); continue; }
                        if (quads[i - 1].tx != q.tx || quads[i - 1].tz != q.tz)
                        {
                            if (d > SmoothRules.SeamM) Keep(new Run { check = "B4s", lineId = "R" + side, kind = "edge", e = e.index, s = q.sA, x = pa.x, y = pa.y, z = pa.z, val = d, e0 = e.index, s0 = q.sA, e1 = e.index, s1 = q.sA, span = i, side = side });
                        }
                        else if (d > V) Keep(new Run { check = "B4", lineId = "R" + side, kind = "edge", e = e.index, s = q.sA, x = pa.x, y = pa.y, z = pa.z, val = d, e0 = e.index, s0 = q.sA, e1 = e.index, s1 = q.sA, span = i, side = side });
                    }
                    piece = new List<Pt> { pa, pb }; into.Add(piece);
                }
            }
            {
                List<Pt> piece = null;
                for (int i = 0; i < n; i++)
                {
                    var q = quads[i];
                    bool adj = i > 0 && Math.Abs(quads[i - 1].sB - q.sA) < 1e-3f;
                    var pa = MidPt(ed, i, q.AL, q.AR, ed.A[i], q.sA); var pb = MidPt(ed, i, q.BL, q.BR, ed.B[i], q.sB);
                    if (piece != null && adj)
                    {
                        var last = piece[piece.Count - 1];
                        if (Math.Abs(last.x - pa.x) + Math.Abs(last.z - pa.z) <= SmoothRules.JoinM) { piece.Add(pb); continue; }
                    }
                    piece = new List<Pt> { pa, pb }; ed.mid.Add(piece);
                }
            }
            return ed;
        }

        static Sec SecOf(CityMap.Edge e, float s, Vector3d L, Vector3d R, ushort f)
        {
            var p = e.PointAt(s);
            double rx = R.x - L.x, rz = R.z - L.z, m = Math.Sqrt(rx * rx + rz * rz);
            if (m < 1e-4) { var t = e.TangentAt(s); rx = -t.y; rz = t.x; } else { rx /= m; rz /= m; }
            return new Sec { s = s, px = p.x, pz = p.y, rx = rx, rz = rz, f = f };
        }

        const ushort FMovedL = CityMeshes.RoadTap.FMovedL, FMovedR = CityMeshes.RoadTap.FMovedR, FClipL = CityMeshes.RoadTap.FClipL,
                     FClipR = CityMeshes.RoadTap.FClipR, FCollapsed = CityMeshes.RoadTap.FCollapsed;

        static Pt RibPt(EdgeData ed, int i, Vector3d v, Sec c, char side, float s)
        {
            bool crop = (c.f & (side == 'L' ? FMovedL : FMovedR)) != 0 || (c.f & FCollapsed) != 0;
            bool x3 = (c.f & (side == 'L' ? FClipL : FClipR)) != 0 || (c.f & FCollapsed) != 0;
            return new Pt { x = v.x, y = v.y, z = v.z, s = s, e = ed.e.index, span = i, tile = ed.quads[i].tx * 100000 + ed.quads[i].tz, tag = 'S', side = side, crop = crop, x3 = x3 };
        }
        static Pt MidPt(EdgeData ed, int i, Vector3d L, Vector3d R, Sec c, float s)
        {
            bool clip = (c.f & (FClipL | FClipR | FCollapsed)) != 0;
            return new Pt { x = (L.x + R.x) * 0.5, y = (L.y + R.y) * 0.5, z = (L.z + R.z) * 0.5, s = s, e = ed.e.index, span = i, tag = 'S', x3 = clip,
                            crop = clip || (c.f & (FMovedL | FMovedR)) != 0 };
        }

        /// <summary>(s, lat) of a point of quad i: exact on a section, else
        /// from the frame c(t) = lerp(pA, pB, t), r(t) = lerp(rA, rB, t).</summary>
        static Pt MakePt(EdgeData ed, int i, double x, double y, double z, double v, char tag)
        {
            var A = ed.A[i]; var B = ed.B[i];
            double s;
            if (tag == 'A') s = A.s;
            else if (tag == 'B') s = B.s;
            else
            {
                double Dx = B.px - A.px, Dz = B.pz - A.pz, dx = x - A.px, dz = z - A.pz;
                double R1x = B.rx - A.rx, R1z = B.rz - A.rz;
                double a2 = -(Dx * R1z - Dz * R1x), a1 = (dx * R1z - dz * R1x) - (Dx * A.rz - Dz * A.rx), a0 = dx * A.rz - dz * A.rx;
                double DD = Dx * Dx + Dz * Dz, tl = DD > 1e-9 ? (dx * Dx + dz * Dz) / DD : 0, t = tl;
                if (Math.Abs(a2) < 1e-9) { if (Math.Abs(a1) > 1e-12) t = -a0 / a1; }
                else
                {
                    double disc = a1 * a1 - 4 * a2 * a0;
                    if (disc >= 0) { double sq = Math.Sqrt(disc), r1 = (-a1 + sq) / (2 * a2), r2 = (-a1 - sq) / (2 * a2); t = Math.Abs(r1 - tl) < Math.Abs(r2 - tl) ? r1 : r2; }
                }
                if (!(t > -0.1 && t < 1.1)) t = Math.Max(0, Math.Min(1, tl));
                s = A.s + (B.s - A.s) * t;
            }
            return new Pt { x = x, y = y, z = z, v = v, s = s, e = ed.e.index, span = i, tag = tag, tile = ed.quads[i].tx * 100000 + ed.quads[i].tz };
        }

        struct IsoSeg { public double ax, ay, az, av, bx, by, bz, bv, grad; public char ea, eb; }

        /// <summary>The iso-line U = u inside one quad's two triangles. Its
        /// five edges are crossed once each, in one direction - A: AL-AR,
        /// B: BL-BR, D: AL-BR, L: AL-BL, R: AR-BR - so the two triangles and
        /// two quads that share an edge agree to the bit. T2 = (AL, AR, BR)
        /// owns A, R, D; T1 = (AL, BR, BL) owns D, B, L (Bucket.Quad).</summary>
        static readonly List<IsoSeg> isoOut = new List<IsoSeg>(2);
        static List<IsoSeg> QuadIso(Quad q, double u)
        {
            isoOut.Clear();
            var X = new (bool ok, double x, double y, double z, double v)[5];
            X[0] = Cross(q.AL, q.uAL, q.AR, q.uAR, u); X[1] = Cross(q.BL, q.uBL, q.BR, q.uBR, u);
            X[2] = Cross(q.AL, q.uAL, q.BR, q.uBR, u); X[3] = Cross(q.AL, q.uAL, q.BL, q.uBL, u);
            X[4] = Cross(q.AR, q.uAR, q.BR, q.uBR, u);
            const string letters = "ABDLR";
            int[] ord = { 0, 3, 2, 1, 1 };   // travel order: A < L/R < D < B
            foreach (var tri in new[] { new[] { 0, 4, 2 }, new[] { 2, 1, 3 } })
            {
                int h0 = -1, h1 = -1;
                foreach (int k in tri) if (X[k].ok) { if (h0 < 0) h0 = k; else if (h1 < 0) h1 = k; else h1 = -2; }
                if (h0 < 0 || h1 < 0) continue;
                if (ord[h1] < ord[h0]) { int t = h0; h0 = h1; h1 = t; }
                double grad = tri[0] == 0 ? GradU(q.AL, q.uAL.x, q.AR, q.uAR.x, q.BR, q.uBR.x) : GradU(q.AL, q.uAL.x, q.BR, q.uBR.x, q.BL, q.uBL.x);
                isoOut.Add(new IsoSeg { ax = X[h0].x, ay = X[h0].y, az = X[h0].z, av = X[h0].v, bx = X[h1].x, by = X[h1].y, bz = X[h1].z, bv = X[h1].v, ea = letters[h0], eb = letters[h1], grad = grad });
            }
            return isoOut;
        }
        static (bool, double, double, double, double) Cross(Vector3d a, Vector2 ua, Vector3d b, Vector2 ub, double u)
        {
            double da = ua.x - u, db = ub.x - u;
            if ((da >= 0) == (db >= 0)) return (false, 0, 0, 0, 0);
            double t = da / (da - db);
            return (true, a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, ua.y + (ub.y - ua.y) * t);
        }
        static double GradU(Vector3d p, double up, Vector3d q, double uq, Vector3d r, double ur)
        {
            double x1 = q.x - p.x, z1 = q.z - p.z, x2 = r.x - p.x, z2 = r.z - p.z, det = x1 * z2 - x2 * z1;
            if (Math.Abs(det) < 1e-9) return double.NaN;
            double du1 = uq - up, du2 = ur - up;
            return Math.Sqrt(Math.Pow((du1 * z2 - du2 * z1) / det, 2) + Math.Pow((x1 * du2 - x2 * du1) / det, 2));
        }
        static string PlanIdOf(Layout lay, int k)
        {
            int j = lay.runPlan[k];
            return j >= 0 ? lay.plan[j].id : "T" + lay.runs[k].col + (lay.runs[k].dashed ? "d" : "s") + "u" + lay.runs[k].u.ToString("0.000", Inv);
        }

        // ---- D1: the pavement of the ring (ribbons, gore quads, fans) in a 16 m grid
        sealed class PaveGrid
        {
            const double Cell = 16;
            readonly List<(Vector3d a, Vector3d b, Vector3d c, int owner, int refIdx)> tris = new List<(Vector3d, Vector3d, Vector3d, int, int)>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            public readonly List<Quad> quadRefs = new List<Quad>();
            public readonly List<FanRec> fanRefs = new List<FanRec>();
            public PaveGrid(Dictionary<int, List<Quad>> byEdge, List<Quad> gores, List<FanRec> fans)
            {
                foreach (var kv in byEdge) foreach (var q in kv.Value) { int r = quadRefs.Count; quadRefs.Add(q); Add(q.AL, q.BR, q.BL, kv.Key, r); Add(q.AL, q.AR, q.BR, kv.Key, r); }
                foreach (var q in gores) { int r = quadRefs.Count; quadRefs.Add(q); Add(q.AL, q.BR, q.BL, -2, r); Add(q.AL, q.AR, q.BR, -2, r); }
                foreach (var f in fans)
                {
                    int r = fanRefs.Count; fanRefs.Add(f);
                    for (int k = 0; k < f.corners.Length; k++) Add(f.centre, f.corners[k], f.corners[(k + 1) % f.corners.Length], -10 - f.node, r);
                }
            }
            void Add(Vector3d a, Vector3d b, Vector3d c, int owner, int r)
            {
                double area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
                if (Math.Abs(area) < 1e-4) return;
                int id = tris.Count; tris.Add((a, b, c, owner, r));
                int x0 = (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)) / Cell), x1 = (int)Math.Floor(Math.Max(a.x, Math.Max(b.x, c.x)) / Cell);
                int z0 = (int)Math.Floor(Math.Min(a.z, Math.Min(b.z, c.z)) / Cell), z1 = (int)Math.Floor(Math.Max(a.z, Math.Max(b.z, c.z)) / Cell);
                for (int cx = x0; cx <= x1; cx++) for (int cz = z0; cz <= z1; cz++) { long k = ((long)cx << 32) ^ (uint)cz; if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>(); l.Add(id); }
            }
            /// <summary>The deepest other pavement at the same level under (x, z):
            /// its owner (edge, -2 gore, -10-node fan) and how far inside its
            /// boundary the point is.</summary>
            public (int owner, double depth) Under(double x, double y, double z, HashSet<int> own)
            {
                long k = ((long)Math.Floor(x / Cell) << 32) ^ (uint)(int)Math.Floor(z / Cell);
                if (!cells.TryGetValue(k, out var l)) return (-1, 0);
                int best = -1; double depth = 0;
                foreach (int id in l)
                {
                    var (a, b, c, owner, r) = tris[id];
                    if (owner >= 0 && own.Contains(owner)) continue;
                    if (!Bary(a, b, c, x, z, out double wa, out double wb, out double wc)) continue;
                    double ty = a.y * wa + b.y * wb + c.y * wc;
                    if (Math.Abs(ty - y) > SmoothRules.CrossDyM) continue;
                    double d = owner <= -10 ? FanDepth(fanRefs[r], x, z) : QuadDepth(quadRefs[r], x, z);
                    if (d > depth) { depth = d; best = owner; }
                }
                return (best, depth);
            }
            static bool Bary(Vector3d a, Vector3d b, Vector3d c, double x, double z, out double wa, out double wb, out double wc)
            {
                double det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                wa = wb = wc = 0;
                if (Math.Abs(det) < 1e-12) return false;
                wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                wc = 1 - wa - wb;
                return wa >= -1e-9 && wb >= -1e-9 && wc >= -1e-9;
            }
            /// <summary>Inside distance across a ribbon quad: to the nearer of its
            /// two long sides (the drawn edges).</summary>
            static double QuadDepth(Quad q, double x, double z) => Math.Min(LineDist(q.AL, q.BL, x, z), LineDist(q.AR, q.BR, x, z));
            static double FanDepth(FanRec f, double x, double z)
            {
                double m = double.MaxValue;
                for (int k = 0; k < f.corners.Length; k++) m = Math.Min(m, SegDist(f.corners[k], f.corners[(k + 1) % f.corners.Length], x, z));
                return m;
            }
            static double LineDist(Vector3d a, Vector3d b, double x, double z)
            {
                double dx = b.x - a.x, dz = b.z - a.z, L = Math.Sqrt(dx * dx + dz * dz);
                return L < 1e-9 ? Math.Sqrt((x - a.x) * (x - a.x) + (z - a.z) * (z - a.z)) : Math.Abs(dx * (z - a.z) - dz * (x - a.x)) / L;
            }
            static double SegDist(Vector3d a, Vector3d b, double x, double z)
            {
                double dx = b.x - a.x, dz = b.z - a.z, L2 = dx * dx + dz * dz;
                double t = L2 > 1e-12 ? Math.Max(0, Math.Min(1, ((x - a.x) * dx + (z - a.z) * dz) / L2)) : 0;
                double qx = a.x + dx * t - x, qz = a.z + dz * t - z;
                return Math.Sqrt(qx * qx + qz * qz);
            }
        }

        static bool MergeZone(int e, int other)
        {
            if (other < 0) return false;
            if (mergePairs.Contains(Pair(e, other))) return true;
            var a = map.edges[e]; var o = map.edges[other];
            foreach (int n in new[] { a.a, a.b })
                if ((n == o.a || n == o.b) && (trims.mitre[n])) return true;
            return false;
        }

        // ---- the A-family, grids and D1 on one edge (its own direction)
        static void CheckEdge(EdgeData ed, HashSet<int> chainSet, PaveGrid grid)
        {
            var e = ed.e; var lay = ed.lay; var rf = RefOf(e);
            double sMin = ed.quads[0].sA, sMax = ed.quads[ed.quads.Count - 1].sB;
            double BIN = SmoothRules.SampleStepM;
            int nb = Math.Max(1, (int)Math.Ceiling((sMax - sMin) / BIN));
            double BinS(int k) => sMin + (k + 0.5) * BIN;
            int SpanAt(double s) { int lo = 0, hi = ed.quads.Count - 1; while (lo < hi) { int m = (lo + hi) >> 1; if (ed.quads[m].sB < s) lo = m + 1; else hi = m; } return lo; }
            foreach (var q in ed.quads) if (InCentre((q.AL.x + q.BR.x) * 0.5, (q.AL.z + q.BR.z) * 0.5)) ribbonKm += (q.sB - q.sA) / 1000.0;
            var G = new double[lay.runs.Count][];
            for (int k = 0; k < G.Length; k++) { G[k] = new double[nb]; for (int b = 0; b < nb; b++) G[k][b] = double.NaN; }
            var GL = new double[nb]; var GR = new double[nb];
            for (int b = 0; b < nb; b++) { GL[b] = GR[b] = double.NaN; }
            void Fill(double[] g, Pt a, Pt b2, double sda, double sdb)
            {
                double lo = Math.Min(a.s, b2.s), hi = Math.Max(a.s, b2.s);
                int k0 = Math.Max(0, (int)Math.Ceiling((lo - sMin) / BIN - 0.5)), k1 = Math.Min(nb - 1, (int)Math.Floor((hi - sMin) / BIN - 0.5));
                for (int k = k0; k <= k1; k++) { double t = hi - lo > 1e-9 ? (BinS(k) - a.s) / (b2.s - a.s) : 0; if (double.IsNaN(g[k])) g[k] = sda + (sdb - sda) * t; }
            }
            // ---- painted lines: A1, A4, A5, D1 on samples
            for (int k = 0; k < lay.runs.Count; k++)
            {
                int j = lay.runPlan[k]; var plan = j >= 0 ? lay.plan[j] : null; char col = lay.runs[k].col; double q = lay.runQ[k];
                string lineId = PlanIdOf(lay, k);
                for (int pi = 0; pi < ed.lines[k].Count; pi++)
                {
                    var pc = ed.lines[k][pi]; var rat = ed.ratios[k][pi];
                    var bA1 = new RunBuilder("A1", lineId); var bA4 = new RunBuilder("A4", lineId); var bA5 = new RunBuilder("A5", lineId);
                    var bD1 = new RunBuilder("D1", lineId); var bD1m = new RunBuilder("D1", lineId, null, "merge zone (BranchSeats attach arc): report-only until WP-18b");
                    for (int i = 1; i < pc.Count; i++)
                    {
                        var a = pc[i - 1]; var b = pc[i];
                        double segL = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                        if (InCentre(b.x, b.z)) lineKm += segL / 1000.0;
                        int n = Math.Max(1, (int)Math.Ceiling(segL / BIN));
                        double sda = Sd(rf, a.x, a.z, a.s), sdb = Sd(rf, b.x, b.z, b.s);
                        Fill(G[k], a, b, sda, sdb);
                        double wv = double.IsNaN(rat[i - 1]) || double.IsInfinity(rat[i - 1]) ? 0 : rat[i - 1] - 1; bool wbad = Math.Abs(wv) > SmoothRules.LineWidthTol;
                        for (int m = i == 1 ? 0 : 1; m <= n; m++)
                        {
                            double t = m / (double)n, x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t, z = a.z + (b.z - a.z) * t, s = a.s + (b.s - a.s) * t;
                            double sd = m == 0 ? sda : m == n ? sdb : Sd(rf, x, z, s);
                            char tag = m == 0 ? a.tag : m == n ? b.tag : 'I'; int span = b.span;
                            double hw = HwD(e, s);
                            if (plan != null && PlanExists(plan, hw))
                            {
                                double err = sd - (PlanOff(plan, e, s, hw) + q);
                                bA1.Push(x, y, z, e.index, s, err, Math.Abs(err) > V, tag, span);
                                if (captureEdge == e.index && captureLine == lineId) captured.Add((new Vector3((float)x, (float)y, (float)z), err));
                            }
                            else bA1.Push(x, y, z, e.index, s, 0, false);
                            bA4.Push(x, y, z, e.index, s, wv, wbad, tag, span);
                            double dmin = double.MaxValue;
                            for (int jj = 0; jj < lay.plan.Count; jj++)
                            {
                                var pj = lay.plan[jj];
                                if (pj.col != col || !PlanExists(pj, hw)) continue;
                                int kk = lay.planRun[jj];
                                double d = Math.Abs(sd - PlanOff(pj, e, s, hw) - (kk >= 0 ? lay.runQ[kk] : 0));
                                if (d < dmin) dmin = d;
                            }
                            bA5.Push(x, y, z, e.index, s, Math.Min(dmin, 10), dmin > SmoothRules.StrayM, tag, span);
                            var (owner, depth) = grid.Under(x, y, z, chainSet);
                            bool cross = depth > SmoothRules.CrossM, mz = cross && MergeZone(e.index, owner);
                            bD1.Push(x, y, z, e.index, s, depth, cross && !mz, tag, span);
                            bD1m.Push(x, y, z, e.index, s, depth, cross && mz, tag, span);
                        }
                    }
                    bA1.Close(); bA4.Close(); bA5.Close(); bD1.Close(); bD1m.Close();
                }
            }
            // ---- ribbon edges: A1-edge, uncropped sections only (X6)
            foreach (char side in new[] { 'L', 'R' })
            {
                double sgn = side == 'L' ? -1 : 1;
                foreach (var pc in side == 'L' ? ed.ribL : ed.ribR)
                {
                    var b1 = new RunBuilder("A1", "R" + side);
                    for (int i = 1; i < pc.Count; i++)
                    {
                        var a = pc[i - 1]; var b = pc[i];
                        double segL = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                        int n = Math.Max(1, (int)Math.Ceiling(segL / BIN));
                        double sda = Sd(rf, a.x, a.z, a.s), sdb = Sd(rf, b.x, b.z, b.s);
                        Fill(side == 'L' ? GL : GR, a, b, sda, sdb);
                        bool cropped = a.crop || b.crop;
                        for (int m = i == 1 ? 0 : 1; m <= n; m++)
                        {
                            double t = m / (double)n, x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t, z = a.z + (b.z - a.z) * t, s = a.s + (b.s - a.s) * t;
                            if (cropped) { b1.Push(x, y, z, e.index, s, 0, false); continue; }
                            double sd = m == 0 ? sda : m == n ? sdb : Sd(rf, x, z, s);
                            double err = sd - sgn * HwD(e, s);
                            b1.Push(x, y, z, e.index, s, err, Math.Abs(err) > V, m == 0 || m == n ? 'S' : 'I', b.span, side);
                        }
                    }
                    b1.Close();
                }
            }
            bool Cropped(int span, char side)
            {
                var q = ed.quads[span];
                ushort f = (ushort)(q.fA | q.fB);
                if ((f & FCollapsed) != 0) return true;
                return side == 'L' ? (f & FMovedL) != 0 : side == 'R' ? (f & FMovedR) != 0 : (f & (FMovedL | FMovedR)) != 0;
            }
            // ---- A2 SKEW (undivided two-way rows): the centre pair against the drawn midline
            if (lay.key != null && lay.key.StartsWith("tw"))
            {
                int j1 = -1, j2 = -1;
                for (int jj = 0; jj < lay.plan.Count; jj++)
                {
                    var p = lay.plan[jj]; if (p.anchor != 'C' || p.col != 'Y') continue;
                    if (j1 < 0 || p.off < lay.plan[j1].off) j1 = jj;
                    if (j2 < 0 || p.off > lay.plan[j2].off) j2 = jj;
                }
                int k1 = j1 >= 0 ? lay.planRun[j1] : -1, k2 = j2 >= 0 ? lay.planRun[j2] : -1;
                if (k1 >= 0 && k2 >= 0 && k1 != k2)
                {
                    var b2 = new RunBuilder("A2", "CPAIR");
                    for (int b = 0; b < nb; b++)
                    {
                        double s = BinS(b); var p = e.PointAt((float)s); int sp = SpanAt(s);
                        double va = G[k1][b], vb = G[k2][b], l = GL[b], r = GR[b];
                        if (Cropped(sp, ' ') || double.IsNaN(va) || double.IsNaN(vb) || double.IsNaN(l) || double.IsNaN(r)) { b2.Push(p.x, 0, p.y, e.index, s, 0, false); continue; }
                        double skew = (va + vb) / 2 - (lay.runQ[k1] + lay.runQ[k2]) / 2 - (l + r) / 2;
                        b2.Push(p.x, 0, p.y, e.index, s, skew, Math.Abs(skew) > V, ' ', sp);
                    }
                    b2.Close();
                }
            }
            // ---- A3 INSET: each edge line against its own drawn edge
            foreach (var (pid, side) in new[] { ("EL", 'R'), ("ER", 'L') })
            {
                int j = lay.plan.FindIndex(p => p.id == pid); int k = j >= 0 ? lay.planRun[j] : -1;
                if (k < 0) continue;
                double planInset = lay.width / 2 - Math.Abs((0.5 - lay.runs[k].u) * lay.width);
                var b3 = new RunBuilder("A3", pid);
                for (int b = 0; b < nb; b++)
                {
                    double s = BinS(b); var p = e.PointAt((float)s); int sp = SpanAt(s);
                    double ln = G[k][b], rb = side == 'L' ? GL[b] : GR[b];
                    if (Cropped(sp, side) || double.IsNaN(ln) || double.IsNaN(rb)) { b3.Push(p.x, 0, p.y, e.index, s, 0, false); continue; }
                    double d = Math.Abs(rb - ln) - planInset;
                    b3.Push(p.x, 0, p.y, e.index, s, d, Math.Abs(d) > V, ' ', sp);
                }
                b3.Close();
            }
            // ---- A5b MISSING (report-only until WP-11b)
            for (int j = 0; j < lay.plan.Count; j++)
            {
                var pj = lay.plan[j]; var b5 = new RunBuilder("A5b", pj.id);
                for (int b = 0; b < nb; b++)
                {
                    double s = BinS(b); var p = e.PointAt((float)s); double hw = HwD(e, s);
                    if (!PlanExists(pj, hw)) { b5.Push(p.x, 0, p.y, e.index, s, 0, false); continue; }
                    double near = double.MaxValue;
                    for (int k = 0; k < lay.runs.Count; k++)
                    {
                        if (lay.runs[k].col != pj.col || double.IsNaN(G[k][b])) continue;
                        near = Math.Min(near, Math.Abs(G[k][b] - (PlanOff(pj, e, s, hw) + lay.runQ[k])));
                    }
                    b5.Push(p.x, 0, p.y, e.index, s, Math.Min(near, 10), near > SmoothRules.StrayM);
                }
                b5.Close();
            }
            // ---- C1 GAP: a solid line interrupted inside the edge where the plan has it
            for (int k = 0; k < lay.runs.Count; k++)
            {
                int j = lay.runPlan[k];
                if (lay.runs[k].dashed || j < 0) continue;
                var ps = new List<List<Pt>>(ed.lines[k]);
                ps.Sort((a, b) => Math.Min(a[0].s, a[a.Count - 1].s).CompareTo(Math.Min(b[0].s, b[b.Count - 1].s)));
                for (int i = 1; i < ps.Count; i++)
                {
                    Pt a = ps[i - 1][0], b = ps[i][0];
                    foreach (var p in ps[i - 1]) if (p.s > a.s) a = p;
                    foreach (var p in ps[i]) if (p.s < b.s) b = p;
                    double gap = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z)), sm = (a.s + b.s) / 2;
                    if (gap > SmoothRules.GapM && PlanExists(lay.plan[j], HwD(e, sm)))
                        Keep(new Run { check = "C1", lineId = lay.plan[j].id, e = e.index, s = sm, x = (a.x + b.x) / 2, y = (a.y + b.y) / 2, z = (a.z + b.z) / 2, val = gap, len = gap, e0 = e.index, s0 = a.s, e1 = e.index, s1 = b.s, span = a.span });
                }
            }
        }

        // ---- the B-family on one strand
        static void ShapeChecks(string kind, string lineId, List<Pt> pts, double rLimit)
        {
            if (pts.Count < 3) return;
            // drop collinear vertices and duplicates
            var keep = new List<int> { 0 };
            double lim = SmoothRules.CollinearDeg / Deg;
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var a = pts[keep[keep.Count - 1]]; var b = pts[i]; var c = pts[i + 1];
                if (Dist(a, b) < 1e-3 || Dist(b, c) < 1e-3) continue;
                if (Math.Abs(Turn(a.x, a.z, b.x, b.z, c.x, c.z)) < lim) continue;
                keep.Add(i);
            }
            keep.Add(pts.Count - 1);
            if (keep.Count < 3) return;
            var C = new double[keep.Count];
            for (int m = 1; m < keep.Count; m++) C[m] = C[m - 1] + Dist(pts[keep[m - 1]], pts[keep[m]]);
            double Ltot = C[C.Length - 1];
            (double x, double z, int i) At(double a, int hint)
            {
                int i = Math.Max(0, Math.Min(hint, keep.Count - 2));
                while (i > 0 && C[i] > a) i--;
                while (i + 2 < keep.Count && C[i + 1] < a) i++;
                var p = pts[keep[i]]; var q = pts[keep[i + 1]]; double L = C[i + 1] - C[i];
                double t = L > 1e-9 ? Math.Max(0, Math.Min(1, (a - C[i]) / L)) : 0;
                return (p.x + (q.x - p.x) * t, p.z + (q.z - p.z) * t, i);
            }
            // B2 KINK
            var b2 = new RunBuilder("B2", lineId, kind);
            for (int m = 1; m + 1 < keep.Count; m++)
            {
                var a = pts[keep[m - 1]]; var b = pts[keep[m]]; var c = pts[keep[m + 1]];
                if (b.x3 || b.gore) { b2.Push(b.x, b.y, b.z, b.e, b.s, 0, false); continue; }
                double th = Turn(a.x, a.z, b.x, b.z, c.x, c.z);
                double f = Math.Min(Math.Min(C[m] - C[m - 1], C[m + 1] - C[m]), SmoothRules.ChordCapM) * Math.Abs(th) / 8;
                b2.Push(b.x, b.y, b.z, b.e, b.s, f, f > V, b.tag, b.span, b.side);
            }
            b2.Close();
            // B3 CURVE (ribbon edges, midline, fans)
            if (rLimit > 0)
            {
                var b3 = new RunBuilder("B3", lineId, kind, null, true) { rLimit = rLimit };
                for (int m = 1; m + 1 < keep.Count; m++)
                {
                    var b = pts[keep[m]];
                    if (C[m] < SmoothRules.CurveHalfM || Ltot - C[m] < SmoothRules.CurveHalfM || b.x3 || b.gore) { b3.Push(b.x, b.y, b.z, b.e, b.s, 1e9, false); continue; }
                    var p0 = At(C[m] - SmoothRules.CurveHalfM, m); var p1 = At(C[m] + SmoothRules.CurveHalfM, m);
                    double h1 = Math.Atan2(b.z - p0.z, b.x - p0.x), h2 = Math.Atan2(p1.z - b.z, p1.x - b.x), dpsi = Math.Abs(h2 - h1);
                    if (dpsi > Math.PI) dpsi = 2 * Math.PI - dpsi;
                    double Rr = dpsi > 1e-9 ? SmoothRules.CurveHalfM / dpsi : 1e9;
                    b3.Push(b.x, b.y, b.z, b.e, b.s, Rr, Rr < rLimit, b.tag, b.span, b.side);
                }
                b3.Close();
            }
            // B1 JITTER: 0.25 m samples within JitterHalfM of a kept vertex; Kasa (line fallback) over +-JitterHalfM
            double step = SmoothRules.JitterStepM, H = SmoothRules.JitterHalfM; int nH = (int)Math.Round(H / step);
            var cand = new List<double>();
            for (int m = 1; m + 1 < keep.Count; m++)
                for (double a = Math.Ceiling((C[m] - H) / step) * step; a <= C[m] + H + 1e-9; a += step)
                    if (a >= H && a <= Ltot - H) cand.Add(a);
            if (cand.Count == 0) return;
            cand.Sort();
            var b1 = new RunBuilder("B1", lineId, kind);
            var xs = new double[2 * nH + 1]; var zs = new double[2 * nH + 1];
            double last = -1; int hint = 0, wlo = 0;
            foreach (double a in cand)
            {
                if (Math.Abs(a - last) < 1e-6) continue;
                if (last >= 0 && a - last > step * 1.5) b1.Close();
                last = a;
                bool exempt = false;
                for (int j = -nH; j <= nH; j++)
                {
                    var p = At(a + j * step, hint);
                    if (j == -nH) hint = p.i;
                    xs[j + nH] = p.x; zs[j + nH] = p.z;
                    var src0 = pts[keep[p.i]];
                    if (src0.x3 || src0.gore) exempt = true;
                }
                var cen = At(a, hint); var src = pts[keep[cen.i]];
                if (exempt) { b1.Push(cen.x, src.y, cen.z, src.e, src.s, 0, false); continue; }
                double cxx = xs[2 * nH] - xs[0], czz = zs[2 * nH] - zs[0], cl = Math.Sqrt(cxx * cxx + czz * czz); if (cl < 1e-9) cl = 1;
                double dev = 0;
                while (wlo < keep.Count && C[wlo] <= a - H) wlo++;
                for (int m = wlo; m < keep.Count && C[m] < a + H; m++)
                {
                    var p = pts[keep[m]];
                    dev = Math.Max(dev, Math.Abs(((p.x - xs[0]) * czz - (p.z - zs[0]) * cxx) / cl));
                }
                double res = dev < V / 2 ? 0 : KasaResidual(xs, zs, nH);
                b1.Push(cen.x, src.y, cen.z, src.e, src.s, res, res > V, src.tag, src.span, src.side);
            }
            b1.Close();
        }
        static double Dist(Pt a, Pt b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        /// <summary>B1's local model: the Kasa circle, or the least-squares line
        /// when the line fits the window at least as well (Kasa's algebraic fit
        /// leans towards small circles on short, nearly straight windows).</summary>
        static double KasaResidual(double[] xs, double[] zs, int c)
        {
            int n = xs.Length; double ox = xs[c], oz = zs[c];
            double Sx = 0, Sz = 0, Sxx = 0, Szz = 0, Sxz = 0, Sxr = 0, Szr = 0, Sr = 0;
            for (int i = 0; i < n; i++)
            {
                double x = xs[i] - ox, z = zs[i] - oz, r = x * x + z * z;
                Sx += x; Sz += z; Sxx += x * x; Szz += z * z; Sxz += x * z; Sxr += x * r; Szr += z * r; Sr += r;
            }
            double mx = Sx / n, mz = Sz / n, cxx = Sxx / n - mx * mx, czz = Szz / n - mz * mz, cxz = Sxz / n - mx * mz;
            double th = 0.5 * Math.Atan2(2 * cxz, cxx - czz), nx = -Math.Sin(th), nz = Math.Cos(th);
            double lineSS = 0;
            for (int i = 0; i < n; i++) { double d = (xs[i] - ox - mx) * nx + (zs[i] - oz - mz) * nz; lineSS += d * d; }
            double lineRes = Math.Abs(-mx * nx - mz * nz);
            double Det3(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2) =>
                a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);
            double d3 = Det3(Sxx, Sxz, Sx, Sxz, Szz, Sz, Sx, Sz, n);
            if (Math.Abs(d3) < 1e-30) return lineRes;
            double D = Det3(-Sxr, Sxz, Sx, -Szr, Szz, Sz, -Sr, Sz, n) / d3;
            double E = Det3(Sxx, -Sxr, Sx, Sxz, -Szr, Sz, Sx, -Sr, n) / d3;
            double F = Det3(Sxx, Sxz, -Sxr, Sxz, Szz, -Szr, Sx, Sz, -Sr) / d3;
            double ccx = -D / 2, ccz = -E / 2, r2 = ccx * ccx + ccz * ccz - F;
            if (!(r2 > 0) || double.IsInfinity(r2)) return lineRes;
            double rr = Math.Sqrt(r2), circSS = 0;
            for (int i = 0; i < n; i++) { double g = Math.Sqrt((xs[i] - ox - ccx) * (xs[i] - ox - ccx) + (zs[i] - oz - ccz) * (zs[i] - oz - ccz)) - rr; circSS += g * g; }
            if (circSS >= lineSS) return lineRes;
            return Math.Abs(Math.Sqrt(ccx * ccx + ccz * ccz) - rr);
        }

        // ---- C3 DASH along one identity chain of a dashed line
        static void DashChecks(string lineId, List<List<Pt>> pieces, PaintRun run)
        {
            double lo = SmoothRules.DashM * (1 - SmoothRules.DashTol), hi = SmoothRules.DashM * (1 + SmoothRules.DashTol);
            double glo = SmoothRules.DashGapM * (1 - SmoothRules.DashTol), ghi = SmoothRules.DashGapM * (1 + SmoothRules.DashTol);
            bool OnAt(double v) { double f = v - Math.Floor(v); return f >= run.vOn0 && f < run.vOn1; }
            int state = -1; double len = 0; bool truncatedStart = true; Pt start = default;
            void CloseRun(Pt endPt, bool truncatedEnd)
            {
                if (state < 0) return;
                bool on = state == 1;
                double l0 = on ? lo : glo, l1 = on ? hi : ghi;
                if (!truncatedStart && !truncatedEnd && (len < l0 || len > l1))
                    Keep(new Run { check = "C3", lineId = lineId, e = endPt.e, s = endPt.s, x = endPt.x, y = endPt.y, z = endPt.z, val = len / (on ? SmoothRules.DashM : SmoothRules.DashGapM) - 1, len = len,
                                   e0 = start.e, s0 = start.s, e1 = endPt.e, s1 = endPt.s, what = on ? "dash" : "gap", span = endPt.span });
                else if (on && (truncatedStart || truncatedEnd) && len < SmoothRules.StubM)
                    Keep(new Run { check = "C3", lineId = lineId, e = endPt.e, s = endPt.s, x = endPt.x, y = endPt.y, z = endPt.z, val = len / SmoothRules.DashM - 1, len = len,
                                   e0 = start.e, s0 = start.s, e1 = endPt.e, s1 = endPt.s, what = "stub", reportOnly = "a truncated dash under 1 m at a mouth or gore: report-only until WP-17", span = endPt.span });
            }
            var ts = new List<double>(8);
            foreach (var pts in pieces)
                for (int i = 1; i < pts.Count; i++)
                {
                    var a = pts[i - 1]; var b = pts[i];
                    double segL = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                    if (segL < 1e-9) continue;
                    double v0 = a.v, v1 = b.v, vmin = Math.Min(v0, v1), vmax = Math.Max(v0, v1);
                    ts.Clear(); ts.Add(0);
                    for (double nn = Math.Floor(vmin) - 1; nn <= Math.Ceiling(vmax) + 1; nn++)
                        foreach (double edgeV in new[] { nn + run.vOn0, nn + run.vOn1 }) if (edgeV > vmin && edgeV < vmax) ts.Add((edgeV - v0) / (v1 - v0));
                    ts.Add(1); ts.Sort();
                    for (int k = 1; k < ts.Count; k++)
                    {
                        double tm = (ts[k - 1] + ts[k]) / 2, dl = segL * (ts[k] - ts[k - 1]);
                        if (dl < 1e-9) continue;
                        int on = OnAt(v0 + (v1 - v0) * tm) ? 1 : 0;
                        if (state != on)
                        {
                            var at = new Pt { x = a.x + (b.x - a.x) * ts[k - 1], y = a.y + (b.y - a.y) * ts[k - 1], z = a.z + (b.z - a.z) * ts[k - 1], e = a.e, s = a.s + (b.s - a.s) * ts[k - 1], span = b.span };
                            CloseRun(at, false);
                            truncatedStart = state < 0; state = on; len = 0; start = at;
                        }
                        len += dl;
                    }
                }
            var lastPiece = pieces[pieces.Count - 1];
            CloseRun(lastPiece[lastPiece.Count - 1], true);
        }

        // ---- one chain: A per edge, then strands (B), ends (C2), dashes (C3), joints (B4)
        sealed class PieceRef { public int c; public EdgeData ed; public int k; public List<Pt> pts; public char col; public bool dashed; public string id; public bool startsAtJoint, endsAtJoint, joined; public PieceRef next, prev; }

        static void AnalyzeChain(List<(CityMap.Edge e, bool fwd)> chain, Dictionary<int, List<Quad>> byEdge, PaveGrid grid)
        {
            var chainSet = new HashSet<int>();
            foreach (var c in chain) chainSet.Add(c.e.index);
            var eds = new List<EdgeData>();
            foreach (var c in chain) { var ed = Extract(c.e, byEdge[c.e.index]); ed.fwd = c.fwd; eds.Add(ed); }
            foreach (var ed in eds) CheckEdge(ed, chainSet, grid);
            List<Pt> Orient(EdgeData ed, List<Pt> p) { if (ed.fwd) return p; var r = new List<Pt>(p); r.Reverse(); return r; }
            int NodeAtStart(EdgeData ed) => ed.fwd ? ed.e.a : ed.e.b;
            // gore noses (X2): within GoreNoseM of a collapsed section
            foreach (var ed in eds)
                foreach (var side in new[] { ed.ribL, ed.ribR })
                    foreach (var pc in side)
                        for (int i = 0; i < pc.Count; i++)
                        {
                            var p = pc[i];
                            foreach (var q in ed.quads)
                                if (((q.fA & FCollapsed) != 0 && Math.Abs(q.sA - p.s) <= SmoothRules.GoreNoseM) || ((q.fB & FCollapsed) != 0 && Math.Abs(q.sB - p.s) <= SmoothRules.GoreNoseM))
                                { p.gore = true; pc[i] = p; break; }
                        }
            // ---- ribbon edges and the midline: join across joints, else a JUMP and a new strand
            void JoinStrands(Func<EdgeData, List<List<Pt>>> get, string kindName, Func<EdgeData, double> rLim)
            {
                List<Pt> open = null; string openId = null; double openR = 0;
                void Finish() { if (open != null) { strands++; ShapeChecks(kindName, openId, open, openR); } open = null; }
                for (int c = 0; c < eds.Count; c++)
                {
                    var ed = eds[c];
                    var pcs = new List<List<Pt>>();
                    foreach (var p in get(ed)) pcs.Add(Orient(ed, p));
                    if (!ed.fwd) pcs.Reverse();
                    // the node ends of the ribbon (mitred: no trim); a ring that stops short of them joins nothing
                    double startS = ed.fwd ? 0 : ed.e.length, endS = ed.fwd ? ed.e.length : 0;
                    for (int i = 0; i < pcs.Count; i++)
                    {
                        var pc = pcs[i];
                        if (i == 0 && open != null && Math.Abs(pc[0].s - startS) < 1e-3)
                        {
                            var a = open[open.Count - 1]; var b = pc[0]; double d = Dist(a, b);
                            if (d <= SmoothRules.JoinM) { open.AddRange(pc.GetRange(1, pc.Count - 1)); continue; }
                            if (d > V) Keep(new Run { check = "B4", lineId = kindName == "midline" ? "MID" : "R" + b.side, kind = kindName, e = b.e, s = b.s, x = b.x, y = b.y, z = b.z, val = d, e0 = a.e, s0 = a.s, e1 = b.e, s1 = b.s, node = NodeAtStart(ed), side = b.side });
                        }
                        Finish();
                        open = new List<Pt>(pc); openId = kindName == "midline" ? "MID" : "R" + pc[0].side; openR = rLim(ed);
                    }
                    if (open != null && Math.Abs(open[open.Count - 1].s - endS) > 1e-3) Finish();
                    if (pcs.Count == 0) Finish();
                }
                Finish();
            }
            JoinStrands(ed => ed.fwd ? ed.ribL : ed.ribR, "edge", ed => SmoothRules.InnerEdgeMinRM);
            JoinStrands(ed => ed.fwd ? ed.ribR : ed.ribL, "edge", ed => SmoothRules.InnerEdgeMinRM);
            JoinStrands(ed => ed.mid, "midline", ed => SmoothRules.RMinFor(SmoothRules.ClassOf(ed.e.cls, ed.e.link)));

            // ---- painted lines: link pieces across joints
            var P = new List<PieceRef>();
            for (int c = 0; c < eds.Count; c++)
            {
                var ed = eds[c]; int nq = ed.quads.Count;
                for (int k = 0; k < ed.lay.runs.Count; k++)
                    foreach (var pc in ed.lines[k])
                    {
                        var pts = Orient(ed, pc);
                        Pt f0 = pts[0], f1 = pts[pts.Count - 1];
                        bool first0 = ed.quads[0].sA < 1e-3f, lastL = Math.Abs(ed.quads[nq - 1].sB - ed.e.length) < 1e-3f;
                        bool AtStart(Pt p) => ed.fwd ? p.tag == 'A' && p.span == 0 && first0 : p.tag == 'B' && p.span == nq - 1 && lastL;
                        bool AtEnd(Pt p) => ed.fwd ? p.tag == 'B' && p.span == nq - 1 && lastL : p.tag == 'A' && p.span == 0 && first0;
                        P.Add(new PieceRef { c = c, ed = ed, k = k, pts = pts, col = ed.lay.runs[k].col, dashed = ed.lay.runs[k].dashed, id = PlanIdOf(ed.lay, k),
                                             startsAtJoint = c > 0 && AtStart(f0), endsAtJoint = c + 1 < eds.Count && AtEnd(f1) });
                    }
            }
            for (int c = 0; c + 1 < eds.Count; c++)
            {
                var pairs = new List<(PieceRef a, PieceRef b, double d)>();
                foreach (var a in P) if (a.c == c && a.endsAtJoint)
                        foreach (var b in P) if (b.c == c + 1 && b.startsAtJoint && a.col == b.col && a.dashed == b.dashed)
                            {
                                double d = Dist(a.pts[a.pts.Count - 1], b.pts[0]);
                                if (d <= SmoothRules.MatchM) pairs.Add((a, b, d));
                            }
                pairs.Sort((p, q) => p.d.CompareTo(q.d));
                foreach (var (a, b, d) in pairs)
                {
                    if (a.next != null || b.prev != null) continue;
                    a.next = b; b.prev = a;
                    if (d <= SmoothRules.JoinM) b.joined = true;
                    else if (d > V)
                    {
                        var pb = b.pts[0]; var pa = a.pts[a.pts.Count - 1];
                        Keep(new Run { check = "B4", lineId = b.id, kind = "paint", e = pb.e, s = pb.s, x = pb.x, y = pb.y, z = pb.z, val = d, e0 = pa.e, s0 = pa.s, e1 = pb.e, s1 = pb.s, node = NodeAtStart(b.ed) });
                    }
                }
            }
            foreach (var head in P)
            {
                if (head.prev != null) continue;
                var seq = new List<PieceRef>();
                for (var p = head; p != null; p = p.next) seq.Add(p);
                List<Pt> strand = null;
                foreach (var p in seq)
                {
                    if (strand != null && p.joined) strand.AddRange(p.pts.GetRange(1, p.pts.Count - 1));
                    else { if (strand != null) { strands++; ShapeChecks("paint", head.id, strand, 0); } strand = new List<Pt>(p.pts); }
                }
                if (strand != null) { strands++; ShapeChecks("paint", head.id, strand, 0); }
                EndChecks(seq[0], true); EndChecks(seq[seq.Count - 1], false);
                if (head.dashed)
                {
                    var pcs = new List<List<Pt>>(); foreach (var p in seq) pcs.Add(p.pts);
                    DashChecks(head.id, pcs, head.ed.lay.runs[head.k]);
                }
            }
        }

        /// <summary>C2: a line may start or end only at a fan mouth, a dead
        /// end, a branch mouth or gore, or where the plan drops or adds it - and
        /// even there not off its plan by more than StrayM.</summary>
        static void EndChecks(PieceRef p, bool start)
        {
            var pt = start ? p.pts[0] : p.pts[p.pts.Count - 1];
            var ed = p.ed; var e = ed.e; var lay = ed.lay;
            // the edge's own ribbon extent (its trims), not the part of it this ring holds
            double sMin = trims.atA[e.index], sMax = e.length - trims.atB[e.index];
            int node = Math.Abs(pt.s - sMin) <= SmoothRules.FanMouthM ? e.a : Math.Abs(pt.s - sMax) <= SmoothRules.FanMouthM ? e.b : -1;
            // an end at the ring's edge is no end: the next tile draws on
            if (node < 0 ? !NearCentre(pt.x, pt.z, 0) : false) return;
            string legit = null;
            if (node >= 0 && trims.patch[node]) legit = "fan mouth";
            else if (node >= 0 && map.nodeEdges[node].Count == 1) legit = "dead end";
            else if (node >= 0 && ((trims.branchA[e.index] >= 0 && node == e.a) || (trims.branchB[e.index] >= 0 && node == e.b))) legit = "branch mouth";
            else
                foreach (var q in ed.quads)
                    if ((((q.fA | q.fB) & (FClipL | FClipR | FCollapsed)) != 0) && (Math.Abs(q.sA - pt.s) <= SmoothRules.GoreNoseM || Math.Abs(q.sB - pt.s) <= SmoothRules.GoreNoseM))
                    { legit = "gore"; break; }
            int j = lay.runPlan[p.k]; var plan = j >= 0 ? lay.plan[j] : null;
            if (legit == null && plan != null)
            {
                double s0 = Math.Max(sMin, pt.s - SmoothRules.FanMouthM), s1 = Math.Min(sMax, pt.s + SmoothRules.FanMouthM);
                if (PlanExists(plan, HwD(e, s0)) != PlanExists(plan, HwD(e, s1))) legit = "plan lane drop";
                else if (node >= 0 && JointAt(e, node) >= 0)
                {
                    var o = map.edges[JointAt(e, node)];
                    var op = PlanOf(RoadProfiles.All[o.profile]);
                    double lat = Sd(RefOf(e), pt.x, pt.z, pt.s);
                    bool same = (e.b == node) == (o.a == node);
                    double so = o.a == node ? 0 : o.length, hwo = HwD(o, so);
                    bool has = false;
                    foreach (var qq in op) if (qq.col == p.col && qq.dashed == p.dashed && Math.Abs((same ? 1 : -1) * PlanOff(qq, o, so, hwo) - lat) <= SmoothRules.MatchM) { has = true; break; }
                    if (!has) legit = "plan lane drop at a node";
                }
            }
            double err = plan != null ? Sd(RefOf(e), pt.x, pt.z, pt.s) - (PlanOff(plan, e, pt.s, HwD(e, pt.s)) + lay.runQ[p.k]) : 0;
            if (legit == null || Math.Abs(err) > SmoothRules.StrayM)
                Keep(new Run { check = "C2", lineId = p.id, e = e.index, s = pt.s, x = pt.x, y = pt.y, z = pt.z, val = Math.Max(1, Math.Abs(err) / SmoothRules.StrayM),
                               e0 = e.index, s0 = pt.s, e1 = e.index, s1 = pt.s, span = pt.span,
                               what = (start ? "start " : "end ") + (legit != null ? $"at a {legit} but {Math.Abs(err):0.00} m off its plan" : "mid-road") });
        }

        /// <summary>B2 / B3 on a fan's curb returns: each run of perimeter
        /// chords between two road mouths (ratchet until WP-19's arcs).</summary>
        static void FanChecks(FanRec f)
        {
            int n = f.corners.Length;
            if (n < 3) return;
            // the junction class pair, for the curb-return radius
            int arterial = 0, local = 0; bool link = false, nonLink = false;
            foreach (var ei in map.nodeEdges[f.node]) { var e = map.edges[ei]; if (e.link) link = true; else nonLink = true; if (e.cls >= 2) arterial++; else local++; }
            string pair = link && nonLink ? "ramp_terminal" : arterial >= 2 ? "arterial_x_arterial" : arterial == 1 ? "arterial_x_local" : "local_x_local";
            double rLim = SmoothRules.CurbReturnShare * SmoothRules.CurbReturnFor(pair);
            int eAny = map.nodeEdges[f.node].Count > 0 ? map.nodeEdges[f.node][0] : 0;
            // start after a mouth so every curb return is one polyline
            int s0 = -1;
            for (int i = 0; i < n; i++) if ((f.mouths >> i & 1UL) != 0) { s0 = (i + 1) % n; break; }
            if (s0 < 0) s0 = 0;
            var cur = new List<Pt>();
            for (int k = 0; k <= n; k++)
            {
                int i = (s0 + k) % n;
                var c = f.corners[i];
                cur.Add(new Pt { x = c.x, y = c.y, z = c.z, e = eAny, s = 0, tag = 'F' });
                bool mouthNext = (f.mouths >> i & 1UL) != 0;
                if (mouthNext || k == n)
                {
                    if (cur.Count >= 3) { strands++; ShapeChecks("fan", "FAN", cur, rLim); }
                    cur = new List<Pt>();
                }
            }
            foreach (var r in runs) if (r.kind == "fan" && r.node < 0) r.node = f.node;
        }

        // ================================================================
        //  Keys, causes, ranking, report
        // ================================================================

        static readonly string[] CheckOrder = { "A1", "A2", "A3", "A4", "A5", "A5b", "B1", "B2", "B3", "B4", "B4s", "C1", "C2", "C3", "D1", "E1" };

        static void FinishRuns()
        {
            foreach (var r in runs)
            {
                var e = map.edges[r.e];
                if (r.kind == "edge" && r.side != ' ' && r.side != '\0') r.lineId = "R" + r.side;
                r.pinned = pinnedWays.Contains(e.wayId);
                r.key = r.kind == "fan" ? $"{e.wayId}:n{r.node}:{r.check}:FAN" : $"{e.wayId}:{Math.Round((wayOff[e.index] + r.s) / SmoothRules.KeyStepM).ToString(Inv)}:{r.check}:{r.lineId}";
                double limit = r.check == "A4" ? SmoothRules.LineWidthTol : r.check == "A5" || r.check == "A5b" ? SmoothRules.StrayM : r.check == "B3" ? r.rLimit
                    : r.check == "C1" ? SmoothRules.GapM : r.check == "C2" ? 1 : r.check == "C3" ? SmoothRules.DashTol : r.check == "D1" ? SmoothRules.CrossM
                    : r.check == "B4s" ? SmoothRules.SeamM : V;
                r.ratio = r.check == "B3" ? limit / Math.Max(1e-6, r.val) : r.check == "C2" ? r.val : Math.Abs(r.val) / limit;
                // causes: the tap's flags and the data near the worst sample
                var causes = new List<string>();
                double lo = r.check == "C1" ? Math.Min(r.s0, r.s1) - 1 : r.s - 1, hi = r.check == "C1" ? Math.Max(r.s0, r.s1) + 1 : r.s + 1;
                bool taper = (trims.taperA[e.index] > 0 && r.s <= trims.taperA[e.index] + 1) || (trims.taperB[e.index] > 0 && e.length - r.s <= trims.taperB[e.index] + 1);
                if (taper) causes.Add("TAPER");
                if (r.tag == 'D' || (taper && r.tag == 'I')) causes.Add("DIAGONAL");
                for (int i = 1; i + 1 < e.s.Length; i++) if (e.s[i] >= lo && e.s[i] <= hi) { causes.Add("VERTEX"); break; }
                if (r.node >= 0 || ((r.s <= 1 || e.length - r.s <= 1) && (trims.mitre[e.a] || trims.mitre[e.b]))) causes.Add(r.kind == "fan" ? "FAN" : "MITRE");
                if (r.check == "B4s") causes.Add("SEAM");
                r.cause = causes.Count > 0 ? string.Join("+", causes) : "-";
                r.data = r.kind == "fan" ? "BUILDER" : DataKinkNear(e, r.s) ? "DATA" : "BUILDER";
                r.exposure = 1;
                if (onRoute.Contains(e.index)) r.exposure = SmoothRules.ExposureRoute;
                else foreach (var sp in refSpots) if ((sp.x - r.x) * (sp.x - r.x) + (sp.y - r.z) * (sp.y - r.z) <= SmoothRules.RefSpotReachM * SmoothRules.RefSpotReachM) { r.exposure = SmoothRules.ExposureRefSpot; break; }
                r.score = Math.Min(r.ratio, SmoothRules.RankRatioCap) * SmoothRules.WeightFor(SmoothRules.ClassOf(e.cls, e.link)) * r.exposure;
            }
            // A5 / A5b are runs of at least StrayRunM
            runs.RemoveAll(r => (r.check == "A5" || r.check == "A5b") && r.len < SmoothRules.StrayRunM);
        }

        sealed class Tally { public int runs, data, builder, roRuns; public double metres, worst, worstRatio, roMetres; public readonly HashSet<string> keys = new HashSet<string>(); }

        static Dictionary<string, Tally> Summarize()
        {
            var t = new Dictionary<string, Tally>();
            foreach (var id in CheckOrder) t[id] = new Tally();
            foreach (var r in runs)
            {
                if (!t.TryGetValue(r.check, out var s)) continue;
                if (r.reportOnly != null) { s.roRuns++; s.roMetres += r.len; continue; }
                s.runs++; s.metres += r.len; s.keys.Add(r.key);
                if (r.ratio > s.worstRatio) { s.worstRatio = r.ratio; s.worst = r.val; }
                if (r.data == "DATA") s.data++; else s.builder++;
            }
            return t;
        }

        static string Unit(string id, double v) =>
            id == "A4" || id == "C3" ? $"{v * 100:0}%" : id == "B3" ? $"R {v:0.0} m" : id == "C2" ? $"{v:0.0}x" :
            Math.Abs(v) >= 1 ? $"{Math.Abs(v):0.00} m" : $"{Math.Abs(v) * 100:0.0} cm";

        static string Describe(string id)
        {
            if (id == "EL") return "edge line, left of travel";
            if (id == "ER") return "edge line, right of travel";
            if (id == "RL") return "ribbon edge, right of travel";
            if (id == "RR") return "ribbon edge, left of travel";
            if (id == "MID") return "ribbon midline";
            if (id == "CPAIR") return "centre pair (double yellow / TWLTL)";
            if (id == "FAN") return "fan perimeter (curb return)";
            if (id.Length > 3 && id[0] == 'C') return (id[1] == 'Y' ? "yellow centre" : "white lane") + $" line ({(id[2] == 'd' ? "dash" : "solid")}, {id.Substring(3)} m)";
            return id;
        }

        /// <summary>The report, the CSV and the JSON at the project root; the
        /// baseline compare; the Check lines (report-only in the first cycle).</summary>
        static bool Report(Action<string> line, Action<bool, string, object> check)
        {
            FinishRuns();
            var sum = Summarize();
            string root = Directory.GetParent(Application.dataPath).FullName;
            uint graph = map.graphHash;
            var baseline = ReadBaseline(out string basePath);
            BaselineEntry baseE = null;
            if (baseline != null) baseline.TryGetValue($"{graph:x8}:mesh", out baseE);
            var sb = new StringBuilder();
            sb.AppendLine($"SMOOTHNESS GATE  V {V * 100:0.0} cm (1 px at {SmoothRules.PixelAtM:0} m; {SmoothRules.FramebufferLines} lines, fov {SmoothRules.FovDeg:0})  mode {mode}{(band >= 0 ? $" band {band}/{SmoothRules.BandCount}" : "")}  graph {graph:x8}");
            sb.AppendLine($"  {analysed.Count} tiles, {ribbonKm:0} km of ribbon, {lineKm:0} km of line, {strands} strands, {Time.realtimeSinceStartup - t0:0.0} s{(tapNotes > 0 ? $"; {tapNotes} tap records unreadable" : "")}");
            sb.AppendLine($"  {(SmoothRules.ReportOnly ? "REPORT-ONLY cycle (SmoothRules.ReportOnly)" : "gating")}; creek pin {(SmoothRules.PinActive ? "ACTIVE" : "recorded, not yet enforced")}; baseline {(baseE != null ? basePath : "none for this graph")}");
            sb.AppendLine("  check          state    runs     metres   worst        x limit  data/builder   baseline");
            bool allOk = true;
            foreach (var c in SmoothRules.Checks)
            {
                var s = sum[c.id];
                BaselineCheck b = null; baseE?.checks.TryGetValue(c.id, out b);
                int fresh = 0;
                if (b != null && b.keys != null) foreach (var k in s.keys) if (!b.keys.Contains(Fnv1a(k))) fresh++;
                bool ok = c.state == SmoothRules.State.Report ? true
                        : c.state == SmoothRules.State.Zero ? s.runs == 0
                        : b != null && fresh == 0 && s.runs <= b.runs && s.worstRatio <= b.worstRatio + 1e-3;
                if (!ok) allOk = false;
                string bl = b != null ? $"{b.runs} runs, worst x{b.worstRatio:0.0}, {fresh} new keys" : "-";
                sb.AppendLine($"  {(c.id + " " + c.name).PadRight(14)} {c.state.ToString().ToUpperInvariant().PadRight(8)} {s.runs,6} {s.metres,10:0}   {Unit(c.id, s.worst).PadRight(12)} {s.worstRatio,6:0.0}   {(s.data + "/" + s.builder).PadRight(14)} {bl}");
                if (s.roRuns > 0) sb.AppendLine($"  {"",-14} {"REPORT",-8} {s.roRuns,6} {s.roMetres,10:0}   {(c.id == "D1" ? "inside BranchSeats attach arcs (report-only until WP-18b)" : "truncated dash stubs at mouths and gores (report-only until WP-17)")}");
                string what = $"smoothness {c.id} {c.name}: {c.what}";
                string detail = $"{s.runs} runs, worst {Unit(c.id, s.worst)} (x{s.worstRatio:0.0}){(b != null ? $" (baseline {b.runs}, {c.state.ToString().ToUpperInvariant()}: {fresh} new keys)" : c.state == SmoothRules.State.Zero ? " (ZERO)" : " (no baseline)")}";
                if (SmoothRules.ReportOnly || c.state == SmoothRules.State.Report) line?.Invoke($"  info {what} - {detail}");
                else check?.Invoke(ok, what, detail);
            }
            // the creek pin
            int pinRuns = 0; var pinBy = new SortedDictionary<string, (int n, double worst, double val)>();
            foreach (var r in runs)
            {
                if (!r.pinned || r.reportOnly != null) continue;
                pinRuns++;
                pinBy.TryGetValue(r.check, out var v);
                pinBy[r.check] = (v.n + 1, Math.Max(v.worst, r.ratio), r.ratio > v.worst ? r.val : v.val);
            }
            var pinTxt = new StringBuilder();
            foreach (var kv in pinBy) pinTxt.Append($"{kv.Key} {kv.Value.n} (worst {Unit(kv.Key, kv.Value.val)}), ");
            sb.AppendLine($"PINNED  the creek (ways {string.Join(", ", SmoothRules.PinnedWays)}) - {(SmoothRules.PinActive ? "ACTIVE" : "recorded")}: {(pinRuns == 0 ? "no violations" : pinTxt.ToString().TrimEnd(',', ' '))}");
            if (PinActive && !ReportOnly) check?.Invoke(pinRuns == 0, "smoothness: the creek pin (ways 1078015030, 16671358, 1252904925) at zero", pinRuns + " runs");
            // the worst N, one per (way, check, line) within DedupM
            var ranked = runs.FindAll(r => r.reportOnly == null && SmoothRules.Check(r.check).state != SmoothRules.State.Report);
            ranked.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : b.ratio.CompareTo(a.ratio));
            var worst = new List<Run>(); var kept = new Dictionary<string, List<Run>>();
            foreach (var r in ranked)
            {
                string k = $"{map.edges[r.e].wayId}:{r.check}:{r.lineId}";
                if (!kept.TryGetValue(k, out var l)) kept[k] = l = new List<Run>();
                bool dup = false; foreach (var q in l) if ((q.x - r.x) * (q.x - r.x) + (q.z - r.z) * (q.z - r.z) < SmoothRules.DedupM * SmoothRules.DedupM) { dup = true; break; }
                if (dup) continue;
                l.Add(r); worst.Add(r);
                if (worst.Count >= SmoothRules.WorstN) break;
            }
            sb.AppendLine($"WORST {worst.Count} (ratio capped at {SmoothRules.RankRatioCap:0} x class weight x exposure)");
            for (int i = 0; i < worst.Count; i++)
            {
                var r = worst[i]; var e = map.edges[r.e];
                sb.AppendLine($" #{i + 1,-2} {r.check} {SmoothRules.Check(r.check).name,-9} {Unit(r.check, r.val),-9} ({r.ratio:0.0}x)  {Describe(r.lineId)}  e{r.e} '{e.name}' way {e.wayId}");
                sb.AppendLine($"      {SmoothRules.ClassOf(e.cls, e.link)} {RoadProfiles.All[e.profile].key} {(e.ElevatedAt((float)r.s) ? "deck" : "ground")}  s {r.s:0.0}/{e.length:0.0}  run {r.len:0.0} m  game ({r.x:0.0}, {r.y:0.00}, {r.z:0.0})  {CityAudit.LatLon((float)r.x, (float)r.z)}  tile ({Math.Floor(r.x / CityMeshes.TileSize)},{Math.Floor(r.z / CityMeshes.TileSize)})");
                sb.AppendLine($"      cause {r.cause}; {r.data}{(r.what != null ? "; " + r.what : "")}{(r.exposure > 1 ? $"; exposure {r.exposure:0.0}" : "")}");
                sb.AppendLine($"      spot {r.x.ToString("0.0", Inv)},{r.z.ToString("0.0", Inv)},{r.e},{r.lineId}");
            }
            if (textureNotes.Count > 0) foreach (var n in textureNotes) sb.AppendLine("  TEXTURE " + n);
            File.WriteAllText(Path.Combine(root, "city_smooth.txt"), sb.ToString());
            // every run
            var csv = new StringBuilder("check,lineId,key,way,edge,name,s,s0,s1,len,val,ratio,x,y,z,cause,data,pinned,reportOnly,what\n");
            foreach (var r in runs)
            {
                var e = map.edges[r.e];
                csv.Append(r.check).Append(',').Append(r.lineId).Append(',').Append(r.key).Append(',').Append(e.wayId).Append(',').Append(r.e).Append(",\"")
                   .Append((e.name ?? "").Replace("\"", "\"\"")).Append("\",").Append(F(r.s)).Append(',').Append(F(r.s0)).Append(',').Append(F(r.s1)).Append(',').Append(F(r.len)).Append(',')
                   .Append(F(r.val)).Append(',').Append(F(r.ratio)).Append(',').Append(F(r.x)).Append(',').Append(F(r.y)).Append(',').Append(F(r.z)).Append(',').Append(r.cause).Append(',').Append(r.data)
                   .Append(',').Append(r.pinned ? 1 : 0).Append(",\"").Append(r.reportOnly ?? "").Append("\",\"").Append(r.what ?? "").Append("\"\n");
            }
            File.WriteAllText(Path.Combine(root, "city_smooth.csv"), csv.ToString());
            // the worst N with what SHOTS needs
            var js = new StringBuilder("{\n \"graph\": \"").Append(graph.ToString("x8")).Append("\", \"mode\": \"").Append(mode).Append("\", \"V\": ").Append(F(V)).Append(",\n \"worst\": [\n");
            for (int i = 0; i < worst.Count; i++)
            {
                var r = worst[i]; var e = map.edges[r.e]; var t = e.TangentAt((float)r.s);
                js.Append("  {\"rank\": ").Append(i + 1).Append(", \"check\": \"").Append(r.check).Append("\", \"line\": \"").Append(r.lineId).Append("\", \"edge\": ").Append(r.e)
                  .Append(", \"way\": ").Append(e.wayId).Append(", \"s\": ").Append(F(r.s)).Append(", \"x\": ").Append(F(r.x)).Append(", \"y\": ").Append(F(r.y)).Append(", \"z\": ").Append(F(r.z))
                  .Append(", \"hx\": ").Append(F(t.x)).Append(", \"hz\": ").Append(F(t.y)).Append(", \"val\": ").Append(F(r.val)).Append(", \"ratio\": ").Append(F(r.ratio))
                  .Append(", \"name\": \"").Append((e.name ?? "").Replace("\"", "'")).Append("\"}").Append(i + 1 < worst.Count ? ",\n" : "\n");
            }
            js.Append(" ]\n}\n");
            File.WriteAllText(Path.Combine(root, "city_smooth.json"), js.ToString());
            if (Environment.GetEnvironmentVariable("PSX_SMOOTH_WRITE_BASELINE") == "1") WriteBaseline(sum, graph, Path.Combine(root, "smooth_baseline.json"));
            line?.Invoke($"  smoothness gate: {analysed.Count} tiles, {runs.Count} runs, city_smooth.txt written ({Time.realtimeSinceStartup - t0:0.0} s)");
            Debug.Log("[CitySmooth] " + sb.ToString().Split('\n')[0]);
            return allOk && !(SmoothRules.PinActive && pinRuns > 0);
        }
        static string F(double v) => v.ToString("0.###", Inv);

        // ================================================================
        //  Baseline: per graph hash, per-check numbers and FNV-1a key hashes
        //  (packed as tools/city/linecheck.mjs packs them)
        // ================================================================

        sealed class BaselineCheck { public int runs; public double worstRatio; public HashSet<uint> keys; }
        sealed class BaselineEntry { public readonly Dictionary<string, BaselineCheck> checks = new Dictionary<string, BaselineCheck>(); }

        static uint Fnv1a(string s)
        {
            uint h = 0x811c9dc5;
            foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 0x01000193; }
            return h;
        }

        static string PackKeys(HashSet<string> keys, out int n)
        {
            var hs = new SortedSet<uint>();
            foreach (var k in keys) hs.Add(Fnv1a(k));
            n = hs.Count;
            var raw = new MemoryStream();
            uint prev = 0;
            foreach (var h in hs)
            {
                uint d = h - prev; prev = h;
                do { byte b = (byte)(d & 0x7f); d >>= 7; if (d != 0) b |= 0x80; raw.WriteByte(b); } while (d != 0);
            }
            var outp = new MemoryStream();
            using (var gz = new GZipStream(outp, System.IO.Compression.CompressionLevel.Optimal, true)) { var a = raw.ToArray(); gz.Write(a, 0, a.Length); }
            return Convert.ToBase64String(outp.ToArray());
        }

        static HashSet<uint> UnpackKeys(string b64)
        {
            var set = new HashSet<uint>();
            using (var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress))
            {
                uint prev = 0, v = 0; int shift = 0, c;
                while ((c = gz.ReadByte()) >= 0)
                {
                    v |= (uint)(c & 0x7f) << shift;
                    if ((c & 0x80) != 0) { shift += 7; continue; }
                    prev += v; set.Add(prev); v = 0; shift = 0;
                }
            }
            return set;
        }

        static Dictionary<string, BaselineEntry> ReadBaseline(out string path)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            path = null;
            foreach (var p in new[] { Environment.GetEnvironmentVariable("PSX_SMOOTH_BASELINE"), Path.Combine(root, "tools", "city", "baseline", "smooth_baseline.json"), Path.Combine(root, "smooth_baseline.json") })
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) { path = p; break; }
            if (path == null) return null;
            try
            {
                var J = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
                var outp = new Dictionary<string, BaselineEntry>();
                if (J == null || !(J.TryGetValue("entries", out var eo) && eo is Dictionary<string, object> entries)) return outp;
                foreach (var kv in entries)
                {
                    var be = new BaselineEntry();
                    if (kv.Value is Dictionary<string, object> ent && ent.TryGetValue("checks", out var co) && co is Dictionary<string, object> checks)
                        foreach (var ck in checks)
                            if (ck.Value is Dictionary<string, object> c)
                                be.checks[ck.Key] = new BaselineCheck
                                {
                                    runs = c.TryGetValue("runs", out var r) ? Convert.ToInt32(r, Inv) : 0,
                                    worstRatio = c.TryGetValue("worstRatio", out var w) ? Convert.ToDouble(w, Inv) : 0,
                                    keys = c.TryGetValue("keys_b64", out var kb) && kb is string ks ? UnpackKeys(ks) : null,
                                };
                    outp[kv.Key] = be;
                }
                return outp;
            }
            catch (Exception ex) { Debug.LogWarning("[CitySmooth] baseline unreadable: " + ex.Message); return null; }
        }

        /// <summary>Record this graph's numbers and keys (FULL mode's run is
        /// the one to record; a re-export re-records, with before and after
        /// numbers in the commit).</summary>
        static void WriteBaseline(Dictionary<string, Tally> sum, uint graph, string path)
        {
            var sb = new StringBuilder("{\n \"schema\": 1, \"tool\": \"Editor/CitySmooth.cs\", \"note\": \"per graph hash (':mesh'): per-check runs, metres and worst, and the violation keys (way, round(s on way / 5 m), check, line) as packed FNV-1a hashes\",\n \"entries\": {\n");
            sb.Append("  \"").Append(graph.ToString("x8")).Append(":mesh\": { \"date\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-dd", Inv)).Append("\", \"mode\": \"").Append(mode).Append("\", \"checks\": {\n");
            int i = 0;
            foreach (var kv in sum)
            {
                string b64 = PackKeys(kv.Value.keys, out int n);
                sb.Append("   \"").Append(kv.Key).Append("\": { \"runs\": ").Append(kv.Value.runs).Append(", \"metres\": ").Append(F(kv.Value.metres)).Append(", \"worst\": ").Append(F(kv.Value.worst))
                  .Append(", \"worstRatio\": ").Append(F(kv.Value.worstRatio)).Append(", \"keys\": ").Append(n).Append(", \"keys_b64\": \"").Append(b64).Append("\" }").Append(++i < sum.Count ? ",\n" : "\n");
            }
            sb.Append("  } }\n }\n}\n");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[CitySmooth] wrote the baseline to " + path);
        }

        /// <summary>Just enough JSON for the baseline and city_smooth.json.</summary>
        static class MiniJson
        {
            public static object Parse(string s) { int i = 0; return Value(s, ref i); }
            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++;
                    for (Ws(s, ref i); s[i] != '}';)
                    {
                        string k = (string)Value(s, ref i); Ws(s, ref i); i++;   // ':'
                        d[k] = Value(s, ref i); Ws(s, ref i);
                        if (s[i] == ',') { i++; Ws(s, ref i); }
                    }
                    i++; return d;
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++;
                    for (Ws(s, ref i); s[i] != ']';) { l.Add(Value(s, ref i)); Ws(s, ref i); if (s[i] == ',') { i++; Ws(s, ref i); } }
                    i++; return l;
                }
                if (c == '"')
                {
                    var sb = new StringBuilder(); i++;
                    while (s[i] != '"') { if (s[i] == '\\') { i++; sb.Append(s[i] == 'n' ? '\n' : s[i]); } else sb.Append(s[i]); i++; }
                    i++; return sb.ToString();
                }
                if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
                if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
                if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
                int j = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(j, i - j), Inv);
            }
        }

        // ================================================================
        //  Modes
        // ================================================================

        /// <summary>FAST, step 1 (CityAudit.Run, before its drive and roadside
        /// audits): every tile they build is collected from here on.</summary>
        public static void BeginFast()
        {
            Reset();
            mode = "FAST";
            collecting = true;
            CityMeshes.RecordTap = true;
        }

        /// <summary>FAST, step 2: analyse every collected tile whose ring is
        /// complete (the drive and roadside audits' tiles), the reference
        /// spots' rings and one band of the road tiles; report through the
        /// audit's own Line / Check.</summary>
        public static void EndFast(CityMap m, CityMeshes.Trims t, Dictionary<long, List<CityBuildings.B>> buildings,
                                   Action<string> line, Action<bool, string, object> check)
        {
            if (!collecting) return;
            try
            {
                Setup(m, t);
                var done = new List<long>(frags.Keys);
                foreach (var k in done)
                {
                    var f = frags[k];
                    bool ring = true;
                    for (int dz = -1; dz <= 1 && ring; dz++) for (int dx = -1; dx <= 1 && ring; dx++) if (!frags.ContainsKey(TileKey(f.tx + dx, f.tz + dz))) ring = false;
                    if (ring) Analyze(f.tx, f.tz);
                }
                var spots = new List<(int, int)>();
                foreach (var sp in refSpots) spots.Add((Mathf.FloorToInt(sp.x / CityMeshes.TileSize), Mathf.FloorToInt(sp.y / CityMeshes.TileSize)));
                RasterPass(spots, buildings, false);
                var tiles = RoadTiles();
                band = BandFromEnv();
                int per = (tiles.Count + SmoothRules.BandCount - 1) / SmoothRules.BandCount;
                RasterPass(tiles.GetRange(Math.Min(tiles.Count, band * per), Math.Max(0, Math.Min(per, tiles.Count - band * per))), buildings, true);
                Report(line, check);
            }
            catch (Exception ex)
            {
                check?.Invoke(false, "smoothness gate ran", ex.GetType().Name + ": " + ex.Message);
                Debug.LogException(ex);
            }
            finally { collecting = false; CityMeshes.RecordTap = false; frags.Clear(); refCache.Clear(); }
        }

        static int BandFromEnv()
        {
            var env = Environment.GetEnvironmentVariable("PSX_SMOOTH_BAND");
            if (!string.IsNullOrEmpty(env) && int.TryParse(env, out int k) && k >= 0 && k < SmoothRules.BandCount) return k;
            return DateTime.Now.DayOfYear % SmoothRules.BandCount;
        }

        /// <summary>Every tile a ribbon crosses, in raster order (rows of z).</summary>
        static List<(int tx, int tz)> RoadTiles()
        {
            var seen = new HashSet<long>(); var o = new List<(int, int)>();
            foreach (var e in map.edges)
                for (float s = 0f; ; s += 16f)
                {
                    var p = e.PointAt(Mathf.Min(s, e.length));
                    int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
                    if (seen.Add(TileKey(tx, tz))) o.Add((tx, tz));
                    if (s >= e.length) break;
                }
            o.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item1.CompareTo(b.Item1));
            return o;
        }

        /// <summary>Analyse a list of tiles row by row, building (and
        /// collecting) whatever of each ring is missing; with evict, only three
        /// rows of fragments stay live (FULL's memory bound).</summary>
        static void RasterPass(List<(int tx, int tz)> centres, Dictionary<long, List<CityBuildings.B>> buildings, bool evict)
        {
            var rows = new SortedDictionary<int, List<int>>();
            foreach (var (tx, tz) in centres) { if (!rows.TryGetValue(tz, out var l)) rows[tz] = l = new List<int>(); l.Add(tx); }
            foreach (var kv in rows)
            {
                int tz = kv.Key;
                foreach (int tx in kv.Value)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long k = TileKey(tx + dx, tz + dz);
                            if (frags.ContainsKey(k)) continue;
                            var tm = CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                            frags[k] = FragOf(tx + dx, tz + dz, tm);
                            DestroyMeshes(tm);
                        }
                foreach (int tx in kv.Value) Analyze(tx, tz);
                if (evict)
                {
                    var drop = new List<long>();
                    foreach (var f in frags.Values) if (f.tz < tz - 1) drop.Add(TileKey(f.tx, f.tz));
                    foreach (var k in drop) frags.Remove(k);
                    refCache.Clear();
                }
            }
        }

        static void DestroyMeshes(CityMeshes.TileMeshes tm)
        {
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.buildings, tm.lampPosts })
                if (m != null) UnityEngine.Object.DestroyImmediate(m);
        }

        /// <summary>FULL (G-ship for every city release): every road tile.
        /// Writes city_smooth.txt / .csv / .json; exits 1 in batch mode when a
        /// gated check fails.</summary>
        [MenuItem("PSX Racing/Smoothness Gate (FULL)")]
        public static void RunFull()
        {
            var m = CityMap.Get();
            if (m == null) { Debug.LogError("[CitySmooth] no city data"); if (Application.isBatchMode) EditorApplication.Exit(2); return; }
            Reset();
            mode = "FULL";
            bool ok = false;
            try
            {
                collecting = true; CityMeshes.RecordTap = true;
                var t = CityMeshes.NodeTrims(m);
                Setup(m, t);
                var buildings = CityBuildings.Precompute(m);
                RasterPass(RoadTiles(), buildings, true);
                ok = Report(l => Debug.Log("[CitySmooth] " + l), (pass, what, detail) => Debug.Log($"[CitySmooth] {(pass ? "ok  " : "FAIL")} {what} - {detail}"));
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { collecting = false; CityMeshes.RecordTap = false; frags.Clear(); refCache.Clear(); }
            if (Application.isBatchMode) EditorApplication.Exit(ok || SmoothRules.ReportOnly ? 0 : 1);
        }

        /// <summary>SHOTS: plan, chase and high close-ups of each offender.
        /// The spots come from PSX_SMOOTH_SPOTS ("x,z[,edge[,line]];...") or the
        /// worst of city_smooth.json (PSX_SMOOTH_SHOTS of them, default
        /// SmoothRules.ShotsN, de-duplicated at ShotsDedupM).</summary>
        public static void RunShots()
        {
            var m = CityMap.Get();
            if (m == null) { Debug.LogError("[CitySmooth] no city data"); return; }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "City", "smooth");
            Directory.CreateDirectory(dir);
            PSXRacingBuilder.EnsureCityTextures();
            var t = CityMeshes.NodeTrims(m);
            var buildings = CityBuildings.Precompute(m);
            Reset(); Setup(m, t);
            var spots = new List<(Vector2 at, int edge, string line, string check, string label)>();
            string env = Environment.GetEnvironmentVariable("PSX_SMOOTH_SPOTS");
            if (!string.IsNullOrEmpty(env))
            {
                foreach (var part in env.Split(';'))
                {
                    var f = part.Split(',');
                    if (f.Length < 2 || !float.TryParse(f[0], NumberStyles.Float, Inv, out float x) || !float.TryParse(f[1], NumberStyles.Float, Inv, out float z)) continue;
                    int e = f.Length > 2 && int.TryParse(f[2], out int ei) ? ei : -1;
                    spots.Add((new Vector2(x, z), e, f.Length > 3 ? f[3] : null, "spot", part));
                }
            }
            else
            {
                string jp = Path.Combine(root, "city_smooth.json");
                if (!File.Exists(jp)) { Debug.LogError("[CitySmooth] no city_smooth.json: run FULL (or a city cycle) first, or set PSX_SMOOTH_SPOTS"); return; }
                int want = int.TryParse(Environment.GetEnvironmentVariable("PSX_SMOOTH_SHOTS"), out int w) ? w : SmoothRules.ShotsN;
                if (MiniJson.Parse(File.ReadAllText(jp)) is Dictionary<string, object> J && J.TryGetValue("worst", out var wo) && wo is List<object> list)
                    foreach (var o in list)
                    {
                        if (!(o is Dictionary<string, object> r)) continue;
                        var at = new Vector2((float)Convert.ToDouble(r["x"], Inv), (float)Convert.ToDouble(r["z"], Inv));
                        if (spots.Exists(q => Vector2.Distance(q.at, at) < SmoothRules.ShotsDedupM)) continue;
                        spots.Add((at, Convert.ToInt32(r["edge"], Inv), (string)r["line"], (string)r["check"], $"#{r["rank"]} {r["check"]} {r["name"]}"));
                        if (spots.Count >= want) break;
                    }
            }
            Shader.SetGlobalFloat("_PSXFogNear", 900f); Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f)); Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));
            var labels = new StringBuilder();
            var world = new GameObject("~CitySmoothShots");
            var mats = PSXRacingBuilder.CityMaterials();
            try
            {
                for (int i = 0; i < spots.Count; i++)
                {
                    var (at, edge, lineId, chk, label) = spots[i];
                    int ptx = Mathf.FloorToInt(at.x / CityMeshes.TileSize), ptz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
                    // the ring, with the tap, so the offender's line can be measured again for the overlay
                    frags.Clear(); analysed.Clear(); runs.Clear(); captured.Clear(); refCache.Clear();
                    collecting = true; CityMeshes.RecordTap = true;
                    var tiles = new List<GameObject>();
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var tm = CityMeshes.Build(m, t, buildings, ptx + dx, ptz + dz);
                            frags[TileKey(ptx + dx, ptz + dz)] = FragOf(ptx + dx, ptz + dz, tm);
                            var go = new GameObject($"~tile_{ptx + dx}_{ptz + dz}");
                            go.transform.SetParent(world.transform, false);
                            go.transform.position = tm.origin;
                            Wrap(go, tm.ground, mats, tm.groundSlots);
                            Wrap(go, tm.roads, mats, tm.roadSlots);
                            Wrap(go, tm.barriers, mats, new[] { CityMeshes.Slot.Concrete });
                            Wrap(go, tm.kerbs, mats, new[] { CityMeshes.Slot.Concrete });
                            tiles.Add(go);
                        }
                    collecting = false; CityMeshes.RecordTap = false;
                    if (edge < 0 && m.NearestRoadPoint(at, 30f, false, out int ne, out _, out _)) edge = ne;
                    captureEdge = edge; captureLine = lineId;
                    Analyze(ptx, ptz);
                    captureEdge = -1;
                    var e = edge >= 0 ? m.edges[edge] : null;
                    float s = 0f; if (e != null) CityElevation.ProjectOn(e, at, out s);
                    var hd = e != null ? e.TangentAt(s) : Vector2.up;
                    float y = e != null ? e.YAt(s) : 0f;
                    var fwd = new Vector3(hd.x, 0f, hd.y);
                    var p3 = new Vector3(at.x, y, at.y);
                    string tag = $"smooth_{i + 1:00}_{chk}";
                    // plan: straight down, the line running up the image, 2.2 cm a pixel
                    Shoot(dir, tag + "_plan", p3 + Vector3.up * 60f, Quaternion.LookRotation(Vector3.down, fwd), 8f, 1280, 720, 1, p3);
                    // chase: the player's camera 5.4 m behind and 1.8 m over a point 8 m short, in the 427x240 frame, scaled 3x
                    var eye = p3 - fwd * (8f + 5.4f) + Vector3.up * 1.8f;
                    var aim = p3 - fwd * 8f + fwd * 5.4f + Vector3.up * 0.9f;
                    Shoot(dir, tag + "_chase", eye, Quaternion.LookRotation(aim - eye), 0f, 427, 240, 3, p3, SmoothRules.FovDeg);
                    // high: CityPreview's _high framing
                    var eyeH = p3 - fwd * 60f + Vector3.up * 32f;
                    Shoot(dir, tag + "_high", eyeH, Quaternion.LookRotation(fwd + Vector3.down * 0.34f), 0f, 1280, 720, 1, p3, 60f);
                    labels.AppendLine($"{i + 1}\t{chk}\t{label}\te{edge}\t{(e != null ? e.wayId.ToString() : "-")}\t{CityAudit.LatLon(at.x, at.y)}\t{at.x.ToString("0.0", Inv)},{at.y.ToString("0.0", Inv)},{edge},{lineId}");
                    foreach (var go in tiles) UnityEngine.Object.DestroyImmediate(go);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(world); collecting = false; CityMeshes.RecordTap = false; frags.Clear(); }
            File.WriteAllText(Path.Combine(dir, "smooth_shots.txt"), labels.ToString());
            Debug.Log($"[CitySmooth] {spots.Count} spots shot to {dir}");
        }

        static void Wrap(GameObject parent, Mesh mesh, Material[] mats, CityMeshes.Slot[] slots)
        {
            if (mesh == null) return;
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var use = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++) use[i] = mats[(int)slots[i]];
            mr.sharedMaterials = use;
        }

        /// <summary>Render one frame; on the plan frame (ortho) overlay the
        /// re-measured line (red over V, green within), the plan line (cyan),
        /// a crosshair at the offender and a 1 m scale bar. No scene objects
        /// are added: the render stays the game's.</summary>
        static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float ortho, int w, int h, int upscale, Vector3 target, float fov = 60f)
        {
            var camGO = new GameObject("~smoothCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 3000f; cam.fieldOfView = fov;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            if (ortho > 0f)
            {
                void Dot(Vector3 wp, Color c, int r)
                {
                    var sp = cam.WorldToScreenPoint(wp);
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                        { int x = (int)sp.x + dx, y = (int)sp.y + dy; if (x >= 0 && y >= 0 && x < w && y < h) tex.SetPixel(x, y, c); }
                }
                foreach (var (p, err) in captured) Dot(p, Math.Abs(err) > V ? Color.red : Color.green, 1);
                // the plan line: each captured sample moved by its error onto the plan
                foreach (var (p, err) in captured)
                {
                    var sp = cam.WorldToScreenPoint(p);
                    if (((int)sp.x / 3 + (int)sp.y / 3) % 2 == 0) continue;   // dashed
                    var right = cam.transform.right;
                    Dot(p - right * (float)err, Color.cyan, 0);
                }
                var ts = cam.WorldToScreenPoint(target);
                for (int k = -12; k <= 12; k++) { tex.SetPixel((int)ts.x + k, (int)ts.y, Color.magenta); tex.SetPixel((int)ts.x, (int)ts.y + k, Color.magenta); }
                float pxPerM = h / (2f * ortho);
                for (int k = 0; k < (int)pxPerM; k++) for (int t2 = 0; t2 < 3; t2++) tex.SetPixel(20 + k, 20 + t2, Color.white);
            }
            tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Texture2D outTex = tex;
            if (upscale > 1)
            {
                outTex = new Texture2D(w * upscale, h * upscale, TextureFormat.RGB24, false);
                var src = tex.GetPixels32(); var dst = new Color32[w * upscale * h * upscale];
                for (int y = 0; y < h * upscale; y++) for (int x = 0; x < w * upscale; x++) dst[y * w * upscale + x] = src[(y / upscale) * w + x / upscale];
                outTex.SetPixels32(dst); outTex.Apply();
            }
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), outTex.EncodeToPNG());
            if (outTex != tex) UnityEngine.Object.DestroyImmediate(outTex);
            UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(camGO);
        }
    }
}
