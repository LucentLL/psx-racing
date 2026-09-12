using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Streams the Charlotte tiles around the player — and, in a race, around
    /// the AI field too.
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
    /// resort so the ground can never lose the race. An AI car that pulls
    /// away from the player keeps a 3x3 of its own under it for the same
    /// reason: a rigidbody over an unbuilt tile falls through the world.
    /// </summary>
    public class CityWorld : MonoBehaviour
    {
        public const int RoadLayer = 8;
        public const int SolidLayer = 9;

        [Tooltip("One material per CityMeshes.Slot, in enum order.")]
        public Material[] materials;
        public Transform player;
        /// <summary>Other cars the world must exist under: the AI field in a
        /// city race. Each keeps a 3x3 ring of tiles built around it.</summary>
        public List<Transform> anchors = new List<Transform>();

        /// <summary>Tiles kept in each direction around the player's tile.</summary>
        public int ring = 2;
        const int AnchorRing = 1;

        public CityMap Map { get; private set; }

        Dictionary<long, List<CityBuildings.B>> buildings;
        CityMeshes.Trims nodeTrims;

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
        static CityMeshes.Trims cachedTrims;

        /// <summary>The tile counts the last build produced, for the HUD's
        /// debug line and the preview's log.</summary>
        public int LiveTiles => live.Count;

        void Awake() => EnsureInit();

        bool inited;
        /// <summary>Load the map and the placement tables. Idempotent, and
        /// called from every entry point rather than trusted to Awake: a
        /// city race's CityMode.Awake asks this world for the tiles under
        /// the grid, and Awake order between two components on one object
        /// is not something to build a race start on.</summary>
        void EnsureInit()
        {
            if (inited) return;
            inited = true;
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
            BuildFoodIndex();
        }

        /// <summary>Every restaurant in the city, flattened out of the tile
        /// buckets once, so the HUD has something to point at.</summary>
        readonly List<(byte kind, Vector2 pos)> food = new List<(byte, Vector2)>();

        void BuildFoodIndex()
        {
            food.Clear();
            if (buildings == null) return;
            foreach (var kv in buildings)
                foreach (var b in kv.Value)
                    if (CityProps.IsFood(b.kind)) food.Add((b.kind, b.pos));
        }

        /// <summary>The nearest place to eat, for the free-roam HUD cue.</summary>
        public bool NearestFood(Vector3 from, out string label, out Vector2 at, out float dist)
        {
            label = ""; at = Vector2.zero; dist = 0f;
            if (food.Count == 0) return false;
            var p = new Vector2(from.x, from.z);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < food.Count; i++)
            {
                float d = (food[i].pos - p).sqrMagnitude;
                if (d < best) { best = d; bi = i; }
            }
            if (bi < 0) return false;
            at = food[bi].pos;
            dist = Mathf.Sqrt(best);
            label = CityProps.FoodName(food[bi].kind);
            return true;
        }

        void Start()
        {
            // the spawn ring exists before the first physics step
            if (player != null) EnsureRing(player.position, 1);
            foreach (var a in anchors) if (a != null) EnsureRing(a.position, 1);
        }

        /// <summary>Build every tile within <paramref name="r"/> of the tile
        /// under a point, synchronously. The grid of a city race calls this
        /// before the countdown so nobody starts over thin air.</summary>
        public void EnsureRing(Vector3 at, int r)
        {
            int tx = Mathf.FloorToInt(at.x / CityMeshes.TileSize);
            int tz = Mathf.FloorToInt(at.z / CityMeshes.TileSize);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    EnsureTile(tx + dx, tz + dz);
        }

        void Update()
        {
            if (player == null || Map == null) return;
            var p = player.position;
            int ptx = Mathf.FloorToInt(p.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);

            // the ground under the car is not allowed to be missing — nor
            // under any car the race is timing
            EnsureTile(ptx, ptz);
            foreach (var a in anchors)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                EnsureTile(Mathf.FloorToInt(a.position.x / CityMeshes.TileSize),
                           Mathf.FloorToInt(a.position.z / CityMeshes.TileSize));
            }

            // drop tiles outside every ring (+1 hysteresis so the boundary
            // does not thrash while driving along it)
            toDrop.Clear();
            foreach (var kv in live)
            {
                int tx = (int)(kv.Key >> 24);
                int tz = (int)((kv.Key << 40) >> 40);
                if (Mathf.Abs(tx - ptx) <= ring + 1 && Mathf.Abs(tz - ptz) <= ring + 1) continue;
                bool held = false;
                foreach (var a in anchors)
                {
                    if (a == null || !a.gameObject.activeInHierarchy) continue;
                    int ax = Mathf.FloorToInt(a.position.x / CityMeshes.TileSize);
                    int az = Mathf.FloorToInt(a.position.z / CityMeshes.TileSize);
                    if (Mathf.Abs(tx - ax) <= AnchorRing + 1 && Mathf.Abs(tz - az) <= AnchorRing + 1) { held = true; break; }
                }
                if (!held) toDrop.Add(kv.Key);
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
            foreach (var a in anchors)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                int ax = Mathf.FloorToInt(a.position.x / CityMeshes.TileSize);
                int az = Mathf.FloorToInt(a.position.z / CityMeshes.TileSize);
                for (int dz = -AnchorRing; dz <= AnchorRing; dz++)
                    for (int dx = -AnchorRing; dx <= AnchorRing; dx++)
                    {
                        int tx = ax + dx, tz = az + dz;
                        if (live.ContainsKey(Key(tx, tz))) continue;
                        float cx = (tx + 0.5f) * CityMeshes.TileSize - a.position.x;
                        float cz = (tz + 0.5f) * CityMeshes.TileSize - a.position.z;
                        wanted.Add((tx, tz, cx * cx + cz * cz));
                    }
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

        public void EnsureTile(int tx, int tz)
        {
            EnsureInit();
            if (Map == null) return;
            long key = Key(tx, tz);
            if (live.ContainsKey(key)) return;

            var tm = CityMeshes.Build(Map, nodeTrims, buildings, tx, tz);
            var root = new GameObject($"Tile_{tx}_{tz}");
            root.transform.SetParent(transform, false);
            root.transform.position = tm.origin;

            var meshes = Attach(root, tm, MatFor);

            // Real models on this tile — houses, trailers, restaurants, the
            // pack towers on their real lots. They parent under the tile root
            // so streaming drops them with it, and they seat on the same
            // GroundY the meshes were built from.
            if (buildings != null && buildings.TryGetValue(key, out var lots))
            {
                foreach (var b in lots)
                {
                    if (b.kind == 0) continue;
                    var prefab = CityProps.Prefab(b.kind);
                    if (prefab == null) continue;
                    var def = CityProps.Defs[b.kind];
                    float gy = CityBuildings.SeatY(Map, b.pos, b.w, b.d, b.yaw);
                    var go = Instantiate(prefab, root.transform);
                    go.transform.position = new Vector3(b.pos.x, gy - def.sink, b.pos.y);
                    go.transform.rotation = Quaternion.Euler(
                        0f, b.yaw * Mathf.Rad2Deg + def.yawOffsetDeg, 0f);
                    if (b.scale.sqrMagnitude > 0.01f) go.transform.localScale = b.scale;
                }
            }

            live[key] = new Tile { go = root, meshes = meshes };
        }

        /// <summary>
        /// Stand a built tile up under a root: renderers AND colliders, the
        /// one definition of which mesh sits on which layer. The audit and
        /// the preview go through here too, so what they photograph and
        /// ray-cast is what the player drives on.
        /// </summary>
        public static Mesh[] Attach(GameObject root, CityMeshes.TileMeshes tm,
                                    System.Func<CityMeshes.Slot, Material> matFor)
        {
            var meshes = new List<Mesh>(5);
            if (tm.ground != null)
            {
                var g = Child(root, "Ground", 0);
                Render(g, tm.ground, tm.groundSlots, matFor);
                g.AddComponent<MeshCollider>().sharedMesh = tm.ground;
                meshes.Add(tm.ground);
            }
            if (tm.roads != null)
            {
                var g = Child(root, "Roads", RoadLayer);
                Render(g, tm.roads, tm.roadSlots, matFor);
                g.AddComponent<MeshCollider>().sharedMesh = tm.roads;
                meshes.Add(tm.roads);
            }
            if (tm.barriers != null)
            {
                // Solid, like a pier: CollisionAudio and the stuck watchdog
                // treat the layer as a wall, which is what a Jersey barrier is.
                var g = Child(root, "Barriers", SolidLayer);
                Render(g, tm.barriers, new[] { CityMeshes.Slot.Concrete }, matFor);
                g.AddComponent<MeshCollider>().sharedMesh = tm.barriers;
                meshes.Add(tm.barriers);
            }
            if (tm.water != null)
            {
                var g = Child(root, "Water", 0);
                Render(g, tm.water, new[] { CityMeshes.Slot.Water }, matFor);
                meshes.Add(tm.water);
            }
            if (tm.buildings != null)
            {
                // The buildings collide as the walls you see. They were
                // oriented boxes, and an L-shaped block's box reached into the
                // street — an invisible wall across a lane, on the Solid layer.
                var g = Child(root, "Buildings", SolidLayer);
                Render(g, tm.buildings, tm.buildingSlots, matFor);
                g.AddComponent<MeshCollider>().sharedMesh = tm.buildings;
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
            return meshes.ToArray();
        }

        static GameObject Child(GameObject parent, string name, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.layer = layer;
            return go;
        }

        static void Render(GameObject go, Mesh mesh, CityMeshes.Slot[] slots,
                           System.Func<CityMeshes.Slot, Material> matFor)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++)
                mats[i] = matFor != null ? matFor(slots[i]) : null;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        Material MatFor(CityMeshes.Slot slot)
        {
            int i = (int)slot;
            if (materials != null && i < materials.Length && materials[i] != null)
                return SeasonDress.Substitute(materials[i]);
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
