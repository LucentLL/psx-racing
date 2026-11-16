using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  THE WARRANT AT MITRED NODES (2026-11-16, control round;
    //  PSX_CITY_MITREWARRANT=0: none, as before; off too with PSX_CITY_SIGNALS2=0).
    //  The owner on Beatties Ford Road (shots 34-37 of PC45): "abrupt
    //  medians that force the road to meet at strange angles without stop
    //  signs". The warrants (CitySignals.Control) skipped every MITRED node:
    //  a road meeting another at a shallow angle, drawn with no junction fan
    //  (CityMeshes.ComputeTrimsCore: a through pair within 32 degrees of
    //  straight, every other arm within 60 degrees of the arm it is clipped
    //  onto). About 340 named side streets at about 315 such nodes had no
    //  sign at all, 281 of them with no OSM tag near (align investigation).
    //
    //  THE RULE. At a mitred node OSM leaves uncontrolled (no signal, STOP or
    //  give-way of its own, not one short edge from a signal), a NAMED side
    //  street - not a ramp or turning roadway (a link), not the through
    //  road's own name, not a service drive - that arrives there STOPS. A
    //  one-way side roadway that merges with the traffic of its side (a
    //  channelised slip: within 50 degrees of a through flow and joining it
    //  from that flow's right, or either side of a one-way carriageway)
    //  YIELDS. Left as they were, each counted and listed by the audit:
    //  a 3-arm carriageway split (no flow crosses another: no control, the
    //  MUTCD's and the old rule's), links (ramps and slips: the merge rule,
    //  and the paint's yield teeth where a link meets a street at 35 degrees
    //  or more), unnamed arms (driveways, aisles), one-way exits (no
    //  approach), side roads under StubM from the junction behind them, and
    //  a side road that outranks the through road.
    //
    //  THE LINE. A clipped arm has no mouth of its own: its pavement leaves
    //  the host's edge some way out (about 19 m at 30 degrees from the centre
    //  of a five-lane road). Its stop line stands where both ends of its bar
    //  are MitreClearM clear of every other road's drawn pavement at that
    //  level, StopBackM further back, walked on through joints up to
    //  MitreReachM; no clear place within that: no line (listed). With the
    //  switch on, OSM's own STOP and give-way tags at mitred nodes stand
    //  their lines the same way (their bar stood StopBackM from the node, on
    //  the host's pavement).
    // ======================================================================
    public static partial class CitySignals
    {
        /// <summary>PSX_CITY_MITREWARRANT=0: no warrant at mitred nodes, OSM's tags there stand their line as before.</summary>
        public static bool MitreWarrantOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SIGNALS2") != "0"    // (read here: another
                                            && System.Environment.GetEnvironmentVariable("PSX_CITY_MITREWARRANT") != "0"; // file's initialiser may run later)
        /// <summary>How far out along a clipped arm its stop line may stand.</summary>
        public const float MitreReachM = 45f;
        const float MitreClearM = 0.3f, MitreStepM = 0.5f, MitreLevelM = 3f;

        public struct MitreCounts
        {
            public int nodes, sideArms, stops, yields, splits, links, unnamed, sameName, exits, internalArms, service, shortStubs,
                       outranks, noLine, nearSignal, intoSignal, taggedLines, taggedNoLine, yieldJ;
            public float lineMin, lineMax, lineSum;
        }
        public static MitreCounts M;
        /// <summary>Why each arm of a mitred node got what it got (node << 32 | edge): the audit's list.</summary>
        public static readonly Dictionary<long, string> MitreWhy = new Dictionary<long, string>();
        /// <summary>Why the warrant never looked at a mitred node (a signal's, a tag's, a roundabout's): the audit's list.</summary>
        public static readonly Dictionary<int, string> MitreNodeWhy = new Dictionary<int, string>();

        static long MitreKey(int node, int edge) => ((long)node << 32) | (uint)edge;

        public static string MitreReport()
        {
            if (!MitreWarrantOn) return "mitred-node warrant OFF (PSX_CITY_MITREWARRANT=0)";
            var k = M;
            int lines = k.stops + k.yields;
            return $"mitred-node warrant: {k.nodes} nodes with a side arm decided, {k.sideArms} side arms: STOP {k.stops}, YIELD (one-way slip) {k.yields}; " +
                   $"left as they were: carriageway splits {k.splits}, links {k.links}, unnamed {k.unnamed}, the through road's own name {k.sameName}, one-way exits {k.exits}, " +
                   $"inside a junction {k.internalArms}, service drives {k.service}, short stubs {k.shortStubs}, outranking the through road {k.outranks}, no clear place for a line {k.noLine}; " +
                   $"arms into a signal within 45 m {k.intoSignal}; nodes next to a signal (decided all the same) {k.nearSignal}; OSM tags at mitred nodes with their line moved clear {k.taggedLines} (no clear place: {k.taggedNoLine}); " +
                   $"lines {(lines > 0 ? (k.lineSum / Mathf.Max(1, lines + k.taggedLines)).ToString("0.0") : "-")} m from the node on average, {k.lineMin:0.0}-{k.lineMax:0.0} m";
        }

        static void MitreReset() { M = default; M.lineMin = float.MaxValue; MitreWhy.Clear(); MitreNodeWhy.Clear(); }

        /// <summary>
        /// The warrant's decision at mitred node n (its usable arms in
        /// <paramref name="arms"/>; no freeway, no roundabout, not taken, not
        /// next to a signal). Adds one Decision whose approaches carry their
        /// own control (2 STOP, 1 YIELD) and line.
        /// </summary>
        static void MitreSide(CityMap map, CityMeshes.Trims trims, int n, List<int> arms, List<Decision> list, bool[] sigMember)
        {
            bool split = SplitArms(map, n, out int tw, out int ia, out int oa);
            if (split && arms.Count == 3)
            {
                M.splits++;
                foreach (int ei in arms) MitreWhy[MitreKey(n, ei)] = "a carriageway split: uncontrolled";
                return;
            }
            // the through road: the control's ranked pair, else the meshes' mitred pair
            ThroughArms(map, n, out int m1, out int m2);
            if (m1 < 0 || m2 < 0) { m1 = trims.throughA[n]; m2 = trims.throughB[n]; }
            if (m1 < 0 || m2 < 0) return;
            var A = map.edges[m1]; var B = map.edges[m2];
            int thrRank = Mathf.Min(RankOf(A), RankOf(B));
            Vector2 tA = -Out(A, n), tB = -Out(B, n);          // the through flows, arriving by A and by B
            bool hostOneWay = A.oneway && B.oneway;
            List<MitreApp> apps = null;
            bool any = false;
            foreach (int ei in arms)
            {
                if (ei == m1 || ei == m2) continue;
                if (split && (ei == tw || ei == ia || ei == oa)) continue;
                var m = map.edges[ei];
                string why;
                if (m.link) { M.links++; why = "a ramp or turning roadway (link): the merge rule, as before"; }
                else if (string.IsNullOrEmpty(m.name)) { M.unnamed++; why = "unnamed (a driveway or aisle): as before"; }
                else if (m.name == A.name || m.name == B.name) { M.sameName++; why = "the through road's own name: as before"; }
                else if (m.oneway && m.a == n) { M.exits++; why = "a one-way exit: no approach"; }
                else if (trims.Internal(ei)) { M.internalArms++; why = "inside a junction cluster"; }
                else if (m.hasLset && m.lsSub == 4) { M.service++; why = "a service drive"; }
                else if (RankOf(m) > thrRank) { M.outranks++; why = "it outranks the through road: left as before"; }
                else if (m.length <= 45f && sigMember[m.a == n ? m.b : m.a]) { M.intoSignal++; why = "it leads into a signal within 45 m: the signal's arm"; }
                else if (ShortStub(map, m, n)) { M.shortStubs++; why = $"a side road under {StubM:0} m from the junction behind it: yields by the traffic's rule"; }
                else
                {
                    any = true;
                    M.sideArms++;
                    var tm = -Out(m, n);
                    // a one-way slip merging with a flow: within 50 degrees of it, from its right (a two-way host)
                    var far = m.PointAt(m.a == n ? Mathf.Min(10f, m.length) : Mathf.Max(0f, m.length - 10f)) - map.nodes[n];
                    bool slip = false;
                    if (m.oneway)
                        foreach (var f in new[] { tA, tB })
                        {
                            if (Vector2.Angle(tm, f) >= 50f) continue;
                            float cr = f.x * far.y - f.y * far.x;                     // > 0: the arm lies left of the flow
                            if (hostOneWay || cr < 0f) slip = true;
                        }
                    byte ctl = slip ? (byte)1 : (byte)2;
                    if (!MitreLine(map, trims, m, n, out int lineEdge, out int lineNode, out float back, out float fromNode))
                    {
                        M.noLine++;
                        MitreWhy[MitreKey(n, ei)] = $"no place within {MitreReachM:0} m where its bar is clear of the other roads: no line";
                        continue;
                    }
                    (apps ??= new List<MitreApp>(2)).Add(new MitreApp { edge = lineEdge, at = lineNode, back = back, ctl = ctl });
                    if (ctl == 1) M.yields++; else M.stops++;
                    M.lineMin = Mathf.Min(M.lineMin, fromNode); M.lineMax = Mathf.Max(M.lineMax, fromNode); M.lineSum += fromNode;
                    MitreWhy[MitreKey(n, ei)] = (ctl == 1 ? "YIELD (a one-way slip merging with the traffic)" : "STOP") + $", the line {fromNode:0.0} m from the node";
                    continue;
                }
                MitreWhy[MitreKey(n, ei)] = why;
            }
            if (any) M.nodes++;
            if (apps != null) list.Add(new Decision { node = n, mitre = apps });
        }

        /// <summary>One approach the mitred warrant stands: the edge and node its line is on, how far back from that
        /// node's mouth, and its control (2 STOP, 1 YIELD).</summary>
        public struct MitreApp { public int edge, at; public float back; public byte ctl; }

        /// <summary>
        /// THE LINE of arm e into mitred node n: the first place out along it
        /// (through joints, up to <see cref="MitreReachM"/>) where both ends of
        /// its approach's bar are <see cref="MitreClearM"/> clear of every other
        /// road's drawn pavement at its level; the line StopBackM behind that.
        /// lineEdge / lineNode: the piece the line stands on and the node it
        /// leads into; back: from that node's mouth, as MakeApproach takes it;
        /// fromNode: the road from n to the line.
        /// </summary>
        static bool MitreLine(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n, out int lineEdge, out int lineNode, out float back, out float fromNode)
        {
            lineEdge = e.index; lineNode = n; back = StopBackM; fromNode = 0f;
            var cur = e; int at = n; float acc = 0f;
            mitreChain.Clear(); mitreChain.Add(e.index);
            for (float d = MitreStepM; d <= MitreReachM; d += MitreStepM)
            {
                float local = d - acc;
                while (local + StopBackM > cur.length - 1f)
                {
                    // on through a joint, the way the traffic comes
                    int far = cur.a == at ? cur.b : cur.a;
                    if (far == n || Degree(map, far) != 2) return false;
                    int next = -1;
                    foreach (int oi in map.nodeEdges[far]) { var x = map.edges[oi]; if (oi != cur.index && x.a != x.b) next = oi; }
                    if (next < 0) return false;
                    var nx = map.edges[next];
                    if (nx.oneway && nx.b != far) return false;                 // its traffic leaves the joint: none comes this way
                    if (nx.tunnel || nx.roundabout || (nx.cls >= 5 && !nx.link)) return false;
                    acc += cur.length; local = d - acc;
                    cur = nx; at = far;
                    mitreChain.Add(next);
                }
                bool atB = cur.b == at;
                float s = atB ? cur.length - local : local;
                if (!BarClear(map, cur, s, atB)) continue;
                float trim = trims.TrimAt(cur, at);
                lineEdge = cur.index; lineNode = at;
                back = Mathf.Max(StopBackM, local + StopBackM - trim);
                fromNode = acc + local + StopBackM;
                return true;
            }
            return false;
        }

        static readonly List<int> mitreChain = new List<int>(4);
        static readonly HashSet<int> barNear = new HashSet<int>();

        /// <summary>Both ends of the approach's bar at (e, s), travelling toward b (atB) or a, clear of every other
        /// usable road's drawn pavement at its level (the chain being walked excepted).</summary>
        static bool BarClear(CityMap map, CityMap.Edge e, float s, bool atB)
        {
            var p = e.PointAt(s);
            var tan = e.TangentAt(s);
            var u = atB ? tan : -tan;
            var right = new Vector2(u.y, -u.x);
            int sideR = atB ? 1 : -1;
            float edgeR = e.PaveEdgeM(s, sideR), edgeL = e.PaveEdgeM(s, -sideR);
            float inR = edgeR - 0.3f, inL;
            if (e.oneway) inL = -edgeL + 0.3f;
            else
            {
                float divider = atB ? -LineModel.DividerLat(e) : LineModel.DividerLat(e);
                bool twltl = e.hasLset ? e.lsCentre == 2 : e.turnLane;
                inL = divider + (twltl ? 1.7f : 0.15f);
            }
            if (inR - inL < 2.4f) inL = inR - 2.4f;
            float y = e.YAt(s);
            return PointClear(map, p + right * inL, y) && PointClear(map, p + right * inR, y);
        }

        static bool PointClear(CityMap map, Vector2 q, float y)
        {
            const float r = 14f;
            barNear.Clear();
            map.EdgeSegsInRect(q - new Vector2(r, r), q + new Vector2(r, r), barNear);
            foreach (int pk in barNear)
            {
                int hi = pk >> 12, seg = pk & 0xFFF;
                if (mitreChain.Contains(hi)) continue;
                var h = map.edges[hi];
                if (h.a == h.b || !Usable(h) || seg + 1 >= h.pts.Length) continue;
                Vector2 a = h.pts[seg], b = h.pts[seg + 1], ab = b - a;
                float L2 = ab.sqrMagnitude;
                float t = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / L2) : 0f;
                float dist = Vector2.Distance(a + ab * t, q);
                if (dist > 12f) continue;
                float sh = h.s[seg] + t * Mathf.Sqrt(L2);
                if (Mathf.Abs(h.YAt(sh) - y) > MitreLevelM) continue;           // another level (a deck over it)
                float ext = h.PaveEdgeM(sh, h.SideOf(seg, q));
                if (dist < ext + MitreClearM) return false;
            }
            return true;
        }

        /// <summary>An OSM tag's approach at a mitred node (a clipped arm): its line where the bar is clear, as the
        /// warrant's (true), or as before (false: the switch off, not a clipped arm, or no clear place).</summary>
        static bool MitreTagLine(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n, out int lineEdge, out int lineNode, out float back)
        {
            lineEdge = e.index; lineNode = n; back = StopBackM;
            if (!MitreWarrantOn || trims == null || trims.mitre == null || n >= trims.mitre.Length || !trims.mitre[n]) return false;
            if (trims.BranchAt(e, n) < 0) return false;                         // a through arm: its own line as before
            if (e.oneway && e.a == n) return false;
            if (!MitreLine(map, trims, e, n, out lineEdge, out lineNode, out back, out float fromNode)) { M.taggedNoLine++; return false; }
            M.taggedLines++;
            M.lineMin = Mathf.Min(M.lineMin, fromNode); M.lineMax = Mathf.Max(M.lineMax, fromNode); M.lineSum += fromNode;
            return true;
        }

        /// <summary>Is this approach's bar clear of every other road's drawn pavement at its level (the audit's
        /// check of the lines of arms clipped onto a host)?</summary>
        public static bool ApproachBarClear(CityMap map, Approach a)
        {
            var e = map.edges[a.edge];
            mitreChain.Clear(); mitreChain.Add(a.edge);
            return BarClear(map, e, a.sStop, e.b == a.node);
        }

        /// <summary>Is plan point q (at height y) on any usable road's drawn pavement, or within 0.3 m of it
        /// (the audit's furniture check)?</summary>
        public static bool OnPavement(CityMap map, Vector2 q, float y)
        {
            mitreChain.Clear();
            return !PointClear(map, q, y);
        }

        /// <summary>The audit's why for an OSM STOP / give-way tag on a game road with no control near: where its
        /// rule takes it, and why that approach stands no line.</summary>
        public static string ExplainTag(CityMap map, CityMeshes.Trims trims, ulong nodeId)
        {
            Ensure(map, trims);
            var sb = new System.Text.StringBuilder();
            var flags = LoadStopFlags();
            foreach (var t in map.tagged)
            {
                if (t.nodeId != nodeId || t.edge < 0 || t.edge >= map.edges.Length) continue;
                var e = map.edges[t.edge];
                flags.TryGetValue(t.nodeId, out byte f);
                bool fwd = (f & 2) != 0, back = (f & 4) != 0;
                float d0 = t.s, d1 = e.length - t.s;
                sb.Append($"[kind {t.kind} on e{t.edge} '{e.name}' {t.s:0.0} of {e.length:0.0} m, flags {f}");
                int n, arrive;
                if (Signals2On && !(Mathf.Min(d0, d1) < 1.5f && Degree(map, d0 <= d1 ? e.a : e.b) >= 3))
                {
                    if (!WalkTag(map, t.edge, t.s, fwd ? 1 : back ? -1 : e.oneway ? 1 : 0, out n, out arrive)) { sb.Append(": its walk finds no junction ahead]"); continue; }
                    sb.Append($": walks to n{n} by e{arrive}");
                }
                else { n = d0 <= d1 ? e.a : e.b; arrive = t.edge; sb.Append($": at n{n}"); }
                sb.Append($" (degree {Degree(map, n)}, {(trims.mitre[n] ? "mitred" : trims.patch[n] ? "fan" : "plain")}): ");
                string held = null;
                foreach (var j in junctions) if (System.Array.IndexOf(j.nodes, n) >= 0) { held = j.signal ? "the node is a signal's" : "the node's junction has no approach for it"; break; }
                sb.Append(held ?? WhyNoApproach(map, trims, map.edges[arrive], n)).Append(']');
            }
            return sb.Length > 0 ? sb.ToString() : "[not in TAGN]";
        }

        /// <summary>Why arm e stands no stop line into node n (the audit's).</summary>
        public static string WhyNoLine(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n) => WhyNoApproach(map, trims, e, n);

        static string WhyNoApproach(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n)
        {
            if (Degree(map, n) < 3) return "not a junction (two arms)";
            if (e.a == e.b) return "a loop";
            if (e.tunnel) return "in a tunnel";
            if (e.roundabout) return "on a roundabout";
            if (trims.Internal(e.index)) return "the arm is inside a junction cluster";
            if (e.cls >= 5 && !e.link) return "a freeway";
            if (e.oneway && e.a == n) return "a one-way leaving the junction";
            if (e.length < 6f) return $"the arm is {e.length:0.0} m long (a line needs 6)";
            bool atB = e.b == n;
            float trim = trims.TrimAt(e, n);
            float sMouth = atB ? e.length - trim : trim, sStop = atB ? sMouth - StopBackM : sMouth + StopBackM;
            if (sStop < 1f || sStop > e.length - 1f) return $"no room for the line (trim {trim:0.0} m of {e.length:0.0})";
            if (e.ElevatedAt(sStop)) return "the line would stand on a structure";
            return "an approach could stand: not reached by the rule";
        }

        /// <summary>The signal tag's kind from charlotte_sigtags.bytes (0 a junction's signal, 1 emergency,
        /// 2 pedestrian crossing, 3 blinker; -1 not listed or round 2 off): the audit's.</summary>
        public static int SignalTagKind(ulong nodeId)
        {
            if (sigTagsFor == null || !sigTagsFor.TryGetValue(nodeId, out byte f)) return -1;
            return (f >> 4) & 7;
        }
    }
}
