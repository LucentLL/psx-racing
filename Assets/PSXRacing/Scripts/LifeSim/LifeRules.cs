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
        /// <summary>The three blocks of a day, in the owner's words: MORNING,
        /// DAY, NIGHT (2026-09-17: "broken into three chunks (morning 4:00-
        /// 12:00, day 12:00-20:00, night 20:00-4:00)"). The middle one was
        /// AFTERNOON; renamed here rather than given a second name for the
        /// calendar, because a header that says AFTERNOON over a day view
        /// that says DAY is two clocks.</summary>
        public static readonly string[] SlotNames = { "MORNING", "DAY", "NIGHT" };
        /// <summary>The hours each block covers, for the day and week views.
        /// Display only — the clock itself is the three slots, and
        /// <see cref="TimeOfDay.ForSlot"/> picks an hour inside each band.</summary>
        public static readonly string[] SlotHours = { "4:00 – 12:00", "12:00 – 20:00", "20:00 – 4:00" };
        public const int MorningSlot = 0, DaySlot = 1, NightSlot = 2;

        // ---- the week, as a calendar draws it ----
        // The game's own week is FRI-first (Dow, above): day 1 is a Friday
        // and payday is a Friday, and every rule reads Dow. A CALENDAR is a
        // different thing — the owner's own reference for the week view is a
        // work planner that starts on SUNDAY, which is also how a 1999 North
        // Carolina kitchen calendar reads — so the week and month grids start
        // on Sunday and convert. Nothing but the two grids uses these.
        public static readonly string[] WeekDayNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
        /// <summary>Column of a day in a Sunday-first week: SUN 0 .. SAT 6.</summary>
        public static int WeekCol(int day) => (Dow(day) + 5) % 7;
        /// <summary>The Sunday a day's week starts on. Can be BEFORE day 1
        /// for the first week of a career; a grid draws those cells blank.</summary>
        public static int WeekStart(int day) => day - WeekCol(day);
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

        /// <summary>The booking in ONE block of a day, or null. The day view
        /// draws a race in the block it was written into and offers the RACE
        /// button only when the clock is standing in that block.</summary>
        public static RaceBooking BookingAt(LifeState s, int day, int slot)
        {
            var b = BookingOn(s, day);
            return b != null && b.slot == slot ? b : null;
        }

        /// <summary>Book onto the night, which is what a booking meant before
        /// blocks existed and what a street race is anyway.</summary>
        public static bool Book(LifeState s, int day, int trackIndex, bool practice) =>
            Book(s, day, NightSlot, trackIndex, practice);

        /// <summary>One race a day, because a race costs a slot and there are
        /// three of those — a day with two bookings on it is a day the player
        /// has already lost by lunchtime. A block already behind the clock is
        /// refused for the same reason yesterday is: it is not a plan.</summary>
        public static bool Book(LifeState s, int day, int slot, int trackIndex, bool practice) =>
            Book(s, day, slot, trackIndex, practice, -1);

        /// <param name="hour">The <see cref="TimeOfDay"/> index the player
        /// picked, or -1 for the block's own. An hour outside the block is
        /// dropped to -1 rather than refused: the booking is still a plan,
        /// and a plan at the block's own hour beats no plan.</param>
        public static bool Book(LifeState s, int day, int slot, int trackIndex, bool practice, int hour)
        {
            if (!CanBookAt(s, day, slot)) return false;
            if (s.bookings == null) s.bookings = new System.Collections.Generic.List<RaceBooking>();
            slot = Mathf.Clamp(slot, 0, SlotNames.Length - 1);
            s.bookings.Add(new RaceBooking
            {
                day = day, slot = slot,
                trackIndex = trackIndex, practice = practice,
                hourPick = TimeOfDay.InSlot(hour, slot) ? hour + 1 : 0,
            });
            return true;
        }

        /// <summary>
        /// The hour a booked race runs at: the one written into the diary
        /// with it, or the block's own when none was (every booking made
        /// before the picker existed, and any whose hour no longer sits in
        /// its block). The ONE reader of <see cref="RaceBooking.hourPick"/>.
        /// </summary>
        public static int BookingHour(RaceBooking b)
        {
            if (b == null) return TimeOfDay.Sunset;
            int hour = b.hourPick - 1;
            return TimeOfDay.InSlot(hour, b.slot) ? hour : TimeOfDay.ForSlot(b.slot, b.day);
        }

        /// <summary>Whether a NEW booking could be written into this block:
        /// not behind the clock, inside the horizon, and on a day with no
        /// race on it yet. The planner asks this to decide whether to draw
        /// WRITE IT IN at all.</summary>
        public static bool CanBookAt(LifeState s, int day, int slot)
        {
            if (s == null || day < s.day) return false;
            if (day == s.day && slot < s.slotIndex) return false;
            if (day > s.day + BookingHorizonDays) return false;
            return BookingOn(s, day) == null;
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
        /// between 12:30 and 19:10 and slot 2 at 23:15, so afternoon reads as
        /// 12pm-8pm and night as 8pm-4am without the clock needing a second
        /// representation to disagree with.
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
        public const string ShiftHours = "DAY 12PM-8PM  ·  NIGHT 8PM-4AM, SEVEN DAYS";
        /// <summary>The same roster in half the characters, for the columns
        /// that are half a screen wide. The long form is 46 characters and runs
        /// clean off a 445-unit column into whatever is beside it.</summary>
        public const string ShiftHoursShort = "DAY + NIGHT SHIFTS, SEVEN DAYS";

        // ================= what a block was spent on =================
        // The calendar is the front door now, and a calendar that only knows
        // the FUTURE is half a calendar: the owner asked for it to show the
        // shifts, with the current block highlighted, so that "choosing to
        // sleep, race, or work on the car would skip a shift" is something
        // you can SEE. That needs the day to remember what each block went
        // on. One word per block, stamped by the two things that move the
        // clock (SpendActivitySlot and Sleep), copied into dayLog when the
        // day closes.
        public const string ActSleep = "SLEEP";
        public const string ActWork = "WORK";
        public const string ActRace = "RACE";
        public const string ActDrive = "DRIVE";
        public const string ActInspect = "INSPECT";
        public const string ActViewing = "VIEWING";
        /// <summary>A night at the car meet: the drive into town that ended
        /// with somebody being raced. See <see cref="CarMeets"/>.</summary>
        public const string ActMeet = "MEET";
        /// <summary>A slot spent by a caller that did not say on what. The
        /// tests and one or two errands; never blank, because blank means
        /// "not reached yet".</summary>
        public const string ActErrand = "BUSY";
        /// <summary>How many closed days the save keeps. Six weeks covers any
        /// month view and the week either side of it.</summary>
        public const int DayLogKeep = 42;

        /// <summary>What a block's word reads as on the calendar, past tense.</summary>
        public static string ActLabel(string act)
        {
            switch (act)
            {
                case ActSleep: return "SLEPT";
                case ActWork: return "WORKED";
                case ActRace: return "RACED";
                case ActDrive: return "DROVE";
                case ActInspect: return "INSPECTED";
                case ActViewing: return "VIEWED A CAR";
                case ActMeet: return "AT THE MEET";
                case ActErrand: return "BUSY";
                default: return "";
            }
        }

        /// <summary>The save's slotActs, guaranteed three entries. A v12 save
        /// has none in its JSON (the initializer stands in), and a truncated
        /// list would index out of range on the first sleep.</summary>
        public static System.Collections.Generic.List<string> SlotActs(LifeState s)
        {
            if (s.slotActs == null) s.slotActs = new System.Collections.Generic.List<string>();
            while (s.slotActs.Count < SlotNames.Length) s.slotActs.Add("");
            return s.slotActs;
        }

        static void RecordAct(LifeState s, int slot, string what)
        {
            if (s == null) return;
            var acts = SlotActs(s);
            slot = Mathf.Clamp(slot, 0, SlotNames.Length - 1);
            acts[slot] = string.IsNullOrEmpty(what) ? ActErrand : what;
        }

        /// <summary>What a block of ANY day was spent on: today's from
        /// slotActs, a closed day's from dayLog, and empty for the future,
        /// for a block not reached yet, and for a day older than the log.</summary>
        public static string SlotAct(LifeState s, int day, int slot)
        {
            if (s == null) return "";
            slot = Mathf.Clamp(slot, 0, SlotNames.Length - 1);
            if (day == s.day) return SlotActs(s)[slot] ?? "";
            if (day > s.day || s.dayLog == null) return "";
            var rec = s.dayLog.Find(r => r != null && r.day == day);
            return rec != null ? (rec.Act(slot) ?? "") : "";
        }

        /// <summary>A shift block that went on something other than the
        /// shift. Only ever true of a block that HAS been spent — the block
        /// the clock is standing in is not skipped yet, it is being decided —
        /// and only while there is a job to skip.</summary>
        public static bool ShiftSkipped(LifeState s, int day, int slot)
        {
            if (s == null || string.IsNullOrEmpty(s.playerJob) || !ShiftSlot(slot)) return false;
            string act = SlotAct(s, day, slot);
            return act.Length > 0 && act != ActWork;
        }

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

        /// <summary>What a missed day past the allowance costs: one typical
        /// day's tips, the same figure the jobs page quotes as "a day". Off the
        /// pay pending for Friday, and never more than is pending.</summary>
        public static int MissedDayDock(LifeState s) =>
            s == null ? 0 : Mathf.Max(0, Mathf.RoundToInt(s.basePay * s.payMultiplier));

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
        /// Charlotte is excluded, and it has to be: it has no finish line, so
        /// there would be nothing to arrive AT and the run could never end.
        /// The two SYNTHETIC strips are excluded too, "for logic": a quarter
        /// mile of flat tarmac with a Christmas tree at one end is not a road
        /// anybody lives on, and a delivery that starts from a burnout box
        /// reads as the game not knowing what a delivery is. The Bogue Banks
        /// bridges stay — drag PRESENTATION on a real road is still a real
        /// road with a house at the far end of it. Everything else is fair
        /// game: <see cref="DeliveryParSeconds"/> sizes the clock off the
        /// venue's own raced distance, so a bridge and a seven-kilometre
        /// parkway stage are both graded against what they take to drive.
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
                if (t.IsRoam || t.drag) continue;
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
                // The SAME filter as the roll above. A fallback that admits
                // the strips picks the eighth mile every time — it is the
                // cheapest run in the catalog by a mile — and the venue the
                // roll refuses on principle would be the one a dry tank
                // always gets.
                if (all[i].IsRoam || all[i].drag) continue;
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
        ///
        /// Deliberately NOT lowered when deliveries went rolling: with no
        /// lights to wait for, the six seconds is now pure slack between the
        /// venue's limit and the 79 km/h par pace, which is about what a car
        /// takes to get there from 45 while the driver finds the road. Moving
        /// it moves every quote at the counter, and the economy was balanced
        /// on this number — a change here is a tuning decision, not a tidy-up.
        /// </summary>
        public const float DeliveryLaunchAllowance = 6f;

        /// <summary>
        /// How far round a circuit the customer's door can be, as a fraction
        /// of the lap. A delivery is a SPRINT, not a lap race ("pizza delivery
        /// routes should be sprints, not circuit races"): the run starts at
        /// the line and ends part-way round, and the fraction is rolled at the
        /// counter with the venue so the par quoted there is the par driven.
        ///
        /// The band: never less than half a lap, so a drop is a drive and not
        /// a dash round the first corner, and never a whole one, so the finish
        /// can never coincide with the start line — RaceManager detects a lap
        /// by an index window either side of waypoint 0, and a finish inside
        /// that window would be a lap and a finish on the same frame.
        /// Starting values for tuning.
        /// </summary>
        public const float DeliveryDropMin = 0.55f;
        public const float DeliveryDropMax = 0.95f;

        /// <summary>Where this drop's door is, rolled once at the counter.</summary>
        public static float RollDropFraction() => Random.Range(DeliveryDropMin, DeliveryDropMax);

        /// <summary>
        /// How far a delivery actually drives, in metres — THE distance the
        /// par is sized to and the finish is placed at. On a route with ENDS
        /// (a strip, a stage, a bridge) that is the baked run; on a circuit it
        /// is the drop fraction of a lap. The fraction is clamped into the
        /// band so a ticket carrying nothing (an old roll, a debug launch)
        /// grades as a full lap rather than as a zero-metre drop.
        /// </summary>
        public static float DeliveryMeters(TrackCatalog.TrackDef t, float dropFraction)
        {
            if (t == null) return 0f;
            // A LOOP stage (the 277 belt) has no ends: it is a lap, and the
            // door is a fraction of it like any circuit's.
            if (t.drag || (t.stage && !t.loop) || (t.IsCityRace && !t.loop)) return t.RaceMeters;
            return t.LengthM * Mathf.Clamp(dropFraction, DeliveryDropMin, 1f);
        }

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
        /// when they pick the order up. Measured off the distance the drop
        /// ACTUALLY covers (<see cref="DeliveryMeters"/>), so it means the same
        /// thing everywhere: a sprint round 70% of a lap is quoted as 70% of a
        /// lap, and a stage as the stage.</summary>
        /// <param name="dropFraction">The ticket's drop fraction. Defaults to
        /// ONE LAP — which is what RaceManager races for a fraction of 1 — so
        /// a caller with no ticket (a self-test, a debug launch) is graded
        /// against the sprint it would actually drive. Before the sprint the
        /// par was the whole multi-lap race; no delivery is one any more.</param>
        public static float DeliveryParSeconds(int trackIndex, float dropFraction = 1f)
        {
            var all = TrackCatalog.All;
            if (trackIndex < 0 || trackIndex >= all.Length) return 120f;
            var t = all[trackIndex];
            float speed = t.IsDragEvent ? DeliveryParSpeedDrag : DeliveryParSpeed;
            return DeliveryLaunchAllowance + Mathf.Max(1f, DeliveryMeters(t, dropFraction)) / speed;
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
        /// <param name="dropFraction">How far round the lap the door is. The
        /// HUD and the apply-back both pass RaceHandoff.DeliveryDropFraction —
        /// they are the two callers that must never disagree, and a par sized
        /// to a lap in one and to the sprint in the other would be a tip that
        /// changes on the results screen.</param>
        /// <param name="hitSomething">Whether the car hit anything while the
        /// order was aboard, on either leg. An order can only be REFUSED if it
        /// did — see the rule in the body. Defaults to true, which is the old
        /// behaviour and the right reading of a caller that does not know: the
        /// damage-tally fallback only ever falls BECAUSE of impacts, so with
        /// no simulation a ruined box already means one.</param>
        public static DeliveryOutcome ScoreDelivery(int quoted, int trackIndex,
                                                    float seconds, float damage,
                                                    int hardHits, bool inProgress = false,
                                                    float? cargoCondition = null,
                                                    float carryCondition = 1f,
                                                    float dropFraction = 1f,
                                                    bool hitSomething = true)
        {
            var o = new DeliveryOutcome
            {
                quoted = Mathf.Max(0, quoted),
                parSeconds = DeliveryParSeconds(trackIndex, dropFraction),
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

            // "ZERO TIP SHOULD BE RESERVED FOR A LATE DELIVERY OR DAMAGED
            // DELIVERY." The owner, 2026-09-18, of an order refused on a run
            // that hit nothing and came in 2:15 under par.
            //
            // It used to be the condition alone, and the condition is a
            // physics rig's opinion. Two things in that rig were wrong — wear
            // that summed without limit, and a floor test that fired on a box
            // merely overhanging the seat — and both are fixed in PizzaCargo.
            // But the harness case written to prove it (forty corners, nothing
            // touched) ended with every box on the seat in one build and two
            // in the footwell in the next, walked off the front a braking zone
            // at a time. That rig is deterministic for a build and chaotic
            // across them, and there will be a fourth way for it to ruin an
            // order nobody crashed. So the rule is kept HERE, where it is one
            // line and cannot drift: the customer sends a ruined box away only
            // if the car hit something while it was aboard. A driver who
            // touched nothing and arrives with the lot on the floor is paid
            // what a barely-accepted box is worth — a quarter, before the
            // clock — which is still most of the tip gone, and still every
            // reason to buy the better seat.
            //
            // (A LATE run is never zero either, and was not before: the clock's
            // floor is DeliverySlowMult. The rule says zero is ALLOWED there,
            // not that it is owed.)
            o.refused = hitSomething && o.condition <= PizzaRuinedCondition;
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
        /// An opponent's sheet top speed may be at most this much over the
        /// player's. The price band alone reaches 1.65x the player's car at
        /// the top tier, and price is a poor proxy for speed: a 204 km/h
        /// Miata could draw a 284 km/h S15 that the AI runs to 245 on the
        /// straights. AIDriver caps its target at 1.05x the player's vmax so a
        /// faster car cannot simply drive away — but a car that is capped at
        /// the player's speed while the player is flat out reads as a
        /// rubber-band, so the pool is trimmed FIRST and the cap is the
        /// backstop. 15% leaves room for a genuinely quicker rival to exist.
        /// </summary>
        public const float OpponentVmaxCeiling = 1.15f;

        /// <summary>
        /// The cars a player with a car worth <paramref name="referencePrice"/>
        /// can be drawn against at a tier. Band around the player's money —
        /// it opens UPWARD with tier rather than sliding, because the low end
        /// has to stay reachable or the field stops containing anything the
        /// player could plausibly have beaten to get here — trimmed to
        /// <see cref="OpponentVmaxCeiling"/>. Both ends of the catalog are
        /// thin (a $5,500 Civic and a $1,066,000 hypercar both have almost no
        /// neighbours), so the band widens before giving up, and the speed
        /// trim is dropped last of all: exactly one car in the catalog — the
        /// $950k Nissan R390 GT1 Road Car, a 347 km/h car among 400 km/h
        /// neighbours — has under three same-speed neighbours even in the
        /// wide band, and a fast field beats no field. The self-test counts
        /// that this stays one car.
        /// </summary>
        public static System.Collections.Generic.List<CarSpec> OpponentPool(
            int referencePrice, float playerVmaxMps, int tier, out bool vmaxTrimmed)
        {
            float lo = 0.65f + tier * 0.08f;
            float hi = 1.20f + tier * 0.15f;
            float cap = playerVmaxMps > 1f ? playerVmaxMps * OpponentVmaxCeiling : float.MaxValue;

            var pool = CarCatalog.InPriceBand(Mathf.RoundToInt(referencePrice * lo),
                                              Mathf.RoundToInt(referencePrice * hi));
            pool.RemoveAll(c => c.topSpeedMps > cap);
            vmaxTrimmed = true;
            if (pool.Count < FieldOpponents)
            {
                pool = CarCatalog.InPriceBand(referencePrice / 3, referencePrice * 3);
                pool.RemoveAll(c => c.topSpeedMps > cap);
            }
            if (pool.Count < FieldOpponents)
            {
                pool = CarCatalog.InPriceBand(referencePrice / 3, referencePrice * 3);
                vmaxTrimmed = false;
            }
            return pool;
        }

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
            var playerSpec = CarCatalog.Get(car.specId);
            float playerVmax = playerSpec != null ? playerSpec.topSpeedMps : 0f;

            var pool = OpponentPool(reference, playerVmax, tier, out _);
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
            // ... and NOT a race at a car meet. The night at the meet is ONE
            // block however many people were lined up against, and it is paid
            // by the drive home like any other trip into town — which is also
            // why that drive is written into the day as AT THE MEET rather
            // than as a drive, on a night somebody was actually raced.
            if (!RaceHandoff.Delivery && !RaceHandoff.CommuteLeg && !RaceHandoff.MeetRace)
                SpendActivitySlot(s, !RaceHandoff.FreeRoam ? ActRace
                                     : CarMeets.RacedTonight(s) ? ActMeet : ActDrive);

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

                // 4a. HEAT. Everything the coolant gauge did out there, banked
                // from what the model MEASURED rather than derived from the
                // distance — there is no honest average overheat per kilometre,
                // and the whole point of the gauge is that two identical races
                // cost different amounts depending on whether the driver
                // watched it.
                ApplyHeatResult(s, car, meters);

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

                // 5. fault wear rolls (H535): worn components start throwing
                // faults, at odds set by how worn the lane is and HOW FAR THIS
                // LEG WENT. See WearFaultChance — a stamped leg that covered no
                // ground (a shop door, a turned-back crossing) rolls nothing,
                // which is what stops an errand into town costing three faults.
                RollThresholdFault(s, car, "engine", car.engine, meters);
                RollThresholdFault(s, car, "tires", car.tires, meters);
                RollThresholdFault(s, car, "hp", car.carHP, meters);
            }

            // 6. payout + rep (win-only tier purse)
            string summary;
            if (RaceHandoff.FreeRoam)
            {
                // A drive is not a result: no purse, no rep, and no rep-decay
                // reset — cruising Charlotte is not showing up on the street.
                // The metres, fuel and wear above are already banked.
                // A commute leg is one stretch of a longer trip — to work, to
                // a shop, across the line between your street and the town —
                // so it is "on the road", not a drive that has ended.
                summary = (RaceHandoff.CommuteLeg ? "on the road — " : "free roam — ") +
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
                                         carryCondition: RaceHandoff.CarryCondition,
                                         dropFraction: RaceHandoff.DeliveryDropFraction,
                                         // With no simulation the tally IS the
                                         // impacts, so the question answers itself.
                                         hitSomething: !RaceHandoff.CargoReported ||
                                                       RaceHandoff.CargoImpacts > 0 ||
                                                       RaceHandoff.CarryHit);
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
                    // A call-out at a meet is two cars and one pot: second
                    // place is last place, and last place pays nothing.
                    payout = RaceHandoff.MeetRace ? 0 : Mathf.RoundToInt(purse * scale);
                    s.streetRep = Mathf.Min(100f, s.streetRep + LossRepGain);
                }
                s.money += payout;
                // A blacklist challenge does NOT burn the one-purse-race-a-day
                // cap. A series is three races on a three-day deadline, and a
                // player who could only run one of them a day would lose every
                // call-out to the calendar rather than to the driver — which is
                // the one way of missing a race the rule was never meant to
                // cover.
                //
                // Nor does a race at a car meet, which is RG2's rule ("meet
                // challenges are unlimited — they still award rep/money but do
                // NOT burn the cap"). What stops a meet being a fountain here
                // is CarMeets.MaxRunsPerMeet, and that each driver lines up
                // once a night.
                if (string.IsNullOrEmpty(RaceHandoff.RivalAlias) && !RaceHandoff.MeetRace)
                    s.lastRaceDay = s.day;

                summary = RaceHandoff.MeetRace && !string.IsNullOrEmpty(RaceHandoff.MeetAlias)
                    ? (RaceHandoff.FinishPos == 1 ? "BEAT " : "LOST TO ") + RaceHandoff.MeetAlias +
                      (payout > 0 ? " — won " + MenuKit.Money(payout) : "")
                    : "P" + RaceHandoff.FinishPos + "/" + RaceHandoff.FieldSize +
                      (payout > 0 ? " — won " + MenuKit.Money(payout) : " — no prize");

                if (!string.IsNullOrEmpty(RaceHandoff.RivalAlias))
                {
                    // Recorded AFTER the normal payout: a challenge leg is a
                    // street race first, so it pays, counts for wins and moves
                    // rep on the same rules as any other, then the ladder takes
                    // its cut. What it records is ONE LEG of three — the board
                    // decides whether that settled anything.
                    string ladder = Blacklist.RecordLeg(s, RaceHandoff.RivalAlias,
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

        // ---- wear faults are caused by DISTANCE, and the distance is real ----
        //
        // This used to be "lane under 40 and empty → fault", with no roll in it
        // at all. Two things made that unliveable, and the owner reported both
        // as one: "new faults appear after a race... even when I clear all
        // faults and do not damage the car, new faults appear the next time I
        // warp."
        //
        // The certainty was the first. A lane sitting under the line handed out
        // a fault the instant it was empty, so repairing one bought exactly one
        // apply-back of peace.
        //
        // The second is that an apply-back is NOT a race. ApplyRaceResult runs
        // on every stamped leg — see CityMode.StampExitResult — and legs are
        // stamped by the pause menu, by every town shop door you walk through,
        // and by each half of a crossing. Walking into the mechanic's covers no
        // ground whatever and still banked a result, so a single errand rolled
        // all three lanes two or three times over.
        //
        // Gating on METRES fixes both at once, and it is the honest model as
        // well: a part fails because it was used, so a leg that covered no
        // ground cannot break anything, and the multi-stamp architecture stops
        // mattering — five stamps of one journey carry that journey's distance
        // between them, not five times it.
        //
        // The other cause is IMPACT, and that is already where it belongs: the
        // crash branch above, gated on DamageScore.

        /// <summary>Under this, a leg did not go anywhere. Shop doors, the
        /// crawl off the driveway, an exit thirty seconds after arriving.</summary>
        public const float WearFaultMinM = 300f;

        /// <summary>Chance per kilometre that a lane worn all the way to ZERO
        /// throws a fault. Scaled down linearly by how far above zero the lane
        /// actually is, so the line at 40 is a threshold the risk starts from
        /// rather than a cliff it falls off.</summary>
        public const float WearFaultPerKm = 0.06f;

        /// <summary>Where a lane starts being able to fail at all.</summary>
        public const float WearFaultBelow = 40f;

        /// <summary>And where it starts failing badly — a severe roll reaches
        /// the dearer end of the pool and the lane will hold a second one.</summary>
        public const float SevereWearBelow = 15f;

        /// <summary>The odds this leg put a fault on one lane. Public so the
        /// self-test can pin the shape rather than sampling the roll: p(0 m)
        /// must be 0, p(healthy lane) must be 0, and p must rise with both
        /// distance and wear.</summary>
        public static float WearFaultChance(float value, float meters)
        {
            if (meters < WearFaultMinM) return 0f;
            if (value >= WearFaultBelow) return 0f;
            float depth = Mathf.Clamp01((WearFaultBelow - value) / WearFaultBelow);
            float perKm = Mathf.Clamp01(depth * WearFaultPerKm);
            // Compounded over the distance rather than multiplied by it: a
            // 300 km haul is not a 1,800% chance, it is a near-certainty.
            return 1f - Mathf.Pow(1f - perKm, meters / 1000f);
        }

        // ================= heat (see CoolingModel / EngineTemp) =================
        //
        // A temperature gauge is only a gauge if what it reads can cost
        // something. This is where it does.
        //
        // Three separate things come home from a hot drive, and they are
        // deliberately not one number:
        //
        //   * the ENGINE is worse, permanently, by whatever the model measured
        //     while the needle was in the red;
        //   * the COOLING SYSTEM is worse, because heat is what kills hoses and
        //     fan clutches — so ignoring it once makes the next drive hotter,
        //     which is the loop that turns a warning into a spiral;
        //   * and the COOLANT is wherever the leak left it, which is the one
        //     thing the player can put right for pocket change if they look.
        //
        // The fourth outcome — the engine let go — is not a matter of degree
        // and is handled as a state (see OwnedCar.engineBlown).

        /// <summary>Under this much heat damage a drive was simply warm. Above
        /// it the car has been cooked hard enough to be worth a diagnosis of
        /// its own, on the pools' "cooling" cause.</summary>
        public const float HeatFaultDamage = 3f;

        /// <summary>Set by the last apply-back when the engine was destroyed
        /// out there, for the result screen to lead with. Read once, then
        /// cleared — the same contract as <see cref="lastDiagnosed"/>. It is
        /// the single worst thing that can happen to a car in this game and it
        /// must not arrive as a line in the diary nobody reads.</summary>
        public static string lastBlownEngine;

        /// <summary>Set when the drive ran hot without killing anything, so the
        /// result screen can say so while there is still time to act on it.
        /// </summary>
        public static string lastHeatWarning;

        /// <summary>
        /// Write a destroyed engine onto a car. Idempotent, and the ONLY place
        /// the blown state is entered. False when there was nothing to do.
        /// </summary>
        public static bool BlowEngine(LifeState s, OwnedCar car)
        {
            if (s == null || car == null || car.engineBlown) return false;
            car.engineBlown = true;
            // The condition goes with it. A seized engine is not a tired one,
            // and letting it come home at 40% would mean the rebuild that
            // "restores" it was worth less than the engine already in the car.
            car.engine = 0f;
            car.coolant = 0f;
            // Every engine fault goes too. They were faults with an engine;
            // there is no longer an engine for them to be faults with, and
            // leaving them would have the player paying to reseal a valve
            // cover on a block that is coming out.
            car.faults.RemoveAll(f => f.stat == "engine");
            s.calendarLog.Add(LogDate(s.day) + ": " + car.displayName +
                              " THREW ITS ENGINE — towed home");
            lastBlownEngine = ShortName(car) + " HAS THROWN ITS ENGINE";
            return true;
        }

        /// <summary>
        /// Bank a seizure THE MOMENT IT HAPPENS, from the seat — not at the
        /// exit with the rest of the result.
        ///
        /// Because the rest of the result can be thrown away and this cannot.
        /// Abandoning a race from the pause menu clears ResultReady on purpose
        /// (a half-race must not pay a purse or burn a block), and RESTARTING
        /// one does the same — and both of those are exactly what a player does
        /// when their engine dies a mile from the flag. Routed through the
        /// ordinary apply-back, a destroyed engine would have been undone by
        /// the two most natural things to press after destroying it, and the
        /// feature would have looked broken in a race and worked in free roam.
        ///
        /// There is precedent for reaching into the save from the race scene:
        /// the pause menu's fuel truck takes the money and fills the owned
        /// car's tank the same way, for the same reason — it has happened.
        /// </summary>
        public static void BankSeizureNow()
        {
            // Not a standalone editor race, and not somebody else's car: a
            // seller's engine let go on a test drive is a different game's
            // problem, and the one thing it must not do is blow up the
            // player's own car by resolving to it.
            if (!RaceHandoff.FromLifeSim || RaceHandoff.TestDrive) return;
            var s = LifeSimManager.State;
            if (s == null) return;
            var car = s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar;
            if (BlowEngine(s, car)) LifeSimManager.Save();
        }

        static void ApplyHeatResult(LifeState s, OwnedCar car, float meters)
        {
            // Ordinary use ages the hardware whatever the temperature did, so
            // this half runs on every leg — including one from a scene too old
            // to carry an EngineTemp. A radiator does not know whether anybody
            // was watching it.
            float wearMult = (1f + car.odoMiles / 100000f) * RaceWearScale;
            car.radiator = Mathf.Max(0f, car.radiator - CoolingModel.RadWearPerM * meters * wearMult);
            car.hoses = Mathf.Max(0f, car.hoses - CoolingModel.HoseWearPerM * meters * wearMult);
            car.fan = Mathf.Max(0f, car.fan - CoolingModel.FanWearPerM * meters * wearMult);

            if (!RaceHandoff.HeatReported) return;

            // The coolant is MEASURED, like the tank: a leak is not a function
            // of distance, and a car that boiled its system dry in two minutes
            // stationary has to come home empty.
            car.coolant = Mathf.Clamp(RaceHandoff.EndCoolantPct, 0f, 100f);

            float hot = RaceHandoff.OverheatSeconds;
            if (hot > 0f)
            {
                car.hoses = Mathf.Max(0f, car.hoses - CoolingModel.HoseHeatWearPerSec * hot);
                car.fan = Mathf.Max(0f, car.fan - CoolingModel.FanHeatWearPerSec * hot);
                car.radiator = Mathf.Max(0f, car.radiator - CoolingModel.RadHeatWearPerSec * hot);
            }

            float damage = RaceHandoff.HeatEngineDamage;
            if (damage > 0f) car.engine = Mathf.Max(0f, car.engine - damage);

            if (RaceHandoff.EngineSeized)
            {
                // IT IS DEAD. Usually already written — see BankSeizureNow,
                // which runs the instant it happens — and BlowEngine is
                // idempotent, so this is the path for a scene that seized
                // without a LifeSim under it and a guard for the one that did.
                BlowEngine(s, car);
                return;
            }

            if (damage >= HeatFaultDamage)
            {
                // Cooked hard enough to have broken something specific. The
                // pools tag their rows by cause and "cooling" is one of them —
                // cooling_fail itself, and the two gasket rows that a hot engine
                // is exactly how you get.
                var spec = CarCatalog.Get(car.specId);
                AddFault(s, car, FaultCatalog.RollWearFault(
                    car, "engine", damage >= HeatFaultDamage * 3f, "cooling",
                    spec != null ? spec.origin : "jpn"));
            }

            // And the warning, for a drive that got hot and got away with it.
            // Said in degrees, because the gauge the player was ignoring is in
            // degrees and this has to be recognisable as the same thing.
            if (RaceHandoff.PeakCelsius > EngineTemp.RedMark)
                lastHeatWarning = "IT RAN HOT — PEAK " +
                                  Mathf.RoundToInt(RaceHandoff.PeakCelsius) + " C";
            else if (car.coolant < CoolingModel.CoolantLowPct)
                lastHeatWarning = "COOLANT DOWN TO " + Mathf.RoundToInt(car.coolant) + "%";
        }

        static void RollThresholdFault(LifeState s, OwnedCar car, string stat, float value,
                                       float meters)
        {
            float p = WearFaultChance(value, meters);
            if (p <= 0f || UnityEngine.Random.value >= p) return;
            // Deep wear reaches the severe pool, which is dearer and which the
            // lane will take a second of. The threshold that lets the roll
            // happen at all and the one that makes it severe are different
            // lines, and the gap between them is the warning.
            AddFault(s, car, FaultCatalog.RollWearFault(car, stat, value < SevereWearBelow));
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

        /// <summary>
        /// The one line the result toast appends, and the only place the
        /// PRECEDENCE between the four things a drive can leave behind is
        /// written down.
        ///
        /// A drive can hand back more than one of these at once — a race hot
        /// enough to break something has usually rolled a fault on the way —
        /// and the toast is one line. So they are ranked by what the player has
        /// to do about them: an engine on the floor outranks everything, and
        /// after that the TEMPERATURE outranks the fault it caused.
        ///
        /// That last order is the one worth arguing about, and it was the other
        /// way round first. A race that cooked the engine and rolled a fault
        /// reported "the engine is down on song — worth an inspection", which
        /// is true, useless, and hides the thing the player could actually have
        /// done differently: they drove it hot, and it is going to keep
        /// happening. The fault is still there to be found; the gauge reading
        /// is the only part of it that is gone the moment the screen changes.
        ///
        /// Drains all four whichever one wins, because a note left behind is
        /// announced under the NEXT race's result.
        /// </summary>
        public static string DrainResultNote()
        {
            string note =
                !string.IsNullOrEmpty(lastBlownEngine)
                    ? lastBlownEngine + " — REBUILD OR SWAP IT"
                : !string.IsNullOrEmpty(lastHeatWarning)
                    ? lastHeatWarning + (string.IsNullOrEmpty(lastDiagnosed) &&
                                         string.IsNullOrEmpty(lastSymptom)
                                            ? "" : ", AND SOMETHING BROKE")
                : !string.IsNullOrEmpty(lastDiagnosed)
                    ? "DIAGNOSED: " + lastDiagnosed
                : !string.IsNullOrEmpty(lastSymptom)
                    ? lastSymptom.ToUpper() + " — WORTH AN INSPECTION"
                    : null;
            lastBlownEngine = lastDiagnosed = lastSymptom = lastHeatWarning = null;
            return note;
        }

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
        /// Why this car cannot be driven right now, or null if it can.
        ///
        /// One answer for every door — the house, the town kerb, the pre-race
        /// page — because a car that is refused at one of them and accepted at
        /// another is a car the player gets stranded in. Two reasons so far:
        /// somebody else has it, and it has no engine.
        /// </summary>
        public static string DriveRefusal(LifeState s, OwnedCar car)
        {
            if (car == null) return "you have no car";
            string away = CarWhere.BlockedReason(s, car);
            if (away != null) return away;
            if (car.engineBlown) return BlownLine;
            return null;
        }

        /// <summary>What every screen says about a destroyed engine. One
        /// string, because it is going to be read on a page the player did not
        /// expect to be reading and it has to name the way OUT, not just the
        /// problem — the same rule the dry-tank banner follows.</summary>
        public const string BlownLine = "ENGINE IS GONE — REBUILD OR SWAP IT IN THE GARAGE";

        public static bool CanDrive(LifeState s, OwnedCar car) => DriveRefusal(s, car) == null;

        /// <summary>
        /// Book a repair. Every venue queues into pendingParts now: DIY and the
        /// mechanic for the days they quote, done first thing on the morning
        /// they promised, and the dealership for ONE BLOCK — "same day" means
        /// the car is back for the next third of it, not that nobody ever had
        /// to put it on a ramp. A job anywhere but your own garage takes the
        /// CAR with it (see <see cref="CarWhere"/>): it is at the shop until the
        /// job is done, and the keys move to something else if there is
        /// anything else. Returns null on success, or the reason it was
        /// refused.
        /// </summary>
        public static string OrderRepair(LifeState s, OwnedCar car, CarFault f,
                                         FaultCatalog.Venue venue)
        {
            if (car == null || f == null) return "no car";
            if (s.pendingParts.Exists(p => p.carId == car.id && p.faultId == f.id))
                return "already booked";
            string elsewhere = CarWhere.RefuseWork(s, car, (int)venue);
            if (elsewhere != null) return elsewhere;

            var q = FaultCatalog.GetQuote(s, car, f, venue);
            if (!q.available) return q.blockedReason;
            if (s.money < q.price) return "need " + MenuKit.Money(q.price);

            s.money -= q.price;
            if (venue == FaultCatalog.Venue.Diy)
                s.mechSkill = Mathf.Min(100f, s.mechSkill +
                                        FaultCatalog.DiySkillGain(s.mechSkill, q.difficulty));

            int readyDay = s.day + q.days, readySlot = MorningSlot;
            // The dealership quotes no days at all. It has the car for the
            // block it was dropped off in, and hands it back at the next one.
            if (q.days <= 0) CarWhere.NextBlock(s, out readyDay, out readySlot);

            s.pendingParts.Add(new PendingPart
            {
                carId = car.id,
                faultId = f.id,
                label = f.label,
                stat = f.stat,
                add = f.add,
                readyDay = readyDay,
                readySlot = readySlot,
                venue = (int)venue,
            });
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": booked " + f.label +
                              " (" + MenuKit.Money(q.price) + ", " +
                              (q.days > 0 ? q.days + "d" : "back next block") + ")");
            if (venue != FaultCatalog.Venue.Diy) DropOff(s, car, (int)venue);
            return null;
        }

        /// <summary>
        /// The car has just been left at a shop. Says so in the diary, and
        /// moves the keys — see <see cref="CarWhere.HandOver"/>. Every rule that
        /// books work away from home ends here (repairs, shop-fitted upgrades,
        /// resprays), so "the car is at the shop" is announced one way.
        /// </summary>
        public static void DropOff(LifeState s, OwnedCar car, int venue)
        {
            if (s == null || car == null) return;
            var next = CarWhere.HandOver(s, car);
            lastDropOff = ShortName(car) + " LEFT AT " + CarWhere.ShopNameOf(venue) + " — " +
                          CarWhere.ReadyLabel(s, car) +
                          (next != null ? "  ·  NOW DRIVING " + ShortName(next) : "");
        }

        /// <summary>Set by the last <see cref="DropOff"/>, for whichever screen
        /// booked the job to toast. Read once, then cleared — the same contract
        /// as <see cref="lastDiagnosed"/>.</summary>
        public static string lastDropOff;

        /// <summary>Set when a rollover or a block change brought a car HOME
        /// from a shop, for the next toast to carry: the player slept, and the
        /// thing that changed overnight is that they have a car again.</summary>
        public static string lastCarBack;

        /// <summary>Set when a rebuild or a swap finished. Its own line rather
        /// than a "back from the mechanic" — a car coming home from an oil
        /// change and a car coming home with an engine in it are not the same
        /// news.</summary>
        public static string lastEngineJobDone;

        /// <summary>"Mazda RX-7 Type RS (FD) '98" → "MAZDA RX-7". A toast has
        /// one line and a catalog name is most of it.</summary>
        public static string ShortName(OwnedCar car)
        {
            if (car == null || string.IsNullOrEmpty(car.displayName)) return "THE CAR";
            var parts = car.displayName.Split(' ');
            return (parts.Length <= 2 ? car.displayName : parts[0] + " " + parts[1]).ToUpperInvariant();
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

        /// <summary>
        /// Resolve every job whose BLOCK has come. Called from the Rollover
        /// pipeline and from every other place the clock moves (a spent block,
        /// a nap), because a job can now be promised for a block rather than
        /// for a morning — the dealership's are — and one that only resolved
        /// overnight would keep a "back this afternoon" car until tomorrow.
        /// Waiting on a shop still costs real time; it is just counted in the
        /// unit the rest of the day is.
        /// </summary>
        static void TickPendingParts(LifeState s)
        {
            if (s == null || s.pendingParts == null) return;
            // Who was away BEFORE anything resolves, so a car that comes home
            // in this tick can be told apart from one that was home all along.
            var wasAway = new System.Collections.Generic.List<OwnedCar>();
            var cameFrom = new System.Collections.Generic.List<int>();
            foreach (var c in s.cars)
            {
                var job = CarWhere.AwayJob(s, c);
                if (job == null) continue;
                wasAway.Add(c);
                cameFrom.Add(job.venue);
            }

            for (int i = s.pendingParts.Count - 1; i >= 0; i--)
            {
                var p = s.pendingParts[i];
                if (p == null) { s.pendingParts.RemoveAt(i); continue; }
                if (!CarWhere.Due(s, p)) continue;
                var car = s.FindCar(p.carId);
                if (car != null)
                {
                    if (p.IsRespray)
                    {
                        // The colour goes on when the job is DONE. Same two
                        // things a respray has always done — the new livery,
                        // and the panels back at 100 with the paint lane's
                        // faults gone, because it is a refinish as well as a
                        // colour change (see Paint.Respray).
                        car.paintSkin = p.paintSkin ?? "";
                        car.paint = 100f;
                        car.faults.RemoveAll(x => x.stat == "paint");
                        s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + p.label + " finished");
                    }
                    else if (p.IsEngineJob)
                    {
                        // There is an engine in it again. A rebuild comes back
                        // at 100 and a swap at whatever the donor had, and both
                        // clear the BLOWN state — which is the only thing that
                        // clears it, anywhere.
                        car.engineBlown = false;
                        car.engine = Mathf.Clamp(p.engineCondAfter, 1f, 100f);
                        car.faults.RemoveAll(x => x.stat == "engine");
                        // Anything that has the engine out has the cooling
                        // system off it, and no shop puts the old hoses back on
                        // a fresh motor. Not the radiator, though: the core
                        // comes off, gets looked at, and goes back on.
                        car.hoses = Mathf.Max(car.hoses, 100f);
                        car.coolant = 100f;
                        s.calendarLog.Add(LogDate(s.day) + ": " + p.label + " done — " +
                                          car.displayName + " runs again");
                        lastEngineJobDone = ShortName(car) + " HAS AN ENGINE AGAIN";
                    }
                    else if (p.IsUpgrade)
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
                        // The one fault in the catalog whose name is a list of
                        // PARTS — "Radiator & Hoses" — has to leave those parts
                        // new. Repairing it used to clear a line of text and
                        // leave the temperature gauge climbing exactly as it
                        // was, which is the shape of a bug a player would
                        // describe as "I paid $400 and nothing happened".
                        if (p.faultId == "cooling_fail") CoolingModel.RenewNamedParts(car);
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

            // The cars that are HOME again. A car with two jobs on it is home
            // when the second one is done, which is why this asks the queue
            // again rather than counting the jobs it just removed.
            for (int i = 0; i < wasAway.Count; i++)
            {
                var c = wasAway[i];
                if (CarWhere.Away(s, c)) continue;
                string shop = CarWhere.ShopNameOf(cameFrom[i]);
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + c.displayName +
                                  " is back from " + shop);
                lastCarBack = ShortName(c) + " IS BACK FROM " + shop;
                // Nothing to drive while it was away: the keys never moved, so
                // nothing has to be done. But a player who was handed the OTHER
                // car keeps that one — walking into the garage and finding the
                // game had switched cars overnight would be the game driving.
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
            // While-you-wait work: it does not keep the car. But it is the
            // MECHANIC's work, so a car standing at the dealership or in the
            // spray booth cannot have it done until it is back.
            string elsewhere = CarWhere.RefuseWork(s, car, CarWhere.VenueMechanic);
            if (elsewhere != null) return elsewhere;
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
            //
            // And an ENGINE JOB, which is the same reason again with another
            // zero on it: a rebuild is booked under the engine lane because
            // that is the lane it restores, and an oil change quietly voiding a
            // four-thousand-dollar bottom end would be the most expensive bug
            // in the game.
            s.pendingParts.RemoveAll(p => p.carId == car.id && p.stat == svc.stat &&
                                          !p.IsUpgrade && !p.IsYardPart && !p.IsEngineJob);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + svc.name + " " + MenuKit.Money(price));
            return null;
        }

        // ================= the cooling system =================
        //
        // Four parts, priced the way the mechanic's other while-you-wait work
        // is priced, because that is what they are: bolt-on jobs an afternoon
        // long. The interesting decision is not WHICH to buy — a failing part
        // is obvious once it is diagnosed — it is WHEN, and the answer the game
        // wants a player to arrive at is "before the gauge tells me", because
        // by then the heat has already been taking condition off the engine.

        public enum CoolPart { Radiator = 0, Fan = 1, Hoses = 2, Coolant = 3 }

        /// <summary>Base price, and the name on the invoice. Coolant is last
        /// and cheapest on purpose: it is the one the player should be doing
        /// habitually, and a habit has to be affordable.</summary>
        public static readonly (string name, int cost, CoolPart part)[] CoolingServices =
        {
            ("NEW RADIATOR",      260, CoolPart.Radiator),
            ("FAN + CLUTCH",      175, CoolPart.Fan),
            ("HOSES + CLAMPS",    110, CoolPart.Hoses),
            ("COOLANT FLUSH",      35, CoolPart.Coolant),
        };

        public static float CoolPartCond(OwnedCar car, CoolPart part)
        {
            if (car == null) return 100f;
            switch (part)
            {
                case CoolPart.Radiator: return car.radiator;
                case CoolPart.Fan: return car.fan;
                case CoolPart.Hoses: return car.hoses;
                default: return car.coolant;
            }
        }

        static void SetCoolPart(OwnedCar car, CoolPart part, float v)
        {
            switch (part)
            {
                case CoolPart.Radiator: car.radiator = v; break;
                case CoolPart.Fan: car.fan = v; break;
                case CoolPart.Hoses: car.hoses = v; break;
                default: car.coolant = v; break;
            }
        }

        /// <summary>
        /// Fit one. Returns null on success or the reason it was refused.
        ///
        /// A new radiator or a new set of hoses CLEARS cooling_fail, and has to:
        /// that fault's own name in the catalog is "Radiator &amp; Hoses", so
        /// leaving it on a car whose radiator and hoses are both new would be
        /// the garage charging twice for one repair.
        /// </summary>
        public static string BuyCoolingService(LifeState s, OwnedCar car, int idx)
        {
            if (car == null) return "no car";
            if (idx < 0 || idx >= CoolingServices.Length) return "no such job";
            string elsewhere = CarWhere.RefuseWork(s, car, CarWhere.VenueMechanic);
            if (elsewhere != null) return elsewhere;
            var svc = CoolingServices[idx];
            int price = ServiceCost(car, svc.cost);
            if (s.money < price) return "need " + MenuKit.Money(price);
            s.money -= price;
            SetCoolPart(car, svc.part, 100f);
            // Anything that opens the system fills it back up. Only the coolant
            // job does it alone.
            if (svc.part != CoolPart.Coolant) car.coolant = 100f;
            if (svc.part == CoolPart.Radiator || svc.part == CoolPart.Hoses)
            {
                // Both named parts have to be sound for the fault to be gone —
                // a new core with the old hoses still on it is still the fault
                // the catalog describes.
                if (car.radiator > 95f && car.hoses > 95f)
                    car.faults.RemoveAll(f => f.id == "cooling_fail");
            }
            s.calendarLog.Add(LogDate(s.day) + ": " + svc.name + " " + MenuKit.Money(price));
            return null;
        }

        /// <summary>Top the system up on your own drive, out of a jug. The one
        /// cooling job that is not the mechanic's — it needs no tools, no skill
        /// and no appointment, and it is the whole reward for having looked at
        /// the overflow tank.</summary>
        public static string TopUpCoolant(LifeState s, OwnedCar car)
        {
            if (car == null) return "no car";
            if (car.coolant >= 99.5f) return "it is already full";
            if (s.money < CoolingModel.CoolantTopUpCost)
                return "need " + MenuKit.Money(CoolingModel.CoolantTopUpCost);
            s.money -= CoolingModel.CoolantTopUpCost;
            car.coolant = 100f;
            s.calendarLog.Add(LogDate(s.day) + ": topped up the coolant in " +
                              car.displayName);
            return null;
        }

        // ================= the bottom end: rebuild, or another engine =================
        //
        // The one repair in the game that is not a repair. Every other job puts
        // a number back up; these two exist because a number ran OUT — the
        // engine is destroyed, the car does not move, and until one of them is
        // paid for there is nothing else to decide about that car.
        //
        // Two of them rather than one, because the choice is the interesting
        // part and it is a real one: money against time against what you get
        // back. A rebuild is the expensive, slow, CERTAIN answer. A swap is
        // half the price and half the wait for somebody else's engine, with
        // somebody else's history in it.

        public const string JobRebuild = "rebuild";
        public const string JobSwap = "swap";

        /// <summary>Days each takes. A rebuild is a machine shop and a week;
        /// a swap is a hoist and a long weekend.</summary>
        public const int RebuildDays = 5, SwapDays = 2;

        /// <summary>Engine condition a used engine arrives with, before the
        /// donor's own luck. Not 100 and not close: the point of the cheap
        /// answer is that you will be here again.</summary>
        public const int SwapCondMin = 58, SwapCondMax = 78;

        /// <summary>Percent chance a swapped-in engine brings a hidden fault
        /// with it, through the same door a salvage-yard part comes in by.
        /// </summary>
        public const int SwapFaultRisk = 30;

        /// <summary>
        /// What the bottom end costs on THIS car. Flat labour plus a slice of
        /// what the car is worth: pulling an engine takes the same afternoon
        /// whatever it is bolted into, and the parts to put back in it do not.
        /// </summary>
        public static int EngineJobPrice(OwnedCar car, string job)
        {
            float value = Mathf.Max(1f, car != null ? car.catalogPrice : 1f);
            float price = job == JobSwap ? 520f + value * 0.05f
                                         : 1250f + value * 0.10f;
            return Mathf.RoundToInt(Mathf.Min(price, FaultCatalog.RepairPriceCap));
        }

        /// <summary>The two jobs, in the order the page offers them: the good
        /// one first, because a player who can afford it should not have to
        /// read past the cheap one to find it.</summary>
        public static readonly (string job, string name, string blurb)[] EngineJobs =
        {
            (JobRebuild, "ENGINE REBUILD",
             "Out, stripped, machined, back in. Comes back as new."),
            (JobSwap, "ENGINE SWAP",
             "A running engine out of somebody else's car. Cheaper, quicker, used."),
        };

        /// <summary>
        /// Worth offering at all? A blown engine has no other move, and a
        /// nearly-worn-out one is a car whose owner can see this coming — which
        /// is the only way the player ever gets to make this decision BEFORE
        /// the tow truck does.
        /// </summary>
        public const float RebuildOfferBelow = 35f;

        public static bool EngineJobOffered(OwnedCar car) =>
            car != null && (car.engineBlown || car.engine < RebuildOfferBelow);

        /// <summary>
        /// Book one. Same shape as <see cref="OrderRepair"/> — money now, car at
        /// the shop, part back on a promised block — because it IS that, with a
        /// bigger number on it.
        /// </summary>
        public static string OrderEngineJob(LifeState s, OwnedCar car, string job)
        {
            if (car == null) return "no car";
            if (!EngineJobOffered(car)) return "this engine is fine";
            if (s.pendingParts.Exists(p => p.carId == car.id && p.IsEngineJob))
                return "already booked";
            string elsewhere = CarWhere.RefuseWork(s, car, CarWhere.VenueMechanic);
            if (elsewhere != null) return elsewhere;

            int price = EngineJobPrice(car, job);
            if (s.money < price) return "need " + MenuKit.Money(price);
            s.money -= price;

            bool swap = job == JobSwap;
            s.pendingParts.Add(new PendingPart
            {
                carId = car.id,
                label = swap ? "ENGINE SWAP" : "ENGINE REBUILD",
                stat = "engine",
                engineJob = swap ? JobSwap : JobRebuild,
                engineCondAfter = swap ? Random.Range(SwapCondMin, SwapCondMax + 1) : 100,
                junkRisk = swap ? SwapFaultRisk : 0,
                readyDay = s.day + (swap ? SwapDays : RebuildDays),
                readySlot = MorningSlot,
                venue = CarWhere.VenueMechanic,
            });
            s.calendarLog.Add(LogDate(s.day) + ": booked " +
                              (swap ? "an engine swap" : "an engine rebuild") + " for " +
                              car.displayName + " (" + MenuKit.Money(price) + ", " +
                              (swap ? SwapDays : RebuildDays) + "d)");
            DropOff(s, car, CarWhere.VenueMechanic);
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
        public static void SpendActivitySlot(LifeState s) => SpendActivitySlot(s, ActErrand);

        /// <summary>The same, saying what the block went on — one of the Act
        /// words — so the calendar can draw it afterwards.</summary>
        public static void SpendActivitySlot(LifeState s, string what)
        {
            RecordAct(s, s.slotIndex, what);
            s.slotsActiveToday++;
            s.slotIndex++;
            if (s.slotIndex > 2) Rollover(s, sleptTonight: false);
            // The rollover ticks the job queue itself; a block that merely
            // moved on has to as well, or a car promised for this afternoon
            // stays at the dealership until tomorrow.
            else TickPendingParts(s);
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
            // (A line here used to clear PizzaRun.DriveToShop — the player's
            // stated intent to clock on, which did not survive sleeping through
            // the block it was made in. There is no such intent any more: the
            // town's cue asks the clock whether Tony's would take a shift now,
            // so waking to a morning the shop is shut answers itself.)
            RecordAct(s, s.slotIndex, ActSleep);
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
            // A nap moves the clock, and the clock is what the shops work to.
            TickPendingParts(s);
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
            // 0. close the day's record before anything below changes what
            //    "today" is. The three words are already in slotActs — the
            //    caller stamped the last one before calling in here.
            var acts = SlotActs(s);
            if (s.dayLog == null) s.dayLog = new System.Collections.Generic.List<DayRecord>();
            s.dayLog.RemoveAll(r => r == null || r.day == s.day);
            s.dayLog.Add(new DayRecord
            {
                day = s.day, morning = acts[0] ?? "", afternoon = acts[1] ?? "", night = acts[2] ?? "",
            });
            while (s.dayLog.Count > DayLogKeep) s.dayLog.RemoveAt(0);

            // 1. no-show: employed, the shop was open, no run taken
            //    (noShowAbsence.ts ladder, re-cut for the delivery roster).
            //
            // The shop is open seven days now, so "did not work today" has
            // stopped meaning "skived": it is also the day the gearbox came out.
            // The ladder therefore counts consecutive days off and only starts
            // charging PAST the allowance — two free days, the same two the old
            // weekend handed out, except the player chooses which two they are.
            //
            // NOBODY IS FIRED. There is one job in this game and it is the
            // player's; the ladder that ended in "FIRED from FOOD DELIVERY"
            // was left over from a port with a job board, and it fired the
            // owner during a test — which is how the shop counter came to
            // offer only slices for a week. The owner's rule now: "missing
            // work just reduces your weekly paycheck." So past the free days,
            // a missed day docks one typical day's tips from the pay pending
            // for Friday, and nothing else happens — no rep, no dismissal, no
            // credit score. It cannot go below zero: you cannot owe the shop
            // for tips you never collected.
            if (!string.IsNullOrEmpty(s.playerJob) && !s.workedToday)
            {
                s.consecutiveAbsences++;
                int over = s.consecutiveAbsences - FreeDaysOff;
                if (over > 0)
                {
                    int dock = Mathf.Min(s.pendingSalary, MissedDayDock(s));
                    s.pendingSalary -= dock;
                    s.workDaysTotal++;
                    s.calendarLog.Add(LifeRules.LogDate(s.day) + ": missed a shift" +
                                      (dock > 0 ? " (−$" + dock + " off Friday's pay)" : ""));
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
            for (int i = 0; i < acts.Count; i++) acts[i] = "";

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

            // 9. THE BOARD MOVES. Deadlines close, the ten of them race each
            // other, and the rung below may knock. Order matters — pruning the
            // mail first stops a call-out posted this morning being swept the
            // same morning.
            s.mail.RemoveAll(m => m.expiresDay > 0 && s.day > m.expiresDay);
            lastPage = Blacklist.TickLadder(s);

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
            // On the board from day one, at the bottom of it. The ladder is the
            // only gate a challenge has now, so a name has to be standing on a
            // rung before it can climb one.
            Blacklist.SeedBoard(s);
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
            CoolingModel.Seed(car, cond);
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
