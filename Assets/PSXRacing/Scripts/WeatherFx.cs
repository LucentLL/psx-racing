using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Rain and snow: a particle slab that rides above whichever camera is
    /// live, emitting straight down through the frame.
    ///
    /// Built at runtime rather than baked, because the weather is a property
    /// of the DAY and the scene does not know what day it is until it loads.
    /// One instance per scene, made by <see cref="SeasonDress"/> when the
    /// roll says so, destroyed with the scene like everything else.
    ///
    /// Through PSX/Decal — the skid-mark shader — so the drops take the
    /// scene's own fog and quantise to the same grid as everything else; a
    /// rain that does not fade into the fog wall floats in front of it. The
    /// textures are drawn here, in code, at four by sixteen and eight by
    /// eight: a streak and a dot are not worth a file.
    ///
    /// Sound is deliberately absent. There is no rain loop in the pack yet,
    /// and a silent rain beats a wrong one.
    /// </summary>
    public class WeatherFx : MonoBehaviour
    {
        public Weather weather = Weather.Clear;

        static WeatherFx instance;

        /// <summary>Make sure the scene has the effect for <paramref name="w"/>,
        /// and nothing for clear or fog (fog is the fog band, not particles).</summary>
        public static void Ensure(Weather w)
        {
            if (w != Weather.Rain && w != Weather.Snow) return;
            if (instance != null) { instance.weather = w; return; }
            var go = new GameObject("WeatherFx");
            instance = go.AddComponent<WeatherFx>();
            instance.weather = w;
        }

        ParticleSystem ps;
        Material mat;
        Texture2D tex;

        void Start() => Build();

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (mat != null) Destroy(mat);
            if (tex != null) Destroy(tex);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Above and a little ahead of the lens: the slab is wide enough
            // that the camera's own yaw never finds its edge, and the lead
            // keeps the frame full at speed instead of the drops falling
            // behind the car.
            var t = cam.transform;
            transform.position = t.position + Vector3.up * (weather == Weather.Snow ? 10f : 13f)
                                 + Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized * 6f;
        }

        void Build()
        {
            bool snow = weather == Weather.Snow;
            // Emit straight DOWN: a box shape emits along its own forward.
            transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = snow ? 900 : 1400;
            main.startLifetime = snow ? 7f : 1.3f;
            main.startSpeed = snow ? 1.6f : 15f;
            main.startSize = snow ? 0.10f : 0.045f;
            main.startColor = snow ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.72f, 0.78f, 0.90f, 0.55f);
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = snow ? 220f : 700f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(48f, 48f, 2f);

            // A little wind. Snow drifts, rain leans.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(snow ? 0.4f : 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            if (snow)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.7f;
                noise.frequency = 0.25f;
                noise.scrollSpeed = 0.3f;
            }

            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            if (!snow) { r.velocityScale = 0.035f; r.lengthScale = 0f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -10f;

            tex = snow ? Dot() : Streak();
            var shader = Shader.Find("PSX/Decal");
            mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "WeatherFx (runtime)" };
            mat.mainTexture = tex;
            if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", Color.white);
            r.sharedMaterial = mat;

            ps.Play();
        }

        static Texture2D Dot()
        {
            const int n = 8;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (n * 0.5f);
                    byte a = (byte)(255f * Mathf.Clamp01(1.15f - d * 1.15f));
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D Streak()
        {
            const int w = 4, h = 16;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float ax = 1f - Mathf.Abs(x + 0.5f - w * 0.5f) / (w * 0.5f);
                    float ay = Mathf.Sin((y + 0.5f) / h * Mathf.PI);
                    byte a = (byte)(255f * Mathf.Clamp01(ax * ay * 1.3f));
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
