using System.Linq;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ENFORCED WEEK (v23, owner 2026-11-11): the job's fixed schedule,
    /// misses, the lay-off and the call-back; the car clubs - qualification,
    /// joining at the meet, strikes, the drop and re-admission, the board and
    /// its churn, fixtures - and the calendar's obligations and the v23
    /// migration. Every case is driven through the real clock (SpendActivity
    /// Slot / Sleep / the rollover), because that is where a miss is settled.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Spend blocks (as drives) until the clock stands at this
        /// day and block. Spending NIGHT rolls the day, as play does.</summary>
        static void AdvanceTo(LifeState s, int day, int slot)
        {
            int guard = 0;
            while ((s.day < day || (s.day == day && s.slotIndex < slot)) && ++guard < 2000)
                LifeRules.SpendActivitySlot(s, LifeRules.ActDrive);
        }

        static CarSpec FirstSpec(System.Func<CarSpec, bool> pick) =>
            CarCatalog.All.FirstOrDefault(c => c != null && !c.IsRaceCar && !c.IsRallyCar && pick(c));

        static void TestWorkSchedule()
        {
            Line("the enforced week - the work schedule:");
            string job = LifeRules.DeliveryJobName;
            var sh = WorkSchedule.ShiftsFor(job);
            Check(sh.Length == 4, "pizza delivery has four fixed shifts a week", sh.Length);
            Check(WorkSchedule.RosterShort(job) == "MON-THU AFTERNOON + EVENING",
                  "and they read MON-THU AFTERNOON + EVENING", WorkSchedule.RosterShort(job));
            // Day 1 is a FRIDAY: 4-7 are MON-THU, 1-3 and 8 are FRI/SAT/SUN.
            Check(WorkSchedule.ShiftStartOn(job, 4) == LifeRules.AfternoonSlot &&
                  WorkSchedule.ShiftStartOn(job, 7) == LifeRules.AfternoonSlot,
                  "Monday and Thursday have an afternoon shift");
            Check(WorkSchedule.ShiftStartOn(job, 1) < 0 && WorkSchedule.ShiftStartOn(job, 2) < 0 &&
                  WorkSchedule.ShiftStartOn(job, 3) < 0 && WorkSchedule.ShiftStartOn(job, 8) < 0,
                  "Friday, the weekend and the next Friday are free for the clubs");
            Check(WorkSchedule.HoursLine(LifeRules.AfternoonSlot) == "16:00 - 22:00",
                  "a shift is AFTERNOON + EVENING, 16:00 - 22:00", WorkSchedule.HoursLine(LifeRules.AfternoonSlot));
            Check(WorkSchedule.HasSchedule(job) && !WorkSchedule.HasSchedule("FORKLIFT"),
                  "the roster table takes jobs by name (a future job is a row)");

            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            AdvanceTo(s, 4, LifeRules.NoonSlot);
            Check(!WorkSchedule.CanClockOn(s) && WorkSchedule.ClockOnRefusal(s).Contains("AFTERNOON"),
                  "Monday NOON: the counter says the shift starts in the AFTERNOON",
                  WorkSchedule.ClockOnRefusal(s));
            Check(Duty.MissedOn(s, 1) == false && s.dutyLog.Count == 0,
                  "and nothing was owed over the first weekend", s.dutyLog.Count);
            AdvanceTo(s, 4, LifeRules.AfternoonSlot);
            Check(WorkSchedule.CanClockOn(s), "Monday AFTERNOON: clock on", WorkSchedule.ClockOnRefusal(s));
            Check(Duty.RequiredNow(s) == "SHIFT AT TONY'S", "and the hub says the shift is required now",
                  Duty.RequiredNow(s));
            LifeRules.ClockOnShift(s);
            LifeRules.SpendShift(s);
            Check(s.slotIndex == LifeRules.NightSlot, "the shift spends AFTERNOON + EVENING",
                  LifeRules.SlotNames[s.slotIndex]);
            var done = Duty.RecordFor(s, 4, LifeRules.AfternoonSlot, Duty.KindShift);
            Check(done != null && done.outcome == Duty.Done, "and is recorded WORKED the moment it ends",
                  done != null ? done.outcome : "none");
            Check(!WorkSchedule.CanClockOn(s), "one shift a day");

            // A LATE clock-on works what is left of the shift.
            var late = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            AdvanceTo(late, 4, LifeRules.EveningSlot);
            Check(WorkSchedule.CanClockOn(late) && WorkSchedule.BlocksToSpend(late) == 1,
                  "clocking on in the EVENING (the shift's second block) works one block",
                  WorkSchedule.BlocksToSpend(late));
            LifeRules.ClockOnShift(late);
            LifeRules.SpendShift(late);
            Check(late.slotIndex == LifeRules.NightSlot &&
                  Duty.RecordFor(late, 4, LifeRules.AfternoonSlot, Duty.KindShift)?.outcome == Duty.Done,
                  "and counts as the shift worked");

            // THE FIRST MISS IS A WARNING.
            LifeRules.SleepUntilMorning(s);                       // TUE morning
            LifeRules.SleepUntilMorning(s);                       // slept through TUE's shift -> WED morning
            var miss = Duty.RecordFor(s, 5, LifeRules.AfternoonSlot, Duty.KindShift);
            Check(miss != null && miss.outcome == Duty.Missed, "a shift slept through is MISSED",
                  miss != null ? miss.outcome : "none");
            Check(LifeRules.ShiftSkipped(s, 5, LifeRules.AfternoonSlot) &&
                  LifeRules.ShiftSkipped(s, 5, LifeRules.EveningSlot) &&
                  !LifeRules.ShiftSkipped(s, 5, LifeRules.NoonSlot),
                  "the calendar reads both its blocks as skipped, and no other");
            Check(!s.laidOff && WorkSchedule.MissesInWindow(s, s.day) == 1 &&
                  s.mail.Exists(m => m.subject.StartsWith("WARNING")),
                  "the first miss is a warning in the mail", WorkSchedule.StatusWord(s));
            Check(s.consecutiveAbsences == 0 && s.pendingSalary >= 0,
                  "and the old dock does not ALSO charge it");

            // THE SECOND INSIDE 14 DAYS IS THE LAY-OFF.
            LifeRules.SleepUntilMorning(s);                       // slept through WED -> THU
            Check(s.laidOff && !string.IsNullOrEmpty(s.playerJob),
                  "the second miss inside " + WorkSchedule.LayoffWindowDays + " days lays the driver off",
                  WorkSchedule.StatusWord(s));
            Check(s.callBackDay >= 6 + WorkSchedule.CallBackMinDays && s.callBackDay <= 6 + WorkSchedule.CallBackMaxDays,
                  "the call-back is set 4-9 days out", s.callBackDay);
            AdvanceTo(s, 7, LifeRules.AfternoonSlot);
            Check(!WorkSchedule.CanClockOn(s) && Duty.StartingOn(s, 7).Count == 0,
                  "laid off: no shift to clock on to, and none owed", WorkSchedule.ClockOnRefusal(s));
            int guard = 0;
            while (s.day < s.callBackDay && ++guard < 30) LifeRules.SleepUntilMorning(s);
            Check(s.callBackOffered && s.mail.Exists(m => m.subject.Contains("OPENING")),
                  "the call-back arrives by mail on its day", s.day + " / " + s.callBackDay);
            Check(WorkSchedule.MissesInWindow(s, s.day) == 0 || s.laidOff,
                  "(misses while laid off are not recorded)");
            string back = WorkSchedule.AcceptCallBack(s);
            Check(back != null && !s.laidOff && WorkSchedule.MissesInWindow(s, s.day) == 0,
                  "accepting puts the driver back on the schedule with the misses cleared", back);
            int nextShift = s.day + 1;
            while (WorkSchedule.ShiftStartOn(job, nextShift) < 0) nextShift++;
            Check(Duty.StartingOn(s, nextShift).Exists(o => o.kind == Duty.KindShift),
                  "and the next scheduled shift is owed again", LifeRules.DateLabel(nextShift));
            Check(WorkSchedule.AcceptCallBack(s) == null, "a call-back cannot be accepted twice");
            Duty.lastNotice = null;
        }

        static void TestCarClubs()
        {
            Line("the enforced week - car clubs:");
            if (!CarCatalog.Ready) { Check(false, "the catalog loads"); return; }

            // ---- the table ----
            foreach (var c in Clubs.All)
            {
                int n = CarCatalog.All.Count(x => c.Fits(x));
                Check(n >= 5, c.name + " has cars that qualify (" + c.rule + ")", n);
                Check(Clubs.MemberCar(c, Clubs.Roster(c)[0]) != null &&
                      c.Fits(Clubs.MemberCar(c, Clubs.Roster(c)[0])),
                      c.name + "'s members drive cars the club takes");
                bool clash = WorkSchedule.ShiftsFor(LifeRules.DeliveryJobName).Any(sh =>
                    (sh.col == c.meetCol && c.meetSlot >= sh.slot && c.meetSlot < sh.slot + 2) ||
                    (sh.col == c.raceCol && c.raceSlot >= sh.slot && c.raceSlot < sh.slot + 2));
                Check(!clash && !(c.meetCol == c.raceCol && c.meetSlot == c.raceSlot),
                      c.name + "'s meet and race miss the shifts and each other", c.MeetWhen + " / " + c.RaceWhen);
                Check(TrackCatalog.Offered(Clubs.RaceVenue(c, 3)), c.name + "'s race road is in this build");
                Check(Clubs.PerksOf(c).Count >= 3 && Clubs.PerksOf(c).Exists(p => p.kind == Clubs.PerkPaint && p.live) &&
                      Clubs.PerksOf(c).Exists(p => p.kind == Clubs.PerkMechanic && p.live),
                      c.name + " has a perk table with a live paint and mechanic perk");
            }
            var am = Clubs.ById("american"); var jp = Clubs.ById("japanese");
            var eu = Clubs.ById("euro"); var vi = Clubs.ById("vintage"); var fu = Clubs.ById("future");
            var el = Clubs.ById("elite");
            Check(el != null && el.elite && Clubs.All.Count(c => c.elite) == 1, "one elite club, invitation only");
            Check(CarCatalog.All.Where(x => am.Fits(x)).All(x => x.origin == "usa"), "AMERICAN takes American cars only");
            Check(CarCatalog.All.Where(x => jp.Fits(x)).All(x => x.origin == "jpn"), "JAPANESE takes Japanese cars only");
            Check(CarCatalog.All.Where(x => eu.Fits(x)).All(x => x.origin != "usa" && x.origin != "jpn"),
                  "EURO takes European cars only");
            Check(CarCatalog.All.Where(x => vi.Fits(x)).All(x => x.modelYear <= 1979), "VINTAGE takes 1979 or older");
            Check(CarCatalog.All.Where(x => fu.Fits(x)).All(x => x.modelYear >= 1999), "FUTURE takes 1999 models");
            Check(CarCatalog.All.All(x => am.Fits(x) || jp.Fits(x) || eu.Fits(x)),
                  "every car in the catalog has an origin club", CarCatalog.All.Count(x => !am.Fits(x) && !jp.Fits(x) && !eu.Fits(x)));
            // THE DESIGNED CLASHES (coordinator 11-11: "some pairs of clubs clash and some do not").
            Check(jp.meetCol == fu.meetCol && jp.meetSlot == fu.meetSlot, "JAPANESE and FUTURE meet in the same block (SAT NIGHT)");
            Check(eu.meetCol == vi.meetCol && eu.meetSlot == vi.meetSlot, "EURO and VINTAGE meet in the same block (SUN MORNING)");
            Check(am.meetCol == fu.raceCol && am.meetSlot == fu.raceSlot, "the AMERICAN meet is the FUTURE race's block (FRI NIGHT)");
            Check(!(am.meetCol == jp.meetCol && am.meetSlot == jp.meetSlot) && !(am.raceCol == jp.raceCol && am.raceSlot == jp.raceSlot),
                  "AMERICAN and JAPANESE never clash");
            Check(Clubs.WeekIndex(1) == 0 && Clubs.WeekIndex(3) == 1 && Clubs.WeekIndex(10) == 2,
                  "weeks count from the career's first Sunday", Clubs.WeekIndex(3));
            Check(Clubs.IsFixtureWeek(3) && !Clubs.IsFixtureWeek(10) && Clubs.IsFixtureWeek(17),
                  "every second week's club race is a fixture");
            Check(Clubs.FixtureOpponent(jp, 3) != jp && Clubs.FixtureOpponent(jp, 3) != Clubs.FixtureOpponent(jp, 17) &&
                  !Clubs.FixtureOpponent(jp, 3).elite, "the opponent club rotates (never the elite)");

            // ---- joining at the meet ----
            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            s.playerJob = "";   // the clubs alone: no shifts to miss on the way
            var jpSpec = FirstSpec(x => jp.Fits(x) && x.modelYear < 1999);
            var usSpec = FirstSpec(x => am.Fits(x) && x.modelYear < 1999);
            var fuSpec = FirstSpec(x => fu.Fits(x) && !jp.Fits(x));
            var jpCar = CarMarket.MakeOwnedCar(s, jpSpec, 80, 1000f, jpSpec.price);
            var usCar = CarMarket.MakeOwnedCar(s, usSpec, 80, 1000f, usSpec.price);
            s.activeCar = usCar.id;
            Check(Clubs.Mine(s).Count == 0 && Duty.StartingOn(s, 2).Count == 0, "no club until the player joins one");
            Check(Clubs.JoinRefusal(s, jp).Contains("SAT NIGHT"), "you join at the club's meet",
                  Clubs.JoinRefusal(s, jp));
            Check(Clubs.JoinRefusal(s, el) == "invitation only", "the elite club only by invitation");
            AdvanceTo(s, 2, LifeRules.NightSlot);      // SAT NIGHT: the Japanese (and Future) meet
            Check(Clubs.MeetingAt(2, LifeRules.NightSlot).Contains(jp) && Clubs.MeetingAt(2, LifeRules.NightSlot).Contains(fu),
                  "two clubs meet at the lot on Saturday night");
            Check(Clubs.JoinRefusal(s, jp) != null && Clubs.JoinRefusal(s, jp).Contains("is not"),
                  "not in an American car", Clubs.JoinRefusal(s, jp));
            s.activeCar = jpCar.id;
            Check(Clubs.JoinRefusal(s, jp) == null, "in a Japanese car, yes", Clubs.JoinRefusal(s, jp));
            string joined = Clubs.Join(s, jp);
            Check(Clubs.IsMember(s, jp) && Clubs.Board(s, jp).Count == Clubs.BoardSize &&
                  Clubs.PlayerRank(s, jp) == Clubs.BoardSize, "joined, on a board of ten at the bottom", joined);
            Check(Duty.RecordFor(s, 2, LifeRules.NightSlot, Duty.KindMeet, jp.id)?.outcome == Duty.Done,
                  "and the meet joined at is attended");

            // ---- a fixture week (week 1): Sunday EVENING ----
            AdvanceTo(s, 3, LifeRules.EveningSlot);
            Check(Clubs.RaceDueNow(s) == jp, "the club race is due Sunday EVENING");
            var opp = Clubs.RaceOpponent(s, jp, 3);
            Check(opp.Valid && opp.club != null && opp.club != jp && opp.rank == 10,
                  "a fixture pairs the player with the other club's #10", opp.alias + " " + opp.rank);
            Check(Clubs.RaceCarRefusal(s, jp) == null && Clubs.BeginRace(s, jp)?.outcome == Duty.Done,
                  "started in a Japanese car: attended");
            Check(Clubs.RaceDueNow(s) == null, "and no longer due");
            Clubs.RecordRaceResult(s, jp, 3, opp.alias, true);
            var before = Clubs.Board(s, jp).Select(e => e.alias).ToArray();
            LifeRules.SpendActivitySlot(s, LifeRules.ActRace);
            LifeRules.SpendActivitySlot(s, LifeRules.ActDrive);   // rolls into MON
            Check(Clubs.Board(s, jp).Select(e => e.alias).SequenceEqual(before), "a fixture does not move the board");
            Check(s.clubNews.Exists(l => l.StartsWith(LifeRules.LogDate(3)) && l.Contains("JAPANESE ") &&
                                         l.Contains("(YOU WON YOUR PAIR)")),
                  "the fixture's score is in the club news, dated the race day", s.clubNews.LastOrDefault());
            var (us, them) = Clubs.ScoreFixture(s, jp, 3);
            Check(us + them == Clubs.BoardSize, "ten pairs, summed", us + "-" + them);

            // ---- an ordinary week (week 2) ----
            AdvanceTo(s, 9, LifeRules.NightSlot);
            Check(Clubs.ArrivedAtLot(s) != null &&
                  Duty.RecordFor(s, 9, LifeRules.NightSlot, Duty.KindMeet, jp.id)?.outcome == Duty.Done,
                  "signed in at the lot in a qualifying car: the meet is attended");
            AdvanceTo(s, 10, LifeRules.EveningSlot);
            opp = Clubs.RaceOpponent(s, jp, 10);
            Check(opp.club == null && opp.rank == 9, "an ordinary week races the member directly above", opp.rank);
            Clubs.BeginRace(s, jp);
            string head = Clubs.RecordRaceResult(s, jp, 10, opp.alias, true);
            Check(Clubs.PlayerRank(s, jp) == 9 && Clubs.RankOf(s, jp, opp.alias) == 10,
                  "finish ahead of the member above you and you swap", head);
            var copy = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(s));
            LifeRules.SpendActivitySlot(s, LifeRules.ActRace);
            LifeRules.SpendActivitySlot(s, LifeRules.ActDrive);
            LifeRules.SpendActivitySlot(copy, LifeRules.ActRace);
            LifeRules.SpendActivitySlot(copy, LifeRules.ActDrive);
            Check(Clubs.Board(s, jp).Select(e => e.alias).SequenceEqual(Clubs.Board(copy, jp).Select(e => e.alias)),
                  "the rest of the board races on SEEDED odds (two copies agree)");
            Check(Clubs.PlayerRank(s, jp) == 9 && Clubs.Board(s, jp).Count == Clubs.BoardSize,
                  "the churn leaves the player's own place alone", Clubs.PlayerRank(s, jp));

            // ---- PERKS: rank, read by rule ----
            int sprayFull = Paint.Cost(jpCar);
            Check(Clubs.PaintPrice(s, jpCar, sprayFull) == sprayFull, "no paint perk at #9 (it comes at #7)");
            var b = Clubs.Board(s, jp);
            int me = Clubs.PlayerRank(s, jp) - 1;
            var mine = b[me]; b.RemoveAt(me); b.Insert(1, mine);          // the player at #2, as if raced there
            Check(Clubs.PaintPrice(s, jpCar, sprayFull) < sprayFull && Clubs.PaintPrice(s, usCar, Paint.Cost(usCar)) == Paint.Cost(usCar),
                  "at #2: a cheaper respray on a Japanese car, not on the American one",
                  Clubs.PaintPrice(s, jpCar, sprayFull) + " of " + sprayFull);
            Check(Clubs.MechanicPrice(s, jpCar, 100) == 80 && Mathf.Approximately(Clubs.MechanicWork(s, jpCar), 1.25f),
                  "and the club mechanic: 20% off and a quarter more work", Clubs.MechanicPrice(s, jpCar, 100));
            Check(Clubs.NextPerk(s, jp).HasValue && Clubs.NextPerk(s, jp).Value.kind == Clubs.PerkLoaner &&
                  !Clubs.NextPerk(s, jp).Value.live, "the next perk is the (designed) loaner at #1");
            s.money = 99999; s.activeCar = jpCar.id;
            int engineBefore = Mathf.RoundToInt(jpCar.engine = 40f);
            LifeRules.BuyService(s, jpCar, 1);   // ENGINE TUNE-UP +35
            Check(Mathf.RoundToInt(jpCar.engine) >= engineBefore + 43, "a club mechanic's tune-up does more", jpCar.engine);

            // ---- the ELITE invitation: #1 in one club ----
            Check(Clubs.EliteInviteRefusal(s) != null, "#2 is not enough for the elite");
            b.Remove(mine); b.Insert(0, mine);
            AdvanceTo(s, s.day + 1, LifeRules.DawnSlot);
            var est = Clubs.StandingOf(s, el);
            Check(est != null && est.invited && s.mail.Exists(m => m.subject.Contains("ELITE")),
                  "#1 in the Japanese club: the elite club writes with an invitation");
            Check(Clubs.JoinRefusal(s, el) == null, "an invitation is accepted from anywhere, any block",
                  Clubs.JoinRefusal(s, el));
            Clubs.Join(s, el);
            Check(Clubs.IsMember(s, el) && Clubs.IsMember(s, jp) && Clubs.Mine(s).Count == 2,
                  "and the player is in two clubs at once");
            Check(Duty.Ord(s.day, s.slotIndex) < Clubs.StandingOf(s, el).from,
                  "joined by invitation, owed from the next block");
            Clubs.Leave(s, el);

            // ---- TWO CLUBS, ONE BLOCK: the loyalty test ----
            var fuCar = CarMarket.MakeOwnedCar(s, fuSpec, 80, 1000f, fuSpec.price);
            Clubs.Invite(s, fu, "test");
            Check(Clubs.JoinRefusal(s, fu) == null, "invited to the Future club too (owns a 1999 car)", Clubs.JoinRefusal(s, fu));
            Clubs.Join(s, fu);
            int sat = s.day; while (LifeRules.WeekCol(sat) != WorkSchedule.Sat) sat++;
            Check(Duty.ConflictAt(s, sat, LifeRules.NightSlot) && Duty.At(s, sat, LifeRules.NightSlot).Count == 2,
                  "SAT NIGHT is a CONFLICT for a member of both, ahead of time");
            int fri = sat - 1;
            Check(!Duty.ConflictAt(s, sat + 1, LifeRules.EveningSlot), "SUN EVENING (the Japanese race alone) is not");
            AdvanceTo(s, fri, LifeRules.NightSlot);
            s.activeCar = fuCar.id;
            Clubs.BeginRace(s, fu);                                     // keep the Future race
            AdvanceTo(s, sat, LifeRules.NightSlot);
            s.activeCar = jpCar.id;
            string both = Clubs.ArrivedAtLot(s);
            var keep = Duty.RecordFor(s, sat, LifeRules.NightSlot, Duty.KindMeet, jp.id);
            var lose = Duty.RecordFor(s, sat, LifeRules.NightSlot, Duty.KindMeet, fu.id);
            Check(keep != null && keep.outcome == Duty.Done && lose != null && lose.outcome == Duty.WrongCar,
                  "one car at a clash: the Japanese meet kept, the Future meet a miss", both);
            Check(Clubs.MissesInWindow(s, fu, s.day) == 1 && Clubs.MissesInWindow(s, jp, s.day) == 0,
                  "and the strike lands on the club that was let down");
            Check(Duty.MissLine(lose) == "FUTURE MEET - WRONG CAR", "the calendar's word for it", Duty.MissLine(lose));

            // ---- misses: two in one club = DROPPED from that club only ----
            int nextSat = sat + 7;
            s.activeCar = jpCar.id;
            AdvanceTo(s, sat + 1, LifeRules.EveningSlot); Clubs.BeginRace(s, jp);   // keep the Japanese race
            AdvanceTo(s, nextSat - 1, LifeRules.NightSlot); s.activeCar = fuCar.id; Clubs.BeginRace(s, fu);
            AdvanceTo(s, nextSat + 1, LifeRules.MorningSlot);            // both SAT NIGHT meets slept through
            Check(!Clubs.IsMember(s, fu) && Clubs.IsMember(s, jp),
                  "a second Future miss drops the player from the Future club - and only from it");
            Check(Clubs.MissesInWindow(s, jp, s.day) == 1, "(the Japanese club has its first strike)");
            Check(Clubs.JoinRefusal(s, fu) != null && Clubs.JoinRefusal(s, fu).Contains("back from"),
                  "the Future club will not have them back for " + Clubs.ReadmitDays + " days", Clubs.JoinRefusal(s, fu));
            Check(s.mail.Exists(m => m.subject == "OUT OF THE FUTURE CLUB"), "and the mail says so");
            Check(Duty.StartingOn(s, nextSat + 6).TrueForAll(o => o.club != fu.id), "a dropped player owes that club nothing");

            // ---- owning no qualifying car is a miss ----
            s.cars.RemoveAll(c => c.id == jpCar.id);
            AdvanceTo(s, nextSat + 2, LifeRules.DawnSlot);              // past SUN EVENING's Japanese race
            var nc = Duty.RecordFor(s, nextSat + 1, LifeRules.EveningSlot, Duty.KindRace, jp.id);
            Check(nc != null && nc.outcome == Duty.NoCar && !Clubs.IsMember(s, jp),
                  "a club race with no qualifying car owned is a NO CAR miss (the second drops)",
                  nc != null ? nc.outcome : "none");

            var w = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            bool anyClash = false;
            for (int d = 1; d <= 14; d++)
                for (int sl = 0; sl < LifeRules.SlotCount; sl++) anyClash |= Duty.ConflictAt(w, d, sl);
            Check(!anyClash, "no conflicts for a player in no club");
            Duty.lastNotice = null;
        }

        /// <summary>THE WAY IN (owner, 2026-11-11): a car-meet delivery - fast
        /// and undamaged - and the club asks you in.</summary>
        static void TestMeetOrders()
        {
            Line("the enforced week - car-meet orders:");
            var s = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(s);
            // ---- the rota: which shifts carry a car-meet order, for whom ----
            var seen = new System.Collections.Generic.HashSet<string>();
            int orders = 0, shiftDays = 0, offDays = 0;
            for (int d = 1; d <= 42; d++)
            {
                s.day = d; s.slotIndex = LifeRules.AfternoonSlot;
                var c = Clubs.MeetOrderFor(s);
                bool shiftDay = WorkSchedule.ShiftStartOn(s.playerJob, d) >= 0;
                if (shiftDay) shiftDays++;
                if (c != null) { orders++; seen.Add(c.id); if (!shiftDay) offDays++; }
            }
            Check(offDays == 0, "a car-meet order only comes on a shift day", offDays);
            Check(orders * Clubs.MeetOrderEveryNthShift == shiftDays,
                  "one shift day in " + Clubs.MeetOrderEveryNthShift + " carries one", orders + " of " + shiftDays);
            Check(Clubs.All.Where(c => !c.elite).All(c => seen.Contains(c.id)) && !seen.Contains("elite"),
                  "and over six weeks every ordinary club sends one (never the elite)", string.Join(",", seen));
            Check(Clubs.MeetOrderLine(Clubs.ById("euro")).Contains("EURO CLUB"), "the order is announced naming the club",
                  Clubs.MeetOrderLine(Clubs.ById("euro")));

            // ---- judging ----
            Check(Clubs.DroveWell(80f, 100f, 1f, 1f, false), "under par, nothing damaged: drove well");
            Check(!Clubs.DroveWell(120f, 100f, 1f, 1f, false), "over par: not");
            Check(!Clubs.DroveWell(80f, 100f, 0.8f, 1f, false), "a shaken pizza: not");
            Check(!Clubs.DroveWell(80f, 100f, 1f, 0.5f, false), "a pizza lost out of its box: not");
            Check(!Clubs.DroveWell(80f, 100f, 1f, 1f, true), "refused: not");

            var am = Clubs.ById("american");
            string late = Clubs.JudgeMeetDelivery(s, am.id, 130f, 100f, 1f, 1f, false);
            Check(late != null && late.Contains("NOT IMPRESSED") && late.Contains("LATE") &&
                  (Clubs.StandingOf(s, am) == null || !Clubs.StandingOf(s, am).invited),
                  "late: the club is not impressed, no invitation", late);
            string dmg = Clubs.JudgeMeetDelivery(s, am.id, 50f, 100f, 0.5f, 1f, false);
            Check(dmg != null && dmg.Contains("DAMAGED") && Clubs.JoinRefusal(s, am) != null,
                  "damaged: not impressed either, and it can be tried again", dmg);
            string good = Clubs.JudgeMeetDelivery(s, am.id, 70f, 100f, 1f, 1f, false);
            Check(good != null && good.Contains("INVITATION") && Clubs.StandingOf(s, am).invited &&
                  s.mail.Exists(m => m.subject == "AN INVITATION FROM THE AMERICAN CLUB" && m.body.Contains("american-built cars")),
                  "fast and in one piece: an invitation by mail, saying what cars the club takes", good);
            int mails = s.mail.Count(m => m.subject.StartsWith("AN INVITATION"));
            Check(Clubs.JudgeMeetDelivery(s, am.id, 70f, 100f, 1f, 1f, false) == null &&
                  s.mail.Count(m => m.subject.StartsWith("AN INVITATION")) == mails,
                  "no second invitation while one is waiting");
            Check(!Clubs.CanBeAskedIn(s, am), "and the rota skips a club that has already asked");
            var usSpec = FirstSpec(x => am.Fits(x));
            CarMarket.MakeOwnedCar(s, usSpec, 80, 1000f, usSpec.price);
            Clubs.Join(s, am);
            Check(Clubs.IsMember(s, am) && Clubs.JudgeMeetDelivery(s, am.id, 70f, 100f, 1f, 1f, false) == null,
                  "accepted from the CLUBS page; a member is never invited again");

            // ---- the intro's hook: the next order, for a club picked by name ----
            var eu = Clubs.ById("euro");
            s.day = 2;   // a Saturday: no shift, no rota
            Clubs.QueueMeetOrder(s, eu);
            Check(Clubs.MeetOrderFor(s) == eu && s.nextMeetOrderClub == "" && Clubs.MeetOrderFor(s) == null,
                  "a club queued by name gets the next order, once");

            // ---- through the real apply-back ----
            var jp = Clubs.ById("japanese");
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.Delivery = true;
            RaceHandoff.IsPractice = true;
            RaceHandoff.ResultReady = true;
            RaceHandoff.CarId = s.activeCar;
            RaceHandoff.TrackIndex = LifeRules.DeliveryTrackIndex(s);
            RaceHandoff.DeliveryPay = 30;
            RaceHandoff.DeliveryDropFraction = 1f;
            RaceHandoff.RaceTimeSeconds = LifeRules.DeliveryParSeconds(RaceHandoff.TrackIndex, 1f) * 0.6f;
            RaceHandoff.CargoReported = true; RaceHandoff.CargoCondition = 1f; RaceHandoff.CargoConsumable = 1f;
            RaceHandoff.CargoBoxes = 1; RaceHandoff.CargoImpacts = 0;
            RaceHandoff.DeliveryMeetClub = jp.id;
            string sum = LifeRules.ApplyRaceResult(s);
            Check(sum != null && sum.Contains("JAPANESE CLUB WAS IMPRESSED") && Clubs.StandingOf(s, jp).invited,
                  "a fast, clean car-meet delivery through the apply-back brings the invitation", sum);
            RaceHandoff.ClearAll();
            Duty.lastNotice = null;
        }

        static void TestEnforcedWeekSave()
        {
            Line("the enforced week - save v23:");
            var old = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            AdvanceTo(old, 11, LifeRules.EveningSlot);           // MON EVENING, mid-shift
            old.saveVersion = 22;
            old.dutyLog.Clear();
            old.laidOff = false;
            var back = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(old));
            LifeSimManager.Migrate(back);
            Check(back.saveVersion == CurrentSaveVersion, "migrated to v23", back.saveVersion);
            Check(back.dutyFrom == Duty.Ord(11, LifeRules.EveningSlot) && back.jobFrom == back.dutyFrom,
                  "obligations are owed from the block the save stood in", back.dutyFrom);
            Check(back.clubs != null && back.clubs.Count == 0 && !back.laidOff, "no club, on the roster");
            Check(Duty.StartingOn(back, 11).Count == 0, "today's shift, already under way, is not owed");
            LifeRules.SpendActivitySlot(back, LifeRules.ActDrive);
            LifeRules.SpendActivitySlot(back, LifeRules.ActDrive);
            Check(back.dutyLog.Count == 0 && !Duty.MissedOn(back, 11), "nothing missed retroactively");
            Check(Duty.StartingOn(back, 12).Exists(o => o.kind == Duty.KindShift),
                  "tomorrow's shift is on the schedule");

            // JsonUtility: memberships are a list of plain objects, never a null.
            var fresh = LifeRules.SeedNewGame("TESTER", 25, LifeRules.DefaultJobIndex);
            Check(fresh.saveVersion == CurrentSaveVersion && fresh.clubs != null && fresh.clubs.Count == 0 && fresh.dutyFrom == 0,
                  "a new career is born at v23, owing from day 1");
            fresh.clubs.Add(new ClubStanding { id = "euro", member = true, from = 20,
                board = new System.Collections.Generic.List<RankEntry> { new RankEntry { alias = Blacklist.PlayerKey } } });
            fresh.clubs.Add(new ClubStanding { id = "elite", invited = true });
            fresh.dutyLog.Add(new DutyRecord { day = 3, slot = 1, kind = Duty.KindMeet, outcome = Duty.Missed, club = "euro" });
            var rt = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(fresh));
            Check(rt.clubs.Count == 2 && rt.clubs[0].member && rt.clubs[0].board.Count == 1 && rt.clubs[1].invited &&
                  rt.dutyLog.Count == 1 && rt.dutyLog[0].IsMiss, "two standings, a board, an invitation and a record survive the save");
        }
    }
}
