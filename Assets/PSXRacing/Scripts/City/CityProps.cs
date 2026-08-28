using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// The prefab side of the city's buildings: real models (houses, trailers,
    /// restaurants, mid-rise blocks) that stand where CityBuildings decides,
    /// streamed in and out with their tile.
    ///
    /// One table, used from three places, which is the point of it existing:
    /// CityBuildings reads the FOOTPRINTS so the corridor/occupancy tests use
    /// the real lot size, CityWorld reads the RESOURCE names to instantiate,
    /// and the editor prop baker asserts it baked a prefab for every row. A
    /// kind that is missing its prefab degrades to nothing at runtime — the
    /// lot stays empty rather than throwing — and the self-test is what makes
    /// that an error a build cannot ship with.
    /// </summary>
    public static class CityProps
    {
        public struct Def
        {
            public string res;          // Resources path
            public float w, d, h;       // FINAL metres, i.e. after scale
            public float sink;          // metres buried below GroundY
            public float yawOffsetDeg;  // model front → +Z correction
            /// <summary>Baked into the prefab by the prop baker. The house pack
            /// ships ~1.23x oversized — measured against its own interior doors,
            /// which are the one feature with a real-world size — and an
            /// oversized building next to a correctly-sized car makes the CAR
            /// look wrong. 0 means 1.</summary>
            public float scale;
            public float Scale => scale > 0.01f ? scale : 1f;
        }

        /// <summary>
        /// The house pack's own correction, from GarageSceneBuilder's door
        /// measurement: its interior doors are 2.51 m against a real 2.03.
        /// Applied to the residential models and to the single-storey
        /// restaurant, whose 6.5 m parapet was a storey and a half too tall.
        /// The multi-storey blocks are left alone — 13.5 m over four floors is
        /// already right, and scaling them would make their floors 2.7 m.
        /// </summary>
        public const float PackScale = 0.81f;

        public const byte House = 1;
        public const byte Trailer0 = 2;
        public const byte Trailer1 = 3;
        public const byte Trailer2 = 4;
        public const byte Block0 = 5;   // 5..12 are the eight mid-rise shells
        public const byte Burger = 20;
        public const byte Pizzeria = 21;

        public static bool IsFood(byte kind) => kind == Burger || kind == Pizzeria;

        /// <summary>Shop sign, for the HUD's nearest-food cue and the order
        /// screen. Here rather than on DriveThru because the HUD has to name a
        /// restaurant it has not reached yet, which means naming it from the
        /// map rather than from the component standing in it.</summary>
        public static string FoodName(byte kind) =>
            kind == Burger ? "STACK BURGER" : kind == Pizzeria ? "SLICE HOUSE" : "";

        // yawOffsetDeg = 180 on every row: the whole pack models its fronts
        // toward Blender -Y, which lands at Unity -Z — and the placement code
        // points +Z at the road. Verified off the first preview pass, where
        // every restaurant politely showed the street its back.
        public static readonly Dictionary<byte, Def> Defs = new Dictionary<byte, Def>
        {
            [House]    = new Def { res = "CityProps/house_simple", w = 11.7f, d = 16.4f, h = 7.4f, sink = 0.30f, yawOffsetDeg = 180f, scale = PackScale },
            [Trailer0] = new Def { res = "CityProps/trailer_00", w = 4.9f, d = 12.2f, h = 3.2f, sink = 0.20f, yawOffsetDeg = 180f, scale = PackScale },
            [Trailer1] = new Def { res = "CityProps/trailer_02", w = 4.8f, d = 12.2f, h = 3.3f, sink = 0.20f, yawOffsetDeg = 180f, scale = PackScale },
            [Trailer2] = new Def { res = "CityProps/trailer_05", w = 4.8f, d = 12.3f, h = 3.2f, sink = 0.20f, yawOffsetDeg = 180f, scale = PackScale },
            [Block0 + 0] = new Def { res = "CityProps/city_building_03", w = 18.7f, d = 12.6f, h = 13.5f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 1] = new Def { res = "CityProps/city_building_05", w = 16.0f, d = 12.4f, h = 13.5f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 2] = new Def { res = "CityProps/city_building_08", w = 19.6f, d = 14.1f, h = 18.5f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 3] = new Def { res = "CityProps/city_building_11", w = 14.3f, d = 11.7f, h = 14.6f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 4] = new Def { res = "CityProps/city_building_15", w = 18.0f, d = 13.4f, h = 13.5f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 5] = new Def { res = "CityProps/city_building_16", w = 18.6f, d = 14.3f, h = 18.4f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 6] = new Def { res = "CityProps/city_building_17", w = 16.4f, d = 12.5f, h = 13.5f, sink = 0.45f, yawOffsetDeg = 180f },
            [Block0 + 7] = new Def { res = "CityProps/city_building_18", w = 17.7f, d = 12.8f, h = 13.0f, sink = 0.45f, yawOffsetDeg = 180f },
            [Burger]   = new Def { res = "CityProps/burger_drive", w = 21.7f, d = 36.3f, h = 5.6f, sink = 0.25f, yawOffsetDeg = 180f, scale = PackScale },
            [Pizzeria] = new Def { res = "CityProps/pizzeria", w = 21.4f, d = 10.8f, h = 11.5f, sink = 0.25f, yawOffsetDeg = 180f },
        };

        static readonly Dictionary<byte, GameObject> cache = new Dictionary<byte, GameObject>();
        static readonly HashSet<byte> warned = new HashSet<byte>();

        public static GameObject Prefab(byte kind)
        {
            if (cache.TryGetValue(kind, out var go)) return go;
            if (!Defs.TryGetValue(kind, out var def)) return null;
            go = Resources.Load<GameObject>(def.res);
            if (go == null && warned.Add(kind))
                Debug.LogWarning("[City] prop prefab missing: " + def.res +
                                 " — run the scene build to bake CityProps.");
            cache[kind] = go;
            return go;
        }
    }
}
