using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// How fuel is spent, for every car in the game.
    ///
    /// Fuel used to be ONE constant — a flat percent per metre, ported across
    /// from the TypeScript rewrite's arcade burn. That number came from a
    /// free-roam city where a pump is never more than a block away, and it put
    /// a full tank at 6.2 km FOR EVERY CAR: a 90 hp hatchback and a Group C
    /// car emptied at the identical rate, and the BLUE RIDGE PARKWAY stage —
    /// 7 km of real parkway with no services on it — could not be finished by
    /// anything in the catalog on a full tank. The gate was right; the rate
    /// was wrong.
    ///
    /// This is the model the HTML game actually shipped: a per-car TANK and a
    /// per-car MPG, burned against real road speed and weighted by how hard
    /// the engine is being worked.
    ///
    ///     gal/sec = mph / (mpg x 3600) x FuelRate x load
    ///
    /// The first term is the textbook one. <see cref="FuelRate"/> is the
    /// game-speed multiplier on top: real economy over a 3 km race is a
    /// rounding error, and a resource that never moves is not a resource.
    ///
    /// Tank and economy both come off the engine, so they pull against each
    /// other — a big motor carries more fuel AND drinks it faster, and the
    /// second effect wins. Across the 317-car catalog that lands cruising
    /// range between 29 km (Escudo Pikes Peak) and 110 km (Volvo 240 estate),
    /// median 60 km; at racing revs, roughly two thirds of that. One tank is
    /// several circuit races, one long night in Charlotte, or two runs down
    /// the parkway with room to spare.
    /// </summary>
    public static class FuelModel
    {
        /// <summary>Game-speed multiplier on the real gal/sec figure. 8x is the
        /// monolith's own FUEL_RATE, kept rather than re-picked so the HTML
        /// game's balance carries over intact.</summary>
        public const float FuelRate = 8f;

        /// <summary>Revs below this fraction of the usable band (idle to
        /// redline) cost nothing extra — an engine loafing along is an engine
        /// at its quoted MPG.</summary>
        public const float CruiseRevShare = 0.5f;

        /// <summary>What the top half of the rev range adds: load runs 1.0 at
        /// half revs to 1.8 on the limiter. This is the term that makes a lap
        /// driven properly cost more than the same lap cruised, which is the
        /// whole reason to read RPM at all.</summary>
        public const float RedlineSurcharge = 1.6f;

        /// <summary>Trailing throttle. Coasting and braking still burn — the
        /// engine is turning — but at a fraction of the on-power rate.</summary>
        public const float OffThrottleShare = 0.3f;

        /// <summary>The load a race actually sits at, for everything that has
        /// to PREDICT a burn instead of watching one: the pre-race gate, the
        /// menu's estimate, and the apply-back's fallback when the race scene
        /// never reported a tank. 1.5 is about 81% revs — hard driving, not a
        /// qualifying lap, which is the honest average over a race that
        /// includes corners.</summary>
        public const float RacePaceLoad = 1.5f;

        /// <summary>87 REG, straight off RG2's FUEL_GRADES table. There is one
        /// grade here rather than four: PSX Racing has no octane, no diesel
        /// and no fuel-tanker job perk to hang the other three on.</summary>
        public const float PricePerGallon = 0.99f;

        // The built-in Mazda RX-7 Type RS (FD) '98 the CarController ships
        // with — what a car answers to when it has no catalog spec at all: a
        // standalone editor race, or a save from before specs were stored.
        public const int FallbackHp = 255;
        public const int FallbackKg = 1280;

        public static FuelProfile Fallback => FuelProfile.Of(FallbackHp, FallbackKg);

        /// <summary>Tank capacity from the engine and the body it is bolted
        /// into. Verbatim from the monolith's rebuildCarSpecs.</summary>
        public static float TankGallons(int hp, int kg) =>
            Mathf.Round((6f + hp * 0.02f + kg * 0.003f) * 10f) / 10f;

        /// <summary>Economy from power alone, capped at 40 — a 125 hp car and
        /// a 90 hp car both return 40 mpg because past that point the body and
        /// the gearing decide, and the catalog does not carry either.</summary>
        public static float Mpg(int hp) => Mathf.Min(40f, 5000f / Mathf.Max(1f, hp));

        /// <summary>
        /// How hard the engine is working, as a multiplier on the cruise rate.
        /// </summary>
        /// <param name="revFraction">Where the needle sits between idle and
        /// redline, 0-1.</param>
        /// <param name="onThrottle">Whether the accelerator is down at all.</param>
        public static float Load(float revFraction, bool onThrottle) =>
            (1f + Mathf.Max(0f, Mathf.Clamp01(revFraction) - CruiseRevShare) * RedlineSurcharge)
            * (onThrottle ? 1f : OffThrottleShare);
    }

    /// <summary>
    /// One car's relationship with a gallon. Everything that burns fuel,
    /// prices it, or predicts it reads those two numbers from here, so the
    /// gauge on the dash, the gate on the home screen and the total on the
    /// pump can never disagree about what the car is carrying.
    /// </summary>
    public struct FuelProfile
    {
        /// <summary>Tank capacity, US gallons.</summary>
        public float tankGal;
        /// <summary>Steady-cruise economy, miles per US gallon.</summary>
        public float mpg;

        // A default-constructed profile would divide by zero and read as a car
        // that empties instantly. Nothing should hand one out — every path
        // below goes through Of/For — but the arithmetic answers as the
        // built-in car rather than as a bug if something does.
        float Gal => tankGal > 0.1f ? tankGal : FuelModel.TankGallons(FuelModel.FallbackHp, FuelModel.FallbackKg);
        float Economy => mpg > 0.1f ? mpg : FuelModel.Mpg(FuelModel.FallbackHp);

        /// <summary>Percent of THIS tank burned per mile at a cruise. Every
        /// other figure here is derived from it, so there is exactly one
        /// expression of the burn in the project.</summary>
        public float PctPerMile => 100f * FuelModel.FuelRate / (Economy * Gal);

        /// <summary>Percent burned covering <paramref name="metres"/> at a
        /// given engine load (see <see cref="FuelModel.Load"/>).</summary>
        public float Burn(float metres, float load) =>
            Mathf.Max(0f, metres) / LifeRules.MetersPerMile * PctPerMile * Mathf.Max(0f, load);

        /// <summary>How far a full tank goes at that load, kilometres — the
        /// only honest answer to "can I finish this one?".</summary>
        public float RangeKm(float load) =>
            100f / (PctPerMile * Mathf.Max(0.01f, load)) * LifeRules.MetersPerMile / 1000f;

        /// <summary>Dollars per percent of THIS tank. A 28-gallon Group C car
        /// costs nearly three times what a kei car does to fill, which is the
        /// point of the tank being per-car rather than a constant.</summary>
        public float CostPerPct => Gal * FuelModel.PricePerGallon / 100f;

        /// <summary>Whole dollars to fill from where the needle is now.</summary>
        public int CostToFill(float fromPct) =>
            Mathf.CeilToInt(Mathf.Max(0f, 100f - fromPct) * CostPerPct);

        public static FuelProfile Of(int hp, int kg) => new FuelProfile
        {
            tankGal = FuelModel.TankGallons(hp, kg),
            mpg = FuelModel.Mpg(hp),
        };

        /// <summary>
        /// The car as it is BUILT, not as it left the factory. A stage-4 engine
        /// in a stripped shell is a genuinely thirstier car, and reading the
        /// stock figures instead would let a 500 hp 13B do a 255 hp car's
        /// mileage — the one place where tuning has to cost something back.
        /// </summary>
        public static FuelProfile For(CarSpec spec, CarTune.Stages tune)
        {
            if (spec == null) return FuelModel.Fallback;
            return Of(CarTune.PowerAtStage(spec.hp, spec.builtHp, tune.power),
                      CarTune.WeightAtStage(spec.kg, spec.minKg, tune.weight));
        }

        /// <summary>The garage-side resolve: catalog entry plus the parts on
        /// this particular example. Falls back to the built-in car for a save
        /// written before specs were stored.</summary>
        public static FuelProfile For(OwnedCar car)
        {
            if (car == null) return FuelModel.Fallback;
            var spec = CarCatalog.Get(car.specId);
            if (spec == null) return FuelModel.Fallback;
            return For(spec, new CarTune.Stages { power = car.upPower, weight = car.upWeight });
        }
    }
}
