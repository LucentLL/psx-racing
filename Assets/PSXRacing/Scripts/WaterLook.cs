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
    /// THE SWITCH (round 2, 2026-11-16: opt-in until the owner approves it).
    /// The HD water is OFF unless <c>PSX_WATER_HD=1</c> is in the environment
    /// (tools/PSXRacing_WaterHD.cmd beside the exe sets it). OFF: the global
    /// keyword stays off, PSX/Water draws exactly the code it drew before this
    /// round, no hook is installed and the depth copy never runs. Phones and
    /// WebGL cannot set the variable, so they are always OFF.
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
    /// So a phone draws vertex depth, the sky's mirror and two octaves; a PC
    /// with REFLECTIONS off adds the bed and the soft shore; the full look adds
    /// the world in the water and the fine octave.
    ///
    /// THE WEATHER ON THE WATER: the day's wind (the cloud deck's, brought down
    /// to the surface) sets the ripples' heading and strength; rain falling
    /// (Seasons.Now) rings it.
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

        static int seaScene = int.MinValue;
        static bool seaHere;

        /// <summary>The HD variant for the active scene: on, unless it holds an
        /// open-ocean sea (a PSX/Water material with _OceanWaves) and
        /// PSX_WATER_HD_SEA is not 1. One scan per scene, which also moves the
        /// scene's water to <see cref="HDQueue"/> when the HD water is active.</summary>
        public static bool ActiveHere()
        {
            if (!On) return false;
            int h = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetHashCode();
            if (h != seaScene)
            {
                seaScene = h;
                seaHere = false;
                scanBuf.Clear();
                foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null || m.shader.name != ShaderName) continue;
                        scanBuf.Add(m);
                        if (m.HasProperty("_OceanWaves") && m.GetFloat("_OceanWaves") > 0.5f) seaHere = true;
                    }
                if (SeaOn || !seaHere) foreach (var m in scanBuf) QueueMaterial(m);
                scanBuf.Clear();
            }
            return SeaOn || !seaHere;
        }

        static readonly System.Collections.Generic.List<Material> scanBuf = new System.Collections.Generic.List<Material>();
        static readonly System.Collections.Generic.List<Material> moved = new System.Collections.Generic.List<Material>();

        /// <summary>THE QUEUE (round 2): PSX/Water's own queue is Geometry, the
        /// old water's, so with the HD water off the picture is today's to the
        /// pixel (the wet road's mirror copies the frame with the water in it,
        /// the sky draws after it). The HD variant must draw after URP's copy of
        /// the frame and the water's depth copy - at the head of the
        /// transparents - so while it is active its water materials move here.</summary>
        public const int HDQueue = 2501;

        static void QueueMaterial(Material m)
        {
            if (m == null || m.shader == null || m.shader.name != ShaderName || m.renderQueue == HDQueue) return;
            m.renderQueue = HDQueue;
            moved.Add(m);
        }

        /// <summary>A renderer built at runtime (the city's streamed tiles, all
        /// through WetReflections.Tag): its water to the HD queue when the HD
        /// water is active here. Nothing at all when it is off.</summary>
        public static void QueueIfHD(Renderer r)
        {
            if (!On || r == null || !ActiveHere()) return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) QueueMaterial(mats[i]);
        }

        /// <summary>Tools: every moved material back to its shader's queue (an
        /// edit-mode render must not leave a material asset at 2501).</summary>
        public static void RestoreQueues()
        {
            foreach (var m in moved) if (m != null) m.renderQueue = -1;
            moved.Clear();
            seaScene = int.MinValue;
        }

        /// <summary>Does <paramref name="cam"/> need the water's depth copy
        /// this frame (the HD tiers 1 and 2)?</summary>
        public static bool DepthCopyFor(Camera cam) => ActiveHere() && TierFor(cam) >= 1;

        /// <summary>True only with PSX_WATER_HD=1 (opt-in): otherwise the
        /// water as it was, on every platform.</summary>
        public static readonly bool On = System.Environment.GetEnvironmentVariable("PSX_WATER_HD") == "1";

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
                ? "[WaterLook] HD water ON (PSX_WATER_HD=1)" + (SeaOn ? ", the open sea too (PSX_WATER_HD_SEA=1)" : "; the open sea keeps the old water")
                : "[WaterLook] HD water OFF (PSX_WATER_HD is not 1): keyword disabled, no hook, no depth copy");
            // OFF: nothing per camera at all - the old water, as it was
            if (!On) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void ApplyKeyword()
        {
            if (ActiveHere()) Shader.EnableKeyword(Keyword);
            else Shader.DisableKeyword(Keyword);
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) => Push(cam);

        /// <summary>This camera's tier, 0..2 (see the class summary).</summary>
        public static int TierFor(Camera cam)
        {
            if (cam == null) return 0;
            bool copies = SceneCopies(cam);
            // a tool may force a tier, never one whose textures are not there
            if (ForceTier >= 0 && cam == ForceCamera) return copies ? ForceTier : 0;
            if (!copies) return 0;
            // only the game's own view (and the editor's scene view) pays for
            // the copies: a mirror, a menu or a probe keeps the cheap water
            bool gameView = cam.cameraType == CameraType.Game
                            && cam.TryGetComponent(out PSXCameraOutput output) && output.isActiveAndEnabled;
            if (!gameView) return cam.cameraType == CameraType.SceneView ? 1 : 0;
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
            bool colour = asset.supportsCameraOpaqueTexture;
            if (cam.TryGetComponent(out UniversalAdditionalCameraData data))
            {
                if (data.renderType != CameraRenderType.Base) return false;
                if (data.requiresColorOption != CameraOverrideOption.UsePipelineSettings)
                    colour = data.requiresColorOption == CameraOverrideOption.On;
            }
            return colour;
        }

        /// <summary>The globals for <paramref name="cam"/>'s frame.</summary>
        public static void Push(Camera cam)
        {
            ApplyKeyword();
            if (!ActiveHere()) return;
            int tier = TierFor(cam);
            Shader.SetGlobalVector(TierId, new Vector4(tier >= 1 ? 1f : 0f, tier >= 2 ? 1f : 0f, tier >= 2 ? 1f : 0f, 0f));
            Vector2 wind;
            float rain;
            Weather(out wind, out rain);
            float speed = wind.magnitude;
            Vector2 dir = speed > 1e-3f ? wind / speed : new Vector2(0.8f, 0.6f);
            Shader.SetGlobalVector(WindId, new Vector4(dir.x, dir.y, speed, 0f));
            Shader.SetGlobalFloat(RainId, rain);
        }

        static int memoDay = int.MinValue, memoHour = int.MinValue, memoScene = int.MinValue, memoOverride = int.MinValue;
        static Vector2 memoWind;
        static float memoRain;

        /// <summary>The wind on the water (m/s, world x,z) and the rain on it
        /// (0..1), from the weather now. Memoised per day, hour, scene and the
        /// bench's weather override.</summary>
        public static void Weather(out Vector2 wind, out float rain)
        {
            int day = RaceHandoff.CalendarDay, hour = TimeOfDay.Current, over = RaceHandoff.WeatherOverride;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetHashCode();
            if (day != memoDay || hour != memoHour || scene != memoScene || over != memoOverride)
            {
                memoDay = day; memoHour = hour; memoScene = scene; memoOverride = over;
                var w = Seasons.Now;
                VolumetricClouds.CloudsFor(w, out _, out _, out Vector2 deck);
                memoWind = deck * SurfaceWindShare;
                memoRain = w.precip == PrecipKind.Rain ? Mathf.Clamp01(Mathf.Max(w.precipRate, 0.25f)) : 0f;
            }
            wind = ForceWind ?? memoWind;
            rain = ForceRain >= 0f ? Mathf.Clamp01(ForceRain) : memoRain;
        }

        /// <summary>Tools: forget the memo (a tool changed the weather).</summary>
        public static void Invalidate() { memoDay = int.MinValue; }
    }
}
