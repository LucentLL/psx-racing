namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SMOOTHNESS GATE'S NUMBERS, in one place (gate spec, plan amendment
    /// A2/A4 WP-G). The owner's rule of 2026-09-28: painted lines and road
    /// edges never wobble and never kink. Every lateral threshold below is
    /// the one visibility unit <see cref="V"/>; nothing else is tuned.
    ///
    /// Two implementations read this file and must agree (a disagreement is
    /// a gate bug): Editor/CitySmooth.cs, on the meshes CityMeshes built, and
    /// tools/city/linecheck.mjs, offline on the plan-view replica. linecheck
    /// PARSES this file (tools/city/lib/smoothrules.mjs), so keep every
    /// constant on one line in the forms used here: `public const float X =
    /// 1.5f;`, `public const int`, `public const bool`, and the tables' `new
    /// RClass(...)` / `new CheckDef(...)` rows. The RoadsideRules precedent.
    /// </summary>
    public static class SmoothRules
    {
        // ---- the visibility unit (gate spec section 2) -------------------
        /// <summary>One framebuffer pixel at 6 m in the 240-line chase view
        /// (58 degree base fov: 4.22 mrad a pixel). About half a texel of the
        /// painted road textures, a fifth of the 12 cm line.</summary>
        public const float V = 0.025f;
        public const int FramebufferLines = 240;
        public const float FovDeg = 58f;
        public const float PixelAtM = 6f;

        // ---- sampling ------------------------------------------------------
        /// <summary>A-, C- and D-family samples along every line (plus every
        /// rendered vertex: a piecewise-linear error peaks at a vertex).</summary>
        public const float SampleStepM = 0.5f;
        /// <summary>Vertices turning less than this are dropped before the
        /// shape checks (straight-line noise).</summary>
        public const float CollinearDeg = 0.02f;
        /// <summary>B2's evaluation chord cap: about one dash cycle and one
        /// station step. A lone kink between long straights may turn
        /// 8V/10 m = 1.15 degrees.</summary>
        public const float ChordCapM = 10f;
        /// <summary>B2's chords c-, c+ run to the nearest kept vertex on each
        /// side where the line has turned again by this share of the vertex's
        /// (or the cluster's: a corner split over close vertices, judged as
        /// one, tools/city/lib/kink.mjs) own turn - one vertex turning that
        /// much, or several adding up to it. Smaller turns (the bisector pinch,
        /// diagonal crossings, sections turning a few hundredths of a degree
        /// next to a kink) are noise and never shorten the chord, so a lone
        /// kink is judged over the full ChordCapM; a curve that keeps turning
        /// ends it. On a smoothly sampled curve neighbouring turns share a
        /// chord, so the chord stops at the neighbour; a vertex whose chords both
        /// stop at a turn BACK is a zigzag peak and scores twice its sagitta. A
        /// turn back of this share or more makes a HEDGED window, judged by its
        /// net turn; between two straights (for min(1 / KinkNoiseShare times its
        /// own length, ChordCapM) either side the line turns again by less than
        /// this share of the window's excursion, or runs within V of one line
        /// turning no more than the lone limit, or runs on to its end) also at
        /// its drawn corner with signed rounding, by how far it is drawn outside
        /// every smooth transition (a bump, a notch) and by its jog - the whole
        /// step over a quarter of ChordCapM or less, else the part faster than
        /// the plan's fastest ease (a smoothstep over the shortest TaperFloor);
        /// wider windows up to KinkViewM by their corner and outside distance,
        /// eased. Lobes of this share of their neighbours or more, turning back
        /// on both sides within ChordCapM, make a WAVE, judged against its mean
        /// line (SimplifyEpsM) (lib/kink.mjs).</summary>
        public const float KinkNoiseShare = 0.25f;
        /// <summary>B2 judges a corner split over close vertices as one
        /// (tools/city/lib/kink.mjs) as the chase view shows it out to this
        /// distance, where a pixel is V * KinkViewM / PixelAtM (16.7 cm): a
        /// bend rounded by less than that reads as SHARP somewhere in the
        /// view, one rounded by more reads as a curve everywhere and is B3's.
        /// The spec: "at R 7.5 a 90 degree corner stays rounded out to about
        /// 40 m".</summary>
        public const float KinkViewM = 40f;
        /// <summary>B2's WAVE rule (tools/city/lib/kink.mjs): a line that
        /// swings to and fro - two or more lobes in a row, each turning back on
        /// both sides - is judged against its mean line (the chord of each
        /// lobe's inflections): "symmetric zigzag: peak deviation &lt;= V". A
        /// lobe standing this far or more off it is geometry, not a wobble:
        /// the plan's WP-10 Douglas-Peucker (0.5 m) removes every sideways
        /// excursion under it before WP-11 fillets what is left, so a winding
        /// road's lobes pass and B3 judges their radius. The plan's number,
        /// not a tuning knob.</summary>
        public const float SimplifyEpsM = 0.5f;
        /// <summary>B1: resample step and the half window of the circle fit.</summary>
        public const float JitterStepM = 0.25f;
        public const float JitterHalfM = 2f;
        /// <summary>B3: the half chord of the sustained heading change,
        /// R = CurveHalfM / (heading difference of the chords either side).</summary>
        public const float CurveHalfM = 3f;
        /// <summary>B3 on a ribbon edge: tighter than this the inner edge folds.</summary>
        public const float InnerEdgeMinRM = 3f;

        // ---- family A: position ----------------------------------------------
        /// <summary>A4: rendered paint width within +-25% of the texture run at
        /// nominal scale (3 cm on a 12 cm line, 1.2 px at 6 m).</summary>
        public const float LineWidthTol = 0.25f;
        /// <summary>A5 / A5b: a line more than this from every plan line of its
        /// colour is STRAY (a plan line with nothing within it is MISSING);
        /// a violation is a run of at least <see cref="StrayRunM"/>.</summary>
        public const float StrayM = 0.5f;
        public const float StrayRunM = 1f;
        /// <summary>The plan: a centre or lane line exists wherever it lies
        /// more than this inside the design edge.</summary>
        public const float ExistInsetM = 0.3f;
        /// <summary>The plan: an edge line's centre is the shoulder plus this
        /// inside the design edge (the painter's PaintHalf).</summary>
        public const float EdgeLineInsetM = 0.06f;
        /// <summary>A0 TEXTURE: a painted run's centre may sit at most half a
        /// texel (profile width / texture width / 2) plus this from its plan
        /// line; that offset, and only that much, is subtracted from the
        /// position checks (V is half a texel so geometry is told from it).</summary>
        public const float TexelPadM = 0.001f;
        /// <summary>The plan's design edge through a taper: 0 linear (today's
        /// Trims.HalfWidthAt), 1 smoothstep (the M0 stopgap). A design
        /// decision, flipped in the commit that changes the builder.</summary>
        public const int PlanTaperShape = 0;

        // ---- family B: shape; chaining -----------------------------------------
        /// <summary>Two line ends within this across a section, node or seam
        /// are one point. Within V they are still one line: the shape checks
        /// run on the joined polyline with the (invisible) step taken out, so a
        /// kink at the node is judged; past V the step is a JUMP.</summary>
        public const float JoinM = 0.002f;
        /// <summary>Otherwise a line is matched to the nearest piece of its
        /// colour and pattern within this - one lane (RoadProfiles.LaneM) -
        /// and the offset is a JUMP. Only an end with no partner left within a
        /// lane is an END (judged by C2: a plan lane drop is legitimate).</summary>
        public const float MatchM = 3.6576f;
        /// <summary>B4 SEAM: two tiles cut one span from identical sections,
        /// so any offset past this at a tile seam is a determinism bug.</summary>
        public const float SeamM = 0.01f;

        // ---- family C: continuity and pattern -------------------------------
        /// <summary>C1: one texel row along V (12.192 m / 64).</summary>
        public const float GapM = 0.20f;
        /// <summary>C2: a line may end within this of a fan mouth's trim.</summary>
        public const float FanMouthM = 0.5f;
        /// <summary>C3: MUTCD 10 ft dash and 30 ft gap, +-20% (the shared-V
        /// shear on a curve is at most 20%); a truncated dash at a fan mouth
        /// or gore under <see cref="StubM"/> is a stray blip.</summary>
        public const float DashM = 3.048f;
        public const float DashGapM = 9.144f;
        public const float DashTol = 0.2f;
        public const float StubM = 1.0f;

        // ---- family D/E: paint over other pavement, strip height -----------
        /// <summary>D1: penetration into another pavement at the same level
        /// (|dy| within <see cref="CrossDyM"/>, CityMeshes' ArmSplitDyM).</summary>
        public const float CrossM = 0.20f;
        public const float CrossDyM = 0.25f;
        /// <summary>D1's plan merge zone (report-only until WP-18b) is a branch
        /// ATTACH ARC: the branch's clip range (BranchSeats' pieces; EmitBranch's
        /// attached samples, a metre either side, out to the node) plus this.
        /// The same branch/host pair anywhere else is gated.</summary>
        public const float MergeMarginM = 1f;
        public const float FloatMinM = 0.005f;
        public const float FloatMaxM = 0.03f;

        // ---- exemptions ---------------------------------------------------------
        /// <summary>X2: edge shape checks stand down within this of a gore nose
        /// (a collapsed section); and C2's gore NOSE: a line may end within this
        /// of one - not of a merely clipped section along a branch's attach arc.</summary>
        public const float GoreNoseM = 2f;

        // ---- the exporter's densify rule (plan A2 I5) ---------------------
        /// <summary>Every column's facet sagitta is at most this; the exporter
        /// writes its own into the container META and CityAudit checks the two
        /// agree (from WP-11).</summary>
        public const float DensifyEpsM = 0.02f;

        // ---- runs, ranking, report ----------------------------------------------
        /// <summary>Violation keys bucket the arc along the OSM way by this:
        /// a run carries one key for EVERY bucket its bad samples touch, each
        /// with its own worst ratio, so a run that grows or worsens anywhere
        /// trips the ratchet.</summary>
        public const float KeyStepM = 5f;
        /// <summary>Two bad samples further apart than this along one line are
        /// two runs (B2 and B3 sample only the kept vertices, which a straight
        /// leaves tens of metres apart).</summary>
        public const float RunBreakM = 10f;
        /// <summary>Baseline ratios are stored rounded UP to a power of this
        /// (0.1% steps), so the same data always passes its own baseline.</summary>
        public const float RatioQuantum = 1.001f;
        /// <summary>Every key also stores its bad length (metres of bad samples
        /// in its bucket) rounded UP to this: a violation that lengthens inside
        /// buckets it already touches trips the ratchet too.</summary>
        public const float LengthQuantumM = 0.1f;
        public const int WorstN = 40;
        public const float DedupM = 40f;
        public const int ShotsN = 24;
        public const float ShotsDedupM = 60f;
        /// <summary>A reference spot's exposure reaches this far.</summary>
        public const float RefSpotReachM = 150f;
        public const float ExposureRoute = 2f;
        /// <summary>Ranking caps a run's ratio here (1 m at V), so no single
        /// unbounded measure fills the worst list (a 500 m crop is a C1 GAP
        /// 2,500 times its limit); the census capped its error at 3 m for the
        /// same reason. Counts and the ratchet use the raw values.</summary>
        public const float RankRatioCap = 40f;
        public const float ExposureRefSpot = 1.5f;

        // ---- modes ---------------------------------------------------------------
        /// <summary>FAST: one contiguous band of 1/BandCount of the city's
        /// road tiles every cycle (PSX_SMOOTH_BAND = 0..BandCount-1, default
        /// the day of the year mod BandCount).</summary>
        public const int BandCount = 12;
        /// <summary>FAST inside CityAudit.Run is OPT-IN (PSX_SMOOTH_FAST=1)
        /// until its first run in Unity validates the tap and its cost (R4);
        /// flip to true in that commit, and every city cycle then runs it.</summary>
        public const bool FastInAudit = false;

        // ---- rollout (gate spec section 8) ---------------------------------
        /// <summary>The first cycle only reports: its numbers become the
        /// baseline. Flip to false in the commit that records the baseline;
        /// from then every check below is a ratchet (or zero).</summary>
        public const bool ReportOnly = true;
        /// <summary>The creek pin (the owner's circled frame). Any violation on
        /// these ways fails whatever the ratchet says - from the day its fix
        /// (M0 or WP-11b) lands: flip to true in that commit.</summary>
        public const bool PinActive = false;
        public static readonly uint[] PinnedWays = { 1078015030u, 16671358u, 1252904925u };

        /// <summary>How a check gates once the report-only cycle is over.</summary>
        public enum State { Zero, Ratchet, Report }

        public struct CheckDef
        {
            public string id, name, what, zeroFrom; public State state;
            public CheckDef(string id, string name, State state, string what, string zeroFrom)
            { this.id = id; this.name = name; this.state = state; this.what = what; this.zeroFrom = zeroFrom; }
        }

        /// <summary>Every check the gate runs, in report order. The state is
        /// what it gates at after the report-only cycle; zeroFrom is the
        /// package that takes it to hard zero (plan A4 / gate spec 8).</summary>
        public static readonly CheckDef[] Checks =
        {
            new CheckDef("A0", "TEXTURE", State.Zero, "every painted run within half a texel of its plan line; every plan line painted, every run planned", "now"),
            new CheckDef("A1", "OFF", State.Ratchet, "every painted line and edge within V of its plan (a squeezed edge within V of its I7 envelope)", "M0 (tapers), R4"),
            new CheckDef("A2", "SKEW", State.Ratchet, "the centre pair centred on the drawn ribbon", "M0 (tapers), R4"),
            new CheckDef("A3", "INSET", State.Ratchet, "every edge line at its inset from its own drawn edge", "M0 (tapers), R4"),
            new CheckDef("A4", "LINEWIDTH", State.Ratchet, "rendered paint width within 25% of the painted run", "M0 (tapers), R4"),
            new CheckDef("A5", "STRAY", State.Ratchet, "no paint where the plan has no line of its colour", "M0 (tapers), R4"),
            new CheckDef("A5b", "MISSING", State.Report, "every plan line drawn (report-only until the line model draws it)", "R4 (WP-11b)"),
            new CheckDef("B1", "JITTER", State.Ratchet, "no line jitters past V from its local circle", "R4"),
            new CheckDef("B2", "KINK", State.Ratchet, "no line kinks past V (facet sagitta, twice it at a zigzag peak; a corner split over close vertices is one corner, a hedged one is its net turn, a bump or notch its excursion, a jog its step (faster than the plan's ease when wider than 2.5 m), a wave its lobes off their mean line; a bend fan is judged across)", "R4"),
            new CheckDef("B3", "CURVE", State.Ratchet, "no ribbon bends tighter than its class allows", "R4 (fans WP-19)"),
            new CheckDef("B4", "JUMP", State.Ratchet, "no line steps sideways past V where it continues", "R4 (WP-11b)"),
            new CheckDef("B4s", "SEAM", State.Zero, "no line steps at a tile seam (identical sections)", "now"),
            new CheckDef("C1", "GAP", State.Ratchet, "no solid line interrupted between its plan ends", "R4"),
            new CheckDef("C2", "END", State.Ratchet, "lines end only at legitimate ends (fan mouth, gore nose, dead end, plan lane drop)", "R4"),
            new CheckDef("C3", "DASH", State.Ratchet, "dash and gap lengths along the chained line", "R4 (stubs WP-17)"),
            new CheckDef("D1", "CROSS", State.Ratchet, "no paint inside other pavement at the same level (branch attach arcs report-only)", "R4 (merge zones WP-18b)"),
            new CheckDef("E1", "FLOAT", State.Report, "strip paint 0.5-3 cm over the surface (strip paint only)", "WP-17/WP-27"),
        };

        // ---- B3: the ribbon midline's minimum radius by class (4.2.1) --------
        public struct RClass
        {
            public string cls; public float r;
            public RClass(string cls, float r) { this.cls = cls; this.r = r; }
        }

        /// <summary>R_min by OSM class key (tools/city metrics' classOf: the
        /// rank name, + "_link"; every street below tertiary is "local").
        /// Lowered for a class only with before and after numbers in the
        /// commit (the calibration rule); never per spot.</summary>
        public static readonly RClass[] RMin =
        {
            new RClass("motorway", 150f),
            new RClass("trunk", 90f),
            new RClass("motorway_link", 25f),
            new RClass("trunk_link", 25f),
            new RClass("primary", 10f),
            new RClass("secondary", 10f),
            new RClass("tertiary", 10f),
            new RClass("primary_link", 10f),
            new RClass("secondary_link", 10f),
            new RClass("tertiary_link", 10f),
            new RClass("local", 7.5f),
            new RClass("local_link", 7.5f),
        };

        /// <summary>The taper FLOOR by class (plan WP-10 item 6's proposed
        /// values: street 15 m, arterial 30 m, freeway 90 m; ramps count as
        /// arterial). Plan I7 eases the squeeze like a taper over it, so a
        /// squeezed ribbon edge is judged against its I7 ENVELOPE (A1; X6 no
        /// longer exempts it): the cut the edge takes inside its design edge,
        /// max-dilated by a smoothstep falling to 0 over this length. A cut
        /// that arrives or leaves faster than that, or wanders back out
        /// between sections, sits outside its envelope.</summary>
        public static readonly RClass[] TaperFloor =
        {
            new RClass("motorway", 90f),
            new RClass("trunk", 90f),
            new RClass("motorway_link", 30f),
            new RClass("trunk_link", 30f),
            new RClass("primary", 30f),
            new RClass("secondary", 30f),
            new RClass("tertiary", 30f),
            new RClass("primary_link", 30f),
            new RClass("secondary_link", 30f),
            new RClass("tertiary_link", 30f),
            new RClass("local", 15f),
            new RClass("local_link", 15f),
        };

        /// <summary>WP-19's curb-return radius by junction class pair; a fan
        /// perimeter fails B3 below <see cref="CurbReturnShare"/> of it.</summary>
        public static readonly RClass[] CurbReturn =
        {
            new RClass("local_x_local", 7.5f),
            new RClass("arterial_x_local", 10f),
            new RClass("arterial_x_arterial", 12f),
            new RClass("ramp_terminal", 15f),
        };
        public const float CurbReturnShare = 0.9f;

        /// <summary>Ranking: class weight (the kink census's weighting).</summary>
        public static readonly RClass[] ClassWeight =
        {
            new RClass("motorway", 3f),
            new RClass("trunk", 2.5f),
            new RClass("primary", 1.5f),
            new RClass("secondary", 1.2f),
            new RClass("tertiary", 1.2f),
        };

        static readonly string[] RankName = { "local", "tertiary", "secondary", "primary", "trunk", "motorway" };

        /// <summary>The class key of an edge: metrics.mjs' classOf.</summary>
        public static string ClassOf(int cls, bool link) =>
            RankName[UnityEngine.Mathf.Clamp(cls, 0, RankName.Length - 1)] + (link ? "_link" : "");

        public static float RMinFor(string cls)
        {
            foreach (var r in RMin) if (r.cls == cls) return r.r;
            return 7.5f;
        }

        public static float WeightFor(string cls)
        {
            string plain = cls.EndsWith("_link") ? null : cls;
            foreach (var r in ClassWeight) if (r.cls == plain) return r.r;
            return 1f;
        }

        public static float TaperFloorFor(string cls)
        {
            foreach (var r in TaperFloor) if (r.cls == cls) return r.r;
            return 15f;
        }

        public static float CurbReturnFor(string pair)
        {
            foreach (var r in CurbReturn) if (r.cls == pair) return r.r;
            return 7.5f;
        }

        public static CheckDef Check(string id)
        {
            foreach (var c in Checks) if (c.id == id) return c;
            return default;
        }
    }
}
