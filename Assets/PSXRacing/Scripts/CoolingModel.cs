using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// The cooling system as HARDWARE: what the radiator, the fan, the hoses
    /// and the coolant in them are each worth to the engine behind them.
    ///
    /// The temperature gauge used to read one number — <c>CoolMult</c>, derived
    /// from the fault aggregate — and one number can only ever make the needle
    /// sit higher. That is a thermometer with a difficulty slider behind it, not
    /// a cooling system, and it cannot answer the question a driver actually
    /// asks when the needle climbs: WHICH THING IS WRONG. So the system is four
    /// parts, and each one has a different SIGNATURE on the gauge:
    ///
    ///   RADIATOR — the core. Scales the whole speed-dependent term, so a
    ///     crusted core is fine at a cruise and cooks on a fast lap. The one
    ///     that fails under LOAD.
    ///   FAN — only worth anything below about 60 km/h, because above that the
    ///     air coming through the grille is already more than it can pull. A
    ///     dead fan is invisible on the open road and boils the car in traffic,
    ///     at a light, on a drag strip staging lane. The one that fails when
    ///     you STOP.
    ///   HOSES — do not cool anything. They HOLD PRESSURE, and a soft hose or a
    ///     tired cap weeps once the coolant is hot. The one that fails SLOWLY,
    ///     and the one that turns a warm engine into a dead one, because every
    ///     ounce it loses is cooling the next minute will not have.
    ///   COOLANT — what is actually in the loop. Falls only through the hoses
    ///     and through boil-over, and tops up for a few dollars in the garage.
    ///
    /// Read by <see cref="EngineTemp"/> out on the road and by the garage
    /// screens, from the same constants, so what the mechanic quotes and what
    /// the needle does cannot drift apart.
    /// </summary>
    public static class CoolingModel
    {
        // ---- radiator -----------------------------------------------------
        /// <summary>Radiator authority at a dead core. NOT zero: a blocked
        /// radiator is still a lump of metal with air going past it, and a zero
        /// here would make a 1% core identical to no radiator at all.</summary>
        public const float RadFloor = 0.30f;

        /// <summary>
        /// What a core in this condition is worth, 0.3-1.
        ///
        /// Smoothstepped rather than linear because that is how a radiator
        /// actually goes: fins bend and the outside tubes silt up long before
        /// anybody notices, and then the middle of the range is where it turns
        /// into a problem. Above 75 you will never know; under 40 you will.
        /// </summary>
        public static float RadEff(float cond) =>
            Mathf.Lerp(RadFloor, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cond * 0.01f)));

        // ---- fan ----------------------------------------------------------
        /// <summary>Below this the fan does nothing — a seized clutch, a dead
        /// motor, a shredded blade. A fan does not fade out; it stops.</summary>
        public const float FanDeadBelow = 20f;
        /// <summary>And above this it is simply a fan.</summary>
        public const float FanFullAbove = 65f;

        public static float FanEff(float cond) =>
            Mathf.Clamp01((cond - FanDeadBelow) / (FanFullAbove - FanDeadBelow));

        // ---- hoses --------------------------------------------------------
        /// <summary>Coolant temperature a tired hose starts giving up at. Below
        /// this nothing weeps however bad the rubber is, which is why a car
        /// with hoses like liquorice can sit on the drive all week and show a
        /// full overflow tank.</summary>
        public const float WeepAboveC = 100f;
        /// <summary>Percent of coolant per second lost by hoses in the WORST
        /// condition, 20 C over the weep line. A blown hose empties the system
        /// in about a minute of that; good hoses never reach this term at
        /// all.</summary>
        public const float HoseWeepPerSec = 0.30f;

        /// <summary>How leaky this set of hoses is, 0-1. Squared, so the top
        /// half of the condition bar is genuinely dry and the bottom half goes
        /// quickly — which is what makes REPLACING them before they fail worth
        /// the money.</summary>
        public static float HoseLeak(float cond)
        {
            float bad = 1f - Mathf.Clamp01(cond * 0.01f);
            return bad * bad;
        }

        // ---- boil-over ----------------------------------------------------
        /// <summary>Past this the cap lifts whatever the hoses are like: the
        /// coolant is boiling and the system is pushing it into the overflow
        /// and out of it. This is the point the loss becomes a spiral — less
        /// coolant is hotter coolant is less coolant.</summary>
        public const float BoilAboveC = 122f;
        public const float BoilPerSec = 0.55f;

        // ---- coolant level ------------------------------------------------
        /// <summary>Level at which the loop still carries everything the
        /// radiator can take. Below it the pump starts moving as much air as
        /// water and the whole system loses authority PROPORTIONALLY, which is
        /// why a slow leak reads as a gradually rising needle rather than as a
        /// sudden one.</summary>
        public const float CoolantFullAbove = 55f;

        public static float CoolantEff(float pct) =>
            Mathf.Clamp01(pct / CoolantFullAbove);

        /// <summary>Under this the overflow tank looks wrong to anybody who
        /// opens the bonnet, and the garage says so.</summary>
        public const float CoolantLowPct = 60f;

        // ---- what wears them ----------------------------------------------
        //
        // All three are consumables, but they are SLOW ones — an order of
        // magnitude slower than the engine lane, which is worn out in about
        // 30 km of racing. A radiator is a thing you replace once in a career
        // you drive carefully and three times in one you do not.

        /// <summary>Condition per metre. The engine lane is 0.0031/m; these are
        /// a tenth of it, so a hundred points of core is about 320 km.</summary>
        public const float RadWearPerM = 0.00031f;
        public const float HoseWearPerM = 0.00034f;
        /// <summary>The fan is the one part that is not really a wear item —
        /// it fails, it does not wear out — so it ages at a third of the
        /// rate and mostly dies of heat (below).</summary>
        public const float FanWearPerM = 0.00011f;

        /// <summary>
        /// What being HOT costs the hardware, per second over
        /// <see cref="WeepAboveC"/>, per part.
        ///
        /// This is the loop that makes ignoring the gauge compound instead of
        /// merely costing you a race: heat kills hoses fastest (rubber, and it
        /// is already under pressure), then the fan (its clutch and its motor
        /// live in the hot air coming off the core), then the core itself. A
        /// car driven home hot is a car whose cooling system is worse than it
        /// was when the needle first moved.
        /// </summary>
        public const float HoseHeatWearPerSec = 0.085f;
        public const float FanHeatWearPerSec = 0.030f;
        public const float RadHeatWearPerSec = 0.022f;

        // ---- the garage side ----------------------------------------------
        /// <summary>Dollars to top the coolant back up, whole system. Cheap on
        /// purpose: the punishment for a leak is the leak, not the jug.</summary>
        public const int CoolantTopUpCost = 18;

        /// <summary>The worst of the three, 0-100 — the one number a garage
        /// list can show beside ENGINE and TIRES without pretending a good
        /// radiator makes up for a dead fan. A cooling system is as good as its
        /// weakest part and no better.</summary>
        public static float Health(OwnedCar car) =>
            car == null ? 100f : Mathf.Min(car.radiator, Mathf.Min(car.fan, car.hoses));

        /// <summary>
        /// Give a car a cooling system that matches the life it has had.
        ///
        /// Every car in the game arrives through one of four doors — the
        /// classifieds, a seller's driveway, the starting lane, or an old save
        /// being migrated — and all four go through here, because a cooling
        /// system is the one thing on a car nobody negotiates over and every
        /// one of those doors would otherwise hand out a radiator off the
        /// showroom floor with 150,000 miles under it.
        ///
        /// Seeded ABOVE the car's general condition rather than equal to it: a
        /// cooling system outlasts a motor, which is precisely why it is a
        /// separate lane. A car at 15% has a tired radiator, not a scrap one.
        ///
        /// And a car already carrying cooling_fail has the two parts THAT FAULT
        /// NAMES knocked down — the catalog row is called "Radiator &amp; Hoses",
        /// so a car with it and a perfect core would be the fault contradicting
        /// itself. Not the fan, which it says nothing about. At minting time
        /// the fault list is empty and that half is a no-op; it is the
        /// migration and the used-car roll that need it.
        /// </summary>
        public static void Seed(OwnedCar car, float cond)
        {
            if (car == null) return;
            float health = Mathf.Clamp(40f + cond * 0.6f, 40f, 100f);
            bool failing = car.faults != null &&
                           car.faults.Exists(f => f != null && f.id == "cooling_fail");
            car.radiator = failing ? Mathf.Min(health, 28f) : health;
            car.hoses = failing ? Mathf.Min(health, 24f) : health;
            car.fan = Mathf.Min(100f, health + 10f);
            // Full, whatever the hoses are like. A car standing on a forecourt
            // has been topped up; what it does once it is hot is the leak's
            // business, and that is a thing the player gets to watch happen.
            car.coolant = 100f;
            car.engineBlown = false;
        }

        /// <summary>The two parts <c>cooling_fail</c> names, made new — what
        /// paying to have that fault repaired actually buys. Without it a
        /// $400 "Radiator &amp; Hoses" job would clear a line of text and leave
        /// the gauge climbing exactly as it was.</summary>
        public static void RenewNamedParts(OwnedCar car)
        {
            if (car == null) return;
            car.radiator = 100f;
            car.hoses = 100f;
            car.coolant = 100f;
        }

        /// <summary>Which part that was, for the line under the bar.</summary>
        public static string WeakestPart(OwnedCar car)
        {
            if (car == null) return "RADIATOR";
            float w = Health(car);
            if (car.fan <= w) return "FAN";
            if (car.hoses <= w) return "HOSES";
            return "RADIATOR";
        }

        // ---- ambient --------------------------------------------------------
        /// <summary>
        /// Outside air, in Celsius, for a calendar day and an hour of it.
        ///
        /// The Piedmont's own year: a January dawn is around freezing and a
        /// July afternoon is 33, and the engine notices, because every watt the
        /// radiator sheds it sheds into THAT. Fifteen degrees of ambient is
        /// about a fifth of the temperature difference the system works
        /// across — two degrees on the gauge for a healthy car, and the
        /// difference between "warm" and "boiling" for a marginal one.
        ///
        /// Deterministic, like the weather it reads: the same day and hour is
        /// always the same number, so the pre-race page can print it.
        /// </summary>
        public static float AmbientC(int day, int hour)
        {
            // No LifeSim — a standalone editor race — is a mild afternoon, which
            // is what EngineTemp assumed as a constant before any of this.
            if (day <= 0) return 18f;
            int m = Mathf.Clamp(LifeRules.MonthOf(day), 1, 12) - 1;
            float t = MonthMeanC[m] + HourOffsetC[Mathf.Clamp(hour, 0, HourOffsetC.Length - 1)];
            switch (Seasons.WeatherFor(day))
            {
                case Weather.Rain: t -= 3f; break;
                case Weather.Snow: t -= 4f; break;
                case Weather.Fog: t -= 1f; break;
            }
            return t;
        }

        /// <summary>Ambient right now, wherever "now" is: the calendar day the
        /// home screen stamped and the hour the scene was built for.</summary>
        public static float CurrentAmbientC =>
            AmbientC(Seasons.CurrentDay, TimeOfDay.Current);

        /// <summary>Mean daily temperature by month, Charlotte / the NC
        /// Piedmont, Jan..Dec.</summary>
        static readonly float[] MonthMeanC =
            { 6f, 8f, 12f, 16f, 21f, 25f, 27f, 26f, 23f, 16f, 11f, 7f };

        /// <summary>Departure from the daily mean at each of the seven hours
        /// the game is played at — dawn is the cold end of a day and the
        /// afternoon is the hot one, indexed to match
        /// <see cref="TimeOfDay.All"/>.</summary>
        static readonly float[] HourOffsetC =
            { -6f, -2f, 4f, 6f, 3f, 1f, -3f };
    }
}
