using System.Text;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1c-B INSURANCE (2026-11-13): premium math, a loan forcing full cover,
    /// a claim paying less the deductible, points raising the premium and
    /// expiring after three years, the write-off payout, the hospital bill per
    /// plan, no insurance owing all, the payment plan, the v28 step and the
    /// switch. Run alone: RunInsurance -> PSXRacing_selftest_insurance.txt.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        public static void RunInsurance()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            try { RunInsuranceR1cTests(); }
            finally { Edition.Simulate(editionWas); }
            Line(failures == 0 ? "INSURANCE SELF-TEST OK" : "INSURANCE SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_insurance.txt"), log.ToString());
        }

        static void RunInsuranceR1cTests()
        {
            Line("--- R1c-B insurance");
            Guard(nameof(TestInsurancePremium), TestInsurancePremium);
            Guard(nameof(TestInsuranceLoanFull), TestInsuranceLoanFull);
            Guard(nameof(TestInsuranceRepairClaim), TestInsuranceRepairClaim);
            Guard(nameof(TestInsurancePoints), TestInsurancePoints);
            Guard(nameof(TestInsuranceTotalLoss), TestInsuranceTotalLoss);
            Guard(nameof(TestInsuranceHealth), TestInsuranceHealth);
            Guard(nameof(TestInsuranceNoCover), TestInsuranceNoCover);
            Guard(nameof(TestInsuranceSave), TestInsuranceSave);
            Insurance.Install();   // whatever a test did to the switches, the hooks as the game has them
        }

        const string InsEg = "honda_civic_sir_ii__eg___91";

        static LifeState InsCareer(int age = 35) => new LifeState { day = 400, age = age, money = 10000 };

        /// <summary>A car worth exactly <paramref name="price"/> (new, mint, no
        /// miles); specId "" = no car_facts row (price class 1.0).</summary>
        static OwnedCar InsCar(LifeState s, int price, string specId = "")
        {
            var c = new OwnedCar
            {
                id = "ins_" + s.cars.Count, specId = specId, displayName = "TEST CAR " + s.cars.Count,
                catalogPrice = price, paidPrice = price, odoMiles = 0f,
            };
            c.engine = 100; c.tires = 100; c.carHP = 100; c.paint = 100;
            s.cars.Add(c);
            return c;
        }

        static void TestInsurancePremium()
        {
            var s = InsCareer();
            var a = InsCar(s, 10000);
            Check(CarMarket.CarValue(a) == 10000, "the test car is worth its price", CarMarket.CarValue(a));
            Check(Insurance.CoverOf(s, a) == Insurance.Liability && Insurance.MonthlyPremium(s, a) == 28,
                  "liability at the NC minimum: $28 a month (economy, 35, clean)", Insurance.MonthlyPremium(s, a));
            a.insCover = Insurance.Full;
            Check(Insurance.MonthlyPremium(s, a) == 53, "full at $500: $28 + 0.25% of $10,000 = $53", Insurance.MonthlyPremium(s, a));
            a.insDeductible = 250;
            Check(Insurance.MonthlyPremium(s, a) == 57, "a $250 deductible costs more ($57)", Insurance.MonthlyPremium(s, a));
            a.insDeductible = 1000;
            Check(Insurance.MonthlyPremium(s, a) == 48, "a $1,000 deductible costs less ($48)", Insurance.MonthlyPremium(s, a));
            a.insDeductible = 500;
            a.insCover = Insurance.Liability;
            s.age = 22;
            Check(Insurance.MonthlyPremium(s, a) == 42, "a 22-year-old pays half again ($42)", Insurance.MonthlyPremium(s, a));
            s.age = 27;
            Check(Insurance.MonthlyPremium(s, a) == 32, "a 27-year-old pays 15% more ($32)", Insurance.MonthlyPremium(s, a));
            s.age = 35;
            a.insCover = Insurance.Full;

            // the class: the dearest and the cheapest price class at one value
            CarSpec hi = null, lo = null;
            foreach (var sp in CarCatalog.All)
            {
                float pc = CarFacts.PriceClass(sp);
                if (pc >= 2f && hi == null) hi = sp;
                if (pc <= 1.001f && lo == null) lo = sp;
            }
            if (hi != null && lo != null)
            {
                var s2 = InsCareer();
                var h = InsCar(s2, 30000, hi.id); h.insCover = Insurance.Full;
                var l = InsCar(s2, 30000, lo.id); l.insCover = Insurance.Full;
                Check(Insurance.MonthlyPremium(s2, h) > Insurance.MonthlyPremium(s2, l) * 1.8f,
                      "an exotic costs nearly twice an economy car of the same value or more (" + hi.id + " vs " + lo.id + ")",
                      Insurance.MonthlyPremium(s2, h) + " vs " + Insurance.MonthlyPremium(s2, l));
            }
            else Check(false, "the catalog has an exotic and an economy price class");

            var b = InsCar(s, 4000);
            Check(Insurance.MonthlyTotal(s) == 53 + 28 && LifeRules.MonthlyInsurance(s) == Insurance.MonthlyTotal(s),
                  "BILLS' insurance is the sum of the cars' premiums ($81)", LifeRules.MonthlyInsurance(s));
        }

        static void TestInsuranceLoanFull()
        {
            var s = InsCareer();
            var a = InsCar(s, 12000);
            s.carLoans.Add(new CarLoan { carId = a.id, principal = 9000, monthlyPayment = 200, monthsRemaining = 48, apr = 0.08f });
            Check(a.insCover == Insurance.Liability && Insurance.CoverOf(s, a) == Insurance.Full,
                  "a car with a loan is FULL whatever its own setting says");
            Check(Insurance.SetCover(s, a, Insurance.Liability) != null && Insurance.CoverOf(s, a) == Insurance.Full,
                  "and cannot be dropped to liability while the loan runs");
            Insurance.Sync(s);
            Check(a.insCover == Insurance.Full, "the monthly sync holds it at FULL");
            s.carLoans[0].monthsRemaining = 0;
            Check(Insurance.CoverOf(s, a) == Insurance.Full, "a paid-off car stays FULL until the player changes it");
            Check(Insurance.SetCover(s, a, Insurance.Liability) == null && Insurance.CoverOf(s, a) == Insurance.Liability,
                  "and can be dropped once the loan is paid");
        }

        static void TestInsuranceRepairClaim()
        {
            var s = InsCareer();
            var a = InsCar(s, 10000, InsEg);
            var lia = Insurance.RepairPay(s, a, 2000);
            Check(lia.insurerPaid == 0 && lia.playerOwes == 2000, "liability pays nothing on your own car", lia.playerOwes);
            a.insCover = Insurance.Full;
            var p = Insurance.RepairPay(s, a, 2000, 7);
            Check(p.insurerPaid == 1500 && p.deductible == 500 && p.playerOwes == 500,
                  "full coverage pays a $2,000 repair less the $500 deductible", p.insurerPaid + "/" + p.playerOwes);
            var small = Insurance.RepairPay(s, a, 300, 8);
            Check(small.insurerPaid == 0 && small.playerOwes == 300, "a repair under the deductible is all yours");

            var f1 = Insurance.FileRepairClaim(s, a, 2000, 7, "front bumper");
            Check(f1.insurerPaid == 1500 && Insurance.Points(s) == 1, "the claim is filed: $1,500 paid, 1 point", Insurance.Points(s));
            var f2 = Insurance.FileRepairClaim(s, a, 1000, 7, "headlamp");
            Check(f2.insurerPaid == 1000 && f2.playerOwes == 0 && Insurance.Points(s) == 2,
                  "a second repair from the SAME hit: no second deductible; $2,500 in all is 2 points", Insurance.Points(s));
            Check(Insurance.MonthlyPremium(s, a) > 53, "points raise the premium", Insurance.MonthlyPremium(s, a));

            // at the shop: FILE A CLAIM books the job for the deductible
            var s2 = InsCareer();
            var b = InsCar(s2, 10000, InsEg);
            s2.activeCar = b.id;
            b.insCover = Insurance.Full;
            var crash = new CarFault { id = "panel_damage", label = "Crumpled front", hidden = false, diagnosed = true,
                                       stat = "hp", cost = 1400, days = 2, add = 20, cause = FaultCause.Impact, hitId = 31 };
            b.faults.Add(crash);
            var wear = new CarFault { id = "test_clutch", label = "Slipping clutch", hidden = false, stat = "engine",
                                      cost = 300, days = 2, add = 10, cause = FaultCause.Wear };
            Check(Insurance.ClaimOffered(s2, b, crash, FaultCatalog.Venue.Mechanic) &&
                  Insurance.ClaimOffered(s2, b, crash, FaultCatalog.Venue.Dealer) &&
                  !Insurance.ClaimOffered(s2, b, crash, FaultCatalog.Venue.Diy) &&
                  !Insurance.ClaimOffered(s2, b, wear, FaultCatalog.Venue.Mechanic),
                  "the claim is asked for a crash repair at Delmar or Crestline only");
            var q = FaultCatalog.GetQuote(s2, b, crash, FaultCatalog.Venue.Mechanic);
            int m0 = s2.money;
            string err = Insurance.OrderRepairOnClaim(s2, b, crash, FaultCatalog.Venue.Mechanic);
            int want = Mathf.Min(500, q.price);
            Check(err == null && m0 - s2.money == want && s2.pendingParts.Exists(x => x.carId == b.id),
                  "FILE A CLAIM: the job is booked and the player paid the deductible (quote " + q.price + ")",
                  err ?? (m0 - s2.money).ToString());
            a.insCover = Insurance.Liability;
            Check(!Insurance.ClaimOffered(s, a, crash, FaultCatalog.Venue.Mechanic), "a liability car is never asked");
        }

        static void TestInsurancePoints()
        {
            var s = InsCareer();
            var a = InsCar(s, 10000);
            int clean = Insurance.MonthlyPremium(s, a);
            Insurance.AddPoints(s, 2, "speeding 15 over");
            Check(Insurance.Points(s) == 2 && Insurance.MonthlyPremium(s, a) == 41,
                  "two points (a ticket) put 45% on the premium: $28 -> $41", Insurance.MonthlyPremium(s, a));
            s.day += Insurance.PointDays - 1;
            Check(Insurance.Points(s) == 2, "still there the day before three years are up");
            s.day += 1;
            Check(Insurance.Points(s) == 0 && Insurance.MonthlyPremium(s, a) == clean,
                  "gone after three years, and the premium is back", Insurance.MonthlyPremium(s, a));
            Check(Insurance.ClaimPoints(1500) == 1 && Insurance.ClaimPoints(2500) == 2 && Insurance.ClaimPoints(7500) == 3 &&
                  Insurance.ClaimPoints(0) == 0, "claim points by what was paid (SDIP style)");
        }

        static void TestInsuranceTotalLoss()
        {
            var s = InsCareer();
            var a = InsCar(s, 9000);
            a.insCover = Insurance.Full;
            var p = Insurance.TotalLossPay(s, a, 8000, true);
            Check(p.insurerPaid == 7500 && p.deductible == 500 && p.playerOwes == 0,
                  "a write-off pays the value less the deductible ($7,500)", p.insurerPaid);
            Check(Insurance.Points(s) == 3, "a write-off claim is 3 points", Insurance.Points(s));

            var s2 = InsCareer();
            var b = InsCar(s2, 9000);
            s2.carLoans.Add(new CarLoan { carId = b.id, principal = 3000, monthlyPayment = 250, monthsRemaining = 12 });
            var pb = Insurance.TotalLossPay(s2, b, 8000, true);
            Check(pb.insurerPaid == 4500 && pb.playerOwes == 0 && !s2.carLoans.Exists(l => l.carId == b.id),
                  "with a loan the lender is paid first ($3,000), the player gets $4,500, the loan is closed", pb.insurerPaid);

            var s3 = InsCareer();
            var c = InsCar(s3, 9000);
            s3.carLoans.Add(new CarLoan { carId = c.id, principal = 9000, monthlyPayment = 750, monthsRemaining = 12 });
            var pc = Insurance.TotalLossPay(s3, c, 8000, true);
            Check(pc.insurerPaid == 0 && pc.playerOwes == 1500, "a loan bigger than the payout leaves $1,500 owed", pc.playerOwes);

            var s4 = InsCareer();
            var d = InsCar(s4, 9000);
            var pd = Insurance.TotalLossPay(s4, d, 8000, true);
            Check(pd.insurerPaid == 0 && Insurance.Points(s4) == 0, "liability pays nothing for a write-off");
        }

        static void TestInsuranceHealth()
        {
            var s = InsCareer();
            Insurance.Install();
            Check(CrashHooks.HospitalPayer != null && CrashHooks.RepairClaim != null && CrashHooks.TotalLossClaim != null,
                  "the hooks are set at startup");
            var none = CrashHooks.Hospital(s, 10000);
            Check(none.playerOwes == 10000 && none.insurerPaid == 0, "no plan: the whole $10,000 bill", none.playerOwes);
            int[] want = { 3700, 2400, 1000 };
            string[] keys = { "basic", "standard", "premium" };
            for (int i = 0; i < keys.Length; i++)
            {
                var t = InsCareer();
                Insurance.ChoosePlan(t, keys[i]);
                var pay = CrashHooks.Hospital(t, 10000);
                Check(pay.playerOwes == want[i] && pay.insurerPaid == 10000 - want[i],
                      keys[i] + ": the player owes " + MenuKit.Money(want[i]) + " of $10,000", pay.playerOwes);
            }
            Insurance.ChoosePlan(s, "standard");
            Check(s.healthAsked && Insurance.PaycheckPremium(s) == 9 && Insurance.TakeFromPaycheck(s, 400) == 9 &&
                  Insurance.TakeFromPaycheck(s, 5) == 5, "STANDARD is $9 out of each paycheck (never more than the paycheck)");
            CrashHooks.Hospital(s, 6000);
            Check(Insurance.HealthClaims(s) == 1 && Insurance.PaycheckPremium(s) == 11,
                  "using it raises the premium 20% ($9 -> $11)", Insurance.PaycheckPremium(s));
            s.day += Insurance.PointDays;
            Check(Insurance.PaycheckPremium(s) == 9, "for three years", Insurance.PaycheckPremium(s));
            var small = Insurance.HospitalPay(s, 300, true);
            Check(small.playerOwes == 300 && Insurance.HealthClaims(s) == 0, "a bill under the deductible is no claim");
        }

        static void TestInsuranceNoCover()
        {
            var s = InsCareer();
            s.money = 3000;
            var a = InsCar(s, 7000);
            Insurance.Install();
            var r = CrashHooks.Repair(s, a, 2600);
            var t = CrashHooks.TotalLoss(s, a, 6000);
            Check(r.playerOwes == 2600 && r.insurerPaid == 0 && t.insurerPaid == 0 && Insurance.Points(s) == 0,
                  "uninsured (liability): the repair is all yours, a write-off pays nothing, no points");
            int left = Insurance.Charge(s, 8000, "a hospital bill");
            Check(left == 5000 && s.money == 0 && s.bankLoans.Count == 1 && s.bankLoans[0].monthlyPayment == 417 &&
                  s.bankLoans[0].monthsRemaining == 12 && LifeRules.MonthlyLoanPayments(s) == 417,
                  "what the money cannot cover goes on a 12-month payment plan ($417 a month on the bills)", left);
            Check(Insurance.DebtLeft(s) == 5004, "BILLS shows what is left on it", Insurance.DebtLeft(s));
        }

        static void TestInsuranceSave()
        {
            // an old save: no insurance fields at all
            var old = JsonUtility.FromJson<LifeState>("{\"saveVersion\":26,\"day\":50,\"cars\":[{\"id\":\"a\"},{\"id\":\"b\"}]," +
                                                      "\"carLoans\":[{\"carId\":\"b\",\"monthsRemaining\":10,\"monthlyPayment\":150}]}");
            Check(old.insRecord != null && old.cars[0].insCover == 0 && !old.healthAsked && old.healthPlan == "",
                  "an old save reads as liability, no record, plan unasked");
            LifeSimManager.Migrate(old);
            Check(old.saveVersion >= 28 && old.cars[1].insCover == Insurance.Full && old.cars[0].insCover == Insurance.Liability &&
                  old.cars[0].insDeductible == 500 && old.cars[1].insDeductible == 500,
                  "the v28 step holds the car with a loan at FULL, $500 on file", old.saveVersion);
            var s = InsCareer();
            var a = InsCar(s, 5000);
            a.insCover = Insurance.Full; a.insDeductible = 1000;
            Insurance.ChoosePlan(s, "premium");
            Insurance.AddPoints(s, 1, "test");
            var back = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(s));
            Check(back.cars[0].insCover == Insurance.Full && back.cars[0].insDeductible == 1000 &&
                  back.healthPlan == "premium" && back.healthAsked && Insurance.Points(back) == 1,
                  "cover, deductible, plan and record survive a save");

            // the switch: PSX_INSURANCE=0 is the old fleet line, no claims, no hooks
            System.Environment.SetEnvironmentVariable("PSX_INSURANCE", "0");
            try
            {
                Insurance.Install();
                int fleet = 50 + Mathf.RoundToInt(5000 * 0.005f);
                Check(LifeRules.MonthlyInsurance(s) == fleet && CrashHooks.HospitalPayer == null &&
                      CrashHooks.RepairClaim == null && Insurance.PaycheckPremium(s) == 15 && Insurance.TakeFromPaycheck(s, 400) == 0,
                      "PSX_INSURANCE=0: the fleet line ($75), no hooks, nothing out of the paycheck", LifeRules.MonthlyInsurance(s));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_INSURANCE", null);
                Insurance.Install();
            }
        }
    }
}
