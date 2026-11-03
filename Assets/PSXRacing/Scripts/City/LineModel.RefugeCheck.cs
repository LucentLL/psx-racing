using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE REFUGE MEASUREMENT (medians round 4, 2026-11-03): one function for
    /// the medians gate (CityAudit.RunMedianGates) and the builder's own
    /// self-check (LineModel.VerifyRefuges), so a pair can never pass one and
    /// fail the other (round 3: n12028). Every refuge carriageway is measured
    /// in its island's own frame (Refuge.Local: the axis carried through
    /// between the two split nodes, lateral + outward), on the drawn geometry:
    ///   * outer edge: its deviation from the undivided road's edge line
    ///     (centre-lane islands: goal 0.05 m), or (4-lane) its move;
    ///   * lane lines (LinesAt) and the traffic path (OwnLanePoint): the most
    ///     any one moves laterally against the lanes' straight reference
    ///     (Refuge.LaneRef; centre-lane goal 0.02 m, 4-lane 1.3 m) and the
    ///     largest lateral slope of any lane edge, line or path (goal 0.06);
    ///   * the island (inner pavement edge to inner pavement edge): at mid
    ///     against the design wI (goal 0.1 m), never narrowing from a nose
    ///     to mid, none before the tip;
    ///   * the step in a drawn edge at every chain joint and where the chain
    ///     leaves its split node; the lane lines on every chain edge.
    /// </summary>
    public static partial class LineModel
    {
        public struct RefugeM { public float island, wI, islandErr, nonMono, noseW, outerDev, lineMove, pathMove, slope, step; public string slopeAt; }

        /// <summary>The checks a profiled carriageway fails ("" = none), each
        /// ending in ';' - the medians gate's own thresholds.</summary>
        public static string RefugeFails(Refuge r, RefugeM m)
        {
            if (!r.profiled) return "not profiled;";
            string f = "";
            bool centre = r.move <= 0f;
            if (centre && m.outerDev > 0.05f) f += $" outer {m.outerDev:0.000};";
            if (centre && m.lineMove > 0.02f) f += $" line {m.lineMove:0.000};";
            if (!centre && (m.lineMove > 1.31f || r.moveLen < 20f - 1e-3f)) f += $" 4-lane move {m.lineMove:0.00} over {r.moveLen:0.0};";
            if (m.slope > 0.06f) f += $" slope {m.slope:0.000} ({m.slopeAt});";
            if (Mathf.Abs(m.islandErr) > 0.1f) f += $" island {m.island:0.00} vs {m.wI:0.00};";
            if (m.nonMono > 0.02f) f += $" narrows {m.nonMono:0.000};";
            if (m.noseW > 0.05f) f += $" nose {m.noseW:0.00};";
            return f;
        }

        /// <summary>Per refuge carriageway, in its island's frame (see the
        /// class summary); over all of them the largest step in a drawn edge
        /// where two chain edges meet or where the chain leaves its split
        /// node, and the lane lines at each chain edge's mid. A carriageway's
        /// partner (same node and far node) must be in the list for its island.</summary>
        public static List<RefugeM> MeasureRefuges(CityMap map, List<Refuge> refs, out float worstStep, out int lines, out string worst)
        {
            var res = new List<RefugeM>(refs.Count);
            worstStep = 0f; lines = 0; worst = "-";
            var la = new List<LineAt>(16);
            void Edges(CityMap.Edge e, float s, out Vector2 pl, out Vector2 mi)
            {
                Extents(e, s, out float eM, out float eP);
                var p = e.PointAt(s); var left = SectionLeft(e, s);
                pl = p + left * eP; mi = p - left * eM;
            }
            float SetStep(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1) =>
                Mathf.Min(Mathf.Max(Vector2.Distance(a0, b0), Vector2.Distance(a1, b1)), Mathf.Max(Vector2.Distance(a0, b1), Vector2.Distance(a1, b0)));
            // each carriageway's inner edge along its axis (for the island)
            var innerOf = new Dictionary<Refuge, List<(float u, float lat)>>();
            var partial = new List<RefugeM>(refs.Count);
            foreach (var r in refs)
            {
                var m = new RefugeM { island = float.NaN, wI = 2f * r.half, islandErr = float.NaN, nonMono = float.NaN, noseW = float.NaN,
                                      outerDev = float.NaN, lineMove = float.NaN, pathMove = float.NaN, slope = float.NaN, slopeAt = "" };
                var inner = new List<(float u, float lat)>();
                innerOf[r] = inner;
                int from = r.node;
                Vector2 prevPl = default, prevMi = default; bool havePrev = false; float endStep = 0f;
                float outerDev = 0f, lineMove = 0f, pathMove = 0f, slope = 0f; string slopeAt = "";
                void Slope(float v, string what, CityMap.Edge e, float s) { if (v > slope) { slope = v; slopeAt = $"{what} e{e.index} s {s:0.0}"; } }
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
                    else if (r.profiled && SplitU(map, r.node, out int ui))
                    {
                        var U = map.edges[ui];
                        Edges(U, U.a == r.node ? 0f : U.length, out var upl, out var umi);
                        float st = Mathf.Min(Mathf.Min(Vector2.Distance(pl0, upl), Vector2.Distance(pl0, umi)),
                                             Mathf.Min(Vector2.Distance(mi0, upl), Vector2.Distance(mi0, umi)));
                        // the jog ACROSS the road at the node, in the island's frame
                        r.Local(upl, out _, out float a1); r.Local(umi, out _, out float a2);
                        r.Local(pl0, out _, out float b1); r.Local(mi0, out _, out float b2);
                        st = Mathf.Min(Mathf.Abs(Mathf.Max(a1, a2) - Mathf.Max(b1, b2)), st);
                        endStep = Mathf.Max(endStep, st);
                        if (st > worstStep) { worstStep = st; worst = $"e{e.index} at split n{from}"; }
                    }
                    else if (SplitU(map, r.node, out int ui2))
                    {
                        var U = map.edges[ui2];
                        Edges(U, U.a == r.node ? 0f : U.length, out var upl, out var umi);
                        float st = Mathf.Min(Mathf.Min(Vector2.Distance(pl0, upl), Vector2.Distance(pl0, umi)),
                                             Mathf.Min(Vector2.Distance(mi0, upl), Vector2.Distance(mi0, umi)));
                        endStep = Mathf.Max(endStep, st);
                        if (st > worstStep) { worstStep = st; worst = $"e{e.index} at split n{from}"; }
                    }
                    if (r.profiled)
                    {
                        // along the edge every 0.5 m, in the island's frame
                        var lineMin = new Dictionary<int, float>(); var lineMax = new Dictionary<int, float>();
                        var prevLine = new Dictionary<int, (float u, float lat)>();
                        int hint = 0;
                        float pMin = float.MaxValue, pMax = float.MinValue; float pu = float.NaN, plat = 0f, ou = float.NaN, olat = 0f, iu = float.NaN, ilat = 0f;
                        for (float x = 0.25f; x <= e.length - 0.25f + 1e-3f; x += 0.5f)
                        {
                            float s = fwd ? x : e.length - x;
                            Edges(e, s, out var pl, out var mi);
                            r.LocalNear(pl, ref hint, out float u1, out float l1); r.LocalNear(mi, ref hint, out float u2, out float l2);
                            float uo = l1 > l2 ? u1 : u2, lo = Mathf.Max(l1, l2), uin = l1 > l2 ? u2 : u1, li = Mathf.Min(l1, l2);
                            outerDev = Mathf.Max(outerDev, Mathf.Abs(lo - r.OuterTarget(uo)));
                            inner.Add((uin, li));
                            if (!float.IsNaN(ou) && Mathf.Abs(uo - ou) > 0.3f) Slope(Mathf.Abs((lo - olat) / (uo - ou)), "outer edge", e, s);
                            ou = uo; olat = lo;
                            if (!float.IsNaN(iu) && Mathf.Abs(uin - iu) > 0.3f && r.half <= 0f) Slope(Mathf.Abs((li - ilat) / (uin - iu)), "inner edge", e, s);
                            iu = uin; ilat = li;
                            // the lines: against the lanes' straight reference
                            LinesAt(e, s, la);
                            var p = e.PointAt(s); var left = SectionLeft(e, s);
                            foreach (var l in la)
                            {
                                r.LocalNear(p + left * l.lat, ref hint, out float lu, out float ll);
                                float off = r.LaneRef(lu) - ll;
                                lineMin[l.k] = lineMin.TryGetValue(l.k, out float a0) ? Mathf.Min(a0, off) : off;
                                lineMax[l.k] = lineMax.TryGetValue(l.k, out float a1) ? Mathf.Max(a1, off) : off;
                                if (prevLine.TryGetValue(l.k, out var pv) && Mathf.Abs(lu - pv.u) > 0.3f) Slope(Mathf.Abs((ll - pv.lat) / (lu - pv.u)), $"line {l.k}", e, s);
                                prevLine[l.k] = (lu, ll);
                            }
                            // the traffic path
                            r.LocalNear(OwnLanePoint(e, s), ref hint, out float tu, out float tl);
                            float toff = r.LaneRef(tu) - tl;
                            pMin = Mathf.Min(pMin, toff); pMax = Mathf.Max(pMax, toff);
                            if (!float.IsNaN(pu) && Mathf.Abs(tu - pu) > 0.3f) Slope(Mathf.Abs((tl - plat) / (tu - pu)), "path", e, s);
                            pu = tu; plat = tl;
                        }
                        foreach (var kv in lineMin) lineMove = Mathf.Max(lineMove, lineMax[kv.Key] - kv.Value);
                        if (pMax >= pMin) pathMove = Mathf.Max(pathMove, pMax - pMin);
                    }
                    Edges(e, fwd ? e.length : 0f, out prevPl, out prevMi); havePrev = true;
                    LinesAt(e, e.length * 0.5f, la); lines += la.Count;
                    from = fwd ? e.b : e.a;
                }
                m.step = endStep;
                if (r.profiled) { m.outerDev = outerDev; m.lineMove = lineMove; m.pathMove = pathMove; m.slope = slope; m.slopeAt = slopeAt; }
                partial.Add(m);
            }
            // the island: this carriageway's inner edge plus its partner's at the same axis arc
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i]; var m = partial[i];
                Refuge o = null;
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

        static Dictionary<int, int> splitUOf; static CityMap splitUMap;
        static bool SplitU(CityMap map, int node, out int u)
        {
            if (splitUMap != map || splitUOf == null)
            {
                splitUOf = new Dictionary<int, int>(); splitUMap = map;
                if (map.splt != null) foreach (var sp in map.splt) splitUOf[sp.node] = sp.u;
            }
            return splitUOf.TryGetValue(node, out u);
        }
    }
}
