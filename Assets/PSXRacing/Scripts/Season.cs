using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>The four the calendar turns through. The numeric order is the
    /// order the dress variants are stored in — see <see cref="Seasons.DressCount"/>.</summary>
    public enum Season { Winter = 0, Spring = 1, Summer = 2, Fall = 3 }

    /// <summary>What the day is doing. Rolled from the calendar, never saved:
    /// the same day always gets the same sky, so the home screen can print
    /// tomorrow's before you leave for it.</summary>
    public enum Weather { Clear = 0, Fog = 1, Rain = 2, Snow = 3 }

    /// <summary>
    /// The calendar's hold on the world: which season a day is in, what the
    /// weather is doing on it, and what each of those does to the fog, the
    /// grip and the lights.
    ///
    /// The whole game was baked in October. The forest atlas is maples and
    /// oaks in colour, the near ground is gravel under a warm tint, the far
    /// slopes are an orange mottle — and the LifeSim's calendar walked
    /// through May with all of it still orange. "Moved the calendar forward
    /// to May and all the trees were still orange and the grass was brown."
    /// The look is right for one season and wrong for three, and the
    /// assets to fix it were already on the disk: the CC0 tree pack carries
    /// bare crowns, blossom, summer greens and snow-white spruce next to the
    /// autumn set the builder picked from.
    ///
    /// So the builder bakes every seasonable material FIVE ways — winter,
    /// spring, summer, fall, and winter-under-snow — and <see cref="SeasonDress"/>
    /// swaps them in when the scene loads, by the day the LifeSim says it is.
    /// This class is the arithmetic that decides which.
    ///
    /// Southern Appalachian months, not astronomical ones: colour holds
    /// through November, the ridges are bare from December, and May is full
    /// green.
    /// </summary>
    public static class Seasons
    {
        /// <summary>Winter, Spring, Summer, Fall, then the snow dress — winter
        /// with the ground white, used only while it is actually snowing.</summary>
        public const int DressCount = 5;
        public const int DressSnow = 4;
        public static readonly string[] DressNames = { "WINTER", "SPRING", "SUMMER", "FALL", "SNOW" };

        /// <summary>The season a calendar month is in, 1-12.</summary>
        public static Season OfMonth(int month)
        {
            switch (Mathf.Clamp(month, 1, 12))
            {
                case 12: case 1: case 2: return Season.Winter;
                case 3: case 4: case 5: return Season.Spring;
                case 6: case 7: case 8: return Season.Summer;
                default: return Season.Fall;
            }
        }

        /// <summary>The season an absolute LifeSim day is in. Day 0 — no
        /// LifeSim, the editor pressing Play on a circuit — is FALL, which is
        /// what every scene was baked as and what the editor has always shown.</summary>
        public static Season Of(int day) =>
            day <= 0 ? Season.Fall : OfMonth(LifeRules.MonthOf(day));

        /// <summary>
        /// THE DAY'S HEADLINE at home (Charlotte): what the date line prints.
        /// Since 2026-10-25 this is not a roll but the CLIMATE's day
        /// (<see cref="Climate"/>): snow if any fell, rain if it rained, fog,
        /// else clear. Deterministic all the same - the home screen can still
        /// say RAIN before you leave and the race agrees.
        /// </summary>
        public static Weather WeatherFor(int day) => WeatherFor(day, ClimateRegion.Piedmont, Climate.CltElevM);

        /// <summary>The day's headline at a region and elevation.</summary>
        public static Weather WeatherFor(int day, ClimateRegion r, float elevM)
        {
            if (day <= 0) return Weather.Clear;
            if (Climate.SnowFellOn(r, elevM, day)) return Weather.Snow;
            var rec = Climate.DayAt(r, elevM, day);
            if (rec.precipDay) return Weather.Rain;
            if (rec.fogDay) return Weather.Fog;
            return Weather.Clear;
        }

        /// <summary>The word the date line prints, or null on a clear day.</summary>
        public static string WeatherLabel(Weather w) => w == Weather.Clear ? null : w.ToString().ToUpperInvariant();

        /// <summary>Which of the five dresses a season and a sky call for (the
        /// legacy bucket: tools that force a Weather).</summary>
        public static int DressIndex(Season s, Weather w) => w == Weather.Snow ? DressSnow : (int)s;
        /// <summary>Which dress the SCENERY layer calls for: the snow dress
        /// while snow lies on the ground, whatever the sky and the road do.</summary>
        public static int DressFor(Season s, SceneryKind k) => k == SceneryKind.Snow ? DressSnow : (int)s;

        // ---- the world as it is right now ---------------------------------
        //
        // All off RaceHandoff.CalendarDay (which the home screen stamps every
        // time it draws itself and nothing clears), the hour the scene applied
        // (TimeOfDay.Current) and the venue (the active scene). Memoised:
        // the tyres ask every wheel every tick.

        public static int CurrentDay => RaceHandoff.CalendarDay;
        public static Season Current => Of(CurrentDay);

        /// <summary>
        /// THE WEATHER NOW, layer by layer (2026-10-25: "decouple weather
        /// (skybox, road, scenery) conditions"): the sky, what is falling, the
        /// open road and the bridge decks, the scenery, the fog and the three
        /// temperatures - at this venue, on this day, at this hour. A bench or
        /// a tool can force it (<see cref="Forced"/>, the bench's
        /// RaceHandoff.WeatherOverride, PSX_* environment overrides).
        /// </summary>
        public static WeatherNow Now
        {
            get
            {
                if (Forced.HasValue) return SkyOverPrecip(Forced.Value);
                int scene = SceneHandle();
                int d = CurrentDay, h = TimeOfDay.Current, o = RaceHandoff.WeatherOverride;
                if (!nowValid || d != nowDay || h != nowHour || o != nowOverride || scene != nowScene)
                {
                    nowDay = d; nowHour = h; nowOverride = o; nowScene = scene; nowValid = true;
                    VenueNow(out var r, out float elev);
                    now = Resolve(d, h, r, elev, o);
                }
                return now;
            }
        }
        static WeatherNow now;
        static bool nowValid;
        static int nowDay, nowHour, nowOverride, nowScene;

        /// <summary>A tool's (or a test's) whole weather, ahead of everything.
        /// Null = the calendar's.</summary>
        public static WeatherNow? Forced;

        /// <summary>Forget the memo (a tool changed something it keys on
        /// without changing the key - an environment override, the venue).</summary>
        public static void Invalidate() { nowValid = false; }

        /// <summary>The weather for a day, an hour index and a venue id (a
        /// track id or a scene name): the pre-race page's forecast.</summary>
        public static WeatherNow At(int day, int hour, string venueId)
        {
            Climate.VenueOf(venueId, out var r, out float elev);
            return Resolve(day, hour, r, elev, RaceHandoff.WeatherOverride);
        }

        static WeatherNow Resolve(int day, int hour, ClimateRegion r, float elev, int overrideW)
        {
            var w = Climate.At(r, elev, day, TimeOfDay.ClockHours(hour));
            if (overrideW >= 0 && overrideW <= (int)Weather.Snow) w = BenchWeather((Weather)overrideW, w);
            return SkyOverPrecip(EnvOverrides(w));
        }

        /// <summary>
        /// RAIN AND SNOW FALL FROM A CLOSED SKY (owner, 2026-10-26: "Rain or
        /// snow, only a few clouds in the sky."). Whatever set the layers - the
        /// climate (which already closes the sky over every hour it rains or
        /// snows, and the hours either side), the bench, a PSX_SKY override, a
        /// tool's <see cref="Forced"/> state - while anything is falling the
        /// sky is OVERCAST or STORM, never clear or broken, so the sky drawn
        /// (both modes), its light and the date line all agree. After the
        /// rain the sky may clear over a wet road: nothing falls then.
        /// </summary>
        public static WeatherNow SkyOverPrecip(WeatherNow w)
        {
            if (w.precip != PrecipKind.None && w.sky < SkyKind.Overcast) w.sky = SkyKind.Overcast;
            return w;
        }

        /// <summary>
        /// The debug bench's four skies (and PSX_WEATHER), as whole layered
        /// states - each the old weather's look, so a forced RAIN is the wet
        /// road under rain it always was: CLEAR dry under a clear sky; FOG a
        /// damp road in the fog; RAIN overcast, pouring, soaked; SNOW
        /// overcast, snowing, a treated road gone to slush, the scenery white.
        /// </summary>
        public static WeatherNow BenchWeather(Weather forced, WeatherNow w)
        {
            w.fog = forced == Weather.Fog;
            w.road = new DeckState { tempC = w.road.tempC };
            w.bridge = new DeckState { tempC = w.bridge.tempC };
            w.treated = false; w.precipRate = 0f;
            w.snowDepthCm = 0f; w.groundWet = 0f;
            switch (forced)
            {
                case Weather.Clear:
                    w.sky = SkyKind.Clear; w.precip = PrecipKind.None;
                    break;
                case Weather.Fog:
                    w.sky = SkyKind.Overcast; w.precip = PrecipKind.None;
                    w.road.water = w.bridge.water = 0.25f; w.groundWet = 0.5f;
                    break;
                case Weather.Rain:
                    w.sky = SkyKind.Overcast; w.precip = PrecipKind.Rain; w.precipRate = 1f;
                    w.road.water = w.bridge.water = 1f; w.groundWet = 1f;
                    break;
                case Weather.Snow:
                    w.sky = SkyKind.Overcast; w.precip = PrecipKind.Snow; w.precipRate = 0.8f;
                    w.treated = true;
                    w.road.slush = w.bridge.slush = 1f; w.road.water = w.bridge.water = 0.6f;
                    w.snowDepthCm = 8f;
                    w.airC = Mathf.Min(w.airC, -2f); w.hiC = Mathf.Min(w.hiC, 0f); w.loC = Mathf.Min(w.loC, -5f);
                    w.road.tempC = Mathf.Min(w.road.tempC, -1f); w.bridge.tempC = Mathf.Min(w.bridge.tempC, -3f);
                    break;
            }
            w.scenery = w.snowDepthCm >= 1f ? SceneryKind.Snow : w.groundWet > 0.35f ? SceneryKind.Wet : SceneryKind.Dry;
            return w;
        }

        /// <summary>
        /// Checks and shot tools force ONE layer at a time through the
        /// environment (the old PSX_WEATHER=rain|snow|fog forces all of them,
        /// via the bench): PSX_SKY=clear|partly|overcast|storm,
        /// PSX_PRECIP=none|rain|snow, PSX_ROAD / PSX_BRIDGE =
        /// dry|damp|wet|slush|snow|ice|frost, PSX_SCENERY=dry|wet|snow,
        /// PSX_FOG=0|1, PSX_AIRC=(Celsius).
        /// </summary>
        static WeatherNow EnvOverrides(WeatherNow w)
        {
            string v = Env("PSX_SKY");
            if (v != null && System.Enum.TryParse(v == "partly" ? "PartlyCloudy" : v, true, out SkyKind sk)) w.sky = sk;
            v = Env("PSX_PRECIP");
            if (v != null && System.Enum.TryParse(v, true, out PrecipKind pk))
            {
                w.precip = pk; w.precipRate = pk == PrecipKind.None ? 0f : 1f;
                if (pk != PrecipKind.None && w.sky < SkyKind.Overcast) w.sky = SkyKind.Overcast;
            }
            v = Env("PSX_ROAD"); if (v != null) w.road = DeckOf(v, w.road.tempC, out w.treated);
            v = Env("PSX_BRIDGE"); if (v != null) w.bridge = DeckOf(v, w.bridge.tempC, out _);
            v = Env("PSX_SCENERY");
            if (v != null && System.Enum.TryParse(v, true, out SceneryKind sc))
            {
                w.scenery = sc;
                w.snowDepthCm = sc == SceneryKind.Snow ? Mathf.Max(w.snowDepthCm, 8f) : 0f;
                w.groundWet = sc == SceneryKind.Wet ? 1f : sc == SceneryKind.Dry ? 0f : w.groundWet;
            }
            v = Env("PSX_FOG"); if (v != null) w.fog = v == "1";
            v = Env("PSX_AIRC");
            if (v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out float c)) w.airC = c;
            return w;
        }

        static string Env(string name)
        {
            string v = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v.Trim().ToLowerInvariant();
        }

        static DeckState DeckOf(string kind, float tempC, out bool treated)
        {
            var d = new DeckState { tempC = tempC };
            treated = false;
            switch (kind)
            {
                case "damp": d.water = 0.45f; break;
                case "wet": d.water = 1f; break;
                case "slush": d.slush = 1f; d.water = 0.6f; treated = true; break;
                case "snow": d.snow = 1f; break;
                case "ice": d.water = 1f; d.ice = true; d.tempC = Mathf.Min(tempC, -2f); break;
                case "frost": d.frost = true; d.tempC = Mathf.Min(tempC, -2f); break;
            }
            return d;
        }

        // ---- the venue ----------------------------------------------------

        static int SceneHandle() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetHashCode();

        /// <summary>A tool's venue (a track id), ahead of the active scene's.</summary>
        public static string VenueOverride;

        /// <summary>The climate region and the elevation the active scene
        /// drives at: its own name (a stage scene is named for its loop; a
        /// sprint races in its loop's scene), else the booked track's id.</summary>
        public static void VenueNow(out ClimateRegion r, out float elevM)
        {
            string id = VenueOverride;
            if (string.IsNullOrEmpty(id)) id = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Climate.VenueOf(id, out r, out elevM);
            if (r == ClimateRegion.Piedmont && string.IsNullOrEmpty(VenueOverride) && !IsKnownScene(id))
            {
                // A scene this table does not know (a tool's scratch scene):
                // the booked track's.
                var all = TrackCatalog.All;
                int ti = RaceHandoff.TrackIndex;
                if (ti >= 0 && ti < all.Length) Climate.VenueOf(all[ti].id, out r, out elevM);
            }
        }

        static bool IsKnownScene(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var t in TrackCatalog.All) if (t.id == name) return true;
            return name == "LifeHome" || name == "Garage" || name.StartsWith("Town", System.StringComparison.Ordinal) ||
                   name.StartsWith("Charlotte", System.StringComparison.Ordinal);
        }

        // ---- the layers, as the rest of the game reads them ----------------

        /// <summary>
        /// The legacy one-word weather - since 2026-10-25 the SKY'S LIGHT, not
        /// a roll: Fog in fog, Snow while snow falls, Rain under any overcast
        /// sky (raining or not: the grey, shadowless light and the overcast
        /// panorama), Clear under a clear or broken sky. Every lighting table
        /// (TimeOfDay SkyMul/SunMul/ShadowFor...) keys on it. What is FALLING
        /// is <see cref="CurrentPrecipWeather"/>; the road is RoadSurface's.
        /// </summary>
        public static Weather CurrentWeather => SkyLightOf(Now);

        public static Weather SkyLightOf(in WeatherNow w)
        {
            if (w.fog) return Weather.Fog;
            if (w.precip == PrecipKind.Snow) return Weather.Snow;
            if (w.precip == PrecipKind.Rain || w.sky >= SkyKind.Overcast) return Weather.Rain;
            return Weather.Clear;
        }

        /// <summary>What is in the AIR, as the old enum: the rain or snow
        /// falling (WeatherFx's particles, the lamps' haze), Fog in fog, else
        /// Clear - an overcast sky with nothing falling is Clear here.</summary>
        public static Weather CurrentPrecipWeather
        {
            get
            {
                var w = Now;
                if (w.precip == PrecipKind.Snow) return Weather.Snow;
                if (w.precip == PrecipKind.Rain) return Weather.Rain;
                return w.fog ? Weather.Fog : Weather.Clear;
            }
        }

        /// <summary>Today's dress: the season's, or the snow dress while snow
        /// lies on the scenery (with dry roads and a blue sky, if that is what
        /// the day did).</summary>
        public static int CurrentDress => DressFor(Current, Now.scenery);

        // ---- grip -----------------------------------------------------------

        /// <summary>
        /// WET TARMAC (2026-10-06) and since 2026-10-25 every surface the
        /// layers make: dry 1.0, a soaked road WetRoadGrip, slush, snow, frost
        /// and black ice below that (Climate.*Grip). The tyres read their OWN
        /// spot (RoadSurface.At, per wheel: patches, bridges); these are the
        /// legacy one-number views - the open road at a typical patch - for
        /// what has no spot to stand on (the bench line, the traffic, the dirt
        /// the car picks up, a car's wet film).
        /// </summary>
        public const float WetRoadGrip = Climate.WetGrip, WetOffroadGrip = Climate.WetOffroadGrip;
        public const float SnowRoadGrip = Climate.SlushGrip, SnowOffroadGrip = Climate.SnowOffroadGrip;

        /// <summary>The legacy bucket's wetness (the bench's descriptions).</summary>
        public static float GripWetness(Weather w)
        {
            switch (w)
            {
                case Weather.Rain: return 1f;
                case Weather.Fog:  return 0.25f;
                case Weather.Snow: return 1f;
                default: return 0f;
            }
        }

        /// <summary>The legacy bucket's grip, as the bench forces it.</summary>
        public static float GripMult(Weather w, bool road)
        {
            var forced = BenchWeather(w, new WeatherNow());
            return road ? RoadSurface.TypicalRoadGrip(forced) : RoadSurface.OffRoad(forced).grip;
        }
        public static float RoadGripMult => RoadSurface.TypicalRoadGrip(Now);
        public static float OffroadGripMult => RoadSurface.OffRoad(Now).grip;

        /// <summary>0 dry .. 1 soaked (or worse): how far a grip has fallen
        /// toward a wet road's.</summary>
        public static float WetT(float roadGripMult) =>
            Mathf.Clamp01((1f - roadGripMult) / (1f - WetRoadGrip));

        /// <summary>
        /// The open road's wetness now, 0 dry .. 1 soaked (or snow). A sliding
        /// tyre on a wet road hisses rather than screams, lays little rubber
        /// and barely smokes (the owner, 2026-10-07). The tyres themselves read
        /// their own spot (CarController.WheelContact.wetT); this is for
        /// what has none.
        /// </summary>
        public static float RoadWetT => WetT(RoadGripMult);
        public const float WetScreechKeep = 0.35f, WetSmokeKeep = 0.30f, WetMarkKeep = 0.25f;

        /// <summary>The rain film on a car's paint (_PSXCarWet, PaintGloss):
        /// rain or snow falling wets it, and after the rain the spray off a
        /// wet road keeps it wet a while.</summary>
        public static float CarWetNow
        {
            get
            {
                var w = Now;
                float fall = w.precip != PrecipKind.None ? Mathf.Max(0.6f, w.precipRate) : 0f;
                return Mathf.Clamp01(Mathf.Max(fall, 0.7f * Mathf.Max(w.road.water, w.road.slush)));
            }
        }

        // ---- words and temperatures ----------------------------------------

        /// <summary>A temperature the way the player's units read it: F with
        /// mph, C with km/h. No degree sign (the menu font).</summary>
        public static string Temp(float c) =>
            SpeedUnits.Mph ? Mathf.RoundToInt(c * 9f / 5f + 32f) + "F" : Mathf.RoundToInt(c) + "C";

        /// <summary>The sky in a word.</summary>
        public static string SkyWord(SkyKind k) =>
            k == SkyKind.Clear ? "CLEAR" : k == SkyKind.PartlyCloudy ? "PARTLY CLOUDY" : k == SkyKind.Overcast ? "OVERCAST" : "STORMS";

        /// <summary>A deck in a word: at a typical patch, or (worst) where it
        /// lies thickest.</summary>
        public static string RoadWord(in DeckState d, bool worst = false)
        {
            var s = RoadSurface.OfDeck(d, worst ? 1f : 0.5f);
            switch (s.kind)
            {
                case SurfaceKind.Damp: return "DAMP";
                case SurfaceKind.Wet: return "WET";
                case SurfaceKind.Slush: return "SLUSH";
                case SurfaceKind.Snow: return "SNOW";
                case SurfaceKind.Frost: return "FROST";
                case SurfaceKind.Ice: return "ICE";
                default: return d.water > 0.02f || d.slush > 0.02f || d.snow > 0.02f ? "PATCHY" : "DRY";
            }
        }

        /// <summary>
        /// The player's weather in TWO short lines (the menus' no-clip rule:
        /// each fits a 760-unit column at the smallest label size). The sky,
        /// what falls and the ground - "OVERCAST, RAIN", "CLEAR, SNOW ON
        /// GROUND" ...
        /// </summary>
        public static string SkyLine(in WeatherNow w)
        {
            string s = w.fog ? "FOG" : SkyWord(w.sky);
            if (w.precip == PrecipKind.Rain) return s + (w.sky == SkyKind.Storm ? ", T-STORMS" : ", RAIN");
            if (w.precip == PrecipKind.Snow) return s + ", SNOW";
            if (w.scenery == SceneryKind.Snow) return s + ", SNOW ON GROUND";
            return s;
        }

        /// <summary>...and the three temperatures with the decks they are
        /// on: "AIR 41F  ·  ROAD 38F WET  ·  BRIDGES 33F ICE" (the bridges'
        /// word is where it lies worst).</summary>
        public static string TempLine(in WeatherNow w, bool hiLo = false)
        {
            string air = "AIR " + Temp(w.airC);
            if (hiLo && !w.fixedDay) air += " (" + Temp(w.hiC) + "/" + Temp(w.loC) + ")";
            return air + "  ·  ROAD " + Temp(w.road.tempC) + " " + RoadWord(w.road) +
                   "  ·  BRIDGES " + Temp(w.bridge.tempC) + " " + RoadWord(w.bridge, true);
        }

        /// <summary>Multiplier on the hour's fog band: how much CLOSER the
        /// world fades in than it would on a clear day.</summary>
        public static float FogMul(Weather w)
        {
            switch (w)
            {
                case Weather.Fog:  return 0.45f;
                case Weather.Rain: return 0.70f;
                case Weather.Snow: return 0.60f;
                default: return 1f;
            }
        }
        /// <summary>Multiplier on the hour's ambient light.</summary>
        public static float AmbientMul(Weather w) =>
            w == Weather.Rain ? 0.85f : w == Weather.Fog ? 0.95f : 1f;
        /// <summary>Multiplier on the sky panorama's exposure. Overcast is a
        /// darker sky, and a snow sky is bright and flat.</summary>
        public static float SkyMul(Weather w)
        {
            switch (w)
            {
                case Weather.Fog:  return 0.85f;
                case Weather.Rain: return 0.60f;
                case Weather.Snow: return 0.90f;
                default: return 1f;
            }
        }
        /// <summary>Headlights on in anything but clear air.</summary>
        public static bool LightsOn(Weather w) => w != Weather.Clear;
    }
}
