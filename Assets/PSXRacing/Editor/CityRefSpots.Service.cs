using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SERVICE WAYS' PICTURES (2026-11-15): two frames at each place of a
    /// file (PSX_SERVICE_SPOTS: rows "id, x, z, heading (compass degrees),
    /// what", tab separated, world metres):
    ///   eye   the driver's eye, 1.2 m over the ground at the place, looking
    ///         along the heading
    ///   top   from above: 55 m up over a point 22 m ahead, straight down,
    ///         the heading up the picture
    /// The state is the environment's (PSX_CITY_SERVICE=0: the base, in a
    /// process of its own); PSX_SERVICE_TAG is appended to each file's name
    /// (_before / _after), so the two runs' frames sit side by side.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunService
    /// Output: Screenshots/service/&lt;id&gt;_&lt;eye|top&gt;&lt;tag&gt;.png and
    /// service_log&lt;tag&gt;.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunService()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_SERVICE_TAG") ?? "";
            var log = new StringBuilder("SERVICE WAYS SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " tag '" + tag + "'\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "service");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            try
            {
                string file = System.Environment.GetEnvironmentVariable("PSX_SERVICE_SPOTS");
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) throw new System.Exception("PSX_SERVICE_SPOTS names no file");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindAnyObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                log.Append($"graph hash {CityMap.GraphHashOf(map.edges):x8}; {CityMeshes.ServiceStats.Line()}\n");
                RaceHandoff.CalendarDay = 0;
                RaceHandoff.WeatherOverride = -1;
                TimeOfDay.Apply(TimeOfDay.Noon, sun);
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                if (globals != null) globals.Apply();
                NightGlow.PreviewAll(TimeOfDay.At(TimeOfDay.Noon).lightsOn);
                PSXTexDecode.StampAll();
                var I = CultureInfo.InvariantCulture;
                foreach (var row in File.ReadAllLines(file))
                {
                    var c = row.Split('\t');
                    if (c.Length < 5) continue;
                    string id = c[0];
                    float x = float.Parse(c[1], I), z = float.Parse(c[2], I), hdg = float.Parse(c[3], I) * Mathf.Deg2Rad;
                    var fh = new Vector3(Mathf.Sin(hdg), 0f, Mathf.Cos(hdg));
                    world.DropAll();
                    var at0 = new Vector3(x, 0f, z);
                    world.EnsureRing(at0 + fh * 60f, 2);
                    // the ground at the place: the road's own height on a pavement, else the lattice
                    float y = CityMeshes.SharedGroundAt(map, trims, proc, x, z) + 0.05f;
                    var near = new HashSet<int>();
                    map.EdgeSegsInRect(new Vector2(x - 30f, z - 30f), new Vector2(x + 30f, z + 30f), near);
                    foreach (int pk in near)
                    {
                        var e = map.edges[pk >> 12];
                        CityElevation.ProjectOn(e, new Vector2(x, z), out float sp);
                        if (Vector2.Distance(e.PointAt(sp), new Vector2(x, z)) < e.width * 0.5f) { y = Mathf.Max(y, e.YAt(sp)); }
                    }
                    var here = new Vector3(x, y, z);
                    var park = here - fh * 80f + Vector3.Cross(Vector3.up, fh) * 40f;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);
                    if (car != null) car.TeleportTo(park + Vector3.up * 0.35f, rot); else player.transform.SetPositionAndRotation(park, rot);
                    CarLights.PushGlobals();
                    PSXScreenshotTool.ShotAs(cam, $"service/{id}_eye{tag}", here + Vector3.up * 1.2f, Quaternion.LookRotation(fh * 40f + Vector3.down * 2.2f));
                    float topH = 55f, topA = 22f; float.TryParse(System.Environment.GetEnvironmentVariable("PSX_SERVICE_TOPH") ?? "55", NumberStyles.Float, I, out topH); float.TryParse(System.Environment.GetEnvironmentVariable("PSX_SERVICE_AHEAD") ?? "22", NumberStyles.Float, I, out topA);
                    var top = here + fh * topA + Vector3.up * topH;   // PSX_SERVICE_TOPH / PSX_SERVICE_AHEAD: a closer plan (roadsmerge)
                    PSXScreenshotTool.ShotAs(cam, $"service/{id}_top{tag}", top, Quaternion.LookRotation(Vector3.down, fh));
                    shots += 2;
                    bool onWay = CityMeshes.ServiceWayAt(map, new Vector2(x, z) + new Vector2(fh.x, fh.z) * 12f, 1f, out int wi, out _);
                    log.Append(string.Format(I, "{0}\t({1:0.0}, {2:0.0}, y {3:0.00})\theading {4:0}\t{5}\t{6}\n", id, x, z, y, float.Parse(c[3], I), onWay ? $"service piece {wi} ({CityMeshes.ServiceKinds[CityMeshes.ServiceWays[wi].kind]}) 12 m ahead" : "no service pavement 12 m ahead", c[4]));
                }
                log.Append($"{shots} frames to {dir}\nSERVICE WAYS SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("SERVICE WAYS SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "service_log" + tag + ".txt"), log.ToString());
                Debug.Log("[ServiceShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
