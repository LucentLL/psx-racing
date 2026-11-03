using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if PSX_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE PIPELINE COMPARISON (owner, 2026-11-02: "I'm not sure HDRP will be
    /// that big of a benefit for the assets I currently own. Can you share
    /// estimate screenshot of each?"). Real renders, not paint-overs: the same
    /// Blue Ridge views with the SAME textures three ways -
    ///   current  today's PSX shaders (vertex lit, as the game draws them,
    ///            at native resolution - no PS1 framebuffer);
    ///   urp      URP Lit per-pixel materials on the same textures, 500 m
    ///            shadows in 4 cascades, SSAO, ACES + bloom;
    ///   hdrp     HDRP Lit on the same textures, physically based sky, the
    ///            sun in lux at a fixed exposure, 800 m shadows, contact
    ///            shadows, SSAO, SSR.
    /// Nothing is saved: the scene is opened, its materials swapped in memory,
    /// each view rendered to PNG. Two launches, because the active pipeline
    /// is a project setting: PSX_COMPARE=urp (current + urp), then the
    /// sandbox's pipeline set to HDRP and PSX_COMPARE=hdrp. HDRP code needs
    /// the package and the PSX_HDRP define, which only the comparison
    /// sandbox has. Output: PSX_COMPARE_OUT (default
    /// C:\Users\mcgee\PSXRelease\compare).
    /// </summary>
    public static class PipelineCompare
    {
        const string Scene = "Assets/PSXRacing/Scenes/BlueRidge.unity";
        const int W = 1920, H = 1080;

        public static void Run()
        {
            string mode = System.Environment.GetEnvironmentVariable("PSX_COMPARE") ?? "urp";
            string outDir = System.Environment.GetEnvironmentVariable("PSX_COMPARE_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = @"C:\Users\mcgee\PSXRelease\compare";
            Directory.CreateDirectory(outDir);
            int code = 0;
            try
            {
                EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
                var views = Views();
                Debug.Log($"[PipelineCompare] {mode}: {views.Count} views, pipeline {GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "built-in"}");
                if (mode == "urp")
                {
                    Shoot(views, outDir, "current");
                    ToUrpLit();
                    Shoot(views, outDir, "urp");
                }
#if PSX_HDRP
                else if (mode == "hdrp")
                {
                    ToHdrp();
                    Shoot(views, outDir, "hdrp");
                }
#endif
                else { Debug.LogError("[PipelineCompare] unknown mode " + mode); code = 1; }
                File.WriteAllText(Path.Combine(outDir, mode + "_done.txt"), System.DateTime.Now.ToString("s"));
            }
            catch (System.Exception e) { Debug.LogError("[PipelineCompare] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        // ---- the views ----
        struct View { public string name; public Vector3 eye, look; public float fov; }

        static List<View> Views()
        {
            var path = Object.FindFirstObjectByType<TrackPath>(FindObjectsInactive.Include);
            if (path == null || path.Count < 20) throw new System.Exception("no TrackPath in " + Scene);
            int i0 = path.Count / 3, i1 = i0 + 40;
            if (TrackCatalog.TryIndexOfShipped("BlueRidge", out int ti))
            {
                var def = TrackCatalog.At(ti);
                if (def.bridges != null && def.bridges.Length > 0)
                {
                    Vector2 best = def.bridges[0];
                    foreach (var b in def.bridges) if (b.y - b.x > best.y - best.x) best = b;
                    i0 = Mathf.Clamp(Mathf.RoundToInt(best.x / path.spacing), 1, path.Count - 2);
                    i1 = Mathf.Clamp(Mathf.RoundToInt(best.y / path.spacing), 1, path.Count - 2);
                }
            }
            int n = Mathf.Max(1, i1 - i0), im = (i0 + i1) / 2;
            Vector3 P(int i) => path.GetPoint(Mathf.Clamp(i, 1, path.Count - 2));
            Vector3 T(int i) => path.GetTangent(Mathf.Clamp(i, 1, path.Count - 2)).normalized;
            var views = new List<View>();

            // the viaduct, the title's postcard: up the slope at its near end, along the deck
            {
                var a = P(i0); var along = T(i0); var right = Vector3.Cross(Vector3.up, along).normalized;
                float uphill = Ground(a + right * 50f) >= Ground(a - right * 50f) ? 1f : -1f;
                var eye = a - along * 35f + right * uphill * 45f + Vector3.up * 35f;
                views.Add(new View { name = "viaduct", eye = Above(eye, 22f), look = P(i0 + (n * 3) / 5) + Vector3.up, fov = 45f });
            }
            // the driver's eye on a forest stretch before it
            {
                int idx = Mathf.Max(2, i0 - Mathf.RoundToInt(600f / path.spacing));
                var along = T(idx); var right = Vector3.Cross(Vector3.up, along).normalized;
                var eye = P(idx) + Vector3.up * 1.3f + right * 1.6f;
                var look = P(idx + Mathf.RoundToInt(40f / path.spacing)) + Vector3.up * 1.0f + right * 1.6f;
                views.Add(new View { name = "road", eye = eye, look = look, fov = 58f });
            }
            // the valley: high and far, the drone film's opening
            {
                var mid = P(im); var along = T(im); var right = Vector3.Cross(Vector3.up, along).normalized;
                var valley = Ground(mid + right * 80f) <= Ground(mid - right * 80f) ? right : -right;
                var eye = mid + valley * 470f + along * 40f + Vector3.up * 185f;
                views.Add(new View { name = "valley", eye = Above(eye, 40f), look = mid, fov = 50f });
            }
            return views;
        }

        static float Ground(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, p.y + 800f, p.z), Vector3.down, out var hit, 3000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point.y : p.y - 1000f;

        static Vector3 Above(Vector3 eye, float clear)
        {
            float g = Ground(eye);
            if (eye.y < g + clear) eye.y = g + clear;
            return eye;
        }

        // ---- rendering ----
        static void Shoot(List<View> views, string outDir, string tag)
        {
            var go = new GameObject("CompareCam");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 6000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.enabled = false;
            Prepare(cam, tag);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            foreach (var v in views)
            {
                cam.fieldOfView = v.fov;
                cam.transform.SetPositionAndRotation(v.eye, Quaternion.LookRotation(v.look - v.eye, Vector3.up));
                cam.targetTexture = rt;
                // several frames: history-based effects (SSAO, SSR, TAA-free AA) settle
                for (int f = 0; f < 12; f++) cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                string file = Path.Combine(outDir, $"{v.name}_{tag}.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Debug.Log($"[PipelineCompare] wrote {file} (eye {v.eye}, look {v.look})");
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            rt.Release();
        }

        static void Prepare(Camera cam, string tag)
        {
            if (tag == "urp")
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.renderShadows = true;
                data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
#if PSX_HDRP
            if (tag == "hdrp")
            {
                var hd = cam.gameObject.AddComponent<HDAdditionalCameraData>();
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
#endif
        }

        // ---- the same textures as sRGB colour (the shipped set is linear data the PSX shader decodes) ----
        static readonly Dictionary<Texture, Texture> colourOf = new Dictionary<Texture, Texture>();
        static Texture Colour(Texture t)
        {
            if (t == null) return null;
            if (colourOf.TryGetValue(t, out var c)) return c;
            c = t;
            if (t is Texture2D t2 && !t2.isDataSRGB)
            {
                try
                {
                    var copy = new Texture2D(t2.width, t2.height, t2.format, t2.mipmapCount > 1, false) { name = t2.name + "_srgb", wrapMode = t2.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                    Graphics.CopyTexture(t2, copy);
                    c = copy;
                }
                catch (System.Exception e) { Debug.LogWarning("[PipelineCompare] sRGB copy failed for " + t2.name + ": " + e.Message); }
            }
            colourOf[t] = c;
            return c;
        }

        static IEnumerable<(Renderer r, Material[] mats)> Renderers()
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                yield return (r, r.sharedMaterials);
        }

        static bool IsPsx(Material m) => m != null && m.shader != null && m.shader.name.StartsWith("PSX/");

        static Texture MainTex(Material m) => m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        static Color Tint(Material m) => m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        static float Cutoff(Material m) => m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0f;
        static bool TwoSided(Material m) => m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f;
        static bool Watery(Material m) => m.shader.name.Contains("Water");

        // ---- URP Lit ----
        static void ToUrpLit()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var map = new Dictionary<Material, Material>();
            int swapped = 0;
            foreach (var (r, mats) in Renderers())
            {
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!IsPsx(m) || m.shader.name == "PSX/Sky") continue;
                    if (!map.TryGetValue(m, out var n))
                    {
                        n = new Material(lit) { name = m.name + "_urplit" };
                        n.SetTexture("_BaseMap", Colour(MainTex(m)));
                        n.SetColor("_BaseColor", Watery(m) ? new Color(0.12f, 0.2f, 0.22f) : Tint(m));
                        n.SetFloat("_Smoothness", Watery(m) ? 0.92f : 0.12f);
                        n.SetFloat("_Metallic", 0f);
                        if (Cutoff(m) > 0f) { n.SetFloat("_AlphaClip", 1f); n.SetFloat("_Cutoff", Cutoff(m)); n.EnableKeyword("_ALPHATEST_ON"); }
                        if (TwoSided(m)) n.SetFloat("_Cull", 0f);
                        map[m] = n;
                    }
                    mats[i] = n; any = true; swapped++;
                }
                if (any) r.sharedMaterials = mats;
            }
            // long shadows, SSAO, ACES + bloom
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 500f;
                urp.shadowCascadeCount = 4;
                var data = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(urp) as ScriptableRendererData[];
                var ssaoType = System.Type.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
                if (data != null && data.Length > 0 && data[0] != null && ssaoType != null)
                {
                    bool has = false;
                    foreach (var f in data[0].rendererFeatures) if (f != null && f.GetType() == ssaoType) { f.SetActive(true); has = true; }
                    if (!has) { var f = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType); f.name = "SSAO"; data[0].rendererFeatures.Add(f); data[0].SetDirty(); }
                }
                else Debug.LogWarning("[PipelineCompare] SSAO feature not added");
            }
            var vol = new GameObject("CompareVolume").AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = prof.Add<UnityEngine.Rendering.Universal.Tonemapping>(true); tm.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.ACES);
            var bl = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true); bl.intensity.Override(0.35f); bl.threshold.Override(1.0f);
            vol.sharedProfile = prof;
            Debug.Log($"[PipelineCompare] URP Lit: {map.Count} materials, {swapped} slots swapped");
        }

#if PSX_HDRP
        /// <summary>The comparison sandbox's one-time switch: an HDRP asset made
        /// and set as the project's pipeline (every quality level). Its own
        /// launch, so the render launch starts with HDRP already active.</summary>
        public static void SetupHdrp()
        {
            int code = 0;
            try
            {
                const string at = "Assets/CompareHDRP.asset";
                var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(at);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                    AssetDatabase.CreateAsset(asset, at);
                }
                GraphicsSettings.defaultRenderPipeline = asset;
                int was = QualitySettings.GetQualityLevel();
                for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = asset; }
                QualitySettings.SetQualityLevel(was, false);
                AssetDatabase.SaveAssets();
                Debug.Log("[PipelineCompare] HDRP set as the pipeline: " + at);
            }
            catch (System.Exception e) { Debug.LogError("[PipelineCompare] HDRP setup FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        // ---- HDRP Lit ----
        static void ToHdrp()
        {
            var lit = Shader.Find("HDRP/Lit");
            if (lit == null) throw new System.Exception("HDRP/Lit not found - is HDRP the active pipeline?");
            var map = new Dictionary<Material, Material>();
            int swapped = 0;
            foreach (var (r, mats) in Renderers())
            {
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!IsPsx(m)) continue;
                    if (m.shader.name == "PSX/Sky") { mats[i] = null; any = true; continue; }
                    if (!map.TryGetValue(m, out var n))
                    {
                        n = new Material(lit) { name = m.name + "_hdlit" };
                        n.SetTexture("_BaseColorMap", Colour(MainTex(m)));
                        n.SetColor("_BaseColor", Watery(m) ? new Color(0.12f, 0.2f, 0.22f) : Tint(m));
                        n.SetFloat("_Smoothness", Watery(m) ? 0.92f : 0.12f);
                        n.SetFloat("_Metallic", 0f);
                        if (Cutoff(m) > 0f) { HDMaterial.SetAlphaClipping(n, true); HDMaterial.SetAlphaCutoff(n, Cutoff(m)); }
                        if (TwoSided(m)) HDMaterial.SetDoubleSided(n, true);
                        HDMaterial.ValidateMaterial(n);
                        map[m] = n;
                    }
                    mats[i] = n; any = true; swapped++;
                }
                if (any) r.sharedMaterials = mats;
            }
            // the sun in lux, everything else off
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.GetComponent<HDAdditionalLightData>() == null) l.gameObject.AddComponent<HDAdditionalLightData>();
                if (l.type == LightType.Directional)
                {
                    l.lightUnit = LightUnit.Lux;
                    l.intensity = 100000f;
                    l.shadows = LightShadows.Soft;
                }
                else l.enabled = false;
            }
            var vol = new GameObject("CompareVolume").AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var env = prof.Add<VisualEnvironment>(true); env.skyType.Override((int)SkyType.PhysicallyBased);
            prof.Add<PhysicallyBasedSky>(true);
            var ex = prof.Add<Exposure>(true); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(14.5f);
            var sh = prof.Add<HDShadowSettings>(true); sh.maxShadowDistance.Override(800f);
            prof.Add<ContactShadows>(true).enable.Override(true);
            prof.Add<ScreenSpaceAmbientOcclusion>(true).intensity.Override(1f);
            prof.Add<ScreenSpaceReflection>(true).enabled.Override(true);
            var fog = prof.Add<Fog>(true); fog.enabled.Override(false);
            prof.Add<UnityEngine.Rendering.HighDefinition.Tonemapping>(true).mode.Override(UnityEngine.Rendering.HighDefinition.TonemappingMode.ACES);
            vol.sharedProfile = prof;
            Debug.Log($"[PipelineCompare] HDRP Lit: {map.Count} materials, {swapped} slots swapped");
        }
#endif
    }
}
