using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// MERGE ZONES (roads pass L5, 2026-10-03; plan A7 + A9, owner Q2;
    /// Docs/CHARLOTTE.md "L5").
    ///
    /// Before: a ramp was never one pavement with its mainline. Its ribbon
    /// was clipped to the mainline's edge and narrowed to a zero-width sliver
    /// tens of metres before OSM's merge node, while the mainline did not
    /// widen at all - the merging lane rode the shoulder (diag/merges: freeway
    /// pinch p50 53 m), and where OSM adds the lane at the node it faded in
    /// from zero over 90 m.
    ///
    /// Now, for every one-way branch (a ramp, or a one-way fork at 35 degrees
    /// or less) of a tier-1 host at a mitred node:
    ///   N        THE NOSE: walking the ramp away from its node, the first
    ///            point where its inner LANE edge meets the host's through-lane
    ///            edge (gap + both shoulders = 0; bisection to a few cm).
    ///   CUT      the ramp's ribbon ENDS at N, its last section laid on the
    ///            host's cross-line there: inner vertex on the host's own edge,
    ///            outer vertex on the widened edge - the same points the host's
    ///            section at N has (shared vertices). Nothing of the ramp is
    ///            drawn between N and the node (no sliver, no seam strip).
    ///   AUX      the host WIDENS on the ramp's side from N (an aux ease,
    ///            <see cref="LineModel.Ease.aux"/>): k lanes (style G: the lanes
    ///            OSM adds or drops at the node, whose mouth ease no longer
    ///            narrows the pavement; style T: the ramp's lanes), full width
    ///            through the node, the shoulder eased from the ramp's to the
    ///            host's at 20:1.
    ///   Q2       (owner: "EXTEND TO DOT LENGTH") where OSM leaves less than
    ///            AASHTO's acceleration (merge) or deceleration (diverge)
    ///            length of full-width lane from N (Green Book tables 10-3 and
    ///            10-5 by the host's and the ramp's design speeds), the lane
    ///            runs on at full width past the node to that length, then the
    ///            taper (merge: 90 m freeway / ramp host, else MUTCD W S; exit:
    ///            75 m / 30 m), new pavement on the ramp's side only. Clamped
    ///            at a junction (fan), a deck or tunnel, a lane change on that
    ///            side; run on at full width into the next exit on the same
    ///            side (an auxiliary lane between interchanges).
    ///   LANES    traffic, the AI and the race line on a ramp between N and
    ///            the node drive the host's aux lane (<see cref="ZoneLanePoint"/>),
    ///            moving over to the host's lanes by the node.
    /// PSX_CITY_MERGEZONES=0 turns it all off (L4's drawing).
    /// </summary>
    public static partial class CityMeshes
    {
        public sealed class MergeZone
        {
            public int node, branch, host, other;
            /// <summary>The ramp arrives at the node (a merge) or leaves it (a diverge).</summary>
            public bool merge;
            /// <summary>'G': OSM adds/drops k lanes at the node; 'T': it does not.</summary>
            public char style;
            /// <summary>k lanes carried; side: the zone side in the zone-arm
            /// chain's frame (+1 its left), sideOther in the other arm's.</summary>
            public int k, side, sideOther;
            public float theta, D, wN, target, lReq, lt, ltRule, xf, need, avail, ls, vHost, vRamp, dataAux, seatDy;
            public bool sideMismatch, backToBack, loop;
            public string clamp;
            /// <summary>The ramp's last section, on the host's cross-line at N
            /// (world plan), and the host edge and arc whose height it takes.</summary>
            public Vector2 endInner, endOuter, nZ;
            public int endHost; public float endHostS;
            public int cutEdge; public float sCut; public int innerSide; public float margin;
            internal Chain hostChain, otherChain;

            /// <summary>The host edge, its arc and the zone side in its own
            /// frame at zone distance x (+ the zone arm, - the other arm).</summary>
            public bool HostAt(CityMap map, float x, out CityMap.Edge H, out float s, out int sideH)
            {
                H = null; s = 0f; sideH = 0;
                var ch = x >= 0f ? hostChain : otherChain;
                int sd = x >= 0f ? side : sideOther;
                if (ch == null || !ch.Walk(Mathf.Abs(x), out H, out s, out _, out var dir)) return false;
                var t = H.TangentAt(s);
                sideH = Vector2.Dot(new Vector2(-dir.y, dir.x), new Vector2(-t.y, t.x)) >= 0f ? sd : -sd;
                return true;
            }
            /// <summary>The aux profile (the same numbers every host piece's ease carries).</summary>
            public LineModel.Ease proto;
            public readonly List<ZoneCut> cuts = new List<ZoneCut>(2);
            internal readonly List<long> pairs = new List<long>(8);
            internal readonly List<int> auxEdges = new List<int>(4);
            public int id; public long gTag;
            /// <summary>The ramp's lanes' width at N (its own lane widths).</summary>
            public float brLaneW;
        }

        /// <summary>One branch piece's cut: the arc range not drawn; on the
        /// piece holding N, its end section's arc and the side the node is
        /// on (+1 the b end, -1 the a end).</summary>
        public struct ZoneCut { public int edge; public float s0, s1; public bool end; public float sEnd; public int nodeDir, innerSide; public MergeZone z; }

        public static bool MergeZonesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_MERGEZONES") != "0";
        /// <summary>PSX_CITY_ZONELANES=0: the drawing as L5, the lane paths as L4 (A9 off).</summary>
        public static bool ZoneLanesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_ZONELANES") != "0";
        public static readonly List<MergeZone> Zones = new List<MergeZone>();
        public static readonly SortedDictionary<string, int> ZoneRejects = new SortedDictionary<string, int>();
        static readonly Dictionary<int, List<ZoneCut>> zoneCuts = new Dictionary<int, List<ZoneCut>>();
        /// <summary>A zone's host pieces and ramp pieces: host and branch for
        /// the squeeze whether or not a tile has clipped them yet (clipPairs
        /// is a tile build's own).</summary>
        static readonly HashSet<long> zonePairs = new HashSet<long>();
        const float ZoneMaxDeg = 35f;
        /// <summary>The first few branch ends each reason turned away (e/node).</summary>
        public static readonly Dictionary<string, string> ZoneRejectAt = new Dictionary<string, string>();
        static int rejectEdge, rejectNode;
        static void ZoneReject(string why)
        {
            if (!countRejects) return;
            ZoneRejects.TryGetValue(why, out int n); ZoneRejects[why] = n + 1;
            if (n < 3) { ZoneRejectAt.TryGetValue(why, out var at); ZoneRejectAt[why] = (at == null ? "" : at + " ") + $"e{rejectEdge}/n{rejectNode}"; }
        }

        /// <summary>The zone of a ramp's end at a node, or null.</summary>
        static MergeZone ZoneAt(int branch, int node)
        {
            if (Zones.Count == 0 || !zoneCuts.ContainsKey(branch)) return null;
            foreach (var c in zoneCuts[branch]) if (c.z.branch == branch && c.z.node == node) return c.z;
            return null;
        }

        /// <summary>The zone whose cut a ramp's arc lies in (not drawn), or null.</summary>
        static MergeZone ZoneCutOwner(CityMap.Edge e, float s)
        {
            if (zoneCuts.Count == 0 || !zoneCuts.TryGetValue(e.index, out var cl)) return null;
            foreach (var c in cl)
                if (c.end ? (c.nodeDir > 0 ? s > c.sEnd : s < c.sEnd) : (s >= c.s0 && s <= c.s1)) return c.z;
            return null;
        }

        /// <summary>The cuts on a branch edge (null: none).</summary>
        public static List<ZoneCut> ZoneCutsOf(int edge) => zoneCuts.TryGetValue(edge, out var l) ? l : null;

        // ----------------------------------------------------------------
        //  AASHTO Green Book (2011) exhibits 10-70 / 10-73: acceleration and
        //  deceleration lengths (ft) by highway design speed V (rows 30..75
        //  mph) and ramp curve speed V' (columns stop, 15, 20 ... 50 mph);
        //  -1 = no entry (V' near V: interpolated to 0 at V' = V).
        // ----------------------------------------------------------------
        static readonly float[] TabVp = { 0f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f };
        static readonly float[,] AccelFt =
        {
            { 180, 140,  -1,  -1,  -1,  -1,  -1,  -1,  -1 },
            { 280, 220, 160,  -1,  -1,  -1,  -1,  -1,  -1 },
            { 360, 300, 270, 210, 120,  -1,  -1,  -1,  -1 },
            { 560, 490, 440, 380, 280, 160,  -1,  -1,  -1 },
            { 720, 660, 610, 550, 450, 350, 130,  -1,  -1 },
            { 960, 900, 810, 780, 670, 550, 320, 150,  -1 },
            { 1200, 1140, 1100, 1020, 910, 800, 550, 420, 180 },
            { 1410, 1350, 1310, 1220, 1120, 1000, 770, 600, 370 },
            { 1620, 1560, 1520, 1420, 1350, 1230, 1000, 820, 580 },
            { 1790, 1730, 1630, 1580, 1510, 1420, 1160, 1040, 780 },
        };
        static readonly float[,] DecelFt =
        {
            { 235, 200, 170, 140,  -1,  -1,  -1,  -1,  -1 },
            { 280, 250, 210, 185, 150,  -1,  -1,  -1,  -1 },
            { 320, 295, 265, 235, 185, 155,  -1,  -1,  -1 },
            { 385, 355, 315, 285, 225, 175,  -1,  -1,  -1 },
            { 435, 405, 385, 355, 315, 285, 225,  -1,  -1 },
            { 480, 455, 440, 410, 380, 350, 285, 235,  -1 },
            { 530, 500, 480, 460, 430, 405, 350, 300, 240 },
            { 570, 540, 520, 500, 470, 440, 390, 340, 275 },
            { 615, 590, 570, 550, 520, 490, 440, 390, 330 },
            { 660, 635, 620, 600, 575, 535, 490, 440, 385 },
        };
        static float TabRow(float[,] t, int r, float vp)
        {
            float rowV = 30f + 5f * r;
            if (vp >= rowV) return 0f;
            int last = 0;
            for (int c = 0; c < TabVp.Length; c++) if (t[r, c] >= 0f) last = c;
            if (vp >= TabVp[last])
            {
                float span = rowV - TabVp[last];
                return span > 1e-3f ? t[r, last] * (1f - (vp - TabVp[last]) / span) : 0f;
            }
            for (int c = 0; c + 1 <= last; c++)
                if (vp <= TabVp[c + 1]) return Mathf.Lerp(t[r, c], t[r, c + 1], (vp - TabVp[c]) / (TabVp[c + 1] - TabVp[c]));
            return t[r, last];
        }
        /// <summary>The table's length in metres at host V, ramp V' (mph).</summary>
        public static float AashtoM(bool accel, float v, float vp)
        {
            var t = accel ? AccelFt : DecelFt;
            float fr = Mathf.Clamp((v - 30f) / 5f, 0f, 9f);
            int r0 = Mathf.FloorToInt(fr), r1 = Mathf.Min(9, r0 + 1);
            float ft = Mathf.Lerp(TabRow(t, r0, vp), TabRow(t, r1, vp), fr - r0);
            return Mathf.Max(0f, ft) * 0.3048f;
        }
        /// <summary>Design speed (mph), owner_decisions.md: motorway 65, trunk
        /// and primary 50, secondary/tertiary 40, local 30, ramps 40; a posted
        /// speed above it wins.</summary>
        static float DesignMph(CityMap.Edge e)
        {
            float v = e.link ? 40f : e.cls >= 5 ? 65f : e.cls >= 3 ? 50f : e.cls >= 1 ? 40f : 30f;
            if (e.speedKmh > 0) v = Mathf.Max(v, e.speedKmh / 1.609344f);
            return v;
        }
        static bool FreewayLike(CityMap.Edge e) => e.cls >= 5 || (e.cls == 4 && e.oneway && !e.link) || e.link;

        /// <summary>A road walked away from a node through its MITRED through
        /// joins only (the pieces an ease can run on through).</summary>
        static Chain MitredChain(CityMap map, Trims t, CityMap.Edge first, int node, float reach, List<int> nearNodes)
        {
            var ch = new Chain();
            var cur = first; int at = node; float len = 0f;
            nearNodes?.Clear();
            for (int k = 0; k < 24; k++)
            {
                ch.edges.Add(cur); nearNodes?.Add(at);
                bool fromA = cur.a == at;
                int n = cur.pts.Length;
                for (int i = 0; i < n; i++)
                {
                    int pi = fromA ? i : n - 1 - i;
                    var p = cur.pts[pi];
                    if (i == 0) { if (ch.pts.Count == 0) { ch.pts.Add(p); ch.cum.Add(0f); } continue; }
                    int prevPi = fromA ? pi - 1 : pi + 1;
                    ch.seg.Add(cur); ch.sA.Add(cur.s[prevPi]); ch.sB.Add(cur.s[pi]);
                    ch.cum.Add(ch.cum[ch.cum.Count - 1] + Vector2.Distance(ch.pts[ch.pts.Count - 1], p));
                    ch.pts.Add(p);
                }
                len += cur.length;
                if (len >= reach) break;
                int far = fromA ? cur.b : cur.a;
                if (!t.mitre[far]) break;
                int oi = t.throughA[far] == cur.index ? t.throughB[far] : t.throughB[far] == cur.index ? t.throughA[far] : -1;
                if (oi < 0 || oi == cur.index) break;
                var nx = map.edges[oi];
                if (nx.a == nx.b || ch.edges.Contains(nx)) break;
                cur = nx; at = far;
            }
            return ch;
        }

        static float ShoulderToward(CityMap.Edge E, float sE, Vector2 r, float sign)
        {
            var t = E.TangentAt(sE);
            bool same = Vector2.Dot(r, new Vector2(-t.y, t.x)) >= 0f;
            return (sign > 0f) == same ? E.shl : E.shr;
        }

        /// <summary>The host's own pavement edge (no aux lane) on a side of a
        /// chain's left <paramref name="rM"/>, and its shoulder there.</summary>
        static float HostEdgeNoAux(CityMap.Edge H, float sM, Vector2 rM, int side, out float shoulder)
        {
            LineModel.ExtentsNoAux(H, sM, out float eM, out float eP);
            var tH = H.TangentAt(sM);
            bool same = Vector2.Dot(rM, new Vector2(-tH.y, tH.x)) >= 0f;
            int sideH = same ? side : -side;
            shoulder = sideH > 0 ? H.shl : H.shr;
            return sideH > 0 ? eP : eM;
        }

        struct ZoneSmp { public float g, off, arcIn; public int sideNow; public CityMap.Edge E, H; public float sE, sM; public Vector2 p, dirL, dirM; }

        /// <summary>The ramp at travel tt from its node against its host: g =
        /// its inner LANE edge past the host's through-lane edge (+ apart).</summary>
        static bool ZoneSample(Chain host, Chain br, float tt, ref int side, out ZoneSmp o)
        {
            o = default;
            if (!br.Walk(tt, out var E, out float sE, out var p, out var dirL)) return false;
            host.Project(p, out var H, out float sM, out var q, out var dirM, out bool atEnd, out float arc);
            if (atEnd) return false;
            var rM = new Vector2(-dirM.y, dirM.x);
            float off = Vector2.Dot(p - q, rM);
            int sideNow = off >= 0f ? 1 : -1;
            if (side == 0 && Mathf.Abs(off) > 0.4f) side = sideNow;
            o.E = E; o.sE = sE; o.p = p; o.dirL = dirL; o.H = H; o.sM = sM; o.dirM = dirM; o.off = off; o.sideNow = sideNow; o.arcIn = arc;
            if (side == 0) { o.g = -1f; return true; }
            var rL = new Vector2(-dirL.y, dirL.x);
            float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
            float inExt = ExtToward(E, sE, rL, lSign), inSh = ShoulderToward(E, sE, rL, lSign);
            var inLane = p + rL * (lSign * Mathf.Max(0f, inExt - inSh));
            float hExt = HostEdgeNoAux(H, sM, rM, side, out float hSh);
            o.g = Vector2.Dot(inLane - q, rM) * side - (hExt - hSh);
            host.Project(inLane, out _, out _, out _, out _, out _, out o.arcIn);
            return true;
        }

        /// <summary>The ramp's OUTER pavement vertex at travel tt (its own
        /// cross-line) and the host-chain arc it projects to.</summary>
        static bool OuterAt(Chain host, Chain br, float tt, int side, out Vector2 outer, out float arc, out CityMap.Edge E, out float sE)
        {
            outer = default; arc = 0f; sE = 0f;
            if (!br.Walk(tt, out E, out sE, out var p, out var dirL)) return false;
            host.Project(p, out _, out _, out _, out var dirM, out bool atEnd);
            if (atEnd) return false;
            var rM = new Vector2(-dirM.y, dirM.x);
            var rL = new Vector2(-dirL.y, dirL.x);
            float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
            outer = p + rL * (-lSign * ExtToward(E, sE, rL, -lSign));
            host.Project(outer, out _, out _, out _, out _, out bool atEnd2, out arc);
            return !atEnd2;
        }

        /// <summary>Once per map, after <see cref="LineModel.BuildEases"/>
        /// (ComputeTrims): find every zone, stand down the mouth eases its
        /// style G lane makes redundant, add the aux eases and the cuts.
        /// Plan geometry only (no heights): the elevation's own ComputeTrims
        /// and the tiles' see the same zones.</summary>
        static void BuildMergeZones(CityMap map, Trims t)
        {
            Zones.Clear(); zoneCuts.Clear(); ZoneRejects.Clear(); ZoneRejectAt.Clear(); zonePairs.Clear();
            if (!MergeZonesOn) return;
            var splitNodes = new HashSet<int>();
            if (map.splt != null) foreach (var sp in map.splt) splitNodes.Add(sp.node);
            int Partner(CityMap.Edge e, int node)
            {
                if (!t.mitre[node]) return -1;
                int o = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
                return o == e.index ? -1 : o;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cands = new List<MergeZone>();
            for (int ei = 0; ei < map.edges.Length; ei++)
                for (int end = 0; end < 2; end++)
                {
                    int hostIdx = end == 0 ? t.branchA[ei] : t.branchB[ei];
                    if (hostIdx < 0) continue;
                    var L = map.edges[ei];
                    if (L.a == L.b) continue;
                    int n = end == 0 ? L.a : L.b;
                    rejectEdge = ei; rejectNode = n;
                    var M = map.edges[hostIdx];
                    if (CityTier.Of(M) != 1) continue;   // tier 1 (plan A7); A14 (tier 2) is cut for lean
                    if (!L.oneway) { ZoneReject("two-way branch (a mouth)"); continue; }
                    if (!t.mitre[n] || splitNodes.Contains(n)) { ZoneReject("not a mitred node"); continue; }
                    int oi = Partner(M, n);
                    if (oi < 0) { ZoneReject("no through partner"); continue; }
                    var O = map.edges[oi];
                    bool merge = L.b == n;
                    if (M.oneway && (M.b == n) != merge) { ZoneReject("against the host's direction"); continue; }
                    var z = TryZone(map, t, L, M, O, n, merge);
                    if (z != null) cands.Add(z);
                }
            BuildMsFind = (float)sw.Elapsed.TotalMilliseconds;
            // style G: the lane OSM adds at the node is the zone's lane - its
            // mouth ease (narrow at the node) stands down for the pavement
            foreach (var z in cands)
            {
                if (z.style != 'G') continue;
                var O = map.edges[z.other];
                var tO = O.a == z.node ? O.TangentAt(0f) : O.TangentAt(O.length);
                int sideO = Vector2.Dot(new Vector2(-tO.y, tO.x), z.nZ) >= 0f ? 1 : -1;
                z.gTag = LineModel.EaseTag(z.node, O.index, sideO);
                LineModel.CentreOnly(map, z.gTag, true);
            }
            // ...which moves other zones' host edges: every nose whose host or
            // ramp that touched is placed again on the pavement as it now stands
            var moved = new HashSet<int>();
            foreach (var z in cands) if (z.gTag != 0) { var tl = LineModel.EdgesOfTag(z.gTag); if (tl != null) foreach (int ei in tl) moved.Add(ei); }
            countRejects = false;
            for (int i = 0; i < cands.Count; i++)
            {
                var z = cands[i];
                bool touched = false;
                foreach (var h in z.hostChain.edges) if (moved.Contains(h.index)) { touched = true; break; }
                if (!touched) foreach (var c in z.cuts) if (moved.Contains(c.edge)) { touched = true; break; }
                if (!touched) continue;
                var z2 = TryZone(map, t, map.edges[z.branch], map.edges[z.host], map.edges[z.other], z.node, z.merge);
                if (z2 == null || z2.style != z.style || z2.k != z.k)
                {
                    Unsuppress(map, z); cands[i] = null;
                    rejectEdge = z.branch; rejectNode = z.node;
                    countRejects = true; ZoneReject("unstable once the style G lanes stood"); countRejects = false;
                    continue;
                }
                z2.gTag = z.gTag;
                cands[i] = z2;
            }
            countRejects = true;
            cands.RemoveAll(z => z == null);
            BuildMsAgain = (float)sw.Elapsed.TotalMilliseconds - BuildMsFind;
            foreach (var z in cands)
            {
                var M = map.edges[z.host]; var O = map.edges[z.other];
                rejectEdge = z.branch; rejectNode = z.node;
                if (z.style == 'G')
                {
                    z.target = ExtAtNode(O, z.node, z.nZ) - ExtAtNode(M, z.node, z.nZ);
                    if (z.target < 0.5f * z.k * RoadProfiles.LaneM || z.target > z.k * RoadProfiles.LaneM + 2.5f)
                    { Unsuppress(map, z); ZoneReject("style G step off its lanes"); z.k = -1; continue; }
                    z.xf = 0f; z.lt = 0f; z.need = 0f;
                    // (the OSM aux run beyond the node: the report reads ZoneDataAux)
                }
                else z.target = z.k * RoadProfiles.LaneM;
                if (z.wN < 0.5f * z.target || z.wN > z.target + 4.5f) { Unsuppress(map, z); ZoneReject("outer edge off the lanes"); z.k = -1; continue; }
            }
            cands.RemoveAll(z => z.k < 0);
            // style T: the full-width length and the taper (Q2), clamped
            var diverges = new Dictionary<int, List<MergeZone>>();
            foreach (var z in cands) if (!z.merge) { if (!diverges.TryGetValue(z.node, out var dl)) diverges[z.node] = dl = new List<MergeZone>(1); dl.Add(z); }
            var byNode = new Dictionary<int, List<MergeZone>>();
            foreach (var z in cands) { if (!byNode.TryGetValue(z.node, out var bl)) byNode[z.node] = bl = new List<MergeZone>(1); bl.Add(z); }
            foreach (var z in cands)
                if (z.style == 'T') Extend(map, t, z, diverges, byNode);
            for (int i = 0; i < cands.Count; i++) { cands[i].id = i + 1; Apply(map, t, cands[i]); }
            // a ramp whose run beside its host (or its nose) lies under ANOTHER
            // zone's aux lane is not a zone: its ribbon is drawn and clipped to
            // that widened edge as before (no lane drawn twice)
            foreach (var z in cands)
            {
                bool hit = false;
                for (float x = z.D - 0.5f; x <= z.D + 30f && !hit; x += 2f)
                {
                    if (!z.HostAt(map, x, out var H, out float sH, out int sdH)) break;
                    var ez = H.lmEase;
                    if (ez == null) continue;
                    for (int q = 0; q < ez.Length && !hit; q++)
                        if (ez[q].aux && ez[q].auxZone != z.id && (ez[q].side > 0) == (sdH > 0) && LineModel.AuxAt(H, ez[q], sH) > 0.05f) hit = true;
                }
                if (hit) { z.k = -1; rejectEdge = z.branch; rejectNode = z.node; ZoneReject("beside another zone's aux lane"); }
            }
            foreach (var z in cands) if (z.k < 0) Drop(map, z);
            cands.RemoveAll(z => z.k < 0);
            zonePairs.Clear();
            foreach (var z in cands) foreach (var k in z.pairs) zonePairs.Add(k);
            Zones.AddRange(cands);
            BuildMs = (float)sw.Elapsed.TotalMilliseconds;
        }
        /// <summary>The last BuildMergeZones' time (ms).</summary>
        public static float BuildMs, BuildMsFind, BuildMsAgain;
        static bool countRejects = true;

        static void Unsuppress(CityMap map, MergeZone z)
        {
            if (z.gTag != 0) LineModel.CentreOnly(map, z.gTag, false);
        }

        /// <summary>Undo one applied zone: its aux eases, its cuts, its style G stand-down.</summary>
        static void Drop(CityMap map, MergeZone z)
        {
            Unsuppress(map, z);
            foreach (int ei in z.auxEdges)
            {
                var e = map.edges[ei];
                if (e.lmEase == null) continue;
                var keep = new List<LineModel.Ease>(e.lmEase.Length);
                foreach (var q in e.lmEase) if (!(q.aux && q.auxZone == z.id)) keep.Add(q);
                e.lmEase = keep.Count > 0 ? keep.ToArray() : null;
            }
            foreach (var c in z.cuts)
                if (zoneCuts.TryGetValue(c.edge, out var l)) { l.RemoveAll(q => q.z == z); if (l.Count == 0) zoneCuts.Remove(c.edge); }
        }

        static bool InUnion(CityMap map, CityMap.Edge e) =>
            map.deckUnionsOf != null && e.index < map.deckUnionsOf.Length && map.deckUnionsOf[e.index] != null && map.deckUnionsOf[e.index].Length > 0;

        static float ExtAtNode(CityMap.Edge X, int node, Vector2 toward)
        {
            float s = X.a == node ? 0f : X.length;
            LineModel.ExtentsNoAux(X, s, out float eM, out float eP);
            var tX = X.TangentAt(s);
            return Vector2.Dot(new Vector2(-tX.y, tX.x), toward) >= 0f ? eP : eM;
        }
        static float BaseExt(CityMap.Edge X, int node, Vector2 toward)
        {
            if (X.lmPlus == 0f && X.lmMinus == 0f) return X.width * 0.5f;
            var tX = X.a == node ? X.TangentAt(0f) : X.TangentAt(X.length);
            return Vector2.Dot(new Vector2(-tX.y, tX.x), toward) >= 0f ? X.lmPlus : X.lmMinus;
        }

        /// <summary>Style G (report): how far OSM's added lane runs on the
        /// other arm before that side changes again.</summary>
        public static float ZoneDataAux(CityMap map, Trims t, MergeZone z) => DataAuxRun(map, t, map.edges[z.other], z.node, z.nZ);
        static float DataAuxRun(CityMap map, Trims t, CityMap.Edge O, int node, Vector2 nZ)
        {
            var near = new List<int>(8);
            var ch = MitredChain(map, t, O, node, 2000f, near);
            float ext0 = BaseExt(O, node, nZ), run = 0f;
            var dir0 = OutDir(O, node); var left0 = new Vector2(-dir0.y, dir0.x);
            int sideC = Vector2.Dot(left0, nZ) >= 0f ? 1 : -1;
            for (int i = 0; i < ch.edges.Count; i++)
            {
                var P = ch.edges[i];
                var d = OutDir(P, near[i]);
                var nP = new Vector2(-d.y, d.x) * sideC;
                if (Mathf.Abs(BaseExt(P, near[i], nP) - ext0) > 0.3f) break;
                run += P.length;
            }
            return run;
        }

        static MergeZone TryZone(CityMap map, Trims t, CityMap.Edge L, CityMap.Edge M, CityMap.Edge O, int n, bool merge)
        {
            var near = new List<int>(8);
            var host = MitredChain(map, t, M, n, GoreReach + 60f, near);
            var br = BuildChain(map, L, n, linkChain: true, GoreReach + 10f, host);
            int side = 0;
            float prevT = -1f, tN = -1f;
            for (float tt = 0f; tt <= GoreReach; tt += 2f)
            {
                if (!ZoneSample(host, br, tt, ref side, out var o)) { ZoneReject("the host or the ramp runs out first"); return null; }
                if (side != 0 && o.sideNow != side && Mathf.Abs(o.off) > 0.4f) { ZoneReject("crosses its host"); return null; }
                if (o.g >= 0f)
                {
                    if (prevT < 0f) { ZoneReject("lanes apart at the node (a mouth)"); return null; }
                    float a = prevT, b = tt;
                    for (int it = 0; it < 8; it++)
                    {
                        float m = 0.5f * (a + b); int s2 = side;
                        if (!ZoneSample(host, br, m, ref s2, out var om)) break;
                        if (om.g >= 0f) b = m; else a = m;
                    }
                    tN = b;
                    break;
                }
                prevT = tt;
            }
            if (tN < 0f || side == 0) { ZoneReject("never leaves its host's lanes"); return null; }
            int sd = side;
            ZoneSample(host, br, tN, ref sd, out var at);
            float theta = Mathf.Acos(Mathf.Clamp(Vector2.Dot(at.dirL, at.dirM), -1f, 1f)) * Mathf.Rad2Deg;
            if (theta > ZoneMaxDeg) { ZoneReject("over 35 deg (a junction mouth)"); return null; }
            float xN = at.arcIn;
            // the step at N must lie inside one host piece, off its polyline
            // vertices (a section there is square to the chain), and the
            // ramp's end section inside one of its pieces: nudge N toward the
            // node until it does
            Vector2 endOuter = default, qN = default, rMN = default; float hExtN = 0f, tO = -1f, sO = 0f; CityMap.Edge EO = null, HN = null; float sHN = 0f;
            bool placed = false;
            for (int tryN = 0; tryN < 8 && !placed; tryN++)
            {
                if (xN < 2f) break;
                bool nearV = false;
                for (int i = 1; i + 1 < host.cum.Count; i++) if (Mathf.Abs(host.cum[i] - xN) < 0.35f) { nearV = true; break; }
                if (nearV) { xN -= 0.5f; continue; }
                if (!host.Walk(xN, out HN, out sHN, out qN, out var dN)) break;
                if (sHN < 0.7f || sHN > HN.length - 0.7f) { xN -= 0.8f; continue; }
                rMN = new Vector2(-dN.y, dN.x);
                hExtN = HostEdgeNoAux(HN, sHN, rMN, side, out _);
                // the ramp's outer vertex on the host's cross-line at xN
                float lo = Mathf.Max(0f, tN - 60f), hi = Mathf.Min(br.Length, tN + 60f);
                if (!OuterAt(host, br, lo, side, out _, out float aLo, out _, out _) || !OuterAt(host, br, hi, side, out _, out float aHi, out _, out _)
                    || aLo > xN || aHi < xN) break;
                for (int it = 0; it < 26; it++)
                {
                    float m = 0.5f * (lo + hi);
                    if (!OuterAt(host, br, m, side, out _, out float am, out _, out _)) break;
                    if (am < xN) lo = m; else hi = m;
                }
                tO = 0.5f * (lo + hi);
                OuterAt(host, br, tO, side, out endOuter, out _, out EO, out sO);
                if (sO < 0.7f || sO > EO.length - 0.7f) { xN -= 0.8f; continue; }
                placed = true;
            }
            if (!placed) { ZoneReject("no clean place for the nose"); return null; }
            for (int i = 0; i < host.edges.Count; i++)
                if (host.PieceRange(host.edges[i], out float c0u, out _) && c0u < xN && InUnion(map, host.edges[i]))
                { ZoneReject("on a twin deck"); return null; }
            // nor a ramp that is itself one carriageway of a twin-deck union
            for (int i = 0; i < br.edges.Count; i++)
                if (br.PieceRange(br.edges[i], out float b0u, out _) && b0u < tO + 40f && InUnion(map, br.edges[i]))
                { ZoneReject("the ramp is a twin deck"); return null; }
            var z = new MergeZone { node = n, branch = L.index, host = M.index, other = O.index, merge = merge, side = side, theta = theta, D = xN, hostChain = host };
            float outerLat = Vector2.Dot(endOuter - qN, rMN) * side;
            z.wN = outerLat - hExtN;
            z.endInner = qN + rMN * (side * hExtN);
            z.endOuter = qN + rMN * (side * outerLat);
            z.endHost = HN.index; z.endHostS = sHN;
            z.cutEdge = EO.index; z.sCut = sO;
            var tE = EO.TangentAt(sO);
            var leftE = new Vector2(-tE.y, tE.x);
            z.innerSide = Vector2.Dot(leftE, -rMN * side) > 0f ? 1 : -1;
            // the end section moves only its INNER vertex, toward the node
            // (its outer vertex is where it crosses N already): the section
            // before it never folds, so only the dedupe's half metre is kept
            // clear (a gore gap's end there stays a section)
            z.margin = 0.5f;
            // the zone side at the node, and the style
            var dOut = OutDir(M, n);
            z.nZ = new Vector2(-dOut.y, dOut.x) * side;
            var dO = OutDir(O, n);
            z.sideOther = Vector2.Dot(new Vector2(-dO.y, dO.x), z.nZ) >= 0f ? 1 : -1;
            float dBase = BaseExt(O, n, z.nZ) - BaseExt(M, n, z.nZ), dOpp = BaseExt(O, n, -z.nZ) - BaseExt(M, n, -z.nZ);
            int kG = Mathf.RoundToInt(dBase / RoadProfiles.LaneM);
            int brLanes = Mathf.Clamp(EO.lanes > 0 ? EO.lanes : RoadProfiles.All[EO.profile].lanes, 1, 3);
            if (kG >= 1 && Mathf.Abs(dBase - kG * RoadProfiles.LaneM) <= 1.2f)
            {
                if (kG > brLanes) { ZoneReject("OSM adds more lanes than the ramp brings"); return null; }
                z.style = 'G'; z.k = kG;
            }
            else { z.style = 'T'; z.k = brLanes; }
            z.sideMismatch = dBase < 0.5f * RoadProfiles.LaneM && dOpp > 0.5f * RoadProfiles.LaneM;
            // design speeds: the ramp a loop when it turns past 120 deg in reach
            float turn = 0f;
            for (int i = 1; i + 1 < br.pts.Count && br.cum[i] < 300f; i++)
            {
                var u = br.pts[i] - br.pts[i - 1]; var v = br.pts[i + 1] - br.pts[i];
                if (u.sqrMagnitude < 1e-4f || v.sqrMagnitude < 1e-4f) continue;
                turn += Vector2.SignedAngle(u, v);
            }
            z.loop = Mathf.Abs(turn) > 120f;
            z.vHost = DesignMph(M);
            z.vRamp = L.link ? (z.loop ? 30f : 40f) : Mathf.Min(DesignMph(L), z.vHost);
            z.lReq = AashtoM(merge, z.vHost, z.vRamp);
            bool fw = FreewayLike(M);
            z.ltRule = merge ? (fw ? 90f : Mathf.Max(30f, z.k * RoadProfiles.LaneM * (z.vHost <= 40f ? z.vHost * z.vHost / 60f : z.vHost)))
                             : (fw ? 75f : 30f);
            z.lt = z.ltRule;
            LineModel.Extents(EO, sO, out float bM, out float bP);
            z.brLaneW = Mathf.Max(0.5f * brLanes * RoadProfiles.LaneM, bM + bP - EO.shl - EO.shr);
            // the ramp's own height against the host's at N (report)
            z.seatDy = EO.YAt(sO) - HN.YAt(sHN);
            // the cuts: every ramp piece between N and the node
            for (int i = 0; i < br.edges.Count; i++)
            {
                var E = br.edges[i];
                if (!br.PieceRange(E, out float b0, out float b1)) continue;
                if (b0 >= tO - 1e-3f) break;
                int nodeEnd = i == 0 ? n : SharedNode(E, br.edges[i - 1]);
                int nodeDir = E.a == nodeEnd ? -1 : 1;
                if (E == EO)
                {
                    z.cuts.Add(new ZoneCut { edge = E.index, end = true, sEnd = sO, nodeDir = nodeDir, innerSide = z.innerSide, z = z,
                                             s0 = nodeDir < 0 ? 0f : sO, s1 = nodeDir < 0 ? sO : E.length });
                    break;
                }
                var tEi = E.TangentAt(E.length * 0.5f);
                int inS = Vector2.Dot(new Vector2(-tEi.y, tEi.x), -z.nZ) > 0f ? 1 : -1;
                z.cuts.Add(new ZoneCut { edge = E.index, end = false, s0 = 0f, s1 = E.length, nodeDir = nodeDir, innerSide = inS, z = z });
            }
            foreach (var h in host.edges) foreach (var b in br.edges) z.pairs.Add(PairKey(b.index, h.index));
            return z;
        }

        /// <summary>Q2 for one style-T zone: the full-width length AASHTO asks
        /// from N, the taper after it, and how much of the other arm that
        /// takes - clamped where the other arm stops being this road.</summary>
        static void Extend(CityMap map, Trims t, MergeZone z, Dictionary<int, List<MergeZone>> diverges, Dictionary<int, List<MergeZone>> byNode)
        {
            z.xf = z.D - z.lReq;
            z.need = Mathf.Max(0f, z.lt - z.xf);
            if (z.need <= 0f) return;
            var O = map.edges[z.other];
            var near = new List<int>(8);
            var oc = MitredChain(map, t, O, z.node, z.need + 5f, near);
            float ext0 = BaseExt(O, z.node, z.nZ);
            float avail = 0f;
            for (int i = 0; i < oc.edges.Count; i++)
            {
                var P = oc.edges[i];
                var dP = OutDir(P, near[i]);
                var nP = new Vector2(-dP.y, dP.x) * z.sideOther;
                // a deck carries the lane on (its slab follows the ribbon), but
                // not a twin-deck union (one structure, its median between)
                if (P.tunnel || InUnion(map, P)) { z.clamp = P.tunnel ? "tunnel" : "twin deck"; break; }
                if (Mathf.Abs(BaseExt(P, near[i], nP) - ext0) > 0.3f) { z.clamp = "lane change"; break; }
                avail += P.length;
                if (avail >= z.need) break;
                int far = P.a == near[i] ? P.b : P.a;
                if (i + 1 >= oc.edges.Count) { z.clamp = t.mitre[far] ? "chain end" : "junction"; break; }
                // another branch at the far node on this side
                var dFar = -OutDir(P, far);
                var nFar = new Vector2(-dFar.y, dFar.x) * z.sideOther;
                bool stop = false;
                foreach (int xi in map.nodeEdges[far])
                {
                    var X = map.edges[xi];
                    if (X == P || X == oc.edges[i + 1] || X.a == X.b) continue;
                    if (Vector2.Dot(OutDir(X, far), nFar) <= 0f) continue;   // the other side
                    bool diverge = X.oneway && X.a == far;
                    if (diverge && diverges.TryGetValue(far, out var dl) && dl.Exists(d => d.branch == X.index)) { z.backToBack = true; z.clamp = "next exit (aux lane)"; }
                    else
                    {
                        z.clamp = diverge ? "next exit" : "next entrance";
                        // never over the next entrance's own zone, nor where
                        // its ramp still runs beside the host (its P..N)
                        float back = 30f;
                        if (!diverge && byNode.TryGetValue(far, out var ml))
                            foreach (var m2 in ml) if (m2.branch == X.index) back = m2.D + 30f;
                        avail = Mathf.Max(0f, avail - back);
                    }
                    stop = true; break;
                }
                if (stop) break;
            }
            z.avail = avail;
            if (avail >= z.need) { z.clamp = null; return; }
            if (z.backToBack) { z.xf = -avail; z.lt = 0f; return; }
            z.xf = Mathf.Max(z.xf, z.lt - avail);
            if (z.xf > z.D) { z.lt = Mathf.Max(0f, z.lt - (z.xf - z.D)); z.xf = z.D; }
        }

        /// <summary>The zone's aux eases on the host pieces either side of
        /// the node, and its cuts on the ramp.</summary>
        static void Apply(CityMap map, Trims t, MergeZone z)
        {
            z.ls = Mathf.Clamp(20f * Mathf.Abs(z.wN - z.target), 0f, Mathf.Max(0f, z.D - Mathf.Max(z.xf, 0f)));
            z.proto = new LineModel.Ease { aux = true, auxD = z.D, auxWN = z.wN, auxW = z.target, auxLs = z.ls, auxXF = z.xf, auxLt = z.lt };
            float xLo = z.xf - z.lt;   // where the aux lane is gone (x)
            // the zone arm
            var near = new List<int>(8);
            var zc = MitredChain(map, t, map.edges[z.host], z.node, z.D + 2f, near);
            for (int i = 0; i < zc.edges.Count; i++)
            {
                var P = zc.edges[i];
                if (!zc.PieceRange(P, out float c0, out float c1)) continue;
                if (c0 > z.D + 1e-3f) break;
                if (c1 < Mathf.Max(0f, xLo)) continue;
                var ez = z.proto;
                ez.auxArm = 1; ez.d0 = c0; ez.fromA = P.a == near[i];
                ez.side = (sbyte)(P.a == near[i] ? z.side : -z.side);
                ez.auxZone = z.id;
                LineModel.AddAux(P, ez); z.auxEdges.Add(P.index);
            }
            if (xLo < 0f)
            {
                var oc = MitredChain(map, t, map.edges[z.other], z.node, -xLo + 2f, near);
                z.otherChain = oc;
                for (int i = 0; i < oc.edges.Count; i++)
                {
                    var P = oc.edges[i];
                    if (!oc.PieceRange(P, out float c0, out _)) continue;
                    if (c0 >= -xLo) break;
                    var ez = z.proto;
                    ez.auxArm = -1; ez.d0 = c0; ez.fromA = P.a == near[i];
                    ez.side = (sbyte)(P.a == near[i] ? z.sideOther : -z.sideOther);
                    ez.auxZone = z.id;
                    LineModel.AddAux(P, ez); z.auxEdges.Add(P.index);
                }
            }
            foreach (var c in z.cuts)
            {
                if (!zoneCuts.TryGetValue(c.edge, out var l)) zoneCuts[c.edge] = l = new List<ZoneCut>(1);
                l.Add(c);
            }
            foreach (var k in z.pairs) zonePairs.Add(k);
        }

        // ----------------------------------------------------------------
        //  The builder's side of it
        // ----------------------------------------------------------------
        static readonly List<float> zoneForced = new List<float>(4);
        static readonly List<(float a, float b)> zoneClear = new List<(float, float)>(4);

        /// <summary>After SamplePositions' dedupe: the sections the zones need
        /// exactly - the host's step at N (N and 2 cm past it), the ramp's end
        /// section and a collapsed one 2 cm toward the node - with no other
        /// section near them (none within the ramp's skew upstream, so the
        /// end section, square to the HOST, never folds over the one before).</summary>
        static void ZoneForcedSamples(CityMap.Edge e, float sMin, float sMax)
        {
            if (Zones.Count == 0) return;
            zoneForced.Clear(); zoneClear.Clear();
            var ez = e.lmEase;
            if (ez != null)
                foreach (var z in ez)
                {
                    if (!z.aux || z.auxArm <= 0) continue;
                    float d = z.auxD;
                    if (d < z.d0 + 0.05f || d > z.d0 + e.length - 0.05f) continue;
                    float sN = z.fromA ? d - z.d0 : e.length - (d - z.d0);
                    float sB = sN + (z.fromA ? 0.02f : -0.02f);
                    if (sN <= sMin + 0.05f || sN >= sMax - 0.05f || sB <= sMin + 0.01f || sB >= sMax - 0.01f) continue;
                    zoneForced.Add(sN); zoneForced.Add(sB);
                    zoneClear.Add((Mathf.Min(sN, sB) - 0.4f, Mathf.Max(sN, sB) + 0.4f));
                }
            if (zoneCuts.TryGetValue(e.index, out var cl))
                foreach (var c in cl)
                {
                    if (!c.end || c.sEnd <= sMin + 0.05f || c.sEnd >= sMax - 0.05f) continue;
                    zoneForced.Add(c.sEnd);
                    float sIn = c.sEnd + c.nodeDir * 0.02f;
                    if (sIn > sMin + 0.01f && sIn < sMax - 0.01f) zoneForced.Add(sIn);
                    float a = c.sEnd - c.nodeDir * c.z.margin, b = c.sEnd + c.nodeDir * 0.4f;
                    zoneClear.Add((Mathf.Min(a, b), Mathf.Max(a, b)));
                }
            if (zoneForced.Count == 0) return;
            int w = 0;
            for (int i = 0; i < sampleS.Count; i++)
            {
                float sv = sampleS[i];
                bool drop = false;
                if (sv > sMin + 1e-3f && sv < sMax - 1e-3f)
                    foreach (var r in zoneClear) if (sv > r.a && sv < r.b) { drop = true; break; }
                if (!drop) sampleS[w++] = sv;
            }
            sampleS.RemoveRange(w, sampleS.Count - w);
            sampleS.AddRange(zoneForced);
            sampleS.Sort();
        }

        /// <summary>A ramp section inside its zone: the end section on the
        /// host's cross-line at N (the host's own vertices there, at the host's
        /// height), or collapsed (nothing drawn: a span with both ends
        /// collapsed is skipped).</summary>
        static void ApplyZoneCut(CityMap map, CityMap.Edge e, float s, ref Section sec)
        {
            if (!zoneCuts.TryGetValue(e.index, out var cl)) return;
            foreach (var c in cl)
            {
                if (c.end && Mathf.Abs(s - c.sEnd) < 2e-3f)
                {
                    var z = c.z;
                    float y = map.edges[z.endHost].YAt(z.endHostS);
                    var vi = new Vector3(z.endInner.x, y, z.endInner.y);
                    var vo = new Vector3(z.endOuter.x, y, z.endOuter.y);
                    if (c.innerSide > 0) { sec.R = vi; sec.L = vo; } else { sec.L = vi; sec.R = vo; }
                    sec.collapsed = false; sec.clippedIn = true; sec.innerSide = c.innerSide; sec.zoneEnd = true;
                    return;
                }
                bool inside = c.end ? (c.nodeDir > 0 ? s > c.sEnd + 1e-3f : s < c.sEnd - 1e-3f)
                                    : (s >= c.s0 - 1e-3f && s <= c.s1 + 1e-3f);
                if (!inside) continue;
                var v = c.innerSide > 0 ? sec.R : sec.L;
                sec.L = v; sec.R = v;
                sec.collapsed = true; sec.clippedIn = true; sec.innerSide = c.innerSide;
                return;
            }
        }

        /// <summary>
        /// PLAN A9: the lane a ramp drives between N and its node is the
        /// host's aux lane - its centre at N (where the ramp's own lane ends),
        /// moving over to the host's lanes' centre by the node (smoothstep on
        /// the host arc), so the race line, the grid, traffic and the AI never
        /// cut across the cut ramp's old line.
        /// </summary>
        public static bool ZoneLanePoint(CityMap.Edge e, float s, out Vector2 p)
        {
            p = default;
            if (!ZoneLanesOn || zoneCuts.Count == 0 || !zoneCuts.TryGetValue(e.index, out var cl)) return false;
            foreach (var c in cl)
            {
                bool inside = c.end ? (c.nodeDir > 0 ? s >= c.sEnd : s <= c.sEnd) : (s >= c.s0 - 1e-4f && s <= c.s1 + 1e-4f);
                if (!inside) continue;
                var z = c.z;
                z.hostChain.Project(e.PointAt(s), out var H, out float sM, out var q, out var dirM, out bool atEnd, out float x);
                if (atEnd) return false;
                var rM = new Vector2(-dirM.y, dirM.x);
                float hExt = HostEdgeNoAux(H, sM, rM, z.side, out float hSh);
                float aux = LineModel.AuxProfile(z.proto, Mathf.Min(x, z.D));
                float full = z.k * RoadProfiles.LaneM;
                // at N the ramp's own lanes' width (its lane centre runs on), eased with the shoulder
                if (z.ls > 1e-3f && x > z.D - z.ls) full += (z.brLaneW - full) * LineModel.Smooth((Mathf.Min(x, z.D) - (z.D - z.ls)) / z.ls);
                else if (x >= z.D - 1e-3f) full = z.brLaneW;
                float laneW = x >= z.xf ? full : full * Mathf.Clamp01(z.target > 1e-3f ? aux / z.target : 0f);
                var auxPt = q + rM * (z.side * (hExt - hSh + 0.5f * laneW));
                var hostPt = LineModel.OwnLanePoint(H, sM);
                float f = LineModel.Smooth(Mathf.Clamp01(x / Mathf.Max(1f, z.D)));
                p = Vector2.Lerp(hostPt, auxPt, f);
                // the last metres before N hand over from the ramp's own lane
                // (its centre at N is the aux lane's to within a skewed cross-line)
                float lb = Mathf.Min(15f, 0.5f * z.D);
                float g = lb > 1e-3f ? LineModel.Smooth(Mathf.Clamp01((x - (z.D - lb)) / lb)) : 1f;
                if (g > 0f) p = Vector2.Lerp(p, LineModel.OwnLanePoint(e, s), g);
                return true;
            }
            return false;
        }

        /// <summary>The race line's samples through a ramp's zone (every 3 m).</summary>
        public static void ZoneSamples(CityMap.Edge e, float sMin, float sMax, List<float> into)
        {
            if (zoneCuts.Count == 0 || !zoneCuts.TryGetValue(e.index, out var cl)) return;
            foreach (var c in cl)
            {
                float a = Mathf.Max(sMin, c.end ? (c.nodeDir > 0 ? c.sEnd : 0f) : c.s0);
                float b = Mathf.Min(sMax, c.end ? (c.nodeDir > 0 ? e.length : c.sEnd) : c.s1);
                for (float sv = a; sv <= b; sv += 3f) into.Add(sv);
                if (b > a) into.Add(b);
            }
        }
    }
}
