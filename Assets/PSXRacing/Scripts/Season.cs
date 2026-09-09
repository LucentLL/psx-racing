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
        /// The weather on a day, rolled from the day number alone.
        ///
        /// Deterministic on purpose — it is not saved anywhere, and it does
        /// not need to be: the same day always rolls the same, so the date
        /// line on the home screen can say RAIN before you leave the house
        /// and the race you arrive at agrees with it. The odds are a mountain
        /// year in North Carolina, per mille per month: snow only from
        /// November to March, fog thickest in the fall, rain everywhere but
        /// heaviest in spring.
        /// </summary>
        public static Weather WeatherFor(int day)
        {
            if (day <= 0) return Weather.Clear;
            int m = LifeRules.MonthOf(day) - 1;
            int snow = SnowPerMille[m], fog = FogPerMille[m], rain = RainPerMille[m];
            int r = Roll(day);
            if (r < snow) return Weather.Snow;
            if (r < snow + fog) return Weather.Fog;
            if (r < snow + fog + rain) return Weather.Rain;
            return Weather.Clear;
        }

        // Jan .. Dec.
        static readonly int[] SnowPerMille = { 180, 160, 60, 0, 0, 0, 0, 0, 0, 0, 30, 150 };
        static readonly int[] FogPerMille  = { 120, 120, 130, 110, 90, 60, 50, 60, 120, 160, 150, 120 };
        static readonly int[] RainPerMille = { 100, 110, 170, 190, 180, 160, 170, 160, 120, 110, 120, 110 };

        /// <summary>0-999 from the day number. An integer hash rather than
        /// System.Random so the answer is the same on every platform the game
        /// ships to, which a seeded Random is not guaranteed to be.</summary>
        static int Roll(int day)
        {
            uint h = (uint)day * 2654435761u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (int)(h % 1000u);
        }

        /// <summary>The word the date line prints, or null on a clear day.</summary>
        public static string WeatherLabel(Weather w) => w == Weather.Clear ? null : w.ToString().ToUpperInvariant();

        /// <summary>Which of the five dresses a season and a sky call for.</summary>
        public static int DressIndex(Season s, Weather w) => w == Weather.Snow ? DressSnow : (int)s;

        // ---- the world as it is right now ---------------------------------
        //
        // All off RaceHandoff.CalendarDay, which the home screen stamps every
        // time it draws itself and nothing clears. Memoised by day because
        // the tyres ask every wheel every tick.

        public static int CurrentDay => RaceHandoff.CalendarDay;
        public static Season Current => Of(CurrentDay);
        public static Weather CurrentWeather
        {
            get
            {
                int d = CurrentDay;
                if (d != cachedDay) { cachedDay = d; cachedWeather = WeatherFor(d); }
                return cachedWeather;
            }
        }
        public static int CurrentDress => DressIndex(Current, CurrentWeather);
        static int cachedDay = int.MinValue;
        static Weather cachedWeather;

        // ---- what the weather does ----------------------------------------
        //
        // Starting values. Rain and snow cost grip and close the fog in; fog
        // is fog. All three run the headlights, which is the cue a player
        // reads before any of the numbers.

        /// <summary>Multiplier on the tyre's road or off-road mu.</summary>
        public static float GripMult(Weather w, bool road)
        {
            switch (w)
            {
                case Weather.Fog:  return road ? 0.97f : 0.95f;
                case Weather.Rain: return road ? 0.85f : 0.78f;
                case Weather.Snow: return road ? 0.72f : 0.62f;
                default: return 1f;
            }
        }
        public static float RoadGripMult => GripMult(CurrentWeather, true);
        public static float OffroadGripMult => GripMult(CurrentWeather, false);

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
