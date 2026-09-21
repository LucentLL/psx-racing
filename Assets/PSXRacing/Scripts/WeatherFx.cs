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
    /// Snow goes through PSX/Decal — the skid-mark shader — so the flakes
    /// take the scene's own fog and quantise to the same grid as everything
    /// else; a snowfall that does not fade into the fog wall floats in front
    /// of it. The textures are drawn here, in code, at four by sixteen and
    /// eight by eight: a streak and a dot are not worth a file.
    ///
    /// RAIN IS LIT (2026-09-21, the NFS 2015 night pass). Rain used to go
    /// through PSX/Decal too, which made every streak the same pale blue at
    /// noon and at midnight: a grey screen door over a black night city,
    /// untouched by the lamps and beams it fell through. It now goes through
    /// PSX/Rain, additive and vertex-lit from the street-lamp table, the
    /// headlight table and the scene's ambient — invisible against the dark,
    /// an orange column under every sodium head, a white cone of streaks in
    /// every car's beams, still plainly there on a rainy noon. It is also
    /// THINNER and DENSER than before (the NFS frames are a fine fast
    /// hatching, not a sparse drizzle of fat dashes), and its streaks lean
    /// into the camera's motion at speed. See Build for the numbers.
    ///
    /// On a rain day this also hands every car a <see cref="TireSpray"/>, and
    /// publishes <see cref="LensRain"/> for the lens-drop pass (SpeedBlur's
    /// LensFx) — the water on the camera belongs to the same weather as the
    /// water in the air, so it starts and stops with this instance.
    ///
    /// Sound is deliberately absent. There is no rain loop in the pack yet,
    /// and a silent rain beats a wrong one.
    /// </summary>
    public class WeatherFx : MonoBehaviour
    {
        public Weather weather = Weather.Clear;

        static WeatherFx instance;

        /// <summary>Seconds <see cref="LensRain"/> takes to reach 1 after the
        /// rain starts. Drops GATHER on a lens; a camera that is fully beaded
        /// on the first frame of a race reads as a filter, not as weather.</summary>
        public const float LensRainEase = 3f;

        /// <summary>How wet the camera's lens should be, 0..1: 0 when this
        /// scene has no RAIN instance (snow is not rain — a flake on a lens
        /// is a white speck, not a refracting bead), easing up to 1 over
        /// <see cref="LensRainEase"/> seconds from the moment the rain
        /// started falling. Read every frame by SpeedBlur's LensFx, which owns
        /// every other gate (the LENS FX pref, on foot, top-down view).
        /// Stateless: computed from the start time on each read, so any
        /// number of readers agree and none of them can advance it.</summary>
        public static float LensRain
        {
            get
            {
                if (instance == null || instance.rainSince < 0f) return 0f;
                float x = Mathf.Clamp01((Time.time - instance.rainSince) / LensRainEase);
                return x * x * (3f - 2f * x);
            }
        }

        /// <summary>True while this scene's rain is falling (built as rain,
        /// not snow, and not yet destroyed). TireSpray gates on it, so a car
        /// that outlives the weather stops throwing water.</summary>
        public static bool Raining => instance != null && instance.rainSince >= 0f;

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

        /// <summary>
        /// CHANGE THE WEATHER MID-SCENE — the debug bench's switch.
        ///
        /// <see cref="Ensure"/> is the scene-load entry and only ever ADDS: it
        /// returns on clear and fog, and handed a second kind it renames the
        /// instance without rebuilding it, so rain "switched" to snow kept
        /// falling as rain. Here a change of kind tears the old slab down
        /// (its material and texture with it) and lets Ensure build the new
        /// one, and clear or fog leaves nothing falling. The lens beads and the
        /// tyre spray follow on their own: both read <see cref="Raining"/>.
        /// </summary>
        public static void Set(Weather w)
        {
            if (instance != null && instance.weather != w) PreviewClear();
            Ensure(w);
        }

        ParticleSystem ps;
        Material mat;
        Texture2D tex;

        /// <summary>Rain's slab height above the lens (m) and the drops' fall
        /// speed (m/s). Named because the speed lead in LateUpdate is derived
        /// from them: a drop takes RainHeight / RainSpeed seconds to fall to
        /// eye level.</summary>
        const float RainHeight = 13f, RainSpeed = 15f;
        /// <summary>Cap on the rain slab's speed lead (m), and the one-frame
        /// camera speed (m/s) above which a move is a CUT (respawn, replay
        /// camera switch, scene teleport) rather than motion.</summary>
        const float MaxLead = 50f, CutSpeed = 120f;
        /// <summary>Seconds between scans for cars that need a TireSpray.
        /// Twice a second: a car that spawns mid-scene (a respawn, a meet
        /// arrival) is spraying within half a second, and the scan is one
        /// FindObjectsByType, not a per-frame cost.</summary>
        const float SprayPollSeconds = 0.5f;

        /// <summary>Time.time when the RAIN started (Build), or -1 when this
        /// instance is snow or has not built yet. Drives LensRain and Raining.</summary>
        float rainSince = -1f;
        float sprayPoll;
        /// <summary>Smoothed horizontal camera velocity for the rain lead.</summary>
        Vector3 camVel, lastCamPos;
        bool haveLastCam;

        void Start() => Build();

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (mat != null) Destroy(mat);
            if (tex != null) Destroy(tex);
        }

        void LateUpdate()
        {
            // Every car on a rain day throws spray; cars that appear later
            // (respawns, meet arrivals) are found by the poll. Ahead of the
            // camera check: the spray does not need a main camera.
            if (rainSince >= 0f)
            {
                sprayPoll -= Time.unscaledDeltaTime;
                if (sprayPoll <= 0f)
                {
                    sprayPoll = SprayPollSeconds;
                    AttachSpray();
                }
            }

            var cam = Camera.main;
            if (cam == null) return;
            var t = cam.transform;
            transform.position = SlabOver(t) + RainLead(t.position);
        }

        /// <summary>
        /// Where the slab sits for a still camera. Above and a little ahead of
        /// the lens: the slab is wide enough that the camera's own yaw never
        /// finds its edge, and the lead keeps the frame full at speed instead
        /// of the drops falling behind the car. (The fixed six metres only
        /// does that standing still; RainLead adds the part that scales with
        /// speed.)
        /// </summary>
        Vector3 SlabOver(Transform t)
        {
            return t.position + Vector3.up * (weather == Weather.Snow ? 10f : RainHeight)
                   + Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized * 6f;
        }

        /// <summary>
        /// EDIT-MODE entry for screenshot tools: put a settled rain (or snow)
        /// over <paramref name="cam"/> for a render request.
        ///
        /// Edit mode never calls Start on a plain MonoBehaviour, so without this
        /// the lit rain could only ever be seen in play mode and the night-look
        /// shots would photograph a wet road with nothing falling on it. This
        /// builds the same particle system play mode builds, parks the slab
        /// over the shot camera and SIMULATES <paramref name="seconds"/> of
        /// fall (more than the 1.3 s drop life gives the steady state), so the
        /// frame holds exactly the density a player would see. HideFlags
        /// DontSave, so a tool that saves the scene never bakes weather into
        /// it. Publishes NO lens rain (a lens shot sets LensFx.PreviewSet
        /// itself) and attaches no spray. Call <see cref="PreviewClear"/>
        /// afterwards; a call for a different kind rebuilds.
        /// </summary>
        public static void Preview(Weather w, Camera cam, float seconds = 2.5f)
        {
            if (w != Weather.Rain && w != Weather.Snow) { PreviewClear(); return; }
            if (instance != null && instance.ps != null && instance.weather != w) PreviewClear();
            Ensure(w);
            if (instance == null) return;
            instance.gameObject.hideFlags = HideFlags.DontSave;
            if (instance.ps == null) instance.Build();
            // A picture, not weather: nothing downstream reads a preview as rain.
            instance.rainSince = -1f;
            if (cam != null) instance.transform.position = instance.SlabOver(cam.transform);
            instance.ps.Simulate(Mathf.Max(0.1f, seconds), true, true);
        }

        /// <summary>Remove a <see cref="Preview"/> instance (edit mode or play
        /// mode). The material and texture are freed HERE: OnDestroy is not
        /// called in edit mode on a component that never woke.</summary>
        public static void PreviewClear()
        {
            if (instance == null) return;
            var fx = instance;
            instance = null;
            bool playing = Application.isPlaying;
            if (fx.mat != null) { if (playing) Destroy(fx.mat); else DestroyImmediate(fx.mat); }
            if (fx.tex != null) { if (playing) Destroy(fx.tex); else DestroyImmediate(fx.tex); }
            if (playing) Destroy(fx.gameObject); else DestroyImmediate(fx.gameObject);
        }

        /// <summary>
        /// Extra lead for the RAIN slab along the camera's own motion, so the
        /// drops still reach eye level IN FRONT of a moving camera.
        ///
        /// The six-metre lead above is only right standing still. A drop takes
        /// RainHeight / RainSpeed = 0.87 s to fall to the lens's height, and at
        /// 100 km/h the camera covers 24 m in that time — past the far edge of
        /// the slab — so every drop born over the road ahead had been driven
        /// under before it came down, and the frame emptied of rain exactly
        /// when the car was going fastest. Leading by (camera velocity x fall
        /// time) puts the drops where the camera WILL be when they arrive, so
        /// at eye level a moving frame is as full as a still one. Zero for a still
        /// camera (the picture there is unchanged) and for snow (a flake takes
        /// six seconds to fall; no lead can chase that, and snow is not part
        /// of this pass).
        ///
        /// The velocity is measured from the camera's own position, smoothed,
        /// and a one-frame jump faster than CutSpeed is treated as a cut and
        /// resets it: a respawn or a replay camera switch must not fling the
        /// slab fifty metres down the road for the next second.
        /// </summary>
        Vector3 RainLead(Vector3 camPos)
        {
            if (weather == Weather.Snow) { haveLastCam = false; return Vector3.zero; }
            float dt = Time.deltaTime;
            if (haveLastCam && dt > 1e-4f)
            {
                Vector3 v = (camPos - lastCamPos) / dt;
                v.y = 0f;
                if (v.sqrMagnitude > CutSpeed * CutSpeed) camVel = Vector3.zero;
                else camVel = Vector3.Lerp(camVel, v, 1f - Mathf.Exp(-6f * dt));
            }
            lastCamPos = camPos;
            haveLastCam = true;
            return Vector3.ClampMagnitude(camVel * (RainHeight / RainSpeed), MaxLead);
        }

        /// <summary>Give every live car without one a <see cref="TireSpray"/>.
        /// Idle sprays cost a component and nothing else (the particle system
        /// is built on the first frame a car actually sprays).</summary>
        static void AttachSpray()
        {
            foreach (var car in FindObjectsByType<CarController>(FindObjectsInactive.Exclude))
                if (!car.TryGetComponent<TireSpray>(out _)) car.gameObject.AddComponent<TireSpray>();
        }

        void Build()
        {
            // Once per instance: a Preview in play mode has already built by
            // the time Start arrives, and a second ParticleSystem cannot be
            // added to the same GameObject.
            if (ps != null) return;
            bool snow = weather == Weather.Snow;
            // Emit straight DOWN: a box shape emits along its own forward.
            transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = false;
            // RAIN, retuned for the NFS pass (2026-09-21): thinner (2.8 cm
            // streaks, down from 4.5) and denser (1100 a second over a 40 m
            // slab, 2.3x the old drops per square metre), so it reads as a
            // fine fast hatching rather than a drizzle of fat dashes. About
            // 1430 alive at once (rate x life); the 2200 cap is headroom, not
            // a throttle. Paler colour because the shader now LIGHTS it: the
            // rgb is only the water's faint blue, the brightness comes from
            // the lamps, the beams and the sky.
            main.maxParticles = snow ? 900 : 2200;
            main.startLifetime = snow ? 7f : 1.3f;
            main.startSpeed = snow ? 1.6f : RainSpeed;
            main.startSize = snow ? 0.10f : 0.028f;
            main.startColor = snow ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.80f, 0.85f, 0.95f, 0.55f);
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = snow ? 220f : 1100f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Rain's slab is smaller than snow's so the density buys drops
            // where the camera looks; the speed lead (RainLead) keeps it over
            // the road ahead, which is what used to need the extra width.
            shape.scale = snow ? new Vector3(48f, 48f, 2f) : new Vector3(40f, 40f, 2f);

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
            if (!snow)
            {
                // Streak length from the drop's own fall (15 m/s x 0.045 =
                // 0.68 m), plus a lean into the CAMERA's motion: at 150 km/h
                // the camera term adds another 0.8 m along the direction of
                // travel, so the rain rakes toward the lens the way it does
                // in the NFS frames instead of falling past a car that
                // appears to be parked.
                r.velocityScale = 0.045f;
                r.cameraVelocityScale = 0.02f;
                r.lengthScale = 0f;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -10f;

            tex = snow ? Dot() : Streak();
            // Rain through PSX/Rain (lit, additive — see the class summary);
            // snow stays on PSX/Decal. If PSX/Rain is missing from a build the
            // rain falls back to the old unlit look rather than to pink.
            Shader shader = null;
            if (!snow) shader = Shader.Find("PSX/Rain");
            if (shader == null) shader = Shader.Find("PSX/Decal");
            mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "WeatherFx (runtime)" };
            mat.mainTexture = tex;
            if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", Color.white);
            r.sharedMaterial = mat;

            ps.Play();

            // The rain has started: the lens begins to gather drops from now
            // (LensRain), and the first spray scan runs on the next frame.
            if (!snow)
            {
                rainSince = Time.time;
                sprayPoll = 0f;
            }
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
