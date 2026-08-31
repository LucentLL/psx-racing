using System;
using System.Collections.Generic;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The persistent life-sim state — a C# mirror of Racing Game 2's save
    /// schema (src/save/schema.ts, SAVE_VERSION 9.0.0), minus the 2D-world
    /// fields (player x/y, home/office anchors) that a menu-based build has
    /// no use for.
    ///
    /// JsonUtility cannot serialize dictionaries, so the schema's per-car
    /// Record&lt;carId, ...&gt; maps collapse into OwnedCar entries that carry
    /// their own odometer/condition/upgrades. Field names otherwise track the
    /// TS schema so the two codebases stay greppable against each other.
    /// </summary>
    [Serializable]
    public class LifeState
    {
        /// <summary>Bump whenever a field is RENAMED or its meaning changes.
        /// JsonUtility leaves fields absent from the JSON at their initializer
        /// defaults — which is what makes adding fields free — but a renamed
        /// field silently resets with no error, so names are save-format API.
        /// v2: CarFault gained catalog fields (stat/cost/days/add/repairType).
        /// v3: OwnedCar gained specId/catalogPrice; the used-car market landed.
        /// v4: blacklist ladder + at-fault incident record.
        /// v5: toolbox + per-car inspection latches; hidden faults are real.
        /// v6: housing keys renamed apartment→house (start = 1-car-garage house).
        /// v7: faults rolled before the hidden layer landed are re-hidden on
        ///     cars nobody has inspected; OwnedCar gained proInspectDay.
        /// v8: the job book closed down to one job; absences count every day.
        /// v9: the salvage yard — LifeState gained junkyard, PendingPart gained
        ///     junkRisk. Both are ADDED, so the migration only exists to stock
        ///     the shelves an old career would otherwise open onto empty.
        /// v10: the town — LifeState gained dealerLot and viewings, both ADDED.
        ///     The migration exists for one thing that is NOT free: adjustable
        ///     aero became a race-car-only part, so a road car in an old save
        ///     can be carrying a kit it could no longer buy and can no longer
        ///     use. It is unfitted and refunded rather than left as a padlocked
        ///     row the player paid for.
        /// </summary>
        public int saveVersion = 10;

        // === Core economy / clock ===
        public int money;
        public int day = 1;              // absolute day counter; day 1 is a FRIDAY
        public int slotIndex;            // 0 morning / 1 afternoon / 2 night
        public int slotsActiveToday;     // non-rest slots burned since last sleep
        public bool workedToday;

        // === Race setup (remembered between races) ===
        /// <summary>Which circuit the next race runs on, indexing
        /// TrackCatalog.All. An ADDED field, so old saves default to 0 —
        /// the city circuit they have always raced.</summary>
        public int trackIndex;
        /// <summary>Hour every race runs at, indexing TimeOfDay.All. -1 means
        /// FOLLOW THE CLOCK — take whatever hour the current activity slot
        /// falls in, which is the default and the one that makes the day feel
        /// like a day. Anything else is the player overriding it, because
        /// wanting to drive at night on a Tuesday morning is a reasonable thing
        /// to want from a game with seven skies in it.</summary>
        public int raceTimeIndex = -1;

        // === Player identity ===
        public string playerName = "";
        public int age = 25;
        /// <summary>Career created under the TEST name — seeded with enough
        /// money to exercise the market, garage and tuning ladder without
        /// grinding a season first. An ADDED field, so JsonUtility leaves every
        /// existing save at false and no migration is needed.</summary>
        public bool debugMode;

        // === Garage ===
        public string activeCar = "";
        public List<OwnedCar> cars = new List<OwnedCar>();

        // === Health / fitness ===
        public float health = 100f;
        public int daysSinceEat;
        public int daysSinceSleep;
        public bool ateToday;
        public string lastMealTier = ""; // junk / regular / premium
        public int foodStock;
        public float fitness = 50f;

        // === Work / pay / rep ===
        public string playerJob = "";
        public int basePay;
        public float payMultiplier = 1f;
        public float workRep;   // 0 unemployed; 25 set on hire
        public int workDaysTotal;
        public int workDaysPresent;
        public int consecutiveAbsences;
        public bool fired;
        public int pendingSalary;

        // === Street racing ===
        public float streetRep;
        public int streetRacesTotal;
        public int streetRacesWon;
        /// <summary>Last race that counted against the one-purse-race-a-day cap.
        /// 0 = never.</summary>
        public int lastRaceDay;
        /// <summary>Last race of ANY kind, including practice-cap-exempt rival
        /// challenges. Rep decay reads this one: a player working through the
        /// blacklist is racing constantly and should not be losing rep for
        /// inactivity just because challenges do not burn the cap.</summary>
        public int lastAnyRaceDay;

        // === Blacklist ladder (L4) ===
        // Two int lists rather than the TS side's Record<rank, …>: JsonUtility
        // cannot serialize a dictionary, and rank is dense 1..10 anyway.
        /// <summary>Ranks the player has beaten. PERMANENT — rep decay can
        /// re-lock a rival you have not fought, but never un-beat one.</summary>
        public List<int> blDefeated = new List<int>();
        /// <summary>Ranks whose call-out has already fired. One-shot, so a gate
        /// that opens, closes to rep decay and reopens does not re-page.</summary>
        public List<int> blPaged = new List<int>();

        /// <summary>At-fault incidents on the driving record — heavy impacts,
        /// counted from the race scene's collision layer. Feeds the insurance
        /// premium; capped in the premium math, not here.</summary>
        public int atFaultIncidents;

        // === Housing / finance ===
        public string housingType = "";
        public int monthlyHousingCost;
        public int missedPayments;
        public int creditScore = 650;
        public int garageSlots = 1;
        public List<CarLoan> carLoans = new List<CarLoan>();
        public List<BankLoan> bankLoans = new List<BankLoan>();

        // === Skills ===
        public float mechSkill;

        /// <summary>Tool ids the player has bought (see Toolbox). Starter tools
        /// are NOT listed — they are owned by definition, so an old save with no
        /// list still has a floor jack.</summary>
        public List<string> tools = new List<string>();

        // === Repairs in progress ===
        public List<PendingPart> pendingParts = new List<PendingPart>();

        // === Used-car market ===
        public List<CarListing> newspaper = new List<CarListing>();
        public List<CarAd> carAds = new List<CarAd>();

        /// <summary>
        /// The dealership's own stock, out on the lot in town.
        ///
        /// A SECOND list rather than a filter on the paper, because the two
        /// markets behave differently and are supposed to: the lot never
        /// expires, discloses nothing, and will not hand you the keys, while
        /// the classifieds turn over every few days, admit to one problem in
        /// three and let you drive it. RG2 keeps them as two generators for the
        /// same reason (generateNewspaper vs generateCarLot).
        /// </summary>
        public List<CarListing> dealerLot = new List<CarListing>();

        /// <summary>
        /// Cars the player has gone to LOOK at but does not own.
        ///
        /// Each one carries a phantom <see cref="OwnedCar"/> with the faults
        /// that car really has — rolled the day you turned up, not the day you
        /// pay — so the inspection map, the X-ray and the fault list all work
        /// on a stranger's car with no second implementation, and buying is a
        /// MOVE rather than a copy. See <see cref="Viewings"/>.
        /// </summary>
        public List<Viewing> viewings = new List<Viewing>();

        /// <summary>
        /// Which visit the player is currently in the middle of
        /// (<see cref="Viewings.KeyOf"/>), or empty.
        ///
        /// In the SAVE rather than in a static, because a test drive is a
        /// scene round trip and a static does not survive one reliably — the
        /// same reason RaceHandoff exists at all. It is also what the seller's
        /// driveway reads to know which car to park on it.
        /// </summary>
        public string activeViewing = "";

        /// <summary>What is on the salvage yard's shelves. One flat list rather
        /// than three, because JsonUtility serialises a list of one type and not
        /// a list of lists — each part carries the shelf it is on
        /// (<see cref="YardPart.shelf"/>) and the page groups them.</summary>
        public List<YardPart> junkyard = new List<YardPart>();

        /// <summary>Wrecks already stripped by hand this week, as
        /// "week:index" keys (see <see cref="Junkyard.WreckPulled"/>). A pull
        /// that respawned on the next visit would be a free-parts fountain.
        /// ADDED field: an old save simply starts with every wreck intact.</summary>
        public List<string> yardPulls = new List<string>();

        // === Mail / log ===
        public List<MailItem> mail = new List<MailItem>();
        public List<string> calendarLog = new List<string>();

        /// <summary>Races the player has put in the diary. Bookings are the
        /// only thing in this save that points at a FUTURE day — everything else
        /// records what happened — which is what makes the calendar a planner
        /// rather than a history.</summary>
        public List<RaceBooking> bookings = new List<RaceBooking>();
        public OwnedCar FindCar(string id) =>
            cars.Find(c => c.id == id);

        public OwnedCar ActiveCar => FindCar(activeCar);
    }

    /// <summary>One owned car: identity plus everything the schema kept in
    /// per-car Records (carOdometers, carConditions, carUpgrades).</summary>
    [Serializable]
    public class OwnedCar
    {
        public string id;            // unique instance id (not the catalog key)
        public string displayName;
        /// <summary>Catalog key into CarCatalog — what the car actually IS.
        /// Empty on pre-v3 saves, which fall back to the built-in RX-7.</summary>
        public string specId = "";
        /// <summary>MSRP from the catalog. Every value formula scales off this,
        /// NOT off what the player paid — a good deal should not permanently
        /// devalue the car.</summary>
        public int catalogPrice;
        public float odoMiles;       // RG2 is miles-native; every formula reads miles
        public float fuel = 100f;    // tank percent, per car (schema: fuel)
        public int paidPrice;

        // Condition, 0-100. RG2's four stats are engine / tires / carHP
        // (chassis-body hit points) / paint; brakes and suspension exist only
        // as fault effects and upgrade stages, not as wear stats.
        public float engine = 100f;
        public float tires = 100f;
        public float carHP = 100f;
        public float paint = 100f;
        /// <summary>The livery this car has been RESPRAYED into, by baked
        /// name, or empty for "however it left the factory". A NAME rather
        /// than an index into CarModelDef.skinMaterials, because that array is
        /// rebuilt from the pack every time the model baker runs and a saved
        /// index would quietly become a different colour the day a livery was
        /// added. An ADDED field, so every existing save reads back empty —
        /// which is exactly right: nobody has painted anything yet. See
        /// <see cref="Paint"/>.</summary>
        public string paintSkin = "";

        public List<CarFault> faults = new List<CarFault>();

        // === Inspection (see LifeSim/Inspection.cs) ===
        /// <summary>Day an inspection was opened on this car. Re-entering the
        /// same day is free; a new day costs an activity slot and clears the
        /// per-sub latch below.</summary>
        public int inspectDay = -1;
        /// <summary>"comp:sub" keys already checked today. A failed roll has to
        /// stay failed until tomorrow, or the player just taps until the dice
        /// agree with them and the tools stop meaning anything.</summary>
        public List<string> inspectedSubs = new List<string>();
        /// <summary>Day the free look at the garage floor was taken.</summary>
        public int floorCheckedDay = -1;
        /// <summary>Day a mechanic or dealer last inspected this car. Paid
        /// inspections used to reveal faults without leaving any mark on the
        /// car, which meant the save had no record that anyone had ever looked
        /// - and a migration that has to reconstruct what the player knows has
        /// nothing to reconstruct it from. An ADDED field, so existing saves
        /// read back as 0; the v7 migration is what turns those into -1.</summary>
        public int proInspectDay = -1;
        /// <summary>How the car is standing right now: 0 on its wheels,
        /// 1 on jack stands, 2 on the two-post lift (see Toolbox.Raise). An
        /// ADDED field, so every existing save reads back as ON ITS WHEELS —
        /// which is where a car that nobody has jacked up ought to be.</summary>
        public int raised;

        // Upgrade stages 0-4 by category (H875/H879)
        public int upPower;
        public int upWeight;
        public int upBrakes;
        public int upSuspension;
        public int upTires;

        // One-off bolt-on mods. Separate from the stage ladder because they are
        // not a ladder — you either welded the diff or you did not, and neither
        // has a stage 2. RG2 keeps these as the same two booleans.
        /// <summary>Welded rear diff — the driven wheels break away together.</summary>
        public bool welded;
        /// <summary>Roots blower. Offered on naturally-aspirated cars only.</summary>
        public bool supercharged;

        // The six adjustable parts. Each one exists to UNLOCK a group of
        // sliders on the advanced-tuning screen rather than to change the car
        // by itself: fitting a coilover does nothing until you turn it. An
        // ADDED field is false on every existing save, which is exactly right —
        // nobody has fitted any of these yet.
        /// <summary>Adjustable anti-roll bars, front and rear.</summary>
        public bool swayBars;
        /// <summary>Quick rack: steering lock, rate and self-centring.</summary>
        public bool steeringRack;
        /// <summary>Plate-type limited-slip differential.</summary>
        public bool lsd;
        /// <summary>A crown wheel and pinion set — the final drive alone.</summary>
        public bool finalDriveSet;
        /// <summary>Close-ratio gear set — every individual ratio.</summary>
        public bool gearSet;
        /// <summary>Adjustable wing and splitter: downforce level and balance.</summary>
        public bool aeroKit;

        /// <summary>
        /// The driver's own advanced tune. Null until the player opens the
        /// setup screen, and null is a FACTORY setup — see <see cref="CarSetup"/>
        /// for why that is by construction rather than by a migration. Never
        /// read this directly; go through <see cref="CarSetupGate.SetupOf"/>,
        /// which builds one on demand.
        /// </summary>
        public CarSetup setup;
    }

    [Serializable]
    public class CarFault
    {
        public string id;            // fault catalog key (FaultCatalog pool id)
        public string label;
        public bool hidden = true;   // not yet revealed to the player
        public bool diagnosed;
        public float severity = 1f;

        // Baked from the catalog at roll time so a repair quote never has to
        // re-look-up the pool, and so a save survives a catalog edit.
        public string stat = "engine";   // engine / tires / hp
        public int cost;                 // dollars, already origin-multiplied
        public int days;                 // base shop days
        public int add;                  // stat percent restored on fix
        public string repairType = "mechanic";   // diy / delivery / mechanic
        /// <summary>Cached +/-1 so a pulling fault pulls the SAME way every
        /// race. Rolling the direction per race would read as a random defect
        /// rather than a broken alignment.</summary>
        public int pullDir = 1;
    }

    /// <summary>A repair in progress. Resolved by the daily rollover when
    /// <see cref="readyDay"/> arrives, which is what makes sleeping the way you
    /// pass time waiting on parts.</summary>
    [Serializable]
    public class PendingPart
    {
        public string carId;
        public string faultId;
        public string label;
        public string stat;
        public int add;
        public int readyDay;
        public int venue;        // 0 diy / 1 mechanic / 2 dealer

        /// <summary>Empty for a fault repair; otherwise the upgrade category key
        /// ("power", "weight", "brakes", "suspension", "tires") this job builds.
        /// A string rather than an enum ordinal so reordering the enum cannot
        /// silently re-point every queued job in every existing save. Older
        /// saves read back as "" — which is exactly right, since every job they
        /// hold IS a fault repair.</summary>
        public string upgradeKind = "";
        /// <summary>Stage this job installs, 1-4. Ignored when
        /// <see cref="upgradeKind"/> is empty.</summary>
        public int upgradeStage;

        /// <summary>Percent chance this job seeds a hidden fault on
        /// <see cref="stat"/> when it goes in — a used part off the salvage
        /// yard's shelf, and how rough it looked. Rolled at INSTALL rather than
        /// at purchase, which is the reason it rides on the job at all: a fault
        /// stamped the day you paid would be on the car for the days the part
        /// spends in the boot. 0 on everything a shop or a dealer touched, and
        /// on every job in a save written before the yard existed.</summary>
        public int junkRisk;

        public bool IsUpgrade => !string.IsNullOrEmpty(upgradeKind);

        /// <summary>A part bought off the salvage yard's shelf rather than
        /// booked at a bench: no fault behind it and no stage in front of it,
        /// which is a combination nothing else in the queue produces. It is a
        /// thing you OWN and are waiting to fit, and that is why the mechanic's
        /// supersede rule has to be able to see it — see
        /// <see cref="LifeRules.BuyService"/>.</summary>
        public bool IsYardPart => !IsUpgrade && string.IsNullOrEmpty(faultId);
    }

    /// <summary>
    /// One part on the salvage yard's shelf. Expires like a classified ad does,
    /// but on the clock its own shelf keeps — see <see cref="Junkyard.Shelf"/>.
    ///
    /// Carries a rated <see cref="add"/> and a <see cref="basePrice"/> for a
    /// service part, or an <see cref="upgradeKind"/> and a
    /// <see cref="maxStage"/> for hardware, and never both. Hardware has no
    /// price of its own here on purpose: a used turbo is worth a fraction of
    /// whatever the stage it serves costs on the car it is going onto, so the
    /// number is quoted at the row rather than stamped on the shelf.
    /// </summary>
    [Serializable]
    public class YardPart
    {
        public int shelf;                // Junkyard.Shelf ordinal
        public string label = "";
        /// <summary>What the yard says about where it came from. Flavour, and
        /// the only thing on the row that is not a number.</summary>
        public string donorHint = "";
        /// <summary>0-100, and openly shown: you can see rust. Drives price,
        /// how much of `add` you actually get, and the fault risk.</summary>
        public int grade = 60;
        public int expiresDay;

        // ---- service part ----
        public string stat = "engine";   // engine / tires / hp / paint
        public int add;                  // condition a PERFECT example restores
        public int basePrice;
        public int days;

        // ---- hardware ----
        /// <summary>Upgrade category key, or empty for a service part. Same
        /// string form <see cref="PendingPart.upgradeKind"/> uses, and for the
        /// same reason: a reordered enum must not re-point a saved shelf.</summary>
        public string upgradeKind = "";
        /// <summary>Highest stage this pull can serve. A car already past it is
        /// told so rather than being sold a part that does nothing.</summary>
        public int maxStage;

        public bool IsUpgrade => !string.IsNullOrEmpty(upgradeKind);
    }

    /// <summary>A car for sale in the classifieds. Listings expire, so a deal
    /// you sleep on can be gone — which is what makes the newspaper worth
    /// checking rather than a permanent shop.</summary>
    [Serializable]
    public class CarListing
    {
        public string specId;
        public string displayName;
        public int price;
        public int cond;
        public float odoMiles;
        public int expiresDay;
        public bool isNew;
        /// <summary>Disclosed defect, or empty. A disclosed problem knocks 45%
        /// off the asking price — the cheap listings are cheap for a reason.</summary>
        public string problem = "";
    }

    /// <summary>A car the player has listed for sale, and the standing offer on
    /// it if one has come in.</summary>
    [Serializable]
    public class CarAd
    {
        public string carId;
        public int askPrice;
        public int daysListed;
        public int offerAmount;   // 0 = no live offer
        public int offerDay;
    }

    [Serializable]
    public class CarLoan
    {
        public string carId;
        public int principal;
        public int monthlyPayment;
        public int monthsRemaining;
        public float apr;
    }

    [Serializable]
    public class BankLoan
    {
        public int principal;
        public int monthlyPayment;
        public int monthsRemaining;
    }

    [Serializable]
    public class MailItem
    {
        public int day;
        public string subject;
        public string body;
        public bool read;
        /// <summary>0 = keeps forever. A blacklist call-out expires; a purchase
        /// offer does not.</summary>
        public int expiresDay;
    }

    /// <summary>
    /// A race the player has written into the diary for a future day.
    ///
    /// Carries its own venue rather than reading the MAIN screen's picker,
    /// because the point of a diary is that three bookings on three days can be
    /// three different places. Practice is stored too: a booked practice lap is
    /// a legitimate plan — you can commit an afternoon to learning a circuit
    /// before the night you have to win on it.
    /// </summary>
    [Serializable]
    public class RaceBooking
    {
        public int day;
        public int trackIndex;
        public bool practice;
    }
}
