using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE WATER, PS3-PS4 (2026-11-16, the water round; the owner: "A lot of
    /// potentially nice race tracks around Lake Lure. Would be a good time to
    /// update the water visuals as well."). The game half of PSX/Water's
    /// PSX_WATER_HD variant (PSXWaterHD.cginc says what it draws).
    ///
    /// THE SWITCH (part B, 2026-11-17; the owner: "Why hasn't the upgraded
    /// bodies of water been implemented?"). The HD water is ON by default on a
    /// PC (the PC profile, PlatformProfile.PC); phones and WebGL keep the old
    /// water. VISUALS > WATER (HD / CLASSIC, <see cref="WaterPrefs"/>) switches
    /// it live; <c>PSX_WATER_HD=0</c> / <c>=1</c> in the environment overrides
    /// both (0 = the old water exactly: the keyword off, no globals, no copy).
    /// The open sea of the coast stages keeps the old water unless
    /// <c>PSX_WATER_HD_SEA=1</c> (a separate decision).
    ///
    /// THE TIERS, per camera, pushed from the render pipeline's own callback
    /// (the way StreetLights pushes its lamp table) into _PSXWaterTier:
    ///   x  the bed through the water and a soft shore: the camera has URP's
    ///      copies of the frame and its depth (the PC asset requires both for
    ///      every camera; the Mobile asset WebGL and Android ship requires
    ///      neither, and the car-paint probe and the HUD camera turn them off);
    ///   y  the world mirrored in it (screen-space, the wet road's march): a PC,
    ///      VISUALS > REFLECTIONS on, the game camera (the one PSXCameraOutput
    ///      drives) - the same rule as <see cref="WetReflections"/>, but in any
    ///      weather;
    ///   z  the third, finest ripple octave: only with y.
    /// The copies tiers 1-2 read are made ONLY for a camera with water in its
    /// view this frame (part B, item 14: no depth copy in the garage, the home
    /// or a dry stage; <see cref="CopiesWanted"/> also tells the pipeline's
    /// copies-on-demand switch when the opaque copy is needed).
    ///
    /// THE WEATHER ON THE WATER: the day's wind (the cloud deck's, brought down
    /// to the surface) sets the ripples' heading and strength; rain falling
    /// (Seasons.Now) rings, roughens and greys it; the sky's clear share gates
    /// the sun's highlight (no glint under overcast).
    /// </summary>
    public static class WaterLook
    {
        public const string Keyword = "PSX_WATER_HD";
        public const string ShaderName = "PSX/Water";
        /// <summary>The hidden shader and pass that copy the depth attachment
        /// (SpeedBlurFeature.WaterDepthPass draws it; always-included). Not a
        /// pass of PSX/Water: that changed the opaques' order with HD off.</summary>
        public const string DepthCopyShader = "Hidden/PSX/WaterDepthCopy";
        public const string DepthCopyPass = "PSXWaterDepthCopy";

        /// <summary>PSX_WATER_HD_SEA=1: the HD water on the coast stages' open
        /// sea too. Default OFF (2026-11-16): on the Bogue Banks sea the HD
        /// variant lost today's white swash line and the foam fringes round the
        /// sandbars (the water sheet's frames), so a scene with an ocean-swell
        /// sea keeps the old water until that is fixed. Lakes, rivers and the
        /// city take the HD water.</summary>
        public static readonly bool SeaOn = System.Environment.GetEnvironmentVariable("PSX_WATER_HD_SEA") == "1";

        /// <summary>The environment's word, if it says one: 1 = HD, 0 = the old
        /// water, null = the row decides (tools and the owner's .cmd set it).</summary>
        public static readonly bool? EnvOverride = ReadEnv();

        static bool? ReadEnv()
        {
            string s = System.Environment.GetEnvironmentVariable("PSX_WATER_HD");
            if (s == "1") return true;
            if (s == "0") return false;
            return null;
        }

        /// <summary>The HD water, now: the environment's word, else the WATER
        /// row (whose default is HD on a PC, CLASSIC elsewhere).</summary>
        public static bool On => EnvOverride ?? WaterPrefs.HD;

        static int seaScene = int.MinValue;
        static bool seaHere, waterHere;

        /// <summary>The HD variant for the active scene: on, if the scene has
        /// water at all and holds no open-ocean sea (a PSX/Water material with
        /// _OceanWaves) unless PSX_WATER_HD_SEA is 1. One scan per scene, which
        /// also moves the scene's water to <see cref="HDQueue"/>; the city's
        /// streamed tiles register through <see cref="QueueIfHD"/>.</summary>
        public static bool ActiveHere()
        {
            if (!On) return false;
            int h = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetHashCode();
            if (h != seaScene)
            {
                seaScene = h;
                seaHere = false;
                waterHere = false;
                waters.Clear();
                scanBuf.Clear();
                foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                {
                    bool any = false;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null || m.shader.name != ShaderName) continue;
                        scanBuf.Add(m);
                        any = true;
                        if (m.HasProperty("_OceanWaves") && m.GetFloat("_OceanWaves") > 0.5f) seaHere = true;
                    }
                    if (any) waters.Add(r);
                }
                waterHere = waters.Count > 0;
                if (SeaOn || !seaHere) foreach (var m in scanBuf) QueueMaterial(m);
                scanBuf.Clear();
                if (waterHere && (SeaOn || !seaHere)) Warm();
            }
            return waterHere && (SeaOn || !seaHere);
        }

        static readonly List<Material> scanBuf = new List<Material>();
        static readonly List<Material> moved = new List<Material>();
        /// <summary>Every renderer drawing PSX/Water in the active scene (the
        /// scan's, and the city's tiles as they stream in).</summary>
        static readonly List<Renderer> waters = new List<Renderer>();

        /// <summary>THE QUEUE (round 2): PSX/Water's own queue is Geometry, the
        /// old water's, so with the HD water off the picture is today's to the
        /// pixel (the wet road's mirror copies the frame with the water in it,
        /// the sky draws after it). The HD variant must draw after URP's copy of
        /// the frame and the water's depth copy - at the head of the
        /// transparents - so while it is active its water materials move here.</summary>
        public const int HDQueue = 2501;

        static void QueueMaterial(Material m)
        {
            if (m == null || m.shader == null || m.shader.name != ShaderName) return;
            WaterPresets.ApplyByName(m);
            if (m.renderQueue == HDQueue) return;
            m.renderQueue = HDQueue;
            moved.Add(m);
        }

        /// <summary>A renderer built at runtime (the city's streamed tiles, all
        /// through WetReflections.Tag): registered as water, and its water to
        /// the HD queue when the HD water is active here. Nothing at all when
        /// it is off.</summary>
        public static void QueueIfHD(Renderer r)
        {
            if (!On || r == null) return;
            var mats = r.sharedMaterials;
            bool any = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].shader != null && mats[i].shader.name == ShaderName) { any = true; break; }
            if (!any) return;
            ActiveHere();   // the scene's scan first (it clears the list)
            if (!waters.Contains(r)) waters.Add(r);
            if (!waterHere) { waterHere = true; if (SeaOn || !seaHere) Warm(); }
            if (!ActiveHere()) return;
            for (int i = 0; i < mats.Length; i++) QueueMaterial(mats[i]);
        }

        /// <summary>Tools, and the row switched to CLASSIC: every moved material
        /// back to its shader's queue (an edit-mode render must not leave a
        /// material asset at 2501).</summary>
        public static void RestoreQueues()
        {
            foreach (var m in moved) if (m != null) m.renderQueue = -1;
            moved.Clear();
            seaScene = int.MinValue;
        }

        /// <summary>The WATER row changed: the keyword, the queues and the
        /// copies follow at once (no restart).</summary>
        public static void Refresh()
        {
            RestoreQueues();
            ApplyKeyword();
            Debug.Log("[WaterLook] water " + (On ? "HD" : "CLASSIC") + " (VISUALS > WATER)");
        }

        // ---- is there water in this camera's view? ----
        static readonly Plane[] planes = new Plane[6];

        /// <summary>Does any registered water renderer stand in this camera's
        /// view right now (its frustum, its culling mask)? Destroyed renderers
        /// are dropped on the way.</summary>
        public static bool WaterInView(Camera cam)
        {
            if (cam == null || waters.Count == 0) return false;
            // asked three times a camera a frame (the copies switch, the push,
            // the depth copy): once per camera per frame in the game
            if (Application.isPlaying && cam == viewCam && Time.frameCount == viewFrame) return viewAns;
            viewAns = InViewNow(cam);
            viewCam = cam; viewFrame = Time.frameCount;
            return viewAns;
        }

        static Camera viewCam;
        static int viewFrame = -1;
        static bool viewAns;

        static bool InViewNow(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            int mask = cam.cullingMask;
            for (int i = waters.Count - 1; i >= 0; i--)
            {
                var r = waters[i];
                if (r == null) { waters.RemoveAt(i); continue; }
                if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
                if ((mask & (1 << r.gameObject.layer)) == 0) continue;
                if (GeometryUtility.TestPlanesAABB(planes, r.bounds)) return true;
            }
            return false;
        }

        /// <summary>Does <paramref name="cam"/> need the water's depth copy
        /// this frame (the HD tiers 1 and 2, with water in its view)?</summary>
        public static bool DepthCopyFor(Camera cam) => ActiveHere() && TierFor(cam) >= 1;

        /// <summary>Does <paramref name="cam"/> need URP's opaque-colour copy
        /// for the water this frame? (The pipeline's copies-on-demand switch:
        /// the HD water is that copy's only reader.)</summary>
        public static bool CopiesWanted(Camera cam) => ActiveHere() && TierFor(cam) >= 1;

        /// <summary>False once SpeedBlurFeature found no depth-copy shader in
        /// this build: then no camera claims the copies (item 16, m6).</summary>
        public static bool CopyReady = true;

        /// <summary>How many times SpeedBlurFeature enqueued the water's depth
        /// copy (tools: 0 proves it never ran with the HD water off).</summary>
        public static int DepthCopies;

        /// <summary>Tools only: -1 the game's rule; 0 the cheap tier (a
        /// phone's); 1 the bed and the shore, no mirror (a PC with REFLECTIONS
        /// off); 2 the full look - for <see cref="ForceCamera"/> only.</summary>
        public static int ForceTier = -1;
        public static Camera ForceCamera;
        /// <summary>Tools only: the surface wind (m/s, world x,z) instead of
        /// the day's; null = the day's.</summary>
        public static Vector2? ForceWind;
        /// <summary>Tools only: the rain rate 0..1 instead of the weather's.</summary>
        public static float ForceRain = -1f;

        /// <summary>The wind at the water: this share of the cloud deck's.</summary>
        public const float SurfaceWindShare = 0.35f;

        static readonly int TierId = Shader.PropertyToID("_PSXWaterTier");
        static readonly int WindId = Shader.PropertyToID("_PSXWaterWind");
        static readonly int RainId = Shader.PropertyToID("_PSXWaterRain");

        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => EnsureHook();

        /// <summary>Hook the per-camera push (idempotent). The player hooks at
        /// load; the editor's tools hook from WaterShots' InitializeOnLoad.</summary>
        public static void EnsureHook()
        {
            ApplyKeyword();
            if (hooked) return;
            hooked = true;
            Debug.Log(On
                ? "[WaterLook] HD water ON (" + (EnvOverride.HasValue ? "PSX_WATER_HD=1" : "VISUALS > WATER = HD") + ")" +
                  (SeaOn ? ", the open sea too (PSX_WATER_HD_SEA=1)" : "; the open sea keeps the old water")
                : "[WaterLook] HD water OFF (" + (EnvOverride.HasValue ? "PSX_WATER_HD=0" : "VISUALS > WATER = CLASSIC") + "): keyword disabled, no globals, no depth copy");
            // Only where the HD water can ever be on (a PC's row, or the
            // environment saying 1); with the environment saying 0, nothing per
            // camera at all - the old water
            if (EnvOverride == false || !(WaterPrefs.Offered || EnvOverride == true)) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static bool keywordOn;

        static void ApplyKeyword()
        {
            bool want = ActiveHere();
            if (want == keywordOn && Shader.IsKeywordEnabled(Keyword) == want) return;
            keywordOn = want;
            if (want) Shader.EnableKeyword(Keyword);
            else Shader.DisableKeyword(Keyword);
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) => Push(cam);

        /// <summary>This camera's tier, 0..2 (see the class summary); 0 with
        /// no water in its view (no copies are made for it).</summary>
        public static int TierFor(Camera cam)
        {
            if (cam == null || !CopyReady) return 0;
            bool copies = SceneCopies(cam);
            // a tool may force a tier, never one whose textures are not there
            if (ForceTier >= 0 && cam == ForceCamera) return copies ? ForceTier : 0;
            if (!copies) return 0;
            // only the game's own view (and the editor's scene view) pays for
            // the copies: a mirror, a menu or a probe keeps the cheap water
            bool gameView = cam.cameraType == CameraType.Game
                            && cam.TryGetComponent(out PSXCameraOutput output) && output.isActiveAndEnabled;
            if (!gameView && cam.cameraType != CameraType.SceneView) return 0;
            // ...and only while water is in that view (item 14)
            if (!WaterInView(cam)) return 0;
            if (!gameView) return 1;
            if (Application.isMobilePlatform || !ReflectionPrefs.On) return 1;
            return 2;
        }

        /// <summary>Does this camera get the copies the HD tiers read: URP's
        /// copy of the frame after the sky (_CameraOpaqueTexture: the PC asset
        /// requires it, the Mobile asset does not, a camera may turn it off)
        /// and the water's own depth copy (SpeedBlurFeature's pass, on any
        /// base camera)?</summary>
        public static bool SceneCopies(Camera cam)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return false;
            bool colour = asset.supportsCameraOpaqueTexture || CopiesOnDemand;
            if (cam.TryGetComponent(out UniversalAdditionalCameraData data))
            {
                if (data.renderType != CameraRenderType.Base) return false;
                if (data.requiresColorOption != CameraOverrideOption.UsePipelineSettings)
                    colour = data.requiresColorOption == CameraOverrideOption.On;
            }
            return colour;
        }

        /// <summary>Set by the pipeline's copies-on-demand switch (the perf
        /// round's PSX_COPIES_ON_DEMAND, which turns the asset's opaque copy on
        /// only for a camera whose water wants it): the copy is then still
        /// there for the water when <see cref="CopiesWanted"/> says so.</summary>
        public static bool CopiesOnDemand;

        /// <summary>The globals for <paramref name="cam"/>'s frame.</summary>
        public static void Push(Camera cam)
        {
            ApplyKeyword();
            if (!ActiveHere()) return;
            int tier = TierFor(cam);
            Shader.SetGlobalVector(TierId, new Vector4(tier >= 1 ? 1f : 0f, tier >= 2 ? 1f : 0f, tier >= 2 ? 1f : 0f, 0f));
            Weather(out Vector2 wind, out float rain, out float clear);
            float speed = wind.magnitude;
            Vector2 dir = speed > 1e-3f ? wind / speed : new Vector2(0.8f, 0.6f);
            Shader.SetGlobalVector(WindId, new Vector4(dir.x, dir.y, speed, clear));
            Shader.SetGlobalFloat(RainId, rain);
        }

        static long memoKey = long.MinValue;
        static Vector2 memoWind;
        static float memoRain, memoClear = 1f;

        /// <summary>The sky's clear share by its layer (Clear, Partly cloudy,
        /// Overcast, Storm): the sun's (the moon's) highlight on the water.</summary>
        static readonly float[] ClearBySky = { 1f, 0.8f, 0.08f, 0f };

        /// <summary>The wind on the water (m/s, world x,z), the rain on it
        /// (0..1) and the sky's clear share, from the weather now. Memoised on
        /// the weather itself (item 16, m3: a forced weather, a sky or road
        /// override changes it too) and the scene.</summary>
        public static void Weather(out Vector2 wind, out float rain, out float clear)
        {
            var w = Seasons.Now;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetHashCode();
            long key = ((long)w.day * 1000 + (int)(w.clock * 10f)) * 64 + (int)w.sky * 8 + (int)w.precip;
            key = key * 1000 + Mathf.RoundToInt(w.precipRate * 100f);
            key ^= (long)scene << 40;
            if (key != memoKey)
            {
                memoKey = key;
                VolumetricClouds.CloudsFor(w, out _, out _, out Vector2 deck);
                memoWind = deck * SurfaceWindShare;
                memoRain = w.precip == PrecipKind.Rain ? Mathf.Clamp01(Mathf.Max(w.precipRate, 0.25f)) : 0f;
                memoClear = ClearBySky[Mathf.Clamp((int)w.sky, 0, ClearBySky.Length - 1)] * (memoRain > 0f ? 0f : 1f);
            }
            wind = ForceWind ?? memoWind;
            rain = ForceRain >= 0f ? Mathf.Clamp01(ForceRain) : memoRain;
            clear = ForceRain > 0f ? 0f : memoClear;
        }

        /// <summary>The older two-value form (tools).</summary>
        public static void Weather(out Vector2 wind, out float rain) => Weather(out wind, out rain, out _);

        /// <summary>Tools: forget the memo (a tool changed the weather).</summary>
        public static void Invalidate() { memoKey = long.MinValue; }

        // ---- the first sight (item 14) ----
        static bool warmed;

        /// <summary>PSX_WATER_HD is a GLOBAL keyword, so the city's shader warm
        /// (each material's own keywords) never compiles the HD variant: the
        /// first time water came into view it built its pipeline state then - a
        /// hitch. Warm it once, when a scene with HD water starts.</summary>
        static void Warm()
        {
            if (warmed || !Application.isPlaying) return;
            warmed = true;
            var sh = Shader.Find(ShaderName);
            if (sh == null || !sh.isSupported) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var svc = new ShaderVariantCollection();
            int n = 0;
            foreach (var pt in new[] { PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline, PassType.Normal })
            {
                try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(sh, pt, Keyword))) n++; }
                catch (System.ArgumentException) { }
            }
            svc.WarmUp();
            Object.Destroy(svc);
            Debug.Log($"[WaterLook] warmed PSX/Water + {Keyword}: {n} variant(s) in {clock.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    /// <summary>
    /// VISUALS > WATER (part B, item 15): HD, the PS3-PS4 water, or CLASSIC,
    /// the water as it was. Read live (WaterLook.On). Offered only on a PC: a
    /// phone's pipeline makes neither copy the HD water reads, and its variant
    /// has not been compiled for GLES3/Vulkan, so phones keep the old water.
    /// Default HD on a PC since part B passed its checks; PSX_WATER_HD in the
    /// environment still overrides it.
    /// </summary>
    public static class WaterPrefs
    {
        const string PrefKey = "psx.waterhd";
        static int cached = -2;

        /// <summary>Can this platform draw the HD water at all?</summary>
        public static bool Offered => PlatformProfile.PC && !Application.isMobilePlatform
                                      && Application.platform != RuntimePlatform.WebGLPlayer;

        public static bool HD
        {
            get
            {
                if (!Offered) return false;
                if (cached == -2) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                cached = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefKey, cached);
                PlayerPrefs.Save();
                WaterLook.Refresh();
            }
        }

        public static void Toggle() => HD = !HD;
        /// <summary>What is drawn (PSX_WATER_HD, when set, wins over the row).</summary>
        public static string Label => WaterLook.On ? "HD" : "CLASSIC";
    }

    /// <summary>
    /// THE HD WATER'S LOOK PER BODY OF WATER (PSXWaterHD.cginc), the numbers
    /// the stage and city builders write (PSXRacingBuilder.WaterPreset) - and,
    /// since part B, applied again at runtime to every water material the HD
    /// water takes (by its name: *RiverWater, *LakeWater, CityWater), so a
    /// retune reaches Lake Lure, Chimney Rock and Charlotte without a scene
    /// pass. Raw linear numbers: absorb per-metre extinction (rgb, red first),
    /// scatter the water's own in-scattered colour under unit light, look x
    /// ripple repeat (m), y ripple strength, z foam gain, w roughness per m/s
    /// of flow.
    /// </summary>
    public static class WaterPresets
    {
        public struct Look
        {
            public Vector4 absorb, scatter, look;
            public Look(Vector4 absorb, Vector4 scatter, Vector4 look) { this.absorb = absorb; this.scatter = scatter; this.look = look; }
        }

        /// <summary>Bogue Sound and the Atlantic off Emerald Isle:
        /// green-grey, a few metres' visibility.</summary>
        public static readonly Look Sea = new Look(
            new Vector4(0.75f, 0.32f, 0.30f, 0f), new Vector4(0.026f, 0.078f, 0.108f, 0f), new Vector4(11f, 0.4f, 1f, 0.6f));
        /// <summary>Lake Lure: a deep green-blue mountain reservoir, calm
        /// between the gusts, mirroring its ridges (part B: a touch deeper and
        /// greener; foam only in a blow).</summary>
        public static readonly Look Lake = new Look(
            new Vector4(0.55f, 0.20f, 0.24f, 0f), new Vector4(0.008f, 0.032f, 0.030f, 0f), new Vector4(7f, 0.75f, 0.35f, 0.25f));
        /// <summary>The Rocky Broad River (part B, item 12): clear over brown
        /// stones (less absorption, so the bed shows), amber-green, its surface
        /// roughened by the flow at 0.25 per m/s, foam only at the rapids.</summary>
        public static readonly Look River = new Look(
            new Vector4(0.30f, 0.12f, 0.14f, 0f), new Vector4(0.030f, 0.034f, 0.016f, 0f), new Vector4(4f, 1f, 0.8f, 0.25f));
        /// <summary>Charlotte's Catawba, its lakes and creeks: green-brown,
        /// turbid with red clay, slow; no shore foam (part B, item 7: the
        /// frame's depth foamed every gently sloping bank).</summary>
        public static readonly Look City = new Look(
            new Vector4(1.5f, 1.2f, 1.45f, 0f), new Vector4(0.042f, 0.048f, 0.028f, 0f), new Vector4(8f, 0.7f, 0f, 0.6f));

        static readonly int AbsorbId = Shader.PropertyToID("_Absorb");
        static readonly int ScatterId = Shader.PropertyToID("_Scatter");
        static readonly int LookId = Shader.PropertyToID("_WaterLook");

        /// <summary>The look a water material's name says it is, if any.</summary>
        public static bool ForName(string name, out Look look)
        {
            look = default;
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Contains("RiverWater")) { look = River; return true; }
            if (name.Contains("LakeWater")) { look = Lake; return true; }
            if (name.StartsWith("CityWater")) { look = City; return true; }
            return false;
        }

        /// <summary>PSX_WATER_PRESETS=0: the materials keep what their builder
        /// wrote (tools: the round-1 HD look, for a before/after).</summary>
        public static readonly bool RuntimeOn = System.Environment.GetEnvironmentVariable("PSX_WATER_PRESETS") != "0";

        /// <summary>Onto a material whose name names its body of water (the
        /// sea's materials keep what their builder wrote).</summary>
        public static void ApplyByName(Material m)
        {
            if (!RuntimeOn || m == null || !ForName(m.name, out var l) || !m.HasProperty(LookId)) return;
            if (m.GetVector(AbsorbId) != l.absorb) m.SetVector(AbsorbId, l.absorb);
            if (m.GetVector(ScatterId) != l.scatter) m.SetVector(ScatterId, l.scatter);
            if (m.GetVector(LookId) != l.look) m.SetVector(LookId, l.look);
        }
    }
}
