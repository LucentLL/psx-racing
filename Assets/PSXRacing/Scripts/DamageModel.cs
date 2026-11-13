using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// R1a "HITS LAND WHERE YOU HIT", part 1 (Fault_System_Design_2026-11-11
    /// §3.1, §2 save rules, the R1a row of §10; OWNER'S ANSWERS 1 and 8).
    ///
    /// WHERE. Every contact is taken into the car's OWN frame (the body box,
    /// CarBody.box): FRONT, REAR, SIDE left/right, the four CORNERS (a patch
    /// within tyre radius + 0.15 m of a wheel hub, in plan - a corner wins over
    /// a panel), UNDER (the car pushed UP from below, outside the tyre patches)
    /// and ROOF (pushed DOWN from above, or a horizontal hit above the
    /// beltline: glass and pillars). The world-up test that throws roof
    /// landings, nose digs and belly strikes away stays only for DamageScore,
    /// so AI retirement and the carHP summary do not move.
    ///
    /// HOW HARD. Delta-V over the crash PULSE: the normal impulse summed over
    /// the first 0.2 s after a collider's Enter, over the car's mass. After
    /// 0.2 s a held contact is a scrape (cosmetic, never adds to delta-V).
    /// Capped at closing speed x m_other / (m_car + m_other) x 1.2 (traffic
    /// cars at 1,400 kg, a wall infinite). Barrier sections within 4 m and
    /// 0.6 s of each other are ONE incident, whose delta-V is its hardest
    /// pulse. Trees and poles count a band higher, a glancing hit (under 30
    /// degrees) a band lower (at the bottom band: a scuff and no more).
    ///
    /// WHAT IT BREAKS. The zone tables (REALISTIC; cumulative - a hit rolls its
    /// band and every band below it, each row on its own die, re-read against
    /// the same dice as the pulse grows, so an incident only ever adds). Part 1
    /// writes the starred hit rows panel_damage, bumper_bar, lamp_out and
    /// radiator_leak plus exhaust_damage and roof_glass; a corner's toe / tie
    /// rod / arm column is the existing alignment row (R0a's degrees) until
    /// R1b's per-wheel corners; scuffs and dents move the paint and body lanes
    /// as R0a's did (value code reads them). Rows of later rounds (SRS, oil
    /// pan, frame, fan, condenser, mounts) are not rolled.
    ///
    /// LIVE. The radiator weeps at once through EngineTemp.radiatorCond /
    /// radiatorWeep (0.5 / 2 / 6 % a minute by stage), the pull through
    /// CarController.hitSteerPull, a struck exhaust takes its power at once.
    /// Banked AT THE EVENT: each incident writes the small per-car damage file
    /// (LifeSim.DamageBank, debounced 2 s), folded into the career exactly once
    /// by hitId at every exit stamp, at QUIT / the tow, at the apply-back and at
    /// the next Load. ROLL, DROP and WATER are incidents too (no respawn any
    /// more in a career: they are banked when they happen, not before a reset).
    ///
    /// PSX_FAULT_LOCATED=0 = R0a's stop-gap table at the hit (the base's
    /// behaviour); PSX_FAULT_CRASH=0 = the crash from before R0a (both off).
    /// </summary>
    public static class DamageModel
    {
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_FAULT_LOCATED") != "0";
        /// <summary>The located model runs: its own switch and R0a's (CRASH=0
        /// is the crash from before R0a, which owns the apply-back then).</summary>
        public static bool Live => On && CrashStopGap.On;

        /// <summary>A race harness's census: every responder runs a ledger that
        /// resolves and reports its incidents but banks nothing.</summary>
        public static bool CensusAll;
        public static event System.Action<CollisionResponder, Incident> IncidentReported;
        internal static void Report(CollisionResponder r, Incident i) => IncidentReported?.Invoke(r, i);

        // ---- zones (FaultCause codes: 1 front 2 rear 3 side 4 under; + roof, corner)
        public const int ZNone = 0, ZFront = 1, ZRear = 2, ZSide = 3, ZUnder = 4, ZRoof = 5, ZCorner = 6;
        public static string ZoneName(int z) =>
            z == ZFront ? "front" : z == ZRear ? "rear" : z == ZSide ? "side" : z == ZUnder ? "under" :
            z == ZRoof ? "roof" : z == ZCorner ? "corner" : "none";
        public static readonly string[] CornerNames = { "FL", "FR", "RL", "RR" };

        // ---- incident kinds
        public const int KHit = 0, KBottom = 1, KRoll = 2, KDrop = 3, KWater = 4;
        public static string KindName(int k) =>
            k == KBottom ? "bump stop" : k == KRoll ? "roll" : k == KDrop ? "drop" : k == KWater ? "water" : "hit";

        // ---- the pulse
        public const float PulseSeconds = 0.2f, MergeSeconds = 0.6f, MergeMetres = 4f;
        public const float TrafficKg = 1400f, CapFactor = 1.2f;
        /// <summary>A crushed bumper bar: the next hit in its zone counts x1.3.</summary>
        public const float BarCrushMult = 1.3f;
        public const float CornerPad = 0.15f, VerticalDot = 0.7f, GlancingIncidence = 0.5f;
        /// <summary>The beltline when a shell has no measured cowl: this share
        /// of the box's half-height above its centre.</summary>
        public const float BeltFrac = 0.35f;

        // ---- rolls, drops, water
        /// <summary>A fall shorter than this is a crest jump, not a DROP.</summary>
        public const float DropMinM = 1.5f;
        /// <summary>What springs and stops take before the floor pan meets the
        /// ground: a DROP's UNDER row is read at landing speed less this.</summary>
        public const float SuspensionAbsorbMps = 3f;
        public const float SeaDepthM = 0.5f;

        // ---- the rows part 1 writes
        public const string PanelId = "panel_damage", BarId = "bumper_bar", LampId = "lamp_out",
                            RadiatorId = "radiator_leak", ExhaustId = "exhaust_damage", GlassId = "roof_glass";
        public static readonly string[] RowIds = { PanelId, BarId, LampId, RadiatorId, ExhaustId, GlassId };
        /// <summary>Coolant lost, % a minute above ~90 C, by radiator_leak stage.</summary>
        public static readonly float[] LeakPctMin = { 0f, 0.5f, 2f, 6f };
        public static float LeakFor(float stage) => LeakPctMin[Mathf.Clamp(Mathf.RoundToInt(stage), 0, 3)];
        /// <summary>A leak rate as EngineTemp.radiatorWeep (R0a's 0-1 weep).</summary>
        public static float WeepFor(float pctPerMin) =>
            pctPerMin <= 0f ? 0f : Mathf.Clamp01(pctPerMin / (CrashStopGap.WeepPctPerSec * 60f));
        /// <summary>A struck exhaust's power (design: x0.96-0.98).</summary>
        public const float ExhaustPower = 0.97f;

        // =================== the shell ===================
        /// <summary>The car's geometry in its body box's frame, relative to the
        /// box centre: half extents, hubs (FL FR RL RR), tyre radius, beltline.</summary>
        public sealed class Shell
        {
            public Vector3 half = new Vector3(0.86f, 0.5f, 2.05f);
            public readonly Vector3[] hubs = new Vector3[4];
            public float tyreR = 0.31f, beltY = 0.175f;

            /// <summary>From a model def alone (the self-test's 52 shells).</summary>
            public static Shell FromDef(CarModelDef def)
            {
                var sh = new Shell();
                if (def == null) return sh;
                Vector3 c = def.colliderCenter;
                sh.half = def.colliderSize * 0.5f;
                float tf = def.trackWidth * 0.5f;
                float tr = (def.trackRear > 0f ? def.trackRear : def.trackWidth) * 0.5f;
                float rf = def.wheelRadius, rr = def.tyreRadiusRear > 0f ? def.tyreRadiusRear : def.wheelRadius;
                float wb = def.wheelbase * 0.5f;
                sh.hubs[0] = new Vector3(-tf, rf, wb) - c;
                sh.hubs[1] = new Vector3(tf, rf, wb) - c;
                sh.hubs[2] = new Vector3(-tr, rr, -wb) - c;
                sh.hubs[3] = new Vector3(tr, rr, -wb) - c;
                sh.tyreR = rf;
                sh.beltY = BeltOf(def, sh.half.y);
                return sh;
            }
        }

        /// <summary>The beltline above the box centre: the shell's measured cowl
        /// (the foot of the windscreen), else <see cref="BeltFrac"/> of the half-height.</summary>
        public static float BeltOf(CarModelDef def, float halfY)
        {
            float belt = def != null && def.cowlY > 0f ? def.cowlY - def.colliderCenter.y : halfY * BeltFrac;
            return Mathf.Clamp(belt, 0.1f * halfY, 0.8f * halfY);
        }

        static float Plan(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// WHERE a contact patch is. <paramref name="p"/>: the patch centre in
        /// the box frame relative to its centre; <paramref name="n"/>: the push
        /// on the car in the same frame (unit). False = a tyre's patch with the
        /// car upright (the suspension's and the bump stops', not the ledger's).
        /// </summary>
        public static bool Classify(Shell sh, Vector3 p, Vector3 n, bool upright, out int zone, out int side, out int corner)
        {
            zone = ZNone; corner = -1;
            float nx = sh.half.x > 1e-3f ? p.x / sh.half.x : 0f;
            side = Mathf.Abs(nx) < CrashStopGap.CentreBand ? 0 : (nx < 0f ? -1 : 1);
            if (n.y > VerticalDot)
            {
                if (upright)
                    for (int i = 0; i < 4; i++)
                        if (Plan(p, sh.hubs[i]) <= sh.tyreR + CornerPad) return false;
                zone = ZUnder;
                return true;
            }
            if (n.y < -VerticalDot || p.y > sh.beltY) { zone = ZRoof; return true; }
            for (int i = 0; i < 4; i++)
                if (Plan(p, sh.hubs[i]) <= sh.tyreR + CornerPad && p.y <= sh.hubs[i].y + sh.tyreR + CornerPad)
                {
                    zone = ZCorner; corner = i; side = i % 2 == 0 ? -1 : 1;
                    return true;
                }
            CrashStopGap.Classify(p, sh.half, out zone, out side);
            return true;
        }

        /// <summary>A tree trunk or a pole: static, tall and narrow.</summary>
        public static bool IsNarrow(Collider col)
        {
            if (col == null || col.attachedRigidbody != null) return false;
            if (col is CapsuleCollider cap) return cap.radius * Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.z) < 0.6f;
            var b = col.bounds;
            return b.size.y > 1.5f && b.size.x < 1.2f && b.size.z < 1.2f;
        }

        // =================== the bands ===================
        /// <summary>The table band a delta-V reads as in a zone. 0 = nothing.
        /// Front/rear from the bumper's rating; side, corner and roof from 1.5;
        /// under from 1 (1-2 is sparks only).</summary>
        public static int Band(int zone, float dv, CrashStopGap.Bumper bumper, bool narrow, bool glancing, bool barCrushed)
        {
            float v = dv * (barCrushed && (zone == ZFront || zone == ZRear) ? BarCrushMult : 1f);
            int b, max;
            if (zone == ZFront || zone == ZRear)
            {
                b = v < CrashStopGap.RatingMps(bumper) ? 0 : v < 3f ? 1 : v < 5f ? 2 : v < 8f ? 3 : v < 10f ? 4 : 5;
                max = 5;
            }
            else if (zone == ZUnder) { b = v < 1f ? 0 : v < 2f ? 1 : v < 3.5f ? 2 : 3; max = 3; }
            else if (zone == ZRoof) { b = v < 1.5f ? 0 : v < 3f ? 1 : v < 5f ? 2 : 3; max = 3; }
            else { b = v < 1.5f ? 0 : v < 3f ? 1 : v < 5f ? 2 : v < 8f ? 3 : 4; max = 4; }
            if (b > 0 && narrow) b++;
            if (b > 1 && glancing && zone != ZUnder && zone != ZRoof) b--;
            return Mathf.Clamp(b, 0, max);
        }

        /// <summary>A bump-stop strike as a CORNER column: 6 / 8 / 10 m/s.</summary>
        public static int BottomBand(float mps) => mps < 6f ? 0 : mps < 8f ? 1 : mps < 10f ? 2 : 3;

        /// <summary>A DROP's UNDER band from its landing speed (the floor pan
        /// meets the ground at landing less the suspension's share; over 8 m/s
        /// the frame check).</summary>
        public static int DropBand(float landing)
        {
            float v = landing - SuspensionAbsorbMps;
            int b = v < 1f ? 0 : v < 2f ? 1 : v < 3.5f ? 2 : 3;
            return landing > 8f ? 4 : b;
        }

        /// <summary>A ROLL's ROOF band: its hardest roof pulse, and at least the
        /// first band once it went over onto the roof (two quarter-turns).</summary>
        public static int RollBand(float roofDv, int quarters)
        {
            int b = Band(ZRoof, roofDv, CrashStopGap.Bumper.Plastic, false, false, false);
            if (quarters >= 2) b = Mathf.Max(b, 1);
            return b;
        }

        // =================== resolve: the tables ===================
        public const int Dice = 12;
        // die index per row: the same row always reads the same die
        const int DPaint = 0, DCover = 1, DBar = 2, DLamp = 3, DHood = 4, DRad = 5, DExh = 6, DAlign = 7,
                  DBody = 8, DGlass = 9, DMag = 10, DWater = 11;

        public static float[] Draw(System.Random r)
        {
            var d = new float[Dice];
            for (int i = 0; i < Dice; i++) d[i] = (float)r.NextDouble();
            return d;
        }

        static void Scuff(Outcome o, float[] d) => o.paintLoss = Mathf.Max(o.paintLoss, Mathf.Lerp(2f, 5f, d[DPaint]));
        static void Body(Outcome o, float a, float b, float[] d) => o.bodyLoss = Mathf.Max(o.bodyLoss, Mathf.Lerp(a, b, d[DBody]));

        static void AddRow(Outcome o, string id, int stage, float mag, int zone, int side, int corner)
        {
            for (int i = 0; i < o.rows.Count; i++)
                if (o.rows[i].id == id && o.rows[i].zone == zone && o.rows[i].side == side)
                {
                    if (stage > o.rows[i].stage) o.rows[i] = new Row { id = id, stage = stage, mag = mag, zone = zone, side = side, corner = corner };
                    return;
                }
            o.rows.Add(new Row { id = id, stage = stage, mag = mag, zone = zone, side = side, corner = corner });
        }

        static void Radiator(Outcome o, int stage, float[] d)
        {
            for (int i = 0; i < o.rows.Count; i++) if (o.rows[i].id == RadiatorId && o.rows[i].stage >= stage) return;
            AddRow(o, RadiatorId, stage, LeakPctMin[stage], ZFront, 0, -1);
            o.leakPctMin = LeakPctMin[stage];
            float lo = stage == 1 ? 10f : stage == 2 ? 25f : 45f, hi = stage == 1 ? 20f : stage == 2 ? 45f : 60f;
            o.radiatorLoss = Mathf.Lerp(lo, hi, d[DMag]);
        }

        static void Align(Outcome o, int side, int severity)
        {
            if (!CrashStopGap.NewAlignWins(o.alignSeverity, severity)) return;
            o.alignSeverity = severity;
            o.alignSide = side < 0 ? -1 : 1;
        }

        /// <summary>
        /// THE TABLES, pure: the same incident (kind, zone, side, corner, band,
        /// bumper, glancing) and dice give the same outcome, and for one set of
        /// dice a higher band never gives less. Written onto inc.outcome.
        /// </summary>
        public static void Resolve(Incident inc, CrashStopGap.Bumper bumper)
        {
            var o = inc.outcome;
            o.Clear();
            var d = inc.draws ?? new float[Dice];
            int b = inc.band, z = inc.zone, s = inc.side;
            if (inc.kind == KWater)
            {
                o.flood = true;
                o.hydrolock = inc.running && d[DWater] < 0.5f;
                return;
            }
            if (b <= 0) return;
            switch (z)
            {
                case ZFront:
                case ZRear:
                {
                    bool front = z == ZFront;
                    Scuff(o, d);
                    if (b == 1 && inc.glancing) return;          // a glancing rub: a scuff and no more
                    float panelMag = Mathf.Lerp(1f, 2f, d[DMag]);
                    if (bumper == CrashStopGap.Bumper.Chrome)
                    {
                        // chrome dents from 1.5 m/s but shields the body to 3
                        if (d[DCover] < 0.30f) AddRow(o, PanelId, 1, panelMag, z, s, -1);
                    }
                    else
                    {
                        if (d[DCover] < 0.30f) AddRow(o, PanelId, 1, panelMag, z, s, -1);    // cover cracked
                        if (d[DBar] < 0.25f) AddRow(o, BarId, 1, BarCrushMult, z, 0, -1);      // bar crushed (hidden)
                    }
                    if (b < 2) return;
                    if (d[DCover] < 0.80f || d[DHood] < 0.30f)                                  // cover; hood / latch / fender
                        AddRow(o, PanelId, 2, Mathf.Lerp(3f, 5f, d[DMag]), z, s, -1);
                    if (bumper != CrashStopGap.Bumper.Chrome && d[DBar] < 0.60f) AddRow(o, BarId, 1, BarCrushMult, z, 0, -1);
                    if (d[DLamp] < (front ? 0.40f : 0.50f))                                    // head lamp / pop-up, or a tail lamp
                    {
                        int ls = s != 0 ? s : (d[DLamp] < (front ? 0.20f : 0.25f) ? -1 : 1);
                        AddRow(o, LampId, 1, 0f, z, ls, -1);
                    }
                    if (front && d[DRad] < 0.15f) Radiator(o, 1, d);                           // radiator weep
                    if (!front && d[DExh] < 0.30f) AddRow(o, ExhaustId, 1, 2f, z, 0, -1);      // exhaust tip
                    Body(o, 3f, 8f, d);
                    if (b < 3) return;
                    if (d[DCover] < 0.80f) AddRow(o, PanelId, 3, Mathf.Lerp(6f, 8f, d[DMag]), z, s, -1);
                    if (front && d[DRad] < 0.60f) Radiator(o, 2, d);                           // radiator holed
                    Body(o, 8f, 15f, d);
                    if (b < 4) return;
                    if (front && d[DRad] < 0.90f) Radiator(o, 3, d);                           // radiator crushed
                    Body(o, 15f, 25f, d);
                    if (b < 5) return;
                    Body(o, 25f, 40f, d);                                                      // rails, core support: R1b
                    return;
                }
                case ZSide:
                    Scuff(o, d);                                                               // scuff, crease
                    if (b < 2) return;
                    if (d[DCover] < 0.60f) AddRow(o, PanelId, 2, Mathf.Lerp(3f, 5f, d[DMag]), z, s, -1);   // door dent
                    Body(o, 3f, 8f, d);
                    if (b < 3) return;
                    if (d[DCover] < 0.80f) AddRow(o, PanelId, 3, Mathf.Lerp(6f, 8f, d[DMag]), z, s, -1);   // jammed door, rocker
                    Body(o, 10f, 20f, d);
                    if (b < 4) return;
                    Body(o, 20f, 35f, d);                                                      // floor or pillar
                    return;
                case ZCorner:
                {
                    if (inc.kind != KBottom) Scuff(o, d);
                    int cs = inc.corner >= 0 ? (inc.corner % 2 == 0 ? -1 : 1) : (s != 0 ? s : 1);
                    if (d[DAlign] < 0.25f) Align(o, cs, 1);                                     // toe 0.02-0.08 deg
                    if (b < 2) return;
                    if (d[DAlign] < 0.40f) Align(o, cs, 2);                                     // tie rod bent
                    if (b < 3) return;
                    if (d[DAlign] < 0.50f) Align(o, cs, 2);                                     // arm, strut or knuckle
                    if (b < 4) return;
                    Align(o, cs, 2);                                                           // subframe / knuckle: R1b, FAIL part 2
                    Body(o, 5f, 15f, d);
                    return;
                }
                case ZUnder:
                    if (b < 2) { o.sparks = true; return; }                                    // sparks only
                    if (d[DExh] < 0.65f) AddRow(o, ExhaustId, 1, 2f, z, 0, -1);                // exhaust, hanger, heat shield
                    if (b < 3) return;
                    if (d[DExh] < 0.65f) AddRow(o, ExhaustId, 2, 4f, z, 0, -1);
                    Body(o, 2f, 6f, d);                                                        // floor dents (pan, lines: R2a+)
                    if (b < 4) return;
                    Body(o, 15f, 30f, d);                                                      // the DROP's frame check: R1b
                    return;
                case ZRoof:
                    Scuff(o, d);                                                               // roof scuffed
                    if (d[DGlass] < 0.30f) AddRow(o, GlassId, 1, 3f, z, 0, -1);               // glass cracked
                    if (b < 2) return;
                    AddRow(o, GlassId, 2, 15f, z, 0, -1);                                      // roof dented, windshield
                    Body(o, 5f, 15f, d);
                    if (b < 3) return;
                    AddRow(o, GlassId, 3, 40f, z, 0, -1);                                      // roof crushed (FAIL: part 2)
                    Body(o, 25f, 40f, d);
                    return;
            }
        }
    }

    /// <summary>One row an outcome writes (DamageModel.RowIds).</summary>
    public struct Row
    {
        public string id;
        public int stage;
        /// <summary>The row's size: value % (panels, glass), %/min (radiator),
        /// power % (exhaust), the x1.3 (bar).</summary>
        public float mag;
        public int zone, side, corner;
    }

    /// <summary>What one incident does to the car.</summary>
    public sealed class Outcome
    {
        public float paintLoss, bodyLoss, radiatorLoss, leakPctMin;
        public int alignSide, alignSeverity;
        public bool flood, hydrolock, sparks;
        public readonly List<Row> rows = new List<Row>();

        public void Clear()
        {
            paintLoss = bodyLoss = radiatorLoss = leakPctMin = 0f;
            alignSide = alignSeverity = 0;
            flood = hydrolock = sparks = false;
            rows.Clear();
        }

        /// <summary>Anything at all (sparks are not damage).</summary>
        public bool Any => paintLoss > 0f || bodyLoss > 0f || radiatorLoss > 0f || alignSeverity > 0 ||
                           flood || hydrolock || rows.Count > 0;
        /// <summary>A cosmetic mark and nothing else.</summary>
        public bool ScuffOnly => paintLoss > 0f && bodyLoss <= 0f && radiatorLoss <= 0f && alignSeverity == 0 &&
                                 !flood && !hydrolock && rows.Count == 0;

        public bool HasRow(string id) { foreach (var r in rows) if (r.id == id) return true; return false; }

        public override string ToString()
        {
            if (!Any) return sparks ? "sparks" : "nothing";
            var sb = new System.Text.StringBuilder();
            if (paintLoss > 0f) sb.Append("paint -").Append(paintLoss.ToString("0.0")).Append(' ');
            if (bodyLoss > 0f) sb.Append("body -").Append(bodyLoss.ToString("0.0")).Append(' ');
            foreach (var r in rows) sb.Append(r.id).Append(" s").Append(r.stage).Append(' ');
            if (radiatorLoss > 0f) sb.Append("radiator -").Append(radiatorLoss.ToString("0")).Append(' ');
            if (alignSeverity > 0) sb.Append("align ").Append(alignSeverity >= 2 ? "severe " : "minor ")
                                     .Append(alignSide < 0 ? "left " : "right ");
            if (flood) sb.Append("flood ");
            if (hydrolock) sb.Append("hydrolock ");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>One crash, strike, roll, drop or dunking: a hitId, where, how
    /// hard (its hardest pulse), its dice and what it did.</summary>
    public sealed class Incident
    {
        public int hitId, kind, zone, side, corner = -1, band;
        public float dv, closing, t0, tLast;
        public Vector3 pos;
        public bool narrow, glancing, running, reported;
        public string what = "";
        public float[] draws;
        public readonly Outcome outcome = new Outcome();
        public int quarters;
    }

    /// <summary>
    /// One drive's damage on one car (the career car: Banks; or a census
    /// car: resolves and reports, writes nothing). Fed by CollisionResponder.
    /// </summary>
    public sealed class DamageLedger
    {
        readonly CarController car;
        readonly CollisionResponder resp;
        readonly EngineTemp temp;
        readonly Rigidbody rb;
        readonly CarBody body;
        readonly System.Random rng;
        public readonly bool Banks;
        public string CarId = "";
        public int Day;
        public float Odo;
        public string DriveTag = "";
        public CrashStopGap.Bumper bumper;
        /// <summary>Tests force the dice through this.</summary>
        public System.Func<float[]> DrawOverride;
        /// <summary>Front and rear (index = zone) carry a crushed bar.</summary>
        public readonly bool[] barCrushed = new bool[3];
        /// <summary>The leak the car came out with (its radiator_leak row).</summary>
        public float baseWeep;
        public readonly List<Incident> incidents = new List<Incident>();

        float radiatorApplied;
        bool exhaustApplied, dirty;
        float lastWrite = -100f;

        sealed class Pulse
        {
            public int colliderId;
            public float t0, jn, closing, ratio;
            public Vector3 normal;
            public Incident inc;
            public bool roll;
        }
        readonly List<Pulse> pulses = new List<Pulse>();
        readonly DamageModel.Shell shell = new DamageModel.Shell();
        static Vector3[] pts = new Vector3[16];

        public DamageLedger(CarController car, CollisionResponder resp, bool banks, int seed)
        {
            this.car = car;
            this.resp = resp;
            Banks = banks;
            temp = car != null ? car.GetComponent<EngineTemp>() : null;
            rb = car != null ? car.GetComponent<Rigidbody>() : null;
            body = car != null ? car.GetComponent<CarBody>() : null;
            rng = new System.Random(seed);
            DriveTag = ((uint)seed).ToString("x8");
        }

        /// <summary>Damage events this drive (incidents that did anything).</summary>
        public int Events { get { int n = 0; foreach (var i in incidents) if (i.outcome.Any) n++; return n; } }

        // ================= arming =================
        /// <summary>
        /// The career car's ledger (CrashStopGap.Arm keeps the guards: a career
        /// drive, not a test drive, an edition with a career). Banks only onto
        /// the car this drive names (RaceHandoff.CarId) - never ActiveCar.
        /// Reads what the car carries: a crushed bar per zone, a leaking core.
        /// </summary>
        public static bool Arm(CarController car, CollisionResponder resp)
        {
            if (car == null || resp == null) return false;
            var s = LifeSim.LifeSimManager.State;
            string id = RaceHandoff.CarId;
            if (s == null || string.IsNullOrEmpty(id)) return false;
            var owned = s.FindCar(id);
            if (owned == null) return false;
            var L = new DamageLedger(car, resp, true, System.Guid.NewGuid().GetHashCode())
            {
                CarId = id, Day = s.day, Odo = owned.odoMiles, bumper = CrashStopGap.LiveBumper(),
            };
            if (owned.faults != null)
                foreach (var f in owned.faults)
                {
                    if (f == null) continue;
                    if (f.id == DamageModel.BarId && (f.zone1 == DamageModel.ZFront || f.zone1 == DamageModel.ZRear))
                        L.barCrushed[f.zone1] = true;
                    if (f.id == DamageModel.RadiatorId)
                        L.baseWeep = Mathf.Max(L.baseWeep, DamageModel.WeepFor(DamageModel.LeakFor(f.stage)));
                }
            resp.Damage = L;
            resp.Ledger = null;
            car.hitSteerPull = 0f;
            if (L.temp != null && L.baseWeep > L.temp.radiatorWeep) L.temp.radiatorWeep = L.baseWeep;
            return true;
        }

        // ================= contacts =================
        Transform Frame(out Vector3 centre)
        {
            var box = body != null ? body.box : null;
            centre = box != null ? box.center : Vector3.zero;
            if (box != null)
            {
                shell.half = box.size * 0.5f;
                var def = body.Def;
                shell.beltY = DamageModel.BeltOf(def, shell.half.y);
            }
            return box != null ? box.transform : car.transform;
        }

        void Hubs(Transform frame, Vector3 centre)
        {
            if (car == null || car.wheelHubs == null) return;
            for (int i = 0; i < 4 && i < car.wheelHubs.Length; i++)
                if (car.wheelHubs[i] != null) shell.hubs[i] = frame.InverseTransformPoint(car.wheelHubs[i].position) - centre;
            shell.tyreR = car.wheelRadiusFront > 0f ? car.wheelRadiusFront : car.wheelRadius;
        }

        Pulse Find(int id)
        {
            for (int i = 0; i < pulses.Count; i++) if (pulses[i].colliderId == id) return pulses[i];
            return null;
        }

        /// <summary>A contact step (Enter opens a pulse; Stay adds to it for
        /// <see cref="DamageModel.PulseSeconds"/>; after that it is a scrape).</summary>
        public void OnContact(Collision c, bool enter, float now)
        {
            if (car == null || rb == null || c == null) return;
            int n = c.contactCount;
            if (n == 0) return;
            int cid = c.collider != null ? c.collider.GetHashCode() : 0;
            if (!enter)
            {
                lastContact = now;
                var open = Find(cid);
                if (open == null || now - open.t0 > DamageModel.PulseSeconds) return;
                open.jn += Mathf.Abs(Vector3.Dot(c.impulse, open.normal));
                Grow(open, now);
                return;
            }
            // the push on the car: the contacts that pushed (as Measure does)
            Vector3 sum = Vector3.zero, all = Vector3.zero;
            if (pts.Length < n) pts = new Vector3[n];
            Transform frame = Frame(out Vector3 centre);
            for (int i = 0; i < n; i++)
            {
                var cp = c.GetContact(i);
                all += cp.normal;
                if (cp.impulse.sqrMagnitude > 1e-8f) sum += cp.normal;
                pts[i] = frame.InverseTransformPoint(cp.point) - centre;
            }
            if (sum.sqrMagnitude < 1e-4f) sum = all;
            if (sum.sqrMagnitude < 1e-4f) return;
            Vector3 nW = sum.normalized;
            Vector3 rel = c.relativeVelocity;
            float closing = Mathf.Abs(Vector3.Dot(rel, nW));
            if (closing * DamageModel.CapFactor < 1f) return;     // under every band
            float vMag = rel.magnitude;
            Hubs(frame, centre);
            Vector3 local = CrashStopGap.PatchCentre(pts, n);
            Vector3 nL = frame.InverseTransformDirection(nW);
            bool upright = Vector3.Dot(car.transform.up, Vector3.up) > 0.25f;
            if (!DamageModel.Classify(shell, local, nL, upright, out int zone, out int side, out int corner)) return;

            var orb = c.rigidbody;
            float ratio = 1f;
            if (orb != null)
            {
                float mo = orb.GetComponent<CarController>() != null ? orb.mass : DamageModel.TrafficKg;
                ratio = mo / Mathf.Max(1f, rb.mass + mo);
            }
            var p = Find(cid);
            if (p == null) { p = new Pulse { colliderId = cid }; pulses.Add(p); }
            p.t0 = now; p.jn = Mathf.Abs(Vector3.Dot(c.impulse, nW)); p.closing = closing; p.ratio = ratio; p.normal = nW;
            p.roll = rolling && zone == DamageModel.ZRoof;
            p.inc = null;
            if (!p.roll)
            {
                bool glancing = zone != DamageModel.ZUnder && zone != DamageModel.ZRoof &&
                                vMag > 0.01f && closing / vMag < DamageModel.GlancingIncidence;
                bool narrow = orb == null && DamageModel.IsNarrow(c.collider);
                if (zone == DamageModel.ZUnder && (airborne || now <= dropUntil))
                    p.inc = Land(now);                                // the landing of a DROP takes its belly pulse
                if (p.inc == null)
                    p.inc = JoinOrOpen(DamageModel.KHit, zone, side, corner, now, narrow, glancing,
                                       c.collider != null ? c.collider.name : "?");
            }
            lastContact = now;
            Grow(p, now);
        }

        void Grow(Pulse p, float now)
        {
            float dv = Mathf.Min(p.jn / Mathf.Max(1f, rb.mass), p.closing * p.ratio * DamageModel.CapFactor);
            if (p.roll) { rollRoofDv = Mathf.Max(rollRoofDv, dv); return; }
            var inc = p.inc;
            if (inc == null) return;
            inc.tLast = now;
            if (inc.kind == DamageModel.KDrop) return;     // the belly pulse of a DROP is the drop's (its landing speed)
            if (dv <= inc.dv) return;
            inc.dv = dv;
            Reresolve(inc);
        }

        Incident JoinOrOpen(int kind, int zone, int side, int corner, float now, bool narrow, bool glancing, string what)
        {
            Vector3 at = car != null ? car.transform.position : Vector3.zero;
            for (int i = incidents.Count - 1; i >= 0; i--)
            {
                var o = incidents[i];
                if (o.kind != kind || o.zone != zone || o.corner != corner) continue;
                if (now - o.tLast > DamageModel.MergeSeconds || (o.pos - at).magnitude > DamageModel.MergeMetres) continue;
                o.pos = at;
                o.narrow |= narrow;
                o.glancing &= glancing;
                return o;
            }
            var inc = new Incident
            {
                hitId = NewHitId(), kind = kind, zone = zone, side = side, corner = corner, t0 = now, tLast = now,
                pos = at, narrow = narrow, glancing = glancing, what = what ?? "",
                draws = DrawOverride != null ? DrawOverride() : DamageModel.Draw(rng),
            };
            incidents.Add(inc);
            return inc;
        }

        int NewHitId()
        {
            int id;
            do { id = rng.Next(1, int.MaxValue); } while (incidents.Exists(i => i.hitId == id));
            return id;
        }

        /// <summary>Re-read an incident against its own dice (it only grows).</summary>
        void Reresolve(Incident inc)
        {
            int band;
            if (inc.kind == DamageModel.KBottom) band = DamageModel.BottomBand(inc.dv);
            else if (inc.kind == DamageModel.KDrop) band = DamageModel.DropBand(inc.dv);
            else if (inc.kind == DamageModel.KRoll) band = DamageModel.RollBand(inc.dv, inc.quarters);
            else if (inc.kind == DamageModel.KWater) band = 1;
            else band = DamageModel.Band(inc.zone, inc.dv, bumper, inc.narrow, inc.glancing,
                                         inc.zone == DamageModel.ZFront || inc.zone == DamageModel.ZRear ? barCrushed[inc.zone] && !BarThisIncident(inc) : false);
            if (band < inc.band) band = inc.band;
            inc.band = band;
            DamageModel.Resolve(inc, bumper);
            ApplyLive();
        }

        /// <summary>The bar this very incident crushed does not multiply it.</summary>
        bool BarThisIncident(Incident inc) => barFrom != null && barFrom[inc.zone] == inc.hitId;
        readonly int[] barFrom = new int[3];

        // ================= strikes, rolls, drops, water =================
        /// <summary>A bump-stop strike: the struck wheel's CORNER column at 6 / 8 / 10 m/s.</summary>
        public void Bottom(int wheel, float mps, float now)
        {
            if (wheel < 0 || wheel > 3 || DamageModel.BottomBand(mps) <= 0) return;
            var inc = JoinOrOpen(DamageModel.KBottom, DamageModel.ZCorner, wheel % 2 == 0 ? -1 : 1, wheel, now, false, false, "bump stop");
            inc.tLast = now;
            if (mps <= inc.dv) return;
            inc.dv = mps;
            Reresolve(inc);
        }

        bool rolling, rollConfirmed, rollLatched, airborne, waterDone;
        float rollAngle, rollRoofDv, restT, rollStart, peakY, vDown, airStart, dropUntil, lastContact = -100f;
        Vector3 prevUp = Vector3.up;
        Incident dropInc;

        /// <summary>Every physics step: rolls, drops, water, closing, the file.</summary>
        public void Tick(float dt, float now)
        {
            if (car == null || rb == null) return;
            Vector3 up = car.transform.up;
            float upDot = Vector3.Dot(up, Vector3.up);
            // ROLL: from the first big tilt to rest (or back on the wheels)
            if (rollLatched && upDot > 0.9f) rollLatched = false;   // back on its wheels: the next roll is a new one
            if (!rolling && !rollLatched && upDot < 0.9f) { rolling = true; rollConfirmed = false; rollAngle = 0f; rollRoofDv = 0f; restT = 0f; rollStart = now; }
            if (rolling)
            {
                rollAngle += Vector3.Angle(prevUp, up);
                if (upDot < 0.25f) rollConfirmed = true;
                bool still = rb.linearVelocity.magnitude < 0.6f && rb.angularVelocity.magnitude < 0.6f;
                restT = still ? restT + dt : 0f;
                bool backUp = upDot > 0.9f && Grounded() >= 2;
                if (backUp && !rollConfirmed) rolling = false;
                else if (rollConfirmed && (restT > 0.75f || (backUp && now - rollStart > 0.5f)))
                {
                    rolling = false;
                    rollLatched = upDot < 0.9f;   // banked once while it lies there
                    BankRoll(Mathf.FloorToInt(rollAngle / 90f), rollRoofDv, now);
                }
            }
            prevUp = up;
            // DROP: airborne with no wheel down and no contact, then down again
            int g = Grounded();
            bool touching = now - lastContact < 0.05f;
            if (!airborne && g == 0 && !touching && !rolling) { airborne = true; airStart = now; peakY = car.transform.position.y; vDown = 0f; }
            if (airborne)
            {
                peakY = Mathf.Max(peakY, car.transform.position.y);
                vDown = Mathf.Max(vDown, -rb.linearVelocity.y);
                if (g > 0 || touching) Land(now);
            }
            if (dropInc != null && now > dropUntil) dropInc = null;
            // WATER: the car's origin half a metre under the drawn sea
            if (!waterDone && stuck != null && car.transform.position.y < stuck.SeaY - DamageModel.SeaDepthM)
            {
                waterDone = true;
                BankWater(Running(), now);
            }
            // close: the census hears each finished incident once
            foreach (var inc in incidents)
                if (!inc.reported && now - inc.tLast > DamageModel.MergeSeconds + DamageModel.PulseSeconds)
                {
                    inc.reported = true;
                    DamageModel.Report(resp, inc);
                }
            for (int i = pulses.Count - 1; i >= 0; i--)
                if (now - pulses[i].t0 > DamageModel.PulseSeconds) pulses.RemoveAt(i);
            if (Banks && dirty && now - lastWrite >= 2f) Flush(now);
        }

        StuckRecovery stuckRef;
        bool stuckLooked;
        StuckRecovery stuck
        {
            get
            {
                if (!stuckLooked) { stuckLooked = true; stuckRef = car != null ? car.GetComponent<StuckRecovery>() : null; }
                return stuckRef;
            }
        }

        int Grounded()
        {
            int g = 0;
            var w = car.wheelContacts;
            if (w != null) for (int i = 0; i < w.Length; i++) if (w[i].grounded) g++;
            return g;
        }

        bool Running()
        {
            if (temp != null && temp.Seized) return false;
            var tank = car.GetComponent<FuelTank>();
            return tank == null || !tank.Empty;
        }

        /// <summary>The landing of a fall: a DROP when it fell far enough.
        /// Null when it was only a hop.</summary>
        Incident Land(float now)
        {
            if (!airborne) return dropInc;
            airborne = false;
            float fall = peakY - car.transform.position.y;
            if (now - airStart < 0.3f || fall < DamageModel.DropMinM) return null;
            float landing = Mathf.Max(vDown, Mathf.Sqrt(2f * 9.81f * fall));
            return PlantDrop(landing, now);
        }

        /// <summary>A DROP landed at <paramref name="landing"/> m/s (public for
        /// the self-test): the UNDER table at landing less the suspension's
        /// share; the stops strike on their own (Bottom).</summary>
        public Incident PlantDrop(float landing, float now)
        {
            var inc = JoinOrOpen(DamageModel.KDrop, DamageModel.ZUnder, 0, -1, now, false, false, "drop");
            inc.tLast = now;
            dropInc = inc;
            dropUntil = now + DamageModel.MergeSeconds;
            if (landing > inc.dv) { inc.dv = landing; Reresolve(inc); }
            return inc;
        }

        /// <summary>A ROLL banked when it comes to rest (public for the test).</summary>
        public Incident BankRoll(int quarters, float roofDv, float now)
        {
            var inc = JoinOrOpen(DamageModel.KRoll, DamageModel.ZRoof, 0, -1, now, false, false, "roll");
            inc.quarters = Mathf.Max(inc.quarters, quarters);
            inc.tLast = now;
            inc.dv = Mathf.Max(inc.dv, roofDv);
            Reresolve(inc);
            return inc;
        }

        /// <summary>The car went into the water (public for the test).</summary>
        public Incident BankWater(bool running, float now)
        {
            var inc = JoinOrOpen(DamageModel.KWater, DamageModel.ZUnder, 0, -1, now, false, false, "water");
            inc.running = running;
            inc.tLast = now;
            inc.dv = Mathf.Max(inc.dv, 0.01f);
            Reresolve(inc);
            if (Banks && inc.outcome.hydrolock && temp != null && !temp.Seized)
                temp.Seize("went into the water running and stopped dead");
            return inc;
        }

        /// <summary>A hit with no Collision (the self-test and the debug bench):
        /// the same incident rules as a real contact.</summary>
        public Incident PlantHit(int zone, int side, int corner, float dv, float now, bool narrow = false, bool glancing = false)
        {
            var inc = JoinOrOpen(DamageModel.KHit, zone, side, corner, now, narrow, glancing, "planted");
            inc.tLast = now;
            if (dv > inc.dv) { inc.dv = dv; Reresolve(inc); }
            return inc;
        }

        // ================= live =================
        /// <summary>Onto the car, now, from every incident of the drive
        /// (absolute: applying twice is applying once).</summary>
        void ApplyLive()
        {
            dirty = true;
            int aBand = 0, aSide = 0;
            float rad = 0f, leak = 0f;
            bool exhaust = false;
            foreach (var inc in incidents)
            {
                var o = inc.outcome;
                if (o.alignSeverity > 0 && CrashStopGap.NewAlignWins(aBand, o.alignSeverity)) { aBand = o.alignSeverity; aSide = o.alignSide; }
                rad += o.radiatorLoss;
                leak = Mathf.Max(leak, o.leakPctMin);
                foreach (var r in o.rows)
                {
                    if (r.id == DamageModel.BarId && (r.zone == DamageModel.ZFront || r.zone == DamageModel.ZRear) && !barCrushed[r.zone])
                    {
                        barCrushed[r.zone] = true;
                        barFrom[r.zone] = inc.hitId;
                    }
                    if (r.id == DamageModel.ExhaustId) exhaust = true;
                }
            }
            if (!Banks || car == null) return;
            car.hitSteerPull = aBand > 0 ? aSide * CrashStopGap.PullDeg(aBand) : 0f;
            if (exhaust && !exhaustApplied) { exhaustApplied = true; car.faultAccelMult *= DamageModel.ExhaustPower; }
            if (temp == null) return;
            rad = Mathf.Min(rad, 100f);
            float more = rad - radiatorApplied;
            if (more > 0f)
            {
                temp.radiatorHit += more;
                temp.radiatorCond = Mathf.Max(0f, temp.radiatorCond - more);
                radiatorApplied = rad;
            }
            float w = Mathf.Max(baseWeep, DamageModel.WeepFor(leak));
            if (w > temp.radiatorWeep) temp.radiatorWeep = w;
        }

        // ================= the file =================
        /// <summary>Write the small damage file now (nothing when nothing
        /// happened, or not a career car).</summary>
        public void Flush(float now = -1f)
        {
            if (!Banks || !dirty) return;
            dirty = false;
            lastWrite = now >= 0f ? now : Time.time;
            var f = new LifeSim.DamageBank.DamageFile { carId = CarId, day = Day, odo = Odo, drive = DriveTag };
            foreach (var inc in incidents)
                if (inc.outcome.Any) f.incidents.Add(LifeSim.DamageBank.Record(inc));
            if (f.incidents.Count > 0) LifeSim.DamageBank.Write(f);
        }

        /// <summary>Write the file and fold every damage file into the career
        /// now (exit stamps, QUIT, the tow, RESTART). Once per hitId.</summary>
        public static int FoldNow(DamageLedger L)
        {
            if (L != null) L.Flush();
            var s = LifeSim.LifeSimManager.State;
            int n = LifeSim.DamageBank.FoldFiles(s);
            if (n > 0) LifeSim.LifeSimManager.Save();
            return n;
        }
    }
}
