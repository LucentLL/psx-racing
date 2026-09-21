using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DAY LOOK, PHOTOGRAPHED (2026-09-21, the Forza daylight pass).
    ///
    /// The owner, with five daylight frames: improved lighting "during
    /// morning, day, and afternoon ... when headlights and street lights are
    /// not the major driving factors of the lighting." NightLookShots is the
    /// instrument for the hours the lamps own; this is the one for the hours
    /// the sun does, and it photographs what that pass is made of:
    ///
    ///   * EVERY FRAME TWICE - with the sun's shadow map and without it
    ///     (dl_*_shadow.png / dl_*_flat.png). The pair is what
    ///     tools/day/day_stats.py measures: which pixels the map darkened, and
    ///     by how much - the sun-to-shade ratio, which in the reference frames
    ///     is 3.5 to 1 in linear light, with the shade BLUE.
    ///   * WHERE SHADOWS HAPPEN: the circuit's buildings and trees, three
    ///     kinds of Charlotte street (the towers of an arterial, a motorway,
    ///     a tree-lined residential street), a mountain stage's forest, and a
    ///     TUNNEL from outside looking in and from inside looking out (the
    ///     owner's fourth and fifth frames).
    ///   * MORNING, NOON, AFTERNOON everywhere, SUNSET on the circuit (the
    ///     longest shadows the game draws), and one OVERCAST noon, where the
    ///     map must all but switch itself off.
    ///
    ///   tools\daylook-shots.ps1 -> Screenshots\dl_*.png, dl_log.txt
    ///   PSX_DAY_ONLY=circuit,city,stage,tunnel restricts the venues.
    /// </summary>
    public static class DayLookShots
    {
        static string RootDir => Directory.GetParent(Application.dataPath).FullName;
        static string OutDir => Path.Combine(RootDir, "Screenshots");
        const float CarLift = 0.35f;

        static StringBuilder log;
        static int shots, failures, rainDay;

        [MenuItem("PSX Racing/Day Look Shots")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutDir);
            log = new StringBuilder();
            shots = 0;
            failures = 0;
            Line("DAY LOOK SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            var only = Only();
            int keepDay = RaceHandoff.CalendarDay;
            var keepView = ChaseCamera.Current;
            rainDay = 0;
            for (int d = 1; d <= 400 && rainDay == 0; d++)
                if (Seasons.WeatherFor(d) == Weather.Rain) rainDay = d;
            try
            {
                ChaseCamera.PreviewView(ChaseCamera.View.Chase);
                if (only == null || only.Contains("circuit")) Venue("circuit", CaptureCircuit);
                if (only == null || only.Contains("city")) Venue("city", CaptureCity);
                if (only == null || only.Contains("stage")) Venue("stage", CaptureStage);
                if (only == null || only.Contains("tunnel")) Venue("tunnel", CaptureTunnel);
            }
            finally
            {
                RaceHandoff.CalendarDay = keepDay;
                ChaseCamera.PreviewView(keepView);
                Line(shots + " frames written to " + OutDir);
                Line(failures == 0 && shots > 0
                    ? "DAY LOOK CAPTURE OK"
                    : "DAY LOOK CAPTURE FAILED (" + failures + " venue(s) threw or were skipped, " + shots + " frames)");
                File.WriteAllText(Path.Combine(OutDir, "dl_log.txt"), log.ToString());
                Debug.Log("[DayLook] " + shots + " frames written to " + OutDir + "\n" + log);
            }
        }

        // ------------------------------------------------------------------
        //  The venues
        // ------------------------------------------------------------------

        static void CaptureCircuit()
        {
            var def = TrackCatalog.At(0);
            Line(def.id + ":");
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Skipped("the scene did not open (run the scene build)"); return; }
            var sun = NightLookShots.FindSun();
            var t = player.transform;
            string venue = def.id.ToLowerInvariant();
            Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
            var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye);
            Vector3 grid = t.TransformPoint(new Vector3(3.6f, 1.6f, 6.0f));
            var gridRot = Quaternion.LookRotation(t.position + Vector3.up * 0.6f - grid);
            foreach (int hour in new[] { TimeOfDay.Morning, TimeOfDay.Noon, TimeOfDay.Afternoon, TimeOfDay.Sunset })
            {
                Begin(hour, false, sun);
                Pair(cam, venue, "chase", hour, false, eye, chaseRot);
                Pair(cam, venue, "34", hour, false, grid, gridRot);
            }
            if (rainDay > 0)
            {
                Begin(TimeOfDay.Noon, true, sun);
                Pair(cam, venue, "chase", TimeOfDay.Noon, true, eye, chaseRot);
            }
        }

        static void CaptureCity()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
            Line("Charlotte:");
            if (def.id != "Charlotte") { Skipped("no Charlotte venue in the catalog"); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Skipped("the scene did not open (run the scene build)"); return; }
            var world = Object.FindAnyObjectByType<CityWorld>();
            var map = CityMap.Get();
            if (world == null || map == null) { Skipped(world == null ? "the scene has no CityWorld" : "charlotte_city.bytes is missing"); return; }
            var sun = NightLookShots.FindSun();
            var car = player.GetComponent<CarController>();

            var loop = TrackCatalog.At(TrackCatalog.IndexOf("UptownLoop"));
            string routeId = loop.id == "UptownLoop" && !string.IsNullOrEmpty(loop.cityRoute) ? loop.cityRoute : "uptown";
            Vector2 start = NightLookShots.RouteStart(map, routeId, map.uptown);

            var spots = new List<(string name, CityMap.Edge e, float s)>();
            Nearest(spots, "arterial", map, start, 4000f, e => !e.link && !e.bridge && !e.tunnel && (e.cls == 2 || e.cls == 3));
            Nearest(spots, "motorway", map, map.uptown, 12000f, e => !e.link && !e.bridge && !e.tunnel && e.cls >= 5);
            Nearest(spots, "residential", map, map.uptown, 8000f, e => !e.link && !e.bridge && !e.tunnel && e.cls == 0);
            // Downtown proper: the towers are what cast across a street.
            Nearest(spots, "uptown", map, map.uptown, 1500f, e => !e.link && !e.bridge && !e.tunnel && e.cls >= 1 && e.cls <= 3);

            foreach (var sp in spots)
            {
                NightLookShots.Stand(sp.e, sp.s, out Vector3 pos, out Quaternion rot);
                world.EnsureRing(pos, 2);
                if (car != null) car.TeleportTo(pos, rot);
                else player.transform.SetPositionAndRotation(pos, rot);
                var t = player.transform;
                Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
                var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye);
                Line($"  {sp.name}: '{sp.e.name}' cls {sp.e.cls}, at ({pos.x:0}, {pos.z:0})");
                foreach (int hour in new[] { TimeOfDay.Morning, TimeOfDay.Noon, TimeOfDay.Afternoon })
                {
                    Begin(hour, false, sun);
                    Pair(cam, "charlotte", sp.name, hour, false, eye, chaseRot);
                }
            }
        }

        static void CaptureStage()
        {
            var def = TrackCatalog.At(TrackCatalog.IndexOf("BlueRidge"));
            Line("BlueRidge:");
            if (def.id != "BlueRidge") { Skipped("no BlueRidge venue in the catalog"); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Skipped("the scene did not open (run the scene build)"); return; }
            var sun = NightLookShots.FindSun();
            var t = player.transform;
            Vector3 eye = t.position - t.forward * 8f + Vector3.up * 2.6f;
            var chaseRot = Quaternion.LookRotation(t.position + Vector3.up * 0.8f + t.forward * 20f - eye);
            foreach (int hour in new[] { TimeOfDay.Morning, TimeOfDay.Noon, TimeOfDay.Afternoon })
            {
                Begin(hour, false, sun);
                Pair(cam, def.id.ToLowerInvariant(), "chase", hour, false, eye, chaseRot);
            }
        }

        /// <summary>The first tunnel of the first stage that has one, from
        /// 25 m outside looking in and from 40 m inside looking at the mouth:
        /// the owner's fourth and fifth frames.</summary>
        static void CaptureTunnel()
        {
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                var d = TrackCatalog.At(i);
                // A stage's spans live in its JSON and are read on demand.
                if (d.stage) TrackCatalog.EnsureStage(d);
                if (d.tunnels != null && d.tunnels.Length > 0 && !d.Reversed) { def = d; break; }
            }
            Line("tunnel:");
            if (def == null) { Skipped("no venue in the catalog has a tunnel"); return; }
            Line("  " + def.id);
            if (!PSXScreenshotTool.Open(def, out var cam, out var player)) { Skipped("the scene did not open (run the scene build)"); return; }
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count == 0) { Skipped("no TrackPath in " + def.id); return; }
            var sun = NightLookShots.FindSun();
            var car = player.GetComponent<CarController>();
            var span = def.tunnels[0];

            foreach (var (name, metres) in new[] { ("mouth", span.x - 25f), ("inside", span.y - 40f) })
            {
                int i = path.Wrap(Mathf.RoundToInt(metres / path.spacing));
                Vector3 fwd = path.GetTangent(i).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 pos = path.GetPoint(i) + right * 2.2f + Vector3.up * CarLift;
                var rot = Quaternion.LookRotation(fwd, Vector3.up);
                if (car != null) car.TeleportTo(pos, rot);
                else player.transform.SetPositionAndRotation(pos, rot);
                Vector3 eye = pos - fwd * 8f + Vector3.up * 2.3f;
                var chaseRot = Quaternion.LookRotation(pos + Vector3.up * 0.8f + fwd * 20f - eye);
                foreach (int hour in new[] { TimeOfDay.Noon, TimeOfDay.Afternoon })
                {
                    Begin(hour, false, sun);
                    Pair(cam, def.id.ToLowerInvariant(), "tunnel_" + name, hour, false, eye, chaseRot);
                }
            }
        }

        // ------------------------------------------------------------------
        //  One hour, and one pair of frames
        // ------------------------------------------------------------------

        static void Begin(int hour, bool rain, Light sun)
        {
            RaceHandoff.CalendarDay = rain ? rainDay : 0;
            TimeOfDay.Apply(hour, sun);
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (globals != null) globals.Apply();
            var preset = TimeOfDay.At(hour);
            NightGlow.PreviewAll(preset.lightsOn);
            bool carsLit = preset.lightsOn || Seasons.LightsOn(Seasons.CurrentWeather);
            foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude))
                lights.PreviewBuild(carsLit);
            foreach (var c in Object.FindObjectsByType<GaugeCluster>(FindObjectsInactive.Exclude))
                c.Build();
            foreach (var hud in Object.FindObjectsByType<RaceHUD>(FindObjectsInactive.Exclude))
                HudOnTop.Apply(hud.gameObject);
            Canvas.ForceUpdateCanvases();
            Line($"  [{preset.name.ToLowerInvariant()}{(rain ? "_rain" : "")}] weather {Seasons.CurrentWeather}  " +
                 $"shadow {(globals != null ? globals.sunShadow : 0f):0.00}  fogSun {(globals != null ? globals.fogSun : default)}  " +
                 $"sun {(sun != null ? sun.transform.eulerAngles.x : 0f):0} deg up");
        }

        /// <summary>The frame as the game draws it, then the same frame with
        /// the shadow map switched off and everything else left alone.</summary>
        static void Pair(Camera cam, string venue, string spot, int hour, bool rain, Vector3 eye, Quaternion rot)
        {
            string stem = "dl_" + venue + "_" + spot + "_" + TimeOfDay.At(hour).name.ToLowerInvariant() + (rain ? "_rain" : "");
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            float keep = globals != null ? globals.sunShadow : 0f;
            float keepSky = globals != null ? globals.skyShade : 0f;

            var keepPos = cam.transform.position;
            var keepRot = cam.transform.rotation;
            cam.transform.SetPositionAndRotation(eye, rot);
            CarLights.PushGlobals();
            StreetLights.Push(eye, rot * Vector3.forward);

            SunShadows.RenderFor(cam);
            PSXScreenshotTool.ShotAs(cam, stem + "_shadow", eye, rot);
            int drawn = SunShadows.DrawnLast, known = SunShadows.Known, calls = SunShadows.DrawCallsLast;

            int roofs = SunShadows.SkyDrawnLast;
            if (globals != null) { globals.sunShadow = 0f; globals.skyShade = 0f; globals.Apply(); }
            PSXScreenshotTool.ShotAs(cam, stem + "_flat", eye, rot);
            if (globals != null) { globals.sunShadow = keep; globals.skyShade = keepSky; globals.Apply(); }

            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            shots += 2;
            string msg = $"{stem}  casters drawn {drawn} of {known} known, {calls} draw calls; {roofs} in the sky map";
            Line("    " + msg);
            Debug.Log("[DayLook] " + msg);
        }

        // ------------------------------------------------------------------
        //  Plumbing
        // ------------------------------------------------------------------

        static void Nearest(List<(string name, CityMap.Edge e, float s)> spots, string name, CityMap map,
                            Vector2 near, float maxDist, System.Func<CityMap.Edge, bool> want)
        {
            var cands = NightLookShots.Candidates(map, near, want, maxDist);
            if (cands.Count == 0) { Line("  no " + name + " street within " + maxDist + " m"); return; }
            spots.Add((name, cands[0].e, cands[0].s));
        }

        static HashSet<string> Only()
        {
            string only = System.Environment.GetEnvironmentVariable("PSX_DAY_ONLY");
            if (string.IsNullOrWhiteSpace(only)) return null;
            var set = new HashSet<string>();
            foreach (var s in only.Split(',', ';', ' '))
                if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim().ToLowerInvariant());
            return set.Count > 0 ? set : null;
        }

        static void Skipped(string why) { failures++; Line("  skipped: " + why); }

        static void Venue(string name, System.Action body)
        {
            try { body(); }
            catch (System.Exception e)
            {
                failures++;
                Line("  ERROR " + name + " threw " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
        }

        static void Line(string s) => log.AppendLine(s);
    }
}
