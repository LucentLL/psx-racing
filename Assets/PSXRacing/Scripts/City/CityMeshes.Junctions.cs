using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// JUNCTIONS (roads pass L7, 2026-10-03; plan A10 + A11 + owner Q5 (a);
    /// Docs/CHARLOTTE.md "L7").
    ///
    /// Before: every junction was a fan trimmed just clear of its arms
    /// (0.6 m past the crossing road's edge), so its corners were square -
    /// a right turn swept over the corner lot - and a divided road crossing a
    /// street was drawn as one fan per carriageway with the median crossing a
    /// ribbon between them: the left turn across the median ran over grass
    /// (West 5th Street's WB left onto I-77's ramp, 1.5 m), 893 m2 of grass
    /// inside the OwnerBox's T1 junction boxes.
    ///
    ///   CURB RETURNS  every corner between two arms that are neither going
    ///                 straight through (170 deg or more) nor clipped against
    ///                 each other (a ramp at its host, a fork) is a circular
    ///                 arc tangent to both arms' edge lines, R by the class
    ///                 pair (WP-19's table: local x local 7.5 m, arterial x
    ///                 local 10, arterial x arterial 12, ramp terminal 15).
    ///                 The arms are trimmed back to the arc's tangent point
    ///                 (trim = s + R / tan(phi / 2), s where the two edge
    ///                 lines meet); where the edge is too short for that (it
    ///                 keeps at least 40% of its ribbon, or what it had) the
    ///                 arc is capped by the room: R_eff = tan(phi / 2) x room.
    ///                 Sagitta 6 cm, at most 10 pieces a corner; the verge on
    ///                 each piece meets the next on their shared bisector.
    ///   CLUSTERS      fan nodes joined by an edge the two fans swallow (its
    ///                 ribbon under 0.6 m) or by a median crossing of 30 m or
    ///                 less (A1's CityJunctionClusters rule) are ONE junction:
    ///                 one ring round all their outside arms, triangulated
    ///                 from the members' centroid at their mean height, drawn
    ///                 by the member with the lowest index; the crossing
    ///                 draws no ribbon. Straight across a median opening.
    ///                 A group of more than 8 nodes, over 80 m across, rising
    ///                 more than 2.5 m, or with a road of its own between two
    ///                 members, stays as separate fans (listed).
    ///   CROSSWALKS    (Q5 a) see CitySignals: every arm of a tier-1
    ///                 signalised junction gets a continental crosswalk cut
    ///                 into its ribbon, its stop bar 1.2 m behind it.
    /// PSX_CITY_ARCS=0 / PSX_CITY_CLUSTERS=0 / PSX_CITY_CROSSWALKS=0 measure without each.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool ArcsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_ARCS") != "0";
        public static bool ClustersOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CLUSTERS") != "0";

        /// <summary>A corner's interior angle range that takes a curb return (plan A10).</summary>
        const float CurbMinPhiDeg = 30f, CurbMaxPhiDeg = 170f;
        /// <summary>An arc's largest sagitta between its pieces, and its most pieces.</summary>
        const float CurbSagM = 0.06f;
        const int CurbMaxPieces = 10;
        /// <summary>Below this the corner stays square (the edge lines' meeting point).</summary>
        const float CurbMinR = 0.5f;
        /// <summary>The longest tangent a curb return takes along an arm: at
        /// an acute corner R / tan(phi / 2) runs away (a ramp terminal at 34
        /// deg on Tyvola Road wanted 49 m and trimmed the road back 53-64 m),
        /// so there the radius gives instead.</summary>
        public const float CurbMaxTangentM = 18f;
        /// <summary>An edge keeps at least this share of its length as ribbon
        /// (or what it had) when its two ends grow for their curb returns.</summary>
        const float CurbKeepShare = 0.4f;

        /// <summary>The plan's median-crossing bound (A1's CityJunctionClusters.MedianCrossM).</summary>
        public const float ClusterMedianM = 30f;
        const int ClusterMaxNodes = 8;
        const float ClusterMaxExtentM = 80f, ClusterMaxRiseM = 2.5f;

        /// <summary>One junction drawn as one paved area.</summary>
        public sealed class JunctionCluster
        {
            public int id, owner;
            public int[] nodes;
            public List<int> inner = new List<int>(4);
        }

        /// <summary>The last ComputeTrims' junction tallies (the audit's JUNCTIONS block).</summary>
        public sealed class JunctionTally
        {
            public int medianCrossings, cornersGrown, edgesLimited, groups, built, rejNodes, rejExtent, rejRise, rejRoad;
            public readonly List<string> rejected = new List<string>();
        }
        public static JunctionTally JStats = new JunctionTally();

        /// <summary>WP-19's curb-return radius by the two arms' classes
        /// (SmoothRules.CurbReturn; arterial = secondary and up).</summary>
        public static float CurbRadius(CityMap.Edge a, CityMap.Edge b)
        {
            if (a.link != b.link) return 15f;                     // a ramp terminal
            int art = (a.cls >= 2 ? 1 : 0) + (b.cls >= 2 ? 1 : 0);
            return art == 2 ? 12f : art == 1 ? 10f : 7.5f;
        }

        /// <summary>The nose of a median where one road's two carriageways
        /// part (plan A11: 1.2 m).</summary>
        public const float MedianNoseR = 1.2f;

        static bool SameRoad(CityMap.Edge a, CityMap.Edge b) =>
            !a.link && !b.link && !string.IsNullOrEmpty(a.name) && a.name == b.name;

        /// <summary>The curb return between arm a (meeting the junction at
        /// node na) and arm b (at nb), phi radians apart: the class pair's;
        /// a median's nose where they are one road's two carriageways (one
        /// in, one out); none (0) where they are one road going on through
        /// a split or a widening (over 120 deg), or two ramps.</summary>
        public static float CurbRadiusAt(CityMap.Edge a, int na, CityMap.Edge b, int nb, float phi)
        {
            // two ramps meeting (a fork or a merge of links): the corner is a
            // gore's, not a curb's - a 10 m arc there trimmed node 597's links
            // back 28-33 m and stood the fan's chord over Tyvola Road's drop
            if (a.link && b.link) return 0f;
            if (SameRoad(a, b))
            {
                if (a.oneway && b.oneway && ((a.b == na) != (b.b == nb))) return MedianNoseR;
                if (phi > 120f * Mathf.Deg2Rad) return 0f;
            }
            return CurbRadius(a, b);
        }

        public static string CurbPair(CityMap.Edge a, CityMap.Edge b)
        {
            if (SameRoad(a, b) && a.oneway && b.oneway) return "median_nose";
            if (a.link != b.link) return "ramp_terminal";
            int art = (a.cls >= 2 || a.link ? 1 : 0) + (b.cls >= 2 || b.link ? 1 : 0);
            return art == 2 ? "arterial_x_arterial" : art == 1 ? "arterial_x_local" : "local_x_local";
        }

        // ---- the median crossing (A1's rule, at run time) ---------------------

        static void CarriagewayPairs(CityMap map, int n, CityMap.Edge M, List<(CityMap.Edge a, CityMap.Edge b)> into)
        {
            into.Clear();
            var arms = map.nodeEdges[n];
            for (int i = 0; i < arms.Count; i++)
                for (int j = i + 1; j < arms.Count; j++)
                {
                    var a = map.edges[arms[i]]; var b = map.edges[arms[j]];
                    if (a == M || b == M || !a.oneway || !b.oneway || a.link || b.link || a.a == a.b || b.a == b.b) continue;
                    if (Vector2.Dot(OutDir(a, n), OutDir(b, n)) < -0.85f && ((a.b == n) != (b.b == n))) into.Add((a, b));
                }
        }
        static readonly List<(CityMap.Edge, CityMap.Edge)> cwPa = new List<(CityMap.Edge, CityMap.Edge)>(), cwPb = new List<(CityMap.Edge, CityMap.Edge)>();

        /// <summary>Does edge M cross the median of one divided road (both its
        /// ends between that road's two carriageways, which run opposite
        /// ways)? The run-time copy of CityJunctionClusters.MedianCrossing.</summary>
        public static bool IsMedianCrossing(CityMap map, CityMap.Edge M)
        {
            CarriagewayPairs(map, M.a, M, cwPa);
            if (cwPa.Count == 0) return false;
            CarriagewayPairs(map, M.b, M, cwPb);
            foreach (var p in cwPa)
                foreach (var q in cwPb)
                {
                    if (p.Item1.name != q.Item1.name) continue;
                    var d1 = OutDir(p.Item1.a == M.a ? p.Item1 : p.Item2, M.a);
                    var d2 = OutDir(q.Item1.a == M.b ? q.Item1 : q.Item2, M.b);
                    return Vector2.Dot(d1, d2) <= -0.7f;
                }
            return false;
        }

        /// <summary>The direction an arm leaves its node in, as a chord to
        /// 10 m out (or half the edge): what its edge lines near a curb
        /// return run along.</summary>
        static Vector2 ArmChordDir(CityMap map, CityMap.Edge e, int n)
        {
            float d = Mathf.Min(10f, e.length * 0.49f);
            var p = e.PointAt(e.a == n ? d : e.length - d);
            var v = p - map.nodes[n];
            return v.sqrMagnitude > 1e-4f ? v.normalized : OutDir(e, n);
        }

        /// <summary>
        /// CURB RETURNS, the trims (plan A10): after the fans' own trims and
        /// before the short-edge rescale. Returns each edge's median-crossing
        /// flag (the clusters' inside: no curb return there).
        /// </summary>
        static bool[] JunctionTrims(CityMap map, Trims t)
        {
            int ne = map.edges.Length, nn = map.nodes.Length;
            JStats = new JunctionTally();
            var medianX = new bool[ne];
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                if (e.a == e.b || !t.patch[e.a] || !t.patch[e.b] || e.length > ClusterMedianM) continue;
                if (IsMedianCrossing(map, e)) { medianX[i] = true; JStats.medianCrossings++; }
            }
            if (!ArcsOn) return medianX;
            var baseA = (float[])t.atA.Clone(); var baseB = (float[])t.atB.Clone();
            var arms = new List<(CityMap.Edge e, float ang)>(8);
            float minPhi = CurbMinPhiDeg * Mathf.Deg2Rad, maxPhi = CurbMaxPhiDeg * Mathf.Deg2Rad;
            for (int n = 0; n < nn; n++)
            {
                if (!t.patch[n]) continue;
                arms.Clear();
                int all = 0;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    all++;
                    if (medianX[ei]) continue;
                    var d = ArmChordDir(map, e, n);
                    arms.Add((e, Mathf.Atan2(d.y, d.x)));
                }
                if (all < 3 || arms.Count < 2) continue;
                arms.Sort((x, y) => x.ang != y.ang ? x.ang.CompareTo(y.ang) : x.e.index.CompareTo(y.e.index));
                for (int i = 0; i < arms.Count; i++)
                {
                    var A = arms[i]; var B = arms[(i + 1) % arms.Count];
                    if (A.e == B.e || t.BranchAt(A.e, n) >= 0 || t.BranchAt(B.e, n) >= 0) continue;
                    float phi = Mathf.Repeat(B.ang - A.ang, 2f * Mathf.PI);
                    if (phi < minPhi || phi > maxPhi) continue;
                    // B lies left of A: A's left edge line meets B's right one
                    float eA = ArmExt(A.e, n, true), eB = ArmExt(B.e, n, false);
                    float sin = Mathf.Sin(phi), cos = Mathf.Cos(phi);
                    float sA = (eB + eA * cos) / sin, sB = (eA + eB * cos) / sin;
                    float R = CurbRadiusAt(A.e, n, B.e, n, phi);
                    if (R <= 0f) continue;
                    float L = Mathf.Min(R / Mathf.Tan(phi * 0.5f), CurbMaxTangentM);
                    bool grew = false;
                    void Grow(CityMap.Edge e, float need)
                    {
                        need = Mathf.Min(need, e.length * 0.49f);
                        if (e.a == n) { if (need > t.atA[e.index] + 1e-3f) { t.atA[e.index] = need; grew = true; } }
                        else if (need > t.atB[e.index] + 1e-3f) { t.atB[e.index] = need; grew = true; }
                    }
                    // (+0.15: FanCorners keeps a tenth of a metre straight before the arc)
                    Grow(A.e, sA + L + 0.15f);
                    Grow(B.e, sB + L + 0.15f);
                    if (grew) JStats.cornersGrown++;
                }
            }
            // an edge keeps CurbKeepShare of its length as ribbon (or what it
            // had): two junctions close together share the room, and the arcs
            // are capped by what is left (FanCorners reads the trims)
            for (int i = 0; i < ne; i++)
            {
                float gA = t.atA[i] - baseA[i], gB = t.atB[i] - baseB[i];
                if (gA <= 1e-4f && gB <= 1e-4f) continue;
                gA = Mathf.Max(0f, gA); gB = Mathf.Max(0f, gB);
                float len = map.edges[i].length, b = baseA[i] + baseB[i];
                float ribMin = Mathf.Min(len - b, Mathf.Max(0.6f, CurbKeepShare * len));
                float over = t.atA[i] + t.atB[i] - (len - ribMin);
                if (over <= 0f) continue;
                float k = Mathf.Clamp01(1f - over / Mathf.Max(gA + gB, 1e-4f));
                t.atA[i] = baseA[i] + gA * k; t.atB[i] = baseB[i] + gB * k;
                JStats.edgesLimited++;
            }
            return medianX;
        }

        /// <summary>
        /// JUNCTION CLUSTERS (plan A11 core): after the short-edge rescale.
        /// </summary>
        static void BuildJunctionClusters(CityMap map, Trims t, bool[] medianX)
        {
            int nn = map.nodes.Length, ne = map.edges.Length;
            t.clusterOf = new int[nn];
            for (int i = 0; i < nn; i++) t.clusterOf[i] = -1;
            t.internalEdge = new bool[ne];
            var list = new List<JunctionCluster>();
            t.clusters = list;
            if (!ClustersOn) return;
            var parent = new int[nn];
            for (int i = 0; i < nn; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            bool Inner(CityMap.Edge e) => e.a != e.b && t.patch[e.a] && t.patch[e.b] &&
                                          (e.length - t.atA[e.index] - t.atB[e.index] < 0.6f || (medianX != null && medianX[e.index]));
            foreach (var e in map.edges)
                if (Inner(e)) { int ra = Find(e.a), rb = Find(e.b); if (ra != rb) parent[System.Math.Max(ra, rb)] = System.Math.Min(ra, rb); }
            var groups = new Dictionary<int, List<int>>();
            for (int n = 0; n < nn; n++)
            {
                if (!t.patch[n]) continue;
                int r = Find(n);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(n);
            }
            var keys = new List<int>(groups.Keys);
            keys.Sort();
            var inGroup = new HashSet<int>();
            foreach (int r in keys)
            {
                var g = groups[r];
                if (g.Count < 2) continue;
                JStats.groups++;
                g.Sort();
                string why = null;
                if (g.Count > ClusterMaxNodes) { why = $"{g.Count} nodes"; JStats.rejNodes++; }
                float ext = 0f, yLo = float.MaxValue, yHi = float.MinValue;
                foreach (int a in g)
                {
                    yLo = Mathf.Min(yLo, map.nodeY[a]); yHi = Mathf.Max(yHi, map.nodeY[a]);
                    foreach (int b in g) ext = Mathf.Max(ext, Vector2.Distance(map.nodes[a], map.nodes[b]));
                }
                if (why == null && ext > ClusterMaxExtentM) { why = $"{ext:0} m across"; JStats.rejExtent++; }
                if (why == null && yHi - yLo > ClusterMaxRiseM) { why = $"rises {yHi - yLo:0.0} m"; JStats.rejRise++; }
                inGroup.Clear();
                foreach (int a in g) inGroup.Add(a);
                var inner = new List<int>(4);
                if (why == null)
                    foreach (int a in g)
                        foreach (int ei in map.nodeEdges[a])
                        {
                            var e = map.edges[ei];
                            if (e.a == e.b || e.a != a) continue;          // each edge once, from its a end
                            if (!inGroup.Contains(e.b)) continue;
                            if (!Inner(e)) { why = $"e{ei} '{e.name}' runs between n{e.a} and n{e.b}"; JStats.rejRoad++; break; }
                            inner.Add(ei);
                        }
                if (why != null)
                {
                    if (JStats.rejected.Count < 40) JStats.rejected.Add($"n{g[0]} ({map.nodes[g[0]].x:0},{map.nodes[g[0]].y:0}) {g.Count} nodes: {why}");
                    continue;
                }
                var c = new JunctionCluster { id = list.Count, owner = g[0], nodes = g.ToArray() };
                c.inner.AddRange(inner);
                foreach (int ei in inner)
                {
                    var e = map.edges[ei];
                    t.atA[ei] = t.atB[ei] = e.length * 0.5f;
                    t.internalEdge[ei] = true;
                }
                foreach (int a in g) t.clusterOf[a] = c.id;
                list.Add(c);
                JStats.built++;
            }
        }

        /// <summary>The junction a fan node draws into: its cluster's lowest
        /// member, or itself.</summary>
        public static int FanKey(Trims t, int n)
        {
            var c = t.ClusterOfNode(n);
            return c != null ? c.owner : n;
        }

        /// <summary>Where a fan's triangles meet and at what height: the node,
        /// or a cluster's members' centroid at their mean height.</summary>
        public static void FanCentre(CityMap map, Trims t, int n, out Vector2 c, out float y)
        {
            var cl = t?.ClusterOfNode(n);
            if (cl == null) { c = map.nodes[n]; y = map.nodeY[n]; return; }
            c = Vector2.zero; y = 0f;
            foreach (int m in cl.nodes) { c += map.nodes[m]; y += map.nodeY[m]; }
            c /= cl.nodes.Length; y /= cl.nodes.Length;
        }

        /// <summary>For the audit: each curb-return corner FanCorners decides
        /// (null: not recording).</summary>
        public sealed class CurbRec { public int node, edgeA, edgeB; public float phiDeg, r, rEff, room; public bool arc; }
        public static List<CurbRec> CurbLog;

        static readonly List<(int e, int n)> fanArmIn = new List<(int, int)>(16);

        /// <summary>A cluster's member nodes as interior points of its fan
        /// (plan A11's Steiner points): each at its own height + FanProudM,
        /// relative to <paramref name="origin"/>. Empty for a lone fan. The
        /// centroid alone, at the members' mean, flattened a cluster that
        /// rises across its median: South Boulevard at node 466 stood 0.6 m
        /// under the land graded to its high carriageway.</summary>
        static void FanSteiner(CityMap map, Trims t, int n, Vector3 origin, List<Vector3> into)
        {
            into.Clear();
            var cl = t.ClusterOfNode(n);
            void Put(Vector2 p, float y) => into.Add(new Vector3(p.x - origin.x, y + FanProudM, p.y - origin.z));
            if (cl != null)
            {
                foreach (int m in cl.nodes) Put(map.nodes[m], map.nodeY[m]);
                // the inside edges' own profile
                foreach (int ei in cl.inner)
                {
                    var e = map.edges[ei];
                    if (e.length < 4f) continue;
                    for (int q = 1; q <= 3; q++) { float s = e.length * q * 0.25f; Put(e.PointAt(s), e.YAt(s)); }
                }
            }
            // Each arm's own profile under the fan (roads pass L7): since the
            // curb returns the arms are trimmed 10-20 m back, and a cone from
            // the node to the mouths cut under a crest's profile - the lattice,
            // pinned 10 cm under the road's profile, came within 0.5-8 cm of the
            // fan (Trade x Tryon, 269 m2 of flicker at range). Two points on
            // each arm's line, at its profile's height, carry the fan along it.
            int count = cl != null ? cl.nodes.Length : 1;
            for (int k = 0; k < count; k++)
            {
                int m = cl != null ? cl.nodes[k] : n;
                foreach (int ei in map.nodeEdges[m])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || t.Internal(ei) || t.BranchAt(e, m) >= 0) continue;
                    float trim = t.TrimAt(e, m);
                    if (trim < 5f) continue;
                    for (int q = 0; q < 2; q++)
                    {
                        float d = trim * (q == 0 ? 0.35f : 0.7f);
                        float s = e.a == m ? d : e.length - d;
                        Put(e.PointAt(s), e.YAt(s));
                    }
                }
            }
        }

        static readonly List<Vector2> rfP = new List<Vector2>(128);
        static readonly Dictionary<long, int> rfEdge = new Dictionary<long, int>(256);

        /// <summary>
        /// Inserts the Steiner points (vertex N + 1 + k, after the centre 0 and
        /// the N corners) into a fan's triangles - split in three inside a
        /// triangle, in two across an edge - then flips interior edges to
        /// Delaunay (Lawson), so the surface passes through every member node
        /// at its own height. Triangles stay anticlockwise in map view. A
        /// point outside the ring (or on a corner) is left out.
        /// </summary>
        static void RefineFan(List<FanCorner> corners, Vector2 centre, List<Vector3> steiner, List<int> tris)
        {
            if (steiner == null || steiner.Count == 0 || tris.Count < 3) return;
            int N = corners.Count;
            rfP.Clear();
            rfP.Add(Vector2.zero);
            foreach (var k in corners) rfP.Add(new Vector2(k.pos.x, k.pos.z) - centre);
            foreach (var s in steiner) rfP.Add(new Vector2(s.x, s.z) - centre);
            for (int si = 0; si < steiner.Count; si++)
            {
                int vi = N + 1 + si;
                var q = rfP[vi];
                int hit = -1, opp = 0; float best = float.NegativeInfinity;
                bool near = false;
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector2 a = rfP[tris[t]], b = rfP[tris[t + 1]], c = rfP[tris[t + 2]];
                    if ((a - q).sqrMagnitude < 0.0025f || (b - q).sqrMagnitude < 0.0025f || (c - q).sqrMagnitude < 0.0025f) { near = true; break; }
                    float area = Cross2(b - a, c - a);
                    if (area <= 1e-6f) continue;
                    float w0 = Cross2(b - q, c - q) / area, w1 = Cross2(c - q, a - q) / area, w2 = 1f - w0 - w1;
                    float m = Mathf.Min(w0, Mathf.Min(w1, w2));
                    if (m > best) { best = m; hit = t; opp = w0 <= w1 && w0 <= w2 ? 0 : w1 <= w2 ? 1 : 2; }
                }
                if (near || hit < 0 || best < -1e-4f) continue;
                int A = tris[hit], B = tris[hit + 1], C = tris[hit + 2];
                // the share of the triangle's smallest barycentric, as a distance: on an edge within a centimetre
                if (best > 1e-3f)
                {
                    tris[hit + 2] = vi;
                    tris.Add(B); tris.Add(C); tris.Add(vi);
                    tris.Add(C); tris.Add(A); tris.Add(vi);
                    continue;
                }
                int o = opp == 0 ? A : opp == 1 ? B : C;
                int u = opp == 0 ? B : opp == 1 ? C : A;
                int v = opp == 0 ? C : opp == 1 ? A : B;
                tris[hit] = o; tris[hit + 1] = u; tris[hit + 2] = vi;
                tris.Add(o); tris.Add(vi); tris.Add(v);
                // the triangle across (v, u), if the edge is not the ring's
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        if (tris[t + r] != v || tris[t + (r + 1) % 3] != u) continue;
                        int w = tris[t + (r + 2) % 3];
                        tris[t] = v; tris[t + 1] = vi; tris[t + 2] = w;
                        tris.Add(vi); tris.Add(u); tris.Add(w);
                        t = tris.Count;   // done
                        break;
                    }
                }
            }
            // Lawson: flip each interior edge whose opposite vertex lies inside
            // the other triangle's circumcircle, while the quad stays convex
            for (int flips = 0; flips < 300; flips++)
            {
                rfEdge.Clear();
                for (int t = 0; t + 2 < tris.Count; t += 3)
                    for (int r = 0; r < 3; r++)
                        rfEdge[((long)tris[t + r] << 20) | (long)tris[t + (r + 1) % 3]] = t;
                bool flipped = false;
                for (int t = 0; t + 2 < tris.Count && !flipped; t += 3)
                    for (int r = 0; r < 3; r++)
                    {
                        int u = tris[t + r], v = tris[t + (r + 1) % 3], w1 = tris[t + (r + 2) % 3];
                        if (!rfEdge.TryGetValue(((long)v << 20) | (long)u, out int t2) || t2 == t) continue;
                        int w2 = -1;
                        for (int r2 = 0; r2 < 3; r2++) if (tris[t2 + r2] != u && tris[t2 + r2] != v) w2 = tris[t2 + r2];
                        if (w2 < 0 || w2 == w1) continue;
                        Vector2 pu = rfP[u], pv = rfP[v], p1 = rfP[w1], p2 = rfP[w2];
                        if (InCircle(pu, pv, p1, p2) <= 1e-4f) continue;
                        // the two new triangles (u, w2, w1) and (w2, v, w1) must both stand
                        if (Cross2(p2 - pu, p1 - pu) <= 1e-5f || Cross2(pv - p2, p1 - p2) <= 1e-5f) continue;
                        tris[t] = u; tris[t + 1] = w2; tris[t + 2] = w1;
                        tris[t2] = w2; tris[t2 + 1] = v; tris[t2 + 2] = w1;
                        flipped = true;
                        break;
                    }
                if (!flipped) break;
            }
        }

        /// <summary>Positive where d lies inside the circle through the
        /// anticlockwise triangle a, b, c.</summary>
        static float InCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float adx = a.x - d.x, ady = a.y - d.y, bdx = b.x - d.x, bdy = b.y - d.y, cdx = c.x - d.x, cdy = c.y - d.y;
            return (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy)
                 - (bdx * bdx + bdy * bdy) * (adx * cdy - cdx * ady)
                 + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
        }

        /// <summary>A fan's vertex i in its own frame: 0 the centre, 1..N the
        /// corners, then the Steiner points.</summary>
        static Vector3 FanVertex(int i, Vector3 centre, List<FanCorner> corners, List<Vector3> steiner) =>
            i == 0 ? centre : i <= corners.Count ? corners[i - 1].pos : steiner[i - corners.Count - 1];

        /// <summary>For the JUNCTIONS report: a fan's triangulation checked
        /// against its ring - the ring's area (shoelace), the triangles'
        /// (all anticlockwise: a face-down one counts negative), how many
        /// Steiner points went in, and how far the surface stands off each
        /// member node's own height.</summary>
        public struct FanCheckResult { public float ringArea, triArea, worstNodeDy; public int tris, folded, steiner, steinerUsed; }
        public static FanCheckResult FanCheck(CityMap map, Trims t, int n)
        {
            var r = new FanCheckResult();
            var ring = new List<FanCorner>(16); var tris = new List<int>(64); var st = new List<Vector3>(4);
            FanCorners(map, t, n, Vector3.zero, ring);
            if (ring.Count < 3) return r;
            FanCentre(map, t, n, out var c, out float cy);
            FanTriangles(ring, c, tris);
            FanSteiner(map, t, n, Vector3.zero, st);
            RefineFan(ring, c, st, tris);
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i].pos; var b = ring[(i + 1) % ring.Count].pos;
                r.ringArea += 0.5f * (a.x * b.z - b.x * a.z);
            }
            var centre = new Vector3(c.x, cy + FanProudM, c.y);
            var used = new HashSet<int>();
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 a = FanVertex(tris[i], centre, ring, st), b = FanVertex(tris[i + 1], centre, ring, st), d = FanVertex(tris[i + 2], centre, ring, st);
                float ar = 0.5f * Cross2(new Vector2(b.x - a.x, b.z - a.z), new Vector2(d.x - a.x, d.z - a.z));
                r.triArea += ar; r.tris++;
                if (ar < -1e-4f) r.folded++;
                for (int k = 0; k < 3; k++) if (tris[i + k] > ring.Count) used.Add(tris[i + k]);
            }
            r.steiner = st.Count; r.steinerUsed = used.Count;
            // the surface over each member node against its height
            var cl = t.ClusterOfNode(n);
            if (cl != null)
                foreach (int m in cl.nodes)
                {
                    var q = map.nodes[m];
                    float bestW = float.NegativeInfinity, y = float.NaN;
                    for (int i = 0; i + 2 < tris.Count; i += 3)
                    {
                        Vector3 a = FanVertex(tris[i], centre, ring, st), b = FanVertex(tris[i + 1], centre, ring, st), d = FanVertex(tris[i + 2], centre, ring, st);
                        float area = Cross2(new Vector2(b.x - a.x, b.z - a.z), new Vector2(d.x - a.x, d.z - a.z));
                        if (area <= 1e-6f) continue;
                        var qa = new Vector2(a.x, a.z); var qb = new Vector2(b.x, b.z); var qd = new Vector2(d.x, d.z);
                        float w0 = Cross2(qb - q, qd - q) / area, w1 = Cross2(qd - q, qa - q) / area, w2 = 1f - w0 - w1;
                        float mw = Mathf.Min(w0, Mathf.Min(w1, w2));
                        if (mw > bestW) { bestW = mw; y = w0 * a.y + w1 * b.y + w2 * d.y; }
                    }
                    if (!float.IsNaN(y)) r.worstNodeDy = Mathf.Max(r.worstNodeDy, Mathf.Abs(y - FanProudM - map.nodeY[m]));
                }
            return r;
        }

        // ---- the lattice under the fans -------------------------------------
        /// <summary>How far under a fan's own surface a lattice corner inside
        /// it is held: the corridor's sink plus a margin for the two meshes'
        /// different triangles between the 8 m corners.</summary>
        const float FanFloorM = CityElevation.CorridorSink + 0.04f;
        /// <summary>The most a corner is lowered, and how far inside the
        /// fan's ring it must lie: the lattice past a fan's edge is the
        /// verge's and the ledge audit's, and a deeper dip there showed as a
        /// ledge beside a clipped arm (East 13th Street at North College).</summary>
        const float FanFloorMaxDropM = 0.12f, FanFloorInsetM = 0.75f;
        sealed class FanFloorRec { public Vector3[] tris; public Vector2[] ring; public float x0, z0, x1, z1; }
        static readonly List<FanFloorRec> fanFloors = new List<FanFloorRec>(32);
        static readonly Dictionary<int, FanFloorRec> fanFloorCache = new Dictionary<int, FanFloorRec>(1024);
        static Trims fanFloorTrims;

        /// <summary>
        /// THE LATTICE UNDER THE FANS (roads pass L7). The lattice is pinned
        /// 10 cm under the nearest road's PROFILE; a fan is its own surface (a
        /// cone from the node, through each arm's profile points, to the
        /// mouths and curb returns), and since the curb returns made fans
        /// 10-20 m deep it lay 0.5-8 cm over the lattice in places (Trade x
        /// Tryon: 269 m2, flicker at range) and under it in others (the turning
        /// movements' samples met land first: 160 T1 arcs). Before the ground
        /// is laid, every fan within reach of the tile is triangulated - once
        /// per trims, always here, where the tile's clip table is still empty,
        /// so the answer never depends on which tile asked first - and a
        /// lattice corner at least <see cref="FanFloorInsetM"/> inside one is
        /// held <see cref="FanFloorM"/> under it, lowered by at most
        /// <see cref="FanFloorMaxDropM"/>.
        /// </summary>
        static void PrepareFanFloor(CityMap map, Trims trims, Vector2 min, Vector2 max)
        {
            fanFloors.Clear();
            if (trims == null) return;
            if (fanFloorTrims != trims) { fanFloorCache.Clear(); fanFloorTrims = trims; }
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(min - Vector2.one * 50f, max + Vector2.one * 50f, segs);
            var seen = new HashSet<int>();
            List<FanCorner> ring = null; List<int> tris = null; List<Vector3> st = null;
            bool computed = false;
            foreach (int packed in segs)
            {
                var e = map.edges[packed >> 12];
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? e.a : e.b;
                    if (!trims.patch[n]) continue;
                    int fk = FanKey(trims, n);
                    if (!seen.Add(fk)) continue;
                    if (!fanFloorCache.TryGetValue(fk, out var rec))
                    {
                        if (ring == null) { ring = new List<FanCorner>(32); tris = new List<int>(96); st = new List<Vector3>(16); }
                        computed = true;
                        rec = null;
                        FanCorners(map, trims, fk, Vector3.zero, ring);
                        if (ring.Count >= 3)
                        {
                            FanCentre(map, trims, fk, out var c, out float cy);
                            FanTriangles(ring, c, tris);
                            FanSteiner(map, trims, fk, Vector3.zero, st);
                            RefineFan(ring, c, st, tris);
                            if (tris.Count >= 3)
                            {
                                var centre = new Vector3(c.x, cy + FanProudM, c.y);
                                rec = new FanFloorRec { tris = new Vector3[tris.Count], ring = new Vector2[ring.Count],
                                                        x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue };
                                for (int i = 0; i < tris.Count; i++)
                                {
                                    var v = FanVertex(tris[i], centre, ring, st);
                                    rec.tris[i] = v;
                                    rec.x0 = Mathf.Min(rec.x0, v.x); rec.x1 = Mathf.Max(rec.x1, v.x);
                                    rec.z0 = Mathf.Min(rec.z0, v.z); rec.z1 = Mathf.Max(rec.z1, v.z);
                                }
                                for (int i = 0; i < ring.Count; i++) rec.ring[i] = new Vector2(ring[i].pos.x, ring[i].pos.z);
                            }
                        }
                        fanFloorCache[fk] = rec;
                    }
                    if (rec == null) continue;
                    if (rec.x1 < min.x - 8f || rec.x0 > max.x + 8f || rec.z1 < min.y - 8f || rec.z0 > max.y + 8f) continue;
                    fanFloors.Add(rec);
                }
            }
            // what was sectioned here had no clip table: the build sections again
            if (computed) { ClearSectionCaches(); fanPolys.Clear(); fanStructure.Clear(); }
        }

        /// <summary>A lattice corner's height held under any fan it lies well inside.</summary>
        static float FanFloor(float x, float z, float y)
        {
            float y0 = y;
            var q = new Vector2(x, z);
            foreach (var f in fanFloors)
            {
                if (x < f.x0 + FanFloorInsetM || x > f.x1 - FanFloorInsetM || z < f.z0 + FanFloorInsetM || z > f.z1 - FanFloorInsetM) continue;
                var T = f.tris;
                for (int i = 0; i + 2 < T.Length; i += 3)
                {
                    Vector3 a = T[i], b = T[i + 1], c = T[i + 2];
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-6f) continue;
                    float w1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                    float w2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                    float w3 = 1f - w1 - w2;
                    if (w1 < -1e-4f || w2 < -1e-4f || w3 < -1e-4f) continue;
                    // well inside the ring, not at its edge
                    bool deep = true;
                    var R = f.ring;
                    for (int k = 0; k < R.Length && deep; k++)
                    {
                        Vector2 p0 = R[k], p1 = R[(k + 1) % R.Length], dd = p1 - p0;
                        float L2 = dd.sqrMagnitude;
                        float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - p0, dd) / L2) : 0f;
                        if ((q - (p0 + dd * u)).sqrMagnitude < FanFloorInsetM * FanFloorInsetM) deep = false;
                    }
                    if (!deep) break;
                    float fy = w1 * a.y + w2 * b.y + w3 * c.y - FanFloorM;
                    if (fy < y) y = Mathf.Max(fy, y0 - FanFloorMaxDropM);
                    break;
                }
            }
            return y;
        }

        /// <summary>For the coverage audit: a fan's (or a cluster's, for its
        /// lowest member) whole ring of corners in order, world space; empty
        /// where the node draws none.</summary>
        public static void FanRingPoints(CityMap map, Trims t, int n, List<Vector3> into)
        {
            into.Clear();
            if (!t.patch[n] || FanKey(t, n) != n) return;
            var ring = new List<FanCorner>(16);
            FanCorners(map, t, n, Vector3.zero, ring);
            foreach (var k in ring) into.Add(k.pos);
        }

        /// <summary>A perimeter piece's outward direction at one of its ends:
        /// on a curb return's vertex the mean of the two pieces' (so their
        /// verges share that cross-section - no sliver over, no wedge open),
        /// else the piece's own.</summary>
        static Vector2 RingOut(List<FanCorner> ring, int i, Vector2 own, bool atStart)
        {
            int n = ring.Count;
            var k = ring[i];
            if (!k.arc) return own;
            var o = ring[atStart ? (i + n - 1) % n : (i + 1) % n];
            var d = atStart ? new Vector2(k.pos.x - o.pos.x, k.pos.z - o.pos.z) : new Vector2(o.pos.x - k.pos.x, o.pos.z - k.pos.z);
            float len = d.magnitude;
            if (len < 0.05f) return own;
            var other = new Vector2(d.y, -d.x) / len;
            var m = own + other;
            return m.sqrMagnitude > 1e-4f ? m.normalized : own;
        }
    }
}
