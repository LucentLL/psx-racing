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

            int joins = 0, sym = 0, symCore = 0, symRoute = 0;
            int jumpN = 0, jumpCore = 0, jumpRoute = 0; float jumpMax = 0f; string jumpWorst = null;
            var la = new List<LineModel.LineAt>(16); var lb = new List<LineModel.LineAt>(16);
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.mitre[n] || trims.throughA[n] < 0 || trims.throughB[n] < 0) continue;
                var E = map.edges[trims.throughA[n]]; var O = map.edges[trims.throughB[n]];
                if (E == O || E.a == E.b || O.a == O.b) continue;
                joins++;
                bool core = InCore(map.nodes[n]), route = onRoute.Contains(E.index) || onRoute.Contains(O.index);
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
                if (laneChange && ((dL > V && dR > V) || (dL < -V && dR < -V)))
                {
                    sym++; if (core) symCore++; if (route) symRoute++;
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
                    if (jumpN <= 6) Line($"  line jump: node {n} e{E.index} '{E.name}' {RoadProfiles.All[E.profile].key} / e{O.index} {RoadProfiles.All[O.profile].key} kind {kf} {best:0.00} m (few {few.Count} lines, many {many.Count}; eases E {E.lmEase?.Length ?? 0} O {O.lmEase?.Length ?? 0})");
                    if (best > jumpMax) { jumpMax = best; jumpWorst = $"node {n} e{E.index} '{E.name}' {layE.W:0.0} m / e{O.index} {layO.W:0.0} m, line kind {kf}"; }
                }
            }
            Line($"  symmetric widenings at mitred joins (a lane added or dropped, both edges moving more than V): {sym} of {joins} joins (core {symCore}, routes {symRoute}); goal 0");
            Line($"  through-lane continuity: {jumpN} lines jump more than V at a join (core {jumpCore}, routes {jumpRoute}), worst {jumpMax:0.00} m{(jumpWorst != null ? " at " + jumpWorst : "")}; goal 0");

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
    }
}
