using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface round 2: PAINT INSIDE A JUNCTION BUILT AS ONE SURFACE. Every piece is a strip CUT INTO the fill
    /// (CityMeshes.Minor EmitCutFan, the through lines' mechanism): coplanar, only where the junction's own triangles
    /// are - nothing off the outline, nothing under an island. The rules (city-wide; a pick may add to them):
    ///   - the through road's lane lines carry across only where the major road is not interrupted (RoadSurface.cs:
    ///     one named road through a through corner), never across a signalised crossing; no edge line across (kerbed);
    ///   - a lane line that runs along a measured island is solid white for the island's length (FIT);
    ///   - an edge wholly inside the junction (an approach between a split and its stop line) carries its lanes' lines
    ///     to its stop line; a painted median on it: the doubled yellow line and yellow diagonal bars, no nose, no hole;
    ///   - PICKS: a solid or paint-only picked line goes on from the row to its own end; an arrow (the existing
    ///     stamp) and a stop line (square to the lanes, across the lane band) at their stations.
    /// </summary>
    public static partial class CityMeshes
    {
        public static int RsPaintStrips;
        static readonly List<Vector2> rsPts = new List<Vector2>(64), rsNrm = new List<Vector2>(64);

        /// <summary>The U of a paint column of an edge's road texture: white (its solid white edge line) or yellow (the
        /// double yellow's; a one-way's yellow left edge line).</summary>
        static void RsPaintU(CityMap.Edge e, bool yellow, out int slot, out float uR, out float uL)
        {
            var tex = LineModel.LayoutOf(e.profile);
            slot = (int)SlotOf(e.profile, SurfaceOf(e, false));
            int j = yellow ? LineOfKind(tex, LineModel.KYellow) : LineOfKind(tex, LineModel.KEdgeM);
            if (j < 0 && yellow && !tex.twoWay) j = LineOfKind(tex, LineModel.KEdgeP);
            if (j < 0) j = LineOfKind(tex, LineModel.KEdgeM);
            if (j < 0) j = 0;
            float m = tex.srcM[j], ph = tex.half;
            uR = (m + ph * 0.6f) / tex.W; uL = (m - ph * 0.6f) / tex.W;
        }

        /// <summary>A strip through world points (plan) with left normals (not unit: a normal's length scales the half
        /// width there, 0 = a point), cut into the fill. dashM > 0: broken, 3 m of paint in every 12 m from the start.</summary>
        static void RsStrip(Vector3 origin, int slot, float uR, float uL, float half, List<Vector2> pts, List<Vector2> nrm, bool broken)
        {
            if (pts.Count < 2) return;
            var st = throughPool.Count > 0 ? throughPool.Pop() : new ThroughStrip();
            st.half = half; st.slot = slot; st.uR = uR; st.uL = uL;
            st.x0 = st.z0 = float.MaxValue; st.x1 = st.z1 = float.MinValue;
            float arc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) arc += Vector2.Distance(pts[i - 1], pts[i]);
                var cq = new Vector2(pts[i].x - origin.x, pts[i].y - origin.z);
                st.c.Add(cq); st.n.Add(nrm[i]); st.v.Add(arc / RoadVTile);
                float pad = half * Mathf.Max(1f, nrm[i].magnitude) + 0.01f;
                st.x0 = Mathf.Min(st.x0, cq.x - pad); st.x1 = Mathf.Max(st.x1, cq.x + pad);
                st.z0 = Mathf.Min(st.z0, cq.y - pad); st.z1 = Mathf.Max(st.z1, cq.y + pad);
            }
            float d = 0f;
            for (int i = 0; i + 1 < st.c.Count; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                st.paint.Add(!broken || Mathf.Repeat(d + 0.5f * len, 12.192f) < 3.048f);
                d += len;
            }
            throughStrips.Add(st);
            RsPaintStrips++;
        }

        /// <summary>A line along an edge at lat(s) over [s0, s1] (a->b), as strips.</summary>
        static void RsLine(Vector3 origin, CityMap.Edge e, float s0, float s1, System.Func<float, float> lat, bool yellow, bool broken, float half = -1f)
        {
            if (s1 - s0 < 0.3f) return;
            RsPaintU(e, yellow, out int slot, out float uR, out float uL);
            if (half < 0f) half = LineModel.LayoutOf(e).half;
            rsPts.Clear(); rsNrm.Clear();
            // a broken line: a point at every dash end (3.05 m paint / 9.14 m gap); a solid one every 2 m
            float step = broken ? 3.048f : 1f;
            int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / step));
            for (int i = 0; i <= n; i++)
            {
                float s = broken ? Mathf.Min(s0 + i * step, s1) : Mathf.Lerp(s0, s1, (float)i / n);
                float l = lat(s);
                if (float.IsNaN(l)) { if (rsPts.Count >= 2) RsStrip(origin, slot, uR, uL, half, rsPts, rsNrm, broken); rsPts.Clear(); rsNrm.Clear(); continue; }
                var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                // the line's own direction (its lateral moves along s)
                float dl = (lat(Mathf.Min(s + 0.5f, s1)) - lat(Mathf.Max(s - 0.5f, s0))) / Mathf.Max(0.01f, Mathf.Min(s + 0.5f, s1) - Mathf.Max(s - 0.5f, s0));
                var dir = float.IsNaN(dl) ? t : (t + left * dl).normalized;
                rsPts.Add(e.PointAt(s) + left * l); rsNrm.Add(new Vector2(-dir.y, dir.x));
            }
            if (rsPts.Count >= 2) RsStrip(origin, slot, uR, uL, half, rsPts, rsNrm, broken);
        }

        /// <summary>A convex polygon in an edge's (lat, s) frame as a strip: a quad (4 points: p0 p1 | p3 p2 are its two
        /// ends) or a triangle (3 points: p0 p1 the base, p2 the tip).</summary>
        static void RsPoly(Vector3 origin, CityMap.Edge e, Vector2[] poly, bool yellow)
        {
            RsPaintU(e, yellow, out int slot, out float uR, out float uL);
            Vector2 Wp(Vector2 q) { float s = Mathf.Clamp(q.y, 0f, e.length); var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x); return e.PointAt(s) + left * q.x + t * (q.y - s); }
            Vector2 a0 = Wp(poly[0]), a1 = Wp(poly[1]);
            Vector2 b0 = poly.Length >= 4 ? Wp(poly[3]) : Wp(poly[2]), b1 = Wp(poly[2]);
            // ends: A = (a0, a1), B = (b0, b1); the strip runs from A's middle to B's, its sides a0 -> b0 and a1 -> b1
            Vector2 cA = 0.5f * (a0 + a1), cB = 0.5f * (b0 + b1);
            var dir = cB - cA; if (dir.sqrMagnitude < 1e-6f) return;
            var left0 = new Vector2(-dir.y, dir.x).normalized;
            // which end point lies to the left
            Vector2 hA = 0.5f * (a1 - a0), hB = 0.5f * (b1 - b0);
            if (Vector2.Dot(hA, left0) < 0f) hA = -hA;
            if (Vector2.Dot(hB, left0) < 0f) hB = -hB;
            if (hB.sqrMagnitude < 1e-8f) hB = left0 * 1e-3f;
            rsPts.Clear(); rsNrm.Clear();
            rsPts.Add(cA); rsNrm.Add(hA); rsPts.Add(cB); rsNrm.Add(hB);
            RsStrip(origin, slot, uR, uL, 1f, rsPts, rsNrm, false);
        }

        /// <summary>The paint inside one junction surface (see the class comment), into throughStrips.</summary>
        static void RsPaint(CityMap map, Trims trims, TileMeshes tm, JunctionSurfaces.Built js)
        {
            var set = JunctionMeasures.Current;
            if (set == null) return;
            var edges = new List<int>(js.rowOf.Keys);
            foreach (int ei in js.inside) if (!edges.Contains(ei)) edges.Add(ei);
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                var f = JunctionMeasures.FitOf(ei);
                if (f == null || !f.oneway) continue;
                set.edges.TryGetValue(ei, out var em);
                bool inside = js.inside.Contains(ei);
                float s0 = 0f, s1 = e.length;
                if (!inside)
                {
                    float row = js.rowOf[ei];
                    if (js.nodes.Contains(e.a)) s1 = Mathf.Min(e.length, row); else s0 = Mathf.Max(0f, e.length - row);
                }
                // an inside edge stops its lines at its stop line
                float sStop = em != null && JunctionMeasures.PicksOn ? em.stop : float.NaN;
                bool toStop = !inside && !float.IsNaN(sStop) && js.nodes.Contains(e.b) && sStop > s0;
                float sEnd = (inside || toStop) && !float.IsNaN(sStop) ? Mathf.Min(s1, sStop) : s1;
                bool kerbR = em == null || em.kindR == 0, paintL = em != null && em.kindL == 2;
                for (int idx = 0; idx <= f.nRule; idx++)
                {
                    JunctionMeasures.LineM pick = null;
                    if (f.lines != null) foreach (var ln in f.lines) if (!ln.paintOnly && ln.index == idx) pick = ln;
                    int id = idx;
                    float Lat(float s)
                    {
                        if (id > f.Lerp(f.nEff, s) + 0.5f) return float.NaN;
                        return f.Bnd(id, s);
                    }
                    byte kind = pick != null ? pick.kind
                              : idx == 0 ? (kerbR ? JunctionMeasures.LkNone : JunctionMeasures.LkEdge)
                              : idx == f.nRule ? (paintL ? JunctionMeasures.LkYellow : JunctionMeasures.LkNone)
                              : JunctionMeasures.LkBroken;
                    float a = s0, b = sEnd;
                    if (pick != null && pick.st.Count >= 2) { a = Mathf.Max(a, pick.st[0].x); b = Mathf.Min(b, pick.st[pick.st.Count - 1].x); }
                    else if (inside && em != null && em.st.Count > 0) { a = Mathf.Max(a, em.st[0].s); b = Mathf.Min(b, em.st[em.st.Count - 1].s); }
                    if (kind == JunctionMeasures.LkNone)
                    {
                        // FIT: the lanes' left line is solid white along a measured island
                        if (pick != null || idx != f.nRule) continue;
                        Runs(e, s0, sEnd, s =>
                        {
                            float bl = f.Bnd(id, s);
                            if (float.IsNaN(bl)) return false;
                            var t = e.TangentAt(s);
                            return JunctionMeasures.IslandNear(set, e.PointAt(s), new Vector2(-t.y, t.x), bl - 0.3f, bl + 1.5f);
                        }, (r0, r1) => RsLine(tm.origin, e, r0, r1, Lat, false, false));
                        continue;
                    }
                    // an arm's broken lines inside the junction are the through pairing's (row to row)
                    if (!inside && !toStop && kind == JunctionMeasures.LkBroken) continue;
                    if (kind == JunctionMeasures.LkYellow2) { RsLine(tm.origin, e, a, b, s => Lat(s) - 0.12f, true, false); RsLine(tm.origin, e, a, b, s => Lat(s) + 0.12f, true, false); }
                    else RsLine(tm.origin, e, a, b, Lat, kind == JunctionMeasures.LkYellow || (kind == JunctionMeasures.LkEdge && idx == f.nRule), kind == JunctionMeasures.LkBroken);
                }
                // a painted median inside the junction: the second yellow line and the bars, over the stations the measure
                // gives its middle (an inside edge: from its first to its last; an arm: from its row to its node)
                if (paintL)
                {
                    float h0 = s0, h1 = sEnd;
                    if (inside)
                    {
                        float lf = float.MaxValue, ll = float.MinValue;
                        foreach (var st in em.st) if (!float.IsNaN(st.left)) { lf = Mathf.Min(lf, st.s); ll = Mathf.Max(ll, st.s); }
                        h0 = Mathf.Max(h0, lf); h1 = Mathf.Min(h1, ll);
                    }
                    RsLine(tm.origin, e, h0, h1, s => BandL(f, s) + 0.25f, true, false);
                    float h = MeasHatchBarW * 1.41421356f;
                    for (float y = h0 + 0.5f; y < h1 - 0.5f; y += MeasHatchStepM)
                    {
                        float lo = BandL(f, y) + 0.45f, hi = f.Lerp(f.leftRaw, y);
                        float rise = hi - lo;
                        if (rise < 0.4f || y + rise + h > h1) continue;
                        RsPoly(tm.origin, e, new[] { new Vector2(lo, y), new Vector2(lo, y + h), new Vector2(hi, y + rise + h), new Vector2(hi, y + rise) }, true);
                    }
                }
                if (f.lines != null)
                    foreach (var ln in f.lines)
                    {
                        if (!ln.paintOnly && ln.index <= f.nRule) continue;
                        if (ln.kind == JunctionMeasures.LkNone || ln.st.Count < 2) continue;
                        float a = Mathf.Max(s0, ln.st[0].x), b = Mathf.Min(sEnd, ln.st[ln.st.Count - 1].x);
                        var l2 = ln;
                        float Lp(float s) => l2.At(s, out float off, out _) ? off : float.NaN;
                        if (ln.kind == JunctionMeasures.LkYellow2) { RsLine(tm.origin, e, a, b, s => Lp(s) - 0.12f, true, false); RsLine(tm.origin, e, a, b, s => Lp(s) + 0.12f, true, false); }
                        else RsLine(tm.origin, e, a, b, Lp, ln.kind == JunctionMeasures.LkYellow, ln.kind == JunctionMeasures.LkBroken);
                        if (ln.hatch)
                        {
                            rsBars.Clear();
                            BufferBars(f, ln, a, b, rsBars);
                            foreach (var pc in rsBars) RsPoly(tm.origin, e, pc, false);
                        }
                    }
                if (em == null || !JunctionMeasures.PicksOn) continue;
                // arrows whose head lies inside the junction (the existing stamp's three pieces)
                foreach (var ar in em.arrows)
                {
                    if (ar.x <= s0 + 0.1f || ar.x > s1) continue;
                    if (!inside && ar.x - ArrowH >= (js.nodes.Contains(e.a) ? -1f : s0) && ar.x - ArrowH < s0) { }   // straddles the row: drawn whole here
                    glyphLocal.Clear();
                    Arrow(glyphLocal, ar.z < 0f ? -1 : 1);
                    foreach (var p in glyphLocal)
                    {
                        var q = new Vector2[p.Length];
                        for (int i = 0; i < p.Length; i++) q[i] = new Vector2(ar.y - p[i].x, ar.x - (ArrowH - p[i].y));
                        RsPoly(tm.origin, e, q, false);
                    }
                }
                // the stop line: square to the lanes, across the lane band
                if (!float.IsNaN(sStop) && sStop > s0 && sStop <= s1 && JunctionMeasures.LaneCentres(e, sStop, rsLanes, out float lo2, out float hi2))
                    RsPoly(tm.origin, e, new[] { new Vector2(lo2 + 0.1f, sStop - 0.3f), new Vector2(lo2 + 0.1f, sStop + 0.3f), new Vector2(hi2 - 0.1f, sStop + 0.3f), new Vector2(hi2 - 0.1f, sStop - 0.3f) }, false);
            }
        }
        static readonly List<float> rsLanes = new List<float>(8);
        static readonly List<Vector2[]> rsBars = new List<Vector2[]>(16);
    }
}
