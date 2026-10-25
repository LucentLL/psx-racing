using System.Text;
using UnityEngine;
using PSXRacing.City;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE WEATHER MATRIX (2026-10-25, weather layers): a handful of
    /// day/venue/hour combinations the CLIMATE actually produces - found by
    /// searching ten simulated years, not forced - rendered from the driver's
    /// eye, with the layers each frame shows written beside it.
    ///   wx_charlotte_snowground : Charlotte, snow lying, roads treated and wet, overcast
    ///   wx_blueridge_wetclear   : the Parkway soaked under a clear sky after the rain
    ///   wx_blueridge_snowdry    : snow on the mountain, a dry road, a clear sky
    ///   wx_emeraldisle_summer   : Bogue Banks, a clear summer noon
    /// Frames go to Screenshots\wx_*.png, the notes to PSXRacing_weathershots.txt.
    /// PSX_WX_ONLY=tag runs one.
    /// </summary>
    public static class WeatherShots
    {
        delegate bool Want(in WeatherNow w);

        struct Combo
        {
            public string tag, venue;
            public int[] hours;
            public int fromMonth, toMonth;
            public Want want;
        }

        static StringBuilder notes;

        /// <summary>The climate self-test, then the matrix: one Unity job.</summary>
        public static void RunWithSelfTest()
        {
            LifeSimSelfTest.RunWeather();
            Run();
        }

        public static void Run()
        {
            notes = new StringBuilder();
            string only = System.Environment.GetEnvironmentVariable("PSX_WX_ONLY");
            var combos = new[]
            {
                new Combo { tag = "charlotte_snowground", venue = "Charlotte", hours = new[] { TimeOfDay.Morning, TimeOfDay.Noon, TimeOfDay.Afternoon },
                    fromMonth = 12, toMonth = 3,
                    want = (in WeatherNow w) => w.scenery == SceneryKind.Snow && w.precip == PrecipKind.None && w.treated &&
                                                w.sky >= SkyKind.Overcast && w.road.water > 0.3f && w.road.snow < 0.05f && !w.road.ice },
                new Combo { tag = "blueridge_wetclear", venue = "BlueRidge", hours = new[] { TimeOfDay.Morning, TimeOfDay.Noon, TimeOfDay.Afternoon },
                    fromMonth = 4, toMonth = 10,
                    want = (in WeatherNow w) => w.precip == PrecipKind.None && w.sky <= SkyKind.PartlyCloudy && !w.fog && w.road.water > 0.6f },
                new Combo { tag = "blueridge_snowdry", venue = "BlueRidge", hours = new[] { TimeOfDay.Noon, TimeOfDay.Afternoon, TimeOfDay.Morning },
                    fromMonth = 11, toMonth = 3,
                    want = (in WeatherNow w) => w.scenery == SceneryKind.Snow && w.precip == PrecipKind.None && w.sky == SkyKind.Clear &&
                                                w.road.water < 0.05f && w.road.snow < 0.05f && w.road.slush < 0.05f && !w.road.ice },
                new Combo { tag = "emeraldisle_summer", venue = "EmeraldIsle", hours = new[] { TimeOfDay.Noon },
                    fromMonth = 6, toMonth = 8,
                    want = (in WeatherNow w) => w.sky == SkyKind.Clear && w.precip == PrecipKind.None && w.road.water < 0.05f && !w.fog },
            };
            int oldDay = RaceHandoff.CalendarDay, oldOverride = RaceHandoff.WeatherOverride;
            try
            {
                foreach (var c in combos)
                {
                    if (!string.IsNullOrEmpty(only) && c.tag != only) continue;
                    if (!Find(c, out int day, out int hour)) { Note("NOT FOUND " + c.tag); continue; }
                    Shoot(c, day, hour);
                }
            }
            finally
            {
                RaceHandoff.CalendarDay = oldDay; RaceHandoff.WeatherOverride = oldOverride;
                Seasons.VenueOverride = null; Seasons.Invalidate();
                WeatherFx.PreviewClear();
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../PSXRacing_weathershots.txt"), notes.ToString());
            }
            Debug.Log("[WeatherShots] done");
        }

        static void Note(string s) { notes.AppendLine(s); Debug.Log("[WeatherShots] " + s); }

        static bool Find(Combo c, out int day, out int hour)
        {
            int d0 = LifeRules.DayNumber(new System.DateTime(1999, 1, 1)), d1 = LifeRules.DayNumber(new System.DateTime(2009, 1, 1));
            for (int d = d0; d < d1; d++)
            {
                int m = Climate.DateOf(d).Month;
                bool inSeason = c.fromMonth <= c.toMonth ? m >= c.fromMonth && m <= c.toMonth : m >= c.fromMonth || m <= c.toMonth;
                if (!inSeason) continue;
                foreach (int h in c.hours)
                {
                    var w = Seasons.At(d, h, c.venue);
                    if (c.want(w)) { day = d; hour = h; return true; }
                }
            }
            day = 0; hour = 0;
            return false;
        }

        static void Shoot(Combo c, int day, int hour)
        {
            TrackCatalog.TrackDef def = null;
            for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == c.venue) def = TrackCatalog.At(i);
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { Note("no scene " + c.venue); return; }
            foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) cv.enabled = false;

            RaceHandoff.CalendarDay = day;
            RaceHandoff.WeatherOverride = -1;
            Seasons.VenueOverride = c.venue;
            Seasons.Invalidate();
            // The dress before anything (the city's tiles ask Substitute as they build).
            var sd = Object.FindAnyObjectByType<SeasonDress>();
            if (sd != null)
            {
                var reg = typeof(SeasonDress).GetMethod("Register",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                reg?.Invoke(sd, null);
            }
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            var globals = Object.FindFirstObjectByType<PSXGlobals>();
            TimeOfDay.Apply(hour, sun);     // stamps TimeOfDay.Current, which Now keys on
            Seasons.Invalidate();
            TimeOfDay.Apply(hour, sun);
            if (sd != null) sd.Apply(Seasons.CurrentDress);

            Vector3 eye; Quaternion rot;
            GameObject cityGo = null; CityWorld world = null;
            if (def.city)
            {
                var map = CityMap.Get();
                PSXRacingBuilder.EnsureCityTextures();
                if (!CitySpot(map, "I-277", out eye, out rot)) { Note("no city spot"); return; }
                cityGo = new GameObject("~wxWorld");
                world = cityGo.AddComponent<CityWorld>();
                world.EnsureRing(eye, world.ring);
            }
            else
            {
                var path = Object.FindFirstObjectByType<TrackPath>();
                if (path == null || path.Count < 50) { Note("no path " + c.venue); return; }
                int at = Mathf.Clamp(Mathf.RoundToInt(0.42f * path.Count), 2, path.Count - 12);
                eye = path.GetPoint(at) + Vector3.up * 1.5f;
                rot = Quaternion.LookRotation(path.GetPoint(at + 10) + Vector3.up * 1.2f - eye);
            }
            try
            {
                NightGlow.PreviewAll(hour == TimeOfDay.Night || hour == TimeOfDay.Dusk);
                if (globals != null) globals.Apply();
                var w = Seasons.Now;
                var fall = Seasons.CurrentPrecipWeather;
                if (fall == Weather.Rain || fall == Weather.Snow) WeatherFx.Preview(fall, cam, 2.5f);
                else WeatherFx.PreviewClear();
                string file = "wx_" + c.tag;
                PSXScreenshotTool.ShotAs(cam, file, eye, rot);
                var road = RoadSurface.OfDeck(w.road, RoadSurface.Patch(eye.x, eye.z));
                Note(file + ": " + c.venue + " " + LifeRules.DateLabel(day) + " " + TimeOfDay.Label(hour) +
                     "  |  " + Seasons.SkyLine(w) + "  |  " + Seasons.TempLine(w, true) +
                     "  |  light " + Seasons.CurrentWeather + ", dress " + Seasons.DressNames[Seasons.CurrentDress] +
                     ", sky tex " + (Seasons.CurrentWeather == Weather.Rain || Seasons.CurrentWeather == Weather.Snow ? "overcast" : "hour's") +
                     ", wetWater " + (globals != null ? globals.wetWater.ToString("0.00") : "?") +
                     ", roadSnow " + (globals != null ? globals.roadSnow.ToString("0.00") : "?") +
                     ", snow " + w.snowDepthCm.ToString("0.0") + " cm, treated " + w.treated +
                     ", grip here " + road.grip.ToString("0.00") + " (" + road.kind + "), bridges " +
                     RoadSurface.OfDeck(w.bridge, 1f).grip.ToString("0.00"));
            }
            finally
            {
                WeatherFx.PreviewClear();
                if (world != null) world.DropAll();
                if (cityGo != null) Object.DestroyImmediate(cityGo);
            }
        }

        /// <summary>RainShots' spot: the named road 1.2 km out from uptown, the
        /// driver's eye looking 25 m on.</summary>
        static bool CitySpot(CityMap map, string road, out Vector3 eye, out Quaternion rot)
        {
            eye = default; rot = Quaternion.identity;
            if (map == null) return false;
            int bei = -1; float bs = 0f, best = float.MaxValue;
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (e.link || e.name != road || e.length < 60f) continue;
                for (float s = 20f; s <= e.length - 30f; s += 10f)
                {
                    float d = Mathf.Abs(Vector2.Distance(e.PointAt(s), map.uptown) - 1200f);
                    if (d < best) { best = d; bei = ei; bs = s; }
                }
            }
            if (bei < 0) return false;
            var be = map.edges[bei];
            Vector2 at = be.PointAt(bs), ahead = be.PointAt(bs + 25f);
            float y0 = be.YAt(bs), y1 = be.YAt(bs + 25f);
            eye = new Vector3(at.x, y0 + 1.6f, at.y);
            rot = Quaternion.LookRotation(new Vector3(ahead.x, y1 + 1.0f, ahead.y) - eye);
            return true;
        }
    }
}
