using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE TWIN-DECK TABLE (2026-10-02, plan B1; Docs/CHARLOTTE.md "B1").
    ///
    /// The owner's example: West 5th Street over I-77 is ONE bridge with a
    /// median, and Charlotte drew it as two decks 1.5 m apart with four
    /// parapets and a slot down to the freeway. OSM maps each carriageway as
    /// its own way, so nothing said the two decks were one structure. This
    /// table says it: every pair of parallel decks, and whether they are one.
    /// Built once at load (<see cref="Build"/>, from the graph alone, before
    /// the elevation solve) into <see cref="CityMap.deckPairs"/>; read by the
    /// solve (one height for a pair: <see cref="CityElevation"/>'s
    /// HoldTwinDecks and AlignTwinStructure, and the land under the gap), by
    /// the TWIN report (Editor/CityAudit.Twin.cs) and by the mesh (plan A2:
    /// one slab, outer parapets, a median).
    ///
    /// THE RULE, step for step the same as tools/city/deckpairs.mjs (the
    /// offline census; the TWIN report writes this table's CSV and the tool's
    /// --compare checks the two agree):
    ///   DECKS (critic C1): a bridge=yes edge end to end, an over-edge within
    ///     <see cref="CityElevation.DeckReach"/> of its crossing, a water span.
    ///   PAIRS: every <see cref="Step"/> m of deck, the nearest deck on each
    ///     side running within 15 degrees either way, its foot inside that
    ///     edge's deck, the gap between the two ribbons' facing edges
    ///     (lmPlus / lmMinus: the line model's extents) under <see cref="GapMax"/>;
    ///     kept when one edge sees the other for <see cref="OverlapMin"/> m.
    ///     The gap is the median sample.
    ///   ONE STRUCTURE (owner_decisions.md, 2026-10-02):
    ///     gap <= 0.3 m         no: the squeeze or the clip joins them already
    ///     another OSM layer    no
    ///     an override          FORCE / NEVER by way pair (BRST; Q7)
    ///     a culvert's creek    no: owner Q8 - OSM says the creek is piped
    ///                          (B2 builds a culvert); left exactly as it is
    ///     outlines (BRST)      the same man_made=bridge outline: yes to 20 m;
    ///                          two different outlines: never
    ///     else G               one road's two carriageways: 6.1 m motorway or
    ///                          trunk, 9.1 m arterial; a ramp beside its road
    ///                          or two ramps: 3.05 m; any other pair 1.2 m
    /// </summary>
    public static class DeckPairs
    {
        public const float Step = 2f, GapMax = 20f, OverlapMin = 10f, SqueezeM = 0.3f;
        public const float GOutline = 20f, GDualFreeway = 6.1f, GDualArterial = 9.1f, GRamp = 3.05f, GOther = 1.2f;
        const float Cell = 32f;
        static readonly float CosMin = Mathf.Cos(15f * Mathf.Deg2Rad);

        public enum Kind : byte { Dual, OpposedOther, SameDirection, RampMainline, RampRamp }
        /// <summary>The median a union draws (plan A2; owner_decisions.md):
        /// a 0.81 m Jersey on motorway, trunk and ramp pairs and arterials over
        /// 80 km/h, 0.10 m mountable curbs on other arterials, flush paint when
        /// the gap is under 1.2 m.</summary>
        public enum Median : byte { None, Flush, Raised, Barrier }

        /// <summary>One pair of parallel decks.</summary>
        public sealed class Pair
        {
            /// <summary>The two edges, <c>a &lt; b</c>.</summary>
            public int a, b;
            public Kind kind;
            /// <summary>The higher class of the two (0 local .. 5 motorway)
            /// and its tier (<see cref="CityTier"/>).</summary>
            public int cls, tier;
            /// <summary>The median gap between the facing ribbon edges, the
            /// smallest, and the metres one edge saw the other.</summary>
            public float gap, gapMin, overlap;
            /// <summary>Where they are beside each other, on each edge (arc).</summary>
            public float a0, a1, b0, b1;
            /// <summary>Which side of each edge the other lies on: +1 the
            /// left of its travel (its R vertex), -1 the right.</summary>
            public int sideA, sideB;
            public uint structA, structB;
            /// <summary>One of the two decks is only a culvert's creek (Q8).</summary>
            public bool culvert;
            /// <summary>Found only on the SOLVED structure (<see cref="Complete"/>):
            /// a deck the solve made that the facts did not name. No height
            /// hold was taken for it.</summary>
            public bool solved;
            /// <summary>One structure? Up to which gap, and why.</summary>
            public bool union;
            public float G;
            public string why;
            public Median median;
            /// <summary>b's arc beside a's, every <see cref="Step"/> m from
            /// <see cref="a0"/>; and a's beside b's from <see cref="b0"/>
            /// (-1: no foot within reach).</summary>
            public float[] mapAB, mapBA;

            public int Other(int e) => e == a ? b : a;
            public int SideOf(int e) => e == a ? sideA : sideB;
            public float From(int e) => e == a ? a0 : b0;
            public float To(int e) => e == a ? a1 : b1;

            /// <summary>The other edge's arc beside arc <paramref name="s"/>
            /// of edge <paramref name="e"/> (within the overlap, plus a
            /// <see cref="Step"/>), or -1.</summary>
            public float ArcOnOther(int e, float s)
            {
                var m = e == a ? mapAB : mapBA;
                float s0 = From(e);
                if (m == null || m.Length == 0) return -1f;
                float k = (s - s0) / Step;
                if (k < -1f || k > m.Length) return -1f;
                int i = Mathf.Clamp(Mathf.FloorToInt(k), 0, m.Length - 1);
                int j = Mathf.Min(i + 1, m.Length - 1);
                if (m[i] < 0f || m[j] < 0f) return m[i] >= 0f ? m[i] : m[j];
                return Mathf.Lerp(m[i], m[j], Mathf.Clamp01(k - i));
            }
        }

        /// <summary>What the last <see cref="Build"/> cost and found, for the
        /// audit and the budget probe.</summary>
        public static float LastBuildMs { get; private set; }
        public static string LastReport { get; private set; } = "";

        // the decks by edge (geometric: what the solve will put on structure
        // from the facts), as intervals with their source
        const byte SrcBridge = 1, SrcCrossing = 2, SrcWater = 4, SrcCulvert = 8, SrcSolved = 16;
        static List<(float s0, float s1, byte src)>[] decks;

        /// <summary>Which deck sources hold arc <paramref name="s"/> of the
        /// edge (0: none; 8 alone: only a culvert's creek), from the last
        /// <see cref="Build"/>.</summary>
        public static int DeckSourcesAt(int edge, float s)
        {
            if (decks == null || edge < 0 || edge >= decks.Length || decks[edge] == null) return 0;
            int m = 0;
            foreach (var (s0, s1, src) in decks[edge]) if (s >= s0 - 1e-4f && s <= s1 + 1e-4f) m |= src;
            return m;
        }

        /// <summary>Is arc <paramref name="s"/> of the edge a deck the solve
        /// will put on structure from the facts (a bridge, a crossing's deck,
        /// a water span)? Before the solve's last-resort margin decks.</summary>
        public static bool DeckAt(int edge, float s) => DeckSourcesAt(edge, s) != 0;

        public static void Build(CityMap map)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var E = map.edges;
            int ne = E.Length;
            decks = new List<(float, float, byte)>[ne];
            void Add(int ei, float s0, float s1, byte src)
            {
                var e = E[ei];
                s0 = Mathf.Clamp(s0, 0f, e.length); s1 = Mathf.Clamp(s1, 0f, e.length);
                (decks[ei] ??= new List<(float, float, byte)>(2)).Add((s0, s1, src));
            }
            foreach (var e in E) if (e.bridge) Add(e.index, 0f, e.length, SrcBridge);
            foreach (var c in map.crossings)
            {
                var over = E[c.over]; var under = E[c.under];
                CityElevation.ProjectOn(over, c.at, out float sO);
                CityElevation.ProjectOn(under, c.at, out float sU);
                float r = CityElevation.DeckReach(over, under, sO, sU);
                Add(c.over, sO - r, sO + r, SrcCrossing);
            }
            for (int k = 0; k < map.wspans.Length; k++)
            {
                var w = map.wspans[k];
                bool culvert = map.culvertSpan != null && k < map.culvertSpan.Length && map.culvertSpan[k];
                Add(w.edge, w.s0, w.s1, culvert ? SrcCulvert : SrcWater);
            }

            var facts = Census(map, (ei, s) => DeckSourcesAt(ei, s), out int samples);
            foreach (var pr in facts) pr.mapAB = pr.mapBA = null;
            Publish(map, facts);
            LastBuildMs = (float)clock.Elapsed.TotalMilliseconds;
            LastReport = $"{samples} deck samples, {Summary(map)}, {LastBuildMs:0} ms";
        }

        /// <summary>
        /// THE TABLE COMPLETED ON THE SOLVED STRUCTURE (critic C1: the
        /// candidates are every span <see cref="CityMap.Edge.ElevatedAt"/>
        /// draws as deck). Run once by <see cref="CityElevation.Solve"/>
        /// after it marks structure: the solve also makes decks the facts did
        /// not name - the 3.5 m margin's untagged viaducts, a seated ramp
        /// taking its host's deck, a deck end carried to its twin's, and the
        /// station either side of every deck - and two of those side by side
        /// are drawn as two railed decks like any other. The census runs
        /// again over what is on structure now: a pair the facts already
        /// knew keeps its decision and its height hold, and only grows its
        /// range; a pair only the solve made is decided by the same rule
        /// (<see cref="Pair.solved"/>: it took no hold, so a union of two
        /// decks at different heights ends where the mesh's limit says).
        /// </summary>
        public static void Complete(CityMap map)
        {
            if (map.deckPairs == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var E = map.edges;
            var solved = Census(map, (ei, s) => E[ei].ElevatedAt(s) ? (DeckSourcesAt(ei, s) | SrcSolved) : 0, out int samples);
            var byKey = new Dictionary<long, Pair>(map.deckPairs.Length);
            foreach (var pr in map.deckPairs) byKey[((long)pr.a << 32) | (uint)pr.b] = pr;
            var all = new List<Pair>(map.deckPairs);
            int added = 0, grown = 0;
            foreach (var sp in solved)
            {
                long key = ((long)sp.a << 32) | (uint)sp.b;
                if (byKey.TryGetValue(key, out var fp))
                {
                    // the facts' pair: its decision and its numbers stand; the
                    // range is what both censuses saw
                    float a0 = Mathf.Min(fp.a0, sp.a0), a1 = Mathf.Max(fp.a1, sp.a1), b0 = Mathf.Min(fp.b0, sp.b0), b1 = Mathf.Max(fp.b1, sp.b1);
                    if (a0 < fp.a0 - 0.01f || a1 > fp.a1 + 0.01f || b0 < fp.b0 - 0.01f || b1 > fp.b1 + 0.01f) { grown++; fp.mapAB = fp.mapBA = null; }
                    fp.a0 = a0; fp.a1 = a1; fp.b0 = b0; fp.b1 = b1;
                    continue;
                }
                sp.solved = true;
                sp.why = "solved structure: " + sp.why;
                all.Add(sp);
                added++;
            }
            all.Sort((p, q) => p.a != q.a ? p.a.CompareTo(q.a) : p.b.CompareTo(q.b));
            Publish(map, all);
            LastCompleteMs = (float)clock.Elapsed.TotalMilliseconds;
            LastReport += $"; on the solved structure ({samples} samples): {added} more pairs, {grown} ranges grown -> {Summary(map)}, {LastCompleteMs:0} ms";
        }

        public static float LastCompleteMs { get; private set; }

        static string Summary(CityMap map)
        {
            int unions = 0, t1 = 0, t2 = 0, kept = 0, solvedU = 0; float metres = 0f;
            foreach (var pr in map.deckPairs)
            {
                if (pr.union) { unions++; metres += pr.overlap; if (pr.tier == 1) t1++; else if (pr.tier == 2) t2++; if (pr.solved) solvedU++; }
                else if (pr.why.StartsWith("culvert") && WouldUnion(map, pr)) kept++;
            }
            return $"{map.deckPairs.Length} parallel pairs, {unions} one structure (T1 {t1}, T2 {t2}; {metres:0} m{(solvedU > 0 ? $"; {solvedU} on decks only the solve made" : "")}), {kept} culvert creeks kept apart (Q8)";
        }

        /// <summary>The table into the map: the arc maps (where missing) and
        /// each edge's pairs and unions.</summary>
        static void Publish(CityMap map, List<Pair> pairs)
        {
            var E = map.edges; int ne = E.Length;
            foreach (var pr in pairs)
            {
                if (pr.mapAB == null) pr.mapAB = ArcMap(E[pr.a], E[pr.b], pr.a0, pr.a1, pr.gap);
                if (pr.mapBA == null) pr.mapBA = ArcMap(E[pr.b], E[pr.a], pr.b0, pr.b1, pr.gap);
            }
            map.deckPairs = pairs.ToArray();
            map.deckPairsOf = new int[ne][];
            map.deckUnionsOf = new int[ne][];
            var tmp = new List<int>[ne];
            var tmpU = new List<int>[ne];
            for (int i = 0; i < map.deckPairs.Length; i++)
            {
                var pr = map.deckPairs[i];
                (tmp[pr.a] ??= new List<int>(2)).Add(i);
                (tmp[pr.b] ??= new List<int>(2)).Add(i);
                if (!pr.union) continue;
                (tmpU[pr.a] ??= new List<int>(2)).Add(i);
                (tmpU[pr.b] ??= new List<int>(2)).Add(i);
            }
            for (int i = 0; i < ne; i++)
            {
                if (tmp[i] != null) map.deckPairsOf[i] = tmp[i].ToArray();
                if (tmpU[i] != null) map.deckUnionsOf[i] = tmpU[i].ToArray();
            }
        }

        /// <summary>
        /// THE CENSUS (the rule in the class summary): every <see cref="Step"/>
        /// m of every deck (<paramref name="srcAt"/>: which deck sources hold
        /// an arc of an edge, 0 none), the nearest deck on each side, summed
        /// per pair, decided. The same steps as tools/city/deckpairs.mjs.
        /// </summary>
        static List<Pair> Census(CityMap map, System.Func<int, float, int> srcAt, out int samples)
        {
            var E = map.edges;
            int ne = E.Length;
            var isDeck = new bool[ne];
            for (int ei = 0; ei < ne; ei++)
            {
                var e = E[ei];
                if (decks[ei] != null) { isDeck[ei] = true; continue; }
                if (e.stElev == null) continue;
                foreach (bool b in e.stElev) if (b) { isDeck[ei] = srcAt(ei, e.length * 0.5f) != 0 || AnyDeck(e, ei, srcAt); break; }
            }
            // the deck edges' segments on a 32 m grid, in edge then segment
            // order, as flat arrays (the census asks ~45,000 points)
            var grid = new Dictionary<long, List<int>>();
            long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
            var sgEdge = new List<int>(8192); var sgA = new List<Vector2>(8192); var sgD = new List<Vector2>(8192);
            var sgT = new List<Vector2>(8192); var sgS0 = new List<float>(8192); var sgL = new List<float>(8192); var sgInvL2 = new List<float>(8192);
            for (int ei = 0; ei < ne; ei++)
            {
                if (!isDeck[ei]) continue;
                var p = E[ei].pts;
                for (int k = 0; k + 1 < p.Length; k++)
                {
                    Vector2 d = p[k + 1] - p[k];
                    float L2 = d.sqrMagnitude;
                    if (L2 < 1e-6f) continue;
                    float L = Mathf.Sqrt(L2);
                    int id = sgEdge.Count;
                    sgEdge.Add(ei); sgA.Add(p[k]); sgD.Add(d); sgT.Add(d / L); sgS0.Add(E[ei].s[k]); sgL.Add(L); sgInvL2.Add(1f / L2);
                    int i0 = Mathf.FloorToInt(Mathf.Min(p[k].x, p[k + 1].x) / Cell), i1 = Mathf.FloorToInt(Mathf.Max(p[k].x, p[k + 1].x) / Cell);
                    int j0 = Mathf.FloorToInt(Mathf.Min(p[k].y, p[k + 1].y) / Cell), j1 = Mathf.FloorToInt(Mathf.Max(p[k].y, p[k + 1].y) / Cell);
                    for (int i = i0; i <= i1; i++)
                        for (int j = j0; j <= j1; j++)
                        {
                            long kk = Key(i, j);
                            if (!grid.TryGetValue(kk, out var l)) grid[kk] = l = new List<int>(8);
                            l.Add(id);
                        }
                }
            }

            var acc = new Dictionary<long, Acc>();
            var order = new List<long>();
            // the nearest foot on each other edge, per sample: stamped arrays
            // (the tool's insertion-ordered map, without a map)
            var stamp = new int[ne]; int stampN = 0;
            var nearD = new float[ne]; var nearAt = new float[ne];
            var nearQ = new Vector2[ne]; var nearT = new Vector2[ne];
            var seen = new List<int>(32);
            samples = 0;
            const float R = GapMax + 30f;
            for (int ei = 0; ei < ne; ei++)
            {
                if (!isDeck[ei]) continue;
                var e = E[ei];
                for (float s = Step * 0.5f; s < e.length; s += Step)
                {
                    int src = srcAt(ei, s);
                    if (src == 0) continue;
                    samples++;
                    var x = e.PointAt(s); var t = e.TangentAt(s);
                    seen.Clear(); stampN++;
                    for (int i = Mathf.FloorToInt((x.x - R) / Cell); i <= Mathf.FloorToInt((x.x + R) / Cell); i++)
                        for (int j = Mathf.FloorToInt((x.y - R) / Cell); j <= Mathf.FloorToInt((x.y + R) / Cell); j++)
                        {
                            if (!grid.TryGetValue(Key(i, j), out var l)) continue;
                            for (int li = 0; li < l.Count; li++)
                            {
                                int id = l[li];
                                int oi = sgEdge[id];
                                if (oi == ei) continue;
                                var to = sgT[id];
                                float c = to.x * t.x + to.y * t.y;
                                if (c < CosMin && -c < CosMin) continue;
                                Vector2 a = sgA[id], d = sgD[id];
                                float u = ((x.x - a.x) * d.x + (x.y - a.y) * d.y) * sgInvL2[id];
                                if (u < 0f || u > 1f) continue;
                                var q = new Vector2(a.x + d.x * u, a.y + d.y * u);
                                float dist = Vector2.Distance(x, q);
                                if (stamp[oi] != stampN)
                                {
                                    stamp[oi] = stampN; seen.Add(oi);
                                    nearD[oi] = dist; nearAt[oi] = sgS0[id] + sgL[id] * u; nearQ[oi] = q; nearT[oi] = to;
                                }
                                else if (dist < nearD[oi]) { nearD[oi] = dist; nearAt[oi] = sgS0[id] + sgL[id] * u; nearQ[oi] = q; nearT[oi] = to; }
                            }
                        }
                    // the nearest deck on each side
                    int bestL = -1, bestR = -1;
                    float gapL = 0f, gapR = 0f;
                    int srcL = 0, srcR = 0;
                    for (int si = 0; si < seen.Count; si++)
                    {
                        int oi = seen[si];
                        var o = E[oi];
                        int srcO = srcAt(oi, nearAt[oi]);
                        if (srcO == 0) continue;
                        int side = SideOfPoint(x, nearQ[oi], t), sideO = SideOfPoint(nearQ[oi], x, nearT[oi]);
                        float gap = nearD[oi] - Facing(e, side) - Facing(o, sideO);
                        if (gap >= GapMax) continue;
                        if (side > 0) { if (bestL < 0 || gap < gapL) { bestL = oi; gapL = gap; srcL = srcO; } }
                        else { if (bestR < 0 || gap < gapR) { bestR = oi; gapR = gap; srcR = srcO; } }
                    }
                    for (int sd = 0; sd < 2; sd++)
                    {
                        int oi = sd == 0 ? bestR : bestL;   // -1 first, as the tool does
                        if (oi < 0) continue;
                        int side = sd == 0 ? -1 : 1;
                        float gap = sd == 0 ? gapR : gapL;
                        int srcO = sd == 0 ? srcR : srcL;
                        int lo = Mathf.Min(ei, oi), hi = Mathf.Max(ei, oi);
                        long key = ((long)lo << 32) | (uint)hi;
                        if (!acc.TryGetValue(key, out var P))
                        {
                            acc[key] = P = new Acc { a = lo, b = hi, cos = Vector2.Dot(nearT[oi], t) };
                            order.Add(key);
                        }
                        P.gaps.Add(gap);
                        if (ei == lo) { P.fromA += Step; P.sideSumA += side; } else { P.fromB += Step; P.sideSumB += side; }
                        P.total++;
                        // only a culvert's creek holds the deck there (Q8)
                        if ((src & ~SrcSolved) == SrcCulvert) { if (ei == lo) P.culA++; else P.culB++; }
                        if ((srcO & ~SrcSolved) == SrcCulvert) { if (oi == lo) P.culA++; else P.culB++; }
                        P.Grow(ei == lo, s); P.Grow(oi == lo, nearAt[oi]);
                    }
                }
            }

            // the decisions
            var pairs = new List<Pair>(order.Count);
            foreach (long key in order)
            {
                var P = acc[key];
                float overlap = Mathf.Max(P.fromA, P.fromB);
                if (overlap < OverlapMin) continue;
                var A = E[P.a]; var B = E[P.b];
                P.gaps.Sort();
                var hiE = A.cls >= B.cls ? A : B;
                var pr = new Pair
                {
                    a = P.a, b = P.b, cls = hiE.cls, tier = CityTier.Of(hiE),
                    kind = KindOf(A, B, P.cos),
                    gap = P.gaps[P.gaps.Count >> 1], gapMin = P.gaps[0], overlap = overlap,
                    a0 = P.a0, a1 = P.a1, b0 = P.b0, b1 = P.b1,
                    structA = A.structId, structB = B.structId,
                    culvert = P.culA * 2 > P.total || P.culB * 2 > P.total,
                };
                pr.sideA = P.sideSumA != 0 ? (P.sideSumA > 0 ? 1 : -1) : SideAcross(B, A);
                pr.sideB = P.sideSumB != 0 ? (P.sideSumB > 0 ? 1 : -1) : SideAcross(A, B);
                Decide(map, pr, A, B);
                pr.median = MedianOf(pr, A, B);
                pairs.Add(pr);
            }
            pairs.Sort((p, q) => p.a != q.a ? p.a.CompareTo(q.a) : p.b.CompareTo(q.b));
            return pairs;
        }

        static bool AnyDeck(CityMap.Edge e, int ei, System.Func<int, float, int> srcAt)
        {
            for (float s = Step * 0.5f; s < e.length; s += Step) if (srcAt(ei, s) != 0) return true;
            return false;
        }

        /// <summary>The decision, given the census (see the class summary).</summary>
        static void Decide(CityMap map, Pair pr, CityMap.Edge A, CityMap.Edge B)
        {
            pr.union = false; pr.G = 0f;
            if (pr.gap <= SqueezeM) { pr.why = "squeeze/clip (<= 0.3 m: one rail or one surface already)"; return; }
            if (A.layer != B.layer) { pr.why = "another OSM layer"; return; }
            if (map.deckOverrides != null && map.deckOverrides.TryGetValue(WayKey(A.wayId, B.wayId), out bool force))
            {
                pr.union = force; pr.G = force ? GOutline : 0f;
                pr.why = force ? "override FORCE" : "override NEVER";
                return;
            }
            if (pr.culvert) { pr.why = "culvert creek (owner Q8: B2 builds a culvert; no union)"; return; }
            if (pr.structA != 0 && pr.structB != 0)
            {
                if (pr.structA == pr.structB) { pr.G = GOutline; pr.union = pr.gap <= GOutline; pr.why = pr.union ? "outline: ONE" : "outline: ONE (gap past 20 m)"; }
                else pr.why = "outline: TWO";
                return;
            }
            bool fwy = pr.cls >= 4;
            pr.G = pr.kind == Kind.Dual ? (fwy ? GDualFreeway : GDualArterial)
                 : pr.kind == Kind.RampMainline || pr.kind == Kind.RampRamp ? GRamp : GOther;
            pr.union = pr.gap <= pr.G;
            pr.why = "rule G=" + pr.G.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Would this pair be one structure but for the culvert (Q8)?</summary>
        public static bool WouldUnion(CityMap map, Pair pr)
        {
            if (!pr.culvert) return pr.union;
            var probe = new Pair { a = pr.a, b = pr.b, kind = pr.kind, cls = pr.cls, gap = pr.gap, structA = pr.structA, structB = pr.structB };
            Decide(map, probe, map.edges[pr.a], map.edges[pr.b]);
            return probe.union;
        }

        static Median MedianOf(Pair pr, CityMap.Edge A, CityMap.Edge B)
        {
            if (!pr.union) return Median.None;
            if (pr.gap < 1.2f) return Median.Flush;
            bool fwy = pr.cls >= 4 || pr.kind == Kind.RampMainline || pr.kind == Kind.RampRamp;
            if (fwy || Mathf.Max(A.speedKmh, B.speedKmh) > 80) return Median.Barrier;
            return Median.Raised;
        }

        /// <summary>The steepest height difference a union of this median
        /// keeps between its two inner edges (plan A2): flush 5 cm, mountable
        /// curbs 15 cm, a split-level Jersey 46 cm.</summary>
        public static float DyLimit(Median m) => m == Median.Barrier ? 0.46f : m == Median.Raised ? 0.15f : 0.05f;

        static Kind KindOf(CityMap.Edge A, CityMap.Edge B, float cos)
        {
            if (A.link || B.link) return A.link && B.link ? Kind.RampRamp : Kind.RampMainline;
            if (cos < 0f) return !string.IsNullOrEmpty(A.name) && A.name == B.name ? Kind.Dual : Kind.OpposedOther;
            return Kind.SameDirection;
        }

        static float Facing(CityMap.Edge e, int side) => side > 0 ? e.lmPlus : e.lmMinus;

        /// <summary>+1 when <paramref name="q"/> is on the left of direction
        /// <paramref name="t"/> from <paramref name="p"/>, else -1.</summary>
        static int SideOfPoint(Vector2 p, Vector2 q, Vector2 t) => ((q.x - p.x) * -t.y + (q.y - p.y) * t.x) >= 0f ? 1 : -1;

        /// <summary>Which side of <paramref name="of"/> the middle of
        /// <paramref name="other"/> lies on (+1 left).</summary>
        static int SideAcross(CityMap.Edge other, CityMap.Edge of)
        {
            var m = other.PointAt(other.length * 0.5f);
            CityElevation.ProjectOn(of, m, out float s);
            return SideOfPoint(of.PointAt(s), m, of.TangentAt(s));
        }

        static float[] ArcMap(CityMap.Edge e, CityMap.Edge o, float s0, float s1, float gap)
        {
            int n = Mathf.Max(1, Mathf.FloorToInt((s1 - s0) / Step) + 1);
            var m = new float[n];
            float reach = e.HalfMax + o.HalfMax + Mathf.Max(0f, gap) + 6f;
            for (int k = 0; k < n; k++)
            {
                float s = Mathf.Min(s1, s0 + k * Step);
                var p = e.PointAt(s);
                CityElevation.ProjectOn(o, p, out float at);
                m[k] = Vector2.Distance(o.PointAt(at), p) <= reach ? at : -1f;
            }
            return m;
        }

        public static long WayKey(uint a, uint b) => a < b ? ((long)a << 32) | b : ((long)b << 32) | a;

        sealed class Acc
        {
            public int a, b, total, culA, culB, sideSumA, sideSumB;
            public float cos, fromA, fromB;
            public float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
            public readonly List<float> gaps = new List<float>(16);
            public void Grow(bool onA, float v)
            {
                if (onA) { if (v < a0) a0 = v; if (v > a1) a1 = v; }
                else { if (v < b0) b0 = v; if (v > b1) b1 = v; }
            }
        }

        // ------------------------------------------------------------------
        //  For the land: the gap a union's slab will span (plan A2) is capped
        //  under the soffit too (CityElevation.Ground).
        // ------------------------------------------------------------------

        /// <summary>
        /// How far from edge <paramref name="e"/>'s line on side
        /// <paramref name="sideLeft"/> (+1 the left of its travel) the land
        /// under its deck at arc <paramref name="s"/> is capped for a union:
        /// its pavement edge plus half the median gap plus half a metre, where
        /// a union pair lies on that side and the partner beside it is on
        /// structure too; 0 where no union applies. (Each deck caps its own
        /// half of the gap.)
        /// </summary>
        public static float UnionCapReach(CityMap map, CityMap.Edge e, float s, int sideLeft, float paveEdge)
        {
            var list = map.deckUnionsOf != null ? map.deckUnionsOf[e.index] : null;
            if (list == null) return 0f;
            float best = 0f;
            foreach (int pi in list)
            {
                var pr = map.deckPairs[pi];
                if (pr.SideOf(e.index) != sideLeft) continue;
                if (s < pr.From(e.index) - Step || s > pr.To(e.index) + Step) continue;
                float at = pr.ArcOnOther(e.index, s);
                if (at < 0f || !map.edges[pr.Other(e.index)].ElevatedAt(at)) continue;
                best = Mathf.Max(best, paveEdge + Mathf.Max(0f, pr.gap) * 0.5f + 0.5f);
            }
            return best;
        }
    }
}
