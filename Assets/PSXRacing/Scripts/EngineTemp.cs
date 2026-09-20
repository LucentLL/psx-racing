using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Coolant temperature, for the gauge in the bottom of the tachometer —
    /// and everything behind it.
    ///
    /// A temperature gauge with nothing behind it is a sticker, so this is a
    /// real lumped model: heat that scales with how hard the engine is being
    /// worked, against a cooling system made of separate PARTS that each fail
    /// in their own way (see <see cref="CoolingModel"/>), into outside air that
    /// is whatever the calendar says it is. The behaviour that comes out of it
    /// is the behaviour a driver expects — the needle spends the first minute
    /// or two climbing off C and then sits JUST UNDER the middle whatever you
    /// do, because that is what a thermostat is FOR — and every way to move it
    /// off there is a real one:
    ///
    ///   * sit still with your foot in it, or with a dead FAN,
    ///   * hold it on the limiter (see <see cref="HeatRedline"/>),
    ///   * drive a crusted RADIATOR hard,
    ///   * lose your coolant through a soft HOSE, which is the slow one,
    ///   * or simply race on the hottest afternoon of July.
    ///
    /// And ignoring it costs the engine. Past <see cref="RedMark"/> the needle
    /// is in the red, the engine starts losing power, and condition is coming
    /// off it every second; past <see cref="SeizeFromC"/> it can let go
    /// outright, which ends the drive and leaves a car that needs a rebuild or
    /// a different engine. That is the whole point of a gauge: it is the only
    /// warning the game gives before a bill it cannot un-ring.
    ///
    /// Calibrated, not guessed — see PSXRacing_selftest_log.txt, which runs
    /// this exact integration over a dozen scenarios and pins the settle
    /// points. A healthy car cruising settles at 88; flat out at 200 km/h,
    /// 89, because more airflow arrives with the extra heat; stationary at
    /// full throttle it runs away, which it should.
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
        /// on the grid has not been running. Overwritten by
        /// <see cref="StartCold"/> once the scene knows what the weather is.
        /// </summary>
        public float celsius = 18f;

        /// <summary>What is in the system, 0-100. Falls through the hoses and
        /// over the cap; the same unit <see cref="LifeSim.OwnedCar.coolant"/>
        /// carries, so the handoff never converts.</summary>
        public float coolantPct = 100f;

        // ---- the hardware (set by RaceHandoffApplier from the owned car) ----
        /// <summary>Radiator core condition, 0-100.</summary>
        public float radiatorCond = 100f;
        /// <summary>Fan condition, 0-100. Under 20 it is not turning.</summary>
        public float fanCond = 100f;
        /// <summary>Hoses, clamps and cap, 0-100. What holds the coolant in.
        /// </summary>
        public float hoseCond = 100f;

        /// <summary>
        /// What is left of the cooling system after the FAULT lane, 0-1. Comes
        /// off the fault aggregate via <see cref="RaceHandoff.CoolMult"/>:
        /// cooling_fail is the fault whose own description in the catalog is
        /// "Overheating risk", and this is where that stops being a line of
        /// text. Multiplies the hardware above rather than replacing it — a
        /// diagnosed cooling failure on top of a tired core is worse than
        /// either.
        /// </summary>
        public float coolMult = 1f;

        /// <summary>Outside air. The calendar's, not a constant — see
        /// <see cref="CoolingModel.AmbientC"/>.</summary>
        public float ambientC = 18f;

        /// <summary>How worn the engine already is, 0-1 where 1 is a fresh
        /// one. Does not change the temperature; changes how well the engine
        /// SURVIVES it. A tired motor lets go sooner.</summary>
        public float engineHealth = 1f;

        // ---- the scale ----------------------------------------------------
        /// <summary>Left end of the gauge. The needle sits on C below this,
        /// which is where a cold engine's needle sits.</summary>
        public const float ColdMark = 50f;
        /// <summary>Right end. Off the scale, not merely warm.</summary>
        public const float HotMark = 140f;
        /// <summary>Where the thermostat holds a healthy engine. Deliberately
        /// NOT the middle of the scale: the ends are 50 and 140, so 90 sits at
        /// 0.44 of the sweep — just below centre, which is where the needle
        /// sits in a car that is fine and where a driver's eye learns to expect
        /// it. A gauge whose normal reading is dead centre has nowhere to put
        /// "a bit warm".</summary>
        public const float Normal = 90f;
        /// <summary>Where the red band starts, and the same number the engine
        /// starts taking damage at — so what the face shows and what the model
        /// does cannot disagree. 0.69 of the sweep.</summary>
        public const float RedMark = 112f;

        /// <summary>Needle position, 0 = C, 1 = H.</summary>
        public float Gauge => Mathf.Clamp01((celsius - ColdMark) / (HotMark - ColdMark));

        /// <summary>Where the red band starts on that same 0-1 sweep, for the
        /// face bake.</summary>
        public const float RedFrac = (RedMark - ColdMark) / (HotMark - ColdMark);

        /// <summary>Past the point a driver should be lifting: damage is
        /// accruing and the engine is down on power.</summary>
        public bool Overheating => celsius > RedMark;

        /// <summary>Warm enough to be worth a word from the HUD before it is a
        /// problem — the hint that something is wrong while there is still
        /// time to do something about it.</summary>
        public bool RunningHot => celsius > Normal + 12f;

        /// <summary>The engine has let go. Terminal for this drive: the throttle
        /// is dead (see <see cref="PlayerCarInput"/>) and the car is going home
        /// on a truck.</summary>
        public bool Seized { get; private set; }

        /// <summary>Power the heat has taken away, multiplied into the
        /// controller's traction every frame. This is what makes the gauge
        /// something a driver FEELS before they read it.</summary>
        public float PowerMult { get; private set; } = 1f;

        // ---- what the drive has cost so far (stamped into the handoff) ----
        /// <summary>Hottest it got. The number the result screen reports.</summary>
        public float PeakC { get; private set; }
        /// <summary>Seconds spent past <see cref="RedMark"/>. Ages the cooling
        /// hardware in the apply-back as well as the engine.</summary>
        public float OverheatSeconds { get; private set; }
        /// <summary>Engine condition points burned off by heat, 0-100 scale —
        /// the same scale <see cref="LifeSim.OwnedCar.engine"/> is in.</summary>
        public float EngineDamage { get; private set; }

        // ---- the model ----------------------------------------------------
        /// <summary>Heat with the throttle shut, in degrees per second against
        /// a cold block.</summary>
        const float HeatIdle = 0.55f;
        /// <summary>Extra heat at full load.</summary>
        const float HeatLoad = 1.60f;
        /// <summary>And what the last of the rev range adds on top.
        /// Peak power is peak heat, and an engine held against the limiter is
        /// making everything it has with no more air going through the radiator
        /// than it had at 4,000 rpm. The one heat term the driver controls
        /// directly, which is why it is here and not folded into load.</summary>
        const float HeatRedline = 0.55f;
        /// <summary>Fraction of the usable band the redline term starts at.</summary>
        const float RedlineFrom = 0.90f;

        /// <summary>Losses that do not go through the radiator: the block, the
        /// oil, the exhaust. Always on, which is why a car with the thermostat
        /// shut still cannot heat forever — and why one with NO coolant still
        /// takes a little while to destroy itself rather than none at all.
        /// </summary>
        const float LossBlock = 0.006f;
        /// <summary>Radiator authority per unit of airflow.</summary>
        const float LossRad = 0.0135f;

        /// <summary>Airflow through the core with nothing moving it — what gets
        /// past a stopped car on its own. Small on purpose: this is the number
        /// that decides how bad a dead fan is, and a dead fan in traffic has to
        /// be bad or the fan is not a part.</summary>
        const float AirBase = 0.12f;
        /// <summary>What a working fan is worth on top.</summary>
        const float AirFan = 0.73f;
        /// <summary>Ram air per km/h. Takes the MAXIMUM with the fan rather
        /// than adding to it, because that is the physical truth — above about
        /// 60 km/h the air coming through the grille is already more than the
        /// fan could pull, and a fan that kept adding at 200 km/h would make
        /// the part that fails in traffic matter most on the motorway.</summary>
        const float AirPerKmh = 1f / 89f;
        const float AirMax = 2.5f;

        /// <summary>Thermostat opening band. Shut below the first, wide open
        /// above the second.</summary>
        const float ThermoShut = 82f, ThermoOpen = 94f;

        // ---- damage -------------------------------------------------------
        /// <summary>Engine condition per second at 15 C into the red, scaled by
        /// how far in it is. A minute at 127 costs about five points — a
        /// tune-up's worth — and a minute at 142 costs fourteen.</summary>
        const float DamagePerSec = 0.08f;
        /// <summary>Temperature at which an engine can let go outright rather
        /// than merely wear.</summary>
        public const float SeizeFromC = 130f;
        /// <summary>
        /// Chance per second of a hard failure at 20 C past that, on a FRESH
        /// engine, rising with the SQUARE of how far past. A minute pinned at
        /// 150 kills one nineteen times in twenty; a minute at 140 is a coin
        /// toss; 135 is a risk you can take and usually get away with, and
        /// just over the line is very nearly free.
        ///
        /// Sized against what the player has already been shown by the time
        /// they are here: a needle in a red band, a blinking lamp, and a car
        /// visibly down on power. Three ignored warnings is enough — and the
        /// square is what makes BACKING OFF work, because the difference
        /// between 150 and 135 is a factor of sixteen rather than of one and a
        /// half.
        /// </summary>
        const float SeizePerSec = 0.05f;
        /// <summary>How much more likely a completely worn-out engine is to let
        /// go than a new one.</summary>
        const float WornFragility = 1.5f;
        /// <summary>Power left at <see cref="SeizeFromC"/>. The engine is
        /// pulling timing and boiling its oil; it does not feel like it did.
        /// </summary>
        const float HotPowerMult = 0.55f;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
        }

        /// <summary>
        /// Put the engine where a parked car's engine is: at whatever the air
        /// is, with a full needle-sweep's worth of warming up to do.
        ///
        /// Called by the applier once the ambient is known. It matters that
        /// this is a METHOD and not a field default: the scene builder adds
        /// this component with 18 C baked in, and a January dawn race that
        /// started at 18 would have the needle leave C before the lights did.
        /// </summary>
        public void StartCold(float ambient)
        {
            ambientC = ambient;
            celsius = ambient;
            PeakC = ambient;
            Seized = false;
            PowerMult = 1f;
            OverheatSeconds = 0f;
            EngineDamage = 0f;
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

            Tick(dt, Load(pedal, revs), OverRev(pedal, revs), car.speedKmh);
            car.heatAccelMult = PowerMult;
        }

        /// <summary>How hard the engine is working, 0.12 (shut) to 1.</summary>
        public static float Load(float pedal, float revs) =>
            Mathf.Clamp01(0.12f + 0.88f * Mathf.Clamp01(pedal) * Mathf.Clamp01(revs));

        /// <summary>How far into the last of the rev range, 0-1. On the
        /// THROTTLE only: coasting down through the same revs at a closed
        /// throttle is the coolest an engine ever is, and a term that ignored
        /// the pedal would charge a driver for every downshift.</summary>
        public static float OverRev(float pedal, float revs) =>
            Mathf.Clamp01(pedal) * Mathf.InverseLerp(RedlineFrom, 1f, revs);

        /// <summary>
        /// One step of the model on numbers, with the car left out of it.
        ///
        /// Separated from <see cref="Update"/> so the self-test can integrate
        /// the real thing — the same constants, the same order, the same Euler
        /// step — over a cruise, a standstill, a dead fan and a blocked core
        /// and assert where each one settles. The calibration claims in this
        /// file's summary are that test's output, not an estimate, and they stay
        /// true because it runs on every build.
        /// </summary>
        public void Tick(float dt, float load, float overRev, float speedKmh)
        {
            // A seized engine is not making heat any more. It is still hot, and
            // it still cools through the same radiator, which is why this is a
            // load of zero rather than an early return — and why it is HERE
            // rather than in Update, which is only one of the callers: a model
            // that kept making full power after it died would climb off the
            // scale for as long as anything kept stepping it.
            if (Seized) { load = 0f; overRev = 0f; }

            // ---- airflow: the fan OR the road, whichever is more ----------
            float fanAir = AirFan * CoolingModel.FanEff(fanCond);
            float ramAir = Mathf.Abs(speedKmh) * AirPerKmh;
            float air = Mathf.Min(AirMax, AirBase + Mathf.Max(fanAir, ramAir));

            float thermo = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThermoShut, ThermoOpen, celsius));
            float rad = CoolingModel.RadEff(radiatorCond) *
                        CoolingModel.CoolantEff(coolantPct) *
                        Mathf.Clamp(coolMult, 0.05f, 1f);

            float heat = HeatIdle + HeatLoad * load + HeatRedline * Mathf.Clamp01(overRev);
            float loss = (LossBlock + LossRad * air * thermo * rad) * (celsius - ambientC);

            // Explicit Euler at 60 Hz on a system whose fastest time constant
            // is tens of seconds — the step is four orders of magnitude inside
            // stability, so there is nothing to integrate more carefully.
            celsius = Mathf.Max(ambientC, celsius + (heat - loss) * dt);
            if (celsius > PeakC) PeakC = celsius;

            LoseCoolant(dt);
            TakeDamage(dt);
        }

        /// <summary>
        /// What the system gives up while it is hot: through the hoses first,
        /// and then past the cap once it is boiling.
        ///
        /// The second term is the one that makes an overheat a SPIRAL rather
        /// than a plateau. Below the boil it is only ever the hoses, so a car
        /// with good rubber can be driven hot all day and still have a full
        /// system — which is exactly the car that gets away with it.
        /// </summary>
        void LoseCoolant(float dt)
        {
            if (coolantPct <= 0f) { coolantPct = 0f; return; }
            float lost = 0f;
            if (celsius > CoolingModel.WeepAboveC)
                lost += CoolingModel.HoseWeepPerSec * CoolingModel.HoseLeak(hoseCond) *
                        Mathf.Min(2f, (celsius - CoolingModel.WeepAboveC) / 20f);
            if (celsius > CoolingModel.BoilAboveC)
                lost += CoolingModel.BoilPerSec *
                        Mathf.Min(2f, (celsius - CoolingModel.BoilAboveC) / 20f);
            if (lost > 0f) coolantPct = Mathf.Max(0f, coolantPct - lost * dt);
        }

        /// <summary>
        /// What being in the red costs: power now, condition permanently, and —
        /// past <see cref="SeizeFromC"/> — the engine.
        ///
        /// The power cut happens BEFORE the damage the player can see, on
        /// purpose. A gauge you have to read to know about is a gauge that gets
        /// ignored; a car that has quietly stopped pulling is one the driver
        /// notices in the next corner and then looks down.
        /// </summary>
        void TakeDamage(float dt)
        {
            if (Seized) { PowerMult = 0f; return; }

            PowerMult = Mathf.Lerp(1f, HotPowerMult,
                Mathf.InverseLerp(RedMark, SeizeFromC, celsius));

            if (celsius <= RedMark) return;
            OverheatSeconds += dt;
            EngineDamage += DamagePerSec *
                            Mathf.Pow((celsius - RedMark) / 15f, 1.5f) * dt;

            if (celsius <= SeizeFromC) return;
            // A worn engine has less of everything standing between it and this
            // — bearing clearance, ring seal, a head that has been off before.
            float fragility = 1f + (1f - Mathf.Clamp01(engineHealth)) * WornFragility;
            float over = (celsius - SeizeFromC) / 20f;
            if (Random.value < SeizePerSec * over * over * fragility * dt) Seize();
        }

        /// <summary>
        /// It let go. Separate from the roll so the debug bench and the
        /// self-test can do it on demand.
        /// </summary>
        public void Seize()
        {
            if (Seized) return;
            Seized = true;
            PowerMult = 0f;
            // Whatever was in it is on the road. Nothing reads this — the
            // engine is already dead — but a car towed home with a full
            // overflow tank after a seizure would be a lie the garage screen
            // would then print.
            coolantPct = 0f;
            // AND IT GOES INTO THE SAVE NOW, not at the exit with the rest of
            // the result. The exit's result can be thrown away — abandoning a
            // race clears it, restarting one clears it — and those are the two
            // things a player presses when their engine dies a mile from the
            // flag. See LifeRules.BankSeizureNow.
            LifeSim.LifeRules.BankSeizureNow();
        }

        /// <summary>Put coolant in, out on the road. Nothing calls this yet —
        /// there is no jug in the boot — but the gauge's one recoverable
        /// failure ought to have a door left open for it.</summary>
        public void TopUp(float pct) => coolantPct = Mathf.Clamp(coolantPct + pct, 0f, 100f);

        /// <summary>
        /// Bank what the heat cost into the handoff, on the way out of a scene.
        ///
        /// Static and taking the car rather than living at each exit, because
        /// there are TWO exits — the finish line and the pause menu's EXIT in
        /// free roam — and a heat model that only one of them reported would
        /// mean an engine cooked on the way to a race survived it and one
        /// cooked during the race did not. The tank is stamped twice in exactly
        /// this shape for exactly this reason.
        /// </summary>
        public static void StampResult(CarController car)
        {
            var temp = car != null ? car.GetComponent<EngineTemp>() : null;
            if (temp == null) return;
            RaceHandoff.HeatReported = true;
            RaceHandoff.PeakCelsius = temp.PeakC;
            RaceHandoff.OverheatSeconds = temp.OverheatSeconds;
            RaceHandoff.HeatEngineDamage = temp.EngineDamage;
            RaceHandoff.EndCoolantPct = temp.coolantPct;
            RaceHandoff.EngineSeized = temp.Seized;
        }
    }
}
