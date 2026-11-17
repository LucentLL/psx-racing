using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OWNER'S TWO DRIVES (2026-11-16: "test in Unity with 3D trees enabled.
    /// Drive around Charlotte in Winter and a mountain track in Fall to see both
    /// of my complaints"). PLAY mode, the real scene, the real player car on the
    /// autopilot (as RacePlayCheck drives it), alone on the road, on a calendar
    /// day of the season asked for, the sky clear - and a frame of the game's
    /// own view every PSX_TSD_EVERY seconds of the drive, kept.
    ///
    ///   -executeMethod PSXRacing.EditorTools.TreeSeasonDrive.Run     (no -quit: it plays)
    ///   PSX_TSD_VENUE=SwissNC226A  PSX_TSD_DAY=288  PSX_TSD_HOUR=noon  PSX_TSD_SECONDS=90  PSX_TSD_EVERY=0.5
    ///   PSX_TSD_TAG=name (frames: Screenshots/treeseason/&lt;tag&gt;/drive_&lt;venue&gt;_&lt;n&gt;.png)  PSX_TSD_VIEW=chase|cockpit
    /// Watched (a visible editor) it plays in real time; hidden (batch mode, with
    /// a graphics device) the clock is stepped 30 frames a game second, so the
    /// frames stand the same distance apart however long each takes to draw.
    /// No WaitForEndOfFrame anywhere; a hard time limit ends it whatever happens.
    /// Report: PSXRacing_treeseason_drive_&lt;tag&gt;.txt
    /// </summary>
    public static class TreeSeasonDrive
    {
        internal static StringBuilder log = new StringBuilder();
        internal static string tag, venue, view;
        internal static float seconds, every;
        internal static double hardUntil;
        internal static bool done;

        static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        public static void Run()
        {
            tag = Env("PSX_TSD_TAG", "drive");
            venue = Env("PSX_TSD_VENUE", "SwissNC226A");
            view = Env("PSX_TSD_VIEW", "chase").ToLowerInvariant();
            seconds = float.Parse(Env("PSX_TSD_SECONDS", "90"), CultureInfo.InvariantCulture);
            every = Mathf.Max(0.1f, float.Parse(Env("PSX_TSD_EVERY", "0.5"), CultureInfo.InvariantCulture));
            int day = int.Parse(Env("PSX_TSD_DAY", "288"), CultureInfo.InvariantCulture);
            log = new StringBuilder($"TREE SEASON DRIVE {venue} ({tag}) {System.DateTime.Now:yyyy-MM-dd HH:mm}; day {day} = {LifeSim.LifeRules.DateLabel(day)}; PSX_TREE_MATCH={Env("PSX_TREE_MATCH", "(default)")}; " +
                                    $"{(Application.isBatchMode ? "hidden (batch mode)" : "watched")}\n");
            done = false;
            // the hard limit: loading, the drive at a crawl, and then some
            hardUntil = EditorApplication.timeSinceStartup + 240.0 + seconds * 12.0;
            EditorApplication.update += Watchdog;
            try
            {
                int index = TrackCatalog.IndexOf(venue);
                if (index < 0) throw new System.Exception("no venue " + venue);
                var def = TrackCatalog.At(index);
                string path = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
                if (!File.Exists(path)) throw new System.Exception("no scene " + path);
                Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "treeseason", tag));
                EditorSceneManager.OpenScene(path);
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;
                RaceHandoff.TrackIndex = index;
                RaceHandoff.CalendarDay = day;
                int hourIdx = TimeOfDay.Noon;
                string hour = Env("PSX_TSD_HOUR", "noon");
                for (int h = 0; h < TimeOfDay.All.Length; h++)
                    if (string.Equals(TimeOfDay.All[h].name, hour, System.StringComparison.OrdinalIgnoreCase)) hourIdx = h;
                RaceHandoff.TimeOfDayIndex = hourIdx;
                RaceHandoff.WeatherOverride = (int)Weather.Clear;
                RaceHandoff.Solo = true;
                RaceHandoff.TrafficLevel = TrafficLevels.None;
                var cars = CarCatalog.All;
                RaceHandoff.CarSpecId = cars[Mathf.Clamp(20, 0, cars.Count - 1)].id;
                ForestDetailPrefs.Override = Mathf.Clamp(int.Parse(Env("PSX_TSD_DETAIL", "1"), CultureInfo.InvariantCulture), 0, 2);
                log.AppendLine($"  hour {TimeOfDay.All[hourIdx].name}, sky forced clear, solo, no traffic, car {RaceHandoff.CarSpecId}, FOREST DETAIL {ForestDetailPrefs.Label}, profile PC {PlatformProfile.PC}, season {Seasons.Of(day)}");
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.playModeStateChanged += OnState;
                EditorApplication.EnterPlaymode();
            }
            catch (System.Exception e)
            {
                log.AppendLine("FAILED before play: " + e);
                Finish(1);
            }
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
                new GameObject("TreeSeasonDriveRunner").AddComponent<TreeSeasonDriveRunner>();
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnState;
                Finish(done ? 0 : 2);
            }
        }

        static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < hardUntil) return;
            log.AppendLine("HARD TIME LIMIT: ended by the watchdog");
            Finish(3);
        }

        internal static void Finish(int code)
        {
            EditorApplication.update -= Watchdog;
            ForestDetailPrefs.Override = -1;
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_treeseason_drive_" + tag + ".txt"), log.ToString());
            Debug.Log("[TreeSeasonDrive]\n" + log);
            EditorApplication.Exit(code);
        }
    }

    // (after every camera script of the frame, and after the forests queued their draws: 1400, 1500)
    [DefaultExecutionOrder(20000)]
    public class TreeSeasonDriveRunner : MonoBehaviour
    {
        RaceManager rm;
        CarController car;
        Rigidbody body;
        bool live;
        float t0, nextShot;
        int shots;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            if (rm == null || car == null || rm.path == null) { TreeSeasonDrive.log.AppendLine("  NO race, player or path in the scene"); End(); yield break; }
            if (Application.isBatchMode) { QualitySettings.vSyncCount = 0; Time.captureFramerate = 30; }
            float until = Time.realtimeSinceStartup + 30f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            TreeSeasonDrive.log.AppendLine($"  race state {rm.State}; dress {Seasons.DressNames[Mathf.Clamp(SeasonDress.AppliedDress, 0, Seasons.DressCount - 1)]}; road grip x{Seasons.RoadGripMult:0.00}");
            if (TreeSeasonDrive.view == "cockpit") ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            else if (TreeSeasonDrive.view == "chase") ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            var auto = car.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.9f;
            car.allowReverse = false;
            body = car.GetComponent<Rigidbody>();
            foreach (var f in FindObjectsByType<MeshForest>(FindObjectsInactive.Exclude))
                TreeSeasonDrive.log.AppendLine($"  mesh forest '{f.name}': {(f.variants != null ? f.variants.Length : 0)} species, levels {f.lod0To:0} / {f.lod1To:0} / {f.meshRange:0} m, bands {f.band0:0} / {f.band1:0} / {f.band2:0} m, match cards on {f.MatchCount}");
            t0 = Time.time;
            nextShot = t0 + 1f;
            live = true;
            TreeSeasonDrive.log.AppendLine("  frame, game s, road m, km/h");
        }

        void LateUpdate()
        {
            if (!live) return;
            float t = Time.time - t0;
            if (t >= TreeSeasonDrive.seconds) { live = false; TreeSeasonDrive.done = true; End(); return; }
            if (Time.time < nextShot) return;
            nextShot += TreeSeasonDrive.every;
            var cam = Camera.main;
            if (cam == null) return;
            var p = rm.GetProgress(car);
            float m = p != null ? p.nearestIdx * rm.path.spacing : 0f;
            float kmh = body != null ? body.linearVelocity.magnitude * 3.6f : 0f;
            string name = $"treeseason/{TreeSeasonDrive.tag}/drive_{TreeSeasonDrive.venue}_{shots:000}";
            PSXScreenshotTool.ShotAs(cam, name, cam.transform.position, cam.transform.rotation);
            TreeSeasonDrive.log.AppendLine($"  {shots:000}, {t:0.0}, {m:0}, {kmh:0}");
            shots++;
        }

        void End()
        {
            TreeSeasonDrive.log.AppendLine($"{shots} frames\nTREE SEASON DRIVE {(TreeSeasonDrive.done ? "OK" : "ENDED EARLY")}");
            Time.captureFramerate = 0;
            EditorApplication.ExitPlaymode();
        }
    }
}
