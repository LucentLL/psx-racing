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
        public static void RunMedianGates()
        {
            outLog = new StringBuilder();
            failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing from Resources"); FinishTo("city_medians.txt"); return; }
            bool refWas = LineModel.RefugeOn;
            LineModel.RefugeOn = true;
            var tOn = CityMeshes.ComputeTrims(map);
            var refs = new List<LineModel.Refuge>(LineModel.Refuges);
            var seps = new List<LineModel.Refuge>(LineModel.Separated);
            int profiled = LineModel.RefugeProfiled, fallback = LineModel.RefugeFallback, capped = LineModel.RefugeTapers, flushN = LineModel.RefugeFlush;
            int selfFlush = LineModel.RefugeSelfFlush, selfFail = LineModel.RefugeSelfFail; float checkMs = LineModel.RefugeCheckMs;
            var mOn = LineModel.MeasureRefuges(map, refs, out float stepOn, out int linesOn, out string worstOn);
            // round 3: one section normal at every profiled nose, the traffic's heading
            string wedge = NoseWedges(map, tOn, refs, out float wedgeMax, out int wedgeBad, out int wedgeMet, out int wedgeFan);
            string heading = Headings(map, refs, out float headOn, out float headOsm, out int headN);
            string diag = DiagPairs(map, refs);
            CityMeshes.EnsureMarks(map, tOn);
            string hatchDump = HatchDump(map, new HashSet<int> { 10229, 10230, 10231, 10232, 10236, 10238 });
            string bendDump = BendCells(map, new[] { 14094, 20695 });
            string timing = PavedTiming(map);
            var ms = CityMeshes.MarkStats;
            int noseOn = ms.noseArrows, noseSkip = ms.noseSkipped, groupsOn = ms.groups;
            string hatch = $"{ms.noseHatches} nose approaches hatched ({ms.noseHatchBars} bars, at least {CityMeshes.HatchMinBars} each; {ms.noseSolid} bounding lines that render solid, {ms.noseSolidFallback} that would fall back to the dashed line; {ms.noseWhiteOnly} on a texture with no solid yellow), {ms.noseFlushTip} noses with a centre lane that could not be hatched (it moves, or under {CityMeshes.HatchMinBars} bars: the island kerb is kept back {CityMeshes.KerbFlushTipM:0} m from those tips - a flush tip), {ms.noseNoCentre} plain noses with no centre lane";
            string cells = CityMeshes.MedianCellCensus(map);
            LineModel.RefugeOn = false;
            var tOff = CityMeshes.ComputeTrims(map);
            var mOff = LineModel.MeasureRefuges(map, refs, out float stepOff, out int linesOff, out string worstOff);
            CityMeshes.EnsureMarks(map, tOff);
            int noseOff = CityMeshes.MarkStats.noseArrows, groupsOff = CityMeshes.MarkStats.groups;
            LineModel.RefugeOn = refWas;

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
            bool Prof(int i) => refs[i].profiled;
            bool Centre(int i) => refs[i].profiled && refs[i].move <= 0f;
            bool Four(int i) => refs[i].profiled && refs[i].move > 0f;
            int nCentre = 0, nFour = 0;
            for (int i = 0; i < refs.Count; i++) { if (Centre(i)) nCentre++; if (Four(i)) nFour++; }
            Line($"REFUGE ISLANDS round 4 (PSX_CITY_REFUGE): {refs.Count / 2} pairs, {refs.Count} carriageways; {profiled} drawn by the island profile ({nCentre} beside a centre lane, {nFour} 4-lane with the lanes moved out), {fallback} pairs left on round 1's cap; {flushN / 2} profiled pairs with no room for a kerbed island drawn straight with a flush painted median; {capped} split-node tapers replaced");
            Line($"  outer edge off the undivided road's edge line, centre-lane carriageways: OFF {Q(Col(mOff, m => m.outerDev, Centre))}; ON {Q(Col(mOn, m => m.outerDev, Centre))}");
            Line($"  outer edge's move out (4-lane carriageways): OFF {Q(Col(mOff, m => m.outerDev, Four))}; ON {Q(Col(mOn, m => m.outerDev, Four))}");
            Line($"  lane line move against that edge line, centre-lane: OFF {Q(Col(mOff, m => m.lineMove, Centre))}; ON {Q(Col(mOn, m => m.lineMove, Centre))}");
            Line($"  lane line move, 4-lane: OFF {Q(Col(mOff, m => m.lineMove, Four))}; ON {Q(Col(mOn, m => m.lineMove, Four))}");
            Line($"  traffic path move: OFF {Q(Col(mOff, m => m.pathMove, Prof))}; ON {Q(Col(mOn, m => m.pathMove, Prof))}");
            Line($"  largest lateral slope of a lane edge / line / traffic path: OFF {Q(Col(mOff, m => m.slope, Prof))}; ON {Q(Col(mOn, m => m.slope, Prof))}");
            Line($"  island at mid minus design wI: ON {Q(Col(mOn, m => m.islandErr, Prof))}; island at mid OFF {Q(Col(mOff, m => m.island, Prof))}, ON {Q(Col(mOn, m => m.island, Prof))}, design wI {Q(Col(mOn, m => m.wI, Prof))}");
            Line($"  island narrowing from a nose toward mid (m): ON {Q(Col(mOn, m => m.nonMono, Prof))}; widest island before the tip: ON {Q(Col(mOn, m => m.noseW, Prof))}");
            Line($"  largest step in a drawn edge at a chain joint or its split node: OFF {stepOff:0.000} m ({worstOff}), ON {stepOn:0.000} m ({worstOn})");
            Line($"  lane lines on the chain edges at their mids: OFF {linesOff}, ON {linesOn}");
            var spots = new HashSet<int> { 10229, 10234, 10230, 10233, 10231, 10232, 10236, 10238, 13570, 13571 };
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (!spots.Contains(r.node)) continue;
                var p = map.nodes[r.node];
                var a = mOff[i]; var b = mOn[i];
                Line($"  n{r.node}->n{r.far} e{r.edge} '{map.edges[r.edge].name}' Lc {r.lc:0.0} ({LatLon(p.x, p.y)}) {(r.profiled ? (r.move > 0f ? $"4-lane move {r.move:0.00} over {r.moveLen:0} m" : "centre lane") : "FALLBACK")}: OSM island {r.osmIsland:0.00}, wI {2f * r.half:0.00}, nose {r.x0s:0.0}/{r.x0e:0.0}+{r.ln:0.0} m r {r.rn:0.00}" +
                     $" | OFF island {a.island:0.00} outer {a.outerDev:0.00} lines {a.lineMove:0.00} slope {a.slope:0.000} | ON island {b.island:0.00} outer {b.outerDev:0.000} lines {b.lineMove:0.000} path {b.pathMove:0.000} slope {b.slope:0.000}");
            }
            int badOuter = 0, badLine = 0, badSlope = 0, badIsl = 0, badMono = 0, badNose = 0, badFour = 0, badShared = 0; string badAt = "";
            float worstSlope = 0f; string worstSlopeAt = "-";
            void Bad(ref int n, int i, string what) { n++; if (badAt.Length < 2500) badAt += $" n{refs[i].node}/e{refs[i].edge} {what};"; }
            for (int i = 0; i < refs.Count; i++)
            {
                if (!refs[i].profiled) continue;
                var m = mOn[i];
                if (Centre(i) && m.outerDev > 0.05f) Bad(ref badOuter, i, $"outer {m.outerDev:0.000}");
                if (Centre(i) && m.lineMove > 0.02f) Bad(ref badLine, i, $"line {m.lineMove:0.000}");
                if (Four(i) && (m.lineMove > 1.31f || refs[i].moveLen < 20f - 1e-3f)) Bad(ref badFour, i, $"4-lane move {m.lineMove:0.00} over {refs[i].moveLen:0.0}");
                // round 4: no exemption - every profiled carriageway (review r3)
                if (m.slope > worstSlope) { worstSlope = m.slope; worstSlopeAt = $"n{refs[i].node}/e{refs[i].edge} {m.slopeAt}"; }
                if (m.slope > 0.06f) Bad(ref badSlope, i, $"slope {m.slope:0.000} at {m.slopeAt} (edge line {refs[i].o1:0.00}->{refs[i].o2:0.00} over {refs[i].axLen:0} m, move {refs[i].move:0.00} over {refs[i].moveLen:0})");
                if (Mathf.Abs(m.islandErr) > 0.1f) Bad(ref badIsl, i, $"island {m.island:0.00} vs {m.wI:0.00}");
                if (m.nonMono > 0.02f) Bad(ref badMono, i, $"narrows {m.nonMono:0.000}");
                if (m.noseW > 0.05f) Bad(ref badNose, i, $"nose {m.noseW:0.00}");
                // the builder's self-check calls the same function: it must agree
                if ((LineModel.RefugeFails(refs[i], m).Length > 0) != (refs[i].check.Length > 0)) badShared++;
            }
            Check(badOuter == 0, "centre-lane islands: outer edge within 0.05 m of the undivided road's edge line", badOuter);
            Check(badLine == 0, "centre-lane islands: no lane line moves over 0.02 m against the lanes' straight reference", badLine);
            Check(badFour == 0, "4-lane islands: lanes move at most 1.3 m, eased over 20 m or more", badFour);
            Check(badSlope == 0, $"no lane edge, line or traffic path slopes over 0.06 on any profiled carriageway (worst {worstSlope:0.000} at {worstSlopeAt})", badSlope);
            Check(badShared == 0, "the builder's self-check and this gate agree on every carriageway (one measurement)", badShared);
            Line($"  SELF-CHECK (LineModel.VerifyRefuges, the gate's own measurement): {selfFlush} pairs rebuilt flush, {selfFail} pairs still failing; {checkMs:0.0} ms at load");
            Check(fallback == 0, "no refuge pair left on round 1's cap", fallback);
            Check(badIsl == 0, "island at mid within 0.1 m of its design width", badIsl);
            Check(badMono == 0, "island never narrows from a nose toward mid", badMono);
            Check(badNose == 0, "no island before its tip (nose width 0 there)", badNose);
            if (badAt.Length > 0) Line("  failing carriageways:" + badAt);
            int worse = 0; string worseAt = "";
            for (int i = 0; i < refs.Count; i++)
                if (mOn[i].step > Mathf.Max(0.3f, mOff[i].step + 0.01f)) { worse++; if (worseAt.Length < 300) worseAt += $" n{refs[i].node}/e{refs[i].edge} {mOff[i].step:0.00}->{mOn[i].step:0.00};"; }
            Check(worse == 0, "no refuge carriageway steps 0.3 m or more (and more than OFF) at a joint or its split node", worse + worseAt);
            Check(linesOn == linesOff, "no lane line lost on a refuge carriageway", $"{linesOn} vs {linesOff}");
            Line($"ARROWS INTO A NOSE: turn-only arrow groups painted into a plain split's nose OFF {noseOff}, ON {noseOn} ({noseSkip} lanes skipped); all arrow groups OFF {groupsOff}, ON {groupsOn}");
            Check(noseOn == 0, "no turn arrow painted into a plain split nose", noseOn);
            Line($"NOSE APPROACHES: {hatch}");
            Line(cells);
            Line(bendDump);
            // round 3
            Line($"NOSE WEDGES (one section normal at a profiled split node): {wedgeMet} carriageway ends meet the undivided road's end at the node, {wedgeFan} meet a fan; largest corner off the undivided road's cut {wedgeMax:0.000} m{wedge}");
            Check(wedgeBad == 0, "no wedge (over 0.02 m) between a profiled carriageway and the undivided road at its nose", wedgeBad);
            Line($"TRAFFIC HEADING on refuge carriageways ({headN} samples): heading vs the lane path's own direction, max {headOn:0.00} deg (OSM tangent, round 2: max {headOsm:0.00} deg){heading}");
            Check(headOn <= 2f, "traffic heading within 2 degrees of the path on refuge carriageways", $"{headOn:0.00} deg");
            Line(timing);
            var fbs = new StringBuilder(); int nFb = 0;
            for (int i = 0; i + 1 < refs.Count; i += 2)
            {
                var r = refs[i];
                if (r.profiled) continue;
                nFb++;
                var p = map.nodes[r.node];
                fbs.Append($"\n    n{r.node}->n{r.far} e{r.edge}/e{refs[i + 1].edge} '{map.edges[r.edge].name}' Lc {r.lc:0} ({LatLon(p.x, p.y)}; x {p.x:0} z {p.y:0}) OSM island {r.osmIsland:0.00}: {r.why}");
            }
            Line($"FALLBACK PAIRS (not drawn by the profile): {nFb}{fbs}");
            Line(RoundFour(map, refs, mOn, seps));
            Line(hatchDump);
            if (diag.Length > 0) Line(diag);
            FinishTo("city_medians.txt");
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

        /// <summary>Round 4: the common-mode shifts put on undivided roads,
        /// the real separated medians and their slopes, and the 13 pairs
        /// round 3 left on round 1's cap.</summary>
        static string RoundFour(CityMap map, List<LineModel.Refuge> refs, List<LineModel.RefugeM> mOn, List<LineModel.Refuge> seps)
        {
            var sb = new StringBuilder();
            int nU = 0; var su = new StringBuilder();
            for (int i = 0; i + 1 < refs.Count; i += 2)
            {
                var r = refs[i];
                if (Mathf.Abs(r.uShift1) < 0.01f && Mathf.Abs(r.uShift2) < 0.01f) continue;
                nU++;
                // the slope the shift gives the undivided road: smoothstep over the length given (room-capped)
                float g1 = Mathf.Min(r.uLen1, r.uRoom1), g2 = Mathf.Min(r.uLen2, r.uRoom2);
                float s1 = Mathf.Abs(r.uShift1) > 0.01f ? 1.5f * Mathf.Abs(r.uShift1) / Mathf.Max(0.5f, g1) : 0f, s2 = Mathf.Abs(r.uShift2) > 0.01f ? 1.5f * Mathf.Abs(r.uShift2) / Mathf.Max(0.5f, g2) : 0f;
                su.Append($"\n    n{r.node}->n{r.far} '{map.edges[r.edge].name}': U1 shift {r.uShift1:0.00} m over {g1:0} m (asked {r.uLen1:0}, slope {s1:0.000}), U2 shift {r.uShift2:0.00} m over {g2:0} m (asked {r.uLen2:0}, slope {s2:0.000})");
            }
            sb.Append($"COMMON-MODE SHIFTS on the undivided roads (round 4: the sideways part of an edge-line change, so the island keeps under the slope): {nU} pairs{su}");
            sb.Append($"\nSEPARATED MEDIANS (real, not islands: own lines, the split's ordinary tapers): {seps.Count / 2} pairs");
            foreach (var r in seps)
            {
                float worst = 0f;
                foreach (int ei in r.chain)
                {
                    var e = map.edges[ei];
                    LineModel.Extents(e, 0f, out float pm, out float pp);
                    for (float s = 0.5f; s <= e.length + 1e-3f; s += 0.5f)
                    {
                        LineModel.Extents(e, s, out float em, out float ep);
                        worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(ep - pp), Mathf.Abs(em - pm)) / 0.5f);
                        pm = em; pp = ep;
                    }
                }
                var p = map.nodes[r.node];
                sb.Append($"\n    n{r.node}->n{r.far} e{r.edge} '{map.edges[r.edge].name}' ({LatLon(p.x, p.y)}) OSM island {r.osmIsland:0.0} m: largest lateral slope of a drawn edge off its own line {worst:0.000}");
            }
            var former = new HashSet<int> { 2225, 2228, 6098, 7198, 7969, 9660, 11419, 13199, 14285, 15853, 16463, 16685, 17325 };
            sb.Append("\nROUND 3's 13 CAPPED PAIRS, now:");
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (!former.Contains(r.node)) continue;
                var p = map.nodes[r.node]; var m = mOn[i];
                string kind = !r.profiled ? "CAP" : r.half <= 0f ? "flush" : r.move > 0f ? "4-lane island" : "island";
                sb.Append($"\n    n{r.node}->n{r.far} e{r.edge} '{map.edges[r.edge].name}' ({LatLon(p.x, p.y)}; x {p.x:0} z {p.y:0}) {kind} wI {2f * r.half:0.00} edge line {r.o1:0.00}->{r.o2:0.00} outer widen {r.w1:0.00}/{r.w2:0.00} | slope {m.slope:0.000} at {m.slopeAt} lines {m.lineMove:0.000} outer {m.outerDev:0.000}{(r.check.Length > 0 ? " FAILS" + r.check : "")}");
            }
            foreach (var r in seps) if (former.Contains(r.node)) sb.Append($"\n    n{r.node}->n{r.far} e{r.edge} '{map.edges[r.edge].name}' separated (real {r.osmIsland:0} m median)");
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
