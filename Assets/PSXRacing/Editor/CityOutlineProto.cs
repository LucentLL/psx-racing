using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// c7roads round 5, stage 0 h: A PROTOTYPE OF THE JUNCTION OUTLINE SOLVER (edit mode, no mesh change). For each row of
    /// the spot table that names map nodes, the cluster of those nodes is one junction:
    ///   1. ARMS: every edge with one end in the cluster; each arm's two borders sampled every BorderStepM from the
    ///      line model's extents (LineModel.Extents: the pavement as drawn, aux lanes and bay shifts included) out to
    ///      ArmReachM, in the arm's away frame (left / right of travel away from the junction).
    ///   2. ORDER: arms sorted anticlockwise by their direction at 12 m.
    ///   3. CORNERS, between each arm's LEFT border and the next arm's RIGHT border, one of three shapes, never a point:
    ///      - THROUGH: the two arms are one road going on (over 150 degrees apart): the borders join straight;
    ///      - NOSE: the arms run side by side (under 35 degrees, or the borders never cross): walk out until the gap
    ///        between the borders reaches NoseGapM (2.4 m); both arms are cut there on one shared line, the land beyond
    ///        ends in a rounded nose of radius gap / 2, the gore from the node to the nose is paved (inside the outline);
    ///      - ARC: the StreetGen construction on the real (curved) borders: offset each border by R into the corner,
    ///        intersect the two offsets (the centre), the tangent points are the centre's feet on the borders; the arc is
    ///        sampled to 1 cm sagitta and at most 6 degrees a joint; R by class (CornerRadius), shrunk until it fits.
    ///   4. CUTS: each arm is cut at the farther of its two corners' tangent points (its END ROW: both corners'
    ///      vertices lie on it, the ribbon would share them).
    ///   5. THE OUTLINE: arm by arm anticlockwise: cut row right -> left, the left border in to the corner, the corner,
    ///      the next arm's right border out to its cut.
    ///   6. FILL (cost only): an ear clip of the outline (no holes: the hole-capable constrained triangulation is NOT
    ///      written - sized in the hand-back).
    /// Output: Screenshots/c7/c7_outline_{PSX_C7_TAG}.json (per spot: arms, corners, cuts, outline) and .txt (timings).
    /// PSX_ISL_SPOTS: the spot table (tools/city/spots/junction_spots.csv).
    /// </summary>
    public static class CityOutlineProto
    {
        public const float BorderStepM = 0.5f, ArmReachM = 80f, NoseGapM = 2.4f, ThroughDeg = 150f, SideBySideDeg = 35f, SagM = 0.01f, MaxTurnDeg = 6f;

        sealed class Arm
        {
            public int edge, node; public bool fwd; public float ang;
            public List<Vector2> L = new List<Vector2>(), R = new List<Vector2>(), C = new List<Vector2>();
            public float cut;   // distance out along the arm
            public string name;
        }
        sealed class Corner { public int a, b; public string shape; public float r, dA, dB, deg; public Vector2 tA, tB, centre; public List<Vector2> pts = new List<Vector2>(); public string note = ""; }

        static float CornerRadius(CityMap.Edge a, CityMap.Edge b)
        {
            // the curb returns' class radii (local 6 m, collector 9 m, arterial 12 m, link 15 m), never 0
            float Of(CityMap.Edge e) => e.link ? 15f : e.cls >= 3 ? 12f : e.cls >= 1 ? 9f : 6f;
            return Mathf.Min(Of(a), Of(b));
        }

        static void Borders(CityMap map, Arm arm)
        {
            var e = map.edges[arm.edge];
            for (float d = 0f; d <= Mathf.Min(e.length, ArmReachM) + 1e-3f; d += BorderStepM)
            {
                float s = arm.fwd ? d : e.length - d;
                var t = e.TangentAt(s); if (!arm.fwd) t = -t;
                var left = new Vector2(-t.y, t.x);
                LineModel.Extents(e, s, out float eM, out float eP);
                float pl = arm.fwd ? eP : eM, pr = arm.fwd ? eM : eP;
                var p = e.PointAt(s);
                arm.C.Add(p); arm.L.Add(p + left * pl); arm.R.Add(p - left * pr);
            }
        }

        static Vector2 Offset(List<Vector2> pl, int i, float off, bool leftSide)
        {
            var t = (pl[Mathf.Min(i + 1, pl.Count - 1)] - pl[Mathf.Max(i - 1, 0)]).normalized;
            var n = new Vector2(-t.y, t.x);
            return pl[i] + (leftSide ? n : -n) * off;
        }

        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 x, out float ta)
        {
            x = default; ta = 0f;
            var r = b - a; var s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            var q = c - a;
            float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            x = a + r * t; ta = t;
            return true;
        }

        /// <summary>The first crossing of two polylines (index along the first, the point).</summary>
        static bool Cross(List<Vector2> A, List<Vector2> B, out int ia, out int ib, out Vector2 x)
        {
            ia = ib = -1; x = default;
            for (int i = 0; i + 1 < A.Count; i++)
                for (int j = 0; j + 1 < B.Count; j++)
                    if (SegX(A[i], A[i + 1], B[j], B[j + 1], out x, out _)) { ia = i; ib = j; return true; }
            return false;
        }

        static int Foot(List<Vector2> pl, Vector2 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pl.Count; i++) { float d = (pl[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        static void ArcPts(Vector2 c, Vector2 from, Vector2 to, float r, bool ccw, List<Vector2> into)
        {
            float a0 = Mathf.Atan2(from.y - c.y, from.x - c.x), a1 = Mathf.Atan2(to.y - c.y, to.x - c.x);
            float sweep = a1 - a0;
            if (ccw) { while (sweep < 0f) sweep += 2f * Mathf.PI; } else { while (sweep > 0f) sweep -= 2f * Mathf.PI; }
            float step = 2f * Mathf.Acos(Mathf.Clamp01(1f - SagM / Mathf.Max(r, 0.05f)));
            if (r >= 6f) step = Mathf.Min(step, MaxTurnDeg * Mathf.Deg2Rad);
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) / Mathf.Max(step, 1e-3f)));
            for (int k = 0; k <= n; k++) { float a = a0 + sweep * k / n; into.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
        }

        static Corner Solve(CityMap map, Arm A, Arm B)
        {
            var c = new Corner { a = A.edge, b = B.edge };
            var ea = map.edges[A.edge]; var eb = map.edges[B.edge];
            float dAng = Mathf.Repeat(B.ang - A.ang, 360f);
            c.deg = dAng;
            if (dAng > ThroughDeg && dAng < 360f - ThroughDeg + 360f && dAng > ThroughDeg)
            {
                // one road going on (the sector between them is over 150 degrees): straight
                if (dAng >= ThroughDeg && dAng <= 210f && (ea.name == eb.name || ea.oneway || eb.oneway))
                {
                    c.shape = "through";
                    c.dA = 0f; c.dB = 0f; c.tA = A.L[0]; c.tB = B.R[0];
                    return c;
                }
            }
            bool crosses = Cross(A.L, B.R, out int ia, out int ib, out Vector2 x);
            float gap0 = Vector2.Distance(A.L[0], B.R[Foot(B.R, A.L[0])]);
            if (!crosses && gap0 >= NoseGapM)
            {
                // CONNECTOR: the borders start apart and never cross (arms of different nodes of one cluster): a
                // tangent-continuous Hermite curve from A's left border start (heading in) to B's right border start
                // (heading out), sampled to the same 1 cm / 6 degree rule
                c.shape = "connector"; c.tA = A.L[0]; c.tB = B.R[0]; c.dA = 0f; c.dB = 0f;
                var tIn = -(A.C[Mathf.Min(4, A.C.Count - 1)] - A.C[0]).normalized; var tOut = (B.C[Mathf.Min(4, B.C.Count - 1)] - B.C[0]).normalized;
                float k = Vector2.Distance(c.tA, c.tB);
                int n0 = Mathf.Clamp(Mathf.CeilToInt(k / 0.5f), 4, 400);
                for (int q = 0; q <= n0; q++)
                {
                    float t = q / (float)n0, t2 = t * t, t3 = t2 * t;
                    c.pts.Add((2 * t3 - 3 * t2 + 1) * c.tA + (t3 - 2 * t2 + t) * k * tIn + (-2 * t3 + 3 * t2) * c.tB + (t3 - t2) * k * tOut);
                }
                return c;
            }
            if (dAng < SideBySideDeg || !crosses)
            {
                // NOSE: out along both until the gap reaches NoseGapM
                int n = Mathf.Min(A.L.Count, B.R.Count);
                for (int i = 0; i < n; i++)
                {
                    float gap = Vector2.Distance(A.L[i], B.R[Foot(B.R, A.L[i])]);
                    if (gap < NoseGapM) continue;
                    c.shape = "nose";
                    c.tA = A.L[i]; int jb = Foot(B.R, A.L[i]); c.tB = B.R[jb];
                    c.dA = i * BorderStepM; c.dB = jb * BorderStepM;
                    c.r = 0.5f * Vector2.Distance(c.tA, c.tB);
                    c.centre = 0.5f * (c.tA + c.tB);
                    // the nose bulges toward the junction (the land's tip)
                    var outA = (A.C[Mathf.Min(i + 1, A.C.Count - 1)] - A.C[Mathf.Max(i - 1, 0)]).normalized;
                    var tip = c.centre - outA * c.r;
                    c.pts.Add(c.tA);
                    ArcPts(c.centre, c.tA, tip, c.r, true, c.pts); c.pts.RemoveAt(c.pts.Count - 1);
                    ArcPts(c.centre, tip, c.tB, c.r, true, c.pts);
                    // pick the sweep that passes the tip (anticlockwise from tA)
                    return c;
                }
                c.shape = "nose-not-found"; c.note = $"the gap never reaches {NoseGapM} m within {ArmReachM} m";
                c.tA = A.L[A.L.Count - 1]; c.tB = B.R[B.R.Count - 1]; c.dA = (A.L.Count - 1) * BorderStepM; c.dB = (B.R.Count - 1) * BorderStepM;
                return c;
            }
            // ARC: offset A's left border to its left (into the corner) and B's right border to its right, by R
            float R = CornerRadius(ea, eb);
            for (int tries = 0; tries < 6; tries++, R *= 0.7f)
            {
                var oA = new List<Vector2>(A.L.Count); var oB = new List<Vector2>(B.R.Count);
                for (int i = 0; i < A.L.Count; i++) oA.Add(Offset(A.L, i, R, true));
                for (int i = 0; i < B.R.Count; i++) oB.Add(Offset(B.R, i, R, false));
                if (!Cross(oA, oB, out int ja, out int jb, out Vector2 cc)) continue;
                int fa = Foot(A.L, cc), fb = Foot(B.R, cc);
                c.shape = "arc"; c.r = R; c.centre = cc; c.tA = A.L[fa]; c.tB = B.R[fb];
                c.dA = fa * BorderStepM; c.dB = fb * BorderStepM;
                // from A's left border round the corner (clockwise as seen from the centre) to B's right border
                ArcPts(cc, c.tA, c.tB, R, false, c.pts);
                return c;
            }
            c.shape = "arc-did-not-fit"; c.note = "no fillet down to 0.12 x the class radius";
            c.tA = x; c.tB = x; c.dA = ia * BorderStepM; c.dB = ib * BorderStepM;
            return c;
        }

        public static void Run()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_C7_TAG") ?? "run";
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "c7");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder($"C7 OUTLINE PROTOTYPE {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            var json = new StringBuilder("{\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.ComputeTrims(map);   // the line model's eases (bays, tapers) as the tiles see them
                log.Append($"trims {clock.ElapsedMilliseconds} ms\n");
                var rows = CityIslandGate.ReadSpots(System.Environment.GetEnvironmentVariable("PSX_ISL_SPOTS"));
                var raw = File.ReadAllLines(System.Environment.GetEnvironmentVariable("PSX_ISL_SPOTS")).Skip(1).Where(l => l.Trim().Length > 0).ToList();
                bool firstSpot = true;
                foreach (var line in raw)
                {
                    var id = line.Split(',')[0];
                    var nodesCol = SplitNodes(line);
                    if (nodesCol.Count == 0) continue;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var cluster = new HashSet<int>(nodesCol);
                    var arms = new List<Arm>();
                    var centre = Vector2.zero; foreach (int n in cluster) centre += map.nodes[n]; centre /= cluster.Count;
                    foreach (int n in cluster)
                        foreach (int ei in map.nodeEdges[n])
                        {
                            var e = map.edges[ei];
                            if (e.a == e.b) continue;
                            int other = e.a == n ? e.b : e.a;
                            if (cluster.Contains(other)) continue;   // internal to the junction
                            var arm = new Arm { edge = ei, node = n, fwd = e.a == n, name = e.name };
                            Borders(map, arm);
                            var p12 = arm.C[Mathf.Min(arm.C.Count - 1, Mathf.RoundToInt(12f / BorderStepM))];
                            arm.ang = Mathf.Atan2(p12.y - centre.y, p12.x - centre.x) * Mathf.Rad2Deg;
                            arms.Add(arm);
                        }
                    arms.Sort((p, q) => p.ang.CompareTo(q.ang));
                    var corners = new List<Corner>();
                    for (int i = 0; i < arms.Count; i++) corners.Add(Solve(map, arms[i], arms[(i + 1) % arms.Count]));
                    // cuts: the farther tangent of each arm's two corners; a nose cuts both arms on one line
                    for (int i = 0; i < arms.Count; i++)
                    {
                        var left = corners[i]; var right = corners[(i - 1 + arms.Count) % arms.Count];
                        arms[i].cut = Mathf.Max(left.dA, right.dB);
                    }
                    // the outline
                    var outline = new List<Vector2>();
                    for (int i = 0; i < arms.Count; i++)
                    {
                        var A = arms[i]; var c = corners[i]; var B = arms[(i + 1) % arms.Count];
                        int ka = Mathf.Min(A.L.Count - 1, Mathf.RoundToInt(A.cut / BorderStepM));
                        outline.Add(A.R[ka]); outline.Add(A.L[ka]);                      // the cut row
                        int ta = Mathf.RoundToInt(c.dA / BorderStepM);
                        for (int k = ka - 1; k > ta; k--) outline.Add(A.L[k]);            // in along A's left border
                        if (c.pts.Count > 0) outline.AddRange(c.pts); else { outline.Add(c.tA); outline.Add(c.tB); }
                        int tb = Mathf.RoundToInt(c.dB / BorderStepM), kb = Mathf.Min(B.R.Count - 1, Mathf.RoundToInt(B.cut / BorderStepM));
                        for (int k = tb + 1; k < kb; k++) outline.Add(B.R[k]);           // out along B's right border
                    }
                    float solveMs = (float)sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    int tris = EarClipCount(outline);
                    float fillMs = (float)sw.Elapsed.TotalMilliseconds;
                    log.Append(string.Format(inv, "spot {0}: {1} nodes, {2} arms, corners: {3}; outline {4} vertices; solve {5:0.0} ms, ear-clip fill {6} triangles {7:0.0} ms\n",
                        id, cluster.Count, arms.Count, string.Join(", ", corners.Select(cn => cn.shape + (cn.r > 0 ? $" r {cn.r:0.0}" : "") + $" ({cn.deg:0} deg)")), outline.Count, solveMs, tris, fillMs));
                    foreach (var cn in corners) if (cn.note.Length > 0) log.Append($"    e{cn.a} / e{cn.b}: {cn.shape}: {cn.note}\n");
                    // JSON
                    if (!firstSpot) json.Append(",\n"); firstSpot = false;
                    json.Append($"  \"{id}\": {{\n    \"nodes\": [{string.Join(",", cluster)}],\n    \"arms\": [");
                    json.Append(string.Join(", ", arms.Select(a => string.Format(inv, "{{\"edge\": {0}, \"node\": {1}, \"name\": \"{2}\", \"angle\": {3:0.0}, \"cut\": {4:0.0}}}", a.edge, a.node, (a.name ?? "").Replace("\"", "'"), a.ang, a.cut))));
                    json.Append("],\n    \"corners\": [");
                    json.Append(string.Join(", ", corners.Select(cn => string.Format(inv, "{{\"a\": {0}, \"b\": {1}, \"shape\": \"{2}\", \"r\": {3:0.00}, \"deg\": {4:0.0}, \"tA\": [{5:0.00}, {6:0.00}], \"tB\": [{7:0.00}, {8:0.00}]}}",
                        cn.a, cn.b, cn.shape, cn.r, cn.deg, cn.tA.x, cn.tA.y, cn.tB.x, cn.tB.y))));
                    json.Append("],\n    \"outline\": [");
                    json.Append(string.Join(", ", outline.Select(p => string.Format(inv, "[{0:0.00}, {1:0.00}]", p.x, p.y))));
                    json.Append("]\n  }");
                }
                json.Append("\n}\n");
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(dir, $"c7_outline_{tag}.json"), json.ToString());
            File.WriteAllText(Path.Combine(dir, $"c7_outline_{tag}.txt"), log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }


        /// <summary>(round 5 item 6) THE MEDIAN-OPENING CLUSTER RULE: two fan clusters become one junction when a node of
        /// each is the FAR NODE of a crossover (a one-way link arriving from the other carriageway of the same road, which
        /// runs the other way) and the two crossovers share ONE opening: their PAVED crossings (the line model's border
        /// polygons) intersect or lie within a lane width (3.66 m) of each other near the two nodes. Bounded so it cannot swallow two openings 100 m apart: the two nodes lie within the
        /// opening's own length along the road (twice the carriageways' spacing, at most OpeningMaxM), and NO THROUGH ARM
        /// between them: no other road leaves either carriageway at a node lying between the two along the road. Returns
        /// a line per decision; merges the key lists in place.</summary>
        public const float OpeningMaxM = 60f;
        public static List<string> JoinOpenings(CityMap map, Dictionary<int, List<int>> keys)
        {
            var log = new List<string>();
            var keyOf = new Dictionary<int, int>();
            foreach (var kv in keys) foreach (int n in kv.Value) keyOf[n] = kv.Key;
            // the far nodes: n with a one-way link ARRIVING from a carriageway of n's road that runs the other way
            var far = new List<(int n, int e, string road, Vector2 dir)>();
            foreach (int n in keyOf.Keys)
            {
                string road = null; Vector2 dir = Vector2.zero;
                foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; if (!e.link && e.oneway && !string.IsNullOrEmpty(e.name)) { road = e.name; dir = (map.nodes[e.b] - map.nodes[e.a]).normalized; break; } }
                if (road == null) continue;
                bool found = false;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (found || !e.link || !e.oneway || e.b != n) continue;
                    foreach (int fe in map.nodeEdges[e.a])
                    {
                        var c = map.edges[fe];
                        if (c.link || !c.oneway || c.name != road) continue;
                        var cd = (map.nodes[c.b] - map.nodes[c.a]).normalized;
                        if (Vector2.Dot(cd, dir) < -0.5f) { far.Add((n, ei, road, dir)); found = true; break; }
                    }
                }
            }
            for (int i = 0; i < far.Count; i++)
                for (int j = i + 1; j < far.Count; j++)
                {
                    var A = far[i]; var B = far[j];
                    if (A.road != B.road || Vector2.Dot(A.dir, B.dir) > -0.5f) continue;   // the two carriageways of one road
                    if (!keyOf.TryGetValue(A.n, out int ka) || !keyOf.TryGetValue(B.n, out int kb) || ka == kb) continue;
                    var ea = map.edges[A.e]; var eb = map.edges[B.e];
                    Vector2 pa = map.nodes[A.n], pb = map.nodes[B.n];
                    var u = A.dir; var w = new Vector2(-u.y, u.x);
                    float along = Mathf.Abs(Vector2.Dot(pb - pa, u)), across = Mathf.Abs(Vector2.Dot(pb - pa, w));
                    float open = Mathf.Min(OpeningMaxM, 2f * across);
                    if (along > open) { log.Add($"n{A.n} / n{B.n}: not joined, {along:0.0} m apart along the road (the opening's length {open:0.0} m)"); continue; }
                    // ONE OPENING: the paved gap in the median each crossing uses (its border polygon, the line model's, where
                    // it lies between the two carriageways' lines) overlaps the other's ALONG THE ROAD, or comes within a lane
                    float best = AlongGap(LinkPoly(ea, pa, pb), LinkPoly(eb, pa, pb), pa, u, w, Vector2.Dot(pb - pa, w));
                    if (best > LaneM) { log.Add($"n{A.n} / n{B.n}: not joined, their paved gaps in the median lie {best:0.0} m apart along the road (over a lane, {LaneM} m: two openings)"); continue; }
                    // no through arm between: no other road at a node of either carriageway lying between them along the road
                    string between = null;
                    float sb = Vector2.Dot(pb - pa, u);
                    foreach (int end in new[] { A.n, B.n })
                        foreach (int ei in map.nodeEdges[end])
                        {
                            var e = map.edges[ei]; if (e.link || e.name != A.road) continue;
                            int o = e.a == end ? e.b : e.a;
                            float so = Vector2.Dot(map.nodes[o] - pa, u);
                            if (so <= Mathf.Min(0f, sb) + 0.5f || so >= Mathf.Max(0f, sb) - 0.5f) continue;
                            foreach (int oe in map.nodeEdges[o]) { var x = map.edges[oe]; if (x.name != A.road && !x.link) between = $"n{o} ({x.name})"; }
                        }
                    if (between != null) { log.Add($"n{A.n} / n{B.n}: not joined, a through arm between them at {between}"); continue; }
                    foreach (int n in keys[kb]) { keys[ka].Add(n); keyOf[n] = ka; }
                    keys.Remove(kb);
                    log.Add($"n{A.n} / n{B.n}: JOINED (one median opening: {along:0.0} m along, {across:0.0} m across, paved crossings {best:0.0} m apart; e{A.e} / e{B.e})");
                }
            return log;
        }
        public const float LaneM = 3.66f, PaintedReachM = 40f;
        /// <summary>(round 5, 81) THE PAINTED-GORE RULE: a split (one way in, two out) whose two branches are drawn
        /// OVERLAPPING at the split (5 m out their centres are closer than their half widths less 0.5 m: one paved
        /// approach, the gore painted) joins the junction one branch reaches within PaintedReachM: the gore is paint to
        /// that junction's stop line, and the kerbed island is that junction's corner between the other branch and the
        /// road it meets.</summary>
        public static List<string> JoinPaintedSplits(CityMap map, Dictionary<int, List<int>> keys)
        {
            var log = new List<string>();
            var keyOf = new Dictionary<int, int>();
            foreach (var kv in keys) foreach (int n in kv.Value) keyOf[n] = kv.Key;
            foreach (var k in keys.Keys.ToList())
            {
                if (!keys.TryGetValue(k, out var nodes) || nodes.Count != 1) continue;
                int n = nodes[0];
                var outs = new List<CityMap.Edge>(); int ins = 0, other = 0;
                foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; if (e.a == e.b) continue; if (!e.oneway) other++; else if (e.a == n) outs.Add(e); else ins++; }
                if (other > 0 || ins != 1 || outs.Count != 2) continue;
                float at = Mathf.Min(5f, Mathf.Min(outs[0].length, outs[1].length) * 0.5f);
                LineModel.Extents(outs[0], at, out float m0, out float p0); LineModel.Extents(outs[1], at, out float m1, out float p1);
                float gap = (outs[0].PointAt(at) - outs[1].PointAt(at)).magnitude;
                float halves = 0.5f * (m0 + p0) + 0.5f * (m1 + p1);
                if (gap >= halves - 0.5f) continue;
                foreach (var b in outs)
                {
                    if (b.length > PaintedReachM || !keyOf.TryGetValue(b.b, out int kb) || kb == k) continue;
                    foreach (int x in keys[k]) { keys[kb].Add(x); keyOf[x] = kb; }
                    keys.Remove(k);
                    log.Add($"n{n}: painted split JOINED to the junction e{b.index} reaches {b.length:0.0} m on (branches overlap {halves - gap:0.0} m at {at:0.0} m)");
                    break;
                }
            }
            return log;
        }
        /// <summary>A link's paved polygon from the line model's borders, the part within reach of the two nodes.</summary>
        static List<Vector2> LinkPoly(CityMap.Edge e, Vector2 pa, Vector2 pb)
        {
            var mid = 0.5f * (pa + pb); float reach = (pb - pa).magnitude + 15f;
            var L = new List<Vector2>(); var R = new List<Vector2>();
            for (float d = 0f; d <= e.length + 1e-3f; d += 1f)
            {
                var p = e.PointAt(d); if ((p - mid).magnitude > reach) continue;
                var t = e.TangentAt(d); var left = new Vector2(-t.y, t.x);
                LineModel.Extents(e, d, out float eM, out float eP);
                L.Add(p + left * eP); R.Add(p - left * eM);
            }
            R.Reverse(); L.AddRange(R);
            return L;
        }
        /// <summary>Along the road (u), the gap between the stretches two polygons cover inside the median band (across, w,
        /// strictly between 0 and acrossB: the two nodes' carriageway lines); 0 where they overlap.</summary>
        static float AlongGap(List<Vector2> P, List<Vector2> Q, Vector2 o, Vector2 u, Vector2 w, float acrossB)
        {
            float lo = Mathf.Min(0f, acrossB), hi = Mathf.Max(0f, acrossB), m = 0.25f * (hi - lo);
            bool Range(List<Vector2> X, out float a, out float b)
            {
                a = float.MaxValue; b = float.MinValue;
                foreach (var q in X) { float c = Vector2.Dot(q - o, w); if (c < lo + m || c > hi - m) continue; float t = Vector2.Dot(q - o, u); a = Mathf.Min(a, t); b = Mathf.Max(b, t); }
                return a <= b;
            }
            if (!Range(P, out float p0, out float p1) || !Range(Q, out float q0, out float q1)) return float.MaxValue;
            return Mathf.Max(0f, Mathf.Max(p0, q0) - Mathf.Min(p1, q1));
        }
        /// <summary>The gap between two polygons' outlines (0 where they touch or one holds a point of the other).</summary>
        static float PolyGap(List<Vector2> P, List<Vector2> Q)
        {
            if (P.Count < 3 || Q.Count < 3) return float.MaxValue;
            if (JunctionOutline.InPoly(P, Q[0]) || JunctionOutline.InPoly(Q, P[0])) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < P.Count; i++)
                for (int j = 0; j < Q.Count; j++)
                    best = Mathf.Min(best, SegDist(P[i], P[(i + 1) % P.Count], Q[j], Q[(j + 1) % Q.Count]));
            return best;
        }
        static float SegDist(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var r = b - a; var s = d - c; float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) > 1e-9f)
            {
                var q = c - a; float t = (q.x * s.y - q.y * s.x) / den, v = (q.x * r.y - q.y * r.x) / den;
                if (t >= 0f && t <= 1f && v >= 0f && v <= 1f) return 0f;
            }
            return Mathf.Min(Mathf.Min(PD(a, c, d), PD(b, c, d)), Mathf.Min(PD(c, a, b), PD(d, a, b)));
        }
        static float PD(Vector2 p, Vector2 x, Vector2 y) { var xy = y - x; float l2 = xy.sqrMagnitude; float tt = l2 > 1e-9f ? Mathf.Clamp01(Vector2.Dot(p - x, xy) / l2) : 0f; return (x + xy * tt - p).magnitude; }

        /// <summary>A cluster's plain solver inputs: its arms (edges with one end in it, 80 m of the line model's borders,
        /// centre and profile heights) and its links (edges with both ends in it).</summary>
        public static void BuildCluster(CityMap map, HashSet<int> cluster, out List<JunctionOutline.Arm> arms, out List<JunctionOutline.Link> links) => BuildCluster(map, cluster, out arms, out links, out _);
        /// <summary>... in the cluster's own frame (about its first node: float metres a few thousand from the map's origin
        /// lose half a millimetre, enough to make a near-touching outline cross itself, as 81's did).</summary>
        public static void BuildCluster(CityMap map, HashSet<int> cluster, out List<JunctionOutline.Arm> arms, out List<JunctionOutline.Link> links, out Vector2 origin)
        {
            arms = new List<JunctionOutline.Arm>(); links = new List<JunctionOutline.Link>();
            origin = map.nodes[cluster.First()]; var o0 = origin;
            var seenE = new HashSet<int>();
            foreach (int n in cluster)
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    bool inA = cluster.Contains(e.a), inB = cluster.Contains(e.b);
                    if (inA && inB)
                    {
                        if (!seenE.Add(ei)) continue;
                        var lk = new JunctionOutline.Link { id = ei };
                        for (float d = 0f; d <= e.length + 1e-3f; d += JunctionOutline.Step)
                        {
                            var t = e.TangentAt(d); var left = new Vector2(-t.y, t.x);
                            LineModel.Extents(e, d, out float eM, out float eP);
                            var p = e.PointAt(d) - o0;
                            lk.L.Add(p + left * eP); lk.R.Add(p - left * eM);
                        }
                        if (lk.L.Count >= 2) links.Add(lk);
                        continue;
                    }
                    var arm = new JunctionOutline.Arm { id = ei, oneway = e.oneway, name = e.name ?? "", radius = e.link ? 15f : e.cls >= 3 ? 12f : e.cls >= 1 ? 9f : 6f, Y = new List<float>() };
                    bool fwd = e.a == n;
                    for (float d = 0f; d <= Mathf.Min(e.length, 80f) + 1e-3f; d += JunctionOutline.Step)
                    {
                        float s = fwd ? d : e.length - d;
                        var t = e.TangentAt(s); if (!fwd) t = -t;
                        var left = new Vector2(-t.y, t.x);
                        LineModel.Extents(e, s, out float eM, out float eP);
                        var p = e.PointAt(s) - o0;
                        arm.C.Add(p); arm.L.Add(p + left * (fwd ? eP : eM)); arm.R.Add(p - left * (fwd ? eM : eP)); arm.Y.Add(e.YAt(s));
                    }
                    if (arm.C.Count >= 4) arms.Add(arm);
                }
        }

        /// <summary>A cluster's plain inputs as a fixture (JunctionOutlineTests reads them), coordinates about the cluster's
        /// first node, centimetres.</summary>
        public static string FixtureText(string name, string kind, Vector2 origin, List<JunctionOutline.Arm> arms, List<JunctionOutline.Link> links)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append($"fixture {name}\nkind {kind}\norigin {origin.x.ToString("0.00", inv)} {origin.y.ToString("0.00", inv)}\n");
            string P(List<Vector2> l) => string.Join(" ", l.Select(q => (q.x - origin.x).ToString("0.00", inv) + "," + (q.y - origin.y).ToString("0.00", inv)));
            foreach (var a in arms)
            {
                sb.Append($"arm {a.id} {(a.oneway ? 1 : 0)} {a.radius.ToString("0.0", inv)} {a.name}\n");
                sb.Append("L " + P(a.L) + "\nR " + P(a.R) + "\nC " + P(a.C) + "\n");
                sb.Append("Y " + string.Join(" ", a.Y.Select(y => y.ToString("0.000", inv))) + "\n");
            }
            foreach (var l in links) sb.Append($"link {l.id}\nL " + P(l.L) + "\nR " + P(l.R) + "\n");
            return sb.ToString();
        }

        /// <summary>(round 5) THE WHOLE CITY, edit mode, no mesh: every junction cluster (FanKey, then the median-opening
        /// rule) through MergeBays and the solver. Per cluster: clean / kink (which pieces meet) / refused (why) / fill
        /// mismatch, by the scorecard's kind; the time for the whole city. PSX_C7_FIXTURES ("name:node,name:node"): the
        /// clusters holding those nodes are written as fixtures. Output: c7_city_{tag}.txt / .tsv and fixtures/*.txt.</summary>
        public static void RunCity()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_C7_TAG") ?? "run";
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "c7");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "fixtures"));
            var log = new StringBuilder($"C7 CITY OUTLINES {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            var tsv = new StringBuilder("key\tnodes\tkind\tresult\tdetail\tarms\tlinks\tms\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var trims = CityMeshes.ComputeTrims(map);
                var crossFar = new HashSet<int>(CityMeshes.CrossoverFanNodes);
                var splitNodes = new HashSet<int>();
                if (map.splt != null) foreach (var sp in map.splt) splitNodes.Add(sp.node);
                var want = new Dictionary<int, string>();
                foreach (var kv in (System.Environment.GetEnvironmentVariable("PSX_C7_FIXTURES") ?? "").Split(','))
                { var t = kv.Split(':'); if (t.Length == 2 && int.TryParse(t[1], out int nd)) want[nd] = t[0]; }
                var keys = new Dictionary<int, List<int>>();
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (!trims.patch[n]) continue;
                    int k = CityMeshes.FanKey(trims, n);
                    if (!keys.TryGetValue(k, out var l)) keys[k] = l = new List<int>();
                    l.Add(n);
                }
                // a split or merge the generator draws without a fan (no patch: SPLT, gores) is a junction of the outline too
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (trims.patch[n]) continue;
                    int deg = 0; foreach (int ei in map.nodeEdges[n]) if (map.edges[ei].a != map.edges[ei].b) deg++;
                    if (deg >= 3) keys[-1 - n] = new List<int> { n };
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var joins = JoinOpenings(map, keys);
                joins.AddRange(JoinPaintedSplits(map, keys));
                double joinMs = sw.Elapsed.TotalMilliseconds;
                int joined = joins.Count(x => x.Contains("JOINED"));
                string[] order = { "median opening", "split of one road", "skew crossing", "ramp / link fork", "slip lane", "crossing corner", "T corner", "bend" };
                var tally = new SortedDictionary<string, int[]>();   // kind -> clean, kink, refused, fill
                var why = new SortedDictionary<string, int>();
                var kinkWhy = new SortedDictionary<string, int>();
                double solveAll = 0;
                foreach (var kv in keys)
                {
                    var cluster = new HashSet<int>(kv.Value);
                    string kind = "bend / dead end (2 arms or fewer)"; int best = 99;
                    foreach (int n in cluster)
                    {
                        string kn = CityKinkProbe.KindOf(map, n, crossFar, splitNodes);
                        for (int o = 0; o < order.Length; o++) if (kn.StartsWith(order[o]) && o < best) { best = o; kind = kn; }
                    }
                    if (cluster.Count > 1) kind += " [" + cluster.Count + " nodes]";
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    BuildCluster(map, cluster, out var arms, out var links, out var origin);
                    if (arms.Count < 3 && links.Count == 0) continue;   // a bend or dead end: no junction outline
                    var rawArms = arms.ConvertAll(x => new JunctionOutline.Arm { id = x.id, oneway = x.oneway, radius = x.radius, name = x.name, L = new List<Vector2>(x.L), R = new List<Vector2>(x.R), C = new List<Vector2>(x.C), Y = new List<float>(x.Y) });
                    var rawLinks = links.ConvertAll(x => new JunctionOutline.Link { id = x.id, L = new List<Vector2>(x.L), R = new List<Vector2>(x.R) });
                    JunctionOutline.MergeBays(arms, links);
                    var r = JunctionOutline.Solve(arms, null, links);
                    string res, detail;
                    if (r.refused != null) { res = "refused"; detail = r.refused; }
                    else
                    {
                        detail = OutlineKink(r);
                        if (detail != null) res = "kink";
                        else { detail = FillArea(r); res = detail == null ? "clean" : "fill"; }
                    }
                    double ms = sw.Elapsed.TotalMilliseconds - t0; solveAll += ms;
                    string kindKey = kind.Split('(')[0].Trim();
                    if (cluster.Count > 1) kindKey += " (cluster)";
                    if (!tally.TryGetValue(kindKey, out var c4)) tally[kindKey] = c4 = new int[4];
                    c4[res == "clean" ? 0 : res == "kink" ? 1 : res == "refused" ? 2 : 3]++;
                    if (res == "refused") { string w = Category(r.refused); why[w] = (why.TryGetValue(w, out int q) ? q : 0) + 1; }
                    if (res == "kink") { string w = KinkCategory(detail); kinkWhy[w] = (kinkWhy.TryGetValue(w, out int q) ? q : 0) + 1; }
                    tsv.Append($"{kv.Key}\t{string.Join(" ", cluster)}\t{kind}\t{res}\t{(detail ?? "").Replace('\t', ' ')}\t{arms.Count}\t{links.Count}\t{ms.ToString("0.0", inv)}\n");
                    foreach (int n in cluster)
                        if (want.TryGetValue(n, out string fname))
                            File.WriteAllText(Path.Combine(dir, "fixtures", fname + ".txt"), FixtureText(fname, kind + " | " + res + ": " + (detail ?? ""), Vector2.zero, rawArms, rawLinks).Replace("origin 0.00 0.00", "origin " + origin.x.ToString("0.00", inv) + " " + origin.y.ToString("0.00", inv)));
                }
                int[] tot = new int[4]; foreach (var c4 in tally.Values) for (int i = 0; i < 4; i++) tot[i] += c4[i];
                log.Append($"{tot.Sum()} junction clusters (3+ arms or a link) solved in {sw.Elapsed.TotalSeconds:0.0} s ({joinMs / 1000.0:0.0} s the opening rule, {joined} openings joined): " +
                           $"{tot[0]} clean, {tot[1]} with a kink, {tot[2]} refused, {tot[3]} fill mismatch\n\nBY KIND (clean / kink / refused / fill):\n");
                foreach (var kv in tally) log.Append($"  {kv.Key}: {kv.Value[0]} / {kv.Value[1]} / {kv.Value[2]} / {kv.Value[3]}\n");
                log.Append("\nREFUSED, WHY:\n"); foreach (var kv in why.OrderByDescending(x => x.Value)) log.Append($"  {kv.Value}  {kv.Key}\n");
                log.Append("\nKINKS, WHICH PIECES MEET:\n"); foreach (var kv in kinkWhy.OrderByDescending(x => x.Value)) log.Append($"  {kv.Value}  {kv.Key}\n");
                log.Append("\nOPENING RULE:\n"); foreach (var l in joins) log.Append("  " + l + "\n");
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(dir, $"c7_city_{tag}.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, $"c7_city_{tag}.tsv"), tsv.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }

        /// <summary>The first rule-4 kink of an outline away from its end rows: which pieces meet, at what angle.</summary>
        public static string OutlineKink(JunctionOutline.Result r)
        {
            var o = r.outline; int m = o.Count;
            var rows = new List<Vector2>(); for (int i = 0; i < r.rowR.Length; i++) { rows.Add(r.rowR[i]); rows.Add(r.rowL[i]); }
            for (int i = 0; i < m; i++)
            {
                var b = o[i]; bool atRow = false; foreach (var q in rows) if ((q - b).sqrMagnitude < 1e-4f) { atRow = true; break; }
                if (atRow) continue;
                var a = o[(i - 1 + m) % m]; var c = o[(i + 1) % m];
                if (JunctionOutline.Kink(a, b, c))
                    return $"[{JunctionOutline.Piece(r.outlineSrc, (i - 1 + m) % m)}] meets [{JunctionOutline.Piece(r.outlineSrc, i)}] at {Vector2.Angle(b - a, c - b):0.0} deg";
            }
            return null;
        }
        /// <summary>The fill against the outline: triangle area equal to the ring's (0.1 %), none face down.</summary>
        public static string FillArea(JunctionOutline.Result r)
        {
            double ring = 0; var o = r.outline;
            for (int i = 0; i < o.Count; i++) { var a = o[i]; var b = o[(i + 1) % o.Count]; ring += a.x * b.y - b.x * a.y; }
            ring = System.Math.Abs(ring) * 0.5;
            double tri = 0; int down = 0;
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
            {
                Vector2 a = r.fillVerts[r.tris[t]], b = r.fillVerts[r.tris[t + 1]], c = r.fillVerts[r.tris[t + 2]];
                double ar = 0.5 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                if (ar < -1e-6) down++;
                tri += ar;
            }
            if (down > 0 || System.Math.Abs(tri - ring) > 0.001 * ring + 0.01) return $"fill {tri:0.0} m2 against the outline's {ring:0.0} m2, {down} face down";
            return null;
        }
        static string Category(string refused)
        {
            if (refused.StartsWith("the outline crosses itself")) { var m = System.Text.RegularExpressions.Regex.Matches(refused, @"\[([^\]]*)\]"); return "crosses itself: " + (m.Count >= 2 ? Kinds(m[0].Groups[1].Value) + " x " + Kinds(m[1].Groups[1].Value) : refused); }
            if (refused.StartsWith("corner")) return "corner: " + System.Text.RegularExpressions.Regex.Replace(refused, @"corner \d+/\d+: ", "");
            return System.Text.RegularExpressions.Regex.Replace(refused, @"(arm |e)\d+", "$1#").Replace("  ", " ");
        }
        static string KinkCategory(string d) { var m = System.Text.RegularExpressions.Regex.Matches(d, @"\[([^\]]*)\]"); return m.Count >= 2 ? Kinds(m[0].Groups[1].Value) + " | " + Kinds(m[1].Groups[1].Value) : d; }
        static string Kinds(string piece) => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(piece, @"e\d+", "e"), @" \(via link \d+\)", " (via link)");

        /// <summary>(round 5 item 4) THE REAL CLUSTERS: for each spot of the table, every junction fan cluster within its
        /// radius (the generator's own clustering: the nodes that share a FanKey), its arms (edges with one end in the
        /// cluster) sampled from the line model's borders out to 80 m, solved by the pure JunctionOutline. Writes
        /// c7_outline2_{tag}.json (per spot, per cluster: nodes, corners, cuts, outline, refusal) and .txt.</summary>
        public static void RunClusters()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_C7_TAG") ?? "run";
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "c7");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder($"C7 OUTLINE (real clusters, pure solver) {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            var json = new StringBuilder("{\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var trims = CityMeshes.ComputeTrims(map);
                var rows = CityIslandGate.ReadSpots(System.Environment.GetEnvironmentVariable("PSX_ISL_SPOTS"));
                bool firstSpot = true;
                foreach (var sp in rows)
                {
                    var keys = new Dictionary<int, List<int>>();
                    for (int n = 0; n < map.nodes.Length; n++)
                    {
                        if (!trims.patch[n] || (map.nodes[n] - sp.p).magnitude > sp.r) continue;
                        int k = CityMeshes.FanKey(trims, n);
                        if (!keys.TryGetValue(k, out var l)) keys[k] = l = new List<int>();
                        l.Add(n);
                    }
                    // a cluster's members may lie beyond the radius: take every node of the key
                    foreach (var k in keys.Keys.ToList())
                        for (int n = 0; n < map.nodes.Length; n++)
                            if (trims.patch[n] && CityMeshes.FanKey(trims, n) == k && !keys[k].Contains(n)) keys[k].Add(n);
                    foreach (var line in JoinOpenings(map, keys)) log.Append($"spot {sp.id}: {line}\n");
                    if (!firstSpot) json.Append(",\n"); firstSpot = false;
                    json.Append($"  \"{sp.id}\": [");
                    bool firstC = true;
                    foreach (var kv in keys)
                    {
                        var cluster = new HashSet<int>(kv.Value);
                        BuildCluster(map, cluster, out var arms, out var links);
                        var rawArms = arms.ConvertAll(x => new JunctionOutline.Arm { id = x.id, L = new List<Vector2>(x.L), R = new List<Vector2>(x.R), C = x.C });
                        foreach (var line in JunctionOutline.MergeBays(arms, links)) log.Append($"spot {sp.id} cluster {kv.Key}: {line}\n");
                        var r = JunctionOutline.Solve(arms, null, links);
                        log.Append(string.Format(inv, "spot {0} cluster {1} ({2} nodes {3}): {4} arms; corners: {5}; {6}; outline {7} vertices, fill {8} triangles ({9} slivers); solve {10:0.0} ms, fill {11:0.0} ms; check: {12}\n",
                            sp.id, kv.Key, cluster.Count, string.Join(" ", cluster.Select(x => "n" + x)), arms.Count, string.Join(", ", r.corners.Select(c => c.shape + (c.r > 0 ? $" r{c.r:0.0}" : ""))),
                            r.refused == null ? "solved" : "REFUSED: " + r.refused, r.outline.Count, r.tris.Count / 3, r.slivers, r.solveMs, r.fillMs, JunctionOutlineTests.Check(r, 0.5f) ?? "PASS"));
                        if (!firstC) json.Append(", "); firstC = false;
                        json.Append($"{{\"key\": {kv.Key}, \"nodes\": [{string.Join(",", cluster)}], \"refused\": \"{(r.refused ?? "").Replace("\"", "'")}\", \"corners\": [");
                        json.Append(string.Join(", ", r.corners.Select(c => string.Format(inv, "{{\"shape\": \"{0}\", \"r\": {1:0.00}, \"tA\": [{2:0.00}, {3:0.00}], \"tB\": [{4:0.00}, {5:0.00}]}}", c.shape, c.r, c.tA.x, c.tA.y, c.tB.x, c.tB.y))));
                        string P(IEnumerable<Vector2> l) => "[" + string.Join(",", l.Select(q => string.Format(inv, "[{0:0.00},{1:0.00}]", q.x, q.y))) + "]";
                        json.Append("], \"outline\": " + P(r.outline));
                        json.Append(", \"raw\": [" + string.Join(",", rawArms.Select(x => "{\"id\":" + x.id + ",\"L\":" + P(x.L.Take(121)) + ",\"R\":" + P(x.R.Take(121)) + "}")) + "]");
                        json.Append(", \"arms\": [" + string.Join(",", r.arms.Select(x => "{\"id\":" + x.id + ",\"L\":" + P(x.L.Take(121)) + ",\"R\":" + P(x.R.Take(121)) + "}")) + "]");
                        json.Append(", \"links\": [" + string.Join(",", links.Select(x => "{\"id\":" + x.id + ",\"L\":" + P(x.L) + ",\"R\":" + P(x.R) + "}")) + "]");
                        json.Append(", \"verts\": " + P(r.fillVerts) + ", \"tris\": [" + string.Join(",", r.tris) + "]");
                        json.Append(string.Format(inv, ", \"planeDev\": {0:0.0000}, \"rows\": \"{1}\"}}", r.planeDev, r.yR == null ? "" : string.Join(" ", r.yR.Select(y => y.ToString("0.00", inv)))));
                    }
                    json.Append("]");
                }
                json.Append("\n}\n");
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(dir, $"c7_outline2_{tag}.json"), json.ToString());
            File.WriteAllText(Path.Combine(dir, $"c7_outline2_{tag}.txt"), log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }

        static List<int> SplitNodes(string line)
        {
            // the csv's 6th column: "n4434 n4441 ..."
            var o = new List<int>();
            var cols = new List<string>(); var sb = new StringBuilder(); bool q = false;
            foreach (char ch in line) { if (ch == '"') q = !q; else if (ch == ',' && !q) { cols.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch); }
            cols.Add(sb.ToString());
            if (cols.Count < 6) return o;
            foreach (var tok in cols[5].Split(' ')) if (tok.StartsWith("n") && int.TryParse(tok.Substring(1), out int n)) o.Add(n);
            return o;
        }

        /// <summary>An ear clip of a simple polygon (cost only; a self-crossing outline stops early).</summary>
        static int EarClipCount(List<Vector2> poly)
        {
            var idx = Enumerable.Range(0, poly.Count).ToList();
            float area = 0f; for (int i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; area += a.x * b.y - b.x * a.y; }
            if (area < 0f) idx.Reverse();
            int tris = 0, guard = 0;
            while (idx.Count > 3 && guard++ < poly.Count * poly.Count)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    var a = poly[idx[(i - 1 + idx.Count) % idx.Count]]; var b = poly[idx[i]]; var c = poly[idx[(i + 1) % idx.Count]];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 0f) continue;
                    bool inside = false;
                    for (int j = 0; j < idx.Count && !inside; j++)
                    {
                        var p = poly[idx[j]];
                        if (p == a || p == b || p == c) continue;
                        inside = Tri(a, b, c, p);
                    }
                    if (inside) continue;
                    idx.RemoveAt(i); tris++; cut = true; break;
                }
                if (!cut) break;
            }
            return idx.Count == 3 ? tris + 1 : tris;
        }
        static bool Tri(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y), d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y), d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
