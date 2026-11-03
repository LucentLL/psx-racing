using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE FIELD'S SIZE AND GRID (owner, 2026-11-02: "increase the potential
    /// racer cap per race to 8 (including the player)"). Every race scene
    /// bakes <see cref="MaxCars"/> cars - the player and seven rivals - and a
    /// race that names fewer retires the spares at load
    /// (RaceHandoffApplier.ApplyField). The grid stands TWO ABREAST: slot 0
    /// and 1 are the front row, the player's slot the last, so eight cars
    /// take the same 28.5 m of road four took in single file (a stage's
    /// lead-in is 33-60 m). A drag strip races four abreast and a parking
    /// deck's roof grid holds four: there the field is cut to
    /// <see cref="DragAndDeckCars"/>.
    /// </summary>
    public static class RaceField
    {
        public const int MaxCars = 8;
        public const int DragAndDeckCars = 4;

        /// <summary>The grid row of a slot (0 = front), two cars to a row.</summary>
        public static int Row(int slot) => slot / 2;

        /// <summary>Which side of the road a slot stands on: -1 left, +1 right.</summary>
        public static float Side(int slot) => slot % 2 == 0 ? -1f : 1f;

        /// <summary>How many cars (player included) this venue can race.</summary>
        public static int MaxFor(TrackCatalog.TrackDef venue) =>
            venue != null && (venue.IsDragEvent || venue.IsDeckRun) ? DragAndDeckCars : MaxCars;

        /// <summary>How many cars a path's venue can race, by the path: a drag
        /// presentation stands abreast on the line.</summary>
        public static int MaxFor(TrackPath path, TrackCatalog.TrackDef venue) =>
            path != null && path.drag ? DragAndDeckCars : MaxFor(venue);
    }
}
