using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// JUNCTIONS (roads pass L7, 2026-10-03; plan A10 + A11 + owner Q5 a):
    /// the curb returns FanCorners draws, the junction clusters drawn as one
    /// paved area, and the crosswalks at tier-1 signalised junctions. Read
    /// off the run-time geometry (no tile builds): a minute's worth of the
    /// AuditOnly. Called at the end of the COVERAGE report, whose cluster-box
    /// holes and the launch audit's TURNING MOVEMENTS are this step's other
    /// numbers.
    /// </summary>
    public static partial class CityAudit
    {
        /// <summary>THE JUNCTIONS REPORT ALONE (roads pass L7): the map, the
        /// trims, <see cref="JunctionsReport"/> on the default box, PAINT, and
        /// the nodes PSX_JUNCTION_NODES lists (comma-separated) described.
        /// Writes junctions.txt beside the project. Headless: -executeMethod
        /// PSXRacing.EditorTools.CityAudit.JunctionsOnly</summary>
        public static void JunctionsOnly()
        {
            outLog = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) Line("charlotte_city.bytes missing from Resources");
            else
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                Line($"trims in {sw.ElapsedMilliseconds} ms");
                JunctionsReport(map, trims, ScopeFor("COVER"));
                PaintReport(map, trims);
                var env = System.Environment.GetEnvironmentVariable("PSX_JUNCTION_NODES") ?? "";
                var envE = System.Environment.GetEnvironmentVariable("PSX_JUNCTION_EDGES");
                if (!string.IsNullOrEmpty(envE))
                    foreach (var tok in envE.Split(','))
                        if (int.TryParse(tok.Trim(), out int ei) && ei >= 0 && ei < map.edges.Length)
                            env += "," + map.edges[ei].a + "," + map.edges[ei].b;
                if (!string.IsNullOrEmpty(env))
                    foreach (var tok in env.Split(','))
                        if (int.TryParse(tok.Trim(), out int n) && n >= 0 && n < map.nodes.Length)
                        {
                            Line(CityMeshes.DescribeNode(map, trims, n));
                            var cl = trims.ClusterOfNode(n);
                            Line(cl == null ? $"  node {n}: no cluster" : $"  node {n}: cluster {cl.id} owner n{cl.owner} nodes {string.Join(",", cl.nodes)} inner e{string.Join(",e", cl.inner)}");
                        }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "junctions.txt"), outLog.ToString());
        }

        static void JunctionsReport(CityMap map, CityMeshes.Trims trims, AuditScope sc)
        {
            var I = CultureInfo.InvariantCulture;
            var js = CityMeshes.JStats;
            Line("");
            Line(string.Format(I, "JUNCTIONS (roads pass L7: curb returns {0}, one paved area per cluster {1}, crosswalks {2}) - {3}",
                CityMeshes.ArcsOn ? "ON" : "OFF (PSX_CITY_ARCS=0)", CityMeshes.ClustersOn ? "ON" : "OFF (PSX_CITY_CLUSTERS=0)",
                CitySignals.CrosswalksOn ? "ON" : "OFF (PSX_CITY_CROSSWALKS=0)", sc.Describe()));
            Line(string.Format(I, "  trims (city-wide): {0} median crossings of 30 m or less between two fans; {1} corners grew their arms for a curb return; {2} edges held to {3:0}% ribbon (two junctions sharing the room)",
                js.medianCrossings, js.cornersGrown, js.edgesLimited, 40));

            // ---- tiers of fans and clusters
            int nn = map.nodes.Length;
            int FanTier(int n)
            {
                int t = 3;
                var cl = trims.ClusterOfNode(n);
                if (cl == null) { foreach (int ei in map.nodeEdges[n]) t = Mathf.Min(t, CityTier.Of(map.edges[ei])); return t; }
                foreach (int m in cl.nodes) foreach (int ei in map.nodeEdges[m]) t = Mathf.Min(t, CityTier.Of(map.edges[ei]));
                return t;
            }

            // ---- CLUSTERS: the run-time set against A1's
            var a1 = CityJunctionClusters.Of(map, trims);
            int[] a1In = new int[4], a1One = new int[4];
            var notOne = new List<string>();
            for (int c = 0; c < a1.members.Length; c++)
            {
                var mem = a1.members[c];
                var p = Vector2.zero;
                foreach (int n in mem) p += map.nodes[n];
                p /= mem.Count;
                if (!sc.Contains(p)) continue;
                int t = a1.tier[c];
                a1In[t]++;
                var rc = trims.ClusterOfNode(mem[0]);
                bool one = rc != null;
                foreach (int n in mem) if (trims.ClusterOfNode(n) != rc) one = false;
                if (one) a1One[t]++;
                else if (notOne.Count < 12) notOne.Add(string.Format(I, "T{0} n{1} ({2:0},{3:0}) {4} nodes{5}", t, mem[0], p.x, p.y, mem.Count, rc == null ? "" : " (part drawn as one)"));
            }
            Line(string.Format(I, "  CLUSTERS (plan A11; city-wide): {0} groups of fan nodes joined by a swallowed edge or a median crossing -> {1} drawn as ONE paved area; left as separate fans: {2} over 8 nodes, {3} over 80 m across, {4} rising over 2.5 m, {5} with a road of their own between members",
                js.groups, js.built, js.rejNodes, js.rejExtent, js.rejRise, js.rejRoad));
            Line(string.Format(I, "  CLUSTERS in scope (A1's set, the COVERAGE cluster boxes): T1 {0} of {1} drawn as one, T2 {2} of {3}, T3 {4} of {5}",
                a1One[1], a1In[1], a1One[2], a1In[2], a1One[3], a1In[3]));
            foreach (var s in notOne) Line("      not one: " + s);
            // each cluster's one surface: its triangles against its ring
            {
                int cl = 0, folded = 0, gaps = 0, stTotal = 0, stUsed = 0; float worstDy = 0f, worstGap = 0f; string worstAt = "-";
                foreach (var c in trims.clusters)
                {
                    CityMeshes.FanCentre(map, trims, c.owner, out var cp, out _);
                    if (!sc.Contains(cp)) continue;
                    var r = CityMeshes.FanCheck(map, trims, c.owner);
                    cl++; folded += r.folded; stTotal += r.steiner; stUsed += r.steinerUsed;
                    float gap = r.ringArea - r.triArea;
                    if (Mathf.Abs(gap) > 0.5f) { gaps++; if (Mathf.Abs(gap) > worstGap) { worstGap = Mathf.Abs(gap); worstAt = string.Format(I, "n{0} ({1:0},{2:0})", c.owner, cp.x, cp.y); } }
                    worstDy = Mathf.Max(worstDy, r.worstNodeDy);
                }
                Line(string.Format(I, "  CLUSTER SURFACES in scope: {0} clusters; triangles folded {1}; ring area not covered by its triangles (over 0.5 m2) {2} (worst {3:0.0} m2 at {4}); interior points (members, inside edges, arms' profiles) in {5} of {6}; surface off a member node's height worst {7:0.000} m",
                    cl, folded, gaps, worstGap, worstAt, stUsed, stTotal, worstDy));
            }
            for (int i = 0; i < js.rejected.Count && i < 6; i++) Line("      left as fans (city-wide): " + js.rejected[i]);

            // ---- CURB RETURNS: every fan (one per cluster) in scope
            var log = new List<CityMeshes.CurbRec>(256);
            int[] fans = new int[4], corners = new int[4], full = new int[4], capped = new int[4], square = new int[4];
            var effs = new List<float>[4]; for (int i = 0; i < 4; i++) effs[i] = new List<float>();
            var cappedList = new List<(float share, string what)>();
            var byPair = new Dictionary<string, int[]>();
            for (int n = 0; n < nn; n++)
            {
                if (!trims.patch[n] || CityMeshes.FanKey(trims, n) != n) continue;
                CityMeshes.FanCentre(map, trims, n, out var cpos, out _);
                if (!sc.Contains(cpos)) continue;
                int t = FanTier(n);
                fans[t]++;
                log.Clear();
                CityMeshes.CurbLog = log;
                try { CityMeshes.DescribeFan(map, trims, n); }
                finally { CityMeshes.CurbLog = null; }
                foreach (var r in log)
                {
                    corners[t]++;
                    string pair = CityMeshes.CurbPair(map.edges[r.edgeA], map.edges[r.edgeB]);
                    if (!byPair.TryGetValue(pair, out var bp)) byPair[pair] = bp = new int[3];
                    if (!r.arc) { square[t]++; bp[2]++; }
                    else if (r.rEff >= 0.9f * r.r) { full[t]++; bp[0]++; }
                    else { capped[t]++; bp[1]++; effs[t].Add(r.rEff); }
                    if (t == 1 && (!r.arc || r.rEff < 0.9f * r.r))
                        cappedList.Add((r.arc ? r.rEff / r.r : 0f, string.Format(I, "n{0} ({1:0},{2:0}) e{3} '{4}' / e{5} '{6}' {7:0} deg: R {8:0.0} -> {9}",
                            n, cpos.x, cpos.y, r.edgeA, map.edges[r.edgeA].name, r.edgeB, map.edges[r.edgeB].name, r.phiDeg, r.r,
                            r.arc ? string.Format(I, "{0:0.0} m (room {1:0.0} m)", r.rEff, r.room) : string.Format(I, "square (room {0:0.0} m)", r.room))));
                }
            }
            string Eff(List<float> l)
            {
                if (l.Count == 0) return "-";
                l.Sort();
                return string.Format(I, "R_eff p50 {0:0.0} m, min {1:0.0} m", l[l.Count / 2], l[0]);
            }
            Line(string.Format(I, "  CURB RETURNS (plan A10, fans and clusters in scope by their best arm's tier; a corner: two arms neither through nor clipped, 30-170 deg): " +
                "T1 {0} fans, {1} corners: {2} full arc (R_eff >= 0.9 R), {3} room-capped ({4}), {5} square | T2 {6} fans, {7} corners: {8} / {9} / {10} | T3 {11} fans, {12} corners: {13} / {14} / {15}",
                fans[1], corners[1], full[1], capped[1], Eff(effs[1]), square[1],
                fans[2], corners[2], full[2], capped[2], square[2], fans[3], corners[3], full[3], capped[3], square[3]));
            var sbp = new StringBuilder("  by class pair (full / capped / square):");
            foreach (var kv in byPair) sbp.Append(string.Format(I, " {0} {1}/{2}/{3};", kv.Key, kv.Value[0], kv.Value[1], kv.Value[2]));
            Line(sbp.ToString());
            cappedList.Sort((x, y) => x.share.CompareTo(y.share));
            for (int i = 0; i < cappedList.Count && i < 8; i++) Line("      T1 short: " + cappedList[i].what);

            // ---- SPLITS (plan A4, measured only in L7): how the split nodes are drawn
            if (map.splt != null)
            {
                int[] sFan = new int[4], sMitre = new int[4], sOther = new int[4], sBig = new int[4];
                int cFan = 0, cAll = 0;
                foreach (var sp in map.splt)
                {
                    int n = sp.node;
                    if (n < 0 || n >= nn) continue;
                    cAll++;
                    if (trims.patch[n]) cFan++;
                    if (!sc.Contains(map.nodes[n])) continue;
                    int t = 3;
                    foreach (int ei in map.nodeEdges[n]) t = Mathf.Min(t, CityTier.Of(map.edges[ei]));
                    if (trims.patch[n])
                    {
                        sFan[t]++;
                        foreach (int ei in map.nodeEdges[n]) if (trims.TrimAt(map.edges[ei], n) > 15f) { sBig[t]++; break; }
                    }
                    else if (trims.mitre[n]) sMitre[t]++; else sOther[t]++;
                }
                Line(string.Format(I, "  SPLITS (plan A4, measured; not changed in L7): city-wide {0} split nodes, {1} drawn as fans; in scope fans/mitres/other T1 {2}/{3}/{4} (fans with a trim over 15 m {5}), T2 {6}/{7}/{8} ({9}), T3 {10}/{11}/{12} ({13})",
                    cAll, cFan, sFan[1], sMitre[1], sOther[1], sBig[1], sFan[2], sMitre[2], sOther[2], sBig[2], sFan[3], sMitre[3], sOther[3], sBig[3]));
            }

            // ---- CROSSWALKS
            CityMeshes.EnsureMarks(map, trims);
            var cws = CitySignals.Crosswalks(map, trims);
            int cwIn = 0; var jIn = new HashSet<int>();
            foreach (var c in cws)
            {
                if (!sc.Contains(map.nodes[c.node])) continue;
                cwIn++; jIn.Add(c.junction);
            }
            int sigT1In = 0;
            foreach (var j in CitySignals.All(map, trims))
            {
                if (!j.signal || !sc.Contains(j.centre)) continue;
                int t = 3;
                foreach (int n in j.nodes) foreach (int ei in map.nodeEdges[n]) t = Mathf.Min(t, CityTier.Of(map.edges[ei]));
                if (t == 1) sigT1In++;
            }
            var ms = CityMeshes.MarkStats;
            Line(string.Format(I, "  CROSSWALKS (Q5 a; continental, cut into the ribbon - lift 0, no new draw): city-wide {0} on {1} tier-1 signalised junctions ({2} bars; {3} arms with no room or clipped, {4} too narrow); in scope {5} on {6} of {7} tier-1 signalised junctions",
                cws.Count, CitySignals.CrosswalkJunctions, ms.crosswalkBars, CitySignals.CrosswalkArmsSkipped, ms.crosswalksSkipped, cwIn, jIn.Count, sigT1In));
        }
    }
}
