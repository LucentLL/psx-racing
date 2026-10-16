using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE OWNER'S AUTHORED TAIL LAMPS, photographed (2026-10-15: "please add
    /// the R32 running and brake light textures. I want to test"). Played for
    /// real - the race handoff, the R32's own shell and livery, the hour, the
    /// display blit with its halation - so each frame is the game's:
    ///   * at NIGHT, a straight rear view of the parked R32 (the field gone up
    ///     the road): OFF, RUNNING, BRAKING, BRAKING with the running lights off;
    ///   * at DUSK, the same view RUNNING and BRAKING;
    ///   * at NIGHT, one chase-cam frame of the R32 on autopilot, taken the
    ///     moment it is on the brakes at speed.
    /// And the checks the test budget asks for: CarLampMapCheck (the map lights
    /// only the four lamps), the R32 wears its map with no tail sprites, every
    /// rival without a map has no property block, and - when the sandbox holds
    /// Hidden/PSXCarPaintBefore (the shader before this change, written there
    /// by tools\r32lights-shots.ps1) - a car without a map and the R32 with its
    /// lamps off render bit for bit as they did.
    ///
    /// PSX_RL_VENUE (BlueRidge), PSX_RL_CAR (nissan_skyline_gt_r__r32___89, the
    /// gtr_89 shell), PSX_RL_HOURS ("NIGHT,DUSK"), PSX_RL_OUT (directory).
    /// Writes &lt;hour&gt;_&lt;view&gt;.png and PSXRacing_r32lights_shots.txt.
    /// Needs a device (a watched editor): -executeMethod
    /// PSXRacing.EditorTools.R32LightShots.Run
    /// </summary>
    public static class R32LightShots
    {
        static readonly Queue<int> hours = new Queue<int>();
        internal static string outDir, carId;
        static int trackIndex = -1;
        internal static bool first = true;
        internal static int fails;
        internal static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static void Run()
        {
            string venue = Env("PSX_RL_VENUE", "BlueRidge");
            carId = Env("PSX_RL_CAR", "nissan_skyline_gt_r__r32___89");
            outDir = Env("PSX_RL_OUT", Path.Combine(ProjectDir, "Screenshots", "r32lights"));
            Directory.CreateDirectory(outDir);
            log.AppendLine("R32 lamp shots: " + venue + ", " + carId);
            log.AppendLine("lamp control maps:");
            CarLampMapCheck.Run(Check);
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == venue) trackIndex = i;
            foreach (var h in Env("PSX_RL_HOURS", "NIGHT,DUSK").Split(','))
            {
                int k = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(h.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (k >= 0) hours.Enqueue(k);
            }
            if (trackIndex < 0) { Finish("no venue " + venue); return; }
            StartNext();
        }

        internal static void Check(bool ok, string what, object got)
        {
            if (!ok) fails++;
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : ""));
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
            int day = 1;
            while (day < 400 && Seasons.Of(day) != Season.Summer) day++;
            RaceHandoff.CalendarDay = day + 20;
            RaceHandoff.CarSpecId = carId;
            // Rivals on other shells: cars WITHOUT a lamp map, for the
            // "untouched" checks.
            var cars = CarCatalog.All;
            int b = Mathf.Clamp(20, 0, cars.Count - 5);
            RaceHandoff.OpponentSpecIds = cars[b + 1].id + ";" + cars[b + 2].id + ";" + cars[b + 3].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            R32LightShotsRunner.hour = hour;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("R32LightShotsRunner").AddComponent<R32LightShotsRunner>();
        }

        internal static void Next()
        {
            first = false;
            CarLights.BrakeOverride = -1f;
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
            log.AppendLine(fails == 0 ? "R32 LAMP CHECKS OK" : "R32 LAMP CHECKS: " + fails + " FAIL");
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_r32lights_shots.txt"), log.ToString());
            Debug.Log("[R32LightShots] " + log);
            EditorApplication.Exit(0);
        }
    }

    public class R32LightShotsRunner : MonoBehaviour
    {
        internal static int hour;

        IEnumerator Start()
        {
            var preset = TimeOfDay.All[hour];
            string h = preset.name.ToLowerInvariant();
            var L = R32LightShots.log;
            Camera cam = null;
            for (int i = 0; i < 600 && cam == null; i++)
            {
                var go = GameObject.Find("PSXCamera");
                if (go != null) cam = go.GetComponent<Camera>();
                yield return null;
            }
            var rm = Object.FindFirstObjectByType<RaceManager>();
            var player = rm != null ? rm.playerCar : null;
            if (cam == null || player == null) { L.AppendLine(h + ": no camera or player"); R32LightShots.Next(); yield break; }
            var lights = player.GetComponent<CarLights>();
            var body = player.GetComponent<CarBody>();
            var def = body != null ? body.Def : null;
            L.AppendLine($"{h}: player {player.name}, shell {(def != null ? def.key : "-")}");
            // The player's own input off: the parking brake and a standing
            // brake would otherwise decide the lamps; the shots set them.
            var input = player.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            player.throttleInput = 0f; player.brakeInput = 0f; player.handbrakeInput = false;

            yield return Wait(3f);
            if (R32LightShots.first) yield return Checks(cam, player, lights, body);

            // The field drives off; the R32 sits on the line.
            yield return Wait(9f);
            if (def == null || lights == null) { L.AppendLine("  no shell or lights"); R32LightShots.Next(); yield break; }

            var rig = cam.GetComponent<ChaseCamera>();
            if (rig != null) rig.enabled = false;
            float fov = cam.fieldOfView;
            var canvases = new List<Canvas>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.enabled) { c.enabled = false; canvases.Add(c); }
            var t = player.transform;
            Vector3 tl = def.tailLamp;
            Vector3 eye = t.TransformPoint(new Vector3(0f, tl.y + 0.32f, tl.z - 3.9f));
            Vector3 at = t.TransformPoint(new Vector3(0f, tl.y - 0.06f, tl.z));
            cam.fieldOfView = 30f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));

            bool night = preset.name == "NIGHT";
            var states = night
                ? new (string, bool, float)[] { ("rear_1_off", false, 0f), ("rear_2_running", true, 0f), ("rear_3_braking", true, 1f), ("rear_4_brake_only", false, 1f) }
                : new (string, bool, float)[] { ("rear_running", true, 0f), ("rear_braking", true, 1f) };
            foreach (var (name, run, brake) in states)
            {
                CarLights.SetAll(run);
                CarLights.BrakeOverride = brake;
                yield return Wait(0.6f);
                var tails = TailSprites(lights);
                L.AppendLine($"  {name}: lamp map {lights.HasLampMap}, tail sprites lit {tails}");
                yield return Grab(cam, h + "_" + name);
            }
            CarLights.BrakeOverride = -1f;
            CarLights.SetAll(preset.lightsOn);
            cam.fieldOfView = fov;
            foreach (var c in canvases) if (c != null) c.enabled = true;
            if (rig != null) rig.enabled = true;

            if (night) yield return ChaseBraking(cam, rm, player);
            R32LightShots.Next();
        }

        /// <summary>The R32 on autopilot (as RacePlayCheck drives the player),
        /// photographed from its own chase camera the moment it is braking
        /// hard at speed - the lamps as a follower sees them in a race.</summary>
        IEnumerator ChaseBraking(Camera cam, RaceManager rm, CarController player)
        {
            var L = R32LightShots.log;
            var auto = player.gameObject.AddComponent<AIDriver>();
            auto.path = rm.path;
            auto.skill = 0.9f;
            player.allowReverse = false;
            float until = Time.realtimeSinceStartup + 75f, held = 0f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                bool hard = player.brakeInput > 0.5f && player.forwardSpeed > 12f;
                held = hard ? held + Time.deltaTime : 0f;
                if (held > 0.15f) break;
            }
            if (held <= 0.15f)
            {
                L.AppendLine("  chase: no hard braking in 75 s - brake held by the tool");
                CarLights.BrakeOverride = 1f;
                yield return Wait(0.5f);
            }
            L.AppendLine($"  chase: {player.forwardSpeed * 3.6f:0} km/h, brake {player.brakeInput:0.00}, tail sprites lit {TailSprites(player.GetComponent<CarLights>())}");
            yield return Grab(cam, TimeOfDay.All[hour].name.ToLowerInvariant() + "_chase_braking");
            CarLights.BrakeOverride = -1f;
        }

        /// <summary>The first session's checks, on the grid.</summary>
        IEnumerator Checks(Camera cam, CarController player, CarLights lights, CarBody body)
        {
            var C = (System.Action<bool, string, object>)R32LightShots.Check;
            R32LightShots.log.AppendLine("in play:");
            C(lights != null && lights.HasLampMap, "the R32 wears its lamp control map", null);
            C(lights != null && TailSprites(lights) == 0, "...and its tail-lens sprites stay off", lights != null ? TailSprites(lights) : -1);
            int rivals = 0, blocked = 0, wrongSprites = 0;
            CarLights rival = null;
            foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsSortMode.None))
            {
                if (l == lights) continue;
                var b = l.GetComponent<CarBody>();
                rivals++;
                if (l.HasLampMap || (b != null && b.bodyRenderer != null && b.bodyRenderer.HasPropertyBlock())) blocked++;
                else if (rival == null && b != null && b.bodyRenderer != null) rival = l;
                // A car without a map keeps today's sprites: lit with the
                // running lights (or a brake).
                if (TimeOfDay.All[hour].lightsOn && TailSprites(l) != 2) wrongSprites++;
            }
            C(rivals > 0 && blocked == 0, "rivals without a map: no lamp map, no property block on the body", $"{blocked} of {rivals}");
            C(wrongSprites == 0, "...and their tail-lens sprites run as before", wrongSprites);

            var before = Shader.Find("Hidden/PSXCarPaintBefore");
            if (before == null) { R32LightShots.log.AppendLine("  (no Hidden/PSXCarPaintBefore in the sandbox: bit-identity not checked)"); yield break; }
            var ab = new GameObject("R32LightShotsAB").AddComponent<Camera>();
            ab.CopyFrom(cam);
            ab.targetTexture = null;
            ab.enabled = false;
            if (rival != null)
            {
                int d = ABDiff(ab, rival.transform, rival.GetComponent<CarBody>(), before, out int n);
                C(d == 0, "a car without a map renders bit for bit as before (PSX/CarPaint vs the old shader)", $"max diff {d}, {n} px differ");
            }
            // The R32 with its lamps out: the block is skipped.
            CarLights.SetAll(false);
            CarLights.BrakeOverride = 0f;
            yield return Wait(0.3f);
            int d2 = ABDiff(ab, player.transform, body, before, out int n2);
            C(d2 == 0, "the R32 with its lamps off renders bit for bit as before", $"max diff {d2}, {n2} px differ");
            CarLights.BrakeOverride = -1f;
            CarLights.SetAll(TimeOfDay.All[hour].lightsOn);
            Destroy(ab.gameObject);
        }

        /// <summary>Render a car's tail from 5 m with its body in PSX/CarPaint and
        /// then in the old shader, the same frame; the largest channel difference.</summary>
        static int ABDiff(Camera ab, Transform car, CarBody body, Shader before, out int differ)
        {
            differ = -1;
            var r = body != null ? body.bodyRenderer : null;
            var def = body != null ? body.Def : null;
            if (r == null || def == null) return 255;
            Vector3 tl = def.tailLamp;
            Vector3 eye = car.TransformPoint(new Vector3(0.6f, tl.y + 0.4f, tl.z - 5f));
            ab.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(car.TransformPoint(new Vector3(0f, tl.y, tl.z + 0.8f)) - eye));
            ab.fieldOfView = 35f;
            var orig = r.sharedMaterial;
            var a = RenderTo(ab);
            var tmp = new Material(orig) { shader = before };
            r.sharedMaterial = tmp;
            var bpx = RenderTo(ab);
            r.sharedMaterial = orig;
            Object.DestroyImmediate(tmp);
            if (a == null || bpx == null || a.Length != bpx.Length) return 255;
            int max = 0;
            differ = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int m = Mathf.Max(Mathf.Max(Mathf.Abs(a[i].r - bpx[i].r), Mathf.Abs(a[i].g - bpx[i].g)),
                                  Mathf.Max(Mathf.Abs(a[i].b - bpx[i].b), Mathf.Abs(a[i].a - bpx[i].a)));
                if (m > 0) differ++;
                max = Mathf.Max(max, m);
            }
            return max;
        }

        static Color32[] RenderTo(Camera cam)
        {
            var rt = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            Color32[] px = null;
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = rt;
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                px = tex.GetPixels32();
                Object.DestroyImmediate(tex);
            }
            rt.Release();
            Object.DestroyImmediate(rt);
            return px;
        }

        /// <summary>How many of a car's two tail-lens sprites are drawn.</summary>
        static int TailSprites(CarLights l)
        {
            int n = 0;
            if (l == null) return -1;
            foreach (var mr in l.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.name.StartsWith("Taillight") && mr.enabled) n++;
            return n;
        }

        static IEnumerator Wait(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>The frame the player sees: the camera's target through the
        /// display blit (grade, halation by the emitter mask, colour depth).</summary>
        IEnumerator Grab(Camera cam, string name)
        {
            yield return null;
            yield return null;
            var rt = cam.targetTexture;
            if (rt == null) { R32LightShots.log.AppendLine("  " + name + ": camera has no target"); yield break; }
            var shown = PSXScreenshotTool.Dithered(rt);
            var prev = RenderTexture.active;
            RenderTexture.active = shown;
            var tex = new Texture2D(shown.width, shown.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, shown.width, shown.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string file = Path.Combine(R32LightShots.outDir, name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            if (shown != rt) { shown.Release(); Destroy(shown); }
            R32LightShots.log.AppendLine($"  shot {file} ({shown.width}x{shown.height})");
        }
    }
}
