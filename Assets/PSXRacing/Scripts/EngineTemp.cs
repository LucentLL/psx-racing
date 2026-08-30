using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Coolant temperature, for the gauge in the bottom of the tachometer.
    ///
    /// A temperature gauge with nothing behind it is a sticker, so this is a
    /// real lumped model rather than a needle parked at the middle: one heat
    /// source that scales with how hard the engine is working, and one radiator
    /// whose effectiveness scales with road speed and is gated by a thermostat.
    /// The behaviour that comes out of it is the behaviour a driver expects —
    /// the needle leaves C during the first minute and then sits just under the
    /// middle whatever you do, because that is what a thermostat is FOR — and
    /// the two ways to move it off there are the two real ones: sit still with
    /// your foot in it, or drive a car whose cooling system is finished.
    ///
    /// Calibrated, not guessed. At a light cruise it reaches 82 C in about a
    /// minute and settles at 87.5; at full throttle and 200 km/h it settles at
    /// 89.6, because more airflow arrives with the extra heat. Held stationary
    /// at wide open throttle it runs away to 138, which it should. With the
    /// cooling system down to 45% it passes 130 on a fast lap and pins the
    /// needle on H.
    ///
    /// Player only, like <see cref="FuelTank"/>: nothing reads an opponent's
    /// temperature and four more Update()s a frame for a number nobody sees is
    /// not a trade worth making.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class EngineTemp : MonoBehaviour
    {
        public CarController car;

        /// <summary>Coolant temperature in Celsius. Starts at ambient — a car
        /// on the grid has not been running.</summary>
        public float celsius = Ambient;

        /// <summary>
        /// How much of the cooling system still works, 0-1. Comes off the fault
        /// aggregate via <see cref="RaceHandoff.CoolMult"/>: cooling_fail is
        /// the fault whose own description in the catalog is "Overheating risk",
        /// and this is where that stops being a line of text.
        /// </summary>
        public float coolMult = 1f;

        /// <summary>Outside air. Fixed rather than taken from the hour: the
        /// difference between a Blue Ridge dawn and a Charlotte afternoon is a
        /// couple of degrees on the gauge, and a temperature that drifted with
        /// the clock would read as an instrument fault.</summary>
        public const float Ambient = 18f;

        /// <summary>Left end of the gauge. The needle sits on C below this,
        /// which is where a cold engine's needle sits.</summary>
        public const float ColdMark = 50f;
        /// <summary>Right end. Off the scale, not merely warm.</summary>
        public const float HotMark = 130f;
        /// <summary>Where the thermostat holds a healthy engine — the middle of
        /// the gauge by construction, since that is where a driver reads
        /// "fine".</summary>
        public const float Normal = 90f;

        // ---- the model ----------------------------------------------------
        /// <summary>Heat with the throttle shut, in degrees per second against
        /// a cold block.</summary>
        const float HeatIdle = 0.55f;
        /// <summary>Extra heat at full load.</summary>
        const float HeatLoad = 1.60f;
        /// <summary>Losses that do not go through the radiator: the block, the
        /// oil, the exhaust. Always on, which is why a car with the thermostat
        /// shut still cannot heat forever.</summary>
        const float LossBlock = 0.006f;
        /// <summary>Radiator authority per unit of airflow.</summary>
        const float LossRad = 0.014f;
        /// <summary>Airflow with the car stopped — the fan. Without a floor
        /// here a stationary engine has no cooling at all and boils on the
        /// grid.</summary>
        const float AirIdle = 0.85f;
        /// <summary>Road speed that doubles the fan's airflow.</summary>
        const float AirPerKmh = 1f / 90f;
        const float AirMax = 2.5f;
        /// <summary>Thermostat opening band. Shut below the first, wide open
        /// above the second.</summary>
        const float ThermoShut = 82f, ThermoOpen = 94f;

        /// <summary>Needle position, 0 = C, 1 = H.</summary>
        public float Gauge => Mathf.Clamp01((celsius - ColdMark) / (HotMark - ColdMark));

        /// <summary>Past the point a driver should be lifting. Nothing reads
        /// this yet — the cooling fault already carries its own power handicap —
        /// but the gauge means nothing if the game cannot tell.</summary>
        public bool Overheating => celsius > 115f;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
        }

        void Update()
        {
            if (car == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float revs = Mathf.InverseLerp(car.idleRPM, car.redlineRPM, car.currentRPM);
            // Which pedal is the accelerator depends on the gear, the same swap
            // FuelTank makes: an engine worked hard in reverse is worked hard.
            float pedal = car.currentGear == -1 ? car.brakeInput : car.throttleInput;
            float load = Mathf.Clamp01(0.12f + 0.88f * Mathf.Clamp01(pedal) * revs);

            float air = Mathf.Min(AirMax, AirIdle + Mathf.Abs(car.speedKmh) * AirPerKmh);
            float thermo = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThermoShut, ThermoOpen, celsius));
            float cooling = Mathf.Max(0.05f, coolMult);

            float heat = HeatIdle + HeatLoad * load;
            float loss = (LossBlock + LossRad * air * thermo) * cooling * (celsius - Ambient);

            // Explicit Euler at 60 Hz on a system whose fastest time constant
            // is tens of seconds — the step is four orders of magnitude inside
            // stability, so there is nothing to integrate more carefully.
            celsius = Mathf.Max(Ambient, celsius + (heat - loss) * dt);
        }
    }
}
