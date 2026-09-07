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
        // Starting values. Modest on purpose: the springs already give real
        // roll, so this is the visible EXCESS, and on a 1.72 m wide shell a
        // 2.7 deg lean drops the outside sill about 4 cm — enough to read from
        // a chase camera, not enough to plant the sill in the road.
        public const float RollDegPerG = 1.8f;
        public const float RollMaxG = 1.5f;
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
        float longG;
        float prevForward;
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

            Vector3 vel = car.Body.linearVelocity;
            float fwdV = Vector3.Dot(vel, transform.forward);
            // Centripetal acceleration in a steady turn is v * yawRate, and the
            // sign of the yaw rate about the car's up axis is the side the car
            // is turning to.
            float yawRate = Vector3.Dot(car.Body.angularVelocity, transform.up);
            float latG = fwdV * yawRate / 9.81f;

            float rawLongG = havePrev ? (fwdV - prevForward) / dt / 9.81f : 0f;
            prevForward = fwdV;
            havePrev = true;
            longG = Mathf.Lerp(longG, rawLongG, 1f - Mathf.Exp(-dt / LongGTau));

            // Airborne there is nothing to lean against; let it settle flat.
            bool grounded = car.anyWheelGrounded;
            float wantRoll = grounded ? RollDegFor(latG) : 0f;
            float wantPitch = grounded ? PitchDegFor(longG) : 0f;
            roll = Mathf.MoveTowards(roll, wantRoll, RollRateDegPerSec * dt);
            pitch = Mathf.MoveTowards(pitch, wantPitch, PitchRateDegPerSec * dt);

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
