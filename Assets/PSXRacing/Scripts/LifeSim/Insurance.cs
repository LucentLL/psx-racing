using System;
using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>One line of the driving record (v28, R1c-B): a car claim, a
    /// ticket (AddPoints) or a health claim. Points count for
    /// <see cref="Insurance.PointYears"/> years from <see cref="day"/>.</summary>
    [Serializable]
    public class InsRecord
    {
        public int day;
        /// <summary><see cref="Insurance.KindCar"/>, KindTicket or KindHealth.</summary>
        public string kind = "";
        public string carId = "";
        /// <summary>The incident a car claim is for (CarFault.hitId); 0 = unknown,
        /// when same-day filings on one car share the claim.</summary>
        public int hitId;
        /// <summary>What the insurer paid on it so far.</summary>
        public int paid;
        public int points;
        public string reason = "";
    }

    /// <summary>
    /// ROUND R1c-B INSURANCE (owner, 2026-11-13: "Seat belt on by default, car
    /// insurance (liability minimum) on by default. Health insurance premiums
    /// are selected when getting hired. Deductibles. So each car requires
    /// insurance. Cars with loans require full coverage. using insurance
    /// (health or car) increases premium costs." - and "Just keep it simple").
    ///
    /// CARS: every owned car carries North Carolina's 1999 minimum liability
    /// (25/50/15) unless the player buys FULL coverage (collision +
    /// comprehensive) with a $250 / $500 / $1,000 deductible; a car with a loan
    /// is always FULL (the lender's rule). The monthly premium follows the car's
    /// value and price class (car_facts), the driver's age, the coverage and
    /// the driving record. RECORD: a paid claim puts points on it in the style
    /// of NC's Safe Driver Incentive Plan, and every point surcharges every car
    /// for three years; <see cref="AddPoints"/> is the door for speeding tickets
    /// once there are police.
    ///
    /// HEALTH: one of three plans picked when hired (premium per paycheck,
    /// deductible, co-pay, a most-you-pay per stay), or none. A hospital bill
    /// goes through it (<see cref="CrashHooks.HospitalPayer"/>) and a claim it
    /// paid raises that plan's premium for three years.
    ///
    /// THE HOOKS FILE CLAIMS AND MOVE NO MONEY: the caller charges playerOwes
    /// (<see cref="Charge"/> turns what the player cannot pay into a payment
    /// plan) and, for a write-off, credits insurerPaid. Call TotalLoss BEFORE
    /// removing the car or its loan: the payout pays the lender first.
    ///
    /// Switches: PSX_INSURANCE=0 = today's single fleet-value INSURANCE line,
    /// no claims, no plans, hooks unset; PSX_INS_HEALTH=0 = no health plans
    /// (the player pays every hospital bill); PSX_INS_DEBT=0 = a bill the
    /// player cannot pay takes the money below zero instead of a payment plan.
    /// </summary>
    public static class Insurance
    {
        // ---------------------------------------------------------------- switches
        public static bool On => Flag("PSX_INSURANCE");
        public static bool HealthOn => On && Flag("PSX_INS_HEALTH");
        public static bool DebtOn => On && Flag("PSX_INS_DEBT");
        static bool Flag(string name) => Environment.GetEnvironmentVariable(name) != "0";

        public const string KindCar = "car", KindTicket = "ticket", KindHealth = "health";

        // ---------------------------------------------------------------- cars
        public const int Liability = 0, Full = 1;
        public static readonly int[] Deductibles = { 250, 500, 1000 };
        public const int DefaultDeductible = 500;
        /// <summary>NC's 1999 minimum: $25k a person, $50k an accident, $15k property.</summary>
        public const string MinimumLimits = "25/50/15";

        /// <summary>Liability at the minimum for an economy car and a driver over
        /// thirty with a clean record, $ a month (about $340 a year, NC 1999).</summary>
        public const float LiabilityMonthly = 28f;
        /// <summary>Collision + comprehensive at the $500 deductible: 3% of the
        /// car's value a year, times its price class.</summary>
        public const float PhysicalRateMonthly = 0.0025f;

        public const int PointYears = 3;
        public const int DaysPerYear = 365;
        public static int PointDays => PointYears * DaysPerYear;

        /// <summary>Premium surcharge by points on the record, in the style of
        /// NC's SDIP table (1 point +30% ... 12 points +390%).</summary>
        static readonly float[] SurchargeByPoints =
            { 0f, 0.30f, 0.45f, 0.60f, 0.80f, 1.10f, 1.40f, 1.75f, 2.10f, 2.50f, 2.95f, 3.40f, 3.90f };

        public static float Surcharge(int points) =>
            SurchargeByPoints[Mathf.Clamp(points, 0, SurchargeByPoints.Length - 1)];

        /// <summary>SDIP-style points for one claim, by what the insurer paid:
        /// under $1,800 one, under $3,100 two, more three.</summary>
        public static int ClaimPoints(int paid) => paid <= 0 ? 0 : paid < 1800 ? 1 : paid < 3100 ? 2 : 3;

        public static float AgeFactor(int age) => age < 25 ? 1.5f : age < 30 ? 1.15f : age < 65 ? 1f : 1.1f;

        public static float DeductibleFactor(int ded) => ded <= 250 ? 1.15f : ded >= 1000 ? 0.80f : 1f;

        /// <summary>car_facts priceClass (economy 1.0, sport 1.15, exotic 2.5, race 3.0).</summary>
        public static float ClassFactor(OwnedCar car)
        {
            var spec = car != null && !string.IsNullOrEmpty(car.specId) ? CarCatalog.Get(car.specId) : null;
            return spec == null ? 1f : Mathf.Max(1f, CarFacts.PriceClass(spec));
        }

        public static bool HasLoan(LifeState s, OwnedCar car) =>
            s != null && car != null && s.carLoans != null &&
            s.carLoans.Exists(l => l != null && l.carId == car.id && l.monthsRemaining > 0);

        /// <summary>FULL or LIABILITY; a car with a loan is always FULL.</summary>
        public static int CoverOf(LifeState s, OwnedCar car) =>
            car == null ? Liability : HasLoan(s, car) || car.insCover == Full ? Full : Liability;

        public static int DeductibleOf(OwnedCar car)
        {
            int d = car != null ? car.insDeductible : DefaultDeductible;
            foreach (int k in Deductibles) if (k == d) return d;
            return DefaultDeductible;
        }

        public static string CoverName(LifeState s, OwnedCar car) =>
            CoverOf(s, car) == Full ? "FULL, " + MenuKit.Money(DeductibleOf(car)) + " DED"
                                    : "LIABILITY " + MinimumLimits;

        /// <summary>One car's monthly premium; <paramref name="extraPoints"/> =
        /// what a claim about to be filed would add.</summary>
        public static int MonthlyPremium(LifeState s, OwnedCar car, int extraPoints = 0)
        {
            if (car == null) return 0;
            float cls = ClassFactor(car);
            float liab = LiabilityMonthly * Mathf.Sqrt(cls);
            float phys = 0f;
            if (CoverOf(s, car) == Full)
                phys = Mathf.Max(0, CarMarket.CarValue(car)) * PhysicalRateMonthly * cls *
                       DeductibleFactor(DeductibleOf(car));
            float age = AgeFactor(s != null ? s.age : 30);
            float rec = 1f + Surcharge(Points(s) + Mathf.Max(0, extraPoints));
            return Mathf.RoundToInt((liab + phys) * age * rec);
        }

        public static int MonthlyTotal(LifeState s, int extraPoints = 0)
        {
            int t = 0;
            if (s != null && s.cars != null)
                foreach (var c in s.cars) if (c != null) t += MonthlyPremium(s, c, extraPoints);
            return t;
        }

        /// <summary>Change one car's coverage; null on success, else why not.</summary>
        public static string SetCover(LifeState s, OwnedCar car, int cover)
        {
            if (car == null) return "no car";
            if (cover != Full && HasLoan(s, car)) return "the lender requires full coverage until the loan is paid";
            car.insCover = cover == Full ? Full : Liability;
            return null;
        }

        /// <summary>The next deductible on the ladder ($250 -> $500 -> $1,000 -> $250).</summary>
        public static int NextDeductible(OwnedCar car)
        {
            int d = DeductibleOf(car);
            for (int i = 0; i < Deductibles.Length; i++)
                if (Deductibles[i] == d) return Deductibles[(i + 1) % Deductibles.Length];
            return DefaultDeductible;
        }

        /// <summary>Data only, switch-independent: a car with a loan is held at
        /// FULL (and stays there when the loan ends, until the player changes
        /// it), every deductible is one of the three.</summary>
        public static void Sync(LifeState s)
        {
            if (s == null) return;
            if (s.insRecord == null) s.insRecord = new List<InsRecord>();
            if (s.cars == null) return;
            foreach (var c in s.cars)
            {
                if (c == null) continue;
                if (HasLoan(s, c)) c.insCover = Full;
                c.insDeductible = DeductibleOf(c);
            }
        }

        // ---------------------------------------------------------------- the record
        static bool Live(LifeState s, InsRecord r) => r != null && s.day - r.day < PointDays;

        /// <summary>Points on the record now (car claims and tickets of the last three years).</summary>
        public static int Points(LifeState s)
        {
            if (s == null || s.insRecord == null) return 0;
            int p = 0;
            foreach (var r in s.insRecord) if (Live(s, r) && r.kind != KindHealth) p += Mathf.Max(0, r.points);
            return p;
        }

        /// <summary>The day the next live point comes off (0 = none).</summary>
        public static int NextClearDay(LifeState s)
        {
            int best = 0;
            if (s == null || s.insRecord == null) return 0;
            foreach (var r in s.insRecord)
                if (Live(s, r) && r.kind != KindHealth && r.points > 0)
                {
                    int d = r.day + PointDays;
                    if (best == 0 || d < best) best = d;
                }
            return best;
        }

        /// <summary>Points for something other than a claim - a speeding ticket
        /// once police exist (they are not built). They surcharge every car for
        /// three years like a claim's.</summary>
        public static void AddPoints(LifeState s, int points, string reason)
        {
            if (s == null || points <= 0) return;
            if (s.insRecord == null) s.insRecord = new List<InsRecord>();
            s.insRecord.Add(new InsRecord { day = s.day, kind = KindTicket, points = points, reason = reason ?? "" });
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + points + " point" + (points == 1 ? "" : "s") +
                              " on your driving record" + (string.IsNullOrEmpty(reason) ? "" : " - " + reason));
        }

        /// <summary>The claim already open on this hit (its deductible is paid).</summary>
        public static InsRecord OpenClaim(LifeState s, OwnedCar car, int hitId)
        {
            if (s == null || s.insRecord == null || car == null) return null;
            return s.insRecord.Find(r => r != null && r.kind == KindCar && r.carId == car.id &&
                                         (hitId != 0 ? r.hitId == hitId : r.hitId == 0 && r.day == s.day));
        }

        /// <summary>The points filing <paramref name="paid"/> more on this hit would add.</summary>
        public static int PointsIfFiled(LifeState s, OwnedCar car, int hitId, int paid)
        {
            var r = OpenClaim(s, car, hitId);
            int was = r != null ? r.points : 0;
            return Mathf.Max(0, ClaimPoints((r != null ? r.paid : 0) + paid) - was);
        }

        static int Record(LifeState s, OwnedCar car, int hitId, int paid, string what)
        {
            if (s.insRecord == null) s.insRecord = new List<InsRecord>();
            var r = OpenClaim(s, car, hitId);
            if (r == null)
            {
                r = new InsRecord { day = s.day, kind = KindCar, carId = car.id, hitId = hitId, reason = what ?? "" };
                s.insRecord.Add(r);
            }
            int was = r.points;
            r.paid += paid;
            r.points = Mathf.Max(r.points, ClaimPoints(r.paid));
            int added = r.points - was;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": insurance claim, " + car.displayName + " - the policy paid " +
                              MenuKit.Money(paid) + (added > 0 ? ", +" + added + " point" + (added == 1 ? "" : "s") : ""));
            return added;
        }

        // ---------------------------------------------------------------- car claims
        /// <summary>A crash repair row: struck (R0b's impact cause) or from a located hit (R1a).</summary>
        public static bool IsCrashRow(CarFault f) => f != null && (f.cause == FaultCause.Impact || f.hitId != 0);

        /// <summary>FILE A CLAIM or PAY IT YOURSELF is asked for a crash repair
        /// at Delmar or Crestline on a fully covered car.</summary>
        public static bool ClaimOffered(LifeState s, OwnedCar car, CarFault f, FaultCatalog.Venue v) =>
            On && s != null && car != null && v != FaultCatalog.Venue.Diy && IsCrashRow(f) && CoverOf(s, car) == Full;

        /// <summary>What a repair claim would pay. No side effects.</summary>
        public static InsurancePay RepairPay(LifeState s, OwnedCar car, int cost, int hitId = 0)
        {
            cost = Mathf.Max(0, cost);
            if (!On || car == null || CoverOf(s, car) != Full)
            {
                var all = InsurancePay.PlayerPaysAll(cost);
                all.note = "Liability only: your policy pays nothing on your own car.";
                return all;
            }
            bool met = OpenClaim(s, car, hitId) != null;
            int ded = met ? 0 : Mathf.Min(cost, DeductibleOf(car));
            return new InsurancePay
            {
                insurerPaid = cost - ded, deductible = ded, playerOwes = ded,
                note = met ? "Already a claim: the deductible is paid, the policy pays all of it."
                           : "Full coverage pays it, less the " + MenuKit.Money(DeductibleOf(car)) + " deductible.",
            };
        }

        /// <summary>The RepairClaim hook: files the claim (points) and returns the split. Moves no money.</summary>
        public static InsurancePay FileRepairClaim(LifeState s, OwnedCar car, int cost, int hitId, string what)
        {
            var pay = RepairPay(s, car, cost, hitId);
            if (pay.insurerPaid > 0 && s != null) Record(s, car, hitId, pay.insurerPaid, what);
            return pay;
        }

        /// <summary>Book a shop repair on a claim: the player pays the deductible
        /// (nothing more if this hit is already a claim), the insurer the rest.
        /// Null on success, else why not.</summary>
        public static string OrderRepairOnClaim(LifeState s, OwnedCar car, CarFault f, FaultCatalog.Venue v)
        {
            if (s == null || car == null || f == null) return "no car";
            var q = FaultCatalog.GetQuote(s, car, f, v);
            if (!q.available) return q.blockedReason;
            var pay = RepairPay(s, car, q.price, f.hitId);
            if (s.money < pay.playerOwes) return "need " + MenuKit.Money(pay.playerOwes);
            // The insurer's cheque goes to the shop with the player's share, so
            // OrderRepair takes the whole quote and books the job as ever.
            s.money += pay.insurerPaid;
            string err = LifeRules.OrderRepair(s, car, f, v);
            if (err != null) { s.money -= pay.insurerPaid; return err; }
            if (pay.insurerPaid > 0) Record(s, car, f.hitId, pay.insurerPaid, f.label);
            return null;
        }

        /// <summary>A write-off: full coverage pays the car's value less the
        /// deductible, the lender first. insurerPaid = the cheque the PLAYER
        /// gets, playerOwes = what the payout left on the loan. With
        /// <paramref name="file"/> the loan is closed and the claim recorded.</summary>
        public static InsurancePay TotalLossPay(LifeState s, OwnedCar car, int value, bool file)
        {
            value = Mathf.Max(0, value);
            if (!On || car == null || CoverOf(s, car) != Full)
                return new InsurancePay { note = "Liability only: nothing for your own car." };
            int ded = Mathf.Min(value, DeductibleOf(car));
            int payout = value - ded;
            int payoff = HasLoan(s, car) ? CarMarket.LoanPayoff(s, car.id) : 0;
            int toLender = Mathf.Min(payout, payoff);
            var pay = new InsurancePay
            {
                insurerPaid = payout - toLender, deductible = ded, playerOwes = payoff - toLender,
                note = "Full coverage paid " + MenuKit.Money(payout) + " (" + MenuKit.Money(value) + " less the " +
                       MenuKit.Money(ded) + " deductible)" +
                       (payoff > 0 ? ", " + MenuKit.Money(toLender) + " of it to the lender" : "") + ".",
            };
            if (file && s != null)
            {
                if (payoff > 0) s.carLoans.RemoveAll(l => l != null && l.carId == car.id);
                if (payout > 0) Record(s, car, 0, payout, "written off");
            }
            return pay;
        }

        // ---------------------------------------------------------------- health
        public class Plan
        {
            public string key, name;
            /// <summary>$ out of each paycheck (Friday), before any claim rise.</summary>
            public int premium, deductible;
            /// <summary>The player's share of a bill past the deductible.</summary>
            public float copay;
            /// <summary>The most the player pays on one stay, deductible included.</summary>
            public int cap;
        }

        /// <summary>The three plans Tony's offers (1999 employee shares).</summary>
        public static readonly Plan[] Plans =
        {
            new Plan { key = "basic", name = "BASIC", premium = 5, deductible = 1000, copay = 0.30f, cap = 5000 },
            new Plan { key = "standard", name = "STANDARD", premium = 9, deductible = 500, copay = 0.20f, cap = 2500 },
            new Plan { key = "premium", name = "PREMIUM", premium = 15, deductible = 250, copay = 0.10f, cap = 1000 },
        };

        /// <summary>Each health claim of the last three years adds this to the premium.</summary>
        public const float HealthClaimRise = 0.20f;

        public static Plan PlanByKey(string key)
        {
            foreach (var p in Plans) if (p.key == key) return p;
            return null;
        }

        public static Plan PlanOf(LifeState s) => s == null ? null : PlanByKey(s.healthPlan);

        public static void ChoosePlan(LifeState s, string key)
        {
            if (s == null) return;
            var p = PlanByKey(key);
            s.healthPlan = p != null ? p.key : "";
            s.healthAsked = true;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": health plan - " + (p != null ? p.name : "none"));
        }

        public static int HealthClaims(LifeState s)
        {
            if (s == null || s.insRecord == null) return 0;
            int n = 0;
            foreach (var r in s.insRecord) if (Live(s, r) && r.kind == KindHealth) n++;
            return n;
        }

        /// <summary>This plan's premium per paycheck now (0 = no plan).</summary>
        public static int PaycheckPremium(LifeState s) => PaycheckPremium(s, PlanOf(s));

        public static int PaycheckPremium(LifeState s, Plan p) =>
            p == null ? 0 : Mathf.RoundToInt(p.premium * (1f + HealthClaimRise * HealthClaims(s)));

        /// <summary>What payday takes out of a paycheck of <paramref name="net"/> for the plan.</summary>
        public static int TakeFromPaycheck(LifeState s, int net) =>
            HealthOn ? Mathf.Clamp(PaycheckPremium(s), 0, Mathf.Max(0, net)) : 0;

        public static string PlanTerms(Plan p) =>
            p == null ? "No plan: you pay every hospital bill yourself."
                      : MenuKit.Money(p.premium) + " a paycheck, " + MenuKit.Money(p.deductible) + " deductible, " +
                        Mathf.RoundToInt(p.copay * 100f) + "% co-pay after it, at most " + MenuKit.Money(p.cap) +
                        " a stay.";

        /// <summary>A hospital bill through the plan; with <paramref name="file"/>
        /// a claim the plan paid on is recorded (it raises the premium).</summary>
        public static InsurancePay HospitalPay(LifeState s, int bill, bool file)
        {
            bill = Mathf.Max(0, bill);
            var p = HealthOn ? PlanOf(s) : null;
            if (p == null)
            {
                var all = InsurancePay.PlayerPaysAll(bill);
                all.note = "No health plan: the whole bill is yours.";
                return all;
            }
            int ded = Mathf.Min(bill, p.deductible);
            int co = Mathf.RoundToInt((bill - ded) * p.copay);
            int owes = Mathf.Min(ded + co, Mathf.Max(p.cap, ded));
            int paid = bill - owes;
            if (file && paid > 0 && s != null)
            {
                if (s.insRecord == null) s.insRecord = new List<InsRecord>();
                s.insRecord.Add(new InsRecord { day = s.day, kind = KindHealth, paid = paid, reason = "hospital" });
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": health plan paid " + MenuKit.Money(paid) +
                                  " of a " + MenuKit.Money(bill) + " hospital bill");
            }
            return new InsurancePay
            {
                insurerPaid = paid, deductible = ded, playerOwes = owes,
                note = "Your " + p.name + " plan paid " + MenuKit.Money(paid) + "; you owe the " +
                       MenuKit.Money(ded) + " deductible" + (owes > ded ? " and " + MenuKit.Money(owes - ded) + " co-pay" : "") + ".",
            };
        }

        // ---------------------------------------------------------------- what cannot be paid
        public const int PlanMonths = 12;

        /// <summary>
        /// Take a bill the player cannot refuse (a hospital bill, what a
        /// write-off left on a loan): what the money covers is paid now, the
        /// rest goes on a payment plan - a bank loan, interest-free over twelve
        /// months, paid with the bills on the 1st (missing those is the game's
        /// existing strike and credit hit). PSX_INS_DEBT=0: the money goes below
        /// zero. Returns what was not paid now.
        /// </summary>
        public static int Charge(LifeState s, int amount, string what)
        {
            if (s == null || amount <= 0) return 0;
            int now = Mathf.Min(Mathf.Max(0, s.money), amount);
            s.money -= now;
            int owed = amount - now;
            if (owed <= 0) return 0;
            if (!DebtOn) { s.money -= owed; return owed; }
            int monthly = Mathf.CeilToInt(owed / (float)PlanMonths);
            if (s.bankLoans == null) s.bankLoans = new List<BankLoan>();
            s.bankLoans.Add(new BankLoan { principal = owed, monthlyPayment = monthly, monthsRemaining = PlanMonths });
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + MenuKit.Money(owed) + " of " + (what ?? "a bill") +
                              " on a payment plan - " + MenuKit.Money(monthly) + " a month for " + PlanMonths + " months");
            return owed;
        }

        /// <summary>Left to pay on every payment plan and bank loan.</summary>
        public static int DebtLeft(LifeState s)
        {
            int t = 0;
            if (s != null && s.bankLoans != null)
                foreach (var l in s.bankLoans) if (l != null && l.monthsRemaining > 0) t += l.monthlyPayment * l.monthsRemaining;
            return t;
        }

        // ---------------------------------------------------------------- the seam
        /// <summary>Set CrashHooks at startup (player and editor). PSX_INSURANCE=0
        /// leaves them null: the player pays everything, a write-off pays nothing.</summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            CrashHooks.HospitalPayer = HealthOn ? (Func<LifeState, int, InsurancePay>)((s, bill) => HospitalPay(s, bill, true)) : null;
            CrashHooks.RepairClaim = On
                ? (Func<LifeState, OwnedCar, int, InsurancePay>)((s, car, cost) => FileRepairClaim(s, car, cost, 0, "crash repair"))
                : null;
            CrashHooks.TotalLossClaim = On
                ? (Func<LifeState, OwnedCar, int, InsurancePay>)((s, car, value) => TotalLossPay(s, car, value, true))
                : null;
        }
    }
}
