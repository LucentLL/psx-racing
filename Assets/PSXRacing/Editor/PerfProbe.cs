using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FRAME-RATE PROBE (tools\perf-probe.ps1; owner 2026-10-25:
    /// "Performance is very poor. Dropping to as low as 12FPS" - a Charlotte
    /// race at night in the rain, cockpit view 39-58 FPS, chase 66-80, spikes
    /// of 40-76 ms in both). A real race, played: the venue (PSX_PERF_VENUE,
    /// UptownLoop), NIGHT, RAIN, the '70 Charger (PSX_PERF_CAR), three rivals,
    /// the hour's traffic, the player on autopilot. Then, uncapped:
    ///   COCKPIT 60 s, CHASE 60 s - frame time avg/p50/p95/p99/max, frames
    ///     over 33 and 50 ms, GC bytes a frame and collections, draws /
    ///     SetPass, and every camera that rendered: how often and its CPU ms
    ///     (begin to end of the URP camera, culling and submit included);
    ///     every frame over 33 ms listed with what happened in it.
    ///   PROFILE - the editor profiler on for a window in each view, read back
    ///     marker by marker: self ms and GC bytes a frame by marker, and the
    ///     hot path (the most expensive child, level by level) of each slow
    ///     frame.
    ///   A/B (cockpit) - each suspect switched off and on every second for
    ///     PSX_PERF_AB_SECONDS: the rear-view mirror, the door mirrors, the
    ///     car-reflection probe, the rain particles, the far city, the wet
    ///     road, the speed blur.
    /// Editor numbers: a browser is slower, so the ratios and the spike counts
    /// are what carry over. Report: PSXRacing_perf_probe.txt (and
    /// PSXRacing_perf_probe_TAG.txt with PSX_PERF_TAG) at the project root.
    /// </summary>
    public static class PerfProbe
    {
        internal static readonly StringBuilder log = new StringBuilder();
        internal static int failures;
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        internal static string Env(string key, string fallback)
        {
            string v = System.Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? fallback : v.Trim();
        }

        internal static float EnvF(string key, float fallback)
        {
            return float.TryParse(Env(key, ""), System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : fallback;
        }

        [MenuItem("PSX Racing/Perf Probe (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log.Clear();
            failures = 0;
            string venue = Env("PSX_PERF_VENUE", "UptownLoop");
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == venue) index = i;
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            log.AppendLine("PERF PROBE " + Env("PSX_PERF_TAG", "") + "  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            // by its index, or (a sandbox whose build list is another
            // edition's) by its file: the probe opens the scene itself
            string path = s >= 0 && s < scenes.Length && scenes[s].path.EndsWith("/" + venue + ".unity") ? scenes[s].path
                        : "Assets/PSXRacing/Scenes/" + venue + ".unity";
            if (index < 0 || !File.Exists(path))
            {
                Fail("the venue " + venue + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            string hour = Env("PSX_PERF_HOUR", "Night");
            int hourIdx = TimeOfDay.Morning;
            for (int h = 0; h < TimeOfDay.All.Length; h++)
                if (string.Equals(TimeOfDay.All[h].name, hour, System.StringComparison.OrdinalIgnoreCase)) hourIdx = h;
            RaceHandoff.TimeOfDayIndex = hourIdx;
            string sky = Env("PSX_PERF_WEATHER", "Rain");
            if (System.Enum.TryParse(sky, true, out Weather w)) RaceHandoff.WeatherOverride = (int)w;
            // PSX_PERF_DYNSKY=1: the DYNAMIC sky (its volumetric clouds and
            // their A/B against the old puffs); put back as it was at the end.
            dynSkyWas = SkyModePrefs.Dynamic ? 1 : 0;
            if (Env("PSX_PERF_DYNSKY", "0") == "1") SkyModePrefs.Dynamic = true;
            string carId = Env("PSX_PERF_CAR", "charger_70");
            var cars = CarCatalog.All;
            int ci = -1;
            for (int i = 0; i < cars.Count; i++) if (cars[i].id == carId) ci = i;
            // or by its model's key (charger_70 is the '70 Charger's shell)
            for (int i = 0; i < cars.Count && ci < 0; i++)
            {
                var d = CarModelLibrary.LoadFor(cars[i]);
                if (d != null && d.key == carId) { ci = i; carId = cars[i].id; }
            }
            if (ci < 0) { Fail("the car " + carId + " is in the catalog"); ci = 0; carId = cars[0].id; }
            RaceHandoff.CarSpecId = carId;
            string rivals = Env("PSX_PERF_RIVALS", "");
            if (string.IsNullOrEmpty(rivals))
                rivals = cars[(ci + 1) % cars.Count].id + ";" + cars[(ci + 2) % cars.Count].id + ";" + cars[(ci + 3) % cars.Count].id;
            RaceHandoff.OpponentSpecIds = rivals;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            RaceHandoff.StartFuelPct = 100f;
            log.AppendLine($"venue {venue}, hour {TimeOfDay.All[hourIdx].name}, weather {(RaceHandoff.WeatherOverride >= 0 ? ((Weather)RaceHandoff.WeatherOverride).ToString() : "calendar")}, " +
                           $"car {carId}, rivals {rivals}, traffic by the hour, seed {Env("PSX_PERF_SEED", "1")}");
            log.AppendLine($"unity {Application.unityVersion}, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            var go = new GameObject("PerfProbeRunner");
            go.AddComponent<PerfProbeRunner>();
            go.AddComponent<PerfProbeLate>();
        }

        internal static void Line(string s) => log.AppendLine(s);
        internal static void Fail(string what) { failures++; log.AppendLine("  FAIL " + what); }

        static int dynSkyWas = -1;

        internal static void Finish()
        {
            if (dynSkyWas >= 0) { SkyModePrefs.Dynamic = dynSkyWas == 1; dynSkyWas = -1; }
            if (VolumetricClouds.TickCount > 0)
                log.AppendLine($"volumetric clouds: driver CPU {VolumetricClouds.TickMsTotal * 1000.0 / VolumetricClouds.TickCount:0} us a frame over {VolumetricClouds.TickCount} frames (two Blits + globals)");
            log.AppendLine(failures == 0 ? "PERF PROBE DONE." : failures + " FAILURE(S).");
            string text = log.ToString();
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_perf_probe.txt"), text);
            string tag = Env("PSX_PERF_TAG", "");
            if (tag.Length > 0) File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_perf_probe_" + tag + ".txt"), text);
            Debug.Log(text);
        }
    }

    /// <summary>The A/B switches, applied after every game LateUpdate (the
    /// mirrors choose their cameras' enabled flag in theirs).</summary>
    [DefaultExecutionOrder(32000)]
    public class PerfProbeLate : MonoBehaviour
    {
        public static bool RearOff, DoorsOff;
        static Camera[] buf = new Camera[32];

        void LateUpdate()
        {
            if (!RearOff && !DoorsOff) return;
            int n = Camera.allCamerasCount;
            if (buf.Length < n) buf = new Camera[n * 2];
            n = Camera.GetAllCameras(buf);
            for (int i = 0; i < n; i++)
            {
                var c = buf[i];
                buf[i] = null;
                if (c == null) continue;
                if (RearOff && c.name == "CockpitMirrorCamera") c.enabled = false;
                if (DoorsOff && c.name.StartsWith("DoorMirrorCamera")) c.enabled = false;
            }
        }
    }

    [DefaultExecutionOrder(-32000)]
    public class PerfProbeRunner : MonoBehaviour
    {
        const float SpikeMs = 33.3f, BigSpikeMs = 50f;
        const float HangSeconds = 900f;

        class CamAgg { public double ms, maxMs; public int renders, frames; }

        class Phase
        {
            public string name;
            public readonly List<float> ms = new List<float>();
            public readonly List<float> gcKB = new List<float>();
            public readonly List<float> mainCamMs = new List<float>();
            public readonly List<float> extraCamMs = new List<float>();
            public readonly List<int> draws = new List<int>(), setPass = new List<int>(), batches = new List<int>();
            public readonly List<float> gpuMs = new List<float>();
            public readonly Dictionary<string, CamAgg> cams = new Dictionary<string, CamAgg>();
            public readonly List<string> spikes = new List<string>();
            public int collections, framesWithCollect, tileEvents, tileFrames;
            public float tileFrameMax, speedSum, distM;
            public long heap0, heap1;
            public float t0;
        }

        // ---- the frame being drawn (flushed at the start of the next) ----
        readonly Dictionary<Camera, long> camStart = new Dictionary<Camera, long>();
        readonly Dictionary<string, (double ms, int n)> frameCams = new Dictionary<string, (double, int)>();
        double frameMainMs, frameExtraMs;
        int frameCamCount;
        float frameTileMs; int frameTileN;
        string frameTileWhat = "";
        int gcCountWas;

        Phase phase;
        readonly List<Phase> done = new List<Phase>();
        ProfilerRecorder gcRec, drawRec, setPassRec, batchRec;
        readonly FrameTiming[] timings = new FrameTiming[1];
        RaceManager rm;
        CarController car;
        Vector3 lastPos;
        float bornAt;
        bool finished;
        string stage = "loading";
        Camera mainCam;

        // ---- A/B ----
        bool abOn; bool abState; float abSwitchedAt; int abSkip;
        readonly List<float> abMsOn = new List<float>(), abMsOff = new List<float>();
        readonly List<float> abCamOn = new List<float>(), abCamOff = new List<float>();
        readonly List<float> abExtraOn = new List<float>(), abExtraOff = new List<float>();
        readonly List<float> abGpuOn = new List<float>(), abGpuOff = new List<float>();

        void Awake() { bornAt = Time.realtimeSinceStartup; }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            CityWorld.TileBuilt += OnTile;
            gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            batchRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            CityWorld.TileBuilt -= OnTile;
            gcRec.Dispose(); drawRec.Dispose(); setPassRec.Dispose(); batchRec.Dispose();
        }

        void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            camStart[cam] = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || !camStart.TryGetValue(cam, out long t)) return;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            string key = cam.name;
            frameCams.TryGetValue(key, out var a);
            frameCams[key] = (a.ms + ms, a.n + 1);
            frameCamCount++;
            if (cam == mainCam) frameMainMs += ms; else frameExtraMs += ms;
        }

        void OnTile(CityWorld.TileTiming t)
        {
            frameTileN++;
            float ms = t.FrameMs;
            if (ms > frameTileMs)
            {
                frameTileMs = ms;
                frameTileWhat = t.treeFrame ? (t.trees > 0 ? "trees" : "signs/poles") : (t.slices > 0 ? $"stand-up/slice({t.slices})" : "whole tile");
            }
        }

        void Update()
        {
            Sample();
            if (!finished && Time.realtimeSinceStartup - bornAt > HangSeconds)
            {
                finished = true;
                PerfProbe.Fail("HUNG in " + stage + " after " + HangSeconds + " s");
                PerfProbe.Finish();
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Last frame's numbers into the open phase (or the A/B bins).</summary>
        // the tile builder's steps over 8 ms, from the race itself
        static readonly System.Reflection.FieldInfo jobField =
            typeof(CityWorld).GetField("job", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        readonly List<(int phase, int item, int part, float ms, float missMs)> bigSteps = new List<(int, int, int, float, float)>();

        void Sample()
        {
            var cw = CityWorld.Active;
            if (cw != null && jobField != null && jobField.GetValue(cw) is CityMeshes.TileJob tj && tj.BigSteps == null) tj.BigSteps = bigSteps;
            float ms = Time.unscaledDeltaTime * 1000f;
            int gcNow = System.GC.CollectionCount(0);
            bool collected = gcNow != gcCountWas;
            gcCountWas = gcNow;
            float kb = gcRec.Valid ? gcRec.LastValue / 1024f : -1f;
            if (phase != null)
            {
                var p = phase;
                p.ms.Add(ms);
                p.gcKB.Add(kb);
                if (collected) { p.framesWithCollect++; }
                p.mainCamMs.Add((float)frameMainMs);
                p.extraCamMs.Add((float)frameExtraMs);
                if (drawRec.Valid) p.draws.Add((int)drawRec.LastValue);
                if (setPassRec.Valid) p.setPass.Add((int)setPassRec.LastValue);
                if (batchRec.Valid) p.batches.Add((int)batchRec.LastValue);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) p.gpuMs.Add((float)timings[0].gpuFrameTime);
                foreach (var kv in frameCams)
                {
                    if (!p.cams.TryGetValue(kv.Key, out var c)) p.cams[kv.Key] = c = new CamAgg();
                    c.ms += kv.Value.ms; c.renders += kv.Value.n; c.frames++;
                    if (kv.Value.ms > c.maxMs) c.maxMs = kv.Value.ms;
                }
                if (frameTileN > 0) { p.tileEvents += frameTileN; p.tileFrames++; p.tileFrameMax = Mathf.Max(p.tileFrameMax, frameTileMs); }
                if (car != null)
                {
                    p.speedSum += Mathf.Abs(car.forwardSpeed);
                    p.distM += Vector3.Distance(car.transform.position, lastPos);
                }
                if (ms > SpikeMs && p.spikes.Count < 40)
                {
                    var sb = new StringBuilder();
                    sb.Append($"    {Time.realtimeSinceStartup - p.t0,6:0.0}s {ms,6:0.0} ms  gc {kb,6:0.0} KB{(collected ? " COLLECTED" : "")}  cams {frameMainMs:0.0} main + {frameExtraMs:0.0} extra ({frameCamCount})");
                    if (frameTileN > 0) sb.Append($"  city {frameTileMs:0.0} ms {frameTileWhat} x{frameTileN}");
                    p.spikes.Add(sb.ToString());
                }
            }
            if (abOn && car != null)
            {
                if (abSkip > 0) abSkip--;
                else
                {
                    (abState ? abMsOff : abMsOn).Add(ms);
                    (abState ? abCamOff : abCamOn).Add((float)frameMainMs);
                    (abState ? abExtraOff : abExtraOn).Add((float)frameExtraMs);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                        (abState ? abGpuOff : abGpuOn).Add((float)timings[0].gpuFrameTime);
                }
            }
            if (car != null) lastPos = car.transform.position;
            frameCams.Clear();
            frameMainMs = frameExtraMs = 0; frameCamCount = 0;
            frameTileMs = 0f; frameTileN = 0; frameTileWhat = "";
        }

        void Uncap()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        void Begin(string name)
        {
            phase = new Phase { name = name, t0 = Time.realtimeSinceStartup, heap0 = System.GC.GetTotalMemory(false) };
            gcCountWas = System.GC.CollectionCount(0);
            int c0 = gcCountWas;
            phase.collections = -c0;
        }

        void End()
        {
            if (phase == null) return;
            phase.collections += System.GC.CollectionCount(0);
            phase.heap1 = System.GC.GetTotalMemory(false);
            done.Add(phase);
            Report(phase);
            phase = null;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            if (rm == null || car == null || rm.path == null) { PerfProbe.Fail("the scene has a race, a player and a path"); Exit(); yield break; }
            int seed = 1;
            int.TryParse(PerfProbe.Env("PSX_PERF_SEED", "1"), out seed);
            Random.InitState(9173 + seed * 101);
            stage = "the countdown";
            float until = Time.realtimeSinceStartup + 20f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            if (rm.State != RaceManager.RaceState.Racing) { PerfProbe.Fail("the race goes live (" + rm.State + ")"); Exit(); yield break; }

            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            var auto = car.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.95f;
            car.allowReverse = false;
            lastPos = car.transform.position;
            Uncap();

            float secs = PerfProbe.EnvF("PSX_PERF_SECONDS", 60f);
            float warm = PerfProbe.EnvF("PSX_PERF_WARM", 8f);
            PerfProbe.Line($"race live; autopilot on the player; uncapped (vSync 0); {secs:0} s a view after {warm:0} s of warm-up; " +
                           $"screen {Screen.width}x{Screen.height}; reflections {(ReflectionPrefs.On ? "ON" : "OFF")}; speed blur {SpeedBlurPrefs.Label}");

            // ---- COCKPIT ----
            stage = "cockpit";
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            yield return WaitReal(warm);
            mainCam = Camera.main;
            PerfProbe.Line("main camera: " + (mainCam != null ? $"{mainCam.name} far {mainCam.farClipPlane:0} m, occlusion {mainCam.useOcclusionCulling}" : "none"));
            CameraCensus();
            Uncap();
            Begin("COCKPIT");
            yield return WaitReal(secs);
            End();

            // ---- CHASE ----
            stage = "chase";
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            yield return WaitReal(3f);
            mainCam = Camera.main;
            Uncap();
            Begin("CHASE");
            yield return WaitReal(secs);
            End();

            // ---- PROFILER WINDOWS ----
            if (PerfProbe.Env("PSX_PERF_PROFILE", "1") == "1")
            {
                stage = "profile chase";
                yield return ProfileWindow("CHASE");
                stage = "profile cockpit";
                ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                yield return WaitReal(2f);
                mainCam = Camera.main;
                yield return ProfileWindow("COCKPIT");
            }

            // ---- A/B: the fixes (OFF = as before 2026-10-25) and the suspects ----
            float abSecs = PerfProbe.EnvF("PSX_PERF_AB_SECONDS", 12f);
            if (abSecs > 0f)
            {
                PerfProbe.Line("");
                PerfProbe.Line($"A/B (each switched off and on every second for {abSecs:0} s; the first 3 frames after a switch not counted; FIX rows: OFF = the code before 2026-10-25):");
                PerfProbe.Line("  what                 frame ms ON -> OFF (p50, p95)   main cam ms ON -> OFF   extra cams ms ON -> OFF");
                bool cloudsOnly = PerfProbe.Env("PSX_PERF_AB", "all") == "clouds";
                PerfProbe.Line(" chase:");
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                yield return WaitReal(1f);
                mainCam = Camera.main;
                // ON = the volumetric clouds, OFF = the old puffs (the dynamic
                // sky before 2026-10-25), switched without re-applying the hour.
                if (SkyModePrefs.Dynamic)
                {
                    yield return AB("volumetric clouds", abSecs, off => SetVolClouds(!off));
                    yield return AB("vol cloud map update", abSecs, off => VolumetricClouds.Paused = off);
                    yield return AB("dynamic sky (vs photo)", abSecs, off => { SkyModePrefs.Dynamic = !off; });
                    SkyModePrefs.Dynamic = true;
                }
                if (!cloudsOnly) {
                yield return AB("FIX shared lamp field", abSecs, off => StreetLights.ShareFieldWindow = !off);
                yield return AB("FIX one extra view a frame", abSecs, off => ExtraViews.Enabled = !off);
                yield return AB("FIX skyline refresh cache", abSecs, off => CitySkyline.CacheRefresh = !off);
                yield return AB("car reflections", abSecs, off => { foreach (var r in Object.FindObjectsByType<CarReflections>(FindObjectsSortMode.None)) r.enabled = !off; });
                }
                PerfProbe.Line(" cockpit:");
                ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                yield return WaitReal(1f);
                mainCam = Camera.main;
                if (SkyModePrefs.Dynamic) yield return AB("volumetric clouds", abSecs, off => SetVolClouds(!off));
                if (!cloudsOnly) {
                yield return AB("FIX shared lamp field", abSecs, off => StreetLights.ShareFieldWindow = !off);
                yield return AB("FIX one extra view a frame", abSecs, off => ExtraViews.Enabled = !off);
                yield return AB("FIX skyline refresh cache", abSecs, off => CitySkyline.CacheRefresh = !off);
                yield return AB("rear-view mirror", abSecs, off => PerfProbeLate.RearOff = off);
                yield return AB("door mirrors", abSecs, off => PerfProbeLate.DoorsOff = off);
                yield return AB("car reflections", abSecs, off => { foreach (var r in Object.FindObjectsByType<CarReflections>(FindObjectsSortMode.None)) r.enabled = !off; });
                yield return AB("skyline (drawing)", abSecs, off => { foreach (var f in Object.FindObjectsByType<CitySkyline>(FindObjectsSortMode.None)) foreach (var r in f.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = off; });
                yield return AB("far city (drawing)", abSecs, off => { foreach (var f in Object.FindObjectsByType<CityFar>(FindObjectsSortMode.None)) foreach (var r in f.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = off; });
                yield return AB("far buildings (city-popin)", abSecs, off => { CityProxies.Suppress = off; });
                }
                SetVolClouds(true);
            }
            ReportBigSteps();
            PerfProbe.Line("");
            PerfProbe.Line($"race state at the end: {rm.State}");
            Exit();
        }

        /// <summary>The A/B's switch: the volumetric clouds, or the old puffs
        /// under the same sky (the hour is re-applied, as the pause menu's SKY
        /// switch would).</summary>
        static void SetVolClouds(bool on)
        {
            if (VolumetricClouds.Override == on) return;
            VolumetricClouds.Override = on;
            TimeOfDay.RefreshSky();
        }

        void Exit()
        {
            if (finished) return;
            finished = true;
            PerfProbe.Finish();
            EditorApplication.Exit(PerfProbe.failures == 0 ? 0 : 1);
        }

        static IEnumerator WaitReal(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        /// <summary>Every camera in the scene with what it draws.</summary>
        void CameraCensus()
        {
            var sb = new StringBuilder("cameras:");
            foreach (var c in Camera.allCameras)
                sb.Append($"\n    {c.name,-26} depth {c.depth,5:0} far {c.farClipPlane,6:0} m  mask 0x{c.cullingMask:X8}  target {(c.targetTexture != null ? c.targetTexture.width + "x" + c.targetTexture.height : "screen")}  occlusion {c.useOcclusionCulling}");
            PerfProbe.Line(sb.ToString());
        }

        IEnumerator AB(string what, float seconds, System.Action<bool> set)
        {
            stage = "A/B " + what;
            abMsOn.Clear(); abMsOff.Clear(); abCamOn.Clear(); abCamOff.Clear(); abExtraOn.Clear(); abExtraOff.Clear();
            abGpuOn.Clear(); abGpuOff.Clear();
            abState = false; set(false);
            abSkip = 3; abOn = true;
            float end = Time.realtimeSinceStartup + seconds;
            abSwitchedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < end)
            {
                if (Time.realtimeSinceStartup - abSwitchedAt >= 1f)
                {
                    abState = !abState;
                    set(abState);
                    abSwitchedAt = Time.realtimeSinceStartup;
                    abSkip = 3;
                }
                yield return null;
            }
            abOn = false;
            set(false);
            PerfProbe.Line($"  {what,-28} {Avg(abMsOn),5:0.00} -> {Avg(abMsOff),5:0.00} (p50 {Pct(abMsOn, 50),5:0.0} -> {Pct(abMsOff, 50),5:0.0}; p95 {Pct(abMsOn, 95),5:0.0} -> {Pct(abMsOff, 95),5:0.0})   " +
                           $"{Avg(abCamOn),5:0.00} -> {Avg(abCamOff),5:0.00}   {Avg(abExtraOn),5:0.00} -> {Avg(abExtraOff),5:0.00}   [{abMsOn.Count}/{abMsOff.Count} frames]" +
                           (abGpuOn.Count > 0 && abGpuOff.Count > 0 ? $"   GPU {Avg(abGpuOn):0.00} -> {Avg(abGpuOff):0.00} (p95 {Pct(abGpuOn, 95):0.0} -> {Pct(abGpuOff, 95):0.0})" : "   (no GPU timing)"));
            yield return null;
        }

        // ------------------------------------------------------------------
        //  The editor profiler, read back
        // ------------------------------------------------------------------

        IEnumerator ProfileWindow(string view)
        {
            int frames = Mathf.RoundToInt(PerfProbe.EnvF("PSX_PERF_PROFILE_FRAMES", 240f));
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            for (int i = 0; i < frames; i++) yield return null;
            ProfilerDriver.enabled = false;
            yield return null;
            try { Analyze(view); }
            catch (System.Exception e) { PerfProbe.Line("  profile " + view + " could not be read: " + e.Message); }
            ProfilerDriver.ClearAllFrames();
            yield return null;
        }

        void Analyze(string view)
        {
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var self = new Dictionary<string, double>();
            var gc = new Dictionary<string, double>();
            var calls = new Dictionary<string, double>();
            var hot = new List<(float ms, string path)>();
            var watchSum = new Dictionary<string, double>();
            var watchMax = new Dictionary<string, double>();
            var kids = new List<int>();
            var stack = new Stack<int>();
            int n = 0; double frameSum = 0;
            for (int f = first; f <= last; f++)
            {
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                                                                        HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (v == null || !v.valid) continue;
                    n++;
                    float frameMs = v.frameTimeMs;
                    frameSum += frameMs;
                    stack.Clear();
                    stack.Push(v.GetRootItemID());
                    while (stack.Count > 0)
                    {
                        int id = stack.Pop();
                        string parent = v.GetItemName(id);
                        kids.Clear();
                        v.GetItemChildren(id, kids);
                        foreach (int k in kids)
                        {
                            string nm = v.GetItemName(k);
                            float st = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnSelfTime);
                            float cl = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnCalls);
                            // an allocation is a GC.Alloc sample under whatever
                            // allocated: charged to that marker
                            if (nm == "GC.Alloc")
                                Add(gc, parent, v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnGcMemory));
                            if (Watched(nm))
                            {
                                float tt = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
                                Add(watchSum, nm, tt);
                                watchMax.TryGetValue(nm, out double wm);
                                if (tt > wm) watchMax[nm] = tt;
                            }
                            Add(self, nm, st);
                            Add(calls, nm, cl);
                            stack.Push(k);
                        }
                    }
                    if (frameMs > 25f) hot.Add((frameMs, HotPath(v)));
                }
            }
            PerfProbe.Line("");
            PerfProbe.Line($"PROFILE {view}: {n} frames, avg {(n > 0 ? frameSum / n : 0):0.00} ms (profiler on; main thread)");
            PerfProbe.Line("  self ms a frame, top 30:");
            foreach (var kv in Top(self, 30)) PerfProbe.Line($"    {kv.Value / Mathf.Max(1, n),7:0.000}  {kv.Key}  ({calls[kv.Key] / Mathf.Max(1, n):0.#} calls)");
            PerfProbe.Line("  GC bytes a frame by the marker that allocated, top 20:");
            foreach (var kv in Top(gc, 20)) if (kv.Value > 0) PerfProbe.Line($"    {kv.Value / Mathf.Max(1, n) / 1024.0,8:0.00} KB  {kv.Key}");
            PerfProbe.Line("  watched markers, total ms a frame (avg / worst frame):");
            foreach (var kv in Top(watchSum, 40))
                PerfProbe.Line($"    {kv.Value / Mathf.Max(1, n),7:0.000} / {watchMax[kv.Key],6:0.0}  {kv.Key}");
            hot.Sort((a, b) => b.ms.CompareTo(a.ms));
            PerfProbe.Line($"  frames over 25 ms: {hot.Count}; the slowest, hot path:");
            for (int i = 0; i < Mathf.Min(8, hot.Count); i++) PerfProbe.Line($"    {hot[i].ms,6:0.0} ms  {hot[i].path}");
        }

        static string HotPath(HierarchyFrameDataView v)
        {
            var sb = new StringBuilder();
            var kids = new List<int>();
            int id = v.GetRootItemID();
            for (int depth = 0; depth < 12; depth++)
            {
                kids.Clear();
                v.GetItemChildren(id, kids);
                if (kids.Count == 0) break;
                int best = -1; float bestMs = 0f, second = 0f; string secondName = "";
                foreach (int k in kids)
                {
                    float t = v.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
                    if (t > bestMs) { second = bestMs; secondName = best >= 0 ? v.GetItemName(best) : ""; bestMs = t; best = k; }
                    else if (t > second) { second = t; secondName = v.GetItemName(k); }
                }
                if (best < 0 || bestMs < 1f) break;
                if (sb.Length > 0) sb.Append(" > ");
                sb.Append(v.GetItemName(best)).Append(' ').Append(bestMs.ToString("0.0"));
                if (second >= 3f) sb.Append(" [also ").Append(secondName).Append(' ').Append(second.ToString("0.0")).Append(']');
                id = best;
            }
            return sb.ToString();
        }

        static readonly string[] WatchPrefixes = { "CityWorld.", "CityFar.", "CitySkyline.", "CityProxies.", "StreetLights.", "CockpitArms.", "CockpitInterior.", "Inl_RenderPipeline.BeginCameraRendering",
                                                   "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "GC.Collect", "Physics.BakePhysXCollisionMeshData" };

        static bool Watched(string name)
        {
            foreach (var p in WatchPrefixes) if (name.StartsWith(p)) return true;
            return name.EndsWith("[Invoke]") && (name.Contains("CityWorld") || name.Contains("Cockpit") || name.Contains("CarReflections") || name.Contains("DoorMirror"));
        }

        static void Add(Dictionary<string, double> d, string k, double v)
        {
            d.TryGetValue(k, out double a);
            d[k] = a + v;
        }

        static List<KeyValuePair<string, double>> Top(Dictionary<string, double> d, int n)
        {
            var l = new List<KeyValuePair<string, double>>(d);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        // ------------------------------------------------------------------
        //  The report
        // ------------------------------------------------------------------

        void Report(Phase p)
        {
            int n = p.ms.Count;
            float secs = Time.realtimeSinceStartup - p.t0;
            int over33 = 0, over50 = 0;
            foreach (float m in p.ms) { if (m > SpikeMs) over33++; if (m > BigSpikeMs) over50++; }
            var gcPos = new List<float>();
            foreach (float k in p.gcKB) if (k >= 0f) gcPos.Add(k);
            PerfProbe.Line("");
            PerfProbe.Line($"{p.name}: {n} frames in {secs:0.0} s, {(n > 0 ? p.speedSum / n * 2.23694f : 0):0} mph avg, {p.distM / 1000f:0.00} km");
            PerfProbe.Line($"  frame ms  avg {Avg(p.ms):0.00}  p50 {Pct(p.ms, 50):0.0}  p95 {Pct(p.ms, 95):0.0}  p99 {Pct(p.ms, 99):0.0}  max {Pct(p.ms, 100):0.0}   " +
                           $"over 33 ms: {over33}  over 50 ms: {over50}   ({(n > 0 ? 1000f / Avg(p.ms) : 0):0} fps)");
            double gcSumKB = 0; foreach (float k in gcPos) gcSumKB += k;
            PerfProbe.Line($"  GC  avg {Avg(gcPos):0.0} KB a frame  p95 {Pct(gcPos, 95):0.0}  max {Pct(gcPos, 100):0.0}; {gcSumKB / 1024.0 / Mathf.Max(0.01f, p.distM / 1000f):0} MB a km driven; {p.collections} collections ({p.framesWithCollect} frames); " +
                           $"heap {p.heap0 / 1048576f:0} -> {p.heap1 / 1048576f:0} MB");
            PerfProbe.Line($"  draws avg {AvgI(p.draws):0}  SetPass avg {AvgI(p.setPass):0}  batches avg {AvgI(p.batches):0}" +
                           (p.gpuMs.Count > 0 ? $"  GPU avg {Avg(p.gpuMs):0.00} ms p95 {Pct(p.gpuMs, 95):0.0}" : "  (no GPU timing)"));
            PerfProbe.Line($"  cameras (CPU ms, URP begin..end): main avg {Avg(p.mainCamMs):0.00} p95 {Pct(p.mainCamMs, 95):0.0}; extra avg {Avg(p.extraCamMs):0.00} p95 {Pct(p.extraCamMs, 95):0.0}");
            var cams = new List<KeyValuePair<string, CamAgg>>(p.cams);
            cams.Sort((a, b) => b.Value.ms.CompareTo(a.Value.ms));
            foreach (var kv in cams)
                PerfProbe.Line($"    {kv.Key,-26} rendered on {100f * kv.Value.frames / Mathf.Max(1, n),5:0}% of frames ({kv.Value.renders} renders), " +
                               $"{kv.Value.ms / Mathf.Max(1, kv.Value.renders),6:0.00} ms a render, {kv.Value.ms / Mathf.Max(1, n),6:0.00} ms a frame, worst {kv.Value.maxMs:0.0}");
            var w = CityWorld.Active;
            PerfProbe.Line($"  city: {(w != null ? w.LiveTiles + " tiles, " + w.LiveColliders + " colliders" : "no world")}; {p.tileEvents} tile/tree completions on {p.tileFrames} frames, worst {p.tileFrameMax:0.0} ms");
            if (p.spikes.Count > 0)
            {
                PerfProbe.Line($"  frames over {SpikeMs:0} ms (first {p.spikes.Count}):");
                foreach (var s in p.spikes) PerfProbe.Line(s);
            }
        }

        void ReportBigSteps()
        {
            var map = CityWorld.Active != null ? CityWorld.Active.Map : null;
            var l = new List<(int phase, int item, int part, float ms, float missMs)>(bigSteps);
            l.Sort((x, y) => y.ms.CompareTo(x.ms));
            PerfProbe.Line("");
            PerfProbe.Line($"TILE BUILDER: {l.Count} steps over 8 ms in the whole run; the longest:");
            string[] roadParts = { "sections", "side flags", "spans", "lamps" };
            var byPhase = new Dictionary<string, (int n, float ms)>();
            foreach (var bs in l)
            {
                string what = bs.part == 4 ? "sectioning ahead" : bs.part == 5 ? "a neighbour's sections" : bs.part == 10 ? "side flags: setup + structure ends" : bs.part == 11 ? "side flags: spans"
                            : bs.part == 12 ? "side flags: runs + flares" : bs.part == 20 ? "branch: chains" : bs.part == 21 ? "branch: stations"
                            : bs.part == 22 ? "branch: clips" : bs.phase == 2 ? roadParts[Mathf.Clamp(bs.part, 0, 3)] : bs.phase == 9 ? "item " + bs.item : "";
                string key = CityMeshes.PhaseNames[Mathf.Clamp(bs.phase, 0, CityMeshes.PhaseNames.Length - 1)] + " " + what;
                byPhase.TryGetValue(key, out var a);
                byPhase[key] = (a.n + 1, a.ms + bs.ms);
            }
            for (int i = 0; i < Mathf.Min(15, l.Count); i++)
            {
                var bs = l[i];
                string edge = (bs.phase == 1 || bs.phase == 2) && map != null && bs.item >= 0 && bs.item < map.edges.Length ? $" (edge {bs.item}, way {map.edges[bs.item].wayId})" : $" (item {bs.item})";
                PerfProbe.Line($"    {bs.ms,6:0.0} ms  phase {CityMeshes.PhaseNames[Mathf.Clamp(bs.phase, 0, CityMeshes.PhaseNames.Length - 1)]} part {bs.part}{edge}");
            }
            var groups = new List<KeyValuePair<string, (int n, float ms)>>(byPhase);
            groups.Sort((x, y) => y.Value.ms.CompareTo(x.Value.ms));
            PerfProbe.Line("  by kind (count, total ms):");
            foreach (var g in groups) PerfProbe.Line($"    {g.Value.n,4} x  {g.Value.ms,7:0}  {g.Key}");
        }

        static float Avg(List<float> v)
        {
            if (v.Count == 0) return 0f;
            double s = 0; foreach (float x in v) s += x;
            return (float)(s / v.Count);
        }

        static float AvgI(List<int> v)
        {
            if (v.Count == 0) return 0f;
            double s = 0; foreach (int x in v) s += x;
            return (float)(s / v.Count);
        }

        static float Pct(List<float> v, float pct)
        {
            if (v.Count == 0) return 0f;
            var a = new List<float>(v); a.Sort();
            int i = Mathf.Clamp(Mathf.CeilToInt(pct / 100f * a.Count) - 1, 0, a.Count - 1);
            return a[i];
        }
    }
}
