using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSXRacing
{
    /// <summary>
    /// ONE EXTRA VIEW OF THE WORLD A FRAME (the perf probe, 2026-10-25; the
    /// owner: "Dropping to as low as 12FPS", cockpit 8-10 ms slower than
    /// chase). Every camera render costs its whole URP setup, culling and
    /// submit whatever its size - a 256x64 mirror about what a face of the
    /// reflection probe costs - and in the cockpit three of them drew every
    /// frame on top of the view: the rear-view or a door mirror, and two of
    /// the probe's six faces. Now the secondary views share one slot a frame:
    ///   * the rear-view mirror: even frames (as before);
    ///   * a door mirror: every fourth frame (the odd frame 1 mod 4) while one
    ///     is in view - two in view take turns;
    ///   * the car-reflection probe: one face on the odd frames no door mirror
    ///     takes (every fourth frame with a door mirror in view, every other
    ///     without: a new panorama every 24 or 12 frames) - and one face every
    ///     frame in the chase views, where no mirror draws (every 6 frames;
    ///     it was two faces a frame at MAX, every 3).
    /// The door mirrors' "in view" is read a frame late by both sides, so the
    /// probe and a door mirror agree on whose slot it is whatever order their
    /// LateUpdates run in.
    /// </summary>
    public static class ExtraViews
    {
        // the last four frames: were the cabin mirrors live, was a door mirror seen
        static readonly int[] frameOf = { -1, -1, -1, -1 };
        static readonly bool[] mirrorsAt = new bool[4], doorAt = new bool[4];

        /// <summary>The cabin's mirrors this frame: live (the cockpit is
        /// showing), and whether a door mirror is in the driver's view.</summary>
        public static void Report(bool mirrorsLive, bool doorSeen)
        {
            int f = Time.frameCount, i = f & 3;
            frameOf[i] = f; mirrorsAt[i] = mirrorsLive; doorAt[i] = doorSeen;
        }

        static bool Was(int f, bool[] flags) { int i = f & 3; return frameOf[i] == f && flags[i]; }

        /// <summary>Is the cabin in use (its mirrors live last frame)?</summary>
        public static bool MirrorsLive(int f) => Was(f - 1, mirrorsAt);

        /// <summary>Frame <paramref name="f"/> is a door mirror's.</summary>
        public static bool DoorFrame(int f) => (f & 3) == 1 && Was(f - 1, doorAt);

        /// <summary>The probe may draw a face on frame <paramref name="f"/>.</summary>
        public static bool ProbeFrame(int f) => !MirrorsLive(f) || ((f & 1) == 1 && !DoorFrame(f));

        /// <summary>The perf probe's A/B: false is the schedule before
        /// 2026-10-25 (a door mirror on every odd frame, two probe faces a
        /// frame at MAX), for <see cref="ProbeFaces"/> and
        /// <see cref="DoorSlot"/>.</summary>
        public static bool Enabled = true;

        /// <summary>How many probe faces frame <paramref name="f"/> draws
        /// (after the cold start's six).</summary>
        public static int ProbeFaces(int f) =>
            Enabled ? (ProbeFrame(f) ? 1 : 0) : (FrameRatePrefs.Max && !Application.isMobilePlatform ? 2 : 1);

        /// <summary>A door mirror may draw on frame <paramref name="f"/>, and
        /// which of two in view (<paramref name="second"/>) has the turn.</summary>
        public static bool DoorSlot(int f, out bool second)
        {
            if (!Enabled) { second = ((f >> 1) & 1) == 1; return (f & 1) == 1; }
            second = ((f >> 2) & 1) == 1;
            return DoorFrame(f);
        }
    }

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
    /// textures, a face or two a frame (a whole set from one eye every few
    /// frames), then stitched into one equirect panorama with mips
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
        static readonly int FaceTexId = Shader.PropertyToID("_Face");

        /// <summary>Roof height the views are taken from, metres above the
        /// car's origin (its ground point).</summary>
        const float EyeUp = 1.15f;
        /// <summary>How far the reflection sees: a bonnet reflects the street,
        /// not the skyline's draw distance.</summary>
        const float FarM = 220f;

        /// <summary>NOT IN ANY CAR'S MIRROR (2026-10-23, the owner: car
        /// windows flicker, "especially in fog or rain"): the falling rain
        /// (WeatherFx), every car's tyre spray and splash (TireSpray) and
        /// tyre smoke (TireSmoke) live on this layer (Unity's TransparentFX,
        /// which nothing else uses), and the probe's culling mask leaves it
        /// out. They are near-field puffs and streaks, a metre or two from the
        /// roof the probe stands on: in a 256-pixel face a drop or a puff
        /// three metres out is a blob a tenth of the view across, new every
        /// refresh, and the glass - which shows the whole sphere, sharp - was
        /// flashing with them. Every camera that draws the world still draws
        /// them.</summary>
        public const int NoReflectLayer = 1;

        /// <summary>THE WORLD IS NOT INFINITELY FAR (2026-10-23). The owner,
        /// driving the '69 Charger down a street: the reflections "move in
        /// the blue arrow direction" (sideways, across the roof) "when they
        /// should move in the red arrow direction" (down the flank, front to
        /// back). Nothing in the chain is mirrored - ReflectionMapCheck traces
        /// every face, the panorama and the paint against coloured boxes on
        /// OpenGL and Direct3D, 100% right. What was wrong is the model: a
        /// panorama read by direction alone puts everything at infinity, so a
        /// panel shows one direction and the world can only SWEEP across it by
        /// angle (a house passing changes its heading, and on a curved roof
        /// that is a sideways slide); it never FLOWS past along the body the
        /// way a real car's reflections do, because the world is a few metres
        /// to a few dozen away. So the panorama gets a distance: before each
        /// set the probe measures how far the nearest solid thing is in eight
        /// headings (physics rays at the eye's height, eased between sets),
        /// and the paint traces each pixel's own reflected ray to that
        /// cylinder - or to the road - and reads the panorama in the
        /// direction of the hit (PSXSkyReflect.cginc, PSXEnvRay). A door
        /// passing a house now shows it sliding from the front of the door to
        /// the back at road speed.</summary>
        const float RadiusNear = 4f, RadiusFar = 40f, RadiusEase = 0.35f;

        static CarReflections inst;
        Camera probe;
        readonly RenderTexture[] faces = new RenderTexture[6];
        RenderTexture pano;
        Material stitch;
        int next, drawn;
        int faceRes;
        ChaseCamera chase;
        float nextFind;
        readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
        readonly System.Collections.Generic.List<bool> hiddenWas = new System.Collections.Generic.List<bool>();
        // the set being drawn: its eye and the distances round it
        Vector3 setEye;
        readonly float[] radius = new float[8];
        Transform radiusFor;
        static readonly RaycastHit[] rayHits = new RaycastHit[16];
        static readonly int EnvEyeId = Shader.PropertyToID("_PSXEnvEye");
        static readonly int EnvRad0Id = Shader.PropertyToID("_PSXEnvRad0");
        static readonly int EnvRad1Id = Shader.PropertyToID("_PSXEnvRad1");
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
            next = 0;
            radiusFor = null;
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
            }
            if (pano == null)
            {
                pano = new RenderTexture(faceRes * 4, faceRes * 2, 0, RenderTextureFormat.ARGB32)
                {
                    name = "EnvPano", useMipMap = true, autoGenerateMips = false,
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
            // Then ONE face on the frames it may have (ExtraViews: every frame
            // in the chase views, the odd frames the door mirrors leave it in
            // the cockpit) - it was two a frame at MAX, and each face is a
            // whole camera render (the perf probe, 2026-10-25).
            int n = drawn < 6 ? 6 : ExtraViews.ProbeFaces(Time.frameCount);
            RenderAround(ch.targetCar.transform, view, n);
        }

        /// <summary>Draw <paramref name="n"/> of the six views round
        /// <paramref name="car"/> (the car hidden); when they complete a set,
        /// restitch and publish.
        ///
        /// A SET OF SIX FROM ONE EYE, SHOWN WHOLE (2026-10-23; the owner: car
        /// windows "flicker reflections inaccurately while driving"). The
        /// faces used to be drawn a face or two a frame, each from where the
        /// car was THAT frame, and restitched every frame: the panorama was
        /// always a patchwork of up to three moments half a metre apart, its
        /// seams shifting every frame, and the glass reads it sharp. Now the
        /// eye is fixed when a set starts, every face of the set is drawn
        /// from it, and the panorama the paint reads is replaced only when the
        /// sixth is in - one moment, no seams, a new one every third frame at
        /// two a frame (and the stitch and its mips are paid once a set, not
        /// every frame).</summary>
        void RenderAround(Transform car, Camera view, int n)
        {
            if (n <= 0) { PushEye(car); return; }
            if (next == 0)
            {
                setEye = car.position + car.up * EyeUp;
                Measure(car, setEye);
            }
            probe.transform.position = setEye;
            if (view != null)
            {
                probe.clearFlags = view.clearFlags == CameraClearFlags.Depth || view.clearFlags == CameraClearFlags.Nothing
                    ? CameraClearFlags.Skybox : view.clearFlags;
                probe.backgroundColor = view.backgroundColor;
                probe.cullingMask = view.cullingMask & ~((1 << 5) | (1 << NoReflectLayer));   // no UI, no weather
            }

            // The watched car is not in its own reflection - all of it as it
            // is NOW: the cockpit, its arms and door mirrors, the lamps and a
            // re-dressed body all arrive after the car is first seen, and a
            // list taken once drew them into the faces from inside the car.
            car.GetComponentsInChildren(false, hidden);
            hiddenWas.Clear();
            for (int i = 0; i < hidden.Count; i++) { hiddenWas.Add(hidden[i].forceRenderingOff); hidden[i].forceRenderingOff = true; }

            bool whole = false;
            for (int k = 0; k < n && !whole; k++)
            {
                int i = next; next = (next + 1) % 6;
                probe.transform.rotation = FaceRot[i];
                var req = new RenderPipeline.StandardRequest { destination = faces[i] };
                if (FacePath == 0 && RenderPipeline.SupportsRenderRequest(probe, req)) RenderPipeline.SubmitRenderRequest(probe, req);
                else { probe.targetTexture = faces[i]; probe.Render(); probe.targetTexture = null; }
                drawn++;
                whole = next == 0;
            }

            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i] != null) hidden[i].forceRenderingOff = hiddenWas[i];

            // The paint traces from the car's eye AS IT IS NOW, every frame -
            // not from where the published set was drawn. Traced from the
            // set's eye, the reflection slid on between refreshes and snapped
            // back at each one wherever the measured distance was off (the
            // glass changed half as much again as a refresh every frame
            // would); from the car's own eye a rigid car's reflection holds
            // still between sets, as it always did.
            PushEye(car);
            if (!whole) return;

            Stitch();
            Shader.SetGlobalTexture(EnvPanoId, pano);
            // Readers ask for a blur in mips of a 512-wide panorama
            // (PSXSkyReflect.cginc): +1 at 1024, -1 at 256.
            Shader.SetGlobalFloat(EnvLodBiasId, Mathf.Log(pano.width / 512f, 2f));
            // how far the world is round the car, measured with this set
            Shader.SetGlobalVector(EnvRad0Id, new Vector4(radius[0], radius[1], radius[2], radius[3]));
            Shader.SetGlobalVector(EnvRad1Id, new Vector4(radius[4], radius[5], radius[6], radius[7]));
            Shader.SetGlobalFloat(EnvOnId, 1f);
        }

        /// <summary>The car's eye as it is now, for the paint (every frame,
        /// drawn or not).</summary>
        void PushEye(Transform car)
        {
            if (drawn < 6) return;
            Vector3 eyeNow = car.position + car.up * EyeUp;
            Shader.SetGlobalVector(EnvEyeId, new Vector4(eyeNow.x, eyeNow.y, eyeNow.z, EyeUp));
        }

        /// <summary>How far the nearest solid thing is from the eye in eight
        /// headings, k x 45 degrees from +X toward +Z (the panorama's own
        /// atan2(z, x)), clamped to RadiusNear..RadiusFar and eased from set to
        /// set so a gap between two houses is a swell, not a jump. The car's
        /// own colliders are skipped (the eye is inside them).</summary>
        void Measure(Transform car, Vector3 eye)
        {
            bool fresh = radiusFor != car;
            radiusFor = car;
            int mask = ~((1 << 5) | (1 << NoReflectLayer));
            for (int i = 0; i < 8; i++)
            {
                float a = i * (Mathf.PI / 4f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float r = RadiusFar;
                int hits = Physics.RaycastNonAlloc(eye, dir, rayHits, RadiusFar, mask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits; h++)
                    if (rayHits[h].distance < r && !rayHits[h].collider.transform.IsChildOf(car)) r = rayHits[h].distance;
                r = Mathf.Max(r, RadiusNear);
                radius[i] = fresh ? r : Mathf.Lerp(radius[i], r, RadiusEase);
            }
        }

        /// <summary>The six views into the panorama, one face a pass (see
        /// Hidden/PSX/EnvPano: the one-pass stitch drew a single face in the
        /// WebGL player), then its mips once.</summary>
        void Stitch()
        {
            for (int i = 0; i < 6; i++)
            {
                stitch.SetTexture(FaceTexId, faces[i]);
                stitch.SetVector(FaceFId, FaceRot[i] * Vector3.forward);
                stitch.SetVector(FaceRId, FaceRot[i] * Vector3.right);
                stitch.SetVector(FaceUId, FaceRot[i] * Vector3.up);
                Graphics.Blit(null, pano, stitch);
            }
            pano.GenerateMips();
        }

        /// <summary>Checks only (ReflTour): how the six views are drawn - 0 a
        /// render request (the game's), 1 Camera.Render into the face.</summary>
        public static int FacePath;

        /// <summary>Checks only (ReflTour): one of the six views as drawn.</summary>
        public static RenderTexture Face(int i) => inst != null ? inst.faces[i] : null;

        /// <summary>Checks only (ReflTour): <paramref name="n"/> more views round
        /// <paramref name="car"/> the way LateUpdate draws them (a new set when
        /// <paramref name="fresh"/>); true when this call completed a set.</summary>
        public static bool StepNow(Transform car, Camera view, int n, bool fresh)
        {
            if (inst == null)
            {
                var go = new GameObject("~CarReflections") { hideFlags = HideFlags.HideAndDontSave };
                inst = go.AddComponent<CarReflections>();
            }
            // (its own LateUpdate would release the views between frames: no chase camera here)
            if (inst.enabled) inst.enabled = false;
            if (car == null || !inst.Ensure()) return false;
            if (fresh) inst.next = 0;
            inst.RenderAround(car, view, n);
            return inst.next == 0;
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
            inst.next = 0;   // a whole set, from here
            inst.RenderAround(car, view, 6);
            return true;
        }

        /// <summary>The current panorama (checks and shots), or null.</summary>
        public static RenderTexture Panorama => inst != null ? inst.pano : null;

        /// <summary>The paint back to the sky lookup alone.</summary>
        public static void Off() => Shader.SetGlobalFloat(EnvOnId, 0f);
    }
}
