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
    ///                             report; not the default).
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
        /// <summary>"0", "bfr" (default) or "all". Not readonly: the medians
        /// gate decides the refuge list for each scope in one process.</summary>
        public static string SplitAxis = SplitAxisOf(System.Environment.GetEnvironmentVariable("PSX_CITY_SPLITAXIS"));
        public static string SplitAxisOf(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return v == "0" || v == "off" || v == "false" ? "0" : v == "all" ? "all" : "bfr";
        }
        public static bool BayEndOn = System.Environment.GetEnvironmentVariable("PSX_CITY_BAYEND") != "0";
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
        public static bool SplitsOn => SplitAxis != "0";

        /// <summary>Is this split node drawn by the splits round's rules?</summary>
        public static bool InSplitScope(CityMap map, int node)
        {
            if (SplitAxis == "0" || map == null) return false;
            if (SplitAxis == "all") return true;
            if (map.graphHash != SplitsGraph) return false;
            bfrSplitSet ??= new HashSet<int>(BfrSplitNodes);
            return bfrSplitSet.Contains(node);
        }

        /// <summary>The refuge list's scope bit for the current SplitAxis: an
        /// entry carrying any of 16/32/64 is drawn only under the scopes whose
        /// bit it carries (16 = 0, 32 = bfr, 64 = all); none: every scope.</summary>
        public static int SplitScopeBit => SplitAxis == "0" ? 16 : SplitAxis == "bfr" ? 32 : 64;
        public const int RefugeScopeMask = 16 | 32 | 64;

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
            bayEndOrig.Clear(); bayOpenNodes.Clear(); BayEndLog.Clear();
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
                for (int g = 0; g < 12; g++)
                {
                    if (!seen.Add(cur.index)) break;
                    if (cur.oneway || !cur.hasLset || cur.name != U.name) break;
                    bool bf = BayOf(cur, 1), bb = BayOf(cur, -1);
                    if (cur.lsCentre == 1 && (bf ^ bb))
                    {
                        int d = bf ? 1 : -1;
                        if (!BayServesJunction(map, cur, d, out int sv2))
                        {
                            if (BayEndLog.Count < 200) BayEndLog.Add($"n{sp.node}: e{cur.index} '{cur.name}' {cur.lsNF}+{cur.lsNB} bay {(d > 0 ? "a->b" : "b->a")} serves n{sv2} ({map.nodeEdges[sv2].Count} arms, no street of the graph): ended, its space the centre lane (TWLTL)");
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
    }
}
