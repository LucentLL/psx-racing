using System;
using System.Collections.Generic;

namespace PSXRacing
{
    /// <summary>Where a venue's weather comes from: the three climates of the
    /// game's map. Each is one NOAA station's normals (see <see cref="Climate"/>).</summary>
    public enum ClimateRegion { Piedmont = 0, Mountains = 1, Coast = 2 }

    /// <summary>The SKY layer: how much cloud. Rain and snow always come from
    /// an overcast (or storm) sky; a clear sky can sit over a soaked road.</summary>
    public enum SkyKind { Clear = 0, PartlyCloudy = 1, Overcast = 2, Storm = 3 }

    /// <summary>The PRECIPITATION layer: what is falling right now.</summary>
    public enum PrecipKind { None = 0, Rain = 1, Snow = 2 }

    /// <summary>The SCENERY layer: the ground off the road.</summary>
    public enum SceneryKind { Dry = 0, Wet = 1, Snow = 2 }

    /// <summary>The ROAD layer, as one word: what a tyre at one spot stands on.</summary>
    public enum SurfaceKind { Dry = 0, Damp = 1, Wet = 2, Slush = 3, Snow = 4, Frost = 5, Ice = 6 }

    /// <summary>One deck's state - the open road, or a bridge deck, which has
    /// no ground under it and runs colder at night.</summary>
    public struct DeckState
    {
        /// <summary>0..1 film of water: rain, meltwater, treated slush melting.</summary>
        public float water;
        /// <summary>0..1 treated snow churned to slush (brine on the road).</summary>
        public float slush;
        /// <summary>0..1 snow lying on an untreated deck.</summary>
        public float snow;
        /// <summary>Its water is frozen (black ice).</summary>
        public bool ice;
        /// <summary>Frost on a dry deck: a clear, still, humid night below freezing.</summary>
        public bool frost;
        /// <summary>The surface's own temperature, Celsius.</summary>
        public float tempC;
    }

    /// <summary>
    /// The weather at one place and one time, as independent layers: the sky,
    /// what is falling, the road (open road and bridge decks apart), the
    /// scenery, the fog, and the three temperatures the player sees.
    /// </summary>
    public struct WeatherNow
    {
        public int day;
        public float clock;
        public ClimateRegion region;
        public float elevM;
        /// <summary>Today's high and low, and the air right now (Celsius).</summary>
        public float hiC, loC, airC;
        public SkyKind sky;
        public PrecipKind precip;
        /// <summary>0..1 how hard it is falling (1 = a downpour / heavy snow).</summary>
        public float precipRate;
        public bool fog;
        public DeckState road, bridge;
        /// <summary>The roads are treated (brined/salted) for a snow event: snow
        /// on them is slush, and their water freezes far lower.</summary>
        public bool treated;
        /// <summary>Snow lying on the scenery, centimetres.</summary>
        public float snowDepthCm;
        /// <summary>0..1 how wet the ground off the road is.</summary>
        public float groundWet;
        public SceneryKind scenery;
        /// <summary>True for the editor's day 0 and anything else with no calendar.</summary>
        public bool fixedDay;
    }

    /// <summary>
    /// THE CLIMATE (2026-10-25, the owner: "I want ambient temp, road temp
    /// (bridge temp). These temps should be average charlotte (and NC
    /// mountains and beach) temperatures for each day of the year ... Variance
    /// is random each day from these ranges of values ... snow is rare in
    /// Charlotte. Maybe twice a year on average, lasting a few days. Roads can
    /// be treated and safe to drive on. Snow is more common in the mountains
    /// and extremely rare at the beach.").
    ///
    /// A deterministic daily weather model per region, simulated an HOUR at a
    /// time across a year so what lies on the ground (snow, water, ice) has a
    /// history: the same day, hour and venue always give the same answer and
    /// nothing is saved. Pure C# (no UnityEngine) so tools can run it outside
    /// the editor (the climate printout).
    ///
    /// SOURCES - monthly normals, interpolated to the day between mid-months:
    ///   Piedmont  = Charlotte Douglas Intl (CLT), NOAA NCEI U.S. Climate
    ///               Normals 1991-2020 (as tabled on Wikipedia "Charlotte,
    ///               North Carolina", climate box): 222 m.
    ///   Mountains = Boone 1 SE, NOAA 1991-2020 normals (Wikipedia "Boone,
    ///               North Carolina"): 1,016 m. Each venue is moved to its own
    ///               elevation at <see cref="LapseCPerKm"/>.
    ///   Coast     = Morehead City, NOAA 1981-2010 normals (Wikipedia
    ///               "Morehead City, North Carolina" - the page has no
    ///               1991-2020 table), for Bogue Banks (Atlantic Beach,
    ///               Emerald Isle).
    /// The day-to-day spread (standard deviation of the daily anomaly) is the
    /// typical Southeast figure: ~5 C in winter, ~2 C in summer; persistence
    /// comes from smooth noise over 3- and 12-day weather systems, the same
    /// systems over all three regions (a cold snap is statewide).
    /// </summary>
    public static class Climate
    {
        /// <summary>LifeRules.Epoch: day 1 is 1 January 1999 (LifeSimSelfTest
        /// pins the two together).</summary>
        public static readonly DateTime Epoch = new DateTime(1999, 1, 1);
        public static DateTime DateOf(int day) => Epoch.AddDays(Math.Max(1, day) - 1);

