using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE ENFORCED WEEK's pages (v23): the JOB page with the week's shifts,
    /// the CLUB page (the clubs, or the player's club board), and the club
    /// race's pre-race page. The calendar's words for shifts, meets and club
    /// races live in BlockLines with everything else a block can say.
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>Set by the "racenow" hop when the player reaches the line
        /// for the club race in a car the club does not take; toasted once.</summary>
        string clubWrongCar;
        bool confirmLeaveClub;

        static readonly Color ClubRowBg = new Color(0.14f, 0.16f, 0.20f, 1f);

        /// <summary>A label/value row whose value has the width it needs and is
        /// fitted to it - the shared Row() gives a value 200 units, and
        /// "AFTERNOON + EVENING" in bold capitals is wider than that.</summary>
        void DutyRow(string label, string value, ref float y, Color? ink = null)
        {
            float lw = ColW * 0.36f, vx = ColL + ColW * 0.38f, vw = ColW * 0.62f;
            MenuKit.FitOneLine(MenuKit.Label(body, label, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, lw, height: 28f), lw);
            MenuKit.FitOneLine(MenuKit.Label(body, value, MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(vx, y), TextAnchor.MiddleLeft, ink ?? Color.white, vw, height: 28f, bold: true), vw);
            y -= 32f;
        }

        void Paragraph(string text, ref float y, Color? ink = null)
        {
            MenuKit.Para(body, text, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y - 2f),
                out float h, TextAnchor.UpperLeft, ink ?? MenuKit.Dim, ColW);
            y -= h + 10f;
        }

        // =================== THE JOB ===================
        void BuildJobs()
        {
            float y = -14f;
            PageHeader(ref y, "THE JOB");
            if (!WorkSchedule.Employed(S))
            {
                Paragraph("Not on anybody's roster.", ref y);
                ClubLink(ref y);
                return;
            }

            string status = WorkSchedule.StatusWord(S);
            DutyRow("CURRENT JOB", S.playerJob, ref y, MenuKit.Accent);
            DutyRow("STATUS", status, ref y,
                S.laidOff ? MenuKit.Bad : status.StartsWith("WARNED") ? MenuKit.Accent : MenuKit.Good);
            DutyRow("SCHEDULE", WorkSchedule.RosterShort(S.playerJob), ref y);
            var sh = WorkSchedule.ShiftsFor(S.playerJob);
            if (sh.Length > 0)
                DutyRow("HOURS", WorkSchedule.HoursLine(sh[0].slot) + "  ·  TIPS AT THE DOOR", ref y);
            DutyRow("MISSES (" + WorkSchedule.LayoffWindowDays + " DAYS)",
                WorkSchedule.MissesInWindow(S, S.day) + " OF " + WorkSchedule.LayoffMisses, ref y,
                WorkSchedule.MissesInWindow(S, S.day) > 0 ? MenuKit.Bad : MenuKit.Good);
            DutyRow("WORK REP", Mathf.RoundToInt(S.workRep) + "/100", ref y,
                S.workRep >= 50 ? MenuKit.Good : MenuKit.Bad);
            y -= 6f;

            // ---- this week's shifts ----
            MenuKit.Label(body, "THIS WEEK", MenuKit.Small, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 28f, bold: true);
            y -= 32f;
            int start = LifeRules.WeekStart(S.day);
            int shown = 0;
            for (int d = start; d < start + 7; d++)
            {
                if (d < 1) continue;
                int st = WorkSchedule.ShiftStartOn(S.playerJob, d);
                if (st < 0) continue;
                shown++;
                string when = LifeRules.WeekDayNames[LifeRules.WeekCol(d)] + " " + LifeRules.DayOfMonth(d) +
                              "  ·  " + WorkSchedule.BlocksLine(st);
                var (word, ink) = ShiftStatus(d, st);
                DutyRow(when, word, ref y, ink);
            }
            if (shown == 0) Paragraph("No shifts this week.", ref y);
            y -= 4f;

            // ---- the call-back ----
            if (S.laidOff)
            {
                if (S.callBackOffered)
                {
                    MenuKit.Button(body, "TAKE THE SHIFTS BACK", new Vector2(0.5f, 1f), new Vector2(0f, y),
                        new Vector2(Mathf.Min(ColW, 520f), 54f), () =>
                        {
                            string line = WorkSchedule.AcceptCallBack(S);
                            LifeSimManager.Save(); Rebuild();
                            if (line != null) Toast(line);
                        }, 19, GoBg);
                    y -= 62f;
                    Paragraph("Tony's called: there is an opening. Your schedule starts again from the " +
                              "next block, with the misses wiped.", ref y, MenuKit.Accent);
                }
                else
                    Paragraph("LAID OFF since " + LifeRules.DateLabel(S.layoffDay) +
                              ". No shifts and no pay until Tony's calls back with an opening.", ref y,
                              MenuKit.Bad);
            }

            Paragraph("Shifts are FIXED: clock on at Tony's counter during one, and only then. " +
                      "Miss a shift and Tony warns you; miss " + WorkSchedule.LayoffMisses + " inside " +
                      WorkSchedule.LayoffWindowDays + " days and you are LAID OFF - no shifts, no pay - " +
                      "until they call you back when there is an opening (" + WorkSchedule.CallBackMinDays +
                      " to " + WorkSchedule.CallBackMaxDays + " days).", ref y);
            ClubLink(ref y);
        }

        /// <summary>One shift's state for the JOB page.</summary>
        (string, Color) ShiftStatus(int day, int startSlot)
        {
            var rec = Duty.RecordFor(S, day, startSlot, Duty.KindShift);
            if (rec != null) return rec.IsMiss ? ("MISSED", MenuKit.Bad) : ("WORKED", MenuKit.Good);
            if (S.laidOff) return ("LAID OFF", MenuKit.Dim);
            if (Duty.Ord(day, startSlot) < S.jobFrom || Duty.Ord(day, startSlot) < S.dutyFrom)
                return ("BEFORE THE SCHEDULE", MenuKit.Dim);
            int end = Duty.Ord(day, startSlot + WorkSchedule.SpanFrom(startSlot));
            int now = Duty.NowOrd(S);
            if (Duty.Ord(day, startSlot) <= now && now < end)
                return (S.workedToday ? "WORKING" : "NOW - CLOCK ON", MenuKit.Accent);
            return ("DUE", Color.white);
        }

        void ClubLink(ref float y)
        {
            int n = Clubs.Mine(S).Count;
            MenuKit.Button(body, n > 0 ? "YOUR CLUBS (" + n + ")" : "CAR CLUBS",
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 520f), 50f),
                () => { tab = "club"; clubView = ""; Rebuild(); }, 18);
            y -= 58f;
        }

        // =================== THE CLUBS ===================
        /// <summary>The club page's club, by id; empty = the list of clubs.</summary>
        string clubView = "";
        /// <summary>When two of the player's club races fall in one block, the
        /// one the pre-race page is running (the other is the clash's miss).</summary>
        string clubRacePick = "";

        void BuildClub()
        {
            var c = Clubs.ById(clubView);
            if (c == null) { BuildClubList(); return; }
            float y = -14f;
            bool member = Clubs.IsMember(S, c);
            var st = Clubs.StandingOf(S, c);

            MenuKit.Button(body, "<  ALL CLUBS", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, 200f), y),
                new Vector2(200f, 40f), () => { clubView = ""; confirmLeaveClub = false; Rebuild(); }, 17);
            MenuKit.FitOneLine(MenuKit.Label(body, c.name, MenuKit.Small, new Vector2(0.5f, 1f),
                new Vector2(ColL + 214f, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW * 0.36f, height: 40f, bold: true),
                ColW * 0.36f);
            if (member)
            {
                var b0 = Clubs.Board(S, c);
                var mine = b0[Clubs.PlayerRank(S, c) - 1];
                MenuKit.FitOneLine(MenuKit.Label(body, "YOU ARE #" + Clubs.PlayerRank(S, c) + " OF " + b0.Count +
                        "  ·  " + mine.wins + "W " + mine.losses + "L", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(ColR, y), TextAnchor.MiddleRight, MenuKit.Dim, ColW * 0.34f, height: 40f), ColW * 0.34f);
            }
            y -= 46f;
            MenuKit.FitOneLine(MenuKit.Label(body, "MEET " + c.MeetWhen + "  ·  RACE " + c.RaceWhen +
                    "  ·  " + RuleCars(c), MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, Color.white, ColW, height: 24f), ColW);
            y -= 26f;
            string clash = ClashLine(c);
            if (clash != null)
            {
                MenuKit.FitOneLine(MenuKit.Label(body, clash, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, MenuKit.Bad, ColW, height: 24f), ColW);
                y -= 26f;
            }

            if (member)
            {
                int strikes = Clubs.MissesInWindow(S, c, S.day);
                int next = NextClubRaceDay(c);
                var opp = Clubs.RaceOpponent(S, c, next);
                string nextLine = "NEXT RACE " + LifeRules.DateLabel(next) + " " + LifeRules.SlotNames[c.raceSlot] +
                                  "  ·  " + (Clubs.IsFixtureWeek(next) ? "FIXTURE VS " +
                                      (opp.club != null ? opp.club.shortName : "") + " #" + opp.rank + " " + opp.alias
                                      : "VS #" + opp.rank + " " + opp.alias);
                MenuKit.FitOneLine(MenuKit.Label(body, nextLine, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Good, ColW * 0.70f, height: 24f), ColW * 0.70f);
                MenuKit.FitOneLine(MenuKit.Label(body, "STRIKES " + strikes + " OF " + Clubs.MissesToDrop,
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColR, y), TextAnchor.MiddleRight,
                    strikes > 0 ? MenuKit.Bad : MenuKit.Dim, ColW * 0.28f, height: 24f, bold: strikes > 0), ColW * 0.28f);
                y -= 30f;
            }

            // THE PERKS, earned by rank and read off it. The next one is the
            // reason to race the member above you.
            BuildPerks(c, ref y);

            if (member)
            {
                // THE BOARD - the blacklist board's rule: ten rows give way to
                // the fixed furniture rather than the other way round.
                var board = Clubs.Board(S, c);
                int me = Clubs.PlayerRank(S, c);
                int next = NextClubRaceDay(c);
                var opp = Clubs.RaceOpponent(S, c, next);
                float span = ColW;
                float nameW = span * 0.30f, carW = span * 0.50f, recW = span * 0.16f;
                float colCar = ColL + nameW + span * 0.02f;
                float rowH = Mathf.Clamp(((BodyH - MenuKit.ScrollPad) - 300f) / Mathf.Max(1, board.Count), 30f, 40f);
                for (int r = 1; r <= board.Count; r++)
                {
                    var e = board[r - 1];
                    bool you = e.IsPlayer;
                    bool target = !you && e.alias == opp.alias && !Clubs.IsFixtureWeek(next);
                    Color tone = you ? MenuKit.Active : target ? MenuKit.Accent : r < me ? Color.white : MenuKit.Dim;
                    string name = "#" + r + " " + (you ? Clip(S.playerName, 10) + " (YOU)" : e.alias);
                    MenuKit.FitOneLine(MenuKit.Label(body, name, MenuKit.Tiny, new Vector2(0.5f, 1f),
                        new Vector2(ColL, y), TextAnchor.MiddleLeft, tone, nameW, height: rowH - 4f,
                        bold: you || target), nameW);
                    var spec = you ? null : Clubs.MemberCar(c, e.alias);
                    string car = you ? (S.ActiveCar != null ? S.ActiveCar.displayName : "no car")
                                     : (spec != null ? spec.name : "");
                    MenuKit.FitOneLine(MenuKit.Label(body, Clip(car, 34), MenuKit.Tiny, new Vector2(0.5f, 1f),
                        new Vector2(colCar, y), TextAnchor.MiddleLeft, you ? MenuKit.Accent : MenuKit.Dim, carW,
                        height: rowH - 4f), carW);
                    MenuKit.Label(body, e.wins + "W " + e.losses + "L", MenuKit.Tiny, new Vector2(0.5f, 1f),
                        new Vector2(ColR, y), TextAnchor.MiddleRight, MenuKit.Dim, recW, height: rowH - 4f);
                    y -= rowH;
                }
                y -= 8f;
                MenuKit.Button(body, confirmLeaveClub ? "PRESS AGAIN TO HAND YOUR CARD BACK" : "LEAVE THE " + c.shortName + " CLUB",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Mathf.Min(ColW, 520f), 48f), () =>
                    {
                        if (!confirmLeaveClub) { confirmLeaveClub = true; Rebuild(); return; }
                        confirmLeaveClub = false;
                        string line = Clubs.Leave(S, c);
                        LifeSimManager.Save(); Rebuild();
                        if (line != null) Toast(line);
                    }, 17, confirmLeaveClub ? (Color?)new Color(0.44f, 0.16f, 0.14f, 1f) : null);
                y -= 56f;
                Paragraph("Miss " + Clubs.MissesToDrop + " of this club's meets or races inside " + Clubs.MissWindowDays +
                          " days - or turn up in a car that is not " + c.rule + " - and you are dropped. " +
                          "They take you back " + Clubs.ReadmitDays + " days later, at the bottom.", ref y);
                return;
            }

            // Not a member: how in.
            string no = Clubs.JoinRefusal(S, c);
            if (st != null && st.invited && no == null)
            {
                MenuKit.Button(body, "ACCEPT THE INVITATION", new Vector2(0.5f, 1f), new Vector2(0f, y),
                    new Vector2(Mathf.Min(ColW, 520f), 54f), () =>
                    {
                        string line = Clubs.Join(S, c);
                        LifeSimManager.Save(); Rebuild();
                        Toast(line);
                    }, 19, GoBg);
                y -= 62f;
            }
            Paragraph(c.elite
                ? "INVITATION ONLY: #" + Clubs.EliteOneClubRank + " in any club, or #" + Clubs.EliteEveryClubRank +
                  " or better in every club. " + (st != null && st.invited ? "You have been asked." : "Not yet: " + Clubs.EliteInviteRefusal(S) + ".")
                : no == null ? "You can join now."
                : st != null && st.invited ? "You have been asked in - " + no + "."
                : "Join at their meet (" + c.MeetWhen + ", " + CarMeets.PlaceName.ToLowerInvariant() + ") in a " +
                  c.rule.ToLowerInvariant() + " car: walk up to one of their cars. Or be invited.",
                ref y, no == null || (st != null && st.invited) ? MenuKit.Accent : MenuKit.Dim);
        }

        /// <summary>The club's perks: earned, or the rank that earns it, and
        /// which one is next.</summary>
        void BuildPerks(ClubDef c, ref float y)
        {
            MenuKit.Label(body, "PERKS", MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                TextAnchor.MiddleLeft, MenuKit.Accent, ColW * 0.3f, height: 24f, bold: true);
            var nextPerk = Clubs.NextPerk(S, c);
            if (Clubs.IsMember(S, c))
                MenuKit.FitOneLine(MenuKit.Label(body, nextPerk.HasValue
                        ? "NEXT: #" + nextPerk.Value.rank + " - " + nextPerk.Value.text
                        : "EVERY PERK IS YOURS", MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColR, y),
                    TextAnchor.MiddleRight, MenuKit.Good, ColW * 0.68f, height: 24f), ColW * 0.68f);
            y -= 26f;
            foreach (var p in Clubs.PerksOf(c))
            {
                bool has = Clubs.HasPerk(S, p);
                string line = (has ? "YOURS  " : "AT #" + p.rank + "  ") + p.text + (p.live ? "" : "  (COMING)");
                MenuKit.FitOneLine(MenuKit.Label(body, line, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(ColL + 12f, y), TextAnchor.MiddleLeft,
                    has ? MenuKit.Good : p.live ? Color.white : MenuKit.Dim, ColW - 12f, height: 24f), ColW - 12f);
                y -= 25f;
            }
            y -= 6f;
        }

        /// <summary>The other clubs whose meet or race falls in the same block
        /// as this club's, in words; null when none does.</summary>
        static string ClashLine(ClubDef c)
        {
            var bits = new System.Collections.Generic.List<string>();
            foreach (var o in Clubs.All)
            {
                if (o == c) continue;
                if (o.meetCol == c.meetCol && o.meetSlot == c.meetSlot) bits.Add(o.shortName + " MEET (" + c.MeetWhen + ")");
                if (o.raceCol == c.meetCol && o.raceSlot == c.meetSlot) bits.Add(o.shortName + " RACE (" + c.MeetWhen + ")");
                if (o.meetCol == c.raceCol && o.meetSlot == c.raceSlot) bits.Add(o.shortName + " MEET (" + c.RaceWhen + ")");
                if (o.raceCol == c.raceCol && o.raceSlot == c.raceSlot) bits.Add(o.shortName + " RACE (" + c.RaceWhen + ")");
            }
            return bits.Count == 0 ? null : "CLASHES WITH " + string.Join(", ", bits.ToArray());
        }

        /// <summary>"JAPANESE-BUILT CARS", "1999 MODELS", "ANY CAR".</summary>
        static string RuleCars(ClubDef c) =>
            c.elite || c.rule.EndsWith("MODELS") ? c.rule : c.rule + " CARS";

        int NextClubRaceDay(ClubDef c)
        {
            for (int d = S.day; d < S.day + 8; d++)
                if (c.RaceOn(d) && (d > S.day || S.slotIndex <= c.raceSlot)) return d;
            return S.day + 7;
        }

        /// <summary>The clubs, the player's standing in each, and the clashes.
        /// Tap one for its board and perks.</summary>
        void BuildClubList()
        {
            confirmLeaveClub = false;
            float y = -14f;
            PageHeader(ref y, "CAR CLUBS");
            Paragraph("Join as many as you like - but some meet and race in the same block, and you can only " +
                      "be at one. Join at a club's meet at " + CarMeets.PlaceName.ToLowerInvariant() + ", in a car " +
                      "it takes, or by invitation. Members MUST make the meet and the race every week.", ref y);
            float span = Mathf.Min(ColW, 1000f);
            foreach (var c in Clubs.All)
            {
                var st = Clubs.StandingOf(S, c);
                bool member = st != null && st.member;
                bool barred = st != null && !member && st.readmitDay > 0 && S.day < st.readmitDay;
                bool invited = st != null && st.invited && !member;
                bool owns = Clubs.OwnsQualifying(S, c);
                var cc = c;
                var row = MenuKit.Button(body, "", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(span, 58f),
                    () => { clubView = cc.id; confirmLeaveClub = false; Rebuild(); }, MenuKit.Tiny,
                    member ? new Color(0.20f, 0.30f, 0.24f, 1f) : ClubRowBg);
                row.gameObject.name = "Btn_CLUB_" + c.id;
                var rt = (RectTransform)row.transform;
                float w1 = span * 0.42f, w2 = span * 0.54f;
                MenuKit.FitOneLine(MenuKit.Label(rt, c.name, MenuKit.Tiny, new Vector2(0f, 1f),
                    new Vector2(12f, -4f), TextAnchor.MiddleLeft, MenuKit.Accent, w1, height: 26f, bold: true), w1);
                MenuKit.FitOneLine(MenuKit.Label(rt, c.elite ? "INVITATION ONLY" : RuleCars(c), MenuKit.Tiny,
                    new Vector2(0f, 1f), new Vector2(12f, -30f), TextAnchor.MiddleLeft, Color.white, w1, height: 24f), w1);
                MenuKit.FitOneLine(MenuKit.Label(rt, "MEET " + c.MeetWhen + "  ·  RACE " + c.RaceWhen,
                    MenuKit.Tiny, new Vector2(1f, 1f), new Vector2(-12f, -4f), TextAnchor.MiddleRight,
                    MeetInk, w2, height: 26f), w2);
                string status = member ? "MEMBER  ·  #" + Clubs.PlayerRank(S, c) + " OF " + Clubs.BoardSize +
                                         "  ·  STRIKES " + Clubs.MissesInWindow(S, c, S.day)
                              : invited ? "YOU ARE INVITED - TAP TO ACCEPT"
                              : barred ? "BACK FROM " + LifeRules.DateLabel(st.readmitDay)
                              : c.elite ? "NOT YET ASKED"
                              : owns ? "YOU HAVE A CAR FOR IT" : "NO CAR FOR IT YET";
                MenuKit.FitOneLine(MenuKit.Label(rt, status, MenuKit.Tiny, new Vector2(1f, 1f), new Vector2(-12f, -30f),
                    TextAnchor.MiddleRight, member || invited ? MenuKit.Good : barred ? MenuKit.Bad : MenuKit.Dim,
                    w2, height: 24f), w2);
                y -= 64f;
            }
            var news = S.clubNews;
            if (news != null && news.Count > 0)
            {
                y -= 4f;
                for (int i = news.Count - 1, k = 0; i >= 0 && k < 3; i--, k++)
                {
                    MenuKit.FitOneLine(MenuKit.Label(body, news[i], MenuKit.Tiny, new Vector2(0.5f, 1f),
                        new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f), ColW);
                    y -= 26f;
                }
            }
        }

        // =================== the club race's pre-race page ===================
        /// <summary>The club race the pre-race page is about: the one picked
        /// when two are due at once, else the first.</summary>
        ClubDef DueRaceClub()
        {
            var due = Clubs.RacesDueNow(S);
            if (due.Count == 0) return null;
            var pick = due.Find(c => c.id == clubRacePick);
            return pick ?? due[0];
        }

        /// <summary>
        /// The pre-race page when a club race is DUE in this block: the club's
        /// road, the opponent and the purse are the club's, not the player's to
        /// pick. From the house it is a reminder (DRIVE out, the line starts
        /// it); from the line it is START. Still the one launcher.
        /// </summary>
        void BuildClubPreRace()
        {
            var c = DueRaceClub();
            var due = Clubs.RacesDueNow(S);
            int venue = Clubs.RaceVenue(c, S.day);
            var t = TrackCatalog.At(venue);
            var opp = Clubs.RaceOpponent(S, c, S.day);
            bool fixture = Clubs.IsFixtureWeek(S.day);

            float y = -14f;
            PageHeader(ref y, c.shortName + (fixture ? " CLUB FIXTURE" : " CLUB RACE"));
            float vw = Mathf.Min(ColW, 760f);
            float vx = -vw * 0.5f;
            float mapSize = Mathf.Clamp(vw * 0.31f, 180f, 236f);
            RouteMap.Build(body, t, new Vector2(0.5f, 1f), new Vector2(vx, y), mapSize);
            float top = y;
            float tx = vx + mapSize + 14f, tw = vw - mapSize - 14f;
            void Line(string s, Color ink, float dy, bool bold = false) =>
                MenuKit.FitOneLine(MenuKit.Label(body, s, dy == 0f ? 22 : MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(tx, y - dy), TextAnchor.MiddleLeft, ink, tw, height: dy == 0f ? 26f : 24f,
                    bold: bold), tw);
            Line(t.name, MenuKit.Accent, 0f, true);
            Line(VenueNumbers(t), Color.white, 28f);
            Line(c.name + "  ·  " + LifeRules.SlotNames[S.slotIndex] + " " + TimeOfDay.Label(RaceHour()),
                 MenuKit.Accent, 52f);
            Line((fixture ? "VS " + (opp.club != null ? opp.club.shortName + " " : "") : "VS ") +
                 "#" + opp.rank + " " + opp.alias, MenuKit.Good, 76f, true);
            Line(opp.spec != null ? opp.spec.name : "", MenuKit.Dim, 100f);
            Line("WIN " + MenuKit.Money(Clubs.PurseFor(S.day)) + "  ·  ONE ON ONE  ·  " +
                 (fixture ? "A POINT FOR THE CLUB" : "PASS THEM, TAKE THEIR SPOT"), Color.white, 124f);
            y -= 152f;
            y = Mathf.Min(y, top - mapSize - 10f);

            // TWO CLUB RACES AT ONCE: run one, and the other is a miss.
            if (due.Count > 1)
            {
                foreach (var other in due)
                {
                    if (other == c) continue;
                    var oc = other;
                    MenuKit.Button(body, "RUN THE " + other.shortName + " RACE INSTEAD", new Vector2(0.5f, 1f),
                        new Vector2(0f, y), new Vector2(Mathf.Min(vw, 520f), 44f),
                        () => { clubRacePick = oc.id; Rebuild(); }, 17);
                    y -= 50f;
                }
                MenuKit.FitOneLine(MenuKit.Label(body, "CLASH - THE RACE YOU DO NOT RUN IS A STRIKE IN THAT CLUB",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(vx, y), TextAnchor.MiddleLeft, MenuKit.Bad, vw,
                    height: 24f), vw);
                y -= 28f;
            }

            MenuKit.Rect(body, "Rule", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(vx, y), new Vector2(vw, 2f), MenuKit.Line);
            y -= 8f;

            var car = S.ActiveCar;
            string wrong = Clubs.RaceCarRefusal(S, c);
            if (raceFromLine)
            {
                MenuKit.Label(body, "THE CAR YOU DROVE OUT", MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(vx, y), TextAnchor.MiddleLeft, MenuKit.Dim, vw, height: 22f);
                y -= 24f;
                if (car != null) CarPickRow(ref y, vx, vw, car, true, wrong == null ? "QUALIFIES" : "WRONG CAR", null);
            }
            else
            {
                MenuKit.Label(body, "PICK A CAR  ·  THE CLUB RUNS " + c.rule, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(vx, y), TextAnchor.MiddleLeft, MenuKit.Dim, vw, height: 22f);
                y -= 24f;
                foreach (var owned in S.cars)
                {
                    var captured = owned;
                    bool driving = owned.id == S.activeCar;
                    bool fits = Clubs.Fits(c, owned);
                    CarPickRow(ref y, vx, vw, owned, driving,
                        fits ? (driving ? "THIS ONE - QUALIFIES" : "QUALIFIES") : "NOT " + c.rule,
                        CarWhere.Away(S, owned) ? null : (UnityEngine.Events.UnityAction)(() =>
                        {
                            S.activeCar = captured.id;
                            LifeSimManager.Save();
                            Rebuild();
                        }));
                }
            }
            y -= 6f;

            float bw = Mathf.Min(vw, 560f);
            if (!raceFromLine)
            {
                MenuKit.Para(body,
                    "REQUIRED. DRIVE out in a " + c.rule + " car before the " + LifeRules.SlotNames[S.slotIndex] +
                    " block ends - the line at the end of the street starts it. Miss it, or turn up in " +
                    "the wrong car, and it is a strike.",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(vx + vw * 0.5f, y - 2f), out float h,
                    TextAnchor.UpperCenter, wrong == null ? MenuKit.Accent : MenuKit.Bad, vw);
                y -= h + 6f;
                return;
            }

            bool carGone = CarWhere.Away(S, car);
            bool lowFuel = car != null && car.fuel <= LifeRules.RequiredFuelPct(t, car);
            string label = car == null ? "NO CAR TO RACE"
                : carGone ? "YOUR CAR IS AT " + CarWhere.ShopName(CarWhere.PlaceOf(S, car))
                : wrong != null ? wrong
                : lowFuel ? "LOW FUEL - FILL UP FIRST"
                : !opp.Valid || opp.spec == null ? "NOBODY TURNED UP"
                : "START  >>";
            bool can = car != null && !carGone && wrong == null && !lowFuel && opp.Valid && opp.spec != null;
            var club = c;
            MenuKit.Button(body, label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(bw, 64f),
                can ? (UnityEngine.Events.UnityAction)(() => StartClubRace(club)) : null, 21,
                can ? RaceBg : MenuKit.BtnBgDisabled);
            y -= 70f;
        }

        /// <summary>A club race, launched from the line: one on one, the
        /// club's road, the block's own hour. Attendance is committed here.</summary>
        void StartClubRace(ClubDef c)
        {
            if (!CarIsHere()) return;
            if (c == null || !Clubs.RacesDueNow(S).Contains(c)) { Rebuild(); return; }
            int venue = Clubs.RaceVenue(c, S.day);
            if (!VenueHere(venue)) return;
            var opp = Clubs.RaceOpponent(S, c, S.day);
            if (!opp.Valid || opp.spec == null) { Toast("nobody turned up to race"); return; }

            driveUnpaid = false;
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar != null ? S.ActiveCar.specId : "";
            RaceHandoff.TrackIndex = venue;
            RaceHandoff.TimeOfDayIndex = RaceHour();
            RaceHandoff.TrafficLevel = -1;
            RaceHandoff.StartFuelPct = S.ActiveCar != null ? S.ActiveCar.fuel : 100f;
            RaceHandoff.PurseWin = Clubs.PurseFor(S.day);
            RaceHandoff.OpponentSpecIds = opp.spec.id;
            RaceHandoff.OpponentSkills = opp.skill.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture);
            RaceHandoff.ClubRace = true;
            RaceHandoff.ClubOpponent = opp.alias;
            RaceHandoff.ClubRaceDay = S.day;
            RaceHandoff.ClubRaceClub = c.id;
            // COMMITTED AT THE START LINE: attended, whatever happens next.
            Clubs.BeginRace(S, c);
            FillCarRequest();
            LifeSimManager.Save();
            SceneGate.Load(TrackCatalog.SceneIndex(venue));
        }
    }
}
