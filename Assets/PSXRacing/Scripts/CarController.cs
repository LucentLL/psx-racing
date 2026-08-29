using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Arcade-sim car physics: raycast suspension, per-wheel friction-circle
    /// tire model, and a gear/torque-curve engine using GT4 data for the
    /// Mazda RX-7 Type RS (FD) '98 — 280 PS, 1280 kg, FR, 6-speed,
    /// redline 7500 / rev limit 8000.
    ///
    /// The tire model and the drift layer are ported from Racing Game 2
    /// (src/physics/tire.ts and the Phase 0B force integrator). Mechanics that
    /// a 3D Rigidbody produces for free — weight transfer, centripetal
    /// coupling, per-corner load — are deliberately NOT ported; they emerge
    /// from the four raycast suspension anchors. What IS ported is the
    /// gameplay layer that has no physical analogue: the wheelspin yaw
    /// injector, the e-brake mu collapse, the drift state machine, and the
    /// yaw damping tiers that let a slide feel weightless but still end.
    ///
    /// Note on units: the source game works in "world pixels" at 6.2746 wpx/m,
    /// so its speed gates (8, 12, 5 ...) are NOT metres per second. They are
    /// converted here — the source's 8 gu/s handbrake gate is 1.3 m/s.
    /// </summary>
    public class CarController : MonoBehaviour
    {
        // ---- GT4 spec: Mazda RX-7 Type RS (FD) '98 -------------------------
        [Header("Chassis (GT4)")]
        public float massKg = 1280f;
        public float wheelbase = 2.425f;     // wb: 2425 mm
        public float trackWidth = 1.46f;     // trF/trR: 1460 mm
        public float wheelRadius = 0.31f;    // 255/40 R17
        public float weightDistFront = 0.5f; // wdF: 50
        /// <summary>CG height above ground. h/L = 0.1856 matches true geometry;
        /// the source's effective ratio was 0.1746.</summary>
        public float cgHeight = 0.465f;
        /// <summary>Yaw inertia as a fraction of the box-slab value. The 2D
        /// source ran 0.55, but in a 3D rig that reads as a car pivoting about
        /// its own boot — it changes heading far too willingly. NFS-era arcade
        /// handling feels heavy in yaw, so this sits much closer to slab.</summary>
        public float yawInertiaScale = 0.85f;

        /// <summary>Default curve: the RX-7's 13B-REW, 1000-8000 RPM in 500 RPM
        /// steps. Used when no CarSpec has been applied, which keeps standalone
        /// editor play identical to how it has always driven.</summary>
        static readonly float[] DefaultTorqueNm =
        { 147f, 201f, 253f, 280f, 295f, 307f, 309f, 314f, 313f, 313f, 312f, 303f, 274f, 244f, 206f };
        const float DefaultCurveStartRPM = 1000f;
        const float DefaultCurveStepRPM = 500f;

        // Per-instance curve, so different catalog cars pull differently. Null
        // until a spec is applied; GetTorqueAtRPM falls back to the default.
        float[] curveRPM, curveNm;

        [Header("Engine / Drivetrain")]
        public float idleRPM = 900f;
        public float redlineRPM = 7500f;
        public float revLimitRPM = 8000f;
        public float finalDrive = 4.10f;
        public float drivetrainEfficiency = 0.88f;
        public float[] gearRatios = { 3.483f, 2.015f, 1.391f, 1.000f, 0.806f, 0.700f };
        public float reverseRatio = 3.6f;
        public float shiftTime = 0.18f;
        public float upshiftRPM = 7200f;
        public float downshiftRPM = 3400f;
        public float topSpeedMps = 64.75f;
        /// <summary>Crank drag off throttle, Nm: 38 at idle rising to 78 at
        /// redline. Deliberately modest — a 13B has no valvetrain and famously
        /// weak engine braking. Rear-axle only, so it gives lift-off oversteer.</summary>
        public float engineBrakeBaseNm = 38f;
        public float engineBrakeRpmNm = 40f;
        public float clutchEngageSpeed = 3.5f;
        /// <summary>AI cars never need reverse, and holding them on the brake at
        /// the grid would otherwise select it and drive them backwards.</summary>
        public bool allowReverse = true;
        /// <summary>Share of drive torque sent to the front axle: 0 = RWD,
        /// 1 = FWD, 0.4 = the 4WD split. Set from the CarSpec's drv field.</summary>
        [Range(0f, 1f)] public float frontDriveShare;
        /// <summary>The catalog entry currently applied, or null for the
        /// built-in RX-7 spec the controller ships with.</summary>
        public CarSpec activeSpec { get; private set; }

        [Header("Arcade stabilizers (NFS Underground / MW05 / Carbon feel)")]
        /// <summary>Sideways-velocity damping at the CG, 1/s. This is the single
        /// biggest difference between a sim and an NFS-style arcade car: the game
        /// actively deletes lateral velocity so the car feels bolted to the road,
        /// then relaxes it while drifting so slides still work. It deliberately
        /// fights the tire model — that is the point.</summary>
        public float lateralDampGrip = 4.5f;
        public float lateralDampDrift = 0.6f;
        /// <summary>Ceiling on the stabilizer in g, so it assists rather than
        /// teleports the car sideways.</summary>
        public float lateralDampMaxG = 0.7f;
        /// <summary>Speed (m/s) below which the yaw injector is fully suppressed.
        /// Without this, full throttle at walking pace spins the car on the spot.</summary>
        public float yawInjectorMinSpeed = 4f;
        public float yawInjectorFullSpeed = 12f;
        /// <summary>How long after an impact the stabilizers stay stood down.
        /// Scaled by hit severity — see <see cref="RegisterImpact"/>.</summary>
        public float impactGraceWindow = 0.45f;
        /// <summary>How far the lateral damper is cut at peak grace. Not to zero:
        /// a fully unassisted car after a heavy hit is unrecoverable, and the
        /// target is "knocked off line", not "spun out and beached".</summary>
        [Range(0f, 1f)] public float impactStabilizerCut = 0.85f;

        [Header("Fault handicaps (set by RaceHandoffApplier from the LifeSim)")]
        /// <summary>All neutral by default, so a race played standalone in the
        /// editor drives exactly as it always has. A car carrying faults races
        /// worse — that is the whole point of the garage economy being wired to
        /// the track rather than being a spreadsheet.</summary>
        public float faultAccelMult = 1f;
        public float faultGripMult = 1f;
        public float faultBrakeMult = 1f;
        public float faultShiftMult = 1f;
        /// <summary>Signed steering bias, added to the steering target.</summary>
        public float faultSteerPull;

        [Header("Tires")]
        public float roadGrip = 1.25f;
        /// <summary>The source's 0.55 is a MULTIPLIER on base grip, not an
        /// absolute: 1.05 * 0.55 = 0.578.</summary>
        public float offroadGrip = 0.72f;
        /// <summary>N/rad per N of Fz. Hard ceiling around 13 — the source tried
        /// raising this and reverted it with a "rear warps left to right" spike.</summary>
        public float corneringStiffness = 11.0f;
        /// <summary>Staggered tires (235 front / 255 rear) give the rear more
        /// grip than the front, so the front saturates first. That is the FD's
        /// designed limit understeer, and it is what keeps the car catchable.</summary>
        public float tireMuFront = 1.010f;
        public float tireMuRear = 1.030f;
        /// <summary>Longitudinal speed floor in the slip-angle denominator (m/s).
        /// Too large and the car feels numb turning in at low speed.</summary>
        public float slipEpsilon = 0.30f;
        const float SlipPeak = 0.17f;
        /// <summary>Lateral velocity a GRIPPING tyre carries, as a fraction of
        /// its rolling speed: tan of the slip angle where its curve peaks.
        /// Everything above this is scrub. Precomputed — it is read four times
        /// per car per physics tick.</summary>
        const float SlipPeakTan = 0.1717f;
        /// <summary>How abruptly a wheel goes from gripping to locked once the
        /// demand passes its friction circle. At 2.5 a demand 40% over the
        /// circle already reads as fully locked, which is about how quickly a
        /// real wheel stops turning once the pads win.</summary>
        const float LockSharpness = 2.5f;
        /// <summary>Surface speed a fully spinning tyre is treated as having
        /// when the car itself is barely moving (m/s). There is no wheel
        /// angular velocity in this model to read it from, and without a floor
        /// a standing burnout — the smokiest thing a car does — scrubs at zero.
        /// </summary>
        const float SpinScrubSpeed = 6f;

        [Header("Suspension (GT4 susp[]: 4.8 / 3.6 kgf/mm)")]
        public float springRateFront = 47100f;
        public float springRateRear = 35300f;
        public float damperFront = 4000f;
        public float damperRear = 3400f;
        public float antiRollFront = 16000f;
        public float antiRollRear = 12000f;
        /// <summary>Cap the anti-roll couple at half the static axle load, so a
        /// one-wheel kerb strike cannot launch the car.</summary>
        public float antiRollMaxForce = 6280f;
        /// <summary>Ceiling on a single wheel's spring+damper force, as a
        /// multiple of its static load. The road sits ~13 cm proud of the gravel,
        /// so rejoining the track steps compression in one tick; without a cap
        /// the damper term alone spikes to ~24 kN and launches the car.</summary>
        public float maxSuspensionForceRatio = 5f;
        public float restLength = 0.30f;
        public float mountHeight = 0.55f;

        [Header("Aero")]
        public float dragCoefficient = 0.34f;
        /// <summary>N per (m/s)^2 pressing the car down. DERIVED, not configured
        /// — see <see cref="DeriveDownforce"/>; the value here is only what an
        /// un-Awakened prefab shows in the inspector.</summary>
        public float downforceCoefficient = 1.05f;
        /// <summary>
        /// Downforce at a car's own top speed, as a fraction of its own weight.
        /// This is the P4 knob, and it is expressed as a FRACTION rather than as
        /// a coefficient so it means the same thing on every car in the catalog:
        /// a 950 kg hatchback and a 1700 kg GT both gain 35% of their weight at
        /// the point where they are hardest to hold, instead of the hatchback
        /// gaining 60% and the GT 15% off one shared number.
        ///
        /// 0.35 lands the reference FD at a 1.05 coefficient, which is where the
        /// handling notes wanted this (they suggested trying 1.0-1.2 by hand);
        /// deriving it means the same feel now transfers to all 317 cars.
        /// The old flat 0.35 coefficient gave the FD 11% of its weight — enough
        /// to measure and not enough to feel.
        /// </summary>
        public float downforceWeightFractionAtVmax = 0.35f;
        public float rollingResistance = 165f;

        [Header("Steering")]
        public float maxSteerLowSpeedDeg = 34f;
        public float maxSteerHighSpeedDeg = 12f;
        /// <summary>At 34 deg falling to 22 at speed, a deep slide is
        /// mathematically uncatchable: the front wheel cannot make its slip
        /// angle change sign, so counter-steer is cosmetic. This is not a grip
        /// cheat — the friction circle still bounds lateral force.</summary>
        public float maxSteerDriftDeg = 45f;
        public float steerSpeedFalloff = 55f;
        public float steerRateDeg = 220f;
        public float steerRateDriftDeg = 400f;   // lock-to-lock in 0.3 s
        public float gripBonus = 1f;

        // ---- bolt-on mods (LifeSim parts shop) ----
        /// <summary>Welded rear diff. Set from the owned car's mods.</summary>
        public bool weldedDiff;
        const float WeldedSpinGain = 1.3f;
        /// <summary>Roots blower fitted. Multiplies engine torque on the curve
        /// below, NOT the drive force — so gearing, wheelspin and the yaw
        /// injector all see the extra torque the way they see the engine's.</summary>
        public bool supercharged;
        /// <summary>Peak boost multiplier, held to 60% of the rev range then
        /// tapering to +15% at redline. That flat-then-taper shape is ROOTS
        /// character: a positive-displacement blower moves a fixed volume per
        /// revolution, so it makes its boost immediately and runs out of breath
        /// at the top — which is also why it suits the NA muscle cars this mod
        /// is offered on and not the turbo cars, which already have boost.</summary>
        const float SuperchargerPeak = 1.30f;
        const float SuperchargerTop = 1.15f;
        const float SuperchargerTaperStart = 0.6f;

        [Header("Brakes")]
        public float brakeDemandG = 0.9f;
        public float brakeFrontShare = 0.6f;

        /// <summary>Layer holding the drivable road surface. Checked by layer
        /// rather than by collider name: reading Collider.name allocates a
        /// managed string on every wheel of every car on every physics tick.</summary>
        public int roadLayer = 8;

        /// <summary>Layer holding walls, buildings and other solid scenery. The
        /// suspension rays must NOT see it: with no mask at all a wheel can
        /// "ground" on a barrier face or a building wall and take spring force
        /// from it, which launches the car when it pitches near a barrier.</summary>
        public int solidLayer = 9;

        [Header("Drift feel (Racing Game 2 gameplay layer)")]
        /// <summary>0 = simulator, 1 = maximum forgiveness. The source ships 0.3.</summary>
        [Range(0f, 1f)] public float countersteerAssist = 0.55f;
        [Range(0f, 2f)] public float brakeStabDrift = 1f;
        [Range(0f, 2f)] public float wheelspinYawGain = 1f;

        // ---- ported tuning constants ---------------------------------------
        const float EbrakeWindow = 0.75f;
        const float EbrakeMuCollapse = 0.70f;     // rear mu -> 30% at full window
        const float EbrakeKickCooldown = 0.15f;
        const float EbrakeKickBase = 1.2f;        // rad/s
        const float DriveGateSpeed = 1.3f;        // source 8 gu/s
        const float ThrottleSustainWindow = 0.4f;
        const float BrakeStabWindow = 0.35f;
        const float BrakeStabCooldown = 0.3f;
        const float BrakeStabBase = 0.55f;
        const float DriftEnterSlip = 0.32f;
        const float DriftExitSlip = 0.10f;
        const float DriftStopSpeed = 0.8f;        // source 5 gu/s
        const float PostDriftLockout = 0.5f;
        const float CountersteerDeadzone = 0.14f;
        const float CountersteerMinSpeed = 2.0f;  // source 12 gu/s
        const float CountersteerMaxAccel = 3.0f;
        const float MaxBodySlipForSustain = 1.3f; // ~75 deg; without it, donuts never end

        // ---- yaw layer (P4) ------------------------------------------------
        // These were fifteen bare numbers inline in ApplyYawLayer and
        // ApplyLateralStabilizer. Every one of them is a tuning decision, and a
        // tuning session that has to find them by reading the algorithm is a
        // tuning session that changes the wrong one. Named here, used once each
        // below; the values are unchanged.

        /// <summary>How much steering the injector needs before it will rotate
        /// the car. The handbrake gate is near-zero because pulling the lever IS
        /// the request — you should be able to kick the tail out on a whiff of
        /// lock — while on throttle alone it takes a real commitment.</summary>
        const float InjectorSteerGate = 0.35f;
        const float InjectorSteerGateEbrake = 0.05f;
        /// <summary>Injector strength by mode: a subtle rotation on corner exit,
        /// enough to SUSTAIN an existing slide, and a committed entry off the
        /// handbrake. The 10x spread between the first and last is what keeps
        /// normal driving from feeling like it is always half-drifting.</summary>
        const float InjectorGrip = 0.20f;
        const float InjectorDrift = 1.5f;
        const float InjectorEbrake = 2.0f;
        /// <summary>Off the tarmac there is less to push against.</summary>
        const float InjectorOffroadMult = 0.6f;
        /// <summary>Share of the rear friction circle the injector may borrow as
        /// a yaw couple. Not a physical quantity — it is the scale factor that
        /// makes the whole term land in the right order of magnitude.</summary>
        const float InjectorCircleShare = 0.8f;

        /// <summary>Below this the driver is not asking for a direction.</summary>
        const float YawSteerNeutral = 0.10f;
        /// <summary>Below this on BOTH controls the driver has let go entirely,
        /// which is the one case where the car should tidy itself up hardest.
        /// </summary>
        const float YawDriverIdleInput = 0.05f;
        /// <summary>Body-slip window (rad) over which hands-off damping ramps
        /// from its floor to its ceiling — ~34 deg to ~69 deg.</summary>
        const float YawSlipRampStart = 0.6f;
        const float YawSlipRampWidth = 0.6f;
        /// <summary>What counts as catching it: real opposite lock against a
        /// yaw rate that is actually going somewhere.</summary>
        const float YawCounterSteerInput = 0.4f;
        const float YawCounterSteerRate = 0.3f;
        /// <summary>The four-tier damping table, ~2.5x the 2D source's numbers.
        /// That game had a synthetic heading integrator holding the car straight;
        /// a Rigidbody has nothing equivalent, so the source values leave the car
        /// rotating long after the driver stopped asking. The 4x spread between
        /// Committed and NeutralMax is the whole "weightless slide that still
        /// ends cleanly" character.</summary>
        const float YawDampIdle = 1.8f;
        const float YawDampNeutralMin = 2.2f;
        const float YawDampNeutralMax = 4.0f;
        const float YawDampCounter = 2.2f;
        const float YawDampCommitted = 0.45f;
        const float YawDampGrip = 1.6f;

        /// <summary>Assist torque per radian of body slip past the deadzone.</summary>
        const float CountersteerGain = 15f;
        /// <summary>Steering input at which the assist has fully backed out, so
        /// held opposite lock is the player's alone.</summary>
        const float CountersteerReleaseSpan = 0.55f;

        /// <summary>Lateral velocity below which the stabilizer has nothing worth
        /// correcting, and the speed over which it fades in so that parking
        /// manoeuvres are not rail-roaded.</summary>
        const float LateralDampDeadzone = 0.05f;
        const float LateralDampFadeSpeed = 3f;

        // ---- runtime state -------------------------------------------------
        [HideInInspector] public float throttleInput;
        [HideInInspector] public float brakeInput;
        [HideInInspector] public float steerInput;
        [HideInInspector] public bool handbrakeInput;

        [HideInInspector] public float currentRPM;
        [HideInInspector] public int currentGear = 1;
        [HideInInspector] public bool manualMode;
        [HideInInspector] public float speedKmh;
        [HideInInspector] public float forwardSpeed;
        [HideInInspector] public float rearSlipAngle;
        [HideInInspector] public float frontSlipAngle;
        [HideInInspector] public float chassisSlipAngle;
        /// <summary>Smoothed 0..1, for audio and visuals only.</summary>
        [HideInInspector] public float wheelSpin;
        /// <summary>Unsmoothed 0..2 friction-circle exceedance. Drives the yaw
        /// injector, which needs the immediate value, not a 0.25 s average.</summary>
        [HideInInspector] public float wheelspinRatio;
        [HideInInspector] public bool anyWheelGrounded;
        [HideInInspector] public bool onRoad = true;

        public bool Drifting { get; private set; }
        public float EbrakeTimer { get; private set; }
        /// <summary>1 immediately after an impact, decaying to 0 across the grace
        /// window. Read by the stabilizer and the counter-steer assist.</summary>
        public float ImpactGrace01 { get; private set; }
        /// <summary>True while the ECU is actually cutting fuel. Audio gates the
        /// on-the-limiter recordings on this rather than on RPM position, because
        /// RPM alone cannot tell "deep in the red" from "bouncing off the cut".</summary>
        public bool RevLimiterActive { get; private set; }
        public Rigidbody Body { get; private set; }

        public Transform[] wheelHubs = new Transform[4];
        public Transform[] wheelMeshes = new Transform[4];

        /// <summary>
        /// Where one tyre meets the ground and how hard it is scrubbing across
        /// it. Published for the VISUALS — the skid marks and the smoke — which
        /// otherwise have to raycast for a contact patch the physics already
        /// found this tick, four times per car per frame.
        ///
        /// <see cref="slide"/> is the part that matters and the part worth
        /// naming carefully: it is metres per second of tyre sliding over
        /// tarmac, not slip angle and not a 0..1 "driftiness". A tyre generates
        /// its grip THROUGH a few degrees of slip, so any measure that starts
        /// at zero slip paints a black line through every corner taken at
        /// walking pace. This one is zero until the tyre is past the peak of
        /// its own curve and then grows with how fast the rubber is actually
        /// moving over the road, which is also what decides how much smoke
        /// comes off it.
        /// </summary>
        public struct WheelContact
        {
            public bool grounded;
            /// <summary>World contact patch, from the suspension ray's hit.</summary>
            public Vector3 point;
            /// <summary>Surface normal there — a mark on a banked corner has to
            /// lie in the road, not in the horizontal plane.</summary>
            public Vector3 normal;
            /// <summary>Wheel heading, steer included. The mark is laid ACROSS
            /// this, so a locked front wheel on full lock leaves a mark at the
            /// angle the tyre is pointing rather than the angle the car is.</summary>
            public Vector3 forward;
            /// <summary>m/s of rubber scrubbing over the surface. Zero while the
            /// tyre is inside its grip envelope.</summary>
            public float slide;
            /// <summary>Vertical load, N. A wheel light on its springs marks
            /// and smokes less than one the weight has transferred onto.</summary>
            public float load;
            /// <summary>Hit the road layer, rather than grass or dirt. Decides
            /// black rubber against pale dust.</summary>
            public bool onRoad;
        }

        /// <summary>Live contact state for the four wheels, in the usual order:
        /// FL, FR, RL, RR.</summary>
        public readonly WheelContact[] wheelContacts = new WheelContact[4];

        /// <summary>Front road-wheel angle in degrees, signed like the steering
        /// input. The cockpit's steering wheel turns from this rather than from
        /// the raw input, so it lags and self-centres exactly as the car does.
        /// </summary>
        public float SteerAngleDeg => steerAngleDeg;

        Vector3[] wheelLocalPos;
        readonly float[] suspensionCompression = new float[4];
        readonly float[] prevCompression = new float[4];
        readonly float[] wheelLoad = new float[4];
        readonly bool[] wheelGrounded = new bool[4];
        readonly float[] wheelGrip = new float[4];
        readonly float[] wheelRollAngle = new float[4];
        float steerAngleDeg;
        float shiftTimer;
        float gearJustChangedTimer;
        float reverseHold;
        float postDriftTimer;
        float ebrakeCooldown;
        bool prevHandbrake;
        bool prevBrake;
        float rearCircleTotal;
        float yawDamp = 0.6f;
        float staticWheelLoad;
        float impactGraceTimer, impactGraceDuration;
        int suspensionMask;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (Body == null) Body = gameObject.AddComponent<Rigidbody>();
            Body.mass = massKg;
            Body.linearDamping = 0f;
            // Angular damping applies to ALL THREE axes. Left at 0.6 it silently
            // damps body roll and pitch, and stacks with the yaw tier table so a
            // committed drift damps at 0.75/s instead of 0.15/s — dead on arrival.
            Body.angularDamping = 0.05f;
            Body.automaticCenterOfMass = false;
            Body.centerOfMass = new Vector3(0f, cgHeight, 0f);
            // At 80 m/s the car covers 1.6 m per 50 Hz tick and the barriers are
            // 0.35 m thick, so discrete detection can step straight through one.
            // Only raise the mode — the builder sets ContinuousDynamic on the
            // player (car-vs-car sweeps too) and the cheaper ContinuousSpeculative
            // on the AI, and Awake must not stomp that choice.
            if (Body.collisionDetectionMode == CollisionDetectionMode.Discrete)
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Everything except cars (layer 2) and solid scenery.
            suspensionMask = ~((1 << 2) | (1 << solidLayer));

            staticWheelLoad = massKg * 9.81f * 0.25f;
            // The built-in car never goes through ApplySpec, so derive its aero
            // here too — otherwise standalone editor play and a race launched
            // from the LifeSim would be two different cars.
            DeriveDownforce();

            RebuildGeometry();
            currentRPM = idleRPM;
        }

        /// <summary>
        /// Rebuild everything derived from the car's SHAPE: the four suspension
        /// mounts and the inertia tensor.
        ///
        /// Public because the body shell is no longer fixed. CarBody writes
        /// wheelbase, track and wheel radius when it swaps a catalog car's mesh
        /// in, and those three feed a table that is otherwise built once in
        /// Awake — a Charger on an RX-7's mount table steers from the wrong
        /// axle and rolls about the wrong centre.
        /// </summary>
        public void RebuildGeometry()
        {
            float halfTrack = trackWidth * 0.5f;
            float halfBase = wheelbase * 0.5f;
            wheelLocalPos = new[]
            {
                new Vector3(-halfTrack, mountHeight,  halfBase),
                new Vector3( halfTrack, mountHeight,  halfBase),
                new Vector3(-halfTrack, mountHeight, -halfBase),
                new Vector3( halfTrack, mountHeight, -halfBase),
            };
            // Null before Awake: the builder fits a shell at bake time, where
            // there is no Rigidbody cached yet and none is needed — Awake runs
            // this again the moment the scene loads.
            if (Body != null) ApplyInertiaTensor();
        }

        /// <summary>
        /// Unity derives the tensor from the box collider, landing on the
        /// textbook slab value. Real cars centralize mass, so yaw is scaled
        /// down; pitch and roll stay at slab. Body dimensions come from the
        /// collider itself rather than from literals — two independent sources
        /// of truth for the car's size is how they drift apart.
        /// </summary>
        void ApplyInertiaTensor()
        {
            var box = GetComponent<BoxCollider>();
            float lng = box != null ? box.size.z : 4.1f;
            float wid = box != null ? box.size.x : 1.72f;
            float hgt = box != null ? box.size.y : 1.0f;
            float slabYaw = massKg * (lng * lng + wid * wid) / 12f;
            float slabPitch = massKg * (lng * lng + hgt * hgt) / 12f;
            float slabRoll = massKg * (wid * wid + hgt * hgt) / 12f;
            Body.automaticInertiaTensor = false;
            Body.inertiaTensor = new Vector3(slabPitch, slabYaw * yawInertiaScale, slabRoll);
            Body.inertiaTensorRotation = Quaternion.identity;
        }

        public float GetTorqueAtRPM(float rpm) =>
            RawTorqueAtRPM(rpm) * (supercharged ? SuperchargerBoost(rpm) : 1f);

        /// <summary>Roots boost by RPM: flat to 60% of the rev range, then
        /// tapering as airflow falls off.</summary>
        float SuperchargerBoost(float rpm)
        {
            float frac = Mathf.Clamp01((rpm - idleRPM) / Mathf.Max(redlineRPM - idleRPM, 1f));
            float taper = Mathf.Max(0f, (frac - SuperchargerTaperStart) / (1f - SuperchargerTaperStart));
            return SuperchargerPeak - (SuperchargerPeak - SuperchargerTop) * taper;
        }

        float RawTorqueAtRPM(float rpm)
        {
            if (curveRPM == null || curveRPM.Length < 2)
            {
                float t = (rpm - DefaultCurveStartRPM) / DefaultCurveStepRPM;
                if (t <= 0f)
                    return DefaultTorqueNm[0] * Mathf.InverseLerp(0f, DefaultCurveStartRPM, rpm);
                int i = Mathf.FloorToInt(t);
                if (i >= DefaultTorqueNm.Length - 1) return DefaultTorqueNm[DefaultTorqueNm.Length - 1];
                return Mathf.Lerp(DefaultTorqueNm[i], DefaultTorqueNm[i + 1], t - i);
            }

            // GT4 curves are sampled at arbitrary RPM points, not a fixed step,
            // so walk them. They are short (a handful of points) and sorted
            // ascending, which makes a linear scan cheaper than a binary search.
            if (rpm <= curveRPM[0])
                return curveNm[0] * Mathf.InverseLerp(0f, curveRPM[0], rpm);
            for (int i = 0; i < curveRPM.Length - 1; i++)
            {
                if (rpm > curveRPM[i + 1]) continue;
                float f = Mathf.InverseLerp(curveRPM[i], curveRPM[i + 1], rpm);
                return Mathf.Lerp(curveNm[i], curveNm[i + 1], f);
            }
            return curveNm[curveNm.Length - 1];
        }

        /// <summary>
        /// Re-spec this car from the catalog. Called by RaceHandoffApplier for
        /// the player's owned car and by the builder for the AI field.
        ///
        /// Drag is DERIVED rather than configured: solving it from the car's
        /// spec'd top speed is what makes every catalog car actually reach the
        /// number on its spec sheet. The old hardcoded dragCoefficient produced
        /// a terminal velocity about 25% above the topSpeedMps field, which is
        /// why that field was only ever safe to use as a normalizer.
        /// </summary>
        public void ApplySpec(CarSpec spec) => ApplySpec(spec, default);

        /// <param name="tune">The parts bolted to this particular example.
        /// Folded in HERE rather than applied afterwards because half of what
        /// ApplySpec does is derived from mass and power — drag, downforce,
        /// inertia tensor, chassis rates — and re-deriving them from stock
        /// numbers and then overwriting mass leaves a lightened car with a
        /// heavy car's springs. Stock stages make this identical to before.</param>
        public void ApplySpec(CarSpec spec, CarTune.Stages tune)
        {
            if (spec == null) return;
            activeSpec = spec;
            activeTune = tune;
            spec.Decode();

            massKg = CarTune.WeightAtStage(spec.kg, spec.minKg, tune.weight);
            redlineRPM = spec.redline;
            revLimitRPM = spec.redline + 500f;
            idleRPM = spec.idleRPM;
            upshiftRPM = spec.redline * 0.96f;
            downshiftRPM = Mathf.Max(1200f, spec.idleRPM * 3.4f);

            if (spec.curveRPM != null && spec.curveRPM.Length >= 2)
            {
                curveRPM = spec.curveRPM;
                // A POWER stage scales the whole curve rather than adding a flat
                // figure: the shape is this engine's character and the stage is
                // buying more of the same engine. Never write into spec.curveNm —
                // CarSpec instances are shared out of the catalog, so scaling in
                // place would tune every other car of the same model, including
                // the opponents, and would compound each time a race loaded.
                powerScale = spec.hp > 0
                    ? CarTune.PowerAtStage(spec.hp, spec.builtHp, tune.power) / (float)spec.hp
                    : 1f;
                if (Mathf.Abs(powerScale - 1f) < 1e-4f) curveNm = spec.curveNm;
                else
                {
                    curveNm = new float[spec.curveNm.Length];
                    for (int i = 0; i < curveNm.Length; i++) curveNm[i] = spec.curveNm[i] * powerScale;
                }
            }

            var ratios = spec.BuildGearRatios(wheelRadius, finalDrive);
            if (ratios != null && ratios.Length > 0) gearRatios = ratios;

            frontDriveShare = spec.FrontDriveShare;
            topSpeedMps = spec.topSpeedMps > 1f ? spec.topSpeedMps : topSpeedMps;
            ApplyTuneHandling();
            DeriveDrag();
            DeriveDownforce();
            ScaleChassisToMass();

            if (Body != null)
            {
                Body.mass = massKg;
                ApplyInertiaTensor();
            }
        }

        /// <summary>The parts fitted to this car. Read by the HUD and by
        /// anything that wants to know why the numbers moved.</summary>
        public CarTune.Stages activeTune { get; private set; }

        /// <summary>
        /// Chassis-side effects of the tuning stages. Power and weight are folded
        /// into ApplySpec because everything downstream derives from them; these
        /// three are direct multipliers on knobs the physics already has.
        ///
        /// Baselines are captured on the first call so re-specing a car (which
        /// happens on every race load) multiplies against stock rather than
        /// against the last race's already-upgraded value.
        /// </summary>
        void ApplyTuneHandling()
        {
            if (!tuneBaselineCaptured)
            {
                stockBrakeDemandG = brakeDemandG;
                stockCorneringStiffness = corneringStiffness;
                stockGripBonus = gripBonus;
                tuneBaselineCaptured = true;
            }

            brakeDemandG = CarTune.BrakeDemandG(stockBrakeDemandG, activeTune);
            gripBonus = stockGripBonus * CarTune.GripStageMult(activeTune.tires);

            // SUSPENSION. RG2 models this as a turn-rate multiplier, which has no
            // direct analogue in a raycast-wheel car — the turn rate here is an
            // OUTPUT of the tyre model. Cornering stiffness is the input that
            // moves it the same way: a stiffer, better-located tyre builds
            // lateral force in fewer degrees of slip, which is what "sharper
            // turn-in" physically is. Hard-capped at 13, the ceiling the drift
            // tuning was established against — above it the rear steps out
            // before the front loads and the car darts.
            corneringStiffness = Mathf.Min(
                stockCorneringStiffness * CarTune.SuspStageMult(activeTune.suspension), 13f);
        }

        bool tuneBaselineCaptured;
        float stockBrakeDemandG, stockCorneringStiffness, stockGripBonus;
        /// <summary>How much the POWER stage scaled the torque curve by. Held so
        /// DeriveDrag can take it back out — see the note there.</summary>
        float powerScale = 1f;

        /// <summary>
        /// Re-scale every load-bearing suspension figure to the new mass.
        ///
        /// Springs, dampers, anti-roll rates and the force caps were all tuned
        /// against the RX-7's 1280 kg. Applying a spec without rescaling them
        /// leaves a 950 kg hatchback on springs 35% too stiff (it skates, because
        /// the wheels barely load) and a 1700 kg GT on springs too soft (it
        /// wallows and rolls onto its outside tire). Both read to the player as
        /// the car swinging around unpredictably rather than as a spring rate.
        ///
        /// Ratios are held to the reference car, so this is a scale, not a
        /// retune: the FD still gets exactly the numbers it was tuned with.
        /// </summary>
        void ScaleChassisToMass()
        {
            const float refMass = 1280f;
            float k = massKg / refMass;

            springRateFront = 47100f * k;
            springRateRear = 35300f * k;
            // Critical damping goes with sqrt(k*m), and k itself scales with
            // mass here, so the damper scales linearly too — keeping the damping
            // RATIO constant is the part that matters for how settled it feels.
            damperFront = 4000f * k;
            damperRear = 3400f * k;
            antiRollFront = 16000f * k;
            antiRollRear = 12000f * k;

            staticWheelLoad = massKg * 9.81f * 0.25f;
            // Half the static axle load, same relationship the reference used.
            antiRollMaxForce = staticWheelLoad * 2f;
        }

        /// <summary>
        /// Pick the drag coefficient that makes the car top out at its spec'd
        /// speed: at vmax the tractive effort in top gear exactly balances drag
        /// plus rolling resistance.
        /// </summary>
        void DeriveDrag()
        {
            if (topSpeedMps < 5f || gearRatios == null || gearRatios.Length == 0) return;
            float topRatio = gearRatios[gearRatios.Length - 1];
            float wheelRpm = topSpeedMps / (2f * Mathf.PI * wheelRadius) * 60f;
            float rpmAtVmax = Mathf.Min(wheelRpm * topRatio * finalDrive, revLimitRPM);
            // STOCK torque, deliberately: this solves for the drag figure that
            // makes the car reach the top speed ON ITS SPEC SHEET. Feeding it
            // tuned torque would solve for MORE drag and pin terminal velocity
            // at the stock number, so a full engine build would accelerate
            // harder and top out at exactly the same speed — which is not what
            // anyone buying a turbo expects, and would be invisible until
            // someone timed it. Dividing the power stage back out (and skipping
            // the blower entirely) keeps drag a property of the BODY, which is
            // what it is.
            float force = RawTorqueAtRPM(rpmAtVmax) / Mathf.Max(powerScale, 0.01f)
                          * topRatio * finalDrive * drivetrainEfficiency / wheelRadius;
            float net = force - rollingResistance;
            // A car geared so it cannot reach its own quoted top speed would
            // otherwise ask for negative drag. Keep a floor rather than let the
            // car accelerate forever.
            dragCoefficient = net > 1f
                ? net / (topSpeedMps * topSpeedMps)
                : 0.30f;
        }

        /// <summary>
        /// Solve the downforce coefficient from the weight fraction: the force
        /// law is k*v^2, so k = fraction * m * g / vmax^2.
        ///
        /// Downforce is applied to the BODY, not straight into the friction
        /// circle, so it reaches grip the honest way — it compresses the springs,
        /// which raises wheel load, which widens the circle. That also means it
        /// costs ride height, which is why the fraction stays well under 1.
        /// </summary>
        void DeriveDownforce()
        {
            float vmax = Mathf.Max(topSpeedMps, 10f);
            downforceCoefficient = downforceWeightFractionAtVmax * massKg * 9.81f / (vmax * vmax);
        }

        /// <summary>Lateral tire force. Ported 1:1 from tire.ts tireCurve().</summary>
        static float TireCurve(float slip, float C)
        {
            float sMag = Mathf.Abs(slip);
            if (sMag <= SlipPeak) return -C * slip;
            float peakF = C * SlipPeak;
            float t = Mathf.Min(1f, (sMag - SlipPeak) / (Mathf.PI / 2f - SlipPeak));
            return -Mathf.Sign(slip) * peakF * (1.0f - 0.65f * t);
        }

        /// <summary>Longitudinal capacity cut when sliding. From tire.ts.</summary>
        static float CombinedSlipFactor(float slipMag)
        {
            if (slipMag <= SlipPeak) return 1.0f;
            float t = Mathf.Min(1f, (slipMag - SlipPeak) / (Mathf.PI / 2f - SlipPeak));
            return 1.0f - 0.7f * t;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 vel = Body.linearVelocity;
            forwardSpeed = Vector3.Dot(vel, transform.forward);
            speedKmh = vel.magnitude * 3.6f;

            UpdateImpactGrace(dt);
            UpdateChassisSlip(vel);
            // Slip pre-pass, then the mode switch, then everything that reads it.
            //
            // UpdateDriftState used to run AFTER TireForces, which put the tick's
            // consumers of Drifting on two different sides of the switch: the
            // steering (34 deg gripping vs 45 deg sliding) and the gesture layer
            // read LAST tick's answer, while the yaw damper and the injector read
            // this one. So for one tick out of every mode change the car steered
            // like it was gripping while being damped like it was sliding — and
            // mode changes are exactly the moments the driver is paying most
            // attention to.
            //
            // The pre-pass measures slip from THIS tick's velocity using last
            // tick's contact geometry and steer angle. That is strictly fresher
            // than what the steering saw before (velocity is what actually
            // changed; the steer angle moves at most 4.4 deg per tick), and now
            // there is exactly one value of Drifting per tick.
            RefreshSlipAngles();
            UpdateDriftState(vel);
            UpdateDriftGestures(dt);      // runs early, so the frame sees the kick
            UpdateSteering(dt);
            UpdateGearbox(dt);
            SuspensionAndLoads(dt);
            TireForces(dt);               // recomputes slip for the force integration
            ApplyLateralStabilizer();
            ApplyYawLayer(dt);
            AeroForces();
            UpdateWheelVisuals(dt);
        }

        /// <summary>
        /// Per-axle slip angles for the current velocity, using the wheel contact
        /// points and steer angle left over from the previous tick. Same maths as
        /// the loop in <see cref="TireForces"/>, which recomputes them against
        /// this tick's suspension before integrating forces — this pass exists
        /// only so the drift state machine has something to read before the
        /// steering asks it a question.
        ///
        /// Silent no-op with no wheel on the ground: the previous answer is a
        /// better guess than zero, and zeroing would drop the car out of drift
        /// state every time it went over a crest.
        /// </summary>
        void RefreshSlipAngles()
        {
            if (wheelLocalPos == null) return;
            float frontSum = 0f, rearSum = 0f;
            int frontCount = 0, rearCount = 0;

            for (int i = 0; i < 4; i++)
            {
                if (!wheelGrounded[i]) continue;
                bool front = i < 2;
                Vector3 mount = transform.TransformPoint(wheelLocalPos[i]);
                Vector3 contact = mount - transform.up *
                                  (restLength + wheelRadius - suspensionCompression[i]);

                Quaternion steerRot = front
                    ? Quaternion.AngleAxis(steerAngleDeg, transform.up)
                    : Quaternion.identity;
                Vector3 contactVel = Body.GetPointVelocity(contact);
                float vLong = Vector3.Dot(contactVel, steerRot * transform.forward);
                float vLat = Vector3.Dot(contactVel, steerRot * transform.right);
                float slip = Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), slipEpsilon));

                if (front) { frontSum += slip; frontCount++; }
                else { rearSum += slip; rearCount++; }
            }

            if (frontCount > 0) frontSlipAngle = frontSum / frontCount;
            if (rearCount > 0) rearSlipAngle = rearSum / rearCount;
        }

        /// <summary>
        /// Called by <see cref="CollisionResponder"/> on impact.
        /// <paramref name="severity01"/> 0..1 scales both how deep the stabilizer
        /// cut goes and how long it lasts, so a kerb tap barely registers while a
        /// barrier hit genuinely takes the car away from the driver for a moment.
        /// Impacts extend an existing window rather than restarting it, so a
        /// scraping series of contacts cannot hold the car unassisted forever.
        /// </summary>
        public void RegisterImpact(float severity01)
        {
            severity01 = Mathf.Clamp01(severity01);
            // Light contact must NOT stand the stabilizers down. Scraping a
            // barrier fires OnCollisionEnter over and over as contacts break and
            // reform, and at a low threshold each one re-armed the grace window —
            // holding the car unassisted for the whole length of the wall. That
            // is the same self-feeding shape as the drift-latch bug: the state
            // that makes the car loose is refreshed by the consequences of being
            // loose. Only a real hit (~1.4 m/s into the surface) counts.
            if (severity01 <= 0.15f) return;
            float window = impactGraceWindow * Mathf.Lerp(0.35f, 1f, severity01);
            if (window <= impactGraceTimer) return;
            impactGraceTimer = window;
            impactGraceDuration = window;
        }

        void UpdateImpactGrace(float dt)
        {
            if (impactGraceTimer <= 0f) { ImpactGrace01 = 0f; return; }
            impactGraceTimer = Mathf.Max(0f, impactGraceTimer - dt);
            ImpactGrace01 = impactGraceDuration > 0f ? impactGraceTimer / impactGraceDuration : 0f;
        }

        void UpdateChassisSlip(Vector3 vel)
        {
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            if (flat.sqrMagnitude < 1f) { chassisSlipAngle = 0f; return; }
            Vector3 fwd = transform.forward; fwd.y = 0f;
            chassisSlipAngle = Vector3.SignedAngle(flat, fwd, Vector3.up) * Mathf.Deg2Rad;
        }

        void UpdateDriftGestures(float dt)
        {
            EbrakeTimer = Mathf.Max(0f, EbrakeTimer - dt);
            postDriftTimer = Mathf.Max(0f, postDriftTimer - dt);
            ebrakeCooldown = Mathf.Max(0f, ebrakeCooldown - dt);

            float speed = Mathf.Abs(forwardSpeed);
            float steerMag = Mathf.Abs(steerInput);
            float massDamp = Mathf.Sqrt(1200f / Mathf.Max(800f, massKg));
            float speedRatio = Mathf.Min(1f, speed / topSpeedMps);
            float surfBoost = onRoad ? 1.0f : 1.3f;

            // --- gesture 1: handbrake press edge. The mu collapse alone slides
            // the car but has no punch; this is the punch. The steer gate is not
            // optional — without it ambient yaw noise spins a straight-line pull.
            bool edge = handbrakeInput && !prevHandbrake;
            if (edge && anyWheelGrounded && speed > DriveGateSpeed &&
                ebrakeCooldown <= 0f && steerMag > 0.15f)
            {
                float inputScale = steerMag * (0.3f + speedRatio * 0.7f);
                // VelocityChange ignores the inertia tensor, which is why massDamp stays.
                float dOmega = Mathf.Sign(steerInput) * EbrakeKickBase * 1.1f * massDamp *
                               surfBoost * inputScale;
                Body.AddTorque(transform.up * dOmega, ForceMode.VelocityChange);
                Body.linearVelocity *= 1f - 0.025f * 1.1f * inputScale;
                ebrakeCooldown = EbrakeKickCooldown;
            }
            if (handbrakeInput && speed > DriveGateSpeed) EbrakeTimer = EbrakeWindow;
            prevHandbrake = handbrakeInput;

            // --- gesture 2: brake stab. Gentler and shorter-windowed than the
            // handbrake so it rotates rather than spins.
            bool brakeEdge = brakeInput > 0.5f && !prevBrake;
            if (brakeStabDrift > 0f && brakeEdge && anyWheelGrounded &&
                forwardSpeed > topSpeedMps * 0.15f &&
                ebrakeCooldown <= 0f && EbrakeTimer <= 0f && steerMag > 0.2f)
            {
                float inputScale = steerMag * (0.4f + speedRatio * 0.6f);
                float dOmega = Mathf.Sign(steerInput) * BrakeStabBase * brakeStabDrift *
                               1.1f * massDamp * inputScale;
                Body.AddTorque(transform.up * dOmega, ForceMode.VelocityChange);
                EbrakeTimer = BrakeStabWindow;
                ebrakeCooldown = BrakeStabCooldown;
            }
            prevBrake = brakeInput > 0.5f;

            // --- sustain: throttle holds the drift after the handbrake is out.
            // Bounded, so a held handbrake (0.75) always dominates. Two caps are
            // mandatory. The upper one stops a donut that never ends: collapsed
            // rear mu plus low yaw damping is otherwise a stable limit cycle. The
            // LOWER one stops the latch: without it, being in the drift state
            // refreshes the timer, the live timer blocks the drift exit, and the
            // car stays permanently loose for as long as the throttle is held.
            float slipNow = Mathf.Max(Mathf.Abs(frontSlipAngle), Mathf.Abs(rearSlipAngle));
            if (!handbrakeInput && Drifting && throttleInput > 0.3f && speed > DriveGateSpeed &&
                slipNow > DriftExitSlip &&
                Mathf.Abs(chassisSlipAngle) < MaxBodySlipForSustain &&
                EbrakeTimer < ThrottleSustainWindow)
                EbrakeTimer = ThrottleSustainWindow;
        }

        void UpdateSteering(float dt)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float gripSteer = Mathf.Lerp(maxSteerLowSpeedDeg, maxSteerHighSpeedDeg,
                                         Mathf.Clamp01(speed / steerSpeedFalloff));
            // Blend the extra lock in with actual body slip rather than snapping
            // to it the instant the drift flag sets. A brake-stab entry would
            // otherwise hand the player 60 degrees of lock mid-corner.
            float slideT = Mathf.Clamp01((Mathf.Abs(chassisSlipAngle) - 0.15f) / 0.45f);
            float maxSteer = Mathf.Lerp(gripSteer, maxSteerDriftDeg, Drifting ? slideT : 0f);
            float rate = Mathf.Lerp(steerRateDeg, steerRateDriftDeg, Drifting ? slideT : 0f);
            // A pulling fault (bad alignment) biases the wheels, so holding a
            // straight line costs the player constant correction. Added to the
            // TARGET, not to steerInput, so it survives a released stick.
            float steerTarget = Mathf.Clamp(steerInput + faultSteerPull, -1f, 1f);
            steerAngleDeg = Mathf.MoveTowards(steerAngleDeg, steerTarget * maxSteer, rate * dt);
        }

        void UpdateGearbox(float dt)
        {
            if (shiftTimer > 0f) shiftTimer -= dt;
            if (gearJustChangedTimer > 0f) gearJustChangedTimer -= dt;

            float speed = Mathf.Abs(forwardSpeed);

            if (currentGear == -1)
            {
                if (throttleInput > 0.3f && forwardSpeed > -0.5f) { currentGear = 1; reverseHold = 0f; }
            }
            else if (allowReverse && brakeInput > 0.3f && throttleInput < 0.05f &&
                     speed < 0.6f && shiftTimer <= 0f)
            {
                // Require a deliberate hold. A car merely being held stationary on
                // the brake — the whole grid during the countdown — must not
                // silently select reverse and then drive off backwards.
                reverseHold += dt;
                if (reverseHold > 0.4f) { currentGear = -1; reverseHold = 0f; }
            }
            else reverseHold = 0f;

            float ratio = GearRatio();
            float wheelRPM = speed / (2f * Mathf.PI * wheelRadius) * 60f;
            float kinematicRPM = wheelRPM * Mathf.Abs(ratio) * finalDrive;

            float accelPedal = currentGear == -1 ? brakeInput : throttleInput;
            float launchRPM = Mathf.Lerp(idleRPM, 5200f, accelPedal);
            float clutchLock = Mathf.Clamp01(speed / clutchEngageSpeed);
            kinematicRPM = Mathf.Lerp(Mathf.Max(kinematicRPM, launchRPM), kinematicRPM, clutchLock);

            float target = Mathf.Lerp(kinematicRPM, revLimitRPM * 0.97f, Mathf.Clamp01(wheelSpin) * 0.6f);
            target = Mathf.Clamp(target, idleRPM, revLimitRPM);
            currentRPM = Mathf.MoveTowards(currentRPM, target, 12000f * dt);

            if (currentGear >= 1 && !manualMode && shiftTimer <= 0f && gearJustChangedTimer <= 0f)
            {
                if (currentRPM > upshiftRPM && currentGear < gearRatios.Length && wheelSpin < 0.5f)
                    ShiftTo(currentGear + 1);
                else if (currentRPM < downshiftRPM && currentGear > 1)
                    ShiftTo(currentGear - 1);
            }
        }

        float GearRatio() => currentGear == -1
            ? -reverseRatio
            : gearRatios[Mathf.Clamp(currentGear, 1, gearRatios.Length) - 1];

        /// <summary>Raised on an upshift, with the RPM fraction at the moment of
        /// the change. Audio uses it for the turbo flutter between gears.</summary>
        public event System.Action<float> Upshifted;

        public void ShiftTo(int gear)
        {
            gear = Mathf.Clamp(gear, 1, gearRatios.Length);
            if (gear == currentGear) return;
            bool up = gear > currentGear;
            currentGear = gear;
            shiftTimer = shiftTime * faultShiftMult;
            gearJustChangedTimer = 0.6f;
            if (up && Upshifted != null)
                Upshifted(Mathf.Clamp01((currentRPM - idleRPM) /
                                        Mathf.Max(revLimitRPM - idleRPM, 1f)));
        }

        void SuspensionAndLoads(float dt)
        {
            anyWheelGrounded = false;
            int roadHits = 0, hits = 0;
            float rayLength = restLength + wheelRadius;

            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                prevCompression[i] = suspensionCompression[i];
                Vector3 mount = transform.TransformPoint(wheelLocalPos[i]);
                wheelGrounded[i] = Physics.Raycast(mount, -transform.up, out RaycastHit hit,
                                                   rayLength, suspensionMask,
                                                   QueryTriggerInteraction.Ignore);

                // Contact geometry for the visuals, taken from the ray that was
                // cast anyway. TireForces fills in the sliding speed a few lines
                // later; everything geometric is known here and here only —
                // hit.normal in particular, which nothing downstream can
                // reconstruct without casting the same ray a second time.
                wheelContacts[i].grounded = wheelGrounded[i];
                if (!wheelGrounded[i]) { wheelContacts[i].slide = 0f; wheelContacts[i].load = 0f; }

                if (wheelGrounded[i])
                {
                    anyWheelGrounded = true;
                    hits++;
                    float compression = rayLength - hit.distance;
                    suspensionCompression[i] = compression;
                    // Clamp damper velocity: landing after air time otherwise steps
                    // compression from 0 to full in one tick and fires the car away.
                    float compressionVel = Mathf.Clamp((compression - prevCompression[i]) / dt, -4f, 4f);
                    float k = front ? springRateFront : springRateRear;
                    float c = front ? damperFront : damperRear;
                    float force = Mathf.Max(0f, k * compression + c * compressionVel);
                    force = Mathf.Min(force, staticWheelLoad * maxSuspensionForceRatio);
                    wheelLoad[i] = force;

                    bool isRoad = hit.collider != null && hit.collider.gameObject.layer == roadLayer;
                    if (isRoad) roadHits++;
                    wheelGrip[i] = isRoad ? roadGrip : offroadGrip;

                    wheelContacts[i].point = hit.point;
                    wheelContacts[i].normal = hit.normal;
                    wheelContacts[i].load = force;
                    wheelContacts[i].onRoad = isRoad;

                    Body.AddForceAtPosition(transform.up * force, mount);
                }
                else
                {
                    suspensionCompression[i] = 0f;
                    wheelLoad[i] = 0f;
                    wheelGrip[i] = roadGrip;
                }
            }
            onRoad = hits == 0 || roadHits * 2 >= hits;

            for (int axle = 0; axle < 2; axle++)
            {
                int l = axle * 2, r = axle * 2 + 1;
                if (!wheelGrounded[l] || !wheelGrounded[r]) continue;
                float rate = axle == 0 ? antiRollFront : antiRollRear;
                float arb = Mathf.Clamp((suspensionCompression[l] - suspensionCompression[r]) * rate,
                                        -antiRollMaxForce, antiRollMaxForce);
                // Push the MORE COMPRESSED side up and the extended side down.
                // `compression` grows as the wheel is pushed in, which is the
                // opposite sign to the "suspension travel" the usual formulation
                // uses — getting this backwards makes the bar amplify roll
                // instead of resisting it, and the car wallows like it has blown
                // dampers.
                Body.AddForceAtPosition(transform.up * arb, transform.TransformPoint(wheelLocalPos[l]));
                Body.AddForceAtPosition(-transform.up * arb, transform.TransformPoint(wheelLocalPos[r]));
            }
        }

        void TireForces(float dt)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float accelPedal = currentGear == -1 ? brakeInput : throttleInput;
            float brakePedal = currentGear == -1 ? throttleInput : brakeInput;
            float ratio = GearRatio();

            // Tractive effort and engine drag are tracked separately. They can
            // both be non-zero at part throttle, and only the tractive part may
            // count toward wheelspin.
            float tractionForce = 0f;
            RevLimiterActive = false;
            if (accelPedal > 0.01f)
            {
                float torque = GetTorqueAtRPM(currentRPM) * accelPedal;
                if (shiftTimer > 0f) torque *= 0.15f;
                RevLimiterActive = currentRPM >= revLimitRPM - 50f;
                if (RevLimiterActive) torque *= 0.05f;                  // hard ECU cut
                tractionForce = torque * ratio * finalDrive * drivetrainEfficiency /
                                wheelRadius * faultAccelMult;
            }
            // Engine braking, rear axle only and gear-scaled, so downshifting
            // into a corner actually does something and lift-off rotates the car.
            float driveForce = tractionForce;
            if (accelPedal < 0.99f && currentGear >= 1 && speed > 0.5f)
            {
                float rpmNorm = Mathf.Clamp01((currentRPM - idleRPM) / Mathf.Max(redlineRPM - idleRPM, 1f));
                float tBrake = (engineBrakeBaseNm + engineBrakeRpmNm * rpmNorm) * (1f - accelPedal);
                driveForce -= tBrake * ratio * finalDrive * drivetrainEfficiency /
                              wheelRadius * Mathf.Sign(forwardSpeed);
            }

            float brakeForceTotal = brakePedal * brakeDemandG * massKg * 9.81f * faultBrakeMult;

            // Rear-mu collapse: the handbrake shrinks the rear friction circle so
            // the integrator saturates the rear first and yaw develops from the
            // tire model, not from a scripted "drift mode". Collapse mu, NOT
            // cornering stiffness — backwards makes the rear feel numb, not loose.
            float rearMuMult = EbrakeTimer > 0f
                ? 1f - EbrakeMuCollapse * Mathf.Min(1f, EbrakeTimer / EbrakeWindow)
                : 1f;

            // Only tractive effort counts toward wheelspin. Engine drag would
            // otherwise report wheelspin on a lift, firing the power-oversteer
            // yaw injector every time the player came off the throttle.
            float totalDriveDemand = Mathf.Abs(tractionForce);
            float frontCircleTotal = 0f;
            rearCircleTotal = 0f;
            float frontSlipSum = 0f, rearSlipSum = 0f;
            int frontCount = 0, rearCount = 0;

            // Count grounded wheels per axle BEFORE distributing force. The old
            // code split drive and brake force by a flat 0.5 per wheel, so with
            // one wheel of an axle in the air half that axle's force silently
            // vanished instead of moving to the wheel that could still use it.
            int groundedFront = 0, groundedRear = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!wheelGrounded[i]) continue;
                if (i < 2) groundedFront++; else groundedRear++;
            }
            float frontShare = frontDriveShare;
            float rearShare = 1f - frontDriveShare;

            for (int i = 0; i < 4; i++)
            {
                if (!wheelGrounded[i]) continue;
                bool front = i < 2;
                Vector3 mount = transform.TransformPoint(wheelLocalPos[i]);
                Vector3 contact = mount - transform.up * (restLength + wheelRadius - suspensionCompression[i]);

                Quaternion steerRot = front ? Quaternion.AngleAxis(steerAngleDeg, transform.up) : Quaternion.identity;
                Vector3 wheelForward = steerRot * transform.forward;
                Vector3 wheelRight = steerRot * transform.right;

                Vector3 contactVel = Body.GetPointVelocity(contact);
                float vLong = Vector3.Dot(contactVel, wheelForward);
                float vLat = Vector3.Dot(contactVel, wheelRight);
                float slip = Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), slipEpsilon));

                if (front) { frontSlipSum += slip; frontCount++; }
                else { rearSlipSum += slip; rearCount++; }

                float Fz = wheelLoad[i];
                float mu = wheelGrip[i] * gripBonus * faultGripMult *
                           (front ? tireMuFront : tireMuRear);
                if (!front) mu *= rearMuMult;
                float circle = mu * Fz;
                // FULL circle, not the combined-slip reduced cap: using the
                // reduced one inflates the wheelspin ratio during a slide and
                // the yaw injector runs away.
                if (front) frontCircleTotal += circle; else rearCircleTotal += circle;

                float fLong = 0f;
                float longCap = circle * CombinedSlipFactor(Mathf.Abs(slip));
                // What the wheel was ASKED for along its own axis, before the
                // friction circle took its cut, kept as two numbers because the
                // two ways of exceeding it look nothing alike. The DELIVERED
                // force cannot tell them apart — a locked wheel, a spinning one
                // and a perfectly gripping one all report exactly longCap — so
                // the demand is the only place the difference survives, and it
                // is what the marks and the smoke are made of.
                float driveDemand = 0f, brakeDemand = 0f;

                // Drive torque goes to the driven axle(s), divided among the
                // wheels of that axle still touching the road.
                float axleShare = front ? frontShare : rearShare;
                int axleWheels = front ? groundedFront : groundedRear;
                if (axleShare > 0f && axleWheels > 0)
                {
                    driveDemand = driveForce * axleShare / axleWheels;
                    fLong = Mathf.Clamp(driveDemand, -longCap, longCap);
                }

                // Below the solver-jitter floor the brakes still hold the car:
                // without this the car creeps off on any gradient with the
                // pedal buried, which reads as a broken handbrake.
                if (brakeForceTotal > 0f && speed <= 0.3f)
                {
                    // Scale with velocity instead of taking its sign: at a
                    // near-standstill the sign flips on solver noise, and a
                    // bang-bang force that flips with it buzzes the car.
                    float hold = Mathf.Clamp(vLong * 4f, -1f, 1f);
                    fLong -= hold * Mathf.Min(brakeForceTotal * 0.25f, longCap);
                }
                if (brakeForceTotal > 0f && speed > 0.3f)
                {
                    float share = front ? brakeFrontShare : (1f - brakeFrontShare);
                    int brakeWheels = Mathf.Max(1, front ? groundedFront : groundedRear);
                    brakeDemand = brakeForceTotal * share / brakeWheels;
                    fLong -= Mathf.Sign(vLong) * Mathf.Min(brakeDemand, longCap);
                }
                if (!front && handbrakeInput && speed > 0.3f)
                {
                    fLong = -Mathf.Sign(vLong) * Mathf.Min(circle * 0.9f, Mathf.Abs(fLong) + circle * 0.6f);
                    // A pulled handbrake is a locked rear wheel by definition,
                    // whatever the arithmetic above worked out — the pads are
                    // not modulating anything. Say so, or a flick of the lever
                    // marks the road only in proportion to how hard the car
                    // happened to be braking already.
                    brakeDemand = circle * (1f + 1f / LockSharpness);
                }

                // Contact-patch scrub, for the marks and the smoke.
                //
                // Laterally the free part is the slip a tyre needs to make grip
                // AT ALL — tan(SlipPeak) of the rolling speed — and only the
                // excess is rubber moving over tarmac. Without that subtraction
                // every corner taken at walking pace paints a black line.
                //
                // Longitudinally a wheel asked for more than its circle can
                // give has stopped matching road speed, and the two ways that
                // happens are NOT symmetric. A locked wheel slides at whatever
                // speed the car is doing, so at a crawl it barely scrubs. A
                // SPINNING one is the opposite: the less the car is moving the
                // faster the tyre is turning relative to the road, and a
                // standing burnout — road speed zero — is the single smokiest
                // thing a car can do. Taking |vLong| for both would make it the
                // quietest.
                float driveOver = longCap > 1f
                    ? Mathf.Clamp01((Mathf.Abs(driveDemand) / longCap - 1f) * LockSharpness) : 0f;
                float brakeOver = longCap > 1f
                    ? Mathf.Clamp01((brakeDemand / longCap - 1f) * LockSharpness) : 0f;
                float rollSpeed = Mathf.Abs(vLong);
                float longSlide = Mathf.Max(driveOver * Mathf.Max(rollSpeed, SpinScrubSpeed),
                                            brakeOver * rollSpeed);
                float latSlide = Mathf.Max(0f, Mathf.Abs(vLat) - rollSpeed * SlipPeakTan);
                wheelContacts[i].slide = Mathf.Sqrt(latSlide * latSlide + longSlide * longSlide);
                wheelContacts[i].forward = wheelForward;

                float C = corneringStiffness * Fz;
                float fLat = TireCurve(slip, C);
                float latCap = Mathf.Sqrt(Mathf.Max(circle * circle - fLong * fLong, 0f));
                fLat = Mathf.Clamp(fLat, -latCap, latCap);
                // Only fade lateral force right at a standstill, to stop solver
                // jitter. Fading it out to 2 m/s made the tires let go at parking
                // speeds, which is most of why the car felt unbound from the road.
                fLat *= Mathf.Clamp01(contactVel.magnitude / 0.6f);

                Body.AddForceAtPosition(wheelForward * fLong + wheelRight * fLat, contact);
            }

            frontSlipAngle = frontCount > 0 ? frontSlipSum / frontCount : 0f;
            rearSlipAngle = rearCount > 0 ? rearSlipSum / rearCount : 0f;

            // Wheelspin is per-AXLE: a 4WD splitting torque two ways can be
            // within grip at the front and over it at the rear, and it is the
            // worse axle that is actually spinning.
            wheelspinRatio = Mathf.Max(
                AxleSpin(totalDriveDemand * frontShare, frontCircleTotal),
                AxleSpin(totalDriveDemand * rearShare, rearCircleTotal));
            // WELDED DIFF. There is no left/right differential in this model —
            // drive splits front/rear and the tyre model handles the rest — so
            // there is nothing to lock. What a welded diff DOES, and the reason
            // people weld them, is that the driven wheels break away together
            // instead of the open diff dumping torque into whichever one gave up
            // first. Scaling the spin ratio is the honest single-knob version of
            // that: it is the input to the yaw injector, so the car lights up its
            // rear earlier and holds the slide, which is the mod's whole point.
            // Deliberately modest — this multiplies a term the drift feel was
            // tuned around, and a welded diff should change the car's manners,
            // not re-tune it.
            if (weldedDiff) wheelspinRatio *= WeldedSpinGain;
            wheelSpin = Mathf.MoveTowards(wheelSpin, Mathf.Clamp01(wheelspinRatio), dt * 4f);
        }

        static float AxleSpin(float demand, float cap) =>
            cap > 1f ? Mathf.Min(2f, Mathf.Max(0f, Mathf.Abs(demand) - cap) / cap) : 0f;

        /// <summary>
        /// Hysteresis state machine over per-axle slip. Not cosmetic: it is a
        /// mode switch for steering authority, yaw damping, and the wheelspin
        /// multiplier. The 6-to-18-degree band is what stops mode chatter.
        /// </summary>
        void UpdateDriftState(Vector3 vel)
        {
            float slipMax = Mathf.Max(Mathf.Abs(frontSlipAngle), Mathf.Abs(rearSlipAngle));
            bool ebrakeActive = EbrakeTimer > 0f;

            if (Mathf.Abs(forwardSpeed) < DriftStopSpeed && vel.magnitude < DriftStopSpeed)
            {
                Drifting = false;      // deliberately does not clear postDriftTimer
            }
            else if (Drifting)
            {
                if (slipMax < DriftExitSlip && !ebrakeActive)
                {
                    Drifting = false;
                    postDriftTimer = PostDriftLockout;
                }
            }
            else
            {
                if (ebrakeActive) Drifting = true;
                else if (slipMax > DriftEnterSlip && postDriftTimer <= 0f) Drifting = true;
            }
        }

        /// <summary>
        /// Deletes sideways velocity at the CG. This is the arcade stabilizer
        /// that separates an NFS-style car from a simulator: the tire model alone
        /// lets the whole car drift laterally whenever grip is exceeded, which
        /// reads as the wheels not being attached to the road. Damping the
        /// lateral component directly makes the car track where it is pointed,
        /// and backing the gain off while drifting keeps deliberate slides alive.
        /// </summary>
        void ApplyLateralStabilizer()
        {
            if (!anyWheelGrounded) return;
            float vLat = Vector3.Dot(Body.linearVelocity, transform.right);
            if (Mathf.Abs(vLat) < LateralDampDeadzone) return;

            float k = Drifting ? lateralDampDrift : lateralDampGrip;
            // Stand down after an impact, or the hit is deleted before the player
            // can see it — the damper would pull the car straight within ~3 ticks.
            k *= 1f - impactStabilizerCut * ImpactGrace01;
            // Fade in with speed so low-speed manoeuvring is not rail-roaded.
            float speedFade = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / LateralDampFadeSpeed);
            float cap = lateralDampMaxG * 9.81f;
            float accel = Mathf.Clamp(-vLat * k * speedFade, -cap, cap);
            Body.AddForce(transform.right * (accel * massKg), ForceMode.Force);
        }

        void ApplyYawLayer(float dt)
        {
            float steerMag = Mathf.Abs(steerInput);
            float yawRate = Vector3.Dot(Body.angularVelocity, transform.up);

            // --- wheelspin yaw boost: the injector that makes throttle rotate
            // the car. Models a spinning rear tire producing rotation the linear
            // slip-angle model cannot capture.
            float steerGate = EbrakeTimer > 0f ? InjectorSteerGateEbrake : InjectorSteerGate;
            // Fade the injector in with speed. First gear makes ~12.7 kN against
            // ~6.8 kN of rear grip, so wheelspinRatio is near 1 the moment you
            // touch the throttle from rest — ungated, that alone rotates a
            // stationary car, which reads as pivoting about the rear axle.
            float injectorFade = Mathf.Clamp01(
                (Mathf.Abs(forwardSpeed) - yawInjectorMinSpeed) /
                Mathf.Max(yawInjectorFullSpeed - yawInjectorMinSpeed, 0.01f));
            // Power oversteer needs a driven REAR axle. A front-driver that
            // lights up its tires understeers instead, so the injector is gated
            // off entirely for FWD and scaled down for 4WD.
            float layoutGain = 1f - frontDriveShare;
            if (wheelspinRatio > 0f && injectorFade > 0f && steerMag > steerGate &&
                postDriftTimer <= 0f && anyWheelGrounded && layoutGain > 0.05f)
            {
                float mult = EbrakeTimer > 0f ? InjectorEbrake
                           : (Drifting ? InjectorDrift : InjectorGrip);
                float surfMult = onRoad ? 1.0f : InjectorOffroadMult;
                float arm = wheelbase * weightDistFront;
                float torque = Mathf.Sign(steerInput) * steerMag * wheelspinRatio *
                               arm * rearCircleTotal * InjectorCircleShare * mult * surfMult *
                               wheelspinYawGain * injectorFade * layoutGain;
                Body.AddTorque(transform.up * torque, ForceMode.Force);
            }

            // --- four-tier yaw damping (table and rationale at the const block)
            bool steerNeutral = steerMag < YawSteerNeutral;
            bool driverIdle = steerMag < YawDriverIdleInput &&
                              throttleInput < YawDriverIdleInput && !handbrakeInput;
            float slipT = Mathf.Clamp01(
                (Mathf.Abs(chassisSlipAngle) - YawSlipRampStart) / YawSlipRampWidth);
            bool counterSteering = Drifting && steerMag > YawCounterSteerInput &&
                                   Mathf.Abs(yawRate) > YawCounterSteerRate &&
                                   Mathf.Sign(steerInput) != Mathf.Sign(yawRate);

            if (Drifting)
            {
                if (driverIdle) yawDamp = YawDampIdle;
                else if (steerNeutral) yawDamp = Mathf.Lerp(YawDampNeutralMin, YawDampNeutralMax, slipT);
                else if (counterSteering) yawDamp = YawDampCounter;
                else yawDamp = YawDampCommitted;   // committed slide still feels loose
            }
            else yawDamp = YawDampGrip;

            // Damp only the yaw component, in the body frame — leave roll/pitch alone.
            Vector3 w = Body.angularVelocity;
            float newYaw = yawRate * Mathf.Max(0f, 1f - yawDamp * dt);
            Body.angularVelocity = w + transform.up * (newYaw - yawRate);

            // --- counter-steer assist: never catches a deliberate slide, and
            // backs off as the player steers, so held-opposite-lock is untouched.
            if (countersteerAssist > 0f && EbrakeTimer <= 0f && !handbrakeInput &&
                forwardSpeed > CountersteerMinSpeed)
            {
                float excess = Mathf.Abs(chassisSlipAngle) - CountersteerDeadzone;
                float steerRelease = 1f - Mathf.Min(1f, steerMag / 0.55f);
                if (excess > 0f && steerRelease > 0f)
                {
                    float accel = Mathf.Min(excess * 15f * countersteerAssist * steerRelease,
                                            CountersteerMaxAccel);
                    // Fully off at the moment of impact: this assist reads the
                    // hit as a slide to be caught, and catching it is precisely
                    // what makes barriers feel like they are not there.
                    accel *= 1f - ImpactGrace01;
                    Body.AddTorque(transform.up * (-Mathf.Sign(chassisSlipAngle) * accel),
                                   ForceMode.Acceleration);
                }
            }
        }

        void AeroForces()
        {
            Vector3 vel = Body.linearVelocity;
            float v2 = vel.sqrMagnitude;
            if (v2 < 0.01f) return;
            Body.AddForce(-vel.normalized * (dragCoefficient * v2));
            if (anyWheelGrounded)
            {
                Body.AddForce(-transform.up * (downforceCoefficient * v2));
                Body.AddForce(-vel.normalized * rollingResistance * Mathf.Clamp01(vel.magnitude));
            }
        }

        void UpdateWheelVisuals(float dt)
        {
            float rollDelta = forwardSpeed / wheelRadius * Mathf.Rad2Deg * dt;
            float spinExtra = wheelSpin * 720f * dt;

            for (int i = 0; i < 4; i++)
            {
                if (wheelHubs[i] == null) continue;
                bool front = i < 2;

                Vector3 local = wheelLocalPos[i];
                local.y = wheelGrounded[i]
                    ? mountHeight - restLength + suspensionCompression[i]
                    : mountHeight - restLength;
                wheelHubs[i].localPosition = local;
                wheelHubs[i].localRotation = front
                    ? Quaternion.Euler(0f, steerAngleDeg, 0f)
                    : Quaternion.identity;

                if (wheelMeshes[i] != null)
                {
                    wheelRollAngle[i] += rollDelta + (front ? 0f : spinExtra);
                    float sign = (i % 2 == 0) ? -1f : 1f;
                    wheelMeshes[i].localRotation = Quaternion.Euler(wheelRollAngle[i] * sign, 0f, 0f);
                }
            }
        }

        /// <summary>How far above the point it is given <see cref="ResetTo"/>
        /// actually puts the car. Named because the recovery code has to run its
        /// clearance test at the height the car will END UP at, and two copies
        /// of this number would drift.</summary>
        public const float ResetLift = 0.4f;

        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position + Vector3.up * ResetLift, rotation);
            currentGear = 1;
            currentRPM = idleRPM;
            wheelSpin = 0f;
            wheelspinRatio = 0f;
            EbrakeTimer = 0f;
            Drifting = false;
            postDriftTimer = 0f;
            impactGraceTimer = 0f;
            ImpactGrace01 = 0f;
        }
    }
}
