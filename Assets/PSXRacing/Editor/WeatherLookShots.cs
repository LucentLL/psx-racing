using System.Text;
using UnityEngine;
using PSXRacing.City;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WEATHER LOOK, before and after (2026-10-26, the owner from the NoDa
    /// start in the snow: "The roads look terrible during snow. I thought it was
    /// broken textures. The skybox has not been updated to have overcast skies.
    /// Rain or snow, only a few clouds in the sky."). One view - the TRYON
    /// SPRINT's start line (the race the HUD calls NODA), the chase eye behind
    /// the player's car, noon - under states the CLIMATE produces at Charlotte
    /// (found by searching ten simulated years, not forced), each in the sky
    /// modes the owner can pick:
    ///   snowfall  : snow falling - PHOTO, DYNAMIC on a phone (the old puffs'
    ///               fallback), DYNAMIC on a desktop (the volumetric clouds)
    ///   snowroad  : an untreated road under fresh snow (PHOTO)
    ///   slush     : a treated road gone to slush (PHOTO)
    ///   rain      : rain falling - PHOTO and DYNAMIC on a phone
    /// Frames: Screenshots\wl_{PSX_WL_STAGE}_{tag}_{mode}.png; the states each
    /// frame shows go to PSXRacing_weatherlook.txt.
    ///   -executeMethod PSXRacing.EditorTools.WeatherLookShots.Capture
    /// </summary>
    public static class WeatherLookShots
    {
        delegate bool Want(in WeatherNow w);

        static StringBuilder notes;

        /// <summary>The climate self-test, the GLES3 compile check, then the
        /// frames: one Unity job.</summary>
        public static void CaptureWithChecks()
        {
            LifeSimSelfTest.RunWeather();
            int fails = VolCloudsCheck.Run();
            Debug.Log("[WeatherLook] VolCloudsCheck failures: " + fails);
            Capture();
        }

        public static void Capture()
        {
            notes = new StringBuilder();
            string stage = System.Environment.GetEnvironmentVariable("PSX_WL_STAGE");
            if (string.IsNullOrEmpty(stage)) stage = "after";
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Note("no Charlotte scene"); Flush(); return; }
            foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) cv.enabled = false;
            var map = CityMap.Get();
            if (map == null) { Note("no city map"); Flush(); return; }
            PSXRacingBuilder.EnsureCityTextures();

            if (!RouteStand(map, "tryon", out Vector3 pos, out Quaternion rot)) { Note("no tryon route"); Flush(); return; }
            var car = player.GetComponent<CarController>();
            if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
            Vector3 fwd = rot * Vector3.forward;
            Vector3 eye = pos - fwd * 6.2f + Vector3.up * 2.1f;
            Quaternion look = Quaternion.LookRotation(pos + Vector3.up * 0.9f + fwd * 22f - eye);

            var world = Object.FindAnyObjectByType<CityWorld>();
            GameObject made = null;
            if (world == null) { made = new GameObject("~wlWorld"); world = made.AddComponent<CityWorld>(); }
            world.EnsureRing(pos, 2);

            int oldDay = RaceHandoff.CalendarDay, oldOverride = RaceHandoff.WeatherOverride;
            bool oldDyn = SkyModePrefs.Dynamic;
            int hour = TimeOfDay.Noon;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            try
            {
                RaceHandoff.WeatherOverride = -1;
                Seasons.VenueOverride = "Charlotte";
                // Charlotte's climate never snows hard at noon in ten years: the
                // bench's SNOW (what the owner's NODA start shows - overcast,
                // snowing, the treated road gone to slush) on a January day.
                Shoot(cam, stage, "snowfall", hour, sun, eye, look, new[] { "photo", "phone", "desktop" },
                      (in WeatherNow w) => w.precip == PrecipKind.Snow && w.precipRate >= 0.6f && !w.fog, Weather.Snow, false);
                Shoot(cam, stage, "snowroad", hour, sun, eye, look, new[] { "photo" },
                      (in WeatherNow w) => !w.treated && w.road.snow > 0.7f && !w.fog, Weather.Snow, true);
                Shoot(cam, stage, "slush", hour, sun, eye, look, new[] { "photo" },
                      (in WeatherNow w) => w.treated && w.road.slush > 0.6f && !w.fog);
                Shoot(cam, stage, "rain", hour, sun, eye, look, new[] { "photo", "phone" },
                      (in WeatherNow w) => w.precip == PrecipKind.Rain && w.precipRate >= 0.7f && !w.fog);
            }
            finally
            {
                Seasons.Forced = null;
                RaceHandoff.CalendarDay = oldDay; RaceHandoff.WeatherOverride = oldOverride;
                Seasons.VenueOverride = null; Seasons.Invalidate();
                VolumetricClouds.Override = null;
                SkyModePrefs.Dynamic = oldDyn;
                WeatherFx.PreviewClear();
                if (made != null) { world.DropAll(); Object.DestroyImmediate(made); }
                Flush();
            }
            Debug.Log("[WeatherLook] done");
        }

        static void Shoot(Camera cam, string stage, string tag, int hour, Light sun, Vector3 eye, Quaternion look,
                          string[] modes, Want want, Weather bench = Weather.Clear, bool untreatedSnow = false)
        {
            bool found = Find(want, hour, out int day);
            if (!found && bench == Weather.Clear) { Note("NOT FOUND " + tag); return; }
            if (!found) day = LifeRules.DayNumber(new System.DateTime(2000, 1, 20));
            RaceHandoff.CalendarDay = day;
            Seasons.Forced = null;
            Seasons.Invalidate();
            var w = Seasons.At(day, hour, "Charlotte");
            if (!found)
            {
                w = Seasons.BenchWeather(bench, w);
                if (untreatedSnow)
                {
                    // an untreated street under fresh snow: the plough not yet by
                    w.treated = false;
                    w.road = new DeckState { tempC = w.road.tempC, snow = 1f };
                }
                Note(tag + ": not in the climate at this hour - the bench's " + bench + (untreatedSnow ? ", road untreated" : ""));
            }
            Seasons.Forced = w;
            var sd = Object.FindAnyObjectByType<SeasonDress>();
            if (sd != null)
            {
                var reg = typeof(SeasonDress).GetMethod("Register",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                reg?.Invoke(sd, null);
                sd.Apply(Seasons.CurrentDress);
            }
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            foreach (string mode in modes)
            {
                SkyModePrefs.Dynamic = mode != "photo";
                VolumetricClouds.Override = mode == "desktop";
                TimeOfDay.Apply(hour, sun);
                if (globals != null) globals.Apply();
                if (mode != "photo") DynamicSky.PrimeClouds(eye);
                var fall = Seasons.CurrentPrecipWeather;
                if (fall == Weather.Rain || fall == Weather.Snow) WeatherFx.Preview(fall, cam, 2.5f);
                else WeatherFx.PreviewClear();
                string file = "wl_" + stage + "_" + tag + "_" + mode;
                PSXScreenshotTool.ShotAs(cam, file, eye, look);
                WeatherFx.PreviewClear();
                var now = Seasons.Now;
                Note(file + ": " + LifeRules.DateLabel(day) + " " + TimeOfDay.Label(hour) + " | " + Seasons.SkyLine(now) +
                     " | sky " + now.sky + ", precip " + now.precip + " " + now.precipRate.ToString("0.00") +
                     ", light " + Seasons.CurrentWeather + ", volumetric " + (VolumetricClouds.Active ? "ON" : "off") +
                     " | road water " + now.road.water.ToString("0.00") + " slush " + now.road.slush.ToString("0.00") +
                     " snow " + now.road.snow.ToString("0.00") + " ice " + now.road.ice + ", treated " + now.treated +
                     " | globals roadSnow " + (globals != null ? globals.roadSnow.ToString("0.00") : "?") +
                     " roadSlush " + (globals != null ? globals.roadSlush.ToString("0.00") : "?") +
                     " wetWater " + (globals != null ? globals.wetWater.ToString("0.00") : "?"));
            }
        }

        static bool Find(Want want, int hour, out int day)
        {
            int d0 = LifeRules.DayNumber(new System.DateTime(1999, 1, 1)), d1 = LifeRules.DayNumber(new System.DateTime(2009, 1, 1));
            for (int d = d0; d < d1; d++)
            {
                var w = Seasons.At(d, hour, "Charlotte");
                if (want(w)) { day = d; return true; }
            }
            day = 0;
            return false;
        }

        /// <summary>The route's start line, facing the way the race runs, in
        /// the right-hand lane of a two-way street.</summary>
        static bool RouteStand(CityMap map, string id, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var r = map.RouteById(id);
            if (r == null || r.edges == null || r.dirs == null) return false;
            float acc = 0f;
            for (int k = 0; k < r.edges.Length && k < r.dirs.Length; k++)
            {
                var e = map.edges[r.edges[k]];
                if (acc + e.length < r.startM) { acc += e.length; continue; }
                float into = Mathf.Clamp(r.startM - acc, 0f, e.length);
                bool fwdDir = r.dirs[k] > 0;
                float s = fwdDir ? into : e.length - into;
                Vector2 p = e.PointAt(s), t2 = e.TangentAt(s) * (fwdDir ? 1f : -1f);
                float s0 = Mathf.Max(0f, s - 3f), s1 = Mathf.Min(e.length, s + 3f);
                float rise = s1 > s0 ? (e.YAt(s1) - e.YAt(s0)) / (s1 - s0) * (fwdDir ? 1f : -1f) : 0f;
                Vector3 fwd = new Vector3(t2.x, rise, t2.y).normalized;
                Vector3 right = new Vector3(t2.y, 0f, -t2.x).normalized;
                float lateral = e.oneway ? 0f : e.width * 0.25f;
                pos = new Vector3(p.x, e.YAt(s) + 0.35f, p.y) + right * lateral;
                rot = Quaternion.LookRotation(fwd, Vector3.up);
                Note("tryon start: edge '" + e.name + "' at (" + pos.x.ToString("0") + ", " + pos.z.ToString("0") + ")");
                return true;
            }
            return false;
        }

        static void Note(string s) { notes.AppendLine(s); Debug.Log("[WeatherLook] " + s); }

        static void Flush() =>
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../PSXRacing_weatherlook.txt"), notes.ToString());
    }
}
