using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Piedmont Auto Salvage: a shelf of used parts that turns over on its own
    /// clock, reached from the paper the way the classifieds are.
    ///
    /// WHY IT EXISTS. Every other way to put condition back into a car in this
    /// game scales with what the car cost — <see cref="LifeRules.ServiceCost"/>
    /// and <see cref="Upgrades.CarCostMult"/> both run a sqrt of the price out
    /// to 3.5x, which is right for a shop quoting labour on an exotic and is
    /// what makes an expensive car a trap on a delivery driver's wage. A used
    /// part off a donor is not that: it costs roughly what it costs whatever it
    /// came off, so the yard's multiplier caps at 1.8. That gap IS the yard.
    /// It is how a career keeps something quick alive, and it is why the shop
    /// still exists for anyone who can afford not to gamble.
    ///
    /// WHAT IT COSTS YOU. Grade, and what grade means. Every pull is graded
    /// openly — you can see rust — and grade moves the price, how much of the
    /// part's rated restoration you actually get, and the chance it seeds a
    /// HIDDEN fault on the lane it went in on. Hidden, not announced: the yard
    /// feeds the inspection layer rather than working around it, so a cheap
    /// part that was a mistake is something the car tells you about on track
    /// first and an inspection names later.
    ///
    /// HOW IT TURNS OVER. Three shelves on three clocks — see
    /// <see cref="Shelf"/> — which is the whole reason it is a page worth
    /// opening rather than a shop that is always the same.
    /// </summary>
    public static class Junkyard
    {
        public const string YardName = "PIEDMONT AUTO SALVAGE";

        /// <summary>
        /// The three shelves, and they are three CLOCKS rather than three
        /// categories of thing.
        ///
        ///   Bin      — the picked-over stuff by the gate. Gone tomorrow,
        ///              restocked tomorrow. Cheap, small, always something.
        ///   Week     — a week's worth of what came in off the wreckers. The
        ///              shelf a player actually plans around.
        ///   BackLot  — engines, gearboxes, front clips, race hardware. Sits
        ///              for a month, so finding one is an event and losing one
        ///              costs you a month.
        ///
        /// Same shape as the classifieds — expire, then top back up on the
        /// rollover — but at three speeds instead of one, so the page reads
        /// differently depending on how long it has been since you last looked.
        /// </summary>
        public enum Shelf { Bin = 0, Week = 1, BackLot = 2 }

        public static readonly string[] ShelfNames =
            { "THE BIN — GONE TOMORROW", "THIS WEEK'S PULLS", "THE BACK LOT" };

        /// <summary>Rows each shelf holds, and the stall life of a part on it:
        /// minimum days plus a spread. The Bin's one-day floor is what makes it
        /// a different shelf and not just a cheaper one.</summary>
        static readonly (int slots, int minDays, int spreadDays)[] ShelfLife =
        {
            (5, 1, 2),      // Bin      — 1-2 days
            (4, 5, 5),      // Week     — 5-9 days
            (3, 25, 16),    // BackLot  — 25-40 days
        };

        public static int SlotsOn(Shelf shelf) => ShelfLife[(int)shelf].slots;

        // ================= what is on the shelves =================

        /// <summary>One kind of thing the yard can have: a used part that puts
        /// a condition lane back up. `add` is what a PERFECT example restores;
        /// grade takes its cut in <see cref="EffectiveAdd"/>.</summary>
        struct ServiceKind
        {
            public string label, donorHint, stat;
            public int add, basePrice, days;
            public ServiceKind(string label, string stat, int add, int basePrice,
                               int days, string donorHint)
            {
                this.label = label; this.stat = stat; this.add = add;
                this.basePrice = basePrice; this.days = days; this.donorHint = donorHint;
            }
        }

        // Priced against LifeRules.MechanicServices, which is the thing a player
        // compares these to: an OIL CHANGE is $50 for +15 engine at a 1.0x car.
        // The yard is a shade dearer per point there ON A CHEAP CAR and much
        // cheaper on an expensive one, because of the multiplier cap above —
        // plus it costs days and can bite. Nobody should buy used plugs for a
        // Civic; everybody should buy a used radiator for an NSX.
        static readonly ServiceKind[] BinStock =
        {
            new ServiceKind("USED PLUG SET",        "engine",  8,  18, 1, "pulled this morning"),
            new ServiceKind("AIR FILTER + HOSES",   "engine",  6,  12, 1, "off a runner"),
            new ServiceKind("USED ALTERNATOR",      "engine", 14,  55, 1, "spins free"),
            new ServiceKind("COIL + LEADS",         "engine", 10,  34, 1, "untested"),
            new ServiceKind("PART-WORN PAIR",       "tires",  18,  60, 1, "5mm left"),
            new ServiceKind("SINGLE STEEL WHEEL",   "tires",   8,  30, 1, "true, no buckle"),
            new ServiceKind("MIRROR + WIPERS",      "hp",      5,  10, 1, "colour-matched, nearly"),
            new ServiceKind("RATTLE CAN + PRIMER",  "paint",  12,  16, 1, "close enough at night"),
            new ServiceKind("TAIL LIGHT + TRIM",    "hp",      7,  22, 1, "one lens crazed"),
        };

        static readonly ServiceKind[] WeekStock =
        {
            new ServiceKind("USED RADIATOR",        "engine", 22, 120, 2, "pressure-tested"),
            new ServiceKind("STARTER + BATTERY",    "engine", 18,  95, 2, "cranks strong"),
            new ServiceKind("USED CLUTCH + FLYWHEEL","engine",26, 180, 3, "half a life left"),
            new ServiceKind("EXHAUST, CAT BACK",    "engine", 14, 110, 2, "one weld repair"),
            new ServiceKind("MATCHED SET + TYRES",  "tires",  45, 260, 2, "four the same, finally"),
            new ServiceKind("STRAIGHT FENDER",      "hp",     22, 110, 2, "no filler in it"),
            new ServiceKind("DOOR SKIN + GLASS",    "hp",     26, 150, 2, "winds up and down"),
            new ServiceKind("BUMPER, UNCRACKED",    "hp",     18, 100, 2, "tabs all there"),
            new ServiceKind("PANEL SET, RESPRAYED", "paint",  35, 190, 2, "shade off in daylight"),
        };

        static readonly ServiceKind[] BackLotStock =
        {
            new ServiceKind("DONOR ENGINE, RUNNING","engine", 60, 900, 5, "heard it run in the car"),
            new ServiceKind("GEARBOX, LOW MILES",   "engine", 40, 650, 4, "no crunch into second"),
            new ServiceKind("FRONT CLIP",           "hp",     55, 700, 4, "rails straight"),
            new ServiceKind("SHELL PANELS, WHOLE CAR","paint", 50, 520, 3, "one owner, one colour"),
            new ServiceKind("REAR SUBFRAME + DIFF", "hp",     38, 480, 3, "no play in it"),
        };

        static ServiceKind[] StockFor(Shelf shelf) =>
            shelf == Shelf.Bin ? BinStock : shelf == Shelf.Week ? WeekStock : BackLotStock;

        /// <summary>
        /// The upgrade hardware each shelf can turn up, and the highest stage
        /// that hardware can serve.
        ///
        /// A pull is a PART, not a plan: a set of second-hand coilovers is a
        /// set of coilovers whoever fits them, so the yard offers a category
        /// and a ceiling and the price is quoted against your car's next step
        /// when you look at it. The Bin never carries any — nothing on that
        /// shelf is a stage of anything — and the back lot is where the stage 3
        /// and 4 hardware turns up, which is what makes it worth the walk.
        /// </summary>
        static readonly (Upgrades.Kind kind, string label, int maxStage)[] WeekHardware =
        {
            (Upgrades.Kind.Brakes,     "USED PADS + ROTORS",      2),
            (Upgrades.Kind.Suspension, "TAKE-OFF SPRINGS + STRUTS",2),
            (Upgrades.Kind.Tires,      "SPORT TYRES, HALF WORN",   2),
            (Upgrades.Kind.Weight,     "LIGHT WHEELS + SEATS",     2),
            (Upgrades.Kind.Power,      "INTAKE + HEADER, USED",    2),
        };

        static readonly (Upgrades.Kind kind, string label, int maxStage)[] BackLotHardware =
        {
            (Upgrades.Kind.Power,      "TURBO + MANIFOLD, USED",   4),
            (Upgrades.Kind.Brakes,     "BIG BRAKE KIT, TRACK CAR", 4),
            (Upgrades.Kind.Suspension, "COILOVERS OFF A RACE CAR", 4),
            (Upgrades.Kind.Weight,     "CARBON PANELS, REPAIRED",  4),
            (Upgrades.Kind.Tires,      "SEMI-SLICKS, ONE WEEKEND", 3),
        };

        /// <summary>How often a slot comes up hardware rather than a service
        /// part. Under a half on both shelves that carry any, so the yard reads
        /// as a scrapyard with the odd good find in it rather than a discount
        /// speed shop.</summary>
        const float WeekHardwareChance = 0.35f;
        const float BackLotHardwareChance = 0.45f;

        // ================= grade =================

        /// <summary>
        /// Grade bands, worst first, as (floor, what the yard calls it).
        /// The word is the row's headline and the number is in the detail line:
        /// a player scanning the page should be able to sort the shelf without
        /// reading a single percentage.
        /// </summary>
        static readonly (int floor, string word)[] GradeWords =
        {
            (85, "CLEAN"), (70, "SOLID"), (55, "SERVICEABLE"),
            (40, "ROUGH"), (0, "SCRAP"),
        };

        public static string GradeWord(int grade)
        {
            foreach (var g in GradeWords) if (grade >= g.floor) return g.word;
            return GradeWords[GradeWords.Length - 1].word;
        }

        /// <summary>
        /// What a part actually restores. A perfect pull gives its rated add; a
        /// scrap one still gives most of it, because the failure mode of a bad
        /// used part is the FAULT it brings with it, not a part that does
        /// nothing. Two punishments for one bad roll is how a system stops
        /// being a gamble and starts being a tax.
        /// </summary>
        public static int EffectiveAdd(int ratedAdd, int grade) =>
            Mathf.Max(1, Mathf.RoundToInt(ratedAdd * (0.55f + 0.45f * Mathf.Clamp01(grade / 100f))));

        /// <summary>
        /// Percent chance the part seeds a hidden fault on its own lane when it
        /// goes in. Nothing at all above 55 — a clean pull is a clean pull —
        /// then it climbs to about a third at the bottom of the shelf.
        ///
        /// Rolled at INSTALL rather than at purchase, and carried on the queued
        /// job to get there (<see cref="PendingPart.junkRisk"/>). It matters:
        /// a fault that appeared the day you paid would be on the car for the
        /// days it sits waiting on you, which is a car broken by a part that is
        /// still in the boot.
        /// </summary>
        public static int FaultRisk(int grade) =>
            grade >= 55 ? 0 : Mathf.Clamp(Mathf.RoundToInt((55 - grade) * 0.6f), 0, 40);

        /// <summary>
        /// The car multiplier, CAPPED AT 1.8 where every other one in the game
        /// runs to 3.5. See the note at the top of the file: this cap is the
        /// design, not a rounding of someone else's.
        /// </summary>
        public static float CarMult(CarSpec spec)
        {
            float price = spec != null && spec.price > 0 ? spec.price : 15000f;
            return Mathf.Clamp(Mathf.Sqrt(price / 15000f), 0.7f, 1.8f);
        }

        // ================= stocking the shelves =================

        /// <summary>
        /// Drop what has gone and top each shelf back up. Called from the daily
        /// rollover beside <see cref="CarMarket.RefreshListings"/>, so the yard
        /// turns over while the player is asleep — and at three different
        /// speeds, so a week away changes the Bin completely and leaves the
        /// back lot recognisable.
        /// </summary>
        public static void RefreshStock(LifeState s)
        {
            if (s.junkyard == null) s.junkyard = new List<YardPart>();
            s.junkyard.RemoveAll(p => p == null || p.expiresDay < s.day);

            for (int sh = 0; sh < ShelfLife.Length; sh++)
            {
                var shelf = (Shelf)sh;
                // Dedupe WITHIN the shelf only. The same used radiator turning
                // up in the Bin and in the week's pulls at two grades and two
                // prices is a scrapyard; two of them on the same shelf is a
                // listing bug.
                var have = new HashSet<string>();
                int count = 0;
                foreach (var p in s.junkyard)
                    if (p.shelf == sh) { have.Add(p.label); count++; }

                int guard = 0;
                while (count < ShelfLife[sh].slots && guard++ < 40)
                {
                    var part = Roll(s, shelf);
                    if (part == null || have.Contains(part.label)) continue;
                    s.junkyard.Add(part);
                    have.Add(part.label);
                    count++;
                }
            }
        }

        static YardPart Roll(LifeState s, Shelf shelf)
        {
            int idx = (int)shelf;
            var part = new YardPart
            {
                shelf = idx,
                // Grade is flat across 25..100 rather than bell-shaped on
                // purpose: a yard where most of everything is average is a yard
                // with nothing to find. The two ends are the reason to look.
                grade = Random.Range(25, 101),
                expiresDay = s.day + ShelfLife[idx].minDays +
                             Random.Range(0, ShelfLife[idx].spreadDays + 1),
            };

            float hardwareChance = shelf == Shelf.Week ? WeekHardwareChance
                                 : shelf == Shelf.BackLot ? BackLotHardwareChance : 0f;
            if (Random.value < hardwareChance)
            {
                var table = shelf == Shelf.BackLot ? BackLotHardware : WeekHardware;
                var pick = table[Random.Range(0, table.Length)];
                part.label = pick.label;
                part.upgradeKind = Upgrades.UpgradeKindKey(pick.kind);
                part.maxStage = pick.maxStage;
                part.stat = "engine";           // the lane a bad one bites, as Upgrades does
                part.donorHint = DonorHint();
                return part;
            }

            var stock = StockFor(shelf);
            var kind = stock[Random.Range(0, stock.Length)];
            part.label = kind.label;
            part.stat = kind.stat;
            part.add = kind.add;
            part.basePrice = kind.basePrice;
            part.days = kind.days;
            part.donorHint = kind.donorHint;
            return part;
        }

        /// <summary>Flavour for a hardware pull, which has no fixed line of its
        /// own because it is quoted per car. Says where it came off, which on a
        /// used performance part is most of what you would want to know.</summary>
        static string DonorHint()
        {
            string[] hints =
            {
                "off a track car", "one season on it", "seller says lightly used",
                "boxed, no receipts", "still on the donor", "swapped out for newer",
            };
            return hints[Random.Range(0, hints.Length)];
        }

        // ================= quoting and buying =================

        /// <summary>What one row on the shelf costs and does FOR THIS CAR, and
        /// the reason it cannot be bought when it cannot.</summary>
        public struct Quote
        {
            public int price, days, skillReq, risk;
            public bool available, isUpgrade;
            /// <summary>Restored condition, for a service part.</summary>
            public int add;
            /// <summary>Stage this pull would install, for a hardware part.</summary>
            public int stage;
            public string effect, blockedReason;
        }

        public static Quote GetQuote(LifeState s, OwnedCar car, CarSpec spec, YardPart part)
        {
            var q = new Quote { available = false, risk = FaultRisk(part.grade) };
            if (car == null) { q.blockedReason = "no car"; return q; }

            if (part.IsUpgrade)
            {
                q.isUpgrade = true;
                // Asked BEFORE the plan, because NextStagePlan answers "no" the
                // same way to a maxed car and to a car it has no numbers for —
                // and the starter RX-7 is the one car in the game written by
                // hand rather than baked from the catalog, so it really has
                // none. "Already maxed" on a stock car is a lie the player
                // cannot argue with.
                if (spec == null)
                {
                    q.blockedReason = "nothing on paper for this car";
                    return q;
                }
                var kind = Upgrades.KindFromKey(part.upgradeKind);
                var plan = Upgrades.NextStagePlan(s, car, spec, kind);
                if (!plan.valid) { q.blockedReason = "already maxed"; return q; }
                if (plan.toStage > part.maxStage)
                {
                    // The pull is not good enough for where this car already is.
                    // Said plainly rather than hidden, because "your car is past
                    // this" is information — it is the row telling you that you
                    // outgrew the shelf it is on.
                    q.blockedReason = "you are past this — it is stage " + part.maxStage + " kit";
                    return q;
                }
                if (Upgrades.PendingFor(s, car, kind) != null)
                {
                    q.blockedReason = "already building";
                    return q;
                }
                q.stage = plan.toStage;
                q.days = plan.days;
                q.skillReq = plan.skillReq;
                // A third to two thirds of what the same stage costs new, by
                // grade. Fitting it is on you either way — there is no shop
                // price on a part with no warranty behind it.
                q.price = Mathf.RoundToInt(plan.diyPrice *
                                           (0.30f + 0.30f * Mathf.Clamp01(part.grade / 100f)));
                q.effect = Upgrades.StageNames[(int)kind][plan.toStage] +
                           "  ·  stage " + plan.toStage +
                           (plan.unit == "kg" ? "  ·  -" + plan.delta + " kg"
                                              : "  ·  +" + plan.delta + " " + plan.unit);
                if (s.mechSkill < plan.skillReq)
                {
                    q.blockedReason = "needs skill " + plan.skillReq;
                    return q;
                }
                q.available = true;
                return q;
            }

            q.add = EffectiveAdd(part.add, part.grade);
            q.days = part.days;
            q.price = Mathf.Max(8, Mathf.RoundToInt(
                part.basePrice * CarMult(spec) * (0.5f + 0.5f * Mathf.Clamp01(part.grade / 100f))));
            q.effect = "+" + q.add + " " + StatWord(part.stat);
            // One job per lane per car, matching the rule the mechanic bench
            // keeps: two radiators queued for the same engine is one radiator
            // and a receipt.
            if (s.pendingParts.Exists(p => p.carId == car.id && p.stat == part.stat && !p.IsUpgrade))
            {
                q.blockedReason = "a " + StatWord(part.stat) + " job is already booked";
                return q;
            }
            q.available = true;
            return q;
        }

        public static string StatWord(string stat) =>
            stat == "engine" ? "engine" : stat == "tires" ? "tyres" :
            stat == "paint" ? "paint" : "body";

        /// <summary>
        /// Buy it and take it home. Queues into the same pendingParts list the
        /// repairs and the builds use, so a yard part costs the days it costs
        /// and the car is visibly in bits while you wait — and so the ONE place
        /// that applies a queued job stays the one place.
        ///
        /// Returns null on success, or the reason it was refused.
        /// </summary>
        public static string Buy(LifeState s, OwnedCar car, CarSpec spec, YardPart part)
        {
            if (part == null) return "gone";
            if (!s.junkyard.Contains(part)) return "someone else took it";
            var q = GetQuote(s, car, spec, part);
            if (!q.available) return q.blockedReason;
            if (s.money < q.price) return "need " + MenuKit.Money(q.price);

            s.money -= q.price;
            // You fitted it, so you learned something — the same DIY gain the
            // bench pays, against the same difficulty scale. A service part is
            // graded off its own downtime for want of a skill number of its own.
            s.mechSkill = Mathf.Min(100f, s.mechSkill + FaultCatalog.DiySkillGain(
                s.mechSkill, q.isUpgrade ? q.skillReq : 10 + q.days * 8));

            s.pendingParts.Add(new PendingPart
            {
                carId = car.id,
                faultId = "",
                label = part.label,
                stat = part.stat,
                add = q.isUpgrade ? 0 : q.add,
                readyDay = s.day + q.days,
                venue = 0,                      // DIY: it is a yard, you fit it
                upgradeKind = q.isUpgrade ? part.upgradeKind : "",
                upgradeStage = q.isUpgrade ? q.stage : 0,
                junkRisk = q.risk,
            });
            s.junkyard.Remove(part);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": bought " + part.label +
                              " at the yard (" + MenuKit.Money(q.price) + ", " +
                              GradeWord(part.grade).ToLowerInvariant() + ", " + q.days + "d)");
            return null;
        }

        /// <summary>The shelf, in the order the page prints it. A fresh list
        /// rather than a filter over the save, so the page cannot reorder the
        /// state it is drawing.</summary>
        public static List<YardPart> OnShelf(LifeState s, Shelf shelf)
        {
            var rows = new List<YardPart>();
            if (s.junkyard == null) return rows;
            foreach (var p in s.junkyard) if (p != null && p.shelf == (int)shelf) rows.Add(p);
            // Best first: a scrapyard page sorted by nothing is a scrapyard.
            rows.Sort((a, b) => b.grade.CompareTo(a.grade));
            return rows;
        }
    }
}
