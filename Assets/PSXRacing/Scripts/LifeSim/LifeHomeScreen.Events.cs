using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// EVENTS (v29, owner 2026-11-15): the EVENTS page off MAIN (beside WEEK)
    /// with its SANCTIONED and STREET lists, one event's page (rule badges,
    /// money, each race's road, date, block, field and prizes 1-6, SIGN UP /
    /// WITHDRAW), the event race's pre-race page (still the one launcher: the
    /// line starts it), and the words the calendar and the club pages say about
    /// events. The rules live in Events.cs.
    /// </summary>
    public partial class LifeHomeScreen
    {
        /// <summary>The list on show: "sanctioned" or "street".</summary>
        string evList = "sanctioned";
        /// <summary>The event page's event, by id; empty = the lists.</summary>
        string evView = "";
        bool evConfirmWithdraw;
        /// <summary>The block (Duty.Ord) in which the player chose the event
        /// race over a club race due at the same time; -1 = none.</summary>
        int evOverClubOrd = -1;

        bool EventOverClub => evOverClubOrd == Duty.NowOrd(S);

        static readonly Color EvSignedBg = new Color(0.20f, 0.30f, 0.24f, 1f);
        static readonly Color BadgeBg = new Color(0.08f, 0.08f, 0.10f, 1f);
        static readonly Color DotEmpty = new Color(0.22f, 0.22f, 0.24f, 1f);

        // =================== hooks the shared screen calls ===================

        /// <summary>MAIN's day view: WEEK and EVENTS side by side under the
        /// blocks. False (and nothing drawn) when events are off.</summary>
        bool WeekAndEventsRow(float x, float y, float w)
        {
            if (!Events.On) return false;
            float half = (w - 6f) * 0.5f;
            MenuKit.Button(body, "WEEK", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x, half), y),
                new Vector2(half, 44f), () => { calView = CalView.Week; Rebuild(); }, 18);
            int n = Events.SignedUpCount(S);
            bool due = Events.DueNow(S).Valid;
            var b = MenuKit.Button(body, n > 0 ? "EVENTS · " + n : "EVENTS", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x + half + 6f, half), y), new Vector2(half, 44f),
                () => { tab = "events"; evView = ""; evConfirmWithdraw = false; Rebuild(); }, 18,
                due ? RaceBg : (Color?)null);
            // By hand: the caption changes, and the cursor is restored BY NAME.
            b.gameObject.name = "Btn_EVENTS";
            return true;
        }

        /// <summary>The hub's race row under a signed-up race due now: it opens
        /// the pre-race page. A club race due too keeps the row unless the
        /// player chose the event on the club's page.</summary>
        void EventRaceRow(bool clubDue, ref string label, ref UnityEngine.Events.UnityAction act, ref Color bg)
        {
            var d = Events.DueNow(S);
            if (!d.Valid || (clubDue && !EventOverClub)) return;
            label = Clip(d.ev.shortName + " · RACE " + (d.race + 1) + " OF " + d.ev.races.Length, 28);
            act = () => { tab = "prerace"; Rebuild(); };
            bg = RaceBg;
        }

        /// <summary>The pre-race page belongs to the event race due now - unless
        /// a club race is due too and the player has not picked the event.</summary>
        bool EventPreRaceFirst()
        {
            var d = Events.DueNow(S);
            return d.Valid && (Clubs.RaceDueNow(S) == null || EventOverClub);
        }

        /// <summary>BACK from one event's page is the lists, not MAIN.</summary>
        bool EventsBack()
        {
            if (tab != "events" || string.IsNullOrEmpty(evView)) return false;
            evView = "";
            evConfirmWithdraw = false;
            Rebuild();
            return true;
        }

        /// <summary>
        /// What the calendar says about a block holding a signed-up race:
        /// "EVENT · ROOKIE" over "RACE 1 OF 2" (or a clash) ahead, the place
        /// and prize after, NO-SHOW / WRONG CAR / DNF for a race not run.
        /// </summary>
        bool EventBlockLines(int day, int slot, bool past, out (string, Color, string, Color) lines)
        {
            lines = default;
            if (!Events.On) return false;
            var d = Events.RaceAt(S, day, slot);
            if (!d.Valid) return false;
            int p = Events.PlaceOf(d.entry, d.race);
            string l1 = "EVENT · " + d.ev.shortName;
            if (p >= 1)
            {
                int prize = d.entry.prize != null && d.race < d.entry.prize.Count ? d.entry.prize[d.race] : 0;
                lines = (l1, MenuKit.Good, "P" + p + (prize > 0 ? " · " + MenuKit.Money(prize) : ""),
                         p == 1 ? MenuKit.Accent : MenuKit.Dim);
                return true;
            }
            if (p == Events.NoShow || p == Events.WrongCar || p == Events.Dnf || (past && p == Events.Pending))
            {
                lines = (l1, MenuKit.Bad, p == Events.WrongCar ? "WRONG CAR" : p == Events.Dnf ? "DNF" : "NO-SHOW",
                         MenuKit.Bad);
                return true;
            }
            string l2 = "RACE " + (d.race + 1) + " OF " + d.ev.races.Length;
            Color c2 = MenuKit.Good;
            var obs = Duty.At(S, day, slot);
            if (obs.Count > 0) { l2 = "CLASHES WITH " + Duty.Word(obs[0]); c2 = MenuKit.Bad; }
            else if (LifeRules.BookingAt(S, day, slot) != null) { l2 = "CLASHES WITH A BOOKED RACE"; c2 = MenuKit.Bad; }
            lines = (l1, MenuKit.Accent, l2, c2);
            return true;
        }

        /// <summary>The week grid's word for an event line, or null.</summary>
        string EventCellWord(string line)
        {
            if (line.StartsWith("EVENT · ")) return "EVENT";
            if (line.StartsWith("RACE ") && line.Contains(" OF "))
            {
                var bits = line.Split(' ');
                if (bits.Length >= 4) return "RACE " + bits[1] + "/" + bits[3];
            }
            if (line.Length >= 2 && line[0] == 'P' && char.IsDigit(line[1]))
            {
                int sp = line.IndexOf(' ');
                return sp > 0 ? line.Substring(0, sp) : line;
            }
            return null;
        }

        /// <summary>The club list's EVENT STANDINGS: every club by event points.</summary>
        void ClubStandingsBlock(ref float y)
        {
            if (!Events.On) return;
            MenuKit.Label(body, "EVENT STANDINGS  ·  CLUB POINTS", MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 26f, bold: true);
            y -= 28f;
            var st = Events.Standings(S);
            float colW = (ColW - 20f) * 0.5f;
            int rows = (st.Count + 1) / 2;
            for (int i = 0; i < st.Count; i++)
            {
                var c = st[i].Key;
                bool member = Clubs.IsMember(S, c);
                string text = "#" + (i + 1) + "  " + c.name + "  " + st[i].Value + " PTS" +
                              (member ? "  ·  YOURS " + Events.MyPointsOf(S, c.id) : "");
                float x = ColL + (i % 2) * (colW + 20f);
                MenuKit.FitOneLine(MenuKit.Label(body, text, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(x, y - (i / 2) * 26f), TextAnchor.MiddleLeft, member ? MenuKit.Good : Color.white,
                    colW, height: 24f), colW);
            }
            y -= rows * 26f + 8f;
        }

        /// <summary>One club's event line for its page, or null when events are off.</summary>
        string ClubEventLine(ClubDef c)
        {
            if (!Events.On || c == null) return null;
            int rank = Events.StandingRank(S, c);
            string line = "EVENT POINTS " + Events.PointsOf(S, c.id) + " (#" + rank + " OF " + Clubs.All.Length + ")";
            if (Clubs.IsMember(S, c))
            {
                int mine = Events.MyPointsOf(S, c.id);
                int next = (mine / Events.PointsPerPlace + 1) * Events.PointsPerPlace;
                line += "  ·  YOURS " + mine + "  ·  NEXT PLACE UP AT " + next;
            }
            return line;
        }

        /// <summary>The club race's page, when an event race is due in the same
        /// block: run the event instead (the club race is then a miss).</summary>
        void EventInsteadButton(ref float y, float vw)
        {
            var d = Events.DueNow(S);
            if (!d.Valid) return;
            MenuKit.Button(body, "RUN THE " + d.ev.shortName + " RACE INSTEAD", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(Mathf.Min(vw, 520f), 44f),
                () => { evOverClubOrd = Duty.NowOrd(S); Rebuild(); }, 17);
            y -= 50f;
            MenuKit.Para(body, "CLASH - YOU SIGNED UP FOR THE " + d.ev.name + " IN THIS BLOCK. RUN ONE: THE OTHER IS A MISS",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(-vw * 0.5f, y - 2f), out float h, TextAnchor.UpperLeft,
                MenuKit.Bad, vw);
            y -= h + 8f;
        }

        // =================== the EVENTS page ===================

        void BuildEvents()
        {
            if (!Events.On) { tab = "main"; BuildMain(); return; }
            var ev = Events.ById(evView);
            if (ev != null && Events.Visible(S, ev)) { BuildEventPage(ev); return; }
            evView = "";
            BuildEventList();
        }

        void BuildEventList()
        {
            evConfirmWithdraw = false;
            float y = -14f;
            PageHeader(ref y, "EVENTS");
            bool street = evList == "street";
            float span = Mathf.Min(ColW, 1000f);
            float half = (span - 8f) * 0.5f;
            float x0 = -span * 0.5f;
            var bs = MenuKit.Button(body, "SANCTIONED", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(x0, half), y),
                new Vector2(half, 46f), () => { evList = "sanctioned"; Rebuild(); }, 18);
            var bt = MenuKit.Button(body, "STREET", new Vector2(0.5f, 1f),
                new Vector2(MenuKit.ColLeft(x0 + half + 8f, half), y), new Vector2(half, 46f),
                () => { evList = "street"; Rebuild(); }, 18);
            MenuKit.MarkTab(bs, !street);
            MenuKit.MarkTab(bt, street);
            y -= 54f;

            MenuKit.FitOneLine(MenuKit.Label(body, "RACING REP " + Mathf.FloorToInt(S.racingRep) + "  ·  STREET REP " +
                    Mathf.FloorToInt(S.streetRep) + "  ·  SIGNED UP " + Events.SignedUpCount(S), MenuKit.Tiny,
                    new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft, Color.white, ColW, height: 26f,
                    bold: true), ColW);
            y -= 30f;
            Paragraph(street
                ? "Public roads at night, Friday to Sunday, with the night's traffic on them. The pot is the buy-ins. " +
                  "Damage is yours. Street rep " + Events.StreetListRep + " gets you on the lists; car meets and your " +
                  "clubs pass the rest on."
                : "No traffic: Saturday and Sunday, MORNING or NOON. Pay the entry to sign up and the " +
                  "races go in your calendar. Every race pays places 1-6; win them all for the bonus.", ref y);

            int shown = 0, hidden = 0;
            foreach (var ev in Events.OfKind(street))
            {
                if (!Events.Visible(S, ev)) { hidden++; continue; }
                EventRow(ev, ref y, span);
                shown++;
            }
            if (street && hidden > 0)
                Paragraph(shown == 0
                    ? "Nobody has told you where they run yet. Street rep " + Events.StreetListRep +
                      " puts you on the lists; car meets and your clubs pass the word on."
                    : hidden + " more run that nobody has told you about. Car meets and your clubs pass them on.",
                    ref y, MenuKit.Accent);

            var news = S.evNews;
            if (news != null && news.Count > 0)
            {
                y -= 4f;
                MenuKit.Label(body, "RESULTS", MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(ColL, y),
                    TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 24f, bold: true);
                y -= 26f;
                for (int i = news.Count - 1, k = 0; i >= 0 && k < 4; i--, k++)
                {
                    MenuKit.FitOneLine(MenuKit.Label(body, news[i], MenuKit.Tiny, new Vector2(0.5f, 1f),
                        new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Dim, ColW, height: 24f), ColW);
                    y -= 26f;
                }
            }
        }

        /// <summary>One event on a list: emblem, name, when, badges, progress
        /// dots and where the player stands. Tap for the event's page.</summary>
        void EventRow(EventDef ev, ref float y, float span)
        {
            var e = Events.ActiveEntry(S, ev);
            int n = Events.ShownEdition(S, ev);
            string no = e == null ? Events.SignUpRefusal(S, ev) : null;
            var cev = ev;
            const float H = 100f;
            var row = MenuKit.Button(body, "", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(span, H),
                () => { evView = cev.id; evConfirmWithdraw = false; Rebuild(); }, MenuKit.Tiny,
                e != null ? EvSignedBg : ClubRowBg);
            row.gameObject.name = "Btn_EVENT_" + ev.id;
            var rt = (RectTransform)row.transform;
            Emblem(rt, ev, new Vector2(0f, 1f), new Vector2(14f, -14f), 72f);
            float tx = 100f, rightW = Mathf.Min(300f, span * 0.30f), midW = span - tx - rightW - 24f;
            MenuKit.FitOneLine(MenuKit.Label(rt, ev.name, MenuKit.Small, new Vector2(0f, 1f), new Vector2(tx, -6f),
                TextAnchor.MiddleLeft, MenuKit.Accent, midW, height: 28f, bold: true), midW);
            string when = e != null ? NextRaceWords(ev, e)
                : "NEXT " + Events.WhenLine(ev, n, 0) + (ev.races.Length > 1 ? " + " + (ev.races.Length - 1) + " MORE" : "");
            MenuKit.FitOneLine(MenuKit.Label(rt, when + "  ·  ENTRY " + MenuKit.Money(ev.fee), MenuKit.Tiny,
                new Vector2(0f, 1f), new Vector2(tx, -36f), TextAnchor.MiddleLeft, Color.white, midW, height: 24f), midW);
            BadgeRow(rt, ev, new Vector2(0f, 1f), tx, -66f, midW);
            Dots(rt, ev, e, new Vector2(-14f, -10f));
            string status = e != null ? "SIGNED UP"
                : no == null ? "OPEN"
                : no.StartsWith("needs") ? "NEEDS " + (ev.street ? "STREET" : "RACING") + " REP " + ev.repNeed
                : no.StartsWith("you own no") ? "NO CAR FOR IT"
                : no.StartsWith("the entry") ? "ENTRY " + MenuKit.Money(ev.fee)
                : no.StartsWith("clashes") ? "CLASHES"
                : no.StartsWith("you withdrew") ? "WITHDRAWN" : "CLOSED";
            MenuKit.FitOneLine(MenuKit.Label(rt, status, MenuKit.Tiny, new Vector2(1f, 1f), new Vector2(-14f, -40f),
                TextAnchor.MiddleRight, e != null ? MenuKit.Good : no == null ? Color.white : MenuKit.Dim, rightW,
                height: 24f, bold: e != null), rightW);
            string prize = "1ST " + MenuKit.Money(ev.prizes[0]) + "  ·  " + ev.races.Length + " RACES";
            MenuKit.FitOneLine(MenuKit.Label(rt, prize, MenuKit.Tiny, new Vector2(1f, 1f), new Vector2(-14f, -66f),
                TextAnchor.MiddleRight, MenuKit.Dim, rightW, height: 24f), rightW);
            y -= H + 6f;
        }

        /// <summary>"RACE 2 SUN 22 NOV · MORNING" for a live entry.</summary>
        static string NextRaceWords(EventDef ev, EventEntry e)
        {
            int r = Events.NextPending(e, ev);
            return r < 0 ? "ALL RUN" : "RACE " + (r + 1) + " " + Events.WhenLine(ev, e.edition, r);
        }

        /// <summary>The emblem: the event's letters on its colour, framed.</summary>
        void Emblem(RectTransform parent, EventDef ev, Vector2 anchor, Vector2 pos, float size)
        {
            var frame = MenuKit.Rect(parent, "Emblem", anchor, new Vector2(0f, 1f), pos, new Vector2(size, size),
                MenuKit.Accent);
            frame.GetComponent<Image>().raycastTarget = false;
            var fill = MenuKit.Rect(frame, "Fill", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size - 6f, size - 6f), ev.color);
            fill.GetComponent<Image>().raycastTarget = false;
            var band = MenuKit.Rect(fill, "Band", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f),
                new Vector2(size - 18f, 4f), new Color(1f, 1f, 1f, 0.55f));
            band.GetComponent<Image>().raycastTarget = false;
            int len = ev.mono != null ? ev.mono.Length : 1;
            var t = MenuKit.Label(fill, ev.mono, len >= 3 ? MenuKit.Small : MenuKit.Head, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 4f), TextAnchor.MiddleCenter, Color.white, size - 8f, height: size - 16f, bold: true);
            t.raycastTarget = false;
        }

        /// <summary>The rule as small framed badges, left to right; a badge
        /// that would run past the room is left off (the event page wraps the
        /// same words in full).</summary>
        void BadgeRow(RectTransform parent, EventDef ev, Vector2 anchor, float x, float y, float room)
        {
            float cx = 0f;
            foreach (var word in Events.Badges(ev))
            {
                var t = MenuKit.Label(parent, word, MenuKit.Tiny, anchor, Vector2.zero, TextAnchor.MiddleCenter,
                    word.Contains("REP") ? MenuKit.Accent : Color.white, 400f, height: 24f, bold: true);
                float w = Mathf.Ceil(t.preferredWidth) + 16f;
                if (cx + w > room) { Object.DestroyImmediate(t.gameObject); break; }
                var bg = MenuKit.Rect(parent, "Badge", anchor, new Vector2(0f, 1f), new Vector2(x + cx, y),
                    new Vector2(w, 24f), BadgeBg);
                bg.GetComponent<Image>().raycastTarget = false;
                var edge = MenuKit.Rect(bg, "Edge", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                    new Vector2(w, 2f), MenuKit.Line);
                edge.GetComponent<Image>().raycastTarget = false;
                t.transform.SetParent(bg, false);
                var lrt = t.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = Vector2.zero;
                lrt.sizeDelta = new Vector2(w, 24f);
                cx += w + 6f;
            }
        }

        /// <summary>Progress dots, one a race, at a top-right corner: gold =
        /// won, green = placed, red = missed, dark = still to run.</summary>
        void Dots(RectTransform parent, EventDef ev, EventEntry e, Vector2 topRight)
        {
            int n = ev.races.Length;
            for (int i = 0; i < n; i++)
            {
                int p = Events.PlaceOf(e, i);
                Color c = e == null || p == Events.Pending || p == Events.Withdrawn ? DotEmpty
                        : p == 1 ? MenuKit.Accent : p > 1 ? MenuKit.Good : MenuKit.Bad;
                var d = MenuKit.Rect(parent, "Dot", new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(topRight.x - (n - 1 - i) * 26f, topRight.y), new Vector2(18f, 18f), c);
                d.GetComponent<Image>().raycastTarget = false;
            }
        }

        // =================== one event ===================

        void BuildEventPage(EventDef ev)
        {
            float y = -14f;
            var e = Events.ActiveEntry(S, ev);
            int n = Events.ShownEdition(S, ev);
            MenuKit.Button(body, "<  ALL EVENTS", new Vector2(0.5f, 1f), new Vector2(MenuKit.ColLeft(ColL, 220f), y),
                new Vector2(220f, 40f), () => { evView = ""; evConfirmWithdraw = false; Rebuild(); }, 17);
            MenuKit.FitOneLine(MenuKit.Label(body, ev.name, MenuKit.Small, new Vector2(0.5f, 1f),
                new Vector2(ColL + 234f, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW - 234f, height: 40f, bold: true),
                ColW - 234f);
            y -= 50f;

            // The emblem, what kind of event, and the rule as badges.
            Emblem(body, ev, new Vector2(0.5f, 1f), new Vector2(ColL, y), 72f);
            float bx = ColL + 88f, bw = ColW - 88f;
            MenuKit.FitOneLine(MenuKit.Label(body, ev.street
                    ? "STREET  ·  PUBLIC ROADS AT NIGHT  ·  TRAFFIC  ·  DAMAGE IS YOURS"
                    : "SANCTIONED  ·  NO TRAFFIC  ·  MARSHALS ON THE COURSE", MenuKit.Tiny, new Vector2(0.5f, 1f),
                new Vector2(bx, y - 4f), TextAnchor.MiddleLeft, ev.street ? MeetInk : MenuKit.Good, bw, height: 24f,
                bold: true), bw);
            BadgeRow(body, ev, new Vector2(0.5f, 1f), bx, y - 40f, bw);
            y -= 84f;
            Paragraph(ev.blurb, ref y, Color.white);

            float rep = Events.RepFor(S, ev);
            DutyRow("ENTRY", MenuKit.Money(ev.fee) + (e != null ? "  ·  PAID" : "  ·  PAID AT SIGN-UP"), ref y);
            DutyRow("BONUS", MenuKit.Money(ev.bonus) + " FOR WINNING ALL " + ev.races.Length, ref y, MenuKit.Accent);
            DutyRow(ev.street ? "STREET REP" : "RACING REP",
                Mathf.FloorToInt(rep) + (ev.repNeed > 0 ? "  ·  NEEDS " + ev.repNeed : "  ·  NONE NEEDED"), ref y,
                rep >= ev.repNeed ? MenuKit.Good : MenuKit.Bad);
            var car = S.ActiveCar;
            string wrong = Events.CarRefusal(ev, car);
            DutyRow("YOUR CAR", car == null ? "NONE" : LifeRules.ShortName(car) + "  ·  " +
                (wrong == null ? "QUALIFIES" : "WRONG CAR - " + wrong), ref y, wrong == null ? MenuKit.Good : MenuKit.Bad);
            y -= 4f;

            // ---- the door: SIGN UP, or WITHDRAW ----
            float bwid = Mathf.Min(ColW, 520f);
            if (e == null)
            {
                string no = Events.SignUpRefusal(S, ev);
                var cev = ev;
                MenuKit.Button(body, no == null ? "SIGN UP  ·  " + MenuKit.Money(ev.fee) : "CAN'T SIGN UP",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(bwid, 54f),
                    no == null ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        bool ok = Events.SignUp(S, cev, out string line);
                        LifeSimManager.Save(); Rebuild();
                        Toast(ok ? line : line.ToUpperInvariant());
                    }) : null, 19, no == null ? GoBg : MenuKit.BtnBgDisabled);
                y -= 62f;
                Paragraph(no != null ? Cap(no) + "."
                    : "Signing up pays the entry and writes every race below into your calendar. Miss one and it is " +
                      "a no-show: no prize, the entry kept, rep lost. Withdraw before the day for nothing back.",
                    ref y, no != null ? MenuKit.Bad : MenuKit.Dim);
            }
            else
            {
                string no = Events.WithdrawRefusal(S, ev);
                var cev = ev;
                MenuKit.Button(body, no != null ? "SIGNED UP" : evConfirmWithdraw ? "PRESS AGAIN TO WITHDRAW" : "WITHDRAW",
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(bwid, 50f),
                    no == null ? (UnityEngine.Events.UnityAction)(() =>
                    {
                        if (!evConfirmWithdraw) { evConfirmWithdraw = true; Rebuild(); return; }
                        evConfirmWithdraw = false;
                        Events.Withdraw(S, cev, out string line);
                        LifeSimManager.Save(); Rebuild();
                        Toast(line);
                    }) : null, 18,
                    no != null ? MenuKit.BtnBgDisabled
                    : evConfirmWithdraw ? (Color?)new Color(0.44f, 0.16f, 0.14f, 1f) : null);
                y -= 58f;
                Paragraph(no != null ? "SIGNED UP. " + Cap(no) + "."
                    : "Signed up. Withdrawing gives nothing back - but it costs no rep, and a no-show does.",
                    ref y, MenuKit.Accent);
            }

            // ---- the races ----
            MenuKit.Label(body, "THE RACES  ·  " + (e != null ? "YOUR ENTRY" : "NEXT RUNNING"), MenuKit.Tiny,
                new Vector2(0.5f, 1f), new Vector2(ColL, y), TextAnchor.MiddleLeft, MenuKit.Accent, ColW, height: 26f,
                bold: true);
            y -= 30f;
            for (int r = 0; r < ev.races.Length; r++) RaceCard(ev, n, e, r, ref y);
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>One race of a running: road, day and block, traffic, the
        /// field's rough power, prizes 1-6, and how it went.</summary>
        void RaceCard(EventDef ev, int n, EventEntry e, int r, ref float y)
        {
            float cw = Mathf.Min(ColW, 1000f), cx = -cw * 0.5f;
            var bg = MenuKit.Rect(body, "RaceCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
                new Vector2(cw, 10f), ClubRowBg);
            bg.GetComponent<Image>().raycastTarget = false;
            float top = y, ly = y - 8f, tx = cx + 14f, tw = cw - 28f;
            int v = Events.VenueOf(ev, r);
            var t = v >= 0 ? TrackCatalog.At(v) : null;

            var (word, ink) = RaceState(ev, n, e, r);
            float sw = Mathf.Min(320f, tw * 0.40f);
            MenuKit.FitOneLine(MenuKit.Label(body, "RACE " + (r + 1) + "  ·  " + (t != null ? t.name : "NOT IN THIS EDITION"),
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(tx, ly), TextAnchor.MiddleLeft, Color.white,
                tw - sw - 10f, height: 26f, bold: true), tw - sw - 10f);
            if (word.Length > 0)
                MenuKit.FitOneLine(MenuKit.Label(body, word, MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(tx + tw, ly), TextAnchor.MiddleRight, ink, sw, height: 26f, bold: true), sw);
            ly -= 30f;
            void Line(string s, Color c)
            {
                MenuKit.Para(body, s, MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(tx, ly), out float h,
                    TextAnchor.UpperLeft, c, tw);
                ly -= h + 4f;
            }
            int hour = Events.HourFor(ev, n, r);
            int traffic = Events.TrafficFor(ev, t);
            Line(Events.WhenLine(ev, n, r) + " " + TimeOfDay.At(hour).clock + "  ·  " +
                 (traffic == TrafficLevels.None ? "NO TRAFFIC" : TrafficLevels.Name(traffic) + " TRAFFIC"), MeetInk);
            var f = Events.FieldFor(ev, n, r);
            Line("FIELD ~" + f.roughHp + " HP  ·  " + (f.cars.Count + 1) + " CARS" +
                 (t != null ? "  ·  " + VenueNumbers(t) : ""), MenuKit.Dim);
            var sb = new System.Text.StringBuilder();
            string[] ord = { "1ST", "2ND", "3RD", "4TH", "5TH", "6TH" };
            for (int i = 0; i < ev.prizes.Length && i < ord.Length; i++)
            {
                if (i > 0) sb.Append("   ");
                sb.Append(ord[i]).Append(' ').Append(MenuKit.Money(ev.prizes[i]));
            }
            Line(sb.ToString(), Color.white);
            bg.sizeDelta = new Vector2(cw, top - ly + 4f);
            y = ly - 10f;
        }

        /// <summary>Where one race of a running stands, in a word and a colour.</summary>
        (string, Color) RaceState(EventDef ev, int n, EventEntry e, int r)
        {
            if (e == null) return ("", Color.white);
            int p = Events.PlaceOf(e, r);
            if (p >= 1)
            {
                int prize = e.prize != null && r < e.prize.Count ? e.prize[r] : 0;
                return ((p == 1 ? "WON  ·  " : "P" + p + "  ·  ") + (prize > 0 ? MenuKit.Money(prize) : "NO PRIZE"),
                        p == 1 ? MenuKit.Accent : MenuKit.Good);
            }
            if (p == Events.NoShow) return ("NO-SHOW", MenuKit.Bad);
            if (p == Events.WrongCar) return ("WRONG CAR - NO-SHOW", MenuKit.Bad);
            if (p == Events.Dnf) return ("DID NOT FINISH", MenuKit.Bad);
            if (p == Events.Withdrawn) return ("WITHDRAWN", MenuKit.Dim);
            int day = Events.RaceDay(ev, n, r), slot = Events.RaceSlot(ev, r);
            if (day == S.day && slot == S.slotIndex) return ("NOW - DRIVE OUT", MenuKit.Accent);
            if (Duty.Ord(day, slot) < Duty.NowOrd(S)) return ("NO-SHOW", MenuKit.Bad);
            if (day == S.day) return ("TODAY", MenuKit.Accent);
            return ("SIGNED UP", MenuKit.Good);
        }

        // =================== the event race's pre-race page ===================

        /// <summary>
        /// The pre-race page when a signed-up race is due in this block: the
        /// event's road, field, hour, traffic and prizes are the event's, not
        /// the player's to pick. From the house it is a reminder (DRIVE out,
        /// the line starts it); from the line it is START, after the rule is
        /// checked against the car you brought - a wrong car is a no-show.
        /// </summary>
        void BuildEventPreRace()
        {
            var d = Events.DueNow(S);
            var ev = d.ev;
            int venue = Events.VenueOf(ev, d.race);
            var t = TrackCatalog.At(venue >= 0 ? venue : TrackCatalog.FirstOffered());
            int hour = Events.HourFor(ev, d.entry.edition, d.race);
            int traffic = Events.TrafficFor(ev, t);
            var field = Events.FieldFor(ev, d.entry.edition, d.race);

            float y = -14f;
            PageHeader(ref y, ev.shortName + " · RACE " + (d.race + 1) + " OF " + ev.races.Length);
            float vw = Mathf.Min(ColW, 760f);
            float vx = -vw * 0.5f;
            float mapSize = Mathf.Clamp(vw * 0.31f, 180f, 236f);
            RouteMap.Build(body, t, new Vector2(0.5f, 1f), new Vector2(vx, y), mapSize);
            float top = y;
            float tx = vx + mapSize + 14f, tw = vw - mapSize - 14f;
            float ly = y;
            void Line(string s, Color ink, int size = MenuKit.Tiny)
            {
                MenuKit.Para(body, s, size, new Vector2(0.5f, 1f), new Vector2(tx, ly - 1f), out float h,
                    TextAnchor.UpperLeft, ink, tw);
                ly -= h + 4f;
            }
            Line(t.name, MenuKit.Accent, 22);
            Line(VenueNumbers(t), Color.white);
            Line(ev.name + "  ·  " + LifeRules.SlotNames[S.slotIndex] + " " + TimeOfDay.At(hour).clock + "  ·  " +
                 (traffic == TrafficLevels.None ? "NO TRAFFIC" : TrafficLevels.Name(traffic) + " TRAFFIC"),
                 ev.street ? MeetInk : MenuKit.Good);
            Line("FIELD ~" + field.roughHp + " HP  ·  " + (field.cars.Count + 1) + " CARS", Color.white);
            Line("1ST " + MenuKit.Money(ev.prizes[0]) + "  ·  2ND " + MenuKit.Money(ev.prizes[1]) + "  ·  3RD " +
                 MenuKit.Money(ev.prizes[2]) + "  ·  4TH " + MenuKit.Money(ev.prizes[3]) + "  ·  5TH " +
                 MenuKit.Money(ev.prizes[4]) + "  ·  6TH " + MenuKit.Money(ev.prizes[5]), Color.white);
            bool bonusLive = true;
            for (int i = 0; i < d.race; i++) if (Events.PlaceOf(d.entry, i) != 1) bonusLive = false;
            Line(bonusLive ? "WIN EVERY RACE: BONUS " + MenuKit.Money(ev.bonus) : "NO BONUS THIS TIME - A RACE GOT AWAY",
                 bonusLive ? MenuKit.Accent : MenuKit.Dim);
            y = ly - 8f;
            y = Mathf.Min(y, top - mapSize - 10f);

            // A CLUB RACE DUE AT ONCE: run one, and the other is a miss.
            var club = Clubs.RaceDueNow(S);
            if (club != null)
            {
                MenuKit.Button(body, "RUN THE " + club.shortName + " CLUB RACE INSTEAD", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(Mathf.Min(vw, 520f), 44f),
                    () => { evOverClubOrd = -1; Rebuild(); }, 17);
                y -= 50f;
                MenuKit.Para(body, "CLASH - THE " + club.shortName + " CLUB RACE IS NOW TOO. THE ONE YOU DO NOT RUN IS A MISS",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(vx, y - 2f), out float ch, TextAnchor.UpperLeft,
                    MenuKit.Bad, vw);
                y -= ch + 8f;
            }

            MenuKit.Rect(body, "Rule", new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(vx, y), new Vector2(vw, 2f), MenuKit.Line);
            y -= 8f;

            var car = S.ActiveCar;
            string wrong = Events.CarRefusal(ev, car);
            if (raceFromLine)
            {
                MenuKit.Label(body, "THE CAR YOU DROVE OUT  ·  " + Events.RuleWords(ev), MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(vx, y), TextAnchor.MiddleLeft, MenuKit.Dim, vw, height: 22f);
                y -= 24f;
                if (car != null) CarPickRow(ref y, vx, vw, car, true, wrong == null ? "QUALIFIES" : "WRONG CAR", null);
                // AT THE LINE IN A CAR THE RULE TURNS AWAY: it cannot start,
                // and it counts as a no-show (the clubs' rule for the wrong car).
                if (wrong != null)
                {
                    string line = Events.MarkWrongCar(S, d, wrong);
                    if (line != null) { LifeSimManager.Save(); Toast(line); }
                }
            }
            else
            {
                MenuKit.Label(body, "PICK A CAR  ·  " + Events.RuleWords(ev), MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(vx, y), TextAnchor.MiddleLeft, MenuKit.Dim, vw, height: 22f);
                y -= 24f;
                foreach (var owned in S.cars)
                {
                    var captured = owned;
                    bool fits = Events.CarRefusal(ev, owned) == null;
                    CarPickRow(ref y, vx, vw, owned, owned.id == S.activeCar, fits ? "QUALIFIES" : "WRONG CAR",
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
                    "SIGNED UP. DRIVE out in a car it takes before the " + LifeRules.SlotNames[S.slotIndex] +
                    " block ends - the line at the end of the street starts it. Miss it, or turn up in the wrong car, " +
                    "and it is a no-show: no prize, the entry kept, rep lost.",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(vx + vw * 0.5f, y - 2f), out float h,
                    TextAnchor.UpperCenter, wrong == null ? MenuKit.Accent : MenuKit.Bad, vw);
                y -= h + 6f;
                return;
            }

            bool carGone = CarWhere.Away(S, car);
            bool lowFuel = car != null && car.fuel <= LifeRules.RequiredFuelPct(t, car);
            string label = car == null ? "NO CAR TO RACE"
                : carGone ? "YOUR CAR IS AT " + CarWhere.ShopName(CarWhere.PlaceOf(S, car))
                : wrong != null ? "WRONG CAR - " + wrong
                : venue < 0 ? "NOT IN THIS EDITION"
                : lowFuel ? "LOW FUEL - FILL UP FIRST"
                : "START  >>";
            bool can = car != null && !carGone && wrong == null && venue >= 0 && !lowFuel;
            var due = d;
            MenuKit.Button(body, label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(bw, 64f),
                can ? (UnityEngine.Events.UnityAction)(() => StartEventRace(due)) : null, 21,
                can ? RaceBg : MenuKit.BtnBgDisabled);
            y -= 70f;
        }

        /// <summary>An event race, launched from the line: the event's road,
        /// field, hour and traffic. Committed here (a race that never reports
        /// back is a DNF).</summary>
        void StartEventRace(Events.Due d)
        {
            if (!CarIsHere()) return;
            var now = Events.DueNow(S);
            if (!now.Valid || now.entry != d.entry || now.race != d.race) { Rebuild(); return; }
            int venue = Events.VenueOf(d.ev, d.race);
            if (venue < 0 || !VenueHere(venue)) return;
            string wrong = Events.CarRefusal(d.ev, S.ActiveCar);
            if (wrong != null) { Toast("WRONG CAR - " + wrong); return; }
            var field = Events.FieldFor(d.ev, d.entry.edition, d.race);

            driveUnpaid = false;
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar != null ? S.ActiveCar.specId : "";
            RaceHandoff.TrackIndex = venue;
            RaceHandoff.TimeOfDayIndex = Events.HourFor(d.ev, d.entry.edition, d.race);
            RaceHandoff.TrafficLevel = Events.TrafficFor(d.ev, TrackCatalog.At(venue));
            RaceHandoff.StartFuelPct = S.ActiveCar != null ? S.ActiveCar.fuel : 100f;
            RaceHandoff.PurseWin = d.ev.prizes[0];
            RaceHandoff.PurseSecond = d.ev.prizes[1];
            RaceHandoff.PurseThird = d.ev.prizes[2];
            if (Events.FillField(field)) EngineVoiceLibrary.PrefetchHandoff();
            else LifeRules.FillOpponentField(S);
            RaceHandoff.EventKey = d.entry.Key;
            RaceHandoff.EventRace = d.race;
            Events.Begin(S, d);
            FillCarRequest();
            LifeSimManager.Save();
            SceneGate.Load(TrackCatalog.SceneIndex(venue));
        }
    }
}
