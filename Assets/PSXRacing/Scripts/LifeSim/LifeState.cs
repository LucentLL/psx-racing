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
        /// </summary>
        public int saveVersion = 6;

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

        // === Mail / log ===
        public List<MailItem> mail = new List<MailItem>();
        public List<string> calendarLog = new List<string>();

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
}
