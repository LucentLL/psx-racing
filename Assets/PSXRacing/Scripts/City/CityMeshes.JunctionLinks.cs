using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        /// <summary>
        /// A SHORT LINK INSIDE A JUNCTION COMPLEX CARRIES NO LINES (Five Points
        /// round 4, 2026-11-03).
        ///
        /// Where a two-way road crosses a divided road, OSM draws its crossing
        /// of the median as a short two-way piece between the two carriageways'
        /// nodes. That piece lies INSIDE the intersection: MUTCD carries no
        /// centre line or edge line through an intersection (3B.01/3B.06; a
        /// divided road's crossing with a median under 30 ft is one
        /// intersection), so the crossing road's lines stop at the first
        /// carriageway and start again past the second. Five Points: W 5th /
        /// Rozzelles' link e9645 + e9644 (10.3 m, n10969 -> n10968 -> n10966)
        /// across the Beatties Ford / West Trade median drew its double yellow
        /// and its edge lines over the median opening, and the fan of n10966
        /// carried them on across the northbound carriageway's through lanes
        /// (the straightest pair of arms wins the fan's through lines, and W 5th
        /// is straighter than BF NB -> Trade NB): the owner's row-2 view.
        ///
        /// A JUNCTION-COMPLEX LINK: a two-way, non-link edge whose run through
        /// 2-arm nodes reaches a junction node (3+ arms) at both ends within
        /// <see cref="JclMaxM"/>, where both junctions take a one-way
        /// carriageway of the SAME named road (the divided road it crosses).
        /// Its ribbon is plain pavement; it is never one of a fan's through
        /// pair (the divided road's carriageway keeps its lines across); and
        /// it opens no dotted mouth on the carriageways (their lines stop).
        /// PSX_CITY_JCL=0: drawn as before.
        /// </summary>
        public static bool JclOn = System.Environment.GetEnvironmentVariable("PSX_CITY_JCL") != "0";
        /// <summary>The longest median crossing treated as part of its junction.</summary>
        public const float JclMaxM = 25f;
        static bool[] jcl;
        static CityMap jclMap;
        /// <summary>For the audit: links found, their metres, and where.</summary>
        public static int JclCount; public static float JclMetres;
        public static readonly List<string> JclAt = new List<string>();

        /// <summary>Is <paramref name="ei"/> a short link inside a junction complex?</summary>
        public static bool IsJcl(CityMap map, int ei)
        {
            if (!JclOn || map == null || ei < 0) return false;
            if (jclMap != map || jcl == null || jcl.Length != map.edges.Length) BuildJcl(map);
            return ei < jcl.Length && jcl[ei];
        }

        /// <summary>The same, for the map the marks were last built for
        /// (<see cref="EnsureJcl"/> runs with them, before any ribbon).</summary>
        public static bool IsJcl(CityMap.Edge e) => JclOn && jcl != null && e != null && e.index >= 0 && e.index < jcl.Length && jcl[e.index];

        public static void EnsureJcl(CityMap map)
        {
            if (map != null && (jclMap != map || jcl == null || jcl.Length != map.edges.Length)) BuildJcl(map);
        }

        static void BuildJcl(CityMap map)
        {
            jclMap = map;
            jcl = new bool[map.edges.Length];
            JclCount = 0; JclMetres = 0f; JclAt.Clear();
            var run = new List<int>(8);
            var seen = new bool[map.edges.Length];
            for (int i = 0; i < map.edges.Length; i++)
            {
                var e = map.edges[i];
                if (seen[i] || !Candidate(e)) continue;
                // the run through 2-arm nodes, both ways
                run.Clear(); run.Add(i); seen[i] = true;
                float len = e.length;
                int endA = Walk(map, e, e.a, run, seen, ref len), endB = Walk(map, e, e.b, run, seen, ref len);
                if (endA < 0 || endB < 0 || endA == endB || len > JclMaxM) continue;
                if (!SharesDividedRoad(map, endA, endB, out string road)) continue;
                foreach (int r in run) jcl[r] = true;
                JclCount++; JclMetres += len;
                if (JclAt.Count < 400)
                    JclAt.Add($"n{endA}-n{endB} ({map.nodes[endA].x:0},{map.nodes[endA].y:0}) {len:0.0} m '{e.name}' across '{road}': e{string.Join(" e", run)}");
            }
        }

        static bool Candidate(CityMap.Edge e) => e.a != e.b && !e.oneway && !e.link && !e.roundabout && !e.tunnel;

        /// <summary>From edge <paramref name="e"/> out through <paramref name="node"/>
        /// along 2-arm nodes: the junction node reached (3+ arms), or -1 (a dead
        /// end, a link that is not a candidate, or too long).</summary>
        static int Walk(CityMap map, CityMap.Edge e, int node, List<int> run, bool[] seen, ref float len)
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var list = map.nodeEdges[node];
                if (list.Count >= 3) return node;
                if (list.Count < 2) return -1;
                int oi = list[0] == e.index ? list[1] : list[0];
                var o = map.edges[oi];
                if (o == e || !Candidate(o)) return -1;
                len += o.length;
                if (len > JclMaxM) return -1;
                run.Add(oi); seen[oi] = true;
                node = o.a == node ? o.b : o.a;
                e = o;
            }
            return -1;
        }

        /// <summary>Both junctions take a one-way, non-link carriageway of one
        /// named road: the crossing is of that divided road's median.</summary>
        static bool SharesDividedRoad(CityMap map, int nA, int nB, out string road)
        {
            road = null;
            foreach (int ai in map.nodeEdges[nA])
            {
                var a = map.edges[ai];
                if (!a.oneway || a.link || string.IsNullOrEmpty(a.name)) continue;
                foreach (int bi in map.nodeEdges[nB])
                {
                    var b = map.edges[bi];
                    if (b == a || !b.oneway || b.link || b.name != a.name) continue;
                    road = a.name;
                    return true;
                }
            }
            return false;
        }
    }
}
