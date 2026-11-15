using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  JUNCTION CONTROL, ROUND 2 (2026-11-15; PSX_CITY_SIGNALS2=0: as it was).
    //  The road review found 29 junctions with a real signal that the game
    //  signs with STOP or with nothing (Five Points first), and the owner
    //  asked for the minor and neighbourhood streets' control by name.
    //
    //  WHAT WAS WRONG (the old rule, kept behind the switch):
    //   * a signal tagged on an approach marked the NEARER END of the piece
    //     of road it lies on, within 20 m, whatever way its traffic goes: a
    //     stop-line signal 24-38 m before a wide junction marked nothing, one
    //     10 m past a slip's node marked the slip, and one whose nearer end
    //     is a joint of two pieces (a way split where its tags change) was
    //     dropped (Rozzelles Ferry Road at Five Points);
    //   * a junction whose arms are split by such joints a few metres out
    //     (West Trade Street and West 5th Street at Five Points: 4-6 m
    //     stubs) could stand no stop line on them, came out with fewer than
    //     two approaches, and was dropped - with its nodes still held by the
    //     signal, so no STOP either: nothing at all;
    //   * every traffic_signals node read as a junction's signal, the fire
    //     stations' (emergency), the crossings' own (pedestrian_crossing)
    //     and the flashing beacons (blinker) too.
    //
    //  THE RULE NOW.
    //   SIGNALS  A tag AT a junction node marks it. A tag on an approach
    //     marks the junction its traffic DRIVES INTO: along the tag's own way
    //     in the tagged direction (traffic_signals:direction / direction;
    //     a one-way's own sense), through joints and carriageway splits, to
    //     the first junction within SignalReachM; untagged on a two-way road,
    //     the nearer junction either way within the old 20 m. Nothing ahead:
    //     the junction just behind, as before. Emergency and pedestrian
    //     signals mark nothing (the junction falls to its STOP tags and the
    //     warrants); a blinker is an all-way STOP.
    //   CLUSTERS  Marked nodes joined through joints within ClusterM are one
    //     junction. A node across a short edge joins only where that edge
    //     does not carry the junction's own stop-line tag (the line is ON
    //     that edge: a slip leaving before it stays outside, as it is built).
    //     An arm too short for its line whose far end is a joint takes the
    //     joint in: the line stands on the road behind it.
    //   NO NEW SIGNAL WITHOUT A CROSSING  A junction this round makes
    //     (a walked mark, a joint taken in) must have two roads at it: an
    //     undivided road opening into its carriageways beside a signal whose
    //     side street is not in the game is no junction.
    //     A cluster that cannot be built gives its nodes back to the STOP
    //     tags and the warrants.
    //   STOPS AND YIELDS  A tag on an approach stops the junction its
    //     traffic drives into (the same walk, StopReachM); stop=all on an
    //     approach makes the junction all-way; stop=minor on a node stops
    //     the node's minor road by the THROUGH road (the stem of a T, never
    //     the street that carries on); a STOP's arm too short for its line
    //     stands it behind the joint.
    //   STOP LINES  A signal's line stands where its own directed tag does
    //     (a mapper puts it AT the stop line: 15-40 m back at a wide junction
    //     or where a ramp joins), never nearer than the junction's own line.
    //   LOCAL STREETS (PSX_CITY_LOCALSTOPS=0: left to yield, as before)
    //     Where OSM says nothing at a junction of two local streets: the
    //     stem of a T stops, the side streets of a through street stop, and
    //     a crossing of two equal streets is an all-way STOP. Charlotte's
    //     own tagged junctions split that way: of 130 tagged three-arm local
    //     junctions 94 stop the stem alone, of 130 tagged local crossings 69
    //     are all-way (and where one street stops, the longer street is the
    //     one that goes only 26 times in 54: no rule to pick it).
    // ======================================================================
    public static partial class CitySignals
    {
        /// <summary>PSX_CITY_SIGNALS2=0: the junction controls as they were.</summary>
        public static bool Signals2On = System.Environment.GetEnvironmentVariable("PSX_CITY_SIGNALS2") != "0";
        /// <summary>PSX_CITY_LOCALSTOPS=0: two local streets meeting with no tag are left to yield, as before.</summary>
        public static bool LocalStopsOn = Signals2On && System.Environment.GetEnvironmentVariable("PSX_CITY_LOCALSTOPS") != "0";
        /// <summary>How far ahead of a stop-line signal tag its junction is looked for, along its way.</summary>
        public const float SignalReachM = 45f;
        /// <summary>The same for a STOP or yield tag.</summary>
        public const float StopReachM = 40f;
        /// <summary>The most road taken into a junction through joints (stubs) before an arm's line.</summary>
        public const float StubReachM = 30f;

        public struct Signals2Counts
        {
            public int rowsAtNode, rowsWalked, rowsNear, rowsBehind, rowsNone, emergency, pedestrian, blinker, blinkerAllWay, linesAtTag,
                       chainJoins, joinsHeld, stubsIn, refusedNoCross, refusedArms, released, stopRowsWalked, stopStubs, stopAllWayByRow,
                       stopThroughKept, yieldRowsWalked, localStems, localAllWay;
        }
        public static Signals2Counts S2;
        /// <summary>The signal clusters that were marked and not built, with why (the audit).</summary>
        public static readonly List<string> Sig2Refused = new List<string>();
        /// <summary>The nodes a special signal (emergency, pedestrian, blinker) stands at (the audit): node, kind.</summary>
        public static readonly List<(int node, int kind, int edge, float s)> Sig2Special = new List<(int, int, int, float)>();

        public static string Signals2Report()
        {
            var k = S2;
            if (!Signals2On) return "signals round 2 OFF (PSX_CITY_SIGNALS2=0)";
            return $"signals round 2: tag rows at a junction {k.rowsAtNode}, walked to the junction ahead {k.rowsWalked}, the nearer junction (no direction) {k.rowsNear}, the junction behind {k.rowsBehind}, none in reach {k.rowsNone}; " +
                   $"not junction signals: emergency {k.emergency}, pedestrian {k.pedestrian}, blinker {k.blinker} ({k.blinkerAllWay} all-way STOP); " +
                   $"clusters: joined through joints {k.chainJoins}, a node kept out by the stop line's own edge {k.joinsHeld}, joints taken in for a line {k.stubsIn}, stop lines at their tag {k.linesAtTag}, refused {k.refusedArms} (arms / approaches) + {k.refusedNoCross} (one road, no crossing), {k.released} nodes given back; " +
                   $"STOP rows walked {k.stopRowsWalked}, lines behind a joint {k.stopStubs}, all-way by an approach's tag {k.stopAllWayByRow}, through roads kept moving {k.stopThroughKept}, yield rows walked {k.yieldRowsWalked}; " +
                   $"local streets {(LocalStopsOn ? "on" : "OFF")}: stems / side streets stopped {k.localStems}, all-way {k.localAllWay}";
        }

        /// <summary>charlotte_sigtags.bytes (tools/city/signal_tags.mjs): bits 0-1 the direction
        /// (1 forward, 2 backward, 3 both), bits 4-6 the kind (0 a junction's signal, 1 emergency,
        /// 2 pedestrian crossing, 3 blinker). Signal and give-way nodes.</summary>
        static Dictionary<ulong, byte> LoadSignalTags()
        {
            var d = new Dictionary<ulong, byte>();
            var ta = Resources.Load<TextAsset>("charlotte_sigtags");
            if (ta == null) { Debug.LogWarning("[City] Resources/charlotte_sigtags.bytes missing (node tools/city/signal_tags.mjs) - signal tags are read without their kind and direction."); return d; }
            var b = ta.bytes;
            if (b.Length < 12 || b[0] != 'P' || b[1] != 'S' || b[2] != 'G' || b[3] != 'T') { Debug.LogError("[City] charlotte_sigtags.bytes: bad magic"); return d; }
            int n = System.BitConverter.ToInt32(b, 8);
            for (int i = 0; i < n && 12 + i * 9 + 9 <= b.Length; i++)
            {
                int p = 12 + i * 9;
                ulong id = System.BitConverter.ToUInt32(b, p) | ((ulong)System.BitConverter.ToUInt32(b, p + 4) << 32);
                d[id] = b[p + 8];
            }
            Resources.UnloadAsset(ta);
            return d;
        }

        /// <summary>
        /// From (edge, s) the way its traffic goes (dir +1 toward b, -1 toward
        /// a), through joints (two arms) and carriageway splits (a road
        /// opening into its two one-ways: no junction), to the first junction
        /// within <paramref name="reach"/>: the node, the edge it is entered
        /// by, the distance driven.
        /// </summary>
        static bool WalkToJunction(CityMap map, int ei, float s, int dir, float reach, out int node, out int arrive, out float dist)
        {
            var e = map.edges[ei];
            dist = dir > 0 ? e.length - s : s;
            node = dir > 0 ? e.b : e.a;
            arrive = ei;
            if (e.a == e.b) return false;
            for (int hop = 0; hop < 8; hop++)
            {
                if (dist > reach) return false;
                int deg = Degree(map, node), next = -1;
                if (deg >= 3)
                {
                    if (deg == 3 && SplitArms(map, node, out int tw, out int ia, out int oa))
                        next = e.index == tw ? oa : e.index == ia ? tw : -1;
                    if (next < 0) return true;
                }
                else
                {
                    foreach (int oi in map.nodeEdges[node])
                    {
                        var o = map.edges[oi];
                        if (oi == e.index || o.a == o.b) continue;
                        next = next < 0 ? oi : -2;
                    }
                    if (next < 0) return false;
                }
                var ne = map.edges[next];
                bool fwd = ne.a == node;
                if (ne.oneway && !fwd) return false;            // it arrives here: nobody drives on into it
                e = ne; arrive = next;
                dist += ne.length;
                node = fwd ? ne.b : ne.a;
            }
            return false;
        }

        /// <summary>A STOP or yield tag at (edge, s): the junction its traffic drives into - dir +1 / -1
        /// by its tag or its one-way, within <see cref="StopReachM"/>; 0 (no direction on a two-way
        /// road): the nearer junction either way within <see cref="StopSnapM"/> - and the edge it arrives by.</summary>
        static bool WalkTag(CityMap map, int ei, float s, int dir, out int node, out int arrive)
        {
            if (dir != 0) return WalkToJunction(map, ei, s, dir, StopReachM, out node, out arrive, out _);
            bool ga = WalkToJunction(map, ei, s, -1, StopSnapM, out int na, out int ea, out float da);
            bool gb = WalkToJunction(map, ei, s, 1, StopSnapM, out int nb, out int eb, out float db);
            bool useA = ga && (!gb || da <= db);
            node = useA ? na : nb; arrive = useA ? ea : eb;
            return ga || gb;
        }

        static readonly List<int> chainScratch = new List<int>(8);
        /// <summary>The signal tags' flags of the map being decided (null: round 2 off).</summary>
        static Dictionary<ulong, byte> sigTagsFor;

        /// <summary>The arm e at node n is too short to stand its own stop line (and is no
        /// tunnel, roundabout, freeway or junction-cluster piece: those stand none for other reasons).</summary>
        static bool ArmTooShort(CityMeshes.Trims trims, CityMap.Edge e, int n)
        {
            if (e.a == e.b || e.tunnel || e.roundabout || trims.Internal(e.index) || (e.cls >= 5 && !e.link)) return false;
            if (e.length < 6f) return true;
            bool atB = e.b == n;
            float trim = trims.TrimAt(e, n);
            float sMouth = atB ? e.length - trim : trim;
            float sStop = atB ? sMouth - StopBackM : sMouth + StopBackM;
            return sStop < 1f || sStop > e.length - 1f;
        }

        /// <summary>The rule as it was (the A/B's before, and what this round's marks are compared with).</summary>
        static void MarkSignalsOld(CityMap map, bool[] sig)
        {
            int nn = map.nodes.Length;
            if (map.nodeControl != null)
                for (int i = 0; i < nn; i++) if (map.nodeControl[i] == 4) sig[i] = true;
            foreach (var t in map.tagged)
            {
                if (t.kind != 4 || t.edge < 0 || t.edge >= map.edges.Length) continue;
                var e = map.edges[t.edge];
                float d0 = t.s, d1 = e.length - t.s;
                if (Mathf.Min(d0, d1) > SignalSnapM) continue;    // mid-block: a crossing's, not a junction's
                sig[d0 <= d1 ? e.a : e.b] = true;
            }
            for (int i = 0; i < nn; i++) if (sig[i] && Degree(map, i) < 3) sig[i] = false;
        }

        /// <summary>
        /// The nodes the signal tags mark, by each tag's own way and direction
        /// (the header). <paramref name="lineEdge"/>: the edges a stop-line tag
        /// lies on, walked straight to the node at its end, and <paramref name="lineBack"/> how
        /// far before that node it stands (by (edge << 1) | toward b); <paramref name="blinkers"/>:
        /// the flashing beacons' junction nodes; <paramref name="fresh"/>: marked
        /// here and not by the rule as it was.
        /// </summary>
        static void MarkSignals2(CityMap map, bool[] sig, bool[] lineEdge, float[] lineBack, List<int> blinkers, bool[] fresh)
        {
            int nn = map.nodes.Length;
            var tags = LoadSignalTags();
            sigTagsFor = tags;
            var special = new bool[nn];
            var seenId = new HashSet<ulong>();
            S2 = default; Sig2Refused.Clear(); Sig2Special.Clear();
            foreach (var t in map.tagged)
            {
                if (t.kind != 4 || t.edge < 0 || t.edge >= map.edges.Length) continue;
                var e = map.edges[t.edge];
                tags.TryGetValue(t.nodeId, out byte f);
                int kind = (f >> 4) & 7, dirTag = f & 3;
                float d0 = t.s, d1 = e.length - t.s;
                int near = d0 <= d1 ? e.a : e.b;
                bool atEnd = Mathf.Min(d0, d1) < 1.5f;
                if (kind != 0)
                {
                    // not a junction's signal: a fire station's, a crossing's own, a flashing beacon
                    if (atEnd) special[near] = true;
                    if (seenId.Add(t.nodeId))
                    {
                        if (kind == 1) S2.emergency++; else if (kind == 2) S2.pedestrian++; else S2.blinker++;
                        Sig2Special.Add((near, kind, t.edge, t.s));
                    }
                    if (kind == 3 && atEnd && Degree(map, near) >= 3 && !blinkers.Contains(near)) blinkers.Add(near);
                    continue;
                }
                if (atEnd && Degree(map, near) >= 3) { sig[near] = true; S2.rowsAtNode++; continue; }
                int dir = e.oneway ? 1 : dirTag == 1 ? 1 : dirTag == 2 ? -1 : 0;
                if (dir != 0)
                {
                    if (WalkToJunction(map, t.edge, t.s, dir, SignalReachM, out int n, out int arrive, out _))
                    {
                        sig[n] = true; S2.rowsWalked++;
                        if (arrive == t.edge)
                        {
                            // the tag stands AT the stop line: the line's own place on this arm
                            lineEdge[t.edge] = true;
                            int lk = (t.edge << 1) | (dir > 0 ? 1 : 0);
                            float back = dir > 0 ? e.length - t.s : t.s;
                            if (lineBack[lk] <= 0f || back < lineBack[lk]) lineBack[lk] = back;
                        }
                        continue;
                    }
                }
                else
                {
                    bool ga = WalkToJunction(map, t.edge, t.s, -1, SignalSnapM, out int na, out _, out float da);
                    bool gb = WalkToJunction(map, t.edge, t.s, 1, SignalSnapM, out int nb, out _, out float db);
                    if (ga || gb) { sig[ga && (!gb || da <= db) ? na : nb] = true; S2.rowsNear++; continue; }
                }
                // nothing ahead in reach: the junction just behind it, as the rule was
                if (Mathf.Min(d0, d1) <= SignalSnapM && Degree(map, near) >= 3) { sig[near] = true; S2.rowsBehind++; }
                else S2.rowsNone++;
            }
            if (map.nodeControl != null)
                for (int i = 0; i < nn; i++) if (map.nodeControl[i] == 4 && !special[i]) sig[i] = true;
            for (int i = 0; i < nn; i++) if (sig[i] && Degree(map, i) < 3) sig[i] = false;
            var old = new bool[nn];
            MarkSignalsOld(map, old);
            for (int i = 0; i < nn; i++) fresh[i] = sig[i] && !old[i];
        }

        /// <summary>The roads at a cluster: its outside arms' names (a ramp is "ramp", an unnamed
        /// street its own) - two at least where traffic crosses.</summary>
        static int RoadsAt(CityMap map, List<int> ns, int[] inCluster, int root)
        {
            roadScratch.Clear();
            foreach (int n in ns)
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    int o = e.a == n ? e.b : e.a;
                    if (inCluster[o] == root) continue;
                    roadScratch.Add(e.link ? "#ramp" : string.IsNullOrEmpty(e.name) ? "#" + ei : e.name);
                }
            return roadScratch.Count;
        }
        static readonly HashSet<string> roadScratch = new HashSet<string>();

        /// <summary>Why the arm e stands no stop line at node n (the audit's REFUSED lines): "" = it does.</summary>
        static string ApproachWhyNot(CityMap map, CityMeshes.Trims trims, CityMap.Edge e, int n)
        {
            if (e.tunnel) return "tunnel";
            if (e.roundabout) return "roundabout";
            if (trims.Internal(e.index)) return "inside a junction cluster";
            if (e.cls >= 5 && !e.link) return "freeway";
            if (e.oneway && e.a == n) return "leaves";
            if (e.length < 6f) return "under 6 m";
            bool atB = e.b == n;
            float trim = trims.TrimAt(e, n);
            float sMouth = atB ? e.length - trim : trim;
            float sStop = atB ? sMouth - StopBackM : sMouth + StopBackM;
            if (sStop < 1f || sStop > e.length - 1f) return $"no room ({e.length:0} m, trim {trim:0.0})";
            if (e.ElevatedAt(sStop)) return "the line on structure";
            return "";
        }

        static string ArmsWhyNot(CityMap map, CityMeshes.Trims trims, List<int> ns, int[] inCluster, int root)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int n in ns)
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b) continue;
                    int o = e.a == n ? e.b : e.a;
                    if (inCluster[o] == root) continue;
                    string why = ApproachWhyNot(map, trims, e, n);
                    if (why == "leaves") continue;
                    sb.Append($" e{ei} '{(string.IsNullOrEmpty(e.name) ? (e.link ? "ramp" : "unnamed") : e.name)}': {(why.Length == 0 ? "ok" : why)};");
                }
            return sb.ToString();
        }

        static string NamesAt(CityMap map, List<int> ns)
        {
            var names = new SortedSet<string>();
            foreach (int n in ns)
                foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; names.Add(string.IsNullOrEmpty(e.name) ? (e.link ? "(ramp)" : "(unnamed)") : e.name); }
            return string.Join(" x ", names);
        }
    }
}
