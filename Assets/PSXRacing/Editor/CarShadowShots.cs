using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CAR'S SUN SHADOW, photographed (2026-10-15). The owner's own frame
    /// to compare against: Linn Cove, the red Viper, chase camera, noon, a few
    /// seconds after the start with the field gone up the road. Played for
    /// real - the race handoff, the car's own shell and livery, the chase rig
    /// at rest - so the frame is the game's, and the same before and after a
    /// change. Per hour: "grid" (on the grid with the rivals ahead), "start"
    /// (the owner's frame, once the rivals have left), at the first hour
    /// "side" (a three-quarter view from beside the car, the rig parked),
    /// "bridge"/"bridgehigh" (the car on the longest bridge span, chase and
    /// from high on the sun's side), and under a low sun "open"/"openhigh" (a
    /// stretch the sun reaches, running across its rays: Linn Cove's viaduct
    /// is in the mountain's shade at sunset and its parapet's at dawn).
    ///
    /// PSX_CS_VENUE (BlueRidge), PSX_CS_CAR (dodge_viper_gts__99, the viper_gts shell), PSX_CS_HOURS
    /// ("noon,sunset"), PSX_CS_OUT (directory), PSX_CS_PREFIX (before|after).
    /// Writes &lt;prefix&gt;_&lt;hour&gt;_&lt;view&gt;.png and PSXRacing_carshadow_shots.txt.
    /// Needs a device (a watched editor): -executeMethod
    /// PSXRacing.EditorTools.CarShadowShots.Run
    /// </summary>
    public static class CarShadowShots
    {
        static readonly Queue<int> hours = new Queue<int>();
        internal static string outDir, prefix, carId;
        static int trackIndex = -1;
        internal static bool first = true;
        internal static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static void Run()
        {
            string venue = Env("PSX_CS_VENUE", "BlueRidge");
            carId = Env("PSX_CS_CAR", "dodge_viper_gts__99");
            outDir = Env("PSX_CS_OUT", Path.Combine(ProjectDir, "Screenshots", "carshadow"));
            prefix = Env("PSX_CS_PREFIX", "shot");
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == venue) trackIndex = i;
            foreach (var h in Env("PSX_CS_HOURS", "noon,sunset").Split(','))
            {
                int k = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(h.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (k >= 0) hours.Enqueue(k);
            }
            log.AppendLine("car shadow shots: " + venue + ", " + carId + ", " + prefix);
            if (trackIndex < 0) { Finish("no venue " + venue); return; }
            StartNext();
        }

        static string Env(string k, string d)
        {
            string v = System.Environment.GetEnvironmentVariable(k);
            return string.IsNullOrEmpty(v) ? d : v;
        }

        static void StartNext()
        {
            if (hours.Count == 0) { Finish("done"); return; }
            int hour = hours.Dequeue();
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(trackIndex);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Finish("venue not built"); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = trackIndex;
            RaceHandoff.TimeOfDayIndex = hour;
            RaceHandoff.WeatherOverride = (int)Weather.Clear;
            RaceHandoff.TrafficLevel = 0;
            // A summer day: the owner's frame is green.
            int day = 1;
            while (day < 400 && Seasons.Of(day) != Season.Summer) day++;
            RaceHandoff.CalendarDay = day + 20;
            RaceHandoff.CarSpecId = carId;
            var cars = CarCatalog.All;
            int b = Mathf.Clamp(20, 0, cars.Count - 5);
            RaceHandoff.OpponentSpecIds = cars[b + 1].id + ";" + cars[b + 2].id + ";" + cars[b + 3].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            CarShadowShotsRunner.hour = hour;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CarShadowShotsRunner").AddComponent<CarShadowShotsRunner>();
        }

        internal static void Next()
        {
            first = false;
            EditorApplication.playModeStateChanged += OnExited;
            EditorApplication.ExitPlaymode();
        }

        static void OnExited(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnExited;
            EditorApplication.delayCall += StartNext;
        }

        static void Finish(string why)
        {
            log.AppendLine(why);
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_carshadow_shots.txt"), log.ToString());
            Debug.Log("[CarShadowShots] " + log);
            EditorApplication.Exit(0);
        }
    }

    public class CarShadowShotsRunner : MonoBehaviour
    {
        internal static int hour;

        IEnumerator Start()
        {
            string h = TimeOfDay.All[hour].name.ToLowerInvariant();
            var L = CarShadowShots.log;
            Camera cam = null;
            for (int i = 0; i < 600 && cam == null; i++)
            {
                var go = GameObject.Find("PSXCamera");
                if (go != null) cam = go.GetComponent<Camera>();
                yield return null;
            }
            var rm = Object.FindFirstObjectByType<RaceManager>();
            var player = rm != null ? rm.playerCar : null;
            if (cam == null || player == null) { L.AppendLine(h + ": no camera or player"); CarShadowShots.Next(); yield break; }
            L.AppendLine(h + ": player " + player.name + ", sun dir " + Shader.GetGlobalVector("_PSXLightDir"));

            yield return Wait(3f);
            yield return Grab(cam, h + "_grid");
            // The field drives off; the player sits on the line, as in the
            // owner's frame (0'07" on the clock, 0 mph).
            yield return Wait(9f);
            float sum = 0f, worst = 0f; int n = 0;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                sum += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); n++;
            }
            L.AppendLine($"  frame {1000f * sum / Mathf.Max(1, n):0.0} ms mean, {1000f * worst:0.0} worst over {n} (editor)");
            L.AppendLine($"  sun map: {SunShadows.DrawnLast} casters, {SunShadows.DrawCallsLast} calls; strength {SunShadows.Strength:0.00}");
            AppendCarMap(L);
            yield return Grab(cam, h + "_start");

            if (CarShadowShots.first)
            {
                var rig = cam.GetComponent<ChaseCamera>();
                if (rig != null) rig.enabled = false;
                var t = player.transform;
                Vector3 eye = t.TransformPoint(new Vector3(-4.2f, 1.7f, 3.6f));
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(t.position + Vector3.up * 0.4f - eye));
                yield return Grab(cam, h + "_side");
                if (rig != null) rig.enabled = true;
            }

            // In the open: the middle of the venue's longest bridge span (Linn
            // Cove's viaduct); and under a LOW sun, a stretch of road the
            // sun reaches (no terrain, wall or trunk on the way to it, for
            // rays from both lanes) that runs across the sun's rays, so the
            // long shadow lies sideways where the chase camera sees it.
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            TrackCatalog.EnsureStage(def);
            var path = rm.path;
            if (path != null && path.Count > 8)
            {
                if (def.bridges != null && def.bridges.Length > 0)
                {
                    Vector2 best = def.bridges[0];
                    foreach (var b in def.bridges) if (b.y - b.x > best.y - best.x) best = b;
                    int i = Mathf.Clamp(Mathf.RoundToInt((best.x + best.y) * 0.5f / path.spacing), 1, path.Count - 2);
                    L.AppendLine($"  bridge wp {i} ({best.x:0}-{best.y:0} m)");
                    yield return Spot(cam, player, path, i, h + "_bridge");
                }
                Vector3 toSun = Shader.GetGlobalVector("_PSXLightDir");
                if (toSun.y < 0.26f)
                {
                    int open = OpenStretch(path, toSun.normalized);
                    L.AppendLine("  open road for the low sun: wp " + open);
                    if (open > 0) yield return Spot(cam, player, path, open, h + "_open");
                }
            }
            CarShadowShots.Next();
        }

        /// <summary>The car on waypoint i (right-hand lane, facing the way
        /// the race runs), shot from the chase rig at rest and from high on
        /// the sun's side (the long shadow runs away from the eye).</summary>
        IEnumerator Spot(Camera cam, CarController player, TrackPath path, int i, string name)
        {
            var t = player.transform;
            float lift = t.position.y - path.GetPoint(path.NearestIndex(t.position)).y;
            Vector3 at = path.GetPoint(i);
            Vector3 fwd = path.GetPoint(i + 1) - path.GetPoint(i - 1); fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            player.TeleportTo(at + right * (path.roadWidth * 0.25f) + Vector3.up * lift, Quaternion.LookRotation(fwd, Vector3.up));
            yield return Wait(3f);
            yield return Grab(cam, name);
            var rig = cam.GetComponent<ChaseCamera>();
            if (rig != null) rig.enabled = false;
            Vector3 toSun = Shader.GetGlobalVector("_PSXLightDir");
            float side = Vector3.Dot(toSun, t.right) > 0f ? 1f : -1f;
            Vector3 eye = t.TransformPoint(new Vector3(side * 7f, 6f, -5f));
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(t.position + t.forward * 1.5f - eye));
            yield return Grab(cam, name + "high");
            if (rig != null) rig.enabled = true;
        }

        /// <summary>The waypoint (past the first 200 m) where the road runs
        /// most nearly across a low sun and nothing solid stands between it
        /// and the road; 0 if none.</summary>
        static int OpenStretch(TrackPath path, Vector3 toSun)
        {
            Vector3 sunFlat = new Vector3(toSun.x, 0f, toSun.z).normalized;
            int best = 0;
            float bestScore = 0.5f;
            for (int i = 50; i < path.Count - 50; i += 5)
            {
                Vector3 fwd = path.GetPoint(i + 1) - path.GetPoint(i - 1); fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                float across = 1f - Mathf.Abs(Vector3.Dot(fwd, sunFlat));
                if (across <= bestScore) continue;
                bool clear = true;
                for (int k = -2; k <= 2 && clear; k++)
                    for (int s = -1; s <= 1 && clear; s += 2)
                    {
                        Vector3 p = path.GetPoint(i + k) + right * (s * path.roadWidth * 0.3f) + Vector3.up * 1.0f;
                        if (Physics.Raycast(p, toSun, 600f, ~0, QueryTriggerInteraction.Ignore)) clear = false;
                    }
                if (!clear) continue;
                best = i;
                bestScore = across;
            }
            return best;
        }

        /// <summary>The car map's numbers, when this build has one (by
        /// reflection: the BEFORE run is a build without it).</summary>
        static void AppendCarMap(System.Text.StringBuilder L)
        {
            var t = typeof(SunShadows);
            var cars = t.GetProperty("CarsLast");
            var calls = t.GetProperty("CarCallsLast");
            if (cars == null || calls == null) { L.AppendLine("  car map: none in this build"); return; }
            L.AppendLine($"  car map: {cars.GetValue(null)} cars, {calls.GetValue(null)} calls");
        }

        static IEnumerator Wait(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        IEnumerator Grab(Camera cam, string name)
        {
            yield return null;
            yield return null;
            var rt = cam.targetTexture;
            if (rt == null) { CarShadowShots.log.AppendLine("  " + name + ": camera has no target"); yield break; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string file = Path.Combine(CarShadowShots.outDir, CarShadowShots.prefix + "_" + name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            CarShadowShots.log.AppendLine($"  shot {file} ({rt.width}x{rt.height})");
        }
    }
}
