using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Streams the Charlotte tiles around the player.
    ///
    /// This is the project's first runtime-generated world: circuits are baked
    /// whole into their scenes, but a 31 km city cannot be, so the Charlotte
    /// scene ships nearly empty and this component conjures the ~25 tiles
    /// (256 m each) around the car as it moves. The camera's hard 360 m far
    /// plane and the fog that closes before it are what make this cheap: a
    /// 5x5 ring is always more world than the player can see.
    ///
    /// Budget: at most one tile build per frame — a car crossing a tile row at
    /// 280 km/h leaves ~3 s to build 5 tiles, and the budget builds 60 in that
    /// time. The tile under the car is force-built synchronously as a last
    /// resort so the ground can never lose the race.
    /// </summary>
    public class CityWorld : MonoBehaviour
    {
        public const int RoadLayer = 8;
        public const int SolidLayer = 9;

        [Tooltip("One material per CityMeshes.Slot, in enum order.")]
        public Material[] materials;
        public Transform player;

        /// <summary>Tiles kept in each direction around the player's tile.</summary>
        public int ring = 2;

        public CityMap Map { get; private set; }

        Dictionary<long, List<CityBuildings.B>> buildings;
        float[] nodeTrims;

        class Tile
        {
            public GameObject go;
            public Mesh[] meshes;
        }

        readonly Dictionary<long, Tile> live = new Dictionary<long, Tile>();
        readonly List<long> toDrop = new List<long>();
        readonly List<(int tx, int tz, float d2)> wanted = new List<(int, int, float)>();

        static long Key(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        // Derived once per process, like the map itself: leaving for the menu
        // and driving back out should not re-place 40,000 buildings.
        static CityMap cachedFor;
        static Dictionary<long, List<CityBuildings.B>> cachedBuildings;
        static float[] cachedTrims;

        void Awake()
        {
            Map = CityMap.Get();
            if (Map == null) { enabled = false; return; }
            if (cachedFor != Map)
            {
                cachedFor = Map;
                cachedTrims = CityMeshes.NodeTrims(Map);
                cachedBuildings = CityBuildings.Precompute(Map);
            }
            nodeTrims = cachedTrims;
            buildings = cachedBuildings;
        }

        void Start()
        {
            // the spawn ring exists before the first physics step
            if (player != null)
            {
                int tx = Mathf.FloorToInt(player.position.x / CityMeshes.TileSize);
                int tz = Mathf.FloorToInt(player.position.z / CityMeshes.TileSize);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        EnsureTile(tx + dx, tz + dz);
            }
        }

        void Update()
        {
            if (player == null || Map == null) return;
            var p = player.position;
            int ptx = Mathf.FloorToInt(p.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);

            // the ground under the car is not allowed to be missing
            EnsureTile(ptx, ptz);

            // drop tiles outside the ring (+1 hysteresis so the boundary
            // does not thrash while driving along it)
            toDrop.Clear();
            foreach (var kv in live)
            {
                int tx = (int)(kv.Key >> 24);
                int tz = (int)((kv.Key << 40) >> 40);
                if (Mathf.Abs(tx - ptx) > ring + 1 || Mathf.Abs(tz - ptz) > ring + 1)
                    toDrop.Add(kv.Key);
            }
            foreach (var k in toDrop) DropTile(k);

            // build the nearest missing tile, one per frame
            wanted.Clear();
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int tx = ptx + dx, tz = ptz + dz;
                    if (live.ContainsKey(Key(tx, tz))) continue;
                    float cx = (tx + 0.5f) * CityMeshes.TileSize - p.x;
                    float cz = (tz + 0.5f) * CityMeshes.TileSize - p.z;
                    wanted.Add((tx, tz, cx * cx + cz * cz));
                }
            if (wanted.Count > 0)
            {
                wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
                EnsureTile(wanted[0].tx, wanted[0].tz);
            }
        }

        void OnDestroy()
        {
            foreach (var k in new List<long>(live.Keys)) DropTile(k);
        }

        void DropTile(long key)
        {
            if (!live.TryGetValue(key, out var t)) return;
            live.Remove(key);
            if (t.go != null) Destroy(t.go);
            // runtime meshes are not garbage-collected with their GameObjects
            foreach (var m in t.meshes) if (m != null) Destroy(m);
        }

        void EnsureTile(int tx, int tz)
        {
            long key = Key(tx, tz);
            if (live.ContainsKey(key)) return;

            var tm = CityMeshes.Build(Map, nodeTrims, buildings, tx, tz);
            var root = new GameObject($"Tile_{tx}_{tz}");
            root.transform.SetParent(transform, false);
            root.transform.position = tm.origin;

            var meshes = new List<Mesh>(4);

            if (tm.ground != null)
            {
                var g = Child(root, "Ground", 0);
                Render(g, tm.ground, new[] { CityMeshes.Slot.Ground });
                g.AddComponent<MeshCollider>().sharedMesh = tm.ground;
                meshes.Add(tm.ground);
            }
            if (tm.roads != null)
            {
                var g = Child(root, "Roads", RoadLayer);
                Render(g, tm.roads, tm.roadSlots);
                g.AddComponent<MeshCollider>().sharedMesh = tm.roads;
                meshes.Add(tm.roads);
            }
            if (tm.water != null)
            {
                var g = Child(root, "Water", 0);
                Render(g, tm.water, new[] { CityMeshes.Slot.Water });
                meshes.Add(tm.water);
            }
            if (tm.buildings != null)
            {
                var g = Child(root, "Buildings", 0);
                Render(g, tm.buildings, tm.buildingSlots);
                meshes.Add(tm.buildings);
            }
            foreach (var box in tm.solids)
            {
                var s = new GameObject("Solid");
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = box.center;
                s.transform.localRotation = Quaternion.Euler(0f, box.yawDeg, 0f);
                s.layer = SolidLayer;
                var bc = s.AddComponent<BoxCollider>();
                bc.size = box.size;
            }

            live[key] = new Tile { go = root, meshes = meshes.ToArray() };
        }

        static GameObject Child(GameObject parent, string name, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.layer = layer;
            return go;
        }

        void Render(GameObject go, Mesh mesh, CityMeshes.Slot[] slots)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++)
                mats[i] = MatFor(slots[i]);
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        Material MatFor(CityMeshes.Slot slot)
        {
            int i = (int)slot;
            if (materials != null && i < materials.Length && materials[i] != null) return materials[i];
            return null;
        }

        /// <summary>The named street nearest a world position, for the HUD.</summary>
        public string StreetNameAt(Vector3 pos)
        {
            if (Map == null) return "";
            if (!Map.NearestRoadPoint(new Vector2(pos.x, pos.z), 60f, skipLinks: false,
                out int ei, out _, out _)) return "";
            var e = Map.edges[ei];
            if (!string.IsNullOrEmpty(e.name)) return e.name;
            return e.link ? "RAMP" : "";
        }
    }
}
