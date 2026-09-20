using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// THE DEBUG BENCH: give the car being driven any fault in the catalog and
    /// any part it could be sold, right now, and have the car under the player
    /// become that car.
    ///
    /// The owner's brief (2026-09-18): "In Debug Mode, I want the option to
    /// give my car any fault or any upgrade modification available to the car
    /// at any time. So I can actively test the effects of the faults and mods
    /// mid-race or free roam."
    ///
    /// Until this existed a fault could only be ROLLED — a lane, a mileage
    /// gate, one per lane, a dice throw — and a part could only be bought,
    /// waited days for, and met at the next scene load. Testing "what does a
    /// warped rotor do to this car" meant buying American cars until one had
    /// one. PLANT FAULTS on the options page was the only tool, and it plants
    /// at random.
    ///
    /// This class is the RULES, with no UI in it, so the self-test can drive
    /// it: <see cref="DebugCarPanel"/> is the page the pause menu opens.
    ///
    /// THREE DECISIONS worth writing down:
    ///
    ///   * THE CHANGE IS MADE TO THE OWNED CAR, not to the physics. The bench
    ///     edits the save's <see cref="OwnedCar"/>, rewrites the race request
    ///     through the garage's own <see cref="LifeHomeScreen.FillCarRequestFor"/>
    ///     and has <see cref="RaceHandoffApplier.ReapplyCar"/> read it back —
    ///     the identical path a scene load takes. A bench that poked
    ///     faultGripMult directly would be testing itself: the point is to
    ///     feel what the GAME does with a fault, aggregate rule and all. It
    ///     also means the car in the garage afterwards is the car that was
    ///     driven, and RESTART RACE comes back as the same car.
    ///   * A BENCH FAULT IS AN ORDINARY FAULT. Hidden, like every other one,
    ///     priced and repairable like every other one. The inspection layer
    ///     can then be tested against a fault the tester KNOWS is there, which
    ///     is the one thing PLANT FAULTS could never offer. The bench lists
    ///     what the car carries regardless — it is a debug page.
    ///   * "AVAILABLE TO THE CAR" MEANS WHAT THE SHOP MEANS. The wallet, the
    ///     skill gate, the days and the where-is-the-car rule are all waived;
    ///     the two facts about the CAR are not (<see cref="Upgrades.CarRefuses"/>:
    ///     no wing on a road car, no blower on a turbo engine). A weld and a
    ///     plate pack are still the same hole in the car, so fitting one takes
    ///     the other out rather than refusing.
    /// </summary>
    public static class DebugCarOps
    {
        /// <summary>
        /// Whether the bench exists at all right now: a debug career, in a
        /// drive the LifeSim started. FromLifeSim is tested FIRST and that
        /// order is load-bearing — <see cref="LifeSimManager.State"/> creates
        /// a career on first touch, and a standalone editor race must not
        /// grow one because somebody opened the pause menu.
        /// </summary>
        public static bool Available =>
            RaceHandoff.FromLifeSim && LifeSimManager.State != null &&
            LifeSimManager.State.debugMode;

        /// <summary>
        /// The car under the player, as the SAVE knows it.
        ///
        /// A test drive is the seller's car, which lives on the viewing and
        /// not in the garage — and falling back to the active car there would
        /// have the bench quietly rebuilding the player's own car, parked at
        /// home, while they drove somebody else's. So a test drive that cannot
        /// find its viewing gets NO car rather than the wrong one.
        /// </summary>
        public static OwnedCar TargetCar(LifeState s)
        {
            if (s == null) return null;
            if (RaceHandoff.TestDrive)
            {
                var v = Viewings.ByKey(s, RaceHandoff.TestDriveKey);
                return v != null ? v.car : null;
            }
            return s.FindCar(RaceHandoff.CarId) ?? s.ActiveCar;
        }

        static string OriginOf(CarSpec spec) =>
            spec != null && !string.IsNullOrEmpty(spec.origin) ? spec.origin : "jpn";

        // ------------------------------------------------------------------
        //  Faults
        // ------------------------------------------------------------------

        /// <summary>The three lanes a fault can sit in, in the order the page
        /// lists them, with what the lane is actually about.</summary>
        public static readonly string[] Lanes = { "engine", "tires", "hp" };
        public static readonly string[] LaneTitles =
        {
            "ENGINE + DRIVELINE   ·   power, fuel, shifting, cooling",
            "TYRES, SUSPENSION, BRAKES   ·   grip, pull, stopping",
            "BODY + ELECTRICS   ·   gauges, and things that only cost money",
        };

        public struct FaultRow
        {
            public string id, name, stat;
            /// <summary>What it does, in the driver's terms.</summary>
            public string effect;
            /// <summary>False when nothing the drive simulates changes — the
            /// fault is money only, or its effect is one this port never
            /// wired. Said on the row, because a tester who switches it on and
            /// feels nothing must be told that nothing is the right answer.
            /// </summary>
            public bool felt;
        }

        /// <summary>
        /// Every fault in the catalog, lane by lane — ALL of them, not the
        /// car's own origin pool. Named from the car's own row where it has
        /// one (see <see cref="FaultCatalog.PoolRow"/>).
        /// </summary>
        public static List<FaultRow> FaultRows(CarSpec spec, string lane)
        {
            string origin = OriginOf(spec);
            var rows = new List<FaultRow>();
            var origins = new List<string>();
            foreach (var id in FaultCatalog.AllIds())
            {
                var p = FaultCatalog.PoolRow(id, origin);
                if (p == null || p.stat != lane) continue;
                var row = new FaultRow { id = id, name = p.name, stat = p.stat };
                row.effect = Describe(FaultCatalog.Effect(id), out row.felt);
                rows.Add(row);
                origins.Add(p.origin);
            }

            // TWO IDS CAN WEAR ONE NAME. RG2's Japanese pool has `oil_leak` and
            // its American pool has `oil_pan_gasket`, both printed OIL PAN
            // GASKET LEAK with the same effect — separate rows in the data and
            // so separate switches here, and two identical switches side by
            // side read as a bug in the page. Where a name collides, each copy
            // says whose pool it came from.
            var uses = new Dictionary<string, int>();
            foreach (var r in rows) uses[r.name] = uses.TryGetValue(r.name, out int n) ? n + 1 : 1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (uses[rows[i].name] < 2) continue;
                var r = rows[i];
                r.name = r.name + " (" + origins[i].ToUpperInvariant() + ")";
                rows[i] = r;
            }
            return rows;
        }

        /// <summary>
        /// An effect entry in words. Fuller than
        /// <see cref="FaultCatalog.EffectSummary"/>, which is the garage's
        /// line and leaves out what the garage has no use for: cooling (it
        /// prints "wear"), and the two RG2 effects this port carries in its
        /// data and never simulated. Those are NAMED here rather than hidden —
        /// a bench that lists forty faults and lets four of them silently do
        /// nothing reads as a broken bench.
        /// </summary>
        public static string Describe(FaultCatalog.EffectEntry e, out bool felt)
        {
            var parts = new List<string>();
            if (e.accelMult < 0.999f) parts.Add("power " + Pct(e.accelMult));
            if (e.gripMult < 0.999f) parts.Add("grip " + Pct(e.gripMult));
            if (e.brakeMult < 0.999f) parts.Add("brakes " + Pct(e.brakeMult));
            if (Mathf.Abs(e.steerPull) > 0.001f)
                parts.Add("pulls " + Mathf.RoundToInt(Mathf.Abs(e.steerPull) * 100f) + "% of lock");
            if (e.shiftMult > 1.001f) parts.Add("shifts x" + e.shiftMult.ToString("0.#") + " slower");
            if (e.fuelMult > 1.001f) parts.Add("fuel +" + Mathf.RoundToInt((e.fuelMult - 1f) * 100f) + "%");
            if (e.engineWearMult > 1.001f)
                parts.Add("cooling " + Pct(CoolMultFor(e.engineWearMult)));
            if (e.hideGauges) parts.Add("dead gauges");
            if (e.rpmFlutter) parts.Add("tach flutter");
            felt = parts.Count > 0;

            // In RG2's data, carried across by the bake, read by nothing here.
            if (e.steerSlow) parts.Add("slow steering (not simulated)");
            if (e.nightVisMult < 0.999f) parts.Add("dim lights (not simulated)");

            // A single-spaced dot: this line has to fit a cell ~430 units wide
            // at the type floor, and the worst row carries three effects.
            return parts.Count == 0 ? "costs money, changes nothing on the road"
                                    : string.Join(" · ", parts);
        }

        static string Pct(float mult)
        {
            int p = Mathf.RoundToInt((mult - 1f) * 100f);
            return (p > 0 ? "+" : "") + p + "%";
        }

        /// <summary>How much cooling is left for a given engine-wear
        /// multiplier — the SAME expression FillCarRequestFor uses for the
        /// whole car, so the row quotes what the gauge will do.</summary>
        static float CoolMultFor(float engineWearMult) =>
            Mathf.Clamp(1f / Mathf.Max(1f, engineWearMult), 0.3f, 1f);

        // ------------------------------------------------------------------
        //  Cooling
        // ------------------------------------------------------------------

        /// <summary>What to go and LOOK for once this part has been dropped.
        /// The bench's cooling rows are worth nothing without it: four bars
        /// that all make the needle climb are four ways of saying the same
        /// thing, and the whole point of splitting the system into parts is
        /// that they do not.</summary>
        public static string CoolPartNote(LifeRules.CoolPart part)
        {
            switch (part)
            {
                case LifeRules.CoolPart.Radiator:
                    return "the core. Fine at a cruise, cooks under load";
                case LifeRules.CoolPart.Fan:
                    return "only worth anything under 60 km/h — stop and watch";
                case LifeRules.CoolPart.Hoses:
                    return "holds pressure. Weeps once it is hot, and keeps weeping";
                default:
                    return "what is in it. Under 55% the loop loses authority";
            }
        }

        /// <summary>Set one cooling part on the car record. Clamped, and the
        /// coolant is topped back up with the hardware for the same reason the
        /// mechanic does it: no job that opens the system leaves it empty.
        /// </summary>
        public static void SetCoolPart(OwnedCar car, LifeRules.CoolPart part, int value)
        {
            if (car == null) return;
            float v = Mathf.Clamp(value, 0f, 100f);
            switch (part)
            {
                case LifeRules.CoolPart.Radiator: car.radiator = v; break;
                case LifeRules.CoolPart.Fan: car.fan = v; break;
                case LifeRules.CoolPart.Hoses: car.hoses = v; break;
                default: car.coolant = v; return;
            }
        }

        /// <summary>The model running under the player RIGHT NOW, or null in a
        /// menu. The bench's three live buttons need it; everything else on the
        /// page goes through the car record and the handoff.</summary>
        public static EngineTemp LiveTemp()
        {
            var applier = Object.FindFirstObjectByType<RaceHandoffApplier>();
            var car = applier != null ? applier.playerCar : null;
            if (car == null && RaceManager.Instance != null) car = RaceManager.Instance.playerCar;
            return car != null ? car.GetComponent<EngineTemp>() : null;
        }

        public static bool HasFault(OwnedCar car, string id) =>
            car != null && car.faults.Exists(f => f.id == id);

        /// <summary>Put a fault on, or take it off. True when the car
        /// changed. Taking one off removes EVERY copy: the severe lane rule
        /// can in principle leave two of one id on a neglected car, and a
        /// switch that says OFF has to mean off.</summary>
        public static bool SetFault(OwnedCar car, CarSpec spec, string id, bool on)
        {
            if (car == null || string.IsNullOrEmpty(id)) return false;
            if (!on) return car.faults.RemoveAll(f => f.id == id) > 0;
            if (HasFault(car, id)) return false;
            var f = FaultCatalog.MakeById(id, OriginOf(spec));
            if (f == null) return false;
            car.faults.Add(f);
            return true;
        }

        public static int ClearFaults(OwnedCar car)
        {
            if (car == null) return 0;
            int n = car.faults.Count;
            car.faults.Clear();
            return n;
        }

        // ------------------------------------------------------------------
        //  Parts
        // ------------------------------------------------------------------

        /// <summary>Set a ladder straight to a stage — not "buy the next
        /// one": a tester comparing stock against stage 4 wants two taps, not
        /// eight.</summary>
        public static bool SetStage(OwnedCar car, Upgrades.Kind kind, int stage)
        {
            if (car == null) return false;
            int before = Upgrades.GetStage(car, kind);
            Upgrades.SetStage(car, kind, stage);
            return Upgrades.GetStage(car, kind) != before;
        }

        /// <summary>What a ladder is worth at a stage, for the row: the same
        /// curves the shop quotes and the race builds from.</summary>
        public static string StageValue(CarSpec spec, Upgrades.Kind kind, int stage)
        {
            if (spec == null) return "";
            switch (kind)
            {
                case Upgrades.Kind.Power:
                    return CarTune.PowerAtStage(spec.hp, spec.builtHp, stage) + " hp";
                case Upgrades.Kind.Weight:
                    return CarTune.WeightAtStage(spec.kg, spec.minKg, stage) + " kg";
                case Upgrades.Kind.Brakes:
                    return "+" + Mathf.RoundToInt((CarTune.BrakeStageMult(stage) - 1f) * 100f) + "% bite";
                case Upgrades.Kind.Suspension:
                    return "+" + Mathf.RoundToInt((CarTune.SuspStageMult(stage) - 1f) * 100f) +
                           "% turn-in, " + Mathf.RoundToInt(CarTune.RideDropAtStage(stage) * 1000f) +
                           " mm lower";
                case Upgrades.Kind.Tires:
                    return "+" + Mathf.RoundToInt((CarTune.GripStageMult(stage) - 1f) * 100f) + "% grip";
                default:
                    return "holds " + PizzaCargo.SeatAt(stage).HoldsG.ToString("0.00") + " g";
            }
        }

        /// <summary>Why the bench will not fit this mod, or null. Only the
        /// facts about the car — see the class comment.</summary>
        public static string ModRefusal(CarSpec spec, Upgrades.Mod mod) =>
            spec == null ? "NO CATALOG ENTRY" : Upgrades.CarRefuses(spec, mod);

        /// <summary>
        /// Fit or remove a mod. Returns the refusal, or null when it went on
        /// (or came off). Fitting a weld takes the plate pack out and the
        /// other way round: they are the same hole in the car, and "refused —
        /// go and remove the other one first" is a shop's answer, not a
        /// bench's.
        /// </summary>
        public static string SetMod(OwnedCar car, CarSpec spec, Upgrades.Mod mod, bool on)
        {
            if (car == null) return "no car";
            if (on)
            {
                string no = ModRefusal(spec, mod);
                if (no != null) return no;
                if (mod == Upgrades.Mod.WeldedDiff) Upgrades.SetMod(car, Upgrades.Mod.LimitedSlip, false);
                if (mod == Upgrades.Mod.LimitedSlip) Upgrades.SetMod(car, Upgrades.Mod.WeldedDiff, false);
            }
            Upgrades.SetMod(car, mod, on);
            return null;
        }

        /// <summary>Every ladder to the top and every part the car will take.
        /// The plate pack rather than the weld, where the two collide: it is
        /// the one that unlocks something.</summary>
        public static void BuildEverything(OwnedCar car, CarSpec spec)
        {
            if (car == null) return;
            for (var k = Upgrades.Kind.Power; k <= Upgrades.LastKind; k++)
                Upgrades.SetStage(car, k, Upgrades.MaxStage);
            foreach (var mod in Upgrades.AllMods)
            {
                if (mod == Upgrades.Mod.WeldedDiff) continue;
                SetMod(car, spec, mod, true);
            }
        }

        /// <summary>Back to the car the factory built. The driver's TUNE is
        /// left in the save untouched — with the parts gone Sanitize zeroes
        /// it on the way to the car, and it is still there when they go back
        /// on.</summary>
        public static void StripEverything(OwnedCar car)
        {
            if (car == null) return;
            for (var k = Upgrades.Kind.Power; k <= Upgrades.LastKind; k++)
                Upgrades.SetStage(car, k, 0);
            foreach (var mod in Upgrades.AllMods) Upgrades.SetMod(car, mod, false);
        }

        // ------------------------------------------------------------------
        //  Onto the car
        // ------------------------------------------------------------------

        /// <summary>
        /// Make the car under the player the car the save now describes.
        ///
        /// The request is rewritten by the garage's own method and read back
        /// by the scene's own applier, so nothing here knows what a fault or a
        /// stage DOES — see the class comment for why that is the point. The
        /// applier is looked up rather than passed in because every drivable
        /// scene the builder makes carries exactly one, and the pause menu
        /// that opens the bench has never needed to know about it.
        ///
        /// Does NOT write the save. The page does that once, as it closes —
        /// see <see cref="DebugCarPanel"/>'s dirty flag for why.
        /// </summary>
        public static void Commit(LifeState s, OwnedCar car)
        {
            if (s == null || car == null) return;
            // Only in a drive, and only a drive the LifeSim started. The
            // garage opens this bench too, and there is no car to re-spec in a
            // menu: rewriting the request there would do nothing useful and
            // one thing harmful — FillCarRequestFor ends by setting the car
            // down off its jack stands, which is right on the way out to a
            // race and wrong for a car somebody is standing underneath.
            if (RaceHandoff.FromLifeSim)
            {
                var applier = Object.FindFirstObjectByType<RaceHandoffApplier>();
                if (applier != null)
                {
                    LifeHomeScreen.FillCarRequestFor(s, car);
                    applier.ReapplyCar();
                }
            }
        }

        public const string NoHandicap = "NO HANDICAP — nothing on this car is slowing it down";

        /// <summary>
        /// The handicap this car's faults add up to, through the game's own
        /// <see cref="FaultCatalog.Aggregate_"/> — the combining rule is not
        /// obvious (power and grip MULTIPLY, pull ADDS with a per-fault
        /// direction, shift takes the WORST), and what two faults do together
        /// is exactly the kind of thing the bench is for finding out. The same
        /// aggregate FillCarRequestFor hands the race, so the line and the car
        /// cannot disagree.
        /// </summary>
        public static string HandicapLine(OwnedCar car)
        {
            var a = FaultCatalog.Aggregate_(car);
            var parts = new List<string>();
            if (a.accelMult < 0.999f) parts.Add("POWER " + Pct(a.accelMult));
            if (a.gripMult < 0.999f) parts.Add("GRIP " + Pct(a.gripMult));
            if (a.brakeMult < 0.999f) parts.Add("BRAKES " + Pct(a.brakeMult));
            if (Mathf.Abs(a.steerPull) > 0.001f)
                parts.Add("PULLS " + (a.steerPull > 0f ? "RIGHT " : "LEFT ") +
                          Mathf.RoundToInt(Mathf.Abs(a.steerPull) * 100f) + "%");
            if (a.shiftMult > 1.001f) parts.Add("SHIFTS x" + a.shiftMult.ToString("0.#"));
            if (a.fuelMult > 1.001f)
                parts.Add("FUEL +" + Mathf.RoundToInt((a.fuelMult - 1f) * 100f) + "%");
            if (a.engineWearMult > 1.001f) parts.Add("COOLING " + Pct(CoolMultFor(a.engineWearMult)));
            if (a.hideGauges) parts.Add("NO GAUGES");
            if (a.rpmFlutter) parts.Add("FLUTTER");
            // Single-spaced: with everything wrong at once this is nine terms,
            // and it has one line of a phone to fit on.
            return parts.Count == 0 ? NoHandicap : string.Join(" · ", parts);
        }

        /// <summary>The build in one line: what it makes, what it weighs, and
        /// which bolt-ons are aboard.</summary>
        public static string BuildLine(OwnedCar car, CarSpec spec)
        {
            if (car == null) return "";
            if (spec == null) return "NO CATALOG ENTRY — this car takes faults but not parts";
            var sb = new System.Text.StringBuilder();
            sb.Append(Upgrades.EffectiveHp(car, spec)).Append(" hp");
            if (car.supercharged) sb.Append(" + blower");
            sb.Append("  ·  ").Append(Upgrades.EffectiveKg(car, spec)).Append(" kg  ·  ")
              .Append(Upgrades.TotalStages(car)).Append("/").Append(Upgrades.KindCount * Upgrades.MaxStage)
              .Append(" stages");
            int mods = 0;
            foreach (var mod in Upgrades.AllMods) if (Upgrades.HasMod(car, mod)) mods++;
            sb.Append("  ·  ").Append(mods).Append("/").Append(Upgrades.AllMods.Length).Append(" parts");
            return sb.ToString();
        }
    }
}
