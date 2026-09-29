using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LANE AUDIT. Born as a probe (2026-09-28) for the owner's "one side
    /// of the road looks wider than the other": on every real-width stage the
    /// painter put the double yellow 3.658 m (one 12 ft lane) in from the LEFT
    /// edge instead of at the middle - a 3.60 m lane beside a 2.68 m one on
    /// the 6.4 m Parkway loops, 3.60 beside 2.38 on the 6.1 m roads - and
    /// nothing measured paint against pavement, so it shipped on seven stages.
    /// Kept as a gate so it never ships again.
    ///
    ///  Run(): every venue with a road ribbon (Scened, plus the HeldBack
    ///  scenes that exist, which are reported but do not fail the run). For
    ///  every ring (4 m) and every mid-quad (2 m between) it intersects the
    ///  section line with the ribbon's TRIANGLES (so the diagonal split is
    ///  measured, not assumed) and finds where the texture's painted lines
    ///  are. FAILS a venue when, anywhere:
    ///   - left and right lanes (centre line to each edge line, line centre
    ///     to line centre) differ by more than V = 2.5 cm;
    ///   - the painted centre stands more than V/2 off the ribbon's centre
    ///     (the midpoint of its two edges - the lane boundary traffic, the
    ///     grid and the AI all drive to);
    ///   - any painted line misses its smooth (bilinear) position by more
    ///     than V at a mid-quad (the owner's smooth-lines rule);
    ///   - the ribbon's centre is more than 1 cm off the TrackPath waypoint;
    ///   - the texture's own centre line is off u = 0.5 by more than half a
    ///     texel, or its edge lines do not mirror (the painter, checked
    ///     without any geometry).
    ///  Writes PSXRacing_lane_audit.txt beside the project (tools\verify.ps1
    ///  fails on its FAIL lines) and Screenshots/lanes/lane_census_&lt;id&gt;.csv.
    ///
    ///  AuditForLab(): the same pass on the one scene tools\stage-lab.ps1 built.
    ///
    ///  Shots(): PSX_LANES_VENUE (default BlowingRock). The game camera at the
    ///  owner's two phone frames (the chase pose fitted to the line positions
    ///  measured off his JPGs, or fixed by PSX_LANES_POSE1/2 = "s,lat,yaw,grade"
    ///  so a rebuilt road is shown from exactly where the old one was), at
    ///  night in the snow dress, with the HUD (game resolution, dithered) and
    ///  without it (2340x1080, for measuring). While the texture is still
    ///  off-centre the same frame is also rendered with a centred texture on
    ///  the road (MaterialPropertyBlock - no asset is touched).
    /// </summary>
    public static class LaneAudit
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static string ProjRoot => Directory.GetParent(Application.dataPath).FullName;
        static string OutDir => Path.Combine(ProjRoot, "Screenshots", "lanes");
        static string ReportPath => Path.Combine(ProjRoot, "PSXRacing_lane_audit.txt");

        static readonly Color32 PaintYellow = new Color32(196, 160, 40, 255);
        static readonly Color32 PaintWhite = new Color32(200, 200, 196, 255);

        /// <summary>The smooth-lines gate: one framebuffer pixel at 6 m.</summary>
        internal const float V = 0.025f;
        /// <summary>The ribbon's centre against the waypoint traffic and the
        /// AI steer to.</summary>
        const float WaypointTolM = 0.01f;

        // ------------------------------------------------------------------
        //  The texture's painted lines
        // ------------------------------------------------------------------
        internal class Paint { public char kind; public int x0, x1; public bool dashed; public float U0, U1; public float Uc => (U0 + U1) * 0.5f; }

        internal class TexLines
        {
            public string path; public int w, h;
            public readonly List<Paint> lines = new List<Paint>();
            public float uEdgeL = float.NaN, uEdgeR = float.NaN, uCentre = float.NaN;
            public bool twoWay;
            public Color32[] px;
        }

        static bool Near(Color32 a, Color32 b, int tol = 6) =>
            Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol;

        static TexLines ReadLines(Texture tex) =>
            tex == null ? null : ReadLinesAt(AssetDatabase.GetAssetPath(tex));

        /// <summary>The painted line columns of a road PNG, read off the file
        /// (the imported texture may be compressed; the PNG is what the painter
        /// wrote). Null when there is no such file.</summary>
        internal static TexLines ReadLinesAt(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var tl = new TexLines { path = assetPath };
            string full = Path.Combine(ProjRoot, tl.path);
            if (!File.Exists(full)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(full))) { Object.DestroyImmediate(t); return null; }
            tl.w = t.width; tl.h = t.height;
            tl.px = t.GetPixels32();
            Object.DestroyImmediate(t);

            var cls = new char[tl.w];
            var fullCol = new bool[tl.w];
            for (int x = 0; x < tl.w; x++)
            {
                int yc = 0, wc = 0;
                for (int y = 0; y < tl.h; y++)
                {
                    var p = tl.px[y * tl.w + x];
                    if (Near(p, PaintYellow)) yc++;
                    else if (Near(p, PaintWhite)) wc++;
                }
                cls[x] = yc > 0 ? 'Y' : wc > 0 ? 'W' : '.';
                fullCol[x] = yc + wc == tl.h;
            }
            for (int x = 0; x < tl.w; x++)
            {
                if (cls[x] == '.') continue;
                int x1 = x;
                bool dashed = !fullCol[x];
                while (x1 + 1 < tl.w && cls[x1 + 1] == cls[x]) { x1++; if (!fullCol[x1]) dashed = true; }
                tl.lines.Add(new Paint { kind = cls[x], x0 = x, x1 = x1, dashed = dashed,
                                         U0 = x / (float)tl.w, U1 = (x1 + 1) / (float)tl.w });
                x = x1;
            }
            var solidW = tl.lines.Where(l => l.kind == 'W' && !l.dashed).ToList();
            if (solidW.Count >= 2) { tl.uEdgeL = solidW[0].Uc; tl.uEdgeR = solidW[solidW.Count - 1].Uc; }
            var yel = tl.lines.Where(l => l.kind == 'Y').ToList();
            tl.twoWay = yel.Count > 0;
            if (tl.twoWay) tl.uCentre = (yel.Min(l => l.U0) + yel.Max(l => l.U1)) * 0.5f;
            else if (!float.IsNaN(tl.uEdgeL)) tl.uCentre = (tl.uEdgeL + tl.uEdgeR) * 0.5f;
            return tl;
        }

        static string Layout(TexLines tl)
        {
            var sb = new StringBuilder();
            foreach (var l in tl.lines)
                sb.AppendFormat(CI, "{0}{1}[{2}-{3}] ", l.kind, l.dashed ? "d" : "", l.x0, l.x1);
            return sb.ToString().Trim();
        }

        /// <summary>The painter's half of the gate, no geometry: the centre
        /// line (the double yellow, or a one-way road's middle) at u = 0.5 and
        /// the edge lines mirrored, each to half a texel. Null when it holds,
        /// else what is wrong. Shared with the self-test.</summary>
        internal static string PaintProblem(TexLines tl)
        {
            if (tl == null) return "unreadable";
            if (float.IsNaN(tl.uEdgeL) || float.IsNaN(tl.uEdgeR)) return "no pair of solid white edge lines";
            if (float.IsNaN(tl.uCentre)) return "no centre line";
            float halfTexel = 0.5f / tl.w + 1e-5f;
            if (Mathf.Abs(tl.uCentre - 0.5f) > halfTexel)
                return string.Format(CI, "centre line at u {0:0.0000}, not 0.5 ({1:+0.0;-0.0} texels)", tl.uCentre, (tl.uCentre - 0.5f) * tl.w);
            if (Mathf.Abs(tl.uEdgeL - (1f - tl.uEdgeR)) > halfTexel)
                return string.Format(CI, "edge lines at u {0:0.0000} / {1:0.0000} do not mirror", tl.uEdgeL, tl.uEdgeR);
            return null;
        }

        // ------------------------------------------------------------------
        //  Section solver: where along O + s*d does the triangulated u reach a
        //  target? Doubles, relative to O (world coords reach 2 km).
        // ------------------------------------------------------------------
        struct Tri { public double ax, az, bx, bz, cx, cz; public double ua, ub, uc; }

        static void Bary(in Tri t, double px, double pz, out double la, out double lb, out double lc)
        {
            double v0x = t.bx - t.ax, v0z = t.bz - t.az, v1x = t.cx - t.ax, v1z = t.cz - t.az;
            double v2x = px - t.ax, v2z = pz - t.az;
            double den = v0x * v1z - v1x * v0z;
            lb = (v2x * v1z - v1x * v2z) / den;
            lc = (v0x * v2z - v2x * v0z) / den;
            la = 1.0 - lb - lc;
        }

        static bool Clip(double l0, double dl, ref double lo, ref double hi)
        {
            const double eps = 1e-7;
            if (Math.Abs(dl) < 1e-14) return l0 >= -eps;
            double s = (-eps - l0) / dl;
            if (dl > 0) lo = Math.Max(lo, s); else hi = Math.Min(hi, s);
            return lo <= hi;
        }

        /// <summary>Triangles already translated so the section origin is (0,0).</summary>
        static double SolveS(List<Tri> tris, double dx, double dz, double target)
        {
            foreach (var t in tris)
            {
                Bary(t, 0, 0, out double a0, out double b0, out double c0);
                Bary(t, dx, dz, out double a1, out double b1, out double c1);
                double da = a1 - a0, db = b1 - b0, dc = c1 - c0;
                double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
                if (!Clip(a0, da, ref lo, ref hi) || !Clip(b0, db, ref lo, ref hi) || !Clip(c0, dc, ref lo, ref hi)) continue;
                double u0 = a0 * t.ua + b0 * t.ub + c0 * t.uc;
                double du = da * t.ua + db * t.ub + dc * t.uc;
                if (Math.Abs(du) < 1e-12) continue;
                double s = (target - u0) / du;
                if (s < lo - 1e-5 || s > hi + 1e-5) continue;
                return s;
            }
            return double.NaN;
        }

        // ------------------------------------------------------------------
        //  One venue
        // ------------------------------------------------------------------
        class Sec
        {
            public int quad; public bool mid; public float m;
            public double left, right, diff, off, asphL, asphR;
            public double zigC, zigL, zigR, wpOff = double.NaN;
            public double Zig => Math.Max(Math.Abs(zigC), Math.Max(Math.Abs(zigL), Math.Abs(zigR)));
        }

        /// <summary>The tarmac ribbon, ring by ring: left and right edge (and
        /// the centre column when the mesh has one), whatever its stride.</summary>
        class Ribbon
        {
            public Vector3[] L, R; public int rings, stride;
            public Vector3[] V; public Vector2[] UV;
            public List<int>[] quadTris;
            public int[] tris;
            public TexLines tex, deckTex;
        }

        static Ribbon ReadRibbon(GameObject road, StringBuilder log)
        {
            var mf = road.GetComponent<MeshFilter>();
            var mr = road.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) { log.AppendLine("  no mesh on Road"); return null; }
            var mesh = mf.sharedMesh;
            var rb = new Ribbon();
            rb.V = mesh.vertices.Select(v => road.transform.TransformPoint(v)).ToArray();
            rb.UV = mesh.uv;
            // Stride 3 (left, centre, right: the ribbon since 2026-09-28) or
            // 2 (left, right: an older scene).
            bool Pattern(int stride)
            {
                if (rb.V.Length % stride != 0 || rb.V.Length < 2 * stride) return false;
                for (int k = 0; k < Mathf.Min(rb.V.Length / stride, 50); k++)
                    for (int j = 0; j < stride; j++)
                        if (Mathf.Abs(rb.UV[k * stride + j].x - j / (float)(stride - 1)) > 1e-4f) return false;
                return true;
            }
            rb.stride = Pattern(PSXRacingBuilder.RoadStride) ? PSXRacingBuilder.RoadStride : Pattern(2) ? 2 : 0;
            if (rb.stride == 0) { log.AppendLine("  Road ribbon is neither left|centre|right nor left|right across (u 0/0.5/1 or 0/1)"); return null; }
            rb.rings = rb.V.Length / rb.stride;
            rb.L = new Vector3[rb.rings]; rb.R = new Vector3[rb.rings];
            for (int k = 0; k < rb.rings; k++) { rb.L[k] = rb.V[k * rb.stride]; rb.R[k] = rb.V[k * rb.stride + rb.stride - 1]; }
            var all = new List<int>();
            for (int sm = 0; sm < mesh.subMeshCount; sm++) all.AddRange(mesh.GetTriangles(sm));
            rb.tris = all.ToArray();
            rb.quadTris = new List<int>[rb.rings];
            for (int k = 0; k < rb.rings; k++) rb.quadTris[k] = new List<int>();
            for (int t = 0; t < rb.tris.Length; t += 3)
            {
                int mn = Mathf.Min(rb.tris[t], Mathf.Min(rb.tris[t + 1], rb.tris[t + 2]));
                rb.quadTris[mn / rb.stride].Add(t);
            }
            var mats = mr.sharedMaterials;
            rb.tex = mats.Length > 0 && mats[0] != null ? ReadLines(mats[0].mainTexture) : null;
            rb.deckTex = mats.Length > 1 && mats[1] != null ? ReadLines(mats[1].mainTexture) : null;
            return rb;
        }

        static List<Tri> TrisAround(Ribbon rb, int quad, double ox, double oz)
        {
            var list = new List<Tri>();
            for (int q = quad - 1; q <= quad + 1; q++)
            {
                if (q < 0 || q >= rb.rings) continue;
                foreach (int t in rb.quadTris[q])
                {
                    int ia = rb.tris[t], ib = rb.tris[t + 1], ic = rb.tris[t + 2];
                    var A = rb.V[ia]; var B = rb.V[ib]; var C = rb.V[ic];
                    list.Add(new Tri
                    {
                        ax = A.x - ox, az = A.z - oz, bx = B.x - ox, bz = B.z - oz, cx = C.x - ox, cz = C.z - oz,
                        ua = rb.UV[ia].x, ub = rb.UV[ib].x, uc = rb.UV[ic].x,
                    });
                }
            }
            return list;
        }

        static double Pct(List<double> xs, double p)
        {
            if (xs.Count == 0) return double.NaN;
            var s = xs.OrderBy(v => v).ToList();
            int i = Mathf.Clamp(Mathf.CeilToInt((float)(p * s.Count)) - 1, 0, s.Count - 1);
            return s[i];
        }

        static void KerbReport(Transform track, string name, StringBuilder sb, int rings)
        {
            var go = track.Find(name);
            if (go == null) { sb.AppendLine("    " + name + ": none"); return; }
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null) { sb.AppendLine("    " + name + ": no mesh"); return; }
            var v = mf.sharedMesh.vertices.Select(p => go.TransformPoint(p)).ToArray();
            int stride = v.Length / Mathf.Max(1, rings);
            if (stride < 2) { sb.AppendLine("    " + name + ": " + v.Length + " verts for " + rings + " rings"); return; }
            var w = new List<double>();
            for (int k = 0; k + stride - 1 < v.Length; k += stride)
            {
                var a = v[k]; var b = v[k + stride - 1];
                w.Add(new Vector2(b.x - a.x, b.z - a.z).magnitude);
            }
            string tex = mr != null && mr.sharedMaterial != null && mr.sharedMaterial.mainTexture != null
                ? Path.GetFileName(AssetDatabase.GetAssetPath(mr.sharedMaterial.mainTexture)) : "?";
            sb.AppendFormat(CI, "    {0}: {1} verts/station, width mean {2:0.000} min {3:0.000} max {4:0.000} m, surface {5}\n",
                            name, stride, w.Average(), w.Min(), w.Max(), tex);
        }

        static string Header() =>
            "LANE AUDIT " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CI) + "\n" +
            "Sections every 2 m (each 4 m ring + each mid-quad). left = centre line to LEFT edge line, right = to RIGHT edge line,\n" +
            "line centre to line centre, metres, in the forward bake direction. off = painted centre minus ribbon centre (+ = right).\n" +
            string.Format(CI, "zig = triangulated paint vs bilinear (smooth) paint at mid-quad. Gates: |L-R| <= {0:0.000}, |off| <= {1:0.0000}, zig <= {0:0.000}, ribbon centre vs waypoint <= {2:0.000} m.\n",
                          V, V * 0.5f, WaypointTolM);

        static string SummaryHeader() =>
            string.Format(CI, "{0,-20} {1,5} {2,-38} {3,6} {4,7} {5,7} {6,7} {7,7} {8,8} {9,7} {10}",
                "venue", "W", "texture", "stride", "left", "right", "maxDiff", "maxOff", "zigMax", "wpOff", "verdict");

        /// <summary>Every venue: the verify stage (tools\verify.ps1,
        /// tools\lane-audit.ps1). HeldBack venues are measured when their
        /// scene exists and reported, but only a shipped venue fails the run.</summary>
        [MenuItem("PSX Racing/Audits/Lane Audit")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder(Header());
            sb.AppendLine();
            var summary = new StringBuilder(SummaryHeader() + "\n");
            int problems = 0;
            // The targeted edition's venues (EditionTarget; ALL by default) -
            // a MAIN run must not FAIL 'MISSING SCENE' for Charlotte's, which
            // MAIN never ships - and the held-back ones that edition would get.
            var target = EditionTarget.Current;
            var venues = new List<TrackCatalog.TrackDef>(TrackCatalog.ScenedFor(target));
            var held = new HashSet<string>();
            foreach (var h in TrackCatalog.HeldBack)
                if (Edition.ShipsIn(h, target) && !venues.Any(v => v.id == h.id)) { venues.Add(h); held.Add(h.id); }
            string only = Environment.GetEnvironmentVariable("PSX_LANES_ONLY");
            foreach (var def in venues)
            {
                if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(def.id)) continue;
                string scene = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
                bool isHeld = held.Contains(def.id);
                if (!File.Exists(Path.Combine(ProjRoot, scene)))
                {
                    if (isHeld) { sb.AppendLine(def.id + ": held back, no scene built - not measured\n"); continue; }
                    sb.AppendLine("  FAIL " + def.id + ": MISSING SCENE " + scene + "\n");
                    problems++;
                    continue;
                }
                int p;
                try { p = AuditOne(def, scene, sb, summary, isHeld); }
                catch (Exception e) { sb.AppendLine("  FAIL " + def.id + ": THREW " + e.Message); Debug.LogException(e); p = 1; }
                if (!isHeld) problems += p;
            }
            sb.AppendLine();
            sb.AppendLine("SUMMARY");
            sb.Append(summary);
            sb.AppendLine(problems == 0 ? "LANE AUDIT OK" : "LANE AUDIT: " + problems + " PROBLEM(S)");
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log("[Lanes] audit written to " + ReportPath + "\n" + summary);
        }

        /// <summary>StageLab: the one venue it just built.</summary>
        internal static string AuditForLab(TrackCatalog.TrackDef def, string scenePath)
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder("lane audit:\n");
            var summary = new StringBuilder(SummaryHeader() + "\n");
            int p;
            try { p = AuditOne(def, scenePath, sb, summary, false); }
            catch (Exception e) { sb.AppendLine("  FAIL THREW " + e.Message); p = 1; }
            sb.Append(summary);
            sb.AppendLine(p == 0 ? "LANE AUDIT OK" : "LANE AUDIT: " + p + " PROBLEM(S)");
            return sb.ToString();
        }

        /// <summary>One scene; returns the number of failing checks.</summary>
        static int AuditOne(TrackCatalog.TrackDef def, string scene, StringBuilder sb, StringBuilder summary, bool heldBack)
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var path = Object.FindFirstObjectByType<TrackPath>();
            var track = GameObject.Find("Track");
            var road = track != null ? track.transform.Find("Road") : null;
            sb.AppendFormat(CI, "{0} ({1}) catalog width {2:0.00} m, TrackPath.roadWidth {3:0.00}, stage {4}, loop {5}, oneWay/drag {6}, scene {7:yyyy-MM-dd HH:mm}{8}\n",
                def.id, def.name, def.roadWidth, path != null ? path.roadWidth : float.NaN, def.stage, def.loop,
                def.oneWay || def.drag, File.GetLastWriteTime(Path.Combine(ProjRoot, scene)), heldBack ? "  [HELD BACK: reported, not gated]" : "");
            if (road == null || path == null)
            {
                // Charlotte and its three routes: streets built by the city,
                // not a Track/Road ribbon (their paint is the city's line
                // model and the city audit's).
                sb.AppendLine("  no Track/Road ribbon (city streets or a runtime world): not measured here\n");
                summary.AppendLine(string.Format(CI, "{0,-20} {1,5:0.0} (no ribbon)", def.id, def.roadWidth));
                return 0;
            }
            int fails = 0;
            // A held-back venue's findings say "held", so verify (which fails
            // on "FAIL") reports them without failing a run that ships none.
            void Fail(string what) { fails++; sb.AppendLine((heldBack ? "  held " : "  FAIL ") + def.id + ": " + what); }

            var rb = ReadRibbon(road.gameObject, sb);
            if (rb == null || rb.tex == null || float.IsNaN(rb.tex.uCentre))
            {
                Fail("ribbon or its texture not readable");
                summary.AppendLine(string.Format(CI, "{0,-20} {1,5:0.0} (unreadable) FAIL", def.id, def.roadWidth));
                sb.AppendLine();
                return fails;
            }
            var tx = rb.tex;
            float W = path.roadWidth;
            sb.AppendFormat(CI, "  tarmac texture {0} ({1}x{2}): {3}\n", Path.GetFileName(tx.path), tx.w, tx.h, Layout(tx));
            sb.AppendFormat(CI, "    u: edgeL {0:0.0000} centre {1:0.0000} edgeR {2:0.0000}  => metres from the left edge {3:0.000} / {4:0.000} / {5:0.000} (ribbon half {6:0.000})\n",
                tx.uEdgeL, tx.uCentre, tx.uEdgeR, tx.uEdgeL * W, tx.uCentre * W, tx.uEdgeR * W, W * 0.5f);
            string paint = PaintProblem(tx);
            if (paint != null) Fail("tarmac texture " + Path.GetFileName(tx.path) + ": " + paint);
            if (rb.deckTex != null)
            {
                bool same = Mathf.Abs(rb.deckTex.uCentre - tx.uCentre) < 1e-4f && Mathf.Abs(rb.deckTex.uEdgeL - tx.uEdgeL) < 1e-4f &&
                            Mathf.Abs(rb.deckTex.uEdgeR - tx.uEdgeR) < 1e-4f;
                sb.AppendFormat(CI, "  deck texture {0}: {1} ({2})\n", Path.GetFileName(rb.deckTex.path), Layout(rb.deckTex),
                                same ? "same line positions" : "DIFFERENT line positions");
                string deckPaint = PaintProblem(rb.deckTex);
                if (deckPaint != null) Fail("deck texture " + Path.GetFileName(rb.deckTex.path) + ": " + deckPaint);
                if (!same) Fail("deck and tarmac paint their lines in different places");
            }
            sb.AppendFormat(CI, "  ribbon: {0} rings, {1} vertices a ring ({2})\n", rb.rings, rb.stride,
                            rb.stride == PSXRacingBuilder.RoadStride ? "left | centre | right" : "left | right - an older build");

            float uL = tx.uEdgeL, uC = tx.uCentre, uR = tx.uEdgeR;
            float lineHalfU = 0f;
            var edge0 = tx.lines.FirstOrDefault(l => l.kind == 'W' && !l.dashed);
            if (edge0 != null) lineHalfU = (edge0.U1 - edge0.U0) * 0.5f;

            var secs = new List<Sec>();
            int quads = rb.rings - 1;
            float spacing = path.spacing;
            for (int q = 0; q < quads; q++)
            {
                for (int half = 0; half < 2; half++)
                {
                    bool mid = half == 1;
                    Vector3 L0 = rb.L[q], R0 = rb.R[q], L1 = rb.L[q + 1], R1 = rb.R[q + 1];
                    Vector3 Ls = mid ? (L0 + L1) * 0.5f : L0, Rs = mid ? (R0 + R1) * 0.5f : R0;
                    // The ribbon's centre on this section: the midpoint of its
                    // two edges, which is where the lanes divide.
                    Vector3 O = (Ls + Rs) * 0.5f;
                    var d2 = new Vector2(Rs.x - Ls.x, Rs.z - Ls.z);
                    double rungW = d2.magnitude;
                    if (rungW < 1e-4) continue;
                    d2 /= (float)rungW;
                    var tris = TrisAround(rb, q, O.x, O.z);
                    var s = new Sec { quad = q, mid = mid, m = (q + (mid ? 0.5f : 0f)) * spacing };
                    double sL = SolveS(tris, d2.x, d2.y, uL);
                    double sC = SolveS(tris, d2.x, d2.y, uC);
                    double sR = SolveS(tris, d2.x, d2.y, uR);
                    double s0 = SolveS(tris, d2.x, d2.y, 0.0);
                    double s1 = SolveS(tris, d2.x, d2.y, 1.0);
                    if (double.IsNaN(sL) || double.IsNaN(sC) || double.IsNaN(sR)) continue;
                    s.left = sC - sL; s.right = sR - sC; s.diff = s.left - s.right;
                    s.off = sC;
                    s.asphL = (sL - lineHalfU * rungW) - s0;
                    s.asphR = s1 - (sR + lineHalfU * rungW);
                    // bilinear (smooth) position of a line at u: (u - 0.5) * rungW on this section
                    s.zigC = sC - (uC - 0.5) * rungW;
                    s.zigL = sL - (uL - 0.5) * rungW;
                    s.zigR = sR - (uR - 0.5) * rungW;
                    if (!mid)
                    {
                        int wi = path.Wrap(q);
                        if (wi >= 0 && wi < path.Count)
                        {
                            var wp = path.waypoints[wi];
                            s.wpOff = (O.x - wp.x) * d2.x + (O.z - wp.z) * d2.y;
                        }
                    }
                    secs.Add(s);
                }
            }
            if (secs.Count == 0)
            {
                Fail("no section of the ribbon solved");
                summary.AppendLine(string.Format(CI, "{0,-20} {1,5:0.0} (no sections) FAIL", def.id, def.roadWidth));
                sb.AppendLine();
                return fails;
            }

            var absDiff = secs.Select(x => Math.Abs(x.diff)).ToList();
            var zigs = secs.Where(x => x.mid).Select(x => x.Zig).ToList();
            var rings = secs.Where(x => !x.mid && !double.IsNaN(x.wpOff)).ToList();
            var worst = secs.OrderByDescending(x => Math.Abs(x.diff)).First();
            var worstOff = secs.OrderByDescending(x => Math.Abs(x.off)).First();
            var worstZig = secs.Where(x => x.mid).OrderByDescending(x => x.Zig).FirstOrDefault();
            double maxWp = rings.Count > 0 ? rings.Max(x => Math.Abs(x.wpOff)) : 0.0;
            double maxZig = zigs.Count > 0 ? zigs.Max() : 0.0;
            sb.AppendFormat(CI, "  {0} sections over {1:0} m\n", secs.Count, quads * spacing);
            sb.AppendFormat(CI, "  lanes     left mean {0:0.000} min {1:0.000} max {2:0.000} | right mean {3:0.000} min {4:0.000} max {5:0.000}\n",
                secs.Average(x => x.left), secs.Min(x => x.left), secs.Max(x => x.left),
                secs.Average(x => x.right), secs.Min(x => x.right), secs.Max(x => x.right));
            sb.AppendFormat(CI, "            |L-R| max {0:0.0000} m (wp {1} {2}, {3:0} m) p95 {4:0.0000}; ratio L/R mean {5:0.000}\n",
                Math.Abs(worst.diff), worst.quad, worst.mid ? "mid" : "ring", worst.m, Pct(absDiff, 0.95),
                secs.Average(x => x.left / x.right));
            sb.AppendFormat(CI, "  centre    painted centre - ribbon centre: mean {0:+0.0000;-0.0000} max|.| {1:0.0000} m (wp {2} {3})\n",
                secs.Average(x => x.off), Math.Abs(worstOff.off), worstOff.quad, worstOff.mid ? "mid" : "ring");
            sb.AppendFormat(CI, "            ribbon centre - TrackPath waypoint (rings): mean {0:+0.0000;-0.0000} max|.| {1:0.0000} m\n",
                rings.Count > 0 ? rings.Average(x => x.wpOff) : 0.0, maxWp);
            sb.AppendFormat(CI, "  edges     asphalt outside the edge lines: left mean {0:0.000} max {1:0.000} | right mean {2:0.000} max {3:0.000} m\n",
                secs.Average(x => x.asphL), secs.Max(x => x.asphL), secs.Average(x => x.asphR), secs.Max(x => x.asphR));
            sb.AppendFormat(CI, "  smooth    paint zig (mid-quad, any line) max {0:0.0000} p95 {1:0.0000} m (wp {2}); centre line max {3:0.0000}\n",
                maxZig, Pct(zigs, 0.95), worstZig != null ? worstZig.quad : -1,
                secs.Where(x => x.mid).Select(x => Math.Abs(x.zigC)).DefaultIfEmpty(0).Max());

            if (Math.Abs(worst.diff) > V)
                Fail(string.Format(CI, "LOPSIDED LANES: left {0:0.000} vs right {1:0.000} m at wp {2} ({3:0} m), |L-R| {4:0.000} > {5:0.000}",
                                   worst.left, worst.right, worst.quad, worst.m, Math.Abs(worst.diff), V));
            if (Math.Abs(worstOff.off) > V * 0.5f)
                Fail(string.Format(CI, "CENTRE LINE OFF CENTRE by {0:+0.000;-0.000} m at wp {1} ({2:0} m), over {3:0.0000}",
                                   worstOff.off, worstOff.quad, worstOff.m, V * 0.5f));
            if (maxZig > V)
                Fail(string.Format(CI, "PAINT ZIGZAG {0:0.0000} m off its smooth line at wp {1} mid-quad, over {2:0.000}",
                                   maxZig, worstZig != null ? worstZig.quad : -1, V));
            if (maxWp > WaypointTolM)
                Fail(string.Format(CI, "RIBBON OFF ITS PATH: centre {0:0.0000} m from the TrackPath waypoint, over {1:0.000}", maxWp, WaypointTolM));

            KerbReport(track.transform, "KerbL", sb, rb.rings);
            KerbReport(track.transform, "KerbR", sb, rb.rings);
            if (fails == 0) sb.AppendLine("  ok   " + def.id + ": lanes even, centre line on the centre, lines smooth");
            sb.AppendLine();

            summary.AppendLine(string.Format(CI, "{0,-20} {1,5:0.0} {2,-38} {3,6} {4,7:0.000} {5,7:0.000} {6,7:0.0000} {7,7:0.0000} {8,8:0.0000} {9,7:0.0000} {10}",
                def.id, W, Path.GetFileName(tx.path), rb.stride, secs.Average(x => x.left), secs.Average(x => x.right),
                absDiff.Max(), Math.Abs(worstOff.off), maxZig, maxWp,
                fails == 0 ? "ok" : heldBack ? "fails (held back)" : "FAIL"));

            var csv = new StringBuilder("quad,mid,m,left,right,diff,off,asphL,asphR,zigC,zigL,zigR,wpOff\n");
            foreach (var x in secs)
                csv.AppendLine(string.Format(CI, "{0},{1},{2:0.0},{3:0.0000},{4:0.0000},{5:0.0000},{6:0.0000},{7:0.0000},{8:0.0000},{9:0.00000},{10:0.00000},{11:0.00000},{12:0.00000}",
                    x.quad, x.mid ? 1 : 0, x.m, x.left, x.right, x.diff, x.off, x.asphL, x.asphR,
                    x.zigC, x.zigL, x.zigR, x.wpOff));
            File.WriteAllText(Path.Combine(OutDir, "lane_census_" + def.id + ".csv"), csv.ToString());
            return fails;
        }

        // ------------------------------------------------------------------
        //  Shots: the owner's frames, rendered
        // ------------------------------------------------------------------
        const int PhoneW = 2340, PhoneH = 1080;

        /// <summary>Line x positions (L edge, centre, R edge) per image row,
        /// measured off the owner's 2340x1080 JPGs of 2026-09-28 (Blowing Rock,
        /// snow, night; rowfit.py).</summary>
        static readonly (int row, float L, float C, float R)[] Owner1 =
        {
            (420, 1088f, 1206.75f, 1293f), (426, 1072.5f, 1202.75f, 1301f), (432, 1059.5f, 1200.5f, 1305.5f),
            (438, 1039.5f, 1196.25f, 1312f), (444, 1018.5f, 1193f, 1318f), (456, 987.5f, 1183f, 1328f),
            (462, 974.5f, 1179.5f, 1330f), (468, 957f, 1175f, 1337.5f), (474, 943f, 1171f, 1340f),
            (480, 922f, 1166.25f, 1346f), (486, 908.5f, 1161.75f, 1350.5f),
        };
        static readonly (int row, float L, float C, float R)[] Owner2 =
        {
            (420, 1252.5f, 1369.75f, 1458f), (426, 1243.5f, 1376.25f, 1474f), (438, 1229.5f, 1385.5f, 1503.5f),
            (450, 1215f, 1394.5f, 1531f), (456, 1206f, 1399.75f, 1547f), (462, 1200f, 1403.5f, 1555f),
            (468, 1190.5f, 1408.75f, 1573.5f), (474, 1185.5f, 1412.5f, 1586f), (480, 1175f, 1418.25f, 1601f),
            (486, 1169.5f, 1422.25f, 1612f),
        };

        struct Fit { public float s, lat, yaw, grade, err; public Vector3 carPos; public Quaternion carRot; public Vector3 camPos; public Quaternion camRot; public float vfov, shift; }

        static float EnvF(string name, float dflt)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(v) && float.TryParse(v.Trim(), NumberStyles.Float, CI, out float f) ? f : dflt;
        }

        /// <summary>"s,lat,yaw,grade" from the environment, or false.</summary>
        static bool EnvPose(string name, out float s, out float lat, out float yaw, out float grade)
        {
            s = lat = yaw = grade = 0f;
            string v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(v)) return false;
            var p = v.Split(',');
            if (p.Length != 4) return false;
            return float.TryParse(p[0].Trim(), NumberStyles.Float, CI, out s) &&
                   float.TryParse(p[1].Trim(), NumberStyles.Float, CI, out lat) &&
                   float.TryParse(p[2].Trim(), NumberStyles.Float, CI, out yaw) &&
                   float.TryParse(p[3].Trim(), NumberStyles.Float, CI, out grade);
        }

        static Vector3 RingMid(Ribbon rb, int k) => (rb.L[k] + rb.R[k]) * 0.5f;
        static Vector3 RingRight(Ribbon rb, int k) { var d = rb.R[k] - rb.L[k]; d.y = 0f; return d.normalized; }

        static void CarAt(Ribbon rb, float spacing, float s, float lat, float yawDeg, out Vector3 pos, out Quaternion rot)
        {
            int k = Mathf.Clamp(Mathf.FloorToInt(s / spacing), 0, rb.rings - 2);
            float f = Mathf.Clamp01(s / spacing - k);
            Vector3 c = Vector3.Lerp(RingMid(rb, k), RingMid(rb, k + 1), f);
            Vector3 r = Vector3.Lerp(RingRight(rb, k), RingRight(rb, k + 1), f).normalized;
            pos = c + r * lat;
            Vector3 fwd = Vector3.Cross(r, Vector3.up); // right x up = forward
            fwd.y = 0f;
            rot = Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        static Matrix4x4 ViewOf(Vector3 pos, Quaternion rot) =>
            Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(pos, rot, Vector3.one).inverse;

        static bool Pixel(Matrix4x4 vp, Vector3 w, out float x, out float yTop)
        {
            Vector4 c = vp * new Vector4(w.x, w.y, w.z, 1f);
            x = yTop = 0f;
            if (c.w <= 0.05f) return false;
            x = (c.x / c.w * 0.5f + 0.5f) * PhoneW;
            yTop = (0.5f - c.y / c.w * 0.5f) * PhoneH;
            return true;
        }

        /// <summary>The painted line at u, ring by ring from k0 to k1, in
        /// phone pixels (NaN where behind the lens). A ring is flat across, so
        /// the line at u is the lerp of its edges whatever the stride.</summary>
        static Vector2[] ProjectLine(Ribbon rb, Matrix4x4 vp, int k0, int k1, float u)
        {
            k1 = Mathf.Min(k1, rb.rings - 1);
            var o = new Vector2[Mathf.Max(0, k1 - k0 + 1)];
            for (int k = k0; k <= k1; k++)
            {
                Vector3 p = Vector3.Lerp(rb.L[k], rb.R[k], u);
                o[k - k0] = Pixel(vp, p, out float x, out float y) ? new Vector2(x, y) : new Vector2(float.NaN, float.NaN);
            }
            return o;
        }

        /// <summary>x where a projected line first crosses image row y
        /// walking away from the car; NaN if it never does.</summary>
        static float CrossRow(Vector2[] pts, float row)
        {
            for (int i = 1; i < pts.Length; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                if (float.IsNaN(a.x) || float.IsNaN(b.x)) continue;
                if ((a.y - row) * (b.y - row) <= 0f && Mathf.Abs(b.y - a.y) > 1e-4f)
                    return Mathf.Lerp(a.x, b.x, (row - a.y) / (b.y - a.y));
            }
            return float.NaN;
        }

        static Fit Search(Ribbon rb, float spacing, Transform t, ChaseCamera.CarFrame frame, ChaseCamera.HudDials dials, float aspect,
                          (int row, float L, float C, float R)[] owner, float uL, float uC, float uR,
                          float s0, float s1, float ds, float lat0, float lat1, float dl, float yaw0, float yaw1, float dy,
                          float g0, float g1, float dg, Fit best, SortedDictionary<float, float> perS = null)
        {
            for (float s = s0; s <= s1 + 1e-3f; s += ds)
            for (float gOff = g0; gOff <= g1 + 1e-3f; gOff += dg)
            {
                CarAt(rb, spacing, s, 0f, 0f, out Vector3 cp, out Quaternion cr);
                // The rig's grade is HELD (low-passed travel, kept below 2 m/s),
                // so it need not be this road's: searched as a free pitch.
                float grade = gOff;
                t.SetPositionAndRotation(cp, cr);
                ChaseCamera.SteadyPose(ChaseCamera.View.Chase, aspect, 0f, ChaseCamera.DefaultSpeedFullMps, t, frame, dials,
                                       out Vector3 camPos, out Quaternion camRot, out float vfov, out float shift, grade);
                Vector3 lp = Quaternion.Inverse(cr) * (camPos - cp);
                Quaternion lr = Quaternion.Inverse(cr) * camRot;
                var P = ChaseCamera.ShiftedProjection(vfov, aspect, 0.25f, 1000f, shift);
                int kc = Mathf.FloorToInt(s / spacing);
                for (float lat = lat0; lat <= lat1 + 1e-3f; lat += dl)
                    for (float yaw = yaw0; yaw <= yaw1 + 1e-3f; yaw += dy)
                    {
                        CarAt(rb, spacing, s, lat, yaw, out Vector3 p2, out Quaternion r2);
                        Vector3 eye = p2 + r2 * lp;
                        Quaternion er = r2 * lr;
                        var vp = P * ViewOf(eye, er);
                        var pl = ProjectLine(rb, vp, kc, kc + 60, uL);
                        var pc = ProjectLine(rb, vp, kc, kc + 60, uC);
                        var pr = ProjectLine(rb, vp, kc, kc + 60, uR);
                        float err = 0f;
                        foreach (var o in owner)
                        {
                            float xl = CrossRow(pl, o.row), xc = CrossRow(pc, o.row), xr = CrossRow(pr, o.row);
                            if (float.IsNaN(xl) || float.IsNaN(xc) || float.IsNaN(xr)) { err += 3f * 400f * 400f; continue; }
                            err += (xl - o.L) * (xl - o.L) + (xc - o.C) * (xc - o.C) + (xr - o.R) * (xr - o.R);
                            if (perS == null && err >= best.err) break;
                        }
                        if (perS != null && (!perS.TryGetValue(s, out float had) || err < had)) perS[s] = err;
                        if (err < best.err)
                            best = new Fit { s = s, lat = lat, yaw = yaw, grade = grade, err = err, carPos = p2, carRot = r2,
                                             camPos = eye, camRot = er, vfov = vfov, shift = shift };
                    }
            }
            return best;
        }

        [MenuItem("PSX Racing/Audits/Lane Shots (owner frames)")]
        public static void Shots()
        {
            Directory.CreateDirectory(OutDir);
            var log = new StringBuilder("LANE SHOTS " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CI) + "\n");
            string id = Environment.GetEnvironmentVariable("PSX_LANES_VENUE");
            if (string.IsNullOrEmpty(id)) id = "BlowingRock";
            var def = TrackCatalog.Scened.FirstOrDefault(d => d.id == id);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player))
            { Debug.LogError("[Lanes] cannot open " + id); return; }
            var path = Object.FindFirstObjectByType<TrackPath>();
            var road = GameObject.Find("Track")?.transform.Find("Road");
            var rb = road != null ? ReadRibbon(road.gameObject, log) : null;
            if (rb == null || rb.tex == null) { Debug.LogError("[Lanes] no ribbon"); return; }
            float uL = rb.tex.uEdgeL, uC = rb.tex.uCentre, uR = rb.tex.uEdgeR;
            log.AppendFormat(CI, "{0}: texture {1} u L/C/R {2:0.0000}/{3:0.0000}/{4:0.0000}, ribbon {5} vertices a ring\n",
                             id, Path.GetFileName(rb.tex.path), uL, uC, uR, rb.stride);
            float spacing = path.spacing;

            // Guard runs (Walls/Wall*): station span and side, for the stretch.
            var wallsGO = GameObject.Find("Walls");
            if (wallsGO != null)
                foreach (var r in wallsGO.GetComponentsInChildren<MeshFilter>())
                {
                    if (r.sharedMesh == null) continue;
                    var vv = r.sharedMesh.vertices;
                    int lo = int.MaxValue, hi = int.MinValue; float side = 0f;
                    for (int i = 0; i < vv.Length; i += Mathf.Max(1, vv.Length / 200))
                    {
                        Vector3 w = r.transform.TransformPoint(vv[i]);
                        int ni = path.NearestIndex(w, -1, path.Count);
                        if (ni < 60 || ni > 200) continue;
                        lo = Mathf.Min(lo, ni); hi = Mathf.Max(hi, ni);
                        side += Vector3.Dot(w - path.GetPoint(ni), Vector3.Cross(Vector3.up, path.GetTangent(ni)));
                    }
                    if (lo <= hi)
                        log.AppendFormat(CI, "  wall run {0} ({1}) covers wp {2}-{3} ({4:0}-{5:0} m) on the {6}\n",
                            r.name, r.GetComponent<MeshRenderer>() != null && r.GetComponent<MeshRenderer>().sharedMaterial != null ? r.GetComponent<MeshRenderer>().sharedMaterial.name : "?",
                            lo, hi, lo * spacing, hi * spacing, side > 0 ? "RIGHT" : "LEFT");
                }

            // The owner's car: a yellow RUF on the flat-six shell.
            var body = player.GetComponent<CarBody>();
            var spec = CarCatalog.All.FirstOrDefault(c => c.id == "ruf_ctr__yellow_bird___87");
            if (body != null && spec != null)
            {
                body.widthMm = spec.widthMm;
                var shell = CarModelLibrary.LoadFor(spec);
                if (shell != null) body.Apply(shell, shell.SkinFor("#e8c020", 0));
            }
            // Other cars off the stage so nothing parks in the frame.
            foreach (var cc in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (cc.gameObject != player) cc.transform.position += Vector3.down * 500f;

            // The owner's HUD dials, measured off his frame (fractions, y up).
            var dials = new ChaseCamera.HudDials
            {
                left = new Vector4(708f / PhoneW, (PhoneH - 883f) / PhoneH, 158f / PhoneW, 158f / PhoneH),
                right = new Vector4(1819f / PhoneW, (PhoneH - 883f) / PhoneH, 158f / PhoneW, 158f / PhoneH),
            };
            float aspect = PhoneW / (float)PhoneH;
            var t = player.transform;
            var frame = ChaseCamera.FrameOf(player);

            var fits = new List<(string tag, Fit fit)>();
            foreach (var (tag, owner, s0, s1, poseVar) in new[]
                     {
                         ("shot1", Owner1, EnvF("PSX_LANES_S1A", 380f), EnvF("PSX_LANES_S1B", 780f), "PSX_LANES_POSE1"),
                         ("shot2", Owner2, EnvF("PSX_LANES_S2A", 360f), EnvF("PSX_LANES_S2B", 760f), "PSX_LANES_POSE2"),
                     })
            {
                Fit bestFit;
                int nPts = owner.Length * 3;
                if (EnvPose(poseVar, out float ps, out float pl0, out float py, out float pg))
                {
                    // A fixed pose: the same camera the fit found on the old
                    // road, so a rebuilt one is seen from exactly there.
                    bestFit = Search(rb, spacing, t, frame, dials, aspect, owner, uL, uC, uR,
                                     ps, ps, 1f, pl0, pl0, 1f, py, py, 1f, pg, pg, 1f, new Fit { err = float.MaxValue });
                    log.AppendFormat(CI, "{0}: FIXED pose ({1}) s {2:0.0} m (wp {3:0.0}), lateral {4:+0.00;-0.00} m, yaw {5:+0.00;-0.00} deg, rig grade {6:+0.00;-0.00} deg; the owner's lines miss this road's by {7:0.0} px RMS\n",
                        tag, poseVar, bestFit.s, bestFit.s / spacing, bestFit.lat, bestFit.yaw, bestFit.grade, Mathf.Sqrt(bestFit.err / nPts));
                }
                else
                {
                    // coarse, then fine around the coarse best
                    var perS = new SortedDictionary<float, float>();
                    bestFit = Search(rb, spacing, t, frame, dials, aspect, owner, uL, uC, uR,
                                     s0, s1, 4f, -3f, 3f, 0.5f, -12f, 12f, 2f, -9f, 9f, 1.5f, new Fit { err = float.MaxValue }, perS);
                    var prof = new StringBuilder("  " + tag + " best RMS (px) per station, coarse:");
                    foreach (var kv in perS) prof.AppendFormat(CI, " {0:0}:{1:0.0}", kv.Key, Mathf.Sqrt(kv.Value / nPts));
                    log.AppendLine(prof.ToString());
                    bestFit = Search(rb, spacing, t, frame, dials, aspect, owner, uL, uC, uR,
                                     bestFit.s - 4f, bestFit.s + 4f, 1f, bestFit.lat - 0.5f, bestFit.lat + 0.5f, 0.1f,
                                     bestFit.yaw - 2f, bestFit.yaw + 2f, 0.25f, bestFit.grade - 1.5f, bestFit.grade + 1.5f, 0.25f, bestFit);
                    log.AppendFormat(CI, "{0}: best fit s {1:0.0} m (wp {2:0.0}), lateral {3:+0.00;-0.00} m from the ribbon centre, yaw {4:+0.00;-0.00} deg, rig grade {7:+0.00;-0.00} deg, RMS {5:0.0} px over {6} line crossings\n",
                        tag, bestFit.s, bestFit.s / spacing, bestFit.lat, bestFit.yaw, Mathf.Sqrt(bestFit.err / nPts), nPts, bestFit.grade);
                }
                {
                    var P = ChaseCamera.ShiftedProjection(bestFit.vfov, aspect, 0.25f, 1000f, bestFit.shift);
                    var vp = P * ViewOf(bestFit.camPos, bestFit.camRot);
                    int kc = Mathf.FloorToInt(bestFit.s / spacing);
                    var pl = ProjectLine(rb, vp, kc, kc + 60, uL); var pc = ProjectLine(rb, vp, kc, kc + 60, uC);
                    var pr = ProjectLine(rb, vp, kc, kc + 60, uR); var ph = ProjectLine(rb, vp, kc, kc + 60, 0.5f);
                    foreach (var o in owner)
                    {
                        float xl = CrossRow(pl, o.row), xc = CrossRow(pc, o.row), xr = CrossRow(pr, o.row), xh = CrossRow(ph, o.row);
                        log.AppendFormat(CI, "   row {0}: owner L {1:0} C {2:0} R {3:0} (L/R {4:0.000}) | this road L {5:0} C {6:0} R {7:0} (L/R {8:0.000}) | ribbon centre at {9:0} (L/R {10:0.000})\n",
                            o.row, o.L, o.C, o.R, (o.C - o.L) / (o.R - o.C), xl, xc, xr, (xc - xl) / (xr - xc), xh, (xh - xl) / (xr - xh));
                    }
                }
                fits.Add((tag, bestFit));
            }

            // ---- render: night, snow dress, headlights --------------------------
            RaceHandoff.CalendarDay = 0;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            if (sun != null) TimeOfDay.Apply(TimeOfDay.Night, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            NightGlow.PreviewAll(true);
            var dress = Object.FindFirstObjectByType<SeasonDress>();
            if (dress != null)
            {
                typeof(SeasonDress).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.Instance)?.Invoke(dress, null);
                dress.Apply(Seasons.DressSnow);
            }
            var roadR = road.GetComponent<MeshRenderer>();
            // The what-if texture only while the road's own is still off-centre.
            var fixedTex = PaintProblem(rb.tex) != null ? CentredTexture(rb.tex, path.roadWidth) : null;
            string built = Environment.GetEnvironmentVariable("PSX_LANES_TAG");
            if (string.IsNullOrWhiteSpace(built)) built = "asbuilt";

            foreach (var (tag, f) in fits)
            {
                t.SetPositionAndRotation(f.carPos, f.carRot);
                foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude))
                    lights.PreviewBuild(true);
                foreach (var hud in Object.FindObjectsByType<RaceHUD>(FindObjectsInactive.Exclude))
                { hud.PreviewMap(); HudOnTop.Apply(hud.gameObject); }
                CarLights.PushGlobals();
                foreach (bool fix in fixedTex != null ? new[] { false, true } : new[] { false })
                {
                    if (fix)
                    {
                        var mpb = new MaterialPropertyBlock();
                        mpb.SetTexture("_MainTex", fixedTex);
                        roadR.SetPropertyBlock(mpb, 0);
                    }
                    else roadR.SetPropertyBlock(new MaterialPropertyBlock(), 0);
                    string name = "lanes_" + id + "_" + tag + "_" + (fix ? "centred" : built);
                    Render(cam, f, aspect, 240, true, true, Path.Combine(OutDir, name + "_game.png"));
                    var big = Render(cam, f, aspect, PhoneH, false, false, Path.Combine(OutDir, name + "_2340.png"));
                    if (big != null)
                    {
                        log.AppendLine("  " + name + "_2340.png measured off the render (yellow/white columns):");
                        MeasureRender(big, (tag == "shot1" ? Owner1 : Owner2).Select(o => o.row).ToArray(), log);
                        Object.DestroyImmediate(big);
                    }
                }
                roadR.SetPropertyBlock(new MaterialPropertyBlock(), 0);
            }
            File.WriteAllText(Path.Combine(OutDir, "lane_shots.txt"), log.ToString());
            Debug.Log("[Lanes] shots\n" + log);
        }

        /// <summary>Both, in one Unity start (tools\lane-audit.ps1 -Method All).</summary>
        public static void All()
        {
            try { Run(); } catch (Exception e) { Debug.LogException(e); }
            try { Shots(); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>The same texture with the double yellow painted where the
        /// fixed painter puts it, total / 2 (the asphalt under the old line is
        /// the asphalt 40 columns to the left).</summary>
        static Texture2D CentredTexture(TexLines tl, float total)
        {
            if (tl == null || !tl.twoWay) return null;
            var px = (Color32[])tl.px.Clone();
            var yel = tl.lines.Where(l => l.kind == 'Y').ToList();
            for (int y = 0; y < tl.h; y++)
                foreach (var l in yel)
                    for (int x = l.x0; x <= l.x1; x++)
                        px[y * tl.w + x] = tl.px[y * tl.w + Mathf.Clamp(x - 40, 8, tl.w - 1)];
            float c = total * 0.5f;
            for (int x = 0; x < tl.w; x++)
            {
                float m = (x + 0.5f) / tl.w * total, d = Mathf.Abs(m - c);
                if (d > 0.06f && d < 0.18f)
                    for (int y = 0; y < tl.h; y++) px[y * tl.w + x] = PaintYellow;
            }
            var tex = new Texture2D(tl.w, tl.h, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, "centred_" + Path.GetFileName(tl.path)), tex.EncodeToPNG());
            return tex;
        }

        static Texture2D Render(Camera cam, Fit f, float aspect, int h, bool dither, bool hud, string file)
        {
            int w = Mathf.RoundToInt(h * aspect) & ~1;
            var hidden = new List<Canvas>();
            if (!hud)
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                    if (c.enabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera == cam)
                    { c.enabled = false; hidden.Add(c); }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var keepTarget = cam.targetTexture;
            cam.transform.SetPositionAndRotation(f.camPos, f.camRot);
            cam.fieldOfView = f.vfov;
            cam.nearClipPlane = 0.25f;
            cam.targetTexture = rt;
            cam.aspect = w / (float)h;
            cam.projectionMatrix = ChaseCamera.ShiftedProjection(f.vfov, w / (float)h, 0.25f, cam.farClipPlane, f.shift);
            StreetLights.Push(f.camPos, f.camRot * Vector3.forward);
            CarLights.PushGlobals();
            Canvas.ForceUpdateCanvases();
            Texture2D tex = null;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = dither ? PSXScreenshotTool.Dithered(rt) : rt;
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                if (h < PhoneH)
                {
                    // point-scale to the phone's size so it sits beside his frame
                    var big = new Texture2D(PhoneW, PhoneH, TextureFormat.RGB24, false);
                    var src = tex.GetPixels32(); var dst = new Color32[PhoneW * PhoneH];
                    for (int y = 0; y < PhoneH; y++)
                        for (int x = 0; x < PhoneW; x++)
                            dst[y * PhoneW + x] = src[Mathf.Min(h - 1, y * h / PhoneH) * w + Mathf.Min(w - 1, x * w / PhoneW)];
                    big.SetPixels32(dst); big.Apply();
                    File.WriteAllBytes(file, big.EncodeToPNG());
                    Object.DestroyImmediate(big);
                }
                else File.WriteAllBytes(file, tex.EncodeToPNG());
            }
            else Debug.LogWarning("[Lanes] RenderRequest unsupported");
            cam.targetTexture = keepTarget;
            cam.ResetProjectionMatrix();
            rt.Release();
            Object.DestroyImmediate(rt);
            foreach (var c in hidden) if (c != null) c.enabled = true;
            return tex;
        }

        static void MeasureRender(Texture2D tex, int[] rows, StringBuilder log)
        {
            if (tex.width != PhoneW || tex.height != PhoneH) return;
            var px = tex.GetPixels32();
            foreach (int row in rows)
            {
                int y = PhoneH - 1 - row;
                var yel = new List<int>(); var wht = new List<int>();
                for (int x = 700; x < 1800; x++)
                {
                    var p = px[y * PhoneW + x];
                    if (p.g > 110 && p.r > 100 && p.g - p.b > 40 && p.r - p.b > 35) yel.Add(x);
                    else if (Mathf.Min(p.r, Mathf.Min(p.g, p.b)) > 120 && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) - Mathf.Min(p.r, Mathf.Min(p.g, p.b)) < 30) wht.Add(x);
                }
                var Y = Runs(yel); var Wr = Runs(wht);
                float C = Y.Count >= 1 ? (Y.First() + Y.Last()) * 0.5f : float.NaN;
                float L = Wr.Where(v => v < C - 15).DefaultIfEmpty(float.NaN).Last();
                float R = Wr.Where(v => v > C + 15).DefaultIfEmpty(float.NaN).First();
                log.AppendFormat(CI, "     row {0}: L {1:0} C {2:0} R {3:0} ratio {4:0.000}\n", row, L, C, R, (C - L) / (R - C));
            }
        }

        static List<float> Runs(List<int> xs)
        {
            var o = new List<float>();
            int a = -99, b = -99;
            foreach (int x in xs)
            {
                if (x - b <= 3) { b = x; continue; }
                if (a >= 0 && b - a < 25) o.Add((a + b) * 0.5f);
                a = b = x;
            }
            if (a >= 0 && b - a < 25) o.Add((a + b) * 0.5f);
            return o;
        }
    }
}
