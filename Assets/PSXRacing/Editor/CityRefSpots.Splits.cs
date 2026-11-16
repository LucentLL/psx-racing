using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SPLITS ROUND'S PICTURES (J1, 2026-11-16): the owner's four poses on
    /// Beatties Ford Road (PC45, night in rain) from his chase camera - the car
    /// where his was, the camera where ChaseCamera.SteadyPose puts it - at
    /// night in rain and at dry noon; then a bird's-eye frame over each of
    /// Beatties Ford's 19 split-type nodes at noon (the car out of view, no
    /// HUD). One process draws one PSX_CITY_SPLITAXIS value: run it once with
    /// 0 and once with bfr (fresh processes) for the before / after.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunSplitShots
    /// PSX_SPLITS_TAG names the frames (default the scope);
    /// PSX_SPLITS_SHOTS = poses,tops (default both).
    /// Output: Screenshots/splits/&lt;tag&gt;_&lt;id&gt;_&lt;cond&gt;.png, splits_shots_&lt;tag&gt;.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunSplitShots()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_SPLITS_TAG") ?? LineModel.SplitAxis;
            string want = System.Environment.GetEnvironmentVariable("PSX_SPLITS_SHOTS") ?? "poses,tops";
            var log = new StringBuilder($"SPLITS SHOTS '{tag}' {System.DateTime.Now:yyyy-MM-dd HH:mm} PSX_CITY_SPLITAXIS {LineModel.SplitAxis}\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "splits");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            float fovWas = 60f; Camera camK = null;
            try
            {
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                camK = cam; fovWas = cam.fieldOfView;
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                (string id, float x, float z, float hdg)[] poses =
                {
                    ("34", -3358.8f, 8768f, 9.8f), ("35", -3324.8f, 8960f, 9.9f), ("36", -3307.4f, 9386f, 356.5f), ("37", -3467.8f, 10422f, 347f),
                };
                void Light(string cond)
                {
                    RaceHandoff.CalendarDay = 0;
                    RaceHandoff.WeatherOverride = cond == "night_rain" ? (int)Weather.Rain : -1;
                    int hour = cond == "night_rain" ? HourOf("night") : HourOf("noon");
                    TimeOfDay.Apply(hour, sun);
                    if (globals != null) globals.Apply();
                    NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                }
                if (want.Contains("poses"))
                    foreach (var cond in new[] { "night_rain", "noon" })
                    {
                        Light(cond);
                        foreach (var ps in poses)
                        {
                            var at = new Vector2(ps.x, ps.z);
                            float y = 0f;
                            if (map.NearestRoadPoint(at, 40f, false, out int ei, out float s, out _)) y = map.edges[ei].YAt(s);
                            var pos = new Vector3(ps.x, y + 0.02f, ps.z);
                            var rot = Quaternion.Euler(0f, ps.hdg, 0f);
                            world.EnsureRing(pos, 1);
                            PSXTexDecode.StampAll();
                            if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                            ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 12f, ChaseCamera.DefaultSpeedFullMps, player.transform,
                                                   ChaseCamera.FrameOf(player), ChaseCamera.LiveDials(), out var cp, out var cr, out float vfov, out _);
                            cam.fieldOfView = vfov;
                            if (cond == "night_rain") WeatherFx.Preview(Weather.Rain, cam, 2.5f);
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"splits/{tag}_{ps.id}_{cond}", cp, cr);
                            if (cond == "night_rain") WeatherFx.PreviewClear();
                            shots++;
                            log.Append($"{ps.id}\t{cond}\tcar ({pos.x:0.0},{pos.y:0.00},{pos.z:0.0}) hdg {ps.hdg:0.0}\tcam ({cp.x:0.0},{cp.y:0.00},{cp.z:0.0}) vfov {vfov:0.0}\te{(ei)}\n");
                        }
                    }
                if (want.Contains("tops"))
                {
                    Light("noon");
                    var canv = new List<Canvas>();
                    foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (c.enabled) { c.enabled = false; canv.Add(c); }
                    try
                    {
                        foreach (int n in LineModel.BfrSplitNodes)
                        {
                            if (n >= map.nodes.Length) continue;
                            var c = map.nodes[n];
                            // the axis: the undivided road's way into the node (its last 30 m), else north
                            Vector2 ax = Vector2.up;
                            int ui = -1;
                            if (LineModel.SplitUOf(map, n, out int u0)) ui = u0;
                            else foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; if (!e.oneway && e.name == "Beatties Ford Road") ui = ei; }
                            if (ui >= 0)
                            {
                                var U = map.edges[ui];
                                var pb = U.PointAt(U.a == n ? Mathf.Min(30f, U.length) : Mathf.Max(0f, U.length - 30f));
                                if ((c - pb).sqrMagnitude > 1f) ax = (c - pb).normalized;
                            }
                            float y = 0f;
                            if (map.NearestRoadPoint(c, 40f, false, out int ej, out float sj, out _)) y = map.edges[ej].YAt(sj);
                            var centre = c + ax * 22f;
                            var eye = new Vector3(centre.x, y + 78f, centre.y);
                            var rot = Quaternion.LookRotation(Vector3.down, new Vector3(ax.x, 0f, ax.y));
                            world.EnsureRing(new Vector3(c.x, y, c.y), 1);
                            PSXTexDecode.StampAll();
                            var hide = eye + Vector3.down * 400f;
                            if (car != null) car.TeleportTo(hide, Quaternion.identity); else player.transform.position = hide;
                            cam.fieldOfView = 60f;
                            PSXScreenshotTool.ShotAs(cam, $"splits/{tag}_top_n{n}", eye, rot);
                            shots++;
                            log.Append($"top n{n}\tcentre ({centre.x:0.0},{centre.y:0.0}) axis ({ax.x:0.000},{ax.y:0.000}) y {y:0.0}\n");
                        }
                    }
                    finally { foreach (var c in canv) c.enabled = true; }
                }
                log.Append($"{shots} frames to {dir}\nSPLITS SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("SPLITS SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                if (camK != null) camK.fieldOfView = fovWas;
                File.WriteAllText(Path.Combine(dir, $"splits_shots_{tag}.txt"), log.ToString());
                Debug.Log("[SplitShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
