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
    /// THE LOW-RISE CLASSES' PAIRS (buildings round 2 "types", 2026-11-11): a
    /// driver's-eye frame at each place, from the road, turned to look AT the
    /// building (so it fills the frame), one job per state - the switches
    /// (PSX_CITY_LRHEIGHT / LRLOOK / LRFRONTAGE) are read at load, so the base
    /// frames come from a job run with them at 0; PSX_TYPES_TAG names the state.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTypes
    ///   PSX_TYPES_TAG = base | r2; PSX_TYPES_HOURS = noon (night for PSX_TYPES_NIGHT's places);
    ///   PSX_TYPES_ONLY = place,place
    /// Output: Screenshots/types/&lt;place&gt;_&lt;hour&gt;_&lt;tag&gt;.png and types_log_&lt;tag&gt;.txt (the low-rise
    /// report, the frontage counts, each place's road edge for CityAudit.RunDrive's PSX_DRIVE_EDGES,
    /// and the triangles of the building's tile).
    /// </summary>
    public static partial class CityRefSpots
    {
        struct TypesPlace { public string id, road, what; public double lat, lon, tlat, tlon; public float lookUp; }

        static readonly TypesPlace[] TypesPlaces =
        {
            new TypesPlace { id = "atando", lat = 35.26152, lon = -80.82547, road = "Atando", tlat = 35.26213, tlon = -80.82478, what = "Atando Ave: OSM 324608519, 11,015 m2 building=yes height=26" },
            new TypesPlace { id = "noda_lofts", lat = 35.24680, lon = -80.80700, road = "Davidson", tlat = 35.24700, tlon = -80.80611, what = "N Davidson St, NoDa: Fat City Lofts (apartments, 7 levels)" },
            new TypesPlace { id = "noda_mill", lat = 35.24909, lon = -80.80213, road = "37th", tlat = 35.24940, tlon = -80.80238, what = "E 37th St, NoDa: Lofts at NoDa Mills (industrial, historic)" },
            new TypesPlace { id = "strip", lat = 35.20345, lon = -80.74620, road = "Albemarle", tlat = 35.20387, tlon = -80.74612, what = "Albemarle Rd at 5865: a strip centre behind its lot (1,868 m2 building=yes height=16 in landuse=retail)" },
            new TypesPlace { id = "church", lat = 35.22144, lon = -80.81008, road = "Hamorton", tlat = 35.22124, tlon = -80.81014, what = "Hamorton Pl, Plaza Midwood: a brick church with a steeple (OSM building=church, 737 m2, height=16)" },
            new TypesPlace { id = "school", lat = 35.20490, lon = -80.84485, road = "Buchanan", tlat = 35.20517, tlon = -80.84458, what = "Buchanan St, Dilworth: St Patrick's school" },
            new TypesPlace { id = "ranch", lat = 35.23731, lon = -80.79592, road = "Shamrock", tlat = 35.23698, tlon = -80.79541, what = "Shamrock Dr: brick ranches (round 1's 3-storey frontage block)" },
            new TypesPlace { id = "graham_ind", lat = 35.24510, lon = -80.82887, road = "Bancroft", tlat = 35.24489, tlon = -80.82850, what = "Bancroft St off N Graham St: an industrial street (3,332 m2 building=yes height=25)" },
        };

        public static void RunTypes()
        {
            var I = CultureInfo.InvariantCulture;
            var log = new StringBuilder("TYPES SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "types");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            string tag = System.Environment.GetEnvironmentVariable("PSX_TYPES_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "r2";
            try
            {
                log.Append($"tag {tag}: LRHEIGHT {CityMeshes.LowRiseHeightOn} LRLOOK {CityMeshes.LowRiseLookOn} LRFRONTAGE {CityBuildings.FrontageRuleOn}\n");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                CityBuildings.Precompute(map);   // (idempotent for the classes: the report)
                log.Append(CityMeshes.LowRiseReportText);
                log.Append(CityBuildings.FrontageReport + "\n");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                string only = System.Environment.GetEnvironmentVariable("PSX_TYPES_ONLY");
                var hours = Pick("PSX_TYPES_HOURS", "noon");
                var nightAt = Pick("PSX_TYPES_NIGHT", "atando,strip");
                var edges = new List<int>();
                foreach (var pl in TypesPlaces)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                    var camP = LL(pl.lat, pl.lon);
                    var tgt = LL(pl.tlat, pl.tlon);
                    if (!Snap(map, camP, pl.road, out var e, out float s, out bool named)) { log.Append($"{pl.id}: no road near\n"); continue; }
                    edges.Add(e.index);
                    Vector2 p2 = e.PointAt(s);
                    var t2 = e.TangentAt(s);
                    var side = new Vector2(-t2.y, t2.x);
                    if (Vector2.Dot(tgt - p2, side) < 0f) side = -side;
                    float lat = e.oneway ? 0f : e.width * 0.25f;
                    var q = p2 + side * lat;
                    float gy = e.YAt(s);
                    var pos = new Vector3(q.x, gy + 0.35f, q.y);
                    var fh3 = new Vector3(t2.x, 0f, t2.y).normalized;
                    var swt = Stopwatch();
                    world.EnsureRing(pos, 2);
                    double ringMs = swt.Elapsed.TotalMilliseconds;
                    // the car stands 14 m behind the eye (down the road, away from the building), out of the frame
                    var away = new Vector3(q.x - tgt.x, 0f, q.y - tgt.y).normalized;
                    var carAt = pos + (Vector3.Dot(away, fh3) >= 0f ? fh3 : -fh3) * 14f;
                    if (car != null) car.TeleportTo(carAt, Quaternion.LookRotation(fh3, Vector3.up)); else player.transform.SetPositionAndRotation(carAt, Quaternion.LookRotation(fh3, Vector3.up));
                    float tg = CityElevation.GroundY(map, tgt.x, tgt.y);
                    var eye = pos + Vector3.up * 0.85f;
                    var look = new Vector3(tgt.x, tg + 4.5f + pl.lookUp, tgt.y) - eye;
                    var rot = Quaternion.LookRotation(look.normalized, Vector3.up);
                    float hdg = (Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg + 360f) % 360f;
                    // the triangles of the building's tile (every renderer under the world whose bounds' centre is on it)
                    int ttx = Mathf.FloorToInt(tgt.x / CityMeshes.TileSize), ttz = Mathf.FloorToInt(tgt.y / CityMeshes.TileSize);
                    long tris = 0; int rends = 0;
                    foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
                    {
                        var m = mf.sharedMesh; if (m == null) continue;
                        var r = mf.GetComponent<Renderer>(); if (r == null) continue;
                        var c = r.bounds.center;
                        if (Mathf.FloorToInt(c.x / CityMeshes.TileSize) != ttx || Mathf.FloorToInt(c.z / CityMeshes.TileSize) != ttz) continue;
                        rends++;
                        for (int sm = 0; sm < m.subMeshCount; sm++) tris += (long)m.GetIndexCount(sm) / 3;
                    }
                    var camLL = new Vector2(q.x, q.y);
                    double cLat = 35.18456015184093 + camLL.y / 111132.0, cLon = -80.81770185962013 + camLL.x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0));
                    log.Append(string.Format(I, "{0}\t'{1}'{2} e{3}\tcam {4:0.00000},{5:0.00000} heading {6:0} to the building {7:0} m\ttile ({8},{9}) {10} renderers {11} triangles; ring of 25 built in {12:0} ms\t{13}\n",
                        pl.id, e.name, named ? "" : " (NOT the named road)", e.index, cLat, cLon, hdg, look.magnitude, ttx, ttz, rends, tris, ringMs, pl.what));
                    log.Append(CityMeshes.LowRiseDescribe(map, tgt, pl.id == "ranch" ? 45f : 25f));
                    foreach (var h in hours)
                    {
                        if (h == "night" && System.Array.IndexOf(nightAt, pl.id) < 0) continue;
                        int hour = HourOf(h);
                        RaceHandoff.CalendarDay = 0;
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        var hd = TimeOfDay.At(hour);
                        NightGlow.PreviewAll(hd.lightsOn);
                        foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) lights.PreviewBuild(hd.lightsOn);
                        TextureHD.OffAll(); PSXTexDecode.StampAll();
                        CarLights.PushGlobals();
                        PSXScreenshotTool.ShotAs(cam, $"types/{pl.id}_{h}_{tag}", eye, rot);
                        shots++;
                    }
                    world.DropAll();
                }
                log.Append("PSX_DRIVE_EDGES=" + string.Join(",", edges) + "\n");
                log.Append($"{shots} frames to {dir}\nTYPES SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("TYPES SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(dir, $"types_log_{tag}.txt"), log.ToString());
                Debug.Log("[Types]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static System.Diagnostics.Stopwatch Stopwatch() => System.Diagnostics.Stopwatch.StartNew();
    }
}
