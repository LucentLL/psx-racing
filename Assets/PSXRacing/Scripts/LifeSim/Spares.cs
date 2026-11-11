using System;
using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// One spare part on the garage shelf: what it is, how good, where it came
    /// from and what it cost. The same two shapes a <see cref="YardPart"/> has
    /// - a SERVICE part (a lane and a rated add) or HARDWARE (an upgrade
    /// category and the highest stage it serves) - plus the provenance a part
    /// you own has to carry: the donor, the day, the price.
    /// </summary>
    [Serializable]
    public class SparePart
    {
        public string id = "";
        public string label = "";
        /// <summary>0-100, the yard's grade, kept as pulled.</summary>
        public int grade = 60;

        // ---- service part ----
        public string stat = "engine";
        public int add;
        public int basePrice;
        public int days = 1;

        // ---- hardware ----
        public string upgradeKind = "";
        public int maxStage;

        // ---- provenance ----
        /// <summary>Catalog id of the wreck it came off, or empty (the rack).</summary>
        public string donorSpec = "";
        /// <summary>"HONDA CIVIC SIR-II (EG) `93", or "THE YARD'S RACK".</summary>
        public string donorName = "";
        public string donorHint = "";
        /// <summary>What the part itself cost, tool rental left out. The scrap
        /// price is a share of this.</summary>
        public int paid;
        public int dayPulled;

        public bool IsUpgrade => !string.IsNullOrEmpty(upgradeKind);
        public bool IsTurbo => upgradeKind == "power" && label != null && label.Contains("TURBO");
    }

    /// <summary>
    /// THE PARTS SHELF (v24). Owner, 2026-11-11, at a wreck refusing four pulls
    /// with "already maxed": "What if I want a spare part for when the Civic
    /// part needs to be replaced? What if I have multiple Civics? What if I
    /// don't have a Civic right now, but I plan to buy one and want to store
    /// parts in preparation?"
    ///
    /// Before this a yard part did not exist as a thing: it was a discount
    /// route to the next stage of the car you were standing in, so a car that
    /// was already built refused the pull. Now a pull can go on the shelf
    /// whatever you drive, and comes off it three ways:
    ///   UPGRADE  - the next stage of a category, when a car reaches the stage
    ///              the part serves (a car bought later included);
    ///   REPLACE  - a known fault the part covers (<see cref="Covers"/>), on a
    ///              car whose stage in that category the part is not below;
    ///   RESTORE  - a service part with no fault to clear puts its lane back
    ///              up, exactly as fitting it at the yard always did.
    /// On WHICH car is chosen when it is fitted. DIY at home (skill-gated, no
    /// charge: the part is paid for) or at the mechanic (labour only).
    ///
    /// FITMENT is unchanged from the yard's: nothing about a used part is
    /// matched to the make or model of the car it goes on. Hardware serves any
    /// car whose next stage is no higher than its own; a service part goes on
    /// any car. The donor is recorded and shown, not enforced.
    ///
    /// Grade keeps the yard's meaning: a rough part restores less
    /// (<see cref="Junkyard.EffectiveAdd"/>) and may seed a hidden fault when
    /// it goes in (<see cref="Junkyard.FaultRisk"/>, rolled at install).
    /// </summary>
    public static class Spares
    {
        /// <summary>
        /// How many parts the garage shelf holds. A home garage has a wall of
        /// racking, not a warehouse; twenty is four wrecks stripped bare, which
        /// is more than any plan needs, and a cap keeps a long career's save and
        /// this page from growing without end. The scrap sale is how room is
        /// made, so a full shelf is never a dead end.
        /// </summary>
        public const int Capacity = 20;

        /// <summary>What the yard pays to take a spare back: this share of
        /// what the part cost you, tool rental excluded.</summary>
        public const float ScrapShare = 0.40f;

        public enum Use { Auto = 0, Upgrade = 1, Replace = 2, Restore = 3 }

        /// <summary>What fitting one spare to one car would do, or why not.</summary>
        public struct FitPlan
        {
            public bool ok;
            public Use use;
            /// <summary>"STAGE 3 SEMI-SLICKS", "REPLACES UNEVEN TIRE WEAR", "+31 TYRES".</summary>
            public string what;
            public string reason;
            public int days, skillReq, stage, add, labour;
            public string faultId, stat;
        }

        // ================= what a part covers =================

        /// <summary>Faults a HARDWARE category's parts can clear. A kind's
        /// parts replace the worn version of themselves: rotors and pads for
        /// brakes, struts, arms and bushings for suspension, the manifold and
        /// pipework for a power pull, panels and trim for weight.</summary>
        static readonly Dictionary<string, string[]> KindFaults = new Dictionary<string, string[]>
        {
            { "power",      new[] { "intake_manifold", "exhaust_rust", "exhaust_rot" } },
            { "weight",     new[] { "panel_rust", "bumper_crack", "bumper_dent", "trim_rattle" } },
            { "brakes",     new[] { "rotor_warp", "sport_brake_wear" } },
            { "suspension", new[] { "strut_bushings", "strut_wear", "control_arm_bush", "control_arm_rust",
                                    "ball_joint", "bushing_clunk", "air_susp_leak" } },
            { "tires",      new[] { "tire_wear" } },
            { "seat",       new[] { "trim_rattle" } },
        };

        /// <summary>Faults each SERVICE part on the yard's lists can clear,
        /// by its label (the yard's own words for what it is).</summary>
        static readonly Dictionary<string, string[]> LabelFaults = new Dictionary<string, string[]>
        {
            { "USED PLUG SET",          new[] { "spark_plugs" } },
            { "AIR FILTER + HOSES",     new[] { "carbon_buildup" } },
            { "USED ALTERNATOR",        new[] { "alternator" } },
            { "COIL + LEADS",           new[] { "spark_plugs", "electrical_sensor", "cam_sensor" } },
            { "PART-WORN PAIR",         new[] { "tire_wear" } },
            { "SINGLE STEEL WHEEL",     new[] { "tire_wear" } },
            { "MATCHED SET + TYRES",    new[] { "tire_wear" } },
            { "RATTLE CAN + PRIMER",    new[] { "minor_rust" } },
            { "TAIL LIGHT + TRIM",      new[] { "trim_rattle" } },
            { "USED RADIATOR",          new[] { "cooling_fail" } },
            { "STARTER + BATTERY",      new[] { "battery_drain" } },
            { "USED CLUTCH + FLYWHEEL", new[] { "trans_slip" } },
            { "EXHAUST, CAT BACK",      new[] { "exhaust_rust", "exhaust_rot", "o2_sensor" } },
            { "STRAIGHT FENDER",        new[] { "panel_rust", "minor_rust" } },
            { "DOOR SKIN + GLASS",      new[] { "panel_rust" } },
            { "BUMPER, UNCRACKED",      new[] { "bumper_crack", "bumper_dent" } },
            { "PANEL SET, RESPRAYED",   new[] { "paint_fade", "paint_bubble", "minor_rust" } },
            { "DONOR ENGINE, RUNNING",  new[] { "timing_chain", "timing_belt", "valve_cover_gasket", "oil_leak",
                                                "oil_pan_gasket", "carbon_buildup", "intake_manifold", "cam_sensor" } },
            { "GEARBOX, LOW MILES",     new[] { "trans_slip", "trans_hesitation" } },
            { "FRONT CLIP",             new[] { "bumper_crack", "bumper_dent", "ball_joint" } },
            { "SHELL PANELS, WHOLE CAR",new[] { "panel_rust", "paint_fade", "paint_bubble", "minor_rust" } },
            { "REAR SUBFRAME + DIFF",   new[] { "frame_rust", "control_arm_rust", "bushing_clunk" } },
        };

        static readonly string[] None = new string[0];

        public static string[] FaultsCovered(SparePart p)
        {
            if (p == null) return None;
            if (p.IsUpgrade) return KindFaults.TryGetValue(p.upgradeKind, out var k) ? k : None;
            return LabelFaults.TryGetValue(p.label ?? "", out var l) ? l : None;
        }

        public static bool Covers(SparePart p, string faultId) =>
            !string.IsNullOrEmpty(faultId) && Array.IndexOf(FaultsCovered(p), faultId) >= 0;

        static bool Booked(LifeState s, OwnedCar car, string faultId) =>
            s.pendingParts.Exists(j => j != null && j.carId == car.id && j.faultId == faultId);

        // ================= what a part is worth =================

        /// <summary>Restoration a service part gives at its grade.</summary>
        public static int EffectiveAdd(SparePart p) => Junkyard.EffectiveAdd(p.add, p.grade);

        /// <summary>The scrap price: <see cref="ScrapShare"/> of what it cost.</summary>
        public static int ScrapValue(SparePart p) =>
            p == null ? 0 : Mathf.Max(1, Mathf.RoundToInt(p.paid * ScrapShare));

        // ================= on the shelf =================

        public static bool Full(LifeState s) => s.partsShelf != null && s.partsShelf.Count >= Capacity;

        /// <summary>Put a yard part on the shelf. Null when there is no room.</summary>
        public static SparePart Shelve(LifeState s, YardPart part, string donorSpec, string donorName, int paid)
        {
            if (s == null || part == null) return null;
            if (s.partsShelf == null) s.partsShelf = new List<SparePart>();
            if (Full(s)) return null;
            s.spareSeq++;
            var sp = new SparePart
            {
                id = "sp" + s.spareSeq,
                label = part.label,
                grade = part.grade,
                stat = part.stat,
                add = part.add,
                basePrice = part.basePrice,
                days = Mathf.Max(1, part.days),
                upgradeKind = part.upgradeKind ?? "",
                maxStage = part.maxStage,
                donorSpec = donorSpec ?? "",
                donorName = string.IsNullOrEmpty(donorName) ? "THE YARD'S RACK" : donorName,
                donorHint = part.donorHint,
                paid = Mathf.Max(0, paid),
                dayPulled = s.day,
            };
            s.partsShelf.Add(sp);
            return sp;
        }

        public static SparePart Find(LifeState s, string id) =>
            s == null || s.partsShelf == null ? null : s.partsShelf.Find(p => p != null && p.id == id);

        /// <summary>Sell a spare back to the yard for scrap. Returns the money.</summary>
        public static int SellForScrap(LifeState s, SparePart p)
        {
            if (s == null || p == null || s.partsShelf == null || !s.partsShelf.Remove(p)) return 0;
            int v = ScrapValue(p);
            s.money += v;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": sold a spare " + p.label + " for scrap (" +
                              MenuKit.Money(v) + ")");
            return v;
        }

        // ================= naming =================

        /// <summary>"TURBO + MANIFOLD, USED" with what it is FOR.</summary>
        public static string KindWord(SparePart p) =>
            p.IsUpgrade ? Upgrades.KindLabels[(int)Upgrades.KindFromKey(p.upgradeKind)]
                        : Junkyard.StatWord(p.stat).ToUpperInvariant();

        /// <summary>The fitment rule for this part, in words. Hardware: any car
        /// whose next stage is no higher than the part's. Service: any car.</summary>
        public static string FitsLine(SparePart p) =>
            p.IsUpgrade
                ? "FITS ANY CAR UP TO STAGE " + p.maxStage + " " + KindWord(p) +
                  (p.IsTurbo ? " (TURBO OR UNBUILT NA)" : "")
                : "FITS ANY CAR  ·  +" + EffectiveAdd(p) + " " + KindWord(p);

        // ================= fitting =================

        /// <summary>
        /// What this spare would do on this car, and whether it can go on now.
        /// <paramref name="gates"/> false asks only "does it FIT this car" -
        /// skill, money and where the car is are left out - which is what the
        /// shelf's "could take it now" list and a car page's "you have this on
        /// the shelf" want.
        /// </summary>
        public static FitPlan Plan(LifeState s, OwnedCar car, SparePart p, bool mechanic,
                                   Use want = Use.Auto, string faultId = null, bool gates = true)
        {
            var f = new FitPlan();
            if (s == null || p == null) { f.reason = "gone"; return f; }
            if (car == null) { f.reason = "no car"; return f; }
            if (gates)
            {
                string where = CarWhere.RefuseWork(s, car, mechanic ? CarWhere.VenueMechanic : CarWhere.VenueDiy);
                if (where != null) { f.reason = where; return f; }
            }
            if (car.noWheels && (p.stat == "tires" || p.upgradeKind == "tires"))
            { f.reason = "no wheels on it"; return f; }

            var spec = CarCatalog.Get(car.specId);
            var kind = p.IsUpgrade ? Upgrades.KindFromKey(p.upgradeKind) : Upgrades.Kind.Power;

            // ---- REPLACE: a known fault this part covers ----
            if (want != Use.Upgrade && want != Use.Restore)
            {
                CarFault fault = null;
                foreach (var x in car.faults)
                {
                    if (x == null || x.hidden || !Covers(p, x.id)) continue;
                    if (faultId != null && x.id != faultId) continue;
                    if (Booked(s, car, x.id)) { if (faultId != null) { f.reason = "already booked"; return f; } continue; }
                    fault = x;
                    break;
                }
                if (fault != null)
                {
                    int have = p.IsUpgrade ? Upgrades.GetStage(car, kind) : 0;
                    if (p.IsUpgrade && p.maxStage < have)
                    {
                        if (want == Use.Replace)
                        {
                            f.reason = "this car runs stage " + have + " - this is stage " + p.maxStage + " kit";
                            return f;
                        }
                    }
                    else
                    {
                        f.use = Use.Replace;
                        f.faultId = fault.id;
                        f.stat = fault.stat;
                        f.what = "REPLACES " + (fault.label ?? fault.id).ToUpperInvariant();
                        if (p.IsUpgrade)
                        {
                            f.add = Junkyard.EffectiveAdd(Mathf.Max(1, fault.add), p.grade);
                            f.days = 2;
                            f.skillReq = Upgrades.FitSkill(kind, Mathf.Max(1, have), spec);
                            f.labour = Mathf.RoundToInt(0.6f * Upgrades.RefDiyPrice(kind, Mathf.Max(1, have)) *
                                                        Upgrades.CarCostMult(spec));
                        }
                        else
                        {
                            f.add = Mathf.Max(EffectiveAdd(p), Junkyard.EffectiveAdd(Mathf.Max(1, fault.add), p.grade));
                            f.days = Mathf.Max(1, p.days);
                            f.skillReq = Mathf.Max(0, (f.days - 1) * 15);
                            f.labour = Mathf.RoundToInt(0.6f * p.basePrice * Junkyard.CarMult(spec));
                        }
                        return Gate(s, car, f, mechanic, gates);
                    }
                }
                else if (want == Use.Replace)
                {
                    f.reason = faultId != null ? "it does not fix that" : "nothing on this car it replaces";
                    return f;
                }
            }

            // ---- UPGRADE: hardware, the next stage ----
            if (p.IsUpgrade)
            {
                if (want == Use.Restore) { f.reason = "it is hardware"; return f; }
                if (spec == null) { f.reason = "nothing on paper for this car"; return f; }
                string race = Upgrades.RaceCarRefuses(spec, kind);
                if (race != null) { f.reason = race.ToLowerInvariant(); return f; }
                var plan = Upgrades.NextStagePlan(s, car, spec, kind);
                if (p.IsTurbo && !spec.OnTurboPath(car.turbo))
                {
                    string no = Upgrades.TurboKitRefuses(s, car, spec);
                    if (no != null)
                    {
                        f.reason = spec.IsSupercharged || spec.IsBuiltToTune ? no.ToLowerInvariant()
                                 : "turbo hardware - this engine is an NA build";
                        return f;
                    }
                    plan = Upgrades.TurboKitPlan(s, car, spec);
                }
                if (!plan.valid) { f.reason = "fully built - nothing to step up"; return f; }
                if (plan.toStage > p.maxStage)
                {
                    f.reason = "car is at stage " + plan.fromStage + " - this is stage " + p.maxStage + " kit";
                    return f;
                }
                if (Upgrades.PendingFor(s, car, kind) != null) { f.reason = "already building"; return f; }
                f.use = Use.Upgrade;
                f.stage = plan.toStage;
                f.stat = "engine";
                f.what = "STAGE " + plan.toStage + " " + plan.stageName;
                f.days = plan.days;
                f.skillReq = plan.skillReq;
                f.labour = plan.shopPrice - plan.diyPrice;
                return Gate(s, car, f, mechanic, gates);
            }

            // ---- RESTORE: a service part, its lane ----
            if (want == Use.Upgrade) { f.reason = "it is not an upgrade"; return f; }
            if (s.pendingParts.Exists(j => j != null && j.carId == car.id && j.stat == p.stat &&
                                           !j.IsUpgrade && string.IsNullOrEmpty(j.faultId)))
            {
                f.reason = "a " + Junkyard.StatWord(p.stat) + " job is already booked";
                return f;
            }
            f.use = Use.Restore;
            f.stat = p.stat;
            f.add = EffectiveAdd(p);
            f.what = "+" + f.add + " " + Junkyard.StatWord(p.stat).ToUpperInvariant();
            f.days = Mathf.Max(1, p.days);
            f.skillReq = Mathf.Max(0, (f.days - 1) * 15);
            f.labour = Mathf.RoundToInt(0.6f * p.basePrice * Junkyard.CarMult(spec));
            return Gate(s, car, f, mechanic, gates);
        }

        /// <summary>The last two gates: who is fitting it (skill at home,
        /// labour at the mechanic), and whether the player can pay.</summary>
        static FitPlan Gate(LifeState s, OwnedCar car, FitPlan f, bool mechanic, bool gates)
        {
            f.labour = mechanic ? Clubs.MechanicPrice(s, car, Mathf.Max(30, f.labour)) : 0;
            if (gates)
            {
                if (!mechanic && s.mechSkill < f.skillReq)
                {
                    f.reason = "needs skill " + f.skillReq + " - yours is " + Mathf.FloorToInt(s.mechSkill);
                    return f;
                }
                if (mechanic && s.money < f.labour)
                {
                    f.reason = "labour is " + MenuKit.Money(f.labour);
                    return f;
                }
            }
            f.ok = true;
            return f;
        }

        /// <summary>
        /// Fit a spare. Queues it into pendingParts like every other job (so
        /// it costs its days and the ONE place that applies a job stays the
        /// one place), carrying the grade's fault risk, and takes it off the
        /// shelf. DIY costs nothing but the days; the mechanic charges labour
        /// and keeps the car. Returns null on success, or the reason.
        /// </summary>
        public static string Fit(LifeState s, OwnedCar car, SparePart p, bool mechanic,
                                 Use want = Use.Auto, string faultId = null)
        {
            if (s == null || p == null || s.partsShelf == null || !s.partsShelf.Contains(p)) return "not on the shelf";
            var f = Plan(s, car, p, mechanic, want, faultId);
            if (!f.ok) return f.reason;

            if (mechanic) s.money -= f.labour;
            else s.mechSkill = Mathf.Min(100f, s.mechSkill + FaultCatalog.DiySkillGain(s.mechSkill,
                                         Mathf.Max(f.skillReq, 10 + f.days * 8)));

            s.pendingParts.Add(new PendingPart
            {
                carId = car.id,
                faultId = f.use == Use.Replace ? f.faultId : "",
                label = p.label + " (spare)",
                stat = f.use == Use.Upgrade ? "engine" : f.stat,
                add = f.use == Use.Upgrade ? 0 : f.add,
                readyDay = s.day + f.days,
                venue = mechanic ? CarWhere.VenueMechanic : CarWhere.VenueDiy,
                upgradeKind = f.use == Use.Upgrade ? p.upgradeKind : "",
                upgradeStage = f.use == Use.Upgrade ? f.stage : 0,
                junkRisk = Junkyard.FaultRisk(p.grade),
                fromShelf = true,
            });
            // A used turbo onto an unbuilt NA engine starts the turbo path, as
            // fitting it at the yard always did.
            if (f.use == Use.Upgrade && p.IsTurbo) car.turbo = true;
            s.partsShelf.Remove(p);
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": spare " + p.label + " onto " + car.displayName +
                              (mechanic ? " at the mechanic (labour " + MenuKit.Money(f.labour) + ")" : " (DIY)") +
                              ", " + f.days + "d");
            if (mechanic) LifeRules.DropOff(s, car, CarWhere.VenueMechanic);
            return null;
        }

        // ================= asking the shelf =================

        /// <summary>Your cars this spare FITS now (stage, fault, lane), gates
        /// left out: skill, money and where the car is are the fit step's.</summary>
        public static List<OwnedCar> CarsThatTakeIt(LifeState s, SparePart p)
        {
            var list = new List<OwnedCar>();
            if (s == null || p == null) return list;
            foreach (var c in s.cars)
                if (c != null && Plan(s, c, p, false, Use.Auto, null, gates: false).ok) list.Add(c);
            return list;
        }

        /// <summary>The best spare on the shelf for this car's next stage of
        /// <paramref name="kind"/>, or null.</summary>
        public static SparePart ForUpgrade(LifeState s, OwnedCar car, Upgrades.Kind kind)
        {
            if (s == null || car == null || s.partsShelf == null) return null;
            string key = Upgrades.UpgradeKindKey(kind);
            SparePart best = null;
            foreach (var p in s.partsShelf)
            {
                if (p == null || p.upgradeKind != key) continue;
                if (!Plan(s, car, p, false, Use.Upgrade, null, gates: false).ok) continue;
                if (best == null || p.grade > best.grade) best = p;
            }
            return best;
        }

        /// <summary>The best spare on the shelf that replaces this fault on
        /// this car, or null.</summary>
        public static SparePart ForFault(LifeState s, OwnedCar car, CarFault fault)
        {
            if (s == null || car == null || fault == null || s.partsShelf == null) return null;
            SparePart best = null;
            foreach (var p in s.partsShelf)
            {
                if (p == null || !Covers(p, fault.id)) continue;
                if (!Plan(s, car, p, false, Use.Replace, fault.id, gates: false).ok) continue;
                if (best == null || p.grade > best.grade) best = p;
            }
            return best;
        }
    }
}
