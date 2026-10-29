using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 8 (2026-10-29): the owner's medians, at his spots first. On the live
    /// round 6 he saw "rectangle cutouts with holes, spots that go down then back up, and empty
    /// box areas"; round 7 counted them (the drive audit's MEDIAN CLASSES). Every one of the
    /// three he showed is two medians that do not MEET:
    ///
    ///   0'54" I-277 e2311 (CHAIN)   a deck run (owner e2306) hands over to a ground run (owner
    ///             e2317) at node n3523, which stands 1.1 m into the partner's deck e2311. The
    ///             ground run's road across was only the twin pieces its walk met (e9877, e2322),
    ///             so at the node its foot clamped to e9877's end 1.1 m back while the deck run
    ///             found e2311: two cross-lines 1.1 m apart at the partner, the Jersey stepped
    ///             0.55 m aside with a square end, and the triangle between drawn by neither - a
    ///             hole to the grass. A ground or approach run's road across now also holds the
    ///             edges mitred nodes carry those pieces on into (as a deck run's ChainAround), and
    ///             an edge its median reaches gets its partner run (it stands down there).
    ///   4'06" I-77 e15241 (EXACT, FILL, SEAM)   a partner run claimed half a walk step (2 m)
    ///             past where its owner draws and was snapped 6 m on to its edge's end: e15241's
    ///             claimed 15.8..20 past e1887's start (n695), so the twin's 2.6 m of e1894 beside
    ///             it (57.7..60.3) was refused as covered - in no run, a hole between the decks
    ///             with each side's own rail sloped down into it; and e1887's own run snapped its
    ///             end to the padded 15.6 instead of 17.6 where e1885's median reaches, so
    ///             e15241's first 2.5 m kept its own parapet beside the union Jersey (a box).
    ///             A run meeting a partner run now meets it where its owner's median is DRAWN (the
    ///             feet of the owner's ends, <see cref="DrawnRange"/>); a gap of up to
    ///             <see cref="R8FillMaxM"/> between two drawn medians, with the twin beside
    ///             in the band at one level, is laid (the walk's 4 m samples never saw 2.6 m);
    ///             and where a median is carried on by one OWNED ACROSS (the other carriageway's
    ///             run), each end draws the same cross-line - from its own edge at its end to the
    ///             other's edge at ITS end - since perpendicular feet from two diverging
    ///             carriageways land up to ~2 m apart (e1887 s 17.6 vs e1885's end at n2945).
    /// 1'47" I-277 past the Elizabeth Ave bridge (DECKEND): see <see cref="ExtendToDeckEnds"/>.
    /// PSX_CITY_R8_CHAIN / _EXACT / _FILL / _SEAM / _DECKEND = 0: each before.
    /// </summary>
    public static partial class CityMeshes
    {
        public static bool R8ChainOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R8_CHAIN") != "0";
        public static bool R8ExactOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R8_EXACT") != "0";
        public static bool R8FillOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R8_FILL") != "0";
        public static bool R8SeamOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R8_SEAM") != "0";
        public static bool R8DeckEndOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R8_DECKEND") != "0";
        /// <summary>(round 8) The round's median switches as one set (the look tool's A/B: PSX_LOOK_AB=round8).</summary>
        public static bool[] Round8Flags() => new[] { R8ChainOn, R8ExactOn, R8FillOn, R8SeamOn, R8DeckEndOn };
        public static void SetRound8Flags(bool[] f) { R8ChainOn = f[0]; R8ExactOn = f[1]; R8FillOn = f[2]; R8SeamOn = f[3]; R8DeckEndOn = f[4]; }
        public static void SetRound8(bool on) => SetRound8Flags(new[] { on, on, on, on, on });

        // ------------------------------------------------------------------
        //  DECK END: the 1'47" box past the Elizabeth Ave bridge
        // ------------------------------------------------------------------
        /// <summary>
        /// (round 8, the owner's 1'47": "empty box areas", I-277 past the Elizabeth Ave bridge
        /// e2437/e2438) The one bridge's union run (owner e2437) began 8 m into e2437's deck, where
        /// the pair table's range began; but the two carriageways' decks end on a skewed abutment
        /// line (e2438's at n3657, e2437's 7.5 m further on at n3295), and between them past the
        /// V's mouth the median was the ground under the bridge, 1 m and more below both roads -
        /// a pit 12 m wide and 9 m long, walled by each carriageway's own rail and the V's legs,
        /// with the terrain's concrete closing face beyond: an empty box. A real deck carries its
        /// median slab to the abutment. A closed end of a Barrier run whose OWNER's deck carries
        /// on past it, with its twin beside within <see cref="R8DeckEndBandM"/> at one level and
        /// the ground between more than <see cref="R8DeckEndDropM"/> under both roads, now carries
        /// on (slab, soffit, Jersey) to where that deck - or the pit - ends, at most
        /// <see cref="R8DeckEndMaxM"/>; its V stands there. PSX_CITY_R8_DECKEND=0: before.
        /// </summary>
        const float R8DeckEndBandM = 16f, R8DeckEndDropM = 0.5f, R8DeckEndMaxM = 24f;
        static int r8DeckEnds;
        static float r8DeckEndM;

        /// <summary>(round 8) Why the last closed ends near a deck were or were not carried on (the look tool's report).</summary>
        public static string LastDeckEndWhy { get; private set; } = "";
        static string deckPitWhy;
        static bool DeckPitAt(CityMap map, Trims t, UnionRun r, CityMap.Edge X, float x)
        {
            deckPitWhy = "not on structure";
            if (!X.ElevatedAt(x) && !X.bridge) return false;
            deckPitWhy = "no twin";
            if (!NearestOpposingTwin(map, X, x, out var Y, out float y, out float best)) return false;
            var p = X.PointAt(x); var q = Y.PointAt(y);
            deckPitWhy = $"twin e{Y.index} on the other side";
            if (SideToward(X, x, q) != r.side) return false;
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f, yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            deckPitWhy = $"twin e{Y.index} in its fan";
            if (y < ylo - 1e-3f || y > yhi + 1e-3f) return false;
            float yx = X.YAt(x), yy = Y.YAt(y);
            deckPitWhy = $"dy {Mathf.Abs(yx - yy):0.00} to e{Y.index}";
            if (Mathf.Abs(yx - yy) > UnionSplitDyM) return false;
            int sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (r.side > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            deckPitWhy = $"gap {gap:0.0} to e{Y.index}";
            if (gap < DeckPairs.SqueezeM || gap > R8DeckEndBandM) return false;
            // the ground across the gap, at its quarter points between the two drawn edges
            var tx = X.TangentAt(x); var lx = new Vector2(-tx.y, tx.x);
            var ty = Y.TangentAt(y); var ly = new Vector2(-ty.y, ty.x);
            var pe = p + lx * (r.side > 0 ? xp : -xm);
            var qe = q + ly * (sy > 0 ? yp : -ym);
            // (a pit: the land at one of the gap's quarter points R8DeckEndDropM or more under both roads)
            float road = Mathf.Min(yx, yy), deepest = float.MaxValue;
            for (int k = 1; k <= 3; k++)
            {
                var c = Vector2.Lerp(pe, qe, k * 0.25f);
                deepest = Mathf.Min(deepest, CityElevation.GroundY(map, c.x, c.y) - road);
            }
            deckPitWhy = $"land {deepest:+0.00;-0.00} (e{Y.index} gap {gap:0.0})";
            if (-deepest < R8DeckEndDropM) return false;
            deckPitWhy = "another run there";
            // no other run draws this side there already
            var lst = t.unions[X.index];
            if (lst != null)
                foreach (var o in lst)
                {
                    if (o == r || o.side != r.side) continue;
                    DrawnRange(map, o, out float o0, out float o1);
                    if (x > o0 + 0.05f && x < o1 - 0.05f) return false;
                }
            deckPitWhy = "pit";
            return true;
        }

        /// <summary>
        /// (round 8, 1'47" again, 22 m on: I-277 e2435 -> e4593 at n3658) A concrete rail carried on
        /// across a node uncapped only on structure (RailContinuesPast), so the approach rail along
        /// the MEDIAN side of a divided freeway, one band from the bridge end, was capped at the
        /// plain node between two OSM ways: sloped down to nothing at e2435's end and up again from
        /// e4593's start - a 3 m notch in the median's wall, the owner's "spots that go down then
        /// back up". It now carries on where: the node joins just these two (same road, one-way,
        /// barriered), the side faces the opposing twin within <see cref="R8DeckEndBandM"/>, the node
        /// is inside an approach rail band on both (a structure end within ApproachRailM), and no
        /// union median holds that side at the node on either (a rail never runs on into one).
        /// PSX_CITY_R8_MEDRAIL=0: before.
        /// </summary>
        public static bool R8MedRailOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R8_MEDRAIL") != "0";
        static readonly List<float> medRailEnds = new List<float>(8);
        static bool MedianRailCarriesOn(CityMap map, Trims t, CityMap.Edge e, CityMap.Edge o, int node, int side)
        {
            if (!R8MedRailOn || map.nodeEdges[node].Count != 2 || e.link || o.link || !e.oneway || !o.oneway
                || string.IsNullOrEmpty(e.name) || e.name != o.name || !Barriered(e) || !Barriered(o)) return false;
            float se = e.a == node ? 0f : e.length, so = o.a == node ? 0f : o.length;
            if (!NearestOpposingTwin(map, e, se, out var Y, out float y, out float best) || SideToward(e, se, Y.PointAt(y)) != side) return false;
            if (best - e.HalfMax - Y.HalfMax > R8DeckEndBandM) return false;
            bool sameWay = (e.b == node) == (o.a == node);
            int sideO = sameWay ? side : -side;
            if (!InRailBand(map, t, e, se) || !InRailBand(map, t, o, so)) return false;
            if (UnionHolds(t, e, side, se) || UnionHolds(t, o, sideO, so)) return false;
            return true;
        }
        static bool InRailBand(CityMap map, Trims t, CityMap.Edge e, float s)
        {
            StructureEnds(map, t, e, medRailEnds);
            foreach (float se in medRailEnds) if (Mathf.Abs(s - se) < ApproachRailM) return true;
            return false;
        }
        static bool UnionHolds(Trims t, CityMap.Edge e, int side, float s)
        {
            var l = t.unions != null ? t.unions[e.index] : null;
            if (l == null) return false;
            foreach (var r in l) if (r.side == side && r.Covers(s, 0.5f)) return true;
            return false;
        }

        static int r8Dropped;
        static int DropContainedRuns(CityMap map, Trims t)
        {
            int n = 0;
            foreach (var list in t.unions)
            {
                if (list == null || list.Count < 2) continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var r = list[i];
                    if (!r.owner) continue;
                    for (int j = 0; j < list.Count; j++)
                    {
                        var o = list[j];
                        if (j == i || !o.owner || o.side != r.side || o.median != r.median) continue;
                        if (o.s0 > r.s0 + 0.01f || o.s1 < r.s1 - 0.01f) continue;
                        // identical ranges: the first one stays
                        if (o.s1 - o.s0 < r.s1 - r.s0 + 0.01f && j > i) continue;
                        list.RemoveAt(i); n++;
                        break;
                    }
                }
            }
            return n;
        }

        static int ExtendToDeckEnds(CityMap map, Trims t, out float metres)
        {
            metres = 0f; int n = 0;
            var E = map.edges;
            var grown = new List<(UnionRun r, float a, float b)>();
            var why = new System.Text.StringBuilder();
            for (int xi = 0; xi < E.Length; xi++)
            {
                var list = t.unions[xi];
                if (list == null) continue;
                var X = E[xi];
                foreach (var r in list)
                {
                    if (!r.owner || r.median != DeckPairs.Median.Barrier || r.s1 - r.s0 < 4f || r.across == null) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        float s = end == 0 ? r.s0 : r.s1;
                        int dir = end == 0 ? -1 : 1;
                        bool nearDeck = X.ElevatedAt(Mathf.Clamp(s - dir, 0f, X.length)) || X.bridge;
                        if (Continued(map, t, r, end)) { if (nearDeck && why.Length < 4000) why.Append($" e{X.index} s{s:0.0}: continued;"); continue; }
                        float reach = s; string first = null;
                        for (float d = 1f; d <= R8DeckEndMaxM + 1e-3f; d += 1f)
                        {
                            float x = s + dir * d;
                            if (x < -1e-3f || x > X.length + 1e-3f) break;
                            x = Mathf.Clamp(x, 0f, X.length);
                            bool ok = DeckPitAt(map, t, r, X, x);
                            first ??= deckPitWhy;
                            if (!ok) { first += $" / stop at {x:0.0}: {deckPitWhy}"; break; }
                            reach = x;
                            if (x <= 0f || x >= X.length) break;
                        }
                        if (nearDeck && why.Length < 4000) why.Append($" e{X.index} s{s:0.0}->{reach:0.0}: {first};");
                        if (Mathf.Abs(reach - s) < 2f) continue;
                        if (reach <= UnionNodeSnapM) reach = 0f;
                        if (X.length - reach <= UnionNodeSnapM) reach = X.length;
                        grown.Add((r, Mathf.Min(s, reach), Mathf.Max(s, reach)));
                        if (end == 0) r.s0 = reach; else r.s1 = reach;
                        n++; metres += Mathf.Abs(reach - s);
                    }
                }
            }
            LastDeckEndWhy = why.ToString();
            // the edges across stand down beside the new slab
            foreach (var (r, a, b) in grown)
            {
                var X = E[r.edge];
                var acrossX = ChainAround(map, t, X);
                foreach (int ci in r.across) AddExactPartner(map, t, r, X, a, b, E[ci], acrossX);
            }
            return n;
        }

        /// <summary>The longest gap between two drawn medians the fill lays: the drive audit's
        /// BREAK (MEDIAN CLASSES a/b) is a stretch of no wall up to 16 m with the median standing
        /// both sides.</summary>
        public const float R8FillMaxM = 16f;
        /// <summary>A run owned across ends within this of this run's end foot: one seam.</summary>
        const float SeamReachM = 3.5f;
        static int r8Fills, r8Seams, r8ChainPartners;
        static float r8FillM;

        /// <summary>The edges across plus the edges mitred nodes carry them on into (CHAIN).</summary>
        static int[] WithContinuations(CityMap map, Trims t, int[] across)
        {
            if (!R8ChainOn || across == null || across.Length == 0) return across;
            List<int> l = null;
            foreach (int yi in across)
            {
                var y = map.edges[yi];
                for (int k = 0; k < 2; k++)
                {
                    var c = MitredNext(map, t, y, k == 0 ? y.a : y.b);
                    if (c == null || System.Array.IndexOf(across, c.index) >= 0 || (l != null && l.Contains(c.index))) continue;
                    (l ??= new List<int>(across)).Add(c.index);
                }
            }
            return l == null ? across : l.ToArray();
        }

        /// <summary>The arc of <paramref name="Y"/> beside plan point p: a true foot (within half a
        /// metre along Y's tangent, within reach), or Y's own end where p lies past it.</summary>
        static bool FootOrEnd(CityMap.Edge Y, Vector2 p, float reach, out float f)
        {
            CityElevation.ProjectOn(Y, p, out f);
            if (f <= 0.01f || f >= Y.length - 0.01f) return true;
            var q = Y.PointAt(f);
            return Vector2.Distance(p, q) <= reach && Mathf.Abs(Vector2.Dot(p - q, Y.TangentAt(f))) <= 0.5f;
        }

        /// <summary>The arcs of <paramref name="Y"/> beside an owner run's two ends X(x0), X(x1)
        /// (EXACT): how far along Y the owner's median is drawn. A foot within UnionNodeSnapM of
        /// Y's end is at the end.</summary>
        static bool ExactFeet(CityMap.Edge Y, CityMap.Edge X, float x0, float x1, out float lo, out float hi)
        {
            lo = hi = 0f;
            float reach = X.HalfMax + Y.HalfMax + GroundTwinGapMaxM + 8f;
            if (!FootOrEnd(Y, X.PointAt(Mathf.Clamp(x0, 0f, X.length)), reach, out float f0)
                || !FootOrEnd(Y, X.PointAt(Mathf.Clamp(x1, 0f, X.length)), reach, out float f1)) return false;
            lo = Mathf.Min(f0, f1); hi = Mathf.Max(f0, f1);
            if (lo <= UnionNodeSnapM) lo = 0f;
            if (Y.length - hi <= UnionNodeSnapM) hi = Y.length;
            return true;
        }

        /// <summary>
        /// (EXACT) How far along its edge a run's median is DRAWN: an owner's own range; a partner's,
        /// the feet of its owner's two ends, cut to its own range. A partner run keeps its padded
        /// claim (half a walk step past its last foot, snapped on to its edge's end): Continued
        /// reads it, and cutting every claim to the exact feet left 408 run ends open instead of
        /// 110 (the round-8 first look) - but a run meeting it, a gap between two, and the fill's
        /// tests read where the median actually stands.
        /// </summary>
        static void DrawnRange(CityMap map, UnionRun r, out float lo, out float hi)
        {
            lo = r.s0; hi = r.s1;
            if (!R8ExactOn || r.owner) return;
            var M = r.mirror;
            if (M == null || !M.owner || M.edge < 0 || M.edge >= map.edges.Length || M.s1 - M.s0 < 0.05f) return;
            if (!ExactFeet(map.edges[r.edge], map.edges[M.edge], M.s0, M.s1, out float a, out float b)) return;
            float nlo = Mathf.Max(lo, a), nhi = Mathf.Min(hi, b);
            if (nhi < nlo) { lo = hi = Mathf.Clamp(0.5f * (a + b), r.s0, r.s1); return; }
            lo = nlo; hi = nhi;
        }

        /// <summary>A partner run on <paramref name="be"/> exactly as far as owner run
        /// <paramref name="ra"/> (on X, x0..x1) reaches beside it; its side from its middle's own
        /// pair of points. Null where the median does not reach be.</summary>
        static UnionRun AddExactPartner(CityMap map, Trims t, UnionRun ra, CityMap.Edge X, float x0, float x1, CityMap.Edge be, int[] acrossX)
        {
            if (be == null || be == X || be.a == be.b || !ExactFeet(be, X, x0, x1, out float lo, out float hi) || hi - lo < 0.25f) return null;
            float ym = 0.5f * (lo + hi);
            var q = be.PointAt(ym);
            CityElevation.ProjectOn(X, q, out float xm);
            var p = X.PointAt(xm);
            if (Vector2.Distance(p, q) > X.HalfMax + be.HalfMax + GroundTwinGapMaxM + 8f) return null;
            var rb = new UnionRun { edge = be.index, nb = X.index, side = SideToward(be, ym, p), s0 = lo, s1 = hi, median = ra.median,
                                    owner = false, pair = ra.pair, approach = ra.approach, across = acrossX, mirror = ra };
            AddRun(t, rb);
            return rb;
        }

        /// <summary>(CHAIN) The edges across an owner's median that no sample of its walk stood beside
        /// but its median reaches (a twin's piece past a node by the run's end): each a partner run.</summary>
        static void AddChainPartners(CityMap map, Trims t, UnionRun ra, CityMap.Edge X, float x0, float x1, int[] acrossX)
        {
            if (!R8ChainOn || ra.across == null) return;
            foreach (int ci in ra.across)
            {
                var lst = t.unions[ci];
                bool has = false;
                if (lst != null) foreach (var o in lst) if (o.mirror == ra) { has = true; break; }
                if (has) continue;
                var rb = AddExactPartner(map, t, ra, X, x0, x1, map.edges[ci], acrossX);
                if (rb == null) continue;
                r8ChainPartners++;
                if (ra.mirror == null) ra.mirror = rb;
            }
        }

        // ------------------------------------------------------------------
        //  FILL: the gaps between two drawn medians
        // ------------------------------------------------------------------
        static readonly HashSet<int> r8Segs = new HashSet<int>();
        static readonly List<Vector2> r8Cov = new List<Vector2>(16);
        static readonly List<Vector2> r8Gaps = new List<Vector2>(8);
        static readonly List<CityMap.Edge> r8Met = new List<CityMap.Edge>(4);

        /// <summary>The nearest opposing twin (same name, one-way, not a ramp, running the other way)
        /// beside arc x of X, its foot on a segment (GroundTwinSample's search, nothing else).</summary>
        static bool NearestOpposingTwin(CityMap map, CityMap.Edge X, float x, out CityMap.Edge Y, out float y, out float best)
        {
            Y = null; y = 0f; best = float.MaxValue;
            var p = X.PointAt(x); var tanX = X.TangentAt(x);
            float r = X.HalfMax + GroundTwinGapMaxM + 14f;
            r8Segs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, r8Segs);
            foreach (int seg in r8Segs)
            {
                var c = map.edges[seg >> 12];
                if (c == X || c.a == c.b || !c.oneway || c.link || c.name != X.name) continue;
                int si = seg & 0xFFF;
                if (si + 1 >= c.pts.Length) continue;
                Vector2 a0 = c.pts[si], dseg = c.pts[si + 1] - a0;
                float L2 = dseg.sqrMagnitude;
                if (L2 < 1e-8f) continue;
                float L = Mathf.Sqrt(L2);
                var dirC = dseg / L;
                if (Vector2.Dot(tanX, dirC) > -0.5f) continue;
                float along = Vector2.Dot(p - a0, dirC);
                bool first = si == 0, lastSeg = si + 2 >= c.pts.Length;
                if (along < -(first ? UnionFootSlackM : 0.5f) || along > L + (lastSeg ? UnionFootSlackM : 0.5f)) continue;
                float tcl = Mathf.Clamp(along, 0f, L);
                float d = Vector2.Distance(p, a0 + dirC * tcl);
                if (d < best) { best = d; Y = c; y = c.s[si] + tcl; }
            }
            return Y != null;
        }

        /// <summary>Does a drawn median carry on from X's end (0: a, 1: b) on this side - the next
        /// edge's run through a mitred node, or the twin's run at the end's foot?</summary>
        static bool NodeCovered(CityMap map, Trims t, CityMap.Edge X, int end, int side, bool throughNodeOnly = false)
        {
            int node = end == 0 ? X.a : X.b;
            var y = MitredNext(map, t, X, node);
            if (y != null && t.unions[y.index] != null)
            {
                bool same = end == 1 ? y.a == node : y.b == node;
                int sideY = same ? side : -side;
                bool yAtA = y.a == node;
                foreach (var o in t.unions[y.index])
                {
                    if (o.side != sideY) continue;
                    DrawnRange(map, o, out float o0, out float o1);
                    if (o1 - o0 > 0.05f && (yAtA ? o0 <= 0.1f : o1 >= y.length - 0.1f)) return true;
                }
            }
            if (throughNodeOnly) return false;
            float xe = end == 0 ? 0f : X.length;
            if (!NearestOpposingTwin(map, X, xe, out var Y, out float ya, out _) || t.unions[Y.index] == null) return false;
            int sy = SideToward(Y, ya, X.PointAt(xe));
            foreach (var o in t.unions[Y.index])
            {
                if (o.side != sy) continue;
                DrawnRange(map, o, out float o0, out float o1);
                if (ya >= o0 - 0.5f && ya <= o1 + 0.5f) return true;
            }
            return false;
        }

        /// <summary>The twin beside arc x of X on <paramref name="side"/>, where a shared median
        /// belongs (the ground-twin walk's tests: short of fans, one level, in the band) and no
        /// run there already draws one to another edge.</summary>
        static bool FillTwinOk(CityMap map, Trims t, CityMap.Edge X, float x, int side, out CityMap.Edge Y, out float y)
        {
            if (!NearestOpposingTwin(map, X, x, out Y, out y, out float best)) return false;
            var p = X.PointAt(x);
            if (SideToward(X, x, Y.PointAt(y)) != side) return false;
            float xlo = t.patch[X.a] ? t.atA[X.index] + UnionFanSetbackM : 0f, xhi = t.patch[X.b] ? X.length - t.atB[X.index] - UnionFanSetbackM : X.length;
            float ylo = t.patch[Y.a] ? t.atA[Y.index] + UnionFanSetbackM : 0f, yhi = t.patch[Y.b] ? Y.length - t.atB[Y.index] - UnionFanSetbackM : Y.length;
            if (x < xlo - 1e-3f || x > xhi + 1e-3f || y < ylo - 1e-3f || y > yhi + 1e-3f) return false;
            if (Mathf.Abs(X.YAt(x) - Y.YAt(y)) > UnionSplitDyM) return false;
            int sy = SideToward(Y, y, p);
            LineModel.Extents(X, x, out float xm, out float xp);
            LineModel.Extents(Y, y, out float ym, out float yp);
            float gap = best - (side > 0 ? xp : xm) - (sy > 0 ? yp : ym);
            if (gap > GroundTwinGapMaxM || gap < DeckPairs.SqueezeM) return false;
            var ly = t.unions[Y.index];
            if (ly != null)
                foreach (var o in ly)
                {
                    if (o.side != sy) continue;
                    DrawnRange(map, o, out float o0, out float o1);
                    if (y <= o0 + 0.5f || y >= o1 - 0.5f) continue;
                    // a median drawn there to (or from) another edge than X: not ours to double
                    if (o.owner) { if (o.across == null || System.Array.IndexOf(o.across, X.index) < 0) return false; }
                    else if (o.mirror != null && o.mirror.edge != X.index) return false;
                }
            return true;
        }

        static bool TryFill(CityMap map, Trims t, CityMap.Edge X, int side, float g0, float g1)
        {
            r8Met.Clear();
            int ns = g1 - g0 < 1.2f ? 1 : 3;
            CityMap.Edge Ymid = null;
            for (int k = 0; k < ns; k++)
            {
                float x = ns == 1 || k == 1 ? 0.5f * (g0 + g1) : k == 0 ? g0 + 0.25f : g1 - 0.25f;
                if (!FillTwinOk(map, t, X, x, side, out var Y, out _)) return false;
                if (!r8Met.Contains(Y)) r8Met.Add(Y);
                if (k == ns / 2) Ymid = Y;
            }
            var met = new int[r8Met.Count];
            for (int k = 0; k < met.Length; k++) met[k] = r8Met[k].index;
            var acrossX = ChainAround(map, t, X);
            var ra = new UnionRun { edge = X.index, nb = Ymid.index, side = side, s0 = g0, s1 = g1, median = DeckPairs.Median.Barrier,
                                    owner = true, pair = -1, approach = true, across = WithContinuations(map, t, met), fill = true };
            AddRun(t, ra);
            foreach (int ci in ra.across)
            {
                var rb = AddExactPartner(map, t, ra, X, g0, g1, map.edges[ci], acrossX);
                if (rb != null && (ra.mirror == null || ci == ra.nb)) ra.mirror = rb;
            }
            if (ra.mirror == null) ra.mirror = new UnionRun { edge = ra.nb, nb = X.index, side = -side, median = DeckPairs.Median.Barrier, pair = -1, approach = true, across = acrossX };
            return true;
        }

        /// <summary>
        /// (FILL) Every barriered twin carriageway's median side: the stretches no run covers
        /// (owner or partner) between two that do - on this edge, or carried on through its node -
        /// up to <see cref="R8FillMaxM"/> long, with the opposing twin beside in the band at one
        /// level: an owner run laid over each, its partner runs exact. Their ends meet the runs
        /// either side, so neither is turned down nor left square (Continued).
        /// </summary>
        static int FillMedianGaps(CityMap map, Trims t, out float metres)
        {
            metres = 0f; int n = 0;
            var E = map.edges;
            for (int xi = 0; xi < E.Length; xi++)
            {
                var X = E[xi];
                if (X.a == X.b || !X.oneway || X.link || !Barriered(X) || string.IsNullOrEmpty(X.name)) continue;
                var list = t.unions[xi];
                for (int side = -1; side <= 1; side += 2)
                {
                    r8Cov.Clear();
                    if (list != null)
                        foreach (var r in list)
                        {
                            if (r.side != side) continue;
                            DrawnRange(map, r, out float c0, out float c1);
                            if (c1 - c0 > 0.05f) r8Cov.Add(new Vector2(c0, c1));
                        }
                    if (r8Cov.Count == 0)
                    {
                        // (I-277 e2139, uptown 8688 m) no median at all on this side: a run carried on
                        // into it through its node meets twins still in the band for less than the
                        // walk's shortest run (12 m) before the median widens past it - each
                        // carriageway stood its own Jersey 8.6 m apart for those metres (a box).
                        // The median carries on as far as the twin stays in the band, up to R8FillMaxM.
                        for (int end = 0; end < 2; end++)
                        {
                            // carried on through the node itself (Continued sees it): a piece both of
                            // whose ends are closed and under MinMedianRunM is drawn flush - an opening
                            if (!NodeCovered(map, t, X, end, side, true)) continue;
                            float x0 = end == 0 ? 0f : X.length, L = 0f;
                            for (float d = 0.5f; d <= R8FillMaxM + 1e-3f && d <= X.length; d += 1f)
                            {
                                if (!FillTwinOk(map, t, X, end == 0 ? x0 + d : x0 - d, side, out _, out _)) break;
                                L = d;
                            }
                            if (L < 2f) continue;
                            if (X.length - L <= UnionNodeSnapM) L = X.length;
                            float g0 = end == 0 ? 0f : X.length - L, g1 = end == 0 ? L : X.length;
                            if (TryFill(map, t, X, side, g0, g1)) { n++; metres += g1 - g0; }
                        }
                        continue;
                    }
                    r8Cov.Sort((p, q) => p.x.CompareTo(q.x));
                    r8Gaps.Clear();
                    float reach = r8Cov[0].y;
                    if (r8Cov[0].x > 0.3f && r8Cov[0].x <= R8FillMaxM && NodeCovered(map, t, X, 0, side)) r8Gaps.Add(new Vector2(0f, r8Cov[0].x));
                    for (int k = 1; k < r8Cov.Count; k++)
                    {
                        var c = r8Cov[k];
                        if (c.x - reach > 0.3f && c.x - reach <= R8FillMaxM) r8Gaps.Add(new Vector2(reach, c.x));
                        reach = Mathf.Max(reach, c.y);
                    }
                    if (X.length - reach > 0.3f && X.length - reach <= R8FillMaxM && NodeCovered(map, t, X, 1, side)) r8Gaps.Add(new Vector2(reach, X.length));
                    foreach (var g in r8Gaps)
                        if (TryFill(map, t, X, side, g.x, g.y)) { n++; metres += g.y - g.x; }
                }
            }
            return n;
        }

        // ------------------------------------------------------------------
        //  SEAMS: a median carried on by one owned across
        // ------------------------------------------------------------------

        /// <summary>
        /// (SEAM) Each owner run's carried-on end whose median is carried on by a run OWNED ACROSS
        /// (an owner run on an edge of its road across, its own end within <see cref="SeamReachM"/>
        /// of this end's foot and lying beyond it): the two are linked, and each draws its end's
        /// cross-line to the other's edge at the other's end (<see cref="SeamDelta"/>).
        /// </summary>
        static int FindSeams(CityMap map, Trims t)
        {
            int n = 0;
            var E = map.edges;
            for (int ei = 0; ei < E.Length; ei++)
            {
                var list = t.unions[ei];
                if (list == null) continue;
                foreach (var r in list)
                {
                    if (!r.owner || r.across == null || r.s1 - r.s0 < 0.5f) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        if ((end == 0 ? r.open0 : r.open1) || (end == 0 ? r.seamEdge0 : r.seamEdge1) >= 0) continue;
                        if (!FindSeam(map, t, r, end, out var o, out int oe)) continue;
                        float s = end == 0 ? r.s0 : r.s1, m = oe == 0 ? o.s0 : o.s1;
                        if (end == 0) { r.seamEdge0 = o.edge; r.seamArc0 = m; } else { r.seamEdge1 = o.edge; r.seamArc1 = m; }
                        if (oe == 0) { o.seamEdge0 = r.edge; o.seamArc0 = s; } else { o.seamEdge1 = r.edge; o.seamArc1 = s; }
                        n++;
                    }
                }
            }
            return n;
        }

        static bool FindSeam(CityMap map, Trims t, UnionRun r, int end, out UnionRun best, out int bestEnd)
        {
            best = null; bestEnd = 0;
            var X = map.edges[r.edge];
            float s = end == 0 ? r.s0 : r.s1;
            var pe = X.PointAt(Mathf.Clamp(s, 0f, X.length));
            FootAny(map, r.across, pe, out var Yf, out float f);
            if (Yf == null) return false;
            var Pf = Yf.PointAt(f);
            // which way along the road across lies "beyond" this end: from the foot of a point
            // inside the run to this end's foot
            var pIn = X.PointAt(Mathf.Clamp(end == 0 ? s + 2f : s - 2f, 0f, X.length));
            FootAny(map, r.across, pIn, out var Yi, out float fi);
            if (Yi == null) return false;
            var dir = Pf - Yi.PointAt(fi);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            float bestD = SeamReachM;
            foreach (int yi in r.across)
            {
                var lst = t.unions[yi];
                if (lst == null) continue;
                var Y = map.edges[yi];
                foreach (var o in lst)
                {
                    if (!o.owner || o == r || o.edge == r.edge || o.s1 - o.s0 < 0.5f) continue;
                    for (int oe = 0; oe < 2; oe++)
                    {
                        if ((oe == 0 ? o.open0 : o.open1) || (oe == 0 ? o.seamEdge0 : o.seamEdge1) >= 0) continue;
                        float m = oe == 0 ? o.s0 : o.s1;
                        var q = Y.PointAt(m);
                        float d = Vector2.Distance(q, Pf);
                        if (d >= bestD) continue;
                        // it carries on beyond (its inside lies past its end, the way beyond is)
                        var qIn = Y.PointAt(Mathf.Clamp(oe == 0 ? m + 1f : m - 1f, 0f, Y.length));
                        if (Vector2.Dot(qIn - q, dir) <= 0f) continue;
                        // its side faces this run's edge
                        if (o.side != SideToward(Y, m, pe)) continue;
                        bestD = d; best = o; bestEnd = oe;
                    }
                }
            }
            return best != null;
        }

        /// <summary>(SEAM) At a run end linked to a run owned across: the move from this end's own
        /// partner point (the perpendicular foot) to the other run's edge at its end, and the
        /// length over which it is blended in (never steeper than 1:1.5 along the median).</summary>
        static void SeamDelta(CityMap map, Trims trims, CityMap.Edge e, UnionRun run, int end, out Vector3 delta, out float len)
        {
            delta = default; len = 0f;
            int se = end == 0 ? run.seamEdge0 : run.seamEdge1;
            if (se < 0 || se >= map.edges.Length) return;
            float s = end == 0 ? run.s0 : run.s1, m = end == 0 ? run.seamArc0 : run.seamArc1;
            var pl = e.PointAt(Mathf.Clamp(s, 0f, e.length));
            FootAny(map, run.across, pl, out var o, out float tk);
            if (o == null) return;
            var nat = FacingEdgeAt(map, trims, o, tk, pl);
            var Y = map.edges[se];
            var ovr = FacingEdgeAt(map, trims, Y, Mathf.Clamp(m, 0f, Y.length), pl);
            delta = ovr - nat;
            float h = new Vector2(delta.x, delta.z).magnitude;
            if (h < 0.02f || h > 2f * SeamReachM) { delta = default; return; }
            len = Mathf.Max(3f, 1.5f * h);
        }
    }
}
