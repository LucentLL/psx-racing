using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// LANE-USE PAINT (roads pass L6, 2026-10-03; plan A8 + owner Q5 (b)/(c);
    /// Docs/CHARLOTTE.md "L6").
    ///
    /// Every mark here is CUT INTO the ribbon (critic C5): the span it lies on
    /// is drawn as columns and the mark is one of them, coplanar with the
    /// pavement around it, in the road's own slot and from the texture's own
    /// solid-white texels. No lift, no decal, no new draw call.
    ///
    ///   AUX DW     a merge zone's aux lane (L5) gets the wide dotted white
    ///              lane line (0.20 m, 3 ft line / 9 ft gap) at the through
    ///              lanes' edge, from the nose N to where the taper starts
    ///              (MUTCD 3B.04: none through the taper).
    ///   GORE CH    the gore between N and the physical nose P (where the two
    ///              pavements are 4.5 m apart and the painted gore ends) is
    ///              bounded by two 0.20 m solid white channelizing lines that
    ///              meet at N: the host's edge line on that side, and the
    ///              ramp's inner lane edge - drawn on the HOST's ribbon while
    ///              it lies over the host's shoulder, on the ramp's own (its
    ///              inner edge line, white, no longer stood down by the clip)
    ///              once it is over its own. The ramp's yellow left edge
    ///              starts at P.
    ///   EXIT DROP  a style G diverge (OSM drops the lane at the exit node):
    ///              the line between the exit-only lane and the through lanes
    ///              is wide dotted for min(OSM's run, 805 m) before the node.
    ///   MOUTH      a branch clipped onto a mitred host at over 35 degrees: the
    ///              host's edge line stops across its mouth (+0.6 m each side).
    ///   LS         a lane line between a turn-only lane (turn:lanes, or a TAPR
    ///              left-turn bay) and a through lane is solid white.
    ///   ARROW/ONLY tier 1: a turn arrow and the word ONLY in each turn-only
    ///              lane before its junction; an exit-only lane gets them too.
    ///   YIELD      tier 1: a row of shark teeth where a link ends at a yield
    ///              (OSM give_way, or a link entering a street with no signal
    ///              or stop at more than 35 degrees).
    /// PSX_CITY_MARKS=0 draws L5's paint.
    /// </summary>
    public static partial class CityMeshes
    {
        public const byte MkOff = 0, MkLS = 1, MkCH = 2, MkDW = 3, MkAuxDW = 4, MkGoreCH = 5, MkGlyph = 6;
        public static readonly string[] MarkNames = { "off", "LS", "CH", "DW", "aux DW", "gore CH", "glyph" };

        /// <summary>One mark on one edge over [s0, s1].
        ///   MkOff / MkLS / MkCH / MkDW  override the layout line k there
        ///   MkAuxDW   a line at the through lanes' edge on side (+1 plus)
        ///   MkGoreCH  a line at the ramp's inner lane edge: chain x =
        ///             c0 + (fromA ? s : L - s), lateral sideH * pd(x)
        ///   MkGlyph   convex polygons in (lat, s)</summary>
        public sealed class PaintMark
        {
            public byte style; public int edge; public float s0, s1;
            public int k = -1, side;
            public float[] px, pd; public bool fromA; public float c0, xN, blend; public int sideH;
            public List<Vector2[]> pieces; public float[] bps; public float hdg;
            public int zone; public string what;
        }

        public static bool MarksOn = System.Environment.GetEnvironmentVariable("PSX_CITY_MARKS") != "0";
        public static readonly Dictionary<int, List<PaintMark>> Marks = new Dictionary<int, List<PaintMark>>();
        static CityMap marksMap; static Trims marksTrims;

        /// <summary>The last build's tallies (the PAINT report's MARKS lines).</summary>
        public sealed class MarkTally
        {
            public int auxZones, auxMarks, goreZones, goreHostPieces, goreRampPieces, noGore, exitDrops, exitDropPieces, mouths, mouthsSkipped,
                       lsLines, lsEdges, bays, groups, groupsSkipped, arrowsL, arrowsR, yields, yieldsSkipped, yieldsTagged,
                       crosswalks, crosswalkBars, crosswalksSkipped;
            public float auxM, exitDropM, lsM, buildMs; public string partsMs = "";
            /// <summary>Per zone (by id): the gore's P on the host chain and
            /// the ramp travel at N and P (0: no gore).</summary>
            public readonly Dictionary<int, (float xP, float tO, float tP)> gore = new Dictionary<int, (float, float, float)>();
            public readonly List<string> notes = new List<string>();
        }
        public static MarkTally MarkStats = new MarkTally();

        /// <summary>The marks of the map as these trims draw it (built once per
        /// trims, after the merge zones).</summary>
        public static void EnsureMarks(CityMap map, Trims t)
        {
            if (marksMap == map && marksTrims == t) return;
            marksMap = map; marksTrims = t;
            Marks.Clear();
            MarkStats = new MarkTally();
            if (!MarksOn || map == null || t == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var z in Zones) ZoneMarks(map, t, z);
            float t0 = (float)sw.Elapsed.TotalMilliseconds;
            MouthMarks(map, t);
            CrosswalkMarks(map, t);
            float t1 = (float)sw.Elapsed.TotalMilliseconds;
            TurnMarks(map, t);
            float t2 = (float)sw.Elapsed.TotalMilliseconds;
            YieldMarks(map, t);
            MarkStats.buildMs = (float)sw.Elapsed.TotalMilliseconds;
            MarkStats.partsMs = $"zones {t0:0} / mouths {t1 - t0:0} / turn-only {t2 - t1:0} / yields {MarkStats.buildMs - t2:0} ms";
            Debug.Log($"[City] lane-use paint (L6): {MarkStats.auxZones} aux lanes dotted ({MarkStats.auxM:0} m), {MarkStats.goreZones} gores white to their nose, " +
                      $"{MarkStats.exitDrops} exit-only drops ({MarkStats.exitDropM:0} m), {MarkStats.mouths} mouths, {MarkStats.lsLines} turn-only lines solid, " +
                      $"{MarkStats.groups} arrow+ONLY groups, {MarkStats.yields} yield lines, {MarkStats.crosswalks} crosswalks; {MarkStats.buildMs:0} ms");
        }

        public static List<PaintMark> MarksOf(int edge) => Marks.TryGetValue(edge, out var l) ? l : null;

        static void Note(string what) { if (MarkStats.notes.Count < 40) MarkStats.notes.Add(what); }

        static void AddMark(PaintMark m)
        {
            if (m.s1 - m.s0 < 0.05f && m.style != MkGlyph) return;
            if (!Marks.TryGetValue(m.edge, out var l)) Marks[m.edge] = l = new List<PaintMark>(2);
            l.Add(m);
        }

        /// <summary>The style the marks give layout line k of an edge at s
        /// (255: none) - the builder's and the audit's one rule.</summary>
        public static byte OverrideAt(CityMap.Edge e, int k, float s)
        {
            var l = MarksOf(e.index);
            if (l == null) return 255;
            foreach (var m in l)
                if (m.k == k && m.style <= MkDW && s >= m.s0 && s <= m.s1) return m.style;
            return 255;
        }

        static int LineOfKind(LineModel.Layout lay, byte kind)
        {
            for (int i = 0; i < lay.kind.Length; i++) if (lay.kind[i] == kind) return i;
            return -1;
        }

        // ================================================================
        //  Merge zones: the aux lane's DW, the gore's CH, the exit-only drop
        // ================================================================

        static void ZoneMarks(CityMap map, Trims t, MergeZone z)
        {
            // ---- AUX DW: over the aux lane's full width, N to the taper start
            bool any = false;
            foreach (int ei in z.auxEdges)
            {
                var e = map.edges[ei];
                if (e.lmEase == null) continue;
                foreach (var q in e.lmEase)
                {
                    if (!q.aux || q.auxZone != z.id || q.auxXF >= q.auxD - 0.5f) continue;
                    float dLo = q.auxArm > 0 ? q.auxXF : -q.auxD, dHi = q.auxArm > 0 ? q.auxD : -q.auxXF;
                    float a = q.fromA ? dLo - q.d0 : e.length - (dHi - q.d0);
                    float b = q.fromA ? dHi - q.d0 : e.length - (dLo - q.d0);
                    a = Mathf.Max(0f, a); b = Mathf.Min(e.length, b);
                    if (b - a < 0.3f) continue;
                    AddMark(new PaintMark { style = MkAuxDW, edge = ei, s0 = a, s1 = b, side = q.side, zone = z.id });
                    MarkStats.auxMarks++; MarkStats.auxM += b - a; any = true;
                }
            }
            if (any) MarkStats.auxZones++;

            // ---- GORE CH: from N to the physical nose P
            var host = z.hostChain;
            var br = BuildChain(map, map.edges[z.branch], z.node, linkChain: true, GoreReach + 10f, host);
            float tO = -1f;
            for (int j = 0; j < br.seg.Count; j++)
            {
                if (br.seg[j].index != z.cutEdge) continue;
                float lo = Mathf.Min(br.sA[j], br.sB[j]), hi = Mathf.Max(br.sA[j], br.sB[j]);
                if (z.sCut < lo - 1e-3f || z.sCut > hi + 1e-3f) continue;
                tO = br.cum[j] + Mathf.Abs(z.sCut - br.sA[j]);
                break;
            }
            if (tO < 0f) { MarkStats.noGore++; return; }
            var px = new List<float>(64); var pd = new List<float>(64);
            float tP = -1f, xP = -1f;
            for (float tt = Mathf.Max(0f, tO - 4f); tt <= tO + GoreReach; tt += tt < tO + 20f ? 1f : 2f)
            {
                if (!GoreSample(host, br, tt, z.side, out float gap, out float x, out float dist)) { if (tt < tO) continue; break; }
                if (px.Count == 0 || x > px[px.Count - 1] + 1e-3f) { px.Add(x); pd.Add(dist); }
                if (tt < tO) continue;
                if (gap > GoreMaxGap) break;
                tP = tt; xP = x;
            }
            if (tP < tO + 2f || xP < z.D + 1f) { MarkStats.noGore++; return; }
            MarkStats.gore[z.id] = (xP, tO, tP);
            var pxa = px.ToArray(); var pda = pd.ToArray();
            bool hostOk = false, rampOk = false;
            // the host pieces over [D, xP]: the edge line CH, the ramp's lane edge CH
            var done = new HashSet<int>();
            for (int j = 0; j < host.seg.Count; j++)
            {
                var H = host.seg[j];
                if (done.Contains(H.index)) continue;
                if (!host.PieceRange(H, out float p0, out float p1) || p1 <= z.D || p0 >= xP) continue;
                done.Add(H.index);
                // the piece's s <-> chain x (its first segment)
                int j0 = -1;
                for (int q = 0; q < host.seg.Count; q++) if (host.seg[q] == H) { j0 = q; break; }
                bool fromA = host.sB[j0] > host.sA[j0];
                float c0 = host.cum[j0] - (fromA ? host.sA[j0] : H.length - host.sA[j0]);
                float xa = Mathf.Max(z.D, p0), xb = Mathf.Min(xP, p1);
                float sa = fromA ? xa - c0 : H.length - (xa - c0), sb = fromA ? xb - c0 : H.length - (xb - c0);
                if (sa > sb) { float tmp = sa; sa = sb; sb = tmp; }
                sa = Mathf.Clamp(sa, 0f, H.length); sb = Mathf.Clamp(sb, 0f, H.length);
                if (!z.HostAt(map, 0.5f * (xa + xb), out var H2, out _, out int sideH) || H2 != H) continue;
                var lay = LineModel.LayoutOf(H);
                int k = LineOfKind(lay, sideH > 0 ? LineModel.KEdgeP : LineModel.KEdgeM);
                if (k < 0) continue;
                AddMark(new PaintMark { style = MkCH, edge = H.index, s0 = sa, s1 = sb, k = k, zone = z.id, what = "gore: host edge" });
                AddMark(new PaintMark { style = MkGoreCH, edge = H.index, s0 = sa, s1 = sb, px = pxa, pd = pda, fromA = fromA, c0 = c0, sideH = sideH, zone = z.id,
                                        xN = z.D, blend = Mathf.Clamp(0.5f * (xP - z.D), 0.5f, 6f), what = "gore: ramp lane edge" });
                MarkStats.goreHostPieces++; hostOk = true;
            }
            // the ramp pieces over [tO, tP]: their inner edge line white
            done.Clear();
            for (int j = 0; j < br.seg.Count; j++)
            {
                var E = br.seg[j];
                if (done.Contains(E.index)) continue;
                if (!br.PieceRange(E, out float b0, out float b1) || b1 <= tO || b0 >= tP) continue;
                done.Add(E.index);
                float ta = Mathf.Max(tO, b0), tb = Mathf.Min(tP, b1);
                if (!br.Walk(0.5f * (ta + tb), out var Em, out float sEm, out var pm, out _)) continue;
                if (Em != E) continue;
                host.Project(pm, out _, out _, out _, out var dirM);
                var rM = new Vector2(-dirM.y, dirM.x);
                var tE = E.TangentAt(sEm);
                int innerE = Vector2.Dot(new Vector2(-tE.y, tE.x), rM) * z.side < 0f ? 1 : -1;
                var lay = LineModel.LayoutOf(E);
                int k = LineOfKind(lay, innerE > 0 ? LineModel.KEdgeP : LineModel.KEdgeM);
                if (k < 0) continue;
                float sa = ChainS(br, E, ta), sb = ChainS(br, E, tb);
                if (float.IsNaN(sa) || float.IsNaN(sb)) continue;
                if (sa > sb) { float tmp = sa; sa = sb; sb = tmp; }
                AddMark(new PaintMark { style = MkCH, edge = E.index, s0 = sa, s1 = sb, k = k, zone = z.id, what = "gore: ramp inner edge" });
                MarkStats.goreRampPieces++; rampOk = true;
            }
            if (hostOk && rampOk) MarkStats.goreZones++; else MarkStats.noGore++;

            // ---- EXIT DROP: a style G diverge's exit-only lane, DW back from the node
            if (z.style == 'G' && !z.merge) ExitDropMarks(map, t, z);
        }

        /// <summary>A chain arc on one of its pieces as that piece's own s
        /// (NaN: the piece does not hold it).</summary>
        static float ChainS(Chain ch, CityMap.Edge E, float t)
        {
            for (int j = 0; j < ch.seg.Count; j++)
            {
                if (ch.seg[j] != E || t < ch.cum[j] - 1e-3f || t > ch.cum[j + 1] + 1e-3f) continue;
                float dir = ch.sB[j] >= ch.sA[j] ? 1f : -1f;
                return Mathf.Clamp(ch.sA[j] + dir * (t - ch.cum[j]), 0f, E.length);
            }
            return float.NaN;
        }

        /// <summary>The ramp at travel tt beside its host: the pavement gap,
        /// and its inner edge line's centre as host-chain x and distance off
        /// the host's line toward the zone side.</summary>
        static bool GoreSample(Chain host, Chain br, float tt, int side, out float gap, out float x, out float dist)
        {
            gap = 0f; x = 0f; dist = 0f;
            if (!br.Walk(tt, out var E, out float sE, out var p, out var dirL)) return false;
            host.Project(p, out var H, out float sM, out var q, out var dirM, out bool atEnd, out _);
            if (atEnd) return false;
            var rM = new Vector2(-dirM.y, dirM.x);
            float off = Vector2.Dot(p - q, rM);
            if (Mathf.Abs(off) > 0.4f && (off >= 0f ? 1 : -1) != side) return false;
            var rL = new Vector2(-dirL.y, dirL.x);
            float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
            float inExt = ExtToward(E, sE, rL, lSign), inSh = ShoulderToward(E, sE, rL, lSign);
            var inLane = p + rL * (lSign * Mathf.Max(0f, inExt - inSh));
            var lineC = inLane - rL * (lSign * RoadProfiles.PaintHalfM);
            float hExt = HostEdgeNoAux(H, sM, rM, side, out float hSh);
            float g = Vector2.Dot(inLane - q, rM) * side - (hExt - hSh);
            gap = g - inSh - hSh;
            host.Project(lineC, out _, out _, out var q2, out var d2, out bool atEnd2, out x);
            if (atEnd2) return false;
            dist = Vector2.Dot(lineC - q2, new Vector2(-d2.y, d2.x)) * side;
            return true;
        }

        /// <summary>The ramp's lane edge line in the host's frame at s.</summary>
        static float GoreLat(PaintMark m, CityMap.Edge e, float s)
        {
            float x = m.c0 + (m.fromA ? s : e.length - s);
            var px = m.px; var pd = m.pd;
            float d;
            if (x <= px[0]) d = pd[0];
            else if (x >= px[px.Length - 1]) d = pd[pd.Length - 1];
            else
            {
                int lo = 0, hi = px.Length - 1;
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (px[mid] <= x) lo = mid; else hi = mid; }
                float f = (x - px[lo]) / Mathf.Max(1e-4f, px[hi] - px[lo]);
                d = Mathf.Lerp(pd[lo], pd[hi], f);
            }
            return m.sideH * d;
        }

        /// <summary>The gore's ramp-side line as drawn, from the host's edge line
        /// at the same s: never inside the through lanes (where N was nudged
        /// toward the node the ramp's lane edge still lies a little inside),
        /// and eased onto the host's line over the first metres past N, so the
        /// two white lines always meet at N (the lane-edge offsets of the two
        /// roads differ by a shoulder's rounding at a few gores).</summary>
        static float GoreDrawn(PaintMark m, CityMap.Edge e, float s, float hostLat)
        {
            float lat = GoreLat(m, e, s);
            lat = m.sideH > 0 ? Mathf.Max(lat, hostLat) : Mathf.Min(lat, hostLat);
            float x = m.c0 + (m.fromA ? s : e.length - s);
            float f = LineModel.Smooth((x - m.xN) / Mathf.Max(0.1f, m.blend));
            return Mathf.Lerp(hostLat, lat, f);
        }

        static readonly List<LineModel.LineAt> markLines = new List<LineModel.LineAt>(16);

        /// <summary>The through lanes' edge on a side at s: the edge line where
        /// it would be with no aux lane (NaN: none).</summary>
        static float AuxThroughLat(CityMap.Edge e, float s, int side)
        {
            LineModel.LinesAt(e, s, markLines);
            var lay = LineModel.LayoutOf(e);
            byte want = side > 0 ? LineModel.KEdgeP : LineModel.KEdgeM;
            foreach (var l in markLines)
                if (lay.kind[l.k] == want) return l.lat - side * LineModel.AuxLineShift(e, s, side);
            return float.NaN;
        }

        /// <summary>A style G diverge: the k lanes OSM drops at the node are
        /// exit-only upstream of it - their line to the through lanes DW over
        /// min(OSM's run, 805 m); an arrow and ONLY in the exit lane.</summary>
        static void ExitDropMarks(CityMap map, Trims t, MergeZone z)
        {
            float run = Mathf.Min(805f, ZoneDataAux(map, t, z));
            if (run < 5f) { Note($"exit drop n{z.node}: OSM's lane runs {run:0.0} m"); return; }
            var near = new List<int>(8);
            var ch = MitredChain(map, t, map.edges[z.other], z.node, run + 2f, near);
            var d0 = OutDir(map.edges[z.other], z.node);
            int sideC = Vector2.Dot(new Vector2(-d0.y, d0.x), z.nZ) >= 0f ? 1 : -1;
            bool any = false, arrow = false;
            for (int i = 0; i < ch.edges.Count; i++)
            {
                var P = ch.edges[i];
                if (!ch.PieceRange(P, out float c0, out float c1) || c0 >= run) break;

                var dP = OutDir(P, near[i]);
                var nP = new Vector2(-dP.y, dP.x) * sideC;
                float sMid = P.length * 0.5f;
                var tP = P.TangentAt(sMid);
                int sideP = Vector2.Dot(new Vector2(-tP.y, tP.x), nP) >= 0f ? 1 : -1;
                var lay = LineModel.LayoutOf(P);
                var whites = new List<int>(6);
                float ymid = float.NaN;
                if (!P.oneway)
                {
                    float ylo = float.MaxValue, yhi = float.MinValue;
                    for (int k = 0; k < lay.m.Length; k++)
                        if (lay.kind[k] == LineModel.KYellow || lay.kind[k] == LineModel.KYellowDash) { ylo = Mathf.Min(ylo, lay.m[k]); yhi = Mathf.Max(yhi, lay.m[k]); }
                    if (ylo > yhi) { Note($"exit drop n{z.node}: e{P.index} two-way with no centre line"); break; }
                    ymid = 0.5f * (ylo + yhi);
                }
                // m < ymid is the plus side of the centre
                for (int k = 0; k < lay.m.Length; k++)
                    if (lay.kind[k] == LineModel.KWhiteDash && (P.oneway || (lay.m[k] < ymid) == (sideP > 0))) whites.Add(k);
                if (sideP > 0) whites.Sort((a, b) => lay.m[a].CompareTo(lay.m[b])); else whites.Sort((a, b) => lay.m[b].CompareTo(lay.m[a]));
                if (whites.Count < z.k) { Note($"exit drop n{z.node}: e{P.index} has {whites.Count} lane lines for k {z.k}"); break; }
                int kl = whites[z.k - 1];
                bool fromNode = P.a == near[i];
                float xa = 0f, xb = Mathf.Min(run, c1) - c0;
                float sa = fromNode ? xa : P.length - xb, sb = fromNode ? xb : P.length - xa;
                AddMark(new PaintMark { style = MkDW, edge = P.index, s0 = Mathf.Max(0f, sa), s1 = Mathf.Min(P.length, sb), k = kl, zone = z.id, what = "exit-only drop" });
                MarkStats.exitDropPieces++; MarkStats.exitDropM += sb - sa; any = true;
                // tier 1 (Q5 b): an arrow and ONLY in the exit lane, the group ending
                // 30 m before the node, elongated for the freeway (MUTCD 3B.20)
                if (!arrow && P.oneway && c1 >= 30f + 26f)
                {
                    float xRef = 30f - c0;   // on this piece, from its near end
                    if (xRef >= 0f && xRef < P.length)
                    {
                        float sRef = fromNode ? xRef : P.length - xRef;
                        // travel on P runs TOWARD the node on a diverge's other arm
                        // (a one-way's points run in its travel, toward the node here)
                        int n = RoadProfiles.All[P.profile].lanes;
                        int lane = sideP > 0 ? 0 : n - 1;    // the outermost lane on the zone side (lane 0 at plus)
                        int turn = sideP > 0 ? -1 : 1;       // plus is the left of travel
                        // elongated (x2) on a freeway-like road (MUTCD 3B.20), standard on a street
                        float sy = P.link || P.cls >= 5 || P.cls == 4 ? 2.0f : 1.0f;
                        if (P.b == near[i] && n >= 2 && Group(map, t, P, sRef, true, LaneLatOneWay(P, lane), turn, sy, "exit-only")) arrow = true;
                    }
                }
            }
            if (any) MarkStats.exitDrops++;
        }

        static float PlusOf(CityMap.Edge e) => e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;

        /// <summary>A one-way's lane i (0 at the plus edge) centre, lateral.</summary>
        static float LaneLatOneWay(CityMap.Edge e, int i)
        {
            var pr = RoadProfiles.All[e.profile];
            return PlusOf(e) - (pr.shl + (i + 0.5f) * RoadProfiles.LaneM);
        }

        // ================================================================
        //  Mouths: the host's edge line stops across a branch over 35 deg
        // ================================================================

        static void MouthMarks(CityMap map, Trims t)
        {
            float cos35 = Mathf.Cos(35f * Mathf.Deg2Rad);
            foreach (var L in map.edges)
            {
                if (L.a == L.b) continue;
                for (int end = 0; end < 2; end++)
                {
                    int node = end == 0 ? L.a : L.b;
                    int hi = t.BranchAt(L, node);
                    if (hi < 0 || !t.mitre[node]) continue;
                    if (ZoneAt(L.index, node) != null) continue;
                    var M = map.edges[hi];
                    var dB = OutDir(L, node); var dM = OutDir(M, node);
                    float c = Vector2.Dot(dB, dM);
                    if (c >= cos35) continue;
                    var lay = LineModel.LayoutOf(M);
                    float sN = M.a == node ? 0f : M.length;
                    var tM = M.TangentAt(sN);
                    var nM = new Vector2(-tM.y, tM.x);
                    int sigma = Vector2.Dot(dB, nM) >= 0f ? 1 : -1;
                    int k = LineOfKind(lay, sigma > 0 ? LineModel.KEdgeP : LineModel.KEdgeM);
                    if (k < 0) { MarkStats.mouthsSkipped++; continue; }
                    LineModel.Extents(M, sN, out float eM, out float eP);
                    float E = sigma > 0 ? eP : eM;
                    float w = L.HalfMax;
                    // in the node's frame: x along M's outward direction, y toward the branch
                    var yAx = new Vector2(-dM.y, dM.x);
                    if (Vector2.Dot(yAx, dB) < 0f) yAx = -yAx;
                    float cs = Vector2.Dot(dB, dM), sn = Mathf.Max(0.05f, Vector2.Dot(dB, yAx));
                    float x0 = float.MaxValue, x1 = float.MinValue;
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        float tt = (E - sg * w * cs) / sn;
                        float xx = tt * cs - sg * w * sn;
                        x0 = Mathf.Min(x0, xx); x1 = Mathf.Max(x1, xx);
                    }
                    x0 = Mathf.Max(0f, x0 - 0.6f); x1 = Mathf.Min(M.length, x1 + 0.6f);
                    if (x1 - x0 < 0.3f) { MarkStats.mouthsSkipped++; continue; }
                    float s0 = M.a == node ? x0 : M.length - x1, s1 = M.a == node ? x1 : M.length - x0;
                    AddMark(new PaintMark { style = MkOff, edge = M.index, s0 = s0, s1 = s1, k = k, what = $"mouth of e{L.index}" });
                    MarkStats.mouths++;
                }
            }
        }

        // ================================================================
        //  Turn-only lanes: solid lines, arrows and ONLY
        // ================================================================

        /// <summary>An edge's turn-only lanes per direction (bit i = OSM lane
        /// i, 0 the leftmost in travel), a TAPR left-turn bay counted as its
        /// leftmost lane where turn:lanes says nothing.</summary>
        public static void TurnOnly(CityMap.Edge e, out int fwd, out int bwd, out int nF, out int nB, out bool bay)
        {
            fwd = e.lsTurnOnly & 0xFF; bwd = e.oneway ? 0 : (e.lsTurnOnly >> 8) & 0xFF;
            nF = e.oneway ? RoadProfiles.All[e.profile].lanes : e.lsNF; nB = e.oneway ? 0 : e.lsNB;
            bay = false;
            if ((e.lsFlags & 2) != 0 && fwd == 0 && nF >= 2) { fwd = 1; bay = true; }
            if ((e.lsFlags & 4) != 0 && bwd == 0 && nB >= 2) { bwd = 1; bay = true; }
        }
        static void TurnBits(CityMap.Edge e, out int fwd, out int bwd, out int nF, out int nB)
        {
            TurnOnly(e, out fwd, out bwd, out nF, out nB, out bool bay);
            if (bay) MarkStats.bays++;
        }

        /// <summary>The through lanes' edge on a side at s (audits).</summary>
        public static float AuxThroughLatAt(CityMap.Edge e, float s, int side) => AuxThroughLat(e, s, side);

        /// <summary>The gore's two lines on a host at s, as drawn (audits): the
        /// ramp's lane edge (kept outside the through lanes) and the host's
        /// edge line (NaN: not white CH there).</summary>
        public static bool GoreLatAt(CityMap.Edge e, float s, out float lat, out float hostLat)
        {
            lat = 0f; hostLat = float.NaN;
            var l = MarksOf(e.index);
            if (l == null) return false;
            foreach (var m in l)
            {
                if (m.style != MkGoreCH || s < m.s0 - 1e-3f || s > m.s1 + 1e-3f) continue;
                lat = GoreLat(m, e, s);
                foreach (var h in l)
                {
                    if (h.style != MkCH || h.zone != m.zone || h.k < 0 || s < h.s0 - 1e-3f || s > h.s1 + 1e-3f) continue;
                    LineModel.LinesAt(e, s, rawA);
                    if (FindLat(rawA, h.k, out float hl))
                    {
                        hostLat = hl;
                        lat = GoreDrawn(m, e, s, hl);
                    }
                    break;
                }
                return true;
            }
            return false;
        }

        static void TurnMarks(CityMap map, Trims t)
        {
            foreach (var e in map.edges)
            {
                if (e.a == e.b || !e.hasLset || (e.lsFlags & 1) == 0) continue;
                TurnBits(e, out int fwd, out int bwd, out int nF, out int nB);
                if (fwd == 0 && bwd == 0) continue;
                var lay = LineModel.LayoutOf(e);
                var pr = RoadProfiles.All[e.profile];
                // the white lane lines of each direction, from its leftmost lane out
                var wf = new List<int>(6); var wb = new List<int>(6);
                float mid = float.NaN;
                if (!e.oneway)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int k = 0; k < lay.m.Length; k++)
                        if (lay.kind[k] == LineModel.KYellow || lay.kind[k] == LineModel.KYellowDash) { lo = Mathf.Min(lo, lay.m[k]); hi = Mathf.Max(hi, lay.m[k]); }
                    if (lo > hi) continue;
                    mid = 0.5f * (lo + hi);
                }
                for (int k = 0; k < lay.m.Length; k++)
                {
                    if (lay.kind[k] != LineModel.KWhiteDash) continue;
                    if (e.oneway || lay.m[k] > mid) wf.Add(k); else wb.Add(k);
                }
                wf.Sort((a, b) => lay.m[a].CompareTo(lay.m[b]));        // forward: from the centre (or the left edge) out
                wb.Sort((a, b) => lay.m[b].CompareTo(lay.m[a]));        // backward: from the centre out
                bool lsAny = false;
                void Solid(List<int> ws, int bits, int n)
                {
                    if (bits == 0 || ws.Count != n - 1) return;
                    for (int i = 0; i + 1 < n; i++)
                    {
                        bool a = ((bits >> i) & 1) != 0, b = ((bits >> (i + 1)) & 1) != 0;
                        if (a == b) continue;
                        AddMark(new PaintMark { style = MkLS, edge = e.index, s0 = 0f, s1 = e.length, k = ws[i], what = "turn-only" });
                        MarkStats.lsLines++; MarkStats.lsM += e.length; lsAny = true;
                    }
                }
                Solid(wf, fwd, nF);
                Solid(wb, bwd, nB);
                if (lsAny) MarkStats.lsEdges++;
                // tier 1 (Q5 b): an arrow and ONLY in each turn-only lane before its
                // junction (a freeway's exit-only lane has the zone's elongated group)
                if (CityTier.Of(e) != 1 || (!e.link && (e.cls >= 5 || (e.cls == 4 && e.oneway)))) continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    int bits = dir == 0 ? fwd : bwd, n = dir == 0 ? nF : nB;
                    if (bits == 0 || n < 2) continue;
                    bool forward = dir == 0;
                    int nd = forward ? e.b : e.a;
                    if (map.nodeEdges[nd].Count < 3) { MarkStats.groupsSkipped++; continue; }
                    float sEnd = forward ? e.length - t.TrimAt(e, e.b) : t.TrimAt(e, e.a);
                    // behind a crosswalk's stop bar (roads pass L7), else 3 m back
                    float cwBack = CitySignals.CrosswalkStopBack(map, t, e, nd);
                    float back = cwBack > 0f ? cwBack + 1.75f : 3.0f;
                    float sRef = forward ? sEnd - back : sEnd + back;
                    for (int i = 0; i < n; i++)
                    {
                        if (((bits >> i) & 1) == 0) continue;
                        int turn = i < n - 1 - i ? -1 : i > n - 1 - i ? 1 : 0;
                        if (turn == 0) { MarkStats.groupsSkipped++; continue; }
                        // the lane's centre, m from the plus edge (RoadProfiles.LinesFor)
                        float m;
                        if (e.oneway) m = pr.shl + (i + 0.5f) * RoadProfiles.LaneM;
                        else
                        {
                            float medStart = pr.shl + e.lsNB * RoadProfiles.LaneM;
                            float farStart = medStart + (e.lsCentre == 2 ? RoadProfiles.LaneM : 0f);
                            m = forward ? farStart + (i + 0.5f) * RoadProfiles.LaneM : medStart - (i + 0.5f) * RoadProfiles.LaneM;
                        }
                        Group(map, t, e, sRef, forward, PlusOf(e) - m, turn, 1f, "turn-only");
                    }
                }
            }
        }

        // ---- the glyphs (local frame: x to the driver's right, y forward) ----
        const float ArrowH = 3.66f, WordH = 2.44f, GroupGap = 1.5f;

        static void GRect(List<Vector2[]> into, float x0, float x1, float y0, float y1) =>
            into.Add(new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) });

        /// <summary>A turn arrow (turn -1 left, +1 right), y 0..ArrowH, centred on x = 0.</summary>
        static void Arrow(List<Vector2[]> into, int turn)
        {
            const float H = ArrowH;
            float yA0 = H - 1.0f, yA1 = H - 0.70f, yM = 0.5f * (yA0 + yA1);
            var ps = new List<Vector2[]>(3);
            GRect(ps, 0.40f, 0.65f, 0f, yA0);                         // the stem
            GRect(ps, -0.05f, 0.65f, yA0, yA1);                       // the arm
            ps.Add(new[] { new Vector2(-0.05f, yM - 0.5f), new Vector2(-0.05f, yM + 0.5f), new Vector2(-0.65f, yM) });   // the head
            foreach (var p in ps)
            {
                if (turn > 0) for (int i = 0; i < p.Length; i++) p[i].x = -p[i].x;
                into.Add(p);
            }
        }

        /// <summary>The word ONLY, y 0..WordH, centred on x = 0.</summary>
        static void Only(List<Vector2[]> into)
        {
            const float H = WordH, w = 0.55f, t = 0.14f, gap = 0.15f;
            float x = -(4f * w + 3f * gap) * 0.5f;
            // O
            GRect(into, x, x + t, 0f, H); GRect(into, x + w - t, x + w, 0f, H);
            GRect(into, x + t, x + w - t, 0f, t); GRect(into, x + t, x + w - t, H - t, H);
            x += w + gap;
            // N: two bars and the diagonal from the top left down to the bottom right
            GRect(into, x, x + t, 0f, H); GRect(into, x + w - t, x + w, 0f, H);
            float dw = 0.12f;
            into.Add(new[] { new Vector2(x + t, H), new Vector2(x + t + dw, H), new Vector2(x + w - t, 0f), new Vector2(x + w - t - dw, 0f) });
            x += w + gap;
            // L
            GRect(into, x, x + t, 0f, H); GRect(into, x + t, x + w, 0f, t);
            x += w + gap;
            // Y: the stem and two arms meeting on it
            float c = x + 0.5f * w, ym = H * 0.45f;
            GRect(into, c - 0.5f * t, c + 0.5f * t, 0f, ym);
            into.Add(new[] { new Vector2(c - 0.5f * t, ym), new Vector2(c, ym), new Vector2(x + t, H), new Vector2(x, H) });
            into.Add(new[] { new Vector2(c, ym), new Vector2(c + 0.5f * t, ym), new Vector2(x + w, H), new Vector2(x + w - t, H) });
        }

        static readonly List<Vector2[]> glyphLocal = new List<Vector2[]>(32);

        /// <summary>An arrow + ONLY group in a lane (lateral laneLat), ENDING
        /// downstream at sRef: ONLY over the last WordH (x sy), the arrow
        /// GroupGap (x sy) before it - read in that order by the driver. Laid
        /// only where the lane runs at full width (no taper, aux lane, relay
        /// or shift change) inside the drawn ribbon.</summary>
        static bool Group(CityMap map, Trims t, CityMap.Edge e, float sRef, bool forward, float laneLat, int turn, float sy, string what)
        {
            float len = (WordH + GroupGap + ArrowH) * sy;
            float sLo = forward ? sRef - len : sRef, sHi = forward ? sRef : sRef + len;
            float d0 = t.TrimAt(e, e.a) + 1f, d1 = e.length - t.TrimAt(e, e.b) - 1f;
            if (sLo < d0 || sHi > d1) { MarkStats.groupsSkipped++; return false; }
            float shMid = LineModel.ShiftAt(e, 0.5f * (sLo + sHi));
            for (float s = sLo; s <= sHi + 1e-3f; s += 1f)
            {
                float sc = Mathf.Min(s, sHi);
                LineModel.Extents(e, sc, out float eM, out float eP);
                if ((e.lmPlus != 0f || e.lmMinus != 0f) && (Mathf.Abs(eM - e.lmMinus) > 1e-3f || Mathf.Abs(eP - e.lmPlus) > 1e-3f))
                { MarkStats.groupsSkipped++; return false; }
                if (LineModel.Relayed(e, sc) || Mathf.Abs(LineModel.ShiftAt(e, sc) - shMid) > 0.05f || AuxActive(e, sc)) { MarkStats.groupsSkipped++; return false; }
            }
            laneLat += shMid;
            glyphLocal.Clear();
            int wordFrom = glyphLocal.Count;
            Only(glyphLocal);
            int wordTo = glyphLocal.Count;
            Arrow(glyphLocal, turn);
            var pieces = new List<Vector2[]>(glyphLocal.Count);
            var bps = new List<float>(32);
            for (int g = 0; g < glyphLocal.Count; g++)
            {
                var p = glyphLocal[g];
                bool word = g >= wordFrom && g < wordTo;
                // back-distance of the piece's foot from sRef: the word's foot WordH back,
                // the arrow's foot the whole group back
                float foot = word ? WordH * sy : len;
                var q = new Vector2[p.Length];
                for (int i = 0; i < p.Length; i++)
                {
                    float back = foot - p[i].y * sy;
                    float s = forward ? sRef - back : sRef + back;
                    float lat = forward ? laneLat - p[i].x : laneLat + p[i].x;
                    q[i] = new Vector2(lat, s);
                    bps.Add(s);
                }
                pieces.Add(q);
            }
            bps.Sort();
            var u = new List<float>(bps.Count);
            foreach (float b in bps) if (u.Count == 0 || b - u[u.Count - 1] > 1e-3f) u.Add(b);
            var tg = e.TangentAt(0.5f * (sLo + sHi));
            if (!forward) tg = -tg;
            AddMark(new PaintMark { style = MkGlyph, edge = e.index, s0 = sLo, s1 = sHi, pieces = pieces, bps = u.ToArray(), side = turn, what = what,
                                    hdg = Mathf.Repeat(Mathf.Atan2(tg.x, tg.y) * Mathf.Rad2Deg, 360f) });
            MarkStats.groups++;
            if (turn < 0) MarkStats.arrowsL++; else MarkStats.arrowsR++;
            return true;
        }

        static bool AuxActive(CityMap.Edge e, float s) =>
            LineModel.AuxWidth(e, s, 1) > 1e-3f || LineModel.AuxWidth(e, s, -1) > 1e-3f;

        // ================================================================
        //  Yield lines (shark teeth) where a tier-1 link ends at a yield
        // ================================================================

        // ================================================================
        //  Crosswalks (roads pass L7, owner Q5 a)
        // ================================================================

        /// <summary>Continental crosswalk bars (0.6 m, 0.6 m apart, along the
        /// travel) across each crosswalk arm's whole pavement, CrossInsetM
        /// out from the junction's patch and CrossWideM wide; the lines stop
        /// short of it at the stop bar (MUTCD 3B.18 / 3B.16).</summary>
        static void CrosswalkMarks(CityMap map, Trims t)
        {
            if (!CitySignals.CrosswalksOn) return;
            const float bar = 0.6f, gap = 0.6f, edgeIn = 0.3f;
            foreach (var cw in CitySignals.Crosswalks(map, t))
            {
                var e = map.edges[cw.edge];
                float sLo = Mathf.Min(cw.sNear, cw.sFar), sHi = Mathf.Max(cw.sNear, cw.sFar);
                LineModel.Extents(e, 0.5f * (sLo + sHi), out float eM, out float eP);
                float lo = -eM + edgeIn, hi = eP - edgeIn;
                int nB = Mathf.FloorToInt((hi - lo + gap) / (bar + gap));
                if (nB < 2) { MarkStats.crosswalksSkipped++; continue; }
                float start = 0.5f * (lo + hi) - 0.5f * (nB * bar + (nB - 1) * gap);
                var pieces = new List<Vector2[]>(nB);
                for (int i = 0; i < nB; i++)
                {
                    float x0 = start + i * (bar + gap);
                    pieces.Add(new[] { new Vector2(x0, sLo), new Vector2(x0 + bar, sLo), new Vector2(x0 + bar, sHi), new Vector2(x0, sHi) });
                }
                var tg = e.TangentAt(0.5f * (sLo + sHi));
                AddMark(new PaintMark { style = MkGlyph, edge = e.index, s0 = sLo, s1 = sHi, pieces = pieces, bps = new[] { sLo, sHi }, what = "crosswalk",
                                        hdg = Mathf.Repeat(Mathf.Atan2(tg.x, tg.y) * Mathf.Rad2Deg, 360f) });
                // every line stops at the stop bar (the patch side of it is the crosswalk's)
                bool atA = e.a == cw.node;
                float mouth = atA ? t.atA[e.index] : e.length - t.atB[e.index];
                float oLo = Mathf.Min(mouth, cw.sStop), oHi = Mathf.Max(mouth, cw.sStop);
                var lay = LineModel.LayoutOf(e);
                for (int k = 0; lay != null && k < lay.kind.Length; k++)
                    AddMark(new PaintMark { style = MkOff, edge = e.index, s0 = oLo, s1 = oHi, k = k, what = "crosswalk" });
                MarkStats.crosswalks++; MarkStats.crosswalkBars += nB;
            }
        }

        static void YieldMarks(CityMap map, Trims t)
        {
            int nn = map.nodes.Length;
            // controlled nodes: a stop or signal there, or a signal within 40 m
            var ctl = new bool[nn]; var sig = new bool[nn]; var yieldTag = new HashSet<long>();
            if (map.nodeControl != null)
                for (int i = 0; i < nn && i < map.nodeControl.Length; i++) { ctl[i] = (map.nodeControl[i] & 6) != 0; sig[i] = map.nodeControl[i] == 4; }
            if (map.tagged != null)
                foreach (var tg in map.tagged)
                {
                    if (tg.edge < 0 || tg.edge >= map.edges.Length) continue;
                    var e = map.edges[tg.edge];
                    float d0 = tg.s, d1 = e.length - tg.s;
                    if (Mathf.Min(d0, d1) > 30f) continue;
                    int n = d0 <= d1 ? e.a : e.b;
                    if (tg.kind == 4) { ctl[n] = true; sig[n] = true; }
                    else if (tg.kind == 2) ctl[n] = true;
                    else if (tg.kind == 1) yieldTag.Add(((long)tg.edge << 1) | (n == e.b ? 1L : 0L));
                }
            var near = (bool[])ctl.Clone();
            foreach (var e in map.edges)
            {
                if (e.a == e.b || e.length > 40f) continue;
                if (sig[e.a]) near[e.b] = true;
                if (sig[e.b]) near[e.a] = true;
            }
            float cos35 = Mathf.Cos(35f * Mathf.Deg2Rad);
            foreach (var e in map.edges)
            {
                if (e.a == e.b || !e.link || !e.oneway || CityTier.Of(e) != 1) continue;
                int n = e.b;
                if (map.nodeEdges[n].Count < 3) continue;
                if (ZoneAt(e.index, n) != null) continue;
                bool tagged = yieldTag.Contains(((long)e.index << 1) | 1L) || (map.nodeControl != null && n < map.nodeControl.Length && (map.nodeControl[n] & 1) != 0);
                if (near[n]) continue;
                // it enters a street (not a freeway, not only other links)
                bool street = false, freeway = false;
                var dIn = -OutDir(e, n);   // travel into the node
                float bestCos = -1f;
                foreach (int oi in map.nodeEdges[n])
                {
                    if (oi == e.index) continue;
                    var o = map.edges[oi];
                    if (o.cls >= 5 && !o.link) freeway = true;
                    if (!o.link) street = true;
                    bestCos = Mathf.Max(bestCos, Vector2.Dot(dIn, OutDir(o, n)));
                }
                if (freeway || !street) continue;
                // a merge at a shallow angle is not a yield (DOT practice) unless tagged
                if (!tagged && bestCos >= cos35) continue;
                float sEnd = e.length - t.TrimAt(e, n);
                // a link clipped onto a mitred road ends where it meets that
                // road's pavement, not at the node on its centre line
                if (t.mitre[n] && t.BranchAt(e, n) >= 0)
                {
                    float best = -1f;
                    for (float d = 0f; d <= Mathf.Min(60f, e.length - 1f); d += 0.5f)
                    {
                        var p = e.PointAt(e.length - d);
                        bool clear = true;
                        foreach (int hx in new[] { t.throughA[n], t.throughB[n], t.BranchAt(e, n) })
                        {
                            if (hx < 0) continue;
                            var H = map.edges[hx];
                            if (DistTo(H, p) < H.HalfMax + 0.3f) { clear = false; break; }
                        }
                        if (clear) { best = e.length - d; break; }
                    }
                    if (best < 0f) { MarkStats.yieldsSkipped++; continue; }
                    sEnd = Mathf.Min(sEnd, best);
                }
                float sB = sEnd - 0.5f, sA = sB - 0.6f;
                if (sA < t.TrimAt(e, e.a) + 1f) { MarkStats.yieldsSkipped++; continue; }
                LineModel.LinesAt(e, sA, markLines);
                var lay = LineModel.LayoutOf(e);
                float lo = float.NaN, hi = float.NaN;
                foreach (var l in markLines)
                {
                    if (lay.kind[l.k] == LineModel.KEdgeM) lo = l.lat + lay.half;
                    if (lay.kind[l.k] == LineModel.KEdgeP) hi = l.lat - lay.half;
                }
                if (float.IsNaN(lo) || float.IsNaN(hi))
                {
                    LineModel.Extents(e, sA, out float eM, out float eP);
                    lo = -eM + e.shr; hi = eP - e.shl;
                }
                lo += 0.15f; hi -= 0.15f;
                const float baseW = 0.45f, toothGap = 0.15f;
                int nT = Mathf.FloorToInt((hi - lo + toothGap) / (baseW + toothGap));
                if (nT < 2) { MarkStats.yieldsSkipped++; continue; }
                float start = 0.5f * (lo + hi) - 0.5f * (nT * baseW + (nT - 1) * toothGap);
                var pieces = new List<Vector2[]>(nT);
                for (int i = 0; i < nT; i++)
                {
                    float x0 = start + i * (baseW + toothGap);
                    // the base at the junction side, the point toward the driver
                    pieces.Add(new[] { new Vector2(x0, sB), new Vector2(x0 + baseW, sB), new Vector2(x0 + 0.5f * baseW, sA) });
                }
                var ty = e.TangentAt(sB);
                AddMark(new PaintMark { style = MkGlyph, edge = e.index, s0 = sA, s1 = sB, pieces = pieces, bps = new[] { sA, sB }, what = tagged ? "yield (tagged)" : "yield",
                                        hdg = Mathf.Repeat(Mathf.Atan2(ty.x, ty.y) * Mathf.Rad2Deg, 360f) });
                MarkStats.yields++;
                if (tagged) MarkStats.yieldsTagged++;
            }
        }

        static float DistTo(CityMap.Edge H, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < H.pts.Length; i++)
            {
                Vector2 a = H.pts[i], d = H.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float u = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                best = Mathf.Min(best, (p - (a + d * u)).magnitude);
            }
            return best;
        }

        // ================================================================
        //  The builder's side
        // ================================================================

        /// <summary>After SamplePositions' dedupe, before the zones': a
        /// section where each line mark starts and ends, so the span that
        /// changes a line changes it exactly there.</summary>
        static void MarkForcedSamples(CityMap map, Trims trims, CityMap.Edge e, float sMin, float sMax)
        {
            if (!MarksOn) return;
            EnsureMarks(map, trims);
            var l = MarksOf(e.index);
            if (l == null) return;
            bool added = false;
            foreach (var m in l)
            {
                if (m.style == MkGlyph) continue;
                for (int q = 0; q < 2; q++)
                {
                    float s = q == 0 ? m.s0 : m.s1;
                    if (s <= sMin + 0.3f || s >= sMax - 0.3f) continue;
                    bool close = false;
                    foreach (float v in sampleS) if (Mathf.Abs(v - s) < 0.3f) { close = true; break; }
                    if (close) continue;
                    sampleS.Add(s); added = true;
                }
            }
            if (added) sampleS.Sort();
        }

        static readonly List<PaintMark> spanMarks = new List<PaintMark>(8);

        /// <summary>The marks a span draws: line marks whose range holds its
        /// middle, glyphs that overlap it.</summary>
        static bool SpanMarks(CityMap.Edge e, float sA, float sB)
        {
            spanMarks.Clear();
            if (!MarksOn) return false;
            var l = MarksOf(e.index);
            if (l == null) return false;
            float mid = 0.5f * (sA + sB);
            foreach (var m in l)
            {
                if (m.style == MkGlyph) { if (m.s1 > sA + 1e-4f && m.s0 < sB - 1e-4f) spanMarks.Add(m); }
                else if (mid >= m.s0 && mid <= m.s1) spanMarks.Add(m);
            }
            return spanMarks.Count > 0;
        }

        static byte SpanOverride(int k)
        {
            foreach (var m in spanMarks) if (m.k == k && m.style <= MkDW) return m.style;
            return 255;
        }

        const float CHHalf = 0.10f, DWHalf = 0.10f, DWPeriod = 3.6576f, DWDash = 0.9144f;

        /// <summary>A column of a marked span: laterals at A and B; kind 0 a
        /// texture line (uL..uR), 1 a texture line cut into 10 ft dashes, 2
        /// solid white, 3 wide dotted white.</summary>
        struct MCol { public float loA, hiA, loB, hiB, uL, uR; public byte kind; public int k; public bool extra; }
        static readonly List<MCol> mcols = new List<MCol>(24);
        static readonly List<LineModel.LineAt> rawA = new List<LineModel.LineAt>(16), rawB = new List<LineModel.LineAt>(16);
        static readonly List<float> subT = new List<float>(32);
        static readonly List<(float la0, float la1, float lb0, float lb1)> ivs = new List<(float, float, float, float)>(16);

        static bool FindLat(List<LineModel.LineAt> ls, int k, out float lat)
        {
            foreach (var l in ls) if (l.k == k) { lat = l.lat; return true; }
            lat = 0f; return false;
        }

        static Vector3 ColAt(in Section c, float lL, float lR, float lat, float aS, Vector2 along)
        {
            float t = lR - lL > 1e-4f ? Mathf.Clamp01((lat - lL) / (lR - lL)) : 0.5f;
            if (c.zoneEnd) return Vector3.Lerp(c.L, c.R, t);
            var q = c.P + c.right * lat + (c.s <= aS ? -along : along);
            float y = Mathf.Lerp(c.L.y, c.R.y, t);
            return new Vector3(q.x - tileOrigin.x, y, q.y - tileOrigin.z);
        }

        /// <summary>
        /// A span with marks on it, drawn as columns (the caller has its model
        /// lines in <see cref="spanLines"/>): the model's lines (a mark may
        /// remove one, make it solid, white CH or wide dotted), the marks' own
        /// lines, and the pavement between - subdivided where a glyph lies in
        /// it. Returns the quads drawn.
        /// </summary>
        static int EmitMarked(Bucket bk, CityMap.Edge e, in Section A, in Section B, float v0, float v1,
                              LineModel.Layout lay, LineModel.Layout tex, float latLA, float latRA, float latLB, float latRB)
        {
            float tw = tex.texW, PH = lay.half;
            int jw = LineOfKind(tex, LineModel.KEdgeM);
            if (jw < 0) jw = 0;
            float wHi = (tex.x1[jw] + 0.5f) / tw, wLo = (tex.x0[jw] + 0.5f) / tw;
            mcols.Clear();
            void Add(float la0, float la1, float lb0, float lb1, byte kind, float uL, float uR, int k, bool extra) =>
                mcols.Add(new MCol { loA = la0, hiA = la1, loB = lb0, hiB = lb1, kind = kind, uL = uL, uR = uR, k = k, extra = extra });
            foreach (var sl in spanLines)
            {
                byte ov = SpanOverride(sl.k);
                if (ov == MkOff || ov == MkCH) continue;
                if (ov == MkLS) { Add(sl.latA - PH, sl.latA + PH, sl.latB - PH, sl.latB + PH, 2, wHi, wLo, sl.k, false); continue; }
                if (ov == MkDW) { Add(sl.latA - DWHalf, sl.latA + DWHalf, sl.latB - DWHalf, sl.latB + DWHalf, 3, wHi, wLo, sl.k, false); continue; }
                float uHi = (lay.srcM[sl.k] + PH) / lay.W, uLo = (lay.srcM[sl.k] - PH) / lay.W;
                Add(sl.latA - PH, sl.latA + PH, sl.latB - PH, sl.latB + PH, (byte)(lay.synth[sl.k] ? 1 : 0), uHi, uLo, sl.k, false);
            }
            bool raw = false;
            bool glyphs = false;
            foreach (var m in spanMarks)
            {
                if (m.style == MkCH)
                {
                    if (!raw) { LineModel.LinesAt(e, A.s, rawA); LineModel.LinesAt(e, B.s, rawB); raw = true; }
                    if (!FindLat(rawA, m.k, out float la) || !FindLat(rawB, m.k, out float lb)) continue;
                    Add(la - CHHalf, la + CHHalf, lb - CHHalf, lb + CHHalf, 2, wHi, wLo, m.k, false);
                }
                else if (m.style == MkAuxDW)
                {
                    float la = AuxThroughLat(e, A.s, m.side), lb = AuxThroughLat(e, B.s, m.side);
                    if (float.IsNaN(la) || float.IsNaN(lb)) continue;
                    Add(la - DWHalf, la + DWHalf, lb - DWHalf, lb + DWHalf, 3, wHi, wLo, -1, true);
                }
                else if (m.style == MkGoreCH)
                {
                    float la = GoreLat(m, e, A.s), lb = GoreLat(m, e, B.s);
                    // never inside the through lanes: where N was nudged toward the
                    // node the ramp's lane edge still lies a little inside the host's
                    // line - the two are one line there, the V's point
                    foreach (var h in spanMarks)
                    {
                        if (h.style != MkCH || h.zone != m.zone || h.k < 0) continue;
                        if (!raw) { LineModel.LinesAt(e, A.s, rawA); LineModel.LinesAt(e, B.s, rawB); raw = true; }
                        if (FindLat(rawA, h.k, out float ha) && FindLat(rawB, h.k, out float hb))
                        { la = GoreDrawn(m, e, A.s, ha); lb = GoreDrawn(m, e, B.s, hb); }
                        break;
                    }
                    Add(la - CHHalf, la + CHHalf, lb - CHHalf, lb + CHHalf, 2, wHi, wLo, -1, true);
                }
                else if (m.style == MkGlyph) glyphs = true;
            }
            // inside the drawn ribbon only
            int w = 0;
            for (int i = 0; i < mcols.Count; i++)
            {
                var c = mcols[i];
                c.loA = Mathf.Max(c.loA, latLA); c.hiA = Mathf.Min(c.hiA, latRA);
                c.loB = Mathf.Max(c.loB, latLB); c.hiB = Mathf.Min(c.hiB, latRB);
                if (c.hiA - c.loA < 0.005f && c.hiB - c.loB < 0.005f) continue;
                if (c.hiA < c.loA) c.hiA = c.loA;
                if (c.hiB < c.loB) c.hiB = c.loB;
                mcols[w++] = c;
            }
            mcols.RemoveRange(w, mcols.Count - w);
            mcols.Sort((x, y) => (x.loA + x.hiA + x.loB + x.hiB).CompareTo(y.loA + y.hiA + y.loB + y.hiB));
            w = 0;
            for (int i = 0; i < mcols.Count; i++)
            {
                var c = mcols[i];
                if (w > 0)
                {
                    var p = mcols[w - 1];
                    if (c.loA < p.hiA - 0.002f || c.loB < p.hiB - 0.002f)
                    {
                        if (p.kind == 2 && c.kind == 2)
                        {
                            p.loA = Mathf.Min(p.loA, c.loA); p.loB = Mathf.Min(p.loB, c.loB);
                            p.hiA = Mathf.Max(p.hiA, c.hiA); p.hiB = Mathf.Max(p.hiB, c.hiB);
                            mcols[w - 1] = p;
                        }
                        else if (p.extra && !c.extra) mcols[w - 1] = c;
                        continue;
                    }
                }
                mcols[w++] = c;
            }
            mcols.RemoveRange(w, mcols.Count - w);

            var along = B.P - A.P;
            along = along.sqrMagnitude > 1e-8f ? along.normalized * ColumnOverlapM : Vector2.zero;
            float plus = PlusOf(e);
            float pA = plus + LineModel.ShiftAt(e, A.s), pB = plus + LineModel.ShiftAt(e, B.s);
            int quads = 0;
            float cA = latLA, cB = latLB;
            for (int i = 0; i <= mcols.Count; i++)
            {
                bool last = i == mcols.Count;
                float nA = last ? latRA : mcols[i].loA, nB = last ? latRB : mcols[i].loB;
                if (nA - cA > 1e-3f || nB - cB > 1e-3f)
                    quads += Pave(bk, e, A, B, latLA, latRA, latLB, latRB, cA, cB, Mathf.Max(cA, nA), Mathf.Max(cB, nB), v0, v1, tex, pA, pB, along, glyphs);
                if (last) break;
                var c = mcols[i];
                switch (c.kind)
                {
                    case 1:
                    {
                        float pL = (tex.bandHi + 0.75f) / tw, pR = (tex.bandLo + 0.25f) / tw;
                        quads += StripDashed(bk, A, B, latLA, latRA, latLB, latRB, c.loA, c.loB, c.hiA, c.hiB, c.uL, c.uR, pL, pR, v0, v1);
                        break;
                    }
                    case 3:
                        quads += Dotted(bk, A, B, latLA, latRA, latLB, latRB, c.loA, c.loB, c.hiA, c.hiB, wHi, wLo,
                                        (tex.bandHi + 0.75f) / tw, (tex.bandLo + 0.25f) / tw, v0, v1, along);
                        break;
                    default:
                        Strip(bk, A, B, latLA, latRA, latLB, latRB, c.loA, c.loB, c.hiA, c.hiB, c.uL, c.uR, v0, v1);
                        quads++;
                        break;
                }
                cA = Mathf.Max(cA, c.hiA); cB = Mathf.Max(cB, c.hiB);
            }
            RibbonColumnSpans++; RibbonStrips += quads;
            return quads;
        }

        /// <summary>A wide dotted white column: 3 ft of paint in every 12 ft
        /// of chain distance, pavement between.</summary>
        static int Dotted(Bucket bk, in Section A, in Section B, float lLA, float lRA, float lLB, float lRB,
                          float aL, float bL, float aR, float bR, float uLineL, float uLineR, float uPaveL, float uPaveR, float v0, float v1, Vector2 along)
        {
            float aS = A.s;
            Vector3 qAL = ColAt(A, lLA, lRA, aL, aS, along), qBL = ColAt(B, lLB, lRB, bL, aS, along),
                    qBR = ColAt(B, lLB, lRB, bR, aS, along), qAR = ColAt(A, lLA, lRA, aR, aS, along);
            float dA = v0 * RoadVTile, dB = v1 * RoadVTile;
            float lo = Mathf.Min(dA, dB), hi = Mathf.Max(dA, dB);
            breaks.Clear(); breaks.Add(0f);
            if (hi - lo > 1e-4f)
                for (float k = Mathf.Floor(lo / DWPeriod); k * DWPeriod <= hi; k += 1f)
                    for (int q = 0; q < 2; q++)
                    {
                        float d = k * DWPeriod + (q == 0 ? 0f : DWDash);
                        if (d > lo + 1e-4f && d < hi - 1e-4f) breaks.Add((d - dA) / (dB - dA));
                    }
            breaks.Add(1f);
            breaks.Sort();
            int quads = 0;
            for (int i = 0; i + 1 < breaks.Count; i++)
            {
                float ta = breaks[i], tb = breaks[i + 1];
                if (tb - ta < 1e-5f) continue;
                float dm = Mathf.Lerp(dA, dB, 0.5f * (ta + tb));
                float ph = dm / DWPeriod - Mathf.Floor(dm / DWPeriod);
                bool dash = ph < DWDash / DWPeriod;
                float uL = dash ? uLineL : uPaveL, uR = dash ? uLineR : uPaveR;
                float va = Mathf.Lerp(v0, v1, ta), vb = Mathf.Lerp(v0, v1, tb);
                bk.Quad(Vector3.Lerp(qAL, qBL, ta), Vector3.Lerp(qAL, qBL, tb), Vector3.Lerp(qAR, qBR, tb), Vector3.Lerp(qAR, qBR, ta),
                    new Vector2(uL, va), new Vector2(uL, vb), new Vector2(uR, vb), new Vector2(uR, va));
                quads++;
            }
            return quads;
        }

        /// <summary>Pavement from (cA, cB) to (nA, nB): the texture's own
        /// texels where they are paint-free there (u = (plus - lat) / W, the
        /// full quad's mapping), else its widest paint-free run; cut around the
        /// glyph pieces that lie in it.</summary>
        static int Pave(Bucket bk, CityMap.Edge e, in Section A, in Section B, float lLA, float lRA, float lLB, float lRB,
                        float cA, float cB, float nA, float nB, float v0, float v1, LineModel.Layout tex, float pA, float pB, Vector2 along, bool glyphs)
        {
            float W = tex.W, tw = tex.texW;
            bool ident = pA - nA >= -1e-3f && pA - cA <= W + 1e-3f && pB - nB >= -1e-3f && pB - cB <= W + 1e-3f
                         && PaintFree(tex, pA - nA, pA - cA) && PaintFree(tex, pB - nB, pB - cB);
            float bL = (tex.bandHi + 0.75f) / tw, bR = (tex.bandLo + 0.25f) / tw;
            float aS = A.s;
            Vector3 qAL = ColAt(A, lLA, lRA, cA, aS, along), qBL = ColAt(B, lLB, lRB, cB, aS, along),
                    qBR = ColAt(B, lLB, lRB, nB, aS, along), qAR = ColAt(A, lLA, lRA, nA, aS, along);
            // the point at (t, lat) in this column, and its pavement u
            Vector3 P(float t, float lat, out float u)
            {
                float lo = Mathf.Lerp(cA, cB, t), hi = Mathf.Lerp(nA, nB, t);
                float f = hi - lo > 1e-4f ? Mathf.Clamp01((lat - lo) / (hi - lo)) : 0.5f;
                u = ident ? (Mathf.Lerp(pA, pB, t) - Mathf.Lerp(lo, hi, f)) / W : Mathf.Lerp(bL, bR, f);
                return Vector3.Lerp(Vector3.Lerp(qAL, qBL, t), Vector3.Lerp(qAR, qBR, t), f);
            }
            int quads = 0;
            // the glyph pieces in this column
            subT.Clear();
            bool hit = false;
            if (glyphs)
            {
                float lo = Mathf.Min(cA, cB), hi = Mathf.Max(nA, nB);
                foreach (var m in spanMarks)
                {
                    if (m.style != MkGlyph) continue;
                    bool mine = false;
                    foreach (var pc in m.pieces)
                    {
                        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                        foreach (var v in pc) { x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y); }
                        if (x1 <= lo || x0 >= hi || y1 <= A.s || y0 >= B.s) continue;
                        mine = true; break;
                    }
                    if (!mine) continue;
                    hit = true;
                    foreach (float b in m.bps)
                        if (b > A.s + 1e-4f && b < B.s - 1e-4f) subT.Add((b - A.s) / (B.s - A.s));
                }
            }
            if (!hit)
            {
                Vector3 a = P(0f, cA, out float ua), b = P(1f, cB, out float ub), c = P(1f, nB, out float uc), d = P(0f, nA, out float ud);
                bk.Quad(a, b, c, d, new Vector2(ua, v0), new Vector2(ub, v1), new Vector2(uc, v1), new Vector2(ud, v0));
                return 1;
            }
            subT.Add(0f); subT.Add(1f);
            subT.Sort();
            float wU = 0f;
            {
                int jw = LineOfKind(tex, LineModel.KEdgeM);
                if (jw < 0) jw = 0;
                wU = (tex.x0[jw] + tex.x1[jw] + 1f) * 0.5f / tw;
            }
            for (int i = 0; i + 1 < subT.Count; i++)
            {
                float ta = subT[i], tb = subT[i + 1];
                if (tb - ta < 1e-5f) continue;
                float sa = Mathf.Lerp(A.s, B.s, ta), sb = Mathf.Lerp(A.s, B.s, tb), sm = 0.5f * (sa + sb);
                float loA = Mathf.Lerp(cA, cB, ta), hiA = Mathf.Lerp(nA, nB, ta), loB = Mathf.Lerp(cA, cB, tb), hiB = Mathf.Lerp(nA, nB, tb);
                float va = Mathf.Lerp(v0, v1, ta), vb = Mathf.Lerp(v0, v1, tb);
                ivs.Clear();
                foreach (var m in spanMarks)
                {
                    if (m.style != MkGlyph) continue;
                    foreach (var pc in m.pieces)
                    {
                        float y0 = float.MaxValue, y1 = float.MinValue;
                        foreach (var v in pc) { y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y); }
                        if (sm < y0 || sm > y1) continue;
                        if (!Cross(pc, sa, out float a0, out float a1) || !Cross(pc, sb, out float b0, out float b1)) continue;
                        a0 = Mathf.Max(a0, loA); a1 = Mathf.Min(a1, hiA); b0 = Mathf.Max(b0, loB); b1 = Mathf.Min(b1, hiB);
                        if (a1 - a0 < 1e-3f && b1 - b0 < 1e-3f) continue;
                        if (a1 < a0) a1 = a0;
                        if (b1 < b0) b1 = b0;
                        ivs.Add((a0, a1, b0, b1));
                    }
                }
                ivs.Sort((x, y) => (x.la0 + x.lb0).CompareTo(y.la0 + y.lb0));
                // one band where pieces touch or overlap
                int w = 0;
                for (int j = 0; j < ivs.Count; j++)
                {
                    var c = ivs[j];
                    if (w > 0)
                    {
                        var p = ivs[w - 1];
                        if (c.la0 <= p.la1 + 2e-3f && c.lb0 <= p.lb1 + 2e-3f)
                        { ivs[w - 1] = (Mathf.Min(p.la0, c.la0), Mathf.Max(p.la1, c.la1), Mathf.Min(p.lb0, c.lb0), Mathf.Max(p.lb1, c.lb1)); continue; }
                        if (c.la0 < p.la1 - 2e-3f || c.lb0 < p.lb1 - 2e-3f) continue;   // crossing pieces: the first one wins
                    }
                    ivs[w++] = c;
                }
                ivs.RemoveRange(w, ivs.Count - w);
                float ca = loA, cb = loB;
                for (int j = 0; j <= ivs.Count; j++)
                {
                    bool end = j == ivs.Count;
                    float na = end ? hiA : ivs[j].la0, nb = end ? hiB : ivs[j].lb0;
                    if (na - ca > 1e-3f || nb - cb > 1e-3f)
                    {
                        Vector3 a = P(ta, ca, out float ua), b = P(tb, cb, out float ub), c = P(tb, nb, out float uc), d = P(ta, na, out float ud);
                        bk.Quad(a, b, c, d, new Vector2(ua, va), new Vector2(ub, vb), new Vector2(uc, vb), new Vector2(ud, va));
                        quads++;
                    }
                    if (end) break;
                    var iv = ivs[j];
                    {
                        Vector3 a = P(ta, iv.la0, out _), b = P(tb, iv.lb0, out _), c = P(tb, iv.lb1, out _), d = P(ta, iv.la1, out _);
                        bk.Quad(a, b, c, d, new Vector2(wU, va), new Vector2(wU, vb), new Vector2(wU, vb), new Vector2(wU, va));
                        quads++;
                    }
                    ca = iv.la1; cb = iv.lb1;
                }
            }
            return quads;
        }

        /// <summary>A convex polygon's cross-section at s (x = lateral, y = s).</summary>
        public static bool Cross(Vector2[] poly, float s, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                if (Mathf.Abs(a.y - b.y) < 1e-5f)
                {
                    if (Mathf.Abs(s - a.y) < 1e-3f) { lo = Mathf.Min(lo, Mathf.Min(a.x, b.x)); hi = Mathf.Max(hi, Mathf.Max(a.x, b.x)); }
                    continue;
                }
                if (s < Mathf.Min(a.y, b.y) - 1e-3f || s > Mathf.Max(a.y, b.y) + 1e-3f) continue;
                float t = Mathf.Clamp01((s - a.y) / (b.y - a.y));
                float x = a.x + (b.x - a.x) * t;
                lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
            }
            return hi >= lo;
        }
    }
}