        static float FtoC(float f) => (f - 32f) * 5f / 9f;

        // ---- the stations (Jan .. Dec, as published, Fahrenheit) ------------

        // Charlotte Douglas Intl, NOAA 1991-2020.
        static readonly float[] CltHiF = { 52.3f, 56.6f, 64.2f, 73.2f, 80.1f, 86.9f, 90.3f, 88.6f, 82.8f, 73.3f, 62.9f, 54.9f };
        static readonly float[] CltLoF = { 31.8f, 34.9f, 41.2f, 49.1f, 58.0f, 66.2f, 69.9f, 68.7f, 62.6f, 50.4f, 39.8f, 34.5f };
        static readonly float[] CltPrcpDays = { 10.3f, 9.7f, 10.2f, 9.0f, 9.5f, 10.6f, 10.5f, 10.1f, 7.7f, 7.1f, 8.1f, 9.6f };
        /// <summary>Days with 0.1 in of snow or more, CLT: 1.9 a year.</summary>
        public static readonly float[] CltSnowDays = { 0.9f, 0.5f, 0.2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.3f };

        // Boone 1 SE, NOAA 1991-2020.
        static readonly float[] BooneHiF = { 41.4f, 44.7f, 51.7f, 61.7f, 69.4f, 75.8f, 79.0f, 77.8f, 72.2f, 63.0f, 53.0f, 45.0f };
        static readonly float[] BooneLoF = { 21.6f, 23.8f, 29.2f, 36.9f, 45.8f, 53.7f, 57.7f, 56.3f, 50.3f, 39.3f, 29.6f, 24.7f };
        static readonly float[] BoonePrcpDays = { 11.7f, 11.5f, 12.3f, 12.9f, 14.0f, 14.3f, 14.1f, 13.3f, 10.9f, 9.8f, 9.2f, 11.9f };
        /// <summary>Boone 1 SE: 14.2 snow days a year.</summary>
        public static readonly float[] BooneSnowDays = { 3.7f, 3.8f, 2.5f, 0.7f, 0f, 0f, 0f, 0f, 0f, 0.2f, 0.7f, 2.6f };

        // Morehead City, NOAA 1981-2010.
        static readonly float[] MhcHiF = { 56.3f, 58.6f, 64.1f, 71.5f, 77.8f, 84.3f, 87.5f, 86.9f, 83.3f, 76.1f, 67.7f, 59.5f };
        static readonly float[] MhcLoF = { 36.2f, 38.1f, 43.6f, 51.7f, 60.8f, 69.6f, 73.4f, 72.3f, 67.1f, 56.7f, 47.5f, 39.3f };
        static readonly float[] MhcPrcpDays = { 9f, 8f, 8f, 7f, 8f, 9f, 11f, 11f, 9f, 7f, 8f, 9f };
        /// <summary>Morehead City: 0.8 in of snow a year, no month with a
        /// snow day on the books.</summary>
        public static readonly float[] MhcSnowDays = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };

        /// <summary>SD of the daily temperature anomaly, Celsius, Piedmont.</summary>
        static readonly float[] AnomSdC = { 5.0f, 5.0f, 4.6f, 4.0f, 3.0f, 2.2f, 1.8f, 1.9f, 2.6f, 3.6f, 4.3f, 4.9f };

        /// <summary>Fog days per mille, mountains (the old game table, which
        /// was a mountain year). The Piedmont has a third of them, the coast
        /// its own (sea fog in the cool months).</summary>
        static readonly int[] FogMountain = { 120, 120, 130, 110, 90, 60, 50, 60, 120, 160, 150, 120 };
        static readonly int[] FogCoast    = { 80, 80, 90, 70, 50, 30, 20, 20, 40, 60, 70, 80 };

        /// <summary>Share of precipitation days that are afternoon storms
        /// (short, heavy, convective) rather than a stratiform day of rain.</summary>
        static readonly float[] ConvectiveShare = { 0.05f, 0.05f, 0.1f, 0.3f, 0.55f, 0.7f, 0.75f, 0.7f, 0.5f, 0.25f, 0.1f, 0.05f };

        public const float LapseCPerKm = 6.0f;
        public const float BooneElevM = 1016f, CltElevM = 222f, CoastElevM = 5f;

