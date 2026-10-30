namespace PSXRacing.City
{
    public static partial class CitySigns
    {
        /// <summary>
        /// City perf round 1 (2026-10-30): the city-wide sign tables worked out
        /// at LOAD. EnsureSites (every guide sign's site: the exits, the
        /// gantries, the curve warnings) ran inside the first tree frame's sign
        /// slice: 262.8 ms in ONE frame in the editor (Editor/HitchProbe.cs,
        /// "signs/poles slice"), a good second of a browser's - the first
        /// seconds of every free roam. CityWorld.EnsureInit calls this behind
        /// the loading screen; the tables are the same either way.
        /// </summary>
        public static void Warm(CityMap map)
        {
            if (!Enabled || map == null || !CitySignData.Loaded) return;
            EnsureSites(map);
        }
    }
}
