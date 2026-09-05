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
    /// the player spends one. Day 1 is FRIDAY 1 JANUARY 1999 — a real date on a
    /// real calendar, and a Friday, which is why the game's own FRI-first week
    /// (dow = (day-1) % 7, 0 = FRI) is also the true day of the week for every
    /// day of a career. Payday is Friday; bills land on the 1st.
    /// </summary>
    public static class LifeRules
    {
        // ================= calendar (config/calendar.ts) =================
        //
        // The game is set in 1999, like the original — and the calendar is a
        // REAL one rather than the flat 30-day counter this shipped with. Two
        // reasons, and the second is what decided it: a screen that lets you
        // plan around paydays and bills has to agree with the month it is
        // printing, and "day 14 of month 1" is not a date anybody plans around.
        //
        // The anchor is a gift. Day 1 was already a FRIDAY, dow already ran
        // FRI-SAT-SUN-MON..THU, and **1 January 1999 was a Friday** — so the
        // existing (day-1)%7 convention IS the real day of the week for every
        // day of the career, with nothing to reconcile and no save to migrate.
        // The calendar grid starts weeks on Friday for the same reason it always
        // has: payday is a column, and January 1999 happens to fill the top-left
        // cell exactly.
        public static readonly string[] DowNames = { "FRI", "SAT", "SUN", "MON", "TUE", "WED", "THU" };
        public static readonly string[] SlotNames = { "MORNING", "AFTERNOON", "NIGHT" };
        /// <summary>Month names spelled out here rather than taken from the
        /// culture. WebGL ships an invariant-ish culture set and a menu that
        /// renders "janv." on somebody's phone is a bug nobody can reproduce.
        /// </summary>
        public static readonly string[] MonthNames =
        {
            "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE",
            "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER",
        };
        public static readonly string[] MonthShort =
        {
            "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
            "JUL", "AUG", "SEP", "OCT", "NOV", "DEC",
        };

        /// <summary>Day 1 of a career. A Friday, which is what makes the whole
        /// existing week convention line up with the real 1999.</summary>
        public static readonly System.DateTime Epoch = new System.DateTime(1999, 1, 1);

        /// <summary>The real date an absolute day number lands on.</summary>
        public static System.DateTime DateOf(int day) => Epoch.AddDays(Mathf.Max(1, day) - 1);

        /// <summary>Absolute day number for a real date — the inverse of
        /// <see cref="DateOf"/>, for a calendar grid that walks months rather
        /// than days.</summary>
        public static int DayNumber(System.DateTime d) =>
            (int)(d.Date - Epoch).TotalDays + 1;

        public static int Dow(int day) => ((day - 1) % 7 + 7) % 7;
        public static bool IsWeekend(int day) => Dow(day) == 1 || Dow(day) == 2;
        public static bool IsPayday(int day) => Dow(day) == 0;          // Friday
        public static int DayOfMonth(int day) => DateOf(day).Day;
        public static int MonthOf(int day) => DateOf(day).Month;
        public static int YearOf(int day) => DateOf(day).Year;
        /// <summary>Length of the month a day falls in. Was a flat 30 for the
        /// whole game; the bills tab counts down to the 1st with it, so a
        /// February that claimed 30 days would have been counting to a date
        /// that does not exist.</summary>
        public static int DaysInMonth(int day)
        {
            var d = DateOf(day);
            return System.DateTime.DaysInMonth(d.Year, d.Month);
        }

        /// <summary>"FRI 1 JAN 1999" — short enough for the header line, which
        /// also carries the slot and the debug flag.</summary>
        public static string DateLabel(int day)
        {
            var d = DateOf(day);
            return DowNames[Dow(day)] + " " + d.Day + " " + MonthShort[d.Month - 1] + " " + d.Year;
        }

        /// <summary>"JANUARY 1999", for the calendar's own header.</summary>
        public static string MonthLabel(int day)
        {
            var d = DateOf(day);
            return MonthNames[d.Month - 1] + " " + d.Year;
        }

        /// <summary>Date stamp for a diary line: "1 JAN". Deliberately as short
        /// as the "Day 1" it replaces — the RECENTLY list on MAIN is one line
        /// per entry with no room to spare, and the entry itself is the
        /// interesting half. The year is said once, in the header.</summary>
        public static string LogDate(int day)
        {
            var d = DateOf(day);
            return d.Day + " " + MonthShort[d.Month - 1];
        }

        // ================= the diary =================
        /// <summary>
        /// Races the player has planned, and the rules around planning them.
        ///
        /// A booking is a note to yourself, not a contract: nothing is charged
        /// for making one and nothing is taken away for missing one. That is
        /// deliberate. The ask was for a way to PLAN — and the scarce thing in
        /// this game is already the three slots in a day, so a diary that also
        /// fined you would be charging twice for the same decision. What a
        /// booking buys is the ability to look at a month and see the night you
        /// meant to race sitting next to the day the bills land.
        /// </summary>
        public static RaceBooking BookingOn(LifeState s, int day) =>
            s == null || s.bookings == null ? null : s.bookings.Find(b => b != null && b.day == day);

        /// <summary>One race a day, because a race costs a slot and there are
        /// three of those — a day with two bookings on it is a day the player
        /// has already lost by lunchtime.</summary>
        public static bool Book(LifeState s, int day, int trackIndex, bool practice)
        {
            if (s == null || day < s.day) return false;
            if (s.bookings == null) s.bookings = new System.Collections.Generic.List<RaceBooking>();
            if (BookingOn(s, day) != null) return false;
            s.bookings.Add(new RaceBooking { day = day, trackIndex = trackIndex, practice = practice });
            return true;
        }

        public static void Unbook(LifeState s, int day)
        {
            if (s == null || s.bookings == null) return;
            s.bookings.RemoveAll(b => b == null || b.day == day);
        }

        /// <summary>How far ahead the diary lets you write. Four weeks is more
        /// than anything in this game has a horizon for — the longest repair is
        /// days and the rent is monthly — so it is a limit that exists to stop
        /// the calendar becoming a list of a hundred stale intentions rather
        /// than to stop the player doing anything they wanted to.</summary>
        public const int BookingHorizonDays = 28;

        // ================= jobs (config/jobs.ts via jobs extraction) =================
        // name, daily salary, starting-savings band (applyStartingConditions)
        //
        // ONE JOB, on purpose. The eight-job book came across from RG2 whole,
        // where a job was a button that added money — and seven of those eight
        // still are. The game the owner is building is the one job you actually
        // DRIVE: collect an order, walk out, and run it across town against the
        // clock. Every other career is a menu that pays better for doing less,
        // which is a straight argument against the only content in the game.
        //
        // They are COMMENTED rather than deleted. Everything that reads this
        // table reads it by name or by index into it, so parking the rows keeps
        // the whole shape intact — StartingCredit still carries their credit
        // adjustments below — and restoring one is uncommenting a line.
        public static readonly (string name, int dailyPay, int saveMin, int saveMax)[] Jobs =
        {
            // ("AUTO PARTS RUN",   77,  400, 2000),
            // ("TOW TRUCK",       115,  700, 3000),
            // ("PARAMEDIC",       135, 1500, 5000),
            // ("OFFICE JOB",      154, 2000, 8000),
            // ("TRUCK DRIVER",    154, 1200, 4500),
            // ("PACKAGE COURIER", 192,  800, 4000),
            // ("FUEL TANKER",     231, 1500, 6000),
            // $0 salary + tips, and you eat on shift.
            ("FOOD DELIVERY",    96,  300, 1500),
        };

        // ================= the shift roster =================
        /// <summary>
        /// When the shop takes drivers: AFTERNOON and NIGHT, seven days a week.
        ///
        /// The old rule was every job's rule — weekdays only — and it printed
        /// "WEEKEND — NO WORK" across the two days a pizza shop is busiest. A
        /// delivery roster is the opposite shape: nothing before noon, and
        /// Friday and Saturday nights ARE the job.
        ///
        /// The slots are the hours. <see cref="TimeOfDay.ForSlot"/> puts slot 1
        /// between 12:30 and 16:10 and slot 2 between sunset and the small
        /// hours, so afternoon reads as 12pm-8pm and night as 8pm-4am without
        /// the clock needing a second representation to disagree with.
        ///
        /// Two open slots means a player CAN take two runs in a day — and doing
        /// it costs them the whole day. That is the trade the game is made of:
        /// those same two slots are the inspection, the repair and the sleep,
        /// and nothing hands them back.
        /// </summary>
        public const int FirstShiftSlot = 1;
        public static bool ShiftSlot(int slot) => slot >= FirstShiftSlot;
        public static bool ShopOpen(LifeState s) => s != null && ShiftSlot(s.slotIndex);
        /// <summary>The roster in words, for every screen that has to say it.
        /// One string so the home screen and the jobs tab cannot drift.</summary>
        public const string ShiftHours = "AFTERNOON 12PM-8PM  ·  NIGHT 8PM-4AM, SEVEN DAYS";
        /// <summary>The same roster in half the characters, for the columns
        /// that are half a screen wide. The long form is 46 characters and runs
        /// clean off a 445-unit column into whatever is beside it.</summary>
        public const string ShiftHoursShort = "AFTERNOONS + NIGHTS, SEVEN DAYS";

        /// <summary>
        /// Days off the roster allows before the absence ladder starts biting.
        ///
        /// The weekday-only rule used to hand out two free days a week and pick
        /// which two for you. Now the shop is open every day, so the allowance
        /// has to be carried explicitly — otherwise a driver is fired for taking
        /// a Tuesday off to put their car back together, which is the exact
        /// decision this whole pass exists to make interesting.
        /// </summary>
        public const int FreeDaysOff = 2;

        /// <summary>The delivery job's advertised $96/day is an AVERAGE of the
        /// tip roll below, not a salary — WorkOneDay branches on the name.</summary>
        public const string DeliveryJobName = "FOOD DELIVERY";
        /// <summary>
        /// What one drop is worth, rolled at the counter.
        ///
        /// Same shape as the menu job's tip roll — basePay is the AVERAGE night
        /// and the swing is the tips — but per DELIVERY rather than per shift,
        /// so it is scaled down to roughly a third of a day's takings. Three or
        /// four runs is a shift, which is what the activity slots allow anyway.
        ///
        /// Tiredness still counts: WorkPerformance is the same curve the desk
        /// jobs pay against, and a driver who has not slept in three days is
        /// worth less to the shop for the same reason.
        /// </summary>
        public static int RollDeliveryPay(LifeState s)
        {
            float perf = WorkPerformance(s);
            float mult = perf >= 0.8f ? 1.0f : perf >= 0.5f ? 0.9f : 0.75f;
            int basePer = Mathf.Max(8, Mathf.RoundToInt(DeliveryBasePay / 3f));
            return Mathf.Max(5, Mathf.RoundToInt(
                (basePer + Random.Range(-6, 15)) * s.payMultiplier * mult));
        }

        /// <summary>The advertised daily average for FOOD DELIVERY, kept beside
        /// the Jobs table so the two cannot drift.</summary>
        public const int DeliveryBasePay = 96;

        /// <summary>
        /// Where tonight's drop is: a RANDOM venue, rolled at the counter.
        ///
        /// The circuits stand in for streets for now, at the owner's ask
        /// ("for now just make it choose a random race track") — a delivery is
        /// a run from one end of a real route to the other, and a circuit is a
        /// real route the game already has. City deliveries come later.
        ///
        /// Charlotte is the one venue excluded, and it has to be: it has no
        /// finish line, so there would be nothing to arrive AT and the run
        /// could never end. Everything else is fair game, strips included —
        /// <see cref="DeliveryParSeconds"/> sizes the clock off the venue's own
        /// raced distance, so a quarter mile and a seven-kilometre parkway
        /// stage are both graded against what they actually take to drive.
        ///
        /// Rolled rather than rotated. The previous version stepped through the
        /// catalog by day so a player could learn the route, which is the right
        /// instinct for a race and the wrong one for a job: the whole texture of
        /// delivery work is not knowing where the next one is going.
        /// </summary>
        public static int DeliveryTrackIndex(LifeState s)
        {
            var all = TrackCatalog.All;
            int n = all.Length;
            var car = s != null ? s.ActiveCar : null;

            // Rolled from the venues this CAR CAN FINISH, not from the catalog.
            //
            // Rotating by day hid this: the parkway stage is 6.9 km with no
            // forecourt on it, so a driver who set off on a quarter tank ran dry
            // somewhere on a mountain with no pumps and no way to end the run.
            // Rolling at random turns that from a rare unlucky Tuesday into a
            // one-in-eight chance every single shift. The race menu has gated on
            // RequiredFuelPct for months; a job that dispatches you somewhere you
            // cannot reach is the same bug with a wage attached.
            int start = Random.Range(0, n);
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) % n;
                var t = all[idx];
                if (t.city) continue;
                if (car != null && car.fuel < RequiredFuelPct(t, car)) continue;
                return idx;
            }

            // Nothing in the catalog fits the tank. Send them to the cheapest
            // run there is rather than refusing the shift — the shortest drop
            // still might not fit, but it is the one that comes closest, and a
            // career whose only job silently stops existing at low fuel is the
            // trap the fallback shift was written to avoid in the first place.
            int cheapest = -1; float least = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (all[i].city) continue;
                float need = car != null ? RequiredFuelPct(all[i], car) : all[i].RaceMeters;
                if (need < least) { least = need; cheapest = i; }
            }
            return cheapest >= 0 ? cheapest : 0;
        }

        // ---- what a drop is actually worth when it arrives ----------------
        //
        // The quote at the counter is the CEILING. What the customer hands over
        // is that quote scaled by how fast the run was and by what state the box
        // is in — and past a point they simply refuse it. The owner's ask: "tip
        // is based on how quickly the track is completed. wrecked the car
        // damages the pizza and lowers tip. might even get the delivery denied."
        //
        // ONE scoring function, called from the two places that must not
        // disagree: the HUD counts the tip down live while the player drives,
        // and the apply-back pays it. A readout that promises $40 against a
        // wallet that grants $18 is worse than no readout at all.

        /// <summary>Average speed a delivery is graded against, m/s. 22 is about
        /// 79 km/h — brisk on these circuits without being a qualifying lap, so
        /// a player who drives properly and does not crash lands on par.
        /// </summary>
        public const float DeliveryParSpeed = 22f;
        /// <summary>A strip or a bridge run is a standing start and then flat
        /// out, so par there is a far higher average. Grading a quarter mile
        /// against 79 km/h would make every drag delivery a free bonus.
        /// </summary>
        public const float DeliveryParSpeedDrag = 35f;
        /// <summary>Seconds allowed for the lights, the launch and getting up to
        /// speed, on top of the distance. It matters most where the run is
        /// shortest: six seconds is 4% of a circuit and 40% of a quarter mile.
        /// </summary>
        public const float DeliveryLaunchAllowance = 6f;

        /// <summary>
        /// Biggest order the shop hands out.
        ///
        /// THE one place this number lives. The pizzeria scene builds its
        /// carried stack this tall and PizzaShift rolls the order against it; a
        /// stack built three tall against an order rolled four long is two boxes
        /// the player is paid for and never sees, and neither half would throw.
        /// </summary>
        public const int MaxOrderBoxes = 3;

        /// <summary>
        /// Tonight's order: one topping per box, bottom of the stack first.
        ///
        /// Weighted toward the small orders. Every extra box is another
        /// independent thing sliding around a car seat and the top one has
        /// nothing holding it down, so a three-box run is genuinely harder as
        /// well as worth three times as much — it should be the night you
        /// remember, not the default.
        /// </summary>
        public static int[] RollOrderToppings(int maxBoxes)
        {
            int cap = Mathf.Clamp(maxBoxes, 1, MaxOrderBoxes);
            float r = Random.value;
            int n = r < 0.52f ? 1 : r < 0.85f ? 2 : 3;
            n = Mathf.Min(n, cap);
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = Random.Range(0, PizzaCargoBakerNames.ToppingCount);
            return order;
        }

        /// <summary>Most an order can carry.</summary>
        public const int MaxOrderBottles = 2;

        /// <summary>
        /// How many two litre bottles come with the pizza.
        ///
        /// Weighted so that most orders have one and a bare few have two: a
        /// bottle is a passenger, and the whole point of it is the movement it
        /// adds to a run, not the odds of getting one. It costs nothing and
        /// earns nothing — see PizzaCargo.BuildBottle for why it is not a slot.
        /// Scaled off the box count, because nobody orders one slice and two
        /// litres of cola.
        /// </summary>
        public static int RollOrderBottles(int boxes)
        {
            float r = Random.value;
            if (boxes >= 2) return r < 0.22f ? 0 : r < 0.74f ? 1 : 2;
            return r < 0.42f ? 0 : 1;
        }

        /// <summary>Impact energy a delivery gets for free. Kerbs, rubs and a
        /// clipped wall happen on any real drive, and a job that punished the
        /// first bump would be graded on luck.</summary>
        public const float PizzaFreeDamage = 6f;
        /// <summary>Condition lost per point of impact energy past the
        /// allowance, and per discrete heavy hit. The hit term is separate
        /// because a box does not care about total energy — it cares how many
        /// times the car stopped dead.</summary>
        public const float PizzaShockPerDamage = 0.022f;
        public const float PizzaShockPerHardHit = 0.20f;
        /// <summary>At or below this the customer refuses it outright.</summary>
        public const float PizzaRuinedCondition = 0.25f;
        /// <summary>At or above this the box counts as untouched.</summary>
        public const float PizzaPerfectCondition = 0.90f;
        /// <summary>What a barely-accepted box is worth: a quarter. There is
        /// still a tip for turning up with a squashed pizza, because a driver
        /// who crashed and finished anyway did more work than one who did not,
        /// and paying $0 for anything short of perfect would turn the job into a
        /// coin flip.</summary>
        public const float PizzaWorstMult = 0.25f;
        /// <summary>Best and worst the clock alone can do to a tip. A quarter
        /// over for beating par is worth chasing; the floor is not zero, because
        /// a cold pizza is still a delivered pizza.</summary>
        public const float DeliveryFastMult = 1.25f;
        public const float DeliverySlowMult = 0.15f;

        /// <summary>How long the drop is expected to take, in seconds — the
        /// number the tip is graded against and the number the player is quoted
        /// when they pick the order up. Measured off the venue's OWN raced
        /// distance, so it means the same thing everywhere.</summary>
        public static float DeliveryParSeconds(int trackIndex)
        {
            var all = TrackCatalog.All;
            if (trackIndex < 0 || trackIndex >= all.Length) return 120f;
            var t = all[trackIndex];
            float speed = t.IsDragEvent ? DeliveryParSpeedDrag : DeliveryParSpeed;
            return DeliveryLaunchAllowance + Mathf.Max(1f, t.RaceMeters) / speed;
        }

        /// <summary>What is left of the pizza, 0-1, from the race's damage
        /// tally. Pure, so the HUD can watch it fall in real time off the live
        /// CollisionResponder while the payout recomputes it from the stamped
        /// result and gets the same answer.</summary>
        public static float PizzaCondition(float damage, int hardHits)
        {
            float shock = Mathf.Max(0f, damage - PizzaFreeDamage) * PizzaShockPerDamage
                        + Mathf.Max(0, hardHits) * PizzaShockPerHardHit;
            return Mathf.Clamp01(1f - shock);
        }

        /// <summary>The whole result of one drop.</summary>
        public struct DeliveryOutcome
        {
            /// <summary>Dollars actually handed over. Zero when refused.</summary>
            public int tip;
            /// <summary>The quote from the counter, i.e. the ceiling.</summary>
            public int quoted;
            public float parSeconds;
            public float seconds;
            /// <summary>0-1. What the box looks like when it is opened.</summary>
            public float condition;
            public float timeMult;
            public float conditionMult;
            /// <summary>The customer would not take it.</summary>
            public bool refused;
            /// <summary>True while the run is still going — the HUD asks for a
            /// running total before there is a finish time.</summary>
            public bool inProgress;
        }

        /// <summary>
        /// Score a drop. Called live by the HUD (with the clock so far) and
        /// again by the apply-back (with the finish time), so the number the
        /// player watches falling is the number that lands in the wallet.
        /// </summary>
        public static DeliveryOutcome ScoreDelivery(int quoted, int trackIndex,
                                                    float seconds, float damage,
                                                    int hardHits, bool inProgress = false,
                                                    float? cargoCondition = null,
                                                    float carryCondition = 1f)
        {
            var o = new DeliveryOutcome
            {
                quoted = Mathf.Max(0, quoted),
                parSeconds = DeliveryParSeconds(trackIndex),
                seconds = seconds,
                // The SIMULATION wins when there is one. PizzaCondition is an
                // estimate off the impact tally, written when the cargo was a
                // number; now that the boxes are objects on a seat, what
                // happened to them is not a thing to infer. The estimate stays
                // as the fallback for a scene with no cargo rig — and for the
                // self-test, which has no scene at all.
                condition = cargoCondition ?? PizzaCondition(damage, hardHits),
                inProgress = inProgress,
            };
            // The drive ACROSS TOWN counts. The order rides the passenger seat
            // from the shop to the junction before the run proper begins, and
            // the customer opens the box, not the lap chart: what arrives is
            // the worse of the two legs, never magically the better.
            o.condition = Mathf.Min(o.condition, Mathf.Clamp01(carryCondition));

            // The clock. Under par pays a premium that keeps climbing to a
            // quarter over at 0.6x par; over par it slides to the floor by the
            // time the run has taken more than twice as long as it should.
            float ratio = seconds / Mathf.Max(1f, o.parSeconds);
            o.timeMult = ratio <= 1f
                ? Mathf.Lerp(DeliveryFastMult, 1f, Mathf.InverseLerp(0.6f, 1f, ratio))
                : Mathf.Lerp(1f, DeliverySlowMult, Mathf.InverseLerp(1f, 2.2f, ratio));

            // The box. Untouched pays in full; anything the customer will still
            // accept pays at least a quarter.
            o.conditionMult = Mathf.Lerp(PizzaWorstMult, 1f,
                Mathf.InverseLerp(PizzaRuinedCondition, PizzaPerfectCondition, o.condition));

            o.refused = o.condition <= PizzaRuinedCondition;
            o.tip = o.refused ? 0
                  : Mathf.Max(0, Mathf.RoundToInt(o.quoted * o.timeMult * o.conditionMult));
            return o;
        }

        /// <summary>The box, in words, for the HUD and the result line. Same
        /// bands the multiplier uses, so what the player reads and what they are
        /// paid cannot tell different stories.</summary>
        public static string PizzaConditionLabel(float condition)
        {
            if (condition <= PizzaRuinedCondition) return "RUINED";
            if (condition < 0.5f) return "WRECKED";
            if (condition < 0.75f) return "SHAKEN";
            if (condition < PizzaPerfectCondition) return "KNOCKED ABOUT";
            return "INTACT";
        }

        /// <summary>mm:ss for a delivery clock. The race HUD has its own
        /// hundredths formatter; a tip target does not want hundredths.</summary>
        public static string DeliveryClock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int t = Mathf.RoundToInt(seconds);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

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

        /// <summary>
        /// Turning up: the day's attendance latch and the end of the absence
        /// ladder. Called when the player CLOCKS ON, not when they arrive.
        ///
        /// The distinction is the entire night shift. A shift taken in the last
        /// slot rolls the day the moment it is taken, and the rollover reads
        /// workedToday to decide whether the player skived — so crediting the
        /// shift on arrival credited it to TOMORROW and booked an absence for
        /// the night the player actually worked. A driver who has picked an
        /// order up and driven off has turned up, whatever becomes of the pizza.
        ///
        /// Idempotent within a day, because both open slots can be worked and
        /// attendance counts DAYS. Two runs on a Tuesday is one Tuesday.
        /// </summary>
        public static void ClockOnShift(LifeState s)
        {
            if (s == null) return;
            if (!s.workedToday)
            {
                s.workDaysTotal++;
                s.workDaysPresent++;
                s.workedToday = true;
            }
            s.consecutiveAbsences = 0;
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

            // FOOD DELIVERY is tips, not salary: RG2 paid it $0 + $2-10 a drop.
            // basePay stands in for the average night, the roll swings around
            // it, and the perk is a meal eaten on shift — junk, because it is
            // pizza out of the bag between runs, and the rollover's opinion of
            // junk is the correct long-run opinion of that diet.
            bool delivery = s.playerJob == DeliveryJobName;
            if (delivery)
            {
                earned = Mathf.RoundToInt((s.basePay + Random.Range(-34, 46)) *
                                          s.payMultiplier * mult);
                s.ateToday = true;
                s.daysSinceEat = 0;
                s.lastMealTier = "junk";
            }

            s.pendingSalary += earned;
            s.workRep = Mathf.Clamp(s.workRep + rep, 0f, 100f);
            ClockOnShift(s);
            string meal = delivery ? "  Ate on shift." : "";
            return perf >= 0.8f ? "A solid shift. +$" + earned + meal
                 : perf >= 0.5f ? "A rough shift (tired). +$" + earned + meal
                 : "You could barely function. +$" + earned + meal;
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

        // Race wear (fast-travel factors converted to meters, x the H78
        // mileage ramp). RaceWearScale is THE balance knob: 1.0 = a race wears
        // exactly what the same miles wear in RG2 (new tires every ~4-5
        // races). Tune only with playtest evidence.
        //
        // Fuel is NOT here. It used to be — one flat FuelPctPerMeter for every
        // car in the game — and that is exactly what made a 7 km parkway stage
        // cost more than a full tank. It lives in FuelModel now, per car, off
        // the tank size and the MPG the HTML game always used.
        public const float TireWearPerM = 0.0062746f;
        public const float EngineWearPerM = 0.0031373f;
        public const float PaintWearPerM = 0.00062746f;
        public const float RaceWearScale = 1.0f;

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

        /// <summary>Fill this tank without driving to a pump. The fuel itself
        /// is priced off the car's own tank, so a supercar's rescue costs what
        /// a supercar's tank costs; only the fee is flat.</summary>
        public static int CallOutRefuelCost(float fuelPct, FuelProfile fuel) =>
            FuelCallOutFee + fuel.CostToFill(fuelPct);

        public static int CallOutRefuelCost(OwnedCar car) =>
            car == null ? FuelCallOutFee
                        : CallOutRefuelCost(car.fuel, FuelProfile.For(car));

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
        public static float RequiredFuelPct(TrackCatalog.TrackDef track, OwnedCar car)
        {
            if (track == null) return 0f;
            if (!track.hasFuelStop) return RaceFuelBurnPct(track.RaceMeters, car);
            return RaceFuelBurnPct(track.LengthM * FuelStopLapFraction * 1.5f, car);
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

        /// <summary>What a car of THIS spec spends covering that distance at a
        /// racing pace — the estimate behind the pre-race gate and the menu's
        /// warnings. The live tank out on track measures the same burn against
        /// the actual needle instead of assuming an average.</summary>
        public static float RaceFuelBurnPct(float meters, OwnedCar car) =>
            FuelProfile.For(car).Burn(meters, FuelModel.RacePaceLoad);

        /// <summary>Pump price to fill this car's tank from where it is.</summary>
        public static int RefuelCost(OwnedCar car) =>
            car == null ? 0 : FuelProfile.For(car).CostToFill(car.fuel);

        /// <summary>Bank a finished race — the apply-back contract, in order:
        /// slot, odometer, fuel, wear, fault rolls, payout+rep, log.</summary>
        public static string ApplyRaceResult(LifeState s)
        {
            if (!RaceHandoff.ResultReady) return null;

            float meters = RaceHandoff.MetersDriven;
            var car = s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar;

            // 1. the race consumed a slot (may roll the day)
            //
            // EXCEPT a delivery, which already paid for its slot at the counter.
            // PizzaShift spends it the moment the player drives off, on purpose:
            // the shift costs the evening whether or not the box arrives. Doing
            // it again here charged a single drop TWO of the three slots in a
            // day, so one afternoon run ate the afternoon, the night and any
            // chance of sleeping — most of the day the inspect-repair-work-sleep
            // decision is supposed to be spent making.
            // ... and NOT a commute leg either: the drive to the shop and the
            // loaded drive to the junction are halves of the shift, and the
            // shift's slot is spent once, at the shop door. Charging each leg
            // as well made one delivery cost most of a day.
            if (!RaceHandoff.Delivery && !RaceHandoff.CommuteLeg) SpendActivitySlot(s);

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
                    car.fuel = Mathf.Max(0f, car.fuel - RaceFuelBurnPct(meters, car) * RaceHandoff.FuelMult);

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

                // 4c. NO DRIVING RECORD. There used to be an at-fault incident
                // tally here, feeding a BILLS row and an insurance multiplier
                // that was never built. Cut at the owner's ask: "the player is
                // punished enough by repairing damages to their car." Counting
                // the same crash twice — once in panel damage you pay to undo,
                // once on a permanent record you cannot — is one punishment
                // with two invoices.

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
                summary = (RaceHandoff.CommuteLeg ? "on the clock — " : "free roam — ") +
                          (RaceHandoff.MetersDriven / 1000f).ToString("0.0") +
                          " km in " + (string.IsNullOrEmpty(RaceHandoff.FreeRoamPlace)
                              ? "Charlotte" : RaceHandoff.FreeRoamPlace.ToLowerInvariant());
            }
            else if (RaceHandoff.Delivery)
            {
                // ARRIVED. The finish line is the customer's door, so crossing
                // it is the whole job — there is nobody to beat and no position
                // to place in. But arriving is not the same as arriving WELL:
                // the quote at the counter is a ceiling, and what is actually
                // handed over is that quote graded on the clock and on the state
                // of the box. ScoreDelivery is the same call the HUD has been
                // counting down all run, so the number the player watched fall
                // is the number that lands here.
                //
                // Paid straight into the wallet rather than into pendingSalary:
                // a delivery driver is tipped in cash at the door, and waiting
                // until Friday for it would make the one job you actually drive
                // the one job you cannot feel. The shift ALSO counts as the
                // day's work, and the meal comes with it — same perk the menu
                // version has always granted, for the same reason.
                var drop = ScoreDelivery(RaceHandoff.DeliveryPay, RaceHandoff.TrackIndex,
                                         RaceHandoff.RaceTimeSeconds,
                                         RaceHandoff.DamageScore, RaceHandoff.HardHits,
                                         cargoCondition: RaceHandoff.CargoReported
                                             ? RaceHandoff.CargoCondition : (float?)null,
                                         carryCondition: RaceHandoff.CarryCondition);
                s.money += drop.tip;
                // Attendance was banked at the counter (ClockOnShift, from
                // PizzaShift) — turning up is what the shop counts, and a night
                // run has already rolled the day by the time this runs. What is
                // still owed here is what the DROP was worth.

                // The shop hears about a refused order. A turned-away box costs
                // standing rather than money — the money is already gone — and
                // it is the only way the job can go backwards, which is what
                // makes driving carefully worth anything.
                s.workRep = Mathf.Clamp(s.workRep + (drop.refused ? -3f : 1f), 0f, 100f);
                // You ate either way, and when it is refused you ate THIS one.
                // Leaving the meal off a failed run would mean a crash cost the
                // tip and the dinner, and starve a player for driving badly.
                s.ateToday = true;
                s.daysSinceEat = 0;
                s.lastMealTier = "junk";
                s.lastAnyRaceDay = s.day;   // rep-decay clock: you were out driving
                // Short enough for the toast, which is 760 px of ONE line and
                // already carries a "RACE RESULT: " prefix. The venue is left
                // out on purpose: the player has just driven it.
                summary = drop.refused
                    ? "REFUSED — the box was a write-off. No tip; you ate it."
                    : "delivered — " + DeliveryClock(drop.seconds) +
                      " (par " + DeliveryClock(drop.parSeconds) + "), box " +
                      PizzaConditionLabel(drop.condition).ToLower() + ", +" +
                      MenuKit.Money(drop.tip);
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
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": race " + summary);
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
            if (f.hidden)
            {
                // The car is genuinely worse now and the player will feel it,
                // but nobody has looked at it. Naming the part here would hand
                // over the answer an inspection exists to find — so the log and
                // the result screen report the SYMPTOM, which is all a driver
                // gets from the seat.
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + car.displayName +
                                  " is not running right");
                lastSymptom = SymptomFor(f.stat);
                return;
            }
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": DIAGNOSED — " + f.label +
                              " ($" + f.cost + ")");
            lastDiagnosed = f.label;
        }

        /// <summary>What a fault in this lane feels like from the driver's seat.
        /// Deliberately vague about the part and specific about the sensation:
        /// it should send the player to INSPECT, not stand in for it.</summary>
        static string SymptomFor(string stat)
        {
            switch (stat)
            {
                case "tires": return "the car does not want to hold a line";
                case "hp": return "something is loose in the bodywork";
                case "paint": return "the paint has taken a knock";
                default: return "the engine is down on song";
            }
        }

        /// <summary>Set by the last apply-back so the result screen can show a
        /// "DIAGNOSED:" line. Read once, then cleared. Only faults somebody has
        /// actually diagnosed reach it.</summary>
        public static string lastDiagnosed;

        /// <summary>Set instead of <see cref="lastDiagnosed"/> when the race
        /// left the car with a fault nobody has found yet. Read once, then
        /// cleared.</summary>
        public static string lastSymptom;

        // ================= repairs (repairCost.ts / pendingParts.ts) =================
        // Crash damage: DamageScore is roughly summed closing speed in m/s, so a
        // firm 8 m/s hit costs ~5 body. Deliberately cheaper than wear per race —
        // crashing should sting, not end a career in one mistake.
        public const float BodyDamagePerHit = 0.65f;
        public const float PaintDamagePerHit = 0.4f;
        public const float ImpactFaultThreshold = 22f;

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
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": fixed " + f.label +
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
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": booked " + f.label +
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
                        s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + p.label + " installed");
                    }
                    else
                    {
                        AddToStat(car, p.stat, p.add);
                        car.faults.RemoveAll(x => x.id == p.faultId);
                        s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + p.label + " repaired");
                    }

                    // A used part off the salvage yard can bring something with
                    // it, and this is where it finds out — the day it goes ON,
                    // not the day it was paid for. Seeded HIDDEN through the
                    // same door every other fault comes in by, so the car goes
                    // off song and an inspection is what names it: the yard
                    // feeds the inspection layer rather than routing round it.
                    if (p.junkRisk > 0 && Random.Range(0, 100) < p.junkRisk)
                    {
                        var spec = CarCatalog.Get(car.specId);
                        AddFault(s, car, FaultCatalog.RollWearFault(
                            car, p.stat, false, "wear", spec != null ? spec.origin : "jpn"));
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
            // new tyres make a booked tyre job pointless. Two kinds of job are
            // explicitly spared.
            //
            // Upgrade builds: they are filed under the "engine" stat for want
            // of a better lane, and an oil change must not cancel a paid-for
            // turbo build.
            //
            // And salvage-yard parts, for a sharper version of the same reason.
            // A booked repair is an APPOINTMENT — cancelling it when the work
            // is no longer needed is doing the player a favour. A yard part is
            // a PART, bought, paid for and sitting in the boot; sweeping it
            // means a $50 oil change silently voiding a $900 donor engine on
            // its way in, with nothing on screen to say where the money went.
            s.pendingParts.RemoveAll(p => p.carId == car.id && p.stat == svc.stat &&
                                          !p.IsUpgrade && !p.IsYardPart);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + svc.name + " " + MenuKit.Money(price));
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

        /// <summary>
        /// A car's condition in words, because a percentage is not something a
        /// driver can see.
        ///
        /// The bars used to print "84%" beside them and that was the game
        /// reading its own save out loud. Nobody looks at an engine and knows it
        /// is at eighty-four; they know it sounds fine, or that something is
        /// off. The exact number is still there under DEBUG, where it is a
        /// developer's readout and not a pretence.
        ///
        /// Bands line up with the bar's own colours (60 and 30 are where it
        /// turns amber and red), so the word and the colour cannot disagree —
        /// and with FaultCatalog's threshold rolls at 40 and 15, so "WORN" is
        /// genuinely the band where things start going wrong.
        /// </summary>
        public static string ConditionLabel(float pct) => ConditionNames[ConditionBand(pct)];

        /// <summary>
        /// The same five bands as an index, 0 for SHOT up to 4 for MINT.
        ///
        /// It exists because the WORD was not the only thing printing the exact
        /// number. The bar beside it filled to value/100, so a player who could
        /// not read "84%" could still read a bar that was fourteen twenty-fifths
        /// of the way along and count pixels — which is the same readout with an
        /// extra step, and defeats the point of hiding the figure at all. The
        /// bar draws five segments off this instead, so the picture says exactly
        /// what the word says and no more.
        /// </summary>
        public static int ConditionBand(float pct) =>
            pct >= 90f ? 4 : pct >= 60f ? 3 : pct >= 30f ? 2 : pct >= 12f ? 1 : 0;

        /// <summary>Indexed by <see cref="ConditionBand"/>.</summary>
        public static readonly string[] ConditionNames =
            { "SHOT", "ROUGH", "WORN", "GOOD", "MINT" };

        // ================= housing & bills (billsCalc.ts / insurance.ts) =================
        // The ladder is HOUSES now, not apartments: the player starts in a
        // small rented house with a one-car garage (the same house the walk-in
        // scene builds), and the slots column is the garage that comes with
        // each rung. Rents kept exactly where the apartment ladder had them so
        // the economy does not move. Old saves are renamed onto these keys by
        // LifeSimManager.Migrate (v6).
        public static readonly (string key, string label, int rent, int slots)[] Housing =
        {
            ("house1g", "SMALL HOUSE — 1-CAR GARAGE", 425, 1),
            ("house2g", "BRICK HOUSE — 2-CAR GARAGE", 575, 2),
            ("house3g", "SUBURBAN HOUSE — 3-CAR GARAGE", 750, 3),
        };

        /// <summary>Friendly name for a housing key; falls back to the raw key
        /// so an unknown save value never renders as an empty bill line.</summary>
        public static string HousingLabel(string key)
        {
            foreach (var h in Housing) if (h.key == key) return h.label;
            return string.IsNullOrEmpty(key) ? "HOUSING" : key.ToUpperInvariant();
        }
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
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": bills paid — " + MenuKit.Money(due));
            }
            else
            {
                s.missedPayments++;
                s.creditScore = Mathf.Max(300, s.creditScore - 40);  // missed (−40)
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": MISSED BILLS (" + MenuKit.Money(due) +
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

        /// <summary>
        /// Sleep: eight hours, one block of the day.
        ///
        /// It used to be "sleep until tomorrow" and it threw away everything
        /// left of the day with it, which made the slot machine a
        /// one-decision affair: a morning you did not want to spend cost you
        /// the afternoon and the night as well. Now the button moves the clock
        /// one band - MORNING to AFTERNOON to NIGHT - and it is the NIGHT
        /// sleep, and only that one, that turns the calendar over.
        ///
        /// Which is also why only the night sleep passes sleptTonight. The
        /// health model's rested/all-nighter ladder is about what you did with
        /// the dark hours; a nap in the afternoon is not an answer to it. A nap
        /// likewise does NOT count towards slotsActiveToday, because rest is
        /// not activity - it costs the slot and nothing else.
        /// </summary>
        public static void Sleep(LifeState s)
        {
            if (s == null) return;
            if (s.slotIndex >= SlotNames.Length - 1)
            {
                s.health = Mathf.Min(100f, s.health + 5f);
                Rollover(s, sleptTonight: true);
                return;
            }
            // A nap restores NOTHING. It was a token point per nap for about
            // an hour, until the self-test caught what that is: two free points
            // a day, every day, against a starvation ladder that takes twelve -
            // which turned "twenty days without food is fatal-grade" into a
            // driver sitting at 7 health and stable. Health is settled once a
            // day, at the rollover, off what you ate and whether you slept at
            // night; an afternoon lie-in is not an answer to either question.
            s.slotIndex++;
        }

        /// <summary>
        /// Sleep through whatever is left of today and wake up tomorrow
        /// morning - which is what Sleep itself used to do.
        ///
        /// Kept under its own name for the callers that genuinely mean "a day
        /// passes" (the absence ladder, parts that arrive on a promised day,
        /// every test that ages a career) rather than "eight hours pass".
        /// Letting those keep calling Sleep is what would silently divide the
        /// day count of every one of them by three.
        /// </summary>
        public static void SleepUntilMorning(LifeState s)
        {
            if (s == null) return;
            int guard = 0;
            do { Sleep(s); } while (s.slotIndex != 0 && ++guard < SlotNames.Length);
        }

        /// <summary>The single day-rollover pipeline, in the gameLoop's order:
        /// absence check → health → rep decay → payday → bills → latch reset.
        /// Everything funnels through here so nothing double-fires.</summary>
        static void Rollover(LifeState s, bool sleptTonight)
        {
            // 1. no-show: employed, the shop was open, no run taken
            //    (noShowAbsence.ts ladder, re-cut for the delivery roster).
            //
            // The shop is open seven days now, so "did not work today" has
            // stopped meaning "skived": it is also the day the gearbox came out.
            // The ladder therefore counts consecutive days off and only starts
            // charging PAST the allowance — two free days, the same two the old
            // weekend handed out, except the player chooses which two they are.
            if (!string.IsNullOrEmpty(s.playerJob) && !s.workedToday)
            {
                s.consecutiveAbsences++;
                int over = s.consecutiveAbsences - FreeDaysOff;
                if (over > 0)
                {
                    float loss = over switch { 1 => 5f, 2 => 15f, _ => 30f };
                    s.workRep = Mathf.Max(0f, s.workRep - loss);
                    s.workDaysTotal++;
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": missed a shift (−" + loss + " rep)");
                    if (over >= 3 || s.workRep <= 0f)
                    {
                        s.calendarLog.Add(LifeRules.LogDate(s.day) + ": FIRED from " + s.playerJob);
                        s.playerJob = ""; s.basePay = 0; s.fired = true;
                        s.creditScore = Mathf.Max(300, s.creditScore - 25);
                    }
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
                s.calendarLog.Add(LifeRules.LogDate((s.day - 1)) + ": PAYDAY +" + MenuKit.Money(net));
                s.pendingSalary = 0;
            }

            // 6. bills on the 1st
            if (DayOfMonth(s.day) == 1) FireMonthlyBills(s);

            // 7. repairs whose day has come
            TickPendingParts(s);

            // 8. the market turns over: listings expire and refill, and any car
            // the player has advertised may draw an offer. The salvage yard
            // turns over beside it, on its own three clocks.
            CarMarket.RefreshListings(s);
            CarMarket.GenerateOffers(s);
            CarMarket.RefreshLot(s);
            Junkyard.RefreshStock(s);
            // A visit outlives the advert it was about unless something reaps
            // it — and a visit carries a whole phantom car, so an unswept one
            // is a car the save keeps for the rest of the career.
            Viewings.Sweep(s);

            // 9. the ladder: expired call-outs go cold, and a gate that has just
            // cleared pages the player. Order matters — pruning first stops a
            // page written this morning being swept the same morning.
            s.mail.RemoveAll(m => m.expiresDay > 0 && s.day > m.expiresDay);
            lastPage = Blacklist.TickPager(s);

            // 9b. the diary: yesterday's booking, if it went unraced, is gone.
            // Swept AFTER the day advances so a booking is live for the whole of
            // its own day and stale the moment that day is over. Logged rather
            // than punished — see BookingOn: the slot the player spent on
            // something else was the cost.
            if (s.bookings != null && s.bookings.Count > 0)
            {
                var missed = s.bookings.FindAll(b => b == null || b.day < s.day);
                foreach (var b in missed)
                    if (b != null)
                        s.calendarLog.Add(LifeRules.LogDate(b.day) + ": missed the booked race at " +
                                          TrackCatalog.At(b.trackIndex).name);
                s.bookings.RemoveAll(b => b == null || b.day < s.day);
            }

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

        /// <summary>
        /// The job a career starts in.
        ///
        /// FOOD DELIVERY, on the owner's instruction — "I want the player's
        /// default job (for now) to be pizza delivery" — because it is the only
        /// job you can actually DRIVE, and a new player should meet the game
        /// through the part of it that is a game rather than through a button
        /// that adds money.
        /// </summary>
        public static int DefaultJobIndex
        {
            get
            {
                for (int i = 0; i < Jobs.Length; i++)
                    if (Jobs[i].name == DeliveryJobName) return i;
                return 0;
            }
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
                housingType = "house1g",
                monthlyHousingCost = 425,
                foodStock = 4,
                lastMealTier = "regular",
            };
            s.creditScore = StartingCredit(s.age, s.money, job.name);
            // Same switch the in-career button uses, so the two entry points
            // cannot drift into granting different things.
            if (debug) EnableDebug(s);
            s.calendarLog.Add(LogDate(1) + ": moved in. " + job.name + ", " +
                              MenuKit.Money(s.money) + " saved." +
                              (debug ? "  [DEBUG CAREER]" : ""));
            CarMarket.RefreshListings(s);
            CarMarket.RefreshLot(s);
            Junkyard.RefreshStock(s);
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
                jobName == "AUTO PARTS RUN" ? 10 :
                jobName == DeliveryJobName ? 5 : 0;
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
