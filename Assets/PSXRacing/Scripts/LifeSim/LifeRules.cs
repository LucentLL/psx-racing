using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The economy rules: every number the LifeSim uses, in one place, each
    /// traced to its Racing Game 2 source file. UI code never hardcodes a
    /// dollar amount — it asks this class.
    ///
    /// The clock is SLOT-BASED, exactly as RG2 (sim/sleepSlot.ts): a day is
    /// three activity slots (morning/afternoon/night) and time only moves when
    /// the player spends one. Day 1 is a FRIDAY; weeks run FRI-SAT-SUN-MON..THU
    /// (dow = (day-1) % 7, 0 = FRI); months are a flat 30 days. Payday is
    /// Friday; bills land on the 1st.
    /// </summary>
    public static class LifeRules
    {
        // ================= calendar (config/calendar.ts) =================
        public static readonly string[] DowNames = { "FRI", "SAT", "SUN", "MON", "TUE", "WED", "THU" };
        public static readonly string[] SlotNames = { "MORNING", "AFTERNOON", "NIGHT" };
        public const int DaysPerMonth = 30;

        public static int Dow(int day) => ((day - 1) % 7 + 7) % 7;
        public static bool IsWeekend(int day) => Dow(day) == 1 || Dow(day) == 2;
        public static bool IsPayday(int day) => Dow(day) == 0;          // Friday
        public static int DayOfMonth(int day) => (day - 1) % DaysPerMonth + 1;
        public static string DateLabel(int day) =>
            DowNames[Dow(day)] + ", day " + DayOfMonth(day) + " of month " + ((day - 1) / DaysPerMonth + 1);

        // ================= jobs (config/jobs.ts via jobs extraction) =================
        // name, daily salary, starting-savings band (applyStartingConditions)
        public static readonly (string name, int dailyPay, int saveMin, int saveMax)[] Jobs =
        {
            ("AUTO PARTS RUN",   77,  400, 2000),
            ("TOW TRUCK",       115,  700, 3000),
            ("PARAMEDIC",       135, 1500, 5000),
            ("OFFICE JOB",      154, 2000, 8000),
            ("TRUCK DRIVER",    154, 1200, 4500),
            ("PACKAGE COURIER", 192,  800, 4000),
            ("FUEL TANKER",     231, 1500, 6000),
        };
        public const float PaycheckTaxRate = 0.22f;   // flat stand-in for calcPaycheckTax
        public const float ApplyHireChance = 0.55f;   // applyForJob.ts
        public const int NewHireWorkRep = 25;

        /// <summary>Daily work performance from sleep deprivation
        /// (workPerformance.ts): 0 nights → 1.0, then 0.6/0.35/0.15 plus a
        /// small age-scaled recovery term.</summary>
        public static float WorkPerformance(LifeState s)
        {
            float af = 1f - Mathf.Max(0f, (s.age - 20) * 0.01f);
            return s.daysSinceSleep switch
            {
                0 => 1.0f,
                1 => 0.6f + af * 0.2f,
                2 => 0.35f + af * 0.15f,
                _ => 0.15f + af * 0.1f,
            };
        }

        /// <summary>One worked day: accumulate pay into pendingSalary with the
        /// perf buckets (>=0.8 → 1.0x +3 rep; >=0.5 → 0.9x +1; else 0.75x and
        /// a coin-flip rep loss). Paid out on Friday.</summary>
        public static string WorkOneDay(LifeState s)
        {
            float perf = WorkPerformance(s);
            float mult; int rep;
            if (perf >= 0.8f) { mult = 1.0f; rep = 3; }
            else if (perf >= 0.5f) { mult = 0.9f; rep = 1; }
            else { mult = 0.75f; rep = Random.value < 0.55f ? -2 : 0; }

            int earned = Mathf.RoundToInt(s.basePay * s.payMultiplier * mult);
            s.pendingSalary += earned;
            s.workRep = Mathf.Clamp(s.workRep + rep, 0f, 100f);
            s.workDaysTotal++; s.workDaysPresent++;
            s.consecutiveAbsences = 0;
            s.workedToday = true;
            return perf >= 0.8f ? "A solid shift. +$" + earned
                 : perf >= 0.5f ? "A rough shift (tired). +$" + earned
                 : "You could barely function. +$" + earned;
        }

        // ================= unit bridge (physicsUnits.ts) =================
        // This project has been burned by the world-pixel trap before, so the
        // conversion constants live here and NOWHERE else.
        public const float WpxPerM = 6.2746f;
        public const float MetersPerMile = 1609.344f;

        // ================= street racing (streetTier.ts / trackRace.ts) =================
        // WIN_PRIZE is indexed by STREET TIER and paid ONLY on a win
        // (trackRace.ts:675-694). The flat prize descends by tier because
        // high-tier money comes from bets; losing pays $0 and +1 rep.
        public static readonly int[] WinPrize = { 500, 300, 150, 75 };
        static readonly int[] TierRepGain = { 6, 4, 2, 2 };
        public const int LossRepGain = 1;

        // Race fuel + wear (fast-travel factors converted to meters, x the H78
        // mileage ramp). RaceWearScale is THE balance knob: 1.0 = a race wears
        // exactly what the same miles wear in RG2 (new tires every ~4-5
        // races). Tune only with playtest evidence.
        public const float FuelPctPerMeter = 0.01619f;
        public const float TireWearPerM = 0.0062746f;
        public const float EngineWearPerM = 0.0031373f;
        public const float PaintWearPerM = 0.00062746f;
        public const float RaceWearScale = 1.0f;
        public const float RefuelCostPerPct = 0.1188f;   // $11.88 for a full 87-octane tank

        public static (int idx, string name) StreetTier(float rep) =>
            rep >= 75 ? (3, "INNER CIRCLE") :
            rep >= 50 ? (2, "TRUSTED") :
            rep >= 25 ? (1, "KNOWN") : (0, "OPEN");

        public static bool RacedToday(LifeState s) => s.lastRaceDay == s.day && s.lastRaceDay > 0;

        public static float RaceFuelBurnPct(float meters) => meters * FuelPctPerMeter;

        public static int RefuelCost(OwnedCar car) =>
            Mathf.CeilToInt((100f - car.fuel) * RefuelCostPerPct);

        /// <summary>Bank a finished race — the apply-back contract, in order:
        /// slot, odometer, fuel, wear, fault rolls, payout+rep, log.</summary>
        public static string ApplyRaceResult(LifeState s)
        {
            if (!RaceHandoff.ResultReady) return null;

            float meters = RaceHandoff.MetersDriven;
            var car = s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar;

            // 1. the race consumed a slot (may roll the day)
            SpendActivitySlot(s);

            if (car != null)
            {
                // 2-3. odometer + fuel
                car.odoMiles += meters / MetersPerMile;
                car.fuel = Mathf.Max(0f, car.fuel - RaceFuelBurnPct(meters) * RaceHandoff.FuelMult);

                // 4. wear: per-meter factors x mileage ramp, plus drift wear
                float wearMult = (1f + car.odoMiles / 100000f) * RaceWearScale;
                car.tires = Mathf.Max(0f, car.tires - TireWearPerM * meters * wearMult
                                              - 0.01f * RaceHandoff.DriftSeconds);
                car.engine = Mathf.Max(0f, car.engine - EngineWearPerM * meters * wearMult);
                car.carHP = Mathf.Max(0f, car.carHP - 0.005f * RaceHandoff.DriftSeconds);
                car.paint = Mathf.Max(0f, car.paint - PaintWearPerM * meters * wearMult
                                              - 0.003f * RaceHandoff.DriftSeconds);

                // 5. fault threshold rolls (H535): worn components start
                // throwing visible faults. v1 uses a generic per-component
                // fault; the full catalog lands with the garage pass.
                RollThresholdFault(s, car, "engine", car.engine);
                RollThresholdFault(s, car, "tires", car.tires);
                RollThresholdFault(s, car, "body", car.carHP);
            }

            // 6. payout + rep (win-only tier purse)
            string summary;
            if (RaceHandoff.IsPractice)
            {
                summary = "practice — P" + RaceHandoff.FinishPos + "/" + RaceHandoff.FieldSize;
            }
            else
            {
                int tier = StreetTier(s.streetRep).idx;
                s.streetRacesTotal++;
                int payout = 0;
                if (RaceHandoff.FinishPos == 1)
                {
                    payout = WinPrize[tier];
                    s.money += payout;
                    s.streetRacesWon++;
                    s.streetRep = Mathf.Min(100f, s.streetRep + TierRepGain[tier]);
                }
                else s.streetRep = Mathf.Min(100f, s.streetRep + LossRepGain);
                s.lastRaceDay = s.day;

                summary = "P" + RaceHandoff.FinishPos + "/" + RaceHandoff.FieldSize +
                          (payout > 0 ? " — won " + MenuKit.Money(payout) : " — no prize");
            }

            // 7. log + clear
            s.calendarLog.Add("Day " + s.day + ": race " + summary);
            RaceHandoff.ClearResult();
            return summary;
        }

        static void RollThresholdFault(LifeState s, OwnedCar car, string comp, float value)
        {
            bool severe = value < 15f;
            if (!severe && value >= 40f) return;
            string id = (severe ? "severe-" : "worn-") + comp;
            if (car.faults.Exists(f => f.id == id)) return;
            car.faults.Add(new CarFault
            {
                id = id,
                label = (severe ? "SEVERE: " : "Worn ") + comp,
                hidden = false,   // v1 pushes threshold faults visible
                diagnosed = true,
                severity = severe ? 2f : 1f,
            });
            s.calendarLog.Add("Day " + s.day + ": DIAGNOSED — " +
                              (severe ? "severe " : "worn ") + comp);
        }

        // ================= food & health (health.ts / sleepSlot.ts) =================
        // junk $8 → 4 meals, regular $25 → 5, premium $45 → 4 (GROCERY_OPTIONS)
        public static readonly (string tier, int cost, int meals)[] Groceries =
        {
            ("junk", 8, 4), ("regular", 25, 5), ("premium", 45, 4),
        };

        public static string EatMeal(LifeState s, string tier)
        {
            if (s.foodStock <= 0) return "No food in the house.";
            s.foodStock--;
            s.ateToday = true;
            s.daysSinceEat = 0;
            s.lastMealTier = tier;
            return "You eat (" + tier + "). " + s.foodStock + " meals left.";
        }

        /// <summary>The daily health update (updateDailyHealth), run once per
        /// rollover BEFORE latches reset. Hunger −2/−4/−8/−12; meal tier
        /// premium +2 / regular +1 / junk −1; sleep-debt −3/−7/−12 scaled by
        /// age; natural recovery when fed and rested.</summary>
        static void UpdateDailyHealth(LifeState s, bool sleptTonight)
        {
            float h = s.health;

            if (!s.ateToday)
            {
                s.daysSinceEat++;
                h -= s.daysSinceEat switch { 1 => 2f, 2 => 4f, 3 => 8f, _ => 12f };
            }
            else h += s.lastMealTier == "premium" ? 2f : s.lastMealTier == "junk" ? -1f : 1f;

            float agePenalty = 1f + Mathf.Max(0f, (s.age - 25) * 0.02f);
            if (!sleptTonight)
            {
                s.daysSinceSleep++;
                h -= Mathf.Round((s.daysSinceSleep switch { 1 => 3f, 2 => 7f, _ => 12f }) * agePenalty);
            }
            else
            {
                if (s.daysSinceSleep > 0) h += s.age <= 25 ? 3f : 2f;
                s.daysSinceSleep = 0;
            }

            if (h < 75f && s.ateToday && s.daysSinceSleep == 0)
            {
                h += s.age <= 30 ? 3f : 2f;
                if (s.fitness >= 60f) h += 1f;
            }

            s.fitness = Mathf.Max(0f, s.fitness - (0.3f + Mathf.Max(0f, (s.age - 20) * 0.01f)));
            s.health = Mathf.Clamp(h, 0f, 100f);
        }

        public static string HealthLabel(float h) =>
            h >= 85 ? "Excellent" : h >= 65 ? "Good" : h >= 45 ? "Fair" :
            h >= 25 ? "Poor" : h >= 10 ? "Bad" : "Critical";

        // ================= housing & bills (billsCalc.ts / insurance.ts) =================
        // v1 housing ladder subset; start = cheapest apartment.
        public static readonly (string key, string label, int rent, int slots)[] Housing =
        {
            ("apt1br", "1BR APARTMENT", 425, 1),
            ("apt2br", "2BR APARTMENT", 575, 2),
            ("rentHouse", "RENTED HOUSE", 750, 3),
        };
        public const int InsuranceBase = 50;          // $/mo
        public const float InsuranceValueRate = 0.005f; // +0.5% of fleet value /mo

        public static int MonthlyInsurance(LifeState s)
        {
            float fleet = 0f;
            foreach (var c in s.cars) fleet += Mathf.Max(0, c.paidPrice);
            return InsuranceBase + Mathf.RoundToInt(fleet * InsuranceValueRate);
        }

        public static int MonthlyHousing(LifeState s) => s.monthlyHousingCost;

        public static int MonthlyLoanPayments(LifeState s)
        {
            int total = 0;
            foreach (var l in s.carLoans) if (l.monthsRemaining > 0) total += l.monthlyPayment;
            foreach (var l in s.bankLoans) if (l.monthsRemaining > 0) total += l.monthlyPayment;
            return total;
        }

        public static int MonthlyTotalDue(LifeState s) =>
            MonthlyHousing(s) + MonthlyInsurance(s) + MonthlyLoanPayments(s);

        static void FireMonthlyBills(LifeState s)
        {
            int due = MonthlyTotalDue(s);
            if (s.money >= due)
            {
                s.money -= due;
                s.creditScore = Mathf.Min(850, s.creditScore + 2);   // on-time (+2, credit.ts)
                s.calendarLog.Add("Day " + s.day + ": bills paid — " + MenuKit.Money(due));
            }
            else
            {
                s.missedPayments++;
                s.creditScore = Mathf.Max(300, s.creditScore - 40);  // missed (−40)
                s.calendarLog.Add("Day " + s.day + ": MISSED BILLS (" + MenuKit.Money(due) +
                                  ") — strike " + s.missedPayments);
            }
            foreach (var l in s.carLoans) if (l.monthsRemaining > 0) l.monthsRemaining--;
            foreach (var l in s.bankLoans) if (l.monthsRemaining > 0) l.monthsRemaining--;
        }

        // ================= the slot machine (sleepSlot.ts) =================
        /// <summary>Burn one non-rest slot (work, race, errand). Rolls the day
        /// as an all-nighter if it was the last slot of the night.</summary>
        public static void SpendActivitySlot(LifeState s)
        {
            s.slotsActiveToday++;
            s.slotIndex++;
            if (s.slotIndex > 2) Rollover(s, sleptTonight: false);
        }

        /// <summary>Sleep: end the day rested (+5 health, sleepSlot.ts).</summary>
        public static void Sleep(LifeState s)
        {
            s.health = Mathf.Min(100f, s.health + 5f);
            Rollover(s, sleptTonight: true);
        }

        /// <summary>The single day-rollover pipeline, in the gameLoop's order:
        /// absence check → health → rep decay → payday → bills → latch reset.
        /// Everything funnels through here so nothing double-fires.</summary>
        static void Rollover(LifeState s, bool sleptTonight)
        {
            // 1. no-show: employed, weekday, didn't work (noShowAbsence.ts ladder)
            if (!string.IsNullOrEmpty(s.playerJob) && !IsWeekend(s.day) && !s.workedToday)
            {
                s.consecutiveAbsences++;
                float loss = s.consecutiveAbsences switch { 1 => 5f, 2 => 15f, _ => 30f };
                s.workRep = Mathf.Max(0f, s.workRep - loss);
                s.workDaysTotal++;
                s.calendarLog.Add("Day " + s.day + ": missed work (−" + loss + " rep)");
                if (s.consecutiveAbsences >= 3 || s.workRep <= 0f)
                {
                    s.calendarLog.Add("Day " + s.day + ": FIRED from " + s.playerJob);
                    s.playerJob = ""; s.basePay = 0; s.fired = true;
                    s.creditScore = Mathf.Max(300, s.creditScore - 25);
                }
            }

            // 2. daily health
            UpdateDailyHealth(s, sleptTonight);

            // 3. street rep decay: 7-day grace, then −1/day (−2 above 50)
            if (s.lastRaceDay >= 0 && s.day - s.lastRaceDay > 7)
                s.streetRep = Mathf.Max(0f, s.streetRep - (s.streetRep > 50f ? 2f : 1f));

            // 4. advance the calendar
            s.day++;
            s.slotIndex = 0;
            s.slotsActiveToday = 0;

            // 5. payday (Friday) — flat-rate withheld tax
            if (IsPayday(s.day - 1) && s.pendingSalary > 0)
            {
                int net = Mathf.RoundToInt(s.pendingSalary * (1f - PaycheckTaxRate));
                s.money += net;
                s.calendarLog.Add("Day " + (s.day - 1) + ": PAYDAY +" + MenuKit.Money(net));
                s.pendingSalary = 0;
            }

            // 6. bills on the 1st
            if (DayOfMonth(s.day) == 1) FireMonthlyBills(s);

            // 7. daily latches
            s.ateToday = false;
            s.workedToday = false;
        }

        // ================= new game (startingConditions.ts) =================
        public static LifeState SeedNewGame(string name, int age, int jobIdx)
        {
            var job = Jobs[Mathf.Clamp(jobIdx, 0, Jobs.Length - 1)];
            var s = new LifeState
            {
                playerName = string.IsNullOrEmpty(name) ? "DRIVER" : name,
                age = Mathf.Clamp(age, 21, 60),
                money = Random.Range(job.saveMin, job.saveMax + 1),
                playerJob = job.name,
                basePay = job.dailyPay,
                workRep = NewHireWorkRep,
                housingType = "apt1br",
                monthlyHousingCost = 425,
                foodStock = 4,
                lastMealTier = "regular",
            };

            // v1: one starting car, the FD the race scene drives. A tired but
            // honest example — the used-car market arrives with the next pass.
            // RG2's no-service-record seed: cond = max(15, 100 - miles/3000)
            // on all four stats — a 73.3k-mile FD arrives around 76.
            float odoMiles = 73300f;
            float cond = Mathf.Max(15f, Mathf.Round(100f - odoMiles / 3000f));
            var car = new OwnedCar
            {
                id = "rx7-fd",
                displayName = "Mazda RX-7 Type RS (FD) '98",
                odoMiles = odoMiles,
                fuel = Random.Range(30f, 70f),
                paidPrice = 9500,
                engine = cond, tires = cond, carHP = cond, paint = cond,
            };
            s.cars.Add(car);
            s.activeCar = car.id;
            s.calendarLog.Add("Day 1 (FRI): moved in. " + job.name + ", " +
                              MenuKit.Money(s.money) + " saved.");
            return s;
        }

        /// <summary>Back-compat: parameterless seed used before the wizard runs.</summary>
        public static LifeState SeedNewGame() => SeedNewGame("DRIVER", 25, 0);
    }
}
