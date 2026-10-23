using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing.City;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// DRIVE TO THE START, played (owner, 2026-10-19). Charlotte's free roam as
    /// the CITY race card's DRIVE TO THE START hands it over (night, rain,
    /// UPTOWN LOOP): is the car put down short of the line, do the gates
    /// stand, does the HUD offer the race at the line and not 70 m back, and
    /// does pressing START load the race with the same car, hour and weather.
    /// Pictures of the gate from the spawn and from the line, and of a second
    /// gate. Writes Screenshots/race_start_shots.txt and race_start_*.png.
    /// Run as CITY (tools\race-start-shots.ps1 sets PSX_EDITION).
    /// </summary>
    public static class RaceStartShots
    {
        internal static StringBuilder log = new StringBuilder();
        internal static int failures;

        [MenuItem("PSX Racing/Race Start Gates (play mode)")]
        public static void Run()
        {
            EditionParking.RecoverIfNeeded();
            log = new StringBuilder();
            failures = 0;
            log.AppendLine("edition " + Edition.Name(Edition.Current));
            int roam = TrackCatalog.IndexOf("Charlotte");
            int uptown = TrackCatalog.IndexOf("UptownLoop");
            var scenes = EditorBuildSettings.scenes;
            int s = roam >= 0 ? TrackCatalog.SceneIndex(roam) : -1;
            if (!CityRaceStarts.Enabled) Fail("gates only stand in the CITY edition (set PSX_EDITION=CITY)");
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path)) Fail("Charlotte scene not built");
            if (failures > 0) { Finish(); EditorApplication.Exit(1); return; }

            EditorSceneManager.OpenScene(scenes[s].path);
            // What the race card's DRIVE TO THE START sends.
            if (!CityFrontEnd.FillFreeRoam(CityFrontEnd.DefaultCarId(), TimeOfDay.Night, (int)Weather.Rain, out _))
            { Fail("FillFreeRoam refused"); Finish(); EditorApplication.Exit(1); return; }
            RaceHandoff.RoamAtVenue = uptown;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("RaceStartShotsRunner").AddComponent<RaceStartShotsRunner>();
        }

        internal static void Line(string s) { log.AppendLine(s); Debug.Log("[RaceStartShots] " + s); }
        internal static void Fail(string s) { failures++; Line("FAIL " + s); }
        internal static void Check(bool ok, string what, string detail)
        {
            if (ok) Line("PASS " + what + "  (" + detail + ")"); else Fail(what + "  (" + detail + ")");
        }

        internal static void Finish()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(root, "Screenshots"));
            log.AppendLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
            File.WriteAllText(Path.Combine(root, "Screenshots", "race_start_shots.txt"), log.ToString());
        }
    }

    public class RaceStartShotsRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            // START loads the race scene; the runner has to see it through.
            DontDestroyOnLoad(gameObject);
            float t0 = Time.realtimeSinceStartup;
            CityMode mode = null;
            while (Time.realtimeSinceStartup - t0 < 40f)
            {
                mode = CityMode.Instance;
                if (mode != null && mode.Live) break;
                yield return null;
            }
            if (mode == null || mode.player == null)
            {
                RaceStartShots.Fail("no free-roam session came up");
                Done();
                yield break;
            }
            var player = mode.player;
            int uptown = TrackCatalog.IndexOf("UptownLoop");
            RaceStartShots.Check(CityRaceStarts.Gates.Count >= 3, "the gates stand in free roam",
                CityRaceStarts.Gates.Count + " gates");
            CityRaceStarts.Gate up = default;
            bool have = false;
            foreach (var g in CityRaceStarts.Gates) if (g.venue == uptown) { up = g; have = true; }
            if (!have) { RaceStartShots.Fail("no UPTOWN LOOP gate"); Done(); yield break; }

            var me = new Vector2(player.transform.position.x, player.transform.position.z);
            float toLine = Vector2.Distance(me, up.plan);
            float ahead = Vector2.Dot(up.plan - me, up.heading);
            RaceStartShots.Check(toLine > 40f && toLine < 110f && ahead > 30f,
                "DRIVE TO THE START put the car short of the line, the gate ahead",
                toLine.ToString("0") + " m from the line, " + ahead.ToString("0") + " m of it ahead");
            RaceStartShots.Check(CityRaceStarts.Prompt == null, "no START offer that far back",
                CityRaceStarts.Prompt ?? "none");
            yield return Settle(2.5f);
            yield return Shot("uptown_approach");

            // Roll up to the line: 10 m short of it, in the lane the car is in.
            var fwd = new Vector3(up.heading.x, 0f, up.heading.y);
            var side = Vector3.Cross(Vector3.up, fwd);
            float lateral = Vector3.Dot(player.transform.position - new Vector3(up.plan.x, 0f, up.plan.y), side);
            var at = new Vector3(up.plan.x, up.y + 0.45f, up.plan.y) - fwd * 10f + side * lateral;
            player.TeleportTo(at, Quaternion.LookRotation(fwd, Vector3.up));
            yield return Settle(1.5f);
            RaceStartShots.Check(CityRaceStarts.AtStart && CityRaceStarts.Prompt != null,
                "at the line the HUD offers the race", CityRaceStarts.Prompt ?? "no prompt");
            yield return Shot("uptown_atline");

            // Another gate, from its approach.
            foreach (var g in CityRaceStarts.Gates)
            {
                if (g.venue == uptown) continue;
                if (CityRaceStarts.PlaceShort(mode.world, player, g.venue))
                {
                    yield return Settle(3f);
                    yield return Shot("gate_" + TrackCatalog.At(g.venue).id);
                }
                break;
            }

            // START, as the press would: back at the 277 line, then the race.
            player.TeleportTo(at, Quaternion.LookRotation(fwd, Vector3.up));
            yield return Settle(1f);
            string spec = RaceHandoff.CarSpecId;
            var starts = Object.FindFirstObjectByType<CityRaceStarts>();
            var launch = typeof(CityRaceStarts).GetMethod("Launch",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (starts == null || launch == null) { RaceStartShots.Fail("no gate component to press"); Done(); yield break; }
            int raceScene = TrackCatalog.SceneIndex(uptown);
            launch.Invoke(starts, new object[] { uptown });
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 40f &&
                   (SceneManager.GetActiveScene().buildIndex != raceScene || RaceManager.Instance == null))
                yield return null;
            var rm = RaceManager.Instance;
            RaceStartShots.Check(rm != null && SceneManager.GetActiveScene().buildIndex == raceScene,
                "START at the gate loads the 277 race", SceneManager.GetActiveScene().name);
            RaceStartShots.Check(RaceHandoff.CarSpecId == spec && RaceHandoff.TimeOfDayIndex == TimeOfDay.Night &&
                                 RaceHandoff.WeatherOverride == (int)Weather.Rain && RaceHandoff.TrackIndex == uptown,
                "with the free-roam car, the hour and the weather",
                RaceHandoff.CarSpecId + ", hour " + RaceHandoff.TimeOfDayIndex + ", weather " + RaceHandoff.WeatherOverride);
            RaceStartShots.Check(CityRaceStarts.Gates.Count == 0, "no gates in the race scene", CityRaceStarts.Gates.Count + "");
            yield return Settle(2f);
            yield return Shot("race_grid");
            Done();
        }

        static IEnumerator Settle(float seconds)
        {
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < seconds) yield return null;
        }

        void Done()
        {
            RaceStartShots.Finish();
            EditorApplication.Exit(RaceStartShots.failures == 0 ? 0 : 1);
        }

        /// <summary>
        /// Two pictures: the PSX camera's own framebuffer as the last frame
        /// left it (the HUD is drawn into it), and the camera rendered on
        /// request (CityPlayCheck's way - the world only, but taken even when
        /// a hidden run renders no frames).
        /// </summary>
        IEnumerator Shot(string name)
        {
            yield return null;
            yield return null;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                RaceStartShots.Line("  shot " + name + " not taken (no camera / no graphics)");
                yield break;
            }
            var fb = cam.targetTexture;
            if (fb != null)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = fb;
                var tex = new Texture2D(fb.width, fb.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, fb.width, fb.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, "race_start_" + name + "_fb.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            int w = fb != null ? fb.width : 1280, h = fb != null ? fb.height : 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt ? null : prev;
                File.WriteAllBytes(Path.Combine(dir, "race_start_" + name + "_req.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            cam.targetTexture = fb;
            rt.Release();
            Object.DestroyImmediate(rt);
            RaceStartShots.Line("  shot race_start_" + name + " (" + w + "x" + h + ")");
        }
    }
}
