using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE BAKED SKY LIGHT ROLLOUT, photographed (2026-10-16): a venue's
    /// scene opened BY PATH (a lab-built stage need not be in the build
    /// settings), a race handed over as the LifeSim hands one (summer, clear,
    /// no traffic), the field let go, then the player parked on a spot.
    ///
    /// PSX_BL_VENUE (BlueRidge), PSX_BL_SPOT ("bridge" = mid-span of the
    /// longest deck, or a route fraction), PSX_BL_VIEWS (comma list:
    /// "chase"; "sideL"/"sideR" = standing on the land 18 m off that side of
    /// the deck, looking back at it; "highL"/"highR" = 12 m over the road,
    /// looking down at the land 14 m off that side), PSX_BL_PAIR (1: each
    /// view twice, the bake on (after_) and PSX_SKYVIS off on every material
    /// (before_) - the colour is read only under the keyword, so off is
    /// exactly the venue without the bake), PSX_BL_PREFIX (the file prefix
    /// without a pair), PSX_BL_NAME (the file stem), PSX_BL_HOUR (noon),
    /// PSX_BL_CAR, PSX_BL_OUT. Files: &lt;prefix&gt;_&lt;name&gt;_&lt;view&gt;.png.
    /// Needs a device (a watched editor). PSXRacing.EditorTools.BakedLightShots.Run
    /// </summary>
    public static class BakedLightShots
    {
        internal static string outDir, prefix, name, spot, carId;
        internal static string[] views;
        internal static bool pair;
        internal static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        static string Env(string k, string d)
        {
            string v = System.Environment.GetEnvironmentVariable(k);
            return string.IsNullOrEmpty(v) ? d : v;
        }

        public static void Run()
        {
            string venue = Env("PSX_BL_VENUE", "BlueRidge");
            carId = Env("PSX_BL_CAR", "dodge_viper_gts__99");
            outDir = Env("PSX_BL_OUT", Path.Combine(ProjectDir, "Screenshots", "bakedlight"));
            prefix = Env("PSX_BL_PREFIX", "shot");
            name = Env("PSX_BL_NAME", venue);
            spot = Env("PSX_BL_SPOT", "0.3");
            views = Env("PSX_BL_VIEWS", "chase").Split(',');
            pair = Env("PSX_BL_PAIR", "0") == "1";
            Directory.CreateDirectory(outDir);
            int trackIndex = -1;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == venue) trackIndex = i;
            int hour = System.Array.FindIndex(TimeOfDay.All, t => t.name.Equals(Env("PSX_BL_HOUR", "noon"), System.StringComparison.OrdinalIgnoreCase));
            if (hour < 0) hour = TimeOfDay.Noon;
            log.AppendLine($"baked light shots: {venue}, spot {spot}, views {string.Join(",", views)}, pair {pair}");
            string scene = "Assets/PSXRacing/Scenes/" + venue + ".unity";
            if (trackIndex < 0 || !File.Exists(Path.Combine(ProjectDir, scene))) { Finish("no venue or scene " + scene); return; }
            EditorSceneManager.OpenScene(scene);
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
            new GameObject("BakedLightShotsRunner").AddComponent<BakedLightShotsRunner>();
        }

        internal static void Done(string why)
        {
            EditorApplication.playModeStateChanged += st =>
            {
                if (st == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => Finish(why);
            };
            EditorApplication.ExitPlaymode();
        }

        static void Finish(string why)
        {
            log.AppendLine(why);
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_bakedlight_shots.txt"), log.ToString());
            Debug.Log("[BakedLightShots] " + log);
            EditorApplication.Exit(0);
        }
    }

    public class BakedLightShotsRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            var L = BakedLightShots.log;
            Camera cam = null;
            for (int i = 0; i < 600 && cam == null; i++)
            {
                var go = GameObject.Find("PSXCamera");
                if (go != null) cam = go.GetComponent<Camera>();
                yield return null;
            }
            var rm = Object.FindAnyObjectByType<RaceManager>();
            var player = rm != null ? rm.playerCar : null;
            if (cam == null || player == null || rm.path == null || rm.path.Count < 8) { BakedLightShots.Done("no camera, player or path"); yield break; }
            // the bake's keyword on the scene's materials (and how many wear it)
            var keyed = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.IsKeywordEnabled("PSX_SKYVIS")) keyed.Add(m);
            L.AppendLine($"  {keyed.Count} materials wear PSX_SKYVIS; dress {SeasonDress.AppliedDress}, weather {Seasons.CurrentWeather}");
            yield return Wait(12f);

            var path = rm.path;
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            TrackCatalog.EnsureStage(def);
            var rig = cam.GetComponent<ChaseCamera>();
            // "0.3;bridge@chase+highL": spots split by ';', each with its own
            // views after '@' (else PSX_BL_VIEWS); a file names its spot
            // when there are several
            string[] spots = BakedLightShots.spot.Split(';');
            foreach (var spotRaw in spots)
            {
                string[] sv = spotRaw.Trim().Split('@');
                string spotId = sv[0].Trim();
                if (spotId.Length == 0) continue;
                string[] views = sv.Length > 1 ? sv[1].Split('+') : BakedLightShots.views;
                string stem = BakedLightShots.name + (spots.Length > 1 ? "_" + spotId : "");
            int i0;
            if (spotId == "bridge")
            {
                if (def.bridges == null || def.bridges.Length == 0) { L.AppendLine("  no bridges here"); continue; }
                Vector2 best = def.bridges[0];
                foreach (var b in def.bridges) if (b.y - b.x > best.y - best.x) best = b;
                i0 = Mathf.Clamp(Mathf.RoundToInt((best.x + best.y) * 0.5f / path.spacing), 1, path.Count - 2);
                L.AppendLine($"  longest deck {best.x:0}-{best.y:0} m: wp {i0}");
            }
            else
            {
                float f = float.Parse(spotId, System.Globalization.CultureInfo.InvariantCulture);
                i0 = Mathf.Clamp(Mathf.RoundToInt(f * (path.Count - 1)), 1, path.Count - 2);
                L.AppendLine($"  spot {f} = wp {i0}");
            }
            var t = player.transform;
            float lift = t.position.y - path.GetPoint(path.NearestIndex(t.position)).y;
            Vector3 at = path.GetPoint(i0);
            Vector3 fwd = path.GetPoint(i0 + 1) - path.GetPoint(i0 - 1); fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            player.TeleportTo(at + right * (path.roadWidth * 0.25f) + Vector3.up * lift, Quaternion.LookRotation(fwd, Vector3.up));
            yield return Wait(3f);

            foreach (var raw in views)
            {
                string v = raw.Trim();
                if (v.Length == 0) continue;
                bool posed = v != "chase";
                if (posed)
                {
                    float side = v.EndsWith("L") ? -1f : 1f;
                    Vector3 eye, look;
                    if (v.StartsWith("side"))
                    {
                        Vector3 foot = at + right * (side * 18f) - fwd * 6f;
                        eye = new Vector3(foot.x, Ground(foot, at.y) + 3f, foot.z);
                        look = at + right * (side * 6f) + fwd * 8f + Vector3.down * 4f;
                    }
                    else
                    {
                        eye = at + right * (side * 4f) - fwd * 8f + Vector3.up * 12f;
                        Vector3 land = at + right * (side * 14f) + fwd * 10f;
                        look = new Vector3(land.x, Ground(land, at.y), land.z);
                    }
                    if (rig != null) rig.enabled = false;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    L.AppendLine($"  {v}: eye {eye.x:0.0},{eye.y:0.0},{eye.z:0.0} ({eye.y - at.y:0.0} m off the road), looking at {look.y - at.y:0.0} m");
                }
                if (BakedLightShots.pair)
                {
                    yield return Grab(cam, "after_" + stem + "_" + v);
                    foreach (var m in keyed) m.DisableKeyword("PSX_SKYVIS");
                    yield return Grab(cam, "before_" + stem + "_" + v);
                    foreach (var m in keyed) m.EnableKeyword("PSX_SKYVIS");
                }
                else yield return Grab(cam, BakedLightShots.prefix + "_" + stem + "_" + v);
                if (posed && rig != null) rig.enabled = true;
            }
            }
            BakedLightShots.Done("done");
        }

        /// <summary>The land under (x, z): the highest GROUND collider hit
        /// from 200 m over the road (not a trunk, not a deck), else the
        /// lowest hit, else the road's own height.</summary>
        static float Ground(Vector3 p, float roadY)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, roadY + 200f, p.z), Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore);
            float ground = float.NegativeInfinity, lowest = float.PositiveInfinity;
            foreach (var h in hits)
            {
                if (h.collider.attachedRigidbody != null) continue;   // a car
                if (h.collider.name.Contains("Ground") && h.point.y > ground) ground = h.point.y;
                if (h.point.y < lowest) lowest = h.point.y;
            }
            return !float.IsNegativeInfinity(ground) ? ground : lowest < float.PositiveInfinity ? lowest : roadY;
        }

        static IEnumerator Wait(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        IEnumerator Grab(Camera cam, string file)
        {
            yield return null;
            yield return null;
            yield return null;
            var rt = cam.targetTexture;
            if (rt == null) { BakedLightShots.log.AppendLine("  " + file + ": camera has no target"); yield break; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string full = Path.Combine(BakedLightShots.outDir, file + ".png");
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Destroy(tex);
            BakedLightShots.log.AppendLine($"  shot {full} ({rt.width}x{rt.height})");
        }
    }
}
