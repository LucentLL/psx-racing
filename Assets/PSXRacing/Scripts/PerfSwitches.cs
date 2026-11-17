using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// THE PERF ROUND (2026-11-16; owner: "we need to start focusing HEAVILY
    /// on performance" - 40-49 fps on the mountain stages at 3832x2054 on an
    /// RTX 3070, the target 60 minimum and 144 the goal). Work the frame was
    /// paying for and nobody sees, each behind its own switch
    /// (<c>PSX_&lt;NAME&gt;=0</c> = the frame as PC48 drew it), each
    /// switchable inside one run by the perf tour's probe
    /// (<c>set:PerfSwitches.X:1</c>).
    ///
    ///   * LEAN OUTPUT (PSX_LEAN_OUTPUT). The scene's OutputCamera draws
    ///     nothing (culling mask 0): it is there to clear the back buffer
    ///     behind the display canvas. But it is a plain camera on the PC
    ///     renderer, so at the display's resolution it also copied its (empty)
    ///     depth, copied its (black) colour for the opaque texture, ran the
    ///     ambient occlusion and its blurs over nothing, and blitted the
    ///     result to the screen. With this on it asks for none of that, and
    ///     is not an HDR camera (which URP cannot draw straight to the screen).
    ///   * AO IN THE DRIVER'S VIEW ONLY (PSX_AO_DRIVER_ONLY). URP's SSAO is a
    ///     renderer feature: it ran for every camera on the renderer - each
    ///     car-reflection face, each mirror, the HUD overlay and the output
    ///     camera - with a depth copy for each. Only the view the player
    ///     looks through shows it: the ground's darkening under a tree is not
    ///     readable in a 512-pixel reflection face or a door mirror.
    ///   * COPIES ON DEMAND (PSX_COPIES_ON_DEMAND). The PC pipeline asset asks
    ///     every camera for the opaque-colour copy and the depth copy. The
    ///     only reader of the colour copy is the HD water (PSX_WATER_HD,
    ///     opt-in, WaterLook), and the depth copy's only reader is the SSAO,
    ///     which asks for its own. So the asset's two flags follow what is
    ///     on: the colour copy only where the HD water draws, the depth copy
    ///     only for a camera whose passes ask.
    ///
    /// All three are ON by default since their lossless pairs passed (on
    /// screen at 3840x2066, the clock stopped): lean output and copies on
    /// demand 0 pixels different; AO driver only 0 pixels different in the
    /// world seen directly, the mirror images and the car's own paint (it
    /// reflects the probe's views) 0.14 / 0.67 levels of 255 on average.
    /// The HD water still gets its copies.
    /// </summary>
    /// <summary>
    /// AMBIENT SHADE (VISUALS, the PC picture): how the ambient occlusion is
    /// drawn in the driver's view. FULL is the picture as it was (every
    /// pixel, a three-pass blur: measured 2.0 ms of a 24.7 ms frame in the
    /// chase view at 3840x2066, 4.2 ms in the cockpit before the mirrors
    /// stopped running it); HALF draws it at half the lines each way and
    /// scales it up (the same shade, a touch softer at its edges); OFF is no
    /// occlusion at all. The default is FULL: the choice is the player's.
    /// </summary>
    public static class AOQualityPrefs
    {
        const string PrefKey = "psx.aoquality";
        static readonly string[] Names = { "FULL", "HALF", "OFF" };
        static int cached = -1;
        public static int Level
        {
            get { if (cached < 0) cached = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 2); return cached; }
            set { int v = Mathf.Clamp(value, 0, 2); if (v == cached) return; cached = v; PlayerPrefs.SetInt(PrefKey, v); PlayerPrefs.Save(); }
        }
        public static string Label => Names[Level];
        public static void Cycle() => Level = (Level + 1) % 3;
    }

    /// <summary>
    /// GRAPHICS (VISUALS, the PC picture): one row that sets the rows that
    /// trade picture for frame rate, together. QUALITY is the game's own
    /// defaults; a player who has set any of those rows by hand reads CUSTOM
    /// until the row is pressed. What each sets (measured, perf round
    /// 2026-11-16: the report's table):
    ///                 RENDER SCALE  AMBIENT SHADE  FOREST DETAIL  CAR REFLECT  MIRRORS
    ///   QUALITY       100 %         FULL           MEDIUM         AUTO         30
    ///   BALANCED      85 %          HALF           MEDIUM         AUTO         30
    ///   PERFORMANCE   67 %          HALF           LOW            AUTO         20
    /// </summary>
    public static class GraphicsPresetPrefs
    {
        public static readonly string[] Names = { "QUALITY", "BALANCED", "PERFORMANCE" };
        struct Set { public int scale, ao, forest, refl, mirror; }
        static readonly Set[] Sets =
        {
            new Set { scale = 100, ao = 0, forest = 1, refl = 0, mirror = 30 },
            new Set { scale = 85, ao = 1, forest = 1, refl = 0, mirror = 30 },
            new Set { scale = 67, ao = 1, forest = 0, refl = 0, mirror = 20 },
        };

        /// <summary>The preset the rows stand at now; -1 = none (CUSTOM).</summary>
        public static int Current
        {
            get
            {
                for (int i = 0; i < Sets.Length; i++)
                {
                    var s = Sets[i];
                    if (RenderScalePrefs.Percent == s.scale && AOQualityPrefs.Level == s.ao && ForestDetailPrefs.Level == s.forest &&
                        ViewRatePrefs.Rate(ViewRatePrefs.View.CarReflections) == s.refl &&
                        ViewRatePrefs.Rate(ViewRatePrefs.View.RearMirror) == s.mirror &&
                        ViewRatePrefs.Rate(ViewRatePrefs.View.DriverMirror) == s.mirror &&
                        ViewRatePrefs.Rate(ViewRatePrefs.View.PassengerMirror) == s.mirror) return i;
                }
                return -1;
            }
        }

        public static string Label => Current < 0 ? "CUSTOM" : Names[Current];

        public static void Apply(int i)
        {
            var s = Sets[Mathf.Clamp(i, 0, Sets.Length - 1)];
            RenderScalePrefs.Percent = s.scale;
            AOQualityPrefs.Level = s.ao;
            ForestDetailPrefs.Level = s.forest;
            ViewRatePrefs.Set(ViewRatePrefs.View.CarReflections, s.refl);
            ViewRatePrefs.Set(ViewRatePrefs.View.RearMirror, s.mirror);
            ViewRatePrefs.Set(ViewRatePrefs.View.DriverMirror, s.mirror);
            ViewRatePrefs.Set(ViewRatePrefs.View.PassengerMirror, s.mirror);
        }

        /// <summary>The row's press: QUALITY, BALANCED, PERFORMANCE, round again (from CUSTOM: QUALITY).</summary>
        public static void Cycle() { int c = Current; Apply(c < 0 ? 0 : (c + 1) % Sets.Length); }
    }

    public static class PerfSwitches
    {
        static bool Env(string name, bool fallback)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v)) return fallback;
            return v != "0";
        }

        /// <summary>See the class comment; PSX_..=0 = as PC48. Players only: in the editor the
        /// pipeline asset is a file, and a flag changed in play mode could be saved into it.</summary>
        public static bool LeanOutput = Env("PSX_LEAN_OUTPUT", true);
        public static bool AODriverOnly = Env("PSX_AO_DRIVER_ONLY", true);
        public static bool CopiesOnDemand = Env("PSX_COPIES_ON_DEMAND", true);
        /// <summary>Measuring only (the perf tour's probe): no ambient occlusion at all.</summary>
        public static bool AOOff;

        static bool hooked;
        static List<ScriptableRendererFeature> ao;
        static readonly List<object> aoSettings = new List<object>();
        static System.Reflection.FieldInfo aoDownsample;
        static int aoHalfSet = -1;
        static bool aoForced, copiesForced, copiesWasOpaque, copiesWasDepth;
        static readonly List<UniversalAdditionalCameraData> leaned = new List<UniversalAdditionalCameraData>();
        static readonly List<Camera> leanedHdr = new List<Camera>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Debug.Log($"[PerfSwitches] lean output {(LeanOutput ? "ON" : "off")}, AO driver only {(AODriverOnly ? "ON" : "off")}, copies on demand {(CopiesOnDemand ? "ON" : "off")}");
        }

        static void FindAO()
        {
            ao = new List<ScriptableRendererFeature>();
            try
            {
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var list = urp == null ? null : typeof(UniversalRenderPipelineAsset)
                    .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .GetValue(urp) as ScriptableRendererData[];
                if (list == null) return;
                foreach (var data in list)
                    if (data != null)
                        foreach (var f in data.rendererFeatures)
                            if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion")
                            {
                                ao.Add(f);
                                // (its settings object, for AMBIENT SHADE's half size)
                                var st = f.GetType().GetField("m_Settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(f);
                                if (st != null)
                                {
                                    aoSettings.Add(st);
                                    if (aoDownsample == null) aoDownsample = st.GetType().GetField("Downsample", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                }
                            }
            }
            catch (System.Exception e) { Debug.LogWarning("[PerfSwitches] SSAO features not found: " + e.Message); }
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || Application.isEditor || cam.cameraType != CameraType.Game) return;

            // ---- the output camera: its clear and nothing else ----
            bool empty = cam.cullingMask == 0;
            if (LeanOutput && empty)
            {
                if (!cam.TryGetComponent(out UniversalAdditionalCameraData d)) d = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (d.requiresColorOption != CameraOverrideOption.Off || d.requiresDepthOption != CameraOverrideOption.Off)
                {
                    d.requiresColorOption = CameraOverrideOption.Off;
                    d.requiresDepthOption = CameraOverrideOption.Off;
                    d.renderPostProcessing = false;
                    d.renderShadows = false;
                    d.antialiasing = AntialiasingMode.None;
                    // (an HDR camera cannot draw into the back buffer: URP gives it a float texture and a
                    // full-screen blit at the end - for a camera that draws a clear)
                    if (cam.allowHDR) { cam.allowHDR = false; leanedHdr.Add(cam); }
                    if (!leaned.Contains(d)) leaned.Add(d);
                }
            }
            else if (!LeanOutput && leaned.Count > 0)
            {
                foreach (var d in leaned)
                    if (d != null) { d.requiresColorOption = CameraOverrideOption.UsePipelineSettings; d.requiresDepthOption = CameraOverrideOption.UsePipelineSettings; }
                leaned.Clear();
                foreach (var c in leanedHdr) if (c != null) c.allowHDR = true;
                leanedHdr.Clear();
            }

            // ---- the ambient occlusion: which cameras run it ----
            if (ao == null) FindAO();
            if (ao.Count > 0)
            {
                // AMBIENT SHADE: HALF = the occlusion at half size (the feature's own Downsample), OFF = none
                int level = AOQualityPrefs.Level;
                int half = level == 1 ? 1 : 0;
                if (half != aoHalfSet && aoDownsample != null && (aoHalfSet >= 0 || half == 1))
                {
                    foreach (var st in aoSettings) aoDownsample.SetValue(st, half == 1);
                    aoHalfSet = half;
                }
                bool off = AOOff || level == 2;
                bool rule = AODriverOnly || off || (LeanOutput && empty);
                if (rule)
                {
                    bool want = !off && !(LeanOutput && empty) && (!AODriverOnly || StreetLights.IsDriver(cam));
                    for (int i = 0; i < ao.Count; i++) if (ao[i].isActive != want) ao[i].SetActive(want);
                    aoForced = true;
                }
                else if (aoForced)
                {
                    // (back to the feature as the PC profile left it: on)
                    for (int i = 0; i < ao.Count; i++) if (!ao[i].isActive) ao[i].SetActive(PlatformProfile.PCAO);
                    aoForced = false;
                }
            }

            // ---- the pipeline's copies: only where something reads them ----
            if (CopiesOnDemand || copiesForced)
            {
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (urp != null)
                {
                    if (CopiesOnDemand)
                    {
                        if (!copiesForced) { copiesWasOpaque = urp.supportsCameraOpaqueTexture; copiesWasDepth = urp.supportsCameraDepthTexture; copiesForced = true; }
                        bool water = copiesWasOpaque && WaterLook.ActiveHere();
                        if (urp.supportsCameraOpaqueTexture != water) urp.supportsCameraOpaqueTexture = water;
                        bool depth = copiesWasDepth && water;
                        if (urp.supportsCameraDepthTexture != depth) urp.supportsCameraDepthTexture = depth;
                    }
                    else
                    {
                        urp.supportsCameraOpaqueTexture = copiesWasOpaque;
                        urp.supportsCameraDepthTexture = copiesWasDepth;
                        copiesForced = false;
                    }
                }
            }
        }
    }
}
