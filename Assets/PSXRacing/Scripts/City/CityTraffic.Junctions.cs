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

        const float GapRightS = 4.5f, GapThroughS = 5.0f, GapLeftS = 6.0f, GapMergeS = 3.0f;
        /// <summary>The why (Drive) of a car held short of a full box.</summary>
        const byte WhyKeepClear = 7;

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
            if ((playerPos - at).sqrMagnitude < 20f * 20f && Mathf.Abs(playerPos3.y - c.y) < LevelM) return false;
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked) continue;
                if (o.boxJ == jk && o.inKey != key) return false;
                if (o.appJ != jk || o.appKey == key || o.appIdx < 0 || o.appCtl != CtlSignal) continue;
                if (CitySignals.AspectOf(apps[o.appIdx].channel, now) == CitySignals.Aspect.Red) continue;
                float cross = u.x * o.appU.y - u.y * o.appU.x;
                bool fromLeft = cross < -0.3f;
                bool oncomingLeft = Vector2.Dot(o.appU, u) < -0.7f && o.turnNext == -1;
                if (!fromLeft && !oncomingLeft) continue;
                float t = o.appDist / Mathf.Max(o.speed, 0.5f);
                if ((o.speed > 1f && t < GapRightS) || (o.appDist < 12f && o.appDist > -2f)) return false;
            }
            return true;
        }

        /// <summary>A car in the box that came in on a phase now over (it is
        /// clearing it): a fresh green waits for it.</summary>
        bool ClearingInBox(Car c, int jk, int key, Vector2 u, double now)
        {
            for (int i = 0; i < live.Count; i++)
            {
                var o = live[i];
                if (o == c || o.wrecked || o.boxJ != jk || o.inKey == key || o.speed < 0.5f) continue;
                if (Mathf.Abs(Vector2.Dot(o.boxU, u)) >= 0.6f) continue;
                // came in on a signal approach whose phase is over (a car from an
                // arm with no head, or one standing in the box, is the physics' to avoid)
                if (!approachOf.TryGetValue(o.inKey, out int ai) || !apps[ai].signal ||
                    CitySignals.AspectOf(apps[ai].channel, now) == CitySignals.Aspect.Green) continue;
                return true;
            }
            return false;
        }
    }
}
