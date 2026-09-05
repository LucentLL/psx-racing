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
        /// <summary>Rank 10..1 when this race is a blacklist challenge, else 0.
        /// The apply-back records the defeat off this, so it has to survive the
        /// scene load with the result.</summary>
        public static int RivalRank;
        public static string RivalAlias;

        // ---- tuning stages (the parts the player bought) ----
        // Passed as stages rather than as finished hp/kg numbers so the race
        // scene derives the effective car through the same Upgrades curves the
        // garage quoted from — two places computing "what a stage-3 build is
        // worth" is how a shop screen and a stopwatch start disagreeing.
        public static int UpPower, UpWeight, UpBrakes, UpSuspension, UpTires;
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
        /// by <see cref="EngineTemp"/>.</summary>
        public static float CoolMult = 1f;
        public static bool HideGauges;
        public static bool RpmFlutter;

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
            DamageScore = 0f;
            HardHits = 0;
            CargoCondition = 1f;
            CargoReported = false;
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
            FreeRoam = false; FreeRoamPlace = null;
            Delivery = false; DeliveryPay = 0; Solo = false;
            CarryCondition = 1f;
            TestDrive = false; TestDriveKey = null;
            OrderToppings = null; OrderBoxes = 1; OrderBottles = 0;
            StartFuelPct = 100f;
            OpponentSpecIds = OpponentSkills = null;
            RivalRank = 0; RivalAlias = null;
            UpPower = UpWeight = UpBrakes = UpSuspension = UpTires = 0;
            // A payload left out of here does not go stale, it goes to the NEXT
            // car: these statics survive a scene load by design, so the tune the
            // player set on one car would silently be applied to another.
            Setup = null;
            Welded = Supercharged = false;
            AccelMult = GripMult = BrakeMult = ShiftMult = FuelMult = CoolMult = 1f;
            SteerPull = 0f; HideGauges = false; RpmFlutter = false;
            ClearResult();
        }
    }
}
