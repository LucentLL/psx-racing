using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CHECKPOINT FILES' SELF-TEST (DESIGN_v2 9.2, stage 1), batch:
    ///   Unity -batchmode -nographics -executeMethod PSXRacing.EditorTools.CheckpointSelfTest.Run
    /// Report: &lt;project&gt;/PSXRacing_checkpoint_selftest.txt (exit 1 on any FAIL). For every
    /// Resources/CityCheckpoints/*.json:
    /// - it loads, its graph matches the city, its route id is registered;
    /// - the fields build for every gate (timed: metric + cost per gate), every gate is reachable from the one before
    ///   (the router reaches every checkpoint) in metres and in the cost model, and the C# leg metres equal the
    ///   generator's (node, checkpoints.mjs metM) within 1 %;
    /// - THE RULER along the suggested route at 10 m steps: between node crossings the remaining distance to the next
    ///   gate falls (2 m slack within 10 m of a gate), a step up at a node is bounded by the leg's cost-optimum minus
    ///   its metre-optimum (0 on the suggested route) + 2 m;
    /// - the chain is valid in both directions on a reversible venue (the "rev" chain, when the file has one);
    /// - RoutedPath on every leg's cost optimum: no NaN, 4 m spacing, both pavement halves over 1.5 m, timed.
    /// </summary>
    public static class CheckpointSelfTest
    {
        [MenuItem("PSX Racing/Checkpoint self-test")]
        public static void Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            string report = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_checkpoint_selftest.txt");
            void Fail(string s) { sb.AppendLine("FAIL " + s); fails++; }
            try
            {
                var map = CityMap.Get();
                if (map == null) { Fail("no city map"); }
                else
                {
                    CityTracks.Register(map);
                    var R = CityRouter.For(map);
                    sb.AppendLine($"graph {map.graphHash:x8}: {map.edges.Length} edges, {R.NS} states, {R.succ.Length} moves");
                    var files = Resources.LoadAll<TextAsset>(CheckpointFile.Folder);
                    sb.AppendLine(files.Length + " checkpoint file(s)");
                    foreach (var ta in files) TestFile(map, R, ta.name, sb, ref fails);
                }
            }
            catch (System.Exception e) { Fail("exception " + e); }
            sb.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAIL(s)");
            File.WriteAllText(report, sb.ToString());
            Debug.Log("[CheckpointSelfTest] " + (fails == 0 ? "ALL PASS" : fails + " FAILS") + " -> " + report);
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static void TestFile(CityMap map, CityRouter R, string routeId, StringBuilder sb, ref int fails)
        {
            var d = CheckpointFile.Load(routeId);
            if (d == null) { sb.AppendLine("FAIL " + routeId + ": does not load"); fails++; return; }
            if (!CheckpointFile.Matches(d, map, out string why)) { sb.AppendLine("FAIL " + routeId + ": " + why); fails++; return; }
            var route = map.RouteById(d.route);
            if (route == null) { sb.AppendLine("FAIL " + routeId + ": route not registered"); fails++; return; }
            sb.AppendLine($"== {routeId} ({d.candidate}, {d.cost} cost, {(d.loop != 0 ? "circuit" : "sprint")}): loads, graph matches, route registered");
            bool fwy = d.cost == "fwy";
            foreach (var ch in d.chains) TestChain(map, R, route, ch, fwy, routeId, sb, ref fails);
        }

        static void TestChain(CityMap map, CityRouter R, CityMap.Route route, CheckpointFile.Chain ch, bool fwy, string id, StringBuilder sb, ref int fails)
        {
            var cps = ch.cps;
            int N = cps.Length;
            bool loop = ch.loop != 0;
            var met = new CityRouter.Field[N]; var cost = new CityRouter.Field[N];
            var seeds = new List<CityRouter.Seed>();
            float worstMs = 0f, totalMs = 0f;
            for (int k = 0; k < N; k++)
            {
                seeds.Clear();
                foreach (var sp in cps[k].spans)
                {
                    var e = map.edges[sp.e];
                    int st = sp.e * 2 + (sp.d == 1 ? 0 : 1);
                    float g = sp.d == 1 ? sp.s : e.length - sp.s;
                    seeds.Add(new CityRouter.Seed { st = st, g = g });
                    if (!e.oneway) seeds.Add(new CityRouter.Seed { st = st ^ 1, g = e.length - g });
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                met[k] = R.Build(seeds, false, fwy);
                cost[k] = R.Build(seeds, true, fwy);
                float ms = (float)sw.Elapsed.TotalMilliseconds;
                worstMs = Mathf.Max(worstMs, ms); totalMs += ms;
            }
            sb.AppendLine($"  chain {ch.dir}: {N} gates; fields metric+cost: worst gate {worstMs:0.0} ms, all {totalMs:0} ms (design budget PC 9 ms / 150 ms)");
            if (worstMs > 9f) sb.AppendLine("  WARN a gate's fields took over 9 ms (editor, unoptimised build: logged, not failed)");
            // reachability + parity with node, every leg
            int Anchor(int k, out float t)
            {
                var g = cps[k]; var e = map.edges[g.e];
                t = g.d == 1 ? g.s : e.length - g.s;
                return g.e * 2 + (g.d == 1 ? 0 : 1);
            }
            var states = new List<int>();
            for (int k = loop ? 0 : 1; k < N; k++)
            {
                int from = k == 0 ? N - 1 : k - 1;
                int st = Anchor(from, out float t);
                float m = R.CostFrom(met[k], st, t), c = R.CostFrom(cost[k], st, t);
                bool reach = !float.IsInfinity(m) && !float.IsInfinity(c);
                float node = cps[k].metM;
                bool parity = node <= 0f || Mathf.Abs(m - node) <= 0.01f * node + 1f;
                // the cost optimum's metres
                float optM = -1f;
                if (R.Descend(cost[k], st, t, states, out float lastT))
                {
                    optM = 0f;
                    for (int i = 0; i < states.Count; i++) optM += (i == states.Count - 1 ? lastT : R.len[states[i]]) - (i == 0 ? t : 0f);
                }
                string line = $"  leg {from,2}->{k,2}: metres {m:0} (node {node:0}), cost optimum {optM:0} m, cost {c:0}";
                if (!reach) { sb.AppendLine("FAIL" + line + ": UNREACHABLE"); fails++; }
                else if (!parity) { sb.AppendLine("FAIL" + line + ": C# and node disagree by more than 1 %"); fails++; }
                else if (optM > 1.04f * m + 1f) { sb.AppendLine("FAIL" + line + ": the AI's road is more than 4 % longer than the ruler's (test e)"); fails++; }
                else sb.AppendLine("ok  " + line);
            }
            // THE RULER along the suggested route (fwd chains only: the route's own direction)
            if (ch.dir == "fwd") Ruler(map, R, route, cps, met, loop, sb, ref fails);
            // routed paths on every leg's cost optimum
            var go = new GameObject("CheckpointSelfTest_path");
            try
            {
                var tp = go.AddComponent<TrackPath>();
                var pieces = new List<RoutedPath.Piece>();
                float worstBuild = 0f; int bad = 0, built = 0, narrow = 0;
                var sbBad = new StringBuilder();
                for (int k = loop ? 0 : 1; k < N; k++)
                {
                    int from = k == 0 ? N - 1 : k - 1;
                    int st = Anchor(from, out float t);
                    if (!R.Descend(cost[k], st, t, states, out float lastT)) continue;
                    pieces.Clear();
                    for (int i = 0; i < states.Count; i++)
                        pieces.Add(new RoutedPath.Piece { st = states[i], t0 = i == 0 ? t : 0f, t1 = i == states.Count - 1 ? lastT : R.len[states[i]] });
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = RoutedPath.Build(map, pieces, tp, 12f);
                    worstBuild = Mathf.Max(worstBuild, (float)sw.Elapsed.TotalMilliseconds);
                    if (!ok) { bad++; continue; }
                    built++;
                    for (int i = 0; i < tp.Count; i++)
                    {
                        var w = tp.waypoints[i];
                        if (float.IsNaN(w.x) || float.IsNaN(w.y) || float.IsNaN(w.z)) { bad++; break; }
                        // 4 m along the line: the chord between two waypoints is at most the pitch, and at least half of
                        // it across a junction corner (the corner is BuildPath's, PSX_CP_FILLET_R=0)
                        float chord = i > 0 ? Vector2.Distance(new Vector2(w.x, w.z), new Vector2(tp.waypoints[i - 1].x, tp.waypoints[i - 1].z)) : tp.spacing;
                        if (i > 0 && i < tp.Count - 1 && (chord > tp.spacing + 0.05f || chord < 0.5f * tp.spacing))
                        { bad++; sbBad.Append($" leg {from}->{k} wp {i} chord {chord:0.00} m;"); break; }
                        if (tp.halfL[i] < 1.5f || tp.halfR[i] < 1.5f) narrow++;
                        // the (edge, s) back-map runs on continuously: on one edge it moves about a pitch a step, and
                        // onto a new edge it starts near that edge's entry end (the gates' arc pass reads it)
                        if (i > 0 && i < tp.Count - 1)
                        {
                            var ed = map.edges[tp.edgeAt[i]];
                            float entry = tp.dirAt[i] > 0 ? tp.sAt[i] : ed.length - tp.sAt[i];
                            bool bad2 = tp.edgeAt[i] == tp.edgeAt[i - 1] ? Mathf.Abs(tp.sAt[i] - tp.sAt[i - 1]) > tp.spacing * 1.6f
                                                                          : entry > tp.spacing * 1.6f;
                            if (bad2) { bad++; sbBad.Append($" leg {from}->{k} wp {i} edge {tp.edgeAt[i]} s {tp.sAt[i]:0.0} (was {tp.edgeAt[i - 1]} s {tp.sAt[i - 1]:0.0});"); break; }
                        }
                    }
                }
                if (bad > 0) { sb.AppendLine($"FAIL  routed paths: {bad} bad of {built}:{sbBad}"); fails++; }
                else sb.AppendLine($"ok    routed paths: {built} legs built, worst build {worstBuild:0.0} ms (budget 3 ms), {narrow} waypoints with a pavement half under 1.5 m");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Ruler(CityMap map, CityRouter R, CityMap.Route route, CheckpointFile.Gate[] cps, CityRouter.Field[] met, bool loop, StringBuilder sb, ref int fails)
        {
            // the route chain's arcs
            int n = route.edges.Length;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + map.edges[route.edges[i]].length;
            float L = cum[n];
            int N = cps.Length;
            float start = cps[0].routeM;
            float span = loop ? L : (cps[N - 1].routeM - start);
            int target = 1, rises = 0, steps = 0;
            float prev = -1f; int prevEdge = -1; float worstRise = 0f; string worstAt = "";
            for (float u = 1f; u < span - 1f; u += 10f)
            {
                float m = start + u; if (loop) m = Mathf.Repeat(m, L);
                // passed the target gate?
                float tu = Mathf.Repeat(cps[target].routeM - start, loop ? L : 1e9f);
                if (target == 0) tu = L;
                if (u >= tu)
                {
                    target = loop ? (target + 1) % N : target + 1;
                    if (!loop && target >= N) break;
                    prev = -1f;
                }
                int i = 0; while (i < n - 1 && cum[i + 1] <= m) i++;
                int e = route.edges[i]; var ed = map.edges[e];
                float t = m - cum[i];
                float s = route.dirs[i] >= 0 ? t : ed.length - t;
                int st = e * 2 + (route.dirs[i] >= 0 ? 0 : 1);
                float v = R.CostFrom(met[target], st, R.TOn(st, s));
                steps++;
                if (prev >= 0f && v > prev + 0.5f)
                {
                    bool atNode = e != prevEdge;
                    float nearGate = Mathf.Abs(u - tu);
                    bool ok = (atNode && v - prev <= 2f) || nearGate < 10f && v - prev <= 2f;
                    if (!ok)
                    {
                        rises++;
                        if (v - prev > worstRise) { worstRise = v - prev; worstAt = $"route {m:0} m (edge {e}, gate {target}): {prev:0} -> {v:0}"; }
                    }
                }
                prev = v; prevEdge = e;
            }
            if (rises > 0) { sb.AppendLine($"FAIL  ruler: remaining rose {rises} times in {steps} steps along the suggested route; worst {worstRise:0.0} m at {worstAt}"); fails++; }
            else sb.AppendLine($"ok    ruler: remaining falls along the suggested route ({steps} steps of 10 m)");
        }
    }
}
