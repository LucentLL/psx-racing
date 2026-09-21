namespace PSXRacing
{
    /// <summary>
    /// Static mailbox between the LifeSim menu scene and the race scene.
    /// Statics survive a scene load (they die on domain reload / app restart,
    /// which is fine: a race abandoned mid-way through an app kill just never
    /// happened, same as RG2's interim-save behavior).
    ///
    /// The LifeSim fills the request half before loading the race; RaceManager
    /// stamps the result half when the player finishes. Applying the result to
    /// LifeState (payout, odometer, wear, fuel, time-of-day) happens back in
    /// the menu scene, so all economy rules live in one place.
    /// </summary>
    public static class RaceHandoff
    {
        // ---- request: filled by the LifeSim before loading the race ----
        /// <summary>True when the race was entered from Home. When false the
        /// race scene behaves as the standalone demo it was before the
        /// LifeSim existed (direct scene play in the editor still works).</summary>
        public static bool FromLifeSim;
        /// <summary>Owned-car instance id, for odometer/wear on the way back.</summary>
        public static string CarId;
        /// <summary>Catalog key the race scene specs the player's car from.
        /// Empty means "leave the built-in RX-7 spec alone".</summary>
        public static string CarSpecId;
        /// <summary>The livery the player's own car is wearing, by baked name,
        /// or empty for the factory answer. The paint shop writes it onto the
        /// OwnedCar; this is how it reaches the grid. See LifeSim.Paint.</summary>
        public static string CarPaintSkin;
        /// <summary>Purse for winning, from the LifeSim's race offer.</summary>
        public static int PurseWin;
        public static int PurseSecond;
        public static int PurseThird;
        /// <summary>Index into <see cref="TimeOfDay.All"/> — dawn through night.
        /// Was a three-way morning/afternoon/night slot; the LifeSim still only
        /// has three activity slots, but it now picks an HOUR out of the band
        /// that slot covers, so two morning races on different days do not look
        /// identical.</summary>
        public static int TimeOfDayIndex = TimeOfDay.Sunset;
        /// <summary>
        /// The LifeSim's absolute day, for the season and the weather. Stamped
        /// by the home screen every time it draws itself, and DELIBERATELY not
        /// touched by ClearAll: it is the state of the world, not a payload
        /// for one car, and a scene that loads with it at zero (the editor
        /// pressing Play) gets the fall every scene was baked as. See
        /// <see cref="Seasons"/>.
        /// </summary>
        public static int CalendarDay;
        /// <summary>
        /// The debug bench's weather, as a <see cref="Weather"/> value, or -1
        /// for the calendar's own. <see cref="Seasons.CurrentWeather"/> reads
        /// it first, so everything that asks the weather — the fog band, the
        /// sky, the tyres, the AI's braking, the rain itself — gets the one
        /// answer. It rides with the REQUEST (ClearAll resets it, the home
        /// screen resets it) and survives a scene load on purpose: RESTART
        /// RACE comes back under the sky the tester left it under, the same
        /// contract the bench keeps for the car.
        /// </summary>
        public static int WeatherOverride = -1;
        /// <summary>Which circuit, as an index into
        /// <see cref="TrackCatalog.All"/>. The scene is loaded from this, so it
        /// is the one field that decides where the car ends up.</summary>
        public static int TrackIndex;
        public static bool IsPractice;
        /// <summary>A Charlotte free-roam session rather than a race. Stamped
        /// by CityMode on exit (there is no finish line to stamp it), and the
        /// apply-back banks metres/fuel/wear but pays no purse and moves no
        /// rep — a drive is not a result.</summary>
        public static bool FreeRoam;
        /// <summary>What the free-roam session calls the place it happened in,
        /// for the line the apply-back writes into the diary. Stamped by
        /// CityMode on the way out — the town is not a TrackCatalog entry, so
        /// there is no index to look a name up from.</summary>
        public static string FreeRoamPlace;

        /// <summary>This run is a paid delivery, not a race: the player picked
        /// an order up at the shop and the finish line is the customer's door.
        /// No purse, no rep, no rivals — <see cref="DeliveryPay"/> instead, paid
        /// on arrival.</summary>
        public static bool Delivery;
        /// <summary>What the drop is worth, rolled at the shop so the player
        /// can be told before they set off. Tips, so it swings.</summary>
        public static int DeliveryPay;
        /// <summary>What was left of the pizza when the RACE began, 0-1. The
        /// order rides across town on the passenger seat before the run
        /// starts, and a box thrown into the footwell on Main Street must not
        /// arrive graded as fresh out of the oven: the drop is scored against
        /// the WORSE of this and the race's own leg.</summary>
        public static float CarryCondition = 1f;
        /// <summary>Whether the car HIT anything on the way across town with
        /// the order aboard. The other half of <see cref="CarryCondition"/>,
        /// and carried for the same reason: a refusal needs an impact (see
        /// LifeRules.ScoreDelivery), and the one that ruined the order may
        /// have been on Main Street.</summary>
        public static bool CarryHit;
        /// <summary>
        /// How far round the lap the customer's door is, as a fraction of a
        /// circuit, rolled at the counter. A delivery is a SPRINT: on a loop
        /// circuit RaceManager puts the finish part-way round instead of
        /// counting laps, and the par the tip is graded against is sized to
        /// the same fraction — LifeRules.DeliveryMeters is the one place both
        /// read it from. 1 (the default and the cleared value) is a full lap,
        /// which is what an old ticket with no fraction on it gets. Ignored on
        /// a route with ENDS, where the baked finish already is the door.
        /// </summary>
        public static float DeliveryDropFraction = 1f;
        /// <summary>
        /// The speed the car is already doing when the race scene opens, km/h
        /// — the venue's speed limit, TrackDef.speedLimitKmh. Above zero the
        /// grid starts ROLLING: no starter, no countdown, the controls live on
        /// frame one. Zero (the default and the cleared value) is the standing
        /// start every race has always had, and a synthetic strip keeps it
        /// whatever this says — the tree is the event there.
        /// </summary>
        public static float RollingStartKmh;
        /// <summary>This free-roam exit is one leg of a longer errand — the
        /// drive to work, or the loaded drive to the junction — and the slot
        /// it would normally cost is the SHIFT's to spend. Without this the
        /// commute charged a second slot on top of the one the shop door
        /// takes, and a single delivery ate two thirds of the day.</summary>
        public static bool CommuteLeg;

        /// <summary>
        /// This run is a TEST DRIVE of a car the player does not own.
        ///
        /// The whole point of it is that the drive tells you something an
        /// inspection cannot — 21 of the 36 used-car faults are only findable
        /// at speed — so the car really is the seller's car, with the seller's
        /// problems on it. Nothing is banked on the way back: no odometer, no
        /// wear, no fuel, no purse, no rep. What comes back is what the drive
        /// revealed, and that lives on the visit rather than on any owned car.
        /// </summary>
        public static bool TestDrive;
        /// <summary>Which visit this drive belongs to
        /// (<see cref="LifeSim.Viewings.KeyOf"/>). A test drive is a scene
        /// round trip, so the return path has to be able to find its way back
        /// to the car it was about.</summary>
        public static string TestDriveKey;
        /// <summary>
        /// The order itself: one topping index per BOX, in stacking order,
        /// bottom first. Indexes into PizzaCargoBaker.Toppings, so the array is
        /// append-only.
        ///
        /// It has to cross the scene load because the cargo is a real object
        /// now — the boxes the player picked up at the counter are the boxes
        /// that ride on the passenger seat, and "three, and the top one is
        /// pepperoni" is not something the race scene can re-derive.
        /// </summary>
        public static int[] OrderToppings;
        /// <summary>How many boxes are in the order. Kept beside the array
        /// rather than read off its length so a handoff with no array (a scene
        /// played standalone) still says one.</summary>
        public static int OrderBoxes = 1;
        /// <summary>Two litre bottles riding with the order. Physical on the
        /// seat, never scored.</summary>
        public static int OrderBottles;
        /// <summary>Retire the whole AI field. An EMPTY OpponentSpecIds does
        /// NOT mean this — ApplyField reads an empty list as "leave the grid as
        /// the track authored it", which is four cars. Solo has to be asked for
        /// out loud.</summary>
        public static bool Solo;
        /// <summary>Tank level the car arrives with, 0-100. The race scene's
        /// <see cref="FuelTank"/> starts here and burns down in real time, so
        /// the pre-race gate and the gauge on the dash read the same number.
        /// </summary>
        public static float StartFuelPct = 100f;

        // ---- the field (P2) ----
        // The scene builds a fixed four-car grid; these two parallel ';'-joined
        // lists respec it. Strings rather than arrays because these are statics
        // crossing a scene load and one field is one thing to clear — but they
        // are STRICTLY parallel: OpponentSpecIds decides how many cars race and
        // OpponentSkills indexes off it.
        /// <summary>Catalog ids for the AI cars, nearest grid slot first. Empty
        /// leaves the built-in RX-7 field alone. Fewer entries than the grid has
        /// cars retires the spares — which is how a 1v1 rival race happens on a
        /// track built for four.</summary>
        public static string OpponentSpecIds;
        public static string OpponentSkills;

        // ---- blacklist challenge (L4) ----
        /// <summary>The rung being raced FOR, for the banner over the finish
        /// line. Display only, and a snapshot: the board moves overnight, so
        /// the apply-back records the leg against the NAME.</summary>
        public static int RivalRank;
        /// <summary>Who this leg is against, and the key the ladder banks the
        /// result under — so it has to survive the scene load with the result.
        /// Empty when the race is not a challenge leg.</summary>
        public static string RivalAlias;
        /// <summary>"RACE 2 OF 3 · 1-0" as the series stood at the start line.
        /// A challenge is best of three now, so "DEFEATED" over the finish line
        /// would be a lie two races out of three.</summary>
        public static string RivalSeries;

        // ---- car meet challenge ----
        /// <summary>
        /// This race was started by walking up to somebody's car at a CAR MEET
        /// and calling them out (see LifeSim.CarMeets). It is a street race in
        /// every respect but two, and both are what this flag is for: it does
        /// not spend an activity block — the night at the meet is ONE block,
        /// paid when the player finally drives home, however many people they
        /// lined up against — and it does not burn the one-purse-race-a-day
        /// cap, the same exemption RG2's meet challenges have.
        /// </summary>
        public static bool MeetRace;
        /// <summary>Who was called out, for the result line. Empty for a
        /// blacklist rival found at the meet, who has <see cref="RivalAlias"/>.
        /// </summary>
        public static string MeetAlias;

        // ---- tuning stages (the parts the player bought) ----
        // Passed as stages rather than as finished hp/kg numbers so the race
        // scene derives the effective car through the same Upgrades curves the
        // garage quoted from — two places computing "what a stage-3 build is
        // worth" is how a shop screen and a stopwatch start disagreeing.
        public static int UpPower, UpWeight, UpBrakes, UpSuspension, UpTires;
        /// <summary>The passenger seat's stage, read by PizzaCargo when it
        /// stands the order up. Rides alongside the rest so the town drive and
        /// the race hand the cargo the same seat.</summary>
        public static int UpSeat;
        /// <summary>One-off bolt-ons: welded rear diff, Roots blower.</summary>
        public static bool Welded, Supercharged;
        /// <summary>
        /// The driver's advanced tune, ALREADY GATED. The menu sanitizes it
        /// against the parts this car actually carries before it crosses, so the
        /// race scene applies it blindly and the unlock rule lives in exactly one
        /// place. Null on a standalone editor race and on a car with nothing
        /// fitted — which is a car that drives as it always did.
        ///
        /// One field rather than thirty statics, for the reason at the top of
        /// this file: one field is one thing to remember to clear.
        /// </summary>
        public static CarSetup Setup;
        // Fault-effect handicaps the race scene applies to the player car
        // (ComputeFaultEffects). All neutral by default so the standalone
        // race is untouched.
        public static float AccelMult = 1f;
        public static float GripMult = 1f;
        public static float BrakeMult = 1f;
        public static float SteerPull;         // signed
        public static float ShiftMult = 1f;
        public static float FuelMult = 1f;
        /// <summary>How much of the cooling system still works, 0-1. Derived
        /// from the fault aggregate's engineWearMult: a fault that eats an
        /// engine is a fault that runs it hot, and cooling_fail — whose entry
        /// in the catalog reads "Overheating risk" — is the worst of them. Read
        /// by <see cref="EngineTemp"/>, which multiplies it by the hardware
        /// below rather than being told one number.</summary>
        public static float CoolMult = 1f;
        public static bool HideGauges;
        public static bool RpmFlutter;

        // ---- the cooling system, as PARTS (see CoolingModel) ----
        // A single "how bad is it" multiplier can only make the needle sit
        // higher. These four make it sit higher in a DIFFERENT WAY each, which
        // is the difference between a temperature gauge and a diagnosis.
        /// <summary>Radiator core condition, 0-100.</summary>
        public static float RadiatorCond = 100f;
        /// <summary>Fan condition, 0-100. The part that only matters slowly.</summary>
        public static float FanCond = 100f;
        /// <summary>Hoses, clamps and cap, 0-100 — what holds the coolant in.</summary>
        public static float HoseCond = 100f;
        /// <summary>What is in the system at the start of the drive, 0-100.
        /// Carried across because a leak does not refill itself overnight: a
        /// car parked half empty goes out half empty.</summary>
        public static float CoolantPct = 100f;
        /// <summary>The engine's own condition, 0-100. Not a cooling part —
        /// what it decides is how well the engine SURVIVES being cooked.</summary>
        public static float EngineCond = 100f;

        // ---- result: stamped by RaceManager when the player finishes ----
        public static bool ResultReady;
        public static int FinishPos;
        public static int FieldSize;
        public static float RaceTimeSeconds;
        public static float BestLapSeconds;
        /// <summary>Speed through the traps on a drag strip, km/h. Zero on a
        /// circuit — an ET without a trap speed is half a drag result.</summary>
        public static float TrapSpeedKmh;
        public static float MetersDriven;
        public static float DriftSeconds;
        /// <summary>Tank level the car finishes with. AUTHORITATIVE when
        /// <see cref="FuelReported"/> is set: the apply-back writes it straight
        /// onto the owned car instead of re-deriving a burn from the distance,
        /// because a car that stopped at the pumps did not burn what its
        /// mileage says it did.</summary>
        public static float EndFuelPct;
        /// <summary>Whether a live tank actually ran this race. False on an old
        /// scene with no FuelTank, where the distance-derived burn is still the
        /// only answer available.</summary>
        public static bool FuelReported;

        // ---- result: what the coolant gauge did ----
        /// <summary>Whether a live <see cref="EngineTemp"/> ran. False on an
        /// old scene with no model in it, where the apply-back has nothing to
        /// bank and does nothing — heat damage is never GUESSED from distance
        /// the way fuel can be, because there is no honest average for it.
        /// </summary>
        public static bool HeatReported;
        /// <summary>Hottest the coolant got, Celsius. The number the result
        /// screen prints — and the only evidence a player gets that the race
        /// they just won cost them an engine.</summary>
        public static float PeakCelsius;
        /// <summary>Seconds spent in the red. Ages the cooling hardware as
        /// well as the engine: a car driven home hot has worse hoses than one
        /// that was not.</summary>
        public static float OverheatSeconds;
        /// <summary>Engine condition points burned off by heat, on the 0-100
        /// scale <see cref="LifeSim.OwnedCar.engine"/> is in. ON TOP of the
        /// ordinary per-metre wear, not instead of it.</summary>
        public static float HeatEngineDamage;
        /// <summary>Coolant left, 0-100 — AUTHORITATIVE, like the tank. Nothing
        /// re-derives a leak from distance; only the model that ran knows.
        /// </summary>
        public static float EndCoolantPct = 100f;
        /// <summary>The engine let go out there. The car is not driveable until
        /// somebody rebuilds it or puts a different one in.</summary>
        public static bool EngineSeized;
        /// <summary>Dollars left at the pumps this race. Already taken out of
        /// the wallet by <see cref="GasPump"/> — a receipt, not a bill.</summary>
        public static int FuelSpent;
        /// <summary>Accumulated impact energy from CollisionResponder. Drives the
        /// body/paint damage and the impact-cause fault roll in the apply-back.
        /// Roughly: closing speed in m/s summed over hits, scrapes weighted down.</summary>
        public static float DamageScore;
        /// <summary>Discrete heavy impacts. Feeds the driving record (at-fault
        /// incidents) rather than the repair bill, which DamageScore covers.</summary>
        public static int HardHits;

        /// <summary>
        /// What is left of the pizza, 0-1, as the SIMULATION saw it — boxes
        /// tipped, lids off, slices on the floor.
        ///
        /// Authoritative over the damage-score estimate when
        /// <see cref="CargoReported"/> is set, and that is the point: a driver
        /// who clouts a wall dead square may keep every box flat on the seat,
        /// and one who never touches anything can still throw the lot into the
        /// footwell on a crest taken too fast. The impact tally was always a
        /// stand-in for this.
        /// </summary>
        public static float CargoCondition = 1f;
        /// <summary>Whether a PizzaCargo actually ran. False on an old scene,
        /// on a race that is not a delivery, and if the cargo prefabs are
        /// missing — where the DamageScore model is still the only answer
        /// available.</summary>
        public static bool CargoReported;
        /// <summary>Jolts the cargo took during the run itself — see
        /// PizzaCargo.Impacts. Only meaningful with <see cref="CargoReported"/>.
        /// </summary>
        public static int CargoImpacts;

        public static void ClearResult()
        {
            ResultReady = false;
            FinishPos = 0;
            FieldSize = 0;
            RaceTimeSeconds = 0f;
            BestLapSeconds = 0f;
            TrapSpeedKmh = 0f;
            MetersDriven = 0f;
            DriftSeconds = 0f;
            EndFuelPct = 0f;
            FuelReported = false;
            FuelSpent = 0;
            HeatReported = false;
            PeakCelsius = 0f;
            OverheatSeconds = 0f;
            HeatEngineDamage = 0f;
            EndCoolantPct = 100f;
            EngineSeized = false;
            DamageScore = 0f;
            HardHits = 0;
            CargoCondition = 1f;
            CargoReported = false;
            CargoImpacts = 0;
            // Rides with the RESULT rather than the request: it is stamped on
            // the way out of a scene and consumed by the one apply-back that
            // reads it, so a later, unrelated exit must start clean.
            CommuteLeg = false;
        }

        public static void ClearAll()
        {
            FromLifeSim = false;
            CarId = null;
            CarSpecId = null;
            CarPaintSkin = null;
            PurseWin = PurseSecond = PurseThird = 0;
            TimeOfDayIndex = TimeOfDay.Sunset; TrackIndex = 0; IsPractice = false;
            WeatherOverride = -1;
            FreeRoam = false; FreeRoamPlace = null;
            Delivery = false; DeliveryPay = 0; Solo = false;
            CarryCondition = 1f;
            CarryHit = false;
            // Both survive a scene load by design, so a delivery that left
            // either behind would hand the NEXT ordinary race a rolling start
            // or a finish part-way round its first lap.
            DeliveryDropFraction = 1f; RollingStartKmh = 0f;
            TestDrive = false; TestDriveKey = null;
            OrderToppings = null; OrderBoxes = 1; OrderBottles = 0;
            StartFuelPct = 100f;
            OpponentSpecIds = OpponentSkills = null;
            RivalRank = 0; RivalAlias = null; RivalSeries = null;
            MeetRace = false; MeetAlias = null;
            UpPower = UpWeight = UpBrakes = UpSuspension = UpTires = UpSeat = 0;
            // A payload left out of here does not go stale, it goes to the NEXT
            // car: these statics survive a scene load by design, so the tune the
            // player set on one car would silently be applied to another.
            Setup = null;
            Welded = Supercharged = false;
            AccelMult = GripMult = BrakeMult = ShiftMult = FuelMult = CoolMult = 1f;
            SteerPull = 0f; HideGauges = false; RpmFlutter = false;
            RadiatorCond = FanCond = HoseCond = CoolantPct = EngineCond = 100f;
            ClearResult();
        }
    }
}
