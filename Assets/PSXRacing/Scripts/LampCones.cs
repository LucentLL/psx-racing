using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE LAMP CONES (2026-10-13): under every street lamp near the eye at
    /// night, a soft additive cone of light in the air - Touge Attack's
    /// signature, the lamp seen through a thin haze, and what makes a lone
    /// lamp on a black mountain road read as a lamp.
    ///
    /// A cheap fake, not volumetrics. ONE renderer, ONE material, ONE draw
    /// call however many cones: a static mesh of <see cref="StreetLights.ConeMax"/>
    /// unit truncated cones (16 sides, both caps; uv.x = the cone's slot),
    /// placed and sized in the vertex shader from two uniform arrays that
    /// StreetLights fills for the <see cref="Cap"/> nearest lit street lamps
    /// (StreetLights.Cones.cs). An empty slot collapses to a point and draws
    /// nothing. PSX/LampCone draws the BACK faces (so the eye inside a cone
    /// still sees it) and works the light out per pixel from the ray: where it
    /// enters and leaves the cone between the head and the ground, and the
    /// haze along that chord - bright on the axis and toward the head, zero at
    /// the rim (the view-angle fade: a grazing ray has no chord), soft over
    /// the last metres above the ground and the first under the head, and
    /// thinned right at the eye. No depth texture (the phone renderer has
    /// none): the ground fade is against the lamp's own ground height.
    ///
    /// Additive into colour only - alpha is untouched, so the cones stay OUT
    /// of the emitter mask (the halation and the sun glare key on it; a cone
    /// is scattered light, not a source). Only at NIGHT (_PSXNight, in the
    /// shader), at full strength where the eye is in the dark (NightDark) and
    /// <see cref="LitKeep"/> of it on a lit street, stronger in fog, rain and
    /// snow (<see cref="HazeFor"/>). The renderer is switched off whenever no
    /// cone is lit, so a day costs nothing.
    /// </summary>
    public static class LampCones
    {
        /// <summary>PSX_CONES=0 (editor A/B): no cones.</summary>
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_CONES") != "0";
        /// <summary>The cone's light per unit of the lamp's linear colour (at a
        /// full-gain lamp, clear air, an unlit eye). PSX_CONE_GAIN overrides.</summary>
        public static float Strength = EnvFloat("PSX_CONE_GAIN", 0.045f);
        /// <summary>The share of it a lit street shows: the cones are there, but
        /// a lit street's own light eats their contrast.</summary>
        public static float LitKeep = 0.4f;
        /// <summary>Half-angle of the cone (degrees from straight down).</summary>
        public static float HalfAngleDeg = 38f;
        /// <summary>Radius of the cone where it leaves the head (m).</summary>
        public static float TopRadiusM = 0.35f;
        /// <summary>Cones drawn at once: fewer on a phone and at RETRO.</summary>
        public static int Cap => Application.isMobilePlatform ? 12
                               : PSXQuality.Current == PSXPixels.Retro ? 16 : StreetLights.ConeMax;
        /// <summary>Cones submitted at the last fill (logs, tests).</summary>
        public static int Drawn { get; private set; }

        /// <summary>One line on the cones' state (the shot tools' log).</summary>
        public static string Describe()
        {
            var a = BufA[0]; var b = BufB[0];
            return $"cones {Drawn} go {(go != null)} on {(mr != null && mr.enabled)} layer {(go != null ? go.layer : -1)} " +
                   $"shader {(mat != null ? mat.shader.name + (mat.shader.isSupported ? "" : " UNSUPPORTED") : "-")} " +
                   $"A0 ({a.x:0.0},{a.y:0.0},{a.z:0.0} h {a.w:0.0}) B0 ({b.x:0.0000},{b.y:0.0000},{b.z:0.0000} r {b.w:0.00})";
        }

        /// <summary>How much more a cone shows in thick air.</summary>
        public static float HazeFor(Weather w)
        {
            switch (w)
            {
                case Weather.Fog:  return 2.2f;
                case Weather.Rain: return 1.7f;
                case Weather.Snow: return 1.4f;
                default: return 1f;
            }
        }

        const int Sides = 16;
        internal static readonly Vector4[] BufA = new Vector4[StreetLights.ConeMax];
        internal static readonly Vector4[] BufB = new Vector4[StreetLights.ConeMax];
        static readonly int AId = Shader.PropertyToID("_PSXConeA");
        static readonly int BId = Shader.PropertyToID("_PSXConeB");
        static readonly int KId = Shader.PropertyToID("_PSXConeK");
        static GameObject go;
        static MeshRenderer mr;
        static Mesh mesh;
        static Material mat;
        static bool warned;

        /// <summary>The fill is done (slots 0..n-1 of BufA/BufB): send it and
        /// switch the renderer. Called by StreetLights for the measured eye.</summary>
        internal static void Submit(int n, Vector3 eye)
        {
            Drawn = Enabled ? n : 0;
            // The renderer is switched only where a switch is safe: in PLAY
            // the game's view decides once a frame (a cone that comes on is a
            // frame late at worst, and fades in anyway); in EDIT mode every
            // camera measures for its own eye, and a camera with no lamps near
            // it (a shadow or reflection view) must not switch the cones off
            // under the next one - there they go off only with the lamps.
            bool want = Drawn > 0 || (!Application.isPlaying && Enabled && StreetLights.StreetOn && mr != null && mr.enabled);
            if (!want)
            {
                if (mr != null) mr.enabled = false;
                return;
            }
            if (!Ensure()) { Drawn = 0; return; }
            for (int i = Drawn; i < BufA.Length; i++) BufA[i] = BufB[i] = Vector4.zero;
            mat.SetVectorArray(AId, BufA);
            mat.SetVectorArray(BId, BufB);
            mat.SetFloat(KId, Mathf.Tan(HalfAngleDeg * Mathf.Deg2Rad));
            if (!mr.enabled) mr.enabled = true;
        }

        static bool Ensure()
        {
            if (go != null && mr != null && mat != null && mesh != null) return true;
            if (mat == null)
            {
                var sh = Shader.Find("PSX/LampCone");
                if (sh == null)
                {
                    if (!warned) { warned = true; Debug.LogWarning("LampCones: PSX/LampCone shader missing - no lamp cones."); }
                    return false;
                }
                mat = new Material(sh) { name = "LampCones", hideFlags = HideFlags.HideAndDontSave };
            }
            if (mesh == null) mesh = BuildMesh(StreetLights.ConeMax);
            if (go == null)
            {
                // DontSave, not HideAndDontSave: the halos' proven path (an
                // edit-mode tool's render request drew no hidden renderer).
                go = new GameObject("PSX Lamp Cones") { hideFlags = HideFlags.DontSave };
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                mr.allowOcclusionWhenDynamic = false;
            }
            else if (mr == null) mr = go.GetComponent<MeshRenderer>();
            return mr != null;
        }

        /// <summary>
        /// <paramref name="count"/> unit truncated cones: the top ring at y 0,
        /// the bottom ring at y -1, both of radius 1 (circumscribed, so the
        /// 16-gon covers the true circle the shader shades), plus a centre
        /// vertex on each cap; uv.x = the slot. Every triangle is wound with
        /// its front face OUTWARD (checked as it is written), so the shader's
        /// Cull Front draws the far side.
        /// </summary>
        public static Mesh BuildMesh(int count)
        {
            int perV = Sides * 2 + 2, perT = Sides * 4;
            var verts = new Vector3[count * perV];
            var uvs = new Vector2[count * perV];
            var tris = new int[count * perT * 3];
            float r = 1f / Mathf.Cos(Mathf.PI / Sides);
            int t = 0;
            for (int c = 0; c < count; c++)
            {
                int v0 = c * perV;
                for (int s = 0; s < Sides; s++)
                {
                    float a = s * 2f * Mathf.PI / Sides;
                    var xz = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    verts[v0 + s] = xz;                                   // top ring
                    verts[v0 + Sides + s] = xz + Vector3.down;            // bottom ring
                }
                int top = v0 + Sides * 2, bot = top + 1;
                verts[top] = Vector3.zero;
                verts[bot] = Vector3.down;
                for (int i = 0; i < perV; i++) uvs[v0 + i] = new Vector2(c, 0f);
                for (int s = 0; s < Sides; s++)
                {
                    int s1 = (s + 1) % Sides;
                    int a0 = v0 + s, a1 = v0 + s1, b0 = v0 + Sides + s, b1 = v0 + Sides + s1;
                    Vector3 mid = (verts[a0] + verts[a1]) * 0.5f;
                    var outward = new Vector3(mid.x, 0f, mid.z);
                    Tri(tris, ref t, verts, a0, b0, b1, outward);
                    Tri(tris, ref t, verts, a0, b1, a1, outward);
                    Tri(tris, ref t, verts, top, a0, a1, Vector3.up);
                    Tri(tris, ref t, verts, bot, b1, b0, Vector3.down);
                }
            }
            var m = new Mesh { name = "PSXLampCones", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            // Last (triangles recompute them): the shader places every vertex
            // in the world itself, so the object never moves and its bounds
            // are the world - never culled while it is on (it is on only at
            // night with lamps near, and an empty slot draws nothing).
            m.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
            return m;
        }

        /// <summary>Write one triangle with its front face toward
        /// <paramref name="outward"/> (Unity's front face is clockwise seen
        /// from the front, i.e. cross(b - a, c - a) points at the viewer).</summary>
        static void Tri(int[] tris, ref int t, Vector3[] v, int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0f) { int x = b; b = c; c = x; }
            tris[t++] = a; tris[t++] = b; tris[t++] = c;
        }

        static float EnvFloat(string key, float fallback)
        {
            string s = System.Environment.GetEnvironmentVariable(key);
            return !string.IsNullOrEmpty(s) && float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float f) && f >= 0f ? f : fallback;
        }
    }
}
