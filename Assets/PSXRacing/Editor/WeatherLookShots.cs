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
    /// 2026-10-26 (weather3): the MOUNTAIN set first (<see cref="CaptureMountain"/>,
    /// the Blue Ridge stage on fixed days, so a before and an after render the
    /// same day under the old and the new road model); the city set is now
    /// the bench's snow (slush), fresh snow and packed snow, PHOTO only.
    /// PSX_WL_SET = mountain | city | all.
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
            string set = System.Environment.GetEnvironmentVariable("PSX_WL_SET");
            if (string.IsNullOrEmpty(set)) set = "all";
            if (set == "all" || set == "mountain") CaptureMountain(stage);
            if (set == "all" || set == "city") CaptureCity(stage);
            Flush();
            Debug.Log("[WeatherLook] done");
        }

        delegate WeatherNow Force(WeatherNow w);

        /// <summary>
        /// THE MOUNTAIN ROAD AFTER SNOW (2026-10-26, the owner from a snowy
        /// Chimney Rock - "Just because there is snow on ground, doesn't mean
        /// roads have to be icy" - and his reference clip: snow to the edge
        /// line, the carriageway black and wet in patches, flat light). The
        /// Blue Ridge stage (the sandbox's mountain scene), a chase eye behind
        /// the player's car in the right-hand lane, on DAYS the climate
        /// printout found (weather3) - so the before and after stages render
        /// the same day and hour under the old and the new road model:
        ///   aftersnow  : 3 Jan 2002 afternoon, 27 cm on the verges, the
        ///                snowfall over (old: slush 0.76; new: wet)
        ///   aftersnow2 : 5 Feb 2001 noon, 7 cm, the road drying in patches
        ///   snowlight  : 29 Jan 2002 morning, light snow falling on a brined
        ///                road (the reference's composition)
        ///   snowcover  : the bench's heavy snow, treated: the slush cover
        ///   snowfresh  : heavy snow on an untreated road: the fresh cover
        ///   drying     : 9 Apr 1999 noon, a clear sky over a drying road
        /// </summary>
        static void CaptureMountain(string stage)
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "BlueRidge");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Note("no BlueRidge scene"); return; }
            foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) cv.enabled = false;
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count < 50) { Note("no BlueRidge path"); return; }
            float atF = 0.42f;
            float.TryParse(System.Environment.GetEnvironmentVariable("PSX_WL_AT") ?? "", System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out atF);
            if (atF <= 0f || atF >= 1f) atF = 0.42f;
            int at = Mathf.Clamp(Mathf.RoundToInt(atF * path.Count), 2, path.Count - 12);
            var car = player.GetComponent<CarController>();
            Vector3 eye = default; Quaternion look = default;
            void Place(int i)
            {
                Vector3 p0 = path.GetPoint(i), p1 = path.GetPoint(i + 4);
                Vector3 fwd = p1 - p0; fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 pos = p0 + right * 1.8f + Vector3.up * 0.35f;
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                eye = pos - fwd * 6.2f + Vector3.up * 2.1f;
                look = Quaternion.LookRotation(pos + Vector3.up * 0.9f + fwd * 22f - eye);
                Note("blue ridge chase view at path " + i + "/" + path.Count);
            }
            Place(at);

            int oldDay = RaceHandoff.CalendarDay, oldOverride = RaceHandoff.WeatherOverride;
            bool oldDyn = SkyModePrefs.Dynamic;
            var sun = GameObject.Find("Sun")?.GetComponent<Light>();
            int jan3 = LifeRules.DayNumber(new System.DateTime(2002, 1, 3)), feb5 = LifeRules.DayNumber(new System.DateTime(2001, 2, 5));
            int jan29 = LifeRules.DayNumber(new System.DateTime(2002, 1, 29)), apr9 = LifeRules.DayNumber(new System.DateTime(1999, 4, 9));
            var photo = new[] { "photo" };
            try
            {
                RaceHandoff.WeatherOverride = -1;
                Seasons.VenueOverride = "BlueRidge";
                ShootAt(cam, stage, "mtn_aftersnow", "BlueRidge", jan3, TimeOfDay.Afternoon, sun, eye, look, photo, null);
                // a drying road shot where its patches change ahead of the car
                Place(PatchyIndex(path, Seasons.At(feb5, TimeOfDay.Noon, "BlueRidge").road.water, at));
                ShootAt(cam, stage, "mtn_aftersnow2", "BlueRidge", feb5, TimeOfDay.Noon, sun, eye, look, photo, null);
                Place(PatchyIndex(path, Seasons.At(apr9, TimeOfDay.Noon, "BlueRidge").road.water, at));
                ShootAt(cam, stage, "mtn_drying", "BlueRidge", apr9, TimeOfDay.Noon, sun, eye, look, photo, null);
                Place(at);
                ShootAt(cam, stage, "mtn_snowlight", "BlueRidge", jan29, TimeOfDay.Morning, sun, eye, look, new[] { "photo", "desktop" }, null);
                ShootAt(cam, stage, "mtn_snowcover", "BlueRidge", jan29, TimeOfDay.Morning, sun, eye, look, photo,
                        w => { w = Seasons.BenchWeather(Weather.Snow, w); w.precipRate = 1f; return w; });
                ShootAt(cam, stage, "mtn_snowfresh", "BlueRidge", jan29, TimeOfDay.Morning, sun, eye, look, photo,
                        w => { w = Seasons.BenchWeather(Weather.Snow, w); w.precipRate = 1f; w.treated = false;
                               w.road = new DeckState { tempC = w.road.tempC, snow = 1f }; return w; });
            }
            finally
            {
                Seasons.Forced = null;
                RaceHandoff.CalendarDay = oldDay; RaceHandoff.WeatherOverride = oldOverride;
                Seasons.VenueOverride = null; Seasons.Invalidate();
                VolumetricClouds.Override = null;
                SkyModePrefs.Dynamic = oldDyn;
                WeatherFx.PreviewClear();
            }
        }

        /// <summary>
        /// A path index where a deck at this much water is visibly PATCHY
        /// ahead of the chase eye: along the next ~25 points of the right-hand
        /// lane the tyres' own mask (RoadSurface.Patch / LocalShare - the
        /// shader draws the same) changes between wet and dry, about half of
        /// each, on a stretch that does not turn away from the camera.
        /// </summary>
        static int PatchyIndex(TrackPath path, float water, int fallback)
        {
            int best = fallback; float bestScore = -1f;
            for (int i = 40; i < path.Count - 40; i += 3)
            {
                Vector3 a = path.GetPoint(i), b = path.GetPoint(i + 4), c = path.GetPoint(i + 24), d = path.GetPoint(i + 28);
                Vector3 f0 = b - a, f1 = d - c; f0.y = 0f; f1.y = 0f;
                if (Vector3.Angle(f0, f1) > 25f) continue;
                int wet = 0, n = 0, trans = 0; bool last = false;
                for (int j = i + 2; j <= i + 26; j++)
                {
                    Vector3 p = path.GetPoint(j), q = path.GetPoint(j + 1);
                    Vector3 fw = q - p; fw.y = 0f; fw.Normalize();
                    p += Vector3.Cross(Vector3.up, fw) * 1.8f;
                    bool isWet = RoadSurface.LocalShare(water, RoadSurface.Patch(p.x, p.z)) >= 0.5f;
                    if (n > 0 && isWet != last) trans++;
                    last = isWet; if (isWet) wet++; n++;
                }
                if (trans == 0) continue;
                float score = 1f - Mathf.Abs(wet / (float)n - 0.5f) * 2f + 0.1f * Mathf.Min(trans, 3);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>One day and hour at a venue, as the climate has it (or as
        /// <paramref name="force"/> bends it), in each sky mode.</summary>
        static void ShootAt(Camera cam, string stage, string tag, string venue, int day, int hour, Light sun,
                            Vector3 eye, Quaternion look, string[] modes, Force force)
        {
            RaceHandoff.CalendarDay = day;
            Seasons.Forced = null;
            Seasons.Invalidate();
            var w = Seasons.At(day, hour, venue);
            if (force != null) { w = force(w); Note(tag + ": forced (the bench's heavy snow)"); }
            Seasons.Forced = w;
            Render(cam, stage, tag, day, hour, sun, eye, look, modes);
        }

        static void CaptureCity(string stage)
        {
            var def = System.Array.Find(TrackCatalog.All, d => d.id == "Charlotte");
            if (def == null || !PSXScreenshotTool.Open(def, out var cam, out var player)) { Note("no Charlotte scene"); return; }
            foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) cv.enabled = false;
            var map = CityMap.Get();
            if (map == null) { Note("no city map"); return; }
            PSXRacingBuilder.EnsureCityTextures();

            if (!RouteStand(map, "tryon", out Vector3 pos, out Quaternion rot)) { Note("no tryon route"); return; }
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
                // snowing, the treated road gone to slush) on a January day;
                // the fresh cover (untreated, snow falling) and the packed one
                // (the snow over, traffic on it).
                var photo = new[] { "photo" };
                Shoot(cam, stage, "snowfall", hour, sun, eye, look, photo,
                      (in WeatherNow w) => w.precip == PrecipKind.Snow && w.precipRate >= 0.6f && !w.fog && w.road.slush > 0.6f, Weather.Snow, false);
                Shoot(cam, stage, "snowroad", hour, sun, eye, look, photo,
                      (in WeatherNow w) => !w.treated && w.road.snow > 0.7f && w.precip == PrecipKind.Snow && !w.fog, Weather.Snow, true);
                int jan20 = LifeRules.DayNumber(new System.DateTime(2000, 1, 20));
                ShootAt(cam, stage, "packed", "Charlotte", jan20, hour, sun, eye, look, photo,
                        w => { w = Seasons.BenchWeather(Weather.Snow, w); w.precip = PrecipKind.None; w.precipRate = 0f;
                               w.treated = false; w.road = new DeckState { tempC = w.road.tempC, snow = 0.8f }; return w; });
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
            }
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
            Render(cam, stage, tag, day, hour, sun, eye, look, modes);
        }

        /// <summary>The forced weather, dressed and lit, shot in each sky mode,
        /// and what each frame shows written down.</summary>
        static void Render(Camera cam, string stage, string tag, int day, int hour, Light sun, Vector3 eye, Quaternion look, string[] modes)
        {
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
                     " | scenery " + now.scenery + " " + now.snowDepthCm.ToString("0") + " cm, air " + now.airC.ToString("0.0") +
                     " | globals roadSnow " + (globals != null ? globals.roadSnow.ToString("0.00") : "?") +
                     " roadSlush " + (globals != null ? globals.roadSlush.ToString("0.00") : "?") +
                     " wetWater " + (globals != null ? globals.wetWater.ToString("0.00") : "?") +
                     " sun " + (sun != null ? sun.intensity.ToString("0.00") : "?"));
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
