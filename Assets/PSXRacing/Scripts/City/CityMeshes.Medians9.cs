using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 9 (2026-10-29): the last median breaks, gaps and boxes on the race routes.
    /// The owner: "This is not how medians work. They don't just start and stop. Go up and down
    /// allowing cars to drive into oncoming traffic"; "rectangle cutouts with holes, spots that go
    /// down then back up, and empty box areas". Round 8 left on the routes (drive audit MEDIAN
    /// CLASSES) 8 breaks, 3 open gaps and 10 boxes. Each place, looked at (spot probe + shots):
    ///
    ///   SPLIT   where the twins part past the shared median's 9.5 m band (or come together into
    ///           it), the shared Jersey was turned down to a curb at the run's closed end right
    ///           beside each carriageway's own Jersey, itself turned down at its start: a 2-8 m
    ///           low spot a car crosses (Independence 161 / 290 / 2482 / 2600 / 4407, uptown 2211 /
    ///           7978). Now the median SPLITS the way a real one does: two Jersey legs from the
    ///           centre line, each flaring 1:6 out to its own carriageway's Jersey, met at full
    ///           height; neither the centre Jersey nor the own Jerseys are turned down there.
    ///           PSX_CITY_R9_SPLIT=0: before.
    ///   BRIDGE  two shared medians on one divided road, a short stretch apart, the twins never
    ///           more than <see cref="R9BridgeBandM"/> apart between them (I-277 e2139 / e8474 at
    ///           uptown 8688-8727: 8.9-10.2 m for 45 m): each ended at the 9.5 m band, so the
    ///           stretch between was two own Jerseys 9 m apart (a box) and two turned-down ends (a
    ///           gap). The median carries on through (up to <see cref="R9BridgeMaxM"/>).
    ///           PSX_CITY_R9_BRIDGE=0: before.
    ///   FANS    a junction on ONE carriageway of a divided road whose other arms all leave on its
    ///           outside (a right-in/right-out side street, a ramp): (a) the shared median stopped
    ///           at the twin's fan and the other carriageway stood its own Jersey across, turned
    ///           down at both ends - a grass rectangle cut out of the median (Independence at
    ///           Norland Road, 4507-4537); it now carries on past the fan. (b) A carriageway's own
    ///           median Jersey stopped square and open at its own fan (every right-in/right-out on
    ///           Independence's busway median: Paul Buck Blvd, Woodland Dr ...); the median side of
    ///           the fan now carries the Jersey across. A junction a road crosses the median at
    ///           keeps its opening. PSX_CITY_R9_FANS=0: before.
    ///   DECKEND2 (1'47") the shared median slab ran to the twin's abutment, while this
    ///           carriageway's deck carried on 5 m past it over the valley: a pit between the deck
    ///           and the twin's approach fill. The slab follows the longer deck to its end.
    ///           PSX_CITY_R9_DECKEND2=0: before.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool R9SplitOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R9_SPLIT") != "0";
        public static bool R9BridgeOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R9_BRIDGE") != "0";
        public static bool R9FansOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R9_FANS") != "0";
        public static bool R9DeckEnd2On = System.Environment.GetEnvironmentVariable("PSX_CITY_R9_DECKEND2") != "0";
        /// <summary>(round 9) The round's median switches as one set (the look tool's A/B: PSX_LOOK_AB=round9).</summary>
        public static bool[] Round9Flags() => new[] { R9SplitOn, R9BridgeOn, R9FansOn, R9DeckEnd2On };
        public static void SetRound9Flags(bool[] f) { R9SplitOn = f[0]; R9BridgeOn = f[1]; R9FansOn = f[2]; R9DeckEnd2On = f[3]; }
        public static void SetRound9(bool on) => SetRound9Flags(new[] { on, on, on, on });

        /// <summary>The widest the twins may stand apart where a gap between two shared medians is
        /// bridged (a 10-12 m median keeps its centre Jersey and a paved strip each side).</summary>
        const float R9BridgeBandM = 12f;
        /// <summary>The longest gap between two shared medians that is bridged.</summary>
        const float R9BridgeMaxM = 60f;
        /// <summary>A split's legs flare 1:6 each: the leg runs this many times the median's width.</summary>
        const float R9SplitLegPerWidth = 3f;
        /// <summary>How far each leg's foot carries on along its carriageway's edge (inset inside
        /// the own Jersey it meets), so a partner whose own Jersey starts a little off the foot
        /// leaves no notch.</summary>
        const float R9SplitFootM = 1.5f;
        static int r9Splits, r9SplitDropped, r9Bridges, r9FanFills, r9DeckEnds2;
        /// <summary>(round 9) fan chords given the median's Jersey (tiles built so far).</summary>
        public static int r9FanChords;
        static float r9BridgeM, r9FanFillM, r9DeckEnd2M;
        static readonly List<float> splitFeetScratch = new List<float>(4);

        /// <summary>(round 9) Every split V on the map, world space (the drive audit's V class):
        /// the mouth (the cross-line at the run's closed end) and the apex (on the centre line,
        /// one leg's length into the run).</summary>
        public static readonly List<(Vector2 mouth, Vector2 apex, float halfW)> SplitVs = new List<(Vector2, Vector2, float)>(64);

        /// <summary>(round 9) The shared median's band (the drive audit's WIDE class).</summary>
        public const float SharedMedianBandM = GroundTwinGapMaxM;

        /// <summary>(round 9, the drive audit) Does the plan segment a-b (a ray across a divided
        /// road's median) cross a split V or the metres just past its mouth?</summary>
        public static bool InSplitV(Vector2 a, Vector2 b)
        {
            var m = 0.5f * (a + b);
            foreach (var v in SplitVs)
            {
                var axis = v.apex - v.mouth;
                float L = axis.magnitude;
                if (L < 0.5f) continue;
                var u = axis / L;
                var d = m - v.mouth;
                float t = Vector2.Dot(d, u), lat = Mathf.Abs(u.x * d.y - u.y * d.x);
                if (t >= -5f && t <= L + 1f && lat <= v.halfW + 2f) return true;
            }
            return false;
        }

        /// <summary>(round 9, the drive audit's fan rays) Does a road leave junction node n on the
        /// left of edge e's travel (into the median: a crossing, a left turn, a left exit)?</summary>
        public static bool NodeLeavesLeft(CityMap map, CityMap.Edge e, int node)
        {
            var tY = e.b == node ? e.TangentAt(e.length) : e.TangentAt(0f);
            var left = new Vector2(-tY.y, tY.x);
            foreach (int oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.a == o.b) continue;
                if (o.name == e.name && o.oneway && Vector2.Dot(OutDir(o, node), OutDir(e, node)) < -0.8f) continue;   // the road on through
                if (Vector2.Dot(OutDir(o, node), left) > 0.15f) return true;
            }
            return false;
        }

        /// <summary>The round's report line (LastUnionReport).</summary>
        static string Round9Report() =>
            $"round 9: {r9Bridges} gaps bridged ({r9BridgeM:0} m), {r9FanFills} of them past a twin's outside-only fan ({r9FanFillM:0} m), {r9Splits} split ends (V to both own Jerseys), " +
            $"{r9SplitDropped} short runs split at both ends dropped, {r9DeckEnds2} medians carried on along the longer deck ({r9DeckEnd2M:0} m)";

        // ------------------------------------------------------------------
        //  The road on through a node, a fan's sides
        // ------------------------------------------------------------------

        /// <summary>The edge carrying X's road on through <paramref name="node"/>: a mitred node's
        /// next, else the same road's one-way edge continuing X's travel there (a ramp merging or
        /// leaving beside it).</summary>
        static CityMap.Edge R9Next(CityMap map, Trims t, CityMap.Edge X, int node)
        {
            var m = MitredNext(map, t, X, node);
            if (m != null) return m;
            if (string.IsNullOrEmpty(X.name)) return null;
            var dX = OutDir(X, node);
            CityMap.Edge best = null; float bd = -0.8f;
            foreach (int oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == X || o.a == o.b || !o.oneway || o.link || o.name != X.name) continue;
                if ((X.b == node) != (o.a == node)) continue;   // on in X's travel
                float d = Vector2.Dot(dX, OutDir(o, node));
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        /// <summary>Does every arm of <paramref name="node"/> but Y's own road leave on Y's
        /// OUTSIDE (the right of its travel) - no road crosses or leaves the median there?</summary>
        static bool FanOutsideOnly(CityMap map, Trims t, CityMap.Edge Y, int node)
        {
            if (!Y.oneway || R9Next(map, t, Y, node) == null) return false;
            var tY = Y.b == node ? Y.TangentAt(Y.length) : Y.TangentAt(0f);   // travel at the node
            var left = new Vector2(-tY.y, tY.x);
            var Z = R9Next(map, t, Y, node);
            foreach (int oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == Y || o == Z || o.a == o.b) continue;
                if (Vector2.Dot(OutDir(o, node), left) > 0.15f) return false;
            }
            return true;
        }

        /// <summary>FillTwinOk's tests with a wider band, and a twin in its fan allowed where that
        /// fan's other arms all leave on the twin's outside. Ground only.</summary>
        static bool FillTwinOk9(CityMap map, Trims t, CityMap.Edge X, float x, int side, float gapMax, bool fanOk, out CityMap.Edge Y, out float y, bool strict = true)
        {
            if (!NearestOpposingTwin(map, X, x, out Y, out y, out float best)) return false;
            var p = X.PointAt(x);
            if (SideToward(X, x, Y.PointAt(y)) != side) return false;
            float xlo = t.patch[X.a] ? t.atA[X.index] + UnionFanSetbackM : 0f, xhi = t.patch[X.b] ? X.length - t.atB[X.index] - UnionFanSetbackM : X.length;
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f, yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            if (x < xlo - 1e-3f || x > xhi + 1e-3f) return false;
            if (y < ylo - 1e-3f && !(fanOk && FanOutsideOnly(map, t, Y, Y.a))) return false;
            if (y > yhi + 1e-3f && !(fanOk && FanOutsideOnly(map, t, Y, Y.b))) return false;
            if (X.ElevatedAt(x) || Y.ElevatedAt(y)) return false;
            if (Mathf.Abs(X.YAt(x) - Y.YAt(y)) > UnionSplitDyM) return false;
            int sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (side > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            if (gap > gapMax || gap < DeckPairs.SqueezeM) return false;
            var ly = t.unions[Y.index];
            if (ly != null)
                foreach (var o in ly)
                {
                    if (o.side != sy) continue;
                    DrawnRange(map, o, out float o0, out float o1);
                    if (y <= o0 + 0.5f || y >= o1 - 0.5f) continue;
                    // (bridge) the twin's side already drawn there: the gap is X's only - not doubled
                    if (strict) return false;
                    if (o.owner) { if (o.across == null || System.Array.IndexOf(o.across, X.index) < 0) return false; }
                    else if (o.mirror != null && o.mirror.edge != X.index) return false;
                }
            return true;
        }

        /// <summary>An owner run over X's g0..g1 with exact partner runs on every twin edge its
        /// samples met (the round-8 fill's run, laid for a bridge).</summary>
        static UnionRun LayFill9(CityMap map, Trims t, CityMap.Edge X, int side, float g0, float g1, List<CityMap.Edge> met)
        {
            if (met.Count == 0 || g1 - g0 < 0.25f) return null;
            var ids = new int[met.Count];
            for (int k = 0; k < ids.Length; k++) ids[k] = met[k].index;
            var acrossX = ChainAround(map, t, X);
            var Ymid = met[met.Count / 2];
            var ra = new UnionRun { edge = X.index, nb = Ymid.index, side = side, s0 = g0, s1 = g1, median = DeckPairs.Median.Barrier,
                                    owner = true, pair = -1, approach = true, across = WithContinuations(map, t, ids), fill = true };
            AddRun(t, ra);
            foreach (int ci in ra.across)
            {
                var rb = AddExactPartner(map, t, ra, X, g0, g1, map.edges[ci], acrossX);
                if (rb != null && (ra.mirror == null || ci == ra.nb)) ra.mirror = rb;
            }
            if (ra.mirror == null) ra.mirror = new UnionRun { edge = ra.nb, nb = X.index, side = -side, median = DeckPairs.Median.Barrier, pair = -1, approach = true, across = acrossX };
            return ra;
        }

        // ------------------------------------------------------------------
        //  BRIDGE (and FANS a): the gaps between two shared medians
        // ------------------------------------------------------------------
        static readonly List<Vector2> r9Cov = new List<Vector2>(8);
        static readonly List<(CityMap.Edge e, float a, float b)> r9Pieces = new List<(CityMap.Edge, float, float)>(4);
        static readonly List<CityMap.Edge> r9Met = new List<CityMap.Edge>(4);

        /// <summary>Where the drawn medians on edge E's side start, after arc <paramref name="from"/>
        /// (float.MaxValue: none); and whether arc <paramref name="from"/> itself is inside one.</summary>
        static float NextCovered(CityMap map, Trims t, CityMap.Edge E, int side, float from)
        {
            float next = float.MaxValue;
            var list = t.unions[E.index];
            if (list == null) return next;
            foreach (var r in list)
            {
                if (r.side != side) continue;
                DrawnRange(map, r, out float c0, out float c1);
                if (c1 - c0 <= 0.05f) continue;
                if (c0 <= from + 0.3f && c1 > from + 0.3f) return from;   // still covered
                if (c0 > from + 0.3f && c0 < next) next = c0;
            }
            return next;
        }

        static int BridgeMedianGaps(CityMap map, Trims t, out float metres, out int fanFills, out float fanM)
        {
            metres = 0f; fanM = 0f; fanFills = 0; int n = 0;
            var E = map.edges;
            var starts = new List<(CityMap.Edge X, int side, float s)>();
            for (int xi = 0; xi < E.Length; xi++)
            {
                var X = E[xi];
                if (X.a == X.b || !X.oneway || X.link || !Barriered(X) || string.IsNullOrEmpty(X.name)) continue;
                var list = t.unions[xi];
                if (list == null) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    r9Cov.Clear();
                    foreach (var r in list)
                    {
                        if (r.side != side) continue;
                        DrawnRange(map, r, out float c0, out float c1);
                        if (c1 - c0 > 0.05f) r9Cov.Add(new Vector2(c0, c1));
                    }
                    if (r9Cov.Count == 0) continue;
                    r9Cov.Sort((p, q) => p.x.CompareTo(q.x));
                    float reach = r9Cov[0].y;
                    for (int k = 1; k <= r9Cov.Count; k++)
                    {
                        if (k < r9Cov.Count && r9Cov[k].x <= reach + 0.3f) { reach = Mathf.Max(reach, r9Cov[k].y); continue; }
                        // a median stops at `reach` going +s: a gap starts here (X's travel), unless at X's end
                        // with the road across the node covered from its start (no gap)
                        starts.Add((X, side, reach));
                        if (k < r9Cov.Count) reach = r9Cov[k].y;
                    }
                }
            }
            foreach (var (X0, side, s0) in starts)
            {
                // walk on (X's travel, on through its nodes) to the next drawn median
                r9Pieces.Clear();
                var cur = X0; float from = s0, walked = 0f; bool ok = false;
                for (int guard = 0; guard < 8; guard++)
                {
                    float nx = NextCovered(map, t, cur, side, from);
                    if (nx <= from + 1e-4f) { ok = r9Pieces.Count > 0; break; }   // covered again right here
                    float end = Mathf.Min(nx, cur.length);
                    if (walked + (end - from) > R9BridgeMaxM) break;
                    if (end - from > 0.05f) r9Pieces.Add((cur, from, end));
                    walked += end - from;
                    if (nx <= cur.length) { ok = true; break; }
                    var Z = R9Next(map, t, cur, cur.b);
                    if (Z == null || Z == X0) break;
                    cur = Z; from = 0f;
                    if (NextCovered(map, t, cur, side, 0f) <= 0.3f) { ok = r9Pieces.Count > 0; break; }
                }
                if (!ok || r9Pieces.Count == 0 || walked < 0.3f) continue;
                // the twin beside every 2 m of it, in the wider band, at one level, on the ground
                bool fine = true, anyFan = false;
                foreach (var (pe, pa, pb) in r9Pieces)
                {
                    int ns = Mathf.Max(1, Mathf.CeilToInt((pb - pa) / 2f));
                    for (int k = 0; k <= ns && fine; k++)
                    {
                        float x = Mathf.Lerp(pa + 0.2f, pb - 0.2f, (float)k / ns);
                        if (!FillTwinOk9(map, t, pe, x, side, R9BridgeBandM, R9FansOn, out var Y, out float y)) { fine = false; break; }
                        float ylo = t.patch[Y.a] ? t.atA[Y.index] : 0f, yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] : Y.length;
                        if (y < ylo || y > yhi) anyFan = true;
                    }
                    if (!fine) break;
                }
                if (!fine) continue;
                foreach (var (pe, pa, pb) in r9Pieces)
                {
                    r9Met.Clear();
                    int ns = Mathf.Max(1, Mathf.CeilToInt((pb - pa) / 2f));
                    for (int k = 0; k <= ns; k++)
                        if (FillTwinOk9(map, t, pe, Mathf.Lerp(pa + 0.2f, pb - 0.2f, (float)k / ns), side, R9BridgeBandM, R9FansOn, out var Y, out _) && !r9Met.Contains(Y)) r9Met.Add(Y);
                    if (LayFill9(map, t, pe, side, pa, pb, r9Met) == null) continue;
                    n++; metres += pb - pa;
                    if (anyFan) { fanFills++; fanM += pb - pa; }
                }
            }
            return n;
        }

        // ------------------------------------------------------------------
        //  SPLIT: a closed ground end where both carriageways go on with their own Jersey
        // ------------------------------------------------------------------

        /// <summary>What the median side of E stands past arc s (dir +1: on in +s), for a few metres
        /// on (through its node if need be): 1 its own Jersey (barriered, grounded, out of fans and
        /// structure-end rail bands), 2 an approach rail on the ground (a rail band), 0 anything else
        /// or a shared median drawn there.</summary>
        static int OwnBarrierPast(CityMap map, Trims t, CityMap.Edge E, float s, int dir, int side)
        {
            var cur = E; float x = s; int d = dir, kind = 0;
            for (float walked = 1f; walked <= 4.01f; walked += 1.5f)
            {
                float nx = x + d * 1.5f;
                if (nx < 0f || nx > cur.length)
                {
                    int node = nx < 0f ? cur.a : cur.b;
                    var Z = R9Next(map, t, cur, node);
                    if (Z == null) return 0;
                    float over = nx < 0f ? -nx : nx - cur.length;
                    bool fromA = Z.a == node;
                    x = fromA ? over : Z.length - over; d = fromA ? 1 : -1;
                    // the same side of the road: Z runs on the same way (one-way through)
                    cur = Z;
                }
                else x = nx;
                if (!cur.oneway || !Barriered(cur) || cur.ElevatedAt(x)) return 0;
                float lo = t.atA[cur.index], hi = cur.length - t.atB[cur.index];
                if (x < lo || x > hi) return 0;
                int k = InRailBand(map, t, cur, x) ? 2 : 1;
                if (kind != 0 && k != kind) return 0;
                kind = k;
                // a median DRAWN there (a partner's padded claim past its owner's end draws nothing)
                var ul = t.unions[cur.index];
                if (ul != null)
                    foreach (var o in ul)
                    {
                        if (o.side != side) continue;
                        DrawnRange(map, o, out float o0, out float o1);
                        if (o1 - o0 > 0.05f && x > o0 - 0.3f && x < o1 + 0.3f) return 0;
                    }
            }
            return kind;
        }

        static int MarkSplitEnds(CityMap map, Trims t)
        {
            int n = 0;
            SplitVs.Clear();
            var E = map.edges;
            for (int ei = 0; ei < E.Length; ei++)
            {
                var list = t.unions[ei];
                if (list == null) continue;
                var X = E[ei];
                foreach (var r in list)
                {
                    r.split0 = r.split1 = false;
                    if (!r.owner || r.median != DeckPairs.Median.Barrier || r.across == null || r.s1 - r.s0 < 1f || r.side <= 0) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        // a closed end - or one "continued" only by this run's OWN partner run's padded
                        // claim across (Continued's twin test: a partner claims 2 m past its owner's last
                        // foot, so the owner's end read as carried on by itself; it was drawn with no end
                        // at all while each carriageway's own Jersey was turned down beside it: every
                        // BREAK / OPEN GAP left by the first round-9 drive - 290, 2211, 2482, 2600, 4407, 7978)
                        bool closed = end == 0 ? r.open0 : r.open1;
                        if (!closed && !ContinuedButByOwnPartner(map, t, r, end)) closed = true;
                        if (!closed) continue;
                        float s = Mathf.Clamp(end == 0 ? r.s0 : r.s1, 0f, X.length);
                        int dir = end == 0 ? -1 : 1;
                        if (X.ElevatedAt(s) || X.ElevatedAt(Mathf.Clamp(s - dir * 0.5f, 0f, X.length))) continue;
                        int kO = OwnBarrierPast(map, t, X, s, dir, r.side);
                        if (kO == 0) continue;
                        var pX = X.PointAt(s);
                        if (!FootOn(map, r.across, pX, 1.0f, out var Y, out float y, out _) || Y == null) continue;
                        int dirY = Vector2.Dot(Y.TangentAt(y), X.TangentAt(s) * dir) >= 0f ? 1 : -1;
                        int sideY = SideToward(Y, y, pX);
                        int kP = sideY > 0 ? OwnBarrierPast(map, t, Y, y, dirY, sideY) : 0;
                        if (kP == 0) continue;
                        // (an approach rail: only where DECKEND2 carried the median on over a pit - a
                        // run ending in a rail band elsewhere keeps its own end, its rails theirs)
                        if ((kO == 2 || kP == 2) && !r.deckEnd9) continue;
                        int rails = (kO == 2 ? 1 : 0) | (kP == 2 ? 2 : 0);
                        if (end == 0) r.splitRail0 = rails; else r.splitRail1 = rails;
                        // the median's width at the end: the leg length (1:6 each), at most 45% of the run
                        var a = DrawnEdgeAt(map, t, X, s, r.side); var b = FacingEdgeAt(map, t, Y, y, pX);
                        float w = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                        if (w < 1.5f) continue;
                        float leg = Mathf.Clamp(R9SplitLegPerWidth * w, TaperLenM, Mathf.Max(TaperLenM, 0.45f * (r.s1 - r.s0)));
                        // (a split end is closed: its V is its end)
                        if (end == 0) { r.split0 = true; r.splitLeg0 = leg; r.open0 = true; } else { r.split1 = true; r.splitLeg1 = leg; r.open1 = true; }
                        n++;
                        // the V for the audit: mouth on the end's cross-line, apex a leg's length in
                        float sIn = Mathf.Clamp(s - dir * leg, 0f, X.length);
                        var aIn = DrawnEdgeAt(map, t, X, sIn, r.side);
                        var pIn = X.PointAt(sIn);
                        FootAny(map, r.across, pIn, out var Yi, out float yi);
                        var bIn = Yi != null ? FacingEdgeAt(map, t, Yi, yi, pIn) : b;
                        SplitVs.Add((new Vector2(0.5f * (a.x + b.x), 0.5f * (a.z + b.z)), new Vector2(0.5f * (aIn.x + bIn.x), 0.5f * (aIn.z + bIn.z)), 0.5f * w));
                    }
                }
            }
            return n;
        }

        /// <summary>(round 9) <see cref="Continued"/> without the run's OWN partner runs (whose padded
        /// claim reaches past its owner's end and only stands down for this very median).</summary>
        static bool ContinuedButByOwnPartner(CityMap map, Trims t, UnionRun r, int end)
        {
            var e = map.edges[r.edge];
            float s = end == 0 ? r.s0 : r.s1;
            foreach (var o in t.unions[r.edge])
            {
                if (o == r || o.side != r.side) continue;
                if (end == 0 ? Mathf.Abs(o.s1 - s) < 0.1f || (o.s0 < s - 0.1f && o.s1 > s - 0.1f) : Mathf.Abs(o.s0 - s) < 0.1f || (o.s0 < s + 0.1f && o.s1 > s + 0.1f)) return true;
            }
            if (TwinContinueOn && r.nb >= 0 && r.nb < map.edges.Length && t.unions[r.nb] != null)
            {
                var yt = map.edges[r.nb];
                var pe = e.PointAt(Mathf.Clamp(s, 0f, e.length));
                CityElevation.ProjectOn(yt, pe, out float ys);
                if (Vector2.Distance(yt.PointAt(ys), pe) <= e.HalfMax + yt.HalfMax + GroundTwinGapMaxM + 2f)
                {
                    var pIn = e.PointAt(Mathf.Clamp(end == 0 ? s + 2f : s - 2f, 0f, e.length));
                    CityElevation.ProjectOn(yt, pIn, out float yIn);
                    float beyond = ys + (ys - yIn) * 0.5f;
                    foreach (var o in t.unions[r.nb])
                    {
                        if (o.mirror == r) continue;   // its own partner: stands down for this median only
                        if (o.Covers(ys, 0.5f) && o.Covers(beyond, 0.5f)) return true;
                    }
                }
            }
            bool atNode = end == 0 ? s <= 0.05f : s >= e.length - 0.05f;
            if (!atNode) return false;
            int node = end == 0 ? e.a : e.b;
            var y = MitredNext(map, t, e, node);
            if (y == null || t.unions[y.index] == null) return false;
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

        /// <summary>A run split at BOTH ends and shorter than its two legs is a diamond in the
        /// median (two V's back to back): not laid - the two own Jerseys run on past it.</summary>
        static int DropShortSplitRuns(CityMap map, Trims t)
        {
            var drop = new HashSet<UnionRun>();
            foreach (var list in t.unions)
            {
                if (list == null) continue;
                foreach (var r in list)
                    if (r.owner && r.split0 && r.split1 && r.s1 - r.s0 < r.splitLeg0 + r.splitLeg1 + 4f) drop.Add(r);
            }
            if (drop.Count == 0) return 0;
            foreach (var list in t.unions) list?.RemoveAll(r => drop.Contains(r) || (!r.owner && r.mirror != null && drop.Contains(r.mirror)));
            return drop.Count;
        }

        /// <summary>(round 9) Is this end of a run (owner; or the owner whose median a partner run
        /// stands down for) a SPLIT, within <paramref name="reach"/> of arc sb on edge E?</summary>
        static bool SplitEndNear(CityMap map, UnionRun run, CityMap.Edge E, float sb, float reach)
        {
            if (run == null) return false;
            if (run.owner) return (run.split0 && Mathf.Abs(sb - run.s0) <= reach) || (run.split1 && Mathf.Abs(sb - run.s1) <= reach);
            var M = run.mirror;
            if (M == null || !M.owner || (!M.split0 && !M.split1) || M.edge < 0 || M.edge >= map.edges.Length) return false;
            var X = map.edges[M.edge];
            for (int end = 0; end < 2; end++)
            {
                if (!(end == 0 ? M.split0 : M.split1)) continue;
                CityElevation.ProjectOn(E, X.PointAt(Mathf.Clamp(end == 0 ? M.s0 : M.s1, 0f, X.length)), out float f);
                if (Mathf.Abs(sb - f) <= reach + 0.6f) return true;
            }
            return false;
        }

        /// <summary>(round 9, DecideSideFlags) An own median Jersey ending beside a union span (arcs
        /// s0..s1) at a split end keeps its full height (it meets the split's leg).</summary>
        static bool R9KeepJerseyEnd(CityMap map, Trims t, CityMap.Edge e, int side, float s0, float s1, float sb)
        {
            if (!R9SplitOn || t.unions == null) return false;
            return SplitEndNear(map, UnionOn(t, e.index, side, s0, s1), e, sb, 0.7f);
        }

        /// <summary>(round 9, the section stations) the feet of the split ends a partner run stands
        /// down for, so its own Jersey starts on that foot.</summary>
        static void SplitFeetOn(CityMap map, Trims t, CityMap.Edge e, List<float> into)
        {
            if (!R9SplitOn || t.unions == null) return;
            var list = t.unions[e.index];
            if (list == null) return;
            foreach (var r in list)
            {
                if (r.owner) continue;
                var M = r.mirror;
                if (M == null || !M.owner || (!M.split0 && !M.split1) || M.edge < 0) continue;
                var X = map.edges[M.edge];
                for (int end = 0; end < 2; end++)
                {
                    if (!(end == 0 ? M.split0 : M.split1)) continue;
                    var p = X.PointAt(Mathf.Clamp(end == 0 ? M.s0 : M.s1, 0f, X.length));
                    CityElevation.ProjectOn(e, p, out float f);
                    if (Vector2.Distance(e.PointAt(f), p) > e.HalfMax + X.HalfMax + R9BridgeBandM + 4f) continue;
                    into.Add(f);
                }
            }
        }

        /// <summary>A Jersey along a carriageway's edge from a to b (tile space), its traffic face
        /// <paramref name="inset"/> behind the edge (into the median along <paramref name="outward"/>),
        /// a hair narrower and lower than the own Jersey it lies inside: a split leg's foot.</summary>
        static void EmitInsetJersey(Vector3 a, Vector3 b, Vector2 outward, float v0, float v1, float inset = 0.02f)
        {
            var o = new Vector3(outward.x, 0f, outward.y);
            if (o.sqrMagnitude < 1e-6f) return;
            o.Normalize();
            var a0 = a + o * inset; var b0 = b + o * inset;
            var w = o * (BarrierW - 2f * inset);
            float h = BarrierH - inset, foot = -KerbFaceM;
            var bk = barrierBucket;
            bk.WallSloped(a0, b0, a0.y + foot, a0.y + h, b0.y + foot, b0.y + h, -outward, v0, v1, 0.3f, 0.45f);
            bk.WallSloped(a0 + w, b0 + w, a0.y + foot, a0.y + h, b0.y + foot, b0.y + h, outward, v0, v1, 0.3f, 0.45f);
            bk.Up(a0 + Vector3.up * h, b0 + Vector3.up * h, b0 + w + Vector3.up * h, a0 + w + Vector3.up * h,
                  new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
            var uv = new Vector2(0.3f, v1);
            bk.Face(b0 + Vector3.up * foot, b0 + Vector3.up * h, b0 + w + Vector3.up * h, b0 + w + Vector3.up * foot, b - a, uv, uv, uv, uv);
        }

        /// <summary>(round 9, EmitUnionMedian) The two leg feet at a split end's cross-line (owner
        /// edge point a, partner edge point b, tile space): each carried on along its own
        /// carriageway's edge past the end.</summary>
        static void EmitSplitFeet(CityMap map, Trims trims, CityMap.Edge e, UnionRun run, int side, int end, Vector3 a, Vector3 b, Vector3 org, float v)
        {
            float s = Mathf.Clamp(end == 0 ? run.s0 : run.s1, 0f, e.length);
            int dir = end == 0 ? -1 : 1;
            // the owner's edge on past the end
            float sOut = Mathf.Clamp(s + dir * R9SplitFootM, 0f, e.length);
            var aOut = DrawnEdgeAt(map, trims, e, sOut, side) - org;
            var toB = new Vector2(b.x - a.x, b.z - a.z);
            if (toB.sqrMagnitude < 1e-4f) return;
            toB.Normalize();
            int rails = end == 0 ? run.splitRail0 : run.splitRail1;   // a rail's side: its leg meets the rail as a deck V's does
            if (Mathf.Abs(sOut - s) > 0.2f && (rails & 1) == 0) EmitInsetJersey(a, aOut, toB, v, v + 0.05f);
            if ((rails & 2) != 0) return;
            // the partner's edge on past its foot
            var pl = e.PointAt(s);
            FootAny(map, run.across, pl, out var o, out float tk);
            if (o == null) return;
            int dirO = Vector2.Dot(o.TangentAt(tk), e.TangentAt(s) * dir) >= 0f ? 1 : -1;
            float tOut = Mathf.Clamp(tk + dirO * R9SplitFootM, 0f, o.length);
            if (Mathf.Abs(tOut - tk) <= 0.2f) return;
            var bOut = FacingEdgeAt(map, trims, o, tOut, pl) - org;
            EmitInsetJersey(b, bOut, -toB, v, v + 0.05f);
        }

        // ------------------------------------------------------------------
        //  FANS (b): the median side of a fan on one carriageway
        // ------------------------------------------------------------------

        /// <summary>(round 9, BuildJunctions) Is the fan chord k0-k1 the MEDIAN side of a divided
        /// road's carriageway running through the node, with nothing leaving on that side, and its
        /// own Jersey (no shared median) on both arms there? Then the Jersey carries on across it.</summary>
        static bool MedianChordJersey(CityMap map, Trims t, int n, int e0, int side0, int node0, int e1, int side1, int node1)
        {
            if (!R9FansOn || e0 == e1 || e0 < 0 || e1 < 0 || side0 <= 0 || side1 <= 0 || node0 != n || node1 != n) return false;
            var A = map.edges[e0]; var B = map.edges[e1];
            if (!A.oneway || !B.oneway || A.link || B.link || !Barriered(A) || !Barriered(B) || string.IsNullOrEmpty(A.name) || A.name != B.name) return false;
            if ((A.b == n) == (B.b == n)) return false;   // one arrives, one leaves
            if (Vector2.Dot(OutDir(A, n), OutDir(B, n)) > -0.8f) return false;
            float sa = A.a == n ? 0f : A.length, sb = B.a == n ? 0f : B.length;
            if (A.ElevatedAt(sa) || B.ElevatedAt(sb)) return false;
            // a shared median there draws the median (its fill past a fan, or a union to the node)
            float ta = A.a == n ? t.atA[A.index] : A.length - t.atB[A.index], tb = B.a == n ? t.atA[B.index] : B.length - t.atB[B.index];
            if (UnionHolds(t, A, 1, ta) || UnionHolds(t, B, 1, tb) || UnionHolds(t, A, 1, sa) || UnionHolds(t, B, 1, sb)) return false;
            // a divided road: the twin beside, on that side
            if (!NearestOpposingTwin(map, A, Mathf.Clamp(ta, 0f, A.length), out var Y, out float y, out float best) || best > 60f) return false;
            if (SideToward(A, Mathf.Clamp(ta, 0f, A.length), Y.PointAt(y)) <= 0) return false;
            // ...that does not draw a shared median across to this fan
            int sy = SideToward(Y, y, A.PointAt(Mathf.Clamp(ta, 0f, A.length)));
            if (UnionHolds(t, Y, sy, y)) return false;
            return true;
        }

        /// <summary>(round 9, BuildJunctions) The fan chord k0-k1 is the median side of a divided
        /// road's carriageway running through the node, and a shared median holds both arms' median
        /// side there (FANS: carried on past this fan): its strip lies over the chord, so no kerb or
        /// verge is drawn on it (the verge's grass stood a hair over the strip - a green seam).</summary>
        static bool MedianChordUnion(CityMap map, Trims t, int n, int e0, int side0, int node0, int e1, int side1, int node1)
        {
            if (!R9FansOn || e0 == e1 || e0 < 0 || e1 < 0 || side0 <= 0 || side1 <= 0 || node0 != n || node1 != n) return false;
            var A = map.edges[e0]; var B = map.edges[e1];
            if (!A.oneway || !B.oneway || A.link || B.link || string.IsNullOrEmpty(A.name) || A.name != B.name) return false;
            if ((A.b == n) == (B.b == n) || Vector2.Dot(OutDir(A, n), OutDir(B, n)) > -0.8f) return false;
            float ta = A.a == n ? t.atA[A.index] : A.length - t.atB[A.index], tb = B.a == n ? t.atA[B.index] : B.length - t.atB[B.index];
            return UnionHolds(t, A, 1, ta) && UnionHolds(t, B, 1, tb);
        }

        // ------------------------------------------------------------------
        //  DECKEND2: the slab along the longer deck at a skewed abutment line
        // ------------------------------------------------------------------

        /// <summary>
        /// (round 9, the owner's 1'47") A Barrier run's PARTNER run that ends where its own deck
        /// carries on over a pit past the owner's end (I-277 e2435 0..7.5 beside e2437's run ending
        /// at its abutment n3295, e2435's deck on to 12.4 over the valley): an owner run on the
        /// partner's edge from that end on as far as DeckPitAt holds (its deck, the twin beside in
        /// the band, the land 0.5 m+ under both). It meets the partner run (one median), its V at
        /// the deck's end.
        /// </summary>
        static int ExtendAlongPartnerDecks(CityMap map, Trims t, out float metres)
        {
            metres = 0f; int n = 0;
            var E = map.edges;
            var add = new List<(CityMap.Edge X, UnionRun like, float a, float b)>();
            for (int xi = 0; xi < E.Length; xi++)
            {
                var list = t.unions[xi];
                if (list == null) continue;
                var X = E[xi];
                foreach (var r in list)
                {
                    if (r.owner || r.median != DeckPairs.Median.Barrier || r.mirror == null || !r.mirror.owner) continue;
                    DrawnRange(map, r, out float d0, out float d1);
                    if (d1 - d0 < 2f) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        float s = end == 0 ? d0 : d1;
                        int dir = end == 0 ? -1 : 1;
                        if (!X.ElevatedAt(Mathf.Clamp(s + dir * 0.5f, 0f, X.length))) continue;
                        // nothing on this side carries the median on already
                        if (NextCoveredBeyond(map, t, X, r.side, s, dir)) continue;
                        float reach = s;
                        for (float d = 1f; d <= R8DeckEndMaxM + 1e-3f; d += 1f)
                        {
                            float x = s + dir * d;
                            if (x < -1e-3f || x > X.length + 1e-3f) break;
                            x = Mathf.Clamp(x, 0f, X.length);
                            // past the deck's end the drawn land still dips into the valley between
                            // the two approaches (its 8 m cells reach the vertices dug under the deck):
                            // the median is carried on over that too (on the ground, its V at the end)
                            if (!DeckPitAt(map, t, r, X, x) && !LatticePitAt(map, t, r, X, x)) break;
                            reach = x;
                            if (x <= 0f || x >= X.length) break;
                        }
                        if (Mathf.Abs(reach - s) < 2f) continue;
                        if (reach <= UnionNodeSnapM) reach = 0f;
                        if (X.length - reach <= UnionNodeSnapM) reach = X.length;
                        add.Add((X, r, Mathf.Min(s, reach), Mathf.Max(s, reach)));
                    }
                }
            }
            foreach (var (X, like, a, b) in add)
            {
                // once per pit (two partner runs on one edge find the same one)
                bool dup = false;
                foreach (var o in t.unions[X.index])
                    if (o.owner && o.side == like.side && o.s0 < 0.5f * (a + b) && o.s1 > 0.5f * (a + b)) { dup = true; break; }
                if (dup) continue;
                var acrossX = ChainAround(map, t, X);
                var across = like.mirror.edge >= 0 ? WithContinuations(map, t, ChainAround(map, t, E[like.mirror.edge])) : acrossX;
                var ra = new UnionRun { edge = X.index, nb = like.mirror.edge, side = like.side, s0 = a, s1 = b, median = DeckPairs.Median.Barrier,
                                        owner = true, pair = like.pair, approach = true, across = across, deckEnd9 = true };
                AddRun(t, ra);
                foreach (int ci in ra.across)
                {
                    var rb = AddExactPartner(map, t, ra, X, a, b, E[ci], acrossX);
                    if (rb != null && ra.mirror == null) ra.mirror = rb;
                }
                if (ra.mirror == null) ra.mirror = new UnionRun { edge = ra.nb, nb = X.index, side = -ra.side, median = DeckPairs.Median.Barrier, pair = -1, approach = true, across = acrossX };
                n++; metres += b - a;
            }
            return n;
        }

        /// <summary>(round 9, DECKEND2 on the ground) DeckPitAt's tests without its deck, the land read
        /// as the ground lattice DRAWS it (each quarter point's 8 m triangle, from the ground at its
        /// three corners): the twin beside in the band at one level, the drawn land 0.5 m+ under both.</summary>
        static bool LatticePitAt(CityMap map, Trims t, UnionRun r, CityMap.Edge X, float x)
        {
            if (!NearestOpposingTwin(map, X, x, out var Y, out float y, out float best)) return false;
            var p = X.PointAt(x); var q = Y.PointAt(y);
            if (SideToward(X, x, q) != r.side) return false;
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f, yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            if (y < ylo - 1e-3f || y > yhi + 1e-3f) return false;
            float yx = X.YAt(x), yy = Y.YAt(y);
            if (Mathf.Abs(yx - yy) > UnionSplitDyM) return false;
            int sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (r.side > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            if (gap < DeckPairs.SqueezeM || gap > R8DeckEndBandM) return false;
            var tx = X.TangentAt(x); var lx = new Vector2(-tx.y, tx.x);
            var ty = Y.TangentAt(y); var ly = new Vector2(-ty.y, ty.x);
            var pe = p + lx * (r.side > 0 ? xp : -xm);
            var qe = q + ly * (sy > 0 ? yp : -ym);
            float road = Mathf.Min(yx, yy), deepest = float.MaxValue;
            for (int k = 1; k <= 3; k++)
            {
                var c = Vector2.Lerp(pe, qe, k * 0.25f);
                float fx = c.x / LatticeCell, fz = c.y / LatticeCell;
                int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
                float ux = fx - ix, uz = fz - iz;
                float G(int i, int j) => CityElevation.GroundY(map, i * LatticeCell, j * LatticeCell);
                float h00 = G(ix, iz), h11 = G(ix + 1, iz + 1);
                float h = ux >= uz ? h00 + ux * (G(ix + 1, iz) - h00) + uz * (h11 - G(ix + 1, iz))
                                   : h00 + uz * (G(ix, iz + 1) - h00) + ux * (h11 - G(ix, iz + 1));
                deepest = Mathf.Min(deepest, h - road);
            }
            if (-deepest < R8DeckEndDropM) return false;
            var lst = t.unions[X.index];
            if (lst != null)
                foreach (var o in lst)
                {
                    if (o == r || o.side != r.side) continue;
                    DrawnRange(map, o, out float o0, out float o1);
                    if (x > o0 + 0.05f && x < o1 - 0.05f) return false;
                }
            return true;
        }

        /// <summary>Is X's side drawn on past arc s (dir: the way on) by another run within half a metre?</summary>
        static bool NextCoveredBeyond(CityMap map, Trims t, CityMap.Edge X, int side, float s, int dir)
        {
            var list = t.unions[X.index];
            if (list == null) return false;
            float probe = s + dir * 0.5f;
            foreach (var o in list)
            {
                if (o.side != side) continue;
                DrawnRange(map, o, out float o0, out float o1);
                if (probe > o0 + 0.05f && probe < o1 - 0.05f) return true;
            }
            return false;
        }
    }
}
