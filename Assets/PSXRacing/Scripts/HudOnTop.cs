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
                // RGB ONLY (the colour pass, C4). The HUD is drawn INTO the
                // PSX framebuffer, whose alpha is the emitter mask the
                // halation and the lens dirt glow by (Shaders/PSXTone.cginc).
                // A glyph is not a light source: writing its coverage there
                // would halo every white number on the screen.
                shared.SetInt("_ColorMask", (int)(ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue));
                return shared;
            }
        }

        /// <summary>
        /// Put every graphic under <paramref name="root"/> on it. Called again
        /// after the cluster rebuilds, because the dials are created at runtime
        /// and are not there the first time round.
        ///
        /// ONLY GRAPHICS ON A STOCK UI MATERIAL. A graphic that brought a
        /// shader of its own brought its own depth rule with it, and may not
        /// be merely styled by that shader but DRAWN by it. The one that
        /// taught this was the speed-streak overlay (gone since 2026-09-19,
        /// replaced by the SpeedBlur pass): its texture was a polar sheet,
        /// and on the plain UI material its dashes came out as long vertical
        /// white bars across the whole frame. It was a child of the HUD
        /// canvas, which RaceHUD.Awake passes to this, so swapping it was
        /// always one Awake order away from the screen — and it reached a
        /// phone twice. The rule outlives it.
        /// </summary>
        public static void Apply(GameObject root)
        {
            var mat = Material;
            if (root == null || mat == null) return;
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                var current = g.material;
                if (current == mat || !IsStockUi(current)) continue;
                g.material = mat;
            }
            OutlineText(root);
        }

        /// <summary>
        /// THE HUD'S TEXT OUTLINED (the colour pass, C9, 2026-09-29). Every
        /// HUD label had a one-sided drop Shadow (1, -1): it darkens only the
        /// lower-right edge of each stroke, so over a bright noon road or a
        /// pale sky the white letters had nothing under three of their sides -
        /// the owner's street name measured 1.2:1 against its surround at
        /// noon. <see cref="HudTextEdge"/> (black, one pixel on all eight
        /// sides) puts a dark edge round every stroke whatever is behind it:
        /// white 0.935 on the grade's floor is 14:1. The text stays low-res,
        /// dithered and graded - the PS1 identity this class's header defends.
        ///
        /// Here, where every HUD graphic already passes: the labels baked into
        /// the race scenes carry the old Shadow (the builder's MakeText), and a
        /// rebake is not needed to fix them. A graphic with any other edge (an
        /// Outline, a HudTextEdge) or none at all is left alone.
        /// </summary>
        public static void OutlineText(GameObject root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                var sh = t.GetComponent<Shadow>();
                // Exactly a Shadow: an Outline IS a Shadow (it derives from it).
                if (sh == null || sh.GetType() != typeof(Shadow)) continue;
                var go = t.gameObject;
                if (Application.isPlaying) Object.Destroy(sh); else Object.DestroyImmediate(sh);
                // Destroy is deferred to the end of the frame in play: a second
                // pass in the same frame still finds the Shadow - and must not
                // add a second edge.
                if (go.GetComponent<HudTextEdge>() == null) AddOutline(go);
            }
        }

        /// <summary>The HUD text's edge: solid black, <paramref name="radius"/>
        /// pixels deep on every side (<see cref="HudTextEdge"/>).</summary>
        public static HudTextEdge AddOutline(GameObject go, float alpha = 1f, int radius = 2)
        {
            var o = go.AddComponent<HudTextEdge>();
            o.effectColor = new Color(0f, 0f, 0f, alpha);
            o.effectDistance = new Vector2(1f, 1f);
            o.radius = radius;
            return o;
        }

        /// <summary>A material this may take over: none, or one of the UI
        /// shaders Unity ships (UI/Default and its variants).</summary>
        public static bool IsStockUi(Material m) =>
            m == null || m.shader == null ||
            m.shader.name.StartsWith("UI/", System.StringComparison.Ordinal);
    }
}
