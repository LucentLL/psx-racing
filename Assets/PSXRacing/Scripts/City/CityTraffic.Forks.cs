using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// ROAD LOGIC JUNCTIONS ROUND 3e: LANE USE AT A FORK, AND ONE APPROACH'S WAYS
    /// THAT CROSS (2026-10-30; main's traffic check at node 706, West 12th Street).
    ///
    /// THE FAULT: West 12th Street (e9802, a 12 m one-way of two lanes) forks at node
    /// 706 into the I-277 on-ramp (e388, one lane, 10 deg to the LEFT) and West 12th
    /// Street on (e10600, two lanes, 6 deg to the right). Both are straight-ons (under
    /// 35 deg), so each in-lane was mapped by MapStraight on its own, to the nearest
    /// lane of each branch within 2 m - and the ramp's one lane starts on the street's
    /// centre line, 1.83 m from BOTH in-lanes: the right lane took the ramp on the left
    /// while the left lane took the street on the right. Their ways cross in a 4 m box,
    /// and nothing paired them: on the run they were "known" (one leg, kept apart by
    /// their lanes: LeaderGap), in the box their nearly parallel ways stayed outside
    /// the crossing scan's corridor until the bodies met (car 4 at 2.6 m/s from lane 0
    /// into the ramp, car 25 at 8.4 m/s from lane 1 into the street, into its side).
    ///
    /// THE RULE (MUTCD lane use, option lanes - 2009 MUTCD 2B.20 / 3B.10, the lane-use
    /// arrows and the "option lane" of an interchange split): at a FORK - an approach
    /// with two or more ways on that are straight-ons (Extend's kind 0, freeway exits
    /// and ramp merges aside, they have their own lane) - the LEFT lanes take the LEFT
    /// branch and the RIGHT lanes the right one, each branch its share of the lanes by
    /// its own lane count, the boundary lane an OPTION lane where the counts need it
    /// (two lanes into a one-lane ramp and a two-lane street: the left lane may take
    /// either, the right lane the street only); where OSM's turn:lanes marks the
    /// approach's lanes (slight_left / slight_right with no through, and the node has
    /// no left / right turn of its own) those lanes are the branch's. Within a branch
    /// the lanes keep their order (no two lanes swap across a box). The branches' sides
    /// are where they lie downstream (their point up to 30 m on), not their tangents at
    /// the node (two near-parallel branches' tangents are a coin toss).
    /// A car in the wrong lane changes on the run before (LaneBack takes the change back
    /// over a short piece onto the longer road behind it), holds for its gap like at a
    /// turn (no squeezed change into a fork), and where it can no longer change it takes
    /// the branch its lane serves (a missed branch, Replan) - never a cut across the box.
    /// Where two ways of one approach still cross (a car still changing lanes, a turn
    /// begun from the wrong lane anywhere), CrossAhead orders them: the one already in
    /// its turn, else the one nearer the crossing, goes first, the other stops short of
    /// the crossing until the first's body is past it - the same rule from both sides.
    /// PSX_TRAFFIC_FORKLANES=0 is the before (the census's crossing audit counts both).
    /// </summary>
    public partial class CityTraffic
    {
        /// <summary>Round 3e: lane use by side at a fork, and lanes kept in their order across
        /// a box (PSX_TRAFFIC_FORKLANES=0: each branch from any lane within 2 m of its lanes).</summary>
        public static bool ForkLanesOn = System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_FORKLANES") != "0";
        /// <summary>Round 3e: two ways of one approach that cross are ordered (PSX_TRAFFIC_CROSSAHEAD=0: not).</summary>
        public static bool CrossAheadOn = System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_CROSSAHEAD") != "0";

        /// <summary>The why (Drive, the census) of a car held short of the way of a car of its
        /// own approach that crosses its own (CrossAhead).</summary>
        const byte LeadCrossOwn = 6;

        sealed class ForkUse
        {
            public int node;
            public readonly List<int> edge = new List<int>();
            public readonly List<int> dir = new List<int>();
            /// <summary>Per branch (leftmost first): the in-lanes (kerb-counted bits) that may take it.</summary>
            public readonly List<int> allow = new List<int>();
            /// <summary>Pairs of its branches' ways whose bodies still meet with that allotment.</summary>
            public int conflicts;
        }
        readonly Dictionary<int, ForkUse> forks = new Dictionary<int, ForkUse>();
        static readonly ForkUse NoFork = new ForkUse();

        /// <summary>The fork at the end of approach (e, dir) (the node it arrives at), or null.</summary>
        ForkUse ForkOf(CityMap.Edge e, int dir)
        {
            int key = (e.index << 1) | (dir > 0 ? 1 : 0);
            if (forks.TryGetValue(key, out var f)) return f == NoFork ? null : f;
            f = BuildFork(e, dir);
            forks[key] = f ?? NoFork;
            return f;
        }

        /// <summary>The in-lanes (kerb-counted bits) that may take the way on (e2, dir2) from
        /// approach (e, dir) at node n: all of them where the node is no fork for it (or the rule is off).</summary>
        int ForkAllow(CityMap.Edge e, int dir, int n, int e2i, int dir2)
        {
            int nIn = Lanes(e.index, dir), all = (1 << nIn) - 1;
            if (!ForkLanesOn || nIn < 2 || (dir > 0 ? e.b : e.a) != n) return all;
            var f = ForkOf(e, dir);
            if (f == null) return all;
            for (int i = 0; i < f.edge.Count; i++) if (f.edge[i] == e2i && f.dir[i] == dir2) return f.allow[i];
            return all;
        }

        /// <summary>Is turn T a branch of a fork that not every lane may take?</summary>
        bool ForkBranch(in Leg T)
        {
            if (!ForkLanesOn || !T.turn || T.kind != 0 || T.merge || T.exit) return false;
            var e = map.edges[T.edge];
            int all = (1 << Lanes(T.edge, T.dir)) - 1;
            return ForkAllow(e, T.dir, T.node, T.edge2, T.dir2) != all;
        }

        /// <summary>May a car in <paramref name="lane"/> take fork turn T (true where T is no fork's)?</summary>
        bool ForkServes(in Leg T, int lane)
        {
            if (!ForkLanesOn || !T.turn || T.kind != 0 || T.merge || T.exit) return true;
            int m = ForkAllow(map.edges[T.edge], T.dir, T.node, T.edge2, T.dir2);
            return lane >= 0 && lane < 30 && (m & (1 << lane)) != 0;
        }

        ForkUse BuildFork(CityMap.Edge e, int dir)
        {
            int n = dir > 0 ? e.b : e.a;
            int nIn = Lanes(e.index, dir);
            if (nIn < 2 || degree[n] < 3) return null;
            Vector2 tIn = TravelTan(e, dir, dir > 0 ? e.length : 0f);
            bool anyMain = AnyMainline(e, dir, n);
            var br = new List<(int ei, int d2, float side, float ang, int m)>();
            foreach (int ei in map.nodeEdges[n])
            {
                var o = map.edges[ei];
                if (ei == e.index || o.a == o.b || !routable[ei]) continue;
                int d2 = o.a == n ? 1 : -1;
                if (d2 < 0 && o.oneway) continue;
                float ang = Vector2.SignedAngle(tIn, TravelTan(o, d2, d2 > 0 ? 0f : o.length));
                if (Mathf.Abs(ang) >= 35f) continue;
                // a freeway's exit or a ramp's merge has its own lane (Extend): no branch of a fork
                bool exit = !e.link && o.link && anyMain && e.cls >= 4, merge = e.link && !o.link && o.cls >= 4;
                if ((exit || merge) && !RampLanesRunOn(e, dir, o, d2, n, merge)) continue;
                // where it lies downstream, left (+) or right of the approach's line through the node
                float z = Mathf.Clamp(o.length * 0.5f, 3f, 30f);
                Vector2 p = o.PointAt(d2 > 0 ? z : o.length - z) - map.nodes[n];
                br.Add((ei, d2, tIn.x * p.y - tIn.y * p.x, ang, Mathf.Max(1, Lanes(ei, d2))));
            }
            if (br.Count < 2) return null;
            // leftmost first; two that lie within half a metre of each other there, by their angle
            br.Sort((x, y) => Mathf.Abs(x.side - y.side) > 0.5f ? y.side.CompareTo(x.side) : y.ang.CompareTo(x.ang));
            // the lanes a straight-on may use here, as MapStraight takes them: not a lane its
            // taper ends, not one marked for a turn this node has
            float sA = EndS(e, dir);
            TurnOnlyLanes(e, dir, n, out int lo, out int ro);
            NodeTurns(e, dir, n, out bool hl, out bool hr);
            int avoid = StraightAvoid(lo, ro, hl, hr, nIn);
            var U = new List<int>();
            for (int i = 0; i < nIn; i++) if ((avoid & (1 << i)) == 0 && LaneWidth(e, dir, i, sA) >= 2f) U.Add(i);
            if (U.Count == 0) return null;
            int N = U.Count, M = 0, uMask = 0;
            foreach (var b in br) M += b.m;
            foreach (int i in U) uMask |= 1 << i;
            // OSM's turn:lanes where the node has no turn of its own on that side: the lanes it
            // marks slight_left (right) with no through are the left (right) branch's
            int tagL = (hl ? 0 : lo) & uMask, tagR = (hr ? 0 : ro) & uMask;
            int B = br.Count;
            // THE PRIMARY ALLOTMENT (left-counted lanes t: U[N-1-t]): the tags, else the count rule
            var primary = new int[B];
            bool tagged = false;
            int C = 0;
            for (int b = 0; b < B; b++)
            {
                int mask = 0;
                if (B == 2 && tagL != 0 && tagR == 0 && tagL != uMask) { mask = b == 0 ? tagL : uMask & ~tagL; tagged = true; }
                else if (B == 2 && tagR != 0 && tagL == 0 && tagR != uMask) { mask = b == 1 ? tagR : uMask & ~tagR; tagged = true; }
                if (mask == 0)
                {
                    // the count rule: branch b's share of the N usable lanes counted from the left,
                    // [C N / M, (C + m) N / M) - a boundary that falls inside a lane makes it an option lane
                    int loT = C * N / M, hiT = ((C + br[b].m) * N + M - 1) / M - 1;
                    loT = Mathf.Clamp(loT, 0, N - 1); hiT = Mathf.Clamp(hiT, loT, N - 1);
                    for (int t = loT; t <= hiT; t++) mask |= 1 << U[N - 1 - t];
                }
                C += br[b].m;
                primary[b] = mask;
            }
            // THE WAYS AS DRAWN AT THE MOUTHS: each usable lane's way into each branch (the
            // branch's lane its own lane runs on into, as MapStraight finds it) - the sides are
            // where the branches lie downstream, but the bodies meet where their lanes lie at the
            // mouths (a ramp whose one lane starts on the street's centre line, node 706)
            var way = new Leg[B, N];
            var wayOk = new bool[B, N];
            for (int b = 0; b < B; b++)
            {
                var o = map.edges[br[b].ei]; int d2 = br[b].d2;
                float sB = d2 > 0 ? Back(o, n) : o.length - Back(o, n);
                int nOut = Lanes(o.index, d2);
                for (int t = 0; t < N; t++)
                {
                    int lane = U[N - 1 - t], bj = -1; float bd = float.MaxValue;
                    for (int j = 0; j < nOut; j++)
                    {
                        if (LaneWidth(o, d2, j, sB) < 2f) continue;
                        float d = LaneJump(e, dir, lane, sA, o, d2, j, sB, n);
                        if (d < bd) { bd = d; bj = j; }
                    }
                    if (bj < 0) continue;
                    float sBw = sB;
                    ForkStretch(e, dir, lane, sA, o, d2, bj, ref sBw);   // as LayMove lays a branch
                    var T = new Leg { turn = true, edge = e.index, dir = (sbyte)dir, edge2 = o.index, dir2 = (sbyte)d2, node = n, laneIn = lane, laneOut = bj, kind = 0, sA = sA, sB = sBw };
                    ShapeTurn(ref T);
                    way[b, t] = T; wayOk[b, t] = true;
                }
            }
            // the allotments in reach of the primary one (two branches: every boundary, with and
            // without an option lane there), the one whose ways' bodies meet least, nearest the
            // primary (and the tags: OSM's word) - lane use by side where the drawn lanes allow it
            int Conflicts(int[] al)
            {
                int k = 0;
                for (int b = 0; b < B; b++)
                    for (int b2 = b + 1; b2 < B; b2++)
                        for (int t = 0; t < N; t++)
                        {
                            if ((al[b] & (1 << U[N - 1 - t])) == 0 || !wayOk[b, t]) continue;
                            for (int t2 = 0; t2 < N; t2++)
                                if (t2 != t && (al[b2] & (1 << U[N - 1 - t2])) != 0 && wayOk[b2, t2] && WaysTouch(way[b, t], way[b2, t2], out _)) k++;
                        }
                return k;
            }
            int Mask(int lo2, int hi2) { int m = 0; for (int t = Mathf.Max(0, lo2); t <= Mathf.Min(N - 1, hi2); t++) m |= 1 << U[N - 1 - t]; return m; }
            int Diff(int[] al) { int d = 0; for (int b = 0; b < B; b++) { int x = al[b] ^ primary[b]; while (x != 0) { d += x & 1; x >>= 1; } } return d; }
            var best = primary;
            int bestScore = Conflicts(primary) * 1000;
            if (B == 2 && N >= 2)
                for (int k = 1; k < N; k++)
                    foreach (var al in new[] { new[] { Mask(0, k - 1), Mask(k, N - 1) }, new[] { Mask(0, k), Mask(k, N - 1) }, new[] { Mask(0, k - 1), Mask(k - 1, N - 1) } })
                    {
                        int sc = Conflicts(al) * 1000 + Diff(al) * (tagged ? 50 : 1);
                        if (sc < bestScore) { bestScore = sc; best = al; }
                    }
            var f = new ForkUse { node = n };
            for (int b = 0; b < B; b++) { f.edge.Add(br[b].ei); f.dir.Add(br[b].d2); f.allow.Add(best[b]); }
            f.conflicts = bestScore / 1000;
            return f;
        }

        static readonly Vector2[] lineA = new Vector2[24], lineB = new Vector2[24];

        /// <summary>A move's way as a line: its arc (13 points) and 12 m on along the lane it joins.</summary>
        int WayLine(in Leg T, Vector2[] pts)
        {
            int k = 0;
            for (int i = 0; i <= 12; i++) pts[k++] = Bez(T, i / 12f, out _);
            var e2 = map.edges[T.edge2];
            for (float x = 2f; x <= 12.01f; x += 2f)
            {
                float s = T.sB + T.dir2 * x;
                if (s < 0f || s > e2.length) break;
                pts[k++] = LanePos(e2, T.dir2, T.laneOut, s);
            }
            return k;
        }

        /// <summary>A CROSSING PAIR's test (round 3e): two ways whose lines come within two half
        /// widths of a car (1.8 m) of each other - along their arcs and 12 m on along the lanes they
        /// join (node 706's: the ramp's lane is laid from the street's centre line and crosses the
        /// street's left lane in its first 7 m; the right lane's way into it and the left lane's
        /// way on never cross as arcs - their lanes do, past the mouths) - not where both are still
        /// at their own lane's mouth. Two cars abreast there would touch.</summary>
        bool WaysTouch(in Leg a, in Leg b, out float atA)
        {
            int na = WayLine(a, lineA), nb = WayLine(b, lineB);
            for (int i = 0; i < na; i++)
                for (int j = 0; j < nb; j++)
                {
                    if (i <= 1 && j <= 1) continue;
                    if ((lineA[i] - lineB[j]).sqrMagnitude < 1.8f * 1.8f) { atA = i / (float)Mathf.Max(1, na - 1); return true; }
                }
            atA = -1f;
            return false;
        }

        /// <summary>The half width two cars' bodies are swept with in the conflict tests (the
        /// traffic's cars are 1.7-1.9 m wide).</summary>
        const float ConflictHW = 0.9f, ConflictHL = 2.3f;

        /// <summary>Do two turns' swept bodies meet (two cars side by side through the box): any
        /// point of one arc whose body (2.3 x 0.9 m half, along its tangent there) overlaps the
        /// other's at any point of its arc - not where both are still at their own lane's mouth.
        /// <paramref name="atA"/>: how far along A (0..1).</summary>
        static bool LegsMeet(in Leg a, in Leg b, float hw) => LegsMeet(a, b, hw, out _);
        static bool LegsMeet(in Leg a, in Leg b, float hw, out float atA)
        {
            const int K = 12;
            atA = -1f;
            for (int i = 0; i <= K; i++)
            {
                Vector2 pa = Bez(a, (float)i / K, out var da);
                Vector2 ta = da.sqrMagnitude > 1e-8f ? da.normalized : (a.p3 - a.p0).normalized;
                for (int j = 0; j <= K; j++)
                {
                    if (i <= 1 && j <= 1) continue;   // both at their own lane's mouth: side by side, as the lanes lie
                    Vector2 pb = Bez(b, (float)j / K, out var db);
                    if ((pa - pb).sqrMagnitude > 36f) continue;
                    Vector2 tb = db.sqrMagnitude > 1e-8f ? db.normalized : (b.p3 - b.p0).normalized;
                    if (RectDepth(pa, ta, ConflictHL, hw, pb, tb, ConflictHL, hw) > 0f) { atA = (float)i / K; return true; }
                }
            }
            return false;
        }

        /// <summary>Does the node offer approach (e, dir) a straight-on onto a road that is not a
        /// ramp (Extend's anyMainline: an exit is an exit only off a road that goes on)?</summary>
        bool AnyMainline(CityMap.Edge e, int dir, int n)
        {
            Vector2 tIn = TravelTan(e, dir, dir > 0 ? e.length : 0f);
            foreach (int ei in map.nodeEdges[n])
            {
                var o = map.edges[ei];
                if (ei == e.index || o.a == o.b || !routable[ei] || o.link) continue;
                int d2 = o.a == n ? 1 : -1;
                if (d2 < 0 && o.oneway) continue;
                if (Vector2.Angle(tIn, TravelTan(o, d2, d2 > 0 ? 0f : o.length)) < 35f) return true;
            }
            return false;
        }

        /// <summary>ONE MOVE AS EXTEND LAYS IT (round 3e: Extend, the turn probe and the
        /// census's crossing audit share it): its kind, a freeway exit or merge, where its turn
        /// leaves the run (sA) and joins the next edge (sB), the lane it leaves from (req) and
        /// the lane it joins - for a car in <paramref name="refLane"/>.</summary>
        void LayMove(CityMap.Edge e, int dir, int n, CityMap.Edge e2, int dir2, float angle, bool anyMainline, float runS0, float runS1,
                     int refLane, int leftOnly, int rightOnly, bool hasLeft, bool hasRight,
                     out sbyte kind, out bool exit, out bool merge, out float sA, out float sB, out int req, out int outLane)
        {
            float aAbs = Mathf.Abs(angle);
            kind = e2.index == e.index || aAbs > 150f ? (sbyte)2 : aAbs < 35f ? (sbyte)0 : angle > 0f ? (sbyte)-1 : (sbyte)1;
            exit = kind == 0 && !e.link && e2.link && anyMainline && e.cls >= 4;
            merge = kind == 0 && e.link && !e2.link && e2.cls >= 4;
            // A RAMP WHOSE LANES RUN ON (road-logic lanes): no merge, no exit - every lane runs straight on
            if ((merge || exit) && RampLanesRunOn(e, dir, e2, dir2, n, merge)) merge = exit = false;
            // where the turn starts and ends: the junction's mouths, or a long run through a
            // freeway merge or diverge
            sA = runS1;
            // (round 3e: a merge begins, an exit ends, no nearer the node than where the ramp's own
            // lane is clear of the mainline's lanes - RampClear)
            if (merge) { float zb = Mathf.Max(ZoneBack(e, dir), RampClear(e, dir, true, n, e2, dir2)); sA = dir > 0 ? e.length - zb : zb; }
            if (exit) sA = dir > 0 ? Mathf.Max(e.length - 25f, e.length * 0.4f) : Mathf.Min(25f, e.length * 0.6f);
            if (merge) sB = dir2 > 0 ? Mathf.Min(25f, e2.length * 0.6f) : Mathf.Max(e2.length - 25f, e2.length * 0.4f);
            else if (exit) { float zb = Mathf.Max(ZoneBack(e2, -dir2), RampClear(e2, dir2, false, n, e, dir)); sB = dir2 > 0 ? zb : e2.length - zb; }
            else sB = dir2 > 0 ? Back(e2, n) : e2.length - Back(e2, n);
            // never behind where the run started
            if ((sA - runS0) * dir < 0f) sA = runS0;
            // the lanes: the kerb lane to turn right, the inside lane to turn left - or, where
            // turn:lanes marks them, any of the turn's own lanes (a double left: the one nearest)
            // - the lane that runs on going straight (round 3e: at a fork, a lane of its side)
            int nIn = Lanes(e.index, dir), nOut = Lanes(e2.index, dir2);
            bool rampRight = exit ? RampOnRight(e2, dir2, n, TravelTan(e, dir, dir > 0 ? e.length : 0f), angle <= 0f) : angle <= 0f;
            if (kind == 1) TurnLane(rightOnly != 0 ? rightOnly : 1, refLane, nIn, nOut, false, out req, out outLane);
            else if (kind == -1) TurnLane(leftOnly != 0 ? leftOnly : 1 << (nIn - 1), refLane, nIn, nOut, true, out req, out outLane);
            else if (kind == 2) { req = nIn - 1; outLane = nOut - 1; }
            else if (exit) { req = rampRight ? 0 : nIn - 1; outLane = rampRight ? 0 : nOut - 1; }
            else if (merge)
            {
                bool fromRight = RampOnRight(e, dir, n, TravelTan(e2, dir2, dir2 > 0 ? 0f : e2.length), angle >= 0f);
                req = fromRight ? 0 : nIn - 1; outLane = fromRight ? 0 : nOut - 1;
            }
            else MapStraight(e, dir, sA, e2, dir2, sB, refLane, n, StraightAvoid(leftOnly, rightOnly, hasLeft, hasRight, nIn), out req, out outLane);
            // (round 3e) a fork's branch laid without a sideways S (ForkStretch)
            if (kind == 0 && !merge && !exit && ForkLanesOn && (dir > 0 ? e.b : e.a) == n &&
                ForkAllow(e, dir, n, e2.index, dir2) != (1 << nIn) - 1)
                ForkStretch(e, dir, req, sA, e2, dir2, outLane, ref sB);
        }

        /// <summary>
        /// A FORK'S WAY WITHOUT A SIDEWAYS S (round 3e; V2's trace at node 706: the left lane's way
        /// into the I-277 ramp, laid mouth to mouth, swung 29 deg RIGHT across the 4 m box - the
        /// ramp's one lane starts on the street's centre line, 1.4 m right of the left lane, then
        /// bears 13 deg left - and its body met the right lane's car going on, 0.22 m). A branch
        /// that bends off by more than StretchStraight's 4 deg is joined where its lane lies on the
        /// branch's own side of the in-lane's line: the turn's end is carried on along the branch,
        /// a metre at a time (at most 20 m, at most 45% of it), until the arc bends one way only -
        /// the far mouth on the side the branch turns to, the near one on the other side of the
        /// far mouth's line (both crosses of a plain arc). The branch's lane is its own pavement
        /// there; the arc between keeps to the in-lane's side of it.
        /// </summary>
        void ForkStretch(CityMap.Edge e, int dir, int laneIn, float sA, CityMap.Edge e2, int dir2, int laneOut, ref float sB)
        {
            Vector2 p0 = LanePos(e, dir, laneIn, sA), t0 = TravelTan(e, dir, sA);
            Vector2 t3 = TravelTan(e2, dir2, sB);
            if (Vector2.Angle(t0, t3) <= 4f) return;   // StretchStraight's
            float sMax = dir2 > 0 ? Mathf.Min(sB + 20f, e2.length * 0.45f) : Mathf.Max(sB - 20f, e2.length * 0.55f);
            for (int guard = 0; guard < 21; guard++)
            {
                Vector2 p3 = LanePos(e2, dir2, laneOut, sB);
                t3 = TravelTan(e2, dir2, sB);
                float turn = t0.x * t3.y - t0.y * t3.x;
                Vector2 d = p3 - p0;
                float c0 = t0.x * d.y - t0.y * d.x, c3 = -(t3.x * d.y - t3.y * d.x);
                float sg = Mathf.Sign(turn);
                // one way only: each mouth at least half a plain arc's own rise (chord x sin half
                // the turn) to the side it turns to of the other's line
                float rise = 0.5f * d.magnitude * Mathf.Sin(0.5f * Vector2.Angle(t0, t3) * Mathf.Deg2Rad) - 0.05f;
                if (c0 * sg >= rise && c3 * sg >= rise) return;
                float next = sB + dir2;
                if ((next - sMax) * dir2 > 0f) return;
                sB = next;
            }
        }


        /// <summary>
        /// WHERE A RAMP'S LANE IS CLEAR OF THE MAINLINE'S (round 3e; the base's traffic check, the
        /// West 12th Street ramp e388 into I-277 at node 707: OSM joins the ramp's way to the
        /// freeway's centre-line node, so the ramp's own line - and the lane the traffic drives on
        /// it - runs over I-277's lanes for its last ~45 m: 3.1 m right of the freeway's line 26 m
        /// out, astride its lanes 0 and 1 (lats -5.5 / -1.8), while the merge began only 20 m out
        /// (ZoneBack's heading test: the line kinks into the node): a ramp car at 7.9 m/s into the
        /// side of one in lane 1 at 10.9). The metres from the node along the ramp - back from it
        /// for a merge (<paramref name="arrives"/>), on from it for an exit - beyond which every
        /// lane of the ramp lies at least a lane's width (3.2 m) from every lane of the mainline
        /// beside it (the arm that arrives with it, or leaves with the exit's road), plus 5 m; 0
        /// where it never comes within that. At most 110 m, at most 80% of the ramp.
        /// </summary>
        float RampClear(CityMap.Edge ramp, int rampDir, bool arrives, int n, CityMap.Edge fwy, int fwyDir)
        {
            long ck = ((long)ramp.index << 33) | ((long)fwy.index << 2) | (rampDir > 0 ? 2L : 0L) | (arrives ? 1L : 0L);
            if (rampClear.TryGetValue(ck, out float cached)) return cached;
            float r = RampClearOf(ramp, rampDir, arrives, n, fwy, fwyDir);
            rampClear[ck] = r;
            return r;
        }
        readonly Dictionary<long, float> rampClear = new Dictionary<long, float>();

        float RampClearOf(CityMap.Edge ramp, int rampDir, bool arrives, int n, CityMap.Edge fwy, int fwyDir)
        {
            Vector2 tRef = arrives ? TravelTan(fwy, fwyDir, fwyDir > 0 ? 0f : fwy.length) : TravelTan(fwy, fwyDir, fwyDir > 0 ? fwy.length : 0f);
            CityMap.Edge m = null; int dm = 0;
            foreach (int mi in map.nodeEdges[n])
            {
                var o = map.edges[mi];
                if (mi == ramp.index || mi == fwy.index || o.a == o.b || o.link || !routable[mi]) continue;
                int d = arrives ? (o.b == n ? 1 : -1) : (o.a == n ? 1 : -1);
                if (d < 0 && o.oneway) continue;
                Vector2 t = arrives ? TravelTan(o, d, d > 0 ? o.length : 0f) : TravelTan(o, d, d > 0 ? 0f : o.length);
                if (Vector2.Angle(t, tRef) < 35f) { m = o; dm = d; break; }
            }
            if (m == null) return 0f;
            int nr = Lanes(ramp.index, rampDir), nm = Lanes(m.index, dm);
            float cap = Mathf.Min(110f, ramp.length * 0.8f, m.length);
            float last = -1f;
            for (float x = 0f; x <= cap; x += 2f)
            {
                float sr = arrives ? (rampDir > 0 ? ramp.length - x : x) : (rampDir > 0 ? x : ramp.length - x);
                float sm = arrives ? (dm > 0 ? m.length - x : x) : (dm > 0 ? x : m.length - x);
                // only along the stretch where the two run side by side (the merge's arc is laid
                // straight across it): where the ramp swings away, it has parted
                if (x > 10f && Vector2.Angle(TravelTan(ramp, rampDir, sr), TravelTan(m, dm, sm)) > 20f) break;
                float best = float.MaxValue;
                for (int i = 0; i < nr; i++)
                {
                    Vector2 p = LanePos(ramp, rampDir, i, sr);
                    for (int k = 0; k < nm; k++) best = Mathf.Min(best, Vector2.Distance(p, LanePos(m, dm, k, sm)));
                }
                if (best < 3.2f) last = x;
            }
            return last < 0f ? 0f : Mathf.Min(cap, last + 5f);
        }

        // ======================================================================
        //  Two ways whose bodies meet
        // ======================================================================
        const int WayN = 12, WayMax = 40;
        static readonly Vector2[] wayA = new Vector2[WayMax], wayB = new Vector2[WayMax], wayAT = new Vector2[WayMax], wayBT = new Vector2[WayMax];
        static readonly float[] wayAD = new float[WayMax], wayBD = new float[WayMax];
        static readonly bool[] wayATurn = new bool[WayMax], wayBTurn = new bool[WayMax];

        /// <summary>The way car x takes from where it is through its turn ahead (leg tk: 0 the
        /// one it is in, 1 the one after its run): its own place now first, its run along its lane
        /// every 2 m (its lateral place - a change under way taken to where it will be), then the
        /// turn - laid from that place at the run's end, as PopLeg lays it (a car still changing
        /// lanes, or in another lane than the turn was laid from, takes its turn from where it is).
        /// Points, headings, metres to each, and which are in the turn.</summary>
        int WayPoints(Car x, int tk, Vector2[] pts, Vector2[] tans, float[] dist, bool[] inTurn)
        {
            ref Leg T = ref x.legs[tk];
            int k = 0;
            pts[k] = x.pos; tans[k] = x.fwd; dist[k] = 0f; inTurn[k] = tk == 0; k++;
            if (tk == 0)
            {
                float t0 = T.len > 0.01f ? Mathf.Clamp01(x.u / T.len) : 1f;
                for (int i = 1; i <= WayN; i++)
                {
                    pts[k] = Bez(T, t0 + (1f - t0) * i / WayN, out var d);
                    tans[k] = d.sqrMagnitude > 1e-8f ? d.normalized : tans[k - 1];
                    dist[k] = dist[k - 1] + Vector2.Distance(pts[k - 1], pts[k]); inTurn[k] = true; k++;
                }
                return k;
            }
            ref Leg L = ref x.legs[0];
            var e = map.edges[L.edge];
            float rem = Mathf.Max(0f, L.len - x.u);
            float latEnd = x.lat;
            if (x.lc)
            {
                // a change under way: where it will be at the run's end (at its rate)
                float a0 = LaneLat(e, L.dir, x.laneFrom, L.s1), a1 = LaneLat(e, L.dir, x.laneTo, L.s1);
                float tEnd = Mathf.Clamp01(x.lcT + rem / Mathf.Max(0.5f, x.speed) / Mathf.Max(0.5f, x.lcDur));
                latEnd = Mathf.Lerp(a0, a1, LineModel.Smooth(tEnd));
            }
            else latEnd = LaneLat(e, L.dir, x.lane, L.s1);
            float sNow = Mathf.Clamp(L.s0 + L.dir * x.u, 0f, e.length);
            for (float m = 2f; m < Mathf.Min(rem, 20f) && k < WayMax - WayN - 2; m += 2f)
            {
                float s = Mathf.Clamp(sNow + L.dir * m, 0f, e.length);
                float lat = Mathf.Lerp(x.lat, latEnd, m / Mathf.Max(0.1f, rem));
                pts[k] = PlanPoint(e, s, lat, out var tn); tans[k] = tn * L.dir; dist[k] = m; inTurn[k] = false; k++;
            }
            Vector2 tA = TravelTan(e, L.dir, L.s1);
            var T2 = T;
            Shape(ref T2, PlanPoint(e, L.s1, latEnd, out _), tA, T.p3, (T.p3 - T.p2).sqrMagnitude > 1e-6f ? (T.p3 - T.p2).normalized : tA);
            pts[k] = T2.p0; tans[k] = tA; dist[k] = rem; inTurn[k] = true; k++;
            for (int i = 1; i <= WayN; i++)
            {
                pts[k] = Bez(T2, (float)i / WayN, out var d);
                tans[k] = d.sqrMagnitude > 1e-8f ? d.normalized : tans[k - 1];
                dist[k] = dist[k - 1] + Vector2.Distance(pts[k - 1], pts[k]); inTurn[k] = true; k++;
            }
            return k;
        }

        /// <summary>The turn leg of car x's plan that it is in (0) or comes to after its run (1); -1 none.</summary>
        static int TurnAhead(Car x)
        {
            if (x.legCount == 0) return -1;
            if (x.legs[0].turn) return x.legs[0].uHalf ? -1 : 0;
            return x.legCount > 1 && x.legs[1].turn && !x.legs[1].uHalf ? 1 : -1;
        }

        /// <summary>Do the ways of c and o (one approach's turns) bring their BODIES together -
        /// the first place along c's way where its body (its half width and 0.1 m) overlaps o's at
        /// any place along o's (o's own place now first: a body already there); how far each goes
        /// to it. Not where both are still on their runs (two lanes of one run are the lanes'), not
        /// two ways into one lane where they end (the converging rule's), not their places now
        /// (a touch is the contact's).</summary>
        bool WaysMeet(Car c, int tc, Car o, int to, out float dC, out float dO)
        {
            dC = dO = float.MaxValue;
            int na = WayPoints(c, tc, wayA, wayAT, wayAD, wayATurn);
            int nb = WayPoints(o, to, wayB, wayBT, wayBD, wayBTurn);
            ref Leg A = ref c.legs[tc]; ref Leg B = ref o.legs[to];
            bool oneLane = A.edge2 == B.edge2 && A.dir2 == B.dir2 && A.laneOut == B.laneOut;
            float wc = c.halfW + 0.1f, wo = o.halfW + 0.1f;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < nb; j++)
                {
                    if (i == 0 && j == 0) continue;
                    if (!wayATurn[i] && !wayBTurn[j]) continue;
                    if ((wayA[i] - wayB[j]).sqrMagnitude > 36f) continue;
                    if (oneLane && wayAD[na - 1] - wayAD[i] < 2.5f && wayBD[nb - 1] - wayBD[j] < 2.5f) continue;
                    if (RectDepth(wayA[i], wayAT[i], c.halfL, wc, wayB[j], wayBT[j], o.halfL, wo) <= 0f) continue;
                    dC = wayAD[i]; dO = wayBD[j];
                    return true;
                }
            return false;
        }

        /// <summary>
        /// THE SAME APPROACH'S WAYS THAT MEET (round 3e): c and o arriving by one approach - on its
        /// run, or one of them in its turn from it already - whose bodies their ways across the
        /// node bring together. Ordered by the same rule from both sides, so they agree: a body
        /// already where the other's way meets it stays first (it cannot wait itself out of the
        /// way); then the one already in its turn (NC G.S. 20-155: within the intersection); then
        /// the one nearer the meeting place, then the lower id. The other stops short of that place
        /// until the first's body is past it. Before, the two were never paired (node 706: car 31
        /// from lane 0 into the ramp on the left swept 2.3 m sideways across the 4 m box into car 17
        /// from lane 1 into the street on the right; their centre lines never cross - their bodies
        /// do: on the run they were one leg kept apart by their lanes, in the box outside each
        /// other's corridor until 1.5 m).
        /// </summary>
        void CrossAhead(Car c, Car o, ref float best, ref float vL)
        {
            if (!CrossAheadOn) return;
            int tc = TurnAhead(c), to = TurnAhead(o);
            if (tc < 0 || to < 0) return;
            ref Leg A = ref c.legs[tc]; ref Leg B = ref o.legs[to];
            if (A.edge != B.edge || A.dir != B.dir || A.node != B.node) return;
            // one movement from one lane is the plan's (SameLeg, OneLaneOut)
            if (A.edge2 == B.edge2 && A.dir2 == B.dir2 && A.laneOut == B.laneOut && c.lane == o.lane && !c.lc && !o.lc) return;
            // two in one lane of the run, one behind the other: the leader rule's
            if (tc == 1 && to == 1 && c.lane == o.lane && !c.lc && !o.lc) return;
            if (!WaysMeet(c, tc, o, to, out float dC, out float dO)) return;
            bool cFirst;
            if (dO <= 0.5f) cFirst = dC <= 0.5f && c.id < o.id;   // o's body is there now
            else if (dC <= 0.5f) cFirst = true;                    // c's body is there now
            else if ((tc == 0) != (to == 0)) cFirst = tc == 0;     // the one in its turn already
            else cFirst = dC < dO - 0.5f || (Mathf.Abs(dC - dO) <= 0.5f && c.id < o.id);
            if (cFirst) return;
            float g = Mathf.Max(0.05f, dC - 0.5f);
            if (c.traced) TraceCross(c, o, "X", g);
            if (g < best) { best = g; vL = 0f; c.leadKind = LeadCrossOwn; c.leadId = o.id; }
        }

        /// <summary>Two cars of one approach both in their turns now, their bodies' ways meeting
        /// ahead of both (the census's and the trace's near miss: "lane-crossing").</summary>
        bool CrossingInBox(Car a, Car b)
        {
            if (a.legCount == 0 || b.legCount == 0 || !a.legs[0].turn || !b.legs[0].turn || a.legs[0].uHalf || b.legs[0].uHalf) return false;
            ref Leg A = ref a.legs[0]; ref Leg B = ref b.legs[0];
            if (A.edge != B.edge || A.dir != B.dir || A.node != B.node) return false;
            if (A.edge2 == B.edge2 && A.dir2 == B.dir2 && A.laneOut == B.laneOut && A.laneIn == B.laneIn) return false;
            return WaysMeet(a, 0, b, 0, out float da, out float db) && da > 0.5f && db > 0.5f;
        }

        // ======================================================================
        //  The audit: one approach's ways that cross (the census, the probe)
        // ======================================================================
        struct Move { public int e2, dir2, req, outLane; public sbyte kind; public Leg T; }
        static readonly List<Move> moves = new List<Move>(32);

        /// <summary>Every move from approach (e, dir) at its node, lane by lane as Extend lays it
        /// (one per in-lane and way on: the lane it leaves from, the lane it joins).</summary>
        void MovesOf(CityMap.Edge e, int dir, List<Move> into)
        {
            into.Clear();
            int n = dir > 0 ? e.b : e.a;
            Vector2 tIn = TravelTan(e, dir, dir > 0 ? e.length : 0f);
            bool anyMain = AnyMainline(e, dir, n);
            TurnOnlyLanes(e, dir, n, out int lo, out int ro);
            NodeTurns(e, dir, n, out bool hl, out bool hr);
            int nIn = Lanes(e.index, dir);
            float s0 = dir > 0 ? 0f : e.length, s1 = EndS(e, dir);
            foreach (int ei in map.nodeEdges[n])
            {
                var o = map.edges[ei];
                if (o.a == o.b || !routable[ei]) continue;
                int d2 = o.a == n ? 1 : -1;
                if (d2 < 0 && o.oneway) continue;
                if (ei == e.index) continue;   // the U-turn back: from the left lane only
                float ang = Vector2.SignedAngle(tIn, TravelTan(o, d2, d2 > 0 ? 0f : o.length));
                for (int lane = 0; lane < nIn; lane++)
                {
                    LayMove(e, dir, n, o, d2, ang, anyMain, s0, s1, lane, lo, ro, hl, hr, out sbyte kind, out bool ex, out bool mg, out float sA, out float sB, out int req, out int outLane);
                    bool seen = false;
                    foreach (var m in into) if (m.e2 == ei && m.dir2 == d2 && m.req == req && m.outLane == outLane) { seen = true; break; }
                    if (seen) continue;
                    var T = new Leg { turn = true, edge = e.index, dir = (sbyte)dir, edge2 = ei, dir2 = (sbyte)d2, node = n, laneIn = req, laneOut = outLane, kind = kind, merge = mg, exit = ex, sA = sA, sB = sB };
                    ShapeTurn(ref T);
                    into.Add(new Move { e2 = ei, dir2 = d2, req = req, outLane = outLane, kind = kind, T = T });
                }
            }
        }

        /// <summary>Do two of one approach's moves from different lanes bring two cars abreast
        /// into touch (WaysTouch: along their arcs and the lanes they join; two into one lane are
        /// the converging rule's, not this)?</summary>
        bool MovesCross(in Move a, in Move b, out float atA)
        {
            atA = -1f;
            if (a.req == b.req) return false;
            if (a.e2 == b.e2 && a.dir2 == b.dir2 && a.outLane == b.outLane) return false;
            return WaysTouch(a.T, b.T, out atA);
        }

        static string MoveName(sbyte kind, bool exit, bool merge) =>
            merge ? "merge" : exit ? "exit" : kind == -1 ? "left" : kind == 1 ? "right" : kind == 2 ? "U" : "straight";

        /// <summary>
        /// ROUND 3e AUDIT: ONE APPROACH'S WAYS THAT CROSS. For every approach at every node,
        /// the moves Extend lays lane by lane (LayMove: each in-lane's way into each way on);
        /// a CROSSING PAIR is two of them from different in-lanes whose ways come within two half
        /// widths of a car of each other (WaysTouch: along their arcs and 12 m on along the lanes
        /// they join - node 706's arcs never cross, the lanes they join do; two ways into one lane
        /// are the converging rule's). Counted with the fork rule off (the before:
        /// PSX_TRAFFIC_FORKLANES=0) and on, one run; each pair left on is logged
        /// (lanes/lane-crossing-pair) with its kind. Also the lane map of the places in
        /// PSX_LANEMAP_AT ("x:z;..."; default West 12th Street's fork at node 706).
        /// </summary>
        public void CrossingAudit(CityRoadLog log, StringBuilder report)
        {
            bool was = ForkLanesOn;
            var list = new List<Move>(32);
            int[] pairs = new int[2], nodes = new int[2], forkNodes = new int[2];
            var kinds = new Dictionary<string, int>[] { new Dictionary<string, int>(), new Dictionary<string, int>() };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int nn = map.nodes.Length;
            for (int pass = 0; pass < 2; pass++)
            {
                ForkLanesOn = pass == 1;
                forks.Clear();
                for (int n = 0; n < nn; n++)
                {
                    if (degree[n] < 2) continue;
                    bool any = false, fork = false;
                    foreach (int ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.a == e.b || !routable[ei]) continue;
                        int dir = e.b == n ? 1 : -1;
                        if (dir < 0 && e.oneway) continue;
                        if (Lanes(ei, dir) < 2) continue;
                        if (pass == 1 && ForkOf(e, dir) != null) fork = true;
                        MovesOf(e, dir, list);
                        for (int a = 0; a < list.Count; a++)
                            for (int b = a + 1; b < list.Count; b++)
                            {
                                if (!MovesCross(list[a], list[b], out float at)) continue;
                                pairs[pass]++; any = true;
                                var A = list[a]; var B = list[b];
                                string k = MoveName(A.kind, A.T.exit, A.T.merge) + " x " + MoveName(B.kind, B.T.exit, B.T.merge);
                                kinds[pass].TryGetValue(k, out int kc); kinds[pass][k] = kc + 1;
                                if (pass == 1 && log != null)
                                    log.Add("lanes", "lane-crossing-pair", n, ei, A.e2, new Vector3(map.nodes[n].x, e.YAt(dir > 0 ? e.length : 0f), map.nodes[n].y), -1, 0f, 1f, "static",
                                            $"node {n}: from e{ei} '{e.name}' ({Lanes(ei, dir)} lanes) lane {A.req} -> e{A.e2} '{map.edges[A.e2].name}' lane {A.outLane} ({MoveName(A.kind, A.T.exit, A.T.merge)}) meets lane {B.req} -> e{B.e2} '{map.edges[B.e2].name}' lane {B.outLane} ({MoveName(B.kind, B.T.exit, B.T.merge)}) {at * 100f:0}% along the first");
                            }
                    }
                    if (any) nodes[pass]++;
                    if (fork) forkNodes[pass]++;
                }
            }
            ForkLanesOn = was;
            forks.Clear();
            string K(int p) { var sb = new StringBuilder(); foreach (var kv in kinds[p]) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value); return sb.ToString(); }
            report.AppendLine($"  STOP / YIELD LINES SET BACK (round 3e: where a car standing at the bar stands in another way through the junction; the traffic's line, not the paint): {(StopBackOn ? "" : "OFF (PSX_TRAFFIC_STOPBACK=0) ")}{StopBacks} approaches, worst {StopBackWorst:0.0} m; no clear place within {StopBackMaxM:0} m {StopBackNone}");
            report.AppendLine($"  ONE APPROACH'S WAYS CROSSING (round 3e: two of its moves from different lanes whose ways - their arcs and the lanes they join - come within 1.8 m: two cars abreast there would touch; as Extend lays them lane by lane): " +
                              $"BEFORE (PSX_TRAFFIC_FORKLANES=0) {nodes[0]} nodes, {pairs[0]} pairs [{K(0)}]; AFTER (lane use by side at {forkNodes[1]} fork nodes) {nodes[1]} nodes, {pairs[1]} pairs [{K(1)}]; {sw.ElapsedMilliseconds} ms");
            // the lane maps of the places asked for
            var atEnv = System.Environment.GetEnvironmentVariable("PSX_LANEMAP_AT");
            if (string.IsNullOrEmpty(atEnv)) atEnv = "-1638:5518";
            foreach (var place in atEnv.Split(';'))
            {
                var xz = place.Split(':');
                if (xz.Length != 2 || !float.TryParse(xz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float px) ||
                    !float.TryParse(xz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pz)) continue;
                int best = -1; float bd = 40f * 40f;
                for (int n = 0; n < nn; n++) { float d = (map.nodes[n] - new Vector2(px, pz)).sqrMagnitude; if (degree[n] >= 3 && d < bd) { bd = d; best = n; } }
                if (best < 0) { report.AppendLine($"  LANE MAP at ({px:0},{pz:0}): no junction within 40 m"); continue; }
                for (int pass = 0; pass < 2; pass++)
                {
                    ForkLanesOn = pass == 1;
                    forks.Clear();
                    report.Append(LaneMap(best, pass == 0 ? "BEFORE (PSX_TRAFFIC_FORKLANES=0)" : "AFTER (round 3e)"));
                }
                ForkLanesOn = was;
                forks.Clear();
            }
        }

        /// <summary>Node n's lane map, every approach, every move lane by lane, and the pairs
        /// of them that cross (the census's audit, the check's probe).</summary>
        public string LaneMap(int n, string label)
        {
            var sb = new StringBuilder();
            var list = new List<Move>(32);
            var pn = map.nodes[n];
            sb.AppendLine($"  LANE MAP {label}: node {n} ({pn.x:0},{pn.y:0}) degree {degree[n]} junction {nodeJunction[n]}");
            foreach (int ei in map.nodeEdges[n])
            {
                var e = map.edges[ei];
                if (e.a == e.b || !routable[ei]) continue;
                int dir = e.b == n ? 1 : -1;
                if (dir < 0 && e.oneway) continue;
                var f = ForkLanesOn ? ForkOf(e, dir) : null;
                MovesOf(e, dir, list);
                sb.AppendLine($"    from e{ei} '{e.name}' cls{e.cls}{(e.link ? " link" : "")} {Lanes(ei, dir)} lanes{(f != null ? " - a FORK of " + f.edge.Count + " branches (leftmost first): " + ForkText(f) : "")}");
                for (int a = 0; a < list.Count; a++)
                {
                    var A = list[a];
                    var crosses = new StringBuilder();
                    for (int b = 0; b < list.Count; b++)
                        if (b != a && MovesCross(A, list[b], out float at)) crosses.Append($" | MEETS lane {list[b].req}'s way to e{list[b].e2} lane {list[b].outLane} at {at * 100f:0}% of its arc");
                    var e2 = map.edges[A.e2];
                    sb.AppendLine($"      lane {A.req} (lat {LaneLat(e, dir, A.req, A.T.sA):0.00}) -> e{A.e2} '{e2.name}'{(e2.link ? " link" : "")} lane {A.outLane} of {Lanes(A.e2, A.dir2)} (lat {LaneLat(e2, A.dir2, A.outLane, A.T.sB):0.00}), " +
                                  $"{MoveName(A.kind, A.T.exit, A.T.merge)}, arc {A.T.len:0.0} m{(crosses.Length > 0 ? crosses.ToString() : " | meets no other lane's way")}");
                }
            }
            return sb.ToString();
        }

        /// <summary>The tools (round 3e): an edge's lanes over its last <paramref name="span"/> metres
        /// before the node it is driven to - each lane's centre and width, the pavement's edges.</summary>
        public string LaneProfile(int ei, int dir, float span)
        {
            if (map == null || ei < 0 || ei >= map.edges.Length) return "no such edge";
            var e = map.edges[ei];
            var sb = new StringBuilder($"  e{ei} '{e.name}' dir {dir}, {Lanes(ei, dir)} lanes, len {e.length:0.0}, run end s {EndS(e, dir):0.0}:\n");
            for (float x = span; x >= 0f; x -= 3f)
            {
                float s = Mathf.Clamp(dir > 0 ? e.length - x : x, 0f, e.length);
                LineModel.Extents(e, s, out float pM, out float pP);
                sb.Append($"    {x,4:0} m before its end: pavement -{pM:0.00}..+{pP:0.00};");
                for (int l = 0; l < Lanes(ei, dir); l++)
                {
                    float c = LaneBounds(e, dir, l, s, out float bl, out float br);
                    sb.Append($" lane {l} at {c:0.00} ({bl - br:0.00} m)");
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        string ForkText(ForkUse f)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < f.edge.Count; i++)
            {
                sb.Append(i > 0 ? "; " : "").Append('e').Append(f.edge[i]).Append(" from lanes ");
                bool firstLane = true;
                for (int l = 0; l < 30; l++) if ((f.allow[i] & (1 << l)) != 0) { sb.Append(firstLane ? "" : ",").Append(l); firstLane = false; }
            }
            return sb.ToString();
        }
    }
}
