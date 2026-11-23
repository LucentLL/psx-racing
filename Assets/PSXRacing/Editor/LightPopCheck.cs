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
            if (LightPopCheck.Env("PSX_LIGHTPOP_MODE", "city") == "race") yield return Race(meter);
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
        IEnumerator TunnelShots(CarController car)
        {
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            if (def == null || def.tunnels == null || def.tunnels.Length == 0 || car == null) { LightPopCheck.Line("FAIL no tunnels here"); yield break; }
            TrackCatalog.EnsureStage(def);
            var pts = def.stagePts;
            var auto = car.GetComponent<AIDriver>();
            if (auto != null) Destroy(auto);
            int n = pts.Length, no = 0;
            foreach (var span in def.tunnels)
            {
                // (parkwaytunnel) PSX_LIGHTPOP_TUNNELOFFS: metres from the entry
                // portal ("-90,12,60" default); an "r" prefix stands that far
                // from the EXIT portal facing back the other way (r-90 = 90 m
                // past the exit, looking at it). PSX_LIGHTPOP_VIEWS picks views.
                foreach (string offRaw in LightPopCheck.Env("PSX_LIGHTPOP_TUNNELOFFS", "-90,12,60").Split(','))
                {
                    string ot = offRaw.Trim();
                    bool back = ot.StartsWith("r");
                    if (back) ot = ot.Substring(1);
                    if (!float.TryParse(ot, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float off)) continue;
                    float m = Mathf.Clamp(back ? span.y - off : span.x + off, 0f, (n - 2) * TrackCatalog.Spacing);
                    int i = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 1, n - 2);
                    Vector3 fwd = pts[i + 1] - pts[i - 1]; fwd.y = 0f; fwd.Normalize();
                    if (back) fwd = -fwd;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    var pos = pts[i] + right * (def.roadWidth * 0.25f) + Vector3.up * 0.45f;
                    var rot = Quaternion.LookRotation(fwd, Vector3.up);
                    foreach (var vn in LightPopCheck.Env("PSX_LIGHTPOP_VIEWS", "chase,cockpit").Split(','))
                    {
                        var view = vn.Trim() == "cockpit" ? ChaseCamera.View.Cockpit : ChaseCamera.View.Chase;
                        ChaseCamera.PreviewView(view);
                        for (int k = 0; k < 40; k++) { car.ResetTo(pos, rot, seated: true); yield return null; }
                        string seg = "tunnel" + no + "_" + (back ? "r" : "") + offRaw.Trim().TrimStart('r').Replace("-", "m") + "_" + view.ToString().ToLowerInvariant();
                        LightLookShotRunner.Queue(System.IO.Path.Combine(LightPopMeter.OutDir, "shots"), seg); yield return null;
                        LightPopCheck.Line("shot " + seg + " at " + pos.ToString("F0") + "; tunnel lamps " + TunnelLamps.Count + ", field lamps " + StreetLights.FieldLamps + ", registered " + StreetLights.Registered);
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
