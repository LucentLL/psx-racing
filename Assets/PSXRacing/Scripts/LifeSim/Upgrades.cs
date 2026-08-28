using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// The tuning ladder: five categories, four stages each, per owned car.
    /// Ported from RG2's config/cars/upgradeHeadroom.ts (the stage curves and
    /// their effects) and sim/upgradeCost.ts (what a stage costs, how long it
    /// takes, and how much mechanical skill it needs to do yourself).
    ///
    /// The endpoints are NOT computed here. A car's stock->built HP span and its
    /// minimum streetable weight come from RG2's platform table (a 13B-REW tops
    /// ~500 crank whether it started at 255 or 280; a 2JZ ~700) and are baked
    /// into <see cref="CarSpec.builtHp"/> / <see cref="CarSpec.minKg"/> by the
    /// catalog bake. This module only walks the ladder between them, which is
    /// what makes the same five categories mean something different on a 85 hp
    /// Civic and on a 600 hp Group C car.
    ///
    /// DELIBERATE DEVIATION from RG2: RG2's DIY path is two steps — mail-order a
    /// parts kit, wait for it, then install it — which needs an `ownedParts`
    /// inventory in the save. This port keeps the ONE-step shape the fault
    /// repairs already use (order -> queued in pendingParts -> resolves on a day
    /// rollover), because a menu-based build has no garage to walk into and a
    /// second queue would be state with no screen behind it. The DIY/shop
    /// distinction survives intact: DIY is the parts price and is skill-gated,
    /// the shop is 1.6x and is not.
    /// </summary>
    public static class Upgrades
    {
        public enum Kind { Power = 0, Weight = 1, Brakes = 2, Suspension = 3, Tires = 4 }

        public const int MaxStage = 4;

        public static readonly string[] KindLabels =
            { "POWER", "WEIGHT", "BRAKES", "SUSPENSION", "TIRES" };

        /// <summary>What each category is actually buying, for the shop rows.
        /// Stage-indexed 1..4; index 0 is the stock car.</summary>
        public static readonly string[][] StageNames =
        {
            new[] { "STOCK", "INTAKE + EXHAUST", "ECU + BOOST", "TURBO / CAMS", "BUILT ENGINE" },
            new[] { "STOCK", "STRIP INTERIOR", "LIGHT WHEELS", "GLASS + SEATS", "CARBON PANELS" },
            new[] { "STOCK", "PADS + FLUID", "SLOTTED ROTORS", "BIG BRAKE KIT", "RACE CALIPERS" },
            new[] { "STOCK", "LOWERING SPRINGS", "SPORT DAMPERS", "COILOVERS", "RACE COILOVERS" },
            new[] { "STOCK", "SPORT TIRES", "PERFORMANCE", "SEMI-SLICKS", "TRACK COMPOUND" },
        };

        // ---- stage curves --------------------------------------------------
        // The curves themselves live in CarTune, on the race side: the shop and
        // the stopwatch must agree on what a stage is worth, and the surest way
        // to make them disagree is to keep two tables.
        public static int BrakeMaxPct => Mathf.RoundToInt((CarTune.BuiltBrakeMult - 1f) * 100f);
        public static int SuspMaxPct => Mathf.RoundToInt((CarTune.BuiltSuspMult - 1f) * 100f);
        public static int GripMaxPct => Mathf.RoundToInt((CarTune.BuiltGripMult - 1f) * 100f);

        static int Clamp(int stage) => Mathf.Clamp(stage, 0, MaxStage);

        // ---- per-car state ------------------------------------------------
        public static int GetStage(OwnedCar car, Kind kind)
        {
            if (car == null) return 0;
            switch (kind)
            {
                case Kind.Power: return Clamp(car.upPower);
                case Kind.Weight: return Clamp(car.upWeight);
                case Kind.Brakes: return Clamp(car.upBrakes);
                case Kind.Suspension: return Clamp(car.upSuspension);
                default: return Clamp(car.upTires);
            }
        }

        public static void SetStage(OwnedCar car, Kind kind, int stage)
        {
            if (car == null) return;
            stage = Clamp(stage);
            switch (kind)
            {
                case Kind.Power: car.upPower = stage; break;
                case Kind.Weight: car.upWeight = stage; break;
                case Kind.Brakes: car.upBrakes = stage; break;
                case Kind.Suspension: car.upSuspension = stage; break;
                default: car.upTires = stage; break;
            }
        }

        public static bool IsStock(OwnedCar car) =>
            car != null && car.upPower == 0 && car.upWeight == 0 && car.upBrakes == 0 &&
            car.upSuspension == 0 && car.upTires == 0;

        /// <summary>Total stages bought across all five categories, 0-20. The
        /// one-number "how built is this car" the garage list shows.</summary>
        public static int TotalStages(OwnedCar car) =>
            car == null ? 0 : Clamp(car.upPower) + Clamp(car.upWeight) + Clamp(car.upBrakes) +
                              Clamp(car.upSuspension) + Clamp(car.upTires);

        // ---- pricing ------------------------------------------------------
        const int PerHp = 55;
        /// <summary>Weight reduction is the CHEAP mod early — stage 1 is pulling
        /// the interior and a lighter battery, mostly labour. $45/kg made a Civic
        /// interior-strip cost ~$1.5k, which is absurd for 1999; $12/kg plus the
        /// steep per-stage premium below keeps stage 1 cheap and still makes
        /// stage 4 carbon panels properly expensive.</summary>
        const int PerKg = 12;
        const int BaseBrake = 220;   // S1 = pads + fluid
        const int BaseSusp = 200;    // S1 = lowering springs
        const int BaseTire = 250;    // S1 = a set of sport tyres
        const float ShopMult = 1.6f;

        /// <summary>DIY skill requirement by category and target stage. Handling
        /// bolt-ons need less than an engine build; tyres are the easiest swap of
        /// the five.</summary>
        static readonly int[][] SkillReq =
        {
            new[] { 0, 25, 45, 65, 85 },   // power
            new[] { 0, 20, 35, 55, 75 },   // weight
            new[] { 0, 15, 30, 50, 70 },   // brakes
            new[] { 0, 20, 38, 58, 78 },   // suspension
            new[] { 0, 10, 22, 40, 60 },   // tires
        };

        /// <summary>Car-class price multiplier — exotics cost more to work on.
        /// sqrt of price against a $15k reference, capped at 3.5x (at 5x, big
        /// jobs printed five-figure bills on $300k cars). Race cars take a
        /// further 1.5x: nothing on them is a catalogue part.</summary>
        public static float CarCostMult(CarSpec spec)
        {
            if (spec == null) return 1f;
            float price = spec.price > 0 ? spec.price : 15000f;
            float mult = Mathf.Clamp(Mathf.Sqrt(price / 15000f), 0.6f, 3.5f);
            return spec.IsRaceCar ? mult * 1.5f : mult;
        }

        /// <summary>
        /// The car multiplier DAMPED by how much labour the job actually is. A
        /// cheap consumable barely tracks car value in the real world — an NSX
        /// oil change is not 2.4x a Civic's — while an engine build tracks it
        /// fully. Without this, stage-1 brakes on an NSX quoted $6,336.
        /// </summary>
        public static float EffCostMult(CarSpec spec, int baseCost)
        {
            float full = CarCostMult(spec);
            float labour = Mathf.Clamp(0.45f + ((baseCost - 150f) / 450f) * 0.55f, 0.45f, 1f);
            return 1f + (full - 1f) * labour;
        }

        /// <summary>Exotics need more mechanical skill to touch. Race cars jump
        /// straight to +60 — you are not doing that in a driveway.</summary>
        public static int CarSkillBoost(CarSpec spec)
        {
            if (spec == null) return 0;
            if (spec.IsRaceCar) return 60;
            float price = spec.price > 0 ? spec.price : 15000f;
            return Mathf.Clamp(Mathf.FloorToInt((price - 15000f) / 8000f), 0, 25);
        }

        /// <summary>One quoted stage step: what it changes, what it costs both
        /// ways, how long it takes and what it needs of you.</summary>
        public struct Plan
        {
            public Kind kind;
            public int fromStage, toStage;
            /// <summary>Displayed before/after value in <see cref="unit"/>.</summary>
            public int fromVal, toVal;
            /// <summary>Positive magnitude of the change (hp gained, kg shed, % gained).</summary>
            public int delta;
            public string unit;          // "hp" / "kg" / "%"
            public int diyPrice, shopPrice, days, skillReq;
            public bool canDiy;
            public string stageName;
            public bool valid;
        }

        /// <summary>
        /// Quote the NEXT stage for a category. Returns an invalid plan when the
        /// car is already maxed — the caller shows "MAXED" rather than a price.
        /// </summary>
        public static Plan NextStagePlan(LifeState s, OwnedCar car, CarSpec spec, Kind kind)
        {
            var p = new Plan { kind = kind, valid = false };
            if (s == null || car == null || spec == null) return p;

            int from = GetStage(car, kind);
            int to = from + 1;
            if (to > MaxStage) return p;

            p.fromStage = from;
            p.toStage = to;
            p.stageName = StageNames[(int)kind][to];

            int basePrice;
            switch (kind)
            {
                case Kind.Power:
                    p.fromVal = CarTune.PowerAtStage(spec.hp, spec.builtHp, from);
                    p.toVal = CarTune.PowerAtStage(spec.hp, spec.builtHp, to);
                    p.delta = Mathf.Max(0, p.toVal - p.fromVal);
                    p.unit = "hp";
                    basePrice = p.delta * PerHp;
                    break;
                case Kind.Weight:
                    p.fromVal = CarTune.WeightAtStage(spec.kg, spec.minKg, from);
                    p.toVal = CarTune.WeightAtStage(spec.kg, spec.minKg, to);
                    p.delta = Mathf.Max(0, p.fromVal - p.toVal);
                    p.unit = "kg";
                    basePrice = p.delta * PerKg;
                    break;
                case Kind.Brakes:
                    p.fromVal = Mathf.RoundToInt((CarTune.BrakeStageMult(from) - 1f) * 100f);
                    p.toVal = Mathf.RoundToInt((CarTune.BrakeStageMult(to) - 1f) * 100f);
                    p.delta = Mathf.Max(0, p.toVal - p.fromVal);
                    p.unit = "%";
                    basePrice = BaseBrake;
                    break;
                case Kind.Suspension:
                    p.fromVal = Mathf.RoundToInt((CarTune.SuspStageMult(from) - 1f) * 100f);
                    p.toVal = Mathf.RoundToInt((CarTune.SuspStageMult(to) - 1f) * 100f);
                    p.delta = Mathf.Max(0, p.toVal - p.fromVal);
                    p.unit = "%";
                    basePrice = BaseSusp;
                    break;
                default:
                    p.fromVal = Mathf.RoundToInt((CarTune.GripStageMult(from) - 1f) * 100f);
                    p.toVal = Mathf.RoundToInt((CarTune.GripStageMult(to) - 1f) * 100f);
                    p.delta = Mathf.Max(0, p.toVal - p.fromVal);
                    p.unit = "%";
                    basePrice = BaseTire;
                    break;
            }

            // Handling categories are hardware with a flat base, so they use the
            // labour-damped multiplier and a steep per-stage premium: stage 1 is
            // a consumable, stage 4 is race hardware. Power keeps the full car
            // multiplier (engine money really does scale with the car) and a
            // gentle premium, since its front-loaded gain curve already makes
            // late stages worse value per dollar.
            bool handling = kind == Kind.Brakes || kind == Kind.Suspension || kind == Kind.Tires;
            float mult = handling ? EffCostMult(spec, basePrice) : CarCostMult(spec);
            float premiumPerStage = (kind == Kind.Weight || handling) ? 1.0f : 0.25f;
            float stagePremium = 1f + (to - 1) * premiumPerStage;

            p.diyPrice = Mathf.RoundToInt(basePrice * mult * stagePremium);
            p.shopPrice = Mathf.RoundToInt(p.diyPrice * ShopMult);
            p.days = to + 1;                       // stage 1 = 2 days ... stage 4 = 5
            p.skillReq = Mathf.Min(95, SkillReq[(int)kind][to] + CarSkillBoost(spec));
            p.canDiy = s.mechSkill >= p.skillReq;
            p.valid = true;
            return p;
        }

        public static PendingPart PendingFor(LifeState s, OwnedCar car, Kind kind)
        {
            if (s == null || car == null) return null;
            return s.pendingParts.Find(p => p.carId == car.id &&
                                            p.upgradeKind == UpgradeKindKey(kind));
        }

        /// <summary>Save-format key for a category. A string rather than the enum
        /// int because a reordered enum would silently re-point every queued job
        /// in every existing save.</summary>
        public static string UpgradeKindKey(Kind kind) => KindLabels[(int)kind].ToLowerInvariant();

        public static Kind KindFromKey(string key)
        {
            for (int i = 0; i < KindLabels.Length; i++)
                if (KindLabels[i].ToLowerInvariant() == key) return (Kind)i;
            return Kind.Power;
        }

        /// <summary>
        /// Buy a stage. Queues into the same pendingParts list the fault repairs
        /// use, so a build costs real days that the player passes by sleeping —
        /// and so a car in the middle of a build is visibly in the shop.
        /// Returns null on success, or the reason it was refused.
        /// </summary>
        public static string Order(LifeState s, OwnedCar car, CarSpec spec, Kind kind, bool useShop)
        {
            if (car == null || spec == null) return "no car";
            var plan = NextStagePlan(s, car, spec, kind);
            if (!plan.valid) return "already maxed";
            if (PendingFor(s, car, kind) != null) return "already booked";
            if (!useShop && !plan.canDiy) return "needs skill " + plan.skillReq;

            int price = useShop ? plan.shopPrice : plan.diyPrice;
            if (s.money < price) return "need " + MenuKit.Money(price);

            s.money -= price;
            if (!useShop)
                s.mechSkill = Mathf.Min(100f, s.mechSkill +
                                        FaultCatalog.DiySkillGain(s.mechSkill, plan.skillReq));

            s.pendingParts.Add(new PendingPart
            {
                carId = car.id,
                faultId = "",
                label = KindLabels[(int)kind] + " STAGE " + plan.toStage,
                stat = "engine",
                add = 0,
                readyDay = s.day + plan.days,
                venue = useShop ? 1 : 0,
                upgradeKind = UpgradeKindKey(kind),
                upgradeStage = plan.toStage,
            });
            s.calendarLog.Add("Day " + s.day + ": booked " + KindLabels[(int)kind] +
                              " stage " + plan.toStage + " (" + MenuKit.Money(price) + ", " +
                              plan.days + "d)");
            return null;
        }

        // ---- one-off mods ---------------------------------------------------
        /// <summary>
        /// The two bolt-ons that are not a ladder. Ported from RG2's PARTS_SHOP
        /// rows of the same names; prices and skill gates are its numbers.
        ///
        /// The supercharger is offered on NATURALLY ASPIRATED cars only. RG2's
        /// modular port allows it on anything because CatalogCar never grew the
        /// per-car `canSC` flag, but its own documentation is explicit that the
        /// monolith excluded turbo cars — they already have forced induction —
        /// and `asp` is right there in the baked spec, so the gate costs nothing
        /// to honour. It is also what keeps the blower whine on cars that should
        /// have one.
        /// </summary>
        public enum Mod { WeldedDiff = 0, Supercharger = 1 }

        public struct ModOffer
        {
            public Mod mod;
            public string name, effect, blockedReason;
            public int price, skillReq;
            public bool owned, canDiy, available;
        }

        /// <summary>
        /// Both mods are same-day garage work, skill-gated, with no shop
        /// alternative. RG2 routes the supercharger through a mechanic over one
        /// day, which here would mean teaching PendingPart a third job type for
        /// a one-off purchase nobody waits on twice. The skill 85 gate is what
        /// actually paces the blower, and that survives intact.
        /// </summary>

        public static ModOffer OfferFor(LifeState s, OwnedCar car, CarSpec spec, Mod mod)
        {
            var o = new ModOffer { mod = mod };
            bool weld = mod == Mod.WeldedDiff;
            o.name = weld ? "WELD DIFF" : "SUPERCHARGER";
            o.effect = weld ? "both driven wheels break away together"
                            : "+30% torque, tapering to +15% at redline";
            int baseCost = weld ? 150 : 3000;
            int baseSkill = weld ? 35 : 85;

            o.price = Mathf.RoundToInt(baseCost * (weld ? EffCostMult(spec, baseCost)
                                                        : CarCostMult(spec)));
            o.skillReq = Mathf.Min(95, baseSkill + CarSkillBoost(spec));
            o.canDiy = s != null && s.mechSkill >= o.skillReq;
            o.owned = car != null && (weld ? car.welded : car.supercharged);

            if (o.owned) { o.blockedReason = "FITTED"; return o; }
            if (!weld && spec != null && spec.IsForcedInduction)
            {
                o.blockedReason = spec.IsTurbo ? "ALREADY TURBOCHARGED" : "ALREADY SUPERCHARGED";
                return o;
            }
            o.available = true;
            return o;
        }

        /// <summary>Buy a mod. The welded diff is a garage job and lands the same
        /// day; the blower goes to a shop and takes one.</summary>
        public static string OrderMod(LifeState s, OwnedCar car, CarSpec spec, Mod mod)
        {
            var o = OfferFor(s, car, spec, mod);
            if (!o.available) return o.blockedReason ?? "unavailable";
            if (!o.canDiy) return "needs skill " + o.skillReq;
            if (s.money < o.price) return "need " + MenuKit.Money(o.price);

            s.money -= o.price;
            s.mechSkill = Mathf.Min(100f, s.mechSkill +
                                    FaultCatalog.DiySkillGain(s.mechSkill, o.skillReq));
            if (mod == Mod.WeldedDiff) car.welded = true; else car.supercharged = true;
            s.calendarLog.Add("Day " + s.day + ": fitted " + o.name + " (" +
                              MenuKit.Money(o.price) + ")");
            return null;
        }

        // ---- the car as it actually performs --------------------------------
        /// <summary>This car's stages in the form the race scene consumes.</summary>
        public static CarTune.Stages StagesOf(OwnedCar car) => new CarTune.Stages
        {
            power = GetStage(car, Kind.Power),
            weight = GetStage(car, Kind.Weight),
            brakes = GetStage(car, Kind.Brakes),
            suspension = GetStage(car, Kind.Suspension),
            tires = GetStage(car, Kind.Tires),
        };

        /// <summary>Crank HP as built — what the SPECS screen should show
        /// instead of the factory figure once anything is bolted on.</summary>
        public static int EffectiveHp(OwnedCar car, CarSpec spec) =>
            spec == null ? 0
                         : CarTune.PowerAtStage(spec.hp, spec.builtHp, GetStage(car, Kind.Power));

        public static int EffectiveKg(OwnedCar car, CarSpec spec) =>
            spec == null ? 0
                         : CarTune.WeightAtStage(spec.kg, spec.minKg, GetStage(car, Kind.Weight));
    }
}
