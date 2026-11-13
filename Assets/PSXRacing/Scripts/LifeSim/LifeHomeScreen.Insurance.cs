using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R1c-B INSURANCE's pages (see Insurance): the per-car policies and the
    /// record on BILLS, the health plan on THE JOB page (and at hire in the
    /// new-game wizard), and FILE A CLAIM or PAY IT YOURSELF for a crash repair
    /// at Delmar or Crestline on a fully covered car.
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>The wizard's health plan (Insurance.Plans key; "" = none).</summary>
        static string wizPlan = "standard";

        // =================== BILLS ===================
        /// <summary>One line per car (its cover, deductible and premium) and the total.</summary>
        void InsuranceBills(ref float y)
        {
            Insurance.Sync(S);
            MenuKit.Label(body, "CAR INSURANCE - ONE POLICY A CAR", MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 34f;
            float bw = Mathf.Min(280f, (ColW - 30f) * 0.58f), dw = Mathf.Min(180f, (ColW - 30f) * 0.40f);
            float x0 = ColL + 16f;
            foreach (var car in S.cars)
            {
                if (car == null) continue;
                var c = car;
                Row("  " + PolicyName(c), MenuKit.Money(Insurance.MonthlyPremium(S, c)), ref y);
                y += 4f;
                bool loan = Insurance.HasLoan(S, c);
                bool full = Insurance.CoverOf(S, c) == Insurance.Full;
                string cover = loan ? "FULL - LOAN" : full ? "FULL COVERAGE" : "LIABILITY " + Insurance.MinimumLimits;
                MenuKit.Button(body, cover, new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x0, bw), y),
                    new Vector2(bw, 38f), loan ? null : (UnityEngine.Events.UnityAction)(() =>
                    {
                        Insurance.SetCover(S, c, full ? Insurance.Liability : Insurance.Full);
                        LifeSimManager.Save(); Rebuild();
                    }), MenuKit.Tiny, loan ? MenuKit.BtnBgDisabled : (Color?)null);
                if (full)
                    MenuKit.Button(body, "DED " + MenuKit.Money(Insurance.DeductibleOf(c)), new Vector2(0.5f, 1f),
                        new Vector2(MenuKit.ColLeft(x0 + bw + 12f, dw), y), new Vector2(dw, 38f), () =>
                        {
                            c.insDeductible = Insurance.NextDeductible(c);
                            LifeSimManager.Save(); Rebuild();
                        }, MenuKit.Tiny);
                y -= 48f;
            }
            if (S.cars.Count == 0) Row("  NO CAR, NO POLICY", MenuKit.Money(0), ref y);
            Row("INSURANCE A MONTH", MenuKit.Money(Insurance.MonthlyTotal(S)), ref y);
            Paragraph("Tap a car's cover to switch between LIABILITY (the state minimum) and FULL " +
                      "(collision and comprehensive), and its deductible to step $250 / $500 / $1,000. " +
                      "A car with a loan stays FULL until the loan is paid.", ref y);
        }

        /// <summary>"NISSAN SKYLINE '89": the badge and the year, so two of a make read apart.</summary>
        static string PolicyName(OwnedCar c)
        {
            string n = LifeRules.ShortName(c);
            var p = (c.displayName ?? "").Split(' ');
            string last = p.Length > 2 ? p[p.Length - 1] : "";
            return last.StartsWith("'") && last.Length <= 4 ? n + " " + last : n;
        }

        /// <summary>The health plan, the driving record and what is on a payment plan.</summary>
        void InsuranceRecordRows(ref float y)
        {
            if (Insurance.HealthOn)
            {
                var plan = Insurance.PlanOf(S);
                Row("HEALTH PLAN", plan == null ? "NONE" : plan.name + " " + MenuKit.Money(Insurance.PaycheckPremium(S)) + "/WK",
                    ref y, plan == null ? MenuKit.Bad : (Color?)null);
            }
            int pts = Insurance.Points(S);
            Row("DRIVING RECORD", pts == 0 ? "CLEAN" : pts + (pts == 1 ? " POINT +" : " POINTS +") +
                Mathf.RoundToInt(Insurance.Surcharge(pts) * 100f) + "%", ref y, pts == 0 ? MenuKit.Good : MenuKit.Bad);
            if (pts > 0)
                Paragraph("The next point comes off on " + LifeRules.DateLabel(Insurance.NextClearDay(S)) +
                          ". A claim the policy pays puts points on your record for three years, and every " +
                          "point raises the premium on every car.", ref y);
            int debt = Insurance.DebtLeft(S);
            if (debt > 0) Row("PAYMENT PLANS LEFT", MenuKit.Money(debt), ref y, MenuKit.Bad);
        }

        // =================== the health plan ===================
        /// <summary>
        /// One row per plan (and NONE): a button with the plan's name and its
        /// terms beside it. Returns the y under the last row. The wizard
        /// highlights its pick; THE JOB page signs up on the press.
        /// </summary>
        float PlanRows(RectTransform parent, Vector2 anchor, float left, float y, float width,
                       string selected, System.Action<string> pick, bool highlight)
        {
            var keys = new[] { "", "basic", "standard", "premium" };
            var bgs = new Image[keys.Length];
            float bw = Mathf.Min(170f, width * 0.26f), tx = left + bw + 12f, tw = width - bw - 12f;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                var p = Insurance.PlanByKey(key);
                int idx = i;
                bool on = highlight && key == (selected ?? "");
                var btn = MenuKit.Button(parent, p != null ? p.name : "NONE", anchor,
                    new Vector2(anchor.x < 0.25f ? left : MenuKit.ColLeft(left, bw), y), new Vector2(bw, 40f), () =>
                    {
                        if (highlight)
                            for (int j = 0; j < bgs.Length; j++)
                                if (bgs[j] != null) bgs[j].color = j == idx ? GoBg : MenuKit.BtnBg;
                        pick(key);
                    }, MenuKit.Tiny, on ? GoBg : (Color?)null);
                bgs[i] = btn.GetComponent<Image>();
                MenuKit.Para(parent, Insurance.PlanTerms(p), MenuKit.Tiny, anchor, new Vector2(tx, y - 4f),
                    out float h, TextAnchor.UpperLeft, p == null ? MenuKit.Dim : Color.white, tw);
                y -= Mathf.Max(46f, h + 10f);
            }
            return y;
        }

        /// <summary>The wizard's job card: the plan picked at hire.</summary>
        float WizardPlanRows(RectTransform list, float cy)
        {
            if (!Insurance.HealthOn) return cy;
            float w = list.rect.width > 1f ? list.rect.width - 40f : MenuKit.HalfWidth * 2f - 120f;
            MenuKit.Label(list, "HEALTH PLAN WITH THE JOB  (out of each paycheck)", MenuKit.Small, new Vector2(0f, 1f),
                new Vector2(20f, cy), TextAnchor.MiddleLeft, MenuKit.Dim, w);
            cy -= 36f;
            // ONE row of four buttons and the picked plan's terms under them. Four
            // wrapped paragraphs ran under the NEXT button on a 4:3 screen (11-13).
            var keys = new[] { "", "basic", "standard", "premium" };
            var bgs = new Image[keys.Length];
            float gap = 8f, bw = Mathf.Min(150f, (w - gap * (keys.Length - 1)) / keys.Length);
            Text terms = null;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                var p = Insurance.PlanByKey(key);
                int idx = i;
                bool on = key == (wizPlan ?? "");
                var btn = MenuKit.Button(list, p != null ? p.name : "NONE", new Vector2(0f, 1f),
                    new Vector2(20f + i * (bw + gap), cy), new Vector2(bw, 40f), () =>
                    {
                        for (int j = 0; j < bgs.Length; j++)
                            if (bgs[j] != null) bgs[j].color = j == idx ? GoBg : MenuKit.BtnBg;
                        wizPlan = key;
                        if (terms != null) terms.text = Insurance.PlanTerms(Insurance.PlanByKey(key));
                    }, MenuKit.Tiny, on ? GoBg : (Color?)null);
                bgs[i] = btn.GetComponent<Image>();
            }
            cy -= 48f;
            terms = MenuKit.Para(list, Insurance.PlanTerms(Insurance.PlanByKey(wizPlan ?? "")), MenuKit.Tiny,
                new Vector2(0f, 1f), new Vector2(20f, cy), out float th, TextAnchor.UpperLeft, Color.white, w);
            // room for the longest plan's terms, whichever is picked now
            return cy - Mathf.Max(th, 46f) - 6f;
        }

        /// <summary>THE JOB page: the plan, or - for a career hired before plans
        /// existed (v28) - the choice it was never offered.</summary>
        void HealthPlanBlock(ref float y)
        {
            if (!Insurance.HealthOn) return;
            var plan = Insurance.PlanOf(S);
            if (S.healthAsked)
            {
                DutyRow("HEALTH PLAN", plan == null ? "NONE - YOU PAY EVERY BILL"
                                                    : plan.name + ", " + MenuKit.Money(Insurance.PaycheckPremium(S)) + " A PAYCHECK",
                    ref y, plan == null ? MenuKit.Bad : MenuKit.Good);
                return;
            }
            MenuKit.Label(body, "PICK A HEALTH PLAN", MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 34f;
            Paragraph("Tony's offers three plans with the job. It comes out of each paycheck; a hospital bill " +
                      "goes through it, and a claim it pays raises its premium for three years.", ref y);
            y = PlanRows(body, new Vector2(0.5f, 1f), ColL, y, Mathf.Min(ColW, 900f), S.healthPlan, k =>
            {
                Insurance.ChoosePlan(S, k);
                LifeSimManager.Save(); Rebuild();
                var p = Insurance.PlanByKey(k);
                Toast(p != null ? "SIGNED UP: " + p.name + " PLAN" : "NO HEALTH PLAN");
            }, highlight: false);
            y -= 6f;
        }

        // =================== FILE A CLAIM ===================
        string claimCarId = "", claimFaultId = "", claimBack = "service";
        int claimVenue = (int)FaultCatalog.Venue.Mechanic;

        void OpenClaim(OwnedCar car, CarFault f, FaultCatalog.Venue v)
        {
            claimCarId = car.id; claimFaultId = f.id; claimVenue = (int)v;
            claimBack = string.IsNullOrEmpty(tab) || tab == "claim" ? "service" : tab;
            tab = "claim";
            Rebuild();
        }

        void BuildClaim()
        {
            float y = -14f;
            PageHeader(ref y, "FILE A CLAIM?");
            var car = S.FindCar(claimCarId);
            var f = car != null ? car.faults.Find(x => x != null && x.id == claimFaultId) : null;
            var v = (FaultCatalog.Venue)claimVenue;
            float bw = Mathf.Min(380f, (ColW - 12f) * 0.5f);
            if (car == null || f == null)
            {
                Paragraph("There is nothing to claim for any more.", ref y);
                MenuKit.Button(body, "BACK", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, bw), y),
                    new Vector2(bw, 48f), () => { tab = claimBack; Rebuild(); }, 18);
                return;
            }
            var q = FaultCatalog.GetQuote(S, car, f, v);
            var pay = Insurance.RepairPay(S, car, q.price, f.hitId);
            int add = Insurance.PointsIfFiled(S, car, f.hitId, pay.insurerPaid);
            int now = Insurance.MonthlyTotal(S), after = Insurance.MonthlyTotal(S, add);
            string shop = v == FaultCatalog.Venue.Dealer ? CarWhere.DealerName : CarWhere.MechanicName;

            DutyRow("THE CAR", LifeRules.ShortName(car), ref y, MenuKit.Accent);
            DutyRow("THE REPAIR", f.label, ref y);
            DutyRow(shop + " QUOTE", MenuKit.Money(q.price), ref y);
            DutyRow("YOUR POLICY", Insurance.CoverName(S, car), ref y);
            DutyRow("THE POLICY PAYS", MenuKit.Money(pay.insurerPaid), ref y, MenuKit.Good);
            DutyRow("YOU PAY", MenuKit.Money(pay.playerOwes) + (pay.deductible > 0 ? " - THE DEDUCTIBLE" : ""), ref y);
            DutyRow("YOUR RECORD", add > 0 ? "+" + add + (add == 1 ? " POINT" : " POINTS") + " FOR 3 YEARS" : "NO NEW POINTS",
                ref y, add > 0 ? MenuKit.Bad : MenuKit.Good);
            DutyRow("INSURANCE A MONTH", MenuKit.Money(now) + " NOW, " + MenuKit.Money(after) + " AFTER", ref y,
                after > now ? MenuKit.Bad : (Color?)null);
            y -= 8f;

            bool canClaim = q.available && S.money >= pay.playerOwes;
            bool canPay = q.available && S.money >= q.price;
            var fc = f;
            MenuKit.Button(body, "FILE A CLAIM - YOU PAY " + MenuKit.Money(pay.playerOwes), new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL, bw), y), new Vector2(bw, 48f),
                canClaim ? (UnityEngine.Events.UnityAction)(() =>
                {
                    string err = Insurance.OrderRepairOnClaim(S, car, fc, v);
                    tab = claimBack;
                    Booked(err, fc.label + " booked on a claim");
                }) : null, 17, canClaim ? GoBg : MenuKit.BtnBgDisabled);
            MenuKit.Button(body, "PAY IT YOURSELF - " + MenuKit.Money(q.price), new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(ColL + bw + 12f, bw), y), new Vector2(bw, 48f),
                canPay ? (UnityEngine.Events.UnityAction)(() =>
                {
                    string err = LifeRules.OrderRepair(S, car, fc, v);
                    tab = claimBack;
                    Booked(err, fc.label + " booked");
                }) : null, 17, canPay ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 58f;
            MenuKit.Button(body, "BACK", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, bw), y),
                new Vector2(bw, 44f), () => { tab = claimBack; Rebuild(); }, 17);
            y -= 54f;
            Paragraph(pay.note + " A claim the policy pays puts points on your driving record for three years, " +
                      "and every point raises the premium on every car you insure - a small repair can cost " +
                      "less paid yourself.", ref y);
        }
    }
}
