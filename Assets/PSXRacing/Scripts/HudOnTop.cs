using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Makes the race HUD ignore the depth buffer.
    ///
    /// The HUD canvas is ScreenSpaceCamera on the PSX camera, which is the whole
    /// reason it comes out dithered and 240 lines tall like the rest of the
    /// picture rather than sitting on top of it as crisp modern UI. The cost of
    /// that is depth: a ScreenSpaceCamera canvas is drawn on a plane one metre
    /// in front of the lens and DEPTH-TESTED against the world, so anything
    /// closer than a metre covers it.
    ///
    /// In every chase view nothing is that close and it never mattered. In the
    /// bonnet camera the car's own bonnet is 20 cm away and fills the bottom of
    /// the frame — which is exactly where the instrument cluster is — so the
    /// dials disappeared into the paintwork the moment they were put there.
    ///
    /// Moving the canvas plane cannot fix it: it has to be further out than the
    /// chase views' 0.25 m near plane and closer than the bonnet at 0.2 m, and
    /// those do not overlap. Turning the depth test off does, and it is what UI
    /// wants anyway — the HUD is not IN the world, it is over it.
    /// </summary>
    public static class HudOnTop
    {
        static Material shared;

        /// <summary>
        /// One shared material for the whole HUD, built from the stock UI
        /// shader with ZTest forced to Always.
        ///
        /// DontSave: this is created at runtime and again by the screenshot
        /// tool in edit mode, and a material left behind in a scene by an
        /// editor tool is the kind of thing that gets committed and then
        /// wondered about.
        /// </summary>
        public static Material Material
        {
            get
            {
                if (shared != null) return shared;
                var shader = Shader.Find("UI/Default");
                if (shader == null) return null;
                shared = new Material(shader)
                {
                    name = "HUD (ZTest Always)",
                    hideFlags = HideFlags.DontSave,
                };
                shared.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                return shared;
            }
        }

        /// <summary>Put every graphic under <paramref name="root"/> on it.
        /// Called again after the cluster rebuilds, because the dials are
        /// created at runtime and are not there the first time round.</summary>
        public static void Apply(GameObject root)
        {
            var mat = Material;
            if (root == null || mat == null) return;
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                if (g.material != mat) g.material = mat;
        }
    }
}
