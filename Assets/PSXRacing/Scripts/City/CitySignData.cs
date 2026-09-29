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
    /// PSGN v1 (little-endian):
    ///   u32 'PSGN' | i32 1 | i32 nx, nz | f32 x0, z0, cell | nx*nz u8 grid
    ///   u16 nRoutes { u8 len, name | u8 cls | f32 perRouteKm | f32 perZonedCarriagewayKm }
    ///   u32 nWays { u32 wayId | u8 route }
    ///   u32 nBill { f32 x, z | f32 yawDeg (NaN: a node) | u8 flags (1 lit) }
    ///   u32 nPoi { f32 x, z | u8 kind }
    ///   u32 nGant { u32 wayId | u8 n | n x u8 lanes }
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

        public static Route[] Routes = new Route[0];
        public static Board[] Boards = new Board[0];
        public static Poi[] Pois = new Poi[0];
        static readonly Dictionary<uint, byte> wayRoute = new Dictionary<uint, byte>();
        static readonly Dictionary<uint, byte[]> gantry = new Dictionary<uint, byte[]>();
        // the businesses and boards bucketed by tile, for a tile's queries
        static readonly Dictionary<long, List<int>> poiByTile = new Dictionary<long, List<int>>();

        public const byte Zoned = 1, Frontage = 2;
        public const int KindFuel = 0, KindBurger = 1, KindPizza = 2, KindRestaurant = 3, KindMotel = 4, KindBank = 5, KindPharmacy = 6,
                         KindCarWash = 7, KindCars = 8, KindTires = 9, KindShop = 10, KindGrocery = 11, KindBar = 12;

        public static bool Loaded { get { Ensure(); return grid != null; } }

        static void Ensure()
        {
            if (tried) return;
            tried = true;
            var ta = Resources.Load<TextAsset>("charlotte_signs");
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
                    if (r.ReadInt32() != 1) { Debug.LogError("[City] charlotte_signs.bytes: not version 1"); return false; }
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
                    if (r.BaseStream.Position != bytes.Length) throw new InvalidDataException($"{bytes.Length - r.BaseStream.Position} bytes left over");
                    nx = w; nz = h; x0 = ox; z0 = oz; cell = c; grid = g;
                    Routes = routes; Boards = boards; Pois = pois;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[City] charlotte_signs.bytes: " + ex.Message);
                grid = null;
                return false;
            }
            poiByTile.Clear();
            for (int i = 0; i < Pois.Length; i++)
            {
                long k = TileKey(Pois[i].pos);
                if (!poiByTile.TryGetValue(k, out var l)) poiByTile[k] = l = new List<int>();
                l.Add(i);
            }
            return true;
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
