using System.Reflection;
using System.Text;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PARTS SHELF (v24). The owner's three questions at a wreck that
    /// refused four pulls with "already maxed" are the acceptance tests:
    ///   1. a spare for when the part on the car wears out - a MAXED car's
    ///      pull still goes on the shelf, and a spare later REPLACES a fault;
    ///   2. "what if I have multiple Civics?" - two of the same model, and the
    ///      part goes on the one picked;
    ///   3. "what if I don't have a Civic right now?" - pull with no car that
    ///      takes it, buy the car later, fit it.
    /// Plus the save (v24 migration, round trip), the scrap price, the shelf's
    /// capacity and the service-sweep protection a shelf job needs.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>The version a new career is born at: what "migrated to the
        /// current version" means in the older migration tests, which pinned
        /// 23 by number until v24 arrived.</summary>
        static int CurrentSaveVersion => new LifeState().saveVersion;

        /// <summary>Only the shelf's pins: seconds, no scene build. Writes
        /// PSXRacing_selftest_spares.txt.</summary>
        public static void RunSpares()
        {
            log = new StringBuilder();
            failures = 0;
            Guard(nameof(TestPartsShelf), TestPartsShelf);
            Guard(nameof(TestPartsShelfSave), TestPartsShelfSave);
            Line(failures == 0 ? "SPARES SELF-TEST OK" : "SPARES SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_spares.txt"), log.ToString());
        }

        static void TickJobs(LifeState s)
        {
            var m = typeof(LifeRules).GetMethod("TickPendingParts", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) { Check(false, "LifeRules.TickPendingParts is there to resolve jobs"); return; }
            int far = s.day;
            foreach (var p in s.pendingParts) if (p != null && p.readyDay > far) far = p.readyDay;
            s.day = far + 1;
            m.Invoke(null, new object[] { s });
        }

        static CarSpec SpareTestSpec()
        {
            var eg = CarCatalog.Get("honda_civic_sir_ii__eg___91");
            if (eg != null) return eg;
            foreach (var c in CarCatalog.All) if (!c.IsRaceCar && c.CanFitTurboKit) return c;
            return null;
        }

        /// <summary>A wreck slot whose part matches, searched week by week.</summary>
        static bool FindSlot(LifeState s, System.Func<YardPart, bool> want, out int wreck, out int slot)
        {
            for (int week = 0; week < 80; week++)
            {
                for (int w = 0; w < 8; w++)
                    foreach (var row in Junkyard.WreckContents(s, w))
                        if (row.Present && want(row.part)) { wreck = w; slot = row.slot; return true; }
                s.day += 7;
            }
            wreck = slot = -1;
            return false;
        }

        static void TestPartsShelf()
        {
            Line("the parts shelf (v24):");
            var spec = SpareTestSpec();
            if (spec == null) { Check(false, "a catalog car to build Civics from"); return; }

            // ---- 1. A MAXED car: the pull still goes on the shelf ----------
            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            s.cars.Clear(); s.activeCar = "";
            s.money = 500000; s.mechSkill = 7f;
            var maxed = CarMarket.MakeOwnedCar(s, spec, 90, 60000f, spec.price);
            for (var k = (Upgrades.Kind)0; k <= Upgrades.LastKind; k++)
                Upgrades.SetStage(maxed, k, Upgrades.MaxStage);
            s.activeCar = maxed.id;

            if (!FindSlot(s, p => p.IsUpgrade, out int wk, out int sl))
            { Check(false, "a wreck with hardware on it within 80 weeks"); return; }
            var offer = Junkyard.GetPull(s, maxed, spec, wk, sl);
            Check(!offer.can, "a fully built car cannot FIT the next stage of it", offer.blocked);
            Check(offer.blocked == null || !offer.blocked.Contains("maxed"),
                  "and the reason is not 'already maxed'", offer.blocked);
            Check(offer.canKeep, "but the pull can be KEPT as a spare", offer.keepBlocked);

            // Every present slot on every shell this week: nothing refuses a
            // KEEP but money and room.
            int rows = 0, keepable = 0;
            for (int w = 0; w < 8; w++)
                foreach (var row in Junkyard.WreckContents(s, w))
                {
                    if (!row.Present) continue;
                    rows++;
                    var o = Junkyard.GetPull(s, maxed, spec, w, row.slot);
                    if (o.canKeep && (o.blocked == null || !o.blocked.Contains("maxed"))) keepable++;
                }
            Check(rows > 0 && keepable == rows, "every part on every shell can be kept, whatever the car",
                  keepable + "/" + rows);

            int money0 = s.money;
            string err = Junkyard.PullToShelf(s, wk, sl, spec.id, "HONDA CIVIC TEST");
            Check(err == null, "KEEP AS A SPARE goes through on a maxed car", err);
            Check(s.partsShelf.Count == 1, "one part on the shelf", s.partsShelf.Count);
            Check(Junkyard.SlotPulled(s, wk, sl), "and that slot on the shell is spent");
            Check(money0 - s.money == offer.keepPrice, "it cost the KEEP price it was quoted",
                  MenuKit.Money(money0 - s.money) + " vs " + MenuKit.Money(offer.keepPrice));
            var kept = s.partsShelf.Count > 0 ? s.partsShelf[0] : null;
            if (kept != null)
            {
                Check(kept.donorName == "HONDA CIVIC TEST" && kept.donorSpec == spec.id && kept.dayPulled == s.day,
                      "the spare knows its donor and the day", kept.donorName + " d" + kept.dayPulled);
                Check(kept.paid == offer.keepPrice - offer.rental, "and what it cost (tool rental out)",
                      kept.paid + " vs " + (offer.keepPrice - offer.rental));
                Check(kept.grade == offer.part.grade && kept.label == offer.part.label,
                      "and keeps the yard's grade and name", kept.grade + " " + kept.label);
                Check(Spares.CarsThatTakeIt(s, kept).Count == 0,
                      "the maxed car is not listed as able to take it");
                // The fit price is never dearer than keeping it.
            }
            Check(s.pendingParts.Count == 0, "keeping a part queues nothing onto any car", s.pendingParts.Count);
            var stock = new OwnedCar { id = "price_probe", specId = spec.id, displayName = spec.name };
            s.cars.Add(stock);
            var probe = Junkyard.GetPull(s, stock, spec, wk, (sl + 1) % Junkyard.WreckSlots);
            if (probe.part != null && !probe.stripped && !probe.pulled && probe.quote.available)
                Check(probe.price <= probe.keepPrice, "fitting now is never dearer than keeping it",
                      MenuKit.Money(probe.price) + " vs " + MenuKit.Money(probe.keepPrice));
            s.cars.Remove(stock);

            // ---- 2. Two Civics: the part goes on the one picked ------------
            var a = CarMarket.MakeOwnedCar(s, spec, 80, 90000f, spec.price / 3);
            var b = CarMarket.MakeOwnedCar(s, spec, 80, 120000f, spec.price / 3);
            s.mechSkill = 100f;
            var tyres = Spares.Shelve(s, new YardPart
            {
                label = "SPORT TYRES, HALF WORN", upgradeKind = "tires", maxStage = 2, grade = 80, stat = "engine",
            }, spec.id, "TEST DONOR", 120);
            var takers = Spares.CarsThatTakeIt(s, tyres);
            Check(takers.Contains(a) && takers.Contains(b) && !takers.Contains(maxed),
                  "two stock Civics can both take it, the maxed one cannot", takers.Count);
            err = Spares.Fit(s, b, tyres, false);
            Check(err == null, "fitted to the SECOND Civic, the one picked", err);
            Check(!s.partsShelf.Contains(tyres), "and it is off the shelf");
            var jobB = s.pendingParts.Find(p => p.carId == b.id);
            Check(jobB != null && jobB.fromShelf && jobB.upgradeKind == "tires" && jobB.upgradeStage == 1,
                  "the job is on that car: tyres, stage 1, from the shelf",
                  jobB != null ? jobB.upgradeKind + " s" + jobB.upgradeStage : "none");
            Check(!s.pendingParts.Exists(p => p.carId == a.id), "and nothing on the other Civic");
            TickJobs(s);
            Check(b.upTires == 1 && a.upTires == 0, "after the days it is stage 1 on that Civic only",
                  "a " + a.upTires + " / b " + b.upTires);

            // A spare that is too small for where a car already is says so.
            Upgrades.SetStage(a, Upgrades.Kind.Brakes, 3);
            var smallBrakes = Spares.Shelve(s, new YardPart
            { label = "USED PADS + ROTORS", upgradeKind = "brakes", maxStage = 2, grade = 70 }, "", "", 50);
            var past = Spares.Plan(s, a, smallBrakes, false);
            Check(!past.ok && past.reason != null && past.reason.Contains("stage"),
                  "a stage-2 kit on a stage-3 car is refused with the stages named", past.reason);

            // ---- the mechanic: bring your own part, labour only ------------
            var coil = Spares.Shelve(s, new YardPart
            { label = "TAKE-OFF SPRINGS + STRUTS", upgradeKind = "suspension", maxStage = 2, grade = 90 }, "", "", 80);
            int before = s.money;
            var mech = Spares.Plan(s, a, coil, true);
            Check(mech.ok && mech.labour > 0, "the mechanic quotes labour only", mech.labour);
            err = Spares.Fit(s, a, coil, true);
            Check(err == null && before - s.money == mech.labour, "and charges exactly that",
                  err ?? MenuKit.Money(before - s.money));
            Check(CarWhere.Away(s, a), "and keeps the car while it is done");
            TickJobs(s);
            Check(a.upSuspension == 1 && !CarWhere.Away(s, a), "stage 1 suspension, car back", a.upSuspension);

            // ---- 1b. A REPLACEMENT clears the fault --------------------------
            b.tires = 35f;
            b.faults.Add(new CarFault { id = "tire_wear", label = "Uneven Tire Wear", stat = "tires",
                                        add = 30, hidden = false, diagnosed = true });
            var set = Spares.Shelve(s, new YardPart
            { label = "MATCHED SET + TYRES", stat = "tires", add = 45, basePrice = 260, days = 2, grade = 92 },
                "", "THE YARD'S RACK", 200);
            var forFault = Spares.ForFault(s, b, b.faults[b.faults.Count - 1]);
            Check(forFault == set, "the car page finds the spare that replaces its fault",
                  forFault != null ? forFault.label : "none");
            err = Spares.Fit(s, b, set, false, Spares.Use.Replace, "tire_wear");
            Check(err == null, "fitted as a REPLACEMENT", err);
            var job = s.pendingParts.Find(p => p.carId == b.id && p.faultId == "tire_wear");
            Check(job != null && job.fromShelf && job.IsYardPart, "a shelf job, protected from service sweeps");
            Check(b.faults.Exists(f => f.id == "tire_wear"), "the fault is still there while the part waits");
            float tiresBefore = b.tires;
            TickJobs(s);
            Check(!b.faults.Exists(f => f.id == "tire_wear"), "the fault is gone once it is on");
            Check(b.tires > tiresBefore, "and the tyres lane is back up", tiresBefore + " -> " + b.tires);

            // A service on the same lane must not sweep a part you own away
            // (BuyService cancels booked repairs on its lane; a shelf job is
            // a part, not an appointment). Warped rotors on the stage-3 car,
            // a stage-4 kit off the shelf, then a tyre rotation.
            a.faults.Add(new CarFault { id = "rotor_warp", label = "Rotors Warped", stat = "tires",
                                        add = 20, hidden = false, diagnosed = true });
            var bigBrakes = Spares.Shelve(s, new YardPart
            { label = "BIG BRAKE KIT, TRACK CAR", upgradeKind = "brakes", maxStage = 4, grade = 75 }, "", "", 300);
            Check(Spares.ForFault(s, a, a.faults[a.faults.Count - 1]) == bigBrakes,
                  "a stage-4 brake kit can replace warped rotors on a stage-3 car (the stage-2 one cannot)");
            err = Spares.Fit(s, a, bigBrakes, false, Spares.Use.Replace, "rotor_warp");
            var brakeJob = s.pendingParts.Find(p => p.carId == a.id && p.faultId == "rotor_warp");
            Check(err == null && brakeJob != null, "fitted as a replacement", err);
            Check(LifeRules.BuyService(s, a, 2) == null && s.pendingParts.Contains(brakeJob),
                  "a tyre rotation at the mechanic does not cancel the part you own");
            TickJobs(s);
            Check(Upgrades.GetStage(a, Upgrades.Kind.Brakes) == 3,
                  "a replacement does not change the car's stage", Upgrades.GetStage(a, Upgrades.Kind.Brakes));

            // A scrap part keeps the yard's meaning: less back, and a hazard.
            var scrapPart = Spares.Shelve(s, new YardPart
            { label = "PART-WORN PAIR", stat = "tires", add = 18, basePrice = 60, days = 1, grade = 30 }, "", "", 20);
            var cleanPart = Spares.Shelve(s, new YardPart
            { label = "PART-WORN PAIR", stat = "tires", add = 18, basePrice = 60, days = 1, grade = 95 }, "", "", 50);
            Check(Spares.EffectiveAdd(scrapPart) < Spares.EffectiveAdd(cleanPart),
                  "a scrap spare restores less than a clean one",
                  Spares.EffectiveAdd(scrapPart) + " vs " + Spares.EffectiveAdd(cleanPart));
            err = Spares.Fit(s, a, scrapPart, false);
            var scrapJob = s.pendingParts.Find(p => p.carId == a.id && p.label.StartsWith("PART-WORN"));
            Check(err == null && scrapJob != null && scrapJob.junkRisk == Junkyard.FaultRisk(30) && scrapJob.junkRisk > 0,
                  "and carries the yard's fault risk onto the car", scrapJob != null ? scrapJob.junkRisk : -1);

            // ---- scrap sale --------------------------------------------------
            int m0 = s.money;
            int got = Spares.SellForScrap(s, cleanPart);
            Check(got == Mathf.RoundToInt(50 * Spares.ScrapShare) && s.money - m0 == got &&
                  !s.partsShelf.Contains(cleanPart), "scrap pays 40% of what it cost", got);

            // ---- capacity ----------------------------------------------------
            while (s.partsShelf.Count < Spares.Capacity)
                Spares.Shelve(s, new YardPart { label = "FILLER", stat = "hp", add = 5, basePrice = 10, grade = 50 }, "", "", 10);
            Check(Spares.Full(s) && Spares.Shelve(s, new YardPart { label = "ONE MORE" }, "", "", 1) == null,
                  "the shelf holds " + Spares.Capacity + " and refuses the next", s.partsShelf.Count);
            if (FindSlot(s, p => true, out int w2, out int s2))
            {
                var full = Junkyard.GetPull(s, maxed, spec, w2, s2);
                Check(!full.canKeep && full.keepBlocked != null && full.keepBlocked.Contains("full"),
                      "a full shelf says so on the wreck row", full.keepBlocked);
            }

            // ---- 3. No Civic yet: pull, buy the car, fit ---------------------
            var t = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            t.cars.Clear(); t.activeCar = "";
            t.money = 500000; t.mechSkill = 100f;
            if (!FindSlot(t, p => p.IsUpgrade && !(p.label != null && p.label.Contains("TURBO")), out int w3, out int s3))
            { Check(false, "a wreck with bolt-on hardware on it"); return; }
            var noCar = Junkyard.GetPull(t, null, null, w3, s3);
            Check(!noCar.can && noCar.canKeep, "with no car at all: no FIT, but KEEP", noCar.blocked);
            err = Junkyard.PullToShelf(t, w3, s3, spec.id, "HONDA CIVIC TEST");
            Check(err == null && t.partsShelf.Count == 1, "pulled onto the shelf with no car to fit it to", err);
            var later = t.partsShelf.Count > 0 ? t.partsShelf[0] : null;
            if (later == null) return;
            Check(Spares.CarsThatTakeIt(t, later).Count == 0, "nothing takes it yet");
            t.day += 30;   // a month later, the Civic
            var bought = CarMarket.MakeOwnedCar(t, spec, 70, 110000f, spec.price / 4);
            Check(Spares.CarsThatTakeIt(t, later).Contains(bought), "the Civic bought later can take it");
            var kind = Upgrades.KindFromKey(later.upgradeKind);
            Check(Spares.ForUpgrade(t, bought, kind) == later,
                  "and its parts page says the next stage is on the shelf");
            err = Spares.Fit(t, bought, later, false);
            Check(err == null, "fitted to the car bought after the pull", err);
            TickJobs(t);
            Check(Upgrades.GetStage(bought, kind) == 1, "stage 1 " + later.upgradeKind + " on it",
                  Upgrades.GetStage(bought, kind));

            // ---- selling a car with a spare going on: the part comes back ----
            var hack = CarMarket.MakeOwnedCar(t, spec, 60, 150000f, spec.price / 5);
            var going = Spares.Shelve(t, new YardPart
            { label = "MATCHED SET + TYRES", stat = "tires", add = 45, basePrice = 260, days = 2, grade = 77 }, "", "", 140);
            err = Spares.Fit(t, hack, going, false);
            Check(err == null && !t.partsShelf.Contains(going), "a spare going onto the car about to be sold", err);
            int shelfBefore = t.partsShelf.Count;
            // QUICK SELL already refuses a car with work on it ("car is at the
            // shop"); a buyer's OFFER on a listed car does not, and that was
            // the path where a part on its way on went with the car.
            Check(CarMarket.QuickSell(t, hack) != null, "quick sell refuses a car with a spare going on");
            var ad = new CarAd { carId = hack.id, offerAmount = 1000, offerDay = t.day };
            t.carAds.Add(ad);
            err = CarMarket.AcceptOffer(t, ad);
            Check(err == null && t.FindCar(hack.id) == null, "a buyer's offer for it is accepted", err);
            Check(t.partsShelf.Count == shelfBefore + 1 && t.partsShelf.Exists(x => x.id == going.id),
                  "and the spare is back on the shelf, not sold with it", t.partsShelf.Count);
            Check(!t.pendingParts.Exists(j => j.carId == hack.id), "with no job left on a car that is gone");
            var reload = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(t));
            Check(reload.partsShelf.Exists(x => x.id == going.id && x.grade == 77), "and it survives a save");
        }

        static void TestPartsShelfSave()
        {
            Line("the parts shelf in the save (v24):");
            var cur = new LifeState().saveVersion;
            // R0b re-pin: the shelf arrived at v24; later rounds add steps above
            // it (v25 is R0b's), so a save is "at least v24" and lands at the
            // current version.
            Check(cur >= 24, "new careers are born at v24 or later", cur);

            // An old (v23) save: no shelf in the JSON at all.
            var old = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            old.saveVersion = 23;
            string json = JsonUtility.ToJson(old);
            int at = json.IndexOf("\"partsShelf\"");
            Check(at > 0, "the shelf is a field JsonUtility writes");
            var back = JsonUtility.FromJson<LifeState>(json.Replace("\"partsShelf\"", "\"partsShelfGone\""));
            LifeSimManager.Migrate(back);
            Check(back.saveVersion == cur && back.partsShelf != null && back.partsShelf.Count == 0,
                  "a v23 save loads at v24 with an empty shelf", back.saveVersion);
            var nul = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            nul.saveVersion = 23; nul.partsShelf = null;
            LifeSimManager.Migrate(nul);
            Check(nul.partsShelf != null && nul.saveVersion == cur, "and a null shelf becomes a list");

            // A shelf survives the round trip, every field.
            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            Spares.Shelve(s, new YardPart
            {
                label = "TURBO + MANIFOLD, USED", upgradeKind = "power", maxStage = 4, grade = 88,
                donorHint = "off a track car",
            }, "honda_civic_sir_ii__eg___91", "HONDA CIVIC SIR-II (EG) `93", 321);
            var rt = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(s));
            LifeSimManager.Migrate(rt);
            var p = rt.partsShelf.Count == 1 ? rt.partsShelf[0] : null;
            Check(p != null && p.label == "TURBO + MANIFOLD, USED" && p.upgradeKind == "power" && p.maxStage == 4 &&
                  p.grade == 88 && p.paid == 321 && p.donorName == "HONDA CIVIC SIR-II (EG) `93" &&
                  p.IsTurbo && p.id == "sp1",
                  "a spare survives save and load", p != null ? p.id + " " + p.label : "missing");
            Check(rt.spareSeq == 1, "and so does the id counter", rt.spareSeq);
        }
    }
}
