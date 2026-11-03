using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MEDIANS PASS GATES (P2 refuge islands + turn paint, P3 median
    /// surface; round 2, 2026-11-02). Headless, seconds: the graph, the trims
    /// twice (PSX_CITY_REFUGE on, then off) and the marks, no tiles. Writes
    /// city_medians.txt. Every refuge carriageway is measured in its island's
    /// own frame (LineModel.Refuge: the axis carried through between the two
    /// split nodes, lateral + outward), the drawn geometry of ON and of OFF:
    ///   * outer edge: its deviation from the undivided road's edge line
    ///     (centre-lane islands: goal 0.05 m), or (4-lane) its move;
    ///   * lane lines (LinesAt) and the traffic path (OwnLanePoint): the most
    ///     any one moves laterally against the undivided road's edge line
    ///     along an edge (centre-lane goal 0.02 m, 4-lane 1.3 m) and the
    ///     largest lateral slope of any lane edge, line or path (goal 0.06);
    ///   * the island (inner pavement edge to inner pavement edge): at mid
    ///     against the design wI (goal 0.1 m), never narrowing from a nose
    ///     to mid, none before the tip;
    ///   * the step in a drawn edge at every chain joint and where the chain
    ///     leaves its split node (goal: no 0.3 m step, none worse than OFF);
    ///     the lane lines on every chain edge (goal: as many on as off);
    ///   * ARROWS INTO A NOSE (goal 0) and the nose hatches;
    ///   * MEDIAN CELLS: CityMeshes.MedianCellCensus.
    /// </summary>
    public static partial class CityAudit
    {
        // ROUND 5 (2026-11-03): PER-PAIR ALL-OR-NOTHING, DECIDED HERE. The gate
        // builds live (OFF), then DECIDES: every refuge pair the detection
        // finds is drawn, measured on the drawn geometry (MeasureRefuges +
        // RefugeFails + PairFails), and each failing pair is tried once flush,
        // then reverted to live; rebuilt and measured again until nothing
        // fails (a revert can change a neighbour). The accepted pairs are
        // written as refuge_list.generated.cs (copy it over
        // Scripts/City/LineModel.RefugeList.cs), and the gate then VERIFIES the
        // compiled list: same city export (hash), same pairs and modes, every
        // drawn pair passing, every reverted pair's edges drawing exactly as
        // live. A stale list fails the gate.
        public static void RunMedianGates()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); FinishTo("city_medians.txt"); return; }
            bool refWas = LineModel.RefugeOn;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // ---- OFF: live ----
            LineModel.RefugeOn = false; LineModel.RefugeOverride = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tOff = CityMeshes.ComputeTrims(map);
            float offMs = (float)sw.Elapsed.TotalMilliseconds;
            var offEase = new LineModel.Ease[map.edges.Length][];
            for (int i = 0; i < map.edges.Length; i++) offEase[i] = map.edges[i].lmEase;
            // the build must be repeatable: a second OFF build draws the same eases
            CityMeshes.ComputeTrims(map);
            int detDiff = 0; string detAt = "";
            for (int i = 0; i < map.edges.Length; i++)
                if (!EasesSame(map.edges[i].lmEase, offEase[i]))
                {
                    detDiff++;
                    if (detAt.Length < 1500) detAt += $"\n    e{i} '{map.edges[i].name}': {EaseDump(offEase[i])} || {EaseDump(map.edges[i].lmEase)}";
                }
            Line($"REPEATABILITY: a second OFF build differs from the first on {detDiff} edges{detAt}");
            CityMeshes.EnsureMarks(map, tOff);
            int noseOff = CityMeshes.MarkStats.noseArrows, groupsOff = CityMeshes.MarkStats.groups;
            // ---- DECIDE ----
            LineModel.RefugeOn = true;
            var why = new Dictionary<long, string>();
            var decided = DecideRefuges(map, offEase, why, out int iters, out var cands, out int noneDiff, out string decideLog);
            string cityHash = CityHash();
            string gen = RefugeListSource(map, decided, cands, cityHash);
            System.IO.File.WriteAllText("refuge_list.generated.cs", gen);
            // ---- VERIFY: the compiled list ----
            LineModel.RefugeOverride = null;
            sw.Restart();
            var tOn = CityMeshes.ComputeTrims(map);
            float onMs = (float)sw.Elapsed.TotalMilliseconds, buildMs = LineModel.RefugeBuildMs;
            var refs = new List<LineModel.Refuge>(LineModel.Refuges);
            var seps = new List<LineModel.Refuge>(LineModel.Separated);
            int notDrawn = LineModel.RefugeNotDrawn.Count, staleFp = LineModel.RefugeStaleFp; bool stale = LineModel.RefugeListStale;
            Line($"REFUGE ISLANDS round 5 (PSX_CITY_REFUGE; per-pair all-or-nothing): {cands.Count} pairs found; decided in {iters + 1} builds: {decided.Count} accepted ({Count(decided, LineModel.RefugeModeAuto)} as the profile draws them, {Count(decided, LineModel.RefugeModeFlush)} forced flush), {cands.Count - decided.Count} reverted to live");
            Line(decideLog);
            // the list in the build is the list just decided, for this city export
            int listDiff = 0; string listDiffAt = "";
            var drawn = new Dictionary<long, byte>();
            for (int i = 0; i < refs.Count; i += 2) drawn[refs[i].pk] = refs[i].mode;
            foreach (var kv in decided) if (!drawn.TryGetValue(kv.Key, out byte md) || md != kv.Value) { listDiff++; if (listDiffAt.Length < 300) listDiffAt += $" {PairName(map, cands, kv.Key)} decided {kv.Value} drawn {(drawn.ContainsKey(kv.Key) ? drawn[kv.Key].ToString() : "-")};"; }
            foreach (var kv in drawn) if (!decided.ContainsKey(kv.Key)) { listDiff++; if (listDiffAt.Length < 300) listDiffAt += $" {PairName(map, cands, kv.Key)} drawn, not accepted;"; }
            Line($"  ACCEPTED LIST (LineModel.RefugeList.cs): city export {(LineModel.RefugeListCity == cityHash ? "matches" : $"STALE (list {LineModel.RefugeListCity}, bytes {cityHash})")}; edge/node counts {(stale ? "STALE" : "match")}; {staleFp} listed pairs with a changed fingerprint; {notDrawn} listed pairs the builder could not draw; {listDiff} pairs differ from this run's decision{listDiffAt}. This run's list: refuge_list.generated.cs");
            Check(LineModel.RefugeListCity == cityHash && !stale && staleFp == 0 && notDrawn == 0 && listDiff == 0, "the compiled accepted list is this city export's and this gate's decision (else regenerate: copy refuge_list.generated.cs over LineModel.RefugeList.cs)", listDiff + staleFp + notDrawn + (stale ? 1 : 0));
            Check(noneDiff == 0, "with no pair listed (a stale list's fail-safe) every edge's eases are live's exactly", noneDiff);
            Line($"  LOAD COST: the refuge pairs' build {buildMs:0.0} ms (FindRefuges: frames, profiles, eases); the trims ON {onMs:0} ms vs OFF {offMs:0} ms (editor, one run each); no self-check at load");
            Check(buildMs < 50f, "refuge pairs add under 50 ms at city load", $"{buildMs:0.0} ms");

            var mOn = LineModel.MeasureRefuges(map, refs, out float stepOn, out int linesOn, out string worstOn);
            var pf = PairFails(map, tOn, refs, mOn, offEase, out int relayN, out int relayWorse, out int relayLoose, out float relayWorst, out string relayAt);
            string pfAt = "";
            foreach (var kv in pf) if (pfAt.Length < 1500) pfAt += $"\n    {PairName(map, cands, kv.Key)}: {kv.Value}";
            Check(pf.Count == 0, "every drawn refuge pair passes every check on the drawn geometry (MeasureRefuges/RefugeFails/PairFails)", pf.Count + pfAt);
            Line($"  RELAY + SHIFT CENSUS (every edge outside the refuge chains whose eases differ from live): {relayN} edges; ON steeper than max(0.06, OFF + 0.005) on {relayWorse} ({relayLoose} of them near no drawn pair){(relayWorse > 0 ? $"; worst {relayWorst:0.000} at {relayAt}" : "")}");
            Check(relayWorse == 0, "relay / common-mode shift census: no edge's lane line, edge or path steeper than live (or 0.06)", relayWorse);

            // round 3/4 checks on the drawn pairs
            string wedge = NoseWedges(map, tOn, refs, out float wedgeMax, out int wedgeBad, out int wedgeMet, out int wedgeFan);
            string heading = Headings(map, refs, out float headOn, out float headOsm, out int headN);
            string diag = DiagPairs(map, refs);
            CityMeshes.EnsureMarks(map, tOn);
            var ms = CityMeshes.MarkStats;
            int noseOn = ms.noseArrows, noseSkip = ms.noseSkipped, groupsOn = ms.groups;
            int flushTipKerbed = 0, flushTipFlush = 0;
            foreach (int n in CityMeshes.NoseFlushTip)
            {
                LineModel.Refuge rr = null;
                foreach (var r in refs) if (r.node == n || r.far == n) { rr = r; break; }
                if (rr != null && rr.half > 0f) flushTipKerbed++; else flushTipFlush++;
            }
            string hatchDump = HatchDump(map, new HashSet<int> { 10229, 10230, 10231, 10232, 10236, 10238 });
            string bendDump = BendCells(map, new[] { 14094, 20695 });
            var noseYs = new HashSet<long>(ms.noseYs);
            string hatch = $"{ms.noseHatches} refuge noses hatched ({ms.noseHatchBars} bars, at least {CityMeshes.HatchMinBars} each; {ms.noseSolid} bounding lines forced solid, {noseYs.Count} of them yellow); {ms.noseFlushTip} refuge noses with a centre lane that could not be hatched ({flushTipKerbed} on kerbed islands: the kerb kept back {CityMeshes.KerbFlushTipM:0} m from the tip; {flushTipFlush} on flush painted medians: no kerb at all); {ms.noseNoCentre} refuge noses with no centre lane; {ms.noseNonRefuge} plain noses of no drawn refuge pair left as live (no hatch, no flush tip, live's kerb)";
            // render-time counts (review r4 #7): build the tiles under every drawn pair
            var tiles = new HashSet<long>();
            foreach (var r in refs)
                foreach (int n in new[] { r.node, r.far })
                {
                    var p = map.nodes[n];
                    for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                        tiles.Add(TileKey(Mathf.FloorToInt((p.x + dx * 40f) / CityMeshes.TileSize), Mathf.FloorToInt((p.y + dz * 40f) / CityMeshes.TileSize)));
                }
            CityMeshes.YsDrawnKeys = new HashSet<long>(); CityMeshes.YsFallbackKeys = new HashSet<long>();
            CityMeshes.KerbFanEnds = 0; CityMeshes.KerbFanEndMaxH = 0f;
            var noBld = new Dictionary<long, List<CityBuildings.B>>();
            sw.Restart();
            foreach (long k in tiles)
            {
                var tm = CityMeshes.Build(map, tOn, noBld, (int)(k >> 32), (int)(uint)k);
                foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings })
                    if (m != null) Object.DestroyImmediate(m);
            }
            float tileMs = (float)sw.Elapsed.TotalMilliseconds;
            int ysOk = 0, ysMissing = 0, ysFell = 0;
            foreach (long key in noseYs)
            {
                bool d = CityMeshes.YsDrawnKeys.Contains(key), f = CityMeshes.YsFallbackKeys.Contains(key);
                if (f) ysFell++; else if (d) ysOk++; else ysMissing++;
            }
            int ysDrawnSpans = CityMeshes.MarkStats.ysDrawn, ysFallbackSpans = CityMeshes.MarkStats.ysFallback;
            CityMeshes.YsDrawnKeys = null; CityMeshes.YsFallbackKeys = null;
            Line($"  RENDER-TIME HATCH LINES ({tiles.Count} tiles built, {tileMs / 1000f:0.0} s): of {noseYs.Count} nose lines forced solid yellow, {ysOk} rendered solid, {ysFell} fell back to the layout's line, {ysMissing} not met in the tiles; spans: ysDrawn {ysDrawnSpans}, ysFallback {ysFallbackSpans}");
            Check(ysFell == 0 && ysFallbackSpans == 0 && ysMissing == 0, "every forced-solid yellow nose line renders solid (counted at render)", ysFell + ysMissing);
            Line($"  KERB FAN ENDS (a junction fan trims a kerbed carriageway past its island tip): {CityMeshes.KerbFanEnds} end spans; highest kerb top at a drawn end {CityMeshes.KerbFanEndMaxH * 100f:0.0} cm (goal 0: it ramps up over {CityMeshes.KerbEndRampM} m)");
            Check(CityMeshes.KerbFanEndMaxH <= 0.01f, "no fan end leaves a kerb face standing at the drawn end", $"{CityMeshes.KerbFanEndMaxH:0.000} m");
            string cells = CityMeshes.MedianCellCensus(map);
            string timing = PavedTiming(map);

            // reverted pairs: their edges must draw exactly as live
            var revs = new StringBuilder(); int nRev = 0, revDiff = 0;
            var accEdges = new HashSet<int>();
            foreach (var r in refs) { foreach (int ei in r.chain) accEdges.Add(ei); foreach (int ei in r.uEdges) accEdges.Add(ei); }
            foreach (var c in cands)
            {
                if (decided.ContainsKey(c.pk)) continue;
                nRev++;
                var es = new List<int>(c.ca); es.AddRange(c.cb); es.Add(c.u1); es.Add(c.u2);
                string d = "";
                foreach (int ei in es) if (!EasesSame(map.edges[ei].lmEase, offEase[ei])) d += $" e{ei}{(accEdges.Contains(ei) ? "(a drawn pair's)" : "")}";
                if (d.Length > 0) revDiff++;
                var p = map.nodes[c.n1];
                revs.Append($"\n    n{c.n1}-n{c.n2} e{c.e1}/e{c.e2} '{map.edges[c.e1].name}' ({LatLon(p.x, p.y)}; x {p.x:0} z {p.y:0}): {(d.Length == 0 ? "unchanged from live" : "eases differ from live on" + d)}; needs lane-add modelling. Failed: {(why.TryGetValue(c.pk, out var w) ? w : "?")}");
            }
            Line($"REVERTED PAIRS (drawn as live): {nRev}{revs}");
            Check(revDiff == 0, "every reverted pair's chain and undivided edges draw exactly as live", revDiff);
            var accs = new StringBuilder();
            foreach (var c in cands)
            {
                if (!decided.TryGetValue(c.pk, out byte md)) continue;
                LineModel.Refuge r = null;
                foreach (var q in refs) if (q.pk == c.pk) { r = q; break; }
                var p = map.nodes[c.n1];
                accs.Append($"\n    n{c.n1}-n{c.n2} '{map.edges[c.e1].name}' ({LatLon(p.x, p.y)}): {(r == null ? "?" : r.half <= 0f ? "flush painted median" : r.move > 0f ? $"4-lane island wI {2f * r.half:0.00}" : $"island wI {2f * r.half:0.00}")}{(md == LineModel.RefugeModeFlush ? " (forced flush; the island failed: " + (why.TryGetValue(c.pk, out var w) ? w : "?") + ")" : "")}");
            }
            Line($"ACCEPTED PAIRS: {decided.Count}{accs}");

            // OFF -> ON on the accepted pairs (OFF: live, measured in the same frames)
            LineModel.RefugeOn = false;
            var tOff2 = CityMeshes.ComputeTrims(map);
            var mOff = LineModel.MeasureRefuges(map, refs, out float stepOff, out int linesOff, out string worstOff);
            LineModel.RefugeOn = refWas; LineModel.RefugeOverride = null;

            string Q(List<float> v)
            {
                v.RemoveAll(float.IsNaN);
                if (v.Count == 0) return "-";
                v.Sort();
                float P(float p) => v[Mathf.Clamp(Mathf.FloorToInt(p * (v.Count - 1)), 0, v.Count - 1)];
                return $"{P(0.1f):0.000}/{P(0.5f):0.000}/{P(0.9f):0.000} (min {v[0]:0.000}, max {v[v.Count - 1]:0.000}, n {v.Count})";
            }
            List<float> Col(List<LineModel.RefugeM> m, System.Func<LineModel.RefugeM, float> f, System.Func<int, bool> keep)
            {
                var l = new List<float>();
                for (int i = 0; i < m.Count; i++) if (keep(i)) l.Add(f(m[i]));
                return l;
            }
            bool All(int i) => true;
            bool Centre(int i) => refs[i].move <= 0f && refs[i].half > 0f;
            bool Four(int i) => refs[i].move > 0f;
            bool Kerbed(int i) => refs[i].half > 0f;
            bool Flush(int i) => refs[i].half <= 0f;
            int nCentre = 0, nFour = 0, nFlush = 0;
            for (int i = 0; i < refs.Count; i++) { if (Centre(i)) nCentre++; if (Four(i)) nFour++; if (Flush(i)) nFlush++; }
            Line($"OFF -> ON ON THE ACCEPTED PAIRS ({refs.Count / 2} pairs, {refs.Count} carriageways: {nCentre} beside a centre lane, {nFour} 4-lane with the lanes moved out, {nFlush} flush painted median; p10/p50/p90):");
            Line($"  outer edge off the undivided road's edge line, centre-lane: OFF {Q(Col(mOff, m => m.outerDev, Centre))}; ON {Q(Col(mOn, m => m.outerDev, Centre))} (goal 0.05)");
            Line($"  outer edge's move, 4-lane: OFF {Q(Col(mOff, m => m.outerDev, Four))}; ON {Q(Col(mOn, m => m.outerDev, Four))} (goal 1.3)");
            Line($"  lane line move against the lanes' straight chord, centre-lane: OFF {Q(Col(mOff, m => m.lineMove, Centre))}; ON {Q(Col(mOn, m => m.lineMove, Centre))} (goal 0.02)");
            Line($"  TOTAL lane line move against the undivided road's edge line (planned 4-lane move included), all: OFF {Q(Col(mOff, m => m.lineMoveO, All))}; ON {Q(Col(mOn, m => m.lineMoveO, All))} (goal 1.3)");
            Line($"  TOTAL traffic path move against that edge line, all: OFF {Q(Col(mOff, m => m.pathMoveO, All))}; ON {Q(Col(mOn, m => m.pathMoveO, All))} (goal 1.3)");
            Line($"  largest lateral slope of a lane edge / line / path on the chain: OFF {Q(Col(mOff, m => m.slope, All))}; ON {Q(Col(mOn, m => m.slope, All))} (goal 0.06)");
            Line($"  largest slope on the undivided edges a common-mode shift reaches (strict, drawn): OFF {Q(Col(mOff, m => m.uSlope, All))}; ON {Q(Col(mOn, m => m.uSlope, All))} (goal 0.06)");
            Line($"  island at mid minus design wI, kerbed: ON {Q(Col(mOn, m => m.islandErr, Kerbed))} (goal 0.1); island at mid OFF {Q(Col(mOff, m => m.island, Kerbed))}, ON {Q(Col(mOn, m => m.island, Kerbed))}");
            Line($"  island narrowing toward mid: ON {Q(Col(mOn, m => m.nonMono, Kerbed))}; widest before the tip: ON {Q(Col(mOn, m => m.noseW, Kerbed))}");
            Line($"  inner edge to inner edge between the tips, least (raw, below 0 = overlap): OFF {Q(Col(mOff, m => m.overlap, All))}; ON {Q(Col(mOn, m => m.overlap, All))} (goal >= -0.01); in the {LineModel.RefugeTipM} m tip zones (pavements joining the undivided road's): ON {Q(Col(mOn, m => m.overlapTip, All))}");
            Line($"  kerbed islands: inner edge's least distance from the axis (below 0 = across it): ON {Q(Col(mOn, m => m.innerMin, Kerbed))} (goal >= -0.01)");
            Line($"  largest step in a drawn edge at a chain joint or its split node: OFF {stepOff:0.000} m ({worstOff}), ON {stepOn:0.000} m ({worstOn}) (goal < 0.3)");
            Line($"  lane lines on the chain edges at their mids: OFF {linesOff}, ON {linesOn}");
            Check(stepOn < 0.3f, "no 0.3 m step on a drawn refuge carriageway", $"{stepOn:0.000}");
            Check(linesOn == linesOff, "no lane line lost on a drawn refuge carriageway", $"{linesOn} vs {linesOff}");
            var spots = new HashSet<int> { 10229, 10234, 10230, 10233, 10231, 10232, 10236, 10238, 13570, 13571 };
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (!spots.Contains(r.node)) continue;
                var p = map.nodes[r.node];
                var a = mOff[i]; var b = mOn[i];
                Line($"  n{r.node}->n{r.far} e{r.edge} '{map.edges[r.edge].name}' Lc {r.lc:0.0} ({LatLon(p.x, p.y)}) {(r.half <= 0f ? "flush" : r.move > 0f ? $"4-lane move {r.move:0.00} over {r.moveLen:0} m" : "centre lane")}: wI {2f * r.half:0.00}" +
                     $" | OFF island {a.island:0.00} outer {a.outerDev:0.00} lines {a.lineMoveO:0.00} slope {a.slope:0.000} | ON island {b.island:0.00} outer {b.outerDev:0.000} lines {b.lineMove:0.000}/{b.lineMoveO:0.000} path {b.pathMoveO:0.000} slope {b.slope:0.000}");
            }
            Line($"ARROWS INTO A NOSE (every plain split nose, round 1's TurnMarks rule - city-wide, not per pair): turn-only arrow groups painted into a plain split's nose OFF {noseOff}, ON {noseOn} ({noseSkip} lanes skipped); all arrow groups OFF {groupsOff}, ON {groupsOn}");
            Check(noseOn == 0, "no turn arrow painted into a plain split nose", noseOn);
            Line($"NOSE APPROACHES: {hatch}");
            Line(cells);
            Line(bendDump);
            Line($"NOSE WEDGES (one section normal at a drawn split node): {wedgeMet} carriageway ends meet the undivided road's end at the node, {wedgeFan} meet a fan; largest corner off the undivided road's cut {wedgeMax:0.000} m{wedge}");
            Check(wedgeBad == 0, "no wedge (over 0.02 m) between a drawn refuge carriageway and the undivided road at its nose", wedgeBad);
            Line($"TRAFFIC HEADING on refuge carriageways ({headN} samples): heading vs the lane path's own direction, max {headOn:0.00} deg (OSM tangent: max {headOsm:0.00} deg){heading}");
            Check(headOn <= 2f, "traffic heading within 2 degrees of the path on refuge carriageways", $"{headOn:0.00} deg");
            Line(timing);
            Line($"SEPARATED MEDIANS found (real, not islands: drawn as live): {seps.Count / 2}");
            Line(hatchDump);
            if (diag.Length > 0) Line(diag);
            Line($"(gate {clock.Elapsed.TotalSeconds:0} s)");
            FinishTo("city_medians.txt");
        }

        static int Count(Dictionary<long, byte> d, byte v) { int n = 0; foreach (var x in d.Values) if (x == v) n++; return n; }

        static string PairName(CityMap map, List<LineModel.RefugeCand> cands, long pk)
        {
            foreach (var c in cands) if (c.pk == pk) return $"n{c.n1}-n{c.n2} '{map.edges[c.e1].name}'";
            return $"pair {pk}";
        }

        /// <summary>FNV-1a 64 of charlotte_city.bytes (the export the list is for).</summary>
        static string CityHash()
        {
            var ta = CharlotteData.Load("charlotte_city");
            if (ta == null) return "missing";
            ulong h = 14695981039346656037UL;
            foreach (byte b in ta.bytes) { h ^= b; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        static string RefugeListSource(CityMap map, Dictionary<long, byte> decided, List<LineModel.RefugeCand> cands, string hash)
        {
            var sb = new StringBuilder();
            sb.Append("// GENERATED by PSXRacing.EditorTools.CityAudit.RunMedianGates (medians round 5) - do not edit by hand.\r\n");
            sb.Append("// The refuge pairs that passed every medians gate on the drawn geometry; any\r\n");
            sb.Append("// pair not listed (or whose fingerprint no longer matches) is drawn as live.\r\n");
            sb.Append("namespace PSXRacing.City\r\n{\r\n    public static partial class LineModel\r\n    {\r\n");
            sb.Append($"        const int RefugeListEdges = {map.edges.Length}, RefugeListNodes = {map.nodes.Length};\r\n");
            sb.Append("        /// <summary>FNV-1a 64 of charlotte_city.bytes the list was decided on (the gate checks it).</summary>\r\n");
            sb.Append($"        public const string RefugeListCity = \"{hash}\";\r\n");
            sb.Append("        /// <summary>Triples: pair key (RefugePairKey), fingerprint, mode (1 auto, 2 flush).</summary>\r\n");
            sb.Append("        static readonly long[] RefugeListData = {\r\n");
            foreach (var c in cands)
                if (decided.TryGetValue(c.pk, out byte md))
                    sb.Append($"            {c.pk}L, {c.fp}L, {md},   // n{c.n1}-n{c.n2} {map.edges[c.e1].name.Replace("\\", "").Replace("\n", " ")}\r\n");
            sb.Append("        };\r\n    }\r\n}\r\n");
            return sb.ToString();
        }

        /// <summary>The decision: every pair drawn (the profile's own choice),
        /// measured; each failing pair tried once forced flush (if it was an
        /// island), then reverted; repeated until nothing fails. noneDiff: with
        /// no pair listed, the edges whose eases differ from live.</summary>
        static Dictionary<long, byte> DecideRefuges(CityMap map, LineModel.Ease[][] offEase, Dictionary<long, string> why, out int iters,
                                                    out List<LineModel.RefugeCand> cands, out int noneDiff, out string log)
        {
            var sb = new StringBuilder();
            LineModel.RefugeOverride = new Dictionary<long, byte>();
            CityMeshes.ComputeTrims(map);
            cands = new List<LineModel.RefugeCand>(LineModel.RefugeCandidates);
            noneDiff = 0;
            for (int i = 0; i < map.edges.Length; i++) if (!EasesSame(map.edges[i].lmEase, offEase[i])) noneDiff++;
            var mode = new Dictionary<long, byte>();
            foreach (var c in cands) mode[c.pk] = LineModel.RefugeModeAuto;
            iters = 0;
            for (; iters < 20; iters++)
            {
                LineModel.RefugeOverride = new Dictionary<long, byte>(mode);
                var tD = CityMeshes.ComputeTrims(map);
                var bad = new Dictionary<long, string>();
                foreach (var r in LineModel.RefugeNotDrawn) bad[r.pk] = r.separated ? $"a real separated median, not an island ({r.why})" : $"no island frame ({r.why})";
                var refs = new List<LineModel.Refuge>(LineModel.Refuges);
                var m = LineModel.MeasureRefuges(map, refs, out _, out _, out _);
                var pfs = PairFails(map, tD, refs, m, offEase, out _, out _, out int loose, out _, out _);
                foreach (var kv in pfs) bad[kv.Key] = kv.Value;
                int toFlush = 0, toLive = 0;
                foreach (var kv in bad)
                {
                    bool island = false;
                    foreach (var r in refs) if (r.pk == kv.Key && r.half > 0f) island = true;
                    if (mode.TryGetValue(kv.Key, out byte md) && md == LineModel.RefugeModeAuto && island) { mode[kv.Key] = LineModel.RefugeModeFlush; why[kv.Key] = "island:" + kv.Value; toFlush++; }
                    else { mode.Remove(kv.Key); why[kv.Key] = (why.TryGetValue(kv.Key, out var w0) ? w0 + " | flush:" : "") + kv.Value; toLive++; }
                }
                sb.Append($"  decision build {iters + 1}: {refs.Count / 2} pairs drawn, {bad.Count} failing ({toFlush} islands -> flush, {toLive} -> live){(loose > 0 ? $", {loose} census edges near no drawn pair" : "")}\n");
                if (bad.Count == 0) break;
            }
            log = sb.ToString().TrimEnd('\n');
            return mode;
        }

        /// <summary>Per drawn pair (key -> what fails): both carriageways'
        /// RefugeFails, a 0.3 m step, a lane line lost against live; and the
        /// RELAY + SHIFT CENSUS - every edge outside the refuge chains whose
        /// eases differ from live, measured ON and live (EdgeSlope): steeper
        /// than max(0.06, live + 0.005) fails each drawn pair at whose split
        /// node it ends or whose common-mode shift it carries (loose: none).</summary>
        /// <summary>Round 5: a kerbed island whose two carriageways a junction
        /// fan trims back past the tip by lengths more than this apart ends
        /// unevenly inside the fan - the island's top met the fan's corner walk
        /// in a sunken pocket (review r4: fan_6551). The pair fails.</summary>
        const float FanEndUnevenM = 3f;

        static Dictionary<long, string> PairFails(CityMap map, CityMeshes.Trims t, List<LineModel.Refuge> refs, List<LineModel.RefugeM> m, LineModel.Ease[][] offEase,
                                                  out int censusN, out int worse, out int loose, out float worst, out string worstAt)
        {
            var res = new Dictionary<long, string>();
            void Add(long pk, string f) { res[pk] = res.TryGetValue(pk, out var o) ? o + f : f; }
            var la = new List<LineModel.LineAt>(16);
            var chainSet = new HashSet<int>();
            for (int i = 0; i + 1 < refs.Count; i += 2)
            {
                var r0 = refs[i]; var r1 = refs[i + 1];
                foreach (int ei in r0.chain) chainSet.Add(ei);
                foreach (int ei in r1.chain) chainSet.Add(ei);
                string f = LineModel.RefugeFails(r0, m[i]) + LineModel.RefugeFails(r1, m[i + 1]);
                float st = Mathf.Max(m[i].step, m[i + 1].step);
                if (st >= 0.3f) f += $" step {st:0.00};";
                int on = 0, off = 0;
                foreach (var r in new[] { r0, r1 })
                    foreach (int ei in r.chain)
                    {
                        var e = map.edges[ei];
                        LineModel.LinesAt(e, 0.5f * e.length, la); on += la.Count;
                        var keep = e.lmEase; e.lmEase = offEase[ei];
                        LineModel.LinesAt(e, 0.5f * e.length, la); off += la.Count;
                        e.lmEase = keep;
                    }
                if (on < off) f += $" lane lines {on} vs live {off};";
                if (r0.half > 0f)
                    for (int end = 0; end < 2; end++)
                    {
                        int node = end == 0 ? r0.node : r0.far;
                        int e0 = end == 0 ? r0.chain[0] : r0.chain[r0.chain.Length - 1], e1 = end == 0 ? r1.chain[0] : r1.chain[r1.chain.Length - 1];
                        float t0 = t.TrimAt(map.edges[e0], node), t1 = t.TrimAt(map.edges[e1], node);
                        if (Mathf.Max(t0, t1) > LineModel.RefugeTipM + 0.05f && Mathf.Abs(t0 - t1) > FanEndUnevenM)
                            f += $" kerbed island ends unevenly in a junction fan at n{node} (trims {t0:0.0}/{t1:0.0} m);";
                    }
                if (f.Length > 0) Add(r0.pk, f);
            }
            censusN = 0; worse = 0; loose = 0; worst = 0f; worstAt = "";
            for (int i = 0; i < map.edges.Length; i++)
            {
                var e = map.edges[i];
                if (chainSet.Contains(i) || EasesSame(e.lmEase, offEase[i])) continue;
                censusN++;
                float sOn = LineModel.EdgeSlope(e, out string atOn);
                var keep = e.lmEase; e.lmEase = offEase[i];
                float sOff = LineModel.EdgeSlope(e, out _);
                e.lmEase = keep;
                if (sOn <= Mathf.Max(0.06f, sOff + 0.005f)) continue;
                worse++;
                if (sOn > worst) { worst = sOn; worstAt = $"{atOn} '{e.name}' (live {sOff:0.000})"; }
                bool hit = false;
                for (int j = 0; j + 1 < refs.Count; j += 2)
                {
                    var r = refs[j];
                    bool near = r.node == e.a || r.node == e.b || r.far == e.a || r.far == e.b || System.Array.IndexOf(r.uEdges, i) >= 0;
                    if (!near) continue;
                    hit = true;
                    Add(r.pk, $" census e{i} '{e.name}' {sOff:0.000}->{sOn:0.000} ({atOn});");
                }
                if (!hit) loose++;
            }
            return res;
        }

        static string EaseDump(LineModel.Ease[] a)
        {
            if (a == null) return "null";
            var sb = new StringBuilder($"[{a.Length}]");
            foreach (var z in a) sb.Append($" (sh{(z.shift ? 1 : 0)} rl{(z.relay ? 1 : 0)} ax{(z.aux ? 1 : 0)} co{(z.centreOnly ? 1 : 0)} s{z.side} dw{z.dw:0.000} len{z.len:0.00} d0{z.d0:0.0} tag{z.tag} p{(z.partner == null ? -1 : z.partner.Length)})");
            return sb.ToString();
        }

        static bool Same(float[] a, float[] b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i] && !(float.IsNaN(a[i]) && float.IsNaN(b[i]))) return false;
            return true;
        }

        /// <summary>Two eases arrays draw the same (every scalar field and sampled profile equal).</summary>
        static bool EasesSame(LineModel.Ease[] a, LineModel.Ease[] b)
        {
            int na = a?.Length ?? 0, nb = b?.Length ?? 0;
            if (na != nb) return false;
            for (int i = 0; i < na; i++)
            {
                var x = a[i]; var y = b[i];
                if (x.side != y.side || x.fromA != y.fromA || x.narrowFlip != y.narrowFlip || x.shift != y.shift || x.relay != y.relay || x.dw != y.dw || x.len != y.len ||
                    x.d0 != y.d0 || x.span != y.span || x.dwOther != y.dwOther || x.twoRole != y.twoRole || x.narrow != y.narrow || x.tag != y.tag || x.centreOnly != y.centreOnly ||
                    x.aux != y.aux || x.auxArm != y.auxArm || x.auxD != y.auxD || x.auxWN != y.auxWN || x.auxW != y.auxW || x.auxLs != y.auxLs || x.auxXF != y.auxXF ||
                    x.auxLt != y.auxLt || x.auxZone != y.auxZone || x.profD0 != y.profD0 || x.profDs != y.profDs || x.widen != y.widen ||
                    !Same(x.partner, y.partner) || !Same(x.prof, y.prof)) return false;
            }
            return true;
        }

        /// <summary>Round 3: at every profiled pair's two split nodes, each
        /// carriageway's first section against the undivided road's last:
        /// where they meet at the node itself (a mitred pair, no fan) both of
        /// the carriageway's corners must lie on the undivided road's cut line
        /// (one section normal) - the round-2 wedge was up to a metre there.</summary>
        static string fanAt = "";
        static string NoseWedges(CityMap map, CityMeshes.Trims t, List<LineModel.Refuge> refs, out float worst, out int bad, out int met, out int fan)
        {
            fanAt = "";
            worst = 0f; bad = 0; met = 0; fan = 0;
            var sb = new StringBuilder();
            var splitU = new Dictionary<int, int>();
            if (map.splt != null) foreach (var sp in map.splt) splitU[sp.node] = sp.u;
            var seen = new HashSet<long>();
            foreach (var r in refs)
            {
                if (!r.profiled) continue;
                for (int end = 0; end < 2; end++)
                {
                    int node = end == 0 ? r.node : r.far;
                    int ei = end == 0 ? r.chain[0] : r.chain[r.chain.Length - 1];
                    if (!seen.Add(((long)node << 32) | (uint)ei) || !splitU.TryGetValue(node, out int ui)) continue;
                    var U = map.edges[ui]; var C = map.edges[ei];
                    float tu = t.TrimAt(U, node), tc = t.TrimAt(C, node);
                    if (tu > 0.05f || tc > 0.05f)
                    {
                        fan++;
                        // round 4: where they are (the kerb ramps up from these drawn ends)
                        if (fanAt.Length < 900 && tc > LineModel.RefugeTipM + 0.05f) { var pn = map.nodes[node]; var pc = C.PointAt(C.a == node ? tc : C.length - tc); fanAt += $" n{node}/e{ei} trim {tc:0.0} m at ({pc.x:0.0},{pc.y:0.0}) half {r.half:0.00};"; }
                        continue;
                    }
                    met++;
                    var vu = CityMeshes.AuditView.SectionAt(map, t, U, U.a == node ? 0f : U.length, false);
                    var vc = CityMeshes.AuditView.SectionAt(map, t, C, C.a == node ? 0f : C.length, false);
                    Vector2 a = new Vector2(vu.L.x, vu.L.z), b = new Vector2(vu.R.x, vu.R.z), ab = b - a;
                    float worstHere = 0f;
                    foreach (var q3 in new[] { vc.L, vc.R })
                    {
                        var q = new Vector2(q3.x, q3.z);
                        float d = ab.sqrMagnitude > 1e-6f ? Mathf.Abs(ab.x * (q.y - a.y) - ab.y * (q.x - a.x)) / ab.magnitude : 0f;
                        worstHere = Mathf.Max(worstHere, d);
                    }
                    if (worstHere > worst) worst = worstHere;
                    if (worstHere > 0.02f) { bad++; if (sb.Length < 600) sb.Append($" n{node}/e{ei} {worstHere:0.00};"); }
                }
            }
            return (sb.Length > 0 ? "; over 0.02 m:" + sb : "") + (fanAt.Length > 0 ? "\n  FAN ENDS past the tip (kerb ramps up over " + CityMeshes.KerbEndRampM + " m from the drawn end):" + fanAt : "");
        }

        /// <summary>Round 3: the heading traffic gives a car on a refuge
        /// carriageway (PlanPoint's tangent) against the direction its lane
        /// path actually runs (OwnLanePoint, 0.5 m apart), every metre; and
        /// what OSM's own tangent (round 2's heading) was off by.</summary>
        static string Headings(CityMap map, List<LineModel.Refuge> refs, out float worst, out float worstOsm, out int n)
        {
            worst = 0f; worstOsm = 0f; n = 0; string at = "", atOsm = "";
            var seen = new HashSet<int>();
            foreach (var r in refs)
            {
                if (!r.profiled) continue;
                foreach (int ei in r.chain)
                {
                    if (!seen.Add(ei)) continue;
                    var e = map.edges[ei];
                    for (float s = 0.5f; s <= e.length - 0.5f + 1e-3f; s += 1f)
                    {
                        var d = LineModel.OwnLanePoint(e, Mathf.Min(e.length, s + 0.25f)) - LineModel.OwnLanePoint(e, Mathf.Max(0f, s - 0.25f));
                        if (d.sqrMagnitude < 1e-6f) continue;
                        d.Normalize();
                        var p = LineModel.OwnLanePoint(e, s);
                        var nL = LineModel.SectionLeft(e, s);
                        float lat = Vector2.Dot(p - e.PointAt(s), nL);
                        CityTraffic.PlanPointForAudit(e, s, lat, out var tan);
                        float ang = Vector2.Angle(tan, d), angO = Vector2.Angle(e.TangentAt(s), d);
                        n++;
                        if (ang > worst) { worst = ang; at = $"e{ei} s {s:0.0}"; }
                        if (angO > worstOsm) { worstOsm = angO; atOsm = $"e{ei} s {s:0.0}"; }
                    }
                }
            }
            return $" (worst at {at}; OSM's at {atOsm})";
        }

        /// <summary>Round 3 diagnostics: the paint at the library noses.</summary>
        static string HatchDump(CityMap map, HashSet<int> nodes)
        {
            var sb = new StringBuilder("NOSE HATCH DUMP:");
            var la = new List<LineModel.LineAt>(16);
            if (map.splt == null) return sb.ToString();
            foreach (var sp in map.splt)
            {
                if (!nodes.Contains(sp.node) || sp.u < 0) continue;
                var U = map.edges[sp.u];
                var lay = LineModel.LayoutOf(U); var tex = lay.tex ?? lay;
                sb.Append($"\n  n{sp.node} U e{U.index} a{U.a} b{U.b} len {U.length:0.0} kinds [{string.Join(",", lay.kind)}] m [{string.Join(",", System.Array.ConvertAll(lay.m, x => x.ToString("0.00")))}] tex kinds [{string.Join(",", tex.kind)}]");
                if (U.lmEase != null) foreach (var z in U.lmEase) sb.Append($" ease[{(z.shift ? "shift" : z.widen ? "widen" : z.aux ? "aux" : z.relay ? "relay" : "taper")} side {z.side} len {z.len:0.0} fromA {z.fromA}]");
                var ms = CityMeshes.MarksOf(U.index);
                if (ms == null) continue;
                foreach (var m in ms)
                {
                    sb.Append($"\n    mark {CityMeshes.MarkNames[m.style]} k{m.k} s {m.s0:0.0}-{m.s1:0.0} '{m.what}'{(m.yellow ? " yellow" : "")}{(m.pieces != null ? $" pieces {m.pieces.Count}" : "")}");
                    if (m.what != "nose hatch") continue;
                    for (float s = m.s0; s <= m.s1 + 1e-3f; s += Mathf.Max(1f, (m.s1 - m.s0) / 5f))
                    {
                        LineModel.LinesAt(U, s, la);
                        sb.Append($"\n      s {s:0.0} sh {LineModel.ShiftAt(U, s):0.00} lines");
                        foreach (var l in la) sb.Append($" {l.k}:{l.lat:0.00}");
                    }
                    if (m.pieces.Count > 0) { var pc = m.pieces[0]; sb.Append($"\n      piece0 ({pc[0].x:0.00},{pc[0].y:0.0}) ({pc[1].x:0.00},{pc[1].y:0.0}) ({pc[2].x:0.00},{pc[2].y:0.0}) ({pc[3].x:0.00},{pc[3].y:0.0})"); }
                }
            }
            return sb.ToString();
        }

        /// <summary>Round 3: the bend's concrete median, cell by cell.</summary>
        static string BendCells(CityMap map, int[] edges)
        {
            var sb = new StringBuilder("BEND MEDIAN CELLS (concrete-listed ways):");
            int conc = 0, grass = 0, none = 0;
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                sb.Append($"\n  e{ei} way {e.wayId} len {e.length:0}:");
                for (float s = 5f; s < e.length - 2f; s += 10f)
                {
                    var p = e.PointAt(s); var lf = LineModel.SectionLeft(e, s);
                    LineModel.Extents(e, s, out float eM, out float eP);
                    foreach (int sd in new[] { 1, -1 })
                    {
                        var c = p + lf * sd * ((sd > 0 ? eP : eM) + 1.5f);
                        int k = CityMeshes.MedianCellKind(map, c);
                        if (k == 2) conc++; else if (k == 1) grass++; else none++;
                        if (s < 50f) sb.Append($" s{s:0}{(sd > 0 ? "+" : "-")}:{k}(e{CityMeshes.MedCellA}/e{CityMeshes.MedCellB})");
                    }
                }
            }
            sb.Append($"\n  cells 1.5 m past either edge: concrete {conc}, grass {grass}, not a median {none}");
            return sb.ToString();
        }

        /// <summary>Round 3 (the review's perf finding): PavedRule over every
        /// 8 m lattice cell of 3x3 tiles at five sites, median surface off and
        /// on (on: the bucket table cold, then warm), in ms per 256 m tile.</summary>
        static string PavedTiming(CityMap map)
        {
            var sites = new (float x, float z, string n)[] { (-3305f, 9100f, "library"), (-3940f, 13800f, "bend"), (-1600f, 6100f, "dalton"), (1184f, -1795f, "providence"), (-8938f, 26444f, "rural BF") };
            bool was = CityMeshes.MedianGrassOn;
            var sb = new StringBuilder("PAVED-CELL RULE TIMING (ms per 256 m tile, 1024 cells; off / on cold / on warm):");
            const float cell = CityMeshes.TileSize / CityMeshes.GroundRes;
            float tot0 = 0f, tot1 = 0f, tot2 = 0f; int tiles = 0;
            foreach (var st in sites)
            {
                int tx = Mathf.FloorToInt(st.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(st.z / CityMeshes.TileSize);
                float Run(bool on, bool cold)
                {
                    CityMeshes.MedianGrassOn = on;
                    if (cold) CityMeshes.MedBucketsReset();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            for (int i = 0; i < CityMeshes.GroundRes; i++)
                                for (int j = 0; j < CityMeshes.GroundRes; j++)
                                    CityMeshes.PavedRule(map, new Vector2(((tx + dx) * CityMeshes.GroundRes + i + 0.5f) * cell, ((tz + dz) * CityMeshes.GroundRes + j + 0.5f) * cell));
                    return (float)sw.Elapsed.TotalMilliseconds / 9f;
                }
                Run(false, false);
                float a = Run(false, false), b = Run(true, true), c = Run(true, false);
                tot0 += a; tot1 += b; tot2 += c; tiles++;
                sb.Append($" {st.n} {a:0.00}/{b:0.00}/{c:0.00};");
            }
            CityMeshes.MedianGrassOn = was;
            sb.Append($" mean {tot0 / tiles:0.00}/{tot1 / tiles:0.00}/{tot2 / tiles:0.00} - added per tile cold {(tot1 - tot0) / tiles:+0.00;-0.00} ms, warm {(tot2 - tot0) / tiles:+0.00;-0.00} ms");
            return sb.ToString();
        }

        /// <summary>Round 4 diagnostic (PSX_REFUGE_DIAG=node,node,...): each
        /// pair at those split nodes - its frame, the undivided roads' base
        /// and drawn extents at the nodes, every chain edge's layout and eases.</summary>
        static string DiagPairs(CityMap map, List<LineModel.Refuge> refs)
        {
            var env = System.Environment.GetEnvironmentVariable("PSX_REFUGE_DIAG");
            if (string.IsNullOrEmpty(env)) return "";
            var want = new HashSet<int>();
            foreach (var t in env.Split(',')) if (int.TryParse(t, out int v)) want.Add(v);
            var splitAt = new Dictionary<int, LineModel.Split>();
            if (map.splt != null) foreach (var sp in map.splt) splitAt[sp.node] = sp;
            string Ez(CityMap.Edge e)
            {
                if (e.lmEase == null) return "";
                var s = "";
                foreach (var z in e.lmEase) s += $" [{(z.shift ? "shift" : z.widen ? "widen" : z.aux ? "aux" : z.relay ? "relay" : "taper")} sd{z.side} dw{z.dw:0.00} len{z.len:0.0} d0{z.d0:0.0} A{(z.fromA ? 1 : 0)}{(z.prof != null ? " prof" : "")}{(z.centreOnly ? " co" : "")}]";
                return s;
            }
            string Ed(CityMap.Edge e, int node)
            {
                float s = e.a == node ? 0f : e.length;
                LineModel.Extents(e, s, out float eM, out float eP);
                return $"e{e.index} '{e.name}' a{e.a} b{e.b} len {e.length:0.0} w {e.width:0.00} lanes {e.lanes} shl {e.shl:0.00} shr {e.shr:0.00} lm +{e.lmPlus:0.00}/-{e.lmMinus:0.00} drawn@n{node} +{eP:0.00}/-{eM:0.00} sh {LineModel.ShiftAt(e, s):0.00}{Ez(e)}";
            }
            var sb = new StringBuilder("REFUGE DIAG:");
            foreach (var r in refs)
            {
                if (!want.Contains(r.node)) continue;
                sb.Append($"\n  PAIR n{r.node}->n{r.far} e{r.edge} so {r.so} prof {r.profiled} why '{r.why}' axLen {r.axLen:0.0} uOut {r.uOut1:0.00}/{r.uOut2:0.00} move {r.move:0.00}/{r.moveLen:0} half {r.half:0.00} x0 {r.x0s:0.0}/{r.x0e:0.0} ln {r.ln:0.0} osmIsl {r.osmIsland:0.00}");
                if (splitAt.TryGetValue(r.node, out var s1)) sb.Append($"\n    U1 {Ed(map.edges[s1.u], r.node)} offA {s1.offA:0.00} offB {s1.offB:0.00}");
                if (splitAt.TryGetValue(r.far, out var s2)) sb.Append($"\n    U2 {Ed(map.edges[s2.u], r.far)} offA {s2.offA:0.00} offB {s2.offB:0.00}");
                int from = r.node;
                foreach (int ei in r.chain)
                {
                    var e = map.edges[ei];
                    sb.Append($"\n    C {Ed(e, from)}");
                    from = e.a == from ? e.b : e.a;
                }
                if (r.profiled && r.ax != null)
                {
                    // the drawn edges every 4 m in the frame
                    from = r.node; sb.Append("\n    drawn (u: outer/inner lat, target, nose):");
                    foreach (int ei in r.chain)
                    {
                        var e = map.edges[ei]; bool fwd = e.a == from;
                        for (float x = 0f; x <= e.length; x += 4f)
                        {
                            float s = fwd ? x : e.length - x;
                            LineModel.Extents(e, s, out float eM, out float eP);
                            var p = e.PointAt(s); var lf = LineModel.SectionLeft(e, s);
                            r.Local(p + lf * eP, out float u1, out float l1); r.Local(p - lf * eM, out float u2, out float l2);
                            sb.Append($" {u1:0}:{Mathf.Max(l1, l2):0.00}/{Mathf.Min(l1, l2):0.00},{r.OuterTarget(u1):0.00},{r.NoseHalf(u1):0.00}");
                        }
                        from = fwd ? e.b : e.a;
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Diagnostic: one refuge carriageway's eases and its drawn outer edge every 2 m.</summary>
        static void DumpRefuge(CityMap map, List<LineModel.Refuge> refs, int edge)
        {
            foreach (var r in refs)
            {
                if (r.edge != edge) continue;
                var sb = new StringBuilder($"  DUMP e{edge} n{r.node}->n{r.far} so {r.so} axLen {r.axLen:0.0} uOut {r.uOut1:0.00}/{r.uOut2:0.00} chain [{string.Join(",", r.chain)}]\n");
                int from = r.node;
                foreach (int ei in r.chain)
                {
                    var e = map.edges[ei];
                    bool fwd = e.a == from;
                    sb.Append($"   e{ei} a{e.a} b{e.b} len {e.length:0.0} lm +{e.lmPlus:0.00}/-{e.lmMinus:0.00} fwd {fwd} eases:");
                    if (e.lmEase != null) foreach (var z in e.lmEase) sb.Append($" [{(z.shift ? "shift" : z.widen ? "widen" : z.aux ? "aux" : z.relay ? "relay" : "taper")} side {z.side} dw {z.dw:0.00} len {z.len:0.0} d0 {z.d0:0.0} fromA {z.fromA} prof {(z.prof != null ? z.prof.Length : 0)}]");
                    sb.Append('\n');
                    for (float x = 0f; x <= e.length; x += 2f)
                    {
                        float s = fwd ? x : e.length - x;
                        LineModel.Extents(e, s, out float eM, out float eP);
                        var p = e.PointAt(s); var t = e.TangentAt(s); var lf = new Vector2(-t.y, t.x);
                        if (LineModel.RefugeNormal(e, s, out var nL3)) lf = nL3;
                        r.Local(p, out float u, out float q);
                        r.Local(p + lf * eP, out float u1, out float l1); r.Local(p - lf * eM, out float u2, out float l2);
                        sb.Append($"    s {s:0.0} u {u:0.0} q {q:0.00} sh {LineModel.ShiftAt(e, s):0.00} eP {eP:0.00} eM {eM:0.00} latP {l1:0.00} latM {l2:0.00} target {r.OuterTarget(u):0.00} nose {r.NoseHalf(u):0.00}\n");
                    }
                    from = fwd ? e.b : e.a;
                }
                Line(sb.ToString());
            }
        }

    }
}