        /// <summary>
        /// Air at or below this falls as snow. TUNED, with the three below, on
        /// the climate printout (30 simulated years) against the stations'
        /// snow: CLT 2.6 snow days a year at 3.9 in (normal 1.9 days, 3.5 in),
        /// Boone 13.9 at 26.6 in (14.2, 25.6), Morehead City 0.3 at 0.7 in
        /// (0.8 in, no month with a snow day) - while every month's mean
        /// high and low stays within ~1 C of the normals.
        /// </summary>
        public const float SnowAirC = -2.0f;
        /// <summary>Winter rain comes on a warm wind: a stratiform day's air
        /// runs this much above the day's anomaly (cloud, warm advection);
        /// the dry days give it back so the month's mean holds.</summary>
        public const float PrecipWarmC = 4.0f;
        /// <summary>The sea keeps the coast's rain warmer again, and its snow
        /// needs a colder airmass.</summary>
        public const float CoastPrecipExtraC = 2.0f, CoastSnowExtraC = -1.5f;

        /// <summary>Chance a snow event's roads get treated (brine ahead,
        /// salt/plough after): main roads in a city that owns the trucks.</summary>
        static float TreatChance(ClimateRegion r) => r == ClimateRegion.Coast ? 0.6f : r == ClimateRegion.Mountains ? 0.9f : 0.85f;

        // ---- venues ---------------------------------------------------------

        /// <summary>
        /// The region and the elevation (m) a venue drives at, by track id or
        /// scene name (a sprint shares its loop's). Anything not listed is the
        /// Piedmont: Charlotte, the town, home and the fictional circuits.
        /// Mountain elevations are the route's middle, from the stage blurbs.
        /// </summary>
        public static void VenueOf(string id, out ClimateRegion region, out float elevM)
        {
            region = ClimateRegion.Piedmont; elevM = CltElevM;
            if (string.IsNullOrEmpty(id)) return;
            foreach (var v in Venues)
                if (id.StartsWith(v.id, StringComparison.Ordinal)) { region = v.region; elevM = v.elevM; return; }
        }

        struct Venue { public string id; public ClimateRegion region; public float elevM; }
        static readonly Venue[] Venues =
        {
            new Venue { id = "MtMitchell",        region = ClimateRegion.Mountains, elevM = 1800f },
            new Venue { id = "BlueRidge",         region = ClimateRegion.Mountains, elevM = 1300f },
            new Venue { id = "BeechGap",          region = ClimateRegion.Mountains, elevM = 1300f },
            new Venue { id = "BlowingRock",       region = ClimateRegion.Mountains, elevM = 1150f },
            new Venue { id = "LittleSwitzerland", region = ClimateRegion.Mountains, elevM = 1000f },
            new Venue { id = "SwissNC226A",       region = ClimateRegion.Mountains, elevM = 820f },
            new Venue { id = "GillespieGap",      region = ClimateRegion.Mountains, elevM = 750f },
            new Venue { id = "ChimneyRock",       region = ClimateRegion.Mountains, elevM = 500f },
            new Venue { id = "EmeraldIsle",       region = ClimateRegion.Coast, elevM = CoastElevM },
            new Venue { id = "LangstonBridge",    region = ClimateRegion.Coast, elevM = CoastElevM },
            new Venue { id = "AtlanticBeachBridge", region = ClimateRegion.Coast, elevM = CoastElevM },
        };

        public static string RegionName(ClimateRegion r) =>
            r == ClimateRegion.Mountains ? "MOUNTAINS" : r == ClimateRegion.Coast ? "COAST" : "PIEDMONT";

        // ---- normals --------------------------------------------------------

        /// <summary>The day's normal high and low (Celsius) at a region and
        /// elevation: monthly normals laid at mid-month and joined in straight
        /// lines, so the year is a smooth curve and not twelve steps.</summary>
        public static void Normals(ClimateRegion r, float elevM, int day, out float hiC, out float loC)
        {
            float[] hi, lo; float baseElev;
            Station(r, out hi, out lo, out _, out baseElev);
            float m = MonthPos(day);
            hiC = FtoC(Interp(hi, m)) - LapseCPerKm * (elevM - baseElev) / 1000f;
            loC = FtoC(Interp(lo, m)) - LapseCPerKm * (elevM - baseElev) / 1000f;
        }

        static void Station(ClimateRegion r, out float[] hi, out float[] lo, out float[] prcp, out float elev)
        {
            switch (r)
            {
                case ClimateRegion.Mountains: hi = BooneHiF; lo = BooneLoF; prcp = BoonePrcpDays; elev = BooneElevM; break;
                case ClimateRegion.Coast: hi = MhcHiF; lo = MhcLoF; prcp = MhcPrcpDays; elev = CoastElevM; break;
                default: hi = CltHiF; lo = CltLoF; prcp = CltPrcpDays; elev = CltElevM; break;
            }
        }

