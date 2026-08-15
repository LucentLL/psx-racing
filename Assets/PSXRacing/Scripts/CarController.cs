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

        static readonly float[] TorqueNm =
        { 147f, 201f, 253f, 280f, 295f, 307f, 309f, 314f, 313f, 313f, 312f, 303f, 274f, 244f, 206f };
        const float TorqueCurveStartRPM = 1000f;
        const float TorqueCurveStepRPM = 500f;

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
        public float downforceCoefficient = 0.35f;
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

        [Header("Brakes")]
        public float brakeDemandG = 0.9f;
        public float brakeFrontShare = 0.6f;

        /// <summary>Layer holding the drivable road surface. Checked by layer
        /// rather than by collider name: reading Collider.name allocates a
        /// managed string on every wheel of every car on every physics tick.</summary>
        public int roadLayer = 8;

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
        /// <summary>True while the ECU is actually cutting fuel. Audio gates the
        /// on-the-limiter recordings on this rather than on RPM position, because
        /// RPM alone cannot tell "deep in the red" from "bouncing off the cut".</summary>
        public bool RevLimiterActive { get; private set; }
        public Rigidbody Body { get; private set; }

        public Transform[] wheelHubs = new Transform[4];
        public Transform[] wheelMeshes = new Transform[4];

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
        float staticWheelLoad = 3139f;

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
            Body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // Unity derives the tensor from the box collider, landing on the
            // textbook slab value. Real cars centralize mass, so yaw is scaled
            // down; pitch and roll stay at slab.
            float lng = 4.28f, wid = 1.76f, hgt = 1.23f;
            float slabYaw = massKg * (lng * lng + wid * wid) / 12f;
            float slabPitch = massKg * (lng * lng + hgt * hgt) / 12f;
            float slabRoll = massKg * (wid * wid + hgt * hgt) / 12f;
            Body.automaticInertiaTensor = false;
            Body.inertiaTensor = new Vector3(slabPitch, slabYaw * yawInertiaScale, slabRoll);
            Body.inertiaTensorRotation = Quaternion.identity;

            staticWheelLoad = massKg * 9.81f * 0.25f;

            float halfTrack = trackWidth * 0.5f;
            float halfBase = wheelbase * 0.5f;
            wheelLocalPos = new[]
            {
                new Vector3(-halfTrack, mountHeight,  halfBase),
                new Vector3( halfTrack, mountHeight,  halfBase),
                new Vector3(-halfTrack, mountHeight, -halfBase),
                new Vector3( halfTrack, mountHeight, -halfBase),
            };
            currentRPM = idleRPM;
        }

        public float GetTorqueAtRPM(float rpm)
        {
            float t = (rpm - TorqueCurveStartRPM) / TorqueCurveStepRPM;
            if (t <= 0f) return TorqueNm[0] * Mathf.InverseLerp(0f, TorqueCurveStartRPM, rpm);
            int i = Mathf.FloorToInt(t);
            if (i >= TorqueNm.Length - 1) return TorqueNm[TorqueNm.Length - 1];
            return Mathf.Lerp(TorqueNm[i], TorqueNm[i + 1], t - i);
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

            UpdateChassisSlip(vel);
            UpdateDriftGestures(dt);      // runs first, so the frame sees the kick
            UpdateSteering(dt);
            UpdateGearbox(dt);
            SuspensionAndLoads(dt);
            TireForces(dt);
            UpdateDriftState(vel);
            ApplyLateralStabilizer();
            ApplyYawLayer(dt);
            AeroForces();
            UpdateWheelVisuals(dt);
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
            steerAngleDeg = Mathf.MoveTowards(steerAngleDeg, steerInput * maxSteer, rate * dt);
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
            shiftTimer = shiftTime;
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
                wheelGrounded[i] = Physics.Raycast(mount, -transform.up, out RaycastHit hit, rayLength);

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
                tractionForce = torque * ratio * finalDrive * drivetrainEfficiency / wheelRadius;
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

            float brakeForceTotal = brakePedal * brakeDemandG * massKg * 9.81f;

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
            float totalDriveCap = 0f;
            rearCircleTotal = 0f;
            float frontSlipSum = 0f, rearSlipSum = 0f;
            int frontCount = 0, rearCount = 0;

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
                float mu = wheelGrip[i] * gripBonus * (front ? tireMuFront : tireMuRear);
                if (!front) mu *= rearMuMult;
                float circle = mu * Fz;
                if (!front)
                {
                    rearCircleTotal += circle;
                    totalDriveCap += circle;   // FULL circle: using the combined-slip
                                               // reduced cap inflates the ratio during
                                               // a slide and the yaw injector runs away.
                }

                float fLong = 0f;
                float longCap = circle * CombinedSlipFactor(Mathf.Abs(slip));

                if (!front) fLong = Mathf.Clamp(driveForce * 0.5f, -longCap, longCap);
                if (brakeForceTotal > 0f && speed > 0.3f)
                {
                    float share = front ? brakeFrontShare : (1f - brakeFrontShare);
                    float braking = Mathf.Min(brakeForceTotal * share * 0.5f, longCap);
                    fLong -= Mathf.Sign(vLong) * braking;
                }
                if (!front && handbrakeInput && speed > 0.3f)
                    fLong = -Mathf.Sign(vLong) * Mathf.Min(circle * 0.9f, Mathf.Abs(fLong) + circle * 0.6f);

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

            wheelspinRatio = totalDriveCap > 1f
                ? Mathf.Min(2f, Mathf.Max(0f, totalDriveDemand - totalDriveCap) / totalDriveCap)
                : 0f;
            wheelSpin = Mathf.MoveTowards(wheelSpin, Mathf.Clamp01(wheelspinRatio), dt * 4f);
        }

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
            if (Mathf.Abs(vLat) < 0.05f) return;

            float k = Drifting ? lateralDampDrift : lateralDampGrip;
            // Fade in with speed so low-speed manoeuvring is not rail-roaded.
            float speedFade = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 3f);
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
            float steerGate = EbrakeTimer > 0f ? 0.05f : 0.35f;
            // Fade the injector in with speed. First gear makes ~12.7 kN against
            // ~6.8 kN of rear grip, so wheelspinRatio is near 1 the moment you
            // touch the throttle from rest — ungated, that alone rotates a
            // stationary car, which reads as pivoting about the rear axle.
            float injectorFade = Mathf.Clamp01(
                (Mathf.Abs(forwardSpeed) - yawInjectorMinSpeed) /
                Mathf.Max(yawInjectorFullSpeed - yawInjectorMinSpeed, 0.01f));
            if (wheelspinRatio > 0f && injectorFade > 0f && steerMag > steerGate &&
                postDriftTimer <= 0f && anyWheelGrounded)
            {
                // 0.20 subtle corner-exit / 1.5 sustains a slide / 2.0 committed entry
                float mult = EbrakeTimer > 0f ? 2.0f : (Drifting ? 1.5f : 0.20f);
                float surfMult = onRoad ? 1.0f : 0.6f;
                float arm = wheelbase * weightDistFront;
                float torque = Mathf.Sign(steerInput) * steerMag * wheelspinRatio *
                               arm * rearCircleTotal * 0.8f * mult * surfMult *
                               wheelspinYawGain * injectorFade;
                Body.AddTorque(transform.up * torque, ForceMode.Force);
            }

            // --- four-tier yaw damping: the 16.7x spread between 0.15 and 2.5
            // is the entire "weightless slide that still ends cleanly" character.
            bool steerNeutral = steerMag < 0.10f;
            bool driverIdle = steerMag < 0.05f && throttleInput < 0.05f && !handbrakeInput;
            float slipT = Mathf.Clamp01((Mathf.Abs(chassisSlipAngle) - 0.6f) / 0.6f);
            bool counterSteering = Drifting && steerMag > 0.4f && Mathf.Abs(yawRate) > 0.3f &&
                                   Mathf.Sign(steerInput) != Mathf.Sign(yawRate);

            // Roughly 2.5x the 2D source's numbers across the board. That game
            // had a synthetic heading integrator holding the car straight; a
            // Rigidbody has nothing equivalent, so the same values leave the car
            // rotating freely long after the driver has stopped asking for it.
            if (Drifting)
            {
                if (driverIdle) yawDamp = 1.8f;
                else if (steerNeutral) yawDamp = Mathf.Lerp(2.2f, 4.0f, slipT);
                else if (counterSteering) yawDamp = 2.2f;
                else yawDamp = 0.45f;      // committed slide still feels loose
            }
            else yawDamp = 1.6f;

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

        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position + Vector3.up * 0.4f, rotation);
            currentGear = 1;
            currentRPM = idleRPM;
            wheelSpin = 0f;
            wheelspinRatio = 0f;
            EbrakeTimer = 0f;
            Drifting = false;
            postDriftTimer = 0f;
        }
    }
}
