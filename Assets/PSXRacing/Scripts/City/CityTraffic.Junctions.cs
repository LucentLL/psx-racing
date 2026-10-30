using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// ROAD LOGIC AT THE JUNCTIONS (2026-10-29, the census's box, STOP and
    /// crossing-flow classes). What a driver in Charlotte does, written down:
    ///   * DON'T BLOCK THE BOX: nobody enters a junction (on a green, from a
    ///     STOP or a yield, or through on the major road) unless the lane it
    ///     leaves by has room for it past the junction - a queue that ends in
    ///     the box locks the crossing street (the census's gridlock-box: cars
    ///     standing 15 s+ in the box behind a full exit).
    ///   * A CLAIM: a car given the way at a STOP or yield, still short of the
    ///     box, holds it - the next car does not take the same moment.
    ///   * ALL-WAY STOPS go in the order the cars stopped, but the car whose
    ///     exit is full is skipped, and opposite approaches going straight or
    ///     right go together (MUTCD 2B.07's usual practice).
    ///   * GAPS by movement (HCM's critical gaps, a little shorter: the
    ///     traffic pulls out briskly): right 5.0 s, straight 5.5, left 6.5,
    ///     a merge 3.5; a major-road car standing in its queue leaves the way.
    ///   * RIGHT ON RED after a full stop (NC G.S. 20-158), clear of the
    ///     cross street's green and the oncoming lefts - not where the city
    ///     posts NO TURN ON RED (uptown's walked junctions).
    ///   * PERMISSIVE LEFTS yield to oncoming on the green; the first one
    ///     waiting goes on the yellow once the oncoming stop for it.
    ///   * A FRESH GREEN waits for the car still clearing the box from the
    ///     last phase.
    /// </summary>
    public partial class CityTraffic
    {
        /// <summary>Carriageway splits (CitySignals.SplitArms): not boxes.</summary>
        bool[] splitNode;

        /// <summary>The critical gaps (HCM 2010 Exhibit 19-10 less ~1 s: the
        /// city's drivers pull out briskly). Round 2's half second less made
        /// more cars pull out in front of the major road's (the check's wrecks
        /// 0 -> 2): back to round 1's.</summary>
        const float GapRightS = 5.0f, GapThroughS = 5.5f, GapLeftS = 6.5f, GapMergeS = 3.5f;
        /// <summary>The why (Drive) of a car held short of a full box.</summary>
        const byte WhyKeepClear = 7;
        /// <summary>The why (Drive) of a car giving way in a signal's box with no head for it (InnerYield).</summary>
        const byte WhyHeadless = 8;

        /// <summary>The horizon: junctions ahead on each car's plan, up to its
        /// first control, within this far.</summary>
        const int HorizonN = 3;
        const float HorizonM = 170f;

        /// <summary>What held a car at its line (Car.blockWhy; the census's
        /// stuck notes say it with the car).</summary>
        const byte BlockPlayer = 1, BlockInBox = 2, BlockClaim = 3, BlockOrder = 4, BlockMajor = 5, BlockRight = 6,
                   BlockOncoming = 7, BlockYellowIn = 8, BlockClearing = 9, BlockFullExit = 10, BlockRtor = 11;

        static string BlockName(byte b)
        {
            switch (b)
            {
                case BlockPlayer: return "the player";
                case BlockInBox: return "a car in the box";
                case BlockClaim: return "a car given the way";
                case BlockOrder: return "a car that stopped first";
                case BlockMajor: return "the major road's traffic";
                case BlockRight: return "a yield on its right";
                case BlockOncoming: return "oncoming";
                case BlockYellowIn: return "a car on the last yellow";
                case BlockClearing: return "a car clearing the box";
                case BlockFullExit: return "its full exit";
                case BlockRtor: return "the cross street (right on red)";
                default: return "?";
            }
        }

        /// <summary>The census's why of a hold at a line: the rule and the
        /// car (with what holds IT - a deadlock reads as a ring).</summary>
        string Blocker(Car c)
        {
            if (c.why < 10 || c.why >= 20) return c.leadId >= 0 ? " [" + CarHeld(c.leadId) + "]" : "";
            if (c.blockWhy == 0) return "";
            return " [" + BlockName(c.blockWhy) + (c.blockId >= 0 ? ": " + CarHeld(c.blockId) : "") + "]";
        }

        /// <summary>Car id, what it does and what holds it (and by whom).</summary>
        string CarHeld(int id)
        {
            foreach (var o in live)
                if (o.id == id)
                {
                    int by = o.why < 10 ? o.leadId : o.blockId;
                    return $"car {o.id} {o.speed:0.0} m/s {MoveOf(o)} held by {WhyName(o.why)}{(by >= 0 ? " (car " + by + ")" : "")}";
                }
            return "car " + id;
        }

        /// <summary>
        /// THE HORIZON of car c this step: the junctions ahead on its plan
        /// within <see cref="HorizonM"/> - each run's end (a degree-2 vertex
        /// too: a road is split there) up to and with the first control
        /// (past it the car stops, or is let through, there). The gap rules
        /// (CanGo, OncomingConflict, RtorClear) read it: a car one edge back
        /// from a junction - past a vertex, a short internal run - is on its
        /// way there all the same (2026-10-29 census: a STOP car at Edgehill
        /// Road South pulled out in front of a Queens Road car 74 m back on
        /// the edge before, most likely - it saw only cars whose run ENDS at
        /// its junction). A control it will be let through - a green now, a
        /// yellow it is committed to, a STOP or yield it was given the way at,
        /// a line it is over - is no end: past it the car comes on (round 2's
        /// census: a STOP at Queens Road pulled out across a car that had a
        /// green 50 m before it).
        /// Entry 0 is Facts' approach.
        /// </summary>
        void Horizon(Car c, double now)
        {
            c.hN = 0;
            float dist = -c.u;
            for (int k = 0; k < c.legCount && c.hN < HorizonN && dist < HorizonM; k++)
            {
                ref Leg L = ref c.legs[k];
                if (L.turn) { dist += L.len; continue; }
                int i = c.hN++;
                c.hJ[i] = JKey(L.node); c.hKey[i] = L.key; c.hIdx[i] = L.app; c.hCtl[i] = L.ctl;
                c.hDist[i] = dist + LineAt(L) - c.halfL;
                c.hU[i] = TravelTan(map.edges[L.edge], L.dir, L.s1);
                c.hTurn[i] = k + 1 < c.legCount ? c.legs[k + 1].kind : (sbyte)0;
                c.hLeg[i] = (sbyte)k;
                bool through = L.ctl == CtlNone || c.hDist[i] < 0f || c.goKey == L.key || c.committed == L.key || c.rtorKey == L.key ||
                               (L.ctl == CtlSignal && L.app >= 0 && CitySignals.AspectOf(apps[L.app].channel, now) == CitySignals.Aspect.Green);
                if (!through) break;
                dist += L.len;
            }
        }

        /// <summary>
        /// LANES THAT JOIN AHEAD: c and o in different lanes of one run (k, j)
        /// whose next turns end in ONE lane - a lane that ends at a node (East
        /// 6th Street's three lanes into one at node 15117), two turn lanes into
        /// one. Before, two cars on one run in different lanes never looked at
        /// each other again until one was in the turn: side by side into a 4 m
        /// turn, the one a hair behind braked too late (2026-10-29 check: a
        /// 6.1 m/s wreck there). Now the merge is ordered on the approach: the
        /// lane that goes on (the turn with the least sideways shift) first
        /// unless the other is clearly ahead; two merging lanes, whoever gets
        /// there first. The second keeps a car length and a half behind.
        /// </summary>
        void JoinAhead(Car c, int k, Car o, int j, float oc, ref float best, ref float vL)
        {
            ref Leg A = ref c.legs[k + 1];
            ref Leg B = ref o.legs[j + 1];
            if (!A.turn || !B.turn || A.node != B.node || A.edge2 != B.edge2 || A.dir2 != B.dir2 || A.laneOut != B.laneOut || A.laneIn == B.laneIn) return;
            float dC = cumC[k + 1], dO = oc + o.legs[j].len;
            if (dC > 120f && dO > 120f) return;
            float gap = dC - dO, lengths = c.halfL + o.halfL;
            float shiftC = Shift(A), shiftO = Shift(B);
            bool cThrough = shiftC + 0.5f < shiftO, oThrough = shiftO + 0.5f < shiftC;
            float tC = dC / Mathf.Max(c.speed, 2f), tO = dO / Mathf.Max(o.speed, 2f);
            bool cFirst;
            if (cThrough) cFirst = !(gap - lengths > 6f + c.speed * 0.8f || tC - tO > 2.5f);
            else if (oThrough) cFirst = -gap - lengths > 6f + o.speed * 0.8f || tO - tC > 2.5f;
            else cFirst = tC < tO || (Mathf.Approximately(tC, tO) && c.id < o.id);
            if (cFirst) return;
            float g = Mathf.Max(gap, lengths + 1.5f) - lengths;
            if (g < best) { best = g; vL = o.speed; c.leaderInLane = false; c.leaderV = o.speed; c.leadKind = 1; c.leadId = o.id; }
        }

        /// <summary>A turn's sideways shift: how far its end lies off the line
        /// of its start (m).</summary>
        static float Shift(in Leg T)
        {
            Vector2 t0 = (T.p1 - T.p0).sqrMagnitude > 1e-6f ? (T.p1 - T.p0).normalized : Vector2.up;
            Vector2 d = T.p3 - T.p0;
            return Mathf.Abs(t0.x * d.y - t0.y * d.x);
        }

        /// <summary>Do car c's turn after run k and car o's turn - its leg
        /// <paramref name="jo"/> (-1: the one it is in, or the one after its
        /// run) - come within the two cars' half widths of each other (o's
        /// from where it is when it is in it)?</summary>
        static bool LeftsCross(Car c, int k, Car o, int jo = -1)
        {
            if (k + 1 >= c.legCount || !c.legs[k + 1].turn || o.legCount == 0) return false;
            if (jo < 0) jo = o.legs[0].turn ? 0 : 1;
            if (jo >= o.legCount || !o.legs[jo].turn) return false;
            ref Leg A = ref c.legs[k + 1];
            ref Leg B = ref o.legs[jo];
            float reach = c.halfW + o.halfW + 0.3f;
            int na = Mathf.Clamp(Mathf.CeilToInt(A.len / 1.5f), 4, 24), nb = Mathf.Clamp(Mathf.CeilToInt(B.len / 1.5f), 4, 24);
            float t0 = jo == 0 ? Mathf.Clamp01(o.u / Mathf.Max(0.1f, B.len)) : 0f;
            for (int i = 0; i <= na; i++)
            {
                var p = Bez(A, (float)i / na, out _);
                for (int j = 0; j <= nb; j++)
                {
                    var q = Bez(B, t0 + (1f - t0) * j / nb, out _);
                    if ((p - q).sqrMagnitude < reach * reach) return true;
                }
            }
            return false;
        }

        /// <summary>Does the way car c takes from where it is - the rest of its run
        /// along its lane as far as the crossing test looks (14 m at least, more at
        /// speed: CrossGap's reach), then its next turn; or the rest of the turn it
        /// is in - pass within its half width and half a metre of car o's body?</summary>
        bool PathHits(Car c, Car o)
        {
            if (c.legCount == 0) return true;
            ref Leg L = ref c.legs[0];
            if (L.turn) return L.len <= 0.5f || InTheWay(c, L, o, Mathf.Clamp01(c.u / L.len));
            var e = map.edges[L.edge];
            var right = new Vector2(o.fwd.y, -o.fwd.x);
            float reach = c.halfW + 0.5f, rem = L.len - c.u;
            // as far as CrossGap sees a car across (3 m + a second of travel + both
            // bodies): a shorter look let a fast car find a standing one on its
            // lane only inside its stopping distance
            float win = Mathf.Max(14f, 4f + c.speed + c.halfL + o.halfL + o.halfW);
            for (float x = 0f; x <= Mathf.Min(rem, win); x += 1f)
            {
                var p = PlanPoint(e, Mathf.Clamp(L.s0 + L.dir * (c.u + x), 0f, e.length), c.lat, out _);
                Vector2 d = p - o.pos;
                float a = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, o.fwd)) - o.halfL), b = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, right)) - o.halfW);
                if (a * a + b * b < reach * reach) return true;
            }
            if (rem >= win || c.legCount < 2) return false;
            // the next leg: its turn (and 8 m past it); a run on past a plain
            // vertex is not walked - braked for, as before round 3
            return !c.legs[1].turn || InTheWay(c, c.legs[1], o, 0f);
        }

        /// <summary>Is car o's body in the way car c takes through the box
        /// (the turn T from <paramref name="t0"/> of it on, and 8 m on past it,
        /// c's half width and half a metre either side)?</summary>
        static bool InTheWay(Car c, in Leg T, Car o, float t0 = 0f)
        {
            var right = new Vector2(o.fwd.y, -o.fwd.x);
            int n = Mathf.Clamp(Mathf.CeilToInt(T.len * (1f - t0)), 6, 40);
            float reach = c.halfW + 0.5f;
            Vector2 tEnd = (T.p3 - T.p2).sqrMagnitude > 1e-6f ? (T.p3 - T.p2).normalized : c.fwd;
            for (int i = 0; i <= n + 8; i++)
            {
                Vector2 p = i <= n ? Bez(T, t0 + (1f - t0) * i / n, out _) : T.p3 + tEnd * (i - n);
                Vector2 d = p - o.pos;
                float a = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, o.fwd)) - o.halfL), b = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, right)) - o.halfW);
                if (a * a + b * b < reach * reach) return true;
            }
            return false;
        }

        /// <summary>Per CitySignals junction id: is it signalised?</summary>
        bool[] junctionSignal = new bool[0];

        void PrepareJunctions()
        {
            var js = CitySignals.All(map, trims);
            junctionSignal = new bool[js != null ? js.Count : 0];
            if (js != null) foreach (var j in js) junctionSignal[j.id] = j.signal;
            int nn = map.nodes.Length;
            splitNode = new bool[nn];
            for (int i = 0; i < nn; i++) if (degree[i] >= 3) splitNode[i] = CitySignals.SplitArms(map, i, out _, out _, out _);
        }

        /// <summary>Is node n a junction box (three roads or more, not a
        /// carriageway split, not a mitred bend)?</summary>
        bool IsBox(int n) => degree[n] >= 3 && !splitNode[n] && !trims.mitre[n];

        /// <summary>
        /// DON'T BLOCK THE BOX: should the car hold short of the junction at
        /// the end of run k - is the lane it leaves by full (a slow car's tail
        /// within its own length of the far side, less the cars already in the
        /// box for that lane)? Only before the line, only where it can stop,
        /// never at a freeway merge or diverge, never where the way out is too
        /// short to hold a car anyway (a queue there is one box with the next).
        /// </summary>
        bool KeepClearHold(Car c, int k, float dLine)
        {
            if (k + 2 >= c.legCount) return false;
            ref Leg L = ref c.legs[k];
            ref Leg T = ref c.legs[k + 1];
            if (!T.turn || T.merge || T.exit || T.kind == 2) return false;
            if (!IsBox(L.node)) return false;
            if (dLine < 0.3f || dLine > 45f) return false;
            if (c.committed == L.key || c.goKey == L.key || c.rtorKey == L.key) return false;
            if (c.speed > 3f && dLine < c.speed * c.speed / (2f * 4.5f)) return false;   // too late to stop for it
            // the way out: past the box's turns and its internal runs (a cluster),
            // and past runs too short to hold this car (two nodes a few metres
            // apart are one box: the way out is beyond both)
            float need = 2f * c.halfL + StopS0 + 0.5f;
            int kx = k + 2;
            while (kx < c.legCount && (c.legs[kx].turn || trims.Internal(c.legs[kx].edge) || c.legs[kx].len < need + 3f))
            {
                // a car standing on a short run between the two: the box is full
                if (!c.legs[kx].turn && ExitRoom(c, c.legs[kx]) < c.legs[kx].len) return true;
                kx++;
            }
            if (kx >= c.legCount) return false;
            ref Leg X = ref c.legs[kx];
            float room = ExitRoom(c, X);
            if (room >= need) return false;

            return true;
        }

        /// <summary>The room at the start of exit run X in its lane: the tail
        /// of the nearest slow car on it, less what the cars in the box bound
        /// for that lane will take. MaxValue: nothing slow there.</summary>
        float ExitRoom(Car c, in Leg X)
        {
            float tail = float.MaxValue, boxed = 0f;
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked || o.legCount == 0) continue;
                ref Leg ol = ref o.legs[0];
                if (!ol.turn)
                {
                    if (ol.edge != X.edge || ol.dir != X.dir) continue;
                    bool lane = o.lane == X.laneIn || (o.lc && (o.laneFrom == X.laneIn || o.laneTo == X.laneIn));
                    if (!lane || o.speed > 5f) continue;
                    // its tail, and the room a queue still rolling makes in 1.5 s
                    float t = ((ol.s0 + ol.dir * o.u) - X.s0) * X.dir - o.halfL + o.speed * 1.5f;
                    if (t < -2f * o.halfL) continue;
                    if (t < tail) tail = t;
                }
                else if (o.legCount > 1)
                {
                    ref Leg on = ref o.legs[1];
                    if (on.turn || on.edge != X.edge || on.dir != X.dir || on.laneIn != X.laneIn) continue;
                    boxed += 2f * o.halfL + StopS0;
                }
            }
            return tail == float.MaxValue ? float.MaxValue : tail - boxed;
        }

        /// <summary>The seconds car c needs from where it is to be clear across
        /// the box after run k (its line, the turn, its own length) at its
        /// comfortable acceleration from its speed now (0: no turn there).</summary>
        static float ClearS(Car c, int k, float dLine)
        {
            if (k + 1 >= c.legCount || !c.legs[k + 1].turn) return 0f;
            float d = Mathf.Max(0f, dLine) + c.legs[k + 1].len + 2f * c.halfL;
            float a = Mathf.Max(1f, c.aMax), v = c.speed;
            return (-v + Mathf.Sqrt(v * v + 2f * a * d)) / a;
        }

        /// <summary>The gap a car pulling out from a STOP or a yield needs in
        /// the major road, by its movement.</summary>
        static float GapFor(sbyte turn, bool merge) =>
            merge ? GapMergeS : turn == 1 ? GapRightS : turn == -1 ? GapLeftS : GapThroughS;

        /// <summary>Two movements into one box that do not cross: the same way
        /// in, or from opposite approaches with neither turning left.</summary>
        static bool Compatible(Vector2 u, sbyte turn, Vector2 ou, sbyte oturn)
        {
            float d = Vector2.Dot(u, ou);
            if (d > 0.85f) return true;
            return d < -0.8f && turn != -1 && oturn != -1 && turn != 2 && oturn != 2;
        }

        /// <summary>A major-road car standing in its queue (held by the car
        /// ahead close by, or short of a full box): it leaves the way to a car
        /// crossing in front of it.</summary>
        static bool StandingInQueue(Car o) =>
            o.speed < 0.5f && (o.why == 10 + WhyKeepClear || (o.why == 1 && o.gap < 6f));

        /// <summary>
        /// RIGHT ON RED (after the full stop): nothing in the box from another
        /// way, nothing on a green from the left within the right turn's gap,
        /// no oncoming left turn coming.
        /// </summary>
        bool RtorClear(Car c, int jk, int key, Vector2 u, double now)
        {
            Vector2 at = map.nodes[c.legs[0].node];
            if ((playerPos - at).sqrMagnitude < 20f * 20f && Mathf.Abs(playerPos3.y - c.y) < LevelM) { c.blockId = -2; c.blockWhy = BlockPlayer; return false; }
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked) continue;
                if (o.boxJ == jk && o.inKey != key) { c.blockId = o.id; c.blockWhy = BlockInBox; return false; }
                // the horizon: on its way to this junction's green
                for (int h = 0; h < o.hN; h++)
                {
                    if (o.hJ[h] != jk) continue;
                    if (o.hKey[h] == key || o.hIdx[h] < 0 || o.hCtl[h] != CtlSignal) break;
                    if (CitySignals.AspectOf(apps[o.hIdx[h]].channel, now) == CitySignals.Aspect.Red) break;
                    float cross = u.x * o.hU[h].y - u.y * o.hU[h].x;
                    bool fromLeft = cross < -0.3f;
                    bool oncomingLeft = Vector2.Dot(o.hU[h], u) < -0.7f && o.hTurn[h] == -1;
                    if (!fromLeft && !oncomingLeft) break;
                    float d = o.hDist[h], t = d / Mathf.Max(o.speed, 0.5f);
                    if ((o.speed > 1f && t < GapRightS) || (h == 0 && d < 12f && d > -2f)) { c.blockId = o.id; c.blockWhy = BlockRtor; return false; }
                    break;
                }
            }
            return true;
        }

        /// <summary>
        /// A SIGNAL'S BOX WITH NO HEAD FOR THIS CAR: on an internal run (a
        /// divided road's median, a one-way pair's square) moving off from a
        /// stand, or on an arm the signal could stand no head on, a car gives
        /// way to what crosses its way there now - a car moving in the box
        /// across its turn, a car coming on its green (2026-10-29 census: a car
        /// that stood on Albemarle Road's 2 m internal run for its lane, then
        /// went on, met North Sharon Amity's green in the box; Monroe Road's
        /// square and the "signal / no control" crossing flows).
        /// </summary>
        bool InnerYield(Car c, int k, int jk, int key, Vector2 u, double now)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked) continue;
                if (o.boxJ == jk && o.inKey != c.inKey && o.speed >= 1f && Mathf.Abs(Vector2.Dot(o.boxU, u)) < 0.6f && LeftsCross(c, k, o))
                { c.blockId = o.id; c.blockWhy = BlockInBox; return true; }
                if (o.appJ != jk || o.appKey == key || o.appIdx < 0 || o.appCtl != CtlSignal || o.legCount < 2) continue;
                var asp = CitySignals.AspectOf(apps[o.appIdx].channel, now);
                if (asp == CitySignals.Aspect.Red || (asp == CitySignals.Aspect.Yellow && o.committed != o.appKey)) continue;
                if (Mathf.Abs(Vector2.Dot(o.appU, u)) >= 0.6f) continue;
                float t = o.appDist / Mathf.Max(o.speed, 0.5f);
                if (!((o.speed > 1f && t < 4f) || (o.speed > 0.5f && o.appDist < 6f && o.appDist > -2f))) continue;
                if (!LeftsCross(c, k, o, 1)) continue;
                c.blockId = o.id; c.blockWhy = BlockClearing;
                return true;
            }
            return false;
        }

        /// <summary>A car in the box that came in on a phase now over (it is
        /// clearing it): a fresh green waits for it.</summary>
        bool ClearingInBox(Car c, int jk, int key, Vector2 u, double now)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked || o.inKey == key || o.speed < 0.5f || o.rtorKey == o.inKey) continue;
                // committed to the last phase's yellow and still coming
                if (o.boxJ != jk)
                {
                    if (o.appJ == jk && o.appKey != key && o.committed == o.appKey && o.appDist < 40f && o.appDist > -2f &&
                        o.speed > 2f && o.appDist < o.speed * 4f &&
                        Mathf.Abs(Vector2.Dot(o.appU, u)) < 0.6f) { c.blockId = o.id; c.blockWhy = BlockYellowIn; return true; }
                    continue;
                }
                if (Mathf.Abs(Vector2.Dot(o.boxU, u)) >= 0.6f) continue;
                // came in on a signal approach whose phase is over (a car from an
                // arm with no head, or one standing in the box, is the physics' to
                // avoid). A car crossing a cluster's second node - a divided road's
                // median, a one-way pair's square - came in by the approach it
                // ENTERED on (Car.entryKey: before, its internal run had no head,
                // and the fresh green ran into it)
                if (!approachOf.TryGetValue(o.inKey, out int ai) || !apps[ai].signal ||
                    CitySignals.AspectOf(apps[ai].channel, now) == CitySignals.Aspect.Green) continue;
                c.blockId = o.id; c.blockWhy = BlockClearing;
                return true;
            }
            return false;
        }
    }
}
