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
        /// fights the tire model — that is the point.
        ///
        /// 4.5 was too much of a good thing. Paired with the 0.7 g cap it bound
        /// at 1.53 m/s of lateral velocity — 2.9 deg of body slip at 30 m/s —
        /// and above that supplied a flat 0.7 g against a total tire budget of
        /// 1.275 g, so 55% of the car's whole lateral capacity came from
        /// something that was not a tire. That is what "arcade-like" means when
        /// a player says it: past about 7 deg of lock the car is already being
        /// dragged onto its nose vector and more steering does nothing. 3.6 with
        /// a 0.45 g cap binds at 2.34 deg and is 35% of the budget — still
        /// planted under 2.3 deg, which is where the Underground/MW05 feel
        /// actually lives, but a real slide is no longer half-deleted before it
        /// begins.</summary>
        public float lateralDampGrip = 3.6f;
        public float lateralDampDrift = 0.6f;
        /// <summary>Ceiling on the stabilizer in g, so it assists rather than
        /// teleports the car sideways.</summary>
        public float lateralDampMaxG = 0.45f;
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

        [Header("Advanced tune (written by ApplySetup — all default to no effect)")]
        /// <summary>Static camber, degrees. Negative is the useful direction.</summary>
        public float camberFrontDeg, camberRearDeg;
        /// <summary>Static toe, degrees. POSITIVE IS TOE-IN.</summary>
        public float toeFrontDeg, toeRearDeg;
        /// <summary>How hard the differential ties the two wheels of the driven
        /// axle together, 0 = open, 1 = solid. Split on/off throttle because
        /// that is the distinction a plate pack actually makes.</summary>
        [Range(0f, 1f)] public float diffAccelLock;
        [Range(0f, 1f)] public float diffDecelLock;
        /// <summary>Standing clamp force in the plate pack, N. It dominates when
        /// there is little torque about and is irrelevant when there is a lot,
        /// which is what makes an LSD felt on corner ENTRY and not only on exit.
        /// </summary>
        public float diffPreloadN;
        /// <summary>Share of downforce carried by the front axle. At 0.5 with the
        /// CG at the geometric midpoint this is identical to applying the whole
        /// force at the CG, which is what the car did before.</summary>
        [Range(0f, 1f)] public float downforceBalanceFront = 0.5f;

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
        /// <summary>Steering actuator rate, deg/s. 220 -> 260: at 30 m/s the
        /// front axle saturates at only 6.6 deg of the 22 deg available (the
        /// rest is dead travel), so this is 30 ms to saturation rather than 25 —
        /// below the perceptual floor on its own, and not the fix for a car that
        /// feels late (see <see cref="yawDampGrip"/>). Worth having anyway for
        /// hairpins, and 260 stays well under steerRateDriftDeg so the grip ->
        /// drift blend below keeps its shape.</summary>
        public float steerRateDeg = 260f;
        public float steerRateDriftDeg = 400f;   // lock-to-lock in 0.3 s
        public float gripBonus = 1f;

        // ---- bolt-on mods (LifeSim parts shop) ----
        /// <summary>
        /// Welded rear diff. Set from the owned car's mods.
        ///
        /// A property rather than a plain field because the SETUP reads it to
        /// decide the differential, and the setup is applied at the end of
        /// ApplySpec — so assigning this afterwards silently did nothing, and a
        /// welded car raced with an open diff while keeping the weld's wheelspin
        /// penalty. The call site is fixed; this makes the ordering stop
        /// mattering, which is the difference between a bug that was fixed and a
        /// bug that cannot come back.
        /// </summary>
        public bool weldedDiff
        {
            get => weldedDiffFitted;
            set
            {
                if (weldedDiffFitted == value) return;
                weldedDiffFitted = value;
                if (setupBaselineCaptured) ApplySetup();
            }
        }
        [SerializeField] bool weldedDiffFitted;
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

        /// <summary>
        /// Yaw damping while GRIPPING — a field, not a const, because it is the
        /// one number in this table that decides how late the car feels and it
        /// wants to be reachable from the inspector.
        ///
        /// It was 1.6, and 1.6 was the "too late" the player was feeling. As a
        /// torque it is I*1.6 = 2868 N.m per rad/s on a 1280 kg car; the rear
        /// axle's own natural yaw damping at 30 m/s is 2*C*Fz*(b/v)*b = 3383,
        /// so the artificial term added 85% on top of the entire rear axle and
        /// cut steady-state yaw gain to 0.54 of what the tires asked for. Worse,
        /// the natural term falls off as 1/v and this one does not, so by 60 m/s
        /// it was 1.7x the real one — which is exactly the "I need more lock
        /// than I expected, and more of it the faster I go" signature.
        ///
        /// 0.9 puts the gain at 0.68 — about +25% yaw response — and it is a
        /// reduction rather than a removal, because the comment above is right
        /// that a Rigidbody has no synthetic heading integrator and the 2D
        /// source's numbers leave the car rotating after the driver stopped
        /// asking.
        ///
        /// Lower damping lengthens the time constant, so the obvious objection
        /// is that the car gets SLOWER to respond. It does not: for w' = (T -
        /// D*w)/I the initial slope T/I is independent of D, and w(t) is
        /// monotonically decreasing in D at every t > 0. At every instant after
        /// the input the less-damped car has more yaw rate, not less.
        ///
        /// This is the arcade STABILITY layer, not a steering-system parameter.
        /// The advanced-tuning screen deliberately does not expose it — a slider
        /// on this is a cheat slider. What that screen exposes is the rack:
        /// steering lock, rate, and the input-side self-centring.
        /// </summary>
        public float yawDampGrip = 0.9f;

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

        /// <summary>Torque off the curve with no forced-induction layer on top.
        /// The setup ranges derive from this rather than from
        /// <see cref="GetTorqueAtRPM"/>, so that bolting on a blower does not
        /// move where a slider's ends sit — the garage cannot see the blower at
        /// the moment it draws the row, and the two must agree.</summary>
        public float StockTorqueAtRPM(float rpm) => RawTorqueAtRPM(rpm);

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

            // LAST, and in this order. Everything above re-derives a chassis
            // from the spec and the parts; the driver's own setup is a decision
            // ON TOP of that result, so it has to be the thing that gets the
            // final word or half of it would be silently overwritten.
            CaptureSetupBaseline();
            ApplySetup();
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
            // Kept UNCLAMPED as well. The setup's tyre-pressure term multiplies
            // this and then clamps once, so that a car on stage-3 suspension —
            // where the stage alone already asks for 13.42 and is cut to 13 —
            // still gets something back for raising its pressures. Clamping
            // twice made the whole upper half of that slider pure downside on
            // exactly the cars the player had spent the most on.
            rawCorneringStiffness =
                stockCorneringStiffness * CarTune.SuspStageMult(activeTune.suspension);
            corneringStiffness = Mathf.Min(rawCorneringStiffness, CorneringStiffnessCap);
        }

        /// <summary>Cornering stiffness before the ceiling. See ApplySetup.
        /// Not [SerializeField] — it is derived, and the inspector showing a
        /// second stiffness would invite somebody to edit the wrong one.
        /// </summary>
        [System.NonSerialized] public float rawCorneringStiffness = DefaultCorneringStiffness;
        /// <summary>The ceiling the drift tuning was established against — above
        /// it the rear steps out before the front loads and the car darts.
        /// </summary>
        public const float CorneringStiffnessCap = 13f;

        bool tuneBaselineCaptured;
        float stockBrakeDemandG, stockCorneringStiffness, stockGripBonus;
        /// <summary>How much the POWER stage scaled the torque curve by. Held so
        /// DeriveDrag can take it back out — see the note there.</summary>
        float powerScale = 1f;

        // ================= advanced tuning (the driver's own setup) =========

        /// <summary>
        /// Everything <see cref="ApplySetup"/> writes, as it stood before it
        /// wrote anything. Not the same job as the stock* fields above: those
        /// exist because ApplyTuneHandling reads the fields it writes, and this
        /// exists because ApplySetup runs AFTER every derivation and so has to
        /// know what the derivations produced.
        /// </summary>
        struct SetupBaseline
        {
            public float brakeDemandG, brakeFrontShare;
            public float tireMuFront, tireMuRear, corneringStiffness, rawCorneringStiffness;
            public float maxSteerLowSpeedDeg, steerRateDeg;
            public float springRateFront, springRateRear, damperFront, damperRear;
            public float antiRollFront, antiRollRear, antiRollMaxForce;
            public float restLength, cgHeight;
            public float frontDriveShare;
            public float downforceWeightFractionAtVmax, downforceBalanceFront;
            public float[] gearRatios;

            public void Capture(CarController c)
            {
                brakeDemandG = c.brakeDemandG; brakeFrontShare = c.brakeFrontShare;
                tireMuFront = c.tireMuFront; tireMuRear = c.tireMuRear;
                corneringStiffness = c.corneringStiffness;
                rawCorneringStiffness = c.rawCorneringStiffness;
                maxSteerLowSpeedDeg = c.maxSteerLowSpeedDeg; steerRateDeg = c.steerRateDeg;
                springRateFront = c.springRateFront; springRateRear = c.springRateRear;
                damperFront = c.damperFront; damperRear = c.damperRear;
                antiRollFront = c.antiRollFront; antiRollRear = c.antiRollRear;
                antiRollMaxForce = c.antiRollMaxForce;
                restLength = c.restLength; cgHeight = c.cgHeight;
                frontDriveShare = c.frontDriveShare;
                downforceWeightFractionAtVmax = c.downforceWeightFractionAtVmax;
                downforceBalanceFront = c.downforceBalanceFront;
                gearRatios = c.gearRatios == null ? null : (float[])c.gearRatios.Clone();
            }

            /// <summary>Put everything back. Used when a car is handed a NULL
            /// setup — it must end up exactly where the derivations left it.
            /// </summary>
            public void Restore(CarController c)
            {
                RestoreOwned(c);
                c.brakeDemandG = brakeDemandG;
                c.corneringStiffness = corneringStiffness;
                c.rawCorneringStiffness = rawCorneringStiffness;
                c.springRateFront = springRateFront; c.springRateRear = springRateRear;
                c.damperFront = damperFront; c.damperRear = damperRear;
                c.antiRollFront = antiRollFront; c.antiRollRear = antiRollRear;
                c.antiRollMaxForce = antiRollMaxForce;
                c.frontDriveShare = frontDriveShare;
                if (gearRatios != null) c.gearRatios = (float[])gearRatios.Clone();
            }

            /// <summary>
            /// Put back only the fields ApplySetup OWNS — the ones no derivation
            /// ever rewrites, so whatever is sitting in them is last race's tune
            /// and nothing else.
            ///
            /// The split matters and getting it backwards is silent. Restoring
            /// the whole struct before a re-capture would undo ScaleChassisToMass:
            /// buy a weight stage, and the freshly derived 40477 N/m spring gets
            /// overwritten by the 47100 the heavier car had, and that wrong
            /// number becomes the new baseline. Restoring NOTHING is the mirror
            /// failure: brakeFrontShare would snapshot last race's 65% as this
            /// race's "stock", and a +15% front bias would creep another 15%
            /// every time the player lined up.
            /// </summary>
            public void RestoreOwned(CarController c)
            {
                c.brakeFrontShare = brakeFrontShare;
                c.tireMuFront = tireMuFront; c.tireMuRear = tireMuRear;
                c.maxSteerLowSpeedDeg = maxSteerLowSpeedDeg; c.steerRateDeg = steerRateDeg;
                c.restLength = restLength; c.cgHeight = cgHeight;
                c.downforceWeightFractionAtVmax = downforceWeightFractionAtVmax;
                c.downforceBalanceFront = downforceBalanceFront;
            }

            /// <summary>
            /// The range basis, built from the SNAPSHOT rather than from the
            /// live car.
            ///
            /// CarSetupBasis.FromController reads whatever is in the fields right
            /// now, and after one ApplySetup that is a tuned car — so deriving
            /// ranges from it would compound every setting on the second call.
            /// SetSetup after ApplySpec is exactly that second call, and it is on
            /// the normal path.
            /// </summary>
            public CarSetupBasis BasisFor(CarController c)
            {
                var b = CarSetupBasis.FromController(c);
                b.brakeDemandG = brakeDemandG; b.brakeFrontShare = brakeFrontShare;
                b.tireMuFront = tireMuFront; b.tireMuRear = tireMuRear;
                b.corneringStiffness = corneringStiffness;
                b.rawCorneringStiffness = rawCorneringStiffness;
                b.maxSteerLowSpeedDeg = maxSteerLowSpeedDeg; b.steerRateDeg = steerRateDeg;
                b.springRateFront = springRateFront; b.springRateRear = springRateRear;
                b.damperFront = damperFront; b.damperRear = damperRear;
                b.antiRollFront = antiRollFront; b.antiRollRear = antiRollRear;
                b.restLength = restLength; b.cgHeight = cgHeight;
                b.frontDriveShare = frontDriveShare;
                b.downforceWeightFractionAtVmax = downforceWeightFractionAtVmax;
                b.downforceBalanceFront = downforceBalanceFront;
                if (gearRatios != null) b.gearRatios = gearRatios;
                // The two DERIVED fields, recomputed from the snapshot. Missing
                // these is the whole hole this method exists to plug and it is
                // easy to miss twice: FromController computed firstGearForceN
                // from the LIVE gearbox, so putting the baseline array back
                // above does not put the force back. A car on a short final
                // drive would then quote a preload range 30% too wide on the
                // second apply, and applying a setup twice would not equal
                // applying it once.
                b.fourWheelDrive = b.frontDriveShare > 0.01f && b.frontDriveShare < 0.99f;
                b.firstGearForceN = CarSetupBasis.FirstGearForceFor(
                    c.StockTorqueAtRPM(0.6f * c.redlineRPM), b.gearRatios,
                    b.finalDrive, b.drivetrainEfficiency, b.wheelRadius);
                return b;
            }
        }

        SetupBaseline setupBaseline;
        bool setupBaselineCaptured;

        /// <summary>The driver's own tune, already gated by the garage against
        /// the parts this car actually carries. Null on a standalone editor
        /// race, on every AI car, and on a stock car — and a null setup means
        /// the car behaves exactly as it did before this feature existed.
        /// </summary>
        public CarSetup activeSetup { get; private set; }

        /// <summary>
        /// Hand the car its setup. Safe either side of <see cref="ApplySpec"/>:
        /// before, it is stored and ApplySpec applies it at the end; after, it
        /// is applied immediately. Either way ApplySetup reads only the
        /// baseline, so calling this ten times cannot compound.
        /// </summary>
        public void SetSetup(CarSetup s)
        {
            activeSetup = s;
            if (setupBaselineCaptured) ApplySetup();
        }

        /// <summary>The basis this car's setup ranges are derived from. Anything
        /// outside this class that needs to evaluate a range against this car
        /// must use THIS and never CarSetupBasis.FromController — the live
        /// fields are a tuned car the moment ApplySetup has run once.</summary>
        public CarSetupBasis SetupRangeBasis => setupBaselineCaptured
            ? setupBaseline.BasisFor(this)
            : CarSetupBasis.FromController(this);

        /// <summary>
        /// Snapshot what the derivations produced, so ApplySetup has something
        /// honest to work from.
        ///
        /// NOT latched-once, unlike <see cref="tuneBaselineCaptured"/>, and the
        /// difference is the single easiest thing in this file to "fix" into a
        /// bug. ApplyTuneHandling latches because it writes the same fields it
        /// reads. This runs after ScaleChassisToMass, which re-derives every
        /// spring and bar from mass on every single ApplySpec — so a latched
        /// baseline would leave a lightened car's setup sitting on the heavy
        /// car's numbers.
        ///
        /// And the subtle half: restore the OWNED fields before capturing, and
        /// only those. Nothing upstream re-derives brakeFrontShare,
        /// maxSteerLowSpeedDeg, restLength, the tyre mus or the downforce
        /// fraction — ApplySetup wrote those itself last time round, so
        /// capturing them where they stand would snapshot the previous TUNE as
        /// the new "stock" and every setting would creep a little further every
        /// time a race loaded. See RestoreOwned for why the other half must NOT
        /// be restored.
        /// </summary>
        void CaptureSetupBaseline()
        {
            if (setupBaselineCaptured) setupBaseline.RestoreOwned(this);
            setupBaseline.Capture(this);
            setupBaselineCaptured = true;
        }

        /// <summary>
        /// Write the setup onto the physics. Everything here is either a direct
        /// assignment onto a knob the model already had, or one of the four
        /// small new models in the region below — nothing rewrites an equation.
        /// </summary>
        void ApplySetup()
        {
            if (!setupBaselineCaptured) return;
            var b = setupBaseline;
            var s = activeSetup;

            // A car with no setup — or with one that is still entirely at
            // factory — is restored to exactly what the derivations produced and
            // then left alone. This is the guarantee that every AI car, every
            // standalone editor race and every stock car drives bit-for-bit as
            // it did before advanced tuning existed.
            //
            // The IsFactory half matters as much as the null half: the garage
            // sanitizes a setup on the way to EVERY race and never hands over
            // null, so a stock player car arrives here with an all-zero object,
            // not with nothing. Without this test the "no setup" path would be
            // the one path no player ever took.
            if (s == null || s.IsFactory)
            {
                b.Restore(this);
                camberFrontDeg = camberRearDeg = toeFrontDeg = toeRearDeg = 0f;
                diffAccelLock = diffDecelLock = diffPreloadN = 0f;
                if (weldedDiff) { diffAccelLock = 1f; diffDecelLock = 1f; }
                DeriveDownforce();
                if (Body != null) Body.centerOfMass = new Vector3(0f, cgHeight, 0f);
                return;
            }

            // The ranges are derived from the SNAPSHOT, never from the live car.
            // Reading the live fields here is what makes a setting compound on
            // the second call — and SetSetup after ApplySpec is a second call on
            // the ordinary path, not an edge case.
            var basis = b.BasisFor(this);

            // ---- tires and brakes ----
            var rPf = CarSetupRanges.Of(basis, SetupParam.TyrePressureFront);
            var rPr = CarSetupRanges.Of(basis, SetupParam.TyrePressureRear);
            float nF = CarSetupRanges.PressureNorm(rPf, s.tyrePressureFront);
            float nR = CarSetupRanges.PressureNorm(rPr, s.tyrePressureRear);

            // Peak grip is best AT the recommended pressure and falls off either
            // side of it — a crowned patch under-inflated, a rolled-under one
            // over. A parabola is the cheapest shape that is still honest, and
            // it makes the factory setting a real choice rather than a floor.
            tireMuFront = b.tireMuFront * (1f - PressureMuLoss * nF * nF);
            tireMuRear = b.tireMuRear * (1f - PressureMuLoss * nR * nR);
            // Carcass stiffness rises with pressure, and that IS the trade: a
            // harder tyre turns in sharper and holds less. Cornering stiffness
            // is per-car rather than per-axle, so it takes the mean — and the 13
            // ceiling is re-applied so no setup can breach the limit the whole
            // drift layer was established against.
            // Off the UNCLAMPED figure, clamped once here — see
            // ApplyTuneHandling. Clamping the stage first and then multiplying
            // meant a stage-3 car got the pressure penalty and none of the gain.
            corneringStiffness = Mathf.Min(
                b.rawCorneringStiffness * (1f + PressureStiffGain * (nF + nR)),
                CorneringStiffnessCap);

            brakeDemandG = b.brakeDemandG *
                CarSetupRanges.Of(basis, SetupParam.BrakePressure).Value(s.brakePressure);
            brakeFrontShare = CarSetupRanges.Of(basis, SetupParam.BrakeBalance).Value(s.brakeBalance);

            // ---- alignment ----
            maxSteerLowSpeedDeg = CarSetupRanges.Of(basis, SetupParam.SteerLock).Value(s.steerLock);
            steerRateDeg = CarSetupRanges.Of(basis, SetupParam.SteerRate).Value(s.steerRate);
            camberFrontDeg = CarSetupRanges.Of(basis, SetupParam.CamberFront).Value(s.camberFront);
            camberRearDeg = CarSetupRanges.Of(basis, SetupParam.CamberRear).Value(s.camberRear);
            toeFrontDeg = CarSetupRanges.Of(basis, SetupParam.ToeFront).Value(s.toeFront);
            toeRearDeg = CarSetupRanges.Of(basis, SetupParam.ToeRear).Value(s.toeRear);

            // ---- springs and dampers ----
            springRateFront = CarSetupRanges.Of(basis, SetupParam.SpringFront).Value(s.springFront);
            springRateRear = CarSetupRanges.Of(basis, SetupParam.SpringRear).Value(s.springRear);
            damperFront = CarSetupRanges.Of(basis, SetupParam.DamperFront).Value(s.damperFront);
            damperRear = CarSetupRanges.Of(basis, SetupParam.DamperRear).Value(s.damperRear);
            antiRollFront = CarSetupRanges.Of(basis, SetupParam.ArbFront).Value(s.arbFront);
            antiRollRear = CarSetupRanges.Of(basis, SetupParam.ArbRear).Value(s.arbRear);

            // Ride height has to move the CENTRE OF GRAVITY or it means nothing:
            // rest length alone changes only available travel and where the
            // wheel is drawn, because static compression is mg/4k either way.
            // Moving the CG makes the lower car transfer less weight through the
            // AddForceAtPosition physics that is already there — real physics for
            // one line, and no new equation anywhere.
            restLength = CarSetupRanges.Of(basis, SetupParam.RideHeight).Value(s.rideHeight);
            cgHeight = Mathf.Max(0.30f, b.cgHeight + (restLength - b.restLength));
            if (Body != null) Body.centerOfMass = new Vector3(0f, cgHeight, 0f);

            // ---- differential ----
            if (weldedDiff)
            {
                // A weld is not a setting. It is fully locked both ways, and it
                // KEEPS its separate wheelspin gain: a weld also drags the inside
                // wheel round a corner, which a plate pack does not do.
                diffAccelLock = 1f; diffDecelLock = 1f; diffPreloadN = 0f;
            }
            else
            {
                diffAccelLock = CarSetupRanges.Of(basis, SetupParam.DiffAccel).Value(s.diffAccel);
                diffDecelLock = CarSetupRanges.Of(basis, SetupParam.DiffDecel).Value(s.diffDecel);
                diffPreloadN = CarSetupRanges.Of(basis, SetupParam.DiffPreload).Value(s.diffPreload);
            }
            if (basis.fourWheelDrive)
                frontDriveShare = CarSetupRanges.Of(basis, SetupParam.DriveSplit).Value(s.driveSplit);

            // ---- gearing ----
            ApplyGearing(b, basis, s);

            // ---- aero ----
            downforceWeightFractionAtVmax =
                CarSetupRanges.Of(basis, SetupParam.AeroLevel).Value(s.aeroLevel);
            downforceBalanceFront =
                CarSetupRanges.Of(basis, SetupParam.AeroBalance).Value(s.aeroBalance);
            DeriveDownforce();
            // NOT DeriveDrag(). Drag is solved from the STOCK torque so it stays
            // a property of the body — re-solving it after a short final drive
            // would quietly hand the car back the top speed the short gearing was
            // supposed to cost it. A short final drive should hit the limiter
            // below vmax. That is what a short final drive IS.
        }

        void ApplyGearing(in SetupBaseline b, in CarSetupBasis basis, CarSetup s)
        {
            if (b.gearRatios == null || b.gearRatios.Length == 0) return;
            int n = b.gearRatios.Length;
            // Read the caller's array, never grow it. SetSetup takes no
            // ownership of the object it is handed, and today it happens to be
            // a Sanitize clone — but a caller that passed the SAVE's own setup
            // would have the race scene writing into the player's file.

            // The final drive is applied as a SCALE on every gear, never by
            // writing finalDrive. BuildGearRatios solves for the PRODUCT
            // ratio*finalDrive and every consumer uses that product, so the
            // field cancels out of its own definition — writing it does exactly
            // nothing, in either order. LifeSimSelfTest asserts that a final
            // drive setting actually moves first gear, which is the only thing
            // standing between this screen and a slider that does nothing.
            var rFd = CarSetupRanges.Of(basis, SetupParam.FinalDrive);
            float fdScale = rFd.def > 1e-3f ? rFd.Value(s.finalDriveScale) / rFd.def : 1f;

            var outv = new float[n];
            for (int g = 0; g < n; g++)
            {
                var rg = CarSetupRanges.Of(basis, CarSetupTable.GearParam(g));
                float t = s.gear != null && g < s.gear.Length ? s.gear[g] : 0f;
                outv[g] = rg.Value(t) * fdScale;
            }
            // Keep the box strictly descending whatever the player asked for. A
            // second gear taller than first is a car that cannot pull away, and
            // the gearbox's upshift logic assumes the order.
            for (int g = 1; g < n; g++)
                outv[g] = Mathf.Min(outv[g], outv[g - 1] * GearMinStep);
            gearRatios = outv;
        }

        /// <summary>
        /// The gearbox a setup actually produces, without applying it. The setup
        /// screen needs this because the clamp above is REACHABLE by the most
        /// obvious edit on the page — every gear shape has a top pair close
        /// enough together that a +20% trim on the taller one gets cut — and a
        /// row that prints a ratio the car will not use is a lying row.
        /// </summary>
        public static float[] TunedRatios(in CarSetupBasis basis, CarSetup s)
        {
            if (basis.gearRatios == null || basis.gearRatios.Length == 0 || s == null)
                return basis.gearRatios;
            int n = basis.gearRatios.Length;
            var rFd = CarSetupRanges.Of(basis, SetupParam.FinalDrive);
            float fdScale = rFd.def > 1e-3f ? rFd.Value(s.finalDriveScale) / rFd.def : 1f;
            var outv = new float[n];
            for (int g = 0; g < n; g++)
            {
                var p = CarSetupTable.GearParam(g);
                outv[g] = CarSetupRanges.Of(basis, p).Value(s.Get(p)) * fdScale;
            }
            for (int g = 1; g < n; g++)
                outv[g] = Mathf.Min(outv[g], outv[g - 1] * GearMinStep);
            return outv;
        }

        /// <summary>
        /// The steer rotation for wheel <paramref name="i"/>, static toe
        /// included.
        ///
        /// Extracted because there were two copies of this line — one in
        /// RefreshSlipAngles and one in TireForces — and toe would have had to
        /// be added to both. Two expressions that must stay identical, will not.
        ///
        /// Mount order is FL, FR, RL, RR and the LEFT wheels are the -halfTrack
        /// ones, so left takes +toe to point its nose at the centreline.
        /// POSITIVE IS TOE-IN.
        /// </summary>
        Quaternion SteerRotFor(int i)
        {
            float a = SteerDegFor(i);
            return a == 0f ? Quaternion.identity : Quaternion.AngleAxis(a, transform.up);
        }

        /// <summary>The angle behind <see cref="SteerRotFor"/>, so the wheel
        /// VISUAL can use the same number without building a world-space
        /// quaternion and unpicking it again.</summary>
        float SteerDegFor(int i)
        {
            bool front = i < 2;
            float toe = (front ? toeFrontDeg : toeRearDeg) * ((i == 0 || i == 2) ? 1f : -1f);
            return (front ? steerAngleDeg : 0f) + toe;
        }

        /// <summary>
        /// Static camber, as an axle grip multiplier that flips sign with roll.
        ///
        /// Nothing in this model carries a wheel roll angle, and inventing one
        /// would mean a real tyre model — which this game is explicitly not.
        /// What a camber setting DOES is trade straight-line contact patch for
        /// cornering contact patch, and the roll that makes that trade pay is
        /// already being measured one function upstream for the anti-roll bars.
        /// So: cost it always, pay it back in proportion to how hard this axle
        /// is actually rolled.
        ///
        /// At -2.0 deg that is -2.4% of grip on a straight and +3.2% net at the
        /// limit, breaking even around 0.4 g. The whole span stays inside +-5%,
        /// which keeps camber a setup decision and not a power-up. Positive
        /// camber is offered and is simply worse everywhere, as it should be.
        /// </summary>
        float CamberMu(bool front)
        {
            float camDeg = front ? camberFrontDeg : camberRearDeg;
            if (camDeg > -1e-4f && camDeg < 1e-4f) return 1f;
            int l = front ? 0 : 2, r = front ? 1 : 3;
            float roll = Mathf.Clamp01(
                Mathf.Abs(suspensionCompression[l] - suspensionCompression[r]) / CamberRollRef);
            float c = -camDeg;
            // The straight-line term is a LOSS only. Without the Max, positive
            // camber came out as a small free grip bonus on a flat car — which
            // is a slider documented as pure downside quietly paying out for the
            // whole of a drag run.
            return Mathf.Max(0.85f,
                1f - CamberStraightLoss * Mathf.Max(0f, c) + CamberRollGain * c * roll);
        }
        /// <summary>Compression difference the camber trade is measured against.
        /// At the reference chassis 1.0 g of lateral puts about 42 mm across the
        /// front axle, so the full camber benefit arrives right at the limit.
        /// </summary>
        const float CamberRollRef = 0.040f;
        const float CamberStraightLoss = 0.012f;   // per degree, going straight
        const float CamberRollGain = 0.028f;       // per degree, at full roll

        /// <summary>
        /// How one wheel's share of its axle's drive torque is decided.
        ///
        /// An open differential splits evenly whatever the wheels are doing; a
        /// locked one feeds the wheel with the load on it, which is the whole
        /// reason to fit one. Pure function of three numbers so the self-test
        /// can pin it without a physics step — and so the "an open diff is
        /// bit-for-bit the car we shipped" guarantee is checkable rather than
        /// argued.
        /// </summary>
        public static float DiffShare(float loadShare, float evenShare, float lockT) =>
            Mathf.Lerp(evenShare, loadShare, Mathf.Clamp01(lockT));

        /// <summary>Peak grip lost at the far end of the pressure range. A real
        /// pressure sweep moves peak mu 5-10% over 10 psi; 8% over the full
        /// span keeps setup as fine-tuning and never a power-up.</summary>
        const float PressureMuLoss = 0.08f;
        /// <summary>Cornering stiffness gained per unit of normalised pressure,
        /// per axle. About +-10% across the full span.</summary>
        const float PressureStiffGain = 0.05f;
        /// <summary>The most two adjacent gears may be squeezed together before
        /// the clamp bites. Anything closer is not a gearbox.</summary>
        public const float GearMinStep = 0.97f;

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
        /// <summary>
        /// The reference chassis every car's rates are scaled from: the RX-7 FD
        /// this project's handling was established against.
        ///
        /// Public because the garage has to quote a spring rate for a car that
        /// has no CarController in the scene — the menu lives two scene loads
        /// away from the physics. Same rule <see cref="CarTune"/> opens with: if
        /// the shop screen and the stopwatch keep two copies of what a number
        /// is, they will disagree, and nobody will find it for weeks.
        /// </summary>
        public const float ChassisRefMass = 1280f;
        public const float SpringFrontRef = 47100f;
        public const float SpringRearRef = 35300f;
        public const float DamperFrontRef = 4000f;
        public const float DamperRearRef = 3400f;
        public const float AntiRollFrontRef = 16000f;
        public const float AntiRollRearRef = 12000f;

        /// <summary>
        /// The fields ApplySpec does NOT rewrite, as constants the garage can
        /// read. Each one must stay equal to the field initialiser above it — a
        /// value that drifts here quotes a range the race scene will not honour,
        /// which is the exact failure mode the fence exists to prevent. Pinned
        /// by the self-test rather than by hoping.
        /// </summary>
        public const float DefaultBrakeDemandG = 0.9f;
        public const float DefaultBrakeFrontShare = 0.6f;
        public const float DefaultTireMuFront = 1.010f;
        public const float DefaultTireMuRear = 1.030f;
        public const float DefaultCorneringStiffness = 11.0f;
        public const float DefaultRestLength = 0.30f;
        public const float DefaultCgHeight = 0.465f;
        public const float DefaultMaxSteerLowSpeedDeg = 34f;
        public const float DefaultMaxSteerHighSpeedDeg = 12f;
        public const float DefaultMaxSteerDriftDeg = 45f;
        public const float DefaultSteerRateDeg = 260f;
        public const float DefaultSteerRateDriftDeg = 400f;
        public const float DefaultDownforceWeightFraction = 0.35f;
        public const float DefaultDrivetrainEfficiency = 0.88f;
        public const float DefaultFinalDrive = 4.10f;

        void ScaleChassisToMass()
        {
            float k = massKg / ChassisRefMass;

            springRateFront = SpringFrontRef * k;
            springRateRear = SpringRearRef * k;
            // Critical damping goes with sqrt(k*m), and k itself scales with
            // mass here, so the damper scales linearly too — keeping the damping
            // RATIO constant is the part that matters for how settled it feels.
            damperFront = DamperFrontRef * k;
            damperRear = DamperRearRef * k;
            antiRollFront = AntiRollFrontRef * k;
            antiRollRear = AntiRollRearRef * k;

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
            // Split per axle so an aero balance means something. Kept as two
            // derived coefficients rather than a runtime multiply, because
            // AeroForces runs every tick and this runs once per spec.
            downforceFrontCoef = downforceCoefficient * downforceBalanceFront;
            downforceRearCoef = downforceCoefficient * (1f - downforceBalanceFront);
        }
        float downforceFrontCoef, downforceRearCoef;

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

                Quaternion steerRot = SteerRotFor(i);
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

            // Axle loads for the differential. wheelLoad[] is a COMPLETED pass
            // by now — SuspensionAndLoads runs one call earlier in FixedUpdate —
            // so a load-biased diff needs no restructuring of the loop below,
            // only a different share going into it.
            float frontAxleLoad = 0f, rearAxleLoad = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (!wheelGrounded[i]) continue;
                if (i < 2) frontAxleLoad += wheelLoad[i]; else rearAxleLoad += wheelLoad[i];
            }

            for (int i = 0; i < 4; i++)
            {
                if (!wheelGrounded[i]) continue;
                bool front = i < 2;
                Vector3 mount = transform.TransformPoint(wheelLocalPos[i]);
                Vector3 contact = mount - transform.up * (restLength + wheelRadius - suspensionCompression[i]);

                Quaternion steerRot = SteerRotFor(i);
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
                           (front ? tireMuFront : tireMuRear) * CamberMu(front);
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
                    // THE DIFFERENTIAL. An open one splits evenly; a locked one
                    // feeds the loaded wheel, whose friction circle is bigger by
                    // exactly the same ratio, which is why locking buys traction
                    // rather than just moving the problem.
                    //
                    // With every lock at zero this reduces to
                    // driveForce * axleShare / axleWheels — bit-for-bit the line
                    // it replaced. That is what keeps every AI car and every car
                    // without an LSD driving exactly as it did.
                    float even = 1f / axleWheels;
                    float axleLoad = front ? frontAxleLoad : rearAxleLoad;
                    float loadShare = (axleWheels > 1 && axleLoad > 1f) ? Fz / axleLoad : even;

                    // Preload is a fixed clamping force in the plate pack: it
                    // dominates when there is little torque about and washes out
                    // when there is a lot. That is what makes an LSD felt on
                    // corner ENTRY and not only on the way out.
                    float axleDemandN = Mathf.Abs(driveForce * axleShare);
                    float preloadT = axleDemandN > 1f
                        ? Mathf.Clamp01(diffPreloadN / axleDemandN) : 0f;
                    float lockT = Mathf.Max(preloadT,
                        driveForce >= 0f ? diffAccelLock : diffDecelLock);

                    driveDemand = driveForce * axleShare * DiffShare(loadShare, even, lockT);
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
            // WELDED DIFF, the half of it the lock cannot express. There IS a
            // left/right differential now (see DiffShare in TireForces), and a
            // weld drives it fully locked both ways — but locking only decides
            // where the torque goes. The other reason people weld a diff is that
            // the driven wheels break away together
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
            else yawDamp = yawDampGrip;

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
                // Both of these were declared as named constants above and then
                // shadowed by identical inline literals, so editing the constant
                // did nothing at all. Wired up, so the next tuning pass changes
                // what it thinks it is changing.
                float steerRelease = 1f - Mathf.Min(1f, steerMag / CountersteerReleaseSpan);
                if (excess > 0f && steerRelease > 0f)
                {
                    float accel = Mathf.Min(
                        excess * CountersteerGain * countersteerAssist * steerRelease,
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
                // Applied at the two axle midpoints rather than at the CG, so an
                // aero balance produces a real pitch couple and loads the axle
                // it is dialled toward. With balance at 0.5 and the CG at the
                // geometric centre these two sum to exactly the single CG force
                // this used to be — an un-setup car is untouched.
                Vector3 fMid = transform.TransformPoint(
                    (wheelLocalPos[0] + wheelLocalPos[1]) * 0.5f);
                Vector3 rMid = transform.TransformPoint(
                    (wheelLocalPos[2] + wheelLocalPos[3]) * 0.5f);
                Body.AddForceAtPosition(-transform.up * (downforceFrontCoef * v2), fMid);
                Body.AddForceAtPosition(-transform.up * (downforceRearCoef * v2), rMid);
                // Drag and rolling resistance stay at the CG: they make no pitch
                // couple today and should not start making one.
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
                // Through SteerDegFor, so the visible wheel carries the toe the
                // physics is using. This was the THIRD copy of the steer
                // expression — the extraction note on SteerRotFor says there
                // were two, and it was wrong when it was written.
                wheelHubs[i].localRotation = front || toeRearDeg != 0f
                    ? Quaternion.Euler(0f, SteerDegFor(i), 0f)
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
