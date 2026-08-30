using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// What the speedometer counts in, remembered across sessions.
    ///
    /// MPH by default. The physics, the catalog and every saved number stay in
    /// km/h — a preference that reached into the simulation would be a second
    /// set of units to keep in step with the first — so this is purely a
    /// display conversion, applied at the handful of places a speed is actually
    /// printed. There is exactly one of those per surface: the dial and its
    /// readout, the drag trap, the pause menu's debug block, and the spec line
    /// in the classifieds.
    ///
    /// Same shape as <see cref="LookPrefs"/> and <see cref="ClusterBulbs"/>: a
    /// lazily-read PlayerPrefs int with an eager Save, because on the Web build
    /// a preference that is not flushed is a preference lost to the next tab
    /// close. <see cref="Changed"/> is the dirty token the cluster compares
    /// against, for the same reason the bulb has one — an event across a scene
    /// load is a dangling reference waiting to happen.
    /// </summary>
    public static class SpeedUnits
    {
        const string PrefKey = "psx.mph";

        /// <summary>Miles per hour in one kilometre per hour. The exact figure,
        /// since it costs nothing: 1 mile is 1609.344 m by definition.</summary>
        public const float MphPerKmh = 1f / 1.609344f;

        static int cached = -1;

        public static bool Mph
        {
            get
            {
                // Defaults to 1. The game is set in North Carolina in 1999 and
                // every car in the catalog is being driven on a US road.
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 1);
                return cached != 0;
            }
            set
            {
                int v = value ? 1 : 0;
                if (cached == v) return;
                cached = v;
                PlayerPrefs.SetInt(PrefKey, v);
                PlayerPrefs.Save();
                Changed++;
            }
        }

        /// <summary>Bumped on every change, so a cluster can notice by
        /// comparing an int rather than by subscribing to anything.</summary>
        public static int Changed { get; private set; }

        /// <summary>A speed the simulation produced, in the unit the player
        /// reads. The ONE conversion in the game — call this rather than
        /// writing 0.621 anywhere.</summary>
        public static float FromKmh(float kmh) => Mph ? kmh * MphPerKmh : kmh;

        /// <summary>What to print beside it. Upper case: it is a dial
        /// legend.</summary>
        public static string Label => Mph ? "MPH" : "KM/H";

        /// <summary>Lower case, for a line of prose rather than an
        /// instrument.</summary>
        public static string Suffix => Mph ? " mph" : " km/h";

        public static void Toggle() => Mph = !Mph;
    }
}
