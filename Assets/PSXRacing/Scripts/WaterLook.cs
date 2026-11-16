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
    /// THE SWITCH. <c>PSX_WATER_HD=0</c> in the environment leaves the global
    /// keyword off, and PSX/Water draws exactly the code it drew before this
    /// round. Anything else: the keyword is on.
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

        /// <summary>False with PSX_WATER_HD=0: the water as it was.</summary>
        public static readonly bool On = System.Environment.GetEnvironmentVariable("PSX_WATER_HD") != "0";

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
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void ApplyKeyword()
        {
            if (On) Shader.EnableKeyword(Keyword);
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
            if (Application.isMobilePlatform || !ReflectionPrefs.On) return 1;
            if (cam.cameraType != CameraType.Game) return 1;
            if (!cam.TryGetComponent(out PSXCameraOutput output) || !output.isActiveAndEnabled) return 1;
            return 2;
        }

        /// <summary>Does this camera get URP's copies of the frame (after the
        /// sky) and its depth (after the opaques)?</summary>
        public static bool SceneCopies(Camera cam)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return false;
            bool depth = asset.supportsCameraDepthTexture, colour = asset.supportsCameraOpaqueTexture;
            if (cam.TryGetComponent(out UniversalAdditionalCameraData data))
            {
                if (data.renderType != CameraRenderType.Base) return false;
                if (data.requiresDepthOption != CameraOverrideOption.UsePipelineSettings)
                    depth = data.requiresDepthOption == CameraOverrideOption.On;
                if (data.requiresColorOption != CameraOverrideOption.UsePipelineSettings)
                    colour = data.requiresColorOption == CameraOverrideOption.On;
            }
            return depth && colour;
        }

        /// <summary>The globals for <paramref name="cam"/>'s frame.</summary>
        public static void Push(Camera cam)
        {
            ApplyKeyword();
            if (!On) return;
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
