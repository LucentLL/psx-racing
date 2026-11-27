using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface stage 1: the junctions that are ONE PAVED SURFACE. For every junction the measures source
    /// (JunctionMeasures) names, the cluster's arms are built from the map (line-model borders, replaced by the measured
    /// pavement offsets), bays merged, the outline solved (JunctionOutline) and kept: its end rows become the arms'
    /// ribbon trims, its fill the junction's pavement (CityMeshes.RoadSurface.cs draws it). Solved once per map load.
    /// A junction with no measure keeps the rules and today's fan (the rules-provider clusters are still solved by the
    /// editor prototype only: CityOutlineProto). A refused outline keeps the old fan and logs why.
    /// </summary>
    public static class JunctionSurfaces
    {
        public sealed class Built
        {
            public string name = ""; public HashSet<int> nodes = new HashSet<int>(); public int ownerNode;
            public Vector2 origin; public JunctionOutline.Result res;
            /// <summary>Per edge with one end in the junction: where its ribbon ends, metres from that end.</summary>
            public Dictionary<int, float> rowOf = new Dictionary<int, float>();
            /// <summary>Edges with both ends in the junction: no ribbon.</summary>
            public HashSet<int> inside = new HashSet<int>();
            public List<string> log = new List<string>();
            public int mainEdge = -1, mainNode = -1;
            /// <summary>THE LANE CHECK (JunctionOutline.LaneCheck): the rule's ribbons (lanes, paint, traffic) that stand
            /// off the measured pavement inside the junction - the measure moved the kerb, not the lanes (one line an edge).</summary>
            public List<string> laneConflicts = new List<string>();
            /// <summary>(round 2) A bay merged into its host: metres out from the node where it becomes the host's lane
            /// (nearer the node it runs on its own line, the link through the opening).</summary>
            public Dictionary<int, float> bayFrom = new Dictionary<int, float>();
            public Dictionary<int, int> bayHost = new Dictionary<int, int>();
            /// <summary>(round 2) Per fitted arm: the lanes (from the right, first..last) that have a receiving lane on its
            /// through partner; the others end at the row (a turn lane whose exit the map lacks).</summary>
            public Dictionary<int, (int lo, int hi)> carried = new Dictionary<int, (int, int)>();
            public double ms;
            public bool Solved => res != null && res.refused == null;
            public Vector2 min, max;   // the outline's box, about origin
        }

        static CityMap forMap; static JunctionMeasures.Set forSet; static List<Built> all = new List<Built>(); static Dictionary<int, Built> byNode = new Dictionary<int, Built>();
        public static IReadOnlyList<Built> All => all;
        public static void Reset() { forMap = null; all = new List<Built>(); byNode = new Dictionary<int, Built>(); }

        /// <summary>Is the node one of a junction the measures name (and, once the junctions are solved, one that solved)?
        /// For readers that run where nothing is solved (a baked tile's furniture).</summary>
        public static bool MeasuredNode(int n)
        {
            if (forMap != null) return AtNode(n) != null;
            var set = JunctionMeasures.Current;
            if (set == null) return false;
            foreach (var j in set.junctions) if (j.nodes.Contains(n)) return true;
            return false;
        }

        /// <summary>The junction that owns a node as one surface (null: the old fan).</summary>
        public static Built AtNode(int n) => byNode.TryGetValue(n, out var b) && b.Solved ? b : null;

        /// <summary>The solved junction whose OUTLINE holds a map point (its pavement and its islands; null: none). For
        /// emitters and readers that must keep out of a junction drawn as one surface.</summary>
        public static Built Covering(Vector2 world)
        {
            foreach (var b in all)
            {
                if (!b.Solved) continue;
                var p = world - b.origin;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;
                if (JunctionOutline.InPoly(b.res.outline, p)) return b;
            }
            return null;
        }

        /// <summary>(round 2) Is a map point ON AN ISLAND of a solved junction (a hole of its pavement)? The gates count
        /// a measured island as an island, not as lane cells.</summary>
        public static bool OnIsland(Vector2 world, float grow = 0f)
        {
            foreach (var b in all)
            {
                if (!b.Solved) continue;
                var p = world - b.origin;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;
                foreach (var h in b.res.holes)
                    if (JunctionOutline.InPoly(h, p) || (grow > 0f && NearRing(h, p, grow))) return true;
            }
            return false;
        }
        static bool NearRing(List<Vector2> ring, Vector2 p, float d)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var ab = ring[(i + 1) % ring.Count] - a; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                if ((a + ab * t - p).sqrMagnitude < d * d) return true;
            }
            return false;
        }

        /// <summary>(round 2) THE KINK PROBE READS WHAT IS BUILT: the free outline (kerb lines, island rings) of every
        /// solved junction with a node within r + 40 m of p, as world rings; onRow[i]: vertex i lies on an end row (a
        /// mouth: the ribbon goes on there, no kerb).</summary>
        public static void OutlineRings(Vector2 p, float r, List<(List<Vector2> ring, List<bool> onRow)> into)
        {
            foreach (var b in all)
            {
                if (!b.Solved || forMap == null) continue;
                bool near = false;
                foreach (int n in b.nodes) near |= (forMap.nodes[n] - p).magnitude <= r + 40f;
                if (!near) continue;
                var rings = new List<List<Vector2>> { b.res.outline };
                rings.AddRange(b.res.holes);
                foreach (var ring in rings)
                {
                    var w = new List<Vector2>(ring.Count); var on = new List<bool>(ring.Count);
                    foreach (var q in ring)
                    {
                        w.Add(q + b.origin);
                        bool row = false;
                        for (int a = 0; a < b.res.rowR.Length && !row; a++)
                        {
                            var ra = b.res.rowR[a]; var ab = b.res.rowL[a] - ra; float l2 = ab.sqrMagnitude;
                            float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - ra, ab) / l2) : 0f;
                            row = (ra + ab * t - q).sqrMagnitude < 0.03f * 0.03f;
                        }
                        on.Add(row);
                    }
                    into.Add((w, on));
                }
            }
        }

        /// <summary>Solve every measured junction once for this map. farTrim(edge, node): the rule's trim of an edge at its
        /// OTHER end (an arm is only as long as the ribbon the far junction leaves it).</summary>
        public static List<Built> Ensure(CityMap map, System.Func<CityMap.Edge, int, float> farTrim)
        {
            var set = JunctionMeasures.Current;
            if (ReferenceEquals(forMap, map) && ReferenceEquals(forSet, set)) return all;
            forMap = map; forSet = set; all = new List<Built>(); byNode = new Dictionary<int, Built>();
            if (set == null) return all;
            foreach (var jm in set.junctions)
            {
                var b = new Built { name = jm.name };
                foreach (int n in jm.nodes) if (n >= 0 && n < map.nodes.Length) b.nodes.Add(n);
                foreach (var (ja, jb) in jm.join) { if (ja >= 0 && ja < map.nodes.Length) b.nodes.Add(ja); if (jb >= 0 && jb < map.nodes.Length) b.nodes.Add(jb); }
                if (b.nodes.Count == 0) continue;
                b.ownerNode = b.nodes.Min();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { Solve(map, set, jm, b, farTrim); }
                catch (System.Exception ex) { b.res = new JunctionOutline.Result { refused = "exception: " + ex.Message }; }
                b.ms = sw.Elapsed.TotalMilliseconds;
                if (b.res != null && b.res.outline.Count > 0) { b.min = b.max = b.res.outline[0]; foreach (var q in b.res.outline) { b.min = Vector2.Min(b.min, q); b.max = Vector2.Max(b.max, q); } }
                all.Add(b);
                foreach (int n in b.nodes) byNode[n] = b;
                Debug.Log($"[JunctionSurfaces] {b.name} (nodes {string.Join(" ", b.nodes.OrderBy(x => x))}): {(b.Solved ? "solved" : "REFUSED: " + b.res.refused + " (the old fan stays)")}; provider {set.provider}; {b.res.outline.Count} outline vertices, {b.res.holes.Count} islands, {b.res.tris.Count / 3} triangles; solve {b.res.solveMs:0.0} ms, fill {b.res.fillMs:0.0} ms, in all {b.ms:0.0} ms; rows {string.Join(", ", b.rowOf.Select(kv => "e" + kv.Key + " " + kv.Value.ToString("0.0")))}; {string.Join("; ", b.log)}; LANE CHECK: {(b.laneConflicts.Count == 0 ? "clear" : string.Join("; ", b.laneConflicts))}");
            }
            return all;
        }

        /// <summary>A cluster's arms (edges with one end in it: the line model's borders out to maxOut metres or to the far
        /// end's trim, centre and profile heights), with the measured offsets applied, and its links (both ends in it).
        /// About 'origin' (the cluster's first node): absolute metres lose half a millimetre in float.</summary>
        public static void BuildCluster(CityMap map, HashSet<int> cluster, ICollection<int> drop, System.Func<CityMap.Edge, int, float> farTrim,
                                        out List<JunctionOutline.Arm> arms, out List<JunctionOutline.Link> links, out Vector2 origin, float maxOut = 80f)
        {
            arms = new List<JunctionOutline.Arm>(); links = new List<JunctionOutline.Link>();
            origin = map.nodes[cluster.Min()]; var o0 = origin;
            var seenE = new HashSet<int>();
            foreach (int n in cluster.OrderBy(x => x))
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    if (drop != null && drop.Contains(ei)) continue;
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
                    // radius by class: 15 link / 12 arterial / 9 collector / 6 local (the rule; a measured return replaces it)
                    bool fwd = e.a == n;
                    var arm = new JunctionOutline.Arm { id = ei, oneway = e.oneway, name = e.name ?? "", radius = e.link ? 15f : e.cls >= 3 ? 12f : e.cls >= 1 ? 9f : 6f, Y = new List<float>(), fromA = fwd, edgeLength = e.length };
                    float far = farTrim != null ? Mathf.Max(0f, farTrim(e, fwd ? e.b : e.a)) : 0f;
                    float outTo = Mathf.Min(Mathf.Max(e.length - far, 2f), maxOut);
                    for (float d = 0f; d <= outTo + 1e-3f; d += JunctionOutline.Step)
                    {
                        float s = fwd ? d : e.length - d;
                        var t = e.TangentAt(s); if (!fwd) t = -t;
                        var left = new Vector2(-t.y, t.x);
                        LineModel.Extents(e, s, out float eM, out float eP);
                        var p = e.PointAt(s) - o0;
                        arm.C.Add(p); arm.L.Add(p + left * (fwd ? eP : eM)); arm.R.Add(p - left * (fwd ? eM : eP)); arm.Y.Add(e.YAt(s));
                    }
                    if (arm.C.Count < 4) continue;
                    arms.Add(arm);
                }
        }

        static void Solve(CityMap map, JunctionMeasures.Set set, JunctionMeasures.JunctionM jm, Built b, System.Func<CityMap.Edge, int, float> farTrim)
        {
            BuildCluster(map, b.nodes, jm.drop, farTrim, out var arms, out var links, out b.origin);
            var before = new List<JunctionOutline.Arm>(arms);
            // the rule's own ribbons (before bays merge and measures apply): the lane check's
            var ruleL = new Dictionary<int, List<Vector2>>(); var ruleR = new Dictionary<int, List<Vector2>>();
            foreach (var x in arms) { ruleL[x.id] = new List<Vector2>(x.L); ruleR[x.id] = new List<Vector2>(x.R); }
            b.log.AddRange(JunctionOutline.MergeBays(arms, links));
            foreach (string ln in b.log)
            {
                var mt = System.Text.RegularExpressions.Regex.Match(ln, "^arm ([0-9]+) is a lane of arm ([0-9]+) on its [a-z]+ from ([0-9.]+) m");
                if (mt.Success)
                {
                    b.bayFrom[int.Parse(mt.Groups[1].Value)] = float.Parse(mt.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                    b.bayHost[int.Parse(mt.Groups[1].Value)] = int.Parse(mt.Groups[2].Value);
                }
            }
            // the measures AFTER the bays: a measured border is the pavement's own edge (bays included) and blends back,
            // where the measure ends, to the host's border with its bay
            // (an edge the LINE MODEL carries has the measure in LineModel.Extents already: its ribbon, lanes and paint too)
            foreach (var arm in arms) if (set.edges.TryGetValue(arm.id, out var em) && !JunctionMeasures.Fed.Contains(arm.id)) JunctionMeasures.ApplyToArm(arm, arm.fromA, arm.edgeLength, em);
            var r = b.res = JunctionOutline.Solve(arms, null, links, JunctionMeasures.ToSolver(set, jm, b.origin));
            foreach (var lk in links) if (map.edges[lk.id].a != map.edges[lk.id].b && b.nodes.Contains(map.edges[lk.id].a) && b.nodes.Contains(map.edges[lk.id].b)) b.inside.Add(lk.id);
            if (r.refused != null || r.rowR == null) return;
            float widest = 0f;
            for (int i = 0; i < r.arms.Count; i++)
            {
                var a = r.arms[i];
                b.rowOf[a.id] = Mathf.Abs(r.sR[i] - r.sL[i]) > 0.2f ? Mathf.Min(r.sR[i], r.sL[i]) : 0.5f * (r.sR[i] + r.sL[i]);
                float w = (r.rowL[i] - r.rowR[i]).magnitude;
                if (w > widest) { widest = w; b.mainEdge = a.id; var e = map.edges[a.id]; b.mainNode = b.nodes.Contains(e.a) ? e.a : e.b; }
            }
            // a bay merged into its host: its ribbon ends where its line crosses the host's row
            foreach (var x in before)
            {
                if (arms.Contains(x)) continue;
                float best = float.MaxValue, at = -1f;
                for (int i = 0; i < r.arms.Count; i++)
                {
                    var p = r.rowR[i]; var d = r.rowL[i] - r.rowR[i]; float len = d.magnitude; if (len < 0.1f) continue; d /= len;
                    var nrm = new Vector2(-d.y, d.x);
                    for (int k = 0; k + 1 < x.C.Count; k++)
                    {
                        float s0 = Vector2.Dot(x.C[k] - p, nrm), s1 = Vector2.Dot(x.C[k + 1] - p, nrm);
                        if ((s0 > 0f) == (s1 > 0f)) continue;
                        float t = s0 / (s0 - s1); var q = Vector2.Lerp(x.C[k], x.C[k + 1], t);
                        float along = Vector2.Dot(q - p, d);
                        float off = along < 0f ? -along : along > len ? along - len : 0f;   // how far past the row's ends
                        if (off < best) { best = off; at = (k + t) * JunctionOutline.Step; }
                        break;
                    }
                }
                if (at >= 0f && best < 6f) { b.rowOf[x.id] = at; b.log.Add($"bay e{x.id}'s ribbon ends {at:0.0} m out (at its host's row)"); }
                else { r.refused = $"bay e{x.id}: its line crosses no row of the junction"; return; }
            }
            Carried(map, b);
            // THE LANE CHECK (not a refusal in stage 1: reported): every LANE the line model lays on the junction's edges
            // (the lines traffic, the AI and the paint follow), from each node out to the row, against the solved pavement
            foreach (var x in before) LaneCheck(map, b, x.id, b.rowOf.TryGetValue(x.id, out float st) ? st - 1f : 0f);
            foreach (int ei in b.inside) LaneCheck(map, b, ei, float.MaxValue);
        }

        /// <summary>(round 2) Which lanes of a fitted arm go on across the junction: those that, carried straight on from
        /// the arm's node end, come within half a lane of a lane of its through partner (the arm a through corner joins
        /// it to). A lane with none ends at the row: a turn lane whose exit the map lacks (logged, a MAP cause).</summary>
        static void Carried(CityMap map, Built b)
        {
            var r = b.res; int na = r.arms.Count;
            var la = new List<float>(8); var lb = new List<float>(8);
            for (int i = 0; i < na && na >= 2; i++)
            {
                var c = r.corners[i];
                if (c.shape != "through" && c.shape != "through-taper" && !c.through) continue;
                for (int pass = 0; pass < 2; pass++)
                {
                    var A = map.edges[r.arms[pass == 0 ? i : (i + 1) % na].id]; var B = map.edges[r.arms[pass == 0 ? (i + 1) % na : i].id];
                    if (b.carried.ContainsKey(A.index)) continue;
                    // one NAMED road going on (a split's approach and its slip are a through corner too: the approach's
                    // other lanes go on along a link inside the junction)
                    if (string.IsNullOrEmpty(A.name) || A.name != B.name) continue;
                    float sA = b.nodes.Contains(A.a) ? 0f : A.length, sB = b.nodes.Contains(B.a) ? 0f : B.length;
                    if (!JunctionMeasures.LaneCentres(A, sA, la, out _, out _) || !JunctionMeasures.LaneCentres(B, sB, lb, out _, out _)) continue;
                    var tA = A.TangentAt(sA); var nA = new Vector2(-tA.y, tA.x); var tB = B.TangentAt(sB); var nB = new Vector2(-tB.y, tB.x);
                    var pA = A.PointAt(sA); var pB = B.PointAt(sB);
                    int lo = -1, hi = -1;
                    for (int k = 0; k < la.Count; k++)
                    {
                        float v = Vector2.Dot(pA + nA * la[k] - pB, nB);
                        bool got = false;
                        foreach (float q in lb) got |= Mathf.Abs(q - v) < 1.83f;
                        if (got) { if (lo < 0) lo = k; hi = k; }
                    }
                    if (lo < 0) continue;
                    b.carried[A.index] = (lo, hi);
                    if (lo > 0 || hi < la.Count - 1)
                        b.log.Add($"e{A.index}: lanes {lo + 1}-{hi + 1} of {la.Count} (from the right) go on across to e{B.index} ({lb.Count} lanes); the other{(la.Count - (hi - lo + 1) > 1 ? "s end" : " ends")} at the row (a turn lane whose exit is not in the map)");
                }
            }
        }

        /// <summary>(round 2) Inside a solved junction (between its row and its node), the lanes of a fitted arm that go on
        /// across it (indices from the right); false: not such a place (every lane counts).</summary>
        public static bool CarriedAt(CityMap.Edge e, float s, out int lo, out int hi)
        {
            lo = hi = 0;
            if (all.Count == 0) return false;
            foreach (var b in all)
            {
                if (!b.Solved || !b.carried.TryGetValue(e.index, out var c) || !b.rowOf.TryGetValue(e.index, out float row)) continue;
                float d = b.nodes.Contains(e.a) ? s : e.length - s;
                if (d > row) return false;
                lo = c.lo; hi = c.hi; return true;
            }
            return false;
        }

        /// <summary>(round 2) A BAY'S LANE INSIDE ITS JUNCTION: on its own line (the link through the opening) out to
        /// where it becomes its host's lane, then over BayBlendM onto the host's bay lane (zonePt: the line model's).</summary>
        public const float BayBlendM = 10f;
        public static bool BayLane(CityMap.Edge e, float s, Vector2 zonePt, out Vector2 p)
        {
            p = zonePt;
            if (all.Count == 0) return false;
            foreach (var b in all) if (b.Solved && BayLaneOf(b, e, s, zonePt, out p)) return true;
            return false;
        }
        static bool BayLaneOf(Built b, CityMap.Edge e, float s, Vector2 zonePt, out Vector2 p)
        {
            p = zonePt;
            if (!b.bayFrom.TryGetValue(e.index, out float from) || !b.bayHost.TryGetValue(e.index, out int hi) || forMap == null) return false;
            float d = b.nodes.Contains(e.a) ? s : e.length - s;
            if (!b.rowOf.TryGetValue(e.index, out float row) || d > row + 0.5f) return false;
            // its own line = the MAP's turn-lane way itself (the rule lays the link's lanes off that line, beside where IT
            // put the host's lanes: stale once the host's lanes are fitted to the measured kerbs)
            var own = e.PointAt(s);
            // beside its host: the bay keeps its place against the median (the host's measured edge on the bay's side,
            // less the shoulder and half a lane)
            var H = forMap.edges[hi];
            CityElevation.ProjectOn(H, own, out float sH);
            sH = Mathf.Clamp(sH, 0f, H.length);
            var t = H.TangentAt(sH); var left = new Vector2(-t.y, t.x); var hp = H.PointAt(sH);
            LineModel.Extents(H, sH, out float eM, out float eP);
            bool onLeft = Vector2.Dot(own - hp, left) > 0f;
            var bay = hp + left * (onLeft ? eP - H.shl - 0.5f * RoadProfiles.LaneM : -(eM - H.shr - 0.5f * RoadProfiles.LaneM));
            p = Vector2.Lerp(own, bay, Mathf.SmoothStep(0f, 1f, (d - from) / BayBlendM));
            return true;
        }

        /// <summary>Half a car and a hand: a lane's centre nearer than this to a kerb (or on land) cannot be driven as laid.</summary>
        public const float LaneClearM = 1.1f;
        static readonly List<float> fitLanes = new List<float>(8);
        static void LaneCheck(CityMap map, Built b, int ei, float upTo)
        {
            var e = map.edges[ei];
            bool atA = b.nodes.Contains(e.a), atB = b.nodes.Contains(e.b);
            int lanes = Mathf.Max(1, e.lanes);
            float lw = Mathf.Max(2.5f, (e.width - e.shl - e.shr) / lanes);
            float run = 0f, worst = float.MaxValue; Vector2 at = Vector2.zero; int worstLane = 0;
            for (float d = 0f; d <= Mathf.Min(upTo, e.length) + 1e-3f; d += JunctionOutline.Step)
            {
                float s = atA ? d : e.length - d;   // out from the junction's node (an inside link: its whole length)
                if (atA && atB) s = d;
                var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                bool off = false;
                // (round 2) a fitted edge: the lanes the fit lays
                if (lanes > 1 && JunctionMeasures.LaneCentres(e, s, fitLanes, out _, out _))
                {
                    bool some = b.carried.TryGetValue(ei, out var cr);
                    for (int k = 0; k < fitLanes.Count; k++)
                    {
                        if (some && (k < cr.lo || k > cr.hi)) continue;   // ends at the row: not a lane inside the junction
                        var p = e.PointAt(s) + left * fitLanes[k];
                        float cl = JunctionOutline.Clearance(b.res, p - b.origin);
                        if (cl < worst) { worst = cl; at = p; worstLane = k; }
                        off |= cl < LaneClearM;
                    }
                    if (off) run += JunctionOutline.Step;
                    continue;
                }
                for (int k = 0; k < lanes; k++)
                {
                    // a one-lane edge: the traffic's own point (a bay drives its host's aux lane: LineModel.LanePoint)
                    var p = lanes == 1 ? LineModel.LanePoint(e, s) : e.PointAt(s) + left * (LineModel.LaneCentre(e, s) + (k - 0.5f * (lanes - 1)) * lw);
                    // (round 2) a bay inside this junction: its own line through the opening (this junction is not yet
                    // in the solved list while it is being checked)
                    if (lanes == 1 && BayLaneOf(b, e, s, p, out var bp)) p = bp;
                    float cl = JunctionOutline.Clearance(b.res, p - b.origin);
                    if (cl < worst) { worst = cl; at = p; worstLane = k; }
                    off |= cl < LaneClearM;
                }
                if (off) run += JunctionOutline.Step;
            }
            if (run >= 1f) b.laneConflicts.Add($"e{ei}: {run:0.0} m where a lane's centre is within {LaneClearM} m of a kerb or on land, worst {worst:+0.00;-0.00} m (lane {worstLane + 1} of {lanes} from the right) at ({at.x:0.0}, {at.y:0.0})");
        }
    }
}
