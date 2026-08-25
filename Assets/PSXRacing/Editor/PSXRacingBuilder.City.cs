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
        static Material[] CityMaterials()
        {
            var m = new Material[(int)CityMeshes.Slot.COUNT];
            m[(int)CityMeshes.Slot.Ground] = MakeMat("CityGround", CityTexDir + "/city_grass.png", affine: 0f);
            m[(int)CityMeshes.Slot.RoadMinor] = MakeMat("CityRoadMinor", CityTexDir + "/city_road_minor.png", affine: 0f);
            m[(int)CityMeshes.Slot.RoadMajor] = MakeMat("CityRoadMajor", CityTexDir + "/city_road_major.png", affine: 0f);
            m[(int)CityMeshes.Slot.DividedGrass] = MakeMat("CityRoadDivG", CityTexDir + "/city_road_divided_grass.png", affine: 0f);
            m[(int)CityMeshes.Slot.DividedAsphalt] = MakeMat("CityRoadDivA", CityTexDir + "/city_road_divided_asphalt.png", affine: 0f);
            m[(int)CityMeshes.Slot.Motorway] = MakeMat("CityMotorway", CityTexDir + "/city_road_motorway.png", affine: 0f);
            m[(int)CityMeshes.Slot.Ramp] = MakeMat("CityRamp", CityTexDir + "/city_road_ramp.png", affine: 0f);
            m[(int)CityMeshes.Slot.Junction] = MakeMat("CityJunction", CityTexDir + "/city_junction.png", affine: 0f);
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

        static void GenerateCityTextures()
        {
            DrawRoadTex("city_road_minor.png", lanesPerSide: 1, medianM: 0f, grassMed: false, shoulderM: 0f, width: 128);
            DrawRoadTex("city_road_major.png", lanesPerSide: 2, medianM: 0f, grassMed: false, shoulderM: 0f, width: 128);
            DrawRoadTex("city_road_divided_grass.png", lanesPerSide: 3, medianM: LaneM * 6f * 0.25f, grassMed: true, shoulderM: LaneM * 0.5f, width: 256);
            DrawRoadTex("city_road_divided_asphalt.png", lanesPerSide: 3, medianM: LaneM * 6f * 0.22f, grassMed: false, shoulderM: LaneM * 0.5f, width: 256);
            DrawRoadTex("city_road_motorway.png", lanesPerSide: 4, medianM: LaneM * 8f * 0.02f, grassMed: false, shoulderM: LaneM * 0.5f, width: 256);
            DrawRoadTex("city_road_ramp.png", lanesPerSide: 1, medianM: 0f, grassMed: false, shoulderM: 0.3f, width: 64, oneWay: true);

            // plain surfaces
            WriteTexture(CityTexDir + "/city_junction.png", 64, 64, (x, y) =>
                Asphalt(x, y));
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

        static Color32 Asphalt(int x, int y)
        {
            float n = Noise(x, y);
            byte v = (byte)(52 + n * 14);
            return new Color32(v, v, (byte)(v + 2), 255);
        }

        static float Noise(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h >> 8) & 0xFF) / 255f;
            }
        }

        static void DrawRoadTex(string file, int lanesPerSide, float medianM, bool grassMed,
                                float shoulderM, int width, bool oneWay = false)
        {
            int laneCount = oneWay ? lanesPerSide : lanesPerSide * 2;
            float carriage = laneCount * LaneM;
            float total = carriage + medianM + shoulderM * 2f;
            int h = 64;

            // stripe positions in metres from the left edge
            var whiteLines = new List<float>();   // dashed lane separators
            var edgeLines = new List<float> { shoulderM + 0.15f, total - shoulderM - 0.15f };
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
                var px = Asphalt(x, y);

                // median
                if (!oneWay && medianM > 0.2f && m > medStart && m < medStart + medianM)
                {
                    if (grassMed)
                    {
                        float n = Noise(x, y);
                        return new Color32((byte)(56 + n * 24), (byte)(92 + n * 30), (byte)(44 + n * 16), 255);
                    }
                    // asphalt median with a solid yellow line each side
                    if (m < medStart + 0.35f || m > medStart + medianM - 0.35f)
                        return new Color32(196, 160, 40, 255);
                    return px;
                }
                // centre line on undivided two-way roads
                if (!oneWay && medianM <= 0.2f && Mathf.Abs(m - total * 0.5f) < 0.22f)
                    return new Color32(196, 160, 40, 255);

                foreach (var e in edgeLines)
                    if (Mathf.Abs(m - e) < 0.14f) return new Color32(200, 200, 196, 255);

                foreach (var wl in whiteLines)
                    if (Mathf.Abs(m - wl) < 0.12f && (y % 16) < 9)
                        return new Color32(190, 190, 186, 255);

                return px;
            });
        }

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
