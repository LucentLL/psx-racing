using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Going to look at a car somebody is selling: the walk-round, the test
    /// drive, the haggle, and the price that moves as you find things.
    ///
    /// Ported from the monolith's private-seller overlay
    /// (driver_city_charlotte_v8_99_126_89.html, drawSellerOverlay at :49478
    /// and its five actions) rather than from the TS rewrite, which never grew
    /// one. Its five buttons are this file's five verbs: PURCHASE, HAGGLE,
    /// INSPECT, TEST DRIVE, WALK AWAY.
    ///
    /// THE ONE IDEA THAT MAKES IT WORK is that the faults are rolled when you
    /// arrive, not when you buy — so the car has real problems while you are
    /// standing in front of it, and everything you fail to find comes home in
    /// the boot. <see cref="Adopt"/> is therefore not "create a car"; it is
    /// "keep the one you have been looking at".
    ///
    /// Three deliberate departures from the source, all of them bugs there:
    ///   * the monolith throws the advertised condition away at purchase and
    ///     rerolls 80-95 for everybody, so a 40% beater and a 95% cream puff
    ///     arrived identical. Here the advert is honest about condition and
    ///     lies only by omission, which is the whole point of an inspection.
    ///   * it mutates listing.price on the dealer path, baking a fault discount
    ///     into the sticker for ever. The offer lives on the VISIT here.
    ///   * its mid-drive symptom stream is dead code (updateTestDrive is never
    ///     called). <see cref="AfterDrive"/> is the end-of-drive roll, and it
    ///     runs.
    /// </summary>
    [System.Serializable]
    public class Viewing
    {
        /// <summary>Which advert this visit is about. See
        /// <see cref="Viewings.KeyOf"/> — a listing has no id of its own, so
        /// the key is built from what makes it unique in the paper.</summary>
        public string key = "";
        /// <summary>"paper" for the classifieds, "lot" for the dealership.
        /// Kept because the two differ in what they will let you do, not just
        /// in where they are.</summary>
        public string source = "paper";
        /// <summary>The advert's number. NEVER written to — the sticker is the
        /// sticker, and a discount you talked them into is not a price cut for
        /// the next person to read the paper.</summary>
        public int askPrice;
        /// <summary>What the seller will take today, given what you have both
        /// found and said.</summary>
        public int offerPrice;
        public bool haggled;
        public bool lookedOver;
        public bool testDrove;
        /// <summary>Day the visit's activity slot was paid for. -1 until you
        /// actually turn up. Re-entering the same day is free, exactly like
        /// <see cref="Inspection"/>'s own day latch.</summary>
        public int visitDay = -1;
        /// <summary>
        /// The car itself, carrying its faults — NOT in <c>LifeState.cars</c>.
        ///
        /// A phantom OwnedCar rather than fields bolted onto CarListing,
        /// because every tool that could tell the player something about it —
        /// the inspection map, the X-ray, the fault list, the effect
        /// aggregator — is OwnedCar-typed end to end. Giving the visit a real
        /// car means all of it works on a stranger's car with no second
        /// implementation, and buying is then a move rather than a copy.
        /// </summary>
        public OwnedCar car;
    }

    public static class Viewings
    {
        /// <summary>Cost of turning up: one activity slot, once per day, the
        /// same shape and the same reason as opening an inspection.</summary>
        public const int VisitSlots = 1;

        /// <summary>
        /// A listing's identity.
        ///
        /// CarListing has no id — it is generated, held in a list and matched
        /// by reference everywhere else. specId alone is not enough (the same
        /// model can be relisted next week at a different price and it is a
        /// different car), so the expiry day goes in: two listings alive at
        /// once cannot share both.
        /// </summary>
        public static string KeyOf(CarListing l) =>
            l == null ? "" : l.specId + "@" + l.expiresDay + "@" + l.price;

        public static Viewing Find(LifeState s, CarListing l)
        {
            if (s == null || l == null) return null;
            string k = KeyOf(l);
            foreach (var v in s.viewings) if (v.key == k) return v;
            return null;
        }

        public static Viewing ByKey(LifeState s, string key)
        {
            if (s == null || string.IsNullOrEmpty(key)) return null;
            foreach (var v in s.viewings) if (v.key == key) return v;
            return null;
        }

        /// <summary>The advert a visit belongs to, or null when it has expired
        /// out of the paper while the player was looking elsewhere.</summary>
        public static CarListing ListingFor(LifeState s, Viewing v)
        {
            if (s == null || v == null) return null;
            foreach (var l in s.newspaper) if (KeyOf(l) == v.key) return l;
            foreach (var l in s.dealerLot) if (KeyOf(l) == v.key) return l;
            return null;
        }

        /// <summary>
        /// Start (or resume) a visit. The faults are rolled ONCE, here, and
        /// live on the visit from then on — going back tomorrow does not give
        /// the car a different set of problems.
        /// </summary>
        public static Viewing Open(LifeState s, CarListing l, string source)
        {
            var v = Find(s, l);
            if (v != null) return v;

            var spec = CarCatalog.Get(l.specId);
            var car = new OwnedCar
            {
                // Prefixed so nothing can mistake it for something the player
                // owns — the id ends up on PendingPart, on loans and on the
                // inspection latches, and a phantom leaking into any of those
                // would be a car in the garage that is not in the garage.
                id = "view#" + KeyOf(l),
                displayName = l.displayName,
                specId = l.specId,
                catalogPrice = spec != null ? spec.price : l.price,
                paidPrice = l.price,
                odoMiles = l.odoMiles,
                fuel = Random.Range(25f, 60f),
                engine = l.cond, tires = l.cond, carHP = l.cond, paint = l.cond,
            };

            string origin = spec != null && !string.IsNullOrEmpty(spec.origin) ? spec.origin : "jpn";
            if (!l.isNew)
                car.faults.AddRange(FaultCatalog.RollUsedFaults(l.cond, l.odoMiles, origin));

            // The advertised problem is the one thing the seller told you, so
            // it arrives already diagnosed and already in the asking price —
            // the listing was 45% off for it. Same rule CarMarket.Buy had.
            if (!string.IsNullOrEmpty(l.problem))
            {
                var f = FaultCatalog.RollWearFault(car, ProblemStat(l.problem), false, "wear", origin);
                if (f != null) { f.hidden = false; f.diagnosed = true; car.faults.Add(f); }
            }

            v = new Viewing
            {
                key = KeyOf(l),
                source = source,
                askPrice = l.price,
                car = car,
            };
            v.offerPrice = Reprice(v);
            s.viewings.Add(v);
            return v;
        }

        /// <summary>Turning up costs a slot, once a day. Returns the reason it
        /// could not happen, or null.</summary>
        public static string Arrive(LifeState s, Viewing v)
        {
            if (s == null || v == null) return "no car to see";
            if (v.visitDay == s.day) return null;          // already here today
            // Spend BEFORE stamping the day, for the reason Inspection.Enter
            // spells out: spending the last slot rolls the calendar, and a
            // pre-stamp then writes yesterday onto the visit and charges twice.
            LifeRules.SpendActivitySlot(s);
            v.visitDay = s.day;
            // A new day is a new conversation. The seller has stopped feeling
            // generous and the walk-round is worth doing again on a car you
            // have since learnt something about.
            v.haggled = false;
            return null;
        }

        /// <summary>
        /// The walk-round: kick the tyres, look under the bonnet, run the
        /// engine on the drive.
        ///
        /// Rolls every hidden fault that is NOT test-drive-only. Skill helps a
        /// little, the same 0.003 per point the garage inspection uses, so the
        /// two ways of looking at a car agree about what being good at this is
        /// worth. Once per visit — a second look is a second day.
        /// </summary>
        public static int LookOver(LifeState s, Viewing v)
        {
            if (v == null || v.car == null || v.lookedOver) return 0;
            v.lookedOver = true;
            int found = 0;
            foreach (var f in v.car.faults)
            {
                if (!f.hidden) continue;
                if (FaultCatalog.IsTestDriveOnly(f.id)) continue;
                float p = FaultCatalog.TierDetect(FaultCatalog.TierOf(f.id)) +
                          (s != null ? s.mechSkill : 0f) * 0.003f;
                if (Random.value > Mathf.Clamp(p, 0.05f, 0.95f)) continue;
                f.hidden = false; f.diagnosed = true;
                found++;
            }
            if (found > 0) v.offerPrice = Reprice(v);
            return found;
        }

        /// <summary>
        /// What the drive told you. Every hidden TEST-DRIVE-ONLY fault gets its
        /// one roll, and a find reopens the haggle — which is the whole
        /// economy of asking for the keys: you are buying information you can
        /// then spend.
        /// </summary>
        public static int AfterDrive(LifeState s, Viewing v)
        {
            if (v == null || v.car == null) return 0;
            v.testDrove = true;
            int found = 0;
            foreach (var f in v.car.faults)
            {
                if (!f.hidden) continue;
                if (!FaultCatalog.IsTestDriveOnly(f.id)) continue;
                float p = FaultCatalog.TierDetect(FaultCatalog.TierOf(f.id)) +
                          (s != null ? s.mechSkill : 0f) * 0.002f;
                if (Random.value > Mathf.Clamp(p, 0.05f, 0.95f)) continue;
                f.hidden = false; f.diagnosed = true;
                found++;
            }
            if (found > 0)
            {
                v.offerPrice = Reprice(v);
                v.haggled = false;   // new information, new conversation
            }
            return found;
        }

        /// <summary>Talk them down. 30% flat refusal, otherwise 5-20% off —
        /// the monolith's numbers, and note it is NOT gated on having found
        /// anything: some sellers just want the car gone.</summary>
        public static string Haggle(LifeState s, Viewing v)
        {
            if (v == null) return "nothing to haggle over";
            if (v.haggled) return "they have made their best offer today";
            v.haggled = true;
            if (Random.value < 0.30f) return "they will not budge on the price";
            float disc = 0.80f + Random.value * 0.15f;
            int was = v.offerPrice;
            v.offerPrice = Mathf.Max(200, Mathf.RoundToInt(v.offerPrice * disc));
            return "down to " + MenuKit.Money(v.offerPrice) +
                   " (" + MenuKit.Money(was - v.offerPrice) + " off)";
        }

        /// <summary>
        /// The asking price less what is now openly wrong with it.
        ///
        /// Only DIAGNOSED faults count, which is the asymmetry the whole visit
        /// runs on: a fault the seller has not admitted to and you have not
        /// found is a fault you are paying full price for. Compounded rather
        /// than summed, and floored, so four small problems cannot make a car
        /// free.
        /// </summary>
        public static int Reprice(Viewing v)
        {
            if (v == null) return 0;
            float mult = 1f;
            if (v.car != null)
                foreach (var f in v.car.faults)
                    if (!f.hidden) mult *= FaultCatalog.TierPriceMult(FaultCatalog.TierOf(f.id));
            return Mathf.Max(200, Mathf.RoundToInt(v.askPrice * Mathf.Max(0.35f, mult)));
        }

        /// <summary>Known problems, for the screen. Counted rather than listed
        /// when the caller only wants a headline.</summary>
        public static int KnownFaults(Viewing v)
        {
            int n = 0;
            if (v != null && v.car != null)
                foreach (var f in v.car.faults) if (!f.hidden) n++;
            return n;
        }

        /// <summary>
        /// Buy it. Returns null on success or the reason it was refused.
        ///
        /// The phantom BECOMES the owned car — id and all — rather than being
        /// copied into a fresh one. That is what carries everything you found
        /// (diagnosed) and everything you missed (still hidden) home with the
        /// car, and it is why nothing here calls Inspection.SeedHidden: the
        /// problems were rolled the day you went to look.
        /// </summary>
        public static string Adopt(LifeState s, Viewing v, CarMarket.FinanceOption opt)
        {
            if (s == null || v == null || v.car == null) return "no car";
            if (s.cars.Count >= s.garageSlots) return "garage full (" + s.garageSlots + ")";
            int price = v.offerPrice;
            if (opt.isCash) opt.downPayment = price;
            else opt.downPayment = Mathf.Min(opt.downPayment, price);
            if (s.money < opt.downPayment) return "need " + MenuKit.Money(opt.downPayment);

            s.money -= opt.downPayment;
            var car = v.car;
            car.paidPrice = price;
            // A real instance id at last. The "view#" prefix exists so nothing
            // can mistake a phantom for a car in the garage, and the moment it
            // IS one, that has to stop being true — the id is what every loan,
            // fault repair and inspection latch keys off.
            car.id = car.specId + "#" + (s.day * 1000 + s.cars.Count + Random.Range(0, 999));
            // The latches were taken against the old id and mean nothing now.
            // Left alone they would tell the garage that today's inspection is
            // already open on a car the player has only just bought.
            car.inspectDay = -1;
            car.floorCheckedDay = -1;
            car.proInspectDay = -1;
            car.inspectedSubs.Clear();
            s.cars.Add(car);
            if (string.IsNullOrEmpty(s.activeCar)) s.activeCar = car.id;

            if (!opt.isCash && opt.months > 0)
            {
                s.carLoans.Add(new CarLoan
                {
                    carId = car.id,
                    principal = price - opt.downPayment,
                    monthlyPayment = opt.monthlyPayment,
                    monthsRemaining = opt.months,
                    apr = opt.apr,
                });
            }

            var listing = ListingFor(s, v);
            if (listing != null)
            {
                s.newspaper.Remove(listing);
                s.dealerLot.Remove(listing);
            }
            s.viewings.Remove(v);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": bought " + car.displayName + " " +
                              MenuKit.Money(opt.downPayment) + " down" +
                              (price < v.askPrice
                                  ? " (" + MenuKit.Money(v.askPrice - price) + " under the ad)"
                                  : ""));
            return null;
        }

        /// <summary>Finance quotes against the NEGOTIATED price rather than the
        /// advert's, so a deal you talked them into is a smaller loan and not
        /// just a smaller cash payment.</summary>
        public static List<CarMarket.FinanceOption> FinanceFor(LifeState s, Viewing v)
        {
            var listing = new CarListing
            {
                price = v != null ? v.offerPrice : 0,
                isNew = v != null && v.car != null && v.car.odoMiles < 100f,
            };
            return CarMarket.FinanceOptions(s, listing);
        }

        /// <summary>Drop visits whose advert has gone. Called from the daily
        /// rollover beside the listing sweep — a phantom car for an advert
        /// nobody can reach is a car the save carries for ever.</summary>
        public static void Sweep(LifeState s)
        {
            if (s == null) return;
            s.viewings.RemoveAll(v => ListingFor(s, v) == null);
        }

        static string ProblemStat(string problem) =>
            problem.Contains("brake") || problem.Contains("windshield") ? "hp"
            : problem.Contains("transmission") || problem.Contains("Engine") ||
              problem.Contains("radiator") || problem.Contains("Oil") ? "engine" : "tires";
    }
}
