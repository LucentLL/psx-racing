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
        public bool vertexSnap = true;

        void OnEnable() => Apply();
        void Update() => Apply();

        void Apply()
        {
            Vector3 dir = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.8f, 0.2f).normalized;
            Color lightCol = sun != null ? sun.color * sun.intensity : Color.white;
            Shader.SetGlobalVector("_PSXLightDir", dir);
            Shader.SetGlobalColor("_PSXLightColor", lightCol);
            Shader.SetGlobalColor("_PSXAmbient", ambient);
            Shader.SetGlobalColor("_PSXFogColor", fogColor);
            Shader.SetGlobalFloat("_PSXFogNear", fogNear);
            Shader.SetGlobalFloat("_PSXFogFar", fogFar);
            Shader.SetGlobalFloat("_PSXSnap", vertexSnap ? 1f : 0f);
        }
    }
}
