using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TERRAIN DETAIL LAYER, photographed in a real race start (the GT4
    /// study, gap 4): the owner's Linn Cove frame (red Viper, chase camera,
    /// noon, the field gone) and the car parked on a spread of waypoints, once
    /// per dress. The same frames before and after a change, so a pair is a
    /// pair (tools/terraindetail-shots.ps1; tools/terraindetail/detail_metric.py
    /// measures them).
    ///
    /// PSX_TD_VENUE (BlueRidge), PSX_TD_CAR (dodge_viper_gts__99),
    /// PSX_TD_DRESSES ("summer,snow"; snow = the snow dress under a snowy sky,
    /// the only sky that wears it), PSX_TD_SPOTS (waypoint FRACTIONS of the
    /// route, "0.15,0.3,0.45,0.6,0.75,0.9"), PSX_TD_HOUR (noon),
    /// PSX_TD_OUT (directory), PSX_TD_PREFIX (before|after).
    ///
    /// PSXRacing.EditorTools.TerrainDetailShots.Run
    /// </summary>
    public static class TerrainDetailShots
    {
        static readonly System.Collections.Generic.Queue<string> dresses = new System.Collections.Generic.Queue<string>();
        internal static string outDir, prefix, carId, dress;
        internal static float[] spots;
        internal static int hour;
        static int trackIndex = -1;
        internal static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static void Run()
        {
            string venue = Env("PSX_TD_VENUE", "BlueRidge");
            carId = Env("PSX_TD_CAR", "dodge_viper_gts__99");
            outDir = Env("PSX_TD_OUT", Path.Combine(ProjectDir, "Screenshots", "terraindetail"));
            prefix = Env("PSX_TD_PREFIX", "shot");
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == venue) trackIndex = i;
            hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(Env("PSX_TD_HOUR", "noon"), System.StringComparison.OrdinalIgnoreCase));
            if (hour < 0) hour = TimeOfDay.Noon;
            var list = new System.Collections.Generic.List<float>();
            foreach (var s in Env("PSX_TD_SPOTS", "0.15,0.3,0.45,0.6,0.75,0.9").Split(','))
                if (float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) list.Add(f);
            spots = list.ToArray();
            foreach (var d in Env("PSX_TD_DRESSES", "summer,snow").Split(',')) dresses.Enqueue(d.Trim().ToLowerInvariant());
            log.AppendLine("terrain detail shots: " + venue + ", " + carId + ", " + prefix);
            // the layer's self-test lines first (PSX_TD_SELFTEST=0 skips them)
            if (Env("PSX_TD_SELFTEST", "1") != "0")
            {
                System.Action<bool, string, object> line = (ok, what, got) =>
                    log.AppendLine((ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : ""));
                TerrainDetailTool.SelfTest(line);
                LifeSimSelfTest.DappleSelfTest(line);
            }
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
            if (dresses.Count == 0) { Finish("done"); return; }
            // A dress may carry its own weather and hour: "summer+rain@sunset"
            // (Weather and TimeOfDay names); the shots are named with them,
            // "summer_rain_sunset_wp1630". Plain "summer"/"snow" are as before.
            string token = dresses.Dequeue();
            string[] at = token.Split('@');
            string[] plus = at[0].Split('+');
            dress = token.Replace('+', '_').Replace('@', '_');
            int dressHour = hour;
            if (at.Length > 1)
            {
                int h = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(at[1], System.StringComparison.OrdinalIgnoreCase));
                if (h >= 0) dressHour = h;
            }
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(trackIndex);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) { Finish("venue not built"); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = trackIndex;
            RaceHandoff.TimeOfDayIndex = dressHour;
            bool snow = plus[0] == "snow";
            // The snow dress is worn under a snowy sky only (Seasons.DressIndex).
            Weather wx = snow ? Weather.Snow : Weather.Clear;
            if (plus.Length > 1 && System.Enum.TryParse(plus[1], true, out Weather w)) wx = w;
            RaceHandoff.WeatherOverride = (int)wx;
            RaceHandoff.TrafficLevel = 0;
            Season want = snow ? Season.Winter : Season.Summer;
            int day = 1;
            while (day < 400 && Seasons.Of(day) != want) day++;
            RaceHandoff.CalendarDay = day + 20;
            RaceHandoff.CarSpecId = carId;
            var cars = CarCatalog.All;
            int b = Mathf.Clamp(20, 0, cars.Count - 5);
            RaceHandoff.OpponentSpecIds = cars[b + 1].id + ";" + cars[b + 2].id + ";" + cars[b + 3].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TerrainDetailShotsRunner").AddComponent<TerrainDetailShotsRunner>();
        }

        internal static void Next()
        {
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
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_terraindetail_shots.txt"), log.ToString());
            Debug.Log("[TerrainDetailShots] " + log);
            EditorApplication.Exit(0);
        }
    }

    public class TerrainDetailShotsRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            string d = TerrainDetailShots.dress;
            var L = TerrainDetailShots.log;
            Camera cam = null;
            for (int i = 0; i < 600 && cam == null; i++)
            {
                var go = GameObject.Find("PSXCamera");
                if (go != null) cam = go.GetComponent<Camera>();
                yield return null;
            }
            var rm = Object.FindFirstObjectByType<RaceManager>();
            var player = rm != null ? rm.playerCar : null;
            if (cam == null || player == null) { L.AppendLine(d + ": no camera or player"); TerrainDetailShots.Next(); yield break; }
            L.AppendLine($"{d}: dress {SeasonDress.AppliedDress}, weather {Seasons.CurrentWeather}, textures {(TexturePrefs.Enhanced ? "ENHANCED" : "CLASSIC")}");
            // The field drives off; the player sits on the line, as in the
            // owner's frame.
            yield return Wait(12f);
            float sum = 0f, worst = 0f; int n = 0;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                sum += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); n++;
            }
            L.AppendLine($"  frame {1000f * sum / Mathf.Max(1, n):0.0} ms mean, {1000f * worst:0.0} worst over {n} (editor)");
            yield return Grab(cam, d + "_start");

            var path = rm.path;
            if (path != null && path.Count > 8)
            {
                for (int k = 0; k < TerrainDetailShots.spots.Length; k++)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(TerrainDetailShots.spots[k] * (path.Count - 1)), 1, path.Count - 2);
                    yield return Spot(cam, player, path, i, d + "_wp" + i);
                }
            }
            TerrainDetailShots.Next();
        }

        /// <summary>The car on waypoint i (right-hand lane, facing the way
        /// the race runs), shot from the chase rig at rest.</summary>
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
            if (rt == null) { TerrainDetailShots.log.AppendLine("  " + name + ": camera has no target"); yield break; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string file = Path.Combine(TerrainDetailShots.outDir, TerrainDetailShots.prefix + "_" + name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            TerrainDetailShots.log.AppendLine($"  shot {file} ({rt.width}x{rt.height})");
        }
    }
}
