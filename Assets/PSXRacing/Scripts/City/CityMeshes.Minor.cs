using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  ROADS PASS L8 (plan A13): THROUGH LINES ACROSS MINOR MOUTHS.
    //
    //  Before L8 every junction fan was a bare slab: a road's centre line,
    //  lane lines and far edge line stopped at the mouth of every side street
    //  it passed, as they do only at a signalised junction. Where a road goes
    //  straight on past a lesser street with no signal and no stop of its
    //  own, its paint runs on across the mouth (MUTCD 3B: the through lines
    //  are continued, the near-side edge line is broken where the street
    //  opens).
    //
    //  The lines are cut INTO the fan (critic C5: no lift, no second surface
    //  over the first): each fan triangle loses the strip of each line that
    //  crosses it, and that strip is drawn in the through road's own slot at
    //  the line's own texels, on the fan triangle's plane - the same column
    //  the road's ribbon draws the line from, its dash phase carried on from
    //  the arm it leaves. A broken line cut from a solid column (synth) is
    //  cut into its 10 ft dashes the same way. Draws: the arm's slot is the
    //  tile's already wherever its ribbon is.
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool ThroughPaintOn = System.Environment.GetEnvironmentVariable("PSX_CITY_THROUGHPAINT") != "0";

        /// <summary>A13's counts (the MARKS report): junctions that carry
        /// through lines, the lines and metres drawn, and why the others
        /// do not (one count per reason, first reason wins).</summary>
        public static class ThroughStats
        {
            public static int junctions, lines, nearEdgesDropped, synthLines;
            public static float metres;
            public static int skipSignal, skipAllWay, skipStop, skipRank, skipAngle, skipNoLines, skipCluster, skipStructure, skipRoundabout;
            public static void Reset()
            {
                junctions = lines = nearEdgesDropped = synthLines = 0; metres = 0f;
                skipSignal = skipAllWay = skipStop = skipRank = skipAngle = skipNoLines = skipCluster = skipStructure = skipRoundabout = 0;
            }
        }

        /// <summary>One through line across a fan: its centre points and
        /// left normals (tile frame, plan), half width, the U at its right
        /// (lat - half) and left side, V at each point, which quads are paint
        /// (a synth line's dashes), and the slot it is drawn in.</summary>
        sealed class ThroughStrip
        {
            public readonly List<Vector2> c = new List<Vector2>(32), n = new List<Vector2>(32);
            public readonly List<float> v = new List<float>(32);
            public readonly List<bool> paint = new List<bool>(32);
            public float half, uR, uL;
            public int slot;
            public float x0, z0, x1, z1;
        }
        static readonly List<ThroughStrip> throughStrips = new List<ThroughStrip>(8);
        static readonly Stack<ThroughStrip> throughPool = new Stack<ThroughStrip>();
        static readonly List<LineModel.LineAt> tpLinesA = new List<LineModel.LineAt>(12), tpLinesB = new List<LineModel.LineAt>(12);
        static readonly HashSet<int> tpUsedB = new HashSet<int>();

        static void ReleaseStrips()
        {
            foreach (var s in throughStrips)
            {
                s.c.Clear(); s.n.Clear(); s.v.Clear(); s.paint.Clear();
                throughPool.Push(s);
            }
            throughStrips.Clear();
        }

        /// <summary>
        /// The through lines a lone fan carries (A13), into
        /// <see cref="throughStrips"/>, tile frame. A junction qualifies when
        /// two non-link arms go straight through it (cos below -0.85), every
        /// other arm meets them at 60 degrees or more and is of no higher class
        /// than the lower of the two, no arm is a roundabout's, the node is not
        /// a signal's, not an all-way stop and no stop is tagged on either
        /// through arm, and the fan is a lone one on the ground. A line is drawn
        /// where both through arms paint one of the same kind within 0.3 m of
        /// each other (laterals in the frame of travel); an edge line on a side
        /// a street opens on is not.
        /// </summary>
        static bool ThroughLines(CityMap map, Trims trims, int n, Vector3 origin, bool count)
        {
            ReleaseStrips();
            if (!ThroughPaintOn || !trims.patch[n]) return false;
            if (trims.ClusterOfNode(n) != null) { if (count) ThroughStats.skipCluster++; return false; }
            var list = map.nodeEdges[n];
            if (list.Count < 3 || list.Count > 4) return false;
            int iA = -1, iB = -1; float best = 1f;
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var ei = map.edges[list[i]]; var ej = map.edges[list[j]];
                    if (ei.a == ei.b || ej.a == ej.b || ei.link || ej.link) continue;
                    float d = Vector2.Dot(OutDir(ei, n), OutDir(ej, n));
                    if (d < best) { best = d; iA = i; iB = j; }
                }
            if (iA < 0 || best >= ThroughCos) { if (count) ThroughStats.skipAngle++; return false; }
            var A = map.edges[list[iA]]; var B = map.edges[list[iB]];
            var dA = OutDir(A, n);
            int thrCls = Mathf.Min(A.cls, B.cls);
            bool sideLeft = false, sideRight = false;
            // travel: in along A, out along B; left of travel is +left of -dA
            var travel = -dA;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == iA || i == iB) continue;
                var s = map.edges[list[i]];
                if (s.a == s.b) continue;
                if (s.roundabout) { if (count) ThroughStats.skipRoundabout++; return false; }
                var ds = OutDir(s, n);
                if (Mathf.Abs(Vector2.Dot(ds, dA)) > 0.5f || Mathf.Abs(Vector2.Dot(ds, OutDir(B, n))) > 0.5f) { if (count) ThroughStats.skipAngle++; return false; }
                if (s.cls > thrCls) { if (count) ThroughStats.skipRank++; return false; }
                if (travel.x * ds.y - travel.y * ds.x > 0f) sideLeft = true; else sideRight = true;
            }
            if (A.roundabout || B.roundabout) { if (count) ThroughStats.skipRoundabout++; return false; }
            int ctl = CitySignals.ControlAt(map, trims, n, A.index, B.index, out bool throughStops);
            if (ctl == 4) { if (count) ThroughStats.skipSignal++; return false; }
            if (ctl == 3) { if (count) ThroughStats.skipAllWay++; return false; }
            if (throughStops) { if (count) ThroughStats.skipStop++; return false; }
            if (FanOnStructure(map, trims, n)) { if (count) ThroughStats.skipStructure++; return false; }

            float sA = A.a == n ? trims.atA[A.index] : A.length - trims.atB[A.index];
            float sB = B.a == n ? trims.atA[B.index] : B.length - trims.atB[B.index];
            LineModel.LinesAt(A, sA, tpLinesA);
            LineModel.LinesAt(B, sB, tpLinesB);
            if (tpLinesA.Count == 0 || tpLinesB.Count == 0) { if (count) ThroughStats.skipNoLines++; return false; }
            var layA = LineModel.LayoutOf(A); var layB = LineModel.LayoutOf(B);
            float sgnA = A.b == n ? 1f : -1f;    // A's s along the travel
            float sgnB = B.a == n ? 1f : -1f;    // B's s along the travel (out of the node)
            var pA = A.PointAt(sA); var rA = RightAt(map, trims, A, sA, out _);
            var pB = B.PointAt(sB); var rB = RightAt(map, trims, B, sB, out _);
            var tA = A.TangentAt(sA) * sgnA; var tB = B.TangentAt(sB) * sgnB;
            int slot = (int)SlotOf(A.profile, SurfaceOf(A, false));
            float PH = layA.half;
            tpUsedB.Clear();
            int drawn = 0;
            foreach (var la in tpLinesA)
            {
                byte ka = layA.kind[la.k];
                bool edgeA = ka == LineModel.KEdgeP || ka == LineModel.KEdgeM;
                float leftA = sgnA > 0f ? la.lat : -la.lat;
                // an edge line on the side a street opens on is broken there
                if (edgeA && ((leftA > 0f && sideLeft) || (leftA < 0f && sideRight))) { if (count) ThroughStats.nearEdgesDropped++; continue; }
                int match = -1; float bestD = 0.3f;
                for (int j = 0; j < tpLinesB.Count; j++)
                {
                    if (tpUsedB.Contains(j)) continue;
                    var lb = tpLinesB[j];
                    byte kb = layB.kind[lb.k];
                    bool edgeB = kb == LineModel.KEdgeP || kb == LineModel.KEdgeM;
                    if (edgeA != edgeB || (!edgeA && ka != kb)) continue;
                    float leftB = sgnB > 0f ? lb.lat : -lb.lat;
                    float d = Mathf.Abs(leftA - leftB);
                    if (d < bestD) { bestD = d; match = j; }
                }
                if (match < 0) continue;
                tpUsedB.Add(match);
                var lbm = tpLinesB[match];
                // the two ends on the lines' own laterals, plan, world
                Vector2 P0 = pA + rA * la.lat, P1 = pB + rB * lbm.lat;
                float chord = Vector2.Distance(P0, P1);
                if (chord < 0.2f) continue;
                var st = throughPool.Count > 0 ? throughPool.Pop() : new ThroughStrip();
                st.half = PH; st.slot = slot;
                float srcM = layA.srcM[la.k];
                st.uR = (srcM + PH) / layA.W; st.uL = (srcM - PH) / layA.W;
                bool synth = layA.synth[la.k];
                bool bend = Vector2.Dot(tA.normalized, tB.normalized) < Mathf.Cos(5f * Mathf.Deg2Rad);
                // the samples, plus a synth line's dash ends
                // a straight line is one quad (cut into the fan once); a bend every 2 m
                int segs = bend ? Mathf.Max(2, Mathf.CeilToInt(chord / 2f)) : 1;
                float v0 = (A.vOff + A.vDir * sA) / RoadVTile, dv = A.vDir * sgnA / RoadVTile;
                Vector2 Pt(float u)
                {
                    if (!bend) return Vector2.Lerp(P0, P1, u);
                    // cubic Hermite, end tangents the arms' travel directions
                    float u2 = u * u, u3 = u2 * u;
                    var m0 = tA.normalized * chord; var m1 = tB.normalized * chord;
                    return (2f * u3 - 3f * u2 + 1f) * P0 + (u3 - 2f * u2 + u) * m0 + (-2f * u3 + 3f * u2) * P1 + (u3 - u2) * m1;
                }
                // arc lengths at the metre samples
                var us = tpU; us.Clear();
                for (int i = 0; i <= segs; i++) us.Add((float)i / segs);
                if (synth)
                {
                    // a dash's ends: V crosses a whole repeat or its first quarter
                    float len = 0f; var prev = P0;
                    tpArc.Clear(); tpArc.Add(0f);
                    for (int i = 1; i <= segs; i++) { var q = Pt(us[i]); len += Vector2.Distance(prev, q); prev = q; tpArc.Add(len); }
                    float va = v0, vb = v0 + dv * len;
                    float lo = Mathf.Min(va, vb), hi = Mathf.Max(va, vb);
                    for (float k = Mathf.Floor(lo); k <= hi + 1f; k += 1f)
                        foreach (float br in new[] { k, k + 0.25f })
                        {
                            if (br <= lo || br >= hi) continue;
                            float arc = (br - v0) / dv;
                            // the u at that arc length
                            for (int i = 1; i <= segs; i++)
                                if (tpArc[i] >= arc)
                                {
                                    float f = (arc - tpArc[i - 1]) / Mathf.Max(1e-5f, tpArc[i] - tpArc[i - 1]);
                                    us.Add(Mathf.Lerp(us[i - 1], us[i], f));
                                    break;
                                }
                        }
                    us.Sort();
                    ThroughStats.synthLines += count ? 1 : 0;
                }
                float arcAcc = 0f; Vector2 last = Vector2.zero;
                st.x0 = st.z0 = float.MaxValue; st.x1 = st.z1 = float.MinValue;
                for (int i = 0; i < us.Count; i++)
                {
                    if (i > 0 && us[i] - us[i - 1] < 1e-4f) continue;
                    var q = Pt(us[i]);
                    if (st.c.Count > 0) arcAcc += Vector2.Distance(last, q);
                    last = q;
                    // the left normal: the curve's own (the ends: the arms')
                    Vector2 tg = i == 0 ? tA : i == us.Count - 1 ? tB : (Pt(Mathf.Min(1f, us[i] + 1e-3f)) - Pt(Mathf.Max(0f, us[i] - 1e-3f)));
                    tg = tg.sqrMagnitude > 1e-10f ? tg.normalized : tA.normalized;
                    var nl = new Vector2(-tg.y, tg.x);
                    // the ends sit on the arms' own cross-sections: their normals
                    if (i == 0) nl = rA * (sgnA > 0f ? 1f : -1f);
                    if (i == us.Count - 1) nl = rB * (sgnB > 0f ? 1f : -1f);
                    var cq = new Vector2(q.x - origin.x, q.y - origin.z);
                    st.c.Add(cq); st.n.Add(nl.normalized);
                    st.v.Add(v0 + dv * arcAcc);
                    st.x0 = Mathf.Min(st.x0, cq.x - PH); st.x1 = Mathf.Max(st.x1, cq.x + PH);
                    st.z0 = Mathf.Min(st.z0, cq.y - PH); st.z1 = Mathf.Max(st.z1, cq.y + PH);
                }
                for (int i = 0; i + 1 < st.c.Count; i++)
                {
                    bool paint = true;
                    if (synth) paint = Mathf.Repeat(0.5f * (st.v[i] + st.v[i + 1]), 1f) < 0.25f;
                    st.paint.Add(paint);
                }
                // the line's normals point to ITS left; laterals in LinesAt are
                // + left of a->b: on an arm entered against its direction the
                // strip's left is the edge's right, so U swaps
                if (sgnA < 0f) { float t = st.uR; st.uR = st.uL; st.uL = t; }
                throughStrips.Add(st);
                drawn++;
                if (count) { ThroughStats.lines++; ThroughStats.metres += arcAcc; }
            }
            if (drawn > 0 && count) ThroughStats.junctions++;
            return drawn > 0;
        }
        static readonly List<float> tpU = new List<float>(48), tpArc = new List<float>(48);

        /// <summary>For the audit: every lone fan's A13 decision, counted into
        /// <see cref="ThroughStats"/> (inside the box when one is given: x0, z0,
        /// x1, z1), and the per-tier junctions that carry lines.</summary>
        public static void ThroughCensus(CityMap map, Trims trims, Vector4? box, int[] byTier)
        {
            ThroughStats.Reset();
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.patch[n]) continue;
                var p = map.nodes[n];
                if (box.HasValue && (p.x < box.Value.x || p.y < box.Value.y || p.x > box.Value.z || p.y > box.Value.w)) continue;
                int before = ThroughStats.junctions;
                ThroughLines(map, trims, n, Vector3.zero, true);
                if (ThroughStats.junctions > before && byTier != null)
                {
                    int best = 3;
                    foreach (int ei in map.nodeEdges[n]) best = Mathf.Min(best, CityTier.Of(map.edges[ei]));
                    byTier[best]++;
                }
            }
            ReleaseStrips();
        }

        // ==================================================================
        //  ROADS PASS L8 (plan A17): CUL-DE-SAC BULBS AND CLEAN DEAD ENDS.
        //
        //  A dead end OSM tags highway=turning_circle (or turning_loop) is a
        //  bulb: the street's last metres are a one-arm fan whose ring runs
        //  from the street's two edge corners round a circle about the node
        //  (OSM's diameter/2, else 12.2 m - AASHTO's 40 ft cul-de-sac), on a
        //  plane that carries the street's end grade on (5 % at most). Being a
        //  fan, it gets everything a fan gets: verges round its rim, the
        //  lattice held under it, the audits' fan tests. Every other dead end
        //  gets a verge across its end, so the ground meets the tarmac there as
        //  it does along the sides.
        // ==================================================================
        public static bool BulbsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_BULBS") != "0";
        public static class BulbStats { public static int tagged, drawn, skipShort, skipStructure, skipLink; }

        static void BulbTrims(CityMap map, Trims t)
        {
            int nn = map.nodes.Length;
            t.bulb = new bool[nn]; t.bulbRad = new float[nn];
            BulbStats.tagged = BulbStats.drawn = BulbStats.skipShort = BulbStats.skipStructure = BulbStats.skipLink = 0;
            if (!BulbsOn || map.turns == null) return;
            foreach (var tu in map.turns)
            {
                int n = tu.node;
                BulbStats.tagged++;
                if (map.nodeEdges[n].Count != 1) continue;
                var e = map.edges[map.nodeEdges[n][0]];
                if (e.a == e.b) continue;
                if (e.link) { BulbStats.skipLink++; continue; }
                float sEnd = e.a == n ? 0f : e.length;
                if (e.tunnel || e.ElevatedAt(sEnd)) { BulbStats.skipStructure++; continue; }
                float hw = e.HalfMax;
                float R = Mathf.Max(tu.r, hw + 1.5f);
                float trim = Mathf.Sqrt(R * R - hw * hw);
                float other = e.a == n ? t.atB[e.index] : t.atA[e.index];
                if (e.length < trim + other + 5f) { BulbStats.skipShort++; continue; }
                t.patch[n] = true; t.bulb[n] = true; t.bulbRad[n] = R;
                if (e.a == n) t.atA[e.index] = trim; else t.atB[e.index] = trim;
                BulbStats.drawn++;
            }
        }

        /// <summary>A bulb's ring: the street's mouth (early, late), then the
        /// circle anticlockwise from the late corner round to the early one,
        /// pieces of 6 cm sagitta, on the plane of the street's end grade.</summary>
        static void BulbCorners(CityMap map, Trims trims, int n, Vector3 origin, List<FanCorner> corners)
        {
            int ei = -1;
            foreach (int x in map.nodeEdges[n]) if (map.edges[x].a != map.edges[x].b) { ei = x; break; }
            if (ei < 0) return;
            var e = map.edges[ei];
            float trim = trims.TrimAt(e, n);
            float at = e.a == n ? trim : e.length - trim;
            var p = e.PointAt(at);
            var right = RightAt(map, trims, e, at, out float widen);
            LineModel.Extents(e, at, out float eMinus, out float ePlus);
            eMinus *= widen; ePlus *= widen;
            var tan = e.TangentAt(at);
            var outDir = e.a == n ? tan : -tan;
            int lateSide = Vector2.Dot(right, new Vector2(-outDir.y, outDir.x)) >= 0f ? 1 : -1;
            Vector2 cPlus = p + right * ePlus, cMinus = p - right * eMinus;
            Vector2 early = lateSide > 0 ? cMinus : cPlus, late = lateSide > 0 ? cPlus : cMinus;
            float yArm = e.YAt(at);
            BulbPlane(map, trims, n, out var np, out float yNode, out float g, out _, out float R);
            float Y(Vector2 q) => yNode + g * Vector2.Dot(q - np, outDir);
            void Add(Vector2 c, float y, int side, bool mouthNext, bool extra) =>
                corners.Add(new FanCorner
                {
                    ang = Mathf.Atan2(c.y - np.y, c.x - np.x),
                    pos = new Vector3(c.x - origin.x, y + FanProudM, c.y - origin.z),
                    edge = ei, side = side, yArm = y, mouthNext = mouthNext, extra = extra, node = n, arc = extra,
                });
            Add(early, yArm, -lateSide, true, false);
            Add(late, yArm, lateSide, false, false);
            float a0 = Mathf.Atan2(late.y - np.y, late.x - np.x), a1 = Mathf.Atan2(early.y - np.y, early.x - np.x);
            if (a1 <= a0) a1 += 2f * Mathf.PI;
            float step = 2f * Mathf.Acos(1f - 0.06f / Mathf.Max(R, 1f));
            int k = Mathf.Max(3, Mathf.CeilToInt((a1 - a0) / step));
            for (int i = 1; i < k; i++)
            {
                float a = a0 + (a1 - a0) * i / k;
                var q = np + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
                Add(q, Y(q), lateSide, false, true);
            }
        }

        /// <summary>A bulb's plane: the node, its height, the street's end
        /// grade (5 % at most) along <paramref name="dir"/> (out of the node
        /// along the street), and the radius.</summary>
        static bool BulbPlane(CityMap map, Trims trims, int n, out Vector2 c, out float y0, out float g, out Vector2 dir, out float R)
        {
            c = map.nodes[n]; y0 = map.nodeY[n]; g = 0f; dir = Vector2.right; R = 0f;
            if (trims.bulb == null || !trims.bulb[n]) return false;
            int ei = -1;
            foreach (int x in map.nodeEdges[n]) if (map.edges[x].a != map.edges[x].b) { ei = x; break; }
            if (ei < 0) return false;
            var e = map.edges[ei];
            float trim = trims.TrimAt(e, n);
            float at = e.a == n ? trim : e.length - trim;
            var tan = e.TangentAt(at);
            dir = e.a == n ? tan : -tan;
            g = Mathf.Clamp((e.YAt(at) - y0) / Mathf.Max(trim, 0.5f), -0.05f, 0.05f);
            R = trims.bulbRad[n];
            return true;
        }

        /// <summary>A plain dead end's verge across its end (A17): from the
        /// last section's two edge corners outward along the street, laid by
        /// the fan chords' own verge (EmitVergeLine). Not on structure, not
        /// a link's, not where the end lies inside another road's pavement.</summary>
        public static int DeadEndVerges;
        static void EmitDeadEnd(CityMap map, Trims trims, TileMeshes tm, int n)
        {
            if (map.nodeEdges[n].Count != 1 || trims.patch[n]) return;
            var e = map.edges[map.nodeEdges[n][0]];
            if (e.a == e.b || e.link || e.tunnel) return;
            bool atA = e.a == n;
            if (e.ElevatedAt(atA ? 0f : e.length)) return;
            var ol = OutlineOf(map, trims, e.index);
            if (ol.L == null || ol.L.Length < 2) return;
            int i = atA ? 0 : ol.L.Length - 1;
            Vector3 L = ol.L[i], R = ol.R[i];
            var tan = e.TangentAt(atA ? 0f : e.length);
            var outw = atA ? -tan : tan;
            // the perimeter's outward is the right of a -> b: order the two so
            var ab = new Vector2(R.x - L.x, R.z - L.z);
            bool lr = ab.x * outw.y - ab.y * outw.x < 0f;   // outw on ab's right
            Vector3 a = lr ? L : R, b = lr ? R : L;
            var o = tm.origin;
            EmitVergeLine(map, trims, tm, a - o, b - o, outw, outw, VergeShoulderM);
            DeadEndVerges++;
        }

        // ---- cutting convex polygons (plan view, x and z of Vector3) ---------

        static readonly List<Vector3> cpA = new List<Vector3>(16), cpB = new List<Vector3>(16);
        static readonly List<List<Vector3>> cutPieces = new List<List<Vector3>>(32), cutNext = new List<List<Vector3>>(32);
        static readonly Stack<List<Vector3>> polyPool = new Stack<List<Vector3>>();
        static List<Vector3> NewPoly() => polyPool.Count > 0 ? polyPool.Pop() : new List<Vector3>(12);
        static void FreePoly(List<Vector3> p) { p.Clear(); polyPool.Push(p); }

        /// <summary>The part of convex <paramref name="poly"/> on the LEFT of
        /// the directed line a -> b (keep) into <paramref name="into"/>, or on
        /// its right (keep false). Heights are carried linearly (the pieces of a
        /// triangle stay on its plane).</summary>
        static void ClipHalfPlane(List<Vector3> poly, Vector2 a, Vector2 b, bool keepLeft, List<Vector3> into)
        {
            into.Clear();
            int n = poly.Count;
            if (n == 0) return;
            var d = b - a;
            float Side(Vector3 p) { float s = d.x * (p.z - a.y) - d.y * (p.x - a.x); return keepLeft ? s : -s; }
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                float sp = Side(p), sq = Side(q);
                if (sp >= 0f) into.Add(p);
                if ((sp >= 0f) != (sq >= 0f))
                {
                    float t = sp / (sp - sq);
                    into.Add(Vector3.Lerp(p, q, t));
                }
            }
        }

        static float PolyArea(List<Vector3> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var u = p[i]; var w = p[(i + 1) % p.Count]; a += u.x * w.z - w.x * u.z; }
            return 0.5f * a;
        }

        /// <summary>
        /// A fan's triangles drawn with the through strips cut out of them:
        /// the rest into <paramref name="fanBk"/> (world-planar UV), each
        /// strip's share into its slot's bucket at the line's texels. Every
        /// piece lies on its triangle's plane. Returns the fan bucket's new
        /// triangle count; the strips' vertex and triangle ranges go to the
        /// tap (owner: the fan).
        /// </summary>
        static void EmitCutFan(TileMeshes tm, int node, Bucket fanBk, List<FanCorner> corners, Vector3 centre,
                               List<Vector3> steiner, List<int> fanTris, out int fanTriStart, out int fanTriCount)
        {
            fanTriStart = fanBk.t.Count;
            int stripV0 = -1, stripT0 = -1; Bucket stripBk = null; int stripSlot = -1;
            for (int ti = 0; ti + 2 < fanTris.Count; ti += 3)
            {
                Vector3 a = FanVertex(fanTris[ti], centre, corners, steiner);
                Vector3 b = FanVertex(fanTris[ti + 1], centre, corners, steiner);
                Vector3 c = FanVertex(fanTris[ti + 2], centre, corners, steiner);
                foreach (var pp in cutPieces) FreePoly(pp);
                cutPieces.Clear();
                var first = NewPoly(); first.Add(a); first.Add(b); first.Add(c);
                cutPieces.Add(first);
                float tx0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), tx1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float tz0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), tz1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                foreach (var st in throughStrips)
                {
                    if (st.x1 < tx0 || st.x0 > tx1 || st.z1 < tz0 || st.z0 > tz1) continue;
                    if (stripBk == null) { stripBk = buckets[st.slot]; stripSlot = st.slot; stripV0 = stripBk.v.Count; stripT0 = stripBk.t.Count; }
                    for (int qi = 0; qi + 1 < st.c.Count; qi++)
                    {
                        if (!st.paint[qi]) continue;
                        Vector2 c0 = st.c[qi], c1 = st.c[qi + 1];
                        Vector2 R0 = c0 - st.n[qi] * st.half, L0 = c0 + st.n[qi] * st.half;
                        Vector2 R1 = c1 - st.n[qi + 1] * st.half, L1 = c1 + st.n[qi + 1] * st.half;
                        float qx0 = Mathf.Min(Mathf.Min(R0.x, L0.x), Mathf.Min(R1.x, L1.x)), qx1 = Mathf.Max(Mathf.Max(R0.x, L0.x), Mathf.Max(R1.x, L1.x));
                        float qz0 = Mathf.Min(Mathf.Min(R0.y, L0.y), Mathf.Min(R1.y, L1.y)), qz1 = Mathf.Max(Mathf.Max(R0.y, L0.y), Mathf.Max(R1.y, L1.y));
                        if (qx1 < tx0 || qx0 > tx1 || qz1 < tz0 || qz0 > tz1) continue;
                        // the quad R0 R1 L1 L0, anticlockwise
                        cutNext.Clear();
                        foreach (var piece in cutPieces)
                        {
                            // inside: clip by the four edges
                            ClipHalfPlane(piece, R0, R1, true, cpA);
                            ClipHalfPlane(cpA, R1, L1, true, cpB);
                            ClipHalfPlane(cpB, L1, L0, true, cpA);
                            ClipHalfPlane(cpA, L0, R0, true, cpB);
                            if (cpB.Count >= 3 && PolyArea(cpB) > 1e-5f)
                                EmitStripPiece(stripBk, cpB, st, qi, c0, c1);
                            else if (cpB.Count < 3 || PolyArea(cpB) <= 1e-5f)
                            { cutNext.Add(piece); continue; }
                            // outside: what lies beyond each edge in turn
                            var rem = NewPoly(); rem.AddRange(piece);
                            Vector2 e0, e1;
                            for (int k = 0; k < 4 && rem.Count >= 3; k++)
                            {
                                e0 = k == 0 ? R0 : k == 1 ? R1 : k == 2 ? L1 : L0;
                                e1 = k == 0 ? R1 : k == 1 ? L1 : k == 2 ? L0 : R0;
                                var outP = NewPoly();
                                ClipHalfPlane(rem, e0, e1, false, outP);
                                if (outP.Count >= 3 && PolyArea(outP) > 1e-6f) cutNext.Add(outP); else FreePoly(outP);
                                ClipHalfPlane(rem, e0, e1, true, cpA);
                                rem.Clear(); rem.AddRange(cpA);
                            }
                            FreePoly(rem);
                            FreePoly(piece);
                        }
                        cutPieces.Clear();
                        cutPieces.AddRange(cutNext);
                    }
                }
                // what is left of the triangle: the fan's asphalt
                foreach (var piece in cutPieces)
                {
                    int v0 = fanBk.v.Count;
                    foreach (var p in piece)
                    {
                        fanBk.v.Add(p);
                        fanBk.uv.Add(new Vector2((p.x + tm.origin.x) / 12f, (p.z + tm.origin.z) / 12f));
                    }
                    for (int k = 1; k + 1 < piece.Count; k++) { fanBk.t.Add(v0); fanBk.t.Add(v0 + k + 1); fanBk.t.Add(v0 + k); }
                }
            }
            fanTriCount = (fanBk.t.Count - fanTriStart) / 3;
            if (stripBk != null && tm.tap != null && stripBk.v.Count > stripV0)
                tm.tap.fans.Add(new RoadTap.Fan { slot = stripSlot, bucketV = stripV0, count = stripBk.v.Count - stripV0, node = node, mouths = 0,
                                                  triStart = stripT0, triCount = (stripBk.t.Count - stripT0) / 3 });
        }

        /// <summary>One cut piece of a strip quad: U across the line's own
        /// texels by the lateral, V along it.</summary>
        static void EmitStripPiece(Bucket bk, List<Vector3> piece, ThroughStrip st, int qi, Vector2 c0, Vector2 c1)
        {
            int v0 = bk.v.Count;
            var seg = c1 - c0;
            float L2 = Mathf.Max(seg.sqrMagnitude, 1e-8f);
            foreach (var p in piece)
            {
                var q = new Vector2(p.x, p.z);
                float t = Mathf.Clamp01(Vector2.Dot(q - c0, seg) / L2);
                var cq = Vector2.Lerp(c0, c1, t);
                var nn = Vector2.Lerp(st.n[qi], st.n[qi + 1], t);
                nn = nn.sqrMagnitude > 1e-8f ? nn.normalized : st.n[qi];
                float lat = Vector2.Dot(q - cq, nn);
                float f = Mathf.Clamp01((lat + st.half) / (2f * st.half));
                bk.v.Add(p);
                bk.uv.Add(new Vector2(Mathf.Lerp(st.uR, st.uL, f), Mathf.Lerp(st.v[qi], st.v[qi + 1], t)));
            }
            for (int k = 1; k + 1 < piece.Count; k++) { bk.t.Add(v0); bk.t.Add(v0 + k + 1); bk.t.Add(v0 + k); }
        }
    }
}
