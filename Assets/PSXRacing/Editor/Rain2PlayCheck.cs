using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// RAIN 2 PLAYED (tools: scratch rain_job / tools\rain2-play-check.ps1):
    /// free roam in Charlotte in the rain, the player's car on I-77 south with
    /// the race AI at the wheel, through the three states the owner named -
    /// SITTING IDLE, CRUISING (45 mph) and RACING (flat out, 100 mph and up) -
    /// in the chase view and the cockpit. It takes a frame of the game's own
    /// camera in each (a render request: no point in the frame to wait for,
    /// so it runs hidden as well as watched), counts what the rain did
    /// (splashes alive, ground anchors, wiper strokes, puddle hits, the sound's
    /// gains) and meters what it cost on the CPU: the time and the managed
    /// allocation of its own three per-frame parts.
    ///
    /// One process plays ONE side: PSX_RAIN2=0 is the rain as it was. Frames
    /// to PSX_RAIN2_OUT\&lt;tag&gt;\play_*.png, the report to
    /// PSXRacing_rain2_play.txt at the project root. PSX_RAIN2_HOUR picks the
    /// hour (Night).
    /// </summary>
    public static class Rain2PlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            int roam = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = roam >= 0 ? TrackCatalog.SceneIndex(roam) : -1;
            if (roam < 0 || s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path))
            {
                Fail("Charlotte: scene not built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.TrackIndex = roam;
            var cars = CarCatalog.All;
            if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.TimeOfDayIndex = HourIndex(Env("PSX_RAIN2_HOUR", "Night"));
            RaceHandoff.WeatherOverride = (int)Weather.Rain;

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static int HourIndex(string name)
        {
            for (int k = 0; k < TimeOfDay.All.Length; k++)
                if (string.Equals(TimeOfDay.All[k].name, name, System.StringComparison.OrdinalIgnoreCase)) return k;
            return TimeOfDay.Night;
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("Rain2PlayCheckRunner").AddComponent<Rain2PlayCheckRunner>();
        }

        internal static void Line(string s) => log.AppendLine(s);
        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : ""));
        }
        internal static void Fail(string what) { failures++; log.AppendLine("  FAIL " + what); }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "RAIN2 PLAY OK." : failures + " FAILURE(S).");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_rain2_play.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    // Last of all in LateUpdate: a frame is taken AFTER the chase camera has
    // mounted its eye for this frame. (Taken from a coroutine it was the last
    // frame's eye on this frame's car: at 45 mph the cockpit camera sat a
    // metre behind its seat.)
    [DefaultExecutionOrder(32000)]
    public class Rain2PlayCheckRunner : MonoBehaviour
    {
        string pendingShot;
        void LateUpdate()
        {
            if (pendingShot == null) return;
            string name = pendingShot;
            pendingShot = null;
            TakeShot(name);
        }

        const float HangSeconds = 420f;
        float bornAt;
        bool finished;
        string stage = "start";
        string outDir;
        int W = 1920, H = 1080;

        void Awake() { bornAt = Time.realtimeSinceStartup; }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true;
            Rain2PlayCheck.Fail("HUNG: no end after " + HangSeconds.ToString("0") + " s of play, stuck in " + stage);
            Rain2PlayCheck.Finish();
            EditorApplication.Exit(1);
        }

        static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }

        // one state's frame times
        readonly List<float> ms = new List<float>(4096);
        void Frame() { ms.Add(Time.unscaledDeltaTime * 1000f); }
        string Stats()
        {
            if (ms.Count == 0) return "no frames";
            var v = new List<float>(ms);
            v.Sort();
            float sum = 0f; foreach (float x in v) sum += x;
            return $"{v.Count} frames avg {sum / v.Count:0.0} ms p50 {v[v.Count / 2]:0.0} p95 {v[(int)(v.Count * 0.95f)]:0.0} worst {v[v.Count - 1]:0.0}";
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            string tag = Env("PSX_RAIN2_TAG", Rain2.On ? "on" : "off");
            outDir = System.IO.Path.Combine(Env("PSX_RAIN2_OUT", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots", "rain2")), tag);
            System.IO.Directory.CreateDirectory(outDir);
            int.TryParse(Env("PSX_RAIN2_W", "1920"), out W); int.TryParse(Env("PSX_RAIN2_H", "1080"), out H);
            Rain2PlayCheck.Line($"RAIN2 PLAY {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag} Rain2.On {Rain2.On} tier {Rain2.CurrentTier} batch {Application.isBatchMode} gpu {SystemInfo.graphicsDeviceName}");

            // ---- free roam up
            stage = "free roam coming up";
            float t0 = Time.realtimeSinceStartup;
            CityMode mode = null;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                mode = CityMode.Instance;
                if (mode != null && mode.player != null && mode.world != null && mode.world.Map != null && mode.Live) break;
                yield return null;
            }
            if (mode == null || mode.player == null || mode.world == null || !mode.Live)
            {
                Rain2PlayCheck.Fail("free roam never came up");
                Done();
                yield break;
            }
            var world = mode.world;
            var map = world.Map;
            var car = mode.player;
            Rain2PlayCheck.Line($"hour {TimeOfDay.All[RaceHandoff.TimeOfDayIndex].name} weather {Seasons.CurrentWeather} precip {Seasons.Now.precip} rate {Seasons.Now.precipRate:0.00} " +
                                $"road water {Seasons.Now.road.water:0.00}");
            Rain2PlayCheck.Check(Seasons.Now.precip == PrecipKind.Rain, "it is raining in this free roam");

            // ---- to I-77 south, the race AI at the wheel, through the traffic
            stage = "the route";
            var route = PerfTour.RouteFor(map, Env("PSX_RAIN2_ROUTE", "i77s"));
            if (route == null || route.edges == null || route.edges.Length == 0) { Rain2PlayCheck.Fail("no route"); Done(); yield break; }
            var path = new GameObject("Rain2PlayPath").AddComponent<TrackPath>();
            CityMode.BuildPath(map, route, path);
            int i0 = route.loop ? 0 : 2;
            Vector3 tan = path.GetTangent(i0); tan.y = 0f; tan.Normalize();
            var pos = path.GetPoint(i0) + Vector3.up * 0.45f;
            var rot = Quaternion.LookRotation(tan, Vector3.up);
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var old = car.GetComponent<AIDriver>();
            if (old != null) Destroy(old);
            yield return null;
            world.EnsureRing(pos, 1);
            car.ResetTo(pos, rot, seated: true);
            float settle0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle0 < 60f)
            {
                if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - settle0 > 6f) break;
                car.ResetTo(pos, rot, seated: true);
                yield return null;
            }
            foreach (var col in car.GetComponentsInChildren<Collider>(true))
                if (col.gameObject.layer != 2) Physics.IgnoreLayerCollision(col.gameObject.layer, 2, true);
            car.allowReverse = false;
            var ai = car.gameObject.AddComponent<AIDriver>();
            ai.path = path;
            ai.skill = 0.95f;
            ai.ReseedPath();
            ai.speedCapMps = 0f;
            ai.driving = false;
            var heat = car.GetComponent<EngineTemp>();
            if (heat != null) { heat.StartCold(heat.ambientC); heat.TopUp(100f); }

            Rain2.Measure = true;
            Rain2.MeterReset();
            // the meter must be able to SEE an allocation, or its zero means nothing
            long m0 = Rain2.AllocNow();
            var probe = new byte[256 * 1024];
            long seen = Rain2.AllocSince(m0);
            probe[0] = 1;
            Rain2PlayCheck.Line($"alloc meter: a 256 KB array reads as {seen} B ({(System.GC.GetAllocatedBytesForCurrentThread() != 0 ? "the thread's own count" : "the heap in use")})");
            Rain2PlayCheck.Check(seen >= 128 * 1024, "the allocation meter sees an allocation", seen);
            int gc0 = System.GC.CollectionCount(0);
            int strokes0 = CockpitInterior.WiperStrokes;
            int maxSplash = 0, maxAnchors = 0;
            Vector3 gains = Vector3.zero;

            // ---- 1: SITTING IDLE (the wipers park five seconds after the car stops)
            stage = "idle";
            ms.Clear();
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            for (float t = 0f; t < 7f; t += Time.unscaledDeltaTime)
            {
                Frame();
                maxSplash = Mathf.Max(maxSplash, Rain2Driver.LiveSplashes); maxAnchors = Mathf.Max(maxAnchors, Rain2Driver.Anchors);
                yield return null;
            }
            yield return Shot("idle_chase");
            int idleSplash = maxSplash;
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) { Frame(); gains = Vector3.Max(gains, Rain2Audio.Gains); yield return null; }
            yield return Shot("idle_cockpit");
            int idleStrokes = CockpitInterior.WiperStrokes - strokes0;
            Rain2PlayCheck.Line($"IDLE: {Stats()}; splashes alive (max) {idleSplash}, anchors {maxAnchors}, covered cells {Rain2Driver.CoveredCells}; " +
                                $"wiper strokes while parked {idleStrokes}; _PSXRain2 {Shader.GetGlobalVector("_PSXRain2")}");

            // ---- 2: CRUISING, 45 mph
            stage = "cruise";
            ms.Clear();
            ai.driving = true;
            ai.speedCapMps = 20f;
            int strokesC0 = CockpitInterior.WiperStrokes;
            float top = 0f;
            for (float t = 0f; t < 12f; t += Time.unscaledDeltaTime)
            {
                Frame();
                top = Mathf.Max(top, car.speedKmh);
                gains = Vector3.Max(gains, Rain2Audio.Gains);
                yield return null;
            }
            yield return Shot("cruise_cockpit");
            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime) { Frame(); top = Mathf.Max(top, car.speedKmh); yield return null; }
            yield return Shot("cruise_chase");
            // a wheel into standing water, from the chase camera: the frame after the next puddle hit
            // (PSX_RAIN2=0 has no pools to hit: its frame is taken when the wait runs out)
            {
                var sp = car.GetComponent<TireSpray>();
                int h0 = sp != null ? sp.PoolHits : 0;
                for (float t = 0f; t < 6f; t += Time.unscaledDeltaTime)
                {
                    Frame();
                    if (sp == null) sp = car.GetComponent<TireSpray>();
                    if (sp != null && sp.PoolHits > h0 && t > 1f) break;
                    yield return null;
                }
                for (int i = 0; i < 4; i++) yield return null;
                yield return Shot("puddle_chase");
            }
            int cruiseStrokes = CockpitInterior.WiperStrokes - strokesC0;
            Rain2PlayCheck.Line($"CRUISE: {Stats()}; top {top / 1.609f:0} mph; wiper strokes {cruiseStrokes} in 16 s; splashes alive now {Rain2Driver.LiveSplashes}");

            // ---- 3: RACING, flat out
            stage = "race";
            ms.Clear();
            ai.speedCapMps = float.PositiveInfinity;
            top = 0f;
            float raceT = 0f;
            bool shotChase = false;
            while (raceT < 34f)
            {
                raceT += Time.unscaledDeltaTime;
                Frame();
                top = Mathf.Max(top, car.speedKmh);
                gains = Vector3.Max(gains, Rain2Audio.Gains);
                if (!shotChase && (car.speedKmh > 170f || raceT > 22f))
                {
                    shotChase = true;
                    yield return Shot("race_chase");
                    ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                    for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime) { raceT += Time.unscaledDeltaTime; Frame(); top = Mathf.Max(top, car.speedKmh); yield return null; }
                    yield return Shot("race_cockpit");
                    break;
                }
                yield return null;
            }
            Rain2PlayCheck.Line($"RACE: {Stats()}; top {top / 1.609f:0} mph after {raceT:0.0} s; splashes alive now {Rain2Driver.LiveSplashes}; " +
                                $"traffic cars known {Rain2Traffic.Cars}, their mist puffs alive {Rain2Traffic.Live}");

            // ---- what it did, what it cost
            var spray = car.GetComponent<TireSpray>();
            int hits = spray != null ? spray.PoolHits : 0;
            var trail = car.GetComponent<WetTrail>();
            int laid = trail != null ? trail.Laid : 0;
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int frames = Mathf.Max(1, Rain2.DriverFrames);
            Rain2PlayCheck.Line($"COUNTS: damp-trail pieces laid by the player's tyres {laid}; puddle hits by the player's wheels {hits}; wiper strokes total {CockpitInterior.WiperStrokes - strokes0}; " +
                                $"audio gains max (road, roof, hiss) {gains.x:0.00} {gains.y:0.00} {gains.z:0.00}");
            Rain2PlayCheck.Line($"CPU (this run's own meter, {frames} frames): driver {Rain2.DriverTicks * toMs / frames:0.000} ms/frame avg, worst {Rain2.DriverMaxTicks * toMs:0.000} ms, " +
                                $"alloc {Rain2.DriverAlloc} B; audio {Rain2.AudioTicks * toMs / frames:0.000} ms/frame, alloc {Rain2.AudioAlloc} B; " +
                                $"tyre spray (all {(frames > 0 ? Rain2.SprayCalls / frames : 0)} cars) {Rain2.SprayTicks * toMs / frames:0.000} ms/frame, worst one car {Rain2.SprayMaxTicks * toMs:0.000} ms, alloc {Rain2.SprayAlloc} B; " +
                                $"gen-0 collections in the run {System.GC.CollectionCount(0) - gc0}");
            if (Rain2.On)
            {
                Rain2PlayCheck.Check(idleSplash > 20, "parked in the rain: drops land round the camera", idleSplash);
                Rain2PlayCheck.Check(maxAnchors > 20, "the ground round the camera was probed", maxAnchors);
                Rain2PlayCheck.Check(idleStrokes <= 3, "the wipers park when the car stands (the screen beads over)", idleStrokes);
                Rain2PlayCheck.Check(cruiseStrokes >= 3, "the wipers sweep while it is driven in the rain", cruiseStrokes);
                Rain2PlayCheck.Check(gains.x > 0.01f || gains.y > 0.01f, "the rain is heard", gains);
                Rain2PlayCheck.Check(gains.z > 0.01f, "the tyres hiss on the wet road at speed", gains.z);
                Rain2PlayCheck.Check(laid > 20, "the tyres leave a damp trail", laid);
                Rain2PlayCheck.Check(hits > 0, "a wheel ran through standing water (and threw its burst)", hits);
                Rain2PlayCheck.Check(Rain2.DriverAlloc == 0 && Rain2.AudioAlloc <= 4_000_000, "the driver allocates nothing per frame; the audio only its clips, once",
                                     Rain2.DriverAlloc + " / " + Rain2.AudioAlloc);
                Rain2PlayCheck.Check(Rain2.DriverMaxTicks * toMs < 2.0, "no CPU spike from the driver (worst frame under 2 ms)", (Rain2.DriverMaxTicks * toMs).ToString("0.000"));
            }
            else
            {
                Rain2PlayCheck.Check(Rain2Driver.LiveSplashes == 0 && CockpitInterior.WiperStrokes == strokes0 && gains == Vector3.zero,
                                     "PSX_RAIN2=0: no splashes, no wipers, no rain sound (the rain as it was)");
            }
            Rain2.Measure = false;
            Done();
        }

        void Done()
        {
            finished = true;
            Rain2PlayCheck.Finish();
            EditorApplication.Exit(Rain2PlayCheck.failures == 0 ? 0 : 1);
        }

        /// <summary>The game's own camera, as it stands this frame, through a
        /// render request (never WaitForEndOfFrame: a hidden run has no frame end).</summary>
        IEnumerator Shot(string name)
        {
            pendingShot = name;
            while (pendingShot != null) yield return null;
        }

        void TakeShot(string name)
        {
            string path = System.IO.Path.Combine(outDir, "play_" + name + ".png");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            var cam = Camera.main;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || cam == null)
            {
                Rain2PlayCheck.Line("  shot " + name + " not taken (" + (cam == null ? "no main camera" : "no graphics device") + ")");
                return;
            }
            var target = cam.targetTexture;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); DestroyImmediate(shown); }
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                DestroyImmediate(tex);
                var car = CityMode.Instance != null ? CityMode.Instance.player : null;
                Rain2PlayCheck.Line($"  shot play_{name}.png  {(car != null ? (car.speedKmh / 1.609f).ToString("0") + " mph" : "")} view {ChaseCamera.Current} " +
                                    $"splashes {Rain2Driver.LiveSplashes} wipe {Shader.GetGlobalVector("_CabinWipe")}");
            }
            cam.targetTexture = target;
            rt.Release();
            DestroyImmediate(rt);
        }
    }
}
