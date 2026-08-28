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
        static void Migrate(LifeState s)
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
