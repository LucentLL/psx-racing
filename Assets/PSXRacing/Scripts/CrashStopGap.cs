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
    /// 1.5 for chrome (before 1973), 1.1 for everything else. A hit centred
    /// on the nose or tail (inside <see cref="CentreBand"/>) has no struck
    /// side, so it rolls no alignment.
    ///
    /// ONLY THE LEG'S HARDEST HIT IS ROLLED: a hit harder than every one
    /// before it this leg (CollisionResponder.RecordHit); a softer crash
    /// later in the leg rolls nothing. The leg's outcome is the worst of what
    /// was rolled, lane by lane, never a sum (<see cref="Merge"/>).
    ///
    /// The pull acts at once through <see cref="CarController.hitSteerPull"/>,
    /// in ROAD-WHEEL DEGREES toward the struck side, combined with the
    /// alignment the car already carries by one rule (<see cref="CombinePull"/>)
    /// that the apply-back banks by too; the radiator acts at once through
    /// <see cref="EngineTemp.radiatorCond"/> and a weep; the apply-back
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
        public static string SideName(int s) => s < 0 ? "left" : s > 0 ? "right" : "centre";

        /// <summary>
        /// The middle of the car, as a share of its half-width either side of
        /// the centreline (0.2 is about 18 cm on a 1.75 m car). A hit whose
        /// patch is centred inside it - a square nose into a wall, a pole dead
        /// ahead - has NO struck side: side 0, and so no alignment (R1a's rule:
        /// a centre-front hit bends no corner). Without it the side of a square
        /// hit came from rounding noise in the contact points: the owner's
        /// coin-flip pull again, only smaller.
        /// </summary>
        public const float CentreBand = 0.2f;

        /// <summary>
        /// Which face of the car a contact patch is on, in the car's OWN frame:
        /// <paramref name="local"/> is the patch's centre (<see cref="PatchCentre"/>)
        /// relative to the body box's centre, <paramref name="half"/> the box's
        /// half extents. Normalised by the box so a long car's flank is still a
        /// flank; the side is the patch's own side of the centreline, 0 inside
        /// <see cref="CentreBand"/>. Never the struck object's bounds - a 200 m
        /// barrier's centre is in front of the car whichever side it hit.
        /// </summary>
        public static void Classify(Vector3 local, Vector3 half, out int zone, out int side)
        {
            float nx = half.x > 1e-3f ? local.x / half.x : 0f;
            float nz = half.z > 1e-3f ? local.z / half.z : 0f;
            zone = Mathf.Abs(nz) >= Mathf.Abs(nx) ? (nz >= 0f ? ZoneFront : ZoneRear) : ZoneSide;
            side = Mathf.Abs(nx) < CentreBand ? 0 : (nx < 0f ? -1 : 1);
        }

        /// <summary>
        /// Where a contact patch is, in the car's own frame: the centre of the
        /// box around the contact points of the pair (all of them - the
        /// player car's continuous collision keeps them within the 2 cm
        /// contact distance, and dropping the ones a little further out would
        /// only bias a nearly square hit toward its leading corner).
        ///
        /// NOT WEIGHTED BY IMPULSE. A car stopped by a wall across part of its
        /// nose pivots about the wall's inner edge, and the solver piles the
        /// impulse onto that edge: the weighted point read -0.27 m for a wall
        /// covering the left 35% of an RX-7's nose (inner edge -0.28 m), so a
        /// wall over more than half the nose would read on the far side of
        /// the centreline. The patch's extent does not care where the impulse
        /// went: a full-width wall is centred, a wall over the left 60% is left.
        /// </summary>
        public static Vector3 PatchCentre(Vector3[] points, int n)
        {
            if (points == null || n <= 0) return Vector3.zero;
            n = Mathf.Min(n, points.Length);
            Vector3 lo = points[0], hi = points[0];
            for (int i = 1; i < n; i++)
            {
                lo = Vector3.Min(lo, points[i]);
                hi = Vector3.Max(hi, points[i]);
            }
            return (lo + hi) * 0.5f;
        }

        // ---- bumpers ------------------------------------------------------
        public enum Bumper { Plastic = 0, FiveMph = 1, Chrome = 2 }

        /// <summary>By era and market. The market is the origin until
        /// car_facts carries one (round D): only US-market cars of 1973-82
        /// wore the 5-mph federal bumper.</summary>
        public static Bumper BumperFor(CarSpec spec)
        {
            if (spec == null) return Bumper.Plastic;
            // R1a: the car's own facts row (round D: chrome / 5mph / 2.5mph /
            // plastic, by market and year). A car with no row: era and origin.
            var facts = CarFacts.Get(spec);
            if (facts != null && !string.IsNullOrEmpty(facts.bumperEra))
                return facts.bumperEra == "chrome" ? Bumper.Chrome
                     : facts.bumperEra == "5mph" ? Bumper.FiveMph : Bumper.Plastic;
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

        /// <summary>The band an alignment row's severity reads as, the same
        /// split <see cref="PullDeg"/> makes: 2 severe, else 1 minor.</summary>
        public static int Band(float severity) => severity >= 1.5f ? 2 : 1;

        /// <summary>
        /// ONE CAR, ONE ALIGNMENT: does a new alignment of band
        /// <paramref name="newBand"/> replace the one the car carries (band
        /// <paramref name="carriedBand"/>, 0 none)? The worse band wins and a
        /// tie goes to the NEWER hit, so the car pulls toward the latest
        /// crash. One rule for the leg's own crashes (<see cref="Merge"/>), the
        /// live pull (<see cref="CombinePull"/>) and the apply-back
        /// (LifeSim.CrashBank): the pull felt after a crash is the pull the
        /// next drive starts with.
        /// </summary>
        public static bool NewAlignWins(int carriedBand, int newBand) => newBand > 0 && newBand >= carriedBand;

        /// <summary>
        /// The pull on the road wheels, live: this drive's crash pull
        /// (<paramref name="hitDeg"/>, CarController.hitSteerPull) against the
        /// alignment the car came out with (<paramref name="carriedDeg"/>,
        /// faultSteerPullDeg), by <see cref="NewAlignWins"/> - never their sum,
        /// which doubled a same-side pull and cancelled an opposite one for the
        /// rest of the leg, then banked something else. No crash this drive:
        /// the carried pull, exactly (a sound car: 0, exactly).
        /// </summary>
        public static float CombinePull(float carriedDeg, float hitDeg) =>
            hitDeg != 0f && Mathf.Abs(hitDeg) >= Mathf.Abs(carriedDeg) ? hitDeg : carriedDeg;

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
                    // On the STRUCK side - and a hit square in the middle has
                    // none (CentreBand), so it rolls no alignment.
                    if (side != 0 && d.align < HeavyAlignChance) Align(ref o, side, mps >= HeavySevereMps ? 2 : 1);
                }
                return o;
            }
            if (zone == ZoneSide)
            {
                if (mps < SideScuffMps) return o;
                Scuff(ref o, d);
                if (mps < SideDentMps) return o;
                Body(ref o, mps < SideHeavyMps ? Mathf.Lerp(5f, 15f, d.body) : Mathf.Lerp(15f, 30f, d.body));
                if (side != 0 && mps >= SideHeavyMps && d.align < SideAlignChance)
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

        /// <summary>
        /// Two outcomes of one leg, <paramref name="b"/> the LATER. The leg's
        /// outcome is the WORST of what was rolled, lane by lane, and never a
        /// sum: summed, a 6.5 then a 7 m/s nose-in took up to 120 off the
        /// radiator where the table's band stops at 60, and the same two hits
        /// the other way round took one roll's worth. Nothing applied live is
        /// taken back: the classes gather, each loss and the weep take the
        /// larger, the first bumper row stays, and the alignment follows
        /// <see cref="NewAlignWins"/> (a tie to the later hit).
        /// </summary>
        public static Outcome Merge(Outcome a, Outcome b)
        {
            var o = a;
            o.classes |= b.classes;
            o.paintLoss = Mathf.Max(a.paintLoss, b.paintLoss);
            o.bodyLoss = Mathf.Max(a.bodyLoss, b.bodyLoss);
            o.radiatorLoss = Mathf.Max(a.radiatorLoss, b.radiatorLoss);
            o.weep = Mathf.Max(a.weep, b.weep);
            if (string.IsNullOrEmpty(o.bumperId)) o.bumperId = b.bumperId;
            if (NewAlignWins(o.alignSeverity, b.alignSeverity))
            {
                o.alignSeverity = b.alignSeverity;
                o.alignSide = b.alignSide;
            }
            return o;
        }

        // ---- the drive: rolled at the hit, applied at once ------------------
        /// <summary>
        /// One career drive's crash ledger, on the player's responder (armed
        /// by RaceHandoffApplier). Every hit that becomes the leg's hardest
        /// (CollisionResponder.RecordHit - only those) is rolled here the
        /// moment it lands and the result goes onto the car at once:
        /// <see cref="CarController.hitSteerPull"/> and the engine's radiator.
        /// What it holds is what the exit stamp hands the apply-back.
        /// </summary>
        public sealed class Ledger
        {
            readonly CarController car;
            readonly EngineTemp temp;
            readonly System.Random rng;
            /// <summary>Tests force the dice through this.</summary>
            public System.Func<Draws> DrawOverride;

            /// <summary>Incidents already over, and the latest one (still
            /// taking contacts inside its window).</summary>
            public Outcome Closed, Open;
            float openAt = -100f, openMps;
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

            /// <summary>A hit that just became the leg's hardest. A contact of
            /// the same crash (same zone, inside the window) is read against
            /// that crash's dice again only when it is harder than the crash's
            /// hardest so far - a softer one adds nothing - and what it reads
            /// is merged on, so it can only add.</summary>
            public void Hit(int zone, int side, float mps, Bumper bumper, float now)
            {
                if (zone == ZoneNone) return;
                bool sameIncident = now - openAt <= IncidentWindow && zone == openZone;
                if (sameIncident && mps <= openMps) return;
                if (!sameIncident)
                {
                    Closed = Merge(Closed, Open);
                    Open = new Outcome();
                    openDraws = DrawOverride != null ? DrawOverride() : Draw(rng);
                    openZone = zone;
                    openAt = now;
                }
                openMps = mps;
                Open = Merge(Open, Resolve(zone, side, mps, bumper, openDraws));
                Rolls++;
                ApplyLive();
            }

            /// <summary>A bump-stop strike that just became the leg's hardest.
            /// Merged after everything so far (Open is the latest crash), so an
            /// alignment tie goes to the strike, the later of the two.</summary>
            public void Bottom(int wheel, float mps)
            {
                var b = ResolveBottom(wheel, mps);
                if (b.classes == Cls.None) return;
                Open = Merge(Open, b);
                Rolls++;
                ApplyLive();
            }

            /// <summary>Onto the car, now. Absolute from the total, so applying
            /// twice is applying once. The pull is THIS DRIVE'S crash pull alone;
            /// CarController combines it with the alignment the car carries
            /// (<see cref="CombinePull"/>).</summary>
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
            // R1a: the located model replaces this table at the hit
            // (PSX_FAULT_LOCATED=0 = this table, as before).
            if (DamageModel.Live) return DamageLedger.Arm(car, resp);
            // Unseeded in play, like every other fault roll here (seeded only
            // in tests, through DrawOverride) - and seeded from the OS, never
            // from UnityEngine.Random: arming a drive must not take a draw from
            // the sequence the rest of the game shares (PerfTour seeds it with
            // InitState and expects its drives to replay).
            resp.Ledger = new Ledger(car, car.GetComponent<EngineTemp>(), System.Guid.NewGuid().GetHashCode());
            car.hitSteerPull = 0f;
            return true;
        }

        /// <summary>
        /// A DIFFERENT CAR under the player mid-drive (RaceHandoffApplier.SwapCar,
        /// the debug bench's DRIVE): it starts clean - no crash pull, no cracked
        /// core, no weep, its own hardest hit and its own ledger - instead of
        /// driving on with the old car's crash and banking it onto the new car
        /// at the exit. The old car's rolled outcome is banked onto the old car
        /// first, by the caller (DebugCarOps.DriveCar). Nothing at all on a car
        /// with no ledger (the switch off, or not a career drive): that path is
        /// today's exactly. Call it BEFORE the applier re-reads the cooling
        /// system, which subtracts radiatorHit.
        /// </summary>
        public static void StartClean(CarController car)
        {
            if (car == null) return;
            var resp = car.GetComponent<CollisionResponder>();
            // R1a: the old car's damage is in its own file already (by its
            // own id); the new car gets its own ledger, reading its own rows.
            if (resp != null && resp.Damage != null && resp.Damage.Banks)
            {
                resp.Damage.Flush();
                resp.Damage = null;
                resp.StartOverForNewCar();
                car.hitSteerPull = 0f;
                var t = car.GetComponent<EngineTemp>();
                if (t != null) { t.radiatorHit = 0f; t.radiatorWeep = 0f; }
                Arm(car);
                return;
            }
            if (resp == null || resp.Ledger == null) return;
            resp.StartOverForNewCar();
            resp.Ledger = null;
            car.hitSteerPull = 0f;
            Arm(car);
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) { temp.radiatorHit = 0f; temp.radiatorWeep = 0f; }
        }

        /// <summary>The bumper the car being driven wears, read at the hit (the
        /// bench can change the car mid-drive).</summary>
        public static Bumper LiveBumper() => BumperFor(CarCatalog.Get(RaceHandoff.CarSpecId));

        /// <summary>
        /// Everything the responder knows, into the handoff - the one place
        /// both exits (the finish line and the city exit, <see cref="StampCityExit"/>)
        /// write it, so they cannot disagree. DamageScore, HardHits and the
        /// bump-stop tally are written exactly as they were.
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
            // R1a: every exit stamp (the finish line, QUIT / the tow, the city
            // exits) folds the drive's damage into the career, once by hitId.
            if (r != null && r.Damage != null && r.Damage.Banks) DamageLedger.FoldNow(r.Damage);
        }

        /// <summary>R1a: fold the car's damage now (the pause menu's EXIT and
        /// RESTART, whatever else they stamp). Nothing on a car with no ledger.</summary>
        public static int FoldDamage(CarController car)
        {
            var r = car != null ? car.GetComponent<CollisionResponder>() : null;
            return r != null && r.Damage != null && r.Damage.Banks ? DamageLedger.FoldNow(r.Damage) : 0;
        }

        /// <summary>
        /// The city exit: called right after CityMode.StampExitResult by its two
        /// callers (PauseMenu EXIT, TownVenue's doors). StampExitResult still
        /// writes DamageScore, HardHits and the bump-stop tally as it always
        /// has; this writes the same values again plus the located hardest hit
        /// and what was rolled at it, through the finish line's own writer.
        /// It lives here and not in CityMode so that no file under
        /// Scripts/City changes: the city tile bake fingerprints every .cs
        /// there (Editor/CityTileBake.cs CodeDirs), and an edit to CityMode
        /// would make the PC build re-bake Charlotte. Nothing without a city
        /// session or a player car, where StampExitResult stamps nothing either.
        /// </summary>
        public static void StampCityExit()
        {
            var city = City.CityMode.Instance;
            if (city == null || city.player == null) return;
            StampHandoff(city.player.GetComponent<CollisionResponder>());
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
