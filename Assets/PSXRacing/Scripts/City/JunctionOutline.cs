using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// c7roads round 5 (Docs/ROAD_RULES.md section 2): THE JUNCTION OUTLINE SOLVER as a pure function. No CityMap: the
    /// caller hands each arm's two border polylines (in its away frame: left / right of travel AWAY from the junction,
    /// sampled every Step metres out from the junction), its centre line and profile heights, its class radius, the
    /// cluster's internal links (a median opening's crossover: its two borders) and the holes (islands) it knows of; the
    /// solver returns one closed outline, each arm's END ROW (two points on its borders), the corner shapes, a height
    /// function and a fill.
    /// Corners, between each arm's LEFT border and the next arm's (anticlockwise) RIGHT border - never a point:
    ///   THROUGH   one road going on (headings 150 degrees or more apart, same name or one-way): straight where the two
    ///             borders meet, else a TAPER (a cubic leaving each border along its tangent);
    ///   NOSE      the arms run side by side (headings under 35 degrees apart) from borders under NoseGapM apart: the
    ///             fillet of radius NoseR (1.2 m) tangent to both borders, which sits where the gap is 2.4 m; the CUT LINE
    ///             through its two tangent points is built first and both arms' end rows lie on it (each arm's other
    ///             border is intersected with that line); the gore before it is inside the outline (paved);
    ///   ARC       StreetGen's construction on the real (curved) borders: offset each border by R into the corner,
    ///             intersect the offsets (the centre), the tangent points are the centre's feet; R by class, shrunk to fit;
    ///   CONNECTOR two arms of a cluster that start apart: through a LINK's border (the crossover of a median opening) by a
    ///             fillet onto it and a fillet off it (the largest R that fits: a full round nose in a narrow median);
    ///             with no link, a cubic leaving each border along its tangent.
    /// Every curve is sampled to SagM (1 cm) and, from a 6 m radius, MaxTurnDeg (6 degrees) a joint. The solver checks its
    /// own outline is simple and refuses (the caller falls back, with the reason) when it is not.
    /// HEIGHT: each end row takes its arm's own profile heights; inside, the arms' own planes (row height plus the arm's
    /// grade continued inward) blended by inverse squared distance to the rows: equal to each arm at its row, with its
    /// grade there, smooth between. A cluster whose rows rise more than MaxRiseM is refused.
    /// FILL: holes bridged to the outer ring (rightmost hole first, a ray to +x, the reflex check), ear-clipped (points on an
    /// ear's edge are not inside, zero-area ears from the bridge dropped), interior points every SteinerM, Lawson flips.
    /// </summary>
    public static class JunctionOutline
    {
        public const float Step = 0.5f, NoseGapM = 2.4f, NoseR = 1.2f, ThroughDeg = 150f, SideBySideDeg = 35f, SagM = 0.01f, MaxTurnDeg = 6f,
            SliverDeg = 5f, MaxRiseM = 2.5f, MaxPlaneGrade = 0.15f, SteinerM = 2.5f, EdgeM = 1.25f, MinRowM = 1f, TogetherM = 6f;

        public sealed class Arm
        {
            public List<Vector2> L = new List<Vector2>(), R = new List<Vector2>(), C = new List<Vector2>();
            public List<float> Y;              // profile height per station (null: flat 0)
            public float radius = 9f;          // the class's kerb-return radius
            public bool oneway; public string name = "";
            public int id;                     // the caller's (an edge index)
            // (road-surface stage 1) a border taken from the MEASURES SOURCE (JunctionMeasures): MergeBays does not widen
            // it (the measure is the pavement's own edge, bays included); the arm's row lies at or beyond measEnd, where
            // the border is the rule's again (the ribbon's own width)
            public bool measL, measR; public float measEnd;
            public bool fromA; public float edgeLength;   // the caller's: the arm leaves its edge's a end; the edge's length
            internal float ang, head;
            public float YAt(float s)
            {
                if (Y == null || Y.Count == 0) return 0f;
                float f = Mathf.Clamp(s / Step, 0f, Y.Count - 1);
                int i = Mathf.Min((int)f, Y.Count - 1), j = Mathf.Min(i + 1, Y.Count - 1);
                return Mathf.Lerp(Y[i], Y[j], f - i);
            }
        }
        /// <summary>An internal edge of the cluster (a median opening's crossover): its two borders, any direction.</summary>
        public sealed class Link { public List<Vector2> L = new List<Vector2>(), R = new List<Vector2>(); public int id; }
        /// <summary>(road-surface stage 1) A MEASURED MEDIAN END between arm a's left border and arm b's right border: the
        /// nose's front lies on the CAP line through capA and capB (two points of the front, read across the median), its
        /// two shoulders are fillets of radius r onto the borders (r at or over half the median's width: a full round).
        /// kind: 0 raised grass, 1 raised concrete (2, painted, is no nose at all: the measure widens the borders until
        /// they touch and the arms are abreast).</summary>
        public sealed class Nose { public int a, b; public Vector2 capA, capB; public float r = 1.2f; public int kind; }
        /// <summary>A MEASURED ISLAND or channel strip the map lacks: a short closed polygon, each vertex rounded by its
        /// own radius (a tangent arc; shrunk to fit its two edges), its top's kind and its kerb height.</summary>
        public sealed class Island { public List<Vector2> pts = new List<Vector2>(); public List<float> r = new List<float>(); public int kind = 1; public float kerb = 0.15f; }
        /// <summary>A MEASURED KERB RETURN between arm a's left border and arm b's right border.</summary>
        public sealed class Return { public int a, b; public float r; }
        /// <summary>(round 2) A corner PAVED UP TO A MEASURED LINE between arm a's left border and arm b's right border.
        /// open: the two arms run on as one pavement (a painted median or a painted triangle between converging arms):
        /// no corner, their rows on one cut line 'at' metres out. Else: the pavement's limit is the line through pts
        /// (from a's border to b's, e.g. across the mouth of a street the map lacks), a kerb along it.</summary>
        public sealed class Paved { public int a, b; public bool open; public float at; public List<Vector2> pts = new List<Vector2>(); }
        public sealed class Measures
        {
            public List<Paved> paved = new List<Paved>();
            public List<Nose> noses = new List<Nose>(); public List<Island> islands = new List<Island>(); public List<Return> returns = new List<Return>();
            /// <summary>Per arm (its id): its end row stands at least this far out - where the junction's own land (a
            /// measured median end) takes over from the ribbon's median.</summary>
            public Dictionary<int, float> rows = new Dictionary<int, float>();
            public string provider = "rules";
        }
        public sealed class Corner
        {
            public int kind = -1;              // a measured nose's kind (-1: the rule's)
            public int a, b; public string shape = ""; public float r, deg, sA, sB; public Vector2 tA, tB;
            public List<Vector2> pts = new List<Vector2>(); public string note = "";
            public Vector2 lineP, lineD;       // a nose's cut line
        }
        public sealed class Result
        {
            public List<Arm> arms;                       // sorted anticlockwise
            public List<Corner> corners = new List<Corner>();
            public Vector2[] rowR, rowL;                 // per sorted arm, its end row's two points
            public float[] sR, sL;                       // their stations on the arm's R / L borders (metres out)
            public float[] yR, yL, grade;                // the row's heights, the arm's grade there (rise per metre out)
            public float[] cut;                          // per sorted arm, the row's nearer station (metres out)
            public List<Vector2> outline = new List<Vector2>();
            public List<string> outlineSrc = new List<string>();   // per outline vertex: which piece made it (a diagnosis)
            public List<List<Vector2>> holes = new List<List<Vector2>>();
            public List<int> holeKind = new List<int>();          // per hole: -1 the map's island (the rule decides), 0 grass, 1 concrete
            public List<float> holeKerb = new List<float>();      // per hole: its kerb height (m)
            public string provider = "rules";                     // where the numbers came from (rules / hand / survey)
            // the fill's own boundary vertices (the outline and each hole with every edge cut to EdgeM): kerbs, island
            // solids and the ribbons' end rows are built on exactly these points (no T-junction against the pavement)
            public List<Vector2> ringOuter = new List<Vector2>();           // anticlockwise
            public List<List<Vector2>> ringHoles = new List<List<Vector2>>();   // clockwise, in the order of 'holes'
            public List<int> tris = new List<int>();     // into fillVerts, anticlockwise
            public List<Vector2> fillVerts = new List<Vector2>();
            public List<float> fillY = new List<float>();
            public int ringCount;                        // fillVerts below this are the (bridged) ring; the rest interior points
            public int slivers, flips, steiner; public string refused;   // null: solved
            public float planeDev, steinerM, rise, warp, plane; public int relaxed, droppedHoles;             // the worst interior point off its neighbours' plane; the interior spacing used
            public double solveMs, fillMs;

            public double[] quad; public Vector2 qc;     // the junction's quadratic surface (1, x, y, xx, xy, yy about qc)
            public float Quad(Vector2 p)
            {
                if (quad == null) return 0f;
                double x = p.x - qc.x, y = p.y - qc.y;
                return (float)(quad[0] + quad[1] * x + quad[2] * y + quad[3] * x * x + quad[4] * x * y + quad[5] * y * y);
            }
            Vector2 QuadGrad(Vector2 p)
            {
                if (quad == null) return Vector2.zero;
                double x = p.x - qc.x, y = p.y - qc.y;
                return new Vector2((float)(quad[1] + 2 * quad[3] * x + quad[4] * y), (float)(quad[2] + quad[4] * x + 2 * quad[5] * y));
            }

            /// <summary>The height at a point: the junction's quadratic (fitted to every row's heights and grades), plus each
            /// arm's remainder as a plane (its row's heights less the quadratic there; its grade less the quadratic's)
            /// blended by inverse squared distance to the rows. At a row: the arm's own heights and its own grade.</summary>
            public float HeightAt(Vector2 p)
            {
                if (rowR == null || arms == null) return 0f;
                double ws = 0, hs = 0;
                for (int i = 0; i < rowR.Length; i++)
                {
                    var a = rowR[i]; var b = rowL[i]; var ab = b - a;
                    // the remainder plane uses the row's LINE (its cross profile carried on past its ends: no crease where
                    // the nearest point leaves the row); the weight, the distance to the row's segment
                    float tu = ab.sqrMagnitude > 1e-6f ? Vector2.Dot(p - a, ab) / ab.sqrMagnitude : 0f, t = Mathf.Clamp01(tu);
                    var foot = a + ab * t;
                    var outward = new Vector2(ab.y, -ab.x).normalized;   // R -> L turned clockwise: away from the junction
                    // the remainder along the row (quadratic in t on the row, carried on straight past its ends: C1) and
                    // the remainder of the grade across it (held past the ends)
                    float e = yR[i] + (yL[i] - yR[i]) * t - Quad(foot);
                    float de = (yL[i] - yR[i]) - Vector2.Dot(QuadGrad(foot), ab);
                    e += de * (tu - t);
                    float gamma = grade[i] - Vector2.Dot(QuadGrad(foot), outward);
                    float h = e + gamma * Vector2.Dot(p - foot, outward);
                    float d2 = (foot - p).sqrMagnitude;
                    if (d2 < 1e-8f) return Quad(p) + h;
                    double w = 1.0 / d2;
                    ws += w; hs += w * h;
                }
                return Quad(p) + (ws > 0 ? (float)(hs / ws) : 0f);
            }
        }

        /// <summary>The least-squares quadratic through every row's heights (both ends and the middle) and with every arm's
        /// grade across its row (weighted by a 5 m length), the quadratic terms lightly held toward a plane.</summary>
        static void FitQuad(Result res)
        {
            int n = res.rowR.Length;
            var qc = Vector2.zero; for (int i = 0; i < n; i++) qc += res.rowR[i] + res.rowL[i]; qc /= 2 * n;
            var M = new double[6, 6]; var B = new double[6];
            void Add(double[] row, double v, double w)
            {
                for (int r = 0; r < 6; r++) { B[r] += w * row[r] * v; for (int c = 0; c < 6; c++) M[r, c] += w * row[r] * row[c]; }
            }
            const double L = 5.0;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k <= 2; k++)
                {
                    float t = k * 0.5f; var q = Vector2.Lerp(res.rowR[i], res.rowL[i], t) - qc;
                    Add(new double[] { 1, q.x, q.y, q.x * q.x, q.x * q.y, q.y * q.y }, Mathf.Lerp(res.yR[i], res.yL[i], t), 1.0);
                }
                var ab = res.rowL[i] - res.rowR[i]; var nrm = new Vector2(ab.y, -ab.x).normalized;
                var m = 0.5f * (res.rowR[i] + res.rowL[i]) - qc;
                Add(new double[] { 0, nrm.x * L, nrm.y * L, 2 * m.x * nrm.x * L, (m.y * nrm.x + m.x * nrm.y) * L, 2 * m.y * nrm.y * L }, res.grade[i] * L, 1.0);
            }
            for (int r = 3; r < 6; r++) M[r, r] += 1e-2 * 100.0;      // the quadratic terms held lightly (in 10 m units)
            M[1, 1] += 1e-6; M[2, 2] += 1e-6; M[0, 0] += 1e-6;
            var x = SolveLinear(M, B);
            if (x != null) { res.quad = x; res.qc = qc; }
        }
        static double[] SolveLinear(double[,] A, double[] b)
        {
            int n = b.Length; var a = (double[,])A.Clone(); var v = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int p = c; for (int r = c + 1; r < n; r++) if (System.Math.Abs(a[r, c]) > System.Math.Abs(a[p, c])) p = r;
                if (System.Math.Abs(a[p, c]) < 1e-12) return null;
                if (p != c) { for (int k = 0; k < n; k++) { var t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; } var tv = v[c]; v[c] = v[p]; v[p] = tv; }
                for (int r = c + 1; r < n; r++) { double f = a[r, c] / a[c, c]; for (int k = c; k < n; k++) a[r, k] -= f * a[c, k]; v[r] -= f * v[c]; }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--) { double s = v[r]; for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k]; x[r] = s / a[r, r]; }
            return x;
        }

        public static Result Solve(List<Arm> arms, List<List<Vector2>> holes = null, List<Link> links = null, Measures meas = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new Result { arms = new List<Arm>(arms) };
            if (meas != null) res.provider = meas.provider;
            if (arms.Count < 2) { res.refused = "fewer than two arms"; return res; }
            var centre = Vector2.zero; foreach (var a in arms) centre += a.C[0]; centre /= arms.Count;
            // arms that start together (one node, or side by side within a road's width) sort by their own heading;
            // arms of a cluster that start apart sort by where they are round the cluster's centre
            float spread = 0f, maxHalf = 0f;
            foreach (var a in arms) { spread = Mathf.Max(spread, (a.C[0] - centre).magnitude); maxHalf = Mathf.Max(maxHalf, Mathf.Max((a.L[0] - a.C[0]).magnitude, (a.R[0] - a.C[0]).magnitude)); }
            bool together = spread <= Mathf.Min(maxHalf * 1.5f + 1f, TogetherM);
            int k12 = Mathf.RoundToInt(12f / Step);
            foreach (var a in arms)
            {
                var p = a.C[Mathf.Min(a.C.Count - 1, k12)];
                var h = p - a.C[0];
                a.head = Mathf.Atan2(h.y, h.x) * Mathf.Rad2Deg;
                var from = together ? a.C[0] : centre;
                a.ang = Mathf.Atan2(p.y - from.y, p.x - from.x) * Mathf.Rad2Deg;
            }
            res.arms.Sort((p, q) => p.ang.CompareTo(q.ang));
            var A = res.arms; int n = A.Count;
            for (int i = 0; i < n; i++) res.corners.Add(SolveCorner(A[i], A[(i + 1) % n], links, meas));
            // (road-surface stage 1) MEASURED ISLANDS: rounded here; an arm's row lies beyond every island point that
            // stands in that arm's own pavement (an island is a hole: it may not cross a row), and beyond the arm's measure
            var isl = new List<List<Vector2>>(); var reach = new float[n];
            if (meas != null) foreach (var im in meas.islands) isl.Add(RoundedPolygon(im.pts, im.r));
            for (int i = 0; i < n; i++)
            {
                var a = A[i]; reach[i] = a.measEnd;
                if (meas != null && meas.rows.TryGetValue(a.id, out float rowMin)) reach[i] = Mathf.Max(reach[i], rowMin);
                foreach (var ring in isl) foreach (var p in ring)
                {
                    Project(a.C, p, out var f, out float st);
                    if (st <= 0f || st >= (a.C.Count - 1) * Step) continue;
                    int k = Mathf.Clamp(Mathf.RoundToInt(st / Step), 0, a.C.Count - 1);
                    var t = Tangent(a.C, k); var left = new Vector2(-t.y, t.x);
                    float lat = Vector2.Dot(p - a.C[k], left);
                    if (lat <= Vector2.Dot(a.L[k] - a.C[k], left) + 0.5f && lat >= Vector2.Dot(a.R[k] - a.C[k], left) - 0.5f) reach[i] = Mathf.Max(reach[i], st + IslandRowM);
                }
                reach[i] = Mathf.Min(reach[i], (a.C.Count - 2) * Step);
            }
            foreach (var c in res.corners) if (c.shape.EndsWith("not-found") || c.shape.EndsWith("did-not-fit")) { res.refused = $"corner {c.a}/{c.b}: {c.shape} ({c.note})"; }
            // END ROWS. An arm beside a nose: its row lies ON the nose's cut line (the farther, if two). Otherwise square
            // across the arm at the farther of its corners' tangent stations (at least MinRowM out).
            res.rowR = new Vector2[n]; res.rowL = new Vector2[n]; res.sR = new float[n]; res.sL = new float[n];
            res.yR = new float[n]; res.yL = new float[n]; res.grade = new float[n]; res.cut = new float[n];
            // ABREAST corners first: their one cut line, square across the left arm where both arms run side by side and
            // beyond the two arms' other corners
            for (int i = 0; i < n; i++)
            {
                var c = res.corners[i]; if (c.shape != "abreast") continue;
                var a = A[i]; var b = A[(i + 1) % n];
                float s0 = Mathf.Max(Mathf.Max(c.sA, reach[i]), Mathf.Max(MinRowM, res.corners[(i - 1 + n) % n].sB));
                if (reach[(i + 1) % n] > 0f) { int kr = Mathf.Min(b.C.Count - 1, Mathf.CeilToInt(reach[(i + 1) % n] / Step)); Project(a.C, b.C[kr], out _, out float sr); s0 = Mathf.Max(s0, sr); }
                var bl = res.corners[(i + 1) % n];   // b's left corner: its station on b, carried across to a
                if (bl.sA > 0f) { int kb = Mathf.Min(b.C.Count - 1, Mathf.CeilToInt(bl.sA / Step) + 2); Project(a.C, b.C[kb], out _, out float sa2); s0 = Mathf.Max(s0, sa2 + Step); }
                bool cut = AbreastLine(a, b, s0, c);
                // (road-surface stage 1; spot 77) no room for the cut because b's own kerb return runs far along it (the class
                // radius at an acute corner: r 12 m took 28.7 m of a 35 m arm): that return is shrunk (x 0.7, at most four
                // times; never a measured one) until the row fits beyond its tangent point
                if (!cut && bl.shape == "arc" && bl.note != "measured")
                {
                    var after = A[(i + 2) % n];
                    float r2 = bl.r;
                    for (int t = 0; t < 4 && !cut; t++)
                    {
                        r2 *= 0.7f;
                        var c2 = new Corner { a = bl.a, b = bl.b, deg = bl.deg };
                        if (!Fillet(b.L, after.R, r2, c2)) break;
                        c2.shape = "arc"; c2.note = $"shrunk from r {bl.r:0.0} for the abreast row";
                        float s2 = Mathf.Max(c.sA, Mathf.Max(MinRowM, res.corners[(i - 1 + n) % n].sB));
                        s2 = Mathf.Max(s2, reach[i]);
                        int kb2 = Mathf.Min(b.C.Count - 1, Mathf.CeilToInt(c2.sA / Step) + 2); Project(a.C, b.C[kb2], out _, out float sa3); s2 = Mathf.Max(s2, sa3 + Step);
                        if (AbreastLine(a, b, s2, c)) { cut = true; res.corners[(i + 1) % n] = c2; }
                    }
                }
                if (!cut) { c.shape = "abreast-not-found"; res.refused ??= $"corner {c.a}/{c.b}: abreast arms with no cut line across both"; }
            }
            var noseRow = new bool[n];
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                var a = A[i]; var left = res.corners[i]; var right = res.corners[(i - 1 + n) % n];
                bool nl = left.shape == "nose" || left.shape == "abreast", nr = right.shape == "nose" || right.shape == "abreast";
                if ((nl || nr) != (pass == 0)) continue;   // the noses' rows first: a taper beside one ends before it
                if (nl || nr)
                {
                    noseRow[i] = true;
                    bool useLeft = nl && (!nr || left.sA >= right.sB);
                    var nc = useLeft ? left : right;
                    if (useLeft)
                    {
                        res.rowL[i] = nc.tA; res.sL[i] = nc.sA;
                        if (!LineCross(a.R, nc.lineP, nc.lineD, nc.tA, out res.rowR[i], out res.sR[i])) { res.refused ??= $"arm {a.id}: the nose's cut line misses its right border"; res.rowR[i] = a.R[a.R.Count - 1]; res.sR[i] = (a.R.Count - 1) * Step; }
                    }
                    else
                    {
                        res.rowR[i] = nc.tB; res.sR[i] = nc.sB;
                        if (!LineCross(a.L, nc.lineP, nc.lineD, nc.tB, out res.rowL[i], out res.sL[i])) { res.refused ??= $"arm {a.id}: the nose's cut line misses its left border"; res.rowL[i] = a.L[a.L.Count - 1]; res.sL[i] = (a.L.Count - 1) * Step; }
                    }
                    // the arm's other corner must end before the row (a taper is shortened to)
                    if (!useLeft && left.shape == "through-taper" && left.sA > res.sL[i] - Step) Through(a, A[(i + 1) % n], left, res.sL[i] - Step, noseRow[(i + 1) % n] ? res.sR[(i + 1) % n] - Step : float.MaxValue);
                    if (useLeft && right.shape == "through-taper" && right.sB > res.sR[i] - Step) Through(A[(i - 1 + n) % n], a, right, noseRow[(i - 1 + n) % n] ? res.sL[(i - 1 + n) % n] - Step : float.MaxValue, res.sR[i] - Step);
                    if (!useLeft && left.sA > res.sL[i] + 1e-3f) res.refused ??= $"arm {a.id}: its left corner ({left.shape}) runs past the nose's cut line";
                    if (useLeft && right.sB > res.sR[i] + 1e-3f) res.refused ??= $"arm {a.id}: its right corner ({right.shape}) runs past the nose's cut line";
                }
                else
                {
                    float s = Mathf.Max(Mathf.Max(MinRowM, reach[i]), Mathf.Max(left.sA, right.sB));
                    int k = Mathf.Min(a.L.Count - 1, Mathf.CeilToInt(s / Step - 1e-4f));
                    res.rowR[i] = a.R[k]; res.rowL[i] = a.L[k]; res.sR[i] = res.sL[i] = k * Step;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var a = A[i];
                res.cut[i] = Mathf.Min(res.sR[i], res.sL[i]);
                res.yR[i] = a.YAt(res.sR[i]); res.yL[i] = a.YAt(res.sL[i]);
                float sm = 0.5f * (res.sR[i] + res.sL[i]);
                res.grade[i] = (a.YAt(sm + 2f) - a.YAt(sm)) / 2f;
            }
            FitQuad(res);
            // THE RISE RULE (replacing the old flat 2.5 m limit): a junction on a steady slope may rise any height; what is
            // limited is the WARP, the rows' distance from the one plane that fits them best: over MaxRiseM / 2 (1.25 m)
            // the rows cannot share a smooth surface and the junction is refused with both numbers
            float ylo = float.MaxValue, yhi = float.MinValue;
            for (int i = 0; i < n; i++) { ylo = Mathf.Min(ylo, Mathf.Min(res.yR[i], res.yL[i])); yhi = Mathf.Max(yhi, Mathf.Max(res.yR[i], res.yL[i])); }
            res.rise = yhi - ylo; res.warp = Warp(res);
            if (res.warp <= MaxRiseM * 0.5f && res.plane > MaxPlaneGrade) res.refused ??= $"the junction's rows lie on a {res.plane * 100f:0} % slope (over {MaxPlaneGrade * 100f:0} %; it rises {res.rise:0.00} m)";
            if (res.warp > MaxRiseM * 0.5f) res.refused ??= $"the junction's rows lie {res.warp:0.00} m off one plane (over {MaxRiseM * 0.5f} m; it rises {res.rise:0.00} m)";
            // THE OUTLINE: arm by arm, its row right -> left, in along its left border to the corner, the corner, out along
            // the next arm's right border to that arm's row (a shared point is one value)
            var o = res.outline; var src = res.outlineSrc;
            for (int i = 0; i < n; i++)
            {
                var a = A[i]; var c = res.corners[i]; int j = (i + 1) % n; var b = A[j];
                o.Add(res.rowR[i]); src.Add($"e{a.id} end row"); o.Add(res.rowL[i]); src.Add($"e{a.id} end row");
                for (int k = Mathf.CeilToInt(res.sL[i] / Step - 1e-4f) - 1; k * Step > c.sA + 1e-3f; k--) if (k >= 0 && k < a.L.Count) { o.Add(a.L[k]); src.Add($"e{a.id} left border"); }
                foreach (var q in c.pts) { o.Add(q); src.Add($"e{c.a}/e{c.b} {c.shape}{(c.note.Length > 0 ? " (" + c.note + ")" : "")}"); }
                for (int k = Mathf.FloorToInt(c.sB / Step + 1e-4f) + 1; k * Step < res.sR[j] - 1e-3f; k++) if (k >= 0 && k < b.R.Count) { o.Add(b.R[k]); src.Add($"e{b.id} right border"); }
            }
            Dedupe(o, src);
            res.relaxed = RelaxKinks(o, src);
            string cross = SelfCross(o, src);
            if (cross != null) res.refused ??= "the outline crosses itself: " + cross;
            // an island counts only if it lies wholly inside the outline (one reaching past it is the land beside the junction)
            // (road-surface stage 1) with a measure the junction's islands are the measured ones: the raster's (land the
            // rule's ribbons leave uncovered) describe the rule's pavement, not the measured one
            if (holes != null && (meas == null || meas.provider == "rules")) foreach (var h in holes) { var hh = new List<Vector2>(h); Dedupe(hh); if (hh.Count < 3) continue; bool inside = true; foreach (var q in hh) if (!InPoly(o, q)) { inside = false; break; } if (inside && SelfCross(hh) == null) { res.holes.Add(hh); res.holeKind.Add(-1); res.holeKerb.Add(0.15f); } else res.droppedHoles++; }
            for (int q = 0; q < isl.Count; q++)
            {
                var hh = new List<Vector2>(isl[q]); Dedupe(hh);
                bool inside = hh.Count >= 3; foreach (var p in hh) if (!InPoly(o, p)) { inside = false; break; }
                if (inside && SelfCross(hh) == null) { res.holes.Add(hh); res.holeKind.Add(meas.islands[q].kind); res.holeKerb.Add(meas.islands[q].kerb); }
                else res.refused ??= $"measured island {q} does not lie inside the outline (its rows are too near or it crosses a kerb)";
            }
            res.solveMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            if (res.refused == null) Fill(res);
            res.fillMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        /// <summary>A corner the MAP put in an arm's or a link's border (an OSM vertex the line model carries: 11 to 24
        /// degrees at 80 and g32) is relaxed where it lies inside the outline's own stretch of that border: the vertex and
        /// its neighbours moved toward their neighbours' mean (end rows held) until no joint breaks rule 4, at most 30
        /// rounds. Returns how many vertices moved (the ribbon beyond the row keeps the map's corner: a line-model fault).</summary>
        static int RelaxKinks(List<Vector2> o, List<string> src)
        {
            int m = o.Count, moved = 0;
            if (m < 5) return 0;
            for (int it = 0; it < 80; it++)
            {
                var bad = new List<int>();
                for (int i = 0; i < m; i++)
                {
                    if (!src[i].EndsWith("border")) continue;   // only the map's own lines: arcs, tapers and noses are built to rule 4
                    if (Kink(o[(i - 1 + m) % m], o[i], o[(i + 1) % m])) bad.Add(i);
                }
                if (bad.Count == 0) break;
                var cp = new List<Vector2>(o);
                foreach (int b0 in bad)
                    for (int d = -2; d <= 2; d++)
                    {
                        int i = (b0 + d + m) % m;
                        if (!src[i].EndsWith("border")) continue;
                        o[i] = 0.5f * cp[i] + 0.25f * (cp[(i - 1 + m) % m] + cp[(i + 1) % m]);
                        moved++;
                    }
            }
            return moved;
        }

        /// <summary>The rows' worst distance (height) from their least-squares plane.</summary>
        static float Warp(Result res)
        {
            var M = new double[3, 3]; var B = new double[3]; var pts = new List<(Vector2 p, float y)>();
            for (int i = 0; i < res.rowR.Length; i++) { pts.Add((res.rowR[i], res.yR[i])); pts.Add((res.rowL[i], res.yL[i])); }
            var o = pts[0].p;
            foreach (var q in pts) { var d = q.p - o; double[] r = { d.x, d.y, 1 }; for (int a = 0; a < 3; a++) { B[a] += r[a] * q.y; for (int b = 0; b < 3; b++) M[a, b] += r[a] * r[b]; } }
            M[0, 0] += 1e-6; M[1, 1] += 1e-6;
            var x = SolveLinear(M, B); if (x == null) return 0f;
            float w = 0f; foreach (var q in pts) { var d = q.p - o; w = Mathf.Max(w, Mathf.Abs(q.y - (float)(x[0] * d.x + x[1] * d.y + x[2]))); }
            res.plane = (float)System.Math.Sqrt(x[0] * x[0] + x[1] * x[1]);
            return w;
        }

        static void Dedupe(List<Vector2> o, List<string> src)
        {
            for (int i = o.Count - 1; i > 0; i--) if ((o[i] - o[i - 1]).sqrMagnitude < 1e-6f) { o.RemoveAt(i); src.RemoveAt(i); }
            if (o.Count > 1 && (o[0] - o[o.Count - 1]).sqrMagnitude < 1e-6f) { o.RemoveAt(o.Count - 1); src.RemoveAt(src.Count - 1); }
        }
        static void Dedupe(List<Vector2> o)
        {
            for (int i = o.Count - 1; i > 0; i--) if ((o[i] - o[i - 1]).sqrMagnitude < 1e-6f) o.RemoveAt(i);
            if (o.Count > 1 && (o[0] - o[o.Count - 1]).sqrMagnitude < 1e-6f) o.RemoveAt(o.Count - 1);
        }

        /// <summary>Null if the ring is simple, else where it crosses.</summary>
        public static string SelfCross(List<Vector2> o, List<string> src = null)
        {
            int n = o.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegXStrict(o[i], o[(i + 1) % n], o[j], o[(j + 1) % n]))
                        return src == null ? $"at {o[i]} / {o[j]}" : $"[{Piece(src, i)}] at {o[i]} crosses [{Piece(src, j)}] at {o[j]}";
                }
            return null;
        }
        /// <summary>What an outline segment (i -> i+1) is made of: its two vertices' sources (one if the same).</summary>
        public static string Piece(List<string> src, int i)
        {
            string a = src[i], b = src[(i + 1) % src.Count];
            return a == b ? a : a + " -> " + b;
        }
        static bool SegXStrict(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var r = b - a; var s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            var q = c - a;
            float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
            return t > 1e-4f && t < 1f - 1e-4f && u > 1e-4f && u < 1f - 1e-4f;
        }

        // ---- corners ----
        static Corner SolveCorner(Arm A, Arm B, List<Link> links, Measures meas = null)
        {
            var c = new Corner { a = A.id, b = B.id };
            // (road-surface stage 1) MEASURED: a median end (its cap line and radius) or a kerb return's radius
            if (meas != null)
            {
                foreach (var pv in meas.paved) if (pv.a == A.id && pv.b == B.id)
                {
                    if (pv.open) { c.shape = "abreast"; c.sA = pv.at; c.note = "paved to a measured line"; return c; }
                    if (PavedCorner(A, B, pv, c)) return c;
                    c.shape = "paved-not-found"; c.note = "the measured line does not start and end on the two borders"; c.tA = A.L[0]; c.tB = B.R[0]; return c;
                }
                foreach (var ns in meas.noses) if (ns.a == A.id && ns.b == B.id)
                {
                    if (MeasuredNose(A, B, ns, c)) return c;
                    c.shape = "nose-not-found"; c.note = "the measured cap line does not meet both borders"; c.tA = A.L[0]; c.tB = B.R[0]; return c;
                }
                foreach (var rt in meas.returns) if (rt.a == A.id && rt.b == B.id)
                {
                    c.deg = Mathf.Repeat(B.ang - A.ang, 360f);
                    if (Fillet(A.L, B.R, rt.r, c)) { c.shape = "arc"; c.note = "measured"; return c; }
                    c.shape = "arc-did-not-fit"; c.note = $"the measured radius {rt.r:0.0} m does not fit the borders"; c.tA = A.L[0]; c.tB = B.R[0]; return c;
                }
            }
            float dAng = Mathf.Repeat(B.ang - A.ang, 360f);
            float dHead = Mathf.Abs(Mathf.DeltaAngle(A.head, B.head));
            c.deg = dAng;
            if (dHead >= ThroughDeg && (A.name == B.name || A.oneway || B.oneway)) { Through(A, B, c); return c; }
            float gap0 = Gap0(A, B);
            // ABREAST: two arms from different nodes of a cluster running side by side with their borders touching or
            // overlapping (one paved surface, e.g. two carriageways drawn wider than their median): no corner between
            // them, both rows on one cut line (built in Solve), the two borders meeting there
            if (dHead < SideBySideDeg && Abreast(A, B, out float sAb)) { c.shape = "abreast"; c.sA = sAb; return c; }
            if (dHead < SideBySideDeg && gap0 < NoseGapM)
            {
                if (Fillet(A.L, B.R, NoseR, c)) { c.shape = "nose"; c.lineP = c.tA; c.lineD = (c.tB - c.tA).normalized; return c; }
                c.shape = "nose-not-found"; c.note = "no 1.2 m nose fits between the borders given"; c.tA = A.L[0]; c.tB = B.R[0];
                return c;
            }
            // a cluster's link (a crossover, a U-turn lane's crossing) between the two borders: the median's nose rounds
            // off against it, before any arc straight across the median
            if (gap0 >= NoseGapM && links != null && links.Count > 0 && ViaLink(A, B, links, c)) return c;
            if (TryArc(A, B, c)) return c;   // any angle: an acute corner (a crossover leaving at 30 degrees) is a kerb return too
            if (gap0 >= NoseGapM && !Cross(A.L, B.R, out _, out _, out _))
            {
                c.shape = "connector"; c.tA = A.L[0]; c.tB = B.R[0];
                var tIn = -Tangent(A.L, 0); var tOut = Tangent(B.R, 0);
                float k = Vector2.Distance(c.tA, c.tB);
                Hermite(c.tA, tIn * k, c.tB, tOut * k, c.pts);
                return c;
            }
            c.shape = "arc-did-not-fit"; c.note = "no fillet down to 0.12 x the class radius and no nose";
            c.tA = A.L[0]; c.tB = B.R[0];
            return c;
        }

        /// <summary>Side by side with touching or overlapping borders: from within 30 m out along A, for 20 m, B's right
        /// border stays within 0.3 m of A's left border or on A's side of it. Returns where that run starts on A.</summary>
        static bool Abreast(Arm A, Arm B, out float s0)
        {
            s0 = 0f;
            if ((A.C[0] - B.C[0]).magnitude < 1f) return false;   // one node: a nose or a split, not abreast
            int run = Mathf.RoundToInt(20f / Step);
            for (int k = 0; k * Step <= 30f && k < A.L.Count; k++)
            {
                int ok = 0;
                for (int q = k; q < A.L.Count && q < k + run; q++)
                {
                    Project(B.R, A.L[q], out var f, out float st);
                    if (st <= 0f || st >= (B.R.Count - 1) * Step) break;
                    var t = Tangent(A.L, q); var left = new Vector2(-t.y, t.x);
                    if (Vector2.Dot(f - A.L[q], left) > 0.3f) break;
                    ok++;
                }
                if (ok >= run) { s0 = k * Step; return true; }
            }
            return false;
        }
        /// <summary>The abreast corner's cut line: square across A at station s0 (or the first beyond it that crosses B's
        /// two borders); tA on A's left border, tB on B's right; where they overlap, one shared point between.</summary>
        static bool AbreastLine(Arm A, Arm B, float s0, Corner c)
        {
            for (int k = Mathf.CeilToInt(s0 / Step); k < A.C.Count; k++)
            {
                var t = Tangent(A.C, k); var nrm = new Vector2(-t.y, t.x);
                if (!LineCross(A.L, A.C[k], nrm, A.L[k], out var pa, out float sa)) continue;
                if (!LineCross(B.R, A.C[k], nrm, A.L[k], out var pb, out float sb)) continue;
                if (!LineCross(B.L, A.C[k], nrm, A.L[k], out _, out _)) continue;
                if (sb < MinRowM) continue;
                c.lineP = A.C[k]; c.lineD = nrm;
                if (Vector2.Dot(pb - pa, nrm) < 0f) { var m = 0.5f * (pa + pb); pa = m; pb = m; }
                c.tA = pa; c.tB = pb; c.sA = sa; c.sB = sb;
                c.pts.Clear(); c.pts.Add(pa); if ((pb - pa).sqrMagnitude > 1e-6f) c.pts.Add(pb);
                return true;
            }
            return false;
        }

        static float Gap0(Arm A, Arm B)
        {
            // how far apart the two borders start: the nearer of A.L[0] to B.R and B.R[0] to A.L, signed negative where
            // they overlap (A's left border starts on B's side of B's right border)
            Project(B.R, A.L[0], out var f, out _);
            return Vector2.Distance(A.L[0], f);
        }

        /// <summary>One road going on: straight where the borders meet, else a taper leaving each along its tangent.</summary>
        static void Through(Arm A, Arm B, Corner c, float maxA = float.MaxValue, float maxB = float.MaxValue)
        {
            c.shape = "through"; c.pts.Clear();
            float off = Vector2.Distance(A.L[0], B.R[0]);
            if (off < 1e-3f) { c.tA = A.L[0]; c.tB = B.R[0]; c.sA = c.sB = 0f; c.pts.Add(c.tA); return; }
            // a taper: half its length on each border, 6 x the offset in all (3 m to 30 m); on an arm whose row a nose
            // fixes, it ends a step before that row
            float half = Mathf.Clamp(3f * off, 1.5f, 15f);
            int ka = Mathf.Min(A.L.Count - 1, Mathf.CeilToInt(Mathf.Min(half, maxA) / Step)), kb = Mathf.Min(B.R.Count - 1, Mathf.CeilToInt(Mathf.Min(half, maxB) / Step));
            if (Mathf.Min(half, maxA) < half) ka = Mathf.Max(0, Mathf.FloorToInt(maxA / Step));
            if (Mathf.Min(half, maxB) < half) kb = Mathf.Max(0, Mathf.FloorToInt(maxB / Step));
            c.shape = "through-taper"; c.r = off;
            c.tA = A.L[ka]; c.tB = B.R[kb]; c.sA = ka * Step; c.sB = kb * Step;
            float k = Vector2.Distance(c.tA, c.tB);
            Hermite(c.tA, -Tangent(A.L, ka) * k, c.tB, Tangent(B.R, kb) * k, c.pts);
        }

        static Vector2 Tangent(List<Vector2> pl, int i) => (pl[Mathf.Min(i + 1, pl.Count - 1)] - pl[Mathf.Max(i - 1, 0)]).normalized;

        static bool TryArc(Arm A, Arm B, Corner c)
        {
            float R = A.radius > 0f && B.radius > 0f ? Mathf.Min(A.radius, B.radius) : Mathf.Max(A.radius, B.radius);
            for (int t = 0; t < 6; t++, R *= 0.7f)
                if (Fillet(A.L, B.R, R, c)) { c.shape = "arc"; return true; }
            return false;
        }

        /// <summary>StreetGen's fillet of radius R between P (land on its left: an arm's L border, out from the junction)
        /// and Q (land on its right): offset each by R onto the land, intersect (the centre), the tangent points are the
        /// centre's feet on P and Q (exact, between samples); the arc from P's to Q's clockwise round the centre.</summary>
        static bool Fillet(List<Vector2> P0, List<Vector2> Q0, float R, Corner c)
        {
            // each border carried on straight BEHIND its start (BackM): at a skew crossing of wide roads the obtuse corner's
            // fillet touches the through road's edge behind the node (on the other arm's side); stations there are negative
            var P = Extend(P0, out int nP); var Q = Extend(Q0, out int nQ);
            var oP = new List<Vector2>(P.Count); var oQ = new List<Vector2>(Q.Count);
            for (int i = 0; i < P.Count; i++) oP.Add(Offset(P, i, R, true));
            for (int i = 0; i < Q.Count; i++) oQ.Add(Offset(Q, i, R, false));
            if (!Cross(oP, oQ, out _, out _, out Vector2 cc)) return false;
            Project(P, cc, out c.tA, out c.sA); c.sA -= nP * Step;
            Project(Q, cc, out c.tB, out c.sB); c.sB -= nQ * Step;
            if (Mathf.Abs((c.tA - cc).magnitude - R) > 0.05f * R + 0.02f || Mathf.Abs((c.tB - cc).magnitude - R) > 0.05f * R + 0.02f) return false;   // a foot off the end
            // a kerb return turns less than a half circle (clockwise from P's foot to Q's)
            float a0 = Mathf.Atan2(c.tA.y - cc.y, c.tA.x - cc.x), a1 = Mathf.Atan2(c.tB.y - cc.y, c.tB.x - cc.x), sw = a1 - a0;
            while (sw > 0f) sw -= 2f * Mathf.PI;
            if (sw < -Mathf.PI * 1.001f || sw > -1e-4f) return false;
            c.r = R; c.pts.Clear();
            ArcThrough(cc, c.tA, c.tB, R, c.pts, false);
            return true;
        }

        const float BackM = 20f;
        static List<Vector2> Extend(List<Vector2> pl, out int n)
        {
            n = Mathf.RoundToInt(BackM / Step);
            var t = Tangent(pl, 0);
            var o = new List<Vector2>(pl.Count + n);
            for (int k = n; k >= 1; k--) o.Add(pl[0] - t * (k * Step));
            o.AddRange(pl);
            return o;
        }

        /// <summary>A median opening's nose: from A's left border onto a link's border (a fillet), along it, off it onto B's
        /// right border (a fillet); the largest radius up to the class's that keeps the two fillets in order on the link.</summary>
        static bool ViaLink(Arm A, Arm B, List<Link> links, Corner c) => ViaLink(A, B, links, c, true) || ViaLink(A, B, links, c, false);
        static bool ViaLink(Arm A, Arm B, List<Link> links, Corner c, bool strict)
        {
            var dirA = Tangent(A.L, 0); var dirB = Tangent(B.R, 0);
            foreach (var lk in links)
                for (int side = 0; side < 2; side++)
                {
                    var X = new List<Vector2>(side == 0 ? lk.L : lk.R);
                    if (X.Count < 2) continue;
                    var mid = X[X.Count / 2];
                    // the border on this corner's land: out along A from A's start and out along B from B's start
                    // (first a border ahead of both starts; then, a crossover ending AT its two nodes, one ahead of either)
                    bool aheadA = Vector2.Dot(mid - A.L[0], dirA) > 0f, aheadB = Vector2.Dot(mid - B.R[0], dirB) > 0f;
                    if (strict ? !(aheadA && aheadB) : !(aheadA || aheadB)) continue;
                    // from A's side to B's side
                    if ((X[0] - A.L[0]).sqrMagnitude > (X[X.Count - 1] - A.L[0]).sqrMagnitude) X.Reverse();
                    // and the link's road on its left as the outline runs along it (the land, the nose, on its right)
                    var other = side == 0 ? lk.R : lk.L; var om = other[other.Count / 2];
                    var xd = X[Mathf.Min(X.Count / 2 + 1, X.Count - 1)] - X[Mathf.Max(X.Count / 2 - 1, 0)];
                    if (xd.x * (om - mid).y - xd.y * (om - mid).x <= 0f) continue;
                    var Xr = new List<Vector2>(X); Xr.Reverse();
                    float R = Mathf.Min(A.radius, B.radius);
                    float lo = 0.3f, hi = R; Corner best1 = null, best2 = null;
                    for (int it = 0; it < 18; it++)
                    {
                        float r = it == 0 ? hi : 0.5f * (lo + hi);
                        var f1 = new Corner(); var f2 = new Corner();
                        bool ok = Fillet(A.L, X, r, f1) && Fillet(Xr, B.R, r, f2);
                        // in order on the link: f1's foot on X before f2's (f2's station is on X reversed)
                        float len = (X.Count - 1) * Step;
                        if (ok && f1.sB <= len - f2.sA + 1e-3f) { best1 = f1; best2 = f2; if (it == 0) break; lo = r; } else hi = r;
                    }
                    if (best1 == null) continue;
                    c.shape = "connector"; c.note = $"via link {lk.id}"; c.r = best1.r;
                    c.tA = best1.tA; c.sA = best1.sA; c.tB = best2.tB; c.sB = best2.sB;
                    c.pts.AddRange(best1.pts);
                    float s1 = best1.sB, s2 = (X.Count - 1) * Step - best2.sA;
                    for (int k = Mathf.FloorToInt(s1 / Step + 1e-4f) + 1; k * Step < s2 - 1e-3f && k < X.Count; k++) if (k >= 0) c.pts.Add(X[k]);
                    c.pts.AddRange(best2.pts);
                    Dedupe(c.pts);
                    // the link's own border may carry a corner of its map line (80's U-turn crossings: 11 degrees): the
                    // stretch walked along it is relaxed (ends held) until no joint breaks rule 4, at most 40 rounds
                    int i0 = 0, i1 = c.pts.Count - 1;   // the whole connector, its two ends held
                    for (int it = 0; it < 40; it++)
                    {
                        bool bad = false;
                        for (int i = i0 + 1; i < i1; i++) if (Kink(c.pts[i - 1], c.pts[i], c.pts[i + 1])) { bad = true; break; }
                        if (!bad) break;
                        var cp = new List<Vector2>(c.pts);
                        for (int i = i0 + 1; i < i1; i++) c.pts[i] = 0.5f * cp[i] + 0.25f * (cp[i - 1] + cp[i + 1]);
                    }
                    return true;
                }
            return false;
        }

        /// <summary>(round 2) A corner paved up to a measured line: a smooth curve through its points (the 1 cm / 6
        /// degree rule), its two ends on the borders (within a metre: moved onto them).</summary>
        static bool PavedCorner(Arm A, Arm B, Paved pv, Corner c)
        {
            var P = pv.pts; int n = P.Count; if (n < 2) return false;
            var X = new List<Vector2>(); var seg = new List<Vector2>();
            for (int i = 0; i + 1 < n; i++)
            {
                Vector2 m0 = i == 0 ? P[1] - P[0] : 0.5f * (P[i + 1] - P[i - 1]), m1 = i == n - 2 ? P[n - 1] - P[n - 2] : 0.5f * (P[i + 2] - P[i]);
                // a Catmull-Rom tangent is per unit of parameter over TWO spans: scale to this span
                float span = (P[i + 1] - P[i]).magnitude;
                if (i > 0) m0 *= 2f * span / Mathf.Max(1e-3f, (P[i + 1] - P[i - 1]).magnitude);
                if (i < n - 2) m1 *= 2f * span / Mathf.Max(1e-3f, (P[i + 2] - P[i]).magnitude);
                Hermite(P[i], m0, P[i + 1], m1, seg);
                for (int q = i == 0 ? 0 : 1; q < seg.Count; q++) X.Add(seg[q]);
            }
            Project(A.L, X[0], out var fa, out float sa); Project(B.R, X[X.Count - 1], out var fb, out float sb);
            if ((fa - X[0]).magnitude > 1f || (fb - X[X.Count - 1]).magnitude > 1f) return false;
            X[0] = fa; X[X.Count - 1] = fb;
            c.shape = "arc"; c.note = "paved to a measured line"; c.r = 0f;
            c.tA = fa; c.sA = sa; c.tB = fb; c.sB = sb;
            c.pts.Clear(); c.pts.AddRange(X); Dedupe(c.pts);
            return true;
        }

        public const float IslandRowM = 2f;   // a row stands this far beyond an island's end
        /// <summary>(road-surface stage 1) A measured median end: the cap line through the two measured points of the nose's
        /// front, carried on to both borders; a fillet from A's left border onto it and one off it onto B's right border,
        /// of the measured radius or the largest under it that keeps the two in order (half the width: a full round).</summary>
        static bool MeasuredNose(Arm A, Arm B, Nose ns, Corner c)
        {
            var d = ns.capB - ns.capA; float len = d.magnitude; if (len < 0.05f) return false; d /= len;
            const float Ext = 12f;
            var X = new List<Vector2>();
            for (float s = -Ext; s <= len + Ext + 1e-3f; s += Step) X.Add(ns.capA + d * s);
            // from A's side to B's side
            Project(A.L, X[0], out var fa0, out _); Project(A.L, X[X.Count - 1], out var fa1, out _);
            if ((fa0 - X[0]).sqrMagnitude > (fa1 - X[X.Count - 1]).sqrMagnitude) X.Reverse();
            var Xr = new List<Vector2>(X); Xr.Reverse();
            float lo = 0.2f, hi = Mathf.Max(0.3f, ns.r); Corner best1 = null, best2 = null;
            for (int it = 0; it < 20; it++)
            {
                float r = it == 0 ? hi : 0.5f * (lo + hi);
                var f1 = new Corner(); var f2 = new Corner();
                bool ok = Fillet(A.L, X, r, f1) && Fillet(Xr, B.R, r, f2);
                float xl = (X.Count - 1) * Step;
                if (ok && f1.sB <= xl - f2.sA + 1e-3f) { best1 = f1; best2 = f2; if (it == 0) break; lo = r; } else hi = r;
            }
            if (best1 == null) return false;
            c.shape = "connector"; c.note = "measured nose"; c.kind = ns.kind; c.r = best1.r;
            c.tA = best1.tA; c.sA = best1.sA; c.tB = best2.tB; c.sB = best2.sB;
            c.pts.Clear(); c.pts.AddRange(best1.pts); c.pts.AddRange(best2.pts);
            Dedupe(c.pts);
            return true;
        }

        /// <summary>A closed polygon with each vertex rounded by a tangent arc of its radius (the tangent lengths of two
        /// arcs sharing an edge are scaled to fit it), sampled to the 1 cm / 6 degree rule; anticlockwise.</summary>
        public static List<Vector2> RoundedPolygon(List<Vector2> pts, List<float> radii)
        {
            var p = new List<Vector2>(pts); int n = p.Count;
            var rr = new List<float>(); for (int i = 0; i < n; i++) rr.Add(radii != null && i < radii.Count ? radii[i] : 0.5f);
            if (n < 3) return p;
            if (Area(p) < 0f) { p.Reverse(); rr.Reverse(); }
            var tl = new float[n]; var half = new float[n];
            for (int i = 0; i < n; i++)
            {
                var u = (p[(i - 1 + n) % n] - p[i]).normalized; var v = (p[(i + 1) % n] - p[i]).normalized;
                float th = Vector2.Angle(u, v) * Mathf.Deg2Rad;   // the interior angle between the two edges
                half[i] = th * 0.5f;
                tl[i] = th > 3.1f || th < 0.02f ? 0f : Mathf.Max(0.05f, rr[i]) / Mathf.Tan(th * 0.5f);
            }
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n; float el = (p[j] - p[i]).magnitude, sum = tl[i] + tl[j];
                    if (sum > el * 0.98f && sum > 1e-4f) { float k = el * 0.98f / sum; tl[i] *= k; tl[j] *= k; }
                }
            var o = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                if (tl[i] < 1e-3f) { o.Add(p[i]); continue; }
                var u = (p[(i - 1 + n) % n] - p[i]).normalized; var v = (p[(i + 1) % n] - p[i]).normalized;
                var t0 = p[i] + u * tl[i]; var t1 = p[i] + v * tl[i];
                float r = tl[i] * Mathf.Tan(half[i]);
                var bis = (u + v).normalized; var cc = p[i] + bis * (r / Mathf.Sin(half[i]));
                // anticlockwise ring: a convex vertex turns left (the arc anticlockwise), a reflex one right
                bool convex = u.x * v.y - u.y * v.x < 0f;
                ArcThrough(cc, t0, t1, r, o, convex);
            }
            Dedupe(o);
            return o;
        }

        static void Hermite(Vector2 p0, Vector2 m0, Vector2 p1, Vector2 m1, List<Vector2> into)
        {
            // sampled until no joint breaks the 1 cm / 6 degree rule (by halving)
            int n = 8;
            for (int guard = 0; guard < 9; guard++, n *= 2)
            {
                into.Clear();
                for (int q = 0; q <= n; q++)
                {
                    float t = q / (float)n, t2 = t * t, t3 = t2 * t;
                    into.Add((2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1);
                }
                bool ok = true;
                for (int i = 1; i + 1 < into.Count && ok; i++) ok = !Kink(into[i - 1], into[i], into[i + 1]);
                if (ok) return;
            }
        }

        /// <summary>Rule 4 (the kink probe's test): over 1 cm off the curve through the joint, or over 6 degrees from r 6 m.</summary>
        public static bool Kink(Vector2 a, Vector2 b, Vector2 c)
        {
            var u = b - a; var v = c - b;
            float lu = u.magnitude, lv = v.magnitude;
            if (lu < 0.05f || lv < 0.05f) return false;
            float th = Vector2.Angle(u, v) * Mathf.Deg2Rad;
            if (th < 1e-4f) return false;
            float L = Mathf.Min(lu, lv), sag = L * th / 8f, r = L / th;
            return sag > SagM * 1.02f || (r >= 6f && th * Mathf.Rad2Deg > MaxTurnDeg + 0.1f);
        }

        static void ArcThrough(Vector2 c, Vector2 from, Vector2 to, float r, List<Vector2> into, bool ccw)
        {
            float a0 = Mathf.Atan2(from.y - c.y, from.x - c.x), a1 = Mathf.Atan2(to.y - c.y, to.x - c.x);
            float sweep = a1 - a0;
            if (ccw) { while (sweep < 0f) sweep += 2f * Mathf.PI; } else { while (sweep > 0f) sweep -= 2f * Mathf.PI; }
            float step = 2f * Mathf.Acos(Mathf.Clamp01(1f - SagM / Mathf.Max(r, 0.05f)));
            if (r >= 6f) step = Mathf.Min(step, MaxTurnDeg * Mathf.Deg2Rad);
            step *= 0.95f;
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) / Mathf.Max(step, 1e-3f)));
            // the feet on curved borders sit a little off the circle: the radius blends from one foot's to the other's
            float r0 = (from - c).magnitude, r1 = (to - c).magnitude;
            into.Add(from);
            for (int k = 1; k < n; k++) { float f = k / (float)n, a = a0 + sweep * f; into.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Lerp(r0, r1, f)); }
            into.Add(to);
        }

        static Vector2 Offset(List<Vector2> pl, int i, float off, bool leftSide)
        {
            var t = Tangent(pl, i);
            var n = new Vector2(-t.y, t.x);
            return pl[i] + (leftSide ? n : -n) * off;
        }

        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 x)
        {
            x = default;
            var r = b - a; var s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            var q = c - a;
            float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            x = a + r * t;
            return true;
        }

        /// <summary>The crossing of a polyline with an infinite line (point, direction) nearest to 'near'; its station.</summary>
        static bool LineCross(List<Vector2> pl, Vector2 p, Vector2 d, Vector2 near, out Vector2 x, out float station)
        {
            x = default; station = 0f; float best = float.MaxValue; bool any = false;
            var nrm = new Vector2(-d.y, d.x);
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                float s0 = Vector2.Dot(pl[i] - p, nrm), s1 = Vector2.Dot(pl[i + 1] - p, nrm);
                if ((s0 > 0f && s1 > 0f) || (s0 < 0f && s1 < 0f) || Mathf.Abs(s0 - s1) < 1e-9f) continue;
                float t = s0 / (s0 - s1);
                var q = Vector2.Lerp(pl[i], pl[i + 1], t);
                float dd = (q - near).sqrMagnitude;
                if (dd < best) { best = dd; x = q; station = (i + t) * Step; any = true; }
            }
            return any;
        }

        static bool Cross(List<Vector2> A, List<Vector2> B, out int ia, out int ib, out Vector2 x)
        {
            ia = ib = -1; x = default;
            for (int i = 0; i + 1 < A.Count; i++)
                for (int j = 0; j + 1 < B.Count; j++)
                    if (SegX(A[i], A[i + 1], B[j], B[j + 1], out x)) { ia = i; ib = j; return true; }
            return false;
        }

        /// <summary>The nearest point of a polyline to p, and its station (its fractional sample index x Step, the arm's
        /// own stations: a curve's inner border is shorter than its centre).</summary>
        static void Project(List<Vector2> pl, Vector2 p, out Vector2 foot, out float station)
        {
            foot = pl[0]; station = 0f; float bd = float.MaxValue;
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                var a = pl[i]; var ab = pl[i + 1] - a; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                var q = a + ab * t; float d = (q - p).sqrMagnitude;
                if (d < bd) { bd = d; foot = q; station = (i + t) * Step; }
            }
        }

        // ---- the fill ----
        static float Area(List<Vector2> p) { float s = 0f; for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.x * b.y - b.x * a.y; } return 0.5f * s; }
        static float Cross2(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static void Fill(Result res)
        {
            var ring = Densify(res.outline, EdgeM);
            if (ring.Count < 3) return;
            if (Area(ring) < 0f) ring.Reverse();                                   // the outer ring anticlockwise
            var holes = new List<List<Vector2>>();
            foreach (var h in res.holes) { var hh = Densify(h, EdgeM); if (Area(hh) > 0f) hh.Reverse(); holes.Add(hh); }   // holes clockwise
            res.ringOuter = new List<Vector2>(ring); res.ringHoles = new List<List<Vector2>>(); foreach (var h in holes) res.ringHoles.Add(new List<Vector2>(h));
            holes.Sort((p, q) => MaxX(q).CompareTo(MaxX(p)));                     // rightmost first
            foreach (var h in holes)
            {
                if (!Bridge(ring, h, out var merged)) { res.refused = "a hole the outer ring does not enclose"; return; }
                ring = merged;
            }
            res.ringCount = ring.Count;
            var ear = new List<int>(); EarClip(ring, ear);
            // interior points every SteinerM; closer (x 0.75 a round, to 1 m) while an interior point stands more than
            // 9 mm off its neighbours' plane (the surface bends there: a crest, a sag, a grade meeting a cross grade)
            float sp = SteinerM;
            for (int round = 0; ; round++)
            {
                res.fillVerts = new List<Vector2>(ring); res.tris = new List<int>(ear);
                res.steiner = Steiner(res, holes, sp);
                res.flips = Lawson(res.fillVerts, res.tris);
                res.fillY.Clear();
                foreach (var v in res.fillVerts) res.fillY.Add(res.HeightAt(v));
                res.planeDev = PlaneDev(res);
                if (res.planeDev <= 0.009f || sp <= 1.05f) break;
                sp = Mathf.Max(1f, sp * 0.75f);
            }
            res.steinerM = sp;
            res.slivers = 0;
            for (int t = 0; t + 2 < res.tris.Count; t += 3)
                if (MinAngle(res.fillVerts[res.tris[t]], res.fillVerts[res.tris[t + 1]], res.fillVerts[res.tris[t + 2]]) < SliverDeg) res.slivers++;
        }

        /// <summary>The interior points settled onto their neighbours' least-squares planes (Gauss-Seidel from the height
        /// function, to 0.05 mm): the ring (rows, kerbs, islands) stays on the height function.</summary>
        static void Settle(Result r)
        {
            int n = r.fillVerts.Count;
            if (n <= r.ringCount) return;
            var nb = new List<int>[n];
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int u = r.tris[t + e], w = r.tris[t + (e + 1) % 3];
                    if (u >= r.ringCount) { nb[u] ??= new List<int>(6); if (!nb[u].Contains(w)) nb[u].Add(w); }
                    if (w >= r.ringCount) { nb[w] ??= new List<int>(6); if (!nb[w].Contains(u)) nb[w].Add(u); }
                }
            // each interior point's intercept weights: h_v = sum alpha_w h_w (the plane fit is linear in the heights)
            var alpha = new List<float>[n];
            for (int v = r.ringCount; v < n; v++)
            {
                if (nb[v] == null || nb[v].Count < 3) continue;
                var M = new double[3, 3]; var c = r.fillVerts[v];
                foreach (int w in nb[v]) { var p = r.fillVerts[w] - c; double[] row = { p.x, p.y, 1.0 }; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) M[i, j] += row[i] * row[j]; }
                var al = new List<float>(nb[v].Count);
                foreach (int w in nb[v])
                {
                    var p = r.fillVerts[w] - c; var x = SolveLinear(M, new double[] { p.x, p.y, 1.0 });
                    if (x == null) { al = null; break; }
                    al.Add((float)x[2]);
                }
                alpha[v] = al;
            }
            for (int it = 0; it < 400; it++)
            {
                float worst = 0f;
                for (int v = r.ringCount; v < n; v++)
                {
                    var al = alpha[v]; if (al == null) continue;
                    float h = 0f; for (int k = 0; k < al.Count; k++) h += al[k] * r.fillY[nb[v][k]];
                    worst = Mathf.Max(worst, Mathf.Abs(h - r.fillY[v]));
                    r.fillY[v] = h;
                }
                if (worst < 5e-5f) break;
            }
        }

        /// <summary>A ring with every edge longer than m cut in equal pieces (the end rows and straight kerbs: the fill's
        /// triangles stay small enough to carry the surface's bend; the points are on the edges, the outline unchanged).</summary>
        static List<Vector2> Densify(List<Vector2> ring, float m)
        {
            var o = new List<Vector2>(ring.Count * 2);
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                o.Add(a);
                int k = Mathf.CeilToInt((b - a).magnitude / m - 1e-3f);
                for (int j = 1; j < k; j++) o.Add(Vector2.Lerp(a, b, j / (float)k));
            }
            return o;
        }

        /// <summary>The worst distance of an interior point's height from the least-squares plane of its neighbours.</summary>
        public static float PlaneDev(Result r) => PlaneDev(r, out _);
        public static float PlaneDev(Result r, out int worstV)
        {
            worstV = -1;
            int n = r.fillVerts.Count;
            var nb = new List<int>[n];
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int u = r.tris[t + e], w = r.tris[t + (e + 1) % 3];
                    if (u >= r.ringCount) { nb[u] ??= new List<int>(6); if (!nb[u].Contains(w)) nb[u].Add(w); }
                    if (w >= r.ringCount) { nb[w] ??= new List<int>(6); if (!nb[w].Contains(u)) nb[w].Add(u); }
                }
            float worst = 0f;
            for (int v = r.ringCount; v < n; v++)
            {
                if (nb[v] == null || nb[v].Count < 3) continue;
                var M = new double[3, 3]; var B = new double[3];
                var c = r.fillVerts[v];
                foreach (int w in nb[v])
                {
                    var p = r.fillVerts[w] - c; double[] row = { p.x, p.y, 1.0 }; double h = r.fillY[w];
                    for (int i = 0; i < 3; i++) { B[i] += row[i] * h; for (int j = 0; j < 3; j++) M[i, j] += row[i] * row[j]; }
                }
                var x = SolveLinear(M, B); if (x == null) continue;
                float dev = Mathf.Abs(r.fillY[v] - (float)x[2]);
                if (dev > worst) { worst = dev; worstV = v; }
            }
            return worst;
        }

        /// <summary>The standard bridge (Eberly): from the hole's rightmost vertex M a ray to +x; the nearest ring edge it
        /// hits at I; P that edge's endpoint with the larger x; a reflex ring vertex inside M-I-P replaces P (the one at
        /// the smallest angle to +x, nearest on ties). The ring becomes ... P, M, the hole round to M, P, ...</summary>
        static bool Bridge(List<Vector2> ring, List<Vector2> h, out List<Vector2> merged)
        {
            merged = null;
            int mi = 0; for (int i = 1; i < h.Count; i++) if (h[i].x > h[mi].x || (h[i].x == h[mi].x && h[i].y > h[mi].y)) mi = i;
            var M = h[mi];
            int pe = -1; float bestX = float.MaxValue; Vector2 I = default;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % n];
                // the anticlockwise ring's edges the ray can enter: running downward (a above, b at or below) - the ring's
                // inside is on their left, so the ray from inside meets them from the left
                if (!(a.y >= M.y && b.y <= M.y) && !(a.y <= M.y && b.y >= M.y)) continue;
                if (Mathf.Abs(b.y - a.y) < 1e-9f) { if (Mathf.Abs(a.y - M.y) > 1e-9f) continue; float xe = Mathf.Min(a.x, b.x); if (xe >= M.x && xe < bestX) { bestX = xe; I = new Vector2(xe, M.y); pe = i; } continue; }
                float t = (M.y - a.y) / (b.y - a.y);
                float x = a.x + t * (b.x - a.x);
                if (x < M.x || x >= bestX) continue;
                bestX = x; I = new Vector2(x, M.y); pe = i;
            }
            if (pe < 0) return false;
            int P;
            var e0 = ring[pe]; var e1 = ring[(pe + 1) % n];
            if ((I - e0).sqrMagnitude < 1e-12f) P = pe;
            else if ((I - e1).sqrMagnitude < 1e-12f) P = (pe + 1) % n;
            else
            {
                P = e0.x > e1.x ? pe : (pe + 1) % n;
                var Pp = ring[P];
                float bestAng = float.MaxValue, bestD = float.MaxValue; int pick = -1;
                for (int i = 0; i < n; i++)
                {
                    var cur = ring[i];
                    if (i == P) continue;
                    if (Cross2(ring[(i - 1 + n) % n], cur, ring[(i + 1) % n]) > 0f) continue;   // convex: not a candidate
                    if (!InTriClosed(M, I, Pp, cur)) continue;
                    float ang = Mathf.Abs(Mathf.Atan2(cur.y - M.y, cur.x - M.x)), d = (cur - M).sqrMagnitude;
                    if (ang < bestAng - 1e-7f || (Mathf.Abs(ang - bestAng) <= 1e-7f && d < bestD)) { bestAng = ang; bestD = d; pick = i; }
                }
                if (pick >= 0)
                {
                    // the reflex vertex is visible from M only if nothing nearer than Pp's angle hides it; compare with P too
                    float angP = Mathf.Abs(Mathf.Atan2(Pp.y - M.y, Pp.x - M.x));
                    if (bestAng <= angP + 1e-7f) P = pick;
                }
            }
            merged = new List<Vector2>(n + h.Count + 2);
            for (int i = 0; i <= P; i++) merged.Add(ring[i]);
            for (int k = 0; k <= h.Count; k++) merged.Add(h[(mi + k) % h.Count]);   // M, the hole (clockwise), M again
            merged.Add(ring[P]);
            for (int i = P + 1; i < n; i++) merged.Add(ring[i]);
            return true;
        }

        static float MaxX(List<Vector2> h) { float m = float.MinValue; foreach (var p in h) m = Mathf.Max(m, p.x); return m; }

        static float MinAngle(Vector2 a, Vector2 b, Vector2 c)
        {
            float A = Vector2.Angle(b - a, c - a), B = Vector2.Angle(a - b, c - b);
            return Mathf.Min(A, Mathf.Min(B, 180f - A - B));
        }

        /// <summary>Ear clipping of an anticlockwise ring (a bridged hole's two copies of P and of M are distinct entries). A
        /// point ON an ear's edge is not inside; a zero-area ear made by the bridge's copies is clipped away, no triangle.</summary>
        static void EarClip(List<Vector2> poly, List<int> into)
        {
            var idx = new List<int>(poly.Count); for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < poly.Count * 8)
            {
                bool cut = false;
                // degenerate first: a spike (a == c) or a repeated point, clipped with no triangle
                for (int i = 0; i < idx.Count && !cut; i++)
                {
                    Vector2 a = poly[idx[(i - 1 + idx.Count) % idx.Count]], b = poly[idx[i]], c = poly[idx[(i + 1) % idx.Count]];
                    if ((a - c).sqrMagnitude < 1e-12f || (a - b).sqrMagnitude < 1e-12f) { idx.RemoveAt(i); cut = true; }
                }
                if (cut) continue;
                for (int i = 0; i < idx.Count && !cut; i++)
                {
                    int ia = idx[(i - 1 + idx.Count) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (Cross2(a, b, c) <= 1e-7f) continue;                          // reflex or straight
                    bool inside = false;
                    for (int j = 0; j < idx.Count && !inside; j++)
                    {
                        int ij = idx[j];
                        if (ij == ia || ij == ib || ij == ic) continue;
                        var p = poly[ij];
                        if (InTriStrict(a, b, c, p)) { inside = true; break; }
                        // a ring point ON the new diagonal a-c (not a copy of a or c) would be left as a T: not an ear
                        if ((p - a).sqrMagnitude > 1e-12f && (p - c).sqrMagnitude > 1e-12f && OnSeg(a, c, p)) { inside = true; break; }
                        // a copy of an ear corner (the bridge): the ear may not swallow the edges that leave it
                        if ((p - a).sqrMagnitude < 1e-12f || (p - b).sqrMagnitude < 1e-12f || (p - c).sqrMagnitude < 1e-12f)
                        {
                            var pp = poly[idx[(j - 1 + idx.Count) % idx.Count]]; var pn = poly[idx[(j + 1) % idx.Count]];
                            if (Enters(a, b, c, p, pp) || Enters(a, b, c, p, pn)) inside = true;
                        }
                    }
                    if (inside) continue;
                    into.Add(ia); into.Add(ib); into.Add(ic);
                    idx.RemoveAt(i); cut = true;
                }
                if (!cut)
                {
                    // no ear: drop a straight vertex (zero area) to go on
                    for (int i = 0; i < idx.Count && !cut; i++)
                    {
                        Vector2 a = poly[idx[(i - 1 + idx.Count) % idx.Count]], b = poly[idx[i]], c = poly[idx[(i + 1) % idx.Count]];
                        if (Mathf.Abs(Cross2(a, b, c)) <= 1e-6f) { idx.RemoveAt(i); cut = true; }
                    }
                    if (!cut) break;   // what is left is straight (zero area)
                }
            }
            if (idx.Count == 3 && Cross2(poly[idx[0]], poly[idx[1]], poly[idx[2]]) > 1e-7f) { into.Add(idx[0]); into.Add(idx[1]); into.Add(idx[2]); }
        }

        /// <summary>Does the segment from p (a copy of one of the ear's corners) toward q start into the ear's inside? An
        /// angular test at that corner: strictly between the ear's two edges there (along an edge is not inside).</summary>
        static bool Enters(Vector2 a, Vector2 b, Vector2 c, Vector2 p, Vector2 q)
        {
            var u = q - p; float l = u.magnitude; if (l < 1e-6f) return false; u /= l;
            Vector2 x, e1, e2;   // the corner, then its edges in the ear's anticlockwise order
            if ((p - a).sqrMagnitude < 1e-12f) { x = a; e1 = b; e2 = c; }
            else if ((p - b).sqrMagnitude < 1e-12f) { x = b; e1 = c; e2 = a; }
            else { x = c; e1 = a; e2 = b; }
            var d1 = (e1 - x).normalized; var d2 = (e2 - x).normalized;
            return d1.x * u.y - d1.y * u.x > 1e-6f && u.x * d2.y - u.y * d2.x > 1e-6f;
        }

        static bool OnSeg(Vector2 a, Vector2 c, Vector2 p)
        {
            var ac = c - a; float l2 = ac.sqrMagnitude; if (l2 < 1e-12f) return false;
            float t = Vector2.Dot(p - a, ac) / l2; if (t <= 1e-6f || t >= 1f - 1e-6f) return false;
            return Mathf.Abs(Cross2(a, c, p)) / Mathf.Sqrt(l2) < 1e-5f;
        }

        /// <summary>Strictly inside (a point on an edge, within 10 microns, is not inside).</summary>
        static bool InTriStrict(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float o = Cross2(a, b, c); if (Mathf.Abs(o) < 1e-12f) return false;
            float s = o > 0f ? 1f : -1f;
            float lab = (b - a).magnitude, lbc = (c - b).magnitude, lca = (a - c).magnitude;
            if (lab < 1e-9f || lbc < 1e-9f || lca < 1e-9f) return false;
            const float e = 1e-5f;
            return s * Cross2(a, b, p) / lab > e && s * Cross2(b, c, p) / lbc > e && s * Cross2(c, a, p) / lca > e;
        }
        static bool InTriClosed(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = Cross2(a, b, p), d2 = Cross2(b, c, p), d3 = Cross2(c, a, p);
            const float e = 1e-7f;
            return (d1 >= -e && d2 >= -e && d3 >= -e) || (d1 <= e && d2 <= e && d3 <= e);
        }

        /// <summary>Interior points every SteinerM (at least 0.6 x that from the outline and the holes), each into the
        /// triangle that holds it (split in three); the flips after make them Delaunay.</summary>
        static int Steiner(Result res, List<List<Vector2>> holes, float SteinerM)
        {
            var v = res.fillVerts; var tris = res.tris;
            var o = res.outline;
            var min = o[0]; var max = o[0];
            foreach (var p in o) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            float clear = 0.6f * SteinerM, clear2 = clear * clear;
            int added = 0;
            for (float x = min.x + SteinerM * 0.5f; x < max.x; x += SteinerM)
                for (float y = min.y + SteinerM * 0.5f; y < max.y; y += SteinerM)
                {
                    var p = new Vector2(x, y);
                    if (!InPoly(o, p)) continue;
                    bool inHole = false; foreach (var h in holes) if (InPoly(h, p)) { inHole = true; break; }
                    if (inHole) continue;
                    if (SegDist2(o, p) < clear2) continue;
                    bool near = false; foreach (var h in holes) if (SegDist2(h, p) < clear2) { near = true; break; }
                    if (near) continue;
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        if (!InTriStrict(v[a], v[b], v[c], p)) continue;
                        // keep each piece standing (a point too near an edge is skipped)
                        if (Cross2(v[a], v[b], p) < 1e-4f || Cross2(v[b], v[c], p) < 1e-4f || Cross2(v[c], v[a], p) < 1e-4f) break;
                        int vi = v.Count; v.Add(p);
                        tris[t + 2] = vi;
                        tris.Add(b); tris.Add(c); tris.Add(vi);
                        tris.Add(c); tris.Add(a); tris.Add(vi);
                        added++;
                        break;
                    }
                }
            return added;
        }
        static float SegDist2(List<Vector2> ring, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var ab = ring[(i + 1) % ring.Count] - a; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }
            return best;
        }

        /// <summary>Lawson flips (RefineFan's): every interior edge (one with a triangle on both sides: ring and bridge edges
        /// have none, their copies are distinct entries) whose opposite vertex lies inside the other triangle's
        /// circumcircle is flipped, while both new triangles stand, until none is. Returns how many.</summary>
        static int Lawson(List<Vector2> v, List<int> tris)
        {
            var edge = new Dictionary<long, int>(tris.Count * 2);
            long Key(int a, int b) => ((long)a << 32) | (uint)b;
            for (int t = 0; t + 2 < tris.Count; t += 3)
                for (int r = 0; r < 3; r++) edge[Key(tris[t + r], tris[t + (r + 1) % 3])] = t;
            int flips = 0, cap = 40 * tris.Count + 100;
            bool any = true;
            while (any && flips < cap)
            {
                any = false;
                for (int t = 0; t + 2 < tris.Count && flips < cap; t += 3)
                    for (int r = 0; r < 3; r++)
                    {
                        int u = tris[t + r], w = tris[t + (r + 1) % 3], w1 = tris[t + (r + 2) % 3];
                        if (!edge.TryGetValue(Key(w, u), out int t2) || t2 == t) continue;
                        int w2 = -1;
                        for (int r2 = 0; r2 < 3; r2++) if (tris[t2 + r2] != u && tris[t2 + r2] != w) w2 = tris[t2 + r2];
                        if (w2 < 0 || w2 == w1) continue;
                        Vector2 pu = v[u], pw = v[w], p1 = v[w1], p2 = v[w2];
                        if (InCircle(pu, pw, p1, p2) <= 1e-9 * Scale4(pu, pw, p1, p2)) continue;
                        if (Cross2(pu, p2, p1) <= 1e-6f || Cross2(p2, pw, p1) <= 1e-6f) continue;
                        edge.Remove(Key(u, w)); edge.Remove(Key(w, u));
                        tris[t] = u; tris[t + 1] = w2; tris[t + 2] = w1;
                        tris[t2] = w2; tris[t2 + 1] = w; tris[t2 + 2] = w1;
                        edge[Key(u, w2)] = t; edge[Key(w2, w1)] = t; edge[Key(w1, u)] = t;
                        edge[Key(w2, w)] = t2; edge[Key(w, w1)] = t2; edge[Key(w1, w2)] = t2;
                        flips++; any = true;
                        break;
                    }
            }
            return flips;
        }
        static double Scale4(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double s = Mathf.Max(Mathf.Max((a - d).sqrMagnitude, (b - d).sqrMagnitude), (c - d).sqrMagnitude);
            return s * s;
        }
        /// <summary>Positive where d lies inside the circle through the anticlockwise triangle a, b, c.</summary>
        static double InCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double adx = a.x - d.x, ady = a.y - d.y, bdx = b.x - d.x, bdy = b.y - d.y, cdx = c.x - d.x, cdy = c.y - d.y;
            return (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                 - (bdx * bdx + bdy * bdy) * (adx * cdy - cdx * ady)
                 + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
        }

        /// <summary>BAYS INTO THEIR HOSTS (round 3's R0, in the outline): an arm X that, from within BayStartM of its
        /// start, runs on beside another arm H (its border within BayGapM of H's facing border) for BayRunM or to its end is a
        /// lane of H: H's border on that side becomes X's outer border (carried straight back from where X comes alongside),
        /// and X's part before that (a U-turn lane's crossing of the median, a bay's own opening) becomes a LINK of the
        /// cluster. Returns a line per merge for the log.</summary>
        public const float BayGapM = 1.2f, BayRunM = 30f, BayStartM = 45f;
        public static List<string> MergeBays(List<Arm> arms, List<Link> links)
        {
            var log = new List<string>();
            for (int guard = 0; guard < 8; guard++)
            {
                bool merged = false;
                for (int xi = 0; xi < arms.Count && !merged; xi++)
                    for (int hi = 0; hi < arms.Count && !merged; hi++)
                    {
                        if (xi == hi) continue;
                        var X = arms[xi]; var H = arms[hi];
                        // the lane is the narrower (a host is not a lane of its bay)
                        int km = Mathf.Min(Mathf.Min(X.C.Count, H.C.Count) - 1, Mathf.RoundToInt(BayStartM / Step));
                        if ((X.L[km] - X.R[km]).magnitude >= (H.L[km] - H.R[km]).magnitude - 0.1f) continue;
                        // X on H's right (X.L along H.R) or on H's left (X.R along H.L)
                        for (int side = 0; side < 2 && !merged; side++)
                        {
                            var xf = side == 0 ? X.L : X.R;   // X's border facing H
                            var hf = side == 0 ? H.R : H.L;   // H's border facing X
                            int run = Mathf.RoundToInt(BayRunM / Step), s0 = -1;
                            for (int k = 0; k * Step <= BayStartM && k < xf.Count; k++)
                            {
                                int ok = 0;
                                for (int q = k; q < xf.Count && q < k + run; q++)
                                {
                                    Project(hf, xf[q], out var f, out float st);
                                    if (st <= 0f || st >= (hf.Count - 1) * Step) break;
                                    // X's facing border within BayGapM outside H's facing border, or inside H's ribbon (a bay drawn
                                    // overlapping its host, as OSM draws 80's U-turn lanes), and X's centre on that side of H's centre
                                    var hc = H.C[Mathf.Clamp(Mathf.RoundToInt(st / Step), 0, H.C.Count - 1)];
                                    var hb = hf[Mathf.Clamp(Mathf.RoundToInt(st / Step), 0, hf.Count - 1)];
                                    // (road-surface stage 1) a lane runs the SAME WAY as its host (a measured host is wide at its
                                    // node: the opposite carriageway's first metres lay "inside its ribbon" and merged as a bay)
                                    if (Vector2.Dot(Tangent(X.C, q), Tangent(H.C, Mathf.Clamp(Mathf.RoundToInt(st / Step), 0, H.C.Count - 1))) < 0.5f) break;
                                    var outw = (hb - hc).normalized; float halfH = (hb - hc).magnitude;
                                    float gap = Vector2.Dot(xf[q] - hb, outw);
                                    if (gap > BayGapM || gap < -halfH || Vector2.Dot(X.C[q] - hc, outw) <= 0.5f) break;
                                    ok++;
                                }
                                if (ok >= Mathf.Min(run, xf.Count - k - 1) && ok >= 8) { s0 = k; break; }
                            }
                            if (s0 < 0) continue;
                            // the host's border on that side: X's outer border from where X comes alongside, carried straight back
                            var xo = side == 0 ? X.R : X.L;
                            Project(hf, xf[s0], out _, out float h0);
                            int k0 = Mathf.CeilToInt(h0 / Step - 1e-4f);
                            var t0 = Tangent(xo, s0);
                            // the outer border as one polyline: carried straight back from s0, then X's, then carried on
                            var ob = new List<Vector2>();
                            for (int k = Mathf.RoundToInt(BackM / Step); k >= 1; k--) ob.Add(xo[s0] - t0 * (k * Step));
                            for (int q = s0; q < xo.Count; q++) ob.Add(xo[q]);
                            var te = Tangent(xo, xo.Count - 1);
                            for (int k = 1; k <= Mathf.RoundToInt(BackM / Step); k++) ob.Add(xo[xo.Count - 1] + te * (k * Step));
                            // each host station's point on it, square across the host (its rows stay square); before X comes
                            // alongside, the host's own border moved out by X's width there (parallel to the host)
                            var nb = new List<Vector2>(hf.Count);
                            k0 = Mathf.Clamp(k0, 0, hf.Count - 1);
                            for (int k = 0; k < hf.Count; k++)
                            {
                                var tc = Tangent(H.C, k); var nrm = new Vector2(-tc.y, tc.x);
                                if (k >= k0 && LineCross(ob, H.C[k], nrm, hf[k], out var q, out _) && Vector2.Dot(q - hf[k], hf[k] - H.C[k]) > 0f) nb.Add(q);
                                else nb.Add(hf[k]);
                            }
                            var w0 = nb[k0] - hf[k0];
                            for (int k = 0; k < k0; k++) nb[k] = hf[k] + w0;
                            // (road-surface stage 1: measures are applied AFTER the bays are merged, so a measure blends back to
                            // the host's border WITH its bay - the pavement the ribbons draw beyond the row)
                            if (side == 0) H.R = nb; else H.L = nb;
                            // X's crossing before it comes alongside: a link (when it has any length)
                            if (s0 >= 2)
                            {
                                var lk = new Link { id = X.id };
                                for (int k = 0; k <= s0; k++) { lk.L.Add(X.L[k]); lk.R.Add(X.R[k]); }
                                links.Add(lk);
                            }
                            log.Add($"arm {X.id} is a lane of arm {H.id} on its {(side == 0 ? "right" : "left")} from {s0 * Step:0.0} m (host {h0:0.0} m){(s0 >= 2 ? "; its first " + (s0 * Step).ToString("0.0") + " m a link" : "")}");
                            arms.RemoveAt(xi);
                            merged = true;
                        }
                    }
                if (!merged) break;
            }
            return log;
        }

        /// <summary>The fill alone, on a given outline and holes (the fill's own tests).</summary>
        public static Result FillOnly(List<Vector2> outline, List<List<Vector2>> holes)
        {
            var res = new Result { arms = new List<Arm>() };
            res.outline.AddRange(outline); Dedupe(res.outline);
            if (holes != null) foreach (var h in holes) { var hh = new List<Vector2>(h); Dedupe(hh); if (hh.Count >= 3) res.holes.Add(hh); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Fill(res);
            res.fillMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ---- helpers for callers and tests ----
        /// <summary>A straight synthetic arm leaving origin along dir: half widths left / right, length, a grade (rise per
        /// metre out) from height y0.</summary>
        public static Arm StraightArm(int id, Vector2 origin, Vector2 dir, float halfL, float halfR, float length = 80f, float radius = 9f, bool oneway = false, string name = "", float grade = 0f, float y0 = 0f)
        {
            dir = dir.normalized; var left = new Vector2(-dir.y, dir.x);
            var a = new Arm { id = id, radius = radius, oneway = oneway, name = name, Y = new List<float>() };
            for (float d = 0f; d <= length + 1e-3f; d += Step) { var p = origin + dir * d; a.C.Add(p); a.L.Add(p + left * halfL); a.R.Add(p - left * halfR); a.Y.Add(y0 + grade * d); }
            return a;
        }
        /// <summary>A curved synthetic arm: a circular arc of radius rc (sign: + bends left) from origin, heading dir.</summary>
        public static Arm CurvedArm(int id, Vector2 origin, Vector2 dir, float rc, float halfL, float halfR, float length = 80f, float radius = 9f, bool oneway = false, string name = "", float grade = 0f, float y0 = 0f)
        {
            dir = dir.normalized;
            var a = new Arm { id = id, radius = radius, oneway = oneway, name = name, Y = new List<float>() };
            var p = origin; var t = dir;
            for (float d = 0f; d <= length + 1e-3f; d += Step)
            {
                var left = new Vector2(-t.y, t.x);
                a.C.Add(p); a.L.Add(p + left * halfL); a.R.Add(p - left * halfR); a.Y.Add(y0 + grade * d);
                float turn = Step / rc;
                float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
                t = new Vector2(t.x * c - t.y * s, t.x * s + t.y * c).normalized;
                p += t * Step;
            }
            return a;
        }
        /// <summary>An arm along a centre polyline (resampled every Step), half widths left / right.</summary>
        public static Arm ArmAlong(int id, List<Vector2> centre, float halfL, float halfR, float radius = 9f, bool oneway = false, string name = "")
        {
            var a = new Arm { id = id, radius = radius, oneway = oneway, name = name, Y = new List<float>() };
            var c = Resample(centre, Step);
            for (int k = 0; k < c.Count; k++)
            {
                var t = (c[Mathf.Min(k + 1, c.Count - 1)] - c[Mathf.Max(k - 1, 0)]).normalized; var left = new Vector2(-t.y, t.x);
                a.C.Add(c[k]); a.L.Add(c[k] + left * halfL); a.R.Add(c[k] - left * halfR); a.Y.Add(0f);
            }
            return a;
        }
        public static List<Vector2> Resample(List<Vector2> pl, float step)
        {
            var o = new List<Vector2> { pl[0] }; float carry = 0f;
            for (int i = 1; i < pl.Count; i++)
            {
                var a = pl[i - 1]; var b = pl[i]; float l = (b - a).magnitude; float s = step - carry;
                while (s <= l + 1e-6f) { o.Add(Vector2.Lerp(a, b, s / l)); s += step; }
                carry = l - (s - step);
            }
            return o;
        }
        /// <summary>A cubic from p0 (tangent t0) to p1 (tangent t1), then straight on to length, sampled finely.</summary>
        public static List<Vector2> CurveThenStraight(Vector2 p0, Vector2 t0, Vector2 p1, Vector2 t1, float length)
        {
            var o = new List<Vector2>(); float k = (p1 - p0).magnitude;
            for (int q = 0; q <= 200; q++)
            {
                float t = q / 200f, t2 = t * t, t3 = t2 * t;
                o.Add((2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * (t0.normalized * k) + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * (t1.normalized * k));
            }
            o.Add(p1 + t1.normalized * length);
            return o;
        }
        /// <summary>A straight link (a crossover) from a to b, half width each side, sampled every Step.</summary>
        public static Link StraightLink(int id, Vector2 a, Vector2 b, float half)
        {
            var lk = new Link { id = id };
            var d = (b - a); float len = d.magnitude; d /= len; var left = new Vector2(-d.y, d.x);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
            for (int k = 0; k <= n; k++) { var p = a + d * (len * k / n); lk.L.Add(p + left * half); lk.R.Add(p - left * half); }
            return lk;
        }
        /// <summary>(road-surface stage 1) How far a point stands from the pavement's edge (the outline or an island):
        /// positive on the pavement, negative on land (off the outline or on an island). The LANE CHECK: a map line that
        /// traffic and paint follow must keep half a lane of it; a measured kerb does not move the map's line.</summary>
        public static float Clearance(Result r, Vector2 p)
        {
            float d2 = SegDist2(r.outline, p);
            foreach (var h in r.holes) d2 = Mathf.Min(d2, SegDist2(h, p));
            float d = Mathf.Sqrt(d2);
            return Covers(r, p) ? d : -d;
        }

        /// <summary>(road-surface stage 1) THE LANE CHECK of one ribbon as the RULE draws it (its two borders from the line
        /// model: the lanes, the paint and the traffic follow them) against the solved pavement, from the junction out to
        /// 'upTo' metres: how many metres of it have a point 0.6 m inside either border, or its middle, off the pavement
        /// (on land or on an island), and the worst depth. A measured kerb does not move the line model's lanes.</summary>
        public static void LaneCheck(Result r, List<Vector2> L, List<Vector2> R, float upTo, out float runM, out float worst, out Vector2 at)
        {
            runM = 0f; worst = float.MaxValue; at = Vector2.zero;
            for (int k = 0; k < L.Count && k < R.Count && k * Step <= upTo; k++)
            {
                var d = L[k] - R[k]; float w = d.magnitude; if (w < 0.5f) continue; d /= w;
                float inset = Mathf.Min(0.6f, 0.4f * w); bool off = false;
                for (int q = 0; q < 3; q++)
                {
                    var p = q == 0 ? R[k] + d * inset : q == 1 ? R[k] + d * (0.5f * w) : L[k] - d * inset;
                    float cl = Clearance(r, p);
                    if (cl < worst) { worst = cl; at = p; }
                    off |= cl < 0f;
                }
                if (off) runM += Step;
            }
        }

        /// <summary>Is a point inside the outline and outside every hole?</summary>
        public static bool Covers(Result r, Vector2 p)
        {
            if (!InPoly(r.outline, p)) return false;
            foreach (var h in r.holes) if (InPoly(h, p)) return false;
            return true;
        }
        public static bool InPoly(List<Vector2> poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }
        /// <summary>How many fill triangles cover a point strictly (the 'covered exactly once' check).</summary>
        public static int CoverCount(Result r, Vector2 p)
        {
            int n = 0;
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
                if (InTriStrict(r.fillVerts[r.tris[t]], r.fillVerts[r.tris[t + 1]], r.fillVerts[r.tris[t + 2]], p)) n++;
            return n;
        }
        /// <summary>Is a point on an edge of the fill (within 0.1 mm of a triangle, not strictly inside one)?</summary>
        public static bool OnFillEdge(Result r, Vector2 p)
        {
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
                if (InTriClosedM(r.fillVerts[r.tris[t]], r.fillVerts[r.tris[t + 1]], r.fillVerts[r.tris[t + 2]], p, 1e-4f)) return true;
            return false;
        }
        static bool InTriClosedM(Vector2 a, Vector2 b, Vector2 c, Vector2 p, float e)
        {
            float o = Cross2(a, b, c); if (Mathf.Abs(o) < 1e-12f) return false;
            float s = o > 0f ? 1f : -1f;
            float lab = (b - a).magnitude, lbc = (c - b).magnitude, lca = (a - c).magnitude;
            return s * Cross2(a, b, p) / lab > -e && s * Cross2(b, c, p) / lbc > -e && s * Cross2(c, a, p) / lca > -e;
        }
        /// <summary>The fill's own height at a point (barycentric in the triangle that holds it); NaN outside.</summary>
        public static float MeshHeight(Result r, Vector2 p)
        {
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
            {
                int ia = r.tris[t], ib = r.tris[t + 1], ic = r.tris[t + 2];
                Vector2 a = r.fillVerts[ia], b = r.fillVerts[ib], c = r.fillVerts[ic];
                float area = Cross2(a, b, c); if (Mathf.Abs(area) < 1e-9f) continue;
                float w0 = Cross2(b, c, p) / area, w1 = Cross2(c, a, p) / area, w2 = 1f - w0 - w1;
                if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                return w0 * r.fillY[ia] + w1 * r.fillY[ib] + w2 * r.fillY[ic];
            }
            return float.NaN;
        }
    }
}
