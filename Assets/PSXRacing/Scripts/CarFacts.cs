using System;
using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// One catalog row's FACTS (fault-system round D, 2026-11-12; design
    /// section 2 "Catalog facts"): what market the car was built for, its
    /// diagnostics, airbags, bumpers, timing drive, parts waits, price class
    /// and whether it is road legal. Catalog data, never saved: baked into
    /// Resources/car_facts.json by tools/bake_car_facts.py and keyed by the
    /// CarSpec id. Field names are the JSON keys (JsonUtility).
    ///
    /// Strings rather than enums so a bad bake reads plainly in the file:
    ///   family       jpn / usa / eur (the maker's home; R0a's origin families)
    ///   market       US / JDM / EUR / UK / RACE (every built-to-tune row)
    ///   fuel         carb / mech (mechanical injection) / efi
    ///   eSys         CARB / MECH / EFI-OBD1 / EFI-OBD2 / EFI-JDM / EFI-RACE
    ///   obd          0 no codes, 1 the maker's own system, 2 OBD-II
    ///   obd1Read     none / gm_jumper / toyota_te1_e1 / ecu_led /
    ///                analog_meter / jumper_lamp / dealer
    ///   camDrive     belt / chain / gear / none (rotary); beltMi its interval
    ///   interference yes / no / unknown / n/a
    ///   ignition     points / dist / dis
    ///   turbo        none / single / twin / sequential / twincharged
    ///   airbags      none / driver / dual / dual+side
    ///   bumperEra    chrome / 5mph / 2.5mph / plastic
    ///   ac           none / R-12 / R-134a
    ///   serviceUnit  miles / hours (race hours)
    ///   priceClass   economy 1.0, sport 1.15, exotic 2.5, race 3.0
    ///   partsWaitMin/Max  days for a part ordered for this car
    ///   lowConf      the fields the author was not sure of (comma list)
    /// </summary>
    [Serializable]
    public class CarFact
    {
        public string id, name, origin, family, market;
        public int year;
        public bool builtToTune, roadLegal;
        /// <summary>A one-off show or prototype car: road legal as the catalog
        /// treats it, but never offered for sale in the career (owner 2026-11-12).</summary>
        public bool oneOff;
        /// <summary>An engine put in by a previous owner (empty = the car's own). The swapped
        /// engine's ECU decides how codes are read (owner 2026-11-12: the 240SXs).</summary>
        public string swap;
        public string serviceUnit;
        public float priceClass;
        public string priceClassName;
        public int stockInternals;
        public float stockBoostBar;
        public string engineCode, fuel, eSys;
        public int obd;
        public string obd1Read, camDrive;
        public int beltMi;
        public string interference, ignition, turbo, airbags, rearBrakes, bumperEra, ac, partsSource;
        public int partsWaitMin, partsWaitMax;
        public string knownIssues, lowConf, note;

        public bool IsCarburettor => fuel == "carb";
        public bool IsMechanicalInjection => fuel == "mech";
        public bool IsEfi => fuel == "efi";
        /// <summary>Stores codes someone can read (the maker's way or OBD-II).</summary>
        public bool HasCodes => obd > 0;
        public bool HasObd2 => obd == 2;
        public bool HasAirbag => !string.IsNullOrEmpty(airbags) && airbags != "none";
        public bool HasPassengerAirbag => airbags == "dual" || airbags == "dual+side";
        public bool HasTimingBelt => camDrive == "belt";
        /// <summary>Valves meet pistons if the cam timing goes (a snapped belt
        /// or a jumped chain). False for "no", "unknown" and rotaries.</summary>
        public bool IsInterference => interference == "yes";
        public bool ServicedByHours => serviceUnit == "hours";
        public bool HasAc => !string.IsNullOrEmpty(ac) && ac != "none";
        /// <summary>The author marked this field as uncertain.</summary>
        public bool LowConfidence(string field)
        {
            if (string.IsNullOrEmpty(lowConf) || string.IsNullOrEmpty(field)) return false;
            foreach (var f in lowConf.Split(',')) if (f == field) return true;
            return false;
        }
    }

    /// <summary>
    /// Loader for <see cref="CarFact"/> rows. Later fault rounds read it
    /// (OBD by market and year, airbags, bumper eras, timing belts, parts
    /// waits, price classes, modification hazards). Today only the
    /// self-test validator and the career market read it: race and rally
    /// cars (every row that is not road legal) are not for sale in the
    /// career - the newspaper, the dealer lot and the starting lanes
    /// (owner's answer 9, 2026-11-12). They stay in quick race and debug,
    /// and a career that already owns one keeps it.
    ///
    /// PSX_FACTS_MARKET=0 = today's market (the paper sells anything, the lot
    /// keeps out only "Race Car" rows).
    /// </summary>
    public static class CarFacts
    {
        public const string ResourceName = "car_facts";
        public const int Format = 1;

        /// <summary>The career market filter. Mutable so the self-test can
        /// compare both behaviours in one run.</summary>
        public static bool MarketOn = Environment.GetEnvironmentVariable("PSX_FACTS_MARKET") != "0";

        [Serializable] class Bundle { public int format; public string source; public List<CarFact> rows = new List<CarFact>(); }

        static List<CarFact> all;
        static Dictionary<string, CarFact> byId;
        static int duplicates;

        static void Load()
        {
            if (all != null) return;
            all = new List<CarFact>();
            byId = new Dictionary<string, CarFact>();
            duplicates = 0;
            var text = Resources.Load<TextAsset>(ResourceName);
            if (text == null)
            {
                Debug.LogWarning("CarFacts: Resources/" + ResourceName + ".json missing - the market falls back to the catalog's race/rally names.");
                return;
            }
            try
            {
                var b = JsonUtility.FromJson<Bundle>(text.text);
                if (b != null && b.rows != null) all = b.rows;
            }
            catch (Exception e)
            {
                Debug.LogWarning("CarFacts: could not read " + ResourceName + ".json - " + e.Message);
                all = new List<CarFact>();
            }
            foreach (var f in all)
            {
                if (f == null || string.IsNullOrEmpty(f.id)) continue;
                if (byId.ContainsKey(f.id)) { duplicates++; continue; }
                byId[f.id] = f;
            }
        }

        /// <summary>Forget the loaded file (tests).</summary>
        public static void Reload() { all = null; byId = null; }

        public static IReadOnlyList<CarFact> All { get { Load(); return all; } }
        public static bool Ready { get { Load(); return all.Count > 0; } }
        /// <summary>Rows whose id appeared earlier in the file (should be 0).</summary>
        public static int Duplicates { get { Load(); return duplicates; } }

        public static CarFact Get(string specId)
        {
            Load();
            return specId != null && byId.TryGetValue(specId, out var f) ? f : null;
        }

        public static CarFact Get(CarSpec spec) => spec != null ? Get(spec.id) : null;

        /// <summary>Can be registered and driven on the street. Without a facts
        /// row, the catalog's own rule: no built-to-tune car is.</summary>
        public static bool RoadLegal(CarSpec spec)
        {
            if (spec == null) return false;
            var f = Get(spec.id);
            return f != null ? f.roadLegal : !spec.IsBuiltToTune;
        }

        /// <summary>The career market (newspaper, dealer lot, starting lanes)
        /// may offer this car. Callers check <see cref="MarketOn"/> first.</summary>
        public static bool CareerSells(CarSpec spec)
        {
            if (spec == null || !RoadLegal(spec)) return false;
            var f = Get(spec.id);
            return f == null || !f.oneOff;
        }

        /// <summary>A listing already in a save that must leave the market:
        /// its spec is known and the career does not sell it (not road legal,
        /// or a one-off). Unknown specs are left as they are.</summary>
        public static bool LeavesMarket(string specId)
        {
            if (!MarketOn) return false;
            var spec = CarCatalog.Get(specId);
            return spec != null && !CareerSells(spec);
        }

        /// <summary>Days until a part ordered today arrives (inclusive range
        /// from the row; next morning = 1). 1 without a row.</summary>
        public static int PartsWaitDays(CarSpec spec, System.Random rng)
        {
            var f = Get(spec);
            if (f == null) return 1;
            int lo = Mathf.Max(1, f.partsWaitMin), hi = Mathf.Max(lo, f.partsWaitMax);
            return rng != null ? rng.Next(lo, hi + 1) : lo;
        }

        /// <summary>Parts and labour multiplier (economy 1.0 ... race 3.0).
        /// Without a row: 3.0 for a built-to-tune car, else 1.0.</summary>
        public static float PriceClass(CarSpec spec)
        {
            var f = Get(spec);
            if (f != null && f.priceClass > 0f) return f.priceClass;
            return spec != null && spec.IsBuiltToTune ? 3f : 1f;
        }
    }
}
