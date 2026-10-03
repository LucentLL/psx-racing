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
            // No materials on the component (WP-07): CityWorld reads them
            // from the CityKit in Resources, written here for all four
            // scenes, so the next city material is a kit change and not a
            // rebake of every city scene.
            var worldGO = new GameObject("CityWorld");
            var world = worldGO.AddComponent<CityWorld>();
            world.player = player.transform;
            var kit = EnsureCityKit();
            RegisterCitySeasonal(kit);

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
            EnsureCityKit();
        }

        // ------------------------------------------------------------------
        //  THE CITY KIT (WP-07): every city material in one Resources asset.
        // ------------------------------------------------------------------
        const string CityKitPath = Root + "/Resources/" + CityKit.ResourcePath + ".asset";

        /// <summary>The street-lamp posts' pack metal: the house pack's
        /// Metal.jpg (a weathered galvanised grey that already ships with
        /// the house props, so it costs no download), tinted down to the
        /// dark weathered steel the untextured posts were. The sums, in the
        /// linear light the shader works in: the old tint (0.30, 0.31, 0.33)
        /// is (0.073, 0.078, 0.089); the texture ships as RGB565
        /// (ReleaseBudget), which is sampled WITHOUT the sRGB decode, so its
        /// mean (0.50, 0.51, 0.48) arrives as it stands; the tint that lands
        /// on the old tone is therefore (0.146, 0.154, 0.184) linear, which
        /// is written here as the colour it is authored in, (0.42, 0.43, 0.46).</summary>
        /// <summary>WP-25: the creeks' banks - the owner's pack clay
        /// (PSX Textures II dirt_pt_7, the red-brown of Piedmont clay), a
        /// copy in Art/City/Pack like the grass and the concrete. The pack
        /// texel averages half the city grass's brightness (52 against 97):
        /// the tint lifts it to a sunlit bank's, a little warmer.</summary>
        internal static Material CityBankMat() =>
            MakeMat("CityBank", CityBankTexPath, tint: CityBankTint, affine: 0f);
        const string CityBankTexPath = CityPackDir + "/dirt_pt_7_city.png";
        static readonly Color CityBankTint = new Color(1.75f, 1.6f, 1.5f);

        /// <summary>The city's season wardrobe: the ground's grass, and (WP-25)
        /// the creeks' and culvert ditches' clay, which dresses with it - the
        /// dirt tints through the year and the snow turf on a snowy day (red
        /// clay on a white city, every creek, otherwise; CityWorld asks
        /// SeasonDress.Substitute for it as a tile is built).</summary>
        static void RegisterCitySeasonal(CityKit kit)
        {
            RegisterSeasonalGround("CityGround", CityPackDir + "/grass_7_city.png",
                                   kit.MaterialFor(CityMeshes.Slot.Ground), Color.white, "grass");
            RegisterSeasonalGround("CityBank", CityBankTexPath, kit.bank, CityBankTint, "bank");
        }

        /// <summary><see cref="RegisterCitySeasonal"/>'s entries, as a city
        /// scene's SeasonDress carries them, for a tool with no scene
        /// (CityHydroShots' snow shots).</summary>
        internal static SeasonDress.Entry[] CitySeasonEntries()
        {
            ClearSeasonEntries();
            RegisterCitySeasonal(EnsureCityKit());
            var entries = seasonEntries.ToArray();
            ClearSeasonEntries();
            return entries;
        }

        internal static Material CityLampPostMat() =>
            MakeMat("CityLampPost", LifeSimArtDir + "/House/Textures/Metal.jpg",
                    tint: new Color(0.42f, 0.43f, 0.46f), affine: 0f);

        /// <summary>
        /// Charlotte's signs (WP-23): billboards, business cabinets and exit
        /// gantries on one 512 atlas (tools/city/signs_atlas.py composes it
        /// from the owner's packs; every brand fictional), so a tile's signs
        /// are one draw. After dark the faces glow in their own colours
        /// through the atlas's night mask (PSX/Lit _NightMask + _NightWin +
        /// _NightFace): a billboard's floodlit from its foot, a cabinet from
        /// inside; the gantry panels and the steel stay dark (NCDOT's panels
        /// are retroreflective: the headlights light them). Null if the atlas
        /// is missing, and the signs then stand with their renderer off.
        /// </summary>
        internal static Material CitySignsMat()
        {
            const string dir = Root + "/Art/City/Signs";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/CitySigns.png") == null)
            {
                Log("WARN: " + dir + "/CitySigns.png missing (py tools/city/signs_atlas.py) - Charlotte's signs will not draw.");
                return null;
            }
            var mat = MakeMat("CitySigns", dir + "/CitySigns.png", affine: 0f);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/CitySigns_night.png");
            if (mat.HasProperty("_NightMask") && mat.HasProperty("_NightWin") && mat.HasProperty("_NightFace"))
            {
                mat.SetTexture("_NightMask", mask);
                mat.SetFloat("_NightWin", mask != null ? 1f : 0f);
                mat.SetFloat("_NightFace", mask != null ? 1f : 0f);
                EditorUtility.SetDirty(mat);
            }
            else Log("WARN: PSX/Lit has no _NightFace - Charlotte's sign faces stay dark at night.");
            return mat;
        }

        /// <summary>
        /// Charlotte's roadside furniture (WP-15): the utility poles, their
        /// wires and cobra-heads, the street lamps and uptown's acorn posts on
        /// one 256 atlas of the owner's pack wood and metal
        /// (tools/city/furniture_atlas.py), so a tile's furniture is one draw.
        /// PSX/Lit's PSX_FURNITURE variant: the prop atlas's cell-in-the-vertex-
        /// colour wrap (PSX_ATLAS_RECT's), plus the wires stood up square to the
        /// eye a pixel wide at least and faded out past 100 m (CityPoles).
        /// Null if the atlas is missing: the furniture then does not draw and
        /// the lamp posts keep their own mesh.
        /// </summary>
        internal static Material CityFurnitureMat()
        {
            const string tex = Root + "/Art/City/Furniture/CityFurniture.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tex) == null)
            {
                Log("WARN: " + tex + " missing (py tools/city/furniture_atlas.py) - Charlotte's poles and wires will not draw.");
                return null;
            }
            var mat = MakeMat("CityFurniture", tex, affine: 0f);
            if (mat.HasProperty("_AtlasPx")) mat.SetFloat("_AtlasPx", CityPoles.AtlasPx);
            mat.DisableKeyword("PSX_ATLAS_RECT");
            mat.EnableKeyword("PSX_FURNITURE");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Charlotte's junction furniture (WP-26..28, CitySignals): STOP signs
        /// and their posts, signal poles, mast arms, span wires, heads and
        /// stop bars on one 256 atlas (tools/city/signals_atlas.py: the
        /// owner's pack metal and his Roads pack's STOP sign), the same
        /// PSX_FURNITURE variant as the poles', so a tile's junction furniture
        /// is one draw. The lenses' one shared material is made from this at
        /// runtime (CitySignals.LampMaterial). Null if the atlas is missing:
        /// the furniture then does not draw (the signal poles still stand).
        /// </summary>
        internal static Material CitySignalsMat()
        {
            const string tex = Root + "/Art/City/Signals/CitySignals.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(tex) == null)
            {
                Log("WARN: " + tex + " missing (py tools/city/signals_atlas.py) - Charlotte's stop signs and signals will not draw.");
                return null;
            }
            var mat = MakeMat("CitySignals", tex, affine: 0f);
            if (mat.HasProperty("_AtlasPx")) mat.SetFloat("_AtlasPx", 256f);
            mat.DisableKeyword("PSX_ATLAS_RECT");
            mat.EnableKeyword("PSX_FURNITURE");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The city's trees (WP-08), one material per season dress in
        /// <see cref="Seasons"/> order: the stage forest's five atlases
        /// (Art/BRP/Gen, the owner's CC0 retro tree pack composed with every
        /// billboard's painted trunk under the crossing of its cards), so they
        /// cost the city no new texture. Cut out at 0.5 and drawn both sides,
        /// like the forest (one winding per card; PSX/Lit's _Cull 0 flips the
        /// normal to the eye). CityTrees.MaterialFor picks the day's.
        /// </summary>
        internal static Material[] CityTreeMats()
        {
            var mats = new Material[Seasons.DressCount];
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                bool fall = d == (int)Season.Fall;
                string tex = Root + "/Art/BRP/Gen/TreeAtlas" + (fall ? "" : "_" + DressSuffix[d]) + ".png";
                mats[d] = MakeMat("CityTrees" + (fall ? "" : "_" + DressSuffix[d]), tex, cutoff: 0.5f, twoSided: true);
            }
            return mats;
        }

        /// <summary>
        /// How far each atlas cell's painted tree reaches out from its trunk
        /// below each twentieth of its height, as a fraction of the card's
        /// width, the widest of the five dresses (a tree is planted once and
        /// wears all five): [cell * 21 + level], level L covering the rows
        /// under L/20 of the height. CityTrees keeps a card's low foliage off
        /// the pavement with it. The composer slides every billboard's painted
        /// trunk to the cell's middle column (TreeKit.CentreOnTrunk), so the
        /// middle is where the trunk is. Read off the atlas PNGs; nothing drawn.
        /// </summary>
        internal static float[] CityTreeLowReach()
        {
            const int cell = 128, levels = CityTrees.ReachLevels;
            var reach = new float[16 * levels];
            for (int d = 0; d < Seasons.DressCount; d++)
            {
                bool fall = d == (int)Season.Fall;
                string path = ProjectRootPath(Root + "/Art/BRP/Gen/TreeAtlas" + (fall ? "" : "_" + DressSuffix[d]) + ".png");
                if (!File.Exists(path)) { Log("WARN: tree atlas missing " + path); continue; }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                var px = tex.GetPixels32();   // bottom-up rows, as the UVs read it
                int W = tex.width;
                UnityEngine.Object.DestroyImmediate(tex);
                var cum = new float[cell];
                for (int c = 0; c < 16; c++)
                {
                    int x0 = (c % 4) * cell, y0 = (c / 4) * cell;
                    float run = 0f;
                    for (int y = 0; y < cell; y++)
                    {
                        for (int x = 0; x < cell; x++)
                            if (px[(y0 + y) * W + x0 + x].a > 127) run = Mathf.Max(run, Mathf.Abs(x + 0.5f - cell * 0.5f) / cell);
                        cum[y] = run;   // the widest of the rows 0..y
                    }
                    // level L: the rows under L/20 of the height
                    for (int L = 1; L < levels; L++)
                    {
                        int top = Mathf.Clamp(Mathf.CeilToInt(L * cell / (float)(levels - 1)) - 1, 0, cell - 1);
                        reach[c * levels + L] = Mathf.Max(reach[c * levels + L], cum[top]);
                    }
                }
            }
            return reach;
        }

        /// <summary>
        /// Write (or rewrite) Resources/CityKit.asset from the one material
        /// table, CityMaterials(), plus the lamp posts, and hand it back.
        /// Every city scene build and every city tool (EnsureCityTextures)
        /// runs it, so what a tool photographs and what the game draws are
        /// the same asset.
        /// </summary>
        internal static CityKit EnsureCityKit()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Resources"))
                AssetDatabase.CreateFolder(Root, "Resources");
            var kit = AssetDatabase.LoadAssetAtPath<CityKit>(CityKitPath);
            bool made = kit == null;
            if (made) kit = ScriptableObject.CreateInstance<CityKit>();
            kit.slots = CityMaterials();
            kit.slotCount = kit.slots.Length;
            kit.lampPost = CityLampPostMat();
            kit.bank = CityBankMat();
            kit.trees = CityTreeMats();
            kit.treeLowReach = CityTreeLowReach();
            kit.signs = CitySignsMat();
            kit.furniture = CityFurnitureMat();
            kit.signals = CitySignalsMat();
            var shaders = new List<Shader>();
            if (kit.furniture != null && kit.furniture.shader != null && !shaders.Contains(kit.furniture.shader))
                shaders.Add(kit.furniture.shader);
            foreach (var m in kit.slots)
                if (m != null && m.shader != null && !shaders.Contains(m.shader)) shaders.Add(m.shader);
            if (kit.lampPost != null && kit.lampPost.shader != null && !shaders.Contains(kit.lampPost.shader))
                shaders.Add(kit.lampPost.shader);
            if (kit.bank != null && kit.bank.shader != null && !shaders.Contains(kit.bank.shader))
                shaders.Add(kit.bank.shader);
            foreach (var m in kit.trees)
                if (m != null && m.shader != null && !shaders.Contains(m.shader)) shaders.Add(m.shader);
            if (kit.signs != null && kit.signs.shader != null && !shaders.Contains(kit.signs.shader))
                shaders.Add(kit.signs.shader);
            if (kit.signals != null && kit.signals.shader != null && !shaders.Contains(kit.signals.shader))
                shaders.Add(kit.signals.shader);
            foreach (var name in new[] { "PSX/Lit", "PSX/Water", "PSX/LitTransparent" })
            {
                var sh = Shader.Find(name);
                if (sh != null && !shaders.Contains(sh)) shaders.Add(sh);
            }
            kit.shaders = shaders.ToArray();
            int missing = 0;
            foreach (var m in kit.slots) if (m == null) missing++;
            if (missing > 0) Log("WARN: city kit: " + missing + " of " + kit.slots.Length + " slots have no material");
            if (made) AssetDatabase.CreateAsset(kit, CityKitPath);
            else EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            CityKit.Forget();
            return kit;
        }

        /// <summary>
        /// The four city scenes and nothing else (WP-07's one rebake, and the
        /// fast loop for any city change that touches the scenes): the city
        /// prop variants are baked from the full prefabs the last full build
        /// left, then Charlotte and its three race scenes are rebuilt.
        /// Leaves the build settings and every other scene alone, and writes
        /// PSXRacing_city_build_log.txt, never the full build's log (which the
        /// publish gate reads).
        /// </summary>
        [MenuItem("PSX Racing/Build City Scenes Only")]
        public static void BuildCityScenesOnly()
        {
            log = new System.Text.StringBuilder();
            try
            {
                Log("City scenes build started " + DateTime.Now);
                EnsureFolders();
                ConfigureTextureImporters();
                EnsureRoadLayer();
                psxLit = Shader.Find("PSX/Lit");
                if (psxLit == null) throw new Exception("PSX/Lit shader not found - did shaders compile?");
                foreach (var line in CityPropBaker.BakeVariants()) Log("  props " + line);
                int n = 0;
                foreach (var def in TrackCatalog.Scened)
                    if (def.city) { BuildCityScene(def); n++; }
                Log("CITY BUILD OK - " + n + " city scenes");
            }
            catch (Exception e)
            {
                Log("CITY BUILD FAILED: " + e.Message + " | " + e.StackTrace);
                Debug.LogException(e);
            }
            finally
            {
                File.WriteAllText(ProjectRootPath("PSXRacing_city_build_log.txt"), log.ToString());
                AssetDatabase.SaveAssets();
            }
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
            m[(int)CityMeshes.Slot.Ground] = MakeMat("CityGround", CityPackDir + "/grass_7_city.png", affine: 0f);
            // Wet masks (see WetAsphalt in PSXRacingBuilder.cs): every road
            // slot takes the full night; pavement and the shared concrete soak
            // it and go dull. Unprefixed and shared by all four city scenes,
            // which is why wetness is a mask here and a global at runtime.
            m[(int)CityMeshes.Slot.Pavement] = MakeMat("CityPavement", CityPackDir + "/concrete_pt_5_city.png", affine: 0f,
                wet: WetCityPavement);
            for (int p = 0; p < CityMeshes.RoadClassCount; p++)
                for (int s = 0; s < CityMeshes.SurfaceCount; s++)
                {
                    var surf = (CityMeshes.Surface)s;
                    string key = ProfileKey(p);
                    m[(int)CityMeshes.SlotOf(p, surf)] =
                        MakeMat("CityRoad_" + key + "_" + SurfaceKey(surf),
                                CityTexDir + "/" + RoadTexFile(key, surf), affine: 0f,
                                wet: WetAsphalt);
                }
            m[(int)CityMeshes.Slot.Concrete] = MakeMat("CityConcrete", EnsureConcreteTex(), affine: 0f,
                wet: WetCityConcrete);
            m[(int)CityMeshes.Slot.Water] = CityWaterMat();
            // Night windows: the five facades with glass in them carry a mask
            // (Art/City/Night, see WithNightWindows); brick and both roofs are
            // written WITHOUT one, which is not a no-op — it clears whatever a
            // stale asset on disk might still hold.
            m[(int)CityMeshes.Slot.FacadeTower] = WithNightWindows(
                MakeMat("CityFacadeTower", CityTexDir + "/city_facade_tower.jpg"), "city_facade_tower_night.png");
            m[(int)CityMeshes.Slot.FacadeMid] = WithNightWindows(
                MakeMat("CityFacadeMid", CityTexDir + "/city_facade_mid.jpg"), "city_facade_mid_night.png");
            m[(int)CityMeshes.Slot.FacadeBrick] = WithNightWindows(
                MakeMat("CityFacadeBrick", CityTexDir + "/city_facade_brick.jpg"), null);
            m[(int)CityMeshes.Slot.Shops] = WithNightWindows(
                MakeMat("CityShops", CityTexDir + "/city_shops.png"), "city_shops_night.png");
            m[(int)CityMeshes.Slot.FacadeGlass] = WithNightWindows(
                MakeMat("CityFacadeGlass", CityTexDir + "/city_facade_glass.png"), "city_facade_glass_night.png");
            m[(int)CityMeshes.Slot.FacadeHouse] = WithNightWindows(
                MakeMat("CityFacadeHouse", CityTexDir + "/city_facade_house.png"), "city_facade_house_night.png");
            m[(int)CityMeshes.Slot.RoofTiles] = WithNightWindows(
                MakeMat("CityRoofTiles", CityTexDir + "/city_roof_tiles.jpg", affine: 0f), null);
            m[(int)CityMeshes.Slot.RoofFlat] = WithNightWindows(
                MakeMat("CityRoofFlat", CityPackDir + "/concrete_pt_2_city.png", affine: 0f), null);
            return m;
        }

        /// <summary>The city's lakes and rivers: PSX/Water over the pack's water
        /// sheets, like the sea. City meshes carry no depth colour, so every
        /// pixel reads as deep water - which a reservoir is.</summary>
        static Material CityWaterMat()
        {
            var shader = Shader.Find("PSX/Water");
            if (shader == null) return MakeMat("CityWater", Root + "/Art/Water/water_2.png", affine: 0f);
            string assetPath = MatDir + "/CityWater.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, assetPath); }
            mat.shader = shader;
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Water/water_2.png");
            mat.SetTexture("_DeepTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Water/water_1.png"));
            mat.SetColor("_Color", new Color(0.62f, 0.72f, 0.66f));   // a brown-green river, not a sea
            mat.SetColor("_SandColor", new Color(0.50f, 0.46f, 0.36f));
            mat.SetFloat("_OceanWaves", 0f);   // a river takes the ripple, not a swell
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Where the night-window masks live: committed art, made by
        /// <c>py tools/night/window_masks.py</c> from the facade textures
        /// beside them, with hand-written .meta files so their GUIDs survive a
        /// sandbox mirror (design R15, the pink-pizza lesson).</summary>
        const string CityNightDir = CityTexDir + "/Night";

        /// <summary>Warned once per editor session, like MakeMat's _Wet
        /// warning: a PSX/Lit without the window properties is one fact.</summary>
        static bool warnedNoNightWin;

        /// <summary>
        /// Lit windows at night (2026-09-21, the NFS-2015 night pass). The
        /// owner asked for NFS's dark night, and a dark downtown whose every
        /// window is the daytime photograph at a tenth of its brightness reads
        /// as a car park, not a city. PSX/Lit lights a share of the glass
        /// after dark from a per-facade MASK (R = A = glass, G = a random id
        /// per window, B = shopfront, always lit) — the shader side is
        /// <c>_NightMask</c>/<c>_NightWin</c>, gated on <c>_PSXNight</c>, so
        /// a daytime frame is pixel-for-pixel what it was.
        ///
        /// BOTH properties are written on every call, the mask as null and the
        /// switch as 0 when there is no mask: MakeMat LOADS the asset rather
        /// than recreating it, so a facade that once had windows and lost them
        /// (or a mask file deleted) would otherwise go on glowing from the
        /// stale values on disk. The switch follows the texture actually
        /// found, not the name asked for — <c>_NightWin</c> 1 over a missing
        /// mask would sample the shader's black default for nothing.
        ///
        /// The mask shares the facade's UVs; it is written at the facade's
        /// SOURCE size so the importer's 256 px clamp resamples the two
        /// through the same filter and their windows stay in register.
        /// </summary>
        static Material WithNightWindows(Material mat, string maskFile)
        {
            if (mat == null) return null;
            Texture2D mask = null;
            if (!string.IsNullOrEmpty(maskFile))
            {
                string maskPath = CityNightDir + "/" + maskFile;
                mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                if (mask == null)
                    Log("WARN: night window mask missing " + maskPath +
                        " (py tools/night/window_masks.py) — " + mat.name + " stays dark at night.");
            }
            if (mat.HasProperty("_NightMask") && mat.HasProperty("_NightWin"))
            {
                mat.SetTexture("_NightMask", mask);
                mat.SetFloat("_NightWin", mask != null ? 1f : 0f);
                EditorUtility.SetDirty(mat);
            }
            else if (mask != null && !warnedNoNightWin)
            {
                // An older PSX/Lit (or a build run before the shader change
                // landed) still builds — with every window dark.
                warnedNoNightWin = true;
                Log("WARN: PSX/Lit has no _NightMask/_NightWin — night windows not written (first: " + mat.name + ").");
            }
            return mat;
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

            // Grass, pavement, structural concrete, the flat roofs and the
            // water were drawn here; they are the owner's pack textures now
            // (CityPackDir: PSX Textures v3.1 grass_7, PSX Textures II
            // concrete_pt_5 / _1 / _2, each copied at the brightness of the
            // texture it replaced - a deck is road surface, and road colours
            // are not to move - and the water is PSX/Water).

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
        /// game looks like it does - for every profile (owner Q3, roads pass
        /// L2: the 512 exception is gone; the importer capped it anyway). A
        /// line keeps its two pixels by being as wide as two texels where a
        /// texel is wider than 6 cm (RoadProfiles.PaintHalfOf): a one-pixel
        /// line does not thin, it flickers.
        /// </summary>
        static void DrawProfileTex(RoadProfiles.Profile pr, CityMeshes.Surface surf)
        {
            float total = pr.Width;
            int width = RoadProfiles.TexWidthOf(pr), h = 64;
            float ph = RoadProfiles.PaintHalfOf(pr), yh = RoadProfiles.YellowHalfOf(pr);
            // the ONE layout (RoadProfiles.PaintLines), which the line model
            // draws its paint columns from (WP-11b)
            var ms = new List<float>(); var ks = new List<byte>();
            RoadProfiles.PaintLines(pr, ms, ks);
            var whiteDash = new List<float>();
            var yellowSolid = new List<float>();
            var yellowDash = new List<float>();
            float leftEdge = 0f, rightEdge = 0f;
            for (int i = 0; i < ms.Count; i++)
                switch (ks[i])
                {
                    case LineModel.KEdgeP: leftEdge = ms[i]; break;
                    case LineModel.KEdgeM: rightEdge = ms[i]; break;
                    case LineModel.KYellow: yellowSolid.Add(ms[i]); break;
                    case LineModel.KYellowDash: yellowDash.Add(ms[i]); break;
                    default: whiteDash.Add(ms[i]); break;
                }
            // The MUTCD's rule, not the freeway's: the left edge line of ANY
            // one-way roadway is yellow — a divided arterial's carriageways
            // and a one-way downtown street included.
            bool carriageway = pr.oneway;

            WriteTexture(CityTexDir + "/" + RoadTexFile(pr.key, surf), width, h, (x, y) =>
            {
                float m = (x + 0.5f) / width * total;
                var px = Grain(x, y, surf);
                if (Mathf.Abs(m - leftEdge) < ph) return carriageway ? Yellow : White;
                if (Mathf.Abs(m - rightEdge) < ph) return White;
                foreach (var ys in yellowSolid) if (Mathf.Abs(m - ys) < yh) return Yellow;
                // Broken lines: the first quarter of the repeat, which
                // RoadVTile makes 10 feet of a 40 foot cycle.
                if ((y % h) < h / 4)
                {
                    foreach (var yd in yellowDash) if (Mathf.Abs(m - yd) < yh) return Yellow;
                    foreach (var wd in whiteDash) if (Mathf.Abs(m - wd) < ph) return White;
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
            TrackLaneLadder(totalM, oneWay, out int lanesPerSide, out float shoulderM, out float laneM);
            string file = TrackRoadTexFile(totalM, oneWay, surf);
            DrawRoadTexCore(file, lanesPerSide, 0f, false, shoulderM, totalM, 256, oneWay, surf, laneM);
            return CityTexDir + "/" + file;
        }

        static string TrackRoadTexFile(float totalM, bool oneWay, CityMeshes.Surface surf) =>
            "city_road_track_" + Mathf.RoundToInt(totalM * 10f) + (oneWay ? "_ow" : "") + "_" + SurfaceKey(surf) + ".png";

        /// <summary>Where EnsureTrackRoadTex paints a circuit's surface (for
        /// the self-test, which reads it without drawing).</summary>
        internal static string TrackRoadTexPath(float totalM, bool oneWay, CityMeshes.Surface surf) =>
            CityTexDir + "/" + TrackRoadTexFile(totalM, oneWay, surf);

        /// <summary>
        /// The lane ladder a circuit or stage ribbon is painted with: as many
        /// 12 ft lanes a side as leave a shoulder of 0.4 m or more, the rest
        /// split evenly into two shoulders.
        ///
        /// A road NARROWER than its lanes at 12 ft (a two-way road under
        /// 2 x LaneM = 7.3 m: every real-width stage, 6.1-6.7 m) has no
        /// shoulder and its lanes FITTED to it, each total / lanes. The
        /// shoulder used to clamp to zero while the ladder kept the full
        /// 3.658 m lane, which put the double yellow 3.658 m in from the
        /// LEFT edge: on a 6.4 m Parkway loop a 3.60 m lane beside a 2.68 m
        /// one ("one side of the road looks wider than the other", owner
        /// 2026-09-28), while traffic, the grid and the AI all used the
        /// ribbon's centre as the lane boundary. Every width of 7.3 m and up
        /// keeps LaneM exactly, so the circuits' textures do not move.
        /// </summary>
        internal static void TrackLaneLadder(float totalM, bool oneWay,
            out int lanesPerSide, out float shoulderM, out float laneM)
        {
            const float MinShoulder = 0.4f;
            lanesPerSide = 1;
            shoulderM = Mathf.Max(0f, (totalM - (oneWay ? LaneM : LaneM * 2f)) * 0.5f);
            for (int n = 2; n <= 4; n++)
            {
                float sh = (totalM - n * (oneWay ? LaneM : LaneM * 2f)) * 0.5f;
                if (sh < MinShoulder || sh >= shoulderM) continue;
                lanesPerSide = n;
                shoulderM = sh;
            }
            int laneCount = oneWay ? lanesPerSide : lanesPerSide * 2;
            float fitted = (totalM - 2f * shoulderM) / laneCount;
            // Only a road that cannot hold its lanes at 12 ft is fitted; the
            // tolerance keeps float noise on a wide road off the painter.
            laneM = fitted < LaneM - 1e-4f ? fitted : LaneM;
        }

        /// <summary>Concrete, for anything structural: bridge decks, piers, and
        /// the parapets on them. Shared with the city so a viaduct reads the
        /// same wherever the player meets one.</summary>
        internal static string EnsureConcreteTex()
        {
            EnsureCityFolders();
            return CityPackDir + "/concrete_pt_1_city.png";
        }

        /// <summary>The owner's pack textures the city wears, each a copy at
        /// the brightness of the code-drawn one it replaced.</summary>
        const string CityPackDir = CityTexDir + "/Pack";

        /// <summary>The circuit painter: symmetric shoulders, a given total
        /// width, the double yellow on a two-way road. Every lane is
        /// <paramref name="laneM"/> wide (<see cref="TrackLaneLadder"/>), so
        /// on a two-way road the centre line lands at total / 2 whatever the
        /// width - the pixel columns mirror about the middle of the texture,
        /// and the ribbon's u = 0.5 is its centre.</summary>
        static void DrawRoadTexCore(string file, int lanesPerSide, float medianM, bool grassMed,
                                    float shoulderM, float total, int width, bool oneWay,
                                    CityMeshes.Surface surf, float laneM)
        {
            int laneCount = oneWay ? lanesPerSide : lanesPerSide * 2;
            int h = 64;

            var whiteLines = new List<float>();   // dashed lane separators
            var edgeLines = new List<float> { shoulderM + PaintHalf, total - shoulderM - PaintHalf };
            float cursor = shoulderM;
            for (int i = 1; i < (oneWay ? laneCount : lanesPerSide); i++)
                whiteLines.Add(cursor + laneM * i);
            float medStart = shoulderM + lanesPerSide * laneM;
            if (!oneWay)
                for (int i = 1; i < lanesPerSide; i++)
                    whiteLines.Add(medStart + medianM + laneM * i);

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
            {
                if (w.ravine) continue;   // a dry ravine is not water (WP-04b)
                for (int i = 0; i + 1 < w.pts.Length; i++) Line(w.pts[i], w.pts[i + 1], water);
            }
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
