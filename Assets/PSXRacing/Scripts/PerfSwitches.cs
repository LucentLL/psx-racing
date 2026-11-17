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
    /// Nothing here changes a pixel of the driver's view with the switches'
    /// features on as they ship (the HD water still gets its copies).
    /// </summary>
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
        public static bool LeanOutput = Env("PSX_LEAN_OUTPUT", false);
        public static bool AODriverOnly = Env("PSX_AO_DRIVER_ONLY", false);
        public static bool CopiesOnDemand = Env("PSX_COPIES_ON_DEMAND", false);
        /// <summary>Measuring only (the perf tour's probe): no ambient occlusion at all.</summary>
        public static bool AOOff;

        static bool hooked;
        static List<ScriptableRendererFeature> ao;
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
                            if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") ao.Add(f);
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
                bool rule = AODriverOnly || AOOff || (LeanOutput && empty);
                if (rule)
                {
                    bool want = !AOOff && !(LeanOutput && empty) && (!AODriverOnly || StreetLights.IsDriver(cam));
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
