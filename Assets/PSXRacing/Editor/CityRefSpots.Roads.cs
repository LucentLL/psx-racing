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
    /// THE ROAD REVIEW'S PICTURES (2026-11-15, "Please review all roads for
    /// accuracy"): the game's frame at each place a public street-level view
    /// was looked at, from the same point and heading, for the corridors a
    /// Charlotte race track is likely to use. The places come from a file
    /// (PSX_ROADS_SPOTS: rows "id, edge, s, dir, x, z, heading, corridor,
    /// what", tab separated - the round's poses.mjs snaps each to the game's
    /// own edge, so the game frame and the street-level view share one
    /// lat/lon and heading). Two frames a place:
    ///   eye   the driver's eye, 1.15 m over the inner through lane
    ///   high  7 m up and 14 m back, looking 70 m ahead: the cross-section
    ///         (lanes, median, turn lane, curb, walk) legible in one picture
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunRoads
    /// Output: Screenshots/roads/&lt;id&gt;_&lt;eye|high&gt;.png and roads_log.txt
    /// (what the game has at each place). PSX_ROADS_ONLY=id,id shoots some.
    /// Nothing is read from or written to the street-level source.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunRoads()
        {
            var log = new StringBuilder("ROADS REVIEW SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "roads");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            int code = 0, shots = 0;
            try
            {
                string file = System.Environment.GetEnvironmentVariable("PSX_ROADS_SPOTS");
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) throw new System.Exception("PSX_ROADS_SPOTS names no file");
                string only = System.Environment.GetEnvironmentVariable("PSX_ROADS_ONLY");
                var onlySet = new HashSet<string>();
                if (!string.IsNullOrWhiteSpace(only)) foreach (var s in only.Split(',')) if (s.Trim().Length > 0) onlySet.Add(s.Trim());

                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var trims = CityMeshes.NodeTrims(map);
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                log.Append($"graph hash {CityMap.GraphHashOf(map.edges):x8}; turn pockets {(CityMeshes.TurnPocketsOn ? "on" : "off")}\n");

                // PSX_ROADS_AB=Field:1 - a public static bool of CityMeshes and its BEFORE value: two pairs a place
                System.Reflection.FieldInfo abField = null; bool abBefore = false;
                string ab = System.Environment.GetEnvironmentVariable("PSX_ROADS_AB");
                if (!string.IsNullOrWhiteSpace(ab))
                {
                    var kv = ab.Split(':');
                    abField = typeof(CityMeshes).GetField(kv[0].Trim(), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (abField == null || abField.FieldType != typeof(bool)) throw new System.Exception("PSX_ROADS_AB: CityMeshes has no public static bool " + kv[0]);
                    abBefore = kv.Length > 1 && kv[1].Trim() == "1";
                    log.Append($"A/B on CityMeshes.{abField.Name}: before = {abBefore}, after = {!abBefore}\n");
                }
                RaceHandoff.CalendarDay = 0;
                RaceHandoff.WeatherOverride = -1;
                TimeOfDay.Apply(TimeOfDay.Noon, sun);
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                if (globals != null) globals.Apply();
                NightGlow.PreviewAll(TimeOfDay.At(TimeOfDay.Noon).lightsOn);
                PSXTexDecode.StampAll();

                foreach (var row in File.ReadAllLines(file))
                {
                    var c = row.Split('\t');
                    if (c.Length < 9) continue;
                    string id = c[0];
                    if (onlySet.Count > 0 && !onlySet.Contains(id)) continue;
                    if (!int.TryParse(c[1], out int ei) || ei < 0 || ei >= map.edges.Length) { log.Append($"{id}\tBAD EDGE {c[1]}\n"); continue; }
                    float s = float.Parse(c[2], CultureInfo.InvariantCulture);
                    int dirSign = c[3].Trim() == "-1" ? -1 : 1;
                    var e = map.edges[ei];
                    s = Mathf.Clamp(s, 0f, e.length);
                    var t2 = e.TangentAt(s) * dirSign;
                    var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                    var rightV = Vector3.Cross(Vector3.up, fh).normalized;
                    Vector2 p = e.PointAt(s);
                    var pr = RoadProfiles.All[Mathf.Clamp(e.profile, 0, RoadProfiles.All.Length - 1)];
                    // the inner through lane of our direction (two-way), the lane right of the middle (one-way)
                    float lat = e.oneway ? (pr.lanes % 2 == 0 ? RoadProfiles.LaneM * 0.5f : 0f)
                                         : RoadProfiles.LaneM * 0.5f + (pr.turnLane ? RoadProfiles.LaneM * 0.5f : 0f);
                    float y = e.YAt(s);
                    var road = new Vector3(p.x, y, p.y) + rightV * lat;
                    var rot = Quaternion.LookRotation(fh, Vector3.up);

                    Vector3 eye = road + Vector3.up * 1.15f;
                    Vector3 high = new Vector3(p.x, y, p.y) - fh * 14f + Vector3.up * 7f;
                    void Pair(string tag)
                    {
                        world.DropAll();
                        world.EnsureRing(road + fh * 200f, 2);
                        var park = road - fh * 60f;
                        if (car != null) car.TeleportTo(park + Vector3.up * 0.35f, rot); else player.transform.SetPositionAndRotation(park, rot);
                        CarLights.PushGlobals();
                        PSXScreenshotTool.ShotAs(cam, $"roads/{id}_eye{tag}", eye, Quaternion.LookRotation(fh * 40f + Vector3.down * 0.25f));
                        PSXScreenshotTool.ShotAs(cam, $"roads/{id}_high{tag}", high, Quaternion.LookRotation(new Vector3(p.x, y, p.y) + fh * 70f - high));
                        shots += 2;
                    }
                    if (abField == null) Pair("");
                    else
                    {
                        // the same pose with a switch as it was (before) and as it is now (after), the tiles built again between
                        abField.SetValue(null, abBefore); Pair("_before");
                        abField.SetValue(null, !abBefore); Pair("_after");
                    }

                    // what the game has here
                    int ahead = dirSign > 0 ? e.b : e.a;
                    string ctl = "none";
                    foreach (var j in CitySignals.All(map, trims))
                    {
                        bool at = false;
                        foreach (int n in j.nodes) if (n == ahead) { at = true; break; }
                        if (!at) continue;
                        ctl = j.signal ? (j.warrant ? "signal (warrant)" : "signal (OSM)") : j.allWay ? "all-way stop" : j.YieldOnly ? "yield" : "stop";
                        break;
                    }
                    string median = e.oneway && !e.link ? (e.cls >= 5 ? (CityMeshes.Barriered(e) ? "freeway, barriered" : "freeway") : e.cls == 4 ? (CityMeshes.TrunkIsland(e) ? "trunk, curbed island" : "trunk, concrete barrier") : "one-way carriageway") : (pr.turnLane ? "centre turn lane" : "undivided");
                    log.Append($"{id}\te{e.index} '{e.name}' cls {e.cls} {pr.key} lanes {pr.lanes}{(e.oneway ? " one-way" : "")} width {e.width:0.0} m{(e.bridge ? " BRIDGE" : "")}\t{median}\tnext junction n{ahead} ({map.nodeEdges[ahead].Count} arms, {Vector2.Distance(map.nodes[ahead], p):0} m ahead): {ctl}\teye ({eye.x:0.0},{eye.y:0.00},{eye.z:0.0}) heading {Mathf.Atan2(fh.x, fh.z) * Mathf.Rad2Deg:0}\t{c[7]}\t{c[8]}\n");
                }
                log.Append($"{shots} frames to {dir}\nROADS REVIEW SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("ROADS REVIEW SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "roads_log.txt"), log.ToString());
                Debug.Log("[RoadsShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
