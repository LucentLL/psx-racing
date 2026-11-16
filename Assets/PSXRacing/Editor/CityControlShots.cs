using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CONTROL ROUND'S OWNER-POSE PICTURES (2026-11-16): the player's car
    /// set down on the road at an owner's screenshot position and heading,
    /// shot from the game's own chase rig at rest (ChaseCamera.SteadyPose, as
    /// ColourShots does), at night in the rain like his PC45 frame and at a
    /// dry noon. Whatever junction controls the synced code and its switches
    /// make (PSX_CITY_SIGNALS2 / PSX_CITY_MITREWARRANT): run once per switch
    /// state, each in a fresh process.
    ///   -executeMethod PSXRacing.EditorTools.CityControlShots.RunOwner
    ///   PSX_CTLSHOT_POSES=file   rows "id x z heading [variants]" (tab separated; heading in degrees, 0 north,
    ///                            90 east) or "id @edge s dir [variants]" (the right-hand lane of an edge, toward
    ///                            b if dir is 1); variants "nightrain,noon" by default
    ///   (a row's car stands where the game could put it: on a lane of a road, never staged in a junction)
    ///   PSX_CTLSHOT_TAG=name     appended to every file name (base / after)
    /// Output: Screenshots/control/&lt;id&gt;_&lt;nightrain|noon&gt;_&lt;tag&gt;.png and control_shots_&lt;tag&gt;.txt
    /// </summary>
    public static class CityControlShots
    {
        public static void RunOwner()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_CTLSHOT_TAG") ?? "run";
            var log = new StringBuilder($"CONTROL OWNER-POSE SHOTS {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}; PSX_CITY_SIGNALS2 {(CitySignals.Signals2On ? "on" : "OFF")}, mitred warrant {(CitySignals.MitreWarrantOn ? "on" : "OFF")}, turn pockets {(CityMeshes.TurnPocketsOn ? "on" : "off")}\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "control");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            try
            {
                string file = System.Environment.GetEnvironmentVariable("PSX_CTLSHOT_POSES");
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) throw new System.Exception("PSX_CTLSHOT_POSES names no file");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var trims = CityMeshes.NodeTrims(map);
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                var path = Object.FindAnyObjectByType<TrackPath>();
                foreach (var row in File.ReadAllLines(file))
                {
                    var c = row.Split('\t');
                    if (c.Length < 4 || row.StartsWith("#")) continue;
                    string id = c[0];
                    float x, z, hdg, y = 0f;
                    if (c[1].StartsWith("@"))
                    {
                        // "@edge s dir": on that edge at s, travelling toward b (1) or a (-1), in the right-hand lane
                        var e = map.edges[int.Parse(c[1].Substring(1), inv)];
                        float se; int way;
                        if (c[2].StartsWith("n"))
                        {
                            // "@edge nNODE d": d metres out from that node along the edge, driving toward it
                            int node = int.Parse(c[2].Substring(1), inv); float dd = float.Parse(c[3], inv);
                            if (e.a == node) { se = Mathf.Min(dd, e.length); way = -1; } else { se = Mathf.Max(0f, e.length - dd); way = 1; }
                        }
                        else { se = Mathf.Clamp(float.Parse(c[2], inv), 0f, e.length); way = c[3].Trim() == "-1" ? -1 : 1; }
                        var t2 = e.TangentAt(se) * way;
                        var r2 = new Vector2(t2.y, -t2.x);
                        var pe = e.PointAt(se) + r2 * (e.oneway ? 0f : 1.83f);
                        x = pe.x; z = pe.y; hdg = Mathf.Atan2(t2.x, t2.y) * Mathf.Rad2Deg; y = e.YAt(se);
                    }
                    else
                    {
                        x = float.Parse(c[1], inv); z = float.Parse(c[2], inv); hdg = float.Parse(c[3], inv);
                        if (map.NearestRoadPoint(new Vector2(x, z), 40f, false, out int ei, out float s, out float dist)) y = map.edges[ei].YAt(s);
                    }
                    var p2 = new Vector2(x, z);
                    string[] variants = c.Length > 4 && c[4].Trim().Length > 0 ? c[4].Trim().Split(',') : new[] { "nightrain", "noon" };
                    var fwd = new Vector3(Mathf.Sin(hdg * Mathf.Deg2Rad), 0f, Mathf.Cos(hdg * Mathf.Deg2Rad));
                    var rot = Quaternion.LookRotation(fwd, Vector3.up);
                    var pos = new Vector3(x, y + 0.35f, z);
                    foreach (var variant in variants)
                    {
                        bool rain = variant == "nightrain";
                        int hour = rain ? TimeOfDay.Night : TimeOfDay.Noon;
                        RaceHandoff.CalendarDay = 0;
                        RaceHandoff.WeatherOverride = rain ? (int)Weather.Rain : (int)Weather.Clear;
                        if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                        world.DropAll();
                        world.EnsureRing(pos + fwd * 60f, 2);
                        TimeOfDay.Apply(hour, sun);
                        if (globals != null) globals.Apply();
                        var hd = TimeOfDay.At(hour);
                        NightGlow.PreviewAll(hd.lightsOn);
                        bool lit = hd.lightsOn || Seasons.LightsOn(Seasons.CurrentWeather);
                        foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) l.PreviewBuild(lit, false);
                        PSXTexDecode.StampAll();
                        cam.ResetProjectionMatrix();
                        ChaseCamera.SteadyPose(ChaseCamera.View.Chase, 16f / 9f, 0f, ChaseCamera.DefaultSpeedFullMps, player.transform,
                                               ChaseCamera.FrameOf(player), default, out Vector3 eye, out Quaternion look, out float fov, out float shift);
                        cam.fieldOfView = fov;
                        cam.transform.SetPositionAndRotation(eye, look);
                        if (globals != null)
                        {
                            globals.adapt = ExposureAdapt.SteadyFor(eye, player.transform, sun != null ? -sun.transform.forward : Vector3.up,
                                                                    globals.night, path, def, globals.dayFill);
                            globals.Apply();
                        }
                        if (rain) WeatherFx.Preview(Weather.Rain, cam, 2.5f);
                        CarLights.PushGlobals();
                        StreetLights.Push(eye, look * Vector3.forward);
                        SunShadows.RenderFor(cam);
                        cam.aspect = 16f / 9f;
                        if (Mathf.Abs(shift) > 1e-5f) cam.projectionMatrix = ChaseCamera.ShiftedProjection(cam.fieldOfView, cam.aspect, cam.nearClipPlane, cam.farClipPlane, shift);
                        PSXScreenshotTool.ShotAs(cam, $"control/{id}_{variant}_{tag}", eye, look);
                        cam.ResetProjectionMatrix(); cam.ResetAspect();
                        if (rain) WeatherFx.PreviewClear();
                        shots++;
                    }
                    // the junction controls within 90 m ahead of the car, as the game decided them
                    log.Append(string.Format(inv, "{0}\tcar ({1:0.0},{2:0.00},{3:0.0}) heading {4:0.0}\n", id, pos.x, pos.y, pos.z, hdg));
                    foreach (var j in CitySignals.All(map, trims))
                    {
                        var d = j.centre - p2;
                        if (d.magnitude > 90f || Vector2.Dot(d, new Vector2(fwd.x, fwd.z)) < -10f) continue;
                        var sbA = new StringBuilder();
                        foreach (var a in j.approaches) sbA.Append($" e{a.edge}'{map.edges[a.edge].name}':{(j.signal ? "sig" : a.ctl == 1 ? "YIELD" : "STOP")}");
                        log.Append(string.Format(inv, "\t{0} {1}{2} at ({3:0},{4:0}), {5:0} m: nodes {6};{7}\n", j.signal ? "SIGNAL" : j.allWay ? "ALL-WAY STOP" : j.YieldOnly ? "YIELD" : "STOP",
                            j.warrant ? "(default) " : "", "", j.centre.x, j.centre.y, d.magnitude, string.Join(",", j.nodes), sbA));
                    }
                }
                log.Append($"{shots} frames to {dir}\nCONTROL SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("CONTROL SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, $"control_shots_{tag}.txt"), log.ToString());
                Debug.Log("[ControlShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
