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
    /// (256 m each) around the car as it moves. The camera's hard 500 m far
    /// plane and the fog that closes before it are what make this cheap: a
    /// 5x5 ring is always more world than the player can see. That ring is
    /// also what caps the far plane — two tiles is as little as 512 m of built
    /// world in the worst case, so the fog has to be at full strength by 500 m
    /// or the edge of it would show (see CircuitFarClip in the builder).
    ///
    /// Budget: a few milliseconds of tile building a frame (WP-09, 2026-10-01:
    /// a whole Uptown tile in one frame was a 50-350 ms freeze, five in a row at
    /// every tile line - "it bogs down about every two blocks"). The nearest
    /// missing tile is built as a CityMeshes.TileJob, SliceBudgetMs a frame
    /// (UrgentSliceBudgetMs while a tile next to a car is missing); a car
    /// crossing a tile row at 280 km/h still leaves ~3 s, and the five tiles
    /// take about two in the editor. The tile under the car is force-built
    /// synchronously as a last resort so the ground can never lose the race. An AI car that pulls
    /// away from the player keeps a 3x3 of its own under it for the same
    /// reason: a rigidbody over an unbuilt tile falls through the world.
    /// </summary>
    public class CityWorld : MonoBehaviour
    {
        public const int RoadLayer = 8;
        public const int SolidLayer = 9;
        /// <summary>The name of the Solid-layer object holding a tile's lamp
        /// post colliders, which is every such collider's name: the audits
        /// skip it by name, since a pole standing beside a road is neither a
        /// barrier guarding a drop nor something in a lane.</summary>
        public const string LampPostName = "LampPost";
        /// <summary>Galvanised steel gone dark with weather: the posts' tint
        /// when the build has no city kit (the fallback, never the look).</summary>
        static readonly Color LampPostColor = new Color(0.30f, 0.31f, 0.33f);

        /// <summary>
        /// One material per CityMeshes.Slot, in enum order - for a TOOL that
        /// wants to hand in its own table. Left null, which is what the game
        /// does, the world draws with the <see cref="CityKit"/> (WP-07): the
        /// scenes no longer serialize a materials array, so a new city
        /// material is a kit change and never a rebake of four scenes.
        /// </summary>
        [System.NonSerialized] public Material[] materials;
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
            public int colliders;
        }

        // ---- the tile-build clock (WP-01 instruments) ----------------------
        //
        // What one EnsureTile cost, split the way the budget asks: the mesh
        // build (CityMeshes.Build), standing it up (Attach: renderers and
        // colliders; the MeshCollider cook is timed on its own inside that),
        // and the prop models. The FpsOverlay CITY line shows the worst total
        // of the last ten seconds, which on a phone is the hitch the owner
        // feels (plan: WP-09's trigger is that number over 16.7 ms there);
        // CityBudgetProbe reads the same numbers in the editor.

        /// <summary>One tile build's cost, milliseconds.</summary>
        public struct TileTiming
        {
            public int tx, tz;
            public float buildMs, attachMs, cookMs, propsMs, totalMs;
            /// <summary>The trees (WP-08): the occupancy mask, planting and
            /// the mesh (<see cref="CityTrees.Build"/>), then standing them up.
            /// Set on a TREE FRAME only: a tile's trees are planted on a frame
            /// of their own after its build (see <see cref="PlantTrees"/>).</summary>
            public float treesMs, treePlantMs;
            /// <summary>The signs' share of a tree frame (WP-23: placed on the
            /// same mask, before the trees), and how many.</summary>
            public float signsMs;
            public int colliders, props, trees, signs;
            /// <summary>This is a tile's trees being planted, not its build.</summary>
            public bool treeFrame;
            /// <summary>WP-09: the frames a sliced build took (0 = built in
            /// one go), its longest frame's share, and standing it up
            /// (renderers, colliders, props, lamps - a frame of its own).</summary>
            public int slices;
            public float maxSliceMs, standUpMs;
            /// <summary>What the worst FRAME of this build cost: the whole
            /// build when it ran in one go, else its longest slice or its
            /// stand-up. The FPS overlay's CITY line shows the worst of these.</summary>
            public float FrameMs => slices > 0 ? Mathf.Max(maxSliceMs, standUpMs) : totalMs;
        }

        /// <summary>The most recent tile build, anywhere.</summary>
        public static TileTiming LastTiming { get; private set; }
        /// <summary>Every tile build, as it finishes (the budget probe listens).</summary>
        public static event System.Action<TileTiming> TileBuilt;

        static readonly List<(float at, float ms)> recentBuilds = new List<(float, float)>();

        /// <summary>The slowest tile build (total ms) in the last
        /// <paramref name="seconds"/> of unscaled time; 0 if none.</summary>
        public static float RecentMaxBuildMs(float seconds)
        {
            float now = Time.realtimeSinceStartup, worst = 0f;
            recentBuilds.RemoveAll(b => now - b.at > seconds);
            foreach (var b in recentBuilds) if (b.ms > worst) worst = b.ms;
            return worst;
        }

        /// <summary>The world streaming now, for the FPS overlay's CITY line
        /// (null outside Charlotte).</summary>
        public static CityWorld Active { get; private set; }
        void OnEnable() => Active = this;
        void OnDisable() { if (Active == this) Active = null; }

        /// <summary>Colliders on the live tiles (MeshColliders and boxes), and
        /// the tree trunks standing round the cars.</summary>
        public int LiveColliders
        {
            get
            {
                int n = Trunks != null ? Trunks.LiveColliders : 0;
                foreach (var t in live.Values) n += t.colliders;
                return n;
            }
        }

        /// <summary>
        /// THE CITY'S TREE TRUNKS (WP-08; the plan's TreeTrunks AddTable /
        /// RemoveTable). Every live tile hands its solid trunks to this one
        /// table when its trees are planted and takes them back when it is
        /// dropped; the table stands capsules only in the 40 m cells round each
        /// car (TreeTrunks), so a tile build stands no trunk collider at all and
        /// the ring holds a few hundred round the cars, not thousands.
        /// </summary>
        public TreeTrunks Trunks { get; private set; }

        /// <summary>Tiles built whose trees are still to plant, with what their
        /// build cached: its meshes (fill houses, lamps) and its lattice.</summary>
        readonly Dictionary<long, (CityMeshes.TileMeshes tm, Dictionary<long, float> lattice)> treesPending =
            new Dictionary<long, (CityMeshes.TileMeshes, Dictionary<long, float>)>();

        /// <summary>Tiles whose trees are still to plant (the probe's check).</summary>
        public int TreesPending => treesPending.Count;

        /// <summary>The live tiles' signs (WP-23), for the tools that look at
        /// them (the sign shots stand their cameras where the drivers are).</summary>
        public IEnumerable<CitySigns.SignTile> LiveSigns => liveSigns.Values;
        readonly Dictionary<long, CitySigns.SignTile> liveSigns = new Dictionary<long, CitySigns.SignTile>();

        /// <summary>The live tiles' poles and wires (WP-15), for the tools.</summary>
        public IEnumerable<CityPoles.PoleTile> LivePoles => livePoles.Values;
        readonly Dictionary<long, CityPoles.PoleTile> livePoles = new Dictionary<long, CityPoles.PoleTile>();

        /// <summary>The live tiles' junction furniture (STOP signs, signals),
        /// for the tools.</summary>
        public IEnumerable<CitySignals.SignalTile> LiveSignals => liveSignals.Values;
        readonly Dictionary<long, CitySignals.SignalTile> liveSignals = new Dictionary<long, CitySignals.SignalTile>();

        /// <summary>The node trims and building lots this world places by
        /// (the same instances), for the tools that ask the roadside mask
        /// about what it stood (the pole shots' spot check).</summary>
        public CityMeshes.Trims NodeTrims => nodeTrims;
        public Dictionary<long, List<CityBuildings.B>> Buildings => buildings;

        static double cookTicks;
        static readonly System.Diagnostics.Stopwatch cookClock = new System.Diagnostics.Stopwatch();

        /// <summary>Assign a MeshCollider's mesh - which is when PhysX cooks
        /// it - on the cook clock.</summary>
        static void CookCollider(GameObject g, Mesh m)
        {
            cookClock.Restart();
            g.AddComponent<MeshCollider>().sharedMesh = m;
            cookClock.Stop();
            cookTicks += cookClock.ElapsedTicks;
        }

        /// <summary>Destroy in play, DestroyImmediate in the editor: the
        /// budget probe builds tiles through this component without play
        /// mode, and Destroy is refused there.</summary>
        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>Drop every live tile (the budget probe, between sites).</summary>
        public void DropAll()
        {
            foreach (var k in new List<long>(live.Keys)) DropTile(k);
        }

        readonly Dictionary<long, Tile> live = new Dictionary<long, Tile>();
        readonly List<long> toDrop = new List<long>();
        readonly List<(int tx, int tz, float d2)> wanted = new List<(int, int, float)>();

        // ---- WP-09: the time-sliced build ----------------------------------
        /// <summary>Build streamed tiles a few milliseconds a frame
        /// (CityMeshes.TileJob) instead of a whole tile in one frame.
        /// PSX_CITY_SLICE=0 in a tool's environment turns it off (the A/B).</summary>
        public static bool SliceBuilds = System.Environment.GetEnvironmentVariable("PSX_CITY_SLICE") != "0";
        /// <summary>A frame's share of tile work, ms, and the share while a
        /// tile next to a car (one tile away) is still missing.</summary>
        public static float SliceBudgetMs = 4f, UrgentSliceBudgetMs = 10f;
        CityMeshes.TileJob job;
        long jobKey;

        static long Key(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        // Derived once per process, like the map itself: leaving for the menu
        // and driving back out should not re-place 40,000 buildings.
        static CityMap cachedFor;
        static Dictionary<long, List<CityBuildings.B>> cachedBuildings;
        static CityMeshes.Trims cachedTrims;
        /// <summary>The trims and building table every tile build reads (set
        /// once a world has initialised) - for tools that call CityMeshes.</summary>
        public static CityMeshes.Trims SharedTrims => cachedTrims;
        public static Dictionary<long, List<CityBuildings.B>> SharedBuildings => cachedBuildings;

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
            // the pole lines (WP-15), once a map, at load rather than on a tree frame
            CityPoles.Warm(Map);
            BuildFoodIndex();
            // the trunk table, on a child of its own that no scene saves
            var tgo = new GameObject("CityTreeTrunks") { hideFlags = HideFlags.DontSave };
            tgo.transform.SetParent(transform, false);
            Trunks = tgo.AddComponent<TreeTrunks>();
            Trunks.standName = CityTrees.TrunkName;
            Trunks.trunkHeight = CityTrees.TrunkHeightM;
        }

        /// <summary>Every restaurant in the city, flattened out of the tile
        /// buckets once, so the HUD has something to point at.</summary>
        readonly List<(byte kind, Vector2 pos)> food = new List<(byte, Vector2)>();

        /// <summary>Every restaurant lot (kind, centre), for the budget probe
        /// and the play check.</summary>
        public IReadOnlyList<(byte kind, Vector2 pos)> FoodLots { get { EnsureInit(); return food; } }

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
        /// under a point, synchronously, trees and all. The grid of a city
        /// race calls this before the countdown so nobody starts over thin air.</summary>
        public void EnsureRing(Vector3 at, int r)
        {
            int tx = Mathf.FloorToInt(at.x / CityMeshes.TileSize);
            int tz = Mathf.FloorToInt(at.z / CityMeshes.TileSize);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                    EnsureTile(tx + dx, tz + dz);
            // whoever asks for a ring is about to put a car (or a camera) in it
            // (the signs decided first, in the slices streaming spends a frame
            // each on, so the budget probe times what play does)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    long k = Key(tx + dx, tz + dz);
                    if (treesPending.ContainsKey(k)) while (PrepareSigns(k) != 0) { }
                    PlantTrees(k);
                }
        }

        void Update()
        {
            // the one clock every signal head in the city runs on
            CitySignals.Tick(Time.timeAsDouble);
            if (player == null || Map == null) return;
            var p = player.position;
            int ptx = Mathf.FloorToInt(p.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);

            // the ground under the car is not allowed to be missing — nor
            // under any car the race is timing (and those plant their trees
            // at once: a car is in them)
            bool built = false;
            if (EnsureTile(ptx, ptz)) { PlantTrees(Key(ptx, ptz)); built = true; }
            foreach (var a in anchors)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                int ax = Mathf.FloorToInt(a.position.x / CityMeshes.TileSize), az = Mathf.FloorToInt(a.position.z / CityMeshes.TileSize);
                if (EnsureTile(ax, az)) { PlantTrees(Key(ax, az)); built = true; }
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

            // build the nearest missing tile (WP-09: a slice a frame)
            wanted.Clear();
            bool hurry = false;
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int tx = ptx + dx, tz = ptz + dz;
                    if (live.ContainsKey(Key(tx, tz))) continue;
                    if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1) hurry = true;
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
                        hurry = true;   // an anchor's ring is the tiles next to it
                        float cx = (tx + 0.5f) * CityMeshes.TileSize - a.position.x;
                        float cz = (tz + 0.5f) * CityMeshes.TileSize - a.position.z;
                        wanted.Add((tx, tz, cx * cx + cz * cz));
                    }
            }
            if (SliceBuilds && (job != null || wanted.Count > 0))
            {
                // A few ms of the nearest missing tile's build every frame
                // until it stands; its stand-up (renderers, colliders, props)
                // takes a frame of its own. The tile is finished even if the
                // car turned away from it: a drop takes it back if need be.
                if (job == null)
                {
                    wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
                    StartJob(wanted[0].tx, wanted[0].tz);
                }
                if (job != null)
                {
                    if (job.Done) CompleteJob();
                    else job.Step(hurry ? UrgentSliceBudgetMs : SliceBudgetMs);
                    built = true;
                }
            }
            else if (wanted.Count > 0)
            {
                wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
                built |= EnsureTile(wanted[0].tx, wanted[0].tz);
            }

            // THE TREES ON A FRAME OF THEIR OWN (WP-08; WP-09's third stage,
            // for the trees): a tile's build and its planting never share a
            // frame. When no tile was built this frame, the nearest tile still
            // waiting plants its trees (a tile is built at least a tile ahead
            // of any car, and a car at 250 km/h is seconds from it). A waiting
            // tile a car is IN plants now regardless: its trunks must stand.
            if (treesPending.Count > 0)
            {
                long best = 0; float bestD = float.MaxValue; bool urgent = false;
                foreach (var k in treesPending.Keys)
                {
                    int tx = (int)(k >> 24), tz = (int)((k << 40) >> 40);
                    bool near = tx == ptx && tz == ptz;
                    foreach (var a in anchors)
                    {
                        if (near || a == null || !a.gameObject.activeInHierarchy) continue;
                        near = tx == Mathf.FloorToInt(a.position.x / CityMeshes.TileSize) &&
                               tz == Mathf.FloorToInt(a.position.z / CityMeshes.TileSize);
                    }
                    float cx = (tx + 0.5f) * CityMeshes.TileSize - p.x, cz = (tz + 0.5f) * CityMeshes.TileSize - p.z;
                    float d2 = cx * cx + cz * cz - (near ? 1e12f : 0f);
                    if (d2 < bestD) { bestD = d2; best = k; urgent = near; }
                }
                // the gantries and billboards that reach it are decided first,
                // a slice a frame (WP-23: they are decided on the masks of the
                // tiles round it, which a tile's own frame should not all pay for)
                if (!built || urgent)
                {
                    // the trees put their own lattice in the builder's cache:
                    // no job may be open across them (WP-09)
                    if (job != null) CompleteJob();
                    if (urgent || PrepareSigns(best) == 0) PlantTrees(best);
                }
            }
        }

        void OnDestroy()
        {
            if (job != null) { job.Abandon(); job = null; }
            foreach (var k in new List<long>(live.Keys)) DropTile(k);
        }

        void DropTile(long key)
        {
            treesPending.Remove(key);
            liveSigns.Remove(key);
            livePoles.Remove(key);
            liveSignals.Remove(key);
            if (Trunks != null) Trunks.RemoveTable(key);
            if (!live.TryGetValue(key, out var t)) return;
            live.Remove(key);
            if (t.go != null) Kill(t.go);
            // runtime meshes are not garbage-collected with their GameObjects
            foreach (var m in t.meshes) if (m != null) Kill(m);
        }

        /// <summary>
        /// Plant a built tile's trees, if they are still to plant (WP-08): the
        /// occupancy mask, the planting and the mesh (CityTrees.Build, on the
        /// lattice its build cached), the cards under the tile's root, and its
        /// solid trunks handed to <see cref="Trunks"/>. Timed as a TREE FRAME
        /// (<see cref="TileTiming.treeFrame"/>), which the FPS overlay's CITY
        /// line counts like a tile build.
        /// </summary>
        public void PlantTrees(long key)
        {
            if (!treesPending.TryGetValue(key, out var job)) return;
            treesPending.Remove(key);
            if (!live.TryGetValue(key, out var tile) || tile.go == null) return;
            int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CityMeshes.PutLattice(job.lattice);
            var tt = CityTrees.Build(Map, nodeTrims, buildings, job.tm, tx, tz);
            if (tt.mesh != null)
            {
                AttachTrees(tile.go, tt, CityTrees.MaterialFor(CityTrees.DressNow()));
                System.Array.Resize(ref tile.meshes, tile.meshes.Length + 1);
                tile.meshes[tile.meshes.Length - 1] = tt.mesh;
            }
            if (Trunks != null && tt.solids > 0) Trunks.AddTable(key, tt.Trunks());
            // THE POLES AND WIRES (WP-15): the tile's furniture mesh (its lamp
            // posts drawn with them), the poles solid
            var pt = tt.poles;
            if (pt != null) livePoles[key] = pt;
            if (pt != null && (pt.mesh != null || pt.poles.Count > 0))
            {
                AttachFurniture(tile.go, pt, CityPoles.Material());
                if (pt.mesh != null)
                {
                    System.Array.Resize(ref tile.meshes, tile.meshes.Length + 1);
                    tile.meshes[tile.meshes.Length - 1] = pt.mesh;
                }
                tile.colliders += pt.poles.Count;
            }
            // THE JUNCTION FURNITURE: STOP signs and stop bars, traffic
            // signals on their poles, mast arms and span wires - one static
            // mesh, one mesh of lenses on the shared signal material, one of
            // halos lit after dark (CitySignals)
            var jt = tt.signals;
            if (jt != null) liveSignals[key] = jt;
            if (jt != null && jt.mesh != null)
            {
                foreach (var m in CitySignals.Attach(tile.go, jt))
                {
                    System.Array.Resize(ref tile.meshes, tile.meshes.Length + 1);
                    tile.meshes[tile.meshes.Length - 1] = m;
                }
                tile.colliders += jt.poles.Count;
            }
            // THE SIGNS (WP-23), placed on the mask before the trees
            var st = tt.signs;
            if (st != null) liveSigns[key] = st;
            if (st != null && st.mesh != null)
            {
                AttachSigns(tile.go, st, CitySigns.Material());
                System.Array.Resize(ref tile.meshes, tile.meshes.Length + 1);
                tile.meshes[tile.meshes.Length - 1] = st.mesh;
                tile.colliders += st.posts.Count;
            }
            // the billboards' floodlights and the poles' cobra-heads join the
            // tile's street lamps: one NightGlow, one halo draw a tile
            int signLamps = st != null && st.mesh != null ? st.lamps.Count : 0;
            int poleHeads = pt != null ? pt.heads.Count : 0;
            if (signLamps + poleHeads > 0)
            {
                var heads = new List<Vector3>(job.tm.lamps.Count + signLamps + poleHeads);
                var gains = new List<float>(heads.Capacity);
                foreach (var l in job.tm.lamps) { heads.Add(job.tm.origin + l.head); gains.Add(LampGain(l)); }
                if (signLamps > 0) heads.AddRange(st.lamps);
                if (poleHeads > 0) heads.AddRange(pt.heads);
                var lt = tile.go.transform.Find("LampLights");
                GameObject lights = lt != null ? lt.gameObject : null;
                if (lights == null)
                {
                    lights = new GameObject("LampLights");
                    lights.transform.SetParent(tile.go.transform, false);
                }
                var glow = lights.GetComponent<NightGlow>();
                if (glow == null) glow = lights.AddComponent<NightGlow>();
                glow.Init(heads, gains);
            }
            var timing = new TileTiming
            {
                tx = tx, tz = tz, treeFrame = true,
                treesMs = (float)clock.Elapsed.TotalMilliseconds, treePlantMs = tt.ms,
                totalMs = (float)clock.Elapsed.TotalMilliseconds, trees = tt.trees.Count,
                signsMs = st != null ? st.ms : 0f, signs = st != null ? st.signs.Count : 0,
            };
            recentBuilds.Add((Time.realtimeSinceStartup, timing.totalMs));
            if (recentBuilds.Count > 256) recentBuilds.RemoveAt(0);
            TileBuilt?.Invoke(timing);
        }

        /// <summary>A frame's slice of the signs a waiting tile needs decided
        /// (<see cref="CitySigns.Prepare"/>), then of its poles
        /// (<see cref="CityPoles.Prepare"/>), timed as a tree frame: 0 when
        /// there was nothing to do (the tile may plant this frame), 1 when this
        /// slice finished them, 2 when there is more.</summary>
        public int PrepareSigns(long key)
        {
            if (Map == null) return 0;
            int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int r = CitySigns.Enabled ? CitySigns.Prepare(Map, nodeTrims, buildings, tx, tz, SignSliceMs) : 0;
            // then the tile's poles and wires (WP-15), a slice a frame the same
            // way, so its tree frame only draws them
            if (r == 0 && CityPoles.Enabled) r = CityPoles.Prepare(Map, nodeTrims, buildings, tx, tz, SignSliceMs);
            if (r == 0) return 0;
            float ms = (float)clock.Elapsed.TotalMilliseconds;
            var timing = new TileTiming { tx = tx, tz = tz, treeFrame = true, treesMs = ms, totalMs = ms, signsMs = ms };
            recentBuilds.Add((Time.realtimeSinceStartup, ms));
            if (recentBuilds.Count > 256) recentBuilds.RemoveAt(0);
            TileBuilt?.Invoke(timing);
            return r;
        }
        /// <summary>How long a frame's slice of sign decisions may start new
        /// ones for (one already begun runs to its end).</summary>
        public const float SignSliceMs = 3f;

        /// <summary>Prop lots left empty because the drawn ground falls away
        /// under them further than the foundation skirt reaches
        /// (CityProps.MaxFallM), since the process started.</summary>
        public static int PropsDropped;

        /// <summary>Build a tile if it is not live. True when it was built
        /// (its trees then wait for <see cref="PlantTrees"/>).</summary>
        public bool EnsureTile(int tx, int tz)
        {
            EnsureInit();
            if (Map == null) return false;
            long key = Key(tx, tz);
            if (live.ContainsKey(key)) return false;
            // A tile still being built a slice a frame is finished first: the
            // builder's scratch belongs to one build at a time (WP-09).
            if (job != null)
            {
                bool same = jobKey == key;
                CompleteJob();
                if (same) return true;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tm = CityMeshes.Build(Map, nodeTrims, buildings, tx, tz);
            StandUp(key, tx, tz, tm, clock.Elapsed.TotalMilliseconds, 0, 0f);
            return true;
        }

        /// <summary>Start building a tile a slice a frame (WP-09).</summary>
        void StartJob(int tx, int tz)
        {
            EnsureInit();
            if (Map == null) return;
            jobKey = Key(tx, tz);
            job = CityMeshes.Begin(Map, nodeTrims, buildings, tx, tz);
        }

        /// <summary>Finish the open job now if it is not done, and stand its
        /// tile up.</summary>
        void CompleteJob()
        {
            var j = job;
            long k = jobKey;
            job = null;
            if (j == null) return;
            j.Finish();
            if (live.ContainsKey(k)) return;
            StandUp(k, j.tx, j.tz, j.Result, j.WorkMs, j.Slices, (float)j.MaxSliceMs);
        }

        /// <summary>A built tile's meshes stood up under a root: renderers and
        /// colliders, the prop models, its trees queued, its lamps lit.
        /// <paramref name="buildMs"/> is the build's own work.</summary>
        void StandUp(long key, int tx, int tz, CityMeshes.TileMeshes tm, double buildMs, int slices, float maxSliceMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double tBuild = 0.0;
            var root = new GameObject($"Tile_{tx}_{tz}");
            root.transform.SetParent(transform, false);
            root.transform.position = tm.origin;

            cookTicks = 0;
            var meshes = Attach(root, tm, MatFor);
            double tAttach = clock.Elapsed.TotalMilliseconds;
            float cookMs = (float)(cookTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            int props = 0;

            // Real models on this tile — houses, trailers, restaurants, the
            // pack towers on their real lots. They parent under the tile root
            // so streaming drops them with it, and they seat on the same
            // GroundY the meshes were built from.
            if (buildings != null && buildings.TryGetValue(key, out var lots))
            {
                foreach (var b in lots)
                {
                    if (b.kind == 0) continue;
                    var prefab = CityProps.CityPrefab(b.kind);
                    if (prefab == null) continue;
                    var def = CityProps.Defs[b.kind];
                    // seated on the ground this build DREW (its lattice is
                    // still cached), and left out where the ground falls away
                    // further than its foundation skirt reaches
                    float gy = CityBuildings.SeatY(Map, b.pos, b.w, b.d, b.yaw, out float low);
                    if (gy - low > CityProps.MaxFallM(def)) { PropsDropped++; continue; }
                    var go = Instantiate(prefab, root.transform);
                    go.transform.position = new Vector3(b.pos.x, gy - def.sink, b.pos.y);
                    go.transform.rotation = Quaternion.Euler(
                        0f, b.yaw * Mathf.Rad2Deg + def.yawOffsetDeg, 0f);
                    if (b.scale.sqrMagnitude > 0.01f) go.transform.localScale = b.scale;
                    props++;
                }
            }
            double tProps = clock.Elapsed.TotalMilliseconds;

            // THE TREES (WP-08) come after everything they must keep clear of
            // (the tile's roads, fill houses and lamps are in tm, the lots in
            // the building table) and on a later frame: queued here with the
            // lattice this build cached, planted by PlantTrees.
            if (CityTrees.Enabled || CitySigns.Enabled || CityPoles.Enabled || tm.lamps.Count > 0) treesPending[key] = (tm, CityMeshes.TakeLattice());
            double tTrees = clock.Elapsed.TotalMilliseconds;

            // THE LIGHT the street lamps throw (the posts are Attach's). A
            // NightGlow of its own per tile, under its own child, so the posts
            // are not among what it switches, and Init'd by hand: it is added
            // to an empty object, and in edit mode (the night-look shots stand
            // tiles up without play) AddComponent runs no Awake at all. It
            // registers every head with StreetLights and builds the halos;
            // dropping the tile destroys it, which takes both back.
            if (tm.lamps.Count > 0)
            {
                var heads = new Vector3[tm.lamps.Count];
                var gains = new float[tm.lamps.Count];
                for (int i = 0; i < heads.Length; i++) { heads[i] = tm.origin + tm.lamps[i].head; gains[i] = LampGain(tm.lamps[i]); }
                var lights = new GameObject("LampLights");
                lights.transform.SetParent(root.transform, false);
                lights.AddComponent<NightGlow>().Init(heads, gains);
            }

            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            live[key] = new Tile { go = root, meshes = meshes, colliders = colliders };
            // Its towers cast on the frame they appear, not at the next
            // re-count of the scene.
            SunShadows.Register(root);

            var timing = new TileTiming
            {
                tx = tx, tz = tz,
                buildMs = (float)buildMs, attachMs = (float)(tAttach - tBuild), cookMs = cookMs,
                propsMs = (float)(tProps - tAttach), treesMs = (float)(tTrees - tProps),
                totalMs = (float)(buildMs + clock.Elapsed.TotalMilliseconds),
                colliders = colliders, props = props,
                slices = slices, maxSliceMs = maxSliceMs, standUpMs = (float)clock.Elapsed.TotalMilliseconds,
            };
            LastTiming = timing;
            recentBuilds.Add((Time.realtimeSinceStartup, timing.FrameMs));
            if (recentBuilds.Count > 256) recentBuilds.RemoveAt(0);
            TileBuilt?.Invoke(timing);
        }

        /// <summary>
        /// Stand a built tile up under a root: renderers AND colliders, the
        /// one definition of which mesh sits on which layer. The audit and
        /// the preview go through here too, so what they photograph and
        /// ray-cast is what the player drives on. That includes the street
        /// lamps' posts (render mesh + Solid boxes); their LIGHT is
        /// EnsureTile's, since an audit has no use for a NightGlow.
        /// </summary>
        public static Mesh[] Attach(GameObject root, CityMeshes.TileMeshes tm,
                                    System.Func<CityMeshes.Slot, Material> matFor)
        {
            var meshes = new List<Mesh>(5);
            if (tm.ground != null)
            {
                var g = Child(root, "Ground", 0);
                Render(g, tm.ground, tm.groundSlots, matFor);
                // Nothing lies under the ground to be shaded by it (nor under
                // the kerbs or the water; and a lamp post's shadow is a pixel):
                // each would be draw calls a tile a frame in the sun's map.
                SunShadows.Exclude(g);
                CookCollider(g, tm.ground);
                meshes.Add(tm.ground);
            }
            if (tm.roads != null)
            {
                var g = Child(root, "Roads", RoadLayer);
                Render(g, tm.roads, tm.roadSlots, matFor);
                CookCollider(g, tm.roads);
                meshes.Add(tm.roads);
            }
            if (tm.barriers != null)
            {
                // Solid, like a pier: CollisionAudio and the stuck watchdog
                // treat the layer as a wall, which is what a Jersey barrier, a
                // retaining wall and a bridge rail all are. The rails lived in
                // the Roads mesh until 2026-09-13, so the wheel rays (which
                // skip only this layer) could stand a car on a rail's top.
                var g = Child(root, "Barriers", SolidLayer);
                Render(g, tm.barriers, new[] { CityMeshes.Slot.Concrete }, matFor);
                CookCollider(g, tm.barriers);
                meshes.Add(tm.barriers);
            }
            if (tm.kerbs != null)
            {
                // RENDER-ONLY, deliberately: the inch of face under a grounded
                // edge. As a collider it was a 20 cm wall along every street
                // that the car's body box met before its wheels saw the edge;
                // the verge in the ground mesh is what the car climbs back on.
                var g = Child(root, "Kerbs", 0);
                Render(g, tm.kerbs, new[] { CityMeshes.Slot.Concrete }, matFor);
                SunShadows.Exclude(g);
                meshes.Add(tm.kerbs);
            }
            if (tm.water != null)
            {
                var g = Child(root, "Water", 0);
                Render(g, tm.water, new[] { CityMeshes.Slot.Water }, matFor);
                SunShadows.Exclude(g);
                meshes.Add(tm.water);
            }
            if (tm.banks != null)
            {
                // WP-25: the creeks' clay banks, draped a hand over the
                // lattice. Render-only (the lattice under them is what the
                // wheels meet) and no sun-map caster, like the water.
                var g = Child(root, "Banks", 0);
                g.AddComponent<MeshFilter>().sharedMesh = tm.banks;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = matFor != null ? BankMaterial() : null;
                mr.enabled = mr.sharedMaterial != null;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                SunShadows.Exclude(g);
                meshes.Add(tm.banks);
            }
            if (tm.buildings != null)
            {
                // The buildings collide as the walls you see. They were
                // oriented boxes, and an L-shaped block's box reached into the
                // street — an invisible wall across a lane, on the Solid layer.
                var g = Child(root, "Buildings", SolidLayer);
                Render(g, tm.buildings, tm.buildingSlots, matFor);
                CookCollider(g, tm.buildings);
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
            if (tm.lampPosts != null)
            {
                // Render-only: posts, arms and heads in one mesh, one draw a
                // tile. The runtime material only where the caller renders
                // (the audit stands tiles up with no materials at all).
                var g = new GameObject("Lamps");
                g.transform.SetParent(root.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = tm.lampPosts;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = matFor != null ? LampPostMaterial() : null;
                mr.enabled = mr.sharedMaterial != null;   // no material draws pink, not nothing
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                SunShadows.Exclude(g);
                meshes.Add(tm.lampPosts);
            }
            int solidLamps = 0;
            foreach (var l in tm.lamps) if (!l.breakaway) solidLamps++;
            if (solidLamps > 0)
            {
                // SOLID, a box up each pole: a car that leaves a street at
                // speed meets a pole, as it would (and the fast roads keep
                // theirs out of the clear zone for exactly that reason). Every
                // box on one object, so every one of them is NAMED LampPost: a
                // GameObject per post would be a hundred transforms a tile for
                // nothing, and a 0.3 m square pole's yaw is not something a car
                // can feel. Built here and not in EnsureTile, so the audits
                // ray-cast the posts the player meets, and skip them by that
                // name. A post in a race route's run-off BREAKS AWAY (Q15's
                // pattern; the WP-15 review): drawn and lit, no box.
                var c = Child(root, LampPostName, SolidLayer);
                foreach (var l in tm.lamps)
                {
                    if (l.breakaway) continue;
                    var bc = c.AddComponent<BoxCollider>();
                    bc.center = l.foot + Vector3.up * (l.height * 0.5f);
                    bc.size = new Vector3(LampColliderM, l.height, LampColliderM);
                }
            }
            return meshes.ToArray();
        }

        /// <summary>
        /// Stand a tile's tree cards up under its root (WP-08): one
        /// render-only mesh on the Foliage layer (one draw; SunShadows makes
        /// it one cutout caster, registered here as it appears). A null
        /// material switches the cards off. The trunks are not the tile's:
        /// PlantTrees hands them to <see cref="Trunks"/>.
        /// </summary>
        public static GameObject AttachTrees(GameObject root, CityTrees.TreeTile tt, Material mat)
        {
            if (tt == null || tt.mesh == null) return null;
            var g = Child(root, "Trees", CityTrees.FoliageLayer);
            g.AddComponent<MeshFilter>().sharedMesh = tt.mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.enabled = mat != null;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SunShadows.Register(g);
            return g;
        }

        /// <summary>
        /// Stand a tile's signs up under its root (WP-23): one render-only mesh
        /// (one draw, the kit's atlas material; not a sun-map caster: a draw a
        /// tile a frame for shadows few would see), and the billboards' and
        /// gantries' posts as box colliders, all on one Solid-layer object
        /// named <see cref="CitySigns.PostName"/>. A null material switches the
        /// faces off (they still collide).
        /// </summary>
        public static GameObject AttachSigns(GameObject root, CitySigns.SignTile st, Material mat)
        {
            if (st == null || st.mesh == null) return null;
            var g = Child(root, "Signs", 0);
            g.AddComponent<MeshFilter>().sharedMesh = st.mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.enabled = mat != null;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SunShadows.Exclude(g);
            if (st.posts.Count > 0)
            {
                var c = Child(root, CitySigns.PostName, SolidLayer);
                var origin = root.transform.position;
                foreach (var p in st.posts)
                {
                    // one child per post: they stand at their own yaw
                    var pg = new GameObject(CitySigns.PostName);
                    pg.layer = SolidLayer;
                    pg.transform.SetParent(c.transform, false);
                    pg.transform.position = p.centre;
                    pg.transform.rotation = Quaternion.Euler(0f, p.yawDeg, 0f);
                    pg.AddComponent<BoxCollider>().size = p.size;
                }
            }
            return g;
        }

        /// <summary>
        /// Stand a tile's furniture up under its root (WP-15): the poles, their
        /// wires and cobra-heads and the tile's street lamps as one render-only
        /// mesh on the kit's furniture atlas (one draw, no sun-map caster) - the
        /// lamp posts' own mesh (Attach's "Lamps") is destroyed, since this
        /// draws them - and a box up every pole on one Solid-layer object named
        /// <see cref="CityPoles.PostName"/> (utility poles are solid: Q15). A
        /// null material leaves the lamp mesh as it was and draws no furniture
        /// (the poles still collide).
        /// </summary>
        public static GameObject AttachFurniture(GameObject root, CityPoles.PoleTile pt, Material mat)
        {
            if (pt == null) return null;
            GameObject g = null;
            if (pt.mesh != null)
            {
                g = Child(root, "Furniture", 0);
                g.AddComponent<MeshFilter>().sharedMesh = pt.mesh;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.enabled = mat != null;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                SunShadows.Exclude(g);
                if (mat != null)
                {
                    // the lamp posts' own mesh goes: the furniture draws them now
                    var lamps = root.transform.Find("Lamps");
                    if (lamps != null)
                    {
                        var mf = lamps.GetComponent<MeshFilter>();
                        if (mf != null) Kill(mf.sharedMesh);
                        Kill(lamps.gameObject);
                    }
                }
            }
            if (pt.poles.Count > 0)
            {
                var c = Child(root, CityPoles.PostName, SolidLayer);
                var origin = root.transform.position;
                foreach (var p in pt.poles)
                {
                    var (centre, size) = CityPoles.ColliderOf(p);
                    var bc = c.AddComponent<BoxCollider>();
                    bc.center = centre - origin;
                    bc.size = size;
                }
            }
            return g;
        }

        /// <summary>How hard a street lamp lights the road, against a
        /// cobra-head's: uptown's acorn posts are pedestrian lamps, lower and a
        /// fraction of the wattage, and there are twice as many of them.</summary>
        public const float AcornLightGain = 0.25f;
        static float LampGain(CityMeshes.Lamp l) => l.kind == CityMeshes.LampAcorn ? AcornLightGain : 1f;

        /// <summary>A lamp post collider's square side, a hair over the drawn
        /// post's 0.26 m so a wheel never clips into the pole it touches.</summary>
        const float LampColliderM = 0.3f;

        static Material lampPostMat;

        /// <summary>
        /// The posts' material: the city kit's pack metal (WP-07; the owner's
        /// pack-texture rule). Without a kit, a PSX/Lit tint made once per
        /// process and never saved, as it was before the kit existed. Null if
        /// even the shader is missing: Attach then switches the posts'
        /// renderer off rather than draw them pink, and they still collide.
        /// </summary>
        public static Material LampPostMaterial()
        {
            var kit = CityKit.Get();
            if (kit != null && kit.lampPost != null) return kit.lampPost;
            if (lampPostMat != null) return lampPostMat;
            var sh = Shader.Find("PSX/Lit");
            if (sh == null) return null;
            lampPostMat = new Material(sh) { name = "CityLampPost", hideFlags = HideFlags.HideAndDontSave };
            lampPostMat.SetColor("_Color", LampPostColor);
            if (lampPostMat.HasProperty("_Affine")) lampPostMat.SetFloat("_Affine", 0f);
            return lampPostMat;
        }

        /// <summary>WP-25: the creeks' banks, the kit's pack clay in the day's
        /// dress (the builder registers it with the ground's wardrobe: the
        /// dirt tints, and the snow turf on a snowy day); null without a kit
        /// (the banks' renderer is then off).</summary>
        public static Material BankMaterial()
        {
            var kit = CityKit.Get();
            return kit != null ? SeasonDress.Substitute(kit.bank) : null;
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
            var table = materials;
            if (table == null) { var kit = CityKit.Get(); table = kit != null ? kit.slots : null; }
            if (table != null && i < table.Length && table[i] != null)
                return SeasonDress.Substitute(table[i]);
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
