using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The apartment. RG2's home surface is eight tabs (main / bills / garage /
    /// newspaper / eat / calendar / mail / sleep — src/ui/screens/home/overlay.ts);
    /// v1 ships the load-bearing subset: MAIN / GARAGE / EAT / BILLS / JOBS.
    /// Sleep lives on MAIN because ending the day is the core verb.
    ///
    /// The whole UI is runtime-generated through MenuKit; the scene holds only
    /// a camera and this component. First run (no player name) shows the
    /// new-game wizard instead.
    /// </summary>
    public class LifeHomeScreen : MonoBehaviour
    {
        Canvas canvas;
        RectTransform body;
        Text moneyText, dateText, healthText;
        string tab = "main";

        // wizard state
        bool wizard;
        string wizName = "";
        int wizAge = 25;
        int wizJob;
        InputField wizNameField;

        LifeState S => LifeSimManager.State;

        void Start()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "HomeCanvas", 10);
            MenuKit.Panel(canvas.transform, "Backdrop", MenuKit.Bg);

            wizard = !LifeSimManager.HasSave || string.IsNullOrEmpty(S.playerName);
            if (wizard) { BuildWizard(); return; }

            // Returning from a race? Bank the result, then the race burns a slot.
            string raceSummary = null;
            if (RaceHandoff.ResultReady)
            {
                raceSummary = LifeRules.ApplyRaceResult(S);   // burns the slot itself
                RaceHandoff.ClearAll();
                LifeSimManager.Save();
            }

            BuildChrome();
            Rebuild();
            if (raceSummary != null) Toast("RACE RESULT: " + raceSummary);
        }

        // =================== new-game wizard ===================
        void BuildWizard()
        {
            var root = MenuKit.Rect(canvas.transform, "Wizard",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760f, 640f), MenuKit.PanelBg);

            MenuKit.Label(root, "NEW GAME", 32, new Vector2(0.5f, 1f),
                new Vector2(0f, -30f), TextAnchor.MiddleCenter, MenuKit.Accent, 700f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);

            // Name (InputField brings up the soft keyboard on mobile browsers)
            MenuKit.Label(root, "NAME", 16, new Vector2(0f, 1f),
                new Vector2(60f, -110f), TextAnchor.MiddleLeft, MenuKit.Dim, 200f);
            var fieldRect = MenuKit.Rect(root, "NameField", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(60f, -134f), new Vector2(420f, 52f),
                new Color(0f, 0f, 0f, 0.5f));
            wizNameField = fieldRect.gameObject.AddComponent<InputField>();
            var nameText = MenuKit.Label(fieldRect, "", 22, new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), TextAnchor.MiddleLeft, Color.white, 400f);
            nameText.raycastTarget = false;
            var placeholder = MenuKit.Label(fieldRect, "DRIVER", 22, new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), TextAnchor.MiddleLeft, MenuKit.Dim, 400f);
            placeholder.raycastTarget = false;
            wizNameField.textComponent = nameText;
            wizNameField.placeholder = placeholder;
            wizNameField.characterLimit = 10;   // nameEntry.ts limit
            wizNameField.targetGraphic = fieldRect.GetComponent<Image>();

            // Age 21-60 (nameEntry.ts), default 25
            MenuKit.Label(root, "AGE", 16, new Vector2(0f, 1f),
                new Vector2(540f, -110f), TextAnchor.MiddleLeft, MenuKit.Dim, 100f);
            var ageLabel = MenuKit.Label(root, wizAge.ToString(), 26, new Vector2(0f, 1f),
                new Vector2(600f, -158f), TextAnchor.MiddleCenter, Color.white, 70f, bold: true);
            MenuKit.Button(root, "-", new Vector2(0f, 1f), new Vector2(540f, -134f),
                new Vector2(52f, 52f), () =>
                { wizAge = Mathf.Max(21, wizAge - 1); ageLabel.text = wizAge.ToString(); }, 24);
            MenuKit.Button(root, "+", new Vector2(0f, 1f), new Vector2(660f, -134f),
                new Vector2(52f, 52f), () =>
                { wizAge = Mathf.Min(60, wizAge + 1); ageLabel.text = wizAge.ToString(); }, 24);

            // Job pick — daily pay shown; savings roll happens on start
            MenuKit.Label(root, "PICK A DAY JOB (pays weekly, Fridays)", 16,
                new Vector2(0f, 1f), new Vector2(60f, -216f), TextAnchor.MiddleLeft, MenuKit.Dim, 500f);
            var jobLabels = new Text[LifeRules.Jobs.Length];
            for (int i = 0; i < LifeRules.Jobs.Length; i++)
            {
                int idx = i;
                var job = LifeRules.Jobs[i];
                float y = -248f - i * 46f;
                MenuKit.Button(root, "", new Vector2(0f, 1f), new Vector2(60f, y),
                    new Vector2(640f, 40f), () =>
                    {
                        wizJob = idx;
                        for (int j = 0; j < jobLabels.Length; j++)
                            jobLabels[j].color = j == idx ? MenuKit.Accent : Color.white;
                    }, 16);
                jobLabels[i] = MenuKit.Label(root, job.name + "  —  $" + job.dailyPay + "/day",
                    17, new Vector2(0f, 1f), new Vector2(80f, y - 6f),
                    TextAnchor.MiddleLeft, i == 0 ? MenuKit.Accent : Color.white, 560f);
                jobLabels[i].raycastTarget = false;
            }

            MenuKit.Button(root, "START LIFE", new Vector2(0.5f, 0f), new Vector2(0f, 24f),
                new Vector2(300f, 60f), () =>
                {
                    LifeSimManager.StartNewGame(
                        wizNameField.text.Trim().ToUpper(), wizAge, wizJob);
                    SceneManager.LoadScene(0);   // reload home, now in play mode
                }, 22, new Color(1f, 0.84f, 0.4f, 0.28f));
        }

        // =================== chrome ===================
        void BuildChrome()
        {
            var header = MenuKit.Rect(canvas.transform, "Header",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(1280f, 92f), new Color(0f, 0f, 0f, 0.35f));
            MenuKit.Label(header, "AT HOME", 28, new Vector2(0f, 0.5f),
                new Vector2(28f, 8f), TextAnchor.MiddleLeft, MenuKit.Accent, 260f, bold: true);
            dateText = MenuKit.Label(header, "", 16, new Vector2(0f, 0.5f),
                new Vector2(28f, -22f), TextAnchor.MiddleLeft, MenuKit.Dim, 500f);
            healthText = MenuKit.Label(header, "", 16, new Vector2(0.5f, 0.5f),
                new Vector2(40f, -22f), TextAnchor.MiddleLeft, MenuKit.Dim, 400f);
            moneyText = MenuKit.Label(header, "", 26, new Vector2(1f, 0.5f),
                new Vector2(-28f, 0f), TextAnchor.MiddleRight, MenuKit.Good, 300f, bold: true);

            var bar = MenuKit.Rect(canvas.transform, "Tabs",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -92f), new Vector2(1280f, 64f), new Color(0f, 0f, 0f, 0.2f));
            string[] tabs = { "main", "garage", "eat", "bills", "jobs" };
            float x = 28f;
            foreach (var t in tabs)
            {
                string captured = t;
                MenuKit.Button(bar, t.ToUpper(), new Vector2(0f, 0.5f),
                    new Vector2(x, 0f), new Vector2(160f, 48f),
                    () => { tab = captured; Rebuild(); }, 17);
                x += 172f;
            }
        }

        void Rebuild()
        {
            if (body != null) Destroy(body.gameObject);
            body = MenuKit.Rect(canvas.transform, "Body",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 0f), new Vector2(1280f, 560f));

            moneyText.text = MenuKit.Money(S.money);
            dateText.text = LifeRules.DateLabel(S.day) + "  ·  " + LifeRules.SlotNames[Mathf.Clamp(S.slotIndex, 0, 2)];
            healthText.text = "HEALTH " + Mathf.RoundToInt(S.health) + " (" + LifeRules.HealthLabel(S.health) + ")" +
                              "   REP " + Mathf.RoundToInt(S.streetRep) + " (" + LifeRules.StreetTier(S.streetRep).name + ")";

            switch (tab)
            {
                case "main": BuildMain(); break;
                case "garage": BuildGarage(); break;
                case "eat": BuildEat(); break;
                case "bills": BuildBills(); break;
                case "jobs": BuildJobs(); break;
            }
        }

        // =================== tabs ===================
        void BuildMain()
        {
            float y = -20f;

            bool racedToday = LifeRules.RacedToday(S);
            // Pre-race fuel gate: a 3-lap race burns ~57% of the tank.
            float burn = LifeRules.RaceFuelBurnPct(3f * 1168f);
            bool lowFuel = S.ActiveCar != null && S.ActiveCar.fuel <= burn;
            string raceLabel = racedToday ? "RACED TODAY — SLEEP FIRST"
                             : lowFuel ? "LOW FUEL — REFUEL IN GARAGE"
                             : "GET IN CAR  >>";
            bool canRace = !racedToday && !lowFuel;
            MenuKit.Button(body, raceLabel,
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 72f),
                canRace ? (UnityEngine.Events.UnityAction)StartRace : null, 22,
                canRace ? new Color(1f, 0.84f, 0.4f, 0.28f) : MenuKit.BtnBgDisabled);
            y -= 90f;

            bool canWork = !string.IsNullOrEmpty(S.playerJob) &&
                           !LifeRules.IsWeekend(S.day) && !S.workedToday;
            string workLabel = string.IsNullOrEmpty(S.playerJob) ? "NO JOB (SEE JOBS TAB)"
                : LifeRules.IsWeekend(S.day) ? "WEEKEND — NO WORK"
                : S.workedToday ? "ALREADY WORKED TODAY"
                : "GO TO WORK (" + S.playerJob + ")";
            MenuKit.Button(body, workLabel, new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(460f, 56f), canWork ? DoWork : (UnityEngine.Events.UnityAction)null,
                18, canWork ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 74f;

            MenuKit.Button(body, "SLEEP UNTIL TOMORROW", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(460f, 56f), DoSleep, 18);
            y -= 84f;

            MenuKit.Label(body, "RECENTLY", 15, new Vector2(0.5f, 1f),
                new Vector2(-230f, y), TextAnchor.MiddleLeft, MenuKit.Dim, 460f);
            y -= 28f;
            int n = S.calendarLog.Count;
            for (int i = Mathf.Max(0, n - 6); i < n; i++)
            {
                MenuKit.Label(body, S.calendarLog[i], 14, new Vector2(0.5f, 1f),
                    new Vector2(-230f, y), TextAnchor.MiddleLeft, Color.white, 700f);
                y -= 24f;
            }
        }

        void BuildGarage()
        {
            var car = S.ActiveCar;
            if (car == null)
            {
                MenuKit.Label(body, "No car.", 20, new Vector2(0.5f, 1f), new Vector2(-210f, -40f));
                return;
            }
            float y = -20f;
            MenuKit.Label(body, car.displayName, 23, new Vector2(0.5f, 1f),
                new Vector2(-400f, y), TextAnchor.MiddleLeft, MenuKit.Accent, 800f, bold: true);
            y -= 38f;
            MenuKit.Label(body, "Odometer " + car.odoMiles.ToString("N0") + " mi   ·   paid " +
                MenuKit.Money(car.paidPrice), 16, new Vector2(0.5f, 1f),
                new Vector2(-400f, y), TextAnchor.MiddleLeft, MenuKit.Dim, 800f);
            y -= 44f;
            DrawBar("ENGINE", car.engine, ref y);
            DrawBar("TIRES", car.tires, ref y);
            DrawBar("BODY", car.carHP, ref y);
            DrawBar("PAINT", car.paint, ref y);
            DrawBar("FUEL", car.fuel, ref y);

            int cost = LifeRules.RefuelCost(car);
            bool canFill = car.fuel < 99.5f && S.money >= cost;
            MenuKit.Button(body, car.fuel >= 99.5f ? "TANK FULL"
                    : "REFUEL — " + MenuKit.Money(cost),
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(360f, 50f),
                canFill ? (UnityEngine.Events.UnityAction)(() =>
                {
                    S.money -= cost;
                    car.fuel = 100f;
                    LifeSimManager.Save(); Rebuild();
                }) : null, 17, canFill ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 66f;

            if (car.faults.Count > 0)
            {
                MenuKit.Label(body, "FAULTS", 15, new Vector2(0.5f, 1f),
                    new Vector2(-400f, y), TextAnchor.MiddleLeft, MenuKit.Bad, 300f, bold: true);
                y -= 28f;
                foreach (var f in car.faults)
                {
                    MenuKit.Label(body, "· " + f.label, 15, new Vector2(0.5f, 1f),
                        new Vector2(-400f, y), TextAnchor.MiddleLeft,
                        f.severity >= 2f ? MenuKit.Bad : MenuKit.Accent, 700f);
                    y -= 24f;
                }
                y -= 8f;
                MenuKit.Label(body, "Repairs arrive with the garage pass.", 14,
                    new Vector2(0.5f, 1f), new Vector2(-400f, y),
                    TextAnchor.MiddleLeft, MenuKit.Dim, 700f);
            }
        }

        void DrawBar(string label, float value, ref float y)
        {
            MenuKit.Label(body, label, 14, new Vector2(0.5f, 1f),
                new Vector2(-400f, y), TextAnchor.MiddleLeft, MenuKit.Dim, 170f);
            var track = MenuKit.Rect(body, "bar", new Vector2(0.5f, 1f),
                new Vector2(0f, 1f), new Vector2(-220f, y - 4f),
                new Vector2(430f, 16f), new Color(0f, 0f, 0f, 0.5f));
            Color c = value > 60f ? MenuKit.Good : value > 30f ? MenuKit.Accent : MenuKit.Bad;
            MenuKit.Rect(track, "fill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0f), new Vector2(428f * Mathf.Clamp01(value / 100f), 14f), c);
            MenuKit.Label(body, Mathf.RoundToInt(value) + "%", 14, new Vector2(0.5f, 1f),
                new Vector2(226f, y), TextAnchor.MiddleLeft, c, 80f);
            y -= 38f;
        }

        void BuildEat()
        {
            float y = -20f;
            MenuKit.Label(body, "FOOD STOCK: " + S.foodStock + " meals" +
                (S.ateToday ? "   ·   eaten today: yes" : "   ·   NOT EATEN TODAY"),
                18, new Vector2(0.5f, 1f), new Vector2(-300f, y), TextAnchor.MiddleLeft,
                S.ateToday ? MenuKit.Good : MenuKit.Bad, 620f);
            y -= 52f;

            bool canEat = S.foodStock > 0 && !S.ateToday;
            MenuKit.Button(body, S.ateToday ? "ALREADY ATE TODAY" : "EAT A MEAL",
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 56f),
                canEat ? (UnityEngine.Events.UnityAction)(() =>
                {
                    Toast(LifeRules.EatMeal(S, S.lastMealTier == "" ? "regular" : S.lastMealTier));
                    LifeSimManager.Save(); Rebuild();
                }) : null, 18, canEat ? (Color?)null : MenuKit.BtnBgDisabled);
            y -= 84f;

            MenuKit.Label(body, "BUY GROCERIES", 15, new Vector2(0.5f, 1f),
                new Vector2(-300f, y), TextAnchor.MiddleLeft, MenuKit.Dim, 400f);
            y -= 36f;
            foreach (var g in LifeRules.Groceries)
            {
                var captured = g;
                bool afford = S.money >= g.cost;
                MenuKit.Button(body,
                    g.tier.ToUpper() + "  $" + g.cost + " → " + g.meals + " meals",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 50f),
                    afford ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        S.money -= captured.cost;
                        S.foodStock += captured.meals;
                        S.lastMealTier = captured.tier;
                        LifeSimManager.Save(); Rebuild();
                    }) : null, 17, afford ? (Color?)null : MenuKit.BtnBgDisabled);
                y -= 62f;
            }
        }

        void BuildBills()
        {
            float y = -20f;
            int housing = LifeRules.MonthlyHousing(S);
            int insurance = LifeRules.MonthlyInsurance(S);
            int loans = LifeRules.MonthlyLoanPayments(S);
            int due = housing + insurance + loans;
            int daysLeft = LifeRules.DaysPerMonth - LifeRules.DayOfMonth(S.day) + 1;

            Row("HOUSING (" + S.housingType + ")", MenuKit.Money(housing), ref y);
            Row("INSURANCE", MenuKit.Money(insurance), ref y);
            if (loans > 0) Row("LOAN PAYMENTS", MenuKit.Money(loans), ref y);
            y -= 12f;
            Row("DUE ON THE 1st (" + daysLeft + " days)", MenuKit.Money(due), ref y, MenuKit.Accent);
            y -= 24f;
            Row("CREDIT SCORE", S.creditScore.ToString(), ref y,
                S.creditScore >= 660 ? MenuKit.Good : S.creditScore >= 550 ? MenuKit.Accent : MenuKit.Bad);
            if (S.missedPayments > 0)
                Row("MISSED PAYMENTS", S.missedPayments.ToString(), ref y, MenuKit.Bad);
            if (S.pendingSalary > 0)
                Row("PAY PENDING (Friday)", MenuKit.Money(S.pendingSalary), ref y, MenuKit.Good);
        }

        void Row(string label, string value, ref float y, Color? valueColor = null)
        {
            MenuKit.Label(body, label, 17, new Vector2(0.5f, 1f),
                new Vector2(-360f, y), TextAnchor.MiddleLeft, MenuKit.Dim, 480f);
            MenuKit.Label(body, value, 17, new Vector2(0.5f, 1f),
                new Vector2(160f, y), TextAnchor.MiddleRight, valueColor ?? Color.white, 200f, bold: true);
            y -= 38f;
        }

        void BuildJobs()
        {
            float y = -20f;
            if (!string.IsNullOrEmpty(S.playerJob))
            {
                Row("CURRENT JOB", S.playerJob, ref y, MenuKit.Accent);
                Row("DAILY PAY", MenuKit.Money(Mathf.RoundToInt(S.basePay * S.payMultiplier)), ref y);
                Row("WORK REP", Mathf.RoundToInt(S.workRep) + "/100", ref y,
                    S.workRep >= 50 ? MenuKit.Good : MenuKit.Bad);
                Row("ATTENDANCE", S.workDaysPresent + "/" + S.workDaysTotal, ref y);
                y -= 16f;
                MenuKit.Label(body, "Miss weekday shifts and the rep ladder bites: −5, then −15, then −30 and fired.",
                    14, new Vector2(0.5f, 1f), new Vector2(-360f, y), TextAnchor.MiddleLeft, MenuKit.Dim, 760f);
                return;
            }

            MenuKit.Label(body, (S.fired ? "You were FIRED. " : "") + "Apply for work (55% hire odds per try):",
                17, new Vector2(0.5f, 1f), new Vector2(-360f, y), TextAnchor.MiddleLeft,
                S.fired ? MenuKit.Bad : Color.white, 740f);
            y -= 44f;
            foreach (var (name, dailyPay, _, _) in LifeRules.Jobs)
            {
                string jn = name; int jp = dailyPay;
                MenuKit.Button(body, name + "  —  $" + dailyPay + "/day",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 46f), () =>
                    {
                        if (Random.value < LifeRules.ApplyHireChance)
                        {
                            S.playerJob = jn; S.basePay = jp;
                            S.workRep = LifeRules.NewHireWorkRep;
                            S.consecutiveAbsences = 0; S.fired = false;
                            S.calendarLog.Add("Day " + S.day + ": hired — " + jn);
                            Toast("HIRED: " + jn);
                        }
                        else Toast("No luck at " + jn + ". Try again.");
                        LifeSimManager.Save(); Rebuild();
                    }, 16);
                y -= 56f;
            }
        }

        // =================== actions ===================
        void StartRace()
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.TimeSlot = Mathf.Clamp(S.slotIndex, 0, 2);
            LifeSimManager.Save();
            SceneManager.LoadScene(1);   // CityCircuit
        }

        void DoWork()
        {
            string msg = LifeRules.WorkOneDay(S);
            LifeRules.SpendActivitySlot(S);
            LifeSimManager.Save();
            Rebuild();
            Toast(msg);
        }

        void DoSleep()
        {
            LifeRules.Sleep(S);
            LifeSimManager.Save();
            tab = "main";
            Rebuild();
            Toast(LifeRules.DateLabel(S.day).ToUpper());
        }

        // =================== toast ===================
        Text statusText;
        void Toast(string msg)
        {
            if (statusText != null) Destroy(statusText.transform.parent.gameObject);
            var box = MenuKit.Rect(canvas.transform, "Toast",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(780f, 52f), new Color(0f, 0f, 0f, 0.78f));
            statusText = MenuKit.Label(box, msg, 18, new Vector2(0.5f, 0.5f),
                Vector2.zero, TextAnchor.MiddleCenter, MenuKit.Accent, 760f, bold: true);
            statusText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
