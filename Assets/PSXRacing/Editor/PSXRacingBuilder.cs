using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Builds the whole game scene: configures asset importers, generates the
    /// circuit (road ribbon, walls, ground, start line), scatters the building /
    /// gas station / tree scenery, assembles the RX-7 cars, and wires the
    /// camera, HUD, audio and race management. Menu: PSX Racing > Build Scene.
    ///
    /// PARTIAL: the Charlotte city bake lives in PSXRacingBuilder.City.cs and
    /// shares the material/texture/car machinery here rather than growing a
    /// second copy of it.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string Root = "Assets/PSXRacing";
        const string GenDir = Root + "/Generated";
        const string MatDir = Root + "/Materials";
        /// <summary>Road markings this project draws for itself, because it
        /// owns no art for them. Under Art/ rather than Generated/ so that
        /// ConfigureTextureImporters sweeps them up with everything else —
        /// point filter, no mips, uncompressed.</summary>
        const string TrackTexDir = Root + "/Art/Track";

        static StringBuilder log = new StringBuilder();
        static Dictionary<Texture, Material> matByTex = new Dictionary<Texture, Material>();
        static Shader psxLit;

        // ------------------------------------------------------------------
        //  Which circuit is being built
        // ------------------------------------------------------------------
        /// <summary>The track currently under construction. The shape lives in
        /// runtime code (TrackCatalog) because the LifeSim's picker needs it
        /// too; only the LOOK of a circuit — which textures, how much scenery —
        /// is editor data, and that is <see cref="Theme"/> below.</summary>
        static TrackCatalog.TrackDef track;
        static Theme theme;

        static string ScenePathFor(TrackCatalog.TrackDef d) =>
            Root + "/Scenes/" + d.id + ".unity";

        /// <summary>Generated meshes are per-track, so their asset names are
        /// too. Four circuits sharing one "RoadMesh.asset" would be four scenes
        /// pointing at whichever one was baked last.</summary>
        static string MeshPrefix => track != null ? track.id + "_" : "";

        static float RoadWidth => track != null ? track.roadWidth : 12f;

        /// <summary>False on a drag strip, where the waypoint list has ends.
        /// Every ribbon in here used to close by walking one segment past the
        /// last waypoint back to the first; on a straight that segment is 700 m
        /// of road laid back down the strip on top of itself.</summary>
        // A LOOP stage (the 277 belt) is a stage whose ends meet, so it wraps
        // like a circuit: the closing segment is road.
        static bool Loop => track == null || (!track.drag && !(track.stage && !track.loop));

        internal const float WallOffset = 10f;
        // 2.4 m puts the top edge above the ~1.9 m chase-cam eyeline, so the
        // barrier silhouettes against the sky instead of hiding in the ground band.
        const float WallHeight = 2.4f;
        const float WallThick = 0.35f;
        /// <summary>Collider depth, grown outward from the visible face. The car
        /// covers ~1.6 m per physics tick at top speed, so a 0.35 m collider is
        /// thin enough to be stepped over even with continuous detection on.</summary>
        const float WallCollThick = 1.2f;
        /// <summary>How far past a chord's two ends WallFootLatticeMin looks
        /// for the lowest ground under a wall. It was the overlap between the
        /// per-chord wall boxes, which are gone (BuildWallSolid: overlapping
        /// boxes are what stopped cars dead); the footprint margin stays so
        /// the footings a span's abutment gets did not move with them.</summary>
        const float WallCollOverlap = 0.6f;
        internal const float KerbWidth = 0.9f;

        // ------------------------------------------------------------------
        //  The street curb (KerbStyle.Street). Built INSIDE KerbWidth: the
        //  roadbed dig (RoadbedToe), the shoulder profile's first point
        //  (EdgeProfileFn), the forecourt overlap (PadRoadOverlap) and
        //  TrackObstacleAudit's track half-width all key on that 0.9 m, so
        //  the curb stone and the pavement behind it share the footprint and
        //  nothing else moves.
        // ------------------------------------------------------------------
        /// <summary>Height of the curb stone's face above the tarmac: a 6 in
        /// US barrier curb. NOT a solid step to the car's BODY — the player
        /// box's bottom face sits ~9 cm over the tarmac and overhangs the
        /// wheel centre by 13 cm (WorldKit: "a solid 11 cm lip is a wall the
        /// car stops dead against"), so what the car COLLIDES with under this
        /// face is the ramp in <see cref="StreetKerbRamp"/>, never the face
        /// itself. The wheel raycast rides the ramp; the picture shows the
        /// stone.</summary>
        internal const float StreetKerbHeight = 0.15f;
        /// <summary>How far the top of the face leans OUT over its foot. Real
        /// curb stones are battered; 3 cm over 15 gives the face triangles
        /// normal.y ~ +0.19. (TrackObstacleAudit.AuditFacing no longer needs
        /// it: it skips near-vertical triangles, |normal.y| under 0.05, and
        /// flags only a face turned DOWN. The face is drawing only — the
        /// collider is the ramp below.)</summary>
        internal const float StreetKerbFaceBatter = 0.03f;
        /// <summary>Width of the curb stone's top, measured from the tarmac
        /// edge and including the face. The rest of <see cref="KerbWidth"/>
        /// (0.6 m) is the pavement slab behind it — narrower than a real
        /// 1.5 m sidewalk, deliberately: widening it means moving four
        /// constants and re-running every audit.</summary>
        internal const float StreetKerbTop = 0.30f;
        /// <summary>Length of the COLLIDER's rise from the tarmac edge to the
        /// curb top. 0.15 over 0.45 is a 33% grade, chosen against three
        /// numbers: its normal is 0.95 from vertical, above
        /// CollisionResponder.LandingNormalDot (0.7), so a body touch is a
        /// landing and never a crash; under the body's 0.13 m overhang the
        /// ramp has risen 4.3 cm against ~9 cm of clearance at stock ride
        /// height; and the obstacle audit's edge pass sees 0.043 m of rise over
        /// RoadsideRules.FaceRunM (0.13 m) against FaceRiseFailM (0.06), so
        /// it is a mountable curb and not an EDGE FACE. Starting value for
        /// tuning — 0.6 (25%)
        /// buys a slammed setup margin at the cost of the wheel visibly
        /// sinking further into the curb stone.</summary>
        internal const float StreetKerbRamp = 0.45f;
        /// <summary>Metres of road per repeat of StreetKerb.png along the
        /// curb — the same 2 m pitch the racing strip has always used, so the
        /// curb-stone joints land every 2 m and the pavement's every 1 m.</summary>
        internal const float StreetKerbUTile = 2f;

        /// <summary>
        /// What the strip along the tarmac edge IS on a venue.
        ///
        ///   Racing — the flat red/white ribbon a purpose-built circuit has.
        ///   Street — a raised concrete curb with a pavement slab behind it,
        ///            which is what a public road has. The owner's rule:
        ///            "there should not be racing red/white strips on city
        ///            streets. it should be concrete textured curbs."
        ///   Verge  — the same flat ribbon as Racing, drawn as gravel: a
        ///            stage's tarmac runs straight into its shoulder, and the
        ///            stage texture pass chooses what that shoulder is.
        /// </summary>
        internal enum KerbStyle { Racing, Street, Verge }
        /// <summary>Metres between the wall line and the nearest face of a
        /// building. Measured to the FACE, not to the building's origin, so it
        /// holds whatever mesh the scatter happens to pick.</summary>
        const float BuildingClearance = 2.5f;
        /// <summary>How far a building is buried below the lowest ground under
        /// its own footprint. These meshes have no floor, so a base level with
        /// the ground is a base you can see under — from a bumper camera, from
        /// a dip in the road, or from anywhere at all once the land is not
        /// flat.</summary>
        const float BuildingSink = 0.6f;
        const float Spacing = TrackCatalog.Spacing;

        // ------------------------------------------------------------------
        //  Themes — the LOOK of each circuit
        // ------------------------------------------------------------------
        /// <summary>
        /// What separates one circuit from another once the shape is decided:
        /// what the ground and the barriers are made of, what grows beside the
        /// road, and how much of it there is.
        ///
        /// Kept editor-side and keyed by track id, because every field is an
        /// asset path — the runtime catalog has no business knowing which JPEG
        /// a barrier is textured with. <see cref="ThemeFor"/> fails loudly on a
        /// track with no theme rather than quietly building a grey circuit.
        /// </summary>
        class Theme
        {
            public string road = Root + "/Art/GasStation/Textures/Road.jpg";
            public string ground = Root + "/Art/Roads/T (5).jpg";
            public string wall = Root + "/Art/Roads/T (2).jpg";
            public string tree = Root + "/Art/Roads/Ar (4).png";
            /// <summary>What runs along the edge of the tarmac. STREET by
            /// default, because a road is a road unless the theme says it is
            /// a circuit — "there should not be racing red/white strips on
            /// city streets. it should be concrete textured curbs." — and
            /// because <c>new Theme()</c> IS the downtown circuit. The airfield
            /// and the drag strips say Racing explicitly; a stage never reads
            /// this (<see cref="KerbStyleFor"/> answers Verge for it).</summary>
            public KerbStyle kerb = KerbStyle.Street;
            /// <summary>Metres of ground per texture repeat.</summary>
            public float groundTile = 9f;
            /// <summary>Amplitude of the rolling relief away from the road, in
            /// metres. The circuit itself is graded by its own height spline;
            /// this is only what the land does once it is out of the corridor,
            /// and it is per theme because a dockyard is dead flat and a
            /// mountain pass is not.</summary>
            public float relief = 3f;
            /// <summary>Waypoints between scenery of each kind; 0 means none of
            /// it. Waypoints are 4 m apart, so 9 is a building every 36 m.</summary>
            public int buildingEvery = 9, treeEvery = 4, parkedEvery = 11, lampEvery = 13;
            /// <summary>
            /// Waypoints between roadside delineator posts, both verges; 0 for
            /// none. THE cheapest sense-of-speed cue there is: Black Box's
            /// track designers put the feeling of speed in what streams past
            /// the edge of the frame, and a 1.2 m post every 12 m at 50 m/s is
            /// four of them a second per side, inside the widened FOV's
            /// periphery where the eye reads motion. Lamps are NOT densified
            /// for this — each one used to carry a 16 m additive night-glow
            /// quad (retired 2026-09-21), and each is now a candidate for one
            /// of StreetLights' twelve per-pixel slots, so doubling them would
            /// only halve how far down the road the lit ones reach; a post is
            /// twenty unlit vertices in one combined mesh per side.
            /// </summary>
            public int postEvery = 3;
            /// <summary>
            /// A stage's guard walls drawn as STEEL: a W-beam guardrail on
            /// posts instead of dry stone. Owner, 2026-09-26: "I would like to
            /// see more guardrails instead of always using stone barriers." The
            /// Parkway and Mount Mitchell keep their stone - the real roads'
            /// own masonry - and the state roads get what NCDOT puts up. The
            /// runs are the same runs and the collider the same solid; only
            /// what is drawn changes (see DrawGuardrail).
            /// </summary>
            public bool guardrail;
            /// <summary>Chance a candidate site is skipped, so a run of scenery
            /// reads as a street rather than as a fence. The city's trees were
            /// every 28 m with a third skipped and are now every 16 m with a
            /// quarter: the same reason as the posts, taller.</summary>
            public double buildingSkip = 0.25, treeSkip = 0.25;
            /// <summary>Ranks of trees behind the roadside one, and whether a
            /// site plants BOTH verges rather than alternating. A street tree
            /// is one rank, alternating; a wood is several, both sides. Owner,
            /// 2026-09-19: "Trees in this game are too sparse."</summary>
            public int treeRows = 1;
            public bool treeBothSides = false;
            public bool gasStation = true;

            // --------------------------------------------------------------
            //  Stage-only. Ignored entirely by a circuit.
            // --------------------------------------------------------------
            /// <summary>Where this stage's baked DEM, mask and generated art
            /// live, and the filename prefix inside it. Was hardcoded to BRP
            /// until there was a second region; a stage that shared the
            /// mountain's folder would load the mountain's heights and put a
            /// barrier island 1200 m up the Blue Ridge.</summary>
            public string stageDir = Root + "/Art/BRP";
            public string stagePrefix = "brp";
            /// <summary>Another stage's folder to take the GENERATED art and
            /// the copied tree billboards from, or null for this stage's own.
            /// Every forest stage composes the same atlases from the same
            /// pack; three copies were three megabytes of build each.</summary>
            public string artShareDir;
            /// <summary>Plant the billboard forest. Off on sand — the tree pass
            /// is the single most expensive thing a stage does, and a barrier
            /// island's vegetation is knee-high scrub nobody sees at 200 km/h.
            /// </summary>
            public bool stageForest = true;
            /// <summary>Plant houses, trailers and a couple of restaurants
            /// along a stage road. On for Emerald Isle — a drag strip through a
            /// beach TOWN — and meaningless on a bridge or a mountside.</summary>
            public bool stageHomes = false;
            /// <summary>Ground textures for the surface mask the bake writes
            /// beside the DEM. Null <see cref="sand"/> means the stage has no
            /// mask — which is what the mountain is, and why it is allowed to
            /// stay null rather than being given a beach it does not have.
            /// </summary>
            public string sand, water;
            /// <summary>Salt marsh. Falls back to <see cref="ground"/> when a
            /// coastal theme has not been given one, so a stage whose bake has
            /// marsh in it never renders a hole.</summary>
            public string marsh;
            /// <summary>Metres of ground per repeat for those three.</summary>
            public float sandTile = 8f, waterTile = 26f, marshTile = 6f;

            // --------------------------------------------------------------
            //  Stage LOOK. The defaults are the mountain's — every field here
            //  used to be a constant in PSXRacingBuilder.Stage.cs and the
            //  three shipped mountains read exactly those values through
            //  them. The Charlotte themes are what override them.
            // --------------------------------------------------------------
            /// <summary>An URBAN stage: the shoulder strip is concrete curb
            /// rather than the mountain's gravel or the coast's shell, the
            /// far ring is painted as ground rather than as autumn forest,
            /// and the ground carries no tint.</summary>
            public bool stageUrban;
            /// <summary>A barrier down both shoulders whatever the DEM does.
            /// A freeway has one; the mountain's rule ("only where the land
            /// falls five metres") would leave the 277 belt open.</summary>
            public bool stageWallAlways;
            /// <summary>Dig the ground out under each bridge span by the
            /// track's bridgeDepth, the way a circuit's field does. It can
            /// only ever LOWER ground. OFF on the mountains on purpose: their
            /// spans cross real gorges the DEM already has, and a dig there
            /// would move the abutments of three shipped stages by a metre.
            /// In a flat city SRTM reads street level under an overpass, so
            /// without it the terrain audit finds no daylight under a deck.
            /// </summary>
            public bool stageBridgeDig;
            /// <summary>Build the cut-bank pass. Off in a city: SRTM there
            /// reads ROOFS beside the road, and a "cut" solved against a
            /// roofline is a five-metre concrete face along the pavement of
            /// Tryon Street with the lots seated on top of it.</summary>
            public bool stageBanks = true;
            /// <summary>Fog band multiplier over the hour presets and the
            /// camera's far plane: mountain scale by default.</summary>
            public float fogScale = StageFogScale;
            public float farClip = StageFarClip;
            /// <summary>How far past the route the far (60 m) ground ring is
            /// built. 2300 m on a mountain is the far wall of the valley; a
            /// flat city has nothing out there to see and every far chunk is
            /// download.</summary>
            public float farCoverage = FarCoverageDefault;
            /// <summary>Tint on the near ground material, or null for none.
            /// The mountain's warm autumn tint stops its dirt reading
            /// grey-green against the orange mottle.</summary>
            public Color? groundTint = StageAutumnTint;
            /// <summary>Texture for the far ring, or null for the default
            /// (FallMottle on a forest stage, the ground itself on the coast).
            /// </summary>
            public string farGround;
            /// <summary>The CityProps kinds BuildStageHomes may draw from, or
            /// null for the beach-town mix. Bucketed by kind — towers, mid-
            /// rise blocks, everything else — and dealt by distance from
            /// uptown, so one list serves a venue that runs from the core to
            /// the suburbs.</summary>
            public byte[] stageProps;
        }

        static readonly Dictionary<string, Theme> Themes = new Dictionary<string, Theme>
        {
            // Downtown: gravel verges, concrete barriers, street trees, and the
            // gas station on the back straight this circuit was designed around.
            ["CityCircuit"] = new Theme(),

            // Docks: concrete slab everywhere, corrugated hoarding instead of
            // barriers, warehouses shoulder to shoulder, and nothing growing.
            ["HarborPoint"] = new Theme
            {
                ground = Root + "/Art/Roads/T (4).jpg",
                wall = Root + "/Art/GasStation/Textures/MetalPlates.jpg",
                groundTile = 12f,
                relief = 1.2f,          // reclaimed dock land, graded flat
                buildingEvery = 7, buildingSkip = 0.12,
                treeEvery = 0,
                parkedEvery = 9,
                lampEvery = 9,
                // Every circuit has a forecourt now. Fuel burns in real time and
                // is bought at the pumps, so a circuit without them is one the
                // player cannot finish a long race on — the station stopped
                // being set dressing the moment the nozzle became a control.
                gasStation = true,
            },

            // Out of town: dirt and grass, dry-stone walling, trees close enough
            // to the road to matter, and almost nothing built.
            ["RidgePass"] = new Theme
            {
                ground = Root + "/Art/GasStation/Textures/Ground.jpg",
                wall = Root + "/Art/Roads/T (3).jpg",
                tree = Root + "/Art/Roads/Ar (6).png",
                groundTile = 14f,
                relief = 13f,           // the hillside the pass is cut into
                // Barely any: a 12 m apartment slab on a mountain pass reads as
                // a mistake, so what few there are should be landmarks.
                buildingEvery = 34, buildingSkip = 0.55,
                treeEvery = 4, treeSkip = 0.15,
                // A pass through a wood, not a lane with a tree every 32 m.
                treeRows = 3, treeBothSides = true,
                parkedEvery = 0,
                lampEvery = 24,
                gasStation = true,
            },

            // An airfield: tarmac to the horizon, slab walls, a hangar here and
            // there, and a line of trees along the perimeter.
            ["AirfieldSprint"] = new Theme
            {
                ground = Root + "/Art/Roads/T (1).jpg",
                wall = Root + "/Art/Roads/T (4).jpg",
                tree = Root + "/Art/Roads/Ar (5).png",
                groundTile = 16f,
                relief = 1f,            // an airfield is chosen for being flat
                buildingEvery = 17, buildingSkip = 0.35,
                treeEvery = 7, treeSkip = 0.3,
                // The perimeter line is a shelter belt: two ranks deep.
                treeRows = 2,
                parkedEvery = 19,
                lampEvery = 11,
                gasStation = true,
                // The one venue whose blurb is about racing rather than about
                // a place: perimeter-track tarmac, slab walls, hangars — the
                // Silverstone/Goodwood lineage, and the archetype of a
                // purpose-built circuit. It keeps the red/white.
                kerb = KerbStyle.Racing,
            },

            // Both strips share one look: fresh prepped tarmac, concrete walls
            // the length of it, timing towers standing in as the only buildings,
            // and light poles close enough together to read as speed.
            ["DragQuarter"] = DragTheme(),
            ["DragEighth"] = DragTheme(),

            // The parkway: real terrain, a fall forest the stage plants
            // itself, low stone guard walls, and NOTHING built — no lamps, no
            // buildings, no pumps, which is what the road is like. The zeros
            // are load-bearing: the stage runs its own forest pass instead of
            // PlaceTrees, and every other scatter pass stays off.
            ["BlueRidge"] = new Theme
            {
                ground = Root + "/Art/GasStation/Textures/Ground.jpg",
                wall = Root + "/Art/Roads/T (3).jpg",   // dry stone — the parkway's own guard wall
                groundTile = 13f,
                relief = 0f,                            // the DEM is the relief
                buildingEvery = 0, treeEvery = 0, parkedEvery = 0, lampEvery = 0,
                // Reflector posts behind the guard walls every 16 m — the one
                // near-road vertical a stage can carry without breaking the
                // "nothing built" rule, and the real parkway has them.
                postEvery = 4,
                gasStation = false,
            },

            // Mount Mitchell. The same mountain as the Parkway and the same
            // look, so it borrows the Parkway's theme wholesale and changes
            // only where its bake lives. What it does NOT share is the
            // altitude: this one starts where the Parkway is and climbs 432 m
            // above it, to 2,003 m — the highest tarmac in eastern America and
            // above the hardwood line, which is why the forest thins toward the
            // top all by itself: the tree pass reads the DEM.
            ["MtMitchell"] = new Theme
            {
                ground = Root + "/Art/GasStation/Textures/Ground.jpg",
                wall = Root + "/Art/Roads/T (3).jpg",
                groundTile = 13f,
                relief = 0f,                            // the DEM is the relief
                buildingEvery = 0, treeEvery = 0, parkedEvery = 0, lampEvery = 0,
                // Reflector posts behind the guard walls every 16 m — the one
                // near-road vertical a stage can carry without breaking the
                // "nothing built" rule, and the real parkway has them.
                postEvery = 4,
                gasStation = false,
                stageDir = Root + "/Art/MtMitchell",
                stagePrefix = "mtm",
                artShareDir = Root + "/Art/BRP",
            },

            // NC 215 off the Parkway at Beech Gap. It wears the Parkway's look
            // for the reason Mount Mitchell does: same mountains, same rock in
            // the cuts, same hardwood on the shoulders. What differs is the DEM
            // it reads, which is the whole of what a stage theme decides.
            ["BeechGap"] = new Theme
            {
                ground = Root + "/Art/GasStation/Textures/Ground.jpg",
                wall = Root + "/Art/Roads/T (3).jpg",
                groundTile = 13f,
                relief = 0f,
                buildingEvery = 0, treeEvery = 0, parkedEvery = 0, lampEvery = 0,
                // Reflector posts behind the guard walls every 16 m — the one
                // near-road vertical a stage can carry without breaking the
                // "nothing built" rule, and the real parkway has them.
                postEvery = 4,
                guardrail = true,                       // NC 215 is a state road
                gasStation = false,
                stageDir = Root + "/Art/BeechGap",
                stagePrefix = "beech",
                artShareDir = Root + "/Art/BRP",
            },

            // The two Parkway LOOPS: a section of the Parkway and the roads
            // that meet it, closed into a ring. Same mountain look as the
            // three stages above — the DEM folder is the only thing that is
            // theirs.
            ["BlowingRock"] = MountainLoopTheme(Root + "/Art/BlowingRock", "brock"),
            ["LittleSwitzerland"] = MountainLoopTheme(Root + "/Art/Switzerland", "swiss"),

            // The sprints of 2026-09-26 with roads of their own: the same
            // mountain look, each reading its own DEM. (The Blowing Rock
            // sprint races in the loop's scene and needs none.)
            ["ChimneyRock"] = MountainLoopTheme(Root + "/Art/ChimneyRock", "chimney"),
            ["SwissNC226A"] = MountainLoopTheme(Root + "/Art/Swiss226A", "swa"),
            ["GillespieGap"] = MountainLoopTheme(Root + "/Art/Gillespie", "gap"),

            // Bogue Banks. One look, three venues: pale sand, scrub behind the
            // dune line, water on both sides of everything. All three share a
            // folder because they share an island; the DEM PREFIX is what keeps
            // their three bakes apart.
            ["EmeraldIsle"] = EmeraldTheme(),
            ["LangstonBridge"] = BogueTheme(),
            ["AtlanticBeachBridge"] = BogueTheme(),

        };


        /// <summary>
        /// The Crystal Coast. Everything here is a consequence of the ground
        /// being sand at sea level: no forest pass, no relief (the DEM is as
        /// flat as the island), concrete parapet rather than stone, and the two
        /// extra ground materials the surface mask needs.
        /// </summary>
        static Theme BogueTheme() => new Theme
        {
            ground = Root + "/Art/Bogue/Gen/Scrub.png",
            sand = Root + "/Art/Bogue/Gen/Sand.png",
            water = Root + "/Art/Bogue/Gen/Sea.png",
            marsh = Root + "/Art/Bogue/Gen/Marsh.png",
            wall = Root + "/Art/Roads/T (4).jpg",   // concrete — a bridge parapet
            groundTile = 11f,
            sandTile = 7f,
            waterTile = 24f,
            // Tighter than the scrub: cordgrass is a fine texture and at 11 m
            // it smears into a flat olive field from the bridge deck.
            marshTile = 5.5f,
            relief = 0f,                            // the DEM is the relief
            stageDir = Root + "/Art/Bogue",
            // Null on purpose: three tracks share this theme and each has its
            // own bake, so the prefix comes off the TRACK (see StagePrefix)
            // rather than being three near-identical Theme literals.
            stagePrefix = null,
            stageForest = false,
            buildingEvery = 0, treeEvery = 0, parkedEvery = 0, lampEvery = 0,
            gasStation = false,
        };

        /// <summary>Emerald Isle is the Bogue look plus the town: the quarter
        /// mile runs down a real residential drive, so it gets the houses.</summary>
        static Theme EmeraldTheme()
        {
            var t = BogueTheme();
            t.stageHomes = true;
            return t;
        }

        /// <summary>The Parkway's look, pointed at a loop's own DEM folder -
        /// with the state roads' steel guardrail in place of its stone.</summary>
        static Theme MountainLoopTheme(string dir, string prefix) => new Theme
        {
            ground = Root + "/Art/GasStation/Textures/Ground.jpg",
            wall = Root + "/Art/Roads/T (3).jpg",
            groundTile = 13f,
            relief = 0f,
            buildingEvery = 0, treeEvery = 0, parkedEvery = 0, lampEvery = 0,
            postEvery = 4,
            // US 221, NC 226A, NC 226, US 64: state roads, W-beam on posts.
            guardrail = true,
            gasStation = false,
            stageDir = dir,
            stagePrefix = prefix,
            artShareDir = Root + "/Art/BRP",
        };

        static Theme DragTheme() => new Theme
        {
            ground = Root + "/Art/Roads/T (1).jpg",
            wall = Root + "/Art/Roads/T (4).jpg",
            groundTile = 14f,
            // A strip is a prepped surface on a prepped site. The default 3 m of
            // relief would put rolling hills either side of a quarter mile,
            // which is the one place in the game where flat is the point.
            relief = 0.8f,
            // Sparse and one-sided-ish: a strip is a wall, a fence and a lot of
            // nothing, and the reference for speed is the light poles going past.
            buildingEvery = 24, buildingSkip = 0.45,
            treeEvery = 0,
            parkedEvery = 0,
            lampEvery = 7,
            // Every 8 m. There is no verge on a strip — 18 m of tarmac inside
            // a 10 m barrier line — so this pitch is taken by the wall's own
            // seam posts (see PlacePosts), which is what a strip's wall looks
            // like anyway.
            postEvery = 2,
            gasStation = false,
            // A strip is a purpose-built racing surface, and its kerb band
            // (9.0-9.9 m out) is mostly behind the wall at 9.8 m anyway.
            // Left as the red/white to bound the change; a plain white line
            // over concrete would be truer and is one enum value away.
            kerb = KerbStyle.Racing,
        };

        /// <summary>For the self-test: every catalog track needs a theme, and a
        /// missing one only shows up as a circuit that quietly looks like the
        /// city one.</summary>
        public static bool HasTheme(string id) => Themes.ContainsKey(id);

        static Theme ThemeFor(TrackCatalog.TrackDef d)
        {
            if (Themes.TryGetValue(d.id, out var t)) return t;
            Log("WARN: no theme for track '" + d.id + "' — using the city one.");
            return Themes["CityCircuit"];
        }

        /// <summary>
        /// The kerb style for a venue — the per-venue accessor the audits and
        /// the self-test read, mirroring <see cref="WallOffsetFor"/>.
        ///
        /// A stage's strip is its VERGE whatever its theme says: the stage
        /// texture pass draws that strip's Shoulder.png, and a raised curb on
        /// a stage would need the guard wall and the falling verge rethought
        /// (a later pass gives Charlotte's stages an urban concrete
        /// Shoulder.png through the same texture pass, not through this).
        /// Null is Street, the Theme default, so nothing here can hand a
        /// venue the red/white by accident.
        /// </summary>
        internal static KerbStyle KerbStyleFor(TrackCatalog.TrackDef d) =>
            d == null ? KerbStyle.Street
            : d.stage ? KerbStyle.Verge
            : ThemeFor(d).kerb;

        /// <summary>Filename prefix for the current stage's bake. The theme may
        /// name one (the parkway does — "brp"); otherwise the track's own
        /// Resources key is it, which is how three Bogue Banks venues share one
        /// theme without sharing one another's terrain.</summary>
        static string StagePrefix =>
            !string.IsNullOrEmpty(theme.stagePrefix) ? theme.stagePrefix : track.stageData;

        [MenuItem("PSX Racing/Build Scene")]
        public static void Build()
        {
            log = new StringBuilder();
            matByTex.Clear();
            try
            {
                Log("PSX Racing scene build started " + DateTime.Now);
                EnsureFolders();
                GenerateTrackTextures();
                ConfigureTextureImporters();
                ConfigureSkyImporters();
                ConfigureAudioImporters();
                ConfigureAudioVoiceLimits();
                EnsureRoadLayer();
                psxLit = Shader.Find("PSX/Lit");
                if (psxLit == null) throw new Exception("PSX/Lit shader not found — did shaders compile?");

                // Bake the body shells before the scene exists: BuildCars fits
                // one to each car on the grid, and RaceHandoffApplier loads them
                // out of Resources when the LifeSim hands over a field.
                CarModelBaker.Bake();
                foreach (var line in CarModelBaker.LastLog) Log("  model " + line);

                // The LifeSim props (houses, trailers, restaurants) bake next:
                // Charlotte's streamed tiles load them from Resources at
                // runtime, and the Emerald Isle town pass instantiates the same
                // prefabs at build time below.
                BakeCityProps();

                DeleteLegacyMeshes();

                // LifeHome is scene 0 — the boot scene, and where RaceManager
                // returns to — then one scene per circuit IN CATALOG ORDER.
                // TrackCatalog.SceneIndex is the other half of this contract.
                if (!File.Exists(LifeHomeSceneBuilder.ScenePath))
                    LifeHomeSceneBuilder.Build();
                var scenes = new List<EditorBuildSettingsScene>
                {
                    new EditorBuildSettingsScene(LifeHomeSceneBuilder.ScenePath, true),
                };

                foreach (var def in TrackCatalog.Scened)
                    scenes.Add(new EditorBuildSettingsScene(
                        def.city ? BuildCityScene(def) : BuildTrack(def), true));

                // The walk-in garage goes LAST, after every circuit, because
                // TrackCatalog.SceneIndex addresses tracks by their position in
                // this list. TrackCatalog.GarageSceneIndex is the other half of
                // that contract.
                GarageSceneBuilder.Build();
                PizzeriaSceneBuilder.Build();
                BuildTownScene();
                SellerLotSceneBuilder.Build();
                BuildNeighborhoodScene();

                // Written from SceneOrder rather than from the list assembled
                // as we went, so the build settings and the WebGL player are the
                // same list BY CONSTRUCTION and not by agreement. They were
                // maintained separately and drifted: the player shipped without
                // the pizza shop, and GO TO WORK silently did nothing.
                scenes.Clear();
                foreach (var p in SceneOrder())
                    scenes.Add(new EditorBuildSettingsScene(p, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Log($"BUILD OK — {TrackCatalog.SceneCount} venues ({TrackCatalog.Count} with reverses) " +
                    "+ home + garage + shop + town + street + neighbourhood.");
            }
            catch (Exception e)
            {
                Log("BUILD FAILED: " + e.Message + "\n" + e.StackTrace);
                Debug.LogException(e);
            }
            finally
            {
                File.WriteAllText(ProjectRootPath("PSXRacing_build_log.txt"), log.ToString());
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// Build one circuit into its own scene and return the path.
        ///
        /// Everything below the line is exactly what the single-track builder
        /// did; the only change is that the shape and the look now come from
        /// <see cref="track"/> and <see cref="theme"/> rather than from consts.
        /// </summary>
        /// <summary>StageLab: one venue's scene, with the setup Build() does
        /// first - held-back venues included, so a road the builder does not
        /// yet handle can be shaped without shipping it.</summary>
        public static string BuildOneForLab(TrackCatalog.TrackDef def)
        {
            log = new StringBuilder();
            EnsureFolders();
            GenerateTrackTextures();
            EnsureRoadLayer();
            psxLit = Shader.Find("PSX/Lit");
            if (psxLit == null) throw new Exception("PSX/Lit shader not found");
            string path = BuildTrack(def);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_lab_build_log.txt"), log.ToString());
            return path;
        }

        static string BuildTrack(TrackCatalog.TrackDef def)
        {
            track = def;
            theme = ThemeFor(def);
            // Materials are cached by texture path and by key, and two circuits
            // legitimately want a "Ground" material off different textures.
            // Clearing per track keeps the key namespace per track as well.
            matByTex.Clear();
            matByKey.Clear();
            ClearSeasonEntries();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var waypoints = BuildWaypoints(out float[] curvatures);
            Log($"--- {def.name} ({def.id}): {waypoints.Count} waypoints, " +
                $"~{waypoints.Count * Spacing:0} m, {def.laps} laps, {def.roadWidth:0.0} m wide");

            var pathGO = new GameObject("Track");
            var path = pathGO.AddComponent<TrackPath>();
            path.waypoints = waypoints.ToArray();
            path.curvatures = curvatures;
            path.spacing = Spacing;
            path.roadWidth = RoadWidth;
            // PRESENTATION, not geometry: TrackPath.drag is what unlocks the
            // top-down camera and the trap-speed readout, and a bridge run
            // wants both on a route that is emphatically not a synthetic strip.
            // TrackPath.pointToPoint carries the geometry, and HasEnds is their
            // union, so a stage with dragEvent gets clamped index walks either
            // way.
            path.drag = def.IsDragEvent;
            // A loop stage has no ends: RaceManager counts its laps at
            // waypoint 0 exactly as it does a circuit's.
            path.pointToPoint = def.stage && !def.loop;
            path.finishIndex = def.FinishIndex;
            path.dragLabel = def.dragLabel;

            // A stage's ground truth is a real DEM rather than a field derived
            // from the road. Loaded before ANY height is asked for, because
            // GroundHeightAt silently answers for whichever world is loaded.
            if (def.stage) StageLoadDem();
            else StageUnloadDem();

            // Before anything that has to sit ON the ground, which is
            // everything below: the road is the only thing here whose height is
            // its own, and the land is graded to it rather than the other way
            // round. (On the stage the roles flip — the road came FROM the real
            // land — but the corridor pinning below still reads this field's
            // bridge table.)
            BuildTerrainField(waypoints);

            // Before the ground mesh and before the barriers, both of which
            // read the result: the forecourt flattens the land under itself and
            // takes a bite out of the wall line, and neither is something that
            // can be done to geometry after it has been generated.
            PlanFuelStop(waypoints);

            // A stage decides its roadside — which stations are warranted a
            // wall, a bridge parapet or its approach run, a cut, a graded
            // fill — before anything is built from that decision: the
            // shoulder profile BuildRoad emits, the ground, the walls, the
            // banks and the posts all read it.
            if (def.stage) PlanStageRoadside(waypoints);

            BuildRoad(waypoints, pathGO.transform);
            BuildKerbs(waypoints, pathGO.transform);
            if (def.stage) { BuildStageWalls(waypoints, pathGO.transform);
                             if (theme.stageBanks) BuildStageBanks(waypoints, pathGO.transform); }
            else BuildWalls(waypoints, pathGO.transform);
            if (def.stage) BuildStageGround(waypoints, pathGO.transform);
            else BuildGround(waypoints, pathGO.transform);
            if (def.stage) BuildStageTunnels(waypoints, pathGO.transform);
            BuildBridges(waypoints, pathGO.transform);
            BuildStartLine(waypoints, pathGO.transform);
            if (def.stage && theme.stageForest) BuildStageForest(waypoints, pathGO.transform);
            else BuildScenery(waypoints, pathGO.transform);
            if (def.stage && theme.stageHomes) BuildStageHomes(waypoints, pathGO.transform);

            var lightGO = BuildLighting();
            var cars = BuildCars(waypoints);
            var player = cars[0];
            BuildCameraAndHUD(player, cars, path, lightGO.GetComponent<Light>());

            var systems = new GameObject("GameSystems");
            systems.AddComponent<PSXBootstrap>();
            systems.AddComponent<TouchControls>();
            var menu = systems.AddComponent<PauseMenu>();
            menu.playerCar = player;

            // Getting out at the pumps. Added on every circuit, including the
            // ones with no forecourt: it does nothing at all unless GasPump
            // says the car is standing at a nozzle, and a component that costs
            // one branch a frame is cheaper than a per-track special case.
            var forecourt = systems.AddComponent<PSXRacing.OnFoot.ForecourtMode>();
            forecourt.playerCar = player;
            forecourt.carInput = player.GetComponent<PlayerCarInput>();
            forecourt.engine = player.GetComponent<EngineAudio>();
            var psxCam = GameObject.Find("PSXCamera");
            if (psxCam != null)
            {
                forecourt.raceCamera = psxCam.GetComponent<Camera>();
                forecourt.chase = psxCam.GetComponent<ChaseCamera>();
            }

            AttachSeasonDress();

            string scenePath = ScenePathFor(def);
            EditorSceneManager.SaveScene(scene, scenePath);
            Log("Scene saved: " + scenePath);
            return scenePath;
        }

        /// <summary>
        /// Remove the unprefixed meshes the single-track builder left behind.
        /// They are not referenced by anything once every circuit names its own,
        /// and an orphan RoadMesh.asset in Generated is the sort of thing that
        /// gets picked up by mistake a year later.
        /// </summary>
        static void DeleteLegacyMeshes()
        {
            string[] legacy =
            {
                "RoadMesh", "KerbMeshL", "KerbMeshR", "WallMeshL", "WallMeshR",
                "GroundMesh", "TreeMesh",
            };
            int n = 0;
            foreach (var name in legacy)
            {
                string p = GenDir + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(p) != null &&
                    AssetDatabase.DeleteAsset(p)) n++;
            }
            if (n > 0) Log($"Removed {n} single-track mesh assets from {GenDir}.");
        }

        static string ProjectRootPath(string file) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, file);

        static void Log(string msg) { log.AppendLine(msg); Debug.Log("[PSXBuild] " + msg); }

        static void EnsureFolders()
        {
            foreach (var dir in new[] { GenDir, MatDir, Root + "/Scenes", TrackTexDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                }
            }
        }

        // ------------------------------------------------------------------
        //  Road markings
        // ------------------------------------------------------------------
        /// <summary>
        /// Draw the kerb stripe and the start-line chequer into Art/Track.
        ///
        /// Both used to be Art/GasStation/Textures/Checker.png, which the
        /// filename promises is a chequerboard and which is in fact a
        /// PHOTOGRAPH OF A PUNCH CLOCK -- the asset pack ships it as the face of
        /// the time recorder on the back-office wall, teal case and all. So the
        /// kerb down both sides of all six circuits was a mile of wall clocks
        /// laid end to end, and so was the start line. Nothing caught it because
        /// nothing in the build ever looks at a texture, only at its path, and
        /// the path read exactly right.
        ///
        /// Drawn rather than sourced: two flat bands and a chequerboard are less
        /// code than an art pipeline, they are already the right palette for a
        /// machine that dithers to 16 bit, and a texture the builder draws for
        /// itself cannot be quietly replaced by a punch clock.
        /// </summary>
        static void GenerateTrackTextures()
        {
            // Charlotte's surfaces first, and for EVERY venue rather than
            // lazily when a track asks: ConfigureTextureImporters runs straight
            // after this and is what makes these point-filtered with no mips.
            // A texture written later in the build misses that pass and gets
            // Unity's defaults — bilinear and mipmapped, which averages a
            // 12 cm lane marking away to nothing by the second mip level. The
            // city textures had that bug and only escaped it because a SECOND
            // build configured what the first one drew.
            EnsureCityFolders();
            GenerateCityTextures();
            EnsureConcreteTex();
            foreach (var def in TrackCatalog.Scened)
            {
                if (def.city) continue;      // the city draws its own by class
                // All four, not just the one this circuit turns out to want.
                // BuildRoad picks its asphalt age and its deck surface from the
                // geometry, which does not exist yet at texture time — and a
                // texture written later in the build misses the importer pass
                // above and comes back bilinear and mipmapped. Four 256x64
                // PNGs per venue is nothing; a coordination channel between
                // here and there would have been the expensive part.
                // One-way (dashes only, no double yellow) for a strip AND
                // for a one-way real road: the 277 belt and Independence
                // are single carriageways.
                for (int s = 0; s < CityMeshes.SurfaceCount; s++)
                    EnsureTrackRoadTex(def.roadWidth, def.drag || def.oneWay, (CityMeshes.Surface)s);
            }

            // Bands run ACROSS the direction of travel. BuildKerbs lays u along
            // the road at one repeat per 2 m, so two bands is the 1 m red/white
            // dashing a real kerb has.
            WriteTexture(KerbTexPath, 32, 16, (x, y) =>
            {
                // A dark line down each long edge. The kerb is 0.9 m of high
                // chroma between grey tarmac and grey gravel, and with no edge
                // to it it reads as a light source rather than as a raised
                // strip -- especially at night, which is when it matters.
                if (y == 0 || y == 15) return new Color32(48, 42, 40, 255);
                return x < 16 ? new Color32(178, 34, 36, 255)
                              : new Color32(214, 210, 202, 255);
            });

            // The street curb, for KerbStyle.Street venues. Drawn HERE, right
            // after Kerb.png and never lazily from BuildKerbs: anything
            // written after ConfigureTextureImporters ships bilinear and
            // mipmapped, and a 2 px joint is the first thing a mip averages
            // away.
            //
            // u runs along the road (64 px per StreetKerbUTile = 2 m, so
            // 3.1 cm/px) and v across the PROFILE, in the three bands the
            // street mesh assigns: rows 0-7 (v 0..0.25) are the curb FACE,
            // 8-15 (0.25..0.5) the curb stone's TOP, 16-31 (0.5..1) the
            // PAVEMENT slab. Colours come off SurfaceBase — RG2's concrete
            // hexes — so the curb matches the bridge decks and Charlotte's
            // own concrete rather than being a fifth grey. 64x32 is far under
            // the 256 ceiling, and at 240 lines a 2 px joint is 6 cm and
            // reads.
            WriteTexture(StreetKerbTexPath, 64, 32, (x, y) =>
            {
                var old = SurfaceBase[(int)CityMeshes.Surface.ConcreteOld];
                var fresh = SurfaceBase[(int)CityMeshes.Surface.ConcreteNew];
                // The same grain amplitude Grain() gives concrete (20), off a
                // different noise offset so the curb does not repeat the
                // junction slab pixel for pixel.
                float g = (Noise(x + 31, y + 7) - 0.5f) * 20f;
                if (y < 8)
                {
                    // FACE: weathered concrete in shadow, ~18% down. No joints
                    // along it — at 3 cm/px a joint on a 15 cm face would
                    // read as a crack.
                    float d = g - 28f;
                    return new Color32(Chan(old.r, d), Chan(old.g, d), Chan(old.b, d + 2f), 255);
                }
                if (y < 16)
                {
                    // TOP of the curb stone: a joint every repeat (precast
                    // stones are 1-2 m long) and the arris highlight along
                    // the row where the top meets the face.
                    float d = g;
                    if (x < 2) d -= 34f;
                    else if (y == 8) d += 12f;
                    return new Color32(Chan(old.r, d), Chan(old.g, d), Chan(old.b, d + 2f), 255);
                }
                // PAVEMENT: fresh concrete ~10% down, slabs every metre (the
                // repeat's joint at x 0-1 and a second at 32-33), the seam
                // against the curb stone (row 16) and the back edge (row 31)
                // both dark so the slab has an edge instead of fading into the
                // run-off behind it.
                float p = g - 18f;
                if (x < 2 || x == 32 || x == 33 || y == 16 || y == 31) p -= 34f;
                return new Color32(Chan(fresh.r, p), Chan(fresh.g, p), Chan(fresh.b, p + 2f), 255);
            });

            // One 2x2 cell, tiled 8x2 by BuildStartLine: 16 squares across the
            // road and 4 along it.
            WriteTexture(GridTexPath, 32, 32, (x, y) =>
                ((x < 16) ^ (y < 16)) ? new Color32(18, 18, 20, 255)
                                      : new Color32(220, 218, 212, 255));

            // A delineator post, seen from the side: v runs up the post. The
            // bottom 29 rows are the white shaft, the top three the red cap —
            // 3/32 of a 1.22 m post is 11 cm of red. Written rather than drawn
            // because the two colours ARE the design.
            WriteTexture(PostTexPath, 8, 32, (x, y) =>
                y >= 29 ? new Color32(190, 36, 34, 255)
                        : new Color32(226, 224, 218, 255));

            // A W-beam guardrail, one station (4 m) of it. Rows 0-23 run up the
            // beam from its bottom lip to its top lip; rows 24-31 are the post,
            // in four 8-texel columns. The geometry carries the corrugation
            // (DrawGuardrail); this makes it galvanised steel: highlights on
            // the two ridges, grime in the valley and on the lips, a pair of
            // bolt heads in the valley at each post (x 0 and 16 - posts every
            // 2 m), and a faint roll grain along the beam.
            WriteTexture(GuardrailTexPath, 32, 32, (x, y) =>
            {
                uint h = (uint)(x * 374761393 + y * 668265263) + 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                int n = (int)((h >> 8) & 0x0F);
                if (y >= 24)
                {
                    int c = x % 8;
                    int p = 108 + n / 2 + (c == 0 || c == 7 ? -26 : c == 1 ? 14 : 0);
                    return new Color32((byte)p, (byte)p, (byte)(p + 5), 255);
                }
                int g = 146 + n / 2 + ((x * 7) % 13) - 6;
                if (y <= 1 || y >= 22) g -= 18;                    // the lips
                else if (y >= 5 && y <= 7) g += 24;                // lower ridge
                else if (y >= 16 && y <= 18) g += 24;              // upper ridge
                else if (y >= 10 && y <= 13) g -= 24;              // the valley
                bool bolt = y >= 11 && y <= 12 && (x % 16 == 1 || x % 16 == 2);
                if (bolt) g = 62;
                if (x == 31) g -= 14;                               // the splice lap
                byte b = (byte)Mathf.Clamp(g, 0, 255);
                return new Color32(b, b, (byte)Mathf.Clamp(g + 6, 0, 255), 255);
            });

            // A bridge expansion joint, seen from a car: two steel angle plates
            // with the finger gap between them, dark with the grease and grit
            // that collects in it. v runs ACROSS the band (along the road), so
            // the gap is the middle third and the plates are the outer thirds.
            //
            // Shared rather than per-theme: four circuits have bridges too, and
            // the parkway's eight spans have exactly the same joints on them —
            // they were simply never drawn.
            WriteTexture(JointTexPath, 16, 16, (x, y) =>
            {
                int band = y * 3 / 16;                 // 0 plate, 1 gap, 2 plate
                uint h = (uint)(x * 374761393 + y * 668265263) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                int n = (int)((h >> 8) & 0x0F);
                if (band == 1)
                {
                    // The gap: near black, with the odd glint off whatever is
                    // wedged in it.
                    byte v = (byte)(22 + n / 2);
                    return new Color32(v, v, (byte)(v + 3), 255);
                }
                // Galvanised steel, streaked along the band so it reads as
                // rolled plate rather than as noise.
                byte s = (byte)(118 + ((x * 5) % 11) * 4 + n / 3);
                return new Color32(s, s, (byte)(s + 6), 255);
            });
        }

        static string KerbTexPath => TrackTexDir + "/Kerb.png";
        internal static string StreetKerbTexPath => TrackTexDir + "/StreetKerb.png";
        static string GridTexPath => TrackTexDir + "/StartGrid.png";
        static string JointTexPath => TrackTexDir + "/Joint.png";
        static string PostTexPath => TrackTexDir + "/Post.png";
        static string GuardrailTexPath => TrackTexDir + "/Guardrail.png";

        /// <summary>Write a PNG, but only when it would differ from the one
        /// already there. Rewriting two textures unconditionally costs a
        /// reimport on every build, and reimporting a texture invalidates every
        /// material pointing at it.</summary>
        static void WriteTexture(string path, int w, int h, Func<int, int, Color32> shade)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = shade(x, y);
            tex.SetPixels32(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            if (File.Exists(full))
            {
                var have = File.ReadAllBytes(full);
                if (have.Length == png.Length)
                {
                    bool same = true;
                    for (int i = 0; i < png.Length; i++)
                        if (have[i] != png[i]) { same = false; break; }
                    if (same) return;
                }
            }
            File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Log("Drew " + path);
        }
        // ------------------------------------------------------------------
        //  Importers
        // ------------------------------------------------------------------
        static void ConfigureTextureImporters()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art" });
            int n = 0;
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;
                // 256 everywhere but the exemptions PSXTextureCaps names.
                int wantMax = PSXTextureCaps.MaxFor(p);
                bool dirty = imp.filterMode != FilterMode.Point || imp.mipmapEnabled ||
                             imp.textureCompression != TextureImporterCompression.Uncompressed ||
                             (wantMax != 256 && imp.maxTextureSize != wantMax);
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                // 256 is the PS1's own texture-page ceiling, so this is both the
                // authentic look and a 4x cut in download size for mobile.
                imp.maxTextureSize = wantMax;
                imp.wrapMode = TextureWrapMode.Repeat;
                if (p.EndsWith(".png")) imp.alphaIsTransparency = true;
                if (dirty) { imp.SaveAndReimport(); n++; }
            }
            Log($"Configured {n} texture importers (point filter, no mips).");
        }

        /// <summary>Where the sky panoramas live. Under Resources because
        /// TimeOfDay swaps them at runtime when the player picks an hour, and a
        /// texture reachable only through a material asset would ship the one
        /// baked into the scene and nothing else.</summary>
        const string SkyTexDir = Root + "/Resources/Sky";

        static Texture2D SkyPanoramaFor(TimeOfDay.Preset hour) =>
            string.IsNullOrEmpty(hour.skyTex) ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexDir + "/" + hour.skyTex + ".png");

        /// <summary>
        /// The panorama's spin at BAKE time — the same arithmetic
        /// TimeOfDay.SkyRotationFor does at runtime, off the hour's own sun
        /// angle rather than off a Light that does not exist yet.
        ///
        /// Two copies of one formula is a thing worth flinching at, and the
        /// alternative is worse: the runtime one has to read the live light
        /// (a scene can aim its sun where it likes) and this one has to run
        /// before there is a scene. The screenshot pass is what keeps them
        /// honest — a sign error here puts the sunset in the wrong quarter of
        /// the sky and every hour frame shows it.
        /// </summary>
        static float BakedSkyRotation(TimeOfDay.Preset hour)
        {
            Vector3 toSun = -(Quaternion.Euler(hour.sunEuler) * Vector3.forward);
            if (new Vector2(toSun.x, toSun.z).sqrMagnitude < 1e-6f) return 0f;
            float worldAzi = Mathf.Atan2(toSun.z, toSun.x) * Mathf.Rad2Deg;
            return hour.skyTexAzimuth - 180f - worldAzi;
        }

        /// <summary>
        /// The sky is the ONE set of textures that does not get the PS1
        /// treatment, and that is deliberate.
        ///
        /// Everything under Art/ is point-filtered, unmipped and clamped to
        /// 256 px because that is the PS1's texture page and the reason the
        /// game looks like it does. A skybox on that hardware was never in
        /// that budget — it is a handful of polygons at infinity with no
        /// lighting and no overdraw, so a PS1 or N64 game could hang a much
        /// better picture up there than it could lay on the road, and most of
        /// them did. Bilinear with mips on top of that stops the sun disc
        /// crawling when the car turns, which point sampling at 1024 px
        /// absolutely does.
        ///
        /// 1024 x 512 is not a compromise, it is the right number: the
        /// framebuffer is 240 lines, the visible band above the horizon is
        /// about 100 of them, and 512 px of panorama across 180 degrees puts
        /// roughly one texel on one pixel there. Wrapping repeats round the
        /// horizon and clamps at the poles — repeat in V mirrors the zenith
        /// into the ground.
        /// </summary>
        static void ConfigureSkyImporters()
        {
            if (!Directory.Exists(SkyTexDir)) return;
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SkyTexDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;
                bool dirty = imp.filterMode != FilterMode.Bilinear || !imp.mipmapEnabled ||
                             imp.maxTextureSize != 1024 ||
                             imp.textureCompression != TextureImporterCompression.Compressed ||
                             imp.wrapModeU != TextureWrapMode.Repeat ||
                             imp.wrapModeV != TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Bilinear;
                imp.mipmapEnabled = true;
                imp.maxTextureSize = 1024;
                imp.textureCompression = TextureImporterCompression.Compressed;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.wrapModeU = TextureWrapMode.Repeat;
                imp.wrapModeV = TextureWrapMode.Clamp;
                imp.alphaSource = TextureImporterAlphaSource.None;
                if (dirty) { imp.SaveAndReimport(); n++; }
            }
            Log($"Configured {n} sky importers (bilinear, mipped, 1024, clamped at the poles).");
        }

        static void ConfigureAudioImporters()
        {
            // Two populations with genuinely different budgets, so they get
            // different settings rather than one compromise.
            int core = ConfigureAudioFolder(Root + "/Audio", 1.0f, true);
            int engines = ConfigureAudioFolder(Root + "/Resources/Engines", EngineClipQuality, false);
            // The generated wind beds (tools/gen_wind.mjs): two short mono
            // loops the player's WindAudio loads itself, core settings.
            ConfigureAudioFolder(Root + "/Resources/Sfx", 1.0f, true);
            Log($"Configured {core} core audio importers (Vorbis q1.0, preloaded) and " +
                $"{engines} engine-family clips (Vorbis q{EngineClipQuality:0.00}, load-on-demand).");
        }

        /// <summary>
        /// Vorbis quality for the 560 clips of the 28 recorded engine families.
        ///
        /// Lower than the core set's 1.0 on purpose: those 28 families are the
        /// single biggest thing in the WebGL download, and the difference
        /// between q1.0 (~500 kbps) and this is ~40 MB of data file that every
        /// phone pays for on first load. 0.8 is ~256 kbps, which is what the
        /// source .ogg files in Resources/Engines were encoded at, so this
        /// re-encode is close to a copy rather than a real second generation.
        ///
        /// Do NOT drop this toward 0.65 without listening on the deployed build:
        /// AudioToneChain runs a +7.5 dB low shelf at 110 Hz over the final mix,
        /// which re-amplifies exactly what a low-bitrate Vorbis encoder throws
        /// away down there. That is what "sounds 1980s arcade, no bass" was.
        /// </summary>
        const float EngineClipQuality = 0.8f;

        /// <param name="preload">Whether sample data loads with the scene.
        /// TRUE for the core set (a dozen clips, all of them used every race).
        /// FALSE for the engine families: 560 clips decompressed on boot would
        /// be hundreds of megabytes of PCM in a browser tab, and a race only
        /// ever touches the player's family plus one per opponent. With preload
        /// off, Resources.Load hands back the asset and the sample data arrives
        /// when the family is actually selected.</param>
        static int ConfigureAudioFolder(string folder, float quality, bool preload)
        {
            if (!AssetDatabase.IsValidFolder(folder)) { Log("No " + folder + " folder yet."); return 0; }
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
            int n = 0;
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) continue;

                var s = imp.defaultSampleSettings;
                // ALREADY RIGHT? Then leave it alone.
                //
                // SaveAndReimport on an importer whose settings have not changed
                // still re-encodes the clip, and this loop runs over 560 engine
                // takes on every single scene build. That is minutes of Vorbis
                // per build at best, and at worst it is what was KILLING the
                // editor: batchmode Unity died somewhere inside the FMOD bank
                // build, with no error in the log, on run after run — and every
                // retry started the same 560-clip re-encode from the top.
                //
                // Same guard CarModelBaker.PointFilter has had on the textures
                // all along, for the same reason.
                if (s.loadType == AudioClipLoadType.DecompressOnLoad
                    && s.compressionFormat == AudioCompressionFormat.Vorbis
                    && Mathf.Abs(s.quality - quality) < 0.005f
                    && s.preloadAudioData == preload
                    && !imp.forceToMono && !imp.loadInBackground) continue;

                // WebGL cannot stream audio, and these clips are short loops, so
                // decompress on load rather than decoding continuously.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = quality;
                s.preloadAudioData = preload;
                imp.defaultSampleSettings = s;
                // Keep the source stereo. The takes are recorded stereo, and
                // collapsing them was throwing away the width that makes an
                // engine sound like it occupies space. Unity downmixes
                // automatically for the 3D-positioned opponent cars.
                imp.forceToMono = false;
                imp.loadInBackground = false;
                imp.SaveAndReimport();
                n++;
            }
            return n;
        }

        /// <summary>
        /// The engine voice keeps every band resident so loops never restart out
        /// of phase. Player (18) + three opponents (6 each) needs more than the
        /// default 32 real voices, or Unity virtualizes the quiet ones and the
        /// restart artifact comes back.
        /// </summary>
        const int RoadLayer = 8;
        /// <summary>Walls, buildings and other solid scenery. Kept off the
        /// suspension raycast mask so a wheel can never take spring force from a
        /// barrier face — see CarController.solidLayer.</summary>
        const int SolidLayer = 9;
        /// <summary>The stage's forest chunks. Their own layer purely so
        /// StageCulling can clip them at ~500 m while the terrain runs out to
        /// the stage's full far plane. No colliders ever go on it.</summary>
        const int FoliageLayer = 10;

        static void EnsureRoadLayer()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { Log("WARN: TagManager.asset not found"); return; }
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null || layers.arraySize <= FoliageLayer) return;
            layers.GetArrayElementAtIndex(RoadLayer).stringValue = "Road";
            layers.GetArrayElementAtIndex(SolidLayer).stringValue = "Solid";
            layers.GetArrayElementAtIndex(FoliageLayer).stringValue = "Foliage";
            so.ApplyModifiedPropertiesWithoutUndo();
            Log("Layer " + RoadLayer + " named 'Road', layer " + SolidLayer + " named 'Solid'.");
        }

        static void ConfigureAudioVoiceLimits()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0) { Log("WARN: AudioManager.asset not found"); return; }
            var so = new SerializedObject(assets[0]);
            var real = so.FindProperty("m_RealVoiceCount");
            var virt = so.FindProperty("m_VirtualVoiceCount");
            // Player now runs 27 voices (16 band takes + limiter + 2 intake +
            // skid + 3 turbo + 3 impact + scrape) and each opponent 10, so the
            // default 32 is well short. Under-provisioning here does not drop the
            // newest sound — Unity virtualizes the QUIETEST, which means the
            // always-playing volume-gated loops stop and restart out of phase.
            if (real != null) real.intValue = 72;
            if (virt != null) virt.intValue = 512;
            so.ApplyModifiedPropertiesWithoutUndo();
            Log($"Audio voice limits set to {(real != null ? real.intValue : -1)} real / " +
                $"{(virt != null ? virt.intValue : -1)} virtual.");
        }

        // ------------------------------------------------------------------
        //  Materials
        // ------------------------------------------------------------------
        /// <param name="affine">1 = PS1 affine texture warping, 0 = perspective
        /// correct. DEFAULTS TO 0 everywhere now — warping grows with triangle
        /// size and nothing in this game is made of small enough triangles for
        /// it to read as anything but a bug. Left as a parameter so one mesh
        /// could opt back in; nothing does.</param>
        /// <summary>
        /// Every scene the game ships, in build-index order.
        ///
        /// THE definition — EditorBuildSettings and the WebGL player both come
        /// from here, because they were separately maintained and drifted: the
        /// player shipped without the pizza shop while the editor knew about it,
        /// and the only symptom was GO TO WORK doing nothing.
        ///
        /// Order is a contract: LifeHome is 0 (the boot scene RaceManager
        /// returns to), then one per circuit IN CATALOG ORDER, then the garage,
        /// then the pizzeria. TrackCatalog.SceneIndex / GarageSceneIndex /
        /// PizzeriaSceneIndex are the other half of it, and anything new can
        /// only ever go on the END.
        /// </summary>
        public static string[] SceneOrder()
        {
            var list = new List<string> { LifeHomeSceneBuilder.ScenePath };
            foreach (var t in TrackCatalog.Scened)
                list.Add("Assets/PSXRacing/Scenes/" + t.id + ".unity");
            list.Add(GarageSceneBuilder.ScenePath);
            list.Add(PizzeriaSceneBuilder.ScenePath);
            list.Add(TownScenePath);
            list.Add(SellerLotSceneBuilder.ScenePath);
            list.Add(NeighborhoodScenePath);
            return list.ToArray();
        }

        // ---- wet masks: how much of the weather each surface takes ----
        //
        // The 2026-09-21 night pass (the owner's NFS 2015 reference: "how
        // street lights bathe the road") made roads WET. PSX/Lit multiplies
        // a per-material _Wet by the global _PSXWetness that TimeOfDay writes
        // (WetnessFor: rain soaks everything, fog and snow less, and a CLEAR
        // night is damp — NFS's always-wet night, taken as an art licence)
        // and by how squarely the face looks up, so a deck's soffit, a kerb's
        // riser and the side of a wall stay dry whatever their mask says.
        //
        // The mask is the one place a SURFACE says how it takes water: dense
        // asphalt darkens and mirrors the lamps (1), paint, a kerb and a
        // drive's finished slab shed a little (.8-.9), porous pavement and
        // rough-cast concrete soak it and go dull rather than glossy (.6-.7),
        // a yard of hardcore mostly just darkens (.35). Everything not named
        // here — grass, walls, facades, posts, and every scenery_* material an
        // FBX import writes — is 0, the shader's default, which is also what
        // keeps an INDOOR floor that happens to share a pack texture from
        // growing puddles (the pizzeria, the garage and every house interior
        // are scenery_*).
        //
        // AN INDOOR SURFACE THIS BUILDER AUTHORS GETS AN ASSET OF ITS OWN, even
        // where it wears the same photograph and tint as the one outside the
        // door (WetUnderRoof). The wetness is one global over the whole scene
        // and the shader's only other gate is which way a face looks; nothing
        // in it can see a roof. So one material cannot be both the apron in
        // the rain and the floor behind the shutters — the first cut tried,
        // and had to choose between a dry apron under the lamps and puddles
        // on a workshop floor (R14 chose the dry apron, and the dealer's bay
        // paint then shone on matte concrete; the workshop's bench, built
        // from the street kerb, grew puddles under the roof either way).
        //
        // Per material rather than per scene because the assets are SHARED:
        // CityRoad_* serve four city scenes, Town* the town AND the
        // neighbourhood, StartLine every venue. How wet it is tonight is the
        // scene's (a global); what the thing is made of is the material's.
        //
        // Grip does not read any of this: dampness is a look. Grip stays
        // weather-only, exactly as it was.
        /// <summary>Tarmac, and everything lying ON the tarmac the car drives
        /// over: <c>&lt;id&gt;_Road</c>, <c>_RoadDeck</c>, <c>_Deck</c> (the
        /// shader keeps its soffit and fascias dry), <c>_Joint</c>,
        /// <c>_Forecourt</c>, <c>StartLine</c>, every <c>CityRoad_*</c> slot,
        /// <c>TownRoad</c>.</summary>
        const float WetAsphalt = 1f;
        /// <summary><c>&lt;id&gt;_Kerb</c> in all three styles (painted racing
        /// kerb, street kerb, a stage's gravel/shell/slab verge).</summary>
        const float WetKerb = 0.8f;
        /// <summary><c>CityPavement</c>: the paved ground cells uptown and
        /// around every non-house footprint.</summary>
        const float WetCityPavement = 0.7f;
        /// <summary><c>CityConcrete</c>: kerbs, Jersey barriers, retaining
        /// walls, deck boxes and piers share it — only their TOPS take the
        /// mask, the up-facing gate in the shader sees to that.</summary>
        const float WetCityConcrete = 0.6f;
        /// <summary><c>TownLine</c>: the centre line and the meet lot's stall
        /// paint (both on <c>TownRoad</c>, 1) and the dealer's bay lines (on
        /// <c>TownDrive</c>, .8). It sits BETWEEN its two grounds on purpose:
        /// the puddles are world-XZ noise, so a puddle runs straight across a
        /// stripe on either, and the paint is never more than a tenth wetter
        /// or drier than what it is painted on. (While the apron was dry the
        /// bay lines were the only thing on the dealer's lot that shone.)</summary>
        const float WetTownLine = 0.9f;
        /// <summary><c>TownKerb</c>: the street's kerbs (town and
        /// neighbourhood), and the low walls round the dealer's and the meet
        /// lots (tops only). OUTDOORS ONLY — the workshop's bench and shutter
        /// boxes wore it once and are <c>TownShopFit</c> now
        /// (<see cref="WetUnderRoof"/>); keep it that way, or tuning this
        /// puts puddles under a roof.</summary>
        const float WetTownKerb = 0.7f;
        /// <summary><c>TownDirt</c>: the wreck yard's oil and hardcore.</summary>
        const float WetTownDirt = 0.35f;
        /// <summary><c>TownDrive</c>: every drive and apron OUTDOORS — your
        /// own drive and the neighbours', the pizzeria's frontage, the two
        /// units' aprons, the dealer's lot and its way in — plus
        /// <c>TownForecourt</c>, the same ConcreteBare photograph under the
        /// pumps, tied to this constant so the two concretes in town can never
        /// drift apart. A step under the tarmac, and wetter than the city's
        /// rough-cast kerb concrete (.6): a drive is a finished slab the car
        /// is ON, the headlights sweep across it straight off the street, and
        /// the owner's "street lights bathe the road" should not stop dead at
        /// the dropped kerb. It was 0 for a while (R14) because the workshop's
        /// floor under its roof was this same asset; that floor is
        /// <c>TownShopFloor</c> now, so the slabs outside take the night like
        /// the street they open onto. Neighbourhood footings are drawn in it
        /// too, but their tops are buried under the lawn and their faces are
        /// vertical, which the shader keeps dry. Two slabs DO run in under a
        /// building — the pizzeria's apron a couple of metres under its
        /// shopfront, the forecourt under the station shop — and neither shows
        /// indoors: each pack's own floor stands over it (pizzeria.fbx's at
        /// +3.9 cm over the 1.5 cm apron, the shop's Tiles at +3.0 cm over the
        /// 1.8 cm forecourt, both measured off the FBX in headless Blender).
        /// The pump canopy is an open-sided roof and its forecourt takes the
        /// damp, as every circuit forecourt (1) and the stage tunnels
        /// (R14) already do.</summary>
        const float WetTownDrive = 0.8f;
        /// <summary>Under a roof: <c>TownShopFloor</c> (the workshop's floor,
        /// the drive's photograph) and <c>TownShopFit</c> (its bench and the
        /// rolled shutter boxes under the lintel, the kerb's grey), in both
        /// workshops BuildTownUnit makes (Delmar Auto, Colourworks). Written
        /// as an explicit 0, not left to MakeMat's default, so the next
        /// person to wet the town reads WHY these two stay dry. Grip is
        /// untouched here as everywhere: the floor is still RoadLayer.</summary>
        const float WetUnderRoof = 0f;
        /// <summary>Warned once per editor session, not once per material:
        /// a PSX/Lit without <c>_Wet</c> is one fact, not three hundred.</summary>
        static bool warnedNoWet;

        /// <param name="twoSided">Draw both faces (PSX/Lit's <c>_Cull</c> Off)
        /// and light each from the side the camera is on. For TREES: a
        /// crossed pair of one-sided quads is a flat card from half the
        /// compass and invisible from a quarter of it.</param>
        /// <param name="wet">The surface's wet mask (PSX/Lit <c>_Wet</c>,
        /// 0..1) — see <see cref="WetAsphalt"/> and its neighbours for what
        /// takes how much. Zero, the default, is dry whatever the weather.</param>
        static Material MakeMat(string name, string texPath, float cutoff = 0f,
                                Color? tint = null, float affine = 0f, bool twoSided = false,
                                float wet = 0f)
        {
            // Resolve the shader HERE rather than trusting Build() to have run.
            // psxLit is only assigned inside Build, and every other entry point
            // that makes materials already carries this guard (PSXMaterialFor,
            // ConvertToPSXMaterials) — MakeMat did not, so the moment CityPreview
            // was pointed at the real material table it wrote a NULL shader onto
            // all thirty-five city materials and SAVED them. Magenta roads,
            // magenta ground, and a corrupted asset each time the preview ran.
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            string assetPath = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                mat = new Material(psxLit);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            mat.shader = psxLit;
            if (!string.IsNullOrEmpty(texPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) Log("WARN: texture missing " + texPath);
                mat.mainTexture = tex;
            }
            mat.color = tint ?? Color.white;
            mat.SetFloat("_Cutoff", cutoff);
            mat.SetFloat("_Affine", affine);
            // Written only when it is not the shader's own default, so the
            // hundreds of one-sided materials this factory rewrites every build
            // do not all grow a line they never needed.
            if (twoSided || (mat.HasProperty("_Cull") && mat.GetFloat("_Cull") != 2f))
                mat.SetFloat("_Cull", twoSided ? 0f : 2f);
            // The wet mask is written EVERY time, zero included — NOT the
            // _Cull economy above. The asset is loaded, not recreated, so a
            // value this factory once wrote and later stopped writing would
            // stay on disk for good: a surface taken out of the wet set would
            // go on shining in the rain until someone deleted its .mat.
            // Guarded on HasProperty so an older PSX/Lit (or a build run
            // before the shader change landed) still builds, dry.
            if (mat.HasProperty("_Wet")) mat.SetFloat("_Wet", Mathf.Clamp01(wet));
            else if (wet > 0f && !warnedNoWet)
            {
                warnedNoWet = true;
                Log("WARN: PSX/Lit has no _Wet property — wet masks not written (first: " + name + ").");
            }
            if (cutoff > 0f) mat.renderQueue = 2450;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Dictionary<string, Material> matByKey = new Dictionary<string, Material>();

        /// <summary>A float as a short, stable, filename-safe token. Rounded to
        /// a thousandth: UV numbers that differ below that are the same window,
        /// and a raw ToString() would put a minus sign and a dot in a path.</summary>
        static string Sig(float v) =>
            Mathf.RoundToInt(v * 1000f).ToString().Replace("-", "n");

        /// <summary>
        /// Is this the pack's window glass?
        ///
        /// The material NAME is the whole signal, and it is enough. Every pack
        /// in this project that has glass at all calls it one thing: the gas
        /// station, the pizzeria block, the burger drive-thru, the hero house
        /// and the standalone pizzeria each carry exactly ONE material called
        /// "Glass"; house_simple.fbx alone says "Windows". Nothing else in the
        /// ~250 material names across those packs contains either token, and
        /// no TEXTURE in any of their Textures folders does — which is why the
        /// question cannot be put to the importer the way the cutout one is.
        ///
        /// Whole-token, not Contains(): "Window_frame" is a real mesh sitting
        /// right beside the four Glass_00N panes in Gas_station.fbx, and it is
        /// aluminium.
        /// </summary>
        static bool IsGlassName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            // "Glass", "Glass_001", "Windows", "Glass.001" — a dot or an
            // underscore and digits is the only suffix these packs use.
            return System.Text.RegularExpressions.Regex.IsMatch(
                n, @"^(glass|windows?)([._]\d+)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        static Shader psxGlass;

        /// <param name="glass">Draw this one blended instead of opaque. See
        /// <see cref="ConvertToPSXMaterials"/> for why it is opt-in.</param>
        static Material PSXMaterialFor(Texture tex, string fallbackName, Vector2 scale,
                                       Vector2 offset, bool glass = false)
        {
            // Reachable from OTHER builders (the home scene, the prop baker)
            // outside a full Build() — without this, a standalone run created
            // every scenery material with a NULL shader and saved the magenta
            // to disk.
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            if (glass && psxGlass == null) psxGlass = Shader.Find("PSX/LitTransparent");
            if (glass && psxGlass == null)
            {
                // Loudly, never silently: writing a null shader onto a SAVED
                // asset is the magenta-and-corrupted-.mat failure MakeMat's
                // header records, and it survives the run that caused it.
                Log("WARN: PSX/LitTransparent missing — glass stays opaque");
                glass = false;
            }
            if (tex == null) tex = Texture2D.whiteTexture;
            string texKey = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(texKey)) texKey = tex.name;
            // GLASS IS PART OF THE KEY, and this is the load-bearing half of
            // the change. The pack's Glass material carries NO texture, so it
            // resolves to Texture2D.whiteTexture and keys as
            // "UnityWhite|(1,1)|(0,0)" — the same slot every other untextured
            // material in every pack gets. The house's White, Blu and
            // Fabric_15 are all wearing that one asset today. Mutating it into
            // glass would turn a chunk of the house transparent.
            string key = texKey + "|" + scale + "|" + offset + (glass ? "|glass" : "");
            if (matByKey.TryGetValue(key, out var cached)) return cached;
            string safe = string.Join("_", (tex.name + "_" + fallbackName).Split(Path.GetInvalidFileNameChars()));
            // The FILE has to be keyed by everything the cache is keyed by.
            //
            // It used to add a suffix only when the SCALE was non-default, so
            // two materials sharing a texture and a name and differing only in
            // OFFSET took different cache slots and the same .mat file — the
            // second call loaded the first's asset and overwrote its offset, so
            // both ended up drawing whichever UV window was written last. On an
            // atlased prop that is the fridge showing its own shelves on the
            // outside of the door, and the bin showing the bag. Reported
            // exactly that way.
            //
            // And the old suffix was matByKey.Count, which depends on the
            // order materials happen to be met in — so the same material could
            // land on a different file between builds. Derived from the numbers
            // themselves now: same inputs, same path, every time.
            if (scale != Vector2.one || offset != Vector2.zero)
                safe += "_uv" + Sig(scale.x) + "x" + Sig(scale.y) +
                        "o" + Sig(offset.x) + "x" + Sig(offset.y);
            // Same reason the key carries it: a new FILE, so the opaque asset
            // the house is already wearing is never opened, let alone rewritten.
            if (glass) safe += "_glass";
            string assetPath = MatDir + "/scenery_" + safe + ".mat";
            var shader = glass ? psxGlass : psxLit;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            mat.shader = shader;
            mat.mainTexture = tex;
            mat.mainTextureScale = scale;     // keep the source material's tiling
            mat.mainTextureOffset = offset;
            mat.color = Color.white;
            // Perspective-correct. This factory dresses every IMPORTED model in
            // the game — the house and its doors, the trees, the gas station,
            // the restaurants — and it never set _Affine at all, so all of them
            // silently inherited the shader's old default of 1. A door leaf is
            // two triangles: at that size the warp bends the panel lines as you
            // walk past, which is exactly how this was reported.
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);

            // Cut out where the source has an alpha channel.
            //
            // Without this every alpha-masked billboard in an imported model
            // renders as an opaque BLACK quad — which is what the gas station's
            // bushes and trees have always been, invisible only because the
            // station used to be built at a fifth of its size and parked behind
            // a barrier nobody could cross. Asked of the importer rather than
            // guessed from the file extension: what matters is whether the
            // source carries alpha, and a PNG without any is common.
            if (glass)
            {
                // A pane is a TINT, not a texture: the pack's glass material
                // has no map at all. _Color.a is the opacity, and PSX/Lit
                // already multiplied _Color in and returned its alpha, so the
                // blended sibling needed no property PSX/Lit lacks.
                mat.color = GlassTint;
                mat.SetFloat("_Cutoff", 0f);
                mat.renderQueue = -1;   // the shader's own Transparent queue
            }
            else
            {
                bool cutout = false;
                if (AssetImporter.GetAtPath(texKey) is TextureImporter imp)
                    cutout = imp.DoesSourceTextureHaveAlpha();
                mat.SetFloat("_Cutoff", cutout ? 0.5f : 0f);
                mat.renderQueue = cutout ? 2450 : -1;
            }

            EditorUtility.SetDirty(mat);
            matByKey[key] = mat;
            return mat;
        }

        /// <summary>Shop glass: a cold pale tint at a third opacity. Dark
        /// enough to read as glazing from outside on a sunlit street, open
        /// enough that the counter and the booths behind it are legible —
        /// which is the whole point of asking for it.</summary>
        static readonly Color GlassTint = new Color(0.80f, 0.87f, 0.90f, 0.34f);

        /// <param name="glass">Let this model's WINDOW panes come through
        /// blended. OFF by default, which is byte-for-byte the behaviour every
        /// existing caller had: the city props, the track-side buildings, the
        /// house and the pizza cargo keep getting one opaque PSX/Lit material
        /// per texture and no scene they bake changes. Only a builder that
        /// asks gets glass, and only on materials <see cref="IsGlassName"/>
        /// recognises — the shared cache and the shared .mat file made this
        /// the one change here that could not safely be made globally.</param>
        internal static void ConvertToPSXMaterials(GameObject go, bool glass = false)
        {
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    Vector2 scale = Vector2.one, offset = Vector2.zero;
                    if (src != null && src.HasProperty("_BaseMap"))
                    { scale = src.GetTextureScale("_BaseMap"); offset = src.GetTextureOffset("_BaseMap"); }
                    else if (src != null && src.HasProperty("_MainTex"))
                    { scale = src.mainTextureScale; offset = src.mainTextureOffset; }
                    mats[i] = PSXMaterialFor(src != null ? src.mainTexture : null,
                                             src != null ? src.name : "none", scale, offset,
                                             glass && IsGlassName(src != null ? src.name : null));
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------
        //  Track geometry
        // ------------------------------------------------------------------
        static List<Vector3> BuildWaypoints(out float[] curvatures)
        {
            // The spline sampler lives in TrackCatalog rather than here: the
            // LifeSim quotes a circuit's length before the race, and a second
            // implementation of "how long is this track" is a second answer.
            var pts = TrackCatalog.Sample(track, Spacing);

            int n = pts.Count;
            // Clamped rather than wrapped on a strip: joining the last waypoint
            // to the first there is a 700 m jump, which reads as an infinitely
            // tight corner and would have the AI crawling over the traps.
            int Idx(int i) => Loop ? (i % n + n) % n : Mathf.Clamp(i, 0, n - 1);

            curvatures = new float[n];
            for (int i = 0; i < n; i++)
            {
                // FLATTENED. Curvature is what the AI lifts for, and with the
                // circuits now climbing, a 10% crest measured in 3D reads as a
                // 6-degree bend — so the field would brake for the brow of a
                // hill on a straight. A hill is not a corner.
                Vector3 a = pts[Idx(i - 1)]; a.y = 0f;
                Vector3 b = pts[i]; b.y = 0f;
                Vector3 c = pts[Idx(i + 1)]; c.y = 0f;
                float angle = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
                curvatures[i] = angle / Spacing;
            }
            // Light smoothing so AI target speeds don't jitter
            var smoothed = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int o = -2; o <= 2; o++) sum += curvatures[Idx(i + o)];
                smoothed[i] = sum / 5f;
            }
            curvatures = smoothed;
            return pts;
        }

        static Vector3 RightAt(List<Vector3> pts, int i)
        {
            int n = pts.Count;
            int a = Loop ? (i - 1 + n) % n : Mathf.Max(0, i - 1);
            int b = Loop ? (i + 1) % n : Mathf.Min(n - 1, i + 1);
            Vector3 fwd = pts[b] - pts[a];
            return Vector3.Cross(Vector3.up, fwd.normalized).normalized;
        }

        /// <summary>Meshes the car never touches with a wheel, which may be
        /// quantised in the build. Matched on the prefixed asset name.
        ///
        /// What Medium actually does, read back from the saved asset
        /// (2026-09-11): positions to 16 bits of the mesh's own range (1.5 cm
        /// on a 240 m ground chunk; the terrain audit's tightest clearance
        /// moved from 0.245 to 0.233 m and nothing else), UVs to as many bits
        /// as their range needs (18 on a tiled ground chunk, 10 on a
        /// billboard: a pixel of the atlas), normals to 8. Roads, kerbs,
        /// decks and aprons stay exact because a wheel reads them.</summary>
        static bool CompressibleMesh(string name)
        {
            foreach (var key in new[] { "StageGround", "StageForest", "StageSea", "GroundMesh", "TreeMesh",
                                        "Posts", "PostMesh", "StageWall", "StageBank", "WallMesh", "Tunnel" })
                if (name.Contains(key)) return true;
            return false;
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            // Prefixed with the circuit id: four scenes each want their own
            // RoadMesh, and an unprefixed asset would leave the first three
            // pointing at the fourth circuit's geometry.
            name = MeshPrefix + name;
            m.name = name;
            // A mesh that arrives with its own normals keeps them: the stage's
            // terrain chunks compute theirs from the height FIELD so adjacent
            // chunks agree along their shared border, which per-chunk
            // recalculation cannot do.
            var existingNormals = m.normals;
            if (existingNormals == null || existingNormals.Length != m.vertexCount)
                m.RecalculateNormals();

            // SIZE. Two levers, both invisible from the driving seat:
            //
            //  * 16-bit indices wherever the mesh has under 65k vertices.
            //    Every generated mesh here declared UInt32, and a 12 m ground
            //    chunk of 441 vertices carries 2,400 indices — at four bytes
            //    each that is 40% of the chunk. The build stores the index
            //    buffer at its declared width.
            //  * Vertex compression (positions, normals, uvs quantised at
            //    build time) on everything nobody DRIVES on: ground, forest,
            //    sea, walls, banks, posts, tubes. Medium keeps 16-bit uvs; a
            //    240 m chunk's positions land on ~4 mm. The road, the kerbs,
            //    the decks and the aprons stay exact — a 7 km ribbon at 16
            //    bits is a 10 cm staircase.
            //
            // The shipped data file was 83 MB against GitHub's 100 MB wall
            // with 54 MB of it in these meshes; two more mountain loops did
            // not fit without this.
            if (m.vertexCount <= 65000 && m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32)
            {
                var subs = new int[m.subMeshCount][];
                for (int sIdx = 0; sIdx < m.subMeshCount; sIdx++) subs[sIdx] = m.GetTriangles(sIdx);
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
                m.subMeshCount = subs.Length;
                for (int sIdx = 0; sIdx < subs.Length; sIdx++) m.SetTriangles(subs[sIdx], sIdx);
            }
            if (CompressibleMesh(name))
                MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);
            // Guard: this exact failure shipped once already. Double-sided
            // triangles cancel in RecalculateNormals and the surface goes unlit.
            //
            // Counted over the vertices triangles actually USE. A vertex no
            // triangle references also comes back with a zero normal, and there
            // are legitimately some now: the barrier keeps its vertex run
            // through the forecourt opening and drops only the faces, so the UV
            // distance either side of the gap still lines up. Those orphans are
            // not the bug this guard is for, and counting them turned a real
            // alarm into four lines of noise per build.
            int bad = 0;
            var nrm = m.normals;
            var used = new bool[nrm.Length];
            var idx = m.triangles;
            for (int k = 0; k < idx.Length; k++) used[idx[k]] = true;
            for (int k = 0; k < nrm.Length; k++)
                if (used[k] && nrm[k].sqrMagnitude < 1e-8f) bad++;
            if (bad > 0)
                Log($"WARN: {name} has {bad}/{m.vertexCount} zero-length normals — " +
                    "opposite-winding duplicate triangles cancel in RecalculateNormals.");
            m.RecalculateBounds();
            string p = GenDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (existing != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        static void BuildRoad(List<Vector3> pts, Transform parent)
        {
            int n = pts.Count;
            // A loop needs one extra ring to close back onto waypoint 0; a strip
            // stops at its last waypoint. Everything else is identical.
            int last = Loop ? n : n - 1;
            var verts = new Vector3[(last + 1) * 2];
            var uvs = new Vector2[(last + 1) * 2];
            // Two triangle lists, one per surface. A bridge deck is poured
            // concrete and the ribbon over it was blacktop, so a viaduct read
            // as a road that happened to have a parapet - the deck, the piers
            // and the fascia were all concrete underneath and the one surface
            // you actually look at was not.
            var tris = new List<int>();      // submesh 0: tarmac
            var deckTris = new List<int>();  // submesh 1: the spans
            float dist = 0f;

            // Same BridgeBlend the deck builder reads, so the concrete on the
            // driving surface starts and stops exactly where the structure
            // under it does. Two thresholds would drift.
            var onDeck = new bool[n];
            if (track != null && !track.drag && track.bridges != null && track.bridges.Length > 0)
            {
                float lap = Mathf.Max(track.LengthM, 1f);
                for (int i = 0; i < n; i++)
                    onDeck[i] = TrackCatalog.BridgeBlend(track, Mathf.Repeat(i * Spacing, lap)) > DeckBlendMin;
            }

            for (int i = 0; i <= last; i++)
            {
                int idx = Loop ? i % n : i;
                Vector3 right = RightAt(pts, idx);
                // 12 cm above the ground plane: enough depth separation that the
                // road doesn't z-fight ("flash orange") against it at distance
                Vector3 center = pts[idx] + Vector3.up * RoadLift;
                verts[i * 2] = center - right * (RoadWidth * 0.5f);
                verts[i * 2 + 1] = center + right * (RoadWidth * 0.5f);
                // U ACROSS the carriageway, V along it — Charlotte's mapping,
                // and the reason its markings are the right size. The old
                // mapping ran U along the road and squeezed the whole texture
                // across the width, so a photographed road surface was
                // stretched over 12 m and its one painted line was the only
                // marking a circuit had.
                uvs[i * 2] = new Vector2(0f, dist / TrackRoadVTile);
                uvs[i * 2 + 1] = new Vector2(1f, dist / TrackRoadVTile);
                dist += Spacing;
                if (i < last)
                {
                    int a = i * 2;
                    // A quad counts as deck if EITHER end stands on one, so the
                    // concrete reaches the abutment rather than stopping a
                    // waypoint short of it with a stripe of tarmac in mid-air.
                    // That is exactly the length BuildBridges lays the deck
                    // to — one station past each end of the span
                    // (DeckCoversStation) — so the concrete ribbon and the
                    // structure under it still start and stop together.
                    int nxt = Loop ? (i + 1) % n : i + 1;
                    var into = (onDeck[idx] || onDeck[nxt]) ? deckTris : tris;
                    into.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }

            bool anyDeck = deckTris.Count > 0;
            var mesh = new Mesh { vertices = verts, uv = uvs };
            mesh.subMeshCount = anyDeck ? 2 : 1;
            mesh.SetTriangles(tris, 0, false);
            if (anyDeck) mesh.SetTriangles(deckTris, 1, false);
            SaveMesh(mesh, "RoadMesh");

            var go = new GameObject("Road");
            go.transform.SetParent(parent, false);
            go.layer = RoadLayer;   // wheels detect tarmac by layer, not by name
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // Drawn to THIS track's width, so the lane ladder lands where the
            // tarmac actually ends. The per-track name matters: two circuits of
            // different widths sharing one material would each get whichever
            // one built last.
            // How long since this venue was resurfaced: the city's own hash on
            // the first waypoint, so a circuit and the streets outside it are
            // aged by the same rule and the answer is the same on every build.
            bool fresh = pts.Count > 0 && CityMeshes.IsFresh(new Vector2(pts[0].x, pts[0].z));
            var tarmacSurf = fresh ? CityMeshes.Surface.AsphaltNew : CityMeshes.Surface.AsphaltOld;
            var deckSurf = fresh ? CityMeshes.Surface.ConcreteNew : CityMeshes.Surface.ConcreteOld;

            bool oneWay = track != null && (track.drag || track.oneWay);
            var mat = MakeMat(MeshPrefix + "Road",
                              EnsureTrackRoadTex(RoadWidth, oneWay, tarmacSurf),
                              affine: 0f, wet: WetAsphalt);
            var mr = go.AddComponent<MeshRenderer>();
            if (anyDeck)
            {
                var deckRoadMat = MakeMat(MeshPrefix + "RoadDeck",
                                          EnsureTrackRoadTex(RoadWidth, oneWay, deckSurf),
                                          affine: 0f, wet: WetAsphalt);
                mr.sharedMaterials = new[] { mat, deckRoadMat };
            }
            else mr.sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;

            // The stage's section is its own (PSXRacingBuilder.Stage.cs,
            // planned by PlanStageRoadside); a circuit's is a graded run-off
            // to its perimeter wall.
            BuildShoulders(pts, parent, track != null && track.stage
                ? (EdgeProfileFn)StageEdgeProfile
                : CircuitEdgeProfile);
        }

        // ------------------------------------------------------------------
        //  The shoulder: how the road meets the ground
        // ------------------------------------------------------------------
        //
        //  This was BuildRoadEdge: a batter from the back of the kerb strip
        //  down to the bottom of the roadbed slab, 0.33 m under the waypoint
        //  plane, textured as dark cut earth and given a collider. On a
        //  circuit it came out a 22-31% slope from a berm (0.46-0.59 m of climb
        //  to the street curb's pavement), and on a stage, where the guard
        //  wall line left it 0.25 m of run, it was a 0.46 m face at 61 degrees
        //  on every unwalled station — a wall to the car's body box, which is
        //  what "difficult to drive back onto tracks" and "thick roads
        //  sticking out of the ground" both were.
        //
        //  The owner's rule (RoadsideRules): the road meets the ground by DOT
        //  practice. So the slab stays UNDER the paved width, where it keeps
        //  the coarse ground lattice out of the tarmac, and beside the tarmac
        //  there is now an exact SHOULDER surface: a per-station cross-section
        //  each venue family describes (EdgeProfileFn), laid on the same 4 m
        //  stations and the same RightAt vectors as the road so it shares the
        //  kerb strip's outer edge in plan, carrying its own collider. The
        //  lattice is held under it (HideMarginM) and its outer toe tucks
        //  under the lattice (ToeTuckM), so the two surfaces CROSS rather than
        //  abut, and a car rides the higher of two continuous surfaces.

        /// <summary>
        /// A roadside cross-section at one station and side — the contract the
        /// circuit (<see cref="CircuitEdgeProfile"/>) and the stage
        /// (StageEdgeProfile) both build to.
        ///
        /// Fills <paramref name="profile"/> with (e, dy) points ordered
        /// OUTWARD: e is metres outward from the TARMAC edge (RoadWidth / 2),
        /// dy is metres relative to the tarmac surface (pts[idx].y +
        /// <see cref="RoadLift"/>), negative below. The first point is at
        /// e = <see cref="KerbWidth"/> — the outer edge of the strip
        /// BuildKerbs draws — at that strip's top. An EMPTY profile means no
        /// shoulder at this station and side (a deck, where the deck is the
        /// surface). The emitter adds the toe tuck, the skirt and the
        /// collider; a profile describes only the surface a car drives on.
        /// </summary>
        internal delegate void EdgeProfileFn(List<Vector3> pts, int idx, float side, List<Vector2> profile);

        /// <summary>Height of the kerb strip's top over the tarmac, in every
        /// style (a street curb's lift is added to it). Was +0.01, "so it
        /// reads as a raised kerb and cannot z-fight" — but the strip starts
        /// where the road mesh stops, so the two never overlapped in plan and
        /// there was nothing to fight; what the centimetre did buy was a 1 cm
        /// step up at every tarmac edge. Flush, the drop from the strip to the
        /// shoulder is the whole of RoadsideRules.EdgeDropM.</summary>
        internal const float KerbStripLift = 0f;

        /// <summary>Horizontal run of the bevel that takes the shoulder down
        /// its <see cref="RoadsideRules.EdgeDropM"/> from the strip: a 30
        /// degree face, the FHWA "Safety Edge" angle, so even the inch is a
        /// slope a tyre climbs (0.025 / tan 30 = 0.043 m) rather than a
        /// square lip.</summary>
        const float SafetyEdgeRunM = RoadsideRules.EdgeDropM * 1.7320508f;

        /// <summary>Cross-fall of a circuit's run-off ribbon from the bevel
        /// to the wall: 2%. Half <see cref="RoadsideRules.ShoulderCrossFall"/>
        /// on purpose — that 4% is a gravel road shoulder draining a crowned
        /// lane, and a circuit's run-off is a graded apron to a perimeter
        /// wall three to four metres out, where 4% would put the wall foot
        /// 13 cm under the tarmac for no reason a driver can see.</summary>
        const float RunoffCrossFall = 0.02f;

        /// <summary>How far short of the wall face a circuit's run-off ribbon
        /// stops. Its toe tuck then passes through the wall plane below the
        /// ribbon, inside the wall's collider box, so the end of the surface
        /// is never a thing a car can reach.</summary>
        const float RunoffWallGapM = 0.05f;

        /// <summary>
        /// Past the tuck, a near-vertical SKIRT down to below the lattice.
        ///
        /// RoadsideRules.ToeTuckM (0.10 over 0.40 m, 1V:4H) is what a car
        /// meets where the lattice is within a tuck of the surface's end (the
        /// section's last point, or where its slope was carried on to —
        /// <see cref="ShoulderSlopedEnd"/>) — the crossing lands inside the
        /// tuck and the face is a recoverable slope. Where the lattice is
        /// lower than that (a flat end against a barrier), a tuck
        /// alone would leave its outer edge floating over the ground with
        /// daylight under it: a lip, and a slot a wheel ray can drop through.
        /// The skirt closes it. Its run (0.1 m) keeps every face's normal
        /// pointing up and out, which is what AuditFacing asserts of RoadEdge.
        ///
        /// ON A FLAT END ONLY THE SKIRT GOES DOWN. The tuck used to follow the
        /// lattice too, as deep as <see cref="ShoulderSkirtMaxM"/>, and on a
        /// stage's walled fill — where the 12 m lattice runs a metre under
        /// the shoulder — that was a metre of fall over 0.4 m starting at the
        /// shoulder's last point. The guard wall's collider does not stand on
        /// that point: on the outside of a bend each chord box is moved out
        /// by its sag (BuildOneStageWall), so at a station the face stood 5-30
        /// cm past the shoulder, and the slot between was an EDGE DROP of 0.09-
        /// 0.25 m "at 1.10 m past the tarmac edge, onto RoadEdge" in eight
        /// runs on four mountains (a wheel ray drops into it; the body box
        /// stops on the wall). The walled section now runs on to the collider
        /// itself (StageEdgeProfile, StageWallContactE); this is the other
        /// half, for wherever a box still stands off the shoulder's end. A
        /// plain tuck is 1V:4H for its whole 0.4 m, so whatever part of it lies
        /// in front of a face is a slope, and the dive is the skirt's, 0.4 m
        /// further out, inside the stone.
        /// </summary>
        const float ShoulderSkirtRunM = 0.1f;
        /// <summary>Extra depth under the lattice the skirt reaches. Both
        /// lattices are read exactly — the circuit's solved by
        /// PrepareCircuitLattice and the stage's by PrepareStageLattice before
        /// any toe is placed — so this is slack for the ground mesh's own
        /// quantisation and for a toe whose lattice triangle differs across
        /// its skirt's 0.1 m.</summary>
        const float ShoulderSkirtSlackM = 0.25f;
        /// <summary>Deepest a tuck or skirt goes below the surface's end.
        /// RoadsideRules.OpenDropM (1.0 m): ground further down than
        /// that past a shoulder is not a toe to tuck under, it is a drop, and
        /// closing a drop is the barrier warrant's job. It is also what keeps
        /// a toe over a gorge inside a 1.3 m deck box instead of hanging out
        /// of its soffit.</summary>
        const float ShoulderSkirtMaxM = RoadsideRules.OpenDropM;
        /// <summary>
        /// Deepest a sloped end is carried on down, below the section's own
        /// last point, to find the lattice: RoadsideRules.CriticalFallM.
        ///
        /// It was <see cref="ShoulderSkirtMaxM"/>, and a slope that had not
        /// met the lattice a metre down stopped where it was and dived the
        /// rest as a tuck — a face at the catch. Blowing Rock 1168-1170 R
        /// (an open section beside the flared end of a critical-fill wall)
        /// measured off the built meshes: the 1V:6H foreslope caught the DEM
        /// at 2.71 m, the 12 m lattice under it was 0.88 m lower and falling
        /// at 1V:9.5H, so the 1V:4H carry needed 6.1 m and 1.5 m of depth;
        /// capped at 1.0 it stopped, and the obstacle audit found a 0.33 m
        /// face "climbing back in" at 2.90 m. A 1V:4H slope is recoverable
        /// at any height (RoadsideRules.IsCriticalFall asks for steeper than
        /// 1V:3H), so the carry may go as deep as the warrant's own fall
        /// before the ground under it is called a drop; and where even that
        /// does not reach the lattice, it is carried to its full run anyway,
        /// so what is left of the dive is the part the land really falls
        /// away by, as far from the road as the recoverable slope reaches.
        /// </summary>
        const float ShoulderCarryMaxM = RoadsideRules.CriticalFallM;
        /// <summary>
        /// A section that ENDS ON A SLOPE — a foreslope falling to its catch,
        /// a backslope climbing to a rock toe — was graded to meet the land
        /// there, and where the coarse lattice is lower than that land the
        /// slope carries on down until it crosses the lattice, and only then
        /// tucks. Steeper than this either way (as rise over run) is a slope;
        /// flatter is a shoulder or run-off at its cross-fall
        /// (RoadsideRules.ShoulderCrossFall 4%, the circuit run-off's 2%),
        /// which ends against something that stops a car — a wall's face, a
        /// tube's wall, a circuit's perimeter wall — and is left to it.
        ///
        /// Why a tuck alone was not enough: the stage lattice is 12 m cells,
        /// and a chord from a vertex under the roadbed dig to one down the
        /// fill runs BELOW the land the section was graded to, right where
        /// the section ends. Tucking straight down to that chord from the
        /// section's last point is a face. Python replica of the stage plan
        /// and lattice (lane C's), every station of all eight stages: a face
        /// rising more than RoadsideRules.FaceRiseFailM within FaceRunM at
        /// 28-45% of open half-sections (p90 0.07-0.15 m, max 0.33 m); with
        /// the slope carried on, 0-2.6% (what is left is lattice sagging a
        /// metre and more under a catch, which no toe can meet).
        /// </summary>
        const float ShoulderSlopedEnd = 0.1f;
        /// <summary>Pitch at which a carried-on slope looks for the lattice.</summary>
        const float ShoulderCatchStepM = 0.1f;
        /// <summary>Road metres per shoulder mesh. The stage ground's own
        /// chunk (NearChunk): short enough that a chunk is culled with the
        /// hillside it sits on. Kept EXACT (not in CompressibleMesh) — a
        /// wheel reads it, and a quantised first vertex would open a crack
        /// against the exact kerb strip beside it.</summary>
        const float ShoulderChunkM = 240f;
        /// <summary>On the inside of a bend, how far out a profile may reach
        /// as a fraction of the local turning radius. Adjacent stations'
        /// cross-section lines meet at the centre of the turn, and a shoulder
        /// that ran past it would fold its triangles over each other. At 0.8
        /// the last points of neighbouring stations are still 0.2 x Spacing
        /// apart.</summary>
        const float ShoulderInsideBendReach = 0.8f;
        /// <summary>
        /// How far a CARRIED slope may run on the inside of a tight bend, as a
        /// fraction of where the neighbouring cross-section lines actually
        /// cross — the section itself stops at <see cref="ShoulderInsideBendReach"/>
        /// of it, and the toe (ToeTuckRunM + ShoulderSkirtRunM) still has to
        /// fit inside this.
        ///
        /// A carry that stopped at the section's own limit dived to the
        /// lattice from there, and on the inside of a hairpin — where the land
        /// in the bend falls away under a fill — that dive was a face 6-7.5 m
        /// out: 0.09 m at Blowing Rock wp 1024-1027 L (bend reach 6.15 m), 0.15 m
        /// at Little Switzerland 1239-1243 L (6.49 m), 0.07 m at 2390-2392 R
        /// (7.34 m), all "climbing back in". Past 0.8 the lines are still a
        /// fifth of the turning radius short of crossing — 2.3-3 m on those
        /// bends — so the slope carries on into that room at
        /// RoadsideRules.TraversableSlope (1V:3H, 0.043 m in the audit's
        /// 0.13 m, under its 0.06 m face) until it meets the lattice. Python
        /// replica of the plan, lattice, solve and emitter on all three: every
        /// half-section 0.043 m, nothing folded (no ribbon triangle faces
        /// down), and no new face on any other hairpin inside of either loop.
        /// </summary>
        const float ShoulderFoldFraction = 0.95f;
        /// <summary>
        /// THE TAIL: how steep a STAGE's carried slope goes once it is past the
        /// barrier warrant's reach without having met the land — 1V:2.5H.
        ///
        /// A 1V:4H carry that ran its ShoulderCarryMaxM without meeting the
        /// lattice ended where it ran out, 8.7 m past the tarmac edge, and tucked
        /// straight down to a lattice still 0.2-0.8 m under it: a ledge the body
        /// box meets climbing back up the slope. The 2026-09-14 bake measured
        /// them past the audit's reach, "info edge face past the reach" — Blue
        /// Ridge 13 half-sections in 7 runs (1232-1236 L 0.19 m at 8.80 m), Mount
        /// Mitchell 1538 R 0.27 m at 11.50 m, Beech Gap 254 L 0.16 m at 12.10 m,
        /// Blowing Rock 1621-1622 R, Little Switzerland 844-853 R 0.31 m — most
        /// of them the buried terminals of critical-fill walls, over lattice
        /// falling at 1V:1.8H to 1V:5H where the carry ran out. The 12 m facets
        /// chord under the fill section RoadsideDy grades (1V:6H, 1V:4H, 1V:3H,
        /// each steeper than the last), so the lattice sags under a slope that
        /// is itself gentler than the land, and the gap only grows the further
        /// a 1V:4H slope is carried. Two ways to close
        /// it were measured and dropped: carrying on from the depth cap at
        /// 1V:3H (it runs parallel to such a hillside, and met it on none of
        /// Blue Ridge 1226-1237 L within 30 m), and pulling the lattice up to
        /// the toe (the facet under a toe has its other corners 3-5 m out,
        /// under the section the solve holds down, so the corner beyond has to
        /// rise 0.5-1.2 m over the field on Blue Ridge 1094, 1236, 1400 and
        /// 1226 L — a berm across the hillside, under every station sharing it).
        ///
        /// Past <see cref="ShoulderTailE"/> nothing a car stands on is the
        /// warrant's business (RoadsideRules.WorstCriticalFall walks the section
        /// no further than the kerb strip plus that reach), and RDG practice
        /// lets a slope beyond the clear zone be steeper than traversable.
        /// 1V:2.5H is the steepest that is still not a face to the body box:
        /// 0.052 m in RoadsideRules.FaceRunM against its FaceRiseFailM of 0.06 m,
        /// on an exact ribbon (the shoulder is never mesh-compressed).
        ///
        /// ONLY WHERE THE CARRY FAILED. A carry that meets the land at 1V:4H is
        /// left exactly as it was; the tail replaces only one that ran out of
        /// depth without meeting it. Tailing every carry that passed the reach
        /// made the neighbour of a short one steeper too, and the zipper's fan
        /// between a 1V:4H catch and a 1V:2.5H tail four metres along the road is
        /// steeper than either line — the replica's rasterised fans (every pair
        /// of neighbouring sloped ends two metres and more apart, at 5 cm, along
        /// both axes and both diagonals) went from 47 with a face on the five
        /// mountains to 105; tailing only the failures, 33. And the plan's fall
        /// walk never sees the tail, which turns down past its last sample.
        ///
        /// Python replica of the plan, lattice, solve, both holds and emitter,
        /// own ribbon over the solved lattice to the ribbon's end, all eight
        /// stages: faces at a ribbon's toe 17/1/1/8/21 on Blue Ridge, Mount
        /// Mitchell, Beech Gap, Blowing Rock and Little Switzerland, 0/0/0/0/2
        /// with the tail (Little Switzerland 612 L, <see cref="ShoulderTailRunM"/>,
        /// and 611 L, where the lattice itself falls at 1V:1.8H just past the
        /// toe of a tail that met it — 0.073 m on Ground, not on the ribbon);
        /// no fall, no slope in a clear zone, no lattice within 3 cm of a shoulder,
        /// the same walls, the carry holds converging in no more passes, no
        /// shoulder triangle facing down. Read the way the obstacle audit reads it
        /// — barrier rays, walls, rock tops and the other legs as triangles, on
        /// every station of the five mountains — the round-three build gave back
        /// its own far-face lists exactly (Blue Ridge 13 half-sections in 7 runs,
        /// Little Switzerland 19 in 6), and with the tail none is on a station's
        /// own ribbon.
        /// </summary>
        const float ShoulderTailSlope = 0.4f;
        /// <summary>
        /// Furthest a tail runs past its knee looking for the land: one lattice
        /// cell (NearCell). A tail that has crossed a whole cell of facets without
        /// meeting them is on a hillside at least as steep as itself — Little
        /// Switzerland 612 L, a critical fill's terminal over land falling at
        /// 1V:1.8H-1V:2.6H for 15 m — and would only end higher over it further
        /// out, so there the carry is laid as it was, to its depth. The longest
        /// tail that met in the replica ran 11.4 m (Mount Mitchell 1539 R, from a
        /// section that already ended past the warrant's reach at 8.08 m, to
        /// 19.48 m).
        /// </summary>
        const float ShoulderTailRunM = 12f;
        /// <summary>Where a stage carry that has not met the land becomes the
        /// tail (<see cref="ShoulderTailSlope"/>): the reach of the barrier
        /// warrant past the graded shoulder, RoadsideRules.WarrantReachM — the
        /// same point OpenSectionDy steepens its own section to 1V:3H.</summary>
        static float ShoulderTailE => ShoulderEndE + RoadsideRules.WarrantReachM;
        /// <summary>Least outward step between two profile points: a profile
        /// that repeats an e would draw a vertical face, and a vertical face
        /// is exactly what a shoulder is not.</summary>
        const float ShoulderMinStepM = 0.01f;

        /// <summary>
        /// Per side (0 = left, 1 = right) per station, the profile BuildShoulders
        /// was given, tidied. Kept for the rest of the build so the ground
        /// lattice (<see cref="PrepareCircuitLattice"/>), the forecourt apron
        /// and the verge posts read the SAME section the ribbon was built from
        /// rather than three copies of it. Null until BuildShoulders runs.
        /// </summary>
        static List<Vector2>[][] shoulderProfiles;

        /// <summary>
        /// Emit the shoulder ribbon down both sides of the road from a
        /// per-station profile. Replaces BuildRoadEdge.
        ///
        /// Each station and side's profile, plus its slope carried on to the
        /// lattice where it ends on one, a toe tuck and a skirt, is
        /// stitched to the next station's with a zipper that advances by e
        /// and zips toe to toe (ZipShoulder),
        /// so two stations may carry sections with different point counts (a
        /// cut beside a fill, a ramp beside a flat) and still share one
        /// surface. A quad whose either end is empty is not drawn: that is
        /// a deck.
        ///
        /// GameObjects are all named "RoadEdge", whatever chunk they are, so
        /// the audits that exempt or check the shoulder by that exact name
        /// (TrackSweepAudit, AuditFacing) still find every one. Off the road
        /// layer on purpose: grip is decided by that layer, and a shoulder
        /// that gripped like tarmac would make running wide free.
        /// </summary>
        static void BuildShoulders(List<Vector3> pts, Transform parent, EdgeProfileFn profile)
        {
            shoulderProfiles = null;
            int n = pts != null ? pts.Count : 0;
            if (n < 2 || profile == null) return;
            int last = Loop ? n : n - 1;

            shoulderProfiles = new List<Vector2>[2][];
            int bent = 0;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var row = shoulderProfiles[s] = new List<Vector2>[n];
                for (int idx = 0; idx < n; idx++)
                {
                    var p = new List<Vector2>();
                    profile(pts, idx, side, p);
                    if (TidyShoulderProfile(pts, idx, side, p)) bent++;
                    row[idx] = p;
                }
            }

            // The ground lattice is solved against these profiles BEFORE a toe
            // is placed, so every toe reads the ground it will actually meet:
            // the circuit's one grid here, the stage's 12 m near grid in
            // PSXRacingBuilder.Stage.cs (which its ground pass then builds).
            if (!stageDemLoaded) PrepareCircuitLattice(pts);
            else PrepareStageLattice(pts);

            var pos = new Vector3[2][][];
            var es = new float[2][][];
            int toesDeep = 0, toesCaught = 0, toesPastBend = 0, toesTailed = 0;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                pos[s] = new Vector3[n][];
                es[s] = new float[n][];
                for (int idx = 0; idx < n; idx++)
                {
                    if (ShoulderStation(pts, idx, side, shoulderProfiles[s][idx],
                                        out pos[s][idx], out es[s][idx], out bool caught, out bool pastBend,
                                        out bool tailed))
                        toesDeep++;
                    if (caught) toesCaught++;
                    if (pastBend) toesPastBend++;
                    if (tailed) toesTailed++;
                }
            }

            // Looks like the ground beside it, because it IS that ground now:
            // the same texture, the same tint and the same world-metre UVs as
            // the lattice it meets (BuildGround / GridChunkMesh), so the seam
            // where the two cross is a crossing and not a border. The old
            // 0.42 darkening drew the batter as the road's slab — the "thick
            // road" the owner could see.
            // And it changes with the season the way that ground does: a forest
            // stage's near ground swaps to the season's turf texture
            // (BuildStageGround), everything else re-tints its own.
            Color tint = track.stage && string.IsNullOrEmpty(theme.sand)
                ? (theme.groundTint ?? Color.white) : Color.white;
            var mat = MakeMat(MeshPrefix + "RoadEdge", theme.ground, affine: 0f, tint: tint);
            if (track.stage && theme.stageForest)
                RegisterSeasonalTexture(MeshPrefix + "RoadEdge", mat, DressTurfPath, "edge");
            else
                RegisterSeasonalGround(MeshPrefix + "RoadEdge", theme.ground, mat, tint, "edge");
            float tile = Mathf.Max(theme.groundTile, 0.01f);
            GroundUvOrigin(pts, out float uox, out float uoz);
            var phys = SlidePhys();

            int per = Mathf.Max(1, Mathf.FloorToInt(ShoulderChunkM / Spacing));
            int chunks = 0, faces = 0;
            for (int c0 = 0; c0 < last; c0 += per)
            {
                int c1 = Mathf.Min(last, c0 + per);
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    int prevStart = -1, prevIdx = -1;
                    for (int i = c0; i <= c1; i++)
                    {
                        int idx = Loop ? i % n : i;
                        var w = pos[s][idx];
                        int start = -1;
                        if (w != null)
                        {
                            start = verts.Count;
                            foreach (var v in w)
                            {
                                verts.Add(v);
                                uvs.Add(new Vector2((v.x - uox) / tile, (v.z - uoz) / tile));
                            }
                        }
                        if (prevStart >= 0 && start >= 0)
                            ZipShoulder(tris, prevStart, es[s][prevIdx], start, es[s][idx], side);
                        prevStart = start;
                        prevIdx = idx;
                    }
                }
                if (tris.Count == 0) continue;

                var mesh = new Mesh
                {
                    indexFormat = verts.Count > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16,
                };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                SaveMesh(mesh, "RoadEdgeMesh_" + chunks);

                var go = new GameObject("RoadEdge");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                // The car's SHELL is what touches a shoulder face, and the
                // shoulder is ground — see SlidePhys.
                col.sharedMaterial = phys;
                go.isStatic = true;
                chunks++;
                faces += tris.Count / 3;
            }

            // What an earlier bake left: the one-piece RoadEdgeMesh, and any
            // chunk past this build's count (a shorter venue, fewer chunks).
            string legacy = GenDir + "/" + MeshPrefix + "RoadEdgeMesh.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(legacy) != null) AssetDatabase.DeleteAsset(legacy);
            for (int k = chunks; ; k++)
            {
                string stale = GenDir + "/" + MeshPrefix + "RoadEdgeMesh_" + k + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(stale) == null) break;
                AssetDatabase.DeleteAsset(stale);
            }

            Log($"Shoulders: {chunks} RoadEdge chunk(s), {faces} faces; " +
                $"{bent} half-section(s) clipped on the inside of a tight bend; " +
                $"{toesCaught} sloped end(s) carried on down to meet the ground lattice (or to the end of a recoverable run), " +
                $"{toesPastBend} of them on past a tight bend's inside at 1V:{1f / RoadsideRules.TraversableSlope:0}H, " +
                $"{toesTailed} down past the warrant's reach at 1V:{1f / ShoulderTailSlope:0.0}H to meet it; " +
                $"{toesDeep} toe(s) skirted deeper than a tuck to get under the ground.");
        }

        /// <summary>
        /// One station and side of the ribbon in world space: the profile's
        /// points, the slope carried on to its catch where the section ends
        /// on one (<see cref="ShoulderSlopedEnd"/>), then the toe tuck, then
        /// the skirt, with each point's e alongside for the zipper. Returns
        /// false for an empty profile's null arrays AND for a toe that stayed
        /// within a plain tuck; true when the lattice was lower than the tuck
        /// and the toe had to go further down (the tuck itself on a sloped
        /// end, only the skirt on a flat one). <paramref name="caught"/> says
        /// the slope was carried on, <paramref name="pastBend"/> that it went on
        /// past a tight bend's own limit, <paramref name="tailed"/> that it met
        /// the land as a stage's tail (<see cref="ShoulderCarry"/>).
        /// BuildShoulders counts all four.
        /// </summary>
        static bool ShoulderStation(List<Vector3> pts, int idx, float side, List<Vector2> prof,
                                    out Vector3[] pos, out float[] es, out bool caught, out bool pastBend,
                                    out bool tailed)
        {
            pos = null; es = null; caught = false; pastBend = false; tailed = false;
            if (prof == null || prof.Count == 0) return false;
            float half = RoadWidth * 0.5f;
            Vector3 outw = RightAt(pts, idx) * side;
            float roadY = pts[idx].y + RoadLift;
            int m = prof.Count;
            // On a deck the thing the toe crosses is the concrete box, which
            // is always under a tuck (DeckTopBelowTarmac down, DeckThick deep).
            // The "ground" there is the gorge floor, and reading it would carry
            // a slope on or drop the skirt out through the soffit.
            bool onDeck = DeckCoversStation(idx);

            float eEnd = prof[m - 1].x, yEnd = roadY + prof[m - 1].y;
            // Which kind of end this is decides the toe below: a slope is
            // carried on to the lattice and tucks from there; a flat end
            // keeps a plain tuck and lets only the skirt go down.
            bool slopedEnd = false, kneed = false;
            float kneeE = 0f, kneeY = 0f;
            if (!onDeck && m >= 2)
            {
                float slope = (prof[m - 2].y - prof[m - 1].y) /
                              Mathf.Max(prof[m - 1].x - prof[m - 2].x, 1e-5f);   // + falls outward
                slopedEnd = Mathf.Abs(slope) >= ShoulderSlopedEnd;
                Vector3 endP = pts[idx] + outw * (half + eEnd);
                if (slopedEnd && ShoulderLatticeY(endP.x, endP.z) < yEnd)
                {
                    // Down at the section's own fall, never gentler than the
                    // steepest recoverable slope (a backslope climbing to a
                    // rock toe turns down under the rock at that), until it
                    // meets the lattice: ShoulderCarry, the walk the stage
                    // plan's fall test takes too. A lattice still further down
                    // than it can reach is a drop, and what is left below is
                    // the tuck's.
                    float fall = Mathf.Max(slope, RoadsideRules.SteepestRecoverableSlope);
                    Vector3 at = pts[idx];
                    // A stage's carry turns into the tail past the warrant's
                    // reach (ShoulderTailSlope); a circuit's run-off keeps its
                    // carry as it was.
                    float tailE = stageDemLoaded ? ShoulderTailE : float.PositiveInfinity;
                    if (ShoulderCarry(eEnd, yEnd, fall, ShoulderBendReach(pts, idx, side), ShoulderFoldReach(pts, idx, side),
                                      tailE,
                                      e =>
                                      {
                                          Vector3 q = at + outw * (half + e);
                                          return ShoulderLatticeY(q.x, q.z);
                                      },
                                      out kneeE, out kneeY, out float carryE, out float carryY, out _, out pastBend,
                                      out tailed))
                    {
                        // A knee on the section's own last point is that point.
                        kneed = !float.IsNaN(kneeE) && kneeE > eEnd + ShoulderMinStepM;
                        eEnd = carryE;
                        yEnd = carryY;
                        caught = true;
                    }
                }
            }

            int tail = m + (kneed ? 1 : 0) + (caught ? 1 : 0);
            pos = new Vector3[tail + 2];
            es = new float[tail + 2];
            for (int k = 0; k < m; k++)
            {
                Vector3 p = pts[idx] + outw * (half + prof[k].x);
                p.y = roadY + prof[k].y;
                pos[k] = p;
                es[k] = prof[k].x;
            }
            if (kneed)
            {
                Vector3 c = pts[idx] + outw * (half + kneeE);
                c.y = kneeY;
                pos[m] = c;
                es[m] = kneeE;
            }
            if (caught)
            {
                Vector3 c = pts[idx] + outw * (half + eEnd);
                c.y = yEnd;
                pos[tail - 1] = c;
                es[tail - 1] = eEnd;
            }

            float eTuck = eEnd + RoadsideRules.ToeTuckRunM, eSkirt = eTuck + ShoulderSkirtRunM;
            Vector3 tuck = pts[idx] + outw * (half + eTuck);
            Vector3 skirt = pts[idx] + outw * (half + eSkirt);
            float yTuck = yEnd - RoadsideRules.ToeTuckM, ySkirt = yTuck;
            bool deeper = false;
            if (!onDeck)
            {
                float floor = yEnd - ShoulderSkirtMaxM;
                float latTuck = ShoulderLatticeY(tuck.x, tuck.z) - RoadsideRules.ToeTuckM;
                // A sloped end's tuck follows the lattice down (it has just
                // been carried to it, so the two are within a tuck); a flat
                // end's stays a plain tuck in front of whatever stops a car
                // there, and the skirt alone goes under the ground.
                if (latTuck < yTuck)
                {
                    if (slopedEnd) yTuck = Mathf.Max(floor, latTuck);
                    deeper = true;
                }
                ySkirt = Mathf.Max(floor,
                    Mathf.Min(yTuck, ShoulderLatticeY(skirt.x, skirt.z) - RoadsideRules.ToeTuckM)
                    - ShoulderSkirtSlackM);
            }
            tuck.y = yTuck;
            skirt.y = ySkirt;
            pos[tail] = tuck; es[tail] = eTuck;
            pos[tail + 1] = skirt; es[tail + 1] = eSkirt;
            return deeper;
        }

        /// <summary>
        /// THE CARRY, walked once for the emitter (ShoulderStation) and once
        /// for the stage plan's fall test (BuiltSectionCritical), so the slope
        /// the plan judges is the slope that gets built. From a section's last
        /// point (<paramref name="eEnd"/>, <paramref name="yEnd"/>) the slope
        /// goes on down at <paramref name="fall"/> in ShoulderCatchStepM steps
        /// until it is at or under <paramref name="land"/> (a height in the
        /// same frame as yEnd, at e) — no deeper than ShoulderCarryMaxM and no
        /// further than <paramref name="bendE"/>, where the inside of a bend
        /// clips a section (ShoulderBendReach).
        ///
        /// Where the BEND stopped it and not the depth, it goes on into the
        /// room left before the neighbouring lines cross (<paramref name="foldE"/>,
        /// ShoulderFoldReach) at the steeper of its fall and
        /// RoadsideRules.TraversableSlope — past the clear zone; inside it, at
        /// its own fall — and the point it went on from comes back as the
        /// knee (NaN when it did not go on; the section's own last point when
        /// the section already ended at the bend). See <see cref="ShoulderFoldFraction"/>
        /// for the hairpins that dived from their bend limit instead.
        ///
        /// On a stage (<paramref name="tailE"/> finite: <see cref="ShoulderTailE"/>;
        /// +infinity on a circuit), a carry that did NOT meet the land that way
        /// is walked again as the tail (<see cref="ShoulderTail"/>), and where the
        /// tail meets it, that is the carry — <paramref name="tailed"/>, with the
        /// knee at tailE, or at the section's own last point when the section
        /// ended past it. A carry that meets the land is never touched.
        ///
        /// False when there was no room to carry at all. <paramref name="met"/>
        /// says the slope really reached the land; a carry that ran out of
        /// depth or room, and whose tail found no land either, ends where it
        /// ran out, as it always has.
        /// </summary>
        static bool ShoulderCarry(float eEnd, float yEnd, float fall, float bendE, float foldE, float tailE,
                                  Func<float, float> land,
                                  out float kneeE, out float kneeY, out float carryE, out float carryY,
                                  out bool met, out bool pastBend, out bool tailed)
        {
            tailed = false;
            bool carried = ShoulderCarryToDepth(eEnd, yEnd, fall, bendE, foldE, land,
                                                out kneeE, out kneeY, out carryE, out carryY, out met, out pastBend);
            if (met || !ShoulderTail(eEnd, yEnd, fall, bendE, foldE, tailE, land,
                                     out float tailKneeE, out float tailKneeY, out float tailEndE, out float tailEndY))
                return carried;
            kneeE = tailKneeE; kneeY = tailKneeY;
            carryE = tailEndE; carryY = tailEndY;
            met = tailed = true;
            pastBend = false;
            return true;
        }

        /// <summary>The carry as it was laid before the tail: at its fall to the
        /// depth or the bend, then on past a bend's own limit (see
        /// <see cref="ShoulderCarry"/>).</summary>
        static bool ShoulderCarryToDepth(float eEnd, float yEnd, float fall, float bendE, float foldE,
                                         Func<float, float> land,
                                         out float kneeE, out float kneeY, out float carryE, out float carryY,
                                         out bool met, out bool pastBend)
        {
            kneeE = kneeY = float.NaN;
            carryE = eEnd; carryY = yEnd;
            met = pastBend = false;
            float depthRun = ShoulderCarryMaxM / fall;
            int steps = Mathf.FloorToInt(Mathf.Min(depthRun, bendE - eEnd) / ShoulderCatchStepM);
            for (int k = 1; k <= steps; k++)
            {
                float d = k * ShoulderCatchStepM;
                if (yEnd - fall * d > land(eEnd + d)) continue;
                carryE = eEnd + d;
                carryY = yEnd - fall * d;
                met = true;
                return true;
            }
            if (steps > 0)
            {
                carryE = eEnd + steps * ShoulderCatchStepM;
                carryY = yEnd - fall * steps * ShoulderCatchStepM;
            }
            // 1V:3H is traversable, not recoverable: never inside the clear
            // zone (a bend that tight carries on at its own fall instead).
            // A knee a float's width past its end is still on it to the
            // self-test, which reads the boundary back off world vertices a
            // few km out, so the knee has to be clear of it by a step.
            float steep = carryE >= ShoulderEndE + RoadsideRules.ClearZoneM + ShoulderMinStepM
                ? Mathf.Max(fall, RoadsideRules.TraversableSlope) : fall;
            int more = bendE - eEnd < depthRun
                ? Mathf.FloorToInt(Mathf.Min((ShoulderCarryMaxM - (yEnd - carryY)) / steep, foldE - carryE) / ShoulderCatchStepM)
                : 0;
            if (more <= 0) return steps > 0;
            kneeE = carryE; kneeY = carryY;
            float e0 = carryE, y0 = carryY;
            pastBend = true;
            for (int k = 1; k <= more; k++)
            {
                float d = k * ShoulderCatchStepM;
                met = y0 - steep * d <= land(e0 + d);
                if (!met && k < more) continue;
                carryE = e0 + d;
                carryY = y0 - steep * d;
                break;
            }
            return true;
        }

        /// <summary>
        /// The stage's tail (<see cref="ShoulderTailSlope"/>), for a carry that
        /// did not meet the land: from a knee at <paramref name="tailE"/> on the
        /// carry's own <paramref name="fall"/> (which that carry has already
        /// walked clear of the land out to there), or from the section's last
        /// point where the section ended past it, on down at ShoulderTailSlope —
        /// no further than ShoulderTailRunM and <paramref name="foldE"/> — to the
        /// first ShoulderCatchStepM at or under the land. False where the bend or
        /// the depth stopped the carry short of tailE, and where the tail finds
        /// no land in its run.
        /// </summary>
        static bool ShoulderTail(float eEnd, float yEnd, float fall, float bendE, float foldE, float tailE,
                                 Func<float, float> land,
                                 out float kneeE, out float kneeY, out float carryE, out float carryY)
        {
            kneeE = kneeY = carryE = carryY = float.NaN;
            if (float.IsInfinity(tailE)) return false;
            if (eEnd < tailE)
            {
                if (tailE - eEnd > Mathf.Min(ShoulderCarryMaxM / fall, bendE - eEnd) + 1e-4f) return false;
                kneeE = tailE;
                kneeY = yEnd - fall * (tailE - eEnd);
            }
            else
            {
                kneeE = eEnd;
                kneeY = yEnd;
            }
            int steps = Mathf.FloorToInt(Mathf.Min(ShoulderTailRunM, foldE - kneeE) / ShoulderCatchStepM);
            for (int k = 1; k <= steps; k++)
            {
                float d = k * ShoulderCatchStepM;
                float y = kneeY - ShoulderTailSlope * d;
                if (y > land(kneeE + d)) continue;
                carryE = kneeE + d;
                carryY = y;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Triangles between two stations' point runs, advancing along
        /// whichever run's NEXT point is nearer the road — the toes keyed
        /// after both surfaces, below. Any order that advances both runs
        /// outward lays the triangles side by side in plan, never over one
        /// another, while the two stations' lines do not cross (the bend
        /// reaches). The winding is the old RoadEdge quad's, per side (the
        /// corner order flips with `side` because `outw` does), so every face
        /// points up and out.
        ///
        /// THE TOE ZIPS TO THE TOE. Every run ends in its tuck and skirt
        /// (ShoulderStation), and those two advance only once BOTH runs'
        /// surfaces are done — keyed past the further of the two surface
        /// ends by their own offsets. Advanced purely by e, a station whose
        /// slope was carried a short way put its tuck and skirt among the
        /// points of a neighbour carried further, and the fan from that deep
        /// skirt up to the neighbour's surface was a trough between the two
        /// stations with a face up its far side. A station's own line never
        /// crosses it (it reads only its own vertices), which is why no edge
        /// probe of that station saw it; another road's line does: Little
        /// Switzerland wp 1790-1792 L and 715 L, two legs of the loop 22.5 m
        /// apart whose shoulders meet in the valley between them, measured
        /// "0.11 m rise within 0.13 m at 6.25 m (running wide), on
        /// Track/RoadEdge" across 1790-1791's fan. Zipped toe to toe, the
        /// surface between the stations runs from one surface end to the
        /// other and the toes pass under it together: 0.044 m in the replica.
        /// Where the two surfaces end at the same e this is exactly the old
        /// order, so a circuit's run-off is unchanged.
        /// </summary>
        static void ZipShoulder(List<int> tris, int a0, float[] ea, int b0, float[] eb, float side)
        {
            int i = 0, j = 0, na = ea.Length, nb = eb.Length;
            float endK = Mathf.Max(ea[Mathf.Max(na - 3, 0)], eb[Mathf.Max(nb - 3, 0)]);
            float KeyA(int k) => k < na - 2 ? ea[k] : endK + (ea[k] - ea[Mathf.Max(na - 3, 0)]);
            float KeyB(int k) => k < nb - 2 ? eb[k] : endK + (eb[k] - eb[Mathf.Max(nb - 3, 0)]);
            while (i < na - 1 || j < nb - 1)
            {
                bool stepA = j >= nb - 1 || (i < na - 1 && KeyA(i + 1) <= KeyB(j + 1));
                if (stepA)
                {
                    if (side < 0f) { tris.Add(a0 + i); tris.Add(a0 + i + 1); tris.Add(b0 + j); }
                    else { tris.Add(a0 + i); tris.Add(b0 + j); tris.Add(a0 + i + 1); }
                    i++;
                }
                else
                {
                    if (side < 0f) { tris.Add(a0 + i); tris.Add(b0 + j + 1); tris.Add(b0 + j); }
                    else { tris.Add(a0 + i); tris.Add(b0 + j); tris.Add(b0 + j + 1); }
                    j++;
                }
            }
        }

        /// <summary>
        /// Make a profile safe to emit: finite points only, strictly outward
        /// by <see cref="ShoulderMinStepM"/>, its first point ON the strip's
        /// outer edge, and on the inside of a bend cut short of where the
        /// neighbouring cross-sections meet. Returns true when the bend cut it.
        ///
        /// The first point is pinned rather than trusted because the strip's
        /// height is this file's fact (KerbStripLift, KerbLiftAt) and a
        /// profile written elsewhere can only copy it: a copy one centimetre
        /// out is a centimetre-tall crack along the whole road, which is the
        /// one place the ribbon must be exact.
        /// </summary>
        static bool TidyShoulderProfile(List<Vector3> pts, int idx, float side, List<Vector2> p)
        {
            p.RemoveAll(v => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y));
            if (p.Count == 0) return false;
            if (Mathf.Abs(p[0].x - KerbWidth) < 0.05f)
                p[0] = new Vector2(KerbWidth, KerbStripLift + KerbLiftAt(idx, pts.Count, side));
            for (int k = 1; k < p.Count; k++)
                if (p[k].x < p[k - 1].x + ShoulderMinStepM)
                    p[k] = new Vector2(p[k - 1].x + ShoulderMinStepM, p[k].y);

            float reach = Mathf.Max(ShoulderBendReach(pts, idx, side), p[0].x);
            if (p[p.Count - 1].x <= reach) return false;
            for (int k = 1; k < p.Count; k++)
            {
                if (p[k].x <= reach) continue;
                float t = Mathf.InverseLerp(p[k - 1].x, p[k].x, reach);
                float y = Mathf.Lerp(p[k - 1].y, p[k].y, t);
                p.RemoveRange(k, p.Count - k);
                if (reach > p[k - 1].x + ShoulderMinStepM) p.Add(new Vector2(reach, y));
                break;
            }
            return true;
        }

        /// <summary>Largest e a shoulder may reach at this station and side
        /// before its cross-section line meets a neighbour's — the inside of a
        /// bend, where the two lines converge at the turning radius. Infinite
        /// on the outside of a bend and on a straight.</summary>
        static float ShoulderBendReach(List<Vector3> pts, int idx, float side) =>
            ShoulderLinesMeet(pts, idx, side, ShoulderInsideBendReach);

        /// <summary>Largest e a CARRIED slope's end may reach on the inside of
        /// a bend: <see cref="ShoulderFoldFraction"/> of the way to where the
        /// lines cross, less the toe that is laid past it.</summary>
        static float ShoulderFoldReach(List<Vector3> pts, int idx, float side) =>
            ShoulderLinesMeet(pts, idx, side, ShoulderFoldFraction) - (RoadsideRules.ToeTuckRunM + ShoulderSkirtRunM);

        /// <summary><paramref name="fraction"/> of the distance from the
        /// centreline at which this station's cross-section line meets a
        /// neighbour's, as e past the tarmac edge.</summary>
        static float ShoulderLinesMeet(List<Vector3> pts, int idx, float side, float fraction)
        {
            int n = pts.Count;
            float half = RoadWidth * 0.5f;
            float reach = float.MaxValue;
            Vector3 outHere = RightAt(pts, idx) * side;
            for (int o = -1; o <= 1; o += 2)
            {
                int j = Loop ? ((idx + o) % n + n) % n : idx + o;
                if (j < 0 || j >= n) continue;
                Vector3 step = pts[j] - pts[idx];
                step.y = 0f;
                float len = step.magnitude;
                if (len < 1e-3f) continue;
                // The two lines close up toward j when the outward vectors
                // turn against the direction of travel; they meet at about
                // len / |turn| metres out.
                Vector3 turn = RightAt(pts, j) * side - outHere;
                if (Vector3.Dot(turn, step) >= 0f) continue;
                float mag = turn.magnitude;
                if (mag < 1e-5f) continue;
                reach = Mathf.Min(reach, fraction * len / mag - half);
            }
            return reach;
        }

        /// <summary>
        /// dy of a profile at <paramref name="e"/>: linear between its points,
        /// the first point's height inside it (the strip), and past its last
        /// point either carried on along the last segment
        /// (<paramref name="extend"/>) or NaN.
        /// </summary>
        static float EvalShoulderProfile(List<Vector2> p, float e, bool extend)
        {
            int m = p != null ? p.Count : 0;
            if (m == 0) return float.NaN;
            if (e <= p[0].x) return p[0].y;
            for (int k = 1; k < m; k++)
                if (e <= p[k].x)
                    return Mathf.Lerp(p[k - 1].y, p[k].y, Mathf.InverseLerp(p[k - 1].x, p[k].x, e));
            if (!extend) return float.NaN;
            if (m == 1) return p[0].y;
            float slope = (p[m - 1].y - p[m - 2].y) / Mathf.Max(p[m - 1].x - p[m - 2].x, 1e-5f);
            return p[m - 1].y + slope * (e - p[m - 1].x);
        }

        /// <summary>
        /// The designed surface beside the road at a world point, from the
        /// profiles BuildShoulders kept: the tarmac's height over the road,
        /// the profile (carried on past its last point) beside it. e is the
        /// point's distance past the tarmac edge, eLast where that station
        /// pair's shoulder ends. False when there is no shoulder to ask (no
        /// profiles yet, or a deck quad with no ribbon).
        /// </summary>
        static bool ShoulderDesignAt(List<Vector3> pts, float x, float z,
                                     out float y, out float e, out float eLast)
        {
            y = 0f; e = 0f; eLast = 0f;
            int n = pts != null ? pts.Count : 0;
            if (shoulderProfiles == null || n < 2) return false;
            int last = Loop ? n : n - 1;
            int a = -1;
            float bestD2 = float.MaxValue, bestT = 0f;
            for (int i = 0; i < last; i++)
            {
                int j = Loop ? (i + 1) % n : i + 1;
                float ax = pts[i].x, az = pts[i].z;
                float ex = pts[j].x - ax, ez = pts[j].z - az;
                float len2 = ex * ex + ez * ez;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((x - ax) * ex + (z - az) * ez) / len2);
                float dx = ax + ex * t - x, dz = az + ez * t - z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; a = i; bestT = t; }
            }
            if (a < 0) return false;
            int b = Loop ? (a + 1) % n : a + 1;
            Vector3 seg = pts[b] - pts[a];
            seg.y = 0f;
            if (seg.sqrMagnitude < 1e-6f) return false;
            Vector3 right = Vector3.Cross(Vector3.up, seg.normalized);
            Vector3 foot = Vector3.Lerp(pts[a], pts[b], bestT);
            float lateral = (x - foot.x) * right.x + (z - foot.z) * right.z;
            e = Mathf.Abs(lateral) - RoadWidth * 0.5f;
            float roadY = foot.y + RoadLift;
            if (e <= 0f) { y = roadY; eLast = float.MaxValue; return true; }
            int s = lateral < 0f ? 0 : 1;
            var pa = shoulderProfiles[s][a];
            var pb = shoulderProfiles[s][b];
            if (pa.Count == 0 || pb.Count == 0) return false;
            y = roadY + Mathf.Lerp(EvalShoulderProfile(pa, e, true), EvalShoulderProfile(pb, e, true), bestT);
            eLast = Mathf.Lerp(pa[pa.Count - 1].x, pb[pb.Count - 1].x, bestT);
            return true;
        }

        /// <summary>
        /// A circuit's roadside: a graded run-off from the kerb strip to the
        /// perimeter wall, whose face hides where it ends.
        ///
        ///   strip top (Racing: the tarmac; Street: the pavement, 15 cm up,
        ///   0 across a driveway) -> the Safety Edge bevel down EdgeDropM ->
        ///   2% fall to RunoffWallGapM short of the wall face.
        ///
        /// The Street curb keeps its picture and its 33% mountable ramp
        /// collider (the owner: "concrete textured curbs" on a street), and
        /// the run-off behind it is graded to the PAVEMENT, so leaving and
        /// rejoining is one curb, not a berm and then a batter. At the
        /// forecourt driveways KerbLiftAt drops the curb and the run-off
        /// starts flush; the apron then lies over it (BuildApron).
        ///
        /// On a deck (DeckCoversStation) the deck top is the surface:
        ///   * a Racing strip bevels straight onto the concrete — EMPTY over
        ///     the span itself, and at the station either end (where the deck
        ///     is carried onto solid ground) out to the wall at the deck top
        ///     exactly, so the run-off arriving from the approach meets the
        ///     deck's leading edge with no step;
        ///   * a Street pavement ramps down 1V:6H (RecoverableSlope) to the
        ///     deck top — the berm-then-drop the pavement would otherwise
        ///     be on every bridge — ending just under the concrete over the
        ///     span, and running on flush with it at the end stations.
        /// </summary>
        static void CircuitEdgeProfile(List<Vector3> pts, int idx, float side, List<Vector2> profile)
        {
            profile.Clear();
            int n = pts.Count;
            float top = KerbStripLift + KerbLiftAt(idx, n, side);
            float bevelE = KerbWidth + SafetyEdgeRunM;
            float bevelY = top - RoadsideRules.EdgeDropM;
            float endE = Mathf.Max(bevelE, WallOffset - RunoffWallGapM - RoadWidth * 0.5f);

            if (DeckCoversStation(idx))
            {
                bool span = bridgeBlend[idx] > DeckBlendMin;
                float deckDy = -DeckTopBelowTarmac;
                if (bevelY <= deckDy)
                {
                    if (span) return;
                    profile.Add(new Vector2(KerbWidth, top));
                    profile.Add(new Vector2(bevelE, deckDy));
                    if (endE > bevelE) profile.Add(new Vector2(endE, deckDy));
                    return;
                }
                // Over the span the ramp ends DeckHideM under the concrete,
                // so its flat never lies in the deck's own plane.
                float floorDy = span ? deckDy - DeckHideM : deckDy;
                float slope = RoadsideRules.RecoverableSlope;
                float footE = Mathf.Min(endE, bevelE + (bevelY - floorDy) / slope);
                profile.Add(new Vector2(KerbWidth, top));
                profile.Add(new Vector2(bevelE, bevelY));
                if (footE > bevelE)
                    profile.Add(new Vector2(footE, Mathf.Max(floorDy, bevelY - (footE - bevelE) * slope)));
                if (!span && endE > footE) profile.Add(new Vector2(endE, deckDy));
                return;
            }

            profile.Add(new Vector2(KerbWidth, top));
            profile.Add(new Vector2(bevelE, bevelY));
            if (endE > bevelE)
                profile.Add(new Vector2(endE, bevelY - (endE - bevelE) * RunoffCrossFall));
        }

        // ------------------------------------------------------------------
        //  The ground lattice under the shoulder
        // ------------------------------------------------------------------
        /// <summary>Cells a side in the circuit ground grid. 144 since the
        /// ground started following the road: a cell is about 9 m across, and
        /// the whole reason CorridorR is six metres wider than the barrier
        /// line is that a cell that size cannot be trusted to land anywhere
        /// in particular.</summary>
        const int GroundCells = 144;
        /// <summary>The circuit ground mesh's vertex heights, row by row
        /// ((GroundCells+1)^2), solved by <see cref="PrepareCircuitLattice"/>
        /// before the shoulder's toes are placed and consumed by BuildGround.
        /// Null on a stage and before the first road of a track.</summary>
        static float[] circuitLattice;
        static float circuitGroundOx, circuitGroundOz, circuitGroundSize;

        /// <summary>The circuit ground mesh's frame: centred on the route's
        /// plan bounds, 380 m of apron past its furthest waypoint each way.
        /// One function for BuildGround and the lattice solve, so the grid the
        /// toes read IS the grid that gets built.</summary>
        static void CircuitGroundFrame(List<Vector3> pts, out float ox, out float oz, out float size)
        {
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            ox = b.center.x;
            oz = b.center.z;
            size = Mathf.Max(b.size.x, b.size.z) + 760f;
        }

        /// <summary>World-metre UV origin shared by the ground and the
        /// shoulder: the circuit ground mesh is local to its centre, the
        /// stage's chunks are in world metres.</summary>
        static void GroundUvOrigin(List<Vector3> pts, out float ox, out float oz)
        {
            ox = oz = 0f;
            if (track != null && track.stage) return;
            CircuitGroundFrame(pts, out ox, out oz, out _);
        }

        /// <summary>
        /// Heights of the circuit ground mesh, with every vertex under a
        /// designed surface — tarmac, kerb strip, shoulder — held at least
        /// <see cref="RoadsideRules.HideMarginM"/> below it.
        ///
        /// Before, the only thing keeping the lattice down was the roadbed dig
        /// and the corridor sink, and the circuits reader measured what a 9 m
        /// cell does with them: a triangle with one vertex in the dig
        /// (-0.33) and one out in the relief blend smears the verge anywhere
        /// from -0.20 to -0.43 under the plane. That was invisible under a
        /// batter; under a flush run-off 1-8 cm below the tarmac it is 7-14 cm
        /// of margin on a good day and ground through the shoulder on a bad
        /// one (Ridge Pass's 13 m of relief, a crest).
        ///
        /// So the design surface is SAMPLED — every 4/3 m along each quad,
        /// at every profile point and at most a metre apart across it — and
        /// wherever the lattice triangle under a sample comes within the
        /// margin, that triangle's three vertices go down by the excess. The
        /// weights under any point sum to one, so that sample is then exactly
        /// satisfied; a second pass takes the residue between samples. It only
        /// ever LOWERS ground, so nothing that cleared the tarmac before stops
        /// clearing it, and it is a no-op wherever the corridor already held.
        /// </summary>
        static void PrepareCircuitLattice(List<Vector3> pts)
        {
            CircuitGroundFrame(pts, out circuitGroundOx, out circuitGroundOz, out circuitGroundSize);
            int stride = GroundCells + 1;
            var h = new float[stride * stride];
            for (int y = 0; y <= GroundCells; y++)
                for (int x = 0; x <= GroundCells; x++)
                {
                    float wx = (x / (float)GroundCells - 0.5f) * circuitGroundSize;
                    float wz = (y / (float)GroundCells - 0.5f) * circuitGroundSize;
                    h[y * stride + x] = GroundHeightAt(wx + circuitGroundOx, wz + circuitGroundOz);
                }
            circuitLattice = h;
            if (shoulderProfiles == null) return;

            var want = new float[h.Length];
            var moved = new bool[h.Length];
            float worst = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                Array.Clear(want, 0, want.Length);
                bool any = false;
                ForEachShoulderSample(pts, (x, z, top) =>
                {
                    if (!CircuitLatticeTri(x, z, out int i0, out int i1, out int i2,
                                           out float w0, out float w1, out float w2)) return;
                    float excess = h[i0] * w0 + h[i1] * w1 + h[i2] * w2 - (top - RoadsideRules.HideMarginM);
                    if (excess <= 0f) return;
                    any = true;
                    if (excess > want[i0]) want[i0] = excess;
                    if (excess > want[i1]) want[i1] = excess;
                    if (excess > want[i2]) want[i2] = excess;
                });
                if (!any) break;
                for (int v = 0; v < h.Length; v++)
                {
                    if (want[v] <= 0f) continue;
                    h[v] -= want[v];
                    moved[v] = true;
                    worst = Mathf.Max(worst, want[v]);
                }
            }
            int count = 0;
            foreach (bool m in moved) if (m) count++;
            Log(count == 0
                ? $"Ground lattice: already {RoadsideRules.HideMarginM:0.00} m under every road and shoulder surface."
                : $"Ground lattice: {count} vertices lowered (up to {worst:0.000} m) to sit " +
                  $"{RoadsideRules.HideMarginM:0.00} m under the road and shoulder.");
        }

        /// <summary>
        /// Points on every designed surface beside the road, with the height
        /// the surface has there: the tarmac and kerb strip (at the tarmac's
        /// own height, which is the lower of the two on every style), and each
        /// shoulder quad between two non-empty profiles out to the shorter
        /// one's last point.
        /// </summary>
        static void ForEachShoulderSample(List<Vector3> pts, Action<float, float, float> visit)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            float half = RoadWidth * 0.5f;
            var across = new List<float>();
            for (int i = 0; i < last; i++)
            {
                int a = i, b = Loop ? (i + 1) % n : i + 1;
                Vector3 ra = RightAt(pts, a), rb = RightAt(pts, b);
                float ya = pts[a].y + RoadLift, yb = pts[b].y + RoadLift;
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    var pa = shoulderProfiles[s][a];
                    var pb = shoulderProfiles[s][b];

                    across.Clear();
                    // The road and the strip, centreline out, a metre and a
                    // half apart at most.
                    int roadSteps = Mathf.CeilToInt((half + KerbWidth) / 1.5f);
                    for (int k = 0; k <= roadSteps; k++)
                        across.Add(-half + (half + KerbWidth) * k / roadSteps);
                    int roadCount = across.Count;
                    if (pa.Count > 0 && pb.Count > 0)
                    {
                        float reach = Mathf.Min(pa[pa.Count - 1].x, pb[pb.Count - 1].x);
                        AddAcross(across, pa, reach);
                        AddAcross(across, pb, reach);
                    }

                    for (int k = 0; k < across.Count; k++)
                    {
                        float e = across[k];
                        float dyA = 0f, dyB = 0f;
                        if (k >= roadCount)
                        {
                            dyA = EvalShoulderProfile(pa, e, false);
                            dyB = EvalShoulderProfile(pb, e, false);
                            if (float.IsNaN(dyA) || float.IsNaN(dyB)) continue;
                        }
                        Vector3 pA = pts[a] + ra * side * (half + e);
                        Vector3 pB = pts[b] + rb * side * (half + e);
                        for (int q = 0; q < 3; q++)
                        {
                            float t = q / 3f;
                            visit(Mathf.Lerp(pA.x, pB.x, t), Mathf.Lerp(pA.z, pB.z, t),
                                  Mathf.Lerp(ya + dyA, yb + dyB, t));
                        }
                    }
                }
            }
        }

        /// <summary>Every point of a profile out to <paramref name="reach"/>,
        /// and enough between them that no two are more than a metre
        /// apart.</summary>
        static void AddAcross(List<float> into, List<Vector2> p, float reach)
        {
            for (int k = 0; k < p.Count; k++)
            {
                float e0 = p[k].x;
                if (e0 > reach) break;
                into.Add(e0);
                float e1 = k + 1 < p.Count ? Mathf.Min(p[k + 1].x, reach) : e0;
                int parts = Mathf.CeilToInt((e1 - e0) / 1f);
                for (int q = 1; q < parts; q++) into.Add(e0 + (e1 - e0) * q / parts);
            }
        }

        /// <summary>The circuit ground-mesh triangle under a world point, as
        /// three vertex indices and their barycentric weights. The same
        /// diagonal BuildGround's two triangles per cell use: (x,y)-(x,y+1)-
        /// (x+1,y+1) above it, (x,y)-(x+1,y)-(x+1,y+1) below.</summary>
        static bool CircuitLatticeTri(float x, float z, out int i0, out int i1, out int i2,
                                      out float w0, out float w1, out float w2)
        {
            i0 = i1 = i2 = 0; w0 = w1 = w2 = 0f;
            float gx = ((x - circuitGroundOx) / circuitGroundSize + 0.5f) * GroundCells;
            float gz = ((z - circuitGroundOz) / circuitGroundSize + 0.5f) * GroundCells;
            int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
            if (ix < 0 || iz < 0 || ix >= GroundCells || iz >= GroundCells) return false;
            float fu = gx - ix, fw = gz - iz;
            int stride = GroundCells + 1;
            int v00 = iz * stride + ix, v01 = v00 + stride, v11 = v01 + 1, v10 = v00 + 1;
            if (fw > fu) { i0 = v00; w0 = 1f - fw; i1 = v01; w1 = fw - fu; i2 = v11; w2 = fu; }
            else { i0 = v00; w0 = 1f - fu; i1 = v10; w1 = fu - fw; i2 = v11; w2 = fw; }
            return true;
        }

        /// <summary>
        /// Height of the ground MESH a car would land on at a world point —
        /// not the analytic field, which is only the same thing at the
        /// vertices. The circuit's is the solved lattice exactly; the stage's
        /// is its 12 m near grid, read back by the stage's own
        /// StageLatticeY so the toes and the stage's walls and rock tops
        /// measure against one lattice; anything else falls back to the field.
        /// </summary>
        static float ShoulderLatticeY(float x, float z)
        {
            if (stageDemLoaded) return StageLatticeY(x, z);
            if (circuitLattice != null &&
                CircuitLatticeTri(x, z, out int i0, out int i1, out int i2, out float w0, out float w1, out float w2))
                return circuitLattice[i0] * w0 + circuitLattice[i1] * w1 + circuitLattice[i2] * w2;
            return GroundHeightAt(x, z);
        }

        /// <summary>
        /// The strip along the exact edge of the tarmac, in whichever
        /// <see cref="KerbStyle"/> the venue wears. The barrier sits 4 m out
        /// in the gravel, so without this there is nothing marking where grip
        /// actually ends — the road just fades into dirt.
        ///
        /// Racing and Verge are the flat two-verts-per-station ribbon this
        /// has always been, textured red/white or gravel. Street is a SECTION:
        /// a battered 15 cm face, a 30 cm curb-stone top and a 60 cm pavement
        /// slab — and, on the same object, a collider that is NOT the picture
        /// but a ramp (<see cref="StreetKerbRamp"/>), because a solid vertical
        /// lip at the tarmac edge is a wall the car's body stops dead against
        /// before its wheel ray ever lifts it (the failure WorldKit's
        /// render-only skirts and the neighbourhood's collider-less kerb were
        /// both built to avoid). Dropped across the forecourt driveways
        /// (<see cref="KerbLiftAt"/>). Same 0.9 m footprint in every style:
        /// the shoulder profile starts at its outer edge (EdgeProfileFn), and
        /// four other constants key on its width.
        /// </summary>
        static void BuildKerbs(List<Vector3> pts, Transform parent)
        {
            var style = KerbStyleFor(track);
            // MeshPrefix on EVERY branch. The circuit branch used to make one
            // shared Materials/Kerb.mat — harmless while every circuit wanted
            // the same texture, and a trap the moment there were two: the last
            // venue built repaints every other venue's kerb, exactly as
            // BuildRoad records for its Road material.
            string tex = style == KerbStyle.Verge ? StageGenDir + "/Shoulder.png"
                       : style == KerbStyle.Street ? StreetKerbTexPath
                       : KerbTexPath;
            var mat = MakeMat(MeshPrefix + "Kerb", tex, affine: 0f, wet: WetKerb);
            mat.mainTextureScale = new Vector2(1f, 1f);

            foreach (float side in new[] { -1f, 1f })
            {
                Mesh mesh, coll;
                if (style == KerbStyle.Street) mesh = StreetKerbMeshes(pts, side, out coll);
                else { mesh = FlatKerbMesh(pts, side); coll = mesh; }
                SaveMesh(mesh, side < 0 ? "KerbMeshL" : "KerbMeshR");
                if (coll != mesh) SaveMesh(coll, side < 0 ? "KerbCollMeshL" : "KerbCollMeshR");

                var go = new GameObject(side < 0 ? "KerbL" : "KerbR");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                // A COLLIDER, which this strip once had none of.
                //
                // The road mesh stops dead at RoadWidth/2 and the strip runs
                // 0.9 m further out, so for its whole width there was nothing
                // under the car at all: a wheel over the white line raycast
                // straight past the kerb it can SEE and landed on the ground
                // half a metre below. A car putting two wheels wide therefore
                // fell into a trench at the exact edge of the tarmac and then
                // had a lip taller than its own wheels between it and the way
                // back — invisible, because the picture showed a kerb.
                //
                // On the road layer for a racing kerb, because it grips; off
                // it on a stage, where the same geometry is drawn as a gravel
                // shoulder and should not. The street curb stays ON it: the
                // pavement is concrete, not gravel, and the road layer is also
                // what keeps TrackSweepAudit skipping this object — its top at
                // +0.27 is level with the sweep box's floor. A pavement
                // that should punish running wide would need to come off the
                // layer AND be exempted there by name, like RoadEdge.
                if (style != KerbStyle.Verge) go.layer = RoadLayer;
                go.AddComponent<MeshCollider>().sharedMesh = coll;
                go.isStatic = true;
            }
            Log($"Built {style} kerb strips at the tarmac edge.");
        }

        /// <summary>
        /// Two triangles between station i and station i+1 of a strip laid
        /// down <paramref name="stride"/> vertices per station, joining the
        /// vertex at <paramref name="v"/> and the one after it to their
        /// counterparts a station on. Opposite winding on the two sides,
        /// because `outw` flips with `side` and the corner order goes with it
        /// — the same branch the old RoadEdge batter always had, and which the kerb
        /// strip once never got. Without it the LEFT kerb faced downward: it
        /// was invisible from the car (which is why every screenshot of this
        /// game had a kerb on one side only), and once the strip carried a
        /// collider its back face was invisible to a downward raycast too, so
        /// the run-off audit found half a metre of missing surface down the
        /// left of all four circuits and none down the right.
        ///
        /// The pattern is the flat strip's original (stride 2:
        /// {v,v+1,v+2, v+1,v+3,v+2} / {v,v+2,v+1, v+1,v+2,v+3}) with the
        /// station-on offset named; it is COPIED, not re-derived, because
        /// per-call-site winding is the recorded trap.
        /// </summary>
        static void KerbQuad(List<int> tris, int v, int stride, float side)
        {
            if (side < 0f) tris.AddRange(new[] { v, v + 1, v + stride, v + 1, v + stride + 1, v + stride });
            else tris.AddRange(new[] { v, v + stride, v + 1, v + 1, v + stride, v + stride + 1 });
        }

        /// <summary>The Racing/Verge ribbon: two verts per station, flush
        /// with the road (<see cref="KerbStripLift"/>), u along the road at
        /// one repeat per 2 m (the classic red/white dashing), v across.</summary>
        static Mesh FlatKerbMesh(List<Vector3> pts, float side)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float dist = 0f;

            for (int i = 0; i <= last; i++)
            {
                int idx = Loop ? i % n : i;
                Vector3 outw = RightAt(pts, idx) * side;
                // Flush with the road ribbon, which ends exactly where this
                // begins: they share an edge and never an area, so there is no
                // depth to fight over.
                Vector3 inner = pts[idx] + Vector3.up * (RoadLift + KerbStripLift) + outw * (RoadWidth * 0.5f);
                Vector3 outer = inner + outw * KerbWidth;
                int v = verts.Count;
                verts.Add(inner); verts.Add(outer);
                // Repeat every 2 m of travel gives the classic red/white dashing.
                uvs.Add(new Vector2(dist / 2f, 0f));
                uvs.Add(new Vector2(dist / 2f, 1f));
                dist += Spacing;
                if (i < last) KerbQuad(tris, v, 2, side);
            }
            return new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
        }

        /// <summary>
        /// The Street section, per station and side, four verts A-B-C-D:
        ///
        ///   A  foot of the face, tarmac edge, flush (as the flat strip)  v 0
        ///   B  top of the face: A + lift, leaned out by the batter         v 0.25
        ///   C  back of the curb stone's top, StreetKerbTop out            v 0.5
        ///   D  back edge of the pavement, KerbWidth out                   v 1
        ///
        /// u = dist / StreetKerbUTile on all four, so the bands of
        /// StreetKerb.png land on face, top and pavement. `lift` is the
        /// curb height here (0 across a driveway) and the same number
        /// CircuitEdgeProfile starts the run-off at, so the run-off behind
        /// the curb is graded to the pavement.
        ///
        /// The COLLIDER is a second mesh on three verts: A, then R = A + lift
        /// at StreetKerbRamp out, then D — a 33% ramp and a flat top. Over the
        /// first 0.3 m the wheel raycast rides the ramp while the picture
        /// shows a face, so a wheel on the curb stone sits up to ~10 cm into
        /// it; at 240 lines from the chase camera that is the same class of
        /// compromise as WorldKit's render-only skirts.
        /// </summary>
        static Mesh StreetKerbMeshes(List<Vector3> pts, float side, out Mesh collider)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var cverts = new List<Vector3>();
            var ctris = new List<int>();
            float dist = 0f;

            for (int i = 0; i <= last; i++)
            {
                int idx = Loop ? i % n : i;
                Vector3 outw = RightAt(pts, idx) * side;
                Vector3 rise = Vector3.up * KerbLiftAt(idx, n, side);
                Vector3 a = pts[idx] + Vector3.up * (RoadLift + KerbStripLift) + outw * (RoadWidth * 0.5f);
                Vector3 b = a + rise + outw * StreetKerbFaceBatter;
                Vector3 c = a + rise + outw * StreetKerbTop;
                Vector3 d = a + rise + outw * KerbWidth;
                float u = dist / StreetKerbUTile;
                int v = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(new Vector2(u, 0f)); uvs.Add(new Vector2(u, 0.25f));
                uvs.Add(new Vector2(u, 0.5f)); uvs.Add(new Vector2(u, 1f));

                int w = cverts.Count;
                cverts.Add(a); cverts.Add(a + rise + outw * StreetKerbRamp); cverts.Add(d);
                dist += Spacing;

                if (i < last)
                {
                    // Face, top, pavement — three quads on a stride of four.
                    for (int k = 0; k < 3; k++) KerbQuad(tris, v + k, 4, side);
                    // Ramp, flat — two quads on a stride of three.
                    for (int k = 0; k < 2; k++) KerbQuad(ctris, w + k, 3, side);
                }
            }
            collider = new Mesh { vertices = cverts.ToArray(), triangles = ctris.ToArray() };
            return new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
        }

        static void BuildWalls(List<Vector3> pts, Transform parent)
        {
            var wallMat = MakeMat(MeshPrefix + "Wall", theme.wall, affine: 0f);
            var physMat = GetOrCreatePhysMat("WallPhys", 0.05f, 0f);
            int n = pts.Count, last = Loop ? n : n - 1;
            int solids0 = wallSolidCount;
            float solidM0 = wallSolidM;

            foreach (float side in new[] { -1f, 1f })
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                float dist = 0f;

                var wallRoot = new GameObject(side < 0 ? "WallL" : "WallR");
                wallRoot.transform.SetParent(parent, false);

                for (int i = 0; i <= last; i++)
                {
                    int idx = Loop ? i % n : i;
                    Vector3 right = RightAt(pts, idx);
                    Vector3 basePos = pts[idx] + right * (WallOffset * side);
                    int v = verts.Count;
                    verts.Add(basePos);
                    verts.Add(basePos + Vector3.up * WallHeight);
                    uvs.Add(new Vector2(dist / 8f, 0f));
                    uvs.Add(new Vector2(dist / 8f, 1f));
                    dist += Spacing;

                    // The way in. A circuit ringed by an unbroken barrier is a
                    // circuit whose gas station can be photographed and never
                    // reached — which is exactly what it was. The vertices stay
                    // (so the UV run does not restart and the two ends of the
                    // opening line up) and only the FACES and the colliders are
                    // dropped, on the forecourt's side only.
                    bool gap = side == padSide && InWallGap(idx, n);

                    if (i < last && !gap)
                    {
                        // Single-sided, facing the road. Emitting the quad twice
                        // with opposite winding to fake two-sidedness makes
                        // RecalculateNormals sum each face normal with its own
                        // negation, so every vertex normal comes out exactly
                        // zero and the barrier renders with ambient light only.
                        //
                        // "Facing the road" is two different windings, because
                        // the road is on the other side of the wall on the
                        // other side of the track. One winding for both meant
                        // the LEFT-HAND BARRIER OF EVERY CIRCUIT faced out over
                        // the scenery and was invisible from the car — you
                        // looked straight through it to the ground beyond, and
                        // only its collider stopped you. The stage walls have
                        // always branched here (BuildOneStageWall); the
                        // circuits' never did.
                        if (side < 0f) tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                        else tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                    }
                }

                // THE COLLIDER IS ONE SOLID PER RUN OF WALL, not a box per chord.
                //
                // It was a box per drawn segment, each overlapping the next by
                // WallCollOverlap so no hairline seam showed between them. The
                // overlap did not remove the seam, it moved it: box k+1's END
                // FACE is a real face, standing in (or, on the outside of a bend,
                // up to 7 cm proud of) the very plane a car scraping the wall
                // slides along. PhysX only suppresses contacts on an edge that is
                // internal to ONE triangle mesh; between two separate colliders
                // every face is real. So a car sliding down the barrier met that
                // end face with a contact whose normal pointed straight back down
                // the road, and lost its speed in one step — WallScrapeAudit
                // measured 136 to 20 km/h in 1/60 s, every 36 m of the quarter
                // mile, and 13 dead stops round City Circuit (the stage walls,
                // bridges included, had the same chain: BuildOneStageWall).
                // Reported as "invisible barriers while scraping walls that
                // look smooth".
                //
                // One closed, concave MeshCollider per run (BuildWallSolid) has no
                // end faces except the run's own two ends, and its traffic face is
                // ONE surface: the edges between its quads are internal edges the
                // cooker knows the neighbours of, so a car sliding along it meets
                // the face (or the bend between two faces), never a face turned
                // back down the road. It keeps everything the boxes were for:
                // the face passes through exactly where their inner faces did at
                // every station (the drawn line, so the contact surface is the
                // quad the player sees), it is WallCollThick deep grown only
                // OUTWARD so a fast car has real depth to catch against, and it
                // reaches DOWN below the wall it is drawn as. The ground under a
                // wall is not the waypoint plane:
                // the corridor sink puts it 0.1-0.3 m lower everywhere, and at a
                // bridge abutment the coarse lattice smears the gorge dig under
                // the wall line — 1.25-2.28 m of window under the old box on
                // Ridge Pass, taller than the car. Each ring's footing is the
                // lowest of the two chords either side of it (the chord rule
                // below, unchanged), so nothing a car can pass under opens up;
                // not where the deck lies under the whole chord, where the deck
                // is the floor and "the ground" is the gorge floor.
                //
                // Runs: the forecourt opening splits the pad side; everything
                // else is one run. On a circuit the run WRAPS across waypoint 0 —
                // cut there it would put two end caps back to back in the middle
                // of a straight, which is the very fault this replaces — and a
                // circuit with no opening on this side is a closed ring with no
                // caps at all.
                float ChordBottom(int c)
                {
                    int nxt = Loop ? (c + 1) % n : Mathf.Min(c + 1, n - 1);
                    Vector3 outw = RightAt(pts, c) * side;
                    Vector3 a = pts[c] + RightAt(pts, c) * (WallOffset * side);
                    Vector3 b = pts[nxt] + RightAt(pts, nxt) * (WallOffset * side);
                    float planeY = (a.y + b.y) * 0.5f;
                    float bottomY = planeY - WallCollDepthM;
                    if (NearBridgeSpan(c, WallSpanReach) &&
                        !(DeckCoversStation(c) && DeckCoversStation(nxt)))
                        bottomY = Mathf.Min(bottomY, WallFootLatticeMin(a, b, outw) - WallCollUnderGroundM);
                    return bottomY;
                }
                bool ChordGap(int c) => side == padSide && InWallGap(c, n);

                int chords = Loop ? n : n - 1;
                var runs = new List<(int first, int count)>();
                bool closed = false;
                int scan0 = 0;
                if (Loop)
                {
                    scan0 = -1;
                    for (int c = 0; c < n; c++) if (ChordGap(c)) { scan0 = c; break; }
                    if (scan0 < 0) { closed = true; runs.Add((0, n)); }
                    else scan0 = (scan0 + 1) % n;
                }
                for (int k = 0; !closed && k < chords; )
                {
                    int c = Loop ? (scan0 + k) % n : k;
                    if (ChordGap(c)) { k++; continue; }
                    int len = 1;
                    while (k + len < chords && !ChordGap(Loop ? (scan0 + k + len) % n : k + len)) len++;
                    runs.Add((c, len));
                    k += len;
                }

                for (int r = 0; r < runs.Count; r++)
                {
                    var (first, count) = runs[r];
                    int rings = closed ? n : count + 1;
                    var faceBottom = new List<Vector3>(rings);
                    var faceTop = new List<Vector3>(rings);
                    var outward = new List<Vector3>(rings);
                    for (int j = 0; j < rings; j++)
                    {
                        int st = Loop ? (first + j) % n : first + j;
                        Vector3 right = RightAt(pts, st);
                        Vector3 face = pts[st] + right * (WallOffset * side);
                        // The chords either side of this ring that belong to the run.
                        float bottomY = float.MaxValue;
                        if (closed || j > 0) bottomY = Mathf.Min(bottomY, ChordBottom(Loop ? (st - 1 + n) % n : st - 1));
                        if (closed || j < count) bottomY = Mathf.Min(bottomY, ChordBottom(st));
                        faceBottom.Add(new Vector3(face.x, bottomY, face.z));
                        faceTop.Add(new Vector3(face.x, face.y + WallHeight, face.z));
                        outward.Add(right * side);
                    }
                    string meshName = (side < 0 ? "CollWallL" : "CollWallR") + (runs.Count > 1 ? r.ToString() : "");
                    BuildWallSolid("Wall", wallRoot.transform, faceBottom, faceTop, outward, WallCollThick,
                                   closed, physMat, meshName);
                }

                var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
                SaveMesh(mesh, side < 0 ? "WallMeshL" : "WallMeshR");
                var meshGO = new GameObject("WallMesh");
                meshGO.transform.SetParent(wallRoot.transform, false);
                meshGO.AddComponent<MeshFilter>().sharedMesh = mesh;
                meshGO.AddComponent<MeshRenderer>().sharedMaterial = wallMat;
                meshGO.isStatic = true;
            }
            Log($"Barriers: {wallSolidCount - solids0} wall solid(s) covering {wallSolidM - solidM0:0} m of " +
                "barrier face, one closed MeshCollider per run (no per-chord boxes, no seams).");
        }

        /// <summary>Wall solids built, and the plan length of their traffic
        /// faces, since the builder's domain loaded — the build log reports the
        /// difference over each pass.</summary>
        static int wallSolidCount;
        static float wallSolidM;

        /// <summary>Rings of a wall solid closer together than this in plan
        /// are one ring: the quad between them has no area worth cooking, and
        /// a zero-length one is a sliver PhysX's mesh cleaning deletes, which
        /// leaves a hole in a solid that is supposed to be closed.</summary>
        const float WallSolidMinRingM = 0.01f;
        /// <summary>How far an open wall solid runs on past its end rings —
        /// see BuildWallSolid.</summary>
        const float WallSolidEndReachM = 0.1f;

        /// <summary>
        /// ONE BARRIER RUN AS ONE SOLID: a closed, concave MeshCollider in
        /// place of the chain of per-chord BoxColliders every wall used to be.
        ///
        /// Why: in a chain, box k+1's end face is a real face lying in (or a few
        /// centimetres proud of) the plane a car scraping the barrier slides
        /// along, and PhysX answers it with a contact whose normal points back
        /// down the road — full speed to a dead stop on a wall that looks
        /// smooth. PhysX suppresses such contacts on edges INTERNAL to one
        /// triangle mesh (the cooker flags which edges are real) and never
        /// between two colliders, so the only cure is for the whole run to be
        /// one mesh.
        ///
        /// Per ring k: the traffic face runs from <paramref name="faceBottom"/>
        /// up to <paramref name="faceTop"/> (same x/z; the caller's contact
        /// line), and the solid is <paramref name="thick"/> deep along
        /// <paramref name="outward"/> (horizontal, away from the road), so it
        /// has four corners — face foot, face top, back top, back foot — and
        /// between rings four sides: the traffic face, the top, the back and
        /// the bottom. End caps close ring 0 and the last ring; a
        /// <paramref name="closedLoop"/> joins the last ring to the first and
        /// has no caps and no seam anywhere. Every triangle is wound to face
        /// OUT of the solid (QuadFacingSkipFlat, by the whole quad's normal):
        /// sweep CCD against a triangle mesh does not see a back face, and a
        /// single inside-out quad on the traffic face would be a hole a fast
        /// car goes through.
        ///
        /// Layer SolidLayer, static, no renderer — the drawing is the caller's
        /// own mesh. The collider mesh is saved uncompressed (meshName must not
        /// match a CompressibleMesh key): a collider is exact geometry, and 16
        /// bits over a 2 km barrier is a centimetre staircase on the very face
        /// this exists to make smooth. Returns null when fewer than two rings
        /// (three on a loop) survive.
        /// </summary>
        static GameObject BuildWallSolid(string name, Transform parent, List<Vector3> faceBottom, List<Vector3> faceTop,
                                         List<Vector3> outward, float thick, bool closedLoop, PhysicsMaterial phys,
                                         string meshName)
        {
            var fb = new List<Vector3>(faceBottom.Count);
            var ft = new List<Vector3>(faceBottom.Count);
            var ow = new List<Vector3>(faceBottom.Count);
            const float MinSq = WallSolidMinRingM * WallSolidMinRingM;
            for (int k = 0; k < faceBottom.Count; k++)
            {
                Vector3 o = outward[k]; o.y = 0f;
                o = o.sqrMagnitude > 1e-8f ? o.normalized : (ow.Count > 0 ? ow[ow.Count - 1] : Vector3.zero);
                if (fb.Count > 0)
                {
                    // A duplicate ring: its footing folds into the ring it
                    // duplicates, so dropping it can never open a window.
                    int j = fb.Count - 1;
                    Vector3 d = faceBottom[k] - fb[j]; d.y = 0f;
                    if (d.sqrMagnitude < MinSq)
                    {
                        fb[j] = new Vector3(fb[j].x, Mathf.Min(fb[j].y, faceBottom[k].y), fb[j].z);
                        continue;
                    }
                }
                fb.Add(faceBottom[k]); ft.Add(faceTop[k]); ow.Add(o);
            }
            if (closedLoop && fb.Count > 1)
            {
                int j = fb.Count - 1;
                Vector3 d = fb[j] - fb[0]; d.y = 0f;
                if (d.sqrMagnitude < MinSq)
                {
                    fb[0] = new Vector3(fb[0].x, Mathf.Min(fb[0].y, fb[j].y), fb[0].z);
                    fb.RemoveAt(j); ft.RemoveAt(j); ow.RemoveAt(j);
                }
            }
            int R = fb.Count;
            if (R < 2 || (closedLoop && R < 3)) return null;
            // Only a leading ring can still have no outward (the loop above
            // carries the previous one forward): borrow the first real one.
            int firstOut = ow.FindIndex(o => o.sqrMagnitude > 0.5f);
            if (firstOut < 0) return null;
            for (int k = 0; k < firstOut; k++) ow[k] = ow[firstOut];

            // An open run reaches WallSolidEndReachM past its end rings, on
            // along its end chords at the end rings' own section. Ending flush
            // ON the end station left that station's own probes grazing the
            // cap, edge-on: the edge audit's barrier ray at a drag strip's wp 0
            // flew along the cap plane, found no wall, and walked on over the
            // solid's top to report a 2.7 m face and fall. The boxes this
            // replaces overhung 0.3 m; ten centimetres puts the station inside
            // the face and nobody can see it.
            if (!closedLoop)
            {
                Vector3 d0 = fb[0] - fb[1]; d0.y = 0f;
                Vector3 d1 = fb[R - 1] - fb[R - 2]; d1.y = 0f;
                d0 = d0.normalized * WallSolidEndReachM;
                d1 = d1.normalized * WallSolidEndReachM;
                fb.Insert(0, fb[0] + d0); ft.Insert(0, ft[0] + d0); ow.Insert(0, ow[0]);
                int e = fb.Count - 1;
                fb.Add(fb[e] + d1); ft.Add(ft[e] + d1); ow.Add(ow[e]);
                R = fb.Count;
            }

            // Four corners per ring, shared by the faces that meet there (a
            // collider has no seams to keep for UVs).
            var verts = new List<Vector3>(R * 4);
            for (int k = 0; k < R; k++)
            {
                Vector3 top = ft[k];
                top.y = Mathf.Max(top.y, fb[k].y + WallSolidMinRingM);
                Vector3 back = ow[k] * thick;
                verts.Add(fb[k]);            // 0 face foot
                verts.Add(top);              // 1 face top
                verts.Add(top + back);       // 2 back top
                verts.Add(fb[k] + back);     // 3 back foot
            }

            var tris = new List<int>(R * 24 + 12);
            int segs = closedLoop ? R : R - 1;
            float lengthM = 0f;
            for (int k = 0; k < segs; k++)
            {
                int k2 = (k + 1) % R;
                int a = 4 * k, b = 4 * k2;
                Vector3 step = fb[k2] - fb[k]; step.y = 0f;
                lengthM += step.magnitude;
                Vector3 away = ow[k] + ow[k2];
                QuadFacingSkipFlat(verts, tris, a + 0, a + 1, b + 1, b + 0, -away);          // traffic face
                QuadFacingSkipFlat(verts, tris, a + 1, a + 2, b + 2, b + 1, Vector3.up);     // top
                QuadFacingSkipFlat(verts, tris, a + 3, a + 2, b + 2, b + 3, away);           // back
                QuadFacingSkipFlat(verts, tris, a + 0, a + 3, b + 3, b + 0, Vector3.down);   // bottom
            }
            if (!closedLoop)
            {
                // The run's two ends, each facing on along the run.
                Vector3 run0 = fb[1] - fb[0]; run0.y = 0f;
                Vector3 run1 = fb[R - 1] - fb[R - 2]; run1.y = 0f;
                int e = 4 * (R - 1);
                QuadFacingSkipFlat(verts, tris, 0, 1, 2, 3, -run0);
                QuadFacingSkipFlat(verts, tris, e, e + 1, e + 2, e + 3, run1);
            }

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts.ToArray(), triangles = tris.ToArray(),
            };
            SaveMesh(mesh, meshName);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = SolidLayer;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = false;
            mc.sharedMesh = mesh;
            mc.sharedMaterial = phys;
            go.isStatic = true;
            wallSolidCount++;
            wallSolidM += lengthM;
            return go;
        }

        /// <summary>How far below the waypoint plane every circuit wall
        /// collider reaches: under the corridor sink (0.1 m), the run-off's
        /// toe and skirt, and the lattice's ordinary smear, with the car's
        /// 1 m box still unable to fit beneath.</summary>
        const float WallCollDepthM = 0.6f;
        /// <summary>Near a span, how far under the LOWEST ground beneath the
        /// chord it reaches instead — the abutment, where the lattice falls
        /// toward the gorge under the wall line.</summary>
        const float WallCollUnderGroundM = 0.5f;
        /// <summary>Stations either side of a span that count as its
        /// abutment for the wall's depth: the one station the deck is carried
        /// onto the approach (DeckCoversStation) and one more.</summary>
        const int WallSpanReach = 2;

        /// <summary>Is the wall segment from station <paramref name="idx"/> to
        /// the next within <paramref name="reach"/> stations of a bridge
        /// span?</summary>
        static bool NearBridgeSpan(int idx, int reach)
        {
            if (bridgeBlend == null || track == null || track.drag) return false;
            int n = bridgeBlend.Length;
            for (int o = -reach; o <= reach + 1; o++)
            {
                int j = Loop ? ((idx + o) % n + n) % n : idx + o;
                if (j < 0 || j >= n) continue;
                if (bridgeBlend[j] > DeckBlendMin) return true;
            }
            return false;
        }

        /// <summary>Lowest ground MESH under one chord of a wall solid's
        /// footprint: along the chord (and WallCollOverlap past each end),
        /// across the solid's whole thickness.</summary>
        static float WallFootLatticeMin(Vector3 a, Vector3 b, Vector3 outw)
        {
            Vector3 along = b - a;
            along.y = 0f;
            float len = along.magnitude;
            Vector3 dir = len > 1e-4f ? along / len : Vector3.zero;
            float lo = float.MaxValue;
            for (int k = 0; k <= 4; k++)
            {
                Vector3 p = a + dir * Mathf.Lerp(-WallCollOverlap * 0.5f, len + WallCollOverlap * 0.5f, k / 4f);
                for (int q = 0; q <= 2; q++)
                {
                    Vector3 at = p + outw * (WallCollThick * q * 0.5f);
                    lo = Mathf.Min(lo, ShoulderLatticeY(at.x, at.z));
                }
            }
            return lo;
        }

        /// <summary>
        /// Friction on the car's own BODY collider, and through the Minimum
        /// combine the CEILING on friction against anything it touches.
        ///
        /// The wheels are raycast, not colliders, so this number never touches
        /// grip — it is purely how the shell behaves against scenery, and the
        /// scenery is supposed to let go. 0.15 was enough to drag on a cut bank
        /// while CollisionResponder's scrub was doing the rest of the damage;
        /// 0.06 is a rail. Speed loss belongs in the responder, where it can be
        /// angle-aware, and not in a friction coefficient that cannot tell a
        /// three-degree brush from a thirty-degree scrape.
        /// </summary>
        public const float CarSlideFriction = 0.06f;

        /// <summary>Ground and road meshes the car's BODY can touch. Same
        /// reasoning as CarSlideFriction: nothing here is a tyre.</summary>
        static PhysicsMaterial SlidePhys() => GetOrCreatePhysMat("SlidePhys", 0.04f, 0f);

        static PhysicsMaterial GetOrCreatePhysMat(string name, float friction, float bounce)
        {
            string p = GenDir + "/" + name + ".asset";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(p);
            if (m == null)
            {
                m = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(m, p);
            }
            m.dynamicFriction = friction;
            m.staticFriction = friction;
            m.bounciness = bounce;
            m.frictionCombine = PhysicsMaterialCombine.Minimum;
            m.bounceCombine = PhysicsMaterialCombine.Minimum;
            return m;
        }

        static void BuildGround(List<Vector3> pts, Transform parent)
        {
            // Sized and centred on the circuit rather than on the city one. The
            // airfield is 660 m across where the city is 370, and a plane fixed
            // at the city's 900 m centred on the city's middle leaves a fast
            // car driving off the edge of the world on the back straight.
            //
            // 380 m of apron past the furthest waypoint: the camera's far plane
            // is 360 and the fog closes well before that, so anything more is
            // vertices nobody will ever see. (CircuitGroundFrame, shared with
            // the lattice solve, so the grid the shoulder toes read is this
            // one.)
            CircuitGroundFrame(pts, out float ox, out float oz, out float size);
            float tile = theme.groundTile;
            // 45 cells meant 20 m triangles. Affine UVs distort in proportion to
            // triangle size and depth contrast, which is worst on ground right
            // under the camera — measured at ~52 px of texture slip. The ground
            // material also opts out of affine entirely (see MakeMat below);
            // this finer grid is for the per-vertex fog and lighting gradient,
            // and now for the hills as well.
            //
            // GroundCells (144) since the ground started following the road.
            const int cells = GroundCells;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // The heights PrepareCircuitLattice solved when the shoulder was
            // built — the field, lowered wherever a coarse triangle came within
            // the hide margin of the tarmac or the run-off. The field itself
            // only when no shoulder was laid first.
            bool solved = circuitLattice != null && circuitLattice.Length == (cells + 1) * (cells + 1);
            // The mesh is local to a GameObject parked at the circuit's centre,
            // so the height field — which is a function of WORLD position — has
            // to be asked about the world point, not the local one.
            for (int y = 0; y <= cells; y++)
                for (int x = 0; x <= cells; x++)
                {
                    float fx = x / (float)cells - 0.5f, fy = y / (float)cells - 0.5f;
                    float wx = fx * size, wz = fy * size;
                    float h = solved ? circuitLattice[y * (cells + 1) + x] : GroundHeightAt(wx + ox, wz + oz);
                    verts.Add(new Vector3(wx, h, wz));
                    uvs.Add(new Vector2(wx / tile, wz / tile));
                }
            for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    int v = y * (cells + 1) + x;
                    tris.AddRange(new[] { v, v + cells + 1, v + cells + 2, v, v + cells + 2, v + 1 });
                }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            SaveMesh(mesh, "GroundMesh");

            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(ox, 0f, oz);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var groundMat = MakeMat(MeshPrefix + "Ground", theme.ground, affine: 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
            RegisterSeasonalGround(MeshPrefix + "Ground", theme.ground, groundMat, Color.white, "ground");
            // A box no longer describes it. The ground the wheels find off the
            // racing line is the ground you can see, hills and gorge included —
            // a flat plate under a mountain pass would have a car that ran wide
            // driving along thin air at valley height.
            var groundCol = go.AddComponent<MeshCollider>();
            groundCol.sharedMesh = mesh;
            groundCol.sharedMaterial = SlidePhys();
            go.isStatic = true;
        }

        // ------------------------------------------------------------------
        //  Terrain
        // ------------------------------------------------------------------
        /// <summary>
        /// How far either side of the centreline the ground is held dead level
        /// with the road. Six metres past the barrier line, which is what makes
        /// the roadbed safe: every ground vertex inside this band sits at the
        /// road base exactly, so the coarse landscape grid can never push a
        /// corner up through the tarmac however steep the hill gets.
        /// </summary>
        const float CorridorR = 16f;
        /// <summary>Metres the shelf takes to blend out into the landscape. This
        /// IS the embankment: short and the road runs along a wall, long and a
        /// mountain pass reads as a gentle rise.</summary>
        const float CorridorBlend = 48f;
        /// <summary>
        /// How far the shelf sits BELOW the road base, fading out over the same
        /// blend. A real road stands proud of its shoulder, and this buys back
        /// the margin the ground grid spends: the shelf follows the centreline
        /// by PROJECTION, which is exactly linear along a straight and is not
        /// across a 9 m cell in a tight corner on a gradient. Measured at 5 cm
        /// of clearance left under the tarmac on the mountain pass before this
        /// existed, against the 12 cm the ribbon is lifted by.
        /// </summary>
        const float CorridorSink = 0.1f;

        /// <summary>Metres the tarmac ribbon rides above the waypoint plane.
        /// Was written as a bare 0.12 in three places that all had to agree.
        /// </summary>
        internal const float RoadLift = 0.12f;

        /// <summary>
        /// Structural depth of the pavement — surface course over base over
        /// subbase, the way a DOT section is built.
        ///
        /// A road with no thickness is a decal, and the ground only has to
        /// disagree with it by a millimetre to be ON it. This is the margin
        /// the coarse ground grid gets to spend before anything shows: the
        /// subgrade is dug to the bottom of the slab under the paved
        /// footprint, so there is 45 cm between the tarmac and the land rather
        /// than the 22 cm the lift and the shelf sink used to buy between
        /// them.
        ///
        /// It is NOT something to see. It used to be — the slab's cut face was
        /// drawn and collided as the road edge, and that face was the "thick
        /// road sticking out of the ground" and the half-metre lip a car could
        /// not climb back over. The slab now stays under the paved width, and
        /// the shoulder surface (BuildShoulders) is what meets the land.
        /// </summary>
        internal const float RoadSlabDepth = 0.45f;

        /// <summary>How far past the kerb strip the full-depth dig continues
        /// before RoadbedRamp brings it back up: the footprint of the slab's
        /// edge, under the shoulder's bevel and first quarter-metre. Nothing is
        /// drawn at it any more; it only shapes the ground under the
        /// shoulder.</summary>
        internal const float RoadSlabBatter = 0.25f;

        /// <summary>How far outside the slab the dig ramps back up to the
        /// shoulder shelf. Short, and entirely inside CorridorR, so every
        /// height outside the roadbed is exactly what it was before this
        /// existed — barriers, scenery and the forecourt pad have not
        /// moved.</summary>
        const float RoadbedRamp = 2.5f;

        /// <summary>Outer edge of the dug footprint: tarmac, kerb strip and
        /// the slab's edge.</summary>
        static float RoadbedToe => RoadWidth * 0.5f + KerbWidth + RoadSlabBatter;

        /// <summary>
        /// How far below the road datum the shelf sits, as a function of
        /// distance from the centreline: the roadbed dig under the pavement
        /// itself, ramping back out to the shoulder shelf beside it.
        ///
        /// Returns exactly <see cref="CorridorSink"/> from RoadbedToe +
        /// RoadbedRamp outward, which is well inside CorridorR — so this
        /// changes the ground UNDER the road and nowhere else.
        /// </summary>
        static float RoadbedSinkAt(float d) =>
            Mathf.Lerp(RoadSlabDepth - RoadLift, CorridorSink,
                       Mathf.SmoothStep(0f, 1f,
                           Mathf.InverseLerp(RoadbedToe, RoadbedToe + RoadbedRamp, d)));

        static List<Vector3> terrainPts;
        /// <summary>Height of the GROUND at each waypoint — the road height,
        /// except under a bridge where it drops into the gorge. The road itself
        /// stays where the elevation spline put it; this is the only place the
        /// two part company.</summary>
        static float[] terrainGroundY;
        static float terrainRelief;
        static float terrainSeed;
        /// <summary>How much bridge there is at each waypoint. Computed once
        /// here because four different scatter passes need to ask it, and
        /// asking BridgeBlend per candidate site would recompute the whole span
        /// table a few hundred times.</summary>
        static float[] bridgeBlend;
        /// <summary>Height of the land far enough from the circuit that the
        /// circuit no longer has anything to say about it. The mean of the
        /// track, so a mountain pass sits IN a plateau rather than on a
        /// pedestal above a plain at sea level.</summary>
        static float terrainBaseY;

        /// <summary>
        /// Prepare the height field the ground mesh, the scenery and the bridge
        /// piers all read.
        ///
        /// It is derived FROM the road rather than the other way round. Draping
        /// a road over a generated height field gives you gradients nobody
        /// chose and crests in the middle of hairpins; grading the land to a
        /// road somebody drew is how the real ones are built, and it means the
        /// tarmac and the ground under it can never disagree.
        /// </summary>
        static void BuildTerrainField(List<Vector3> pts)
        {
            terrainPts = pts;
            // The previous venue's shoulder and solved lattice describe
            // another road; BuildShoulders lays this one's.
            shoulderProfiles = null;
            circuitLattice = null;
            terrainRelief = theme.relief;
            // Deterministic per circuit: the same track has to bake to the same
            // hills every time or the scenery walks between builds.
            terrainSeed = Mathf.Abs(track.id.GetHashCode() % 997) * 0.37f;

            int n = pts.Count;
            terrainGroundY = new float[n];
            bridgeBlend = new float[n];
            float lap = Mathf.Max(track.LengthM, 1f);
            float deepest = 0f;
            for (int i = 0; i < n; i++)
            {
                float blend = track.drag ? 0f
                    : TrackCatalog.BridgeBlend(track, Mathf.Repeat(i * Spacing, lap));
                bridgeBlend[i] = blend;
                float drop = blend * track.bridgeDepth;
                terrainGroundY[i] = pts[i].y - drop;
                if (drop > deepest) deepest = drop;
            }
            double mean = 0.0;
            for (int i = 0; i < n; i++) mean += terrainGroundY[i];
            terrainBaseY = n > 0 ? (float)(mean / n) : 0f;
            if (deepest > 0.01f)
                Log($"Terrain: {track.bridges.Length} bridge span(s), gorge floor " +
                    $"{deepest:0.0} m below the deck at its deepest.");
        }

        /// <summary>
        /// Ground height anywhere in the world.
        ///
        /// Three terms, in order of how close you are to the road: a dead-flat
        /// shelf out to <see cref="CorridorR"/>, taken from the centreline by
        /// PROJECTION rather than from the nearest waypoint (waypoints are 4 m
        /// apart and the ground grid is nearer 9, so snapping to one would step
        /// the shelf in a way the road does not); a Gaussian-weighted mean of
        /// the whole circuit past that, which is what makes the land between
        /// two arms of a loop meet itself smoothly instead of at a ridge; and
        /// the relief noise, faded in over the same blend so no bump can ever
        /// appear inside the corridor.
        /// </summary>
        static float GroundHeightAt(float x, float z)
        {
            // The stage's ground truth is the real DEM (with the same corridor
            // pinning this function does), so every caller — piers, footings,
            // scatter, audits — reads the mountain without knowing it is one.
            if (stageDemLoaded) return StageGroundHeightAt(x, z);

            var pts = terrainPts;
            if (pts == null || pts.Count == 0) return 0f;
            int n = pts.Count;

            int best = 0;
            float bestD2 = float.MaxValue;
            // A virtual waypoint at the base height with a tiny weight, present
            // everywhere. Without it the weighted mean is a ratio of sums that
            // both underflow at around 330 m — past which the field would snap
            // from "whatever the track is doing over there" to zero, ringing
            // every circuit with a cliff as tall as its highest point. Beyond
            // the fog, but only just, and only until somebody stands on a
            // summit and looks out.
            const float BackWeight = 1e-3f;
            double sw = BackWeight, sy = BackWeight * terrainBaseY;
            const float Kernel = 75f;
            const float K2 = Kernel * Kernel;
            for (int i = 0; i < n; i++)
            {
                float dx = pts[i].x - x, dz = pts[i].z - z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; best = i; }
                float w = Mathf.Exp(-d2 / K2);
                sw += w; sy += w * terrainGroundY[i];
            }
            float far = (float)(sy / sw);

            // Refine against the two segments touching the nearest waypoint, so
            // both the height and the distance are the polyline's and not a
            // single point's.
            float nearY = terrainGroundY[best];
            float d = Mathf.Sqrt(bestD2);
            for (int o = -1; o <= 0; o++)
            {
                int a = Loop ? ((best + o) % n + n) % n : Mathf.Clamp(best + o, 0, n - 1);
                int b = Loop ? (a + 1) % n : Mathf.Min(a + 1, n - 1);
                if (a == b) continue;
                float ax = pts[a].x, az = pts[a].z;
                float ex = pts[b].x - ax, ez = pts[b].z - az;
                float len2 = ex * ex + ez * ez;
                if (len2 < 1e-6f) continue;
                float t = Mathf.Clamp01(((x - ax) * ex + (z - az) * ez) / len2);
                float px = ax + ex * t, pz = az + ez * t;
                float dd = Mathf.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                if (dd < d)
                {
                    d = dd;
                    nearY = Mathf.Lerp(terrainGroundY[a], terrainGroundY[b], t);
                }
            }

            float blend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(CorridorR, CorridorR + CorridorBlend, d));
            float h = Mathf.Lerp(nearY, far, blend)
                    - RoadbedSinkAt(d) * (1f - blend)
                    + terrainRelief * ReliefNoise(x, z) * blend;

            // The forecourt is graded into whatever the land was doing here.
            // Everything that stands on the ground reads this function, so the
            // pad has to live INSIDE it rather than being a slab laid over the
            // top afterwards — the ground mesh, the building footings and the
            // tree bases would all still be following the hillside.
            if (!padActive) return h;
            PadLocal(x, z, out float along, out float deep);
            float pw = PadWeight(along, deep);
            if (pw <= 0f) return h;
            // The corridor sink survives the pad. It is the ten centimetres
            // that keep the ground mesh from poking up through the tarmac
            // between its own vertices, and the forecourt reaches to the kerb —
            // so cancelling it here would put a coarse 8 m ground triangle
            // exactly level with the road for the length of the fuel stop.
            return Mathf.Lerp(h, PadSurfaceY(along, deep) - RoadbedSinkAt(d) * (1f - blend), pw);
        }

        /// <summary>
        /// Rolling relief, roughly -1..1. Three sine terms rather than Perlin
        /// because it has to be identical in the editor that bakes the mesh and
        /// in any tool that checks it, and Unity does not promise that about
        /// PerlinNoise across versions. The wavelengths are 80 m, 43 m and 25 m,
        /// which at this amplitude reads as land rather than as ripples.
        /// </summary>
        static float ReliefNoise(float x, float z)
        {
            float s = terrainSeed;
            return 0.55f * Mathf.Sin((x + s) * 0.0125f) * Mathf.Cos((z - s) * 0.0104f)
                 + 0.30f * Mathf.Sin((x - z) * 0.0231f + s)
                 + 0.15f * Mathf.Cos((x * 0.6f + z) * 0.0407f - s);
        }

        /// <summary>
        /// Is this waypoint out over a gorge?
        ///
        /// Anything that stands on the GROUND has to skip these, because over a
        /// span the ground is nine to fourteen metres down: a dockside
        /// warehouse placed beside the harbour bridge does not stand beside it,
        /// it stands in the water underneath it, at full height, in shot from
        /// the deck the whole way across.
        ///
        /// The threshold is low on purpose. Half a metre of drop is already
        /// enough to leave a building hanging off the lip of the ravine.
        /// </summary>
        static bool OverGorge(int i) =>
            bridgeBlend != null && i >= 0 && i < bridgeBlend.Length &&
            bridgeBlend[i] * track.bridgeDepth > 0.5f;

        /// <summary>Lowest ground under a footprint, sampled at its corners and
        /// centre. What a building has to be set into: taking the height at the
        /// origin alone leaves the downhill corner of a 20 m block hanging in
        /// the air, which is exactly the fault this was reported as.</summary>
        static float LowestGroundUnder(Vector3 centre, Quaternion rot, float halfX, float halfZ)
        {
            float lo = GroundHeightAt(centre.x, centre.z);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -halfX : halfX;
                float sz = (i & 2) == 0 ? -halfZ : halfZ;
                Vector3 c = centre + rot * new Vector3(sx, 0f, sz);
                lo = Mathf.Min(lo, GroundHeightAt(c.x, c.z));
            }
            return lo;
        }

        // ------------------------------------------------------------------
        //  Bridges
        // ------------------------------------------------------------------
        /// <summary>Deck half-width. Wider than the barrier line so the parapet
        /// stands ON the deck instead of over its edge, which is the difference
        /// between a bridge and a road with nothing under it. On the stage the
        /// barrier hugs the shoulder, so the deck does too — a 23 m deck under
        /// a 9.5 m parkway would read as an aircraft carrier.</summary>
        static float DeckHalfWidth => track != null && track.stage
            ? StageWallOffset + 1.2f : WallOffset + 1.4f;

        /// <summary>Metres of structure per concrete texture repeat. One number
        /// for both axes, or the noise smears along whichever one is longer.
        /// </summary>
        const float ConcreteTile = 4f;
        /// <summary>Depth of the deck box under the tarmac.</summary>
        const float DeckThick = 1.3f;

        /// <summary>Bridge blend above which a station is part of a span —
        /// the deck's, the concrete ribbon's and the shoulder's one test.</summary>
        const float DeckBlendMin = 0.001f;
        /// <summary>
        /// How far the deck top sits under the tarmac: two centimetres, inside
        /// RoadsideRules.EdgeDropM, so what a car rolls off the kerb strip onto
        /// is the owner's inch and not a step.
        ///
        /// It was four (+0.08 over the plane under a +0.12 ribbon), "so the
        /// two never fight for the same pixels" — which two centimetres also
        /// does at every distance the fog lets you see a deck from (the depth
        /// buffer resolves about 2 mm at 100 m against the stage's far clip).
        /// </summary>
        internal const float DeckTopBelowTarmac = 0.02f;
        /// <summary>The deck top over the waypoint plane.</summary>
        internal const float DeckTopLift = RoadLift - DeckTopBelowTarmac;
        /// <summary>How far under the deck top a shoulder surface that runs
        /// over a span finishes, so it never lies in the concrete's own
        /// plane.</summary>
        const float DeckHideM = 0.03f;

        /// <summary>
        /// Does the deck cover this station? Every station of a span, and the
        /// one station either side of it: BuildBridges carries each deck one
        /// station onto solid ground, so its end cap stands on the approach
        /// rather than over the coarse ground's abutment pit.
        /// </summary>
        internal static bool DeckCoversStation(int idx)
        {
            if (bridgeBlend == null || track == null || track.drag) return false;
            int n = bridgeBlend.Length;
            if (idx < 0 || idx >= n) return false;
            if (bridgeBlend[idx] > DeckBlendMin) return true;
            int a = Loop ? (idx - 1 + n) % n : idx - 1;
            int b = Loop ? (idx + 1) % n : idx + 1;
            return (a >= 0 && bridgeBlend[a] > DeckBlendMin) || (b < n && bridgeBlend[b] > DeckBlendMin);
        }
        /// <summary>Metres between piers. Real short-span viaducts sit around
        /// 25-30 m; closer than that and the gorge fills up with columns.</summary>
        const float PierEvery = 26f;
        const float PierHalf = 1.3f;

        /// <summary>
        /// Deck, fascias and piers for every elevated span.
        ///
        /// The road ribbon itself is untouched — it was already at the right
        /// height, because the elevation spline does not know or care whether
        /// there is ground under it. What a bridge adds is everything you can
        /// only see BECAUSE the ground has gone: a top surface out to the
        /// parapet, a soffit under it, two fascia beams down the sides, and the
        /// piers holding the whole thing over the gorge.
        ///
        /// Built from the same BridgeBlend the terrain carve reads, so the deck
        /// and the hole in the ground are the same span. Two thresholds would
        /// drift, and the failure — a deck ending ten metres short of the
        /// abutment — is invisible from the driving line and obvious from
        /// anywhere else.
        ///
        /// Then carried ONE STATION further at each end (DeckCoversStation).
        /// The span's first station already has some blend (0.057 on Ridge
        /// Pass: 0.8 m of dig on the centreline), and the 7-10 m ground cells
        /// smear that dig into the verge a station before the deck began: an
        /// abutment pit 0.5-2.3 m deep beside the wall, a half-metre toe lip
        /// on one side of it and a deck end cap up to 2.1 m tall on the other.
        /// Extended, the end cap stands on ground the dig has not reached.
        /// </summary>
        static void BuildBridges(List<Vector3> pts, Transform parent)
        {
            if (track.drag || track.bridges == null || track.bridges.Length == 0) return;

            int n = pts.Count;
            float lap = Mathf.Max(track.LengthM, 1f);
            var blend = new float[n];
            for (int i = 0; i < n; i++)
                blend[i] = TrackCatalog.BridgeBlend(track, Mathf.Repeat(i * Spacing, lap));

            // CONCRETE, not the barrier texture. A deck and its piers are the
            // one structure on a circuit that is unambiguously poured — they
            // were wearing whatever the venue's walls are made of, so a viaduct
            // over a gorge came out looking like a very long fence, and the
            // bridges in the city (which have always been concrete) and the
            // bridges on the circuits did not read as the same kind of thing.
            string concrete = EnsureConcreteTex();
            // The deck is wet, the piers are not: one deck mesh holds the top,
            // the soffit and both fascias, and PSX/Lit's up-facing gate is what
            // keeps all but the top dry — the mask only says "concrete road".
            var deckMat = MakeMat(MeshPrefix + "Deck", concrete, affine: 0f, wet: WetAsphalt);
            var pierMat = MakeMat(MeshPrefix + "Pier", concrete, affine: 0f);
            var physMat = GetOrCreatePhysMat("DeckPhys", 0.8f, 0f);

            var root = new GameObject("Bridges");
            root.transform.SetParent(parent, false);

            // A span is a maximal run of waypoints with any bridge in them.
            // Walking the blend array rather than the metre ranges is what lets
            // a span cross the start line: begin the scan at the first waypoint
            // that is CLEAR and go round from there, and a bridge sitting on
            // waypoint 0 is one run rather than two half-decks with an abutment
            // in the middle of it.
            int origin = 0;
            while (origin < n && blend[origin] > DeckBlendMin) origin++;
            if (origin >= n) origin = 0;     // the whole lap is elevated

            int spanNo = 0, piers = 0;
            var jointIdx = new List<int>();
            for (int k = 0; k < n; )
            {
                if (blend[(origin + k) % n] <= DeckBlendMin) { k++; continue; }
                int len = 1;
                while (k + len < n && blend[(origin + k + len) % n] > DeckBlendMin) len++;

                int from = (origin + k) % n;
                // The deck one station longer at each end; the piers and the
                // joints stay on the span itself. A strip's ends clamp, and a
                // span that is the whole lap has no ends to carry.
                int deckFrom = from, deckLen = len;
                if (len < n)
                {
                    if (Loop) { deckFrom = (from - 1 + n) % n; deckLen = Mathf.Min(n, len + 2); }
                    else if (from + len <= n)
                    {
                        deckFrom = Mathf.Max(0, from - 1);
                        deckLen = Mathf.Min(n - 1, from + len) - deckFrom + 1;
                    }
                }
                BuildOneDeck(pts, deckFrom, deckLen, root.transform, deckMat, physMat, ++spanNo);
                piers += BuildPiers(pts, from, len, root.transform, pierMat);
                CollectJoints(from, len, n, jointIdx);
                k += len;
            }

            if (jointIdx.Count > 0)
            {
                BuildJointBands(pts, jointIdx, root.transform);
                jointIdx.Sort();
                // On the Track object, beside the TrackPath it reads — the
                // component needs the waypoint list, and `parent` IS that
                // object (BuildBridges is handed pathGO.transform).
                var jc = parent.gameObject.AddComponent<PSXRacing.BridgeJoints>();
                jc.path = parent.GetComponent<TrackPath>();
                jc.jointIndex = jointIdx.ToArray();
            }

            Log($"Built {spanNo} bridge deck(s), {piers} piers and {jointIdx.Count} expansion joints.");
        }

        /// <summary>
        /// Where the expansion joints go: over the piers, because that is where
        /// a real span ends and the next begins. Derived from the SAME
        /// <see cref="PierEvery"/> the columns are placed on rather than a
        /// spacing of their own — a joint band between two piers would be a
        /// gap in a beam, which is the one place a bridge does not have one.
        ///
        /// The abutments get one each too. They are the joints you feel most:
        /// the step from solid ground onto a deck that moves.
        /// </summary>
        static void CollectJoints(int from, int stations, int n, List<int> into)
        {
            float runM = stations * Spacing;
            int count = Mathf.Max(1, Mathf.RoundToInt(runM / PierEvery));
            for (int p = 0; p <= count; p++)
            {
                int k = Mathf.RoundToInt(p * (stations - 1f) / count);
                int i = (from + k) % n;
                if (!into.Contains(i)) into.Add(i);
            }
        }

        /// <summary>Metal band across the deck at each joint. One merged mesh —
        /// 50-odd quads on a 1.4 km bridge, and fifty GameObjects for something
        /// you drive over at 200 km/h would be fifty draw calls for four
        /// pixels each.</summary>
        static void BuildJointBands(List<Vector3> pts, List<int> joints, Transform parent)
        {
            int n = pts.Count;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // Half the joint's width along the road. 0.34 m reads as a band at
            // this texture resolution without becoming a stripe.
            const float halfLen = 0.34f;
            float hw = DeckHalfWidth - 0.15f;

            foreach (int i in joints)
            {
                Vector3 right = RightAt(pts, i);
                Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
                // ABOVE the road ribbon (+0.12) rather than level with it: two
                // coplanar surfaces z-fight, and a joint that flickers is worse
                // than no joint at all. 6 mm is under the suspension's notice.
                Vector3 c = pts[i] + Vector3.up * 0.126f;
                int b = verts.Count;
                verts.Add(c - right * hw - fwd * halfLen); uvs.Add(new Vector2(0f, 0f));
                verts.Add(c + right * hw - fwd * halfLen); uvs.Add(new Vector2(1f, 0f));
                verts.Add(c + right * hw + fwd * halfLen); uvs.Add(new Vector2(1f, 1f));
                verts.Add(c - right * hw + fwd * halfLen); uvs.Add(new Vector2(0f, 1f));
                tris.AddRange(new[] { b, b + 3, b + 2, b, b + 2, b + 1 });
            }

            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            SaveMesh(mesh, "BridgeJoints");

            var go = new GameObject("BridgeJoints");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial =
                MakeMat(MeshPrefix + "Joint", JointTexPath, affine: 0f, wet: WetAsphalt);
            go.isStatic = true;
            // No collider, deliberately: the jolt comes from BridgeJoints by
            // distance, and a 6 mm lip in the suspension's path would be a
            // random extra depending on where the raycast happened to land.
        }

        static void BuildOneDeck(List<Vector3> pts, int from, int stations,
                                 Transform parent, Material mat, PhysicsMaterial phys, int no)
        {
            int n = pts.Count;
            float hw = DeckHalfWidth;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float dist = 0f;

            // Four ribbons sharing one mesh: deck top, soffit, and a fascia
            // down each side. Emitted station by station so they stay in step.
            var top = new int[stations * 2];
            var bot = new int[stations * 2];

            for (int k = 0; k < stations; k++)
            {
                int i = (from + k) % n;
                Vector3 right = RightAt(pts, i);
                // The deck top sits just UNDER the road ribbon and the kerb
                // strip (DeckTopBelowTarmac), so the two never fight for the
                // same pixels and a car leaving the strip drops an inch.
                Vector3 c = pts[i] + Vector3.up * DeckTopLift;
                Vector3 under = pts[i] + Vector3.up * (DeckTopLift - DeckThick);

                // Concrete tiles by the METRE in both directions. The old UVs
                // ran 0..1 across the deck whatever its width, which on a 23 m
                // deck stretched one texture repeat over more than twice the
                // distance it covered along the span — a visible smear on the
                // soffit from the gorge floor. The fascia strips take their
                // height from DeckThick for the same reason.
                float v = dist / ConcreteTile;
                float uOut = hw * 2f / ConcreteTile;
                float uLip = DeckThick / ConcreteTile;
                top[k * 2] = verts.Count; verts.Add(c - right * hw); uvs.Add(new Vector2(v, 0f));
                top[k * 2 + 1] = verts.Count; verts.Add(c + right * hw); uvs.Add(new Vector2(v, uOut));
                bot[k * 2] = verts.Count; verts.Add(under - right * hw); uvs.Add(new Vector2(v, -uLip));
                bot[k * 2 + 1] = verts.Count; verts.Add(under + right * hw); uvs.Add(new Vector2(v, uOut + uLip));
                dist += Spacing;
            }

            for (int k = 0; k + 1 < stations; k++)
            {
                int a = k * 2, b = (k + 1) * 2;
                // Top, facing up.
                tris.AddRange(new[] { top[a], top[b], top[a + 1], top[a + 1], top[b], top[b + 1] });
                // Soffit, facing down: the opposite winding, which is what makes
                // it visible from the gorge floor rather than from the sky.
                tris.AddRange(new[] { bot[a], bot[a + 1], bot[b], bot[a + 1], bot[b + 1], bot[b] });
                // Left fascia, facing out (-right).
                tris.AddRange(new[] { top[a], bot[a], top[b], bot[a], bot[b], top[b] });
                // Right fascia, facing out (+right).
                tris.AddRange(new[] { top[a + 1], top[b + 1], bot[a + 1], bot[a + 1], top[b + 1], bot[b + 1] });
            }
            // Abutment end caps, so the deck reads as a box and not as a ribbon
            // when you come over the crest at it.
            AddQuad(tris, top[0], top[1], bot[1], bot[0]);
            int e = (stations - 1) * 2;
            AddQuad(tris, top[e + 1], top[e], bot[e], bot[e + 1]);

            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            SaveMesh(mesh, "BridgeDeck" + no);

            var go = new GameObject("BridgeDeck" + no);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // The barrier keeps a car on the tarmac, so nothing should ever
            // stand on the verge of a deck — but "should never" is how the
            // beached-car reports start, and the alternative here is falling
            // through the world into a gorge.
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.sharedMaterial = phys;
            go.isStatic = true;
        }

        static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        /// <summary>Is <paramref name="at"/> within <paramref name="minDist"/>
        /// (in plan) of a station that is another part of the route — more
        /// than forty stations from <paramref name="i"/>, the short way round
        /// on a loop?</summary>
        static bool NearOtherRoute(List<Vector3> pts, int i, Vector3 at, float minDist)
        {
            int n = pts.Count;
            float min2 = minDist * minDist;
            for (int j = 0; j < n; j++)
            {
                int sep = Mathf.Abs(i - j);
                if (Loop) sep = Mathf.Min(sep, n - sep);
                if (sep <= 40) continue;
                float dx = pts[j].x - at.x, dz = pts[j].z - at.z;
                if (dx * dx + dz * dz < min2) return true;
            }
            return false;
        }

        static int BuildPiers(List<Vector3> pts, int from, int stations,
                              Transform parent, Material mat)
        {
            int n = pts.Count;
            int step = Mathf.Max(1, Mathf.RoundToInt(PierEvery / Spacing));
            int placed = 0;
            for (int k = 0; k < stations; k += step)
            {
                int i = (from + k) % n;
                float deckBottom = pts[i].y + DeckTopLift - DeckThick;
                float ground = GroundHeightAt(pts[i].x, pts[i].z);
                float h = deckBottom - ground;
                // Nothing to hold up at the abutments, where the ramp has
                // already brought the ground back to the deck.
                if (h < 2f) continue;

                Vector3 right = RightAt(pts, i);
                Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
                // A pair either side of the centreline, which is what a deck
                // this wide needs and what makes the span read as spanning.
                foreach (float side in new[] { -1f, 1f })
                {
                    // NOT IN THE ROAD BELOW. Where a span carries the route
                    // over ITSELF (both Parkway loops pass under their own
                    // bridge), a pier planted on the deck's grid lands on the
                    // lower carriageway: a concrete column on the racing line.
                    Vector3 foot = new Vector3(pts[i].x, 0f, pts[i].z) + right * side * (DeckHalfWidth * 0.55f);
                    if (NearOtherRoute(pts, i, foot, RoadWidth * 0.5f + KerbWidth + PierHalf + 1f)) continue;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Pier";
                    go.transform.SetParent(parent, false);
                    // Sunk a metre into the floor of the gorge: a column resting
                    // exactly on a mesh you can see under shows daylight beneath
                    // itself the moment the ground facet tilts.
                    float baseY = ground - 1f;
                    go.transform.position = new Vector3(pts[i].x, (baseY + deckBottom) * 0.5f, pts[i].z)
                                          + right * side * (DeckHalfWidth * 0.55f);
                    go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                    go.transform.localScale = new Vector3(PierHalf * 2f, deckBottom - baseY, PierHalf * 2f);
                    go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    go.layer = SolidLayer;
                    go.isStatic = true;
                    placed++;
                }
            }
            return placed;
        }

        static void BuildStartLine(List<Vector3> pts, Transform parent)
        {
            // Wet like the tarmac it is painted on: a dry stripe across a
            // shining road reads as a decal, which is what it is.
            var mat = MakeMat("StartLine", GridTexPath, wet: WetAsphalt);
            mat.mainTextureScale = new Vector2(8f, 2f);

            // On the stage the start line sits a lead-in past waypoint 0, so
            // the whole grid can stand on real road behind it without the
            // index walk falling off the front of the list.
            // (A loop stage's line is waypoint 0, like a circuit's: that is
            // where RaceManager counts the lap.)
            int startIdx = track.stage && !track.loop
                ? Mathf.RoundToInt(track.stageStartLineM / Spacing) : 0;
            Line("StartLine", startIdx);
            // A route with ends needs a line at each end: the one you launch
            // from is not the one that stops the clock, and they are kilometres
            // apart with a shutdown area beyond.
            if ((track.drag || (track.stage && !track.loop)) && track.FinishIndex > 0 && track.FinishIndex < pts.Count)
                Line("FinishLine", track.FinishIndex);

            void Line(string name, int idx)
            {
                Vector3 fwd = pts[Mathf.Min(idx + 1, pts.Count - 1)] - pts[idx];
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = name;
                go.transform.SetParent(parent, false);
                go.transform.position = pts[idx] + Vector3.up * 0.17f;
                go.transform.rotation = Quaternion.LookRotation(Vector3.down, fwd);
                go.transform.localScale = new Vector3(RoadWidth, 3f, 1f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                go.isStatic = true;
            }
        }

        // ------------------------------------------------------------------
        //  Scenery
        // ------------------------------------------------------------------
        static void BuildScenery(List<Vector3> pts, Transform parent)
        {
            var sceneryRoot = new GameObject("Scenery");
            sceneryRoot.transform.SetParent(parent, false);

            if (theme.buildingEvery > 0) PlaceBuildings(pts, sceneryRoot.transform);
            if (theme.gasStation) PlaceGasStation(pts, sceneryRoot.transform);
            if (theme.treeEvery > 0) PlaceTrees(pts, sceneryRoot.transform);
            if (theme.parkedEvery > 0) PlaceParkedCars(pts, sceneryRoot.transform);
            if (theme.lampEvery > 0) PlaceStreetLamps(pts, sceneryRoot.transform);
            PlacePosts(pts, sceneryRoot.transform);
        }

        // ------------------------------------------------------------------
        //  Roadside posts — the sense-of-speed pass
        // ------------------------------------------------------------------
        /// <summary>Delineator post: shaft height, cap height, square width.
        /// 1.1 m of white with 12 cm of red on top is a highway delineator;
        /// 8 cm is one or two pixels at 240 lines, which is a post rather
        /// than a plank.</summary>
        const float PostShaftH = 1.1f;
        const float PostCapH = 0.12f;
        const float PostW = 0.08f;
        /// <summary>Post centre, metres past the kerb's outer edge — on the
        /// verge, inside the wall line: 7.25 m from the centreline on a 12 m
        /// road against a 10 m barrier. Its inner face is 0.31 m clear of the
        /// kerb, and the obstacle audit holds it to 0.2.</summary>
        const float PostVergeOffset = 0.35f;
        /// <summary>Room the verge must have between the kerb and the wall for
        /// a post line at all. A drag strip has 0.1 m; there the wall's own
        /// seam posts carry the pitch instead.</summary>
        const float PostVergeMinRoom = 0.8f;
        /// <summary>Wall seam posts: one every two waypoints, 8 m, which is the
        /// wall texture's own repeat, so the seam lands on the seam. 0.2 m
        /// square, the wall's full height, proud of the face by 0.1 m — enough
        /// to throw a line at 240 lines and turn a smeared 8 m photo into a
        /// barrier that streams.</summary>
        const int WallPostEvery = 2;
        const float WallPostW = 0.2f;
        const float WallPostProud = 0.1f;
        /// <summary>Vertices one <see cref="AppendPost"/> adds: five faces of
        /// four. The audit divides by it to count posts.</summary>
        internal const int PostVerts = 20;

        /// <summary>
        /// Delineator posts down both verges and seam posts on both walls,
        /// ONE mesh per side per kind. A 5 km stage at 12 m would otherwise
        /// be ~850 GameObjects; four combined meshes are four draw calls.
        /// No colliders, like the lamps: a post that stops a car that ran wide
        /// is a wall in the run-off, and the audit would say so.
        ///
        /// Circuits only. A stage's guard walls hug the shoulder and exist
        /// only where the mountain falls away, so its posts are placed along
        /// those runs by BuildStageWalls instead.
        /// </summary>
        static void PlacePosts(List<Vector3> pts, Transform parent)
        {
            if (track != null && track.stage) return;
            int n = pts.Count;
            float kerbOuter = RoadWidth * 0.5f + KerbWidth;
            bool verge = theme.postEvery > 0 && WallOffset - kerbOuter >= PostVergeMinRoom;
            var postMat = MakeMat("RoadPost", PostTexPath, affine: 0f);
            var wallPostMat = MakeMat("WallPost", null,
                                      tint: new Color(0.16f, 0.16f, 0.18f), affine: 0f);
            int vergePosts = 0, wallPosts = 0;

            foreach (float side in new[] { -1f, 1f })
            {
                string sfx = side < 0 ? "L" : "R";
                if (verge)
                {
                    var verts = new List<Vector3>();
                    var uvs = new List<Vector2>();
                    var tris = new List<int>();
                    for (int i = 1; i < n; i += theme.postEvery)
                    {
                        Vector3 right = RightAt(pts, i);
                        Vector3 baseP = pts[i] + right * side * (kerbOuter + PostVergeOffset);
                        // Not across the forecourt entrance.
                        if (OnFuelPad(baseP, 1.5f)) continue;
                        // Seated on the shoulder it stands in — the run-off,
                        // or the Street pavement's ramp onto a deck — where
                        // there is one here; the deck over a gorge, or its own
                        // patch of ground, where there is not. The ground
                        // lattice is held HideMarginM and more under the
                        // shoulder, so a post seated on it was buried.
                        var prof = shoulderProfiles != null ? shoulderProfiles[side < 0f ? 0 : 1][i] : null;
                        float postE = KerbWidth + PostVergeOffset;
                        float onShoulder = prof != null ? EvalShoulderProfile(prof, postE, false) : float.NaN;
                        baseP.y = !float.IsNaN(onShoulder) ? pts[i].y + RoadLift + onShoulder
                                : OverGorge(i) ? pts[i].y + DeckTopLift
                                : GroundHeightAt(baseP.x, baseP.z);
                        AppendPost(verts, uvs, tris, baseP, right, PostW,
                                   PostShaftH + PostCapH);
                        vergePosts++;
                    }
                    CombinedPosts(verts, uvs, tris, "PostMesh" + sfx, "Posts" + sfx, postMat, parent);
                }

                {
                    var verts = new List<Vector3>();
                    var uvs = new List<Vector2>();
                    var tris = new List<int>();
                    int last = Loop ? n : n - 1;
                    for (int i = 0; i < last; i += WallPostEvery)
                    {
                        // Not in the driveway the wall leaves open.
                        if (side == padSide && InWallGap(i, n)) continue;
                        Vector3 right = RightAt(pts, i);
                        // On the wall's own base (BuildWalls seats the wall at
                        // the waypoint height, not on GroundHeightAt), a
                        // hand's width inside its face.
                        Vector3 baseP = pts[i] + right * side * (WallOffset - WallPostProud);
                        AppendPost(verts, uvs, tris, baseP, right, WallPostW, WallHeight);
                        wallPosts++;
                    }
                    CombinedPosts(verts, uvs, tris, "WallPostMesh" + sfx, "WallPosts" + sfx,
                                  wallPostMat, parent);
                }
            }
            Log($"Placed {vergePosts} verge posts and {wallPosts} wall seam posts " +
                (verge ? "" : "(no verge room on this venue — walls only) ") +
                "as four combined meshes.");
        }

        /// <summary>Save one combined post mesh as its own static, colliderless
        /// object. Nothing to save is nothing built.</summary>
        static void CombinedPosts(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                  string meshName, string goName, Material mat, Transform parent)
        {
            if (verts.Count == 0) return;
            var mesh = new Mesh();
            // Five faces a post; a long circuit's wall seams can pass the
            // 16-bit index ceiling.
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            SaveMesh(mesh, meshName);
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        /// <summary>
        /// Append one square post standing on <paramref name="baseP"/>, its
        /// faces aligned to <paramref name="right"/>: four sides and a top,
        /// twenty vertices, wound OUTWARD (Cross(b - a, c - a) is the face
        /// normal, the convention BuildWalls relies on). v runs up the post
        /// so the post texture's red band lands on the cap.
        /// </summary>
        static void AppendPost(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                               Vector3 baseP, Vector3 right, float width, float height)
        {
            right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            float hw = width * 0.5f;
            Vector3 up = Vector3.up * height;

            void Side(Vector3 normal)
            {
                // Tangent chosen so Cross(up, tangent) == normal.
                Vector3 tangent = Vector3.Cross(normal, Vector3.up);
                Vector3 a = baseP + normal * hw - tangent * hw;
                Vector3 c = baseP + normal * hw + tangent * hw + up;
                int v = verts.Count;
                verts.Add(a); verts.Add(a + up); verts.Add(c); verts.Add(c - up);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
            Side(right); Side(-right); Side(fwd); Side(-fwd);

            // The top, normal up.
            {
                int v = verts.Count;
                Vector3 top = baseP + up;
                verts.Add(top - right * hw - fwd * hw);
                verts.Add(top - right * hw + fwd * hw);
                verts.Add(top + right * hw + fwd * hw);
                verts.Add(top + right * hw - fwd * hw);
                for (int k = 0; k < 4; k++) uvs.Add(new Vector2(0.5f, 1f));
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
        }

        /// <summary>
        /// Street lighting: a post and a lamp head that are there all day, and
        /// a "Glow" marker under each head that says where the light is.
        ///
        /// The glows go under one "NightLights" parent carrying a single
        /// NightGlow component — the hour toggles that one object rather than
        /// thirty. Nothing here gets a collider: the posts stand outside the
        /// barrier line, where a collider could only ever cost contact pairs
        /// against a car that cannot reach them.
        ///
        /// NO POOL any more (2026-09-21, the NFS night pass). Each lamp used to
        /// lay a 16 m additive "Pool" quad on the road under it. It never lit
        /// the road: it was a disc of orange ADDED on top of whatever was
        /// there, so it brightened the black gaps between lamps' reach the
        /// same as the tarmac, lit a car driving through it not at all, put
        /// no glint in a wet surface, and cost a 16 m quad of overdraw per
        /// lamp after dark. The pool is per pixel now: NightGlow reads the
        /// Glow markers' positions at runtime as lamp heads and registers
        /// them with StreetLights, which pushes the ones nearest the camera
        /// into a twelve-slot table that PSXLamps.cginc lights per pixel —
        /// in the road, the kerb, the car paint and the rain alike. The
        /// Glow quads STAY: they are the lamp-head markers NightGlow needs
        /// (it retires the quads themselves and draws its own halos), and
        /// LampGlow.mat is what keeps PSX/Glow in the WebGL build for the
        /// cars' lenses. Scenes baked before this still carry their Pools;
        /// NightGlow retires those at runtime too, and the next scene build
        /// drops them for good.
        /// </summary>
        static void PlaceStreetLamps(List<Vector3> pts, Transform parent)
        {
            var glowShader = Shader.Find("PSX/Glow");
            if (glowShader == null) { Log("WARN: PSX/Glow missing — no street lighting."); return; }

            var postMat = MakeMat("LampPost", null, tint: new Color(0.30f, 0.30f, 0.34f), affine: 0f);
            var headMat = MakeMat("LampHead", null, tint: new Color(0.62f, 0.60f, 0.55f), affine: 0f);
            var glowMat = MakeGlowMaterial("LampGlow", new Color(1.00f, 0.86f, 0.55f), 1.5f);
            var glowMesh = GetOrCreateGlowQuad();

            var lampRoot = new GameObject("StreetLamps");
            lampRoot.transform.SetParent(parent, false);
            var nightRoot = new GameObject("NightLights");
            nightRoot.transform.SetParent(parent, false);
            nightRoot.AddComponent<NightGlow>();

            const float postH = 6.2f;
            const float armLen = 1.6f;
            int placed = 0;
            for (int i = 2; i < pts.Count; i += theme.lampEvery)
            {
                float side = (i / theme.lampEvery) % 2 == 0 ? 1f : -1f;
                Vector3 right = RightAt(pts, i);
                // Off THIS venue's barrier line: on a stage the wall hugs the
                // shoulder, and a lamp at the circuits' constant 10 m would
                // stand on the tarmac of a 16 m freeway.
                Vector3 baseP = pts[i] + right * side * (WallOffsetFor(track) + 0.8f);
                // The lamp line runs where the forecourt entrance is, and a
                // post in the middle of it would be a lamp standing on tarmac
                // the player is meant to drive over. The station lights its own
                // canopy.
                if (OnFuelPad(baseP, 1.5f)) continue;
                // Its own patch of ground — except over a gorge, where the
                // ground is ten metres down and the thing to stand on is the
                // deck. The lamp line sits at WallOffset + 0.8, inside the deck
                // edge at WallOffset + 1.4, so there is something under it.
                baseP.y = OverGorge(i) ? pts[i].y + DeckTopLift
                                       : ShoulderLatticeY(baseP.x, baseP.z);

                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(post.GetComponent<Collider>());
                post.name = "LampPost";
                post.transform.SetParent(lampRoot.transform, false);
                post.transform.position = baseP + Vector3.up * (postH * 0.5f);
                post.transform.localScale = new Vector3(0.22f, postH, 0.22f);
                post.GetComponent<MeshRenderer>().sharedMaterial = postMat;
                post.isStatic = true;

                // The head leans in over the road, which is what makes a row of
                // posts read as street lighting rather than as fence posts.
                Vector3 headP = baseP + Vector3.up * postH - right * side * armLen;
                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
                head.name = "LampHead";
                head.transform.SetParent(lampRoot.transform, false);
                head.transform.position = headP;
                head.transform.rotation = Quaternion.LookRotation(-right * side, Vector3.up);
                head.transform.localScale = new Vector3(0.5f, 0.20f, armLen * 2.1f);
                head.GetComponent<MeshRenderer>().sharedMaterial = headMat;
                head.isStatic = true;

                var glow = new GameObject("Glow");
                glow.transform.SetParent(nightRoot.transform, false);
                glow.transform.position = headP - Vector3.up * 0.18f;
                // Face down: the lamp is seen from below and from the side, and
                // Cull Off means one horizontal quad covers both.
                glow.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                glow.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
                glow.AddComponent<MeshFilter>().sharedMesh = glowMesh;
                // Saved OFF. A scene opened in the editor is a scene at the hour
                // it was baked at, which is sunset with the lamps not yet lit;
                // NightGlow turns them on at runtime when the hour says so.
                glow.AddComponent<MeshRenderer>().sharedMaterial = glowMat;
                glow.GetComponent<MeshRenderer>().enabled = false;
                // (No "Pool" quad under it: see the summary. The light on the
                // road is PSXLamps.cginc's, sourced from this Glow's position.)
                placed++;
            }
            Log($"Placed {placed} street lamps.");
        }

        // ---- the zone-line curtain ----
        // The gradient the curtain wears, bottom to top. Starting values for
        // tuning; what each one is FOR is the derivation.
        /// <summary>The body colour: a mid-dark saturated blue. Over the Noon
        /// sky (0.72, 0.85, 0.96) at alpha 0.6 it lands near (0.38, 0.58,
        /// 0.98) — plainly a wall; over grass it goes teal, over tarmac blue.
        /// A PALE cyan (the FFXIV reference's colour) would read as the
        /// additive line did against daylight sky, which is not at all.</summary>
        static readonly Color ZoneCurtainBlue = new Color(0.15f, 0.40f, 1.00f);
        /// <summary>The bottom tenth is a solid white band with a HARD step
        /// above it, so the foot of the curtain survives PSXBlit's 5-bit
        /// quantize at RETRO, which bands the gradient above into a handful of
        /// steps and would smear a soft foot into the road.</summary>
        const float ZoneCurtainBaseBand = 0.10f, ZoneCurtainBaseAlpha = 0.95f;
        /// <summary>The body starts at 0.70 above the band and falls to 0 at
        /// the top with a power of 1.6 — above 1 so the mass stays low, the
        /// way light standing on a road would, and the top edge is soft
        /// enough never to read as a rectangle against the sky.</summary>
        const float ZoneCurtainBodyAlpha = 0.70f, ZoneCurtainFalloff = 1.6f;
        /// <summary>Every 4th column is a quarter brighter: faint vertical
        /// streaks, the one texture the reference has. 16 columns across
        /// 11-13 m is a streak every ~0.8 m.</summary>
        const int ZoneCurtainStreakPitch = 4;
        const float ZoneCurtainStreakGain = 1.25f;
        /// <summary>_Strength on the material; the texture already carries
        /// the intended alpha, so this is a tuning hook at unity.</summary>
        const float ZoneCurtainStrength = 1f;

        /// <summary>
        /// THE ZONE-LINE CURTAIN'S MATERIAL: PSX/ZoneLine (alpha-blended — see
        /// the shader's own note for why additive could not work) wearing the
        /// gradient below.
        ///
        /// The asset name is the one the first (additive, PSX/Glow) line used,
        /// and the shader is written UNCONDITIONALLY, exactly as
        /// MakeGlowMaterial writes its own: the sandbox already holds a
        /// ZoneLine.mat, and a load-or-create that trusted the asset would
        /// rebuild every scene with the additive material the owner could not
        /// see. Warns rather than throws on a missing shader, the way the lamp
        /// pass does — a run through a script that does not mirror Shaders/
        /// gets a magenta curtain and a line in the log saying why.
        /// </summary>
        static Material MakeZoneLineMaterial()
        {
            string p = MatDir + "/ZoneLine.mat";
            var shader = Shader.Find("PSX/ZoneLine");
            if (shader == null)
                Log("WARN: PSX/ZoneLine shader not found — the zone line will render magenta " +
                    "(was Assets/PSXRacing/Shaders mirrored?)");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (mat == null)
            {
                mat = new Material(shader != null ? shader : Shader.Find("PSX/Glow"));
                AssetDatabase.CreateAsset(mat, p);
            }
            if (shader != null) mat.shader = shader;
            mat.mainTexture = GetOrCreateCurtainTexture();
            // WHITE, not the curtain's blue: the shader's tex.rgb * _Color
            // cannot lift a texel above the tint, so a white base band under
            // a blue body has to be painted INTO the texture; this stays a
            // tint hook at unity.
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Strength", ZoneCurtainStrength);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The curtain gradient: 16 x 64, white band at the foot, blue body
        /// fading to nothing at the top, faint streaks. A generated .asset
        /// beside Glow.asset, so ConfigureTextureImporters' Art/-only 256 px
        /// point-filter rule neither applies to it nor is broken by it —
        /// bilinear and clamped on purpose, because a 64-row gradient
        /// point-sampled across 150 screen lines is a staircase.
        ///
        /// v = 0 is the ROAD: GlowQuad's uv (0,0) is its bottom-left vertex,
        /// so texture row 0 lands at the curtain's foot.
        /// </summary>
        static Texture2D GetOrCreateCurtainTexture()
        {
            string p = GenDir + "/ZoneCurtain.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (tex != null) return tex;
            const int w = 16, h = 64;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                bool band = v < ZoneCurtainBaseBand;
                float a = band
                    ? ZoneCurtainBaseAlpha
                    : ZoneCurtainBodyAlpha * Mathf.Pow(
                        1f - (v - ZoneCurtainBaseBand) / (1f - ZoneCurtainBaseBand),
                        ZoneCurtainFalloff);
                Color rgb = band ? Color.white : ZoneCurtainBlue;
                for (int x = 0; x < w; x++)
                {
                    float ax = x % ZoneCurtainStreakPitch == 0
                        ? Mathf.Min(1f, a * ZoneCurtainStreakGain) : a;
                    tex.SetPixel(x, y, new Color(rgb.r, rgb.g, rgb.b, ax));
                }
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            AssetDatabase.CreateAsset(tex, p);
            return tex;
        }

        /// <summary>Additive glow material, shared by every circuit — the lamps
        /// are the same lamps whichever track they stand beside.</summary>
        static Material MakeGlowMaterial(string name, Color tint, float strength)
        {
            string p = MatDir + "/" + name + ".mat";
            var shader = Shader.Find("PSX/Glow");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, p); }
            mat.shader = shader;
            mat.mainTexture = GetOrCreateGlowTexture();
            mat.SetColor("_Color", tint);
            mat.SetFloat("_Strength", strength);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D GetOrCreateGlowTexture()
        {
            string p = GenDir + "/Glow.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (tex != null) return tex;
            const int n = 64;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - (n - 1) * 0.5f) / (n * 0.5f);
                    float dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            AssetDatabase.CreateAsset(tex, p);
            return tex;
        }

        static Mesh GetOrCreateGlowQuad()
        {
            string p = GenDir + "/GlowQuad.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (m != null) return m;
            m = new Mesh { name = "GlowQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f),
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        /// <summary>
        /// Traffic that never moves: the pack's van, work truck and everyday
        /// shells parked along the kerb outside the barriers.
        ///
        /// This is where the models with no catalog car earn their place. A
        /// 1950s delivery van and a Land Rover pickup are not in a GT4-derived
        /// car list and should never turn up on a grid, but they are exactly
        /// what a city street should have parked on it — and the street was
        /// previously empty apart from trees.
        ///
        /// No colliders: they sit beyond the wall line, so a collider would only
        /// cost pairs the player can never touch.
        /// </summary>
        static void PlaceParkedCars(List<Vector3> pts, Transform parent)
        {
            // The owner's traffic cars (2026-09-25, "add these vehicles as
            // traffic ... just race tracks") are interleaved rather than
            // appended, so a short street still shows them: the keys are dealt
            // in order.
            string[] keys = { "crown_victoria", "classic_van", "camry_2001", "jdm_pickup",
                              "ford_transit", "landrover", "euro_hatch",
                              "volvo_estate", "citroen_cx", "bmw_e30", "audi_saloon" };

            var rng = new System.Random(31);
            int placed = 0;
            for (int i = 9; i < pts.Count; i += theme.parkedEvery)
            {
                // PlaceTrees puts a 5.2 m crossed quad every treeEvery-th
                // waypoint, and waypoints are 4 m apart — so anything within one
                // of a tree index grows a tree through its roof.
                if (theme.treeEvery > 0)
                {
                    int phase = i % theme.treeEvery;
                    if (phase >= 3 && phase <= 5) continue;
                }

                if (OverGorge(i)) continue;
                var def = CarModelLibrary.Load(keys[placed % keys.Length]);
                if (def == null) continue;

                float side = (i / theme.parkedEvery) % 2 == 0 ? 1f : -1f;
                Vector3 right = RightAt(pts, i);
                // The direction of TRAVEL here. (Cross(up, right) is the
                // reverse of it: RightAt is Cross(up, travel), so crossing
                // back the same way turns round.)
                Vector3 travel = Vector3.Cross(right, Vector3.up);

                var go = new GameObject("Parked_" + def.key);
                go.transform.SetParent(parent, false);
                // Tight against the outside of the barrier, in front of the
                // tree line: street parking, not a scrapyard in a field.
                Vector3 parkAt = pts[i] + right * side * (WallOffsetFor(track) + 1.5f);
                // The forecourt is the one stretch of verge that is a road.
                if (OnFuelPad(parkAt, 2f)) { UnityEngine.Object.DestroyImmediate(go); continue; }
                // On the ground MESH, a metre and a half behind the wall: where
                // the lattice was lowered under the run-off beside it, the
                // field above the mesh would leave the car standing on air.
                parkAt.y = ShoulderLatticeY(parkAt.x, parkAt.z);
                go.transform.position = parkAt;
                // NORTH AMERICA: a car parks WITH the traffic on its own side
                // of the street (owner, 2026-09-25: "Cars should drive on the
                // right side of the road"). Right of the direction of travel
                // faces forward, left faces back — this used to be a coin flip
                // per car, which parked half the street against traffic. A
                // couple of degrees off true still: a row of perfectly aligned
                // cars reads as a texture, not as parking.
                go.transform.rotation = Quaternion.LookRotation(
                    side > 0f ? travel : -travel, Vector3.up)
                    * Quaternion.Euler(0f, (float)(rng.NextDouble() * 6.0 - 3.0), 0f);

                DressProp(go.transform, def, rng.Next(Mathf.Max(1, def.SkinCount)));
                foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
                placed++;
            }
            Log($"Placed {placed} parked cars.");
        }

        /// <summary>Build a static copy of a shell: body plus four wheels at the
        /// axle positions the baker measured.</summary>
        static void DressProp(Transform root, CarModelDef def, int skin)
        {
            var mat = def.SkinCount > 0 ? def.skinMaterials[Mathf.Clamp(skin, 0, def.SkinCount - 1)] : null;
            var wheelMat = def.wheelMaterial != null ? def.wheelMaterial : mat;

            var body = new GameObject("Body");
            body.transform.SetParent(root, false);
            // Body and wheels through the SAME offsets the driven cars use.
            body.transform.localPosition = new Vector3(0f, def.bodyYOffset, def.bodyZOffset);
            body.transform.localRotation = Quaternion.Euler(0f, def.bodyYaw, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = def.bodyMesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = mat;

            for (int w = 0; w < 4; w++)
            {
                bool left = w % 2 == 0;
                var wheel = new GameObject("Wheel" + w);
                wheel.transform.SetParent(root, false);
                wheel.transform.localPosition = new Vector3(
                    (left ? -0.5f : 0.5f) * def.trackWidth,
                    def.wheelRadius,
                    (w < 2 ? 0.5f : -0.5f) * def.wheelbase);
                wheel.transform.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                wheel.transform.localScale = Vector3.one * def.wheelMeshScale;
                wheel.AddComponent<MeshFilter>().sharedMesh = def.wheelMesh;
                var wmr = wheel.AddComponent<MeshRenderer>();
                wmr.sharedMaterial = wheelMat;
                CarPaint.DullWheels(wmr);
            }
        }

        static void PlaceBuildings(List<Vector3> pts, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Buildings/Buildings.fbx");
            if (prefab == null) { Log("WARN: Buildings.fbx not found"); return; }

            var template = (GameObject)UnityEngine.Object.Instantiate(prefab);
            var children = new List<Transform>();
            foreach (Transform c in template.transform)
                if (c.GetComponentInChildren<MeshRenderer>() != null) children.Add(c);
            if (children.Count == 0) children.Add(template.transform);
            Log($"Buildings.fbx: {children.Count} building meshes found.");

            // Normalize: median height should be city-scale (~12 m)
            var heights = children.Select(c => CombinedBounds(c.gameObject).size.y)
                                  .OrderBy(h => h).ToList();
            float median = heights[heights.Count / 2];
            float scale = (median > 0.5f) ? Mathf.Clamp(12f / median, 0.05f, 40f) : 1f;
            Log($"Building median height {median:0.0} -> uniform scale {scale:0.00}");

            int n = pts.Count;
            int placed = 0, crowded = 0;
            var rng = new System.Random(42);
            // Start half a spacing in rather than at waypoint 0. The grid sits
            // on waypoint 0, so a building there is a 12 m slab directly beside
            // the start line, boxing in the one shot every player sees first.
            for (int i = theme.buildingEvery / 2; i < n; i += theme.buildingEvery)
            {
                if (OverGorge(i)) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < theme.buildingSkip) continue;
                    // Leave room for the forecourt, wherever it landed on this
                    // circuit. Measured against the PAD rather than against a
                    // radius round its waypoint: the pad is 36 m along the road
                    // and 30 m off it, and a circle big enough to contain that
                    // clears a great deal of ground that is not forecourt.
                    if (OnFuelPad(pts[i] + RightAt(pts, i) * side * (WallOffsetFor(track) + 8f), 10f))
                        continue;
                    var src = children[rng.Next(children.Count)];
                    var b = (GameObject)UnityEngine.Object.Instantiate(src.gameObject);
                    b.name = "Building";
                    b.transform.SetParent(parent, false);
                    b.transform.localScale = src.localScale * scale;

                    Vector3 right = RightAt(pts, i);
                    Vector3 fwd = -right * side;        // face the road
                    b.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

                    // Measure the building in its OWN frame, never with
                    // Renderer.bounds.
                    //
                    // Renderer.bounds is a WORLD-axis-aligned box. These
                    // buildings are yawed to face the road, so for anything not
                    // on a cardinal heading that AABB reports the building's
                    // DIAGONAL — up to 1.41x its real footprint. The old code
                    // took that inflated size and applied it along the
                    // building's own rotated axes, which is where 19 colliders
                    // reaching clear across the racing line came from: solid,
                    // on the Solid layer, and with no renderer of their own,
                    // so they were invisible barriers in the most literal sense.
                    //
                    // The placement was wrong in the same direction: extents is
                    // ALREADY a half-size, and the old `extents * 0.5f` made it
                    // a quarter — so every building was set roughly half its own
                    // width too close to the track before the oversized collider
                    // was even added.
                    Bounds local = LocalBounds(b);
                    Vector3 ls = b.transform.lossyScale;
                    // LookRotation puts local +Z on `fwd`, so the road-facing
                    // face is local.max.z from the origin. Placing off that
                    // makes the clearance mean what it says regardless of how
                    // the mesh is centred or which building was drawn.
                    float faceOffset = local.max.z * ls.z;
                    // Measured from THIS venue's barrier line (WallOffsetFor),
                    // never the circuits' 10 m constant: a stage's wall hugs
                    // its shoulder, and a wide enough road would otherwise put
                    // a building face inside its own barrier.
                    Vector3 pos = pts[i] + right * side *
                                  (WallOffsetFor(track) + BuildingClearance + faceOffset);

                    // Then push it back out until the WHOLE FOOTPRINT clears the
                    // barrier, not just the middle of its front wall.
                    //
                    // The placement above measures one distance, from one
                    // waypoint, along that one waypoint's normal. A road is not a
                    // straight line. Laid tangentially beside a 20 m radius
                    // corner, a 20 m warehouse has its far corners raked round
                    // toward the inside of the bend, and on the dock circuit that
                    // left one of them 9.43 m off the centreline -- INSIDE the
                    // 10 m barrier line, standing out in the gravel, on the Solid
                    // layer with no renderer of its own. Which is an invisible
                    // barrier in the most literal sense, and is how it was
                    // reported: run wide onto a legal piece of the circuit and
                    // stop dead against nothing at all.
                    //
                    // Note this is the SECOND time a building collider has ended
                    // up where a car can reach it. The first was a sizing error
                    // and was audited against the tarmac; this one is a placement
                    // error out in the run-off, which the tarmac audit could not
                    // see by construction. TrackObstacleAudit now measures both
                    // bands.
                    Vector3 halfExt = new Vector3(local.extents.x * ls.x, 0f,
                                                  local.extents.z * ls.z);
                    // The mesh origin is not the middle of the mesh, so the
                    // footprint has to be taken about local.center or the corners
                    // being tested are not the building's corners.
                    Vector3 footCentre = b.transform.rotation *
                        new Vector3(local.center.x * ls.x, 0f, local.center.z * ls.z);
                    if (!PushClearOfTrack(pts, ref pos, b.transform.rotation,
                                          footCentre, halfExt, right * side))
                    {
                        // Nowhere along this normal is clear of every arm of the
                        // circuit. A missing warehouse is a gap in a skyline; one
                        // seated in the run-off is a wall you cannot see.
                        UnityEngine.Object.DestroyImmediate(b);
                        crowded++;
                        continue;
                    }
                    // SET INTO the ground, and into the lowest corner of it.
                    //
                    // This used to be pos.y = -local.min.y * ls.y, which stands
                    // the building on the plane y = 0 with its base exactly
                    // coplanar with the ground. That was already wrong on a flat
                    // circuit — these meshes are hollow shells with no floor, so
                    // a lens below the base line sees straight in under the
                    // walls, reported as "I can see under buildings" — and with
                    // the ground now following the road it would leave the whole
                    // downhill side of a 20 m block standing in mid-air.
                    //
                    // Two fixes in one line: take the LOWEST ground under the
                    // footprint rather than the height at the origin, and bury
                    // the base half a metre under it so there is no seam to see
                    // through from any angle.
                    float footing = LowestGroundUnder(
                        new Vector3(pos.x, 0f, pos.z) + footCentre,
                        b.transform.rotation, halfExt.x, halfExt.z);
                    pos.y = footing - local.min.y * ls.y - BuildingSink;
                    b.transform.position = pos;

                    ConvertToPSXMaterials(b);
                    // The collider lives on its own child so the building can sit
                    // on the Solid layer for the suspension mask without moving
                    // its renderers off the camera's culling mask. Local bounds
                    // are already in this child's space (identity local
                    // transform), so they need no scale correction.
                    var colGO = new GameObject("Collider");
                    colGO.transform.SetParent(b.transform, false);
                    colGO.layer = SolidLayer;
                    var col = colGO.AddComponent<BoxCollider>();
                    col.center = local.center;
                    col.size = local.size;
                    placed++;
                }
            }
            UnityEngine.Object.DestroyImmediate(template);
            Log($"Placed {placed} buildings" +
                (crowded > 0 ? $", dropped {crowded} with no room clear of the circuit." : "."));
        }

        /// <summary>
        /// Slide a footprint outward until EVERY corner of it is at least the
        /// barrier line plus <see cref="BuildingClearance"/> from the centreline,
        /// measured against the whole path rather than against one waypoint.
        ///
        /// Iterative because the answer moves: pushing a building out changes
        /// which stretch of road is nearest to it, and on the inside of a bend
        /// one shove is never enough. Returns false when no amount of pushing
        /// works -- a circuit that folds back on itself has places where walking
        /// away from one arm walks into another -- so the caller can drop the
        /// building instead of seating it somewhere a car can reach.
        /// </summary>
        static bool PushClearOfTrack(List<Vector3> pts, ref Vector3 pos, Quaternion rot,
                                     Vector3 footCentre, Vector3 halfExt, Vector3 outward)
        {
            float Want = WallOffsetFor(track) + BuildingClearance;
            const float MaxPush = 14f;
            float pushed = 0f;
            for (int pass = 0; pass < 32; pass++)
            {
                float nearest = float.MaxValue;
                for (int c = 0; c < 4; c++)
                {
                    Vector3 corner = pos + footCentre + rot * new Vector3(
                        (c & 1) == 0 ? -halfExt.x : halfExt.x, 0f,
                        (c & 2) == 0 ? -halfExt.z : halfExt.z);
                    nearest = Mathf.Min(nearest, PlanDistanceToPath(pts, corner));
                }
                float deficit = Want - nearest;
                if (deficit <= 0.01f) return true;
                if (pushed + deficit > MaxPush) return false;
                pos += outward * deficit;
                pushed += deficit;
            }
            return false;
        }

        /// <summary>Distance from a world point to the centreline, in PLAN.
        /// Height is dropped on purpose: a warehouse beside a road that climbs at
        /// 5% is still beside it, and a 3D distance would let one creep in on the
        /// low side of a grade by exactly the height it stands below.</summary>
        static float PlanDistanceToPath(List<Vector3> pts, Vector3 p)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            float best = float.MaxValue;
            for (int i = 0; i < last; i++)
            {
                Vector3 a = pts[i], b = pts[Loop ? (i + 1) % n : i + 1];
                float ex = b.x - a.x, ez = b.z - a.z;
                float len2 = ex * ex + ez * ez;
                float t = len2 < 1e-6f ? 0f
                    : Mathf.Clamp01(((p.x - a.x) * ex + (p.z - a.z) * ez) / len2);
                float dx = a.x + ex * t - p.x, dz = a.z + ez * t - p.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < best) best = d2;
            }
            return Mathf.Sqrt(best);
        }
        /// <summary>
        /// Bounds of every mesh under <paramref name="root"/>, expressed in
        /// root's OWN local space — an oriented box, not a world AABB.
        ///
        /// This is what a child BoxCollider with an identity local transform
        /// actually wants, and it is rotation-invariant, so a building yawed to
        /// face the road measures the same as one left on a cardinal heading.
        /// Uses mesh bounds rather than Renderer.bounds precisely because the
        /// renderer's version has already been flattened into world axes.
        /// </summary>
        static Bounds LocalBounds(GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
            bool any = false;
            var acc = new Bounds();

            foreach (var mf in filters)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Bounds mb = mesh.bounds;
                Matrix4x4 toWorld = mf.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? mb.min.x : mb.max.x,
                        (c & 2) == 0 ? mb.min.y : mb.max.y,
                        (c & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = toLocal.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(corner));
                    if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                    else acc.Encapsulate(p);
                }
            }
            return any ? acc : new Bounds(Vector3.zero, Vector3.one);
        }

        static Bounds CombinedBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>A forecourt this flat wants ground this level, in metres of
        /// rise across its own width. 1.5 m over sixty is a 2.5% grade — what a
        /// petrol station forecourt actually sits on.</summary>
        const float FuelStopFlatEnough = 1.5f;

        /// <summary>
        /// Where the forecourt goes: from 62% of the way round, on the outside,
        /// walking on until the ground is LEVEL enough to build on.
        ///
        /// A lap fraction rather than a world coordinate, so it lands beside the
        /// road on every circuit instead of only on the one whose back straight
        /// the coordinate was read off — and level, because a 60 m building with
        /// a flat floor on a 10% hillside is three metres in the air at one end
        /// or three metres underground at the other. Nobody builds one there,
        /// and the terrain audit says so.
        ///
        /// Falls back to the flattest site on the lap when a circuit has no
        /// level stretch at all, which is a real possibility on a mountain pass.
        /// </summary>
        static int GasStationIndex(List<Vector3> pts)
        {
            int n = pts.Count;
            if (n == 0) return 0;
            int start = (int)(n * 0.62f);
            int span = Mathf.Max(1, Mathf.RoundToInt(PadHalfAcross / Spacing));
            // Every station the forecourt touches — its driveways lie inside
            // the apron, the apron inside the pad, and the pad's flattening
            // fades out over PadBlend past that — plus the station the deck
            // is carried onto the approach.
            int clear = Mathf.CeilToInt((PadHalfAcross + PadBlend) / Spacing) + 1;

            // Strict first: no bridge anywhere under the pad or its driveways.
            // Only a lap with no such site at all falls back to the old test.
            for (int strict = 1; strict >= 0; strict--)
            {
                int best = -1;
                float bestRelief = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    int idx = (start + k) % n;
                    // Never out over a gorge. A forecourt is a slab on the ground,
                    // and 62% of the way round HarborPoint is the channel.
                    if (OverGorge(idx)) continue;
                    // And never beside one. This tested the pad CENTRE only, so
                    // a centre on solid ground 20 m from an abutment passed with
                    // a driveway — a twenty-metre gap in the wall — opening onto
                    // the deck approach, or onto the deck itself with a 9-14 m
                    // drop past its edge. Latent on the shipped circuits, and
                    // one moved span away from being real.
                    if (strict == 1 && NearBridgeSpan(idx, clear)) continue;

                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int o = -span; o <= span; o++)
                    {
                        int j = Loop ? ((idx + o) % n + n) % n : Mathf.Clamp(idx + o, 0, n - 1);
                        lo = Mathf.Min(lo, pts[j].y);
                        hi = Mathf.Max(hi, pts[j].y);
                    }
                    float relief = hi - lo;
                    if (relief < bestRelief) { bestRelief = relief; best = idx; }
                    if (relief <= FuelStopFlatEnough) return idx;
                }
                if (best >= 0) return best;
                if (strict == 1)
                    Log("WARN: no forecourt site clear of every bridge on this lap — " +
                        "falling back to one that is only clear of the gorge.");
            }
            return start;
        }

        // ------------------------------------------------------------------
        //  The fuel stop
        // ------------------------------------------------------------------
        //
        //  The gas station used to be a photograph of a gas station: a model
        //  dropped outside the barrier line with one box collider over the
        //  whole of it, on a circuit whose walls ran past it unbroken. There
        //  was no way to reach it and nothing to do there if you had.
        //
        //  Fuel is now spent by the metre and bought by the gallon, so the
        //  forecourt has to be a place a car can be driven onto and stopped on.
        //  That takes four things the old code did none of, and they have to
        //  happen in this order because each depends on the last:
        //
        //    1. PLAN it, before the terrain mesh is built, so the ground can be
        //       flattened under it (a forecourt on a 13% hillside is not one).
        //    2. CUT the barrier, so there is a way in and a way out.
        //    3. LAY an apron on the Road layer from the road edge to the
        //       pumps, so the surface has tarmac grip rather than field grip.
        //    4. Place the station FACING the road — the model is turned by
        //       where its own pumps are, not by an assumption — and collide
        //       only the parts that should stop a car.
        //
        /// <summary>
        /// Half the forecourt's width and depth, MEASURED off the station model
        /// rather than typed.
        ///
        /// The model is scaled to seven metres tall at bake time and its
        /// footprint follows from that — nobody chose it and nobody can predict
        /// it from the .fbx. A hand-picked pad is therefore a pad that either
        /// fails to contain the building (leaving its back half hanging over a
        /// hillside the flattening never reached) or is far larger than it needs
        /// to be, which on a mountain pass is a plateau you can see from the
        /// other side of the valley.
        /// </summary>
        static float PadHalfAcross = 18f;
        static float PadHalfDeep = 15f;
        /// <summary>
        /// Half the TARMAC, which is a smaller thing than half the flattened
        /// ground and has to be tracked separately.
        ///
        /// The ground is flattened to the lot's diagonal because world-axis
        /// boxes reason about it; the apron only has to cover the lot and the
        /// room to turn into it. Laying tarmac over the whole flattened area
        /// gives a 26 m-wide filling station sixty-six metres of forecourt,
        /// most of it a car park nobody parks in.
        /// </summary>
        static float ApronHalfAcross = 18f;
        /// <summary>Open tarmac between the road and the front of the station.
        /// The room to turn in off the racing line, line up and stop.</summary>
        const float ForecourtApproach = 16f;
        /// <summary>How far the station is set into its own pad. Small, because
        /// the pad under it is level by construction — this only has to cover
        /// the corridor sink and the grid's own coarseness.</summary>
        const float StationSink = 0.35f;
        /// <summary>
        /// How far past the barrier line the apron reaches, toward the road.
        ///
        /// Derived from the circuit's own width rather than typed. The road is
        /// 12 m on three circuits and 14 on the airfield, and a fixed number
        /// that lands the apron edge neatly outside a 12 m road's kerb lays it
        /// ON the kerb of a 14 m one — two coplanar tarmac surfaces fighting for
        /// the same pixels down a 36 m stretch. Forty centimetres of verge past
        /// the kerbstone, always.
        /// </summary>
        static float PadRoadOverlap => WallOffset - (RoadWidth * 0.5f + KerbWidth + 0.4f);
        /// <summary>
        /// How far the flattening fades out past the pad edge.
        ///
        /// Twenty metres, not the ten it reads like it wants. The far side of
        /// the forecourt sits well outside the road corridor, where Ridge Pass
        /// runs thirteen metres of relief — and a bench cut into that with a
        /// short blend meets the hillside as a cliff rather than as a bank.
        /// </summary>
        const float PadBlend = 20f;
        /// <summary>Half a driveway, in waypoints. Two is a 20 m opening —
        /// wide enough to turn into off the racing line without aiming.</summary>
        const int DrivewayHalf = 2;
        /// <summary>Waypoints from the forecourt's centre to each driveway, or
        /// ZERO for a single entrance on a lot too narrow to have two.</summary>
        static int DrivewayOffset;
        /// <summary>Half-width of each opening, in waypoints.</summary>
        static int DrivewaySpan = DrivewayHalf;

        static bool padActive;
        static int padIdx;
        static float padSide = 1f;
        static Vector3 padCentre;
        /// <summary>The pad's own frame: +Z toward the road, +X along it.</summary>
        static Vector3 padToRoad, padAlong;

        /// <summary>
        /// Work out where the forecourt goes and reserve the ground for it.
        /// Called BEFORE the terrain, the walls and the ground mesh, all three
        /// of which read the result.
        /// </summary>
        static void PlanFuelStop(List<Vector3> pts)
        {
            padActive = false;
            if (!theme.gasStation || pts == null || pts.Count < 12) return;

            // How big the station actually is, at the scale it will be built at.
            //
            // Sized off the lot's DIAGONAL, not its sides. The building is yawed
            // to face the road, so every tool that reasons about it from a
            // world-axis box — the placement's own ground sampling, and the
            // terrain audit's daylight rays — asks about ground at the corners
            // of a box up to 1.41x its footprint. Size the flat ground to the
            // sides and those corners land on the hillside outside the pad,
            // whose height is metres lower; the placement then "sets the
            // building into the ground" by three metres and buries a
            // seven-metre station up to its canopy. It did exactly that.
            var size = StationSize();
            float diagHalf = Mathf.Sqrt(size.x * size.x + size.y * size.y) * 0.5f;
            PadHalfAcross = Mathf.Max(16f, diagHalf + 3f);
            PadHalfDeep = Mathf.Max(diagHalf + 3f, (size.y + ForecourtApproach) * 0.5f);
            // The tarmac: the lot, plus eight metres of turning room either
            // side of it, and never wider than the ground that was flattened.
            ApronHalfAcross = Mathf.Min(PadHalfAcross, Mathf.Max(14f, size.x * 0.5f + 8f));

            padIdx = GasStationIndex(pts);
            padSide = 1f;
            Vector3 outward = RightAt(pts, padIdx) * padSide;
            padToRoad = -outward;
            padAlong = Vector3.Cross(Vector3.up, padToRoad).normalized;
            padCentre = pts[padIdx] + outward * (WallOffset - PadRoadOverlap + PadHalfDeep);
            // Where the barrier opens.
            //
            // An in and an out with a run of wall between them is what a
            // filling station beside a road actually has, and it matters for
            // more than looks: a single opening as wide as the whole apron is
            // sixty metres of missing barrier on a street circuit, which reads
            // as the wall having been forgotten and gives a car that ran wide
            // sixty metres of nothing to disappear into.
            //
            // Measured against the TARMAC, so a driveway never leads onto
            // grass. Two of them need room for a run of barrier between: with
            // less than that the pair would leave a single four-metre stub of
            // wall standing in the middle of the entrance, which reads as
            // damage rather than as design. A narrow lot gets one wide way in.
            int maxOffset = Mathf.Max(0, Mathf.FloorToInt(ApronHalfAcross / Spacing) - DrivewayHalf);
            int want = Mathf.RoundToInt(ApronHalfAcross * 0.62f / Spacing);
            if (want >= DrivewayHalf + 2 && maxOffset >= DrivewayHalf + 2)
            {
                DrivewayOffset = Mathf.Min(want, maxOffset);
                DrivewaySpan = DrivewayHalf;
            }
            else
            {
                DrivewayOffset = 0;
                DrivewaySpan = Mathf.Max(DrivewayHalf, maxOffset + DrivewayHalf);
            }
            padActive = true;
            Log($"Fuel stop planned at waypoint {padIdx}: station {size.x:0.0} x {size.y:0.0} m, " +
                $"ground flat over {PadHalfAcross * 2:0} x {PadHalfDeep * 2:0} m, " +
                $"apron {ApronHalfAcross * 2:0} m wide, " +
                (DrivewayOffset > 0
                    ? $"two {(DrivewaySpan * 2 + 1) * Spacing:0} m driveways at +/-{DrivewayOffset * Spacing:0} m."
                    : $"one {(DrivewaySpan * 2 + 1) * Spacing:0} m entrance."));
        }

        /// <summary>
        /// Objects in the station model that are the asset's own DISPLAY STAND
        /// rather than the building: painted backdrop planes carrying a city
        /// skyline, and the checkerboard the pack photographs its models on.
        ///
        /// They have to go before anything is measured, and they are the reason
        /// the first four builds of this produced a gas station the size of a
        /// bus shelter. The model is rescaled to a target HEIGHT, the backdrop
        /// is a 39 m painted wall, and scaling that down to seven metres takes
        /// the actual station down with it — to a fifth of its size, sitting in
        /// the middle of a forecourt built for the full one.
        /// </summary>
        static readonly string[] StationBackdrop = { "Background", "Checker" };

        /// <summary>
        /// How tall the model's <c>Fuel_pump</c> object stands, in metres.
        ///
        /// That object is the ONLY thing in this model whose real-world size is
        /// known, and it is the anchor everything else about the station's
        /// scale hangs off. Every other candidate was tried and every one of
        /// them was measuring something that is not the building: the raw
        /// bounds include a painted skyline 39 m tall, and stripping that still
        /// leaves a 300 x 143 m DIORAMA — the asset ships as a whole scene,
        /// with roads and hillsides and a treeline, not as a filling station.
        /// Scaling by any of those is how the station ended up a fifth of the
        /// size of its own forecourt.
        ///
        /// 2.2 rather than the 1.85 this shipped with. 1.85 is the height of a
        /// pump's BODY, and the object being measured is the whole dispenser
        /// including its price display — a Gilbarco Encore is 2.29 m over the
        /// head, a Wayne Ovation 2.2. Measuring the tall thing and calling it
        /// the short thing shrank the entire forecourt by a sixth, which is
        /// what the player was reporting when they said they felt eight feet
        /// tall looking over the pumps. Everything else on the forecourt — pad,
        /// apron, colliders, trigger volumes — is derived from this one number,
        /// so it is the only place the correction has to be made.
        /// </summary>
        const float PumpHeightM = 2.2f;

        /// <summary>How far from the pumps the station itself reaches. Thirty
        /// metres covers the shop behind them and the apron in front; past that
        /// is the diorama's own landscape, which this game has its own version
        /// of standing all around the circuit.</summary>
        const float StationKeepRadius = 30f;

        /// <summary>
        /// Throw away everything that is not the filling station.
        ///
        /// Leaf renderers only: a group node carrying a renderer AND children
        /// would take the children with it, and the children are exactly what
        /// this is trying to judge one at a time.
        /// </summary>
        static void TrimToStation(GameObject root, List<Transform> pumps)
        {
            if (pumps == null || pumps.Count == 0) return;
            var pb = PumpBounds(pumps);
            Vector3 hub = pb.center;
            // The pumps STAND on the forecourt, so the bottom of a pump is the
            // floor of the lot. It is the only reference in this model that is
            // certain, and both the trim and the placement hang off it.
            float floorY = pb.min.y;

            var doomed = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                if (r.GetComponentsInChildren<Renderer>().Length > 1) continue;   // a group

                // How far the mesh REACHES, not where its middle is. Judging by
                // the centre keeps an eighty-metre strip of road that happens to
                // run past the pumps, because its middle is right next to them —
                // which is how the "trimmed" lot came out 129 m across.
                var b = r.bounds;
                float reach = Mathf.Max(Mathf.Abs(b.center.x - hub.x) + b.extents.x,
                                        Mathf.Abs(b.center.z - hub.z) + b.extents.z);
                // And DOWNWARD, which the first version never checked. The
                // diorama is built on a landscape, and the hillside under the
                // lot passes the horizontal test easily — it is directly under
                // the pumps. Anything whose top is well below the forecourt
                // floor is that landscape.
                bool underneath = b.max.y < floorY - 1.5f;
                if (reach > StationKeepRadius || underneath) doomed.Add(r.gameObject);
            }
            foreach (var go in doomed)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>
        /// One station, stripped of its display backdrop, scaled off its own
        /// pumps and trimmed to the lot. The single place any of that happens —
        /// the measuring pass and the placing pass have to agree exactly, and
        /// two copies of this sequence would be two chances to disagree.
        /// </summary>
        static GameObject SpawnStation(string name, out List<Transform> pumps)
        {
            var root = new GameObject(name);
            foreach (var file in new[] { "Gas_station.fbx", "Gas_station_Props.fbx" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/GasStation/" + file);
                if (prefab == null) { Log("WARN: missing " + file); continue; }
                var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
                inst.name = Path.GetFileNameWithoutExtension(file);
                inst.transform.SetParent(root.transform, false);
            }
            StripBackdrop(root);

            pumps = FindPumps(root);
            float scale = 1f;
            if (pumps.Count > 0)
            {
                float h = PumpBounds(pumps).size.y;
                if (h > 0.01f) scale = PumpHeightM / h;
            }
            else
            {
                // No pumps to measure against. Fall back to the old rule, which
                // is wrong about this model but is at least the wrongness the
                // project already shipped.
                var raw = CombinedBounds(root);
                if (raw.size.y > 15f || raw.size.y < 2f)
                    scale = 7f / Mathf.Max(raw.size.y, 0.001f);
            }
            root.transform.localScale = Vector3.one * scale;

            TrimToStation(root, pumps);
            pumps = FindPumps(root);

            var lot = CombinedBounds(root);
            var pumpSpread = pumps.Count > 0 ? PumpBounds(pumps).size : Vector3.zero;
            Log($"Station: pumps scaled x{scale:0.000}, lot trims to " +
                $"{lot.size.x:0.0} x {lot.size.y:0.0} x {lot.size.z:0.0} m, " +
                $"{pumps.Count} pump(s) spread over " +
                $"{pumpSpread.x:0.0} x {pumpSpread.z:0.0} m.");
            return root;
        }

        static void StripBackdrop(GameObject root)
        {
            var doomed = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root.transform) continue;
                foreach (var name in StationBackdrop)
                {
                    if (!t.name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                    doomed.Add(t.gameObject);
                    break;
                }
            }
            foreach (var go in doomed)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        static Vector2 stationSize = Vector2.zero;

        /// <summary>
        /// The station's footprint in metres at the scale it is built at, split
        /// into the axis that ends up ACROSS the road and the one that ends up
        /// pointing away from it.
        ///
        /// Measured by instantiating the model, reading it and throwing it away.
        /// That looks wasteful and is the only honest way to know: the model is
        /// rescaled to a target HEIGHT, so its footprint is a consequence of a
        /// number in the .fbx that nobody here chose.
        ///
        /// WHICH axis is which comes from the pumps, exactly as the placement
        /// does — the vector from the middle of the lot to the middle of the
        /// pumps points at the road once the building is turned, so the model
        /// axis nearest to it is the depth axis. Taking the larger of the two
        /// for both (the first version) made the pad square, which for a lot
        /// that is 54 m wide and 30 m deep meant forty metres of flattened
        /// hillside and forty metres of tarmac that nothing stands on.
        ///
        /// Cached: it is the same building on all six circuits, and the answer
        /// is in the model's own frame, so it does not depend on the track.
        /// </summary>
        static Vector2 StationSize()
        {
            if (stationSize != Vector2.zero) return stationSize;

            var probe = SpawnStation("StationProbe", out var pumps);
            var b = CombinedBounds(probe);

            float across, deep;
            if (pumps.Count > 0)
            {
                Vector3 face = PumpBounds(pumps).center - b.center;
                bool depthIsX = Mathf.Abs(face.x) >= Mathf.Abs(face.z);
                deep = depthIsX ? b.size.x : b.size.z;
                across = depthIsX ? b.size.z : b.size.x;
            }
            else
            {
                across = deep = Mathf.Max(b.size.x, b.size.z);
            }
            UnityEngine.Object.DestroyImmediate(probe);

            if (across < 4f || across > 200f || deep < 4f || deep > 200f)
            {
                Log($"WARN: station footprint measured {across:0.0} x {deep:0.0} m — clamping.");
                across = Mathf.Clamp(across, 24f, 70f);
                deep = Mathf.Clamp(deep, 20f, 60f);
            }
            stationSize = new Vector2(across, deep);
            return stationSize;
        }

        /// <summary>
        /// The forecourt is flat ACROSS the road and follows the road ALONG it.
        ///
        /// A single height would be the obvious choice and it is wrong: the pad
        /// reaches to the tarmac edge, and inside the corridor the ground is
        /// pinned dead level with a road that climbs. One constant height there
        /// would step against the racing line by however much the circuit
        /// gained over the length of the forecourt — a metre on Ridge Pass.
        /// Following the road's own height along one axis and holding it across
        /// the other is what a real forecourt cut into a slope does anyway.
        /// </summary>
        static float PadHeightAt(float along)
        {
            var pts = terrainPts;
            int n = pts != null ? pts.Count : 0;
            if (n == 0) return 0f;
            float f = padIdx + along / Spacing;
            int a = Mathf.FloorToInt(f);
            float t = f - a;
            int i0 = Loop ? ((a % n) + n) % n : Mathf.Clamp(a, 0, n - 1);
            int i1 = Loop ? ((a + 1) % n + n) % n : Mathf.Clamp(a + 1, 0, n - 1);
            return Mathf.Lerp(terrainGroundY[i0], terrainGroundY[i1], t);
        }

        /// <summary>
        /// The forecourt surface: the road's own profile at the kerb, graded to
        /// dead level by the time it reaches the building.
        ///
        /// Both halves are load-bearing. At the road edge it MUST follow the
        /// road, or the apron steps against the racing line by whatever the
        /// circuit gains over sixty metres — the thing the player would feel.
        /// Under the building it must be FLAT, because the building's floor is,
        /// and a flat floor on a graded pad is daylight at one end and buried
        /// brickwork at the other — the thing the player would see. The change
        /// happens across the approach strip, where there is nothing standing
        /// and a gentle twist reads as a graded lot.
        /// </summary>
        static float PadSurfaceY(float along, float deep)
        {
            float t = Mathf.Clamp01(Mathf.InverseLerp(
                PadHalfDeep, PadHalfDeep - ForecourtApproach * 0.8f, deep));
            return Mathf.Lerp(PadHeightAt(along), PadHeightAt(0f), Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>Pad-local coordinates of a world XZ point: x along the road,
        /// z toward it.</summary>
        static void PadLocal(float x, float z, out float along, out float deep)
        {
            float dx = x - padCentre.x, dz = z - padCentre.z;
            along = dx * padAlong.x + dz * padAlong.z;
            deep = dx * padToRoad.x + dz * padToRoad.z;
        }

        /// <summary>0 outside the forecourt, 1 on it, smooth between.</summary>
        static float PadWeight(float along, float deep)
        {
            float wa = 1f - Mathf.InverseLerp(PadHalfAcross, PadHalfAcross + PadBlend, Mathf.Abs(along));
            float wd = 1f - Mathf.InverseLerp(PadHalfDeep, PadHalfDeep + PadBlend, Mathf.Abs(deep));
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(wa, wd));
        }

        /// <summary>Is this world point on the forecourt (plus a margin)?
        /// Scenery asks before it plants a tree in the middle of it.</summary>
        static bool OnFuelPad(Vector3 p, float margin)
        {
            if (!padActive) return false;
            PadLocal(p.x, p.z, out float along, out float deep);
            return Mathf.Abs(along) < PadHalfAcross + margin &&
                   Mathf.Abs(deep) < PadHalfDeep + margin;
        }

        /// <summary>Is this waypoint inside a driveway? One opening centred on
        /// the forecourt when <see cref="DrivewayOffset"/> is zero, otherwise a
        /// matching pair either side of it.</summary>
        static bool InWallGap(int idx, int n)
        {
            if (!padActive || n == 0) return false;
            int d = Mathf.Abs(idx - padIdx);
            if (Loop) d = Mathf.Min(d, n - d);
            return Mathf.Abs(d - DrivewayOffset) <= DrivewaySpan;
        }

        /// <summary>
        /// How high the street curb stands at a waypoint, per side: the full
        /// <see cref="StreetKerbHeight"/> nearly everywhere, ZERO across each
        /// forecourt driveway, and a one-station ramp between the two. Zero
        /// on any venue that is not <see cref="KerbStyle.Street"/>.
        ///
        /// A curb that ran unbroken across the filling station's entrance is
        /// the bug the home street had ("AND IT IS DROPPED AT EVERY DRIVE"):
        /// a car leaving the race for fuel would mount a 15 cm step on the
        /// way in and again on the way out. The openings are exactly the
        /// wall's (<see cref="InWallGap"/>), so the curb drops where the
        /// barrier does. Ramped over ONE station (4 m) rather than cut
        /// square: 0.15 m over 4 m is 3.75%, invisible to AuditSurface's
        /// 0.12-per-0.35 m rule, where a square cut is a 0.15 m step along
        /// the edge lane and fails it.
        ///
        /// Read by BuildKerbs for the strip AND by CircuitEdgeProfile for the
        /// run-off behind it, so the two can never disagree about the height
        /// of the pavement's back edge.
        /// </summary>
        static float KerbLiftAt(int idx, int n, float side)
        {
            if (KerbStyleFor(track) != KerbStyle.Street) return 0f;
            if (!padActive || n == 0 || side != padSide) return StreetKerbHeight;
            int d = Mathf.Abs(idx - padIdx);
            if (Loop) d = Mathf.Min(d, n - d);
            // Stations outside the nearest opening: 0 inside it (InWallGap's
            // own test), 1 at the first station past its edge. The mesh
            // interpolates the 4 m between — that is the ramp.
            int outside = Mathf.Abs(d - DrivewayOffset) - DrivewaySpan;
            return StreetKerbHeight * Mathf.Clamp01(outside);
        }

        static void PlaceGasStation(List<Vector3> pts, Transform parent)
        {
            if (!padActive) return;
            int idx = padIdx;

            var root = SpawnStation("GasStation", out var pumps);
            root.transform.SetParent(parent, false);
            ConvertToPSXMaterials(root);

            // ---- turn it so the PUMPS face the road ----
            //
            // The model's own idea of forward is a Blender export convention
            // nobody here chose, and the old code simply pointed its +Z at the
            // road and hoped. Half of a forecourt is a shop with no doors on
            // the back of it; which half is which is answered by where the
            // pumps are, and the pumps say so in their names.
            root.transform.rotation = Quaternion.LookRotation(padToRoad, Vector3.up);
            if (pumps.Count > 0)
            {
                var bounds0 = CombinedBounds(root);
                Bounds pb = PumpBounds(pumps);
                Vector3 face = pb.center - bounds0.center;
                face.y = 0f;
                if (face.sqrMagnitude > 1f)
                {
                    float fix = Vector3.SignedAngle(face.normalized, padToRoad, Vector3.up);
                    root.transform.rotation = Quaternion.Euler(0f, fix, 0f) * root.transform.rotation;
                }
            }
            else Log("WARN: no Fuel_pump objects in the station model — no pumps on this circuit.");

            // ---- park it at the BACK of the pad ----
            //
            // Depth from the MEASURED footprint, never from the world bounding
            // box. The station is yawed to face the road and its box is
            // world-axis: on a circuit whose forecourt faces a diagonal, that
            // box reports the lot's 60 m diagonal as its depth, the placement
            // "sets it back" by a negative distance, and a station meant to sit
            // at the back of its apron ends up jammed against the kerb with
            // seventy metres of empty tarmac behind it.
            var lot = StationSize();
            var bounds = CombinedBounds(root);
            float depth = lot.y * 0.5f;
            Vector3 want = padCentre - padToRoad * (PadHalfDeep - depth - 1.5f);
            // The LOWEST ground under its own footprint, then a little further
            // in — the same rule buildings are set into a hill with, and for
            // the same reason: these meshes have no floor, so a base level with
            // the ground at the middle is one you can see under from a corner.
            // Sampled in the PAD's frame over the real footprint, so every
            // sample lands on ground the pad actually flattened.
            float ground = LowestGroundUnder(new Vector3(want.x, 0f, want.z),
                                             Quaternion.LookRotation(padToRoad, Vector3.up),
                                             lot.x * 0.5f, depth) - StationSink;

            // Sit the FORECOURT FLOOR on the ground, not the bottom of the
            // bounding box. Those are the same thing only if nothing in the
            // model hangs below the lot — and this model is a diorama built on
            // a hillside, so something always did. Aligning the box put the
            // whole station ten metres into the air, hovering over its own
            // apron, which is exactly how it shipped and exactly how it looked.
            // The pumps stand on the floor; their base IS the floor.
            float floorY = pumps.Count > 0 ? PumpBounds(pumps).min.y : bounds.min.y;
            Log($"Station floor sits {floorY - bounds.min.y:0.0} m above the bottom of its " +
                $"bounding box; placing on the floor.");

            root.transform.position += new Vector3(want.x - bounds.center.x,
                                                   ground - floorY,
                                                   want.z - bounds.center.z);

            BuildApron(root.transform.parent);
            BuildStationColliders(root, pumps);
            BuildPumps(root, pumps);

            Log($"Fuel stop built at waypoint {idx}: {pumps.Count} pump(s), " +
                $"{2f * PadHalfDeep - 2f * depth - 1.5f:0.0} m of open apron " +
                "between the kerb and the front of the lot.");
        }

        /// <summary>Every object in the station model whose name marks it as a
        /// pump. The pack calls them Fuel_pump, Fuel_pump_01 and so on, in both
        /// the building file and the props file.</summary>
        static List<Transform> FindPumps(GameObject root)
        {
            var found = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                if (t == root.transform) continue;
                if (!t.name.StartsWith("Fuel_pump", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.GetComponentInChildren<MeshRenderer>() == null) continue;
                // Only the outermost of a nest — a pump made of six named parts
                // is one pump, not six.
                bool nested = false;
                for (var p = t.parent; p != null && p != root.transform; p = p.parent)
                    if (p.name.StartsWith("Fuel_pump", StringComparison.OrdinalIgnoreCase)) nested = true;
                if (!nested) found.Add(t);
            }
            return found;
        }

        static Bounds PumpBounds(List<Transform> pumps)
        {
            var b = CombinedBounds(pumps[0].gameObject);
            for (int i = 1; i < pumps.Count; i++)
                b.Encapsulate(CombinedBounds(pumps[i].gameObject));
            return b;
        }

        /// <summary>Metres the apron stands over the ground it is laid on, and
        /// over a shoulder it covers.</summary>
        const float ApronLiftM = 0.03f;
        /// <summary>The least it may stand over the ground anywhere, grading
        /// included: the ground mesh must never show through the tarmac.</summary>
        const float ApronGroundClearM = 0.01f;
        /// <summary>The steepest the apron grades from the edge that meets the
        /// road's shoulder to the pad: 1V:12H, a driveway apron's slope.</summary>
        const float ApronGrade = 1f / 12f;
        /// <summary>How far inside the run-off's own end the apron's road-side
        /// row is brought (<see cref="ApronRowOntoShoulder"/>): enough that the
        /// straight edge between two row vertices a station apart stays on the
        /// run-off across a bend, where the run-off's end is a curve.</summary>
        const float ApronRowInsetM = 0.3f;

        /// <summary>
        /// THE FORECOURT'S ROAD EDGE LIES ON THE RUN-OFF, ALL THE WAY ALONG.
        ///
        /// The apron is a straight grid in the pad's frame, its road-side row
        /// PadRoadOverlap inside the barrier line at the pad's own station. On
        /// a bend the road curves away from a straight row, and on City
        /// Circuit's forecourt (pad on the outside of a ~130 m bend) the row
        /// was 1.3 m past the tarmac edge at the pad and 4.7 m past it at the
        /// far driveway's last station — beyond the run-off's end (3.95 m).
        /// There its vertices could not meet the shoulder, stood on the
        /// run-off's plane carried on, and the edge between them crossed the
        /// run-off's toe tuck in open air: read off the saved meshes, the
        /// apron's rim 0.27 m over the tuck beside it at wp 188 R, which the
        /// obstacle audit failed as a 0.21 m EDGE FACE onto the Forecourt at
        /// 4.30 m. Sliding each such row vertex toward the road until it is on
        /// the run-off (<see cref="ApronRowInsetM"/> inside its end) lets it
        /// MEET the shoulder like every other row vertex, so the edge is flush
        /// from one driveway to the other and the tuck is under tarmac.
        ///
        /// Along the pad's own road-ward axis, so the grid keeps its shape. The
        /// row is the grid's road-most, so the slide only ever opens its last
        /// cell (it cannot fold over the row behind it); half a cell is a bound
        /// on a bend so sharp that the row would otherwise chase the run-off
        /// round it.
        /// </summary>
        static Vector3 ApronRowOntoShoulder(List<Vector3> pts, Vector3 p, float cellDeep)
        {
            float moved = 0f, most = cellDeep * 0.5f;
            for (int it = 0; it < 4; it++)
            {
                if (!ShoulderDesignAt(pts, p.x, p.z, out _, out float e, out float eLast)) break;
                float over = e - (eLast - ApronRowInsetM);
                if (over <= 0.005f || e <= 0f) break;
                float step = Mathf.Min(over, most - moved);
                if (step <= 0f) break;
                p += padToRoad * step;
                moved += step;
            }
            return p;
        }

        /// <summary>
        /// The tarmac. On the ROAD layer, because the wheels tell tarmac from
        /// grass by layer and a forecourt the car slides across at field grip
        /// is one nobody can stop on.
        ///
        /// It MEETS THE SHOULDER. Laid at the ground plus three centimetres,
        /// its road edge was the roadbed dig — 0.42 m under the tarmac — and
        /// the old batter crossed it at 8.8 m: every driveway on every circuit
        /// was a 34 cm bowl in and out. Now:
        ///
        ///   * its edge vertices that lie on the run-off (the road-side row,
        ///     and the side columns inside the wall line) are set to the
        ///     run-off's own height, so the car rolls from one to the other,
        ///     and the first side-column vertex past the run-off's end stays
        ///     on that plane;
        ///   * the vertices over the rest of the run-off, and one apron cell
        ///     past its toe, are held ApronLiftM above it, so the run-off —
        ///     whose end is open across a driveway, where there is no wall to
        ///     hide it — is always under the tarmac and never pokes through
        ///     (a centimetre and a half of it can, along the road-side row
        ///     where a street curb's one-station drop kinks the run-off
        ///     between two apron vertices);
        ///   * everything else is the pad as before, graded no steeper than
        ///     1V:12H away from that edge (a cone out of the edge vertices, then
        ///     lifted until no two neighbours differ by more).
        /// </summary>
        static void BuildApron(Transform parent)
        {
            const int cells = 16;
            int stride = cells + 1;
            var pts = terrainPts;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var y = new float[stride * stride];
            var least = new float[y.Length];
            var meets = new bool[y.Length];
            float cellAcross = 2f * ApronHalfAcross / cells, cellDeep = 2f * PadHalfDeep / cells;
            // A whole cell's diagonal past the toe: every apron triangle that
            // covers any of the tuck or the skirt then has all three corners
            // held, whichever way the pad frame sits against the road.
            float holdPast = RoadsideRules.ToeTuckRunM + ShoulderSkirtRunM +
                             Mathf.Sqrt(cellAcross * cellAcross + cellDeep * cellDeep);
            float rimHoldPast = RoadsideRules.ToeTuckRunM + ShoulderSkirtRunM + Mathf.Max(cellAcross, cellDeep);
            int met = 0, held = 0;

            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                {
                    int v = j * stride + i;
                    float along = Mathf.Lerp(-ApronHalfAcross, ApronHalfAcross, i / (float)cells);
                    float deep = Mathf.Lerp(-PadHalfDeep, PadHalfDeep, j / (float)cells);
                    Vector3 p = padCentre + padAlong * along + padToRoad * deep;
                    if (j == cells && pts != null) p = ApronRowOntoShoulder(pts, p, cellDeep);
                    // Three centimetres proud of THE GROUND AT THIS POINT, not
                    // of the pad's own flat plane. The two are the same
                    // everywhere the pad is at full strength, and they are not
                    // the same at its edges, where the flattening is fading out.
                    float ground = GroundHeightAt(p.x, p.z);
                    y[v] = ground + ApronLiftM;
                    least[v] = ground + ApronGroundClearM;
                    // deep = +PadHalfDeep (j == cells) is the edge toward the road.
                    if (pts != null && ShoulderDesignAt(pts, p.x, p.z, out float sy, out float e, out float eLast))
                    {
                        bool rim = j == cells || i == 0 || i == cells;
                        if (rim && e <= eLast)
                        {
                            y[v] = sy;
                            meets[v] = true;
                            met++;
                        }
                        else if (rim && e <= eLast + rimHoldPast)
                        {
                            // The first edge vertex past the run-off's end
                            // stays on its plane (no lift: it is an edge), or
                            // the edge from the last vertex that meets it would
                            // cut under the run-off — 9-16 cm of run-off stood
                            // through the apron's corner in the offline replica.
                            y[v] = Mathf.Max(y[v], sy);
                            least[v] = Mathf.Max(least[v], sy);
                        }
                        else if (!rim && e <= eLast + holdPast)
                        {
                            y[v] = Mathf.Max(y[v], sy + ApronLiftM);
                            least[v] = Mathf.Max(least[v], sy + ApronLiftM);
                            held++;
                        }
                    }
                    verts.Add(p);
                    uvs.Add(new Vector2(along / 8f, deep / 8f));
                }

            // Never climb away from the edge that meets the shoulder faster
            // than the grade: a cone down out of every such vertex caps the
            // rest (except where that would put tarmac under the ground).
            for (int v = 0; v < y.Length; v++)
            {
                if (meets[v]) continue;
                float cap = float.MaxValue;
                for (int f = 0; f < y.Length; f++)
                {
                    if (!meets[f]) continue;
                    float dx = (v % stride - f % stride) * cellAcross;
                    float dz = (v / stride - f / stride) * cellDeep;
                    cap = Mathf.Min(cap, y[f] + Mathf.Sqrt(dx * dx + dz * dz) * ApronGrade);
                }
                if (cap < y[v]) y[v] = Mathf.Max(cap, least[v]);
            }
            // ...and never fall away faster either: lift any vertex its
            // neighbours are above by more than the grade. Only ever raises, so
            // it cannot put the tarmac under the ground or under the shoulder.
            for (int pass = 0; pass < 4 * stride; pass++)
            {
                bool changed = false;
                for (int v = 0; v < y.Length; v++)
                {
                    if (meets[v]) continue;
                    int vi = v % stride, vj = v / stride;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = vi + di, nj = vj + dj;
                            if ((di == 0 && dj == 0) || ni < 0 || nj < 0 || ni >= stride || nj >= stride) continue;
                            float dx = di * cellAcross, dz = dj * cellDeep;
                            float want = y[nj * stride + ni] - Mathf.Sqrt(dx * dx + dz * dz) * ApronGrade;
                            if (want > y[v] + 1e-4f) { y[v] = want; changed = true; }
                        }
                }
                if (!changed) break;
            }
            for (int v = 0; v < y.Length; v++)
            {
                var p = verts[v];
                p.y = y[v];
                verts[v] = p;
            }
            Log($"Forecourt apron: {met} edge vertices meet the shoulder, {held} held over it, " +
                $"graded no steeper than 1:{1f / ApronGrade:0} away from it.");

            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int v = j * (cells + 1) + i;
                    tris.AddRange(new[] { v, v + cells + 1, v + cells + 2, v, v + cells + 2, v + 1 });
                }

            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            SaveMesh(mesh, "ApronMesh");

            var go = new GameObject("Forecourt");
            go.transform.SetParent(parent, false);
            go.layer = RoadLayer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // NOT Asphalt.jpg. That one is a photograph of tarmac WITH the
            // painted kerb line along its bottom edge, so tiled every eight
            // metres it lays a yellow stripe across the whole forecourt — the
            // same trap Concrete.jpg sets on a floor with its skirting board.
            // These are surface photographs complete with their trim.
            go.AddComponent<MeshRenderer>().sharedMaterial =
                MakeMat(MeshPrefix + "Forecourt",
                        Root + "/Art/GasStation/Textures/AsphaltDamaged.jpg", affine: 0f,
                        wet: WetAsphalt);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
        }

        /// <summary>
        /// Solid where a THING is and open everywhere else.
        ///
        /// The previous shape was one box from the pump line to the back of
        /// the lot. It stopped cars ending up in the shop, and it also filled
        /// every open yard of concrete beside and behind the building with
        /// invisible wall — which nobody noticed from a car and everybody hit
        /// the moment the forecourt became somewhere you walk ("invisible
        /// walls stop me from walking into areas like one of the gas
        /// stations"). Each piece of the model now carries its own local-space
        /// box: the shop stops you at its walls, the canopy columns at the
        /// columns, and the concrete between them is concrete.
        ///
        /// Returns the SHOP cluster's world bounds — the tall, wide pieces —
        /// so the caller can stand a StoreDoor at its face.
        /// </summary>
        static Bounds AddStationPieceColliders(GameObject root, List<Transform> pumps)
        {
            // Doors on their hinges first, so the piece pass below finds them
            // already collided and leaves them alone: the 6TWELVE models real
            // doors on a real interior, and a shop you can walk into is the
            // whole reason the colliders went piece-by-piece. The leaves used
            // to be DISABLED here, which is what "the doors are missing to
            // Pizzeria and Convenience store" was — they swing now.
            WorldKit.HingeDoors(root);

            var lotBounds = CombinedBounds(root);
            float floorY = pumps.Count > 0 ? PumpBounds(pumps).min.y : lotBounds.min.y;

            var shopBounds = new Bounds();
            bool haveShop = false;

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                if (r.GetComponentsInChildren<Renderer>().Length > 1) continue;   // a group

                var b = r.bounds;
                // Ground clutter you can step over, and the canopy overhead
                // you drive under. The 2.05 m headroom line is above the
                // walker's eye and above every car in the game.
                if (b.size.y < 0.35f) continue;
                if (b.min.y > floorY + 2.05f) continue;

                if (r.GetComponent<Collider>() == null)
                {
                    // The MESH, not a box. A box was right until somebody
                    // walked here: this pack draws a pump island's two canopy
                    // legs as ONE object eleven metres long, so its box was an
                    // eleven-metre invisible wall straight through the pump
                    // line — and the shop is one mesh whose box filled the
                    // whole store, doorway, aisles and all. These are 8-500
                    // vert meshes; a static MeshCollider each is nothing, and
                    // it is the difference between colliding with the model
                    // and colliding with a rumour of it. (This also covers
                    // the pumps themselves, which used to get bespoke
                    // world-axis island boxes from the caller — a yawed
                    // forecourt inflated those into their own diagonals.)
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    else
                        r.gameObject.AddComponent<BoxCollider>();
                }
                r.gameObject.layer = SolidLayer;

                // The building: tall and wide. Everything else is furniture.
                if (b.size.y > 2.3f && Mathf.Min(b.size.x, b.size.z) > 2.5f)
                {
                    if (!haveShop) { shopBounds = b; haveShop = true; }
                    else shopBounds.Encapsulate(b);
                }
            }

            // The pack's trees came through the pass above like any other
            // tall piece and got a MeshCollider of their CARDS — a solid wall
            // the size of each crown round the forecourt. Trunks instead.
            int trunks = TreeKit.PlantTrunks(root);
            if (trunks > 0) Log($"Station: {trunks} pack tree(s) stood on trunks, card colliders off.");

            return haveShop ? shopBounds : lotBounds;
        }

        /// <summary>Where the shop door is, for somebody on foot: the face of
        /// the shop cluster that looks at the pumps, a step out onto the
        /// forecourt. The asset has no identifiable door object, and a marker
        /// that is merely NEAR the shop is a better answer than one that is
        /// exactly on a mesh chosen by guesswork.</summary>
        static void PlaceStoreDoor(Transform parent, Bounds shop, List<Transform> pumps,
                                   float eyeY)
        {
            Vector3 toward = pumps.Count > 0
                ? PumpBounds(pumps).center - shop.center : Vector3.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f) toward = Vector3.forward;
            toward.Normalize();
            float reach = Mathf.Abs(toward.x) * shop.extents.x +
                          Mathf.Abs(toward.z) * shop.extents.z;
            var storeDoor = new GameObject("StoreDoor");
            storeDoor.transform.SetParent(parent, false);
            Vector3 doorAt = shop.center + toward * (reach + 1.4f);
            doorAt.y = eyeY;
            storeDoor.transform.position = doorAt;
        }

        /// <summary>The circuit wiring: piece colliders and the shop door.
        /// The pumps are collided by their own meshes inside the piece pass
        /// now — the old world-axis island boxes inflated into their diagonals
        /// on any forecourt that faced a yawed road, and stood invisible walls
        /// where a person plainly fits.</summary>
        static void BuildStationColliders(GameObject root, List<Transform> pumps)
        {
            var shop = AddStationPieceColliders(root, pumps);
            PlaceStoreDoor(root.transform.parent, shop, pumps,
                GroundHeightAt(shop.center.x, shop.center.z) + 1.2f);
        }

        /// <summary>
        /// A drive-up volume per pump. Generous — you park BESIDE a pump, not
        /// on it, and a trigger tight to the bodywork of the pump would be one
        /// nobody ever entered.
        /// </summary>
        static void BuildPumps(GameObject root, List<Transform> pumps)
        {
            foreach (var pump in pumps)
            {
                var pb = CombinedBounds(pump.gameObject);
                // Height from the ground under THIS pump. The forecourt follows
                // the circuit's gradient, so one height for a row of pumps would
                // bury the trigger at one end and float it at the other.
                var go = new GameObject("Pump");
                go.transform.SetParent(root.transform.parent, false);
                go.transform.position = new Vector3(
                    pb.center.x, GroundHeightAt(pb.center.x, pb.center.z) + 1.1f, pb.center.z);
                go.transform.rotation = Quaternion.LookRotation(padToRoad, Vector3.up);
                var trigger = go.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(7f, 3f, 5.5f);
                go.AddComponent<GasPump>();
            }
        }

        static void PlaceTrees(List<Vector3> pts, Transform parent)
        {
            // Crossed-quad billboard mesh
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int q = 0; q < 2; q++)
            {
                Quaternion rot = Quaternion.Euler(0f, q * 90f, 0f);
                int v = verts.Count;
                float hw = TreeCardW * 0.5f;
                verts.Add(rot * new Vector3(-hw, 0f, 0f));
                verts.Add(rot * new Vector3(-hw, TreeCardH, 0f));
                verts.Add(rot * new Vector3(hw, TreeCardH, 0f));
                verts.Add(rot * new Vector3(hw, 0f, 0f));
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
                // One winding — see the note in BuildWalls — and the MATERIAL
                // draws both faces. This comment used to say each plane "is
                // still visible from both sides via the other plane of the
                // cross", and it is not: under Cull Back a one-sided X is two
                // planes from one quarter of the compass, ONE flat card from
                // two more, and nothing at all from behind both. "Some trees
                // are still 2 dimensions and flat" was exactly that.
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            SaveMesh(mesh, "TreeMesh");

            var mat = MakeMat(MeshPrefix + "Tree", theme.tree, cutoff: 0.5f, twoSided: true);
            var rng = new System.Random(7);
            int n = pts.Count, placed = 0;
            // What a rank behind the first must stay clear of: every OTHER
            // part of the circuit (25 m out from one straight is the middle of
            // the next on a hairpin) and every building already standing.
            var buildings = new List<Bounds>();
            foreach (Transform child in parent)
                if (child.name == "Building")
                {
                    var bb = CombinedBounds(child.gameObject);
                    bb.Expand(new Vector3(5f, 40f, 5f));
                    buildings.Add(bb);
                }
            float clearOfRoad = WallOffsetFor(track) + 2.2f;
            bool Clear(Vector3 at)
            {
                foreach (var bb in buildings) if (bb.Contains(new Vector3(at.x, bb.center.y, at.z))) return false;
                for (int k = 0; k < n; k++)
                {
                    float dx = pts[k].x - at.x, dz = pts[k].z - at.z;
                    if (dx * dx + dz * dz < clearOfRoad * clearOfRoad) return false;
                }
                return true;
            }

            for (int i = 4; i < n; i += theme.treeEvery)
            {
                float firstSide = (i / theme.treeEvery) % 2 == 0 ? -1f : 1f;
                if (rng.NextDouble() < theme.treeSkip || OverGorge(i)) continue;
                for (int sideIx = 0; sideIx < (theme.treeBothSides ? 2 : 1); sideIx++)
                for (int rank = 0; rank < Mathf.Max(1, theme.treeRows); rank++)
                {
                    float side = sideIx == 0 ? firstSide : -firstSide;
                    // Ranks behind the first stand 5-8 m further out each and
                    // half a site along, so the wood has depth instead of rows.
                    int at = rank == 0 ? i : Mathf.Min(n - 1, i + (rank % 2 == 1 ? theme.treeEvery / 2 : 0));
                    // The roadside rank draws exactly what it always drew, in
                    // the order it always drew it, so the trees a circuit
                    // already had stand where and as they stood.
                    float out_ = WallOffsetFor(track) + 2.6f, along = 0f;
                    if (rank > 0)
                    {
                        out_ += rank * (5f + (float)rng.NextDouble() * 3f);
                        along = ((float)rng.NextDouble() - 0.5f) * 5f;
                    }
                    Vector3 right = RightAt(pts, at);
                    Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
                    Vector3 treeAt = pts[at] + right * side * out_ + fwd * along;
                    // Not through the forecourt; and a back rank not on another
                    // part of the circuit nor through a building.
                    if (OnFuelPad(treeAt, 2f)) continue;
                    if ((rank > 0 || sideIx > 0) && !Clear(treeAt)) continue;
                    float yaw = (float)rng.NextDouble() * 360f;
                    float s = 0.8f + (float)rng.NextDouble() * 0.5f;
                    if (rank > 0) s *= 1.05f + rank * 0.12f;      // the wood behind is older than the verge

                    var t = new GameObject("Tree");
                    t.transform.SetParent(parent, false);
                    // Sunk 20 cm. A billboard whose base is exactly on a facet edge
                    // shows a sliver of sky under itself the moment the ground tips.
                    treeAt.y = GroundHeightAt(treeAt.x, treeAt.z) - 0.2f;
                    t.transform.position = treeAt;
                    t.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    t.transform.localScale = new Vector3(s, s, s);
                    t.AddComponent<MeshFilter>().sharedMesh = mesh;
                    t.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    // The trunk: "trees should also occupy space with their
                    // trunks, stopping a car if it drives into it." Where the two
                    // planes cross, which is where the sheet paints it, sized off
                    // the card (the painted trunk is ~5% of the sheet's width) and
                    // solid to half the tree's height — well over any roof, and
                    // clear of nothing, because nothing is meant to pass under a
                    // tree. On the Solid layer so no wheel ever stands on it.
                    TreeKit.AddTrunk(t.transform, treeAt, TreeKit.TrunkRadiusFor(TreeCardW * s),
                                     Mathf.Min(4f, 0.5f * TreeCardH * s));
                    placed++;
                }
            }
            Log($"Placed {placed} trees, each with a trunk.");
        }

        /// <summary>The circuit tree's card, metres at scale 1.</summary>
        const float TreeCardW = 5.2f, TreeCardH = 6.5f;

        // ------------------------------------------------------------------
        //  Lighting / sky
        // ------------------------------------------------------------------
        static GameObject BuildLighting()
        {
            // The scene is BAKED at sunset — the hour the game shipped with and
            // still its default — and TimeOfDay.Apply moves it at runtime.
            // Taking the numbers from the same table means a scene opened in the
            // editor looks like the game rather than like an earlier draft of it.
            var hour = TimeOfDay.At(TimeOfDay.Sunset);

            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = hour.sunColor;
            light.intensity = hour.sunIntensity;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(hour.sunEuler);

            var globals = go.AddComponent<PSXGlobals>();
            globals.sun = light;
            globals.ambient = hour.ambient;
            globals.fogColor = hour.fogColor;
            // The stage lives at mountain scale: the same hour table, seen
            // four times further. TimeOfDay.Apply reads the scale back at
            // runtime, so the seven hours all stretch with the venue.
            globals.fogScale = track != null && track.stage ? theme.fogScale : CircuitFogScale;
            globals.fogNear = hour.fogNear * globals.fogScale;
            globals.fogFar = hour.fogFar * globals.fogScale;
            var skyShader = Shader.Find("PSX/Sky");
            if (skyShader != null)
            {
                string p = MatDir + "/Sky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (sky == null)
                {
                    sky = new Material(skyShader);
                    AssetDatabase.CreateAsset(sky, p);
                }
                sky.shader = skyShader;
                sky.SetColor("_TopColor", hour.skyTop);
                sky.SetColor("_HorizonColor", hour.skyHorizon);
                sky.SetColor("_BottomColor", hour.skyBottom);
                sky.SetFloat("_HorizonSharpness", hour.skySharpness);
                // The panorama, baked in as well as applied at runtime. The
                // asset is what an editor scene, a screenshot pass and the
                // first frame after a load all render with — TimeOfDay.Apply
                // only runs once somebody has chosen an hour, and until then
                // the material on disk IS the sky.
                var pano = SkyPanoramaFor(hour);
                sky.SetTexture("_MainTex", pano);
                sky.SetFloat("_PanoAmount", pano != null ? 1f : 0f);
                sky.SetFloat("_Rotation", BakedSkyRotation(hour));
                sky.SetFloat("_Tint", hour.skyTint);
                sky.SetFloat("_Exposure", Mathf.Max(0.01f, hour.skyExposure));
                sky.SetFloat("_Stars", hour.skyStars);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }
            RenderSettings.fog = false; // PSX/Lit does its own fog
            return go;
        }

        // ------------------------------------------------------------------
        //  Cars
        // ------------------------------------------------------------------
        /// <summary>
        /// The standalone demo grid. Pressing Play on CityCircuit still gives
        /// the FD the handling was tuned against, but the three opponents now
        /// wear different shells — which is the fastest way to see at a glance
        /// that the model library baked and fitted correctly, without going
        /// through the LifeSim menus to start a real race.
        /// </summary>
        static readonly (string name, string model, string skin, float skill, float offset)[] CarSetups =
        {
            ("RX-7 Player", "rx7_fd",      "silver_tornado_silver", 0f,    0f),
            ("Skyline AI",  "skyline_r32", "rpm_red",               1.00f, -1.6f),
            ("Supra AI",    "supra_a80",   "midnight_purple",       0.95f,  1.6f),
            ("Charger AI",  "charger_69",  "go_mango",              0.90f, -0.8f),
        };

        static List<CarController> BuildCars(List<Vector3> pts)
        {
            var physMat = GetOrCreatePhysMat("CarPhys", CarSlideFriction, 0.05f);
            var blobMat = MakeBlobShadowMaterial();

            var cars = new List<CarController>();
            var carsRoot = new GameObject("Cars");

            for (int c = 0; c < CarSetups.Length; c++)
            {
                var setup = CarSetups[c];
                bool isPlayer = c == 0;

                // Grid: player at the back of a 2x2 grid, staggered.
                //
                // The row is found by walking BACK ALONG THE PATH, not by
                // extrapolating the tangent at the start line. A straight-line
                // projection is only correct when the line sits on a straight,
                // and on three of the four circuits it does not — the polar
                // layouts start at their easternmost point, which on an
                // elongated oval is the apex of a hairpin. Extrapolating 28.5 m
                // backwards from there put the whole grid on the far side of
                // the barrier, in a spot with no way back onto the road.
                Vector3 right, tangent, gridPos;

                if (track.IsDragEvent)
                {
                    // A drag race stages its field ABREAST on the line. There is
                    // no rolling start and no advantage to being ahead — the
                    // whole event is which car leaves first and pulls hardest,
                    // so a staggered grid would decide it before the tree does.
                    //
                    // WHICH line, though, is not the same question on both kinds
                    // of venue. A synthetic strip's waypoint 0 IS the start line.
                    // A baked stage's waypoint 0 is the far end of the lead-in,
                    // so staging there would start the bridge runs 150 m back
                    // down the causeway and hand every ET a free run-up.
                    int lineIdx = track.stage
                        ? Mathf.Clamp(Mathf.RoundToInt(track.stageStartLineM / Spacing), 0, pts.Count - 1)
                        : 0;
                    right = RightAt(pts, lineIdx);
                    tangent = Vector3.Cross(right, Vector3.up).normalized;
                    float laneW = RoadWidth / (CarSetups.Length + 1);
                    float lane = (c - (CarSetups.Length - 1) * 0.5f) * laneW;
                    gridPos = pts[lineIdx] + right * lane + Vector3.up * 0.35f;
                }
                else if (track.stage && !track.loop)
                {
                    // A stage grids like a circuit — 2x2, player at the back —
                    // but the index walk CLAMPS on the lead-in behind the start
                    // line instead of wrapping, because wrapping backwards from
                    // waypoint 0 on a point-to-point route puts the grid at the
                    // FINISH, seven kilometres away. (A LOOP stage takes the
                    // circuit branch below: its start line is waypoint 0 and
                    // the road behind it is the end of the lap.)
                    int lineIdx = Mathf.RoundToInt(track.stageStartLineM / Spacing);
                    int row = isPlayer ? 3 : c - 1;
                    float back = 9f + row * 6.5f;
                    float lateral = (row % 2 == 0) ? -2.1f : 2.1f;
                    float fIdx = lineIdx - back / Spacing;
                    int i0 = Mathf.Max(0, Mathf.FloorToInt(fIdx));
                    int i1 = Mathf.Min(pts.Count - 1, i0 + 1);
                    right = RightAt(pts, i0);
                    tangent = Vector3.Cross(right, Vector3.up).normalized;
                    gridPos = Vector3.Lerp(pts[i0], pts[i1], Mathf.Clamp01(fIdx - i0))
                            + right * lateral + Vector3.up * 0.35f;
                }
                else
                {
                    int row = isPlayer ? 3 : c - 1;
                    float back = 9f + row * 6.5f;
                    float lateral = (row % 2 == 0) ? -2.6f : 2.6f;
                    // Interpolated between waypoints rather than snapped to one:
                    // rows are 6.5 m apart and waypoints 4 m, so rounding would
                    // put two of the four rows only 4 m apart.
                    float fIdx = back / Spacing;
                    int step = Mathf.FloorToInt(fIdx);
                    int i0 = ((-step) % pts.Count + pts.Count) % pts.Count;
                    int i1 = ((-step - 1) % pts.Count + pts.Count) % pts.Count;
                    right = RightAt(pts, i0);
                    tangent = Vector3.Cross(right, Vector3.up).normalized;
                    gridPos = Vector3.Lerp(pts[i0], pts[i1], fIdx - step)
                            + right * lateral + Vector3.up * 0.35f;
                }

                cars.Add(BuildOneCar(carsRoot.transform, setup, isPlayer,
                    gridPos, Quaternion.LookRotation(tangent, Vector3.up), physMat, blobMat));
            }
            return cars;
        }

        /// <summary>
        /// Assemble one complete drivable car — rigidbody, collider, body
        /// shell, wheels, lights, audio, and the player-only stack (input,
        /// tank, stuck watchdog). Extracted from the grid loop so the city
        /// scene can bake its single free-roam car through exactly the same
        /// path; a second car assembler would be a second set of numbers to
        /// keep in agreement with this one.
        /// </summary>
        static CarController BuildOneCar(Transform carsRoot,
            (string name, string model, string skin, float skill, float offset) setup,
            bool isPlayer, Vector3 gridPos, Quaternion gridRot,
            PhysicsMaterial physMat, Material blobMat)
        {
                var root = new GameObject(setup.name);
                root.transform.SetParent(carsRoot, false);
                root.transform.SetPositionAndRotation(gridPos, gridRot);
                root.layer = 2; // Ignore Raycast: suspension rays skip car colliders

                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 1280f;
                // EVERY car is interpolated, not just the one the camera is
                // behind. PSXBootstrap pins physics at 60 Hz and asks for 60
                // frames, but a BROWSER draws at the display's refresh whatever
                // it is asked for, and the phones this ships to refresh at 120:
                // a body left at None then repeats its last pose on every other
                // frame — the car stands still, then jumps two steps' worth.
                // Baked on the player alone, that is exactly what the three AI
                // looked like: "jittery/jerky ... like they are loading into
                // each spot rather than smoothly driving."
                //
                // It also puts the whole field on ONE clock. An interpolated
                // body is drawn a step behind a raw one, so a player running
                // wheel-to-wheel at 200 km/h was drawn 0.9 m out of step with
                // the car beside him.
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // 1.6 m of travel per tick at top speed against 1.2 m barriers.
                // The player sweeps against other cars too (ContinuousDynamic);
                // the AI get the cheaper speculative mode, which still catches
                // static geometry without the full sweep cost on every pair.
                rb.collisionDetectionMode = isPlayer
                    ? CollisionDetectionMode.ContinuousDynamic
                    : CollisionDetectionMode.ContinuousSpeculative;

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.72f, 0.05f);
                box.size = new Vector3(1.72f, 1.0f, 4.1f);
                box.sharedMaterial = physMat;

                var car = root.AddComponent<CarController>();
                ApplyHandlingDefaults(car);

                // Body visual. Meshes and materials are left empty here and
                // filled by CarBody below: which shell this car wears is a
                // runtime decision once a LifeSim race hands over a spec, so
                // the builder goes through the same path rather than a second
                // one that could drift from it.
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                var bodyFilter = body.AddComponent<MeshFilter>();
                var bodyRenderer = body.AddComponent<MeshRenderer>();

                // Wheels. The hub is what steers, the holder carries the model's
                // scale and the outward flip, and the spin transform is what
                // CarController rolls.
                var hubs = new Transform[4];
                var meshes = new Transform[4];
                var holders = new Transform[4];
                var wheelFilters = new MeshFilter[4];
                var wheelRenderers = new MeshRenderer[4];
                for (int w = 0; w < 4; w++)
                {
                    bool left = w % 2 == 0;
                    var hub = new GameObject("Hub" + w);
                    hub.transform.SetParent(root.transform, false);
                    hub.transform.localPosition = new Vector3(left ? -0.73f : 0.73f, 0.31f, w < 2 ? 1.2125f : -1.2125f);
                    hubs[w] = hub.transform;

                    var wm = new GameObject("Wheel");
                    wm.transform.SetParent(hub.transform, false);
                    // Flip left wheels to face outward
                    wm.transform.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                    holders[w] = wm.transform;

                    var spin = new GameObject("Spin");
                    spin.transform.SetParent(wm.transform, false);
                    wheelFilters[w] = spin.AddComponent<MeshFilter>();
                    wheelRenderers[w] = spin.AddComponent<MeshRenderer>();
                    meshes[w] = spin.transform;
                }
                car.wheelHubs = hubs;
                car.wheelMeshes = meshes;

                // Blob shadow
                var blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.DestroyImmediate(blob.GetComponent<Collider>());
                blob.name = "BlobShadow";
                blob.transform.SetParent(root.transform, false);
                blob.transform.localPosition = new Vector3(0f, 0.07f, 0f);
                blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                blob.GetComponent<MeshRenderer>().sharedMaterial = blobMat;

                var shell = root.AddComponent<CarBody>();
                shell.car = car;
                shell.box = box;
                shell.bodyRoot = body.transform;
                shell.bodyFilter = bodyFilter;
                shell.bodyRenderer = bodyRenderer;
                shell.wheelHolders = holders;
                shell.wheelFilters = wheelFilters;
                shell.wheelRenderers = wheelRenderers;
                shell.blobShadow = blob.transform;
                FitShell(shell, setup.model, setup.skin);

                // Head, tail and brake lights. On every car, not just the
                // player's: the one lighting cue that matters most is the brake
                // light of the car you are about to run into.
                var lights = root.AddComponent<CarLights>();
                lights.car = car;
                lights.box = box;

                // Render-only roll/dive/squat on the shell (research B6). Every
                // car, not only the player's: an opponent that leans into a
                // corner reads as cornering from three car-lengths back.
                var lean = root.AddComponent<CarBodyLean>();
                lean.car = car;
                lean.body = shell;
                lean.bodyRoot = body.transform;

                AttachAudio(root, car, isPlayer);
                AttachTireEffects(root, car, isPlayer);

                // Collision layer. The audio clips are synthesised (no crash
                // samples exist in the pack) and shared statically, so the extra
                // component costs one AudioSource set per car, not memory.
                var crashAudio = root.AddComponent<CollisionAudio>();
                crashAudio.spatial = !isPlayer;
                crashAudio.volumeScale = isPlayer ? 1f : 0.75f;
                var responder = root.AddComponent<CollisionResponder>();
                responder.cameraShake = isPlayer;

                if (isPlayer)
                {
                    root.AddComponent<PlayerCarInput>();
                    // The tank. Player only — the AI field has never had an
                    // economy behind it, and giving four opponents a fuel
                    // budget would mean four cars that can coast to a halt on
                    // the back straight for reasons the player cannot see.
                    // RaceHandoffApplier fills it from the save; standalone it
                    // starts full and the pumps are free.
                    root.AddComponent<FuelTank>();
                    // And the temperature behind the coolant gauge, for the
                    // same reason and on the same terms: nothing reads an
                    // opponent's.
                    root.AddComponent<EngineTemp>();
                    // Player-only: the AI has had its own stuck/pinned recovery
                    // since P2, and until now the human was the only driver on
                    // the grid who could be left beached against a barrier with
                    // no way out.
                    root.AddComponent<StuckRecovery>();
                }
                else
                {
                    var ai = root.AddComponent<AIDriver>();
                    ai.skill = setup.skill;
                    ai.lateralOffset = setup.offset;
                    car.gripBonus = 1.04f;      // small AI stability bonus
                    // The AI brakes hard and steers at the same time, which would
                    // keep tripping the brake-stab drift initiator and spin it.
                    car.brakeStabDrift = 0f;
                    car.countersteerAssist = 0.5f;
                    car.allowReverse = false;   // they respawn instead of reversing
                }
                return car;
        }

        /// <summary>
        /// STAMP THE HANDLING NUMBERS FROM THE CONSTANTS, at bake time.
        ///
        /// Every tuning value on CarController is a serialized public field, so
        /// AddComponent writes the C# initialiser into the scene YAML once and
        /// that YAML is what ships forever after. A retune that edits the
        /// initialiser therefore does NOTHING until somebody re-bakes — and
        /// PSXBuildWebGL only re-runs the builder when a scene FILE IS MISSING,
        /// so the shipped cars had been running a lateral stabilizer of 4.5 at
        /// 0.7 g and a 220 deg/s steering rate against source defaults of 3.6,
        /// 0.45 and 260 for weeks, silently, while the file said otherwise.
        ///
        /// Assigning them here does not fix a stale scene on its own — a bake
        /// is still a bake — but it makes the CONSTANTS the thing a rebake
        /// copies, so the next drift between the two is a rebuild away from
        /// being closed rather than a hand-edit of forty-one YAML entries. Same
        /// pattern speedFOV and speedFullMps already follow on the camera.
        /// </summary>
        static void ApplyHandlingDefaults(CarController car)
        {
            car.brakeFrontShare = CarController.DefaultBrakeFrontShare;
            car.brakeDemandG = CarController.DefaultBrakeDemandG;
            car.steerRateDeg = CarController.DefaultSteerRateDeg;
            car.steerRateDriftDeg = CarController.DefaultSteerRateDriftDeg;
            car.maxSteerLowSpeedDeg = CarController.DefaultMaxSteerLowSpeedDeg;
            car.maxSteerHighSpeedDeg = CarController.DefaultMaxSteerHighSpeedDeg;
            car.steerSpeedFalloff = CarController.DefaultSteerSpeedFalloff;
            car.tireMuFront = CarController.DefaultTireMuFront;
            car.tireMuRear = CarController.DefaultTireMuRear;
            car.maxSteerDriftDeg = CarController.DefaultMaxSteerDriftDeg;
            car.lateralDampGrip = CarController.DefaultLateralDampGrip;
            car.lateralDampDrift = CarController.DefaultLateralDampDrift;
            car.lateralDampMaxG = CarController.DefaultLateralDampMaxG;
            car.brakeStabDrift = CarController.DefaultBrakeStabDrift;
            car.countersteerAssist = CarController.DefaultCountersteerAssist;
        }

        /// <summary>
        /// Dress one grid car at bake time. Named liveries rather than indices,
        /// so re-baking the pack in a different order cannot silently repaint
        /// the grid; an unknown name falls back to the first skin and says so.
        /// </summary>
        static void FitShell(CarBody shell, string key, string skin)
        {
            var def = CarModelLibrary.Load(key);
            if (def == null) { Log($"WARN: model '{key}' missing — car left unskinned."); return; }

            int idx = def.skinNames != null ? Array.IndexOf(def.skinNames, skin) : -1;
            if (idx < 0)
            {
                Log($"WARN: {key} has no livery '{skin}' — using {(def.SkinCount > 0 ? def.skinNames[0] : "none")}.");
                idx = 0;
            }
            shell.Apply(def, idx);
            Log($"{shell.name}: {def.displayName} in {(def.SkinCount > 0 ? def.skinNames[idx] : "no livery")} " +
                $"(wb {def.wheelbase:0.00} m, track {def.trackWidth:0.00} m)");
        }

        // The band ladders moved to EngineVoiceLibrary when the voice became
        // per-car: the builder no longer knows which recordings a car will use,
        // and two copies of the rung fractions is one copy too many.

        static AudioClip Clip(string name, bool required = true)
        {
            // The core set is WAV; the pack material imported later is Ogg
            // (encoded once on the way in, to keep 30 MB of engine audio out of
            // the repo as 180 MB of WAV). Try both rather than making callers
            // remember which is which.
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".wav")
                 ?? AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".ogg");
            if (c == null && required) Log("WARN: missing audio clip " + name);
            return c;
        }

        static void AttachAudio(GameObject root, CarController car, bool isPlayer)
        {
            var engine = root.AddComponent<EngineAudio>();
            engine.car = car;
            engine.spatial = !isPlayer;
            engine.masterVolume = isPlayer ? 1f : 0.6f;
            engine.useOffTakes = isPlayer;
            // The clips are NOT wired here any more. Which of the 28 recorded
            // families a car speaks through is a property of the car, and the
            // car is not known until RaceHandoffApplier reads the save — so the
            // builder only sets the default, and EngineAudio loads the family's
            // folder out of Resources at Awake (and again on SetFamily).
            engine.family = EngineVoiceLibrary.DefaultFamily;

            var tires = root.AddComponent<TireAudio>();
            tires.car = car;
            tires.skidClip = Clip("skid_loop");
            tires.spatial = !isPlayer;
            tires.masterVolume = isPlayer ? 1f : 0.5f;

            // Forced induction, player only: three more voices per car would push
            // the opponents past the mixer's real-voice budget, and their spool is
            // inaudible at race distance. Attached unconditionally but SILENT by
            // default — 176 of the 317 catalog cars are naturally aspirated, and
            // TurboAudio.aspiration is what RaceHandoffApplier flips per car.
            // (The built-in RX-7 FD is a sequential twin-turbo 13B-REW, so
            // standalone editor play still gets boost.)
            if (isPlayer)
            {
                // The wind bed, player only: the field's airstream is inaudible
                // from the driving seat, and it is two more always-on voices.
                var wind = root.AddComponent<WindAudio>();
                wind.car = car;

                var turbo = root.AddComponent<TurboAudio>();
                turbo.car = car;
                turbo.aspiration = TurboAudio.Aspiration.Turbo;
                turbo.spoolClip = Clip("turbo_spool");
                turbo.maxLoopClip = Clip("turbo_maxloop");
                turbo.superchargerOnClip = Clip("supercharger_on", false);
                turbo.superchargerOffClip = Clip("supercharger_off", false);
                turbo.blowOffLong = new[]
                {
                    Clip("turbo_bov_long_1"), Clip("turbo_bov_long_2"), Clip("turbo_bov_long_3"),
                };
                turbo.blowOffShort = new[]
                {
                    Clip("turbo_bov_short_1"), Clip("turbo_bov_short_2"), Clip("turbo_bov_short_3"),
                };
            }
        }

        /// <summary>
        /// The marks a sliding tyre leaves and the smoke that comes off it.
        ///
        /// On EVERY car, not just the player's. A drifting opponent that leaves
        /// no line is the tell that the effect is a decoration on the player
        /// rather than something the physics is doing — and a pack of four cars
        /// braking into the first corner is the whole reason to have it. The
        /// opponents get shorter trails and thinner clouds, because their smoke
        /// is not the smoke being looked at and four full budgets is four times
        /// the vertex upload for a car three lengths away.
        /// </summary>
        static void AttachTireEffects(GameObject root, CarController car, bool isPlayer)
        {
            var marks = root.AddComponent<SkidMarks>();
            marks.car = car;
            marks.material = MakeSkidMaterial();
            marks.capacity = isPlayer ? 224 : 72;

            var smoke = root.AddComponent<TireSmoke>();
            smoke.car = car;
            smoke.material = MakeSmokeMaterial();
            smoke.capacity = isPlayer ? 80 : 24;
            smoke.density = isPlayer ? 1f : 0.65f;
        }

        /// <summary>
        /// Tyre-mark material: PSX/Decal, tinted almost black, in the
        /// transparent queue BELOW the blob shadow so a car's shadow falls over
        /// its own marks rather than under them.
        /// </summary>
        static Material MakeSkidMaterial()
        {
            var mat = LoadOrCreate(MatDir + "/SkidMark.mat", "PSX/Decal");
            if (mat == null) return null;
            mat.mainTexture = MakeSkidTexture();
            mat.SetColor("_Tint", new Color(0.06f, 0.06f, 0.07f, 1f));
            mat.renderQueue = 2800;      // Transparent (3000) - 200
            return mat;
        }

        /// <summary>Smoke material: the same shader, white, in the ordinary
        /// transparent queue so it draws over the marks and the cars.</summary>
        static Material MakeSmokeMaterial()
        {
            var mat = LoadOrCreate(MatDir + "/TireSmoke.mat", "PSX/Decal");
            if (mat == null) return null;
            mat.mainTexture = MakeSmokeTexture();
            mat.SetColor("_Tint", Color.white);
            mat.renderQueue = 3050;
            return mat;
        }

        static Material LoadOrCreate(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Log("WARN: shader " + shaderName + " missing."); return null; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            return mat;
        }

        /// <summary>
        /// One tyre mark across its width: soft at the shoulders, with the
        /// grooves of a tread down the middle.
        ///
        /// U runs ACROSS the mark and V along it, so the ribs are columns here
        /// and the length repeats every 1.4 m of road. Soft edges are the whole
        /// point — a mark with hard sides is a strip of tape, and at 240 lines
        /// the two-pixel ramp is most of what sells it as rubber.
        /// </summary>
        static Texture2D MakeSkidTexture()
        {
            string p = GenDir + "/SkidMark.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (tex != null) return tex;

            const int W = 32, H = 32;
            tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "SkidMark" };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    // Shoulders: full in the middle, ramped over the outer 12%.
                    //
                    // The whole cross-section has to average HIGH, and that is
                    // not a taste decision. A mark is about four pixels wide on
                    // screen, so the texture is minified twenty to one and what
                    // is sampled is a deep mip — the MEAN of this row, not any
                    // part of it. Soft shoulders over 18% with ribs at half
                    // alpha averaged to about a fifth, and the marks came out
                    // as grey hairlines that a drift barely registered on.
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.5f - Mathf.Abs(u - 0.5f)) / 0.12f));
                    // Four ribs, and a scuffed length so the mark is not a
                    // uniform bar of grey.
                    float rib = Mathf.Repeat(u * 4f, 1f);
                    if (rib < 0.16f) a *= 0.72f;
                    a *= 0.90f + 0.10f * Mathf.PerlinNoise(u * 6f, (y + 0.5f) / H * 9f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            AssetDatabase.CreateAsset(tex, p);
            return tex;
        }

        /// <summary>
        /// One puff: a soft round blob with its edge broken up, so a dozen of
        /// them at different sizes read as a cloud rather than as a dozen
        /// circles.
        ///
        /// Small — 32 pixels — but FILTERED, which is the one place this game
        /// does not point-sample. Everything else it draws is a surface with a
        /// texture on it, where point filtering is the era's look; a puff is an
        /// alpha ramp, and a point-sampled alpha ramp blown up to a third of
        /// the screen is a staircase of hard-edged rectangles rather than
        /// anything resembling smoke.
        /// </summary>
        static Texture2D MakeSmokeTexture()
        {
            string p = GenDir + "/TireSmoke.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (tex != null) return tex;

            const int S = 32;
            tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "TireSmoke" };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    // Lumpy radius, so the silhouette is not a circle.
                    float lump = 0.82f + 0.18f * Mathf.PerlinNoise(
                        Mathf.Atan2(dy, dx) * 1.6f + 4f, d * 2f);
                    float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.15f, lump, d));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            AssetDatabase.CreateAsset(tex, p);
            return tex;
        }

        /// <summary>Where the cockpit artwork lives. See the README in it.</summary>
        const string CockpitDir = Root + "/Art/Cockpit";

        /// <summary>
        /// One piece of cockpit artwork, imported as a Sprite.
        ///
        /// The import settings are FORCED rather than assumed. A PNG dropped
        /// into an Assets folder arrives as a plain Texture, and
        /// LoadAssetAtPath&lt;Sprite&gt; on a plain Texture returns null — so
        /// the cabin would silently not exist and the only symptom would be a
        /// cockpit view with no cockpit in it. Guarded the same way
        /// CarModelBaker guards its own importer settings: compare first and
        /// only reimport when something actually differs, because a
        /// SaveAndReimport on every scene build is how this project's audio
        /// pipeline used to take twelve minutes.
        ///
        /// Missing is a normal state, not a warning worth failing over: the
        /// view works without artwork and says so once.
        /// </summary>
        static Sprite CockpitSprite(string name)
        {
            string path = CockpitDir + "/" + name + ".png";
            if (!File.Exists(path)) { Log("Cockpit: no " + name + ".png — cabin left unpainted."); return null; }

            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool ok = imp.textureType == TextureImporterType.Sprite
                          && imp.spriteImportMode == SpriteImportMode.Single
                          && imp.alphaIsTransparency
                          && !imp.mipmapEnabled
                          && imp.wrapMode == TextureWrapMode.Clamp
                          && imp.filterMode == FilterMode.Bilinear
                          && imp.maxTextureSize >= 2048;
                if (!ok)
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.alphaIsTransparency = true;
                    // No mips. This sheet is displayed at roughly one texel per
                    // pixel and never minified, and a mip chain on it only
                    // costs memory and softens the edge of the windscreen.
                    imp.mipmapEnabled = false;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.filterMode = FilterMode.Bilinear;
                    imp.maxTextureSize = 2048;
                    imp.SaveAndReimport();
                }
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Log("WARN: " + path + " would not import as a sprite.");
            else Log("Cockpit: " + name + ".png " + sprite.texture.width + "x" + sprite.texture.height);
            return sprite;
        }

        static Material MakeBlobShadowMaterial()
        {
            string texPath = GenDir + "/BlobShadow.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float dx = (x - 31.5f) / 30f, dy = (y - 31.5f) / 30f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                    }
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                AssetDatabase.CreateAsset(tex, texPath);
            }

            string p = MatDir + "/BlobShadow.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            var shader = Shader.Find("PSX/Shadow");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, p);
            }
            mat.shader = shader;
            mat.mainTexture = tex;
            return mat;
        }

        // ------------------------------------------------------------------
        //  Camera, HUD, race wiring
        // ------------------------------------------------------------------
        static void BuildCameraAndHUD(CarController player, List<CarController> cars,
                                      TrackPath path, Light sun)
        {
            // Main (PSX) camera renders into the 320x240 target
            var camGO = new GameObject("PSXCamera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.25f;
            // The circuits end where their fog closes; the stage's fog closes
            // four times further out, and what it is buying is the far wall of
            // the valley.
            cam.farClipPlane = track != null && track.stage ? theme.farClip : CircuitFarClip;
            cam.clearFlags = CameraClearFlags.Skybox;
            if (track != null && track.stage)
                camGO.AddComponent<StageCulling>();
            camGO.AddComponent<AudioListener>();
            // Master tone chain: Unity has no parametric EQ, so the low shelf,
            // rotary formant and saturation that give the mix weight are done as
            // biquads on the final mix.
            camGO.AddComponent<AudioToneChain>();

            var chase = camGO.AddComponent<ChaseCamera>();
            chase.target = player.transform;
            chase.targetCar = player;
            // Stamped from the constants for the reason ApplyHandlingDefaults
            // gives: these are serialized fields, and a scene baked before a
            // retune outvotes the retune forever otherwise. rotationLag is the
            // live example — every scene in the tree carries its old 7.
            chase.rotationLag = ChaseCamera.DefaultRotationLag;
            chase.rotationLagDrift = ChaseCamera.DefaultRotationLagDrift;
            chase.aimVelBlendMax = ChaseCamera.DefaultAimVelBlendMax;
            chase.aimSlipFullRad = ChaseCamera.DefaultAimSlipFullRad;
            chase.speedFOV = ChaseCamera.DefaultChaseSpeedFOV;
            chase.velFilterGrip = ChaseCamera.DefaultVelFilterGrip;
            chase.velFilterDrift = ChaseCamera.DefaultVelFilterDrift;
            chase.speedFullMps = ChaseCamera.DefaultSpeedFullMps;
            chase.speedLookAhead = ChaseCamera.DefaultSpeedLookAhead;
            camGO.transform.position = player.transform.position - player.transform.forward * 5.4f + Vector3.up * 1.8f;
            camGO.transform.rotation = Quaternion.LookRotation(player.transform.forward);

            var output = camGO.AddComponent<PSXCameraOutput>();
            // The serialized value is what a scene opened in the editor uses;
            // at runtime PSXQuality overrides it from the player's setting.
            // Baking the shipped default means a reference screenshot is taken
            // through the same framebuffer the game renders into.
            output.height = PSXQuality.Height;

            // Output camera guarantees the backbuffer is cleared behind the overlay UI
            var outCamGO = new GameObject("OutputCamera");
            var outCam = outCamGO.AddComponent<Camera>();
            outCam.clearFlags = CameraClearFlags.SolidColor;
            outCam.backgroundColor = Color.black;
            outCam.cullingMask = 0;
            outCam.depth = 50f;

            // Display canvas: full-screen RawImage showing the RT through the dither blit
            var displayCanvasGO = new GameObject("DisplayCanvas");
            var displayCanvas = displayCanvasGO.AddComponent<Canvas>();
            displayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            displayCanvas.sortingOrder = 0;
            displayCanvasGO.AddComponent<CanvasScaler>();

            var rawGO = new GameObject("PSXDisplay");
            rawGO.transform.SetParent(displayCanvasGO.transform, false);
            var raw = rawGO.AddComponent<RawImage>();
            // PSXCameraOutput overwrites this ratio with the framebuffer it
            // actually built, which now tracks the display — so the fitter fills
            // the screen instead of boxing the game into a 4:3 island.
            var fitter = rawGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            var rrt = raw.rectTransform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            var blitShader = Shader.Find("PSX/Blit");
            if (blitShader != null)
            {
                string p = MatDir + "/Blit.mat";
                var blit = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (blit == null) { blit = new Material(blitShader); AssetDatabase.CreateAsset(blit, p); }
                blit.shader = blitShader;
                raw.material = blit;
            }
            output.display = raw;

            // HUD canvas rendered by the PSX camera => rasterized at 320x240
            var hudCanvasGO = new GameObject("HUDCanvas");
            var hudCanvas = hudCanvasGO.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            hudCanvas.worldCamera = cam;
            hudCanvas.planeDistance = 1f;
            var scaler = hudCanvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var hud = hudCanvasGO.AddComponent<RaceHUD>();
            hud.car = player;
            hud.stuck = player.GetComponent<StuckRecovery>();

            Text MakeText(string name, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
            {
                var go = new GameObject(name);
                go.transform.SetParent(hudCanvasGO.transform, false);
                var t = go.AddComponent<Text>();
                t.font = font;
                t.fontSize = size;
                t.color = Color.white;
                t.alignment = align;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                var sh = go.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
                sh.effectDistance = new Vector2(1f, -1f);
                var rt = t.rectTransform;
                rt.anchorMin = anchor; rt.anchorMax = anchor;
                rt.pivot = new Vector2(anchor.x, 0.5f); // keep edge-anchored text on screen
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(160f, 30f);
                return t;
            }

            hud.lapText = MakeText("Lap", new Vector2(0f, 1f), new Vector2(44f, -14f), 12, TextAnchor.MiddleLeft);
            hud.timeText = MakeText("Time", new Vector2(0.5f, 1f), new Vector2(0f, -14f), 12, TextAnchor.MiddleCenter);
            hud.lastLapText = MakeText("Best", new Vector2(0.5f, 1f), new Vector2(0f, -30f), 10, TextAnchor.MiddleCenter);
            hud.posText = MakeText("Pos", new Vector2(1f, 1f), new Vector2(-44f, -14f), 12, TextAnchor.MiddleRight);
            hud.centerText = MakeText("Center", new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), 22, TextAnchor.MiddleCenter);
            hud.centerText.rectTransform.sizeDelta = new Vector2(300f, 120f);
            // ABOVE the cluster, not beside it. This used to sit at y=16 in the
            // strip between the old RPM bar and the old speed readout; the dials
            // now own that strip and the whole bottom third of the frame.
            hud.camText = MakeText("Cam", new Vector2(0.5f, 0f), new Vector2(0f, 98f), 11, TextAnchor.MiddleCenter);
            hud.camText.color = new Color(1f, 0.85f, 0.35f);

            // The fuel gauge, top RIGHT under the position counter.
            //
            // It was top LEFT under the lap counter, which is where the pause
            // menu's always-visible MENU button lives — a different canvas at
            // device resolution, so nothing in either layout could see the
            // collision. The button covered "FUEL 100%" and half its bar in
            // every screenshot the owner sent. The right-hand column has the
            // position readout and then nothing until the speedometer, which
            // is also where a driver's eye goes for a fuel gauge.
            //
            // A BAR and not just a number: it is read mid-corner at a glance,
            // and it is now the one readout on screen that can end a race on its
            // own. It stays on this canvas, at 240 lines, with the rest of the
            // race data — the analogue cluster is the CABIN and lives at device
            // resolution, but how much fuel is left is information printed over
            // the world, not an instrument you look down at.
            hud.tank = player.GetComponent<FuelTank>();
            hud.fuelText = MakeText("Fuel", new Vector2(1f, 1f), new Vector2(-10f, -30f), 10, TextAnchor.MiddleRight);

            var barBgGO = new GameObject("FuelBarBg");
            barBgGO.transform.SetParent(hudCanvasGO.transform, false);
            var barBg = barBgGO.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.6f);
            barBg.raycastTarget = false;
            var barBgRT = barBg.rectTransform;
            barBgRT.anchorMin = barBgRT.anchorMax = new Vector2(1f, 1f);
            barBgRT.pivot = new Vector2(1f, 0.5f);
            barBgRT.anchoredPosition = new Vector2(-10f, -42f);
            barBgRT.sizeDelta = new Vector2(50f, 6f);

            var fillGO = new GameObject("FuelBarFill");
            fillGO.transform.SetParent(barBgGO.transform, false);
            var fill = fillGO.AddComponent<Image>();
            fill.color = new Color(0.55f, 0.95f, 0.6f);
            fill.raycastTarget = false;
            var fillRT = fill.rectTransform;
            // Anchored to the bar's left edge with a left pivot, so emptying it
            // is one number: the width. Anchor-stretching it and animating
            // offsets would be the same picture through two coupled values.
            fillRT.anchorMin = fillRT.anchorMax = new Vector2(0f, 0.5f);
            fillRT.pivot = new Vector2(0f, 0.5f);
            fillRT.anchoredPosition = new Vector2(1f, 0f);
            fillRT.sizeDelta = new Vector2(48f, 4f);
            hud.fuelFill = fillRT;
            hud.fuelFillWidth = 48f;

            // The sense-of-speed blur (it replaced the radial speed streaks
            // that used to be a full-frame RawImage on this canvas: the owner's
            // call, 2026-09-19 — "not a fan of the speed lines ... would prefer
            // a blur that increases with speed like NFS Carbon"). The component
            // turns road speed into a strength; the smear itself is a URP pass
            // (SpeedBlurFeature, on both renderer assets) over the low-res
            // frame, so it is dithered by PSX/Blit with the world. It is handed
            // THIS canvas because the lap counter and the map sit in the
            // corners, where a radial blur is strongest: at runtime it moves
            // the canvas onto a camera stacked over the PSX one, which URP
            // draws after the blur. Nothing is baked for that — see SpeedBlur.
            var blur = camGO.AddComponent<SpeedBlur>();
            blur.car = player;
            blur.hudCanvas = hudCanvas;

            // The instrument cluster: a tachometer and a speedometer, built at
            // RUNTIME rather than here. Both scales come from the car — redline
            // on one, top speed on the other — and which car the player is in is
            // not decided until RaceHandoffApplier runs. Baking dials for the
            // reference FD would give a 130 km/h hatchback a 350 km/h sweep.
            //
            // ON ITS OWN CANVAS, AT SCREEN RESOLUTION, and not on the HUD canvas
            // above. The cluster used to be rasterised into the 240-line
            // framebuffer with everything else, on the theory that instruments
            // should dither and crawl along with the picture rather than sit on
            // top of it as crisp modern vector art. In a still that reasoning
            // holds. On a phone it does not: a dial at a tenth of 240 lines is
            // 25 pixels of radius carrying eight-pixel numerals, and eight-pixel
            // numerals through a dynamic font atlas are a grey smudge whatever
            // you upscale them with. Reported, accurately, as "too small and too
            // blurry".
            //
            // The touch wheel and pedals were already at screen resolution on
            // their own overlay, so the frame was never uniformly 240 lines
            // anyway. This makes the split an intentional one: the CABIN — the
            // instruments you read and the controls you hold — is drawn at
            // device resolution, and the WORLD behind it, plus the race data
            // printed over it, stay at 240 lines.
            //
            // Sorting order sits below the touch canvas (100) so a pedal is
            // never behind a dial, and above the display RawImage.
            var clusterCanvasGO = new GameObject("ClusterCanvas");
            var clusterCanvas = clusterCanvasGO.AddComponent<Canvas>();
            clusterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            clusterCanvas.sortingOrder = 90;
            var clusterScaler = clusterCanvasGO.AddComponent<CanvasScaler>();
            clusterScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // EXACTLY the touch panel's scaler, reference and match alike. The
            // cluster places its dials in the band the panel reports between the
            // wheel and the pedals, and a canvas unit has to mean the same thing
            // on both canvases or that band is measured in one currency and
            // spent in another.
            clusterScaler.referenceResolution = new Vector2(1280f, 720f);
            clusterScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            clusterScaler.matchWidthOrHeight = 0.5f;
            clusterCanvasGO.AddComponent<GraphicRaycaster>();

            var clusterGO = new GameObject("Cluster", typeof(RectTransform));
            clusterGO.transform.SetParent(clusterCanvasGO.transform, false);
            var clusterRT = (RectTransform)clusterGO.transform;
            clusterRT.anchorMin = Vector2.zero; clusterRT.anchorMax = Vector2.one;
            clusterRT.offsetMin = Vector2.zero; clusterRT.offsetMax = Vector2.zero;
            var cluster = clusterGO.AddComponent<GaugeCluster>();
            cluster.car = player;
            hud.cluster = cluster;

            // The cabin, for COCKPIT view: roof, pillars, dash, the car's own
            // bonnet and a working mirror.
            //
            // Its own canvas UNDER the cluster's (90) and under the touch
            // panel's (100), because that is the order these things are in
            // physically: the dashboard is behind the instruments on it, and
            // both are behind the wheel and pedals the player is holding.
            //
            // The scaler matches the cluster's exactly, for the same reason the
            // cluster's matches the touch panel's — the cabin decides where the
            // dash line is and the cluster puts its binnacle on that line, and
            // a canvas unit has to mean the same thing on both or the
            // instruments float above the dashboard or sink into it.
            var cabinCanvasGO = new GameObject("CockpitCanvas");
            var cabinCanvas = cabinCanvasGO.AddComponent<Canvas>();
            cabinCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cabinCanvas.sortingOrder = 80;
            var cabinScaler = cabinCanvasGO.AddComponent<CanvasScaler>();
            cabinScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cabinScaler.referenceResolution = new Vector2(1280f, 720f);
            cabinScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cabinScaler.matchWidthOrHeight = 0.5f;

            var cabinGO = new GameObject("Cockpit", typeof(RectTransform));
            cabinGO.transform.SetParent(cabinCanvasGO.transform, false);
            var cabinRT = (RectTransform)cabinGO.transform;
            cabinRT.anchorMin = Vector2.zero; cabinRT.anchorMax = Vector2.one;
            cabinRT.offsetMin = Vector2.zero; cabinRT.offsetMax = Vector2.zero;
            var cabin = cabinGO.AddComponent<CockpitView>();
            cabin.car = player;
            cabin.worldCamera = cam;
            cabin.cabin = CockpitSprite("cabin");
            cabin.wheel = CockpitSprite("wheel");

            // Race manager — or, with no path, the city: Charlotte has no laps
            // to count, so no RaceManager exists there at all; CityMode (wired
            // by the city builder) is the session instead, and everything that
            // needs "the session" reads it through DriveSession.
            var rmGO = new GameObject(path != null ? "RaceManager" : "Session");
            if (path != null)
            {
                var rm = rmGO.AddComponent<RaceManager>();
                rm.path = path;
                rm.playerCar = player;
                rm.allCars = cars;
                // Laps per circuit, so every race is about the same distance: four
                // laps of an 824 m dock circuit against two of a 1632 m mountain one.
                rm.totalLaps = track.laps;

                foreach (var c in cars)
                {
                    var ai = c.GetComponent<AIDriver>();
                    if (ai != null) ai.path = path;
                }
            }

            // Applies the LifeSim's fault handicaps, purse and time-of-day when
            // the race was entered from Home; inert on standalone editor play.
            var applier = rmGO.AddComponent<RaceHandoffApplier>();
            applier.playerCar = player;
            applier.hud = hud;
            applier.sun = sun;
            // Grid order IS the contract with RaceHandoff.OpponentSpecIds, so the
            // list is built here from the same ordered pass that spawned the cars
            // rather than discovered at runtime with FindObjectsOfType.
            foreach (var c in cars) if (c != player) applier.aiCars.Add(c);
        }
    }
}
