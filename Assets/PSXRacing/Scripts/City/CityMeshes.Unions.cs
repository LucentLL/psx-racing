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
    ///             line), a Jersey or flush median over the
    ///             <see cref="ApproachRailM"/> band its inner approach rails
    ///             stood on (the rest of the freeway median is A12's).
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
    ///   ENDS      a run end the median does not carry on past: on a deck an
    ///             end face down to the soffit and a rail across the slab's end
    ///             (the decks carry on apart, one ends, or the approaches part);
    ///             on the ground a battered curb end, a Jersey cap, or nothing
    ///             for a flush strip (the verge resumes).
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
        const float UnionApproachRaisedM = 80f, UnionGroundGapMaxM = 6.1f, UnionGroundGapMinM = 0.6f;
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

            float deckM = 0f; int runs = 0;
            for (int ei = 0; ei < ne; ei++)
                if (t.unions[ei] != null) foreach (var r in t.unions[ei]) if (r.owner && !r.approach) { runs++; deckM += r.s1 - r.s0; }
            LastUnionMs = (float)clock.Elapsed.TotalMilliseconds;
            LastUnionReport = $"{pairsDrawn} union pairs drawn as one structure ({runs} runs, {deckM:0} m over the decks' ranges; {approachRuns} approach runs, {approachM:0} m); " +
                              $"{pairsSplit} pairs in more than one run or ended by a split level over {UnionSplitDyM} m ({splitRuns} runs{(splitWorst > 0f ? "; worst " + splitWorstAt : "")}); " +
                              $"{pairsNone} union pairs with no run{(noRun.Length > 0 ? " (" + noRun.ToString().Trim().TrimEnd(';') + ")" : "")}; {open} open run ends; {LastUnionMs:0.0} ms";
        }

        /// <summary>One segment of a pair's walk (pairWalk[i0..i1]) as an owner
        /// run on A and a partner run on each edge across it was beside.</summary>
        static void AddPairRun(CityMap map, Trims t, int pi, DeckPairs.Pair pr, CityMap.Edge A, int i0, int i1, int[] acrossA, int[] acrossB)
        {
            var E = map.edges;
            var ra = new UnionRun { edge = pr.a, nb = pr.b, side = pr.sideA, s0 = pairWalk[i0].s, s1 = pairWalk[i1].s, median = pr.median, owner = true, pair = pi, across = acrossA };
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
                var rb = new UnionRun { edge = yi, nb = pr.a, side = SideToward(Y, ym, A.PointAt(sm)), s0 = lo, s1 = hi, median = pr.median, owner = false, pair = pi, across = acrossB, mirror = ra };
                AddRun(t, rb);
                if (ra.mirror == null || yi == pr.b) ra.mirror = rb;
            }
            if (ra.mirror == null) ra.mirror = new UnionRun { edge = pr.b, nb = pr.a, side = pr.sideB, s0 = 0f, s1 = 0f, median = pr.median, pair = pi, across = acrossB };
        }

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
            var w = DrawnEdgeAt(map, trims, X, x, SideToward(X, x, pm));
            var mine = 0.5f * (A.Edge(side) + B.Edge(side)) + origin;
            var outw = (A.Out(side) + B.Out(side)).normalized;
            float d = Vector2.Dot(new Vector2(w.x - mine.x, w.z - mine.z), outw);
            return d >= (run.owner ? 0.05f : 0.10f) ? run : null;
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
            float maxM = r.median == DeckPairs.Median.Raised ? UnionApproachRaisedM : ApproachRailM;
            float gMax = (r.median == DeckPairs.Median.Flush ? 1.2f : UnionGroundGapMaxM) + 0.5f;
            float gMin = r.median == DeckPairs.Median.Raised ? UnionGroundGapMinM : DeckPairs.SqueezeM;
            // a raised top or a Jersey takes a split level (SharedGuardDyM); a
            // flush strip only what a wheel rolls across
            float dyMax = r.median == DeckPairs.Median.Flush ? 0.10f : SharedGuardDyM;
            // the road across, ahead: (edge, from arc, direction)
            bChain.Clear();
            {
                var cur = Yend; float from = tEnd; int dir = dirB; float run = 0f;
                for (int g = 0; g < 6 && cur != null && run < maxM + 40f; g++)
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
            // walk the owner's road on
            walk.Clear();
            {
                var X = A; float x = sEnd; int dx = dirA; float walked = 0f;
                for (int guard = 0; guard < 400 && walked <= maxM; guard++)
                {
                    if (!ApproachSample(map, t, r, X, x, walked, gMin, gMax, dyMax, out var Y, out float y)) break;
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
                    int sideX = SideToward(X, 0.5f * (x0 + x1), walk[(i0 + i - 1) / 2].Y.PointAt(walk[(i0 + i - 1) / 2].y));
                    var acrossX = ChainAround(map, t, X);
                    var ra = new UnionRun { edge = X.index, nb = walk[(i0 + i - 1) / 2].Y.index, side = sideX, s0 = x0, s1 = x1, median = r.median, owner = true, pair = r.pair, approach = true, across = bAcross };
                    AddRun(t, ra);
                    laid += x1 - x0; added++;
                    foreach (var (be, _, _) in bChain)
                    {
                        float lo = float.MaxValue, hi = float.MinValue; int kMid = -1;
                        for (int k = i0; k < i; k++)
                        {
                            if (walk[k].Y != be) continue;
                            lo = Mathf.Min(lo, walk[k].y); hi = Mathf.Max(hi, walk[k].y); kMid = k;
                        }
                        if (kMid < 0) continue;
                        lo = Mathf.Max(0f, lo - 0.5f * UnionStepM); hi = Mathf.Min(be.length, hi + 0.5f * UnionStepM);
                        if (lo <= 1f) lo = 0f;
                        if (be.length - hi <= 1f) hi = be.length;
                        if (hi - lo < 0.25f) continue;
                        var rb = new UnionRun { edge = be.index, nb = X.index, side = SideToward(be, 0.5f * (lo + hi), X.PointAt(walk[kMid].x)), s0 = lo, s1 = hi,
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

        static readonly List<(CityMap.Edge e, float from, int dir)> bChain = new List<(CityMap.Edge, float, int)>(6);
        static int[] bAcross;
        static readonly List<(CityMap.Edge X, float x, CityMap.Edge Y, float y)> walk = new List<(CityMap.Edge, float, CityMap.Edge, float)>(128);

        static int SideToward(CityMap.Edge e, float s, Vector2 q)
        {
            var p = e.PointAt(s); var tan = e.TangentAt(s);
            return ((q.x - p.x) * -tan.y + (q.y - p.y) * tan.x) >= 0f ? 1 : -1;
        }

        /// <summary>One sample of the approach walk: the road across beside
        /// it, and whether a median belongs between them there.</summary>
        static bool ApproachSample(CityMap map, Trims t, UnionRun r, CityMap.Edge X, float x, float walked,
                                   float gMin, float gMax, float dyMax, out CityMap.Edge Y, out float y)
        {
            Y = null; y = 0f;
            // short of a fan: the ribbon ends at its trim there
            float lo = t.patch[X.a] ? t.atA[X.index] + UnionFanSetbackM : 0f;
            float hi = t.patch[X.b] ? X.length - t.atB[X.index] - UnionFanSetbackM : X.length;
            if (x < lo - 1e-3f || x > hi + 1e-3f) return false;
            if (walked > 3f && X.ElevatedAt(x)) return false;
            var p = X.PointAt(x);
            if (!FootOn(map, bAcross, p, UnionFootSlackM, out Y, out y, out float best)) return false;
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f;
            float yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            if (y < ylo - 1e-3f || y > yhi + 1e-3f) return false;
            if (walked > 3f && Y.ElevatedAt(y)) return false;
            if (Mathf.Abs(X.YAt(x) - Y.YAt(y)) > dyMax) return false;
            // the gap between the two drawn edges facing each other
            var q2 = Y.PointAt(y);
            int sx = SideToward(X, x, q2), sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (sx > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            if (gap > gMax || gap < gMin) return false;
            // never across a run already there (the next structure's)
            var list = t.unions[X.index];
            if (list != null) foreach (var o in list) if (o.side == sx && o.Covers(x, -0.05f) && o != r) return false;
            return true;
        }

        // ------------------------------------------------------------------
        //  The median, drawn by the owner, span by span
        // ------------------------------------------------------------------

        static readonly Vector3[] uA = new Vector3[64], uB = new Vector3[64];
        static readonly float[] uV = new float[64], uS = new float[64];
        static readonly bool[] uOk = new bool[64];

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
                var w = DrawnEdgeAt(map, trims, o, tk, SideToward(o, tk, pl));
                uB[k] = w - org;
                // nothing to span where the two drawn edges touch
                uOk[k] = Vector2.Distance(pw, new Vector2(w.x, w.z)) > 0.05f;
            }
            var bk = buckets[(int)RoadSlot(e, deck)];
            var lay = LineModel.LayoutOf(e.profile);
            float tw = lay.texW;
            float gA = (lay.x0.Length > 0 ? lay.x0[0] - 0.25f : tw - 0.25f) / tw, gB = 0.25f / tw;   // the paint-free shoulder texels
            var con = buckets[(int)Slot.Concrete];
            float dk = CityElevation.DeckThick;
            for (int k = 0; k < pieces; k++)
            {
                if (!uOk[k] || !uOk[k + 1]) continue;
                Profile(run.median, uA[k], uB[k], prof0, out int n0);
                Profile(run.median, uA[k + 1], uB[k + 1], prof1, out int n1);
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
                if (run.median == DeckPairs.Median.Barrier) EmitUnionJersey(uA[k], uB[k], uA[k + 1], uB[k + 1], uV[k], uV[k + 1]);
            }
            // the closed ends this span holds
            for (int end = 0; end < 2; end++)
            {
                bool open = end == 0 ? run.open0 && Mathf.Abs(A.s - run.s0) < 0.3f : run.open1 && Mathf.Abs(B.s - run.s1) < 0.3f;
                if (!open) continue;
                int k = end == 0 ? 0 : pieces, kIn = end == 0 ? 1 : pieces - 1;
                if (!uOk[k]) continue;
                // outward along the run, in plan
                var outw = new Vector2(uA[k].x - uA[kIn].x, uA[k].z - uA[kIn].z);
                if (outw.sqrMagnitude < 1e-6f) continue;
                outw.Normalize();
                EmitUnionEnd(map, tm, e, run, side, uA[k], uB[k], outw, deck, uV[k], end == 0 ? run.s0 : run.s1);
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
        static void EmitUnionJersey(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float v0, float v1)
        {
            var bk = barrierBucket;
            Vector3 c0 = 0.5f * (a0 + b0), c1 = 0.5f * (a1 + b1);
            var d0 = new Vector3(b0.x - a0.x, 0f, b0.z - a0.z); float g0 = d0.magnitude; d0 = g0 > 1e-4f ? d0 / g0 : Vector3.right;
            var d1 = new Vector3(b1.x - a1.x, 0f, b1.z - a1.z); float g1 = d1.magnitude; d1 = g1 > 1e-4f ? d1 / g1 : Vector3.right;
            float h0 = Mathf.Min(BarrierW * 0.5f, Mathf.Max(0.1f, 0.5f * g0 - 0.1f)), h1 = Mathf.Min(BarrierW * 0.5f, Mathf.Max(0.1f, 0.5f * g1 - 0.1f));
            Vector3 pa0 = c0 - d0 * h0, pb0 = c0 + d0 * h0, pa1 = c1 - d1 * h1, pb1 = c1 + d1 * h1;
            float top0 = Mathf.Max(a0.y, b0.y) + BarrierH, top1 = Mathf.Max(a1.y, b1.y) + BarrierH;
            // the feet: the strip's height there, a kerb face lower
            float fa0 = Mathf.Lerp(a0.y, b0.y, 0.5f - h0 / Mathf.Max(g0, 1e-3f)) - KerbFaceM, fb0 = Mathf.Lerp(a0.y, b0.y, 0.5f + h0 / Mathf.Max(g0, 1e-3f)) - KerbFaceM;
            float fa1 = Mathf.Lerp(a1.y, b1.y, 0.5f - h1 / Mathf.Max(g1, 1e-3f)) - KerbFaceM, fb1 = Mathf.Lerp(a1.y, b1.y, 0.5f + h1 / Mathf.Max(g1, 1e-3f)) - KerbFaceM;
            var toA = new Vector2(-d0.x, -d0.z);
            bk.WallSloped(pa0, pa1, fa0, top0, fa1, top1, toA, v0, v1, 0.3f, 0.45f);
            bk.WallSloped(pb0, pb1, fb0, top0, fb1, top1, -toA, v0, v1, 0.3f, 0.45f);
            bk.Up(new Vector3(pa0.x, top0, pa0.z), new Vector3(pa1.x, top1, pa1.z), new Vector3(pb1.x, top1, pb1.z), new Vector3(pb0.x, top0, pb0.z),
                  new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
        }

        /// <summary>
        /// A union run's closed end at the cross-line a (owner) - b (partner):
        /// on a deck, an end face from the median's surface down to the soffit
        /// and a rail across the slab's end (so nothing leaves the slab where
        /// the decks carry on apart, one ends, or the approaches part); on the
        /// ground, a raised median's battered end curb. A Jersey is capped.
        /// </summary>
        static void EmitUnionEnd(CityMap map, TileMeshes tm, CityMap.Edge e, UnionRun run, int side, Vector3 a, Vector3 b, Vector2 outw, bool deck, float v, float sEnd)
        {
            Profile(run.median, a, b, prof0, out int n);
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
                // the rail across the end: from the owner's edge to the partner's,
                // its traffic face toward the slab, its foot at the soffit
                float gap = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (gap > 0.3f)
                {
                    var inw = -outw;
                    EmitRail(a, b, inw, inw, dk, true, true, 0f, gap / RoadVTile);
                    railLog?.Add(new RailRecord
                    {
                        edge = e.index, side = side, node = -1, s0 = sEnd, s1 = sEnd,
                        a = a + tm.origin, b = b + tm.origin, inA = inw, inB = inw, overhang = RailOverhangM,
                    });
                }
            }
            else if (run.median == DeckPairs.Median.Raised && n >= 6)
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
            if (run.median == DeckPairs.Median.Barrier)
            {
                // the Jersey's cap on the end line
                Vector3 c = 0.5f * (a + b);
                var d = new Vector3(b.x - a.x, 0f, b.z - a.z); float g = d.magnitude; d = g > 1e-4f ? d / g : Vector3.right;
                float h = Mathf.Min(BarrierW * 0.5f, Mathf.Max(0.1f, 0.5f * g - 0.1f));
                float top = Mathf.Max(a.y, b.y) + BarrierH, foot = Mathf.Min(a.y, b.y) - KerbFaceM;
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
