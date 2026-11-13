using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R0b "STOP THE LEAKS" (Fault_System_Design_2026-11-11 §6 "Closing the
    /// leaks", §7, §10's R0b row). The gates: TestNoPartLeak (no part name or
    /// effect number on any player screen outside the allowed ones, with every
    /// fault hidden at every stage; the condition bars are the one listed
    /// exception until R2a); an oil change no longer clears a hidden gearbox
    /// fault; TestEngineDeath with a gearbox and a charging fault (re-pinned in
    /// LifeSimSelfTest.cs); a true NAME WHAT YOU FELT claim reprices and a false
    /// one ends the haggle; the v25 step (an old v24 career in, v25 out, run
    /// twice = same); and every fault-creation path stamps a cause.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Only R0b's pins plus TestEngineDeath and the save ladder:
        /// seconds. Writes PSXRacing_selftest_leaks.txt.</summary>
        public static void RunLeaks()
        {
            log = new StringBuilder();
            failures = 0;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try
            {
                RunLeaksR0bTests();
                Guard(nameof(TestEngineDeath), TestEngineDeath);
                Guard(nameof(TestSaveVersionLadder), TestSaveVersionLadder);
            }
            finally { Edition.Simulate(editionWas); }
            Line(failures == 0 ? "LEAKS SELF-TEST OK" : "LEAKS SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            File.WriteAllText(Path.Combine(Application.dataPath, "../PSXRacing_selftest_leaks.txt"), log.ToString());
        }

        static void RunLeaksR0bTests()
        {
            Guard(nameof(TestFaultClassesAndServices), TestFaultClassesAndServices);
            Guard(nameof(TestUsedIdsAndAds), TestUsedIdsAndAds);
            Guard(nameof(TestFeltClaims), TestFeltClaims);
            Guard(nameof(TestPuddleAndStarter), TestPuddleAndStarter);
            Guard(nameof(TestFaultCauseStamps), TestFaultCauseStamps);
            Guard(nameof(TestFaultIdsMigration), TestFaultIdsMigration);
            Guard(nameof(TestNoPartLeak), TestNoPartLeak);
        }

        /// <summary>Words that name a part (lower case). "engine", "fuel" and
        /// "brakes" are not here: they head the condition bars and the chips.</summary>
        static readonly string[] PartWords =
        {
            "spark plug", "plugs", "timing", "water pump", "gasket", "valve cover", "o2 sensor", "sensor",
            "solenoid", "transmission", "trans ", "manifold", "alternator", "radiator", "hose", "thermostat",
            "carbon", "battery", "strut", "bushing", "control arm", "power steering", "alignment", "rotor",
            "ball joint", "air suspension", "brake pad", "frame", "rust", "clear coat", "bumper", "exhaust",
            "clutch", "gearbox", "oil pan", "rebuild", "fan clutch", "core",
        };

        static readonly Regex EffectNumber = new Regex(
            @"\b(accel|grip|brake|shift|fuel|wear) x\d|no gauges|tach flutter|POWER CUT|\d+% now",
            RegexOptions.IgnoreCase);

        /// <summary>The part words and fault labels a line gives away, or null.</summary>
        static string Names(string text, IEnumerable<string> labels)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string low = text.ToLowerInvariant();
            foreach (var w in PartWords) if (low.Contains(w)) return w;
            if (labels != null)
                foreach (var l in labels)
                    if (!string.IsNullOrEmpty(l) && low.Contains(l.ToLowerInvariant())) return l;
            return null;
        }

        static CarFault Row(string id, string label, string stat, bool hidden = true) =>
            new CarFault { id = id, label = label, stat = stat, hidden = hidden, diagnosed = !hidden, cost = 100, days = 1, add = 10 };

        // =====================================================================
        static void TestFaultClassesAndServices()
        {
            Line("R0b: classes, services by part, the respray and the engine jobs:");
            var unclassed = new List<string>();
            foreach (var id in FaultCatalog.AllIdsWithUsed())
                if (FaultClass.Of(id) == FaultClassKind.Other) unclassed.Add(id);
            Check(unclassed.Count == 0, "every pool and used id has a class (engine-internal, long block, gearbox, ...)",
                  unclassed.Count == 0 ? null : string.Join(",", unclassed));
            Check(FaultLeaks.ByClassOn, "PSX_FAULT_BYCLASS is on for this run");

            // THE GATE: an oil change no longer clears a hidden gearbox fault.
            var s = new LifeState { saveVersion = 25, day = 30, money = 100000 };
            var car = new OwnedCar { id = "svc#1", displayName = "Service Car", specId = "", paidPrice = 9000, engine = 60f, tires = 60f };
            s.cars.Add(car); s.activeCar = car.id;
            car.faults.Add(Row("trans_slip", "Trans Slipping", "engine"));
            car.faults.Add(Row("alternator", "Alternator Failing", "engine"));
            car.faults.Add(Row("spark_plugs", "Spark Plug Fouling", "engine"));
            car.faults.Add(Row(FaultIds.UsedFluidsLow, "Fluid Top-Off Needed", "engine"));
            s.pendingParts.Add(new PendingPart { carId = car.id, faultId = "trans_slip", label = "Trans Slipping",
                                                 stat = "engine", add = 10, readyDay = s.day + 3, venue = CarWhere.VenueMechanic });
            int oil = System.Array.FindIndex(LifeRules.MechanicServices, x => x.name == "OIL CHANGE");
            Check(oil >= 0, "the mechanic sells an OIL CHANGE");
            string err = LifeRules.BuyService(s, car, oil);
            Check(err == null, "an oil change is bought", err);
            var gearbox = car.faults.Find(f => f.id == "trans_slip");
            Check(gearbox != null && gearbox.hidden, "AN OIL CHANGE NO LONGER CLEARS A HIDDEN GEARBOX FAULT (still there, still hidden)");
            Check(car.faults.Exists(f => f.id == "alternator") && car.faults.Exists(f => f.id == "spark_plugs"),
                  "nor the charging or the plugs");
            Check(!car.faults.Exists(f => f.id == FaultIds.UsedFluidsLow), "it does top the fluids up (Fluid Top-Off Needed goes)");
            Check(s.pendingParts.Exists(p => p.carId == car.id && p.faultId == "trans_slip"),
                  "and a booked gearbox job is not cancelled by it");
            int tune = System.Array.FindIndex(LifeRules.MechanicServices, x => x.name == "ENGINE TUNE-UP");
            LifeRules.BuyService(s, car, tune);
            Check(!car.faults.Exists(f => f.id == "spark_plugs") && car.faults.Exists(f => f.id == "trans_slip"),
                  "a tune-up replaces the plugs and nothing of the gearbox");
            car.faults.Add(Row("tire_wear", "Uneven Tire Wear", "tires"));
            car.faults.Add(Row("alignment", "Wheel Alignment Off", "tires"));
            int tyres = System.Array.FindIndex(LifeRules.MechanicServices, x => x.name == "NEW TIRES");
            LifeRules.BuyService(s, car, tyres);
            Check(!car.faults.Exists(f => f.id == "tire_wear") && car.faults.Exists(f => f.id == "alignment"),
                  "new tyres take the tyre row, not the alignment");
            int rot = System.Array.FindIndex(LifeRules.MechanicServices, x => x.name == "TIRE ROTATION");
            LifeRules.BuyService(s, car, rot);
            Check(car.faults.Exists(f => f.id == "alignment"), "a rotation replaces nothing");

            Check(FaultClass.ClearedByRespray(Row("paint_fade", "Clear Coat Peeling", "hp")) &&
                  !FaultClass.ClearedByRespray(Row("paint_bubble", "Paint Bubbling/Rust", "hp")) &&
                  !FaultClass.ClearedByRespray(Row("minor_rust", "Surface Rust Spots", "hp")),
                  "a respray clears the finish (clear coat) rows; rust stays under fresh paint");
            // The respray job itself, through the queue.
            var paintCar = new OwnedCar { id = "svc#2", displayName = "Paint Car", specId = "", paint = 20f };
            s.cars.Add(paintCar);
            paintCar.faults.Add(Row("paint_fade", "Clear Coat Peeling", "hp"));
            paintCar.faults.Add(Row("minor_rust", "Surface Rust Spots", "hp"));
            s.pendingParts.Add(new PendingPart { carId = paintCar.id, label = "RESPRAY", stat = "paint",
                                                 paintSkin = "red", readyDay = s.day - 1, venue = CarWhere.VenuePaint });
            typeof(LifeRules).GetMethod("TickPendingParts", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { s });
            Check(!paintCar.faults.Exists(f => f.id == "paint_fade") && paintCar.faults.Exists(f => f.id == "minor_rust"),
                  "a finished respray took the peeling clear coat and left the rust",
                  string.Join(",", paintCar.faults.ConvertAll(f => f.id)));
        }

        // =====================================================================
        static void TestUsedIdsAndAds()
        {
            Line("R0b: the used rows' own ids, and the paper's phrases:");
            Check(FaultLeaks.UsedIdsOn, "PSX_FAULT_USEDIDS is on for this run");
            int worn = 0, low = 0, wrong = 0;
            foreach (var u in FaultCatalog.UsedFaults)
            {
                if (u.name == "Engine Rebuild Needed" || u.name == "Engine Replacement")
                { if (u.id == FaultIds.UsedEngineWorn) worn++; else wrong++; }
                if (u.name == "Fluid Top-Off Needed") { if (u.id == FaultIds.UsedFluidsLow) low++; else wrong++; }
            }
            Check(worn == 3 && low == 2 && wrong == 0,
                  "the three worn-engine rows and the two top-off rows roll under their own ids",
                  worn + " / " + low + " / wrong " + wrong);
            var slip = FaultCatalog.Effect("trans_slip");
            var we = FaultCatalog.Effect(FaultIds.UsedEngineWorn);
            Check(we.accelMult == slip.accelMult && we.shiftMult <= 1f,
                  "a worn engine keeps its power loss and loses the gearbox's shift delay",
                  we.accelMult + " / " + we.shiftMult);
            Check(FaultCatalog.Effect(FaultIds.UsedFluidsLow).engineWearMult == FaultCatalog.Effect("oil_leak").engineWearMult,
                  "low fluids keep their effect on the engine");
            Check(FaultClass.Of(FaultIds.UsedEngineWorn) == FaultClassKind.EngineInternal &&
                  FaultClass.Of(FaultIds.UsedFluidsLow) == FaultClassKind.LongBlock,
                  "and are classed as the engine's (not TRANSMISSION, not the oil pan)");

            // Homes: the inspection map, the floor list, the test-drive lookup.
            var homes = new HashSet<string>();
            foreach (Inspection.Comp c in System.Enum.GetValues(typeof(Inspection.Comp)))
                foreach (var sub in Inspection.SubsOf(c))
                    if (sub.ids != null) foreach (var id in sub.ids) homes.Add(id);
            var homeless = new List<string>();
            foreach (var id in FaultCatalog.AllIdsWithUsed()) if (!homes.Contains(id)) homeless.Add(id);
            Check(homeless.Count == 0, "every pool and used id has a home on the inspection map",
                  homeless.Count == 0 ? null : string.Join(",", homeless));
            Check(System.Array.IndexOf(FaultSymptoms.FloorIds, FaultIds.UsedFluidsLow) >= 0,
                  "low fluids are on the floor-check list");
            Check(FaultSymptoms.CuesOf(FaultIds.UsedEngineWorn).Exists(c => c.chip == FaultSymptoms.ChipPower),
                  "a worn engine shows as DOWN ON POWER on a drive");

            // The paper: every phrase is the row its words mean, in every family.
            foreach (var fam in new[] { "jpn", "usa", "eur" })
                foreach (var phrase in FaultIds.AdProblems)
                {
                    string id = FaultIds.ForProblem(phrase, fam);
                    var f = id != null ? FaultCatalog.MakeById(id, fam) : null;
                    bool ok = f != null;
                    if (ok && phrase == "Worn brakes") ok = FaultCatalog.Effect(id).brakeMult < 1f;
                    if (ok && phrase == "Bad transmission") ok = FaultClass.Of(id) == FaultClassKind.Gearbox;
                    if (ok && phrase == "Engine knock") ok = FaultClass.Of(id) == FaultClassKind.EngineInternal;
                    if (ok && phrase == "Leaky radiator") ok = id == "cooling_fail";
                    if (ok && phrase == "Oil leak") ok = System.Array.IndexOf(FaultSymptoms.FloorIds, id) >= 0;
                    if (ok && phrase == "Rusty bodywork") ok = FaultClass.Of(id) == FaultClassKind.Body;
                    if (!ok) Check(false, "the phrase \"" + phrase + "\" maps to a matching row (" + fam + ")", id ?? "none");
                }
            Check(true, "every advert phrase maps to a matching row in jpn, usa and eur");
            Check(FaultIds.ForProblem("Cracked windshield", "jpn") == null,
                  "an old paper's \"Cracked windshield\" maps to no row (no glass row until R1a)");
            var adCar = new OwnedCar { id = "ad#1", displayName = "Ad Car", specId = "", odoMiles = 120000f };
            var d1 = FaultIds.Disclose(adCar, "Bad transmission", "usa", 12);
            var d2 = FaultIds.Disclose(adCar, "Bad transmission", "usa", 13);
            Check(d1 != null && d1.id == "trans_slip" && !d1.hidden && d1.diagnosed && d1.cause == FaultCause.Seller,
                  "\"Bad transmission\" goes on as the gearbox row, disclosed by the seller");
            Check(d2 == d1 && adCar.faults.Count == 1, "and saying it twice does not double it");
        }

        // =====================================================================
        static void TestFeltClaims()
        {
            Line("R0b: a test drive names nothing; NAME WHAT YOU FELT:");
            Check(FaultLeaks.FeltOn, "PSX_FAULT_FELT is on for this run");
            var s = new LifeState { saveVersion = 25, day = 40, money = 50000 };
            var v = new Viewing { key = "felt#1", source = "paper", askPrice = 10000, offerPrice = 10000,
                                  visitDay = 40, car = new OwnedCar { id = "view#felt#1", displayName = "Seller Car", specId = "" } };
            s.viewings.Add(v);
            v.car.faults.Add(Row("alignment", "Wheel Alignment Off", "tires"));      // pulls (cheap tier)
            v.car.faults.Add(Row("sport_brake_wear", "Brake Pads Worn", "tires"));  // weak brakes
            v.offerPrice = Viewings.Reprice(v);
            Check(v.offerPrice == 10000, "nothing found yet: the asking price", v.offerPrice);

            // The drive went straight and long, and never braked hard from speed.
            TestDriveFeel.Stage(v.key, FaultSymptoms.CondCruise | FaultSymptoms.CondDrove);
            int found = Viewings.AfterDrive(s, v);
            Check(found == 0 && v.car.faults.TrueForAll(f => f.hidden),
                  "THE DRIVE REVEALS NOTHING BY DICE (every fault still hidden, unnamed)");
            Check(v.driveConds == (FaultSymptoms.CondCruise | FaultSymptoms.CondDrove), "what the drive did is kept", v.driveConds);
            Check(Viewings.DriveRefusedToday(s, v) != null, "one test drive per visit-day");
            Check(Viewings.ClaimsOpen(s, v), "after it the player can name what they felt");

            Viewings.Haggle(s, v);                          // a haggle already spent today ...
            int beforeClaim = v.offerPrice;
            string trueLine = Viewings.Claim(s, v, FaultSymptoms.ChipPull);
            float tier = FaultCatalog.TierPriceMult(FaultCatalog.TierOf("alignment"));
            Check(v.offerPrice == Mathf.Max(200, Mathf.RoundToInt(beforeClaim * tier)),
                  "A TRUE CLAIM REPRICES by that fault's tier (PULLS TO A SIDE on a car that pulled)",
                  beforeClaim + " -> " + v.offerPrice + " x" + tier);
            Check(v.car.faults.Find(f => f.id == "alignment").hidden && Names(trueLine, new[] { "Wheel Alignment Off" }) == null,
                  "and names no part (the fault stays hidden)", trueLine);
            Check(!v.haggled, "and reopens the conversation (new information)");
            Check(Viewings.Reprice(v) < 10000, "the felt fault prices like a find from now on");
            int afterTrue = v.offerPrice;
            Viewings.Claim(s, v, FaultSymptoms.ChipPull);
            Check(v.offerPrice == afterTrue, "the same chip twice moves nothing");

            string falseLine = Viewings.Claim(s, v, FaultSymptoms.ChipBrakes);
            Check(v.offerPrice == afterTrue && v.haggled && !Viewings.ClaimsOpen(s, v),
                  "A FALSE CLAIM ENDS THE HAGGLE for the day (weak brakes, but the drive never braked from speed)", falseLine);
            Check(Viewings.Haggle(s, v) == "they have made their best offer today", "HAGGLE is refused for the rest of the day");
            Check(Viewings.Claim(s, v, FaultSymptoms.ChipHot) == "there is nothing to talk about", "and no more claims today");

            // Next day: a new drive is allowed; yesterday's drive opens no claims.
            s.day++;
            Check(Viewings.DriveRefusedToday(s, v) == null && !Viewings.ClaimsOpen(s, v),
                  "the next day the keys can go out again, and yesterday's drive is spent");

            // A claim that matches nothing on the car at all.
            var w = new Viewing { key = "felt#2", source = "paper", askPrice = 5000, offerPrice = 5000, visitDay = s.day,
                                  car = new OwnedCar { id = "view#felt#2", displayName = "Clean Car", specId = "" } };
            TestDriveFeel.Stage(w.key, 0x3ff);
            Viewings.AfterDrive(s, w);
            Viewings.Claim(s, w, FaultSymptoms.ChipHot);
            Check(w.haggled && w.offerPrice == 5000, "naming a symptom a clean car never had ends the haggle");
        }

        // =====================================================================
        static void TestPuddleAndStarter()
        {
            Line("R0b: the floor check describes the puddle; the starter's line is the symptom:");
            Check(FaultLeaks.PuddleOn, "PSX_FAULT_PUDDLE is on for this run");
            foreach (var id in FaultSymptoms.FloorIds)
            {
                var s = new LifeState { saveVersion = 25, day = 1 };
                var car = new OwnedCar { id = "floor#" + id, displayName = "Floor Car", specId = "" };
                var f = FaultCatalog.MakeById(id, "jpn") ?? Row(id, id, "engine");
                car.faults.Add(f);
                string line = null;
                for (int d = 1; d < 400 && (line == null || line.StartsWith("No leaks")); d++)
                {
                    s.day = d;
                    line = Inspection.FloorCheck(s, car);
                }
                bool sees = line != null && !line.StartsWith("No leaks");
                Check(sees && f.hidden && Names(line, new[] { f.label }) == null,
                      "the floor shows " + id + " as a cue - colour, place, size - and names nothing", line);
            }
            var lanes = new List<string>();
            foreach (var p in FaultCatalog.Pools) if (p.stat == "engine" && !lanes.Contains(p.id)) lanes.Add(p.id);
            lanes.Add(FaultIds.UsedEngineWorn);
            var bad = new List<string>();
            foreach (var id in lanes)
            {
                string phrase = FaultSymptoms.StarterSymptom(id);
                var row = FaultCatalog.MakeById(id, "jpn");
                if (Names(phrase, row != null ? new[] { row.label } : null) != null || phrase.Contains("inspect"))
                    bad.Add(id + ": " + phrase);
            }
            Check(bad.Count == 0, "every engine row's day-one line is a symptom only", bad.Count == 0 ? null : string.Join(" | ", bad));

            // The real door: a rough starting lane.
            CarSpec spec = null;
            foreach (var c in CarCatalog.All) if (c != null && c.price > 0) { spec = c; break; }
            if (spec != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    var s = LifeRules.SeedNewGame("STARTER", 25, LifeRules.DefaultJobIndex);
                    int logs = s.calendarLog.Count;
                    CarMarket.ApplyStartingLane(s, new CarMarket.StartingLane { spec = spec, cond = 40, odoMiles = 150000f, price = 1500 });
                    var car = s.ActiveCar;
                    if (car == null || car.faults.Count == 0 || s.calendarLog.Count == logs) continue;
                    string line = s.calendarLog[s.calendarLog.Count - 1];
                    var f = car.faults[0];
                    Check(f.hidden && Names(line, new[] { f.label }) == null && !line.Contains("inspection"),
                          "the starter car's day-one line names only the symptom", line);
                    Check(f.cause == FaultCause.PreviousOwner && f.bornDay == s.day, "and its fault carries a cause (previous owner, day one)");
                    break;
                }
            }
            else Check(false, "a catalog car for the starting lane");
        }

        // =====================================================================
        static void TestFaultCauseStamps()
        {
            Line("R0b: every fault-creation path stamps a cause (never shown):");
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            CarSpec spec = null;
            foreach (var c in CarCatalog.All) if (c != null && c.price > 0 && c.origin == "jpn") { spec = c; break; }

            // 1. distance wear (the threshold roll)
            {
                var s = new LifeState { saveVersion = 25, day = 21 };
                var car = new OwnedCar { id = "cause#wear", displayName = "Wear", specId = spec != null ? spec.id : "", odoMiles = 90000f };
                var m = typeof(LifeRules).GetMethod("RollThresholdFault", flags);
                for (int i = 0; i < 400 && car.faults.Count == 0; i++)
                    m.Invoke(null, new object[] { s, car, "engine", 1f, 50000f });
                var f = car.faults.Count > 0 ? car.faults[0] : null;
                Check(f != null && f.cause == FaultCause.Wear && f.bornDay == 21 && Mathf.Approximately(f.bornOdo, 90000f),
                      "distance wear: cause, day and odometer", f != null ? f.cause + " d" + f.bornDay + " " + f.bornOdo : "no row");
            }
            // 2. heat
            {
                var s = new LifeState { saveVersion = 25, day = 22 };
                var car = new OwnedCar { id = "cause#heat", displayName = "Heat", specId = spec != null ? spec.id : "", odoMiles = 50000f, engine = 90f };
                RaceHandoff.ClearAll();
                RaceHandoff.HeatReported = true;
                RaceHandoff.EndCoolantPct = 100f;
                RaceHandoff.PeakCelsius = 140f;
                RaceHandoff.HeatEngineDamage = 40f;
                typeof(LifeRules).GetMethod("ApplyHeatResult", flags).Invoke(null, new object[] { s, car, 1000f });
                RaceHandoff.ClearAll();
                LifeRules.lastHeatWarning = null;
                var f = car.faults.Count > 0 ? car.faults[0] : null;
                Check(f != null && f.cause == FaultCause.Heat && f.bornDay == 22, "heat", f != null ? f.cause : "no row");
            }
            // 3. the crash (CrashBank from the handoff): zone, side, speed
            {
                var s = new LifeState { saveVersion = 25, day = 23 };
                var car = new OwnedCar { id = "cause#hit", displayName = "Hit", specId = spec != null ? spec.id : "", odoMiles = 30000f };
                RaceHandoff.ClearAll();
                RaceHandoff.CrashRolled = true;
                RaceHandoff.CrashBumperId = "bumper_crack";
                RaceHandoff.CrashAlignSide = -1;
                RaceHandoff.CrashAlignSeverity = 1;
                RaceHandoff.WorstHitZone = 1;
                RaceHandoff.WorstHitSide = -1;
                RaceHandoff.WorstHitMps = 11.5f;
                CrashBank.Apply(s, car);
                RaceHandoff.ClearAll();
                bool all = car.faults.Count == 2 && car.faults.TrueForAll(f =>
                    f.cause == FaultCause.Impact && f.zone1 == 1 && f.hitSide == -1 && Mathf.Approximately(f.hitMps, 11.5f) && f.bornDay == 23);
                Check(all, "a crash: impact, front left, 11.5 m/s, the day", car.faults.Count);
            }
            // 4. a previous owner (the market's hidden seed)
            {
                var s = new LifeState { saveVersion = 25, day = 24 };
                var car = new OwnedCar { id = "cause#seed", displayName = "Seed", specId = spec != null ? spec.id : "", odoMiles = 170000f };
                for (int i = 0; i < 40 && car.faults.Count == 0; i++) Inspection.SeedHidden(s, car, spec, 20);
                Check(car.faults.Count > 0 && car.faults.TrueForAll(f => f.cause == FaultCause.PreviousOwner && f.bornDay == 24),
                      "SeedHidden: previous owner", car.faults.Count);
            }
            // 5. the used rows and the advert (a visit)
            if (spec != null)
            {
                var s = LifeRules.SeedNewGame("CAUSE", 25, LifeRules.DefaultJobIndex);
                var l = new CarListing { specId = spec.id, displayName = spec.name, price = 3000, cond = 30, odoMiles = 160000f,
                                         expiresDay = s.day + 5, problem = "Oil leak" };
                s.newspaper.Add(l);
                var v = Viewings.Open(s, l, "paper");
                bool used = v.car.faults.Exists(f => f.cause == FaultCause.PreviousOwner);
                bool seller = v.car.faults.Exists(f => f.cause == FaultCause.Seller && !f.hidden);
                Check(used && seller && v.car.faults.TrueForAll(f => !FaultCause.Missing(f)),
                      "a visit: the used rows are the previous owner's, the advert's row the seller's", v.car.faults.Count);
            }
            // 6. the yard part's junk risk
            {
                var s = new LifeState { saveVersion = 25, day = 25, slotIndex = LifeRules.MorningSlot };
                var car = new OwnedCar { id = "cause#yard", displayName = "Yard", specId = spec != null ? spec.id : "", odoMiles = 120000f };
                s.cars.Add(car);
                s.pendingParts.Add(new PendingPart { carId = car.id, label = "USED ALTERNATOR", stat = "engine", add = 5,
                                                     readyDay = s.day - 1, venue = CarWhere.VenueDiy, junkRisk = 100 });
                typeof(LifeRules).GetMethod("TickPendingParts", flags).Invoke(null, new object[] { s });
                var f = car.faults.Count > 0 ? car.faults[0] : null;
                Check(f != null && f.cause == FaultCause.PreviousOwner && f.causeNote.Contains("used part"),
                      "a yard part's hidden fault: the donor's past", f != null ? f.cause + " / " + f.causeNote : "no row");
            }
            // 7. the debug bench
            {
                var car = new OwnedCar { id = "cause#bench", displayName = "Bench", specId = "" };
                DebugCarOps.SetFault(car, spec, "rotor_warp", true);
                Check(car.faults.Count == 1 && car.faults[0].cause == FaultCause.Planted, "the debug bench: planted");
            }
            // 8. the source: every runtime line that adds a fault row has a stamp near it.
            string root = Path.Combine(Application.dataPath, "PSXRacing/Scripts");
            var unstamped = new List<string>();
            var add = new Regex(@"\bfaults\.(Add|AddRange)\(|\bAddFault\(\s*s\s*,");
            foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Replace('\\', '/').Contains("/City/")) continue;
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("//") || t.StartsWith("///") || !add.IsMatch(t)) continue;
                    if (t.Contains("static void AddFault(")) continue;
                    bool near = false;
                    for (int k = System.Math.Max(0, i - 12); k <= System.Math.Min(lines.Length - 1, i + 2) && !near; k++)
                        near = lines[k].Contains("FaultCause.");
                    if (!near) unstamped.Add(Path.GetFileName(path) + ":" + (i + 1));
                }
            }
            Check(unstamped.Count == 0, "every runtime line that adds a fault row stamps a cause next to it",
                  unstamped.Count == 0 ? null : string.Join(", ", unstamped));
        }

        // =====================================================================
        static void TestFaultIdsMigration()
        {
            Line("R0b: the v25 step (an old v24 career in, v25 out, twice = once):");
            var old = new LifeState { saveVersion = 24, day = 80, money = 1234 };
            var a = new OwnedCar { id = "mig#a", displayName = "A", specId = "" };
            a.faults.Add(Row("trans_slip", "Engine Rebuild Needed", "engine", hidden: true));
            a.faults.Add(Row("trans_slip", "Trans Slipping", "engine", hidden: false));
            a.faults.Add(Row("oil_leak", "Fluid Top-Off Needed", "engine", hidden: false));
            a.faults.Add(Row("oil_leak", "Oil Pan Gasket Leak", "engine", hidden: true));
            var b = new OwnedCar { id = "mig#b", displayName = "B", specId = "" };
            b.faults.Add(Row("trans_slip", "Engine Replacement", "engine", hidden: true));
            old.cars.Add(a); old.cars.Add(b);
            var vis = new Viewing { key = "mig#v", askPrice = 900, offerPrice = 900,
                                    car = new OwnedCar { id = "view#mig", displayName = "V", specId = "" } };
            vis.car.faults.Add(Row("trans_slip", "Engine Rebuild Needed", "engine", hidden: true));
            old.viewings.Add(vis);
            old.pendingParts.Add(new PendingPart { carId = a.id, faultId = "trans_slip", label = "Engine Rebuild Needed", stat = "engine", readyDay = 82 });
            old.pendingParts.Add(new PendingPart { carId = a.id, faultId = "trans_slip", label = "Trans Slipping", stat = "engine", readyDay = 83 });

            // What a v24 build wrote: none of v25's fields.
            string json = JsonUtility.ToJson(old);
            json = Regex.Replace(json, "\"(cause|causeNote)\":\"[^\"]*\",?", "");
            json = Regex.Replace(json, "\"(bornDay|bornOdo|zone1|hitSide|hitMps|driveDay|driveConds|saidChips|shutDay)\":-?[0-9.eE+-]+,?", "");
            json = Regex.Replace(json, "\"felt\":\\[\\],?", "");
            json = json.Replace(",}", "}");
            Check(!json.Contains("cause") && !json.Contains("felt"), "the v24 save carries none of v25's fields");
            var s = JsonUtility.FromJson<LifeState>(json);
            Check(s != null && s.saveVersion == 24, "it loads as v24");
            LifeSimManager.Migrate(s);
            Check(s.saveVersion == 25, "and migrates to v25", s.saveVersion);
            var ma = s.cars[0];
            Check(ma.faults[0].id == FaultIds.UsedEngineWorn && ma.faults[0].hidden && !ma.faults[0].diagnosed &&
                  ma.faults[0].label == "Engine Rebuild Needed",
                  "\"Engine Rebuild Needed\" moves to used_engine_worn, still hidden");
            Check(ma.faults[1].id == "trans_slip" && !ma.faults[1].hidden, "a real slipping gearbox stays trans_slip, still found");
            Check(ma.faults[2].id == FaultIds.UsedFluidsLow && !ma.faults[2].hidden && ma.faults[2].diagnosed,
                  "\"Fluid Top-Off Needed\" moves to used_fluids_low, still found");
            Check(ma.faults[3].id == "oil_leak", "a real oil leak stays oil_leak");
            Check(s.cars[1].faults[0].id == FaultIds.UsedEngineWorn, "\"Engine Replacement\" moves too");
            Check(s.viewings[0].car.faults[0].id == FaultIds.UsedEngineWorn && s.viewings[0].felt != null,
                  "a viewing's car moves too, with an empty felt list");
            Check(s.pendingParts[0].faultId == FaultIds.UsedEngineWorn && s.pendingParts[1].faultId == "trans_slip",
                  "a booked repair of the moved row moves with it; the gearbox job does not");
            bool legacy = true;
            foreach (var c in s.cars) foreach (var f in c.faults) legacy &= f.cause == FaultCause.Legacy && f.bornDay == 0;
            foreach (var f in s.viewings[0].car.faults) legacy &= f.cause == FaultCause.Legacy;
            Check(legacy, "every old row is marked legacy (cause unknown)");

            string once = JsonUtility.ToJson(s);
            s.saveVersion = 24;
            LifeSimManager.Migrate(s);
            Check(JsonUtility.ToJson(s) == once, "RUN TWICE = THE SAME (the step re-run on a v25 career changes nothing)");
            var again = JsonUtility.FromJson<LifeState>(json);
            LifeSimManager.MigrateToFaultIds(again);
            LifeSimManager.MigrateToFaultIds(again);
            var twice = JsonUtility.FromJson<LifeState>(json);
            LifeSimManager.MigrateToFaultIds(twice);
            Check(JsonUtility.ToJson(again) == JsonUtility.ToJson(twice), "the step itself is idempotent");
            Check(new LifeState().saveVersion == 25, "a new career is born at v25");
        }

        // =====================================================================
        /// <summary>
        /// THE GATE (design §6 "Closing the leaks"): render every page a
        /// player reads with the car's faults HIDDEN, at both stages (severity
        /// 1 and 2), and with none; any line the hidden faults add or change is
        /// a leak (the condition bars are the one exception until R2a - they
        /// read the lanes, which planting a fault does not touch). With every
        /// fault FOUND, the names may show but no effect number may. Then the
        /// string sources no page shows: the floor, the pre-race line, the
        /// seizure story, the result note, the heat prompt, DEBUG INFO.
        /// </summary>
        static void TestNoPartLeak()
        {
            Line("TestNoPartLeak (exception listed: the condition bars stay until R2a):");
            var all = FaultCatalog.AllIdsWithUsed();
            var labels = new List<string>();
            foreach (var id in all) { var r = FaultCatalog.MakeById(id, "jpn"); if (r != null) labels.Add(r.label); }

            // ---- the pages ----
            LifeSimManager.UseScratchSave("selftest.nopartleak");
            try
            {
                LifeSimManager.StartNewGame("LEAKTEST", 30, LifeRules.DefaultJobIndex);
                var S = LifeSimManager.State;
                S.debugMode = false;
                if (S.cars.Count == 0) LifeRules.SeedFallbackCar(S);
                var car = S.ActiveCar ?? S.cars[0];
                S.activeCar = car.id;
                car.faults.Clear();
                CarSpec spec = CarCatalog.Get(car.specId);
                if (spec == null) foreach (var c in CarCatalog.All) if (c != null && c.price > 0) { spec = c; break; }
                var l = new CarListing { specId = spec.id, displayName = spec.name, price = 4000, cond = 60,
                                         odoMiles = 90000f, expiresDay = S.day + 5 };
                S.newspaper.Add(l);
                var v = Viewings.Open(S, l, "paper");
                v.car.faults.Clear();
                v.offerPrice = Viewings.Reprice(v);
                v.visitDay = S.day;
                v.driveDay = S.day;          // the chips are on the page
                S.activeViewing = v.key;
                RaceHandoff.ClearAll();

                string[] tabs = { "main", "garage", "carmenu", "service", "prerace", "viewing", "inspect", "market",
                                  "dealer", "junkyard", "parts", "specs" };
                // Every render starts from the SAME career (a page that spends a
                // block or refreshes a paper must not read as a leak), with the
                // same dice, and differs only in the faults planted.
                string baseJson = JsonUtility.ToJson(S);
                string carId = car.id, vKey = v.key;
                var stateField = typeof(LifeSimManager).GetField("state", BindingFlags.NonPublic | BindingFlags.Static);
                System.Func<string, int, float, List<string>> render = (tab, mode, sev) =>
                {
                    var st = JsonUtility.FromJson<LifeState>(baseJson);
                    stateField.SetValue(null, st);
                    var c = st.FindCar(carId);
                    var vv = Viewings.ByKey(st, vKey);
                    if (mode > 0 && c != null && vv != null)
                    {
                        Plant(c, all, sev, hidden: mode == 1);
                        Plant(vv.car, all, sev, hidden: mode == 1);
                    }
                    UnityEngine.Random.InitState(4242);
                    return RenderTab(tab, carId);
                };
                var clean = new Dictionary<string, List<string>>();
                foreach (var t in tabs) clean[t] = render(t, 0, 1f);
                int cleanTexts = 0;
                foreach (var t in tabs) if (clean[t] != null) cleanTexts += clean[t].Count;
                Line("    rendered " + tabs.Length + " pages with no faults: " + cleanTexts + " lines of text");

                int leaks = 0, numberNotes = 0;
                foreach (var sev in new[] { 1f, 2f })
                {
                    foreach (var t in tabs)
                    {
                        var got = render(t, 1, sev);
                        if (got == null || clean[t] == null) { Check(false, "page " + t + " renders", "threw"); leaks++; continue; }
                        var was = new HashSet<string>(clean[t]);
                        var wasNum = new HashSet<string>();
                        foreach (var x in clean[t]) wasNum.Add(Regex.Replace(x, @"\d", "#"));
                        foreach (var x in got)
                        {
                            if (was.Contains(x)) continue;
                            if (wasNum.Contains(Regex.Replace(x, @"\d", "#")) && Names(x, labels) == null)
                            {
                                numberNotes++;
                                Line("    note (numbers, R3's gate): " + t + ": \"" + Short(x) + "\"");
                                continue;
                            }
                            leaks++;
                            Check(false, "hidden faults (stage " + sev + ") add nothing to " + t, "\"" + Short(x) + "\"");
                        }
                    }
                }
                Check(leaks == 0, "with every fault HIDDEN at both stages, no page shows anything it did not show " +
                                  "for a car with no faults (" + tabs.Length + " pages)", leaks);
                Line("    number differences noted for R3: " + numberNotes);

                // Found: the names may show, never an effect number.
                int numbers = 0, foundTexts = 0;
                foreach (var t in tabs)
                {
                    var got = render(t, 2, 1f);
                    if (got == null) continue;
                    foundTexts += got.Count;
                    foreach (var x in got)
                        if (EffectNumber.IsMatch(x)) { numbers++; Check(false, "no effect number on " + t, "\"" + Short(x) + "\""); }
                }
                Check(numbers == 0, "with every fault FOUND, no page shows an effect number (" + foundTexts + " lines)");
                // The gate can see a leak: the same pages DO show the faults
                // once they are found, so a hidden one shown would differ.
                Check(foundTexts > cleanTexts, "the pages show found faults (the comparison would catch a hidden one shown)",
                      cleanTexts + " -> " + foundTexts);

                // DEBUG INFO: a debug career only.
                stateField.SetValue(null, JsonUtility.FromJson<LifeState>(baseJson));
                S = LifeSimManager.State;
                RaceHandoff.FromLifeSim = true;
                S.debugMode = false;
                bool plain = FaultLeaks.DebugInfoAllowed();
                S.debugMode = true;
                bool debug = FaultLeaks.DebugInfoAllowed();
                S.debugMode = false;
                RaceHandoff.ClearAll();
                Check(!plain && debug, "DEBUG INFO is offered in a debug career and nowhere else in a career");
            }
            finally
            {
                LifeSimManager.UseCareerSave();
            }

            // ---- the string sources no page shows ----
            var tired = new OwnedCar { id = "leak#cool", displayName = "Tired" };
            CoolingModel.Seed(tired, 100f);
            tired.radiator = 15f; tired.hoses = 20f; tired.fan = 30f;
            foreach (var amb in new[] { 16f, 33f })
            {
                string w = CoolingModel.PreRaceWarning(tired, amb);
                Check(w != null && !w.Contains("%") && Names(w, null) == null, "the pre-race line names no cooling part (" + amb + " C)", w);
            }
            tired.coolant = 20f;
            string dry = CoolingModel.PreRaceWarning(tired, 33f);
            Check(dry != null && !dry.Contains("%") && Names(dry, null) == null, "a low system: no percentage, no part", dry);

            var dead = Bench(coolant: 40f, hose: 20f);
            dead.Seize();
            string story = dead.Story();
            Kill(dead);
            Check(!story.Contains("%") && Names(story, null) == null && !story.Contains("ease off") && !story.Contains("lets it cool"),
                  "the seizure story: what the gauge showed, no part, no percentage, no meaning explained", story.Replace("\n", " / "));

            {
                var s = new LifeState { saveVersion = 25, day = 9 };
                var c = new OwnedCar { id = "leak#heat", displayName = "Low", engine = 90f };
                RaceHandoff.ClearAll();
                RaceHandoff.HeatReported = true;
                RaceHandoff.EndCoolantPct = 20f;
                RaceHandoff.PeakCelsius = 95f;
                LifeRules.lastHeatWarning = null;
                typeof(LifeRules).GetMethod("ApplyHeatResult", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { s, c, 500f });
                RaceHandoff.ClearAll();
                Check(LifeRules.lastHeatWarning == null, "the result note has no coolant percentage (instrument facts only)",
                      LifeRules.lastHeatWarning);
                LifeRules.lastHeatWarning = null;
            }

            // The source: forbidden literals, and effect summaries only behind
            // a debug career, and the one gate for every fault list.
            string root = Path.Combine(Application.dataPath, "PSXRacing/Scripts");
            string[] forbidden = { "POWER CUT", "COOLANT DOWN TO", "Weakest part", "% now", "\"COOLANT AT ",
                                   "noise you cannot place", "worth an inspection", "leaked coolant once it got hot",
                                   "Top it up before a drive", "Radiator, fan and hoses" };
            var hits = new List<string>();
            var fx = new List<string>();
            var filters = new List<string>();
            string[] screens = { "LifeHomeScreen.cs", "LifeHomeScreen.Clubs.cs", "LifeHomeScreen.Spares.cs",
                                 "GarageWorld.cs", "SellerLotWorld.cs", "PauseMenu.cs", "RaceHUD.cs" };
            foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (path.Replace('\\', '/').Contains("/City/")) continue;
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("//") || t.StartsWith("///")) continue;
                    foreach (var f in forbidden) if (t.Contains(f)) hits.Add(name + ":" + (i + 1) + " " + f);
                    if (t.Contains("EffectSummary(") && name != "FaultCatalog.cs" && !name.StartsWith("Debug") &&
                        !t.Contains("debugMode"))
                        fx.Add(name + ":" + (i + 1));
                    if (System.Array.IndexOf(screens, name) >= 0 && Regex.IsMatch(t, @"\.hidden\b"))
                        filters.Add(name + ":" + (i + 1));
                }
            }
            Check(hits.Count == 0, "no runtime string carries a part name, a cooling figure or an effect number " +
                                   "outside inspection (the old leak lines)", hits.Count == 0 ? null : string.Join(" | ", hits));
            Check(fx.Count == 0, "an effect summary reaches a screen only in a debug career",
                  fx.Count == 0 ? null : string.Join(", ", fx));
            Check(filters.Count == 0, "the screens filter faults through FaultView.Known only (no hand-written hidden filter)",
                  filters.Count == 0 ? null : string.Join(", ", filters));
        }

        static string Short(string x) => x == null ? "" : (x.Length > 90 ? x.Substring(0, 90) + "..." : x).Replace("\n", " / ");

        static void Plant(OwnedCar car, List<string> ids, float severity, bool hidden)
        {
            car.faults.Clear();
            foreach (var id in ids)
            {
                var f = FaultCatalog.MakeById(id, "jpn");
                if (f == null) continue;
                f.hidden = hidden; f.diagnosed = !hidden; f.severity = severity;
                FaultCause.Stamp(f, FaultCause.Planted, 1, car.odoMiles, "TestNoPartLeak");
                car.faults.Add(f);
            }
        }

        /// <summary>Build one LifeHomeScreen page in edit mode (the tab set
        /// before Start, as LifeHomePreview does) and return every Text on it;
        /// null when the page threw. Everything it made is destroyed.</summary>
        static List<string> RenderTab(string tab, string carId)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var before = new HashSet<GameObject>(scene.GetRootGameObjects());
            var texts = new List<string>();
            GameObject host = null;
            try
            {
                host = new GameObject("LeakTestHome");
                var screen = host.AddComponent<LifeHomeScreen>();
                var any = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(LifeHomeScreen).GetField("tab", any)?.SetValue(screen, tab);
                typeof(LifeHomeScreen).GetField("garageCarId", any)?.SetValue(screen, carId);
                typeof(LifeHomeScreen).GetField("inspectCarId", any)?.SetValue(screen, carId);
                typeof(LifeHomeScreen).GetMethod("Start", any | BindingFlags.Public)?.Invoke(screen, null);
                foreach (var go in scene.GetRootGameObjects())
                {
                    if (before.Contains(go)) continue;
                    foreach (var t in go.GetComponentsInChildren<Text>(true))
                        if (t != null && !string.IsNullOrEmpty(t.text)) texts.Add(t.text);
                }
            }
            catch (System.Exception e)
            {
                Line("    page " + tab + " threw: " + (e.InnerException ?? e).GetType().Name + " " + (e.InnerException ?? e).Message);
                texts = null;
            }
            finally
            {
                foreach (var go in scene.GetRootGameObjects())
                    if (!before.Contains(go)) Object.DestroyImmediate(go);
            }
            return texts;
        }
    }
}
