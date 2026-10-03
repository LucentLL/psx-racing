using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityAudit
    {
        /// <summary>
        /// THE LINE MODEL (WP-11b, plan A8's acceptance): read off the model the
        /// tiles are drawn from, city-wide, and in the 8 x 8 km core and on the
        /// race routes.
        ///   * one-sided tapers: mitred joins eased, of them on a class default
        ///     length (no TAPR record for that side);
        ///   * SYMMETRIC WIDENINGS: joins where one arm is wider than the other
        ///     on BOTH sides by more than V (the owner: "expand a lane and widen
        ///     on both sides, as if center aligned") - goal 0;
        ///   * THROUGH-LANE CONTINUITY: at every mitred join, each line of the
        ///     arm with fewer lines against the nearest line of its kind on the
        ///     other arm, where they meet: a jump over V is a through lane that
        ///     does not run on - goal 0;
        ///   * MERGED CARRIAGEWAYS: two one-way carriageways of one road running
        ///     opposite ways side by side whose pavements, as drawn, touch or
        ///     overlap (less than 0.3 m apart) away from where they meet - goal 0.
        /// Roads pass L4 (lanes line up), each also in the OwnerBox and on T1:
        ///   * a widening at a join explained by the line sets - each direction
        ///     adding its lane on its own outside (owner Q4) - is not symmetric;
        ///   * LANE ALIGN: at every fan junction (not a split), each pair of arms
        ///     running straight through: the travel lanes in against the travel
        ///     lanes out, a lane of the arm with fewer with none of the other
        ///     within V - goal 0;
        ///   * TAPER: one-sided eases steeper than 10.6 degrees at their middle
        ///     (smoothstep: 1.5 dw / len), and relays shorter than the MUTCD
        ///     length;
        ///   * OFFSET: edges drawn more than a lane (3.66 m + 5 cm) off their OSM line.
        /// </summary>
        static void LineModelReport(CityMap map, CityMeshes.Trims trims)
        {
            const float V = 0.025f;
            var ux = map.uptown;
            bool InCore(Vector2 p) => Mathf.Abs(p.x - ux.x) <= 4000f && Mathf.Abs(p.y - ux.y) <= 4000f;
            var onRoute = new HashSet<int>();
            foreach (var r in map.routes) foreach (var ei in r.edges) onRoute.Add(ei);
            int offEdges = 0; double offKm = 0;
            foreach (var e in map.edges) if (Mathf.Abs(e.lmOff) > 0.01f) { offEdges++; offKm += e.length / 1000.0; }
            int easedEdges = 0; foreach (var e in map.edges) if (e.lmEase != null) easedEdges++;
            Line($"LINE MODEL (WP-11b): {offEdges} edges drawn off their OSM line ({offKm:0.0} km: TAPR, a lane added on one side), " +
                 $"{LineModel.EasedJoins} one-sided tapers at mitred joins ({LineModel.EasedDefault} on the class default length, no TAPR record for that side; " +
                 $"{LineModel.HeldJoins} where both edges stepped: one held, the narrow arm shifted to it; {LineModel.CrossedJoins} where the two stuck out on opposite sides: the narrower shifted), {LineModel.SplitTapers} median tapers at {map.splt?.Length ?? 0} splits (SPLT), " +
                 $"{LineModel.MirroredFixed} mirrored TAPR offsets put right ({LineModel.MirroredLeft} left: another change first), on {easedEdges} edges");
            Line($"  L4: {LineModel.RelayJoins} relays (two arms of one width whose line sets differ: the lines move to meet over the MUTCD length, {LineModel.RelayShort} shorter for want of room), " +
                 $"{LineModel.TwoSidedJoins} two-sided widenings (Q4: each direction on its own outside), {LineModel.ReShifts} SHIFT records (a run re-anchored at a junction eased back past it)");
            var box = OwnerBox;
            bool T1(CityMap.Edge e) => e.cls >= 3;
            void DirLanes(CityMap.Edge e, bool along, out int fwd, out int bwd)
            {
                int nF, nB;
                if (e.hasLset) { nF = e.lsNF; nB = e.lsNB; }
                else RoadProfiles.DefaultSplit(RoadProfiles.All[e.profile], out nF, out nB, out _);
                fwd = along ? nF : nB; bwd = along ? nB : nF;
            }

            int joins = 0, sym = 0, symCore = 0, symRoute = 0, symQ4 = 0, symBox = 0, symT1 = 0;
            int jumpN = 0, jumpCore = 0, jumpRoute = 0; float jumpMax = 0f; string jumpWorst = null;
            int jumpBox = 0, jumpT1 = 0, jumpBoxT1 = 0, jumpRelayed = 0, jumpListed = 0;
            var la = new List<LineModel.LineAt>(16); var lb = new List<LineModel.LineAt>(16);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.mitre[n] || trims.throughA[n] < 0 || trims.throughB[n] < 0) continue;
                var E = map.edges[trims.throughA[n]]; var O = map.edges[trims.throughB[n]];
                if (E == O || E.a == E.b || O.a == O.b) continue;
                joins++;
                bool core = InCore(map.nodes[n]), route = onRoute.Contains(E.index) || onRoute.Contains(O.index);
                bool inBox = box.Contains(map.nodes[n]), t1 = T1(E) || T1(O);
                // travel: along E into n, along O out of n; + = left of travel
                bool eIn = E.b == n, oOut = O.a == n;
                float sE = eIn ? E.length : 0f, sO = oOut ? 0f : O.length;
                // symmetric: E's full extents against O's (each with its median
                // taper's shift at the node, not its lane taper), both sides one way
                float shE = LineModel.ShiftAt(E, sE), shO = LineModel.ShiftAt(O, sO);
                float ePE = E.lmPlus + shE, eME = E.lmMinus - shE, ePO = O.lmPlus + shO, eMO = O.lmMinus - shO;
                float dL = (eIn ? ePE : eME) - (oOut ? ePO : eMO);
                float dR = (eIn ? eME : ePE) - (oOut ? eMO : ePO);
                // a lane added or dropped (not a shoulder change: xw -> mw) with both edges moving
                bool laneChange = RoadProfiles.All[E.profile].lanes != RoadProfiles.All[O.profile].lanes;
                // owner Q4 (L4): each direction adding (or dropping) its own lanes
                // on its own outside is the rule, not a symmetric widening
                DirLanes(E, eIn, out int fE, out int bE); DirLanes(O, oOut, out int fO, out int bO);
                int cE = E.hasLset ? E.lsCentre : (RoadProfiles.All[E.profile].turnLane ? 2 : 1), cO = O.hasLset ? O.lsCentre : (RoadProfiles.All[O.profile].turnLane ? 2 : 1);
                bool q4 = (fE != fO && bE != bO && Mathf.Sign(dR) == Mathf.Sign(fE - fO) && Mathf.Sign(dL) == Mathf.Sign(bE - bO))
                          || (cE != cO && (fE != fO || bE != bO));      // a direction's lane and the centre lane, one each side
                if (laneChange && q4 && ((dL > V && dR > V) || (dL < -V && dR < -V))) symQ4++;
                else if (laneChange && ((dL > V && dR > V) || (dL < -V && dR < -V)))
                {
                    sym++; if (core) symCore++; if (route) symRoute++; if (inBox) symBox++; if (t1) symT1++;
                    if (sym <= 8) Line($"  symmetric widening: node {n} e{E.index} '{E.name}' {RoadProfiles.All[E.profile].key} / e{O.index} {RoadProfiles.All[O.profile].key} left {dL:+0.00;-0.00} right {dR:+0.00;-0.00}");
                }
                // through lines where the two meet
                LineModel.LinesAt(E, sE, la); LineModel.LinesAt(O, sO, lb);
                var layE = LineModel.LayoutOf(E); var layO = LineModel.LayoutOf(O);
                bool fewE = la.Count <= lb.Count;
                var few = fewE ? la : lb; var many = fewE ? lb : la;
                var layF = fewE ? layE : layO; var layM = fewE ? layO : layE;
                float sgnF = fewE ? (eIn ? 1f : -1f) : (oOut ? 1f : -1f), sgnM = fewE ? (oOut ? 1f : -1f) : (eIn ? 1f : -1f);
                foreach (var f in few)
                {
                    byte kf = layF.kind[f.k];
                    float best = float.MaxValue;
                    foreach (var m in many)
                    {
                        byte km = layM.kind[m.k];
                        // a one-way's left edge line is yellow: it runs on from a
                        // two-way road's centre line where a divided road begins
                        bool yf = kf == LineModel.KYellow || kf == LineModel.KYellowDash || (kf == LineModel.KEdgeP && !layF.twoWay);
                        bool ym = km == LineModel.KYellow || km == LineModel.KYellowDash || (km == LineModel.KEdgeP && !layM.twoWay);
                        bool same = kf == km || (yf && ym) || (kf <= LineModel.KEdgeM && km <= LineModel.KEdgeM);
                        if (!same) continue;
                        best = Mathf.Min(best, Mathf.Abs(sgnF * f.lat - sgnM * m.lat));
                    }
                    if (best == float.MaxValue || best <= V) continue;
                    jumpN++; if (core) jumpCore++; if (route) jumpRoute++;
                    if (inBox) jumpBox++; if (t1) jumpT1++; if (inBox && t1) jumpBoxT1++;
                    if (LineModel.Relayed(E, sE) || LineModel.Relayed(O, sO))
                    {
                        jumpRelayed++;
                        if (System.Environment.GetEnvironmentVariable("PSX_LM_DEBUG") == "1" && jumpRelayed <= 6)
                        {
                            string Ls(List<LineModel.LineAt> l, LineModel.Layout y, float sg) { var sb = new System.Text.StringBuilder(); foreach (var q in l) sb.Append($" {y.kind[q.k]}@{sg * q.lat:0.00}"); return sb.ToString(); }
                            string Es(CityMap.Edge x) { var sb = new System.Text.StringBuilder(); if (x.lmEase != null) foreach (var z in x.lmEase) sb.Append($" [{(z.relay ? "relay" : z.shift ? "shift" : "ease")} side {z.side} dw {z.dw:0.00} len {z.len:0.0} d0 {z.d0:0.0} fromA {z.fromA}]"); return sb.ToString(); }
                            Line($"    RELAYDBG node {n} kind {kf} {best:0.00}: E e{E.index} len {E.length:0.0} eIn {eIn} lines{Ls(la, layE, eIn ? 1f : -1f)} eases{Es(E)} | O e{O.index} len {O.length:0.0} oOut {oOut} lines{Ls(lb, layO, oOut ? 1f : -1f)} eases{Es(O)}");
                        }
                    }
                    if (inBox && jumpListed++ < 8) Line($"  line jump (OwnerBox): node {n} e{E.index} '{E.name}' {RoadProfiles.All[E.profile].key} {fE}+{bE} / e{O.index} {RoadProfiles.All[O.profile].key} {fO}+{bO} kind {kf} {best:0.00} m (few {few.Count} lines, many {many.Count}; eases E {E.lmEase?.Length ?? 0} O {O.lmEase?.Length ?? 0})");
                    if (jumpN <= 6) Line($"  line jump: node {n} e{E.index} '{E.name}' {RoadProfiles.All[E.profile].key} / e{O.index} {RoadProfiles.All[O.profile].key} kind {kf} {best:0.00} m (few {few.Count} lines, many {many.Count}; eases E {E.lmEase?.Length ?? 0} O {O.lmEase?.Length ?? 0})");
                    if (best > jumpMax) { jumpMax = best; jumpWorst = $"node {n} e{E.index} '{E.name}' {layE.W:0.0} m / e{O.index} {layO.W:0.0} m, line kind {kf}"; }
                }
            }
            Line($"  symmetric widenings at mitred joins (a lane added or dropped, both edges moving more than V): {sym} of {joins} joins (core {symCore}, routes {symRoute}; OwnerBox {symBox}, T1 {symT1}); goal 0 - and {symQ4} widened per direction on their own outsides (owner Q4, the rule)");
            Line($"  through-lane continuity: {jumpN} lines jump more than V at a join (core {jumpCore}, routes {jumpRoute}), worst {jumpMax:0.00} m{(jumpWorst != null ? " at " + jumpWorst : "")}; goal 0");
            Line($"  through-lane continuity (L4): OwnerBox {jumpBox} (T1 {jumpBoxT1}), T1 city-wide {jumpT1}; {jumpRelayed} of them at a relay; goal 0");
            LaneAlignReport(map, trims, box);

            // merged carriageways: opposite one-ways of one road side by side
            var segs = new HashSet<int>();
            double mergedM = 0, mergedCore = 0, mergedRoute = 0; string mergedWorst = null; float worstGap = float.MaxValue;
            const float Step = 5f;
            foreach (var e in map.edges)
            {
                if (!e.oneway || e.link || e.a == e.b) continue;
                bool eRoute = onRoute.Contains(e.index);
                for (float s = Step * 0.5f; s < e.length; s += Step)
                {
                    var p = e.PointAt(s);
                    bool core = InCore(p);
                    if (!core && !eRoute) continue;
                    var t = e.TangentAt(s); var r = new Vector2(-t.y, t.x);
                    LineModel.CentreAt(e, s, out float cE, out float hE);
                    var pc = p + r * cE;
                    segs.Clear();
                    map.EdgeSegsNear(p - Vector2.one * 30f, p + Vector2.one * 30f, segs);
                    float gapMin = float.MaxValue;
                    foreach (var packed in segs)
                    {
                        int oi = packed >> 12, si = packed & 0xFFF;
                        if (oi == e.index) continue;
                        var o = map.edges[oi];
                        if (!o.oneway || o.link || o.name != e.name || string.IsNullOrEmpty(e.name)) continue;
                        if (o.a == e.a || o.a == e.b || o.b == e.a || o.b == e.b) continue;
                        Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude; if (L2 < 1e-6f) continue;
                        var dn = d / Mathf.Sqrt(L2);
                        if (Vector2.Dot(dn, t) > -0.9f) continue;   // opposite ways only
                        float tq = Vector2.Dot(p - a, d) / L2;
                        if (tq <= 0f || tq >= 1f) continue;
                        float at = o.s[si] + Mathf.Sqrt(L2) * tq;
                        // away from where they meet: 60 m from either one's ends
                        if (s < 60f || e.length - s < 60f || at < 60f || o.length - at < 60f) continue;
                        LineModel.CentreAt(o, at, out float cO, out float hO);
                        var qc = a + d * tq + new Vector2(-dn.y, dn.x) * cO;
                        float gap = Vector2.Distance(pc, qc) - hE - hO;
                        gapMin = Mathf.Min(gapMin, gap);
                    }
                    if (gapMin >= 0.3f) continue;
                    mergedM += Step; if (core) mergedCore += Step; if (eRoute) mergedRoute += Step;
                    if (gapMin < worstGap) { worstGap = gapMin; mergedWorst = $"e{e.index} '{e.name}' s={s:0} ({p.x:0}, {p.y:0}) gap {gapMin:+0.00;-0.00} m"; }
                }
            }
            Line($"  merged carriageways (opposite one-ways of one road, drawn under 0.3 m apart, 60 m clear of their ends): {mergedM:0} m (core {mergedCore:0}, routes {mergedRoute:0}){(mergedWorst != null ? ", worst " + mergedWorst : "")}; goal 0");
        }

        /// <summary>THE LINE MODEL REPORT ALONE (roads pass L4): the map, the
        /// trims, <see cref="LineModelReport"/> and the PAINT report - a lane package's iteration
        /// in about two minutes, not the whole audit's fifteen. Writes
        /// line_model.txt beside the project. Headless: -executeMethod
        /// PSXRacing.EditorTools.CityAudit.LineModelOnly</summary>
        public static void LineModelOnly()
        {
            outLog = new System.Text.StringBuilder();
            var map = CityMap.Get();
            if (map == null) { Line("charlotte_city.bytes missing from Resources"); }
            else
            {
                var trims = CityMeshes.NodeTrims(map);
                LineModelReport(map, trims);
                PaintReport(map, trims);    // seconds: no tile builds
                MergeReport(map, trims);    // roads pass L5: plan geometry, seconds
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "line_model.txt"), outLog.ToString());
        }

        /// <summary>Roads pass L4: LANE ALIGN at fan junctions, TAPER and
        /// OFFSET (see <see cref="LineModelReport"/>).</summary>
        static void LaneAlignReport(CityMap map, CityMeshes.Trims trims, Rect box)
        {
            const float V = 0.025f, LANE = RoadProfiles.LaneM;
            bool T1(CityMap.Edge e) => e.cls >= 3;
            var splitNodes = new HashSet<int>();
            if (map.splt != null) foreach (var sp in map.splt) splitNodes.Add(sp.node);
            // the travel lanes at s (+ left of travel), from the line set and
            // the drawn extents (median and SHIFT tapers included)
            void TravelLanes(CityMap.Edge e, bool along, float s, List<float> into)
            {
                into.Clear();
                LineModel.Extents(e, s, out float eM, out float eP);
                var pr = RoadProfiles.All[e.profile];
                int nF, nB, c;
                if (e.oneway) { if (!along) return; nF = pr.lanes; nB = 0; c = 0; }
                else if (e.hasLset) { nF = e.lsNF; nB = e.lsNB; c = e.lsCentre == 2 ? 1 : 0; }
                else { RoadProfiles.DefaultSplit(pr, out nF, out nB, out int dc); c = dc == 2 ? 1 : 0; }
                float top = eP - e.shl;
                if (along) for (int k = 0; k < nF; k++) into.Add(top - LANE * (nB + c + k + 0.5f));
                else for (int k = 0; k < nB; k++) into.Add(-(top - LANE * (k + 0.5f)));
            }
            int pairs = 0, bad = 0, badT1 = 0, badBox = 0, badBoxT1 = 0, listed = 0; float worst = 0f; string worstAt = null;
            var li = new List<float>(8); var lj = new List<float>(8);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.patch[n] || splitNodes.Contains(n)) continue;
                var list = map.nodeEdges[n];
                for (int i = 0; i < list.Count; i++)
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (i == j) continue;
                        var I = map.edges[list[i]]; var J = map.edges[list[j]];
                        if (I == J || I.a == I.b || J.a == J.b || I.link || J.link) continue;
                        Vector2 dI = I.a == n ? I.TangentAt(0f) : -I.TangentAt(I.length);
                        Vector2 dJ = J.a == n ? J.TangentAt(0f) : -J.TangentAt(J.length);
                        if (Vector2.Dot(dI, dJ) >= -0.85f) continue;
                        bool iAlong = I.b == n, jAlong = J.a == n;     // travel: along I into n, along J out of it
                        TravelLanes(I, iAlong, iAlong ? I.length : 0f, li);
                        TravelLanes(J, jAlong, jAlong ? 0f : J.length, lj);
                        if (li.Count == 0 || lj.Count == 0) continue;
                        pairs++;
                        var few = li.Count <= lj.Count ? li : lj; var many = li.Count <= lj.Count ? lj : li;
                        bool inBox = box.Contains(map.nodes[n]), t1 = T1(I) && T1(J);
                        foreach (var f in few)
                        {
                            float b = float.MaxValue;
                            foreach (var m in many) b = Mathf.Min(b, Mathf.Abs(f - m));
                            if (b <= V) continue;
                            bad++; if (t1) badT1++; if (inBox) { badBox++; if (t1) badBoxT1++; }
                            if (inBox && listed++ < 8) Line($"  lane off (OwnerBox): node {n} e{I.index} '{I.name}' {RoadProfiles.All[I.profile].key} -> e{J.index} '{J.name}' {RoadProfiles.All[J.profile].key}: {b:0.00} m");
                            if (b > worst) { worst = b; worstAt = $"node {n} e{I.index} '{I.name}' -> e{J.index} '{J.name}'"; }
                        }
                    }
            }
            Line($"  LANE ALIGN (L4, fan junctions, splits excepted): {bad} through lanes off more than V on {pairs} straight-through arm pairs (T1 {badT1}; OwnerBox {badBox}, T1 {badBoxT1}), worst {worst:0.00} m{(worstAt != null ? " at " + worstAt : "")}; goal 0");

            // TAPER: each one-sided ease at its join (not run on), steeper than
            // 10.6 degrees at its middle; OFFSET: drawn more than a lane off
            float tan106 = Mathf.Tan(10.6f * Mathf.Deg2Rad);
            int eases = 0, steep = 0, steepT1 = 0, steepBox = 0; float steepWorst = 0f; string steepAt = null;
            int off = 0, offT1 = 0, offBox = 0; float offWorst = 0f;
            foreach (var e in map.edges)
            {
                var mid = e.PointAt(e.length * 0.5f);
                bool inBox = box.Contains(mid);
                if (Mathf.Abs(e.lmOff) > LANE + 0.05f) { off++; if (T1(e)) offT1++; if (inBox) offBox++; offWorst = Mathf.Max(offWorst, Mathf.Abs(e.lmOff)); }
                if (e.lmEase == null) continue;
                foreach (var z in e.lmEase)
                {
                    if (z.shift || z.relay || z.aux || z.centreOnly || z.d0 > 0f) continue;
                    eases++;
                    float slope = 1.5f * Mathf.Abs(z.dw) / Mathf.Max(0.01f, z.len);
                    if (slope <= tan106) continue;
                    steep++; if (T1(e)) steepT1++; if (inBox) steepBox++;
                    float deg = Mathf.Atan(slope) * Mathf.Rad2Deg;
                    if (deg > steepWorst) { steepWorst = deg; steepAt = $"e{e.index} '{e.name}' dw {z.dw:0.00} m over {z.len:0.0} m"; }
                }
            }
            Line($"  TAPER (L4): {steep} of {eases} one-sided eases steeper than 10.6 deg at their middle (T1 {steepT1}, OwnerBox {steepBox}), worst {steepWorst:0.0} deg{(steepAt != null ? " at " + steepAt : "")}; relays shorter than the MUTCD length: {LineModel.RelayShort} of {LineModel.RelayJoins}");
            Line($"  OFFSET (L4): {off} edges drawn more than a lane (3.71 m) off their OSM line (T1 {offT1}, OwnerBox {offBox}), worst {offWorst:0.00} m; goal 0");
        }
    }
}
