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
            target += right * (lateralOffset + avoidBias);

            Vector3 local = transform.InverseTransformPoint(target);
            return Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f)) * 1.4f, -1f, 1f);
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
        /// half-widths and a little air.</summary>
        const float TrafficClearM = 2.4f;
        /// <summary>How close to the tarmac edge a pass may take the car's
        /// centre: a half-width and a little shoulder.</summary>
        const float EdgeMarginM = 1.15f;
        /// <summary>Metres behind a traffic car a racer that cannot pass holds
        /// station at, plus this many seconds of its own speed.</summary>
        const float FollowGapM = 7f, FollowTimeS = 0.45f;
        /// <summary>The deceleration a full brake input gives, about - for
        /// turning a stopping distance into a pedal.</summary>
        const float BrakeDecel = 8.5f;

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

            if (!driving)
            {
                float rolling = Mathf.Abs(car.forwardSpeed);
                bool coasting = ShuttingDown && rolling > ShutdownRestMps;
                car.steerInput = coasting ? SteerToLine(rolling) : 0f;
                car.throttleInput = 0f;
                car.brakeInput = coasting ? ShutdownBrake : 0.4f;
                return;
            }

            float speed = Mathf.Abs(car.forwardSpeed);

            // ---- a serious crash ends this car's race ----
            if (responder != null && (responder.WorstHit >= RetireHitMps || responder.DamageScore >= RetireDamage))
            {
                RaceManager.Instance?.RetireCar(car);
                if (Retired) return;
            }

            // ---- give way to whatever is about to be hit ----
            UpdateAvoidance(dt, out float throttleLift, out float trafficBrake);

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

            // Ease off throttle while sliding
            if (Mathf.Abs(car.rearSlipAngle) > 0.25f) throttle *= 0.4f;

            // Closing on a car ahead: lift, and brake if the gap is going away
            // fast. Applied after the corner logic so it can only ever slow the
            // AI down, never talk it into more throttle than the corner allows.
            if (throttleLift > 0f)
            {
                throttle *= 1f - Mathf.Clamp01(throttleLift);
                if (throttleLift > 0.7f) brake = Mathf.Max(brake, (throttleLift - 0.7f) * 1.5f);
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

            UpdateRecovery(dt, speed);
        }

        /// <summary>
        /// Nudge off the line of any car this one is closing on, and lift when the
        /// gap is shutting. Not overtaking logic — the AI has no notion of a pass,
        /// and pretending otherwise would have it dive for gaps it cannot make.
        /// The goal is only that a car ahead stops being furniture.
        /// </summary>
        struct Obs { public float z, lat, closing; public bool oncoming; }
        readonly System.Collections.Generic.List<Obs> obs = new System.Collections.Generic.List<Obs>(16);

        void UpdateAvoidance(float dt, out float throttleLift, out float trafficBrake)
        {
            throttleLift = 0f;
            trafficBrake = 0f;
            float wanted = 0f;
            float slew = AvoidSlew;
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
                    if (Mathf.Abs(pull) > Mathf.Abs(wanted)) wanted = pull;

                    float closing = car.forwardSpeed - other.forwardSpeed;
                    if (closing > 0.5f)
                        throttleLift = Mathf.Max(throttleLift,
                            closeness * Mathf.Clamp01(closing / 8f));
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
            if (traffic != null && traffic.Obstacles.Count > 0)
            {
                Vector3 myRight = Vector3.Cross(Vector3.up, path.GetTangent(nearestIdx)).normalized;
                float myLat = Vector3.Dot(transform.position - path.GetPoint(nearestIdx), myRight);
                float edge = Mathf.Max(0.5f, path.roadWidth * 0.5f - EdgeMarginM);
                float mySpeed = car.forwardSpeed;
                obs.Clear();
                var bodies = traffic.Obstacles;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var rb = bodies[i];
                    if (rb == null) continue;
                    Vector3 local = transform.InverseTransformPoint(rb.position);
                    float along = Vector3.Dot(rb.linearVelocity, transform.forward);
                    float closing = mySpeed - along;
                    float look = Mathf.Clamp(AvoidLookM + Mathf.Max(closing, 0f) * 2.4f, AvoidLookM, 80f);
                    if (local.z < -3f || local.z > look) continue;
                    int oi = path.NearestIndex(rb.position, nearestIdx);
                    Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(oi)).normalized;
                    float lat = Vector3.Dot(rb.position - path.GetPoint(oi), r);
                    obs.Add(new Obs { z = local.z, lat = lat, closing = closing, oncoming = along < -1f });
                }

                // The one to deal with: the nearest ahead, in this corridor.
                int block = -1;
                for (int i = 0; i < obs.Count; i++)
                {
                    var o = obs[i];
                    if (o.z < 1.5f || Mathf.Abs(o.lat - myLat) >= TrafficClearM) continue;
                    if (o.closing <= 0.3f) continue;
                    if (block < 0 || o.z < obs[block].z) block = i;
                }
                if (block >= 0)
                {
                    var bo = obs[block];
                    // How far the pass runs: to the car, and a car's length
                    // and a second past it at the speed we pass it at.
                    float passEnd = bo.z + 6f + Mathf.Max(bo.closing, 3f) * 1f;
                    bool Clear(float line)
                    {
                        if (Mathf.Abs(line) > edge) return false;
                        for (int i = 0; i < obs.Count; i++)
                        {
                            if (i == block) continue;
                            var o = obs[i];
                            if (Mathf.Abs(o.lat - line) >= TrafficClearM) continue;
                            // An oncoming car is met sooner than it stands: it
                            // comes to us while we go to it. Anything within
                            // the pass, meeting point included, blocks the line.
                            float meet = o.oncoming ? o.z * mySpeed / Mathf.Max(mySpeed + Mathf.Abs(o.closing - mySpeed), 1f) : o.z;
                            if (meet > -3f && meet < passEnd + (o.oncoming ? 25f : 0f)) return false;
                        }
                        return true;
                    }
                    float left = bo.lat - TrafficClearM - 0.2f;
                    float right = bo.lat + TrafficClearM + 0.2f;
                    bool leftOk = Clear(left), rightOk = Clear(right);
                    float pick = float.NaN;
                    if (leftOk && rightOk)
                        pick = Mathf.Abs(left - myLat) <= Mathf.Abs(right - myLat) + 0.5f ? left : right;
                    else if (leftOk) pick = left;
                    else if (rightOk) pick = right;

                    // Time to be beside it versus time to reach it: if the
                    // move will not be made in time, brake for the gap too.
                    float gap = bo.z - 4.5f;
                    float need = FollowGapM + Mathf.Max(mySpeed, 0f) * FollowTimeS;
                    float stopBrake = 0f;
                    if (bo.closing > 0.3f)
                    {
                        float room = Mathf.Max(gap - need, 0.5f);
                        stopBrake = Mathf.Clamp01(bo.closing * bo.closing / (2f * room) / BrakeDecel);
                    }
                    if (!float.IsNaN(pick))
                    {
                        wanted = pick - lateralOffset;
                        slew = TrafficSlew;
                        float lateralLeft = Mathf.Max(0f, TrafficClearM - Mathf.Abs(bo.lat - myLat));
                        float tSide = lateralLeft / TrafficSlew;
                        float tHit = gap / Mathf.Max(bo.closing, 0.3f);
                        if (tHit < tSide * 1.3f) trafficBrake = Mathf.Max(trafficBrake, stopBrake);
                        throttleLift = Mathf.Max(throttleLift, Mathf.Clamp01(tSide * 1.3f / Mathf.Max(tHit, 0.1f)) * 0.6f);
                    }
                    else
                    {
                        // Nowhere to go: follow it.
                        wanted = Mathf.Clamp(myLat - lateralOffset, -edge - lateralOffset, edge - lateralOffset);
                        trafficBrake = Mathf.Max(trafficBrake, stopBrake);
                        if (gap < need * 1.5f) throttleLift = 1f;
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
                        keep = Mathf.Clamp(keep, -edge, edge) - lateralOffset;
                        if (block < 0 || Mathf.Abs(keep) > Mathf.Abs(wanted)) { wanted = keep; slew = TrafficSlew; }
                    }
                }
            }

            // Slewed, not snapped: the offset feeds the steering target, and a
            // step change in it reads as a flick of the wheel.
            avoidBias = Mathf.MoveTowards(avoidBias, wanted, slew * dt);
        }

        /// <summary>
        /// Put the car back on the road when it can no longer get there itself.
        /// Two failure modes, two clocks: pinned against something solid, and
        /// pointed the wrong way.
        /// </summary>
        void UpdateRecovery(float dt, float speed)
        {
            bool pinned = responder != null && responder.InWallContact;
            float limit = pinned ? PinnedSeconds : StuckSeconds;
            if (speed < 1f) stuckTimer += dt;
            else stuckTimer = 0f;

            // Wrong way: measured against the road, not against the car's own
            // velocity, so a car sliding backwards through a corner it is still
            // steering through does not trip it.
            float alignment = Vector3.Dot(transform.forward, path.GetTangent(nearestIdx));
            if (alignment < WrongWayDot && speed > WrongWayMinSpeed) wrongWayTimer += dt;
            else wrongWayTimer = 0f;

            if (Retired) return;
            if (stuckTimer > limit || wrongWayTimer > WrongWaySeconds)
            {
                stuckTimer = 0f;
                wrongWayTimer = 0f;
                avoidBias = 0f;
                RaceManager.Instance?.RespawnCar(car);
            }
        }
    }
}
