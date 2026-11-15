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
    /// THE DECK PLATES' PICTURES (2026-11-15, agent "spui"): frames from eyes
    /// that ride an edge's own surface, so the same spec shot in two jobs
    /// (PSX_CITY_SPUI=0, then on) is the same pose before and after.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunSpui
    /// PSX_SPUI_SHOTS: entries split by ';'
    ///   id=E:s:lat:h>E2:s2:lat2:h2   the eye over edge E at arc s (negative:
    ///       from its far end), lat metres to the left of its direction, h
    ///       over its surface; looking at the point given the same way.
    ///   id=E:s:lat:h                 straight down from there (a top view).
    /// PSX_SPUI_TAG: the frames' suffix (before / after).
    /// Output: Screenshots/spui/&lt;id&gt;_&lt;tag&gt;.png, spui_log_&lt;tag&gt;.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        static bool SpuiPoint(CityMap map, string spec, out Vector3 p)
        {
            p = default;
            var f = spec.Split(':');
            var inv = CultureInfo.InvariantCulture;
            if (f.Length < 4 || !int.TryParse(f[0].TrimStart('e'), out int ei) || ei < 0 || ei >= map.edges.Length) return false;
            var e = map.edges[ei];
            float s = float.Parse(f[1], inv), lat = float.Parse(f[2], inv), h = float.Parse(f[3], inv);
            if (s < 0f) s = e.length + s;
            s = Mathf.Clamp(s, 0f, e.length);
            var q = e.PointAt(s); var t = e.TangentAt(s);
            var left = new Vector2(-t.y, t.x);
            q += left * lat;
            p = new Vector3(q.x, e.YAt(s) + h, q.y);
            return true;
        }

        public static void RunSpui()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_SPUI_TAG");
            if (string.IsNullOrEmpty(tag)) tag = CityDeckPlates.On ? "after" : "before";
            var log = new StringBuilder("DECK PLATES SHOTS (" + tag + ") " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "spui");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            try
            {
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                log.Append(CityDeckPlates.Report(map));
                world.DropAll();
                RaceHandoff.CalendarDay = 0;
                RaceHandoff.WeatherOverride = -1;
                int hour = HourOf("noon");
                TimeOfDay.Apply(hour, sun);
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                if (globals != null) globals.Apply();
                NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_SPUI_SHOTS") ?? "").Split(';'))
                {
                    string it = item.Trim();
                    int eq = it.IndexOf('=');
                    if (eq <= 0) continue;
                    string id = it.Substring(0, eq);
                    var parts = it.Substring(eq + 1).Split('>');
                    if (!SpuiPoint(map, parts[0], out var eye)) { log.Append($"{id}: bad eye '{parts[0]}'\n"); continue; }
                    Quaternion r;
                    if (parts.Length > 1)
                    {
                        if (!SpuiPoint(map, parts[1], out var look)) { log.Append($"{id}: bad look '{parts[1]}'\n"); continue; }
                        r = Quaternion.LookRotation(look - eye);
                    }
                    else r = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    world.EnsureRing(eye, 1);
                    PSXTexDecode.StampAll();
                    var hide = eye + Vector3.down * 60f;
                    if (parts.Length == 1) hide = eye + new Vector3(400f, -60f, 0f);
                    if (car != null) car.TeleportTo(hide, r); else player.transform.SetPositionAndRotation(hide, r);
                    CarLights.PushGlobals();
                    PSXScreenshotTool.ShotAs(cam, $"spui/{id}_{tag}", eye, r);
                    log.Append($"{id}\teye ({eye.x:0.0},{eye.y:0.00},{eye.z:0.0})\n");
                    shots++;
                }
                log.Append($"cut {CityMeshes.DeckPlateCutTris} triangles, slab {CityMeshes.DeckPlateSlabTris}, kept beyond {CityMeshes.DeckPlateLeftTris}, on {CityMeshes.DeckPlateTileCuts} tile builds, {CityMeshes.DeckPlateMs:0} ms\n");
                log.Append($"{shots} frames to {dir}\nDECK PLATES SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("DECK PLATES SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, $"spui_log_{tag}.txt"), log.ToString());
                Debug.Log("[SpuiShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
