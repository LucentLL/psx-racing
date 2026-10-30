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
        /// <summary>Round 3d: splits whose leaving carriageway runs within a body of the
        /// arriving one's lane end (TightSplit): boxes, the arriving carriageway yielding; and
        /// per directed edge (key) the arc of such an approach's line (NaN: its lane end).</summary>
        bool[] tightSplit;
        float[] holdS;
        public static int TightSplits;

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
            float reach = PathReach(c), rem = L.len - c.u;
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

        /// <summary>PathHits as a distance (round 3c): how far along its way car c's
        /// centre goes before its body (its half width and <paramref name="margin"/>:
        /// a body IN the lanes it sweeps, not one at their edge - a car waiting at a
        /// side street's line) would meet car o's - the rest of its turn, or its run
        /// as far as CrossGap looks and the turn after; MaxValue where it does not.</summary>
        float PathHitAt(Car c, Car o, float margin = 0.4f)
        {
            if (c.legCount == 0) return 0f;
            ref Leg L = ref c.legs[0];
            float reach = c.halfW + margin;
            if (L.turn) return L.len <= 0.5f ? 0f : WayHit(c, L, o, Mathf.Clamp01(c.u / L.len), 0f, reach);
            var e = map.edges[L.edge];
            var right = new Vector2(o.fwd.y, -o.fwd.x);
            float rem = L.len - c.u;
            float win = Mathf.Max(14f, 4f + c.speed + c.halfL + o.halfL + o.halfW);
            for (float x = 0f; x <= Mathf.Min(rem, win); x += 1f)
            {
                var p = PlanPoint(e, Mathf.Clamp(L.s0 + L.dir * (c.u + x), 0f, e.length), c.lat, out _);
                if (BodyNear(p, o, right, reach)) return x;
            }
            if (rem >= win || c.legCount < 2 || !c.legs[1].turn) return float.MaxValue;
            float wh = WayHit(c, c.legs[1], o, 0f, rem, reach);
            // CHANGING LANES INTO IT (round 3d, trace 1: node 9907, a car moving from lane 0 to
            // lane 1 on the last 18 m of e9370 saw a car standing by its turn only once in it,
            // at 3.8 m/s with 0.8 m left - PopLeg lays the turn again from where the car IS when
            // it gets there, not from the lane's mouth): also along the turn laid from the
            // lateral place it is in now
            if (c.lc)
            {
                var T2 = c.legs[1];
                Vector2 tA = TravelTan(e, L.dir, L.s1);
                Shape(ref T2, PlanPoint(e, L.s1, c.lat, out _), tA, T2.p3, (T2.p3 - T2.p2).sqrMagnitude > 1e-6f ? (T2.p3 - T2.p2).normalized : tA);
                wh = Mathf.Min(wh, WayHit(c, T2, o, 0f, rem, reach));
            }
            return wh;
        }

        static bool BodyNear(Vector2 p, Car o, Vector2 right, float reach)
        {
            Vector2 d = p - o.pos;
            float a = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, o.fwd)) - o.halfL), b = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, right)) - o.halfW);
            return a * a + b * b < reach * reach;
        }

        /// <summary>InTheWay as a distance: metres (from <paramref name="baseM"/> on) along
        /// turn T from <paramref name="t0"/> of it (and 8 m past it) to its first point
        /// within reach of o's body; MaxValue where none is.</summary>
        static float WayHit(Car c, in Leg T, Car o, float t0, float baseM, float reach)
        {
            var right = new Vector2(o.fwd.y, -o.fwd.x);
            int n = Mathf.Clamp(Mathf.CeilToInt(T.len * (1f - t0)), 6, 40);
            float span = T.len * (1f - t0);
            Vector2 tEnd = (T.p3 - T.p2).sqrMagnitude > 1e-6f ? (T.p3 - T.p2).normalized : c.fwd;
            for (int i = 0; i <= n + 8; i++)
            {
                Vector2 p = i <= n ? Bez(T, t0 + (1f - t0) * i / n, out _) : T.p3 + tEnd * (i - n);
                if (BodyNear(p, o, right, reach)) return baseM + (i <= n ? span * i / n : span + (i - n));
            }
            return float.MaxValue;
        }

        // ---- LOCKED PAIRS (round 3c recovery) ---------------------------------------
        const float BackMaxM = 2.0f, BackMps = 0.6f, CreepMps = 0.8f;

        /// <summary>LOCKED (round 3c recovery): c and o stand, each braking for the
        /// other across its way - after a low-speed touch in a box, or two noses in
        /// each other's way. After 1.5 s the one further through the box clears it:
        /// the other backs out of its way (Drive), and it waits for that - then o,
        /// standing off its way, is passed by the rule above; where the other cannot
        /// back (a car behind it, the start of its leg) it creeps past at a walk.
        /// True: c goes on past o now. Never a teleport, never a car taken away.</summary>
        bool Unlock(Car c, Car o)
        {
            if (c.creepPast == o) return true;   // creeping on past it (Drive ends the creep)
            bool mutual = o.leadId == c.id && o.leadKind == 4;
            // or TOUCHING one held by something else (its own queue): this one backs off it
            bool touching = !mutual && RectDepth(c.pos, c.fwd, c.halfL + 0.15f, c.halfW + 0.15f, o.pos, o.fwd, o.halfL, o.halfW) > 0f;
            if (c.speed > 0.3f || o.speed > 0.3f || !(mutual || touching))
            {
                if (c.lockWith == o.id) { c.lockWith = -1; c.backedFor = -1; }
                return false;
            }
            if (c.lockWith != o.id) { c.lockWith = o.id; c.lockSince = Time.time; c.backedFor = -1; return false; }
            if (Time.time - c.lockSince < 1.5f) return false;
            if (touching)
            {
                if (c.backFor != o && c.backedFor != o.id && CanBack(c, o)) { c.backFor = o; c.backedFor = o.id; c.backed = 0f; Stats.lockBacks++; LockNote(c, o, "lock-backed", "touching a car held by its own queue: backs off it"); }
                return false;
            }
            if (!GoesFirst(c, o))
            {
                // this one gives way: out of the other's way, backing along its own
                // turn (once a lock), where there is room behind it
                if (c.backFor != o && c.backedFor != o.id && PathHits(o, c) && CanBack(c, o))
                { c.backFor = o; c.backedFor = o.id; c.backed = 0f; Stats.lockBacks++; LockNote(c, o, "lock-backed", "locked with the car that goes first: backs out of its way"); }
                return false;
            }
            // this one goes: once the other is off its way, or now - creeping - where
            // the other cannot back out of it
            if (o.backFor == c) return false;
            if (o.backedFor != c.id && PathHits(c, o) && CanBack(o, c)) return false;   // it starts backing on its step
            float need = ClearDist(c, o);
            if (need == float.MaxValue) return false;   // its way runs on into the other: never pushed through
            c.creepPast = o; c.creepLeft = need + 0.5f; Stats.lockCreeps++;
            LockNote(c, o, "lock-crept", $"locked with a car that cannot back: creeps on {need + 0.5f:0.0} m past it");
            return true;
        }

        /// <summary>The census's record of a lock undone (not a fault: the recovery at work).</summary>
        void LockNote(Car c, Car o, string sub, string what)
        {
            TraceMoment(c, $"{sub} c{c.id} with c{o.id}");
            if (Log == null) return;
            Note(c, "traffic", sub, Time.time - c.lockSince, what + " after " + (Time.time - c.lockSince).ToString("0.0") + " s; " + Describe(c) + " | other: " + Describe(o));
        }

        /// <summary>Can car c back out of a lock: in a turn (in the box), or on a run to a
        /// junction with no control (no stop line to back over), a little way into it,
        /// room behind it.</summary>
        bool CanBack(Car c, Car from) => c.legCount > 0 && (c.legs[0].turn || c.legs[0].ctl == CtlNone) && c.u > 0.15f && RoomBehind(c, from);

        /// <summary>Of two cars locked together, does c go first? The one that clears
        /// the other sooner driving on along its own way - the car across the other's
        /// nose, not the nose (round 3c final census 1: at node 9907, North Tryon
        /// Street, an uncontrolled crossing, the car on its run touched the side of one
        /// crossing; "further through" by share of the turn tied and the lower id - the
        /// nose - was sent on into the other's side); then the one further through
        /// the box, then the lower id.</summary>
        bool GoesFirst(Car c, Car o)
        {
            float dc = ClearDist(c, o), dO = ClearDist(o, c);
            if (Mathf.Abs(dc - dO) > 0.25f) return dc < dO;
            return FurtherThrough(c, o);
        }

        /// <summary>How far car c drives along its way before its body (and 0.3 m) is
        /// clear of o's and stays clear over the next 10 m; MaxValue where its way runs
        /// on into o.</summary>
        float ClearDist(Car c, Car o)
        {
            float first = -1f;
            for (float x = 0f; x <= 10f; x += 0.5f)
            {
                if (!WayPose(c, x, out var p, out var f)) break;
                if (RectDepth(p, f, c.halfL + 0.3f, c.halfW + 0.3f, o.pos, o.fwd, o.halfL, o.halfW) > 0f) first = -1f;
                else if (first < 0f) first = x;
            }
            return first >= 0f ? first : float.MaxValue;
        }

        /// <summary>Round 3c: how far car c's centre goes along its way before its BODY
        /// (0.15 m round it; its corners swing wide of its centre's line in a sharp turn)
        /// meets o's; MaxValue within <paramref name="look"/> metres.</summary>
        float SweptHit(Car c, Car o, float look)
        {
            for (float x = 0f; x <= look; x += 0.5f)
            {
                if (!WayPose(c, x, out var p, out var f)) break;
                if (RectDepth(p, f, c.halfL + 0.15f, c.halfW + 0.15f, o.pos, o.fwd, o.halfL, o.halfW) > 0f) return x;
            }
            return float.MaxValue;
        }

        /// <summary>Where car c's centre is, and its heading, x metres on along its plan.</summary>
        bool WayPose(Car c, float x, out Vector2 p, out Vector2 f)
        {
            float u = c.u + x;
            for (int k = 0; k < c.legCount; k++)
            {
                ref Leg L = ref c.legs[k];
                if (u > L.len && k < c.legCount - 1) { u -= L.len; continue; }
                u = Mathf.Min(u, L.len);
                if (L.turn)
                {
                    p = Bez(L, L.len > 0.01f ? u / L.len : 1f, out var d);
                    f = d.sqrMagnitude > 1e-6f ? d.normalized : c.fwd;
                    return true;
                }
                var e = map.edges[L.edge];
                float s = Mathf.Clamp(L.s0 + L.dir * u, 0f, e.length);
                p = PlanPoint(e, s, k == 0 ? c.lat : LaneLat(e, L.dir, L.laneIn, s), out var tan);
                f = tan * L.dir;
                return true;
            }
            p = c.pos; f = c.fwd;
            return false;
        }

        /// <summary>Of two cars locked in a box, is c further through it (more of
        /// its turn behind it; then the lower id)?</summary>
        static bool FurtherThrough(Car c, Car o)
        {
            float fc = c.legCount > 0 && c.legs[0].turn && c.legs[0].len > 0.1f ? c.u / c.legs[0].len : 0f;
            float fo = o.legCount > 0 && o.legs[0].turn && o.legs[0].len > 0.1f ? o.u / o.legs[0].len : 0f;
            if (Mathf.Abs(fc - fo) > 0.05f) return fc > fo;
            return c.id < o.id;
        }

        /// <summary>Room to back 0.8 m: no car (nor the player) where its body would go.</summary>
        bool RoomBehind(Car c, Car from, float m = 0.8f)
        {
            // where it would be, m back along its own way (a turn's arc swings its body
            // as it backs): clear of every car round it, not only one behind it
            var back = c.pos - c.fwd * m;
            var bf = c.fwd;
            if (c.legCount > 0 && c.legs[0].turn && c.legs[0].len > 0.01f)
            {
                back = Bez(c.legs[0], Mathf.Max(0f, c.u - m) / c.legs[0].len, out var bd);
                if (bd.sqrMagnitude > 1e-6f) bf = bd.normalized;
            }
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o == from || (o.pos - back).sqrMagnitude > 81f) continue;
                if (RectDepth(back, bf, c.halfL + 0.3f, c.halfW + 0.2f, o.pos, o.fwd, o.halfL, o.halfW) > 0f) return false;
            }
            if (Vector2.Dot(playerPos - c.pos, c.fwd) < 0f && (playerPos - back).sqrMagnitude < (c.halfL + 3f) * (c.halfL + 3f)) return false;
            return true;
        }

        /// <summary>How deep two rectangles (centre, heading, half length, half width) overlap; 0 apart.</summary>
        static float RectDepth(Vector2 pa, Vector2 fa, float la, float wa, Vector2 pb, Vector2 fb, float lb, float wb)
        {
            float best = float.MaxValue;
            Vector2 ra = new Vector2(-fa.y, fa.x), rb = new Vector2(-fb.y, fb.x);
            for (int k = 0; k < 4; k++)
            {
                Vector2 ax = k == 0 ? fa : k == 1 ? ra : k == 2 ? fb : rb;
                float ea = Mathf.Abs(Vector2.Dot(fa, ax)) * la + Mathf.Abs(Vector2.Dot(ra, ax)) * wa;
                float eb = Mathf.Abs(Vector2.Dot(fb, ax)) * lb + Mathf.Abs(Vector2.Dot(rb, ax)) * wb;
                float ov = ea + eb - Mathf.Abs(Vector2.Dot(pb - pa, ax));
                if (ov <= 0f) return 0f;
                best = Mathf.Min(best, ov);
            }
            return best;
        }

        /// <summary>Round 3c: when it came into the box it is in (boxJ).</summary>
        static void BoxSince(Car c)
        {
            if (c.boxJ != c.boxSinceJ) { c.boxSinceJ = c.boxJ; c.boxSince = Time.time; }
        }

        /// <summary>How near the way a car takes another's body may pass: its half
        /// width and half a metre standing, a tenth of a metre more per m/s (round 3b,
        /// 2026-10-29 check: a car follows its planned arc to within a metre, not half
        /// of one - a straight-on at 3.8 m/s and one behind at 7 m/s met a left
        /// turner standing in the box at node 7681 the plan passed 0.5 m clear of;
        /// standing pairs keep the half metre, so their locks stay undone).</summary>
        static float PathReach(Car c) => c.halfW + 0.5f + 0.1f * Mathf.Clamp(c.speed, 0f, 8f);

        /// <summary>Is car o's body in the way car c takes through the box
        /// (the turn T from <paramref name="t0"/> of it on, and 8 m on past it,
        /// c's half width and half a metre either side)?</summary>
        static bool InTheWay(Car c, in Leg T, Car o, float t0 = 0f)
        {
            var right = new Vector2(o.fwd.y, -o.fwd.x);
            int n = Mathf.Clamp(Mathf.CeilToInt(T.len * (1f - t0)), 6, 40);
            float reach = PathReach(c);
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
            // TIGHT SPLITS (round 3d): a split whose leaving carriageway runs within a body of
            // the arriving one's lane end is a junction after all - the arriving carriageway
            // yields there, at a line set back to where its cars stand clear, and keeps the box
            tightSplit = new bool[nn];
            holdS = new float[map.edges.Length * 2];
            for (int i = 0; i < holdS.Length; i++) holdS[i] = float.NaN;
            var sb = new System.Text.StringBuilder();
            TightSplits = 0;
            for (int i = 0; i < nn; i++)
            {
                if (degree[i] < 3) continue;
                if (nodeJunction[i] >= 0 && junctionSignal[nodeJunction[i]]) { if (splitNode[i]) tightWhy[i] = "the signal's"; continue; }
                // a split in shape, whatever its names and classes (North Tryon / North Church at
                // node 9907): a two-way, a one-way arriving and a one-way leaving on its far side
                // (the arms by their shape: SplitArms' own come from the data's SPLT records)
                if (!SplitShape(i, out int tw, out int ia, out int oa)) { if (splitNode[i]) tightWhy[i] = "not a split's shape"; continue; }
                if (approachOf.ContainsKey((ia << 1) | 1)) { tightWhy[i] = "the arriving carriageway has a sign"; continue; }
                if (!TightSplit(i, tw, ia, oa, out float sh)) continue;
                tightSplit[i] = true; splitNode[i] = false;
                holdS[(ia << 1) | 1] = sh;
                majorA[i] = tw; majorB[i] = -1;
                if (TightSplits++ < 40) sb.Append(" n").Append(i).Append(" e").Append(ia).Append(" hold ").Append((map.edges[ia].length - sh).ToString("0.0")).Append(" m");
            }
            Debug.Log($"[CityTraffic] tight splits (the arriving carriageway yields at a line set back): {TightSplits}:{sb}");
        }

        /// <summary>
        /// A TIGHT SPLIT (round 3d, trace 1 at node 9907, North Tryon Street / North Church
        /// Street): CitySignals.SplitArms takes a two-way opening into its one-way pair for a
        /// carriageway split - "not a junction: its flows never cross" - so it has no control,
        /// no box and both flows the way (and the warrants skip it besides: it is 18 m from the
        /// signal at node 10758). But the data lays the leaving carriageway (North Church,
        /// e8228) 2.2 m from the arriving one's lane (North Tryon northbound, e15469) where they
        /// meet the node: the northbound queue from node 10758's red backs through it, its last
        /// car stands with its nose in the node, and the southbound way into North Church runs
        /// within 0.4 m of that nose - a lock nose to nose, undone by backing and creeping with
        /// a touch (trace 1: four touches in 600 s; final_c: crossing-blocked 3, car wrecks 2).
        /// The right of way must be given (MUTCD 2B.10, a YIELD at an entrance where it is
        /// needed): here the arriving carriageway yields to the two-way's flow into the leaving
        /// one, at a line where a car of it stands clear of that flow, and the box is kept clear
        /// (MUTCD R10-7) - the junction's own rules (KeepClearHold, CanGo).
        /// True: a car on the arriving carriageway with its nose at its lane end would stand in
        /// the leaving flow's way (its bodies along the leaving lanes' first 20 m and their
        /// connectors from the two-way's lanes, 0.35 m round it); <paramref name="sHold"/>: the
        /// arc on the arriving edge its nose stays behind (the first place back that is clear).
        /// </summary>
        bool TightSplit(int n, int tw, int ia, int oa, out float sHold)
        {
            sHold = 0f;
            if (ia < 0 || oa < 0 || tw < 0) { tightWhy[n] = "arms"; return false; }
            var A = map.edges[ia]; var O = map.edges[oa]; var T = map.edges[tw];
            if (!A.oneway || A.b != n || !O.oneway || O.a != n || !routable[ia] || !routable[oa] || !routable[tw]) { tightWhy[n] = "not a split's arms"; return false; }
            int dT = T.b == n ? 1 : -1;            // the two-way's flow arriving here (it leaves by O)
            if (dT < 0 && T.oneway) { tightWhy[n] = "one-way"; return false; }
            float aEnd = EndS(A, 1), oStart = Back(O, n), tEnd = EndS(T, dT);
            int nA = Lanes(ia, 1), nO = Lanes(oa, 1), nT = Lanes(tw, dT);
            // THE LANES THE TRAFFIC USES (MapStraight's: a lane that runs on within 2 m): the
            // arriving lanes that run on into the two-way, the leaving lanes the two-way's flow
            // runs on into (North Tryon's e15469 has three lanes and North Church's e8228 two, one
            // each is driven - the others lie over each other); none found, all of them
            int dF = T.a == n ? 1 : -1, nF = Lanes(tw, dF);
            float sF = dF > 0 ? Back(T, n) : T.length - Back(T, n);
            var usedA = new bool[nA];
            bool anyA = false;
            for (int i = 0; i < nA; i++)
            {
                if (LaneWidth(A, 1, i, aEnd) < 2f) continue;
                for (int j = 0; j < nF && !usedA[i]; j++)
                    if (LaneWidth(T, dF, j, sF) >= 2f && LaneJump(A, 1, i, aEnd, T, dF, j, sF, n) <= 2f) usedA[i] = true;
                anyA |= usedA[i];
            }
            if (!anyA) for (int i = 0; i < nA; i++) usedA[i] = LaneWidth(A, 1, i, aEnd) >= 2f;
            var usedO = new bool[nO];
            var pair = new bool[nT, nO];
            bool anyO = false;
            for (int t = 0; t < nT; t++)
                for (int j = 0; j < nO; j++)
                    if (LaneWidth(T, dT, t, tEnd) >= 2f && LaneWidth(O, 1, j, oStart) >= 2f && LaneJump(T, dT, t, tEnd, O, 1, j, oStart, n) <= 2f)
                    { pair[t, j] = true; usedO[j] = true; anyO = true; }
            if (!anyO) for (int t = 0; t < nT; t++) for (int j = 0; j < nO; j++) { pair[t, j] = true; usedO[j] = true; }
            const float hl = 2.4f, hw = 1.0f, pad = 0.35f;
            var pts = new System.Collections.Generic.List<Vector2>();
            var dirs = new System.Collections.Generic.List<Vector2>();
            for (int j = 0; j < nO; j++)
            {
                if (!usedO[j]) continue;
                for (float x = 0f; x <= 20f && oStart + x <= O.length; x += 1f)
                { pts.Add(LanePos(O, 1, j, oStart + x)); dirs.Add(TravelTan(O, 1, oStart + x)); }
                Vector2 q = LanePos(O, 1, j, oStart);
                for (int t = 0; t < nT; t++)
                {
                    if (!pair[t, j]) continue;
                    Vector2 p0 = LanePos(T, dT, t, tEnd), seg = q - p0;
                    if (seg.sqrMagnitude < 0.01f) continue;
                    for (int k = 0; k <= 6; k++) { pts.Add(p0 + seg * (k / 6f)); dirs.Add(seg.normalized); }
                }
            }
            bool Clear(float s)
            {
                float sc = Mathf.Max(0f, s - hl);
                for (int i = 0; i < nA; i++)
                {
                    if (!usedA[i]) continue;
                    Vector2 c = LanePos(A, 1, i, sc), f = TravelTan(A, 1, sc);
                    for (int k = 0; k < pts.Count; k++)
                        if ((pts[k] - c).sqrMagnitude < 64f && RectDepth(c, f, hl + pad, hw + pad, pts[k], dirs[k], hl, hw) > 0f) return false;
                }
                return true;
            }
            if (Clear(aEnd)) { tightWhy[n] = "clear at its lane end"; return false; }   // a split as drawn
            // a THROAT: clear a few metres back (the carriageways part there); two carriageways
            // laid within a body of each other for longer are a road of their own, not this
            for (float back = 0.5f; back <= TightBackM; back += 0.5f)
                if (aEnd - back > 1f && Clear(aEnd - back)) { sHold = aEnd - back; tightWhy[n] = $"tight: clear {back:0.0} m back of its lane end"; return true; }
            tightWhy[n] = $"no clear place within {TightBackM:0} m";
            return false;
        }

        /// <summary>Why TightSplit decided as it did, per split-shaped node (the trace's facts).</summary>
        readonly System.Collections.Generic.Dictionary<int, string> tightWhy = new System.Collections.Generic.Dictionary<int, string>();

        /// <summary>How far back of its lane end a tight split's arriving carriageway may be held.</summary>
        const float TightBackM = 12f;

        /// <summary>A node in a split's shape whatever its names and classes: three arms, a
        /// two-way, a one-way arriving and a one-way leaving, both one-ways on the two-way's far
        /// side (within 60 degrees of its line).</summary>
        bool SplitShape(int n, out int tw, out int ia, out int oa)
        {
            tw = ia = oa = -1;
            int arms = 0;
            foreach (int ei in map.nodeEdges[n])
            {
                var e = map.edges[ei];
                if (e.a == e.b || !routable[ei]) continue;
                arms++;
                if (!e.oneway) { if (tw >= 0) return false; tw = ei; }
                else if (e.b == n) { if (ia >= 0) return false; ia = ei; }
                else { if (oa >= 0) return false; oa = ei; }
            }
            if (arms != 3 || tw < 0 || ia < 0 || oa < 0) return false;
            Vector2 back = -ArmOut(map.edges[tw], n);
            return Vector2.Angle(ArmOut(map.edges[ia], n), back) < 60f && Vector2.Angle(ArmOut(map.edges[oa], n), back) < 60f;
        }

        /// <summary>Is node n a junction box (three roads or more, not a
        /// carriageway split, not a mitred bend)?</summary>
        bool IsBox(int n) => degree[n] >= 3 && !splitNode[n] && (!trims.mitre[n] || (tightSplit != null && tightSplit[n]));

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
