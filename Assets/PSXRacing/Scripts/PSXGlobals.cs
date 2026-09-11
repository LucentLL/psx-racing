using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Drives the global shader uniforms for the PSX/Lit shader:
    /// sun direction/color, ambient, fog range, and the vertex-snap toggle.
    /// </summary>
    [ExecuteAlways]
    public class PSXGlobals : MonoBehaviour
    {
        public Light sun;
        public Color ambient = new Color(0.42f, 0.40f, 0.50f);
        public Color fogColor = new Color(0.87f, 0.56f, 0.44f);
        public float fogNear = 60f;
        public float fogFar = 240f;
        /// <summary>
        /// Per-SCENE multiplier on the hour presets' fog band, baked by the
        /// scene builder. The circuits live happily inside 360 m; a mountain
        /// stage is about the ridge two valleys over, so its scene bakes ~3x
        /// and TimeOfDay.Apply multiplies the preset through this. The preset
        /// table itself stays one table — a second table of seven hours per
        /// venue would drift apart the first time one of them was tuned.
        /// </summary>
        public float fogScale = 1f;
        /// <summary>
        /// THE AMBIENT FROM ABOVE. PSX/Lit used to light every unlit face the
        /// same colour whichever way it pointed, so a roof and a floor sat
        /// under the same grey, and the sky above it, once it became a real
        /// photograph, had a colour the world beneath it never picked up.
        /// Faces that look UP now take this instead of <see cref="ambient"/>,
        /// blended by the normal's upness, so a bonnet under a noon sky is a
        /// touch blue and one under a sunset a touch orange. TimeOfDay writes
        /// it from the hour's own sky stops (see TimeOfDay.SkyAmbientFor); a
        /// scene that never applies an hour keeps it equal to the ambient,
        /// which is exactly the old picture. Left alpha-zero here so Apply
        /// can tell "never set" from "set to black".
        /// </summary>
        public Color skyAmbient = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// PS1 vertex jitter: quantise every vertex to the framebuffer grid.
        ///
        /// DEFAULTS TO OFF, on the owner's instruction: "many textures are
        /// interfering when moving. this should never happen in buildings or
        /// when driving." The snap moves a vertex in SCREEN space and leaves
        /// its depth alone, so the depth rasterised across a polygon no longer
        /// describes where that polygon actually is — and two surfaces sitting
        /// on each other (a table top and its trim, a road and its painted
        /// line, a wall and its poster) disagree about which is in front, per
        /// pixel, differently every frame. That is the flicker. The error is
        /// ANGULAR, so it grows with distance without bound, and it is worst on
        /// exactly the surfaces you look at most: floors and roads at a grazing
        /// angle, where half a pixel sideways is a long way forward.
        ///
        /// The same call the affine warping got, for the same reason and from
        /// the same person — see the _Affine note in PSXLit.shader. Both are
        /// only ever right on small triangles, and almost nothing in this game
        /// is made of small triangles.
        ///
        /// Worth writing down: EVERY preview tool in this project
        /// (CityPreview, TownPreview, PizzeriaPreview, TireFxPreview,
        /// HoistPreview) has always set _PSXSnap to 0 before shooting. So every
        /// screenshot this look was signed off from was rendered WITHOUT the
        /// snap, while the game shipped WITH it — the tools and the game were
        /// never showing the same picture. They agree now.
        ///
        /// The flag stays so the jitter can be turned back on deliberately (one
        /// bool and a scene rebuild), but nothing sets it today.
        /// </summary>
        public bool vertexSnap;

        void OnEnable() => Apply();
        void Update() => Apply();

        public void Apply()
        {
            Vector3 dir = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.8f, 0.2f).normalized;
            Color lightCol = sun != null ? sun.color * sun.intensity : Color.white;
            Shader.SetGlobalVector("_PSXLightDir", dir);
            Shader.SetGlobalColor("_PSXLightColor", lightCol);
            Shader.SetGlobalColor("_PSXAmbient", ambient);
            Shader.SetGlobalColor("_PSXSkyAmbient", skyAmbient.a > 0f ? skyAmbient : ambient);
            Shader.SetGlobalColor("_PSXFogColor", fogColor);
            Shader.SetGlobalFloat("_PSXFogNear", fogNear);
            Shader.SetGlobalFloat("_PSXFogFar", fogFar);
            Shader.SetGlobalFloat("_PSXSnap", vertexSnap ? 1f : 0f);
        }
    }
}
