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
            int profiled = LineModel.RefugeProfiled, fallback = LineModel.RefugeFallback, capped = LineModel.RefugeTapers, flushN = LineModel.RefugeFlush;
            var mOn = MeasureRefuges(map, refs, out float stepOn, out int linesOn, out string worstOn);
            // round 3: one section normal at every profiled nose, the traffic's heading
            string wedge = NoseWedges(map, tOn, refs, out float wedgeMax, out int wedgeBad, out int wedgeMet, out int wedgeFan);
            string heading = Headings(map, refs, out float headOn, out float headOsm, out int headN);
            CityMeshes.EnsureMarks(map, tOn);
            string hatchDump = HatchDump(map, new HashSet<int> { 10229, 10230, 10231, 10232, 10236, 10238 });
            string bendDump = BendCells(map, new[] { 14094, 20695 });
            string timing = PavedTiming(map);
            var ms = CityMeshes.MarkStats;
            int noseOn = ms.noseArrows, noseSkip = ms.noseSkipped, groupsOn = ms.groups;
            string hatch = $"{ms.noseHatches} nose approaches hatched ({ms.noseHatchBars} bars, {ms.noseSolid} bounding lines made solid, {ms.noseWhiteOnly} on a texture with no solid yellow - white), {ms.noseNoRoom} plain noses with no centre lane to hatch";
            string cells = CityMeshes.MedianCellCensus(map);
            LineModel.RefugeOn = false;
            var tOff = CityMeshes.ComputeTrims(map);
            var mOff = MeasureRefuges(map, refs, out float stepOff, out int linesOff, out string worstOff);
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
            List<float> Col(List<RefugeM> m, System.Func<RefugeM, float> f, System.Func<int, bool> keep)
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
            Line($"REFUGE ISLANDS round 3 (PSX_CITY_REFUGE): {refs.Count / 2} pairs, {refs.Count} carriageways; {profiled} drawn by the island profile ({nCentre} beside a centre lane, {nFour} 4-lane with the lanes moved out), {fallback} pairs left on round 1's cap; {flushN / 2} profiled pairs with no room for a kerbed island drawn straight with a flush painted median; {capped} split-node tapers replaced");
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
            int badOuter = 0, badLine = 0, badSlope = 0, badSlopeOwn = 0, badIsl = 0, badMono = 0, badNose = 0, badFour = 0; string badAt = "";
            void Bad(ref int n, int i, string what) { n++; if (badAt.Length < 2500) badAt += $" n{refs[i].node}/e{refs[i].edge} {what};"; }
            for (int i = 0; i < refs.Count; i++)
            {
                if (!refs[i].profiled) continue;
                var m = mOn[i];
                if (Centre(i) && m.outerDev > 0.05f) Bad(ref badOuter, i, $"outer {m.outerDev:0.000}");
                if (Centre(i) && m.lineMove > 0.02f) Bad(ref badLine, i, $"line {m.lineMove:0.000}");
                if (Four(i) && (m.lineMove > 1.31f || refs[i].moveLen < 20f - 1e-3f)) Bad(ref badFour, i, $"4-lane move {m.lineMove:0.00} over {refs[i].moveLen:0.0}");
                bool own = Mathf.Abs(refs[i].uOut2 - refs[i].uOut1) / Mathf.Max(1f, refs[i].axLen) > 0.05f;
                if (m.slope > 0.06f && own) Bad(ref badSlopeOwn, i, $"edge-line slope");
                if (m.slope > 0.06f && !own) Bad(ref badSlope, i, $"slope {m.slope:0.000} (edge line {refs[i].uOut1:0.00}->{refs[i].uOut2:0.00} over {refs[i].axLen:0} m, move {refs[i].move:0.00} over {refs[i].moveLen:0})");
                if (Mathf.Abs(m.islandErr) > 0.1f) Bad(ref badIsl, i, $"island {m.island:0.00} vs {m.wI:0.00}");
                if (m.nonMono > 0.02f) Bad(ref badMono, i, $"narrows {m.nonMono:0.000}");
                if (m.noseW > 0.05f) Bad(ref badNose, i, $"nose {m.noseW:0.00}");
            }
            Check(badOuter == 0, "centre-lane islands: outer edge within 0.05 m of the undivided road's edge line", badOuter);
            Check(badLine == 0, "centre-lane islands: no lane line moves over 0.02 m", badLine);
            Check(badFour == 0, "4-lane islands: lanes move at most 1.3 m, eased over 20 m or more", badFour);
            Check(badSlope == 0, "no lane edge, line or traffic path slopes over 0.06 (where the undivided road keeps its width across the island)", badSlope);
            Line($"  OPEN: {badSlopeOwn} carriageways slope over 0.06 where the undivided road itself widens or narrows across the island by over 0.05 m per metre (OSM's widths differ at the two noses: a lane add/drop the profile carries on the edge line, not a wobble)");
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
            Line(hatchDump);
            FinishTo("city_medians.txt");
        }

        /// <summary>Round 3: at every profiled pair's two split nodes, each
        /// carriageway's first section against the undivided road's last:
        /// where they meet at the node itself (a mitred pair, no fan) both of
        /// the carriageway's corners must lie on the undivided road's cut line
        /// (one section normal) - the round-2 wedge was up to a metre there.</summary>
        static string NoseWedges(CityMap map, CityMeshes.Trims t, List<LineModel.Refuge> refs, out float worst, out int bad, out int met, out int fan)
        {
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
                    if (tu > 0.05f || tc > 0.05f) { fan++; continue; }
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
            return sb.Length > 0 ? "; over 0.02 m:" + sb : "";
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

        struct RefugeM { public float island, wI, islandErr, nonMono, noseW, outerDev, lineMove, pathMove, slope, step; }

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

        /// <summary>Per refuge carriageway, in its island's frame (see the
        /// class summary); over all of them the largest step in a drawn edge
        /// where two chain edges meet or where the chain leaves its split
        /// node, and the lane lines at each chain edge's mid.</summary>
        static List<RefugeM> MeasureRefuges(CityMap map, List<LineModel.Refuge> refs, out float worstStep, out int lines, out string worst)
        {
            var res = new List<RefugeM>(refs.Count);
            worstStep = 0f; lines = 0; worst = "-";
            var la = new List<LineModel.LineAt>(16);
            var splitU = new Dictionary<int, int>();
            if (map.splt != null) foreach (var sp in map.splt) splitU[sp.node] = sp.u;
            void Edges(CityMap.Edge e, float s, out Vector2 pl, out Vector2 mi)
            {
                LineModel.Extents(e, s, out float eM, out float eP);
                var p = e.PointAt(s); var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                if (LineModel.RefugeNormal(e, s, out var nL)) left = nL;
                pl = p + left * eP; mi = p - left * eM;
            }
            float SetStep(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1) =>
                Mathf.Min(Mathf.Max(Vector2.Distance(a0, b0), Vector2.Distance(a1, b1)), Mathf.Max(Vector2.Distance(a0, b1), Vector2.Distance(a1, b0)));
            // each carriageway's inner edge along its axis (for the island)
            var innerOf = new Dictionary<LineModel.Refuge, List<(float u, float lat)>>();
            var partial = new List<RefugeM>(refs.Count);
            foreach (var r in refs)
            {
                var m = new RefugeM { island = float.NaN, wI = 2f * r.half, islandErr = float.NaN, nonMono = float.NaN, noseW = float.NaN,
                                      outerDev = float.NaN, lineMove = float.NaN, pathMove = float.NaN, slope = float.NaN };
                var inner = new List<(float u, float lat)>();
                innerOf[r] = inner;
                int from = r.node;
                Vector2 prevPl = default, prevMi = default; bool havePrev = false; float endStep = 0f;
                float outerDev = 0f, lineMove = 0f, pathMove = 0f, slope = 0f;
                for (int k = 0; k < r.chain.Length; k++)
                {
                    var e = map.edges[r.chain[k]];
                    bool fwd = e.a == from;
                    Edges(e, fwd ? 0f : e.length, out var pl0, out var mi0);
                    if (havePrev)
                    {
                        float st = SetStep(pl0, mi0, prevPl, prevMi);
                        endStep = Mathf.Max(endStep, st);
                        if (st > worstStep) { worstStep = st; worst = $"e{e.index} at n{from}"; }
                    }
                    else if (splitU.TryGetValue(r.node, out int ui))
                    {
                        var U = map.edges[ui];
                        Edges(U, U.a == r.node ? 0f : U.length, out var upl, out var umi);
                        float st = Mathf.Min(Mathf.Min(Vector2.Distance(pl0, upl), Vector2.Distance(pl0, umi)),
                                             Mathf.Min(Vector2.Distance(mi0, upl), Vector2.Distance(mi0, umi)));
                        if (r.profiled)
                        {
                            // round 2: the jog ACROSS the road at the node, in the island's frame (the two
                            // sections are cut on different normals, so their corners differ along it)
                            r.Local(upl, out _, out float a1); r.Local(umi, out _, out float a2);
                            r.Local(pl0, out _, out float b1); r.Local(mi0, out _, out float b2);
                            st = Mathf.Min(Mathf.Abs(Mathf.Max(a1, a2) - Mathf.Max(b1, b2)), st);
                        }
                        endStep = Mathf.Max(endStep, st);
                        if (st > worstStep) { worstStep = st; worst = $"e{e.index} at split n{from}"; }
                    }
                    if (r.profiled)
                    {
                        // along the edge every 0.5 m, in the island's frame
                        var lineMin = new Dictionary<int, float>(); var lineMax = new Dictionary<int, float>();
                        var prevLine = new Dictionary<int, (float u, float lat)>();
                        float pMin = float.MaxValue, pMax = float.MinValue; float pu = float.NaN, plat = 0f, ou = float.NaN, olat = 0f;
                        for (float x = 0.25f; x <= e.length - 0.25f + 1e-3f; x += 0.5f)
                        {
                            float s = fwd ? x : e.length - x;
                            Edges(e, s, out var pl, out var mi);
                            r.Local(pl, out float u1, out float l1); r.Local(mi, out float u2, out float l2);
                            float uo = l1 > l2 ? u1 : u2, lo = Mathf.Max(l1, l2), ui = l1 > l2 ? u2 : u1, li = Mathf.Min(l1, l2);
                            float target = r.OuterTarget(uo);
                            outerDev = Mathf.Max(outerDev, Mathf.Abs(lo - target));
                            inner.Add((ui, li));
                            if (!float.IsNaN(ou) && Mathf.Abs(uo - ou) > 0.3f) slope = Mathf.Max(slope, Mathf.Abs((lo - olat) / (uo - ou)));
                            ou = uo; olat = lo;
                            // the lines: against the undivided road's edge line
                            LineModel.LinesAt(e, s, la);
                            var p = e.PointAt(s); var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                            if (LineModel.RefugeNormal(e, s, out var nL2)) left = nL2;
                            foreach (var l in la)
                            {
                                r.Local(p + left * l.lat, out float lu, out float ll);
                                float off = r.OuterTarget(lu) - ll;
                                lineMin[l.k] = lineMin.TryGetValue(l.k, out float a0) ? Mathf.Min(a0, off) : off;
                                lineMax[l.k] = lineMax.TryGetValue(l.k, out float a1) ? Mathf.Max(a1, off) : off;
                                if (prevLine.TryGetValue(l.k, out var pv) && Mathf.Abs(lu - pv.u) > 0.3f) slope = Mathf.Max(slope, Mathf.Abs((ll - pv.lat) / (lu - pv.u)));
                                prevLine[l.k] = (lu, ll);
                            }
                            // the traffic path
                            r.Local(LineModel.OwnLanePoint(e, s), out float tu, out float tl);
                            float toff = r.OuterTarget(tu) - tl;
                            pMin = Mathf.Min(pMin, toff); pMax = Mathf.Max(pMax, toff);
                            if (!float.IsNaN(pu) && Mathf.Abs(tu - pu) > 0.3f) slope = Mathf.Max(slope, Mathf.Abs((tl - plat) / (tu - pu)));
                            pu = tu; plat = tl;
                        }
                        foreach (var kv in lineMin) lineMove = Mathf.Max(lineMove, lineMax[kv.Key] - kv.Value);
                        if (pMax >= pMin) pathMove = Mathf.Max(pathMove, pMax - pMin);
                    }
                    Edges(e, fwd ? e.length : 0f, out prevPl, out prevMi); havePrev = true;
                    LineModel.LinesAt(e, e.length * 0.5f, la); lines += la.Count;
                    from = fwd ? e.b : e.a;
                }
                m.step = endStep;
                if (r.profiled) { m.outerDev = outerDev; m.lineMove = lineMove; m.pathMove = pathMove; m.slope = slope; }
                partial.Add(m);
            }
            // the island: this carriageway's inner edge plus its partner's at the same axis arc
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i]; var m = partial[i];
                LineModel.Refuge o = null;
                foreach (var q in refs) if (q != r && q.node == r.node && q.far == r.far) { o = q; break; }
                if (r.profiled && o != null)
                {
                    var A = innerOf[r]; var B = innerOf[o];
                    A.Sort((x, y) => x.u.CompareTo(y.u)); B.Sort((x, y) => x.u.CompareTo(y.u));
                    float At(List<(float u, float lat)> l, float u)
                    {
                        if (l.Count == 0) return float.NaN;
                        if (u <= l[0].u) return l[0].lat;
                        for (int k = 1; k < l.Count; k++)
                            if (l[k].u >= u) { float f = (u - l[k - 1].u) / Mathf.Max(1e-4f, l[k].u - l[k - 1].u); return Mathf.Lerp(l[k - 1].lat, l[k].lat, f); }
                        return l[l.Count - 1].lat;
                    }
                    float L = r.axLen, mid = 0.5f * L;
                    float W(float u) => Mathf.Max(0f, At(A, u) + At(B, u));
                    m.island = W(mid); m.islandErr = m.island - m.wI;
                    // never narrower toward mid than nearer a nose; nothing before the tip
                    float nm = 0f, nw = 0f, best = 0f;
                    for (float u = 0.5f; u <= mid; u += 0.25f) { float w = W(u); nm = Mathf.Max(nm, best - w); best = Mathf.Max(best, w); }
                    best = 0f;
                    for (float u = L - 0.5f; u >= mid; u -= 0.25f) { float w = W(u); nm = Mathf.Max(nm, best - w); best = Mathf.Max(best, w); }
                    for (float u = 0.5f; u < r.x0s - 0.25f; u += 0.25f) nw = Mathf.Max(nw, W(u));
                    for (float u = L - 0.5f; u > L - r.x0e + 0.25f; u -= 0.25f) nw = Mathf.Max(nw, W(u));
                    m.nonMono = nm; m.noseW = nw;
                }
                res.Add(m);
            }
            return res;
        }
    }
}
