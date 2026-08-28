using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Buying and selling cars, ported from RG2's market modules
    /// (usedPrice.ts, carLot.ts, newspaperGenerator.ts, startingCars.ts,
    /// carAds.ts, finance.ts, credit.ts).
    ///
    /// NOTE — three price formulas coexist here, and that is deliberate in the
    /// source, not an accident to be tidied up:
    ///   * <see cref="DepreciatedPrice"/> is the real depreciation model, used
    ///     ONLY for the starting-car lineup.
    ///   * <see cref="ListingPrice"/> is the simpler condition-scaled number the
    ///     classifieds and the dealer lot quote.
    ///   * <see cref="CarValue"/> is what an owned car is WORTH — it drives
    ///     resale, insurance and quick-sell.
    /// Collapsing them into one would quietly retune the whole economy.
    /// </summary>
    public static class CarMarket
    {
        public const int GameStartYear = 1999;
        public static int GameYear(int day) => GameStartYear + (day - 1) / 365;

        // ---- listings ----
        public const int NewspaperSlots = 5;
        public const float NewCarChance = 0.25f;
        public const float ProblemChance = 0.30f;
        static readonly string[] Problems =
        {
            "Engine knock", "Leaky radiator", "Worn brakes",
            "Bad transmission", "Oil leak", "Cracked windshield",
        };

        // ---- finance ----
        public const float UsedLoanApr = 0.105f;
        public const float NewLoanApr = 0.085f;

        /// <summary>Credit tier: label, APR adjustment, minimum score.</summary>
        public static (string name, float aprAdj) CreditTier(int score) =>
            score >= 720 ? ("EXCELLENT", -0.005f) :
            score >= 660 ? ("GOOD", 0f) :
            score >= 600 ? ("FAIR", 0.015f) :
            score >= 550 ? ("POOR", 0.030f) : ("BAD", 0.060f);

        /// <summary>Standard amortization. r == 0 degenerates to a plain split,
        /// which the closed form cannot express (it divides by zero).</summary>
        public static int LoanPayment(float principal, float apr, int months)
        {
            if (months <= 0) return Mathf.RoundToInt(principal);
            float r = apr / 12f;
            if (r <= 0.00001f) return Mathf.RoundToInt(principal / months);
            float p = Mathf.Pow(1f + r, months);
            return Mathf.RoundToInt(principal * (r * p) / (p - 1f));
        }

        // ================= price models =================

        /// <summary>
        /// Year-by-year depreciation: a car loses 20% in its first year, then
        /// 12% / 8% / 5% as it ages, with a mileage penalty measured against an
        /// expected 12k miles a year. Floors at 5% of MSRP so nothing is free.
        /// </summary>
        public static int DepreciatedPrice(int msrp, int modelYear, int gameYear,
                                           float cond, float mileage)
        {
            int age = Mathf.Max(0, gameYear - modelYear);
            float factor = 1f;
            for (int y = 1; y <= age; y++)
                factor *= y == 1 ? 0.80f : y <= 5 ? 0.88f : y <= 10 ? 0.92f : 0.95f;

            float expectedMiles = age * 12000f;
            float excess = Mathf.Max(0f, mileage - expectedMiles);
            float milePenalty = Mathf.Max(0.40f, 1f - 0.10f * (excess / 50000f));
            float condFactor = 0.35f + Mathf.Clamp01(cond / 100f) * 0.65f;

            int floor = Mathf.Max(300, Mathf.RoundToInt(msrp * 0.05f));
            return Mathf.Max(floor, Mathf.RoundToInt(msrp * factor * milePenalty * condFactor));
        }

        /// <summary>What a seller asks. Deliberately cruder than the
        /// depreciation model — sellers price off condition and a gut feel.</summary>
        public static int ListingPrice(int msrp, int cond, bool hasProblem)
        {
            float p = msrp * (0.3f + cond / 200f);
            if (hasProblem) p *= 0.55f;
            return Mathf.Max(200, Mathf.RoundToInt(p));
        }

        /// <summary>
        /// What an owned car is worth. Weighted toward engine and body (30%
        /// each) because those are what a buyer actually inspects; paint counts
        /// for 25%, which is why a cosmetic repair is a resale decision.
        /// </summary>
        public static int CarValue(OwnedCar car)
        {
            if (car == null) return 0;
            float condMult = (car.engine * 0.30f + car.tires * 0.15f +
                              car.carHP * 0.30f + car.paint * 0.25f) / 100f;
            float mileMult = Mathf.Max(0.20f, 1f - car.odoMiles / 200000f);
            int basePrice = car.catalogPrice > 0 ? car.catalogPrice : car.paidPrice;
            return Mathf.RoundToInt(basePrice * condMult * mileMult);
        }

        public static int LoanPayoff(LifeState s, string carId)
        {
            int total = 0;
            foreach (var l in s.carLoans)
                if (l.carId == carId) total += l.monthlyPayment * l.monthsRemaining;
            return total;
        }

        /// <summary>Odometers that look like a real used car: roughly 10-14k
        /// miles a year with a wide spread, never a suspiciously round zero.</summary>
        public static float RollOdometer(int modelYear, int gameYear)
        {
            int age = Mathf.Max(0, gameYear - modelYear);
            float perYear = 10000f + Random.value * 4000f;
            float variance = 0.7f + Random.value * 0.6f;
            return Mathf.Max(100f, Mathf.Round(age * perYear * variance));
        }

        static float RollDeliveryMiles() => 2f + Mathf.Floor(Random.value * 48f);

        // ================= the classifieds =================

        /// <summary>
        /// Top the paper back up to five cars, dropping anything that expired.
        /// Called from the daily rollover, so the market turns over while the
        /// player is doing something else.
        /// </summary>
        public static void RefreshListings(LifeState s)
        {
            if (!CarCatalog.Ready) return;
            s.newspaper.RemoveAll(l => l.expiresDay < s.day);

            var owned = new HashSet<string>();
            foreach (var c in s.cars) owned.Add(c.specId);
            var listed = new HashSet<string>();
            foreach (var l in s.newspaper) listed.Add(l.specId);

            int gameYear = GameYear(s.day);
            int guard = 0;
            while (s.newspaper.Count < NewspaperSlots && guard++ < 60)
            {
                var spec = CarCatalog.All[Random.Range(0, CarCatalog.All.Count)];
                if (owned.Contains(spec.id) || listed.Contains(spec.id)) continue;

                bool isNew = Random.value < NewCarChance && gameYear - spec.modelYear <= 2;
                float odo = isNew ? RollDeliveryMiles() : RollOdometer(spec.modelYear, gameYear);
                int cond = isNew ? 100
                    : Mathf.Clamp(Mathf.RoundToInt(100f - odo / 2500f + Random.Range(-10, 10)), 15, 100);
                bool hasProblem = !isNew && Random.value < ProblemChance;

                s.newspaper.Add(new CarListing
                {
                    specId = spec.id,
                    displayName = spec.name,
                    price = isNew ? spec.price : ListingPrice(spec.price, cond, hasProblem),
                    cond = cond,
                    odoMiles = odo,
                    isNew = isNew,
                    problem = hasProblem ? Problems[Random.Range(0, Problems.Length)] : "",
                    expiresDay = s.day + 3 + Random.Range(0, 5),
                });
                listed.Add(spec.id);
            }
        }

        // ================= buying =================

        public struct FinanceOption
        {
            public string label;
            public int downPayment;
            public int monthlyPayment;
            public int months;
            public float apr;
            public bool isCash;
        }

        public static List<FinanceOption> FinanceOptions(LifeState s, CarListing listing)
        {
            var opts = new List<FinanceOption>();
            opts.Add(new FinanceOption { label = "CASH", downPayment = listing.price, isCash = true });

            float aprAdj = CreditTier(s.creditScore).aprAdj;
            float apr = (listing.isNew ? NewLoanApr : UsedLoanApr) + aprAdj;
            float downPct = listing.isNew ? 0.10f : 0.15f;
            int months = listing.isNew ? 60 : 48;
            int down = Mathf.RoundToInt(listing.price * downPct);
            int financed = listing.price - down;
            opts.Add(new FinanceOption
            {
                label = "LOAN " + months + "mo @ " + (apr * 100f).ToString("0.0") + "%",
                downPayment = down,
                monthlyPayment = LoanPayment(financed, apr, months),
                months = months,
                apr = apr,
            });
            return opts;
        }

        /// <summary>Returns null on success, or the reason it was refused.</summary>
        public static string Buy(LifeState s, CarListing listing, FinanceOption opt)
        {
            if (s.cars.Count >= s.garageSlots)
                return "garage full (" + s.garageSlots + ")";
            if (s.money < opt.downPayment) return "need " + MenuKit.Money(opt.downPayment);

            var spec = CarCatalog.Get(listing.specId);
            if (spec == null) return "unknown car";

            s.money -= opt.downPayment;
            var car = MakeOwnedCar(s, spec, listing.cond, listing.odoMiles, listing.price);

            if (!opt.isCash)
            {
                s.carLoans.Add(new CarLoan
                {
                    carId = car.id,
                    principal = listing.price - opt.downPayment,
                    monthlyPayment = opt.monthlyPayment,
                    monthsRemaining = opt.months,
                    apr = opt.apr,
                });
            }

            // A disclosed problem is a real fault, not flavour text: the buyer
            // knew, which is why the price was 45% off.
            if (!string.IsNullOrEmpty(listing.problem))
            {
                var f = FaultCatalog.RollWearFault(car, ProblemStat(listing.problem), false,
                                                  "wear", spec.origin);
                if (f != null) car.faults.Add(f);
            }

            // And the problems the seller did NOT disclose. These go on HIDDEN:
            // the car drives worse for them from the first race, and the only
            // way to learn why is to inspect it. That asymmetry is what makes a
            // cheap high-mileage car a gamble rather than a bargain.
            Inspection.SeedHidden(s, car, spec, listing.cond);

            s.newspaper.Remove(listing);
            s.calendarLog.Add("Day " + s.day + ": bought " + spec.name + " " +
                              MenuKit.Money(opt.downPayment) + " down");
            return null;
        }

        static string ProblemStat(string problem) =>
            problem.Contains("brake") || problem.Contains("windshield") ? "hp"
            : problem.Contains("transmission") || problem.Contains("Engine") ||
              problem.Contains("radiator") || problem.Contains("Oil") ? "engine" : "tires";

        public static OwnedCar MakeOwnedCar(LifeState s, CarSpec spec, int cond,
                                            float odoMiles, int paid)
        {
            var car = new OwnedCar
            {
                // The instance id has to be unique — a player can own two of the
                // same model, and every loan, fault and repair keys off it.
                id = spec.id + "#" + (s.day * 1000 + s.cars.Count + Random.Range(0, 999)),
                displayName = spec.name,
                specId = spec.id,
                catalogPrice = spec.price,
                paidPrice = paid,
                odoMiles = odoMiles,
                fuel = Random.Range(30f, 70f),
                engine = cond, tires = cond, carHP = cond, paint = cond,
            };
            s.cars.Add(car);
            if (string.IsNullOrEmpty(s.activeCar)) s.activeCar = car.id;
            return car;
        }

        // ================= selling =================

        public static string QuickSell(LifeState s, OwnedCar car)
        {
            if (s.cars.Count <= 1) return "can't sell your only car";
            if (LifeRules.CarInShop(s, car)) return "car is at the shop";
            int gross = Mathf.RoundToInt(CarValue(car) * 0.5f);
            int payoff = LoanPayoff(s, car.id);
            int net = gross - payoff;
            if (net < 0 && s.money < -net) return "upside down by " + MenuKit.Money(-net);

            s.money += net;
            s.carLoans.RemoveAll(l => l.carId == car.id);
            s.pendingParts.RemoveAll(p => p.carId == car.id);
            s.carAds.RemoveAll(a => a.carId == car.id);
            s.cars.Remove(car);
            if (s.activeCar == car.id) s.activeCar = s.cars[0].id;
            s.calendarLog.Add("Day " + s.day + ": sold " + car.displayName + " " +
                              MenuKit.Money(net));
            return null;
        }

        public static string ListForSale(LifeState s, OwnedCar car)
        {
            if (s.cars.Count <= 1) return "can't sell your only car";
            if (s.carAds.Exists(a => a.carId == car.id)) return "already listed";
            s.carAds.Add(new CarAd
            {
                carId = car.id,
                askPrice = Mathf.RoundToInt(CarValue(car) * 0.9f),
            });
            return null;
        }

        /// <summary>
        /// Daily offer roll. The longer an ad sits the likelier a bite, which is
        /// what makes holding out for a better number a real decision. Weekends
        /// are dead, matching RG2.
        /// </summary>
        public static void GenerateOffers(LifeState s)
        {
            foreach (var ad in s.carAds)
            {
                var car = s.FindCar(ad.carId);
                if (car == null) continue;
                ad.daysListed++;
                if (LifeRules.IsWeekend(s.day)) continue;
                if (ad.offerAmount > 0) continue;      // one live offer at a time

                float chance = Mathf.Min(0.85f, 0.45f + ad.daysListed * 0.10f);
                if (Random.value > chance) continue;

                ad.offerAmount = Mathf.RoundToInt(CarValue(car) * (0.5f + Random.value * 0.45f));
                ad.offerDay = s.day;
                s.mail.Add(new MailItem
                {
                    day = s.day,
                    subject = "OFFER: " + car.displayName,
                    body = "A buyer offers " + MenuKit.Money(ad.offerAmount) +
                           " for your " + car.displayName + ".",
                });
            }
        }

        public static string AcceptOffer(LifeState s, CarAd ad)
        {
            var car = s.FindCar(ad.carId);
            if (car == null) return "car is gone";
            if (s.cars.Count <= 1) return "can't sell your only car";
            int payoff = LoanPayoff(s, car.id);
            int net = ad.offerAmount - payoff;
            if (net < 0 && s.money < -net) return "upside down by " + MenuKit.Money(-net);

            s.money += net;
            s.carLoans.RemoveAll(l => l.carId == car.id);
            s.pendingParts.RemoveAll(p => p.carId == car.id);
            s.cars.Remove(car);
            s.carAds.Remove(ad);
            if (s.activeCar == car.id) s.activeCar = s.cars[0].id;
            s.calendarLog.Add("Day " + s.day + ": sold " + car.displayName + " " +
                              MenuKit.Money(net));
            return null;
        }

        // ================= starting lanes =================

        public class StartingLane
        {
            public string label, blurb;
            public CarSpec spec;
            public int cond, price, down, monthly, months;
            public float odoMiles;
            public bool financed;
        }

        /// <summary>
        /// The four ways a driver arrives at day one. RG2's backstory rule
        /// (H1287) is the important one: the down payment was paid BEFORE the
        /// game started, so picking a financed lane never touches your starting
        /// cash — only the monthly payment follows you.
        /// </summary>
        public static List<StartingLane> RollStartingLanes(int jobDailyPay, int creditScore)
        {
            var lanes = new List<StartingLane>();
            if (!CarCatalog.Ready) return lanes;

            int gameYear = GameStartYear;
            int targetMo = Mathf.Max(80, Mathf.RoundToInt(jobDailyPay * 20 * 0.25f));
            float aprAdj = CreditTier(creditScore).aprAdj;

            // BEATER — cash, high miles, rough, and it ships with a fault.
            var beaterPool = CarCatalog.InPriceBand(400, 3000);
            if (beaterPool.Count == 0) beaterPool = CarCatalog.InPriceBand(0, 8000);
            var beater = CarCatalog.PickFromUpperHalf(beaterPool);
            if (beater != null)
            {
                int cond = Random.Range(15, 40);
                float odo = Random.Range(100000f, 220000f);
                lanes.Add(new StartingLane
                {
                    label = "BEATER",
                    blurb = "Paid off. Runs. Mostly.",
                    spec = beater, cond = cond, odoMiles = odo,
                    price = DepreciatedPrice(beater.price, beater.modelYear, gameYear, cond, odo),
                });
            }

            // USED RELIABLE — 15% down, 48 months.
            var usedPool = CarCatalog.InPriceBand(Mathf.Max(4000, targetMo * 35),
                                                  Mathf.Max(10000, targetMo * 80), 7, gameYear);
            var used = CarCatalog.PickFromUpperHalf(usedPool);
            if (used != null)
            {
                int cond = Random.Range(55, 75);
                float odo = Random.Range(30000f, 90000f);
                int price = DepreciatedPrice(used.price, used.modelYear, gameYear, cond, odo);
                int down = Mathf.RoundToInt(price * 0.15f);
                lanes.Add(new StartingLane
                {
                    label = "USED, RELIABLE",
                    blurb = "Sensible. Boring. Yours in four years.",
                    spec = used, cond = cond, odoMiles = odo, price = price,
                    down = down, months = 48, financed = true,
                    monthly = LoanPayment(price - down, UsedLoanApr + aprAdj, 48),
                });
            }

            // NEW — 10% down, 60 months, and a payment that hurts.
            var newPool = CarCatalog.InPriceBand(Mathf.Max(8000, targetMo * 60),
                                                 Mathf.Max(18000, targetMo * 150), 2, gameYear);
            var fresh = CarCatalog.PickFromUpperHalf(newPool);
            if (fresh != null)
            {
                int down = Mathf.RoundToInt(fresh.price * 0.10f);
                lanes.Add(new StartingLane
                {
                    label = "NEW, ON FINANCE",
                    blurb = "Smells new. Costs new.",
                    spec = fresh, cond = 100, odoMiles = RollDeliveryMiles(), price = fresh.price,
                    down = down, months = 60, financed = true,
                    monthly = LoanPayment(fresh.price - down, NewLoanApr + aprAdj, 60),
                });
            }

            return lanes;
        }

        public static void ApplyStartingLane(LifeState s, StartingLane lane)
        {
            if (lane == null || lane.spec == null) return;
            var car = MakeOwnedCar(s, lane.spec, lane.cond, lane.odoMiles, lane.price);
            s.activeCar = car.id;

            // H1287: the down payment happened before day one. Only the loan follows.
            if (lane.financed)
            {
                s.carLoans.Add(new CarLoan
                {
                    carId = car.id,
                    principal = lane.price - lane.down,
                    monthlyPayment = lane.monthly,
                    monthsRemaining = lane.months,
                    apr = lane.spec.price > 0 ? UsedLoanApr : NewLoanApr,
                });
            }

            // Beater guarantee: a rough car arrives with something already wrong,
            // so the garage is part of the game from the first morning.
            if (lane.cond <= 55)
            {
                var f = FaultCatalog.RollWearFault(car, "engine", false, "wear", lane.spec.origin);
                if (f != null) car.faults.Add(f);
            }
        }
    }
}
