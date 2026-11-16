using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CONTROL ROUND'S GATES (2026-11-16, PSX_CITY_MITREWARRANT; part of
    /// CityAudit.RunControl): the named side streets at mitred nodes and what
    /// controls each, the 3-arm carriageway splits (uncontrolled), the stop
    /// lines of arms clipped onto a host (clear of its pavement), and a plan
    /// of one box's roads and controls for a picture.
    ///   PSX_CTL_MAPBOX="x0,z0,x1,z1"  -> city_control_map.tsv (E edges, N nodes, J junctions,
    ///                                    A approaches, O the OSM control nodes of PSX_CTL_OSM)
    /// </summary>
    public static partial class CityAudit
    {
        static void ControlMitreCensus(CityMap map, CityMeshes.Trims trims, IReadOnlyList<CitySignals.Junction> all, Dictionary<int, int> nodeJ)
        {
            var inv = CultureInfo.InvariantCulture;
            bool Usable(CityMap.Edge e) => e.a != e.b && e.length > 0.5f && !(e.hasLset && (e.lsSub >= 5 || (e.lsFlags & 32) != 0));
            // ---- the investigation's census (align verify-citycode s10_mitreside.mjs): a mitred node with 3+ usable
            // arms and no freeway; a SIDE arm is not the meshes' through pair, not a link, named, not the through name
            int mNodes = 0, side = 0, cStop = 0, cYield = 0, cSig = 0, cNone = 0, cExit = 0;
            var why = new SortedDictionary<string, int>();
            var rows = new List<string>();
            var ctlRows = new List<string>();
            var chain = new HashSet<int>();
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.mitre[n]) continue;
                int arms = 0; bool fwy = false;
                foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; if (!Usable(e)) continue; arms++; if (e.cls >= 5 && !e.link) fwy = true; }
                if (arms < 3 || fwy) continue;
                int tA = trims.throughA[n], tB = trims.throughB[n];
                if (tA < 0 || tB < 0) continue;
                string nA = map.edges[tA].name, nB = map.edges[tB].name;
                bool any = false;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (!Usable(e) || ei == tA || ei == tB || e.link || string.IsNullOrEmpty(e.name) || e.name == nA || e.name == nB) continue;
                    any = true; side++;
                    // the arm's chain out through joints (a line may stand behind one)
                    chain.Clear(); chain.Add(ei);
                    { var cur = e; int at = n; float len = 0f;
                      for (int hop = 0; hop < 4; hop++)
                      {
                          int far = cur.a == at ? cur.b : cur.a; len += cur.length;
                          if (len > CitySignals.MitreReachM + 5f || map.nodeEdges[far].Count != 2) break;
                          int next = -1; foreach (int oi in map.nodeEdges[far]) if (oi != cur.index) next = oi;
                          if (next < 0) break;
                          chain.Add(next); cur = map.edges[next]; at = far;
                      } }
                    string status = null;
                    if (nodeJ.TryGetValue(n, out int ji))
                        foreach (var a in all[ji].approaches)
                            if (chain.Contains(a.edge)) { status = all[ji].signal ? "signal" : a.ctl == 1 ? "YIELD" : "STOP"; break; }
                    string ll = LatLon(map.nodes[n].x, map.nodes[n].y);
                    if (status != null)
                    {
                        if (status == "signal") cSig++; else if (status == "YIELD") cYield++; else cStop++;
                        string w = CitySignals.MitreWhy.TryGetValue(((long)n << 32) | (uint)ei, out string ww) ? ww : (nodeJ.ContainsKey(n) && all[nodeJ[n]].warrant ? "by warrant" : "OSM tag");
                        ctlRows.Add($"n{n} {ll} ({map.nodes[n].x:0},{map.nodes[n].y:0}) e{ei} '{e.name}' meets '{nA}'/'{nB}': {status} ({w})");
                        continue;
                    }
                    string reason;
                    if (e.oneway && e.a == n) { reason = "a one-way exit: no approach"; cExit++; }
                    else if (CitySignals.MitreWhy.TryGetValue(((long)n << 32) | (uint)ei, out string mw)) reason = mw;
                    else if (nodeJ.TryGetValue(n, out int jj)) reason = (all[jj].signal ? "in a signal junction with no approach for it" : "the node's OSM-tag junction has no approach for it") + " (" + CitySignals.WhyNoLine(map, trims, e, n) + ")";
                    else if (!CitySignals.MitreWarrantOn) reason = "no warrant at mitred nodes (PC45 rule)";
                    else if (CitySignals.MitreNodeWhy.TryGetValue(n, out string nw)) reason = nw;
                    else reason = "not reached by the warrant (unknown)";
                    cNone++;
                    why[reason] = why.TryGetValue(reason, out int c) ? c + 1 : 1;
                    rows.Add($"n{n} {ll} ({map.nodes[n].x:0},{map.nodes[n].y:0}) e{ei} '{e.name}' meets '{nA}'/'{nB}'{(e.oneway ? " one-way" : "")}: {reason}");
                }
                if (any) mNodes++;
            }
            int exempt = 0;
            foreach (var kv in why) if (kv.Key.StartsWith("a one-way exit")) exempt += kv.Value;
            Line($"  MITRED SIDE STREETS (named, not a link, not the through name; mitred node, 3+ arms, no freeway): {side} arms at {mNodes} nodes; STOP {cStop}, YIELD {cYield}, at a signal {cSig}; NO CONTROL {cNone} ({exempt} of them one-way exits, {cNone - exempt} others)");
            foreach (var kv in why) Line($"    no control because {kv.Key}: {kv.Value}");
            foreach (var r in rows) outLog.AppendLine("    MITRE-NOCTL " + r);
            foreach (var r in ctlRows) outLog.AppendLine("    MITRE-CTL " + r);

            // ---- 3-arm carriageway splits stay uncontrolled
            int splits = 0, splitsCtl = 0;
            var splitRows = new List<string>();
            for (int n = 0; n < map.nodes.Length; n++)
            {
                int arms = 0;
                foreach (int ei in map.nodeEdges[n]) if (Usable(map.edges[ei])) arms++;
                if (arms != 3 || !CitySignals.SplitArms(map, n, out _, out _, out _)) continue;
                splits++;
                if (!nodeJ.TryGetValue(n, out int ji)) continue;
                splitsCtl++;
                if (splitRows.Count < 80) splitRows.Add($"n{n} {LatLon(map.nodes[n].x, map.nodes[n].y)} [{CtlNames(map, new[] { n })}]: {CtlKind(all[ji])}{(all[ji].warrant ? " (default)" : "")}");
            }
            Line($"  3-ARM CARRIAGEWAY SPLITS: {splits}; with a control {splitsCtl} (a signal or OSM tag of a junction beside it, or a fault)");
            foreach (var r in splitRows) outLog.AppendLine("    SPLIT-CTL " + r);
            foreach (int n in new[] { 10238, 10236, 10035, 10229, 10234 })
                if (n < map.nodes.Length) Line($"    split n{n} [{CtlNames(map, new[] { n })}]: {(nodeJ.TryGetValue(n, out int ji) ? CtlKind(all[ji]) + " (junction " + ji + ")" : "uncontrolled")}");

            // ---- stop lines of arms clipped onto a host at a mitred node: clear of the host's pavement?
            int clipApps = 0, clipOnHost = 0, clipSig = 0, clipSigOnHost = 0;
            var hostRows = new List<string>();
            foreach (var j in all)
                foreach (var a in j.approaches)
                {
                    var e = map.edges[a.edge];
                    if (!trims.mitre[a.node] || trims.BranchAt(e, a.node) < 0) continue;
                    if (j.signal) { clipSig++; if (!CitySignals.ApproachBarClear(map, a)) clipSigOnHost++; continue; }
                    clipApps++;
                    if (CitySignals.ApproachBarClear(map, a)) continue;
                    clipOnHost++;
                    if (hostRows.Count < 80) hostRows.Add($"n{a.node} {LatLon(map.nodes[a.node].x, map.nodes[a.node].y)} e{a.edge} '{e.name}' ({CtlKind(j)}{(j.warrant ? ", default" : "")}): the bar {(e.b == a.node ? e.length - a.sStop : a.sStop):0.0} m from the node lies on another road's pavement");
                }
            Line($"  STOP LINES OF CLIPPED ARMS at mitred nodes (STOP / yield): {clipApps}; the bar on another road's pavement {clipOnHost}. Signal approaches of clipped arms (the signals round's lines, untouched): {clipSig}, on another road's pavement {clipSigOnHost}");
            foreach (var r in hostRows) outLog.AppendLine("    BAR-ON-HOST " + r);

            // ---- a plan of one box's roads and controls (the picture)
            string box = System.Environment.GetEnvironmentVariable("PSX_CTL_MAPBOX");
            if (string.IsNullOrWhiteSpace(box)) return;
            var q = box.Split(',');
            if (q.Length != 4) return;
            float x0 = float.Parse(q[0], inv), z0 = float.Parse(q[1], inv), x1 = float.Parse(q[2], inv), z1 = float.Parse(q[3], inv);
            var sb = new StringBuilder();
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(new Vector2(x0, z0), new Vector2(x1, z1), segs);
            var edges = new SortedSet<int>();
            foreach (int pk in segs) edges.Add(pk >> 12);
            var nodesIn = new SortedSet<int>();
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                if (e.a == e.b) continue;
                var pts = new StringBuilder();
                for (int i = 0; i < e.pts.Length; i++) pts.Append(string.Format(inv, "{0:0.0},{1:0.0};", e.pts[i].x, e.pts[i].y));
                LineModel.Extents(e, e.length * 0.5f, out float eM, out float eP);
                sb.Append(string.Format(inv, "E\t{0}\t{1}\t{2}\t{3}\t{4}\t{5:0.00}\t{6:0.00}\t{7}\t{8}\t{9}\n", ei, e.name, e.cls, e.oneway ? 1 : 0, e.link ? 1 : 0, eM, eP, e.a, e.b, pts));
                nodesIn.Add(e.a); nodesIn.Add(e.b);
            }
            foreach (int n in nodesIn)
                sb.Append(string.Format(inv, "N\t{0}\t{1:0.0}\t{2:0.0}\t{3}\t{4}\t{5}\n", n, map.nodes[n].x, map.nodes[n].y, map.nodeEdges[n].Count, trims.mitre[n] ? 1 : 0, trims.patch[n] ? 1 : 0));
            for (int i = 0; i < all.Count; i++)
            {
                var j = all[i];
                if (j.centre.x < x0 - 60f || j.centre.x > x1 + 60f || j.centre.y < z0 - 60f || j.centre.y > z1 + 60f) continue;
                sb.Append(string.Format(inv, "J\t{0}\t{1}\t{2}\t{3:0.0}\t{4:0.0}\t{5}\n", i, CtlKind(j), j.warrant ? 1 : 0, j.centre.x, j.centre.y, string.Join(",", j.nodes)));
                foreach (var a in j.approaches)
                    sb.Append(string.Format(inv, "A\t{0}\t{1}\t{2}\t{3}\t{4:0.00}\t{5:0.00}\t{6:0.000}\t{7:0.000}\t{8:0.00}\t{9:0.00}\n", i, a.edge, a.node, j.signal ? 4 : a.ctl, a.at.x, a.at.y, a.u.x, a.u.y, a.inL, a.inR));
            }
            string f = System.Environment.GetEnvironmentVariable("PSX_CTL_OSM");
            if (!string.IsNullOrWhiteSpace(f) && File.Exists(f))
            {
                var tagIds = new HashSet<ulong>();
                foreach (var t in map.tagged) tagIds.Add(t.nodeId);
                foreach (var row in File.ReadAllLines(f))
                {
                    var p = row.Split('\t');
                    if (p.Length < 8 || !float.TryParse(p[1], NumberStyles.Float, inv, out float x) || !float.TryParse(p[2], NumberStyles.Float, inv, out float z)) continue;
                    if (x < x0 || x > x1 || z < z0 || z > z1) continue;
                    bool ing = ulong.TryParse(p[7], out ulong id) && tagIds.Contains(id);
                    sb.Append($"O\t{p[0]}\t{p[5]}\t{p[7]}\t{p[1]}\t{p[2]}\t{(ing ? 1 : 0)}\t{p[6]}\n");
                }
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "city_control_map.tsv"), sb.ToString());
            Line($"  control map of the box {box}: {edges.Count} edges, {nodesIn.Count} nodes -> city_control_map.tsv");
        }
    }
}
