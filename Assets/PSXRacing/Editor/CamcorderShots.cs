using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CAMCORDER, played and photographed (2026-11-16; round 2: a camera at
    /// every race point). A PLAY-MODE run, meant to be WATCHED
    /// (tools\camcorder-shots.ps1). For each item of the plan a career race
    /// runs with the player's car on autopilot (fast-forwarded), to its finish
    /// or to a cap; SAVE TO TAPE cuts a tape of it into a scratch folder; then
    /// the REPLAY plays in its default view and a frame of the PSX framebuffer
    /// (the OSD drawn in) is taken AT EVERY CLIP: the car at the point from the
    /// FRONT bystander, and from the BACK one where there are two.
    ///
    /// The report says, per race point: its kind and number, how many
    /// bystanders stand at it and where (off every road, the point in sight,
    /// how far a soft point slid), and per clip: the OSD's words, which lens,
    /// its distance to the car, and what the camera SAW of the player (the
    /// flex tags and the clip's dollars), then the tape's price.
    ///
    /// The plan: PSX_CAMSHOT_PLAN = "Venue:Hour[:slideN],..." - ":slideN" has
    /// the autopilot throw the car sideways through checkpoint N (the flex
    /// proof: the same race clean and with a slide). PSX_CAMSHOT_UNTILCP=N cuts
    /// the race 120 m past checkpoint N; PSX_CAMSHOT_MAXRACE = game seconds
    /// (default 420); PSX_CAMSHOT_FAST = the race's time scale (default 3);
    /// PSX_CAMSHOT_TRAFFIC = the race's traffic level (0 none; unset = the hour's);
    /// PSX_CAMSHOT_LAPS = laps for a circuit (default 1).
    /// PSX_CAMCORDER=0 photographs today's trackside replay at the same race
    /// moments (the honest before). PSX_CAMSHOT_TAG names the files.
    /// No WaitForEndOfFrame anywhere (batch never brings it), a hard watchdog.
    /// </summary>
    public static class CamcorderShots
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static List<(int venue, int hour, string name, int slideCp)> plan;
        internal static string tag;
        /// <summary>Nov 16 1999 on the career calendar.</summary>
        internal const int Day = 320;
        internal static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        internal static string TapeDir => Path.Combine(ProjectDir, "CamcorderShots", "Tapes");

        [MenuItem("PSX Racing/Camcorder Shots (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            // A watched editor is not batch: the first trip home would be the title's.
            TitleScreen.Skip = true;
            tag = System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_TAG");
            if (string.IsNullOrEmpty(tag)) tag = Camcorder.Enabled ? "cam" : "base";
            string spec = System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_PLAN");
            if (string.IsNullOrEmpty(spec)) spec = "GillespieGap:Noon,GillespieGap:Night,UptownLoop:Noon,UptownLoop:Night";
            plan = new List<(int, int, string, int)>();
            var scenes = EditorBuildSettings.scenes;
            foreach (var item in spec.Split(','))
            {
                var parts = item.Split(':');
                if (parts.Length < 2) continue;
                if (!TrackCatalog.TryIndexOf(parts[0].Trim(), out int v)) { Line("  FAIL no venue " + parts[0]); failures++; continue; }
                int hour = parts[1].Trim().ToLowerInvariant() switch
                {
                    "dawn" => TimeOfDay.Dawn, "morning" => TimeOfDay.Morning, "noon" => TimeOfDay.Noon,
                    "afternoon" => TimeOfDay.Afternoon, "sunset" => TimeOfDay.Sunset, "dusk" => TimeOfDay.Dusk,
                    _ => TimeOfDay.Night,
                };
                int slide = 0;
                if (parts.Length > 2 && parts[2].Trim().ToLowerInvariant().StartsWith("slide"))
                    int.TryParse(parts[2].Trim().Substring(5), out slide);
                int s = TrackCatalog.SceneIndex(v);
                if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Line("  FAIL scene not built: " + parts[0]); failures++; continue; }
                plan.Add((v, hour, parts[0].Trim() + "_" + parts[1].Trim().ToLowerInvariant() + (slide > 0 ? "_slide" + slide : ""), slide));
            }
            Line("camcorder shots, tag " + tag + ", switch " + (Camcorder.Enabled ? "ON" : "OFF") + ", " + plan.Count + " item(s)" +
                 (Application.isBatchMode ? ", HIDDEN (frames rendered on request)" : ", watched"));
            if (plan.Count == 0) { Finish(); EditorApplication.Exit(1); return; }
            try { if (Directory.Exists(TapeDir)) Directory.Delete(TapeDir, true); } catch (System.Exception) { }
            Tapes.DirOverride = TapeDir;

            EditorSceneManager.OpenScene(scenes[TrackCatalog.SceneIndex(plan[0].venue)].path);
            Prime(0);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        internal static void Prime(int k)
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = plan[k].venue;
            RaceHandoff.TimeOfDayIndex = plan[k].hour;
            RaceHandoff.CalendarDay = Day;
            RaceHandoff.WeatherOverride = 0;
            // (sprayfix) PSX_CAMSHOT_WEATHER=rain: the race in the rain, its spray photographed and measured (SprayFrames)
            if (SprayFrames.On) RaceHandoff.WeatherOverride = (int)Weather.Rain;
            else if (SprayFrames.W == "fog") RaceHandoff.WeatherOverride = (int)Weather.Fog;
            else if (SprayFrames.W == "clear") RaceHandoff.WeatherOverride = (int)Weather.Clear;
            RaceHandoff.StartFuelPct = 100f;
            // PSX_CAMSHOT_TRAFFIC=0..4: the race's traffic (the hour's own when unset).
            if (int.TryParse(System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_TRAFFIC") ?? "", out int lvl) && lvl >= 0)
                RaceHandoff.TrafficLevel = lvl;
            var cars = CarCatalog.All;
            if (cars.Count > 4)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id + ";" + cars[3].id;
                RaceHandoff.OpponentSkills = "1.0;0.98;0.96";
            }
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CamcorderShotsRunner").AddComponent<CamcorderShotsRunner>();
        }

        internal static void Line(string s)
        {
            log.AppendLine(s);
            Debug.Log("[CamcorderShots] " + s);
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            Line("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            Tapes.DirOverride = null;
            Time.timeScale = 1f;
            Line(failures == 0 ? "CAMCORDER SHOTS OK." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_camcorder_shots_" + tag + ".txt"), log.ToString());
        }

        internal static float Env(string name, float fallback) =>
            float.TryParse(System.Environment.GetEnvironmentVariable(name) ?? "", System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    public class CamcorderShotsRunner : MonoBehaviour
    {
        const float HardLimitS = 2400f;
        float started;
        readonly List<(string name, int price, string cpLine)> flex = new List<(string, int, string)>();

        void Update()
        {
            // A HIDDEN run is silent too: no window, no sound.
            if (Application.isBatchMode) AudioListener.pause = true;
            // The watchdog: a run that hangs exits with what it has.
            if (Time.realtimeSinceStartup - started > HardLimitS)
            {
                CamcorderShots.Check(false, "the run finished inside " + HardLimitS + " s");
                CamcorderShots.Finish();
                EditorApplication.Exit(2);
                enabled = false;
            }
        }

        IEnumerator Start()
        {
            started = Time.realtimeSinceStartup;
            DontDestroyOnLoad(gameObject);
            yield return null;
            for (int k = 0; k < CamcorderShots.plan.Count; k++)
            {
                var item = CamcorderShots.plan[k];
                CamcorderShots.Line(item.name + ":");
                if (k > 0)
                {
                    CamcorderShots.Prime(k);
                    SceneGate.Load(TrackCatalog.SceneIndex(item.venue));
                    yield return null;
                }
                yield return Item(k, item.name, item.slideCp);
                Time.timeScale = 1f;
            }
            // THE FLEX PROOF: the same race clean and with a slide.
            for (int i = 0; i < flex.Count; i++)
                for (int j = 0; j < flex.Count; j++)
                {
                    if (i == j || !flex[j].name.StartsWith(flex[i].name + "_slide")) continue;
                    CamcorderShots.Line("flex: " + flex[i].name + " $" + flex[i].price + " [" + flex[i].cpLine + "]  vs  " +
                                        flex[j].name + " $" + flex[j].price + " [" + flex[j].cpLine + "]");
                    CamcorderShots.Check(flex[j].price != flex[i].price && flex[j].cpLine != flex[i].cpLine,
                                         "the tape's value changes with what the checkpoint's camera saw",
                                         "$" + flex[i].price + " -> $" + flex[j].price);
                }
            CamcorderShots.Finish();
            EditorApplication.Exit(CamcorderShots.failures == 0 ? 0 : 1);
        }

        IEnumerator Item(int k, string name, int slideCp)
        {
            int want = TrackCatalog.SceneIndex(CamcorderShots.plan[k].venue);
            float t0 = Time.realtimeSinceStartup;
            while ((SceneManager.GetActiveScene().buildIndex != want || RaceManager.Instance == null || RaceReplay.Instance == null)
                   && Time.realtimeSinceStartup - t0 < 180f)
                yield return null;
            var rm = RaceManager.Instance;
            var rp = RaceReplay.Instance;
            CamcorderShots.Check(rm != null && rp != null && SceneManager.GetActiveScene().buildIndex == want,
                                 "the race is up", SceneManager.GetActiveScene().name);
            if (rm == null || rp == null) yield break;

            // The player's car on autopilot, so the camcorders have it to film.
            var car = rm.playerCar;
            if ((SprayFrames.On || SprayFrames.Dry) && car != null)
            {
                // (sprayfix) parked on the grid in the rain, the clock stopped for the frames
                float w0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - w0 < 2.5f) yield return null;
                CamcorderShots.Line("  rain: weather " + Seasons.CurrentWeather + ", WeatherFx.Raining " + WeatherFx.Raining + ", spray2 " + TireSpray.Spray2 +
                                    ", cardrops2 " + TireSpray.CarDrops2);
                float ts0 = Time.timeScale;
                Time.timeScale = 0f;
                yield return null;
                SprayFrames.Parked(car, name);
                if (SprayFrames.On) SprayFrames.Viewer(name);
                Time.timeScale = ts0;
                TireSpray.ResetBirthStats();
            }
            AIDriver auto = null;
            if (car != null)
            {
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                auto = car.gameObject.AddComponent<AIDriver>();
                auto.path = rm.path;
                auto.skill = 0.97f;
                car.allowReverse = false;
            }
            int laps = Mathf.Max(1, Mathf.RoundToInt(CamcorderShots.Env("PSX_CAMSHOT_LAPS", 1f)));
            if (rm.path != null && !rm.path.HasEnds && !rm.Sprint) rm.totalLaps = laps;

            t0 = Time.realtimeSinceStartup;
            // A city course streams in first (HoldCountdown): minutes on a busy machine.
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup - t0 < 480f) yield return null;
            CamcorderShots.Check(rm.State != RaceManager.RaceState.Countdown, "the lights went green",
                                 (Time.realtimeSinceStartup - t0).ToString("0") + " s");

            // ---- the race, fast-forwarded, to its finish or the cap ----
            float fast = Mathf.Clamp(CamcorderShots.Env("PSX_CAMSHOT_FAST", 3f), 1f, 6f);
            float maxRace = CamcorderShots.Env("PSX_CAMSHOT_MAXRACE", 420f);
            int untilCp = Mathf.RoundToInt(CamcorderShots.Env("PSX_CAMSHOT_UNTILCP", 0f));
            Time.timeScale = fast;
            float g0 = Time.time;
            t0 = Time.realtimeSinceStartup;
            RacePlan live = null;
            int slidePoint = -1, untilPoint = -1;
            float slideUntil = -1f, slideSign = 1f, untilAt = -1f;
            bool slid = false;
            int sprayNext = 0;
            while (Time.time - g0 < maxRace && Time.realtimeSinceStartup - t0 < 900f)
            {
                if (rm.State == RaceManager.RaceState.Finished && rp.RecordingDone) break;
                if (SprayFrames.On && car != null && sprayNext < SprayMarksKmh.Length && car.speedKmh >= SprayMarksKmh[sprayNext])
                {
                    float ts = Time.timeScale;
                    Time.timeScale = 1f;
                    yield return SprayMoving(rm, car, name, sprayNext);
                    Time.timeScale = ts;
                    sprayNext++;
                }
                // The plan needs the grid (the recording's first frames).
                if (live == null && rp.FrameCount > 5 && car != null)
                {
                    live = RacePlan.For(rm, rp);
                    if (live != null)
                        for (int i = 0; i < live.course.points.Count; i++)
                        {
                            var p = live.course.points[i];
                            if (p.kind == RacePoint.Kind.Checkpoint && p.index == slideCp) slidePoint = i;
                            if (p.kind == RacePoint.Kind.Checkpoint && p.index == untilCp) untilPoint = i;
                        }
                }
                if (live != null && car != null)
                {
                    // THE DELIBERATE SLIDE: handbrake and a flick of the wheel
                    // 35 m before the point, held for under a second.
                    if (slidePoint >= 0 && !slid)
                    {
                        var p = live.course.points[slidePoint];
                        Vector3 d = car.transform.position - p.pos;
                        float along = Vector3.Dot(d, p.heading);
                        if (along > -35f && along < 0f && d.magnitude < 60f)
                        {
                            slid = true;
                            slideUntil = Time.time + 0.8f;
                            // Into the bend the road makes there (right = +).
                            slideSign = 1f;
                            if (p.pathIdx >= 0 && rm.path != null)
                            {
                                float turn = Vector3.SignedAngle(rm.path.GetTangent(p.pathIdx - 4), rm.path.GetTangent(p.pathIdx + 12), Vector3.up);
                                if (Mathf.Abs(turn) > 2f) slideSign = Mathf.Sign(turn);
                            }
                            if (auto != null) auto.enabled = false;
                            CamcorderShots.Line("  the autopilot throws it sideways into checkpoint " + slideCp + " at " +
                                                Mathf.RoundToInt(car.speedKmh) + " km/h");
                        }
                    }
                    if (slideUntil > 0f)
                    {
                        float left = slideUntil - Time.time;
                        if (left > 0f)
                        {
                            car.throttleInput = 1f; car.brakeInput = 0f;
                            car.steerInput = left > 0.35f ? slideSign * 0.8f : -slideSign * 0.4f;
                            car.handbrakeInput = left > 0.4f;
                        }
                        else
                        {
                            car.handbrakeInput = false;
                            if (auto != null) auto.enabled = true;
                            slideUntil = -1f;
                        }
                    }
                    if (untilPoint >= 0)
                    {
                        var p = live.course.points[untilPoint];
                        Vector3 d = car.transform.position - p.pos;
                        if (untilAt < 0f && Vector3.Dot(d, p.heading) > 0f && d.magnitude < 60f) untilAt = Time.time;
                        if (untilAt > 0f && Vector3.Dot(d, p.heading) > 120f) break;
                    }
                }
                yield return null;
            }
            Time.timeScale = 1f;
            bool finished = rm.State == RaceManager.RaceState.Finished && rp.RecordingDone;
            if (!finished)
            {
                // The flag, for the check (ReplayCheck's way): STAGED, nobody won.
                typeof(RaceReplay).GetField("recording", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(rp, false);
            }
            CamcorderShots.Line("  the race: " + (Time.time - g0).ToString("0") + " s of it at x" + fast + ", " +
                                (finished ? "to the flag, P" + rm.GetPosition(car) + " of " + rm.allCars.Count
                                          : "CUT by the check (not a finished race)"));
            CamcorderShots.Check(rp.Available, "a recording to play", rp.Duration.ToString("0.0") + " s");
            if (!rp.Available) yield break;

            // ---- SAVE TO TAPE (a scratch folder), and what its cameras saw ----
            var plan = RacePlan.For(rm, rp);
            CamcorderShots.Check(plan != null && plan.course.points.Count >= 2, "the race has its points",
                                 plan != null ? plan.course.points.Count + " (" + plan.course.source + ", " + plan.course.Checkpoints +
                                 " checkpoints, " + (plan.course.loop ? plan.course.laps + " lap(s)" : "point to point") + ")" : "none");
            if (plan == null) yield break;
            var tape = Tapes.SaveFromReplay(rp, rm, out string why);
            CamcorderShots.Check(tape != null && tape.clips.Count > 0, "SAVE TO TAPE cuts a tape with its clips", tape != null ? tape.clips.Count + " clips, " + new FileInfo(tape.path).Length + " bytes" : why);
            if (tape != null)
            {
                foreach (string line in Tapes.ClipLines(tape, true)) CamcorderShots.Line("    " + line);
                CamcorderShots.Line("    the result +$" + Tapes.ResultValue(tape) + "   THE TAPE: $" + Tapes.Price(tape) + "   \"" + Tapes.Pitch(tape) + "\"");
                string cpLine = "";
                int cpWant = slideCp > 0 ? slideCp : 0;
                foreach (var c in tape.clips)
                    if (c.kind == TapeClip.Kind.Checkpoint && (cpWant == 0 || c.index == cpWant)) { cpLine = TapeClips.Line(c, tape.Laps) + " $" + c.value; if (cpWant > 0) break; }
                if (slideCp == 0)
                {
                    // The clean run's line for the checkpoint its twin slides at.
                    foreach (var it in CamcorderShots.plan)
                        if (it.slideCp > 0 && it.name.StartsWith(name + "_slide"))
                            foreach (var c in tape.clips)
                                if (c.kind == TapeClip.Kind.Checkpoint && c.index == it.slideCp) cpLine = TapeClips.Line(c, tape.Laps) + " $" + c.value;
                }
                flex.Add((name, Tapes.Price(tape), cpLine));
                if (slideCp > 0)
                {
                    bool saw = false;
                    foreach (var c in tape.clips) if (c.kind == TapeClip.Kind.Checkpoint && c.index == slideCp && c.Has(TapeClip.Slide)) saw = true;
                    CamcorderShots.Check(saw, "checkpoint " + slideCp + "'s camera saw the slide", cpLine);
                }
            }

            CamcorderShots.Line("  bystanders found during the race: " + plan.Judged + " of " + plan.stations.Length +
                                " points judged, " + plan.LensCount + " cameras");

            // ---- the replay ----
            rp.Begin();
            CamcorderShots.Check(RaceReplay.Playing, "the replay plays");
            bool camOn = Camcorder.Enabled;
            CamcorderShots.Check(camOn ? rp.Mode == RaceReplay.CamMode.Camcorder : rp.Mode == RaceReplay.CamMode.Trackside,
                                 "in its default view", rp.Mode);
            var cam = Camera.main;
            var cc = cam != null ? cam.GetComponent<CamcorderCam>() : null;
            var clips = tape != null ? tape.clips : TapeClips.Plan(plan.course, rp.SampleTimes, rp.TrackFrames(rp.FocusIndex));
            float dtSum = 0f; int dtN = 0;
            int shot = 0, labelled = 0, travelling = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                string label = TapeClips.Label(c, plan.course.Checkpoints, plan.course.laps);
                string file = name + "_" + i.ToString("00") + "_" + TapeClips.Short(c, plan.course.laps).Replace(" ", "");
                var st = plan.stations[Mathf.Clamp(c.point, 0, plan.stations.Length - 1)];
                if (camOn && cc != null)
                {
                    // (A point not judged yet is judged when the tape cuts to it.)
                    if (st.judged && st.lenses.Count == 0) { CamcorderShots.Line("    " + label + ": no camera, not on the tape"); continue; }
                    // The tape plays on its own (it cuts itself): wait for this clip's pass.
                    float w0 = Time.realtimeSinceStartup;
                    float front = Mathf.Max(c.tIn + 0.4f, c.tPass - 0.3f);
                    while (Time.realtimeSinceStartup - w0 < 60f && !(cc.CurrentClip == i && !rp.Hold && rp.ReplayTime >= front))
                    { dtSum += Time.unscaledDeltaTime; dtN++; yield return null; }
                    bool onIt = cc.CurrentClip == i;
                    if (st.lenses.Count == 0) { CamcorderShots.Line("    " + label + ": no camera found when the tape cut to it"); continue; }
                    Vector3 at = cam.transform.position;
                    yield return Shot(file);
                    if (SprayFrames.On) SprayFrames.Measure("replay_" + file, false);
                    string osd = CamcorderOSD.Describe();
                    if (osd.StartsWith(label)) labelled++;
                    // A bystander stands still: the lens within its sway of its spot.
                    var lens = st.lenses[Mathf.Clamp(cc.CurrentLens, 0, st.lenses.Count - 1)];
                    if ((at - lens.pos).magnitude > 0.2f) travelling++;
                    shot++;
                    CamcorderShots.Line("    t " + rp.ReplayTime.ToString("0.0") + " s  " + (onIt ? "" : "(NOT ON ITS CLIP) ") + (lens.back ? "BACK" : "FRONT") +
                                        "  car " + (rp.Focus != null ? Vector3.Distance(at, rp.Focus.transform.position).ToString("0") : "?") + " m" +
                                        "  fov " + cam.fieldOfView.ToString("0") + "  defocus " + Camcorder.Defocus.ToString("0.00") +
                                        "  OSD [" + osd + "]  mic " + (CamcorderMic.Active ? "on" : "off"));
                    // The BACK bystander, after the car has crossed.
                    if (st.lenses.Count > 1 && c.tOut - c.tPass > 1.0f)
                    {
                        w0 = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - w0 < 20f && cc.CurrentClip == i && rp.ReplayTime < c.tPass + 0.7f)
                        { dtSum += Time.unscaledDeltaTime; dtN++; yield return null; }
                        if (cc.CurrentClip == i)
                        {
                            yield return Shot(file + "_b");
                            if (SprayFrames.On) SprayFrames.Measure("replay_" + file + "_b", false);
                            CamcorderShots.Line("    t " + rp.ReplayTime.ToString("0.0") + " s  " + (cc.CurrentLens == 1 ? "BACK" : "FRONT") +
                                                "  car " + (rp.Focus != null ? Vector3.Distance(cam.transform.position, rp.Focus.transform.position).ToString("0") : "?") + " m" +
                                                "  OSD [" + CamcorderOSD.Describe() + "]");
                        }
                    }
                }
                else
                {
                    // Today's replay at the same race moment.
                    rp.Seek(Mathf.Max(0f, c.tPass - 1.3f), hard: true);
                    float w0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - w0 < 8f && rp.ReplayTime < c.tPass - 0.3f) yield return null;
                    yield return Shot(file);
                    shot++;
                    CamcorderShots.Line("    t " + rp.ReplayTime.ToString("0.0") + " s  " + rp.Mode + "  (" + label + ")");
                }
            }
            if (dtN > 0) CamcorderShots.Line("  mean frame " + (1000f * dtSum / dtN).ToString("0.0") + " ms (editor, " + (Application.isBatchMode ? "hidden" : "watched") + ")");
            if (camOn && cc != null)
            {
                CamcorderShots.Check(shot > 0 && labelled == shot, "the OSD names every clip", labelled + " of " + shot);
                CamcorderShots.Check(travelling == 0, "no camera travels: each lens at its bystander's spot", travelling + " moved");
            }
            // ---- the bystanders (after the tape: a streamed place is judged when a car or the tape reaches it) ----
            int withLens = 0, lenses = 0, far = 0, onRoad = 0, blind = 0;
            // The points the tape reached (a race cut short by the check never saw the rest).
            var reached = new HashSet<int>();
            foreach (var c in clips) reached.Add(c.point);
            for (int i = 0; i < plan.course.points.Count; i++)
            {
                var p = plan.course.points[i];
                var st = plan.stations[i];
                if (st.lenses.Count > 0 && reached.Contains(i)) withLens++;
                string where = "";
                foreach (var l in st.lenses)
                {
                    lenses++;
                    if (l.far) far++;
                    if (RacePlan.RoadUnder(l.pos)) onRoad++;
                    if (RacePlan.Blocked(l.pos, p.pos + Vector3.up * 1.1f)) blind++;
                    float lat = Mathf.Abs(Vector3.Dot(l.pos - p.pos, Vector3.Cross(Vector3.up, p.heading)));
                    float along = Vector3.Dot(l.pos - p.pos, p.heading);
                    where += "  " + (l.back ? "BACK" : "FRONT") + (l.far ? " (long lens)" : "") + " " + along.ToString("0") + " m along, " +
                             lat.ToString("0.0") + " m off the middle" + (l.crouch ? ", crouched" : "");
                }
                CamcorderShots.Line("    " + (p.kind == RacePoint.Kind.Checkpoint ? "CHECKPOINT " + p.index : p.kind.ToString().ToUpperInvariant()) +
                                    ": " + st.lenses.Count + " camera(s)" + (Mathf.Abs(st.slidM) > 0.1f ? " (the point slid " + st.slidM.ToString("0") + " m)" : "") +
                                    (st.judged ? "" : " (ground not built yet)") + where);
            }
            CamcorderShots.Check(withLens == reached.Count, "a camera at every race point the tape reached",
                                 withLens + " of " + reached.Count + " points (" + plan.course.points.Count + " on the course), " + lenses + " cameras, " + far + " long lens");
            CamcorderShots.Check(onRoad == 0, "no camera stands on a road", onRoad);
            CamcorderShots.Check(blind == 0, "every camera sees its point", blind + " blind");

            if (SprayFrames.On)
            {
                CamcorderShots.Line("  spray births in the race and its replay: " + TireSpray.Births + ", the highest " + TireSpray.MaxBirthShare.ToString("0.00") +
                                    " of its tyre's height (" + (TireSpray.Spray2 ? "spray 2" : "a99a8ef1's spray: its births are not counted") + ")");
                if (TireSpray.Spray2)
                    CamcorderShots.Check(TireSpray.Births > 0 && TireSpray.MaxBirthShare <= TireSpray.BirthShareMax + 1e-3f,
                                         "SPRAY2: every spray particle born at its tyre (under a third of its height)", TireSpray.MaxBirthShare.ToString("0.000"));
                CamcorderShots.Line("  spray frames " + SprayFrames.Frames + ": the worst plume added p95 " + SprayFrames.WorstDay.ToString("0.00") + " by day, " +
                                    SprayFrames.WorstNight.ToString("0.00") + " at night");
                if (TireSpray.Spray2)
                    CamcorderShots.Check(SprayFrames.WorstDay <= SprayDayLimit && SprayFrames.WorstNight <= SprayNightLimit,
                                         "SPRAY2: no plume brighter than the road behind it by more than " + SprayDayLimit + " by day / " + SprayNightLimit + " at night (p95)",
                                         SprayFrames.WorstDay.ToString("0.00") + " / " + SprayFrames.WorstNight.ToString("0.00"));
            }
            rp.End();
            CamcorderShots.Check(!RaceReplay.Playing && Camcorder.Camera == null, "the replay ends and the look goes with it");
            yield return null;
        }

        bool saidPass;
        static int fbLive = -1;

        // (sprayfix) the speeds the race is photographed at: 40, 60, 100 and 120 mph
        static readonly float[] SprayMarksKmh = { 64f, 97f, 161f, 193f };
        /// <summary>The plume's limit over the road behind it (display luminance 0..1, 95th percentile of
        /// the pixels the spray changes), by day and at night.</summary>
        internal const float SprayDayLimit = 0.25f, SprayNightLimit = 0.15f;

        IEnumerator SprayMoving(RaceManager rm, CarController car, string item, int k)
        {
            string mph = Mathf.RoundToInt(SprayMarksKmh[k] / 1.609f).ToString();
            for (int i = 0; i < 3; i++) yield return null;
            SprayFrames.Measure(item + "_" + mph + "mph_chase");
            if (k == 1 || k == 3)
            {
                ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
                for (int i = 0; i < 4; i++) yield return null;
                SprayFrames.Measure(item + "_" + mph + "mph_cockpit");
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                for (int i = 0; i < 4; i++) yield return null;
            }
            var chase = ChaseCamera.Active;
            if ((k == 1 || k == 2) && chase != null)
            {
                // a rival's chase view: the nearest other car
                CarController rival = null;
                float best = float.MaxValue;
                foreach (var c in rm.allCars)
                {
                    if (c == null || c == car) continue;
                    float d = (c.transform.position - car.transform.position).sqrMagnitude;
                    if (d < best) { best = d; rival = c; }
                }
                if (rival != null)
                {
                    var keep = chase.target;
                    chase.target = rival.transform;
                    for (int i = 0; i < 12; i++) yield return null;
                    SprayFrames.Measure(item + "_" + mph + "mph_rival_" + Mathf.RoundToInt(rival.speedKmh / 1.609f) + "mph");
                    chase.target = keep;
                    for (int i = 0; i < 6; i++) yield return null;
                }
            }
        }

        /// <summary>A sum over the middle of a render texture (to tell whether it changes).</summary>
        static long Sum(RenderTexture rt)
        {
            int w = Mathf.Min(96, rt.width), h = Mathf.Min(64, rt.height);
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            t.ReadPixels(new Rect((rt.width - w) / 2, (rt.height - h) / 2, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            long sum = 0;
            foreach (var c in t.GetPixels32()) sum += c.r * 3 + c.g * 5 + c.b * 7;
            Object.DestroyImmediate(t);
            return sum;
        }

        IEnumerator Shot(string name, bool quiet = false)
        {
            yield return null;
            yield return null;
            string dir = Path.Combine(CamcorderShots.ProjectDir, "Screenshots", "camcorder");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var fb = cam != null ? cam.targetTexture : null;
            if (fb == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                if (!quiet) CamcorderShots.Line("    shot " + name + " not taken (no framebuffer / no graphics)");
                yield break;
            }
            // A HIDDEN run: is the game's own framebuffer being drawn each frame
            // (then it is the picture, OSD and all) or not (then the camera is
            // rendered on request)? The tape's grain changes every frame, so
            // two reads a few frames apart tell.
            if (Application.isBatchMode && fbLive < 0)
            {
                long a = Sum(fb);
                for (int i = 0; i < 6; i++) yield return null;
                long b = Sum(fb);
                fbLive = a != b ? 1 : 0;
                CamcorderShots.Line("    (hidden run: the game's framebuffer is " + (fbLive == 1 ? "drawn each frame - read as it is" : "not drawn - the camera is rendered on request") + ")");
            }
            var tex = new Texture2D(fb.width, fb.height, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            if (!Application.isBatchMode || fbLive == 1)
            {
                // A watched editor: the frame as the game drew it, OSD and all.
                RenderTexture.active = fb;
                tex.ReadPixels(new Rect(0, 0, fb.width, fb.height), 0, 0);
            }
            else
            {
                // A HIDDEN run draws no frames (the owner is playing: no window
                // in front of his game): the camera rendered on request, three
                // times so the camcorder's eye settles. The picture and the
                // look are the game's; text the HUD camera draws may be absent.
                var rt = new RenderTexture(fb.width, fb.height, 24, fb.graphicsFormat);
                rt.Create();
                // No frame loop built the UI's meshes either.
                Canvas.ForceUpdateCanvases();
                int rec0 = CamcorderPass.RecordedForTools, skip0 = CamcorderPass.SkippedForTools;
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
                    for (int i = 0; i < 3; i++) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, fb.width, fb.height), 0, 0);
                RenderTexture.active = prev == rt ? null : prev;
                cam.targetTexture = fb;
                if (!quiet && !saidPass)
                {
                    saidPass = true;
                    CamcorderShots.Line("    (hidden frame: the camcorder pass drew " + (CamcorderPass.RecordedForTools - rec0) + "x, stood down " +
                                        (CamcorderPass.SkippedForTools - skip0) + "x; lens camera " + (Camcorder.Camera == cam ? "set" : "NOT set") + ")");
                }
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, CamcorderShots.tag + "_" + name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }

    /// <summary>
    /// THE SPRAY IN THE FRAME (sprayfix, 2026-11-23). With PSX_CAMSHOT_WEATHER=rain the camcorder run
    /// also photographs the rain on and behind the cars: parked on the grid (the body's beads close up,
    /// the whole car, the chase view at rest), the race in chase and cockpit as the player's car passes
    /// 40 / 60 / 100 / 120 mph and a rival's chase view, and every replay clip - and MEASURES each frame:
    /// the same camera rendered with every car's spray drawn and with it hidden, so the plume is the
    /// pixels the spray changed, and its brightness is read against the road behind it (the frame
    /// without it). The births of every spray particle in the run are checked against the wheels
    /// (TireSpray.MaxBirthShare). Frames to Screenshots\camcorder\&lt;tag&gt;_spray_*.png.
    /// </summary>
    internal static class SprayFrames
    {
        internal static string W => (System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_WEATHER") ?? "").ToLowerInvariant();
        internal static bool On => W == "rain";
        /// <summary>PSX_CAMSHOT_WEATHER=fog|clear: a dry race, its parked body frames only (the dots-in-shade bisect).</summary>
        internal static bool Dry => W == "fog" || W == "clear";
        /// <summary>The worst plume of the run, by day and at night (added luminance, 95th percentile).</summary>
        internal static float WorstDay, WorstNight;
        internal static int Frames;
        static readonly List<Renderer> hidden = new List<Renderer>(64);

        static bool IsSpray(Renderer r)
        {
            string n = r.gameObject.name;
            return r is ParticleSystemRenderer && (n.EndsWith(" Spray") || n.EndsWith(" Splash") || n == "Rain2 Traffic Mist");
        }

        static Texture2D Render(Camera cam, int w, int h, UnityEngine.Experimental.Rendering.GraphicsFormat fmt)
        {
            var fb = cam.targetTexture;
            var rt = new RenderTexture(w, h, 24, fmt);
            rt.Create();
            Canvas.ForceUpdateCanvases();
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
                for (int i = 0; i < 2; i++) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev == rt ? null : prev;
            cam.targetTexture = fb;
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        static float Lum(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        /// <summary>Render the camera as it stands with and without the spray, save the first as
        /// &lt;tag&gt;_spray_&lt;name&gt;.png (unless `save` is false) and log the plume against the road.</summary>
        internal static void Measure(string name, bool save = true, int forceW = 0, int forceH = 0)
        {
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var fb = cam.targetTexture;
            int w = fb != null ? fb.width : 1280, h = fb != null ? fb.height : 720;
            if (forceW > 0) { w = forceW; h = forceH; }
            var fmt = fb != null ? fb.graphicsFormat : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB;
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var with = Render(cam, w, h, fmt);
            hidden.Clear();
            foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Exclude))
                if (r.enabled && IsSpray(r)) { r.enabled = false; hidden.Add(r); }
            var without = Render(cam, w, h, fmt);
            foreach (var r in hidden) if (r != null) r.enabled = true;
            var a = with.GetPixels32();
            var b = without.GetPixels32();
            var add = new List<float>(4096);
            var lw = new List<float>(4096);
            double road = 0;
            for (int i = 0; i < a.Length; i++)
            {
                float la = Lum(a[i]), lb = Lum(b[i]);
                if (Mathf.Abs(la - lb) < 0.03f) continue;
                add.Add(la - lb);
                lw.Add(la);
                road += lb;
            }
            string dir = Path.Combine(CamcorderShots.ProjectDir, "Screenshots", "camcorder");
            Directory.CreateDirectory(dir);
            if (save) File.WriteAllBytes(Path.Combine(dir, CamcorderShots.tag + "_spray_" + name + ".png"), with.EncodeToPNG());
            Object.DestroyImmediate(with);
            Object.DestroyImmediate(without);
            bool night = RaceHandoff.TimeOfDayIndex == TimeOfDay.Night || RaceHandoff.TimeOfDayIndex == TimeOfDay.Dusk;
            if (add.Count == 0)
            {
                CamcorderShots.Line("    SPRAY " + name + ": no pixel changed by the spray (" + hidden.Count + " spray systems)");
                return;
            }
            add.Sort(); lw.Sort();
            float p95Add = add[(int)(add.Count * 0.95f)], p95With = lw[(int)(lw.Count * 0.95f)], maxWith = lw[lw.Count - 1];
            float roadMean = (float)(road / add.Count);
            Frames++;
            if (night) WorstNight = Mathf.Max(WorstNight, p95Add); else WorstDay = Mathf.Max(WorstDay, p95Add);
            CamcorderShots.Line("    SPRAY " + name + ": plume " + add.Count + " px (" + (100f * add.Count / a.Length).ToString("0.0") + " % of the frame), " +
                                "brightness p95 " + p95With.ToString("0.00") + " max " + maxWith.ToString("0.00") + " over the road behind it " + roadMean.ToString("0.00") +
                                ", added p95 " + p95Add.ToString("0.00") + (night ? " (night)" : " (day)"));
        }

        /// <summary>Parked on the grid, the time stopped: the body's rain close up and whole, and the chase view at rest.</summary>
        internal static void Parked(CarController car, string item)
        {
            var cam = Camera.main;
            if (car == null || cam == null) return;
            var chase = cam.GetComponent<ChaseCamera>();
            CamcorderShots.Line("    parked: _PSXCarDrops " + Shader.GetGlobalVector("_PSXCarDrops") + ", _PSXCarDrops2Off " + Shader.GetGlobalVector("_PSXCarDrops2Off"));
            Measure(item + "_rest_chase");
            var t = car.transform;
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            if (chase != null) chase.enabled = false;
            try
            {
                // the boot lid and the rear glass from a step behind, then the whole car from the rear quarter
                cam.fieldOfView = 40f;
                cam.transform.position = t.position + t.up * 1.55f - t.forward * 3.0f + t.right * 0.6f;
                cam.transform.LookAt(t.position + t.up * 0.95f - t.forward * 1.6f);
                Measure(item + "_rest_close", true, 1920, 1080);
                cam.transform.position = t.position + t.up * 1.6f - t.forward * 5.5f + t.right * 3.5f;
                cam.transform.LookAt(t.position + t.up * 0.6f);
                Measure(item + "_rest_whole", true, 1920, 1080);
                // a macro of the boot lid / rear glass: half a metre away, the beads resolved at 1920
                cam.fieldOfView = 30f;
                cam.transform.position = t.position + t.up * 1.45f - t.forward * 2.35f + t.right * 0.25f;
                cam.transform.LookAt(t.position + t.up * 1.0f - t.forward * 1.75f);
                Measure(item + "_rest_macro", true, 1920, 1080);
            }
            finally
            {
                cam.transform.SetPositionAndRotation(p0, r0);
                cam.fieldOfView = f0;
                if (chase != null) chase.enabled = true;
            }
        }

        /// <summary>A menu car viewer opened while it rains (its turntable frame, 960 x 600): the car indoors, dry.</summary>
        internal static void Viewer(string item)
        {
            var cars = CarCatalog.All;
            if (cars.Count == 0) return;
            var host = new GameObject("SprayFrames_Viewer");
            host.transform.position = new Vector3(0f, -500f, 0f);
            try
            {
                var viewer = host.AddComponent<PSXRacing.LifeSim.CarViewer>();
                viewer.width = 960; viewer.height = 600;
                viewer.Show(cars[0]);
                viewer.SetVisible(true);
                CamcorderShots.Line("    viewer: _PSXCarDrops in the world " + Shader.GetGlobalVector("_PSXCarDrops"));
                viewer.RenderNow();
                var rt = viewer.Picture;
                if (rt == null) return;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string dir = Path.Combine(CamcorderShots.ProjectDir, "Screenshots", "camcorder");
                File.WriteAllBytes(Path.Combine(dir, CamcorderShots.tag + "_spray_" + item + "_viewer.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                CamcorderShots.Line("    viewer: _PSXCarDrops after its render " + Shader.GetGlobalVector("_PSXCarDrops") + " (put back)");
            }
            finally { Object.Destroy(host); }
        }
    }
}
