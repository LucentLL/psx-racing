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

            public bool IsStock =>
                power == 0 && weight == 0 && brakes == 0 && suspension == 0 && tires == 0;
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
