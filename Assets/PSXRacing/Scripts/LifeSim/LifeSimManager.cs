using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Owns the LifeState: load, save, new game. Pure state — no UI.
    ///
    /// Persistence is PlayerPrefs carrying one JSON blob, which is the direct
    /// equivalent of what RG2 shipped (a JSON blob in localStorage under
    /// 'driverCitySave'). On WebGL, PlayerPrefs is backed by browser storage
    /// and Unity handles the async IndexedDB flush; raw File IO there needs a
    /// manual JS_FileSystem_Sync and loses data on tab close if you forget it,
    /// so PlayerPrefs is deliberately the boring, safe choice.
    /// </summary>
    public static class LifeSimManager
    {
        const string SaveKey = "psxRacingLifeSave";

        static LifeState state;

        /// <summary>The live state. Loads (or creates) lazily so any scene can
        /// run first in the editor without a boot ceremony.</summary>
        public static LifeState State
        {
            get
            {
                if (state == null) state = Load() ?? NewGame();
                return state;
            }
        }

        public static bool HasSave => PlayerPrefs.HasKey(SaveKey);

        public static void Save()
        {
            if (state == null) return;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
        }

        public static LifeState Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey)) return null;
            try
            {
                var s = JsonUtility.FromJson<LifeState>(PlayerPrefs.GetString(SaveKey));
                if (s == null || s.saveVersion < 1) return null;
                Migrate(s);
                return s;
            }
            catch { return null; }   // corrupt save: fall through to new game
        }

        /// <summary>
        /// Forward-migrate an older save in place. Added fields need nothing —
        /// JsonUtility leaves them at their initializer defaults — so this only
        /// handles cases where an old value would be WRONG rather than absent.
        /// </summary>
        /// <summary>Bring an older save forward. PUBLIC rather than private
        /// because the self-test lives in the editor assembly, which cannot
        /// see internals of this one - and the v7 rule is the only thing
        /// between an old career and a fault list it never earned, which makes
        /// it exactly the code that has to be pinned rather than assumed.
        /// Idempotent: it runs on every load.</summary>
        public static void Migrate(LifeState s)
        {
            if (s.saveVersion < 2)
            {
                // v1 wrote synthetic faults ("worn-engine") with no catalog
                // backing: no cost, no repair time, no effect entry. They cannot
                // be quoted or repaired, so retire them and let the next race
                // re-roll a real catalogued fault from the same worn stat.
                foreach (var car in s.cars)
                    car.faults.RemoveAll(f => f.id != null &&
                                              (f.id.StartsWith("worn-") || f.id.StartsWith("severe-")));
                s.saveVersion = 2;
            }
            if (s.saveVersion < 3)
            {
                // v3 added the catalog link. Existing cars were all the built-in
                // RX-7, so give them its MSRP as a value base — leaving
                // catalogPrice at 0 would make every owned car worthless.
                foreach (var car in s.cars)
                {
                    if (car.catalogPrice <= 0) car.catalogPrice = 36000;
                    if (car.specId == null) car.specId = "";
                }
                s.saveVersion = 3;
            }
            if (s.saveVersion < 4)
            {
                // v4 added the blacklist ladder. The lists are ADDED fields, so
                // JsonUtility already left them at their empty initializers — but
                // a save from before the ladder existed has a race history the
                // ladder would read as unclaimed progress. That is the intended
                // reading: an existing career walks into an open bottom rung
                // (or several) rather than having to re-earn wins it already has.
                if (s.blDefeated == null) s.blDefeated = new System.Collections.Generic.List<int>();
                if (s.blPaged == null) s.blPaged = new System.Collections.Generic.List<int>();
                // Rep decay moved off lastRaceDay onto lastAnyRaceDay. Left at 0
                // an existing save reads as "never raced", and the first rollover
                // would decay rep for a 300-day inactivity that never happened.
                if (s.lastAnyRaceDay == 0) s.lastAnyRaceDay = s.lastRaceDay;
                s.saveVersion = 4;
            }

            if (s.saveVersion < 5)
            {
                // v5 added the toolbox and per-car inspection latches. Every
                // field is ADDED, so JsonUtility has already left them at their
                // initializers — except the per-car ones, which JsonUtility
                // fills with 0 rather than the -1 they default to. Day 0 is
                // before the game starts, so a 0 would read as "inspected on
                // day 0" and silently eat the first inspection of every car
                // the player already owns.
                if (s.tools == null) s.tools = new System.Collections.Generic.List<string>();
                foreach (var car in s.cars)
                {
                    if (car.inspectedSubs == null)
                        car.inspectedSubs = new System.Collections.Generic.List<string>();
                    if (car.inspectDay == 0) car.inspectDay = -1;
                    if (car.floorCheckedDay == 0) car.floorCheckedDay = -1;
                }
                s.saveVersion = 5;
            }

            if (s.saveVersion < 6)
            {
                // v6 turned the apartment ladder into houses (the player now
                // starts in the small house the walk-in scene builds). Same
                // rents, same slots — only the KEY and the label change, so an
                // old save is renamed onto the new rung rather than left with
                // a housingType no table row can label.
                s.housingType = s.housingType switch
                {
                    "apt1br" => "house1g",
                    "apt2br" => "house2g",
                    "rentHouse" => "house3g",
                    _ => string.IsNullOrEmpty(s.housingType) ? "house1g" : s.housingType,
                };
                s.saveVersion = 6;
            }

            if (s.saveVersion < 7)
            {
                // v7 is the save-side half of "nothing diagnoses itself". Until
                // 2026-08-28 RollWearFault stamped every wear, threshold and
                // impact fault hidden=false/diagnosed=true, so a career carried
                // over from before that change lists parts by name on the MAIN
                // and GARAGE screens that nobody ever inspected for - reported
                // exactly that way, as fault pop-ups after a race.
                //
                // The save records WHAT was found, never WHO found it, so the
                // discriminator has to be the car's own inspection history: a
                // car nobody has ever had a lamp under is a car nobody has
                // found anything on. Faults on a car the player HAS inspected
                // stay visible, because that reading is at least as likely to
                // be the true one and taking knowledge back is the worse error.
                //
                // BookPro was the one hole - it revealed without stamping the
                // car - so it stamps proInspectDay now. That cannot be
                // recovered for an existing save, and under the old rules it
                // had nothing to reveal anyway: every fault was born visible,
                // so its `if (!f.hidden) continue` skipped the lot. Under-
                // revealing is the safe direction regardless - the fault is
                // still on the car and still slowing it down, and one
                // inspection finds it again.
                foreach (var car in s.cars)
                {
                    if (car == null) continue;
                    // proInspectDay is an ADDED field, so JsonUtility hands it
                    // back as 0 rather than the -1 it initialises to - and 0 is
                    // a day BEFORE the game starts, so left alone it would read
                    // as "a dealer looked at this car" for every car in the
                    // save and re-hide nothing at all. Same trap v5 hit with
                    // inspectDay; normalise before anything reads it.
                    if (car.proInspectDay == 0) car.proInspectDay = -1;
                    if (car.faults == null) continue;
                    bool everLooked = car.inspectDay >= 0 || car.floorCheckedDay >= 0 ||
                                      car.proInspectDay >= 0 ||
                                      (car.inspectedSubs != null && car.inspectedSubs.Count > 0);
                    if (everLooked) continue;
                    foreach (var f in car.faults)
                    {
                        if (f == null) continue;
                        f.hidden = true;
                        f.diagnosed = false;
                    }
                }
                s.saveVersion = 7;
            }

            if (s.saveVersion < 8)
            {
                // v8 closed the job book down to the one job the game is about.
                // A career carried over from the old book is holding a job title
                // that no longer exists in LifeRules.Jobs — and nothing in the
                // game can pay, rate or fire a job that is not in the table, so
                // it would silently become an unpaid title with an attendance
                // ladder still ticking underneath it.
                //
                // Moved across rather than cleared: taking the job away would
                // drop the player onto the JOBS tab with a 55% roll to get any
                // work at all, which is a punishment for a change they did not
                // make. Standing carries too — work rep and attendance are what
                // the player earned, whoever they earned it from. Only the pay
                // rate is rewritten, because a tanker driver's $231 salary
                // against a tip roll is two different economies at once.
                if (!string.IsNullOrEmpty(s.playerJob))
                {
                    bool known = false;
                    foreach (var j in LifeRules.Jobs) if (j.name == s.playerJob) known = true;
                    if (!known)
                    {
                        s.calendarLog.Add(LifeRules.LogDate(s.day) + ": the shop took you on — " +
                                          LifeRules.DeliveryJobName);
                        s.playerJob = LifeRules.DeliveryJobName;
                        s.basePay = LifeRules.DeliveryBasePay;
                    }
                }

                // The absence counter meant something different yesterday: it
                // only ever counted weekdays, because the weekend was free. It
                // now counts every day, so a save that came in mid-weekend with
                // two strikes on it would be one rollover from being fired by
                // the rule change alone. Everyone starts the new roster clean.
                s.consecutiveAbsences = 0;
                s.saveVersion = 8;
            }

            if (s.saveVersion < 9)
            {
                // v9 added the salvage yard. The list itself needs nothing —
                // JsonUtility leaves an added field at its initializer — but an
                // EMPTY yard is not the same as a yard nobody has looked at:
                // the shelves only fill on a rollover, so a career that came in
                // from before this would open the advert onto three empty
                // shelves and have to sleep to find out the page works at all.
                // Stock it here so it is a yard the first time it is opened.
                if (s.junkyard == null)
                    s.junkyard = new System.Collections.Generic.List<YardPart>();
                Junkyard.RefreshStock(s);
                s.saveVersion = 9;
            }

            if (s.saveVersion < 10)
            {
                // v10 added the town: dealerLot and viewings are both ADDED
                // fields and need nothing. The lot is stocked here for the same
                // reason the yard was — a forecourt you drive to should have
                // cars on it the first time you arrive, not after a sleep.
                if (s.dealerLot == null)
                    s.dealerLot = new System.Collections.Generic.List<CarListing>();
                if (s.viewings == null)
                    s.viewings = new System.Collections.Generic.List<Viewing>();
                CarMarket.RefreshLot(s);

                // The one thing in this version that is NOT free.
                //
                // ADJUSTABLE AERO became a race-car-only part. A road car in an
                // existing save can therefore be carrying a kit that the shop
                // will no longer sell and the gate will no longer honour: two
                // padlocked rows where two sliders used to be, and money spent
                // on nothing. Unfit it and hand the money back, at the price
                // the part would cost on THAT car — the kit is priced per car,
                // so a flat refund would be wrong in both directions.
                int refunded = 0, cars = 0;
                foreach (var car in s.cars)
                {
                    if (!car.aeroKit) continue;
                    var spec = CarCatalog.Get(car.specId);
                    if (Upgrades.AeroKitAllowed(spec)) continue;
                    car.aeroKit = false;
                    var setup = car.setup;
                    if (setup != null)
                    {
                        setup.Set(SetupParam.AeroLevel, 0f);
                        setup.Set(SetupParam.AeroBalance, 0f);
                    }
                    int back = Upgrades.OfferFor(s, car, spec, Upgrades.Mod.AeroKit).price;
                    s.money += back;
                    refunded += back;
                    cars++;
                }
                if (cars > 0)
                    s.calendarLog.Add(LifeRules.LogDate(s.day) +
                        ": returned the adjustable aero on " + cars +
                        (cars == 1 ? " car" : " cars") + " — race parts only (" +
                        MenuKit.Money(refunded) + " back)");
                s.saveVersion = 10;
            }
        }

        public static void DeleteSave()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            state = null;
        }

        /// <summary>Fresh anonymous state — the home screen sees the empty
        /// player name and routes into the new-game wizard.</summary>
        public static LifeState NewGame()
        {
            state = new LifeState();
            return state;   // deliberately unsaved: the wizard commits
        }

        /// <summary>Commit the wizard: named character, chosen job, rolled
        /// starting savings (applyStartingConditions bands).</summary>
        public static void StartNewGame(string name, int age, int jobIdx)
        {
            state = LifeRules.SeedNewGame(name, age, jobIdx);
            Save();
        }
    }
}
