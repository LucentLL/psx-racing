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
        public struct Ease
        {
            public sbyte side; public bool fromA, narrowFlip, shift, relay; public float dw, len, d0, span, dwOther; public byte twoRole; public int narrow; public float[] partner; public Layout narrowLay;
            /// <summary>Roads pass L5: the join this ease was made for
            /// (<see cref="EaseTag"/>; every edge it runs on through carries
            /// the same tag), and CENTRE ONLY - a merge zone's host now carries
            /// the lane at full width (<see cref="CityMeshes.MergeZone"/>), so
            /// the pavement ignores this ease while the lanes' centre (traffic,
            /// the AI, the race line) still eases over it as before.</summary>
            public long tag; public bool centreOnly;
            /// <summary>Roads pass L5, THE AUX LANE: a merge zone WIDENS the
            /// host on <see cref="side"/> (never a reduction). x = the host-chain
            /// distance from the zone's node, + into the zone arm (auxArm +1),
            /// - into the other arm (auxArm -1): full width auxW over
            /// [auxXF, auxD] (auxD = the nose N; the last auxLs before N eases
            /// from auxWN, the ramp's own outer edge there), a smoothstep taper
            /// to 0 over the auxLt before auxXF, nothing past N.</summary>
            public bool aux; public sbyte auxArm; public float auxD, auxWN, auxW, auxLs, auxXF, auxLt; public int auxZone;
            /// <summary>The medians pass round 2 (2026-11-02), A REFUGE
            /// ISLAND'S PROFILE: the value is sampled, not eased - prof[k] at
            /// k * profDs from this edge's near end (chain distance profD0 =
            /// d0). On a shift ease it is the shift (edge frame); a WIDEN ease
            /// widens the PAVEMENT only on <see cref="side"/> (like an aux
            /// lane: the lanes, the lines and traffic never see it) - the
            /// centre lane's space beside the island.</summary>
            public float[] prof; public float profD0, profDs; public bool widen;
        }

        /// <summary>The tag of the eases one mitred join makes on its wide
        /// arm W on a side (W's frame).</summary>
        public static long EaseTag(int node, int wide, int side) => ((long)(node + 1) << 33) | ((long)wide << 1) | (side > 0 ? 1L : 0L);
        static Dictionary<long, List<int>> taggedEdges;

        /// <summary>Roads pass L5: make every ease of a join's wide-arm side
        /// CENTRE ONLY (the pavement no longer narrows there; the lanes'
        /// centre still eases). Returns how many eases it touched.</summary>
        public static int CentreOnly(CityMap map, long tag, bool on = true)
        {
            if (taggedEdges == null || !taggedEdges.TryGetValue(tag, out var list)) return 0;
            int n = 0;
            foreach (int ei in list)
            {
                var ez = map.edges[ei].lmEase;
                if (ez == null) continue;
                for (int i = 0; i < ez.Length; i++) if (ez[i].tag == tag && !ez[i].aux) { ez[i].centreOnly = on; n++; }
            }
            return n;
        }

        /// <summary>The edges a join's tag runs on (null: none).</summary>
        public static List<int> EdgesOfTag(long tag) => taggedEdges != null && taggedEdges.TryGetValue(tag, out var l) ? l : null;

        /// <summary>Roads pass L5: one aux-lane ease added to an edge.</summary>
        public static void AddAux(CityMap.Edge e, Ease z)
        {
            z.aux = true;
            var ez = e.lmEase;
            if (ez == null) { e.lmEase = new[] { z }; return; }
            var n = new Ease[ez.Length + 1];
            System.Array.Copy(ez, n, ez.Length);
            n[ez.Length] = z;
            e.lmEase = n;
        }

        /// <summary>The aux widening of one aux ease at arc s.</summary>
        public static float AuxAt(CityMap.Edge e, in Ease z, float s)
        {
            float d = z.d0 + (z.fromA ? s : e.length - s);
            return AuxProfile(z, z.auxArm * d);
        }

        /// <summary>The aux widening at host-chain distance x (see
        /// <see cref="Ease.aux"/>).</summary>
        public static float AuxProfile(in Ease z, float x)
        {
            if (x > z.auxD + 1e-3f) return 0f;
            // at N itself always the ramp's own outer edge (the shared vertex)
            if (x >= z.auxD - 1e-3f) return z.auxWN;
            if (x >= z.auxXF) return AuxFull(z, x);
            if (z.auxLt > 1e-3f && x > z.auxXF - z.auxLt)
            {
                // the taper starts from whatever the full width is where it
                // starts (the ramp's own edge when it starts at N)
                float w0 = z.auxXF >= z.auxD - 1e-3f ? z.auxWN : AuxFull(z, z.auxXF);
                return w0 * Smooth((x - (z.auxXF - z.auxLt)) / z.auxLt);
            }
            return 0f;
        }
        static float AuxFull(in Ease z, float x) =>
            z.auxLs > 1e-3f && x > z.auxD - z.auxLs ? z.auxW + (z.auxWN - z.auxW) * Smooth((x - (z.auxD - z.auxLs)) / z.auxLs) : z.auxW;

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
        public static void Extents(CityMap.Edge e, float s, out float eMinus, out float ePlus) => Ext(e, s, true, out eMinus, out ePlus);

        /// <summary>pave: the PAVEMENT (aux lanes in, centre-only eases out);
        /// otherwise the LANES' frame the centre is read from (aux lanes out,
        /// centre-only eases in: traffic drives the through lanes as before).</summary>
        static void Ext(CityMap.Edge e, float s, bool pave, out float eMinus, out float ePlus)
        {
            ePlus = e.lmPlus; eMinus = e.lmMinus;
            if (ePlus == 0f && eMinus == 0f) { ePlus = eMinus = e.width * 0.5f; return; }
            var ez = e.lmEase;
            if (ez == null) return;
            float rp = 0f, rm = 0f, sh = 0f, ap = 0f, am = 0f;
            for (int i = 0; i < ez.Length; i++)
            {
                if (ez[i].relay) continue;
                if (ez[i].aux || ez[i].widen)
                {
                    if (!pave) continue;
                    float w = ez[i].widen ? Reduction(e, ez[i], s) : AuxAt(e, ez[i], s);
                    if (ez[i].side > 0) { if (w > ap) ap = w; } else if (w > am) am = w;
                    continue;
                }
                if (pave && ez[i].centreOnly) continue;
                float r = Reduction(e, ez[i], s);
                if (ez[i].shift) { sh += r; continue; }
                if (ez[i].side > 0) { if (r > rp) rp = r; } else if (r > rm) rm = r;
            }
            ePlus -= rp - sh; eMinus -= rm + sh;
            ePlus += ap; eMinus += am;
        }

        /// <summary>The pavement's extents with no aux lane (roads pass L5):
        /// the host's own edge, where a merge zone's lane is added.</summary>
        public static void ExtentsNoAux(CityMap.Edge e, float s, out float eMinus, out float ePlus)
        {
            Ext(e, s, true, out eMinus, out ePlus);
            var ez = e.lmEase;
            if (ez == null) return;
            float ap = 0f, am = 0f;
            for (int i = 0; i < ez.Length; i++)
            {
                if (!ez[i].aux) continue;
                float w = AuxAt(e, ez[i], s);
                if (ez[i].side > 0) { if (w > ap) ap = w; } else if (w > am) am = w;
            }
            ePlus -= ap; eMinus -= am;
        }

        /// <summary>The pavement as L4 drew it (roads pass L5's BEFORE, for the
        /// MERGE report): no aux lane, every mouth ease narrowing it.</summary>
        public static void ExtentsBefore(CityMap.Edge e, float s, out float eMinus, out float ePlus)
        {
            Ext(e, s, false, out eMinus, out ePlus);
        }

        /// <summary>How far the edge line on a side (+1 plus, -1 minus) moves
        /// out with an aux lane at s: the lane's own width over the full-width
        /// run (the shoulder beside it is the ramp's at N, the host's past
        /// its ease), the eased edge through the taper. The outer edge line
        /// rides the aux lane; the line between it and the through lanes is
        /// A8's (L6).</summary>
        public static float AuxLineShift(CityMap.Edge e, float s, int side)
        {
            var ez = e.lmEase;
            if (ez == null) return 0f;
            float a = 0f;
            for (int i = 0; i < ez.Length; i++)
            {
                if (!ez[i].aux || (ez[i].side > 0) != (side > 0)) continue;
                float x = ez[i].auxArm * (ez[i].d0 + (ez[i].fromA ? s : e.length - s));
                float w = x > ez[i].auxD + 1e-3f ? 0f : x >= ez[i].auxXF ? ez[i].auxW : AuxProfile(ez[i], x);
                if (w > a) a = w;
            }
            return a;
        }

        /// <summary>The aux widening on a side (+1 plus, -1 minus) at s.</summary>
        public static float AuxWidth(CityMap.Edge e, float s, int side)
        {
            var ez = e.lmEase;
            if (ez == null) return 0f;
            float a = 0f;
            for (int i = 0; i < ez.Length; i++)
                if (ez[i].aux && (ez[i].side > 0) == (side > 0)) a = Mathf.Max(a, AuxAt(e, ez[i], s));
            return a;
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
            if (z.prof != null) return ProfAt(z, d);
            if (d >= z.len) return 0f;
            return z.dw * (1f - Smooth(Mathf.Max(0f, d) / z.len));
        }

        /// <summary>A profile ease's value at chain distance d (linear between
        /// its samples, held past its ends).</summary>
        static float ProfAt(in Ease z, float d)
        {
            var p = z.prof;
            float x = (d - z.profD0) / z.profDs;
            if (x <= 0f) return p[0];
            int n = p.Length;
            if (x >= n - 1) return p[n - 1];
            int i = (int)x;
            return p[i] + (p[i + 1] - p[i]) * (x - i);
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
            Ext(e, s, false, out float eM, out float eP);
            return 0.5f * ((eP - e.shl) - (eM - e.shr));
        }

        /// <summary>The point on the lanes' centre at s (world plan).</summary>
        public static Vector2 LanePoint(CityMap.Edge e, float s)
        {
            // roads pass L5 (plan A9): a ramp between its nose and its node
            // drives the host's aux lane (the ramp's own ribbon ends at N)
            if (CityMeshes.ZoneLanePoint(e, s, out var zp)) return zp;
            return OwnLanePoint(e, s);
        }

        /// <summary>The point on the lanes' centre at s, the edge's own (no
        /// merge zone mapping).</summary>
        public static Vector2 OwnLanePoint(CityMap.Edge e, float s)
        {
            if (RefugeNormal(e, s, out var nL)) return e.PointAt(s) + nL * LaneCentre(e, s);
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
            taggedEdges = new Dictionary<long, List<int>>();
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
            var refuge = RefugeOn ? FindRefuges(map, splitAt, add) : null;
            if (!RefugeOn) { Refuges.Clear(); refugeEdge.Clear(); RefugeTapers = 0; RefugeHeld = 0; RefugeProfiled = 0; RefugeFallback = 0; }
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
                    // A REFUGE ISLAND (the owner, 2026-11-02): the shift fades
                    // out by mid-island, so the island is OSM's own width there
                    if (refuge != null && refuge.TryGetValue(RefugeKey(sp.node, ci), out var rf))
                    {
                        // round 2: the island's own profile drew this carriageway end to end
                        if (rf.profiled) { RefugeTapers++; continue; }
                        float cap = RefugeLen(rf.lc, Mathf.Abs(shift0), rf.extra * 0.5f);
                        if (cap < len) { len = cap; RefugeTapers++; } else RefugeHeld++;
                    }
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

        // ================================================================
        //  Refuge islands (the medians pass, 2026-11-02)
        // ================================================================
        // The owner on Beatties Ford: "curbs blocking turn lanes", a wide
        // grass island where the real one is a 1.5-2 m planted strip. Where
        // a 5-lane road opens into two carriageways for a short island and
        // closes again, each split node's median taper shifted its
        // carriageway out by the undivided road's half width less its own
        // (5.49 m there) over the whole island: RoomAlong let each taper run
        // to the far node, and two smoothsteps from opposite ends sum to one,
        // so the shift was a constant 5.49 m from end to end - an island of
        // 1.9 + 2 x 5.49 = 12.9 m, the road's outer edges bulging by 5 m.
        // Now the taper of a REFUGE carriageway (its chain reaches another
        // split node of the same road within RefugeMaxM, on a road that is
        // not Barriered, both ends without a lane-count mismatch) is capped
        // at half the chain: the shift fades to nothing at mid-island and
        // the island takes OSM's own width there. An island OSM draws
        // narrower than RefugeMinIslandM keeps that much: the cap is solved
        // so the two ends' shifts add up to the shortfall at mid.

        /// <summary>PSX_CITY_REFUGE=0: today's tapers (the A/B). Not readonly:
        /// the medians gate measures both in one run.</summary>
        public static bool RefugeOn = System.Environment.GetEnvironmentVariable("PSX_CITY_REFUGE") != "0";
        public const float RefugeMaxM = 150f, RefugeMinIslandM = 1.2f, RefugeMismatchM = 1f;
        /// <summary>For the audit: carriageway tapers capped, tapers left as
        /// they were (OSM's island too narrow for the cap to help), pairs.</summary>
        public static int RefugeTapers, RefugeHeld;
        /// <summary>Round 2, for the audit: carriageways drawn by the full
        /// profile, and pairs left on round 1's cap (no room for an island).</summary>
        public static int RefugeProfiled, RefugeFallback;
        /// <summary>One carriageway of a refuge island: the split node, the
        /// carriageway edge leaving it into the island, the chain of edges to
        /// the far split node (in travel order away from this node), its
        /// length, the island's width in OSM at mid-chain, and how much wider
        /// than that it is held (round 1's cap). Round 2 (profiled): the
        /// island's frame - the axis (the undivided road's line carried
        /// through, a Hermite curve between the two nodes on their approach
        /// tangents), this carriageway's OUTER side (so: +1 left of the axis
        /// from node to far), the undivided road's outer half width at each
        /// end on that side, the lanes' outward move at mid (4-lane case,
        /// 0 with a centre lane) and its ease length, the island's half width
        /// at mid, the nose set-backs from each end, its taper and tip
        /// radius.</summary>
        public sealed class Refuge
        {
            public int node, far, edge; public int[] chain; public float lc, osmIsland, extra;
            public bool profiled; public int so; public Vector2[] ax; public float[] axS; public float axLen;
            public float uOut1, uOut2, move, moveLen, half, x0s, x0e, ln, rn;
            /// <summary>The undivided road's outer edge at axis arc u (from the axis, outward).</summary>
            public float OuterTarget(float u) => Mathf.Lerp(uOut1, uOut2, axLen > 1e-3f ? Mathf.Clamp01(u / axLen) : 0f);
            /// <summary>The lanes' outward move at axis arc u (4-lane case).</summary>
            public float MoveAt(float u) => move <= 0f ? 0f : move * Smooth(Mathf.Min(u, axLen - u) / Mathf.Max(1f, moveLen));
            /// <summary>The island's design half width at axis arc u (before
            /// the carriageways' own room limits it).</summary>
            public float NoseHalf(float u) => LineModel.NoseHalf(Mathf.Min(u - x0s, axLen - u - x0e), half, ln, rn);
            /// <summary>Axis arc and lateral (outward +) of a world point.</summary>
            public void Local(Vector2 p, out float u, out float lat)
            {
                AxisClosest(ax, axS, p, out u, out var at, out var left);
                lat = so * Vector2.Dot(p - at, left);
            }
        }
        public static readonly List<Refuge> Refuges = new List<Refuge>();
        static long RefugeKey(int node, int edge) => ((long)node << 32) | (uint)edge;

        /// <summary>The taper length that leaves <paramref name="residual"/>
        /// of a <paramref name="shift"/> at mid-chain (Lc/2): Lc/2 for none,
        /// at most Lc (two ends then sum to the full shift everywhere).</summary>
        static float RefugeLen(float lc, float shift, float residual)
        {
            float half = 0.5f * lc;
            if (residual <= 1e-3f || shift <= 1e-3f) return half;
            if (residual >= 0.5f * shift) return lc;
            float lo = half, hi = lc;
            for (int it = 0; it < 24; it++)
            {
                float mid = 0.5f * (lo + hi);
                if (shift * (1f - Smooth(half / mid)) < residual) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>Walk a carriageway from a split node along its own road
        /// (same name, one-way the same way, no link; a side street may join)
        /// to the next split node within RefugeMaxM.</summary>
        static bool RefugeChain(CityMap map, Dictionary<int, Split> splitAt, int ei, int node, List<int> chain, out int far, out float len)
        {
            chain.Clear(); far = -1; len = 0f;
            if (ei < 0 || ei >= map.edges.Length) return false;
            var e = map.edges[ei];
            int from = node;
            for (int g = 0; g < 30; g++)
            {
                if (e.a == e.b) return false;
                chain.Add(e.index);
                len += e.length;
                far = e.a == from ? e.b : e.a;
                if (splitAt.ContainsKey(far)) return far != node && len <= RefugeMaxM;
                if (len > RefugeMaxM) return false;
                bool arrives = e.b == far;
                CityMap.Edge next = null; int n = 0;
                foreach (int i in map.nodeEdges[far])
                {
                    if (i == e.index) continue;
                    var o = map.edges[i];
                    if (o.name != e.name || !o.oneway || o.link || o.a == o.b || (o.a == far) != arrives) continue;
                    next = o; n++;
                }
                if (n != 1) return false;
                e = next; from = far;
            }
            return false;
        }

        /// <summary>A point a distance d along a chain from its node.</summary>
        static Vector2 ChainPoint(CityMap map, int[] chain, int node, float d)
        {
            int from = node;
            for (int k = 0; k < chain.Length; k++)
            {
                var e = map.edges[chain[k]];
                bool fwd = e.a == from;
                if (d <= e.length || k == chain.Length - 1)
                {
                    float s = Mathf.Clamp(fwd ? d : e.length - d, 0f, e.length);
                    return e.PointAt(s);
                }
                d -= e.length; from = fwd ? e.b : e.a;
            }
            return map.nodes[node];
        }

        static float DistToEdge(CityMap.Edge e, Vector2 p)
        {
            float best = float.MaxValue;
            for (int k = 1; k < e.pts.Length; k++)
            {
                Vector2 a = e.pts[k - 1], ab = e.pts[k] - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * u));
            }
            return best;
        }

        /// <summary>The refuge carriageways, keyed (split node, carriageway
        /// edge at it), both ends of every pair: every split node whose two
        /// carriageway chains reach the same other split node within
        /// RefugeMaxM. Round 2: each pair's two carriageways get the island's
        /// own profile (shift + widen eases, <see cref="BuildRefugePair"/>);
        /// a pair with no room for an island keeps round 1's cap.</summary>
        static Dictionary<long, Refuge> FindRefuges(CityMap map, Dictionary<int, Split> splitAt, List<Ease>[] add)
        {
            Refuges.Clear(); refugeEdge.Clear(); RefugeTapers = 0; RefugeHeld = 0; RefugeProfiled = 0; RefugeFallback = 0;
            var res = new Dictionary<long, Refuge>();
            var done = new HashSet<long>();
            var ca = new List<int>(8); var cb = new List<int>(8);
            bool Bad(int i) => i < 0 || i >= map.edges.Length || CityMeshes.Barriered(map.edges[i]);
            float Nose(Split s) => map.edges[s.u].width - map.edges[s.a].width - map.edges[s.b].width;
            foreach (var sp in splitAt.Values)
            {
                if (Bad(sp.u) || Bad(sp.a) || Bad(sp.b)) continue;
                if (!RefugeChain(map, splitAt, sp.a, sp.node, ca, out int fa, out float la)) continue;
                if (!RefugeChain(map, splitAt, sp.b, sp.node, cb, out int fb, out float lb)) continue;
                if (fa != fb) continue;
                var s2 = splitAt[fa];
                if (Bad(s2.u) || Bad(s2.a) || Bad(s2.b)) continue;
                if (Nose(sp) < -RefugeMismatchM || Nose(s2) < -RefugeMismatchM) continue;
                long pk = ((long)Mathf.Min(sp.node, fa) << 32) | (uint)Mathf.Max(sp.node, fa);
                if (!done.Add(pk)) continue;                 // the far end: built with this one
                var pair = new Refuge[2];
                for (int c = 0; c < 2; c++)
                {
                    var mine = c == 0 ? ca : cb; var other = c == 0 ? cb : ca;
                    float lc = c == 0 ? la : lb;
                    var arr = mine.ToArray();
                    var p = ChainPoint(map, arr, sp.node, 0.5f * lc);
                    float sep = float.MaxValue;
                    foreach (int oi in other) sep = Mathf.Min(sep, DistToEdge(map.edges[oi], p));
                    float isl = sep - 0.5f * (map.edges[sp.a].width + map.edges[sp.b].width);
                    pair[c] = new Refuge { node = sp.node, far = fa, edge = arr[0], chain = arr, lc = lc, osmIsland = isl,
                                           extra = Mathf.Max(0f, 0.5f * (RefugeMinIslandM - isl)) };
                }
                bool prof = BuildRefugePair(map, add, sp, s2, pair);
                if (prof) RefugeProfiled += 2; else RefugeFallback++;
                for (int c = 0; c < 2; c++)
                {
                    var r = pair[c];
                    Refuges.Add(r);
                    res[RefugeKey(sp.node, r.chain[0])] = r;
                    // the far end's key: the chain's last edge at the far node
                    // (round 1's cap there measures the chain from that end)
                    var rc = (int[])r.chain.Clone(); System.Array.Reverse(rc);
                    var back = new Refuge { node = fa, far = sp.node, edge = rc[0], chain = rc, lc = r.lc, osmIsland = r.osmIsland, extra = r.extra, profiled = r.profiled };
                    res[RefugeKey(fa, back.edge)] = back;
                }
            }
            return res;
        }

        // ---- round 2: the island's own profile ---------------------------
        static readonly Dictionary<int, Refuge> refugeEdge = new Dictionary<int, Refuge>();

        /// <summary>THE SECTION DIRECTION on a refuge carriageway (round 2):
        /// the island axis's normal, not the OSM edge's - OSM's centreline
        /// fans up to 25 degrees off the road there, and a cross-section
        /// square to IT draws every lateral (lane lines, edges, traffic)
        /// compressed by the cosine, so no shift could keep them straight.
        /// left: unit, on the edge's left (its plus side). False: not a
        /// refuge carriageway (the edge's own normal).</summary>
        /// <summary>Are two edges the two carriageways of one profiled refuge island?</summary>
        public static bool SameRefuge(int a, int b) =>
            refugeEdge.Count > 0 && refugeEdge.TryGetValue(a, out var ra) && refugeEdge.TryGetValue(b, out var rb) && ra != rb && ra.node == rb.node && ra.far == rb.far;

        public static bool RefugeNormal(CityMap.Edge e, float s, out Vector2 left)
        {
            left = default;
            if (refugeEdge.Count == 0 || !refugeEdge.TryGetValue(e.index, out var r)) return false;
            AxisClosest(r.ax, r.axS, e.PointAt(s), out _, out _, out var lf);
            var t = e.TangentAt(s);
            left = Vector2.Dot(lf, new Vector2(-t.y, t.x)) >= 0f ? lf : -lf;
            return true;
        }

        // The owner, looking at round 1 (2026-11-02): "roads are not made
        // like this". On the real road the through lanes and the outer
        // edges run DEAD STRAIGHT past a refuge island; the island sits in
        // the centre lane's space and tapers to a rounded nose at each end.
        // Round 1 only capped the taper, so the drawn road still followed
        // OSM's fanning centrelines (an S-wobble). Now each carriageway's
        // shift is sampled every RefugeDs along its chain so that its OUTER
        // edge lies on the undivided road's edge line - carried through
        // between the two nodes along an axis on their approach tangents -
        // whatever OSM's line does in between (sh = target - OSM offset). The
        // centre lane's space between the two carriageways is pavement (a
        // WIDEN ease, pavement only) except the island: wI = OSM's island
        // width clamped to RefugeMinIslandM..RefugeMaxIslandM, a rounded
        // nose RefugeTipM in from each node tapering to full width over
        // clamp(Lc/4, 4, 10) m. A 4-lane road (no centre lane) has no room:
        // its lanes move out by what the island needs, eased over at least
        // RefugeMoveMinM so no lane line slopes more than RefugeSlope.
        public const float RefugeDs = 0.25f, RefugeMaxIslandM = 2.4f, RefugeTipM = 1f, RefugeMoveMinM = 20f, RefugeMoveMaxM = 1.3f, RefugeSlope = 0.06f, RefugeDesignSlope = 0.05f, RefugeMaxStrayM = 12f;

        /// <summary>A rounded nose's half width at x past its tip: 0 before
        /// it, a kerb arc of radius r at the tip, then a straight flank tangent
        /// to it reaching h at x = ln, h beyond.</summary>
        public static float NoseHalf(float x, float h, float ln, float r)
        {
            if (x <= 0f || h <= 0f) return 0f;
            if (x >= ln) return h;
            r = Mathf.Min(r, h);
            // the flank from (ln, h) tangent to the circle about (r, 0)
            var C = new Vector2(r, 0f); var Q = new Vector2(ln, h);
            var v = Q - C; float D = v.magnitude;
            if (D <= r + 1e-4f) return x <= r ? Mathf.Sqrt(Mathf.Max(0f, r * r - (r - x) * (r - x))) : h;
            float a = Mathf.Acos(Mathf.Clamp(r / D, -1f, 1f));
            var vn = v / D;
            var T = C + r * new Vector2(vn.x * Mathf.Cos(a) - vn.y * Mathf.Sin(a), vn.x * Mathf.Sin(a) + vn.y * Mathf.Cos(a));
            if (x <= T.x) return Mathf.Sqrt(Mathf.Max(0f, r * r - (r - x) * (r - x)));
            return Mathf.Min(h, T.y + (h - T.y) * (x - T.x) / Mathf.Max(1e-4f, ln - T.x));
        }

        /// <summary>The closest point of a polyline axis to p: its arc u, the
        /// point, and the left normal there.</summary>
        public static void AxisClosest(Vector2[] ax, float[] axS, Vector2 p, out float u, out Vector2 at, out Vector2 left)
        {
            float best = float.MaxValue; u = 0f; at = ax[0]; left = Vector2.up;
            for (int k = 1; k < ax.Length; k++)
            {
                Vector2 a = ax[k - 1], ab = ax[k] - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-8f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                var q = a + ab * t;
                float d = (p - q).sqrMagnitude;
                if (d < best)
                {
                    best = d; at = q; u = axS[k - 1] + t * (axS[k] - axS[k - 1]);
                    var tn = ab / Mathf.Sqrt(l2); left = new Vector2(-tn.y, tn.x);
                }
            }
        }

        /// <summary>The chain edge (index k) and its s at distance d along a
        /// chain from its node.</summary>
        static void ChainAt(CityMap map, int[] chain, int node, float d, out int k, out float s)
        {
            int from = node;
            for (k = 0; k < chain.Length; k++)
            {
                var e = map.edges[chain[k]];
                bool fwd = e.a == from;
                if (d <= e.length || k == chain.Length - 1)
                {
                    s = Mathf.Clamp(fwd ? d : e.length - d, 0f, e.length);
                    return;
                }
                d -= e.length; from = fwd ? e.b : e.a;
            }
            k = chain.Length - 1; s = 0f;
        }

        /// <summary>An undivided road's extent on a side (world vector) at its node.</summary>
        static float ExtentToward(CityMap.Edge U, int node, Vector2 side)
        {
            var t = U.a == node ? U.TangentAt(0f) : U.TangentAt(U.length);
            return Vector2.Dot(new Vector2(-t.y, t.x), side) > 0f ? U.lmPlus : U.lmMinus;
        }

        /// <summary>The direction an undivided road runs INTO its node, as a
        /// chord from 10 m out (or half the edge).</summary>
        static Vector2 ChordInto(CityMap map, CityMap.Edge U, int node)
        {
            float d = Mathf.Min(10f, U.length * 0.49f);
            var p = U.PointAt(U.a == node ? d : U.length - d);
            var v = map.nodes[node] - p;
            if (v.sqrMagnitude > 1e-4f) return v.normalized;
            return U.a == node ? -U.TangentAt(0f) : U.TangentAt(U.length);
        }

        /// <summary>Build one refuge pair's profile eases (both carriageways).
        /// False (nothing added): no room for an island - round 1's cap.</summary>
        static bool BuildRefugePair(CityMap map, List<Ease>[] add, Split sp, Split s2, Refuge[] pr)
        {
            var U1 = map.edges[sp.u]; var U2 = map.edges[s2.u];
            foreach (var r in pr) foreach (int ei in r.chain) { var e = map.edges[ei]; if (e.lmPlus == 0f && e.lmMinus == 0f) return false; }
            if ((U1.lmPlus == 0f && U1.lmMinus == 0f) || (U2.lmPlus == 0f && U2.lmMinus == 0f)) return false;
            // THE AXIS: the undivided road's line carried through on both approach tangents
            Vector2 P1 = map.nodes[sp.node], P2 = map.nodes[s2.node];
            Vector2 t1 = ChordInto(map, U1, sp.node), t2 = -ChordInto(map, U2, s2.node);
            float L = Vector2.Distance(P1, P2);
            if (L < 8f || Vector2.Dot(t1, (P2 - P1) / L) < 0.5f || Vector2.Dot(t2, (P2 - P1) / L) < 0.5f) return false;
            const int NA = 64;
            var ax = new Vector2[NA + 1]; var axS = new float[NA + 1];
            for (int i = 0; i <= NA; i++)
            {
                float t = i / (float)NA, tt = t * t, t3 = tt * t;
                ax[i] = (2f * t3 - 3f * tt + 1f) * P1 + (t3 - 2f * tt + t) * L * t1 + (-2f * t3 + 3f * tt) * P2 + (t3 - tt) * L * t2;
                if (i > 0) axS[i] = axS[i - 1] + Vector2.Distance(ax[i - 1], ax[i]);
            }
            float axLen = axS[NA];
            var left1 = new Vector2(-t1.y, t1.x); var left2 = new Vector2(-t2.y, t2.x);
            for (int c = 0; c < 2; c++) { pr[c].ax = ax; pr[c].axS = axS; pr[c].axLen = axLen; pr[c].so = 1; }
            // each carriageway's outer side: SPLT's offset at the first node (U's frame = the axis's)
            int soA = sp.offA > 0.05f ? 1 : sp.offA < -0.05f ? -1 : 0, soB = sp.offB > 0.05f ? 1 : sp.offB < -0.05f ? -1 : 0;
            int SideOfMid(Refuge r)
            {
                var p = ChainPoint(map, r.chain, r.node, 0.5f * r.lc);
                AxisClosest(ax, axS, p, out _, out var at, out var lf);
                return Vector2.Dot(p - at, lf) >= 0f ? 1 : -1;
            }
            if (soA == 0 || soB == 0 || soA == soB) { soA = SideOfMid(pr[0]); soB = SideOfMid(pr[1]); }
            if (soA == soB) return false;
            // pr[0] is sp.a's chain (FindRefuges walks sp.a first)
            int[] so = { soA, soB };
            for (int c = 0; c < 2; c++)
            {
                var r = pr[c];
                r.so = so[c];
                r.uOut1 = ExtentToward(U1, sp.node, so[c] * left1);
                r.uOut2 = ExtentToward(U2, s2.node, so[c] * left2);
            }
            // the room each carriageway leaves at mid (inner edge from the axis, outer edge flush)
            float GapAt(Refuge r, float d)
            {
                ChainAt(map, r.chain, r.node, d, out int k, out float s);
                var e = map.edges[r.chain[k]];
                r.Local(e.PointAt(s), out float u, out _);
                return r.OuterTarget(u) - (e.lmPlus + e.lmMinus);
            }
            // a chain that strays far from the axis (or onto the wrong side of
            // it) is not a refuge island the axis describes: round 1's cap
            foreach (var r in pr)
                for (float d = 0f; d <= r.lc; d += 1f)
                {
                    ChainAt(map, r.chain, r.node, d, out int k, out float s);
                    r.Local(map.edges[r.chain[k]].PointAt(s), out float u, out float q);
                    if (q > RefugeMaxStrayM || q < -1f || u < -1f || u > axLen + 1f) return false;
                }
            // a carriageway whose lanes change along the island (a bay, a lane
            // added) is not one cross-section the profile can carry: round 1's cap
            foreach (var r in pr)
            {
                var e0 = map.edges[r.chain[0]];
                foreach (int ei in r.chain)
                {
                    var e = map.edges[ei];
                    if (Mathf.Abs(e.lmPlus + e.lmMinus - e0.lmPlus - e0.lmMinus) > 0.05f || e.profile != e0.profile) return false;
                }
            }
            float wI = Mathf.Clamp(0.5f * (pr[0].osmIsland + pr[1].osmIsland), RefugeMinIslandM, RefugeMaxIslandM);
            float half = 0.5f * wI;
            for (int c = 0; c < 2; c++)
            {
                var r = pr[c];
                // the room where the island is widest: the chain point at the axis's mid
                float dMid = 0.5f * r.lc, bestDu = float.MaxValue;
                for (float d = 0f; d <= r.lc; d += 0.5f)
                {
                    ChainAt(map, r.chain, r.node, d, out int kk, out float ss);
                    r.Local(map.edges[r.chain[kk]].PointAt(ss), out float uu, out _);
                    if (Mathf.Abs(uu - 0.5f * axLen) < bestDu) { bestDu = Mathf.Abs(uu - 0.5f * axLen); dMid = d; }
                }
                float g = GapAt(r, dMid);
                float need = half - g;
                if (need > 0.02f)
                {
                    // the undivided road's own width change already slopes the edge line
                    float RefugeDesignSlope = Mathf.Max(0.02f, LineModel.RefugeDesignSlope - Mathf.Abs(r.uOut2 - r.uOut1) / Mathf.Max(1f, axLen));
                    // 4-LANE: no centre lane - the lanes move out, gently
                    r.moveLen = Mathf.Min(0.5f * axLen, Mathf.Max(RefugeMoveMinM, 1.5f * need / RefugeDesignSlope));
                    r.move = Mathf.Min(need, Mathf.Min(RefugeMoveMaxM, RefugeDesignSlope * r.moveLen / 1.5f));
                }
                else { r.move = 0f; r.moveLen = 0f; }
                half = Mathf.Min(half, g + r.move);
            }
            if (half < 0.5f * RefugeMinIslandM - 0.15f) return false;      // no room for a kerbed island
            // the nose set-backs: RefugeTipM, or (4-lane) where both
            // carriageways have moved out far enough for the tip
            float SetBack(bool fromStart)
            {
                float x0 = RefugeTipM;
                if (pr[0].move <= 0f && pr[1].move <= 0f) return x0;
                for (float u = 0f; u < 0.5f * axLen; u += 0.25f)
                {
                    float uu = fromStart ? u : axLen - u, room = float.MaxValue;
                    for (int c = 0; c < 2; c++)
                    {
                        var r = pr[c];
                        float d = Mathf.Clamp(uu / axLen * r.lc, 0f, r.lc);
                        ChainAt(map, r.chain, r.node, d, out int k, out float s);
                        var e = map.edges[r.chain[k]];
                        room = Mathf.Min(room, r.OuterTarget(uu) + r.MoveAt(uu) - (e.lmPlus + e.lmMinus));
                    }
                    if (room >= 0.6f * half) return Mathf.Max(x0, u);
                }
                return 0.5f * axLen;
            }
            float x0s = SetBack(true), x0e = SetBack(false);
            float ln = Mathf.Clamp(0.25f * axLen, 4f, 10f);
            ln = Mathf.Min(ln, Mathf.Max(1.5f, 0.5f * (axLen - 1f - x0s - x0e)));
            if (x0s + x0e + 2f >= axLen) return false;
            float rn = Mathf.Min(CityMeshes.MedianNoseR, 0.5f * half);
            foreach (var r in pr) { r.half = half; r.x0s = x0s; r.x0e = x0e; r.ln = ln; r.rn = rn; r.profiled = true; foreach (int ei in r.chain) refugeEdge[ei] = r; }
            // THE EASES, edge by edge: the shift (edge frame) and the inner widening
            foreach (var r in pr)
            {
                int from = r.node; float d0 = 0f;
                foreach (int ei in r.chain)
                {
                    var e = map.edges[ei];
                    bool fwd = e.a == from;
                    // + when the edge's left (its plus side) is this carriageway's outer side
                    var pm = e.PointAt(0.5f * e.length); var tm = e.TangentAt(0.5f * e.length);
                    AxisClosest(ax, axS, pm, out _, out _, out var lfm);
                    int se = Vector2.Dot(new Vector2(-tm.y, tm.x), r.so * lfm) >= 0f ? 1 : -1;
                    float cOut = se > 0 ? e.lmPlus : e.lmMinus, cw = e.lmPlus + e.lmMinus;
                    int n = Mathf.Max(2, Mathf.CeilToInt(e.length / RefugeDs) + 1);
                    float ds = e.length / (n - 1);
                    var sh = new float[n]; var wd = new float[n];
                    for (int k = 0; k < n; k++)
                    {
                        float x = k * ds, s = fwd ? x : e.length - x;
                        r.Local(e.PointAt(s), out float u, out float q);
                        float O = r.OuterTarget(u) + r.MoveAt(u);
                        sh[k] = se * (O - q - cOut);
                        wd[k] = Mathf.Max(0f, O - cw - r.NoseHalf(u));
                    }
                    var list = add[e.index] ??= new List<Ease>(2);
                    list.Add(new Ease { shift = true, fromA = fwd, dw = 1f, len = d0 + e.length + 1f, d0 = d0, prof = sh, profD0 = d0, profDs = ds });
                    list.Add(new Ease { widen = true, side = (sbyte)(-se), fromA = fwd, dw = 1f, len = d0 + e.length + 1f, d0 = d0, prof = wd, profD0 = d0, profDs = ds });
                    d0 += e.length; from = fwd ? e.b : e.a;
                }
            }
            return true;
        }

        /// <summary>A profile ease's sections: wherever the sampled value
        /// departs from the chord since the last section by over 1 cm (or
        /// every 4 m), so the nose's arc and the shift are not faceted.</summary>
        static void ProfSamples(CityMap.Edge e, in Ease z, float sMin, float sMax, List<float> into)
        {
            var p = z.prof; int n = p.Length;
            int j = 0;
            for (int i = 2; i < n; i++)
            {
                bool cut = (i - j) * z.profDs > 4f;
                for (int m = j + 1; m < i && !cut; m++)
                {
                    float f = (m - j) / (float)(i - j);
                    if (Mathf.Abs(p[m] - (p[j] + (p[i] - p[j]) * f)) > 0.01f) cut = true;
                }
                if (!cut) continue;
                j = i - 1;
                float x = j * z.profDs, sv = z.fromA ? x : e.length - x;
                if (sv > sMin + 0.05f && sv < sMax - 0.05f) into.Add(sv);
            }
        }

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
        public static float ShiftRateOf(CityMap.Edge e) => ShiftRate(e);
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
                              float dwOther = 0f, byte twoRole = 0, long tag = 0)
        {
            // the join's tag (roads pass L5): set at the join, carried on through
            if (depth == 0) tag = EaseTag(fromNode, e.index, side);
            // never longer than the run it eases: the next change along owns the
            // ribbon from there (a default length ran past a 35 m piece into the
            // next join, whose own step it then did not see)
            if (depth == 0) len = Mathf.Max(0.5f, Mathf.Min(len, RoomAlong(map, partner, e, fromNode, side, false)));
            var z = new Ease { side = (sbyte)side, fromA = e.a == fromNode, dw = dw, len = len, d0 = d0, narrow = narrow.profile, narrowLay = LayoutOf(narrow), narrowFlip = flip, dwOther = dwOther, twoRole = twoRole, tag = tag };
            if (taggedEdges != null) { if (!taggedEdges.TryGetValue(tag, out var tl)) taggedEdges[tag] = tl = new List<int>(2); tl.Add(e.index); }
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
            Propagate(map, add, partner, p, far, pSide, dw, len, d0 + e.length, narrow, pFlip, depth + 1, dwOther, twoRole, tag);
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
                if (z.aux) { AuxSamples(e, z, sMin, sMax, into); continue; }
                if (z.prof != null) { ProfSamples(e, z, sMin, sMax, into); continue; }
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

        /// <summary>An aux ease's section positions (roads pass L5): the nose
        /// N, the shoulder ease, the full-width end and the taper (faceted
        /// within 2 cm). The step at N itself is forced by the builder.</summary>
        static void AuxSamples(CityMap.Edge e, in Ease z, float sMin, float sMax, List<float> into)
        {
            float d0 = z.d0, len = e.length; bool fromA = z.fromA; int arm = z.auxArm;
            void At(float x)
            {
                float d = x * arm;
                if (d < d0 - 1e-3f || d > d0 + len + 1e-3f) return;
                float sv = fromA ? d - d0 : len - (d - d0);
                if (sv > sMin + 0.3f && sv < sMax - 0.3f) into.Add(sv);
            }
            At(z.auxD);
            if (z.auxLs > 1e-3f)
            {
                float k = 6f * Mathf.Abs(z.auxWN - z.auxW) / (z.auxLs * z.auxLs);
                float step = k > 1e-6f ? Mathf.Sqrt(8f * 0.02f / k) : z.auxLs;
                int n = Mathf.Max(1, Mathf.CeilToInt(z.auxLs / Mathf.Max(0.5f, step)));
                for (int i = 0; i <= n; i++) At(z.auxD - z.auxLs * i / n);
            }
            At(z.auxXF);
            if (z.auxLt > 1e-3f)
            {
                float k = 6f * Mathf.Abs(z.auxW) / (z.auxLt * z.auxLt);
                float step = k > 1e-6f ? Mathf.Sqrt(8f * 0.02f / k) : z.auxLt;
                int n = Mathf.Max(1, Mathf.CeilToInt(z.auxLt / Mathf.Max(0.5f, step)));
                for (int i = 1; i <= n; i++) At(z.auxXF - z.auxLt * i / n);
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
                    if (ez[i].shift || ez[i].relay || ez[i].aux || ez[i].centreOnly || ez[i].widen) continue;
                    float r = Reduction(e, ez[i], s);
                    if (r <= 1e-4f) continue;
                    if (ez[i].side > 0) { if (r > rp) { rp = r; zp = i; } } else if (r > rm) { rm = r; zm = i; }
                }
            float plus0 = e.lmPlus != 0f || e.lmMinus != 0f ? e.lmPlus : e.width * 0.5f;
            float sh = ShiftAt(e, s);
            float auxP = AuxLineShift(e, s, 1), auxM = AuxLineShift(e, s, -1);
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
                // roads pass L5: the edge line on an aux lane's side rides it
                if (auxP > 0f && lay.kind[k] == KEdgeP) lat += auxP;
                if (auxM > 0f && lay.kind[k] == KEdgeM) lat -= auxM;
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
