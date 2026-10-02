namespace PSXRacing.City
{
    /// <summary>
    /// THE ROADS PASS'S THREE TIERS (2026-10-02, plan P0), the order the owner
    /// asked for: "Highways and primary roads first. Then minor roads. Then
    /// neighborhood roads and parking lots." Every new audit counts by tier,
    /// and a tier's checks become gates when that tier ships.
    ///
    ///   T1  cls >= 3: motorway (5), trunk (4), primary (3), and their links
    ///   T2  cls 1-2: secondary (2), tertiary (1), and their links
    ///   T3  cls 0: residential, unclassified, living_street, service - and
    ///       parking aisles when the city carries them
    ///
    /// A link keeps its base class (<see cref="CityMap.Edge.cls"/>), so a
    /// motorway_link is T1. This is the one definition; the offline tools'
    /// mirror is tools/city/lib/citydata.mjs tierOf (added by the city lane,
    /// plan B1), and the two must agree.
    /// </summary>
    public static class CityTier
    {
        public const int T1 = 1, T2 = 2, T3 = 3;

        /// <summary>1, 2 or 3 for an edge.</summary>
        public static int Of(CityMap.Edge e) => OfClass(e.cls);

        /// <summary>1, 2 or 3 for a road class (0 local ... 5 motorway).</summary>
        public static int OfClass(int cls) => cls >= 3 ? T1 : cls >= 1 ? T2 : T3;

        /// <summary>"T1", "T2", "T3" (a column header).</summary>
        public static string Short(int tier) => tier == T1 ? "T1" : tier == T2 ? "T2" : tier == T3 ? "T3" : "T?";

        /// <summary>What a tier holds, for a report line.</summary>
        public static string Name(int tier) =>
            tier == T1 ? "T1 highways + primary (motorway, trunk, primary, links)" :
            tier == T2 ? "T2 minor roads (secondary, tertiary, links)" :
            tier == T3 ? "T3 neighbourhood + parking (residential, unclassified, service)" : "T? (no tier)";
    }
}
