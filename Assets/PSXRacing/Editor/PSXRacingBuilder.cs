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
        static bool Loop => track == null || (!track.drag && !track.stage);

        internal const float WallOffset = 10f;
        // 2.4 m puts the top edge above the ~1.9 m chase-cam eyeline, so the
        // barrier silhouettes against the sky instead of hiding in the ground band.
        const float WallHeight = 2.4f;
        const float WallThick = 0.35f;
        /// <summary>Collider depth, grown outward from the visible face. The car
        /// covers ~1.6 m per physics tick at top speed, so a 0.35 m collider is
        /// thin enough to be stepped over even with continuous detection on.</summary>
        const float WallCollThick = 1.2f;
        const float WallCollOverlap = 0.6f;
        internal const float KerbWidth = 0.9f;
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
            public int buildingEvery = 9, treeEvery = 7, parkedEvery = 11, lampEvery = 13;
            /// <summary>Chance a candidate site is skipped, so a run of scenery
            /// reads as a street rather than as a fence.</summary>
            public double buildingSkip = 0.25, treeSkip = 0.35;
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
                treeEvery = 13, treeSkip = 0.3,
                parkedEvery = 19,
                lampEvery = 11,
                gasStation = true,
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
                gasStation = false,
            },

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
            gasStation = false,
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
        static string BuildTrack(TrackCatalog.TrackDef def)
        {
            track = def;
            theme = ThemeFor(def);
            // Materials are cached by texture path and by key, and two circuits
            // legitimately want a "Ground" material off different textures.
            // Clearing per track keeps the key namespace per track as well.
            matByTex.Clear();
            matByKey.Clear();

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
            path.pointToPoint = def.stage;
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

            BuildRoad(waypoints, pathGO.transform);
            BuildKerbs(waypoints, pathGO.transform);
            if (def.stage) { BuildStageWalls(waypoints, pathGO.transform);
                             BuildStageBanks(waypoints, pathGO.transform); }
            else BuildWalls(waypoints, pathGO.transform);
            if (def.stage) BuildStageGround(waypoints, pathGO.transform);
            else BuildGround(waypoints, pathGO.transform);
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
                for (int s = 0; s < CityMeshes.SurfaceCount; s++)
                    EnsureTrackRoadTex(def.roadWidth, def.drag, (CityMeshes.Surface)s);
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

            // One 2x2 cell, tiled 8x2 by BuildStartLine: 16 squares across the
            // road and 4 along it.
            WriteTexture(GridTexPath, 32, 32, (x, y) =>
                ((x < 16) ^ (y < 16)) ? new Color32(18, 18, 20, 255)
                                      : new Color32(220, 218, 212, 255));

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
        static string GridTexPath => TrackTexDir + "/StartGrid.png";
        static string JointTexPath => TrackTexDir + "/Joint.png";

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
                // The forest atlas is 4x4 species on one 512 sheet: clamping it
                // to 256 would halve every tree to 64 px — chunkier than the
                // circuit trees it replaces. One page of exemption, explicitly.
                int wantMax = p.Contains("/BRP/Gen/TreeAtlas") ? 512 : 256;
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

        static Material MakeMat(string name, string texPath, float cutoff = 0f,
                                Color? tint = null, float affine = 0f)
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
                    onDeck[i] = TrackCatalog.BridgeBlend(track, Mathf.Repeat(i * Spacing, lap)) > 0.001f;
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

            var mat = MakeMat(MeshPrefix + "Road",
                              EnsureTrackRoadTex(RoadWidth, track != null && track.drag, tarmacSurf),
                              affine: 0f);
            var mr = go.AddComponent<MeshRenderer>();
            if (anyDeck)
            {
                var deckRoadMat = MakeMat(MeshPrefix + "RoadDeck",
                                          EnsureTrackRoadTex(RoadWidth, track != null && track.drag, deckSurf),
                                          affine: 0f);
                mr.sharedMaterials = new[] { mat, deckRoadMat };
            }
            else mr.sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;

            BuildRoadEdge(pts, parent);
        }

        /// <summary>
        /// How far out the verge batter runs before it meets the graded ground.
        ///
        /// Bounded by the barrier line, so a stage — whose guard wall stands
        /// less than a metre off the tarmac — keeps the near-vertical slab face
        /// it has always had, and a circuit with four metres of run-off gets a
        /// real slope. Never inside the kerb: the strip caps the slab and there
        /// must be no seam on the driving surface.
        /// </summary>
        static float VergeOuter()
        {
            float kerbOuter = RoadWidth * 0.5f + KerbWidth;
            float want = Mathf.Min(RoadbedToe + RoadbedRamp, WallOffsetFor(track) - 0.6f);
            return Mathf.Max(kerbOuter + RoadSlabBatter, want);
        }

        /// <summary>
        /// The shoulder: the fill batter from the outer edge of the kerb strip
        /// down to the graded ground beside it.
        ///
        /// This used to be the CUT FACE of the roadbed — a 45 cm drop over
        /// 25 cm of batter, with no collider, because it was thought of as
        /// something you look at rather than something you drive on. That is
        /// true right up until a car runs wide, and then it is a wall: the
        /// ground beside the tarmac is dug to the bottom of the slab
        /// (<see cref="RoadbedSinkAt"/>), so a car in the gravel sits half a
        /// metre below the road with a step taller than its own wheels between
        /// it and the way back. That is what "some sections of track are almost
        /// impossible to drive back onto" was, and it was on both sides of
        /// every circuit.
        ///
        /// Now it lands ON the graded ground rather than in the trench, which
        /// makes it a ramp of a few degrees, and it CARRIES A COLLIDER so a
        /// wheel finds it. The outer edge is dropped three centimetres under
        /// the ground it meets, so the two interpenetrate instead of fighting
        /// for the same pixels.
        ///
        /// Where the land genuinely falls away — an embankment, the end of a
        /// bridge approach — the ground goes with it and the full section still
        /// shows, which is what made this worth drawing in the first place.
        /// </summary>
        static void BuildRoadEdge(List<Vector3> pts, Transform parent)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float kerbOuter = RoadWidth * 0.5f + KerbWidth;
            float outer = VergeOuter();
            float run = outer - kerbOuter;
            // The toe stays at the BOTTOM of the slab, as deep as it always
            // was; only the batter got longer. That depth is what makes the
            // result robust: the graded ground beside the road is never lower
            // than the slab bottom, so the batter is never left standing proud
            // of it with a lip at its outer end — the two surfaces simply cross
            // somewhere in the middle and the car rides whichever is higher,
            // continuously, all the way in.
            const float ToeDrop = RoadSlabDepth - RoadLift;

            foreach (float side in new[] { -1f, 1f })
            {
                float dist = 0f;
                for (int i = 0; i <= last; i++)
                {
                    int idx = Loop ? i % n : i;
                    Vector3 outw = RightAt(pts, idx) * side;
                    Vector3 top = pts[idx] + Vector3.up * (RoadLift + 0.01f)
                                + outw * kerbOuter;
                    Vector3 toe = pts[idx] + Vector3.down * ToeDrop + outw * outer;
                    int v = verts.Count;
                    verts.Add(top); verts.Add(toe);
                    uvs.Add(new Vector2(dist / 6f, 1f));
                    uvs.Add(new Vector2(dist / 6f, 1f - run / 6f));
                    dist += Spacing;
                    // The batter runs THROUGH the forecourt driveway too.
                    //
                    // It was skipped there at first, on the reasoning that the
                    // pad is graded flush with the road and two surfaces in the
                    // same place would fight. They do not overlap: the pad's
                    // near edge is at WallOffset - PadRoadOverlap, which on the
                    // airfield is 8.3 m, and the kerb ends at 7.9 — so skipping
                    // left a 40 cm strip of open trench across the one place on
                    // the circuit a car is MEANT to leave the road, and the
                    // run-off audit found it on exactly the handful of stations
                    // the driveway spans. Where they do meet the pad sits
                    // higher and simply wins.
                    if (i < last)
                    {
                        // Wound to face UPWARD and outward on each side — the
                        // underside is buried in the subgrade and nothing is
                        // ever in there.
                        if (side < 0f) tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                        else tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                    }
                }
            }

            var mesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray(),
            };
            mesh.RecalculateNormals();
            SaveMesh(mesh, "RoadEdgeMesh");

            var go = new GameObject("RoadEdge");
            go.transform.SetParent(parent, false);
            // Deliberately NOT on the road layer, even though a wheel now rolls
            // on it: grip is decided by that layer, and a gravel shoulder that
            // gripped like tarmac would make running wide free. Off the road
            // layer it supports the car and gives it offroadGrip, which is what
            // a shoulder is.
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // The ground texture, darkened: this IS cut earth and aggregate
            // under a wearing course, and at PSX resolution one material does
            // for both halves of that.
            go.AddComponent<MeshRenderer>().sharedMaterial =
                MakeMat(MeshPrefix + "RoadEdge", theme.ground, affine: 0f,
                        tint: new Color(0.42f, 0.39f, 0.36f));
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
        }

        /// <summary>
        /// A high-contrast strip along the exact edge of the tarmac. The barrier
        /// sits 4 m out in the gravel, so without this there is nothing marking
        /// where grip actually ends — the road just fades into dirt.
        /// </summary>
        static void BuildKerbs(List<Vector3> pts, Transform parent)
        {
            int n = pts.Count, last = Loop ? n : n - 1;
            // The parkway has no racing kerb — its tarmac runs into a mown
            // gravel verge, so the stage lays the same strip in gravel. Same
            // geometry either way: the strip is also what visually seals the
            // corridor-sink lip at the road edge.
            var mat = track != null && track.stage
                ? MakeMat(MeshPrefix + "Kerb", StageGenDir + "/Shoulder.png", affine: 0f)
                : MakeMat("Kerb", KerbTexPath, affine: 0f);
            mat.mainTextureScale = new Vector2(1f, 1f);

            foreach (float side in new[] { -1f, 1f })
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                float dist = 0f;

                for (int i = 0; i <= last; i++)
                {
                    int idx = Loop ? i % n : i;
                    Vector3 outw = RightAt(pts, idx) * side;
                    // 1 cm above the road ribbon so it reads as a raised kerb and
                    // cannot z-fight; the road mesh ends exactly at 6 m.
                    Vector3 inner = pts[idx] + Vector3.up * (RoadLift + 0.01f) + outw * (RoadWidth * 0.5f);
                    Vector3 outer = inner + outw * KerbWidth;
                    int v = verts.Count;
                    verts.Add(inner); verts.Add(outer);
                    // Repeat every 2 m of travel gives the classic red/white dashing.
                    uvs.Add(new Vector2(dist / 2f, 0f));
                    uvs.Add(new Vector2(dist / 2f, 1f));
                    dist += Spacing;
                    // Opposite winding on the two sides, because `outw` flips
                    // with `side` and the corner order goes with it — the same
                    // branch BuildRoadEdge has always had, and which this strip
                    // never got. Without it the LEFT kerb faced downward: it was
                    // invisible from the car (which is why every screenshot of
                    // this game has a kerb on one side only), and once the strip
                    // carried a collider its back face was invisible to a
                    // downward raycast too, so the run-off audit found half a
                    // metre of missing surface down the left of all four
                    // circuits and none down the right.
                    if (i < last)
                    {
                        if (side < 0f) tris.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                        else tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                    }
                }

                var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
                SaveMesh(mesh, side < 0 ? "KerbMeshL" : "KerbMeshR");
                var go = new GameObject(side < 0 ? "KerbL" : "KerbR");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                // A COLLIDER, which this strip has never had.
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
                // On the road layer for a circuit, because that is a racing
                // kerb and it grips; off it on a stage, where the same geometry
                // is drawn as a gravel shoulder and should not.
                if (track == null || !track.stage) go.layer = RoadLayer;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.isStatic = true;
            }
            Log("Built kerb strips at the tarmac edge.");
        }

        static void BuildWalls(List<Vector3> pts, Transform parent)
        {
            var wallMat = MakeMat(MeshPrefix + "Wall", theme.wall, affine: 0f);
            var physMat = GetOrCreatePhysMat("WallPhys", 0.05f, 0f);
            int n = pts.Count, last = Loop ? n : n - 1;

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

                    // One collider per drawn segment. Emitting them every other
                    // waypoint made each box a chord across two segments, and the
                    // padding pushed the contact surface inside the drawn face —
                    // so the car stopped before touching anything visible.
                    if (i < last && !gap)
                    {
                        int nxt = Loop ? (idx + 1) % n : Mathf.Min(idx + 1, n - 1);
                        Vector3 outw = RightAt(pts, idx) * side;
                        Vector3 next = pts[nxt] + RightAt(pts, nxt) * (WallOffset * side);
                        Vector3 dir = next - basePos; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.01f)
                        {
                            // Offset outward by half the thickness so the box's
                            // INNER face is coplanar with the quad the player sees.
                            // The collider is far thicker than the drawn wall
                            // (WallCollThick vs WallThick) and grows only
                            // OUTWARD, so the contact surface is unchanged while
                            // a fast car has real depth to catch against.
                            Vector3 mid = (basePos + next) * 0.5f
                                        + Vector3.up * (WallHeight * 0.5f)
                                        + outw * (WallCollThick * 0.5f);
                            var col = new GameObject("Wall");
                            col.transform.SetParent(wallRoot.transform, false);
                            col.layer = SolidLayer;
                            col.transform.position = mid;
                            col.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                            var box = col.AddComponent<BoxCollider>();
                            // Overlap neighbours along the run. Coplanar boxes
                            // meeting exactly edge-to-edge leave a hairline seam
                            // the solver can catch a corner on, which reads as
                            // the car snagging on nothing.
                            box.size = new Vector3(WallCollThick, WallHeight, dir.magnitude + WallCollOverlap);
                            box.sharedMaterial = physMat;
                        }
                    }
                }

                var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
                SaveMesh(mesh, side < 0 ? "WallMeshL" : "WallMeshR");
                var meshGO = new GameObject("WallMesh");
                meshGO.transform.SetParent(wallRoot.transform, false);
                meshGO.AddComponent<MeshFilter>().sharedMesh = mesh;
                meshGO.AddComponent<MeshRenderer>().sharedMaterial = wallMat;
                meshGO.isStatic = true;
            }
        }

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
            // vertices nobody will ever see.
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            float size = Mathf.Max(b.size.x, b.size.z) + 760f;
            float tile = theme.groundTile;
            // 45 cells meant 20 m triangles. Affine UVs distort in proportion to
            // triangle size and depth contrast, which is worst on ground right
            // under the camera — measured at ~52 px of texture slip. The ground
            // material also opts out of affine entirely (see MakeMat below);
            // this finer grid is for the per-vertex fog and lighting gradient,
            // and now for the hills as well.
            //
            // 144 rather than 120 since the ground started following the road.
            // A cell is about 9 m across, and the whole reason CorridorR is six
            // metres wider than the barrier line is that a cell that size cannot
            // be trusted to land anywhere in particular: every vertex within the
            // corridor is pinned dead level with the road, so the coarse grid
            // has no way to push a corner up through the tarmac.
            const int cells = 144;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // The mesh is local to a GameObject parked at the circuit's centre,
            // so the height field — which is a function of WORLD position — has
            // to be asked about the world point, not the local one.
            float ox = b.center.x, oz = b.center.z;
            for (int y = 0; y <= cells; y++)
                for (int x = 0; x <= cells; x++)
                {
                    float fx = x / (float)cells - 0.5f, fy = y / (float)cells - 0.5f;
                    float wx = fx * size, wz = fy * size;
                    verts.Add(new Vector3(wx, GroundHeightAt(wx + ox, wz + oz), wz));
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
            go.transform.position = new Vector3(b.center.x, 0f, b.center.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial =
                MakeMat(MeshPrefix + "Ground", theme.ground, affine: 0f);
            // A box no longer describes it. The ground the wheels find off the
            // racing line is the ground you can see, hills and gorge included —
            // a flat plate under a mountain pass would have a car that ran wide
            // driving along thin air at valley height.
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
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
        /// them. It is also what you SEE where the land falls away — a road
        /// edge with a section, instead of a ribbon one polygon thick.
        /// </summary>
        internal const float RoadSlabDepth = 0.45f;

        /// <summary>Batter on the slab's cut face. Nearly vertical, because
        /// there is nowhere to put a real fill slope: the stage's guard wall
        /// stands 1.15 m off the tarmac edge, and anything wider than this
        /// would push the roadbed out through the masonry.</summary>
        internal const float RoadSlabBatter = 0.25f;

        /// <summary>How far outside the slab the dig ramps back up to the
        /// shoulder shelf. Short, and entirely inside CorridorR, so every
        /// height outside the roadbed is exactly what it was before this
        /// existed — barriers, scenery and the forecourt pad have not
        /// moved.</summary>
        const float RoadbedRamp = 2.5f;

        /// <summary>Outer edge of the paved footprint: tarmac, shoulder strip
        /// and the batter that finishes the slab.</summary>
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
        /// and the hole in the ground are guaranteed to be the same length as
        /// each other. Two thresholds would drift, and the failure — a deck
        /// ending ten metres short of the abutment — is invisible from the
        /// driving line and obvious from anywhere else.
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
            var deckMat = MakeMat(MeshPrefix + "Deck", concrete, affine: 0f);
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
            while (origin < n && blend[origin] > 0.001f) origin++;
            if (origin >= n) origin = 0;     // the whole lap is elevated

            int spanNo = 0, piers = 0;
            var jointIdx = new List<int>();
            for (int k = 0; k < n; )
            {
                if (blend[(origin + k) % n] <= 0.001f) { k++; continue; }
                int len = 1;
                while (k + len < n && blend[(origin + k + len) % n] > 0.001f) len++;

                int from = (origin + k) % n;
                BuildOneDeck(pts, from, len, root.transform, deckMat, physMat, ++spanNo);
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
                MakeMat(MeshPrefix + "Joint", JointTexPath, affine: 0f);
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
                // The deck top sits just UNDER the road ribbon (which is at
                // +0.12) so the two never fight for the same pixels, and the
                // kerb at +0.13 still stands proud of both.
                Vector3 c = pts[i] + Vector3.up * 0.08f;
                Vector3 under = pts[i] + Vector3.up * (0.08f - DeckThick);

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

        static int BuildPiers(List<Vector3> pts, int from, int stations,
                              Transform parent, Material mat)
        {
            int n = pts.Count;
            int step = Mathf.Max(1, Mathf.RoundToInt(PierEvery / Spacing));
            int placed = 0;
            for (int k = 0; k < stations; k += step)
            {
                int i = (from + k) % n;
                float deckBottom = pts[i].y + 0.08f - DeckThick;
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
            var mat = MakeMat("StartLine", GridTexPath);
            mat.mainTextureScale = new Vector2(8f, 2f);

            // On the stage the start line sits a lead-in past waypoint 0, so
            // the whole grid can stand on real road behind it without the
            // index walk falling off the front of the list.
            int startIdx = track.stage
                ? Mathf.RoundToInt(track.stageStartLineM / Spacing) : 0;
            Line("StartLine", startIdx);
            // A route with ends needs a line at each end: the one you launch
            // from is not the one that stops the clock, and they are kilometres
            // apart with a shutdown area beyond.
            if ((track.drag || track.stage) && track.FinishIndex > 0 && track.FinishIndex < pts.Count)
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
        }

        /// <summary>
        /// Street lighting: a post and a lamp head that are there all day, and
        /// the glow and the pool underneath, which are not.
        ///
        /// The glows go under one "NightLights" parent carrying a single
        /// NightGlow component — the hour toggles that one object rather than
        /// thirty. Nothing here gets a collider: the posts stand outside the
        /// barrier line, where a collider could only ever cost contact pairs
        /// against a car that cannot reach them.
        /// </summary>
        static void PlaceStreetLamps(List<Vector3> pts, Transform parent)
        {
            var glowShader = Shader.Find("PSX/Glow");
            if (glowShader == null) { Log("WARN: PSX/Glow missing — no street lighting."); return; }

            var postMat = MakeMat("LampPost", null, tint: new Color(0.30f, 0.30f, 0.34f), affine: 0f);
            var headMat = MakeMat("LampHead", null, tint: new Color(0.62f, 0.60f, 0.55f), affine: 0f);
            var glowMat = MakeGlowMaterial("LampGlow", new Color(1.00f, 0.86f, 0.55f), 1.5f);
            var poolMat = MakeGlowMaterial("LampPool", new Color(1.00f, 0.84f, 0.52f), 0.5f);
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
                Vector3 baseP = pts[i] + right * side * (WallOffset + 0.8f);
                // The lamp line runs where the forecourt entrance is, and a
                // post in the middle of it would be a lamp standing on tarmac
                // the player is meant to drive over. The station lights its own
                // canopy.
                if (OnFuelPad(baseP, 1.5f)) continue;
                // Its own patch of ground — except over a gorge, where the
                // ground is ten metres down and the thing to stand on is the
                // deck. The lamp line sits at WallOffset + 0.8, inside the deck
                // edge at WallOffset + 1.4, so there is something under it.
                baseP.y = OverGorge(i) ? pts[i].y + 0.08f
                                       : GroundHeightAt(baseP.x, baseP.z);

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

                var pool = new GameObject("Pool");
                pool.transform.SetParent(nightRoot.transform, false);
                // The pool of light lands on whatever the lamp is standing on.
                float poolY = OverGorge(i) ? pts[i].y + 0.14f
                                           : GroundHeightAt(headP.x, headP.z);
                pool.transform.position = new Vector3(headP.x, poolY + 0.15f, headP.z);
                pool.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                pool.transform.localScale = new Vector3(16f, 16f, 1f);
                pool.AddComponent<MeshFilter>().sharedMesh = glowMesh;
                pool.AddComponent<MeshRenderer>().sharedMaterial = poolMat;
                pool.GetComponent<MeshRenderer>().enabled = false;
                placed++;
            }
            Log($"Placed {placed} street lamps.");
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
            string[] keys = { "classic_van", "jdm_pickup", "landrover", "euro_hatch",
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
                Vector3 fwd = Vector3.Cross(Vector3.up, right);

                var go = new GameObject("Parked_" + def.key);
                go.transform.SetParent(parent, false);
                // Tight against the outside of the barrier, in front of the
                // tree line: street parking, not a scrapyard in a field.
                Vector3 parkAt = pts[i] + right * side * (WallOffset + 1.5f);
                // The forecourt is the one stretch of verge that is a road.
                if (OnFuelPad(parkAt, 2f)) { UnityEngine.Object.DestroyImmediate(go); continue; }
                parkAt.y = GroundHeightAt(parkAt.x, parkAt.z);
                go.transform.position = parkAt;
                // Nose-to-tail along the kerb, some facing the other way, and a
                // couple of degrees off true — a row of perfectly aligned cars
                // reads as a texture, not as parking.
                go.transform.rotation = Quaternion.LookRotation(
                    rng.NextDouble() < 0.5 ? fwd : -fwd, Vector3.up)
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
                wheel.AddComponent<MeshRenderer>().sharedMaterial = wheelMat;
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
                    if (OnFuelPad(pts[i] + RightAt(pts, i) * side * (WallOffset + 8f), 10f))
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
                    Vector3 pos = pts[i] + right * side *
                                  (WallOffset + BuildingClearance + faceOffset);

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
            const float Want = WallOffset + BuildingClearance;
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

            int best = start;
            float bestRelief = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                int idx = (start + k) % n;
                // Never out over a gorge. A forecourt is a slab on the ground,
                // and 62% of the way round HarborPoint is the channel.
                if (OverGorge(idx)) continue;

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
            return best;
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

        /// <summary>
        /// The tarmac. On the ROAD layer, because the wheels tell tarmac from
        /// grass by layer and a forecourt the car slides across at field grip
        /// is one nobody can stop on.
        /// </summary>
        static void BuildApron(Transform parent)
        {
            const int cells = 16;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++)
                {
                    float along = Mathf.Lerp(-ApronHalfAcross, ApronHalfAcross, i / (float)cells);
                    float deep = Mathf.Lerp(-PadHalfDeep, PadHalfDeep, j / (float)cells);
                    Vector3 p = padCentre + padAlong * along + padToRoad * deep;
                    // Three centimetres proud of THE GROUND AT THIS POINT, not
                    // of the pad's own flat plane. The two are the same
                    // everywhere the pad is at full strength, and they are not
                    // the same at its edges — where the flattening is fading out
                    // and, at the road end, where the corridor sink still
                    // applies. Taking the plane would leave the apron's inner
                    // edge standing over the verge as a lip the car has to bump
                    // up; taking the ground makes the entrance a ramp.
                    p.y = GroundHeightAt(p.x, p.z) + 0.03f;
                    verts.Add(p);
                    uvs.Add(new Vector2(along / 8f, deep / 8f));
                }

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
                        Root + "/Art/GasStation/Textures/AsphaltDamaged.jpg", affine: 0f);
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
                verts.Add(rot * new Vector3(-2.6f, 0f, 0f));
                verts.Add(rot * new Vector3(-2.6f, 6.5f, 0f));
                verts.Add(rot * new Vector3(2.6f, 6.5f, 0f));
                verts.Add(rot * new Vector3(2.6f, 0f, 0f));
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
                // One winding only — see the note in BuildWalls. Trees are
                // crossed quads, so each plane is still visible from both sides
                // via the other plane of the cross.
                tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
            var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            SaveMesh(mesh, "TreeMesh");

            var mat = MakeMat(MeshPrefix + "Tree", theme.tree, cutoff: 0.5f);
            var rng = new System.Random(7);
            int n = pts.Count, placed = 0;
            for (int i = 4; i < n; i += theme.treeEvery)
            {
                float side = (i / theme.treeEvery) % 2 == 0 ? -1f : 1f;
                if (rng.NextDouble() < theme.treeSkip || OverGorge(i)) continue;
                Vector3 right = RightAt(pts, i);
                var t = new GameObject("Tree");
                t.transform.SetParent(parent, false);
                Vector3 treeAt = pts[i] + right * side * (WallOffset + 2.6f);
                // Not through the forecourt.
                if (OnFuelPad(treeAt, 2f)) { UnityEngine.Object.DestroyImmediate(t); continue; }
                // Sunk 20 cm. A billboard whose base is exactly on a facet edge
                // shows a sliver of sky under itself the moment the ground tips.
                treeAt.y = GroundHeightAt(treeAt.x, treeAt.z) - 0.2f;
                t.transform.position = treeAt;
                t.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                float s = 0.8f + (float)rng.NextDouble() * 0.5f;
                t.transform.localScale = new Vector3(s, s, s);
                t.AddComponent<MeshFilter>().sharedMesh = mesh;
                t.AddComponent<MeshRenderer>().sharedMaterial = mat;
                placed++;
            }
            Log($"Placed {placed} trees.");
        }

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
            // three times further. TimeOfDay.Apply reads the scale back at
            // runtime, so the seven hours all stretch with the venue.
            globals.fogScale = track != null && track.stage ? StageFogScale : 1f;
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
            var physMat = GetOrCreatePhysMat("CarPhys", 0.15f, 0.05f);
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
                else if (track.stage)
                {
                    // A stage grids like a circuit — 2x2, player at the back —
                    // but the index walk CLAMPS on the lead-in behind the start
                    // line instead of wrapping, because wrapping backwards from
                    // waypoint 0 on a point-to-point route puts the grid at the
                    // FINISH, seven kilometres away.
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
                rb.interpolation = isPlayer ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
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
            // The circuits end at 360 m because their fog closes before that.
            // The stage's fog closes around three times further out, and what
            // it is buying is the far wall of the valley.
            cam.farClipPlane = track != null && track.stage ? StageFarClip : 360f;
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
