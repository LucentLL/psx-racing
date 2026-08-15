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

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Builds the whole game scene: configures asset importers, generates the
    /// circuit (road ribbon, walls, ground, start line), scatters the building /
    /// gas station / tree scenery, assembles the RX-7 cars, and wires the
    /// camera, HUD, audio and race management. Menu: PSX Racing > Build Scene.
    /// </summary>
    public static class PSXRacingBuilder
    {
        const string Root = "Assets/PSXRacing";
        const string GenDir = Root + "/Generated";
        const string MatDir = Root + "/Materials";
        const string ScenePath = Root + "/Scenes/CityCircuit.unity";

        static StringBuilder log = new StringBuilder();
        static Dictionary<Texture, Material> matByTex = new Dictionary<Texture, Material>();
        static Shader psxLit;

        // Circuit control points (x, z) — "Sunset City GP", counterclockwise
        static readonly Vector2[] ControlPoints =
        {
            new Vector2(0, 0),      new Vector2(120, 0),   new Vector2(180, 8),
            new Vector2(215, 40),   new Vector2(220, 95),  new Vector2(205, 150),
            new Vector2(230, 205),  new Vector2(215, 260), new Vector2(160, 285),
            new Vector2(80, 290),   new Vector2(0, 285),   new Vector2(-70, 265),
            new Vector2(-110, 215), new Vector2(-105, 150),new Vector2(-140, 100),
            new Vector2(-135, 40),  new Vector2(-90, -5),
        };

        const float RoadWidth = 12f;
        const float WallOffset = 10f;
        // 2.4 m puts the top edge above the ~1.9 m chase-cam eyeline, so the
        // barrier silhouettes against the sky instead of hiding in the ground band.
        const float WallHeight = 2.4f;
        const float WallThick = 0.35f;
        const float KerbWidth = 0.9f;
        const float Spacing = 4f;

        [MenuItem("PSX Racing/Build Scene")]
        public static void Build()
        {
            log = new StringBuilder();
            matByTex.Clear();
            try
            {
                Log("PSX Racing scene build started " + DateTime.Now);
                EnsureFolders();
                ConfigureTextureImporters();
                ConfigureAudioImporters();
                ConfigureAudioVoiceLimits();
                EnsureRoadLayer();
                psxLit = Shader.Find("PSX/Lit");
                if (psxLit == null) throw new Exception("PSX/Lit shader not found — did shaders compile?");

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var waypoints = BuildWaypoints(out float[] curvatures);
                Log($"Track: {waypoints.Count} waypoints, ~{waypoints.Count * Spacing:0} m");

                var pathGO = new GameObject("Track");
                var path = pathGO.AddComponent<TrackPath>();
                path.waypoints = waypoints.ToArray();
                path.curvatures = curvatures;
                path.spacing = Spacing;
                path.roadWidth = RoadWidth;

                BuildRoad(waypoints, pathGO.transform);
                BuildKerbs(waypoints, pathGO.transform);
                BuildWalls(waypoints, pathGO.transform);
                BuildGround(pathGO.transform);
                BuildStartLine(waypoints, pathGO.transform);
                BuildScenery(waypoints, pathGO.transform);

                var lightGO = BuildLighting();
                var cars = BuildCars(waypoints);
                var player = cars[0];
                BuildCameraAndHUD(player, cars, path);

                var systems = new GameObject("GameSystems");
                systems.AddComponent<PSXBootstrap>();
                systems.AddComponent<TouchControls>();
                var menu = systems.AddComponent<PauseMenu>();
                menu.playerCar = player;
                Log("Added GameSystems (bootstrap + touch controls + pause menu).");

                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                Log("Scene saved: " + ScenePath);
                Log("BUILD OK");
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

        static string ProjectRootPath(string file) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, file);

        static void Log(string msg) { log.AppendLine(msg); Debug.Log("[PSXBuild] " + msg); }

        static void EnsureFolders()
        {
            foreach (var dir in new[] { GenDir, MatDir, Root + "/Scenes" })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
                }
            }
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
                bool dirty = imp.filterMode != FilterMode.Point || imp.mipmapEnabled ||
                             imp.textureCompression != TextureImporterCompression.Uncompressed;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                // 256 is the PS1's own texture-page ceiling, so this is both the
                // authentic look and a 4x cut in download size for mobile.
                imp.maxTextureSize = 256;
                imp.wrapMode = TextureWrapMode.Repeat;
                if (p.EndsWith(".png")) imp.alphaIsTransparency = true;
                if (dirty) { imp.SaveAndReimport(); n++; }
            }
            Log($"Configured {n} texture importers (point filter, no mips).");
        }

        static void ConfigureAudioImporters()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Audio")) { Log("No Audio folder yet."); return; }
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Audio" });
            int n = 0;
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) continue;

                var s = imp.defaultSampleSettings;
                // WebGL cannot stream audio, and these clips are short loops, so
                // decompress on load and keep them resident.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                // These are the whole soundtrack of the game — 0.65 smears the
                // low end of an engine loop badly, which reads as "no bass".
                s.quality = 1.0f;
                s.preloadAudioData = true;
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
            Log($"Configured {n} audio importers (stereo, Vorbis q1.0, decompress-on-load).");
        }

        /// <summary>
        /// The engine voice keeps every band resident so loops never restart out
        /// of phase. Player (18) + three opponents (6 each) needs more than the
        /// default 32 real voices, or Unity virtualizes the quiet ones and the
        /// restart artifact comes back.
        /// </summary>
        const int RoadLayer = 8;

        static void EnsureRoadLayer()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { Log("WARN: TagManager.asset not found"); return; }
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null || layers.arraySize <= RoadLayer) return;
            layers.GetArrayElementAtIndex(RoadLayer).stringValue = "Road";
            so.ApplyModifiedPropertiesWithoutUndo();
            Log("Layer " + RoadLayer + " named 'Road'.");
        }

        static void ConfigureAudioVoiceLimits()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0) { Log("WARN: AudioManager.asset not found"); return; }
            var so = new SerializedObject(assets[0]);
            var real = so.FindProperty("m_RealVoiceCount");
            var virt = so.FindProperty("m_VirtualVoiceCount");
            // Player now runs 23 voices (16 band takes + limiter + 2 intake +
            // skid + 3 turbo) and each opponent 6, so the default 32 is well short.
            if (real != null) real.intValue = 56;
            if (virt != null) virt.intValue = 512;
            so.ApplyModifiedPropertiesWithoutUndo();
            Log($"Audio voice limits set to {(real != null ? real.intValue : -1)} real / " +
                $"{(virt != null ? virt.intValue : -1)} virtual.");
        }

        // ------------------------------------------------------------------
        //  Materials
        // ------------------------------------------------------------------
        static Material MakeMat(string name, string texPath, float cutoff = 0f, Color? tint = null)
        {
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
            if (cutoff > 0f) mat.renderQueue = 2450;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Dictionary<string, Material> matByKey = new Dictionary<string, Material>();

        static Material PSXMaterialFor(Texture tex, string fallbackName, Vector2 scale, Vector2 offset)
        {
            if (tex == null) tex = Texture2D.whiteTexture;
            string texKey = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(texKey)) texKey = tex.name;
            string key = texKey + "|" + scale + "|" + offset;
            if (matByKey.TryGetValue(key, out var cached)) return cached;
            string safe = string.Join("_", (tex.name + "_" + fallbackName).Split(Path.GetInvalidFileNameChars()));
            if (scale != Vector2.one) safe += "_t" + matByKey.Count;
            string assetPath = MatDir + "/scenery_" + safe + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                mat = new Material(psxLit);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            mat.shader = psxLit;
            mat.mainTexture = tex;
            mat.mainTextureScale = scale;     // keep the source material's tiling
            mat.mainTextureOffset = offset;
            mat.color = Color.white;
            EditorUtility.SetDirty(mat);
            matByKey[key] = mat;
            return mat;
        }

        static void ConvertToPSXMaterials(GameObject go)
        {
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
                                             src != null ? src.name : "none", scale, offset);
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
            // Dense Catmull-Rom sampling, then arc-length resample at Spacing
            var dense = new List<Vector3>();
            int cpCount = ControlPoints.Length;
            for (int i = 0; i < cpCount; i++)
            {
                Vector2 p0 = ControlPoints[(i - 1 + cpCount) % cpCount];
                Vector2 p1 = ControlPoints[i];
                Vector2 p2 = ControlPoints[(i + 1) % cpCount];
                Vector2 p3 = ControlPoints[(i + 2) % cpCount];
                for (int s = 0; s < 40; s++)
                {
                    float t = s / 40f;
                    Vector2 pt = 0.5f * ((2f * p1) + (-p0 + p2) * t
                        + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                    dense.Add(new Vector3(pt.x, 0f, pt.y));
                }
            }

            var pts = new List<Vector3>();
            float acc = 0f;
            pts.Add(dense[0]);
            for (int i = 1; i <= dense.Count; i++)
            {
                Vector3 prev = dense[i - 1];
                Vector3 cur = dense[i % dense.Count];
                float d = Vector3.Distance(prev, cur);
                acc += d;
                while (acc >= Spacing)
                {
                    float overshoot = acc - Spacing;
                    pts.Add(Vector3.Lerp(cur, prev, overshoot / Mathf.Max(d, 0.0001f)));
                    acc = overshoot;
                }
            }
            // Drop the last point if it landed on top of the first
            if (Vector3.Distance(pts[pts.Count - 1], pts[0]) < Spacing * 0.5f)
                pts.RemoveAt(pts.Count - 1);

            curvatures = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 a = pts[(i - 1 + pts.Count) % pts.Count];
                Vector3 b = pts[i];
                Vector3 c = pts[(i + 1) % pts.Count];
                float angle = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
                curvatures[i] = angle / Spacing;
            }
            // Light smoothing so AI target speeds don't jitter
            var smoothed = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                float sum = 0f;
                for (int o = -2; o <= 2; o++) sum += curvatures[(i + o + pts.Count) % pts.Count];
                smoothed[i] = sum / 5f;
            }
            curvatures = smoothed;
            return pts;
        }

        static Vector3 RightAt(List<Vector3> pts, int i)
        {
            Vector3 fwd = pts[(i + 1) % pts.Count] - pts[(i - 1 + pts.Count) % pts.Count];
            return Vector3.Cross(Vector3.up, fwd.normalized).normalized;
        }

        static Mesh SaveMesh(Mesh m, string name)
        {
            m.name = name;
            m.RecalculateNormals();
            // Guard: this exact failure shipped once already. Double-sided
            // triangles cancel in RecalculateNormals and the surface goes unlit.
            int bad = 0;
            var nrm = m.normals;
            for (int k = 0; k < nrm.Length; k++) if (nrm[k].sqrMagnitude < 1e-8f) bad++;
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
            var verts = new Vector3[(n + 1) * 2];
            var uvs = new Vector2[(n + 1) * 2];
            var tris = new List<int>();
            float dist = 0f;

            for (int i = 0; i <= n; i++)
            {
                int idx = i % n;
                Vector3 right = RightAt(pts, idx);
                // 12 cm above the ground plane: enough depth separation that the
                // road doesn't z-fight ("flash orange") against it at distance
                Vector3 center = pts[idx] + Vector3.up * 0.12f;
                verts[i * 2] = center - right * (RoadWidth * 0.5f);
                verts[i * 2 + 1] = center + right * (RoadWidth * 0.5f);
                uvs[i * 2] = new Vector2(dist / 24f, 0.02f);
                uvs[i * 2 + 1] = new Vector2(dist / 24f, 0.98f);
                dist += Spacing;
                if (i < n)
                {
                    int a = i * 2;
                    tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }

            var mesh = new Mesh { vertices = verts, uv = uvs, triangles = tris.ToArray() };
            SaveMesh(mesh, "RoadMesh");

            var go = new GameObject("Road");
            go.transform.SetParent(parent, false);
            go.layer = RoadLayer;   // wheels detect tarmac by layer, not by name
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = MakeMat("Road", Root + "/Art/GasStation/Textures/Road.jpg");
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
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
            int n = pts.Count;
            var mat = MakeMat("Kerb", Root + "/Art/GasStation/Textures/Checker.png");
            mat.mainTextureScale = new Vector2(1f, 1f);

            foreach (float side in new[] { -1f, 1f })
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                float dist = 0f;

                for (int i = 0; i <= n; i++)
                {
                    int idx = i % n;
                    Vector3 outw = RightAt(pts, idx) * side;
                    // 1 cm above the road ribbon so it reads as a raised kerb and
                    // cannot z-fight; the road mesh ends exactly at 6 m.
                    Vector3 inner = pts[idx] + Vector3.up * 0.13f + outw * (RoadWidth * 0.5f);
                    Vector3 outer = inner + outw * KerbWidth;
                    int v = verts.Count;
                    verts.Add(inner); verts.Add(outer);
                    // Repeat every 2 m of travel gives the classic red/white dashing.
                    uvs.Add(new Vector2(dist / 2f, 0f));
                    uvs.Add(new Vector2(dist / 2f, 1f));
                    dist += Spacing;
                    if (i < n) tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                }

                var mesh = new Mesh { vertices = verts.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
                SaveMesh(mesh, side < 0 ? "KerbMeshL" : "KerbMeshR");
                var go = new GameObject(side < 0 ? "KerbL" : "KerbR");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.isStatic = true;
            }
            Log("Built kerb strips at the tarmac edge.");
        }

        static void BuildWalls(List<Vector3> pts, Transform parent)
        {
            var wallMat = MakeMat("Wall", Root + "/Art/Roads/T (2).jpg");
            var physMat = GetOrCreatePhysMat("WallPhys", 0.05f, 0f);
            int n = pts.Count;

            foreach (float side in new[] { -1f, 1f })
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                float dist = 0f;

                var wallRoot = new GameObject(side < 0 ? "WallL" : "WallR");
                wallRoot.transform.SetParent(parent, false);

                for (int i = 0; i <= n; i++)
                {
                    int idx = i % n;
                    Vector3 right = RightAt(pts, idx);
                    Vector3 basePos = pts[idx] + right * (WallOffset * side);
                    int v = verts.Count;
                    verts.Add(basePos);
                    verts.Add(basePos + Vector3.up * WallHeight);
                    uvs.Add(new Vector2(dist / 8f, 0f));
                    uvs.Add(new Vector2(dist / 8f, 1f));
                    dist += Spacing;
                    if (i < n)
                    {
                        // Single-sided, facing the road. Emitting the quad twice
                        // with opposite winding to fake two-sidedness makes
                        // RecalculateNormals sum each face normal with its own
                        // negation, so every vertex normal comes out exactly
                        // zero and the barrier renders with ambient light only.
                        tris.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                    }

                    // One collider per drawn segment. Emitting them every other
                    // waypoint made each box a chord across two segments, and the
                    // padding pushed the contact surface inside the drawn face —
                    // so the car stopped before touching anything visible.
                    if (i < n)
                    {
                        int nxt = (idx + 1) % n;
                        Vector3 outw = RightAt(pts, idx) * side;
                        Vector3 next = pts[nxt] + RightAt(pts, nxt) * (WallOffset * side);
                        Vector3 dir = next - basePos; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.01f)
                        {
                            // Offset outward by half the thickness so the box's
                            // INNER face is coplanar with the quad the player sees.
                            Vector3 mid = (basePos + next) * 0.5f
                                        + Vector3.up * (WallHeight * 0.5f)
                                        + outw * (WallThick * 0.5f);
                            var col = new GameObject("Wall");
                            col.transform.SetParent(wallRoot.transform, false);
                            col.transform.position = mid;
                            col.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                            var box = col.AddComponent<BoxCollider>();
                            box.size = new Vector3(WallThick, WallHeight, dir.magnitude);
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

        static void BuildGround(Transform parent)
        {
            // Subdivided grid: fog is computed per-vertex, so a single giant quad
            // would interpolate "fully fogged" across the whole plane.
            const float size = 900f;
            const float tile = 9f;
            // 45 cells meant 20 m triangles. Affine UVs distort in proportion to
            // triangle size and depth contrast, which is worst on ground right
            // under the camera — measured at ~52 px of texture slip. The ground
            // material also opts out of affine entirely (see MakeMat below);
            // this finer grid is for the per-vertex fog and lighting gradient.
            const int cells = 120;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int y = 0; y <= cells; y++)
                for (int x = 0; x <= cells; x++)
                {
                    float fx = x / (float)cells - 0.5f, fy = y / (float)cells - 0.5f;
                    verts.Add(new Vector3(fx * size, 0f, fy * size));
                    uvs.Add(new Vector2(fx * size / tile, fy * size / tile));
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
            go.transform.position = new Vector3(40f, 0f, 140f); // roughly track center
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var groundMat = MakeMat("Ground", Root + "/Art/Roads/T (5).jpg");
            // The one surface big enough for PS1 affine warping to read as a bug
            // rather than as character.
            groundMat.SetFloat("_Affine", 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.26f, 0f);
            box.size = new Vector3(size, 0.5f, size);
            go.isStatic = true;
        }

        static void BuildStartLine(List<Vector3> pts, Transform parent)
        {
            Vector3 right = RightAt(pts, 0);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "StartLine";
            go.transform.SetParent(parent, false);
            go.transform.position = pts[0] + Vector3.up * 0.17f;
            go.transform.rotation = Quaternion.LookRotation(Vector3.down,
                pts[1 % pts.Count] - pts[0]);
            go.transform.localScale = new Vector3(RoadWidth, 3f, 1f);
            var mat = MakeMat("StartLine", Root + "/Art/GasStation/Textures/Checker.png");
            mat.mainTextureScale = new Vector2(8f, 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------
        //  Scenery
        // ------------------------------------------------------------------
        static void BuildScenery(List<Vector3> pts, Transform parent)
        {
            var sceneryRoot = new GameObject("Scenery");
            sceneryRoot.transform.SetParent(parent, false);

            PlaceBuildings(pts, sceneryRoot.transform);
            PlaceGasStation(pts, sceneryRoot.transform);
            PlaceTrees(pts, sceneryRoot.transform);
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
            int placed = 0;
            var rng = new System.Random(42);
            for (int i = 0; i < n; i += 9) // every ~36 m
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.25) continue;
                    // Leave room for the gas station on the back straight
                    if (side > 0f && (pts[i] - new Vector3(80f, 0f, 290f)).sqrMagnitude < 45f * 45f) continue;
                    var src = children[rng.Next(children.Count)];
                    var b = (GameObject)UnityEngine.Object.Instantiate(src.gameObject);
                    b.name = "Building";
                    b.transform.SetParent(parent, false);
                    b.transform.localScale = src.localScale * scale;

                    Vector3 right = RightAt(pts, i);
                    var bounds = CombinedBounds(b);
                    float halfDepth = Mathf.Max(bounds.extents.z, bounds.extents.x) * 0.5f;
                    Vector3 pos = pts[i] + right * side * (WallOffset + 5.5f + halfDepth);
                    Vector3 fwd = -right * side; // face the road
                    b.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                    // Sit on the ground
                    bounds = CombinedBounds(b);
                    pos.y = -bounds.min.y + b.transform.position.y;
                    b.transform.position = new Vector3(pos.x, pos.y, pos.z);

                    ConvertToPSXMaterials(b);
                    var col = b.AddComponent<BoxCollider>();
                    bounds = CombinedBounds(b);
                    col.center = b.transform.InverseTransformPoint(bounds.center);
                    Vector3 ls = b.transform.lossyScale;
                    col.size = new Vector3(bounds.size.x / Mathf.Max(ls.x, 0.001f),
                                           bounds.size.y / Mathf.Max(ls.y, 0.001f),
                                           bounds.size.z / Mathf.Max(ls.z, 0.001f));
                    placed++;
                }
            }
            UnityEngine.Object.DestroyImmediate(template);
            Log($"Placed {placed} buildings.");
        }

        static Bounds CombinedBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        static void PlaceGasStation(List<Vector3> pts, Transform parent)
        {
            // Back straight, outside of the loop
            int idx = NearestWaypointTo(pts, new Vector3(80f, 0f, 290f));
            Vector3 right = RightAt(pts, idx);
            float side = 1f; // outside on the back straight of a CCW loop

            var root = new GameObject("GasStation");
            root.transform.SetParent(parent, false);

            foreach (var file in new[] { "Gas_station.fbx", "Gas_station_Props.fbx" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/GasStation/" + file);
                if (prefab == null) { Log("WARN: missing " + file); continue; }
                var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
                inst.name = Path.GetFileNameWithoutExtension(file);
                inst.transform.SetParent(root.transform, false);
                ConvertToPSXMaterials(inst);
            }

            var bounds = CombinedBounds(root);
            if (bounds.size.y > 15f || bounds.size.y < 2f)
            {
                float s = 7f / Mathf.Max(bounds.size.y, 0.001f);
                root.transform.localScale = Vector3.one * s;
                Log($"Gas station rescaled x{s:0.00} (height was {bounds.size.y:0.0}).");
                bounds = CombinedBounds(root);
            }

            Vector3 pos = pts[idx] + right * side * (WallOffset + 10f + bounds.extents.x);
            root.transform.rotation = Quaternion.LookRotation(-right * side, Vector3.up);
            bounds = CombinedBounds(root);
            root.transform.position += new Vector3(pos.x - bounds.center.x, -bounds.min.y, pos.z - bounds.center.z);

            var col = root.AddComponent<BoxCollider>();
            bounds = CombinedBounds(root);
            col.center = root.transform.InverseTransformPoint(bounds.center);
            col.size = bounds.size;
            Log("Gas station placed near waypoint " + idx);
        }

        static int NearestWaypointTo(List<Vector3> pts, Vector3 pos)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = (pts[i] - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
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

            var mat = MakeMat("Tree", Root + "/Art/Roads/Ar (4).png", cutoff: 0.5f);
            var rng = new System.Random(7);
            int n = pts.Count, placed = 0;
            for (int i = 4; i < n; i += 7)
            {
                float side = (i / 7) % 2 == 0 ? -1f : 1f;
                if (rng.NextDouble() < 0.35) continue;
                Vector3 right = RightAt(pts, i);
                var t = new GameObject("Tree");
                t.transform.SetParent(parent, false);
                t.transform.position = pts[i] + right * side * (WallOffset + 2.6f);
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
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.72f, 0.50f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(16f, -55f, 0f);

            var globals = go.AddComponent<PSXGlobals>();
            globals.sun = light;
            globals.ambient = new Color(0.40f, 0.38f, 0.50f);
            globals.fogColor = new Color(0.88f, 0.56f, 0.42f);
            globals.fogNear = 90f;
            globals.fogFar = 300f;

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
                RenderSettings.skybox = sky;
            }
            RenderSettings.fog = false; // PSX/Lit does its own fog
            return go;
        }

        // ------------------------------------------------------------------
        //  Cars
        // ------------------------------------------------------------------
        static readonly (string name, string tex, float skill, float offset)[] CarSetups =
        {
            ("RX-7 Player", "silver_tornado_silver", 0f, 0f),
            ("RX-7 Red",    "sunrise_red",    1.00f, -1.6f),
            ("RX-7 Blue",   "marina_blue",    0.95f,  1.6f),
            ("RX-7 Yellow", "sunlight_yellow",0.90f, -0.8f),
        };

        static List<CarController> BuildCars(List<Vector3> pts)
        {
            var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Car/2_seater_coupe.obj");
            var wheelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Car/wheel.obj");
            if (bodyPrefab == null) throw new Exception("Car OBJ failed to import.");

            Mesh bodyMesh = bodyPrefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            Mesh wheelMesh = wheelPrefab != null ? wheelPrefab.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            if (bodyMesh == null) throw new Exception("Car body mesh missing after import.");

            // Which way does the model face? The FD's cabin sits rearward, so the
            // roof centroid tells us: front is the opposite sign along Z.
            float roofZ = 0f; int roofCount = 0;
            float maxY = bodyMesh.vertices.Max(v => v.y);
            foreach (var v in bodyMesh.vertices)
                if (v.y > maxY * 0.85f) { roofZ += v.z; roofCount++; }
            roofZ /= Mathf.Max(roofCount, 1);
            float bodyYaw = roofZ < 0f ? 0f : 180f;
            Log($"Car roof centroid z={roofZ:0.00} -> body yaw {bodyYaw}");

            var physMat = GetOrCreatePhysMat("CarPhys", 0.15f, 0.05f);
            var blobMat = MakeBlobShadowMaterial();

            var cars = new List<CarController>();
            var carsRoot = new GameObject("Cars");

            for (int c = 0; c < CarSetups.Length; c++)
            {
                var setup = CarSetups[c];
                bool isPlayer = c == 0;

                // Grid: player at the back of a 2x2 grid, staggered
                int row = isPlayer ? 3 : c - 1;
                float back = 9f + row * 6.5f;
                float lateral = (row % 2 == 0) ? -2.6f : 2.6f;
                Vector3 tangent = (pts[1] - pts[0]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent);
                Vector3 gridPos = pts[0] - tangent * back + right * lateral + Vector3.up * 0.35f;

                var root = new GameObject(setup.name);
                root.transform.SetParent(carsRoot.transform, false);
                root.transform.SetPositionAndRotation(gridPos, Quaternion.LookRotation(tangent, Vector3.up));
                root.layer = 2; // Ignore Raycast: suspension rays skip car colliders

                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 1280f;
                rb.interpolation = isPlayer ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.72f, 0.05f);
                box.size = new Vector3(1.72f, 1.0f, 4.1f);
                box.sharedMaterial = physMat;

                var car = root.AddComponent<CarController>();

                // Body visual
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                body.transform.localRotation = Quaternion.Euler(0f, bodyYaw, 0f);
                body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
                var bodyMat = MakeMat("Car_" + setup.tex, Root + "/Art/Car/textures/" + setup.tex + ".png");
                body.AddComponent<MeshRenderer>().sharedMaterial = bodyMat;

                // Wheels
                var wheelMat = MakeMat("Wheel", Root + "/Art/Car/textures/crystal_white.png");
                var hubs = new Transform[4];
                var meshes = new Transform[4];
                for (int w = 0; w < 4; w++)
                {
                    bool left = w % 2 == 0;
                    var hub = new GameObject("Hub" + w);
                    hub.transform.SetParent(root.transform, false);
                    hub.transform.localPosition = new Vector3(left ? -0.73f : 0.73f, 0.31f, w < 2 ? 1.2125f : -1.2125f);
                    hubs[w] = hub.transform;

                    if (wheelMesh != null)
                    {
                        var wm = new GameObject("Wheel");
                        wm.transform.SetParent(hub.transform, false);
                        wm.transform.localScale = Vector3.one * 0.93f;
                        var holder = wm.transform;
                        // Flip left wheels to face outward
                        var spin = new GameObject("Spin");
                        spin.transform.SetParent(holder, false);
                        spin.AddComponent<MeshFilter>().sharedMesh = wheelMesh;
                        spin.AddComponent<MeshRenderer>().sharedMaterial = wheelMat;
                        wm.transform.localRotation = Quaternion.Euler(0f, left ? 180f : 0f, 0f);
                        meshes[w] = spin.transform;
                    }
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
                blob.transform.localScale = new Vector3(2.3f, 4.6f, 1f);
                blob.GetComponent<MeshRenderer>().sharedMaterial = blobMat;

                AttachAudio(root, car, isPlayer);

                if (isPlayer)
                {
                    root.AddComponent<PlayerCarInput>();
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
                cars.Add(car);
            }
            return cars;
        }

        // Design positions of each recording on the rev range (0 = idle, 1 = limiter).
        // Gaps stay in the 0.10-0.14 range so no band's playback rate ever hits the
        // 0.66-1.50 clamp while that band is still audible.
        static readonly (string file, float frac)[] EngineBands =
        {
            ("rotary_idle",      0.00f),
            ("rotary_idle_low",  0.10f),
            ("rotary_low",       0.22f),
            ("rotary_low_med",   0.35f),
            ("rotary_med",       0.48f),
            ("rotary_med_high",  0.62f),
            ("rotary_high",      0.75f),
            ("rotary_very_high", 0.88f),
        };

        static AudioClip Clip(string name, bool required = true)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".wav");
            if (c == null && required) Log("WARN: missing audio clip " + name);
            return c;
        }

        // Opponents run a 5-rung ladder with no off-throttle takes. The spacing is
        // still geometric (each home RPM 1.58x the last), which keeps every band's
        // playback rate inside the 0.66-1.50 clamp while it is audible.
        static readonly (string file, float frac)[] EngineBandsAI =
        {
            ("rotary_idle",      0.00f),
            ("rotary_low",       0.21f),
            ("rotary_med",       0.42f),
            ("rotary_high",      0.63f),
            ("rotary_very_high", 0.84f),
        };

        static void AttachAudio(GameObject root, CarController car, bool isPlayer)
        {
            var engine = root.AddComponent<EngineAudio>();
            engine.car = car;
            engine.spatial = !isPlayer;
            engine.masterVolume = isPlayer ? 1f : 0.6f;
            engine.useOffTakes = isPlayer;

            var bands = new System.Collections.Generic.List<EngineAudio.RpmBand>();
            foreach (var (file, frac) in (isPlayer ? EngineBands : EngineBandsAI))
            {
                // idle ships as a single take; the rest have on/off-throttle pairs.
                var onClip = Clip(file + "_on", false) ?? Clip(file);
                var offClip = Clip(file + "_off", false) ?? onClip;
                if (onClip == null) continue;
                bands.Add(new EngineAudio.RpmBand
                {
                    name = file, frac = frac, onClip = onClip, offClip = offClip,
                });
            }
            engine.bands = bands.ToArray();
            engine.maxRpmClip = isPlayer ? Clip("rotary_maxRPM") : null;
            if (isPlayer)
            {
                engine.intakeOnClip = Clip("rotary_intake_on");
                engine.intakeOffClip = Clip("rotary_intake_off");
            }
            engine.startupClip = Clip("rotary_startup");
            engine.engineStopClip = Clip("rotary_engine_stop");

            var tires = root.AddComponent<TireAudio>();
            tires.car = car;
            tires.skidClip = Clip("skid_loop");
            tires.spatial = !isPlayer;
            tires.masterVolume = isPlayer ? 1f : 0.5f;

            // The 13B-REW is sequential twin-turbo (GT4 asp: TURBO). Player only:
            // three more voices per car would push the opponents past the mixer's
            // real-voice budget, and their spool is inaudible at race distance.
            if (isPlayer)
            {
                var turbo = root.AddComponent<TurboAudio>();
                turbo.car = car;
                turbo.spoolClip = Clip("turbo_spool");
                turbo.maxLoopClip = Clip("turbo_maxloop");
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
        static void BuildCameraAndHUD(CarController player, List<CarController> cars, TrackPath path)
        {
            // Main (PSX) camera renders into the 320x240 target
            var camGO = new GameObject("PSXCamera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.25f;
            cam.farClipPlane = 360f;
            cam.clearFlags = CameraClearFlags.Skybox;
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
            var fitter = rawGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 4f / 3f;
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
            hud.speedText = MakeText("Speed", new Vector2(1f, 0f), new Vector2(-52f, 34f), 16, TextAnchor.MiddleRight);
            hud.gearText = MakeText("Gear", new Vector2(1f, 0f), new Vector2(-52f, 14f), 14, TextAnchor.MiddleRight);
            hud.centerText = MakeText("Center", new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), 22, TextAnchor.MiddleCenter);
            hud.centerText.rectTransform.sizeDelta = new Vector2(300f, 120f);

            // RPM bar (bottom left)
            var barBG = new GameObject("RPMBarBG");
            barBG.transform.SetParent(hudCanvasGO.transform, false);
            var bgImg = barBG.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            var bgRT = bgImg.rectTransform;
            bgRT.anchorMin = new Vector2(0f, 0f); bgRT.anchorMax = new Vector2(0f, 0f);
            bgRT.anchoredPosition = new Vector2(70f, 18f);
            bgRT.sizeDelta = new Vector2(90f, 8f);

            var barGO = new GameObject("RPMBar");
            barGO.transform.SetParent(barBG.transform, false);
            var barImg = barGO.AddComponent<Image>();
            barImg.color = new Color(1f, 0.85f, 0.3f);
            barImg.type = Image.Type.Filled;
            barImg.fillMethod = Image.FillMethod.Horizontal;
            var barRT = barImg.rectTransform;
            barRT.anchorMin = Vector2.zero; barRT.anchorMax = Vector2.one;
            barRT.offsetMin = new Vector2(1f, 1f); barRT.offsetMax = new Vector2(-1f, -1f);
            hud.rpmFill = barImg;

            // Race manager
            var rmGO = new GameObject("RaceManager");
            var rm = rmGO.AddComponent<RaceManager>();
            rm.path = path;
            rm.playerCar = player;
            rm.allCars = cars;
            rm.totalLaps = 3;

            foreach (var c in cars)
            {
                var ai = c.GetComponent<AIDriver>();
                if (ai != null) ai.path = path;
            }
        }
    }
}
