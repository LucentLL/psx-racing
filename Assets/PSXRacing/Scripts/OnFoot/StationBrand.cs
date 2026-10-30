namespace PSXRacing.OnFoot
{
    /// <summary>
    /// The filling stations' store, named in ONE place (city fuel r1,
    /// 2026-10-30). The pack's own store was "6twelve", a parody of a real
    /// chain; the owner's game is to be sold, so every station in the game -
    /// the city's, the LifeSim town's and the circuits' fuel stops - is the
    /// made-up PUMPWREN. "The name PUMPWREN stays unless the owner asks for
    /// another."
    ///
    /// A rename is one change and one atlas rebuild:
    ///   1. change <see cref="Name"/> (and <see cref="Split"/>, where the two
    ///      lettered rows break it) here;
    ///   2. run <c>py tools/city/fuel_art.py</c> - it reads both constants
    ///      out of THIS file (its <c>--name PUMP/WREN</c> argument overrides
    ///      them for a trial sheet) and re-letters the pylon, its night
    ///      emissor and the badge - then <c>py tools/city/signs_atlas.py</c>,
    ///      which composes the badge into the city's sign atlas;
    ///   3. commit. Every UI string (the store's title, its log line, the
    ///      door prompt, the LifeSim's groceries line) reads the constants.
    /// </summary>
    public static class StationBrand
    {
        /// <summary>The store's name in capitals, as the signs letter it.</summary>
        public const string Name = "PUMPWREN";

        /// <summary>The pylon and the badge stack the name in two rows:
        /// Name[..Split] over Name[Split..] (PUMP / WREN).</summary>
        public const int Split = 4;

        /// <summary>The name as a sentence writes it: "Pumpwren".</summary>
        public static readonly string Proper =
            Name.Substring(0, 1) + Name.Substring(1).ToLowerInvariant();

        /// <summary>The store as the activity log places a purchase:
        /// "... at the Pumpwren".</summary>
        public static readonly string LogPlace = "the " + Proper;
    }
}
