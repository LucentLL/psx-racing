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
    /// What IS baked: the per-class road surfaces (drawn, never sourced — the
    /// punch-clock rule), the facade set copied from the owner's building
    /// pack, one composed shopfront atlas, the materials for every
    /// CityMeshes.Slot, and the menu thumbnail rasterised from the real graph.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string CityTexDir = Root + "/Art/City";
        const string BuildingsSrc =
            @"C:\Users\mcgee\OneDrive\Documents\Game Development\PSX Assets\PSX Racing\Buildings\Buildings\Textures";

        static string BuildCityScene(TrackCatalog.TrackDef def)
        {
            track = def;
            matByTex.Clear();
            matByKey.Clear();

            EnsureCityFolders();
            GenerateCityTextures();
            EnsureCityArt();
            AssetDatabase.Refresh();

            var map = CityMap.Get();
            if (map == null) throw new Exception("charlotte_city.json missing — run tools/city/export_charlotte.mjs");
            Log($"--- {def.name} ({def.id}): {map.edges.Length} edges, {map.nodes.Length} nodes, " +
                $"{map.crossings.Length} grade separations, {map.wspans.Length} water spans");

            BakeCityThumbnail(map);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGO = BuildLighting();

            // ---- spawn: the nearest real street to Trade & Tryon ----------
            if (!map.NearestRoadPoint(map.uptown, 600f, skipLinks: true,
                    out int spawnEdge, out float spawnS, out _))
                throw new Exception("no road found near uptown — is the export sane?");
            var e = map.edges[spawnEdge];
            var sp = e.PointAt(spawnS);
            var tan2 = e.TangentAt(spawnS);
            var spawnPos = new Vector3(sp.x, e.YAt(spawnS) + 0.45f, sp.y);
            var spawnRot = Quaternion.LookRotation(new Vector3(tan2.x, 0f, tan2.y), Vector3.up);
            Log($"spawn: {e.name} at ({spawnPos.x:0}, {spawnPos.z:0})");

            var physMat = GetOrCreatePhysMat("CarPhys", 0.15f, 0.05f);
            var blobMat = MakeBlobShadowMaterial();
            var carsRoot = new GameObject("Cars");
            var player = BuildOneCar(carsRoot.transform, CarSetups[0], isPlayer: true,
                spawnPos, spawnRot, physMat, blobMat);
            var cars = new List<CarController> { player };

            // ---- the streamed world --------------------------------------
            var worldGO = new GameObject("CityWorld");
            var world = worldGO.AddComponent<CityWorld>();
            world.player = player.transform;
            world.materials = CityMaterials();

            BuildCameraAndHUD(player, cars, null, lightGO.GetComponent<Light>());

            var mode = worldGO.AddComponent<CityMode>();
            mode.player = player;
            mode.world = world;

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

            string scenePath = ScenePathFor(def);
            EditorSceneManager.SaveScene(scene, scenePath);
            Log("Scene saved: " + scenePath);
            return scenePath;
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
        /// CityPreview needs the same array: it used to keep a hand-written
        /// parallel list of material names, and the moment the slot enum grew
        /// the road textures the preview started photographing the city with
        /// facade brick on the carriageway and magenta everywhere past the end
        /// of its list. Two sources for one table is a bug generator; this is
        /// the table.</summary>
        internal static Material[] CityMaterials()
        {
            var m = new Material[(int)CityMeshes.Slot.COUNT];
            m[(int)CityMeshes.Slot.Ground] = MakeMat("CityGround", CityTexDir + "/city_grass.png", affine: 0f);
            for (int c = 0; c < CityMeshes.RoadClassCount; c++)
                for (int s = 0; s < CityMeshes.SurfaceCount; s++)
                {
                    var cls = (CityMeshes.RoadClass)c;
                    var surf = (CityMeshes.Surface)s;
                    string file = RoadTexFile(cls, surf);
                    m[(int)CityMeshes.SlotOf(cls, surf)] =
                        MakeMat("CityRoad_" + ClassKey(cls) + "_" + SurfaceKey(surf),
                                CityTexDir + "/" + file, affine: 0f);
                }
            m[(int)CityMeshes.Slot.Concrete] = MakeMat("CityConcrete", CityTexDir + "/city_concrete.png", affine: 0f);
            m[(int)CityMeshes.Slot.Water] = MakeMat("CityWater", CityTexDir + "/city_water.png", affine: 0f,
                tint: new Color(0.9f, 0.95f, 1f));
            m[(int)CityMeshes.Slot.FacadeTower] = MakeMat("CityFacadeTower", CityTexDir + "/city_facade_tower.jpg");
            m[(int)CityMeshes.Slot.FacadeMid] = MakeMat("CityFacadeMid", CityTexDir + "/city_facade_mid.jpg");
            m[(int)CityMeshes.Slot.FacadeBrick] = MakeMat("CityFacadeBrick", CityTexDir + "/city_facade_brick.jpg");
            m[(int)CityMeshes.Slot.Shops] = MakeMat("CityShops", CityTexDir + "/city_shops.png");
            return m;
        }

        // ------------------------------------------------------------------
        //  Drawn road surfaces. U spans the full paved width, V tiles along.
        //  The stripe fractions are computed from the SAME lane ladder the
        //  exporter baked widths from, so paint and pavement cannot disagree.
        // ------------------------------------------------------------------
        const float LaneM = 3.6576f;

        // ------------------------------------------------------------------
        //  The four road surfaces, straight out of the HTML game.
        //
        //  RG2 (_getAsphaltBaseColor, v8.99.126.50) settled on ONE canonical
        //  pair per material after trying to carry road class in the colour as
        //  well — the markings already tell a major road from a minor one, so
        //  the tarmac only has to say what it is MADE of and how long it has
        //  been there:
        //
        //    asphalt  new  #1e1e22   fresh blacktop, near-black
        //    asphalt  old  #43403e   weathered grey, sun-faded oxidation
        //    concrete new  #c0b8a8   clean light cream-grey, freshly poured
        //    concrete old  #988772   warm tan-grey, oil-stained weathered
        //
        //  Kept as the literal hex the HTML game uses rather than as
        //  "about right" greys, because these are the colours the user has been
        //  looking at for a year and a near-miss reads as a mistake.
        // ------------------------------------------------------------------
        static readonly Color32[] SurfaceBase =
        {
            new Color32(0x1e, 0x1e, 0x22, 255),
            new Color32(0x43, 0x40, 0x3e, 255),
            new Color32(0xc0, 0xb8, 0xa8, 255),
            new Color32(0x98, 0x87, 0x72, 255),
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

        static string ClassKey(CityMeshes.RoadClass c) => c switch
        {
            CityMeshes.RoadClass.Minor => "minor",
            CityMeshes.RoadClass.Major => "major",
            CityMeshes.RoadClass.DividedGrass => "divided_grass",
            CityMeshes.RoadClass.DividedAsphalt => "divided_asphalt",
            CityMeshes.RoadClass.Motorway => "motorway",
            CityMeshes.RoadClass.Ramp => "ramp",
            _ => "junction",
        };

        internal static string RoadTexFile(CityMeshes.RoadClass c, CityMeshes.Surface s) =>
            "city_road_" + ClassKey(c) + "_" + SurfaceKey(s) + ".png";

        static void GenerateCityTextures()
        {
            for (int s = 0; s < CityMeshes.SurfaceCount; s++)
            {
                var surf = (CityMeshes.Surface)s;
                // WIDTH IS RESOLUTION ACROSS THE ROAD, and a painted line has
                // to survive it. A major road is 14.6 m across, so at 128 px
                // one pixel is 11 cm and a 12 cm line is one pixel wide — which
                // does not render as a thin line, it renders as a line that
                // flickers on and off as the road turns. 256 halves that. The
                // three wider classes were already there; these two were the
                // ones being sampled at half the paint's own width.
                DrawRoadTex(RoadTexFile(CityMeshes.RoadClass.Minor, surf), lanesPerSide: 1, medianM: 0f, grassMed: false, shoulderM: 0f, width: 256, surf: surf);
                DrawRoadTex(RoadTexFile(CityMeshes.RoadClass.Major, surf), lanesPerSide: 2, medianM: 0f, grassMed: false, shoulderM: 0f, width: 256, surf: surf);
                DrawRoadTex(RoadTexFile(CityMeshes.RoadClass.DividedGrass, surf), lanesPerSide: 3, medianM: LaneM * 6f * 0.25f, grassMed: true, shoulderM: LaneM * 0.5f, width: 256, surf: surf);
                DrawRoadTex(RoadTexFile(CityMeshes.RoadClass.DividedAsphalt, surf), lanesPerSide: 3, medianM: LaneM * 6f * 0.22f, grassMed: false, shoulderM: LaneM * 0.5f, width: 256, surf: surf);
                DrawRoadTex(RoadTexFile(CityMeshes.RoadClass.Motorway, surf), lanesPerSide: 4, medianM: LaneM * 8f * 0.02f, grassMed: false, shoulderM: LaneM * 0.5f, width: 256, surf: surf);
                DrawRoadTex(RoadTexFile(CityMeshes.RoadClass.Ramp, surf), lanesPerSide: 1, medianM: 0f, grassMed: false, shoulderM: 0.3f, width: 128, oneWay: true, surf: surf);

                // A junction is a poured slab with no markings on it at all,
                // so it is the base surface and nothing else.
                var captured = surf;
                WriteTexture(CityTexDir + "/" + RoadTexFile(CityMeshes.RoadClass.Junction, surf),
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
        }

        /// <summary>
        /// One pixel of road surface: the palette colour with a little grain
        /// over it, plus slab joints on the concretes.
        ///
        /// The joints are what make concrete read as concrete rather than as
        /// pale asphalt - a poured carriageway is laid in bays and the seams
        /// between them are the single most recognisable thing about it. They
        /// run across the road (constant V) because that is the way a slab is
        /// poured, and at 21 px on a 64 px tile covering 18 m they land about
        /// every 6 m, which is the real spacing.
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

        /// <summary>Clamped, because asphalt-new sits at 0x1e and the grain
        /// would otherwise wrap a dark pixel round to white.</summary>
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

        // ------------------------------------------------------------------
        //  The same surfaces, for a circuit
        // ------------------------------------------------------------------
        /// <summary>Metres of road per V repeat. The city's own number, so a
        /// circuit's dashes run at the same pitch as a Charlotte street's.
        /// </summary>
        internal const float TrackRoadVTile = CityMeshes.RoadVTile;

        /// <summary>
        /// A Charlotte road surface drawn to a circuit's exact width.
        ///
        /// The circuits used to wear a photographed road JPEG stretched across
        /// the whole carriageway and repeated every 24 m along it, which put a
        /// single blurred centre line down a 12 m road and no lane markings at
        /// all. Charlotte's roads are drawn instead of photographed, with the
        /// paint placed from the real 3.6576 m lane ladder — so they are sharp
        /// at any resolution and the markings are the right SIZE. This fits
        /// that ladder inside whatever width a track was authored at and hands
        /// back the asset path.
        ///
        /// Fitted rather than scaled: the lanes stay 3.6576 m and the remainder
        /// becomes shoulder. Stretching the ladder to the width instead would
        /// give a 20 m road 20 m lanes, which is how the old texture looked
        /// wrong in the first place.
        /// </summary>
        internal static string EnsureTrackRoadTex(float totalM, bool oneWay,
            CityMeshes.Surface surf = CityMeshes.Surface.AsphaltOld)
        {
            EnsureCityFolders();
            // As many real lanes as fit while still leaving a shoulder either
            // side. A road that is 14 m wide is two lanes with generous paved
            // shoulders, not four narrow ones — the ladder does not stretch.
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

        static void DrawRoadTex(string file, int lanesPerSide, float medianM, bool grassMed,
                                float shoulderM, int width, bool oneWay = false,
                                CityMeshes.Surface surf = CityMeshes.Surface.AsphaltOld)
        {
            int laneCount = oneWay ? lanesPerSide : lanesPerSide * 2;
            float carriage = laneCount * LaneM;
            float total = carriage + medianM + shoulderM * 2f;
            DrawRoadTexCore(file, lanesPerSide, medianM, grassMed, shoulderM, total, width, oneWay, surf);
        }

        /// <summary>The painter. Split out from <see cref="DrawRoadTex"/> so a
        /// caller can give the total width instead of deriving it: the city
        /// builds its roads FROM the ladder, a circuit fits the ladder INTO a
        /// width it already has.</summary>
        static void DrawRoadTexCore(string file, int lanesPerSide, float medianM, bool grassMed,
                                    float shoulderM, float total, int width, bool oneWay,
                                    CityMeshes.Surface surf)
        {
            int laneCount = oneWay ? lanesPerSide : lanesPerSide * 2;
            int h = 64;

            // stripe positions in metres from the left edge
            var whiteLines = new List<float>();   // dashed lane separators
            // The edge line sits just INSIDE the carriageway, its own width in
            // from where the lane ladder starts — that is where a white edge
            // line goes on a real road.
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

                // median
                if (!oneWay && medianM > 0.2f && m > medStart && m < medStart + medianM)
                {
                    if (grassMed)
                    {
                        float n = Noise(x, y);
                        return new Color32((byte)(56 + n * 24), (byte)(92 + n * 30), (byte)(44 + n * 16), 255);
                    }
                    // asphalt median with a solid yellow line each side
                    if (m < medStart + PaintHalf * 2f || m > medStart + medianM - PaintHalf * 2f)
                        return Yellow;
                    return px;
                }

                // THE CENTRE LINE IS A DOUBLE YELLOW, and it is the right width.
                // It used to be one band 0.44 m across — seventeen inches of
                // paint down the middle of the road — which is most of what
                // "the proportions for road lines are not realistic" was about.
                // Two normal lines with a normal gap between them is what a
                // no-passing line IS, and it is what both of the reference
                // photographs show.
                if (!oneWay && medianM <= 0.2f)
                {
                    // Measured from the middle of the CARRIAGEWAY, not the
                    // middle of the texture. They agree while the shoulders
                    // match, and the day one of them does not is the day the
                    // centre line stops being between the lanes.
                    float d = Mathf.Abs(m - (medStart + medianM * 0.5f));
                    if (d > PaintHalf && d < PaintHalf * 3f) return Yellow;
                }

                foreach (var e in edgeLines)
                    if (Mathf.Abs(m - e) < PaintHalf) return White;

                // Broken lane line: the first quarter of the texture repeat,
                // which RoadVTile makes 10 feet of a 40 foot cycle.
                foreach (var wl in whiteLines)
                    if (Mathf.Abs(m - wl) < PaintHalf && (y % h) < h / 4)
                        return White;

                return px;
            });
        }

        /// <summary>
        /// HALF the width of one painted line, in metres.
        ///
        /// 0.06 makes a 12 cm line, which is the middle of the MUTCD's 4-to-6
        /// inch "normal" width and what almost every line on an American road
        /// actually is. Every line here was two to four times that — the centre
        /// band 0.44 m, the edge lines 0.28, the lane dashes 0.24 — and against
        /// a correct 3.6576 m lane ladder that is what made the road read as a
        /// toy: the paint was the wrong size, not the lanes.
        ///
        /// It is deliberately not thinner. These textures are capped at 256 px
        /// across (the PS1's own texture-page ceiling, and the reason this game
        /// looks like it does), so on a 12 m road one pixel is 4.7 cm and a
        /// 12 cm line is between two and three of them. A 10 cm line would be
        /// two pixels on a good day and one on a wide road, and a line that
        /// drops to one pixel does not get thinner — it starts to flicker.
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
        //  water blue. Baked to Resources so the picker never parses 1.5 MB
        //  of JSON to draw a chip.
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
                if (e.link) continue;
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
