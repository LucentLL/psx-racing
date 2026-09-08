using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Render-only body roll, dive and squat on the car's SHELL.
    ///
    /// Black Box did this as a separate layer from the physics — MW05's ecar
    /// carries BodyRoll / BodyDive / BodySquat as "degrees per g, max g,
    /// degrees per second" triplets scaled by chassis.RENDER_MOTION — and it
    /// is the right split here too. The Rigidbody already rolls on its springs
    /// and anti-roll bars; that motion is small by design (a rigid PS1 shell on
    /// a 240-line screen barely shows two degrees of it) and it is PHYSICS, so
    /// exaggerating it would change where the weight goes. This leans the mesh
    /// child and nothing else: the collider, the wheels and the solver never
    /// see it, and the wheels stay planted while the body moves over them —
    /// which is what makes a drift READ on a phone.
    ///
    /// Composed on top of the model's own yaw and offset from CarBody, so a
    /// pack whose shells face -Z still leans about the CAR'S axes.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarBodyLean : MonoBehaviour
    {
        public CarController car;
        public CarBody body;
        /// <summary>The shell's transform. Taken from <see cref="CarBody"/>
        /// when not wired, because that is the one component that knows which
        /// child is the body and which are the wheels.</summary>
        public Transform bodyRoot;

        // ---- MW-shaped constants (deg/g, cap in g, rate in deg/s) -----------
        // Modest on purpose: the springs already give real roll, so this is
        // the visible EXCESS. On a 1.72 m wide shell the 1.69 deg maximum here
        // (1.3 deg/g capped at 1.3 g) drops the outside sill about 2.5 cm —
        // enough to read from a chase camera, not enough to plant the sill in
        // the road. It was 1.8 x 1.5 = 2.7 deg, and 2.7 was measured against a
        // lean that ALSO pinned to its clamp for the whole of every slide,
        // because the g it read was v x yawRate rather than an accelerometer.
        // With the measure honest the same number read as too much.
        public const float RollDegPerG = 1.3f;
        public const float RollMaxG = 1.3f;
        public const float RollRateDegPerSec = 25f;
        public const float PitchDegPerG = 1.2f;
        public const float PitchMaxG = 1.0f;
        public const float PitchRateDegPerSec = 20f;
        /// <summary>The RENDER_MOTION multiplier. 1 = the constants as stated.</summary>
        public const float RenderMotion = 1f;
        /// <summary>Longitudinal g is DIFFERENCED off forward speed once a
        /// frame, and physics runs at 60 Hz under a render rate that may not
        /// be: the raw difference is zero on the frames between ticks and
        /// double on the frames after. Smoothed over this many seconds it
        /// averages to the true acceleration.</summary>
        const float LongGTau = 0.15f;
        /// <summary>The same filter on the LATERAL channel, which had none.
        /// Longer, because the lateral read is now a difference of the whole
        /// velocity vector and so carries every kerb and every wall.</summary>
        const float LatGTau = 0.20f;
        /// <summary>Seconds the lean takes to settle onto its target, on top of
        /// the degrees-per-second slew. MoveTowards alone is a linear ramp that
        /// arrives at the cap and stops dead; a first-order settle underneath it
        /// is what makes the shell ease in and ease out.</summary>
        const float RollTau = 0.30f;
        const float PitchTau = 0.25f;
        /// <summary>Above this many g the reading is a teleport or a wall, not
        /// the road — see the drop in LateUpdate. Well clear of the ~1.3 g the
        /// tyres can actually make and of the 1.3 g clamp on the lean itself.
        /// </summary>
        const float DiscontinuityG = 4f;

        /// <summary>Roll for a signed lateral g (positive = turning LEFT).
        /// The body rolls OUT of the turn — a left turn drops the right
        /// sill, which is a negative rotation about the car's forward axis.</summary>
        public static float RollDegFor(float latG) =>
            -Mathf.Clamp(latG, -RollMaxG, RollMaxG) * RollDegPerG * RenderMotion;

        /// <summary>Pitch for a signed longitudinal g (positive = accelerating).
        /// Braking dives the nose (positive rotation about the car's right
        /// axis), throttle squats it.</summary>
        public static float PitchDegFor(float longG) =>
            -Mathf.Clamp(longG, -PitchMaxG, PitchMaxG) * PitchDegPerG * RenderMotion;

        float roll, pitch;
        float longG, latG;
        float prevForward;
        Vector3 prevVel;
        bool havePrev;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (body == null) body = GetComponent<CarBody>();
            if (bodyRoot == null && body != null) bodyRoot = body.bodyRoot;
        }

        void LateUpdate()
        {
            if (bodyRoot == null || car == null || car.Body == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // AN ACCELEROMETER, not a kinematic estimate.
            //
            // This was v * yawRate, the centripetal acceleration of a car
            // following its nose. That is right in a steady grip corner and
            // badly wrong in a slide, where the yaw rate is decoupled from the
            // path: 25 m/s at 1.5 rad/s reports 3.8 g while the tyres are
            // making 1.3, so the lean pinned to its clamp and STAYED there for
            // the whole slide. And because yaw rate is mostly what steering
            // produces, that made it a steering-driven lean — the classic
            // arcade tell, and half of "the car roll is too aggressive".
            //
            // Differencing the velocity cannot report more than the tyres
            // actually delivered. Same sign as before in a steady turn, so the
            // shell still leans the way it always did; only the magnitude in a
            // slide changes, and only downward.
            Vector3 vel = car.Body.linearVelocity;
            float fwdV = Vector3.Dot(vel, transform.forward);

            // DIVIDED BY THE INTERVAL THE VELOCITY ACTUALLY CHANGED OVER, not
            // by the render frame. A Rigidbody's velocity only moves on a
            // physics step, so on a display faster than 60 Hz the difference
            // spans at most one step of fixedDeltaTime however short the frame
            // was — dividing by the frame instead reports an ordinary 1.3 g
            // corner as 5 g at 240 Hz, which the discontinuity test below would
            // then throw away as a collision. Slower than 60 Hz the frame IS
            // the interval and dt is right. Max of the two is correct in both.
            float aDt = Mathf.Max(dt, Time.fixedDeltaTime);
            Vector3 accel = havePrev ? (vel - prevVel) / aDt : Vector3.zero;
            prevVel = vel;
            float rawLatG = Vector3.Dot(accel, transform.right) / 9.81f;
            // A DIFFERENCE OF VELOCITIES IS ALSO A TELEPORT DETECTOR, and this
            // is the price of reading a real accelerometer. ResetTo and
            // DriveSession.Respawn zero the body's velocity in one step, and a
            // wall does something close to it: the difference then reports tens
            // of g, which the clamp turns into a full-lean lurch held for the
            // length of the filter. No tyre makes more than about two g, so
            // anything past DiscontinuityG did not come from the road and the
            // sample is dropped rather than smoothed — a lean that is not there
            // beats a lean that is wrong. The longitudinal channel gets the
            // same treatment; it always had this exposure through prevForward
            // and simply never had the check.
            if (havePrev && Mathf.Abs(rawLatG) > DiscontinuityG) rawLatG = latG;
            float rawLongG = havePrev ? (fwdV - prevForward) / aDt / 9.81f : 0f;
            if (havePrev && Mathf.Abs(rawLongG) > DiscontinuityG) rawLongG = longG;
            prevForward = fwdV;
            havePrev = true;
            latG = Mathf.Lerp(latG, rawLatG, 1f - Mathf.Exp(-dt / LatGTau));
            longG = Mathf.Lerp(longG, rawLongG, 1f - Mathf.Exp(-dt / LongGTau));

            // Airborne there is nothing to lean against; let it settle flat.
            bool grounded = car.anyWheelGrounded;
            float wantRoll = grounded ? RollDegFor(latG) : 0f;
            float wantPitch = grounded ? PitchDegFor(longG) : 0f;
            // Settle onto the target, then bound the rate — the slew alone is a
            // ramp that hits the cap and stops.
            float nextRoll = Mathf.Lerp(roll, wantRoll, 1f - Mathf.Exp(-dt / RollTau));
            float nextPitch = Mathf.Lerp(pitch, wantPitch, 1f - Mathf.Exp(-dt / PitchTau));
            roll = Mathf.MoveTowards(roll, nextRoll, RollRateDegPerSec * dt);
            pitch = Mathf.MoveTowards(pitch, nextPitch, PitchRateDegPerSec * dt);

            // Lean in CAR space, then the model's own yaw underneath it, so the
            // shell rolls about the car's nose-to-tail line whichever way the
            // pack happened to export it facing.
            var def = body != null ? body.Def : null;
            float modelYaw = def != null ? def.bodyYaw : 0f;
            bodyRoot.localRotation = Quaternion.Euler(pitch, 0f, roll) *
                                     Quaternion.Euler(0f, modelYaw, 0f);
        }
    }
}
