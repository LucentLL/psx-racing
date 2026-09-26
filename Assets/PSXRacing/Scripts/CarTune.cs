using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// What a car's upgrade stages are WORTH — the pure curves, with no
    /// economy attached.
    ///
    /// This lives on the race side of the fence on purpose. The LifeSim's
    /// shop needs these numbers to quote a stage, and the race scene needs them
    /// to build the car that actually drives; if each owned a copy, a shop
    /// screen promising "+45% braking" and a stopwatch measuring something else
    /// is a bug nobody would find for weeks. LifeSim.Upgrades adds prices, days,
    /// skill gates and per-car state on top of this.
    ///
    /// Ported from RG2's config/cars/upgradeHeadroom.ts.
    /// </summary>
    public static class CarTune
    {
        public const int MaxStage = 4;

        /// <summary>The five stage counters for one car, 0-4 each.</summary>
        public struct Stages
        {
            public int power, weight, brakes, suspension, tires;
            /// <summary>The passenger seat. Carried here so the race scene
            /// has it alongside the rest, though nothing in CarTune reads it:
            /// its only consumer is the pizza on it.</summary>
            public int seat;

            public bool IsStock =>
                power == 0 && weight == 0 && brakes == 0 && suspension == 0 && tires == 0 &&
                seat == 0;
        }

        /// <summary>Cumulative share of the stock->built HP span unlocked at each
        /// stage. FRONT-LOADED: stage 1 is the intake/exhaust/turbo and is the
        /// biggest single jump, which is how real forced-induction tuning goes
        /// and what keeps stage 1 feeling worth buying on a slow car.</summary>
        static readonly float[] PowerFrac = { 0f, 0.45f, 0.70f, 0.88f, 1.00f };
        /// <summary>Weight comes off roughly linearly — there is no "the first
        /// mod does most of it" when you are simply removing parts.</summary>
        static readonly float[] WeightFrac = { 0f, 0.25f, 0.50f, 0.75f, 1.00f };
        static readonly float[] BrakeFrac = { 0f, 0.40f, 0.65f, 0.85f, 1.00f };
        static readonly float[] SuspFrac = { 0f, 0.45f, 0.70f, 0.88f, 1.00f };
        static readonly float[] GripFrac = { 0f, 0.40f, 0.66f, 0.86f, 1.00f };

        /// <summary>Pads + fluid -> race calipers.</summary>
        public const float BuiltBrakeMult = 1.45f;
        /// <summary>Lowering springs -> race coilovers + bushings.</summary>
        public const float BuiltSuspMult = 1.25f;
        /// <summary>Sport tyres -> track compound.</summary>
        public const float BuiltGripMult = 1.20f;

        public static int Clamp(int stage) => Mathf.Clamp(stage, 0, MaxStage);

        /// <summary>Crank HP at a stage. The endpoints are per-car and baked:
        /// stock is the factory figure, built is the realistic streetable ceiling
        /// for that specific engine (a 13B-REW tops ~500 whether it started at
        /// 255 or 280).</summary>
        public static int PowerAtStage(int stockHp, int builtHp, int stage) =>
            Mathf.RoundToInt(stockHp + (builtHp - stockHp) * PowerFrac[Clamp(stage)]);

        public static int WeightAtStage(int stockKg, int minKg, int stage) =>
            Mathf.RoundToInt(stockKg - (stockKg - minKg) * WeightFrac[Clamp(stage)]);

        public static float BrakeStageMult(int stage) =>
            1f + (BuiltBrakeMult - 1f) * BrakeFrac[Clamp(stage)];

        public static float SuspStageMult(int stage) =>
            1f + (BuiltSuspMult - 1f) * SuspFrac[Clamp(stage)];

        public static float GripStageMult(int stage) =>
            1f + (BuiltGripMult - 1f) * GripFrac[Clamp(stage)];

        // ---- top speed --------------------------------------------------------
        //
        // The owner, 2026-09-21: "All stock max speeds are listed in original
        // GT4 specs. Upgrades should increase that by a %." So a build's top
        // speed is a PERCENTAGE of the car's stock figure (baked from GT4's own
        // spec fields by tools/bake_topspeed.py), and the physics is solved to
        // land on it: CarSpec.BuildGearRatios puts the engine's peak power at
        // this speed in top gear, and CarController.DeriveDrag solves the drag
        // so the car balances there. That makes the stock gearbox the fastest
        // there is - no gearing slider can beat the number by more than the
        // shape of the power curve allows (2% at worst over the catalog).
        //
        // Before this, the engine build scaled torque while the drag stayed the
        // stock body's, so a build gained the cube root of its power - and the
        // gearing sliders let it use all of it. That, on top of a stock figure
        // that was itself 29% high, is how a car got past 300 mph.

        /// <summary>Top speed a fully built engine adds, as a share of stock.
        /// Walked on the POWER ladder's own front-loaded curve, so the shop's
        /// "+hp" and "+top speed" move together.</summary>
        public const float BuiltTopSpeedGain = 0.15f;
        /// <summary>What a Roots blower adds on top. It is a power part like the
        /// ladder, just not one of its rungs.</summary>
        public const float BlowerTopSpeedGain = 0.04f;

        /// <summary>Stock top speed times this is the build's.
        /// <paramref name="pathShare"/> is how much of the engine's full build
        /// the car's power PATH reaches (1 for a turbo build, NaPathShare for
        /// a naturally aspirated one - see CarSpec.PathShare).</summary>
        public static float TopSpeedMult(int powerStage, bool blower, float pathShare = 1f) =>
            1f + BuiltTopSpeedGain * pathShare * PowerFrac[Clamp(powerStage)] + (blower ? BlowerTopSpeedGain : 0f);

        /// <summary>The build's top speed over stock, as a whole percentage, for
        /// the shop and the spec page.</summary>
        public static int TopSpeedGainPct(int powerStage, bool blower, float pathShare = 1f) =>
            Mathf.RoundToInt((TopSpeedMult(powerStage, blower, pathShare) - 1f) * 100f);

        // ---- the two power paths (owner, 2026-09-25) ---------------------------
        //
        // "When maxing out power upgrades, cars do not acquire a turbo. Max NA
        // builds are fine, but cars need options for turbos. And that may lead
        // to an alternate power path than NA builds." Chosen: TWO LADDERS per
        // car - NA (instant response, lower ceiling; the Roots blower is an NA
        // part) or TURBO (the engine's full built ceiling, with lag that grows
        // with the turbo). A factory-turbo car is on the turbo path only.
        //
        // The baked builtHp was always a TURBO figure for an NA car (RG2's 1.9x
        // bucket says so: "small-displacement engines turbo well and ~double"),
        // so the turbo path keeps it and the NA path takes a share of the gain.

        /// <summary>Share of the stock->built gain an NA build reaches: a 1.9x
        /// small-displacement engine tops out at ~1.5x naturally aspirated, a
        /// 1.4x big V8 at ~1.22x (plus a blower, if it wants more).</summary>
        public const float NaPathShare = 0.55f;

        /// <summary>
        /// Torque a turbo engine makes with NO boost, as a share of its boosted
        /// curve. A turbo kit on an NA engine leaves the NA engine underneath
        /// (stock hp over built hp); a factory turbo engine off boost makes
        /// about 75% of its rated torque. The bigger the build, the more of the
        /// engine is boost - and the more there is to wait for.
        /// </summary>
        public static float TurboOffBoost(int stockHp, int stageHp, bool factoryTurbo)
        {
            if (stageHp <= 0) return 1f;
            float natural = (factoryTurbo ? 0.75f : 1f) * stockHp;
            return Mathf.Clamp(natural / stageHp, 0.35f, 1f);
        }

        /// <summary>How much boost the turbo CAN make at this point in the rev
        /// range (0..1). Nothing below the threshold, full a quarter of the
        /// range later; a bigger turbo (higher stage) comes on later.</summary>
        public static float BoostAvailable(float rpmFrac, int stage)
        {
            float t0 = 0.20f + 0.05f * Clamp(stage);
            return Mathf.Clamp01((rpmFrac - t0) / 0.25f);
        }

        /// <summary>How fast boost builds, per second: quicker at high rpm
        /// (more exhaust), slower for a bigger turbo - stage 4 spools at half
        /// the rate of a small one. RG2's monolith spool, 3.0 x (1 + rpm).</summary>
        public static float SpoolRate(float rpmFrac, int stage) =>
            2.4f * (0.5f + rpmFrac) / (1f + 0.25f * Clamp(stage));

        /// <summary>Boost bleeding off with the throttle shut, per second.</summary>
        public const float BoostBleedRate = 2.5f;

        // ---- the Roots blower's torque curve ----------------------------------
        // Here rather than in CarController because the GEARBOX needs it now:
        // a blower moves where the engine makes its peak power, which is where
        // top gear is anchored, and the garage builds that gearbox with no
        // CarController anywhere (CarSetupBasis.FromSpec).

        public const float SuperchargerPeak = 1.30f;
        public const float SuperchargerTop = 1.15f;
        public const float SuperchargerTaperStart = 0.6f;

        /// <summary>Roots boost by RPM: flat to 60% of the rev range, then
        /// tapering as airflow falls off.</summary>
        public static float BlowerBoost(float rpm, float idleRPM, float redlineRPM)
        {
            float frac = Mathf.Clamp01((rpm - idleRPM) / Mathf.Max(redlineRPM - idleRPM, 1f));
            float taper = Mathf.Max(0f, (frac - SuperchargerTaperStart) / (1f - SuperchargerTaperStart));
            return SuperchargerPeak - (SuperchargerPeak - SuperchargerTop) * taper;
        }

        // ---- race cars ---------------------------------------------------------
        //
        // The owner, 2026-09-21: "Race cars are assumed to already be maxed out
        // by default so they don't get upgrades, but they should also be close
        // to the upper limit of speed, handling, etc." A race car's GT4 power
        // and weight are already race figures, so those ladders stay at its own
        // stock. What a road car has to BUY - race brakes, coilovers, a track
        // compound - a race car arrives with.

        /// <summary>
        /// The stages the car's HANDLING is built from: a race car's own race
        /// hardware, whatever the save says is bolted to it; anybody else's
        /// ladders as bought. Power and weight are zero on a race car because
        /// its stock figures ARE the race figures.
        ///
        /// Handling only, and on purpose: the suspension stage also LOWERS a
        /// road car (<see cref="RestLengthAtStage"/>), and a race car's factory
        /// ride height is already its race ride height. So callers pass THIS to
        /// the brake / grip / stiffness curves and the car's own (zero) stages
        /// to the ride height.
        /// </summary>
        public static Stages HandlingOf(bool raceCar, Stages tune) => raceCar
            ? new Stages { power = 0, weight = 0, brakes = MaxStage, suspension = MaxStage,
                           tires = MaxStage, seat = tune.seat }
            : tune;

        /// <summary>What a race car's save stages COUNT as: none. The one rule
        /// both sides of the fence apply before anything else, so a race car
        /// carried over from a save that let it buy stages races as the race
        /// car it is.</summary>
        public static Stages BoughtOf(bool raceCar, Stages tune) =>
            raceCar ? new Stages { seat = tune.seat } : tune;

        // ---- what a suspension stage does that you cannot adjust -----------
        //
        // A part changes the car whether or not it hands you a slider, and
        // until now this ladder pretended otherwise: LOWERING SPRINGS bought
        // you 11% of cornering stiffness and left the car sitting at exactly
        // the factory ride height, because RIDE HEIGHT was gated on stage 3.
        // That reads as the part not being fitted.
        //
        // The gate is right and stays: in 1999 a height-adjustable coilover
        // was a specialist import, and what the aftermarket actually sold was
        // a fixed lowering spring — you chose the DROP when you bought the
        // set, not afterwards with a spanner. So the stage lowers the car by
        // a fixed amount from stage 1, and stage 3 is where the height stops
        // being the part's decision and becomes yours.

        /// <summary>
        /// How far each SUSPENSION stage drops the car, metres, off its own
        /// stock rest length.
        ///
        /// Stage 1 is a 30 mm lowering spring, which is what a 1999 catalogue
        /// sold. Stage 2 adds sport dampers — a damper does not lower a car by
        /// itself, and the 8 mm is the shorter-bodied strut the set comes on,
        /// not the damping. Stage 3 and 4 are coilovers, where the ride height
        /// is a fitting decision and the numbers below are only where the car
        /// LANDS: from stage 3 the RIDE HEIGHT row is unlocked and the driver
        /// moves it from here.
        /// </summary>
        static readonly float[] RideDropM = { 0f, 0.030f, 0.038f, 0.050f, 0.062f };

        /// <summary>Nothing goes below this however the stages stack. Well
        /// clear of the 0.20 m floor CarSetupRanges puts on the slider, so the
        /// clamp is a backstop rather than something the ladder rides into.
        /// </summary>
        public const float MinRestLength = 0.16f;
        /// <summary>Lowest the centre of gravity is allowed to go. Same figure
        /// CarController.ApplySetup has always clamped to — a car whose CG is
        /// at the axle line stops transferring weight at all.</summary>
        public const float MinCgHeight = 0.30f;
        /// <summary>
        /// How much of a body drop the whole-car CG follows.
        ///
        /// Not 1. The sprung mass comes down by the full amount and the
        /// unsprung mass — wheels, hubs, brakes, roughly a seventh of the car
        /// and sitting low — does not move at all. Three quarters is that
        /// split, and it is the same relationship
        /// <see cref="CarController.ApplySetup"/> uses for the driver's own
        /// ride-height slider, which moves cgHeight one-for-one with
        /// restLength. The two differ deliberately: the slider is a small trim
        /// about a point the ladder already chose.
        /// </summary>
        public const float CgFollowsRide = 0.75f;

        /// <summary>How far below stock this stage sits the car, metres.</summary>
        public static float RideDropAtStage(int stage) => RideDropM[Clamp(stage)];

        /// <summary>Rest length at a stage, from this car's own stock figure.
        /// The ONE derivation of it: <see cref="CarController"/> calls this on
        /// the race side and <see cref="CarSetupBasis.FromSpec"/> calls it in
        /// the garage, and the self-test compares the two field by field.
        /// </summary>
        public static float RestLengthAtStage(float stockRestLength, int stage) =>
            Mathf.Max(MinRestLength, stockRestLength - RideDropAtStage(stage));

        /// <summary>Centre-of-gravity height at a stage, from this car's own
        /// stock pair. Takes the stock rest length as well because the drop it
        /// applies is the one the CLAMP actually allowed, not the one the
        /// table asked for.</summary>
        public static float CgHeightAtStage(float stockCgHeight, float stockRestLength, int stage)
        {
            float dropped = stockRestLength - RestLengthAtStage(stockRestLength, stage);
            return Mathf.Max(MinCgHeight, stockCgHeight - dropped * CgFollowsRide);
        }

        /// <summary>
        /// Tyre-limited peak braking in g on STOCK rubber. A good street car
        /// stops at about 1.0 g; a shade over here, because the brief is an
        /// arcade sim that leans the player's way.
        /// </summary>
        public const float BrakeGCapStock = 1.05f;

        /// <summary>
        /// Braking demand after the brakes stage, CAPPED by what the tyres can
        /// hold. Bigger brakes resist fade and improve modulation; they cannot
        /// raise peak mu, so stacking 1.45x on an already-0.9 g car would give
        /// 1.3 g — which no car on street rubber does. The ceiling rides the
        /// TYRE stage, so the two upgrades interact the way they do on a real
        /// car: brakes get you to the limit, tyres raise the limit.
        /// </summary>
        public static float BrakeDemandG(float stockDemandG, Stages up) =>
            Mathf.Min(stockDemandG * BrakeStageMult(up.brakes),
                      BrakeGCapStock * GripStageMult(up.tires));
    }
}
