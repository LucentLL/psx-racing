using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE SPLITS ROUND (J1, 2026-11-16). The owner on Beatties Ford Road at
    /// night in rain (PC45): "If the city and roads are to 1:1 scale, why are
    /// there so many anomalies like this abrupt medians that force the road to
    /// meet at strange angles without stop signs." The scale is 1:1; the line
    /// model drew OSM's split geometry badly: every carriageway leaving a split
    /// node kept OSM's 20-40 degree departure and was offset 5.5-7.3 m further
    /// out on top of it (an S, then a 100-200 m drift back), short islands drew
    /// as 7-13 m grass lenses, and a turn bay that serves no street in the graph
    /// ran its centre lane into the nose (no hatch: the pair reverted to live).
    ///
    /// Switches (all read once; the medians gate flips SplitAxis in-process):
    ///   PSX_CITY_SPLITAXIS  0   = PC45 drawing exactly;
    ///                       bfr = Beatties Ford Road's 19 split-type nodes
    ///                             (the default, the owner's four spots);
    ///                       all = every split node city-wide (measure and
    ///                             report; not the default);
    ///                       city = PHASE C (splits2, 2026-11-16 afternoon):
    ///                             every split node city-wide but the held-back
    ///                             ones, with Phase C's rules (CityPhaseC):
    ///                             the blend sized on the own line's whole move
    ///                             and bow, BAYEND only on the run into a nose,
    ///                             the taper floors (PSX_CITY_TAPERFLOOR), the
    ///                             approach marking at same-name meetings.
    ///   PSX_CITY_BAYEND=0   turn bays as OSM tags them (no bay ended).
    ///   PSX_CITY_NOSE=0     PC45 noses: CurbMedian's 12 m set-back, NoseMarks
    ///                       only at accepted refuge noses, every refuge nose
    ///                       hatched (a bay that opens at the island end too).
    /// Everything is keyed to graph b2aa3453 (the node list) except 'all'.
    /// </summary>
    public static partial class LineModel
    {
        // ================================================================
        //  Switches and scope
        // ================================================================
        /// <summary>"0", "bfr" (default), "all" or "city". Not readonly: the medians
        /// gate decides the refuge list for each scope in one process.</summary>
        public static string SplitAxis = SplitAxisOf(System.Environment.GetEnvironmentVariable("PSX_CITY_SPLITAXIS"));
        public static string SplitAxisOf(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return v == "0" || v == "off" || v == "false" ? "0" : v == "all" ? "all" : v == "city" ? "city" : "bfr";
        }
        /// <summary>PHASE C (splits2): the scope 'city' draws every split node with Phase C's rules.
        /// 'all' stays J1's measure exactly (the before of Phase C's numbers).</summary>
        public static bool CityPhaseC => SplitAxis == "city";
        public static bool BayEndOn = System.Environment.GetEnvironmentVariable("PSX_CITY_BAYEND") != "0";
        /// <summary>Phase C: bays on the stretches BAYEND walks whose lane goes on as a lane past one end (the
        /// lane drops J1's rule makes; under city they are kept).</summary>
        public static int PhaseCBayLaneOn;

        /// <summary>Phase C: the lanes a piece carried before BAYEND (dir +1 a->b).</summary>
        static int OrigLanes(CityMap.Edge e, int dir) => bayEndOrig.TryGetValue(e.index, out var o) ? (dir > 0 ? o.nF : o.nB) : (dir > 0 ? e.lsNF : e.lsNB);
        static bool OrigBay(CityMap.Edge e, int dir)
        {
            if (!bayEndOrig.TryGetValue(e.index, out var o)) return BayOf(e, dir);
            int fwd = o.to & 0xFF, bwd = (o.to >> 8) & 0xFF;
            return dir > 0 ? o.nF >= 2 && (fwd & 1) != 0 : o.nB >= 2 && (bwd & 1) != 0;
        }
        /// <summary>Phase C: does the bay lane of direction dir on e go on as a lane past one of its ends - a
        /// 2-arm node where the same road's next piece (not carrying the bay) carries at least as many lanes
        /// that way? Then the bay is OSM's lane, not a turn bay that ends.</summary>
        static bool BayLaneRunsOn(CityMap map, CityMap.Edge e, int dir, out string why)
        {
            // AHEAD only (the way the bay travels), followed along the bay's run to its end, so a run is kept
            // or ended whole: where the run ends at a 2-arm node and the piece beyond carries as many lanes that
            // way, the "bay" is OSM's lane going on (e16705 past n15089). A through lane that BECOMES the bay
            // behind it and runs into a nose (e16690 into n10035) still ends, as J1 has it: kept, it would be a
            // turn-only lane running into the island.
            why = "";
            var cur = e; int d = dir;
            for (int g = 0; g < 16; g++)
            {
                int nd = d > 0 ? cur.b : cur.a;
                var arms = map.nodeEdges[nd];
                if (arms == null || arms.Count != 2) return false;          // a junction, a nose, a dead end: it ends there
                var x = map.edges[arms[0] == cur.index ? arms[1] : arms[0]];
                if (x.oneway || x.a == x.b || x.name != cur.name || !x.hasLset) return false;
                int xd = x.a == nd ? 1 : -1;                                 // x's direction that goes on from cur's
                if (OrigBay(x, xd)) { cur = x; d = xd; continue; }          // the same bay runs on
                int n0 = OrigLanes(cur, d), nx = OrigLanes(x, xd);
                if (nx >= n0) { why = $"past n{nd} (e{x.index} carries {nx} lanes that way, the bay's piece e{cur.index} {n0})"; return true; }
                return false;
            }
            return false;
        }
        public static bool NoseRuleOn = System.Environment.GetEnvironmentVariable("PSX_CITY_NOSE") != "0";
        /// <summary>PSX_CITY_REFUGECHORD=1: a refuge island's axis on the 30 m chord too, the undivided
        /// road's last metres eased onto it (unproven: the medians gate rejects it, see BuildRefugePair).</summary>
        public static bool RefugeChordOn = System.Environment.GetEnvironmentVariable("PSX_CITY_REFUGECHORD") == "1";
        /// <summary>The export the Beatties Ford node list is for.</summary>
        public const uint SplitsGraph = 0xb2aa3453u;
        /// <summary>Beatties Ford Road's split-type nodes (graph b2aa3453): the
        /// 16 SPLT nodes and the three same-name meetings n634, n7868, n9152.</summary>
        public static readonly int[] BfrSplitNodes = { 231, 4025, 7869, 7963, 10035, 10229, 10230, 10231, 10232, 10233, 10234, 10236, 10238, 12438, 13570, 13571, 634, 7868, 9152 };
        static HashSet<int> bfrSplitSet;
        /// <summary>Beatties Ford nodes the bfr scope leaves as PC45 (measured, not drawn): n7963 and
        /// n12438, where the carriageways diverge to wide medians along OSM's own lines (the profile cannot
        /// pass the census there) and the bfr traffic census met 3 wrecks on a solid beside e11521's
        /// shifted lanes (-3696,11302; -3694,11374), off the drive audit's box. The fix round adds the
        /// I-85 interchange's two ramp-terminal splits, n231 (south) and n4025 (north): their
        /// carriageways run onto the deck and its median walls, and the profile pulled the two pavements
        /// over each other (0.36-0.84 m) into the deck-approach median's square wall ends (the concrete
        /// inventory's ORPHAN rail pieces 14 -> 40 on the deck north of n231). They wait until that
        /// median is given a nose of its own.</summary>
        public static readonly HashSet<int> BfrHeldBack = new HashSet<int> { 7963, 12438, 231, 4025 };
        /// <summary>The fix round: Beatties Ford bays BAYEND leaves as OSM tags them under bfr (graph
        /// b2aa3453). e16705's NB bay serves a driveway at n15089, 168 m north of n7869 (whose profile is
        /// declined: PC45's taper) and 203 m before n10035's nose - nowhere near a nose the round draws.
        /// Ended, it left NB cars waiting at n15089 for the lane their straight-on needed (the bfr
        /// traffic census: 6 missed turn lanes and 2 lane-change waits there in 6 minutes, the base
        /// none). e16690's bay into n10035's nose is still ended.</summary>
        public static readonly HashSet<int> BfrBayKeep = new HashSet<int> { 16705 };
        public static bool SplitsOn => SplitAxis != "0";

        // ================================================================
        //  THE ROAD-LINES ROUND (splits2, 2026-11-16 night; PSX_CITY_LINEFIX)
        // ================================================================
        // The owner on PC48, North Tryon Street: the double yellow S-jogs mid-road, a solid white line
        // leaves a lane about 8 m wide, dark stripes run diagonally across the lanes. The classes behind
        // each are fixed in the line model behind this one switch (PSX_CITY_LINEFIX=0: PC48's drawing).
        static readonly string lineFixEnv = System.Environment.GetEnvironmentVariable("PSX_CITY_LINEFIX");
        /// <summary>PSX_CITY_LINEFIX=1 on, =0 off (unset: off until each class is proven).</summary>
        public static bool LineFixOn = lineFixEnv == "1";

        // (Faults 1 + 2 - the centre line relayed a lane sideways, a lane 8 m wide - are the LANE FIT:
        // LineModel.LaneFit.cs. A first rule that inferred "borrowed bays" from the neighbours' splits at run
        // time was withdrawn: without the tags it could not tell a squeezed seven-lane street from two real
        // back-to-back bays, and redrew 51 runs city-wide.)

        /// <summary>Is this split node drawn by the splits round's rules?</summary>
        public static bool InSplitScope(CityMap map, int node)
        {
            if (SplitAxis == "0" || map == null) return false;
            if (SplitAxis == "all") return true;
            // Phase C: every node but the held-back ones (each waits for its own fix)
            if (SplitAxis == "city") return map.graphHash != SplitsGraph || !BfrHeldBack.Contains(node);
            if (map.graphHash != SplitsGraph) return false;
            bfrSplitSet ??= new HashSet<int>(BfrSplitNodes);
            return bfrSplitSet.Contains(node) && !BfrHeldBack.Contains(node);
        }

        /// <summary>The refuge list's scope bit for the current SplitAxis: an
        /// entry carrying any of 16/32/64 is drawn only under the scopes whose
        /// bit it carries (16 = 0, 32 = bfr, 64 = all); none: every scope.</summary>
        public static int SplitScopeBit => ScopeBitOf(SplitAxis);
        /// <summary>A scope's bit in the refuge list (Phase C adds 128 = city).</summary>
        public static int ScopeBitOf(string sc) => sc == "0" ? 16 : sc == "bfr" ? 32 : sc == "city" ? 128 : 64;
        public const int RefugeScopeMask = 16 | 32 | 64 | 128;

        // ================================================================
        //  BAYEND (Phase A): a turn bay that serves no junction of the graph
        // ================================================================
        // OSM's Beatties Ford north of LaSalle St alternates left-turn bays
        // (3+2 / 2+3 / a TWLTL on 20-117 m pieces) for side streets and
        // driveways the city export never fetched (STREETS_BOX). The bay at a
        // refuge nose then runs its centre lane into the island (n10236,
        // n10229), and the bays' relays move the centre lane's lines inside
        // the nose approach, so no hatch fits and the pair reverted to live.
        // Owner decision 3 (recommended default): such a bay ends - its space
        // is the centre lane (a TWLTL), hatched as MUTCD 3B.13's approach at
        // the nose - until the street data brings its street in (J4). A bay
        // that serves a REAL junction (LaSalle's, at n10238) is kept, and the
        // nose lets it open at the island's end (no hatch across its entry).
        // Applied to the line sets of the undivided stretch at each in-scope
        // split nose, from the nose to the first junction, at parse and again
        // at every BuildEases (the gate switches scope in-process).
        static readonly Dictionary<int, (byte nF, byte nB, byte c, ushort to, byte fl)> bayEndOrig = new Dictionary<int, (byte, byte, byte, ushort, byte)>();
        static CityMap bayEndMap;
        static string bayEndScope;
        /// <summary>Nodes whose undivided road's centre lane at the nose is a
        /// bay leaving the nose toward a real junction (it opens at the
        /// island's end: no hatch, the island's tip 0.6 m off its start).</summary>
        static readonly HashSet<int> bayOpenNodes = new HashSet<int>();
        public static readonly List<string> BayEndLog = new List<string>();
        public static bool BayOpensAt(int node) => bayOpenNodes.Count > 0 && bayOpenNodes.Contains(node);
        public static int BayEndEdges => bayEndOrig.Count;
        /// <summary>Where a bay opens at the island's end, the island's tip
        /// stands this far past the node (Iowa DM 6A-11: 0.6 m off the lane).</summary>
        public const float BayOpenTipM = 0.6f;

        /// <summary>Is the innermost lane of a direction (+1 a->b, -1 b->a) a
        /// turn-only bay (CityMeshes.TurnOnly's bit 0: OSM lane 0, the leftmost)?</summary>
        public static bool BayOf(CityMap.Edge e, int dir)
        {
            if (e == null || e.oneway || !e.hasLset || e.a == e.b) return false;
            CityMeshes.TurnOnly(e, out int fwd, out int bwd, out int nF, out int nB, out _);
            return dir > 0 ? nF >= 2 && (fwd & 1) != 0 : nB >= 2 && (bwd & 1) != 0;
        }

        /// <summary>Does the bay of direction dir on e lead to a junction of
        /// the graph (a node with an arm of another name)? Followed through
        /// 2-arm nodes onto pieces that carry the same bay; a bay that stops
        /// at a 2-arm node, a dead end or a plain split nose serves nothing.</summary>
        public static bool BayServesJunction(CityMap map, CityMap.Edge e, int dir, out int served)
        {
            var cur = e; int d = dir; served = -1;
            for (int g = 0; g < 16; g++)
            {
                int nd = d > 0 ? cur.b : cur.a;
                served = nd;
                var arms = map.nodeEdges[nd];
                if (arms == null || arms.Count < 2) return false;
                if (arms.Count >= 3) return !CityMeshes.PlainSplitNose(map, nd, cur);
                int xi = arms[0] == cur.index ? arms[1] : arms[0];
                var x = map.edges[xi];
                if (x.oneway || x.name != cur.name || x.a == x.b) return false;
                int xd = x.a == nd ? 1 : -1;
                if (!BayOf(x, xd)) return false;
                cur = x; d = xd;
            }
            return false;
        }

        static void EndBay(CityMap.Edge e, int dir)
        {
            if (!bayEndOrig.ContainsKey(e.index)) bayEndOrig[e.index] = (e.lsNF, e.lsNB, e.lsCentre, e.lsTurnOnly, e.lsFlags);
            int fwd = e.lsTurnOnly & 0xFF, bwd = (e.lsTurnOnly >> 8) & 0xFF;
            if (dir > 0) { e.lsNF--; fwd >>= 1; e.lsFlags = (byte)(e.lsFlags & ~2); }
            else { e.lsNB--; bwd >>= 1; e.lsFlags = (byte)(e.lsFlags & ~4); }
            e.lsTurnOnly = (ushort)(fwd | (bwd << 8));
            e.lsCentre = 2;
        }

        /// <summary>Restore every line set BAYEND changed, then apply it for the
        /// current scope. Idempotent; cheap (the in-scope noses' stretches).</summary>
        public static void ApplyBayEnds(CityMap map)
        {
            if (bayEndMap == map)
                foreach (var kv in bayEndOrig)
                {
                    var e = map.edges[kv.Key];
                    e.lsNF = kv.Value.nF; e.lsNB = kv.Value.nB; e.lsCentre = kv.Value.c; e.lsTurnOnly = kv.Value.to; e.lsFlags = kv.Value.fl;
                }
            bayEndOrig.Clear(); bayOpenNodes.Clear(); BayEndLog.Clear(); PhaseCBayLaneOn = 0;
            bayEndMap = map; bayEndScope = SplitAxis;
            if (!SplitsOn || map == null || map.splt == null) return;
            var seen = new HashSet<int>();
            foreach (var sp in map.splt)
            {
                if (!InSplitScope(map, sp.node) || sp.u < 0 || sp.u >= map.edges.Length) continue;
                var U = map.edges[sp.u];
                if (U.oneway || !U.hasLset || U.a == U.b) continue;
                // the bay at the nose leaving it toward a real junction: it opens at the island's end
                int away = U.a == sp.node ? 1 : -1;
                if (NoseRuleOn && BayOf(U, away) && BayServesJunction(map, U, away, out int sv))
                {
                    bayOpenNodes.Add(sp.node);
                    if (BayEndLog.Count < 200) BayEndLog.Add($"n{sp.node}: e{U.index} '{U.name}' bay leaves the nose toward junction n{sv}: kept, opens at the island's end");
                }
                if (!BayEndOn) continue;
                // the stretch from the nose to the first junction
                var cur = U; int at = sp.node;
                // Phase C (scope city): a bay whose lane goes on as a lane past either of its ends (a 2-arm node
                // where the piece beyond carries as many lanes that way and not the same bay) is OSM's lane, not
                // a turn bay that ends: J1 ended it too, which dropped or added a lane at that 2-arm node (e16705
                // at n15089: NB cars waited for the lane their straight-on needed; BfrBayKeep held that one
                // edge back). Counted in every scope (PhaseCBayLaneOn: the lane drops J1's rule makes), kept
                // under city. Every other bay on the stretch ends as J1's rule has it (a centre lane that runs
                // into the nose: spot 34's island needs e8620's SB bay ended, or its relay moves the lines the
                // hatch needs).
                for (int g = 0; g < 12; g++)
                {
                    if (!seen.Add(cur.index)) break;
                    if (cur.oneway || !cur.hasLset || cur.name != U.name) break;
                    bool bf = BayOf(cur, 1), bb = BayOf(cur, -1);
                    if (cur.lsCentre == 1 && (bf ^ bb))
                    {
                        int d = bf ? 1 : -1;
                        bool keep = SplitAxis == "bfr" && map.graphHash == SplitsGraph && BfrBayKeep.Contains(cur.index);
                        if (keep && BayEndLog.Count < 200) BayEndLog.Add($"n{sp.node}: e{cur.index} '{cur.name}' {cur.lsNF}+{cur.lsNB} bay {(d > 0 ? "a->b" : "b->a")}: kept as OSM tags it (BfrBayKeep: a driveway's bay far from any nose the round draws)");
                        int sv2 = -1;
                        bool serves = keep || BayServesJunction(map, cur, d, out sv2);
                        string runWhy = "";
                        bool runsOn = !serves && BayLaneRunsOn(map, cur, d, out runWhy);
                        if (runsOn) PhaseCBayLaneOn++;
                        if (runsOn && CityPhaseC)
                        {
                            if (BayEndLog.Count < 200) BayEndLog.Add($"n{sp.node}: e{cur.index} '{cur.name}' {cur.lsNF}+{cur.lsNB} bay {(d > 0 ? "a->b" : "b->a")}: kept as OSM tags it (Phase C: its lane goes on {runWhy})");
                        }
                        else if (!serves)
                        {
                            if (BayEndLog.Count < 200) BayEndLog.Add($"n{sp.node}: e{cur.index} '{cur.name}' {cur.lsNF}+{cur.lsNB} bay {(d > 0 ? "a->b" : "b->a")} serves n{sv2} ({(sv2 >= 0 ? map.nodeEdges[sv2].Count : 0)} arms, no street of the graph): ended, its space the centre lane (TWLTL){(runsOn ? $" - its lane goes on {runWhy}: a lane drop" : "")}");
                            EndBay(cur, d);
                        }
                    }
                    int far = cur.a == at ? cur.b : cur.a;
                    var arms = map.nodeEdges[far];
                    if (arms == null || arms.Count != 2) break;
                    int xi = arms[0] == cur.index ? arms[1] : arms[0];
                    var x = map.edges[xi];
                    if (x.a == x.b || x.oneway || x.name != U.name) break;
                    cur = x; at = far;
                }
            }
        }

        /// <summary>PSX_CITY_SPLITMITRE=1: mitre every in-scope 3-arm SPLT node (the plan's rule; OFF by
        /// default - unproven: where the profile then declines the node the mitred joint kinks).</summary>
        public static bool SplitMitreOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SPLITMITRE") == "1";
        /// <summary>In-scope 3-arm split nodes ComputeTrimsCore mitred (no fan), the last solve.</summary>
        public static int SplitForcedMitre;
        /// <summary>The undivided edge of a SPLT node.</summary>
        public static bool SplitUOf(CityMap map, int node, out int u) => SplitU(map, node, out u);

        /// <summary>The scope BAYEND was last applied for (the audits).</summary>
        public static string BayEndScope => bayEndScope;

        /// <summary>The fix round: two carriageways of one shaped split nose (the axis-frame profile
        /// with a nose) are not a branch and its host. Their pavements meet in the middle of the lane
        /// gap up to the nose and part around the island; the gore painter attached them while the
        /// gap was under 4.5 m and paved the island's first 20 m flat (n10035: no curb until u 46,
        /// a square-ended block). As LineModel.SameRefuge for a refuge island.</summary>
        public static bool SameSplitNose(int a, int b)
        {
            if (splitFrameEdge.Count == 0 || splitNoseNodes.Count == 0) return false;
            var fa = SplitFrameOf(a); var fb = SplitFrameOf(b);
            return fa != null && fb != null && fa != fb && fa.node == fb.node && splitNoseNodes.Contains(fa.node);
        }

        /// <summary>The fix round: is this edge drawn off PC45 by the splits round in the current
        /// scope - a split frame's carriageway, or a refuge pair that the PSX_CITY_SPLITAXIS=0 list
        /// does not draw? The roadside occupancy then reads its ribbon where it is drawn (off its OSM
        /// line) instead of PC45's widest half each side: at spot 34 the straightened carriageway's
        /// kerb kept the old bulge's clear zone on the narrow side and the east pole station (a
        /// cobra-head light) found no foot.</summary>
        public static bool SplitsMovedEdge(int edge)
        {
            if (!SplitsOn) return false;
            if (splitFrameEdge.Count > 0 && splitFrameEdge.ContainsKey(edge)) return true;
            var r = RefugeOf(edge);
            return r != null && !RefugePairPc45(r.pk);
        }
        static HashSet<long> pc45Pairs;
        /// <summary>Is a refuge pair drawn under PSX_CITY_SPLITAXIS=0 (its list entry has scope bit
        /// 16 or no scope bit)?</summary>
        public static bool RefugePairPc45(long pk)
        {
            if (pc45Pairs == null)
            {
                var h = new HashSet<long>();
                for (int i = 0; i + 2 < RefugeListData.Length; i += 3)
                {
                    int md = (int)RefugeListData[i + 2];
                    if ((md & RefugeScopeMask) == 0 || (md & 16) != 0) h.Add(RefugeListData[i]);
                }
                pc45Pairs = h;
            }
            return pc45Pairs.Contains(pk);
        }
    }
}
