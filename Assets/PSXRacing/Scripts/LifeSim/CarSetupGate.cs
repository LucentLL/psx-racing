using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// What a particular car is allowed to adjust, and why not.
    ///
    /// This is the whole "custom modifications unlock tuning" rule, in one
    /// place. A stock car can adjust nothing: every row on the setup screen is
    /// padlocked and every padlock names the part that would open it, which
    /// makes the screen the best advertisement the parts shop has.
    ///
    /// The gate is ENFORCED, not advisory. <see cref="Sanitize"/> runs on the
    /// way to the race and zeroes anything the car has no part for, so a
    /// hand-edited save cannot race a tune it never bought — and so the race
    /// scene can apply a setup blindly, without a second copy of this rule.
    /// </summary>
    public static class CarSetupGate
    {
        /// <summary>
        /// This car's setup, made on demand. Never returns null and never hands
        /// back a shared instance, so a caller can write to it freely.
        /// </summary>
        public static CarSetup SetupOf(OwnedCar car)
        {
            if (car == null) return new CarSetup();
            if (car.setup == null) car.setup = new CarSetup();
            return car.setup;
        }

        /// <summary>What each parameter needs. Returns true when a stage gate
        /// applies, filling <paramref name="kind"/> and <paramref name="stage"/>;
        /// false when a MOD gates it, filling <paramref name="mod"/>.</summary>
        static bool StageGate(SetupParam p, out Upgrades.Kind kind, out int stage,
                              out Upgrades.Mod mod)
        {
            kind = Upgrades.Kind.Power; stage = 0; mod = Upgrades.Mod.WeldedDiff;

            if (CarSetupTable.GearIndex(p) >= 0) { mod = Upgrades.Mod.GearSet; return false; }

            switch (p)
            {
                // Sport tyres are the first set with a pressure worth choosing.
                case SetupParam.TyrePressureFront:
                case SetupParam.TyrePressureRear:
                    kind = Upgrades.Kind.Tires; stage = 1; return true;

                // Pads and fluid give you something to modulate.
                case SetupParam.BrakePressure:
                    kind = Upgrades.Kind.Brakes; stage = 1; return true;
                // A proportioning valve comes with the big brake kit, not before.
                case SetupParam.BrakeBalance:
                    kind = Upgrades.Kind.Brakes; stage = 3; return true;

                // Lowering the car forces an alignment, so that is when the
                // front adjustment appears. The rear waits for coilovers: most
                // road cars have no rear camber adjustment at all until then.
                case SetupParam.CamberFront:
                case SetupParam.ToeFront:
                    kind = Upgrades.Kind.Suspension; stage = 1; return true;
                case SetupParam.CamberRear:
                case SetupParam.ToeRear:
                    kind = Upgrades.Kind.Suspension; stage = 3; return true;

                // Dampers at stage 2 and springs at stage 3, and the inversion
                // is deliberate: stage 2 is literally SPORT DAMPERS, and you
                // cannot change the rate of a fixed lowering spring. Stage 3 is
                // COILOVERS, which is when rate and ride height become yours.
                case SetupParam.DamperFront:
                case SetupParam.DamperRear:
                    kind = Upgrades.Kind.Suspension; stage = 2; return true;
                case SetupParam.SpringFront:
                case SetupParam.SpringRear:
                case SetupParam.RideHeight:
                    kind = Upgrades.Kind.Suspension; stage = 3; return true;

                case SetupParam.SteerLock:
                case SetupParam.SteerRate:
                case SetupParam.SelfCentre:
                    mod = Upgrades.Mod.SteeringRack; return false;
                case SetupParam.ArbFront:
                case SetupParam.ArbRear:
                    mod = Upgrades.Mod.SwayBars; return false;
                case SetupParam.DiffAccel:
                case SetupParam.DiffDecel:
                case SetupParam.DiffPreload:
                case SetupParam.DriveSplit:
                    mod = Upgrades.Mod.LimitedSlip; return false;
                case SetupParam.FinalDrive:
                    mod = Upgrades.Mod.FinalDrive; return false;
                case SetupParam.AeroLevel:
                case SetupParam.AeroBalance:
                    mod = Upgrades.Mod.AeroKit; return false;
                // Explicit, so a newly appended SetupParam is a visible error
                // rather than a row silently gated on the aero kit.
                default:
                    Debug.LogError("CarSetupGate: no gate defined for " + p);
                    mod = Upgrades.Mod.AeroKit; return false;
            }
        }

        public static bool Unlocked(OwnedCar car, CarSpec spec, SetupParam p) =>
            string.IsNullOrEmpty(BlockedReason(car, spec, p));

        /// <summary>
        /// True when the car does not HAVE this thing at all — as opposed to
        /// not having bought one yet. Such a row is not drawn.
        ///
        /// Only the gears qualify, and the line has to be drawn somewhere:
        /// "NEEDS CLOSE-RATIO GEAR SET" is a shopping list and belongs on
        /// screen, while "7TH GEAR — NO SUCH GEAR" on a six-speed is a row
        /// that can never become anything, printed twice on most cars. The
        /// reference screens keep row POSITIONS stable across a car's own
        /// build states, which this still does: a gear cannot appear or
        /// disappear while you own the car.
        ///
        /// CENTRE SPLIT on a two-wheel-drive car is deliberately NOT absent.
        /// It is one row on a page of four, and "NO CENTRE DIFF" is a fact
        /// about the car that a player comparing an Evo with a Supra wants
        /// stated rather than silently omitted.
        /// </summary>
        public static bool Absent(OwnedCar car, CarSpec spec, SetupParam p)
        {
            int g = CarSetupTable.GearIndex(p);
            // The same clamp BlockedReason uses, so the two cannot disagree
            // about where the gearbox ends.
            return g >= 0 && spec != null &&
                   g >= Mathf.Clamp(spec.gears, 3, CarSetup.MaxGears);
        }

        /// <summary>
        /// Why this MODEL of car can never adjust this parameter, whatever is
        /// bolted to the particular one in the garage — or null when it can.
        ///
        /// Split out because two callers need the same answer and used to
        /// carry two copies of it: <see cref="BlockedReason"/>, which prints
        /// it, and <see cref="AdjustableCount"/>, which has to subtract
        /// exactly these rows from "every row the car physically has". The
        /// self-test asserts a fully-built car opens every row that is not one
        /// of these, so a fact added to one and not the other fails the build
        /// — which is the intent.
        /// </summary>
        static string CarFact(CarSpec spec, SetupParam p)
        {
            if (p == SetupParam.DriveSplit && (spec == null || spec.drv != "4WD"))
                return "NO CENTRE DIFF";
            int g = CarSetupTable.GearIndex(p);
            if (g >= 0 && spec != null && g >= Mathf.Clamp(spec.gears, 3, CarSetup.MaxGears))
                return "NO SUCH GEAR";
            // A road car has no adjustable wing and, in 1999, no way to buy
            // one. Phrased as a fact rather than as a shopping list, because
            // the parts page will not sell it either — see
            // Upgrades.AeroKitAllowed.
            if ((p == SetupParam.AeroLevel || p == SetupParam.AeroBalance) &&
                !Upgrades.AeroKitAllowed(spec))
                return "NOT A RACE CAR";
            return null;
        }

        /// <summary>
        /// Empty when the parameter is adjustable; otherwise the reason, ready
        /// to print. Every refusal that CAN be fixed names the part that fixes
        /// it — a padlock that does not say what opens it is just a dead row —
        /// and every refusal that cannot states the fact flatly instead. The
        /// two are phrased differently on purpose: "NEEDS LIMITED-SLIP DIFF" is
        /// a shopping list, "NO CENTRE DIFF" is the car.
        /// </summary>
        public static string BlockedReason(OwnedCar car, CarSpec spec, SetupParam p)
        {
            if (car == null) return "NO CAR";

            // Facts about the CAR come before any part it might carry.
            string fact = CarFact(spec, p);
            if (fact != null) return fact;
            // A welded diff has nothing left to adjust. Saying so is more use
            // than showing three sliders that do nothing. Not a CarFact: it is
            // a fact about a part that was FITTED, so it can be true of one
            // owned car and false of the identical model beside it.
            if (car.welded && (p == SetupParam.DiffAccel || p == SetupParam.DiffDecel ||
                               p == SetupParam.DiffPreload))
                return "DIFF IS WELDED";

            if (StageGate(p, out var kind, out int stage, out var mod))
                return Upgrades.GetStage(car, kind) >= stage
                    ? null
                    : "NEEDS " + Upgrades.KindLabels[(int)kind] + " STAGE " + stage;

            if (Upgrades.HasMod(car, mod)) return null;
            Upgrades.ModName(mod, out string name);
            return "NEEDS " + name;
        }

        /// <summary>
        /// A copy of this car's setup with every locked parameter zeroed.
        ///
        /// Called on the way into a race and nowhere else. Never returns null,
        /// so the race scene has one shape to handle; a car with nothing fitted
        /// comes back all-zero, which <see cref="CarController.ApplySetup"/>
        /// tests for with IsFactory and treats as no setup at all. That test is
        /// load-bearing — since this never returns null, a stock car is the ONLY
        /// thing that ever reaches the controller's factory path.
        /// </summary>
        public static CarSetup Sanitize(OwnedCar car, CarSpec spec)
        {
            var src = SetupOf(car);
            var outv = src.Clone();
            for (int i = 0; i < CarSetupTable.Count; i++)
            {
                var p = (SetupParam)i;
                if (!Unlocked(car, spec, p)) outv.Set(p, 0f);
            }
            return outv;
        }

        /// <summary>The basis this car's ranges are derived from, in the menu,
        /// where there is no CarController for two scene loads in any
        /// direction.</summary>
        public static CarSetupBasis BasisFor(OwnedCar car, CarSpec spec) =>
            CarSetupBasis.FromSpec(spec, Upgrades.StagesOf(car), car != null && car.welded);

        /// <summary>How many of the 30-odd rows this car can actually touch.
        /// The one number the parts page shows to say "there is a reason to buy
        /// these".</summary>
        public static int UnlockedCount(OwnedCar car, CarSpec spec)
        {
            int n = 0;
            for (int i = 0; i < CarSetupTable.Count; i++)
                if (Unlocked(car, spec, (SetupParam)i)) n++;
            return n;
        }

        public static int AdjustableCount(OwnedCar car, CarSpec spec)
        {
            int n = 0;
            for (int i = 0; i < CarSetupTable.Count; i++)
                if (CarFact(spec, (SetupParam)i) == null) n++;
            return n;
        }
    }
}
