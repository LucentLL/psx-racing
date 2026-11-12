using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// R0a CRASH STOP-GAP (Fault_System_Design_2026-11-11 §9, "The fix for
    /// every crash = cracked bumper + 25% pull").
    ///
    /// What it replaces. A crash used to be ONE uncapped number summed over
    /// the whole leg (CollisionResponder.DamageScore); past 22 the apply-back
    /// rolled the RG2 "impact" pool, and because no roller passed the car's
    /// origin that was always the Japanese pool, whose only impact rows are
    /// bumper_crack and alignment - a 0.25-of-lock pull in a coin-flip
    /// direction. The hardest hit was tracked on the responder and never
    /// reached the apply-back, and the fault only acted after the next scene
    /// load.
    ///
    /// What it is now. The hardest hit is LOCATED (front / rear / side and
    /// left / right, from the contact points in the car's own frame) and
    /// SIZED (its normal closing speed, m/s), and this table is rolled ONCE,
    /// AT THE HIT, while the car is still moving:
    ///
    ///   zone          band (m/s)              outcome
    ///   front, rear   below the bumper rating  nothing
    ///                 rating - 3               scuff (paint -2..-5)
    ///                 3 - 6                    bumper_crack 70% (bumper_dent on
    ///                                          chrome and 5-mph bumpers), else body -5
    ///   front         6 - 10                   + radiator -25..-60, weeping, 60%
    ///   front, rear   10 and up                + body -20..-40, alignment 40% on the
    ///                                          struck side (severe from 15)
    ///   side          1.5 - 3                  scuff
    ///                 3 - 6                    body -5..-15 (a dent)
    ///                 6 and up                 body -15..-30, alignment 50% on the
    ///                                          struck side (severe from 9)
    ///   bump stop     6 and up                 alignment on that wheel's side (severe from 9)
    ///
    /// The rows are cumulative (a hit rolls its own band and every band
    /// below it). Bumper ratings: 2.2 m/s for the US market's 5-mph bumpers
    /// (origin usa, 1973-82 - the market stand-in until car_facts lands),
    /// 1.5 for chrome (before 1973), 1.1 for everything else.
    ///
    /// The pull acts at once through <see cref="CarController.hitSteerPull"/>,
    /// in ROAD-WHEEL DEGREES toward the struck side; the radiator acts at once
    /// through <see cref="EngineTemp.radiatorCond"/> and a weep; the apply-back
    /// (LifeSim.CrashBank) writes exactly what was rolled - no second roll.
    /// DamageScore keeps its world-up rule untouched, so AI retirement and
    /// the body/paint summary do not move.
    ///
    /// PSX_FAULT_CRASH=0 is today's behaviour from end to end.
    /// </summary>
    public static class CrashStopGap
    {
        /// <summary>The switch. Off: no table at the hit, nothing live, the
        /// apply-back rolls the RG2 impact pool off DamageScore / BottomWorst
        /// and an alignment fault pulls 0.25 of lock, as before.</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_FAULT_CRASH") != "0";

        // ---- where a hit landed (RaceHandoff.WorstHitZone) ----------------
        public const int ZoneNone = 0, ZoneFront = 1, ZoneRear = 2, ZoneSide = 3;
        public static string ZoneName(int z) =>
            z == ZoneFront ? "front" : z == ZoneRear ? "rear" : z == ZoneSide ? "side" : "none";
        public static string SideName(int s) => s < 0 ? "left" : s > 0 ? "right" : "none";

        /// <summary>
        /// Which face of the car a contact point is on, in the car's OWN frame:
        /// <paramref name="local"/> is the impulse-weighted contact point
        /// relative to the body box's centre, <paramref name="half"/> the box's
        /// half extents. Normalised by the box so a long car's flank is still a
        /// flank; the side is the point's own side of the centreline. Never the
        /// struck object's bounds - a 200 m barrier's centre is in front of the
        /// car whichever side it hit.
        /// </summary>
        public static void Classify(Vector3 local, Vector3 half, out int zone, out int side)
        {
            float nx = half.x > 1e-3f ? local.x / half.x : 0f;
            float nz = half.z > 1e-3f ? local.z / half.z : 0f;
            zone = Mathf.Abs(nz) >= Mathf.Abs(nx) ? (nz >= 0f ? ZoneFront : ZoneRear) : ZoneSide;
            side = local.x < 0f ? -1 : 1;
        }

        // ---- bumpers ------------------------------------------------------
        public enum Bumper { Plastic = 0, FiveMph = 1, Chrome = 2 }

        /// <summary>By era and market. The market is the origin until
        /// car_facts carries one (round D): only US-market cars of 1973-82
        /// wore the 5-mph federal bumper.</summary>
        public static Bumper BumperFor(CarSpec spec)
        {
            if (spec == null) return Bumper.Plastic;
            if (spec.modelYear > 0 && spec.modelYear < 1973) return Bumper.Chrome;
            if (spec.origin == "usa" && spec.modelYear >= 1973 && spec.modelYear <= 1982) return Bumper.FiveMph;
            return Bumper.Plastic;
        }

        /// <summary>Below this closing speed a front or rear hit does nothing.</summary>
        public static float RatingMps(Bumper b) =>
            b == Bumper.FiveMph ? 2.2f : b == Bumper.Chrome ? 1.5f : 1.1f;

        /// <summary>The bumper row: a chrome or 5-mph bar dents, a cover cracks.</summary>
        public static string BumperFaultId(Bumper b) => b == Bumper.Plastic ? "bumper_crack" : "bumper_dent";

        // ---- the bands ----------------------------------------------------
        public const float BumperRowMps = 3f, RadiatorMps = 6f, HeavyMps = 10f, HeavySevereMps = 15f;
        public const float SideScuffMps = 1.5f, SideDentMps = 3f, SideHeavyMps = 6f, SideSevereMps = 9f;
        /// <summary>The bump-stop row: LifeRules.BottomFaultMps / BottomSevereMps.</summary>
        public const float BottomMps = 6f, BottomSevereMps = 9f;
        public const float BumperChance = 0.70f, RadiatorChance = 0.60f, HeavyAlignChance = 0.40f, SideAlignChance = 0.50f;

        /// <summary>One incident: contacts within this window of its first one,
        /// in the same zone, are the same crash and share its dice (the same
        /// window CollisionResponder counts incidents in).</summary>
        public const float IncidentWindow = 0.6f;

        // ---- the pull -----------------------------------------------------
        /// <summary>
        /// ROAD-WHEEL DEGREES, set by the drift play check (CrashPlayCheck):
        /// hands-off at 100 km/h the minor band drifts 0.5-1.5 m per 100 m and
        /// the severe band 2-5 m, toward the struck side, with the
        /// counter-steer assist off, standard and full. The old alignment
        /// fault was 0.25 of lock - about 5 degrees of road wheel at 100 km/h,
        /// a hundred times this.
        /// </summary>
        public static float PullDegMinor = 0.042f, PullDegSevere = 0.14f;

        /// <summary>Degrees for an alignment fault of this severity (1 minor,
        /// 2 severe; anything under 1.5 is minor).</summary>
        public static float PullDeg(float severity) => severity >= 1.5f ? PullDegSevere : PullDegMinor;

        // ---- the radiator -------------------------------------------------
        /// <summary>Coolant a fully weeping core (radiatorWeep 1) loses per
        /// second once the system is fully up to pressure. At a cruise the
        /// thermostat holds ~90 C (pressure 0.74 of full), so the median
        /// radiator outcome (-42, weep 0.7) takes the system from full to the
        /// 55% at which cooling starts to fall away in about ten minutes (7 at
        /// the worst, 17 at the mildest): the needle climbs some minutes after
        /// the crash, not at it (the self-test integrates it).</summary>
        public const float WeepPctPerSec = 0.14f;

        /// <summary>What a weeping core loses this second, %/s. Nothing while
        /// the engine is cold (no pressure), building as the thermostat opens,
        /// up to twice as fast boiling hot. Zero weep is zero, exactly.</summary>
        public static float WeepLoss(float weep, float celsius)
        {
            if (weep <= 0f) return 0f;
            float pressure = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(82f, 94f, celsius));
            float hot = 1f + Mathf.Clamp01((celsius - 100f) / 20f);
            return WeepPctPerSec * Mathf.Clamp01(weep) * pressure * hot;
        }

        // ---- outcomes -----------------------------------------------------
        /// <summary>The six outcome classes are these five bits and none.</summary>
        [System.Flags]
        public enum Cls { None = 0, Scuff = 1, Body = 2, Bumper = 4, Radiator = 8, Alignment = 16 }
        public static readonly string[] ClassNames = { "nothing", "scuff", "body", "bumper", "radiator", "alignment" };

        /// <summary>The uniform draws one incident is decided by. Drawn once
        /// when the incident opens; a harder contact in the same incident is
        /// re-read against the SAME draws, so it can only add.</summary>
        public struct Draws
        {
            public float paint, bumper, body, radiator, radAmt, align;
        }

        public static Draws Draw(System.Random r) => new Draws
        {
            paint = (float)r.NextDouble(), bumper = (float)r.NextDouble(), body = (float)r.NextDouble(),
            radiator = (float)r.NextDouble(), radAmt = (float)r.NextDouble(), align = (float)r.NextDouble(),
        };

        public struct Outcome
        {
            public Cls classes;
            /// <summary>Paint, body and radiator condition taken off (0-100 lanes).</summary>
            public float paintLoss, bodyLoss, radiatorLoss;
            /// <summary>How badly the core weeps, 0-1 (EngineTemp.radiatorWeep).</summary>
            public float weep;
            /// <summary>The bumper fault row, or null.</summary>
            public string bumperId;
            /// <summary>-1 left, +1 right; 0 none.</summary>
            public int alignSide;
            /// <summary>0 none, 1 minor, 2 severe.</summary>
            public int alignSeverity;

            public bool Has(Cls c) => (classes & c) != 0;
            public float PullDeg => alignSeverity > 0 ? alignSide * CrashStopGap.PullDeg(alignSeverity) : 0f;

            public override string ToString()
            {
                if (classes == Cls.None) return "nothing";
                var sb = new System.Text.StringBuilder();
                if (Has(Cls.Scuff)) sb.Append("scuff paint -").Append(paintLoss.ToString("0.0")).Append(' ');
                if (Has(Cls.Body)) sb.Append("body -").Append(bodyLoss.ToString("0.0")).Append(' ');
                if (Has(Cls.Bumper)) sb.Append(bumperId).Append(' ');
                if (Has(Cls.Radiator)) sb.Append("radiator -").Append(radiatorLoss.ToString("0")).Append(" weep ")
                                          .Append(weep.ToString("0.00")).Append(' ');
                if (Has(Cls.Alignment)) sb.Append("alignment ").Append(alignSeverity >= 2 ? "severe " : "minor ")
                                          .Append(SideName(alignSide)).Append(' ');
                return sb.ToString().TrimEnd();
            }
        }

        static void Scuff(ref Outcome o, in Draws d)
        {
            o.classes |= Cls.Scuff;
            o.paintLoss += Mathf.Lerp(2f, 5f, d.paint);
        }

        static void Body(ref Outcome o, float loss)
        {
            o.classes |= Cls.Body;
            o.bodyLoss += loss;
        }

        static void Align(ref Outcome o, int side, int severity)
        {
            o.classes |= Cls.Alignment;
            o.alignSide = side < 0 ? -1 : 1;
            o.alignSeverity = severity;
        }

        /// <summary>
        /// THE TABLE. Pure: the same zone, side, speed, bumper and draws give
        /// the same outcome, and for one zone and one set of draws a faster hit
        /// never gives less.
        /// </summary>
        public static Outcome Resolve(int zone, int side, float mps, Bumper bumper, in Draws d)
        {
            var o = new Outcome();
            if (zone == ZoneFront || zone == ZoneRear)
            {
                if (mps < RatingMps(bumper)) return o;
                Scuff(ref o, d);
                if (mps < BumperRowMps) return o;
                if (d.bumper < BumperChance)
                {
                    o.classes |= Cls.Bumper;
                    o.bumperId = BumperFaultId(bumper);
                }
                else Body(ref o, 5f);
                if (zone == ZoneFront && mps >= RadiatorMps && d.radiator < RadiatorChance)
                {
                    o.classes |= Cls.Radiator;
                    o.radiatorLoss = Mathf.Lerp(25f, 60f, d.radAmt);
                    o.weep = o.radiatorLoss / 60f;
                }
                if (mps >= HeavyMps)
                {
                    Body(ref o, Mathf.Lerp(20f, 40f, d.body));
                    if (d.align < HeavyAlignChance) Align(ref o, side, mps >= HeavySevereMps ? 2 : 1);
                }
                return o;
            }
            if (zone == ZoneSide)
            {
                if (mps < SideScuffMps) return o;
                Scuff(ref o, d);
                if (mps < SideDentMps) return o;
                Body(ref o, mps < SideHeavyMps ? Mathf.Lerp(5f, 15f, d.body) : Mathf.Lerp(15f, 30f, d.body));
                if (mps >= SideHeavyMps && d.align < SideAlignChance)
                    Align(ref o, side, mps >= SideSevereMps ? 2 : 1);
            }
            return o;
        }

        /// <summary>The bump-stop row: certain, on the struck wheel's side
        /// (wheels FL FR RL RR; the left ones are even).</summary>
        public static Outcome ResolveBottom(int wheel, float mps)
        {
            var o = new Outcome();
            if (wheel < 0 || mps < BottomMps) return o;
            Align(ref o, wheel % 2 == 0 ? -1 : 1, mps >= BottomSevereMps ? 2 : 1);
            return o;
        }

        /// <summary>Two outcomes of one leg: damage done never un-happens.
        /// Losses add, the weep and the alignment take the worse (a tie keeps
        /// the first side), the first bumper row stays.</summary>
        public static Outcome Merge(Outcome a, Outcome b)
        {
            var o = a;
            o.classes |= b.classes;
            o.paintLoss += b.paintLoss;
            o.bodyLoss += b.bodyLoss;
            o.radiatorLoss += b.radiatorLoss;
            o.weep = Mathf.Max(a.weep, b.weep);
            if (string.IsNullOrEmpty(o.bumperId)) o.bumperId = b.bumperId;
            if (b.alignSeverity > o.alignSeverity) { o.alignSeverity = b.alignSeverity; o.alignSide = b.alignSide; }
            return o;
        }

        // ---- the drive: rolled at the hit, applied at once ------------------
        /// <summary>
        /// One career drive's crash ledger, on the player's responder (armed
        /// by RaceHandoffApplier). Every hit that becomes the leg's hardest is
        /// rolled here the moment it lands and the result goes onto the car at
        /// once: <see cref="CarController.hitSteerPull"/> and the engine's
        /// radiator. What it holds is what the exit stamp hands the apply-back.
        /// </summary>
        public sealed class Ledger
        {
            readonly CarController car;
            readonly EngineTemp temp;
            readonly System.Random rng;
            /// <summary>Tests force the dice through this.</summary>
            public System.Func<Draws> DrawOverride;

            /// <summary>Incidents already over, and the one still taking contacts.</summary>
            public Outcome Closed, Open;
            float openAt = -100f;
            int openZone;
            Draws openDraws;
            float radiatorApplied;

            /// <summary>How many times the table was read (for the play check).</summary>
            public int Rolls { get; private set; }

            public Ledger(CarController car, EngineTemp temp, int seed)
            {
                this.car = car;
                this.temp = temp;
                rng = new System.Random(seed);
            }

            public Outcome Total => Merge(Closed, Open);

            /// <summary>A hit that just became the leg's hardest.</summary>
            public void Hit(int zone, int side, float mps, Bumper bumper, float now)
            {
                if (zone == ZoneNone) return;
                bool sameIncident = now - openAt <= IncidentWindow && zone == openZone;
                if (!sameIncident)
                {
                    Closed = Merge(Closed, Open);
                    Open = new Outcome();
                    openDraws = DrawOverride != null ? DrawOverride() : Draw(rng);
                    openZone = zone;
                    openAt = now;
                }
                Open = Resolve(zone, side, mps, bumper, openDraws);
                Rolls++;
                ApplyLive();
            }

            /// <summary>A bump-stop strike that just became the leg's hardest.</summary>
            public void Bottom(int wheel, float mps)
            {
                var b = ResolveBottom(wheel, mps);
                if (b.classes == Cls.None) return;
                Closed = Merge(Closed, b);
                Rolls++;
                ApplyLive();
            }

            /// <summary>Onto the car, now. Absolute from the total, so applying
            /// twice is applying once.</summary>
            void ApplyLive()
            {
                var t = Total;
                if (car != null) car.hitSteerPull = t.PullDeg;
                if (temp == null) return;
                float more = t.radiatorLoss - radiatorApplied;
                if (more > 0f)
                {
                    temp.radiatorHit += more;
                    temp.radiatorCond = Mathf.Max(0f, temp.radiatorCond - more);
                    radiatorApplied = t.radiatorLoss;
                }
                if (t.weep > temp.radiatorWeep) temp.radiatorWeep = t.weep;
            }
        }

        /// <summary>
        /// Give the player's car a ledger when this drive banks into a
        /// career: the same guards LifeRules.BankSeizureNow keeps (not a
        /// standalone editor race, not a seller's car on a test drive, not the
        /// CITY edition or ARCADE). False when nothing was armed.
        /// </summary>
        public static bool Arm(CarController car)
        {
            if (!On || car == null) return false;
            if (!RaceHandoff.FromLifeSim || RaceHandoff.TestDrive || !Edition.HasCareer) return false;
            var resp = car.GetComponent<CollisionResponder>();
            if (resp == null) return false;
            // Unseeded in play, like every other fault roll here (seeded only
            // in tests, through DrawOverride).
            resp.Ledger = new Ledger(car, car.GetComponent<EngineTemp>(),
                                     System.Environment.TickCount ^ UnityEngine.Random.Range(1, int.MaxValue));
            car.hitSteerPull = 0f;
            return true;
        }

        /// <summary>The bumper the car being driven wears, read at the hit (the
        /// bench can change the car mid-drive).</summary>
        public static Bumper LiveBumper() => BumperFor(CarCatalog.Get(RaceHandoff.CarSpecId));

        /// <summary>
        /// Everything the responder knows, into the handoff - the one place
        /// both exits (the finish line and CityMode's exit stamp) write it, so
        /// they cannot disagree. DamageScore, HardHits and the bump-stop tally
        /// are written exactly as they were.
        /// </summary>
        public static void StampHandoff(CollisionResponder r)
        {
            RaceHandoff.DamageScore = r != null ? r.DamageScore : 0f;
            RaceHandoff.HardHits = r != null ? r.HardHits : 0;
            RaceHandoff.BottomScore = r != null ? r.BottomScore : 0f;
            RaceHandoff.BottomWorst = r != null ? r.WorstBottom : 0f;
            RaceHandoff.WorstHitMps = r != null ? r.WorstHit : 0f;
            RaceHandoff.WorstHitZone = r != null ? r.WorstHitZone : ZoneNone;
            RaceHandoff.WorstHitSide = r != null ? r.WorstHitSide : 0;
            RaceHandoff.BottomWorstWheel = r != null ? r.WorstBottomWheel + 1 : 0;
            var ledger = r != null ? r.Ledger : null;
            StampOutcome(ledger != null, ledger != null ? ledger.Total : new Outcome());
        }

        /// <summary>The rolled outcome alone, into the handoff (the self-test
        /// stands in for a drive through this).</summary>
        public static void StampOutcome(bool rolled, Outcome t)
        {
            RaceHandoff.CrashRolled = rolled;
            RaceHandoff.CrashClasses = (int)t.classes;
            RaceHandoff.CrashPaintLoss = t.paintLoss;
            RaceHandoff.CrashBodyLoss = t.bodyLoss;
            RaceHandoff.CrashRadiatorLoss = t.radiatorLoss;
            RaceHandoff.CrashBumperId = t.bumperId;
            RaceHandoff.CrashAlignSide = t.alignSide;
            RaceHandoff.CrashAlignSeverity = t.alignSeverity;
        }
    }
}
