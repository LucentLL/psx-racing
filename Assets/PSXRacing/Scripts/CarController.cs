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
        /// <summary>
        /// Engine braking, as FRACTIONS OF THE ENGINE'S OWN PEAK TORQUE: this
        /// much crank drag with the throttle shut at idle, rising linearly to
        /// this much at redline. MW05 tunes it the same way — its VLT
        /// ENGINE_BRAKING "multiplies maximum torque at current gear", 0.70-0.90
        /// — and it is what gives those cars their lift-off weight: let go and
        /// the car slows, visibly, until it stops.
        ///
        /// It was a flat 38 Nm rising to 78 for EVERY car: the RX-7's 13B
        /// figure, never rescaled by ApplySpec. On the catalog's 952 Nm
        /// muscle car that was 8% of its torque and on its 71 Nm hatchback
        /// 110% — the small cars braked harder off throttle than they pulled
        /// on it, and the big ones coasted like bicycles. A fraction of the
        /// car's own peak makes the lift feel the same on all 317.
        ///
        /// 0.20 -> 0.60 sits under MW's 0.7-0.9 on purpose, and the reason is
        /// arithmetic, not taste. Their number drives a scripted decel; here
        /// it goes through the TYRES. On the reference FD (297 Nm peak, first
        /// gear 3.65 x 4.10 x 0.88 / 0.31 m = 42.5 N per Nm) 0.9 x 297 through
        /// first is 11.4 kN against an 8.1 kN rear friction circle — a locked
        /// rear axle on every lift. What the chosen pair does: third gear at
        /// 100 km/h is 5124 rpm, 0.66 of the way to redline, so the drag is
        /// 297 x (0.20 + 0.40 x 0.66) = 138 Nm x 17.0 N/Nm = 2.34 kN, plus
        /// ~0.40 kN of aero and rolling drag: 2.74 kN on 1280 kg = 0.22 g of
        /// lift-off deceleration, twice a real road car's and short of a
        /// brake. The old 64 Nm gave 0.12 g. In top gear at low revs it is
        /// 0.05 g, which is coasting, and the auto box downshifts at 3.4 x
        /// idle so the drag rises as the car slows — "auto brakes until it
        /// stands still", the GTPlanet description of MW. Low gears are fenced
        /// by <see cref="EngineBrakeRearCircleShare"/>. Starting values.
        /// </summary>
        public float engineBrakeFracIdle = DefaultEngineBrakeFracIdle;
        public float engineBrakeFracRedline = DefaultEngineBrakeFracRedline;
        public const float DefaultEngineBrakeFracIdle = 0.20f;
        public const float DefaultEngineBrakeFracRedline = 0.60f;
        /// <summary>
        /// Engine braking may never take more than this share of the rear
        /// friction circle, whatever the gear. A lift in first at redline asks
        /// 0.60 x 297 x 42.5 = 7.6 kN of the FD's 8.1 kN circle and would leave
        /// the rear 34% of its lateral grip mid-corner — a spin, and the
        /// handling note says never randomly loose. At 0.45 the rear keeps
        /// sqrt(1 - 0.45^2) = 89% of its lateral grip, so lift-off oversteer
        /// stays a nudge the stability layer can hold. On the FD it binds in
        /// first and second at high revs (7.6 and 4.4 kN asked) and never in
        /// third (3.0 kN, 37%).
        /// </summary>
        public const float EngineBrakeRearCircleShare = 0.30f;
        // Was 0.45, which left the rear 89% of its lateral grip on a lift. On
        // a long descent that lift is the whole road: the engine brakes the
        // rear axle for eleven kilometres and the rear is the weak end of the
        // car the entire way down, before the pedal is even touched. 0.30
        // keeps 95%, and the pedal's own budget (RearBrakeLockShare) is what
        // bounds the two together now.
        /// <summary>Peak of the STOCK torque curve, Nm — what the two fractions
        /// multiply. Derived, never configured: the default curve's 314 for the
        /// built-in car, the spec's own peak after ApplySpec. Stock rather than
        /// the power-scaled curve because engine braking is displacement and
        /// valvetrain, and a turbo stage buys none of that.</summary>
        public float engineBrakePeakNm = 314f;
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
        public float lateralDampGrip = DefaultLateralDampGrip;
        public float lateralDampDrift = DefaultLateralDampDrift;
        public const float DefaultLateralDampGrip = 3.6f;
        public const float DefaultLateralDampDrift = 0.6f;
        /// <summary>Ceiling on the stabilizer in g, so it assists rather than
        /// teleports the car sideways.</summary>
        public float lateralDampMaxG = DefaultLateralDampMaxG;
        public const float DefaultLateralDampMaxG = 0.45f;
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
        public float tireMuFront = DefaultTireMuFront;
        public float tireMuRear = DefaultTireMuRear;
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
        /// 0.35 landed the reference FD at a 1.05 coefficient, where the
        /// handling notes first wanted it (they suggested 1.0-1.2 by hand); the
        /// old flat 0.35 coefficient gave the FD 11% of its weight — enough to
        /// measure and not enough to feel.
        ///
        /// 0.70 now, and the case for it is that downforce is TYRE grip, not
        /// stabiliser. The 2026-08-31 note backed the arcade layer off because
        /// a flat 0.45 g of non-tyre force was 35% of the car's lateral
        /// budget; downforce goes the other way — it loads the wheels, the
        /// friction circle grows with the load, so the budget itself rises
        /// with speed (mu * g * (1 + df/W)) and the damper's share of it
        /// FALLS. On the FD (1280 kg, vmax 81.1 m/s, front mu 1.26):
        ///
        ///   30 m/s (108 km/h): df/W 0.048 -> 0.096, budget 1.32 g -> 1.38 g
        ///   50 m/s (180 km/h): df/W 0.133 -> 0.266, budget 1.43 g -> 1.60 g
        ///   damper share of the budget at 50 m/s: 31% -> 28%
        ///
        /// That is the "progressively more glued as speed rises" an NFS car
        /// has, bought with load rather than with a damper. What does NOT
        /// move is the front's saturation angle: circle / C = mu / 11 whatever
        /// Fz is, so the 6.6 deg of the lock note still holds and steering
        /// lock is still not the lever. What it costs is ride height: at vmax
        /// 0.7 x 12.6 kN over the 47/35 kN/m springs is 4.7 cm front and
        /// 6.2 cm rear of a 30 cm rest length, which is why this stops at 0.7
        /// and not at 1.0.
        /// </summary>
        public float downforceWeightFractionAtVmax = DefaultDownforceWeightFraction;
        public float rollingResistance = 165f;

        [Header("Steering")]
        public float maxSteerLowSpeedDeg = 34f;
        public float maxSteerHighSpeedDeg = DefaultMaxSteerHighSpeedDeg;
        /// <summary>At 34 deg falling to 22 at speed, a deep slide is
        /// mathematically uncatchable: the front wheel cannot make its slip
        /// angle change sign, so counter-steer is cosmetic. This is not a grip
        /// cheat — the friction circle still bounds lateral force.</summary>
        public float maxSteerDriftDeg = 45f;
        public float steerSpeedFalloff = DefaultSteerSpeedFalloff;
        /// <summary>Steering actuator rate, deg/s. 220 -> 260: at 30 m/s the
        /// front axle saturates at only 6.6 deg of the 22 deg available (the
        /// rest is dead travel), so this is 30 ms to saturation rather than 25 —
        /// below the perceptual floor on its own, and not the fix for a car that
        /// feels late (see <see cref="yawDampGrip"/>). Worth having anyway for
        /// hairpins, and 260 stays well under steerRateDriftDeg so the grip ->
        /// drift blend below keeps its shape.</summary>
        public float steerRateDeg = 260f;
        public float steerRateDriftDeg = 400f;   // lock-to-lock in 0.3 s
        /// <summary>Fraction of <see cref="steerRateDeg"/> the actuator is
        /// allowed at and above <see cref="steerSpeedFalloff"/> — see
        /// UpdateSteering. 260 deg/s becomes 143.</summary>
        public const float SteerRateHighSpeedFrac = 0.55f;
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
        public float brakeFrontShare = DefaultBrakeFrontShare;
        /// <summary>How much of the front/rear split follows the LIVE axle
        /// loads rather than the fixed hardware bias — see the brake block in
        /// TireForces. 0 is the old fixed-fraction behaviour.</summary>
        public const float BrakeLoadSensitivity = 0.5f;
        /// <summary>The most of its friction circle a wheel may spend on
        /// stopping, engine braking included — the ABS. See the brake block
        /// in TireForces. Front 0.92 keeps sqrt(1 - 0.92^2) = 39% of its
        /// cornering force at the limit; rear 0.75 keeps 66%, so the rear is
        /// never the axle that lets go under the pedal.</summary>
        public const float FrontBrakeLockShare = 0.92f;
        public const float RearBrakeLockShare = 0.75f;

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
        /// <summary>0 = simulator, 1 = maximum forgiveness. (An older note here
        /// claimed "the source ships 0.3"; there is no such constant in either
        /// Racing Game 2 source — this layer is the Unity port's own.)</summary>
        [Range(0f, 1f)] public float countersteerAssist = DefaultCountersteerAssist;
        public const float DefaultCountersteerAssist = 0.55f;
        /// <summary>Racing Game 2's physBrakeDrift. Zero here — see gesture 2
        /// in UpdateDriftGestures for why the brake pedal no longer starts a
        /// slide. <see cref="DefaultBrakeStabDrift"/> is what the scene builder
        /// bakes, so a stale scene cannot outvote this.</summary>
        [Range(0f, 2f)] public float brakeStabDrift = DefaultBrakeStabDrift;
        public const float DefaultBrakeStabDrift = 0f;
        [Range(0f, 2f)] public float wheelspinYawGain = 1f;

        // ---- ported tuning constants ---------------------------------------
        const float EbrakeWindow = 0.75f;
        const float EbrakeMuCollapse = 0.70f;     // rear mu -> 30% at full window
        const float EbrakeKickCooldown = 0.15f;
        /// <summary>How long a handbrake press waits for the wheels to catch up
        /// before it gives up on kicking — see gesture 1. Five ticks: the
        /// actuator crosses the 0.15 gate in about 17 ms from centre, and a
        /// player who pulls the lever and only then starts steering is making a
        /// different move.</summary>
        const float EbrakePendingSeconds = 0.08f;
        const float EbrakeKickBase = 1.2f;        // rad/s
        /// <summary>Share of road speed a handbrake kick scrubs at full steer
        /// and vmax (the source's 2.5% x 1.1). The slide is not free speed:
        /// this is the punch's cost, and the locked rears keep charging for
        /// the rest of the window through their own friction circle. Named
        /// so the self-test can hold it — an NFS drift that lost no speed
        /// would be the exploit every corner is taken with.</summary>
        public const float EbrakeKickScrub = 0.0275f;
        const float DriveGateSpeed = 1.3f;        // source 8 gu/s
        const float ThrottleSustainWindow = 0.4f;
        const float BrakeStabWindow = 0.35f;
        const float BrakeStabCooldown = 0.3f;
        const float BrakeStabBase = 0.55f;
        const float DriftEnterSlip = 0.32f;
        const float DriftExitSlip = 0.10f;
        /// <summary>How far the whole car has to be pointing away from where it
        /// is going before rear slip counts as a DRIFT rather than as a rear
        /// axle working hard: 7 degrees in, 4.6 out. See UpdateDriftState — the
        /// rear slip test on its own is satisfied by any committed corner near
        /// the limit, which is what made the drift state latch.</summary>
        const float DriftEnterBodySlip = 0.12f;
        const float DriftExitBodySlip = 0.08f;
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

        // ---- the parking brake ---------------------------------------------

        /// <summary>Road speed under which a car with the lever up and the
        /// throttle shut counts as PARKED rather than as crawling.
        ///
        /// The same 0.3 m/s the rest of this solver already calls a standstill,
        /// deliberately: it is the floor the low-speed brake hold uses and the
        /// ceiling the handbrake's own branch starts at, so parking picks up
        /// exactly where those two leave a gap and opens no new one. A car
        /// creeping at walking pace is not parked and must not be seized.
        /// </summary>
        const float ParkHoldSpeed = 0.3f;
        /// <summary>How long the car has to have been still, with nothing
        /// asked of it, before the hold latches by itself — see the parkHold
        /// line in TireForces. Short: it should be continuous with the brake
        /// that stopped the car, not a pause the gradient can use.</summary>
        const float ParkAutoSeconds = 0.5f;
        float parkRestTimer;

        /// <summary>Damping on the parked hold, in 1/s of the mass each tyre
        /// carries. It is NOT what holds the car — the gravity cancellation is
        /// — so it only has to mop up solver residue, and it is deliberately
        /// stiff: 8/s kills a millimetre-per-second wobble inside a tick
        /// without ever reaching the friction circle at speeds this small. Any
        /// larger and it becomes the bang-bang force the low-speed hold's own
        /// comment warns buzzes the car.</summary>
        const float ParkHoldDamp = 8f;

        /// <summary>Yaw rate above which a car is turning rather than parked,
        /// in rad/s. 0.35 is about 20 deg/s — slower than anything a car does
        /// on purpose and far faster than the solver's own noise at a
        /// standstill, so it separates "settling on its springs" from "still
        /// coming round".</summary>
        const float ParkHoldSpin = 0.35f;
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
        /// <summary>
        /// HOW SIDEWAYS THE CAR IS, 0 to 1 — the thing every consumer of
        /// <see cref="Drifting"/> should actually be asking.
        ///
        /// Drifting is a bool, and four separate systems used to switch on it
        /// in the same tick: the lateral stabilizer (a 6x collapse), the yaw
        /// damper (halved), the wheelspin injector (7.5x, or 10x with the
        /// e-brake window live) and the steering lock. So the instant the flag
        /// tripped the car lost most of what was holding it on the road, which
        /// made more slip, which held the flag — and that cascade is what "just
        /// tapping the turn initiates a drift" felt like from the inside.
        ///
        /// This ramps with the body slip angle instead, over a quarter of a
        /// second, so five degrees of slide gets five degrees' worth of loose
        /// car. Same threshold and span the steering lock already blends on
        /// (UpdateSteering's slideT), deliberately, so the lock and the damping
        /// cannot step at different body angles.
        /// </summary>
        public float DriftBlend { get; private set; }
        const float DriftBlendStart = 0.15f;
        const float DriftBlendSpan = 0.45f;
        const float DriftBlendTau = 0.25f;
        /// <summary>How much of the loose car a live handbrake or clutch-kick
        /// window buys before the body has gone anywhere — see the ramp in
        /// FixedUpdate.</summary>
        const float DriftBlendGestureFloor = 0.65f;
        public float EbrakeTimer { get; private set; }
        /// <summary>1 immediately after an impact, decaying to 0 across the grace
        /// window. Read by the stabilizer and the counter-steer assist.</summary>
        public float ImpactGrace01 { get; private set; }
        /// <summary>True while the ECU is actually cutting fuel. Audio gates the
        /// on-the-limiter recordings on this rather than on RPM position, because
        /// RPM alone cannot tell "deep in the red" from "bouncing off the cut".</summary>
        public bool RevLimiterActive { get; private set; }
        /// <summary>True while the tyres are holding a stationary car against
        /// gravity rather than rolling — the handbrake is up (or the pedal is
        /// buried), the throttle is shut, and the car has stopped. Read by the
        /// self-test, and worth having on the outside because "is this car
        /// parked" is a question three different systems ask by guessing.
        /// </summary>
        public bool Parked { get; private set; }
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
        /// <summary>Seconds a handbrake press is still waiting for lock — see gesture 1.</summary>
        float ebrakePending;
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

            // Interpolation, unlike the mode above, is NOT a per-car choice:
            // every car is drawn, and a body left at None repeats its last pose
            // on any rendered frame that lands inside a physics step already
            // drawn — which on a 120 Hz phone against PSXBootstrap's 60 Hz
            // physics is every other frame. The builder bakes Interpolate on
            // all four now; this is the floor for a car that never went through
            // the builder and for a scene baked before it did, because a
            // serialised field is inert until a rebake and "the AI are jerky"
            // is not a symptom anybody should have to diagnose twice.
            if (Body.interpolation == RigidbodyInterpolation.None)
                Body.interpolation = RigidbodyInterpolation.Interpolate;

            // Everything except cars (layer 2) and solid scenery.
            suspensionMask = ~((1 << 2) | (1 << solidLayer));

            staticWheelLoad = massKg * 9.81f * 0.25f;
            // The built-in car never goes through ApplySpec, so derive its aero
            // here too — otherwise standalone editor play and a race launched
            // from the LifeSim would be two different cars.
            DeriveDownforce();
            engineBrakePeakNm = PeakTorque(DefaultTorqueNm);

            RebuildGeometry();
            currentRPM = idleRPM;
        }

        /// <summary>Peak of a torque curve, Nm. Zero for no curve, so a car
        /// with none gets no engine braking rather than a default car's.</summary>
        public static float PeakTorque(float[] nm)
        {
            float p = 0f;
            if (nm != null) foreach (float v in nm) if (v > p) p = v;
            return p;
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
            // The STOCK curve's peak, not the power-scaled copy above.
            if (spec.curveNm != null && spec.curveNm.Length > 0)
                engineBrakePeakNm = PeakTorque(spec.curveNm);
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
            ApplyStageRide();
            setupBaseline.Capture(this);
            setupBaselineCaptured = true;
        }

        /// <summary>
        /// The part of a suspension build that is NOT a slider: how far the
        /// stage sits the car down. See CarTune.RideDropAtStage.
        ///
        /// Here rather than in <see cref="ApplyTuneHandling"/>, and the reason
        /// is the whole subtlety of this file. restLength and cgHeight are two
        /// of the fields ApplySetup OWNS, so <see cref="CaptureSetupBaseline"/>
        /// restores them from the previous snapshot before it captures. Written
        /// any earlier, a build bought between two races would be put straight
        /// back to the OLD stage's ride height and that would become the new
        /// baseline — the car would keep the height it had when the parts were
        /// fitted, for ever, with nothing on screen to say so.
        ///
        /// Latches its own stock pair for the same reason ApplyTuneHandling
        /// latches: it writes the fields it reads, and ApplySpec runs on every
        /// race load.
        /// </summary>
        void ApplyStageRide()
        {
            if (!stageRideCaptured)
            {
                stockRestLength = restLength;
                stockCgHeight = cgHeight;
                stageRideCaptured = true;
            }
            restLength = CarTune.RestLengthAtStage(stockRestLength, activeTune.suspension);
            cgHeight = CarTune.CgHeightAtStage(stockCgHeight, stockRestLength,
                                               activeTune.suspension);
            if (Body != null) Body.centerOfMass = new Vector3(0f, cgHeight, 0f);
        }

        bool stageRideCaptured;
        float stockRestLength, stockCgHeight;

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
        /// <summary>0.66, up from 0.60. With the load-sensitive term in
        /// TireForces this puts both axles at about the same fraction of their
        /// own friction circle at full pedal, so the car brakes STRAIGHT. The
        /// garage's BrakeBalance range is this +/- 0.15, so a player who wants
        /// the loose car can still dial 51% front.</summary>
        public const float DefaultBrakeFrontShare = 0.66f;
        /// <summary>Front 1.00 / rear 1.05 (was 1.01 / 1.03). A 2% stagger
        /// is inside the noise of one wheel finding the verge; 5% means the
        /// front reliably lets go first, which is understeer at the limit —
        /// the stable end of the car goes wide and the driver lifts. The
        /// rear's engine-braking penalty on a descent used to be larger than
        /// this stagger, which flipped the car to oversteer whenever the road
        /// went downhill in gear.</summary>
        public const float DefaultTireMuFront = 1.000f;
        public const float DefaultTireMuRear = 1.050f;
        public const float DefaultCorneringStiffness = 11.0f;
        public const float DefaultRestLength = 0.30f;
        public const float DefaultCgHeight = 0.465f;
        public const float DefaultMaxSteerLowSpeedDeg = 34f;
        /// <summary>9, down from 12, and the speed it is reached at 50 m/s,
        /// down from 55. A neutral-steer car at 40 m/s reaches its cornering
        /// limit at about 1 degree of road wheel; 18 degrees of lock there
        /// meant 94% of the stick was past the limit, and every adjustment
        /// was a demand for more than the tyres had — "every adjustment to
        /// steering made the car want to go sideways". Black Box cars take
        /// most of the lock away at speed for exactly this reason. Low-speed
        /// lock is untouched; hairpins keep their 34.</summary>
        public const float DefaultMaxSteerHighSpeedDeg = 9f;
        public const float DefaultSteerSpeedFalloff = 50f;
        public const float DefaultMaxSteerDriftDeg = 45f;
        public const float DefaultSteerRateDeg = 260f;
        public const float DefaultSteerRateDriftDeg = 400f;
        public const float DefaultDownforceWeightFraction = 0.70f;
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

        /// <summary>Downforce as a fraction of weight at a road speed, for a
        /// car that carries <paramref name="fractionAtVmax"/> at its top speed:
        /// the v^2 law <see cref="DeriveDownforce"/> solves. Static so the
        /// self-test can pin the FD's 30 and 50 m/s figures quoted on the
        /// field — a tuning pass that moves the fraction sees what moved.</summary>
        public static float DownforceFractionAt(float fractionAtVmax, float speedMps, float vmaxMps) =>
            fractionAtVmax * (speedMps * speedMps) / Mathf.Max(vmaxMps * vmaxMps, 1f);

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
            // The ramp the mode switch is actually read through — see DriftBlend.
            // Driven here, once, between the state machine that sets Drifting and
            // everything downstream that used to branch on it.
            {
                float slip = Mathf.Abs(chassisSlipAngle);
                if (slip > Mathf.PI * 0.5f) slip = Mathf.PI - slip;   // see UpdateDriftState
                float want = Drifting
                    ? Mathf.Clamp01((slip - DriftBlendStart) / DriftBlendSpan)
                    : 0f;
                // A GESTURE BUYS THE LOOSE CAR AT ONCE, without waiting for the
                // car to be sideways first. Ramping purely on body slip is
                // right for a slide that develops out of the tyres, and wrong
                // for the handbrake: the lever used to switch the stabilizer
                // from 3.6 to 0.6 in one tick, and blending instead made the
                // one gesture the player is MEANT to drift with feel duller
                // than it did — the opposite of what was asked for. So a live
                // e-brake window or clutch kick puts a floor under the blend,
                // decaying with the window. Not 1.0: a lever held with the car
                // pointing dead straight should not pin the stabilizer fully
                // off, or a handbrake dab on a straight is a spin again.
                float gesture = Mathf.Max(EbrakeTimer / EbrakeWindow,
                                          clutchKickTimer / ClutchKickWindow);
                want = Mathf.Max(want, Mathf.Clamp01(gesture) * DriftBlendGestureFloor);
                DriftBlend = Mathf.MoveTowards(DriftBlend, want, dt / DriftBlendTau);
            }
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
            clutchKickTimer = Mathf.Max(0f, clutchKickTimer - dt);

            float speed = Mathf.Abs(forwardSpeed);
            float steerMag = Mathf.Abs(SteerCommand);   // actuated lock, not the stick — see SteerCommand
            float massDamp = Mathf.Sqrt(1200f / Mathf.Max(800f, massKg));
            float speedRatio = Mathf.Min(1f, speed / topSpeedMps);
            float surfBoost = onRoad ? 1.0f : 1.3f;

            // --- gesture 1: handbrake press edge. The mu collapse alone slides
            // the car but has no punch; this is the punch. The steer gate is not
            // optional — without it ambient yaw noise spins a straight-line pull.
            //
            // THE EDGE IS LATCHED, because the gate now reads the ACTUATOR and
            // the actuator has not moved yet. A flick entry presses the lever
            // and the stick on the same frame; steerInput is 1 immediately but
            // steerCommandDeg is still 0 on the physics tick that follows, so a
            // gate of 0.15 fails — and `prevHandbrake` is assigned
            // unconditionally below, so the edge would be spent and never come
            // back for that pull. Held for a few ticks instead, and fired on the
            // first one where the wheels are actually turned, scaled by the lock
            // as it stands THEN. Reading steerInput here instead would undo the
            // whole point: a keyboard tap would buy a maximum kick again.
            if (handbrakeInput && !prevHandbrake && anyWheelGrounded &&
                speed > DriveGateSpeed && ebrakeCooldown <= 0f)
                ebrakePending = EbrakePendingSeconds;
            ebrakePending = Mathf.Max(0f, ebrakePending - dt);
            if (ebrakePending > 0f && handbrakeInput && anyWheelGrounded &&
                speed > DriveGateSpeed && ebrakeCooldown <= 0f && steerMag > 0.15f)
            {
                ebrakePending = 0f;
                float inputScale = steerMag * (0.3f + speedRatio * 0.7f);
                // VelocityChange ignores the inertia tensor, which is why massDamp stays.
                float dOmega = Mathf.Sign(steerCommandDeg) * EbrakeKickBase * 1.1f * massDamp *
                               surfBoost * inputScale;
                Body.AddTorque(transform.up * dOmega, ForceMode.VelocityChange);
                Body.linearVelocity *= 1f - EbrakeKickScrub * inputScale;
                ebrakeCooldown = EbrakeKickCooldown;
            }
            if (handbrakeInput && speed > DriveGateSpeed)
            {
                EbrakeTimer = EbrakeWindow;
                // The lever is a REQUEST, and this is where the throttle sustain
                // gets the seconds it later spends. Refilled on the hold rather
                // than only on the press edge, so a long pull keeps paying.
                sustainBudget = SustainBudgetSeconds;
            }
            prevHandbrake = handbrakeInput;

            // --- gesture 2: brake stab. OFF BY DEFAULT NOW, and no longer
            // allowed to arm the handbrake's window even when it is on.
            //
            // "Even using front brakes (not e-brake) while turning initiates a
            // drift. This is not fun and low skill." It did three things on the
            // rising edge of half pedal with a fifth of a turn of lock: a
            // direct yaw assignment, EbrakeTimer = 0.35 s — which is the
            // HANDBRAKE's timer, so it collapsed rear mu by a third — and,
            // through that timer, an unconditional `Drifting = true` plus the
            // injector at its e-brake multiplier with its gate dropped to 0.05.
            // One tap of the pedal mid-corner therefore bought the whole loose
            // car, and it also switched OFF the counter-steer assist, which is
            // gated on EbrakeTimer being clear. The builder already turned this
            // off for the AI with the comment "would keep tripping the
            // brake-stab drift initiator and spin it"; only the human still had
            // it. Default is now 0 for everyone.
            //
            // Left in the file, and re-gated rather than deleted, because it is
            // a real Racing Game 2 setting (physBrakeDrift) that a player may
            // want: a genuine STAB now — near-full pedal, off the throttle,
            // most of a turn of lock — and it never touches EbrakeTimer, so it
            // is a yaw nudge and not a mode change.
            bool brakeEdge = brakeInput > 0.5f && !prevBrake;
            if (brakeStabDrift > 0f && brakeEdge && anyWheelGrounded &&
                forwardSpeed > topSpeedMps * 0.15f &&
                ebrakeCooldown <= 0f && EbrakeTimer <= 0f &&
                brakeInput > 0.85f && throttleInput < 0.05f && steerMag > 0.6f)
            {
                float inputScale = steerMag * (0.4f + speedRatio * 0.6f);
                float dOmega = Mathf.Sign(steerCommandDeg) * BrakeStabBase * brakeStabDrift *
                               1.1f * massDamp * inputScale;
                Body.AddTorque(transform.up * dOmega, ForceMode.VelocityChange);
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
            //
            // Neither cap closed the latch, because the timer is what blocks
            // the drift EXIT and this re-arms it every tick. A third bound now
            // does: the sustain spends a budget that only a real gesture
            // refills, so a drift you asked for outlives the lever by a second
            // and a half and a drift nobody asked for cannot outlive anything.
            // And the measure is the REAR axle, matching UpdateDriftState —
            // sustaining on front understeer was half of the same bug.
            sustainBudget = Mathf.Max(0f, sustainBudget - dt);
            float slipNow = Mathf.Abs(rearSlipAngle);
            if (!handbrakeInput && Drifting && throttleInput > 0.3f && speed > DriveGateSpeed &&
                slipNow > DriftExitSlip && sustainBudget > 0f &&
                Mathf.Abs(chassisSlipAngle) < MaxBodySlipForSustain &&
                EbrakeTimer < ThrottleSustainWindow)
                EbrakeTimer = ThrottleSustainWindow;
        }

        /// <summary>Seconds of throttle-sustained drift still owed by the last
        /// deliberate gesture. Refilled by the handbrake and by a clutch kick,
        /// spent by the sustain, and never topped up by the drift state itself
        /// — see UpdateDriftGestures.</summary>
        float sustainBudget;
        const float SustainBudgetSeconds = 1.5f;

        void UpdateSteering(float dt)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float gripSteer = Mathf.Lerp(maxSteerLowSpeedDeg, maxSteerHighSpeedDeg,
                                         Mathf.Clamp01(speed / steerSpeedFalloff));
            // Blend the extra lock in with actual body slip rather than snapping
            // to it the instant the drift flag sets. A brake-stab entry would
            // otherwise hand the player 60 degrees of lock mid-corner.
            float slideT = Mathf.Clamp01((Mathf.Abs(chassisSlipAngle) - DriftBlendStart) / DriftBlendSpan);
            float maxSteer = Mathf.Lerp(gripSteer, maxSteerDriftDeg, Drifting ? slideT : 0f);
            // THE RATE FALLS OFF WITH SPEED TOO, not just the lock.
            //
            // The lock was already speed-sensitive and the rate was not, so at
            // 30 m/s the actuator still swept the whole useful range — the
            // front axle saturates at mu/C = 6.6 deg — in about 30 ms, under
            // the threshold at which a player can feel themselves doing it.
            // That is the "flick" half of "just tapping the turn initiates a
            // drift", and it is the specific thing that gives a Black Box car
            // its heavy, deliberate feel at speed: their wheel visibly moves
            // slower the faster you are going. Down to 55% at the top of the
            // falloff. The DRIFT rate is left alone at 400 deg/s — that is
            // counter-steer authority, and it is what makes a slide catchable.
            float rateFalloff = Mathf.Lerp(1f, SteerRateHighSpeedFrac,
                                           Mathf.Clamp01(speed / steerSpeedFalloff));
            float rate = Mathf.Lerp(steerRateDeg * rateFalloff, steerRateDriftDeg,
                                    Drifting ? slideT : 0f);
            // A pulling fault (bad alignment) biases the wheels, so holding a
            // straight line costs the player constant correction. Added to the
            // TARGET, not to steerInput, so it survives a released stick.
            float steerTarget = Mathf.Clamp(steerInput + faultSteerPull, -1f, 1f);
            steerAngleDeg = Mathf.MoveTowards(steerAngleDeg, steerTarget * maxSteer, rate * dt);
            // The same actuator, WITHOUT the fault — see SteerCommand.
            steerCommandDeg = Mathf.MoveTowards(steerCommandDeg,
                                                Mathf.Clamp(steerInput, -1f, 1f) * maxSteer, rate * dt);
            currentMaxSteerDeg = Mathf.Max(maxSteer, 1f);
        }

        /// <summary>Lock available this tick, for normalising <see cref="SteerCommand"/>.</summary>
        float currentMaxSteerDeg = 34f;
        /// <summary>The actuated wheel angle the DRIVER asked for, with
        /// faultSteerPull left out — see <see cref="SteerCommand"/>.</summary>
        float steerCommandDeg;

        /// <summary>
        /// HOW MUCH LOCK THE CAR HAS ACTUALLY GOT ON, -1 to 1 — what the drift
        /// gestures and the yaw injector read instead of <c>steerInput</c>.
        ///
        /// The keyboard drives steerInput 0 to 1 in a single frame on purpose
        /// (PlayerCarInput rate-limits the release only, so the car never feels
        /// late), and the 260 deg/s actuator limit protects only
        /// <c>steerAngleDeg</c>. Every gate in the arcade layer was reading the
        /// raw value, so one frame of a key press was FULL commitment: maximum
        /// handbrake kick, maximum injector torque — and, through
        /// CountersteerReleaseSpan, the counter-steer assist fully released, so
        /// the one thing that catches a slide contributed nothing at all
        /// whenever the player was steering. Normalised by the lock available
        /// at THIS speed, not by the low-speed maximum, or the gates would
        /// silently stop being reachable above about 30 m/s.
        ///
        /// AND WITHOUT faultSteerPull. These gates exist to detect that the
        /// DRIVER asked for something — the handbrake's own comment says the
        /// steer gate is what stops ambient yaw noise spinning a straight-line
        /// pull — and a bad-alignment fault carries up to 0.25 of lock the
        /// player never asked for. Reading the raw wheel angle would hand a
        /// mis-aligned car a handbrake kick, an active yaw injector and a
        /// "not steering neutral" verdict while the wheel sits centred, i.e.
        /// the fault would quietly buy the arcade layer. The pull still reaches
        /// the TYRES through steerAngleDeg, which is where a pull belongs.
        /// </summary>
        public float SteerCommand => Mathf.Clamp(steerCommandDeg / currentMaxSteerDeg, -1f, 1f);

        /// <summary>
        /// Revs the clutch holds the engine at on a full-pedal launch — the
        /// old hardcoded 5200. Now bounded under the upshift point: five cars
        /// in the catalog (Camaro IROC-Z '88, Charger 440 R/T '70, Corvette C1
        /// '54, Jensen Interceptor '74, Chaparral 2D) redline at 5400 or below,
        /// so 0.96 x redline is UNDER 5200 and a standing start pushed them
        /// straight past their own upshift trigger — an automatic shift to
        /// second from rest, then hunting between gears below 3.5 m/s where
        /// the clutch is still slipping. The margin keeps the launch note
        /// clearly below the shift on those cars; everything else still
        /// launches at 5200.
        /// </summary>
        public const float LaunchTargetRPM = 5200f;
        public const float LaunchUpshiftMarginRPM = 300f;

        public static float LaunchRPMFor(float idleRPM, float upshiftRPM, float pedal) =>
            Mathf.Lerp(idleRPM,
                       Mathf.Max(idleRPM, Mathf.Min(LaunchTargetRPM, upshiftRPM - LaunchUpshiftMarginRPM)),
                       Mathf.Clamp01(pedal));

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
            float launchRPM = LaunchRPMFor(idleRPM, upshiftRPM, accelPedal);
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
            int from = currentGear;
            currentGear = gear;
            shiftTimer = shiftTime * faultShiftMult;
            gearJustChangedTimer = 0.6f;
            if (up && Upshifted != null)
                Upshifted(Mathf.Clamp01((currentRPM - idleRPM) /
                                        Mathf.Max(revLimitRPM - idleRPM, 1f)));
            else if (!up) TryClutchKick(from);
        }

        /// <summary>
        /// THE CLUTCH KICK: a downshift that can break the rear loose.
        ///
        /// "Downshifting and e-brake pulls should be able to initiate drifts
        /// while turning." The e-brake always could; a downshift never could
        /// and structurally could not, because the only path from a gear change
        /// to the tyres was engine braking, and engine braking is hard-fenced
        /// at <see cref="EngineBrakeRearCircleShare"/> — 45% of the rear
        /// friction circle, which costs the rear about 11% of its lateral grip.
        /// You cannot drift a car on 11%.
        ///
        /// So the transient is modelled directly, as its own short window with
        /// its own mu collapse, rather than by loosening that fence (which
        /// would make every trailing-throttle corner loose, which is the bug
        /// this whole pass exists to remove).
        ///
        /// Four gates, and all four are the difference between a MOVE and an
        /// accident:
        ///   * OVERRUN — how far up the rev range the new gear lands the
        ///     engine. Sixth to fifth at 200 km/h is nothing; fourth to second
        ///     into a hairpin is everything. This is also what keeps the
        ///     automatic box out of it: an auto downshift fires at
        ///     downshiftRPM, which by definition puts the engine LOW.
        ///   * OFF THE THROTTLE. A downshift with the pedal down is a
        ///     power-on rev match, not a clutch drop.
        ///   * A DRIVEN REAR AXLE. A front-driver that shocks its driven wheels
        ///     understeers; it does not swing.
        ///   * LOCK ON THE WHEEL for the yaw punch (the mu collapse still
        ///     happens straight-line, which is what makes a badly-timed
        ///     downshift out of a corner cost you).
        /// </summary>
        void TryClutchKick(int fromGear)
        {
            if (Body == null || !anyWheelGrounded) return;
            // MANUAL ONLY. The doc below claims the overrun gate keeps the
            // automatic box out of this; it does not — on the reference car an
            // auto downshift at downshiftRPM produces an overrun of 0.32 to
            // 0.60, so three of five automatic downshifts would kick, including
            // on every AI car braking into a corner. The AI never set
            // manualMode, and UpdateGearbox's auto branch is already gated on
            // it, so one line excludes both. It is not a restriction on the
            // player: PlayerCarInput.ShiftBy sets manualMode true before it
            // calls ShiftTo, on the same press.
            if (!manualMode) return;
            if (throttleInput >= 0.2f) return;
            if (frontDriveShare > 0.6f) return;
            float speed = Mathf.Abs(forwardSpeed);
            if (speed <= DriveGateSpeed) return;

            float rpmAfter = KinematicRPM(speed, currentGear);
            float rpmBefore = KinematicRPM(speed, fromGear);
            // A MONEY SHIFT IS NOT THE BEST MOVE IN THE GAME. overrun clamps to
            // 1 exactly when the new gear would take the engine past the
            // limiter, and UpdateGearbox silently clamps currentRPM, so the
            // strongest kick available was a shift that should have cost an
            // engine. No kick for a downshift the gearbox cannot take.
            if (rpmAfter > revLimitRPM) return;
            // Fraction of the band the change JUMPS, measured against how much
            // band is left above the old revs. A big jump into the top of the
            // range is a shock; a small one is a gear change.
            float head = Mathf.Max(redlineRPM - rpmBefore, redlineRPM * 0.15f);
            float overrun = Mathf.Clamp01((rpmAfter - rpmBefore) / head);
            if (overrun <= ClutchKickMinOverrun) return;

            clutchKickTimer = ClutchKickWindow * overrun;

            float lock01 = Mathf.Abs(SteerCommand);
            if (lock01 > 0.25f)
            {
                // The budget is bought by a gesture that ROTATED the car, and
                // scaled by how hard: a straight-line downshift unsticks the
                // rear for half a second and buys no sustain at all.
                sustainBudget = SustainBudgetSeconds * overrun;
                float massDamp = Mathf.Sqrt(1200f / Mathf.Max(800f, massKg));
                float dOmega = Mathf.Sign(steerCommandDeg) * ClutchKickBase * overrun *
                               lock01 * massDamp;
                Body.AddTorque(transform.up * dOmega, ForceMode.VelocityChange);
            }
        }

        /// <summary>Seconds left on the clutch-kick transient — see
        /// <see cref="TryClutchKick"/>. Collapses rear mu while it runs.</summary>
        [HideInInspector] public float clutchKickTimer;
        const float ClutchKickWindow = 0.5f;
        /// <summary>Rear mu lost at the peak of a full-strength kick: 40%.
        /// Under the handbrake's 70%, because a clutch drop unsticks the rear
        /// and a locked rear axle deletes it.</summary>
        const float ClutchKickMuCollapse = 0.40f;
        const float ClutchKickBase = 0.55f;      // rad/s at full overrun and full lock
        const float ClutchKickMinOverrun = 0.25f;

        /// <summary>Today's weather, as the two grip multipliers the wheel
        /// loop reads. Refreshed once a tick — Seasons memoises by day, but
        /// four wheels a tick is still four lookups.</summary>
        float weatherRoadGrip = 1f, weatherOffroadGrip = 1f;

        void SuspensionAndLoads(float dt)
        {
            weatherRoadGrip = Seasons.RoadGripMult;
            weatherOffroadGrip = Seasons.OffroadGripMult;
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
                    // The weather's cut, on top of the surface's. Rain and
                    // snow are a day's property, not a scene's, so this is
                    // read here rather than baked into roadGrip — see Seasons.
                    wheelGrip[i] = (isRoad ? roadGrip : offroadGrip) *
                                   (isRoad ? weatherRoadGrip : weatherOffroadGrip);

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

        /// <summary>Crank drag with the throttle at <paramref name="accelPedal"/>,
        /// Nm: the idle-to-redline fraction of peak torque, faded out as the
        /// pedal goes down. Static so the self-test can pin the FD's 138 Nm at
        /// 100 km/h in third that the field comment derives.</summary>
        public static float EngineBrakeTorque(float peakNm, float rpmNorm, float accelPedal,
                                              float fracIdle, float fracRedline) =>
            peakNm * Mathf.Lerp(fracIdle, fracRedline, Mathf.Clamp01(rpmNorm)) *
            (1f - Mathf.Clamp01(accelPedal));

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
            // Engine braking, on the driven axle and gear-scaled, so downshifting
            // into a corner actually does something and lift-off rotates the car.
            float driveForce = tractionForce;
            if (accelPedal < 0.99f && currentGear >= 1 && speed > 0.5f)
            {
                float rpmNorm = Mathf.Clamp01((currentRPM - idleRPM) / Mathf.Max(redlineRPM - idleRPM, 1f));
                float tBrake = EngineBrakeTorque(engineBrakePeakNm, rpmNorm, accelPedal,
                                                 engineBrakeFracIdle, engineBrakeFracRedline);
                float fBrake = tBrake * ratio * finalDrive * drivetrainEfficiency / wheelRadius;
                // THE FENCE. rearCircleTotal is rebuilt further down this
                // function, so here it still holds last tick's circle (zero on
                // the very first tick, which is one tick of coasting). It
                // stands in for the front's circle on a front-driver too: the
                // static split is 50/50, and this is a ceiling, not a model.
                fBrake = Mathf.Min(fBrake, EngineBrakeRearCircleShare * rearCircleTotal);
                driveForce -= fBrake * Mathf.Sign(forwardSpeed);
            }

            float brakeForceTotal = brakePedal * brakeDemandG * massKg * 9.81f * faultBrakeMult;

            // Rear-mu collapse: the handbrake shrinks the rear friction circle so
            // the integrator saturates the rear first and yaw develops from the
            // tire model, not from a scripted "drift mode". Collapse mu, NOT
            // cornering stiffness — backwards makes the rear feel numb, not loose.
            // Two collapses, and the DEEPER one wins rather than the two
            // multiplying: a handbrake pull during a clutch kick is still a
            // handbrake pull, not a rear axle with 18% of its grip left.
            float rearMuMult = EbrakeTimer > 0f
                ? 1f - EbrakeMuCollapse * Mathf.Min(1f, EbrakeTimer / EbrakeWindow)
                : 1f;
            if (clutchKickTimer > 0f)
                rearMuMult = Mathf.Min(rearMuMult,
                    1f - ClutchKickMuCollapse * Mathf.Min(1f, clutchKickTimer / ClutchKickWindow));

            // THE PARKING BRAKE. "When I park my car it tends to roll away. It
            // should park in gear and/or with e-brake."
            //
            // It rolled away because NOTHING in this model held a stationary
            // car. brakeForceTotal is the PEDAL only, so the handbrake never
            // reached the low-speed hold below; the handbrake's own branch is
            // gated on speed > 0.3, so a parked car got no lever at all until
            // it was already moving, and then a full rear lock stopped it dead
            // — and released it again the moment it dropped back under 0.3.
            // A car left on the neighbourhood's 15% drive ratcheted down it a
            // third of a metre at a time. Laterally it was worse: fLat is faded
            // to ZERO below 0.6 m/s to stop solver jitter, so a parked car had
            // no sideways grip whatever and slid off any camber it was left on.
            //
            // A DAMPER CANNOT FIX THIS, which is why the existing low-speed
            // hold does not: it opposes VELOCITY, so on a slope it settles at
            // whatever speed makes its force equal gravity's — 3 cm/s with the
            // pedal buried, 11 cm/s on the 0.3 the game applies when it takes
            // the controls away. Non-zero by construction. Static friction does
            // not work like that. It opposes the LOAD, and a parked tyre's load
            // is the component of gravity that runs along the car.
            //
            // So: cancel that component outright, up to the grip the tyre
            // actually has, and keep a stiff damper on top of it for solver
            // residue. Above the tyres' grip it still slides — a car parked on
            // ice, or across a 1-in-1 bank, should.
            //
            // NOT while the throttle is open: a standing burnout is throttle
            // plus handbrake at zero road speed, and a hold that fought it
            // would make the smokiest thing a car can do the quietest. Which is
            // the same argument the scrub model makes twenty lines down.
            //
            // GATED ON THE WHOLE BODY, not on forwardSpeed, and this is the one
            // place in the solver where that distinction is not pedantry.
            // `speed` is Abs(forwardSpeed) — the component along the car's nose
            // — so a car travelling SIDEWAYS reads as stationary by it. Every
            // 0.3 m/s gate in this function shares that blind spot, which is
            // half of why a car left on a cambered street drifted off it. But
            // it cuts both ways: a car properly sideways mid-drift also reads
            // near zero, and seizing THAT would clamp full lateral grip onto
            // the exact moment the whole drift layer exists to allow. The
            // angular gate is the second half of the same guard — a car
            // spinning on the spot is not parked either.
            bool atRest = Body.linearVelocity.magnitude <= ParkHoldSpeed &&
                          Body.angularVelocity.magnitude <= ParkHoldSpin;
            // AND IT LATCHES ON ITS OWN once the car has been still for a
            // moment, which is the whole of "car still rolls away when
            // parked". The hold only ever engaged while a pedal or the lever
            // was HELD, so the instant the player let go of both — which is
            // what parking is — nothing was holding the car and any gradient
            // took it. And it could not re-engage afterwards: once the roll
            // passes ParkHoldSpeed, atRest is false and the test can never
            // become true again.
            //
            // Cheap to get right because the timer runs while braking too, so
            // a car brought to a stop on the pedal has already earned the
            // latch by the time the pedal comes up, and the hold is continuous
            // with no moment of freewheel in between. Any throttle drops it on
            // the same tick — accelPedal gates the whole expression — so
            // pulling away is unaffected, and a driver who wants to roll a
            // slope just touches the pedal.
            if (atRest && accelPedal < 0.02f) parkRestTimer += dt;
            else parkRestTimer = 0f;
            bool parkHold = atRest && accelPedal < 0.02f &&
                            (handbrakeInput || brakePedal > 0.5f ||
                             parkRestTimer >= ParkAutoSeconds);
            int groundedAll = 0;
            for (int i = 0; i < 4; i++) if (wheelGrounded[i]) groundedAll++;
            // Shared over the wheels holding the car up, so a car with a wheel
            // in the air is held by the three that are down rather than by a
            // quarter of itself four times.
            //
            // The load each one carries is resolved in ITS OWN axes, down in
            // the loop, not in the chassis's. A tyre's force can only ever be
            // applied along wheelForward and wheelRight, and on a car left with
            // lock on those are up to 34 degrees round from the body's — so a
            // hold computed against transform.forward and then applied along
            // the steered axis is the right magnitude pointing the wrong way,
            // and the part that misses is a sideways shove the damper then has
            // to argue with. The two axes are orthogonal and both lie in the
            // ground plane, so a dot product each captures the whole of the
            // in-plane load with nothing left over.
            float parkShareKg = groundedAll > 0 ? massKg / groundedAll : 0f;
            Parked = parkHold;

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
                    // LOAD-SENSITIVE, which is what a proportioning valve is
                    // for and why trail-braking used to spin the car.
                    //
                    // The split was a fixed fraction of pedal force with no
                    // reference to what the axle was carrying, while weight
                    // transfer under 0.9 g takes about a third of the load off
                    // the rear. So at full pedal a rear wheel spent ~85% of its
                    // shrunken friction circle on stopping and a front wheel
                    // ~64% of its grown one — and a wheel at its longitudinal
                    // cap has EXACTLY zero lateral grip left, with no
                    // transition. Braking into a corner therefore removed the
                    // rear's cornering force first, every time. "Even using
                    // front brakes (not e-brake) while turning initiates a
                    // drift."
                    //
                    // Half the bias now follows the live axle loads, so the
                    // rear demand falls as the nose dives. Half, not all,
                    // because the fixed number is the car's brake hardware and
                    // the player can still dial it in the garage.
                    float fixedShare = front ? brakeFrontShare : (1f - brakeFrontShare);
                    float totalLoad = frontAxleLoad + rearAxleLoad;
                    // Clamped, because wheelLoad is a raw spring+damper
                    // reading and an axle with one wheel in the air reports
                    // half of what it is carrying. A proportioning valve
                    // shifts the bias; it does not hand one axle everything.
                    float loadShareAxle = totalLoad > 1f
                        ? Mathf.Clamp((front ? frontAxleLoad : rearAxleLoad) / totalLoad, 0.4f, 0.75f)
                        : 0.5f;
                    float share = Mathf.Lerp(fixedShare, loadShareAxle, BrakeLoadSensitivity);
                    int brakeWheels = Mathf.Max(1, front ? groundedFront : groundedRear);
                    brakeDemand = brakeForceTotal * share / brakeWheels;
                    fLong -= Mathf.Sign(vLong) * Mathf.Min(brakeDemand, longCap);
                }
                // THE BRAKES NEVER LOCK A WHEEL, and this is the whole of
                // "every tap on the brake made the car want to go sideways"
                // coming down Beech Gap.
                //
                // Engine braking lands in fLong first (up to
                // EngineBrakeRearCircleShare of the circle, on the driven
                // axle), and the pedal is then SUBTRACTED on top of it with no
                // second look at the circle -- so the two together could ask a
                // rear wheel for more longitudinal force than it has, and
                // latCap = sqrt(circle^2 - fLong^2) below then came out at
                // exactly ZERO. Not reduced: zero. On a 7% descent in gear the
                // rear axle is carrying engine braking the whole way down, so
                // a firm pedal there removed every newton of the rear's
                // cornering force in one tick, while the front -- no engine
                // braking, more load -- kept most of its own. That is a car
                // that oversteers on every brake application, and it is worse
                // the faster and the steeper the road.
                //
                // A real car has ABS; a Black Box car has perfect ABS. So the
                // TOTAL braking demand on a wheel is held under a share of its
                // circle, lower on the rear than the front, which leaves the
                // rear more cornering force than the front under hard braking
                // -- hard braking into a corner now understeers, which is the
                // stable, catchable, NFS answer. The share is on the total, so
                // engine braking spends the same budget the pedal does rather
                // than a budget of its own.
                //
                // On braking only. Traction is left alone: wheelspin is the
                // yaw injector's input and the burnout is the smokiest thing
                // in the game on purpose.
                {
                    bool braking = Mathf.Sign(fLong) != Mathf.Sign(vLong) && Mathf.Abs(vLong) > 0.3f;
                    float lockShare = front ? FrontBrakeLockShare : RearBrakeLockShare;
                    if (braking && Mathf.Abs(fLong) > longCap * lockShare)
                        fLong = -Mathf.Sign(vLong) * longCap * lockShare;
                }
                // PARKED: this tyre holds its share of the car, full stop. It
                // REPLACES the demand rather than adding to it — a parked wheel
                // is not braking against a drive torque, it is standing still,
                // and at this speed with the throttle shut there is nothing in
                // fLong to preserve anyway (tractive effort is zero below 0.02
                // pedal and engine braking is gated at 0.5 m/s).
                if (parkHold)
                    fLong = Mathf.Clamp(
                        -Vector3.Dot(Physics.gravity, wheelForward) * parkShareKg
                        - vLong * ParkHoldDamp * parkShareKg, -longCap, longCap);
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
                // EXCEPT WHEN PARKED, where that fade is the bug: a stationary
                // car has no slip angle, so TireCurve gives it nothing to fade
                // in the first place, and the multiply then takes away the last
                // of it. A parked tyre on a cambered street holds sideways for
                // the same reason it holds fore-and-aft, and by the same
                // arithmetic — the load, not the velocity.
                if (parkHold)
                    fLat = Mathf.Clamp(
                        -Vector3.Dot(Physics.gravity, wheelRight) * parkShareKg
                        - vLat * ParkHoldDamp * parkShareKg, -latCap, latCap);

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
            // A DRIFT IS A REAR-AXLE EVENT, and measuring it as
            // max(front, rear) is why turning the wheel drifted the car.
            //
            // Front slip is computed in the STEERED wheel frame, so for the
            // first tenth of a second after a steering input the contact
            // velocity still points down the old heading and the front slip
            // angle is essentially the road-wheel angle itself. Lock is 34 deg
            // at rest falling to 12 at 55 m/s — above the 18.3 deg entry
            // threshold everywhere below about 40 m/s — so a hard turn-in at
            // any ordinary speed tripped the drift flag off the FRONT axle
            // while the rear was still planted. That flag is a mode switch:
            // the lateral stabilizer, the yaw damper, the injector and the
            // steering lock all change with it, so understeer was being
            // answered with the loose-car settings. Reported as "just tapping
            // the turn initiates a drift".
            //
            // Two measures now, and both have to agree: the REAR tyres are
            // past their peak, AND the whole car is pointing away from where
            // it is going. The second is what separates a rear axle working
            // hard in a fast corner from a car that is actually sideways.
            float rearSlip = Mathf.Abs(rearSlipAngle);
            // MEASURED OFF THE NOSE, and REVERSING IS NOT A DRIFT.
            //
            // chassisSlipAngle is the signed angle between where the car is
            // going and where it points, so a car in reverse reads pi — which
            // sails past the entry threshold AND can never satisfy the exit
            // one. Backing out of a parking space in an arc would have set the
            // drift state and latched it there for the whole manoeuvre, with
            // the stabilizer faded off and the yaw damper halved. Reversing is
            // its own thing: the slip measure is folded about 90 degrees so a
            // car travelling backwards reads as pointing STRAIGHT backwards
            // rather than as maximally sideways, and the drift layer simply
            // does not apply to it.
            float bodySlip = Mathf.Abs(chassisSlipAngle);
            if (bodySlip > Mathf.PI * 0.5f) bodySlip = Mathf.PI - bodySlip;
            bool ebrakeActive = EbrakeTimer > 0f;

            if (Mathf.Abs(forwardSpeed) < DriftStopSpeed && vel.magnitude < DriftStopSpeed)
            {
                Drifting = false;      // deliberately does not clear postDriftTimer
            }
            else if (Drifting)
            {
                // A GESTURE WINDOW HOLDS THE STATE, both of them. Without the
                // clutch-kick term this exits on the very next tick whenever
                // the kick has not yet loosened the rear, the entry branch
                // sets it again the tick after, and the flag chatters at 25 Hz
                // — refreshing postDriftTimer the whole time, so a kick that
                // did not take locks the player out of a drift for half a
                // second afterwards. Exactly the feature they asked for,
                // failing quietly.
                if (rearSlip < DriftExitSlip && bodySlip < DriftExitBodySlip &&
                    !ebrakeActive && clutchKickTimer <= 0f)
                {
                    Drifting = false;
                    postDriftTimer = PostDriftLockout;
                }
            }
            else
            {
                // A gesture is a request: the handbrake's window, and a clutch
                // kick's. On the ENTRY branch only — folding the kick into
                // ebrakeActive would also block the EXIT for its whole window,
                // which is the latch this pass exists to remove.
                if (ebrakeActive || clutchKickTimer > 0f) Drifting = true;
                else if (rearSlip > DriftEnterSlip && bodySlip > DriftEnterBodySlip &&
                         postDriftTimer <= 0f) Drifting = true;
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

            float k = Mathf.Lerp(lateralDampGrip, lateralDampDrift, DriftBlend);
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
            float steerMag = Mathf.Abs(SteerCommand);   // actuated lock, not the stick — see SteerCommand
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
                float mult = Mathf.Lerp(InjectorGrip,
                                        EbrakeTimer > 0f ? InjectorEbrake : InjectorDrift,
                                        DriftBlend);
                float surfMult = onRoad ? 1.0f : InjectorOffroadMult;
                float arm = wheelbase * weightDistFront;
                float torque = Mathf.Sign(steerCommandDeg) * steerMag * wheelspinRatio *
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
                                   Mathf.Sign(steerCommandDeg) != Mathf.Sign(yawRate);

            if (Drifting)
            {
                float loose;
                if (driverIdle) loose = YawDampIdle;
                else if (steerNeutral) loose = Mathf.Lerp(YawDampNeutralMin, YawDampNeutralMax, slipT);
                else if (counterSteering) loose = YawDampCounter;
                else loose = YawDampCommitted;   // committed slide still feels loose
                // Faded in with how sideways the car is, not switched on with
                // the flag — see DriftBlend.
                yawDamp = Mathf.Lerp(yawDampGrip, loose, DriftBlend);
            }
            else yawDamp = yawDampGrip;

            // Damp only the yaw component, in the body frame — leave roll/pitch alone.
            Vector3 w = Body.angularVelocity;
            float newYaw = yawRate * Mathf.Max(0f, 1f - yawDamp * dt);
            Body.angularVelocity = w + transform.up * (newYaw - yawRate);

            // --- counter-steer assist: never catches a deliberate slide, and
            // backs off as the player steers, so held-opposite-lock is untouched.
            if (countersteerAssist > 0f && EbrakeTimer <= 0f && clutchKickTimer <= 0f && !handbrakeInput &&
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

        /// <summary>
        /// SEAT A RESET ON THE SURFACE rather than trusting the Y it was
        /// handed, which is the whole of "the car flies 15 ft off the ground
        /// going to a new area".
        ///
        /// Two callers were handing over a Y that was never the road, and they
        /// produced the same symptom by different routes. TownReturn stores
        /// <c>car.transform.position</c> when you step into a venue and hands
        /// it straight back on the way out, so the 0.3 m its caller adds and
        /// the ResetLift added here were BAKED INTO THE STORED POSITION and
        /// paid again on the next visit: 0.7 m compounding per trip, which is
        /// fifteen feet after ten of them. TownEdge.ArrivalSpot is worse in one
        /// go — it derives the arrival point from its trigger volume's BOUNDS
        /// CENTRE, and a zone line tall enough to catch a car is metres deep,
        /// so its centre is metres above the tarmac.
        ///
        /// Fixing it here rather than at the two call sites is deliberate:
        /// "put the car back" means on the ground in every caller, and the
        /// next one to pass a hopeful Y should not have to know that. A miss
        /// keeps the Y it was given, which is what every caller got before.
        /// </summary>
        static Vector3 GroundedResetPos(Vector3 position)
        {
            const float ProbeUp = 6f, ProbeDown = 80f;
            if (Physics.Raycast(position + Vector3.up * ProbeUp, Vector3.down,
                                out var hit, ProbeUp + ProbeDown,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return new Vector3(position.x, hit.point.y + ResetLift, position.z);
            return position + Vector3.up * ResetLift;
        }

        /// <summary>
        /// Put the car EXACTLY here, standing still — no ground probe, no ride-
        /// height lift, none of <see cref="ResetTo"/>'s driveline reset. The
        /// staging teleport, as opposed to the recovery one.
        ///
        /// IT GOES THROUGH THE RIGIDBODY, and that is the whole reason it
        /// exists. The builder interpolates the body of the car the camera
        /// follows and leaves the three AI at None, so the player's is the only
        /// INTERPOLATED body on the grid — and an interpolated body owns its
        /// transform: every frame it writes a pose derived from its own
        /// internal one, so a plain transform.position written before the first
        /// physics step is simply painted over with the pose the scene was
        /// baked at. Three cars moved and the fourth did not.
        ///
        /// That is what stranded the player on the FORWARD grid of a reversed
        /// venue: on Beech Gap II the field lined up 6.3 km away at the top of
        /// the mountain while the car sat at the bottom, facing back down the
        /// road it was supposed to be climbing — which the wrong-way watchdog,
        /// correctly, called out. Every symptom of that bug was one car in the
        /// field behaving differently from the other three for a reason that
        /// has nothing to do with racing.
        ///
        /// Interpolation is toggled OFF and back ON around the write because
        /// that is what drops the pose history: without it the body still
        /// smears from where it was to where it now is, over the first frames
        /// of the countdown.
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (Body == null) return;
            var interp = Body.interpolation;
            Body.interpolation = RigidbodyInterpolation.None;
            Body.position = position;
            Body.rotation = rotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.interpolation = interp;
        }

        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            // A respawn is not a crash. PizzaCargo drives itself off the
            // velocity difference between two physics ticks, so a teleport that
            // zeroes the body reads to it as the hardest braking it can
            // represent and throws the order across the seat — for a stop the
            // player did not make. See PizzaCargo.ForgetMotion.
            PizzaCargo.Instance?.ForgetMotion();
            // Through the teleport, not straight onto the transform: the
            // player's body is interpolated, and see TeleportTo for what that
            // does to a pose written from outside the physics step.
            TeleportTo(GroundedResetPos(position), rotation);
            currentGear = 1;
            currentRPM = idleRPM;
            wheelSpin = 0f;
            wheelspinRatio = 0f;
            EbrakeTimer = 0f;
            clutchKickTimer = 0f;
            ebrakePending = 0f;
            sustainBudget = 0f;
            DriftBlend = 0f;
            Drifting = false;
            postDriftTimer = 0f;
            impactGraceTimer = 0f;
            ImpactGrace01 = 0f;
        }

        /// <summary>
        /// Engine speed for a road speed in a gear, with the clutch locked —
        /// the same arithmetic UpdateGearbox runs every tick, exposed so a
        /// rolling start can pick its gear from it before the first tick
        /// rather than after the first tick has already picked for it.
        /// </summary>
        public float KinematicRPM(float mps, int gear)
        {
            if (gearRatios == null || gearRatios.Length == 0) return idleRPM;
            float wheelRPM = Mathf.Abs(mps) / (2f * Mathf.PI * wheelRadius) * 60f;
            float ratio = gearRatios[Mathf.Clamp(gear, 1, gearRatios.Length) - 1];
            return wheelRPM * Mathf.Abs(ratio) * finalDrive;
        }

        /// <summary>
        /// Headroom under the upshift point a rolling start's gear is chosen
        /// with, as a fraction of upshiftRPM. The box shifts up on the first
        /// tick currentRPM crosses upshiftRPM, and the RPM slews at 12,000
        /// rpm/s, so a gear picked within a few hundred rpm of the point
        /// upshifts on the driver's first squeeze of throttle — a car that
        /// changes gear before it has done anything. A tenth is ~720 rpm on
        /// the RX-7, 0.06 s of slew. Starting value for tuning.
        /// </summary>
        public const float RollingGearHeadroom = 0.9f;

        /// <summary>
        /// The gear a car already doing <paramref name="mps"/> should be in:
        /// the LOWEST gear whose kinematic RPM sits under the upshift point
        /// with <see cref="RollingGearHeadroom"/> to spare, so the engine is
        /// in the meat of its band rather than lugging at the downshift
        /// threshold in the tallest gear that will hold the speed. Top gear if
        /// none will, first if the car is stopped. Pure — reads the ratios and
        /// the shift points and nothing else — so the self-test can ask it
        /// about the built-in RX-7 and about catalog cars without a scene.
        ///
        /// On the built-in ratios: 45 km/h is first at ~5500 rpm, 72 km/h is
        /// second at ~5100 (first would be 8800, over the limiter).
        /// </summary>
        public int GearForSpeed(float mps)
        {
            if (gearRatios == null || gearRatios.Length == 0) return 1;
            float ceiling = upshiftRPM * RollingGearHeadroom;
            for (int g = 1; g <= gearRatios.Length; g++)
                if (KinematicRPM(mps, g) <= ceiling) return g;
            return gearRatios.Length;
        }

        /// <summary>
        /// Put the car on the road ALREADY MOVING, where it stands and the way
        /// it faces: a delivery arrives at the venue's speed limit rather than
        /// turning the key on the grid. The mirror of <see cref="ResetTo"/>
        /// for a car that is meant to be going.
        ///
        /// Everything the drivetrain remembers is written to agree with the
        /// speed — gear from <see cref="GearForSpeed"/>, RPM from the gear —
        /// because ResetTo's "gear 1, idle" on a car doing 72 km/h is 8,800
        /// rpm on the first tick, over the limiter until the slew and the
        /// auto-upshift catch it. Called from RaceManager.Start, AFTER the
        /// handoff has restaged the grid (which zeroes velocity and reseats
        /// the car, so the forward vector has to be read after it) and BEFORE
        /// the first FixedUpdate (so PizzaCargo seeds its velocity memory on
        /// a car that is already moving instead of reading the step as a
        /// crash — ForgetMotion below is belt and braces for the same thing).
        /// </summary>
        public void SetRolling(float mps)
        {
            if (Body != null)
            {
                Body.linearVelocity = transform.forward * mps;
                Body.angularVelocity = Vector3.zero;
            }
            PizzaCargo.Instance?.ForgetMotion();
            currentGear = GearForSpeed(mps);
            currentRPM = Mathf.Clamp(KinematicRPM(mps, currentGear), idleRPM, revLimitRPM);
            wheelSpin = 0f;
            wheelspinRatio = 0f;
            EbrakeTimer = 0f;
            clutchKickTimer = 0f;
            ebrakePending = 0f;
            sustainBudget = 0f;
            DriftBlend = 0f;
            Drifting = false;
            postDriftTimer = 0f;
            impactGraceTimer = 0f;
            ImpactGrace01 = 0f;
        }
    }
}
