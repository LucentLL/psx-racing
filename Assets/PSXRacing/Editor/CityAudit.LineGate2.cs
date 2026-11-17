using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LINE GATE, part 2: the classes that need more than the painted
    /// lines (<see cref="CityAudit.RunLineGate"/> calls them; the limits and
    /// their sources are here).
    ///   STEP      a step in the driven surface: a junction fan standing proud
    ///             of the ribbon at its mouth, two spans or two edges meeting
    ///             at different heights, a grade break with no vertical curve
    ///   BARRIER   a wall, barrier or rail standing on or inside an edge line
    ///   MEDIAN    a raised curb or island standing in a lane
    ///   JUNCTION  a solid line across a road mouth; a stop line off its
    ///             lanes; a side road with no control
    ///   BRIDGE    a structure whose lanes or width its own road does not have
    /// </summary>
    public static class LineGateRules2
    {
        /// <summary>NCDOT Standard Specifications (2018/2024) 610-12: a
        /// finished pavement surface may not vary more than 1/8 inch from a
        /// 10 ft straightedge. A step in the driven surface larger than that
        /// (3.2 mm) fails.</summary>
        public const float StepM = 0.0032f;

        /// <summary>The largest change of grade with no vertical curve, by
        /// design speed. Read on 2026-11-16: FDOT Plans Preparation Manual
        /// vol. 1 Table 2.6.2 (1.20 % at 20 mph, 1.00 at 30, 0.80 at 40, 0.70
        /// at 45, 0.60 at 50, 0.40 at 60, 0.30 at 65, 0.20 at 70); TxDOT
        /// Roadway Design Manual 4.8.3 (1 % to 45 mph, 0.5 % above). The gate
        /// uses FDOT's table.</summary>
        public static float GradeBreakPct(float mph) => Mathf.Clamp(mph <= 40f ? 1.2f - 0.02f * (mph - 20f) : mph <= 50f ? 0.8f - 0.02f * (mph - 40f) : 0.6f - 0.02f * (mph - 50f), 0.2f, 1.2f);

        /// <summary>A barrier's face on or inside the edge line (no offset at
        /// all) fails. AASHTO Roadside Design Guide 5.6.1 asks for the shy
        /// line (1.1 m at 30 mph to 2.8 m at 70) and the Green Book for at
        /// least 2 ft (0.6 m) between a travelled way and a rail: the gate
        /// takes only the case no table allows.</summary>
        public const float BarrierInsideM = 0.0f;
        /// <summary>A raised curb's face this far inside a marked lane fails
        /// (AASHTO Green Book 4.7.3: curbs are offset from the travelled way;
        /// MUTCD 3B.10: an obstruction in the roadway takes approach markings).</summary>
        public const float CurbInLaneM = 0.15f;
        /// <summary>AASHTO Green Book Tables 5-6 / 6-6: a bridge's clear
        /// roadway is at least the travelled way plus 2 ft each side: one
        /// 12 ft lane gives 16 ft (4.9 m) between rails.</summary>
        public const float BridgeOneLaneM = 4.9f;
    }

    public static partial class CityAudit
    {
        static readonly List<Vector3> lgBarV = new List<Vector3>(1 << 16);
        static readonly List<Vector3> lgCurbV = new List<Vector3>(1 << 16);

        /// <summary>The tile's concrete (barriers, walls, rails) and its curbs, as points.</summary>
        static void LgCollect2(CityMeshes.TileMeshes tm)
        {
            var o = tm.origin;
            if (tm.barriers != null)
            {
                var v = tm.barriers.vertices;
                // a mesh's vertices repeat per face: one point per 0.25 m cell and 0.25 m of height is enough
                var seen = new HashSet<long>();
                for (int i = 0; i < v.Length; i++)
                {
                    long k = ((long)Mathf.RoundToInt(v[i].x * 4f) << 40) ^ ((long)(Mathf.RoundToInt(v[i].z * 4f) & 0xFFFFF) << 20) ^ (uint)(Mathf.RoundToInt(v[i].y * 4f) & 0xFFFFF);
                    if (seen.Add(k)) lgBarV.Add(new Vector3(o.x + v[i].x, o.y + v[i].y, o.z + v[i].z));
                }
            }
            if (tm.ground != null && tm.groundSlots != null)
            {
                var v = tm.ground.vertices;
                for (int sm = 0; sm < tm.groundSlots.Length; sm++)
                {
                    if (tm.groundSlots[sm] != CityMeshes.Slot.Sidewalk) continue;
                    var tr = tm.ground.GetTriangles(sm);
                    var seen = new HashSet<int>();
                    for (int i = 0; i < tr.Length; i++)
                        if (seen.Add(tr[i])) lgCurbV.Add(new Vector3(o.x + v[tr[i]].x, o.y + v[tr[i]].y, o.z + v[tr[i]].z));
                }
            }
        }

        /// <summary>The ribbon polygon under a plan point whose surface lies dyLo..dyHi below y.</summary>
        static bool LgRibbonAt(double x, double z, float y, float dyLo, float dyHi, out LgPoly poly)
        {
            poly = default;
            long k = ((long)(int)Math.Floor(x / LgPolyCell) << 32) ^ (uint)(int)Math.Floor(z / LgPolyCell);
            if (!lgPolyGrid.TryGetValue(k, out var l)) return false;
            float px = (float)x, pz = (float)z;
            foreach (int id in l)
            {
                var q = lgPolys[id];
                if (q.edge < 0 || y - q.y < dyLo || y - q.y > dyHi) continue;
                if (LgInTri(px, pz, q.x0, q.z0, q.x1, q.z1, q.x2, q.z2) || LgInTri(px, pz, q.x0, q.z0, q.x2, q.z2, q.x3, q.z3)) { poly = q; return true; }
            }
            return false;
        }

        /// <summary>The travelled way of an edge at an arc: between its two
        /// edge lines; on a side with none (a curb), the pavement edge.
        /// False where neither can be read.</summary>
        static bool LgTravelled(CityMap.Edge e, float s, out float lo, out float hi, out bool loLine, out bool hiLine)
        {
            lo = float.NaN; hi = float.NaN; loLine = hiLine = false;
            var ids = lgByEdge[e.index];
            if (ids == null) return false;
            float pl = float.NaN, pr = float.NaN;
            foreach (int id in ids)
            {
                var st = lgStrands[id];
                if (st.role == 3 || st.S0 > s + 0.5f || st.S1 < s - 0.5f) continue;
                float lat = LgLatAt(st, s);
                if (st.role == 0) pl = lat;
                else if (st.role == 1) pr = lat;
                else if (st.tk == LineModel.KEdgeM) { lo = lat; loLine = true; }
                else if (st.tk == LineModel.KEdgeP) { hi = lat; hiLine = true; }
            }
            LineModel.Extents(e, Mathf.Clamp(s, 0f, e.length), out float eM, out float eP);
            if (float.IsNaN(lo)) lo = float.IsNaN(pl) ? -eM : pl;
            if (float.IsNaN(hi)) hi = float.IsNaN(pr) ? eP : pr;
            return hi - lo > 1f;
        }

        static float LgArcOn(CityMap.Edge e, in LgPoly q, double x, double z)
        {
            // the arc under a point of a span: along the span's own middle line
            double ax = 0.5 * (q.x0 + q.x3), az = 0.5 * (q.z0 + q.z3), bx = 0.5 * (q.x1 + q.x2), bz = 0.5 * (q.z1 + q.z2);
            double dx = bx - ax, dz = bz - az, dd = dx * dx + dz * dz;
            double t = dd > 1e-9 ? ((x - ax) * dx + (z - az) * dz) / dd : 0.0;
            return Mathf.Lerp(q.sA, q.sB, Mathf.Clamp01((float)t));
        }

        // ================================================================
        //  STEP
        // ================================================================
        static void LgSteps(CityMap map, CityMeshes.Trims t)
        {
            // ---- two spans of one edge meeting at different heights ----
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var spans = lgSpans[ei];
                if (spans == null) continue;
                for (int i = 0; i + 1 < spans.Count; i++)
                {
                    var a = spans[i]; var b = spans[i + 1];
                    if (Mathf.Abs(a.sB - b.sA) > 0.02f) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        double ax = side == 0 ? a.blx : a.brx, az = side == 0 ? a.blz : a.brz, bx = side == 0 ? b.alx : b.arx, bz = side == 0 ? b.alz : b.arz;
                        if ((ax - bx) * (ax - bx) + (az - bz) * (az - bz) > 0.03 * 0.03) continue;
                        float dy = Mathf.Abs((side == 0 ? a.yBL : a.yBR) - (side == 0 ? b.yAL : b.yAR));
                        if (dy <= LineGateRules2.StepM) continue;
                        lgFails.Add(new LgFail { cls = "STEP", sub = "two spans meet at different heights", edge = ei, s = a.sB, s1 = a.sB, x = ax, z = az, measure = dy * 1000f, limit = LineGateRules2.StepM * 1000f, unit = "mm",
                                                 what = $"the surface steps {dy * 1000f:0.0} mm between two spans of e{ei} at s {a.sB:0.0}" });
                        break;
                    }
                }
            }
            // ---- two edges meeting at different heights (the strands joined in plan) ----
            foreach (var st in lgStrands)
            {
                if (st.role > 1 || st.linkB < 0) continue;
                var o = lgStrands[st.linkB];
                var p = st.p[st.p.Count - 1];
                var q = o.linkA == st.id ? o.p[0] : o.p[o.p.Count - 1];
                float dy = Mathf.Abs(p.y - q.y);
                if (dy <= LineGateRules2.StepM) continue;
                lgFails.Add(new LgFail { cls = "STEP", sub = "two edges meet at different heights", edge = st.edge, node = LgEndNode(map.edges[st.edge], p.s), s = p.s, s1 = p.s, x = p.x, z = p.z, measure = dy * 1000f, limit = LineGateRules2.StepM * 1000f, unit = "mm",
                                         what = $"the surface steps {dy * 1000f:0.0} mm where e{st.edge} meets e{o.edge}" });
            }
            // ---- a fan standing proud of (or under) the ribbon at its mouth ----
            // the ribbons' drawn ends, by node
            var ends = new Dictionary<int, List<(double x, double z, float y, int edge)>>();
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var spans = lgSpans[ei];
                if (spans == null || spans.Count == 0) continue;
                var e = map.edges[ei];
                var f = spans[0]; var l = spans[spans.Count - 1];
                if (f.sA <= t.atA[ei] + 0.75f)
                {
                    if (!ends.TryGetValue(e.a, out var la)) ends[e.a] = la = new List<(double, double, float, int)>(8);
                    la.Add((f.alx, f.alz, f.yAL, ei)); la.Add((f.arx, f.arz, f.yAR, ei));
                }
                if (l.sB >= e.length - t.atB[ei] - 0.75f)
                {
                    if (!ends.TryGetValue(e.b, out var lb)) ends[e.b] = lb = new List<(double, double, float, int)>(8);
                    lb.Add((l.blx, l.blz, l.yBL, ei)); lb.Add((l.brx, l.brz, l.yBR, ei));
                }
            }
            foreach (var fa in lgFans)
            {
                if (!ends.TryGetValue(fa.node, out var le)) continue;
                int n = fa.x.Length;
                var worstByEdge = new Dictionary<int, (float dy, double x, double z)>();
                for (int i = 0; i < n; i++)
                {
                    bool mouth = i < 64 && ((fa.mouths >> i) & 1UL) != 0 || (i > 0 && i - 1 < 64 && ((fa.mouths >> (i - 1)) & 1UL) != 0) || (i == 0 && n - 1 < 64 && ((fa.mouths >> (n - 1)) & 1UL) != 0);
                    if (!mouth) continue;
                    foreach (var (x, z, y, edge) in le)
                    {
                        if ((x - fa.x[i]) * (x - fa.x[i]) + (z - fa.z[i]) * (z - fa.z[i]) > 0.25 * 0.25) continue;
                        float dy = fa.y[i] - y;
                        if (Mathf.Abs(dy) <= LineGateRules2.StepM) continue;
                        if (!worstByEdge.TryGetValue(edge, out var w) || Mathf.Abs(dy) > Mathf.Abs(w.dy)) worstByEdge[edge] = (dy, x, z);
                    }
                }
                foreach (var kv in worstByEdge)
                    lgFails.Add(new LgFail { cls = "STEP", sub = "junction fan lip", edge = kv.Key, node = fa.node, s = map.edges[kv.Key].a == fa.node ? t.atA[kv.Key] : map.edges[kv.Key].length - t.atB[kv.Key], x = kv.Value.x, z = kv.Value.z,
                                             measure = Mathf.Abs(kv.Value.dy) * 1000f, limit = LineGateRules2.StepM * 1000f, unit = "mm",
                                             what = $"the junction's slab stands {kv.Value.dy * 1000f:0.0} mm {(kv.Value.dy > 0 ? "over" : "under")} the road at the mouth of e{kv.Key} (node n{fa.node})" });
            }
        }

        /// <summary>STEP / grade break, on one chain: the change of grade at a
        /// point that no vertical curve carries. A vertical curve (drawn as
        /// facets) changes grade twice as much over twice the baseline, a break
        /// the same: |2 g(10 m) - g(20 m)| is the break alone.</summary>
        static void LgGradeBreaks(CityMap map, List<LgP> pts, LgStrand st0)
        {
            if (st0.role > 1) return;       // the pavement's own edges carry the surface
            // resample y at 1 m along the chain
            var ys = new List<float>(256); var at = new List<LgP>(256);
            double acc = 0, next = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                double d = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                if (d < 1e-9) continue;
                while (acc + d >= next)
                {
                    float f = (float)((next - acc) / d);
                    ys.Add(Mathf.Lerp(a.y, b.y, f));
                    at.Add(new LgP { x = a.x + (b.x - a.x) * f, z = a.z + (b.z - a.z) * f, s = f < 0.5f ? a.s : b.s, e = f < 0.5f ? a.e : b.e });
                    next += 1;
                }
                acc += d;
            }
            int n = ys.Count, last = -100;
            for (int i = 20; i < n - 20; i++)
            {
                float g10 = (ys[i + 10] - ys[i]) / 10f - (ys[i] - ys[i - 10]) / 10f, g20 = (ys[i + 20] - ys[i]) / 20f - (ys[i] - ys[i - 20]) / 20f;
                float k = Mathf.Abs(2f * g10 - g20) * 100f;
                var e = map.edges[at[i].e];
                float mph = LineGateRules.Mph(e), lim = LineGateRules2.GradeBreakPct(mph);
                if (k <= lim || i - last < 12) continue;
                // the local maximum
                bool peak = true;
                for (int q = Math.Max(20, i - 6); q <= Math.Min(n - 21, i + 6) && peak; q++)
                {
                    if (q == i) continue;
                    float a10 = (ys[q + 10] - ys[q]) / 10f - (ys[q] - ys[q - 10]) / 10f, a20 = (ys[q + 20] - ys[q]) / 20f - (ys[q] - ys[q - 20]) / 20f;
                    if (Mathf.Abs(2f * a10 - a20) * 100f > k) peak = false;
                }
                if (!peak) continue;
                last = i;
                lgFails.Add(new LgFail { cls = "STEP", sub = "grade break", edge = e.index, s = at[i].s, s1 = at[i].s, x = at[i].x, z = at[i].z, measure = k, limit = lim, unit = "%", mph = mph,
                                         what = $"the grade changes {k:0.0} % at a point with no vertical curve (limit {lim:0.0} % at {mph:0} mph){(e.bridge || e.ElevatedAt(at[i].s) ? " on or at a structure" : "")}" });
            }
        }

        // ================================================================
        //  BARRIER, MEDIAN (a curb in a lane)
        // ================================================================
        static void LgSolids(CityMap map)
        {
            // one failure per edge and 10 m of arc, the deepest point
            var worst = new Dictionary<long, (float d, Vector3 at, float s)>();
            foreach (var v in lgBarV)
            {
                // the barrier's foot: on or within 1.2 m over a ribbon's surface
                if (!LgRibbonAt(v.x, v.z, v.y, -0.15f, 1.2f, out var q)) continue;
                var e = map.edges[q.edge];
                float s = LgArcOn(e, q, v.x, v.z);
                if (!LgTravelled(e, s, out float lo, out float hi, out bool loLine, out bool hiLine)) continue;
                float lat = LgLat(e, s, v.x, v.z);
                // inside the edge line (its near side), or, with no line, more than 0.3 m inside the pavement edge
                float din = Mathf.Min(lat - (lo - (loLine ? 0.06f : -0.3f)), (hi + (hiLine ? 0.06f : -0.3f)) - lat);
                if (din <= LineGateRules2.BarrierInsideM) continue;
                long k = ((long)q.edge << 20) ^ (uint)Mathf.FloorToInt(s / 10f);
                if (!worst.TryGetValue(k, out var w) || din > w.d) worst[k] = (din, v, s);
            }
            foreach (var kv in worst)
            {
                int edge = (int)(kv.Key >> 20);
                var w = kv.Value;
                lgFails.Add(new LgFail { cls = "BARRIER", sub = "wall or barrier inside the edge line", edge = edge, s = w.s, s1 = w.s, x = w.at.x, z = w.at.z, measure = w.d, limit = 0f, unit = "m",
                                         what = $"a wall, barrier or rail stands {w.d:0.00} m inside the edge line of e{edge} at s {w.s:0}" });
            }
            worst.Clear();
            foreach (var v in lgCurbV)
            {
                // a curb's top: 6 to 40 cm over a ribbon's surface
                if (!LgRibbonAt(v.x, v.z, v.y, 0.06f, 0.4f, out var q)) continue;
                var e = map.edges[q.edge];
                float s = LgArcOn(e, q, v.x, v.z);
                if (!LgTravelled(e, s, out float lo, out float hi, out bool loLine, out bool hiLine)) continue;
                float lat = LgLat(e, s, v.x, v.z);
                float din = Mathf.Min(lat - lo, hi - lat);
                if (din <= LineGateRules2.CurbInLaneM) continue;
                long k = ((long)q.edge << 20) ^ (uint)Mathf.FloorToInt(s / 10f);
                if (!worst.TryGetValue(k, out var w) || din > w.d) worst[k] = (din, v, s);
            }
            foreach (var kv in worst)
            {
                int edge = (int)(kv.Key >> 20);
                var w = kv.Value;
                lgFails.Add(new LgFail { cls = "MEDIAN", sub = "curb or island in the travelled way", edge = edge, s = w.s, s1 = w.s, x = w.at.x, z = w.at.z, measure = w.d, limit = LineGateRules2.CurbInLaneM, unit = "m",
                                         what = $"a raised curb stands {w.d:0.00} m inside the travelled way of e{edge} at s {w.s:0}" });
            }
        }

        // ================================================================
        //  JUNCTION
        // ================================================================
        static void LgJunctions(CityMap map, CityMeshes.Trims t, bool[] scope)
        {
            // ---- a solid line across a road mouth: the path a car takes out of a side road's or ramp's end ----
            // every solid painted segment, on a 16 m grid
            var grid = new Dictionary<long, List<(int st, int i)>>();
            foreach (var st in lgStrands)
            {
                if (st.role != 2 || st.broken) continue;
                for (int i = 0; i + 1 < st.p.Count; i++)
                {
                    var a = st.p[i]; var b = st.p[i + 1];
                    int cx0 = (int)Math.Floor(Math.Min(a.x, b.x) / 16.0), cx1 = (int)Math.Floor(Math.Max(a.x, b.x) / 16.0), cz0 = (int)Math.Floor(Math.Min(a.z, b.z) / 16.0), cz1 = (int)Math.Floor(Math.Max(a.z, b.z) / 16.0);
                    if (cx1 - cx0 > 6 || cz1 - cz0 > 6) continue;
                    for (int cx = cx0; cx <= cx1; cx++)
                        for (int cz = cz0; cz <= cz1; cz++)
                        {
                            long k = ((long)cx << 32) ^ (uint)cz;
                            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(int, int)>(8);
                            l.Add((st.id, i));
                        }
                }
            }
            bool Cross(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz, out double ix, out double iz)
            {
                ix = iz = 0;
                double r1 = bx - ax, r2 = bz - az, s1 = dx - cx, s2 = dz - cz, den = r1 * s2 - r2 * s1;
                if (Math.Abs(den) < 1e-12) return false;
                double tt = ((cx - ax) * s2 - (cz - az) * s1) / den, uu = ((cx - ax) * r2 - (cz - az) * r1) / den;
                if (tt < 0 || tt > 1 || uu < 0 || uu > 1) return false;
                ix = ax + tt * r1; iz = az + tt * r2;
                return true;
            }
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                if (!scope[ei] || lgSpans[ei] == null) continue;
                var e = map.edges[ei];
                for (int end = 0; end < 2; end++)
                {
                    int node = end == 0 ? e.a : e.b;
                    if (e.oneway && end == 0) continue;                    // traffic leaves the node at a one-way's start
                    if (!t.mitre[node]) continue;                          // a fan: no line is drawn inside it
                    if (t.throughA[node] == ei || t.throughB[node] == ei) continue;   // the through road itself
                    // the side road's own middle, its last 6 m and 9 m on past its end
                    float sEnd = end == 0 ? t.atA[ei] : e.length - t.atB[ei];
                    float sIn = end == 0 ? Mathf.Min(e.length, sEnd + 6f) : Mathf.Max(0f, sEnd - 6f);
                    LineModel.Extents(e, sEnd, out float eM, out float eP);
                    float mid = e.oneway ? 0.5f * (eP - eM) : (end == 1 ? -0.5f * eM : 0.5f * eP);    // the arriving lanes' middle
                    var tan = e.TangentAt(sEnd); var ln = new Vector2(-tan.y, tan.x);
                    var p0 = e.PointAt(sIn) + new Vector2(-e.TangentAt(sIn).y, e.TangentAt(sIn).x) * mid;
                    var p1 = e.PointAt(sEnd) + ln * mid;
                    var p2 = p1 + tan * (end == 1 ? 9f : -9f);
                    float y = e.YAt(sEnd);
                    LgFail hit = null;
                    for (int leg = 0; leg < 2 && hit == null; leg++)
                    {
                        var A = leg == 0 ? p0 : p1; var B = leg == 0 ? p1 : p2;
                        int cx0 = (int)Math.Floor(Math.Min(A.x, B.x) / 16.0), cx1 = (int)Math.Floor(Math.Max(A.x, B.x) / 16.0), cz0 = (int)Math.Floor(Math.Min(A.y, B.y) / 16.0), cz1 = (int)Math.Floor(Math.Max(A.y, B.y) / 16.0);
                        for (int cx = cx0; cx <= cx1 && hit == null; cx++)
                            for (int cz = cz0; cz <= cz1 && hit == null; cz++)
                            {
                                if (!grid.TryGetValue(((long)cx << 32) ^ (uint)cz, out var l)) continue;
                                foreach (var (sid, i) in l)
                                {
                                    var st = lgStrands[sid];
                                    if (st.edge == ei) continue;
                                    var a = st.p[i]; var b = st.p[i + 1];
                                    if (Mathf.Abs(a.y - y) > 1.5f) continue;
                                    if (!Cross(A.x, A.y, B.x, B.y, a.x, a.z, b.x, b.z, out double ix, out double iz)) continue;
                                    // only the near side of the road being entered: the first line met
                                    hit = new LgFail { cls = "JUNCTION", sub = "solid line across a road mouth", edge = st.edge, node = node, s = a.s, s1 = b.s, x = ix, z = iz, measure = 0f, limit = 0f, unit = "", line = st.Kind,
                                                       what = $"the {st.Kind} of {LgRoad(map.edges[st.edge])} (e{st.edge}) runs solid across the mouth of {LgRoad(e)} (e{ei}) at n{node}" };
                                    break;
                                }
                            }
                    }
                    if (hit != null) lgFails.Add(hit);
                }
            }
            // ---- stop lines and control (CitySignals' own junction list) ----
            var all = CitySignals.All(map, t);
            var controlled = new HashSet<long>();          // (edge, node) with an approach
            var nodeSignal = new HashSet<int>();           // nodes of a signal or all-way junction
            foreach (var j in all)
            {
                foreach (int n in j.nodes) if (j.signal || j.allWay) nodeSignal.Add(n);
                foreach (var a in j.approaches)
                {
                    controlled.Add(((long)a.edge << 32) | (uint)a.node);
                    if (a.edge < 0 || a.edge >= map.edges.Length || !scope[a.edge] || a.ctl == 1) continue;
                    // the bar runs from at + Right * inL to at + Right * inR; its left end belongs on the centre line
                    var e = map.edges[a.edge];
                    if (e.oneway) continue;
                    var ids = lgByEdge[a.edge];
                    if (ids == null) continue;
                    var left = a.at + a.Right * a.inL;
                    float best = float.MaxValue;
                    foreach (int id in ids)
                    {
                        var st = lgStrands[id];
                        if (st.role != 2 || !st.yellow || st.S0 > a.sStop + 1f || st.S1 < a.sStop - 1f) continue;
                        var q = LgPosAt(st, a.sStop);
                        float d = Vector2.Dot(q - left, a.Right);        // + : the yellow lies right of the bar's left end (the bar reaches past it)
                        if (Mathf.Abs(d) < Mathf.Abs(best)) best = d;
                    }
                    if (best == float.MaxValue) continue;
                    if (best > 0.5f || best < -1.5f)
                        lgFails.Add(new LgFail { cls = "JUNCTION", sub = "stop line off its lanes", edge = a.edge, node = a.node, s = a.sStop, s1 = a.sStop, x = left.x, z = left.y, measure = Mathf.Abs(best), limit = 0.5f, unit = "m",
                                                 what = best > 0f ? $"the stop line of {LgRoad(e)} (e{a.edge}) reaches {best:0.0} m past the centre line into the oncoming lanes"
                                                                  : $"the stop line of {LgRoad(e)} (e{a.edge}) ends {-best:0.0} m short of the centre line" });
                }
            }
            // a side road with no control: a named road, not a ramp, arriving at a junction of three arms or
            // more where it is not the through road, with no signal, STOP or yield of its own (MUTCD 2B.04)
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var arms = map.nodeEdges[n];
                if (arms.Count < 3 || nodeSignal.Contains(n)) continue;
                // the through road: the highest class at the node, by name
                int top = -1;
                foreach (int ei in arms) top = Mathf.Max(top, map.edges[ei].cls);
                string main = null; int mainN = 0;
                foreach (int ei in arms)
                {
                    var e = map.edges[ei];
                    if (e.cls != top || string.IsNullOrEmpty(e.name)) continue;
                    int c = 0; foreach (int o in arms) if (map.edges[o].name == e.name) c++;
                    if (c > mainN) { mainN = c; main = e.name; }
                }
                bool anyFreeway = false; foreach (int ei in arms) anyFreeway |= map.edges[ei].cls >= 5;
                if (anyFreeway) continue;
                foreach (int ei in arms)
                {
                    var e = map.edges[ei];
                    if (!scope[ei] || e.link || e.roundabout || string.IsNullOrEmpty(e.name) || e.name == main && mainN >= 2) continue;
                    if (e.oneway && e.a == n) continue;                    // it leaves
                    if (mainN < 2 && e.cls == top) continue;               // no through road here: equals meeting
                    if (controlled.Contains(((long)ei << 32) | (uint)n)) continue;
                    bool rb = false; foreach (int o in arms) rb |= map.edges[o].roundabout;
                    if (rb) continue;                                      // a roundabout entry: its own yield
                    var p = map.nodes[n];
                    lgFails.Add(new LgFail { cls = "JUNCTION", sub = "side road with no control", edge = ei, node = n, s = e.a == n ? 0f : e.length, s1 = e.a == n ? 0f : e.length, x = p.x, z = p.y, measure = 0f, limit = 0f, unit = "",
                                             what = $"{LgRoad(e)} (e{ei}) meets {main ?? "the junction"} at n{n} ({arms.Count} arms) with no signal, STOP or yield" });
                }
            }
        }

        // ================================================================
        //  BRIDGE
        // ================================================================
        static void LgBridges(CityMap map, CityMeshes.Trims t, bool[] scope)
        {
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (!scope[ei] || !(e.bridge || (e.stElev != null && e.stElev.Length > 0 && e.stElev[e.stElev.Length / 2]))) continue;
                float sm = 0.5f * e.length;
                // the lanes its own road has either side of the structure
                int Other(int node)
                {
                    if (!t.mitre[node]) return -1;
                    int o = t.throughA[node] == ei ? t.throughB[node] : t.throughB[node] == ei ? t.throughA[node] : -1;
                    return o == ei ? -1 : o;
                }
                int oa = Other(e.a), ob = Other(e.b);
                if (oa >= 0 && ob >= 0 && !map.edges[oa].bridge && !map.edges[ob].bridge)
                {
                    int la = map.edges[oa].lanes, lb = map.edges[ob].lanes;
                    if (e.lanes < Mathf.Min(la, lb) || e.lanes > Mathf.Max(la, lb))
                    {
                        var p = e.PointAt(sm);
                        lgFails.Add(new LgFail { cls = "BRIDGE", sub = "lane count changes on the structure only", edge = ei, s = 0f, s1 = e.length, x = p.x, z = p.y, measure = e.lanes, limit = Mathf.Min(la, lb), unit = "lanes",
                                                 what = $"{LgRoad(e)} carries {la} lanes before and {lb} after the structure and {e.lanes} on it (e{oa} / e{ei} / e{ob})" });
                    }
                }
                // the clear width between its edges
                float w = LgPaveWidth(ei, sm);
                if (w > 0f && e.oneway && w < LineGateRules2.BridgeOneLaneM - 0.05f)
                {
                    var p = e.PointAt(sm);
                    lgFails.Add(new LgFail { cls = "BRIDGE", sub = "carriageway too narrow on a structure", edge = ei, s = 0f, s1 = e.length, x = p.x, z = p.y, measure = w, limit = LineGateRules2.BridgeOneLaneM, unit = "m",
                                             what = $"{LgRoad(e)} is {w:0.0} m wide on its structure ({e.lanes} lane{(e.lanes == 1 ? "" : "s")}); one lane between rails needs {LineGateRules2.BridgeOneLaneM:0.0} m" });
                }
                else if (w > 0f && !e.oneway && w < 2f * LineGateRules.LaneMinM + 1.2f - 0.05f)
                {
                    var p = e.PointAt(sm);
                    lgFails.Add(new LgFail { cls = "BRIDGE", sub = "carriageway too narrow on a structure", edge = ei, s = 0f, s1 = e.length, x = p.x, z = p.y, measure = w, limit = 2f * LineGateRules.LaneMinM + 1.2f, unit = "m",
                                             what = $"{LgRoad(e)} (two-way) is {w:0.0} m wide on its structure; two lanes between rails need {2f * LineGateRules.LaneMinM + 1.2f:0.0} m" });
                }
            }
        }
    }
}
