using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SPLITS AUDIT (J1, 2026-11-16): every carriageway end at a split node
    /// measured on LineModel's REAL drawn lane centre (OwnLanePoint) and
    /// pavement edges (Extents + SectionLeft), in the undivided road's straight
    /// frame (a 30 m chord into the node; never a midline reference), every
    /// 0.5 m along the carriageway's chain from its drawn start:
    ///   * W, the lane centre's move from its start to the chain's end region
    ///     (mean of the last 10 m), and the 95 % window it takes;
    ///   * the peak lateral slope over 2 m, as an equivalent taper rate
    ///     1.5/slope (a smoothstep of W over L peaks at 1.5 W/L): fails L/2
    ///     below r(S)/2, L below r(S) (r = S^2/60 to 40 mph, S from 45; S the
    ///     undivided road's posted speed, or its class default);
    ///   * the outward overshoot past the final lateral (1 m or more fails);
    ///   * the lane-centre angle at the node (the undivided road's last 4 m
    ///     against the carriageway's first 4 m; G4: atan(1/V));
    ///   * the jump of the outer pavement edge across the node or fan;
    ///   * the undivided road's heading change over its last 30 m.
    /// A node passes when every end passes (slope at L/2 to 35 mph posted, L
    /// from 40; overshoot under 1 m; jump under 0.3 m). Beatties Ford's 19
    /// split-type nodes are listed one by one; the city-wide counts cover every
    /// SPLT node. Writes city_splits.txt, city_splits_ends.tsv (one row per
    /// end) and city_splits_plan.tsv (polylines for the plan sheets: OSM centre
    /// lines, drawn pavement edges, lane centres and painted lines within 90 m
    /// of each Beatties Ford node). PSX_SPLITS_TAG names the run in the files.
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunSplits
    /// </summary>
    public static partial class CityAudit
    {
        struct SplitEndM
        {
            public int node, u, c, far; public string name, kind;
            public float mph, rate, q0, qF, W, Lt, peak, rEq, over, angle, jump, uStart, uMax, q20, own20, med20, bulge, uHead;
            public bool pass, halfOk, fullOk, mono;
            public string why, info;
        }

        public static void RunSplits()
        {
            outLog = new StringBuilder(); failures = 0;
            var inv = CultureInfo.InvariantCulture;
            var map = CityMap.Get();
            if (map == null || map.splt == null) { Fail("charlotte_city.bytes / SPLT missing"); FinishTo("city_splits.txt"); return; }
            string tag = System.Environment.GetEnvironmentVariable("PSX_SPLITS_TAG") ?? LineModel.SplitAxis;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var t = CityMeshes.ComputeTrims(map);
            CityMeshes.EnsureMarks(map, t);
            var ms = CityMeshes.MarkStats;
            System.Func<CityMap.Edge, int, int> partner = (e, node) =>
            {
                if (!t.mitre[node]) return -1;
                int o = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
                return o == e.index ? -1 : o;
            };
            var splitNodes = new HashSet<int>();
            foreach (var sp in map.splt) splitNodes.Add(sp.node);
            Line($"SPLITS AUDIT (J1) run '{tag}': PSX_CITY_SPLITAXIS {LineModel.SplitAxis}, PSX_CITY_BAYEND {(LineModel.BayEndOn ? 1 : 0)}, PSX_CITY_NOSE {(LineModel.NoseRuleOn ? 1 : 0)}, PSX_CITY_REFUGE {(LineModel.RefugeOn ? 1 : 0)}; graph {map.graphHash:x8}; SPLT nodes {map.splt.Length}");
            Line($"  drawn: refuge pairs {LineModel.Refuges.Count / 2} (listed {LineModel.RefugeListedN}), axis-frame carriageway ends {LineModel.SplitFrames.Count}, undivided chord eases {LineModel.SplitUEases}, in-scope 3-arm splits mitred {LineModel.SplitForcedMitre}, BAYEND edges {LineModel.BayEndEdges}, shaped noses {Count(LineModel.SplitNoseNodes)}");
            Line($"  marks: refuge/split noses hatched {ms.noseHatches} ({ms.noseHatchBars} bars), split noses marked {ms.noseSplit} (hatched {ms.noseSplitHatched}), bays opening at a nose {ms.noseBayOpen}, flush tips {ms.noseFlushTip}, turn arrows into a plain nose {ms.noseArrows}");
            Line($"  PHASE C (scope city {(LineModel.CityPhaseC ? "ON" : "off")}): blends lengthened {LineModel.PhaseCLonger}, curving roads on J1's length {LineModel.PhaseCCurving}, blends held near the own line {LineModel.PhaseCDevCap}, blends ended where the own line runs level enough {LineModel.PhaseCEndShortened} (declined: {LineModel.PhaseCSteepEnd}), blends ended before a side street {LineModel.PhaseCJunctionCap}, bays whose lane goes on as a lane past an end (kept under city, ended - a lane drop - otherwise) {LineModel.PhaseCBayLaneOn}, same-name meeting noses marked {ms.noseMeeting}; taper floors {(LineModel.TaperFloorOn ? "ON" : "off")}: SHIFT records lengthened {LineModel.TaperFloorRaised}, tapers short of their floored length for room {LineModel.TaperFloorShort}");
            Line($"  TIGHT TWINS (carriageway pairs mapped closer than their lanes, pushed apart and drawn flush; scope city): {LineModel.TightTwinPairs} pairs, {LineModel.TightTwinEdgeCount} edges, {LineModel.TightTwinM:0} m; LANE FIT (PSX_CITY_LINEFIX {(LineModel.LineFixOn ? "on" : "off")}): {LineModel.LaneFitEdges} edges on the seven-lane profile");
            Line($"  RELAYS (the road-lines round): {LineModel.RelayJoins} joins where lines move sideways between two line sets of one width ({LineModel.RelayShort} short of their length; {LineModel.RelayBayCross} of them the double yellow crossing the centre lane between two opposing bays, over the bay taper); {LineModel.RelayHandOvers} joins where a two-way left-turn lane meets a bay and no line moves (PSX_CITY_LINEFIX)");
            Line($"  CENTRE RUNS (PSX_CITY_LINEFIX): {LineModel.CentreRunsMade} short centre-lane bays between mid-block joins ({LineModel.CentreRunEdges} edges, {LineModel.CentreRunM:0} m) drawn as two-way left-turn lanes");
            { int q = 0; foreach (var s in LineModel.CentreRunLog) if (q++ < 40) Line("    centre run: " + s); }
            { int q = 0; foreach (var s in LineModel.TightTwinLog) if (q++ < 60) Line("    tight twin: " + s); }
            { int q = 0; foreach (var s in LineModel.TaperFloorShortLog) if (q++ < 40) Line("    taper room short: " + s); }
            TaperCensus(map);
            foreach (var l in LineModel.BayEndLog) Line("  BAYEND " + l);

            var ends = new List<SplitEndM>(2048);
            var plan = new StringBuilder("tag\tnode\tkind\tedge\tseq\tx\tz\n");
            var bfr = new HashSet<int>(LineModel.BfrSplitNodes);
            foreach (var sp in map.splt)
                for (int c = 0; c < 2; c++)
                {
                    int ci = c == 0 ? sp.a : sp.b;
                    if (ci < 0 || ci >= map.edges.Length || sp.u < 0 || sp.u >= map.edges.Length) continue;
                    var m = MeasureSplitEnd(map, t, partner, splitNodes, sp.node, sp.u, ci);
                    if (m.kind == null) continue;
                    ends.Add(m);
                }
            // the three same-name fan meetings on Beatties Ford (no SPLT record): measured alike
            foreach (int n in new[] { 634, 7868, 9152 })
            {
                if (map.graphHash != LineModel.SplitsGraph || n >= map.nodes.Length) continue;
                int u = -1; var cs = new List<int>();
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.name != "Beatties Ford Road") continue;
                    if (!e.oneway) u = ei; else cs.Add(ei);
                }
                if (u < 0) continue;
                foreach (int ci in cs) { var m = MeasureSplitEnd(map, t, partner, splitNodes, n, u, ci); if (m.kind != null) { m.kind = "meet-" + m.kind; ends.Add(m); } }
            }

            // Phase C: every other same-name meeting city-wide (the n7868 type), measured in every scope alike
            // ("meet*-": kept apart from the SPLT ends in the CITY-WIDE line)
            {
                int nm = 0;
                foreach (var mt in LineModel.MeetingNodesAll(map))
                {
                    if (map.graphHash == LineModel.SplitsGraph && (mt.node == 634 || mt.node == 7868 || mt.node == 9152)) continue;
                    foreach (int ci in new[] { mt.a, mt.b })
                    {
                        var m = MeasureSplitEnd(map, t, partner, splitNodes, mt.node, mt.u, ci);
                        if (m.kind != null) { m.kind = "meetC-" + m.kind; ends.Add(m); nm++; }
                    }
                }
                Line($"  SAME-NAME MEETINGS city-wide (no SPLT record; the n7868 type): {nm} carriageway ends measured (kind meetC-*)");
            }
            // ---- refuge carriageways: judged by the medians gate's own measure (the island's frame) ----
            {
                var refs = new List<LineModel.Refuge>(LineModel.Refuges);
                var mR = LineModel.MeasureRefuges(map, refs, out _, out _, out _);
                for (int i = 0; i < ends.Count; i++)
                {
                    var m = ends[i];
                    if (!m.kind.StartsWith("refuge")) continue;
                    for (int k = 0; k < refs.Count; k++)
                    {
                        var r = refs[k];
                        bool atN = r.node == m.node && r.chain[0] == m.c, atF = r.far == m.node && r.chain[r.chain.Length - 1] == m.c;
                        if (!atN && !atF) continue;
                        string f = LineModel.RefugeFails(r, mR[k]);
                        m.pass = f.Length == 0 && m.jump < 0.5f;
                        m.why = f.Length == 0 ? "" : " island measure:" + f;
                        m.info = $" [the gate's island measure: island at mid {mR[k].island:0.00} m (design wI {2f * r.half:0.00}), outer edge off the road's edge line {mR[k].outerDev:0.000} m, steepest lane line / edge / path {mR[k].slope:0.000}, x0 {r.x0s:0.00}/{r.x0e:0.00}]";
                        ends[i] = m;
                        break;
                    }
                }
            }
            // ---- Beatties Ford, node by node ----
            var byNode = new Dictionary<int, List<SplitEndM>>();
            foreach (var m in ends) { if (!byNode.TryGetValue(m.node, out var l)) byNode[m.node] = l = new List<SplitEndM>(); l.Add(m); }
            int bfrPass = 0, bfrN = 0;
            Line("BEATTIES FORD'S 19 SPLIT-TYPE NODES (per carriageway end: kind, speed, start q0 -> end qF lateral in the axis frame, move W over its 95% window, peak slope as a taper rate (need r), outward overshoot, angle at the node, outer-edge jump; PASS = the census measure: the outer edge's main move within 80 m (half a short island) at r/2 or flatter to 35 mph, r from 40, bulge < 1 m, jump < 0.5 m):");
            foreach (int n in LineModel.BfrSplitNodes)
            {
                bfrN++;
                if (!byNode.TryGetValue(n, out var l) || l.Count == 0) { Line($"  n{n}: no carriageway end measured"); continue; }
                bool ok = true; foreach (var m in l) ok &= m.pass;
                if (ok) bfrPass++;
                var np = map.nodes[n];
                Line($"  n{n} ({LatLon(np.x, np.y)}; x {np.x:0.0} z {np.y:0.0}) {(ok ? "PASS" : "FAIL")}");
                foreach (var m in l) Line("    " + EndLine(map, m));
            }
            Line($"  Beatties Ford: {bfrPass} of {bfrN} split-type nodes pass");

            // ---- the owner's four spots ----
            Line("THE OWNER'S FOUR SPOTS:");
            SpotLines(map, t, byNode);
            // ---- the fix round: each framed nose's design, its curb as the tiles lay it, the poles ----
            NoseLines(map, t);

            // ---- city-wide ----
            int nE = 0, f2 = 0, f1 = 0, ov = 0, ang = 0, jmp = 0, nodesFail = 0; var failNodes = new HashSet<int>();
            var angs = new List<float>(); var kinds = new Dictionary<string, int[]>();
            foreach (var m in ends)
            {
                if (m.kind.StartsWith("meet")) continue;   // (the meetings: their own line below)
                nE++;
                if (!m.halfOk) f2++;
                if (!m.fullOk) f1++;
                if (m.bulge >= 1f) ov++;
                float g4 = Mathf.Atan(1f / Mathf.Max(1f, m.mph)) * Mathf.Rad2Deg;
                if (m.angle > g4 + 0.05f) ang++;
                if (m.jump >= 0.5f) jmp++;
                angs.Add(m.angle);
                if (!m.pass) failNodes.Add(m.node);
                if (!kinds.TryGetValue(m.kind, out var k)) kinds[m.kind] = k = new int[4];
                k[0]++; if (!m.halfOk) k[1]++; if (!m.fullOk) k[2]++; if (m.bulge >= 1f) k[3]++;
            }
            nodesFail = failNodes.Count;
            angs.Sort();
            float P(List<float> v, float p) => v.Count == 0 ? float.NaN : v[Mathf.Clamp(Mathf.FloorToInt(p * (v.Count - 1)), 0, v.Count - 1)];
            Line($"CITY-WIDE ({nE} carriageway ends at {map.splt.Length} SPLT nodes): failing L/2 {f2}; failing full L {f1} ({(nE > 0 ? 100f * f1 / nE : 0f):0.0}%); outward bulge (overshoot) 1 m or more {ov}; lane-centre angle over atan(1/V) {ang} (p50 {P(angs, 0.5f):0.0} deg, p90 {P(angs, 0.9f):0.0} deg); outer-edge jumps 0.5 m or more {jmp}; nodes failing {nodesFail}");
            foreach (var kv in kinds) Line($"  {kv.Key,-22} ends {kv.Value[0],5} | fail L/2 {kv.Value[1],5} | fail L {kv.Value[2],5} | overshoot >= 1 m {kv.Value[3],5}");
            Line($"  refuge pairs drawn {LineModel.Refuges.Count / 2}; skipped (in scope, old taper kept): {LineModel.SplitSkipped.Count}");
            {
                // Phase C: every skip by its reason (the first 60 listed below as before)
                var why = new Dictionary<string, int>();
                foreach (var s in LineModel.SplitSkipped)
                {
                    int c = s.IndexOf("': ");
                    string r = c >= 0 ? s.Substring(c + 3) : s;
                    r = System.Text.RegularExpressions.Regex.Replace(r, @"[-+]?\d+(\.\d+)?", "#");
                    if (r.Length > 70) r = r.Substring(0, 70);
                    why[r] = (why.TryGetValue(r, out int v) ? v : 0) + 1;
                }
                var wl = new List<KeyValuePair<string, int>>(why); wl.Sort((x, y) => y.Value.CompareTo(x.Value));
                Line($"  skipped by reason ({LineModel.SplitSkipped.Count}): " + string.Join("; ", wl.ConvertAll(kv => $"{kv.Value} {kv.Key}")));
                int capped = 0, capJ = 0, capDev = 0, curving = 0;
                foreach (var f in LineModel.SplitFrames)
                {
                    if (f.capped) capped++;
                    if (f.note.Contains("side street")) capJ++;
                    if (f.note.Contains("held to")) capDev++;
                    if (f.note.Contains("curving")) curving++;
                }
                Line($"  framed ends {LineModel.SplitFrames.Count}: blend cut short by room {capped}, ended before a side street {capJ}, held near the own line {capDev}, curving (J1's length) {curving}");
            }
            int sk = 0; foreach (var s in LineModel.SplitSkipped) { if (sk++ < 60) Line("    skipped " + s); }
            foreach (var f in LineModel.SplitFrames) if (f.capped && sk++ < 120) Line($"    room shortfall n{f.node} e{f.edge}: {f.note}");

            // ---- lot entrances on edges a profile shift moves (charlotte_lotent's key does not see eases) ----
            {
                int na = map.lotEntrances != null ? map.lotEntrances.Length : 0, moved = 0; float worst = 0f; string at = "";
                for (int i = 0; i < na; i++)
                {
                    var E = map.lotEntrances[i];
                    if (E.edge < 0 || E.edge >= map.edges.Length) continue;
                    var e = map.edges[E.edge];
                    if (e.lmEase == null) continue;
                    bool prof = false; foreach (var z in e.lmEase) if (z.prof != null && z.tag < 0 && (z.shift || z.widen)) prof = true;
                    if (!prof) continue;
                    float sh = Mathf.Abs(LineModel.ShiftAt(e, Mathf.Clamp(E.s, 0f, e.length)));
                    if (sh < 0.05f) continue;
                    moved++; if (sh > worst) { worst = sh; at = $"e{E.edge} s {E.s:0.0} '{e.name}'"; }
                }
                Line($"LOT ENTRANCES on edges a profile shift moves (0.05 m or more at the entrance): {moved} of {na}{(moved > 0 ? $"; worst {worst:0.00} m at {at}" : "")} (charlotte_lotent.bytes is keyed without the eases: re-make it if this is not 0 against the base)");
            }
            // ---- the ends table and the plan polylines ----
            {
                // Phase C: every framed end's design (L, room, capped and why) for the analysis
                var fs = new StringBuilder("node\tedge\tu\tuStart\tL\tuEnd\tdelta\tlambda0\tqEnd\troom\tcapped\tmph\tx\tz\tdevMax\tname\tnote\n");
                foreach (var f in LineModel.SplitFrames)
                    fs.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3:0.0}\t{4:0.0}\t{5:0.0}\t{6:0.00}\t{7:0.00}\t{8:0.00}\t{9:0.0}\t{10}\t{11:0.0}\t{12:0.0}\t{13:0.0}\t{14:0.00}\t{15}\t{16}\n", f.node, f.edge, f.u, f.uStart, f.L, f.uEnd, f.delta, f.lambda0, f.qEnd, f.room, f.capped ? 1 : 0, f.mph, f.p0.x, f.p0.y, f.devMax, map.edges[f.u].name, f.note));
                // every SPLT node's position and road (the spot lists)
                var ns = new StringBuilder("node\tx\tz\tname\tmph\n");
                foreach (var sp in map.splt)
                    if (sp.u >= 0 && sp.u < map.edges.Length)
                        ns.Append(string.Format(inv, "{0}\t{1:0.0}\t{2:0.0}\t{3}\t{4:0.0}\n", sp.node, map.nodes[sp.node].x, map.nodes[sp.node].y, map.edges[sp.u].name, LineModel.SplitSpeedMph(map.edges[sp.u])));
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_splits_nodes.tsv"), ns.ToString());
                File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_splits_frames.tsv"), fs.ToString());
            }
            var tsv = new StringBuilder("tag\tnode\tu\tc\tkind\tmph\trate\tq0\tqF\tW\tLt\tpeak\trEq\tover\tangle\tjump\tbulge\tmed20\tq20\town20\tmono\thalfOk\tfullOk\tpass\n");
            foreach (var m in ends)
                tsv.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t{4}\t{5:0.0}\t{6:0.0}\t{7:0.00}\t{8:0.00}\t{9:0.00}\t{10:0.0}\t{11:0.0000}\t{12:0.0}\t{13:0.00}\t{14:0.00}\t{15:0.00}\t{16:0.00}\t{17:0.00}\t{18:0.00}\t{19:0.00}\t{20}\t{21}\t{22}\t{23}\n",
                    tag, m.node, m.u, m.c, m.kind, m.mph, m.rate, m.q0, m.qF, m.W, m.Lt, m.peak, m.rEq, m.over, m.angle, m.jump, m.bulge, m.med20, m.q20, m.own20, m.mono ? 1 : 0, m.halfOk ? 1 : 0, m.fullOk ? 1 : 0, m.pass ? 1 : 0));
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "city_splits_ends.tsv"), tsv.ToString());
            foreach (int n in LineModel.BfrSplitNodes) if (n < map.nodes.Length) PlanDump(map, t, n, tag, plan, inv);
            // Phase C: PSX_SPLITS_PLANNODES=n1,n2,... dumps those nodes' plans too (the worst spots city-wide)
            foreach (var tok in (System.Environment.GetEnvironmentVariable("PSX_SPLITS_PLANNODES") ?? "").Split(','))
                if (int.TryParse(tok.Trim().TrimStart('n'), out int pn) && pn >= 0 && pn < map.nodes.Length && System.Array.IndexOf(LineModel.BfrSplitNodes, pn) < 0) PlanDump(map, t, pn, tag, plan, inv);
            File.WriteAllText(Path.Combine(root, "city_splits_plan.tsv"), plan.ToString());
            // Phase C: lot entrances whose drawn pavement edge moves against PSX_CITY_SPLITAXIS=0 (the eases
            // the lotent key does not read) - PSX_SPLITS_LOTBASE=1 (a second solve under scope 0, last)
            if (System.Environment.GetEnvironmentVariable("PSX_SPLITS_LOTBASE") == "1" && map.lotEntrances != null)
            {
                int na = map.lotEntrances.Length;
                var cur = new float[na];
                for (int i = 0; i < na; i++) cur[i] = EntranceEdge(map, map.lotEntrances[i]);
                string was = LineModel.SplitAxis;
                LineModel.SplitAxis = "0";
                CityMeshes.ComputeTrims(map);
                int moved = 0, moved25 = 0; float worst = 0f; string at = ""; var hist = new int[5];
                for (int i = 0; i < na; i++)
                {
                    float b = EntranceEdge(map, map.lotEntrances[i]);
                    if (float.IsNaN(b) || float.IsNaN(cur[i])) continue;
                    float d = Mathf.Abs(cur[i] - b);
                    if (d < 0.05f) continue;
                    moved++; if (d >= 0.25f) moved25++;
                    hist[d < 0.25f ? 0 : d < 0.5f ? 1 : d < 1f ? 2 : d < 2f ? 3 : 4]++;
                    if (d > worst) { var E = map.lotEntrances[i]; worst = d; at = $"e{E.edge} s {E.s:0.0} '{map.edges[E.edge].name}'"; }
                }
                LineModel.SplitAxis = was;
                Line($"LOT ENTRANCES against scope 0 (the drawn pavement edge on the entrance's side): {moved} of {na} move 0.05 m or more ({moved25} 0.25 m or more; 0.05-0.25 {hist[0]}, 0.25-0.5 {hist[1]}, 0.5-1 {hist[2]}, 1-2 {hist[3]}, 2+ {hist[4]}){(moved > 0 ? $"; worst {worst:0.00} m at {at}" : "")}");
            }
            Line($"(splits audit {clock.Elapsed.TotalSeconds:0.0} s)");
            FinishTo("city_splits.txt");
        }

        /// <summary>Phase C: the drawn pavement edge (lateral off the OSM line) on a lot entrance's side.</summary>
        static float EntranceEdge(CityMap map, CityMap.LotEntrance E)
        {
            if (E.edge < 0 || E.edge >= map.edges.Length) return float.NaN;
            var e = map.edges[E.edge];
            float s = Mathf.Clamp(E.s, 0f, e.length);
            LineModel.Extents(e, s, out float eM, out float eP);
            return E.side >= 0 ? eP : -eM;
        }

        /// <summary>J1b's census (b) on the drawn eases: every one-sided taper and shift (not a profile, not a
        /// relay) of 5 cm or more, its length against MUTCD's for its move at the edge's speed: under L/2,
        /// under full L at 40 mph and above, under the 30.5 m floor.</summary>
        static void TaperCensus(CityMap map)
        {
            int n = 0, halfFail = 0, fullFail40 = 0, floorFail = 0, relays = 0;
            var worst = new List<(float r, string at)>();
            foreach (var e in map.edges)
            {
                if (e.lmEase == null) continue;
                float mph = e.speedKmh > 0 ? e.speedKmh / 1.609344f : e.link ? 35f : (e.cls >= 5 ? 65f : e.cls == 4 ? 55f : e.cls == 3 ? 45f : e.cls == 2 ? 40f : e.cls == 1 ? 35f : 25f);
                float rate = mph <= 40f ? mph * mph / 60f : mph;
                foreach (var z in e.lmEase)
                {
                    if (z.relay) { relays++; continue; }
                    if (z.prof != null || z.aux || z.d0 > 0.01f) continue;
                    float w = Mathf.Abs(z.dw);
                    if (w < 0.05f || z.len < 0.5f) continue;
                    n++;
                    float ratio = z.len / w;
                    bool hf = ratio < 0.5f * rate, ff = mph >= 37.5f && ratio < rate, fl = z.len < 30.4f && e.cls < 5;
                    if (hf) halfFail++;
                    if (ff) fullFail40++;
                    if (fl) floorFail++;
                    if (hf && worst.Count < 400) worst.Add((ratio / rate, $"e{e.index} '{e.name}' {(z.shift ? "shift" : "taper")} {w:0.00} m over {z.len:0.0} m = {ratio:0.0}:1 (r {rate:0.0}, {mph:0} mph)"));
                }
            }
            worst.Sort((a, b) => a.r.CompareTo(b.r));
            Line($"  TAPER CENSUS (J1b, the drawn one-sided tapers and shifts of 5 cm or more, {n}): under L/2 {halfFail}; under full L at 40 mph and above {fullFail40}; under the 30.5 m floor {floorFail} (relays {relays}, the line model's RelayShort {LineModel.RelayShort} of {LineModel.RelayJoins})");
            for (int i = 0; i < Mathf.Min(12, worst.Count); i++) Line("    steepest: " + worst[i].at);
        }

        static int Count(IEnumerable<int> e) { int n = 0; foreach (var _ in e) n++; return n; }

        static string EndLine(CityMap map, SplitEndM m) =>
            $"e{m.c} {m.kind} {m.mph:0}mph r {m.rate:0.0}: outer edge moves {m.W:+0.00;-0.00} m over {m.Lt:0.0} m = {m.rEq:0.0}:1 ({(m.fullOk ? "L ok" : m.halfOk ? "L/2 only" : "under L/2")}), peak slope {m.peak:0.000}, bulge {m.bulge:0.00}, jump {m.jump:0.00}; lanes' centre {m.q0:+0.00;-0.00} -> {m.qF:+0.00;-0.00}, overshoot {m.over:0.00}, angle at the node {m.angle:0.0} deg, U heading change {m.uHead:0.0} deg{m.info}{(m.pass ? "" : " FAIL:" + m.why)}";

        /// <summary>One carriageway end, on the drawn lane centre (see the class summary).</summary>
        static SplitEndM MeasureSplitEnd(CityMap map, CityMeshes.Trims t, System.Func<CityMap.Edge, int, int> partner, HashSet<int> splitNodes, int node, int ui, int ci)
        {
            var m = new SplitEndM { node = node, u = ui, c = ci, q20 = float.NaN, own20 = float.NaN, med20 = float.NaN };
            var U = map.edges[ui]; var C = map.edges[ci];
            if (U.a == U.b || C.a == C.b || (C.a != node && C.b != node) || (U.a != node && U.b != node)) return m;
            m.name = C.name;
            Vector2 tU = LineModel.SplitAxisChord(map, partner, U, node, 30f, out float got);
            var leftU = new Vector2(-tU.y, tU.x); var P0 = map.nodes[node];
            m.mph = LineModel.SplitSpeedMph(U); m.rate = LineModel.TaperRateAt(m.mph);
            var chain = new List<int>(16);
            LineModel.SplitChain(map, splitNodes, ci, node, 260f, chain, out int far, out float clen);
            m.far = far;
            if (chain.Count == 0 || clen < 15f) return m;
            bool refuge = LineModel.RefugeOf(ci) != null, framed = LineModel.SplitFrameOf(ci) != null;
            bool island = splitNodes.Contains(far) && clen <= LineModel.RefugeMaxM;
            m.kind = refuge ? "refuge" : framed ? "axis-frame" : island ? "short-island" : "long-split";
            if (t.patch[node]) m.kind += "(fan)";
            // the drawn lane centre and pavement edges along the chain
            var su = new List<float>(512); var sq = new List<float>(512); var sOut = new List<float>(512); var sIn = new List<float>(512);
            int from = node; bool first = true;
            for (int k = 0; k < chain.Count; k++)
            {
                var e = map.edges[chain[k]];
                bool fwd = e.a == from;
                int to = fwd ? e.b : e.a;
                float x0 = first ? t.TrimAt(e, from) : 0f, x1 = e.length - (k == chain.Count - 1 ? t.TrimAt(e, to) : 0f);
                for (float x = x0; x <= x1 + 1e-3f; x += 0.5f)
                {
                    float s = fwd ? x : e.length - x;
                    var p = LineModel.OwnLanePoint(e, s);
                    float uu = Vector2.Dot(p - P0, tU), qq = Vector2.Dot(p - P0, leftU);
                    if (su.Count > 0 && uu <= su[su.Count - 1] + 0.05f) continue;
                    LineModel.Extents(e, s, out float eM, out float eP);
                    var lf = LineModel.SectionLeft(e, s); var pe = e.PointAt(s);
                    float qa = Vector2.Dot(pe + lf * eP - P0, leftU), qb = Vector2.Dot(pe - lf * eM - P0, leftU);
                    su.Add(uu); sq.Add(qq);
                    sOut.Add(qa); sIn.Add(qb);   // (both edges; the outer one is chosen by side below)
                }
                first = false; from = to;
            }
            if (su.Count < 12) { m.kind = null; return m; }
            m.uStart = su[0]; m.uMax = su[su.Count - 1];
            m.q0 = sq[0];
            float qF = 0f; int nF = 0;
            for (int i = 0; i < su.Count; i++) if (su[i] >= m.uMax - 10f) { qF += sq[i]; nF++; }
            m.qF = qF / Mathf.Max(1, nF);
            m.W = m.qF - m.q0;
            // the carriageway's side of the axis: its lanes' centre over its first 5-30 m (the far end can
            // lie on the other side where the road curves away from the straight frame)
            float qNear = 0f; int nNear = 0;
            for (int i = 0; i < su.Count; i++) if (su[i] >= m.uStart + 5f && su[i] <= m.uStart + 30f) { qNear += sq[i]; nNear++; }
            qNear = nNear > 0 ? qNear / nNear : m.q0;
            float sideOut = Mathf.Abs(qNear) > 0.05f ? Mathf.Sign(qNear) : (Mathf.Abs(m.q0) > 0.05f ? Mathf.Sign(m.q0) : 1f);
            // the 95 % window
            float aW = Mathf.Abs(m.W), uA = float.NaN, uB = float.NaN;
            if (aW >= 0.1f)
                for (int i = 0; i < su.Count; i++)
                {
                    float f = (sq[i] - m.q0) / m.W;
                    if (float.IsNaN(uA) && f > 0.025f) uA = su[i];
                    if (float.IsNaN(uB) && f >= 0.975f) { uB = su[i]; break; }
                }
            m.Lt = float.IsNaN(uA) || float.IsNaN(uB) ? 0f : uB - uA;
            // the peak slope over 2 m, the overshoot, monotony
            float peak = 0f, over = 0f; int rev = 0; float lastDir = 0f, extreme = m.q0;
            for (int i = 0; i < su.Count; i++)
            {
                int j = i;
                while (j + 1 < su.Count && su[j] - su[i] < 2f) j++;
                if (su[j] - su[i] >= 1.5f) peak = Mathf.Max(peak, Mathf.Abs(sq[j] - sq[i]) / (su[j] - su[i]));
                over = Mathf.Max(over, sideOut * (sq[i] - m.qF));
            }
            for (int i = 4; i < su.Count; i += 4)
            {
                float d = sq[i] - sq[i - 4];
                if (Mathf.Abs(d) < 0.01f) continue;
                float dir = Mathf.Sign(d);
                if (lastDir != 0f && dir != lastDir && Mathf.Abs(sq[i] - extreme) > 0.05f) rev++;
                if (dir != lastDir) extreme = sq[i - 4];
                lastDir = dir;
            }
            m.mono = rev == 0;
            m.peak = peak; m.rEq = peak > 1e-5f ? 1.5f / peak : 9999f; m.over = over;
            // the outer edge: the one further out on the carriageway's own side
            for (int i = 0; i < sOut.Count; i++) { float qa = sOut[i], qb = sIn[i]; bool aOut = sideOut * qa >= sideOut * qb; sOut[i] = aOut ? qa : qb; sIn[i] = aOut ? qb : qa; }
            // the outer edge's bulge off the straight line between its ends (short islands)
            float bulge = 0f;
            if (sOut.Count > 2)
            {
                float o0 = sOut[0], o1 = sOut[sOut.Count - 1], u0 = su[0], u1 = su[su.Count - 1];
                for (int i = 0; i < su.Count; i++) { float lin = Mathf.Lerp(o0, o1, (su[i] - u0) / Mathf.Max(1e-3f, u1 - u0)); bulge = Mathf.Max(bulge, sideOut * (sOut[i] - lin)); }
            }
            m.bulge = bulge;
            // at u 20: the lane centre, and the own line (OSM + its lanes' offset, no split shift: the frame's, or OSM's here)
            int i20 = -1; for (int i = 0; i < su.Count; i++) if (su[i] >= 20f) { i20 = i; break; }
            if (i20 >= 0) m.q20 = sq[i20];
            var fr = LineModel.SplitFrameOf(ci);
            if (fr != null) m.own20 = fr.OwnQAt(20f);
            // the angle at the node: the undivided road's lane centre over its last 4 m against the carriageway's first 4 m
            {
                float sEndU = U.b == node ? U.length - t.TrimAt(U, node) : t.TrimAt(U, node);
                float sBackU = U.b == node ? Mathf.Max(0f, sEndU - 4f) : Mathf.Min(U.length, sEndU + 4f);
                var a0 = LineModel.OwnLanePoint(U, sBackU); var a1 = LineModel.OwnLanePoint(U, sEndU);
                int j4 = 0; while (j4 + 1 < su.Count && su[j4] - su[0] < 4f) j4++;
                var dirU = (a1 - a0); var dirC = new Vector2(su[j4] - su[0], sq[j4] - sq[0]);
                dirU = new Vector2(Vector2.Dot(dirU, tU), Vector2.Dot(dirU, leftU));
                m.angle = dirU.sqrMagnitude > 1e-6f && dirC.sqrMagnitude > 1e-6f ? Vector2.Angle(dirU, dirC) : 0f;
                // the outer edge's jump: the undivided road's drawn edge on that side at its end, against the carriageway's start
                LineModel.Extents(U, sEndU, out float uM, out float uP);
                var lfU = LineModel.SectionLeft(U, sEndU); var pu = U.PointAt(sEndU);
                float ua = Vector2.Dot(pu + lfU * uP - P0, leftU), ub = Vector2.Dot(pu - lfU * uM - P0, leftU);
                float uOut = sideOut * ua >= sideOut * ub ? ua : ub;
                m.jump = Mathf.Abs(sOut[0] - uOut);
                // the undivided road's heading change over its last 30 m (its lane centre)
                float sB30 = U.b == node ? Mathf.Max(0f, sEndU - 30f) : Mathf.Min(U.length, sEndU + 30f);
                float sB26 = U.b == node ? Mathf.Max(0f, sEndU - 26f) : Mathf.Min(U.length, sEndU + 26f);
                var h0 = LineModel.OwnLanePoint(U, sB26) - LineModel.OwnLanePoint(U, sB30);
                m.uHead = h0.sqrMagnitude > 1e-6f && dirU.sqrMagnitude > 1e-6f ? Vector2.Angle(new Vector2(Vector2.Dot(h0, tU), Vector2.Dot(h0, leftU)), dirU) : 0f;
            }
            // ---- THE CENSUS'S MEASURE (align/census census_a4, the plan's gates): the OUTER
            // pavement edge, the undivided road's approach (40 m back) then the carriageway,
            // laterally in the straight frame, the approach's own linear trend taken out; the
            // main move within 80 m (half a short island) and the length it takes from 5 %
            float sideO = sideOut;
            var ys = new List<(float u, float y)>(800);
            {
                // the approach: U and its mitred continuation, back 40 m from U's drawn end
                var cur = U; int at = node; float back = 0f;
                for (int g = 0; g < 6 && back < 40f; g++)
                {
                    bool toward = cur.b == at;
                    float sEnd = toward ? cur.length - (cur == U ? t.TrimAt(U, node) : 0f) : (cur == U ? t.TrimAt(U, node) : 0f);
                    for (float x = 0f; x <= cur.length && back + x <= 40f; x += 0.5f)
                    {
                        float s = toward ? sEnd - x : sEnd + x;
                        if (s < 0f || s > cur.length) break;
                        LineModel.Extents(cur, s, out float eM, out float eP);
                        var lf = LineModel.SectionLeft(cur, s); var pe = cur.PointAt(s);
                        Vector2 pa = pe + lf * eP, pb = pe - lf * eM;
                        float ya = sideO * Vector2.Dot(pa - P0, leftU), yb = sideO * Vector2.Dot(pb - P0, leftU);
                        var po = ya > yb ? pa : pb;
                        ys.Add((Vector2.Dot(po - P0, tU), Mathf.Max(ya, yb)));
                    }
                    back += Mathf.Abs(sEnd - (toward ? 0f : cur.length));
                    int farU = cur.a == at ? cur.b : cur.a;
                    int pi = partner(cur, farU);
                    if (pi < 0) break;
                    var pn = map.edges[pi]; if (pn == cur || pn.oneway) break;
                    cur = pn; at = farU;
                }
                for (int i = 0; i < su.Count; i++) ys.Add((su[i], sideO * sOut[i]));
            }
            ys.Sort((x1, x2) => x1.u.CompareTo(x2.u));
            float Near(float lo, float hi) { float sum = 0f; int n = 0; foreach (var y in ys) if (y.u >= lo && y.u <= hi) { sum += y.y; n++; } return n > 0 ? sum / n : float.NaN; }
            float yApp = Near(-25f, -15f); if (float.IsNaN(yApp)) yApp = Near(-10f, -1f); if (float.IsNaN(yApp)) yApp = ys[0].y;
            {
                int n = 0; float mu = 0f, my = 0f;
                foreach (var y in ys) if (y.u >= -30f && y.u <= -3f) { n++; mu += y.u; my += y.y; }
                if (n >= 10)
                {
                    mu /= n; my /= n; float sxy = 0f, sxx = 0f;
                    foreach (var y in ys) if (y.u >= -30f && y.u <= -3f) { sxy += (y.u - mu) * (y.y - my); sxx += (y.u - mu) * (y.u - mu); }
                    float bb = sxx > 0f ? sxy / sxx : 0f;
                    if (Mathf.Abs(bb) < 0.05f) { yApp = my - bb * mu; for (int i = 0; i < ys.Count; i++) ys[i] = (ys[i].u, ys[i].y - bb * ys[i].u); }
                }
            }
            float win = island ? 0.5f * (m.uMax - m.uStart) + m.uStart : Mathf.Min(80f, m.uMax);
            float yMax = float.MinValue, yMin = float.MaxValue, dMax = 0f, dMin = 0f, slope = 0f; int iMax = 0, iMin = 0;
            for (int i = 0; i < ys.Count; i++)
            {
                var y = ys[i];
                if (y.u >= 0f && y.u <= win) { if (y.y > yMax) { yMax = y.y; dMax = y.u; iMax = i; } if (y.y < yMin) { yMin = y.y; dMin = y.u; iMin = i; } }
                if (y.u < -20f || y.u > win) continue;
                int j = i; while (j + 1 < ys.Count && ys[j].u - y.u < 2f) j++;
                if (ys[j].u - y.u >= 1.5f) slope = Mathf.Max(slope, Mathf.Abs(ys[j].y - y.y) / (ys[j].u - y.u));
            }
            if (yMax == float.MinValue) { yMax = yApp; yMin = yApp; }
            float outMove = yMax - yApp, inMove = yApp - yMin; bool mainOut = outMove >= inMove;
            float Amain = Mathf.Max(0f, mainOut ? outMove : inMove); int iA = mainOut ? iMax : iMin; float dA = mainOut ? dMax : dMin;
            float dStart = Mathf.Max(-20f, ys[0].u);
            for (int i = iA; i >= 0; i--) { if (ys[i].u < -20f) break; if (Mathf.Abs(ys[i].y - yApp) <= 0.05f * Amain) { dStart = ys[i].u; break; } }
            float Lmove = Mathf.Max(0.5f, dA - dStart);
            float yEnd = ys[ys.Count - 1].y;
            m.W = mainOut ? Amain : -Amain; m.Lt = Lmove;
            m.peak = slope; m.rEq = Amain > 0.05f ? Lmove / Amain : 9999f;
            m.bulge = Mathf.Max(0f, yMax - Mathf.Max(yApp, island ? yApp : yEnd));
            // the verdict (the census's): fail L/2 where the main move of 0.3 m or more averages steeper
            // than r/2, L steeper than r; a bulge (outward overshoot) of 1 m or more; a 0.5 m jump
            m.halfOk = !(Amain >= 0.3f && m.rEq < 0.5f * m.rate);
            m.fullOk = !(Amain >= 0.3f && m.rEq < m.rate);
            bool slopeOk = m.mph <= 35.5f ? m.halfOk : m.fullOk;
            m.pass = slopeOk && m.bulge < 1f && m.jump < 0.5f;
            m.why = "";
            if (!slopeOk) m.why += $" move {Amain:0.00} m over {Lmove:0.0} m = {m.rEq:0.0}:1 < {(m.mph <= 35.5f ? m.rate * 0.5f : m.rate):0.0}:1;";
            if (m.bulge >= 1f) m.why += $" bulge {m.bulge:0.00} m;";
            if (m.jump >= 0.5f) m.why += $" outer-edge jump {m.jump:0.00} m;";
            return m;
        }

        /// <summary>The pavement gap between a node's two carriageways at axis u (inner edges, + = apart).</summary>
        static float MedianAt(CityMap map, CityMeshes.Trims t, int node, int ea, int eb, float uWant, Vector2 P0, Vector2 tU, Vector2 leftU)
        {
            var spn = new HashSet<int>(); foreach (var x in map.splt) spn.Add(x.node);
            float Inner(int e0, out float side)
            {
                side = 0f;
                float best = float.MaxValue, lat = float.NaN;
                var ch = new List<int>(8);
                LineModel.SplitChain(map, spn, e0, node, 200f, ch, out _, out _);
                foreach (int ei in ch)
                {
                var e = map.edges[ei];
                for (float s = 0f; s <= e.length; s += 0.25f)
                {
                    var p = e.PointAt(s);
                    float uu = Vector2.Dot(p - P0, tU);
                    if (Mathf.Abs(uu - uWant) > best) continue;
                    LineModel.Extents(e, s, out float eM, out float eP);
                    var lf = LineModel.SectionLeft(e, s);
                    float qa = Vector2.Dot(p + lf * eP - P0, leftU), qb = Vector2.Dot(p - lf * eM - P0, leftU);
                    best = Mathf.Abs(uu - uWant);
                    side = Mathf.Sign(qa + qb);
                    lat = side > 0f ? Mathf.Min(qa, qb) : Mathf.Max(qa, qb);
                }
                }
                return lat;
            }
            float a = Inner(ea, out float sa), b = Inner(eb, out float sb);
            if (float.IsNaN(a) || float.IsNaN(b)) return float.NaN;
            return sa > 0f ? a - b : b - a;
        }

        static void SpotLines(CityMap map, CityMeshes.Trims t, Dictionary<int, List<SplitEndM>> byNode)
        {
            (string id, int node, int far)[] spots = { ("34", 10238, 10236), ("35", 10229, 10234), ("36", 231, -1), ("37", 10035, -1) };
            foreach (var sp in spots)
            {
                if (sp.node >= map.nodes.Length || !LineModel.SplitUOf(map, sp.node, out int ui)) continue;
                LineModel.Split rec = default; foreach (var x in map.splt) if (x.node == sp.node) rec = x;
                var U = map.edges[ui];
                System.Func<CityMap.Edge, int, int> partner = (e, node) =>
                {
                    if (!t.mitre[node]) return -1;
                    int o = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
                    return o == e.index ? -1 : o;
                };
                var tU = LineModel.SplitAxisChord(map, partner, U, sp.node, 30f, out _);
                var leftU = new Vector2(-tU.y, tU.x); var P0 = map.nodes[sp.node];
                string s = $"  spot {sp.id} n{sp.node}:";
                if (byNode.TryGetValue(sp.node, out var l))
                    foreach (var m in l) s += $" e{m.c} [{m.kind}] q0 {m.q0:+0.00;-0.00} q20 {m.q20:+0.00;-0.00} own20 {m.own20:+0.00;-0.00} qF {m.qF:+0.00;-0.00} rate {m.rEq:0.0}:1 overshoot {m.over:0.00} bulge {m.bulge:0.00} angle {m.angle:0.0} mono {m.mono};";
                float med20 = MedianAt(map, t, sp.node, rec.a, rec.b, 20f, P0, tU, leftU);
                s += $" median at u 20: {med20:0.00} m";
                if (sp.far >= 0)
                {
                    var r = LineModel.RefugeOf(rec.a);
                    if (r != null) s += $"; refuge island wI {2f * r.half:0.00} (OSM {r.osmIsland:0.00}), x0 {r.x0s:0.00}/{r.x0e:0.00}";
                    float far = Vector2.Dot(map.nodes[sp.far] - P0, tU);
                    s += $"; island (pavement gap) at mid u {0.5f * far:0.0}: {MedianAt(map, t, sp.node, rec.a, rec.b, 0.5f * far, P0, tU, leftU):0.00} m";
                }
                if (sp.id == "37")
                {
                    // the nearest turn-only lane INTO the nose, back along the undivided road
                    float dist = 0f, found = float.NaN; var cur = U; int at = sp.node;
                    for (int g = 0; g < 10 && float.IsNaN(found); g++)
                    {
                        int into = cur.b == at ? 1 : -1;
                        if (LineModel.BayOf(cur, into)) { found = dist; break; }
                        dist += cur.length;
                        int far2 = cur.a == at ? cur.b : cur.a;
                        if (map.nodeEdges[far2].Count != 2 || dist > 400f) break;
                        int xi = map.nodeEdges[far2][0] == cur.index ? map.nodeEdges[far2][1] : map.nodeEdges[far2][0];
                        cur = map.edges[xi]; at = far2;
                        if (cur.oneway) break;
                    }
                    s += float.IsNaN(found) ? $"; no turn-only lane into the nose within {dist:0} m" : $"; a turn-only lane into the nose ends {found:0.0} m before it";
                }
                if (sp.id == "36")
                {
                    // the centre line's jog on the approach (the relay e8619/e7042/e8618): its lateral against u, south of the node
                    var yl = new List<(float u, float q)>();
                    var cur = U; int at = sp.node; var la = new List<LineModel.LineAt>(16);
                    for (int g = 0; g < 6; g++)
                    {
                        var lay = LineModel.LayoutOf(cur);
                        for (float x = 0f; x <= cur.length; x += 0.5f)
                        {
                            LineModel.LinesAt(cur, x, la);
                            float sum = 0f; int n = 0;
                            foreach (var li in la) if (lay.kind[li.k] == LineModel.KYellow || lay.kind[li.k] == LineModel.KYellowDash) { sum += li.lat; n++; }
                            if (n == 0) continue;
                            var p = cur.PointAt(x) + LineModel.SectionLeft(cur, x) * (sum / n);
                            yl.Add((Vector2.Dot(p - P0, tU), Vector2.Dot(p - P0, leftU)));
                        }
                        int far2 = cur.a == at ? cur.b : cur.a;
                        if (map.nodeEdges[far2].Count != 2) break;
                        int xi = map.nodeEdges[far2][0] == cur.index ? map.nodeEdges[far2][1] : map.nodeEdges[far2][0];
                        cur = map.edges[xi]; at = far2;
                        if (cur.oneway) break;
                    }
                    yl.Sort((a, b) => a.u.CompareTo(b.u));
                    // the jog: the largest lateral move of the centre line and the distance it takes (5-95 %)
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var y in yl) if (y.u > -160f && y.u < -10f) { lo = Mathf.Min(lo, y.q); hi = Mathf.Max(hi, y.q); }
                    float jog = hi - lo, ua = float.NaN, ub = float.NaN;
                    if (jog > 0.5f)
                    {
                        float q0 = float.NaN;
                        foreach (var y in yl) { if (y.u <= -160f || y.u >= -10f) continue; if (float.IsNaN(q0)) q0 = y.q; float f = Mathf.Abs(y.q - q0) / jog; if (float.IsNaN(ua) && f > 0.05f) ua = y.u; if (float.IsNaN(ub) && f >= 0.95f) ub = y.u; }
                    }
                    s += $"; centre-line jog on the approach {jog:0.00} m over {(float.IsNaN(ua) || float.IsNaN(ub) ? float.NaN : (ub - ua) / 0.9f):0.0} m (5-95 % scaled to the whole)";
                }
                Line(s);
            }
        }

        /// <summary>The plan polylines within 90 m of a node (axis frame is the sheet's job).</summary>
        /// <summary>The fix round (2026-11-16): per Beatties Ford node the profile frames, the nose's
        /// design (u, the lane gap, each side's inner pavement edge off its lane edge) and, from the
        /// tiles built over it with CityMeshes.CurbMedianTrace on, where the curbed island starts and
        /// why each section before it has none. PSX_SPLITS_POLES "x:z:r": the pole-line stations
        /// round a point, each foot tried (CityPoles.Explain).</summary>
        static void NoseLines(CityMap map, CityMeshes.Trims t)
        {
            Line("SPLIT NOSES (Beatties Ford nodes the profile frames; design: u -> lane gap, each side's inner pavement edge off its lane edge, 0.6 at the nose, half the gap = flush):");
            Dictionary<long, List<CityBuildings.B>> buildings = null;
            foreach (int n in LineModel.BfrSplitNodes)
            {
                if (!LineModel.SplitNoseTable.TryGetValue(n, out var tab)) continue;
                LineModel.SplitFrame fA = null, fB = null;
                foreach (var f in LineModel.SplitFrames) if (f.node == n) { if (fA == null) fA = f; else fB = f; }
                if (fA == null) continue;
                Line($"  n{n}: tip u {fA.uTip:0.0} (flank {fA.noseLn:0.0} m), shaped {LineModel.SplitNoseShaped(n)}; e{fA.edge} shIn {fA.shIn:0.00}" + (fB != null ? $", e{fB.edge} shIn {fB.shIn:0.00}" : ""));
                var sb = new StringBuilder("    ");
                float uT = float.IsNaN(fA.uTip) ? 40f : fA.uTip;
                foreach (var r in tab)
                    if (Mathf.Abs(r.u * 0.4f - Mathf.Round(r.u * 0.4f)) < 0.01f && r.u <= uT + 30f)
                        sb.Append($"u{r.u:0.0} gap {r.gap:0.00} off {r.offA:0.00}/{r.offB:0.00}; ");
                Line(sb.ToString());
                // the curb as the tiles lay it
                buildings ??= CityBuildings.Precompute(map);
                var edges = new HashSet<int>();
                foreach (var f in new[] { fA, fB }) if (f != null) for (int i = 0; i < Mathf.Min(4, f.chain.Length); i++) edges.Add(f.chain[i]);
                var rec = new List<(int e, float s, string why)>();
                var tiles = new HashSet<long>();
                for (float uu = -10f; uu <= uT + 40f; uu += 8f)
                {
                    var p = fA.p0 + fA.tU * uu;
                    tiles.Add(((long)Mathf.FloorToInt(p.x / CityMeshes.TileSize) << 32) | (uint)Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                }
                CityMeshes.CurbMedianTraceEdges = edges;
                CityMeshes.CurbMedianTrace = (ei, s, why) => rec.Add((ei, s, why));
                try { foreach (long k in tiles) CityMeshes.Build(map, t, buildings, (int)(k >> 32), (int)(uint)k); }
                finally { CityMeshes.CurbMedianTrace = null; CityMeshes.CurbMedianTraceEdges = null; }
                foreach (int ei in edges)
                {
                    var e = map.edges[ei];
                    var mine = rec.FindAll(r => r.e == ei);
                    mine.Sort((x, y) => x.s.CompareTo(y.s));
                    var runs = new StringBuilder();
                    string cur = null; float u0 = 0f, u1 = 0f; int nr = 0;
                    foreach (var r in mine)
                    {
                        string key = r.s < 0f ? r.why : r.why.StartsWith("island") ? "island" : r.why.StartsWith("ray:") ? "ray" : r.why;
                        float uu = r.s < 0f ? float.NaN : Vector2.Dot(e.PointAt(r.s) - fA.p0, fA.tU);
                        if (key != cur)
                        {
                            if (cur != null && nr++ < 24) runs.Append($" [{u0:0.0}..{u1:0.0} {cur}]");
                            cur = key; u0 = uu;
                        }
                        u1 = uu;
                        if (key == "ray" && nr < 24 && runs.Length < 1500) runs.Append($" ({uu:0.0}: {r.why})");
                    }
                    if (cur != null) runs.Append($" [{u0:0.0}..{u1:0.0} {cur}]");
                    Line($"    curb on e{ei} (u along the axis):{(mine.Count == 0 ? " no section judged" : runs.ToString())}");
                }
            }
            var spec = System.Environment.GetEnvironmentVariable("PSX_SPLITS_POLES");
            if (!string.IsNullOrEmpty(spec))
            {
                var inv = CultureInfo.InvariantCulture;
                var f = spec.Split(':');
                if (f.Length == 3)
                {
                    float px = float.Parse(f[0], inv), pz = float.Parse(f[1], inv), pr = float.Parse(f[2], inv);
                    buildings ??= CityBuildings.Precompute(map);
                    var sb = new StringBuilder();
                    CityPoles.Explain(map, t, buildings, new Vector2(px - pr, pz - pr), new Vector2(px + pr, pz + pr), sb);
                    Line($"POLES with a station within {pr:0} m of ({px:0.0},{pz:0.0}):");
                    foreach (var l in sb.ToString().Split('\n')) if (l.Trim().Length > 0) Line("  " + l.TrimEnd());
                }
            }
        }

        static void PlanDump(CityMap map, CityMeshes.Trims t, int node, string tag, StringBuilder sb, CultureInfo inv)
        {
            var c = map.nodes[node];
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(c - Vector2.one * 90f, c + Vector2.one * 90f, segs);
            var es = new HashSet<int>();
            foreach (int packed in segs) es.Add(packed >> 12);
            var la = new List<LineModel.LineAt>(16);
            void P(string kind, int e, int seq, Vector2 p) => sb.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t{4}\t{5:0.00}\t{6:0.00}\n", tag, node, kind, e, seq, p.x, p.y));
            P("node", -1, 0, c);
            // the axis: the undivided road's 30 m chord into the node (the sheet's frame)
            {
                int ui = -1;
                if (LineModel.SplitUOf(map, node, out int u0)) ui = u0;
                else foreach (int ei in map.nodeEdges[node]) { var e = map.edges[ei]; if (!e.oneway && e.name == "Beatties Ford Road") ui = ei; }
                if (ui >= 0)
                {
                    System.Func<CityMap.Edge, int, int> partner = (e, nd) =>
                    {
                        if (!t.mitre[nd]) return -1;
                        int o = t.throughA[nd] == e.index ? t.throughB[nd] : t.throughB[nd] == e.index ? t.throughA[nd] : -1;
                        return o == e.index ? -1 : o;
                    };
                    var tU = LineModel.SplitAxisChord(map, partner, map.edges[ui], node, 30f, out _);
                    P("axis", ui, 0, c); P("axis", ui, 1, c + tU * 10f);
                }
            }
            foreach (int ei in es)
            {
                var e = map.edges[ei];
                if (e.a == e.b || e.tunnel) continue;
                for (int k = 0; k < e.pts.Length; k++) P("osm", ei, k, e.pts[k]);
                float s0 = t.TrimAt(e, e.a), s1 = e.length - t.TrimAt(e, e.b);
                if (s1 - s0 < 0.5f) continue;
                int seq = 0;
                var lay = LineModel.LayoutOf(e);
                for (float s = s0; s <= s1 + 1e-3f; s += 1f, seq++)
                {
                    float ss = Mathf.Min(s, s1);
                    LineModel.Extents(e, ss, out float eM, out float eP);
                    var lf = LineModel.SectionLeft(e, ss); var p = e.PointAt(ss);
                    if ((p - c).sqrMagnitude > 100f * 100f) continue;
                    P("edgeP", ei, seq, p + lf * eP); P("edgeM", ei, seq, p - lf * eM);
                    P("lane", ei, seq, LineModel.OwnLanePoint(e, ss));
                    LineModel.LinesAt(e, ss, la);
                    foreach (var l in la)
                    {
                        byte kd = lay.kind[l.k];
                        string kn = kd == LineModel.KYellow || kd == LineModel.KYellowDash ? "lineY" : kd == LineModel.KWhiteDash ? "lineW" : "lineE";
                        P(kn + l.k, ei, seq, p + lf * l.lat);
                    }
                }
            }
        }
    }
}
