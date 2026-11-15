using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// EVENTS (v29, owner 2026-11-15): the catalogue (every road raceable, the
    /// schedule only on the weekend blocks the owner set, the street level
    /// never RUSH HOUR, a field for every race), SIGN UP / WITHDRAW, the
    /// calendar, the no-show and the wrong car, prizes and the bonus through
    /// the real apply-back, the DNF, rep (racing vs street), word of mouth,
    /// club points and the board, the standings, the v29 save step and the
    /// PSX_EVENTS switch. Driven through the real clock, as the clubs' tests are.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void RunEventsTests()
        {
            Guard(nameof(TestEventCatalogue), TestEventCatalogue);
            Guard(nameof(TestEventSignUp), TestEventSignUp);
            Guard(nameof(TestEventResults), TestEventResults);
            Guard(nameof(TestEventStreet), TestEventStreet);
            Guard(nameof(TestEventClubs), TestEventClubs);
            Guard(nameof(TestEventSave), TestEventSave);
            RaceHandoff.ClearAll();
            Duty.lastNotice = null;
        }

        const int EvSat = 6, EvSun = 0, EvFri = 5;   // LifeRules.WeekCol

        /// <summary>A new career with one car of this spec, driving it.</summary>
        static LifeState EvCareer(CarSpec spec, int money = 20000)
        {
            var s = LifeRules.SeedNewGame("EVENTS", 25, LifeRules.DefaultJobIndex);
            s.money = money;
            s.cars.Clear();
            if (spec != null)
            {
                var c = CarMarket.MakeOwnedCar(s, spec, 90, 3000f, spec.price);
                c.fuel = 95f;
                s.activeCar = c.id;
            }
            return s;
        }

        /// <summary>The race due now, run and reported through the apply-back.</summary>
        static string RunEventRace(LifeState s, int pos, int field = 8)
        {
            var d = Events.DueNow(s);
            if (!d.Valid) return null;
            Events.Begin(s, d);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = s.activeCar;
            RaceHandoff.EventKey = d.entry.Key;
            RaceHandoff.EventRace = d.race;
            RaceHandoff.FinishPos = pos;
            RaceHandoff.FieldSize = field;
            RaceHandoff.MetersDriven = 0f;
            RaceHandoff.RaceTimeSeconds = 200f;
            RaceHandoff.ResultReady = true;
            string sum = LifeRules.ApplyRaceResult(s);
            RaceHandoff.ClearAll();
            return sum;
        }

        static CarSpec EvCivic79 => FirstSpec(c => c.drv == "FF" && c.modelYear > 0 && c.modelYear <= 1979 &&
                                                   CarSpec.ToHp(c.hp) <= 180);

        static void TestEventCatalogue()
        {
            Line("events - the catalogue and the schedule:");
            var all = Events.All;
            int sanc = all.Count(e => !e.street), street = all.Count(e => e.street);
            Check(sanc >= 8 && street >= 6, "about 8 sanctioned and 6 street events", sanc + " + " + street);
            Check(all.Select(e => e.id).Distinct().Count() == all.Length, "every event id is unique");
            var bad = new List<string>();
            foreach (var ev in all)
            {
                if (ev.races == null || ev.races.Length < 2 || ev.races.Length > 3) { bad.Add(ev.id + " races"); continue; }
                if (ev.prizes == null || ev.prizes.Length != 6) bad.Add(ev.id + " prizes");
                else for (int i = 1; i < 6; i++) if (ev.prizes[i] <= 0 || ev.prizes[i] >= ev.prizes[i - 1]) bad.Add(ev.id + " prize " + (i + 1));
                if (ev.fee <= 0 || ev.bonus <= 0) bad.Add(ev.id + " fee/bonus");
                if (string.IsNullOrEmpty(ev.shortName) || ev.shortName.Length > 10) bad.Add(ev.id + " short name");
                for (int r = 1; r < ev.races.Length; r++)
                    if (Events.RaceOrd(ev, 0, r) <= Events.RaceOrd(ev, 0, r - 1)) bad.Add(ev.id + " race order");
                if (ev.races.Max(x => x.week) >= ev.period) bad.Add(ev.id + " longer than its period");
            }
            Check(bad.Count == 0, "each: 2-3 races in time order inside its period, prizes 1-6 falling, a fee, a bonus, a short name",
                  string.Join(", ", bad));

            var noRoad = new List<string>();
            foreach (var ev in all)
                for (int r = 0; r < ev.races.Length; r++)
                {
                    if (!TrackCatalog.TryIndexOf(ev.races[r].tracks[0], out _)) noRoad.Add(ev.id + " R" + (r + 1) + " first choice");
                    int v = Events.VenueOf(ev, r);
                    if (v < 0) { noRoad.Add(ev.id + " R" + (r + 1) + " none"); continue; }
                    var t = TrackCatalog.At(v);
                    if (t.IsDragEvent || t.IsDeckRun || t.IsRoam || !TrafficLevels.VenueHasTraffic(t) ||
                        !TrackCatalog.SceneShipped(TrackCatalog.SceneIndex(v)))
                        noRoad.Add(ev.id + " R" + (r + 1) + " " + t.id);
                }
            Check(noRoad.Count == 0, "every race's road exists and is raceable here (a road race, no strip, no deck)",
                  string.Join(", ", noRoad));
            var was = Edition.Simulated;
            var mainBad = new List<string>();
            Edition.Simulate(EditionKind.Main);
            try
            {
                foreach (var ev in all)
                    for (int r = 0; r < ev.races.Length; r++)
                        if (Events.VenueOf(ev, r) < 0) mainBad.Add(ev.id + " R" + (r + 1));
            }
            finally { Edition.Simulate(was); }
            Check(mainBad.Count == 0, "with no Charlotte (MAIN) every city race falls back to a mountain road",
                  string.Join(", ", mainBad));

            var wrongDay = new List<string>();
            var overlap = new List<string>();
            foreach (var ev in all)
                for (int n = 0; n < 12; n++)
                {
                    for (int r = 0; r < ev.races.Length; r++)
                    {
                        int day = Events.RaceDay(ev, n, r), col = LifeRules.WeekCol(day), slot = Events.RaceSlot(ev, r);
                        bool ok = ev.street
                            ? (col == EvFri || col == EvSat || col == EvSun) && slot == LifeRules.NightSlot
                            : (col == EvSat || col == EvSun) && (slot == LifeRules.MorningSlot || slot == LifeRules.NoonSlot);
                        if (!ok) wrongDay.Add(ev.id + " #" + n + " R" + (r + 1) + " " + LifeRules.WeekDayNames[col] + " " +
                                              LifeRules.SlotNames[slot]);
                    }
                    if (n > 0 && Events.RaceOrd(ev, n, 0) <= Events.RaceOrd(ev, n - 1, ev.races.Length - 1))
                        overlap.Add(ev.id + " #" + n);
                }
            Check(wrongDay.Count == 0, "12 runnings each: sanctioned only SAT/SUN MORNING or NOON, street only FRI-SUN NIGHT, " +
                  "nothing on a weekday", string.Join(", ", wrongDay.Take(6)));
            Check(overlap.Count == 0, "a running ends before the next begins", string.Join(", ", overlap));
            var rookie = Events.ById("rookie");
            Check(Events.RaceDay(rookie, 0, 0) == 2 && Events.OpenEdition(rookie, Duty.Ord(1, 0)) == 0 &&
                  Events.OpenEdition(rookie, Duty.Ord(2, LifeRules.MorningSlot)) == 1,
                  "the ROOKIE CUP opens on the first Saturday; once its first block begins, the next running is the open one");

            var traffic = new List<string>();
            var hours = new List<string>();
            foreach (var ev in all)
                for (int r = 0; r < ev.races.Length; r++)
                {
                    int v = Events.VenueOf(ev, r);
                    int lv = Events.TrafficFor(ev, v >= 0 ? TrackCatalog.At(v) : null);
                    if (ev.street ? lv < TrafficLevels.Light || lv > TrafficLevels.Medium : lv != TrafficLevels.None)
                        traffic.Add(ev.id + " R" + (r + 1) + " " + TrafficLevels.Name(lv));
                    if (lv == TrafficLevels.RushHour) traffic.Add(ev.id + " RUSH HOUR");
                    if (!TimeOfDay.InSlot(Events.HourFor(ev, 0, r), Events.RaceSlot(ev, r))) hours.Add(ev.id + " R" + (r + 1));
                }
            Check(traffic.Count == 0, "sanctioned races run NONE; street races LIGHT or MEDIUM - never RUSH HOUR",
                  string.Join(", ", traffic));
            Check(hours.Count == 0, "every race's hour is in its own block", string.Join(", ", hours));

            var thin = new List<string>();
            foreach (var ev in all)
                for (int r = 0; r < ev.races.Length; r++)
                {
                    var f = Events.FieldFor(ev, 3, r);
                    var g = Events.FieldFor(ev, 3, r);
                    if (f.cars.Count < LifeRules.FieldOpponentsMin || f.roughHp <= 0) thin.Add(ev.id + " R" + (r + 1) + " x" + f.cars.Count);
                    if (f.cars.Any(c => Events.RuleRefusal(ev, c, CarSpec.ToHp(c.hp)) != null)) thin.Add(ev.id + " rule");
                    if (!f.cars.Select(c => c.id).SequenceEqual(g.cars.Select(c => c.id))) thin.Add(ev.id + " not seeded");
                }
            var ffField = Events.FieldFor(Events.ById("ffcup"), 0, 0);
            Check(thin.Count == 0, "every race has a field of 3+ that keeps the rule, the same each time it is asked",
                  string.Join(", ", thin));
            Line("    e.g. FRONT DRIVE CUP R1: " + ffField.cars.Count + " cars, ~" + ffField.roughHp + " hp");
        }

        static void TestEventSignUp()
        {
            Line("events - sign up, the calendar, withdraw, the no-show, the wrong car:");
            var spec = EvCivic79;
            Check(spec != null, "a 1979-or-older FF car under 180 hp for the run", spec?.name);
            if (spec == null) return;
            var s = EvCareer(spec);
            var rookie = Events.ById("rookie"); var classic = Events.ById("classic"); var frcup = Events.ById("frcup");
            Check(Events.SignUpRefusal(s, rookie) == null, "a new career can sign up for the ROOKIE CUP", Events.SignUpRefusal(s, rookie));
            Check(Events.SignUpRefusal(s, frcup)?.Contains("rep") == true, "the REAR DRIVE CUP wants racing rep",
                  Events.SignUpRefusal(s, frcup));
            s.racingRep = 50;
            Check(Events.SignUpRefusal(s, frcup)?.Contains("no car") == true, "and a car its rule takes",
                  Events.SignUpRefusal(s, frcup));
            s.racingRep = 0;
            int money = s.money;
            bool ok = Events.SignUp(s, rookie, out string line);
            var e = Events.ActiveEntry(s, rookie);
            Check(ok && e != null && e.edition == 0 && s.money == money - rookie.fee, "SIGN UP takes the fee and opens the entry", line);
            Check(Events.RaceAt(s, 2, LifeRules.MorningSlot).entry == e && Events.RaceAt(s, 3, LifeRules.MorningSlot).race == 1,
                  "both races are in the calendar: SAT 2 and SUN 3 MORNING");
            Check(Events.SignUpRefusal(s, rookie) != null, "signing up twice is refused");
            Check(Events.SignUpRefusal(s, classic)?.StartsWith("clashes") == true,
                  "another event with a race in the same block is refused", Events.SignUpRefusal(s, classic));
            int m2 = s.money;
            ok = Events.Withdraw(s, rookie, out line);
            Check(ok && e.withdrawn && s.money == m2 && s.racingRep == 0, "WITHDRAW before the day: out, nothing back, no rep lost", line);
            Check(!Events.RaceAt(s, 2, LifeRules.MorningSlot).Valid, "and its races leave the calendar");
            Check(Events.SignUpRefusal(s, rookie)?.Contains("withdrew") == true, "that running cannot be re-entered",
                  Events.SignUpRefusal(s, rookie));
            ok = Events.SignUp(s, classic, out line);
            Check(ok, "the CLASSIC CUP is free to enter now", line);
            AdvanceTo(s, 2, LifeRules.DawnSlot);
            Check(Events.WithdrawRefusal(s, classic)?.Contains("race day") == true, "on race day WITHDRAW is shut",
                  Events.WithdrawRefusal(s, classic));
            AdvanceTo(s, 2, LifeRules.MorningSlot);
            var due = Events.DueNow(s);
            Check(due.Valid && due.ev == classic && due.race == 0, "SAT 2 MORNING: race 1 is due now");
            s.racingRep = 10;
            AdvanceTo(s, 3, LifeRules.DawnSlot);
            var ce = Events.EntryFor(s, classic, 0);
            Check(Events.PlaceOf(ce, 0) == Events.NoShow && Mathf.Approximately(s.racingRep, 10 - Events.NoShowRacingRep) && !ce.closed,
                  "not run: at the rollover it is a NO-SHOW - rep down, the fee kept, race 2 still on", s.racingRep);
            Check(s.mail.Any(m => m.subject.StartsWith("NO-SHOW")), "and the mail says so");
            AdvanceTo(s, 3, LifeRules.NoonSlot);
            var due2 = Events.DueNow(s);
            Check(due2.Valid && due2.race == 1, "SUN 3 NOON: race 2 is due");
            Check(Events.CarRefusal(Events.ById("ffcup"), s.ActiveCar) == null &&
                  Events.CarRefusal(frcup, s.ActiveCar) == "FR ONLY", "the rule reads the car: an FF car is not an FR",
                  Events.CarRefusal(frcup, s.ActiveCar));
            Events.MarkWrongCar(s, due2, "TEST");
            Check(Events.PlaceOf(ce, 1) == Events.WrongCar && ce.closed && !Events.DueNow(s).Valid,
                  "at the line in the wrong car: it cannot start and it is a no-show; the entry closes");
        }

        static void TestEventResults()
        {
            Line("events - results through the apply-back: prizes, the bonus, rep, the DNF:");
            var spec = EvCivic79;
            if (spec == null) { Check(false, "a car for the run"); return; }
            var rookie = Events.ById("rookie");
            var s = EvCareer(spec);
            Events.SignUp(s, rookie, out _);
            AdvanceTo(s, 2, LifeRules.MorningSlot);
            int money = s.money;
            float street = s.streetRep;
            string sum = RunEventRace(s, 2);
            var e = Events.EntryFor(s, rookie, 0);
            Check(s.money - money == rookie.prizes[1] && Events.PlaceOf(e, 0) == 2, "P2 pays the second prize", sum);
            Check(Mathf.Approximately(s.racingRep, Events.RacingRepGain[1]) && Mathf.Approximately(s.streetRep, street),
                  "a sanctioned result: racing rep +" + Events.RacingRepGain[1] + ", street rep untouched", s.racingRep);
            Check(s.day == 2 && s.slotIndex == LifeRules.NoonSlot && !LifeRules.RacedToday(s),
                  "the race spent its block, not the purse race's one-a-day cap");
            AdvanceTo(s, 3, LifeRules.MorningSlot);
            money = s.money;
            RunEventRace(s, 1);
            Check(s.money - money == rookie.prizes[0] && !e.bonusPaid && e.closed, "a win pays the first prize; P2 earlier means no bonus",
                  s.money - money);

            var s2 = EvCareer(spec);
            Events.SignUp(s2, rookie, out _);
            AdvanceTo(s2, 2, LifeRules.MorningSlot);
            RunEventRace(s2, 1);
            AdvanceTo(s2, 3, LifeRules.MorningSlot);
            money = s2.money;
            sum = RunEventRace(s2, 1);
            var e2 = Events.EntryFor(s2, rookie, 0);
            Check(s2.money - money == rookie.prizes[0] + rookie.bonus && e2.bonusPaid, "winning every race pays the bonus", sum);
            Check(Mathf.Approximately(s2.racingRep, Events.RacingRepGain[0] * 2 + Events.BonusRep), "and its rep", s2.racingRep);

            var s3 = EvCareer(spec);
            Events.SignUp(s3, rookie, out _);
            AdvanceTo(s3, 2, LifeRules.MorningSlot);
            Events.Begin(s3, Events.DueNow(s3));
            string w = Events.SweepInFlight(s3);
            Check(w != null && Events.PlaceOf(Events.EntryFor(s3, rookie, 0), 0) == Events.Dnf,
                  "a race that went to the line and never came back is a DNF at the house", w);
        }

        static void TestEventStreet()
        {
            Line("events - street: the lists, word of mouth, street rep, traffic at night:");
            var spec = FirstSpec(c => c.origin == "jpn" && c.modelYear >= 1995 && CarSpec.ToHp(c.hp) <= 240);
            Check(spec != null, "a 1995-or-newer Japanese car under 240 hp", spec?.name);
            if (spec == null) return;
            var fri = Events.ById("fridaynight"); var touge = Events.ById("tougenight");
            var s = EvCareer(spec);
            Check(!Events.Visible(s, fri) && !Events.Visible(s, touge), "street rep 0: no street event on the list");
            s.streetRep = Events.StreetListRep - 1;
            bool under = Events.Visible(s, fri);
            s.streetRep = Events.StreetListRep;
            Check(!under && Events.Visible(s, fri) && !Events.Visible(s, touge),
                  "street rep " + Events.StreetListRep + " lists the FRIDAY NIGHT RUN; word-of-mouth events stay hidden");
            AdvanceTo(s, 1, LifeRules.NightSlot);
            Events.ArrivedAtLot(s);
            Check(CarMeets.OnNow(s) && s.evHeard.Count == 1 && Events.Visible(s, Events.ById(s.evHeard[0])),
                  "FRI 1 NIGHT, pulling into the car meet passes one street event on", string.Join(",", s.evHeard));
            Check(s.mail.Any(m => m.subject.StartsWith("WORD ON THE STREET")), "with the word in the mail");
            Events.ArrivedAtLot(s);

            var jp = Clubs.ById("japanese");
            Check(Events.NextUnheard(LifeRules.SeedNewGame("X", 25, LifeRules.DefaultJobIndex), jp) == touge &&
                  Events.NextUnheard(LifeRules.SeedNewGame("X", 25, LifeRules.DefaultJobIndex), Clubs.ById("future")).id == "y2krun",
                  "each club passes on its own event first (JAPANESE: TOUGE NIGHT, FUTURE: Y2K RUN)");
            var s2 = EvCareer(spec);
            Clubs.Invite(s2, jp, "test");
            Clubs.Join(s2, jp);
            AdvanceTo(s2, 2, LifeRules.NightSlot);
            Events.ArrivedAtLot(s2);
            Check(Clubs.IsMember(s2, jp) && s2.evHeard.Contains(touge.id) && s2.evHeard.Count == 2,
                  "SAT 2 NIGHT, a JAPANESE member at the lot: the club passes on TOUGE NIGHT, the meet one more",
                  string.Join(",", s2.evHeard));

            var s3 = EvCareer(spec);
            s3.streetRep = Events.StreetListRep;
            bool ok = Events.SignUp(s3, fri, out string line);
            Check(ok && Events.RaceDay(fri, Events.ActiveEntry(s3, fri).edition, 0) == 8, "signed up for FRI 8 NIGHT", line);
            AdvanceTo(s3, 8, LifeRules.NightSlot);
            var d = Events.DueNow(s3);
            int v = Events.VenueOf(fri, 0);
            int lv = Events.TrafficFor(fri, v >= 0 ? TrackCatalog.At(v) : null);
            Check(d.Valid && d.ev == fri && lv >= TrafficLevels.Light && lv <= TrafficLevels.Medium &&
                  Events.HourFor(fri, d.entry.edition, 0) == TimeOfDay.Night,
                  "FRI 8 NIGHT: the street race is due, at night, with traffic on", TrafficLevels.Name(lv));
            s3.lastAnyRaceDay = s3.day;
            float racing = s3.racingRep;
            string sum = RunEventRace(s3, 1);
            Check(Mathf.Approximately(s3.streetRep, Events.StreetListRep + Events.StreetRepGain[0]) &&
                  Mathf.Approximately(s3.racingRep, racing), "a street win: street rep +" + Events.StreetRepGain[0] +
                  ", racing rep untouched", sum);
            Check(s3.day == 9 && s3.slotIndex == LifeRules.DawnSlot &&
                  Events.PlaceOf(Events.EntryFor(s3, fri, 0), 0) == 1,
                  "the NIGHT race rolled the day - banked first, so the rollover did not call it a no-show");
        }

        static void TestEventClubs()
        {
            Line("events - club points, the board and the standings:");
            var both = FirstSpec(c => c.origin == "jpn" && c.modelYear >= 1999 && CarSpec.ToHp(c.hp) <= 280);
            if (both == null) { Check(false, "a 1999 Japanese car"); return; }
            var s = EvCareer(both);
            var jp = Clubs.ById("japanese"); var fu = Clubs.ById("future");
            Clubs.Invite(s, jp, "t"); Clubs.Join(s, jp);
            Clubs.Invite(s, fu, "t"); Clubs.Join(s, fu);
            int rank0 = Clubs.PlayerRank(s, jp);
            string line = Events.AwardClubPoints(s, s.ActiveCar, 1, s.day);
            Check(Events.PointsOf(s, "japanese") == Events.ClubPoints[0] && Events.PointsOf(s, "future") == Events.ClubPoints[0] &&
                  Events.PointsOf(s, "american") == 0,
                  "a win in a car two of your clubs take scores for both; a club you are not in gets none", line);
            Events.AwardClubPoints(s, s.ActiveCar, 1, s.day);
            Check(rank0 > 1 && Clubs.PlayerRank(s, jp) == rank0 - 1 && Events.MyPointsOf(s, "japanese") == 20,
                  Events.PointsPerPlace + " points of your own move you up one place on the club board",
                  rank0 + " -> " + Clubs.PlayerRank(s, jp));
            Check(Events.AwardClubPoints(s, s.ActiveCar, 7, s.day) == "", "7th scores nothing");

            var s2 = EvCareer(both);
            AdvanceTo(s2, 3, LifeRules.DawnSlot);
            int total = Events.Standings(s2).Sum(kv => kv.Value);
            Check(total > 0 && s2.evNews.Count > 0, "the weekend's fields score the clubs at the rollover, entered or not",
                  total + " pts; " + s2.evNews.LastOrDefault());
        }

        static void TestEventSave()
        {
            Line("events - the save (v29) and the switch:");
            var spec = EvCivic79;
            var rookie = Events.ById("rookie");
            var s = EvCareer(spec);
            Events.SignUp(s, rookie, out _);
            s.racingRep = 12f;
            s.evHeard.Add("tougenight");
            Events.ScoreOf(s, "japanese").points = 7;
            var back = JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(s));
            Check(back.evEntries.Count == 1 && back.evEntries[0].Key == "rookie#0" && back.evEntries[0].place.Count == 2 &&
                  back.evEntries[0].inFlight == -1 && Mathf.Approximately(back.racingRep, 12f) &&
                  back.evHeard.Contains("tougenight") && Events.PointsOf(back, "japanese") == 7,
                  "entries, word heard, racing rep and club points survive a save");
            var old = JsonUtility.FromJson<LifeState>("{\"saveVersion\":28,\"day\":50}");
            Check(old.evEntries != null && old.evEntries.Count == 0 && old.racingRep == 0f,
                  "a v28 save reads as signed up for nothing, racing rep 0");
            LifeSimManager.Migrate(old);
            string once = JsonUtility.ToJson(old);
            LifeSimManager.Migrate(old);
            Check(old.saveVersion == 29 && old.evHeard != null && old.evClubScores != null && JsonUtility.ToJson(old) == once,
                  "the v29 step stamps it, and a second pass changes nothing", old.saveVersion);

            bool was = Events.On;
            Events.On = false;
            try
            {
                Check(!Events.Visible(s, rookie) && Events.SignUpRefusal(s, rookie) != null && !Events.RaceAt(s, 2, 1).Valid,
                      "PSX_EVENTS=0: nothing listed, no sign-up, nothing in the calendar");
                string json = JsonUtility.ToJson(s);
                Events.Tick(s);
                Check(JsonUtility.ToJson(s) == json, "and the rollover leaves the events in the save as they are");
            }
            finally { Events.On = was; }
        }
    }
}
