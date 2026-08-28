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

        void Awake()
        {
            car = GetComponent<CarController>();
            responder = GetComponent<CollisionResponder>();
        }

        void Start()
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
                car.steerInput = 0f; car.throttleInput = 0f; car.brakeInput = 0.4f;
                return;
            }

            float speed = Mathf.Abs(car.forwardSpeed);

            // ---- give way to whatever is about to be hit ----
            UpdateAvoidance(dt, out float throttleLift);

            // ---- steering: chase a lookahead point ----
            float lookDist = 7f + speed * 0.45f;
            int lookIdx = nearestIdx + Mathf.Max(2, Mathf.RoundToInt(lookDist / path.spacing));
            Vector3 target = path.GetPoint(lookIdx);
            Vector3 right = Vector3.Cross(Vector3.up, path.GetTangent(lookIdx));
            target += right * (lateralOffset + avoidBias);

            Vector3 local = transform.InverseTransformPoint(target);
            float steer = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(local.z, 0.5f)) * 1.4f, -1f, 1f);

            // ---- target speed from curvature ahead ----
            float mu = 1.0f * skill;
            float curvNow = Mathf.Max(path.MaxCurvatureAhead(nearestIdx, 6), 0.0005f);
            float cornerSpeed = Mathf.Sqrt(mu * 9.81f / curvNow) * 0.92f;
            float targetSpeed = Mathf.Min(cornerSpeed, 68f * skill);

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
        void UpdateAvoidance(float dt, out float throttleLift)
        {
            throttleLift = 0f;
            float wanted = 0f;
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

            // Slewed, not snapped: the offset feeds the steering target, and a
            // step change in it reads as a flick of the wheel.
            avoidBias = Mathf.MoveTowards(avoidBias, wanted, AvoidSlew * dt);
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
