using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MEDIANS PASS GATES (P2 refuge islands + turn paint, P3 median
    /// surface; 2026-11-02). Headless, seconds: the graph, the trims twice
    /// (PSX_CITY_REFUGE on, then off) and the marks, no tiles. Writes
    /// city_medians.txt.
    ///   * REFUGE ISLANDS: pairs, island width at mid (OSM / off / on,
    ///     p10/p50/p90), the outer edge's bulge past the undivided road's half
    ///     width (off / on), the step in a drawn edge at every joint of a chain
    ///     and where it leaves its split node (goal: none over 5 cm, no worse
    ///     than off), the lane lines on every chain edge (goal: as many on as off);
    ///   * ARROWS INTO A NOSE: turn-only arrow groups painted into a plain
    ///     split's nose (goal 0);
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
            int capped = LineModel.RefugeTapers, held = LineModel.RefugeHeld;
            var mOn = MeasureRefuges(map, refs, out float stepOn, out int linesOn, out string worstOn);
            CityMeshes.EnsureMarks(map, tOn);
            int noseOn = CityMeshes.MarkStats.noseArrows, noseSkip = CityMeshes.MarkStats.noseSkipped, groupsOn = CityMeshes.MarkStats.groups;
            LineModel.RefugeOn = false;
            var tOff = CityMeshes.ComputeTrims(map);
            var mOff = MeasureRefuges(map, refs, out float stepOff, out int linesOff, out string worstOff);
            CityMeshes.EnsureMarks(map, tOff);
            int noseOff = CityMeshes.MarkStats.noseArrows, groupsOff = CityMeshes.MarkStats.groups;
            LineModel.RefugeOn = refWas;

            var pairs = new HashSet<long>();
            foreach (var r in refs) pairs.Add(((long)Mathf.Min(r.node, r.far) << 32) | (uint)Mathf.Max(r.node, r.far));
            string Q(List<float> v)
            {
                v.RemoveAll(float.IsNaN);
                if (v.Count == 0) return "-";
                v.Sort();
                float P(float p) => v[Mathf.Clamp(Mathf.FloorToInt(p * (v.Count - 1)), 0, v.Count - 1)];
                return $"{P(0.1f):0.00}/{P(0.5f):0.00}/{P(0.9f):0.00} (min {v[0]:0.00}, max {v[v.Count - 1]:0.00}, n {v.Count})";
            }
            var osm = new List<float>(); var wOff = new List<float>(); var wOn = new List<float>(); var bOff = new List<float>(); var bOn = new List<float>();
            for (int i = 0; i < refs.Count; i++)
            {
                osm.Add(refs[i].osmIsland); wOff.Add(mOff[i].island); wOn.Add(mOn[i].island); bOff.Add(mOff[i].bulge); bOn.Add(mOn[i].bulge);
            }
            Line($"REFUGE ISLANDS (PSX_CITY_REFUGE): {pairs.Count} pairs, {refs.Count} carriageway ends; {capped} tapers capped (at half the island, or longer where OSM's island is under {LineModel.RefugeMinIslandM} m), {held} left as they were (already that short)");
            Line($"  island width at mid, p10/p50/p90: OSM {Q(osm)}; OFF {Q(wOff)}; ON {Q(wOn)}");
            Line($"  outer edge past the undivided road's half width (bulge): OFF {Q(bOff)}; ON {Q(bOn)}");
            Line($"  largest step in a drawn edge at a chain joint or its split node: OFF {stepOff:0.000} m ({worstOff}), ON {stepOn:0.000} m ({worstOn})");
            Line($"  lane lines on the chain edges at their mids: OFF {linesOff}, ON {linesOn}");
            var spots = new HashSet<int> { 10229, 10234, 10230, 10233, 10231, 10232, 10236, 10238, 13570, 13571 };
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (!spots.Contains(r.node)) continue;
                var p = map.nodes[r.node];
                Line($"  n{r.node}->n{r.far} e{r.edge} '{map.edges[r.edge].name}' Lc {r.lc:0.0} m ({LatLon(p.x, p.y)}): OSM {r.osmIsland:0.00} m; OFF {mOff[i].island:0.00} m, bulge {mOff[i].bulge:0.00}; ON {mOn[i].island:0.00} m, bulge {mOn[i].bulge:0.00}");
            }
            int worse = 0; string worseAt = "";
            for (int i = 0; i < refs.Count; i++)
                if (mOn[i].step > mOff[i].step + 0.01f) { worse++; if (worseAt.Length < 300) worseAt += $" n{refs[i].node}/e{refs[i].edge} {mOff[i].step:0.00}->{mOn[i].step:0.00};"; }
            Check(worse == 0, "no refuge carriageway's drawn edges step more at a joint or its split node than OFF", worse + worseAt);
            Check(linesOn == linesOff, "no lane line lost on a refuge carriageway", $"{linesOn} vs {linesOff}");
            Line($"ARROWS INTO A NOSE: turn-only arrow groups painted into a plain split's nose OFF {noseOff}, ON {noseOn} ({noseSkip} lanes skipped); all arrow groups OFF {groupsOff}, ON {groupsOn}");
            Check(noseOn == 0, "no turn arrow painted into a plain split nose", noseOn);
            Line(CityMeshes.MedianCellCensus(map));
            FinishTo("city_medians.txt");
        }

        struct RefugeM { public float island, bulge, step; }

        /// <summary>Per refuge end: the island at mid-chain (this carriageway's
        /// inner edge to the other's, as drawn) and the most its outer edge
        /// stands past the undivided road's half width from the axis between
        /// the two carriageways (lerped between the two ends); over all of
        /// them the largest step in a drawn edge where two chain edges meet or
        /// where the chain leaves its split node, and the lane lines at each
        /// chain edge's mid.</summary>
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
                pl = p + left * eP; mi = p - left * eM;
            }
            float SetStep(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1) =>
                Mathf.Min(Mathf.Max(Vector2.Distance(a0, b0), Vector2.Distance(a1, b1)), Mathf.Max(Vector2.Distance(a0, b1), Vector2.Distance(a1, b0)));
            foreach (var r in refs)
            {
                LineModel.Refuge o = null;
                foreach (var q in refs) if (q.node == r.node && q.edge != r.edge) { o = q; break; }
                var m = new RefugeM { island = float.NaN, bulge = float.NaN };
                if (o == null) { res.Add(m); continue; }
                var oPts = new List<(Vector2 pl, Vector2 mi, Vector2 c)>();
                foreach (int ei in o.chain)
                {
                    var e = map.edges[ei];
                    for (float s = 0f; s <= e.length; s += 0.5f) { Edges(e, s, out var pl, out var mi); oPts.Add((pl, mi, e.PointAt(s))); }
                }
                float d = 0f; int from = r.node; float best = float.NaN; float bulge = float.NegativeInfinity;
                var U = map.edges[splitU[r.node]];
                float uh1 = U.width * 0.5f, uh2 = splitU.TryGetValue(r.far, out int u2) ? map.edges[u2].width * 0.5f : uh1;
                Vector2 prevPl = default, prevMi = default; bool havePrev = false; float endStep = 0f;
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
                    else
                    {
                        // where it leaves the split node: its outer edge on the undivided road's
                        Edges(U, U.a == r.node ? 0f : U.length, out var upl, out var umi);
                        float st = Mathf.Min(Mathf.Min(Vector2.Distance(pl0, upl), Vector2.Distance(pl0, umi)),
                                             Mathf.Min(Vector2.Distance(mi0, upl), Vector2.Distance(mi0, umi)));
                        endStep = Mathf.Max(endStep, st);
                        if (st > worstStep) { worstStep = st; worst = $"e{e.index} at split n{from}"; }
                    }
                    for (float s = 0f; s <= e.length; s += 1f)
                    {
                        float sa = fwd ? s : e.length - s;
                        Edges(e, sa, out var pl, out var mi);
                        var c = e.PointAt(sa);
                        float bd = float.MaxValue; int bi = -1;
                        for (int j = 0; j < oPts.Count; j++) { float dd = (oPts[j].c - c).sqrMagnitude; if (dd < bd) { bd = dd; bi = j; } }
                        if (bi < 0) continue;
                        var oc = oPts[bi].c;
                        if ((c - oc).sqrMagnitude < 1e-4f) continue;
                        var axis = 0.5f * (c + oc); var away = (c - oc).normalized;
                        float dPl = Vector2.Dot(pl - axis, away), dMi = Vector2.Dot(mi - axis, away);
                        float inner = Mathf.Min(dPl, dMi), outer = Mathf.Max(dPl, dMi);
                        float oIn = Mathf.Max(Vector2.Dot(oPts[bi].pl - axis, away), Vector2.Dot(oPts[bi].mi - axis, away));
                        float at = d + s;
                        float uh = Mathf.Lerp(uh1, uh2, Mathf.Clamp01(at / Mathf.Max(1f, r.lc)));
                        if (at > 2f && at < r.lc - 2f) bulge = Mathf.Max(bulge, outer - uh);
                        if (float.IsNaN(best) && at >= 0.5f * r.lc) best = inner - oIn;
                    }
                    Edges(e, fwd ? e.length : 0f, out prevPl, out prevMi); havePrev = true;
                    LineModel.LinesAt(e, e.length * 0.5f, la); lines += la.Count;
                    d += e.length; from = fwd ? e.b : e.a;
                }
                m.island = best; m.bulge = float.IsNegativeInfinity(bulge) ? float.NaN : bulge; m.step = endStep;
                res.Add(m);
            }
            return res;
        }
    }
}
