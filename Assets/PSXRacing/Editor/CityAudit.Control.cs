using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE JUNCTION CONTROL AUDIT (2026-11-15, round 2: "make every junction
    /// controlled the way the real one is today"). Data level, city-wide, a
    /// minute: what CitySignals decided at every junction against the OSM
    /// control nodes of the extract, both ways.
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunControl
    ///   PSX_CTL_OSM=file   rows "kind x z lat lon type dir id" (tab separated; kind 4 signal,
    ///                      2 stop, 1 give way; type = traffic_signals / stop value; the
    ///                      round's ctl_nodes.mjs writes it from tools/city/cache/nodes_all.json)
    ///   PSX_CTL_NODES=n,n  the junction at each of these nodes, approach by approach
    ///   -> city_control.txt (the counts, SIGNAL-MISSING / SIGNAL-FALSE / STOP-MISSING lines,
    ///      the clusters refused) and city_control.tsv (every junction: kind, nodes, approaches)
    /// </summary>
    public static partial class CityAudit
    {
        public static void RunControl()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); FinishTo("city_control.txt"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
            var trims = CityMeshes.NodeTrims(map);
            ControlCensus(map, trims);
            FinishTo("city_control.txt");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static string CtlNames(CityMap map, IEnumerable<int> ns)
        {
            var names = new SortedSet<string>();
            foreach (int n in ns)
                foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; names.Add(string.IsNullOrEmpty(e.name) ? (e.link ? "(ramp)" : "(unnamed)") : e.name); }
            return string.Join(" x ", names);
        }

        static string CtlKind(CitySignals.Junction j) => j.signal ? "signal" : j.allWay ? "allway" : j.YieldOnly ? "yield" : "stop";

        static void ControlCensus(CityMap map, CityMeshes.Trims trims)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var inv = CultureInfo.InvariantCulture;
            Line($"JUNCTION CONTROL AUDIT {System.DateTime.Now:yyyy-MM-dd HH:mm}: {map.nodes.Length} nodes, {map.edges.Length} edges, graph hash {CityMap.GraphHashOf(map.edges):x8} (GHSH); PSX_CITY_SIGNALS2 {(CitySignals.Signals2On ? "on" : "OFF")}, local stops {(CitySignals.LocalStopsOn ? "on" : "OFF")}");
            var all = CitySignals.All(map, trims);
            int sigOsm = 0, sigWarrant = 0, allWay = 0, allWayW = 0, stop = 0, stopW = 0, yieldOnly = 0, sigApp = 0, stopApp = 0, yieldApp = 0;
            var nodeJ = new Dictionary<int, int>();
            var tsv = new StringBuilder("id\tkind\twarrant\tx\tz\tlatlon\tnodes\tnames\tapproaches\n");
            for (int i = 0; i < all.Count; i++)
            {
                var j = all[i];
                foreach (int n in j.nodes) nodeJ[n] = i;
                if (j.signal) { if (j.warrant) sigWarrant++; else sigOsm++; sigApp += j.approaches.Count; }
                else if (j.allWay) { allWay++; if (j.warrant) allWayW++; }
                else if (j.YieldOnly) yieldOnly++;
                else { stop++; if (j.warrant) stopW++; }
                if (!j.signal) foreach (var a in j.approaches) { if (a.ctl == 1) yieldApp++; else stopApp++; }
                var ap = new StringBuilder();
                foreach (var a in j.approaches) ap.Append($"e{a.edge}:n{a.node}:{a.ctl};");
                var ns = new List<int>(j.nodes); ns.Sort();
                tsv.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3:0.0}\t{4:0.0}\t{5}\t{6}\t{7}\t{8}\n", i, CtlKind(j), j.warrant ? 1 : 0, j.centre.x, j.centre.y,
                    LatLon(j.centre.x, j.centre.y), string.Join(",", ns), CtlNames(map, j.nodes), ap));
            }
            File.WriteAllText(Path.Combine(root, "city_control.tsv"), tsv.ToString());
            Line("CONTROL (CitySignals): " + CitySignals.ControlReport());
            Line("  " + CitySignals.Signals2Report());
            Line($"  junctions furnished {all.Count}: signals {sigOsm + sigWarrant} ({sigOsm} from OSM, {sigWarrant} by warrant; {sigApp} approaches), all-way STOP {allWay} ({allWayW} by default), two-way STOP {stop} ({stopW} by default), yield only {yieldOnly}; STOP approaches {stopApp}, yield approaches {yieldApp}");

            // every junction node with three usable arms: what controls it
            int jn = 0, jCtl = 0, jLocal = 0, jLocalCtl = 0, jMitre = 0;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                int arms = 0, top = -1; bool fwy = false;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || e.length <= 0.5f || (e.hasLset && (e.lsSub >= 5 || (e.lsFlags & 32) != 0))) continue;
                    arms++; top = Mathf.Max(top, e.link ? 2 : e.cls);
                    if (e.cls >= 5 && !e.link) fwy = true;
                }
                if (arms < 3 || fwy) continue;
                if (trims.mitre[n]) { jMitre++; continue; }
                jn++; if (nodeJ.ContainsKey(n)) jCtl++;
                if (top == 0) { jLocal++; if (nodeJ.ContainsKey(n)) jLocalCtl++; }
            }
            Line($"  junction nodes (3+ usable arms, no freeway, not mitred): {jn}, in a furnished junction {jCtl}; of them local x local {jLocal}, furnished {jLocalCtl} ({jMitre} more are mitred joints)");

            // ---- against the OSM control nodes
            string f = System.Environment.GetEnvironmentVariable("PSX_CTL_OSM");
            if (!string.IsNullOrWhiteSpace(f) && File.Exists(f))
            {
                const float G = 96f;
                long K(float x, float z) => ((long)Mathf.FloorToInt(x / G) << 32) | (uint)Mathf.FloorToInt(z / G);
                var grid = new Dictionary<long, List<int>>();
                for (int i = 0; i < all.Count; i++)
                {
                    long k = K(all[i].centre.x, all[i].centre.y);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(i);
                }
                // the nearest junction of a kind: by its centre or any of its nodes
                float Nearest(float x, float z, System.Func<CitySignals.Junction, bool> want, out int which)
                {
                    float bd = float.MaxValue; which = -1;
                    var p = new Vector2(x, z);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            if (grid.TryGetValue(K(x + dx * G, z + dz * G), out var l))
                                foreach (int i in l)
                                {
                                    if (!want(all[i])) continue;
                                    float d = Vector2.Distance(all[i].centre, p);
                                    foreach (int n in all[i].nodes) d = Mathf.Min(d, Vector2.Distance(map.nodes[n], p));
                                    if (d < bd) { bd = d; which = i; }
                                }
                    return bd;
                }
                var near = new HashSet<int>();
                // the nearest junction node (3+ arms) within r, and whether a game road passes within 20 m
                bool OnRoad(float x, float z, float r, out int node, out float nd)
                {
                    node = -1; nd = r;
                    near.Clear();
                    map.EdgeSegsInRect(new Vector2(x - r, z - r), new Vector2(x + r, z + r), near);
                    bool on = false;
                    var p = new Vector2(x, z);
                    foreach (int pk in near)
                    {
                        var e = map.edges[pk >> 12];
                        int seg = pk & 0xFFF;
                        if (seg + 1 < e.pts.Length)
                        {
                            Vector2 a = e.pts[seg], b = e.pts[seg + 1], ab = b - a;
                            float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                            if (Vector2.Distance(a + ab * u, p) <= 20f) on = true;
                        }
                        foreach (int n in new[] { e.a, e.b })
                        {
                            float d = Vector2.Distance(map.nodes[n], p);
                            if (d < nd && map.nodeEdges[n].Count >= 3) { nd = d; node = n; }
                        }
                    }
                    return on;
                }
                int sigN = 0, sigHit30 = 0, sigHit60 = 0, sigSpecial = 0, sigSpecialAtSignal = 0, stopN = 0, stopHit = 0, stopAtSignal = 0, stopMid = 0, stopAllN = 0, stopAllOk = 0, ywN = 0, ywHit = 0;
                var sigUsed = new HashSet<int>();
                var missSig = new List<string>(); var missNames = new SortedDictionary<string, string>();
                var missStop = new List<string>(); var specialAt = new List<string>();
                var osmSig = new List<Vector2>(); var osmSigSpecial = new List<Vector2>();
                foreach (var row in File.ReadAllLines(f))
                {
                    var p = row.Split('\t');
                    if (p.Length < 7 || !int.TryParse(p[0], out int kind) || !float.TryParse(p[1], NumberStyles.Float, inv, out float x) || !float.TryParse(p[2], NumberStyles.Float, inv, out float z)) continue;
                    string type = p[5], ll = p[3] + "," + p[4];
                    if (!OnRoad(x, z, 45f, out int jnode, out float jd)) continue;
                    if (kind == 4)
                    {
                        bool special = type == "emergency" || type == "pedestrian_crossing" || type == "blinker";
                        float d = Nearest(x, z, j => j.signal, out int which);
                        if (special)
                        {
                            sigSpecial++; osmSigSpecial.Add(new Vector2(x, z));
                            if (d <= 60f) { sigSpecialAtSignal++; specialAt.Add($"{ll} {type}: a game signal {d:0} m away [{CtlNames(map, all[which].nodes)}]"); }
                            continue;
                        }
                        sigN++; osmSig.Add(new Vector2(x, z));
                        if (d <= 30f) sigHit30++;
                        if (d <= 60f) { sigHit60++; sigUsed.Add(which); continue; }
                        string what = "mid-block";
                        if (jnode >= 0)
                        {
                            string names = CtlNames(map, new[] { jnode });
                            string ctl = nodeJ.TryGetValue(jnode, out int ji) ? CtlKind(all[ji]) + (all[ji].warrant ? " (default)" : "") : "nothing";
                            what = $"n{jnode} [{names}] at {jd:0} m: {ctl}";
                            missNames[names] = what;
                        }
                        missSig.Add($"{ll} {what}");
                    }
                    else if (kind == 2)
                    {
                        stopN++;
                        float d = Nearest(x, z, j => !j.signal && !j.YieldOnly, out int which);
                        if (type == "all") stopAllN++;
                        if (d <= 45f) { stopHit++; if (type == "all" && all[which].allWay) stopAllOk++; continue; }
                        if (Nearest(x, z, j => j.signal, out _) <= 45f) { stopAtSignal++; continue; }
                        if (jnode < 0) { stopMid++; continue; }
                        missStop.Add($"{ll} n{jnode} [{CtlNames(map, new[] { jnode })}] at {jd:0} m ({type}/{p[6]}): {(nodeJ.TryGetValue(jnode, out int ji) ? CtlKind(all[ji]) : "nothing")}");
                    }
                    else if (kind == 1)
                    {
                        ywN++;
                        if (Nearest(x, z, j => !j.signal, out _) <= 45f) ywHit++;
                    }
                }
                Line($"  OSM JUNCTION SIGNALS on the game's roads: {sigN}; a game signal within 30 m: {sigHit30}, within 60 m: {sigHit60}; NONE within 60 m: {missSig.Count} ({missNames.Count} distinct junctions with 3+ arms, the rest mid-block: the cross street is not in the game)");
                foreach (var kv in missNames) Line("    SIGNAL-MISSING " + kv.Value);
                foreach (var m in missSig) outLog.AppendLine("    NOSIGNAL " + m);
                Line($"  OSM special signals on the game's roads (fire station, pedestrian crossing, beacon): {sigSpecial}; a game signal within 60 m of one: {sigSpecialAtSignal}");
                foreach (var m in specialAt) outLog.AppendLine("    SPECIAL-NEAR-SIGNAL " + m);
                // the other way: a game signal with no OSM junction signal in reach
                int falseSig = 0, falseSpecial = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var j = all[i];
                    if (!j.signal || j.warrant) continue;
                    float bd = float.MaxValue, bs = float.MaxValue;
                    foreach (var q in osmSig) { float d = Vector2.Distance(q, j.centre); foreach (int n in j.nodes) d = Mathf.Min(d, Vector2.Distance(map.nodes[n], q)); if (d < bd) bd = d; }
                    if (bd <= 60f) continue;
                    foreach (var q in osmSigSpecial) { float d = Vector2.Distance(q, j.centre); foreach (int n in j.nodes) d = Mathf.Min(d, Vector2.Distance(map.nodes[n], q)); if (d < bs) bs = d; }
                    falseSig++; if (bs <= 60f) falseSpecial++;
                    Line($"    SIGNAL-FALSE {LatLon(j.centre.x, j.centre.y)} n{j.nodes[0]} [{CtlNames(map, j.nodes)}]: the nearest OSM junction signal is {(bd < 1e6f ? bd.ToString("0", inv) + " m" : "none")} away{(bs <= 60f ? ", a special signal " + bs.ToString("0", inv) + " m" : "")}");
                }
                Line($"  game signals (OSM kind) with no OSM junction signal within 60 m: {falseSig} ({falseSpecial} of them stand at a fire station's, a crossing's or a beacon's signal)");
                Line($"  OSM STOP nodes on the game's roads: {stopN}; a game STOP junction within 45 m: {stopHit}, at a game signal instead: {stopAtSignal}, mid-block (no junction node within 45 m): {stopMid}, at a junction with no STOP: {missStop.Count}; stop=all nodes {stopAllN}, at an all-way junction {stopAllOk}");
                foreach (var m in missStop) outLog.AppendLine("    STOP-MISSING " + m);
                Line($"  OSM give-way nodes on the game's roads: {ywN}; a game STOP / yield junction within 45 m: {ywHit}");
            }
            else Line("  (PSX_CTL_OSM not given: the OSM comparison is skipped)");

            Line($"  clusters marked and not built: {CitySignals.Sig2Refused.Count}");
            foreach (var s in CitySignals.Sig2Refused) outLog.AppendLine("    REFUSED " + s);
            foreach (var (node, kind, edge, s) in CitySignals.Sig2Special)
            {
                // the junction the special signal stands at or before, and what controls it now
                var e = map.edges[edge];
                int n = s <= e.length - s ? e.a : e.b;
                string ctl = nodeJ.TryGetValue(n, out int ji) ? CtlKind(all[ji]) + (all[ji].warrant ? " (default)" : "") : "nothing";
                outLog.AppendLine($"    SPECIAL {(kind == 1 ? "emergency" : kind == 2 ? "pedestrian" : "blinker")} {LatLon(map.nodes[n].x, map.nodes[n].y)} n{n} [{CtlNames(map, new[] { n })}] {Mathf.Min(s, e.length - s):0} m from it: {ctl}");
            }

            // ---- the left-turn pockets (PSX_CITY_TURNPOCKETS): where, how long, at a signal or not
            Line("  " + CityMeshes.TurnPocketLine());
            int pkSig = 0;
            foreach (var pk in CityMeshes.TurnPockets)
            {
                var pe = map.edges[pk.edge];
                bool atSig = nodeJ.TryGetValue(pk.node, out int pj) && all[pj].signal;
                if (atSig) pkSig++;
                outLog.AppendLine(string.Format(inv, "    POCKET n{0} e{1} '{2}' cls {3} w {4:0.00} storage {5:0} taper {6:0} trim {7:0.0} ({8:0},{9:0}) {10} {11}",
                    pk.node, pk.edge, pe.name, pe.cls, pk.w, pk.storage, pk.taper, pk.trim, pk.at.x, pk.at.y, LatLon(pk.at.x, pk.at.y), atSig ? "signal" : nodeJ.ContainsKey(pk.node) ? CtlKind(all[pj]) : "no control"));
            }
            if (CityMeshes.TurnPockets.Count > 0) Line($"  pockets at a signalised junction: {pkSig} of {CityMeshes.TurnPockets.Count}");

            // ---- the furniture as built (PSX_CTL_FURNISH=1): every junction tile's poles, heads and signs, and what they cost
            var headsOf = new Dictionary<long, (int heads, int view)>();
            if (System.Environment.GetEnvironmentVariable("PSX_CTL_FURNISH") == "1")
            {
                var bld = CityBuildings.Precompute(map);
                var tiles = CitySignals.Tiles(map, trims);
                long tris = 0; double ms = 0; int heads = 0, poles = 0, stopSigns = 0, refused = 0, sigAppN = 0, noHead = 0, noView = 0, bars = 0;
                var noHeadAt = new List<string>();
                foreach (var t in tiles)
                {
                    var st = CitySignals.Build(map, trims, bld, null, null, t.x, t.y);
                    ms += st.ms; heads += st.heads.Count; poles += st.poles.Count; stopSigns += st.stops.Count; refused += st.refused; bars += st.stopBars;
                    foreach (var m in new[] { st.mesh, st.lamps, st.halos })
                    {
                        if (m == null) continue;
                        for (int sm = 0; sm < m.subMeshCount; sm++) tris += m.GetIndexCount(sm) / 3;
                        Object.DestroyImmediate(m);
                    }
                    foreach (var ah in st.approachHeads)
                    {
                        sigAppN++;
                        headsOf[((long)ah.junction << 8) | (uint)ah.approach] = (ah.heads, ah.view);
                        if (ah.heads == 0) { noHead++; if (noHeadAt.Count < 60) { var jj = all[ah.junction]; noHeadAt.Add($"{LatLon(jj.centre.x, jj.centre.y)} n{jj.nodes[0]} [{CtlNames(map, jj.nodes)}] approach e{jj.approaches[ah.approach].edge}"); } }
                        else if (ah.view == 0) noView++;
                    }
                }
                Line(string.Format(inv, "  FURNITURE over {0} junction tiles: {1} signal poles, {2} heads, {3} STOP signs ({4} stop bars; {5} approaches with no clear spot for a sign or pole); signal approaches {6}, with no head at all {7}, with heads but none in the driver's view {8}; {9} triangles in all ({10:0} a tile), {11:0} ms in all ({12:0.00} ms a tile)",
                    tiles.Count, poles, heads, stopSigns, bars, refused, sigAppN, noHead, noView, tris, tiles.Count > 0 ? (double)tris / tiles.Count : 0, ms, tiles.Count > 0 ? ms / tiles.Count : 0));
                foreach (var s in noHeadAt) outLog.AppendLine("    NO-HEAD " + s);
            }
            // ---- what a 3 x 3 ring of tiles costs (PSX_CTL_RINGS="name:x,z;..."): the tile meshes and the junction furniture
            string ringSpec = System.Environment.GetEnvironmentVariable("PSX_CTL_RINGS");
            if (!string.IsNullOrWhiteSpace(ringSpec))
            {
                var bld = CityBuildings.Precompute(map);
                var meshFields = new List<System.Reflection.FieldInfo>();
                foreach (var mf in typeof(CityMeshes.TileMeshes).GetFields()) if (mf.FieldType == typeof(Mesh)) meshFields.Add(mf);
                foreach (var spec in ringSpec.Split(';'))
                {
                    var q = spec.Split(':'); if (q.Length != 2) continue;
                    var pq = q[1].Split(',');
                    if (pq.Length != 2 || !float.TryParse(pq[0], NumberStyles.Float, inv, out float rx) || !float.TryParse(pq[1], NumberStyles.Float, inv, out float rz)) continue;
                    int cx = Mathf.FloorToInt(rx / CityMeshes.TileSize), cz = Mathf.FloorToInt(rz / CityMeshes.TileSize);
                    double tTris = 0, tMs = 0, fTris = 0, fMs = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        tTris = tMs = fTris = fMs = 0;
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                var tm = CityMeshes.Build(map, trims, bld, cx + dx, cz + dz);
                                tMs += sw.Elapsed.TotalMilliseconds;
                                foreach (var mf in meshFields)
                                {
                                    var mesh = mf.GetValue(tm) as Mesh; if (mesh == null) continue;
                                    for (int sm = 0; sm < mesh.subMeshCount; sm++) tTris += mesh.GetIndexCount(sm) / 3;
                                    Object.DestroyImmediate(mesh);
                                }
                                if (CitySignals.AnyIn(map, trims, cx + dx, cz + dz))
                                {
                                    var st = CitySignals.Build(map, trims, bld, null, null, cx + dx, cz + dz);
                                    fMs += st.ms;
                                    foreach (var m in new[] { st.mesh, st.lamps, st.halos })
                                    {
                                        if (m == null) continue;
                                        for (int sm = 0; sm < m.subMeshCount; sm++) fTris += m.GetIndexCount(sm) / 3;
                                        Object.DestroyImmediate(m);
                                    }
                                }
                            }
                    }
                    Line(string.Format(inv, "  RING {0} (tile {1},{2}; 3 x 3, the warm pass): tile meshes {3:0} triangles a tile, {4:0.0} ms a tile; junction furniture {5:0} triangles a tile, {6:0.00} ms a tile",
                        q[0], cx, cz, tTris / 9, tMs / 9, fTris / 9, fMs / 9));
                }
            }

            // ---- named nodes, approach by approach
            string want = System.Environment.GetEnvironmentVariable("PSX_CTL_NODES");
            if (!string.IsNullOrWhiteSpace(want))
                foreach (var tok in want.Split(','))
                {
                    if (!int.TryParse(tok.Trim().TrimStart('n'), out int n) || n < 0 || n >= map.nodes.Length) continue;
                    if (!nodeJ.TryGetValue(n, out int ji)) { Line($"  NODE n{n} ({map.nodes[n].x:0},{map.nodes[n].y:0}) [{CtlNames(map, new[] { n })}]: no furnished junction (patch {trims.patch[n]}, mitre {trims.mitre[n]})"); continue; }
                    var j = all[ji];
                    Line($"  NODE n{n} ({map.nodes[n].x:0},{map.nodes[n].y:0}): junction {ji} {CtlKind(j)}{(j.warrant ? " (default)" : "")} [{CtlNames(map, j.nodes)}] nodes {string.Join(",", j.nodes)} centre ({j.centre.x:0},{j.centre.y:0}) phases {j.phases}");
                    for (int ai = 0; ai < j.approaches.Count; ai++)
                    {
                        var a = j.approaches[ai];
                        var e = map.edges[a.edge];
                        float back = e.b == a.node ? e.length - a.sStop : a.sStop;
                        string hv = headsOf.TryGetValue(((long)ji << 8) | (uint)ai, out var hh) ? $", heads {hh.heads} (in view {hh.view})" : "";
                        Line($"     approach e{a.edge} '{e.name}' into n{a.node}: ctl {a.ctl}, the line {back:0.0} m before the node ({Vector2.Distance(a.at, j.centre):0.0} m from the centre), {a.lanesIn} lanes in, group {a.group}{hv}");
                    }
                }
        }
    }
}
