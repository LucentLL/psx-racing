using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// TWIN DECKS DRAWN AS ONE STRUCTURE (2026-10-02, roads pass plan A2;
    /// Docs/CHARLOTTE.md "A2").
    ///
    /// The owner's example: West 5th Street over I-77 is one bridge with a
    /// median, and Charlotte drew two decks 1.5 m apart - four parapets, a
    /// 0.92 m slot down to the freeway and two pier lines out of step.
    /// B1's table (<see cref="DeckPairs"/>, <see cref="CityMap.deckPairs"/>)
    /// says which parallel decks are one structure; this draws them so:
    ///
    ///   RUNS      <see cref="BuildDeckUnions"/>, once per map at the end of
    ///             <see cref="ComputeTrims"/>: every union pair is one run over
    ///             its whole range on BOTH decks (B1's TWIN probes measure that
    ///             range), ended only where the two stand more than a split
    ///             level apart (<see cref="UnionSplitDyM"/>, B1's
    ///             TwinHoldMaxDyM) or where one no longer runs beside the
    ///             other. Two runs meeting on one side of one edge share the
    ///             overlap at its middle. A run whose two decks both come off
    ///             structure carries on over the APPROACHES (critic C21): a
    ///             raised arterial median on to the junction trims (at most
    ///             <see cref="UnionApproachRaisedM"/>) while the carriageways
    ///             stay within <see cref="UnionGroundGapMaxM"/> (A16's grass
    ///             line), a flush median over the <see cref="ApproachRailM"/>
    ///             band its inner approach rails stood on, and a JERSEY on
    ///             for as long as the two carriageways run beside each other
    ///             in the band (hotfix 2026-10-03: two edge Jerseys a few
    ///             metres apart with grass between read as a 1 m concrete box
    ///             with grass on top, and its square ends ended the owner's
    ///             I-277 race).
    ///   SIDES     a union side carries no rail, Jersey, cut wall, verge, kerb
    ///             face or fascia (DecideSideFlagsSteps, EmitSide, EmitDeckBox):
    ///             the OWNER (the lower edge index of the pair; on an approach,
    ///             the owner's chain) draws the median from its own drawn edge
    ///             to the partner's drawn edge (<see cref="EmitUnionMedian"/>);
    ///             the partner draws nothing there.
    ///   MEDIAN    by the pair's kind (owner_decisions.md): FLUSH a strip of
    ///             the road's own paint-free texels an inch down, tucked under
    ///             both edges; RAISED 0.10 m mountable curbs (battered) and a top
    ///             from one carriageway's height to the other's; BARRIER a 0.81 m
    ///             Jersey on the gap's centre line over a flush strip, its top
    ///             over the higher side (a split level). On a deck the strip is
    ///             a slab with a soffit at DeckThick. The one emitter A16 reuses
    ///             for raised medians on the ground (critic D4).
    ///   PIERS     the partner stands none inside a run; the owner stands a
    ///             BENT (<see cref="EmitBent"/>): a column under each
    ///             carriageway and one cap beam from outer edge to outer edge.
    ///   ENDS      a run end the median does not carry on past (hotfix
    ///             2026-10-03: no face square to the traffic): on a deck an end
    ///             face down to the soffit, and the slab's end guarded by a V -
    ///             the median splits over the last <see cref="TaperLenM"/> into
    ///             two legs that meet each carriageway's own parapet at the edge
    ///             (it was a rail square across the slab's end, a 90-degree wall
    ///             across the median strip); on the ground a Jersey turned down
    ///             to a curb over TaperLenM (it was a square cap), a battered
    ///             curb end, or nothing for a flush strip (the verge resumes).
    /// Draw calls: none new. The strip and curbs are the owner span's own
    /// road slot (paint-free texels), the soffit, end faces and bents the
    /// Concrete slot every deck span already uses, the Jersey and end rails
    /// the barrier mesh.
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>A union's two carriageways more than this apart in height
        /// are a split level: the run ends there (B1's TwinHoldMaxDyM, the
        /// TWIN report's "split level (left apart)").</summary>
        public const float UnionSplitDyM = 1.0f;
        /// <summary>How far past the pair's own range a run reaches, and how
        /// far its end snaps out to a structure end or an edge end.</summary>
        const float UnionPadM = 1.0f, UnionSnapM = 3.0f;
        /// <summary>The walk's step, the longest stretch of not-beside a run
        /// bridges, and the shortest run kept.</summary>
        const float UnionStepM = 1.0f, UnionBridgeM = 2.0f, UnionMinRunM = 4.0f;
        /// <summary>A foot more than this along the partner's tangent off the
        /// sample is past the partner's end: not beside it.</summary>
        const float UnionFootSlackM = 1.0f;
        /// <summary>How far the strip reaches under each road's drawn edge.</summary>
        public const float UnionTuckM = 0.10f;
        /// <summary>A raised median's mountable curb (owner rule) and how far
        /// its top is set back from the edge (a battered face, not a wall).</summary>
        public const float RaisedCurbM = 0.10f;
        const float RaisedBatterM = 0.08f;
        /// <summary>The longest one median piece runs between two
        /// cross-sections (the partner's height is read at each).</summary>
        const float UnionPieceM = 2.0f;
        /// <summary>APPROACHES (critic C21): a raised median carries on over
        /// the ground at most this far from the structure end, while the
        /// carriageways stay within <see cref="UnionGroundGapMaxM"/> (A16:
        /// wider medians stay grass) and at least
        /// <see cref="UnionGroundGapMinM"/> apart. A Jersey or a flush strip
        /// covers the <see cref="ApproachRailM"/> band only.</summary>
        /// <summary>A raised median shorter than this with both ends free is
        /// drawn flush (no curbs): a few metres of curb where a street divides
        /// is a block in a car's way, not a median (owner, 2026-10-24).</summary>
        public const float MinRaisedRunM = 20f;
        const float UnionApproachRaisedM = 80f, UnionGroundGapMaxM = 6.1f, UnionGroundGapMinM = 0.6f;
        /// <summary>A Jersey median carries on over the ground (and over
        /// structure the solve made, not an OSM bridge kept apart) for as long
        /// as the two carriageways stay beside each other in the band - this
        /// far at most (hotfix 2026-10-03; the walk's own guard). The road
        /// across is followed through at most <see cref="UnionChainMax"/>
        /// mitred nodes.</summary>
        const float UnionApproachBarrierM = 2500f;
        const int UnionChainMax = 96;
        /// <summary>A walk over an approach stops this short of a ribbon's
        /// end at a junction fan (the fan's own pavement and corners).</summary>
        const float UnionFanSetbackM = 1.0f;
        /// <summary>A bent's cap beam: depth, thickness along the deck, inset
        /// from the outer fascias.</summary>
        const float BentBeamDepthM = 0.9f, BentBeamHalfAlongM = 0.6f, BentBeamInsetM = 0.3f;

        /// <summary>
        /// One run of a union on one side of one edge: the road across the
        /// median lies on side <see cref="side"/> (+1 the left of travel, the
        /// R vertex) over arcs <see cref="s0"/>..<see cref="s1"/>. An OWNER
        /// run draws the median to the nearest of <see cref="across"/> (its
        /// partner edge <see cref="nb"/> and the edges that road carries on
        /// into: twin decks rarely end square with each other); a PARTNER run
        /// stands down for the owner's median, which <see cref="across"/>
        /// (the owner's edge and its continuations) lays.
        /// </summary>
        public sealed class UnionRun
        {
            public int edge, nb, side;
            public float s0, s1;
            public DeckPairs.Median median;
            /// <summary>This edge draws the median (the pair's lower index, or
            /// the owner's chain on an approach).</summary>
            public bool owner;
            /// <summary>The twin-deck pair (index into map.deckPairs) the run
            /// belongs to (an approach run: the pair it carries on from).</summary>
            public int pair;
            /// <summary>Over an approach, on the ground (C21).</summary>
            public bool approach;
            /// <summary>Nothing carries the median on past s0 / s1: the end
            /// is closed (an end face and rail on a deck, a curb or cap on the
            /// ground).</summary>
            public bool open0, open1;
            /// <summary>An owner run: its partner's run on <see cref="nb"/>; a
            /// partner run: the owner run whose median it stands down for.</summary>
            public UnionRun mirror;
            /// <summary>The edges across the median, the first preferred.</summary>
            public int[] across;
            public bool Covers(float s, float slack = 1e-3f) => s >= s0 - slack && s <= s1 + slack;
        }

        /// <summary>What the last <see cref="BuildDeckUnions"/> made, for the
        /// audit and the budget.</summary>
        public static string LastUnionReport { get; private set; } = "";
        public static float LastUnionMs { get; private set; }

        // ------------------------------------------------------------------
        //  The runs, once per map
        // ------------------------------------------------------------------

        static readonly List<(float s, int y, float t, bool ok)> pairWalk = new List<(float, int, float, bool)>(256);

        static void BuildDeckUnions(CityMap map, Trims t)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var E = map.edges;
            int ne = E.Length;
            t.unions = new List<UnionRun>[ne];
            twinEndsOf.Clear();
            var pairs = map.deckPairs;
            if (pairs == null) { LastUnionReport = "no twin-deck table"; return; }
            // The solve asks for trims before it marks any structure (its ramp
            // seats): nothing is on structure yet, and the runs are the final
            // trims' business only.
            bool anyStructure = false;
            foreach (var e in E)
            {
                if (e.stElev == null) continue;
                foreach (bool b in e.stElev) if (b) { anyStructure = true; break; }
                if (anyStructure) break;
            }
            if (!anyStructure) return;
            int pairsDrawn = 0, pairsSplit = 0, pairsNone = 0, splitRuns = 0;
            var noRun = new System.Text.StringBuilder();
            float splitWorst = 0f; string splitWorstAt = "";

            // 1. each union pair: one run over its whole range, the median laid
            // to B or to the road B carries on into, wherever one stands beside
            for (int pi = 0; pi < pairs.Length; pi++)
            {
                var pr = pairs[pi];
                if (!pr.union) continue;
                var A = E[pr.a]; var B = E[pr.b];
                if (A.a == A.b || B.a == B.b || A.stS == null || B.stS == null || A.stS.Length < 2 || B.stS.Length < 2) { pairsNone++; continue; }
                float reach = A.HalfMax + B.HalfMax + Mathf.Max(0f, pr.gap) + 8f;
                var acrossA = ChainAround(map, t, B);
                var acrossB = ChainAround(map, t, A);
                // A's range, widened by B's range seen from A
                float ra0 = pr.a0, ra1 = pr.a1;
                for (int k = 0; k < 2; k++)
                {
                    var q = B.PointAt(k == 0 ? pr.b0 : pr.b1);
                    CityElevation.ProjectOn(A, q, out float sa);
                    if (Vector2.Distance(A.PointAt(sa), q) > reach) continue;
                    ra0 = Mathf.Min(ra0, sa); ra1 = Mathf.Max(ra1, sa);
                }
                ra0 = SnapRunEnd(A, Mathf.Max(0f, ra0 - UnionPadM), -1);
                ra1 = SnapRunEnd(A, Mathf.Min(A.length, ra1 + UnionPadM), +1);
                // walk it: a road across beside it, no split level
                pairWalk.Clear();
                bool split = false;
                float pairDyMax = 0f; int beside = 0;
                for (float s = ra0; ; s += UnionStepM)
                {
                    bool last = s >= ra1 - 1e-4f;
                    if (last) s = ra1;
                    var p = A.PointAt(s);
                    bool ok = FootOn(map, acrossA, p, UnionFootSlackM, out var Y, out float ty, out float d) && d <= reach;
                    if (ok)
                    {
                        float dy = Mathf.Abs(A.YAt(s) - Y.YAt(ty));
                        beside++; pairDyMax = Mathf.Max(pairDyMax, dy);
                        if (dy > UnionSplitDyM)
                        {
                            ok = false; split = true;
                            if (dy > splitWorst) { splitWorst = dy; splitWorstAt = $"e{pr.a}/e{Y.index} dy {dy:0.00} m at ({p.x:0},{p.y:0})"; }
                        }
                    }
                    pairWalk.Add((s, ok ? Y.index : -1, ty, ok));
                    if (last) break;
                }
                // segments: the stretches beside, a gap of no more than UnionBridgeM bridged
                int segs = 0;
                int i0 = -1, iLast = -1;
                for (int i = 0; i <= pairWalk.Count; i++)
                {
                    bool end = i == pairWalk.Count;
                    if (!end && !pairWalk[i].ok) continue;
                    if (!end && i0 >= 0 && pairWalk[i].s - pairWalk[iLast].s <= UnionBridgeM + 1e-3f) { iLast = i; continue; }
                    if (!end && i0 < 0) { i0 = iLast = i; continue; }
                    // close the open segment: the walk's end, or beside again past the bridge
                    if (i0 >= 0 && pairWalk[iLast].s - pairWalk[i0].s >= UnionMinRunM) { AddPairRun(map, t, pi, pr, A, i0, iLast, acrossA, acrossB); segs++; }
                    if (!end) i0 = iLast = i;
                }
                if (segs == 0)
                {
                    pairsNone++;
                    if (noRun.Length < 600) noRun.Append($" e{pr.a}/e{pr.b} '{A.name}' {pr.median} ({(beside == 0 ? "never beside" : $"split level: dy up to {pairDyMax:0.00} m")});");
                    continue;
                }
                pairsDrawn++;
                if (split || segs > 1) { pairsSplit++; splitRuns += segs; }
            }

            // 2. two runs on one side of one edge that DRAW there (an owner's,
            // or the partner run its owner draws to first) share their
            // overlap at its middle; the cut carries over to the run across.
            // A partner run on the next edge across (a skewed deck end) only
            // stands down, and is left as it is.
            var drawn = new List<UnionRun>(8);
            for (int pass = 0; pass < 2; pass++)
                for (int ei = 0; ei < ne; ei++)
                {
                    var list = t.unions[ei];
                    if (list == null || list.Count < 2) continue;
                    drawn.Clear();
                    foreach (var r in list) if (r.owner || IsPrimary(r)) drawn.Add(r);
                    drawn.Sort((x, y) => x.side != y.side ? x.side.CompareTo(y.side) : x.s0.CompareTo(y.s0));
                    for (int k = 1; k < drawn.Count; k++)
                    {
                        var r1 = drawn[k - 1]; var r2 = drawn[k];
                        if (r1.side != r2.side || r2.s0 >= r1.s1 - 0.01f || r2.s1 - r2.s0 < 1e-3f) continue;
                        if (r2.s1 <= r1.s1 + 0.01f) { r2.s1 = r2.s0; ShrinkMirror(map, r2, 1); continue; }   // r2 inside r1: r1 keeps it
                        float c = 0.5f * (r2.s0 + r1.s1);
                        r1.s1 = c; r2.s0 = c;
                        ShrinkMirror(map, r1, 1); ShrinkMirror(map, r2, 0);
                    }
                }
            for (int ei = 0; ei < ne; ei++)
                t.unions[ei]?.RemoveAll(r => r.s1 - r.s0 < 0.5f);

            // 3. on over the approaches (C21)
            float approachM = 0f; int approachRuns = 0;
            var owners = new List<UnionRun>();
            for (int ei = 0; ei < ne; ei++)
                if (t.unions[ei] != null) foreach (var r in t.unions[ei]) if (r.owner && !r.approach) owners.Add(r);
            foreach (var r in owners)
                for (int end = 0; end < 2; end++)
                    approachM += ExtendOverApproach(map, t, r, end, ref approachRuns);

            // 3b. the ground twins (2026-10-24): the rest of every freeway's
            // twin carriageways, wherever no deck or approach run reached
            float groundM = GroundTwinRuns(map, t, out int groundRuns);

            // (round 6) a run end within UnionNodeSnapM of its edge's end is AT the
            // node: an approach run starting 0.2 m into the edge after a deck run
            // that ends on the node read as two closed ends - the deck's V legs and
            // the Jersey turned down, a diamond in the median (I-277 e2307/e2303 at
            // the Uptown Loop's 800 m, I-77 e15241)
            if (TwinBridgesOn)
                for (int ei = 0; ei < ne; ei++)
                {
                    var list = t.unions[ei];
                    if (list == null) continue;
                    float len = E[ei].length;
                    foreach (var r in list)
                    {
                        if (r.s0 > 0f && r.s0 <= UnionNodeSnapM) r.s0 = 0f;
                        if (r.s1 < len && r.s1 >= len - UnionNodeSnapM) r.s1 = len;
                    }
                }

            // 4. which ends are open
            int open = 0;
            for (int ei = 0; ei < ne; ei++)
            {
                var list = t.unions[ei];
                if (list == null) continue;
                list.Sort((x, y) => x.side != y.side ? x.side.CompareTo(y.side) : x.s0.CompareTo(y.s0));
                foreach (var r in list)
                {
                    r.open0 = !Continued(map, t, r, 0);
                    r.open1 = !Continued(map, t, r, 1);
                    if (r.owner) open += (r.open0 ? 1 : 0) + (r.open1 ? 1 : 0);
                }
            }

            // 5. no short raised island (owner 2026-10-24: "random medians only a
            // few feet long on random side streets where roads split ... they
            // often result in wrecks"): a raised median whose owner run is
            // shorter than MinRaisedRunM with neither end carried on is a flush
            // strip - the hotfix's 30 m rule for Jerseys, for curbs. Its partner
            // runs follow (they stand down; the owner draws).
            int raisedFlushed = 0;
            for (int ei = 0; ei < ne; ei++)
            {
                var list = t.unions[ei];
                if (list == null) continue;
                foreach (var r in list)
                {
                    if (!r.owner || r.median != DeckPairs.Median.Raised || r.s1 - r.s0 >= MinRaisedRunM || !r.open0 || !r.open1) continue;
                    r.median = DeckPairs.Median.Flush; raisedFlushed++;
                    if (r.mirror != null) r.mirror.median = DeckPairs.Median.Flush;
                    foreach (int yi in r.across ?? System.Array.Empty<int>())
                        if (t.unions[yi] != null) foreach (var o in t.unions[yi]) if (o.mirror == r) o.median = DeckPairs.Median.Flush;
                }
            }

            float deckM = 0f; int runs = 0;
            for (int ei = 0; ei < ne; ei++)
                if (t.unions[ei] != null) foreach (var r in t.unions[ei]) if (r.owner && !r.approach) { runs++; deckM += r.s1 - r.s0; }
            LastUnionMs = (float)clock.Elapsed.TotalMilliseconds;
            LastUnionReport = $"{pairsDrawn} union pairs drawn as one structure ({runs} runs, {deckM:0} m over the decks' ranges; {approachRuns} approach runs, {approachM:0} m; {groundRuns} ground-twin runs, {groundM:0} m; {raisedFlushed} short raised medians made flush); " +
                              $"{pairsSplit} pairs in more than one run or ended by a split level over {UnionSplitDyM} m ({splitRuns} runs{(splitWorst > 0f ? "; worst " + splitWorstAt : "")}); " +
                              $"{pairsNone} union pairs with no run{(noRun.Length > 0 ? " (" + noRun.ToString().Trim().TrimEnd(';') + ")" : "")}; {open} open run ends; {LastUnionMs:0.0} ms";
        }

        /// <summary>One segment of a pair's walk (pairWalk[i0..i1]) as an owner
        /// run on A and a partner run on each edge across it was beside.</summary>
        static void AddPairRun(CityMap map, Trims t, int pi, DeckPairs.Pair pr, CityMap.Edge A, int i0, int i1, int[] acrossA, int[] acrossB)
        {
            var E = map.edges;
            var ra = new UnionRun { edge = pr.a, nb = pr.b, side = pr.sideA, s0 = pairWalk[i0].s, s1 = pairWalk[i1].s, median = MedianOfUnion(pr), owner = true, pair = pi, across = acrossA };
            AddRun(t, ra);
            // the partner runs: each edge across, over its feet (half a step on)
            foreach (int yi in acrossA)
            {
                float lo = float.MaxValue, hi = float.MinValue, sLo = 0f, sHi = 0f;
                for (int i = i0; i <= i1; i++)
                {
                    var w = pairWalk[i];
                    if (!w.ok || w.y != yi) continue;
                    if (w.t < lo) { lo = w.t; sLo = w.s; }
                    if (w.t > hi) { hi = w.t; sHi = w.s; }
                }
                if (hi < lo) continue;
                var Y = E[yi];
                lo = Mathf.Max(0f, lo - 0.5f * UnionStepM); hi = Mathf.Min(Y.length, hi + 0.5f * UnionStepM);
                if (lo <= 1f) lo = 0f;
                if (Y.length - hi <= 1f) hi = Y.length;
                if (hi - lo < 0.25f) continue;
                float ym = 0.5f * (lo + hi);
                CityElevation.ProjectOn(A, Y.PointAt(ym), out float sm);
                var rb = new UnionRun { edge = yi, nb = pr.a, side = SideToward(Y, ym, A.PointAt(sm)), s0 = lo, s1 = hi, median = MedianOfUnion(pr), owner = false, pair = pi, across = acrossB, mirror = ra };
                AddRun(t, rb);
                if (ra.mirror == null || yi == pr.b) ra.mirror = rb;
            }
            if (ra.mirror == null) ra.mirror = new UnionRun { edge = pr.b, nb = pr.a, side = pr.sideB, s0 = 0f, s1 = 0f, median = MedianOfUnion(pr), pair = pi, across = acrossB };
        }

        /// <summary>
        /// The median a union draws (hotfix 2026-10-03, owner): only roads
        /// whose traffic OPPOSES - twin carriageways, or two roads running
        /// opposite ways on one deck - stand a Jersey or curbs between them.
        /// A ramp beside its mainline (or another ramp), or two roads the same
        /// way, merge and diverge across a painted gore: they share the deck's
        /// surface with a flush strip and no median.
        /// </summary>
        static DeckPairs.Median MedianOfUnion(DeckPairs.Pair pr) =>
            pr.kind == DeckPairs.Kind.Dual || pr.kind == DeckPairs.Kind.OpposedOther ? pr.median
            : pr.median == DeckPairs.Median.None ? DeckPairs.Median.None : DeckPairs.Median.Flush;

        static void AddRun(Trims t, UnionRun r) => (t.unions[r.edge] ??= new List<UnionRun>(2)).Add(r);

        /// <summary>An edge and the edges mitred nodes carry it on into, one
        /// each way: the road across a twin deck, as a run may meet it.</summary>
        static int[] ChainAround(CityMap map, Trims t, CityMap.Edge e)
        {
            var a = MitredNext(map, t, e, e.a); var b = MitredNext(map, t, e, e.b);
            int n = 1 + (a != null ? 1 : 0) + (b != null && b != a ? 1 : 0);
            var r = new int[n];
            int k = 0;
            r[k++] = e.index;
            if (a != null) r[k++] = a.index;
            if (b != null && b != a) r[k++] = b.index;
            return r;
        }

        /// <summary>The nearest TRUE foot of plan point p on the edges
        /// <paramref name="cands"/> (no further than <paramref name="slack"/>
        /// along an edge's tangent off its end); false where none.</summary>
        static bool FootOn(CityMap map, int[] cands, Vector2 p, float slack, out CityMap.Edge Y, out float y, out float dist)
        {
            Y = null; y = 0f; dist = float.MaxValue;
            foreach (int ci in cands)
            {
                var c = map.edges[ci];
                CityElevation.ProjectOn(c, p, out float tc);
                var q = c.PointAt(tc);
                float d = Vector2.Distance(p, q);
                if (d >= dist || Mathf.Abs(Vector2.Dot(p - q, c.TangentAt(tc))) > slack) continue;
                dist = d; Y = c; y = tc;
            }
            return Y != null;
        }

        /// <summary><see cref="FootOn"/> with half a metre's slack, else the
        /// nearest foot of any (clamped at an edge's end).</summary>
        static void FootAny(CityMap map, int[] cands, Vector2 p, out CityMap.Edge Y, out float y)
        {
            if (FootOn(map, cands, p, 0.5f, out Y, out y, out _)) return;
            float best = float.MaxValue;
            foreach (int ci in cands)
            {
                var c = map.edges[ci];
                CityElevation.ProjectOn(c, p, out float tc);
                float d = Vector2.Distance(p, c.PointAt(tc));
                if (d < best) { best = d; Y = c; y = tc; }
            }
        }

        /// <summary>A run end moved out to the edge's end or a structure end
        /// within <see cref="UnionSnapM"/> (dir -1: the run's start, +1 its
        /// end), so no stub of deck is left beside its twin without the slab.
        /// (The walk then decides how far a road across stands beside it.)</summary>
        static float SnapRunEnd(CityMap.Edge e, float s, int dir)
        {
            if (dir < 0 && s <= UnionSnapM) return 0f;
            if (dir > 0 && e.length - s <= UnionSnapM) return e.length;
            endSnapScratch.Clear();
            InternalStructureEnds(e, endSnapScratch);
            float best = s;
            foreach (float se in endSnapScratch)
            {
                float d = (se - s) * dir;
                if (d > 0f && d <= UnionSnapM && Mathf.Abs(se - s) > Mathf.Abs(best - s)) best = se;
            }
            return best;
        }
        static readonly List<float> endSnapScratch = new List<float>(8);

        /// <summary>A partner run its owner draws to first (not one on the
        /// next edge across).</summary>
        static bool IsPrimary(UnionRun r) => !r.owner && r.mirror != null && r.mirror.mirror == r;

        /// <summary>The run across after this run's end <paramref name="end"/>
        /// (0: s0, 1: s1) was cut back, along the primary link only (an owner
        /// and the partner run it draws to first): its end beside the cut is
        /// cut back to it too (plus a hand), its other end stands; gone with
        /// it when the run is gone.</summary>
        static void ShrinkMirror(CityMap map, UnionRun r, int end)
        {
            var m = r.mirror;
            if (m == null || m.mirror != r || m.s1 - m.s0 < 1e-4f) return;
            if (r.s1 - r.s0 < 1e-3f) { m.s1 = m.s0; return; }
            var e = map.edges[r.edge]; var o = map.edges[m.edge];
            CityElevation.ProjectOn(o, e.PointAt(end == 0 ? r.s0 : r.s1), out float tt);
            if (Mathf.Abs(tt - m.s0) <= Mathf.Abs(tt - m.s1)) m.s0 = Mathf.Min(Mathf.Max(m.s0, tt - 0.05f), m.s1);
            else m.s1 = Mathf.Max(Mathf.Min(m.s1, tt + 0.05f), m.s0);
        }

        /// <summary>The run on <paramref name="side"/> of an edge that holds
        /// the middle of [s0, s1], or null.</summary>
        static UnionRun UnionOn(Trims t, int edge, int side, float s0, float s1)
        {
            var list = t.unions != null ? t.unions[edge] : null;
            if (list == null) return null;
            float m = 0.5f * (s0 + s1);
            // an owner's run first (it draws), then a partner's
            UnionRun partner = null;
            foreach (var r in list)
            {
                if (r.side != side || !r.Covers(m)) continue;
                if (r.owner) return r;
                partner ??= r;
            }
            return partner;
        }

        /// <summary>
        /// Is span A-B's side of edge e a union side as DRAWN (DecideSideFlags)?
        /// A run must hold it, and: an owner's road across must stand apart from
        /// it (the median has width: a branch clipped onto it does not); a
        /// partner's side must lie beside an owner run that covers it, its
        /// owner's drawn edge apart from it (else the partner keeps its own
        /// rail or verge: no edge stands down where no slab is laid). A and B
        /// are tile-local; <paramref name="origin"/> the tile's.
        /// </summary>
        static UnionRun UnionSideHere(CityMap map, Trims trims, CityMap.Edge e, int side, in Section A, in Section B, Vector3 origin)
        {
            var run = UnionOn(trims, e.index, side, A.s, B.s);
            if (run == null) return null;
            float sm = 0.5f * (A.s + B.s);
            var pm = e.PointAt(sm);
            CityMap.Edge X; float x;
            if (run.owner) FootAny(map, run.across, pm, out X, out x);
            else
            {
                if (!FootOn(map, run.across, pm, 0.5f, out X, out x, out _)) return null;
                bool covered = false;
                int sideX = SideToward(X, x, pm);
                var list = trims.unions[X.index];
                if (list != null) foreach (var r2 in list) if (r2.owner && r2.side == sideX && r2.Covers(x, 0.3f)) { covered = true; break; }
                if (!covered) return null;
            }
            if (X == null) return null;
            var w = FacingEdgeAt(map, trims, X, x, pm);
            var mine = 0.5f * (A.Edge(side) + B.Edge(side)) + origin;
            var outw = (A.Out(side) + B.Out(side)).normalized;
            float d = Vector2.Dot(new Vector2(w.x - mine.x, w.z - mine.z), outw);
            return d >= (run.owner ? 0.05f : 0.10f) ? run : null;
        }

        /// <summary>
        /// For the audits (TWIN): is arc s of edge e on a deck within a union
        /// run's closed-end V (EmitUnionLegs: the median split into two legs
        /// over the last max(TaperLenM, 1.5x the gap) - its designed end, two
        /// walls where the run's middle has one)?
        /// </summary>
        public static bool InUnionEndV(CityMap map, Trims t, CityMap.Edge e, float s)
        {
            var list = t?.unions != null && e.index < t.unions.Length ? t.unions[e.index] : null;
            if (list == null || !e.ElevatedAt(s)) return false;
            foreach (var r in list)
            {
                if (!r.Covers(s, 0.5f)) continue;
                float endLen = Mathf.Max(TaperLenM, 1.5f * Mathf.Max(0f, r.pair >= 0 ? map.deckPairs[r.pair].gap : 0f)) + 1f;
                if ((r.open0 && s - r.s0 < endLen) || (r.open1 && r.s1 - s < endLen)) return true;
            }
            return false;
        }

        /// <summary>Is the edge the PARTNER of a run over a deck at arc s (it
        /// stands no pier there: the owner's bent carries it)?</summary>
        static bool UnionPartnerAt(Trims t, int edge, float s)
        {
            var list = t.unions != null ? t.unions[edge] : null;
            if (list == null) return false;
            foreach (var r in list) if (!r.owner && !r.approach && r.Covers(s, 0.5f)) return true;
            return false;
        }

        /// <summary>Does the edge own a run over a deck at arc s (on either side)?</summary>
        static bool UnionOwnerAt(Trims t, int edge, float s)
        {
            var list = t.unions != null ? t.unions[edge] : null;
            if (list == null) return false;
            foreach (var r in list) if (r.owner && !r.approach && r.Covers(s)) return true;
            return false;
        }

        /// <summary>Does another run carry the median on past this run's end
        /// (0: s0, 1: s1) on the same side - on this edge, or across a mitred
        /// node on the edge it continues into?</summary>
        static bool Continued(CityMap map, Trims t, UnionRun r, int end)
        {
            var e = map.edges[r.edge];
            float s = end == 0 ? r.s0 : r.s1;
            foreach (var o in t.unions[r.edge])
            {
                if (o == r || o.side != r.side) continue;
                if (end == 0 ? Mathf.Abs(o.s1 - s) < 0.1f || (o.s0 < s - 0.1f && o.s1 > s - 0.1f) : Mathf.Abs(o.s0 - s) < 0.1f || (o.s0 < s + 0.1f && o.s1 > s + 0.1f)) return true;
            }
            // (REAL HEIGHTS round 4) a run laid on the TWIN that carries the median
            // on past this end: a ground run stops where it finds the spot already
            // covered by a run on the carriageway across (a deck's approach run),
            // and each turned its end down to a curb in the other's face - a low V
            // in the median a car crosses (the owner on I-277: "allowing cars to
            // drive into oncoming traffic"; MEDIAN ACROSS none). Across, the two
            // are one median.
            if (TwinContinueOn && r.nb >= 0 && r.nb < map.edges.Length && t.unions[r.nb] != null)
            {
                var yt = map.edges[r.nb];
                var pe = e.PointAt(Mathf.Clamp(s, 0f, e.length));
                CityElevation.ProjectOn(yt, pe, out float ys);
                if (Vector2.Distance(yt.PointAt(ys), pe) <= e.HalfMax + yt.HalfMax + GroundTwinGapMaxM + 2f)
                {
                    // the run across must carry on beyond this end (in our travel sense, on its line)
                    var pIn = e.PointAt(Mathf.Clamp(end == 0 ? s + 2f : s - 2f, 0f, e.length));
                    CityElevation.ProjectOn(yt, pIn, out float yIn);
                    float beyond = ys + (ys - yIn) * 0.5f;
                    foreach (var o in t.unions[r.nb])
                        if (o.Covers(ys, 0.5f) && o.Covers(beyond, 0.5f)) return true;
                }
            }
            bool atNode = end == 0 ? s <= 0.05f : s >= e.length - 0.05f;
            if (!atNode) return false;
            int node = end == 0 ? e.a : e.b;
            var y = MitredNext(map, t, e, node);
            if (y == null || t.unions[y.index] == null) return false;
            // the same side of the road: Y runs on in our direction when it
            // leaves the node at our b end or enters it at our a end
            bool same = end == 1 ? y.a == node : y.b == node;
            int sideY = same ? r.side : -r.side;
            bool yAtA = y.a == node;
            foreach (var o in t.unions[y.index])
            {
                if (o.side != sideY) continue;
                if (yAtA ? o.s0 <= 0.1f : o.s1 >= y.length - 0.1f) return true;
            }
            return false;
        }

        /// <summary>The edge a mitred node carries this one on into, or null.</summary>
        static CityMap.Edge MitredNext(CityMap map, Trims t, CityMap.Edge e, int node)
        {
            if (!t.mitre[node]) return null;
            int o = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
            if (o < 0 || o == e.index) return null;
            var y = map.edges[o];
            return y.a == y.b ? null : y;
        }

        /// <summary>
        /// THE APPROACHES (critic C21). From one end of a deck run whose two
        /// carriageways both come down to the ground there, walk the owner's
        /// road on (through mitred nodes) and lay a run wherever the road across
        /// stays beside it: within the median's gap band, at one height, on the
        /// ground, short of a junction fan. Raised medians go on to
        /// <see cref="UnionApproachRaisedM"/>, flush strips and Jerseys over
        /// the approach rail band. Returns the metres laid.
        /// </summary>
        static float ExtendOverApproach(CityMap map, Trims t, UnionRun r, int end, ref int added)
        {
            if (Continued(map, t, r, end)) return 0f;
            // approach medians only between the twin carriageways of one road
            // (hotfix 2026-10-03): never on between a ramp and its mainline
            if (r.pair < 0 || map.deckPairs[r.pair].kind != DeckPairs.Kind.Dual) return 0f;
            var A = map.edges[r.edge];
            float sEnd = end == 0 ? r.s0 : r.s1;
            int dirA = end == 0 ? -1 : 1;
            // the road across at the run's end, and which way it carries on
            var pEnd = A.PointAt(sEnd);
            FootAny(map, r.across, pEnd, out var Yend, out float tEnd);
            if (Yend == null) return 0f;
            int dirB = Vector2.Dot(A.TangentAt(sEnd) * dirA, Yend.TangentAt(tEnd)) >= 0f ? 1 : -1;
            // another union already carries the road across on: leave it
            {
                int sideY = SideToward(Yend, tEnd, pEnd);
                var ly = t.unions[Yend.index];
                if (ly != null) foreach (var o in ly) if (o.side == sideY && o.mirror != r && o != r.mirror && o.Covers(tEnd + dirB * 1.5f, 0f)) return 0f;
            }
            float maxM = r.median == DeckPairs.Median.Raised ? UnionApproachRaisedM
                       : r.median == DeckPairs.Median.Barrier ? UnionApproachBarrierM : ApproachRailM;
            float gMax = (r.median == DeckPairs.Median.Flush ? 1.2f : UnionGroundGapMaxM) + 0.5f;
            float gMin = r.median == DeckPairs.Median.Raised ? UnionGroundGapMinM : DeckPairs.SqueezeM;
            // a raised top or a Jersey takes a split level (SharedGuardDyM); a
            // flush strip only what a wheel rolls across
            float dyMax = r.median == DeckPairs.Median.Flush ? 0.10f : SharedGuardDyM;
            // the road across, ahead: (edge, from arc, direction)
            bChain.Clear();
            {
                var cur = Yend; float from = tEnd; int dir = dirB; float run = 0f;
                for (int g = 0; g < UnionChainMax && cur != null && run < maxM + 40f; g++)
                {
                    bChain.Add((cur, from, dir));
                    run += dir > 0 ? cur.length - from : from;
                    int node = dir > 0 ? cur.b : cur.a;
                    var nx = MitredNext(map, t, cur, node);
                    if (nx == null || bChain.Exists(c => c.e == nx)) break;
                    from = nx.a == node ? 0f : nx.length; dir = nx.a == node ? 1 : -1;
                    cur = nx;
                }
            }
            bAcross = new int[bChain.Count];
            for (int k = 0; k < bChain.Count; k++) bAcross[k] = bChain[k].e.index;
            bCur = 0;
            // walk the owner's road on
            walk.Clear();
            {
                var X = A; float x = sEnd; int dx = dirA; float walked = 0f;
                int guardMax = Mathf.CeilToInt(maxM / UnionStepM) + 400;
                for (int guard = 0; guard < guardMax && walked <= maxM; guard++)
                {
                    if (!ApproachSample(map, t, r, X, x, walked, gMin, gMax, dyMax, out var Y, out float y))
                    {
                        // stopped by the next run along (another structure's,
                        // or another approach's): the median meets it there
                        // exactly, so the two are one line and neither end is
                        // turned down in the other's face
                        if (approachBlock >= 0f && walk.Count > 0 && walk[walk.Count - 1].X == X
                            && Mathf.Abs(approachBlock - x) <= UnionStepM + 1e-3f
                            && ApproachSample(map, t, r, X, approachBlock, walked, gMin, gMax, dyMax, out Y, out y, true))
                            walk.Add((X, approachBlock, Y, y));
                        // (round 5) stopped (by structure) a step short of another
                        // union run starting on this side: the median meets that run's
                        // start exactly, so no own rail stands in the metre between
                        // (one place city-wide; uptown's route rail ends at I-277
                        // e2144 s 500 / I-77 e1874 s 188 are not this - still open)
                        else if (approachBlock < 0f && MeetNextRunOn && walk.Count > 0 && walk[walk.Count - 1].X == X)
                        {
                            var lw = walk[walk.Count - 1];
                            int sideW = SideToward(X, lw.x, lw.Y.PointAt(lw.y));
                            var lx = t.unions[X.index];
                            float meet = -1f;
                            if (lx != null)
                                foreach (var o in lx)
                                {
                                    if (o == r || o.side != sideW) continue;
                                    float se = dx > 0 ? o.s0 : o.s1;
                                    if ((se - lw.x) * dx > 0.05f && (se - lw.x) * dx <= 2f * UnionStepM) meet = se;
                                }
                            if (meet >= 0f && ApproachSample(map, t, r, X, meet - dx * 0.02f, 0f, gMin, gMax, dyMax, out Y, out y, true))
                                walk.Add((X, meet - dx * 0.02f, Y, y));
                        }
                        break;
                    }
                    walk.Add((X, x, Y, y));
                    float nxt = x + dx * UnionStepM;
                    if (nxt < 0f || nxt > X.length)
                    {
                        float endArc = dx > 0 ? X.length : 0f;
                        if (Mathf.Abs(endArc - x) > 1e-3f)
                        {
                            // the edge's end exactly, then on through its node
                            if (!ApproachSample(map, t, r, X, endArc, walked + Mathf.Abs(endArc - x), gMin, gMax, dyMax, out Y, out y)) break;
                            walk.Add((X, endArc, Y, y));
                            walked += Mathf.Abs(endArc - x);
                        }
                        int node = dx > 0 ? X.b : X.a;
                        var nx = MitredNext(map, t, X, node);
                        if (nx == null) break;
                        X = nx; x = nx.a == node ? 0f : nx.length; dx = nx.a == node ? 1 : -1;
                        continue;
                    }
                    walked += UnionStepM;
                    x = nxt;
                }
            }
            if (walk.Count < 4) return 0f;
            // an owner run per edge of the owner's road; a partner run per edge
            // across, per owner edge
            float laid = 0f;
            int i0 = 0;
            for (int i = 1; i <= walk.Count; i++)
            {
                if (i < walk.Count && walk[i].X == walk[i0].X) continue;
                var X = walk[i0].X;
                float x0 = float.MaxValue, x1 = float.MinValue;
                for (int k = i0; k < i; k++) { x0 = Mathf.Min(x0, walk[k].x); x1 = Mathf.Max(x1, walk[k].x); }
                if (x1 - x0 >= 0.5f)
                {
                    // (round 6) the side from every sample's own pair of points (the
                    // mid ARC against the mid SAMPLE's twin read the wrong side on a
                    // curve: the partner's own median Jersey stood beside the union's)
                    int sideX = 0;
                    for (int k = i0; k < i; k++) sideX += SideToward(X, walk[k].x, walk[k].Y.PointAt(walk[k].y));
                    sideX = !TwinSideVoteOn ? SideToward(X, 0.5f * (x0 + x1), walk[(i0 + i - 1) / 2].Y.PointAt(walk[(i0 + i - 1) / 2].y)) : sideX >= 0 ? 1 : -1;
                    var acrossX = ChainAround(map, t, X);
                    // the road across as far as this edge's stretch met it (a
                    // kilometre-long chain is not searched per median piece)
                    metY.Clear();
                    for (int k = i0; k < i; k++) if (!metY.Contains(walk[k].Y)) metY.Add(walk[k].Y);
                    var acrossMet = new int[metY.Count];
                    for (int k = 0; k < metY.Count; k++) acrossMet[k] = metY[k].index;
                    var ra = new UnionRun { edge = X.index, nb = walk[(i0 + i - 1) / 2].Y.index, side = sideX, s0 = x0, s1 = x1, median = r.median, owner = true, pair = r.pair, approach = true, across = acrossMet };
                    AddRun(t, ra);
                    laid += x1 - x0; added++;
                    foreach (var be in metY)
                    {
                        float lo = float.MaxValue, hi = float.MinValue; int kMid = -1, vote = 0;
                        for (int k = i0; k < i; k++)
                        {
                            if (walk[k].Y != be) continue;
                            lo = Mathf.Min(lo, walk[k].y); hi = Mathf.Max(hi, walk[k].y); kMid = k;
                            vote += SideToward(be, walk[k].y, X.PointAt(walk[k].x));
                        }
                        if (kMid < 0) continue;
                        lo = Mathf.Max(0f, lo - 0.5f * UnionStepM); hi = Mathf.Min(be.length, hi + 0.5f * UnionStepM);
                        if (lo <= 1f) lo = 0f;
                        if (be.length - hi <= 1f) hi = be.length;
                        if (hi - lo < 0.25f) continue;
                        var rb = new UnionRun { edge = be.index, nb = X.index, side = !TwinSideVoteOn ? SideToward(be, 0.5f * (lo + hi), X.PointAt(walk[kMid].x)) : vote >= 0 ? 1 : -1, s0 = lo, s1 = hi,
                                                median = r.median, owner = false, pair = r.pair, approach = true, across = acrossX, mirror = ra };
                        AddRun(t, rb);
                        if (ra.mirror == null || be.index == ra.nb) ra.mirror = rb;
                    }
                    if (ra.mirror == null) ra.mirror = new UnionRun { edge = ra.nb, nb = X.index, side = -sideX, median = r.median, pair = r.pair, approach = true, across = acrossX };
                }
                i0 = i;
            }
            return laid;
        }

        // ------------------------------------------------------------------
        //  THE GROUND TWINS (2026-10-24, the owner's Uptown Loop screenshots:
        //  "median not connecting properly or having random sections that make
        //  boxes or diamonds that result in wrecks")
        // ------------------------------------------------------------------
        /// <summary>The ground twins' gap band - wider than the approaches'
        /// 6.6 m: a paved inside shoulder each side of one centre Jersey, as the
        /// Uptown Loop's 6.9-7.4 m stretches are - the walk's step, and the
        /// shortest run laid. Their split-level limit is a twin deck's
        /// (<see cref="UnionSplitDyM"/>, the Jersey's top over the higher side):
        /// the ground solve leaves I-277's carriageways up to ~1 m apart.</summary>
        const float GroundTwinGapMaxM = 9.5f, GroundTwinStepM = 4f, GroundTwinMinRunM = 12f, GroundTwinSnapM = 6f;
        static readonly HashSet<int> twinSegs = new HashSet<int>();
        static readonly List<float> twinEndsX = new List<float>(4);
        static readonly Dictionary<int, List<float>> twinEndsOf = new Dictionary<int, List<float>>();
        static Vector2 twinQueryAt;
        const float TwinQueryReuseM = 10f;
        /// <summary>The carriageway's own Jersey runs this far ahead of an
        /// approach rail band before a ground median takes over: the rail's
        /// start is never a free end (rails are not turned down - they guard
        /// drops), and the Jersey's own start is (2026-10-24: a ground median
        /// ending AT the band left I-277's e2143 approach rail square).</summary>
        const float GroundTwinBandLeadM = 10f;
        /// <summary>REAL HEIGHTS round 4 (the owner on the live Uptown Loop: "This is not
        /// how medians work. They don't just start and stop"): structure only where OSM
        /// tags it put approach rail bands at every bridge end along I-277, and at each one
        /// the shared median stopped - turned down to a curb - while each carriageway stood
        /// its own approach rail: two walls with a ditch between (MEDIAN ACROSS boxes) and a
        /// low gap where one ends and the others begin (none). The shared median now runs on
        /// through the bands (the median side's approach rail stands down under it, as under
        /// any union run); the outside keeps its approach rail. PSX_CITY_TWIN_BANDS=1 is the before.</summary>
        /// <summary>REAL HEIGHTS round 4: a union run's end met by a run on the twin carriageway is continued (PSX_CITY_TWIN_CONTINUE=0: the before).</summary>
        public static bool TwinContinueOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TWIN_CONTINUE") != "0";
        public static bool TwinStopsAtBands = System.Environment.GetEnvironmentVariable("PSX_CITY_TWIN_BANDS") == "1";
        /// <summary>
        /// REAL HEIGHTS round 6 (the owner, again: "rectangles and diamonds of medians in the
        /// center of the highways", one at the Uptown Loop's start line). The ground-twin walk
        /// skipped OSM bridges on both sides, so wherever one carriageway's bridge (or its deck
        /// carried on to the twin's abutment line) stood beside the other's ground or deck
        /// outside the twin-deck table's union - every place the two OSM bridges do not start
        /// and end together - each carriageway stood its own parapet or Jersey: a box of grass
        /// between two walls (I-277 e1910/e1920 at the start line, 4.5 m by 13 m; e1826/e2413,
        /// e2411/e2413, e2029/e9900, e2438/e2436 ...). The walk now takes bridges too (a deck
        /// union's run already there keeps its place), and a stretch shorter than
        /// GroundTwinMinRunM is laid when it meets a run already on that side (it fills the gap
        /// between two). PSX_CITY_TWIN_BRIDGES=0: before.
        /// </summary>
        public static bool TwinBridgesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TWIN_BRIDGES") != "0";
        /// <summary>(round 6) A union run's end this close to its edge's end is moved onto the node.</summary>
        const float UnionNodeSnapM = 0.6f;
        /// <summary>Round 6: a partner run's side by every sample's own vote (PSX_CITY_TWIN_SIDEVOTE=0: the mid-arc read, before).</summary>
        public static bool TwinSideVoteOn = System.Environment.GetEnvironmentVariable("PSX_CITY_TWIN_SIDEVOTE") != "0";
        /// <summary>A run on <paramref name="edge"/>'s <paramref name="side"/> ending or starting within
        /// <paramref name="reach"/> of the arc range (the gap a short stretch fills).</summary>
        static bool RunNear(Trims t, int edge, int side, float s0, float s1, float reach)
        {
            var list = t.unions[edge];
            if (list == null) return false;
            foreach (var o in list)
                if (o.side == side && o.s0 <= s1 + reach && o.s1 >= s0 - reach) return true;
            return false;
        }

        /// <summary>
        /// (round 6) Is arc s of an edge's side covered by a run that is DRAWN there: its own
        /// owner run, or a partner run whose owner's run covers the partner's foot on the road
        /// across (as UnionSideHere decides, without the drawn edges). A partner run whose
        /// owner stops short of its foot - the owner's edge ends at a node while the twin's
        /// piece runs on (Elizabeth Ave e2436 by e2438's start, I-277 e9672's ends) - covers
        /// nothing: it stood its own parapet there and no median was laid (boxes).
        /// </summary>
        static bool CoveredDrawn(CityMap map, Trims t, int edge, int side, float s)
        {
            var list = t.unions[edge];
            if (list == null) return false;
            var e = map.edges[edge];
            Vector2 pm = default; bool havePm = false;
            foreach (var r in list)
            {
                if (r.side != side || !r.Covers(s, 0.5f)) continue;
                if (r.owner) return true;
                if (r.across == null) continue;
                if (!havePm) { pm = e.PointAt(s); havePm = true; }
                if (!FootOn(map, r.across, pm, 0.5f, out var X, out float x, out _) || X == null) continue;
                int sx = SideToward(X, x, pm);
                var l2 = t.unions[X.index];
                if (l2 == null) continue;
                foreach (var r2 in l2) if (r2.owner && r2.side == sx && r2.Covers(x, 0.3f)) return true;
            }
            return false;
        }
        /// <summary>Why the last refused sample was refused: a run already there.</summary>
        static bool twinRefusedCovered;
        static bool InApproachBand(List<float> ends, float s)
        {
            foreach (float se in ends) if (Mathf.Abs(s - se) < ApproachRailM + GroundTwinBandLeadM) return true;
            return false;
        }
        static readonly List<(float x, int y, float ty, int side)> twinWalk = new List<(float, int, float, int)>(512);

        /// <summary>
        /// Union runs used to start only at a twin DECK and reach the ground
        /// over its approaches, which stop at the first junction that is not a
        /// plain mitre - every ramp on a freeway. Everywhere else each freeway
        /// carriageway stood its own median Jersey at its own inner edge, so a
        /// divided road 3-5 m apart showed two walls with grass between them:
        /// 48% of the Uptown Loop's I-277 (the drive audit's MEDIAN ACROSS),
        /// read as boxes, wedges and diamonds wherever the gap changed. This
        /// walks every barriered carriageway (<see cref="Barriered"/>, not an
        /// OSM bridge) and lays the shared median - a flush strip and one
        /// Jersey on the gap's centre line (<see cref="EmitUnionMedian"/>) -
        /// wherever its opposing twin (same name, the other way) runs beside it
        /// on the ground within the Jersey band, at one height, short of a
        /// junction fan, and no run is there yet. The lower edge index owns the
        /// median; runs carry <c>pair = -1</c> and count as approaches (no
        /// bents; a closed end on the ground is a Jersey turned down to a curb).
        /// </summary>
        static float GroundTwinRuns(CityMap map, Trims t, out int added)
        {
            added = 0; float laid = 0f;
            twinEndsOf.Clear();
            foreach (var X in map.edges)
            {
                if (X.a == X.b || !X.oneway || (X.bridge && !TwinBridgesOn) || !Barriered(X) || string.IsNullOrEmpty(X.name)) continue;
                float lo = t.patch[X.a] ? t.atA[X.index] + UnionFanSetbackM : 0f;
                float hi = t.patch[X.b] ? X.length - t.atB[X.index] - UnionFanSetbackM : X.length;
                if (hi - lo < GroundTwinMinRunM) continue;
                twinWalk.Clear();
                twinEndsX.Clear(); twinEndsX.AddRange(StructureEndsOf(map, t, X));
                twinQueryAt = new Vector2(float.NaN, float.NaN);
                for (float x = lo; ; x += GroundTwinStepM)
                {
                    bool last = x >= hi - 1e-4f;
                    if (last) x = hi;
                    twinWalk.Add(GroundTwinSample(map, t, X, x, out var Y, out float y, out int side) ? (x, Y.index, y, side) : (x, -1, 0f, twinRefusedCovered ? 9 : 0));
                    if (last) break;
                }
                // stretches beside on one side; a gap of up to one missed sample bridged
                float bridge = 2f * GroundTwinStepM + 1e-3f;
                int i0 = -1, iLast = -1;
                for (int i = 0; i <= twinWalk.Count; i++)
                {
                    bool end = i == twinWalk.Count;
                    bool ok = !end && twinWalk[i].y >= 0;
                    if (ok && i0 >= 0 && twinWalk[i].side == twinWalk[i0].side && twinWalk[i].x - twinWalk[iLast].x <= bridge) { iLast = i; continue; }
                    if (!ok && !end && (i0 < 0 || twinWalk[i].x - twinWalk[iLast].x <= bridge)) continue;
                    bool shortFill = i0 >= 0 && TwinBridgesOn && twinWalk[iLast].x - twinWalk[i0].x < GroundTwinMinRunM
                                     && RunNear(t, X.index, twinWalk[i0].side, twinWalk[i0].x, twinWalk[iLast].x, GroundTwinSnapM + GroundTwinStepM);
                    if (i0 >= 0 && (twinWalk[iLast].x - twinWalk[i0].x >= GroundTwinMinRunM || shortFill))
                    {
                        // (round 6) an end next to a sample refused for a run already
                        // there is found to ~0.25 m, so the two meet instead of leaving
                        // up to a step of each carriageway's own wall between them
                        float x0o = float.NaN, x1o = float.NaN;
                        if (TwinBridgesOn && i0 > 0 && twinWalk[i0 - 1].y < 0 && twinWalk[i0 - 1].side == 9)
                            x0o = TwinEdgeBisect(map, t, X, twinWalk[i0].x, twinWalk[i0 - 1].x, twinWalk[i0].side);
                        if (TwinBridgesOn && iLast + 1 < twinWalk.Count && twinWalk[iLast + 1].y < 0 && twinWalk[iLast + 1].side == 9)
                            x1o = TwinEdgeBisect(map, t, X, twinWalk[iLast].x, twinWalk[iLast + 1].x, twinWalk[iLast].side);
                        float l = AddGroundTwinRun(map, t, X, i0, iLast, shortFill ? 2f : 0f, x0o, x1o);
                        if (l > 0f) { laid += l; added++; }
                    }
                    i0 = iLast = ok ? i : -1;
                }
            }
            return laid;
        }

        /// <summary>One sample of the ground-twin walk: the opposing twin beside
        /// arc <paramref name="x"/> of <paramref name="X"/>, if a shared median
        /// belongs between them there and X owns it.</summary>
        /// <summary>Why the ground-twin walk did or did not lay a median at arc
        /// <paramref name="x"/> of <paramref name="X"/> (the drive audit's
        /// MEDIAN ACROSS notes; the walk's own tests, in order).</summary>
        public static string TwinWhy { get; private set; } = "";
        public static string GroundTwinWhy(CityMap map, Trims t, CityMap.Edge X, float x)
        {
            if (X.a == X.b || !X.oneway || (X.bridge && !TwinBridgesOn) || !Barriered(X) || string.IsNullOrEmpty(X.name))
                return X.bridge && !TwinBridgesOn ? "an OSM bridge (decks: the twin table's)" : "not a barriered one-way";
            float lo = t.patch[X.a] ? t.atA[X.index] + UnionFanSetbackM : 0f;
            float hi = t.patch[X.b] ? X.length - t.atB[X.index] - UnionFanSetbackM : X.length;
            if (x < lo || x > hi) return "in its junction fan";
            twinEndsX.Clear(); twinEndsX.AddRange(StructureEndsOf(map, t, X));
            twinQueryAt = new Vector2(float.NaN, float.NaN);
            GroundTwinSample(map, t, X, x, out _, out _, out _);
            return TwinWhy;
        }

        static bool GroundTwinSample(CityMap map, Trims t, CityMap.Edge X, float x, out CityMap.Edge Y, out float y, out int side)
        {
            Y = null; y = 0f; side = 0; TwinWhy = ""; twinRefusedCovered = false;
            var p = X.PointAt(x);
            var tanX = X.TangentAt(x);
            float reach = X.HalfMax + GroundTwinGapMaxM + 12f, best = float.MaxValue;
            // short of the approach rail band at a structure end: its rails and
            // their W-beam lead-ins are the designed transition onto a deck
            if (TwinStopsAtBands && InApproachBand(twinEndsX, x)) { TwinWhy = "in an approach rail band"; return false; }
            // one grid query serves the next TwinQueryReuseM of the walk
            if (!(Vector2.Distance(p, twinQueryAt) <= TwinQueryReuseM))
            {
                twinQueryAt = p;
                float r = reach + TwinQueryReuseM;
                twinSegs.Clear();
                map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, twinSegs);
            }
            // each nearby SEGMENT the grid holds, projected on alone (a whole
            // edge's ProjectOn walks every point of a kilometre of freeway, and
            // this runs every 2 m of every one at load); the edge filter first
            foreach (int seg in twinSegs)
            {
                var c = map.edges[seg >> 12];
                if (c == X || c.a == c.b || !c.oneway || c.link || (c.bridge && !TwinBridgesOn) || c.name != X.name) continue;
                int si = seg & 0xFFF;
                if (si + 1 >= c.pts.Length) continue;
                Vector2 a0 = c.pts[si], dseg = c.pts[si + 1] - a0;
                float L2 = dseg.sqrMagnitude;
                if (L2 < 1e-8f) continue;
                float L = Mathf.Sqrt(L2);
                var dirC = dseg / L;
                if (Vector2.Dot(tanX, dirC) > -0.5f) continue;
                float along = Vector2.Dot(p - a0, dirC);
                // the foot must fall on the segment: within the slack past a
                // polyline end, half a metre past an inner joint (the outside
                // of a bend falls between two segments' feet)
                bool first = si == 0, lastSeg = si + 2 >= c.pts.Length;
                if (along < -(first ? UnionFootSlackM : 0.5f) || along > L + (lastSeg ? UnionFootSlackM : 0.5f)) continue;
                float tcl = Mathf.Clamp(along, 0f, L);
                var q = a0 + dirC * tcl;
                float d = Vector2.Distance(p, q);
                if (d < best) { best = d; Y = c; y = c.s[si] + tcl; }
            }
            // the lower index owns the pair's median: Y's own walk lays it
            if (Y == null) { TwinWhy = "no twin beside"; return false; }
            if (!twinEndsOf.TryGetValue(Y.index, out var endsY)) twinEndsOf[Y.index] = endsY = new List<float>(StructureEndsOf(map, t, Y));
            if (TwinStopsAtBands && InApproachBand(endsY, y)) { TwinWhy = $"twin e{Y.index} in an approach rail band"; return false; }
            if (Y.index < X.index) { TwinWhy = $"owned by e{Y.index}"; return false; }
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f;
            float yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            if (y < ylo - 1e-3f || y > yhi + 1e-3f) { TwinWhy = $"twin e{Y.index} in its fan"; return false; }
            if (Mathf.Abs(X.YAt(x) - Y.YAt(y)) > UnionSplitDyM) { TwinWhy = $"dy {Mathf.Abs(X.YAt(x) - Y.YAt(y)):0.00} m to e{Y.index}"; return false; }
            var q2 = Y.PointAt(y);
            int sx = SideToward(X, x, q2), sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (sx > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            if (gap > GroundTwinGapMaxM || gap < DeckPairs.SqueezeM) { TwinWhy = $"gap {gap:0.00} m to e{Y.index}"; return false; }
            // a deck's or an approach's run already there keeps its place
            if (TwinBridgesOn ? CoveredDrawn(map, t, X.index, sx, x) || CoveredDrawn(map, t, Y.index, sy, y) : CoveredOn(t, X.index, sx, x) || CoveredOn(t, Y.index, sy, y))
            { TwinWhy = $"covered (e{Y.index}, gap {gap:0.00})"; twinRefusedCovered = true; return false; }
            TwinWhy = $"ok e{Y.index} gap {gap:0.00} m";
            side = sx;
            return true;
        }

        /// <summary>A ground run's ends meet a run already on that side of the
        /// edge exactly when they stop within a few steps of it, so the two
        /// are one line (Continued) and no end turns down in the other's face,
        /// nor does the carriageway's own Jersey stand up in between.</summary>
        static void SnapToNeighbours(Trims t, int edge, int side, ref float s0, ref float s1)
        {
            var list = t.unions[edge];
            if (list == null) return;
            float reach = GroundTwinSnapM;
            foreach (var o in list)
            {
                if (o.side != side) continue;
                if (o.s1 <= s0 + 0.5f && s0 - o.s1 <= reach) s0 = o.s1;
                if (o.s0 >= s1 - 0.5f && o.s0 - s1 <= reach) s1 = o.s0;
            }
        }

        static bool CoveredOn(Trims t, int edge, int side, float s)
        {
            var list = t.unions[edge];
            if (list == null) return false;
            foreach (var o in list) if (o.side == side && o.Covers(s, 0.5f)) return true;
            return false;
        }

        /// <summary>twinWalk[i0..i1] as an owner run on X and a partner run on
        /// each twin edge it met (as <see cref="ExtendOverApproach"/> lays them).</summary>
        /// <summary>The last arc from <paramref name="xOk"/> toward <paramref name="xNo"/> the
        /// walk still lays a median at on that side (5 halvings).</summary>
        static float TwinEdgeBisect(CityMap map, Trims t, CityMap.Edge X, float xOk, float xNo, int side)
        {
            float a = xOk, b = xNo;
            for (int it = 0; it < 5; it++)
            {
                float m = 0.5f * (a + b);
                twinQueryAt = new Vector2(float.NaN, float.NaN);
                if (GroundTwinSample(map, t, X, m, out _, out _, out int sd) && sd == side) a = m; else b = m;
            }
            twinQueryAt = new Vector2(float.NaN, float.NaN);
            return a;
        }

        static float AddGroundTwinRun(CityMap map, Trims t, CityMap.Edge X, int i0, int i1, float minLen = 0f, float x0o = float.NaN, float x1o = float.NaN)
        {
            float x0 = float.IsNaN(x0o) ? twinWalk[i0].x : x0o, x1 = float.IsNaN(x1o) ? twinWalk[i1].x : x1o;
            int side = twinWalk[i0].side;
            metY.Clear();
            int kMidAll = i0;
            for (int k = i0; k <= i1; k++)
            {
                if (twinWalk[k].y < 0) continue;
                var Yk = map.edges[twinWalk[k].y];
                if (!metY.Contains(Yk)) metY.Add(Yk);
                if (k <= (i0 + i1) / 2) kMidAll = k;
            }
            if (x0 <= GroundTwinSnapM && !t.patch[X.a]) x0 = 0f;
            if (X.length - x1 <= GroundTwinSnapM && !t.patch[X.b]) x1 = X.length;
            SnapToNeighbours(t, X.index, side, ref x0, ref x1);
            // (round 6) a short stretch filling a gap: only when it spans one after snapping
            if (minLen > 0f && x1 - x0 < minLen) return 0f;
            var acrossMet = new int[metY.Count];
            for (int k = 0; k < metY.Count; k++) acrossMet[k] = metY[k].index;
            var ra = new UnionRun { edge = X.index, nb = twinWalk[kMidAll].y, side = side, s0 = x0, s1 = x1, median = DeckPairs.Median.Barrier,
                                    owner = true, pair = -1, approach = true, across = acrossMet };
            AddRun(t, ra);
            var acrossX = ChainAround(map, t, X);
            foreach (var be in metY)
            {
                float lo = float.MaxValue, hi = float.MinValue; int kMid = -1, vote = 0;
                for (int k = i0; k <= i1; k++)
                {
                    if (twinWalk[k].y != be.index) continue;
                    lo = Mathf.Min(lo, twinWalk[k].ty); hi = Mathf.Max(hi, twinWalk[k].ty); kMid = k;
                    // (round 6) each sample's own pair of points: the mid ARC against the
                    // LAST sample's point read the wrong side on a curve (I-277 e2315: its
                    // partner run on its outside, so its own median Jersey stood 1.4 m
                    // from the union's for 482 m - the owner's "rectangles")
                    vote += SideToward(be, twinWalk[k].ty, X.PointAt(twinWalk[k].x));
                }
                if (kMid < 0) continue;
                lo = Mathf.Max(0f, lo - 0.5f * GroundTwinStepM); hi = Mathf.Min(be.length, hi + 0.5f * GroundTwinStepM);
                // to the edge's end when the walk stopped a few steps short of
                // it, or the carriageway's own Jersey stands up there as a stub
                if (lo <= GroundTwinSnapM) lo = 0f;
                if (be.length - hi <= GroundTwinSnapM) hi = be.length;
                if (hi - lo < 0.25f) continue;
                int sideB = !TwinSideVoteOn ? SideToward(be, 0.5f * (lo + hi), X.PointAt(twinWalk[kMid].x)) : vote >= 0 ? 1 : -1;
                SnapToNeighbours(t, be.index, sideB, ref lo, ref hi);
                var rb = new UnionRun { edge = be.index, nb = X.index, side = sideB, s0 = lo, s1 = hi,
                                        median = DeckPairs.Median.Barrier, owner = false, pair = -1, approach = true, across = acrossX, mirror = ra };
                AddRun(t, rb);
                if (ra.mirror == null || be.index == ra.nb) ra.mirror = rb;
            }
            if (ra.mirror == null) ra.mirror = new UnionRun { edge = ra.nb, nb = X.index, side = -side, median = DeckPairs.Median.Barrier, pair = -1, approach = true, across = acrossX };
            return x1 - x0;
        }

        static readonly List<(CityMap.Edge e, float from, int dir)> bChain = new List<(CityMap.Edge, float, int)>(6);
        static int[] bAcross;
        /// <summary>The walk's place in <see cref="bAcross"/>: the road across
        /// is searched there and one edge either side (it runs on in order).</summary>
        static int bCur;
        static readonly int[] bWin = new int[4];
        static readonly List<CityMap.Edge> metY = new List<CityMap.Edge>(8);
        /// <summary>Where the last refused sample's blocking run starts (or
        /// ends) on the walked edge; -1 when it was refused for anything else.</summary>
        static float approachBlock = -1f;
        static readonly List<(CityMap.Edge X, float x, CityMap.Edge Y, float y)> walk = new List<(CityMap.Edge, float, CityMap.Edge, float)>(128);

        static int SideToward(CityMap.Edge e, float s, Vector2 q)
        {
            var p = e.PointAt(s); var tan = e.TangentAt(s);
            return ((q.x - p.x) * -tan.y + (q.y - p.y) * tan.x) >= 0f ? 1 : -1;
        }

        /// <summary>One sample of the approach walk: the road across beside
        /// it, and whether a median belongs between them there.</summary>
        static bool ApproachSample(CityMap map, Trims t, UnionRun r, CityMap.Edge X, float x, float walked,
                                   float gMin, float gMax, float dyMax, out CityMap.Edge Y, out float y, bool ignoreRuns = false)
        {
            Y = null; y = 0f;
            approachBlock = -1f;
            // short of a fan: the ribbon ends at its trim there
            float lo = t.patch[X.a] ? t.atA[X.index] + UnionFanSetbackM : 0f;
            float hi = t.patch[X.b] ? X.length - t.atB[X.index] - UnionFanSetbackM : X.length;
            if (x < lo - 1e-3f || x > hi + 1e-3f) return false;
            // A Jersey carries on over structure the solve made (fill drawn as
            // deck), never onto an OSM bridge: twin bridges not in the union
            // table are two structures, kept apart.
            bool overStructure = r.median == DeckPairs.Median.Barrier;
            if (walked > 3f && X.ElevatedAt(x) && (!overStructure || X.bridge)) return false;
            var p = X.PointAt(x);
            int nw = 0;
            for (int k = Mathf.Max(0, bCur - 1); k < bAcross.Length && k <= bCur + 2; k++) bWin[nw++] = bAcross[k];
            if (!FootOnN(map, bWin, nw, p, UnionFootSlackM, out Y, out y, out float best)) return false;
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f;
            float yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            if (y < ylo - 1e-3f || y > yhi + 1e-3f) return false;
            if (walked > 3f && Y.ElevatedAt(y) && (!overStructure || Y.bridge)) return false;
            // the road across is this road's twin carriageway: no ramp, the
            // same name, running the other way
            if (X.link || Y.link || string.IsNullOrEmpty(X.name) || X.name != Y.name
                || Vector2.Dot(X.TangentAt(x), Y.TangentAt(y)) > -0.5f) return false;
            if (Mathf.Abs(X.YAt(x) - Y.YAt(y)) > dyMax) return false;
            // the gap between the two drawn edges facing each other
            var q2 = Y.PointAt(y);
            int sx = SideToward(X, x, q2), sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (sx > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            if (gap > gMax || gap < gMin) return false;
            // never across a run already there (the next structure's, or
            // another approach's): refused, with where that run begins
            var list = t.unions[X.index];
            if (!ignoreRuns && list != null)
                foreach (var o in list)
                    if (o.side == sx && o.Covers(x, -0.05f) && o != r)
                    {
                        approachBlock = Mathf.Abs(o.s0 - x) <= Mathf.Abs(o.s1 - x) ? o.s0 : o.s1;
                        return false;
                    }
            for (int k = Mathf.Max(0, bCur - 1); k < bAcross.Length && k <= bCur + 2; k++)
                if (bAcross[k] == Y.index) { bCur = k; break; }
            return true;
        }

        /// <summary><see cref="FootOn"/> over the first <paramref name="n"/>
        /// of <paramref name="cands"/>.</summary>
        static bool FootOnN(CityMap map, int[] cands, int n, Vector2 p, float slack, out CityMap.Edge Y, out float y, out float dist)
        {
            Y = null; y = 0f; dist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = map.edges[cands[i]];
                CityElevation.ProjectOn(c, p, out float tc);
                var q = c.PointAt(tc);
                float d = Vector2.Distance(p, q);
                if (d >= dist || Mathf.Abs(Vector2.Dot(p - q, c.TangentAt(tc))) > slack) continue;
                dist = d; Y = c; y = tc;
            }
            return Y != null;
        }

        // ------------------------------------------------------------------
        //  The median, drawn by the owner, span by span
        // ------------------------------------------------------------------

        static readonly Vector3[] uA = new Vector3[64], uB = new Vector3[64];
        static readonly float[] uV = new float[64], uS = new float[64];
        static readonly bool[] uOk = new bool[64];
        static readonly float[] uEnd = new float[64];
        static readonly bool[] uOut = new bool[64];
        /// <summary>(round 6) The pieces' place in a GROUND V's legs (1: before them).</summary>
        static readonly float[] uLeg0 = new float[64], uLeg1 = new float[64];

        /// <summary>
        /// THE GROUND V (REAL HEIGHTS round 6; route rail ends I-277 e2144 s 500,
        /// I-77 e1874 s 188). A union run on the ground closed where a carriageway's
        /// structure begins within <see cref="TaperLenM"/> past it: that
        /// carriageway's approach rail starts AT the run's end and guards a drop to
        /// its very end, so it kept its square end - facing the traffic along the
        /// edge line beside the turned-down Jersey. Now the median ends there as
        /// it does on a deck: a leg (Jersey high, never steeper than 1:3) from the
        /// gap's centre line to each such carriageway's edge, its end inside the
        /// rail's; with both carriageways on structure there the legs are the
        /// whole end (a V, no turned-down nose), with one the Jersey turns down
        /// beside its leg. PSX_CITY_GROUND_V=0: before.
        /// </summary>
        public static bool GroundVOn = System.Environment.GetEnvironmentVariable("PSX_CITY_GROUND_V") != "0";
        static readonly HashSet<int> gvSegs = new HashSet<int>();
        /// <summary>Which carriageways start structure within TaperLenM past a
        /// ground union run's closed end (dir +1: past s1, -1: before s0): the
        /// owner's own edge, and the road across (any one-way road the other
        /// way beside it within the twins' gap).</summary>
        static void GroundVEnd(CityMap map, CityMap.Edge e, float sEnd, int dir, out bool own, out bool across)
        {
            own = across = false;
            for (float x = 0.5f; x <= TaperLenM + 1e-3f; x += 0.5f)
            {
                float s = sEnd + dir * x;
                if (s < 0f || s > e.length) break;
                if (!own && e.ElevatedAt(s)) own = true;
                if (!across)
                {
                    var p = e.PointAt(s); var t = e.TangentAt(s).normalized;
                    float r = e.HalfMax + GroundTwinGapMaxM + 14f;
                    gvSegs.Clear();
                    map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, gvSegs);
                    foreach (int seg in gvSegs)
                    {
                        var o = map.edges[seg >> 12];
                        if (o == e || !o.oneway || o.link) continue;
                        int si = seg & 0xFFF;
                        if (si + 1 >= o.pts.Length) continue;
                        Vector2 a0 = o.pts[si], dseg = o.pts[si + 1] - a0;
                        float L = dseg.magnitude;
                        if (L < 1e-4f) continue;
                        var dirO = dseg / L;
                        if (Vector2.Dot(t, dirO) > -0.8f) continue;
                        float along = Mathf.Clamp(Vector2.Dot(p - a0, dirO), 0f, L);
                        if (Vector2.Distance(p, a0 + dirO * along) - e.HalfMax - o.HalfMax > GroundTwinGapMaxM) continue;
                        if (o.ElevatedAt(o.s[si] + along)) { across = true; break; }
                    }
                }
                if (own && across) break;
            }
        }

        /// <summary>
        /// The union median beside one span of its owner (EmitSide's union
        /// side; <paramref name="i"/> the span), from the owner's drawn edge to
        /// the partner's drawn edge, in pieces no longer than
        /// <see cref="UnionPieceM"/>; and the run's closed ends where the span
        /// holds them. Tile-local like everything else.
        /// </summary>
        static void EmitUnionMedian(CityMap map, Trims trims, TileMeshes tm, CityMap.Edge e, int i, int side, float v0, float v1, bool deck, UnionRun run)
        {
            var A = sections[i - 1]; var B = sections[i];
            int pieces = Mathf.Clamp(Mathf.CeilToInt((B.s - A.s) / UnionPieceM), 1, uA.Length - 1);
            var org = tm.origin;
            // a run "carried on" across a node into a road whose drawn edge on
            // this side is not where ours ends has nothing to meet: its end is
            // CLOSED (the V on a deck, the turned-down Jersey on the ground) -
            // left open, the Jersey's hollow end stood in the lanes of the road
            // before it with no face drawn toward the car (INVISIBLE COLLIDERS,
            // the owner's I-277 wall at the left exit by East 4th Street)
            bool open0 = run.open0 || UnionEndAdrift(map, trims, e, run, 0, side, org);
            bool open1 = run.open1 || UnionEndAdrift(map, trims, e, run, 1, side, org);
            // (round 6) the ground V's legs at a closed end where structure begins past it
            bool vO0 = false, vP0 = false, vO1 = false, vP1 = false;
            if (!deck && GroundVOn && run.median == DeckPairs.Median.Barrier)
            {
                if (open0) GroundVEnd(map, e, run.s0, -1, out vO0, out vP0);
                if (open1) GroundVEnd(map, e, run.s1, +1, out vO1, out vP1);
            }
            bool vee0 = vO0 || vP0, vee1 = vO1 || vP1, full0 = vO0 && vP0, full1 = vO1 && vP1;
            for (int k = 0; k <= pieces; k++)
            {
                float f = (float)k / pieces;
                uS[k] = Mathf.Lerp(A.s, B.s, f);
                uV[k] = Mathf.Lerp(v0, v1, f);
                uA[k] = Vector3.Lerp(A.Edge(side), B.Edge(side), f);
                var pw = new Vector2(uA[k].x + org.x, uA[k].z + org.z);
                // the road across here: the partner, or the edge it carries on
                // into where the two decks do not end square
                var pl = e.PointAt(uS[k]);
                FootAny(map, run.across, pl, out var o, out float tk);
                var w = FacingEdgeAt(map, trims, o, tk, pl);
                uB[k] = w - org;
                // nothing to span where the two drawn edges touch - or where
                // the road across is drawn INTO this one (its edge past ours,
                // inside our lanes): a median exists only where the two are
                // apart, and stands in no lane (hotfix 2026-10-03)
                var ow = Vector2.Lerp(A.Out(side), B.Out(side), f).normalized;
                uOk[k] = Vector2.Distance(pw, new Vector2(w.x, w.z)) > 0.05f;
                uOut[k] = Vector2.Dot(new Vector2(w.x, w.z) - pw, ow) > 0.05f;
                // how far to the nearest closed end of the run (a sloped end,
                // a V whose legs are never steeper than 1:3 to the traffic)
                float d0 = open0 ? uS[k] - run.s0 : float.MaxValue, d1 = open1 ? run.s1 - uS[k] : float.MaxValue;
                float endLen = Mathf.Max(TaperLenM, deck ? 1.5f * Vector2.Distance(pw, new Vector2(w.x, w.z)) : 0f);
                // (round 6) a ground V: its legs as long as a deck's; a full V
                // takes the end from the Jersey (no turned-down nose there)
                float legLen = Mathf.Max(TaperLenM, 1.5f * Vector2.Distance(pw, new Vector2(w.x, w.z)));
                uLeg0[k] = vee0 ? Mathf.Clamp01((uS[k] - run.s0) / legLen) : 1f;
                uLeg1[k] = vee1 ? Mathf.Clamp01((run.s1 - uS[k]) / legLen) : 1f;
                if (full0) d0 = float.MaxValue;
                if (full1) d1 = float.MaxValue;
                uEnd[k] = Mathf.Clamp01(Mathf.Min(d0, d1) / endLen);
            }
            var bk = buckets[(int)RoadSlot(e, deck)];
            var lay = LineModel.LayoutOf(e.profile);
            float tw = lay.texW;
            float gA = (lay.x0.Length > 0 ? lay.x0[0] - 0.25f : tw - 0.25f) / tw, gB = 0.25f / tw;   // the paint-free shoulder texels
            var con = buckets[(int)Slot.Concrete];
            float dk = CityElevation.DeckThick;
            // a median shorter than MinMedianRunM, closed at both ends, is a
            // flush strip: no isolated block between two roads
            bool solid = UnionDrawsSolid(run);
            var shape = solid ? run.median : DeckPairs.Median.Flush;
            for (int k = 0; k < pieces; k++)
            {
                if (!uOk[k] || !uOk[k + 1]) continue;
                bool stand = uOut[k] && uOut[k + 1];
                Profile(stand ? shape : DeckPairs.Median.Flush, uA[k], uB[k], prof0, out int n0);
                Profile(stand ? shape : DeckPairs.Median.Flush, uA[k + 1], uB[k + 1], prof1, out int n1);
                if (n0 != n1) { Profile(DeckPairs.Median.Flush, uA[k], uB[k], prof0, out n0); Profile(DeckPairs.Median.Flush, uA[k + 1], uB[k + 1], prof1, out n1); }
                // the surface across, quad by quad (curb faces battered: they face up)
                for (int j = 0; j + 1 < n0; j++)
                {
                    float uj = Mathf.Lerp(gA, gB, Mathf.Clamp01(profU[j])), uj1 = Mathf.Lerp(gA, gB, Mathf.Clamp01(profU[j + 1]));
                    bk.Up(prof0[j], prof1[j], prof1[j + 1], prof0[j + 1],
                          new Vector2(uj, uV[k]), new Vector2(uj, uV[k + 1]), new Vector2(uj1, uV[k + 1]), new Vector2(uj1, uV[k]));
                }
                if (deck)
                {
                    // the soffit across the gap, continuing both decks' own
                    con.Down(uA[k] + Vector3.down * dk, uB[k] + Vector3.down * dk, uB[k + 1] + Vector3.down * dk, uA[k + 1] + Vector3.down * dk,
                             new Vector2(0f, uV[k]), new Vector2(1f, uV[k]), new Vector2(1f, uV[k + 1]), new Vector2(0f, uV[k + 1]));
                }
                // THE ENDS (hotfix 2026-10-03). On a deck the median splits
                // into a V over the last TaperLenM, its legs meeting each
                // carriageway's own parapet at the edge where the slab ends
                // (no rail square across the slab); on the ground a Jersey is
                // turned down to a curb.
                if (!stand) continue;
                if (deck && (uEnd[k] < 1f || uEnd[k + 1] < 1f))
                    EmitUnionLegs(uA[k], uB[k], uA[k + 1], uB[k + 1], uEnd[k], uEnd[k + 1], uV[k], uV[k + 1]);
                else if (shape == DeckPairs.Median.Barrier)
                {
                    bool inV0 = uLeg0[k] < 1f || uLeg0[k + 1] < 1f, inV1 = uLeg1[k] < 1f || uLeg1[k + 1] < 1f;
                    if (!(full0 && inV0) && !(full1 && inV1))
                        EmitUnionJersey(uA[k], uB[k], uA[k + 1], uB[k + 1], uV[k], uV[k + 1],
                                        deck ? BarrierH : Mathf.Lerp(TaperFootM, BarrierH, uEnd[k]), deck ? BarrierH : Mathf.Lerp(TaperFootM, BarrierH, uEnd[k + 1]));
                    if (vee0 && inV0) EmitUnionLegs(uA[k], uB[k], uA[k + 1], uB[k + 1], uLeg0[k], uLeg0[k + 1], uV[k], uV[k + 1], vO0, vP0);
                    if (vee1 && inV1) EmitUnionLegs(uA[k], uB[k], uA[k + 1], uB[k + 1], uLeg1[k], uLeg1[k + 1], uV[k], uV[k + 1], vO1, vP1);
                }
            }
            // the closed ends this span holds
            for (int end = 0; end < 2; end++)
            {
                bool open = end == 0 ? open0 && Mathf.Abs(A.s - run.s0) < 0.3f : open1 && Mathf.Abs(B.s - run.s1) < 0.3f;
                if (!open) continue;
                int k = end == 0 ? 0 : pieces, kIn = end == 0 ? 1 : pieces - 1;
                if (!uOk[k]) continue;
                // outward along the run, in plan
                var outw = new Vector2(uA[k].x - uA[kIn].x, uA[k].z - uA[kIn].z);
                if (outw.sqrMagnitude < 1e-6f) continue;
                outw.Normalize();
                // (a full ground V has no Jersey end to cap)
                bool fullV = end == 0 ? full0 : full1;
                EmitUnionEnd(map, tm, e, run, side, uA[k], uB[k], outw, deck, uV[k], end == 0 ? run.s0 : run.s1, uOut[k] && !fullV ? shape : DeckPairs.Median.Flush);
            }
        }

        static readonly Vector3[] prof0 = new Vector3[8], prof1 = new Vector3[8];
        static readonly float[] profU = new float[8];

        /// <summary>The cross profile of a median from the owner's edge
        /// <paramref name="a"/> to the partner's <paramref name="b"/> (tile
        /// space, each at its road's height): tucked under both, an inch down;
        /// RAISED adds the two battered curbs and the top. profU: 0 at a, 1 at b.</summary>
        static void Profile(DeckPairs.Median kind, Vector3 a, Vector3 b, Vector3[] into, out int n)
        {
            var d = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float gap = d.magnitude;
            d = gap > 1e-4f ? d / gap : Vector3.right;
            var dn = Vector3.down * RoadsideRules.EdgeDropM;
            int c = 0;
            into[c] = a - d * UnionTuckM + dn; profU[c++] = 0f;
            into[c] = a + dn; profU[c++] = 0f;
            if (kind == DeckPairs.Median.Raised && gap > 2f * RaisedBatterM + 0.2f)
            {
                into[c] = a + d * RaisedBatterM + Vector3.up * RaisedCurbM; profU[c++] = RaisedBatterM / gap;
                into[c] = b - d * RaisedBatterM + Vector3.up * RaisedCurbM; profU[c++] = 1f - RaisedBatterM / gap;
            }
            into[c] = b + dn; profU[c++] = 1f;
            into[c] = b + d * UnionTuckM + dn; profU[c++] = 1f;
            n = c;
        }

        /// <summary>A union's Jersey on the gap's centre line between two
        /// cross-lines (owner edge a, partner edge b at each), its feet on the
        /// strip at each side's height and its top 0.81 m over the higher.</summary>
        static void EmitUnionJersey(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float v0, float v1,
                                    float h0 = BarrierH, float h1 = BarrierH)
        {
            var bk = barrierBucket;
            Vector3 c0 = 0.5f * (a0 + b0), c1 = 0.5f * (a1 + b1);
            var d0 = new Vector3(b0.x - a0.x, 0f, b0.z - a0.z); float g0 = d0.magnitude; d0 = g0 > 1e-4f ? d0 / g0 : Vector3.right;
            var d1 = new Vector3(b1.x - a1.x, 0f, b1.z - a1.z); float g1 = d1.magnitude; d1 = g1 > 1e-4f ? d1 / g1 : Vector3.right;
            float w0 = Mathf.Min(BarrierW * 0.5f, Mathf.Max(0.1f, 0.5f * g0 - 0.1f)), w1 = Mathf.Min(BarrierW * 0.5f, Mathf.Max(0.1f, 0.5f * g1 - 0.1f));
            Vector3 pa0 = c0 - d0 * w0, pb0 = c0 + d0 * w0, pa1 = c1 - d1 * w1, pb1 = c1 + d1 * w1;
            float top0 = Mathf.Max(a0.y, b0.y) + h0, top1 = Mathf.Max(a1.y, b1.y) + h1;
            // the feet: the strip's height there, a kerb face lower
            float fa0 = Mathf.Lerp(a0.y, b0.y, 0.5f - w0 / Mathf.Max(g0, 1e-3f)) - KerbFaceM, fb0 = Mathf.Lerp(a0.y, b0.y, 0.5f + w0 / Mathf.Max(g0, 1e-3f)) - KerbFaceM;
            float fa1 = Mathf.Lerp(a1.y, b1.y, 0.5f - w1 / Mathf.Max(g1, 1e-3f)) - KerbFaceM, fb1 = Mathf.Lerp(a1.y, b1.y, 0.5f + w1 / Mathf.Max(g1, 1e-3f)) - KerbFaceM;
            var toA = new Vector2(-d0.x, -d0.z);
            bk.WallSloped(pa0, pa1, fa0, top0, fa1, top1, toA, v0, v1, 0.3f, 0.45f);
            bk.WallSloped(pb0, pb1, fb0, top0, fb1, top1, -toA, v0, v1, 0.3f, 0.45f);
            bk.Up(new Vector3(pa0.x, top0, pa0.z), new Vector3(pa1.x, top1, pa1.z), new Vector3(pb1.x, top1, pb1.z), new Vector3(pb0.x, top0, pb0.z),
                  new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
        }

        /// <summary>
        /// THE V at a union's closed end on a deck (hotfix 2026-10-03): between
        /// two cross-lines (owner edge a, partner edge b at each) where the end
        /// is <paramref name="f0"/>/<paramref name="f1"/> of TaperLenM away,
        /// two Jersey-high legs - from the gap's centre line at TaperLenM to
        /// each carriageway's edge at the end, as wide as the parapet each
        /// carriageway carries on with (RailW inside its edge, RailOverhangM
        /// outside), so its end is inside the leg's.
        /// Every face is at a glancing angle to the traffic; nothing stands
        /// square across the strip.
        /// </summary>
        /// <summary>Does a union run's end at a node (s0 at a, s1 at b) meet
        /// nothing: the through road's drawn edge on this side stands away
        /// from where this edge's ends (CityMeshes.EdgeLinesUp)?</summary>
        static bool UnionEndAdrift(CityMap map, Trims trims, CityMap.Edge e, UnionRun run, int end, int side, Vector3 origin)
        {
            float s = end == 0 ? run.s0 : run.s1;
            bool atNode = end == 0 ? s <= 0.05f : s >= e.length - 0.05f;
            if (!atNode || sections.Count < 2) return false;
            var sec = end == 0 ? sections[0] : sections[sections.Count - 1];
            return !EdgeLinesUp(map, trims, e, end == 0 ? e.a : e.b, side, sec, origin);
        }

        static void EmitUnionLegs(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float f0, float f1, float v0, float v1, bool legA = true, bool legB = true)
        {
            var g0 = new Vector3(b0.x - a0.x, 0f, b0.z - a0.z); float l0 = g0.magnitude; g0 = l0 > 1e-4f ? g0 / l0 : Vector3.right;
            var g1 = new Vector3(b1.x - a1.x, 0f, b1.z - a1.z); float l1 = g1.magnitude; g1 = l1 > 1e-4f ? g1 / l1 : Vector3.right;
            float half = 0.5f * (RailW + RailOverhangM);
            float top0 = Mathf.Max(a0.y, b0.y) + BarrierH, top1 = Mathf.Max(a1.y, b1.y) + BarrierH;
            for (int leg = 0; leg < 2; leg++)
            {
                if (leg == 0 ? !legA : !legB) continue;
                // the leg's centre line: on the edge at the end (the parapet's
                // own line there), on the gap's middle TaperLenM back
                Vector3 e0 = leg == 0 ? a0 - g0 * (0.5f * (RailW - RailOverhangM)) : b0 + g0 * (0.5f * (RailW - RailOverhangM)),
                        e1 = leg == 0 ? a1 - g1 * (0.5f * (RailW - RailOverhangM)) : b1 + g1 * (0.5f * (RailW - RailOverhangM));
                Vector3 p0 = Vector3.Lerp(e0, 0.5f * (a0 + b0), f0), p1 = Vector3.Lerp(e1, 0.5f * (a1 + b1), f1);
                var dir = new Vector2(p1.x - p0.x, p1.z - p0.z);
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();
                var nrm = new Vector2(-dir.y, dir.x);
                var n3 = new Vector3(nrm.x, 0f, nrm.y) * half;
                float t0 = l0 > 1e-3f ? Mathf.Clamp01(Vector3.Dot(p0 - a0, g0) / l0) : 0.5f;
                float t1 = l1 > 1e-3f ? Mathf.Clamp01(Vector3.Dot(p1 - a1, g1) / l1) : 0.5f;
                float foot0 = Mathf.Lerp(a0.y, b0.y, t0) - KerbFaceM, foot1 = Mathf.Lerp(a1.y, b1.y, t1) - KerbFaceM;
                barrierBucket.WallSloped(p0 + n3, p1 + n3, foot0, top0, foot1, top1, nrm, v0, v1, 0.3f, 0.45f);
                barrierBucket.WallSloped(p0 - n3, p1 - n3, foot0, top0, foot1, top1, -nrm, v0, v1, 0.3f, 0.45f);
                barrierBucket.Up(new Vector3(p0.x + n3.x, top0, p0.z + n3.z), new Vector3(p1.x + n3.x, top1, p1.z + n3.z),
                                 new Vector3(p1.x - n3.x, top1, p1.z - n3.z), new Vector3(p0.x - n3.x, top0, p0.z - n3.z),
                                 new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
                // the leg's end at the run's closed end: a face, never a hollow
                // end (where a carriageway's parapet carries on it is inside it)
                var d3 = new Vector3(dir.x, 0f, dir.y);
                var uvC = new Vector2(0.3f, v0);
                if (f0 <= 1e-3f)
                    barrierBucket.Face(new Vector3(p0.x - n3.x, foot0, p0.z - n3.z), new Vector3(p0.x - n3.x, top0, p0.z - n3.z),
                                       new Vector3(p0.x + n3.x, top0, p0.z + n3.z), new Vector3(p0.x + n3.x, foot0, p0.z + n3.z), -d3, uvC, uvC, uvC, uvC);
                if (f1 <= 1e-3f)
                    barrierBucket.Face(new Vector3(p1.x - n3.x, foot1, p1.z - n3.z), new Vector3(p1.x - n3.x, top1, p1.z - n3.z),
                                       new Vector3(p1.x + n3.x, top1, p1.z + n3.z), new Vector3(p1.x + n3.x, foot1, p1.z + n3.z), d3, uvC, uvC, uvC, uvC);
            }
        }

        /// <summary>
        /// A union run's closed end at the cross-line a (owner) - b (partner):
        /// on a deck, an end face from the median's surface down to the soffit
        /// and a rail across the slab's end (so nothing leaves the slab where
        /// the decks carry on apart, one ends, or the approaches part); on the
        /// ground, a raised median's battered end curb. A Jersey is capped.
        /// </summary>
        static void EmitUnionEnd(CityMap map, TileMeshes tm, CityMap.Edge e, UnionRun run, int side, Vector3 a, Vector3 b, Vector2 outw, bool deck, float v, float sEnd,
                                 DeckPairs.Median shape)
        {
            Profile(shape, a, b, prof0, out int n);
            var o3 = new Vector3(outw.x, 0f, outw.y);
            var con = buckets[(int)Slot.Concrete];
            float dk = CityElevation.DeckThick;
            if (deck)
            {
                for (int j = 0; j + 1 < n; j++)
                {
                    Vector3 p = prof0[j], q = prof0[j + 1];
                    float yb = Mathf.Min(p.y, q.y) - dk;
                    con.Face(p, q, new Vector3(q.x, yb, q.z), new Vector3(p.x, yb, p.z), o3,
                             new Vector2(0f, v), new Vector2(1f, v), new Vector2(1f, v + 0.05f), new Vector2(0f, v + 0.05f));
                }
                // No rail square across the slab's end (hotfix 2026-10-03: a
                // 90-degree wall across the median strip): the V's legs
                // (EmitUnionLegs) guard it, meeting each carriageway's parapet.
            }
            else if (shape == DeckPairs.Median.Raised && n >= 6)
            {
                // the curb across the end, battered outward like the sides,
                // down to the strip's level; a corner piece at each side curb
                Vector3 ta = prof0[2], tb = prof0[3], ba = prof0[1], bb = prof0[4];
                var bat = o3 * RaisedBatterM;
                Vector3 ea = new Vector3(ta.x, ba.y, ta.z) + bat, eb = new Vector3(tb.x, bb.y, tb.z) + bat;
                var lay = LineModel.LayoutOf(e.profile);
                float tw = lay.texW;
                float gA = (lay.x0.Length > 0 ? lay.x0[0] - 0.25f : tw - 0.25f) / tw, gB = 0.25f / tw;
                var bk = buckets[(int)RoadSlot(e, false)];
                bk.Up(ta, ea, eb, tb, new Vector2(gA, v), new Vector2(gA, v + 0.01f), new Vector2(gB, v + 0.01f), new Vector2(gB, v));
                bk.Up(ba, ea, ta, ta, new Vector2(gA, v), new Vector2(gA, v + 0.01f), new Vector2(gA, v), new Vector2(gA, v));
                bk.Up(bb, tb, tb, eb, new Vector2(gB, v), new Vector2(gB, v), new Vector2(gB, v), new Vector2(gB, v + 0.01f));
            }
            if (shape == DeckPairs.Median.Barrier && !deck)
            {
                // the Jersey's cap on the end line, at the turned-down height
                Vector3 c = 0.5f * (a + b);
                var d = new Vector3(b.x - a.x, 0f, b.z - a.z); float g = d.magnitude; d = g > 1e-4f ? d / g : Vector3.right;
                float h = Mathf.Min(BarrierW * 0.5f, Mathf.Max(0.1f, 0.5f * g - 0.1f));
                float top = Mathf.Max(a.y, b.y) + TaperFootM, foot = Mathf.Min(a.y, b.y) - KerbFaceM;
                Vector3 l = c - d * h, r = c + d * h;
                var uv = new Vector2(0.3f, v);
                barrierBucket.Face(new Vector3(l.x, foot, l.z), new Vector3(l.x, top, l.z), new Vector3(r.x, top, r.z), new Vector3(r.x, foot, r.z), o3, uv, uv, uv, uv);
            }
        }

        /// <summary>The partner's drawn edge on <paramref name="side"/> at arc
        /// <paramref name="t"/>, world space: its sections as this tile cut
        /// them (squeeze, clip, eases), straight between them as its mesh
        /// is; the line model's edge at the solved height where it has none.</summary>
        static Vector3 DrawnEdgeAt(CityMap map, Trims trims, CityMap.Edge o, float t, int side)
        {
            float sMin = trims.atA[o.index], sMax = o.length - trims.atB[o.index];
            if (sMax - sMin >= 0.6f && t >= sMin - 0.01f && t <= sMax + 0.01f)
            {
                var secs = RawSectionsOf(map, trims, o, sMin, sMax);
                if (secs.Count >= 2)
                {
                    int lo = 0, hi = secs.Count - 1;
                    if (t <= secs[0].s) hi = 1;
                    else if (t >= secs[hi].s) lo = hi - 1;
                    else while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (secs[mid].s <= t) lo = mid; else hi = mid; }
                    var S0 = secs[lo]; var S1 = secs[hi];
                    float f = S1.s - S0.s > 1e-5f ? Mathf.Clamp01((t - S0.s) / (S1.s - S0.s)) : 0f;
                    return Vector3.Lerp(S0.Edge(side), S1.Edge(side), f);
                }
            }
            LineModel.Extents(o, t, out float eM, out float eP);
            var p = o.PointAt(t); var tan = o.TangentAt(t);
            var right = new Vector2(-tan.y, tan.x);
            var q = side > 0 ? p + right * eP : p - right * eM;
            return new Vector3(q.x, o.YAt(t), q.y);
        }

        // ------------------------------------------------------------------
        //  A BRANCH COLLAPSED INTO ITS HOST (round 5, the owner's "concrete
        //  box" in I-277's lane at the US 74 merge, uptown 2,486 m): I-277
        //  e2321 merges into US 74 e2367 and its ribbon collapses into the
        //  host's pavement 61 m before their node, while its union with the
        //  opposing carriageway (e1394, then e2350) runs on to 84 / 128 m.
        //  The collapsed sections stand on the cut line (for a metre 2.6 m
        //  inside I-277's lanes), so the owner's median swung into the lane
        //  (WALLS IN LANES 2, BLUNT 1); the host's median side drew its own
        //  Jersey a metre from the union's (a box: two walls); and the
        //  collapsed owner's spans are skipped, so its run drew nothing.
        //  The road across's facing edge is the HOST's where the branch is
        //  collapsed, and the host's side there is the union's: nothing of
        //  its own, the median drawn by the owner - by the host itself when
        //  the collapsed branch is that owner.
        // ------------------------------------------------------------------

        /// <summary>The drawn edge of <paramref name="o"/> facing
        /// <paramref name="p"/> at arc <paramref name="t"/>, world space
        /// (<see cref="DrawnEdgeAt"/>); where o is a branch collapsed into its
        /// host there, the host's drawn edge facing p.</summary>
        static Vector3 FacingEdgeAt(CityMap map, Trims trims, CityMap.Edge o, float t, Vector2 p)
        {
            if (CollapseHostOn && CollapsedAt(map, trims, o, t))
                for (int end = 0; end < 2; end++)
                {
                    int h = end == 0 ? trims.branchA[o.index] : trims.branchB[o.index];
                    if (h < 0 || h == o.index) continue;
                    var H = map.edges[h];
                    CityElevation.ProjectOn(H, p, out float th);
                    if (Mathf.Abs(Vector2.Dot(p - H.PointAt(th), H.TangentAt(th))) > 1f) continue;   // past its end: not beside it
                    return DrawnEdgeAt(map, trims, H, th, SideToward(H, th, p));
                }
            return DrawnEdgeAt(map, trims, o, t, SideToward(o, t, p));
        }

        /// <summary>PSX_CITY_MEET_NEXT_RUN=0: an approach run stops a step short of the next run (round 4).</summary>
        public static bool MeetNextRunOn = System.Environment.GetEnvironmentVariable("PSX_CITY_MEET_NEXT_RUN") != "0";
        /// <summary>PSX_CITY_COLLAPSE_HOST=0: the round-4 medians at a collapsed branch.</summary>
        public static bool CollapseHostOn = System.Environment.GetEnvironmentVariable("PSX_CITY_COLLAPSE_HOST") != "0";

        /// <summary>Is branch <paramref name="o"/>'s ribbon collapsed into its
        /// host at arc <paramref name="t"/> (either section around it)?</summary>
        static bool CollapsedAt(CityMap map, Trims trims, CityMap.Edge o, float t)
        {
            if (trims.branchA[o.index] < 0 && trims.branchB[o.index] < 0) return false;
            float sMin = trims.atA[o.index], sMax = o.length - trims.atB[o.index];
            if (sMax - sMin < 0.6f || t < sMin - 0.01f || t > sMax + 0.01f) return false;
            var secs = RawSectionsOf(map, trims, o, sMin, sMax);
            if (secs.Count < 2) return false;
            int lo = 0, hi = secs.Count - 1;
            if (t <= secs[0].s) hi = 1;
            else if (t >= secs[hi].s) lo = hi - 1;
            else while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (secs[mid].s <= t) lo = mid; else hi = mid; }
            return secs[lo].collapsed || secs[hi].collapsed;
        }

        /// <summary>
        /// A host's span side A-B beside a branch collapsed into it there,
        /// where that branch holds a union run on the side toward this one:
        /// the run (null: none). The road across must stand beyond this side.
        /// For an OWNER run, <paramref name="mapped"/> is the run as this
        /// host draws it (its arcs on the host, no closed ends).
        /// </summary>
        static UnionRun HostUnionHere(CityMap map, Trims trims, CityMap.Edge e, int side, in Section A, in Section B, Vector3 origin, out UnionRun mapped)
        {
            mapped = null;
            if (!CollapseHostOn || trims.unions == null) return null;
            var mine = 0.5f * (A.Edge(side) + B.Edge(side)) + origin;
            var pm = new Vector2(mine.x, mine.z);
            var outw = (A.Out(side) + B.Out(side)).normalized;
            for (int end = 0; end < 2; end++)
            {
                int node = end == 0 ? e.a : e.b;
                foreach (int bi in map.nodeEdges[node])
                {
                    if (bi == e.index) continue;
                    var br = map.edges[bi];
                    if (trims.BranchAt(br, node) != e.index) continue;
                    var list = trims.unions[bi];
                    if (list == null) continue;
                    CityElevation.ProjectOn(br, pm, out float tb);
                    if (Mathf.Abs(Vector2.Dot(pm - br.PointAt(tb), br.TangentAt(tb))) > 1f) continue;
                    if (!CollapsedAt(map, trims, br, tb)) continue;
                    int sideB = SideToward(br, tb, pm);
                    foreach (var r in list)
                    {
                        if (r.side != sideB || !r.Covers(tb, 0.05f)) continue;
                        // the road across stands beyond this side
                        var cands = r.owner ? r.across : r.mirror != null ? new[] { r.mirror.edge } : null;
                        if (cands == null || cands.Length == 0) continue;
                        FootAny(map, cands, pm, out var X, out float x);
                        if (X == null) continue;
                        var w = DrawnEdgeAt(map, trims, X, x, SideToward(X, x, pm));
                        if (Vector2.Dot(new Vector2(w.x - mine.x, w.z - mine.z), outw) < 0.05f) continue;
                        if (r.owner)
                        {
                            CityElevation.ProjectOn(e, br.PointAt(r.s0), out float h0);
                            CityElevation.ProjectOn(e, br.PointAt(r.s1), out float h1);
                            mapped = new UnionRun { edge = e.index, nb = r.nb, side = side, pair = r.pair, s0 = Mathf.Min(h0, h1), s1 = Mathf.Max(h0, h1),
                                                    median = r.median, owner = true, approach = r.approach, across = r.across, mirror = r.mirror };
                        }
                        return r;
                    }
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  One bent per station under the owner
        // ------------------------------------------------------------------

        static readonly List<(CityMap.Edge e, float s)> bentMembers = new List<(CityMap.Edge, float)>(4);

        /// <summary>
        /// A pier under a union: one column under each carriageway (the
        /// owner, its partners, and theirs) and a cap beam
        /// <see cref="BentBeamDepthM"/> deep from outer fascia to outer
        /// fascia, nudged along the owner as a whole off every road below;
        /// where no nudge clears the whole bent, the columns that clear on
        /// their own, without a beam. Recorded once, as the owner's pier.
        /// </summary>
        static void EmitBent(CityMap map, Trims trims, CityMap.Edge e, TileMeshes tm, float sAt)
        {
            var con = buckets[(int)Slot.Concrete];
            for (int pass = 0; pass < 2; pass++)
                foreach (var dS in PierNudges)
                {
                    float s = sAt + dS;
                    if (s < 2f || s > e.length - 2f) continue;
                    if (!UnionOwnerAt(trims, e.index, s) || !e.ElevatedAt(s)) continue;
                    // the carriageways the bent carries
                    bentMembers.Clear();
                    bentMembers.Add((e, s));
                    for (int m = 0; m < bentMembers.Count && bentMembers.Count < 4; m++)
                    {
                        var (me, ms) = bentMembers[m];
                        var list = trims.unions[me.index];
                        if (list == null) continue;
                        foreach (var r in list)
                        {
                            if (!r.owner || r.approach || !r.Covers(ms)) continue;
                            if (!FootOn(map, r.across, me.PointAt(ms), UnionFootSlackM, out var o, out float to, out _)) continue;
                            if (bentMembers.Exists(x => x.e == o)) continue;
                            if (!o.ElevatedAt(to)) continue;
                            bentMembers.Add((o, to));
                            if (bentMembers.Count >= 4) break;
                        }
                    }
                    var p = e.PointAt(s); var tan = e.TangentAt(s);
                    var right = new Vector2(-tan.y, tan.x);
                    // lateral extent and soffit heights across the owner's right
                    float latLo = float.MaxValue, latHi = float.MinValue, soffitMin = float.MaxValue;
                    for (int m = 0; m < bentMembers.Count; m++)
                    {
                        var (me, ms) = bentMembers[m];
                        LineModel.Extents(me, ms, out float eM, out float eP);
                        var pm = me.PointAt(ms); var tm2 = me.TangentAt(ms);
                        var rm = new Vector2(-tm2.y, tm2.x);
                        float l0 = Vector2.Dot(pm - rm * eM - p, right), l1 = Vector2.Dot(pm + rm * eP - p, right);
                        latLo = Mathf.Min(latLo, Mathf.Min(l0, l1)); latHi = Mathf.Max(latHi, Mathf.Max(l0, l1));
                        soffitMin = Mathf.Min(soffitMin, me.YAt(ms) - CityElevation.DeckThick);
                    }
                    float b0 = latLo + BentBeamInsetM, b1 = latHi - BentBeamInsetM;
                    if (b1 - b0 < 1f) continue;
                    bool beam = pass == 0;
                    if (beam)
                    {
                        var mid = p + right * (0.5f * (b0 + b1));
                        if (PierBlocked(map, e, mid, tan, 0.5f * (b1 - b0), soffitMin)) continue;
                    }
                    // the beam's top under each end: the soffit of the member there
                    float SoffitAtLat(float lat)
                    {
                        float bestD = float.MaxValue, y = soffitMin;
                        foreach (var (me, ms) in bentMembers)
                        {
                            LineModel.CentreAt(me, ms, out float c, out _);
                            var pm = me.PointAt(ms); var tm2 = me.TangentAt(ms);
                            float lm = Vector2.Dot(pm + new Vector2(-tm2.y, tm2.x) * c - p, right);
                            if (Mathf.Abs(lm - lat) < bestD) { bestD = Mathf.Abs(lm - lat); y = me.YAt(ms) - CityElevation.DeckThick; }
                        }
                        return y;
                    }
                    var org = tm.origin;
                    var r3 = new Vector3(right.x, 0f, right.y); var f3 = new Vector3(tan.x, 0f, tan.y);
                    var p3 = new Vector3(p.x - org.x, 0f, p.y - org.z);
                    float top0 = SoffitAtLat(b0), top1 = SoffitAtLat(b1);
                    int columns = 0;
                    foreach (var (me, ms) in bentMembers)
                    {
                        LineModel.CentreAt(me, ms, out float c, out float half);
                        var pm = me.PointAt(ms); var tm2 = me.TangentAt(ms);
                        var cm = pm + new Vector2(-tm2.y, tm2.x) * c;
                        float hw = Mathf.Max(0.7f, me.width * 0.18f);
                        float lat = Vector2.Dot(cm - p, right);
                        float topY = beam ? Mathf.Lerp(top0, top1, Mathf.InverseLerp(b0, b1, lat)) - BentBeamDepthM : me.YAt(ms) - CityElevation.DeckThick;
                        if (!beam && PierBlocked(map, me, cm, tm2, hw, topY)) continue;
                        float gy = CityElevation.GroundY(map, cm.x, cm.y);
                        if (topY - gy < 2.2f) continue;
                        var cc = new Vector3(cm.x - org.x, 0f, cm.y - org.z);
                        var rr = new Vector3(-tm2.y, 0f, tm2.x);
                        var ff = new Vector3(tm2.x, 0f, tm2.y);
                        EmitColumn(con, cc + Vector3.up * (gy - 0.6f), cc + Vector3.up * topY, rr * hw, ff * 0.7f);
                        tm.solids.Add(new SolidBox
                        {
                            center = cc + Vector3.up * ((gy - 0.6f + topY) * 0.5f),
                            size = new Vector3(hw * 2f, topY - gy + 0.6f, 1.4f),
                            yawDeg = Mathf.Atan2(ff.x, ff.z) * Mathf.Rad2Deg,
                        });
                        columns++;
                    }
                    if (columns == 0) continue;
                    if (beam)
                    {
                        // the cap beam: a box under the soffits, its top sloping
                        // from one end's soffit to the other's
                        Vector3 B0 = p3 + r3 * b0, B1 = p3 + r3 * b1;
                        var fa = f3 * BentBeamHalfAlongM;
                        Vector3 t00 = B0 - fa + Vector3.up * top0, t01 = B0 + fa + Vector3.up * top0, t10 = B1 - fa + Vector3.up * top1, t11 = B1 + fa + Vector3.up * top1;
                        var dn = Vector3.down * BentBeamDepthM;
                        var uvA = new Vector2(0.55f, 0f); var uvB = new Vector2(0.7f, 0.15f);
                        con.Down(t00 + dn, t10 + dn, t11 + dn, t01 + dn, uvA, uvB, uvB, uvA);
                        con.Face(t00 + dn, t10 + dn, t10, t00, -f3, uvA, uvB, uvB, uvA);
                        con.Face(t01 + dn, t11 + dn, t11, t01, f3, uvA, uvB, uvB, uvA);
                        con.Face(t00 + dn, t01 + dn, t01, t00, -r3, uvA, uvB, uvB, uvA);
                        con.Face(t10 + dn, t11 + dn, t11, t10, r3, uvA, uvB, uvB, uvA);
                    }
                    if (pierLog != null)
                    {
                        float gyP = CityElevation.GroundY(map, p.x, p.y);
                        RecordPier(e, sAt, s, p, gyP - 0.6f, e.YAt(s) - CityElevation.DeckThick, 0.5f * (b1 - b0), tan);
                    }
                    return;
                }
        }

        /// <summary>Is arc s of the edge inside a run over a deck, as owner or
        /// partner (a lone pier is not nudged into one)?</summary>
        static bool InUnionDeckRun(Trims t, int edge, float s)
        {
            var list = t.unions != null ? t.unions[edge] : null;
            if (list == null) return false;
            foreach (var r in list) if (!r.approach && r.Covers(s, 0.5f)) return true;
            return false;
        }
    }
}
