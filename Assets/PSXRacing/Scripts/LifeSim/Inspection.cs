using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Visual inspection: eight components, each a handful of sub-checks, each
    /// sub-check a roll against the hidden faults the car is actually carrying.
    ///
    /// Ported from Racing Game 2 (`docs/INSPECT_SPEC.md`, `sim/inspectComponents.ts`,
    /// `sim/inspectOwnCar.ts`). The load-bearing idea is that a used car has
    /// problems the seller did not mention, they are ALREADY affecting how it
    /// drives, and finding them is a skill-and-tools exercise rather than a
    /// button that lists them. That is why the fault model needed nothing new:
    /// CarFault has carried a `hidden` flag since the port landed and nothing
    /// ever set it.
    ///
    /// Two rules keep this honest and are worth stating:
    ///
    /// 1. **A hidden fault still afflicts the car.** It is not a surprise bill
    ///    waiting to happen, it is why the car feels down on power — otherwise
    ///    inspecting would only ever cost you money and no one would do it.
    ///    (<see cref="FaultCatalog.Aggregate_"/> counts hidden faults for this
    ///    reason; only the LISTING is gated on detection.)
    /// 2. **Checking something and finding nothing is the fiction working**,
    ///    not a wasted tap. Most subs here reveal nothing at all — they exist so
    ///    that a clean car reads as clean rather than as unimplemented.
    /// </summary>
    public static class Inspection
    {
        public enum Comp { Engine, Transmission, Driveline, Cooling, Steering, Suspension, Wheels, Body }

        public static readonly Comp[] Order =
        {
            Comp.Engine, Comp.Transmission, Comp.Driveline, Comp.Cooling,
            Comp.Steering, Comp.Suspension, Comp.Wheels, Comp.Body,
        };

        public static string Name(Comp c)
        {
            switch (c)
            {
                case Comp.Engine: return "ENGINE";
                case Comp.Transmission: return "TRANSMISSION";
                case Comp.Driveline: return "DRIVELINE";
                case Comp.Cooling: return "COOLING";
                case Comp.Steering: return "STEERING";
                case Comp.Suspension: return "SUSPENSION";
                case Comp.Wheels: return "WHEELS & BRAKES";
                default: return "BODY";
            }
        }

        public class Sub
        {
            public string key;
            public string label;
            /// <summary>Hidden-fault ids this check can surface. EMPTY is a
            /// deliberate, common case — see the class note.</summary>
            public string[] ids = new string[0];
            /// <summary>Needs the car off the ground. A jack gets you a look
            /// and a penalty; a lift gets you a bonus.</summary>
            public bool underside;
            /// <summary>Refuses outright without a lift. Only the frame rails.</summary>
            public bool liftOnly;
            /// <summary>Inside the engine — the borescope earns its money here.</summary>
            public bool scope;
            /// <summary>Wheel has to come off. Without an impact wrench (or a
            /// lift) the roll is capped at a token chance.</summary>
            public bool wheelOff;
            public string found = "";
            public string clean = "";
        }

        // The sub-component map, from INSPECT_SPEC.md section 3. Every fault id
        // the pools can roll has a home here; ids are shared where a real
        // mechanic would find the same thing two ways (a tired timing belt
        // shows at the timing cover AND at the water pump).
        static readonly Dictionary<Comp, Sub[]> Subs = new Dictionary<Comp, Sub[]>
        {
            [Comp.Engine] = new[]
            {
                new Sub { key = "plugs", label = "SPARK PLUGS", ids = new[] { "spark_plugs" },
                    found = "Spark plugs are showing their age — these need replacing.",
                    clean = "Plugs look healthy, no oil on the threads." },
                new Sub { key = "headgasket", label = "HEAD GASKET", scope = true,
                    clean = "No seepage at the head mating surface. Looks sound." },
                new Sub { key = "throttle", label = "THROTTLE BODY", scope = true,
                    clean = "Throttle plate is a little sooty but moves freely." },
                new Sub { key = "intake", label = "INTAKE MANIFOLD", scope = true,
                    ids = new[] { "intake_manifold", "carbon_buildup" },
                    found = "The intake shows real problems — get this seen to.",
                    clean = "Intake looks okay from the outside." },
                new Sub { key = "timing", label = "TIMING COVER", scope = true,
                    ids = new[] { "timing_belt", "timing_chain" },
                    found = "The timing gear is past due — get it done before it lets go.",
                    clean = "Belt and tensioner look serviceable." },
                new Sub { key = "valvecover", label = "VALVE COVER", ids = new[] { "valve_cover_gasket" },
                    found = "Oil seep along the valve cover gasket — needs a reseal.",
                    clean = "Cover is clean and dry." },
                new Sub { key = "oilpan", label = "OIL PAN", underside = true,
                    ids = new[] { "oil_leak", "oil_pan_gasket" },
                    found = "Oil weeping around the pan — found the leak.",
                    clean = "Pan is dry, as far as you can see from under the jack." },
                new Sub { key = "sensors", label = "SENSORS & WIRING",
                    ids = new[] { "o2_sensor", "cam_sensor", "electrical_sensor" },
                    found = "A sensor connector crumbles in your fingers.",
                    clean = "Wiring looks intact from here." },
                new Sub { key = "battery", label = "BATTERY & ALTERNATOR",
                    ids = new[] { "alternator", "battery_drain" },
                    found = "The charging system is on its way out.",
                    clean = "Battery terminals are clean; the belt spins the alternator fine." },
            },
            [Comp.Transmission] = new[]
            {
                new Sub { key = "transpan", label = "FLUID & PAN", underside = true,
                    ids = new[] { "trans_hesitation", "trans_slip" },
                    found = "The transmission needs real work.",
                    clean = "Fluid level looks right from the dipstick." },
                new Sub { key = "clutch", label = "CLUTCH & LINKAGE",
                    clean = "Linkage moves cleanly through the gates." },
                new Sub { key = "mounts", label = "MOUNTS",
                    clean = "Mounts show normal cracking, nothing loose." },
            },
            [Comp.Driveline] = new[]
            {
                new Sub { key = "propshaft", label = "PROP SHAFT & U-JOINTS",
                    clean = "No play in the U-joints." },
                new Sub { key = "diff", label = "DIFFERENTIAL", underside = true,
                    clean = "Diff housing is dry, no whine on the last drive." },
                new Sub { key = "cvboots", label = "CV BOOTS", underside = true,
                    clean = "Boots are intact, no grease sling." },
            },
            [Comp.Cooling] = new[]
            {
                new Sub { key = "radcore", label = "RADIATOR CORE", ids = new[] { "cooling_fail" },
                    found = "The cooling system is failing — crusted fins and dried coolant.",
                    clean = "Core fins are straight, no crust." },
                new Sub { key = "hoses", label = "HOSES & CLAMPS", ids = new[] { "cooling_fail" },
                    found = "A hose is swollen and soft — cooling trouble.",
                    clean = "Hoses feel firm, clamps tight." },
                new Sub { key = "waterpump", label = "WATER PUMP", ids = new[] { "timing_belt" },
                    found = "Weep hole shows deposits — the pump (and belt) are due.",
                    clean = "No weeping at the pump." },
                new Sub { key = "overflow", label = "OVERFLOW TANK",
                    clean = "Coolant sits at the line, the right colour." },
            },
            [Comp.Steering] = new[]
            {
                new Sub { key = "tierods", label = "TIE ROD ENDS",
                    clean = "No play in the tie rod ends." },
                new Sub { key = "pslines", label = "PS PUMP & LINES", underside = true,
                    ids = new[] { "ps_leak" },
                    found = "Power steering fluid tracks down the lines.",
                    clean = "Lines are damp with road film but nothing is leaking." },
                new Sub { key = "rackboots", label = "RACK BOOTS", ids = new[] { "alignment" },
                    found = "The rack has been knocked out of true — it will pull.",
                    clean = "Rack boots are intact." },
            },
            [Comp.Suspension] = new[]
            {
                new Sub { key = "struts", label = "STRUTS & SHOCKS", underside = true,
                    ids = new[] { "strut_wear", "strut_bushings" },
                    found = "A strut is leaking oil down its body.",
                    clean = "Struts look dry from this angle." },
                new Sub { key = "controlarms", label = "CONTROL ARMS", underside = true,
                    ids = new[] { "control_arm_bush", "control_arm_rust", "bushing_clunk" },
                    found = "A bushing is cracked through.",
                    clean = "Bushings look whole from here." },
                new Sub { key = "balljoints", label = "BALL JOINTS", underside = true,
                    ids = new[] { "ball_joint" },
                    found = "A ball joint boot is torn open.",
                    clean = "Joints feel tight with the wheel rocked." },
                new Sub { key = "springs", label = "SPRINGS & AIR BAGS", underside = true,
                    ids = new[] { "air_susp_leak" },
                    found = "The air suspension is losing pressure somewhere.",
                    clean = "Springs sit even side to side." },
                new Sub { key = "endlinks", label = "SWAY BAR END LINKS", underside = true,
                    clean = "End links are snug." },
            },
            [Comp.Wheels] = new[]
            {
                new Sub { key = "tires", label = "TIRES", ids = new[] { "tire_wear" },
                    found = "The tyres are worn to the bars — replace them.",
                    clean = "Tread depth looks fine all round." },
                new Sub { key = "pads", label = "BRAKE PADS", wheelOff = true,
                    ids = new[] { "sport_brake_wear" },
                    found = "Pads are down to the backing plates.",
                    clean = "Pad material looks adequate through the spokes." },
                new Sub { key = "rotors", label = "ROTORS", wheelOff = true,
                    ids = new[] { "rotor_warp" },
                    found = "Rotors are scored and lipped.",
                    clean = "Rotor faces look smooth." },
                new Sub { key = "bearings", label = "WHEEL BEARINGS",
                    clean = "No growl or play at the bearings." },
            },
            [Comp.Body] = new[]
            {
                new Sub { key = "paint", label = "PAINT & TRIM",
                    ids = new[] { "paint_fade", "paint_bubble", "minor_rust" },
                    found = "The finish is going — bubbling and surface rust.",
                    clean = "Paint holds up under a close look." },
                new Sub { key = "panels", label = "PANELS & BUMPERS",
                    ids = new[] { "panel_rust", "bumper_crack", "bumper_dent" },
                    found = "Panel damage and rot you missed before.",
                    clean = "Panels line up, no filler rings when you knock." },
                new Sub { key = "framerails", label = "FRAME RAILS", liftOnly = true, underside = true,
                    ids = new[] { "frame_rust" },
                    found = "The frame rails are rotten — structural.",
                    clean = "Rails look solid where you can reach." },
                new Sub { key = "exhaust", label = "EXHAUST", underside = true,
                    ids = new[] { "exhaust_rust", "exhaust_rot" },
                    found = "The exhaust is rotting through.",
                    clean = "System is surface-rusty but solid when you tap it." },
                new Sub { key = "interior", label = "INTERIOR & ELECTRONICS",
                    ids = new[] { "trim_rattle", "display_failure", "electrical_gremlin" },
                    found = "Something electrical is wrong in here.",
                    clean = "Switchgear all works; trim is tight." },
            },
        };

        public static Sub[] SubsOf(Comp c) =>
            Subs.TryGetValue(c, out var v) ? v : new Sub[0];

        // ------------------------------------------------------------------
        //  Session state
        // ------------------------------------------------------------------
        /// <summary>
        /// Open an inspection on a car. Costs an activity slot, the way the gym
        /// does in RG2 — without a cost, inspecting the whole fleet every
        /// morning is strictly correct play and the tools stop mattering.
        /// Re-entering the same car on the same day is free and keeps whatever
        /// was already checked, so a mis-tap does not burn a slot.
        /// </summary>
        public static void Enter(LifeState s, OwnedCar car)
        {
            if (car == null) return;
            if (car.inspectDay == s.day) return;      // already open today
            // Slot FIRST, then stamp. Spending the last slot of a day rolls the
            // calendar over, and stamping before that writes YESTERDAY onto the
            // car — so the inspection the player just paid for would read as
            // not open and charge them a second slot to get into it.
            LifeRules.SpendActivitySlot(s);
            car.inspectDay = s.day;
            car.inspectedSubs.Clear();
        }

        public static bool OpenToday(LifeState s, OwnedCar car) =>
            car != null && car.inspectDay == s.day;

        static string LatchKey(Comp c, Sub sub) => (int)c + ":" + sub.key;

        public static bool AlreadyChecked(OwnedCar car, Comp c, Sub sub) =>
            car != null && car.inspectedSubs.Contains(LatchKey(c, sub));

        /// <summary>Every check on this component already done.</summary>
        public static bool ComponentDone(LifeState s, OwnedCar car, Comp c)
        {
            var acc = Toolbox.AccessFor(s, car);
            foreach (var sub in SubsOf(c))
            {
                if (!Reachable(sub, acc)) continue;
                if (!AlreadyChecked(car, c, sub)) return false;
            }
            return true;
        }

        /// <summary>
        /// Can this check be made at all, standing where the car is standing?
        ///
        /// The underside rule is the one that changed when the garage got jack
        /// stands: it used to be enough to OWN a jack, which meant the sentence
        /// "hard to get a good view underneath" was flavour text over a check
        /// that ran anyway. Now the car has to actually be in the air. Nothing
        /// about a car on its wheels is different from before — the free floor
        /// check still finds puddles, and that is deliberately what a car on the
        /// ground is worth.
        /// </summary>
        public static bool Reachable(Sub sub, Toolbox.Access acc)
        {
            if (sub.liftOnly) return acc.raise == Toolbox.Raise.Lift;
            if (sub.underside) return acc.raise != Toolbox.Raise.Ground;
            return true;
        }

        /// <summary>
        /// Whether the player can actually get a wheel off. Nobody pulls a
        /// wheel with the car sitting on it, whatever is hanging on the tool
        /// board — so this is the raise AND the wrench, or the lift, which
        /// carries its own. Below that you are looking through the spokes, and
        /// the roll is capped to say so rather than refused.
        /// </summary>
        public static bool WheelsComeOff(Toolbox.Access acc) =>
            acc.raise == Toolbox.Raise.Lift || (acc.impact && acc.raise != Toolbox.Raise.Ground);

        /// <summary>Why a check is greyed out, in words the player can act on.
        /// Every refusal in the game names the thing that would fix it.</summary>
        public static string RefusalFor(Sub sub, Toolbox.Access acc)
        {
            if (sub.liftOnly && acc.raise != Toolbox.Raise.Lift)
                return acc.lift
                    ? "You cannot get at the rails from under a jack. Put it on the lift."
                    : "You cannot get at the rails on a jack. That needs the lift.";
            if (sub.underside && acc.raise == Toolbox.Raise.Ground)
                return "Nothing to see under a car sitting on its wheels. Get it up on stands first.";
            return null;
        }

        // ------------------------------------------------------------------
        //  The roll
        // ------------------------------------------------------------------
        public class Result
        {
            public string line;
            /// <summary>Faults this check turned up, by label.</summary>
            public readonly List<string> revealed = new List<string>();
            public bool refused;
        }

        /// <summary>
        /// Run one sub-check. Latches per car per day: a failed roll reads
        /// "nothing obvious" until tomorrow rather than letting the player tap
        /// the same button until the dice cooperate.
        /// </summary>
        public static Result Check(LifeState s, OwnedCar car, Comp c, Sub sub)
        {
            var r = new Result();
            var acc = Toolbox.AccessFor(s, car);

            if (!Reachable(sub, acc))
            {
                r.refused = true;
                r.line = RefusalFor(sub, acc);
                return r;
            }

            if (AlreadyChecked(car, c, sub))
            {
                r.line = "Already looked at that today.";
                return r;
            }
            car.inspectedSubs.Add(LatchKey(c, sub));

            float p = FindChance(s, sub, acc);
            foreach (var f in car.faults)
            {
                if (!f.hidden) continue;
                if (System.Array.IndexOf(sub.ids, f.id) < 0) continue;
                if (Random.value > p) continue;
                f.hidden = false;
                f.diagnosed = true;
                r.revealed.Add(f.label);
                // Finding things is how a mechanic learns. Small, so it is a
                // side effect of playing rather than a grind target.
                s.mechSkill = Mathf.Min(100f, s.mechSkill + 0.6f);
            }

            r.line = r.revealed.Count > 0
                ? (string.IsNullOrEmpty(sub.found) ? "Found a problem." : sub.found)
                : sub.clean;
            return r;
        }

        /// <summary>
        /// p = base + skill + tools + access, clamped. Straight from the spec's
        /// section 4; the only deviation is that this port has ONE mechanic
        /// skill rather than six per-category ones, so every check reads the
        /// same number.
        /// </summary>
        public static float FindChance(LifeState s, Sub sub, Toolbox.Access acc)
        {
            float p = 0.5f;
            p += Mathf.Clamp(s.mechSkill, 0f, 100f) * 0.003f;    // +0.00 .. +0.30
            if (acc.lamp) p += 0.05f;
            if (sub.scope && acc.scope) p += 0.15f;
            // On the lift you are standing under it; on stands you are lying on
            // your back with a torch. Those are the two numbers the spec had,
            // and they now key off where the car IS rather than off what is
            // hanging on the tool board.
            if (sub.underside) p += acc.raise == Toolbox.Raise.Lift ? 0.15f : -0.10f;
            p = Mathf.Clamp(p, 0.05f, 0.95f);
            // A wheel that never came off caps what you can honestly claim to
            // have seen, whatever the rest of the maths says.
            if (sub.wheelOff && !WheelsComeOff(acc)) p = Mathf.Min(p, 0.15f);
            return p;
        }

        /// <summary>
        /// The free look under the car on opening a component — the user's own
        /// "no leaks are seen on the garage floor" line. Flat 25%, leaks only,
        /// and it is what makes a jack-only inspection worth doing at all.
        /// </summary>
        static readonly string[] LeakIds = { "oil_leak", "oil_pan_gasket", "ps_leak", "air_susp_leak" };

        public static string FloorCheck(LifeState s, OwnedCar car)
        {
            if (car == null || car.floorCheckedDay == s.day) return null;
            car.floorCheckedDay = s.day;
            foreach (var f in car.faults)
            {
                if (!f.hidden || System.Array.IndexOf(LeakIds, f.id) < 0) continue;
                if (Random.value > 0.25f) continue;
                f.hidden = false;
                f.diagnosed = true;
                return "There is a fresh puddle under the car. " + f.label + ".";
            }
            return "No leaks are seen on the garage floor.";
        }

        /// <summary>The access sentence a component opens with — the player
        /// should be told what they CAN'T see before they start tapping.</summary>
        public static string AccessLine(LifeState s, OwnedCar car, Comp c)
        {
            var acc = Toolbox.AccessFor(s, car);
            bool anyUnder = false, anyWheel = false, anyRails = false, anyScope = false;
            foreach (var sub in SubsOf(c))
            {
                if (sub.underside) anyUnder = true;
                if (sub.wheelOff) anyWheel = true;
                if (sub.liftOnly) anyRails = true;
                if (sub.scope) anyScope = true;
            }

            var parts = new List<string>();
            if (anyUnder)
                parts.Add(acc.raise == Toolbox.Raise.Lift
                    ? "On the lift you can walk right under it."
                    : acc.raise == Toolbox.Raise.Stands
                        ? "On your back under the stands — you can see, but not well."
                        : "The car is on its wheels. Raise it and the underside opens up.");
            if (anyWheel && !WheelsComeOff(acc))
                parts.Add(acc.impact
                    ? "The wheels stay on with the car sat on them — you are guessing at the brakes."
                    : "Without an impact wrench the wheels stay on — you are guessing at the brakes.");
            if (anyRails && acc.raise != Toolbox.Raise.Lift)
                parts.Add(acc.lift ? "The frame rails need it up on the lift."
                                   : "The frame rails are out of reach on a jack.");
            if (anyScope && !acc.scope)
                parts.Add("No borescope, so the inside of the engine stays the engine's business.");
            return parts.Count == 0 ? "Everything here is in plain sight." : string.Join(" ", parts);
        }

        // ------------------------------------------------------------------
        //  Hidden faults
        // ------------------------------------------------------------------
        /// <summary>
        /// Give a bought car the problems its seller did not mention.
        ///
        /// Mileage and condition decide how many: a 30k-mile car in the 90s is
        /// usually straight, a 160k-mile car at 40 condition almost never is.
        /// One roll per stat lane, because RollWearFault caps at one non-severe
        /// fault per lane and stacking three engine faults on a single car
        /// would be a punishment rather than a puzzle.
        /// </summary>
        public static int SeedHidden(LifeState s, OwnedCar car, CarSpec spec, int cond)
        {
            if (car == null) return 0;
            string origin = spec != null ? spec.origin : "jpn";

            // 0 at a pristine low-mileage car, ~0.85 at a tired high-mileage one.
            float wear = Mathf.Clamp01(car.odoMiles / 180000f);
            float rough = Mathf.Clamp01((85f - cond) / 70f);
            float chance = Mathf.Clamp(0.18f + wear * 0.45f + rough * 0.35f, 0f, 0.85f);

            int n = 0;
            foreach (var stat in new[] { "engine", "tires", "hp" })
            {
                if (Random.value > chance) continue;
                var f = FaultCatalog.RollWearFault(car, stat, false, "wear", origin);
                if (f == null) continue;
                f.hidden = true;
                f.diagnosed = false;
                car.faults.Add(f);
                n++;
            }
            return n;
        }

        public static int HiddenCount(OwnedCar car)
        {
            if (car == null) return 0;
            int n = 0;
            foreach (var f in car.faults) if (f.hidden) n++;
            return n;
        }

        // ------------------------------------------------------------------
        //  Somebody else looks at it
        // ------------------------------------------------------------------
        /// <summary>Who is doing the looking. The player's own inspection is
        /// the component map; these two are the ways to buy the answer.</summary>
        public enum Pro { Mechanic, Dealer }

        /// <summary>
        /// What each pro finds, per hidden fault.
        ///
        /// The mechanic misses things — that is the whole reason the dealer's
        /// price is worth paying and the reason a careful owner still gets
        /// under it themselves. The dealer has the ramp, the reader and the
        /// service history, so they find everything; what they charge for it is
        /// the balance.
        /// </summary>
        static float FindRate(Pro who) => who == Pro.Dealer ? 1f : 0.72f;

        /// <summary>Base fee before the car-value scaling every other bill in
        /// this game gets.</summary>
        static int BaseFee(Pro who) => who == Pro.Dealer ? 300 : 120;

        public static int ProCost(OwnedCar car, Pro who) =>
            LifeRules.ServiceCost(car, BaseFee(who));

        public static string ProLabel(Pro who) =>
            who == Pro.Dealer ? "DEALER INSPECTION" : "MECHANIC INSPECTION";

        /// <summary>
        /// Book a professional inspection: money and a time slot for a list of
        /// what is actually wrong with the car.
        ///
        /// Returns the line to show the player. Costs a slot like the player's
        /// own inspection does, because it is the same errand — the car has to
        /// go somewhere and come back — and because a paid inspection that cost
        /// no time would make the player's own toolbox pointless.
        /// </summary>
        public static string BookPro(LifeState s, OwnedCar car, Pro who)
        {
            if (car == null) return "no car";
            int price = ProCost(car, who);
            if (s.money < price) return "need " + MenuKit.Money(price);

            s.money -= price;
            LifeRules.SpendActivitySlot(s);
            // Leave a mark on the CAR, not just in the calendar. Without it the
            // save cannot tell a fault a dealer found from one that was never
            // hidden in the first place, which is precisely the ambiguity the
            // v7 migration had to guess its way through.
            car.proInspectDay = s.day;

            float rate = FindRate(who);
            var found = new List<string>();
            foreach (var f in car.faults)
            {
                if (!f.hidden) continue;
                if (Random.value > rate) continue;
                f.hidden = false;
                f.diagnosed = true;
                found.Add(f.label);
            }

            string what = found.Count == 0
                ? "nothing they could find"
                : string.Join(", ", found);
            s.calendarLog.Add("Day " + s.day + ": " + ProLabel(who) + " on " +
                              car.displayName + " — " + what);
            // Deliberately does NOT say how many are left. A clean bill from the
            // mechanic has to be able to be wrong, or paying the dealer never
            // buys anything.
            return found.Count == 0
                ? ProLabel(who) + ": they found nothing"
                : ProLabel(who) + ": " + what;
        }
    }
}
