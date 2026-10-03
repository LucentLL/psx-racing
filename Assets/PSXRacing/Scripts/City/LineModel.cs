using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE LINE MODEL (Charlotte refinement WP-11b; plan amendment A4 and A8).
    /// The owner, after ten minutes on /city/ (2026-09-29): "All sections of
    /// road that add an additional lane for a turn lane, instead expand a lane
    /// and widen on both sides, as if center aligned. Two lane to three lane to
    /// two lane. In reality it is just two lanes keeping consistent lanes with
    /// an additional lane added to left or right side for turn lane."
    ///
    /// Every drawn lateral position - both ribbon edges and every painted line
    /// - is the edge's OSM line plus an offset from here:
    ///
    ///   OFFSET   the lanes' centre sits <c>lmOff</c> off the OSM line (section
    ///            TAPR, WP-10: the through lanes keep their position along a
    ///            chain, so a 2 -> 3 -> 2 piece is offset by half the added lane
    ///            and its ribbon grows on ONE side), each side with its own
    ///            shoulder (a freeway's 1.2 m inside, 3 m outside: the lanes,
    ///            not the pavement, are centred).
    ///   TAPERS   where two through ribbons meet mitred and one side's edge
    ///            steps, only THAT side eases, on the wider arm, over TAPR's
    ///            MUTCD length (smoothstep; never clamped to one OSM piece: it
    ///            runs on through the next mitred joints), or a class default
    ///            where TAPR has no record. The other edge and every line on
    ///            the fixed side stay straight.
    ///   LINES    each painted line of the profile's texture is drawn as its
    ///            own column pair at a fixed U (CityMeshes: no squeezed
    ///            texture). Across a taper a line with a partner in the narrow
    ///            layout eases to it; a line with none (the added lane's line)
    ///            ends where the taper reaches full width.
    ///
    /// Traffic, the AI and the city race path read <see cref="LaneCentre"/>,
    /// so they drive the lanes the paint shows.
    /// </summary>
    public static class LineModel
    {
        /// <summary>One TAPR record: a lane-count change on the wide run's
        /// edge at one of its ends (0 a, 1 b). side 0 = the left of a->b moves,
        /// 1 the right; flags 1 turn bay, 2 full width at the node, 4 a drop,
        /// 8 untagged, 16 a SHIFT (roads pass L4: a run re-anchored at a
        /// junction eases back onto its line past it; off = the shift at the
        /// node, edge frame, len the shifting taper); dw the width change, len
        /// the MUTCD length (0: none). A change that widens both directions
        /// (Q4) has a record for each side.</summary>
        public struct Tapr { public int edge; public byte end, side, src, flags; public float dw, len, room, off; }

        /// <summary>One eased one-sided taper on an edge: the side (+1 left of
        /// a->b, the ribbon's plus side; -1 right), the reduction dw at the
        /// join, easing to 0 over len; d0 = the chain distance from the join
        /// to this edge's near end (fromA: the a end). narrow: the profile of
        /// the narrow arm at the join (the line matching). relay (roads pass
        /// L4): no width change - the lines move from this edge's layout to
        /// partner[k] at the join (dw = 1: the blend; span the largest move).
        /// twoRole (L4, owner Q4): one of a pair easing BOTH edges at once
        /// (each direction on its own outside) over one length, dwOther the
        /// other side's; the lead (1) carries the lines between the edges, the
        /// other (2) only its own edge line.</summary>
        public struct Ease { public sbyte side; public bool fromA, narrowFlip, shift, relay; public float dw, len, d0, span, dwOther; public byte twoRole; public int narrow; public float[] partner; public Layout narrowLay; }

        /// <summary>One SPLT record (WP-11, critic C11): where an undivided
        /// road opens into its two carriageways - the node, the undivided edge,
        /// the carriageway leaving and the one arriving, their centre offsets
        /// at the node in the undivided edge's frame (+ = left of its direction
        /// into the node), and the MUTCD shifting-taper rate.</summary>
        public struct Split { public int node, u, a, b; public float offA, offB, rate; }

        public static float Smooth(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

        static Dictionary<long, int> taprAt;
        static CityMap taprMap;
        /// <summary>A lane-count record's key: (edge, end, side) - a change that
        /// widens both directions (Q4, roads pass L4) has one per side.</summary>
        static long TaprKey(int edge, int end, int side) => ((long)edge << 2) | ((long)end << 1) | (long)side;

        /// <summary>At parse, before the elevation solve: each edge's lane
        /// centre offset and full extents. Eases come later
        /// (<see cref="BuildEases"/>, from CityMeshes.ComputeTrims).</summary>
        public static void Init(CityMap map, float[] taprOff)
        {
            foreach (var e in map.edges)
            {
                float off = taprOff != null && e.index < taprOff.Length ? taprOff[e.index] : 0f;
                float lanesHalf = (e.width - e.shl - e.shr) * 0.5f;
                e.lmOff = off;
                e.lmPlus = lanesHalf + e.shl + off;
                e.lmMinus = lanesHalf + e.shr - off;
                e.lmEase = null;
            }
            taprAt = new Dictionary<long, int>();
            taprMap = map;
            if (map.tapr != null)
                for (int i = 0; i < map.tapr.Length; i++)
                    if ((map.tapr[i].flags & 16) == 0) taprAt[TaprKey(map.tapr[i].edge, map.tapr[i].end, map.tapr[i].side)] = i;
            MirroredFixed = 0; MirroredLeft = 0;
            if (map.tapr != null) foreach (var t in map.tapr) if ((t.flags & 16) == 0) FixMirrored(map, t);
        }

        /// <summary>WP-10's data: 14 of its 7,652 lane-count changes (all two
        /// lanes, at junction nodes) carry the narrow run's offset MIRRORED - the
        /// side TAPR says moves is the one whose edges line up (I-277 at US 74:
        /// I-277's two lanes were drawn as the right pair of the four, and US 74's
        /// two, joining on the right, squeezed them 4.4 m). TAPR's side is the
        /// one the tags and the geometry agree on, so the narrow run is moved to
        /// line up with the wide one's FIXED edge: along its 2-arm continuation
        /// with the same profile and offset, out to the junction at its far end
        /// (where the next mouth absorbs the move). Left alone if the run meets
        /// another lane change first.</summary>
        public static int MirroredFixed, MirroredLeft;
        static void FixMirrored(CityMap map, Tapr t)
        {
            if (t.edge < 0 || t.edge >= map.edges.Length) return;
            var W = map.edges[t.edge];
            int n = t.end == 0 ? W.a : W.b;
            Vector2 OutD(CityMap.Edge e, int node) => e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);
            var dW = OutD(W, n);
            CityMap.Edge N = null; float bd = -0.85f;
            foreach (var oi in map.nodeEdges[n])
            {
                var o = map.edges[oi];
                if (o == W || o.a == o.b) continue;
                float d = Vector2.Dot(dW, OutD(o, n));
                if (d < bd) { bd = d; N = o; }
            }
            if (N == null) return;
            bool same = (N.b == n) == (W.a == n);          // N's plus is W's plus
            float nPlus = same ? N.lmPlus : N.lmMinus, nMinus = same ? N.lmMinus : N.lmPlus;
            float dPlus = W.lmPlus - nPlus, dMinus = W.lmMinus - nMinus;
            float moving = t.side == 0 ? dPlus : dMinus, fixd = t.side == 0 ? dMinus : dPlus;
            if (!(Mathf.Abs(moving) <= 0.05f && Mathf.Abs(fixd - t.dw) <= 0.3f)) return;
            // the move in W's frame that lines N's fixed side up: fixed plus ->
            // N.plus += fixd (its offset +fixd); fixed minus -> N.minus += fixd
            // (its offset -fixd)
            float deltaW = t.side == 0 ? -fixd : fixd;      // N's offset change, W's frame
            // the narrow run: N, then on through 2-arm nodes while the profile and offset hold
            var run = new List<(CityMap.Edge e, float sign)>();
            var cur = N; int at = n; float sign = same ? 1f : -1f;
            for (int guard = 0; guard < 64; guard++)
            {
                run.Add((cur, sign));
                int far = cur.a == at ? cur.b : cur.a;
                if (map.nodeEdges[far].Count != 2) break;   // a junction (or a dead end): the mouth absorbs it
                int nxI = map.nodeEdges[far][0] == cur.index ? map.nodeEdges[far][1] : map.nodeEdges[far][0];
                var nx = map.edges[nxI];
                if (nx == cur || nx.a == nx.b) break;
                if (nx.profile != cur.profile || Mathf.Abs(nx.lmOff * ((nx.a == far) == (cur.b == far) ? 1f : -1f) - cur.lmOff) > 0.05f)
                { MirroredLeft++; return; }
                // through far: nx runs the same way as cur if cur arrives (b) and nx leaves (a)
                sign *= ((nx.a == far) == (cur.b == far)) ? 1f : -1f;
                cur = nx; at = far;
            }
            foreach (var (e, sg) in run)
            {
                float d = deltaW * sg;
                e.lmOff += d; e.lmPlus += d; e.lmMinus -= d;
            }
            MirroredFixed++;
        }

        /// <summary>The ribbon's extents off the OSM line at arc position s:
        /// ePlus to the left of a->b (CityMeshes' R vertex, p + right * ePlus),
        /// eMinus to the right (its L vertex, p - right * eMinus), tapers
        /// applied. An edge the model never saw is centred at half its width.</summary>
        public static void Extents(CityMap.Edge e, float s, out float eMinus, out float ePlus)
        {
            ePlus = e.lmPlus; eMinus = e.lmMinus;
            if (ePlus == 0f && eMinus == 0f) { ePlus = eMinus = e.width * 0.5f; return; }
            var ez = e.lmEase;
            if (ez == null) return;
            float rp = 0f, rm = 0f, sh = 0f;
            for (int i = 0; i < ez.Length; i++)
            {
                if (ez[i].relay) continue;
                float r = Reduction(e, ez[i], s);
                if (ez[i].shift) { sh += r; continue; }
                if (ez[i].side > 0) { if (r > rp) rp = r; } else if (r > rm) rm = r;
            }
            ePlus -= rp - sh; eMinus -= rm + sh;
        }

        /// <summary>The median tapers' lateral shift at s (+ left of a->b).</summary>
        public static float ShiftAt(CityMap.Edge e, float s)
        {
            var ez = e.lmEase;
            if (ez == null) return 0f;
            float sh = 0f;
            for (int i = 0; i < ez.Length; i++) if (ez[i].shift) sh += Reduction(e, ez[i], s);
            return sh;
        }

        static float Reduction(CityMap.Edge e, in Ease z, float s)
        {
            float d = z.d0 + (z.fromA ? s : e.length - s);
            if (d >= z.len) return 0f;
            return z.dw * (1f - Smooth(Mathf.Max(0f, d) / z.len));
        }

        /// <summary>Half width and centre offset (+ left of a->b) at s: the
        /// ribbon is centre ± half about p + right * centre.</summary>
        public static void CentreAt(CityMap.Edge e, float s, out float centre, out float half)
        {
            Extents(e, s, out float eM, out float eP);
            centre = 0.5f * (eP - eM); half = 0.5f * (eP + eM);
        }

        /// <summary>The LANES' centre off the OSM line (+ left of a->b): halfway
        /// between the two edges less their shoulders, eased with any taper.
        /// Traffic, the AI and the race path drive about this line.</summary>
        public static float LaneCentre(CityMap.Edge e, float s)
        {
            Extents(e, s, out float eM, out float eP);
            return 0.5f * ((eP - e.shl) - (eM - e.shr));
        }

        /// <summary>The point on the lanes' centre at s (world plan).</summary>
        public static Vector2 LanePoint(CityMap.Edge e, float s)
        {
            // at a polyline vertex, the bisector (the ribbon's own section)
            var t = e.TangentAt(Mathf.Max(0f, s - 0.01f)) + e.TangentAt(Mathf.Min(e.length, s + 0.01f));
            t = t.sqrMagnitude > 1e-8f ? t.normalized : e.TangentAt(s);
            return e.PointAt(s) + new Vector2(-t.y, t.x) * LaneCentre(e, s);
        }

        // ================================================================
        //  Tapers: built once per map from the mitred joins
        // ================================================================

        /// <summary>Every mitred through join (node, the two edges), and the
        /// mitre partner of an edge at a node (-1: none) to run a taper on
        /// through. Clears and rebuilds every edge's eases.</summary>
        public static void BuildEases(CityMap map, List<(int node, int e, int o)> joins, System.Func<CityMap.Edge, int, int> partner)
        {
            var add = new List<Ease>[map.edges.Length];
            foreach (var e in map.edges) e.lmEase = null;
            EasedJoins = 0; EasedDefault = 0; EaseSteps = 0; SplitTapers = 0; HeldJoins = 0; CrossedJoins = 0;
            TwoSidedJoins = 0; RelayJoins = 0; RelayShort = 0; ReShifts = 0;
            var splitAt = new Dictionary<int, Split>();
            if (map.splt != null) foreach (var sp in map.splt) splitAt[sp.node] = sp;
            // the joins whose two arms agree in width but not in their lines
            // (roads pass L4): found first, so a relay at one end of an edge
            // knows whether the other end needs its room too
            var relayJoin = new HashSet<int>(); var relayNode = new HashSet<int>();
            for (int j = 0; j < joins.Count; j++)
            {
                var (n, ei, oi) = joins[j];
                if (ei == oi || (splitAt.TryGetValue(n, out var sj) && (ei == sj.u || oi == sj.u))) continue;
                if (NeedsRelay(map.edges[ei], map.edges[oi], n)) { relayJoin.Add(j); relayNode.Add(n); }
            }
            for (int jn = 0; jn < joins.Count; jn++)
            {
                var (n, ei, oi) = joins[jn];
                var E = map.edges[ei]; var O = map.edges[oi];
                if (E == O) continue;
                // where an undivided road opens into its carriageways, the one
                // it mitres with takes its half by the median taper below; the
                // other half is the other carriageway's (no taper there)
                if (splitAt.TryGetValue(n, out var spj) && (ei == spj.u || oi == spj.u)) continue;
                bool eIn = E.b == n, oOut = O.a == n;   // travel: along E into n, along O out of n
                // BOTH EDGES STEP (a lane-count change the data centred on the
                // node - a junction the builder mitres, a one-way carriageway
                // leaving a two-way road): never widened on both sides (A8).
                // One edge is held: the narrow arm is shifted over to it on a
                // shifting taper, and the wide arm eases the whole difference
                // on the other side.
                {
                    float DStep(int sideT)
                    {
                        int es = eIn ? sideT : -sideT, os = oOut ? sideT : -sideT;
                        return (es > 0 ? E.lmPlus : E.lmMinus) - (os > 0 ? O.lmPlus : O.lmMinus);
                    }
                    float dLt = DStep(1), dRt = DStep(-1);
                    if ((dLt > 0.02f && dRt > 0.02f) || (dLt < -0.02f && dRt < -0.02f))
                    {
                        bool eWide = dLt > 0f;
                        var W = eWide ? E : O; var N = eWide ? O : E;
                        // travel frame (+1 left): which side is held
                        int fixT;
                        bool nForward = N == E ? eIn : oOut;     // N's a->b runs the travel way
                        bool wForward = W == E ? eIn : oOut;
                        if (N.oneway != W.oneway)
                        {
                            // a one-way carriageway keeps its direction's half: its
                            // travel-right edge is the held one
                            var ow = N.oneway ? N : W;
                            bool owForward = ow == E ? eIn : oOut;
                            fixT = owForward ? -1 : 1;
                        }
                        else if (TwoSided(W, n)) fixT = 0;   // Q4: each direction widens on its own outside
                        else if (taprAt != null && (taprAt.TryGetValue(TaprKey(W.index, W.a == n ? 0 : 1, 0), out int tiW)
                                                    || taprAt.TryGetValue(TaprKey(W.index, W.a == n ? 0 : 1, 1), out tiW)))
                        {
                            int mW = taprMap.tapr[tiW].side == 0 ? 1 : -1;       // the moving side, W's frame
                            fixT = -(wForward ? mW : -mW);
                        }
                        else fixT = Mathf.Abs(dRt) <= Mathf.Abs(dLt) ? -1 : 1;   // the smaller step is held: the fewest lanes move
                        if (fixT == 0)
                        {
                            // TWO-SIDED (owner Q4): both edges ease on the wide
                            // arm - the centre line and the through lanes stay put
                            TwoSidedJoins++;
                            // one length for both (the longer record's, within
                            // the room both sides share): the lines between the
                            // edges then move once, to where the narrow arm has them
                            float dl2 = eWide ? dLt : -dLt, dr2 = eWide ? dRt : -dRt, len2 = 0f; bool fromT2 = true;
                            for (int sideT = -1; sideT <= 1; sideT += 2)
                            {
                                float d = sideT > 0 ? dl2 : dr2;
                                if (d <= 0.005f) continue;
                                len2 = Mathf.Max(len2, LengthFor(W, n, sideT * (wForward ? 1 : -1), d, out bool ft));
                                fromT2 &= ft;
                            }
                            len2 = Mathf.Max(0.5f, Mathf.Min(len2, RoomAlong(map, partner, W, n, 1, true)));
                            bool lead = true;
                            for (int sideT = 1; sideT >= -1; sideT -= 2)
                            {
                                float d = sideT > 0 ? dl2 : dr2, dO = sideT > 0 ? dr2 : dl2;
                                if (d <= 0.005f) continue;
                                int wSd = sideT * (wForward ? 1 : -1);
                                EasedJoins++; if (!fromT2) EasedDefault++;
                                Propagate(map, add, partner, W, n, wSd, d, len2, 0f, N, (W.a == n) == (N.a == n), 0, Mathf.Max(0f, dO), (byte)(lead ? 1 : 2));
                                lead = false;
                            }
                            continue;
                        }
                        float dFix = Mathf.Abs(fixT > 0 ? dLt : dRt), dMove = Mathf.Abs(dLt) + Mathf.Abs(dRt);
                        // N shifts toward the held side (in its own frame)
                        float shiftN = fixT * dFix * (nForward ? 1f : -1f);
                        float shLen = Mathf.Max(FloorOf(N), dFix * ShiftRate(N));
                        PropagateShift(map, add, partner, N, n, shiftN, shLen, 0f, 0);
                        int wSide = -fixT * (wForward ? 1 : -1);
                        float len = LengthFor(W, n, wSide, dMove, out bool fromTapr);
                        EasedJoins++; HeldJoins++; if (!fromTapr) EasedDefault++;
                        Propagate(map, add, partner, W, n, wSide, dMove, len, 0f, N, (W.a == n) == (N.a == n), 0);
                        continue;
                    }
                    // CROSSED: each arm sticks out on a different side - the two
                    // ribbons' lanes are off each other (the data re-anchored at a
                    // junction the builder mitres). Easing each side on its wider
                    // arm pinched the road to what the two share. The narrower arm
                    // is shifted instead (by the least that makes one edge flush),
                    // and the wider eases what is left, on the other side.
                    if ((dLt > 0.02f && dRt < -0.02f) || (dLt < -0.02f && dRt > 0.02f))
                    {
                        bool eWider = E.lmPlus + E.lmMinus >= O.lmPlus + O.lmMinus;
                        var W = eWider ? E : O; var N = eWider ? O : E;
                        float sg = eWider ? 1f : -1f;                   // (W - N) = sg * (E - O)
                        float dLw = sg * dLt, dRw = sg * dRt;
                        // shift N (travel frame, + left): dLw flushes the left, -dRw the right
                        float shT = Mathf.Abs(dLw) <= Mathf.Abs(dRw) ? dLw : -dRw;
                        int openT = Mathf.Abs(dLw) <= Mathf.Abs(dRw) ? -1 : 1;   // the side left to ease
                        bool nForward = N == E ? eIn : oOut, wForward = W == E ? eIn : oOut;
                        float shiftN = shT * (nForward ? 1f : -1f);
                        float shLen = Mathf.Max(FloorOf(N), Mathf.Abs(shT) * ShiftRate(N));
                        PropagateShift(map, add, partner, N, n, shiftN, shLen, 0f, 0);
                        CrossedJoins++;
                        float rest = dLw + dRw;                         // W's extra width, on the open side
                        if (rest > 0.005f)
                        {
                            int wSide = openT * (wForward ? 1 : -1);
                            float len = LengthFor(W, n, wSide, rest, out bool fromTapr);
                            EasedJoins++; if (!fromTapr) EasedDefault++;
                            Propagate(map, add, partner, W, n, wSide, rest, len, 0f, N, (W.a == n) == (N.a == n), 0);
                        }
                        continue;
                    }
                }
                if (relayJoin.Contains(jn)) { Relay(map, add, E, O, n, relayNode); continue; }
                for (int sideT = -1; sideT <= 1; sideT += 2)
                {
                    int eSide = eIn ? sideT : -sideT, oSide = oOut ? sideT : -sideT;
                    float extE = eSide > 0 ? E.lmPlus : E.lmMinus, extO = oSide > 0 ? O.lmPlus : O.lmMinus;
                    float d = extE - extO;
                    if (Mathf.Abs(d) <= 0.005f) continue;
                    var W = d > 0f ? E : O; var N = d > 0f ? O : E;
                    int wSide = d > 0f ? eSide : oSide;
                    float len = LengthFor(W, n, wSide, Mathf.Abs(d), out bool fromTapr);
                    if (len < 0.5f) { EaseSteps++; continue; }
                    EasedJoins++; if (!fromTapr) EasedDefault++;
                    // the narrow arm's painter frame against the wide's: flipped
                    // when the two run opposite ways through the node (a one-way
                    // layout's yellow left edge is then on the wide one's right)
                    bool flip = (W.a == n) == (N.a == n);
                    Propagate(map, add, partner, W, n, wSide, Mathf.Abs(d), len, 0f, N, flip, 0);
                }
            }
            // THE MEDIAN TAPERS (critic C11, section SPLT): each carriageway
            // starts where its lanes are in the undivided road and eases onto
            // its own line over the MUTCD shifting taper - two carriageways,
            // never one road that bulges into two
            foreach (var sp in splitAt.Values)
            {
                if (sp.u < 0 || sp.u >= map.edges.Length) continue;
                var U = map.edges[sp.u];
                var tu = U.a == sp.node ? -U.TangentAt(0f) : U.TangentAt(U.length);   // into the node
                var leftU = new Vector2(-tu.y, tu.x);
                for (int c = 0; c < 2; c++)
                {
                    int ci = c == 0 ? sp.a : sp.b;
                    float offC = c == 0 ? sp.offA : sp.offB;
                    if (ci < 0 || ci >= map.edges.Length) continue;
                    var C = map.edges[ci];
                    if (C.a != sp.node && C.b != sp.node) continue;
                    var tc = C.a == sp.node ? C.TangentAt(0f) : C.TangentAt(C.length);
                    float sign = Vector2.Dot(leftU, new Vector2(-tc.y, tc.x)) >= 0f ? 1f : -1f;
                    // the carriageway's OUTER edge (away from the other one: the
                    // side SPLT's offset points to) runs on flush with the
                    // undivided road's edge on that side
                    float out1 = Mathf.Sign(offC * sign);           // C's frame: +1 its plus side
                    if (out1 == 0f) continue;
                    // U's extent on that side (U's plus is leftU when it runs into the node)
                    bool outIsLeftU = out1 * sign > 0f, uPlusIsLeftU = U.b == sp.node;
                    float uOut = outIsLeftU == uPlusIsLeftU ? U.lmPlus : U.lmMinus;
                    float cOut = out1 > 0f ? C.lmPlus : C.lmMinus;
                    float shift0 = out1 * (uOut - cOut);
                    if (Mathf.Abs(shift0) < 0.02f) continue;
                    float len = Mathf.Max(FloorOf(C), Mathf.Max(10f, sp.rate) * Mathf.Abs(shift0));
                    PropagateShift(map, add, partner, C, sp.node, shift0, len, 0f, 0);
                    SplitTapers++;
                }
            }
            // SHIFT records (roads pass L4, plan B6 FX3): a run re-anchored at
            // a junction starts where the lanes were and eases back onto its
            // line past it - never a jump across the junction
            if (map.tapr != null)
                foreach (var t in map.tapr)
                {
                    if ((t.flags & 16) == 0 || t.edge < 0 || t.edge >= map.edges.Length) continue;
                    var e = map.edges[t.edge];
                    if (e.a == e.b) continue;
                    // at a MITRED node the join's own eases already draw the
                    // step smoothly (held / crossed above): no second shift
                    if (partner(e, t.end == 0 ? e.a : e.b) >= 0) continue;
                    PropagateShift(map, add, partner, e, t.end == 0 ? e.a : e.b, t.off, Mathf.Max(0.5f, t.len), 0f, 0);
                    ReShifts++;
                }
            for (int i = 0; i < add.Length; i++) if (add[i] != null) map.edges[i].lmEase = add[i].ToArray();
        }
        public static int SplitTapers, HeldJoins, CrossedJoins;
        /// <summary>Roads pass L4, for the audit: joins widened on both sides
        /// by two records (Q4), relays (lines moved between two layouts of one
        /// width), of them shorter than the MUTCD length (no room), and SHIFT
        /// records drawn.</summary>
        public static int TwoSidedJoins, RelayJoins, RelayShort, ReShifts;

        /// <summary>Does the wide arm carry a record on each side at this node (Q4)?</summary>
        static bool TwoSided(CityMap.Edge w, int node)
        {
            if (taprAt == null) return false;
            int end = w.a == node ? 0 : 1;
            return taprAt.ContainsKey(TaprKey(w.index, end, 0)) && taprAt.ContainsKey(TaprKey(w.index, end, 1));
        }

        // ================================================================
        //  The relay (roads pass L4): one width, two line sets
        // ================================================================

        /// <summary>Two two-way arms of one width meeting mitred whose lines
        /// do not meet: a 2+1 split against a 1+2, a double yellow against a
        /// TWLTL. L2 drew each edge's own split, and at 681 such joins the
        /// centre line jumped a lane.</summary>
        static bool NeedsRelay(CityMap.Edge E, CityMap.Edge O, int n)
        {
            if (E.oneway || O.oneway || E.a == E.b || O.a == O.b) return false;
            bool eIn = E.b == n, oOut = O.a == n;
            float eP = eIn ? E.lmPlus : E.lmMinus, eM = eIn ? E.lmMinus : E.lmPlus;
            float oP = oOut ? O.lmPlus : O.lmMinus, oM = oOut ? O.lmMinus : O.lmPlus;
            if (Mathf.Abs(eP - oP) > 0.02f || Mathf.Abs(eM - oM) > 0.02f) return false;
            var lE = LayoutOf(E); var lO = LayoutOf(O);
            if (lE.m.Length == 0 || lO.m.Length == 0 || Mathf.Abs(lE.W - lO.W) > 0.02f) return false;
            // O's lines in E's frame (E's a->b): flipped when the two run opposite ways
            var oL = (E.a == n) == (O.a == n) ? Flipped(lO) : lO;
            if (lE.m.Length != oL.m.Length) return true;
            for (int k = 0; k < lE.m.Length; k++)
                if (Mathf.Abs(lE.m[k] - oL.m[k]) > 0.02f) return true;
            return false;
        }

        /// <summary>The relay at one join: each arm's lines ease to the middle
        /// of the two layouts (weighted by the two shares) over its share of
        /// the MUTCD length - the merging taper where a lane line ends (a
        /// direction loses a lane), the shifting taper (half) where lines only
        /// move - centred on the node, the share an arm has no room for given
        /// to the other. A line with no partner on the other side ends where
        /// its arm's share reaches full width.</summary>
        static void Relay(CityMap map, List<Ease>[] add, CityMap.Edge E, CityMap.Edge O, int n, HashSet<int> relayNode)
        {
            var lE = LayoutOf(E); var lO = LayoutOf(O);
            bool same = (E.a == n) != (O.a == n);              // one arrives, one leaves: one a->b frame
            var oInE = same ? lO : Flipped(lO);
            var pE = RelayPairs(lE, oInE);                      // E line k -> O's m (E frame), or NaN
            var eInO = same ? lE : Flipped(lE);
            var pO = RelayPairs(lO, eInO);
            float move = 0f; bool ends = false;
            for (int k = 0; k < pE.Length; k++) { if (float.IsNaN(pE[k])) ends |= lE.kind[k] == KWhiteDash; else move = Mathf.Max(move, Mathf.Abs(pE[k] - lE.m[k])); }
            for (int k = 0; k < pO.Length; k++) if (float.IsNaN(pO[k])) ends |= lO.kind[k] == KWhiteDash;
            if (move < 0.005f && !ends) return;
            float mph = Mathf.Max(SpeedMph(E), SpeedMph(O));
            float rate = Mathf.Max(10f, mph <= 40f ? mph * mph / 60f : mph);
            float L = Mathf.Max(Mathf.Max(FloorOf(E), FloorOf(O)), Mathf.Max(move, ends ? RoadProfiles.LaneM : 0f) * rate * (ends ? 1f : 0.5f));
            float Room(CityMap.Edge x) => x.length * (relayNode.Contains(x.a == n ? x.b : x.a) ? 0.45f : 0.9f);
            float rE = Room(E), rO = Room(O);
            float lenE = Mathf.Min(rE, Mathf.Max(0.5f * L, L - rO)), lenO = Mathf.Min(rO, L - lenE);
            // a share under half a metre is no taper: the other arm takes it all
            if (lenE < 0.5f) lenE = 0f;
            if (lenO < 0.5f) lenO = 0f;
            if (lenE + lenO < 0.5f) return;
            if (lenE + lenO < L - 0.5f) RelayShort++;
            RelayJoins++;
            float fE = lenE + lenO > 1e-3f ? lenE / (lenE + lenO) : 0.5f;
            AddRelay(add, E, n, lE, pE, fE, lenE);
            AddRelay(add, O, n, lO, pO, 1f - fE, lenO);
        }

        static void AddRelay(List<Ease>[] add, CityMap.Edge x, int n, Layout lay, float[] partnerM, float f, float len)
        {
            if (len < 0.5f || f <= 1e-4f) return;
            float plus = x.lmPlus != 0f || x.lmMinus != 0f ? x.lmPlus : x.width * 0.5f;
            var z = new Ease { relay = true, fromA = x.a == n, dw = 1f, len = len, d0 = 0f, partner = new float[lay.m.Length] };
            for (int k = 0; k < lay.m.Length; k++)
            {
                if (float.IsNaN(partnerM[k])) { z.partner[k] = float.NaN; continue; }
                float m = lay.m[k] + f * (partnerM[k] - lay.m[k]);    // the meeting place: the middle, by shares
                z.partner[k] = plus - m;
                z.span = Mathf.Max(z.span, Mathf.Abs(m - lay.m[k]));
            }
            (add[x.index] ??= new List<Ease>(2)).Add(z);
        }

        /// <summary>Each line of <paramref name="a"/> against <paramref name="b"/>
        /// (one frame, one width): its partner's m, or NaN. Edge lines pair
        /// with edge lines; solid yellows with solid yellows and broken
        /// yellows with broken yellows, in order, when both have as many;
        /// white lines on each side of the centre in order from that side's
        /// edge, so the outer lanes keep their place and a lane that ends or
        /// begins is the one beside the centre.</summary>
        static float[] RelayPairs(Layout a, Layout b)
        {
            var r = new float[a.m.Length];
            for (int k = 0; k < r.Length; k++) r[k] = float.NaN;
            float ca = YellowMid(a), cb = YellowMid(b);
            for (byte kind = 0; kind <= KWhiteDash; kind++)
            {
                if (kind == KWhiteDash)
                {
                    for (int sideG = 0; sideG < 2; sideG++)
                    {
                        var la = new List<int>(); var lb = new List<int>();
                        for (int i = 0; i < a.m.Length; i++) if (a.kind[i] == kind && ((a.m[i] < ca) == (sideG == 0))) la.Add(i);
                        for (int i = 0; i < b.m.Length; i++) if (b.kind[i] == kind && ((b.m[i] < cb) == (sideG == 0))) lb.Add(i);
                        // from that side's edge: the plus side (small m) ascending, the minus side descending
                        if (sideG == 1) { la.Reverse(); lb.Reverse(); }
                        for (int q = 0; q < la.Count && q < lb.Count; q++) r[la[q]] = b.m[lb[q]];
                    }
                    continue;
                }
                var xa = new List<int>(); var xb = new List<int>();
                for (int i = 0; i < a.m.Length; i++) if (a.kind[i] == kind) xa.Add(i);
                for (int i = 0; i < b.m.Length; i++) if (b.kind[i] == kind) xb.Add(i);
                if (xa.Count != xb.Count) continue;
                for (int q = 0; q < xa.Count; q++) r[xa[q]] = b.m[xb[q]];
            }
            return r;
        }

        /// <summary>The middle of a layout's yellow lines (m), or its middle.</summary>
        static float YellowMid(Layout l)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < l.m.Length; i++)
                if (l.kind[i] == KYellow || l.kind[i] == KYellowDash) { lo = Mathf.Min(lo, l.m[i]); hi = Mathf.Max(hi, l.m[i]); }
            return lo <= hi ? 0.5f * (lo + hi) : 0.5f * l.W;
        }

        static float SpeedMph(CityMap.Edge e) => e.speedKmh > 0 ? e.speedKmh / 1.609344f : e.link ? 35f : ClassMph[Mathf.Clamp(e.cls, 0, 5)];

        /// <summary>Is a relay moving this edge's lines at s? (The builder
        /// draws such a span line by line, never as the texture's one quad.)</summary>
        public static bool Relayed(CityMap.Edge e, float s)
        {
            var ez = e.lmEase;
            if (ez == null) return false;
            for (int i = 0; i < ez.Length; i++) if (ez[i].relay && Reduction(e, ez[i], s) > 1e-4f) return true;
            return false;
        }

        /// <summary>How far a run goes from a join: this edge, and on through
        /// mitred joints while the next edge is as wide on that side (both
        /// sides for a shift) - up to the next change.</summary>
        static float RoomAlong(CityMap map, System.Func<CityMap.Edge, int, int> partner, CityMap.Edge e, int fromNode, int side, bool bothSides)
        {
            float total = e.length;
            var cur = e; int from = fromNode, sd = side;
            for (int guard = 0; guard < 12; guard++)
            {
                int far = cur.a == from ? cur.b : cur.a;
                int pi = partner(cur, far);
                if (pi < 0) break;
                var p = map.edges[pi];
                if (p == cur || p.a == p.b) break;
                int sideT = cur.a == from ? sd : -sd;
                int pSide = p.a == far ? sideT : -sideT;
                float c1 = sd > 0 ? cur.lmPlus : cur.lmMinus, p1 = pSide > 0 ? p.lmPlus : p.lmMinus;
                if (Mathf.Abs(c1 - p1) > 0.05f) break;
                if (bothSides)
                {
                    float c2 = sd > 0 ? cur.lmMinus : cur.lmPlus, p2 = pSide > 0 ? p.lmMinus : p.lmPlus;
                    if (Mathf.Abs(c2 - p2) > 0.05f) break;
                }
                total += p.length;
                cur = p; from = far; sd = pSide;
            }
            return total;
        }
        /// <summary>MUTCD shifting-taper rate (metres along per metre across):
        /// S^2/60 to 40 mph, S from 45.</summary>
        static float ShiftRate(CityMap.Edge e)
        {
            float mph = e.speedKmh > 0 ? e.speedKmh / 1.609344f : e.link ? 35f : ClassMph[Mathf.Clamp(e.cls, 0, 5)];
            return Mathf.Max(10f, mph <= 40f ? mph * mph / 60f : mph);
        }

        static void PropagateShift(CityMap map, List<Ease>[] add, System.Func<CityMap.Edge, int, int> partner,
                                   CityMap.Edge e, int fromNode, float shift, float len, float d0, int depth)
        {
            if (depth == 0) len = Mathf.Max(0.5f, Mathf.Min(len, RoomAlong(map, partner, e, fromNode, 1, true)));
            (add[e.index] ??= new List<Ease>(2)).Add(new Ease { shift = true, fromA = e.a == fromNode, dw = shift, len = len, d0 = d0 });
            if (d0 + e.length >= len || depth >= 12) return;
            int far = e.a == fromNode ? e.b : e.a;
            int pi = partner(e, far);
            if (pi < 0) return;
            var p = map.edges[pi];
            if (p == e || p.a == p.b) return;
            // the shift in p's own frame
            float ps = ((e.a == fromNode) == (p.a == far)) ? shift : -shift;
            PropagateShift(map, add, partner, p, far, ps, len, d0 + e.length, depth + 1);
        }
        /// <summary>For the audit: joins eased, of them on the class default
        /// length (no TAPR record for that side), and steps left (none: a
        /// join always eases).</summary>
        public static int EasedJoins, EasedDefault, EaseSteps;

        static void Propagate(CityMap map, List<Ease>[] add, System.Func<CityMap.Edge, int, int> partner,
                              CityMap.Edge e, int fromNode, int side, float dw, float len, float d0, CityMap.Edge narrow, bool flip, int depth,
                              float dwOther = 0f, byte twoRole = 0)
        {
            // never longer than the run it eases: the next change along owns the
            // ribbon from there (a default length ran past a 35 m piece into the
            // next join, whose own step it then did not see)
            if (depth == 0) len = Mathf.Max(0.5f, Mathf.Min(len, RoomAlong(map, partner, e, fromNode, side, false)));
            var z = new Ease { side = (sbyte)side, fromA = e.a == fromNode, dw = dw, len = len, d0 = d0, narrow = narrow.profile, narrowLay = LayoutOf(narrow), narrowFlip = flip, dwOther = dwOther, twoRole = twoRole };
            var lay = LayoutOf(e);
            z.partner = new float[lay.m.Length];
            for (int k = 0; k < lay.m.Length; k++) z.partner[k] = PartnerLat(e, lay, k, z);
            (add[e.index] ??= new List<Ease>(2)).Add(z);
            if (d0 + e.length >= len || depth >= 12) return;
            int far = e.a == fromNode ? e.b : e.a;
            int pi = partner(e, far);
            if (pi < 0) return;
            var p = map.edges[pi];
            if (p == e || p.a == p.b) return;
            // the side in the travel frame (away from the join), then in p's own
            int sideT = e.a == fromNode ? side : -side;
            int pSide = p.a == far ? sideT : -sideT;
            // only on through the same run: p must be as wide on that side as e
            float extE = side > 0 ? e.lmPlus : e.lmMinus, extP = pSide > 0 ? p.lmPlus : p.lmMinus;
            if (Mathf.Abs(extE - extP) > 0.05f) return;
            bool pFlip = flip ^ ((e.a == fromNode) != (p.a == far));
            Propagate(map, add, partner, p, far, pSide, dw, len, d0 + e.length, narrow, pFlip, depth + 1, dwOther, twoRole);
        }

        /// <summary>A taper's length: TAPR's MUTCD length where the exporter
        /// recorded this change on this side (a zero one - "full width at the
        /// node" - is a junction mouth's, and at a MITRED join that would be
        /// a step, so the class default eases it instead); otherwise the
        /// default: L = W S^2 / 60 to 40 mph, W S from 45 (MUTCD 6C-3), floors
        /// street 15, arterial 30, freeway 90 m.</summary>
        static float LengthFor(CityMap.Edge w, int node, int side, float dw, out bool fromTapr)
        {
            fromTapr = false;
            if (taprAt != null && taprAt.TryGetValue(TaprKey(w.index, w.a == node ? 0 : 1, side > 0 ? 0 : 1), out int ti))
            {
                var t = taprMap.tapr[ti];
                int tSide = t.side == 0 ? 1 : -1;
                if (tSide == side && t.len >= 0.5f) { fromTapr = true; return t.len; }
                // "full width at the node" (a junction mouth) that the builder
                // mitres - a lane that opens at a merge, beside the branch
                // that brings it: the shortest taper the class allows
                if (tSide == side && (t.flags & 2) != 0) return FloorOf(w);
            }
            float mph = w.speedKmh > 0 ? w.speedKmh / 1.609344f : w.link ? 35f : ClassMph[Mathf.Clamp(w.cls, 0, 5)];
            float rate = mph <= 40f ? mph * mph / 60f : mph;
            return Mathf.Max(FloorOf(w), dw * rate);
        }
        static readonly float[] ClassMph = { 25f, 35f, 40f, 45f, 55f, 65f };
        /// <summary>The taper floors (plan A4 WP-10 item 6): street 15,
        /// arterial 30, freeway 90 m.</summary>
        static float FloorOf(CityMap.Edge w) => (w.cls >= 5 || (w.cls == 4 && w.oneway && !w.link)) ? 90f : (w.link || w.cls >= 2) ? 30f : 15f;

        /// <summary>Where along an edge its tapers start and end (arc
        /// positions), for the builder's sections (every taper end is a
        /// section, and a taper is sectioned densely enough that the eased edge
        /// is faceted within 2 cm).</summary>
        public static void TaperSamples(CityMap.Edge e, float sMin, float sMax, List<float> into)
        {
            var ez = e.lmEase;
            if (ez == null) return;
            foreach (var z in ez)
            {
                // the full-width end, in edge s
                float sEnd = z.fromA ? z.len - z.d0 : e.length - (z.len - z.d0);
                float sJoin = z.fromA ? -z.d0 : e.length + z.d0;
                if (sEnd > sMin + 0.3f && sEnd < sMax - 0.3f) into.Add(sEnd);
                // sagitta of the eased edge: max curvature 6 dw / L^2, chord c
                // gives c^2 k / 8 <= 2 cm
                float k = 6f * (z.relay ? z.span : Mathf.Abs(z.dw)) / (z.len * z.len);
                float step = k > 1e-6f ? Mathf.Sqrt(8f * 0.02f / k) : z.len;
                int n = Mathf.Max(1, Mathf.CeilToInt(z.len / Mathf.Max(0.5f, step)));
                for (int i = 1; i < n; i++)
                {
                    float d = z.len * i / n;
                    float sv = z.fromA ? d - z.d0 : e.length - (d - z.d0);
                    if (sv > sMin + 0.3f && sv < sMax - 0.3f) into.Add(sv);
                }
                if (sJoin > sMin + 0.3f && sJoin < sMax - 0.3f) into.Add(sJoin);
            }
        }

        // ================================================================
        //  Paint: the profile's lines, and where each one is drawn
        // ================================================================

        public const byte KEdgeP = 0, KEdgeM = 1, KYellow = 2, KYellowDash = 3, KWhiteDash = 4;
        /// <summary>The narrowest half line (12 cm lines); a layout's own is
        /// <see cref="Layout.half"/> (Q3: at least two texels).</summary>
        public const float PaintHalf = 0.06f;

        /// <summary>One profile's painted lines as its texture carries them -
        /// or, for an EDGE (<see cref="LayoutOf(CityMap.Edge)"/>, roads pass
        /// L2), the lines its line set paints on that profile: m from the
        /// painter's LEFT edge (u = m / W; the left of travel, the ribbon's
        /// plus side), kind, and the texels each one is drawn from (x0..x1 of
        /// texW). An edge line is drawn from a column of its profile's texture
        /// that carries the same paint: <see cref="srcM"/> (= m where the
        /// texture has that very line), or - a broken line whose texture has no
        /// broken line of that colour - from the solid column, cut into 10 ft
        /// dashes by the builder (<see cref="synth"/>).</summary>
        public sealed class Layout
        {
            public float W; public int texW;
            public float[] m; public byte[] kind; public int[] x0, x1;
            public bool twoWay;
            /// <summary>Half a line's width on this profile (Q3), and a
            /// two-way centre yellow's (<see cref="RoadProfiles.YellowHalfOf"/>).</summary>
            public float half, yHalf;
            /// <summary>Where each line's paint is in the texture (m), and
            /// whether it is a broken line cut from a solid column.</summary>
            public float[] srcM; public bool[] synth;
            /// <summary>The profile texture's own layout (this one for a
            /// texture layout), and true when this IS it: the texture's lines,
            /// drawn as one quad where the sections allow.</summary>
            public Layout tex; public bool isDefault;
            /// <summary>Texture layouts: the widest paint-free texel run
            /// (pavement for an edge layout's strips).</summary>
            public int bandLo, bandHi;
            internal Layout flippedCache;
        }
        static Layout[] layouts;

        public static Layout LayoutOf(int profile)
        {
            if (layouts == null) layouts = new Layout[RoadProfiles.Count];
            var l = layouts[profile];
            if (l != null) return l;
            var pr = RoadProfiles.All[profile];
            var ms = new List<float>(); var ks = new List<byte>();
            RoadProfiles.PaintLines(pr, ms, ks);
            l = Build(pr, ms, ks, null);
            l.tex = l; l.isDefault = true;
            // the widest paint-free run of texels: an edge layout's pavement
            int bestLo = 0, bestHi = -1, from = 0;
            for (int j = 0; j <= l.m.Length; j++)
            {
                int to = j < l.m.Length ? l.x0[j] - 1 : l.texW - 1;
                if (to - from > bestHi - bestLo) { bestLo = from; bestHi = to; }
                if (j < l.m.Length) from = l.x1[j] + 1;
            }
            l.bandLo = bestLo; l.bandHi = Mathf.Max(bestLo, bestHi);
            layouts[profile] = l;
            return l;
        }

        /// <summary>A layout from (m, kind) lines, sorted left to right, with
        /// each line's texels; tex null: the texture's own (src = m).</summary>
        static Layout Build(RoadProfiles.Profile pr, List<float> ms, List<byte> ks, Layout tex)
        {
            var idx = new List<int>(); for (int i = 0; i < ms.Count; i++) idx.Add(i);
            idx.Sort((a, b) => ms[a].CompareTo(ms[b]));
            var l = new Layout { W = pr.Width, texW = RoadProfiles.TexWidthOf(pr), twoWay = !pr.oneway, half = RoadProfiles.PaintHalfOf(pr), yHalf = RoadProfiles.YellowHalfOf(pr), tex = tex };
            int n = idx.Count;
            l.m = new float[n]; l.kind = new byte[n]; l.x0 = new int[n]; l.x1 = new int[n];
            l.srcM = new float[n]; l.synth = new bool[n];
            for (int j = 0; j < n; j++)
            {
                float m = ms[idx[j]];
                l.m[j] = m; l.kind[j] = ks[idx[j]];
                l.srcM[j] = m;
                if (tex != null) SourceOf(tex, m, l.kind[j], out l.srcM[j], out l.synth[j]);
                TexelsOf(l, l.srcM[j], SourceKind(tex ?? l, l.srcM[j], l.kind[j]), out l.x0[j], out l.x1[j]);
            }
            return l;
        }

        /// <summary>The texels a line at m covers in its texture (the
        /// painter's test: the texel centre within half of m; a two-way
        /// centre yellow's half is <see cref="Layout.yHalf"/>).</summary>
        public static void TexelsOf(Layout l, float m, byte kind, out int lo, out int hi)
        {
            float half = l.twoWay && (kind == KYellow || kind == KYellowDash) ? l.yHalf : l.half;
            lo = int.MaxValue; hi = int.MinValue;
            int c = Mathf.FloorToInt(m / l.W * l.texW);
            for (int x = c - 4; x <= c + 4; x++)
            {
                if (x < 0 || x >= l.texW) continue;
                float mx = (x + 0.5f) / l.texW * l.W;
                if (Mathf.Abs(mx - m) < half) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            }
            if (lo > hi) { lo = hi = Mathf.Clamp(c, 0, l.texW - 1); }
        }

        /// <summary>Where the texture carries the paint of a line of this
        /// kind at m: that very line if the texture has it there; else a
        /// column of the same paint (a solid white is the right edge line's,
        /// a solid yellow the double yellow's or a one-way's left edge); a
        /// broken line with no broken column of its colour is cut from the
        /// solid one (synth).</summary>
        static void SourceOf(Layout tex, float m, byte kind, out float src, out bool synth)
        {
            synth = false;
            for (int i = 0; i < tex.m.Length; i++)
                if (tex.kind[i] == kind && Mathf.Abs(tex.m[i] - m) < 1e-3f) { src = tex.m[i]; return; }
            byte want = kind;
            if (kind == KEdgeP && tex.twoWay) want = KEdgeM;                 // white solid
            if (kind == KEdgeM) want = KEdgeM;
            int j = FindKind(tex, want);
            if (j < 0 && kind == KYellowDash) { j = FindKind(tex, KYellow); synth = j >= 0; }
            if (j < 0 && kind == KWhiteDash) { j = FindKind(tex, KEdgeM); synth = j >= 0; }
            if (j < 0 && kind == KYellow && !tex.twoWay) j = FindKind(tex, KEdgeP); // a one-way's left edge is yellow
            src = j >= 0 ? tex.m[j] : m;
        }
        static int FindKind(Layout l, byte kind) { for (int i = 0; i < l.kind.Length; i++) if (l.kind[i] == kind) return i; return -1; }
        /// <summary>The kind of the texture line at m (the paint a source
        /// column carries), or the drawn kind where the texture has none.</summary>
        static byte SourceKind(Layout tex, float m, byte kind)
        {
            for (int i = 0; i < tex.m.Length; i++) if (Mathf.Abs(tex.m[i] - m) < 1e-3f) return tex.kind[i];
            return kind;
        }

        /// <summary>
        /// THE EDGE'S OWN LINES (roads pass L2): its line set (section LSET:
        /// lanes each way, the centre, marked or not) laid out on its profile
        /// by <see cref="RoadProfiles.LinesFor"/>, cached by (profile, nF, nB,
        /// centre, marked). The profile's texture layout itself when the set is
        /// the profile's default - drawn exactly as before.
        /// </summary>
        public static Layout LayoutOf(CityMap.Edge e)
        {
            var tex = LayoutOf(e.profile);
            if (!e.hasLset) return tex;
            var pr = RoadProfiles.All[e.profile];
            RoadProfiles.DefaultSplit(pr, out int dF, out int dB, out int dC);
            bool marked = (e.lsFlags & 1) != 0;
            if (marked && e.lsNF == dF && e.lsNB == dB && e.lsCentre == dC) return tex;
            int key = (e.profile << 24) | (e.lsNF << 16) | (e.lsNB << 8) | (e.lsCentre << 1) | (marked ? 1 : 0);
            edgeLayouts ??= new Dictionary<int, Layout>();
            if (edgeLayouts.TryGetValue(key, out var l)) return l;
            var ms = new List<float>(); var ks = new List<byte>();
            RoadProfiles.LinesFor(pr, e.lsNF, e.lsNB, e.lsCentre, marked, ms, ks);
            l = Build(pr, ms, ks, tex);
            // the same lines as the texture after all (a one-way's split is its own)
            l.isDefault = SameLines(l, tex);
            if (l.isDefault) l = tex;
            edgeLayouts[key] = l;
            return l;
        }
        static Dictionary<int, Layout> edgeLayouts;
        static bool SameLines(Layout a, Layout b)
        {
            if (a.m.Length != b.m.Length) return false;
            for (int i = 0; i < a.m.Length; i++) if (a.kind[i] != b.kind[i] || Mathf.Abs(a.m[i] - b.m[i]) > 1e-4f) return false;
            return true;
        }

        /// <summary>The lateral (+ left of a->b, full extents: no taper) of
        /// the middle between the two directions' lanes on a two-way edge -
        /// the double yellow, or the centre of the TWLTL - from its line set
        /// (the lanes' centre, lmOff, on a symmetric one).</summary>
        public static float DividerLat(CityMap.Edge e)
        {
            float plus = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
            int nB; int centre;
            if (e.hasLset) { nB = e.lsNB; centre = e.lsCentre; }
            else { RoadProfiles.DefaultSplit(RoadProfiles.All[e.profile], out _, out nB, out centre); }
            return plus - e.shl - (nB + (centre == 2 ? 0.5f : 0f)) * RoadProfiles.LaneM;
        }

        /// <summary>A layout seen from the other direction of travel: m from
        /// the other edge, the left and right edge lines swapped.</summary>
        static Layout Flipped(Layout l)
        {
            if (l.flippedCache != null) return l.flippedCache;
            int n = l.m.Length;
            var f = new Layout { W = l.W, texW = l.texW, twoWay = l.twoWay, half = l.half, yHalf = l.yHalf, tex = l.tex, isDefault = l.isDefault,
                                 bandLo = l.bandLo, bandHi = l.bandHi,
                                 m = new float[n], kind = new byte[n], x0 = new int[n], x1 = new int[n], srcM = new float[n], synth = new bool[n] };
            for (int j = 0; j < n; j++)
            {
                int i = n - 1 - j;
                f.m[j] = l.W - l.m[i];
                f.kind[j] = l.kind[i] == KEdgeP ? KEdgeM : l.kind[i] == KEdgeM ? KEdgeP : l.kind[i];
                f.x0[j] = l.texW - 1 - l.x1[i]; f.x1[j] = l.texW - 1 - l.x0[i];
                f.srcM[j] = l.W - l.srcM[i]; f.synth[j] = l.synth[i];
            }
            return l.flippedCache = f;
        }

        /// <summary>A painted line where the model draws it: its index in the
        /// layout and its lateral off the OSM line (+ = left of a->b).</summary>
        public struct LineAt { public int k; public float lat; }

        /// <summary>Every line of the edge's profile that the model draws at s,
        /// in the edge's own frame, ordered by lateral ascending (right edge
        /// first). Unsqueezed, unclipped: the builder trims them to what it
        /// draws.</summary>
        public static void LinesAt(CityMap.Edge e, float s, List<LineAt> into)
        {
            into.Clear();
            var lay = LayoutOf(e);
            int n = lay.m.Length;
            // the active taper each side: the largest reduction there
            int zp = -1, zm = -1; float rp = 0f, rm = 0f;
            var ez = e.lmEase;
            if (ez != null)
                for (int i = 0; i < ez.Length; i++)
                {
                    if (ez[i].shift || ez[i].relay) continue;
                    float r = Reduction(e, ez[i], s);
                    if (r <= 1e-4f) continue;
                    if (ez[i].side > 0) { if (r > rp) { rp = r; zp = i; } } else if (r > rm) { rm = r; zm = i; }
                }
            float plus0 = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
            float sh = ShiftAt(e, s);
            for (int k = n - 1; k >= 0; k--)   // m descending = lateral ascending
            {
                float full = plus0 - lay.m[k];
                float lat = full;
                bool ok = true;
                // a relay (L4) first: the line where the two layouts meet ...
                if (ez != null)
                    for (int i = 0; i < ez.Length && ok; i++)
                    {
                        if (!ez[i].relay) continue;
                        float r = Reduction(e, ez[i], s);
                        if (r > 1e-4f) ok &= Shift(e, k, ez[i], r, full, ref lat);
                    }
                // ... then a width taper moves it from THERE toward its partner
                // (from the line's own place where no relay runs: as before)
                float from = lat;
                // of a two-sided pair (Q4) only the lead moves the lines between the edges
                bool interior = lay.kind[k] != KEdgeP && lay.kind[k] != KEdgeM;
                if (ok && zp >= 0 && !(interior && ez[zp].twoRole == 2)) ok &= Shift(e, k, ez[zp], rp, from, ref lat);
                if (ok && zm >= 0 && !(interior && ez[zm].twoRole == 2)) ok &= Shift(e, k, ez[zm], rm, from, ref lat);
                if (ok) into.Add(new LineAt { k = k, lat = lat + sh });   // the whole layout rides the median taper's shift
            }
        }

        /// <summary>Move line k by the taper z at reduction r from
        /// <paramref name="from"/> toward its partner in the narrow layout (or,
        /// a relay, the meeting place); false when it has none (it ends where
        /// the taper reaches full width).</summary>
        static bool Shift(CityMap.Edge e, int k, in Ease z, float r, float from, ref float lat)
        {
            float target = z.partner != null && k < z.partner.Length ? z.partner[k] : float.NaN;
            if (float.IsNaN(target)) return false;
            lat += (target - from) * (r / z.dw);
            return true;
        }

        /// <summary>Line k's partner in the narrow layout, as a lateral in the
        /// wide edge's frame at the join (the fixed edge in common, the moving
        /// one in by dw), or NaN. Edge lines pair with edge lines; the centre
        /// (yellow) group pairs in order from the fixed edge; the white lines
        /// on the fixed side of the centre pair in order from the fixed edge,
        /// those beyond it in order from the moving edge.</summary>
        static float PartnerLat(CityMap.Edge e, Layout lay, int k, in Ease z)
        {
            var narL = z.narrowLay ?? LayoutOf(Mathf.Clamp(z.narrow, 0, RoadProfiles.Count - 1));
            var nar = z.narrowFlip ? Flipped(narL) : narL;
            float plus = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
            float minus = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmMinus : e.width * 0.5f;
            // the narrow ribbon at the join, laterals in the wide frame
            // anchored on the FIXED side (the narrow layout's width may differ
            // from W - dw): its left (plus) edge in the wide frame
            float nLeft = z.side > 0 ? -minus + nar.W : plus;
            float nLat(float m) => nLeft - m;
            byte kind = lay.kind[k];
            // the MOVING side's edge line rides its edge (its shoulder in from
            // it): the fixed side need not be the same on every edge a taper
            // runs on through (another change on that side at a joint), the
            // moving edge is (the taper runs on only while it is)
            if (kind == KEdgeP && z.side > 0) { int j = Find(nar, KEdgeP, 0); return j < 0 ? float.NaN : (plus - z.dw) - nar.m[j]; }
            if (kind == KEdgeM && z.side < 0) { int j = Find(nar, KEdgeM, 0); return j < 0 ? float.NaN : -(minus - z.dw) + (nar.W - nar.m[j]); }
            if (kind == KEdgeP) { int j = Find(nar, KEdgeP, 0); return j < 0 ? float.NaN : nLat(nar.m[j]); }
            if (kind == KEdgeM) { int j = Find(nar, KEdgeM, 0); return j < 0 ? float.NaN : nLat(nar.m[j]); }
            if (z.twoRole != 0)
            {
                // BOTH EDGES (Q4): the narrow layout sits in by each side's dw,
                // its centre on the wide one's; lines pair by kind from the
                // centre outward (the lanes each direction adds are its OUTER
                // ones). The second ease of the pair moves nothing between the edges.
                float dwPlus = z.side > 0 ? z.dw : z.dwOther;
                float t2 = PairFromCentre(lay, k, nar, plus - dwPlus);
                return z.twoRole == 2 && !float.IsNaN(t2) ? plus - lay.m[k] : t2;
            }
            // distance from the FIXED edge, for the wide line and the narrow ones
            bool fixedPlus = z.side < 0;
            float WideD(int i) => fixedPlus ? lay.m[i] : lay.W - lay.m[i];
            float NarD(int i) => fixedPlus ? nar.m[i] : nar.W - nar.m[i];
            float wideCentre = CentreD(lay, fixedPlus), narCentre = CentreD(nar, fixedPlus);
            bool yellow = kind == KYellow || kind == KYellowDash;
            // the group: same kind, and for white lines the same side of the centre
            bool wBeyond = !yellow && lay.twoWay && WideD(k) > wideCentre;
            var wl = new List<int>(); var nl = new List<int>();
            for (int i = 0; i < lay.m.Length; i++)
                if (lay.kind[i] == kind && (yellow || !lay.twoWay || (WideD(i) > wideCentre) == wBeyond)) wl.Add(i);
            for (int i = 0; i < nar.m.Length; i++)
                if (nar.kind[i] == kind && (yellow || !nar.twoWay || (NarD(i) > narCentre) == wBeyond)) nl.Add(i);
            // order: from the fixed edge, or (whites beyond the centre) from the moving edge
            if (wBeyond) { wl.Sort((a, b) => WideD(b).CompareTo(WideD(a))); nl.Sort((a, b) => NarD(b).CompareTo(NarD(a))); }
            else { wl.Sort((a, b) => WideD(a).CompareTo(WideD(b))); nl.Sort((a, b) => NarD(a).CompareTo(NarD(b))); }
            int rank = wl.IndexOf(k);
            if (rank < 0 || rank >= nl.Count) return float.NaN;
            return nLat(nar.m[nl[rank]]);
        }

        /// <summary>Line k of <paramref name="lay"/> against the narrow layout
        /// whose left edge is at <paramref name="nLeft"/> (the wide frame): its
        /// partner's lateral, or NaN. Yellows of a kind pair in order when both
        /// have as many; white lines on each side of the centre pair by their
        /// order from it.</summary>
        static float PairFromCentre(Layout lay, int k, Layout nar, float nLeft)
        {
            byte kind = lay.kind[k];
            if (kind == KYellow || kind == KYellowDash)
            {
                var a = new List<int>(); var b = new List<int>();
                for (int i = 0; i < lay.m.Length; i++) if (lay.kind[i] == kind) a.Add(i);
                for (int i = 0; i < nar.m.Length; i++) if (nar.kind[i] == kind) b.Add(i);
                int r = a.IndexOf(k);
                return a.Count != b.Count || r < 0 ? float.NaN : nLeft - nar.m[b[r]];
            }
            float cw = YellowMid(lay), cn = YellowMid(nar);
            bool plusSide = lay.m[k] < cw;
            var wl = new List<int>(); var nl = new List<int>();
            for (int i = 0; i < lay.m.Length; i++) if (lay.kind[i] == kind && (lay.m[i] < cw) == plusSide) wl.Add(i);
            for (int i = 0; i < nar.m.Length; i++) if (nar.kind[i] == kind && (nar.m[i] < cn) == plusSide) nl.Add(i);
            wl.Sort((x, y) => Mathf.Abs(lay.m[x] - cw).CompareTo(Mathf.Abs(lay.m[y] - cw)));
            nl.Sort((x, y) => Mathf.Abs(nar.m[x] - cn).CompareTo(Mathf.Abs(nar.m[y] - cn)));
            int rank = wl.IndexOf(k);
            return rank < 0 || rank >= nl.Count ? float.NaN : nLeft - nar.m[nl[rank]];
        }

        static int Find(Layout l, byte kind, int nth)
        {
            for (int i = 0; i < l.kind.Length; i++) if (l.kind[i] == kind && nth-- == 0) return i;
            return -1;
        }

        /// <summary>Distance from the fixed edge to the centre (yellow) group's
        /// middle; a one-way layout's is past its far edge.</summary>
        static float CentreD(Layout l, bool fixedPlus)
        {
            if (!l.twoWay) return float.MaxValue;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < l.m.Length; i++)
                if (l.kind[i] == KYellow || l.kind[i] == KYellowDash)
                {
                    float d = fixedPlus ? l.m[i] : l.W - l.m[i];
                    lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                }
            return lo <= hi ? 0.5f * (lo + hi) : l.W * 0.5f;
        }
    }
}
