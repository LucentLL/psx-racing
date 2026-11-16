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
    /// THE CAMCORDER, played and photographed (2026-11-16). A PLAY-MODE run,
    /// meant to be WATCHED (tools\camcorder-shots.ps1): for each venue and hour
    /// in the plan, a career race starts with the player's car on autopilot,
    /// the field races for <see cref="CamcorderShotsRunner.RecordSeconds"/>,
    /// the race is cut there (the check's flag), and the REPLAY plays in its
    /// default view; frames of the PSX framebuffer (the OSD drawn in) are taken
    /// at intervals. The log says what each frame is: the shot (an operator
    /// and what kind, or the buddy's car), the lens's height over the ground,
    /// its clearance from the road edge (never in a lane), the OSD's text, the
    /// mic, and the frame time.
    ///
    /// PSX_CAMCORDER=0 in the same run photographs today's replay at the same
    /// moments (the honest before). PSX_CAMSHOT_PLAN = "Venue:Hour,..."
    /// (default GillespieGap:Noon,GillespieGap:Night,UptownLoop:Noon,UptownLoop:Night),
    /// PSX_CAMSHOT_TAG names the files. PSX_CAMSHOT_SEQ=1 also takes a 12 fps
    /// sequence of the first item for a short clip.
    /// No WaitForEndOfFrame anywhere (batch never brings it), a hard watchdog.
    /// </summary>
    public static class CamcorderShots
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static List<(int venue, int hour, string name)> plan;
        internal static string tag;
        /// <summary>Nov 16 1999 on the career calendar.</summary>
        internal const int Day = 320;

        [MenuItem("PSX Racing/Camcorder Shots (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            tag = System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_TAG");
            if (string.IsNullOrEmpty(tag)) tag = Camcorder.Enabled ? "cam" : "base";
            string spec = System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_PLAN");
            if (string.IsNullOrEmpty(spec)) spec = "GillespieGap:Noon,GillespieGap:Night,UptownLoop:Noon,UptownLoop:Night";
            plan = new List<(int, int, string)>();
            var scenes = EditorBuildSettings.scenes;
            foreach (var item in spec.Split(','))
            {
                var parts = item.Split(':');
                if (parts.Length != 2) continue;
                if (!TrackCatalog.TryIndexOf(parts[0].Trim(), out int v)) { Line("  FAIL no venue " + parts[0]); failures++; continue; }
                int hour = parts[1].Trim().ToLowerInvariant() switch
                {
                    "dawn" => TimeOfDay.Dawn, "morning" => TimeOfDay.Morning, "noon" => TimeOfDay.Noon,
                    "afternoon" => TimeOfDay.Afternoon, "sunset" => TimeOfDay.Sunset, "dusk" => TimeOfDay.Dusk,
                    _ => TimeOfDay.Night,
                };
                int s = TrackCatalog.SceneIndex(v);
                if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Line("  FAIL scene not built: " + parts[0]); failures++; continue; }
                plan.Add((v, hour, parts[0].Trim() + "_" + parts[1].Trim().ToLowerInvariant()));
            }
            Line("camcorder shots, tag " + tag + ", switch " + (Camcorder.Enabled ? "ON" : "OFF") + ", " + plan.Count + " item(s)");
            if (plan.Count == 0) { Finish(); EditorApplication.Exit(1); return; }

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
            RaceHandoff.StartFuelPct = 100f;
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
            Line(failures == 0 ? "CAMCORDER SHOTS OK." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                              "PSXRacing_camcorder_shots_" + tag + ".txt"), log.ToString());
        }
    }

    public class CamcorderShotsRunner : MonoBehaviour
    {
        public const float RecordSeconds = 40f;
        const int Frames = 12;
        const float Every = 2.0f;
        const float HardLimitS = 1500f;
        float started;

        void Update()
        {
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
                yield return Item(k, item.name);
            }
            CamcorderShots.Finish();
            EditorApplication.Exit(CamcorderShots.failures == 0 ? 0 : 1);
        }

        IEnumerator Item(int k, string name)
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
            if (car != null)
            {
                var input = car.GetComponent<PlayerCarInput>();
                if (input != null) { input.inputEnabled = false; input.enabled = false; }
                var auto = car.gameObject.AddComponent<AIDriver>();
                auto.path = rm.path;
                auto.skill = 0.97f;
                car.allowReverse = false;
            }
            t0 = Time.realtimeSinceStartup;
            // A city course streams in first (HoldCountdown): minutes on a busy machine.
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup - t0 < 480f) yield return null;
            CamcorderShots.Check(rm.State != RaceManager.RaceState.Countdown, "the lights went green",
                                 (Time.realtimeSinceStartup - t0).ToString("0") + " s");
            float g0 = Time.time;
            t0 = Time.realtimeSinceStartup;
            while (Time.time - g0 < RecordSeconds && Time.realtimeSinceStartup - t0 < 300f) yield return null;

            // The flag, for the check (ReplayCheck's way).
            typeof(RaceReplay).GetField("recording", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(rp, false);
            CamcorderShots.Check(rp.Available, "a recording to play", rp.Duration.ToString("0.0") + " s");
            if (!rp.Available) yield break;
            rp.Begin();
            CamcorderShots.Check(RaceReplay.Playing, "the replay plays");
            bool camOn = Camcorder.Enabled;
            CamcorderShots.Check(camOn ? rp.Mode == RaceReplay.CamMode.Camcorder : rp.Mode == RaceReplay.CamMode.Trackside,
                                 "in its default view", rp.Mode);
            var cam = Camera.main;
            var cc = cam != null ? cam.GetComponent<CamcorderCam>() : null;
            if (camOn && cc != null)
            {
                int kerb = 0, over = 0, road = 0;
                foreach (var sp in cc.Spots) { if (sp.kind == CamcorderCam.Kind.Kerb) kerb++; else if (sp.kind == CamcorderCam.Kind.Overpass) over++; else road++; }
                CamcorderShots.Line("  operators: " + cc.Spots.Count + " (" + road + " roadside, " + kerb + " kerb, " + over + " overpass) on " +
                                    (rm.path != null ? (rm.path.Count * rm.path.spacing / 1000f).ToString("0.0") + " km" : "?"));
                // Never in a lane: every operator off the road's edge.
                float worst = float.MaxValue;
                foreach (var sp in cc.Spots)
                {
                    if (sp.kind == CamcorderCam.Kind.Overpass) continue;
                    int i = rm.path.NearestIndex(sp.pos);
                    Vector3 p = rm.path.GetPoint(i);
                    float lateral = new Vector2(sp.pos.x - p.x, sp.pos.z - p.z).magnitude - rm.path.roadWidth * 0.5f;
                    worst = Mathf.Min(worst, lateral);
                }
                if (cc.Spots.Count > 0)
                    CamcorderShots.Check(worst > 1.5f, "every operator stands clear of the race line's road edge", "closest " + worst.ToString("0.0") + " m");
                LaneCheck(cc, rm, "planned");
            }

            // Let the eye settle on the first shot, then shoot.
            float s0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - s0 < 1.0f) yield return null;
            bool seq = k == 0 && System.Environment.GetEnvironmentVariable("PSX_CAMSHOT_SEQ") == "1";
            float dtSum = 0f; int dtN = 0;
            for (int f = 0; f < Frames; f++)
            {
                float w0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - w0 < Every) { dtSum += Time.unscaledDeltaTime; dtN++; yield return null; }
                yield return Shot(name + "_" + f.ToString("00"));
                string what = !camOn || cc == null ? rp.Mode.ToString()
                    : cc.Chasing ? "buddy's car" : cc.Current >= 0 && cc.Current < cc.Spots.Count ? cc.Spots[cc.Current].kind.ToString() : "?";
                float ground = float.NaN;
                if (cam != null && Physics.Raycast(cam.transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 200f, ~((1 << 2) | (1 << 4))))
                    ground = cam.transform.position.y - hit.point.y;
                CamcorderShots.Line("    t " + rp.ReplayTime.ToString("0.0") + " s  " + what +
                                    "  eye " + (float.IsNaN(ground) ? "?" : ground.ToString("0.0") + " m") +
                                    "  fov " + (cam != null ? cam.fieldOfView.ToString("0") : "?") +
                                    "  defocus " + Camcorder.Defocus.ToString("0.00") +
                                    "  OSD [" + CamcorderOSD.Describe() + "]" +
                                    "  mic " + (CamcorderMic.Active ? "on" : "off"));
            }
            if (dtN > 0) CamcorderShots.Line("  mean frame " + (1000f * dtSum / dtN).ToString("0.0") + " ms (editor, watched)");
            // Operators added on the way (where the plan left nobody) obey the same rules.
            if (camOn && cc != null) LaneCheck(cc, rm, "after the tape");

            if (seq)
            {
                // ~8 s at 12 fps for the clip.
                for (int f = 0; f < 96; f++)
                {
                    float w0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - w0 < 1f / 12f) yield return null;
                    yield return Shot("seq_" + f.ToString("000"), quiet: true);
                }
                CamcorderShots.Line("  sequence: 96 frames");
            }
            rp.End();
            CamcorderShots.Check(!RaceReplay.Playing && Camcorder.Camera == null, "the replay ends and the look goes with it");
            yield return null;
        }

        /// <summary>Off EVERY road: the ground under each operator and beside
        /// them (a median between carriageways is not a place to stand).</summary>
        static void LaneCheck(CamcorderCam cc, RaceManager rm, string when)
        {
            int onRoad = 0, between = 0, n = 0;
            foreach (var sp in cc.Spots)
            {
                if (sp.kind == CamcorderCam.Kind.Overpass) continue;
                n++;
                int i = rm.path.NearestIndex(sp.pos);
                Vector3 p = rm.path.GetPoint(i);
                Vector3 outw = new Vector3(sp.pos.x - p.x, 0f, sp.pos.z - p.z).normalized;
                if (RoadUnder(sp.pos)) onRoad++;
                else if (RoadUnder(sp.pos + outw * 2.5f) || RoadUnder(sp.pos + outw * 6f)) between++;
            }
            CamcorderShots.Check(onRoad == 0 && between == 0, "no operator on a road or between two (" + when + ", " + n + ")",
                                 onRoad + " on, " + between + " between");
        }

        static bool RoadUnder(Vector3 at)
        {
            int mask = ~((1 << 2) | (1 << 4) | (1 << LayerMask.NameToLayer("Foliage")));
            return Physics.Raycast(new Vector3(at.x, at.y + 30f, at.z), Vector3.down, out var hit, 80f, mask,
                                   QueryTriggerInteraction.Ignore) && hit.collider.gameObject.layer == 8;
        }

        IEnumerator Shot(string name, bool quiet = false)
        {
            yield return null;
            yield return null;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "camcorder");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            var fb = cam != null ? cam.targetTexture : null;
            if (fb == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                if (!quiet) CamcorderShots.Line("    shot " + name + " not taken (no framebuffer / no graphics)");
                yield break;
            }
            var prev = RenderTexture.active;
            RenderTexture.active = fb;
            var tex = new Texture2D(fb.width, fb.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, fb.width, fb.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, CamcorderShots.tag + "_" + name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
