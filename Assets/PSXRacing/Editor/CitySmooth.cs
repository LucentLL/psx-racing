using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
    ///   FAST   inside CityAudit.Run (BeginFast / Collect / EndCollect / EndFast):
    ///          the drive and roadside audits' tiles, the reference spots' rings
    ///          and one band of 1/12 of the road tiles (PSX_SMOOTH_BAND). OPT-IN
    ///          (PSX_SMOOTH_FAST=1) until its first Unity run validates it; then
    ///          SmoothRules.FastInAudit. Run counts are compared only with the
    ///          baseline's runs in the tiles it analysed; keys one by one.
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
        /// <summary>A fan: its corners (the perimeter, anticlockwise; mouth
        /// chords flagged) and the triangles the renderer draws (read from the
        /// mesh: a star, or ear-clipped corners where they are not star-shaped).</summary>
        sealed class FanRec
        {
            public int node, tx, tz; public Vector3d centre; public Vector3d[] corners; public ulong mouths;
            public readonly List<(Vector3d a, Vector3d b, Vector3d c)> tris = new List<(Vector3d, Vector3d, Vector3d)>();
        }
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
            // a fan's triangles, out of its slot's submesh (the tap says which: triStart, triCount)
            var subTris = new Dictionary<int, int[]>();
            int[] TrisOf(int slot)
            {
                for (int i = 0; i < tm.roadSlots.Length; i++)
                    if ((int)tm.roadSlots[i] == slot) { if (!subTris.TryGetValue(i, out var t)) subTris[i] = t = tm.roads.GetTriangles(i); return t; }
                return null;
            }
            foreach (var fa in tap.fans)
            {
                int b = BaseOf(fa.slot);
                if (b < 0 || b + fa.bucketV + fa.count - 1 >= verts.Length) { tapNotes++; continue; }
                int i = b + fa.bucketV;
                var rec = new FanRec { node = fa.node, tx = tx, tz = tz, centre = W(i), corners = new Vector3d[fa.count - 1], mouths = fa.mouths };
                for (int k = 1; k < fa.count; k++) rec.corners[k - 1] = W(i + k);
                var st = TrisOf(fa.slot);
                if (st == null || fa.triStart + 3 * fa.triCount > st.Length) tapNotes++;
                else for (int t = fa.triStart; t < fa.triStart + 3 * fa.triCount; t += 3) rec.tris.Add((W(st[t]), W(st[t + 1]), W(st[t + 2])));
                f.fans.Add(rec);
            }
            if (tapDump != null) DumpFrag(f);
            return f;
        }

        // ================================================================
        //  The agreement instruments (FULL only; Docs/CHARLOTTE.md, "Smoothness
        //  gate"): what the two gates were given, and what each read where the
        //  other found a run. tools/city/gatecmp.mjs reads both.
        // ================================================================

        /// <summary>PSX_SMOOTH_TAPDUMP=&lt;file&gt;: every tile's tap, gzipped:
        /// 'TAP1', then per tile (tx, tz, spans, gores, fans) and each ribbon
        /// or gore quad as (edge, sA, sB, flagsA, flagsB, AL BL BR AR in x, z
        /// from the tile's corner as floats, U at the four corners, the four
        /// heights), each fan as (node, corners, mouths, corners' x, z from the
        /// tile's corner and height). ('TAP2'.) The
        /// builder's own sections and flags, tile by tile - so a run one gate
        /// has and the other lacks can be put down to what each was GIVEN at
        /// its own sections (the replica's flags and positions there), not to
        /// what lies near it.</summary>
        static BinaryWriter tapDump;
        static void DumpFrag(Frag f)
        {
            var w = tapDump;
            double ox = f.tx * (double)CityMeshes.TileSize, oz = f.tz * (double)CityMeshes.TileSize;
            w.Write(f.tx); w.Write(f.tz); w.Write(f.quads.Count); w.Write(f.gores.Count); w.Write(f.fans.Count);
            foreach (var list in new[] { f.quads, f.gores })
                foreach (var q in list)
                {
                    w.Write(q.edge); w.Write(q.sA); w.Write(q.sB); w.Write(q.fA); w.Write(q.fB);
                    foreach (var v in new[] { q.AL, q.BL, q.BR, q.AR }) { w.Write((float)(v.x - ox)); w.Write((float)(v.z - oz)); }
                    w.Write(q.uAL.x); w.Write(q.uBL.x); w.Write(q.uBR.x); w.Write(q.uAR.x);
                    foreach (var v in new[] { q.AL, q.BL, q.BR, q.AR }) w.Write((float)v.y);
                }
            foreach (var fa in f.fans)
            {
                w.Write(fa.node); w.Write(fa.corners.Length); w.Write(fa.mouths);
                foreach (var c in fa.corners) { w.Write((float)(c.x - ox)); w.Write((float)(c.z - oz)); w.Write((float)c.y); }
            }
        }

        /// <summary>PSX_SMOOTH_TRACE=&lt;csv&gt; (idx,check,edge,line,s0,s1:
        /// the OTHER gate's runs, from tools/city/gatecmp.mjs): this gate's
        /// highest reading at each, as a ratio to the check's limit, on the same
        /// edge within [s0, s1], on the same line (maxE) and the same line class
        /// (maxC: the offset dropped), and how many samples it took there (nE,
        /// nC); written to PSX_SMOOTH_TRACE_OUT (idx,maxE,nE,maxC,nC).
        /// linecheck.mjs --trace is the same instrument offline.</summary>
        sealed class TraceAt { public int idx; public string line, cls; public double s0, s1, maxE = -1, maxC = -1; public int nE, nC; }
        static Dictionary<long, List<TraceAt>> trace;
        static readonly Dictionary<string, int> checkIx = new Dictionary<string, int>();
        static long TraceKey(int ci, int e) => ((long)ci << 32) | (uint)e;
        /// <summary>A line id without its offset ('CYs+0.12' -> 'CYs'; linegate.mjs lineClass).</summary>
        static string LineClass(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int i = id.Length;
            while (i > 0 && (char.IsDigit(id[i - 1]) || id[i - 1] == '.')) i--;
            return i < id.Length && i > 0 && (id[i - 1] == '+' || id[i - 1] == '-') ? id.Substring(0, i - 1) : id;
        }
        static double LimitOf(string check) => check == "A4" ? SmoothRules.LineWidthTol : check == "A5" || check == "A5b" ? SmoothRules.StrayM
            : check == "C1" ? SmoothRules.GapM : check == "C2" ? 1 : check == "C3" ? SmoothRules.DashTol : check == "D1" ? SmoothRules.CrossM
            : check == "B4s" ? SmoothRules.SeamM : V;
        static void TraceSample(string check, string kind, int e, double s, double val, double rl, char side, string lid)
        {
            if (!checkIx.TryGetValue(check, out int ci) || !trace.TryGetValue(TraceKey(ci, e), out var list)) return;
            string line = kind == "edge" && (side == 'L' || side == 'R') ? "R" + side : lid, cls = null;
            double r = check == "B3" ? (rl > 0 && val > 0 ? rl / val : 0) : Math.Abs(val) / LimitOf(check);
            foreach (var t in list)
            {
                if (s < t.s0 || s > t.s1) continue;
                if (line == t.line) { t.nE++; if (r > t.maxE) t.maxE = r; }
                cls ??= LineClass(line);
                if (cls == t.cls) { t.nC++; if (r > t.maxC) t.maxC = r; }
            }
        }
        /// <summary>PSX_SMOOTH_STRANDS=&lt;file of edge ids&gt;: every strand through
        /// those edges as the shape checks see it - each kept vertex's edge, s,
        /// x, z, exemption and B2 score - to PSX_SMOOTH_STRANDS_OUT, once per
        /// analysing tile (linecheck --strands is the same dump offline): where
        /// two gates given the same sections read a line differently, the two
        /// polylines say why.</summary>
        static HashSet<int> strandEdges;
        static StreamWriter strandDump;
        static void DumpStrand(string kind, string lineId, List<Pt> pts, List<int> keep, double[] K2)
        {
            bool any = false;
            foreach (int i in keep) if (strandEdges.Contains(pts[i].e)) { any = true; break; }
            if (!any) return;
            var w = strandDump;
            w.Write("S "); w.Write(kind); w.Write(' '); w.Write(lineId); w.Write(" tile "); w.Write(curTile >> 32); w.Write(','); w.Write((int)curTile); w.Write(" n "); w.Write(keep.Count); w.Write('\n');
            for (int m = 0; m < keep.Count; m++)
            {
                var p = pts[keep[m]];
                w.Write(p.e); w.Write(' '); w.Write(p.s.ToString("0.####", Inv)); w.Write(' '); w.Write(p.x.ToString("0.#####", Inv)); w.Write(' '); w.Write(p.z.ToString("0.#####", Inv));
                w.Write(' '); w.Write(p.x3 || p.gore ? 1 : 0); w.Write(' '); w.Write(K2[m].ToString("0.#####", Inv)); w.Write('\n');
            }
        }
        static void TraceBegin()
        {
            trace = null;
            string path = Environment.GetEnvironmentVariable("PSX_SMOOTH_TRACE");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            checkIx.Clear();
            for (int i = 0; i < CheckOrder.Length; i++) checkIx[CheckOrder[i]] = i;
            trace = new Dictionary<long, List<TraceAt>>();
            var intern = new Dictionary<string, string>();
            string In(string x) { if (!intern.TryGetValue(x, out var y)) intern[x] = y = x; return y; }
            int n = 0;
            foreach (var l in File.ReadLines(path))
            {
                if (n++ == 0 || l.Length == 0) continue;
                var f = l.Split(',');
                if (f.Length < 6 || !checkIx.TryGetValue(f[1], out int ci)) continue;
                long k = TraceKey(ci, int.Parse(f[2], Inv));
                if (!trace.TryGetValue(k, out var list)) trace[k] = list = new List<TraceAt>(2);
                list.Add(new TraceAt { idx = int.Parse(f[0], Inv), line = In(f[3]), cls = In(LineClass(f[3])), s0 = double.Parse(f[4], Inv), s1 = double.Parse(f[5], Inv) });
            }
            Debug.Log($"[CitySmooth] tracing {n - 1} places of the other gate's runs ({path})");
        }
        static void TraceEnd()
        {
            if (trace == null) return;
            string path = Environment.GetEnvironmentVariable("PSX_SMOOTH_TRACE_OUT");
            if (string.IsNullOrEmpty(path)) path = Environment.GetEnvironmentVariable("PSX_SMOOTH_TRACE") + ".out.csv";
            var all = new List<TraceAt>();
            foreach (var l in trace.Values) all.AddRange(l);
            all.Sort((a, b) => a.idx.CompareTo(b.idx));
            var sb = new StringBuilder("idx,maxE,nE,maxC,nC\n");
            foreach (var t in all) sb.Append(t.idx).Append(',').Append(t.maxE.ToString("0.#####", Inv)).Append(',').Append(t.nE).Append(',').Append(t.maxC.ToString("0.#####", Inv)).Append(',').Append(t.nC).Append('\n');
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[CitySmooth] traced {all.Count} places to {path}");
            trace = null;
        }

        /// <summary>A gore quad in (A.out, B.out, B.in, A.in) order, as the
        /// ribbon quads are. Bucket.Up emits (a, d, c, b) when the map-view
        /// area of (a, b, c, d) is negative, and D1's depth would then take the
        /// cross chords for the long sides; the long sides run ALONG the host,
        /// so the pair more nearly along its tangent is the one.</summary>
        static Quad CanonGore(Quad g)
        {
            var e = map.edges[g.edge];
            CityElevation.ProjectOn(e, new Vector2((float)((g.AL.x + g.BR.x) * 0.5), (float)((g.AL.z + g.BR.z) * 0.5)), out float s);
            var t = e.TangentAt(s);
            double along1 = Math.Abs((g.BL.x - g.AL.x) * t.x + (g.BL.z - g.AL.z) * t.y), along3 = Math.Abs((g.AR.x - g.AL.x) * t.x + (g.AR.z - g.AL.z) * t.y);
            if (along1 >= along3) return g;
            return new Quad { edge = g.edge, slot = g.slot, tx = g.tx, tz = g.tz, sA = g.sA, sB = g.sB, fA = g.fA, fB = g.fB,
                              AL = g.AL, BL = g.AR, BR = g.BR, AR = g.BL, uAL = g.uAL, uBL = g.uAR, uBR = g.uBR, uAR = g.uBL };
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
            public int[] runPlan = new int[0], planRun = new int[0]; public double[] runQ = new double[0], runQc = new double[0];
            public bool painted; public int texW; public double cap;
        }
        static readonly Dictionary<int, Layout> layouts = new Dictionary<int, Layout>();
        static readonly Color32 Yellow = new Color32(196, 160, 40, 255), White = new Color32(200, 200, 196, 255);
        static readonly List<string> textureNotes = new List<string>();
        /// <summary>A0 TEXTURE failures: (what, |q| in m, ratio).</summary>
        static readonly List<(string what, double val, double ratio)> textureFails = new List<(string, double, double)>();

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
                lay.texW = tex.width;
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            MatchLayout(lay, pr.key + "_" + SurfaceKey(surf));
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
                // within half a centimetre of the centreline a line is +0.00: its sign is float noise in the
                // profile's width (linegate.mjs adds doubles), and a key must not flip on it
                o.Add(new PlanLine { id = "C" + col + (dashed ? "d" : "s") + (off >= 0 || Math.Abs(off) < 0.005 ? "+" : "-") + Math.Abs(off).ToString("0.00", Inv), anchor = 'C', col = col, dashed = dashed, off = off });
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
        /// pattern, nearest within StrayM. runQ is the run centre minus the
        /// plan. Only up to HALF A TEXEL of it (cap = width / texW / 2 +
        /// TexelPadM) is the texture's own rounding, which the position checks
        /// subtract (runQc): V is half a texel precisely so geometry is told
        /// from it. A run further off, a run with no plan line or a plan line
        /// with no run is a painter fault: A0 TEXTURE fails.</summary>
        static void MatchLayout(Layout lay, string name)
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
            lay.cap = (lay.texW > 0 ? lay.width / lay.texW / 2 : 0) + SmoothRules.TexelPadM;
            lay.runQc = new double[lay.runs.Count];
            for (int k = 0; k < lay.runs.Count; k++) lay.runQc[k] = Math.Max(-lay.cap, Math.Min(lay.cap, lay.runQ[k]));
            void Fail(string what, double val, double ratio) { textureNotes.Add($"{name}: {what}"); textureFails.Add(($"{name}: {what}", val, ratio)); }
            for (int k = 0; k < lay.runs.Count; k++)
            {
                if (lay.runPlan[k] < 0) Fail($"texture run at u {lay.runs[k].u.ToString("0.0000", Inv)} has no plan line", 0, 99);
                else if (Math.Abs(lay.runQ[k]) > lay.cap)
                    Fail($"texture run at u {lay.runs[k].u.ToString("0.0000", Inv)} sits {lay.runQ[k] * 100:0.0} cm from plan line {lay.plan[lay.runPlan[k]].id}, over half a texel ({lay.cap * 100:0.0} cm)", Math.Abs(lay.runQ[k]), Math.Abs(lay.runQ[k]) / lay.cap);
            }
            for (int j = 0; j < lay.plan.Count; j++) if (lay.planRun[j] < 0) Fail($"plan line {lay.plan[j].id} has no texture run", 0, 99);
        }

        // ================================================================
        //  The session
        // ================================================================

        static CityMap map;
        static CityMeshes.Trims trims;
        static double[] wayOff;
        /// <summary>D1's plan merge zones: per (branch, host) pair, the branch's
        /// attach arcs (seat pieces: edge, s0, s1 on the branch).</summary>
        static Dictionary<long, List<(int branch, float s0, float s1)>> mergeRanges;
        static string seatNote;
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
            /// <summary>The analysed tile that kept the run (FAST's per-tile counts).</summary>
            public long tile;
            /// <summary>Bucket ids the bad samples touched, with each one's worst value
            /// (see <see cref="Bucket"/>); FinishRuns turns them into keys + ratios.</summary>
            public List<long> bk; public List<double> bv, bl;
            /// <summary>The ribbon side each bucket's samples lie on (a ribbon
            /// edge's key is the side of the edge it is on: linegate.mjs).</summary>
            public List<char> bs;
            /// <summary>The painted line each bucket's samples lie on (their own
            /// edge's plan line; the smallest name where two meet in a bucket):
            /// a strand crosses joints into other profiles, and named after its
            /// identity chain's first piece a run took whichever line the chain
            /// started from - linegate.mjs, which starts chains elsewhere, named
            /// one run two ways.</summary>
            public List<string> bn;
            /// <summary>B3: the limit of each bucket's worst sample (its own
            /// edge's class; linegate.mjs br).</summary>
            public List<double> br;
            /// <summary>The keys, each with its worst ratio and its bad length (the
            /// arc between consecutive bad samples, to the bucket of the later).</summary>
            public List<string> kk; public List<double> kq, kl;
            public double lastArc;
        }

        /// <summary>A key bucket: wayId * 2^20 + round(s on way / KeyStepM);
        /// on a fan perimeter -(node * 4096 + corner + 1).</summary>
        static long Bucket(int e, double s) => (long)map.edges[e].wayId * 1048576L + Math.Max(0L, KeyStep(e, s));
        static long KeyStep(int e, double s) => (long)Math.Floor((wayOff[e] + s) / SmoothRules.KeyStepM + 0.5);   // JS Math.round, not banker's

        static void Reset()
        {
            frags.Clear(); runs.Clear(); analysed.Clear(); layouts.Clear(); textureNotes.Clear(); textureFails.Clear();
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
            // D1's merge zones: every branch end's ATTACH ARC (gate spec 4.4) - where it runs beside its host, the seat's
            // pieces (EmitBranch's own walk: CityMeshes.SeatOf, for street forks as well as the ramps BranchSeats keeps)
            mergeRanges = new Dictionary<long, List<(int, float, float)>>();
            seatNote = null;
            var seats = new List<CityMeshes.Seat>();
            System.Reflection.MethodInfo seatOf = null;
            try { seatOf = typeof(CityMeshes).GetMethod("SeatOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); }
            catch (System.Reflection.AmbiguousMatchException) { }
            if (seatOf != null)
            {
                for (int ei = 0; ei < map.edges.Length; ei++)
                    for (int end = 0; end < 2; end++)
                    {
                        int h = end == 0 ? trims.branchA[ei] : trims.branchB[ei];
                        if (h < 0) continue;
                        var br = map.edges[ei];
                        try { if (seatOf.Invoke(null, new object[] { map, trims, br, map.edges[h], end == 0 ? br.a : br.b }) is CityMeshes.Seat st) seats.Add(st); }
                        catch (Exception ex) { seatNote = "CityMeshes.SeatOf failed (" + (ex.InnerException ?? ex).Message + "): some attach arcs missing"; }
                    }
            }
            else { seats.AddRange(CityMeshes.BranchSeats(map, trims)); seatNote = "CityMeshes.SeatOf not found: only the ramps' attach arcs (BranchSeats)"; }
            foreach (var seat in seats)
                foreach (var pc in seat.pieces)
                {
                    var e = map.edges[pc.edge];
                    for (float s = pc.s0; ; s += 2f)
                    {
                        float ss = Mathf.Min(s, pc.s1);
                        int h = seat.HostAt(e.PointAt(ss), out _);
                        if (h >= 0)
                        {
                            long k = Pair(pc.edge, h);
                            if (!mergeRanges.TryGetValue(k, out var l)) mergeRanges[k] = l = new List<(int, float, float)>();
                            if (!l.Contains((pc.edge, pc.s0, pc.s1))) l.Add((pc.edge, pc.s0, pc.s1));
                        }
                        if (ss >= pc.s1) break;
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

        sealed class RefLine { public double[] X, Z, S; public double off; public List<double> bad; }
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

        /// <summary>B2 (lone and clustered, KinkScores) on the design polyline
        /// within 1 m of s: the DATA / BUILDER cause hint.</summary>
        static bool DataKinkNear(CityMap.Edge e, double s)
        {
            var rf = RefOf(e); double t = s + rf.off;
            if (rf.bad == null)
            {
                rf.bad = new List<double>();
                var keep = KeepIdx(rf.X.Length, i => rf.X[i], i => rf.Z[i]);
                int n = keep.Count;
                var KX = new double[n]; var KZ = new double[n]; var C = new double[n]; var TH = new double[n];
                for (int m = 0; m < n; m++)
                {
                    KX[m] = rf.X[keep[m]]; KZ[m] = rf.Z[keep[m]];
                    if (m > 0) C[m] = C[m - 1] + Math.Sqrt((KX[m] - KX[m - 1]) * (KX[m] - KX[m - 1]) + (KZ[m] - KZ[m - 1]) * (KZ[m] - KZ[m - 1]));
                }
                for (int m = 1; m + 1 < n; m++) TH[m] = Turn(KX[m - 1], KZ[m - 1], KX[m], KZ[m], KX[m + 1], KZ[m + 1]);
                var sc = KinkScores(KX, KZ, C, TH, null);
                for (int m = 1; m + 1 < n; m++) if (sc[m] > V) rf.bad.Add(rf.S[keep[m]]);
            }
            foreach (double sv in rf.bad) if (Math.Abs(sv - t) <= 1) return true;
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
        /// merged, streamed (no sample objects). A run breaks where two bad
        /// samples lie more than RunBreakM apart, and keeps the worst value of
        /// every KeyStepM bucket it touches (its ratchet keys).</summary>
        sealed class RunBuilder
        {
            readonly string check, lineId, kind, reportOnly; readonly bool minIsWorse;
            Run cur; double arc, px = double.NaN, pz;
            public RunBuilder(string check, string lineId, string kind = null, string reportOnly = null, bool minIsWorse = false)
            { this.check = check; this.lineId = lineId; this.kind = kind; this.reportOnly = reportOnly; this.minIsWorse = minIsWorse; }
            public double rLimit;
            /// <summary>The fan whose perimeter this is (-1: none): set on every run at creation.</summary>
            public int node = -1;
            bool Worse(double a, double b) => minIsWorse ? a < b : Math.Abs(a) > Math.Abs(b);
            /// <summary>With the samples' own limits (B3: each sample against its own edge's class), the worst is the
            /// tightest against its own limit.</summary>
            bool Worse(double a, double ra, double b, double rb) => minIsWorse && ra > 0 && rb > 0 ? a / ra < b / rb : Worse(a, b);
            /// <summary>A break in the samples: closes, no arc across it.</summary>
            public void Gap() { Close(); px = double.NaN; }
            public void Push(double x, double y, double z, int e, double s, double val, bool bad, char tag = ' ', int span = -1, char side = ' ', string what = null, string lid = null, double rl = 0)
            {
                if (trace != null) TraceSample(check, kind, e, s, val, rl > 0 ? rl : rLimit, side, lid ?? lineId);
                if (!double.IsNaN(px)) arc += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                px = x; pz = z;
                if (!bad) { Close(); return; }
                if (cur != null && arc - cur.lastArc > SmoothRules.RunBreakM) Close();
                double inc = cur != null ? arc - cur.lastArc : 0;
                if (cur == null)
                    cur = new Run { check = check, lineId = lid ?? lineId, kind = kind, reportOnly = reportOnly, e = e, s = s, x = x, y = y, z = z, val = val,
                                    e0 = e, s0 = s, e1 = e, s1 = s, tag = tag, span = span, side = side, rLimit = rl > 0 ? rl : rLimit, node = node, len = -arc,
                                    bk = new List<long>(), bv = new List<double>(), bl = new List<double>(), bs = new List<char>(), bn = new List<string>(), br = new List<double>(), what = what };
                cur.e1 = e; cur.s1 = s; cur.lastArc = arc;
                long bid = node >= 0 ? -(node * 4096L + Math.Min(Math.Max(span, 0), 4094) + 1) : Bucket(e, s);
                int nb = cur.bk.Count;
                if (nb > 0 && cur.bk[nb - 1] == bid)
                {
                    if (Worse(val, rl, cur.bv[nb - 1], cur.br[nb - 1])) { cur.bv[nb - 1] = val; cur.br[nb - 1] = rl; }
                    cur.bl[nb - 1] += inc;
                    if (lid != null && (cur.bn[nb - 1] == null || string.CompareOrdinal(lid, cur.bn[nb - 1]) < 0)) cur.bn[nb - 1] = lid;
                }
                // the arc from the previous bad sample: half to its bucket, half to this one's (all of it to this one's
                // made a key's bad length depend on which way the line was walked - review 8)
                else { if (nb > 0) cur.bl[nb - 1] += inc / 2; cur.bk.Add(bid); cur.bv.Add(val); cur.bl.Add(nb > 0 ? inc / 2 : inc); cur.bs.Add(side); cur.bn.Add(lid); cur.br.Add(rl); }
                if (Worse(val, rl, cur.val, cur.rLimit))
                { cur.val = val; cur.e = e; cur.s = s; cur.x = x; cur.y = y; cur.z = z; cur.tag = tag; cur.span = span; cur.side = side; cur.what = what; if (lid != null) cur.lineId = lid; if (rl > 0) cur.rLimit = rl; }
            }
            public void Close()
            {
                if (cur == null) return;
                cur.len = cur.lastArc + cur.len;
                Keep(cur); cur = null;
            }
        }

        // the tile being analysed: a run is built over the whole 3x3 ring and kept
        // by the tile its worst sample lies in, so a run crossing a tile edge is
        // counted once, as linecheck.mjs counts it. A fan is analysed once, by the
        // tile that built it, so its runs are kept wherever their worst corner is.
        static double cx0, cz0, cx1, cz1;
        static long curTile;
        static bool keepAnywhere;
        static bool InCentre(double x, double z) => x >= cx0 && x < cx1 && z >= cz0 && z < cz1;
        static bool NearCentre(double x, double z, double m) => x >= cx0 - m && x < cx1 + m && z >= cz0 - m && z < cz1 + m;
        static void Keep(Run r) { if (keepAnywhere || InCentre(r.x, r.z)) { r.tile = curTile; runs.Add(r); } }

        // ================================================================
        //  One tile: chains over its 3x3 ring (gate spec 5.2)
        // ================================================================

        struct Sec { public double s, px, pz, rx, rz; public ushort f; }
        struct Pt { public double x, y, z, v, s; public int e, span, tile; public char tag, side; public bool x3, crop, gore, sq; public string lid; }

        /// <summary>Of the two ends of a jump or a dash, the one a run is reported
        /// at (and the identity a joint's one vertex takes): the lower OSM way,
        /// then the lower edge, then the lower s - one run gets one key whichever
        /// way the chain runs (a chain starts here at the first edge of a tile's
        /// ring, in linegate.mjs at its lowest edge). linegate.mjs firstEnd.</summary>
        /// <summary>Two candidate joint pairs at the same distance, ordered by
        /// their end points (the FirstEnd one first, then the other), so the
        /// pairing does not depend on the order the chain's walk met the pieces
        /// (linegate.mjs pairOrder).</summary>
        static int PtOrder(Pt p, Pt q)
        {
            uint wp = map.edges[p.e].wayId, wq = map.edges[q.e].wayId;
            if (wp != wq) return wp.CompareTo(wq);
            if (p.e != q.e) return p.e.CompareTo(q.e);
            if (p.s != q.s) return p.s.CompareTo(q.s);
            if (p.x != q.x) return p.x.CompareTo(q.x);
            return p.z.CompareTo(q.z);
        }
        static int PairOrder(PieceRef a1, PieceRef b1, PieceRef a2, PieceRef b2)
        {
            (Pt, Pt) Ends(PieceRef a, PieceRef b) { var u = a.pts[a.pts.Count - 1]; var w = b.pts[0]; return PtOrder(u, w) <= 0 ? (u, w) : (w, u); }
            var (p1, q1) = Ends(a1, b1); var (p2, q2) = Ends(a2, b2);
            int c = PtOrder(p1, p2);
            return c != 0 ? c : PtOrder(q1, q2);
        }
        static bool FirstEnd(Pt p, Pt q)
        {
            uint wp = map.edges[p.e].wayId, wq = map.edges[q.e].wayId;
            return wp != wq ? wp < wq : p.e != q.e ? p.e < q.e : p.s <= q.s;
        }
        /// <summary>The joint's one vertex a < V join keeps (a's place), named
        /// after the end FirstEnd picks; exempt only if both ends are (never
        /// looser than either way).</summary>
        static Pt JointVertex(Pt a, Pt b)
        {
            var j = a;
            if ((b.e != a.e || b.s != a.s) && FirstEnd(b, a)) { j.e = b.e; j.s = b.s; j.tag = b.tag; j.span = b.span; j.side = b.side; j.lid = b.lid; j.tile = b.tile; }
            j.x3 = a.x3 && b.x3; j.gore = a.gore && b.gore;
            return j;
        }

        sealed class EdgeData
        {
            public CityMap.Edge e; public List<Quad> quads; public Sec[] A, B; public Layout lay;
            public List<List<Pt>>[] lines; public List<List<double>>[] ratios;
            public List<List<Pt>> ribL = new List<List<Pt>>(), ribR = new List<List<Pt>>(), mid = new List<List<Pt>>();
            public bool fwd;
            /// <summary>Reached across a BEND FAN (a 2-arm node drawn as a slab), not a mitre.</summary>
            public bool bendIn;
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
                    ringFans.AddRange(f.fans);
                    foreach (var g in f.gores) ringGores.Add(CanonGore(g));
                }
            cx0 = tx * CityMeshes.TileSize; cz0 = tz * CityMeshes.TileSize; cx1 = cx0 + CityMeshes.TileSize; cz1 = cz0 + CityMeshes.TileSize;
            curTile = TileKey(tx, tz);
            foreach (var l in byEdge.Values) l.Sort((a, b) => a.sA.CompareTo(b.sA));
            var grid = new PaveGrid(byEdge, ringGores, ringFans);
            var fansByNode = new Dictionary<int, FanRec>();
            foreach (var f in ringFans) fansByNode[f.node] = f;
            // chains through mitred nodes and bend fans, over the ring's edges, whose drawn quads reach this tile
            var used = new HashSet<int>();
            foreach (var kv in byEdge)
            {
                if (used.Contains(kv.Key)) continue;
                bool touches = false;
                foreach (var q in kv.Value)
                {
                    double x0 = Math.Min(Math.Min(q.AL.x, q.BL.x), Math.Min(q.BR.x, q.AR.x)), x1 = Math.Max(Math.Max(q.AL.x, q.BL.x), Math.Max(q.BR.x, q.AR.x));
                    double z0 = Math.Min(Math.Min(q.AL.z, q.BL.z), Math.Min(q.BR.z, q.AR.z)), z1 = Math.Max(Math.Max(q.AL.z, q.BL.z), Math.Max(q.BR.z, q.AR.z));
                    if (x1 >= cx0 - 0.5 && x0 < cx1 + 0.5 && z1 >= cz0 - 0.5 && z0 < cz1 + 0.5) { touches = true; break; }
                }
                if (!touches) continue;
                var chain = ChainFrom(map.edges[kv.Key], byEdge, used);
                AnalyzeChain(chain, byEdge, grid, fansByNode);
            }
            // fan perimeters (curb returns) built by this tile: analysed once, here, so their runs are kept
            // wherever their worst corner lies (a corner can sit 10-30 m out, over the tile edge)
            keepAnywhere = true;
            try { foreach (var fan in ringFans) if (fan.tx == tx && fan.tz == tz) FanChecks(fan); }
            finally { keepAnywhere = false; }
        }

        static List<(CityMap.Edge e, bool fwd, bool bendIn)> ChainFrom(CityMap.Edge e0, Dictionary<int, List<Quad>> byEdge, HashSet<int> used)
        {
            var cur = e0; int enter = e0.a;
            for (int g = 0; g < 100000; g++)
            {
                int o = LinkAt(cur, enter, out _);
                if (o < 0 || o == e0.index || !byEdge.ContainsKey(o) || used.Contains(o)) break;
                var oe = map.edges[o]; enter = oe.a == enter ? oe.b : oe.a; cur = oe;
            }
            var list = new List<(CityMap.Edge, bool, bool)>();
            var at = cur; int from = enter; bool bendIn = false;
            while (at != null && used.Add(at.index))
            {
                bool fwd = at.a == from;
                list.Add((at, fwd, bendIn));
                int exit = fwd ? at.b : at.a;
                int o = LinkAt(at, exit, out bool bend);
                if (o < 0 || !byEdge.ContainsKey(o)) break;
                at = map.edges[o]; from = exit; bendIn = bend;
            }
            return list;
        }

        /// <summary>A 2-arm node the builder draws as a junction slab (past
        /// ContinueCos ComputeTrims patches it). Plan A2: a 2-arm node is never
        /// a junction corner however sharp (A7: never a corner or a fan). The
        /// gate chains the two arms across it - the ribbon edges along the
        /// slab's own perimeter, the midline straight from mouth to mouth - so
        /// B2/B3 judge the corner it draws, and a line ending at its mouths is
        /// no legitimate end (C2).</summary>
        static bool IsBendFan(int n)
        {
            if (!trims.patch[n]) return false;
            int arms = 0;
            foreach (var ei in map.nodeEdges[n]) if (map.edges[ei].a != map.edges[ei].b) arms++;
            return arms == 2;
        }
        static int BendAt(CityMap.Edge e, int n)
        {
            if (e.a == e.b || !IsBendFan(n)) return -1;
            foreach (var ei in map.nodeEdges[n]) if (ei != e.index && map.edges[ei].a != map.edges[ei].b) return ei;
            return -1;
        }
        /// <summary>The next edge of the chain at node n: the mitred through
        /// partner, else the other arm of a bend fan (bend = true), else -1.</summary>
        static int LinkAt(CityMap.Edge e, int n, out bool bend)
        {
            bend = false;
            int o = JointAt(e, n);
            if (o >= 0) return o;
            o = BendAt(e, n);
            bend = o >= 0;
            return o;
        }

        /// <summary>The corners of a fan's perimeter strictly between the corner
        /// at (ax, az) and the one at (bx, bz), walking the way that crosses no
        /// road mouth; empty when either is not a corner (within 5 cm) or no
        /// such way exists (then the strand bridges straight).</summary>
        static List<Vector3d> FanPath(FanRec f, double ax, double az, double bx, double bz)
        {
            int n = f.corners.Length;
            int Near(double x, double z)
            {
                int best = -1; double bd = 0.05 * 0.05;
                for (int i = 0; i < n; i++) { double dx = f.corners[i].x - x, dz = f.corners[i].z - z, d = dx * dx + dz * dz; if (d <= bd) { bd = d; best = i; } }
                return best;
            }
            int ia = Near(ax, az), ib = Near(bx, bz);
            if (ia < 0 || ib < 0 || ia == ib) return new List<Vector3d>();
            bool Mouth(int chord) { int c = ((chord % n) + n) % n; return c < 64 && (f.mouths >> c & 1UL) != 0; }
            foreach (int dir in new[] { 1, -1 })
            {
                var path = new List<Vector3d>();
                int i = ia; bool reached = false;
                for (int g = 0; g < n; g++)
                {
                    if (Mouth(dir > 0 ? i : i - 1)) break;
                    i = ((i + dir) % n + n) % n;
                    if (i == ib) { reached = true; break; }
                    path.Add(f.corners[i]);
                }
                if (reached) return path;
            }
            return new List<Vector3d>();
        }
        static Pt Shift(Pt p, double dx, double dz) { p.x += dx; p.z += dz; return p; }

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
                string lid = PlanIdOf(lay, k);
                for (int i = 0; i < n; i++)
                {
                    var q = quads[i];
                    bool adj = i > 0 && Math.Abs(quads[i - 1].sB - q.sA) < 1e-3f;
                    if (!adj) piece = null;
                    foreach (var sg in QuadIso(q, u))
                    {
                        var pa = MakePt(ed, i, sg.ax, sg.ay, sg.az, sg.av, sg.ea); var pb = MakePt(ed, i, sg.bx, sg.by, sg.bz, sg.bv, sg.eb);
                        pa.lid = lid; pb.lid = lid;
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

        const ushort FSqueezedL = CityMeshes.RoadTap.FSqueezedL, FSqueezedR = CityMeshes.RoadTap.FSqueezedR, FClipL = CityMeshes.RoadTap.FClipL,
                     FClipR = CityMeshes.RoadTap.FClipR, FCollapsed = CityMeshes.RoadTap.FCollapsed;

        /// <summary>A ribbon-edge point with the tap's CAUSES (review 5: the
        /// tap once flagged any edge standing inside its half width, so an edge
        /// a regression pulled in was judged only against its own envelope and
        /// skipped A2/A3 - the offline gate, whose replica flags the squeeze
        /// only when it fired, failed it): squeezed - judged against its I7
        /// envelope; clipped inner or collapsed - X3/X2; cropped (A2/A3 skip) -
        /// any of them, as linegate.mjs's cropL/cropR.</summary>
        static Pt RibPt(EdgeData ed, int i, Vector3d v, Sec c, char side, float s)
        {
            bool sq = (c.f & (side == 'L' ? FSqueezedL : FSqueezedR)) != 0;
            bool x3 = (c.f & (side == 'L' ? FClipL : FClipR)) != 0 || (c.f & FCollapsed) != 0;
            bool crop = sq || x3;
            return new Pt { x = v.x, y = v.y, z = v.z, s = s, e = ed.e.index, span = i, tile = ed.quads[i].tx * 100000 + ed.quads[i].tz, tag = 'S', side = side, crop = crop, x3 = x3, sq = sq };
        }
        static Pt MidPt(EdgeData ed, int i, Vector3d L, Vector3d R, Sec c, float s)
        {
            bool clip = (c.f & (FClipL | FClipR | FCollapsed)) != 0;
            return new Pt { x = (L.x + R.x) * 0.5, y = (L.y + R.y) * 0.5, z = (L.z + R.z) * 0.5, s = s, e = ed.e.index, span = i, tag = 'S', x3 = clip,
                            crop = clip || (c.f & (FSqueezedL | FSqueezedR)) != 0 };
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
            var X = new (bool ok, double x, double y, double z, double v, double t)[5];
            X[0] = Cross(q.AL, q.uAL, q.AR, q.uAR, u); X[1] = Cross(q.BL, q.uBL, q.BR, q.uBR, u);
            X[2] = Cross(q.AL, q.uAL, q.BR, q.uBR, u); X[3] = Cross(q.AL, q.uAL, q.BL, q.uBL, u);
            X[4] = Cross(q.AR, q.uAR, q.BR, q.uBR, u);
            const string letters = "ABDLR";
            // each triangle's two crossings (T2 owns A, R, D; T1 owns D, B, L)
            var segs = new List<(int h0, int h1, double grad)>(2);
            foreach (var tri in new[] { new[] { 0, 4, 2 }, new[] { 2, 1, 3 } })
            {
                int h0 = -1, h1 = -1;
                foreach (int k in tri) if (X[k].ok) { if (h0 < 0) h0 = k; else if (h1 < 0) h1 = k; else h1 = -2; }
                if (h0 < 0 || h1 < 0) continue;
                double grad = tri[0] == 0 ? GradU(q.AL, q.uAL.x, q.AR, q.uAR.x, q.BR, q.uBR.x) : GradU(q.AL, q.uAL.x, q.BR, q.uBR.x, q.BL, q.uBL.x);
                segs.Add((h0, h1, grad));
            }
            // TRAVEL order (paintiso.mjs quadIso): A = 0, B = 1, on L / R / D the edge's own parameter (each runs A to B).
            // Two segments sharing D are one path X - D - Y walked from its earlier outer end, so a line cropped out
            // through the L chord comes back A->D, D->L - never a reversed tail.
            double T(int k) => k == 0 ? 0 : k == 1 ? 1 : X[k].t;
            void Emit(int h0, int h1, double grad) =>
                isoOut.Add(new IsoSeg { ax = X[h0].x, ay = X[h0].y, az = X[h0].z, av = X[h0].v, bx = X[h1].x, by = X[h1].y, bz = X[h1].z, bv = X[h1].v, ea = letters[h0], eb = letters[h1], grad = grad });
            if (segs.Count == 2 && (segs[0].h0 == 2 || segs[0].h1 == 2) && (segs[1].h0 == 2 || segs[1].h1 == 2))
            {
                int x = segs[0].h0 == 2 ? segs[0].h1 : segs[0].h0, y = segs[1].h0 == 2 ? segs[1].h1 : segs[1].h0;
                bool xFirst = T(x) < T(y) || (T(x) == T(y) && x <= y);
                if (xFirst) { Emit(x, 2, segs[0].grad); Emit(2, y, segs[1].grad); }
                else { Emit(y, 2, segs[1].grad); Emit(2, x, segs[0].grad); }
                return isoOut;
            }
            foreach (var (h0, h1, grad) in segs)
            {
                bool swap = T(h1) < T(h0) || (T(h1) == T(h0) && h1 < h0);
                if (swap) Emit(h1, h0, grad); else Emit(h0, h1, grad);
            }
            return isoOut;
        }
        static (bool ok, double x, double y, double z, double v, double t) Cross(Vector3d a, Vector2 ua, Vector3d b, Vector2 ub, double u)
        {
            double da = ua.x - u, db = ub.x - u;
            if ((da >= 0) == (db >= 0)) return (false, 0, 0, 0, 0, 0);
            double t = da / (da - db);
            return (true, a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, ua.y + (ub.y - ua.y) * t, t);
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
                    foreach (var (a, b, c) in f.tris) Add(a, b, c, -10 - f.node, r);   // what the renderer draws (never an assumed star)
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

        /// <summary>A plan merge zone (report-only until WP-18b) is a branch's
        /// ATTACH ARC only (<see cref="Setup"/>; linecheck: the clip range) plus
        /// MergeMarginM: the paint of either road inside the other there. The
        /// same pair anywhere else - a branch bending back across its host -
        /// and paint sharing a fan or a mitred node with another road are
        /// gated ("paint crossing the junction").</summary>
        static bool MergeZoneAt(int e, double s, int other, double x, double z)
        {
            if (other < 0 || !mergeRanges.TryGetValue(Pair(e, other), out var l)) return false;
            double M = SmoothRules.MergeMarginM;
            foreach (var (br, s0, s1) in l)
            {
                if (br == e) { if (s >= s0 - M && s <= s1 + M) return true; continue; }
                CityElevation.ProjectOn(map.edges[br], new Vector2((float)x, (float)z), out float so);
                if (so >= s0 - M && so <= s1 + M) return true;
            }
            return false;
        }

        /// <summary>One A1 sample of a ribbon edge: err is its signed offset from
        /// the design edge (+ = left of travel).</summary>
        struct RibSample { public double x, y, z, s, err; public bool x3, sq; public char tag; public int span; }
        const string SqueezeWhat = "squeezed edge outside its I7 envelope (the cut eased like a taper over the class floor)";

        /// <summary>A1 on a SQUEEZED ribbon edge, against where plan I7 puts it
        /// ("the cut per chain is the cut needed, max-filtered over the taper
        /// floor and eased like a taper"; tools/city/lib/linegate.mjs
        /// squeezeDev). The cut (inward) is -sgn * err; L is the class's taper
        /// floor. The cut is split at its turning points (V hysteresis) into
        /// rises and falls. EASE: inside each rise or fall of height H, two
        /// samples w &lt; L apart may differ by at most H_L g(w / L), g(x) = 1.5x -
        /// 0.5x^3 (the most a smoothstep of height H_L over L changes over any
        /// w), H_L the LOCAL height: the change across the floor-length window
        /// centred on the pair (the last sample at or before its start to the
        /// first at or after its end, inside the rise), never more than H - a
        /// fast step inside a tall, slow rise no longer borrows the whole rise's
        /// height; the excess came faster than a taper over the floor. HOLD: at a dip
        /// between two cuts narrower than L where it drops below the lower of
        /// them, that lower cut less the cut - the edge came back out where I7
        /// holds it in. A cut eased over L or longer (or several stacked) and
        /// held between cuts reads 0 (the sampled window only widens H_L); a
        /// clipped or collapsed sample (X3/X2) breaks the signal, 0.</summary>
        static double[] SqueezeDev(List<RibSample> smp, double sgn, double L)
        {
            int n = smp.Count; var dev = new double[n];
            for (int i0 = 0; i0 < n;)
            {
                if (smp[i0].x3) { i0++; continue; }
                int i1 = i0; while (i1 + 1 < n && !smp[i1 + 1].x3) i1++;
                int m = i1 - i0 + 1; var s = new double[m]; var c = new double[m];
                for (int k = 0; k < m; k++) { s[k] = smp[i0 + k].s; c[k] = -sgn * smp[i0 + k].err; }
                // turning points (V hysteresis; a plateau's first sample), then EASE per rise or fall (lib/kink.mjs)
                var tp = TurningPoints(c, V);
                int b0 = i0;
                void Bump(int k, double e) { if (e > dev[b0 + k]) dev[b0 + k] = e; }
                EaseInto(s, c, tp, L, V, Bump);
                // HOLD, per dip between two cuts
                for (int t = 1; t + 1 < tp.Count; t++)
                {
                    int k0 = tp[t];
                    if (!(c[k0] < c[tp[t - 1]] && c[k0] < c[tp[t + 1]])) continue;
                    double H = Math.Min(c[tp[t - 1]], c[tp[t + 1]]);
                    int kL = k0, kR = k0;
                    while (kL > 0 && c[kL] < H) kL--;
                    while (kR < m - 1 && c[kR] < H) kR++;
                    if (s[kR] - s[kL] >= L) continue;
                    for (int k = kL + 1; k < kR; k++) Bump(k, H - c[k]);
                }
                i0 = i1 + 1;
            }
            return dev;
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
                int j = lay.runPlan[k]; var plan = j >= 0 ? lay.plan[j] : null; char col = lay.runs[k].col; double q = lay.runQc[k];
                string lineId = PlanIdOf(lay, k);
                for (int pi = 0; pi < ed.lines[k].Count; pi++)
                {
                    var pc = ed.lines[k][pi]; var rat = ed.ratios[k][pi];
                    var bA1 = new RunBuilder("A1", lineId); var bA4 = new RunBuilder("A4", lineId); var bA5 = new RunBuilder("A5", lineId);
                    var bD1 = new RunBuilder("D1", lineId); var bD1m = new RunBuilder("D1", lineId, null, "merge zone (a branch attach arc: the seat's pieces + MergeMarginM): report-only until WP-18b");
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
                                double d = Math.Abs(sd - PlanOff(pj, e, s, hw) - (kk >= 0 ? lay.runQc[kk] : 0));
                                if (d < dmin) dmin = d;
                            }
                            bA5.Push(x, y, z, e.index, s, Math.Min(dmin, 10), dmin > SmoothRules.StrayM, tag, span);
                            var (owner, depth) = grid.Under(x, y, z, chainSet);
                            bool cross = depth > SmoothRules.CrossM, mz = cross && MergeZoneAt(e.index, s, owner, x, z);
                            // what the paint is inside: another road's ribbon, a gore quad or a junction fan
                            string into = !cross ? null : owner >= 0 ? "inside e" + owner : owner == -2 ? "inside a gore quad" : "inside the fan at node " + (-10 - owner);
                            bD1.Push(x, y, z, e.index, s, depth, cross && !mz, tag, span, ' ', into);
                            bD1m.Push(x, y, z, e.index, s, depth, cross && mz, tag, span, ' ', into);
                        }
                    }
                    bA1.Close(); bA4.Close(); bA5.Close(); bD1.Close(); bD1m.Close();
                }
            }
            // ---- ribbon edges: A1-edge. An unsqueezed section against the design edge - an edge standing inside its
            // half width for any cause but the squeeze too (a regression); a SQUEEZED one (the tap's squeezed flag, set
            // only where SqueezeSection moved it; X6 no longer exempts it: a smooth squeeze step wandered through every
            // shape check) against its I7 envelope (SqueezeDev); a clipped inner edge or a gore nose (X3/X2) not at all -
            // the host's edge carries it
            double floorM = SmoothRules.TaperFloorFor(SmoothRules.ClassOf(e.cls, e.link));
            foreach (char side in new[] { 'L', 'R' })
            {
                double sgn = side == 'L' ? -1 : 1;
                foreach (var pc in side == 'L' ? ed.ribL : ed.ribR)
                {
                    var smp = new List<RibSample>();
                    for (int i = 1; i < pc.Count; i++)
                    {
                        var a = pc[i - 1]; var b = pc[i];
                        double segL = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                        int n = Math.Max(1, (int)Math.Ceiling(segL / BIN));
                        double sda = Sd(rf, a.x, a.z, a.s), sdb = Sd(rf, b.x, b.z, b.s);
                        Fill(side == 'L' ? GL : GR, a, b, sda, sdb);
                        bool x3 = a.x3 || b.x3, sq = !x3 && (a.sq || b.sq);
                        for (int m = i == 1 ? 0 : 1; m <= n; m++)
                        {
                            double t = m / (double)n, x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t, z = a.z + (b.z - a.z) * t, s = a.s + (b.s - a.s) * t;
                            double sd = x3 ? 0 : m == 0 ? sda : m == n ? sdb : Sd(rf, x, z, s);
                            smp.Add(new RibSample { x = x, y = y, z = z, s = s, x3 = x3, sq = sq, err = x3 ? 0 : sd - sgn * HwD(e, s), tag = m == 0 || m == n ? 'S' : 'I', span = b.span });
                        }
                    }
                    double[] dev = null;
                    foreach (var q in smp) if (q.sq) { dev = SqueezeDev(smp, sgn, floorM); break; }
                    var b1 = new RunBuilder("A1", "R" + side);
                    for (int i = 0; i < smp.Count; i++)
                    {
                        var q = smp[i];
                        if (q.x3) { b1.Push(q.x, q.y, q.z, e.index, q.s, 0, false); continue; }
                        double d = dev != null ? dev[i] : 0;
                        bool env = d > V && (q.sq || d > Math.Abs(q.err));
                        double val = q.sq || d > Math.Abs(q.err) ? d : q.err;
                        b1.Push(q.x, q.y, q.z, e.index, q.s, val, env || (!q.sq && Math.Abs(q.err) > V), q.tag, q.span, side, env ? SqueezeWhat : null);
                    }
                    b1.Close();
                }
            }
            // a span whose edge the squeeze moved in or a clip put on its host (either section), or collapsed: A2/A3 skip
            // it (X6) - linegate.mjs's cropL / cropR, from the tap's causes
            bool Cropped(int span, char side)
            {
                var q = ed.quads[span];
                ushort f = (ushort)(q.fA | q.fB);
                if ((f & FCollapsed) != 0) return true;
                ushort l = (ushort)(FSqueezedL | FClipL), r = (ushort)(FSqueezedR | FClipR);
                return side == 'L' ? (f & l) != 0 : side == 'R' ? (f & r) != 0 : (f & (l | r)) != 0;
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
                        double skew = (va + vb) / 2 - (lay.runQc[k1] + lay.runQc[k2]) / 2 - (l + r) / 2;
                        b2.Push(p.x, 0, p.y, e.index, s, skew, Math.Abs(skew) > V, ' ', sp);
                    }
                    b2.Close();
                }
            }
            // ---- A3 INSET: each edge line against its own drawn edge
            foreach (var (pid, side, sgn) in new[] { ("EL", 'R', 1.0), ("ER", 'L', -1.0) })
            {
                int j = lay.plan.FindIndex(p => p.id == pid); int k = j >= 0 ? lay.planRun[j] : -1;
                if (k < 0) continue;
                // the PLAN's inset (shoulder + EdgeLineInsetM), less only the texture's half-texel rounding
                double planInset = lay.plan[j].inset - sgn * lay.runQc[k];
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
                        near = Math.Min(near, Math.Abs(G[k][b] - (PlanOff(pj, e, s, hw) + lay.runQc[k])));
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
        /// <summary>rLimitOf: B3's limit for a sample on an edge (ribbon edges: InnerEdgeMinRM; the midline: its OWN
        /// edge's class R_min - it came from the strand's first edge, so a midline from a link onto a motorway was
        /// judged at the link's 25 m, or the motorway's 150 m, by where the strand started; linegate.mjs rLimit), or
        /// null: no B3 (paint).</summary>
        static void ShapeChecks(string kind, string lineId, List<Pt> pts, Func<int, double> rLimitOf, int node = -1)
        {
            if (pts.Count < 3) return;
            // drop duplicates and collinear vertices, the same whichever way the strand is walked (KeepIdx)
            var keep = KeepIdx(pts.Count, i => pts[i].x, i => pts[i].z);
            if (keep.Count < 3) return;
            var C = new double[keep.Count];
            for (int m = 1; m < keep.Count; m++) C[m] = C[m - 1] + Dist(pts[keep[m - 1]], pts[keep[m]]);
            double Ltot = C[C.Length - 1];
            (double x, double z, int i, double t) At(double a, int hint)
            {
                int i = Math.Max(0, Math.Min(hint, keep.Count - 2));
                while (i > 0 && C[i] > a) i--;
                while (i + 2 < keep.Count && C[i + 1] < a) i++;
                var p = pts[keep[i]]; var q = pts[keep[i + 1]]; double L = C[i + 1] - C[i];
                double t = L > 1e-9 ? Math.Max(0, Math.Min(1, (a - C[i]) / L)) : 0;
                return (p.x + (q.x - p.x) * t, p.z + (q.z - p.z) * t, i, t);
            }
            // B2 KINK: lone and clustered corners (KinkScores, lib/kink.mjs)
            var TH = new double[keep.Count]; var KX = new double[keep.Count]; var KZ = new double[keep.Count];
            for (int m = 0; m < keep.Count; m++) { KX[m] = pts[keep[m]].x; KZ[m] = pts[keep[m]].z; }
            for (int m = 1; m + 1 < keep.Count; m++) TH[m] = Turn(KX[m - 1], KZ[m - 1], KX[m], KZ[m], KX[m + 1], KZ[m + 1]);
            // the most any line of the strand stands off its data line (the WAVE rule's geometry test): 0 on the midline
            // and a fan's perimeter
            double hwMax = 0;
            if (kind == "edge" || kind == "paint") foreach (int i in keep) if (map.edges[pts[i].e].width * 0.5 > hwMax) hwMax = map.edges[pts[i].e].width * 0.5;
            var K2 = KinkScores(KX, KZ, C, TH, m => pts[keep[m]].x3 || pts[keep[m]].gore, out byte[] K2kind, out byte[] K2silent, hwMax);
            if (strandDump != null) DumpStrand(kind, lineId, pts, keep, K2);
            var b2 = new RunBuilder("B2", lineId, kind) { node = node };
            for (int m = 1; m + 1 < keep.Count; m++)
            {
                var b = pts[keep[m]];
                if (b.x3 || b.gore) { b2.Push(b.x, b.y, b.z, b.e, b.s, 0, false); continue; }
                // collinear in the frame of its curve: no sample (ArcFrame)
                if (K2silent != null && K2silent[m] != 0) continue;
                double f = K2[m];
                b2.Push(b.x, b.y, b.z, b.e, b.s, f, f > V, b.tag, b.span, b.side, f > V ? KindWhat(K2kind[m]) : null, b.lid);
            }
            b2.Close();
            // B3 CURVE (ribbon edges, midline, fans)
            if (rLimitOf != null)
            {
                var b3 = new RunBuilder("B3", lineId, kind, null, true) { node = node };
                for (int m = 1; m + 1 < keep.Count; m++)
                {
                    var b = pts[keep[m]];
                    if (C[m] < SmoothRules.CurveHalfM || Ltot - C[m] < SmoothRules.CurveHalfM || b.x3 || b.gore) { b3.Push(b.x, b.y, b.z, b.e, b.s, 1e9, false); continue; }
                    var p0 = At(C[m] - SmoothRules.CurveHalfM, m); var p1 = At(C[m] + SmoothRules.CurveHalfM, m);
                    double h1 = Math.Atan2(b.z - p0.z, b.x - p0.x), h2 = Math.Atan2(p1.z - b.z, p1.x - b.x), dpsi = Math.Abs(h2 - h1);
                    if (dpsi > Math.PI) dpsi = 2 * Math.PI - dpsi;
                    double Rr = dpsi > 1e-9 ? SmoothRules.CurveHalfM / dpsi : 1e9;
                    double rMin = rLimitOf(b.e);
                    b3.Push(b.x, b.y, b.z, b.e, b.s, Rr, Rr < rMin, b.tag, b.span, b.side, null, null, rMin);
                }
                b3.Close();
            }
            // B1 JITTER: 0.25 m samples within JitterHalfM of a kept vertex; Kasa (line fallback) over +-JitterHalfM
            double step = SmoothRules.JitterStepM, H = SmoothRules.JitterHalfM; int nH = (int)Math.Round(H / step);
            // stations anchored to the WAY's arc (B1Stations): the same samples - and readings - whichever way the
            // strand is walked and wherever a gate cuts it (this gate cuts strands at its 3x3 ring, linegate.mjs at
            // a chain's ends)
            var cand = B1Stations(pts, keep, C, Ltot, step, H);
            if (cand.Count == 0) return;
            var b1 = new RunBuilder("B1", lineId, kind) { node = node };
            var xs = new double[2 * nH + 1]; var zs = new double[2 * nH + 1];
            double last = -1; int hint = 0, wlo = 0;
            foreach (double a in cand)
            {
                if (double.IsNaN(a)) { if (last >= 0) b1.Gap(); last = -1; continue; }   // between two windows
                if (last >= 0 && Math.Abs(a - last) < 1e-6) continue;
                last = a;
                bool exempt = false;
                for (int j = -nH; j <= nH; j++)
                {
                    var p = At(a + j * step, hint);
                    if (j == -nH) hint = p.i;
                    xs[j + nH] = p.x; zs[j + nH] = p.z;
                    // exempt where the line it samples is: on a vertex, that vertex; inside a segment, both its ends
                    // (review 8: the segment's START alone made a window exempt walked one way and judged the other)
                    var q0 = pts[keep[p.i]]; var q1 = pts[keep[Math.Min(p.i + 1, keep.Count - 1)]];
                    bool x0 = q0.x3 || q0.gore, x1 = q1.x3 || q1.gore;
                    if (p.t <= 0 ? x0 : p.t >= 1 ? x1 : x0 && x1) exempt = true;
                }
                var cen = At(a, hint); var src = pts[keep[cen.i]]; var nxt = pts[keep[Math.Min(cen.i + 1, keep.Count - 1)]];
                // the sample's own (edge, s): interpolated along its segment (a kept segment can be tens of metres long);
                // its side and name from the nearer end (a ribbon edge's side flips at a joint the chain runs backwards)
                // (a station at the midpoint of a segment between two ways - the fallback - is a tie: the FirstEnd end)
                var near = cen.t < 0.5 - 1e-9 ? src : cen.t > 0.5 + 1e-9 ? nxt : FirstEnd(src, nxt) ? src : nxt;
                int sE = src.e == nxt.e ? src.e : near.e;
                double sS = src.e == nxt.e ? src.s + (nxt.s - src.s) * cen.t : near.s;
                if (exempt) { b1.Push(cen.x, src.y, cen.z, sE, sS, 0, false); continue; }
                double cxx = xs[2 * nH] - xs[0], czz = zs[2 * nH] - zs[0], cl = Math.Sqrt(cxx * cxx + czz * czz); if (cl < 1e-9) cl = 1;
                double dev = 0;
                while (wlo < keep.Count && C[wlo] <= a - H) wlo++;
                for (int m = wlo; m < keep.Count && C[m] < a + H; m++)
                {
                    var p = pts[keep[m]];
                    dev = Math.Max(dev, Math.Abs(((p.x - xs[0]) * czz - (p.z - zs[0]) * cxx) / cl));
                }
                double res = dev < V / 2 ? 0 : KasaResidual(xs, zs, nH);
                b1.Push(cen.x, src.y, cen.z, sE, sS, res, res > V, near.tag, near.span, near.side, null, near.lid);
            }
            b1.Close();
        }
        static double Dist(Pt a, Pt b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        /// <summary>The vertices the shape checks keep (linegate.mjs keepIdx is
        /// the same code), the SAME whichever way a strand is walked (review 8:
        /// dropped relative to the last kept vertex, a walk from each end kept
        /// different vertices, and the two gates walk strands from different
        /// ends): a run of vertices each within 1 mm of the next is one vertex,
        /// the one with the smallest (x, z); a vertex is kept when the collinear
        /// walk from EITHER end keeps it (its turn from that walk's last kept
        /// vertex to the next is at least CollinearDeg); both ends are kept.</summary>
        static List<int> KeepIdx(int n, Func<int, double> X, Func<int, double> Z)
        {
            var rep = new List<int>();
            if (n == 0) return rep;
            double D(int a, int b) => Math.Sqrt((X(b) - X(a)) * (X(b) - X(a)) + (Z(b) - Z(a)) * (Z(b) - Z(a)));
            for (int i = 0; i < n;)
            {
                int j = i, best = i;
                while (j + 1 < n && D(j, j + 1) < 1e-3)
                {
                    j++;
                    if (X(j) < X(best) || (X(j) == X(best) && Z(j) < Z(best))) best = j;
                }
                rep.Add(best); i = j + 1;
            }
            int m = rep.Count;
            if (m <= 2) return rep;
            double lim = SmoothRules.CollinearDeg / Deg;
            var mark = new bool[m];
            mark[0] = true; mark[m - 1] = true;
            for (int a = 0, k = 1; k + 1 < m; k++)
            {
                int A = rep[a], B = rep[k], Cn = rep[k + 1];
                if (D(A, B) < 1e-3) continue;
                if (Math.Abs(Turn(X(A), Z(A), X(B), Z(B), X(Cn), Z(Cn))) < lim) continue;
                mark[k] = true; a = k;
            }
            for (int a = m - 1, k = m - 2; k > 0; k--)
            {
                int A = rep[a], B = rep[k], Cn = rep[k - 1];
                if (D(A, B) < 1e-3) continue;
                if (Math.Abs(Turn(X(A), Z(A), X(B), Z(B), X(Cn), Z(Cn))) < lim) continue;
                mark[k] = true; a = k;
            }
            var keep = new List<int>();
            for (int k = 0; k < m; k++) if (mark[k]) keep.Add(rep[k]);
            return keep;
        }

        /// <summary>B1's sample stations on one strand, as sorted strand arcs
        /// with NaN between two windows that do not meet (linegate.mjs
        /// b1Stations is the same code). A window is +-H about each kept
        /// interior vertex, clipped to [H, Ltot - H]; windows closer than a step
        /// merge. Inside a window a station stands where the line's own way arc
        /// (wayOff + s, the arc the ratchet keys are rounded on) is a whole
        /// number of steps and a half (never on a 5 m key boundary, where a
        /// sample's float s put it in one bucket walked one way and the next
        /// walked the other), interpolated along each kept segment - so a place is
        /// sampled the same whichever way a strand is walked and wherever it was
        /// cut (anchored to the strand's middle, as before, the two gates read B1
        /// at a different phase wherever they cut a strand differently). A kept
        /// segment that crosses to another way, or along which the way's arc
        /// does not advance about as fast as the line (a fan's perimeter, a
        /// branching way's jump), is stepped from its own midpoint instead.</summary>
        static List<double> B1Stations(List<Pt> pts, List<int> keep, double[] C, double Ltot, double step, double H)
        {
            int n = keep.Count;
            var W = new double[n]; var Wy = new uint[n];
            for (int m = 0; m < n; m++) { var p = pts[keep[m]]; W[m] = wayOff[p.e] + p.s; Wy[m] = map.edges[p.e].wayId; }
            var outp = new List<double>();
            double lo = 0, hi = -1; int seg = 0;
            void Flush()
            {
                if (hi < lo) return;
                if (outp.Count > 0) outp.Add(double.NaN);
                while (seg > 0 && C[seg] > lo) seg--;
                for (int m = seg; m + 1 < n && C[m] <= hi; m++)
                {
                    double a0 = C[m], a1 = C[m + 1], L = a1 - a0;
                    if (a1 < lo || L < 1e-9) continue;
                    seg = m;
                    double x0 = Math.Max(lo, a0), x1 = Math.Min(hi, a1), dw = W[m + 1] - W[m], adw = Math.Abs(dw);
                    if (Wy[m] == Wy[m + 1] && adw >= 0.2 * L && adw <= 5 * L + 0.5 && adw > 1e-6)
                    {
                        double w0 = W[m] + (x0 - a0) / L * dw, w1 = W[m] + (x1 - a0) / L * dw;
                        double k0 = Math.Ceiling(Math.Min(w0, w1) / step - 0.5 - 1e-9), k1 = Math.Floor(Math.Max(w0, w1) / step - 0.5 + 1e-9);
                        if (dw > 0) for (double k = k0; k <= k1; k++) outp.Add(a0 + ((k + 0.5) * step - W[m]) / dw * L);
                        else for (double k = k1; k >= k0; k--) outp.Add(a0 + ((k + 0.5) * step - W[m]) / dw * L);
                    }
                    else
                    {
                        double mid = (a0 + a1) / 2, k0 = Math.Ceiling((x0 - mid) / step - 1e-9), k1 = Math.Floor((x1 - mid) / step + 1e-9);
                        for (double k = k0; k <= k1; k++) outp.Add(mid + k * step);
                    }
                }
            }
            for (int m = 1; m + 1 < n; m++)
            {
                double a = Math.Max(C[m] - H, H), b = Math.Min(C[m] + H, Ltot - H);
                if (b < a) continue;
                if (hi >= lo && a <= hi + step) { if (b > hi) hi = b; continue; }
                Flush(); lo = a; hi = b;
            }
            Flush();
            return outp;
        }

        /// <summary>B2's chord at kept vertex m (tools/city/lib/kink.mjs
        /// kinkChord): the arc to the nearest kept vertex on each side where the
        /// line has turned again by KinkNoiseShare of this one's turn (one turn,
        /// or the running sum of the turns passed), capped at ChordCapM. Noise
        /// (the bisector pinch, diagonal crossings, sections a few hundredths of
        /// a degree off straight) never shortens the chord; a curve that keeps
        /// turning ends it.</summary>
        static double KinkChord(double[] C, double[] TH, int m)
        {
            double thr = Math.Abs(TH[m]) * SmoothRules.KinkNoiseShare, cap = SmoothRules.ChordCapM;
            return Math.Min(Math.Min(C[m] - C[Reach(C, TH, m, -1, thr, cap, 0)], C[Reach(C, TH, m, 1, thr, cap, 0)] - C[m]), cap);
        }

        /// <summary>From kept vertex m, step by dir to the first vertex where the
        /// line has turned again by thr, the strand's end, or cap metres (less
        /// pad, the way already come from a cluster's virtual corner) away
        /// (lib/kink.mjs reach).</summary>
        static int Reach(double[] C, double[] TH, int m, int dir, double thr, double cap, double pad)
        {
            int n = C.Length, i = m + dir; double sum = 0;
            while (i > 0 && i < n - 1 && Math.Abs(C[i] - C[m]) + pad < cap)
            {
                sum += TH[i];
                if (Math.Abs(TH[i]) >= thr || Math.Abs(sum) >= thr) break;
                i += dir;
            }
            return i;
        }

        /// <summary>The sign of the turn that stops Reach(C, TH, m, dir, thr,
        /// cap, 0): the one vertex turning thr or more, else the running sum; 0
        /// when the strand's end or cap stops it instead (lib/kink.mjs
        /// stopSign).</summary>
        static int StopSign(double[] C, double[] TH, int m, int dir, double thr, double cap)
        {
            int n = C.Length, i = m + dir; double sum = 0;
            while (i > 0 && i < n - 1 && Math.Abs(C[i] - C[m]) < cap)
            {
                sum += TH[i];
                if (Math.Abs(TH[i]) >= thr) return Math.Sign(TH[i]);
                if (Math.Abs(sum) >= thr) return Math.Sign(sum);
                i += dir;
            }
            return 0;
        }

        /// <summary>How far from kept vertex m, going dir (-1: back along the
        /// line INTO m; +1: on along the line OUT of m), the line stays within V
        /// of ONE straight line - every vertex passed within V either side of a
        /// line parallel to the chord from P[m] to the far end (the farthest
        /// vertex, or the point exactly cap away) - and turns, over the vertices
        /// passed, by no more than maxTurn (the lone limit: a stretch that turns
        /// more is a curve, however flat). Interpolated between the last vertex
        /// inside and the first outside; +Infinity when it runs so to the
        /// strand's end; at most cap. line (4 numbers): the last line accepted -
        /// the unit chord from P[m] towards the far end, the strip centre's
        /// offset along (cz, -cx), its half-width (lib/kink.mjs straightRun).</summary>
        static double StraightRun(double[] X, double[] Z, double[] C, double[] TH, int m, int dir, double V0, double cap, double maxTurn, double[] line)
        {
            int n = C.Length;
            double prevH = 0, pcx = 0, pcz = 0, pc = 0, turn = 0;
            double Done(double len) { line[0] = pcx; line[1] = pcz; line[2] = pc; line[3] = prevH; return len; }
            for (int i = m + dir; i >= 0 && i < n; i += dir)
            {
                double L = Math.Abs(C[i] - C[m]), Lp = Math.Abs(C[i - dir] - C[m]); bool cut = L > cap;
                if (i != m + dir) { turn += TH[i - dir]; if (Math.Abs(turn) > maxTurn) return Done(Math.Min(cap, Lp)); }
                // the far end: vertex i, or the point exactly cap away on the segment into it
                double ex = X[i], ez = Z[i];
                if (cut) { double t = (cap - Lp) / (L - Lp); ex = X[i - dir] + (X[i] - X[i - dir]) * t; ez = Z[i - dir] + (Z[i] - Z[i - dir]) * t; }
                double cx = ex - X[m], cz = ez - Z[m], cl = Math.Sqrt(cx * cx + cz * cz), lo = 0, hi = 0;
                if (cl > 1e-9)
                {
                    cx /= cl; cz /= cl;
                    for (int q = m + dir; q != i; q += dir)
                    {
                        double d = (X[q] - X[m]) * cz - (Z[q] - Z[m]) * cx;
                        if (d < lo) lo = d; else if (d > hi) hi = d;
                    }
                }
                else { cx = 0; cz = 0; }
                double h = (hi - lo) / 2;
                if (h > V0) return Done(Math.Min(cap, Lp + ((cut ? cap : L) - Lp) * (V0 - prevH) / (h - prevH)));
                pcx = cx; pcz = cz; pc = (lo + hi) / 2; prevH = h;
                if (L >= cap) return Done(cap);
            }
            return Done(double.PositiveInfinity);
        }

        /// <summary>What gave a vertex its B2 score (lib/kink.mjs KIND_*).</summary>
        const byte KindLone = 0, KindCluster = 1, KindHedge = 2, KindJog = 3, KindZig = 4, KindOut = 5, KindWave = 6, KindHook = 7, KindArc = 8;
        static readonly string[] KindText = { "a lone corner", "a corner split over close vertices", "a hedged corner (turn and turn back; the net corner)",
            "a jog (a sideways step between two straights)", "a zigzag peak (the line turns back on both sides)",
            "a bump or notch (drawn outside every smooth transition between its straights)",
            "a wave (lobes turning back on both sides, off their mean line)",
            "a hook (a corner just before the line's end: the approach extrapolated, at most the end's own offset)" };
        /// <summary>The run text of a kind (lib/kink.mjs kindText): null for a
        /// lone corner on the line as drawn.</summary>
        static string KindWhat(byte k) => k == KindLone ? null : KindText[k & 7] + ((k & KindArc) != 0 ? ", on a curve (judged in the frame of its arc)" : "");

        /// <summary>The least sideways gap, anywhere along the window between
        /// the points A and B, between the line through A with unit direction u
        /// and the line through B with unit direction w, across their mean
        /// direction; 0 when they cross inside it (lib/kink.mjs jogLines).</summary>
        static double JogLines(double ax, double az, double ux, double uz, double bx, double bz, double wx, double wz)
        {
            double mx = ux + wx, mz = uz + wz, ml = Math.Sqrt(mx * mx + mz * mz);
            if (ml < 1e-9) return 0;
            mx /= ml; mz /= ml;
            double nx = -mz, nz = mx, cu = ux * mx + uz * mz, cw = wx * mx + wz * mz;
            if (cu < 1e-6 || cw < 1e-6) return 0;
            double x0 = ax * mx + az * mz, x1 = bx * mx + bz * mz;
            double LatIn(double x) => (ax * nx + az * nz) + (x - x0) * (ux * nx + uz * nz) / cu;
            double LatOut(double x) => (bx * nx + bz * nz) + (x - x1) * (wx * nx + wz * nz) / cw;
            double g0 = LatOut(x0) - LatIn(x0), g1 = LatOut(x1) - LatIn(x1);
            return g0 * g1 <= 0 ? 0 : Math.Min(Math.Abs(g0), Math.Abs(g1));
        }
        /// <summary>JogLines between the approach line (P[j-1] -> P[j]) and the
        /// exit line (P[k] -> P[k+1]) (lib/kink.mjs jogGap).</summary>
        static double JogGap(double[] X, double[] Z, int j, int k)
        {
            double ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
            double ul = Math.Sqrt(ux * ux + uz * uz), wl = Math.Sqrt(wx * wx + wz * wz);
            if (ul < 1e-9 || wl < 1e-9) return 0;
            return JogLines(X[j], Z[j], ux / ul, uz / ul, X[k], Z[k], wx / wl, wz / wl);
        }

        /// <summary>g(x) = 1.5x - 0.5x^3 (1 from x = 1): the most a smoothstep
        /// of height 1 over a length L changes over any stretch x L - the
        /// squeeze envelope's EASE (SqueezeDev), the jog's and wide OUTSIDE's
        /// (lib/kink.mjs easeG).</summary>
        static double EaseG(double x) => x >= 1 ? 1 : 1.5 * x - 0.5 * x * x * x;

        /// <summary>The plan's fastest ease: the shortest class taper floor
        /// (SmoothRules.TaperFloor; lib/kink.mjs minTaperFloor).</summary>
        static double MinTaperFloor()
        {
            double m = double.PositiveInfinity;
            foreach (var r in SmoothRules.TaperFloor) if (r.r < m) m = r.r;
            return m;
        }

        /// <summary>The turning points of a signal c (V hysteresis; a plateau's
        /// first sample), the first and the last included (lib/kink.mjs
        /// turningPoints).</summary>
        static List<int> TurningPoints(double[] c, double V0)
        {
            int m = c.Length; var tp = new List<int> { 0 };
            int mode = 0, cand = 0;
            for (int k = 1; k < m; k++)
            {
                if (mode == 0) { if (c[k] - c[0] > V0) { mode = 1; cand = k; } else if (c[0] - c[k] > V0) { mode = -1; cand = k; } continue; }
                if (mode > 0) { if (c[k] > c[cand]) cand = k; else if (c[cand] - c[k] > V0) { tp.Add(cand); mode = -1; cand = k; } }
                else { if (c[k] < c[cand]) cand = k; else if (c[k] - c[cand] > V0) { tp.Add(cand); mode = 1; cand = k; } }
            }
            if (mode != 0 && cand != 0) tp.Add(cand);
            if (tp[tp.Count - 1] != m - 1) tp.Add(m - 1);
            return tp;
        }
        /// <summary>EASE: inside each rise or fall of height H &gt; V between
        /// turning points tp of the signal c (at arc s), any two samples w &lt; L
        /// apart may differ by at most H_L g(w / L), H_L the LOCAL height (the
        /// change across the L-long window centred on the pair, inside the rise;
        /// never more than H); bump gets every excess at both samples
        /// (lib/kink.mjs easeInto).</summary>
        static void EaseInto(double[] s, double[] c, List<int> tp, double L, double V0, Action<int, double> bump)
        {
            for (int t = 0; t + 1 < tp.Count; t++)
            {
                int a = tp[t], b = tp[t + 1]; double H = Math.Abs(c[b] - c[a]);
                if (H <= V0) continue;
                for (int i = a; i < b; i++)
                {
                    // the floor window centred on the pair (i, j): kl the last sample at or before its start, kh the
                    // first at or after its end (inside the rise); both only move forward as j does
                    int kl = i, kh = i + 1;
                    for (int j = i + 1; j <= b && s[j] - s[i] < L; j++)
                    {
                        double mid = (s[i] + s[j]) / 2, lo = mid - L / 2, hi = mid + L / 2;
                        if (j == i + 1) { while (kl > a && s[kl] > lo) kl--; }
                        else while (kl < i && s[kl + 1] <= lo) kl++;
                        if (kh < j) kh = j;
                        while (kh < b && s[kh] < hi) kh++;
                        double HL = Math.Min(H, Math.Abs(c[kh] - c[kl]));
                        double e = Math.Abs(c[j] - c[i]) - HL * EaseG((s[j] - s[i]) / L);
                        if (e > 0) { bump(i, e); bump(j, e); }
                    }
                }
            }
        }

        /// <summary>The convex hull of a few points (Andrew's monotone chain,
        /// counter-clockwise, collinear points dropped; the insertion sort of
        /// lib/kink.mjs hullOf, so both order them identically).</summary>
        static List<(double x, double z)> HullOf(List<(double x, double z)> pts)
        {
            var p = new List<(double x, double z)>(pts);
            for (int i = 1; i < p.Count; i++)
            {
                var q = p[i]; int j = i - 1;
                while (j >= 0 && (p[j].x > q.x || (p[j].x == q.x && p[j].z > q.z))) { p[j + 1] = p[j]; j--; }
                p[j + 1] = q;
            }
            double Cr((double x, double z) o, (double x, double z) a, (double x, double z) b) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
            var lo = new List<(double x, double z)>(); var up = new List<(double x, double z)>();
            for (int i = 0; i < p.Count; i++) { var q = p[i]; while (lo.Count >= 2 && Cr(lo[lo.Count - 2], lo[lo.Count - 1], q) <= 0) lo.RemoveAt(lo.Count - 1); lo.Add(q); }
            for (int i = p.Count - 1; i >= 0; i--) { var q = p[i]; while (up.Count >= 2 && Cr(up[up.Count - 2], up[up.Count - 1], q) <= 0) up.RemoveAt(up.Count - 1); up.Add(q); }
            lo.RemoveAt(lo.Count - 1); up.RemoveAt(up.Count - 1);
            lo.AddRange(up);
            return lo;
        }

        /// <summary>How far (x, z) stands outside the convex polygon poly
        /// (HullOf's order); 0 inside. One or two points are a point or a
        /// segment (lib/kink.mjs outsideOf).</summary>
        static double OutsideOf(List<(double x, double z)> poly, double x, double z)
        {
            int n = poly.Count;
            if (n == 0) return 0;
            if (n == 1) return Math.Sqrt((poly[0].x - x) * (poly[0].x - x) + (poly[0].z - z) * (poly[0].z - z));
            bool inside = n >= 3; double d = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n];
                double sx = b.x - a.x, sz = b.z - a.z, L2 = sx * sx + sz * sz;
                if (n >= 3 && sx * (z - a.z) - sz * (x - a.x) < -1e-9) inside = false;
                double t = L2 > 1e-12 ? Math.Max(0, Math.Min(1, ((x - a.x) * sx + (z - a.z) * sz) / L2)) : 0;
                double qx = a.x + sx * t - x, qz = a.z + sz * t - z, e = Math.Sqrt(qx * qx + qz * qz);
                if (e < d) d = e;
            }
            return inside ? 0 : d;
        }

        /// <summary>A reference line of a window (lib/kink.mjs segLine /
        /// fitLine): a point on it, its unit direction of travel, how far it
        /// holds, how far the drawn line strays either side of it there.</summary>
        struct WinLine { public double px, pz, ux, uz, len, h; }

        /// <summary>B2's score at every kept vertex (lib/kink.mjs kinkScores,
        /// whose header is the rule in full; kind[m] names the rule): the worst
        /// of its lone facet sagitta - twice it at a ZIGZAG peak, whose chords
        /// stop at a turn back on both sides - of every CLUSTER it is a corner
        /// vertex of, of every HEDGED window's net corner, and, between two
        /// straights, the window's signed corner, OUTSIDE distance and JOG (up
        /// to ChordCapM: the whole step over a quarter of the cap or less, else
        /// the part faster than the plan's fastest ease; wider windows, up to
        /// KinkViewM, the corner and the outside distance faster than the ease,
        /// over the whole feature only), and of every WAVE lobe's deviation from
        /// its mean line (under SimplifyEpsM). A straight: the line does not
        /// turn again by KinkNoiseShare of the window's excursion (round 3's
        /// rule, from the drawn segment), or runs within V of one straight line
        /// turning no more than the lone limit (StraightRun, from that line, less
        /// its spread), or runs on to the strand's end; a HOOK, a lone corner
        /// whose chord runs on to the strand's end inside the cap, by the corner
        /// extrapolated from the other side, at most the end's own offset. The
        /// worst of those rules on the line as drawn and on the line in its
        /// ARC'S FRAME (ArcFrame; kind | KindArc). silent (or null): the
        /// vertices collinear in the frame of their curve, which are no samples
        /// of the line.</summary>
        static double[] KinkScores(double[] X, double[] Z, double[] C, double[] TH, Func<int, bool> exempt) => KinkScores(X, Z, C, TH, exempt, out _, out _);
        /// <summary>BOTH WAYS (lib/kink.mjs kinkScores): the rules walk a line from
        /// its first vertex to its last and take greedy decisions on the way, so
        /// the same line read backwards scored differently at 2.6% of the vertices
        /// past V (0.27 cm one way, 10.9 cm the other). The two gates walk a chain
        /// from different ends. A vertex scores the worse of the line walked both
        /// ways (its rule with it) and is silent only when silent both ways: a line
        /// looks the same driven either way, and no reading loosens.</summary>
        static double[] KinkScores(double[] X, double[] Z, double[] C, double[] TH, Func<int, bool> exempt, out byte[] kind, out byte[] silent, double halfWidth = 0)
        {
            int n = C.Length;
            var a = KinkScoresOneWay(X, Z, C, TH, exempt, out kind, out silent, halfWidth);
            if (n < 3) return a;
            double[] Xr = new double[n], Zr = new double[n], Cr = new double[n], THr = new double[n]; double L = C[n - 1];
            for (int m = 0; m < n; m++) { int r = n - 1 - m; Xr[m] = X[r]; Zr[m] = Z[r]; Cr[m] = L - C[r]; THr[m] = -TH[r]; }
            var b = KinkScoresOneWay(Xr, Zr, Cr, THr, exempt == null ? null : (Func<int, bool>)(m => exempt(n - 1 - m)), out byte[] kindR, out byte[] silentR, halfWidth);
            for (int m = 1; m + 1 < n; m++) { int r = n - 1 - m; if (b[r] > a[m] * (1 + 1e-9) + 1e-12) { a[m] = b[r]; kind[m] = kindR[r]; } }
            if (silent != null && silentR != null)
            {
                var both = new byte[n];
                for (int m = 1; m + 1 < n; m++) if (silent[m] != 0 && silentR[n - 1 - m] != 0 && !(a[m] > SmoothRules.V)) both[m] = 1;
                silent = both;
            }
            else silent = null;
            return a;
        }
        /// <summary>The rules on the line walked one way (first vertex to last): KinkScores.</summary>
        static double[] KinkScoresOneWay(double[] X, double[] Z, double[] C, double[] TH, Func<int, bool> exempt, out byte[] kind, out byte[] silent, double halfWidth = 0)
        {
            int n = C.Length;
            var F = ArcFrame(X, Z, C, TH, exempt);
            var score = KinkScoresOn(X, Z, C, TH, exempt, out kind, halfWidth, false, false, false);
            silent = null;
            if (F != null)
            {
                var s2 = KinkScoresOn(F.X, F.Z, F.C, F.TH, m => F.ex[m] != 0, out byte[] k2, halfWidth, true, F.open0, F.open1);
                for (int u = 1; u + 1 < F.C.Length; u++)
                {
                    int r = F.raw[u];
                    if (r <= 0 || r >= n - 1 || (exempt != null && exempt(r))) continue;
                    // the frame's reading where it is the worse one by more than rounding (a straight stretch reads the same in both)
                    if (s2[u] > score[r] * (1 + 1e-9) + 1e-12) { score[r] = s2[u]; kind[r] = (byte)(k2[u] | KindArc); }
                }
                // a vertex collinear in the frame of its curve is no sample of the line, as a collinear vertex on a straight is none
                silent = new byte[n];
                for (int r = 1; r + 1 < n; r++) if (F.zone[r] != 0 && F.rep[r] == 0 && !(score[r] > SmoothRules.V)) silent[r] = 1;
            }
            return score;
        }

        /// <summary>The rules on one frame (lib/kink.mjs scoresOn), the line as
        /// its arrays give it. frame: these ARE the frame's arrays - a vertex or
        /// a cluster turning on both sides by the same way (a stretch that still
        /// curves in the frame: its facets are the line's own, and a curve the
        /// reference did not hold for would read twice its sagitta) scores
        /// nothing as a lone corner or a cluster. open0 / open1: that end of the
        /// line's frame is an extrapolation - no end rule there (no hook; a
        /// straight that runs on to it is as long as it is).</summary>
        static double[] KinkScoresOn(double[] X, double[] Z, double[] C, double[] TH, Func<int, bool> exempt, out byte[] kind, double halfWidth, bool frame, bool open0, bool open1)
        {
            int n = C.Length;
            double V0 = SmoothRules.V, cap = SmoothRules.ChordCapM, share = SmoothRules.KinkNoiseShare, view = SmoothRules.KinkViewM, pMax = V0 * view / SmoothRules.PixelAtM;
            double Lf = MinTaperFloor(), eps = SmoothRules.SimplifyEpsM;
            var score = new double[n]; var kd = new byte[n]; kind = kd;
            bool Ex(int m) => exempt != null && exempt(m);
            for (int m = 1; m + 1 < n; m++)
            {
                if (Ex(m)) continue;
                double a = Math.Abs(TH[m]), thr = a * share;
                int i0 = Reach(C, TH, m, -1, thr, cap, 0), i1 = Reach(C, TH, m, 1, thr, cap, 0);
                double cm = C[m] - C[i0], cp = C[i1] - C[m];
                score[m] = Math.Min(Math.Min(cm, cp), cap) * a / 8;
                // a zigzag peak: the chord stops at a turn back on both sides
                int sg = Math.Sign(TH[m]);
                if (sg != 0 && StopSign(C, TH, m, -1, thr, cap) == -sg && StopSign(C, TH, m, 1, thr, cap) == -sg) { score[m] *= 2; kd[m] = KindZig; continue; }
                if (frame && sg != 0 && StopSign(C, TH, m, -1, thr, cap) == sg && StopSign(C, TH, m, 1, thr, cap) == sg) { score[m] = 0; continue; }
                // a HOOK: the chord runs on to the strand's end inside the cap (the stub)
                bool endM = i0 == 0 && cm < cap && !open0, endP = i1 == n - 1 && cp < cap && !open1;
                if (endM || endP)
                {
                    double app = endM && endP ? Math.Max(cm, cp) : endP ? Math.Min(cm, cap) : Math.Min(cp, cap), stub = endM && endP ? Math.Min(cm, cp) : endP ? cp : cm;
                    double h = Math.Min(app * a / 8, stub * Math.Sin(Math.Min(a, Math.PI / 2)));
                    if (h > score[m]) { score[m] = h; kd[m] = KindHook; }
                }
            }
            double minTurn = 8 * V0 / cap;
            void Give(int j, int k, double s, double big, byte why)
            {
                double corner = share * big;
                for (int m = j; m <= k; m++) if (Math.Abs(TH[m]) >= corner && s > score[m]) { score[m] = s; kd[m] = why; }
            }
            // the least distance from (vx, vz) to the drawn segments P[i0]P[i0+1] .. P[i1]P[i1+1], no more than d0
            double PathDist(int i0, int i1, double vx, double vz, double d0)
            {
                double dr = d0;
                for (int i = i0; i <= i1 && dr > 0; i++)
                {
                    double sx = X[i + 1] - X[i], sz = Z[i + 1] - Z[i], L2 = sx * sx + sz * sz;
                    double tt = L2 > 1e-12 ? Math.Max(0, Math.Min(1, ((vx - X[i]) * sx + (vz - Z[i]) * sz) / L2)) : 0;
                    double qx = X[i] + sx * tt - vx, qz = Z[i] + sz * tt - vz, d = Math.Sqrt(qx * qx + qz * qz);
                    if (d < dr) dr = d;
                }
                return dr;
            }
            // (vx, vz) inside the polygon P[j] .. P[k], closed by its chord (crossing number)
            bool InsidePoly(int j, int k, double vx, double vz)
            {
                bool c = false;
                for (int i = j; i <= k; i++)
                {
                    int i2 = i < k ? i + 1 : j; double xa = X[i], za = Z[i], xb = X[i2], zb = Z[i2];
                    if ((za > vz) != (zb > vz) && vx < (xb - xa) * (vz - za) / (zb - za) + xa) c = !c;
                }
                return c;
            }
            // how far the line runs within V of one straight line into m (back) and out of m (on), and that line: once each
            var runB = new double[n]; var runF = new double[n]; var lineB = new double[4 * n]; var lineF = new double[4 * n]; var tmp = new double[4];
            for (int m = 0; m < n; m++) { runB[m] = -1; runF[m] = -1; }
            double Back(int m) { if (runB[m] < 0) { runB[m] = StraightRun(X, Z, C, TH, m, -1, V0, cap, minTurn, tmp); if (double.IsPositiveInfinity(runB[m]) && open0) runB[m] = C[m] - C[0]; for (int q = 0; q < 4; q++) lineB[4 * m + q] = tmp[q]; } return runB[m]; }
            double On(int m) { if (runF[m] < 0) { runF[m] = StraightRun(X, Z, C, TH, m, 1, V0, cap, minTurn, tmp); if (double.IsPositiveInfinity(runF[m]) && open1) runF[m] = C[n - 1] - C[m]; for (int q = 0; q < 4; q++) lineF[4 * m + q] = tmp[q]; } return runF[m]; }
            // no vertex within the cap from m (going dir) turns thr or more
            bool Quiet(int m, int dir, double thr)
            {
                for (int i = m + dir; i > 0 && i < n - 1 && Math.Abs(C[i] - C[m]) < cap; i += dir) if (Math.Abs(TH[i]) >= thr) return false;
                return true;
            }
            // the drawn segment into j (dir -1) or out of k (dir +1)
            bool SegLine(int m, int dir, out WinLine r)
            {
                double ux = dir < 0 ? X[m] - X[m - 1] : X[m + 1] - X[m], uz = dir < 0 ? Z[m] - Z[m - 1] : Z[m + 1] - Z[m], l = Math.Sqrt(ux * ux + uz * uz);
                r = new WinLine { px = X[m], pz = Z[m], ux = l > 0 ? ux / l : 0, uz = l > 0 ? uz / l : 0, len = l, h = 0 };
                return l >= 1e-9;
            }
            // the straight line the line runs within V of into m (dir -1) or out of m (+1): StraightRun's strip centre
            bool FitLine(int m, int dir, out WinLine r)
            {
                var L = dir < 0 ? lineB : lineF; double cx = L[4 * m], cz = L[4 * m + 1], c = L[4 * m + 2];
                r = new WinLine { px = X[m] + cz * c, pz = Z[m] - cx * c, ux = dir < 0 ? -cx : cx, uz = dir < 0 ? -cz : cz, len = Math.Min(dir < 0 ? runB[m] : runF[m], cap), h = L[4 * m + 3] };
                return !(cx == 0 && cz == 0);
            }
            void Cluster(int j, int k, double T, double big)
            {
                double aT = Math.Abs(T);
                if (aT <= minTurn || aT >= Math.PI * 0.95) return;
                double ux = X[j] - X[j - 1], uz = Z[j] - Z[j - 1], wx = X[k + 1] - X[k], wz = Z[k + 1] - Z[k];
                double ul = Math.Sqrt(ux * ux + uz * uz), wl = Math.Sqrt(wx * wx + wz * wz);
                if (ul < 1e-9 || wl < 1e-9) return;
                ux /= ul; uz /= ul; wx /= wl; wz /= wl;
                double den = ux * wz - uz * wx;
                if (Math.Abs(den) < 1e-9) return;
                double dx = X[k] - X[j], dz = Z[k] - Z[j];
                double a = (dx * wz - dz * wx) / den, b = (ux * dz - uz * dx) / den;
                if (!(a >= 0 && b >= 0)) return;
                double vx = X[j] + ux * a, vz = Z[j] + uz * a;
                double fMax = cap * aT / 8, dr = PathDist(j, k - 1, vx, vz, double.PositiveInfinity);
                if (dr >= pMax || fMax - dr <= Math.Max(V0, dr)) return;
                double thr = share * aT; int sT = Math.Sign(T);
                if (frame && StopSign(C, TH, j, -1, thr, cap) == sT && StopSign(C, TH, k, 1, thr, cap) == sT) return;
                int i0 = Reach(C, TH, j, -1, thr, cap, a), i1 = Reach(C, TH, k, 1, thr, cap, b);
                double f = Math.Min(Math.Min(C[j] - C[i0] + a, C[i1] - C[k] + b), cap) * aT / 8;
                double sc = (f - dr) * V0 / Math.Max(V0, dr);
                if (sc > V0) Give(j, k, sc, big, KindCluster);
            }
            // the window j..k between the approach line A (into j) and the exit line B (out of k), which turn aT between
            // them: the net corner where they meet, the drawing's outside distance, and (up to ChordCapM) its jog - each
            // less the lines' own spread (A.h + B.h)
            void Between(int j, int k, double aT, double big, bool wide, WinLine A, WinLine B)
            {
                double w = C[k] - C[j], slop = A.h + B.h;
                bool corner = aT > minTurn && aT < Math.PI * 0.95;
                double ux = A.ux, uz = A.uz, wx = B.ux, wz = B.uz;
                double den = ux * wz - uz * wx, dx = B.px - A.px, dz = B.pz - A.pz;
                bool meet = Math.Abs(den) > 1e-9;
                double a = meet ? (dx * wz - dz * wx) / den : 0, b = meet ? (ux * dz - uz * dx) / den : 0;
                double vx = A.px + ux * a, vz = A.pz + uz * a;
                // the net corner at Vc, its rounding the drawn polygon's, SIGNED: minus the overshoot where the drawing passes outside Vc
                if (corner && meet && -a <= A.len && -b <= B.len)
                {
                    double dr = k > j + 1 && InsidePoly(j, k, vx, vz) ? -PathDist(j, k - 1, vx, vz, double.PositiveInfinity) : PathDist(j - 1, k, vx, vz, (w / 2) * Math.Tan(aT / 2));
                    if (dr < pMax)
                    {
                        double thr = share * aT;
                        int i0 = Reach(C, TH, j, -1, thr, cap, a), i1 = Reach(C, TH, k, 1, thr, cap, b);
                        double f = Math.Min(Math.Min(C[j] - C[i0] + a, C[i1] - C[k] + b), cap) * aT / 8;
                        double s = (f - dr) * V0 / Math.Max(V0, dr) - slop;
                        if (s > V0) Give(j, k, s, big, KindHedge);
                    }
                }
                // outside: the drawn vertices between j and k against the region every smooth convex transition occupies;
                // wider than the cap, the part of it that came faster than the plan's ease
                if (k > j + 1)
                {
                    List<(double x, double z)> poly;
                    if (meet && a >= 0 && b >= 0) poly = HullOf(new List<(double x, double z)> { (A.px, A.pz), (vx, vz), (B.px, B.pz) });
                    else
                    {
                        double tk = (B.px - A.px) * ux + (B.pz - A.pz) * uz, tj = (A.px - B.px) * wx + (A.pz - B.pz) * wz;
                        poly = HullOf(new List<(double x, double z)> { (A.px, A.pz), (B.px, B.pz), (A.px + ux * tk, A.pz + uz * tk), (B.px + wx * tj, B.pz + wz * tj) });
                    }
                    int m = k - j + 1; var od = new double[m]; double odMax = 0;
                    for (int i = 1; i + 1 < m; i++) { od[i] = OutsideOf(poly, X[j + i], Z[j + i]); if (od[i] > odMax) odMax = od[i]; }
                    if (odMax - slop > V0)
                    {
                        if (!wide) Give(j, k, odMax - slop, big, KindOut);
                        else
                        {
                            var sArc = new double[m]; for (int i = 0; i < m; i++) sArc[i] = C[j + i];
                            double e = 0;
                            EaseInto(sArc, od, TurningPoints(od, V0), Lf, V0, (q, x) => { if (x > e) e = x; });
                            if (e - slop > V0) Give(j, k, e - slop, big, KindOut);
                        }
                    }
                }
                // the jog: a step between the two straights - the whole of it over a quarter of the cap or less, else the
                // part that came faster than the plan's fastest ease
                if (!wide)
                {
                    double d = JogLines(A.px, A.pz, ux, uz, B.px, B.pz, wx, wz) - slop;
                    if (d > V0)
                    {
                        double e = w <= cap * share ? d : d * (1 - EaseG(w / Lf));
                        if (e > V0) Give(j, k, e, big, KindJog);
                    }
                }
            }
            void Hedged(int j, int k, double T, double big, double exc)
            {
                double w = C[k] - C[j], aT = Math.Abs(T); bool wide = w > cap;
                bool corner = aT > minTurn && aT < Math.PI * 0.95;
                // the net corner, rounded at most as T / 2 at each end of the window (anywhere: a lower bound)
                if (corner && !wide)
                {
                    double h = w / 2, dre = h * Math.Tan(aT / 2), thr = share * aT;
                    if (dre < pMax && cap * aT / 8 - dre > Math.Max(V0, dre))
                    {
                        int i0 = Reach(C, TH, j, -1, thr, cap, h), i1 = Reach(C, TH, k, 1, thr, cap, h);
                        double f = Math.Min(Math.Min(C[j] - C[i0] + h, C[i1] - C[k] + h), cap) * aT / 8;
                        double s = (f - dre) * V0 / Math.Max(V0, dre);
                        if (s > V0) Give(j, k, s, big, KindHedge);
                    }
                }
                // between two straights
                bool oldA = false, oldB = false, newA, newB;
                if (wide)
                {
                    // the whole feature: no vertex within the cap either side turns KinkNoiseShare of its largest turn
                    double thrF = share * big;
                    if (Math.Abs(TH[j]) < thrF || Math.Abs(TH[k]) < thrF || !Quiet(j, -1, thrF) || !Quiet(k, 1, thrF)) return;
                    newA = true; newB = On(k) >= cap;
                    if (!newB) return;
                }
                else
                {
                    double Ls = Math.Min(w / share, cap), thrS = share * Math.Max(exc, aT);
                    int o0 = Reach(C, TH, j, -1, thrS, cap, 0), o1 = Reach(C, TH, k, 1, thrS, cap, 0);
                    oldA = C[j] - C[o0] >= Ls || (o0 == 0 && !open0); oldB = C[o1] - C[k] >= Ls || (o1 == n - 1 && !open1);
                    newA = Back(j) >= Ls; newB = On(k) >= Ls;
                    if (!(oldA || newA) || !(oldB || newB)) return;
                }
                bool haveSa = SegLine(j, -1, out var sa), haveSb = SegLine(k, 1, out var sb);
                if (oldA && oldB && haveSa && haveSb) Between(j, k, Math.Abs(T), big, wide, sa, sb);
                if (newA || newB)
                {
                    WinLine la = sa, lb = sb; bool okA = haveSa, okB = haveSb;
                    if (newA) okA = FitLine(j, -1, out la);
                    if (newB) okB = FitLine(k, 1, out lb);
                    if (okA && okB) Between(j, k, Math.Abs(Math.Atan2(la.ux * lb.uz - la.uz * lb.ux, la.ux * lb.ux + la.uz * lb.uz)), big, wide, la, lb);
                }
            }
            for (int j = 1; j + 2 < n; j++)
            {
                if (TH[j] == 0 || Ex(j)) continue;
                int sg = Math.Sign(TH[j]);
                double T = TH[j], big = Math.Abs(TH[j]), bigAll = big, exc = big; bool hedge = false; int wideOk = -1;
                for (int k = j + 1; k + 1 < n; k++)
                {
                    double w = C[k] - C[j];
                    if (w > cap)
                    {
                        // wider windows only where the line runs within V of one straight line for the whole cap into j
                        if (w > view) break;
                        if (wideOk < 0) wideOk = Back(j) >= cap ? 1 : 0;
                        if (wideOk == 0) break;
                    }
                    if (Ex(k)) break;
                    double t = TH[k];
                    // a turn back of KinkNoiseShare of the running turn or more: no longer one cluster, a hedged window from here on
                    if (!hedge && Math.Sign(t) != sg && Math.Abs(t) >= share * Math.Abs(T)) hedge = true;
                    T += t; bigAll = Math.Max(bigAll, Math.Abs(t));
                    if (hedge) Hedged(j, k, T, bigAll, exc);
                    else if (w <= cap && Math.Sign(t) == sg) { big = Math.Max(big, Math.Abs(t)); Cluster(j, k, T, big); }
                    exc = Math.Max(exc, Math.Abs(T));
                }
            }
            Waves(X, Z, C, TH, share, cap, V0, eps, halfWidth, Ex, score, kd);
            return score;
        }

        /// <summary>B2's WAVE rule (lib/kink.mjs waves): lobes - runs of
        /// significant vertices (turning KinkNoiseShare of the largest turn within
        /// ChordCapM / 2) of one sign, consecutive ones at most ChordCapM apart;
        /// a lobe the line leaves and enters by turning back within ChordCapM
        /// (both neighbours turn the other way by KinkNoiseShare of its turn:
        /// legs longer than the cap are lone corners), with such a lobe
        /// beside it, is a wave's: its deviation from the chord of its
        /// inflections (midway to each neighbour; over at most ChordCapM) scores,
        /// under SimplifyEpsM (less the shrink of a lobe turning T on a line hw
        /// off the data line: hw (1 - cos(T / 2))).</summary>
        static void Waves(double[] X, double[] Z, double[] C, double[] TH, double share, double cap, double V0, double eps, double hw, Func<int, bool> Ex, double[] score, byte[] kd)
        {
            int n = C.Length;
            var sig = new bool[n];
            for (int m = 1; m + 1 < n; m++)
            {
                if (Ex(m) || TH[m] == 0) continue;
                double big = 0;
                for (int i = m; i > 0 && C[m] - C[i] <= cap / 2; i--) if (!Ex(i) && Math.Abs(TH[i]) > big) big = Math.Abs(TH[i]);
                for (int i = m + 1; i + 1 < n && C[i] - C[m] <= cap / 2; i++) if (!Ex(i) && Math.Abs(TH[i]) > big) big = Math.Abs(TH[i]);
                if (Math.Abs(TH[m]) >= share * big) sig[m] = true;
            }
            var LA = new List<int>(); var LB = new List<int>(); var LT = new List<double>(); var LG = new List<double>(); var LJ = new List<bool>();
            bool broken = true;
            for (int m = 1; m + 1 < n; m++)
            {
                if (Ex(m)) { broken = true; continue; }
                if (!sig[m]) continue;
                int q = LA.Count - 1;
                if (q >= 0 && !broken && Math.Sign(TH[m]) == Math.Sign(LT[q]) && C[m] - C[LB[q]] <= cap)
                {
                    LB[q] = m; LT[q] += TH[m]; if (Math.Abs(TH[m]) > LG[q]) LG[q] = Math.Abs(TH[m]);
                }
                else { LJ.Add(q >= 0 && !broken && Math.Sign(TH[m]) != Math.Sign(LT[q]) && C[m] - C[LB[q]] <= cap); LA.Add(m); LB.Add(m); LT.Add(TH[m]); LG.Add(Math.Abs(TH[m])); }
                broken = false;
            }
            int L = LA.Count;
            if (L < 4) return;
            bool TurnsBack(int t) => t > 0 && t + 1 < L && LJ[t] && LJ[t + 1] && Math.Abs(LT[t - 1]) >= share * Math.Abs(LT[t]) && Math.Abs(LT[t + 1]) >= share * Math.Abs(LT[t]);
            (double x, double z) At(double s)
            {
                int lo = 0, hi = n - 1;
                while (hi - lo > 1) { int md = (lo + hi) >> 1; if (C[md] <= s) lo = md; else hi = md; }
                double l = C[hi] - C[lo], t = l > 1e-12 ? (s - C[lo]) / l : 0;
                return (X[lo] + (X[hi] - X[lo]) * t, Z[lo] + (Z[hi] - Z[lo]) * t);
            }
            double Dev((double x, double z) p, (double x, double z) q, int lo, int hi, double sgn)
            {
                double cx = q.x - p.x, cz = q.z - p.z, cl = Math.Sqrt(cx * cx + cz * cz);
                if (cl < 1e-9) return 0;
                cx /= cl; cz /= cl;
                double d = 0;
                for (int v = lo; v <= hi; v++) { double e = sgn * (cx * (Z[v] - p.z) - cz * (X[v] - p.x)); if (e > d) d = e; }
                return d;
            }
            for (int t = 1; t + 1 < L; t++)
            {
                if (!TurnsBack(t) || !(TurnsBack(t - 1) || TurnsBack(t + 1))) continue;
                int a = LA[t], b = LB[t]; double sgn = LT[t] > 0 ? -1 : 1;
                // the mean line: the chord of the lobe's inflections, midway between it and each neighbour
                double s0 = (C[LB[t - 1]] + C[a]) / 2, s1 = (C[b] + C[LA[t + 1]]) / 2;
                int lo0 = LB[t - 1] + 1, hi0 = LA[t + 1] - 1;
                double D = Dev(At(s0), At(s1), lo0, hi0, sgn);
                // geometry, not a wobble: the data line's lobe stands SimplifyEpsM off (this line's, less its offset's shrink)
                if (D <= V0 || D + hw * (1 - Math.Cos(Math.Abs(LT[t]) / 2)) >= eps) continue;
                double sc = D;
                if (s1 - s0 > cap)
                {
                    // over at most ChordCapM: the worst sub-chord of the cap's length, one centred on each vertex (kept inside)
                    sc = 0;
                    for (int v = lo0; v <= hi0; v++)
                    {
                        double t0 = Math.Max(s0, Math.Min(s1 - cap, C[v] - cap / 2)), t1 = t0 + cap;
                        int lo = lo0; while (lo <= hi0 && C[lo] <= t0) lo++;
                        int hi = hi0; while (hi >= lo0 && C[hi] >= t1) hi--;
                        if (lo > hi) continue;
                        double e = Dev(At(t0), At(t1), lo, hi, sgn);
                        if (e > sc) sc = e;
                    }
                }
                if (!(sc > V0)) continue;
                double corner = share * LG[t];
                for (int v = a; v <= b; v++) if (Math.Abs(TH[v]) >= corner && sc > score[v]) { score[v] = sc; kd[v] = KindWave; }
            }
        }

        /// <summary>A line in the frame of its arc (lib/kink.mjs arcFrame): its
        /// kept vertices unrolled - X, Z, C, TH as KinkScoresOn takes them - ex
        /// (1: no reference there, or an exempt vertex's segment), raw (the raw
        /// kept vertex nearest each), rep (1: the raw vertex has a kept frame
        /// vertex), zone (1: the raw vertex is on a reference curve), and the
        /// ends whose frame is an extrapolation.</summary>
        sealed class ArcFrameLine { public double[] X, Z, C, TH; public byte[] ex, rep, zone; public int[] raw; public bool open0, open1; }

        /// <summary>THE ARC'S FRAME (lib/kink.mjs arcFrame, whose header is the
        /// rule in full): the line unrolled along the curvature of the
        /// constant-curvature reference it runs on, so that a curve's bumps,
        /// jogs, kinks and waves are judged as a straight's are; null when it
        /// runs on none. The reference at every probe (the kept vertices, and
        /// points Lc / 4 apart along every segment): the least-squares circle of
        /// the vertices sampling the 2 Lc window about it (4 Lc for a coarse
        /// curve), every one within V / 4 of it; clean probes side by side on
        /// curvatures tolK apart hold a step; an island of clean probes shorter
        /// than Lc is a feature's top; each clean curvature the median of its
        /// run's within 2 Lc (those within Lc of a gap left out); a gap takes the
        /// curvature both its sides agree on, or the one a strand end leaves
        /// while the line holds within 4 lone limits of it (the end then OPEN),
        /// else none. The frame's points drop the chord points of a curve and
        /// sample a straight piece in it; each piece is turned back by the
        /// reference's turn so far.</summary>
        static ArcFrameLine ArcFrame(double[] X, double[] Z, double[] C, double[] TH, Func<int, bool> exempt)
        {
            int n = C.Length;
            double Lc = SmoothRules.ChordCapM, V0 = SmoothRules.V, tolFit = V0 / 4, tolK = 2 * V0 / (Lc * Lc);
            if (n < 3) return null;
            double Ltot = C[n - 1];
            if (!(Ltot >= 2 * Lc)) return null;
            // a line whose every 2 Lc stretch turns (net) by under 2 Lc tolK runs on no curve worth a frame of its own
            bool any = false;
            {
                int lo = 1, hi = 1; double sum = 0;
                for (int m = 1; m + 1 < n; m++)
                {
                    while (hi + 1 < n && C[hi] <= C[m] + Lc) sum += TH[hi++];
                    while (C[lo] < C[m] - Lc) sum -= TH[lo++];
                    if (Math.Abs(sum) > 2 * Lc * tolK) { any = true; break; }
                }
            }
            if (!any) return null;
            // the probes: every kept vertex, and points at most Lc / 4 apart along every segment (PV: the vertex, or -1 - segment)
            double q4 = Lc / 4; var PS = new List<double>(); var PV = new List<int>(); var VP = new int[n];
            for (int i = 0; i < n; i++)
            {
                VP[i] = PS.Count; PS.Add(C[i]); PV.Add(i);
                if (i + 1 < n) { double L = C[i + 1] - C[i]; int q = (int)Math.Ceiling(L / q4 - 1e-9); for (int k = 1; k < q; k++) { PS.Add(C[i] + L * k / q); PV.Add(-1 - i); } }
            }
            int SegAt(double a) { int lo = 0, hi = n - 1; while (hi - lo > 1) { int md = (lo + hi) >> 1; if (C[md] <= a) lo = md; else hi = md; } return lo; }
            double Det3(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2) =>
                a0 * (b1 * c2 - b2 * c1) - a1 * (b0 * c2 - b2 * c0) + a2 * (b0 * c1 - b1 * c0);
            // the curvature of the ONE circle the line runs within tolFit of over the 2 Lc window about s (the header); NaN if none
            double CleanK(double s)
            {
                double a = 0, b = 0; int v0 = 0, v1 = -1; bool empty = false, ok = false;
                for (double w = Lc; w <= 2 * Lc; w *= 2)
                {
                    a = s - w; b = s + w;
                    if (a < 0) { b -= a; a = 0; }
                    if (b > Ltot) { a = Math.Max(0, a - (b - Ltot)); b = Ltot; }
                    // the vertices inside the window, and the strand's own end where the window reaches it
                    int ia = SegAt(a), ib = SegAt(b);
                    v0 = a <= 0 ? 0 : ia + 1; v1 = b >= Ltot ? n - 1 : C[ib] < b ? ib : ib - 1;
                    if (w == Lc && v0 > v1) { empty = true; break; }
                    if (v1 - v0 + 1 < 3) continue;
                    double g = Math.Max(C[v0] - a, b - C[v1]);
                    for (int i = v0; i < v1; i++) if (C[i + 1] - C[i] > g) g = C[i + 1] - C[i];
                    if (g <= w / 2) { ok = true; break; }
                }
                if (empty) return 0;
                if (!ok) return double.NaN;
                // the least-squares (Kasa) circle through the window's vertices, about the first of them
                double ox = X[v0], oz = Z[v0];
                double n0 = 0, Sx = 0, Sz = 0, Sxx = 0, Szz = 0, Sxz = 0, Sxr = 0, Szr = 0, Sr = 0, sT = 0;
                for (int i = v0; i <= v1; i++)
                {
                    double x = X[i] - ox, z = Z[i] - oz, q = x * x + z * z;
                    n0++; Sx += x; Sz += z; Sxx += x * x; Szz += z * z; Sxz += x * z; Sxr += x * q; Szr += z * q; Sr += q;
                    if (i > 0 && i < n - 1) sT += TH[i];
                }
                double dd = Det3(Sxx, Sxz, Sx, Sxz, Szz, Sz, Sx, Sz, n0);
                double cx = 0, cz = 0, r = double.PositiveInfinity;
                if (Math.Abs(dd) > 1e-30)
                {
                    double D = Det3(-Sxr, Sxz, Sx, -Szr, Szz, Sz, -Sr, Sz, n0) / dd, E = Det3(Sxx, -Sxr, Sx, Sxz, -Szr, Sz, Sx, -Sr, n0) / dd, F = Det3(Sxx, Sxz, -Sxr, Sxz, Szz, -Szr, Sx, Sz, -Sr) / dd;
                    cx = -D / 2; cz = -E / 2; double r2 = cx * cx + cz * cz - F;
                    if (r2 > 0 && !double.IsInfinity(r2) && !double.IsNaN(r2)) r = Math.Sqrt(r2);
                }
                double lo = 0, hi = 0;
                // flatter than tolK (or no circle): the chord from the first vertex to the last is the reference, a straight
                if (!(r < 1 / tolK))
                {
                    double lx = X[v1] - ox, lz = Z[v1] - oz, ll = Math.Sqrt(lx * lx + lz * lz);
                    if (ll < 1e-9) return double.NaN;
                    for (int i = v0; i <= v1; i++) { double d = ((X[i] - ox) * lz - (Z[i] - oz) * lx) / ll; if (d < lo) lo = d; else if (d > hi) hi = d; }
                    return (hi - lo) / 2 <= tolFit ? 0 : double.NaN;
                }
                lo = double.PositiveInfinity; hi = double.NegativeInfinity;
                for (int i = v0; i <= v1; i++) { double x = X[i] - ox - cx, z = Z[i] - oz - cz, d = Math.Sqrt(x * x + z * z) - r; if (d < lo) lo = d; if (d > hi) hi = d; }
                if ((hi - lo) / 2 > tolFit || sT == 0) return double.NaN;
                return Math.Sign(sT) / (r + (hi + lo) / 2);
            }
            // the reference at every probe (the header)
            int NP = PS.Count; var K = new double[NP]; var okp = new byte[NP];
            for (int p = 0; p < NP; p++) { double k = CleanK(PS[p]); if (!double.IsNaN(k)) { okp[p] = 1; K[p] = Math.Abs(k) <= tolK ? 0 : k; } }
            var cl = (byte[])okp.Clone();
            for (int p = 0; p + 1 < NP; p++) if (okp[p] != 0 && okp[p + 1] != 0 && Math.Abs(K[p] - K[p + 1]) > tolK) { cl[p] = 0; cl[p + 1] = 0; }
            // a reference is a curve the line runs on: clean probes over Lc at least; a shorter island of them is a feature's top
            for (int r0 = 0; r0 < NP;)
            {
                if (cl[r0] == 0) { r0++; continue; }
                int r1 = r0; while (r1 + 1 < NP && cl[r1 + 1] != 0) r1++;
                if (PS[r1] - PS[r0] < Lc) for (int p = r0; p <= r1; p++) cl[p] = 0;
                r0 = r1 + 1;
            }
            var st = new byte[NP]; bool open0 = false, open1 = false;
            // each clean probe's curvature made robust: the median over its clean run's probes within 2 Lc, leaving out those
            // within Lc of an end of the run that a stretch without a reference borders, while others remain
            double Med(List<double> v) { var a = v.ToArray(); Array.Sort(a); return a.Length % 2 == 1 ? a[(a.Length - 1) / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }
            {
                var K2 = new double[NP];
                var v = new List<double>(); var w = new List<double>();
                for (int r0 = 0; r0 < NP;)
                {
                    if (cl[r0] == 0) { r0++; continue; }
                    int r1 = r0; while (r1 + 1 < NP && cl[r1 + 1] != 0) r1++;
                    double lo = r0 > 0 ? PS[r0] + Lc : double.NegativeInfinity, hi = r1 + 1 < NP ? PS[r1] - Lc : double.PositiveInfinity;
                    for (int p = r0; p <= r1; p++)
                    {
                        v.Clear(); w.Clear();
                        for (int q = p; q >= r0 && PS[p] - PS[q] <= 2 * Lc; q--) { w.Add(K[q]); if (PS[q] >= lo && PS[q] <= hi) v.Add(K[q]); }
                        for (int q = p + 1; q <= r1 && PS[q] - PS[p] <= 2 * Lc; q++) { w.Add(K[q]); if (PS[q] >= lo && PS[q] <= hi) v.Add(K[q]); }
                        K2[p] = Med(v.Count > 0 ? v : w);
                    }
                    r0 = r1 + 1;
                }
                for (int p = 0; p < NP; p++) if (cl[p] != 0) K[p] = Math.Abs(K2[p]) <= tolK ? 0 : K2[p];
            }
            for (int p = 0; p < NP;)
            {
                if (cl[p] != 0) { st[p] = 1; p++; continue; }
                int q = p; while (q < NP && cl[q] == 0) q++;
                double kf = double.NaN;
                if (p > 0 && q < NP) { double ka = K[p - 1], kb = K[q]; if (Math.Abs(ka - kb) <= tolK) kf = (ka + kb) / 2; }
                else if (p > 0 || q < NP)
                {
                    // carried on to a strand end only while the line holds within 4 lone limits of its heading
                    double k = p > 0 ? K[p - 1] : K[q], s0 = p > 0 ? PS[p - 1] : PS[q], t = 0, worst = 0;
                    if (p > 0) { for (int i = 1; i + 1 < n; i++) if (C[i] > s0) { t += TH[i]; worst = Math.Max(worst, Math.Abs(t - k * (C[i] - s0))); } }
                    else for (int i = n - 2; i >= 1; i--) if (C[i] < s0) { t += TH[i]; worst = Math.Max(worst, Math.Abs(t - k * (s0 - C[i]))); }
                    if (worst <= 4 * 8 * V0 / Lc) { kf = k; if (p == 0) open0 = true; else open1 = true; }
                }
                for (int r = p; r < q; r++) { if (double.IsNaN(kf)) { st[r] = 0; K[r] = 0; } else { st[r] = 1; K[r] = Math.Abs(kf) <= tolK ? 0 : kf; } }
                p = q;
            }
            bool curved = false;
            for (int p = 0; p < NP; p++) if (K[p] != 0) { curved = true; break; }
            if (!curved) return null;
            // the frame's points: the kept vertices less the chord points of a curve (within V / 50 of the chord from the
            // last point kept to the next vertex), and more along a piece longer than half again its longer neighbour
            bool RawEx(int i) => exempt != null && i > 0 && i < n - 1 && exempt(i);
            int ProbeAt(double s) { int lo = 0, hi = NP - 1; while (hi - lo > 1) { int md = (lo + hi) >> 1; if (PS[md] <= s) lo = md; else hi = md; } return s - PS[lo] <= PS[hi] - s ? lo : hi; }
            double epsD = V0 / 50; var fv = new List<int> { 0 };
            for (int i = 1; i + 1 < n; i++)
            {
                if (K[VP[i]] != 0 && !RawEx(i))
                {
                    int a = fv[fv.Count - 1], b = i + 1; double sx = X[b] - X[a], sz = Z[b] - Z[a], L2 = sx * sx + sz * sz;
                    double t = L2 > 1e-12 ? Math.Max(0, Math.Min(1, ((X[i] - X[a]) * sx + (Z[i] - Z[a]) * sz) / L2)) : 0;
                    double qx = X[a] + sx * t - X[i], qz = Z[a] + sz * t - Z[i];
                    if (qx * qx + qz * qz < epsD * epsD) continue;
                }
                fv.Add(i);
            }
            fv.Add(n - 1);
            var US = new List<double>(); var UX = new List<double>(); var UZ = new List<double>(); var UK = new List<double>(); var UE = new List<byte>(); var UR = new List<int>();
            void Put(double s, int v, int seg)
            {
                int p = v >= 0 ? VP[v] : ProbeAt(s);
                US.Add(s); UK.Add(K[p]);
                if (v >= 0) { UX.Add(X[v]); UZ.Add(Z[v]); UR.Add(v); UE.Add((byte)(RawEx(v) || st[p] == 0 ? 1 : 0)); }
                else
                {
                    double L = C[seg + 1] - C[seg], t = L > 1e-12 ? (s - C[seg]) / L : 0;
                    UX.Add(X[seg] + (X[seg + 1] - X[seg]) * t); UZ.Add(Z[seg] + (Z[seg + 1] - Z[seg]) * t);
                    UR.Add(s - C[seg] <= C[seg + 1] - s ? seg : seg + 1); UE.Add((byte)(RawEx(seg) || RawEx(seg + 1) || st[p] == 0 ? 1 : 0));
                }
            }
            for (int f = 0; f < fv.Count; f++)
            {
                int v = fv[f];
                Put(C[v], v, 0);
                if (f + 1 == fv.Count) break;
                int w = fv[f + 1]; double L = C[w] - C[v], kk = Math.Max(Math.Abs(K[VP[v]]), Math.Abs(K[VP[w]]));
                double nb = Math.Max(f > 0 ? C[v] - C[fv[f - 1]] : 0, f + 2 < fv.Count ? C[fv[f + 2]] - C[w] : 0);
                if (kk == 0 || L <= 1.5 * nb) continue;
                int q = (int)Math.Ceiling(L / Math.Sqrt(4 * V0 / kk) - 1e-9);
                for (int j = 1; j < q; j++) { double s2 = C[v] + L * j / q; Put(s2, -1, SegAt(s2)); }
            }
            // unrolled: each piece turned back by the reference's turn so far (its curvature times each point's Voronoi length)
            int N = US.Count; var FX = new double[N]; var FZ = new double[N];
            FX[0] = UX[0]; FZ[0] = UZ[0];
            double B = 0;
            for (int u = 0; u + 1 < N; u++)
            {
                if (u > 0) B += UK[u] * (US[u + 1] - US[u - 1]) / 2;
                double dx = UX[u + 1] - UX[u], dz = UZ[u + 1] - UZ[u], c = Math.Cos(B), sn = Math.Sin(B);
                FX[u + 1] = FX[u] + dx * c + dz * sn; FZ[u + 1] = FZ[u] - dx * sn + dz * c;
            }
            // its kept vertices, as the line's own are kept (a point with no reference, or on an exempt vertex's segment, always kept)
            double lim = SmoothRules.CollinearDeg * Math.PI / 180;
            double Dst(int p, int q) => Math.Sqrt((FX[q] - FX[p]) * (FX[q] - FX[p]) + (FZ[q] - FZ[p]) * (FZ[q] - FZ[p]));
            var keep = new List<int> { 0 };
            for (int u = 1; u + 1 < N; u++)
            {
                int w = keep[keep.Count - 1];
                if (Dst(w, u) < 1e-3 || Dst(u, u + 1) < 1e-3) continue;
                if (UE[u] == 0 && Math.Abs(Turn(FX[w], FZ[w], FX[u], FZ[u], FX[u + 1], FZ[u + 1])) < lim) continue;
                keep.Add(u);
            }
            keep.Add(N - 1);
            if (keep.Count < 3) return null;
            int m2 = keep.Count;
            var fr = new ArcFrameLine { X = new double[m2], Z = new double[m2], C = new double[m2], TH = new double[m2], ex = new byte[m2], raw = new int[m2], rep = new byte[n], zone = new byte[n], open0 = open0, open1 = open1 };
            for (int i = 0; i < m2; i++) { int u = keep[i]; fr.X[i] = FX[u]; fr.Z[i] = FZ[u]; fr.ex[i] = UE[u]; fr.raw[i] = UR[u]; fr.rep[UR[u]] = 1; if (i > 0) fr.C[i] = fr.C[i - 1] + Dst(keep[i - 1], u); }
            for (int i = 1; i + 1 < m2; i++) fr.TH[i] = Turn(fr.X[i - 1], fr.Z[i - 1], fr.X[i], fr.Z[i], fr.X[i + 1], fr.Z[i + 1]);
            for (int i = 0; i < n; i++) fr.zone[i] = (byte)(st[VP[i]] == 1 && K[VP[i]] != 0 ? 1 : 0);
            return fr;
        }

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
                // reported at the end FirstEnd picks, named after that end's line: one run whichever way the chain runs
                var q = FirstEnd(start, endPt) ? start : endPt; string qid = q.lid ?? lineId;
                if (!truncatedStart && !truncatedEnd && (len < l0 || len > l1))
                    Keep(new Run { check = "C3", lineId = qid, e = q.e, s = q.s, x = q.x, y = q.y, z = q.z, val = len / (on ? SmoothRules.DashM : SmoothRules.DashGapM) - 1, len = len,
                                   e0 = start.e, s0 = start.s, e1 = endPt.e, s1 = endPt.s, what = on ? "dash" : "gap", span = q.span });
                else if (on && (truncatedStart || truncatedEnd) && len < SmoothRules.StubM)
                    Keep(new Run { check = "C3", lineId = qid, e = q.e, s = q.s, x = q.x, y = q.y, z = q.z, val = len / SmoothRules.DashM - 1, len = len,
                                   e0 = start.e, s0 = start.s, e1 = endPt.e, s1 = endPt.s, what = "stub", reportOnly = "a truncated dash under 1 m at a mouth or gore: report-only until WP-17", span = q.span });
            }
            var ts = new List<double>(8);
            for (int pi = 0; pi < pieces.Count; pi++)
            {
                var pts = pieces[pi]; bool joint = pi > 0; Pt prevLast = joint ? pieces[pi - 1][pieces[pi - 1].Count - 1] : default;
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
                            var at = new Pt { x = a.x + (b.x - a.x) * ts[k - 1], y = a.y + (b.y - a.y) * ts[k - 1], z = a.z + (b.z - a.z) * ts[k - 1], e = a.e, s = a.s + (b.s - a.s) * ts[k - 1], span = b.span, lid = a.lid };
                            // at the joint between two pieces: the joint's FirstEnd point (review 8: a dash or gap ending at
                            // a joint was named after the piece the walk reached it on - e11484 one way, e1904 the other)
                            if (joint && i == 1 && k == 1 && FirstEnd(prevLast, a))
                                at = new Pt { x = prevLast.x, y = prevLast.y, z = prevLast.z, e = prevLast.e, s = prevLast.s, span = prevLast.span, lid = prevLast.lid };
                            CloseRun(at, false);
                            truncatedStart = state < 0; state = on; len = 0; start = at;
                        }
                        len += dl;
                    }
                }
            }
            var lastPiece = pieces[pieces.Count - 1];
            CloseRun(lastPiece[lastPiece.Count - 1], true);
        }

        // ---- one chain: A per edge, then strands (B), ends (C2), dashes (C3), joints (B4)
        sealed class PieceRef { public int c; public EdgeData ed; public int k; public List<Pt> pts; public char col; public bool dashed; public string id; public bool startsAtJoint, endsAtJoint, joined; public PieceRef next, prev; }

        static void AnalyzeChain(List<(CityMap.Edge e, bool fwd, bool bendIn)> chain, Dictionary<int, List<Quad>> byEdge, PaveGrid grid, Dictionary<int, FanRec> fansByNode)
        {
            var eds = new List<EdgeData>();
            foreach (var c in chain) { var ed = Extract(c.e, byEdge[c.e.index]); ed.fwd = c.fwd; ed.bendIn = c.bendIn; eds.Add(ed); }
            // D1 leaves out the line's own road: the edges MITRED to it (a bend fan's other arm is not its own road)
            var own = new List<HashSet<int>>();
            for (int c = 0; c < eds.Count; c++)
            {
                if (c == 0 || eds[c].bendIn) own.Add(new HashSet<int>()); else own.Add(own[c - 1]);
                own[c].Add(eds[c].e.index);
            }
            for (int c = 0; c < eds.Count; c++) CheckEdge(eds[c], own[c], grid);
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
            // ---- ribbon edges and the midline. Two ends within V are one strand (a sub-V step is invisible, and a kink
            // at the node must still be judged): the shape checks run on the joined polyline with the step taken out -
            // the new piece moved onto the strand's end (shx, shz: the move applied to the strand's tail). Past V a
            // JUMP and a new strand. Across a BEND FAN the strand runs on across the slab: an edge along the slab's
            // own perimeter, the midline straight from mouth to mouth; no JUMP there, the slab's corner is the fault.
            void JoinStrands(Func<EdgeData, List<List<Pt>>> get, string kindName, Func<int, double> rLim)
            {
                List<Pt> open = null; string openId = null; Func<int, double> openR = rLim; double shx = 0, shz = 0;
                void Finish() { if (open != null) { strands++; ShapeChecks(kindName, openId, open, openR); } open = null; }
                for (int c = 0; c < eds.Count; c++)
                {
                    var ed = eds[c];
                    var pcs = new List<List<Pt>>();
                    foreach (var p in get(ed)) pcs.Add(Orient(ed, p));
                    if (!ed.fwd) pcs.Reverse();
                    // the ribbon's own ends (its trims: 0 at a mitred node); a ring that stops short of them joins nothing
                    double rA = trims.atA[ed.e.index], rB = ed.e.length - trims.atB[ed.e.index];
                    double startS = ed.fwd ? rA : rB, endS = ed.fwd ? rB : rA;
                    for (int i = 0; i < pcs.Count; i++)
                    {
                        var pc = pcs[i];
                        var a = open != null ? open[open.Count - 1] : default; var b = pc[0];
                        bool atJoint = i == 0 && open != null && Math.Abs(pc[0].s - startS) < 1e-3;
                        bool atSeam = i > 0 && open != null && Math.Abs(pc[0].s - a.s) < 1e-3;   // the same section, cut by two tiles
                        if (atJoint && ed.bendIn)
                        {
                            if (kindName == "edge" && fansByNode.TryGetValue(NodeAtStart(ed), out var fan))
                                foreach (var q in FanPath(fan, a.x - shx, a.z - shz, b.x, b.z))
                                    open.Add(new Pt { x = q.x + shx, y = q.y, z = q.z + shz, e = a.e, s = a.s, span = a.span, tag = 'F', side = a.side });
                            foreach (var p in pc) open.Add(Shift(p, shx, shz));
                            continue;
                        }
                        if (atJoint || atSeam)
                        {
                            double dx = a.x - shx - b.x, dz = a.z - shz - b.z, d = Math.Sqrt(dx * dx + dz * dz);   // a's own position
                            if (d <= V)
                            {
                                shx = a.x - b.x; shz = a.z - b.z;
                                open[open.Count - 1] = JointVertex(a, b);   // named after FirstEnd's end, its place kept
                                for (int k = 1; k < pc.Count; k++) open.Add(Shift(pc[k], shx, shz));
                                continue;
                            }
                            // reported at the end FirstEnd picks: the same run whichever way the chain runs
                            if (atJoint)
                            {
                                bool useA = FirstEnd(a, b); var q = useA ? a : b;
                                Keep(new Run { check = "B4", lineId = kindName == "midline" ? "MID" : "R" + q.side, kind = kindName, e = q.e, s = q.s, x = useA ? a.x - shx : b.x, y = q.y, z = useA ? a.z - shz : b.z, val = d, e0 = a.e, s0 = a.s, e1 = b.e, s1 = b.s, node = NodeAtStart(ed), side = q.side });
                            }
                        }
                        Finish();
                        open = new List<Pt>(pc); openId = kindName == "midline" ? "MID" : "R" + pc[0].side; openR = rLim; shx = shz = 0;
                    }
                    if (open != null && Math.Abs(open[open.Count - 1].s - endS) > 1e-3) Finish();
                    if (pcs.Count == 0) Finish();
                }
                Finish();
            }
            JoinStrands(ed => ed.fwd ? ed.ribL : ed.ribR, "edge", ei => SmoothRules.InnerEdgeMinRM);
            JoinStrands(ed => ed.fwd ? ed.ribR : ed.ribL, "edge", ei => SmoothRules.InnerEdgeMinRM);
            JoinStrands(ed => ed.mid, "midline", ei => SmoothRules.RMinFor(SmoothRules.ClassOf(map.edges[ei].cls, map.edges[ei].link)));

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
                        // paint pairs across MITRED joints only: at a bend fan's mouths every line ends (C2)
                        P.Add(new PieceRef { c = c, ed = ed, k = k, pts = pts, col = ed.lay.runs[k].col, dashed = ed.lay.runs[k].dashed, id = PlanIdOf(ed.lay, k),
                                             startsAtJoint = c > 0 && !ed.bendIn && AtStart(f0), endsAtJoint = c + 1 < eds.Count && !eds[c + 1].bendIn && AtEnd(f1) });
                    }
                // one line cut by a tile seam inside the edge: two pieces meeting at the same section, one line when within V
                // (a seam past SeamM is B4s already, in Extract)
                for (int k = 0; k < ed.lay.runs.Count; k++)
                {
                    var mine = P.FindAll(p => p.ed == ed && p.k == k);
                    for (int i = 0; i + 1 < mine.Count; i++)
                    {
                        // chain order: raw piece order going forward, reversed going backward
                        var u = ed.fwd ? mine[i] : mine[i + 1]; var w = ed.fwd ? mine[i + 1] : mine[i];
                        Pt ue = u.pts[u.pts.Count - 1], ws = w.pts[0];
                        if (u.next != null || w.prev != null || (ue.tag != 'A' && ue.tag != 'B') || (ws.tag != 'A' && ws.tag != 'B') || Math.Abs(ue.s - ws.s) > 1e-3) continue;
                        if (Dist(ue, ws) <= V) { u.next = w; w.prev = u; w.joined = true; }
                    }
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
                pairs.Sort((p, q) => p.d != q.d ? p.d.CompareTo(q.d) : PairOrder(p.a, p.b, q.a, q.b));
                foreach (var (a, b, d) in pairs)
                {
                    if (a.next != null || b.prev != null) continue;
                    a.next = b; b.prev = a;
                    if (d <= V) b.joined = true;   // within V: one strand, the step taken out
                    else
                    {
                        var pb = b.pts[0]; var pa = a.pts[a.pts.Count - 1];
                        // at the end FirstEnd picks, named after that end's line: one run, whichever way the chain runs
                        bool useA = FirstEnd(pa, pb); var q = useA ? pa : pb;
                        Keep(new Run { check = "B4", lineId = useA ? a.id : b.id, kind = "paint", e = q.e, s = q.s, x = q.x, y = q.y, z = q.z, val = d, e0 = pa.e, s0 = pa.s, e1 = pb.e, s1 = pb.s, node = NodeAtStart(b.ed) });
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
                    if (strand != null && p.joined)
                    {
                        // the next piece moved onto the strand's end: the (sub-V) step taken out, the shape kept
                        var a = strand[strand.Count - 1]; double shx = a.x - p.pts[0].x, shz = a.z - p.pts[0].z;
                        strand[strand.Count - 1] = JointVertex(a, p.pts[0]);   // named after FirstEnd's end, its place kept
                        for (int k = 1; k < p.pts.Count; k++) strand.Add(Shift(p.pts[k], shx, shz));
                    }
                    else { if (strand != null) { strands++; ShapeChecks("paint", head.id, strand, null); } strand = new List<Pt>(p.pts); }
                }
                if (strand != null) { strands++; ShapeChecks("paint", head.id, strand, null); }
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
            bool bendEnd = node >= 0 && IsBendFan(node);
            if (node >= 0 && trims.patch[node] && !bendEnd) legit = "fan mouth";   // a bend fan's mouths are no legitimate end (plan A2)
            else if (node >= 0 && map.nodeEdges[node].Count == 1) legit = "dead end";
            else if (node >= 0 && ((trims.branchA[e.index] >= 0 && node == e.a) || (trims.branchB[e.index] >= 0 && node == e.b))) legit = "branch mouth";
            // a gore NOSE only (gate spec 4.3's closed list): a collapsed section - the branch wholly inside its host - within
            // GoreNoseM. A line that starts or stops along a branch's attach arc, where the branch is only clipped, ends
            // mid-road: the crop drew it there, and the markings layer (WP-17/18b) ends it at the nose
            else if (SectionNear(ed, pt.s, FCollapsed)) legit = "gore nose";
            int j = lay.runPlan[p.k]; var plan = j >= 0 ? lay.plan[j] : null;
            if (legit == null && plan != null)
            {
                // the plan drops or adds the line here; or at a joint the plan across carries fewer lines of this colour
                // and pattern (a lane drop: one must end), or none within a lane (MatchM). A line that continues across
                // the node was paired in AnalyzeChain (a JUMP) and never gets here.
                double s0 = Math.Max(sMin, pt.s - SmoothRules.FanMouthM), s1 = Math.Min(sMax, pt.s + SmoothRules.FanMouthM);
                if (PlanExists(plan, HwD(e, s0)) != PlanExists(plan, HwD(e, s1))) legit = "plan lane drop";
                else if (node >= 0 && JointAt(e, node) >= 0)
                {
                    var o = map.edges[JointAt(e, node)];
                    var op = PlanOf(RoadProfiles.All[o.profile]);
                    double lat = Sd(RefOf(e), pt.x, pt.z, pt.s);
                    bool same = (e.b == node) == (o.a == node);
                    double so = o.a == node ? 0 : o.length, hwo = HwD(o, so), hwn = HwD(e, node == e.a ? 0 : e.length);
                    int nOther = 0, nThis = 0; bool has = false;
                    foreach (var qq in op)
                        if (qq.col == p.col && qq.dashed == p.dashed && PlanExists(qq, hwo))
                        {
                            nOther++;
                            if (Math.Abs((same ? 1 : -1) * PlanOff(qq, o, so, hwo) - lat) <= SmoothRules.MatchM) has = true;
                        }
                    foreach (var qq in lay.plan) if (qq.col == p.col && qq.dashed == p.dashed && PlanExists(qq, hwn)) nThis++;
                    if (nOther < nThis) legit = "plan lane drop at a node";
                    else if (!has) legit = "plan line ends at a node";
                }
            }
            double err = plan != null ? Sd(RefOf(e), pt.x, pt.z, pt.s) - (PlanOff(plan, e, pt.s, HwD(e, pt.s)) + lay.runQc[p.k]) : 0;
            bool inArc = legit == null && SectionNear(ed, pt.s, (ushort)(FClipL | FClipR));
            if (legit == null || Math.Abs(err) > SmoothRules.StrayM)
                Keep(new Run { check = "C2", lineId = p.id, e = e.index, s = pt.s, x = pt.x, y = pt.y, z = pt.z, val = Math.Max(1, Math.Abs(err) / SmoothRules.StrayM),
                               e0 = e.index, s0 = pt.s, e1 = e.index, s1 = pt.s, span = pt.span,
                               what = (start ? "start " : "end ") + (legit != null ? $"at a {legit} but {Math.Abs(err):0.00} m off its plan" : bendEnd ? "at a bend fan's mouth (a 2-arm node drawn as a junction slab)"
                                      : inArc ? "mid-road, along a branch attach arc (clipped; no gore nose within GoreNoseM)" : "mid-road") });
        }
        /// <summary>A section of the edge within GoreNoseM of s carrying any of these tap flags.</summary>
        static bool SectionNear(EdgeData ed, double s, ushort flags)
        {
            foreach (var q in ed.quads)
                if (((q.fA & flags) != 0 && Math.Abs(q.sA - s) <= SmoothRules.GoreNoseM) || ((q.fB & flags) != 0 && Math.Abs(q.sB - s) <= SmoothRules.GoreNoseM)) return true;
            return false;
        }

        /// <summary>B2 / B3 on a fan's curb returns: each run of perimeter
        /// chords between two road mouths (ratchet until WP-19's arcs).</summary>
        static void FanChecks(FanRec f)
        {
            int n = f.corners.Length;
            if (n < 3) return;
            if (IsBendFan(f.node)) return;   // judged across, as the chain's corner (JoinStrands), never as a curb return
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
                cur.Add(new Pt { x = c.x, y = c.y, z = c.z, e = eAny, s = 0, span = i, tag = 'F' });
                bool mouthNext = (f.mouths >> i & 1UL) != 0;
                if (mouthNext || k == n)
                {
                    if (cur.Count >= 3) { strands++; ShapeChecks("fan", "FAN", cur, ei => rLim, f.node); }
                    cur = new List<Pt>();
                }
            }
        }

        // ================================================================
        //  Keys, causes, ranking, report
        // ================================================================

        static readonly string[] CheckOrder = { "A0", "A1", "A2", "A3", "A4", "A5", "A5b", "B1", "B2", "B3", "B4", "B4s", "C1", "C2", "C3", "D1", "E1" };

        static void FinishRuns()
        {
            foreach (var r in runs)
            {
                var e = map.edges[r.e];
                if (r.kind == "edge" && r.side != ' ' && r.side != '\0') r.lineId = "R" + r.side;
                r.pinned = pinnedWays.Contains(e.wayId);
                r.key = r.kind == "fan" ? $"{e.wayId}:n{r.node}:{r.check}:FAN" : $"{e.wayId}:{KeyStep(e.index, r.s).ToString(Inv)}:{r.check}:{r.lineId}";
                double limit = r.check == "A4" ? SmoothRules.LineWidthTol : r.check == "A5" || r.check == "A5b" ? SmoothRules.StrayM : r.check == "B3" ? r.rLimit
                    : r.check == "C1" ? SmoothRules.GapM : r.check == "C2" ? 1 : r.check == "C3" ? SmoothRules.DashTol : r.check == "D1" ? SmoothRules.CrossM
                    : r.check == "B4s" ? SmoothRules.SeamM : V;
                double RatioOf(double v) => r.check == "B3" ? limit / Math.Max(1e-6, v) : r.check == "C2" ? v : Math.Abs(v) / limit;
                r.ratio = RatioOf(r.val);
                // the ratchet keys (linegate.mjs): (way, round(s on way / KeyStepM), check, line) for EVERY bucket the run's
                // bad samples touch, each with its own worst ratio and bad length; on a fan (way, n<node>c<corner>, check,
                // FAN); a point run (B4, B4s, C2, C3) has its one (length 0), a gap (C1) every bucket it spans (the gap's length)
                // (a ribbon edge's line is the side of the edge each bucket lies on: a chain runs some of its edges backwards,
                // so one strand is RR on one edge and RL on the next; labelled by the run's worst sample, a key moved between
                // RL and RR - vanishing from the ratchet - whenever that worst moved to an edge the chain runs the other way)
                r.kk = new List<string>(); r.kq = new List<double>(); r.kl = new List<double>();
                // (and a painted line's bucket is named after the line its own samples lie on: Run.bn)
                // (a B3 bucket against its own samples' limit: Run.br)
                void AddKey(long bid, double v, double l, char side, string name = null, double lim = 0)
                {
                    string line = r.kind == "edge" && (side == 'L' || side == 'R') ? "R" + side : name ?? r.lineId;
                    r.kk.Add(bid < 0 ? $"{e.wayId}:n{(-bid - 1) / 4096}c{(-bid - 1) % 4096}:{r.check}:FAN" : $"{bid / 1048576L}:{(bid % 1048576L).ToString(Inv)}:{r.check}:{line}");
                    r.kq.Add(r.check == "B3" && lim > 0 ? lim / Math.Max(1e-6, v) : RatioOf(v)); r.kl.Add(l);
                }
                if (r.bk != null) for (int i = 0; i < r.bk.Count; i++) AddKey(r.bk[i], r.bv[i], r.bl[i], r.bs[i], r.bn != null ? r.bn[i] : null, r.br != null ? r.br[i] : 0);
                else if (r.check == "C1") for (long b = Bucket(r.e, Math.Min(r.s0, r.s1)); b <= Bucket(r.e, Math.Max(r.s0, r.s1)); b++) AddKey(b, r.val, r.len, ' ');
                else AddKey(Bucket(r.e, r.s), r.val, 0, ' ');
                r.bk = null; r.bv = null; r.bl = null; r.bs = null; r.bn = null; r.br = null;
                // causes: the tap's flags and the data near the worst sample
                var causes = new List<string>();
                double lo = r.check == "C1" ? Math.Min(r.s0, r.s1) - 1 : r.s - 1, hi = r.check == "C1" ? Math.Max(r.s0, r.s1) + 1 : r.s + 1;
                bool taper = (trims.taperA[e.index] > 0 && r.s <= trims.taperA[e.index] + 1) || (trims.taperB[e.index] > 0 && e.length - r.s <= trims.taperB[e.index] + 1);
                if (taper) causes.Add("TAPER");
                if (r.tag == 'D' || (taper && r.tag == 'I')) causes.Add("DIAGONAL");
                for (int i = 1; i + 1 < e.s.Length; i++) if (e.s[i] >= lo && e.s[i] <= hi) { causes.Add("VERTEX"); break; }
                if (r.node >= 0 || ((r.s <= 1 || e.length - r.s <= 1) && (trims.mitre[e.a] || trims.mitre[e.b]))) causes.Add(r.kind == "fan" ? "FAN" : "MITRE");
                if (r.check == "B4s") causes.Add("SEAM");
                // at a bend fan's mouth: the slab's corner, a DATA fault (WP-11 fillets every 2-arm node)
                bool atBend = (IsBendFan(e.a) && r.s - trims.atA[e.index] <= SmoothRules.GoreNoseM) || (IsBendFan(e.b) && e.length - trims.atB[e.index] - r.s <= SmoothRules.GoreNoseM);
                if (atBend) causes.Add("BEND-FAN");
                r.cause = causes.Count > 0 ? string.Join("+", causes) : "-";
                r.data = r.kind == "fan" ? "BUILDER" : atBend || DataKinkNear(e, r.s) ? "DATA" : "BUILDER";
                r.exposure = 1;
                if (onRoute.Contains(e.index)) r.exposure = SmoothRules.ExposureRoute;
                else foreach (var sp in refSpots) if ((sp.x - r.x) * (sp.x - r.x) + (sp.y - r.z) * (sp.y - r.z) <= SmoothRules.RefSpotReachM * SmoothRules.RefSpotReachM) { r.exposure = SmoothRules.ExposureRefSpot; break; }
                r.score = Math.Min(r.ratio, SmoothRules.RankRatioCap) * SmoothRules.WeightFor(SmoothRules.ClassOf(e.cls, e.link)) * r.exposure;
            }
            // A5 / A5b are runs of at least StrayRunM
            runs.RemoveAll(r => (r.check == "A5" || r.check == "A5b") && r.len < SmoothRules.StrayRunM);
        }

        sealed class Tally
        {
            public int runs, data, builder, roRuns; public double metres, worst, worstRatio, roMetres;
            /// <summary>Every key with its worst ratio and bad length (summed over its runs).</summary>
            public readonly Dictionary<string, (double q, double l)> keys = new Dictionary<string, (double, double)>();
            /// <summary>Runs per analysed tile (TileKey).</summary>
            public readonly Dictionary<long, int> tiles = new Dictionary<long, int>();
        }

        static Dictionary<string, Tally> Summarize()
        {
            var t = new Dictionary<string, Tally>();
            foreach (var id in CheckOrder) t[id] = new Tally();
            foreach (var r in runs)
            {
                if (!t.TryGetValue(r.check, out var s)) continue;
                if (r.reportOnly != null) { s.roRuns++; s.roMetres += r.len; continue; }
                s.runs++; s.metres += r.len;
                for (int i = 0; i < r.kk.Count; i++)
                    s.keys[r.kk[i]] = s.keys.TryGetValue(r.kk[i], out var o) ? (Math.Max(o.q, r.kq[i]), o.l + r.kl[i]) : (r.kq[i], r.kl[i]);
                s.tiles.TryGetValue(r.tile, out int n); s.tiles[r.tile] = n + 1;
                if (r.ratio > s.worstRatio) { s.worstRatio = r.ratio; s.worst = r.val; }
                if (r.data == "DATA") s.data++; else s.builder++;
            }
            // A0 TEXTURE: the textures this run read, against the plan
            var a0 = t["A0"];
            foreach (var (what, val, ratio) in textureFails)
            {
                a0.runs++; a0.builder++; a0.keys["tex:" + what] = (ratio, 0);
                if (ratio > a0.worstRatio) { a0.worstRatio = ratio; a0.worst = val; }
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
        /// baseline compare; the Check lines (report-only in the first cycle).
        /// Returns 0 PASS, 1 FAIL. A STALE baseline (measured on other inputs)
        /// FAILS - a failing Check line, counted in CITY AUDIT: N FAILURES, so
        /// city-cycle.ps1 and verify.ps1 exit 1 - because a stale ratchet
        /// cannot tell a regression from the move, and a gate that went quiet
        /// there would pass the regression that arrives with it. The way out is
        /// the explicit re-record (PSX_SMOOTH_WRITE_BASELINE=1 on a FULL run,
        /// tools\city-smooth.ps1 -WriteBaseline), which logs the BEFORE -> AFTER
        /// numbers for its commit - and is REFUSED (nothing written, FAIL) when
        /// it would loosen the gate: only the gate moved and keys vanished or
        /// score lower, the gate moved together with the data (two steps), or
        /// a check state or a pinned way loosened (Loosenings;
        /// PSX_SMOOTH_ALLOW_LOOSEN=1, -AllowLoosen, records it anyway, listed). A
        /// check gating looser, or a way pinned less, than when the baseline was
        /// recorded fails too, stale or not.</summary>
        static int Report(Action<string> line, Action<bool, string, object> check)
        {
            FinishRuns();
            var sum = Summarize();
            string root = Directory.GetParent(Application.dataPath).FullName;
            uint graph = map.graphHash;
            var inputs = InputsNow();
            var baseline = ReadBaseline(out string basePath);
            BaselineEntry baseE = null;
            if (baseline != null) baseline.TryGetValue("mesh", out baseE);
            var stale = baseE != null ? InputsDiff(baseE.inputs, inputs) : new List<string>();
            bool isStale = stale.Count > 0;
            bool writing = Environment.GetEnvironmentVariable("PSX_SMOOTH_WRITE_BASELINE") == "1";
            var demoted = baseE != null ? Demotions(baseE) : new List<string>();
            int bendFans = 0;
            for (int n = 0; n < map.nodes.Length; n++) if (IsBendFan(n)) bendFans++;
            var sb = new StringBuilder();
            sb.AppendLine($"SMOOTHNESS GATE  V {V * 100:0.0} cm (1 px at {SmoothRules.PixelAtM:0} m; {SmoothRules.FramebufferLines} lines, fov {SmoothRules.FovDeg:0})  mode {mode}{(band >= 0 ? $" band {band}/{SmoothRules.BandCount}" : "")}  graph {graph:x8}");
            sb.AppendLine($"  {analysed.Count} tiles, {ribbonKm:0} km of ribbon, {lineKm:0} km of line, {strands} strands, {Time.realtimeSinceStartup - t0:0.0} s{(tapNotes > 0 ? $"; {tapNotes} tap records unreadable" : "")}");
            sb.AppendLine($"  {(SmoothRules.ReportOnly ? "REPORT-ONLY cycle (SmoothRules.ReportOnly)" : "gating")}; creek pin {(SmoothRules.PinActive ? "ACTIVE" : "recorded, not yet enforced")}; baseline {(baseE != null ? basePath + (isStale ? " - STALE" : "") : "none")}");
            sb.AppendLine($"  bend fans (2-arm nodes drawn as junction slabs; plan A2: never legitimate): {bendFans} in the city - each judged across (B2/B3), its mouths no legitimate end (C2)");
            if (seatNote != null) sb.AppendLine("  NOTE " + seatNote);
            if (isStale)
            {
                string st = $"the baseline ({baseE.date}) was measured on other inputs - {string.Join("; ", stale)}. " +
                            (writing ? "Re-recording it now (the BEFORE -> AFTER numbers below go in the commit)"
                                     : "A stale ratchet cannot tell a regression from the move, so this FAILS until the explicit re-record: tools\\city-smooth.ps1 -Mode FULL -WriteBaseline, which logs the before and after numbers for the commit; the comparison below is for that commit");
                sb.AppendLine("  STALE: " + st);
                if (writing || ReportOnly) line?.Invoke("  info smoothness baseline STALE: " + st);
                else check?.Invoke(false, "smoothness baseline measured on today's inputs (graph, container sections, rules, road PNGs, DEM, the gate's code)", "STALE - " + st);
            }
            if (demoted.Count > 0)
            {
                sb.AppendLine("  DEMOTED: " + string.Join(", ", demoted));
                if (ReportOnly) line?.Invoke("  info smoothness check states loosened since the baseline: " + string.Join(", ", demoted));
                else check?.Invoke(false, "smoothness check states never loosen (against the baseline's)", string.Join(", ", demoted));
            }
            bool refused = false;
            if (writing)
            {
                foreach (var l in BeforeAfter(sum, baseE, inputs)) { sb.AppendLine(l); line?.Invoke("  info " + l); }
                var loose = baseE != null ? Loosenings(sum, baseE, inputs) : new List<string>();
                bool allow = Environment.GetEnvironmentVariable("PSX_SMOOTH_ALLOW_LOOSEN") == "1";
                if (loose.Count > 0)
                {
                    sb.AppendLine("LOOSENING (a re-record would loosen the gate):");
                    foreach (var l in loose) sb.AppendLine(l);
                    refused = !allow;
                    string msg = refused ? "REFUSED - nothing was written: fix the gate, or re-record in two steps (the gate change on the old data first, then the data), or re-run with -AllowLoosen for a deliberate, signed-off change and put the list in the commit"
                                         : "recorded anyway (PSX_SMOOTH_ALLOW_LOOSEN=1): put the list in the commit";
                    sb.AppendLine("  " + msg);
                    check?.Invoke(!refused, "smoothness re-record loosens nothing (keys, check states, pinned ways)", string.Join("; ", loose).Trim() + " - " + msg);
                }
            }
            sb.AppendLine("  check          state    runs     metres   worst        x limit  data/builder   baseline");
            bool allOk = true, zeroOk = true;
            foreach (var c in SmoothRules.Checks)
            {
                var s = sum[c.id];
                BaselineCheck b = null; baseE?.checks.TryGetValue(c.id, out b);
                // per key: new, worse than its own baseline ratio, or longer than its own baseline length
                int fresh = 0, worseKeys = 0, longerKeys = 0;
                if (b != null && b.keys != null)
                    foreach (var kv in s.keys)
                    {
                        if (!b.keys.TryGetValue(Fnv1a(kv.Key), out var q)) { fresh++; continue; }
                        if (kv.Value.q > Unquant(q.q) * (1 + 1e-9)) worseKeys++;
                        if (QuantLen(kv.Value.l) > q.l) longerKeys++;
                    }
                // runs: FULL against the whole baseline; FAST only against the baseline's runs in the tiles it analysed
                // (and FAST leaves metres to the keys)
                int baseRuns = b?.runs ?? 0;
                if (b != null && mode != "FULL" && b.tiles != null)
                {
                    baseRuns = 0;
                    foreach (long tk in analysed) if (b.tiles.TryGetValue(TileId(tk), out int n)) baseRuns += n;
                }
                bool longer = b != null && mode == "FULL" && s.metres > b.metres + 0.05 + 1e-6;
                bool ok = c.state == SmoothRules.State.Report ? true
                        : c.state == SmoothRules.State.Zero ? s.runs == 0
                        : b != null && b.keys != null && fresh == 0 && worseKeys == 0 && longerKeys == 0 && !longer && s.runs <= baseRuns && s.worstRatio <= b.worstRatio + 1e-3;
                if (!ok) allOk = false;
                if (!ok && c.state == SmoothRules.State.Zero) zeroOk = false;
                string bl = b != null ? $"{baseRuns} runs{(mode != "FULL" ? " in these tiles" : $", {b.metres:0} m{(longer ? " LONGER" : "")}")}, worst x{b.worstRatio:0.0}, {fresh} new / {worseKeys} worse / {longerKeys} longer keys of {s.keys.Count}" : "-";
                sb.AppendLine($"  {(c.id + " " + c.name).PadRight(14)} {c.state.ToString().ToUpperInvariant().PadRight(8)} {s.runs,6} {s.metres,10:0}   {Unit(c.id, s.worst).PadRight(12)} {s.worstRatio,6:0.0}   {(s.data + "/" + s.builder).PadRight(14)} {bl}");
                if (s.roRuns > 0) sb.AppendLine($"  {"",-14} {"REPORT",-8} {s.roRuns,6} {s.roMetres,10:0}   {(c.id == "D1" ? "inside a branch attach arc (its seat's pieces + MergeMarginM; report-only until WP-18b)" : "truncated dash stubs at mouths and gores (report-only until WP-17)")}");
                string what = $"smoothness {c.id} {c.name}: {c.what}";
                string detail = $"{s.runs} runs, worst {Unit(c.id, s.worst)} (x{s.worstRatio:0.0}){(b != null ? $" (baseline {baseRuns}, {c.state.ToString().ToUpperInvariant()}: {fresh} new, {worseKeys} worse, {longerKeys} longer keys{(longer ? ", more metres" : "")})" : c.state == SmoothRules.State.Zero ? " (ZERO)" : " (no baseline)")}";
                // against a STALE baseline (it FAILS above) or while re-recording, a ratchet comparison is for the commit, not a verdict
                bool forCommit = (isStale || writing) && c.state == SmoothRules.State.Ratchet;
                if (SmoothRules.ReportOnly || c.state == SmoothRules.State.Report || forCommit) line?.Invoke($"  info {what} - {detail}{(forCommit ? (writing ? " [re-recording]" : " [baseline STALE: compared for the re-record]") : "")}");
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
            var csv = new StringBuilder("check,lineId,key,way,edge,name,s,s0,s1,len,val,ratio,x,y,z,cause,data,pinned,reportOnly,what,e0,e1\n");
            foreach (var r in runs)
            {
                var e = map.edges[r.e];
                csv.Append(r.check).Append(',').Append(r.lineId).Append(',').Append(r.key).Append(',').Append(e.wayId).Append(',').Append(r.e).Append(",\"")
                   .Append((e.name ?? "").Replace("\"", "\"\"")).Append("\",").Append(F(r.s)).Append(',').Append(F(r.s0)).Append(',').Append(F(r.s1)).Append(',').Append(F(r.len)).Append(',')
                   .Append(F(r.val)).Append(',').Append(F(r.ratio)).Append(',').Append(F(r.x)).Append(',').Append(F(r.y)).Append(',').Append(F(r.z)).Append(',').Append(r.cause).Append(',').Append(r.data)
                   .Append(',').Append(r.pinned ? 1 : 0).Append(",\"").Append(r.reportOnly ?? "").Append("\",\"").Append(r.what ?? "").Append("\",")
                   .Append(r.e0).Append(',').Append(r.e1).Append('\n');
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
            if (writing && !refused) WriteBaseline(sum, inputs, Path.Combine(root, "smooth_baseline.json"));
            line?.Invoke($"  smoothness gate: {analysed.Count} tiles, {runs.Count} runs, city_smooth.txt written ({Time.realtimeSinceStartup - t0:0.0} s)");
            Debug.Log("[CitySmooth] " + sb.ToString().Split('\n')[0]);
            // a ZERO check, the pin, a loosened check state or a STALE baseline fails; while re-recording (the explicit,
            // logged step) the ratchet comparisons are for the commit
            bool pinFails = SmoothRules.PinActive && pinRuns > 0;
            return pinFails || !zeroOk || refused || (demoted.Count > 0 && !writing) || (isStale && !writing) ? 1 : writing || allOk ? 0 : 1;
        }

        /// <summary>The checks gating looser now than when the entry was
        /// recorded (ZERO -> RATCHET -> REPORT, or gone): gatebase.mjs demotions.</summary>
        static List<string> Demotions(BaselineEntry b)
        {
            var d = new List<string>();
            if (b.states.Count == 0) { d.Add("the baseline records no check states (it predates the demotion check): re-record it"); return d; }
            int Strict(string st) => st == "ZERO" ? 2 : st == "RATCHET" ? 1 : 0;
            foreach (var kv in b.states)
            {
                SmoothRules.CheckDef now = default; bool found = false;
                foreach (var c in SmoothRules.Checks) if (c.id == kv.Key) { now = c; found = true; break; }
                string ns = found ? now.state.ToString().ToUpperInvariant() : null;
                if (!found) d.Add($"{kv.Key} {kv.Value} -> (removed)");
                else if (Strict(ns) < Strict(kv.Value)) d.Add($"{kv.Key} {kv.Value} -> {ns}");
            }
            foreach (uint w in b.pinned) if (Array.IndexOf(SmoothRules.PinnedWays, w) < 0) d.Add($"pin: way {w} dropped");
            return d;
        }

        /// <summary>What a re-record would LOOSEN (gatebase.mjs loosenings;
        /// review 5: re-recording after a gate change dropped 763 of the city's
        /// keys, hidden inside a total that grew): check states or pinned ways
        /// loosened; when the GATE moved ('rules' or 'code': SmoothRules.cs,
        /// this file and the tap) and the data did not (graph, container
        /// sections, PNGs, DEM, model), every key that vanished or now scores
        /// lower - only the gate can do that; and when the gate moved TOGETHER
        /// with the data (review 6: a loosening then rode along with a re-export
        /// or the charlotte merge unseen), the move itself: re-record in two
        /// steps, the gate change on the old data first. A move of the data
        /// alone, or of the builder (CityMeshes beyond the tap is in no input:
        /// a builder fix drops keys), compares no keys and records freely; a
        /// builder fix in the same commit as a gate change is two steps too.
        /// An entry recorded before ribbon-edge keys took their bucket's side
        /// (keySides) may hold a key under the edge's other side: the same run,
        /// not a vanished key.</summary>
        static List<string> Loosenings(Dictionary<string, Tally> sum, BaselineEntry b, Dictionary<string, string> inputs)
        {
            var o = new List<string>();
            foreach (var d in Demotions(b)) o.Add("  " + d);
            var gate = new List<string>(); var data = new List<string>();
            var keys = new SortedSet<string>(b.inputs.Keys, StringComparer.Ordinal); keys.UnionWith(inputs.Keys);
            foreach (var k in keys)
            {
                b.inputs.TryGetValue(k, out var x); inputs.TryGetValue(k, out var y);
                if (x == y) continue;
                if (k == "rules" || k == "code") gate.Add(k == "rules" ? "SmoothRules.cs rules" : "the gate's code");
                else data.Add(k.StartsWith("sections") ? "container section " + k.Substring(Math.Min(k.Length, 9)) : k == "paint" ? "road PNGs" : k == "dem" ? "the DEM" : k);
            }
            if (gate.Count > 0 && data.Count > 0)
                o.Add($"  the gate ({string.Join(", ", gate)}) moved TOGETHER with the data ({string.Join(", ", data)}): its keys cannot be compared across the data move, so a loosening would ride along unseen - re-record in two steps, the gate change on the old data first, then the data");
            if (gate.Count == 0 || data.Count > 0) return o;
            foreach (var c in SmoothRules.Checks)
            {
                if (!sum.TryGetValue(c.id, out var s) || !b.checks.TryGetValue(c.id, out var bc) || bc.keys == null) continue;
                var now = new Dictionary<uint, int>();
                void Put(uint h, int q) { now[h] = now.TryGetValue(h, out int p) ? Math.Max(p, q) : q; }
                foreach (var kv in s.keys)
                {
                    int q = QuantRatio(kv.Value.q);
                    Put(Fnv1a(kv.Key), q);
                    if (!b.keySides && (kv.Key.EndsWith(":RL") || kv.Key.EndsWith(":RR")))
                        Put(Fnv1a(kv.Key.Substring(0, kv.Key.Length - 1) + (kv.Key.EndsWith("L") ? "R" : "L")), q);
                }
                int gone = 0, lower = 0;
                foreach (var kv in bc.keys) { if (!now.TryGetValue(kv.Key, out int x)) gone++; else if (x < kv.Value.q) lower++; }
                if (gone > 0 || lower > 0) o.Add($"  {c.id} {c.name}: {gone} keys vanished, {lower} keys score lower (the data did not move: the gate loosened)");
            }
            return o;
        }

        /// <summary>Distinct key hashes (what a baseline stores as "keys").</summary>
        static int HashCount(Dictionary<string, (double q, double l)> keys)
        {
            var h = new HashSet<uint>();
            foreach (var k in keys.Keys) h.Add(Fnv1a(k));
            return h.Count;
        }

        /// <summary>The before and after numbers of a re-record (gatebase.mjs
        /// beforeAfter): per check, the replaced entry's runs, metres, worst and
        /// keys against this run's, and what moved in the inputs.</summary>
        static List<string> BeforeAfter(Dictionary<string, Tally> sum, BaselineEntry b, Dictionary<string, string> inputs)
        {
            var o = new List<string>();
            if (b == null) { o.Add("BEFORE: no mesh entry (a first record)"); return o; }
            var moved = InputsDiff(b.inputs, inputs); var dem = b.states.Count > 0 ? Demotions(b) : new List<string>();
            o.Add($"BEFORE ({b.date}) -> AFTER (this run); inputs moved: {(moved.Count > 0 ? string.Join("; ", moved) : "none")}{(dem.Count > 0 ? "; check states loosened: " + string.Join(", ", dem) : "")}");
            o.Add("  check          runs before -> after        metres before -> after     worst x before -> after     keys before -> after");
            string D(object x, object y) => $"{Convert.ToString(x, Inv),9} -> {Convert.ToString(y, Inv),-9}";
            foreach (var c in SmoothRules.Checks)
            {
                var s = sum[c.id]; b.checks.TryGetValue(c.id, out var bc);
                o.Add($"  {(c.id + " " + c.name).PadRight(14)} {D(bc?.runs ?? 0, s.runs)}  {D(Math.Round(bc?.metres ?? 0), Math.Round(Math.Round(s.metres * 10) / 10))}  {D(Math.Round((bc?.worstRatio ?? 0) * 10) / 10, Math.Round(s.worstRatio * 10) / 10)}  {D(bc?.keys?.Count ?? 0, HashCount(s.keys))}");
            }
            return o;
        }
        static string F(double v) => v.ToString("0.###", Inv);

        // ================================================================
        //  Baseline: per graph hash, per-check numbers and FNV-1a key hashes
        //  (packed as tools/city/linecheck.mjs packs them)
        // ================================================================

        sealed class BaselineCheck { public int runs; public double metres, worstRatio; public Dictionary<uint, (int q, int l)> keys; public Dictionary<uint, int> tiles; }
        sealed class BaselineEntry
        {
            public string date;
            /// <summary>The inputs it was measured on, flattened ("graph", "sections.EDGE", "rules", "paint", "dem", "model").</summary>
            public readonly Dictionary<string, string> inputs = new Dictionary<string, string>();
            /// <summary>Every check's gating state when recorded ("ZERO", "RATCHET", "REPORT"): none loosens.</summary>
            public readonly Dictionary<string, string> states = new Dictionary<string, string>();
            /// <summary>The creek pin's ways when recorded: none is dropped.</summary>
            public readonly List<uint> pinned = new List<uint>();
            /// <summary>Recorded with a ribbon edge's keys on their bucket's side.</summary>
            public bool keySides;
            public readonly Dictionary<string, BaselineCheck> checks = new Dictionary<string, BaselineCheck>();
        }
        const int Schema = 3;

        // ---- the inputs a baseline is measured on (tools/city/lib/gatebase.mjs inputsOf; the mesh entry adds the DEM:
        // D1's same-level test reads heights). A ratchet against other inputs is STALE, not FAIL: re-record.
        static readonly string[] GeometrySections = { "NODE", "NAME", "EDGE", "PNTS", "SPAN", "XING" };
        static readonly HashSet<string> NotMeasured = new HashSet<string> { "ReportOnly", "PinActive", "FastInAudit", "WorstN", "DedupM", "ShotsN", "ShotsDedupM",
                                                                            "RefSpotReachM", "ExposureRoute", "ExposureRefSpot", "RankRatioCap", "BandCount" };
        static string Sha12(byte[] b, int off, int len)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(b, off, len); var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }
        static Dictionary<string, string> InputsNow()
        {
            var o = new Dictionary<string, string> { ["graph"] = map.graphHash.ToString("x8"), ["model"] = "mesh" };
            string res = Path.Combine(Application.dataPath, "PSXRacing", "Resources");
            try
            {
                var buf = File.ReadAllBytes(Path.Combine(res, "charlotte_city.bytes"));
                if (buf.Length >= 12 && BitConverter.ToInt32(buf, 4) == 2)
                {
                    int nsec = (int)BitConverter.ToUInt32(buf, 8);
                    for (int i = 0; i < nsec && 24 + i * 12 <= buf.Length; i++)
                    {
                        int at = 12 + i * 12; string tag = Encoding.ASCII.GetString(buf, at, 4);
                        long off = BitConverter.ToUInt32(buf, at + 4), len = BitConverter.ToUInt32(buf, at + 8);
                        if (Array.IndexOf(GeometrySections, tag) >= 0 && off + len <= buf.Length) o["sections." + tag] = Sha12(buf, (int)off, (int)len);
                    }
                }
                else o["sections.v1"] = Sha12(buf, 0, buf.Length);
            }
            catch (Exception ex) { o["sections"] = "unreadable (" + ex.Message + ")"; }
            try { var dem = File.ReadAllBytes(Path.Combine(res, "charlotte_dem.bytes")); o["dem"] = Sha12(dem, 0, dem.Length); }
            catch (Exception) { o["dem"] = "none"; }
            // the geometry rules: every constant and table of SmoothRules that decides a violation
            var fields = typeof(SmoothRules).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            var rs = new StringBuilder();
            foreach (var f in fields)
                if (f.IsLiteral && !NotMeasured.Contains(f.Name)) rs.Append(f.Name).Append('=').Append(Convert.ToString(f.GetRawConstantValue(), Inv)).Append(';');
            foreach (var r in SmoothRules.RMin) rs.Append("RMin.").Append(r.cls).Append('=').Append(r.r.ToString(Inv)).Append(';');
            foreach (var r in SmoothRules.CurbReturn) rs.Append("CurbReturn.").Append(r.cls).Append('=').Append(r.r.ToString(Inv)).Append(';');
            foreach (var r in SmoothRules.TaperFloor) rs.Append("TaperFloor.").Append(r.cls).Append('=').Append(r.r.ToString(Inv)).Append(';');
            foreach (var w in SmoothRules.PinnedWays) rs.Append("PinnedWays.").Append(w.ToString(Inv)).Append(';');
            var rb = Encoding.UTF8.GetBytes(rs.ToString());
            o["rules"] = Sha12(rb, 0, rb.Length);
            // the gate's own code (review 5: a loosening edit to the gate moved no input, so it passed its own ratchet):
            // this file, and the tap in CityMeshes - its block and every line that records or sets what it reads
            try
            {
                var code = new StringBuilder(File.ReadAllText(Path.Combine(Application.dataPath, "PSXRacing", "Editor", "CitySmooth.cs")).Replace("\r\n", "\n"));
                string meshes = File.ReadAllText(Path.Combine(Application.dataPath, "PSXRacing", "Scripts", "City", "CityMeshes.cs")).Replace("\r\n", "\n");
                int a = meshes.IndexOf("// ---- the smoothness gate's tap", StringComparison.Ordinal), z = a >= 0 ? meshes.IndexOf("// ---- growable buckets", a, StringComparison.Ordinal) : -1;
                if (a >= 0 && z > a) code.Append(meshes, a, z - a);
                foreach (var l in meshes.Split('\n'))
                    if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\btap\b|RecordTap|TapFlags|tapSlotBase|\bsq[LR]\b")) code.Append(l).Append('\n');
                var cb = Encoding.UTF8.GetBytes(code.ToString());
                o["code"] = Sha12(cb, 0, cb.Length);
            }
            catch (Exception ex) { o["code"] = "unreadable (" + ex.Message + ")"; }
            // the road PNGs (the paint runs are read out of them)
            string art = Path.Combine(Application.dataPath, "PSXRacing", "Art", "City");
            var pngs = Directory.Exists(art) ? Directory.GetFiles(art, "city_road_*.png") : new string[0];
            Array.Sort(pngs, StringComparer.Ordinal);
            var ms = new MemoryStream();
            foreach (var f in pngs) { var nb = Encoding.UTF8.GetBytes(Path.GetFileName(f)); ms.Write(nb, 0, nb.Length); var fb = File.ReadAllBytes(f); ms.Write(fb, 0, fb.Length); }
            var pb = ms.ToArray();
            o["paint"] = Sha12(pb, 0, pb.Length);
            return o;
        }
        static List<string> InputsDiff(Dictionary<string, string> was, Dictionary<string, string> now)
        {
            var d = new List<string>();
            if (was == null || was.Count == 0) { d.Add("the baseline records no inputs (an older schema)"); return d; }
            var keys = new SortedSet<string>(was.Keys, StringComparer.Ordinal); keys.UnionWith(now.Keys);
            foreach (var k in keys)
            {
                was.TryGetValue(k, out var a); now.TryGetValue(k, out var b);
                if (a == b) continue;
                d.Add(k == "graph" ? $"graph {a} -> {b}" : k.StartsWith("sections") ? "container section " + k.Substring(Math.Min(k.Length, 9)) : k == "rules" ? "SmoothRules.cs geometry rules (or the pinned ways)"
                    : k == "paint" ? "road PNGs" : k == "dem" ? "the DEM (charlotte_dem.bytes)" : k == "code" ? "the gate's code (CitySmooth.cs + the tap in CityMeshes.cs)" : $"{k} {a} -> {b}");
            }
            return d;
        }

        static uint Fnv1a(string s)
        {
            uint h = 0x811c9dc5;
            foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 0x01000193; }
            return h;
        }

        /// <summary>A ratio stored as the power of RatioQuantum at or above it
        /// (tools/city/lib/gatebase.mjs quantRatio): the same data always passes
        /// its own baseline.</summary>
        static int QuantRatio(double r)
        {
            double Q = SmoothRules.RatioQuantum;
            if (!(r > 1)) return 0;
            int q = (int)Math.Ceiling(Math.Log(r) / Math.Log(Q));
            while (Math.Pow(Q, q) < r) q++;
            return q;
        }
        static double Unquant(int q) => Math.Pow(SmoothRules.RatioQuantum, q);
        /// <summary>A bad length in LengthQuantumM steps, rounded up (gatebase.mjs quantLen).</summary>
        static int QuantLen(double l) => l > 1e-9 ? (int)Math.Ceiling(l / (double)SmoothRules.LengthQuantumM - 1e-6) : 0;
        /// <summary>The shared tile id: (tx + 32768) &lt;&lt; 16 | (tz + 32768).</summary>
        static uint TileId(long tileKey)
        {
            int tx = (int)(tileKey >> 32), tz = (int)(uint)(tileKey & 0xffffffffL);
            return (uint)(((tx + 32768) << 16) | ((tz + 32768) & 0xffff));
        }

        static void Leb(MemoryStream raw, uint v) { do { byte b = (byte)(v & 0x7f); v >>= 7; if (v != 0) b |= 0x80; raw.WriteByte(b); } while (v != 0); }
        static string Gz64(MemoryStream raw)
        {
            var outp = new MemoryStream();
            using (var gz = new GZipStream(outp, System.IO.Compression.CompressionLevel.Optimal, true)) { var a = raw.ToArray(); gz.Write(a, 0, a.Length); }
            return Convert.ToBase64String(outp.ToArray());
        }
        /// <summary>Schema 3 (gatebase.mjs packKeys): (FNV-1a hash delta,
        /// quantised worst ratio, quantised bad length) LEB128 triples sorted by
        /// hash, gzipped, base64.</summary>
        static string PackKeys(Dictionary<string, (double q, double l)> keys, out int n)
        {
            var byHash = new SortedDictionary<uint, (int q, int l)>();
            foreach (var kv in keys)
            {
                uint h = Fnv1a(kv.Key); int q = QuantRatio(kv.Value.q), l = QuantLen(kv.Value.l);
                byHash[h] = byHash.TryGetValue(h, out var o) ? (Math.Max(o.q, q), o.l + l) : (q, l);
            }
            n = byHash.Count;
            var raw = new MemoryStream(); uint prev = 0;
            foreach (var kv in byHash) { Leb(raw, kv.Key - prev); prev = kv.Key; Leb(raw, (uint)kv.Value.q); Leb(raw, (uint)kv.Value.l); }
            return Gz64(raw);
        }
        static string PackTiles(Dictionary<long, int> tiles)
        {
            var byId = new SortedDictionary<uint, int>();
            foreach (var kv in tiles) { uint id = TileId(kv.Key); byId.TryGetValue(id, out int o); byId[id] = o + kv.Value; }
            var raw = new MemoryStream(); uint prev = 0;
            foreach (var kv in byId) { Leb(raw, kv.Key - prev); prev = kv.Key; Leb(raw, (uint)kv.Value); }
            return Gz64(raw);
        }
        /// <summary>LEB128 records of `width` numbers, the first delta-coded: tiles
        /// (id, runs) are pairs, keys (hash, ratio quantum, length quantum) triples.</summary>
        static List<uint[]> UnpackRecords(string b64, int width)
        {
            var o = new List<uint[]>();
            using (var gz = new GZipStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress))
            {
                uint prev = 0, v = 0; int shift = 0, c, k = 0; var rec = new uint[width];
                while ((c = gz.ReadByte()) >= 0)
                {
                    v |= (uint)(c & 0x7f) << shift;
                    if ((c & 0x80) != 0) { shift += 7; continue; }
                    if (k == 0) { prev += v; rec[0] = prev; } else rec[k] = v;
                    if (++k == width) { o.Add(rec); rec = new uint[width]; k = 0; }
                    v = 0; shift = 0;
                }
            }
            return o;
        }
        static Dictionary<uint, int> UnpackTiles(string b64) { var m = new Dictionary<uint, int>(); foreach (var r in UnpackRecords(b64, 2)) m[r[0]] = (int)r[1]; return m; }
        static Dictionary<uint, (int q, int l)> UnpackKeys(string b64) { var m = new Dictionary<uint, (int, int)>(); foreach (var r in UnpackRecords(b64, 3)) m[r[0]] = ((int)r[1], (int)r[2]); return m; }

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
                    // older schemas (no bad lengths, no inputs) are skipped: re-record
                    if (kv.Value is Dictionary<string, object> ent && ent.TryGetValue("schema", out var sv) && Convert.ToInt32(sv, Inv) == Schema
                        && ent.TryGetValue("checks", out var co) && co is Dictionary<string, object> checks)
                    {
                        be.date = ent.TryGetValue("date", out var dv) ? dv as string : null;
                        if (ent.TryGetValue("inputs", out var io) && io is Dictionary<string, object> inp)
                            foreach (var ip in inp)
                            {
                                if (ip.Value is Dictionary<string, object> sub) foreach (var sp in sub) be.inputs[ip.Key + "." + sp.Key] = Convert.ToString(sp.Value, Inv);
                                else be.inputs[ip.Key] = Convert.ToString(ip.Value, Inv);
                            }
                        if (ent.TryGetValue("states", out var so) && so is Dictionary<string, object> sts)
                            foreach (var st in sts) be.states[st.Key] = Convert.ToString(st.Value, Inv);
                        if (ent.TryGetValue("pinned", out var po) && po is List<object> pl)
                            foreach (var w in pl) be.pinned.Add(Convert.ToUInt32(w, Inv));
                        be.keySides = ent.TryGetValue("keySides", out var ko) && ko is string ks0 && ks0 == "bucket";
                        foreach (var ck in checks)
                            if (ck.Value is Dictionary<string, object> c)
                                be.checks[ck.Key] = new BaselineCheck
                                {
                                    runs = c.TryGetValue("runs", out var r) ? Convert.ToInt32(r, Inv) : 0,
                                    metres = c.TryGetValue("metres", out var mt) ? Convert.ToDouble(mt, Inv) : 0,
                                    worstRatio = c.TryGetValue("worstRatio", out var w) ? Convert.ToDouble(w, Inv) : 0,
                                    keys = c.TryGetValue("keys_b64", out var kb) && kb is string ks ? UnpackKeys(ks) : null,
                                    tiles = c.TryGetValue("tiles_b64", out var tb) && tb is string ts ? UnpackTiles(ts) : null,
                                };
                    }
                    if (be.checks.Count > 0) outp[kv.Key] = be;
                }
                return outp;
            }
            catch (Exception ex) { Debug.LogWarning("[CitySmooth] baseline unreadable: " + ex.Message); return null; }
        }

        /// <summary>Record the mesh entry: the inputs, the numbers and keys (FULL
        /// mode's run is the one to record; new inputs re-record, with before
        /// and after numbers in the commit).</summary>
        static void WriteBaseline(Dictionary<string, Tally> sum, Dictionary<string, string> inputs, string path)
        {
            var sb = new StringBuilder("{\n \"schema\": 3, \"tool\": \"Editor/CitySmooth.cs\", \"note\": \"the mesh entry: the inputs it was measured on (graph hash, container section, rules, paint and DEM digests); per-check runs, metres and worst; every violation key (way, round(s on way / 5 m), check, line) with its worst ratio and bad length, as FNV-1a hash deltas + ratio quanta (RatioQuantum) + length quanta (LengthQuantumM) in LEB128, gzipped; runs per tile (the format of tools/city/lib/gatebase.mjs)\",\n \"entries\": {\n");
            var inp = new StringBuilder("{ ");
            var secs = new StringBuilder();
            foreach (var kv in inputs)
                if (kv.Key.StartsWith("sections.")) secs.Append(secs.Length > 0 ? ", " : "").Append('"').Append(kv.Key.Substring(9)).Append("\": \"").Append(kv.Value).Append('"');
                else inp.Append('"').Append(kv.Key).Append("\": \"").Append(kv.Value.Replace("\"", "'")).Append("\", ");
            inp.Append("\"sections\": { ").Append(secs).Append(" } }");
            var sts = new StringBuilder("{ ");
            for (int k = 0; k < SmoothRules.Checks.Length; k++)
                sts.Append(k > 0 ? ", " : "").Append('"').Append(SmoothRules.Checks[k].id).Append("\": \"").Append(SmoothRules.Checks[k].state.ToString().ToUpperInvariant()).Append('"');
            sts.Append(" }");
            sb.Append("  \"mesh\": { \"schema\": 3, \"date\": \"").Append(DateTime.UtcNow.ToString("yyyy-MM-dd", Inv)).Append("\", \"mode\": \"").Append(mode).Append("\", \"inputs\": ").Append(inp).Append(", \"states\": ").Append(sts)
              .Append(", \"pinned\": [").Append(string.Join(", ", SmoothRules.PinnedWays)).Append("], \"keySides\": \"bucket\", \"checks\": {\n");
            int i = 0;
            foreach (var kv in sum)
            {
                string b64 = PackKeys(kv.Value.keys, out int n);
                sb.Append("   \"").Append(kv.Key).Append("\": { \"runs\": ").Append(kv.Value.runs).Append(", \"metres\": ").Append(F(kv.Value.metres)).Append(", \"worst\": ").Append(F(kv.Value.worst))
                  .Append(", \"worstRatio\": ").Append(F(kv.Value.worstRatio)).Append(", \"keys\": ").Append(n).Append(", \"keys_b64\": \"").Append(b64)
                  .Append("\", \"tiles_b64\": \"").Append(PackTiles(kv.Value.tiles)).Append("\" }").Append(++i < sum.Count ? ",\n" : "\n");
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

        /// <summary>FAST runs inside CityAudit.Run only when SmoothRules.FastInAudit
        /// is set or PSX_SMOOTH_FAST=1 (tools/city-smooth.ps1 FAST sets it): until
        /// its first run in Unity validates the tap and its cost, a city cycle
        /// never runs code nobody has run.</summary>
        static bool FastWanted => SmoothRules.FastInAudit || Environment.GetEnvironmentVariable("PSX_SMOOTH_FAST") == "1";
        static bool armed;

        /// <summary>FAST, step 1 (CityAudit.Run, before its drive and roadside
        /// audits): every tile they build is collected from here on.</summary>
        public static void BeginFast()
        {
            armed = false;
            if (!FastWanted) return;
            Reset();
            mode = "FAST";
            armed = true;
            collecting = true;
            CityMeshes.RecordTap = true;
        }

        /// <summary>FAST, step 1b (CityAudit.Run's finally around the drive and
        /// roadside audits): stop tapping. The tap never outlives the two
        /// audits, even when one of them throws; a later Build (CityPreview,
        /// play mode) then allocates no RoadTap.</summary>
        public static void EndCollect()
        {
            collecting = false;
            CityMeshes.RecordTap = false;
        }

        /// <summary>FAST, step 2: analyse every collected tile whose ring is
        /// complete (the drive and roadside audits' tiles), the reference
        /// spots' rings and one band of the road tiles; report through the
        /// audit's own Line / Check. While SmoothRules.ReportOnly, even a crash
        /// of the gate is an info line, never a failed cycle.</summary>
        public static void EndFast(CityMap m, CityMeshes.Trims t, Dictionary<long, List<CityBuildings.B>> buildings,
                                   Action<string> line, Action<bool, string, object> check)
        {
            if (!armed)
            {
                line?.Invoke("  info smoothness gate (FAST): not run in the city audit until its first Unity run validates it (PSX_SMOOTH_FAST=1, or tools/city-smooth.ps1 FAST; SmoothRules.FastInAudit)");
                return;
            }
            armed = false;
            try
            {
                collecting = true; CityMeshes.RecordTap = true;   // the passes below build and read tiles of their own
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
                Report(line, check);   // a STALE baseline is a failing Check (outside ReportOnly): re-record with the FULL run
            }
            catch (Exception ex)
            {
                if (ReportOnly) line?.Invoke($"  info smoothness gate (FAST) crashed - {ex.GetType().Name}: {ex.Message} (report-only cycle: not a failure)");
                else check?.Invoke(false, "smoothness gate ran", ex.GetType().Name + ": " + ex.Message);
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

        /// <summary>Every tile a ribbon or its paint reaches, in raster order
        /// (rows of z): each edge sampled every 2 m, with every tile within its
        /// half width + 1 m - a wide road's far edge along a tile boundary, a
        /// corner crossed for less than a sample. A run is kept by the tile its
        /// worst sample lies in, so a tile left out here would drop it.</summary>
        static List<(int tx, int tz)> RoadTiles()
        {
            var seen = new HashSet<long>(); var o = new List<(int, int)>();
            float T = CityMeshes.TileSize;
            foreach (var e in map.edges)
            {
                float r = (float)e.width * 0.5f + 1f;
                for (float s = 0f; ; s += 2f)
                {
                    var p = e.PointAt(Mathf.Min(s, e.length));
                    int x0 = Mathf.FloorToInt((p.x - r) / T), x1 = Mathf.FloorToInt((p.x + r) / T), z0 = Mathf.FloorToInt((p.y - r) / T), z1 = Mathf.FloorToInt((p.y + r) / T);
                    for (int tx = x0; tx <= x1; tx++) for (int tz = z0; tz <= z1; tz++) if (seen.Add(TileKey(tx, tz))) o.Add((tx, tz));
                    if (s >= e.length) break;
                }
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
            int status = 1;
            try
            {
                collecting = true; CityMeshes.RecordTap = true;
                var t = CityMeshes.NodeTrims(m);
                Setup(m, t);
                var buildings = CityBuildings.Precompute(m);
                string dumpPath = Environment.GetEnvironmentVariable("PSX_SMOOTH_TAPDUMP");
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    tapDump = new BinaryWriter(new GZipStream(File.Create(dumpPath), System.IO.Compression.CompressionLevel.Fastest));
                    tapDump.Write(0x32504154);   // 'TAP2': TAP1 with heights
                }
                TraceBegin();
                string se = Environment.GetEnvironmentVariable("PSX_SMOOTH_STRANDS"), so = Environment.GetEnvironmentVariable("PSX_SMOOTH_STRANDS_OUT");
                if (!string.IsNullOrEmpty(se) && File.Exists(se) && !string.IsNullOrEmpty(so))
                {
                    strandEdges = new HashSet<int>();
                    foreach (var l in File.ReadAllLines(se)) if (int.TryParse(l.Trim(), out int ei)) strandEdges.Add(ei);
                    strandDump = new StreamWriter(so);
                }
                RasterPass(RoadTiles(), buildings, true);
                if (strandDump != null) { strandDump.Dispose(); strandDump = null; Debug.Log("[CitySmooth] strands dumped to " + so); }
                if (tapDump != null) { tapDump.Dispose(); tapDump = null; Debug.Log("[CitySmooth] tap dumped to " + dumpPath); }
                TraceEnd();
                status = Report(l => Debug.Log("[CitySmooth] " + l), (pass, what, detail) => Debug.Log($"[CitySmooth] {(pass ? "ok  " : "FAIL")} {what} - {detail}"));
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { collecting = false; CityMeshes.RecordTap = false; frags.Clear(); refCache.Clear(); tapDump?.Dispose(); tapDump = null; trace = null; strandDump?.Dispose(); strandDump = null; }
            // 0 PASS, 1 FAIL (a STALE baseline fails too, until -WriteBaseline re-records it); a report-only cycle never fails
            if (Application.isBatchMode) EditorApplication.Exit(status == 0 || (SmoothRules.ReportOnly && status == 1) ? 0 : status);
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
