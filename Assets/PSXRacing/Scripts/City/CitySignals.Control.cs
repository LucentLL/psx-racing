using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// ROAD LOGIC: WHO HAS THE WAY, AND THE SIGNALS' PHASES (2026-10-29, the
    /// traffic census's control / signal / crossing-flow / box classes; the
    /// owner: "a lot of strange implementation currently").
    ///
    /// CONTROL, BY SOURCE. OSM first: highway=traffic_signals on the node or
    /// up to <see cref="SignalSnapM"/> before it (each carriageway's node of a
    /// divided road's crossing, joined into one junction), highway=stop (with
    /// charlotte_stops.bytes's stop=all / direction), highway=give_way. Where
    /// OSM says nothing, the MUTCD's rules by road class stand in for the
    /// volume warrants (MUTCD 2B.04-2B.07, 4C; NCDOT practice):
    ///   * a CARRIAGEWAY SPLIT (an undivided road opening into its two
    ///     one-ways, section SPLT, or the same shape by name) is not a
    ///     junction: its flows never cross, nobody yields there (the census's
    ///     "no-control major crossings" were these, 112 of 133);
    ///   * a side road meeting a through road (the pair that carries on,
    ///     <see cref="ThroughArms"/>) STOPs - unless both are local streets
    ///     (the neighbourhood's yield to the through street stays) or it is a
    ///     service drive; a one-way slip or ramp joining at a shallow angle
    ///     YIELDs (a merge, no sign: the traffic's merge rule);
    ///   * an arterial meeting an arterial (primary or above, both roads,
    ///     not a slip) and an off-ramp ending at a two-way arterial are
    ///     SIGNALISED (warrants 1-3: those volumes always meet them);
    ///   * equal roads crossing with no through road: all-way STOP below
    ///     arterials, a signal between arterials.
    /// A node within one short edge of an OSM signal is the signal's: no
    /// warrant there. Warrant junctions are furnished (STOP signs and bars,
    /// heads) like OSM's but leave the road paint as it was
    /// (<see cref="ControlAt"/> answers for OSM's only).
    ///
    /// PHASES. A signal's approaches are grouped by AXIS: an approach joins
    /// the axis whose every member it runs along (within 50 degrees as lines)
    /// - opposing approaches share a phase, crossing ones never; a junction
    /// with three roads crossing at odd angles gets three phases. Two signal
    /// junctions joined by a short carriageway (a wide median, a diamond's two
    /// terminals) run as ONE controller: one plan, one clock offset, the axes
    /// over both. A PLAN is pretimed (1999): a green per phase by its roads'
    /// class (the main road's longest), lengthened where crosswalks cross the
    /// other roads (7 s walk + the crossing at 1.07 m/s, MUTCD 4E.06); a
    /// yellow from the approach speed (ITE: 1 s + v / 2a, a = 3.05 m/s2,
    /// 3-5.5 s) and an all-red from the box's width ((W + 6 m) / v, 1-3 s).
    /// A CHANNEL is one (plan, phase, offset): one row of the lens texture,
    /// what every head of that phase at every junction on that plan shows.
    /// </summary>
    public static partial class CitySignals
    {
        /// <summary>PSX_CITY_WARRANTS=0: OSM's controls only (the A/B).</summary>
        public static bool WarrantsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_WARRANTS") != "0";
        /// <summary>PSX_CITY_LEGACY_PHASES=1: the old two-group clock (the A/B's before).</summary>
        public static bool LegacyPhases = System.Environment.GetEnvironmentVariable("PSX_CITY_LEGACY_PHASES") == "1";

        /// <summary>Forget the junctions: the next ask decides them again (a
        /// tool flipping <see cref="WarrantsOn"/> or <see cref="LegacyPhases"/>;
        /// built tiles keep what they stood up).</summary>
        public static void Invalidate() { mapFor = null; trimsFor = null; junctions = null; nodeJunction = null; }

        /// <summary>The assignment's counts (the census and the log).</summary>
        public struct ControlCounts
        {
            public int osmSignals, osmStopJ, osmAllWay, osmYieldJ, warrantSignals, warrantSignalNodes, warrantAllWay,
                       warrantStopJ, warrantStopApp, yieldMerges, splits, splitSides, localLocal, nearSignal, plans, channels,
                       phases2, phases3, phases4, controllersLinked, axesCapped;
        }
        public static ControlCounts Counts;

        // ==================================================================
        //  THE GRAPH'S FACTS
        // ==================================================================

        /// <summary>An edge traffic may use (not a parking aisle, driveway or
        /// unpaved track; CityTraffic's routable).</summary>
        static bool Usable(CityMap.Edge e) =>
            e.a != e.b && e.length > 0.5f && !(e.hasLset && (e.lsSub >= 5 || (e.lsFlags & 32) != 0));

        static bool Freeway(CityMap.Edge e) => e.cls >= 5 && !e.link;

        /// <summary>The way out of node n along edge e.</summary>
        static Vector2 Out(CityMap.Edge e, int n) => e.a == n ? e.TangentAt(0f) : -e.TangentAt(e.length);

        static int RankOf(CityMap.Edge e) => e.link ? 2 : e.cls * 2 + 1;

        static Dictionary<int, LineModel.Split> splitAt;
        static CityMap splitMap;

        /// <summary>
        /// A CARRIAGEWAY SPLIT at node n: an undivided road opening into its
        /// two one-way carriageways (section SPLT; or, unlisted, a two-way arm
        /// and an arriving and a leaving one-way of its name on its far side).
        /// Not a junction: arriving on the two-way goes on into the leaving
        /// carriageway, the arriving carriageway into the two-way, and the two
        /// never cross. Any other arm at the node is a side road.
        /// </summary>
        public static bool SplitArms(CityMap map, int n, out int twoWay, out int inArm, out int outArm)
        {
            twoWay = inArm = outArm = -1;
            if (map == null || n < 0 || n >= map.nodes.Length) return false;
            if (splitMap != map)
            {
                splitMap = map;
                splitAt = new Dictionary<int, LineModel.Split>();
                if (map.splt != null) foreach (var s in map.splt) splitAt[s.node] = s;
            }
            if (splitAt.TryGetValue(n, out var sp))
            {
                twoWay = sp.u; outArm = sp.a; inArm = sp.b;
                return true;
            }
            foreach (int ti in map.nodeEdges[n])
            {
                var t = map.edges[ti];
                if (t.oneway || t.link || !Usable(t) || string.IsNullOrEmpty(t.name)) continue;
                int i = -1, o = -1, extra = 0;
                var back = -Out(t, n);
                foreach (int ei in map.nodeEdges[n])
                {
                    if (ei == ti) continue;
                    var e = map.edges[ei];
                    if (!Usable(e) || !e.oneway || e.link || e.name != t.name) continue;
                    if (Vector2.Angle(Out(e, n), back) > 60f) { extra++; continue; }
                    if (e.b == n) { if (i < 0) i = ei; else extra++; }
                    else { if (o < 0) o = ei; else extra++; }
                }
                if (i >= 0 && o >= 0 && extra == 0) { twoWay = ti; inArm = i; outArm = o; return true; }
            }
            // the same shape under two names (a route's ref as the carriageways'
            // name: "NC 51" opening out of Pineville-Matthews Road): one class,
            // both carriageways within 35 degrees of the two-way's line
            foreach (int ti in map.nodeEdges[n])
            {
                var t = map.edges[ti];
                if (t.oneway || t.link || !Usable(t)) continue;
                int i = -1, o = -1, extra = 0;
                var back = -Out(t, n);
                foreach (int ei in map.nodeEdges[n])
                {
                    if (ei == ti) continue;
                    var e = map.edges[ei];
                    if (!Usable(e) || !e.oneway || e.link || e.cls != t.cls) continue;
                    if (Vector2.Angle(Out(e, n), back) > 35f) continue;
                    if (e.b == n) { if (i < 0) i = ei; else extra++; }
                    else { if (o < 0) o = ei; else extra++; }
                }
                if (i >= 0 && o >= 0 && extra == 0) { twoWay = ti; inArm = i; outArm = o; return true; }
            }
            return false;
        }

        /// <summary>
        /// The THROUGH ROAD at node n: the two arms that carry on from each
        /// other (within 40 degrees, or 80 under one name - a road that bends
        /// at a T) with the highest rank between them, one of the node's top
        /// rank; at a split its two-way arm and arriving carriageway (the
        /// leaving one is through too). None where that is ambiguous (two
        /// pairs of one rank: equal roads crossing). CityTraffic.MajorAt's
        /// rule, which reads this.
        /// </summary>
        public static void ThroughArms(CityMap map, int n, out int m1, out int m2)
        {
            m1 = m2 = -1;
            if (SplitArms(map, n, out int tw, out int ia, out _)) { m1 = tw; m2 = ia; return; }
            var arms = map.nodeEdges[n];
            int top = -1, topN = 0, topArm = -1;
            foreach (int ei in arms)
            {
                var e = map.edges[ei];
                if (!Usable(e)) continue;
                int r = RankOf(e);
                if (r > top) { top = r; topN = 1; topArm = ei; }
                else if (r == top) topN++;
            }
            if (top < 0) return;
            int bestSum = -1, pairs = 0, namedPairs = 0;
            float bestAng = 999f;
            bool bestNamed = false;
            for (int i = 0; i < arms.Count; i++)
            {
                var a = map.edges[arms[i]];
                if (!Usable(a)) continue;
                for (int j = i + 1; j < arms.Count; j++)
                {
                    var b = map.edges[arms[j]];
                    if (arms[j] == arms[i] || !Usable(b)) continue;
                    if (RankOf(a) != top && RankOf(b) != top) continue;
                    bool named = !string.IsNullOrEmpty(a.name) && a.name == b.name;
                    float ang = Vector2.Angle(-Out(a, n), Out(b, n));
                    if (ang > (named ? 80f : 40f)) continue;
                    int sum = RankOf(a) + RankOf(b);
                    if (sum > bestSum) { bestSum = sum; pairs = 1; namedPairs = named ? 1 : 0; bestAng = ang; bestNamed = named; m1 = arms[i]; m2 = arms[j]; }
                    else if (sum == bestSum)
                    {
                        pairs++;
                        if (named) namedPairs++;
                        if ((named && !bestNamed) || (named == bestNamed && ang < bestAng)) { bestAng = ang; bestNamed = named; m1 = arms[i]; m2 = arms[j]; }
                    }
                }
            }
            if (pairs == 1 || (bestNamed && namedPairs == 1)) return;
            m1 = m2 = -1;
            if (pairs == 0 && topN == 1) m1 = topArm;
        }

        // ==================================================================
        //  THE WARRANTS (where OSM says nothing)
        // ==================================================================

        /// <summary>What the warrants decided at one node: the approaches that
        /// STOP (all-way when <c>allWay</c>), or a signal.</summary>
        struct Decision { public int node; public bool signal, allWay; public List<int> stops; }

        static readonly List<int> armScratch = new List<int>(8);

        /// <summary>
        /// The warrant decisions for every node OSM leaves uncontrolled
        /// (<paramref name="taken"/>: signal members and OSM stop / yield
        /// nodes), in node order.
        /// </summary>
        static List<Decision> Warrants(CityMap map, CityMeshes.Trims trims, bool[] taken, bool[] sigMember)
        {
            var list = new List<Decision>();
            int nn = map.nodes.Length;
            for (int n = 0; n < nn; n++)
            {
                if (taken[n]) continue;
                if (trims != null && trims.mitre != null && n < trims.mitre.Length && trims.mitre[n]) continue;
                armScratch.Clear();
                bool freeway = false, round = false;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (!Usable(e)) continue;
                    if (Freeway(e)) freeway = true;
                    if (e.roundabout) round = true;
                    armScratch.Add(ei);
                }
                if (armScratch.Count < 3 || freeway || round) continue;
                // within one short edge of a signal: the signal's (its other
                // carriageway, a slip at its corner) - no sign of our own there
                bool nearSig = false;
                foreach (int ei in armScratch)
                {
                    var e = map.edges[ei];
                    int o = e.a == n ? e.b : e.a;
                    if (e.length <= 45f && sigMember[o]) { nearSig = true; break; }
                }
                if (nearSig) { Counts.nearSignal++; continue; }

                bool split = SplitArms(map, n, out int tw, out int ia, out int oa);
                ThroughArms(map, n, out int m1, out int m2);
                if (split && armScratch.Count == 3) { Counts.splits++; continue; }
                if (split) Counts.splitSides++;

                if (m1 < 0 || m2 < 0)
                {
                    // NO THROUGH ROAD: equal roads crossing
                    int inb = 0, outb = 0, nonLinkIn = 0, firstIn = -1;
                    bool crossIn = false;
                    foreach (int ei in armScratch)
                    {
                        var e = map.edges[ei];
                        if (!e.oneway || e.b == n)
                        {
                            inb++; if (!e.link) nonLinkIn++;
                            // two inbound flows that cross (a one-way carriageway over another)
                            if (firstIn < 0) firstIn = ei;
                            else if (Vector2.Angle(Out(map.edges[firstIn], n), Out(e, n)) > 45f && map.edges[firstIn].name != e.name) crossIn = true;
                        }
                        if (!e.oneway || e.a == n) outb++;
                    }
                    // a merge, a diverge, ramps: no crossing to control
                    if (outb <= 1 || nonLinkIn < 2 || inb < 2 || (inb == 2 && !crossIn)) continue;
                    // the roads: by name (an unnamed arm is its own), each its top class
                    int r1 = -1, r2 = -1; string n1 = null;
                    var roads = new Dictionary<string, int>();
                    foreach (int ei in armScratch)
                    {
                        var e = map.edges[ei];
                        string key = string.IsNullOrEmpty(e.name) ? "#" + ei : e.name;
                        int r = e.link ? 2 : e.cls;
                        roads[key] = roads.TryGetValue(key, out int had) ? Mathf.Max(had, r) : r;
                    }
                    if (roads.Count < 2) continue;                             // one road bending at a branch
                    foreach (var kv in roads)
                    {
                        if (kv.Value > r1) { r2 = r1; r1 = kv.Value; n1 = kv.Key; }
                        else if (kv.Value > r2) r2 = kv.Value;
                    }
                    // two arterials, or two divided (multi-lane) roads: a signal
                    bool multi = true;
                    foreach (int ei in armScratch)
                    {
                        var e = map.edges[ei];
                        if (e.link || (e.oneway && e.a == n)) continue;
                        if (!(e.oneway ? e.lanes >= 2 : e.lanes >= 4)) multi = false;
                    }
                    if (r2 >= 3 || (r2 >= 2 && multi)) { list.Add(new Decision { node = n, signal = true }); continue; }
                    if (r1 < 1) { Counts.localLocal++; continue; }
                    var all = new List<int>();
                    foreach (int ei in armScratch)
                    {
                        var e = map.edges[ei];
                        if (!e.oneway || e.b == n) all.Add(ei);
                    }
                    list.Add(new Decision { node = n, allWay = true, stops = all });
                    continue;
                }

                // A THROUGH ROAD: the side roads STOP (or a signal, or a merge's yield)
                var A = map.edges[m1]; var B = map.edges[m2];
                int thrCls = Mathf.Min(A.link ? 2 : A.cls, B.link ? 2 : B.cls);
                bool thrTwoWay = !A.oneway || !B.oneway;
                Vector2 tA = -Out(A, n), tB = -Out(B, n);       // the through road's travel, both ways
                List<int> stops = null;
                bool signal = false;
                foreach (int ei in armScratch)
                {
                    if (ei == m1 || ei == m2 || (split && (ei == tw || ei == ia || ei == oa))) continue;
                    var m = map.edges[ei];
                    if (m.oneway && m.a == n) continue;                        // it leaves: no approach
                    if (trims != null && trims.Internal(ei)) continue;
                    if (m.hasLset && m.lsSub == 4) continue;                   // a service drive
                    var tm = -Out(m, n);
                    bool shallow = Vector2.Angle(tm, tA) < 50f || Vector2.Angle(tm, tB) < 50f;
                    if (m.oneway && (m.link || string.IsNullOrEmpty(m.name)) && shallow) { Counts.yieldMerges++; continue; }
                    if (m.link)
                    {
                        if (thrCls >= 3 && thrTwoWay && !split) signal = true;
                        else (stops ??= new List<int>()).Add(ei);
                        continue;
                    }
                    if (thrCls >= 3 && m.cls >= 3 && !string.IsNullOrEmpty(m.name) && m.name != A.name && m.name != B.name) { signal = true; continue; }
                    if (thrCls == 0 && m.cls == 0) { Counts.localLocal++; continue; }
                    (stops ??= new List<int>()).Add(ei);
                }
                if (signal) list.Add(new Decision { node = n, signal = true });
                else if (stops != null) list.Add(new Decision { node = n, stops = stops });
            }
            return list;
        }

        // ==================================================================
        //  THE PLANS AND THE CLOCK'S CHANNELS
        // ==================================================================

        /// <summary>A pretimed plan: phase k's green, then the yellow and the
        /// all-red, phase after phase.</summary>
        public sealed class Plan
        {
            public float[] green;
            public float yellow, allRed, cycle;
            public string key;
        }

        /// <summary>One row of the lens texture: a phase of a plan at an
        /// offset - its green's start in the cycle, its green, its yellow.</summary>
        struct Channel { public int plan, phase; public float cycle, start, green, yellow, offset; }

        static readonly List<Plan> plans = new List<Plan>();
        static readonly Dictionary<string, int> planIndex = new Dictionary<string, int>();
        static readonly List<Channel> chans = new List<Channel>();
        static readonly Dictionary<long, int> chanIndex = new Dictionary<long, int>();

        /// <summary>The plans (the tools).</summary>
        public static IReadOnlyList<Plan> Plans => plans;
        public static int ChannelCount => chans.Count;

        /// <summary>Channels 0 and 1: the old two-phase clock (14 s green,
        /// 3.5 yellow, 1.5 all-red each), for a group number read as one.</summary>
        static void ResetPlans()
        {
            plans.Clear(); planIndex.Clear(); chans.Clear(); chanIndex.Clear();
            int p = PlanOf(new[] { GreenS, GreenS }, YellowS, AllRedS);
            ChannelOf(p, 0, 0); ChannelOf(p, 1, 0);
        }

        static int PlanOf(float[] green, float yellow, float allRed)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var g in green) sb.Append(g.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append('/');
            sb.Append('y').Append(yellow.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append('r').Append(allRed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            string key = sb.ToString();
            if (planIndex.TryGetValue(key, out int i)) return i;
            float c = 0f;
            foreach (var g in green) c += g + yellow + allRed;
            plans.Add(new Plan { green = (float[])green.Clone(), yellow = yellow, allRed = allRed, cycle = c, key = key });
            planIndex[key] = plans.Count - 1;
            return plans.Count - 1;
        }

        /// <summary>The channel of phase k of plan p at offset bucket (0-3, a
        /// quarter cycle each).</summary>
        static int ChannelOf(int p, int phase, int bucket)
        {
            long key = ((long)p << 16) | ((long)phase << 4) | (long)bucket;
            if (chanIndex.TryGetValue(key, out int i)) return i;
            var pl = plans[p];
            float start = 0f;
            for (int k = 0; k < phase && k < pl.green.Length; k++) start += pl.green[k] + pl.yellow + pl.allRed;
            chans.Add(new Channel
            {
                plan = p, phase = phase, cycle = pl.cycle, start = start,
                green = phase < pl.green.Length ? pl.green[phase] : 0f, yellow = pl.yellow, offset = pl.cycle * 0.25f * bucket,
            });
            chanIndex[key] = chans.Count - 1;
            return chans.Count - 1;
        }

        /// <summary>Seconds since channel ch's green began at clock time t
        /// (negative: it is not green).</summary>
        public static float GreenAge(int ch, double t)
        {
            if (ch < 0 || ch >= chans.Count) return -1f;
            var c = chans[ch];
            double u = (t + c.offset - c.start) % c.cycle;
            if (u < 0) u += c.cycle;
            return u < c.green ? (float)u : -1f;
        }

        /// <summary>How long channel ch's red lasts (seconds, the cycle less its
        /// green and yellow).</summary>
        public static float RedS(int ch) => ch < 0 || ch >= chans.Count ? CycleS - GreenS - YellowS : chans[ch].cycle - chans[ch].green - chans[ch].yellow;

        /// <summary>A clock time at which channel ch has been green
        /// <paramref name="into"/> seconds (the tools' photographs).</summary>
        public static double GreenTime(int ch, float into = 6f)
        {
            if (ch < 0 || ch >= chans.Count) return into;
            var c = chans[ch];
            double t = c.start - c.offset + Mathf.Min(into, Mathf.Max(0.5f, c.green - 0.5f));
            while (t < 0) t += c.cycle;
            return t;
        }

        /// <summary>A clock time at which channel ch shows yellow (the tools).</summary>
        public static double YellowTime(int ch)
        {
            if (ch < 0 || ch >= chans.Count) return GreenS + 1.0;
            var c = chans[ch];
            double t = c.start - c.offset + c.green + c.yellow * 0.5f;
            while (t < 0) t += c.cycle;
            return t;
        }

        // ==================================================================
        //  PLANNING THE SIGNALS (after every junction is built)
        // ==================================================================

        /// <summary>Two signal junctions joined by an edge this short (not a
        /// freeway) run as one controller - pairs only.</summary>
        public const float LinkM = 70f;
        /// <summary>An approach runs along an axis when it is within this of
        /// every approach already on it (as lines).</summary>
        public const float AxisDeg = 50f;

        static float Importance(CityMap map, Approach a)
        {
            var e = map.edges[a.edge];
            return (e.link ? 4f : e.cls * 16f) + e.lanes + (e.oneway ? 0f : 0.5f);
        }

        /// <summary>
        /// Every signal junction's phases, plan and channels: controllers
        /// (a junction, or two joined by a short carriageway), axes over a
        /// controller's approaches, a plan from its roads and box, an offset
        /// by hash. Deterministic.
        /// </summary>
        static void PlanSignals(CityMap map)
        {
            ResetPlans();
            var sigJ = new List<Junction>();
            foreach (var j in junctions) if (j.signal) sigJ.Add(j);
            if (LegacyPhases)
            {
                // THE OLD RULE (the before of an A/B): the biggest approach's
                // axis and whatever runs within 45 degrees of it one group, the
                // rest the other, half the junctions starting on the other
                foreach (var j in sigJ)
                {
                    int best = 0;
                    for (int i = 1; i < j.approaches.Count; i++)
                    {
                        var ei = map.edges[j.approaches[i].edge]; var eb = map.edges[j.approaches[best].edge];
                        if (ei.cls * 16 + ei.lanes > eb.cls * 16 + eb.lanes) best = i;
                    }
                    var axis = j.approaches[best].u;
                    byte flip = (byte)(Hash01(j.nodes[0], 0, 41) < 0.5f ? 1 : 0);
                    for (int i = 0; i < j.approaches.Count; i++)
                    {
                        var a = j.approaches[i];
                        a.group = (byte)((Mathf.Abs(Vector2.Dot(a.u, axis)) >= 0.7071f ? 0 : 1) ^ flip);
                        a.channel = a.group;
                        j.approaches[i] = a;
                    }
                    j.plan = 0; j.phases = 2; j.controller = j.id;
                }
                Counts.plans = plans.Count; Counts.channels = chans.Count;
                return;
            }
            var jOfNode = new Dictionary<int, int>();
            foreach (var j in sigJ) foreach (int n in j.nodes) jOfNode[n] = j.id;
            // pairs joined by a short carriageway: each its single nearest partner
            var partner = new Dictionary<int, (int other, float len)>();
            foreach (var j in sigJ)
                foreach (int n in j.nodes)
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b || e.length > LinkM || Freeway(e)) continue;
                        int o = e.a == n ? e.b : e.a;
                        if (!jOfNode.TryGetValue(o, out int k) || k == j.id) continue;
                        if (!partner.TryGetValue(j.id, out var p) || e.length < p.len) partner[j.id] = (k, e.length);
                    }
            var ctrlOf = new Dictionary<int, int>();
            foreach (var j in sigJ)
            {
                if (ctrlOf.ContainsKey(j.id)) continue;
                ctrlOf[j.id] = j.id;
                if (partner.TryGetValue(j.id, out var p) && !ctrlOf.ContainsKey(p.other) &&
                    partner.TryGetValue(p.other, out var q) && q.other == j.id)
                {
                    ctrlOf[p.other] = j.id;
                    Counts.controllersLinked++;
                }
            }
            var members = new Dictionary<int, List<Junction>>();
            foreach (var j in sigJ)
            {
                int c = ctrlOf[j.id];
                if (!members.TryGetValue(c, out var l)) members[c] = l = new List<Junction>(2);
                l.Add(j);
            }
            var keys = new List<int>(members.Keys);
            keys.Sort();
            var refs = new List<(Junction j, int i)>(16);
            var axes = new List<List<int>>(4);
            var axisW = new List<float>(4);
            foreach (int c in keys)
            {
                var js = members[c];
                refs.Clear();
                foreach (var j in js) for (int i = 0; i < j.approaches.Count; i++) refs.Add((j, i));
                // the approaches by importance (then by edge, for a fixed order)
                refs.Sort((x, y) =>
                {
                    float ix = Importance(map, x.j.approaches[x.i]), iy = Importance(map, y.j.approaches[y.i]);
                    if (ix != iy) return iy.CompareTo(ix);
                    return x.j.approaches[x.i].edge.CompareTo(y.j.approaches[y.i].edge);
                });
                axes.Clear(); axisW.Clear();
                float cosAxis = Mathf.Cos(AxisDeg * Mathf.Deg2Rad);
                var axisOf = new int[refs.Count];
                for (int r = 0; r < refs.Count; r++)
                {
                    var a = refs[r].j.approaches[refs[r].i];
                    int best = -1; float bestMin = -1f;
                    for (int x = 0; x < axes.Count; x++)
                    {
                        float mn = 1f;
                        foreach (int q in axes[x]) mn = Mathf.Min(mn, Mathf.Abs(Vector2.Dot(a.u, refs[q].j.approaches[refs[q].i].u)));
                        if (mn >= cosAxis && mn > bestMin) { bestMin = mn; best = x; }
                    }
                    if (best < 0 && axes.Count >= 4)
                    {
                        // a fifth road: onto the axis it runs nearest (counted)
                        Counts.axesCapped++;
                        for (int x = 0; x < axes.Count; x++)
                        {
                            float mx = 0f;
                            foreach (int q in axes[x]) mx = Mathf.Max(mx, Mathf.Abs(Vector2.Dot(a.u, refs[q].j.approaches[refs[q].i].u)));
                            if (mx > bestMin) { bestMin = mx; best = x; }
                        }
                    }
                    if (best < 0) { axes.Add(new List<int>(4)); axisW.Add(0f); best = axes.Count - 1; }
                    axes[best].Add(r);
                    axisW[best] += Importance(map, a);
                    axisOf[r] = best;
                }
                // the axes in order of their roads' weight: axis 0 the main road
                var order = new int[axes.Count];
                for (int x = 0; x < order.Length; x++) order[x] = x;
                System.Array.Sort(order, (x, y) => axisW[x] != axisW[y] ? axisW[y].CompareTo(axisW[x]) : x.CompareTo(y));
                var rankOfAxis = new int[axes.Count];
                for (int k = 0; k < order.Length; k++) rankOfAxis[order[k]] = k;
                int phases = Mathf.Max(2, axes.Count);

                // the plan: speeds, the box, the roads, the crosswalks
                float vMax = 8f, box = 0f;
                bool walks = false;
                var cls = new int[phases]; var widthOf = new float[phases];
                for (int k = 0; k < phases; k++) cls[k] = -1;
                foreach (var (j, i) in refs)
                {
                    var a = j.approaches[i];
                    var e = map.edges[a.edge];
                    vMax = Mathf.Max(vMax, e.speedKmh > 0 ? e.speedKmh / 3.6f : DefaultLimitMps(e.cls));
                    walks |= j.walks;
                }
                for (int r = 0; r < refs.Count; r++)
                {
                    var a = refs[r].j.approaches[refs[r].i];
                    var e = map.edges[a.edge];
                    int k = rankOfAxis[axisOf[r]];
                    cls[k] = Mathf.Max(cls[k], e.link ? 2 : e.cls);
                    widthOf[k] += Mathf.Max(3f, a.inR - a.inL);
                    for (int s = r + 1; s < refs.Count; s++) box = Mathf.Max(box, Vector2.Distance(a.at, refs[s].j.approaches[refs[s].i].at));
                }
                float yellow = Mathf.Clamp(Mathf.Ceil((1f + vMax / (2f * 3.05f)) * 2f) / 2f, 3f, 5.5f);
                // the all-red: the box at the speed a car crosses it (about 0.6 of
                // the limit - the city traffic slows for the turn and the box)
                float allRed = Mathf.Clamp(Mathf.Ceil((box + 6f) / Mathf.Max(6f, 0.6f * vMax) * 2f) / 2f, 1f, 3f);
                var green = new float[phases];
                for (int k = 0; k < phases; k++)
                {
                    int ck = Mathf.Max(0, cls[k]);
                    float g = k == 0 ? MainGreen[Mathf.Min(ck, 5)] : SideGreen[Mathf.Min(ck, 5)];
                    if (cls[k] < 0) g = 8f;                                  // a phase nobody arrives on (a crossing's walk)
                    if (walks)
                    {
                        // the crosswalks over the OTHER phases' roads are walked on this green
                        float cross = 0f;
                        for (int o = 0; o < phases; o++) if (o != k) cross = Mathf.Max(cross, widthOf[o]);
                        g = Mathf.Max(g, 7f + cross / 1.07f - yellow);
                    }
                    green[k] = Mathf.Ceil(g / 2f) * 2f;
                }
                // a cycle past 100 s is scaled back (pedestrian minimums kept where they bind)
                float cyc = 0f;
                for (int k = 0; k < phases; k++) cyc += green[k] + yellow + allRed;
                if (cyc > MaxCycleS)
                {
                    float lost = phases * (yellow + allRed), scale = (MaxCycleS - lost) / Mathf.Max(1f, cyc - lost);
                    for (int k = 0; k < phases; k++) green[k] = Mathf.Max(8f, Mathf.Ceil(green[k] * scale / 2f) * 2f);
                }
                int plan = PlanOf(green, yellow, allRed);
                int bucket = (int)(Hash01(c, 7, 113) * 4f) & 3;
                if (phases == 2) Counts.phases2++; else if (phases == 3) Counts.phases3++; else Counts.phases4++;
                for (int r = 0; r < refs.Count; r++)
                {
                    var (j, i) = refs[r];
                    var a = j.approaches[i];
                    a.group = (byte)rankOfAxis[axisOf[r]];
                    a.channel = (ushort)ChannelOf(plan, a.group, bucket);
                    j.approaches[i] = a;
                }
                foreach (var j in js) { j.plan = plan; j.phases = (byte)phases; j.controller = c; }
            }
            Counts.plans = plans.Count;
            Counts.channels = chans.Count;
        }

        /// <summary>Green by the phase's top road class (local .. freeway
        /// ramps), the main road's and a side road's (seconds).</summary>
        static readonly float[] MainGreen = { 16f, 20f, 26f, 32f, 38f, 30f };
        /// <summary>The longest cycle a plan runs (seconds).</summary>
        public const float MaxCycleS = 100f;
        static readonly float[] SideGreen = { 12f, 14f, 18f, 22f, 26f, 20f };

        static float DefaultLimitMps(int cls)
        {
            switch (cls)
            {
                case 0: return 11.2f;
                case 1: return 15.6f;
                case 2: return 17.9f;
                case 3: return 20.1f;
                case 4: return 24.6f;
                default: return 29.1f;
            }
        }

        /// <summary>One line on the assignment and the plans (the log, the census).</summary>
        public static string ControlReport()
        {
            var k = Counts;
            return $"controls: OSM signals {k.osmSignals} junctions, OSM STOP {k.osmStopJ} ({k.osmAllWay} all-way), OSM yield {k.osmYieldJ}; " +
                   $"warrants (OSM silent): signals {k.warrantSignals} ({k.warrantSignalNodes} nodes), all-way STOP {k.warrantAllWay}, two-way STOP {k.warrantStopJ} junctions ({k.warrantStopApp} approaches), " +
                   $"merges left to yield {k.yieldMerges}, local x local left to yield {k.localLocal}, next to a signal {k.nearSignal}; carriageway splits (not junctions) {k.splits} (+{k.splitSides} with a side road); " +
                   $"signal plans {k.plans}, channels {k.channels}, phases 2/3/4: {k.phases2}/{k.phases3}/{k.phases4}, linked controllers {k.controllersLinked}, roads past 4 axes {k.axesCapped}";
        }
    }
}
