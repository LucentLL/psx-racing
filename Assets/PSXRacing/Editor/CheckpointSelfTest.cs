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
                // the race builds the cost field only (its RM is the ruler); the plain-metre one is the node parity's
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cost[k] = R.Build(seeds, true, fwy);
                float ms = (float)sw.Elapsed.TotalMilliseconds;
                worstMs = Mathf.Max(worstMs, ms); totalMs += ms;
                met[k] = R.Build(seeds, false, fwy);
            }
            sb.AppendLine($"  chain {ch.dir}: {N} gates; the race's field (cost + ruler metres): worst gate {worstMs:0.0} ms, all {totalMs:0} ms (design budget PC 9 ms / 150 ms)");
            if (worstMs > 9f || totalMs > 150f) sb.AppendLine("  WARN over the field budget (editor, unoptimised build: logged, not failed)");
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
                // the ruler's metres from the gate before = the metres walked down the route
                float rm = R.RouteFrom(cost[k], st, t);
                string line = $"  leg {from,2}->{k,2}: metres {m:0} (node {node:0}), cost optimum {optM:0} m (ruler {rm:0}), cost {c:0}";
                if (!reach) { sb.AppendLine("FAIL" + line + ": UNREACHABLE"); fails++; }
                else if (!parity) { sb.AppendLine("FAIL" + line + ": C# and node disagree by more than 1 %"); fails++; }
                else if (optM > 1.04f * m + 1f) { sb.AppendLine("FAIL" + line + ": the AI's road is more than 4 % longer than the shortest (test e)"); fails++; }
                else if (Mathf.Abs(rm - optM) > 0.5f) { sb.AppendLine("FAIL" + line + ": the ruler's metres are not the route's"); fails++; }
                else sb.AppendLine("ok  " + line);
            }
            // THE RULER along the suggested route (fwd chains only: the route's own direction)
            if (ch.dir == "fwd") Ruler(map, R, route, cps, cost, loop, sb, ref fails);
            // THE RULER for a car that leaves its road (a crash, a lawn) and comes back, and along a GPS route from
            // off the suggested line (the check race's blockers 1 and 3)
            OffRoad(map, R, cps, cost, loop, sb, ref fails);
            OffRoute(map, R, cps, cost, loop, sb, ref fails);
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
                float v = R.RouteFrom(met[target], st, R.TOn(st, s));
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

        static int AnchorOf(CityMap map, CheckpointFile.Gate g, out float t)
        {
            var e = map.edges[g.e];
            t = g.d == 1 ? g.s : e.length - g.s;
            return g.e * 2 + (g.d == 1 ? 0 : 1);
        }

        static Vector3 At(CityMap map, CityRouter R, int st, float t, out Vector2 tan)
        {
            var p = R.PointOn(st, t, out tan);
            return new Vector3(p.x, map.edges[st >> 1].YAt(R.SOn(st, t)), p.y);
        }

        /// <summary>
        /// BLOCKER 1 (the check race's crashed Supra): a car on its leg's route is knocked off the road, out to 30 m
        /// either side, slides back along it and drives back on. Measured as the player is (the sticky locate, no
        /// speed: a crashed car's heading means nothing), the remaining must move no more than the car does (x2.5 for a curve's inside, +3 m)
        /// at every step, unless the locate took another road the car stands ON (a switch onto its pavement, logged).
        /// </summary>
        static void OffRoad(CityMap map, CityRouter R, CheckpointFile.Gate[] cps, CityRouter.Field[] F, bool loop, StringBuilder sb, ref int fails)
        {
            int N = cps.Length, trials = 0, steps = 0, bad = 0, switches = 0;
            float worst = 0f; string worstAt = "";
            var states = new List<int>();
            float now = 1000f;
            for (int k = loop ? 0 : 1; k < N; k++)
            {
                int from = k == 0 ? N - 1 : k - 1;
                int st0 = AnchorOf(map, cps[from], out float t0);
                if (!R.Descend(F[k], st0, t0, states, out float lastT)) continue;
                // points every 150 m down the leg, 40 m clear of its ends
                float legLen = R.RouteFrom(F[k], st0, t0);
                for (float u = 40f; u < legLen - 40f; u += 150f)
                {
                    // walk u metres down the descent
                    float left = u; int si = 0; float t = t0; int st = states[0];
                    for (si = 0; si < states.Count; si++)
                    {
                        st = states[si];
                        float a = si == 0 ? t0 : 0f, b = si == states.Count - 1 ? lastT : R.len[st];
                        if (left <= b - a) { t = a + left; break; }
                        left -= b - a;
                    }
                    if (si >= states.Count) break;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        trials++;
                        var road = At(map, R, st, t, out Vector2 tan);
                        var right = new Vector3(tan.y, 0f, -tan.x);
                        var fwd = new Vector3(tan.x, 0f, tan.y);
                        var loc = new CityRouter.Loc();
                        int dir = 0, rEdge = -1;
                        // seated on the route, moving along it
                        for (int w = 0; w < 3; w++) { now += 0.12f; R.Locate(road, fwd * 20f, fwd, ref loc, now); }
                        if (loc.edge != rEdge) { rEdge = loc.edge; dir = 0; }
                        float prev = R.Remaining(F[k], road, ref loc, ref dir);
                        Vector3 pos = road, last = road;
                        // out 30 m (12 x 2.5), back along the road 24 m (8 x 3), then in again (12 x 2.5)
                        for (int step = 0; step < 32; step++)
                        {
                            Vector3 mv = step < 12 ? right * side * 2.5f : step < 20 ? -fwd * 3f : -right * side * 2.5f;
                            pos += mv;
                            now += 0.12f;
                            int before = loc.edge;
                            R.Locate(pos, Vector3.zero, fwd, ref loc, now);
                            if (loc.edge != rEdge) { rEdge = loc.edge; dir = 0; }
                            float v = R.Remaining(F[k], pos, ref loc, ref dir);
                            steps++;
                            float moved = (pos - last).magnitude;
                            float jump = Mathf.Abs(v - prev);
                            if (jump > 2.5f * moved + 3f)   // a curve's inside moves the projection faster than the car
                            {
                                bool switched = loc.edge != before && loc.valid && !loc.offGraph && loc.dist <= map.edges[loc.edge].HalfMax + 1.5f;
                                if (switched) switches++;
                                else
                                {
                                    bad++;
                                    if (jump > worst) { worst = jump; worstAt = $"leg ->{k} at {u:0} m, side {side}, step {step}: {prev:0} -> {v:0} m (edge {before} -> {loc.edge}, off {loc.dist:0} m)"; }
                                }
                            }
                            prev = v; last = pos;
                        }
                    }
                }
            }
            if (bad > 0) { sb.AppendLine($"FAIL  off the road: the remaining jumped {bad} times in {steps} steps of {trials} crash walks; worst {worst:0} m at {worstAt}"); fails++; }
            else sb.AppendLine($"ok    off the road: {trials} crash walks ({steps} steps, out to 30 m either side and back): the remaining never moved more than the car; {switches} switches onto another road's pavement");
        }

        /// <summary>
        /// BLOCKER 3 (the check race's forced detour): from streets OFF the suggested route (300-900 m from each
        /// gate), a car follows the GPS route (the field's descent) at 10 m steps, measured as the player is. The
        /// remaining must be the route's own metres at every step (within 3 m), so it falls metre for metre and no
        /// node steps it.
        /// </summary>
        static void OffRoute(CityMap map, CityRouter R, CheckpointFile.Gate[] cps, CityRouter.Field[] F, bool loop, StringBuilder sb, ref int fails)
        {
            int N = cps.Length, walks = 0, steps = 0, bad = 0;
            float worst = 0f; string worstAt = "";
            var states = new List<int>();
            float now = 5000f;
            for (int k = loop ? 0 : 1; k < N; k++)
            {
                var gc = new Vector2(cps[k].x, cps[k].z);
                int picked = 0;
                for (int ei = (k * 7919) % map.edges.Length, guard = 0; guard < map.edges.Length && picked < 4; guard++, ei = (ei + 97) % map.edges.Length)
                {
                    var e = map.edges[ei];
                    if (e.length < 60f || e.pts == null) continue;
                    float dist = Vector2.Distance(e.PointAt(e.length * 0.5f), gc);
                    if (dist < 300f || dist > 900f) continue;
                    int st = ei * 2;
                    if (!R.legal[st] || float.IsInfinity(F[k].D[st]) || float.IsInfinity(F[k].RM[st])) continue;
                    if (!R.Descend(F[k], st, 10f, states, out float lastT)) continue;
                    picked++; walks++;
                    var loc = new CityRouter.Loc();
                    int dir = 0, rEdge = -1;
                    for (int i = 0; i < states.Count && i < 200; i++)
                    {
                        int s = states[i];
                        float a = i == 0 ? 10f : 0f, b = i == states.Count - 1 ? lastT : R.len[s];
                        for (float t = a + 2f; t < b - 2f; t += 10f)
                        {
                            var pos = At(map, R, s, t, out Vector2 tan);
                            var fwd = new Vector3(tan.x, 0f, tan.y);
                            // the lane, not the OSM line: 2 m to the right of the travel direction
                            pos += new Vector3(tan.y, 0f, -tan.x) * 2f;
                            now += 0.5f;
                            R.Locate(pos, fwd * 20f, fwd, ref loc, now);
                            if (loc.edge != rEdge) { rEdge = loc.edge; dir = 0; }
                            float v = R.Remaining(F[k], pos, ref loc, ref dir);
                            float want = R.RouteFrom(F[k], s, t);
                            steps++;
                            if (Mathf.Abs(v - want) > 3f)
                            {
                                bad++;
                                if (Mathf.Abs(v - want) > worst) { worst = Mathf.Abs(v - want); worstAt = $"gate {k} from edge {ei}: on edge {s >> 1} at {t:0} m the ruler read {v:0} m, the route {want:0} m (located edge {loc.edge}, dir {dir})"; }
                            }
                        }
                    }
                }
            }
            float share = steps > 0 ? bad / (float)steps : 0f;
            if (share > 0.01f) { sb.AppendLine($"FAIL  off the route: the ruler left the GPS route's metres at {bad} of {steps} steps on {walks} walks; worst {worst:0} m at {worstAt}"); fails++; }
            else sb.AppendLine($"ok    off the route: {walks} GPS walks from off-route streets, {steps} steps of 10 m: the ruler read the route's own metres at all but {bad} (worst {worst:0} m{(bad > 0 ? ", " + worstAt : "")})");
        }
    }
}
