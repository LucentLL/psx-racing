using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// WHICH CAMERA'S FRAME CARRIES THE EMITTER MASK (the colour pass, C4,
    /// 2026-09-29; Shaders/PSXTone.cginc).
    ///
    /// The halation in PSX/Blit and the dirt in PSX/Lens glow by the
    /// framebuffer's ALPHA now: how much of each pixel is a light source. So
    /// the opaque PSX shaders write 0 there for a lit surface - and that is
    /// only right on the frame PSX/Blit shows. Every other camera in the game
    /// draws into a render texture that a RawImage shows BLENDED BY ITS
    /// ALPHA: the rear-view mirror (CockpitView), the pizza cam (PizzaCam),
    /// the car viewer in the menus (CarViewer). An alpha of 0 there is a
    /// transparent wall. So the shaders write the mask only while
    /// _PSXEmitWrite is 1, and the coverage they always wrote otherwise.
    ///
    /// _PSXEmitWrite is set here, per camera, from the render pipeline's own
    /// callback: 1 for a camera that carries a <see cref="PSXCameraOutput"/>
    /// (the game's view - and the screenshot tools' render requests, which
    /// render that same camera into their own texture), 0 for any other. A
    /// camera that draws only the UI layer changes nothing: the HUD writes no
    /// alpha (HudOnTop), and the stacked HUD camera draws into the frame its
    /// base camera left.
    /// </summary>
    public static class EmitterMask
    {
        static readonly int WriteId = Shader.PropertyToID("_PSXEmitWrite");
        static bool hooked;

        /// <summary>Install the per-camera switch, once (PSXGlobals.OnEnable,
        /// edit mode included, like the lamp table's hook). A domain reload
        /// drops the subscription with this flag.</summary>
        public static void EnsureHook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            if ((cam.cullingMask & ~(1 << 5)) == 0) return;
            Shader.SetGlobalFloat(WriteId, Writes(cam) ? 1f : 0f);
        }

        /// <summary>Whether this camera's frame is the one PSX/Blit shows
        /// (and so carries the emitter mask in its alpha).</summary>
        public static bool Writes(Camera cam) =>
            cam != null && cam.cameraType == CameraType.Game && cam.TryGetComponent<PSXCameraOutput>(out _);
    }
}
