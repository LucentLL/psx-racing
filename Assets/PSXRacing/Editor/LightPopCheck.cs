using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LIGHT-POP CHECK (tools\lightpop-check.ps1; the lightpop round,
    /// 2026-11-22): night drives at speed with the light-pop meter
    /// (Scripts/Dev/LightPopMeter.cs) counting every light that switches.
    ///
    /// PSX_LIGHTPOP_MODE:
    ///   city  - free roam in Charlotte at night, the player's car on the race AI
    ///           down each of PSX_LIGHTPOP_ROUTES (PerfTour.RouteFor ids), once in
    ///           each of PSX_LIGHTPOP_VIEWS, PSX_LIGHTPOP_SECS game seconds each;
    ///   gate  - (roadevents, 2026-11-26) the race start gates in Charlotte's
    ///           free roam (played as the CITY edition, where they stand): for
    ///           each gate a scripted approach up its own street from
    ///           PSX_GATE_FROMM (620) metres at PSX_GATE_KMH (60), the car set
    ///           down every frame along the road and the eye behind it, metered
    ///           until the gate is PSX_GATE_TOM (250) metres off. PSX_GATE_LIST:
    ///           gate indices (default all, at most PSX_GATE_MAX = 12).
    ///           PSX_GATE_SHOTS=1: on gate PSX_GATE_SHOTGATE (default the
    ///           first) look frames (LightLookShots) at 500/450/400/350/300 m
    ///           and every 0.1 s from 470 to 430 m. PSX_GATEFADE=0: the old
    ///           switch at 450 m. Writes gate_trace.csv (eye metres, root on,
    ///           light) beside the summary. PSX_GATE_AB=1: then the cost A/B,
    ///           stood PSX_GATE_ABM (380) metres short of gate PSX_GATE_ABGATE
    ///           (the shot gate): the main view drawn at 3832x2054 each frame
    ///           and waited for, in rotating blocks of the fade NEW, OLD and NEW
    ///           again (the null A/A column), PSX_GATE_ABREPS (8) blocks of 60
    ///           frames each; PSX_GATE_ABONLY=1 skips the approaches;
    ///   race  - a race at night at PSX_LIGHTPOP_VENUE with three rivals, the
    ///           player on autopilot: PSX_LIGHTPOP_SECS in the chase view, the same
    ///           in the cockpit; then SAVE TO TAPE and the replay (the tape's fixed
    ///           cameras) for PSX_LIGHTPOP_REPLAY seconds, every cut counted.
    /// The game runs at a fixed 30 frames a game second (Time.captureFramerate),
    /// so a run is the same drive however slow the machine is. One process plays
    /// ONE side: PSX_LIGHT_NOPOP=0 is today's lights. Output: PSX_LIGHTPOP_OUT
    /// (summary.txt, pops.csv, a folder per segment with the worst frame pairs,
    /// strip frames and the first frames after each replay cut).
    /// </summary>
    public static class LightPopCheck
    {
        internal static StringBuilder log = new StringBuilder();
        internal static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
        internal static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            TitleScreen.Skip = true;
            ToolWatchdog.Arm("LightPopCheck", 50f, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_lightpop.txt"));
            string mode = Env("PSX_LIGHTPOP_MODE", "city");
            string venue = mode == "race" ? Env("PSX_LIGHTPOP_VENUE", "GillespieGap") : "Charlotte";
            if (!TrackCatalog.TryIndexOf(venue, out int v)) { Line("FAIL no venue " + venue); Finish(1); return; }
            int s = TrackCatalog.SceneIndex(v);
            var scenes = EditorBuildSettings.scenes;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Line("FAIL scene not built: " + venue); Finish(1); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = v;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Night;
            // (parkwaytunnel) PSX_LIGHTPOP_HOUR: an hour index 0-6 (default Night)
            if (int.TryParse(Env("PSX_LIGHTPOP_HOUR", ""), out int hourIx)) RaceHandoff.TimeOfDayIndex = Mathf.Clamp(hourIx, 0, TimeOfDay.Count - 1);
            // (lightlook) PSX_LIGHTPOP_WEATHER: Rain etc. (default Clear)
            RaceHandoff.WeatherOverride = System.Enum.TryParse(Env("PSX_LIGHTPOP_WEATHER", "Clear"), true, out Weather wx) ? (int)wx : (int)Weather.Clear;
            RaceHandoff.StartFuelPct = 100f;
            RaceHandoff.CalendarDay = 320;
            // (tunnel2) PSX_LIGHTPOP_DAY: the calendar day (the PC60 tour ran day 290, the fall dress)
            if (int.TryParse(Env("PSX_LIGHTPOP_DAY", ""), out int dayIx) && dayIx > 0) RaceHandoff.CalendarDay = dayIx;
            var cars = CarCatalog.All;
            if (mode == "race")
            {
                if (cars.Count > 4)
                {
                    RaceHandoff.CarSpecId = cars[0].id;
                    RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                    RaceHandoff.OpponentSkills = "1.0;0.98;0.96";
                }
                string tapes = Path.Combine(ProjectDir, "LightPopTapes");
                try { if (Directory.Exists(tapes)) Directory.Delete(tapes, true); } catch (System.Exception) { }
                Tapes.DirOverride = tapes;
            }
            else
            {
                RaceHandoff.FreeRoam = true;
                if (cars.Count > 0) RaceHandoff.CarSpecId = cars[0].id;
                // (roadevents) the gates stand outside the career only (CityRaceStarts.Enabled)
                if (mode == "gate") Edition.Simulate(EditionKind.City);
            }
            LightPopMeter.OutDir = Env("PSX_LIGHTPOP_OUT", Path.Combine(ProjectDir, "LightPop", StreetLights.NoPop ? "after" : "before"));
            try { if (Directory.Exists(LightPopMeter.OutDir)) Directory.Delete(LightPopMeter.OutDir, true); } catch (System.Exception) { }
            Directory.CreateDirectory(LightPopMeter.OutDir);
            Line("LIGHTPOP " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " mode " + mode + " venue " + venue +
                 " NoPop " + StreetLights.NoPop + (Application.isBatchMode ? " (hidden)" : " (watched)"));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("LightPopCheckRunner").AddComponent<LightPopCheckRunner>();
        }

        internal static void Line(string s) { log.AppendLine(s); Debug.Log("[LightPop] " + s); }

        internal static void Finish(int code)
        {
            Tapes.DirOverride = null;
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            var path = Path.Combine(ProjectDir, "PSXRacing_lightpop.txt");
            File.WriteAllText(path, log.ToString());
            if (LightPopMeter.OutDir != null)
                try { File.WriteAllText(Path.Combine(LightPopMeter.OutDir, "run.txt"), log.ToString()); } catch (System.Exception) { }
            EditorApplication.Exit(code);
        }
    }

    public class LightPopCheckRunner : MonoBehaviour
    {
        const float HangSeconds = 3000f;
        float bornAt;
        string stage = "start";

        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }

        void Update()
        {
            if (Application.isBatchMode) AudioListener.pause = true;
            if (Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            LightPopCheck.Line("FAIL HUNG in " + stage);
            LightPopCheck.Finish(2);
            enabled = false;
        }

        static float EnvF(string n, float d) =>
            float.TryParse(LightPopCheck.Env(n, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : d;

        IEnumerator Start()
        {
            yield return null;
            var meter = LightPopMeter.Ensure();
            string mode = LightPopCheck.Env("PSX_LIGHTPOP_MODE", "city");
            if (mode == "race") yield return Race(meter);
            else if (mode == "gate") yield return GateApproach(meter);
            else yield return City(meter);
            LightPopCheck.Line("--- summary ---");
            LightPopCheck.Line(meter.Report);
            LightPopCheck.Line("LIGHTPOP DONE.");
            LightPopCheck.Finish(0);
        }

        // ------------------------------------------------------------------
        IEnumerator City(LightPopMeter meter)
        {
            stage = "free roam coming up";
            float t0 = Time.realtimeSinceStartup;
            CityMode mode = null;
            while (Time.realtimeSinceStartup - t0 < 240f)
            {
                mode = CityMode.Instance;
                if (mode != null && mode.player != null && mode.world != null && mode.world.Map != null && mode.Live) break;
                yield return null;
            }
            if (mode == null || mode.player == null || !mode.Live) { LightPopCheck.Line("FAIL free roam never came up"); yield break; }
            var world = mode.world; var map = world.Map; var car = mode.player;
            LightPopCheck.Line("free roam up in " + (Time.realtimeSinceStartup - t0).ToString("0") + " s; hour " +
                               TimeOfDay.All[RaceHandoff.TimeOfDayIndex].name + " weather " + Seasons.CurrentWeather + " street lamps on " + StreetLights.StreetOn);
            float secs = EnvF("PSX_LIGHTPOP_SECS", 40f);
            int strip = Mathf.RoundToInt(EnvF("PSX_LIGHTPOP_STRIP", 15f));
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            foreach (var col in car.GetComponentsInChildren<Collider>(true))
                if (col.gameObject.layer != 2) Physics.IgnoreLayerCollision(col.gameObject.layer, 2, true);
            car.allowReverse = false;
            foreach (var id in LightPopCheck.Env("PSX_LIGHTPOP_ROUTES", "tryon,uptown,i77s,ctr_prov").Split(','))
            {
                string rid = id.Trim();
                if (rid.Length == 0) continue;
                var route = PerfTour.RouteFor(map, rid);
                if (route == null || route.edges == null || route.edges.Length == 0) { LightPopCheck.Line("FAIL no route " + rid); continue; }
                var path = new GameObject("LightPopPath_" + rid).AddComponent<TrackPath>();
                CityMode.BuildPath(map, route, path);
                foreach (var vn in LightPopCheck.Env("PSX_LIGHTPOP_VIEWS", "chase,cockpit").Split(','))
                {
                    if (!System.Enum.TryParse(vn.Trim(), true, out ChaseCamera.View view)) continue;
                    stage = rid + " " + view;
                    // to the start, the ring stood up, the race AI at the wheel
                    var old = car.GetComponent<AIDriver>();
                    if (old != null) { Destroy(old); yield return null; }
                    int i0 = route.loop ? 0 : 2;
                    Vector3 tan = path.GetTangent(i0); tan.y = 0f; tan.Normalize();
                    var pos = path.GetPoint(i0) + Vector3.up * 0.45f;
                    var rot = Quaternion.LookRotation(tan, Vector3.up);
                    Time.captureFramerate = 0;
                    world.EnsureRing(pos, 1);
                    car.ResetTo(pos, rot, seated: true);
                    float s0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - s0 < 90f)
                    {
                        if (world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - s0 > 8f) break;
                        car.ResetTo(pos, rot, seated: true);
                        yield return null;
                    }
                    ChaseCamera.PreviewView(view);
                    var ai = car.gameObject.AddComponent<AIDriver>();
                    ai.path = path; ai.skill = 0.95f; ai.ReseedPath(); ai.speedCapMps = float.PositiveInfinity; ai.driving = true;
                    Random.InitState(4242);
                    Time.captureFramerate = 30;
                    for (int k = 0; k < 20; k++) yield return null;   // the camera settles behind the car
                    string segName = rid + "_" + view.ToString().ToLowerInvariant();
                    // (lightlook) PSX_LIGHTPOP_SHOTS: full-size look frames at these game seconds (LightLookShots);
                    // PSX_LIGHTPOP_NOMETER=1: no pop metering (a look run)
                    bool metering = LightPopCheck.Env("PSX_LIGHTPOP_NOMETER", "0") != "1";
                    var shotAt = new System.Collections.Generic.List<float>();
                    foreach (var st in LightPopCheck.Env("PSX_LIGHTPOP_SHOTS", "").Split(','))
                        if (float.TryParse(st.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sv)) shotAt.Add(sv);
                    int nextShot = 0;
                    if (metering) meter.Begin(segName, car.transform, strip);
                    float g0 = Time.time, top = 0f, slowFor = 0f;
                    int nudges = 0;
                    while (Time.time - g0 < secs)
                    {
                        if (nextShot < shotAt.Count && Time.time - g0 >= shotAt[nextShot])
                        {
                            LightLookShotRunner.Queue(System.IO.Path.Combine(LightPopMeter.OutDir, "shots"), segName + "_" + shotAt[nextShot].ToString("00") + "s"); yield return null;
                            LightPopCheck.Line("shot " + segName + " " + shotAt[nextShot].ToString("0") + " s at " + car.transform.position.ToString("F0") + " " + car.speedKmh.ToString("0") + " km/h; far plane " + (Camera.main != null ? Camera.main.farClipPlane.ToString("0") : "-") + " m, field lamps " + StreetLights.FieldLamps + ", far field lamps " + StreetLights.FarLamps + ", registered " + StreetLights.Registered + ", tunnel lamps " + TunnelLamps.Count);
                            if (LightPopCheck.Env("PSX_LIGHTLOOK_DIAG", "0") == "1") LightPopCheck.Line(StreetLights.DiagnoseNearest(Camera.main, 12));
                            nextShot++;
                        }
                        top = Mathf.Max(top, car.speedKmh);
                        // held up by the traffic or a signal: set on 60 m (a cut, not counted)
                        slowFor = Mathf.Abs(car.forwardSpeed) < 3f ? slowFor + Time.deltaTime : 0f;
                        if (slowFor > 2.5f && ai.PathIndex < path.Count - 40)
                        {
                            int to = Mathf.Min(path.Count - 13, ai.PathIndex + Mathf.CeilToInt(60f / Mathf.Max(0.5f, path.spacing)));
                            Vector3 tn = path.GetTangent(to); tn.y = 0f; tn.Normalize();
                            car.ResetTo(path.GetPoint(to) + Vector3.up * 0.45f, Quaternion.LookRotation(tn, Vector3.up), seated: true);
                            ai.ReseedPath();
                            slowFor = 0f; nudges++;
                        }
                        if (ai.PathIndex >= path.Count - 12) break;
                        yield return null;
                    }
                    string line = metering ? meter.End() : segName + ": look run, no metering";
                    LightPopCheck.Line(line + "\n    top " + top.ToString("0") + " km/h, " + nudges + " nudges past held-up traffic");
                    Time.captureFramerate = 0;
                }
            }
        }

        // ------------------------------------------------------------------
        //  (roadevents) the start gates: a scripted approach to each
        // ------------------------------------------------------------------
        IEnumerator GateApproach(LightPopMeter meter)
        {
            stage = "free roam coming up";
            float t0 = Time.realtimeSinceStartup;
            CityMode mode = null;
            while (Time.realtimeSinceStartup - t0 < 240f)
            {
                mode = CityMode.Instance;
                if (mode != null && mode.player != null && mode.world != null && mode.world.Map != null && mode.Live) break;
                yield return null;
            }
            if (mode == null || mode.player == null || !mode.Live) { LightPopCheck.Line("FAIL free roam never came up"); yield break; }
            var world = mode.world; var map = world.Map; var car = mode.player;
            var gates = CityRaceStarts.Gates;
            LightPopCheck.Line("free roam up in " + (Time.realtimeSinceStartup - t0).ToString("0") + " s; edition " + Edition.Current +
                               "; hour " + TimeOfDay.All[RaceHandoff.TimeOfDayIndex].name + "; gates " + gates.Count +
                               "; fade " + (CityRaceStarts.FadeIn ? "NEW (PSX_GATEFADE on)" : "OLD (PSX_GATEFADE=0: the switch at 450 m)"));
            if (gates.Count == 0) { LightPopCheck.Line("FAIL no gates stand (edition " + Edition.Current + ")"); yield break; }
            float fromM = EnvF("PSX_GATE_FROMM", 620f), kmh = EnvF("PSX_GATE_KMH", 60f), toM = EnvF("PSX_GATE_TOM", 250f);
            int maxGates = Mathf.RoundToInt(EnvF("PSX_GATE_MAX", 12f));
            bool shots = LightPopCheck.Env("PSX_GATE_SHOTS", "0") == "1";
            int shotGate = Mathf.RoundToInt(EnvF("PSX_GATE_SHOTGATE", -1f));
            var list = new System.Collections.Generic.List<int>();
            string ls = LightPopCheck.Env("PSX_GATE_LIST", "all");
            if (ls == "all") for (int i = 0; i < gates.Count; i++) list.Add(i);
            else foreach (var x in ls.Split(',')) if (int.TryParse(x.Trim(), out int gi) && gi >= 0 && gi < gates.Count) list.Add(gi);
            if (list.Count > maxGates) list.RemoveRange(maxGates, list.Count - maxGates);
            for (int i = 0; i < gates.Count; i++)
                LightPopCheck.Line("  gate " + i + ": " + gates[i].name + " (" + TrackCatalog.At(gates[i].venue).id + ") at " +
                                   gates[i].plan.x.ToString("0") + "," + gates[i].plan.y.ToString("0"));

            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var cam = Camera.main;
            var chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            if (chase == null) chase = FindFirstObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = false;
            var trace = new StringBuilder("gate,frame,t,eye_m,root_on,light\n");
            string shotDir = Path.Combine(LightPopMeter.OutDir, "shots");
            int measured = 0;
            bool abOnly = LightPopCheck.Env("PSX_GATE_ABONLY", "0") == "1";
            if (abOnly) list.Clear();
            foreach (int gi in list)
            {
                var g = gates[gi];
                stage = "gate " + gi + " " + g.name;
                var pts = CityRaceStarts.ApproachLine(map, gi, fromM + 20f);
                if (pts.Count < 20) { LightPopCheck.Line("gate " + gi + " " + g.name + ": no road into it (" + pts.Count + " points): skipped"); continue; }
                // arc length along the approach
                var acc = new float[pts.Count];
                for (int i = 1; i < pts.Count; i++) acc[i] = acc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                float L = acc[pts.Count - 1];
                Vector3 Pose(float sAt, out Quaternion rot)
                {
                    sAt = Mathf.Clamp(sAt, 0f, L);
                    int k = System.Array.BinarySearch(acc, sAt);
                    if (k < 0) k = ~k;
                    k = Mathf.Clamp(k, 1, pts.Count - 1);
                    float seg = Mathf.Max(1e-4f, acc[k] - acc[k - 1]);
                    var pos = Vector3.Lerp(pts[k - 1], pts[k], (sAt - acc[k - 1]) / seg);
                    // the heading over 8 m ahead (a smooth eye round the street's own bends)
                    int k2 = Mathf.Min(pts.Count - 1, k + 4), k1 = Mathf.Max(0, k - 1);
                    var f = pts[k2] - pts[k1]; f.y = 0f;
                    rot = Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward, Vector3.up);
                    return pos;
                }
                float straight = Vector2.Distance(new Vector2(pts[0].x, pts[0].z), g.plan);
                // set down at the far end; the ring built, the trees in
                Time.captureFramerate = 0;
                var p0 = Pose(0f, out var r0);
                world.EnsureRing(p0, 1);
                car.ResetTo(p0, r0, seated: true);
                float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 90f)
                {
                    if (world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - s0 > 8f) break;
                    car.ResetTo(p0, r0, seated: true);
                    PlaceEye(cam, car.transform.position, r0);
                    yield return null;
                }
                for (int k = 0; k < 30; k++) { PlaceEye(cam, car.transform.position, r0); yield return null; }
                float lift = car.transform.position.y - p0.y;
                Time.captureFramerate = 30;
                for (int k = 0; k < 10; k++) { car.TeleportTo(p0 + Vector3.up * lift, r0); PlaceEye(cam, car.transform.position, r0); yield return null; }
                bool shootThis = shots && (shotGate < 0 ? measured == 0 : shotGate == gi);
                string seg = "gate" + gi + "_" + TrackCatalog.At(g.venue).id.ToLowerInvariant();
                meter.Begin(seg, car.transform, 0);
                float sNow = 0f, v = kmh / 3.6f, cameOnAt = -1f, fullAt = -1f;
                var singles = new System.Collections.Generic.List<float> { 500f, 450f, 400f, 350f, 300f };
                var pending = new System.Collections.Generic.Queue<string>();
                int frame = 0, stripN = 0;
                float lastStripT = -1f;
                bool wasOn = g.root.gameObject.activeSelf;
                if (wasOn) LightPopCheck.Line("  NOTE gate " + gi + " already on at the start (" + straight.ToString("0") + " m)");
                while (sNow < L)
                {
                    sNow += v * Time.deltaTime;
                    var p = Pose(sNow, out var r);
                    car.TeleportTo(p + Vector3.up * lift, r);
                    PlaceEye(cam, car.transform.position, r);
                    var eye = cam.transform.position;
                    float d = Vector2.Distance(new Vector2(eye.x, eye.z), g.plan);
                    yield return null;   // the gate code runs in this frame's Update; the meter renders in LateUpdate
                    frame++;
                    CityRaceStarts.MeterRead(gi, out bool on, out float light);
                    trace.Append(gi).Append(',').Append(frame).Append(',').Append((frame / 30f).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                         .Append(d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(on ? 1 : 0).Append(',')
                         .Append((on ? light : 0f).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    if (on && !wasOn && cameOnAt < 0f) cameOnAt = d;
                    if (on && light >= 0.999f && fullAt < 0f) fullAt = d;
                    wasOn = on;
                    if (shootThis)
                    {
                        while (singles.Count > 0 && d <= singles[0]) { pending.Enqueue("single_" + singles[0].ToString("000") + "m_at" + d.ToString("000.0", System.Globalization.CultureInfo.InvariantCulture)); singles.RemoveAt(0); }
                        float tNow = frame / 30f;
                        if (d <= 470f && d >= 430f && (lastStripT < 0f || tNow - lastStripT >= 0.1f - 1e-4f))
                        { pending.Enqueue("strip_" + (stripN++).ToString("00") + "_at" + d.ToString("000.0", System.Globalization.CultureInfo.InvariantCulture)); lastStripT = tNow; }
                        // one look frame per game frame (the runner takes one), the strip's first
                        if (pending.Count > 0 && !LightLookShotRunner.Busy) LightLookShotRunner.Queue(shotDir, seg + "_" + pending.Dequeue());
                    }
                    if (d < toM && pending.Count == 0) break;
                }
                string line = meter.End();
                measured++;
                LightPopCheck.Line(line + "\n    gate " + gi + " " + g.name + ": approach " + L.ToString("0") + " m of road from " + straight.ToString("0") +
                                   " m straight-line; root came on at " + (cameOnAt < 0f ? "-" : cameOnAt.ToString("0.0") + " m") +
                                   ", full light at " + (fullAt < 0f ? "-" : fullAt.ToString("0.0") + " m") + "; " + meter.GateRenderers + " gate renderers metered");
                Time.captureFramerate = 0;
            }
            try { File.WriteAllText(Path.Combine(LightPopMeter.OutDir, "gate_trace.csv"), trace.ToString()); } catch (System.Exception) { }
            LightPopCheck.Line("gates approached " + measured + " of " + list.Count);
            if (LightPopCheck.Env("PSX_GATE_AB", "0") == "1")
            {
                int abGate = Mathf.RoundToInt(EnvF("PSX_GATE_ABGATE", shotGate < 0 ? 0 : shotGate));
                yield return GateAB(cam, car, world, map, Mathf.Clamp(abGate, 0, gates.Count - 1), EnvF("PSX_GATE_ABM", 380f));
            }
            if (chase != null) chase.enabled = true;
        }

        /// <summary>The fade's cost, in one process: NEW / OLD / NEW again (the noise) in rotating blocks.</summary>
        IEnumerator GateAB(Camera cam, CarController car, CityWorld world, CityMap map, int gi, float standM)
        {
            stage = "cost A/B";
            var g = CityRaceStarts.Gates[gi];
            var pts = CityRaceStarts.ApproachLine(map, gi, standM + 80f);
            int bi = -1; float bd = float.MaxValue;
            for (int i = 0; i + 4 < pts.Count; i++)
            {
                // the eye stands 6.5 m behind the car
                float d = Mathf.Abs(Vector2.Distance(new Vector2(pts[i].x, pts[i].z), g.plan) + 6.5f - standM);
                if (d < bd) { bd = d; bi = i; }
            }
            if (bi < 0) { LightPopCheck.Line("A/B: no road into gate " + gi); yield break; }
            var f = pts[bi + 4] - pts[bi]; f.y = 0f;
            var rot = Quaternion.LookRotation(f.normalized, Vector3.up);
            Time.captureFramerate = 0;
            world.EnsureRing(pts[bi], 1);
            float s0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - s0 < 90f)
            {
                car.ResetTo(pts[bi], rot, seated: true);
                PlaceEye(cam, car.transform.position, rot);
                if (world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - s0 > 8f) break;
                yield return null;
            }
            var hold = car.transform.position;
            float eyeD = Vector2.Distance(new Vector2(cam.transform.position.x, cam.transform.position.z), g.plan);
            const int W = 3832, H = 2054, Settle = 20, Frames = 60;
            int reps = Mathf.Max(2, Mathf.RoundToInt(EnvF("PSX_GATE_ABREPS", 8f)));
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1, name = "GateAB" };
            rt.Create();
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            string[] names = { "new", "old", "new2" };
            bool[] vals = { true, false, true };
            var ms = new System.Collections.Generic.List<float>[3];
            var dc = new System.Collections.Generic.List<float>[3];
            var sp = new System.Collections.Generic.List<float>[3];
            var us = new System.Collections.Generic.List<float>[3];
            for (int i = 0; i < 3; i++) { ms[i] = new System.Collections.Generic.List<float>(); dc[i] = new System.Collections.Generic.List<float>(); sp[i] = new System.Collections.Generic.List<float>(); us[i] = new System.Collections.Generic.List<float>(); }
            bool was = CityRaceStarts.FadeIn;
            CityRaceStarts.Clocked = true;
            var sw = new System.Diagnostics.Stopwatch();
            for (int r = 0; r < reps; r++)
                for (int j = 0; j < 3; j++)
                {
                    int v = (j + r) % 3;   // the order rotates: a drift lands on every variant alike
                    CityRaceStarts.FadeIn = vals[v];
                    for (int k = 0; k < Settle; k++) { car.TeleportTo(hold, rot); PlaceEye(cam, hold, rot); yield return null; }
                    for (int k = 0; k < Frames; k++)
                    {
                        car.TeleportTo(hold, rot); PlaceEye(cam, hold, rot);
                        long c0 = CityRaceStarts.ClockTicks;
                        yield return null;   // this frame's Update has run the gates
                        long gt = CityRaceStarts.ClockTicks - c0;
                        sw.Restart();
                        var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                        else { var t = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = t; }
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);   // waits for the GPU
                        RenderTexture.active = prev;
                        sw.Stop();
                        ms[v].Add((float)sw.Elapsed.TotalMilliseconds);
                        dc[v].Add(UnityStats.drawCalls); sp[v].Add(UnityStats.setPassCalls);
                        us[v].Add((float)(gt * 1e6 / System.Diagnostics.Stopwatch.Frequency));
                    }
                }
            CityRaceStarts.Clocked = false;
            CityRaceStarts.FadeIn = was;
            rt.Release(); Object.Destroy(rt); Object.Destroy(one);
            float Med(System.Collections.Generic.List<float> l) { var a = l.ToArray(); System.Array.Sort(a); return a.Length == 0 ? 0f : a[a.Length / 2]; }
            float Mean(System.Collections.Generic.List<float> l) { float t = 0f; foreach (var x in l) t += x; return l.Count == 0 ? 0f : t / l.Count; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            LightPopCheck.Line("COST A/B (EDITOR, profile " + (PlatformProfile.PC ? "PC" : "non-PC (relative)") + "): stood " + eyeD.ToString("0") + " m short of gate " + g.name +
                               " (fade light there " + CityRaceStarts.FadeAt(eyeD).ToString("0.00", ci) + "), main view " + W + "x" + H + " waited for, " + reps + " blocks x " + Frames + " frames per variant");
            LightPopCheck.Line("  variant | render ms median | mean | draw calls | set pass | gate code us/frame (mean)");
            for (int i = 0; i < 3; i++)
                LightPopCheck.Line("  " + names[i].PadRight(7) + " | " + Med(ms[i]).ToString("0.000", ci) + " | " + Mean(ms[i]).ToString("0.000", ci) + " | " +
                                   Mean(dc[i]).ToString("0.0", ci) + " | " + Mean(sp[i]).ToString("0.0", ci) + " | " + Mean(us[i]).ToString("0.00", ci));
            LightPopCheck.Line("  A/B new-old median ms " + (Med(ms[0]) - Med(ms[1])).ToString("+0.000;-0.000", ci) +
                               " | null A/A new-new2 " + (Med(ms[0]) - Med(ms[2])).ToString("+0.000;-0.000", ci) +
                               " | draw calls new-old " + (Mean(dc[0]) - Mean(dc[1])).ToString("+0.0;-0.0", ci) + " (A/A " + (Mean(dc[0]) - Mean(dc[2])).ToString("+0.0;-0.0", ci) + ")" +
                               " | gate code us new-old " + (Mean(us[0]) - Mean(us[1])).ToString("+0.00;-0.00", ci));
        }

        /// <summary>The scripted approach's eye: behind and over the car, as the chase view sits.</summary>
        static void PlaceEye(Camera cam, Vector3 carPos, Quaternion rot)
        {
            if (cam == null) return;
            var f = rot * Vector3.forward;
            cam.transform.position = carPos - f * 6.5f + Vector3.up * 2.4f;
            cam.transform.LookAt(carPos + f * 25f + Vector3.up * 1.2f, Vector3.up);
        }

        // ------------------------------------------------------------------
        IEnumerator TunnelShots(CarController car)
        {
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (def == null || car == null) { LightPopCheck.Line("FAIL no venue / car"); yield break; }
            TrackCatalog.EnsureStage(def);
            // (stage4) PSX_BF_SOLIDSET=old on a PSX_BANKSOLID_AB=1 scene: the old slab stands (for the wire shots)
            if (System.Environment.GetEnvironmentVariable("PSX_BF_SOLIDSET") == "old")
                LightPopCheck.Line("solid set " + BankFitProbe.ApplySolidSet());
            // (tunnel2) a venue with no tunnel: absolute metres only ("a<m>"), against a span at 0
            var spans = def.tunnels != null && def.tunnels.Length > 0 ? def.tunnels : new[] { Vector2.zero };
            var pts = def.stagePts;
            var auto = car.GetComponent<AIDriver>();
            if (auto != null) Destroy(auto);
            int n = pts.Length, no = 0;
            foreach (var span in spans)
            {
                // (parkwaytunnel) PSX_LIGHTPOP_TUNNELOFFS: metres from the entry
                // portal ("-90,12,60" default); an "r" prefix stands that far
                // from the EXIT portal facing back the other way (r-90 = 90 m
                // past the exit, looking at it). PSX_LIGHTPOP_VIEWS picks views.
                foreach (string offRaw in LightPopCheck.Env("PSX_LIGHTPOP_TUNNELOFFS", "-90,12,60").Split(','))
                {
                    // (tunnel2) "a3868" = that path metre itself (facing forward; "ra3868" facing back);
                    // a "@x" suffix stands the car x m right of the centre line (default a quarter road)
                    string ot = offRaw.Trim();
                    float lat = def.roadWidth * 0.25f;
                    int at = ot.IndexOf('@');
                    if (at > 0) { float.TryParse(ot.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat); ot = ot.Substring(0, at); }
                    bool back = ot.StartsWith("r");
                    if (back) ot = ot.Substring(1);
                    bool abs = ot.StartsWith("a");
                    if (abs) ot = ot.Substring(1);
                    if (!float.TryParse(ot, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float off)) continue;
                    float m = Mathf.Clamp(abs ? off : back ? span.y - off : span.x + off, 0f, (n - 2) * TrackCatalog.Spacing);
                    int i = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 1, n - 2);
                    Vector3 fwd = pts[i + 1] - pts[i - 1]; fwd.y = 0f; fwd.Normalize();
                    if (back) fwd = -fwd;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    var pos = pts[i] + right * lat + Vector3.up * 0.45f;
                    var rot = Quaternion.LookRotation(fwd, Vector3.up);
                    foreach (var vn in LightPopCheck.Env("PSX_LIGHTPOP_VIEWS", "chase,cockpit").Split(','))
                    {
                        if (!System.Enum.TryParse(vn.Trim(), true, out ChaseCamera.View view)) view = ChaseCamera.View.Chase;
                        ChaseCamera.PreviewView(view);
                        for (int k = 0; k < 90; k++) { car.ResetTo(pos, rot, seated: true); yield return null; }   // (tunnel2) 90: a chase camera settled after a long jump
                        string seg = "tunnel" + no + "_" + (back ? "r" : "") + offRaw.Trim().TrimStart('r').Replace("-", "m").Replace("@", "x") + "_" + view.ToString().ToLowerInvariant();
                        LightLookShotRunner.Queue(System.IO.Path.Combine(LightPopMeter.OutDir, "shots"), seg); yield return null;
                        LightPopCheck.Line("shot " + seg + " at " + pos.ToString("F0") + " (path m " + m.ToString("0") + "); tunnel lamps " + TunnelLamps.Count + ", forests queued " + LightLookShots.ForestsQueued + (LightLookShots.ShotForest ? "" : " (PSX_SHOT_FOREST=0)"));
                        if (BankWireOverlay.Wanted)
                        {
                            // (stage4) the same view with the cut solids drawn as a wire over it
                            while (LightLookShotRunner.Busy) yield return null;
                            var ov = BankWireOverlay.Build(pos, 70f);
                            car.ResetTo(pos, rot, seated: true); yield return null;
                            LightLookShotRunner.Queue(System.IO.Path.Combine(LightPopMeter.OutDir, "shots"), seg + "_wire"); yield return null;
                            while (LightLookShotRunner.Busy) yield return null;
                            Destroy(ov);
                            LightPopCheck.Line("shot " + seg + "_wire (cut solids as a wire)");
                        }
                    }
                }
                if (++no >= 2) break;
            }
        }

        // ------------------------------------------------------------------
        IEnumerator Race(LightPopMeter meter)
        {
            stage = "race coming up";
            float t0 = Time.realtimeSinceStartup;
            while ((RaceManager.Instance == null || RaceReplay.Instance == null) && Time.realtimeSinceStartup - t0 < 240f) yield return null;
            var rm = RaceManager.Instance; var rp = RaceReplay.Instance;
            if (rm == null || rp == null) { LightPopCheck.Line("FAIL the race never came up"); yield break; }
            var car = rm.playerCar;
            if (car != null)
            {
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                var auto = car.gameObject.AddComponent<AIDriver>();
                auto.path = rm.path; auto.skill = 0.97f; car.allowReverse = false;
            }
            if (rm.path != null && !rm.path.HasEnds && !rm.Sprint) rm.totalLaps = 1;
            t0 = Time.realtimeSinceStartup;
            stage = "countdown";
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup - t0 < 480f) yield return null;
            LightPopCheck.Line("race " + SceneManager.GetActiveScene().name + ": green after " + (Time.realtimeSinceStartup - t0).ToString("0") +
                               " s; " + rm.allCars.Count + " cars; hour " + TimeOfDay.All[RaceHandoff.TimeOfDayIndex].name);
            // (lightlook) PSX_LIGHTPOP_TUNNELSHOTS=1: the look shots at each tunnel of the stage
            // (approach, mouth, inside) in both views, the player's car stood there; then done
            if (LightPopCheck.Env("PSX_LIGHTPOP_TUNNELSHOTS", "0") == "1") { yield return TunnelShots(car); yield break; }
            // (parkwaytunnel) PSX_LIGHTPOP_FROMM: the player's car set down this
            // far along the race path first (a stage tunnel's approach)
            float fromM = EnvF("PSX_LIGHTPOP_FROMM", -1f);
            if (fromM >= 0f && car != null && rm.path != null && rm.path.Count > 4)
            {
                int fi = Mathf.Clamp(Mathf.RoundToInt(fromM / rm.path.spacing), 1, rm.path.Count - 2);
                Vector3 fp = rm.path.GetPoint(fi), ft = rm.path.GetTangent(fi); ft.y = 0f;
                Vector3 fr = Vector3.Cross(Vector3.up, ft.normalized);
                var frot = Quaternion.LookRotation(ft.normalized, Vector3.up);
                for (int k = 0; k < 5; k++) { car.ResetTo(fp + fr * 1.6f + Vector3.up * 0.45f, frot, seated: true); yield return null; }
                var fai = car.GetComponent<AIDriver>();
                if (fai != null) fai.ReseedPath();
                LightPopCheck.Line("set down at " + fromM.ToString("0") + " m");
            }
            float secs = EnvF("PSX_LIGHTPOP_SECS", 40f);
            int strip = Mathf.RoundToInt(EnvF("PSX_LIGHTPOP_STRIP", 15f));
            string venue = LightPopCheck.Env("PSX_LIGHTPOP_VENUE", "race");
            bool meterRace = EnvF("PSX_LIGHTPOP_RACEMETER", 1f) > 0.5f;
            Time.captureFramerate = 30;
            foreach (var view in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Cockpit })
            {
                stage = "race " + view;
                ChaseCamera.PreviewView(view);
                for (int k = 0; k < 10; k++) yield return null;
                if (meterRace) meter.Begin(venue + "_race_" + view.ToString().ToLowerInvariant(), car != null ? car.transform : null, strip);
                float g0 = Time.time;
                while (Time.time - g0 < secs && rm.State != RaceManager.RaceState.Finished) yield return null;
                if (meterRace) LightPopCheck.Line(meter.End());
            }
            Time.captureFramerate = 0;
            bool finished = rm.State == RaceManager.RaceState.Finished && rp.RecordingDone;
            if (!finished)
                typeof(RaceReplay).GetField("recording", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(rp, false);
            if (!rp.Available) { LightPopCheck.Line("FAIL no recording"); yield break; }
            var plan = RacePlan.For(rm, rp);
            var tape = plan != null ? Tapes.SaveFromReplay(rp, rm, out string why) : null;
            LightPopCheck.Line("tape: " + (tape != null ? tape.clips.Count + " clips" : "none"));

            stage = "replay";
            rp.Begin();
            Time.captureFramerate = 30;
            for (int k = 0; k < 3; k++) yield return null;
            meter.Begin(venue + "_replay", null, strip);
            float r0 = Time.time, rsecs = EnvF("PSX_LIGHTPOP_REPLAY", 60f);
            while (Time.time - r0 < rsecs && RaceReplay.Playing) yield return null;
            LightPopCheck.Line(meter.End());
            Time.captureFramerate = 0;
            rp.End();
            yield return null;
        }
    }
}