        /// <summary>Fractional month: 0.5 = mid-January, 11.5 = mid-December.</summary>
        static float MonthPos(int day)
        {
            var d = DateOf(day);
            int dim = DateTime.DaysInMonth(d.Year, d.Month);
            return (d.Month - 1) + (d.Day - 0.5f) / dim;
        }

        /// <summary>A monthly table read at a fractional month, wrapping.</summary>
        static float Interp(float[] t, float m)
        {
            float x = m - 0.5f;
            int i = (int)Math.Floor(x);
            float f = x - i;
            int a = ((i % 12) + 12) % 12, b = (a + 1) % 12;
            return t[a] + (t[b] - t[a]) * f;
        }

        static int MonthIndex(int day) => DateOf(day).Month - 1;

        // ---- noise ----------------------------------------------------------
        //
        // Integer hashes, not System.Random: the same on every platform.

        static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
        static double U01(uint seed, int i) => (Hash(seed, unchecked((uint)i)) >> 8) * (1.0 / 16777216.0);
        static double Gauss(uint seed, int i)
        {
            double u1 = Math.Max(1e-7, U01(seed, i)), u2 = U01(seed ^ 0xA511E9B3u, i);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        /// <summary>Unit-variance Gaussian noise that drifts over L days.</summary>
        static double Smooth(uint seed, int day, int L)
        {
            int k = FloorDiv(day, L);
            double t = (day - k * L) / (double)L;
            double a = Gauss(seed, k), b = Gauss(seed, k + 1);
            return (a * (1 - t) + b * t) / Math.Sqrt((1 - t) * (1 - t) + t * t);
        }
        /// <summary>Standard normal CDF (Abramowitz-Stegun 7.1.26 on erf).</summary>
        static double Phi(double z)
        {
            double x = Math.Abs(z) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return z >= 0 ? 0.5 * (1 + y) : 0.5 * (1 - y);
        }

        const uint ST3 = 0x1001u, ST12 = 0x1002u, STW = 0x1003u, SP2 = 0x2001u, SPW = 0x2002u,
                   SPT = 0x2003u, SPD = 0x2004u, SPA = 0x2005u, SSKY = 0x3001u, SFOG = 0x4001u, STREAT = 0x5001u,
                   SFROST = 0x6001u, SRIDGE = 0x7001u;
        static uint RSeed(ClimateRegion r) => 0x9100u + (uint)r * 0x77u;

        // ---- the day --------------------------------------------------------

        /// <summary>One day's synoptic weather: its temperatures and its one
        /// spell of precipitation (if any), before anything lies on the ground.</summary>
        public struct DayRec
        {
            public float hiC, loC, anomC;
            public bool precipDay, convective;
            /// <summary>Clock hours the precipitation starts at and lasts
            /// (may run past midnight into the next day).</summary>
            public float pStart, pDur;
            /// <summary>Total precipitation, mm of water (10:1 as snow: 1 mm = 1 cm).</summary>
            public float amountMm;
            /// <summary>The sky on its dry hours, and after the rain has gone.</summary>
            public SkyKind drySky, afterSky;
            public bool fogDay;
        }

        public static DayRec Day(ClimateRegion r, float elevM, int day)
        {
            var rec = new DayRec();
            int m = MonthIndex(day);
            uint rs = RSeed(r);
            Normals(r, elevM, day, out float nHi, out float nLo);
            float sd = AnomSdC[m] * (r == ClimateRegion.Mountains ? 1.05f : r == ClimateRegion.Coast ? 0.8f : 1f);
            double z = (0.62 * Smooth(ST3, day, 3) + 0.55 * Smooth(ST12, day, 12) + 0.40 * Gauss(STW ^ rs, day)) / 0.92;
            Station(r, out _, out _, out var prcp, out _);
            int dim = DateTime.DaysInMonth(DateOf(day).Year, m + 1);
            double pi = Math.Min(0.9, prcp[m] / dim);
            rec.anomC = (float)(z * sd);
            double zp = (0.80 * Smooth(SP2, day, 2) + 0.60 * Gauss(SPW ^ rs, day));
            double wetScore = Phi(zp);
            rec.precipDay = wetScore > 1.0 - pi;
            float range = nHi - nLo;
            double us = U01(SSKY ^ rs, day);
            if (rec.precipDay)
            {
                rec.convective = U01(SPT ^ rs, day) < ConvectiveShare[m] * (r == ClimateRegion.Coast ? 0.85f : 1f);
                double ud = U01(SPD ^ rs, day), ua = U01(SPA ^ rs, day);
                if (rec.convective)
                {
                    rec.pStart = 13f + (float)(6.0 * U01(SPT ^ rs ^ 0x55u, day));
                    rec.pDur = 1f + (float)(2.5 * ud);
                    rec.amountMm = 3f + (float)(14.0 * ua);
                }
                else
                {
                    rec.pStart = (float)(24.0 * U01(SPT ^ rs ^ 0xAAu, day));
                    rec.pDur = 3f + (float)(11.0 * ud);
                    rec.amountMm = 2f + (float)(16.0 * ua);
                }
                // Cloud holds the day's range in: cooler afternoon, milder night.
                // The sea keeps the coast's rain a little warmer again.
                float warm = rec.convective ? 0f : PrecipWarmC + (r == ClimateRegion.Coast ? CoastPrecipExtraC : 0f);
                rec.hiC = nHi + rec.anomC + warm - (rec.convective ? 0.10f : 0.30f) * range;
                rec.loC = nLo + rec.anomC + warm + (rec.convective ? 0.0f : 0.12f) * range;
                rec.drySky = rec.convective ? SkyKind.PartlyCloudy : SkyKind.Overcast;
                rec.afterSky = us < 0.55 ? SkyKind.Clear : SkyKind.PartlyCloudy;
            }
            else
            {
                // A dry day's cloud: days near the wet threshold are greyer.
                double c = 0.6 * wetScore + 0.4 * us;
                rec.drySky = c > 0.80 ? SkyKind.Overcast : c > 0.50 ? SkyKind.PartlyCloudy : SkyKind.Clear;
                // The dry days give back the rain days' warmth (PrecipWarmC),
                // so the month's mean stays the normal's.
                rec.anomC -= (PrecipWarmC + (r == ClimateRegion.Coast ? CoastPrecipExtraC : 0f)) *
                             (float)(pi * (1.0 - ConvectiveShare[m]) / (1.0 - pi));
                rec.afterSky = rec.drySky;
                float k = rec.drySky == SkyKind.Overcast ? -0.15f : rec.drySky == SkyKind.Clear ? 0.05f : 0f;
                rec.hiC = nHi + rec.anomC + k * range;
                rec.loC = nLo + rec.anomC - (rec.drySky == SkyKind.Clear ? 0.08f : rec.drySky == SkyKind.Overcast ? -0.10f : 0f) * range;
            }
            int fogPm = r == ClimateRegion.Mountains ? FogMountain[m] : r == ClimateRegion.Coast ? FogCoast[m] : FogMountain[m] / 3;
            rec.fogDay = !rec.convective && U01(SFOG ^ rs, day) * 1000.0 < fogPm;
            return rec;
        }

        // ---- the hour -------------------------------------------------------

        /// <summary>The air at a clock hour (0..24) of a day: the low at 06:00,
        /// the high at 15:00, cosine curves between, the night falling toward
        /// TOMORROW's low and the small hours coming down from YESTERDAY's high.</summary>
        static float AirAt(DayRec prev, DayRec today, DayRec next, float h)
        {
            if (h >= 6f && h <= 15f)
                return today.loC + (today.hiC - today.loC) * (float)(1 - Math.Cos(Math.PI * (h - 6f) / 9f)) * 0.5f;
            if (h > 15f)
                return next.loC + (today.hiC - next.loC) * (float)(1 + Math.Cos(Math.PI * (h - 15f) / 15f)) * 0.5f;
            return today.loC + (prev.hiC - today.loC) * (float)(1 + Math.Cos(Math.PI * (h + 24f - 15f) / 15f)) * 0.5f;
        }

        /// <summary>Day length and the sun's noon height, 35.5 N.</summary>
        static void Sun(int day, float h, out float s, out float sinNoon)
        {
            int doy = DateOf(day).DayOfYear;
            double decl = 23.44 * Math.Sin(2 * Math.PI * (doy - 80) / 365.0);
            sinNoon = (float)Math.Sin((90.0 - 35.5 + decl) * Math.PI / 180.0);
            double len = 12.0 + 2.3 * Math.Sin(2 * Math.PI * (doy - 80) / 365.0);
            double rise = 13.0 - len / 2.0;
            s = (float)Math.Max(0.0, Math.Sin(Math.PI * (h - rise) / len));
            if (h < rise || h > rise + len) s = 0f;
        }

        static float CloudOf(SkyKind k) => k == SkyKind.Clear ? 0.1f : k == SkyKind.PartlyCloudy ? 0.45f : k == SkyKind.Overcast ? 0.92f : 1f;

        // ---- the year, an hour at a time ------------------------------------

        struct Hour
        {
            public float air, road, bridge, rate, depth, groundWet;
            public byte precip, sky;
            public bool fog, treated;
            public DeckState rd, br;
        }

        sealed class YearTable
        {
            public int firstDay, days;
            public DayRec[] rec;      // firstDay-1 .. firstDay+days
            public Hour[] hours;      // days * 24
        }

        static readonly Dictionary<long, YearTable> tables = new Dictionary<long, YearTable>();

        static YearTable TableFor(ClimateRegion r, float elevM, int year)
        {
            long key = ((long)year << 24) ^ ((long)r << 20) ^ (long)Math.Round(elevM);
            if (tables.TryGetValue(key, out var t)) return t;
            if (tables.Count > 12) tables.Clear();
            t = Build(r, elevM, year);
            tables[key] = t;
            return t;
        }

        /// <summary>Simulates 1 September of the year before through 31
        /// December, starting dry and bare (nothing lies on the ground on 1
        /// September in any of the three climates).</summary>
        static YearTable Build(ClimateRegion r, float elevM, int year)
        {
            int first = DayNumber(new DateTime(year - 1, 9, 1));
            int last = DayNumber(new DateTime(year, 12, 31));
            var t = new YearTable { firstDay = first, days = last - first + 1 };
            t.rec = new DayRec[t.days + 2];
            for (int i = 0; i < t.rec.Length; i++) t.rec[i] = Day(r, elevM, first - 1 + i);
            t.hours = new Hour[t.days * 24];
            uint rs = RSeed(r);
            float snowAt = SnowAirC + (r == ClimateRegion.Coast ? CoastSnowExtraC : 0f);

            // Snow events and their treatment, found first so the brine can go
            // down ahead of the storm.
            var treatFrom = new List<int>(); var treatTo = new List<int>();
            for (int i = 0; i < t.days; i++)
            {
                var d = t.rec[i + 1];
                if (!d.precipDay) continue;
                bool snows = false;
                for (float h = d.pStart; h < d.pStart + d.pDur; h += 1f)
                {
                    int di = i + (int)(h / 24f);
                    if (di + 2 >= t.rec.Length) break;
                    if (AirAt(t.rec[di], t.rec[di + 1], t.rec[di + 2], h % 24f) <= snowAt + 1f) { snows = true; break; }
                }
                if (!snows) continue;
                int startH = i * 24 + (int)d.pStart, endH = i * 24 + (int)(d.pStart + d.pDur);
                // One event, one decision: a run of snow days shares the first's.
                if (treatTo.Count > 0 && startH - treatTo[treatTo.Count - 1] < 36) { treatTo[treatTo.Count - 1] = endH + 48; continue; }
                if (U01(STREAT ^ rs, first + i) < TreatChance(r)) { treatFrom.Add(startH - 12); treatTo.Add(endH + 48); }
            }

            var rd = new DeckState(); var br = new DeckState();
            float depth = 0f, groundWet = 0f, meanAir = t.rec[1].loC * 0.5f + t.rec[1].hiC * 0.5f;
            int ti = 0;
            for (int i = 0; i < t.days; i++)
            {
                int day = first + i;
                DayRec prev = t.rec[i], today = t.rec[i + 1], next = t.rec[i + 2];
                bool humidNight = U01(SFROST ^ rs, day) < (r == ClimateRegion.Coast ? 0.6 : 0.3);
                for (int h = 0; h < 24; h++)
                {
                    float clock = h + 0.5f;
                    float air = AirAt(prev, today, next, clock);
                    Sun(day, clock, out float s, out float sinNoon);

                    // What is falling: today's spell, or yesterday's running on.
                    float rate = 0f; bool conv = false;
                    if (today.precipDay && clock >= today.pStart && clock < today.pStart + today.pDur) { rate = 1f; conv = today.convective; }
                    else if (prev.precipDay && clock + 24f < prev.pStart + prev.pDur) { rate = 1f; conv = prev.convective; }
                    float amtPerH = 0f;
                    if (rate > 0f)
                    {
                        var src = clock >= today.pStart && today.precipDay ? today : prev;
                        amtPerH = src.amountMm / Math.Max(1f, src.pDur);
                        rate = conv ? 1f : Math.Min(1f, 0.45f + amtPerH / 4f);
                    }
                    PrecipKind pk = rate <= 0f ? PrecipKind.None : air <= snowAt ? PrecipKind.Snow : PrecipKind.Rain;

                    // The sky this hour.
                    SkyKind sky;
                    if (pk != PrecipKind.None) sky = conv || rate >= 0.95f ? SkyKind.Storm : SkyKind.Overcast;
                    else
                    {
                        sky = today.drySky;
                        if (today.precipDay)
                        {
                            float end = today.pStart + today.pDur;
                            if (clock < today.pStart) sky = today.pStart - clock <= 3f ? SkyKind.Overcast : PrecipBefore(today, prev);
                            else if (clock >= end) sky = clock - end <= (today.convective ? 1f : 2f) ? SkyKind.Overcast : today.afterSky;
                        }
                        else if (prev.precipDay && prev.pStart + prev.pDur > 24f)
                        {
                            float end = prev.pStart + prev.pDur - 24f;
                            sky = clock - end <= 2f ? SkyKind.Overcast : prev.afterSky;
                        }
                        if (next.precipDay && next.pStart + 24f - clock <= 3f) sky = SkyKind.Overcast;
                    }
                    float cloud = CloudOf(sky);

                    // Temperatures: the road's solar gain and its ground, the
                    // bridge's thin deck open to the sky on both faces.
                    meanAir += (air - meanAir) / 48f;
                    float gain = (3f + 15f * sinNoon) * s * (1f - 0.8f * cloud);
                    float roadC = air + gain + 0.3f * (meanAir - air) - 1.8f * (1f - cloud) * (s > 0f ? 0.3f : 1f);
                    float bridgeC = air + 0.55f * gain - (2.6f * (1f - cloud) + 0.6f) * (s > 0f ? 0.25f : 1f);

                    bool treated = false;
                    int hi = i * 24 + h;
                    for (int k = 0; k < treatFrom.Count; k++) if (hi >= treatFrom[k] && hi <= treatTo[k]) { treated = true; break; }

                    rd.tempC = roadC; br.tempC = bridgeC;
                    StepDeck(ref rd, pk, rate, amtPerH, s, cloud, treated, false, humidNight, groundWet);
                    StepDeck(ref br, pk, rate, amtPerH, s, cloud, treated, true, humidNight, groundWet);

                    // The scenery: snow lies where it falls cold enough and goes
                    // with the warmth and the sun; the ground stays wet longer
                    // than the road.
                    if (pk == PrecipKind.Snow && air <= snowAt + 1.5f) depth += amtPerH;
                    else if (pk == PrecipKind.Rain) depth -= 0.25f * amtPerH;   // rain on snow
                    depth -= 0.06f * Math.Max(0f, air) + 0.25f * s * (1f - cloud) * sinNoon;
                    if (depth < 0f) depth = 0f;
                    if (pk == PrecipKind.Rain) groundWet = 1f;
                    else groundWet = Math.Max(0f, groundWet - (0.02f + 0.08f * s * (1f - cloud)));

                    bool fog = false;
                    if (pk != PrecipKind.Snow && !conv)
                    {
                        if (today.fogDay && clock < 10.5f && sky != SkyKind.Storm) fog = true;
                        else if (next.fogDay && clock >= 22f) fog = true;
                        else if (r == ClimateRegion.Mountains && pk == PrecipKind.Rain && U01(SRIDGE, day) < 0.4) fog = true;
                    }

                    t.hours[ti++] = new Hour
                    {
                        air = air, road = roadC, bridge = bridgeC, rate = pk == PrecipKind.None ? 0f : rate,
                        depth = depth, groundWet = groundWet, precip = (byte)pk, sky = (byte)sky,
                        fog = fog, treated = treated, rd = rd, br = br,
                    };
                }
            }
            return t;
        }

        static SkyKind PrecipBefore(DayRec today, DayRec prev)
        {
            // The morning before an afternoon storm is a summer morning; the
            // morning before a day's rain is already grey.
            return today.convective ? SkyKind.PartlyCloudy : SkyKind.Overcast;
        }

        /// <summary>One hour of a deck's water, slush, snow, ice and frost.</summary>
        static void StepDeck(ref DeckState d, PrecipKind pk, float rate, float amtPerH, float s, float cloud,
                             bool treated, bool isBridge, bool humidNight, float groundWet)
        {
            float T = d.tempC;
            float freezeAt = treated ? -6f : 0f;
            if (pk == PrecipKind.Rain)
            {
                d.water = Math.Min(1f, d.water + 0.7f * rate);
                d.frost = false;
            }
            else if (pk == PrecipKind.Snow)
            {
                if (treated) d.slush = Math.Min(1f, d.slush + amtPerH / 2f);
                else if (T <= 0.5f) d.snow = Math.Min(1f, d.snow + amtPerH / 3f);
                else d.water = Math.Min(1f, d.water + amtPerH * 0.3f);
                d.frost = false;
            }
            else
            {
                float e = 0.03f + 0.20f * s * (1f - 0.8f * cloud) + 0.006f * Math.Max(0f, T);
                if (isBridge) e *= 1.2f;   // wind across both faces
                if (!d.ice) d.water = Math.Max(0f, d.water - e);
            }
            // Slush melts (brine) and is churned off by traffic.
            if (d.slush > 0f && T > freezeAt)
            {
                float m = Math.Min(d.slush, 0.08f + 0.04f * Math.Max(0f, T) + 0.04f);
                d.slush -= m; d.water = Math.Min(1f, d.water + 0.8f * m);
            }
            // A treated road's ploughs and salt turn snow already lying into slush.
            if (treated && d.snow > 0f)
            {
                float m = Math.Min(d.snow, 0.35f);
                d.snow -= m; d.slush = Math.Min(1f, d.slush + m);
            }
            // Snow on an untreated deck: melts in the warmth, packs under traffic.
            if (d.snow > 0f)
            {
                float m = T > 0f ? Math.Min(d.snow, 0.04f * T + 0.2f * s * (1f - cloud)) : 0f;
                d.snow = Math.Max(0f, d.snow - m - 0.01f);
                d.water = Math.Min(1f, d.water + 0.6f * m);
            }
            // Water freezes (black ice) and thaws.
            if (d.ice) { if (T > freezeAt + 0.5f) d.ice = false; }
            else if (d.water > 0.12f && T <= freezeAt) d.ice = true;
            // Frost: a deck that falls below freezing on a still, clear, humid
            // night. Bridges only - the road keeps the ground's warmth.
            if (isBridge)
            {
                if (pk == PrecipKind.None && T <= -1.0f && cloud < 0.6f && (humidNight || groundWet > 0.1f) && !d.ice) d.frost = true;
                if (d.frost && (T > 0.5f || s > 0.25f)) d.frost = false;
            }
        }

        public static int DayNumber(DateTime d) => (d.Date - Epoch).Days + 1;

        // ---- the public answer ----------------------------------------------

        /// <summary>The weather at a venue on a day at a clock hour (0..24).</summary>
        public static WeatherNow At(ClimateRegion r, float elevM, int day, float clock)
        {
            var w = new WeatherNow { day = day, clock = clock, region = r, elevM = elevM };
            if (day <= 0)
            {
                // No calendar (the editor pressing Play on a circuit): a mild,
                // dry autumn afternoon - every scene as it was baked.
                w.fixedDay = true;
                w.hiC = 21f; w.loC = 11f; w.airC = 18f;
                w.sky = SkyKind.PartlyCloudy;
                w.road.tempC = 22f; w.bridge.tempC = 18f;
                return w;
            }
            int year = DateOf(day).Year;
            var t = TableFor(r, elevM, year);
            int i = day - t.firstDay;
            int h = Math.Max(0, Math.Min(23, (int)Math.Floor(clock)));
            var hr = t.hours[i * 24 + h];
            var rec = t.rec[i + 1];
            w.hiC = rec.hiC; w.loC = rec.loC; w.airC = hr.air;
            w.sky = (SkyKind)hr.sky; w.precip = (PrecipKind)hr.precip; w.precipRate = hr.rate; w.fog = hr.fog;
            w.road = hr.rd; w.bridge = hr.br; w.road.tempC = hr.road; w.bridge.tempC = hr.bridge;
            w.treated = hr.treated;
            w.snowDepthCm = hr.depth; w.groundWet = hr.groundWet;
            w.scenery = hr.depth >= 1f ? SceneryKind.Snow : hr.groundWet > 0.35f ? SceneryKind.Wet : SceneryKind.Dry;
            return w;
        }

        /// <summary>The day's synoptic record (for labels and the printout).</summary>
        public static DayRec DayAt(ClimateRegion r, float elevM, int day)
        {
            if (day <= 0) return new DayRec { hiC = 21f, loC = 11f, drySky = SkyKind.PartlyCloudy, afterSky = SkyKind.PartlyCloudy };
            var t = TableFor(r, elevM, DateOf(day).Year);
            return t.rec[day - t.firstDay + 1];
        }

        /// <summary>Did snow fall at any hour of this day (the day's headline)?</summary>
        public static bool SnowFellOn(ClimateRegion r, float elevM, int day)
        {
            if (day <= 0) return false;
            var t = TableFor(r, elevM, DateOf(day).Year);
            int i = day - t.firstDay;
            for (int h = 0; h < 24; h++) if (t.hours[i * 24 + h].precip == (byte)PrecipKind.Snow) return true;
            return false;
        }

        /// <summary>Snowfall over the day, cm (1 mm of water = 1 cm).</summary>
        public static float SnowfallCm(ClimateRegion r, float elevM, int day)
        {
            if (day <= 0) return 0f;
            var t = TableFor(r, elevM, DateOf(day).Year);
            int i = day - t.firstDay;
            var rec = t.rec[i + 1]; var prev = t.rec[i];
            float cm = 0f;
            for (int h = 0; h < 24; h++)
            {
                if (t.hours[i * 24 + h].precip != (byte)PrecipKind.Snow) continue;
                var src = h + 0.5f >= rec.pStart && rec.precipDay ? rec : prev;
                cm += src.amountMm / Math.Max(1f, src.pDur);
            }
            return cm;
        }

        // ---- grip -----------------------------------------------------------

        /// <summary>Dry tarmac = 1. Wet = the 2026-10-06 wet-tarmac number.</summary>
        public const float WetGrip = 0.70f, SlushGrip = 0.55f, SnowGrip = 0.42f, FrostGrip = 0.55f, IceGrip = 0.28f;
        public const float WetOffroadGrip = 0.65f, SnowOffroadGrip = 0.45f;
    }
}
