using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The handful of numbers every adjustable range is derived from.
    ///
    /// It exists because the two sides of the fence need the same ranges and
    /// cannot get them the same way: the RACE side has a live CarController that
    /// has already run ApplySpec, and the GARAGE side is two scene loads away
    /// from any CarController at all and has only a CarSpec and a stage count.
    /// Both fill this struct, and everything downstream reads only this — so
    /// there is exactly one derivation of "what does a stiffer spring mean on
    /// this car", which is the fence <see cref="CarTune"/> opens by drawing.
    ///
    /// <see cref="FromSpec"/> reproduces what ApplySpec computes. That is a
    /// duplication and it is the deliberate kind: the alternative is booting a
    /// physics object inside a menu. LifeSimSelfTest sweeps the whole catalog
    /// and asserts the two agree field by field, so the duplication cannot rot
    /// silently.
    /// </summary>
    public struct CarSetupBasis
    {
        public float massKg, staticWheelLoad, wheelRadius;
        public float brakeDemandG, brakeFrontShare;
        public float tireMuFront, tireMuRear, corneringStiffness;
        /// <summary>Cornering stiffness before the 13.0 ceiling. The pressure
        /// term multiplies this and clamps once, so a car whose suspension stage
        /// already saturates the cap still gets something for its pressures.
        /// </summary>
        public float rawCorneringStiffness;
        public float maxSteerLowSpeedDeg, maxSteerHighSpeedDeg, maxSteerDriftDeg;
        public float steerRateDeg, steerRateDriftDeg, steerReleaseRate;
        public float springRateFront, springRateRear;
        public float damperFront, damperRear;
        public float antiRollFront, antiRollRear;
        public float restLength, cgHeight;
        public float frontDriveShare, downforceWeightFractionAtVmax, downforceBalanceFront;
        public float finalDrive, topSpeedMps, redlineRPM, drivetrainEfficiency;
        public float[] gearRatios;
        /// <summary>Tractive effort in first gear at 60% of the rev range, N.
        /// The differential preload range is scaled off it, so a preload setting
        /// means the same fraction of "what this car can actually put down" on a
        /// kei car and on a Group C car.</summary>
        public float firstGearForceN;
        public bool fourWheelDrive, welded;

        public int GearCount => gearRatios != null ? gearRatios.Length : 0;

        /// <summary>Read a car that has already been spec'd. Takes the values as
        /// they stand, so the caller must hand over the BASELINE snapshot rather
        /// than a car with a setup already applied — see
        /// CarController.CaptureSetupBaseline.</summary>
        public static CarSetupBasis FromController(CarController c)
        {
            var b = new CarSetupBasis();
            if (c == null) return b;
            b.massKg = c.massKg;
            b.staticWheelLoad = c.massKg * 9.81f * 0.25f;
            b.wheelRadius = c.wheelRadius;
            b.brakeDemandG = c.brakeDemandG;
            b.brakeFrontShare = c.brakeFrontShare;
            b.tireMuFront = c.tireMuFront;
            b.tireMuRear = c.tireMuRear;
            b.corneringStiffness = c.corneringStiffness;
            b.rawCorneringStiffness = c.rawCorneringStiffness;
            b.maxSteerLowSpeedDeg = c.maxSteerLowSpeedDeg;
            b.maxSteerHighSpeedDeg = c.maxSteerHighSpeedDeg;
            b.maxSteerDriftDeg = c.maxSteerDriftDeg;
            b.steerRateDeg = c.steerRateDeg;
            b.steerRateDriftDeg = c.steerRateDriftDeg;
            b.steerReleaseRate = PlayerCarInput.DefaultSteerReleaseRate;
            b.springRateFront = c.springRateFront;
            b.springRateRear = c.springRateRear;
            b.damperFront = c.damperFront;
            b.damperRear = c.damperRear;
            b.antiRollFront = c.antiRollFront;
            b.antiRollRear = c.antiRollRear;
            b.restLength = c.restLength;
            b.cgHeight = c.cgHeight;
            b.frontDriveShare = c.frontDriveShare;
            b.downforceWeightFractionAtVmax = c.downforceWeightFractionAtVmax;
            b.downforceBalanceFront = c.downforceBalanceFront;
            b.finalDrive = c.finalDrive;
            b.topSpeedMps = c.topSpeedMps;
            b.redlineRPM = c.redlineRPM;
            b.drivetrainEfficiency = c.drivetrainEfficiency;
            b.gearRatios = c.gearRatios;
            // StockTorqueAtRPM, not GetTorqueAtRPM: the blown figure would move
            // where the preload slider's ends sit, and the garage — which has no
            // way to know a blower is fitted at the moment it draws the row —
            // would quote a different range from the one the race applies. The
            // two sides of this fence have to compute the same number.
            b.firstGearForceN = FirstGearForce(
                c.StockTorqueAtRPM(0.6f * c.redlineRPM), c.gearRatios,
                c.finalDrive, c.drivetrainEfficiency, c.wheelRadius);
            b.fourWheelDrive = c.frontDriveShare > 0.01f && c.frontDriveShare < 0.99f;
            b.welded = c.weldedDiff;
            return b;
        }

        /// <summary>Tractive effort in first, N. Internal because
        /// <see cref="CarController"/>'s baseline has to recompute it against
        /// the SNAPSHOT gearbox rather than the tuned one.</summary>
        internal static float FirstGearForceFor(float torqueNm, float[] ratios,
                                                float finalDrive, float eff, float radius) =>
            FirstGearForce(torqueNm, ratios, finalDrive, eff, radius);

        /// <summary>
        /// Derive the same numbers from a catalog entry and a stage count, with
        /// no CarController anywhere. Every line here mirrors one in ApplySpec /
        /// ScaleChassisToMass / DeriveDownforce and must keep mirroring it.
        /// </summary>
        public static CarSetupBasis FromSpec(CarSpec spec, CarTune.Stages tune, bool welded)
        {
            var b = new CarSetupBasis();
            if (spec == null) return b;
            spec.Decode();

            // The shell decides the wheel radius (CarBody.ApplySpec writes it),
            // and the shell is picked by the same resolver the race scene uses.
            // A project with no baked models loads nothing, which is why this
            // falls through to the built-in figure rather than to zero.
            float radius = 0.31f;
            var def = CarModelLibrary.LoadFor(spec);
            if (def != null && def.wheelRadius > 0.05f) radius = def.wheelRadius;
            b.wheelRadius = radius;

            b.massKg = CarTune.WeightAtStage(spec.kg, spec.minKg, tune.weight);
            b.staticWheelLoad = b.massKg * 9.81f * 0.25f;

            float k = b.massKg / CarController.ChassisRefMass;
            b.springRateFront = CarController.SpringFrontRef * k;
            b.springRateRear = CarController.SpringRearRef * k;
            b.damperFront = CarController.DamperFrontRef * k;
            b.damperRear = CarController.DamperRearRef * k;
            b.antiRollFront = CarController.AntiRollFrontRef * k;
            b.antiRollRear = CarController.AntiRollRearRef * k;

            // Prefab defaults. These are the values the builder leaves on the
            // car and nothing in ApplySpec rewrites them, so they are constants
            // on this side of the fence.
            b.brakeFrontShare = CarController.DefaultBrakeFrontShare;
            b.tireMuFront = CarController.DefaultTireMuFront;
            b.tireMuRear = CarController.DefaultTireMuRear;
            // NOT the prefab constants any more: a suspension stage lowers the
            // car whether or not it unlocked the slider, and the race side does
            // the same through CarController.ApplyStageRide. Both call the same
            // CarTune function, which is what the self-test's field-by-field
            // "rest length" / "cg height" comparison is protecting.
            b.restLength = CarTune.RestLengthAtStage(
                CarController.DefaultRestLength, tune.suspension);
            b.cgHeight = CarTune.CgHeightAtStage(
                CarController.DefaultCgHeight, CarController.DefaultRestLength, tune.suspension);
            b.maxSteerLowSpeedDeg = CarController.DefaultMaxSteerLowSpeedDeg;
            b.maxSteerHighSpeedDeg = CarController.DefaultMaxSteerHighSpeedDeg;
            b.maxSteerDriftDeg = CarController.DefaultMaxSteerDriftDeg;
            b.steerRateDeg = CarController.DefaultSteerRateDeg;
            b.steerRateDriftDeg = CarController.DefaultSteerRateDriftDeg;
            b.steerReleaseRate = PlayerCarInput.DefaultSteerReleaseRate;
            b.downforceWeightFractionAtVmax = CarController.DefaultDownforceWeightFraction;
            b.downforceBalanceFront = 0.5f;
            b.drivetrainEfficiency = CarController.DefaultDrivetrainEfficiency;
            b.finalDrive = CarController.DefaultFinalDrive;

            // Same two ApplyTuneHandling lines, on the same CarTune curves.
            b.brakeDemandG = CarTune.BrakeDemandG(CarController.DefaultBrakeDemandG, tune);
            b.rawCorneringStiffness =
                CarController.DefaultCorneringStiffness * CarTune.SuspStageMult(tune.suspension);
            b.corneringStiffness = Mathf.Min(
                b.rawCorneringStiffness, CarController.CorneringStiffnessCap);

            b.topSpeedMps = spec.topSpeedMps > 1f ? spec.topSpeedMps : 64.75f;
            b.redlineRPM = spec.redline;
            b.gearRatios = spec.BuildGearRatios(b.wheelRadius, b.finalDrive);
            b.frontDriveShare = spec.FrontDriveShare;
            b.fourWheelDrive = spec.drv == "4WD";
            b.welded = welded;

            float scale = spec.hp > 0
                ? CarTune.PowerAtStage(spec.hp, spec.builtHp, tune.power) / (float)spec.hp : 1f;
            b.firstGearForceN = FirstGearForce(
                TorqueOnCurve(spec, 0.6f * spec.redline) * scale, b.gearRatios,
                b.finalDrive, b.drivetrainEfficiency, b.wheelRadius);
            return b;
        }

        static float FirstGearForce(float torqueNm, float[] ratios, float finalDrive,
                                    float eff, float radius)
        {
            if (ratios == null || ratios.Length == 0 || radius < 0.01f) return 5000f;
            return Mathf.Max(500f, torqueNm * ratios[0] * finalDrive * eff / radius);
        }

        /// <summary>Linear interpolation over the baked torque curve, matching
        /// CarController.RawTorqueAtRPM. Only used for a range endpoint, so it
        /// does not need the supercharger layer — a blower must not move where
        /// the preload slider's ends sit.</summary>
        static float TorqueOnCurve(CarSpec spec, float rpm)
        {
            var xs = spec.curveRPM; var ys = spec.curveNm;
            if (xs == null || ys == null || xs.Length < 2)
                return Mathf.Max(1f, spec.peakTorqueNm);
            // Deliberately the same walk, the same InverseLerp and the same
            // ramp-from-zero below the first sample as
            // CarController.RawTorqueAtRPM. The two are asserted equal over the
            // whole catalog, and "nearly the same interpolation" is exactly the
            // kind of difference that would make that assertion flap.
            if (rpm <= xs[0]) return ys[0] * Mathf.InverseLerp(0f, xs[0], rpm);
            for (int i = 0; i < xs.Length - 1; i++)
            {
                if (rpm > xs[i + 1]) continue;
                float f = Mathf.InverseLerp(xs[i], xs[i + 1], rpm);
                return Mathf.Lerp(ys[i], ys[i + 1], f);
            }
            return ys[ys.Length - 1];
        }
    }

    /// <summary>
    /// What each parameter's slider actually spans on one particular car.
    ///
    /// Everything here is derived from that car's own numbers rather than from a
    /// global table, which is the whole point: a 684 kg kei car and a 1981 kg GT
    /// have to arrive at the same screen and both find sensible ends on every
    /// slider. The only two exceptions are camber and toe, and they are correct
    /// exceptions — a degree of camber means the same thing on every car.
    /// </summary>
    public static class CarSetupRanges
    {
        public static CarSetupRange Of(in CarSetupBasis b, SetupParam p)
        {
            int g = CarSetupTable.GearIndex(p);
            if (g >= 0) return GearRange(b, g);

            switch (p)
            {
                // ---- tires and brakes ----
                case SetupParam.TyrePressureFront:
                case SetupParam.TyrePressureRear:
                {
                    // Recommended cold pressure rises with the load the corner
                    // carries. A 1280 kg car lands on 34 psi, a kei car on 27,
                    // a heavy GT clamps at 38 — which is the real spread.
                    float def = Mathf.Clamp(20f + 0.0045f * b.staticWheelLoad, 26f, 38f);
                    return R(def - PressureDownPsi, def, def + PressureUpPsi, 1f, 0, "psi");
                }
                // Deliberately one-sided: brakeDemandG is already capped by what
                // the TIRES can hold (CarTune.BrakeDemandG), and a slider that
                // raised it would breach the ceiling the whole upgrade ladder
                // was built around. Coming down is a real choice — less lockup —
                // and cannot break anything.
                case SetupParam.BrakePressure:
                    return R(0.70f, 1f, 1f, 100f, 0, "%");
                case SetupParam.BrakeBalance:
                    return R(b.brakeFrontShare - 0.15f, b.brakeFrontShare,
                             b.brakeFrontShare + 0.15f, 100f, 0, "% F");

                // ---- alignment ----
                case SetupParam.SteerLock:
                {
                    float def = b.maxSteerLowSpeedDeg;
                    // The ceiling matters: maxSteerDriftDeg is blended TO while
                    // sliding, so a lock set above it would make the drift blend
                    // a REDUCTION and invert the whole thing.
                    float max = Mathf.Min(def * 1.35f, b.maxSteerDriftDeg - 2f);
                    float min = Mathf.Min(Mathf.Max(def * 0.75f, b.maxSteerHighSpeedDeg + 6f), max);
                    return R(min, Mathf.Clamp(def, min, max), max, 1f, 0, "deg");
                }
                case SetupParam.SteerRate:
                {
                    float def = b.steerRateDeg;
                    float max = Mathf.Min(def * 1.5f, b.steerRateDriftDeg - 20f);
                    return R(def * 0.65f, Mathf.Min(def, max), max, 1f, 0, "d/s");
                }
                case SetupParam.SelfCentre:
                    return R(1.5f, b.steerReleaseRate, 8f, 1f, 1, "/s");
                case SetupParam.CamberFront:
                case SetupParam.CamberRear:
                    return R(-3f, 0f, 0.5f, 1f, 1, "deg");
                case SetupParam.ToeFront:
                case SetupParam.ToeRear:
                    return R(-0.30f, 0f, 0.30f, 1f, 2, "deg");

                // ---- springs and dampers ----
                case SetupParam.SpringFront:
                    return R(b.springRateFront * 0.70f, b.springRateFront,
                             b.springRateFront * 1.45f, 0.001f, 1, "N/mm");
                case SetupParam.SpringRear:
                    return R(b.springRateRear * 0.70f, b.springRateRear,
                             b.springRateRear * 1.45f, 0.001f, 1, "N/mm");
                case SetupParam.DamperFront:
                    return R(b.damperFront * 0.65f, b.damperFront,
                             b.damperFront * 1.55f, 0.001f, 2, "Ns/mm");
                case SetupParam.DamperRear:
                    return R(b.damperRear * 0.65f, b.damperRear,
                             b.damperRear * 1.55f, 0.001f, 2, "Ns/mm");
                case SetupParam.ArbFront:
                    return R(b.antiRollFront * 0.25f, b.antiRollFront,
                             b.antiRollFront * 2.5f, 0.001f, 1, "N/mm");
                case SetupParam.ArbRear:
                    return R(b.antiRollRear * 0.25f, b.antiRollRear,
                             b.antiRollRear * 2.5f, 0.001f, 1, "N/mm");
                case SetupParam.RideHeight:
                {
                    // Floored so the spring can still carry the car statically:
                    // at the stiffest legal setting the FD sits 46 mm into its
                    // travel and at the softest 95, and 200 mm clears both.
                    float def = b.restLength;
                    return R(Mathf.Max(0.20f, def - 0.08f), def, def + 0.02f, 1000f, 0, "mm");
                }

                // ---- differential ----
                //
                // THE DEFAULT IS AN OPEN DIFF, and it has to be. Every one of
                // these ranges is read through Value(t), and a factory setup is
                // t = 0 — so a non-zero def here is not a suggestion, it is a
                // differential fitted to every car in the game for free. It was
                // 0.25 / 0.10 / 0.005F for exactly one race-load before that
                // showed up: the player's bone-stock car raced a 25%-locked
                // plate pack while the identical AI car alongside it ran open,
                // and the setup screen printed "NEEDS LIMITED-SLIP DIFF" over
                // the top of it.
                //
                // It also lines up with what these parts are: a mod unlocks
                // sliders, it does not change the car on its own. Buy the plate
                // pack and you get an open diff you are now allowed to lock.
                case SetupParam.DiffAccel:
                case SetupParam.DiffDecel:
                    // A welded diff is not adjustable and not open. The gate
                    // locks these rows on a welded car, so t is pinned at 0 —
                    // which means the default is the whole answer, and the
                    // default has to be "solid".
                    return b.welded ? R(1f, 1f, 1f, 100f, 0, "%")
                                    : R(0f, 0f, 1f, 100f, 0, "%");
                case SetupParam.DiffPreload:
                    // A fraction of what this car can actually put down in
                    // first, so "60 N of preload" is the same amount of car on
                    // a Civic and on a Group C prototype.
                    return R(0f, 0f, 0.03f * b.firstGearForceN, 1f, 0, "N");
                case SetupParam.DriveSplit:
                {
                    var r = R(0.15f, b.frontDriveShare, 0.65f, 100f, 0, "% F");
                    // A row that is present but locked, not an absent one: the
                    // reference screens keep row positions stable across cars
                    // and a 2WD car should still be told it has no centre diff.
                    if (!b.fourWheelDrive) { r.absent = true; r.min = r.max = r.def; }
                    return r;
                }

                // ---- gearing ----
                case SetupParam.FinalDrive:
                {
                    // Shown as a RATIO, but applied as a scale on every gear —
                    // finalDrive itself cancels out of BuildGearRatios and every
                    // consumer uses the product, so writing the field would do
                    // precisely nothing. See ApplySetup.
                    float def = b.finalDrive;
                    return R(def * 0.80f, def, def * 1.30f, 1f, 2, "");
                }

                // ---- aero ----
                case SetupParam.AeroLevel:
                    // Already normalised per-car by construction: it is a
                    // FRACTION OF WEIGHT at that car's own top speed, so one
                    // slider range means the same thing on every car in the
                    // catalog.
                    return R(0.10f, b.downforceWeightFractionAtVmax, 0.90f, 100f, 0, "%");
                default:
                    return R(0.35f, b.downforceBalanceFront, 0.65f, 100f, 0, "% F");
            }
        }

        /// <summary>How far off the recommended pressure a setup may go. Down is
        /// the longer side because dropping pressure for grip is the move a
        /// player actually reaches for.</summary>
        public const float PressureDownPsi = 8f;
        public const float PressureUpPsi = 6f;

        /// <summary>
        /// How far off the recommended pressure a setting is, as -1..+1.
        ///
        /// Each half is normalised by its OWN span, because the range is
        /// deliberately asymmetric. Dividing both by PressureDownPsi made the
        /// hard end reach only 0.75, so the stated "+-8% peak grip, +-10%
        /// stiffness" was really -8%/+4.5% and -10%/+7.5% — a slider whose two
        /// halves did not mean the same thing, and PressureUpPsi declared and
        /// never read as the giveaway.
        /// </summary>
        public static float PressureNorm(in CarSetupRange r, float t)
        {
            float d = r.Value(t) - r.def;
            return d / (d < 0f ? PressureDownPsi : PressureUpPsi);
        }

        static CarSetupRange GearRange(in CarSetupBasis b, int g)
        {
            var r = new CarSetupRange { display = 1f, decimals = 2, unit = "" };
            if (b.gearRatios == null || g >= b.gearRatios.Length)
            {
                // This car has no such gear. Locked row, stable position.
                r.absent = true;
                r.min = r.def = r.max = 0f;
                return r;
            }
            // The anchor is "this car reaches its own redline at its own top
            // speed" (CarSpec.BuildGearRatios), so a +-20% trim against it is
            // per-car-correct without any extra work. Shown as the GEAR ratio,
            // with the final drive its own separate row, exactly as a real
            // gearing screen lists them — the physics uses the product.
            float def = b.gearRatios[g];
            r.min = def * 0.80f; r.def = def; r.max = def * 1.20f;
            return r;
        }

        static CarSetupRange R(float min, float def, float max,
                               float display, int decimals, string unit) =>
            new CarSetupRange
            {
                min = Mathf.Min(min, def), def = def, max = Mathf.Max(max, def),
                display = display, decimals = decimals, unit = unit,
            };
    }
}
