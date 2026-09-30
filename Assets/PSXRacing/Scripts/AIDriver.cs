using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Waypoint-following opponent: steers toward a speed-scaled lookahead point,
    /// sets corner speed from baked curvature, brakes ahead of slow corners, and
    /// gives way to cars it is about to drive through.
    ///
    /// Runs on FixedUpdate. It writes CarController's input fields, which
    /// CarController consumes on the physics step, and it reads physics state
    /// (forwardSpeed, rearSlipAngle) that only changes there. On Update the AI
    /// re-read the same tick's state twice at high frame rates and skipped ticks
    /// entirely at low ones, so how hard the field drove depended on the
    /// player's frame rate.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AIDriver : MonoBehaviour
    {
        public TrackPath path;
        [Range(0.8f, 1.05f)] public float skill = 0.95f;
        public float lateralOffset = 0f;   // keeps AI cars off each other's line

        CarController car;
        CollisionResponder responder;
        int nearestIdx;
        float stuckTimer;
        float wrongWayTimer;
        float avoidBias;                   // smoothed metres of give-way
        public bool driving = true;

        /// <summary>
        /// Past its own flag, rolling to a stop. Distinct from plain
        /// `!driving`, which is the GRID: a car waiting on the line wants no
        /// steering and a firm brake, and a car that has just crossed at speed
        /// wants the opposite — the road it is on, and enough brake to be
        /// stopped soon without standing the field on its nose.
        /// </summary>
        public bool ShuttingDown { get; private set; }

        /// <summary>Brake a finisher carries until it is nearly stopped. Under
        /// the grid's 0.4 on purpose: this is a slowing-down lap, not a panic
        /// stop, and a car that hauls up in its own length is a car the player
        /// — who still has their own throttle past the flag — drives into.</summary>
        const float ShutdownBrake = 0.22f;
        /// <summary>A retired car's brake: out of the race, it stops.</summary>
        const float RetiredBrake = 0.6f;
        /// <summary>How far past the tarmac edge a retired car parks its
        /// centre: over the verge strip, clear of the lane.</summary>
        const float RetiredVergeM = 0.8f;
        /// <summary>Below this the shutdown hands over to the grid pose and the
        /// car is simply held where it stopped.</summary>
        const float ShutdownRestMps = 1.5f;

        /// <summary>Take the car off the race and roll it to a stop down the
        /// road. Called by <see cref="RaceManager"/> when this car finishes.</summary>
        public void ShutDown()
        {
            driving = false;
            ShuttingDown = true;
        }

        /// <summary>
        /// Lock on the road: the steer input that chases a speed-scaled
        /// lookahead point on the path, offset onto this car's own line.
        ///
        /// Its own method so the shutdown steers exactly as the race does
        /// rather than with a second, nearly-identical copy of it — the thing
        /// that eventually disagrees.
        /// </summary>
        float SteerToLine(float speed)
        {
            float lookDist = 7f + speed * 0.45f;
            int lookIdx = nearestIdx + Mathf.Max(2, Mathf.RoundToInt(lookDist / path.spacing));
            Vector3 target = path.GetPoint(lookIdx);
            Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(lookIdx));
            // THE CHORD CUTS THE CORNER. Chasing a point this far up the road
            // runs a line inside the bend by about lookDist x turn / 8 - on a
            // mountain left-hander, 1.3-2.7 m: out of this car's lane and over
            // the centreline into oncoming traffic. On a two-way road the
            // point moves out by that much on a LEFT-hander (a right-hander's
            // inside is the shoulder, which the edge already holds).
            float cutBack = 0f;
            var ts = TrafficSystem.Instance;
            if (ts != null && ts.TwoWay)
            {
                float turn = Vector3.SignedAngle(path.GetTangent(nearestIdx), path.GetTangent(lookIdx), Vector3.up) * Mathf.Deg2Rad;
                // Only for the circuits' steering law below: pure pursuit, which
                // two-way roads use now, holds a steady bend with no offset.
                if (turn < 0f && !PurePursuit) cutBack = Mathf.Min(MaxCutBackM, lookDist * -turn / 8f);
            }
            // HOLDING THE MARK: the point-chase settles off it on a long bend
            // (NC 226A: aimed at +1.7, sat at +0.6..+1.0 for three seconds on
            // a left-hander, into an oncoming Audi), and a car that PULLS - a
            // bad-alignment fault is up to 0.25 of lock - sits off it on a
            // straight (Gillespie: +0.17 of steer held for three seconds just
            // to go straight, half a metre over, into an oncoming Crown Vic).
            // What error lasts, the wheel takes up: steer, not aim, because a
            // pull is a steer offset at every speed. Slowly, leaking, and only
            // near the mark, so a pass's own swing across the lane never winds
            // it up.
            float trim = 0f, laneErr = 0f;
            if (ts != null && ts.TwoWay)
            {
                Vector3 pr = Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized;
                float err = laneErr = lineOffset + avoidBias - Vector3.Dot(transform.position - path.GetPoint(nearestIdx), pr);
                float dt = Time.deltaTime;
                laneInt *= 1f - LaneLeak * dt;
                if (Mathf.Abs(err) < LaneIntBandM && speed > 5f) laneInt += err * dt;
                laneInt = Mathf.Clamp(laneInt, -LaneIntMax, LaneIntMax);
                trim = LaneKi * laneInt;
            }
            else laneInt = 0f;
            target += right * (lineOffset + avoidBias + cutBack);

            Vector3 local = transform.InverseTransformPoint(target);
            float steerCmd;
            if (ts != null && ts.TwoWay && PurePursuit)
            {
                // PURE PURSUIT: the wheel angle that arcs the car through the
                // aim point, atan(2 wb sin(a) / L), in units of the lock this
                // speed allows. The old law (1.4 x the angle, in units of lock)
                // turned the wheel 1.8-2x that at 60-110 km/h because the lock
                // shrinks with speed: the car arced INSIDE every bend - over the
                // centreline on a left-hander, which the cut-back only patched.
                // Pure pursuit on a steady bend settles ON the line.
                float ld = Mathf.Max(new Vector2(local.x, local.z).magnitude, 1f);
                float alpha = Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f));
                float wheelDeg = Mathf.Atan(2f * car.wheelbase * Mathf.Sin(alpha) / ld) * Mathf.Rad2Deg;
                steerCmd = PursuitGain * wheelDeg / Mathf.Max(car.CurrentMaxSteerDeg, 1f) + trim;
            }
            else steerCmd = Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f)) * 1.4f + trim;
            // LANE DISCIPLINE on a two-way road: damp the drift across it. The
            // point-chase alone let a car weave +-1 m round its line for
            // seconds after a knock (Mount Mitchell: +0.8, -0.6, +1.6 m in two
            // seconds with the line held at +1.1) - in a 3 m lane, across the
            // centre line into an oncoming Camry. Off on closed circuits,
            // whose racing line this would change.
            if (ts != null && ts.TwoWay && speed > 5f)
            {
                var rb = car.Body;
                if (rb != null)
                {
                    Vector3 pr = Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized;
                    float vLat = Vector3.Dot(rb.linearVelocity, pr);
                    // Damped toward the drift the mark ASKS for, not toward none:
                    // against all drift it fought the car's own passes, and the
                    // autopilot crossed a lane at 0.5 m/s into the Transit it
                    // was going round (Mount Mitchell).
                    // Toward the centreline with something coming (the
                    // leftLimit is live): no faster than LeftDriftOncomingMps.
                    // At the full 3 m/s a racer moving over for a pass under
                    // hard braking overshot its mark by a metre, over the
                    // line into an oncoming Land Rover (Gillespie Gap, heavy).
                    float leftCap = float.IsNegativeInfinity(DebugLeftLimit) ? LaneMaxDriftMps : LeftDriftOncomingMps;
                    float vWant = Mathf.Clamp(laneErr * LaneCloseRate, -leftCap, LaneMaxDriftMps);
                    steerCmd -= LaneDamp * (vLat - vWant) / speed;
                    // YAW DAMPING: turning faster or slower than the road
                    // does is steered against. The point-chase fixes WHERE
                    // the car is, not which way it is rotating, so a loose car
                    // (Charger, Blue Ridge, 110 km/h on a straight) sawed the
                    // wheel +-0.8 into a 17 deg tank-slapper, off the verge
                    // and back across its lane into traffic.
                    int ahead = Mathf.Max(1, Mathf.RoundToInt(8f / path.spacing));
                    float roadYaw = Vector3.SignedAngle(path.GetTangent(nearestIdx), path.GetTangent(nearestIdx + ahead), Vector3.up)
                                    * Mathf.Deg2Rad / (ahead * path.spacing) * speed;
                    steerCmd -= YawDamp * (rb.angularVelocity.y - roadYaw);
                }
            }
            return Mathf.Clamp(steerCmd, -1f, 1f);
        }

        // ---- proximity (P2) ----
        /// <summary>How far up the road the AI looks for a car to avoid. Beyond
        /// this it is not closing on anyone, it is just racing.</summary>
        const float AvoidLookM = 14f;
        /// <summary>Lateral half-window. Wider than the cars are (1.72 m) so the
        /// AI starts easing over before the panels actually line up.</summary>
        const float AvoidWidthM = 2.6f;
        /// <summary>Metres of lane the AI will give up. Deliberately under half
        /// the 12 m road: this is "don't drive through the player", not an
        /// overtaking line, and a bigger number walks the AI into the barrier.
        /// </summary>
        const float AvoidMaxM = 2.4f;
        const float AvoidSlew = 4.0f;      // metres/s of give-way movement
        /// <summary>...and round TRAFFIC, which is met at a closing speed a
        /// racer is not: a 5 m move at 4 m/s takes 30 m at 25 m/s closing.</summary>
        const float TrafficSlew = 6.5f;
        /// <summary>Centre-to-centre gap wanted beside a traffic car: two
        /// half-widths and a little air. What the racer loop and the oncoming
        /// keep-away still use; a PASS is planned round the widths that are
        /// actually there (<see cref="PassAir"/>).</summary>
        const float TrafficClearM = 2.4f;
        /// <summary>
        /// PASSING IN TRAFFIC (owner, 2026-09-30: "AI really struggles to pass
        /// when there is heavy traffic (partially because they refuse to drive
        /// around the right side of the car using the shoulder)"). The pass
        /// lines were a fixed 2.9 m either side of the car being passed and
        /// had to keep the racer's centre 1.15 m inside the tarmac: on a 10.5 m
        /// circuit that refused the right-hand side with 2.5 m of tarmac beside
        /// the traffic car, and on a 6.4 m mountain road it refused the room a
        /// car pulled onto the verge had just made. Now: this car's half-width,
        /// the other's, and this much air between the two sides - more the
        /// faster it goes by.
        /// </summary>
        const float PassAirMinM = 0.3f, PassAirPerMps = 0.012f, PassAirMaxM = 0.8f;
        static float PassAir(float closing) =>
            Mathf.Clamp(PassAirMinM + PassAirPerMps * Mathf.Max(closing, 0f), PassAirMinM, PassAirMaxM);
        /// <summary>A car counts as in this one's corridor when their sides are
        /// nearer than this - the pass air and the lane-holding error both
        /// (0.35 let a racer back on its line from a pass run on at 115 km/h
        /// into a Crown Vic pulled onto the verge 0.27 m clear of that line,
        /// having not seen it as in the way at all: Blue Ridge, rush hour).</summary>
        const float CorridorAirM = 0.7f;
        /// <summary>Out past a pass line already (coming back from the last
        /// car, or out in the other lane), a racer holds where it is rather
        /// than swinging back onto the line beside the car being passed - by
        /// at most this much further out than the line. The swing back at
        /// 118 km/h overshot the line by 0.6 m into the car's rear quarter.</summary>
        const float PassHoldOutM = 0.8f;
        /// <summary>Tarmac left between this car's side and the edge of the
        /// road on an ordinary line.</summary>
        const float EdgeAirM = 0.15f;
        /// <summary>How far past the tarmac edge a pass on the RIGHT may put
        /// this car's outer side: onto the paved verge strip, a little short
        /// of its edge (TrafficSystem.VergeUseM is the traffic's own figure).
        /// Never further: past it is grass, a ditch, or a guard wall.</summary>
        const float VergeSideM = 0.8f;
        /// <summary>The SOLID layer (walls, rock faces, trunks, tunnel tubes)
        /// the shoulder is checked against before a pass goes onto it.</summary>
        const int SolidMask = 1 << 9;
        /// <summary>ALONGSIDE (UpdateAvoidance): a car further back than
        /// AlongsideLevelM and nearer this one's line than AlongsideInLineM (two
        /// cars side by side stand a body width apart, 1.7-1.9 m) is behind it
        /// in line, following, and is no reason to hold a line or brake.</summary>
        const float AlongsideLevelM = 1f, AlongsideInLineM = 1.4f;
        /// <summary>Under this speed the throttle is not eased for a slide:
        /// a crawl's slip angle is noise (see the steering loop).</summary>
        const float SlideEaseMinMps = 4f;
        /// <summary>How fast the stuck and crawl clocks run while the car is
        /// waiting on traffic it cannot pass (UpdateRecovery): a fifth, so a
        /// wait of up to 20 s is the car's own.</summary>
        const float WaitingClockRate = 0.2f;
        /// <summary>How long a give-way counts as waiting after its last tick
        /// (the recovery's clocks), and the speed under which a car does not
        /// brake to drop in behind a car alongside - it is not going anywhere
        /// to drop back from.</summary>
        const float WaitHoldSeconds = 1f, DropBehindMinMps = 3f;
        float waitHold;
        /// <summary>How close to the tarmac edge a pass may take the car's
        /// centre: a half-width and a little shoulder.</summary>
        const float EdgeMarginM = 1.15f;
        /// <summary>The nearest any steering target may put the car's centre to
        /// the tarmac edge: a half-width and a little.</summary>
        const float TarmacKeepM = 1.0f;
        /// <summary>Metres behind a traffic car a racer that cannot pass holds
        /// station at, plus this many seconds of its own speed.</summary>
        const float FollowGapM = 7f, FollowTimeS = 0.45f;
        /// <summary>The deceleration a full brake input gives, about - for
        /// turning a stopping distance into a pedal.</summary>
        const float BrakeDecel = 8.5f;
        /// <summary>The share of it a stop behind traffic is planned on.</summary>
        const float PlanBrakeShare = 0.65f;
        /// <summary>How far up the road oncoming traffic keeps a racer in its
        /// own lane, and how near the centreline its centre may come then (a
        /// half-width and a little).</summary>
        const float OncomingGuardM = 130f, OwnLaneInnerM = 1.0f;
        /// <summary>For the race harness: the line, the give-way offset and
        /// the limit on it this tick (-infinity when nothing is coming).</summary>
        public float DebugLine => lineOffset;
        public float DebugBias => avoidBias;
        public float DebugLeftLimit { get; private set; } = float.NegativeInfinity;
        /// <summary>This tick's give-way: the throttle lift and the brake
        /// UpdateAvoidance asked for (the race play check's trail).</summary>
        public float DebugLift { get; private set; }
        public float DebugTrafficBrake { get; private set; }

        /// <summary>Steer against sideways drift across the road (per unit of
        /// drift angle), on two-way roads.</summary>
        const float LaneDamp = 2.2f;
        /// <summary>The drift across the road asked for per metre off the
        /// mark, and its most.</summary>
        const float LaneCloseRate = 1.2f, LaneMaxDriftMps = 3f;
        /// <summary>Leftward drift and mark slew (m/s) while oncoming traffic
        /// holds the car to its own lane (see SteerToLine, UpdateAvoidance).</summary>
        const float LeftDriftOncomingMps = 1.5f, LeftSlewOncomingMps = 2.0f;
        /// <summary>Steer per rad/s of yaw the road does not ask for.</summary>
        const float YawDamp = 0.8f;
        /// <summary>Lane-error integral: steer per metre-second of error, its
        /// leak per second, its cap (0.1 x 3.5 = 0.35 of lock, more than the
        /// worst alignment fault), and the error band it grows in.</summary>
        const float LaneKi = 0.1f, LaneLeak = 0.15f, LaneIntMax = 3.5f, LaneIntBandM = 1.5f;
        float laneInt;

        /// <summary>Two-way roads steer by pure pursuit (SteerToLine), a touch
        /// over geometric for the tyres' own slip.</summary>
        const bool PurePursuit = true;
        const float PursuitGain = 1.15f;

        /// <summary>The most the steering point moves out on a left-hander.</summary>
        const float MaxCutBackM = 2.5f;

        /// <summary>Is an ONCOMING traffic car within <paramref name="reach"/>
        /// metres up the road - or within OncomingGuardS seconds of closing,
        /// if that is further?</summary>
        bool OncomingAhead(TrafficSystem ts, float reach)
        {
            var bodies = ts.Obstacles;
            float mine = Mathf.Max(car.forwardSpeed, 0f);
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null || rb.useGravity) continue;               // a wreck is not coming
                float theirs = -Vector3.Dot(rb.linearVelocity, transform.forward);
                // A car of the ONCOMING stream counts stopped: a queue in the
                // other lane is a lane that is not free, and it moves off
                // again (Gillespie, rush hour: a pass began beside a queued
                // Transit, the queue moved, 16 m/s head-on; another racer
                // out passing met a stopped Camry at 39 m/s).
                bool otherStream = i < ts.ObstacleDir.Count && ts.ObstacleDir[i] < 0;
                if (theirs < 1f && !otherStream) continue;
                theirs = Mathf.Max(theirs, 0f);
                Vector3 local = transform.InverseTransformPoint(rb.position);
                if (local.z > -3f && local.z < Mathf.Max(reach, (mine + theirs) * OncomingGuardS)) return true;
            }
            return false;
        }
        /// <summary>Seconds of closing an oncoming car must be away before a
        /// racer may be over its own lane's inner edge.</summary>
        const float OncomingGuardS = 5f;

        // ---- serious crashes (2026-09-26) ----
        /// <summary>
        /// "One serious crash can take a car out of racing condition." A
        /// single hard hit at this closing speed into what the car hit (m/s,
        /// the normal component: 61 km/h square into a wall or a car), or this
        /// much damage over the race, and an AI car RETIRES: it coasts to the
        /// side, drops to the back of the order as DNF, and is never respawned.
        /// </summary>
        public const float RetireHitMps = 17f;
        public const float RetireDamage = 95f;
        /// <summary>Out of the race (RaceManager.RetireCar).</summary>
        public bool Retired { get; private set; }

        /// <summary>
        /// How much more it takes to retire this car now. The owner, 2026-09-27:
        /// "Maybe 50% of races a racer gets damaged to the point of exiting ...
        /// It can happen early, but there is more tension if it happens later.
        /// Either way, all three opponents getting knocked out in the first half
        /// mile is extremely disappointing." So a car shrugs off more in the
        /// opening part of a race (x1.35 until 15% of the distance, back to x1
        /// by 60%), and every rival already out makes the rest harder to lose
        /// (x1.3 each): one retirement is drama, three are a farce.
        /// </summary>
        float RetireToughness()
        {
            var rm = RaceManager.Instance;
            if (rm == null) return 1f;
            float early = Mathf.Lerp(RetireEarlyGrace, 1f, Mathf.InverseLerp(0.15f, 0.6f, rm.RaceFraction(car)));
            return early * (1f + RetireEachOut * rm.RetiredCount);
        }
        const float RetireEarlyGrace = 1.35f, RetireEachOut = 0.3f;
        /// <summary>The worst hit already judged (see the retire check).</summary>
        float judgedHit;

        // ---- driver error (2026-09-27) ----
        /// <summary>
        /// "Maybe 50% of races a racer gets damaged to the point of exiting" -
        /// once the field stopped running into oncoming traffic, it stopped
        /// crashing at all (0 retirements in six full races). RaceManager rolls
        /// once per race and hands one rival a point in it (later more likely);
        /// from there, at the next real corner, it carries too much speed in
        /// and turns too little for a few seconds. A solid hit in that window
        /// and it is out; if it gets away with it (open run-off), it tries
        /// again a little further on, three times at most.
        /// </summary>
        public void PlanMistake(float atFraction) { mistakeAt = atFraction; mistakeTries = 0; }
        public bool MakingMistake => Time.time < mistakeUntil;
        float mistakeAt = -1f, mistakeUntil = -1f, mistakeDamage0;
        int mistakeTries;
        // Near-straight on: the cars hold far more than the AI's own corner
        // estimate, and at 35% steering 14 of 14 forced mistakes still made
        // the corner. A race-ending error is the car that goes straight on.
        const float MistakeSeconds = 3.5f, MistakeSteer = 0.1f;
        /// <summary>A corner worth overcooking: one this car must shed this
        /// much speed (m/s) for - a real braking point, missed.</summary>
        const float MistakeMinShedMps = 5f;
        /// <summary>Damage taken in the window that ends the race: one hard hit
        /// at about 7 m/s (RetireCar), which in a corner is the barrier.</summary>
        const float MistakeRetireDamage = 11f;
        /// <summary>Metres past the tarmac edge that is OFF the road: the kerb
        /// strip, the shoulder and a car's width.</summary>
        const float MistakeOffRoadM = 3f;

        /// <summary>Run the planned mistake: start it at the first real corner
        /// past its point in the race, and retire the car if it ends in a hit.
        /// True while the car is making it.</summary>
        bool UpdateMistake(float speed, float cornerSafeSpeed, float cornerAtM)
        {
            var rm = RaceManager.Instance;
            if (rm == null || responder == null) return false;
            if (MakingMistake || Time.time < mistakeUntil + 1f)
            {
                // The window and a second after it: a car that slid wide hits
                // the wall a moment after the steering came back.
                // Or OFF THE ROAD at speed: past the shoulder, still doing over
                // 54 km/h. A stage's run-off is graded to be recoverable, so a
                // mistake there hit nothing (17 of 17 on Gillespie Gap and NC
                // 226A) - and a car that leaves a mountain road at that speed
                // is out of the race whether it hits a tree or not.
                Vector3 rr = Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized;
                float offLat = Mathf.Abs(Vector3.Dot(transform.position - path.GetPoint(nearestIdx), rr));
                bool offRoad = offLat > path.roadWidth * 0.5f + MistakeOffRoadM && speed > 15f;
                if ((responder.DamageScore - mistakeDamage0 >= MistakeRetireDamage || offRoad) && !Retired)
                {
                    mistakeAt = -1f;
                    rm.RetireCar(car);
                    return false;
                }
                return MakingMistake;
            }
            if (mistakeAt < 0f || rm.RaceFraction(car) < mistakeAt) return false;
            if (speed - cornerSafeSpeed < MistakeMinShedMps || speed < 15f) return false;
            // Until the car is AT the corner and a little past: from the start of a
            // 200 m braking zone at 140 km/h, 3.5 s ran out before the bend
            // (6 of 6 on NC 226A made it).
            mistakeUntil = Time.time + Mathf.Clamp(cornerAtM / Mathf.Max(speed, 1f) + 2f, MistakeSeconds, 7f);
            mistakeDamage0 = responder.DamageScore;
            mistakeTries++;
            mistakeAt = mistakeTries >= 3 ? -1f : Mathf.Min(0.97f, rm.RaceFraction(car) + 0.06f);
            return true;
        }

        /// <summary>
        /// The line this car races on, metres right of the centreline. On a
        /// closed road, its grid offset. On a road with ONCOMING traffic, its
        /// own lane - the middle of the race direction's lanes, spread a little
        /// by the grid offset - and the other lane only to pass: the grid's
        /// offsets were measured from the centreline, which put two of the
        /// three rivals on the wrong side of it, and on Gillespie Gap the one at
        /// -0.8 m met an oncoming Volvo head-on at 33 m/s eighteen seconds in.
        /// </summary>
        float LineOffset()
        {
            var ts = TrafficSystem.Instance;
            if (ts == null || !ts.TwoWay) return lateralOffset;
            return ts.RaceLanesCentre + Mathf.Clamp(lateralOffset * 0.3f, -0.5f, 0.5f);
        }
        float lineOffset;
        /// <summary>Where a recovery should put this car across the road: its
        /// own lane on a two-way road, the centreline otherwise.</summary>
        public float RecoveryLateral
        {
            get
            {
                var ts = TrafficSystem.Instance;
                return ts != null && ts.TwoWay ? LineOffset() : 0f;
            }
        }

        /// <summary>Retire the car: coast to a stop and stay there.</summary>
        public void Retire()
        {
            Retired = true;
            ShutDown();
        }

        // ---- recovery (P2) ----
        /// <summary>Stuck in clear air — probably facing a kerb or in the scenery.
        /// The long timer is deliberate: a car crawling out of a hairpin is not
        /// stuck, and teleporting it would look worse than the crawl.</summary>
        const float StuckSeconds = 4f;
        /// <summary>Stuck WHILE grinding a barrier. A pinned car never recovers on
        /// its own — the wall takes the speed as fast as the engine makes it — so
        /// waiting the full four seconds just leaves a car parked on the racing
        /// line for four seconds.</summary>
        const float PinnedSeconds = 1.5f;
        /// <summary>Facing back down the road. Respawn rather than let the AI
        /// drive a lap the wrong way: the steering chases a lookahead point, so a
        /// car spun past 90 degrees can chase it around in a circle forever.</summary>
        const float WrongWaySeconds = 2f;
        const float WrongWayDot = -0.3f;
        /// <summary>Road a racer must make in CrawlSeconds or be recovered.</summary>
        const float CrawlMinM = 12f, CrawlSeconds = 8f;
        /// <summary>Which clock ran out last time: stuck, pinned, wrong way, crawling.</summary>
        public string LastRecoveryWhy { get; private set; } = "";
        int crawlIdx;
        float crawlTimer;
        const float WrongWayMinSpeed = 3f;

        // ---- top speed (sense of speed) ----
        /// <summary>The AI's own straight-line ceiling: 68 m/s scaled by skill,
        /// 54-71 m/s (196-257 km/h) across the skill range.</summary>
        const float SkillTopSpeedMps = 68f;
        /// <summary>
        /// And never more than this much over the PLAYER'S car. The field is
        /// drawn from a price band up to 1.65x the player's car, so a 204 km/h
        /// Miata could line up against an S15 the AI would run to 245 on the
        /// straight — and a car that walks past you on a straight is the one
        /// thing that kills the sense of speed stone dead (NWR's MW review
        /// names exactly that failure). 5% over lets a faster car still get a
        /// pass done off a bad shift without ever simply driving away.
        /// </summary>
        public const float PlayerVmaxMargin = 1.05f;

        /// <summary>Straight-line target ceiling for a skill against the
        /// player's spec top speed (m/s); no player cap when that is unknown.</summary>
        public static float TargetSpeedCap(float skill, float playerVmaxMps) =>
            Mathf.Min(SkillTopSpeedMps * skill,
                      playerVmaxMps > 1f ? playerVmaxMps * PlayerVmaxMargin : float.MaxValue);

        void Awake()
        {
            car = GetComponent<CarController>();
            responder = GetComponent<CollisionResponder>();
        }

        void Start()
        {
            if (path != null) nearestIdx = path.NearestIndex(transform.position);
        }

        /// <summary>
        /// Take the path index again from scratch.
        ///
        /// FixedUpdate only ever REFINES the cached index, searching a window
        /// around it — which is right while a car is driving and wrong the
        /// moment something moves it a long way, or turns the list it is an
        /// index into round underneath it. Both happen on a reverse venue:
        /// RaceHandoffApplier flips the waypoints and restages the grid, and
        /// this component's own Start has no defined order against the one that
        /// does it. A car whose Start won that race is holding an index into
        /// the forward list, and the window is 25 waypoints wide.
        /// </summary>
        public void ReseedPath()
        {
            if (path != null) nearestIdx = path.NearestIndex(transform.position);
        }

        void FixedUpdate()
        {
            if (path == null || path.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            nearestIdx = path.NearestIndex(transform.position, nearestIdx);
            lineOffset = LineOffset();

            if (!driving)
            {
                float rolling = Mathf.Abs(car.forwardSpeed);
                bool coasting = ShuttingDown && rolling > ShutdownRestMps;
                // A WRECK PULLS OVER: onto the verge past the right-hand edge,
                // not stopped in its lane - on a 6 m mountain road a car
                // parked in one lane is a roadblock the field queues behind.
                if (Retired)
                    avoidBias = Mathf.MoveTowards(avoidBias,
                        path.roadWidth * 0.5f + RetiredVergeM - lineOffset, 2.5f * dt);
                car.steerInput = coasting ? SteerToLine(rolling) : 0f;
                car.throttleInput = 0f;
                // A wreck pulls up; a finisher rolls down (ShutdownBrake).
                car.brakeInput = coasting ? (Retired ? RetiredBrake : ShutdownBrake) : 0.4f;
                return;
            }

            float speed = Mathf.Abs(car.forwardSpeed);

            // ---- a serious crash ends this car's race ----
            // A HIT is judged once, when it lands, against the toughness of that
            // moment: judged every tick, a knock taken under the early grace
            // retired the car half a minute later, driving fine, the moment the
            // grace wore off. Damage is a running total and is judged as it goes.
            float tough = RetireToughness();
            bool newHit = false;
            if (responder != null && responder.WorstHit > judgedHit)
            {
                newHit = responder.WorstHit >= RetireHitMps * tough;
                judgedHit = responder.WorstHit;
            }
            if (responder != null && (newHit || responder.DamageScore >= RetireDamage * tough))
            {
                RaceManager.Instance?.RetireCar(car);
                if (Retired) return;
            }

            // ---- give way to whatever is about to be hit ----
            UpdateAvoidance(dt, out float throttleLift, out float trafficBrake);
            DebugLift = throttleLift;
            DebugTrafficBrake = trafficBrake;

            // ---- steering: chase a lookahead point ----
            float steer = SteerToLine(speed);

            // ---- target speed from curvature ahead ----
            // Scaled by the weather with the same number the tyres get, so a
            // wet field brakes for the corner it can actually take.
            float mu = 1.0f * skill * Seasons.RoadGripMult;
            float curvNow = Mathf.Max(path.MaxCurvatureAhead(nearestIdx, 6), 0.0005f);
            float cornerSpeed = Mathf.Sqrt(mu * 9.81f / curvNow) * 0.92f;
            // The player's BUILD top speed: DeriveDrag solves the car to reach
            // exactly that on its own gearing, so it is the number the player
            // can actually reach, tuned or not.
            var rmNow = RaceManager.Instance;
            float playerVmax = rmNow != null && rmNow.playerCar != null ? rmNow.playerCar.BuildTopSpeedMps : 0f;
            float targetSpeed = Mathf.Min(cornerSpeed, TargetSpeedCap(skill, playerVmax));

            // Brake early for upcoming slow corners
            float brakeScan = speed * speed / (2f * 6.5f) + 10f;
            // Cap the scan: it grows with speed squared, and letting it run long
            // enough to wrap the whole track pins the AI to the tightest corner
            // anywhere on the circuit.
            int scanCount = Mathf.Min(Mathf.CeilToInt(brakeScan / path.spacing),
                                      Mathf.Min(60, path.Count / 3));
            float curvAhead = Mathf.Max(path.MaxCurvatureAhead(nearestIdx, scanCount), 0.0005f);
            float aheadSpeed = Mathf.Sqrt(mu * 9.81f / curvAhead) * 0.92f;
            targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(aheadSpeed, 9f) + 3f);

            float throttle = 0f, brake = 0f;
            if (speed < targetSpeed - 1f) throttle = 1f;
            else if (speed > targetSpeed + 2f) brake = Mathf.Clamp01((speed - targetSpeed) * 0.25f);
            else throttle = 0.35f;

            // Ease off throttle while sliding - a car that is MOVING. At a
            // crawl the slip angle is the arctangent of centimetres a second
            // (0.1 across over 0.4 along reads 0.24 rad), and the ease held a
            // rival pulling away from a stop at 0.4 throttle and 1-2 km/h up
            // Chimney Rock's 8% switchbacks until the recovery moved it.
            if (speed > SlideEaseMinMps && Mathf.Abs(car.rearSlipAngle) > 0.25f) throttle *= 0.4f;

            // Closing on a car ahead: lift, and brake if the gap is going away
            // fast. Applied after the corner logic so it can only ever slow the
            // AI down, never talk it into more throttle than the corner allows.
            if (throttleLift > 0f)
            {
                throttle *= 1f - Mathf.Clamp01(throttleLift);
                if (throttleLift > 0.7f) brake = Mathf.Max(brake, (throttleLift - 0.7f) * 1.5f);
            }
            // A DRIVER ERROR (PlanMistake): into this corner too fast and not
            // turning enough. Before the traffic brake, which still wins - a
            // mistake is a corner overcooked, not a car driven into another.
            if (UpdateMistake(speed, aheadSpeed, brakeScan))
            {
                steer *= MistakeSteer;
                brake = 0f;
                throttle = 1f;
            }
            // Traffic that cannot be passed yet: the brake it takes to stop
            // behind it (UpdateAvoidance), not a lift that tops out at 0.45.
            if (trafficBrake > 0f)
            {
                throttle = 0f;
                brake = Mathf.Max(brake, trafficBrake);
            }

            car.steerInput = steer;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.handbrakeInput = false;

            // Holding station behind something it cannot pass (UpdateAvoidance
            // asked for the brake or the whole throttle) is waiting, not stuck -
            // for WaitHoldSeconds after the last tick that asked: a car nose to
            // tail with a crawling one flickers in and out of "closing" every
            // tick.
            if (trafficBrake > 0f || throttleLift >= 0.99f) waitHold = WaitHoldSeconds;
            else waitHold = Mathf.Max(0f, waitHold - dt);
            UpdateRecovery(dt, speed, waitHold > 0f);
        }

        /// <summary>
        /// Nudge off the line of any car this one is closing on, and lift when the
        /// gap is shutting. Not overtaking logic — the AI has no notion of a pass,
        /// and pretending otherwise would have it dive for gaps it cannot make.
        /// The goal is only that a car ahead stops being furniture.
        /// </summary>
        struct Obs { public float z, lat, closing, half; public bool oncoming; public Rigidbody rb; }
        /// <summary>The car this racer is passing and the side it chose (-1
        /// left, +1 right): kept while that side stays clear. Chosen afresh
        /// every tick, the side flipped when the NEXT car's lines moved - at
        /// 100 km/h, out on the right of one car, it swung 2 m back left
        /// across its nose (Sunset City, rush hour, three times in a race).</summary>
        Rigidbody passRb;
        int passSide;
        /// <summary>How fast a racer really moves across the road (m/s) - the
        /// lane damper holds drift to 3 - for "will the move be made before
        /// the car is reached". Was the give-way's own slew, 6.5, and a racer
        /// that "would be beside it in time" ran into its boot.</summary>
        const float LateralRateMps = 2.5f;
        readonly System.Collections.Generic.List<Rigidbody> stopped = new System.Collections.Generic.List<Rigidbody>(4);
        readonly System.Collections.Generic.List<float> stoppedHalf = new System.Collections.Generic.List<float>(4);
        /// <summary>Half this car's own width (its collision box), measured once.</summary>
        float myHalfW = -1f;
        float MyHalfW
        {
            get
            {
                if (myHalfW > 0f) return myHalfW;
                myHalfW = HalfWidthOf(car);
                return myHalfW;
            }
        }
        static float HalfWidthOf(CarController c)
        {
            var box = c != null ? c.GetComponent<BoxCollider>() : null;
            if (box == null) return 0.9f;
            return Mathf.Clamp(box.size.x * Mathf.Abs(c.transform.lossyScale.x) * 0.5f, 0.6f, 1.2f);
        }
        /// <summary>For the race harness's trail: which side the pass this tick
        /// is planned on (-1 left, +1 right, 0 none) and whether it uses the
        /// verge.</summary>
        public int DebugPassSide { get; private set; }
        public bool DebugOnVerge { get; private set; }
        /// <summary>Is a race on a venue with a paved verge beside the tarmac
        /// (every stage and circuit; a city street has a kerb and a pavement)?</summary>
        bool HasVerge
        {
            get
            {
                if (hasVerge < 0)
                {
                    var def = TrackCatalog.At(RaceHandoff.TrackIndex);
                    hasVerge = def != null && !def.city ? 1 : 0;
                }
                return hasVerge == 1;
            }
        }
        int hasVerge = -1;

        /// <summary>
        /// Nothing SOLID on the line a right-hand pass would drive along the
        /// shoulder - a guard wall run, a tunnel tube, a rock face, a trunk:
        /// the verge strip is drawn everywhere, but a wall can stand 1.1 m past
        /// the tarmac and a tunnel's bore closer. Four car-sized boxes down the
        /// pass, at body height (the gravel and the ground are not the Solid
        /// layer), on the path's own bends.
        /// </summary>
        bool ShoulderClear(float line, float passEnd)
        {
            float half = MyHalfW + 0.08f;
            for (int k = 0; k < 4; k++)
            {
                float along = Mathf.Lerp(-2f, Mathf.Max(passEnd, 8f), k / 3f);
                int j = nearestIdx + Mathf.RoundToInt(along / path.spacing);
                if (path.HasEnds) j = Mathf.Clamp(j, 0, path.Count - 1);
                Vector3 tan = path.GetTangent(j);
                Vector3 r = Vector3.Cross(Vector3.up, tan).normalized;
                Vector3 c = path.GetPoint(j) + r * line + Vector3.up * 0.9f;
                if (Physics.CheckBox(c, new Vector3(half, 0.4f, 2.2f), Quaternion.LookRotation(tan, Vector3.up),
                                     SolidMask, QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }
        /// <summary>Under this a rival counts as stopped in the road.</summary>
        const float StoppedRacerMps = 4f;
        /// <summary>Closing on a rival ahead faster than this (m/s), it is an
        /// obstacle like traffic (see the stopped list).</summary>
        const float RacerClosingMps = 2f;
        /// <summary>Air added to a pass for the bend it is made in: a car
        /// running the outside of a bend tracks inside its mark on the way in
        /// (Sunset City, rush hour: racers passing on the right of a car on
        /// 15-19 degree left-handers clipped its rear quarter, 1.7 m centre to
        /// centre on a 2.2 m plan).</summary>
        const float BendAirPerDeg = 0.015f, BendAirMaxM = 0.45f;
        readonly System.Collections.Generic.List<Obs> obs = new System.Collections.Generic.List<Obs>(16);

        void UpdateAvoidance(float dt, out float throttleLift, out float trafficBrake)
        {
            throttleLift = 0f;
            trafficBrake = 0f;
            float wanted = 0f;
            float slew = AvoidSlew;
            // ONCOMING TRAFFIC UP THE ROAD: while there is, the give-way below
            // may not take this car across the centreline. It pushed left round
            // the rival ahead whatever was coming, and on Gillespie Gap the
            // fastest rival, in the pack eleven seconds in, met an oncoming BMW
            // head-on at 38.6 m/s.
            float leftLimit = float.NegativeInfinity;
            var twoWay = TrafficSystem.Instance;
            if (twoWay != null && twoWay.TwoWay && OncomingAhead(twoWay, OncomingGuardM))
                leftLimit = OwnLaneInnerM - lineOffset;
            DebugLeftLimit = leftLimit;
            var rm = RaceManager.Instance;
            if (rm != null)
            {
                var others = rm.allCars;
                for (int i = 0; i < others.Count; i++)
                {
                    var other = others[i];
                    if (other == null || other == car) continue;

                    Vector3 local = transform.InverseTransformPoint(other.transform.position);
                    // Only cars AHEAD. Reacting to a car alongside or behind turns
                    // every side-by-side moment into a swerve, and reacting to one
                    // behind hands the lead car's line to whoever is chasing it.
                    if (local.z < 1.5f || local.z > AvoidLookM) continue;
                    if (Mathf.Abs(local.x) > AvoidWidthM) continue;

                    float closeness = 1f - local.z / AvoidLookM;      // 0 far, 1 touching
                    // Push away from the side they are on. Dead ahead resolves
                    // left, arbitrarily but consistently — a car that dithered
                    // between the two would weave.
                    float dir = local.x >= 0f ? -1f : 1f;
                    float pull = dir * AvoidMaxM * closeness;
                    // Held on this side of the centreline with something coming:
                    // what it cannot steer round, it slows for.
                    bool held = pull < leftLimit;
                    if (held) pull = leftLimit;
                    if (Mathf.Abs(pull) > Mathf.Abs(wanted)) wanted = pull;

                    float closing = car.forwardSpeed - other.forwardSpeed;
                    if (closing > 0.5f)
                        throttleLift = Mathf.Max(throttleLift,
                            closeness * Mathf.Clamp01(closing / (held ? 4f : 8f)));
                }
            }

            // TRAFFIC (TrafficSystem), 2026-09-26: "I don't like how often AI
            // crashes into traffic. They should slow down or drive around."
            // It used to be the racers' rule seen from further off: always
            // pass on the LEFT, whatever was coming the other way, and brake
            // only once the lift passed 0.7 - 0.45 of pedal at most, with no
            // stopping distance in it. So a racer swung out round a car in
            // its lane straight into the oncoming one, or ran up the back of
            // it at 20 m/s closing.
            //
            // Now, in the ROAD's frame (metres right of the centreline, the
            // frame the lanes are drawn in): the nearest car in this racer's
            // corridor is the one to deal with; each side of it is a line to
            // pass on if it is on the tarmac and nothing else - oncoming
            // traffic above all - stands in that line before the pass is done;
            // the nearer clear line wins (left first, North American style,
            // when both are); and when neither is clear the racer FOLLOWS -
            // brakes to hold station behind it, with the stopping distance
            // worked out, until a line opens.
            var traffic = TrafficSystem.Instance;
            // A RIVAL STOPPED IN THE ROAD - retired, or crawling out of a
            // spin - is furniture the racer loop above only sees 14 m out,
            // 0.7 s at 70 km/h: NC 226A, a rival passed a retired Supra at 21
            // m/s with 0.9 m between centres. It joins the traffic here, with
            // the look distance, the pass lines and the stopping distance.
            stopped.Clear();
            stoppedHalf.Clear();
            if (rm != null)
                for (int i = 0; i < rm.allCars.Count; i++)
                {
                    var o = rm.allCars[i];
                    if (o == null || o == car || o.Body == null) continue;
                    var op = rm.GetProgress(o);
                    // ...and a rival this car is CLOSING on, up the road: in a
                    // queue behind traffic the one in front brakes, and the
                    // racer loop's 14 m look is 0.6 s at the 24 m/s one came
                    // up on it at (Blue Ridge and Sunset City, rush hour: rivals
                    // into each other and into the player, over the line,
                    // five times in two races). Here it gets the look, the pass
                    // lines and the stopping distance traffic gets.
                    bool closingOnIt = false;
                    if (car.forwardSpeed - o.forwardSpeed > RacerClosingMps)
                    {
                        Vector3 lo = transform.InverseTransformPoint(o.transform.position);
                        closingOnIt = lo.z > 1.5f && lo.z < 110f && Mathf.Abs(lo.x) < 6f;
                    }
                    if ((op != null && op.retired) || Mathf.Abs(o.forwardSpeed) < StoppedRacerMps || closingOnIt)
                    {
                        stopped.Add(o.Body);
                        stoppedHalf.Add(HalfWidthOf(o));
                    }
                }
            DebugPassSide = 0;
            DebugOnVerge = false;
            // Where this car's CENTRE may go: on the tarmac with a little air
            // to the edge on an ordinary line; onto the paved verge only on
            // the right and only to pass (usedVerge, the clamp at the end).
            float myHalf = MyHalfW;
            float roadHalfW = path.roadWidth * 0.5f;
            float tarmacEdge = Mathf.Max(0.5f, roadHalfW - myHalf - EdgeAirM);
            float vergeEdge = HasVerge ? Mathf.Max(tarmacEdge, roadHalfW + VergeSideM - myHalf) : tarmacEdge;
            bool usedVerge = false;
            int nTraffic = traffic != null ? traffic.Obstacles.Count : 0;
            obs.Clear();
            if (nTraffic + stopped.Count > 0)
            {
                Vector3 myRight = Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized;
                float myLat = Vector3.Dot(transform.position - path.GetPoint(nearestIdx), myRight);
                float edge = Mathf.Max(0.5f, path.roadWidth * 0.5f - EdgeMarginM);
                float mySpeed = car.forwardSpeed;
                obs.Clear();
                for (int i = 0; i < nTraffic + stopped.Count; i++)
                {
                    var rb = i < nTraffic ? traffic.Obstacles[i] : stopped[i - nTraffic];
                    if (rb == null) continue;
                    Vector3 local = transform.InverseTransformPoint(rb.position);
                    float along = Vector3.Dot(rb.linearVelocity, transform.forward);
                    float closing = mySpeed - along;
                    // Three seconds of closing, to 110 m: a rival closing at 22
                    // m/s on a car that then brake-checked it (a Braker, 70 m
                    // ahead) saw it 66 m out and hit it (Gillespie Gap, 2026-09-27).
                    float look = Mathf.Clamp(AvoidLookM + Mathf.Max(closing, 0f) * 3.2f, AvoidLookM, 110f);
                    // From 6 m back: a car level with this one is part of the
                    // pass until it is behind (see ALONGSIDE below).
                    if (local.z < -6f || local.z > look) continue;
                    int oi = path.NearestIndex(rb.position, nearestIdx);
                    Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(oi)).normalized;
                    float lat = Vector3.Dot(rb.position - path.GetPoint(oi), r);
                    float oh = i < nTraffic
                        ? (i < traffic.ObstacleHalfW.Count ? traffic.ObstacleHalfW[i] : 1.0f)
                        : stoppedHalf[i - nTraffic];
                    // The oncoming STREAM, moving or queued (OncomingAhead).
                    bool otherStream = i < nTraffic && i < traffic.ObstacleDir.Count && traffic.ObstacleDir[i] < 0;
                    obs.Add(new Obs { z = local.z, lat = lat, closing = closing, half = oh, oncoming = along < -1f || otherStream, rb = rb });
                }

                // The one to deal with: the nearest ahead, in this corridor.
                int block = -1;
                for (int i = 0; i < obs.Count; i++)
                {
                    var o = obs[i];
                    // In this car's corridor - OR in the lane it will come back
                    // to. Out passing, the next car up its own lane was in
                    // neither the corridor nor anything else: the racer dropped
                    // back in behind the first car at 150 km/h and met the second
                    // at 30 m/s closing (Blowing Rock, twice). Now that car is
                    // the one to deal with: stay out and take it too, or follow.
                    if (o.z < 1.5f) continue;
                    float reach = o.half + myHalf + CorridorAirM;
                    if (Mathf.Abs(o.lat - myLat) >= reach && Mathf.Abs(o.lat - lineOffset) >= reach) continue;
                    if (o.closing <= 0.3f) continue;
                    if (block < 0 || o.z < obs[block].z) block = i;
                }
                if (block >= 0)
                {
                    var bo = obs[block];
                    // How far the pass runs: to the car, and a car's length
                    // and a second past it at the speed we pass it at.
                    float passEnd = bo.z + 6f + Mathf.Max(bo.closing, 3f) * 1f;
                    int bendTo = nearestIdx + Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(passEnd, 20f) / path.spacing));
                    if (path.HasEnds) bendTo = Mathf.Min(bendTo, path.Count - 1);
                    float bendDeg = Mathf.Abs(Vector3.SignedAngle(path.GetTangent(nearestIdx), path.GetTangent(bendTo), Vector3.up));
                    float air = PassAir(bo.closing) + Mathf.Min(bendDeg * BendAirPerDeg, BendAirMaxM);
                    bool Clear(float line)
                    {
                        // On the tarmac - or, on the right, the paved verge.
                        if (line < -tarmacEdge || line > vergeEdge) return false;
                        // Not over our own lane's inner edge with anything
                        // coming (leftLimit): that pass waits, and we follow.
                        if (line - lineOffset < leftLimit) return false;
                        for (int i = 0; i < obs.Count; i++)
                        {
                            if (i == block) continue;
                            var o = obs[i];
                            if (Mathf.Abs(o.lat - line) >= o.half + myHalf + air) continue;
                            // An oncoming car is met sooner than it stands: it
                            // comes to us while we go to it. Anything within
                            // the pass, meeting point included, blocks the line.
                            float meet = o.oncoming ? o.z * mySpeed / Mathf.Max(mySpeed + Mathf.Abs(o.closing - mySpeed), 1f) : o.z;
                            if (meet > -3f && meet < passEnd + (o.oncoming ? 25f : 0f)) return false;
                        }
                        return true;
                    }
                    // Round the car that is THERE: both half-widths and the air
                    // for this closing speed (a rival that passed a car pulled
                    // onto the verge 1.9 m centre to centre clipped it at 20 m/s
                    // on NC 226A - that was 0.1 m of air; this is 0.3-0.8).
                    float left = bo.lat - bo.half - air - myHalf;
                    float right = bo.lat + bo.half + air + myHalf;
                    bool leftOk = Clear(left);
                    // THE RIGHT-HAND SIDE, on the shoulder where the tarmac runs
                    // out: only past a car going OUR way (a car coming at us in
                    // our lane is dodged, not passed, and the tarmac is enough
                    // for that), only where the verge is wide enough for this
                    // car's width - Clear() holds the line to vergeEdge - and
                    // only with nothing solid standing on it.
                    bool rightOnVerge = right > tarmacEdge;
                    bool rightOk = Clear(right) &&
                                   (!rightOnVerge || (!bo.oncoming && ShoulderClear(right, passEnd)));
                    // CROSSING ITS NOSE: a line on the far side of the car
                    // from where this racer is, taken only if the move across
                    // is done well before the car is reached.
                    float tReach = Mathf.Max(bo.z - 4.5f, 0f) / Mathf.Max(bo.closing, 0.3f);
                    float sideNow = myLat - bo.lat;
                    if (leftOk && sideNow > 0.3f && Mathf.Abs(left - myLat) / LateralRateMps > tReach * 0.6f) leftOk = false;
                    if (rightOk && sideNow < -0.3f && Mathf.Abs(right - myLat) / LateralRateMps > tReach * 0.6f) rightOk = false;
                    float pick = float.NaN;
                    // The side already chosen for THIS car, while it is clear.
                    if (passRb != null && passRb == bo.rb && passSide != 0 && (passSide < 0 ? leftOk : rightOk))
                        pick = passSide < 0 ? left : right;
                    else if (leftOk && rightOk)
                        pick = Mathf.Abs(left - myLat) <= Mathf.Abs(right - myLat) + 0.5f ? left : right;
                    else if (leftOk) pick = left;
                    else if (rightOk) pick = right;
                    if (!float.IsNaN(pick))
                    {
                        DebugPassSide = pick < bo.lat ? -1 : 1;
                        passRb = bo.rb;
                        passSide = DebugPassSide;
                        if (pick > tarmacEdge) { usedVerge = true; DebugOnVerge = true; }
                    }
                    else { passRb = null; passSide = 0; }

                    // Time to be beside it versus time to reach it: if the
                    // move will not be made in time, brake for the gap too.
                    float gap = bo.z - 4.5f;
                    float need = FollowGapM + Mathf.Max(mySpeed, 0f) * FollowTimeS;
                    float stopBrake = 0f;
                    if (bo.closing > 0.3f)
                    {
                        float room = Mathf.Max(gap - need, 0.5f);
                        // Planned on two-thirds of the brakes: the car ahead may
                        // brake too, and a stop planned on all of them has none
                        // left for that.
                        stopBrake = Mathf.Clamp01(bo.closing * bo.closing / (2f * room) / (BrakeDecel * PlanBrakeShare));
                    }
                    if (!float.IsNaN(pick))
                    {
                        // Already further out than the line, on the side it
                        // passes: stay out (PassHoldOutM at most) until by.
                        float aim = pick;
                        if (pick < bo.lat && myLat < pick) aim = Mathf.Max(myLat, pick - PassHoldOutM);
                        else if (pick > bo.lat && myLat > pick) aim = Mathf.Min(myLat, pick + PassHoldOutM, vergeEdge);
                        wanted = aim - lineOffset;
                        slew = TrafficSlew;
                        float lateralLeft = Mathf.Max(0f, bo.half + myHalf + air - Mathf.Abs(bo.lat - myLat));
                        float tSide = lateralLeft / LateralRateMps;
                        float tHit = gap / Mathf.Max(bo.closing, 0.3f);
                        if (tHit < tSide * 1.3f) trafficBrake = Mathf.Max(trafficBrake, stopBrake);
                        throttleLift = Mathf.Max(throttleLift, Mathf.Clamp01(tSide * 1.3f / Mathf.Max(tHit, 0.1f)) * 0.6f);
                    }
                    else
                    {
                        // Nowhere to go: follow it.
                        wanted = Mathf.Clamp(myLat - lineOffset, -edge - lineOffset, edge - lineOffset);
                        trafficBrake = Mathf.Max(trafficBrake, stopBrake);
                        if (gap < need * 1.5f) throttleLift = 1f;
                    }
                }
                // ANYTHING IN THE WAY, whatever the plan: a car in this car's
                // path - where it IS, not where its line is - that it will not
                // be beside in time gets the brake it takes to stop short of
                // it. The pass above deals with the NEAREST car in the
                // corridor; out in the other lane passing one, a car queued
                // further up that lane was nobody's block, and the racer ran
                // into it at 39 m/s (Gillespie Gap, rush hour).
                for (int i = 0; i < obs.Count; i++)
                {
                    var o = obs[i];
                    if (o.z < 1.5f || o.closing <= 0.3f) continue;
                    float overlap = o.half + myHalf + 0.2f - Mathf.Abs(o.lat - myLat);
                    if (overlap <= 0f) continue;
                    float gapO = o.z - 4.5f;
                    float tHitO = Mathf.Max(gapO, 0f) / o.closing;
                    // Moving out of its way already, fast enough: the plan holds.
                    float wantedAbs = wanted + lineOffset;
                    bool leaving = Mathf.Abs(wantedAbs - o.lat) >= o.half + myHalf;
                    if (leaving && tHitO > overlap / LateralRateMps * 1.3f) continue;
                    float roomO = Mathf.Max(gapO - 2f, 0.5f);
                    float need = Mathf.Clamp01(o.closing * o.closing / (2f * roomO) / BrakeDecel);
                    if (need > 0.2f)
                    {
                        trafficBrake = Mathf.Max(trafficBrake, need);
                        throttleLift = 1f;
                    }
                }

                // An oncoming car beside this racer's line but not in it:
                // never drift toward it while it goes by.
                for (int i = 0; i < obs.Count; i++)
                {
                    var o = obs[i];
                    if (!o.oncoming || o.z < -3f || o.z > 45f) continue;
                    float away = myLat - o.lat;
                    // Only one BESIDE the corridor: one in it is the pass
                    // above, and pushing "away" from it could be pushing
                    // across its bow.
                    if (Mathf.Abs(away) >= TrafficClearM - 0.1f && Mathf.Abs(away) < TrafficClearM + 1.2f)
                    {
                        float keep = o.lat + Mathf.Sign(away == 0f ? 1f : away) * (TrafficClearM + 0.4f);
                        keep = Mathf.Clamp(keep, -edge, edge) - lineOffset;
                        if (block < 0 || Mathf.Abs(keep) > Mathf.Abs(wanted)) { wanted = keep; slew = TrafficSlew; }
                    }
                }
            }

            // ALONGSIDE: the car being passed drops out of "ahead" once level,
            // and with nothing ahead the racer steered back to its line - into
            // that car's door (Blowing Rock: a Skyline half past a Crown Vic at
            // 132 km/h came back across onto it). Level with a car, the line
            // may not come nearer to it than the clearance.
            bool alongsideHold = false;
            float roadHalf = path.roadWidth * 0.5f;
            if (obs.Count > 0)
            {
                Vector3 rr = Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized;
                float at = Vector3.Dot(transform.position - path.GetPoint(nearestIdx), rr);
                for (int i = 0; i < obs.Count; i++)
                {
                    var o = obs[i];
                    if (o.z < -5.5f || o.z > 1.5f || o.oncoming) continue;
                    float side = at - o.lat;
                    if (Mathf.Abs(side) > TrafficClearM + 1.5f) continue;
                    // BEHIND, IN LINE, is following - not alongside. A rival
                    // crawling round a hairpin (under StoppedRacerMps, so it is
                    // in obs) a car length behind this one asked for TrafficClearM
                    // of the road beside it, which a 6.1 m road has not got, so
                    // this car braked to "drop in behind" the car behind it -
                    // which was braking to follow this one: both sat at 1 km/h
                    // until the recovery put them on (Chimney Rock's switchbacks,
                    // 2026-09-29: with this, the crawl-slip throttle ease, no
                    // drop-behind brake at a standstill and the waiting clock,
                    // a whole race's recoveries went 87 -> 10 up the park road
                    // and 68 -> 2 down it).
                    if (o.z < -AlongsideLevelM && Mathf.Abs(side) < AlongsideInLineM) continue;
                    // Beside it: both half-widths and the slow-pass air apart.
                    float keep = o.lat + (side >= 0f ? 1f : -1f) * (o.half + myHalf + PassAirMinM) - lineOffset;
                    if (side < 0f ? wanted > keep : wanted < keep)
                    {
                        wanted = keep;
                        slew = TrafficSlew;
                        alongsideHold = side < 0f;
                        // Out on its RIGHT, on the verge, going by: that is the
                        // right-hand pass in progress, and the verge is this
                        // car's until it is past (the clamp below).
                        bool onRightVerge = side > 0f && keep + lineOffset > tarmacEdge &&
                                            keep + lineOffset <= vergeEdge;
                        if (onRightVerge) { usedVerge = true; DebugOnVerge = true; }
                        // No room beside it on the tarmac (or, on its right, the
                        // verge): not off the road for it (Blowing Rock: aimed at
                        // +4.0 on a road whose edge is +3.2, into the wall) - drop
                        // in behind. Only a car that is MOVING can drop in behind
                        // anything: two rivals jammed together at a standstill on
                        // a 6.1 m switchback each braked for the other (Chimney
                        // Rock wp 250) until the recovery parted them.
                        float room = side > 0f ? vergeEdge : tarmacEdge;
                        if (Mathf.Abs(keep + lineOffset) > room && car.forwardSpeed > DropBehindMinMps)
                        {
                            throttleLift = 1f;
                            trafficBrake = Mathf.Max(trafficBrake, 0.9f);
                        }
                    }
                }
            }

            // And whatever asked for it, never over the line with something
            // coming: a pass already out there comes back at the traffic slew.
            // (Gillespie/Mount Mitchell: a rival on the centreline passing
            // another met an oncoming Camry head-on at 42 m/s.)
            if (wanted < leftLimit && alongsideHold && car.forwardSpeed > DropBehindMinMps)
            {
                // Out in the other lane, level with the car being passed, and
                // something coming: not into its door - off the throttle and on
                // the brakes to drop in BEHIND it, then the follow above takes
                // the car back into the lane.
                throttleLift = 1f;
                trafficBrake = Mathf.Max(trafficBrake, 0.9f);
            }
            else if (wanted < leftLimit)
            {
                wanted = leftLimit;
                if (avoidBias < leftLimit)
                {
                    slew = Mathf.Max(slew, TrafficSlew);
                    // Aborting a pass: drop in BEHIND the car alongside on the
                    // right rather than into its door.
                    if (rm != null)
                        foreach (var other in rm.allCars)
                        {
                            if (other == null || other == car) continue;
                            Vector3 lo = transform.InverseTransformPoint(other.transform.position);
                            if (lo.x > 0.5f && lo.x < 4f && lo.z > -5f && lo.z < 6f &&
                                car.forwardSpeed > DropBehindMinMps)
                            {
                                throttleLift = 1f;
                                trafficBrake = Mathf.Max(trafficBrake, 0.5f);
                            }
                        }
                }
            }
            // Whatever asked for it, the car's centre stays on the tarmac: a
            // half-width and a little in from each edge - and on the RIGHT,
            // only while a pass is using it, as far as the paved verge (its
            // outer side VergeSideM past the edge), never onto the grass.
            float onRoad = Mathf.Max(roadHalf - TarmacKeepM, 0.3f);
            float onRight = usedVerge ? Mathf.Max(onRoad, vergeEdge) : onRoad;
            wanted = Mathf.Clamp(wanted, -onRoad - lineOffset, onRight - lineOffset);
            // Slewed, not snapped: the offset feeds the steering target, and a
            // step change in it reads as a flick of the wheel.
            // Toward the centreline with something coming: the mark moves no
            // faster than the car can follow it without swinging past.
            if (!float.IsNegativeInfinity(leftLimit) && wanted < avoidBias)
                slew = Mathf.Min(slew, LeftSlewOncomingMps);
            avoidBias = Mathf.MoveTowards(avoidBias, wanted, slew * dt);
        }

        /// <summary>
        /// Put the car back on the road when it can no longer get there itself.
        /// Two failure modes, two clocks: pinned against something solid, and
        /// pointed the wrong way.
        /// </summary>
        void UpdateRecovery(float dt, float speed, bool waiting)
        {
            bool pinned = responder != null && responder.InSolidContact;
            float limit = pinned ? PinnedSeconds : StuckSeconds;
            // A car stopped behind a queue, or for an oncoming car filling a
            // narrow road, is WAITING: its clock runs at WaitingClockRate, so it
            // pulls away itself once the road clears (a queue of three up
            // Chimney Rock was put back every 4 s) and is still put back if
            // the wait never ends.
            if (speed < 1f) stuckTimer += waiting && !pinned ? dt * WaitingClockRate : dt;
            else stuckTimer = 0f;

            // Wrong way: measured against the road, not against the car's own
            // velocity, so a car sliding backwards through a corner it is still
            // steering through does not trip it.
            float alignment = Vector3.Dot(transform.forward, path.GetTangent(nearestIdx));
            if (alignment < WrongWayDot && speed > WrongWayMinSpeed) wrongWayTimer += dt;
            else wrongWayTimer = 0f;

            // CRAWLING IN PLACE: jammed against a wreck at 2-8 km/h, never
            // under the 1 m/s the stuck clock wants - on NC 226A a rival spent
            // four minutes of a five-minute race like that. Progress, not
            // speed: under CrawlMinM of road in CrawlSeconds is stuck too.
            if (nearestIdx > crawlIdx + Mathf.CeilToInt(CrawlMinM / path.spacing) || nearestIdx < crawlIdx - 20)
            { crawlIdx = nearestIdx; crawlTimer = 0f; }
            else crawlTimer += waiting ? dt * WaitingClockRate : dt;

            if (Retired) return;
            if (stuckTimer > limit || wrongWayTimer > WrongWaySeconds || crawlTimer > CrawlSeconds)
            {
                LastRecoveryWhy = stuckTimer > limit ? (pinned ? "pinned" : "stuck")
                                : wrongWayTimer > WrongWaySeconds ? "wrong way" : "crawling";
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                crawlTimer = 0f;
                crawlIdx = nearestIdx;
                avoidBias = 0f;
                laneInt = 0f;
                RaceManager.Instance?.RespawnCar(car);
            }
        }
    }
}
