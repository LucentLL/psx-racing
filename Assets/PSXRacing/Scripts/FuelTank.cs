using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The player car's tank, burning in real time.
    ///
    /// Fuel used to be a number the menu subtracted AFTER a race: distance in,
    /// percentage out, and the only way to put any back was a REFUEL button in
    /// the garage. That made the tank a between-races tax rather than a thing
    /// in the car, and it made the gas station beside the road on two circuits
    /// pure scenery.
    ///
    /// What it burns is <see cref="FuelModel"/> — a per-car tank and a per-car
    /// MPG, weighted by where the needle is sitting. Before that this was one
    /// flat percent-per-metre for all 317 cars, which put every tank in the
    /// game at 6.2 km and made the 7 km parkway stage impossible to finish.
    ///
    /// Distance-based rather than time-based, deliberately: there is no idle
    /// burn. A time-based one would drain the tank on the grid during the
    /// countdown and while the player is parked at the pump deciding — both
    /// moments where the game has taken the controls away or is asking for a
    /// decision, and neither is a moment to charge someone for. Revs still
    /// count, but only against ground actually covered.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class FuelTank : MonoBehaviour
    {
        public CarController car;

        /// <summary>Tank level, 0-100. The SAME unit
        /// <see cref="LifeSim.OwnedCar.fuel"/> carries, so the handoff never
        /// converts.</summary>
        public float percent = 100f;

        /// <summary>Fault handicap — a leaking tank or a rich mixture burns
        /// faster. Comes off <see cref="RaceHandoff.FuelMult"/>, which is what
        /// the old apply-back multiplied the whole-race burn by.</summary>
        public float burnMult = 1f;

        /// <summary>Low-fuel light. A quarter of a tank is about 10 km on a
        /// mid-pack car driven hard, and 5 km on the thirstiest thing in the
        /// catalog — either way more than a lap of the longest circuit, which
        /// is the warning you actually want: "you can finish this lap, then
        /// find a pump".</summary>
        public const float LowPct = 25f;

        /// <summary>Below this the pickup starts sucking air on the overrun.
        /// The engine does not simply stop at zero — a car running out of fuel
        /// stumbles first, and that stumble is the only warning a player who
        /// ignored the gauge is going to read.</summary>
        const float SputterPct = 2.0f;

        /// <summary>One sputter cycle. Long enough to feel like a misfire
        /// rather than a framerate problem.</summary>
        const float SputterPeriod = 0.85f;

        public bool Empty => percent <= 0.0005f;
        public bool Low => percent <= LowPct;

        /// <summary>True on the frames the engine has nothing to burn. Read by
        /// <see cref="PlayerCarInput"/>, which is the one place the throttle is
        /// written — cutting it here as well would be two components fighting
        /// over the same field.</summary>
        public bool Starved { get; private set; }

        float sputterClock;

        CarSpec profileSpec;
        bool profileBuilt;
        FuelProfile profile;

        /// <summary>
        /// What this car does with a gallon. Rebuilt whenever the spec on the
        /// controller changes, which is once — RaceHandoffApplier fits the
        /// catalog entry and the parts in its Start, after every Awake.
        /// Read by the pump, which prices a fill off the tank it is filling.
        /// </summary>
        public FuelProfile Profile
        {
            get
            {
                var spec = car != null ? car.activeSpec : null;
                if (!profileBuilt || spec != profileSpec)
                {
                    profileSpec = spec;
                    profile = spec != null
                        ? FuelProfile.For(spec, car.activeTune)
                        // No catalog entry: the controller's built-in RX-7, but
                        // with whatever mass the scene actually gave it.
                        : FuelProfile.Of(FuelModel.FallbackHp,
                            Mathf.RoundToInt(car != null ? car.massKg : FuelModel.FallbackKg));
                    profileBuilt = true;
                }
                return profile;
            }
        }

        /// <summary>Kilometres left at the current level, driven hard. The
        /// number worth printing next to a percentage.</summary>
        public float RangeKm => Profile.RangeKm(FuelModel.RacePaceLoad) * percent / 100f;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
        }

        void Update()
        {
            if (car == null) { Starved = false; return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!Empty)
            {
                float metres = Mathf.Abs(car.speedKmh) / 3.6f * dt;
                // Which pedal is the accelerator depends on the gear — the
                // controller makes the same swap in reverse, and a tank that
                // disagreed with it would bill the brake for going backwards.
                float accelPedal = car.currentGear == -1 ? car.brakeInput : car.throttleInput;
                float revs = Mathf.InverseLerp(car.idleRPM, car.redlineRPM, car.currentRPM);
                float load = FuelModel.Load(revs, accelPedal > 0.05f);
                percent = Mathf.Max(0f, percent -
                    Profile.Burn(metres, load) * Mathf.Max(0.01f, burnMult));
            }

            if (Empty) { Starved = true; sputterClock = 0f; return; }
            if (percent > SputterPct) { Starved = false; sputterClock = 0f; return; }

            sputterClock += dt;
            Starved = Mathf.Repeat(sputterClock, SputterPeriod) > SputterPeriod * 0.6f;
        }

        /// <summary>Put fuel in. Returns how much actually went in, which is
        /// what the pump bills for — a nozzle that keeps charging after the
        /// tank is full is the one thing everybody notices.</summary>
        public float Add(float pct)
        {
            float room = Mathf.Max(0f, 100f - percent);
            float taken = Mathf.Clamp(pct, 0f, room);
            percent += taken;
            return taken;
        }
    }
}
