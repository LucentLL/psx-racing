using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>One driver at a meet: who they are, what they brought, what
    /// they want to run, and what they will put up.</summary>
    public class MeetRacer
    {
        /// <summary>Which stall of the lot their car stands in. Seat 0 is
        /// kept for the blacklist rival whether or not there is one tonight,
        /// so a name coming off the board mid-evening does not shuffle every
        /// other car one stall along. NOT how the save remembers a driver —
        /// that is the car they brought, see <see cref="Key"/>.</summary>
        public int seat;
        public string alias;
        public CarSpec spec;
        /// <summary>AI skill for the race, the same scale the field and the
        /// blacklist use (0.80 a Sunday driver, 1.05 the boss).</summary>
        public float skill;
        /// <summary>The blacklist name this driver IS, or empty. The alias and
        /// this are the same string when set — it exists so a caller can ask
        /// "is this a name off the board" without matching on aliases that a
        /// regular could also be carrying.</summary>
        public string rivalAlias = "";
        /// <summary>DRAG / GRIP / TOUGE / STREET — the kind of race they are
        /// at the meet to find, which decides the venue they name.</summary>
        public string style;
        /// <summary>Index into <see cref="TrackCatalog.All"/>.</summary>
        public int trackIndex;
        public int purse;
        /// <summary>Where they stood on the board when the lot was dealt, for
        /// the "#4" in front of their name. A SNAPSHOT: the board moves every
        /// night, so nothing may record a result against this — that is what
        /// <see cref="rivalAlias"/> is for.</summary>
        public int rivalRank;

        public bool IsRival => !string.IsNullOrEmpty(rivalAlias);

        /// <summary>Who this is, for the save and for the hop to the start
        /// line: the catalog id of their car. Every car in the lot is a
        /// different model, so it names exactly one driver, and it goes on
        /// naming them if the lot is dealt again.</summary>
        public string Key => spec != null ? spec.id : "";

        /// <summary>How good they are, as the word the lot would use. Shown
        /// instead of the number for the reason condition is shown as a word:
        /// a driver can tell somebody is quick, not that they are 0.94.</summary>
        public string Reputation =>
            skill >= 0.99f ? "DANGEROUS" : skill >= 0.95f ? "SERIOUS" :
            skill >= 0.90f ? "QUICK" : "A REGULAR";
    }

    /// <summary>
    /// CAR MEETS: a parking lot full of race cars, on the calendar.
    ///
    /// The owner's brief (2026-09-18): "Calendar should include 'car meets'.
    /// This means a parking lot full of race cars. Walking up to a car gives
    /// the option to challenge the racer. You can refer to HTML version of
    /// game for code on this." That code is RG2's H1033/H1034 (src/world/
    /// parkedCars.ts, src/ui/hud/meetChallengeHint.ts, sim/trackRace.ts
    /// startMeetChallenge) and BL-3 (sim/blacklistProgress.ts): a lot whose
    /// stalls are ~60% filled with distinct catalog cars, a CHALLENGE prompt on
    /// the nearest one when you pull up beside it, a race against THAT car, no
    /// daily cap burned — and the open blacklist rival's signature car parked
    /// among them, flagged, so the ladder is climbed at the meet.
    ///
    /// What is different here, and why:
    ///
    ///   * THE MEET IS A DATE. RG2's lot is a map you can drive to any night.
    ///     This game's front door is a calendar, so a meet is something that is
    ///     ON — Friday and Saturday nights — and it is on the calendar by RULE
    ///     rather than by being stored: every week of a career has its two
    ///     without the save holding a single one of them, and a calendar that
    ///     reads a rule cannot disagree with it.
    ///   * IT IS IN TOWN. Not a scene of its own: the lot is at the east end
    ///     of the main street (PSXRacingBuilder.Town.BuildTownMeetLot), and on
    ///     a meet night TownWorld fills it. So getting there is the drive the
    ///     game already has, the hour is the hour it already is, and "walking
    ///     up to a car" is the getting-out-anywhere the town already does.
    ///   * EVERY DRIVER NAMES THEIR OWN RACE. RG2 runs every challenge as a
    ///     drag because its meet map has one strip. This game has strips,
    ///     circuits, mountain roads and city routes, so a driver is at the
    ///     meet looking for a particular kind of run and says which.
    ///   * A NIGHT IS ONE BLOCK AND THREE RUNS. A race anywhere else costs a
    ///     block of the day; at a meet the whole night is the block (paid by
    ///     the drive home) and up to <see cref="MaxRunsPerMeet"/> people can
    ///     be lined up against inside it. That cap is what stands in for RG2's
    ///     "unlimited": with a $500 purse and twenty drivers in the lot,
    ///     unlimited is $10,000 a night against a job that tips $96.
    ///
    /// The roster is SEEDED OFF THE DAY. The lot is rebuilt every time the
    /// town loads — and it loads again after every race — so a rolled roster
    /// would be a different crowd each time the player came back from the
    /// start line. Nothing in the seed may move during the night: not rep (a
    /// win moves it), not money, only the day and the car they arrived in.
    /// </summary>
    public static class CarMeets
    {
        /// <summary>The lot's name, for the calendar and the HUD.</summary>
        public const string PlaceName = "THE EASTSIDE LOT";
        /// <summary>The block a meet runs in. Nobody holds a car meet at
        /// eleven in the morning.</summary>
        public const int MeetSlot = LifeRules.NightSlot;
        /// <summary>How many drivers can be raced in one night before the lot
        /// clears out.</summary>
        public const int MaxRunsPerMeet = 3;
        /// <summary>How many drivers turn up: twenty, in a lot of thirty-six
        /// stalls. RG2 leaves ~38% of its stalls empty so the lot reads as
        /// lively rather than jammed; here it is a little more, and the empty
        /// ones are by the way in, because the player has to PARK in it. The
        /// first cut was twelve and photographed as a car park with some cars
        /// in it — the brief says FULL of race cars.</summary>
        public const int RosterSize = 20;

        // ---------------- the calendar ----------------

        /// <summary>Friday and Saturday. LifeRules.Dow is FRI-first, so those
        /// are 0 and 1 — and day 1 of a career is a Friday, which means the
        /// very first night of the game has a meet on it.</summary>
        public static bool MeetOn(int day) => day >= 1 && LifeRules.Dow(day) <= 1;

        public static bool MeetAt(int day, int slot) => slot == MeetSlot && MeetOn(day);

        /// <summary>Is there a meet on in the block the clock is standing in?</summary>
        public static bool OnNow(LifeState s) => s != null && MeetAt(s.day, s.slotIndex);

        /// <summary>The next meet at or after a day, for "NEXT MEET: ...".</summary>
        public static int NextMeetDay(int fromDay)
        {
            for (int d = Mathf.Max(1, fromDay); d < fromDay + 8; d++)
                if (MeetOn(d)) return d;
            return fromDay;
        }

        // ---------------- tonight's state ----------------

        /// <summary>How many drivers have been raced at TONIGHT's meet.</summary>
        public static int RunsTonight(LifeState s) =>
            s != null && s.meetDay == s.day && s.meetRaced != null ? s.meetRaced.Count : 0;

        /// <summary>Did the player race anybody at a meet in the block the
        /// clock is in? The apply-back reads it to write the night into the
        /// day's record as AT THE MEET rather than as a drive.</summary>
        public static bool RacedTonight(LifeState s) => OnNow(s) && RunsTonight(s) > 0;

        public static bool AlreadyRaced(LifeState s, MeetRacer r) =>
            s != null && r != null && s.meetDay == s.day && s.meetRaced != null &&
            s.meetRaced.Contains(r.Key);

        /// <summary>
        /// Why this driver cannot be raced right now, or null when they can.
        /// The reasons are the ones the panel prints where its RACE button
        /// would have been — nothing pressable refuses.
        /// </summary>
        public static string Refusal(LifeState s, MeetRacer r)
        {
            if (s == null || r == null) return "nobody to race";
            if (!OnNow(s)) return "the lot has emptied out";
            // Somebody's dinner is on the passenger seat. It does not go to a
            // street race, and it does not sit in a car park while you do.
            if (PizzaRun.Carrying) return "you have an order on the seat — deliver it first";
            if (AlreadyRaced(s, r)) return "you have already run them tonight";
            if (RunsTonight(s) >= MaxRunsPerMeet)
                return "three runs is a night — the lot is clearing out";
            var car = s.ActiveCar;
            if (!CarWhere.Available(s, car)) return "you have no car here to race";
            if (r.trackIndex < 0 || r.trackIndex >= TrackCatalog.Count) return "nowhere to run it";
            var track = TrackCatalog.At(r.trackIndex);
            if (car.fuel <= LifeRules.RequiredFuelPct(track, car))
                return "not enough fuel for " + track.name.ToLowerInvariant() + " — fill up first";
            return null;
        }

        /// <summary>Write a run into the night. Called when the race is
        /// LAUNCHED, not when it is won: a driver you lined up against and
        /// lost to has still been raced, and quitting out of a race must not
        /// hand the run back.</summary>
        public static void BeginRun(LifeState s, MeetRacer r)
        {
            if (s == null || r == null) return;
            if (s.meetRaced == null) s.meetRaced = new List<string>();
            if (s.meetDay != s.day) { s.meetDay = s.day; s.meetRaced.Clear(); }
            if (!s.meetRaced.Contains(r.Key)) s.meetRaced.Add(r.Key);
        }

        // ---------------- the hop to the start line ----------------
        // The town cannot launch a race itself: leaving it has to bank the
        // drive first, and the only place that happens is scene 0. So the
        // challenge is left here, the town routes through the front end on the
        // "meetrace" page id, and LifeHomeScreen picks it up — the same shape
        // as PizzaRun's "deliverrun". Statics, because the hop is a scene load.

        /// <summary>Who was called out (<see cref="MeetRacer.Key"/>), and the
        /// day they were called out on. Null when nothing is pending.</summary>
        static string pendingKey;
        static int pendingDay;

        public static void SetPending(LifeState s, MeetRacer r)
        {
            pendingKey = r != null ? r.Key : null;
            pendingDay = s != null ? s.day : 0;
        }

        /// <summary>The driver the player called out, once. Null when there is
        /// none, when the night has moved on, or when they are no longer in
        /// the lot.</summary>
        public static MeetRacer TakePending(LifeState s)
        {
            string key = pendingKey;
            int day = pendingDay;
            pendingKey = null;
            if (string.IsNullOrEmpty(key) || s == null || day != s.day) return null;
            foreach (var r in Roster(s, s.day)) if (r.Key == key) return r;
            return null;
        }

        /// <summary>
        /// The day the player last pulled into the lot, so the signpost can
        /// stand down once they are there.
        ///
        /// This was `Heading`, a bool armed by the home screen's CAR MEET
        /// TONIGHT button — the player's stated INTENT to go. That button is
        /// gone with the rest of the launchers, and nothing replaced it,
        /// because nothing needed to: the lot is full on a meet night whether
        /// or not anyone announced they were coming, and an arrow that asks the
        /// calendar cannot disagree with the lot that asks the same calendar.
        ///
        /// A DAY rather than a bool, so it resets itself. A bool would need
        /// somebody to re-arm it each evening, and the thing that used to do
        /// that was the button.
        /// </summary>
        public static int ArrivedDay;

        /// <summary>Has the player already reached the lot tonight?</summary>
        public static bool ArrivedTonight(LifeState s) =>
            s != null && ArrivedDay == s.day && s.day > 0;

        /// <summary>Pulled in. Stands the signpost down for the rest of the
        /// night — the lot is forty metres deep, and a cue that kept running
        /// would spend the evening pointing a parked player back at the gate
        /// they came in by.</summary>
        public static void MarkArrived(LifeState s)
        {
            if (s != null) ArrivedDay = s.day;
        }

        /// <summary>The line the town shows when the player comes back from
        /// the start line. Read once by TownWorld, then cleared.</summary>
        public static string ResultLine;

        // ---------------- who turns up ----------------

        static readonly string[] Aliases =
        {
            "KENJI", "MARCO", "TASHA", "BIG RON", "LUIS", "DEE", "SPIDER", "COOP",
            "NADIA", "T-BONE", "RICO", "SMOKEY", "JAX", "MOUSE", "VEGA", "ODELL",
            "PRIYA", "HANK", "ZEKE", "LOLA", "DUKE", "MINH", "CASS", "BOOMER",
            "SLIM", "ROZ", "TWITCH", "EARL JR", "SUNNY", "KAT", "OSCAR", "BIRDIE",
            "TREY", "MAMA LIZ", "WOLF", "GIZMO",
        };

        // Venue ids by the kind of race they are. Ids, not indices: the
        // catalog's order has moved three times and a save-free rule should
        // not care. One that is missing from a build is skipped.
        static readonly string[] DragVenues = { "DragQuarter", "DragEighth", "EmeraldIsle", "LangstonBridge" };
        static readonly string[] GripVenues = { "CityCircuit", "HarborPoint", "RidgePass", "AirfieldSprint" };
        static readonly string[] TougeVenues = { "BlueRidge", "MtMitchell", "BlowingRock", "LittleSwitzerland",
                                                  "SwissNC226A", "BlowingRockSprint" };
        static readonly string[] StreetVenues = { "TryonSprint", "UptownLoop", "IndependenceSprint" };

        static int PickVenue(string[] ids, System.Random rng)
        {
            // Start at a seeded place and walk, so a missing id costs a step
            // rather than the whole pick.
            int start = rng.Next(ids.Length);
            for (int k = 0; k < ids.Length; k++)
            {
                int idx = TrackCatalog.IndexOf(ids[(start + k) % ids.Length]);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        static string[] VenuesFor(string style) =>
            style == "DRAG" ? DragVenues : style == "TOUGE" ? TougeVenues :
            style == "STREET" ? StreetVenues : GripVenues;

        /// <summary>What kind of race a car is at the meet for. Weighted by
        /// what the car IS — a 450 hp muscle car is there for the strip and a
        /// light rear-driver for a mountain — and then rolled, because a lot
        /// where every Civic wants the same thing is a spreadsheet.</summary>
        static string StyleFor(CarSpec c, System.Random rng)
        {
            int drag = 2, grip = 3, touge = 2, street = 3;
            if (c.hp >= 350) drag += 4;
            if (c.origin == "usa") drag += 3;
            if (c.drv == "FR" || c.drv == "MR") touge += 3;
            if (c.drv == "4WD") { touge += 2; street += 1; }
            if (c.drv == "FF") { grip += 2; street += 2; }
            if (c.kg > 0 && c.kg < 1150) { touge += 2; grip += 1; }
            int roll = rng.Next(drag + grip + touge + street);
            if (roll < drag) return "DRAG";
            roll -= drag;
            if (roll < grip) return "GRIP";
            roll -= grip;
            return roll < touge ? "TOUGE" : "STREET";
        }

        static string StyleOfRival(BlacklistRival r) =>
            r == null ? "GRIP" : r.venue == "drag" ? "DRAG" : r.venue == "city" ? "STREET" : "GRIP";

        /// <summary>Power to weight, the one number both cars have that says
        /// who should win. Floored so a catalog row with no weight cannot
        /// divide by nothing.</summary>
        static float Pace(CarSpec c) => c == null ? 0.12f : c.hp / (float)Mathf.Max(600, c.kg);

        /// <summary>
        /// What a driver puts up against THIS player in THIS car: the street
        /// tier's purse, scaled by how their car compares with yours and by
        /// how good they are. Beating something quicker than you pays more;
        /// turning up in a supercar to take lunch money off a Civic pays
        /// less. Rounded to $25 because nobody bets $317.
        /// </summary>
        public static int PurseFor(LifeState s, MeetRacer r)
        {
            if (r == null) return 0;
            if (r.IsRival) return Blacklist.Purse(Mathf.Max(1, r.rivalRank));
            int tier = LifeRules.StreetTier(s != null ? s.streetRep : 0f).idx;
            var mine = s != null && s.ActiveCar != null ? CarCatalog.Get(s.ActiveCar.specId) : null;
            float ratio = mine != null ? Pace(r.spec) / Mathf.Max(0.01f, Pace(mine)) : 1f;
            float mult = Mathf.Clamp(ratio, 0.6f, 1.8f) * Mathf.Lerp(0.9f, 1.15f,
                         Mathf.InverseLerp(0.84f, 1.0f, r.skill));
            return Mathf.Max(25, Mathf.RoundToInt(LifeRules.WinPrize[tier] * mult / 25f) * 25);
        }

        /// <summary>
        /// Everybody at the meet on a given night, in stall order.
        ///
        /// Half the lot is drawn from around the player's own car, so there is
        /// always somebody worth lining up against; the other half is whatever
        /// turned up, because a meet where every car is within 20% of yours is
        /// not a meet, and because walking past a car you could not possibly
        /// beat is most of what a meet is for. Distinct models, the way RG2's
        /// lot is. The open blacklist rival, if there is one, takes seat 0.
        /// </summary>
        public static List<MeetRacer> Roster(LifeState s, int day)
        {
            var list = new List<MeetRacer>();
            if (!CarCatalog.Ready) return list;
            var rng = new System.Random(day * 7919 + 101);

            var all = new List<CarSpec>(CarCatalog.All);
            var mineOwned = s != null ? s.ActiveCar : null;
            int reference = mineOwned != null
                ? (mineOwned.catalogPrice > 0 ? mineOwned.catalogPrice : Mathf.Max(1, mineOwned.paidPrice))
                : 15000;
            var near = CarCatalog.InPriceBand(reference / 2, reference * 2);

            var names = new List<string>(Aliases);
            var taken = new HashSet<string>();

            // THE CROWD FIRST, on its own dice, and from seat 1. The rival is
            // added afterwards off a separate seed: a name that comes off the
            // board mid-evening (the player beat them, here, tonight) must not
            // re-deal the eleven cars standing next to where theirs was.
            for (int guard = 0; list.Count < RosterSize - 1 && guard < 400; guard++)
            {
                // Alternate the two pools so the mix holds whatever the size —
                // but only while the near one has somebody left in it. Both
                // ends of the catalog are thin (a $5,500 Civic has three
                // neighbours), and a pool that is all taken would be drawn from
                // until the guard ran out, leaving half the lot empty.
                bool nearLeft = near.Exists(c => !taken.Contains(c.id));
                var pool = (list.Count % 2 == 0 && nearLeft) ? near : all;
                var spec = pool[rng.Next(pool.Count)];
                if (spec == null || !taken.Add(spec.id)) continue;

                string alias = names.Count > 0 ? names[rng.Next(names.Count)] : "SOMEBODY";
                names.Remove(alias);
                string style = StyleFor(spec, rng);
                int venue = PickVenue(VenuesFor(style), rng);
                // The roll is taken whether or not the venue exists, so a
                // build missing one circuit has the same crowd as one with it.
                float skill = 0.84f + (float)rng.NextDouble() * 0.16f;
                if (venue < 0) continue;

                list.Add(new MeetRacer
                {
                    seat = list.Count + 1, alias = alias, spec = spec, skill = skill,
                    style = style, trackIndex = venue,
                });
            }

            // Seat 0: the name the player can race for a rank tonight — the one
            // they are mid-series with, or the one directly above them. Theirs
            // is THE example of that model in the lot (RG2's rule), so a
            // regular who brought the same car stays home tonight: their stall
            // stands empty rather than re-dealt.
            //
            // A SERIES ALREADY RUNNING NAMES ITS OWN ROAD, and it is the road
            // the series was opened on — three legs at one venue is what makes
            // it a series rather than a tour, and the lot must not quietly
            // re-point the third race somewhere else.
            var rival = s != null ? Blacklist.OpenRival(s) : null;
            var rivalCar = rival != null ? Blacklist.ResolveCar(rival) : null;
            if (rivalCar != null)
            {
                list.RemoveAll(r => r.spec.id == rivalCar.id);
                string style = StyleOfRival(rival);
                bool mid = s.blChallenge != null && s.blChallenge.Live &&
                           s.blChallenge.alias == rival.alias;
                list.Insert(0, new MeetRacer
                {
                    seat = 0, alias = rival.alias, spec = rivalCar, skill = rival.skill,
                    style = style,
                    trackIndex = mid ? Blacklist.SeriesTrack(s)
                                     : PickVenue(VenuesFor(style), new System.Random(day * 7919 + 977)),
                    rivalRank = Blacklist.RankOf(s, rival.alias),
                    rivalAlias = rival.alias,
                });
                if (list[0].trackIndex < 0) list.RemoveAt(0);
            }

            foreach (var r in list) r.purse = PurseFor(s, r);
            return list;
        }
    }
}
