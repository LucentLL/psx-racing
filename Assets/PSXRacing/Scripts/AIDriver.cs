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
        /// <summary>The waypoint this driver steers from (read by the deck-run play check).</summary>
        public int PathIndex => nearestIdx;
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
            // NOT PAST A HAIRPIN'S OWN RADIUS: a point further round a tight
            // bend than its radius is across its inside, and chasing it cut
            // the corner from the turn-in - on Chimney Rock's downhill hairpin
            // the racers were 1-1.5 m inside their lane at the entry, on a 4.5 m
            // radius their lock could not hold, and ploughed across the road.
            // On a two-way road only: a free line (a circuit, a city route, a
            // deck) straightens a tight corner, and a nearer point dragged
            // the deck racers onto the inside kerb of its tightest turn.
            var tsLook = TrafficSystem.Instance;
            if (tsLook != null && tsLook.TwoWay && path.curvatures != null && path.curvatures.Length == path.Count)
            {
                float kNear = path.MaxCurvatureAhead(nearestIdx, Mathf.CeilToInt(lookDist / path.spacing) + 1);
                if (kNear > 1e-4f) lookDist = Mathf.Min(lookDist, Mathf.Max(LookMinM, LookPerRadius / kNear));
            }
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
            else
            {
                laneInt = 0f;
                // The error the fast damping below aims the drift by.
                laneErr = lineOffset + avoidBias - Vector3.Dot(transform.position - path.GetPoint(nearestIdx),
                                                               Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized);
            }
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
            // whose racing line this would change - except FAST (2026-10-07):
            // on the limit, city routes saw racers drift two metres off their
            // line at 150-170 km/h on a gentle bend, onto the median (Uptown
            // Loop: three spins a race). Damping the drift and the yaw does not
            // move the line; it holds the car on it.
            bool twoWayRoad = ts != null && ts.TwoWay;
            if ((twoWayRoad && speed > 5f) || speed > FastDampMps)
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
        /// <summary>Closing speed (m/s) a pass may be made at with
        /// PassRoomTightM between the sides, rising to PassCloseMaxMps with
        /// PassRoomEasyM (see UpdateAvoidance).</summary>
        const float PassCloseMinMps = 12f, PassCloseMaxMps = 30f, PassRoomTightM = 0.4f, PassRoomEasyM = 1.6f;
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
        /// <summary>...on today's road: a wet one stops on the same smaller
        /// circle it corners on (Seasons.GripMult).</summary>
        static float BrakeDecelNow => BrakeDecel * Seasons.RoadGripMult;
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
        /// <summary>The steering point is no further up the road than this
        /// many radii of the tightest bend it spans, nor nearer than
        /// LookMinM (SteerToLine).</summary>
        const float LookPerRadius = 1.0f, LookMinM = 5f;
        /// <summary>Over this speed the lane and yaw damping hold the car on
        /// its line on every road, not only a two-way one.</summary>
        const float FastDampMps = 25f;

        /// <summary>Is an ONCOMING traffic car within <paramref name="reach"/>
        /// metres up the road - or within OncomingGuardS seconds of closing,
        /// if that is further?</summary>
        bool OncomingAhead(TrafficSystem ts, float reach)
        {
            var bodies = ts.Obstacles;
            float mine = Mathf.Max(car.forwardSpeed, 0f);
            // UP THE ROAD, not up the car's nose (2026-10-07; owner: "I
            // watched all four cars get stuck behind a slow NPC traffic car",
            // a mountain two-lane with the oncoming lane empty). This was a
            // slab - every oncoming-stream car anywhere in front of the car's
            // nose and within the reach, however far to the side: on a
            // winding stage that is a car on the leg below a switchback, or
            // across a valley, nearly always, and the field held its lane
            // behind a slow car for minutes. Measured along the road (the
            // traffic's own path metres), the guard sees exactly the cars that
            // can meet a pass.
            bool alongRoad = ts.ObstacleS.Count == bodies.Count && path != null && path.Count > 1;
            float myS = 0f, total = 0f;
            if (alongRoad)
            {
                myS = nearestIdx * path.spacing + Vector3.Dot(transform.position - path.GetPoint(nearestIdx), path.GetTangent(nearestIdx));
                total = path.TotalLength;
            }
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
                // Coming down the ROAD at us: its whole speed (round a bend it
                // is not pointed at our nose, and still closes at all of it).
                theirs = otherStream ? new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude : Mathf.Max(theirs, 0f);
                float ahead;
                if (alongRoad)
                {
                    ahead = ts.ObstacleS[i] - myS;
                    if (!path.HasEnds)
                    {
                        if (ahead > total * 0.5f) ahead -= total;
                        else if (ahead < -total * 0.5f) ahead += total;
                    }
                }
                else ahead = transform.InverseTransformPoint(rb.position).z;
                if (ahead > -3f && ahead < Mathf.Max(reach, (mine + theirs) * OncomingGuardS)) return true;
            }
            return false;
        }

        /// <summary>A traffic car this far (or more) off where the road says
        /// it is, ahead or behind, is on another leg of it (the far side of a
        /// switchback): not in this car's way, however near in a straight
        /// line. Plus this share of the road distance, for a bend's chord.</summary>
        const float OtherLegM = 25f, OtherLegShare = 0.25f;
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
        /// <summary>
        /// A car on ANOTHER FLOOR of a parking deck (a deck run's path has
        /// checkpoints, TrackPath.gates): the levels of a helix stand over each
        /// other, so the car a floor below or above is "dead ahead in my lane"
        /// to every plan-view test here - and two racers a level apart on the
        /// way down each braked for the other, to a standstill, for good.
        /// Only on a deck: on a road a car 30 m up a grade is really ahead.
        /// </summary>
        bool OtherFloor(Vector3 local) => path != null && path.gates != null && Mathf.Abs(local.y) > OtherFloorM;
        const float OtherFloorM = 2.2f;
        bool OnDeck => path != null && path.gates != null;
        /// <summary>A deck aisle's own lane, either side of this car's centre,
        /// and what counts as stopped at deck speed (the roof loop is taken at
        /// a walk).</summary>
        const float DeckLaneM = 1.6f, DeckStoppedMps = 1f;

        float LineOffset()
        {
            var ts = TrafficSystem.Instance;
            if (ts == null || !ts.TwoWay) return lateralOffset;
            return ts.RaceLanesCentre + Mathf.Clamp(lateralOffset * 0.3f, -0.5f, 0.5f) + openLine;
        }
        float lineOffset;

        /// <summary>
        /// THE OPEN LINE (2026-10-07; owner: "the first/last turn of Chimney
        /// Rock, AI basically just stops instead of taking the hairpin"). A
        /// right-hand hairpin in the right-hand lane is a 6 m radius on
        /// Chimney Rock's 7.5 m bend, and every car turns at most ~0.92 rad/s
        /// (AIGrip.YawRateMax): 20 km/h, on full lock, crawling. With nothing
        /// coming up the road (the same 130 m / 5 s guard a pass needs), the
        /// racer takes it from the OTHER lane - the radius of the whole road -
        /// and comes back to its own on the way out. Metres moved, slewed.
        /// </summary>
        float openLine;
        const float OpenLookM = 50f, OpenMaxRadiusM = 25f, OpenFullRadiusM = 12f, OpenSlewMps = 2.5f;
        void UpdateOpenLine(float dt)
        {
            float want = 0f;
            var ts = TrafficSystem.Instance;
            var curv = path.curvatures;
            if (ts != null && ts.TwoWay && curv != null && curv.Length == path.Count && !OncomingAhead(ts, OncomingGuardM))
            {
                int n = Mathf.CeilToInt(OpenLookM / path.spacing);
                float kMax = 0f; int at = 0;
                for (int j = 0; j <= n; j++)
                {
                    float k = curv[path.Wrap(nearestIdx + j)];
                    if (k > kMax) { kMax = k; at = nearestIdx + j; }
                }
                if (kMax > 1f / OpenMaxRadiusM &&
                    Vector3.SignedAngle(path.GetTangent(at - 2), path.GetTangent(at + 2), Vector3.up) > 0f)
                    want = -2f * ts.RaceLanesCentre * Mathf.InverseLerp(1f / OpenMaxRadiusM, 1f / OpenFullRadiusM, kMax);
            }
            openLine = Mathf.MoveTowards(openLine, want, OpenSlewMps * dt);
        }
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
        /// <summary>A car whose up axis points less skyward than this is on
        /// its side or its roof (UpdateRecovery).</summary>
        const float FlippedUpY = 0.3f;
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

        // ---- AT THE LIMIT (2026-10-07) ----
        // Owner: "I notice the AI is very brake happy, timid, scared to take a
        // corner. Even without traffic like parking decks, they rarely push
        // their cars to the limits, never drift or powerslide."
        /// <summary>Share of the car's measured limit (AIGrip) a driver plans
        /// its corners on, across the skill range 0.8 -> 1.05: the field's
        /// spread is kept, but the best of it is on the limit.</summary>
        const float LimitShareLo = 0.88f, LimitShareHi = 1.0f;
        /// <summary>Share of a full ABS stop (AIGrip.BrakeDecel) the braking
        /// profile is planned on, across the same range.</summary>
        const float BrakeShareLo = 0.82f, BrakeShareHi = 0.96f;
        /// <summary>Seconds of travel the braking point is brought forward by:
        /// the pedal and the tyres take a moment to bite.</summary>
        const float ReactS = 0.12f;
        /// <summary>No bend is planned slower than this (a curvature spike
        /// between two waypoints is not a corner to stop for).</summary>
        const float MinCornerMps = 4.5f;
        /// <summary>The longest look up the road for a bend to brake for.</summary>
        const float PlanScanMaxM = 420f;
        /// <summary>Pedals round the target (see FixedUpdate): brake past this
        /// much over it, the trail band under it while a bend ahead sets it,
        /// brake and throttle per m/s of error, and the throttle on the speed.</summary>
        const float BrakeBandMps = 0.3f, TrailBandMps = 1.5f, BrakeGain = 0.35f;
        const float HoldThrottle = 0.4f, ThrottleGain = 0.4f;
        /// <summary>The least brake a car turning at its limit still gets.</summary>
        const float MinTrailBrake = 0.35f;
        /// <summary>Slides: the rear slip a tidy driver keeps the throttle to,
        /// the slip over its tolerance at which the throttle is fully off, and
        /// the body angle past which it is off whatever the driver.</summary>
        const float GripSlideTolRad = 0.3f, SlideFadeRad = 0.3f, MaxBodySlipRad = 0.75f;
        /// <summary>Up to SlideFreeMps a driver slides as it likes; from
        /// SlideTidyMps on, every driver keeps the rear to FastSlideTolRad and
        /// the body to FastBodySlipRad.</summary>
        const float SlideFreeMps = 14f, SlideTidyMps = 32f, FastSlideTolRad = 0.2f, FastBodySlipRad = 0.3f;
        /// <summary>Front slip the throttle tolerates, and over it the slip at
        /// which it is fully off.</summary>
        const float FrontSlipTolRad = 0.2f, FrontSlipFadeRad = 0.2f;
        /// <summary>Ploughing: steer this near full lock with the fronts this
        /// far past their peak gets up to this much brake.</summary>
        const float PloughSteer = 0.9f, PloughSlipRad = 0.24f, PloughBrake = 0.6f;
        /// <summary>With an oncoming car this near up the road, nobody slides
        /// or flicks: a drift on a two-lane road takes the other lane.</summary>
        const float SlideOncomingM = 150f;
        /// <summary>Counter-steer: body slip ignored, and the share of the
        /// rest the front wheels are turned back along the travel.</summary>
        const float CsDeadzoneRad = 0.07f, CsGain = 0.75f;
        /// <summary>Handbrake flick (drifters): speeds, the tightest radius it
        /// is for, how far up the road the bend must turn FlickMinTurnDeg in,
        /// how long the lever is held, and the wait before the next.</summary>
        const float FlickMinMps = 8f, FlickMaxMps = 24f, FlickMaxRadiusM = 30f;
        const float FlickTurnLookM = 30f, FlickMinTurnDeg = 70f, FlickHoldS = 0.2f, FlickCoolS = 3f;
        /// <summary>Rear-driven (FR, MR, RR) drivers at this skill or over may
        /// be drifters, and this share of them is.</summary>
        const float DrifterMinSkill = 0.88f, DrifterOdds = 0.6f;

        /// <summary>This driver: rolled once, from its car's name and skill
        /// (the slot it races from), so a race replays the same.</summary>
        float limitShare = -1f, brakeShare = 0.9f, slideTol = GripSlideTolRad;
        bool drifter;
        float flickHold, flickCool;
        int flickBend = -100000;
        /// <summary>For the harness: the speed planned this tick, the
        /// driver's shares and whether it drifts.</summary>
        public float DebugTarget { get; private set; }
        public float LimitShare => limitShare;
        public float BrakeShare => brakeShare;
        public bool Drifter => drifter;

        /// <summary>
        /// TRAFFIC MANNERS: with a traffic car up the road (TrafficCapLookM,
        /// measured along it, either stream), no more than its speed plus
        /// TrafficCapMarginMps. On the limit everywhere, racers came up on a
        /// car stopped in the other lane at 140 km/h, wandered half a metre
        /// across the centreline under the brakes, and hit it (Gillespie Gap,
        /// medium traffic, twice). In traffic a racer is a driver in traffic;
        /// on an empty road (deck runs, NONE) this never binds.
        /// </summary>
        float TrafficCap()
        {
            var ts = TrafficSystem.Instance;
            if (ts == null || ts.Obstacles.Count == 0 || ts.ObstacleS.Count != ts.Obstacles.Count) return float.PositiveInfinity;
            float myS = nearestIdx * path.spacing + Vector3.Dot(transform.position - path.GetPoint(nearestIdx), path.GetTangent(nearestIdx));
            float total = path.TotalLength, cap = float.PositiveInfinity;
            for (int i = 0; i < ts.Obstacles.Count; i++)
            {
                var rb = ts.Obstacles[i];
                if (rb == null) continue;
                float ahead = ts.ObstacleS[i] - myS;
                if (!path.HasEnds)
                {
                    if (ahead > total * 0.5f) ahead -= total;
                    else if (ahead < -total * 0.5f) ahead += total;
                }
                if (ahead < -5f || ahead > TrafficCapLookM) continue;
                float theirs = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
                cap = Mathf.Min(cap, theirs + TrafficCapMarginMps);
            }
            return cap;
        }
        const float TrafficCapLookM = 150f, TrafficCapMarginMps = 20f;

        void RollPersona()
        {
            uint h = 2166136261u;
            foreach (char ch in gameObject.name) { h ^= ch; h *= 16777619u; }
            h ^= (uint)Mathf.RoundToInt(skill * 1000f); h *= 16777619u;
            if (h == 0u) h = 1u;
            float U() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xFFFFFFu) / 16777216f; }
            float sk = Mathf.InverseLerp(0.8f, 1.05f, skill);
            limitShare = Mathf.Min(LimitShareHi, Mathf.Lerp(LimitShareLo, LimitShareHi, sk) + (U() - 0.5f) * 0.02f);
            brakeShare = Mathf.Min(BrakeShareHi, Mathf.Lerp(BrakeShareLo, BrakeShareHi, sk) + (U() - 0.5f) * 0.03f);
            // Rear-driven (FR, MR, RR): the throttle can steer it.
            bool rearDriven = car.frontDriveShare < 0.35f;
            float roll = U();
            drifter = rearDriven && skill >= DrifterMinSkill && roll < DrifterOdds;
            slideTol = drifter ? Mathf.Lerp(0.36f, 0.5f, U()) : Mathf.Lerp(0.27f, 0.33f, U());
        }

        /// <summary>
        /// The speed this car may have NOW - a racing driver's speed profile.
        /// Every point up the road gets its corner speed at this driver's share
        /// of the car's limit (AIGrip: grip, and on a hairpin the steering
        /// lock), on the radius THIS car takes it - where it is across the road
        /// now, blending into the line it is aiming for (a right-hander is
        /// tighter in the right-hand lane). Then, from the far end back to the
        /// car, each point may be no faster than the next one plus what this
        /// driver's brakes take off over the gap - with only the brake the
        /// turn there leaves (the friction circle: braking into a hairpin's
        /// entry while already turning is braking on part of the tyre). The
        /// first runs of this braked in a straight line to the apex's speed
        /// as if the entry were straight, and arrived at Chimney Rock's
        /// downhill hairpin at 47 km/h on full lock and ran across the road.
        /// Also: whether a bend AHEAD sets the speed (the braking zone), the
        /// deceleration planned on, and the slowest bend ahead and how far
        /// (the mistake's corner).
        /// </summary>
        float PlanSpeed(float speed, out bool zone, out float planDecel, out float bendSpeed, out float bendAtM)
        {
            float full = AIGrip.BrakeDecel(car, speed);
            // Downhill the brakes have gravity against them; uphill, with them.
            float slope = Mathf.Clamp(transform.forward.y, -0.2f, 0.2f);
            planDecel = Mathf.Max(full * brakeShare + 9.81f * slope, full * 0.4f);
            zone = false; bendSpeed = float.PositiveInfinity; bendAtM = 0f;
            var curv = path.curvatures;
            if (curv == null || curv.Length != path.Count) return float.PositiveInfinity;
            float scan = Mathf.Min(speed * speed / (2f * planDecel) + speed + 25f, PlanScanMaxM);
            int n = Mathf.CeilToInt(scan / path.spacing);
            n = path.HasEnds ? Mathf.Min(n, path.Count - 1 - nearestIdx) : Mathf.Min(n, path.Count / 3);
            if (n < 1) return float.PositiveInfinity;
            if (vcBuf.Length < n + 1) { vcBuf = new float[n + 16]; kBuf = new float[n + 16]; }
            Vector3 t0 = path.GetTangent(nearestIdx);
            int yawWin = Mathf.Max(1, Mathf.RoundToInt(YawWindowM / path.spacing));
            // The turn-rate ceiling is a steady circle's (AIGrip.YawRateMax),
            // and binds on a lane - a two-way road's hairpin. On a free line
            // the car straightens a tight corner: deck runs went round theirs
            // at 1.5-2x it, clean, and held to it they crawled.
            var tsYaw = TrafficSystem.Instance;
            float yawShare = limitShare * (tsYaw != null && tsYaw.TwoWay ? 1f : FreeLineYawMult);
            yawShareNow = yawShare;
            float along0 = Vector3.Dot(transform.position - path.GetPoint(nearestIdx), t0);
            float latNow = Vector3.Dot(transform.position - path.GetPoint(nearestIdx), Vector3.Cross(Vector3.up, t0).normalized);
            float latAim = lineOffset + avoidBias;
            for (int j = 0; j <= n; j++)
            {
                int wi = path.Wrap(nearestIdx + j);
                float k = curv[wi];
                float d = j * path.spacing - along0;
                float lat = Mathf.Lerp(latNow, latAim, Mathf.Clamp01(d / LineBlendM));
                if (k > 1f / 400f && Mathf.Abs(lat) > 0.3f)
                {
                    float turn = Vector3.SignedAngle(path.GetTangent(wi - 2), path.GetTangent(wi + 2), Vector3.up);
                    k /= Mathf.Clamp(1f - (turn >= 0f ? 1f : -1f) * k * lat, 0.5f, 1.5f);
                }
                kBuf[j] = k;
                // The turn rate a bend asks for is over the car's length of
                // it, not one waypoint's kink (a deck's aisle corners).
                float kYaw = 0f;
                for (int w = -yawWin; w <= yawWin; w++) kYaw += curv[path.Wrap(wi + w)];
                kYaw = kYaw / (2 * yawWin + 1) * (k / Mathf.Max(curv[wi], 1e-5f));
                float vc = k < 1e-4f ? float.PositiveInfinity
                         : Mathf.Min(AIGrip.CornerSpeed(car, k, limitShare, 0f), AIGrip.YawSpeed(kYaw, yawShare));
                if (!float.IsInfinity(vc)) vc = Mathf.Max(vc, MinCornerMps);
                // A CREST is a corner in the vertical: over it at speed the
                // car goes light, and a city route's brow launched racers at
                // 200 km/h into a spin (UptownLoop wp 1122, more than once).
                float ky = -(path.GetPoint(wi + 2).y - 2f * path.GetPoint(wi).y + path.GetPoint(wi - 2).y) / (4f * path.spacing * path.spacing);
                if (ky > 1e-4f) vc = Mathf.Min(vc, Mathf.Max(Mathf.Sqrt(CrestG * 9.81f / ky), MinCornerMps * 3f));
                vcBuf[j] = vc;
                if (vc < bendSpeed) { bendSpeed = vc; bendAtM = Mathf.Max(0f, d); }
            }
            float va = vcBuf[n];
            for (int j = n - 1; j >= 1; j--)
                va = Mathf.Min(vcBuf[j], BrakeBack(va, kBuf[j], path.spacing, planDecel));
            float atCar = Mathf.Min(vcBuf[0], BrakeBack(va, kBuf[0], Mathf.Max(0f, path.spacing - along0), planDecel));
            zone = atCar < vcBuf[0] - 0.3f;
            // On the braking curve, the speed it must have a moment of travel
            // EARLIER: the pedal and the tyres take that long to bite.
            if (zone && !float.IsInfinity(atCar))
                atCar = Mathf.Sqrt(Mathf.Max(atCar * atCar - 2f * planDecel * speed * ReactS, MinCornerMps * MinCornerMps));
            return atCar;
        }
        float[] vcBuf = new float[0], kBuf = new float[0];
        /// <summary>Metres over which the planner's line blends from where
        /// the car is across the road to where it is aiming.</summary>
        const float LineBlendM = 20f;
        /// <summary>Half-length of road the turn rate's curvature is averaged
        /// over (PlanSpeed).</summary>
        const float YawWindowM = 10f;
        /// <summary>The turn-rate ceiling on a free line, over a lane's.</summary>
        const float FreeLineYawMult = 1.8f;
        /// <summary>The share of the car's weight a crest may take off it.</summary>
        const float CrestG = 0.6f;
        float yawShareNow = 1f;
        /// <summary>The least share of the brakes a bend at its limit leaves.</summary>
        const float MinBrakeInTurn = 0.3f;

        /// <summary>The fastest a car may be <paramref name="ds"/> metres
        /// before a point it may reach at <paramref name="vNext"/>, braking at
        /// <paramref name="decel"/> less what the bend of curvature
        /// <paramref name="k"/> there takes out of the tyres.</summary>
        float BrakeBack(float vNext, float k, float ds, float decel)
        {
            if (float.IsInfinity(vNext)) return vNext;
            float latUse = 0f;
            if (k > 1e-4f)
                latUse = Mathf.Clamp01(Mathf.Max(vNext * vNext * k / (9.81f * Mathf.Max(AIGrip.LateralG(car, vNext) * limitShare, 0.2f)),
                                                 vNext * k / (AIGrip.YawRateMax * yawShareNow)));
            float avail = decel * Mathf.Max(MinBrakeInTurn, Mathf.Sqrt(1f - latUse * latUse));
            return Mathf.Sqrt(vNext * vNext + 2f * avail * ds);
        }

        /// <summary>Steer that turns the front wheels back along the car's
        /// travel, for the body slip past a small deadzone: what holds a
        /// powerslide as a slide rather than a spin. The point-chase alone
        /// counter-steers by about a third of the angle.</summary>
        float CounterSteer(float speed)
        {
            if (speed < 5f) return 0f;
            float cs = car.chassisSlipAngle;
            float ex = Mathf.Abs(cs) - CsDeadzoneRad;
            if (ex <= 0f) return 0f;
            return -Mathf.Sign(cs) * ex * Mathf.Rad2Deg / Mathf.Max(car.CurrentMaxSteerDeg, 1f) * CsGain;
        }

        /// <summary>
        /// THE HAIRPIN FLICK: a drifter (rear-driven, skilled, see RollPersona)
        /// pulls the handbrake for FlickHoldS at the turn-in of a bend that is
        /// tight (under FlickMaxRadiusM) and turns at least FlickMinTurnDeg in
        /// the next FlickTurnLookM - and then STOPS turning (a deck's helix
        /// keeps turning, and is driven) - with the wheels already turned into
        /// it (the car's kick reads the actuator), nobody near, and nothing
        /// coming. The car's own drift layer does the rest (the E-brake kick
        /// and the rear's collapse); the counter-steer and the throttle hold
        /// it. True while the lever is pulled.
        /// </summary>
        bool UpdateFlick(float dt, float speed, float steer, bool oncomingNear, bool allowed, ref float throttle, ref float brake)
        {
            flickCool -= dt;
            if (flickHold > 0f)
            {
                flickHold -= dt;
                if (!allowed) { flickHold = 0f; return false; }
                throttle = 0f; brake = 0f;
                return true;
            }
            if (!allowed || !drifter || oncomingNear || flickCool > 0f || speed < FlickMinMps || speed > FlickMaxMps) return false;
            if (path.curvatures == null || path.curvatures.Length != path.Count ||
                path.curvatures[path.Wrap(nearestIdx + 1)] < 1f / FlickMaxRadiusM) return false;
            int ahead = Mathf.Max(2, Mathf.RoundToInt(FlickTurnLookM / path.spacing));
            if (path.HasEnds && nearestIdx + 2 * ahead >= path.Count) return false;
            float turn = Vector3.SignedAngle(path.GetTangent(nearestIdx), path.GetTangent(nearestIdx + ahead), Vector3.up);
            if (Mathf.Abs(turn) < FlickMinTurnDeg) return false;
            float turn2 = Vector3.SignedAngle(path.GetTangent(nearestIdx + ahead), path.GetTangent(nearestIdx + 2 * ahead), Vector3.up);
            if (Mathf.Sign(turn2) == Mathf.Sign(turn) && Mathf.Abs(turn2) > FlickMinTurnDeg * 0.8f) return false;
            if (Mathf.Sign(steer) != Mathf.Sign(turn) || Mathf.Abs(steer) < 0.3f) return false;
            if (Mathf.Abs(nearestIdx - flickBend) < 2 * ahead) return false;
            // Room: no racer within 15 m, no traffic within 40 m.
            var rm = RaceManager.Instance;
            if (rm != null)
                foreach (var o in rm.allCars)
                    if (o != null && o != car && (o.transform.position - transform.position).sqrMagnitude < 15f * 15f) return false;
            var ts = TrafficSystem.Instance;
            if (ts != null)
                foreach (var rb in ts.Obstacles)
                    if (rb != null && (rb.position - transform.position).sqrMagnitude < 40f * 40f) return false;
            flickBend = nearestIdx;
            flickHold = FlickHoldS;
            flickCool = FlickCoolS;
            throttle = 0f; brake = 0f;
            return true;
        }

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
            if (driving) UpdateOpenLine(dt);
            lineOffset = LineOffset();

            if (!driving)
            {
                float rolling = Mathf.Abs(car.forwardSpeed);
                bool coasting = ShuttingDown && rolling > ShutdownRestMps;
                // A WRECK PULLS OVER: onto the verge past the right-hand edge,
                // not stopped in its lane - on a 6 m mountain road a car
                // parked in one lane is a roadblock the field queues behind.
                // A deck run's finisher pulls over like a wreck does (gently):
                // the flag is a few car lengths off the deck's exit, and a
                // finisher rolling to a stop in the lane parked the next car
                // short of the line. And now on a stage too (2026-10-07):
                // with the field on the limit the finishers come home seconds
                // apart, and the next racer braked to a stop behind one parked
                // in its lane past the flag (Gillespie Gap's last bend).
                if (Retired || ShuttingDown)
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
            if (limitShare < 0f) RollPersona();
            // ...and hold a slide rather than spin it: the front wheels
            // pointed back along the car's travel (CounterSteer).
            steer = Mathf.Clamp(steer + CounterSteer(speed), -1f, 1f);

            // ---- target speed: THIS car's grip, at this driver's share ----
            // AT THE LIMIT (2026-10-07). The corner speed used to be planned on
            // mu = skill x weather, times 0.92 - about 0.8 g for cars that hold
            // well over 1.2 - and the brake went on at full the moment any
            // slower bend came into a window sized for 6.5 m/s2 of braking,
            // with 0.35 throttle all the way round it. Now (PlanSpeed): every
            // point up the road gets this car's own corner speed (AIGrip, read
            // live off the car: tyres, upgrades, faults, weather, downforce),
            // and the speed allowed HERE is the slowest of sqrt(vc^2 + 2 a d)
            // - a real braking profile on this car's real brakes.
            // The player's BUILD top speed: DeriveDrag solves the car to reach
            // exactly that on its own gearing, so it is the number the player
            // can actually reach, tuned or not.
            var rmNow = RaceManager.Instance;
            float playerVmax = rmNow != null && rmNow.playerCar != null ? rmNow.playerCar.BuildTopSpeedMps : 0f;
            float planned = PlanSpeed(speed, out bool zone, out float planDecel, out float bendSpeed, out float bendAtM);
            float targetSpeed = Mathf.Min(Mathf.Min(planned, TargetSpeedCap(skill, playerVmax)), TrafficCap());
            DebugTarget = targetSpeed;

            // ---- pedals: on the profile, not on/off round it ----
            // Over the target: the brake the profile is planned on (feed-
            // forward) plus what it takes to get back onto it. Just under it
            // while a bend ahead sets it: the brake tapering off - the trail
            // into the turn-in. Otherwise the throttle, flat out a metre a
            // second under the target, which is the whole of a corner's exit.
            float throttle = 0f, brake = 0f;
            float err = speed - targetSpeed;
            float fullDecel = AIGrip.BrakeDecel(car, speed);
            float ff = zone ? Mathf.Clamp01(planDecel / Mathf.Max(fullDecel, 1f)) : 0f;
            if (err > BrakeBandMps)
                brake = Mathf.Clamp01(ff + (err - BrakeBandMps) * BrakeGain);
            else if (zone && err > -TrailBandMps)
                brake = ff * Mathf.Clamp01((err + TrailBandMps) / (TrailBandMps + BrakeBandMps));
            else
                throttle = Mathf.Clamp01(HoldThrottle - err * ThrottleGain);
            // TRAIL BRAKING: what the tyres are spending on the turn is not
            // there for the brake (the friction circle) - never all of it, so
            // a car over the speed still slows.
            if (brake > 0f && car.Body != null)
            {
                float latUse = Mathf.Abs(car.Body.angularVelocity.y * speed) / (9.81f * Mathf.Max(AIGrip.LateralG(car, speed), 0.3f));
                brake = Mathf.Min(brake, Mathf.Max(MinTrailBrake, Mathf.Sqrt(Mathf.Max(0f, 1f - latUse * latUse))));
            }

            // SLIDES. The throttle used to drop to 0.4 at 0.25 rad of rear slip,
            // so nothing ever powerslid. Now it is eased only past this
            // driver's own tolerance (slideTol: 0.27-0.33 rad for a tidy
            // driver, 0.36-0.5 for a drifter), and the counter-steer above
            // holds the angle. A crawl's slip angle is noise (the arctangent
            // of centimetres a second), hence the speed floor - the ease once
            // held a rival pulling away up Chimney Rock's 8% switchbacks.
            var tsNow = TrafficSystem.Instance;
            bool oncomingNear = tsNow != null && tsNow.TwoWay && OncomingAhead(tsNow, SlideOncomingM);
            float tol = oncomingNear ? Mathf.Min(slideTol, GripSlideTolRad) : slideTol;
            // ...and the faster, the tidier: a slide is for a hairpin's exit,
            // not for 120 km/h between city walls (UptownLoop, the first runs:
            // a Charger let go on the power at speed, spun into a wall).
            float fast = Mathf.InverseLerp(SlideFreeMps, SlideTidyMps, speed);
            tol = Mathf.Lerp(tol, FastSlideTolRad, fast);
            float maxBody = Mathf.Lerp(MaxBodySlipRad, FastBodySlipRad, fast);
            if (speed > SlideEaseMinMps)
            {
                float rearSlip = Mathf.Abs(car.rearSlipAngle);
                if (rearSlip > tol) throttle *= Mathf.Clamp01(1f - (rearSlip - tol) / SlideFadeRad);
                // Getting away from it: off the throttle and let the car catch it.
                if (Mathf.Abs(car.chassisSlipAngle) > maxBody) throttle = 0f;
                // PUSHING (the fronts past their peak, a front-driver's exit
                // above all): ease off until they bite, or it runs wide.
                float frontSlip = Mathf.Abs(car.frontSlipAngle);
                if (frontSlip > FrontSlipTolRad) throttle *= Mathf.Clamp01(1f - (frontSlip - FrontSlipTolRad) / FrontSlipFadeRad);
                // PLOUGHING: on full lock with the fronts far past their peak
                // the car is going straight on, and more lock buys nothing -
                // scrub the speed off (the ABS keeps the wheels turning). The
                // racers took Chimney Rock's downhill hairpin at full lock and
                // 0.35-0.45 rad of front slip, off the pedals, and slid four
                // metres across the road.
                if (Mathf.Abs(steer) > PloughSteer && frontSlip > PloughSlipRad)
                {
                    throttle = 0f;
                    brake = Mathf.Max(brake, PloughBrake * Mathf.Clamp01((frontSlip - PloughSlipRad) / 0.15f));
                }
            }

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
            bool mistake = UpdateMistake(speed, bendSpeed, bendAtM);
            if (mistake)
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
            // THE HAIRPIN FLICK (drifters only): a pull of the handbrake at the
            // turn-in of a tight bend, the throttle then holding the slide.
            bool hb = UpdateFlick(dt, speed, steer, oncomingNear,
                                  !mistake && trafficBrake <= 0f && throttleLift <= 0.05f, ref throttle, ref brake);

            car.steerInput = steer;
            car.throttleInput = throttle;
            car.brakeInput = brake;
            car.handbrakeInput = hb;

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
        /// <summary>
        /// How far a car reaches across THIS car's road from its centre: its
        /// half-width straight, its half-LENGTH sideways. A rival spun across
        /// the road was passed as if it were 0.9 m wide - by a racer at 152
        /// km/h, into its nose (UptownLoop, the first runs).
        /// </summary>
        float HalfAcrossOf(CarController c)
        {
            var box = c != null ? c.GetComponent<BoxCollider>() : null;
            if (box == null) return HalfWidthOf(c);
            float hw = HalfWidthOf(c);
            float hl = Mathf.Clamp(box.size.z * Mathf.Abs(c.transform.lossyScale.z) * 0.5f, hw, 3f);
            float yaw = Vector3.Angle(Vector3.ProjectOnPlane(c.transform.forward, Vector3.up),
                                      Vector3.ProjectOnPlane(path.GetTangent(nearestIdx), Vector3.up)) * Mathf.Deg2Rad;
            return Mathf.Max(hw, Mathf.Abs(Mathf.Cos(yaw)) * hw + Mathf.Abs(Mathf.Sin(yaw)) * hl);
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
        /// <summary>How far up the road something closed on at this speed is
        /// looked at: 110 m, or its planned stopping distance and 30 m - at
        /// the speeds the field runs now, 110 m is 2.2 s at 50 m/s, half the
        /// stop.</summary>
        /// <summary>Seconds ahead within which a car on the line this racer
        /// is moving to counts as in its way.</summary>
        const float WantedLineS = 3.5f;
        float LookFor(float closing) =>
            Mathf.Clamp(Mathf.Max(closing, 0f) * Mathf.Max(closing, 0f) / (2f * BrakeDecelNow * PlanBrakeShare) + 30f, 110f, 260f);

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
                    if (OtherFloor(local)) continue;
                    // Only cars AHEAD. Reacting to a car alongside or behind turns
                    // every side-by-side moment into a swerve, and reacting to one
                    // behind hands the lead car's line to whoever is chasing it.
                    if (local.z < 1.5f || local.z > AvoidLookM) continue;
                    if (Mathf.Abs(local.x) > (OnDeck ? DeckLaneM : AvoidWidthM)) continue;

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
                    Vector3 lo0 = transform.InverseTransformPoint(o.transform.position);
                    if (OtherFloor(lo0)) continue;
                    // In a deck only a car IN THIS LANE, ahead, is in the way:
                    // the down lane is 2.4 m over and a racer creeping round the
                    // roof loop is not parked.
                    if (OnDeck && (lo0.z < -2f || Mathf.Abs(lo0.x) > DeckLaneM)) continue;
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
                        closingOnIt = lo.z > 1.5f && lo.z < LookFor(car.forwardSpeed - o.forwardSpeed) && Mathf.Abs(lo.x) < 6f;
                    }
                    if ((op != null && op.retired) || Mathf.Abs(o.forwardSpeed) < (OnDeck ? DeckStoppedMps : StoppedRacerMps) || closingOnIt)
                    {
                        stopped.Add(o.Body);
                        stoppedHalf.Add(HalfAcrossOf(o));
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
                bool roadS = traffic != null && traffic.ObstacleS.Count == nTraffic;
                float myS = nearestIdx * path.spacing + Vector3.Dot(transform.position - path.GetPoint(nearestIdx), path.GetTangent(nearestIdx));
                for (int i = 0; i < nTraffic + stopped.Count; i++)
                {
                    var rb = i < nTraffic ? traffic.Obstacles[i] : stopped[i - nTraffic];
                    if (rb == null) continue;
                    Vector3 local = transform.InverseTransformPoint(rb.position);
                    if (OtherFloor(local)) continue;
                    // ON ANOTHER LEG of the road (OncomingAhead): the car on the
                    // far side of a switchback stood "in the pass line" here.
                    if (i < nTraffic && roadS)
                    {
                        float ds = traffic.ObstacleS[i] - myS;
                        if (!path.HasEnds)
                        {
                            float tot = path.TotalLength;
                            if (ds > tot * 0.5f) ds -= tot; else if (ds < -tot * 0.5f) ds += tot;
                        }
                        if (Mathf.Abs(ds - local.z) > OtherLegM + OtherLegShare * Mathf.Abs(ds)) continue;
                    }
                    float along = Vector3.Dot(rb.linearVelocity, transform.forward);
                    float closing = mySpeed - along;
                    // Three seconds of closing, to 110 m: a rival closing at 22
                    // m/s on a car that then brake-checked it (a Braker, 70 m
                    // ahead) saw it 66 m out and hit it (Gillespie Gap, 2026-09-27).
                    float look = Mathf.Clamp(AvoidLookM + Mathf.Max(closing, 0f) * 3.2f, AvoidLookM, LookFor(closing));
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
                    // ...and nothing SOLID down it either: a city route's
                    // "road" can be both carriageways with a median between,
                    // and a racer passed a spun rival at 120 km/h along the
                    // median's barrier, into it (UptownLoop).
                    bool leftOk = Clear(left) && ShoulderClear(left, passEnd);
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
                        stopBrake = Mathf.Clamp01(bo.closing * bo.closing / (2f * room) / (BrakeDecelNow * PlanBrakeShare));
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
                        // GOING BY CLOSE, GOING BY SLOWER: past a car with under
                        // a metre and a half between the sides, no faster than
                        // PassClosingCap allows for that room. At the limit a
                        // racer came up on a Camry stopped in the other lane at
                        // 33 m/s with 0.6 m planned, drifted half a metre on the
                        // way, and hit it (Gillespie Gap, the first runs).
                        float sideRoom = Mathf.Abs(pick - bo.lat) - bo.half - myHalf;
                        float capClose = Mathf.Lerp(PassCloseMinMps, PassCloseMaxMps, Mathf.InverseLerp(PassRoomTightM, PassRoomEasyM, sideRoom));
                        if (bo.closing > capClose)
                        {
                            float roomC = Mathf.Max(gap - 2f, 0.5f);
                            float needC = (bo.closing * bo.closing - capClose * capClose) / (2f * roomC) / BrakeDecelNow;
                            if (needC > 0.1f)
                            {
                                trafficBrake = Mathf.Max(trafficBrake, Mathf.Clamp01(needC));
                                throttleLift = 1f;
                            }
                        }
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
                    // ...or where it is GOING: across the road to the line it
                    // wants. A racer at 182 km/h swung out to pass a rival and
                    // onto the line of a car stopped 100 m on, which nothing
                    // had braked for because it was not where the racer WAS
                    // (UptownLoop, the first runs: 40 m/s into it).
                    // Only what is reached SOON (WantedLineS): a car further
                    // up is past the end of the move, and braking for it
                    // stopped racers behind a finisher parked in their lane
                    // because another was parked up the other one.
                    float wantedAbs = wanted + lineOffset;
                    bool soon = o.z / Mathf.Max(o.closing, 0.3f) < WantedLineS;
                    float latLo = soon ? Mathf.Min(myLat, wantedAbs) : myLat, latHi = soon ? Mathf.Max(myLat, wantedAbs) : myLat;
                    float latDist = o.lat < latLo ? latLo - o.lat : o.lat > latHi ? o.lat - latHi : 0f;
                    float overlap = o.half + myHalf + 0.2f - latDist;
                    if (overlap <= 0f) continue;
                    float gapO = o.z - 4.5f;
                    float tHitO = Mathf.Max(gapO, 0f) / o.closing;
                    // Moving out of its way already, fast enough: the plan holds.
                    bool leaving = Mathf.Abs(wantedAbs - o.lat) >= o.half + myHalf;
                    if (leaving && tHitO > overlap / LateralRateMps * 1.3f) continue;
                    float roomO = Mathf.Max(gapO - 2f, 0.5f);
                    float need = Mathf.Clamp01(o.closing * o.closing / (2f * roomO) / BrakeDecelNow);
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
            // ON ITS ROOF or its side: it is not driving out of that, and four
            // seconds of it in the road is four seconds for the field to find
            // it (UptownLoop: a Charger upside down in its lane, hit at 40 m/s).
            bool flipped = transform.up.y < FlippedUpY;
            float limit = pinned || flipped ? PinnedSeconds : StuckSeconds;
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
