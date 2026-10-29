using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// What Charlotte's signs are placed from (plan WP-23), made by
    /// tools/city/signs.mjs into Resources/charlotte_signs.bytes. Its own file,
    /// like the canopy grid: the lines release re-exports the roads without it,
    /// and <see cref="CitySigns"/> places everything from the live graph.
    ///
    /// PSGN v2 (little-endian; v1 is v2 without the exits):
    ///   u32 'PSGN' | i32 2 | i32 nx, nz | f32 x0, z0, cell | nx*nz u8 grid
    ///   u16 nRoutes { u8 len, name | u8 cls | f32 perRouteKm | f32 perZonedCarriagewayKm }
    ///   u32 nWays { u32 wayId | u8 route }
    ///   u32 nBill { f32 x, z | f32 yawDeg (NaN: a node) | u8 flags (1 lit) }
    ///   u32 nPoi { f32 x, z | u8 kind }
    ///   u32 nGant { u32 wayId | u8 n | n x u8 lanes }
    ///   u32 nExit { f32 x, z | u8 flags | u8 advisory mph | 7 x (u8 len, latin1):
    ///               number, route, dest1, dest2, cardinal, thru, thruCity }
    /// (v2, the highway signs pass, 2026-10-29: the guide signs' legends,
    /// <see cref="Exit"/>.)
    /// The grid is on the DEM's 60 m lattice (a cell centred on its node): bit
    /// 1 = billboard zoning (commercial or industrial land near: NC 19A NCAC
    /// 02E .0203), bit 2 = commercial frontage.
    /// </summary>
    public static class CitySignData
    {
        const uint Magic = 0x4E475350;   // "PSGN"
        static bool tried;
        static int nx, nz;
        static float x0, z0, cell = 60f;
        static byte[] grid;

        public struct Route { public string name; public byte cls; public float density, rate; }
        public struct Board { public Vector2 pos; public float yawDeg; public bool lit; public bool isWay => !float.IsNaN(yawDeg); }
        public struct Poi { public Vector2 pos; public byte kind; }
        /// <summary>
        /// An exit's guide-sign legends, as the map words them (signs.mjs): the
        /// number ("11 B"), the route it leads to as words ("NC 16 NORTH": no
        /// shield is ever drawn), up to two destinations ("Brookshire Frwy"), a
        /// destination's own cardinal ("WEST"), the posted exit-speed advisory
        /// (0: none posted), and the motorway going on ("I-77 NORTH",
        /// "Statesville"). Keyed by the diverge node.
        /// </summary>
        public struct Exit
        {
            public Vector2 pos;
            public byte flags, advisory;
            public string number, route, dest1, dest2, cardinal, thru, thruCity;
            /// <summary>destination:lanes gives the ramp's destination to the left lane.</summary>
            public bool LeftByLanes => (flags & 1) != 0;
        }

        public static Route[] Routes = new Route[0];
        public static Board[] Boards = new Board[0];
        public static Poi[] Pois = new Poi[0];
        static readonly Dictionary<uint, byte> wayRoute = new Dictionary<uint, byte>();
        static readonly Dictionary<uint, byte[]> gantry = new Dictionary<uint, byte[]>();
        public static Exit[] Exits = new Exit[0];
        static readonly Dictionary<long, int> exitAt = new Dictionary<long, int>();
        // the businesses and boards bucketed by tile, for a tile's queries
        static readonly Dictionary<long, List<int>> poiByTile = new Dictionary<long, List<int>>();

        public const byte Zoned = 1, Frontage = 2;
        public const int KindFuel = 0, KindBurger = 1, KindPizza = 2, KindRestaurant = 3, KindMotel = 4, KindBank = 5, KindPharmacy = 6,
                         KindCarWash = 7, KindCars = 8, KindTires = 9, KindShop = 10, KindGrocery = 11, KindBar = 12;

        public static bool Loaded { get { Ensure(); return grid != null; } }

        static void Ensure()
        {
            if (tried) return;
            if (!CharlotteData.Ready) return;   // not yet downloaded: try again later, never cache the miss
            tried = true;
            var ta = CharlotteData.Load("charlotte_signs");
            if (ta == null) { Debug.LogWarning("[City] charlotte_signs.bytes missing - no billboards or signs (node tools/city/signs.mjs --out Assets/PSXRacing/Resources)"); return; }
            byte[] bytes = ta.bytes;
            Resources.UnloadAsset(ta);
            Load(bytes);
        }

        /// <summary>Install the data from PSGN bytes. False on a bad file.</summary>
        public static bool Load(byte[] bytes)
        {
            tried = true;
            grid = null;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(bytes)))
                {
                    if (r.ReadUInt32() != Magic) { Debug.LogError("[City] charlotte_signs.bytes: bad magic"); return false; }
                    int version = r.ReadInt32();
                    if (version != 1 && version != 2) { Debug.LogError("[City] charlotte_signs.bytes: version " + version + " (this reader knows 1 and 2)"); return false; }
                    int w = r.ReadInt32(), h = r.ReadInt32();
                    float ox = r.ReadSingle(), oz = r.ReadSingle(), c = r.ReadSingle();
                    var g = r.ReadBytes(w * h);
                    if (g.Length != w * h) throw new EndOfStreamException("grid");
                    int nr = r.ReadUInt16();
                    var routes = new Route[nr];
                    for (int i = 0; i < nr; i++)
                    {
                        int len = r.ReadByte();
                        routes[i].name = System.Text.Encoding.ASCII.GetString(r.ReadBytes(len));
                        routes[i].cls = r.ReadByte();
                        routes[i].density = r.ReadSingle();
                        routes[i].rate = r.ReadSingle();
                    }
                    wayRoute.Clear();
                    uint nw = r.ReadUInt32();
                    for (uint i = 0; i < nw; i++) { uint id = r.ReadUInt32(); wayRoute[id] = r.ReadByte(); }
                    uint nb = r.ReadUInt32();
                    var boards = new Board[nb];
                    for (uint i = 0; i < nb; i++)
                    {
                        boards[i].pos = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale;
                        boards[i].yawDeg = r.ReadSingle();
                        boards[i].lit = (r.ReadByte() & 1) != 0;
                    }
                    uint np = r.ReadUInt32();
                    var pois = new Poi[np];
                    for (uint i = 0; i < np; i++)
                    {
                        pois[i].pos = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale;
                        pois[i].kind = r.ReadByte();
                    }
                    gantry.Clear();
                    uint ng = r.ReadUInt32();
                    for (uint i = 0; i < ng; i++) { uint id = r.ReadUInt32(); int n = r.ReadByte(); gantry[id] = r.ReadBytes(n); }
                    var exits = new Exit[0];
                    if (version >= 2)
                    {
                        uint ne = r.ReadUInt32();
                        exits = new Exit[ne];
                        for (uint i = 0; i < ne; i++)
                        {
                            exits[i].pos = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale;
                            exits[i].flags = r.ReadByte();
                            exits[i].advisory = r.ReadByte();
                            exits[i].number = Str(r); exits[i].route = Str(r); exits[i].dest1 = Str(r); exits[i].dest2 = Str(r);
                            exits[i].cardinal = Str(r); exits[i].thru = Str(r); exits[i].thruCity = Str(r);
                        }
                    }
                    if (r.BaseStream.Position != bytes.Length) throw new InvalidDataException($"{bytes.Length - r.BaseStream.Position} bytes left over");
                    nx = w; nz = h; x0 = ox; z0 = oz; cell = c; grid = g;
                    Routes = routes; Boards = boards; Pois = pois; Exits = exits;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[City] charlotte_signs.bytes: " + ex.Message);
                grid = null;
                return false;
            }
            exitAt.Clear();
            for (int i = 0; i < Exits.Length; i++) exitAt[PosKey(Exits[i].pos)] = i;
            poiByTile.Clear();
            for (int i = 0; i < Pois.Length; i++)
            {
                long k = TileKey(Pois[i].pos);
                if (!poiByTile.TryGetValue(k, out var l)) poiByTile[k] = l = new List<int>();
                l.Add(i);
            }
            return true;
        }

        /// <summary>A latin-1 string, its length first (no Encoding: the
        /// WebGL player strips the code pages).</summary>
        static string Str(BinaryReader r)
        {
            int n = r.ReadByte();
            if (n == 0) return "";
            var b = r.ReadBytes(n);
            var c = new char[n];
            for (int i = 0; i < n; i++) c[i] = (char)b[i];
            return new string(c);
        }

        static long PosKey(Vector2 p) => ((long)Mathf.RoundToInt(p.x * 4f) << 32) ^ (uint)Mathf.RoundToInt(p.y * 4f);

        /// <summary>The legends of the exit whose ramp leaves the motorway at
        /// this node (the graph's own node position), if the data has them.</summary>
        public static bool ExitAt(Vector2 node, out Exit ex)
        {
            Ensure();
            if (exitAt.TryGetValue(PosKey(node), out int i)) { ex = Exits[i]; return true; }
            // a node a hair off (a re-export): the nearest within a metre
            for (int k = 0; k < Exits.Length; k++)
                if ((Exits[k].pos - node).sqrMagnitude < 1f) { ex = Exits[k]; return true; }
            ex = default;
            return false;
        }

        static long TileKey(Vector2 p)
        {
            int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
            return ((long)tx << 24) ^ (tz & 0xFFFFFF);
        }

        /// <summary>The grid's bits at a world point (the 60 m cell it is in).</summary>
        public static byte At(Vector2 p)
        {
            Ensure();
            if (grid == null) return 0;
            float x = p.x / CityMap.LayoutScale, z = p.y / CityMap.LayoutScale;
            int ix = Mathf.RoundToInt((x - x0) / cell), iz = Mathf.RoundToInt((z - z0) / cell);
            if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return 0;
            return grid[iz * nx + ix];
        }

        /// <summary>The route an OSM way carries (an index into
        /// <see cref="Routes"/>), or -1.</summary>
        public static int RouteOf(uint wayId)
        {
            Ensure();
            return wayRoute.TryGetValue(wayId, out byte r) ? r : -1;
        }

        /// <summary>The lanes of each destination group on the motorway way
        /// that ends at an exit (OSM destination:lanes, left to right), or null.</summary>
        public static byte[] GantryGroups(uint wayId)
        {
            Ensure();
            return gantry.TryGetValue(wayId, out var g) ? g : null;
        }

        /// <summary>The businesses in the tiles round a rectangle.</summary>
        public static void PoisIn(Vector2 lo, Vector2 hi, List<int> outList)
        {
            Ensure();
            int tx0 = Mathf.FloorToInt(lo.x / CityMeshes.TileSize), tx1 = Mathf.FloorToInt(hi.x / CityMeshes.TileSize);
            int tz0 = Mathf.FloorToInt(lo.y / CityMeshes.TileSize), tz1 = Mathf.FloorToInt(hi.y / CityMeshes.TileSize);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                    if (poiByTile.TryGetValue(((long)tx << 24) ^ (tz & 0xFFFFFF), out var l))
                        foreach (int i in l)
                        {
                            var p = Pois[i].pos;
                            if (p.x >= lo.x && p.y >= lo.y && p.x <= hi.x && p.y <= hi.y) outList.Add(i);
                        }
        }
    }
}
