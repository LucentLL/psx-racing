namespace PSXRacing.Town
{
    /// <summary>
    /// WHERE "HOME" IS: the town map's neighbourhood end, or the old street
    /// map of its own.
    ///
    /// The owner, 2026-11-14: "I want Town and Home/Neighborhood merged onto a
    /// single map. This area will be purely for testing in debugging.
    /// Eventually, all features in this will be placed in Charlotte ...
    /// Delivering pizza is no longer a race track, but to a random house in
    /// the neighborhood." And: "add the neighborhood to one end of the large
    /// town map. don't rewire the town."
    ///
    /// So the street, its houses and your own house and garage are built onto
    /// the WEST end of Town.unity (PSXRacingBuilder.TownHomeEnd), joined to the
    /// main street by a road, and every door that used to load the street
    /// scene loads the town instead, where the car is parked on your own drive.
    /// Neighborhood.unity stays in the build, unused, so the switch below can
    /// put the old map back without a rebuild.
    ///
    /// Read by the doors that go HOME (LifeHomeScreen.StartTown, HomeWalk,
    /// GarageWorld.DriveOut) rather than by TrackCatalog: the door audit asks
    /// the catalog where the Neighborhood scene is, and it still is there.
    /// </summary>
    public static class TownLayout
    {
        /// <summary>PSX_TOWN_HOMEEND=0: home is the old street map again (the
        /// builder reads the same switch and leaves the town as it was).</summary>
        public static readonly bool Switch =
            System.Environment.GetEnvironmentVariable("PSX_TOWN_HOMEEND") != "0";

        /// <summary>PSX_TOWN_LOCALDROP=0: an order goes to a race track from
        /// the edge of town, as before, even on the merged map.</summary>
        public static readonly bool LocalDrops =
            System.Environment.GetEnvironmentVariable("PSX_TOWN_LOCALDROP") != "0";

        /// <summary>Your street is the town's west end in this build.</summary>
        public static bool HomeInTown => Switch && TrackCatalog.TownSceneIndex > 0;

        /// <summary>The scene a trip HOME loads: the town (whose baked spawn is
        /// your own drive) or, switched off, the street map.</summary>
        public static int HomeSceneIndex =>
            HomeInTown ? TrackCatalog.TownSceneIndex : TrackCatalog.NeighborhoodSceneIndex;

        /// <summary>The neighbourhood's one street, for addresses.</summary>
        public const string StreetName = "HOLLY RIDGE COURT";
        public const string StreetShort = "HOLLY RIDGE CT";
    }
}
