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

        /// <summary>
        /// What a truck charges to come to you. Fuel is bought at the pumps on
        /// the circuit now; this is the price of not having planned a stop, and
        /// it is deliberately several times the tank it delivers so that
        /// planning one is always the cheaper answer.
        ///
        /// It exists at all because a resource you can only buy in one PLACE
        /// can strand a player who runs dry somewhere else — and being stuck
        /// with no legal move is not a difficulty setting, it is a bug.
        /// </summary>
        public const int FuelCallOutFee = 40;

        /// <summary>Fill this tank without driving to a pump.</summary>
        public static int CallOutRefuelCost(float fuelPct) =>
            FuelCallOutFee + Mathf.CeilToInt(Mathf.Max(0f, 100f - fuelPct) * RefuelCostPerPct);

        public static int CallOutRefuelCost(OwnedCar car) =>
            car == null ? FuelCallOutFee : CallOutRefuelCost(car.fuel);

        /// <summary>
        /// How far round the lap the forecourt sits. The scene builder puts it
        /// at 62% of the waypoints; this is the runtime half of that contract,
        /// and the pre-race fuel gate is measured against it.
        /// </summary>
        public const float FuelStopLapFraction = 0.62f;

        /// <summary>
        /// The tank a car needs before it is allowed to line up.
        ///
        /// It used to be the WHOLE race, because there was nowhere to buy fuel
        /// between the lights and the flag. On a circuit with pumps the honest
        /// number is much smaller: enough to reach the forecourt, with a
        /// half-again margin for a scrappy first lap and the detour off the
        /// racing line. On a strip, and on anything else with no pumps, it is
        /// still the whole run.
        /// </summary>
        public static float RequiredFuelPct(TrackCatalog.TrackDef track)
        {
            if (track == null) return 0f;
            if (!track.hasFuelStop) return RaceFuelBurnPct(track.RaceMeters);
            return RaceFuelBurnPct(track.LengthM * FuelStopLapFraction * 1.5f);
        }

        public static (int idx, string name) StreetTier(float rep) =>
            rep >= 75 ? (3, "INNER CIRCLE") :
            rep >= 50 ? (2, "TRUSTED") :
            rep >= 25 ? (1, "KNOWN") : (0, "OPEN");

        public static bool RacedToday(LifeState s) => s.lastRaceDay == s.day && s.lastRaceDay > 0;

        // ---------------- the field (P2) ----------------
        /// <summary>Cars the player lines up against in a normal street race.
        /// The scene's grid holds four; anything smaller retires the spares.
        /// </summary>
        public const int FieldOpponents = 3;

        /// <summary>
        /// Choose who shows up, and write them into the handoff.
        ///
        /// The field is drawn from the catalog around the PLAYER'S car, widening
        /// upward with street tier. Before this the answer was always four
        /// RX-7s: a beater and a supercar raced the identical field, so the one
        /// question the garage is supposed to make interesting — is this car any
        /// good — had the same answer whatever was in it.
        ///
        /// Returns false when the catalog cannot fill the grid, which leaves the
        /// handoff empty and the scene's built-in field alone.
        /// </summary>
        public static bool FillOpponentField(LifeState s)
        {
            var car = s.ActiveCar;
            if (car == null || !CarCatalog.Ready) return false;

            int reference = car.catalogPrice > 0 ? car.catalogPrice : Mathf.Max(1, car.paidPrice);
            int tier = StreetTier(s.streetRep).idx;

            // Band around the player's money. It opens UPWARD with tier rather
            // than sliding: the low end has to stay reachable or the field stops
            // containing anything the player could plausibly have beaten to get
            // here, and a race everyone loses is not a difficulty curve.
            float lo = 0.65f + tier * 0.08f;
            float hi = 1.20f + tier * 0.15f;
            var pool = CarCatalog.InPriceBand(Mathf.RoundToInt(reference * lo),
                                              Mathf.RoundToInt(reference * hi));
            // Both ends of the catalog are thin — a $5,500 Civic and a
            // $1,066,000 hypercar both have almost no neighbours — so widen
            // before giving up.
            if (pool.Count < FieldOpponents)
                pool = CarCatalog.InPriceBand(reference / 3, reference * 3);
            if (pool.Count < FieldOpponents) return false;

            var ids = new System.Text.StringBuilder();
            var skills = new System.Text.StringBuilder();
            float baseSkill = 0.88f + tier * 0.04f;
            for (int i = 0; i < FieldOpponents; i++)
            {
                // Draw without replacement: three copies of one car is a worse
                // grid than three different ones, and the pool is big enough
                // that removing three costs nothing.
                int pick = Random.Range(0, pool.Count);
                var spec = pool[pick];
                pool.RemoveAt(pick);

                // Spread so the field is not one wall of equally quick cars —
                // one to chase, one to race, one to catch.
                float skill = Mathf.Clamp(baseSkill + 0.04f - i * 0.04f, 0.80f, 1.05f);
                if (i > 0) { ids.Append(';'); skills.Append(';'); }
                ids.Append(spec.id);
                skills.Append(skill.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            RaceHandoff.OpponentSpecIds = ids.ToString();
            RaceHandoff.OpponentSkills = skills.ToString();
            return true;
        }

        /// <summary>
        /// A blacklist challenge: one opponent, the rival's signature car, at
        /// the rival's tuned skill. The other grid slots go home.
        /// </summary>
        public static bool FillRivalField(BlacklistRival rival)
        {
            var spec = rival != null ? Blacklist.ResolveCar(rival) : null;
            if (spec == null) return false;
            RaceHandoff.OpponentSpecIds = spec.id;
            RaceHandoff.OpponentSkills = rival.skill.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

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
                // The tank the car came home with, when the race scene actually
                // measured one. Deriving the burn from the distance again would
                // silently un-buy every gallon the player stopped for: the
                // pumps already charged them, and this would then charge them
                // the fuel as well.
                if (RaceHandoff.FuelReported)
                    car.fuel = Mathf.Clamp(RaceHandoff.EndFuelPct, 0f, 100f);
                else
                    car.fuel = Mathf.Max(0f, car.fuel - RaceFuelBurnPct(meters) * RaceHandoff.FuelMult);

                // 4. wear: per-meter factors x mileage ramp, plus drift wear
                float wearMult = (1f + car.odoMiles / 100000f) * RaceWearScale;
                car.tires = Mathf.Max(0f, car.tires - TireWearPerM * meters * wearMult
                                              - 0.01f * RaceHandoff.DriftSeconds);
                car.engine = Mathf.Max(0f, car.engine - EngineWearPerM * meters * wearMult);
                car.carHP = Mathf.Max(0f, car.carHP - 0.005f * RaceHandoff.DriftSeconds);
                car.paint = Mathf.Max(0f, car.paint - PaintWearPerM * meters * wearMult
                                              - 0.003f * RaceHandoff.DriftSeconds);

                // 4b. crash damage. CollisionResponder sums impact energy over
                // the race; heavy contact costs body and paint, and past a
                // threshold rolls an IMPACT-cause fault (the pools tag entries
                // by cause, so a wall hit surfaces bent metal rather than a
                // worn-out timing belt).
                float damage = RaceHandoff.DamageScore;
                if (damage > 0f)
                {
                    car.carHP = Mathf.Max(0f, car.carHP - damage * BodyDamagePerHit);
                    car.paint = Mathf.Max(0f, car.paint - damage * PaintDamagePerHit);
                    if (damage >= ImpactFaultThreshold)
                        AddFault(s, car, FaultCatalog.RollWearFault(car, "hp", damage >= ImpactFaultThreshold * 2.5f, "impact"));
                }

                // 4c. the DRIVING RECORD, which is a different quantity from the
                // repair bill: the insurer cares how many times you crashed, not
                // how expensive each one was. Capped per race so one shambolic
                // night cannot eat the whole L5 incident allowance at once.
                s.atFaultIncidents += Mathf.Min(RaceHandoff.HardHits, MaxIncidentsPerRace);

                // 5. fault threshold rolls (H535): worn components start
                // throwing faults. Below 40 rolls a minor one, below 15 a
                // severe one; the picker gates to one fault per stat (two at
                // severe), so this cannot spam.
                RollThresholdFault(s, car, "engine", car.engine);
                RollThresholdFault(s, car, "tires", car.tires);
                RollThresholdFault(s, car, "hp", car.carHP);
            }

            // 6. payout + rep (win-only tier purse)
            string summary;
            if (RaceHandoff.FreeRoam)
            {
                // A drive is not a result: no purse, no rep, and no rep-decay
                // reset — cruising Charlotte is not showing up on the street.
                // The metres, fuel and wear above are already banked.
                summary = "free roam — " + (RaceHandoff.MetersDriven / 1000f).ToString("0.0") + " km in Charlotte";
            }
            else if (RaceHandoff.IsPractice)
            {
                s.lastAnyRaceDay = s.day;   // rep-decay clock: every race resets it
                summary = "practice — P" + RaceHandoff.FinishPos + "/" + RaceHandoff.FieldSize;
            }
            else
            {
                s.lastAnyRaceDay = s.day;   // rep-decay clock: every race resets it
                int tier = StreetTier(s.streetRep).idx;
                s.streetRacesTotal++;
                // The purse rides in with the race so the pre-race screen and the
                // payout agree. Fall back to the tier table when the race scene
                // is played standalone and nothing filled the handoff.
                int purse = RaceHandoff.PurseWin > 0 ? RaceHandoff.PurseWin : WinPrize[tier];
                int payout = 0;
                if (RaceHandoff.FinishPos == 1)
                {
                    payout = purse;
                    s.streetRacesWon++;
                    s.streetRep = Mathf.Min(100f, s.streetRep + TierRepGain[tier]);
                }
                else
                {
                    // Grid races taper by position rather than paying winner-take-
                    // all (trackRace.ts): in a four-car field, second place still
                    // covers a tank of fuel, which is what keeps a bad race from
                    // being a total loss.
                    int field = Mathf.Max(1, RaceHandoff.FieldSize);
                    float scale = Mathf.Max(0f, 1f - (RaceHandoff.FinishPos - 1) / (float)field);
                    payout = Mathf.RoundToInt(purse * scale);
                    s.streetRep = Mathf.Min(100f, s.streetRep + LossRepGain);
                }
                s.money += payout;
                // A blacklist challenge does NOT burn the one-purse-race-a-day
                // cap. Ten of them exist in a career and each needs its own
                // gate cleared first, so there is nothing here to grind — and
                // making the player sleep off a challenge would mean the page
                // that invited them expires while they wait for tomorrow.
                if (RaceHandoff.RivalRank <= 0) s.lastRaceDay = s.day;

                summary = "P" + RaceHandoff.FinishPos + "/" + RaceHandoff.FieldSize +
                          (payout > 0 ? " — won " + MenuKit.Money(payout) : " — no prize");

                if (RaceHandoff.RivalRank > 0)
                {
                    // Recorded AFTER the normal payout: the challenge is a street
                    // race first, so it pays, counts for wins and moves rep on the
                    // same rules as any other, then the ladder takes its cut.
                    string ladder = Blacklist.RecordResult(s, RaceHandoff.RivalRank,
                                                           RaceHandoff.FinishPos == 1);
                    if (!string.IsNullOrEmpty(ladder)) summary = ladder + "  ·  " + summary;
                }
            }

            // A pit stop is part of the story of the race, so it goes in the
            // line the player reads on the way home. The money already left the
            // wallet at the pump — this is the receipt.
            if (RaceHandoff.FuelSpent > 0)
                summary += "  ·  fuel " + MenuKit.Money(RaceHandoff.FuelSpent);

            // 7. log + clear
            s.calendarLog.Add("Day " + s.day + ": race " + summary);
            RaceHandoff.ClearResult();
            return summary;
        }

        static void RollThresholdFault(LifeState s, OwnedCar car, string stat, float value)
        {
            bool severe = value < 15f;
            if (!severe && value >= 40f) return;
            AddFault(s, car, FaultCatalog.RollWearFault(car, stat, severe));
        }

        /// <summary>Commit a rolled fault. Null is the normal "the gate said no"
        /// answer from the picker, not an error.</summary>
        static void AddFault(LifeState s, OwnedCar car, CarFault f)
        {
            if (f == null) return;
            car.faults.Add(f);
            s.calendarLog.Add("Day " + s.day + ": DIAGNOSED — " + f.label +
                              " ($" + f.cost + ")");
            lastDiagnosed = f.label;
        }

        /// <summary>Set by the last apply-back so the result screen can show a
        /// "DIAGNOSED:" line. Read once, then cleared.</summary>
        public static string lastDiagnosed;

        // ================= repairs (repairCost.ts / pendingParts.ts) =================
        // Crash damage: DamageScore is roughly summed closing speed in m/s, so a
        // firm 8 m/s hit costs ~5 body. Deliberately cheaper than wear per race —
        // crashing should sting, not end a career in one mistake.
        public const float BodyDamagePerHit = 0.65f;
        public const float PaintDamagePerHit = 0.4f;
        public const float ImpactFaultThreshold = 22f;
        /// <summary>At-fault incidents one race can put on the record. L5's
        /// insurance multiplier caps at six incidents total, so an uncapped race
        /// could spend the whole allowance in one lap of wall-riding.</summary>
        public const int MaxIncidentsPerRace = 2;

        public static bool CarInShop(LifeState s, OwnedCar car) =>
            car != null && s.pendingParts.Exists(p => p.carId == car.id);

        /// <summary>
        /// Book a repair. DIY and mechanic work queue into pendingParts and
        /// resolve on the rollover; the dealer is same-day and applies at once.
        /// Returns null on success, or the reason it was refused.
        /// </summary>
        public static string OrderRepair(LifeState s, OwnedCar car, CarFault f,
                                         FaultCatalog.Venue venue)
        {
            if (car == null || f == null) return "no car";
            if (s.pendingParts.Exists(p => p.carId == car.id && p.faultId == f.id))
                return "already booked";

            var q = FaultCatalog.GetQuote(s, car, f, venue);
            if (!q.available) return q.blockedReason;
            if (s.money < q.price) return "need " + MenuKit.Money(q.price);

            s.money -= q.price;
            if (venue == FaultCatalog.Venue.Diy)
                s.mechSkill = Mathf.Min(100f, s.mechSkill +
                                        FaultCatalog.DiySkillGain(s.mechSkill, q.difficulty));

            if (q.days <= 0)
            {
                ApplyRepair(s, car, f);
                s.calendarLog.Add("Day " + s.day + ": fixed " + f.label +
                                  " (" + MenuKit.Money(q.price) + ", dealer)");
                return null;
            }

            s.pendingParts.Add(new PendingPart
            {
                carId = car.id,
                faultId = f.id,
                label = f.label,
                stat = f.stat,
                add = f.add,
                readyDay = s.day + q.days,
                venue = (int)venue,
            });
            s.calendarLog.Add("Day " + s.day + ": booked " + f.label +
                              " (" + MenuKit.Money(q.price) + ", " + q.days + "d)");
            return null;
        }

        static void ApplyRepair(LifeState s, OwnedCar car, CarFault f)
        {
            AddToStat(car, f.stat, f.add);
            car.faults.RemoveAll(x => x.id == f.id);
        }

        static void AddToStat(OwnedCar car, string stat, float amount)
        {
            switch (stat)
            {
                case "engine": car.engine = Mathf.Min(100f, car.engine + amount); break;
                case "tires": car.tires = Mathf.Min(100f, car.tires + amount); break;
                case "paint": car.paint = Mathf.Min(100f, car.paint + amount); break;
                default: car.carHP = Mathf.Min(100f, car.carHP + amount); break;
            }
        }

        /// <summary>Resolve every repair whose day has come. Called from the one
        /// Rollover pipeline, so waiting on parts costs real days.</summary>
        static void TickPendingParts(LifeState s)
        {
            for (int i = s.pendingParts.Count - 1; i >= 0; i--)
            {
                var p = s.pendingParts[i];
                if (s.day < p.readyDay) continue;
                var car = s.FindCar(p.carId);
                if (car != null)
                {
                    if (p.IsUpgrade)
                    {
                        // A build only ever steps UP. Max() rather than a plain
                        // assign because two jobs on the same category cannot be
                        // queued, but a save edited or migrated out from under
                        // one should not be able to UNDO a stage already paid for.
                        var kind = Upgrades.KindFromKey(p.upgradeKind);
                        Upgrades.SetStage(car, kind,
                            Mathf.Max(Upgrades.GetStage(car, kind), p.upgradeStage));
                        s.calendarLog.Add("Day " + s.day + ": " + p.label + " installed");
                    }
                    else
                    {
                        AddToStat(car, p.stat, p.add);
                        car.faults.RemoveAll(x => x.id == p.faultId);
                        s.calendarLog.Add("Day " + s.day + ": " + p.label + " repaired");
                    }
                }
                s.pendingParts.RemoveAt(i);
            }
        }

        // ================= mechanic services (MECHANIC_SERVICES) =================
        // The "just make the numbers go up" counterpart to fault repair. A
        // service also CLEARS faults on the stat lane it touches, which is how a
        // set of new tires makes the tire-wear fault go away.
        public static readonly (string name, int cost, int add, string stat)[] MechanicServices =
        {
            ("OIL CHANGE",       50,  15, "engine"),
            ("ENGINE TUNE-UP",  200,  35, "engine"),
            ("TIRE ROTATION",    40,  20, "tires"),
            ("NEW TIRES",       300,  60, "tires"),
            ("BODY PATCH",       80,  20, "hp"),
            ("FULL BODY WORK",  350,  50, "hp"),
            ("PAINT TOUCH-UP",   60,  30, "paint"),
        };

        /// <summary>Services scale with the car the way repairs do — the same
        /// clamp(sqrt(price/15000), 0.6, 3.5) shape, so an oil change on an
        /// exotic costs more than on a beater but not absurdly so.</summary>
        public static int ServiceCost(OwnedCar car, int baseCost)
        {
            float mult = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, car.paidPrice) / 15000f), 0.6f, 3.5f);
            return Mathf.RoundToInt(baseCost * mult);
        }

        public static string BuyService(LifeState s, OwnedCar car, int serviceIdx)
        {
            if (car == null) return "no car";
            var svc = MechanicServices[serviceIdx];
            int price = ServiceCost(car, svc.cost);
            if (s.money < price) return "need " + MenuKit.Money(price);
            s.money -= price;
            AddToStat(car, svc.stat, svc.add);
            car.faults.RemoveAll(f => f.stat == svc.stat);
            // A service supersedes any repair queued for the same stat lane —
            // new tyres make a booked tyre job pointless. Upgrade builds are
            // explicitly spared: they are filed under the "engine" stat for
            // want of a better lane, and an oil change must not cancel a
            // paid-for turbo build.
            s.pendingParts.RemoveAll(p => p.carId == car.id && p.stat == svc.stat && !p.IsUpgrade);
            s.calendarLog.Add("Day " + s.day + ": " + svc.name + " " + MenuKit.Money(price));
            return null;
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

            // 3. street rep decay: 7-day grace, then −1/day (−2 above 50).
            // Reads lastAnyRaceDay, not the cap clock — a challenge is racing.
            if (s.lastAnyRaceDay >= 0 && s.day - s.lastAnyRaceDay > 7)
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

            // 7. repairs whose day has come
            TickPendingParts(s);

            // 8. the market turns over: listings expire and refill, and any car
            // the player has advertised may draw an offer.
            CarMarket.RefreshListings(s);
            CarMarket.GenerateOffers(s);

            // 9. the ladder: expired call-outs go cold, and a gate that has just
            // cleared pages the player. Order matters — pruning first stops a
            // page written this morning being swept the same morning.
            s.mail.RemoveAll(m => m.expiresDay > 0 && s.day > m.expiresDay);
            lastPage = Blacklist.TickPager(s);

            // 10. daily latches
            s.ateToday = false;
            s.workedToday = false;
        }

        /// <summary>Call-out fired by the last rollover, for the home screen to
        /// toast. Read once, then cleared — same contract as lastDiagnosed.</summary>
        public static string lastPage;

        // ================= new game (startingConditions.ts) =================
        /// <summary>
        /// Seed a character WITHOUT a car — the starting-lane picker is a second
        /// wizard step, because which car you arrive in is the first real
        /// decision the game asks for.
        /// </summary>
        /// <summary>Name that opens a debug career, matched case-insensitively
        /// and after trimming — the wizard upper-cases and trims what it is
        /// given, so the player types "Test" and this sees "TEST".</summary>
        public const string DebugName = "TEST";
        /// <summary>Deliberately one dollar short of a seventh digit: it is
        /// unmistakably a cheat rather than a plausible balance, and it still
        /// formats inside the money field's width.</summary>
        public const int DebugMoney = 999999;
        /// <summary>Garage capacity under debug. Normal play ships ONE slot and
        /// nothing anywhere raises it, so a second car cannot be owned at all —
        /// CarMarket.Buy refuses with "garage full (1)". That is a real content
        /// gap rather than a bug, but it makes the market impossible to exercise,
        /// which is exactly what a debug save is for.</summary>
        public const int DebugGarageSlots = 6;
        /// <summary>Top of CarMarket's credit ladder, so financing can be tested
        /// as well as cash.</summary>
        public const int DebugCredit = 820;

        /// <summary>
        /// Turn an existing career into a test career. Idempotent, so the same
        /// call serves both the wizard's TEST name and the TOP UP button.
        /// Deliberately does NOT touch the day/fuel gates — those have their own
        /// button, because clearing them silently would hide the very rules a
        /// tester might be trying to observe.
        /// </summary>
        public static void EnableDebug(LifeState s)
        {
            if (s == null) return;
            s.debugMode = true;
            s.money = DebugMoney;
            s.garageSlots = Mathf.Max(s.garageSlots, DebugGarageSlots);
            s.creditScore = Mathf.Max(s.creditScore, DebugCredit);
        }

        public static LifeState SeedNewGame(string name, int age, int jobIdx)
        {
            var job = Jobs[Mathf.Clamp(jobIdx, 0, Jobs.Length - 1)];
            bool debug = IsDebugName(name);
            var s = new LifeState
            {
                playerName = string.IsNullOrEmpty(name) ? "DRIVER" : name,
                age = Mathf.Clamp(age, 21, 60),
                money = debug ? DebugMoney : Random.Range(job.saveMin, job.saveMax + 1),
                debugMode = debug,
                playerJob = job.name,
                basePay = job.dailyPay,
                workRep = NewHireWorkRep,
                housingType = "apt1br",
                monthlyHousingCost = 425,
                foodStock = 4,
                lastMealTier = "regular",
            };
            s.creditScore = StartingCredit(s.age, s.money, job.name);
            // Same switch the in-career button uses, so the two entry points
            // cannot drift into granting different things.
            if (debug) EnableDebug(s);
            s.calendarLog.Add("Day 1 (FRI): moved in. " + job.name + ", " +
                              MenuKit.Money(s.money) + " saved." +
                              (debug ? "  [DEBUG CAREER]" : ""));
            CarMarket.RefreshListings(s);
            return s;
        }

        /// <summary>Is this the debug name? Kept next to the seed so the wizard
        /// and the seed cannot disagree about what counts.</summary>
        public static bool IsDebugName(string name) =>
            !string.IsNullOrEmpty(name) &&
            string.Equals(name.Trim(), DebugName, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Fallback used when the catalog is unavailable, and by the
        /// back-compat seed: the FD the race scene has always driven.</summary>
        public static void SeedFallbackCar(LifeState s)
        {
            if (s.cars.Count > 0) return;
            // RG2's no-service-record seed: cond = max(15, 100 - miles/3000).
            float odoMiles = 73300f;
            float cond = Mathf.Max(15f, Mathf.Round(100f - odoMiles / 3000f));
            var car = new OwnedCar
            {
                id = "rx7-fd",
                displayName = "Mazda RX-7 Type RS (FD) '98",
                specId = "",
                catalogPrice = 36000,
                odoMiles = odoMiles,
                fuel = Random.Range(30f, 70f),
                paidPrice = 9500,
                engine = cond, tires = cond, carHP = cond, paint = cond,
            };
            s.cars.Add(car);
            s.activeCar = car.id;
        }

        /// <summary>calcStartingCredit — age, savings and the respectability of
        /// the job you walked in with (credit.ts).</summary>
        public static int StartingCredit(int age, int money, string jobName)
        {
            int jobAdj =
                jobName == "OFFICE JOB" ? 40 :
                jobName == "FUEL TANKER" ? 35 :
                jobName == "PACKAGE COURIER" ? 30 :
                jobName == "TRUCK DRIVER" ? 30 :
                jobName == "PARAMEDIC" ? 25 :
                jobName == "TOW TRUCK" ? 15 :
                jobName == "AUTO PARTS RUN" ? 10 : 0;
            int savingsAdj = Mathf.Min(120, Mathf.FloorToInt(money / 1000f) * 8);
            return Mathf.Clamp(650 + (age - 25) * 6 + savingsAdj + jobAdj, 350, 850);
        }

        /// <summary>Back-compat: parameterless seed used before the wizard runs.
        /// Includes a car, since nothing downstream will pick one.</summary>
        public static LifeState SeedNewGame()
        {
            var s = SeedNewGame("DRIVER", 25, 0);
            SeedFallbackCar(s);
            return s;
        }
    }
}
