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
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_DUMP_BOX"))) DumpRoads();
        }

        /// <summary>INTERNAL (leftover item 3): the built road meshes of the
        /// tiles under PSX_DUMP_BOX=x0,z0,x1,z1, triangle by triangle with
        /// their slot, to roads_dump.txt beside the project - a plan of what
        /// is drawn under a deck that no camera over it can see. Headless:
        /// -executeMethod PSXRacing.EditorTools.CityAudit.DumpRoads</summary>
        public static void DumpRoads()
        {
            var map = CityMap.Get();
            var inv = CultureInfo.InvariantCulture;
            var boxes = (System.Environment.GetEnvironmentVariable("PSX_DUMP_BOX") ?? "").Split(';');
            if (map == null) { Debug.LogError("[CityAudit] DumpRoads: no map"); return; }
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var sb = new StringBuilder();
            sb.AppendLine($"# crossings {CityMeshes.CrossingNodes}");
            foreach (var boxStr in boxes)
            {
            var box = boxStr.Split(',');
            if (box.Length != 4) continue;
            float x0 = float.Parse(box[0], inv), z0 = float.Parse(box[1], inv), x1 = float.Parse(box[2], inv), z1 = float.Parse(box[3], inv);
            sb.AppendLine($"# box {boxStr}");
            for (int tz = Mathf.FloorToInt(z0 / CityMeshes.TileSize); tz <= Mathf.FloorToInt(z1 / CityMeshes.TileSize); tz++)
                for (int tx = Mathf.FloorToInt(x0 / CityMeshes.TileSize); tx <= Mathf.FloorToInt(x1 / CityMeshes.TileSize); tx++)
                {
                    var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                    foreach (var (mesh, slots, kind) in new[] { (tm.roads, tm.roadSlots, "R"), (tm.barriers, new[] { CityMeshes.Slot.Concrete }, "B") })
                    {
                        if (mesh == null) continue;
                        var v = mesh.vertices;
                        for (int sm = 0; sm < mesh.subMeshCount && sm < slots.Length; sm++)
                        {
                            var tri = mesh.GetTriangles(sm);
                            sb.Append(kind).Append(' ').Append((int)slots[sm]).Append('\n');
                            for (int i = 0; i + 2 < tri.Length; i += 3)
                            {
                                var a = v[tri[i]] + tm.origin; var b = v[tri[i + 1]] + tm.origin; var c = v[tri[i + 2]] + tm.origin;
                                if (Mathf.Max(a.x, b.x, c.x) < x0 || Mathf.Min(a.x, b.x, c.x) > x1 || Mathf.Max(a.z, b.z, c.z) < z0 || Mathf.Min(a.z, b.z, c.z) > z1) continue;
                                sb.Append(string.Format(inv, "T {0:0.00} {1:0.00} {2:0.00} {3:0.00} {4:0.00} {5:0.00} {6:0.00} {7:0.00} {8:0.00}\n", a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z));
                            }
                        }
                    }
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "roads_dump.txt"), sb.ToString());
            Debug.Log("[CityAudit] DumpRoads wrote roads_dump.txt");
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

            // ---- JUNCTION SURFACE (leftover item 3): every junction in its main road's surface
            {
                int[] js3 = new int[4], diff = new int[4], partnerDiff = new int[4], sideArms = new int[4], sideDiff = new int[4], hashDiff = new int[4], concrete = new int[4];
                var worst = new List<string>();
                for (int n = 0; n < nn; n++)
                {
                    if (!trims.patch[n] || CityMeshes.FanKey(trims, n) != n || trims.fanMain == null || trims.fanMain[n] < 0) continue;
                    CityMeshes.FanCentre(map, trims, n, out var cpos, out _);
                    if (!sc.Contains(cpos)) continue;
                    int t = FanTier(n);
                    js3[t]++;
                    var drawn = CityMeshes.FanSurface(map, trims, n);
                    var me = map.edges[trims.fanMain[n]];
                    var mainS = CityMeshes.ArmSurface(map, trims, me, trims.fanMainNode[n]);
                    var hash = CityMeshes.IsFresh(map.nodes[n]) ? CityMeshes.Surface.AsphaltNew : CityMeshes.Surface.AsphaltOld;
                    if (drawn == CityMeshes.Surface.ConcreteNew || drawn == CityMeshes.Surface.ConcreteOld) concrete[t]++;
                    if (hash != mainS) hashDiff[t]++;
                    if (drawn != mainS)
                    {
                        diff[t]++;
                        if (t <= 2 && worst.Count < 6) worst.Add(string.Format(I, "T{0} n{1} ({2:0},{3:0}) drawn {4}, main e{5} '{6}' {7}", t, n, cpos.x, cpos.y, drawn, me.index, me.name, mainS));
                    }
                    var cl = trims.ClusterOfNode(n);
                    int count = cl != null ? cl.nodes.Length : 1;
                    for (int k = 0; k < count; k++)
                    {
                        int m = cl != null ? cl.nodes[k] : n;
                        foreach (int ei in map.nodeEdges[m])
                        {
                            var e = map.edges[ei];
                            if (e.a == e.b || trims.Internal(ei) || ei == me.index) continue;
                            var s = CityMeshes.ArmSurface(map, trims, e, m);
                            if (ei == trims.fanPartner[n]) { if (s != drawn) partnerDiff[t]++; continue; }
                            sideArms[t]++;
                            if (s != drawn) sideDiff[t]++;
                        }
                    }
                }
                double kmNew = 0, kmAll = 0;
                foreach (var e in map.edges)
                {
                    if (e.a == e.b || !e.hasAgeSeed) continue;
                    kmAll += e.length;
                    if (CityMeshes.IsFresh(e.ageSeed)) kmNew += e.length;
                }
                Line(string.Format(I, "  CROSSINGS (leftover item 3: {0}): {1} nodes city-wide where a road crosses another at a skew are drawn as junctions (they were a merge and a diverge, each half clipped against the through road)",
                    CityMeshes.CrossingsOn ? "ON" : "OFF (PSX_CITY_CROSSINGS=0)", CityMeshes.CrossingNodes));
                Line(string.Format(I, "  JUNCTION SURFACE (leftover item 3: {0}): junctions in scope T1 / T2 / T3 {1} / {2} / {3}; drawn in another surface than their main arm {4} / {5} / {6} (aged from the node, the old rule: {7} / {8} / {9}); the main road's other arm in another surface {10} / {11} / {12}; side arms meeting at a seam (another surface) {13} of {14} / {15} of {16} / {17} of {18}; junctions in concrete (main arm on structure) {19} / {20} / {21}; road pairs joined through junctions (city-wide) {22} of {24} candidates (a group at most 2 km); new asphalt/concrete share of road length {23:0.0}%",
                    CityMeshes.FanSurfaceOn ? "ON, the main road's surface" : "OFF (PSX_CITY_FANSURF=0), aged from the node",
                    js3[1], js3[2], js3[3], diff[1], diff[2], diff[3], hashDiff[1], hashDiff[2], hashDiff[3],
                    partnerDiff[1], partnerDiff[2], partnerDiff[3], sideDiff[1], sideArms[1], sideDiff[2], sideArms[2], sideDiff[3], sideArms[3],
                    concrete[1], concrete[2], concrete[3], CityMeshes.FanJoinsMade, kmAll > 0 ? 100.0 * kmNew / kmAll : 0.0, CityMeshes.FanJoins.Count));
                foreach (var w in worst) Line("      off its main arm: " + w);
                Check(diff[1] + diff[2] == 0, "every T1/T2 junction in scope is paved in its main road's surface, material and age (leftover item 3)",
                    string.Format(I, "{0} of {1} differ", diff[1] + diff[2], js3[1] + js3[2]));
            }
        }
    }
}
