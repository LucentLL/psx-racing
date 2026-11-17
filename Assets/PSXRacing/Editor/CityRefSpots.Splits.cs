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
                // the city kit as THIS code's slot table has it (a shared sandbox's kit may be another branch's:
                // a road profile whose slots it lacks would draw with a fallback texture)
                PSXRacingBuilder.EnsureCityTextures();
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
                // (the fix round) drops on the lens in the rain only: PSX_SPLITS_LENS_RAIN (or PSX_LENS_RAIN)
                // is put on the lens for the night-in-rain frames and taken off for the dry noon and tops
                string lensNight = System.Environment.GetEnvironmentVariable("PSX_SPLITS_LENS_RAIN") ?? System.Environment.GetEnvironmentVariable("PSX_LENS_RAIN");
                void Light(string cond)
                {
                    System.Environment.SetEnvironmentVariable("PSX_LENS_RAIN", cond == "night_rain" ? lensNight : null);
                    RaceHandoff.CalendarDay = 0;
                    RaceHandoff.WeatherOverride = cond == "night_rain" ? (int)Weather.Rain : -1;
                    int hour = cond == "night_rain" ? HourOf("night") : HourOf("noon");
                    TimeOfDay.Apply(hour, sun);
                    if (globals != null) globals.Apply();
                    NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                }
                // (the road-lines round) PSX_SPLITS_POSES = "id:x:z:hdg|id:x:z:hdg": these chase-camera poses instead of
                // the four Beatties Ford ones; PSX_SPLITS_CONDS = "noon" / "night_rain,noon" (default both)
                var poseList = new List<(string id, float x, float z, float hdg)>(poses);
                {
                    string pe = System.Environment.GetEnvironmentVariable("PSX_SPLITS_POSES");
                    if (!string.IsNullOrEmpty(pe))
                    {
                        poseList.Clear();
                        var inv2 = System.Globalization.CultureInfo.InvariantCulture;
                        foreach (var one in pe.Split('|'))
                        {
                            var q = one.Split(':');
                            if (q.Length >= 4) poseList.Add((q[0], float.Parse(q[1], inv2), float.Parse(q[2], inv2), float.Parse(q[3], inv2)));
                        }
                    }
                }
                var condList = (System.Environment.GetEnvironmentVariable("PSX_SPLITS_CONDS") ?? "night_rain,noon").Split(',');
                if (want.Contains("poses"))
                    foreach (var cond in condList)
                    {
                        Light(cond);
                        foreach (var ps in poseList)
                        {
                            var at = new Vector2(ps.x, ps.z);
                            float y = 0f;
                            if (map.NearestRoadPoint(at, 40f, false, out int ei, out float s, out _)) y = map.edges[ei].YAt(s);
                            var pos = new Vector3(ps.x, y + 0.35f, ps.z);   // (as CityRefSpots.Curbs2 stands the car)
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
                            log.Append($"{ps.id}\t{cond}\tcar ({pos.x:0.0},{pos.y:0.00},{pos.z:0.0}) hdg {ps.hdg:0.0}\tcam ({cp.x:0.00},{cp.y:0.00},{cp.z:0.00}) vfov {vfov:0.00} rot ({cr.eulerAngles.x:0.00},{cr.eulerAngles.y:0.00},{cr.eulerAngles.z:0.00})\te{(ei)}\tlens rain {System.Environment.GetEnvironmentVariable("PSX_LENS_RAIN") ?? "off"}\n");
                        }
                    }
                // (the road-lines round) PSX_SPLITS_POSETOPS = "ahead:height|ahead:height": for every pose, noon frames
                // looking straight down from <height> m over the point <ahead> m in front of the car (its heading up),
                // the car left where the pose stands it
                {
                    string pt = System.Environment.GetEnvironmentVariable("PSX_SPLITS_POSETOPS");
                    if (!string.IsNullOrEmpty(pt) && want.Contains("poses"))
                    {
                        Light("noon");
                        var inv3 = System.Globalization.CultureInfo.InvariantCulture;
                        var canv2 = new List<Canvas>();
                        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (c.enabled) { c.enabled = false; canv2.Add(c); }
                        try
                        {
                            foreach (var ps in poseList)
                            {
                                var fw = new Vector2(Mathf.Sin(ps.hdg * Mathf.Deg2Rad), Mathf.Cos(ps.hdg * Mathf.Deg2Rad));
                                float y0 = 0f;
                                if (map.NearestRoadPoint(new Vector2(ps.x, ps.z), 40f, false, out int e9, out float s9, out _)) y0 = map.edges[e9].YAt(s9);
                                var carPos = new Vector3(ps.x, y0 + 0.35f, ps.z);
                                int ti = 0;
                                foreach (var one in pt.Split('|'))
                                {
                                    var q = one.Split(':');
                                    if (q.Length < 2) continue;
                                    float ah = float.Parse(q[0], inv3), hh = float.Parse(q[1], inv3);
                                    var c2 = new Vector2(ps.x, ps.z) + fw * ah;
                                    world.EnsureRing(new Vector3(c2.x, y0, c2.y), 1);
                                    PSXTexDecode.StampAll();
                                    if (car != null) car.TeleportTo(carPos, Quaternion.Euler(0f, ps.hdg, 0f)); else player.transform.SetPositionAndRotation(carPos, Quaternion.Euler(0f, ps.hdg, 0f));
                                    cam.fieldOfView = 60f;
                                    PSXScreenshotTool.ShotAs(cam, $"splits/{tag}_{ps.id}_top{ti}", new Vector3(c2.x, y0 + hh, c2.y), Quaternion.LookRotation(Vector3.down, new Vector3(fw.x, 0f, fw.y)));
                                    shots++; ti++;
                                    log.Append($"{ps.id}\ttop{ti - 1}\tcentre ({c2.x:0.0},{c2.y:0.0}) {hh:0} m up, {ah:0} m ahead\n");
                                }
                            }
                        }
                        finally { foreach (var c in canv2) c.enabled = true; }
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
                // PHASE C (splits2): PSX_SPLITS_SPOTS = node:x:z,... (any split node city-wide) - a top frame 78 m
                // over the node and a driver-height frame (1.15 m, the inner travel lane in, 34 m before the node,
                // looking along the undivided road's axis), at noon, the car out of view
                string spots = System.Environment.GetEnvironmentVariable("PSX_SPLITS_SPOTS");
                if (!string.IsNullOrEmpty(spots) && want.Contains("spots"))
                {
                    Light("noon");
                    var canv = new List<Canvas>();
                    foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (c.enabled) { c.enabled = false; canv.Add(c); }
                    try
                    {
                        foreach (var tok in spots.Split(','))
                        {
                            if (!int.TryParse(tok.Trim().TrimStart('n'), out int n) || n < 0 || n >= map.nodes.Length) continue;
                            var c = map.nodes[n];
                            int ui = -1;
                            if (LineModel.SplitUOf(map, n, out int u0)) ui = u0;
                            else if (LineModel.SplitMeetingU(n, out int u1)) ui = u1;
                            else foreach (int ei in map.nodeEdges[n]) { var e = map.edges[ei]; if (!e.oneway && !e.link) ui = ei; }
                            if (ui < 0) { log.Append($"spot n{n}: no undivided road\n"); continue; }
                            var U = map.edges[ui];
                            var pb = U.PointAt(U.a == n ? Mathf.Min(30f, U.length) : Mathf.Max(0f, U.length - 30f));
                            Vector2 ax = (c - pb).sqrMagnitude > 1f ? (c - pb).normalized : Vector2.up;
                            var right = new Vector2(ax.y, -ax.x);
                            float y = 0f;
                            if (map.NearestRoadPoint(c, 40f, false, out int ej, out float sj, out _)) y = map.edges[ej].YAt(sj);
                            world.EnsureRing(new Vector3(c.x, y, c.y), 1);
                            PSXTexDecode.StampAll();
                            // top
                            var centre = c + ax * 30f;
                            var eye = new Vector3(centre.x, y + 92f, centre.y);
                            var rot = Quaternion.LookRotation(Vector3.down, new Vector3(ax.x, 0f, ax.y));
                            var hide = eye + Vector3.down * 400f;
                            if (car != null) car.TeleportTo(hide, Quaternion.identity); else player.transform.position = hide;
                            cam.fieldOfView = 60f;
                            PSXScreenshotTool.ShotAs(cam, $"splits/{tag}_spot_n{n}_top", eye, rot);
                            shots++;
                            // driver height: the inner travel lane toward the node (right-hand traffic), 34 m before it
                            float laneOff = Mathf.Clamp(0.25f * U.width, 1.8f, 5.5f);
                            var p2 = c - ax * 34f + right * laneOff;
                            float y2 = y;
                            if (map.NearestRoadPoint(p2, 20f, false, out int ek, out float sk, out _)) y2 = map.edges[ek].YAt(sk);
                            var eye2 = new Vector3(p2.x, y2 + 1.15f, p2.y);
                            var look = new Vector3(ax.x, -0.035f, ax.y);
                            cam.fieldOfView = 62f;
                            PSXScreenshotTool.ShotAs(cam, $"splits/{tag}_spot_n{n}_eye", eye2, Quaternion.LookRotation(look, Vector3.up));
                            shots++;
                            log.Append($"spot n{n}\tU e{ui} '{U.name}'\ttop ({centre.x:0.0},{centre.y:0.0})\teye ({eye2.x:0.0},{eye2.y:0.00},{eye2.z:0.0}) axis ({ax.x:0.000},{ax.y:0.000})\n");
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
