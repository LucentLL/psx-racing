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
    /// THE BUILDINGS PAIRS (2026-11-11, the owner's "windows going through
    /// roofs"): driver's-eye frames at named places, one job per state - the
    /// switches (PSX_CITY_HOUSEWALLS / HOUSECLASS / HOUSEEAVES) are read when
    /// the tiles are built, so the before frames come from a job run with them
    /// at 0 and the after frames from a job with them on; PSX_BLDG_TAG names
    /// the state in the file names.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunBldg
    ///   PSX_BLDG_TAG = before | after (default after); PSX_BLDG_HOURS = noon,night;
    ///   PSX_BLDG_ONLY = place,place; PSX_BLDG_PLACES = "id|lat|lon|road|heading|ahead m;..." adds places
    /// Output: Screenshots/bldg/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;tag&gt;.png and bldg_log.txt.
    /// Poses: eye (1.2 m, 4 m ahead of the car, down the lane), right (the same
    /// eye turned 40 degrees to the right-hand frontage), left (40 degrees left).
    /// </summary>
    public static partial class CityRefSpots
    {
        struct BldgPlace { public string id, road, what; public double lat, lon; public float hdg, ahead; }

        static readonly BldgPlace[] BldgPlaces =
        {
            // the owner's frame (images/1.webp): N Graham St south-west past W 10th St toward Uptown,
            // the Fourth Ward residential blocks (OSM building=residential, 3 levels, 11.76 m) on the right
            new BldgPlace { id = "graham", lat = 35.235364, lon = -80.840411, road = "Graham", hdg = 229f, ahead = 0f, what = "N Graham St SW at W 10th St (the owner's frame)" },
            new BldgPlace { id = "graham_b", lat = 35.234700, lon = -80.841150, road = "Graham", hdg = 229f, ahead = 0f, what = "N Graham St SW between W 10th and W 6th" },
            new BldgPlace { id = "dilworth", lat = 35.20225, lon = -80.84758, road = "Dilworth", hdg = 192f, what = "Dilworth Rd E: streetcar-suburb houses" },
        };

        /// <summary>The audit (CityAudit.WindowsAudit) then the frames, one editor.</summary>
        public static void RunBldgAll()
        {
            if (System.Environment.GetEnvironmentVariable("PSX_BLDG_NOAUDIT") != "1") CityAudit.WindowsAudit(false);
            RunBldg();
        }

        public static void RunBldg()
        {
            var I = CultureInfo.InvariantCulture;
            var log = new StringBuilder("BLDG SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "bldg");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            string tag = System.Environment.GetEnvironmentVariable("PSX_BLDG_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "after";
            try
            {
                log.Append($"tag {tag}: HOUSEWALLS {CityMeshes.HouseWallsOn} HOUSECLASS {CityMeshes.HouseClassOn} HOUSEEAVES {CityMeshes.HouseEavesOn}\n");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;   // the PC default
                // the siding's colour flag (the scene pass sets it in the builder; this sandbox's
                // kit material predates it): neutral vertex colour = as painted, so before is unchanged
                var kit = CityKit.Get();
                if (kit != null && kit.slots != null && kit.slots.Length > (int)CityMeshes.Slot.FacadeHouse && kit.slots[(int)CityMeshes.Slot.FacadeHouse] != null)
                    kit.slots[(int)CityMeshes.Slot.FacadeHouse].SetFloat("_HouseTint", CityMeshes.HouseTintOn && tag != "before" ? 1f : 0f);
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var places = new List<BldgPlace>(BldgPlaces);
                string extra = System.Environment.GetEnvironmentVariable("PSX_BLDG_PLACES");
                if (!string.IsNullOrEmpty(extra))
                    foreach (var s in extra.Split(';'))
                    {
                        var p = s.Split('|');
                        if (p.Length < 5) continue;
                        places.Add(new BldgPlace
                        {
                            id = p[0], lat = double.Parse(p[1], I), lon = double.Parse(p[2], I), road = p[3], hdg = float.Parse(p[4], I),
                            ahead = p.Length > 5 ? float.Parse(p[5], I) : 0f, what = p.Length > 6 ? p[6] : p[0],
                        });
                    }
                string only = System.Environment.GetEnvironmentVariable("PSX_BLDG_ONLY");
                var hours = Pick("PSX_BLDG_HOURS", "noon");
                foreach (var pl in places)
                {
                    if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                    if (!Snap(map, LL(pl.lat, pl.lon), pl.road, out var e, out float s, out bool named)) { log.Append($"{pl.id}: no road near\n"); continue; }
                    float hr = pl.hdg * Mathf.Deg2Rad;
                    var want = new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr));
                    var t2 = e.TangentAt(s);
                    var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    if (Vector3.Dot(fh, want) < 0f) fh = -fh;
                    if (pl.ahead != 0f) { s = Mathf.Clamp(s + pl.ahead * Mathf.Sign(Vector2.Dot(t2, new Vector2(fh.x, fh.z))), 1f, e.length - 1f); }
                    Vector2 p2 = e.PointAt(s);
                    float lat = e.oneway ? 0f : e.width * 0.25f;
                    var pos = new Vector3(p2.x, e.YAt(s) + 0.35f, p2.y) + Vector3.Cross(Vector3.up, fh) * lat;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);
                    world.EnsureRing(pos, 2);
                    if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                    Vector3 rightV = Vector3.Cross(Vector3.up, fh).normalized;
                    Vector3 eye = pos + fh * 4f + Vector3.up * 0.85f;
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>
                    {
                        ("eye", eye, Quaternion.LookRotation(fh * 40f + Vector3.up * 1.2f)),
                        ("right", eye, Quaternion.LookRotation(Quaternion.AngleAxis(40f, Vector3.up) * fh * 30f + Vector3.up * 3f)),
                        ("left", eye, Quaternion.LookRotation(Quaternion.AngleAxis(-40f, Vector3.up) * fh * 30f + Vector3.up * 3f)),
                    };
                    log.Append(string.Format(I, "{0}\t'{1}'{2} cls {3} width {4:0.0}\teye ({5:0.0}, {6:0.00}, {7:0.0}) heading {8:0}\t{9}\n",
                        pl.id, e.name, named ? "" : " (NOT the named road)", e.cls, e.width, eye.x, eye.y, eye.z,
                        (Mathf.Atan2(fh.x, fh.z) * Mathf.Rad2Deg + 360f) % 360f, pl.what));
                    foreach (var h in hours)
                    {
                        int hour = HourOf(h);
                        RaceHandoff.CalendarDay = 0;
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        var hd = TimeOfDay.At(hour);
                        NightGlow.PreviewAll(hd.lightsOn);
                        foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) lights.PreviewBuild(hd.lightsOn);
                        TextureHD.OffAll(); PSXTexDecode.StampAll();
                        foreach (var p in poses)
                        {
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"bldg/{pl.id}_{p.name}_{h}_{tag}", p.eye, p.r);
                            shots++;
                        }
                    }
                    world.DropAll();
                }
                log.Append($"{shots} frames to {dir}\nBLDG SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("BLDG SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(dir, $"bldg_log_{tag}.txt"), log.ToString());
                Debug.Log("[Bldg]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
