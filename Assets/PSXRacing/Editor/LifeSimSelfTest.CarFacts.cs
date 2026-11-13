using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Fault-system round D (2026-11-12): the catalog facts
    /// (Resources/car_facts.json, Scripts/CarFacts.cs, tools/bake_car_facts.py)
    /// and the owner's answer 9 - race and rally cars leave the career market.
    ///   TestCarFactsData    the validator: every catalog row exactly once;
    ///                       OBD-II only (and always) on US-market 1996+ EFI
    ///                       road cars; no codes on carburettor or mechanical
    ///                       injection rows; airbags by market and year; every
    ///                       built-to-tune row not road legal; bumper eras;
    ///                       service units, price classes, parts waits
    ///   TestCarFactsMarket  200 seeded days of newspaper and dealer lot list
    ///                       no car that is not road legal (and do with
    ///                       PSX_FACTS_MARKET=0); the starting lanes offer none;
    ///                       a career that owns one keeps it
    /// RunCarFacts writes PSXRacing_selftest_carfacts.txt.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        public static void RunCarFacts()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try { RunCarFactsTests(); }
            finally { Edition.Simulate(editionWas); }
            Line(failures == 0 ? "CAR FACTS SELF-TEST OK" : "CAR FACTS SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_carfacts.txt"), log.ToString());
        }

        static void RunCarFactsTests()
        {
            // The rest of the suite sees the same UnityEngine.Random sequence
            // it saw before these were added.
            var randomWas = Random.state;
            bool marketWas = CarFacts.MarketOn;
            try
            {
                Guard(nameof(TestCarFactsData), TestCarFactsData);
                Guard(nameof(TestCarFactsMarket), TestCarFactsMarket);
            }
            finally
            {
                CarFacts.MarketOn = marketWas;
                Random.state = randomWas;
            }
        }

        static void TestCarFactsData()
        {
            Line("car facts (round D): the validator:");
            CarFacts.Reload();
            Check(CarFacts.Ready, "Resources/car_facts.json loads", CarFacts.All.Count);
            Check(CarCatalog.Ready, "the catalog loads", CarCatalog.All.Count);
            if (!CarFacts.Ready || !CarCatalog.Ready) return;

            // Every catalog row exactly once, and nothing else.
            var catalogIds = new HashSet<string>();
            foreach (var c in CarCatalog.All) catalogIds.Add(c.id);
            var missing = new List<string>();
            foreach (var id in catalogIds) if (CarFacts.Get(id) == null) missing.Add(id);
            var extra = new List<string>();
            var seen = new Dictionary<string, int>();
            foreach (var f in CarFacts.All)
            {
                seen.TryGetValue(f.id ?? "", out int n);
                seen[f.id ?? ""] = n + 1;
                if (!catalogIds.Contains(f.id ?? "")) extra.Add(f.id);
            }
            int dupes = 0;
            foreach (var kv in seen) if (kv.Value > 1) dupes++;
            Check(missing.Count == 0, "every catalog row has facts", missing.Count + " missing" + FactsSample(missing));
            Check(extra.Count == 0, "no facts row without a catalog row", extra.Count + FactsSample(extra));
            Check(dupes == 0 && CarFacts.Duplicates == 0, "every row appears exactly once",
                  dupes + " duplicated ids");
            Check(CarFacts.All.Count == CarCatalog.All.Count, "one facts row per catalog row",
                  CarFacts.All.Count + " / " + CarCatalog.All.Count);

            var obd2Bad = new List<string>(); var obd2Missing = new List<string>();
            var carbCodes = new List<string>(); var mechCodes = new List<string>();
            var esysBad = new List<string>(); var bagBad = new List<string>();
            var legalBad = new List<string>(); var unitBad = new List<string>();
            var bumperBad = new List<string>(); var classBad = new List<string>();
            var waitBad = new List<string>(); var beltBad = new List<string>();
            var fieldBad = new List<string>(); var yearBad = new List<string>();
            int obd2 = 0, carb = 0, mech = 0, btt = 0, notLegal = 0, belts = 0, withBags = 0;
            foreach (var spec in CarCatalog.All)
            {
                var f = CarFacts.Get(spec.id);
                if (f == null) continue;
                string id = f.id; int y = f.year; string m = f.market;
                if (y != spec.modelYear) yearBad.Add(id);
                if (!FactsIn(f.family, "jpn", "usa", "eur") || !FactsIn(f.market, "US", "JDM", "EUR", "UK", "RACE") ||
                    !FactsIn(f.fuel, "carb", "mech", "efi") || !FactsIn(f.camDrive, "belt", "chain", "gear", "none") ||
                    !FactsIn(f.interference, "yes", "no", "unknown", "n/a") || !FactsIn(f.ignition, "points", "dist", "dis") ||
                    !FactsIn(f.turbo, "none", "single", "twin", "sequential", "twincharged") ||
                    !FactsIn(f.airbags, "none", "driver", "dual", "dual+side") || !FactsIn(f.rearBrakes, "disc", "drum") ||
                    !FactsIn(f.bumperEra, "chrome", "5mph", "2.5mph", "plastic") || !FactsIn(f.ac, "none", "R-12", "R-134a") ||
                    !FactsIn(f.serviceUnit, "miles", "hours") || string.IsNullOrEmpty(f.engineCode) ||
                    !FactsIn(f.obd1Read, "none", "gm_jumper", "toyota_te1_e1", "ecu_led", "analog_meter", "jumper_lamp", "dealer") ||
                    f.obd < 0 || f.obd > 2 || f.stockInternals < 0 || f.stockInternals > 3)
                    fieldBad.Add(id);

                // OBD-II follows market and model year, not origin (design section 2).
                if (f.obd == 2) obd2++;
                if (f.obd == 2 && !(m == "US" && y >= 1996 && f.IsEfi)) obd2Bad.Add(id);
                // An engine swap brings its own ECU (owner 11-12: the 240SXs), so the swapped car reads codes its way.
                bool swapped = !string.IsNullOrEmpty(f.swap);
                if (m == "US" && y >= 1996 && f.IsEfi && f.roadLegal && f.obd != 2 && !swapped) obd2Missing.Add(id);
                if (f.IsCarburettor) { carb++; if (f.HasCodes || f.obd1Read != "none" || f.eSys != "CARB") carbCodes.Add(id); }
                if (f.IsMechanicalInjection) { mech++; if (f.HasCodes || f.obd1Read != "none" || f.eSys != "MECH") mechCodes.Add(id); }
                string want = f.IsCarburettor ? "CARB" : f.IsMechanicalInjection ? "MECH" : m == "RACE" ? "EFI-RACE"
                            : swapped ? "EFI-JDM" : f.obd == 2 ? "EFI-OBD2" : m == "JDM" ? "EFI-JDM" : "EFI-OBD1";
                if (f.eSys != want || (f.obd == 1) != (f.obd1Read != "none" && f.IsEfi && m != "RACE" && f.obd != 2))
                    esysBad.Add(id);

                // Airbags by market and year.
                if (f.HasAirbag) withBags++;
                if (!f.roadLegal && f.HasAirbag) bagBad.Add(id + " (race car)");
                if ((m == "US" || m == "JDM") && y < 1987 && f.HasAirbag) bagBad.Add(id + " (before 1987)");
                if ((m == "EUR" || m == "UK") && y < 1981 && f.HasAirbag) bagBad.Add(id + " (before 1981)");
                if (m == "US" && y >= 1998 && f.roadLegal && !f.HasPassengerAirbag) bagBad.Add(id + " (US 1998+ not dual)");

                // Built-to-tune rows are not road legal unless authored otherwise.
                if (spec.IsBuiltToTune) btt++;
                if (!f.roadLegal) notLegal++;
                if (f.builtToTune != spec.IsBuiltToTune) legalBad.Add(id + " (builtToTune flag)");
                if (spec.IsBuiltToTune && f.roadLegal && (f.note ?? "").IndexOf("road legal", System.StringComparison.OrdinalIgnoreCase) < 0)
                    legalBad.Add(id);
                if (spec.IsBuiltToTune != (m == "RACE")) legalBad.Add(id + " (RACE market)");
                if (f.ServicedByHours == f.roadLegal) unitBad.Add(id);

                // Bumper eras: US 5-mph bumpers 1973-82, 2.5-mph from 1983, chrome before.
                string b = f.bumperEra;
                if (m == "US" && y >= 1973 && y <= 1982 && f.roadLegal && b != "5mph") bumperBad.Add(id);
                if (b == "2.5mph" && !(m == "US" && y >= 1983)) bumperBad.Add(id);
                if (b == "5mph" && !(m == "US" && y >= 1973 && y <= 1982) && (f.note ?? "").IndexOf("5-mph") < 0) bumperBad.Add(id);
                if (y < 1973 && (b == "5mph" || b == "2.5mph")) bumperBad.Add(id);

                // Classes and waits.
                float pc = f.priceClass;
                if (!(Mathf.Approximately(pc, 1f) || Mathf.Approximately(pc, 1.15f) || Mathf.Approximately(pc, 2.5f) ||
                      Mathf.Approximately(pc, 3f)) || (!f.roadLegal && !Mathf.Approximately(pc, 3f)))
                    classBad.Add(id);
                if (f.partsWaitMin < 1 || f.partsWaitMax < f.partsWaitMin ||
                    (!f.roadLegal && (f.partsWaitMin < 14 || f.partsWaitMax > 28)))
                    waitBad.Add(id);
                if (f.HasTimingBelt) belts++;
                if ((f.HasTimingBelt && f.roadLegal && f.beltMi <= 0) || (!f.HasTimingBelt && f.beltMi != 0))
                    beltBad.Add(id);
            }
            Check(fieldBad.Count == 0, "every field holds a known value", fieldBad.Count + FactsSample(fieldBad));
            Check(yearBad.Count == 0, "model year matches the catalog", yearBad.Count + FactsSample(yearBad));
            Check(obd2Bad.Count == 0, "OBD-II only on US-market 1996+ EFI rows (" + obd2 + " rows)",
                  obd2Bad.Count + FactsSample(obd2Bad));
            Check(obd2Missing.Count == 0, "every US-market 1996+ EFI road car has OBD-II",
                  obd2Missing.Count + FactsSample(obd2Missing));
            Check(carbCodes.Count == 0 && carb > 0, "no codes on carburettor rows (" + carb + " rows)",
                  carbCodes.Count + FactsSample(carbCodes));
            Check(mechCodes.Count == 0, "no codes on mechanical-injection rows (" + mech + " rows)",
                  mechCodes.Count + FactsSample(mechCodes));
            Check(esysBad.Count == 0, "eSys and the code system agree with fuel, market and OBD",
                  esysBad.Count + FactsSample(esysBad));
            Check(bagBad.Count == 0, "airbags by market and year (" + withBags + " rows with a bag)",
                  bagBad.Count + FactsSample(bagBad));
            Check(legalBad.Count == 0 && btt >= 45, "every built-to-tune row (" + btt + ") is not road legal and on the RACE market",
                  legalBad.Count + FactsSample(legalBad));
            Check(notLegal >= btt, "rows not road legal: " + notLegal + " (built-to-tune " + btt + " + authored)", notLegal);
            Check(unitBad.Count == 0, "race-hour service exactly on the rows that are not road legal",
                  unitBad.Count + FactsSample(unitBad));
            Check(bumperBad.Count == 0, "bumper eras (US 5-mph 1973-82, 2.5-mph 1983+, chrome before)",
                  bumperBad.Count + FactsSample(bumperBad));
            Check(classBad.Count == 0, "price classes 1.0 / 1.15 / 2.5 / 3.0, race 3.0", classBad.Count + FactsSample(classBad));
            Check(waitBad.Count == 0, "parts waits: min 1, race parts 14-28 days", waitBad.Count + FactsSample(waitBad));
            Check(beltBad.Count == 0, "belt intervals exactly on belt engines (" + belts + " rows)",
                  beltBad.Count + FactsSample(beltBad));

            // The design's own examples and a few hard pins.
            FactsPin("nissan_240sx__s14___96", f => !f.HasObd2 && f.market == "US" && !string.IsNullOrEmpty(f.swap),
                "the '96 240SX (S14) is a US car with an SR20DET swap: its JDM ECU, not OBD-II (owner 11-12)");
            FactsPin("nissan_240sx__96", f => f.year == 1994 && f.market == "US" && !string.IsNullOrEmpty(f.swap),
                "the 240SX fastback is a '94 US car with an SR20DET swap (owner 11-12)");
            FactsPin("honda_civic_type_r__ek___97", f => !f.HasObd2 && f.market == "JDM", "the Civic Type R EK has no OBD-II (JDM)");
            FactsPin("chevrolet_camaro_ss__69", f => f.IsCarburettor && !f.HasCodes && f.bumperEra == "chrome",
                "a '69 Camaro SS: carburettor, no codes, chrome bumpers");
            FactsPin("dodge_viper_gts__99", f => f.HasObd2 && f.HasPassengerAirbag && f.partsWaitMax == 1,
                "a '99 Viper GTS: OBD-II, dual airbags, parts next morning");
            FactsPin("mazda_787b_race_car__91", f => !f.roadLegal && f.ServicedByHours && Mathf.Approximately(f.priceClass, 3f) &&
                f.partsWaitMin >= 14, "the 787B: not road legal, race hours, race price class, race-part waits");
            FactsPin("subaru_impreza_rally_car__99", f => !f.roadLegal && f.market == "RACE", "a rally car is not road legal");
            FactsPin("mazda_rx_7_type_rs__fd___98", f => f.camDrive == "none" && f.turbo == "sequential",
                "the FD's rotary has no cam drive; sequential twin turbos");
            FactsPin("toyota_supra_rz__97", f => f.HasTimingBelt && f.interference == "no",
                "the 2JZ-GTE: timing belt, non-interference");
            FactsPin("ford_taurus_sho__98", f => f.camDrive == "chain" && f.IsInterference && f.HasObd2,
                "the 3.4 SHO V8: chain, interference, OBD-II");
            var spec240 = CarCatalog.Get("nissan_240sx__96");
            Check(spec240 != null && CarFacts.RoadLegal(spec240) && CarFacts.CareerSells(spec240),
                  "the owner's starter (the 240SX fastback) is road legal and for sale");
        }

        static void FactsPin(string id, System.Func<CarFact, bool> rule, string what)
        {
            var f = CarFacts.Get(id);
            Check(f != null && rule(f), what, f == null ? "no row" : f.market + " " + f.year + " obd " + f.obd);
        }

        static bool FactsIn(string v, params string[] ok)
        {
            foreach (var o in ok) if (v == o) return true;
            return false;
        }

        static string FactsSample(List<string> l) =>
            l.Count == 0 ? "" : ": " + string.Join(", ", l.GetRange(0, Mathf.Min(6, l.Count)));

        static void TestCarFactsMarket()
        {
            Line("car facts: race and rally cars leave the career market (owner's answer 9):");
            const int Days = 200;
            var raceSpec = default(CarSpec);
            foreach (var c in CarCatalog.All) if (c.IsRaceCar) { raceSpec = c; break; }
            var rallySpec = default(CarSpec);
            foreach (var c in CarCatalog.All) if (c.IsRallyCar) { rallySpec = c; break; }

            int[] onListings = new int[1], onBad = new int[1], offBad = new int[1], offListings = new int[1];
            var onOffenders = new List<string>();
            foreach (int seed in new[] { 1101, 2202, 3303 })
            {
                FactsMarketDays(true, seed, Days, onListings, onBad, onOffenders);
                FactsMarketDays(false, seed, Days, offListings, offBad, null);
            }
            Check(onListings[0] > 300, "3 x " + Days + " seeded days put real listings in the paper and on the lot",
                  onListings[0]);
            Check(onBad[0] == 0, "the newspaper and the dealer lot never list a race or rally car over " + Days +
                  " seeded days (x3 seeds)", onBad[0] + FactsSample(onOffenders));
            Check(offBad[0] > 0, "with PSX_FACTS_MARKET=0 the same days list some (the filter is what keeps them out)",
                  offBad[0] + " of " + offListings[0]);

            // An advert an older build placed is withdrawn at the next refresh.
            CarFacts.MarketOn = true;
            Random.InitState(4404);
            var s = LifeRules.SeedNewGame("FACTS", 25, 3);
            if (rallySpec != null)
            {
                s.newspaper.Add(new CarListing { specId = rallySpec.id, displayName = rallySpec.name, price = 9999,
                    cond = 70, odoMiles = 20000f, expiresDay = s.day + 5, problem = "" });
                s.dealerLot.Add(new CarListing { specId = rallySpec.id, displayName = rallySpec.name, price = 9999,
                    cond = 70, odoMiles = 20000f, expiresDay = int.MaxValue, problem = "" });
                s.day++;
                CarMarket.RefreshListings(s);
                CarMarket.RefreshLot(s);
                Check(!s.newspaper.Exists(l => l.specId == rallySpec.id) && !s.dealerLot.Exists(l => l.specId == rallySpec.id),
                      "a rally car advertised by an older build leaves the paper and the lot at the next refresh",
                      rallySpec.name);
            }

            // A career that already owns one keeps it.
            if (raceSpec != null)
            {
                var owned = CarMarket.MakeOwnedCar(s, raceSpec, 80, 1200f, raceSpec.price);
                for (int d = 0; d < 30; d++) { s.day++; CarMarket.RefreshListings(s); CarMarket.RefreshLot(s); }
                Check(s.cars.Contains(owned) && s.cars.Exists(c => c.specId == raceSpec.id),
                      "a career that owns a race car keeps it (30 days of market refreshes)", raceSpec.name);
            }

            // The starting lanes: a well-paid start's NEW lane reached the rally cars.
            int laneBad = 0, laneOffBad = 0, lanesSeen = 0;
            foreach (bool on in new[] { true, false })
            {
                CarFacts.MarketOn = on;
                Random.InitState(5505);
                for (int r = 0; r < 60; r++)
                    foreach (int pay in new[] { 60, 90, 154, 231 })
                        foreach (var lane in CarMarket.RollStartingLanes(pay, 700))
                        {
                            if (lane.spec == null) continue;
                            if (on) lanesSeen++;
                            if (!CarFacts.RoadLegal(lane.spec)) { if (on) laneBad++; else laneOffBad++; }
                        }
            }
            Check(laneBad == 0, "no starting lane offers a race or rally car (" + lanesSeen + " lanes)", laneBad);
            Line("  info starting lanes with PSX_FACTS_MARKET=0 offering one: " + laneOffBad);
            CarFacts.MarketOn = true;
        }

        /// <summary>Seed a new game with the market switch as given and run
        /// the paper and the lot for a number of days, counting listings and
        /// those that are not road legal.</summary>
        static void FactsMarketDays(bool on, int seed, int days, int[] listings, int[] bad, List<string> offenders)
        {
            CarFacts.MarketOn = on;
            Random.InitState(seed);
            var s = LifeRules.SeedNewGame("FACTS", 25, 3);
            var counted = new HashSet<string>();
            for (int d = 0; d <= days; d++)
            {
                if (d > 0)
                {
                    s.day++;
                    CarMarket.RefreshListings(s);
                    CarMarket.RefreshLot(s);
                }
                foreach (var l in s.newspaper) Count("paper", l);
                foreach (var l in s.dealerLot) Count("lot", l);
            }

            void Count(string where, CarListing l)
            {
                if (!counted.Add(where + "|" + l.specId + "|" + l.expiresDay + "|" + l.price)) return;
                listings[0]++;
                var spec = CarCatalog.Get(l.specId);
                if (spec != null && !CarFacts.RoadLegal(spec))
                {
                    bad[0]++;
                    if (offenders != null && offenders.Count < 6) offenders.Add(where + " day " + s.day + " " + spec.name);
                }
            }
        }
    }
}
