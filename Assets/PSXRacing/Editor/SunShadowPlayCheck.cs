using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SUN'S SHADOW MAP, IN THE RUNNING GAME (the day pass, 2026-09-21).
    ///
    /// DayLookShots photographs the maps in edit mode, where every mesh is its
    /// own mesh. The GAME is not that: on entering play Unity folds every
    /// static renderer into combined meshes, and SunShadows redraws renderers
    /// by hand (CommandBuffer.DrawRenderer) - the one call in the pass whose
    /// behaviour on a batched renderer cannot be read off the page. If it drew
    /// them in the wrong place the shadows would be wrong in exactly the build
    /// the player has and right in every tool. So this enters play mode on a
    /// built circuit, lets the hook draw the maps for the real chase camera,
    /// reads the sun's map back and asks it about places whose answer is known:
    ///
    ///   * THE ROAD IS WHERE THE ROAD IS. Points ray-cast onto the road round
    ///     the car: the map's first surface there must never be FARTHER down
    ///     the ray than the road (a road drawn somewhere else leaves a hole),
    ///     and at most of them it must be the road itself.
    ///   * THE CAR IS IN IT. At the car's roof the first surface is the roof,
    ///     not the road under it - the car casts, and casts where it is.
    ///   * IT MOVES WITH THE CAR. After a teleport down the road the same two
    ///     answers hold at the new place: the map is not a picture of the grid.
    ///   * AND IT GOES OFF. At night no map is drawn and the strength the
    ///     shaders read is zero.
    ///
    ///   tools\sunshadow-play-check.ps1 -> PSXRacing_sunshadow_play_check.txt
    /// </summary>
    public static class SunShadowPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Sun Shadows (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var def = TrackCatalog.At(0);
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + def.id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            // Day 0 is clear; the afternoon sun is 38 degrees up.
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Afternoon;
            var cars = CarCatalog.All;
            if (cars.Count > 2)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id;
                RaceHandoff.OpponentSkills = "0.9";
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("SunShadowPlayCheckRunner").AddComponent<SunShadowPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE SUN CASTS SHADOWS WHERE THINGS ARE." : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_sunshadow_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class SunShadowPlayCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            // Frames, never WaitForEndOfFrame: batch mode has no game view, so
            // an end-of-frame never comes and the check would sit in play mode
            // until its runner timed out (it did, the first time).
            for (int i = 0; i < 25; i++) yield return null;

            var rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            SunShadowPlayCheck.Check(car != null, "there is a player car");
            if (car == null) { Done(); yield break; }

            SunShadowPlayCheck.Check(SunShadows.Strength > 0.9f, "a clear afternoon turns the sun's map on",
                                     SunShadows.Strength.ToString("0.00"));
            SunShadowPlayCheck.Check(SunShadows.SkyStrength > 0.9f, "and the sky's", SunShadows.SkyStrength.ToString("0.00"));
            SunShadowPlayCheck.Check(SunShadows.Map != null && SunShadows.DrawnLast > 0,
                                     "the hook drew it for the game's own camera",
                                     SunShadows.DrawnLast + " casters, " + SunShadows.DrawCallsLast + " draw calls, of " + SunShadows.Known + " known");
            int batched = 0, all = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                all++;
                if (r.isPartOfStaticBatch) batched++;
            }
            SunShadowPlayCheck.Note(batched + " of " + all + " mesh renderers are statically batched in play" +
                                    (batched == 0 ? " (so this run does not exercise batched casters)" : ""));
            if (SunShadows.Map == null) { Done(); yield break; }

            yield return Probe(car, "at the grid");

            // Down the road: the map must follow.
            var path = FindFirstObjectByType<TrackPath>();
            if (path != null && path.Count > 60)
            {
                int i = path.Wrap(45);
                Vector3 fwd = path.GetTangent(i).normalized;
                car.TeleportTo(path.GetPoint(i) + Vector3.up * 0.4f, Quaternion.LookRotation(fwd, Vector3.up));
                for (int f = 0; f < 50; f++) yield return null;
                yield return Probe(car, "180 m down the road");
            }

            // And off, at night.
            var sun = GameObject.Find("Sun");
            TimeOfDay.Apply(TimeOfDay.Night, sun != null ? sun.GetComponent<Light>() : null);
            for (int f = 0; f < 5; f++) yield return null;
            SunShadowPlayCheck.Check(SunShadows.Strength == 0f && SunShadows.SkyStrength == 0f,
                                     "night switches both maps off");
            SunShadowPlayCheck.Check(Shader.GetGlobalVector("_PSXShadowParams").x == 0f && Shader.GetGlobalVector("_PSXSkyParams").x == 0f,
                                     "and the strengths the shaders read are zero");
            Done();
        }

        /// <summary>Read the sun's map back and ask it about the road round
        /// the car and about the car's own roof.</summary>
        IEnumerator Probe(CarController car, string where)
        {
            var map = SunShadows.Map;
            var tex = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false, true);
            var keep = RenderTexture.active;
            RenderTexture.active = map;
            tex.ReadPixels(new Rect(0, 0, map.width, map.height), 0, 0);
            tex.Apply();
            RenderTexture.active = keep;
            var px = tex.GetPixels32();
            Destroy(tex);
            Matrix4x4 m = SunShadows.WorldToMap;
            float range = SunShadows.TowardSunM + SunShadows.AwayM;

            float Depth(Vector3 world, out float want)
            {
                Vector3 s = m.MultiplyPoint(world);
                want = s.z;
                int x = Mathf.Clamp(Mathf.FloorToInt(s.x * map.width), 0, map.width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(s.y * map.height), 0, map.height - 1);
                var c = px[y * map.width + x];
                return c.r / 255f + c.g / 255f / 255f + c.b / 255f / 65025f;
            }

            var t = car.transform;
            int asked = 0, holes = 0, onRoad = 0;
            float worstHole = 0f;
            foreach (var off in new[] { new Vector3(0, 0, 12), new Vector3(3, 0, 20), new Vector3(-3, 0, 28), new Vector3(0, 0, 40),
                                        new Vector3(2, 0, 55), new Vector3(-2, 0, 70), new Vector3(0, 0, -6), new Vector3(3, 0, 6) })
            {
                Vector3 from = t.TransformPoint(off) + Vector3.up * 6f;
                if (!Physics.Raycast(from, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.rigidbody != null) continue;   // a car parked on the spot
                asked++;
                float got = Depth(hit.point, out float want);
                float diffM = (got - want) * range;    // + = the map's surface is FARTHER than the road
                if (diffM > 0.5f) { holes++; worstHole = Mathf.Max(worstHole, diffM); }
                if (Mathf.Abs(diffM) <= 0.5f) onRoad++;
            }
            SunShadowPlayCheck.Check(asked >= 4, where + ": there is road to ask about", asked + " points");
            SunShadowPlayCheck.Check(holes == 0, where + ": the map never sees THROUGH the road (it is drawn where it is)",
                                     holes + " of " + asked + " points, worst " + worstHole.ToString("0.0") + " m");
            SunShadowPlayCheck.Check(onRoad * 2 >= asked, where + ": and at most points the first thing the sun meets is the road",
                                     onRoad + " of " + asked);

            // The roof: a point 1.0 m up from the car's origin, inside the body.
            Vector3 roof = t.position + t.up * 0.9f;
            float gotRoof = Depth(roof, out float wantRoof);
            float roofM = (gotRoof - wantRoof) * range;
            SunShadowPlayCheck.Check(roofM < 0.6f, where + ": the car is in the map, where the car is",
                                     "first surface " + roofM.ToString("0.00") + " m past a point inside the body");
            yield break;
        }

        void Done()
        {
            SunShadowPlayCheck.Finish();
            EditorApplication.Exit(SunShadowPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
