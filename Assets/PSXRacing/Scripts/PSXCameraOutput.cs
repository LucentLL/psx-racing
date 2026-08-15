using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Renders the game camera into a 320x240 point-filtered RenderTexture and
    /// shows it on a full-screen RawImage (4:3, letterboxed) through the
    /// PSX/Blit dither shader. The HUD canvas targets the same camera, so UI is
    /// rasterized at 320x240 too.
    /// </summary>
    public class PSXCameraOutput : MonoBehaviour
    {
        public int width = 320;
        public int height = 240;
        public RawImage display;   // assigned by the scene builder

        RenderTexture rt;
        Camera cam;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                filterMode = FilterMode.Point,
                antiAliasing = 1,
                name = "PSXFramebuffer"
            };
            rt.Create();
            cam.targetTexture = rt;
            cam.allowMSAA = false;
            if (display != null) display.texture = rt;
        }

        void OnDisable()
        {
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        }
    }
}
