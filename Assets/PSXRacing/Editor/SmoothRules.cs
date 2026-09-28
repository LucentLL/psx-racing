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
        /// <summary>The plan's design edge through a taper: 0 linear (today's
        /// Trims.HalfWidthAt), 1 smoothstep (the M0 stopgap). A design
        /// decision, flipped in the commit that changes the builder.</summary>
        public const int PlanTaperShape = 0;

        // ---- family B: shape; chaining -----------------------------------------
        /// <summary>Two line ends within this across a section, node or seam
        /// are one line.</summary>
        public const float JoinM = 0.002f;
        /// <summary>Otherwise a line is matched to the next piece of its
        /// colour and pattern within this, and the offset is a JUMP.</summary>
        public const float MatchM = 1.0f;
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
        public const float FloatMinM = 0.005f;
        public const float FloatMaxM = 0.03f;

        // ---- exemptions ---------------------------------------------------------
        /// <summary>X2: edge shape checks stand down within this of a gore nose.</summary>
        public const float GoreNoseM = 2f;

        // ---- the exporter's densify rule (plan A2 I5) ---------------------
        /// <summary>Every column's facet sagitta is at most this; the exporter
        /// writes its own into the container META and CityAudit checks the two
        /// agree (from WP-11).</summary>
        public const float DensifyEpsM = 0.02f;

        // ---- runs, ranking, report ----------------------------------------------
        /// <summary>Violation keys bucket the arc along the OSM way by this.</summary>
        public const float KeyStepM = 5f;
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
            new CheckDef("A1", "OFF", State.Ratchet, "every painted line and edge within V of its plan", "M0 (tapers), R4"),
            new CheckDef("A2", "SKEW", State.Ratchet, "the centre pair centred on the drawn ribbon", "M0 (tapers), R4"),
            new CheckDef("A3", "INSET", State.Ratchet, "every edge line at its inset from its own drawn edge", "M0 (tapers), R4"),
            new CheckDef("A4", "LINEWIDTH", State.Ratchet, "rendered paint width within 25% of the painted run", "M0 (tapers), R4"),
            new CheckDef("A5", "STRAY", State.Ratchet, "no paint where the plan has no line of its colour", "M0 (tapers), R4"),
            new CheckDef("A5b", "MISSING", State.Report, "every plan line drawn (report-only until the line model draws it)", "R4 (WP-11b)"),
            new CheckDef("B1", "JITTER", State.Ratchet, "no line jitters past V from its local circle", "R4"),
            new CheckDef("B2", "KINK", State.Ratchet, "no line kinks past V (facet sagitta)", "R4"),
            new CheckDef("B3", "CURVE", State.Ratchet, "no ribbon bends tighter than its class allows", "R4 (fans WP-19)"),
            new CheckDef("B4", "JUMP", State.Ratchet, "no line steps sideways past V where it continues", "R4 (WP-11b)"),
            new CheckDef("B4s", "SEAM", State.Zero, "no line steps at a tile seam (identical sections)", "now"),
            new CheckDef("C1", "GAP", State.Ratchet, "no solid line interrupted between its plan ends", "R4"),
            new CheckDef("C2", "END", State.Ratchet, "lines end only at legitimate ends", "R4"),
            new CheckDef("C3", "DASH", State.Ratchet, "dash and gap lengths along the chained line", "R4 (stubs WP-17)"),
            new CheckDef("D1", "CROSS", State.Ratchet, "no paint inside other pavement at the same level", "R4 (attach arcs WP-18b)"),
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
