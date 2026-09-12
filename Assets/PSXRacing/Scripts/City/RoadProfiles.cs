using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// What a stretch of Charlotte road is PAINTED like, as a closed table.
    ///
    /// The exporter hands every edge a real lane count, whether it is one
    /// carriageway of a divided road, and which kind of road it is; the
    /// texture that carries the markings is drawn per PROFILE (lanes x
    /// direction x shoulder class), so the mesh, the painter and the
    /// material table all read the same row here. The road's paved width is
    /// DERIVED from the row — the lane ladder is 3.6576 m and never
    /// stretches — which is what keeps a painted lane the width of a real
    /// one whatever OSM's tag said.
    ///
    /// Shoulders are asymmetric on a one-way carriageway and given IN THE
    /// DIRECTION OF TRAVEL: a freeway's outside (right) shoulder is a full
    /// 3 m, its inside one 1.2 m. That asymmetry is most of what makes a
    /// carriageway read as one side of a freeway rather than as a wide
    /// one-way street.
    /// </summary>
    public static class RoadProfiles
    {
        public const float LaneM = 3.6576f;

        public enum Kind : byte { Street = 0, OneWayStreet, Ramp, Expressway, Motorway }

        public struct Profile
        {
            public Kind kind;
            public bool oneway;
            public int lanes;          // total, both directions on a two-way road
            public bool turnLane;      // two-way with a centre turn lane (TWLTL)
            public float shl, shr;     // paved shoulder left / right of travel
            public string key;         // texture / material name stem

            public float Width => lanes * LaneM + shl + shr;
            /// <summary>Metres from the left edge to the centre of the
            /// carriageway paint (the double yellow, or the turn lane).</summary>
            public float LanesPerSide => oneway ? lanes : (lanes - (turnLane ? 1 : 0)) * 0.5f;
        }

        static Profile TW(int lanes, bool turn) => new Profile
        { kind = Kind.Street, oneway = false, lanes = lanes, turnLane = turn, shl = 0.3f, shr = 0.3f, key = "tw" + lanes + (turn ? "t" : "") };
        static Profile OW(int lanes) => new Profile
        { kind = Kind.OneWayStreet, oneway = true, lanes = lanes, shl = 0.3f, shr = 0.3f, key = "ow" + lanes };
        static Profile Ramp(int lanes) => new Profile
        { kind = Kind.Ramp, oneway = true, lanes = lanes, shl = 0.6f, shr = 1.8f, key = "ramp" + lanes };
        static Profile Xw(int lanes) => new Profile
        { kind = Kind.Expressway, oneway = true, lanes = lanes, shl = 0.9f, shr = 2.4f, key = "xw" + lanes };
        static Profile Mw(int lanes) => new Profile
        { kind = Kind.Motorway, oneway = true, lanes = lanes, shl = 1.2f, shr = 3.0f, key = "mw" + lanes };

        /// <summary>Every profile the painter draws, in slot order.</summary>
        public static readonly Profile[] All =
        {
            TW(2, false), TW(3, true), TW(4, false), TW(5, true), TW(6, false),
            OW(1), OW(2), OW(3), OW(4),
            Ramp(1), Ramp(2), Ramp(3),
            Xw(2), Xw(3), Xw(4),
            Mw(2), Mw(3), Mw(4), Mw(5), Mw(6),
        };
        /// <summary>The table's length as a CONSTANT, because CityMeshes.Slot
        /// counts material slots from it and an enum member has to be one.
        /// The static initialiser below shouts if the table drifts.</summary>
        public const int ProfileCount = 20;
        public static int Count => All.Length;

        static RoadProfiles()
        {
            if (All.Length != ProfileCount)
                Debug.LogError("RoadProfiles.All has " + All.Length + " rows but ProfileCount says " + ProfileCount);
        }

        /// <summary>The row an edge is drawn from. Lane counts clamp into the
        /// table rather than growing it: a seven-lane surface street is
        /// painted as six, which nobody driving it will count.</summary>
        public static int IndexFor(int cls, bool link, bool oneway, int lanes, bool turnLane)
        {
            if (!oneway)
            {
                // Two-way: 2, 3T, 4, 5T, 6. An odd count IS a turn lane; an
                // even count with a turn-lane tag rounds up to the next odd.
                int n = Mathf.Clamp(lanes, 2, 6);
                if (turnLane && n % 2 == 0 && n < 6) n++;
                if (n == 3 || n == 5) return n == 3 ? 1 : 3;
                return n <= 2 ? 0 : n == 4 ? 2 : 4;
            }
            if (link)
            {
                // Ramps and collector-distributor roads.
                if (cls >= 4) return 9 + Mathf.Clamp(lanes, 1, 3) - 1;
                return 5 + Mathf.Clamp(lanes, 1, 4) - 1;   // a surface-street slip: a one-way street
            }
            if (cls >= 5) return 15 + Mathf.Clamp(lanes, 2, 6) - 2;
            if (cls == 4) return 12 + Mathf.Clamp(lanes, 2, 4) - 2;
            return 5 + Mathf.Clamp(lanes, 1, 4) - 1;
        }
    }
}
