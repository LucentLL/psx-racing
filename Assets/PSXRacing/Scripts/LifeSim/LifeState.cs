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
        /// v13: the calendar became the front door. RaceBooking gained SLOT
        ///     (a race is written into a block of a day, not just a day);
        ///     LifeState gained slotActs (what each of today's three blocks
        ///     was spent on) and dayLog (the same, per closed day) so the
        ///     week view can show a past shift as WORKED or MISSED rather than
        ///     as nothing. The migration stamps every existing booking onto
        ///     the night block, because a booking made before blocks existed
        ///     was "some time that day" and the night is when a street race
        ///     happens.
        /// v14: THE BOARD MOVES. The blacklist stopped being ten fixed rungs
        ///     the player climbs past and became an eleven-name ORDER with the
        ///     player in it — see <see cref="blBoard"/>. blDefeated/blPaged are
        ///     retired to legacy; the migration reads them once to work out
        ///     where an existing career already stands and writes the order.
        /// v21: SIX BLOCKS A DAY (DAWN, MORNING, NOON, AFTERNOON, EVENING,
        ///     NIGHT) where there were three. Every stored block index moves:
        ///     the clock and today's record (an old block is two new ones),
        ///     the day log, each booking (onto the block that holds the hour
        ///     it was going to run at, the hour written in so the race runs
        ///     under the same sky), and each job's ready block. See
        ///     LifeSimManager.Migrate.
        /// v23: THE ENFORCED WEEK. The job runs to a fixed schedule (misses,
        ///     lay-off, call-back) and car clubs arrive (membership, board,
        ///     strikes). All ADDED fields; the migration sets dutyFromDay/Slot
        ///     to the clock it finds so nothing is missed retroactively.
        /// v24: THE PARTS SHELF. A part pulled off a wreck is a thing you own
        ///     (partsShelf, see Spares) rather than only a discount route to
        ///     the next stage of the car you are in. ADDED fields; the
        ///     migration gives an old career an empty shelf.
        /// </summary>
        // v28 (R1c-B): INSURANCE - healthPlan/healthAsked/insRecord here and
        //     insCover/insDeductible on each car; ADDED fields, the step holds a
        //     car with a loan at full coverage (Insurance.Sync).
        // v29 (2026-11-15): EVENTS - evEntries/evHeard/racingRep/evClubScores/
        //     evNews (see Events); ADDED fields, the step gives an old career
        //     empty lists and racing rep 0.
        public int saveVersion = 30;   // NEW careers are born at the current version: a fresh save stamped 10 would be "migrated" on its next load and have its twin indices shifted for a move that never happened to it

        /// <summary>
        /// The save version of the BUILD that last wrote this career (R0a,
        /// 2026-11-12). Every PC build shares one registry key, and an exe
        /// from before R0a that loads a newer career drops the fields it does
        /// not know and writes the rest back still stamped with the newer
        /// version. Every <see cref="LifeSimManager.Save"/> stamps this, so a
        /// save at v25 or later whose writerVersion is below its saveVersion
        /// was written by a build that did not know that version (see
        /// LifeSimManager's save safety). ADDED WITHOUT A VERSION BUMP: its
        /// absence (0, what a pre-R0a exe writes) is the signal.
        /// </summary>
        public int writerVersion;

        // === Core economy / clock ===
        public int money;
        public int day = 1;              // absolute day counter; day 1 is a FRIDAY
        public int slotIndex;            // LifeRules.SlotNames: 0 dawn .. 5 night (v21; three blocks before)
        public int slotsActiveToday;     // non-rest slots burned since last sleep
        public bool workedToday;
        /// <summary>
        /// What each of TODAY's six blocks was spent on, by slot index —
        /// one of <see cref="LifeRules.ActWork"/> and friends, or empty for a
        /// block not yet reached. Written by SpendActivitySlot and Sleep,
        /// cleared by the rollover, which first copies it into
        /// <see cref="dayLog"/>. This is what lets the day view print SLEPT
        /// over the morning you slept through and SHIFT SKIPPED over the
        /// afternoon you raced instead of working.
        /// </summary>
        public List<string> slotActs = new List<string> { "", "", "", "", "", "" };
        /// <summary>The all-nighter the last rollover docked, while the DAWN
        /// block after it can still turn it into a late night (sleeping it
        /// off hands it back — see LifeRules.Sleep). Zero once DAWN is spent
        /// or the night was slept.</summary>
        public float lateNightDock;
        /// <summary>daysSinceSleep as it stood before that dock.</summary>
        public int lateNightDays;
        /// <summary>
        /// The last few weeks of <see cref="slotActs"/>, one record per closed
        /// day, newest last. Pruned to <see cref="LifeRules.DayLogKeep"/>
        /// entries at every rollover, so a long career does not carry a year
        /// of it. ADDED in v13: a save from before has none, and the calendar
        /// prints those days as blank rather than as anything it cannot know.
        /// </summary>
        public List<DayRecord> dayLog = new List<DayRecord>();

        // === Race setup (remembered between races) ===
        /// <summary>Which circuit the next race runs on, indexing
        /// TrackCatalog.All. An ADDED field, so old saves default to 0 —
        /// the city circuit they had always raced, until v22 retired it
        /// (the migration sends them to TrackCatalog.FirstOffered). New
        /// careers are seeded on FirstOffered too: 0 is Charlotte's free
        /// roam now, which is not a race.</summary>
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
        /// <summary>Wheels off any car, by the shell they came from (WheelSets).</summary>
        public List<string> wheelRack = new List<string>();
        /// <summary>Spare parts on the garage shelf (v24, see Spares): pulled
        /// off a wreck or bought off the yard's rack and not fitted yet.</summary>
        public List<SparePart> partsShelf = new List<SparePart>();
        /// <summary>Counter behind SparePart.id, so two identical pulls are
        /// still two parts.</summary>
        public int spareSeq;

        // === Health / fitness ===
        public float health = 100f;
        /// <summary>R1c-A (v27): the belt, an injury, the hospital, a write-off, game over (CrashFates).</summary>
        public CrashFate fate = new CrashFate();
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
        /// <summary>
        /// THE BOARD, top first: index 0 is #1 and the last entry is the
        /// bottom rung. One of these entries is the PLAYER
        /// (<see cref="Blacklist.PlayerKey"/>), which is what makes a rank
        /// something the player HAS rather than something they are climbing
        /// past.
        ///
        /// The order is the only place a rank is written down. Nothing else
        /// may store one: names move — every night somebody below calls
        /// somebody above out — so a rank held anywhere else is a rank that is
        /// wrong by morning. That is also why <see cref="BlacklistRival"/>
        /// entries are found BY ALIAS now, and why the signature-car cache is
        /// keyed on the alias rather than on the rung.
        /// </summary>
        public List<RankEntry> blBoard = new List<RankEntry>();

        /// <summary>
        /// The series being raced right now, or an entry with no alias when
        /// there is none. Always non-null on purpose: JsonUtility cannot
        /// represent a null nested object — it hands one back default-built —
        /// so "no challenge" has to be a STATE of an object rather than the
        /// absence of one, the same trick <see cref="OwnedCar.setup"/> plays
        /// with a factory tune.
        /// </summary>
        public RankChallenge blChallenge = new RankChallenge();

        /// <summary>What the board has been doing without you: the last dozen
        /// results, newest last, printed under the table. Kept OUT of
        /// <see cref="calendarLog"/> deliberately — the diary is five lines on
        /// MAIN and a ladder that moves every night would crowd the player's
        /// own week out of their own calendar. Only results the player was IN
        /// go in the diary.</summary>
        public List<string> blNews = new List<string>();

        /// <summary>Who the player may not call out again yet, and until when.
        /// Set by a series they LOST — including one they walked away from, so
        /// forfeiting a call-out you made costs more than the legs.</summary>
        public string blRematchAlias = "";
        public int blRematchDay;

        /// <summary>The earliest day somebody below can call the player out
        /// again. Stops the rung below pestering them every morning; set
        /// whenever a series involving the player ends.</summary>
        public int blIncomingReadyDay;

        /// <summary>The player has been asked onto the blacklist. Until then
        /// the board runs without them (the names still churn every night)
        /// but there is no RIVALS tab, nobody calls them out and nobody at a
        /// meet races them for a rank — the owner, 2026-10-31: the list should
        /// not be there before the player has been invited to it. Set once,
        /// by <see cref="Blacklist.CheckInvite"/>, and never cleared.</summary>
        public bool blInvited;

        // ---- legacy (pre-v14) ----
        /// <summary>DEAD, and kept only so the v14 migration can read where a
        /// career already stood. Ranks the player had beaten under the fixed
        /// ladder. See the note on <see cref="atFaultIncidents"/> for why a
        /// retired field stays in the class.</summary>
        public List<int> blDefeated = new List<int>();
        /// <summary>DEAD. Ranks whose one-shot call-out had already fired.</summary>
        public List<int> blPaged = new List<int>();

        /// <summary>
        /// DEAD. Heavy impacts used to accumulate here as a permanent driving
        /// record, printed on BILLS and destined for an insurance multiplier
        /// that never landed. Retired at the owner's ask: "the player is
        /// punished enough by repairing damages to their car."
        ///
        /// The FIELD stays because JsonUtility reads what a save wrote and a
        /// removed field is a parse that quietly drops the rest of the object
        /// on some Unity versions. Nothing reads it and nothing writes it; an
        /// old career carries its old number and it means nothing.
        /// </summary>
        public int atFaultIncidents;

        // === Housing / finance ===
        public string housingType = "";
        public int monthlyHousingCost;
        public int missedPayments;
        public int creditScore = 650;
        public int garageSlots = 1;
        public List<CarLoan> carLoans = new List<CarLoan>();
        public List<BankLoan> bankLoans = new List<BankLoan>();

        // === Insurance (v28, R1c-B; see Insurance) ===
        /// <summary>The health plan picked when hired (Insurance.Plans key); "" = none.</summary>
        public string healthPlan = "";
        /// <summary>The plan question has been answered (none counts).</summary>
        public bool healthAsked;
        /// <summary>The driving record: car claims, tickets, health claims.</summary>
        public List<InsRecord> insRecord = new List<InsRecord>();

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

        // === Car meets (see CarMeets) ===
        /// <summary>The day of the last meet the player RACED at. The meets
        /// themselves are not in the save — they fall on the calendar by rule,
        /// so every week of a career has them without anything being stored —
        /// but who has already been raced tonight has to survive the trip to
        /// the start line and back. ADDED field: zero on an old save, which is
        /// "never", which is right.</summary>
        public int meetDay;
        /// <summary>Who has already been raced at the <see cref="meetDay"/>
        /// meet, by the catalog id of the car they brought — every car in the
        /// lot is a different model, so the car IS the driver, and an id
        /// survives the lot being re-dealt where a stall number would not. A
        /// driver you have run once tonight does not line up again, and the
        /// list's LENGTH is how many runs the night has had — the lot thins
        /// out after <see cref="CarMeets.MaxRunsPerMeet"/>.</summary>
        public List<string> meetRaced = new List<string>();

        // === The enforced week (v23: work schedule + car clubs) ===
        // See WorkSchedule, Clubs and Duty. Nothing here stores an EVENT:
        // shifts, club meets and club races fall on the calendar by rule.
        // What is stored is standing — on the roster or laid off, which club,
        // the club's order — and the record of what was owed and what was
        // done about it (dutyLog), which is also where the strikes are read.

        /// <summary>Obligations are only owed from this block on, as a block
        /// ordinal (Duty.Ord: day x 6 + block). A new career owes from day 1;
        /// the v23 migration sets it to the clock it found, so nothing before
        /// the rule existed is "missed".</summary>
        public int dutyFrom;
        /// <summary>What each obligation came to: one record per shift, club
        /// meet and club race once it has been settled (or attended early).
        /// Pruned to <see cref="LifeRules.DayLogKeep"/> days. A rank is NOT
        /// stored here — see <see cref="clubBoard"/>.</summary>
        public List<DutyRecord> dutyLog = new List<DutyRecord>();

        /// <summary>Taken off the roster after too many missed shifts. The job
        /// title stays (it is still the player's trade); there are no shifts
        /// and no pay until the call-back is accepted.</summary>
        public bool laidOff;
        public int layoffDay;
        /// <summary>The day the shop calls back with an opening.</summary>
        public int callBackDay;
        /// <summary>The call-back has arrived and waits on the JOBS page.</summary>
        public bool callBackOffered;
        /// <summary>Shifts are owed (and misses counted) from this block
        /// ordinal on - moved to the next block when a call-back is accepted,
        /// which is what clears the misses.</summary>
        public int jobFrom;

        /// <summary>The player's standing with each club they have had to do
        /// with - several at once, like guilds (owner, 2026-11-11). One entry
        /// per club id; a club never joined has none. A LIST, never a null
        /// object (JsonUtility).</summary>
        public List<ClubStanding> clubs = new List<ClubStanding>();
        /// <summary>Club results for the paper's MOTORING line and the board
        /// page, newest last. Kept beside blNews, not in it: the blacklist is
        /// left as it is.</summary>
        public List<string> clubNews = new List<string>();
        /// <summary>The club the NEXT delivery is a car-meet order for, picked
        /// by something outside the rota (the hook for an intro where the shop
        /// sends the player to a matching meet). Empty = the rota decides.</summary>
        public string nextMeetOrderClub = "";

        // === EVENTS (v29, owner 2026-11-15; see Events) ===
        /// <summary>The events the player signed up for, one per running
        /// (edition), with each race's outcome. Closed entries are kept a few
        /// weeks for the record, then pruned (Events.Tick).</summary>
        public List<EventEntry> evEntries = new List<EventEntry>();
        /// <summary>Word-of-mouth STREET events the player has been told about
        /// (a car meet or a club passed them on), by event id.</summary>
        public List<string> evHeard = new List<string>();
        /// <summary>RACING REP, 0-100: what SANCTIONED results build (street
        /// results build <see cref="streetRep"/>). Gates the bigger cups.</summary>
        public float racingRep;
        /// <summary>The clubs' event points (club rep), one row per club that
        /// has scored, with the player's own share. A LIST (JsonUtility).</summary>
        public List<ClubScore> evClubScores = new List<ClubScore>();
        /// <summary>Event results for the EVENTS page, newest last (bounded).</summary>
        public List<string> evNews = new List<string>();

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
        // Road dirt on the finish, 0 just washed .. 1 filthy. Not condition:
        // it costs the paint nothing, it only dulls the shine (PaintGloss)
        // until the car is washed. ADDED field, so old saves read clean.
        public float dirt;
        // (spray-fix2) The water on the body (CarWater): how wet it was when it got wet (the rain it
        // came home in, a wash), and the day and time slot that was in - dry once the slot has
        // passed. ADDED fields, so old saves read dry.
        public float wet;
        public int wetDay, wetSlot;

        // === the cooling system (see CoolingModel) ===
        // Three parts and what is in them, rather than one "cooling" number,
        // because each fails with its own signature on the temperature gauge —
        // a crusted core cooks under load, a dead fan cooks at a standstill,
        // and soft hoses quietly empty the system while everything reads
        // normal. One number could not tell a player which of those they had.
        //
        // ADDED fields, so they read back as a healthy system on every save
        // written before them; the v15 migration then seeds them off the engine
        // condition the car actually has, because a 20%-engine car with a
        // brand-new radiator would be a lie the first hot lap exposed.
        /// <summary>Radiator core, 0-100.</summary>
        public float radiator = 100f;
        /// <summary>Fan, clutch and motor, 0-100. Under 20 it is not turning.</summary>
        public float fan = 100f;
        /// <summary>Hoses, clamps and cap, 0-100 — what holds pressure.</summary>
        public float hoses = 100f;
        /// <summary>What is actually in the system, 0-100. The one cooling
        /// number that goes back UP for pocket change, and the one the player
        /// is meant to learn to check.</summary>
        public float coolant = 100f;
        /// <summary>
        /// The engine has let go — seized on the road, not worn out in the
        /// garage. A blown engine is not a condition of 0; it is a different
        /// STATE, and the difference matters: a 0% engine still starts and
        /// still drives badly, and this one does not move at all until somebody
        /// rebuilds it or fits another (see <see cref="LifeRules.EngineJobs"/>).
        /// </summary>
        public bool engineBlown;
        /// <summary>The shell whose wheels it wears; empty for its own.</summary>
        public string wheelKey = "";
        /// <summary>Its wheels are on the rack, so it cannot be driven.</summary>
        public bool noWheels;
        /// <summary>How the engine came to let go, one line per step (from
        /// <see cref="EngineTemp.Story"/>), for the garage page.</summary>
        public string engineStory;
        /// <summary>The livery this car has been RESPRAYED into, by baked
        /// name, or empty for "however it left the factory". A NAME rather
        /// than an index into CarModelDef.skinMaterials, because that array is
        /// rebuilt from the pack every time the model baker runs and a saved
        /// index would quietly become a different colour the day a livery was
        /// added. An ADDED field, so every existing save reads back empty —
        /// which is exactly right: nobody has painted anything yet. See
        /// <see cref="Paint"/>.</summary>
        public string paintSkin = "";

        /// <summary>The debug bench's one loaner slot (see
        /// <see cref="DebugCarOps.Loan"/>): a car the bench conjured so the
        /// tester could drive something they do not own. There is at most one
        /// — borrowing another REPLACES it — so a session spent walking the
        /// catalog leaves one extra car in the garage rather than thirty. An
        /// added field; false on every car anyone actually bought.</summary>
        public bool debugLoaner;

        /// <summary>The debug bench's DRIVE SIDE pick for THIS car (owner,
        /// 2026-10-16): "RHD" or "LHD", or empty for the side its catalog row
        /// names (DriveSide). Read only in a debug career and only for a car
        /// whose real counterpart was sold both ways (<see cref="DebugCarOps.SidePick"/>);
        /// anywhere else it is ignored. Kept on the car beside
        /// <see cref="debugLoaner"/> rather than in PlayerPrefs because the
        /// pick belongs to the car (two EG Civics can sit on opposite sides)
        /// and rides in the one save the bench already writes on close. An ADDED
        /// field: every save reads back empty, no migration.</summary>
        public string debugDriveSide = "";
        /// <summary>The debug bench's LAMP LENSES pick for THIS car (owner,
        /// 2026-10-16: "I want to test out headlight/taillight lenses"): "B"
        /// = the shell's lens variant (recessed optics behind clear covers),
        /// anything else = A, the opaque lamps every player sees. Read only in
        /// a debug career and only on a shell that has a variant
        /// (<see cref="DebugCarOps.LensB"/>). Beside
        /// <see cref="debugDriveSide"/> for the same reasons; an ADDED field,
        /// no migration.</summary>
        public string debugLampLens = "";

        public List<CarFault> faults = new List<CarFault>();

        /// <summary>R1a (v26): what happened to this car (DamageBank, at most 48).</summary>
        public List<CarEvent> history = new List<CarEvent>();
        /// <summary>R1a (v26): 0 clean, 1 rebuilt, 2 salvage, 3 flood (+8 washed).</summary>
        public int title;
        /// <summary>R1c-A (v27): the hitId of the structural FAIL that wrecked it, 0 none; it does not move while a FAIL row stands.</summary>
        public int wreckHit;
        public const int TitleFlood = 3;
        /// <summary>R1a part 2 (HeatSoak): the needle when it was parked, and
        /// when (day, block). 0 = cold; ADDED fields, no migration.</summary>
        public float parkedC;
        public int parkedDay, parkedSlot;

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
        /// <summary>The passenger seat, 0-4. The one category that does
        /// nothing for the stopwatch: it exists for the pizza on it. A flat
        /// stock seat lets a box slide on a hard corner; a race bucket holds it
        /// between its bolsters. Defaults to 0 like the rest, so a pre-feature
        /// save loads as a stock seat with no migration.</summary>
        public int upSeat;

        // One-off bolt-on mods. Separate from the stage ladder because they are
        // not a ladder — you either welded the diff or you did not, and neither
        // has a stage 2. RG2 keeps these as the same two booleans.
        /// <summary>Welded rear diff — the driven wheels break away together.</summary>
        public bool welded;
        /// <summary>Roots blower. Offered on naturally-aspirated cars only.</summary>
        public bool supercharged;
        /// <summary>A turbo kit on an NA engine: the car's power ladder is the
        /// TURBO one (CarTune, "two ladders"). Chosen with the first turbo
        /// stage; a factory turbo car is on that path without it.</summary>
        public bool turbo;

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

        /// <summary>v28 (R1c-B): Insurance.Liability (NC minimum) or Insurance.Full;
        /// a car with a loan is full whatever this says (Insurance.CoverOf).</summary>
        public int insCover;
        /// <summary>The full-coverage deductible: 250, 500 or 1000.</summary>
        public int insDeductible = 500;
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

        // ---- WHY IT HAPPENED (v25, R0b; see FaultCause). Stamped where the
        // row is created, never shown in R0b (a mechanic or dealer answers
        // "why" from it once they have found or fixed it - R2b). Empty cause
        // = a row from before v25, which the v25 step marks "legacy". 0 day,
        // odometer and zone = unknown.
        /// <summary>FaultCause class: impact, wear, service, abuse, heat, age,
        /// mod, workmanship, previous_owner, seller, legacy, debug.</summary>
        public string cause = "";
        /// <summary>A note for whoever explains it ("front left 7.2 m/s", the
        /// advert's phrase).</summary>
        public string causeNote = "";
        public int bornDay;
        public float bornOdo;
        /// <summary>A hit's zone: 0 unknown, 1 front, 2 rear, 3 side, 4 under.</summary>
        public int zone1;
        /// <summary>A hit's side: -1 left, 0 centre or unknown, +1 right.</summary>
        public int hitSide;
        /// <summary>A hit's closing speed, m/s (0 unknown). R1a: the hit's
        /// delta-V over the crash pulse.</summary>
        public float hitMps;

        // ---- R1a (v26): located damage (DamageModel, DamageBank). 0 = none /
        // unknown; a row from before v26 is staged from its severity.
        /// <summary>The incident that made (or last raised) this row; 0 none.</summary>
        public int hitId;
        /// <summary>The struck corner +1 (FL FR RL RR), 0 none.</summary>
        public int corner1;
        /// <summary>0 latent, 1 whisper, 2 talk, 3 shout, 4 fail (design §1.4).</summary>
        public float stage;
        /// <summary>The row's size: value % (panels, glass), %/min (radiator),
        /// power % (exhaust), x1.3 (a crushed bar).</summary>
        public float mag;
    }

    /// <summary>R1a (v26): one entry in a car's history - a hit, a bump-stop
    /// strike, a roll, a drop, a dunking. hitId keeps a damage file from being
    /// banked twice. Never shown in R1a.</summary>
    [Serializable]
    public class CarEvent
    {
        public int day;
        public float odo;
        /// <summary>DamageModel.K*: 0 hit, 1 bump stop, 2 roll, 3 drop, 4 water.</summary>
        public int kind;
        public int zone1, corner1;
        public float dv;
        public int hitId;
        /// <summary>What it cost to put right (R2b+), and who did it.</summary>
        public int cost, who;
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
        /// <summary>WHERE the work is being done: 0 your own garage (DIY),
        /// 1 the mechanic, 2 the dealership, 3 the paint shop — see
        /// <see cref="CarWhere"/>. Anything above zero means the CAR IS THERE,
        /// not at home, and cannot be driven until the job is done.</summary>
        public int venue;        // 0 diy / 1 mechanic / 2 dealer / 3 paint shop
        /// <summary>
        /// Which BLOCK of <see cref="readyDay"/> the job is done by — a
        /// LifeRules.SlotNames index (six blocks since v21). A mechanic hands the car back first thing in the
        /// morning, which is what every job in a save written before this
        /// field reads back as (an ADDED int is zero). The dealership is the
        /// one that uses it: "same day" there means the NEXT block, so a car
        /// dropped off in the morning is back for the afternoon.
        /// </summary>
        public int readySlot;
        /// <summary>The livery a RESPRAY will leave the car wearing, by baked
        /// name (see <see cref="OwnedCar.paintSkin"/>). Empty on every job that
        /// is not a respray. The colour goes on the car when the job is DONE,
        /// not when it is paid for: a car standing in a spray booth is not yet
        /// the colour on the invoice.</summary>
        public string paintSkin = "";

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

        /// <summary>
        /// The bottom-end job this is, when it is one: "rebuild" or "swap" —
        /// see <see cref="LifeRules.EngineJobs"/>. Empty on everything else,
        /// which is every job in every save written before an engine could be
        /// destroyed at all.
        ///
        /// Its own field rather than a fault id, because a blown engine is not
        /// a fault: there is no pool row for it, no inspection that finds it
        /// (the car does not move — you know), and no version of it that is
        /// merely hidden.
        /// </summary>
        public string engineJob = "";
        /// <summary>Engine condition a SWAP leaves behind: whatever the donor
        /// had. A rebuild writes 100 and ignores this.</summary>
        public int engineCondAfter;

        public bool IsEngineJob => !string.IsNullOrEmpty(engineJob);

        /// <summary>Percent chance this job seeds a hidden fault on
        /// <see cref="stat"/> when it goes in — a used part off the salvage
        /// yard's shelf, and how rough it looked. Rolled at INSTALL rather than
        /// at purchase, which is the reason it rides on the job at all: a fault
        /// stamped the day you paid would be on the car for the days the part
        /// spends in the boot. 0 on everything a shop or a dealer touched, and
        /// on every job in a save written before the yard existed.</summary>
        public int junkRisk;

        /// <summary>A part off the garage shelf going on (v24, see Spares):
        /// owned and paid for, so no service sweep may cancel it, even when it
        /// is replacing a named fault.</summary>
        public bool fromShelf;
        /// <summary>The spare itself while it is going on, so a car sold in
        /// the middle of the job gives the part back to the shelf
        /// (Spares.ReturnJobsToShelf). Empty id on every other job.</summary>
        public SparePart spare;

        public bool IsUpgrade => !string.IsNullOrEmpty(upgradeKind);

        /// <summary>A respray at the paint shop. Told by the VENUE rather than
        /// by <see cref="paintSkin"/>, because a refinish in the colour the car
        /// already wears — or back to the factory one — is a respray with an
        /// empty skin name.</summary>
        public bool IsRespray => venue == CarWhere.VenuePaint;

        /// <summary>A part bought off the salvage yard's shelf rather than
        /// booked at a bench: no fault behind it and no stage in front of it,
        /// which is a combination nothing else in the queue produces — but for
        /// a respray, which is excluded by name. It is a thing you OWN and are
        /// waiting to fit, and that is why the mechanic's supersede rule has to
        /// be able to see it — see <see cref="LifeRules.BuyService"/>.
        ///
        /// An ENGINE JOB is excluded by name for the same reason a respray is:
        /// it carries no fault id either, and a $4,000 rebuild swept away by a
        /// $50 oil change would be the most expensive silent bug in the
        /// game.</summary>
        public bool IsYardPart => fromShelf || (!IsUpgrade && !IsRespray && !IsEngineJob &&
                                  string.IsNullOrEmpty(faultId));
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
        /// <summary>Which block of the day — LifeRules.SlotNames, 0 dawn ..
        /// 5 night since v21 (0 morning / 1 day / 2 night before). A
        /// race is an appointment for an evening, not for a date, and the
        /// calendar has to know which block to draw it in and which block
        /// to offer the RACE button on. v13; older bookings are migrated
        /// onto the night.</summary>
        public int slot = LifeRules.NightSlot;
        public int trackIndex;
        public bool practice;
        /// <summary>
        /// The hour the player chose for this race, as a
        /// <see cref="TimeOfDay"/> index PLUS ONE — zero is "the block's own
        /// hour" (<see cref="TimeOfDay.ForSlot"/>). Off by one on purpose:
        /// zero is what JsonUtility hands every booking written before the
        /// field existed, and zero as an index is DAWN, so a plain index
        /// would have silently moved every old night race to 05:40. Read it
        /// through <see cref="LifeRules.BookingHour"/>, never directly.
        /// </summary>
        public int hourPick;
        /// <summary>
        /// The traffic the player chose for this race (owner, 2026-09-30:
        /// "a toggle to determine traffic amount for each race"), as a
        /// <see cref="TrafficLevels"/> level PLUS ONE - zero is "the level the
        /// race's hour puts on the road" (<see cref="TrafficLevels.ForHour"/>),
        /// which is exactly what every booking written before the setting
        /// existed ran with, so an old save needs no migration: its zero
        /// reads as the traffic it always had. Off by one for hourPick's
        /// reason (zero as a level would be NONE). Read it through
        /// <see cref="LifeRules.BookingTraffic"/>, never directly.
        /// </summary>
        public int trafficPick;
    }

    /// <summary>
    /// One name on the blacklist, and what it has done lately.
    ///
    /// The RANK is the position in <see cref="LifeState.blBoard"/> and is
    /// deliberately not a field: a rank stored beside the name is a second
    /// copy of the order, and two copies of a thing that moves every night is
    /// one copy that is wrong.
    /// </summary>
    [Serializable]
    public class RankEntry
    {
        /// <summary>The rival's alias, or <see cref="Blacklist.PlayerKey"/>
        /// for the player's own row.</summary>
        public string alias = "";
        /// <summary>Challenge races won and lost — legs, not series, because a
        /// leg is what a race is. This is the record printed on the board, and
        /// it is what makes the churn legible: a name that has climbed three
        /// rungs this month has the results to show for it.</summary>
        public int wins;
        public int losses;

        public bool IsPlayer => alias == Blacklist.PlayerKey;
    }

    /// <summary>
    /// A live challenge: two drivers, one rank, best of three.
    ///
    /// One at a time, in either direction. Both are the same object because
    /// they are the same fight — all that changes is who knocked, which decides
    /// what a forfeit costs and which way the board moves when it ends.
    /// </summary>
    [Serializable]
    public class RankChallenge
    {
        /// <summary>The rival's alias. EMPTY means there is no series — see
        /// <see cref="LifeState.blChallenge"/> for why this is a state rather
        /// than a null.</summary>
        public string alias = "";
        /// <summary>THEY called the player out, for the player's spot. False
        /// when the player went up the board looking for it.</summary>
        public bool incoming;
        public int youLegs;
        public int themLegs;
        public int openedDay;
        /// <summary>Last day a leg can be run. Everything still unraced when
        /// this day closes is forfeited — the owner's rule: declining or
        /// missing a race for any reason counts as a loss.</summary>
        public int deadlineDay;
        /// <summary>Where the NEXT leg runs. -1 while it is the player's to
        /// name and they have not named it yet (<see cref="yourPick"/>).
        ///
        /// It used to be one road for all three legs. The owner, 2026-10-31:
        /// whoever is CHALLENGED names the first road, and the LOSER of each
        /// leg names the next one — so this is re-chosen after every leg, and
        /// a road already raced in the series cannot be named again (its
        /// reverse twin can).</summary>
        public int trackIndex = -1;
        /// <summary>The next leg's road is the PLAYER's to name.</summary>
        public bool yourPick;
        /// <summary>Roads already raced in this series, in leg order — stamped
        /// at the start line (<see cref="Blacklist.BeginLeg"/>).</summary>
        public List<int> legRoads = new List<int>();
        /// <summary>A leg has been LAUNCHED and has not reported back. The
        /// same contract <see cref="CarMeets.BeginRun"/> uses: the run is
        /// committed at the start line, so quitting out of it is a loss rather
        /// than a way to un-race a race you were losing.</summary>
        public bool legInFlight;

        public bool Live => !string.IsNullOrEmpty(alias);
        /// <summary>Legs already settled, 0-3.</summary>
        public int LegsRun => youLegs + themLegs;
        /// <summary>Which leg the next race is, 1-3.</summary>
        public int LegNumber => LegsRun >= 2 ? 3 : LegsRun + 1;
    }

    /// <summary>
    /// The player and one club. Membership, the club's board (the rank is a
    /// POSITION in it - the blBoard rule - and stored nowhere else), a drop's
    /// re-admission day, and an invitation. Strikes are read off dutyLog.
    /// </summary>
    [Serializable]
    public class ClubStanding
    {
        public string id = "";
        public bool member;
        /// <summary>The block (Duty.Ord) the player joined in; nothing before
        /// it is owed to the club.</summary>
        public int from;
        /// <summary>Top first, the player in it as Blacklist.PlayerKey.</summary>
        public List<RankEntry> board = new List<RankEntry>();
        /// <summary>Out (dropped or walked out): the first day they will have
        /// the player back, at the bottom. 0 = never out.</summary>
        public int readmitDay;
        /// <summary>Asked to join: an invitation can be accepted from the club
        /// page without turning up at a meet (the elite club's only door, and
        /// the hook for a story that hands one out).</summary>
        public bool invited;
    }

    /// <summary>
    /// One obligation of the enforced week, settled: a scheduled SHIFT, a
    /// CLUB MEET or a CLUB RACE, the block it started in, and what came of
    /// it. Written when the player attends (the meet lot, the start line, the
    /// counter) or when the block has passed without them (Duty.Settle).
    /// </summary>
    [Serializable]
    public class DutyRecord
    {
        public int day;
        /// <summary>The block the obligation STARTS in (a shift covers two).</summary>
        public int slot;
        /// <summary>Duty.KindShift / KindMeet / KindRace.</summary>
        public string kind = "";
        /// <summary>Duty.Done, Missed, WrongCar or NoCar.</summary>
        public string outcome = "";
        /// <summary>The club it was owed to (empty for a shift).</summary>
        public string club = "";
        /// <summary>A club race's result once it is known: WON / LOST, or
        /// empty (not run, or never reported back).</summary>
        public string result = "";
        /// <summary>The line that says why, for the planner.</summary>
        public string note = "";

        public bool IsMiss => outcome != Duty.Done && outcome != Duty.Medical && outcome.Length > 0;
    }

    /// <summary>
    /// One closed day, as the three things its blocks were spent on. The
    /// activity log next door is prose for the RECENTLY list; this is the
    /// structured copy the week and month views read, so that last
    /// Tuesday's afternoon can be drawn as the shift it was or the race it
    /// became.
    /// </summary>
    [Serializable]
    public class DayRecord
    {
        public int day;
        /// <summary>One word per block, LifeRules.SlotNames order (v21).</summary>
        public List<string> acts = new List<string>();
        // The three-block day (v13..v20). Read once by the v21 migration,
        // which spreads each over the two blocks it became; empty after.
        public string morning = "";
        public string afternoon = "";
        public string night = "";

        public string Act(int slot)
        {
            if (acts != null && slot >= 0 && slot < acts.Count) return acts[slot];
            // A record the migration has not reached: each old block is two.
            int old = slot / 2;
            return old <= 0 ? morning : old == 1 ? afternoon : night;
        }
    }
}
