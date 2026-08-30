using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>One rung of the ladder. Static config; the progression lives on
    /// <see cref="LifeState"/> so it saves with everything else.</summary>
    public class BlacklistRival
    {
        /// <summary>10 (entry) … 1 (boss).</summary>
        public int rank;
        public string alias;
        /// <summary>Case-insensitive patterns matched against catalog car NAMES,
        /// '|'-separated and tried IN ORDER — 'CTR2|911' means "the CTR2 if the
        /// catalog has one, else any 911", not "whichever comes first in catalog
        /// order". Split naively, so no parenthesised groups.</summary>
        public string carMatch;
        /// <summary>Shown when no catalog car matches.</summary>
        public string carLabel;
        /// <summary>Where RG2 says they race. Recorded for flavour only: every
        /// challenge here runs on the one circuit the game has, the same
        /// concession RG2 makes when it runs all of them as meet drags.</summary>
        public string venue;
        public int gateWins;
        public int gateRep;
        /// <summary>AI skill for the challenge. This, not the car, is what makes
        /// the ladder get harder: the signature cars are chosen for identity and
        /// come out non-monotonic (PENNY's Miata is slower than JUICE's Civic),
        /// so difficulty has to live somewhere that ascends cleanly.</summary>
        public float skill;
        /// <summary>Pre-race trash talk. Slot: {playerCar}.</summary>
        public string[] taunts;
    }

    public enum RivalStatus { Locked, Open, Beaten }

    /// <summary>
    /// The BLACKLIST: ten named rivals over the street-rep ladder, ported from
    /// RG2's config/blacklist.ts and blacklistProgress.ts.
    ///
    ///   race → wins/rep climb → the next rival's gate clears → the call-out
    ///   fires ONCE → challenge them → win records the rank permanently → the
    ///   next name up pages you when ITS gate clears.
    ///
    /// Out of scope here as in RG2: pink slips and boss-car uniqueness.
    /// </summary>
    public static class Blacklist
    {
        // Ordered entry-first (rank 10 down to 1), which is also display order
        // reversed — the board draws the boss at the top.
        public static readonly BlacklistRival[] Rivals =
        {
            new BlacklistRival { rank = 10, alias = "JUICE", venue = "drag",
                carMatch = "Civic.*EK|Civic.*Type R|Civic.*SiR|Civic", carLabel = "Honda Civic",
                gateWins = 3, gateRep = 10, skill = 0.90f, taunts = new[] {
                    "You think you're gonna beat me with that {playerCar}?",
                    "Pull up to the strip. Bring lunch money." } },
            new BlacklistRival { rank = 9, alias = "PENNY", venue = "oval",
                carMatch = "Eunos Roadster|MX-5|Miata", carLabel = "Mazda Roadster",
                gateWins = 4, gateRep = 18, skill = 0.92f, taunts = new[] {
                    "Corners matter, hotshot. Meet me at the oval.",
                    "That {playerCar} push wide in turn one? Thought so." } },
            new BlacklistRival { rank = 8, alias = "DEACON", venue = "city",
                carMatch = "240SX|Sileighty|Silvia.*S13|Silvia.*S14|Silvia", carLabel = "Nissan Silvia",
                gateWins = 5, gateRep = 25, skill = 0.94f, taunts = new[] {
                    "These streets got a toll, and you ain't paid it.",
                    "Bring that {playerCar}. I need a good laugh." } },
            new BlacklistRival { rank = 7, alias = "KAZE", venue = "drag",
                carMatch = "RX-7.*FC|Savanna|RX-7", carLabel = "Mazda RX-7 FC",
                gateWins = 7, gateRep = 33, skill = 0.96f, taunts = new[] {
                    "Rotary sings, piston begs. Listen close.",
                    "Your {playerCar} against my car? Short race." } },
            new BlacklistRival { rank = 6, alias = "BIG SAL", venue = "drag",
                carMatch = "Cuda|Barracuda|Charger|Super Bee", carLabel = "Plymouth Cuda",
                gateWins = 9, gateRep = 41, skill = 0.97f, taunts = new[] {
                    "Eight cylinders of American arithmetic, kid.",
                    "That {playerCar} got a spare bumper? It'll need one." } },
            new BlacklistRival { rank = 5, alias = "WRENCH", venue = "city",
                carMatch = "Impreza.*22B|Impreza.*STi|Impreza.*WRX|Impreza", carLabel = "Subaru Impreza",
                gateWins = 11, gateRep = 50, skill = 0.98f, taunts = new[] {
                    "I built mine. Who built yours?",
                    "Four driven wheels beat your {playerCar} in the wet AND the dry." } },
            new BlacklistRival { rank = 4, alias = "DUCHESS", venue = "oval",
                carMatch = "S2000", carLabel = "Honda S2000",
                gateWins = 13, gateRep = 58, skill = 1.00f, taunts = new[] {
                    "Nine thousand RPM of goodbye.",
                    "Keep your {playerCar} off my racing line." } },
            new BlacklistRival { rank = 3, alias = "PREACHER", venue = "city",
                carMatch = "Supra RZ|Supra.*Twin|Supra", carLabel = "Toyota Supra",
                gateWins = 15, gateRep = 66, skill = 1.01f, taunts = new[] {
                    "Everybody wants a sermon. Nobody wants the collection plate.",
                    "Boost is a faith, and your {playerCar} is an unbeliever." } },
            new BlacklistRival { rank = 2, alias = "GHOST", venue = "city",
                carMatch = "GT-R.*R34|Skyline.*R34|GT-R", carLabel = "Nissan GT-R R34",
                gateWins = 18, gateRep = 75, skill = 1.03f, taunts = new[] {
                    "You won't see me. That's the point.",
                    "ATTESA does the math your right foot can't." } },
            new BlacklistRival { rank = 1, alias = "CALLAHAN", venue = "city",
                carMatch = "CTR2|RUF.*CTR|RUF.*BTR|911", carLabel = "RUF CTR2",
                gateWins = 20, gateRep = 85, skill = 1.05f, taunts = new[] {
                    "Every name above yours earned it. Every name below yours quit.",
                    "This city has one king. You're looking at him." } },
        };

        /// <summary>Days a call-out stands before it goes cold. The rival stays
        /// OPEN either way — what expires is the invitation, not the fight.</summary>
        public const int PageDays = 3;
        /// <summary>Extra rep for taking a name, on top of the usual tier gain
        /// for winning a race. Deliberately small: the gates were tuned against
        /// the +6/+4/+2 tier ladder, and a big scalp bonus would let one win
        /// cascade the player up two rungs at once.</summary>
        public const int ScalpRepBonus = 2;

        public static BlacklistRival ByRank(int rank)
        {
            foreach (var r in Rivals) if (r.rank == rank) return r;
            return null;
        }

        // ---------------- signature cars ----------------
        static readonly Dictionary<int, CarSpec> carCache = new Dictionary<int, CarSpec>();

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
            if (carCache.TryGetValue(rival.rank, out var cached)) return cached;

            CarSpec found = null;
            var all = CarCatalog.All;
            foreach (var pattern in rival.carMatch.Split('|'))
            {
                var re = new Regex(pattern, RegexOptions.IgnoreCase);
                foreach (var c in all)
                    if (re.IsMatch(c.name) && (found == null || c.price > found.price)) found = c;
                if (found != null) break;
            }
            carCache[rival.rank] = found;
            return found;
        }

        public static string CarName(BlacklistRival rival)
        {
            var spec = ResolveCar(rival);
            return spec != null ? spec.name : (rival != null ? rival.carLabel : "");
        }

        // ---------------- progression ----------------
        /// <summary>A rival is challengeable when every rank BELOW them is beaten
        /// and their own wins/rep gate clears. Rep decay can close a gate again —
        /// but only on a rival you have not yet fought; defeats are permanent.
        /// </summary>
        public static RivalStatus StatusOf(LifeState s, BlacklistRival rival)
        {
            if (rival == null) return RivalStatus.Locked;
            if (s.blDefeated.Contains(rival.rank)) return RivalStatus.Beaten;
            foreach (var lower in Rivals)
                if (lower.rank > rival.rank && !s.blDefeated.Contains(lower.rank))
                    return RivalStatus.Locked;
            return s.streetRacesWon >= rival.gateWins && s.streetRep >= rival.gateRep
                ? RivalStatus.Open : RivalStatus.Locked;
        }

        /// <summary>The next undefeated name going UP the ladder. Null once the
        /// boss is down.</summary>
        public static BlacklistRival NextRival(LifeState s)
        {
            foreach (var r in Rivals) if (!s.blDefeated.Contains(r.rank)) return r;
            return null;
        }

        /// <summary>The rival the player can actually challenge right now, or
        /// null. Only ever one: the ladder is strictly sequential.</summary>
        public static BlacklistRival OpenRival(LifeState s)
        {
            var next = NextRival(s);
            return next != null && StatusOf(s, next) == RivalStatus.Open ? next : null;
        }

        /// <summary>
        /// Rollover hook: when the next rival's gate has just cleared, fire their
        /// call-out ONCE and post it. Returns the headline for a toast, or null.
        ///
        /// The one-shot latch is the point. Rep decays between races, so a gate
        /// the player is hovering on opens and closes repeatedly; without the
        /// latch that is a pager going off every other morning about a fight the
        /// player already knows about.
        /// </summary>
        public static string TickPager(LifeState s)
        {
            var rival = OpenRival(s);
            if (rival == null) return null;
            if (s.blPaged.Contains(rival.rank)) return null;

            s.blPaged.Add(rival.rank);
            string headline = "#" + rival.rank + " " + rival.alias + ": COME TAKE MY SPOT";
            s.mail.Add(new MailItem
            {
                day = s.day,
                subject = headline,
                body = rival.alias + " is driving a " + CarName(rival) +
                       ". Find them on the RIVALS board.",
                expiresDay = s.day + PageDays,
            });
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": PAGE — " + headline);
            return headline;
        }

        public static string Taunt(BlacklistRival rival, string playerCarName)
        {
            if (rival == null || rival.taunts.Length == 0) return "";
            string line = rival.taunts[Random.Range(0, rival.taunts.Length)];
            return line.Replace("{playerCar}",
                string.IsNullOrEmpty(playerCarName) ? "that thing" : playerCarName);
        }

        /// <summary>Purse for taking a rank. Climbs steeply because a challenge
        /// is the one race the player cannot grind — there are exactly ten.
        /// </summary>
        public static int Purse(int rank) => 400 + (10 - Mathf.Clamp(rank, 1, 10)) * 220;

        /// <summary>
        /// Bank a challenge result. Called from the race apply-back, after the
        /// normal payout has already run — a rival race IS a street race and
        /// counts for wins, rep and wear like any other.
        /// </summary>
        public static string RecordResult(LifeState s, int rank, bool won)
        {
            var rival = ByRank(rank);
            if (rival == null) return null;

            if (!won)
            {
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": #" + rank + " " + rival.alias +
                                  " keeps the spot");
                return "#" + rank + " " + rival.alias + " KEEPS THE SPOT";
            }

            if (!s.blDefeated.Contains(rank)) s.blDefeated.Add(rank);
            s.streetRep = Mathf.Min(100f, s.streetRep + ScalpRepBonus);

            string headline = "#" + rank + " " + rival.alias + " IS DOWN. LADDER MOVES.";
            s.mail.Add(new MailItem { day = s.day, subject = headline, body = "The board updates." });
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": " + headline);
            return headline;
        }
    }
}
