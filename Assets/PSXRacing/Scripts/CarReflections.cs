using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>REFLECTIONS ON / OFF (VISUALS). On by default everywhere: it
    /// is the look the owner asked for first ("reflections ... are the things
    /// I notice most", 2026-10-12); the row is how a slow phone says no.</summary>
    public static class ReflectionPrefs
    {
        const string PrefKey = "psx.reflections";
        static int cached = -2;
        public static bool On
        {
            get
            {
                if (cached == -2) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                cached = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefKey, cached);
                PlayerPrefs.Save();
            }
        }
        public static void Toggle() => On = !On;
        public static string Label => On ? "ON" : "OFF";
    }

    /// <summary>
    /// THE WORLD IN THE PAINT (2026-10-12, the GT look plan, item 1). Until
    /// now PSX/CarPaint reflected only the hour's sky panorama, with the fog
    /// colour standing in for everything below the horizon: no tree, building,
    /// barrier or car ever slid across a bonnet. What GT3/GT4 do is the
    /// surroundings in the lacquer.
    ///
    /// Six disabled 90-degree cameras' worth of views (one camera, turned)
    /// are rendered by hand from the watched car's roof into six small
    /// textures, a face or two a frame (the whole set refreshes several times
    /// a second), then stitched into one equirect panorama with mips
    /// (Hidden/PSX/EnvPano) laid out like the sky panorama, so the paint reads
    /// it the way it reads the sky: <c>_PSXEnvPano</c>, <c>_PSXEnvOn</c>.
    /// The car being watched is hidden while its own views are drawn.
    ///
    /// The camera is drawn the way the garage turntable is (CarViewer): a
    /// disabled camera and a StandardRequest into a 2D texture, which is the
    /// path proven in the WebGL build - no cubemap render, so nothing hangs
    /// on CopyTexture support or face conventions that differ between the
    /// editor and a browser. Every camera hook in the game already treats a
    /// non-driver camera as a secondary view (the mirror, the pizza cam):
    /// SunShadows and StreetLights keep the driver's maps and table.
    ///
    /// Self-starting (no scene edit, no rebake): one hidden object for the
    /// whole session, looking each frame for the active ChaseCamera's car, so
    /// it follows races, roaming, replays and spectating alike.
    /// </summary>
    public class CarReflections : MonoBehaviour
    {
        public const string ShaderName = "Hidden/PSX/EnvPano";
        static readonly int EnvPanoId = Shader.PropertyToID("_PSXEnvPano");
        static readonly int EnvOnId = Shader.PropertyToID("_PSXEnvOn");
        static readonly int EnvLodBiasId = Shader.PropertyToID("_PSXEnvLodBias");
        static readonly int FaceFId = Shader.PropertyToID("_FaceF");
        static readonly int FaceRId = Shader.PropertyToID("_FaceR");
        static readonly int FaceUId = Shader.PropertyToID("_FaceU");
        static readonly string[] FaceTex = { "_F0", "_F1", "_F2", "_F3", "_F4", "_F5" };

        /// <summary>Roof height the views are taken from, metres above the
        /// car's origin (its ground point).</summary>
        const float EyeUp = 1.15f;
        /// <summary>How far the reflection sees: a bonnet reflects the street,
        /// not the skyline's draw distance.</summary>
        const float FarM = 220f;

        static CarReflections inst;
        Camera probe;
        readonly RenderTexture[] faces = new RenderTexture[6];
        RenderTexture pano;
        Material stitch;
        int next, drawn;
        int faceRes;
        ChaseCamera chase;
        float nextFind;
        Transform hiddenRoot;
        Renderer[] hidden;
        bool[] hiddenWas;
        static readonly Quaternion[] FaceRot =
        {
            Quaternion.LookRotation(Vector3.right, Vector3.up),
            Quaternion.LookRotation(Vector3.left, Vector3.up),
            Quaternion.LookRotation(Vector3.up, Vector3.back),
            Quaternion.LookRotation(Vector3.down, Vector3.forward),
            Quaternion.LookRotation(Vector3.forward, Vector3.up),
            Quaternion.LookRotation(Vector3.back, Vector3.up),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null) return;
            var go = new GameObject("~CarReflections");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            inst = go.AddComponent<CarReflections>();
        }

        void OnDisable() => Shader.SetGlobalFloat(EnvOnId, 0f);

        void OnDestroy()
        {
            Release();
            if (inst == this) inst = null;
        }

        void Release()
        {
            Shader.SetGlobalFloat(EnvOnId, 0f);
            for (int i = 0; i < 6; i++) if (faces[i] != null) { faces[i].Release(); Kill(faces[i]); faces[i] = null; }
            if (pano != null) { pano.Release(); Kill(pano); pano = null; }
            drawn = 0;
        }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        /// <summary>Face size, pixels. Desktop 256 since the GT4 retune
        /// (2026-10-15: a 1024x512 panorama, sharp enough to read as a
        /// reflection on the glass); phones stay cheap. Measured in the editor
        /// (ReflectionShots PSX_REFL_TIME): a six-face refresh is ~30 ms at 64,
        /// 128 or 256 alike - the views are draw-call bound, not fill bound -
        /// and the game draws one or two faces a frame.</summary>
        public const int FaceResDesktop = 256, FaceResMobile = 64;

        /// <summary>Tools only (ReflectionShots' timing): a face size to use
        /// instead of the platform's; 0 = the platform's.</summary>
        public static int FaceResOverride;

        bool Ensure()
        {
            int res = FaceResOverride > 0 ? FaceResOverride : (Application.isMobilePlatform ? FaceResMobile : FaceResDesktop);
            if (faceRes != res) { Release(); faceRes = res; }
            if (stitch == null)
            {
                var sh = Shader.Find(ShaderName);
                if (sh == null) return false;
                stitch = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                var f = new Vector4[6]; var r = new Vector4[6]; var u = new Vector4[6];
                for (int i = 0; i < 6; i++)
                {
                    f[i] = FaceRot[i] * Vector3.forward;
                    r[i] = FaceRot[i] * Vector3.right;
                    u[i] = FaceRot[i] * Vector3.up;
                }
                stitch.SetVectorArray(FaceFId, f);
                stitch.SetVectorArray(FaceRId, r);
                stitch.SetVectorArray(FaceUId, u);
            }
            if (probe == null)
            {
                var go = new GameObject("EnvProbe");
                go.transform.SetParent(transform, false);
                probe = go.AddComponent<Camera>();
                probe.enabled = false;
                probe.fieldOfView = 90f;
                probe.aspect = 1f;
                probe.nearClipPlane = 0.3f;
                probe.farClipPlane = FarM;
                probe.depth = -60f;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.antialiasing = AntialiasingMode.None;
            }
            for (int i = 0; i < 6; i++)
            {
                if (faces[i] != null) continue;
                faces[i] = new RenderTexture(faceRes, faceRes, 16, RenderTextureFormat.ARGB32)
                { name = "EnvFace" + i, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                faces[i].Create();
                stitch.SetTexture(FaceTex[i], faces[i]);
            }
            if (pano == null)
            {
                pano = new RenderTexture(faceRes * 4, faceRes * 2, 0, RenderTextureFormat.ARGB32)
                {
                    name = "EnvPano", useMipMap = true, autoGenerateMips = true,
                    wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear, hideFlags = HideFlags.HideAndDontSave,
                };
                pano.Create();
                drawn = 0;
            }
            return true;
        }

        ChaseCamera FindChase()
        {
            if (chase != null && chase.isActiveAndEnabled && chase.targetCar != null) return chase;
            if (Time.unscaledTime < nextFind) return null;
            nextFind = Time.unscaledTime + 0.5f;
            chase = null;
            foreach (var c in FindObjectsByType<ChaseCamera>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled && c.targetCar != null) { chase = c; break; }
            return chase;
        }

        void LateUpdate()
        {
            var ch = ReflectionPrefs.On ? FindChase() : null;
            if (ch == null)
            {
                if (drawn > 0 || pano != null) Release();
                return;
            }
            if (!Ensure()) return;
            var view = ch.GetComponent<Camera>();
            if (view == null) view = Camera.main;
            // A cold start fills every face before the paint sees any of it.
            int n = drawn < 6 ? 6 : (FrameRatePrefs.Max && !Application.isMobilePlatform ? 2 : 1);
            RenderAround(ch.targetCar.transform, view, n);
        }

        /// <summary>Draw <paramref name="faces"/> of the six views round
        /// <paramref name="car"/> (the car hidden), restitch, publish.</summary>
        void RenderAround(Transform car, Camera view, int n)
        {
            probe.transform.position = car.position + car.up * EyeUp;
            if (view != null)
            {
                probe.clearFlags = view.clearFlags == CameraClearFlags.Depth || view.clearFlags == CameraClearFlags.Nothing
                    ? CameraClearFlags.Skybox : view.clearFlags;
                probe.backgroundColor = view.backgroundColor;
                probe.cullingMask = view.cullingMask & ~(1 << 5);   // no UI
            }

            // The watched car is not in its own reflection.
            if (hiddenRoot != car || hidden == null)
            {
                hiddenRoot = car;
                hidden = car.GetComponentsInChildren<Renderer>();
                hiddenWas = new bool[hidden.Length];
            }
            for (int i = 0; i < hidden.Length; i++)
                if (hidden[i] != null) { hiddenWas[i] = hidden[i].forceRenderingOff; hidden[i].forceRenderingOff = true; }

            for (int k = 0; k < n; k++)
            {
                int i = next; next = (next + 1) % 6;
                probe.transform.rotation = FaceRot[i];
                var req = new RenderPipeline.StandardRequest { destination = faces[i] };
                if (RenderPipeline.SupportsRenderRequest(probe, req)) RenderPipeline.SubmitRenderRequest(probe, req);
                else { probe.targetTexture = faces[i]; probe.Render(); probe.targetTexture = null; }
                drawn++;
            }

            for (int i = 0; i < hidden.Length; i++)
                if (hidden[i] != null) hidden[i].forceRenderingOff = hiddenWas[i];

            Graphics.Blit(null, pano, stitch);
            Shader.SetGlobalTexture(EnvPanoId, pano);
            // Readers ask for a blur in mips of a 512-wide panorama
            // (PSXSkyReflect.cginc): +1 at 1024, -1 at 256.
            Shader.SetGlobalFloat(EnvLodBiasId, Mathf.Log(pano.width / 512f, 2f));
            Shader.SetGlobalFloat(EnvOnId, 1f);
        }

        /// <summary>Editor shots and checks (nothing runs LateUpdate outside
        /// play): every view round <paramref name="car"/> now, published.
        /// False when the shader is missing. <see cref="Off"/> undoes it.</summary>
        public static bool CaptureNow(Transform car, Camera view)
        {
            if (inst == null)
            {
                var go = new GameObject("~CarReflections") { hideFlags = HideFlags.HideAndDontSave };
                inst = go.AddComponent<CarReflections>();
            }
            if (car == null || !inst.Ensure()) return false;
            inst.RenderAround(car, view, 6);
            return true;
        }

        /// <summary>The current panorama (checks and shots), or null.</summary>
        public static RenderTexture Panorama => inst != null ? inst.pano : null;

        /// <summary>The paint back to the sky lookup alone.</summary>
        public static void Off() => Shader.SetGlobalFloat(EnvOnId, 0f);
    }
}
