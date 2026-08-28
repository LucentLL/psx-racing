using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Renders the game camera into a low-resolution point-filtered
    /// RenderTexture and shows it on a full-screen RawImage through the PSX/Blit
    /// dither shader. The HUD canvas targets the same camera, so UI is rasterized
    /// at the same low resolution.
    ///
    /// The framebuffer is 240 lines tall and as WIDE AS THE SCREEN NEEDS.
    /// It used to be a fixed 320x240 shown 4:3 letterboxed, which on a 2.24:1
    /// phone left roughly a third of the display as black bars down both sides —
    /// the game was playing in a box in the middle of the screen. Locking the
    /// VERTICAL resolution is what preserves the era: pixel size, dither scale
    /// and HUD type all key off the line count, and a PS1 was itself a
    /// fixed-lines/variable-width machine (256, 320, 384 and 512 pixel modes all
    /// shipped). So a wider screen gets more pixels across and a wider view, not
    /// a stretched picture and not bars.
    /// </summary>
    public class PSXCameraOutput : MonoBehaviour
    {
        /// <summary>Vertical resolution. This is the real setting — width follows
        /// from the display. Driven by <see cref="PSXQuality"/> at runtime; the
        /// field is what a scene opened in the editor uses.</summary>
        public int height = 240;
        /// <summary>Fallback width, used only when the screen size is not yet
        /// known (the very first frame of a WebGL boot can report 0).</summary>
        public int width = 320;
        /// <summary>Framebuffer width is clamped to a band AROUND THE LINE
        /// COUNT, not to fixed pixel numbers. The floor keeps a tall/narrow
        /// window from rendering a sliver; the ceiling stops an ultrawide from
        /// quietly asking for a framebuffer several times the intended fill
        /// cost. Both used to be literals sized for 240 lines, so raising the
        /// resolution would have letterboxed a phone rather than sharpening
        /// it — the ceiling was 4:1 at 240 and 2:1 at 480.</summary>
        public float minAspect = 256f / 240f;
        public float maxAspect = 4f;

        public RawImage display;   // assigned by the scene builder

        RenderTexture rt;
        Camera cam;
        int builtWidth, builtHeight;
        int lastScreenW, lastScreenH;
        int builtQuality = -1;
        Material blit;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            // The blit material is a saved asset shared by every circuit, and
            // the quality setting writes to it. Instance it at runtime so a
            // player fiddling with the setting does not dirty the project.
            if (Application.isPlaying && display != null && display.material != null)
            {
                blit = new Material(display.material) { hideFlags = HideFlags.DontSave };
                display.material = blit;
            }
            else if (display != null) blit = display.material;
            Rebuild();
        }

        void OnDestroy()
        {
            if (blit != null && blit.hideFlags == HideFlags.DontSave) Destroy(blit);
        }

        void Update()
        {
            // Orientation changes, browser window resizes and the mobile URL bar
            // sliding away all change the display aspect mid-session. Rebuild
            // only when the screen actually changed — allocating a RenderTexture
            // every frame would be catastrophic. The quality setting is folded
            // into the same test so a change made in the pause menu lands on the
            // next frame rather than the next race.
            if (Screen.width != lastScreenW || Screen.height != lastScreenH
                || PSXQuality.Changed != builtQuality) Rebuild();
        }

        void Rebuild()
        {
            lastScreenW = Screen.width;
            lastScreenH = Screen.height;
            builtQuality = PSXQuality.Changed;
            if (Application.isPlaying) height = PSXQuality.Height;
            ApplyFilter();

            int w = TargetWidth();
            if (rt != null && w == builtWidth && height == builtHeight) return;

            Release();
            builtWidth = w;
            builtHeight = Mathf.Max(1, height);
            rt = new RenderTexture(builtWidth, builtHeight, 24, RenderTextureFormat.Default)
            {
                filterMode = FilterMode.Point,
                antiAliasing = 1,
                name = "PSXFramebuffer"
            };
            rt.Create();
            if (cam != null)
            {
                cam.targetTexture = rt;
                cam.allowMSAA = false;
            }
            if (display != null)
            {
                display.texture = rt;
                // Keep the fitter honest about the buffer it is now showing.
                // Integer widths mean this is never EXACTLY the screen aspect,
                // so a fitter set to the old 4:3 would reintroduce the bars it
                // was the whole point of removing.
                var fitter = display.GetComponent<AspectRatioFitter>();
                if (fitter != null) fitter.aspectRatio = builtWidth / (float)builtHeight;
            }
        }

        /// <summary>Colour depth and dither strength, from the same setting the
        /// line count comes from. Set every rebuild rather than once, because
        /// the material is shared and another scene may have left it
        /// somewhere else.</summary>
        void ApplyFilter()
        {
            if (blit == null) return;
            if (blit.HasProperty("_ColorDepth")) blit.SetFloat("_ColorDepth", PSXQuality.ColorDepth);
            if (blit.HasProperty("_DitherStrength")) blit.SetFloat("_DitherStrength", PSXQuality.Dither);
        }

        int TargetWidth()
        {
            int h = Mathf.Max(1, height);
            if (lastScreenW <= 0 || lastScreenH <= 0) return Mathf.Max(1, width);
            float aspect = lastScreenW / (float)lastScreenH;
            int w = Mathf.RoundToInt(h * aspect);
            // Even widths only: the dither pattern is a 4x4 tile and an odd
            // buffer width walks it one pixel per line, which reads as a faint
            // diagonal crawl over flat surfaces.
            if ((w & 1) == 1) w++;
            return Mathf.Clamp(w, Mathf.RoundToInt(h * minAspect), Mathf.RoundToInt(h * maxAspect));
        }

        void OnDisable()
        {
            if (cam != null) cam.targetTexture = null;
            Release();
        }

        void Release()
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }
    }
}
