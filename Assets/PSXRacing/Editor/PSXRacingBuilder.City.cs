using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The Charlotte bake. Where BuildTrack pours a whole circuit into its
    /// scene, this bakes a nearly EMPTY one: lighting, the player car at the
    /// uptown spawn, camera + HUD, and one configured CityWorld — the world
    /// itself is generated at runtime, tile by tile, from the road graph in
    /// Resources (see Docs/CHARLOTTE.md for why the project's bake-everything
    /// rule inverts here).
    ///
    /// A CITY RACE (TrackDef.cityRoute) bakes the same scene plus what a race
    /// needs: three AI cars, an empty TrackPath, a RaceManager and the
    /// handoff applier. CityMode fills the path from the route at load and
    /// stands the grid on it.
    ///
    /// What IS baked: the per-profile road surfaces (drawn, never sourced —
    /// the punch-clock rule), the facade set copied from the owner's building
    /// pack plus the drawn glass and siding, one composed shopfront atlas, the
    /// materials for every CityMeshes.Slot, and the menu thumbnail rasterised
    /// from the real graph.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string CityTexDir = Root + "/Art/City";
        const string BuildingsSrc =
            @"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Buildings\Buildings\Textures";

        static string BuildCityScene(TrackCatalog.TrackDef def)
        {
            ClearSeasonEntries();
            track = def;
            matByTex.Clear();
            matByKey.Clear();

            EnsureCityFolders();
            GenerateCityTextures();
            EnsureCityArt();
            AssetDatabase.Refresh();

            var map = CityMap.Get();
            if (map == null) throw new Exception("charlotte_city.bytes missing — run tools/city/export_osm.mjs");
            Log($"--- {def.name} ({def.id}): {map.edges.Length} edges, {map.nodes.Length} nodes, " +
                $"{map.crossings.Length} grade separations ({CityElevation.TrenchCount} trenched), " +
                $"{map.wspans.Length} water spans, {map.footprints.Length} footprints, {map.routes.Length} routes");

            if (def.IsRoam) BakeCityThumbnail(map);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGO = BuildLighting();

            // ---- spawn: the nearest real street to Trade & Tryon ----------
            // (A race re-places every car on its route in CityMode.Awake; the
            // spawn only has to be somewhere the tiles can be built.)
            if (!map.NearestRoadPoint(map.uptown, 600f, skipLinks: true,
                    out int spawnEdge, out float spawnS, out _))
                throw new Exception("no road found near uptown — is the export sane?");
            var e = map.edges[spawnEdge];
            var sp = e.PointAt(spawnS);
            var tan2 = e.TangentAt(spawnS);
            var spawnPos = new Vector3(sp.x, e.YAt(spawnS) + 0.45f, sp.y);
            var spawnRot = Quaternion.LookRotation(new Vector3(tan2.x, 0f, tan2.y), Vector3.up);
            Log($"spawn: {e.name} at ({spawnPos.x:0}, {spawnPos.z:0})");

            var physMat = GetOrCreatePhysMat("CarPhys", CarSlideFriction, 0.05f);
            var blobMat = MakeBlobShadowMaterial();
            var carsRoot = new GameObject("Cars");
            var player = BuildOneCar(carsRoot.transform, CarSetups[0], isPlayer: true,
                spawnPos, spawnRot, physMat, blobMat);
            var cars = new List<CarController> { player };

            TrackPath path = null;
            var aiCars = new List<CarController>();
            if (def.IsCityRace)
            {
                var route = map.RouteById(def.cityRoute);
                if (route == null) throw new Exception("city route missing from the bake: " + def.cityRoute);
                Log($"route {route.id}: {route.edges.Length} edges, {route.lengthM:0} m, line at {route.startM:0}, " +
                    (route.loop ? "loop" : $"finish at {route.finishM:0}"));

                // The grid is laid at load, so the AI just need to exist; a
                // few metres apart so they do not spawn inside one another
                // before CityMode moves them.
                for (int c = 1; c < CarSetups.Length; c++)
                {
                    var ai = BuildOneCar(carsRoot.transform, CarSetups[c], isPlayer: false,
                        spawnPos - spawnRot * Vector3.forward * (6.5f * c), spawnRot, physMat, blobMat);
                    cars.Add(ai);
                    aiCars.Add(ai);
                }

                var pathGO = new GameObject("Track");
                path = pathGO.AddComponent<TrackPath>();
                path.waypoints = new Vector3[0];
                path.curvatures = new float[0];
                path.spacing = Spacing;
                path.roadWidth = def.roadWidth;
                path.drag = false;
                path.pointToPoint = !def.loop;
                path.finishIndex = def.FinishIndex;
                path.dragLabel = def.dragLabel;
            }

            // ---- the streamed world --------------------------------------
            var worldGO = new GameObject("CityWorld");
            var world = worldGO.AddComponent<CityWorld>();
            world.player = player.transform;
            world.materials = CityMaterials();
            RegisterSeasonalGround("CityGround", CityTexDir + "/city_grass.png",
                                   world.materials[(int)CityMeshes.Slot.Ground], Color.white, "grass");

            // RaceManager + applier when there is a path, the free-roam
            // session GO otherwise — BuildCameraAndHUD makes that call.
            BuildCameraAndHUD(player, cars, path, lightGO.GetComponent<Light>());

            var mode = worldGO.AddComponent<CityMode>();
            mode.player = player;
            mode.world = world;
            mode.venueName = def.IsRoam ? "CHARLOTTE" : def.name;
            mode.routeId = def.cityRoute ?? "";
            mode.routeLabel = def.dragLabel ?? "";
            mode.aiCars = aiCars;

            var hudGO = GameObject.Find("HUDCanvas");
            if (hudGO != null)
            {
                var hud = hudGO.GetComponent<RaceHUD>();
                if (hud != null) hud.world = world;
            }

            var systems = new GameObject("GameSystems");
            systems.AddComponent<PSXBootstrap>();
            systems.AddComponent<TouchControls>();
            var menu = systems.AddComponent<PauseMenu>();
            menu.playerCar = player;

            AttachSeasonDress();

            string scenePath = ScenePathFor(def);
            EditorSceneManager.SaveScene(scene, scenePath);
            Log("Scene saved: " + scenePath);
            return scenePath;
        }

        /// <summary>Everything the city materials need to exist, for a tool
        /// that runs without a scene build: the drawn surfaces, the facade
        /// copies, the importer pass that keeps them point-filtered. The
        /// preview used to photograph white roads on a fresh sandbox.</summary>
        internal static void EnsureCityTextures()
        {
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            EnsureFolders();
            EnsureCityFolders();
            GenerateCityTextures();
            EnsureCityArt();
            AssetDatabase.Refresh();
            ConfigureTextureImporters();
        }

        static void EnsureCityFolders()
        {
            if (!AssetDatabase.IsValidFolder(CityTexDir))
            {
                var parent = Path.GetDirectoryName(CityTexDir).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(CityTexDir));
            }
        }

        // ------------------------------------------------------------------
        //  Materials, one per CityMeshes.Slot. Big flat surfaces opt out of
        //  affine exactly like the circuit road/ground do.
        // ------------------------------------------------------------------
        /// <summary>One material per slot, in slot order. INTERNAL because
        /// CityPreview needs the same array — a hand-written parallel list in
        /// the preview once photographed uptown with brick on the carriageway.
        /// Two sources for one table is a bug generator; this is the table.</summary>
        internal static Material[] CityMaterials()
        {
            var m = new Material[(int)CityMeshes.Slot.COUNT];
            m[(int)CityMeshes.Slot.Ground] = MakeMat("CityGround", CityTexDir + "/city_grass.png", affine: 0f);
            m[(int)CityMeshes.Slot.Pavement] = MakeMat("CityPavement", CityTexDir + "/city_pavement.png", affine: 0f);
            for (int p = 0; p < CityMeshes.RoadClassCount; p++)
                for (int s = 0; s < CityMeshes.SurfaceCount; s++)
                {
                    var surf = (CityMeshes.Surface)s;
                    string key = ProfileKey(p);
                    m[(int)CityMeshes.SlotOf(p, surf)] =
                        MakeMat("CityRoad_" + key + "_" + SurfaceKey(surf),
                                CityTexDir + "/" + RoadTexFile(key, surf), affine: 0f);
                }
            m[(int)CityMeshes.Slot.Concrete] = MakeMat("CityConcrete", CityTexDir + "/city_concrete.png", affine: 0f);
            m[(int)CityMeshes.Slot.Water] = MakeMat("CityWater", CityTexDir + "/city_water.png", affine: 0f,
                tint: new Color(0.9f, 0.95f, 1f));
            m[(int)CityMeshes.Slot.FacadeTower] = MakeMat("CityFacadeTower", CityTexDir + "/city_facade_tower.jpg");
            m[(int)CityMeshes.Slot.FacadeMid] = MakeMat("CityFacadeMid", CityTexDir + "/city_facade_mid.jpg");
            m[(int)CityMeshes.Slot.FacadeBrick] = MakeMat("CityFacadeBrick", CityTexDir + "/city_facade_brick.jpg");
            m[(int)CityMeshes.Slot.Shops] = MakeMat("CityShops", CityTexDir + "/city_shops.png");
            m[(int)CityMeshes.Slot.FacadeGlass] = MakeMat("CityFacadeGlass", CityTexDir + "/city_facade_glass.png");
            m[(int)CityMeshes.Slot.FacadeHouse] = MakeMat("CityFacadeHouse", CityTexDir + "/city_facade_house.png");
            m[(int)CityMeshes.Slot.RoofTiles] = MakeMat("CityRoofTiles", CityTexDir + "/city_roof_tiles.jpg", affine: 0f);
            m[(int)CityMeshes.Slot.RoofFlat] = MakeMat("CityRoofFlat", CityTexDir + "/city_roof_flat.png", affine: 0f);
            return m;
        }

        static string ProfileKey(int profile) =>
            profile == CityMeshes.JunctionProfile ? "junction" : RoadProfiles.All[profile].key;

        // ------------------------------------------------------------------
        //  Drawn road surfaces. U spans the full paved width, V tiles along.
        //  The stripe fractions are computed from the SAME lane ladder the
        //  runtime derives widths from (RoadProfiles), so paint and pavement
        //  cannot disagree.
        // ------------------------------------------------------------------
        const float LaneM = RoadProfiles.LaneM;

        // ------------------------------------------------------------------
        //  The four road surfaces, straight out of the HTML game.
        //
        //  RG2 (_getAsphaltBaseColor, v8.99.126.50) settled on ONE canonical
        //  pair per material: the markings already tell a major road from a
        //  minor one, so the tarmac only has to say what it is MADE of and
        //  how long it has been there. Kept as the literal hex the HTML game
        //  uses, because these are the colours the user has been looking at
        //  for a year and a near-miss reads as a mistake.
        // ------------------------------------------------------------------
        static readonly Color32[] SurfaceBase =
        {
            new Color32(0x1e, 0x1e, 0x22, 255),   // asphalt new
            new Color32(0x43, 0x40, 0x3e, 255),   // asphalt old
            new Color32(0xc0, 0xb8, 0xa8, 255),   // concrete new
            new Color32(0x98, 0x87, 0x72, 255),   // concrete old
        };

        static bool IsConcrete(CityMeshes.Surface s) =>
            s == CityMeshes.Surface.ConcreteNew || s == CityMeshes.Surface.ConcreteOld;

        static string SurfaceKey(CityMeshes.Surface s) => s switch
        {
            CityMeshes.Surface.AsphaltNew => "asphalt_new",
            CityMeshes.Surface.AsphaltOld => "asphalt_old",
            CityMeshes.Surface.ConcreteNew => "concrete_new",
            _ => "concrete_old",
        };

        internal static string RoadTexFile(string profileKey, CityMeshes.Surface s) =>
            "city_road_" + profileKey + "_" + SurfaceKey(s) + ".png";

        static void GenerateCityTextures()
        {
            for (int s = 0; s < CityMeshes.SurfaceCount; s++)
            {
                var surf = (CityMeshes.Surface)s;
                for (int p = 0; p < RoadProfiles.Count; p++)
                    DrawProfileTex(RoadProfiles.All[p], surf);

                // A junction is a poured slab with no markings on it at all,
                // so it is the base surface and nothing else.
                var captured = surf;
                WriteTexture(CityTexDir + "/" + RoadTexFile("junction", surf),
                    64, 64, (x, y) => Grain(x, y, captured));
            }

            WriteTexture(CityTexDir + "/city_grass.png", 64, 64, (x, y) =>
            {
                float n = Noise(x, y);
                byte g = (byte)(96 + n * 34);
                return new Color32((byte)(58 + n * 26), g, (byte)(44 + n * 18), 255);
            });
            WriteTexture(CityTexDir + "/city_concrete.png", 64, 64, (x, y) =>
            {
                float n = Noise(x + 31, y + 7);
                byte v = (byte)(148 + n * 26);
                return new Color32(v, v, (byte)(v - 4), 255);
            });
            WriteTexture(CityTexDir + "/city_water.png", 64, 64, (x, y) =>
            {
                float n = Noise(x, y * 3);
                return new Color32((byte)(38 + n * 18), (byte)(84 + n * 26), (byte)(128 + n * 30), 255);
            });

            // A curtain wall: 4x4 panes per 8 m repeat, dark mullions, panes
            // that vary a little so a forty-storey slab is not one flat blue.
            // Drawn, because the building pack has no glass in it.
            WriteTexture(CityTexDir + "/city_facade_glass.png", 64, 64, (x, y) =>
            {
                int px = x % 16, py = y % 16;
                if (px < 2 || py < 2) return new Color32(0x2c, 0x30, 0x36, 255);
                int pane = (x / 16) * 7 + (y / 16) * 13;
                float n = Noise(pane, 3 * pane + 1);
                float shade = 0.82f + n * 0.36f + (py - 2) / 14f * 0.10f;
                bool lit = Noise(pane + 5, pane * 3) > 0.9f;
                return lit
                    ? new Color32((byte)(200 * shade), (byte)(186 * shade), (byte)(140 * shade), 255)
                    : new Color32((byte)(104 * shade), (byte)(134 * shade), (byte)(166 * shade), 255);
            });

            // Siding with one window per 6 m x 3.1 m repeat: the whole
            // suburb wears this, so it is deliberately plain.
            WriteTexture(CityTexDir + "/city_facade_house.png", 64, 64, (x, y) =>
            {
                float n = Noise(x, y) - 0.5f;
                bool frame = x >= 20 && x < 44 && y >= 16 && y < 48;
                if (frame)
                {
                    bool border = x < 22 || x >= 42 || y < 18 || y >= 46;
                    bool mullion = x == 31 || x == 32 || y == 31 || y == 32;
                    if (border || mullion) return new Color32(236, 236, 230, 255);
                    float g = 0.9f + n * 0.2f + (y - 18) / 28f * 0.15f;
                    return new Color32((byte)(78 * g), (byte)(104 * g), (byte)(132 * g), 255);
                }
                bool lap = (y % 4) == 0;
                byte v = (byte)Mathf.Clamp((lap ? 178 : 214) + n * 14f, 0, 255);
                return new Color32(v, (byte)(v - 6), (byte)(v - 22), 255);
            });

            // Sidewalk and plaza: poured concrete with a joint every 3 m. The
            // ground of the core, where a downtown is paved edge to edge.
            WriteTexture(CityTexDir + "/city_pavement.png", 64, 64, (x, y) =>
            {
                float n = Noise(x + 7, y + 19) - 0.5f;
                bool joint = (x % 32) == 0 || (y % 32) == 0;
                byte v = (byte)Mathf.Clamp((joint ? 118 : 150) + n * 16f, 0, 255);
                return new Color32(v, v, (byte)(v - 5), 255);
            });
            WriteTexture(CityTexDir + "/city_roof_flat.png", 32, 32, (x, y) =>
            {
                float n = Noise(x * 3 + 11, y * 3 + 5) - 0.5f;
                byte v = (byte)Mathf.Clamp(92 + n * 22f, 0, 255);
                return new Color32(v, v, (byte)(v + 3), 255);
            });
        }

        /// <summary>
        /// One pixel of road surface: the palette colour with a little grain
        /// over it, plus slab joints on the concretes — the seams between
        /// poured bays are the single most recognisable thing about concrete.
        /// </summary>
        static Color32 Grain(int x, int y, CityMeshes.Surface surf)
        {
            var b = SurfaceBase[(int)surf];
            float n = Noise(x, y) - 0.5f;
            float amp = IsConcrete(surf) ? 20f : 14f;
            float joint = IsConcrete(surf) && (y % 21) == 0 ? -26f : 0f;
            return new Color32(
                Chan(b.r, n * amp + joint),
                Chan(b.g, n * amp + joint),
                Chan(b.b, n * amp + joint + 2f), 255);
        }

        static byte Chan(byte b, float d) => (byte)Mathf.Clamp(b + d, 0f, 255f);

        static float Noise(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h >> 8) & 0xFF) / 255f;
            }
        }

        /// <summary>
        /// The painter for one RoadProfiles row.
        ///
        /// Everything on it is placed from the row: the shoulders (asymmetric
        /// on a carriageway), the white edge lines just inside the pavement,
        /// dashed white lane lines within a direction, the DOUBLE YELLOW on an
        /// undivided road — and on a road with a centre turn lane the TWLTL
        /// pair, solid yellow on the through-lane side and dashed yellow on
        /// the turn-lane side of each boundary. A divided carriageway's left
        /// edge line is yellow, as it is on every US freeway.
        ///
        /// 256 px across is the PS1 texture-page ceiling and the reason the
        /// game looks like it does; the widest carriageways go to 512 so a
        /// 12 cm line keeps its two pixels there too (a one-pixel line does
        /// not thin, it flickers).
        /// </summary>
        static void DrawProfileTex(RoadProfiles.Profile pr, CityMeshes.Surface surf)
        {
            float total = pr.Width;
            int width = total > 18f ? 512 : 256, h = 64;
            var whiteDash = new List<float>();
            var yellowSolid = new List<float>();
            var yellowDash = new List<float>();
            float leftEdge = pr.shl + PaintHalf, rightEdge = total - pr.shr - PaintHalf;
            // The MUTCD's rule, not the freeway's: the left edge line of ANY
            // one-way roadway is yellow — a divided arterial's carriageways
            // and a one-way downtown street included.
            bool carriageway = pr.oneway;

            if (pr.oneway)
            {
                for (int i = 1; i < pr.lanes; i++) whiteDash.Add(pr.shl + LaneM * i);
            }
            else
            {
                int perSide = (pr.lanes - (pr.turnLane ? 1 : 0)) / 2;
                float medStart = pr.shl + perSide * LaneM;
                for (int i = 1; i < perSide; i++) whiteDash.Add(pr.shl + LaneM * i);
                if (pr.turnLane)
                {
                    // two lines with a normal gap at each boundary of the turn lane
                    yellowSolid.Add(medStart - PaintHalf * 2f);
                    yellowDash.Add(medStart + PaintHalf * 2f);
                    yellowDash.Add(medStart + LaneM - PaintHalf * 2f);
                    yellowSolid.Add(medStart + LaneM + PaintHalf * 2f);
                    for (int i = 1; i < perSide; i++) whiteDash.Add(medStart + LaneM + LaneM * i);
                }
                else
                {
                    // the double yellow: two normal lines, one normal gap
                    yellowSolid.Add(medStart - PaintHalf * 2f);
                    yellowSolid.Add(medStart + PaintHalf * 2f);
                    for (int i = 1; i < perSide; i++) whiteDash.Add(medStart + LaneM * i);
                }
            }

            WriteTexture(CityTexDir + "/" + RoadTexFile(pr.key, surf), width, h, (x, y) =>
            {
                float m = (x + 0.5f) / width * total;
                var px = Grain(x, y, surf);
                if (Mathf.Abs(m - leftEdge) < PaintHalf) return carriageway ? Yellow : White;
                if (Mathf.Abs(m - rightEdge) < PaintHalf) return White;
                foreach (var ys in yellowSolid) if (Mathf.Abs(m - ys) < PaintHalf) return Yellow;
                // Broken lines: the first quarter of the repeat, which
                // RoadVTile makes 10 feet of a 40 foot cycle.
                if ((y % h) < h / 4)
                {
                    foreach (var yd in yellowDash) if (Mathf.Abs(m - yd) < PaintHalf) return Yellow;
                    foreach (var wd in whiteDash) if (Mathf.Abs(m - wd) < PaintHalf) return White;
                }
                return px;
            });
        }

        // ------------------------------------------------------------------
        //  The same surfaces, for a circuit
        // ------------------------------------------------------------------
        /// <summary>Metres of road per V repeat. The city's own number, so a
        /// circuit's dashes run at the same pitch as a Charlotte street's.</summary>
        internal const float TrackRoadVTile = CityMeshes.RoadVTile;

        /// <summary>
        /// A Charlotte road surface drawn to a circuit's exact width: the lane
        /// ladder FITTED inside whatever width a track was authored at, the
        /// remainder becoming shoulder. Never stretched.
        /// </summary>
        internal static string EnsureTrackRoadTex(float totalM, bool oneWay,
            CityMeshes.Surface surf = CityMeshes.Surface.AsphaltOld)
        {
            EnsureCityFolders();
            const float MinShoulder = 0.4f;
            int lanesPerSide = 1;
            float shoulderM = Mathf.Max(0f, (totalM - (oneWay ? LaneM : LaneM * 2f)) * 0.5f);
            for (int n = 2; n <= 4; n++)
            {
                float sh = (totalM - n * (oneWay ? LaneM : LaneM * 2f)) * 0.5f;
                if (sh < MinShoulder || sh >= shoulderM) continue;
                lanesPerSide = n;
                shoulderM = sh;
            }

            string file = "city_road_track_" + Mathf.RoundToInt(totalM * 10f) +
                          (oneWay ? "_ow" : "") + "_" + SurfaceKey(surf) + ".png";
            DrawRoadTexCore(file, lanesPerSide, 0f, false, shoulderM, totalM, 256, oneWay, surf);
            return CityTexDir + "/" + file;
        }

        /// <summary>Concrete, for anything structural: bridge decks, piers, and
        /// the parapets on them. Shared with the city so a viaduct reads the
        /// same wherever the player meets one.</summary>
        internal static string EnsureConcreteTex()
        {
            EnsureCityFolders();
            WriteTexture(CityTexDir + "/city_concrete.png", 64, 64, (x, y) =>
            {
                float n = Noise(x + 31, y + 7);
                byte v = (byte)(148 + n * 26);
                return new Color32(v, v, (byte)(v - 4), 255);
            });
            return CityTexDir + "/city_concrete.png";
        }

        /// <summary>The circuit painter: symmetric shoulders, a given total
        /// width, the double yellow on a two-way road.</summary>
        static void DrawRoadTexCore(string file, int lanesPerSide, float medianM, bool grassMed,
                                    float shoulderM, float total, int width, bool oneWay,
                                    CityMeshes.Surface surf)
        {
            int laneCount = oneWay ? lanesPerSide : lanesPerSide * 2;
            int h = 64;

            var whiteLines = new List<float>();   // dashed lane separators
            var edgeLines = new List<float> { shoulderM + PaintHalf, total - shoulderM - PaintHalf };
            float cursor = shoulderM;
            for (int i = 1; i < (oneWay ? laneCount : lanesPerSide); i++)
                whiteLines.Add(cursor + LaneM * i);
            float medStart = shoulderM + lanesPerSide * LaneM;
            if (!oneWay)
                for (int i = 1; i < lanesPerSide; i++)
                    whiteLines.Add(medStart + medianM + LaneM * i);

            WriteTexture(CityTexDir + "/" + file, width, h, (x, y) =>
            {
                float m = (x + 0.5f) / width * total;
                var px = Grain(x, y, surf);

                if (!oneWay && medianM > 0.2f && m > medStart && m < medStart + medianM)
                {
                    if (grassMed)
                    {
                        float n = Noise(x, y);
                        return new Color32((byte)(56 + n * 24), (byte)(92 + n * 30), (byte)(44 + n * 16), 255);
                    }
                    if (m < medStart + PaintHalf * 2f || m > medStart + medianM - PaintHalf * 2f)
                        return Yellow;
                    return px;
                }

                if (!oneWay && medianM <= 0.2f)
                {
                    float d = Mathf.Abs(m - (medStart + medianM * 0.5f));
                    if (d > PaintHalf && d < PaintHalf * 3f) return Yellow;
                }

                foreach (var e in edgeLines)
                    if (Mathf.Abs(m - e) < PaintHalf) return White;

                foreach (var wl in whiteLines)
                    if (Mathf.Abs(m - wl) < PaintHalf && (y % h) < h / 4)
                        return White;

                return px;
            });
        }

        /// <summary>
        /// HALF the width of one painted line, in metres. 0.06 makes a 12 cm
        /// line, the middle of the MUTCD's 4-to-6 inch "normal" width. Not
        /// thinner: at 256 px across a 12 m road one pixel is 4.7 cm, and a
        /// line that drops to one pixel does not get thinner, it flickers.
        /// </summary>
        const float PaintHalf = 0.06f;

        static readonly Color32 Yellow = new Color32(196, 160, 40, 255);
        static readonly Color32 White = new Color32(200, 200, 196, 255);

        // ------------------------------------------------------------------
        //  Facades: copied from the owner's pack; shops composed into one
        //  4-front atlas so a whole retail strip is one material.
        // ------------------------------------------------------------------
        static void EnsureCityArt()
        {
            CopyArt("building_01.jpg", "city_facade_tower.jpg");
            CopyArt("building_10.jpg", "city_facade_mid.jpg");
            CopyArt("brick_modern_02.jpg", "city_facade_brick.jpg");
            CopyArt("Roof_tiles.jpg", "city_roof_tiles.jpg");
            ComposeShops("city_shops.png", "Shops.png", "Shops_05.png", "Shops_12.png", "Shops_23.png");
        }

        static void CopyArt(string srcName, string dstName)
        {
            string dst = CityTexDir + "/" + dstName;
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, dst);
            if (File.Exists(full)) return;
            string src = Path.Combine(BuildingsSrc, srcName);
            if (!File.Exists(src)) { Log("WARN: facade source missing " + src); return; }
            File.Copy(src, full);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            Log("Copied " + srcName + " -> " + dst);
        }

        static void ComposeShops(string dstName, params string[] srcNames)
        {
            string dst = CityTexDir + "/" + dstName;
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, dst);
            if (File.Exists(full)) return;

            const int cell = 256;
            var atlas = new Texture2D(cell * srcNames.Length, cell, TextureFormat.RGBA32, false);
            for (int i = 0; i < srcNames.Length; i++)
            {
                string src = Path.Combine(BuildingsSrc, srcNames[i]);
                var tex = new Texture2D(2, 2);
                if (File.Exists(src)) tex.LoadImage(File.ReadAllBytes(src));
                else Log("WARN: shop source missing " + src);
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                        atlas.SetPixel(i * cell + x, y,
                            tex.GetPixelBilinear((x + 0.5f) / cell, (y + 0.5f) / cell));
                UnityEngine.Object.DestroyImmediate(tex);
            }
            atlas.Apply();
            File.WriteAllBytes(full, atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            Log("Composed " + dstName + " from " + srcNames.Length + " shopfronts");
        }

        // ------------------------------------------------------------------
        //  Menu thumbnail, from the real graph — freeways bright, streets dim,
        //  water blue. Baked to Resources so the picker never parses the city
        //  blob to draw a chip.
        // ------------------------------------------------------------------
        static void BakeCityThumbnail(CityMap map)
        {
            const int size = 128;
            var px = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var mn = new Vector2(float.MaxValue, float.MaxValue);
            var mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var e in map.edges)
                foreach (var p in e.pts) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            float span = Mathf.Max(mx.x - mn.x, mx.y - mn.y);
            float scale = (size - 8) / Mathf.Max(span, 1f);
            var c = (mn + mx) * 0.5f;

            void Dot(Vector2 p, Color32 col)
            {
                int x = Mathf.RoundToInt((p.x - c.x) * scale + size * 0.5f);
                int y = Mathf.RoundToInt((p.y - c.y) * scale + size * 0.5f);
                if (x < 0 || y < 0 || x >= size || y >= size) return;
                px[y * size + x] = col;
            }
            void Line(Vector2 a, Vector2 b, Color32 col)
            {
                float d = Vector2.Distance(a, b) * scale;
                int steps = Mathf.Max(1, Mathf.CeilToInt(d));
                for (int i = 0; i <= steps; i++) Dot(Vector2.Lerp(a, b, (float)i / steps), col);
            }

            var water = new Color32(70, 130, 190, 255);
            var street = new Color32(96, 96, 104, 255);
            var art = new Color32(150, 140, 90, 255);
            var fwy = new Color32(255, 190, 70, 255);
            foreach (var w in map.waters)
                for (int i = 0; i + 1 < w.pts.Length; i++) Line(w.pts[i], w.pts[i + 1], water);
            foreach (var e in map.edges)
            {
                if (e.link || e.cls == 0) continue;   // ramps and local streets are noise at 128 px
                var col = e.cls >= 5 ? fwy : e.cls >= 3 ? art : street;
                for (int i = 0; i + 1 < e.pts.Length; i++) Line(e.pts[i], e.pts[i + 1], col);
            }
            Dot(map.uptown, new Color32(255, 255, 255, 255));

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            string path = Root + "/Resources/charlotte_thumb.png";
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            var png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            bool same = File.Exists(full) && File.ReadAllBytes(full).Length == png.Length;
            if (!same)
            {
                File.WriteAllBytes(full, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.filterMode = FilterMode.Point;
                    imp.mipmapEnabled = false;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;
                    imp.isReadable = true;   // the self-test counts its pixels
                    imp.SaveAndReimport();
                }
                Log("Baked charlotte_thumb.png");
            }
        }
    }
}
