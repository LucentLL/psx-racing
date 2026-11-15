using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>One race of an event: where, and when inside its running.</summary>
    public class EventRace
    {
        /// <summary>Venue ids, first choice first. The next one is raced when
        /// a build does not carry the one before it (the MAIN edition ships no
        /// Charlotte), so an event never points at a road that is not there.</summary>
        public string[] tracks;
        /// <summary>Weekend of the running it falls on (0 = the first).</summary>
        public int week;
        /// <summary>0 FRI, 1 SAT, 2 SUN (LifeRules.Dow).</summary>
        public int dow;
        /// <summary>The block (LifeRules.SlotNames).</summary>
        public int slot;

        public EventRace(int week, int dow, int slot, params string[] tracks)
        {
            this.week = week; this.dow = dow; this.slot = slot; this.tracks = tracks;
        }
    }

    /// <summary>One event: its rule, its money, its races and when it runs.</summary>
    public class EventDef
    {
        public string id, name;
        /// <summary>A word for the calendar row (no more than ten capitals).</summary>
        public string shortName;
        /// <summary>The emblem's letters, drawn on the event's colour.</summary>
        public string mono;
        public Color color;
        public string blurb;
        /// <summary>STREET (underground: public roads, traffic, Fri-Sun NIGHT)
        /// or SANCTIONED (closed roads, no traffic, Sat/Sun MORNING or NOON).</summary>
        public bool street;

        // ---- the entry rule (shown as badges) ----
        /// <summary>"FF" / "FR" / "MR" / "4WD", or null for any.</summary>
        public string drv;
        /// <summary>Most horsepower as built (Upgrades.EffectiveHp); 0 = no cap.</summary>
        public int maxHp;
        /// <summary>Newest model year allowed (0 = any) / oldest (0 = any).</summary>
        public int maxYear, minYear;
        /// <summary>"usa" / "jpn" / "euro", or null for any.</summary>
        public string origin;
        /// <summary>Racing rep (sanctioned) or street rep (street) needed to sign up.</summary>
        public int repNeed;
        /// <summary>STREET only: false = never listed, only passed on by word
        /// of mouth (a car meet, or <see cref="club"/>).</summary>
        public bool listed = true;
        /// <summary>The club that passes this word-of-mouth event on first.</summary>
        public string club;

        // ---- money ----
        public int fee, bonus;
        /// <summary>Prize for places 1-6, every race.</summary>
        public int[] prizes;

        // ---- the races and the schedule ----
        public EventRace[] races;
        /// <summary>Runs every <see cref="period"/> weekends, the first running on
        /// weekend <see cref="phase"/> (weekend k's Friday is day 1 + 7k).</summary>
        public int period, phase;

        // ---- the field ----
        /// <summary>The window the field's cars are drawn from, hp.</summary>
        public int fieldLo, fieldHi;
        /// <summary>The field's middle skill (the blacklist's scale).</summary>
        public float skill;
        /// <summary>STREET: MEDIUM traffic on city roads instead of LIGHT.</summary>
        public bool busy;
    }

    /// <summary>One running of an event the player signed up for.</summary>
    [System.Serializable]
    public class EventEntry
    {
        public string id = "";
        /// <summary>Which running (0 = the event's first ever).</summary>
        public int edition;
        public int signedDay;
        /// <summary>What was paid to enter (kept whatever happens).</summary>
        public int fee;
        /// <summary>Per race: <see cref="Events.Pending"/>, the place it finished
        /// (1..8), or a miss code (NoShow, Dnf, WrongCar, Withdrawn).</summary>
        public List<int> place = new List<int>();
        /// <summary>Per race: the prize paid.</summary>
        public List<int> prize = new List<int>();
        /// <summary>The race committed at the start line and not yet reported
        /// (-1 = none). A race that never comes back is a DNF (SweepInFlight).</summary>
        public int inFlight = -1;
        public bool withdrawn;
        public bool bonusPaid;
        /// <summary>Every race settled (or withdrawn).</summary>
        public bool closed;

        public string Key => id + "#" + edition;
    }

    /// <summary>A club's event points (club rep) and the player's share.</summary>
    [System.Serializable]
    public class ClubScore
    {
        public string id = "";
        public int points;
        /// <summary>The player's own points for this club.</summary>
        public int mine;
        /// <summary>Places the player's points have already moved them up the
        /// club's board (one per <see cref="Events.PointsPerPlace"/>).</summary>
        public int moved;
    }

    /// <summary>
    /// EVENTS (owner, 2026-11-15: "legal events where there is no traffic and
    /// then less legal, more underground events where participants race on the
    /// street with traffic ... Doing well in events can increase your own and
    /// your car club's reputation"). Two kinds, both on a schedule fixed weeks
    /// ahead and shown before you SIGN UP:
    ///   SANCTIONED - closed roads, NO traffic, Saturday/Sunday MORNING or NOON;
    ///                results build RACING REP.
    ///   STREET     - public roads at NIGHT, Friday to Sunday, with traffic;
    ///                results build STREET REP. A few are LISTED once street
    ///                rep reaches <see cref="StreetListRep"/>; the rest are
    ///                passed on at car meets and by the player's clubs.
    /// Each event is 2-3 separate races, each with its own prize for places
    /// 1-6; winning every one pays the bonus. Signing up pays the fee and puts
    /// the races in the calendar; a race not run is a NO-SHOW (no prize, the
    /// fee kept, a little rep lost); withdrawing before the day gives nothing
    /// back. A result in a car a club takes scores that club's points, for
    /// every club of the player's the car fits, and the player's own points
    /// move them up that club's board. Nothing weekday: "The weekdays are your
    /// time to work, recover, and repair." PSX_EVENTS=0 hides it all.
    /// </summary>
    public static class Events
    {
        /// <summary>The switch. Off: no page, no calendar rows, no rollover
        /// work; a save keeps whatever it holds.</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_EVENTS") != "0";

        // ---------------- tunables (defaults the owner has not set) ----------------
        /// <summary>Street rep at which the LISTED street events appear.</summary>
        public const int StreetListRep = 10;
        /// <summary>Club points for places 1-6.</summary>
        public static readonly int[] ClubPoints = { 10, 6, 4, 3, 2, 1 };
        /// <summary>The player's own club points that pass one member on that club's board.</summary>
        public const int PointsPerPlace = 20;
        public static readonly int[] RacingRepGain = { 5, 3, 2, 1, 1, 1 };
        public static readonly int[] StreetRepGain = { 6, 4, 2, 1, 1, 1 };
        public const int BonusRep = 5;
        /// <summary>A no-show (a missed race, or the wrong car at the line).</summary>
        public const int NoShowRacingRep = 2, NoShowStreetRep = 3;
        /// <summary>Started and never finished (quit out, or the app died).</summary>
        public const int DnfRacingRep = 1, DnfStreetRep = 2;
        public const int KeepDays = 42, NewsKeep = 12;
        /// <summary>AI cars in a field (the grid holds eight).</summary>
        public const int Opponents = RaceField.MaxCars - 1;

        public const int Pending = 0, NoShow = -1, Dnf = -2, WrongCar = -3, Withdrawn = -4;

        // ---------------- the catalogue ----------------
        static readonly string[] Mtn = { "GillespieGap", "BlowingRock", "LittleSwitzerland" };

        static int[] Prizes(int first) => new[]
        {
            first, Round5(first * 0.60f), Round5(first * 0.40f), Round5(first * 0.25f),
            Round5(first * 0.15f), Round5(first * 0.10f)
        };

        static int Round5(float v) => Mathf.Max(5, Mathf.RoundToInt(v / 5f) * 5);

        const int Fri = 0, Sat = 1, Sun = 2;
        const int Morning = LifeRules.MorningSlot, Noon = LifeRules.NoonSlot, Night = LifeRules.NightSlot;

        /// <summary>
        /// THE EVENTS. Tracks are the ones the career races now (a new road is
        /// added to a list and an event can use it). City races fall back to a
        /// mountain road where a build ships no Charlotte. Sanctioned events keep
        /// off the VINTAGE club's SUN MORNING meet and SAT NOON race for the
        /// CLASSIC CUP, and the EURO club's for the CONTINENTAL CUP; other
        /// clashes with clubs and car meets are left in on purpose (the owner:
        /// events "can clash" with shifts and club meets).
        /// </summary>
        public static readonly EventDef[] All =
        {
            // ======================= SANCTIONED =======================
            new EventDef { id = "rookie", name = "ROOKIE CUP", shortName = "ROOKIE", mono = "RC",
                color = new Color(0.20f, 0.42f, 0.70f), street = false, maxHp = 180, repNeed = 0,
                blurb = "The open cup for new faces: closed roads, marshals on every bend, no traffic.",
                fee = 100, prizes = Prizes(500), bonus = 600, period = 2, phase = 0,
                fieldLo = 110, fieldHi = 180, skill = 0.86f,
                races = new[] { new EventRace(0, Sat, Morning, "GillespieGap", "BlowingRock"),
                                new EventRace(0, Sun, Morning, "BlowingRock", "LittleSwitzerland") } },
            new EventDef { id = "ffcup", name = "FRONT DRIVE CUP", shortName = "FF CUP", mono = "FF",
                color = new Color(0.16f, 0.55f, 0.50f), drv = "FF", maxHp = 200, repNeed = 0,
                blurb = "Front-wheel drive only. Late on the brakes, early on the power.",
                fee = 150, prizes = Prizes(700), bonus = 900, period = 3, phase = 1,
                fieldLo = 130, fieldHi = 200, skill = 0.87f,
                races = new[] { new EventRace(0, Sat, Noon, "LittleSwitzerland", "GillespieGap"),
                                new EventRace(0, Sun, Noon, "SwissNC226A", "BlowingRock") } },
            new EventDef { id = "classic", name = "CLASSIC CUP", shortName = "CLASSIC", mono = "CL",
                color = new Color(0.55f, 0.36f, 0.20f), maxYear = 1979, repNeed = 0,
                blurb = "Cars from 1979 or before, on the roads they were built for.",
                fee = 150, prizes = Prizes(800), bonus = 1000, period = 3, phase = 0,
                fieldLo = 90, fieldHi = 220, skill = 0.86f,
                races = new[] { new EventRace(0, Sat, Morning, "BlowingRock", "LittleSwitzerland"),
                                new EventRace(0, Sun, Noon, "LittleSwitzerland", "GillespieGap") } },
            new EventDef { id = "frcup", name = "REAR DRIVE CUP", shortName = "FR CUP", mono = "FR",
                color = new Color(0.62f, 0.20f, 0.18f), drv = "FR", maxHp = 280, repNeed = 10,
                blurb = "Engine in front, drive at the back, 280 hp at most. Three rounds over two weekends.",
                fee = 250, prizes = Prizes(1200), bonus = 1500, period = 3, phase = 2,
                fieldLo = 190, fieldHi = 280, skill = 0.90f,
                races = new[] { new EventRace(0, Sat, Morning, "BlueRidge", "GillespieGap"),
                                new EventRace(0, Sun, Morning, "MtMitchell", "BlowingRock"),
                                new EventRace(1, Sat, Morning, "GillespieGap", "LittleSwitzerland") } },
            new EventDef { id = "jpcup", name = "IMPORT CUP", shortName = "IMPORT", mono = "JP",
                color = new Color(0.70f, 0.18f, 0.30f), origin = "jpn", maxHp = 280, repNeed = 10,
                blurb = "Japanese-built, 280 hp at most. The mountain roads, three rounds.",
                fee = 250, prizes = Prizes(1100), bonus = 1400, period = 4, phase = 2,
                fieldLo = 200, fieldHi = 280, skill = 0.90f,
                races = new[] { new EventRace(0, Sat, Noon, "GillespieGap", "BlowingRock"),
                                new EventRace(0, Sun, Morning, "SwissNC226A", "LittleSwitzerland"),
                                new EventRace(1, Sat, Noon, "BeechGap", "GillespieGap") } },
            new EventDef { id = "eucup", name = "CONTINENTAL CUP", shortName = "EURO CUP", mono = "EU",
                color = new Color(0.22f, 0.30f, 0.62f), origin = "euro", repNeed = 10,
                blurb = "European-built cars on the tight roads. Saturday morning, Sunday noon.",
                fee = 250, prizes = Prizes(1100), bonus = 1400, period = 4, phase = 0,
                fieldLo = 150, fieldHi = 300, skill = 0.89f,
                races = new[] { new EventRace(0, Sat, Morning, "LittleSwitzerland", "GillespieGap"),
                                new EventRace(0, Sun, Noon, "BlowingRock", "LittleSwitzerland") } },
            new EventDef { id = "uscup", name = "AMERICAN STOCK CUP", shortName = "US CUP", mono = "US",
                color = new Color(0.30f, 0.30f, 0.55f), origin = "usa", repNeed = 10,
                blurb = "American-built. Uptown is closed off for it on Saturday morning.",
                fee = 300, prizes = Prizes(1300), bonus = 1600, period = 4, phase = 3,
                fieldLo = 240, fieldHi = 420, skill = 0.89f,
                races = new[] { new EventRace(0, Sat, Morning, "UptownLoop", "BlueRidge"),
                                new EventRace(0, Sun, Morning, "IndependenceSprint", "MtMitchell") } },
            new EventDef { id = "mrcup", name = "MIDSHIP CUP", shortName = "MR CUP", mono = "MR",
                color = new Color(0.58f, 0.46f, 0.12f), drv = "MR", repNeed = 20,
                blurb = "Engine in the middle. Two rounds on the high roads.",
                fee = 300, prizes = Prizes(1500), bonus = 1800, period = 4, phase = 3,
                fieldLo = 180, fieldHi = 320, skill = 0.92f,
                races = new[] { new EventRace(0, Sat, Noon, "BeechGap", "GillespieGap"),
                                new EventRace(0, Sun, Noon, "ChimneyRock", "BlowingRock") } },
            new EventDef { id = "awdcup", name = "ALL-WHEEL CUP", shortName = "4WD CUP", mono = "4W",
                color = new Color(0.34f, 0.50f, 0.22f), drv = "4WD", maxHp = 300, repNeed = 20,
                blurb = "Four-wheel drive, 300 hp at most. Three rounds over two weekends.",
                fee = 350, prizes = Prizes(1600), bonus = 2000, period = 4, phase = 1,
                fieldLo = 240, fieldHi = 300, skill = 0.92f,
                races = new[] { new EventRace(0, Sat, Morning, "ChimneyRock", "GillespieGap"),
                                new EventRace(0, Sun, Noon, "MtMitchell", "BlowingRock"),
                                new EventRace(1, Sun, Morning, "BlueRidge", "LittleSwitzerland") } },
            new EventDef { id = "opencup", name = "CAROLINA OPEN", shortName = "OPEN", mono = "CO",
                color = new Color(0.50f, 0.50f, 0.54f), repNeed = 40,
                blurb = "Anything goes, the quickest field the promoter can find. Three rounds.",
                fee = 600, prizes = Prizes(3000), bonus = 4000, period = 4, phase = 2,
                fieldLo = 330, fieldHi = 520, skill = 0.95f,
                races = new[] { new EventRace(0, Sat, Morning, "BlueRidge", "GillespieGap"),
                                new EventRace(0, Sun, Noon, "GillespieGap", "BlowingRock"),
                                new EventRace(1, Sun, Morning, "UptownLoop", "MtMitchell") } },

            // ======================= STREET =======================
            new EventDef { id = "fridaynight", name = "FRIDAY NIGHT RUN", shortName = "FRI RUN", mono = "FN",
                color = new Color(0.45f, 0.20f, 0.52f), street = true, listed = true, maxHp = 250, repNeed = 10,
                blurb = "Downtown after the bars close. 250 hp at most, and the city is still driving.",
                fee = 200, prizes = Prizes(1000), bonus = 1200, period = 2, phase = 1,
                fieldLo = 160, fieldHi = 250, skill = 0.90f,
                races = new[] { new EventRace(0, Fri, Night, "TryonSprint", "GillespieGap"),
                                new EventRace(0, Sat, Night, "UptownLoop", "BlowingRock") } },
            new EventDef { id = "midnight277", name = "I-277 MIDNIGHT", shortName = "I-277", mono = "277",
                color = new Color(0.20f, 0.20f, 0.30f), street = true, listed = true, repNeed = 25, busy = true,
                blurb = "The loop round Uptown, three nights running, with the late traffic on it.",
                fee = 400, prizes = Prizes(2000), bonus = 2500, period = 3, phase = 2,
                fieldLo = 260, fieldHi = 400, skill = 0.93f,
                races = new[] { new EventRace(0, Fri, Night, "UptownLoop", "BlueRidge"),
                                new EventRace(0, Sat, Night, "IndependenceSprint", "MtMitchell"),
                                new EventRace(0, Sun, Night, "UptownLoop", "GillespieGap") } },
            new EventDef { id = "innercircle", name = "INNER CIRCLE NIGHT", shortName = "INNER", mono = "IC",
                color = new Color(0.55f, 0.10f, 0.12f), street = true, listed = true, repNeed = 50, busy = true,
                blurb = "Where the names on the list race. Big buy-in, bigger pot.",
                fee = 800, prizes = Prizes(4000), bonus = 5000, period = 4, phase = 3,
                fieldLo = 350, fieldHi = 560, skill = 0.97f,
                races = new[] { new EventRace(0, Sat, Night, "IndependenceSprint", "BlueRidge"),
                                new EventRace(0, Sun, Night, "UptownLoop", "MtMitchell") } },
            new EventDef { id = "tougenight", name = "TOUGE NIGHT", shortName = "TOUGE", mono = "TN",
                color = new Color(0.12f, 0.36f, 0.30f), street = true, listed = false, club = "japanese",
                maxHp = 300, repNeed = 0,
                blurb = "Up the mountain after dark, lights on the cliff side. Word of mouth only.",
                fee = 250, prizes = Prizes(1300), bonus = 1600, period = 3, phase = 1,
                fieldLo = 190, fieldHi = 300, skill = 0.91f,
                races = new[] { new EventRace(0, Sat, Night, "GillespieGap", "BlowingRock"),
                                new EventRace(0, Sun, Night, "BlowingRock", "LittleSwitzerland") } },
            new EventDef { id = "bigblock", name = "US 74 BIG BLOCK NIGHT", shortName = "BIG BLOCK", mono = "74",
                color = new Color(0.40f, 0.26f, 0.12f), street = true, listed = false, club = "american",
                origin = "usa", repNeed = 0, busy = true,
                blurb = "American iron on Independence and Tryon. Word of mouth only.",
                fee = 300, prizes = Prizes(1500), bonus = 1800, period = 3, phase = 0,
                fieldLo = 230, fieldHi = 420, skill = 0.91f,
                races = new[] { new EventRace(0, Fri, Night, "IndependenceSprint", "BlueRidge"),
                                new EventRace(0, Sun, Night, "TryonSprint", "MtMitchell") } },
            new EventDef { id = "y2krun", name = "Y2K RUN", shortName = "Y2K RUN", mono = "Y2K",
                color = new Color(0.10f, 0.40f, 0.55f), street = true, listed = false, club = "future",
                minYear = 1995, maxHp = 320, repNeed = 0,
                blurb = "The newest cars in town, 1995 or later, three nights through the city. Word of mouth only.",
                fee = 300, prizes = Prizes(1400), bonus = 1800, period = 4, phase = 2,
                fieldLo = 200, fieldHi = 320, skill = 0.91f,
                races = new[] { new EventRace(0, Fri, Night, "TryonSprint", "GillespieGap"),
                                new EventRace(0, Sat, Night, "UptownLoop", "BlowingRock"),
                                new EventRace(0, Sun, Night, "IndependenceSprint", "LittleSwitzerland") } },
        };

        public static EventDef ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in All) if (e.id == id) return e;
            return null;
        }

        public static List<EventDef> OfKind(bool street)
        {
            var list = new List<EventDef>();
            foreach (var e in All) if (e.street == street) list.Add(e);
            return list;
        }

        // ---------------- the schedule ----------------

        /// <summary>The career's weekend a day falls in: weekend k runs from
        /// its Friday, day 1 + 7k (day 1 is a Friday). -1 before day 1.</summary>
        public static int WeekendOf(int day) => day < 1 ? -1 : (day - 1) / 7;

        public static int RaceDay(EventDef ev, int edition, int r)
        {
            var race = ev.races[Mathf.Clamp(r, 0, ev.races.Length - 1)];
            int k = ev.phase + Mathf.Max(0, edition) * ev.period + race.week;
            return 1 + 7 * k + race.dow;
        }

        public static int RaceSlot(EventDef ev, int r) => ev.races[Mathf.Clamp(r, 0, ev.races.Length - 1)].slot;

        public static int RaceOrd(EventDef ev, int edition, int r) => Duty.Ord(RaceDay(ev, edition, r), RaceSlot(ev, r));

        /// <summary>The running still open to sign up for: the first whose
        /// opening race is in a block after this one.</summary>
        public static int OpenEdition(EventDef ev, int nowOrd)
        {
            int day = Mathf.Max(1, nowOrd / LifeRules.SlotCount);
            int n = Mathf.Max(0, (WeekendOf(day) - ev.phase) / Mathf.Max(1, ev.period) - 1);
            while (RaceOrd(ev, n, 0) <= nowOrd && n < 100000) n++;
            return n;
        }

        /// <summary>The running the event page shows: the player's own while
        /// it is live, else the next open one.</summary>
        public static int ShownEdition(LifeState s, EventDef ev)
        {
            var e = ActiveEntry(s, ev);
            return e != null ? e.edition : OpenEdition(ev, Duty.NowOrd(s));
        }

        /// <summary>The venue a race runs on in this build, or -1.</summary>
        public static int VenueOf(EventRace race)
        {
            if (race == null || race.tracks == null) return -1;
            foreach (var id in race.tracks)
                if (TrackCatalog.TryIndexOfShipped(id, out int i) && TrackCatalog.Offered(i)) return i;
            return -1;
        }

        public static int VenueOf(EventDef ev, int r) => VenueOf(ev.races[Mathf.Clamp(r, 0, ev.races.Length - 1)]);

        /// <summary>
        /// THE ONE PLACE an event race's traffic is decided. Sanctioned: closed
        /// roads, NONE. Street: the night's traffic is on the road - LIGHT, or
        /// MEDIUM on a busy city road; never heavier (the owner: midnight on a
        /// weekend is light).
        /// </summary>
        public static int TrafficFor(EventDef ev, TrackCatalog.TrackDef t)
        {
            if (ev == null || !ev.street) return TrafficLevels.None;
            return t != null && t.city && ev.busy ? TrafficLevels.Medium : TrafficLevels.Light;
        }

        /// <summary>The hour a race runs at: the block's own, the same on the
        /// event page as on the grid.</summary>
        public static int HourFor(EventDef ev, int edition, int r) =>
            TimeOfDay.ForSlot(RaceSlot(ev, r), RaceDay(ev, edition, r));

        /// <summary>"SAT 21 NOV · MORNING".</summary>
        public static string WhenLine(EventDef ev, int edition, int r)
        {
            int d = RaceDay(ev, edition, r);
            return LifeRules.WeekDayNames[LifeRules.WeekCol(d)] + " " + LifeRules.DayOfMonth(d) + " " +
                   LifeRules.MonthShort[LifeRules.MonthOf(d) - 1] + " · " + LifeRules.SlotNames[RaceSlot(ev, r)];
        }

        /// <summary>Every race of a running, for a notice.</summary>
        public static string ScheduleWords(EventDef ev, int edition)
        {
            var bits = new List<string>();
            for (int r = 0; r < ev.races.Length; r++) bits.Add(WhenLine(ev, edition, r));
            return string.Join(", ", bits.ToArray());
        }

        // ---------------- the rule ----------------

        static bool IsEuro(CarSpec c) =>
            c.origin == "eur" || c.origin == "ger" || c.origin == "gbr" || c.origin == "ita" ||
            c.origin == "fra" || c.origin == "swe";

        static bool OriginIs(CarSpec c, string origin) =>
            origin == "euro" ? IsEuro(c) : c.origin == origin;

        public static string OriginWord(string origin) =>
            origin == "usa" ? "AMERICAN" : origin == "jpn" ? "JAPANESE" : origin == "euro" ? "EUROPEAN" : "ANY";

        /// <summary>Horsepower as built (the SPECS screen's figure).</summary>
        public static int HpOf(OwnedCar car, CarSpec spec) =>
            spec == null ? 0 : CarSpec.ToHp(car != null ? Upgrades.EffectiveHp(car, spec) : spec.hp);

        /// <summary>Why this car may not enter, in a few capitals, or null.</summary>
        public static string RuleRefusal(EventDef ev, CarSpec spec, int hp)
        {
            if (ev == null) return "NO EVENT";
            if (spec == null) return "NO CAR";
            if (!string.IsNullOrEmpty(ev.drv) && spec.drv != ev.drv) return ev.drv + " ONLY";
            if (ev.maxHp > 0 && hp > ev.maxHp) return "OVER " + ev.maxHp + " HP";
            if (ev.maxYear > 0 && (spec.modelYear <= 0 || spec.modelYear > ev.maxYear)) return ev.maxYear + " OR OLDER";
            if (ev.minYear > 0 && spec.modelYear < ev.minYear) return ev.minYear + " OR NEWER";
            if (!string.IsNullOrEmpty(ev.origin) && !OriginIs(spec, ev.origin)) return OriginWord(ev.origin) + " ONLY";
            return null;
        }

        public static string CarRefusal(EventDef ev, OwnedCar car)
        {
            if (car == null) return "NO CAR";
            var spec = CarCatalog.Get(car.specId);
            return RuleRefusal(ev, spec, HpOf(car, spec));
        }

        public static bool OwnsQualifying(LifeState s, EventDef ev)
        {
            if (s == null || s.cars == null) return false;
            foreach (var c in s.cars) if (CarRefusal(ev, c) == null) return true;
            return false;
        }

        /// <summary>The rule as badges: "FF", "MAX 180 HP", "1979 OR OLDER",
        /// "JAPANESE", "RACING REP 10" - "OPEN" when nothing is asked.</summary>
        public static List<string> Badges(EventDef ev)
        {
            var b = new List<string>();
            if (!string.IsNullOrEmpty(ev.drv)) b.Add(ev.drv);
            if (ev.maxHp > 0) b.Add("MAX " + ev.maxHp + " HP");
            if (ev.maxYear > 0) b.Add(ev.maxYear + " OR OLDER");
            if (ev.minYear > 0) b.Add(ev.minYear + " OR NEWER");
            if (!string.IsNullOrEmpty(ev.origin)) b.Add(OriginWord(ev.origin));
            if (b.Count == 0) b.Add("OPEN");
            if (ev.repNeed > 0) b.Add((ev.street ? "STREET" : "RACING") + " REP " + ev.repNeed);
            return b;
        }

        /// <summary>The car rule in words, without the rep.</summary>
        public static string RuleWords(EventDef ev)
        {
            var b = Badges(ev);
            if (ev.repNeed > 0) b.RemoveAt(b.Count - 1);
            return string.Join(" · ", b.ToArray());
        }

        // ---------------- the field ----------------

        public struct Field
        {
            public List<CarSpec> cars;
            public List<float> skills;
            /// <summary>The field's average horsepower, to the nearest 5.</summary>
            public int roughHp;
        }

        static int Seed(string id, int edition, int r)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char ch in id ?? "") h = (h ^ ch) * 16777619;
                h = (h ^ edition) * 16777619;
                h = (h ^ (r + 7)) * 16777619;
                return h & 0x7fffffff;
            }
        }

        /// <summary>
        /// Who turns up for a race: catalogue road cars that pass the rule, in
        /// the event's power window (widened if the rule leaves too few), drawn
        /// by a seed off the event, the running and the race - so the page's
        /// "~295 HP" is the field that is on the grid.
        /// </summary>
        public static Field FieldFor(EventDef ev, int edition, int r)
        {
            var f = new Field { cars = new List<CarSpec>(), skills = new List<float>() };
            if (ev == null || !CarCatalog.Ready) return f;
            int max = Opponents;
            int v = VenueOf(ev, r);
            if (v >= 0) max = Mathf.Min(max, RaceField.MaxFor(TrackCatalog.At(v)) - 1);
            List<CarSpec> pool = null;
            for (int widen = 0; widen < 4; widen++)
            {
                float lo = ev.fieldLo * (1f - 0.15f * widen), hi = ev.fieldHi * (1f + 0.15f * widen);
                pool = new List<CarSpec>();
                foreach (var c in CarCatalog.All)
                {
                    if (c == null || c.IsRaceCar || c.IsRallyCar) continue;
                    int hp = CarSpec.ToHp(c.hp);
                    if (RuleRefusal(ev, c, hp) != null) continue;
                    if (widen < 3 && (hp < lo || hp > hi)) continue;
                    pool.Add(c);
                }
                if (pool.Count >= max) break;
            }
            pool.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            var rng = new System.Random(Seed(ev.id, edition, r));
            int n = Mathf.Min(max, pool.Count);
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                int k = rng.Next(pool.Count);
                f.cars.Add(pool[k]);
                sum += CarSpec.ToHp(pool[k].hp);
                pool.RemoveAt(k);
            }
            for (int i = 0; i < n; i++)
                f.skills.Add(Mathf.Clamp(ev.skill + 0.04f - i * (0.08f / Mathf.Max(1, n - 1)), 0.80f, 1.05f));
            f.roughHp = n > 0 ? Mathf.RoundToInt(sum / (float)n / 5f) * 5 : 0;
            return f;
        }

        /// <summary>The field into the handoff (the purse race's own format).</summary>
        public static bool FillField(Field f)
        {
            if (f.cars == null || f.cars.Count == 0) return false;
            var ids = new System.Text.StringBuilder();
            var skills = new System.Text.StringBuilder();
            for (int i = 0; i < f.cars.Count; i++)
            {
                if (i > 0) { ids.Append(';'); skills.Append(';'); }
                ids.Append(f.cars[i].id);
                skills.Append(f.skills[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            RaceHandoff.OpponentSpecIds = ids.ToString();
            RaceHandoff.OpponentSkills = skills.ToString();
            return true;
        }

        // ---------------- the player's entries ----------------

        public static EventEntry ActiveEntry(LifeState s, EventDef ev)
        {
            if (s == null || ev == null || s.evEntries == null) return null;
            return s.evEntries.Find(e => e != null && e.id == ev.id && !e.closed && !e.withdrawn);
        }

        public static EventEntry EntryFor(LifeState s, EventDef ev, int edition)
        {
            if (s == null || ev == null || s.evEntries == null) return null;
            return s.evEntries.Find(e => e != null && e.id == ev.id && e.edition == edition);
        }

        public static EventEntry ByKey(LifeState s, string key)
        {
            if (s == null || s.evEntries == null || string.IsNullOrEmpty(key)) return null;
            return s.evEntries.Find(e => e != null && e.Key == key);
        }

        static void Ensure(EventEntry e, EventDef ev)
        {
            if (e.place == null) e.place = new List<int>();
            if (e.prize == null) e.prize = new List<int>();
            while (e.place.Count < ev.races.Length) e.place.Add(Pending);
            while (e.prize.Count < ev.races.Length) e.prize.Add(0);
        }

        public static int PlaceOf(EventEntry e, int r) =>
            e != null && e.place != null && r >= 0 && r < e.place.Count ? e.place[r] : Pending;

        /// <summary>The first race not yet settled, or -1.</summary>
        public static int NextPending(EventEntry e, EventDef ev)
        {
            if (e == null || ev == null) return -1;
            for (int r = 0; r < ev.races.Length; r++) if (PlaceOf(e, r) == Pending) return r;
            return -1;
        }

        public static bool WonAll(EventEntry e, EventDef ev)
        {
            if (e == null || ev == null) return false;
            for (int r = 0; r < ev.races.Length; r++) if (PlaceOf(e, r) != 1) return false;
            return true;
        }

        static void CloseIfDone(EventEntry e, EventDef ev)
        {
            if (NextPending(e, ev) < 0 && e.inFlight < 0) e.closed = true;
        }

        public static int SignedUpCount(LifeState s)
        {
            int n = 0;
            if (s != null && s.evEntries != null)
                foreach (var e in s.evEntries) if (e != null && !e.closed && !e.withdrawn) n++;
            return n;
        }

        /// <summary>Is this event on the player's lists at all? Sanctioned:
        /// always. Street: listed ones from <see cref="StreetListRep"/>, the
        /// rest once somebody has passed them on.</summary>
        public static bool Visible(LifeState s, EventDef ev)
        {
            if (!On || s == null || ev == null) return false;
            if (!ev.street || ActiveEntry(s, ev) != null) return true;
            if (ev.listed) return s.streetRep >= StreetListRep;
            return Heard(s, ev);
        }

        public static bool Heard(LifeState s, EventDef ev) =>
            s != null && ev != null && s.evHeard != null && s.evHeard.Contains(ev.id);

        public static float RepFor(LifeState s, EventDef ev) => ev.street ? s.streetRep : s.racingRep;

        /// <summary>Another signed-up race in the same block as one of this
        /// running's, in words, or null.</summary>
        public static string ClashWith(LifeState s, EventDef ev, int edition)
        {
            if (s == null || s.evEntries == null) return null;
            foreach (var other in s.evEntries)
            {
                if (other == null || other.closed || other.withdrawn || other.id == ev.id) continue;
                var od = ById(other.id);
                if (od == null) continue;
                for (int i = 0; i < od.races.Length; i++)
                {
                    if (PlaceOf(other, i) != Pending) continue;
                    for (int r = 0; r < ev.races.Length; r++)
                        if (RaceOrd(od, other.edition, i) == RaceOrd(ev, edition, r))
                            return "clashes with your " + od.shortName.ToLowerInvariant() + " race, " +
                                   WhenLine(ev, edition, r).ToLowerInvariant();
                }
            }
            return null;
        }

        /// <summary>Why SIGN UP is shut, or null.</summary>
        public static string SignUpRefusal(LifeState s, EventDef ev)
        {
            if (!On) return "events are off";
            if (s == null || ev == null) return "no event";
            if (ActiveEntry(s, ev) != null) return "you are signed up";
            if (!Visible(s, ev)) return "nobody has told you where it runs";
            float rep = RepFor(s, ev);
            if (rep < ev.repNeed)
                return "needs " + (ev.street ? "street" : "racing") + " rep " + ev.repNeed +
                       " - you have " + Mathf.FloorToInt(rep);
            if (!OwnsQualifying(s, ev)) return "you own no car it takes (" + RuleWords(ev).ToLowerInvariant() + ")";
            int n = OpenEdition(ev, Duty.NowOrd(s));
            if (EntryFor(s, ev, n) != null) return "you withdrew from this running - the next one opens after it";
            if (s.money < ev.fee) return "the entry is " + MenuKit.Money(ev.fee);
            return ClashWith(s, ev, n);
        }

        /// <summary>Pay the fee and write the running's races into the
        /// calendar. Returns the headline, or the refusal (ok false).</summary>
        public static bool SignUp(LifeState s, EventDef ev, out string line)
        {
            line = SignUpRefusal(s, ev);
            if (line != null) return false;
            int n = OpenEdition(ev, Duty.NowOrd(s));
            var e = new EventEntry { id = ev.id, edition = n, signedDay = s.day, fee = ev.fee };
            Ensure(e, ev);
            if (s.evEntries == null) s.evEntries = new List<EventEntry>();
            s.evEntries.Add(e);
            s.money -= ev.fee;
            Notice(s, "SIGNED UP: " + ev.name,
                   ScheduleWords(ev, n) + ". Entry " + MenuKit.Money(ev.fee) + " paid. Drive out in a car it takes (" +
                   RuleWords(ev).ToLowerInvariant() + ") on each race's day.");
            line = "SIGNED UP - " + ev.name + " IS IN THE CALENDAR";
            return true;
        }

        /// <summary>Why WITHDRAW is shut, or null. Before the day only.</summary>
        public static string WithdrawRefusal(LifeState s, EventDef ev)
        {
            var e = ActiveEntry(s, ev);
            if (e == null) return "not signed up";
            int r = NextPending(e, ev);
            if (r < 0) return "nothing left to run";
            if (RaceDay(ev, e.edition, r) <= s.day) return "it is race day - miss it and it is a no-show";
            return null;
        }

        /// <summary>Out of what is left of the running. Nothing back, no rep lost.</summary>
        public static bool Withdraw(LifeState s, EventDef ev, out string line)
        {
            line = WithdrawRefusal(s, ev);
            if (line != null) return false;
            var e = ActiveEntry(s, ev);
            for (int r = 0; r < ev.races.Length; r++) if (PlaceOf(e, r) == Pending) e.place[r] = Withdrawn;
            e.withdrawn = true;
            e.closed = true;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": withdrew from the " + ev.name.ToLowerInvariant() +
                              " - the " + MenuKit.Money(e.fee) + " entry is gone");
            line = "WITHDRAWN FROM THE " + ev.name + " - NOTHING BACK";
            return true;
        }

        // ---------------- race day ----------------

        /// <summary>A signed-up race and where it stands.</summary>
        public struct Due
        {
            public EventDef ev;
            public EventEntry entry;
            public int race;
            public bool Valid => ev != null && entry != null && race >= 0;
        }

        /// <summary>The signed-up race in this block, if any (whatever state).</summary>
        public static Due RaceAt(LifeState s, int day, int slot)
        {
            var d = new Due { race = -1 };
            if (!On || s == null || s.evEntries == null) return d;
            foreach (var e in s.evEntries)
            {
                if (e == null) continue;
                var ev = ById(e.id);
                if (ev == null) continue;
                for (int r = 0; r < ev.races.Length; r++)
                {
                    if (RaceDay(ev, e.edition, r) != day || RaceSlot(ev, r) != slot) continue;
                    // A withdrawn race is not in the calendar; another entry may be.
                    if (PlaceOf(e, r) == Withdrawn) continue;
                    return new Due { ev = ev, entry = e, race = r };
                }
            }
            return d;
        }

        /// <summary>The signed-up race due NOW: this block, not yet run.</summary>
        public static Due DueNow(LifeState s)
        {
            var d = s != null ? RaceAt(s, s.day, s.slotIndex) : new Due { race = -1 };
            if (!d.Valid || d.entry.closed || d.entry.withdrawn || PlaceOf(d.entry, d.race) != Pending ||
                d.entry.inFlight == d.race)
                return new Due { race = -1 };
            return d;
        }

        /// <summary>The line's RACE row under an event race (DepartScreen).</summary>
        public static string DepartLine(Due d)
        {
            if (!d.Valid) return "";
            int v = VenueOf(d.ev, d.race);
            return (v >= 0 ? TrackCatalog.At(v).name : "NO ROAD") + "  ·  " + d.ev.name.ToLowerInvariant() + ", race " +
                   (d.race + 1) + " of " + d.ev.races.Length + " - in a car it takes (" + RuleWords(d.ev).ToLowerInvariant() + ").";
        }

        /// <summary>Committed at the start line: a race that never reports
        /// back is a DNF (SweepInFlight).</summary>
        public static void Begin(LifeState s, Due d)
        {
            if (!d.Valid) return;
            Ensure(d.entry, d.ev);
            d.entry.inFlight = d.race;
        }

        /// <summary>At the line in a car the rule turns away: it cannot start,
        /// and it counts as a no-show (the clubs' rule for the wrong car).</summary>
        public static string MarkWrongCar(LifeState s, Due d, string why)
        {
            if (!d.Valid || PlaceOf(d.entry, d.race) != Pending) return null;
            Ensure(d.entry, d.ev);
            d.entry.place[d.race] = WrongCar;
            AddRep(s, d.ev, -(d.ev.street ? NoShowStreetRep : NoShowRacingRep));
            CloseIfDone(d.entry, d.ev);
            string line = d.ev.shortName + " R" + (d.race + 1) + ": WRONG CAR (" + why + ") - A NO-SHOW";
            Post(s, s.day, line);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + line.ToLowerInvariant());
            return line;
        }

        /// <summary>
        /// Bank an event race (called by the apply-back BEFORE the block is
        /// spent, so a NIGHT race is settled before the rollover sweeps
        /// no-shows): the prize for the place, the rep, the clubs' points, the
        /// bonus for winning every race. Returns the result line.
        /// </summary>
        public static string RecordResult(LifeState s, string key, int r, int pos, int field, OwnedCar car)
        {
            var e = ByKey(s, key);
            var ev = e != null ? ById(e.id) : null;
            if (e == null || ev == null || r < 0 || r >= ev.races.Length) return null;
            Ensure(e, ev);
            if (e.place[r] != Pending && e.inFlight != r) return null;   // settled already
            e.inFlight = -1;
            pos = Mathf.Max(1, pos);
            e.place[r] = pos;
            int prize = pos <= ev.prizes.Length ? ev.prizes[pos - 1] : 0;
            e.prize[r] = prize;
            s.money += prize;
            int gain = pos <= 6 ? (ev.street ? StreetRepGain : RacingRepGain)[pos - 1] : 0;
            AddRep(s, ev, gain);
            int day = RaceDay(ev, e.edition, r);
            string clubs = AwardClubPoints(s, car, pos, day);
            string bonus = null;
            if (!e.bonusPaid && WonAll(e, ev))
            {
                e.bonusPaid = true;
                s.money += ev.bonus;
                AddRep(s, ev, BonusRep);
                bonus = "EVERY RACE WON - BONUS " + MenuKit.Money(ev.bonus);
            }
            CloseIfDone(e, ev);
            string line = ev.shortName + " R" + (r + 1) + ": P" + pos + "/" + Mathf.Max(pos, field) +
                          (prize > 0 ? " - " + MenuKit.Money(prize) : " - no prize") +
                          (gain > 0 ? " · " + (ev.street ? "STREET" : "RACING") + " REP +" + gain : "") +
                          clubs + (bonus != null ? " · " + bonus : "");
            Post(s, day, line);
            return line;
        }

        /// <summary>A race that went to the line and never came back - quit
        /// out of, or the app died. Run when the house opens with no result
        /// in hand (after the apply-back, which clears every race that did
        /// report). A DNF: no prize, a little rep.</summary>
        public static string SweepInFlight(LifeState s)
        {
            if (!On || s == null || s.evEntries == null) return null;
            string line = null;
            foreach (var e in s.evEntries)
            {
                if (e == null || e.inFlight < 0) continue;
                var ev = ById(e.id);
                int r = e.inFlight;
                e.inFlight = -1;
                if (ev == null || r >= ev.races.Length) continue;
                Ensure(e, ev);
                if (e.place[r] != Pending) continue;
                e.place[r] = Dnf;
                AddRep(s, ev, -(ev.street ? DnfStreetRep : DnfRacingRep));
                CloseIfDone(e, ev);
                line = ev.shortName + " R" + (r + 1) + ": DID NOT FINISH";
                Post(s, s.day, line);
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": walked out of the " + ev.name.ToLowerInvariant() +
                                  " race - a DNF");
            }
            return line;
        }

        static void AddRep(LifeState s, EventDef ev, int delta)
        {
            if (ev.street) s.streetRep = Mathf.Clamp(s.streetRep + delta, 0f, 100f);
            else s.racingRep = Mathf.Clamp(s.racingRep + delta, 0f, 100f);
        }

        // ---------------- club points (club rep) ----------------

        public static ClubScore ScoreOf(LifeState s, string clubId)
        {
            if (s.evClubScores == null) s.evClubScores = new List<ClubScore>();
            var sc = s.evClubScores.Find(x => x != null && x.id == clubId);
            if (sc == null) { sc = new ClubScore { id = clubId }; s.evClubScores.Add(sc); }
            return sc;
        }

        public static int PointsOf(LifeState s, string clubId)
        {
            if (s == null || s.evClubScores == null) return 0;
            var sc = s.evClubScores.Find(x => x != null && x.id == clubId);
            return sc != null ? sc.points : 0;
        }

        public static int MyPointsOf(LifeState s, string clubId)
        {
            if (s == null || s.evClubScores == null) return 0;
            var sc = s.evClubScores.Find(x => x != null && x.id == clubId);
            return sc != null ? sc.mine : 0;
        }

        /// <summary>
        /// The player's place, for every club of theirs whose rule the car meets
        /// ("in more than one club? every club whose rule the car meets gets the
        /// points"). Their own points move them up that club's board, one place
        /// per <see cref="PointsPerPlace"/> - the board's one-place rule.
        /// </summary>
        public static string AwardClubPoints(LifeState s, OwnedCar car, int pos, int day)
        {
            if (s == null || car == null || pos < 1 || pos > ClubPoints.Length) return "";
            var spec = CarCatalog.Get(car.specId);
            if (spec == null) return "";
            int pts = ClubPoints[pos - 1];
            var words = new List<string>();
            foreach (var c in Clubs.Mine(s))
            {
                if (!c.Fits(spec)) continue;
                var sc = ScoreOf(s, c.id);
                sc.points += pts;
                sc.mine += pts;
                words.Add(c.shortName + " +" + pts);
                while (sc.mine / PointsPerPlace > sc.moved)
                {
                    sc.moved++;
                    PassOnBoard(s, c, day);
                }
            }
            return words.Count == 0 ? "" : " · CLUB POINTS " + string.Join(", ", words.ToArray());
        }

        static void PassOnBoard(LifeState s, ClubDef c, int day)
        {
            var b = Clubs.Board(s, c);
            int me = Clubs.PlayerRank(s, c);
            if (me <= 1 || me > b.Count) return;
            var above = b[me - 2];
            b[me - 2] = b[me - 1];
            b[me - 1] = above;
            Clubs.Post(s, day, "YOUR EVENT POINTS TAKE #" + (me - 1) + " IN THE " + c.shortName + " CLUB OFF " + above.alias);
        }

        /// <summary>The clubs by event points, best first.</summary>
        public static List<KeyValuePair<ClubDef, int>> Standings(LifeState s)
        {
            var list = new List<KeyValuePair<ClubDef, int>>();
            foreach (var c in Clubs.All) list.Add(new KeyValuePair<ClubDef, int>(c, PointsOf(s, c.id)));
            list.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value)
                                                   : Clubs.IndexOf(a.Key).CompareTo(Clubs.IndexOf(b.Key)));
            return list;
        }

        public static int StandingRank(LifeState s, ClubDef c)
        {
            var st = Standings(s);
            for (int i = 0; i < st.Count; i++) if (st[i].Key == c) return i + 1;
            return 0;
        }

        // ---------------- the rollover ----------------

        /// <summary>
        /// The rollover's part, for the day that just closed: every signed-up
        /// race of that day (or before) not run is a NO-SHOW; every event race
        /// that day is scored for the clubs (the field's members, and the
        /// player's own place where they raced); old entries are pruned.
        /// </summary>
        public static void Tick(LifeState s)
        {
            if (!On || s == null) return;
            if (s.evEntries == null) s.evEntries = new List<EventEntry>();
            if (s.evHeard == null) s.evHeard = new List<string>();
            foreach (var e in s.evEntries)
            {
                if (e == null || e.closed || e.withdrawn) continue;
                var ev = ById(e.id);
                if (ev == null) { e.closed = true; continue; }
                Ensure(e, ev);
                for (int r = 0; r < ev.races.Length; r++)
                {
                    if (e.place[r] != Pending || e.inFlight == r || RaceDay(ev, e.edition, r) >= s.day) continue;
                    e.place[r] = NoShow;
                    AddRep(s, ev, -(ev.street ? NoShowStreetRep : NoShowRacingRep));
                    Notice(s, "NO-SHOW: " + ev.name + " RACE " + (r + 1),
                           "You were signed up for " + WhenLine(ev, e.edition, r).ToLowerInvariant() +
                           " and did not turn up. No prize, the entry is kept, and people noticed.");
                }
                CloseIfDone(e, ev);
            }
            ScoreDay(s, s.day - 1);
            s.evEntries.RemoveAll(e => e == null || (e.closed && LastDay(e) < s.day - KeepDays));
        }

        static int LastDay(EventEntry e)
        {
            var ev = ById(e.id);
            return ev == null ? 0 : RaceDay(ev, e.edition, ev.races.Length - 1);
        }

        /// <summary>Every event race run on <paramref name="day"/>, scored for
        /// the clubs. The NPC order is seeded; the player's place is theirs.</summary>
        static void ScoreDay(LifeState s, int day)
        {
            int k = WeekendOf(day), dow = LifeRules.Dow(day);
            if (k < 0 || dow > Sun) return;
            foreach (var ev in All)
                for (int r = 0; r < ev.races.Length; r++)
                {
                    var race = ev.races[r];
                    if (race.dow != dow) continue;
                    int k0 = k - race.week - ev.phase;
                    if (k0 < 0 || k0 % ev.period != 0) continue;
                    ScoreRace(s, ev, k0 / ev.period, r, day);
                }
        }

        static void ScoreRace(LifeState s, EventDef ev, int edition, int r, int day)
        {
            var f = FieldFor(ev, edition, r);
            int m = f.cars.Count;
            if (m == 0) return;
            var e = EntryFor(s, ev, edition);
            int player = e != null ? Mathf.Max(0, PlaceOf(e, r)) : 0;
            var rng = new System.Random(Seed(ev.id, edition, r) ^ 0x2545F491);
            var order = new List<int>();
            var pace = new float[m];
            for (int i = 0; i < m; i++) { order.Add(i); pace[i] = f.skills[i] + ((float)rng.NextDouble() - 0.5f) * 0.12f; }
            order.Sort((a, b) => pace[b].CompareTo(pace[a]));
            string winner = null;
            for (int j = 0; j < order.Count; j++)
            {
                int place = j + 1;
                if (player > 0 && player <= place) place++;
                var club = ClubOfDriver(f.cars[order[j]], rng);
                if (place == 1 && club != null)
                    winner = Clubs.Roster(club)[rng.Next(Clubs.RosterSize)] + " (" + club.shortName + ")";
                if (club == null || place > ClubPoints.Length) continue;
                ScoreOf(s, club.id).points += ClubPoints[place - 1];
            }
            if (player == 1) winner = "YOU";
            if (winner != null)
                Post(s, day, ev.shortName + " R" + (r + 1) + ": " + winner + (player == 1 ? " WIN" : " WINS"));
        }

        /// <summary>A field driver's club: one of the ordinary clubs whose rule
        /// the car meets, seeded; null when none takes it.</summary>
        static ClubDef ClubOfDriver(CarSpec spec, System.Random rng)
        {
            var fits = new List<ClubDef>();
            foreach (var c in Clubs.All) if (!c.elite && c.Fits(spec)) fits.Add(c);
            return fits.Count == 0 ? null : fits[rng.Next(fits.Count)];
        }

        // ---------------- word of mouth ----------------

        /// <summary>
        /// Pulled into the Eastside Lot (CarMeets.MarkArrived, once a night): a
        /// car meet passes on one street event the player has not heard of; a
        /// club of theirs meeting there passes on its own first.
        /// </summary>
        public static void ArrivedAtLot(LifeState s)
        {
            if (!On || s == null) return;
            foreach (var c in Clubs.MeetingAt(s.day, s.slotIndex))
                if (Clubs.IsMember(s, c)) Hear(s, NextUnheard(s, c), "the " + c.name.ToLowerInvariant() + " meet");
            if (CarMeets.OnNow(s)) Hear(s, NextUnheard(s, null), "the car meet");
        }

        public static EventDef NextUnheard(LifeState s, ClubDef c)
        {
            if (c != null)
                foreach (var ev in All)
                    if (ev.street && !ev.listed && ev.club == c.id && !Heard(s, ev)) return ev;
            foreach (var ev in All)
                if (ev.street && !ev.listed && !Heard(s, ev)) return ev;
            return null;
        }

        /// <summary>The player is told about a word-of-mouth event: it joins
        /// the STREET list, with a notice. False when already known.</summary>
        public static bool Hear(LifeState s, EventDef ev, string from)
        {
            if (s == null || ev == null || Heard(s, ev)) return false;
            if (s.evHeard == null) s.evHeard = new List<string>();
            s.evHeard.Add(ev.id);
            int n = OpenEdition(ev, Duty.NowOrd(s));
            Notice(s, "WORD ON THE STREET: " + ev.name,
                   "Passed on at " + from + ". Next running " + ScheduleWords(ev, n).ToLowerInvariant() +
                   ". On the EVENTS page, STREET.");
            return true;
        }

        // ---------------- notices and news ----------------

        /// <summary>Mail, the diary line and the pager at once (Duty.Notice's
        /// shape); the mail expires after two weeks.</summary>
        static void Notice(LifeState s, string subject, string body)
        {
            if (s.mail == null) s.mail = new List<MailItem>();
            s.mail.Add(new MailItem { day = s.day, subject = subject, body = body, read = false, expiresDay = s.day + 14 });
            s.calendarLog.Add(LifeRules.LogDate(Mathf.Max(1, s.day)) + ": " + subject.ToLowerInvariant());
            Duty.lastNotice = string.IsNullOrEmpty(Duty.lastNotice) ? subject : Duty.lastNotice + "  ·  " + subject;
        }

        /// <summary>A line on the EVENTS page's results strip, dated.</summary>
        public static void Post(LifeState s, int day, string line)
        {
            if (s.evNews == null) s.evNews = new List<string>();
            s.evNews.Add(LifeRules.LogDate(Mathf.Max(1, day)) + ": " + line);
            while (s.evNews.Count > NewsKeep) s.evNews.RemoveAt(0);
        }
    }
}
