using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>One name on the ladder. Static IDENTITY — who they are, what
    /// they drive, how good they are. Where they stand is not in here: that is
    /// the board's job (<see cref="LifeState.blBoard"/>), because it moves.
    /// </summary>
    public class BlacklistRival
    {
        /// <summary>Where this name starts a fresh career, 10 (entry) … 1
        /// (top). NOT where they are now — see <see cref="Blacklist.RankOf"/>.
        /// </summary>
        public int startRank;
        public string alias;
        /// <summary>Case-insensitive patterns matched against catalog car NAMES,
        /// '|'-separated and tried IN ORDER — 'CTR2|911' means "the CTR2 if the
        /// catalog has one, else any 911", not "whichever comes first in catalog
        /// order". Split naively, so no parenthesised groups.</summary>
        public string carMatch;
        /// <summary>Shown when no catalog car matches.</summary>
        public string carLabel;
        /// <summary>The kind of race they call: drag / oval / city. It decides
        /// the road a challenge against them runs on — their call-out, their
        /// turf. It was flavour for a year because the game had one circuit;
        /// it has twenty-odd now, so it means something.</summary>
        public string venue;
        /// <summary>AI skill, and the whole of what makes one name harder than
        /// another. It travels WITH THE NAME rather than with the rung: a board
        /// that moves would otherwise hand the boss's skill to whoever happened
        /// to be standing at #1 this morning. The signature cars are chosen for
        /// identity and come out non-monotonic (PENNY's Miata is slower than
        /// JUICE's Civic), so difficulty has to live here.</summary>
        public float skill;
        /// <summary>Pre-race trash talk. Slot: {playerCar}.</summary>
        public string[] taunts;
    }

    /// <summary>Where a name stands relative to the player.</summary>
    public enum RivalStatus { Above, Below, You }

    /// <summary>
    /// The BLACKLIST: eleven names in an order, one of which is you.
    ///
    ///   you hold a rank → the name directly above is the one you can call out
    ///   → best of three takes their spot → the name directly below can do the
    ///   same to you → and every night the ten of them do it to each other.
    ///
    /// Ported from RG2's config/blacklist.ts and blacklistProgress.ts, then
    /// rebuilt on the owner's brief (2026-09-20): "Player's name should be
    /// added to Blacklist to show their current rank. The names should move
    /// around as NPCs challenge each other and they win or lose. Lower rank
    /// racers than player can challenge them for their position. Winning best
    /// of 3 to move up rank. Declining or missing a race for any reason counts
    /// as a loss."
    ///
    /// What that replaced: a strictly sequential ladder of ten fixed rungs,
    /// each padlocked behind a wins/rep gate, which the player walked past
    /// once and never looked at again. The gates are gone — the ladder IS the
    /// gate now, and you cannot skip a rung because you can only ever call out
    /// the name directly above you.
    ///
    /// Three rules hold the whole thing up:
    ///
    ///   * A CHALLENGE IS BETWEEN NEIGHBOURS. One rung, in either direction.
    ///     That is what makes a rank worth something and what keeps the board
    ///     honest: nobody, player or otherwise, gets past you without racing
    ///     you.
    ///   * A SERIES IS THREE RACES AND A DEADLINE. First to two. Anything
    ///     unraced when the deadline closes goes to the other driver, and so
    ///     does a leg the player launched and walked out of — see
    ///     <see cref="RankChallenge.legInFlight"/>.
    ///   * THE PAIR IS FROZEN WHILE THEY FIGHT. The nightly sim skips any
    ///     call-out involving either of them, so the two rows stay adjacent
    ///     for the whole series and resolving it is a straight swap.
    ///
    /// Out of scope here as in RG2: pink slips and boss-car uniqueness.
    /// </summary>
    public static class Blacklist
    {
        /// <summary>The player's row on the board. An '@' so it cannot collide
        /// with an alias — every rival is a bare upper-case word.</summary>
        public const string PlayerKey = "@YOU";

        // Ordered entry-first (start rank 10 up to 1), which is also display
        // order reversed — the board draws the top of the ladder at the top.
        public static readonly BlacklistRival[] Rivals =
        {
            new BlacklistRival { startRank = 10, alias = "JUICE", venue = "drag",
                carMatch = "Civic.*EK|Civic.*Type R|Civic.*SiR|Civic", carLabel = "Honda Civic",
                skill = 0.90f, taunts = new[] {
                    "You think you're gonna beat me with that {playerCar}?",
                    "Pull up to the strip. Bring lunch money." } },
            new BlacklistRival { startRank = 9, alias = "PENNY", venue = "oval",
                carMatch = "Eunos Roadster|MX-5|Miata", carLabel = "Mazda Roadster",
                skill = 0.92f, taunts = new[] {
                    "Corners matter, hotshot. Meet me at the oval.",
                    "That {playerCar} push wide in turn one? Thought so." } },
            new BlacklistRival { startRank = 8, alias = "DEACON", venue = "city",
                carMatch = "240SX|Sileighty|Silvia.*S13|Silvia.*S14|Silvia", carLabel = "Nissan Silvia",
                skill = 0.94f, taunts = new[] {
                    "These streets got a toll, and you ain't paid it.",
                    "Bring that {playerCar}. I need a good laugh." } },
            new BlacklistRival { startRank = 7, alias = "KAZE", venue = "drag",
                carMatch = "RX-7.*FC|Savanna|RX-7", carLabel = "Mazda RX-7 FC",
                skill = 0.96f, taunts = new[] {
                    "Rotary sings, piston begs. Listen close.",
                    "Your {playerCar} against my car? Short race." } },
            new BlacklistRival { startRank = 6, alias = "BIG SAL", venue = "drag",
                carMatch = "Cuda|Barracuda|Charger|Super Bee", carLabel = "Plymouth Cuda",
                skill = 0.97f, taunts = new[] {
                    "Eight cylinders of American arithmetic, kid.",
                    "That {playerCar} got a spare bumper? It'll need one." } },
            new BlacklistRival { startRank = 5, alias = "WRENCH", venue = "city",
                carMatch = "Impreza.*22B|Impreza.*STi|Impreza.*WRX|Impreza", carLabel = "Subaru Impreza",
                skill = 0.98f, taunts = new[] {
                    "I built mine. Who built yours?",
                    "Four driven wheels beat your {playerCar} in the wet AND the dry." } },
            new BlacklistRival { startRank = 4, alias = "DUCHESS", venue = "oval",
                carMatch = "S2000", carLabel = "Honda S2000",
                skill = 1.00f, taunts = new[] {
                    "Nine thousand RPM of goodbye.",
                    "Keep your {playerCar} off my racing line." } },
            new BlacklistRival { startRank = 3, alias = "PREACHER", venue = "city",
                carMatch = "Supra RZ|Supra.*Twin|Supra", carLabel = "Toyota Supra",
                skill = 1.01f, taunts = new[] {
                    "Everybody wants a sermon. Nobody wants the collection plate.",
                    "Boost is a faith, and your {playerCar} is an unbeliever." } },
            new BlacklistRival { startRank = 2, alias = "GHOST", venue = "city",
                carMatch = "GT-R.*R34|Skyline.*R34|GT-R", carLabel = "Nissan GT-R R34",
                skill = 1.03f, taunts = new[] {
                    "You won't see me. That's the point.",
                    "ATTESA does the math your right foot can't." } },
            new BlacklistRival { startRank = 1, alias = "CALLAHAN", venue = "city",
                carMatch = "CTR2|RUF.*CTR|RUF.*BTR|911", carLabel = "RUF CTR2",
                skill = 1.05f, taunts = new[] {
                    "Every name above yours earned it. Every name below yours quit.",
                    "This city has one king. You're looking at him." } },
        };

        /// <summary>Names on the board: the ten of them plus you.</summary>
        public static int BoardSize => Rivals.Length + 1;

        // ---------------- the rules, as numbers ----------------
        /// <summary>Races in a series. Odd, so it cannot be drawn.</summary>
        public const int SeriesRaces = 3;
        /// <summary>Legs needed to take it.</summary>
        public const int LegsToWin = SeriesRaces / 2 + 1;
        /// <summary>Days a series stands before what is left of it is forfeited.
        /// Three blocks a day and three legs to run, so one day would be a
        /// series you could only answer by dropping everything; a week would be
        /// a call-out you could forget about. Three is enough room to fit the
        /// races around a shift and short enough that ignoring it is a choice.
        /// </summary>
        public const int ChallengeDays = 3;
        /// <summary>Days before the player may call out a name they just lost
        /// to. Walking away from a call-out you made costs this as well as the
        /// legs — otherwise a forfeit is free and "decline" is a way to look at
        /// somebody's car without racing it.</summary>
        public const int RematchDays = 2;
        /// <summary>Days of quiet after any series before the rung below can
        /// call the player out again.</summary>
        public const int IncomingGapDays = 4;
        /// <summary>Chance per day that the rung below calls the player out,
        /// once the gap above has passed.</summary>
        public const float IncomingChancePerDay = 0.35f;
        /// <summary>How many NPC-vs-NPC call-outs a night can hold. One is
        /// enough to keep the board moving without making it noise: ten names
        /// and one swap a night is a board that looks different every week and
        /// recognisable every morning.</summary>
        public const int NpcChallengesPerNight = 1;
        /// <summary>Chance a given night has one at all.</summary>
        public const float NpcChallengeChance = 0.7f;
        /// <summary>Extra rep for taking a rank, on top of the usual tier gain
        /// for winning the races. Deliberately small: rep drives the purse
        /// ladder, and a big scalp bonus would let one series cascade the
        /// player up two tiers of prize money at once.</summary>
        public const int ScalpRepBonus = 2;
        /// <summary>Rep lost when somebody takes your spot. Smaller than the
        /// scalp bonus: the rank is the punishment.</summary>
        public const int DropRepLoss = 1;
        /// <summary>News lines the board remembers.</summary>
        public const int NewsKeep = 12;

        // ---------------- names ----------------
        public static BlacklistRival ByAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias)) return null;
            foreach (var r in Rivals) if (r.alias == alias) return r;
            return null;
        }

        /// <summary>The name standing on a rung RIGHT NOW, or null when that is
        /// the player's own row (or off the board).</summary>
        public static BlacklistRival ByRank(LifeState s, int rank) => ByAlias(AliasAt(s, rank));

        // ---------------- signature cars ----------------
        // Keyed on the ALIAS. It used to be keyed on the rank, which was fine
        // while a rank was a name; now that names move, a rank-keyed cache
        // hands GHOST's R34 to whoever is standing at #2 this morning.
        static readonly Dictionary<string, CarSpec> carCache = new Dictionary<string, CarSpec>();

        /// <summary>
        /// Resolve a rival's car from the runtime catalog by name. Catalog ids are
        /// generated at bake time, so the NAME is the stable key.
        ///
        /// Deviation from RG2, deliberately: within the first pattern that matches
        /// anything, this takes the most EXPENSIVE match rather than the first.
        /// The catalog is price-sorted, so "first" means "cheapest", which handed
        /// KAZE the $13.5k 185 hp FC when the catalog also holds a $18.5k 215 hp
        /// one under the same pattern. Same car, better example of it.
        /// </summary>
        public static CarSpec ResolveCar(BlacklistRival rival)
        {
            if (rival == null) return null;
            if (carCache.TryGetValue(rival.alias, out var cached)) return cached;

            CarSpec found = null;
            var all = CarCatalog.All;
            foreach (var pattern in rival.carMatch.Split('|'))
            {
                var re = new Regex(pattern, RegexOptions.IgnoreCase);
                foreach (var c in all)
                    if (re.IsMatch(c.name) && (found == null || c.price > found.price)) found = c;
                if (found != null) break;
            }
            carCache[rival.alias] = found;
            return found;
        }

        public static string CarName(BlacklistRival rival)
        {
            var spec = ResolveCar(rival);
            return spec != null ? spec.name : (rival != null ? rival.carLabel : "");
        }

        // ---------------- the board ----------------
        /// <summary>
        /// The order, guaranteed sound: every rival on it exactly once, the
        /// player on it exactly once, nothing else.
        ///
        /// Called by everything that reads the board rather than trusted to a
        /// migration, because the save is edited by hand in testing, a rival
        /// could be added to the roster in a patch, and a list that is one name
        /// short would otherwise read as somebody having no rank at all.
        /// </summary>
        public static List<RankEntry> Board(LifeState s)
        {
            if (s.blBoard == null) s.blBoard = new List<RankEntry>();
            if (!NeedsRepair(s.blBoard)) return s.blBoard;

            var seen = new HashSet<string>();
            // Drop blanks, strangers and duplicates, keeping the HIGHEST copy
            // of a name that somehow got onto the board twice — walking the
            // list from the bottom would silently demote them instead.
            var keep = new List<RankEntry>(s.blBoard.Count);
            foreach (var e in s.blBoard)
            {
                bool known = e != null && !string.IsNullOrEmpty(e.alias) &&
                             (e.alias == PlayerKey || ByAlias(e.alias) != null);
                if (known && seen.Add(e.alias)) keep.Add(e);
            }
            s.blBoard = keep;
            // Anyone missing joins at the bottom, in start-rank order — which
            // is also where a brand-new career puts the player.
            foreach (var r in Rivals)
                if (!seen.Contains(r.alias)) s.blBoard.Add(new RankEntry { alias = r.alias });
            if (!seen.Contains(PlayerKey)) s.blBoard.Add(new RankEntry { alias = PlayerKey });
            return s.blBoard;
        }

        /// <summary>
        /// Is the board unusable as it stands? Checked before every read
        /// because <see cref="Board"/> is read constantly — eleven rows of UI
        /// ask it a dozen questions each — and rebuilding a list to answer
        /// "yes, it is fine" would allocate one per question.
        ///
        /// The player is not looked for by name: with eleven entries, all of
        /// them known and all of them distinct, and only ten rivals in
        /// existence, one of them can only be the player.
        /// </summary>
        static bool NeedsRepair(List<RankEntry> b)
        {
            if (b.Count != BoardSize) return true;
            for (int i = 0; i < b.Count; i++)
            {
                var e = b[i];
                if (e == null || string.IsNullOrEmpty(e.alias)) return true;
                if (e.alias != PlayerKey && ByAlias(e.alias) == null) return true;
                for (int j = i + 1; j < b.Count; j++)
                    if (b[j] != null && b[j].alias == e.alias) return true;
            }
            return false;
        }

        /// <summary>A fresh board: the ten in their start order, the player at
        /// the bottom. Nobody has beaten anybody yet, and the player has to
        /// take the ladder one rung at a time.</summary>
        public static void SeedBoard(LifeState s)
        {
            s.blBoard = new List<RankEntry>();
            for (int rank = 1; rank <= Rivals.Length; rank++)
                foreach (var r in Rivals)
                    if (r.startRank == rank) s.blBoard.Add(new RankEntry { alias = r.alias });
            s.blBoard.Add(new RankEntry { alias = PlayerKey });
        }

        public static RankEntry EntryOf(LifeState s, string alias)
        {
            foreach (var e in Board(s)) if (e.alias == alias) return e;
            return null;
        }

        /// <summary>1-based rank, or 0 when the name is not on the board.</summary>
        public static int RankOf(LifeState s, string alias)
        {
            var board = Board(s);
            for (int i = 0; i < board.Count; i++) if (board[i].alias == alias) return i + 1;
            return 0;
        }

        public static string AliasAt(LifeState s, int rank)
        {
            var board = Board(s);
            return rank >= 1 && rank <= board.Count ? board[rank - 1].alias : null;
        }

        public static int PlayerRank(LifeState s) => RankOf(s, PlayerKey);

        public static RivalStatus StatusOf(LifeState s, string alias)
        {
            if (alias == PlayerKey) return RivalStatus.You;
            return RankOf(s, alias) < PlayerRank(s) ? RivalStatus.Above : RivalStatus.Below;
        }

        /// <summary>The name directly above the player — the only one they can
        /// call out. Null when the player is already top of the board.</summary>
        public static BlacklistRival Above(LifeState s) => ByRank(s, PlayerRank(s) - 1);

        /// <summary>The name directly below — the only one that can call the
        /// player out. Null when the player is bottom of the board, which is
        /// where a career starts and why nobody pesters a beginner.</summary>
        public static BlacklistRival Below(LifeState s) => ByRank(s, PlayerRank(s) + 1);

        /// <summary>The rival in the live series, or null.</summary>
        public static BlacklistRival Opponent(LifeState s) =>
            s != null && s.blChallenge != null && s.blChallenge.Live
                ? ByAlias(s.blChallenge.alias) : null;

        /// <summary>
        /// The name the player can race RIGHT NOW: the one they are already in
        /// a series with, or the one directly above them.
        ///
        /// Kept under the old name because it is the same question every caller
        /// was asking — the car meet parks this rival in seat 0, the pre-race
        /// page names them, the HUD titles the result after them.
        /// </summary>
        public static BlacklistRival OpenRival(LifeState s)
        {
            if (s == null) return null;
            var live = Opponent(s);
            return live ?? (CanChallengeUp(s) ? Above(s) : null);
        }

        /// <summary>The rung being raced for: the higher of the two, which is
        /// the challenger's prize and the defender's to keep.</summary>
        public static int StakeRank(LifeState s)
        {
            if (s == null || s.blChallenge == null || !s.blChallenge.Live) return 0;
            int mine = PlayerRank(s), theirs = RankOf(s, s.blChallenge.alias);
            return Mathf.Min(mine, theirs);
        }

        // ---------------- opening a series ----------------
        /// <summary>Why the player cannot call out the name above them right
        /// now, or null when they can. The board prints this where the
        /// CHALLENGE button would be — nothing pressable refuses.</summary>
        public static string ChallengeRefusal(LifeState s)
        {
            if (s == null) return "no career";
            if (s.blChallenge != null && s.blChallenge.Live)
                return "one call-out at a time — finish this one";
            var up = Above(s);
            if (up == null) return "nobody left above you";
            if (s.blRematchAlias == up.alias && s.day < s.blRematchDay)
                return "they are not taking your calls until " +
                       LifeRules.LogDate(s.blRematchDay);
            return null;
        }

        public static bool CanChallengeUp(LifeState s) => ChallengeRefusal(s) == null;

        /// <summary>
        /// Call out the name above you. Returns the headline, or null when the
        /// board says no.
        /// </summary>
        public static string ChallengeUp(LifeState s)
        {
            if (!CanChallengeUp(s)) return null;
            var up = Above(s);
            Open(s, up, incoming: false);
            string headline = "YOU CALLED OUT #" + RankOf(s, up.alias) + " " + up.alias;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + headline + " — best of " +
                              SeriesRaces + " at " + VenueName(s.blChallenge.trackIndex));
            return headline;
        }

        /// <summary>
        /// The rung below knocks. Posts the call-out to the mail with the
        /// deadline on it, because the one thing the player must not be able to
        /// say afterwards is that nobody told them.
        /// </summary>
        public static string ChallengeDown(LifeState s)
        {
            var below = Below(s);
            if (below == null) return null;
            Open(s, below, incoming: true);
            var ch = s.blChallenge;
            string headline = "#" + RankOf(s, below.alias) + " " + below.alias +
                              " IS COMING FOR YOUR SPOT";
            s.mail.Add(new MailItem
            {
                day = s.day,
                subject = headline,
                body = "Best of " + SeriesRaces + " at " + VenueName(ch.trackIndex) +
                       ", by " + LifeRules.LogDate(ch.deadlineDay) +
                       ". Every race you do not run is a race you lose.",
                expiresDay = ch.deadlineDay,
            });
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": CALL-OUT — " + headline);
            return headline;
        }

        static void Open(LifeState s, BlacklistRival rival, bool incoming)
        {
            s.blChallenge = new RankChallenge
            {
                alias = rival.alias,
                incoming = incoming,
                openedDay = s.day,
                deadlineDay = s.day + ChallengeDays,
                trackIndex = PickVenue(rival, s.day),
            };
        }

        /// <summary>
        /// The road a series runs on, from the rival's own style. Their
        /// call-out, their turf — and a fixed one, so three legs are a series
        /// rather than a tour.
        ///
        /// Falls back to whatever the player last raced when a build is missing
        /// the venues for a style: a challenge that cannot find a road is still
        /// a challenge.
        /// </summary>
        static int PickVenue(BlacklistRival rival, int seed)
        {
            string[] ids =
                rival == null ? CityVenues :
                rival.venue == "drag" ? DragVenues :
                rival.venue == "oval" ? OvalVenues : CityVenues;
            // Seeded off the NAME, by a sum this codebase controls: the
            // framework's own string hash is not promised to be the same number
            // twice, and a venue that moves between runs is a series that ran
            // somewhere else when you reloaded.
            int nameSeed = 0;
            foreach (char c in rival.alias) nameSeed = nameSeed * 31 + c;
            var rng = new System.Random(seed * 7919 + nameSeed);
            int start = rng.Next(ids.Length);
            for (int k = 0; k < ids.Length; k++)
            {
                int idx = TrackCatalog.IndexOf(ids[(start + k) % ids.Length]);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        static readonly string[] DragVenues = { "DragQuarter", "DragEighth", "LangstonBridge" };
        static readonly string[] OvalVenues = { "CityCircuit", "HarborPoint", "AirfieldSprint" };
        static readonly string[] CityVenues = { "TryonSprint", "UptownLoop", "IndependenceSprint", "CityCircuit" };

        static string VenueName(int trackIndex) =>
            trackIndex >= 0 && trackIndex < TrackCatalog.Count
                ? TrackCatalog.At(trackIndex).name : "wherever you like";

        // ---------------- racing it ----------------
        /// <summary>The venue a leg runs at, with a sane fallback for a build
        /// that has lost the rival's road.</summary>
        public static int SeriesTrack(LifeState s)
        {
            var ch = s != null ? s.blChallenge : null;
            if (ch == null || ch.trackIndex < 0 || ch.trackIndex >= TrackCatalog.Count)
                return Mathf.Clamp(s != null ? s.trackIndex : 0, 0, TrackCatalog.Count - 1);
            return ch.trackIndex;
        }

        /// <summary>Stamp a leg as GONE TO THE START LINE. From here on it can
        /// only be won or lost — quitting out of it loses it, which is the
        /// owner's rule about missing a race applied to the one way of missing
        /// one that does not involve the calendar.</summary>
        public static void BeginLeg(LifeState s)
        {
            if (s != null && s.blChallenge != null && s.blChallenge.Live)
                s.blChallenge.legInFlight = true;
        }

        /// <summary>
        /// A leg raced because the player walked up to them in a car park.
        ///
        /// The open rival parks at the meet (CarMeets seat 0), and calling them
        /// out there has to mean the same thing as calling them out from the
        /// board — otherwise the lot is a place where the ladder does not
        /// count. So this opens the series if there is not one yet, and either
        /// way stamps the leg. False when that name is not somebody the player
        /// can race for a rank right now, in which case the meet race still
        /// happens; it is just a race.
        /// </summary>
        public static bool BeginMeetLeg(LifeState s, string alias)
        {
            if (s == null || string.IsNullOrEmpty(alias)) return false;
            var ch = s.blChallenge;
            if (ch != null && ch.Live)
            {
                if (ch.alias != alias) return false;
                BeginLeg(s);
                return true;
            }
            var up = Above(s);
            if (up == null || up.alias != alias || !CanChallengeUp(s)) return false;
            ChallengeUp(s);
            BeginLeg(s);
            return true;
        }

        /// <summary>
        /// Bank a leg. Returns the headline for the result toast, or null when
        /// there was no series to bank it against.
        /// </summary>
        public static string RecordLeg(LifeState s, string alias, bool won)
        {
            var ch = s != null ? s.blChallenge : null;
            if (ch == null || !ch.Live || ch.alias != alias) return null;
            ch.legInFlight = false;
            Score(s, ch, won);
            return ch.Live ? LegLine(s, ch) : lastResolution;
        }

        /// <summary>Award one leg and resolve the series if that settles it.
        /// </summary>
        static void Score(LifeState s, RankChallenge ch, bool playerWon)
        {
            var you = EntryOf(s, PlayerKey);
            var them = EntryOf(s, ch.alias);
            if (playerWon) { ch.youLegs++; if (you != null) you.wins++; if (them != null) them.losses++; }
            else { ch.themLegs++; if (them != null) them.wins++; if (you != null) you.losses++; }
            if (ch.youLegs >= LegsToWin || ch.themLegs >= LegsToWin) Resolve(s, ch);
        }

        /// <summary>"RACE 2 OF 3 · 1-0" — where a series stands. Empty when
        /// there is none.</summary>
        public static string SeriesLine(LifeState s)
        {
            var ch = s != null ? s.blChallenge : null;
            if (ch == null || !ch.Live) return "";
            return "RACE " + ch.LegNumber + " OF " + SeriesRaces + "  ·  " +
                   ch.youLegs + "-" + ch.themLegs;
        }

        /// <summary>
        /// The same series as the RACE SCENE should say it: which leg this is
        /// and what winning it all is worth.
        ///
        /// Deliberately NOT the running score. This string is stamped at the
        /// start line and read back at the finish, and the leg just raced is
        /// not banked until the player is home — so a score printed over the
        /// finish line would always be one race out of date, under a banner
        /// announcing the result of that very race.
        /// </summary>
        public static string SeriesBanner(LifeState s)
        {
            var ch = s != null ? s.blChallenge : null;
            if (ch == null || !ch.Live) return "";
            return "RACE " + ch.LegNumber + " OF " + SeriesRaces + " — FIRST TO " +
                   LegsToWin + " TAKES #" + StakeRank(s);
        }

        static string LegLine(LifeState s, RankChallenge ch) =>
            ch.alias + " " + ch.youLegs + "-" + ch.themLegs + "  ·  RACE " + ch.LegNumber +
            " OF " + SeriesRaces;

        /// <summary>Headline from the last series to END. Read by the callers
        /// that want to say what happened rather than where the series stands.
        /// </summary>
        static string lastResolution;

        /// <summary>
        /// Settle it: the board moves, or it does not, and everything about the
        /// series is cleared either way.
        /// </summary>
        static void Resolve(LifeState s, RankChallenge ch)
        {
            bool youWon = ch.youLegs > ch.themLegs;
            string alias = ch.alias;
            bool incoming = ch.incoming;
            string score = ch.youLegs + "-" + ch.themLegs;

            // The two rows are adjacent — the nightly sim will not touch a
            // driver who is mid-series, which is what keeps that true — so the
            // whole of "the board moves" is one swap.
            bool takes = youWon != incoming;   // you won a call-out you made, or they won theirs
            string headline;
            if (takes && youWon)
            {
                int rank = RankOf(s, alias);
                Swap(s, PlayerKey, alias);
                s.streetRep = Mathf.Min(100f, s.streetRep + ScalpRepBonus);
                s.money += Purse(rank);
                headline = "YOU TAKE #" + rank + " OFF " + alias + " (" + score + ")";
                Post(s, headline);
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + headline + " — " +
                                  MenuKit.Money(Purse(rank)));
            }
            else if (takes)
            {
                int rank = PlayerRank(s);
                Swap(s, PlayerKey, alias);
                s.streetRep = Mathf.Max(0f, s.streetRep - DropRepLoss);
                headline = alias + " TAKES #" + rank + " OFF YOU (" + score + ")";
                Post(s, headline);
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + headline);
            }
            else if (youWon)
            {
                headline = "YOU HOLD #" + PlayerRank(s) + " — " + alias + " STAYS PUT (" + score + ")";
                s.streetRep = Mathf.Min(100f, s.streetRep + ScalpRepBonus);
                Post(s, headline);
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + headline);
            }
            else
            {
                headline = "#" + RankOf(s, alias) + " " + alias + " KEEPS THE SPOT (" + score + ")";
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + headline);
            }

            // A name you could not beat is a name that will not take your call
            // for a couple of days. It is also what stops a forfeited call-out
            // from being free.
            if (!youWon) { s.blRematchAlias = alias; s.blRematchDay = s.day + RematchDays; }
            s.blIncomingReadyDay = s.day + IncomingGapDays;
            // KILL THE OBJECT, not just the save's pointer to it. A caller
            // holding this series in a local — Forfeit walks three legs, and
            // the second one can settle it — would otherwise still read it as
            // Live and score the third into a fight that is over. That scored
            // a forfeited call-out twice and swapped the board back, handing
            // the rank straight back to the player who had just lost it.
            ch.alias = "";
            s.blChallenge = new RankChallenge();
            lastResolution = headline;
        }

        static void Swap(LifeState s, string a, string b)
        {
            var board = Board(s);
            int i = RankOf(s, a) - 1, j = RankOf(s, b) - 1;
            if (i < 0 || j < 0) return;
            var t = board[i]; board[i] = board[j]; board[j] = t;
        }

        // ---------------- forfeits ----------------
        /// <summary>
        /// The owner's rule, in one method: a race you do not run is a race you
        /// lose. Hands <paramref name="legs"/> legs to the rival, one at a
        /// time, so the series resolves the moment they have enough of them.
        /// </summary>
        static void Forfeit(LifeState s, int legs)
        {
            var ch = s.blChallenge;
            for (int i = 0; i < legs && ch.Live; i++) Score(s, ch, false);
        }

        /// <summary>How many legs are still owed.</summary>
        static int LegsLeft(RankChallenge ch) => Mathf.Max(0, SeriesRaces - ch.LegsRun);

        /// <summary>Walk away. Every unraced leg goes to them, which for an
        /// incoming call-out means handing over the rank.</summary>
        public static string Decline(LifeState s)
        {
            var ch = s != null ? s.blChallenge : null;
            if (ch == null || !ch.Live) return null;
            string alias = ch.alias;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": WALKED AWAY from " + alias +
                              " — every race left is a loss");
            Forfeit(s, LegsLeft(ch));
            return lastResolution ?? ("YOU WALKED AWAY FROM " + alias);
        }

        /// <summary>
        /// A leg that went to the start line and never came back — the player
        /// quit out of it, or the app did. Called when the home screen opens
        /// with no race result in hand, which is the only moment anything knows
        /// the race is over without knowing how it went.
        /// </summary>
        public static string SweepInFlight(LifeState s)
        {
            var ch = s != null ? s.blChallenge : null;
            if (ch == null || !ch.Live || !ch.legInFlight) return null;
            ch.legInFlight = false;
            string alias = ch.alias;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": walked out on " + alias +
                              " mid-series — the race counts as a loss");
            Score(s, ch, false);
            return ch.Live ? alias + " TAKES IT — YOU DID NOT FINISH" : lastResolution;
        }

        // ---------------- the nightly board ----------------
        /// <summary>
        /// One day on the blacklist, run at the rollover:
        ///   deadlines close → the ten of them race each other → somebody
        ///   below may knock.
        ///
        /// Returns a headline for the pager when the player needs to know, else
        /// null. The NPC results go to <see cref="LifeState.blNews"/> and stay
        /// off the player's diary — see the field's note.
        /// </summary>
        public static string TickLadder(LifeState s)
        {
            if (s == null) return null;
            Board(s);
            string page = null;

            // 1. the deadline. Checked FIRST, so a series that ran out
            //    overnight is settled before anybody new knocks.
            var ch = s.blChallenge;
            if (ch != null && ch.Live && s.day > ch.deadlineDay)
            {
                string alias = ch.alias;
                int left = LegsLeft(ch);
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": the " + alias +
                                  " call-out ran out — " + left +
                                  (left == 1 ? " race" : " races") + " unraced");
                Forfeit(s, left);
                page = lastResolution;
            }

            // 2. the rest of the board does what the player does: calls out the
            //    name above and races it three times. Simulated rather than
            //    driven, obviously — but on the same rules, which is what makes
            //    the order mean the same thing wherever you are on it.
            if (Random.value < NpcChallengeChance)
                for (int i = 0; i < NpcChallengesPerNight; i++) NpcChallenge(s);

            // 3. somebody below knocks. Only ever the rung directly below, only
            //    when nothing else is live, and only after the quiet gap — a
            //    board that called you out every morning would be a board you
            //    stopped reading.
            if ((s.blChallenge == null || !s.blChallenge.Live) &&
                s.day >= s.blIncomingReadyDay && Below(s) != null &&
                Random.value < IncomingChancePerDay)
            {
                string knock = ChallengeDown(s);
                if (knock != null) page = knock;
            }
            return page;
        }

        /// <summary>
        /// One NPC calls out the name above them and they race it three times.
        ///
        /// The player's row is a WALL: a challenge is between neighbours, and
        /// the player is somebody's neighbour, so the only way past them is
        /// through them. Anyone mid-series with the player is skipped for the
        /// same reason — the pair has to stay adjacent until it resolves.
        /// </summary>
        static void NpcChallenge(LifeState s)
        {
            var board = Board(s);
            string busy = s.blChallenge != null && s.blChallenge.Live ? s.blChallenge.alias : null;

            // Everyone with an NPC directly above them is a possible challenger.
            var picks = new List<int>();
            for (int i = 1; i < board.Count; i++)
            {
                if (board[i].IsPlayer || board[i - 1].IsPlayer) continue;
                if (busy != null && (board[i].alias == busy || board[i - 1].alias == busy)) continue;
                picks.Add(i);
            }
            if (picks.Count == 0) return;

            int idx = picks[Random.Range(0, picks.Count)];
            var lower = board[idx];
            var upper = board[idx - 1];
            var lowRival = ByAlias(lower.alias);
            var upRival = ByAlias(upper.alias);
            if (lowRival == null || upRival == null) return;

            int lowLegs = 0, upLegs = 0;
            float p = LegOdds(lowRival.skill, upRival.skill);
            while (lowLegs < LegsToWin && upLegs < LegsToWin)
            {
                if (Random.value < p) lowLegs++; else upLegs++;
            }
            lower.wins += lowLegs; lower.losses += upLegs;
            upper.wins += upLegs; upper.losses += lowLegs;

            int contested = idx;   // the upper driver's rank, 1-based, is idx
            if (lowLegs > upLegs)
            {
                var t = board[idx]; board[idx] = board[idx - 1]; board[idx - 1] = t;
                Post(s, lower.alias + " takes #" + contested + " off " + upper.alias +
                        " (" + lowLegs + "-" + upLegs + ")");
            }
            else
            {
                Post(s, upper.alias + " holds #" + contested + " against " + lower.alias +
                        " (" + upLegs + "-" + lowLegs + ")");
            }
        }

        /// <summary>
        /// The challenger's chance of taking one leg, from the two skills.
        ///
        /// Fenced well inside 0 and 1 because the board is a LADDER: adjacent
        /// names are close in skill by construction, and an upset has to stay
        /// possible or the order would freeze into the roster's own list and
        /// the whole thing would be furniture. At the usual 0.02 gap this is
        /// about 38% a leg, which is a challenger taking one series in three.
        /// </summary>
        public static float LegOdds(float challengerSkill, float defenderSkill) =>
            Mathf.Clamp(0.5f + (challengerSkill - defenderSkill) * 6f, 0.25f, 0.75f);

        /// <summary>Write a line on the board's own news strip.</summary>
        static void Post(LifeState s, string line)
        {
            if (s.blNews == null) s.blNews = new List<string>();
            s.blNews.Add(LifeRules.LogDate(s.day) + ": " + line);
            while (s.blNews.Count > NewsKeep) s.blNews.RemoveAt(0);
        }

        // ---------------- flavour ----------------
        public static string Taunt(BlacklistRival rival, string playerCarName)
        {
            if (rival == null || rival.taunts.Length == 0) return "";
            string line = rival.taunts[Random.Range(0, rival.taunts.Length)];
            return line.Replace("{playerCar}",
                string.IsNullOrEmpty(playerCarName) ? "that thing" : playerCarName);
        }

        /// <summary>Purse for one leg, and the bonus for taking a rank. Climbs
        /// steeply up the board because the top of it is where the whole
        /// career is pointed.</summary>
        public static int Purse(int rank) => 400 + (BoardSize - Mathf.Clamp(rank, 1, BoardSize)) * 220;
    }
}
