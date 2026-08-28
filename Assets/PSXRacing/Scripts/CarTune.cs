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
