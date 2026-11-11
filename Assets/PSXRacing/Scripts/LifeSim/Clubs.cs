using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    // =====================================================================
    //  THE ENFORCED WEEK (owner, 2026-11-11, "PSX Racing Plans"):
    //    "Schedule enforced (work, car club meet, club races)"
    //    "Miss meets, races, or shifts (dropped from club, laid off from
    //     work, called back when there is opening). Not having a car that
    //     meets club requirements is also grounds from removal and
    //     considered missing event even if showing up with wrong car."
    //
    //  Three classes, one shape. WorkSchedule says when the job wants you,
    //  Clubs says what the club wants and who is in it, Duty turns both
    //  into OBLIGATIONS on the calendar and settles each one as its block
    //  passes. Like CarMeets and CarWhere, NOTHING here stores an event:
    //  a shift or a meet is on the calendar by rule. The save holds only
    //  standing (roster, club, the club's order) and the record of what was
    //  owed and what came of it (LifeState.dutyLog), which is also where
    //  every strike is read from.
    //
    //  EVERY NUMBER THE OWNER MAY WANT TO MOVE IS A CONSTANT IN THIS FILE.
    // =====================================================================

    /// <summary>
    /// When the job wants the player: a fixed weekly schedule of (weekday,
    /// start block) shifts, each <see cref="LifeRules.ShiftBlocks"/> long.
    /// "Instead of possibly slots to work each day, a schedule is defined."
    /// </summary>
    public static class WorkSchedule
    {
        // ---------------- tunables ----------------
        /// <summary>Misses inside <see cref="LayoffWindowDays"/> that end in a
        /// lay-off. The first is a warning in the mail.</summary>
        public const int LayoffMisses = 2;
        public const int LayoffWindowDays = 14;
        /// <summary>The call-back ("there is an opening") arrives a seeded
        /// 4-9 days after the lay-off.</summary>
        public const int CallBackMinDays = 4, CallBackMaxDays = 9;

        public struct Shift
        {
            /// <summary>Sunday-first weekday column (LifeRules.WeekCol): SUN 0 .. SAT 6.</summary>
            public int col;
            /// <summary>The block the shift starts in.</summary>
            public int slot;
            public Shift(int col, int slot) { this.col = col; this.slot = slot; }
        }

        public const int Sun = 0, Mon = 1, Tue = 2, Wed = 3, Thu = 4, Fri = 5, Sat = 6;

        /// <summary>
        /// THE ROSTERS, one row per job. Pizza delivery: Monday to Thursday,
        /// AFTERNOON + EVENING (16:00-22:00), which leaves Friday night and the
        /// weekend free for the clubs. One shift a day at most. The future
        /// jobs (mechanic, paramedic, freight hauler, forklift) are a row each
        /// here when they exist; nothing else needs to change.
        /// </summary>
        static readonly (string job, Shift[] shifts)[] Rosters =
        {
            (LifeRules.DeliveryJobName, new[]
            {
                new Shift(Mon, LifeRules.AfternoonSlot), new Shift(Tue, LifeRules.AfternoonSlot),
                new Shift(Wed, LifeRules.AfternoonSlot), new Shift(Thu, LifeRules.AfternoonSlot),
            }),
        };

        public static Shift[] ShiftsFor(string job)
        {
            foreach (var r in Rosters) if (r.job == job) return r.shifts;
            return new Shift[0];
        }

        public static bool HasSchedule(string job) => ShiftsFor(job).Length > 0;

        /// <summary>The block a shift starts in on this day, or -1.</summary>
        public static int ShiftStartOn(string job, int day)
        {
            if (day < 1 || string.IsNullOrEmpty(job)) return -1;
            int col = LifeRules.WeekCol(day);
            foreach (var sh in ShiftsFor(job)) if (sh.col == col) return sh.slot;
            return -1;
        }

        /// <summary>Blocks a shift starting in this block spans, stopping at
        /// the day's end (the rollover is closing time).</summary>
        public static int SpanFrom(int startSlot) =>
            Mathf.Max(1, Mathf.Min(LifeRules.ShiftBlocks, LifeRules.SlotCount - startSlot));

        /// <summary>The start block of the scheduled shift that covers this
        /// block, or -1.</summary>
        public static int ShiftStartAt(string job, int day, int slot)
        {
            int st = ShiftStartOn(job, day);
            return st >= 0 && slot >= st && slot < st + SpanFrom(st) ? st : -1;
        }

        public static bool Employed(LifeState s) => s != null && !string.IsNullOrEmpty(s.playerJob);
        /// <summary>Employed and not laid off: the schedule applies.</summary>
        public static bool OnRoster(LifeState s) => Employed(s) && !s.laidOff;

        /// <summary>Clocking on is possible only inside a scheduled shift,
        /// once a day, while on the roster.</summary>
        public static bool CanClockOn(LifeState s) => ClockOnRefusal(s) == null;

        /// <summary>Why the counter will not hand over a run, or null.</summary>
        public static string ClockOnRefusal(LifeState s)
        {
            if (!Employed(s)) return "you have no job to clock on to";
            if (s.laidOff)
                return s.callBackOffered ? "there is an opening - take it on the JOB page"
                                         : "you are laid off - wait for the call-back";
            if (!LifeRules.ShopOpen(s)) return "the shop is shut - your shifts are " + RosterShort(s.playerJob);
            if (ShiftStartAt(s.playerJob, s.day, s.slotIndex) < 0)
            {
                int st = ShiftStartOn(s.playerJob, s.day);
                return st > s.slotIndex
                    ? "your shift today starts in the " + LifeRules.SlotNames[st]
                    : st >= 0 ? "today's shift is over" : "no shift today - you work " + RosterShort(s.playerJob);
            }
            if (s.workedToday) return "you have worked today's shift";
            return null;
        }

        /// <summary>How many blocks clocking on NOW spends: to the end of the
        /// scheduled shift (a late clock-on works what is left of it), or a
        /// whole shift when no schedule covers the block (the debug paths).</summary>
        public static int BlocksToSpend(LifeState s)
        {
            if (s == null) return LifeRules.ShiftBlocks;
            int st = ShiftStartAt(s.playerJob, s.day, s.slotIndex);
            return st < 0 ? LifeRules.ShiftBlocks : st + SpanFrom(st) - s.slotIndex;
        }

        /// <summary>"MON-THU" style: the schedule's days as ranges.</summary>
        public static string DaysLine(string job)
        {
            var cols = new List<int>();
            foreach (var sh in ShiftsFor(job)) if (!cols.Contains(sh.col)) cols.Add(sh.col);
            cols.Sort();
            if (cols.Count == 0) return "NO SHIFTS";
            var parts = new List<string>();
            int i = 0;
            while (i < cols.Count)
            {
                int j = i;
                while (j + 1 < cols.Count && cols[j + 1] == cols[j] + 1) j++;
                parts.Add(j == i ? LifeRules.WeekDayNames[cols[i]]
                    : j == i + 1 ? LifeRules.WeekDayNames[cols[i]] + ", " + LifeRules.WeekDayNames[cols[j]]
                    : LifeRules.WeekDayNames[cols[i]] + "-" + LifeRules.WeekDayNames[cols[j]]);
                i = j + 1;
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The blocks a shift covers, in words: "AFTERNOON + EVENING".</summary>
        public static string BlocksLine(int startSlot)
        {
            int span = SpanFrom(startSlot);
            string w = LifeRules.SlotNames[startSlot];
            for (int k = 1; k < span; k++) w += " + " + LifeRules.SlotNames[startSlot + k];
            return w;
        }

        /// <summary>"MON-THU AFTERNOON + EVENING" - the whole roster in one
        /// line, when every shift starts in the same block (it does today).</summary>
        public static string RosterShort(string job)
        {
            var sh = ShiftsFor(job);
            if (sh.Length == 0) return "NOT ON A SCHEDULE";
            bool same = true;
            foreach (var x in sh) if (x.slot != sh[0].slot) same = false;
            return DaysLine(job) + (same ? " " + BlocksLine(sh[0].slot) : "");
        }

        /// <summary>The hours of a shift starting in this block: "16:00 - 22:00".</summary>
        public static string HoursLine(int startSlot)
        {
            int last = startSlot + SpanFrom(startSlot) - 1;
            string a = LifeRules.SlotHours[startSlot], b = LifeRules.SlotHours[last];
            int ia = a.IndexOf(' '), ib = b.LastIndexOf(' ');
            return (ia > 0 ? a.Substring(0, ia) : a) + " - " + (ib > 0 ? b.Substring(ib + 1) : b);
        }

        // ---------------- misses, lay-off and the call-back ----------------

        /// <summary>Shift misses counted against the player as of a day: the
        /// window back from it, since the last call-back.</summary>
        public static int MissesInWindow(LifeState s, int day)
        {
            if (s == null || s.dutyLog == null) return 0;
            int n = 0;
            foreach (var r in s.dutyLog)
                if (r != null && r.kind == Duty.KindShift && r.IsMiss &&
                    r.day > day - LayoffWindowDays && r.day <= day &&
                    Duty.Ord(r.day, r.slot) >= s.jobFrom)
                    n++;
            return n;
        }

        /// <summary>A shift was just recorded as missed. The first is a
        /// warning; the <see cref="LayoffMisses"/>th inside the window is the
        /// lay-off.</summary>
        public static void OnShiftMissed(LifeState s, DutyRecord rec)
        {
            if (s == null || rec == null || s.laidOff) return;
            int n = MissesInWindow(s, rec.day);
            string when = LifeRules.DateLabel(rec.day) + " " + LifeRules.SlotNames[rec.slot];
            if (n >= LayoffMisses)
            {
                LayOff(s, rec.day);
                return;
            }
            Duty.Notice(s, "WARNING FROM TONY'S",
                "You missed your shift (" + when + "). Miss " + (LayoffMisses - n) +
                " more inside " + LayoffWindowDays + " days and you are laid off.");
        }

        public static void LayOff(LifeState s, int day)
        {
            s.laidOff = true;
            s.layoffDay = day;
            s.callBackOffered = false;
            var rng = new System.Random(day * 7919 + 31);
            s.callBackDay = day + rng.Next(CallBackMinDays, CallBackMaxDays + 1);
            Duty.Notice(s, "LAID OFF AT TONY'S",
                "Two missed shifts. No shifts and no pay until they call you back when there is an opening.");
        }

        /// <summary>The rollover's part: the call-back arrives on its day.</summary>
        public static void Tick(LifeState s)
        {
            if (s == null || !s.laidOff || s.callBackOffered) return;
            if (s.day < s.callBackDay) return;
            s.callBackOffered = true;
            Duty.Notice(s, "TONY'S CALLED - THERE IS AN OPENING",
                "Take your shifts back on the JOB page: " + RosterShort(s.playerJob) + ".");
        }

        /// <summary>Accept the call-back: back on the schedule from this
        /// block, misses cleared. Null when there was nothing to accept.</summary>
        public static string AcceptCallBack(LifeState s)
        {
            if (s == null || !s.laidOff || !s.callBackOffered) return null;
            s.laidOff = false;
            s.callBackOffered = false;
            // Owed from the NEXT block: a shift already under way when the
            // phone was answered is not one the player could have worked.
            s.jobFrom = Duty.NowOrd(s) + 1;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": back on the roster at Tony's");
            return "BACK ON THE ROSTER - " + RosterShort(s.playerJob);
        }

        /// <summary>The roster status in two or three words, for the JOB page
        /// and the planner.</summary>
        public static string StatusWord(LifeState s)
        {
            if (!Employed(s)) return "NO JOB";
            if (s.laidOff) return s.callBackOffered ? "CALLED BACK" : "LAID OFF";
            int n = MissesInWindow(s, s.day);
            return n == 0 ? "ON THE ROSTER" : "WARNED (" + n + " MISS)";
        }
    }

    /// <summary>One car club: who it takes, when it meets, when it races.</summary>
    public class ClubDef
    {
        public string id;
        /// <summary>Working name (plain category words, no brands).</summary>
        public string name;
        /// <summary>One word for the calendar cells.</summary>
        public string shortName;
        /// <summary>The car rule in words: "AMERICAN-BUILT".</summary>
        public string rule;
        public int meetCol, meetSlot, raceCol, raceSlot;
        /// <summary>Venue ids the club races on, in preference order; the
        /// meets' and blacklist's own pools (filtered to this build).</summary>
        public string[] venues;
        public System.Func<CarSpec, bool> fits;
        /// <summary>Invitation only (Clubs.EliteInviteRefusal).</summary>
        public bool elite;

        public bool MeetOn(int day) => day >= 1 && LifeRules.WeekCol(day) == meetCol;
        public bool RaceOn(int day) => day >= 1 && LifeRules.WeekCol(day) == raceCol;
        public bool Fits(CarSpec c) => c != null && fits != null && fits(c);
        public string MeetWhen => LifeRules.WeekDayNames[meetCol] + " " + LifeRules.SlotNames[meetSlot];
        public string RaceWhen => LifeRules.WeekDayNames[raceCol] + " " + LifeRules.SlotNames[raceSlot];
    }

    /// <summary>One perk: what a club gives a member who stands at or above
    /// a rank. Read by rule off the rank; nothing about it is stored.</summary>
    public struct ClubPerk
    {
        public string club;
        /// <summary>Earned at this board position or better (#1 is best).</summary>
        public int rank;
        /// <summary>Clubs.PerkPaint / PerkMechanic / PerkLoaner.</summary>
        public string kind;
        /// <summary>PAINT and MECHANIC: the fraction off the price.</summary>
        public float value;
        public string text;
        /// <summary>False for a perk that is designed but not built yet.</summary>
        public bool live;
        public ClubPerk(string club, int rank, string kind, float value, string text, bool live = true)
        { this.club = club; this.rank = rank; this.kind = kind; this.value = value; this.text = text; this.live = live; }
    }

    /// <summary>
    /// CAR CLUBS, like guilds (owner, 2026-11-11: "You can be in multiple.
    /// They can come with perks. But schedule conflicts can show your
    /// loyalty"). A car qualifies for a club by a rule on its spec (origin, or
    /// model year). Each club has a weekly MEET at the Eastside Lot and a
    /// weekly CLUB RACE, both on the calendar by rule; some clubs' blocks
    /// CLASH on purpose. Inside each club, a board of ten with the player on
    /// it (the blBoard rule: a rank is a position, stored nowhere else). Every
    /// second week a club race is a FIXTURE against another club, pairs by
    /// rank. Perks come with rank; the ELITE club is invitation only.
    /// </summary>
    public static class Clubs
    {
        // ---------------- tunables ----------------
        /// <summary>The board: nine members and the player.</summary>
        public const int BoardSize = 10;
        /// <summary>Misses (per club) inside <see cref="MissWindowDays"/> that
        /// drop the player from THAT club. The first is a warning.</summary>
        public const int MissesToDrop = 2;
        public const int MissWindowDays = 28;
        /// <summary>Days before a club takes back somebody it dropped (or who
        /// walked out), at the bottom of the board.</summary>
        public const int ReadmitDays = 14;
        /// <summary>A club race pays the winner; two cars, one pot.</summary>
        public const int RacePurse = 250;
        public const int FixturePurse = 400;
        /// <summary>Every Nth week the club race is a fixture against another club.</summary>
        public const int FixtureEveryWeeks = 2;
        public const int NewsKeep = 12;
        /// <summary>The newest model year a VINTAGE car may be.</summary>
        public const int VintageLastYear = 1979;
        /// <summary>The model year a FUTURE car must be (or newer).</summary>
        public const int FutureYear = 1999;
        /// <summary>THE ELITE INVITATION: this rank or better in EVERY ordinary
        /// club, or <see cref="EliteOneClubRank"/> in any one of them.</summary>
        public const int EliteEveryClubRank = 5;
        public const int EliteOneClubRank = 1;
        /// <summary>A club mechanic's extra work on a car the club takes (the
        /// service's "+N" is multiplied by 1 + this).</summary>
        public const float MechanicSkillBonus = 0.25f;

        public const string PerkPaint = "PAINT", PerkMechanic = "MECHANIC", PerkLoaner = "LOANER";

        static bool IsEuro(CarSpec c) =>
            c.origin == "eur" || c.origin == "ger" || c.origin == "gbr" || c.origin == "ita" ||
            c.origin == "fra" || c.origin == "swe";

        static readonly string[] Drag = { "EmeraldIsle", "LangstonBridge" };
        static readonly string[] Touge = { "BlueRidge", "MtMitchell", "BlowingRock", "LittleSwitzerland",
                                           "SwissNC226A", "BlowingRockSprint", "GillespieGap" };
        static readonly string[] Grip = { "UptownLoop", "BlowingRock", "LittleSwitzerland" };
        static readonly string[] Street = { "TryonSprint", "UptownLoop", "IndependenceSprint" };
        static readonly string[] Classic = { "EmeraldIsle", "BlowingRock", "LittleSwitzerland" };
        static readonly string[] Hardest = { "MtMitchell", "GillespieGap", "BlueRidge", "LittleSwitzerland" };

        /// <summary>
        /// THE CLUBS. None meets or races in the pizza schedule (MON-THU
        /// AFTERNOON + EVENING). Clashes ON PURPOSE, so being in both says
        /// where your loyalty is:
        ///   SAT NIGHT  - the JAPANESE meet and the FUTURE meet
        ///   SUN MORNING- the EURO meet and the VINTAGE meet
        ///   FRI NIGHT  - the AMERICAN meet and the FUTURE race
        ///   SAT EVENING- the ELITE race and nothing (elite meets SUN NIGHT)
        /// Every other pair keeps out of each other's way.
        ///   AMERICAN  meet FRI NIGHT,   race SUN AFTERNOON (the strip)
        ///   JAPANESE  meet SAT NIGHT,   race SUN EVENING   (the mountains)
        ///   EURO      meet SUN MORNING, race SAT AFTERNOON (the circuits)
        ///   VINTAGE   meet SUN MORNING, race SAT NOON      (strip + loops)
        ///   FUTURE    meet SAT NIGHT,   race FRI NIGHT     (the city)
        ///   ELITE     meet SUN NIGHT,   race SAT EVENING   (the hardest roads) - invitation only
        /// </summary>
        public static readonly ClubDef[] All =
        {
            new ClubDef { id = "american", name = "AMERICAN CLUB", shortName = "AMERICAN", rule = "AMERICAN-BUILT",
                meetCol = WorkSchedule.Fri, meetSlot = LifeRules.NightSlot,
                raceCol = WorkSchedule.Sun, raceSlot = LifeRules.AfternoonSlot,
                venues = Drag, fits = c => c.origin == "usa" },
            new ClubDef { id = "japanese", name = "JAPANESE CLUB", shortName = "JAPANESE", rule = "JAPANESE-BUILT",
                meetCol = WorkSchedule.Sat, meetSlot = LifeRules.NightSlot,
                raceCol = WorkSchedule.Sun, raceSlot = LifeRules.EveningSlot,
                venues = Touge, fits = c => c.origin == "jpn" },
            new ClubDef { id = "euro", name = "EURO CLUB", shortName = "EURO", rule = "EUROPEAN-BUILT",
                meetCol = WorkSchedule.Sun, meetSlot = LifeRules.MorningSlot,
                raceCol = WorkSchedule.Sat, raceSlot = LifeRules.AfternoonSlot,
                venues = Grip, fits = IsEuro },
            new ClubDef { id = "vintage", name = "VINTAGE CLUB", shortName = "VINTAGE", rule = "1979 OR OLDER",
                meetCol = WorkSchedule.Sun, meetSlot = LifeRules.MorningSlot,
                raceCol = WorkSchedule.Sat, raceSlot = LifeRules.NoonSlot,
                venues = Classic, fits = c => c.modelYear > 0 && c.modelYear <= VintageLastYear },
            new ClubDef { id = "future", name = "FUTURE CLUB", shortName = "FUTURE", rule = "1999 MODELS",
                meetCol = WorkSchedule.Sat, meetSlot = LifeRules.NightSlot,
                raceCol = WorkSchedule.Fri, raceSlot = LifeRules.NightSlot,
                venues = Street, fits = c => c.modelYear >= FutureYear },
            new ClubDef { id = "elite", name = "ELITE CLUB", shortName = "ELITE", rule = "ANY CAR", elite = true,
                meetCol = WorkSchedule.Sun, meetSlot = LifeRules.NightSlot,
                raceCol = WorkSchedule.Sat, raceSlot = LifeRules.EveningSlot,
                venues = Hardest, fits = c => true },
        };

        /// <summary>
        /// THE PERKS, keyed by club and rank (owner: "perks for certain
        /// levels like access to mechanics that have high skill with certain
        /// cars, paint cost reduction for that club's approved car type, loaner
        /// cars"). PAINT and MECHANIC are live and apply to a car the club
        /// takes; LOANER is designed, not built (see the report).
        /// </summary>
        public static readonly ClubPerk[] Perks =
        {
            new ClubPerk("american", 7, PerkPaint, 0.15f, "15% OFF A RESPRAY, AMERICAN CARS"),
            new ClubPerk("american", 4, PerkMechanic, 0.20f, "CLUB MECHANIC: 20% OFF, +25% WORK, AMERICAN CARS"),
            new ClubPerk("american", 1, PerkLoaner, 0f, "A LOANER WHILE YOUR CAR IS IN THE SHOP", live: false),
            new ClubPerk("japanese", 7, PerkPaint, 0.15f, "15% OFF A RESPRAY, JAPANESE CARS"),
            new ClubPerk("japanese", 4, PerkMechanic, 0.20f, "CLUB MECHANIC: 20% OFF, +25% WORK, JAPANESE CARS"),
            new ClubPerk("japanese", 1, PerkLoaner, 0f, "A LOANER WHILE YOUR CAR IS IN THE SHOP", live: false),
            new ClubPerk("euro", 7, PerkPaint, 0.15f, "15% OFF A RESPRAY, EUROPEAN CARS"),
            new ClubPerk("euro", 4, PerkMechanic, 0.20f, "CLUB MECHANIC: 20% OFF, +25% WORK, EUROPEAN CARS"),
            new ClubPerk("euro", 1, PerkLoaner, 0f, "A LOANER WHILE YOUR CAR IS IN THE SHOP", live: false),
            new ClubPerk("vintage", 7, PerkPaint, 0.20f, "20% OFF A RESPRAY, 1979 OR OLDER"),
            new ClubPerk("vintage", 4, PerkMechanic, 0.25f, "CLUB MECHANIC: 25% OFF, +25% WORK, CLASSICS"),
            new ClubPerk("vintage", 1, PerkLoaner, 0f, "A LOANER WHILE YOUR CAR IS IN THE SHOP", live: false),
            new ClubPerk("future", 7, PerkPaint, 0.15f, "15% OFF A RESPRAY, 1999 MODELS"),
            new ClubPerk("future", 4, PerkMechanic, 0.20f, "CLUB MECHANIC: 20% OFF, +25% WORK, 1999 MODELS"),
            new ClubPerk("future", 1, PerkLoaner, 0f, "A LOANER WHILE YOUR CAR IS IN THE SHOP", live: false),
            new ClubPerk("elite", 10, PerkPaint, 0.25f, "25% OFF A RESPRAY, ANY CAR"),
            new ClubPerk("elite", 5, PerkMechanic, 0.30f, "CLUB MECHANIC: 30% OFF, +25% WORK, ANY CAR"),
            new ClubPerk("elite", 1, PerkLoaner, 0f, "A LOANER WHILE YOUR CAR IS IN THE SHOP", live: false),
        };

        /// <summary>Ten regulars per club, strongest first (the order a new
        /// board is dealt in and the order a club fields in a fixture). Nine
        /// of them stand on the player's board; the tenth races fixtures.</summary>
        static readonly string[] Names =
        {
            // american
            "BUCK", "DALE JR", "TINY", "RHONDA", "CLETUS", "BIG AL", "MOOSE", "SHERYL", "WADE", "HOSS",
            // japanese
            "KAZ", "MIYU", "TETSU", "RYO", "AKI", "SHUN", "NAO", "KENTA", "YUMI", "TAK",
            // euro
            "STEFAN", "NICO", "PIERRE", "INGRID", "MARCO B", "LARS", "GIA", "OLLIE", "HANS", "ELODIE",
            // vintage
            "POP", "LOU", "DOTTIE", "VERN", "MAE", "GUS", "HAROLD", "BEV", "CHUCK", "IRMA",
            // future
            "NEO", "JADE", "ZIP", "AXEL", "KIRA", "DASH", "VOLT", "ECHO", "SKY", "REX",
            // elite
            "THE DUKE", "CROWN", "SILK", "NOMAD", "VANTA", "ACE", "LEGEND", "MONARCH", "QUILL", "SABLE",
        };
        public const int RosterSize = 10;

        public static ClubDef ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in All) if (c.id == id) return c;
            return null;
        }

        public static int IndexOf(ClubDef c) => System.Array.IndexOf(All, c);

        // ---------------- standing ----------------

        public static ClubStanding StandingOf(LifeState s, ClubDef c)
        {
            if (s == null || c == null || s.clubs == null) return null;
            return s.clubs.Find(x => x != null && x.id == c.id);
        }

        static ClubStanding EnsureStanding(LifeState s, ClubDef c)
        {
            if (s.clubs == null) s.clubs = new List<ClubStanding>();
            var st = StandingOf(s, c);
            if (st == null) { st = new ClubStanding { id = c.id }; s.clubs.Add(st); }
            if (st.board == null) st.board = new List<RankEntry>();
            return st;
        }

        public static bool IsMember(LifeState s, ClubDef c)
        {
            var st = StandingOf(s, c);
            return st != null && st.member;
        }

        public static bool IsMember(LifeState s, string id) => IsMember(s, ById(id));

        /// <summary>Every club the player is in, in table order.</summary>
        public static List<ClubDef> Mine(LifeState s)
        {
            var list = new List<ClubDef>();
            foreach (var c in All) if (IsMember(s, c)) list.Add(c);
            return list;
        }

        public static bool Fits(ClubDef c, OwnedCar car) =>
            c != null && car != null && c.Fits(CarCatalog.Get(car.specId));

        /// <summary>Does the player own ANY car the club would take?</summary>
        public static bool OwnsQualifying(LifeState s, ClubDef c)
        {
            if (s == null || c == null || s.cars == null) return false;
            foreach (var car in s.cars) if (Fits(c, car)) return true;
            return false;
        }

        /// <summary>A club's regulars, strongest first.</summary>
        public static string[] Roster(ClubDef c)
        {
            int i = Mathf.Max(0, IndexOf(c));
            var r = new string[RosterSize];
            for (int k = 0; k < RosterSize; k++) r[k] = Names[(i * RosterSize + k) % Names.Length];
            return r;
        }

        static int RosterIndex(ClubDef c, string alias) => System.Array.IndexOf(Roster(c), alias);

        /// <summary>How good a member is: the blacklist's scale (0.84 a
        /// regular .. 1.00 the club's best), by where they were dealt. The
        /// elite are a notch above everybody.</summary>
        public static float Skill(ClubDef c, string alias)
        {
            int i = RosterIndex(c, alias);
            float s = i < 0 ? 0.88f : 1.00f - 0.016f * i;
            return c != null && c.elite ? s + 0.03f : s;
        }

        static int Hash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char ch in s ?? "") h = (h ^ ch) * 16777619;
                return h & 0x7fffffff;
            }
        }

        /// <summary>
        /// The car a member brings: a catalog car that QUALIFIES, the
        /// stronger members higher up the price list, seeded off the name so
        /// it is the same car every week.
        /// </summary>
        public static CarSpec MemberCar(ClubDef c, string alias)
        {
            if (c == null || !CarCatalog.Ready) return null;
            var pool = new List<CarSpec>();
            foreach (var spec in CarCatalog.All)
                if (c.Fits(spec) && !spec.IsRaceCar && !spec.IsRallyCar) pool.Add(spec);
            if (pool.Count == 0) foreach (var spec in CarCatalog.All) if (c.Fits(spec)) pool.Add(spec);
            if (pool.Count == 0) return null;
            pool.Sort((a, b) => a.price != b.price ? a.price.CompareTo(b.price) : string.CompareOrdinal(a.id, b.id));
            int i = Mathf.Max(0, RosterIndex(c, alias));
            var rng = new System.Random(Hash(c.id + ":" + alias));
            float top = c.elite ? 0.97f : 0.80f, bottom = c.elite ? 0.60f : 0.15f;
            float frac = Mathf.Lerp(top, bottom, i / (float)(RosterSize - 1)) +
                         ((float)rng.NextDouble() - 0.5f) * 0.12f;
            int idx = Mathf.Clamp(Mathf.RoundToInt(frac * (pool.Count - 1)), 0, pool.Count - 1);
            return pool[idx];
        }

        // ---------------- the calendar ----------------

        /// <summary>Weeks since the career's first Sunday-week, 0-based.</summary>
        public static int WeekIndex(int day) => (LifeRules.WeekStart(day) + 6) / 7;

        public static bool IsFixtureWeek(int day) => WeekIndex(day) % FixtureEveryWeeks == FixtureEveryWeeks - 1;

        /// <summary>The club a fixture is against, rotating through the others.</summary>
        public static ClubDef FixtureOpponent(ClubDef c, int day)
        {
            if (c == null) return null;
            var others = new List<ClubDef>();
            foreach (var o in All) if (o != c && (c.elite || !o.elite)) others.Add(o);
            if (others.Count == 0) return null;
            return others[(WeekIndex(day) / FixtureEveryWeeks) % others.Count];
        }

        /// <summary>The road a club race runs on, seeded by the week: the
        /// club's pool filtered to this build (the blacklist's PoolFor rule),
        /// the circuits if none of it ships.</summary>
        public static int RaceVenue(ClubDef c, int day)
        {
            if (c == null) return TrackCatalog.FirstOffered();
            var pool = new List<int>();
            foreach (var id in c.venues)
                if (TrackCatalog.TryIndexOfShipped(id, out int i) && TrackCatalog.Offered(i) && !pool.Contains(i))
                    pool.Add(i);
            if (pool.Count == 0)
                foreach (var id in Blacklist.PoolFor("oval"))
                    if (TrackCatalog.TryIndexOfShipped(id, out int i) && !pool.Contains(i)) pool.Add(i);
            if (pool.Count == 0) return TrackCatalog.FirstOffered();
            var rng = new System.Random(WeekIndex(day) * 7919 + IndexOf(c) * 131 + 7);
            return pool[rng.Next(pool.Count)];
        }

        /// <summary>Clubs whose meet is in this block (the lot may hold two).</summary>
        public static List<ClubDef> MeetingAt(int day, int slot)
        {
            var list = new List<ClubDef>();
            foreach (var c in All) if (c.MeetOn(day) && c.meetSlot == slot) list.Add(c);
            return list;
        }

        public static bool AnyMeetNow(LifeState s) => s != null && MeetingAt(s.day, s.slotIndex).Count > 0;

        // ---------------- the boards ----------------

        /// <summary>The player's board in a club, dealt if it is missing.
        /// Empty for a club they are not in.</summary>
        public static List<RankEntry> Board(LifeState s, ClubDef c)
        {
            var st = StandingOf(s, c);
            if (st == null || !st.member) return new List<RankEntry>();
            if (st.board == null) st.board = new List<RankEntry>();
            if (st.board.Count == 0) DealBoard(st, c);
            return st.board;
        }

        static void DealBoard(ClubStanding st, ClubDef c)
        {
            st.board.Clear();
            var r = Roster(c);
            for (int k = 0; k < BoardSize - 1 && k < r.Length; k++) st.board.Add(new RankEntry { alias = r[k] });
            st.board.Add(new RankEntry { alias = Blacklist.PlayerKey });
        }

        public static int RankOf(LifeState s, ClubDef c, string alias)
        {
            var b = Board(s, c);
            for (int i = 0; i < b.Count; i++) if (b[i] != null && b[i].alias == alias) return i + 1;
            return 0;
        }

        /// <summary>The player's position in a club, or 0 when not a member.</summary>
        public static int PlayerRank(LifeState s, ClubDef c) => RankOf(s, c, Blacklist.PlayerKey);

        static void Swap(List<RankEntry> b, int i, int j)
        {
            var t = b[i]; b[i] = b[j]; b[j] = t;
        }

        // ---------------- perks ----------------

        /// <summary>A club's perks, best-ranked last (the order they are earned).</summary>
        public static List<ClubPerk> PerksOf(ClubDef c)
        {
            var list = new List<ClubPerk>();
            foreach (var p in Perks) if (c != null && p.club == c.id) list.Add(p);
            list.Sort((a, b) => b.rank.CompareTo(a.rank));
            return list;
        }

        /// <summary>Earned now: a member standing at the perk's rank or better.</summary>
        public static bool HasPerk(LifeState s, ClubPerk p)
        {
            int r = PlayerRank(s, ById(p.club));
            return r > 0 && r <= p.rank;
        }

        /// <summary>The next perk the player has not reached in a club, or null.</summary>
        public static ClubPerk? NextPerk(LifeState s, ClubDef c)
        {
            foreach (var p in PerksOf(c)) if (!HasPerk(s, p)) return p;
            return null;
        }

        /// <summary>The best LIVE perk of a kind any of the player's clubs
        /// gives THIS car (it has to be a car that club takes): the fraction
        /// off, 0 for none.</summary>
        public static float BestPerk(LifeState s, OwnedCar car, string kind, out ClubDef from)
        {
            from = null;
            float best = 0f;
            if (s == null || car == null) return 0f;
            foreach (var p in Perks)
            {
                if (!p.live || p.kind != kind || p.value <= best) continue;
                var c = ById(p.club);
                if (!HasPerk(s, p) || !Fits(c, car)) continue;
                best = p.value; from = c;
            }
            return best;
        }

        /// <summary>The respray price after any club's PAINT perk.</summary>
        public static int PaintPrice(LifeState s, OwnedCar car, int price) =>
            Mathf.RoundToInt(price * (1f - BestPerk(s, car, PerkPaint, out _)));

        /// <summary>A mechanic's service price after any club's MECHANIC perk.</summary>
        public static int MechanicPrice(LifeState s, OwnedCar car, int price) =>
            Mathf.RoundToInt(price * (1f - BestPerk(s, car, PerkMechanic, out _)));

        /// <summary>The club mechanic's extra work: 1 + MechanicSkillBonus
        /// when a club's MECHANIC perk covers this car, 1 otherwise.</summary>
        public static float MechanicWork(LifeState s, OwnedCar car) =>
            BestPerk(s, car, PerkMechanic, out _) > 0f ? 1f + MechanicSkillBonus : 1f;

        // ---------------- membership ----------------

        /// <summary>Why the player cannot join this club right now, or null.
        /// Two doors: an INVITATION (accepted anywhere, with a car the club
        /// takes in the garage), or turning up at the club's MEET in one.
        /// The elite club has only the first.</summary>
        public static string JoinRefusal(LifeState s, ClubDef c)
        {
            if (s == null || c == null) return "nobody to join";
            var st = StandingOf(s, c);
            if (st != null && st.member) return "you are a member";
            if (st != null && st.readmitDay > 0 && s.day < st.readmitDay)
                return "they will have you back from " + LifeRules.DateLabel(st.readmitDay);
            if (st != null && st.invited)
                return OwnsQualifying(s, c) ? null : "you own no " + c.rule.ToLowerInvariant() + " car";
            if (c.elite) return "invitation only";
            if (!c.MeetOn(s.day) || s.slotIndex != c.meetSlot)
                return "you join at their meet - " + c.MeetWhen;
            var car = s.ActiveCar;
            if (car == null) return "you came on foot";
            if (!Fits(c, car))
                return "your " + LifeRules.ShortName(car).ToLowerInvariant() + " is not " +
                       (c.rule.StartsWith("1") ? "a " + c.rule.ToLowerInvariant() + " car" : c.rule.ToLowerInvariant());
            return null;
        }

        /// <summary>
        /// Ask the player in (the elite rule, or anything later that hands one
        /// out - the owner's intro idea). Accepted from the club page.
        /// </summary>
        public static void Invite(LifeState s, ClubDef c, string why)
        {
            if (s == null || c == null || IsMember(s, c)) return;
            var st = EnsureStanding(s, c);
            if (st.invited) return;
            st.invited = true;
            Duty.Notice(s, "AN INVITATION FROM THE " + c.name, why);
        }

        /// <summary>Sign up. At the meet the meet is attended; by invitation
        /// it is owed from the next block. The board is dealt with the player
        /// at the bottom. Returns the headline, or the refusal.</summary>
        public static string Join(LifeState s, ClubDef c)
        {
            string no = JoinRefusal(s, c);
            if (no != null) return no;
            var st = EnsureStanding(s, c);
            bool atMeet = c.MeetOn(s.day) && s.slotIndex == c.meetSlot;
            st.member = true;
            st.invited = false;
            st.readmitDay = 0;
            st.from = Duty.NowOrd(s) + (atMeet ? 0 : 1);
            st.board = new List<RankEntry>();
            DealBoard(st, c);
            if (atMeet) Duty.Attend(s, Duty.KindMeet, c.id);
            Duty.Notice(s, "WELCOME TO THE " + c.name,
                "Meets " + c.MeetWhen + ", club race " + c.RaceWhen + ". " + c.rule +
                " cars only. Miss " + MissesToDrop + " in " + MissWindowDays + " days and you are out.");
            Post(s, s.day, "THE " + c.shortName + " CLUB SIGNS A NEW NAME AT #" + BoardSize);
            return "YOU ARE IN THE " + c.name + " - #" + PlayerRank(s, c) + " OF " + BoardSize;
        }

        /// <summary>Hand the card back. The same 14 days as a drop before
        /// they will have you back - walking out is not a way round a strike.</summary>
        public static string Leave(LifeState s, ClubDef c)
        {
            if (!IsMember(s, c)) return null;
            Out(s, c, "You handed your " + c.name.ToLowerInvariant() + " card back.");
            return "OUT OF THE " + c.name;
        }

        static void Out(LifeState s, ClubDef c, string why)
        {
            var st = EnsureStanding(s, c);
            st.member = false;
            st.invited = false;
            st.readmitDay = s.day + ReadmitDays;
            st.board = new List<RankEntry>();
            Duty.Notice(s, "OUT OF THE " + c.name, why + " They will have you back from " +
                LifeRules.DateLabel(st.readmitDay) + ", at the bottom.");
        }

        /// <summary>Club misses counted against the player in one club.</summary>
        public static int MissesInWindow(LifeState s, ClubDef c, int day)
        {
            var st = StandingOf(s, c);
            if (st == null || !st.member || s.dutyLog == null) return 0;
            int n = 0;
            foreach (var r in s.dutyLog)
                if (r != null && r.kind != Duty.KindShift && r.club == c.id && r.IsMiss &&
                    r.day > day - MissWindowDays && r.day <= day && Duty.Ord(r.day, r.slot) >= st.from)
                    n++;
            return n;
        }

        /// <summary>A club obligation was just recorded as missed.</summary>
        public static void OnClubMiss(LifeState s, DutyRecord rec)
        {
            var c = ById(rec != null ? rec.club : null);
            if (c == null || !IsMember(s, c)) return;
            int n = MissesInWindow(s, c, rec.day);
            string what = rec.kind == Duty.KindMeet ? "the club meet" : "the club race";
            string why = rec.outcome == Duty.WrongCar ? " in the wrong car"
                       : rec.outcome == Duty.NoCar ? " (you own no " + c.rule.ToLowerInvariant() + " car)" : "";
            if (n >= MissesToDrop)
            {
                Out(s, c, "Dropped: " + n + " misses in " + MissWindowDays + " days, the last " + what + why + ".");
                Post(s, s.day, "THE " + c.shortName + " CLUB DROPS A NAME");
                return;
            }
            Duty.Notice(s, c.name + " - STRIKE " + n + " OF " + MissesToDrop,
                "You missed " + what + why + ". One more inside " + MissWindowDays + " days and you are out.");
        }

        /// <summary>Signed in at the lot: every one of the player's clubs whose
        /// meet is on now is attended - in a car that club takes - or missed in
        /// one it does not. Two clubs meeting at once and one car is the
        /// loyalty test. Returns the line for the town's cue slot, or null.</summary>
        public static string ArrivedAtLot(LifeState s)
        {
            if (s == null) return null;
            var lines = new List<string>();
            foreach (var c in MeetingAt(s.day, s.slotIndex))
            {
                if (!IsMember(s, c)) continue;
                var rec = Duty.Attend(s, Duty.KindMeet, c.id);
                if (rec == null) continue;
                lines.Add(rec.outcome == Duty.Done ? c.shortName + " MEET - SIGNED IN"
                                                   : c.shortName + " MEET - WRONG CAR, A MISS");
            }
            return lines.Count == 0 ? null : string.Join("  ·  ", lines.ToArray());
        }

        // ---------------- the club race ----------------

        /// <summary>Who the player races in a club race on this day.</summary>
        public struct Opponent
        {
            public string alias;
            public CarSpec spec;
            public float skill;
            /// <summary>Set for a fixture: the club they race for.</summary>
            public ClubDef club;
            /// <summary>Their rank: on the player's board, or in their club.</summary>
            public int rank;
            public bool Valid => !string.IsNullOrEmpty(alias);
        }

        /// <summary>
        /// The player's opponent in a club's race: in an ordinary week the
        /// member directly ABOVE them (the one to pass), or #2 when the player
        /// is #1; in a fixture week the opposite number in the other club.
        /// </summary>
        public static Opponent RaceOpponent(LifeState s, ClubDef c, int day)
        {
            var o = new Opponent();
            if (!IsMember(s, c)) return o;
            int r = PlayerRank(s, c);
            if (IsFixtureWeek(day))
            {
                var other = FixtureOpponent(c, day);
                var roster = Roster(other);
                int k = Mathf.Clamp(r - 1, 0, roster.Length - 1);
                o.alias = roster[k]; o.club = other; o.rank = k + 1;
                o.spec = MemberCar(other, o.alias); o.skill = Skill(other, o.alias);
                return o;
            }
            var b = Board(s, c);
            int idx = r <= 1 ? 1 : r - 2;
            if (idx < 0 || idx >= b.Count) return o;
            o.alias = b[idx].alias; o.rank = idx + 1;
            o.spec = MemberCar(c, o.alias); o.skill = Skill(c, o.alias);
            return o;
        }

        /// <summary>The player's club races in the block the clock is in, not
        /// yet attended or missed. Two at once is a clash the player settles
        /// by running one.</summary>
        public static List<ClubDef> RacesDueNow(LifeState s)
        {
            var list = new List<ClubDef>();
            if (s == null) return list;
            foreach (var c in Mine(s))
            {
                if (!c.RaceOn(s.day) || s.slotIndex != c.raceSlot) continue;
                if (Duty.Ord(s.day, s.slotIndex) < StandingOf(s, c).from) continue;
                if (Duty.RecordFor(s, s.day, c.raceSlot, Duty.KindRace, c.id) != null) continue;
                list.Add(c);
            }
            return list;
        }

        /// <summary>The club race the pre-race page launches now, or null.</summary>
        public static ClubDef RaceDueNow(LifeState s)
        {
            var due = RacesDueNow(s);
            return due.Count > 0 ? due[0] : null;
        }

        /// <summary>Why START will not run this club's race in this car, or null.</summary>
        public static string RaceCarRefusal(LifeState s, ClubDef c)
        {
            if (c == null) return "no club";
            if (!Fits(c, s.ActiveCar))
                return "WRONG CAR - THE " + c.shortName + " CLUB RUNS " + c.rule;
            return null;
        }

        /// <summary>Committed at the start line: attended, in this car.</summary>
        public static DutyRecord BeginRace(LifeState s, ClubDef c) =>
            c == null ? null : Duty.Attend(s, Duty.KindRace, c.id);

        public static int PurseFor(int day) => IsFixtureWeek(day) ? FixturePurse : RacePurse;

        /// <summary>
        /// Bank the player's club race. Called BEFORE the apply-back spends
        /// the block, so a NIGHT race is on the board before the rollover reads
        /// it for the fixture. In an ordinary week the board moves on the one
        /// rule: finish ahead of the member above you and you swap.
        /// </summary>
        public static string RecordRaceResult(LifeState s, ClubDef c, int day, string alias, bool won)
        {
            if (!IsMember(s, c)) return null;
            var rec = Duty.RecordFor(s, day, c.raceSlot, Duty.KindRace, c.id);
            if (rec != null) rec.result = won ? "WON" : "LOST";
            if (IsFixtureWeek(day))
            {
                var other = FixtureOpponent(c, day);
                return (won ? "BEAT " : "LOST TO ") + alias + " (" + (other != null ? other.shortName : "") +
                       ") - FIXTURE POINT " + (won ? "FOR" : "AGAINST") + " THE " + c.shortName + "S";
            }
            var b = Board(s, c);
            int me = PlayerRank(s, c), them = RankOf(s, c, alias);
            if (me <= 0 || them <= 0) return (won ? "BEAT " : "LOST TO ") + alias;
            string line;
            if (won && them == me - 1)
            {
                Swap(b, me - 1, them - 1);
                line = "YOU TAKE #" + them + " IN THE " + c.shortName + " CLUB OFF " + alias;
            }
            else if (!won && me == 1 && them == 2)
            {
                Swap(b, 0, 1);
                line = alias + " TAKES #1 IN THE " + c.shortName + " CLUB OFF YOU";
            }
            else line = (won ? "BEAT " : "LOST TO ") + alias + " - YOU STAY #" + me + " (" + c.shortName + ")";
            var you = b[PlayerRank(s, c) - 1];
            var opp = b[RankOf(s, c, alias) - 1];
            if (won) { you.wins++; opp.losses++; } else { you.losses++; opp.wins++; }
            Post(s, day, line);
            return line;
        }

        /// <summary>
        /// The rollover's part, for the day that just closed: in each of the
        /// player's clubs that raced it, the rest of the board races its
        /// neighbours (seeded odds, the blacklist's LegOdds) - or, in a
        /// fixture week, the pairs are scored into the news. Then the elite
        /// invitation is checked.
        /// </summary>
        public static void Tick(LifeState s)
        {
            if (s == null) return;
            int day = s.day - 1;
            foreach (var c in Mine(s))
            {
                if (!c.RaceOn(day) || Duty.Ord(day, c.raceSlot) < StandingOf(s, c).from) continue;
                var opp = RaceOpponent(s, c, day);
                if (IsFixtureWeek(day)) ScoreFixture(s, c, day);
                else Churn(s, c, day, opp.alias);
            }
            CheckEliteInvite(s);
        }

        static void Churn(LifeState s, ClubDef c, int day, string playersOpponent)
        {
            var b = Board(s, c);
            var rng = new System.Random(day * 7919 + IndexOf(c) * 131 + 17);
            for (int i = 1; i < b.Count; i++)
            {
                var up = b[i - 1]; var low = b[i];
                if (up.IsPlayer || low.IsPlayer || up.alias == playersOpponent || low.alias == playersOpponent)
                    continue;
                float p = Blacklist.LegOdds(Skill(c, low.alias), Skill(c, up.alias));
                if (rng.NextDouble() < p)
                {
                    low.wins++; up.losses++;
                    Swap(b, i - 1, i);
                    Post(s, day, low.alias + " PASSES " + up.alias + " FOR #" + i + " (" + c.shortName + ")");
                    i++;   // a driver moves one place a week
                }
                else { up.wins++; low.losses++; }
            }
        }

        /// <summary>The fixture's pairs, summed. Public for the self-test.</summary>
        public static (int us, int them) ScoreFixture(LifeState s, ClubDef c, int day)
        {
            var other = FixtureOpponent(c, day);
            var b = Board(s, c);
            var theirs = Roster(other);
            var rec = Duty.RecordFor(s, day, c.raceSlot, Duty.KindRace, c.id);
            bool playerWon = rec != null && rec.result == "WON";
            var rng = new System.Random(day * 7919 + IndexOf(c) * 131 + IndexOf(other) * 17 + 3);
            int us = 0, them = 0;
            for (int r = 0; r < b.Count && r < theirs.Length; r++)
            {
                bool win;
                if (b[r].IsPlayer) win = playerWon;
                else win = rng.NextDouble() < Blacklist.LegOdds(Skill(c, b[r].alias), Skill(other, theirs[r]));
                if (win) us++; else them++;
            }
            string line = c.shortName + " " + us + " - " + them + " " + other.shortName +
                          (playerWon ? " (YOU WON YOUR PAIR)" : " (YOU LOST YOUR PAIR)");
            Post(s, day, line);
            s.calendarLog.Add(LifeRules.LogDate(day) + ": club fixture " + line.ToLowerInvariant());
            return (us, them);
        }

        // ---------------- the elite club ----------------

        /// <summary>Why the elite club has not asked yet, or null when the
        /// rule is met: #<see cref="EliteEveryClubRank"/> or better in EVERY
        /// ordinary club, or #<see cref="EliteOneClubRank"/> in any one.</summary>
        public static string EliteInviteRefusal(LifeState s)
        {
            bool every = true, one = false;
            foreach (var c in All)
            {
                if (c.elite) continue;
                int r = PlayerRank(s, c);
                if (r <= 0 || r > EliteEveryClubRank) every = false;
                if (r > 0 && r <= EliteOneClubRank) one = true;
            }
            return every || one ? null
                : "#" + EliteOneClubRank + " in one club, or #" + EliteEveryClubRank + " or better in every club";
        }

        static void CheckEliteInvite(LifeState s)
        {
            foreach (var c in All)
            {
                if (!c.elite || IsMember(s, c)) continue;
                var st = StandingOf(s, c);
                if (st != null && (st.invited || (st.readmitDay > 0 && s.day < st.readmitDay))) continue;
                if (EliteInviteRefusal(s) != null) continue;
                Invite(s, c, "Word got round. The " + c.name.ToLowerInvariant() + " meets " + c.MeetWhen +
                             " and races " + c.RaceWhen + ". Accept on the CLUBS page.");
            }
        }

        /// <summary>A line on the club news strip, dated by the day it happened.</summary>
        public static void Post(LifeState s, int day, string line)
        {
            if (s.clubNews == null) s.clubNews = new List<string>();
            s.clubNews.Add(LifeRules.LogDate(Mathf.Max(1, day)) + ": " + line);
            while (s.clubNews.Count > NewsKeep) s.clubNews.RemoveAt(0);
        }
    }

    /// <summary>
    /// The obligations of the week - shifts, club meets, club races - as the
    /// calendar shows them, and their settling as each block passes. A block
    /// owed and not attended is a MISS, recorded once, and the strike goes to
    /// whoever was owed (WorkSchedule or the club). Two obligations in one
    /// block is a CONFLICT: the player keeps one and the other is a miss.
    /// </summary>
    public static class Duty
    {
        public const string KindShift = "SHIFT", KindMeet = "CLUB MEET", KindRace = "CLUB RACE";
        public const string Done = "DONE", Missed = "MISSED", WrongCar = "WRONG CAR", NoCar = "NO CAR";

        public struct Obligation
        {
            public string kind;
            public int day, slot, span;
            public string club;
            public bool Covers(int d, int sl) => d == day && sl >= slot && sl < slot + span;
        }

        /// <summary>A block as one number: day x 6 + block.</summary>
        public static int Ord(int day, int slot) => day * LifeRules.SlotCount + slot;
        public static int NowOrd(LifeState s) => s == null ? 0 : Ord(s.day, s.slotIndex);

        /// <summary>
        /// The obligations that START on a day, by the player's standing now:
        /// the shift while on the roster, each club's meet and race while a
        /// member - each only from the block it became owed (the v23
        /// migration's clock, a call-back, the block the player joined in).
        /// </summary>
        public static List<Obligation> StartingOn(LifeState s, int day)
        {
            var list = new List<Obligation>();
            if (s == null || day < 1) return list;
            if (WorkSchedule.OnRoster(s))
            {
                int st = WorkSchedule.ShiftStartOn(s.playerJob, day);
                if (st >= 0 && Ord(day, st) >= s.jobFrom)
                    list.Add(new Obligation { kind = KindShift, day = day, slot = st, span = WorkSchedule.SpanFrom(st), club = "" });
            }
            foreach (var c in Clubs.Mine(s))
            {
                int from = Clubs.StandingOf(s, c).from;
                if (c.MeetOn(day) && Ord(day, c.meetSlot) >= from)
                    list.Add(new Obligation { kind = KindMeet, day = day, slot = c.meetSlot, span = 1, club = c.id });
                if (c.RaceOn(day) && Ord(day, c.raceSlot) >= from)
                    list.Add(new Obligation { kind = KindRace, day = day, slot = c.raceSlot, span = 1, club = c.id });
            }
            list.RemoveAll(o => Ord(o.day, o.slot) < s.dutyFrom);
            list.Sort((a, b) => a.slot.CompareTo(b.slot));
            return list;
        }

        /// <summary>The obligations that cover one block.</summary>
        public static List<Obligation> At(LifeState s, int day, int slot)
        {
            var list = StartingOn(s, day);
            list.RemoveAll(o => !o.Covers(day, slot));
            return list;
        }

        /// <summary>Two obligations in one block: only one can be kept.</summary>
        public static bool ConflictAt(LifeState s, int day, int slot) => At(s, day, slot).Count >= 2;

        /// <summary>The settled record for an obligation, or null.</summary>
        public static DutyRecord RecordFor(LifeState s, int day, int slot, string kind, string club = "")
        {
            if (s == null || s.dutyLog == null) return null;
            club = club ?? "";
            return s.dutyLog.Find(r => r != null && r.day == day && r.slot == slot && r.kind == kind &&
                                       (r.club ?? "") == club);
        }

        public static DutyRecord RecordFor(LifeState s, Obligation o) => RecordFor(s, o.day, o.slot, o.kind, o.club);

        /// <summary>The record whose obligation covers a block (a shift's
        /// second block finds the shift).</summary>
        public static DutyRecord RecordAt(LifeState s, int day, int slot, string kind = null)
        {
            var all = RecordsAt(s, day, slot);
            foreach (var r in all) if (kind == null || r.kind == kind) return r;
            return null;
        }

        /// <summary>Every record covering a block, misses first.</summary>
        public static List<DutyRecord> RecordsAt(LifeState s, int day, int slot)
        {
            var list = new List<DutyRecord>();
            if (s == null || s.dutyLog == null) return list;
            foreach (var r in s.dutyLog)
            {
                if (r == null || r.day != day) continue;
                int span = r.kind == KindShift ? WorkSchedule.SpanFrom(r.slot) : 1;
                if (slot >= r.slot && slot < r.slot + span) list.Add(r);
            }
            list.Sort((a, b) => (b.IsMiss ? 1 : 0).CompareTo(a.IsMiss ? 1 : 0));
            return list;
        }

        /// <summary>Any obligation on this day was missed.</summary>
        public static bool MissedOn(LifeState s, int day) =>
            s != null && s.dutyLog != null && s.dutyLog.Exists(r => r != null && r.day == day && r.IsMiss);

        static DutyRecord Record(LifeState s, Obligation o, string outcome, string note)
        {
            if (s.dutyLog == null) s.dutyLog = new List<DutyRecord>();
            var rec = new DutyRecord
            {
                day = o.day, slot = o.slot, kind = o.kind, outcome = outcome,
                club = o.club ?? "", note = note ?? "",
            };
            s.dutyLog.Add(rec);
            if (rec.IsMiss)
            {
                s.calendarLog.Add(LifeRules.LogDate(o.day) + ": " + MissLine(rec).ToLowerInvariant());
                if (o.kind == KindShift) WorkSchedule.OnShiftMissed(s, rec);
                else Clubs.OnClubMiss(s, rec);
            }
            return rec;
        }

        /// <summary>
        /// The player turned up for one club's meet or race in the block the
        /// clock is in: the meet lot, the start line. In a car that club takes
        /// it is DONE; in any other it is the owner's rule - "considered
        /// missing event even if showing up with wrong car". The first record
        /// stands: a wrong car cannot be swapped for a right one in the same
        /// block. Null when nothing of that kind is owed to that club now.
        /// </summary>
        public static DutyRecord Attend(LifeState s, string kind, string club)
        {
            if (s == null) return null;
            foreach (var o in At(s, s.day, s.slotIndex))
            {
                if (o.kind != kind || o.slot != s.slotIndex || o.club != club) continue;
                var had = RecordFor(s, o);
                if (had != null) return had;
                var c = Clubs.ById(o.club);
                bool ok = Clubs.Fits(c, s.ActiveCar);
                return Record(s, o, ok ? Done : WrongCar,
                    ok ? "" : "came in a " + LifeRules.ShortName(s.ActiveCar).ToLowerInvariant() +
                              " - the club runs " + c.rule.ToLowerInvariant());
            }
            return null;
        }

        static bool Worked(LifeState s, Obligation o)
        {
            for (int k = o.slot; k < o.slot + o.span; k++)
                if (LifeRules.SlotAct(s, o.day, k) == LifeRules.ActWork) return true;
            return false;
        }

        /// <summary>
        /// Settle every obligation whose blocks are all behind the clock and
        /// that has no record yet: a shift with a WORK block in it is done,
        /// anything else is a miss - and a club event the player owns no
        /// qualifying car for is a miss that says so. Called whenever the
        /// clock moves (SpendActivitySlot, Sleep, the rollover). Looks back a
        /// week at most, so a long-closed day is never re-judged.
        /// </summary>
        public static void Settle(LifeState s)
        {
            if (s == null) return;
            int now = NowOrd(s);
            int firstDay = Mathf.Max(1, Mathf.Max(s.day - 7, s.dutyFrom / LifeRules.SlotCount));
            for (int day = firstDay; day <= s.day; day++)
            {
                foreach (var o in StartingOn(s, day))
                {
                    if (Ord(o.day, o.slot + o.span) > now) continue;
                    if (RecordFor(s, o) != null) continue;
                    if (o.kind == KindShift)
                    {
                        // A lay-off inside this loop ends what is owed.
                        if (!WorkSchedule.OnRoster(s)) continue;
                        Record(s, o, Worked(s, o) ? Done : Missed, "");
                        continue;
                    }
                    // A drop inside this loop ends what is owed to THAT club.
                    if (!Clubs.IsMember(s, o.club)) continue;
                    var c = Clubs.ById(o.club);
                    bool owns = Clubs.OwnsQualifying(s, c);
                    Record(s, o, owns ? Missed : NoCar,
                        owns ? "" : "you own no " + c.rule.ToLowerInvariant() + " car");
                }
            }
            if (s.dutyLog != null)
                s.dutyLog.RemoveAll(r => r == null || r.day < s.day - LifeRules.DayLogKeep);
        }

        // ---------------- words ----------------

        /// <summary>One obligation as the calendar names it: "SHIFT",
        /// "JAPANESE MEET", "FUTURE RACE".</summary>
        public static string Word(Obligation o)
        {
            if (o.kind == KindShift) return "SHIFT";
            var c = Clubs.ById(o.club);
            return (c != null ? c.shortName + " " : "") + (o.kind == KindMeet ? "MEET" : "RACE");
        }

        /// <summary>A miss as the calendar says it: "SHIFT MISSED",
        /// "JAPANESE MEET - WRONG CAR", "FUTURE RACE - NO CAR".</summary>
        public static string MissLine(DutyRecord r)
        {
            if (r == null) return "";
            var c = Clubs.ById(r.club);
            string what = r.kind == KindShift ? "SHIFT"
                        : (c != null ? c.shortName + " " : "") + (r.kind == KindMeet ? "MEET" : "RACE");
            return r.outcome == Missed ? what + " MISSED" : what + " - " + r.outcome;
        }

        /// <summary>Mail, the diary line and the pager, at once. There is no
        /// mail PAGE in the game: the line in RECENTLY and the pager toast are
        /// how a player reads it.</summary>
        public static void Notice(LifeState s, string subject, string body)
        {
            if (s == null) return;
            if (s.mail == null) s.mail = new List<MailItem>();
            s.mail.Add(new MailItem { day = s.day, subject = subject, body = body, read = false });
            int notices = 0;
            for (int i = s.mail.Count - 1; i >= 0; i--)
            {
                var m = s.mail[i];
                if (m == null || m.expiresDay != 0 || !IsNotice(m.subject)) continue;
                if (++notices > 12) s.mail.RemoveAt(i);
            }
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + subject.ToLowerInvariant());
            lastNotice = string.IsNullOrEmpty(lastNotice) ? subject : lastNotice + "  ·  " + subject;
        }

        static bool IsNotice(string subject) =>
            !string.IsNullOrEmpty(subject) &&
            (subject.Contains("TONY'S") || subject.Contains("CLUB") || subject.StartsWith("WARNING") ||
             subject.StartsWith("LAID OFF") || subject.StartsWith("OUT OF THE") || subject.StartsWith("WELCOME") ||
             subject.StartsWith("AN INVITATION"));

        /// <summary>The notices since the home screen last toasted, read once.</summary>
        public static string lastNotice;

        /// <summary>What is owed in the block the clock is in and not yet
        /// done, as a short line ("SHIFT AT TONY'S", "JAPANESE MEET + FUTURE
        /// MEET"), or null.</summary>
        public static string RequiredNow(LifeState s)
        {
            if (s == null) return null;
            var words = new List<string>();
            foreach (var o in At(s, s.day, s.slotIndex))
            {
                if (RecordFor(s, o) != null) continue;
                if (o.kind == KindShift && s.workedToday) continue;
                words.Add(o.kind == KindShift ? "SHIFT AT TONY'S" : Word(o));
            }
            return words.Count == 0 ? null : string.Join(" + ", words.ToArray());
        }
    }
}
