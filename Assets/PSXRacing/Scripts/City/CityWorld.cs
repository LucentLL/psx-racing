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
            var mc = g.AddComponent<MeshCollider>();
            // city perf round 1: the options its mesh was cooked ahead with (CityMeshes.FastCook)
            if (CityMeshes.FastCook) mc.cookingOptions = CityMeshes.CookOptions;
            mc.sharedMesh = m;
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
        // ---- city-popin (2026-10-28): tiles built AHEAD ----
        /// <summary>The tile build order: by when a tile will be needed (its
        /// nearest edge inside <see cref="NeedM"/> of the car, at the car's
        /// closing speed along its travel or view), not by plain distance -
        /// and at speed the next row ahead (ring + 1, which the drop rule
        /// keeps) is built before the car reaches it. PSX_CITY_AHEAD=0: the old
        /// nearest-first order (the A/B).</summary>
        public static bool BuildAhead = System.Environment.GetEnvironmentVariable("PSX_CITY_AHEAD") != "0";
        const float NeedM = 420f, LookaheadSpeed = 25f, LookaheadCos = 0.6f;
        /// <summary>At speed the slice budget rises to this - only while the
        /// frame rate holds (the smoothed frame under <see cref="FastFrameMs"/>).</summary>
        public static float FastSliceBudgetMs = 6f, FastUrgentSliceBudgetMs = 12f;
        const float FastSpeed = 30f, FastFrameMs = 17.5f;
        Vector3 lastPlayerPos;
        Vector3 playerVel;
        float frameMsAvg = 16.7f;
        bool velValid;
        int fadeSeen = -1;

        /// <summary>Seconds until a tile is needed (its nearest edge within
        /// NeedM), plus a tie-break by distance; behind the car a little later.</summary>
        float NeedScore(int tx, int tz, Vector3 p, Vector2 dir, float speed)
        {
            float x0 = tx * CityMeshes.TileSize, z0 = tz * CityMeshes.TileSize;
            float nx = Mathf.Clamp(p.x, x0, x0 + CityMeshes.TileSize) - p.x, nz = Mathf.Clamp(p.z, z0, z0 + CityMeshes.TileSize) - p.z;
            float dNear = Mathf.Sqrt(nx * nx + nz * nz);
            var rel = new Vector2(x0 + 0.5f * CityMeshes.TileSize - p.x, z0 + 0.5f * CityMeshes.TileSize - p.z);
            float cosA = rel.sqrMagnitude > 1f ? Vector2.Dot(rel.normalized, dir) : 1f;
            float closing = 6f + speed * Mathf.Max(cosA, 0f);
            return Mathf.Max(dNear - NeedM, 0f) / closing + dNear * 0.0005f + (cosA < 0f ? 0.25f : 0f);
        }
        CityMeshes.TileJob job;
        long jobKey;
        // ---- city-build-hitches (2026-10-29): no tile job finished in one frame ----
        /// <summary>A car reaching a tile whose trees still waited, or a tile
        /// of its own not built, FINISHED the open tile job in that one frame
        /// (CompleteJob: 89-440 ms in the editor on the Uptown Loop, the HUD's
        /// "build max 208 ms" in the owner's I-277 race) and then planted, or
        /// built. Now: a tile next to a car plants its trees (a slice a frame)
        /// before the next job starts, so a car is seldom IN one still waiting;
        /// and where one is, or a car's own tile is missing, the open job for
        /// ANOTHER tile is given up (its tile is built again later, the same
        /// tile: a job is deterministic from its start) instead of finished.
        /// PSX_CITY_NOFORCE=0: as before (the A/B).</summary>
        public static bool NoForcedFinish = System.Environment.GetEnvironmentVariable("PSX_CITY_NOFORCE") != "0";
        /// <summary>Tile jobs given up so far (their tiles built again later).</summary>
        public static int JobsAbandoned;

        /// <summary>Is a tile within one tile of the player's, or of a car the
        /// world keeps a ring under?</summary>
        bool NearCar(long k, int ptx, int ptz)
        {
            int tx = (int)(k >> 24), tz = (int)((k << 40) >> 40);
            if (Mathf.Abs(tx - ptx) <= 1 && Mathf.Abs(tz - ptz) <= 1) return true;
            foreach (var a in anchors)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                int ax = Mathf.FloorToInt(a.position.x / CityMeshes.TileSize), az = Mathf.FloorToInt(a.position.z / CityMeshes.TileSize);
                if (Mathf.Abs(tx - ax) <= 1 && Mathf.Abs(tz - az) <= 1) return true;
            }
            return false;
        }

        /// <summary>The share of a frame the open job may take while a car is
        /// within <see cref="CriticalM"/> of its tile (city-build-hitches).</summary>
        public static float CriticalSliceBudgetMs = 20f;
        const float CriticalM = 48f;

        /// <summary>Is a car (the player, or one the world keeps a ring under)
        /// within <see cref="CriticalM"/> of the open job's tile?</summary>
        bool JobTileCritical()
        {
            if (job == null) return false;
            float x0 = job.tx * CityMeshes.TileSize, z0 = job.tz * CityMeshes.TileSize;
            bool Near(Vector3 p)
            {
                float nx = Mathf.Clamp(p.x, x0, x0 + CityMeshes.TileSize) - p.x, nz = Mathf.Clamp(p.z, z0, z0 + CityMeshes.TileSize) - p.z;
                return nx * nx + nz * nz < CriticalM * CriticalM;
            }
            if (player != null && Near(player.position)) return true;
            foreach (var a in anchors) if (a != null && a.gameObject.activeInHierarchy && Near(a.position)) return true;
            return false;
        }

        // ---- city perf round 1 (2026-10-30): the city's share paced by the clock ----
        /// <summary>The tile and tree slices were ms a FRAME (4 / 6 / 10 / 12 /
        /// 20): when the frames slow down - a browser's slower code, a busy PC,
        /// a phone - the build falls behind the car by the same factor, the car
        /// reaches a tile still building, and that tile is finished (or built
        /// whole) in ONE frame: the WebGL player's 300-600 ms hitches. Now a
        /// frame's share is also at least <see cref="PaceShareSlow"/> /
        /// <see cref="PaceShareFast"/> of the time the frame itself took (the
        /// share of the CPU the city may use, not a slice of a 60 Hz frame), and
        /// the job whose tile the car is closing on is spread over the frames
        /// left before the car gets there (its work left estimated from the
        /// tiles built so far), up to <see cref="PaceCapMs"/>. PSX_CITY_PACE=0
        /// (or ?ab=CityWorld.PaceBuilds:0): as before.</summary>
        public static bool PaceBuilds = System.Environment.GetEnvironmentVariable("PSX_CITY_PACE") != "0";
        /// <summary>A car IN a tile whose trees still wait planted them all in
        /// one frame; now at <see cref="PaceCapMs"/> a frame (a few frames: the
        /// trunks round a car on the road stand a frame or two later).
        /// ?ab=CityWorld.UrgentTreesPaced:0: in one frame, as before.</summary>
        public static bool UrgentTreesPaced = System.Environment.GetEnvironmentVariable("PSX_CITY_URGENTTREES") != "0";
        public const float PaceShareSlow = 0.25f, PaceShareFast = 0.35f, PaceCapMs = 28f;
        /// <summary>A built tile's own work, ms, as the last tiles took it
        /// (an average that follows the city: uptown's tiles are heavier).</summary>
        float tileWorkAvg = 80f;

        float Paced(float budget, float speed)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 1000f;
            float share = speed > FastSpeed ? PaceShareFast : PaceShareSlow;
            return Mathf.Min(Mathf.Max(budget, dt * share), Mathf.Max(budget, PaceCapMs));
        }

        /// <summary>The share that finishes the open job before the car
        /// (closing on its tile along <paramref name="dir"/>) is within a car
        /// length of it, with a margin.</summary>
        float DeadlineBudget(float budget, Vector3 p, Vector2 dir, float speed)
        {
            if (job == null) return budget;
            float T = CityMeshes.TileSize;
            float x0 = job.tx * T, z0 = job.tz * T;
            float nx = Mathf.Clamp(p.x, x0, x0 + T) - p.x, nz = Mathf.Clamp(p.z, z0, z0 + T) - p.z;
            float d = Mathf.Sqrt(nx * nx + nz * nz);
            var rel = new Vector2(x0 + 0.5f * T - p.x, z0 + 0.5f * T - p.z);
            float cosA = rel.sqrMagnitude > 1f ? Vector2.Dot(rel.normalized, dir) : 1f;
            float closing = 1f + speed * Mathf.Max(cosA, 0f);
            float tEnter = Mathf.Max(d - 6f, 0f) / closing;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1f / 240f, 0.1f);
            float remain = Mathf.Max(tileWorkAvg - (float)job.WorkMs, 0.15f * tileWorkAvg);
            float framesLeft = Mathf.Max(1f, tEnter / dt);
            return Mathf.Clamp(1.5f * remain / framesLeft, budget, Mathf.Max(budget, PaceCapMs));
        }

        /// <summary>Give the open job up: its meshes so far destroyed, the
        /// builder's scratch left for the next job's start to clear.</summary>
        void AbandonJob()
        {
            var j = job;
            job = null;
            if (j == null) return;
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            try { AbandonNow(j); }
            finally { if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileAbandon, pc); }
        }

        void AbandonNow(CityMeshes.TileJob j)
        {
            j.Abandon();
            var r = j.Result;
            if (r != null)
                foreach (var m in new[] { r.ground, r.roads, r.barriers, r.guardrails, r.kerbs, r.lampPosts, r.banks, r.water, r.buildings, r.buildingCollider })
                    Kill(m);
            JobsAbandoned++;
        }

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

        /// <summary>City perf round 1: the city's lazy tables and its lots'
        /// models made at load, behind the loading screen (EnsureInit).</summary>
        public static bool WarmAtLoad = System.Environment.GetEnvironmentVariable("PSX_CITY_WARM") != "0";

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
            // city perf round 1: the other lazy city-wide tables and the lots'
            // models, at load too (each cost its first tile one frame of
            // 250-320 ms in the editor: Editor/HitchProbe.cs); PSX_CITY_WARM=0: as before
            if (WarmAtLoad && Application.isPlaying)
            {
                var wc = System.Diagnostics.Stopwatch.StartNew();
                CitySigns.Warm(Map);
                double tSigns = wc.Elapsed.TotalMilliseconds;
                CitySignals.AnyIn(Map, nodeTrims, 0, 0);
                double tSignals = wc.Elapsed.TotalMilliseconds;
                int models = CityProps.WarmCity(buildings);
                double tModels = wc.Elapsed.TotalMilliseconds;
                // the traffic's cars (the pool builds one a frame and every key is
                // used as it fills: each model's first load was that frame's)
                int cars = 0;
                foreach (var key in TrafficSystem.Keys) if (CarModelLibrary.Load(key) != null) cars++;
                Debug.Log($"[CityWorld] warmed at load: guide sites {tSigns:0} ms, junction controls {tSignals - tSigns:0} ms, {models} lot models {tModels - tSignals:0} ms, {cars} traffic models {wc.Elapsed.TotalMilliseconds - tModels:0} ms");
            }
            BuildFoodIndex();
            // the trunk table, on a child of its own that no scene saves
            var tgo = new GameObject("CityTreeTrunks") { hideFlags = HideFlags.DontSave };
            tgo.transform.SetParent(transform, false);
            Trunks = tgo.AddComponent<TreeTrunks>();
            Trunks.standName = CityTrees.TrunkName;
            Trunks.trunkHeight = CityTrees.TrunkHeightM;
            // Uptown B4: the far skyline, before any tile job holds the
            // builder's scratch
            Skyline = CitySkyline.Create(transform, Map, nodeTrims, MatFor);
            // the far city (2026-10-24): the land, the lights and the glow
            // past the streamed ring; the skyline's buildings keep their own windows
            Far = CityFar.Create(transform, Map, MatFor, Skyline != null ? Skyline.Outlines : null, buildings);
            // city-popin (2026-10-28): the tile fade table, and every building
            // the skyline does not draw as a far proxy out to ~2 km
            CityTileFade.Ensure();
            // (city perf round 1: with CityBuildings' lots - CityMeshes.LotProxies)
            Proxies = CityProxies.Create(transform, Map, nodeTrims, MatFor, Skyline != null ? Skyline.Outlines : null, buildings);
            // city perf round 1: every variant the city draws with, compiled behind the loading screen
            if (WarmAtLoad && Application.isPlaying)
                CityShaderWarm.Warm(new[] { Skyline != null ? Skyline.gameObject : null, Far != null ? Far.gameObject : null, Proxies != null ? Proxies.gameObject : null });
        }

        /// <summary>The far buildings (city-popin): every footprint the
        /// skyline does not draw, past the far plane to ~2 km (null: off).</summary>
        public CityProxies Proxies { get; private set; }

        /// <summary>Uptown B4: the tallest core towers past the streamed ring
        /// (null without a facade material).</summary>
        public CitySkyline Skyline { get; private set; }
        /// <summary>The far city: land, lights, glow (CityFar).</summary>
        public CityFar Far { get; private set; }
        System.Func<long, bool> isLive;

        /// <summary>Streaming's stages, for the profiler (tools\perf-probe.ps1).</summary>
        static readonly Unity.Profiling.ProfilerMarker FarMarker = new Unity.Profiling.ProfilerMarker("CityFar.Refresh"),
            SkylineMarker = new Unity.Profiling.ProfilerMarker("CitySkyline.Refresh"),
            JobStepMarker = new Unity.Profiling.ProfilerMarker("CityWorld.JobStep"),
            StandUpMarker = new Unity.Profiling.ProfilerMarker("CityWorld.StandUp"),
            PlantMarker = new Unity.Profiling.ProfilerMarker("CityWorld.PlantTrees"),
            SignsMarker = new Unity.Profiling.ProfilerMarker("CityWorld.PrepareSigns"),
            BuildSyncMarker = new Unity.Profiling.ProfilerMarker("CityWorld.BuildWholeTile"),
            FinishMarker = new Unity.Profiling.ProfilerMarker("CityWorld.FinishJob"),
            ProxiesMarker = new Unity.Profiling.ProfilerMarker("CityProxies.Refresh"),
            ProxyStepMarker = new Unity.Profiling.ProfilerMarker("CityProxies.Step");

        /// <summary>The skyline and the far city for this camera: what the
        /// live tiles draw is left to them, bounds, band and glow set. The
        /// game calls it every frame; a tool that moves a camera by hand calls
        /// it before a shot. The far city first: the skyline reads the edge
        /// band it sets.</summary>
        public void RefreshSkyline(Camera cam)
        {
            if (Far != null) using (FarMarker.Auto()) Far.Refresh(cam);
            CityTileFade.Ensure();
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            if (Proxies != null) using (ProxiesMarker.Auto()) Proxies.Refresh(cam);
            if (PerfClock.On) { PerfClock.Add(PerfClock.Slot.ProxyRefresh, pc); pc = PerfClock.Now(); }
            if (Skyline == null) return;
            // a live tile draws a skyline tower near only once its own copy
            // has faded in over it (city-popin: PSX/Lit hands the pixels over)
            if (isLive == null) isLive = k => live.ContainsKey(k) && CityTileFade.Get((int)(k >> 24), (int)((k << 40) >> 40), CityTileFade.R) >= 1f;
            using (SkylineMarker.Auto()) Skyline.Refresh(cam, isLive);
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.Skyline, pc);
        }

        void LateUpdate()
        {
            if (Skyline != null || Far != null) RefreshSkyline(Camera.main);
        }

        /// <summary>The key of the tile a plan point stands in (the tile a
        /// footprint is built by is the one its centre is in).</summary>
        public static long TileKeyAt(Vector2 p) =>
            Key(Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));

        /// <summary>Is the tile under a plan point built (its colliders
        /// standing)? The road log tells a hole from a tile not there yet.</summary>
        public bool TileLive(Vector2 p) => live.ContainsKey(TileKeyAt(p));

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
            // city perf round 1: what the worker threads finished, into the world
            CityWorker.Pump();
            // the one clock every signal head in the city runs on
            CitySignals.Tick(Time.timeAsDouble);
            if (player == null || Map == null) return;
            var p = player.position;
            int ptx = Mathf.FloorToInt(p.x / CityMeshes.TileSize);
            int ptz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);
            // city-popin: the fades advance; the skyline re-reads its live
            // towers once a tile's own copy is whole; the car's travel
            long pcf = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            CityTileFade.Tick();
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.FadeTick, pcf);
            if (Skyline != null && fadeSeen != CityTileFade.BuildingsVersion) { fadeSeen = CityTileFade.BuildingsVersion; Skyline.LiveVersion++; }
            float dtS = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            frameMsAvg = Mathf.Lerp(frameMsAvg, dtS * 1000f, 0.05f);
            if (velValid && Time.deltaTime > 1e-5f)
            {
                var v = (p - lastPlayerPos) / Time.deltaTime;
                v.y = 0f;
                if (v.sqrMagnitude > 120f * 120f) v = playerVel;   // a teleport, not a speed
                playerVel = Vector3.Lerp(playerVel, v, 0.2f);
            }
            lastPlayerPos = p; velValid = true;
            float speed = playerVel.magnitude;
            Vector2 dir;
            if (speed > 3f) dir = new Vector2(playerVel.x, playerVel.z) / speed;
            else
            {
                var cf = Camera.main != null ? Camera.main.transform.forward : player.forward;
                dir = new Vector2(cf.x, cf.z);
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up;
            }

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
                    wanted.Add((tx, tz, BuildAhead ? NeedScore(tx, tz, p, dir, speed) : cx * cx + cz * cz));
                }
            // the row ahead, at speed (kept: the drop rule holds ring + 1)
            if (BuildAhead && speed > LookaheadSpeed)
            {
                int r1 = ring + 1;
                for (int dz = -r1; dz <= r1; dz++)
                    for (int dx = -r1; dx <= r1; dx++)
                    {
                        if (Mathf.Abs(dx) != r1 && Mathf.Abs(dz) != r1) continue;
                        int tx = ptx + dx, tz = ptz + dz;
                        if (live.ContainsKey(Key(tx, tz))) continue;
                        var rel = new Vector2((tx + 0.5f) * CityMeshes.TileSize - p.x, (tz + 0.5f) * CityMeshes.TileSize - p.z);
                        if (Vector2.Dot(rel.normalized, dir) < LookaheadCos) continue;
                        wanted.Add((tx, tz, NeedScore(tx, tz, p, dir, speed)));
                    }
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
                        // (ahead: as urgent as the player's tiles in view - a car is on it)
                        // city-build-hitches: by when THIS car is in it (its nearest edge at
                        // the car's own speed), on the player's clock of seconds: a rival
                        // reaching a tile still building forced the job's finish (780 ms)
                        if (NoForcedFinish && BuildAhead)
                        {
                            float x0 = tx * CityMeshes.TileSize, z0 = tz * CityMeshes.TileSize;
                            float nx = Mathf.Clamp(a.position.x, x0, x0 + CityMeshes.TileSize) - a.position.x, nz = Mathf.Clamp(a.position.z, z0, z0 + CityMeshes.TileSize) - a.position.z;
                            var arb = a.GetComponent<Rigidbody>();
                            float sa = arb != null ? new Vector2(arb.linearVelocity.x, arb.linearVelocity.z).magnitude : 0f;
                            wanted.Add((tx, tz, Mathf.Sqrt(nx * nx + nz * nz) / Mathf.Max(6f, sa)));
                        }
                        else wanted.Add((tx, tz, BuildAhead ? Mathf.Sqrt(cx * cx + cz * cz) / 50f : cx * cx + cz * cz));
                    }
            }
            // city-build-hitches: a tile next to a car whose trees still wait
            // plants them before the next tile starts (see NoForcedFinish)
            bool treesFirst = treeSteps != null;   // an open tree frame first (city-build-hitches)
            if (NoForcedFinish && job == null && treesPending.Count > 0)
                foreach (var k in treesPending.Keys) if (NearCar(k, ptx, ptz)) { treesFirst = true; break; }
            if (SliceBuilds && (job != null || wanted.Count > 0))
            {
                // A few ms of the nearest missing tile's build every frame
                // until it stands; its stand-up (renderers, colliders, props)
                // takes a frame of its own. The tile is finished even if the
                // car turned away from it: a drop takes it back if need be.
                if (job == null && !treesFirst)
                {
                    wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
                    StartJob(wanted[0].tx, wanted[0].tz);
                }
                if (job != null)
                {
                    // at speed, while the frame rate holds, a bigger share
                    bool fast = BuildAhead && speed > FastSpeed && frameMsAvg < FastFrameMs;
                    float budget = hurry ? (fast ? FastUrgentSliceBudgetMs : UrgentSliceBudgetMs) : (fast ? FastSliceBudgetMs : SliceBudgetMs);
                    // city-build-hitches: a car about to drive onto the tile still building
                    // spends a bigger share, so it is done over frames, not finished in one
                    if (NoForcedFinish && JobTileCritical()) budget = Mathf.Max(budget, CriticalSliceBudgetMs);
                    // city perf round 1: by the clock, and by when the car gets there
                    if (PaceBuilds) budget = Mathf.Max(Paced(budget, speed), DeadlineBudget(budget, p, dir, speed));
                    if (job.Done) CompleteJob();
                    else
                    {
                        long pcs = PerfClock.On ? PerfClock.Now() : 0;
                        using (JobStepMarker.Auto()) job.Step(budget);
                        if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileStep, pcs);
                    }
                    built = true;
                }
            }
            else if (wanted.Count > 0)
            {
                wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
                built |= EnsureTile(wanted[0].tx, wanted[0].tz);
            }

            // city-popin: the far buildings' share - small while a tile is
            // being built (smaller still when one next to a car is missing)
            if (Proxies != null)
            {
                long pcp = PerfClock.On ? PerfClock.Now() : 0;
                using (ProxyStepMarker.Auto()) Proxies.Step(job != null ? (hurry ? 0.4 : 1.0) : 2.0);
                if (PerfClock.On) PerfClock.Add(PerfClock.Slot.ProxyStep, pcp);
            }

            // THE TREES ON A FRAME OF THEIR OWN (WP-08; WP-09's third stage,
            // for the trees): a tile's build and its planting never share a
            // frame. When no tile was built this frame, the nearest tile still
            // waiting plants its trees (a tile is built at least a tile ahead
            // of any car, and a car at 250 km/h is seconds from it). A waiting
            // tile a car is IN plants now regardless: its trunks must stand.
            if (treesPending.Count > 0 || treeSteps != null)
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
                    if (NoForcedFinish && !near && NearCar(k, ptx, ptz)) d2 -= 1e11f;   // city-build-hitches: next to a car first
                    if (d2 < bestD) { bestD = d2; best = k; urgent = near; }
                }
                // the gantries and billboards that reach it are decided first,
                // a slice a frame (WP-23: they are decided on the masks of the
                // tiles round it, which a tile's own frame should not all pay for)
                if (!built || urgent)
                {
                    // the trees put their own lattice in the builder's cache:
                    // no job may be open across them (WP-09)
                    if (job != null) { if (NoForcedFinish) AbandonJob(); else CompleteJob(); }   // city-build-hitches
                    bool fastT = BuildAhead && speed > FastSpeed && frameMsAvg < FastFrameMs;
                    float treeBudget = fastT ? FastSliceBudgetMs : SliceBudgetMs;
                    if (PaceBuilds) treeBudget = Paced(treeBudget, speed);   // city perf round 1
                    // city perf round 1: a car in a tile whose trees still wait takes
                    // the urgent share over a few frames, not the whole frame in one
                    double urgentBudget = PaceBuilds && UrgentTreesPaced ? PaceCapMs : double.MaxValue;
                    if (treeSteps != null && !(urgent && best != treeKey)) { using (PlantMarker.Auto()) RunTreeJob(urgent ? urgentBudget : treeBudget); }
                    else if (urgent && urgentBudget < double.MaxValue)
                    {
                        // (another tile's frame still open: it goes on first at the same
                        // share - its scratch and lattice are the planting's - then this one)
                        using (PlantMarker.Auto())
                        {
                            if (treeSteps != null) RunTreeJob(urgentBudget);
                            else if (BeginTrees(best)) RunTreeJob(urgentBudget);
                        }
                    }
                    else if (urgent) PlantTrees(best);
                    else if (PrepareSigns(best) == 0)
                    {
                        if (SliceTrees) { using (PlantMarker.Auto()) if (BeginTrees(best)) RunTreeJob(treeBudget); }
                        else PlantTrees(best);
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (job != null) { job.Abandon(); job = null; }
            AbandonTrees();
            foreach (var k in new List<long>(live.Keys)) DropTile(k);
        }

        void DropTile(long key)
        {
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            try { DropTileNow(key); }
            finally { if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileDrop, pc); }
        }

        void DropTileNow(long key)
        {
            if (treeSteps != null && treeKey == key) AbandonTrees();   // city-build-hitches
            treesPending.Remove(key);
            liveSigns.Remove(key);
            livePoles.Remove(key);
            liveSignals.Remove(key);
            if (Trunks != null) Trunks.RemoveTable(key);
            if (!live.TryGetValue(key, out var t)) return;
            live.Remove(key);
            CityTileFade.TileDown((int)(key >> 24), (int)((key << 40) >> 40));
            if (Skyline != null) Skyline.LiveVersion++;
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
            using var marker = PlantMarker.Auto();
            // city-build-hitches: a tree frame still open (sliced) is finished
            // first - the planting's scratch and the lattice are its
            if (treeSteps != null) { bool same = treeKey == key; RunTreeJob(double.MaxValue); if (same) return; }
            if (BeginTrees(key)) RunTreeJob(double.MaxValue);
        }

        // ---- city-build-hitches (2026-10-29): a tile's tree frame a slice a frame ----
        /// <summary>A tile's trees, poles, junction furniture and signs were
        /// planted in one frame (up to 63 ms in the editor, 28 ms on the Uptown
        /// Loop). Now CityTrees.BuildSteps runs a slice a frame
        /// (<see cref="SliceBudgetMs"/>) and the tile stands them up when it
        /// is done; while it is open no tile job and no other tree frame runs
        /// (they share its scratch and the builder's lattice), a tile build
        /// that cannot wait finishes it first, and a car IN the tile plants
        /// it now. PSX_CITY_SLICETREES=0: one frame, as before.</summary>
        public static bool SliceTrees = System.Environment.GetEnvironmentVariable("PSX_CITY_SLICETREES") != "0";
        IEnumerator<int> treeSteps;
        long treeKey;
        CityTrees.TreeTile treeTile;
        CityMeshes.TileMeshes treeTm;
        double treeWorkMs;
        /// <summary>A tile's tree frame is open (sliced, not finished).</summary>
        public bool TreeJobOpen => treeSteps != null;

        bool BeginTrees(long key)
        {
            if (!treesPending.TryGetValue(key, out var job)) return false;
            treesPending.Remove(key);
            if (!live.TryGetValue(key, out var tile) || tile.go == null) return false;
            int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
            CityMeshes.PutLattice(job.lattice);
            treeKey = key; treeTm = job.tm; treeWorkMs = 0.0;
            treeTile = new CityTrees.TreeTile { tx = tx, tz = tz };
            treeSteps = CityTrees.BuildSteps(Map, nodeTrims, buildings, job.tm, tx, tz, treeTile).GetEnumerator();
            return true;
        }

        /// <summary>The open tree frame's steps for a budget (all of them for
        /// double.MaxValue), and the tile's trees stood up when they end. True
        /// when it ended.</summary>
        bool RunTreeJob(double budgetMs)
        {
            if (treeSteps == null) return true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool more;
            do { more = treeSteps.MoveNext(); } while (more && clock.Elapsed.TotalMilliseconds < budgetMs);
            double ms = clock.Elapsed.TotalMilliseconds;
            if (PerfClock.On) PerfClock.AddMs(budgetMs == double.MaxValue ? PerfClock.Slot.TreeUrgent : PerfClock.Slot.TreeStep, ms);   // city perf round 1
            treeWorkMs += ms;
            if (more)
            {
                var slice = new TileTiming { tx = treeTile.tx, tz = treeTile.tz, treeFrame = true, treesMs = (float)ms, totalMs = (float)ms };
                recentBuilds.Add((Time.realtimeSinceStartup, (float)ms));
                if (recentBuilds.Count > 256) recentBuilds.RemoveAt(0);
                TileBuilt?.Invoke(slice);
                return false;
            }
            treeSteps.Dispose();
            treeSteps = null;
            long key = treeKey;
            if (live.TryGetValue(key, out var tile) && tile.go != null) StandTrees(key, tile, treeTile, treeTm, clock);
            treeTile = null; treeTm = null;
            return true;
        }

        /// <summary>Give the open tree frame up (its tile was dropped).</summary>
        void AbandonTrees()
        {
            if (treeSteps == null) return;
            treeSteps.Dispose();
            treeSteps = null;
            var tt = treeTile;
            treeTile = null; treeTm = null;
            if (tt == null) return;
            Kill(tt.mesh);
            if (tt.poles != null) Kill(tt.poles.mesh);
            if (tt.signals != null) { Kill(tt.signals.mesh); Kill(tt.signals.lamps); Kill(tt.signals.halos); }
            if (tt.signs != null) { Kill(tt.signs.mesh); Kill(tt.signs.guideMesh); }
        }

        void StandTrees(long key, Tile tile, CityTrees.TreeTile tt, CityMeshes.TileMeshes tm, System.Diagnostics.Stopwatch clock)
        {
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            try { StandTreesNow(key, tile, tt, tm, clock); }
            finally { if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TreeStand, pc); }
        }

        void StandTreesNow(long key, Tile tile, CityTrees.TreeTile tt, CityMeshes.TileMeshes tm, System.Diagnostics.Stopwatch clock)
        {
            int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
            var job = (tm: tm, lattice: (Dictionary<long, float>)null);
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
            if (st != null && (st.mesh != null || st.guideMesh != null))
            {
                AttachSigns(tile.go, st, CitySigns.Material());
                foreach (var sm in new[] { st.mesh, st.guideMesh })
                {
                    if (sm == null) continue;
                    System.Array.Resize(ref tile.meshes, tile.meshes.Length + 1);
                    tile.meshes[tile.meshes.Length - 1] = sm;
                }
                tile.colliders += st.posts.Count;
            }
            // the billboards' floodlights and the poles' cobra-heads join the
            // tile's street lamps: one NightGlow, one halo draw a tile
            int signLamps = st != null && st.mesh != null ? st.lamps.Count : 0;
            int poleHeads = pt != null ? pt.heads.Count : 0;
            // (signs round 2) the guide signs' mercury sign-lighting luminaires: halos only
            int guideLamps = st != null && st.guideMesh != null ? st.guideLamps.Count : 0;
            if (signLamps + poleHeads + guideLamps > 0)
            {
                var heads = new List<Vector3>(job.tm.lamps.Count + signLamps + poleHeads + guideLamps);
                var gains = new List<float>(heads.Capacity);
                var mercs = new List<bool>(heads.Capacity);   // the 1990s city: mercury heads by street
                foreach (var l in job.tm.lamps) { heads.Add(job.tm.origin + l.head); gains.Add(LampGain(l)); mercs.Add(l.mercury); }
                foreach (var h in job.tm.mastHeads) { heads.Add(job.tm.origin + h); gains.Add(MastLightGain); mercs.Add(false); }
                if (signLamps > 0) { heads.AddRange(st.lamps); for (int k = 0; k < signLamps; k++) { gains.Add(1f); mercs.Add(false); } }
                if (poleHeads > 0) { heads.AddRange(pt.heads); for (int k = 0; k < poleHeads; k++) { gains.Add(1f); mercs.Add(k < pt.headMercury.Count && pt.headMercury[k]); } }
                int lampHeads = heads.Count;
                if (guideLamps > 0) { heads.AddRange(st.guideLamps); for (int k = 0; k < guideLamps; k++) { gains.Add(GuideLampHalo); mercs.Add(true); } }
                var lt = tile.go.transform.Find("LampLights");
                GameObject lights = lt != null ? lt.gameObject : null;
                if (lights == null)
                {
                    lights = new GameObject("LampLights");
                    lights.transform.SetParent(tile.go.transform, false);
                }
                var glow = lights.GetComponent<NightGlow>();
                if (glow == null) glow = lights.AddComponent<NightGlow>();
                glow.cityField = true;
                glow.Init(heads, gains, mercs, lampHeads);
            }
            // city-popin: the tree frame (trees, poles, signals, signs) fades in
            // of its own, unless a car is in the tile
            if (CityTileFade.On || Proxies != null) CityTileFade.MarkTile(tile.go, CityTileFade.ModeTreeFrame, null);
            bool carIn = player != null && TileKeyAt(new Vector2(player.position.x, player.position.z)) == key;
            CityTileFade.TreesUp(tx, tz, !carIn);
            var timing = new TileTiming
            {
                tx = tx, tz = tz, treeFrame = true,
                treesMs = (float)clock.Elapsed.TotalMilliseconds, treePlantMs = tt.ms,
                totalMs = (float)clock.Elapsed.TotalMilliseconds, trees = tt.trees.Count,   // (sliced: the last slice and the stand-up)
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
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            try { return PrepareSignsNow(key); }
            finally { if (PerfClock.On) PerfClock.Add(PerfClock.Slot.Signs, pc); }
        }

        int PrepareSignsNow(long key)
        {
            using var marker = SignsMarker.Auto();
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
                // city-build-hitches: another tile's job is given up, not finished
                if (same || !NoForcedFinish) CompleteJob(); else AbandonJob();
                if (same) { CityTileFade.TileUp(tx, tz, false); return true; }
            }

            if (treeSteps != null) RunTreeJob(double.MaxValue);   // city-build-hitches: its lattice is the builder's
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CityMeshes.TileMeshes tm;
            using (BuildSyncMarker.Auto()) tm = CityMeshes.Build(Map, nodeTrims, buildings, tx, tz);
            if (PerfClock.On) PerfClock.AddMs(PerfClock.Slot.TileSync, clock.Elapsed.TotalMilliseconds);   // city perf round 1
            StandUp(key, tx, tz, tm, clock.Elapsed.TotalMilliseconds, 0, 0f);
            return true;
        }

        /// <summary>Start building a tile a slice a frame (WP-09).</summary>
        void StartJob(int tx, int tz)
        {
            EnsureInit();
            if (Map == null) return;
            if (treeSteps != null) RunTreeJob(double.MaxValue);   // city-build-hitches (not reached: an open tree frame blocks new jobs)
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
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            using (FinishMarker.Auto()) j.Finish();
            if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileFinish, pc);
            if (live.ContainsKey(k)) return;
            tileWorkAvg = Mathf.Lerp(tileWorkAvg, (float)j.WorkMs, 0.25f);   // city perf round 1
            StandUp(k, j.tx, j.tz, j.Result, j.WorkMs, j.Slices, (float)j.MaxSliceMs);
        }

        /// <summary>A built tile's meshes stood up under a root: renderers and
        /// colliders, the prop models, its trees queued, its lamps lit.
        /// <paramref name="buildMs"/> is the build's own work.</summary>
        void StandUp(long key, int tx, int tz, CityMeshes.TileMeshes tm, double buildMs, int slices, float maxSliceMs)
        {
            long pc = PerfClock.On ? PerfClock.Now() : 0;   // city perf round 1
            try { StandUpNow(key, tx, tz, tm, buildMs, slices, maxSliceMs); }
            finally { if (PerfClock.On) PerfClock.Add(PerfClock.Slot.TileStandUp, pc); }
        }

        void StandUpNow(long key, int tx, int tz, CityMeshes.TileMeshes tm, double buildMs, int slices, float maxSliceMs)
        {
            using var marker = StandUpMarker.Auto();
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
                    if (!CityBuildings.PropSeat(Map, b, def, out float gy, out _)) { PropsDropped++; continue; }
                    var go = Instantiate(prefab, root.transform);
                    go.transform.position = new Vector3(b.pos.x, gy - def.sink, b.pos.y);
                    go.transform.rotation = Quaternion.Euler(
                        0f, b.yaw * Mathf.Rad2Deg + def.yawOffsetDeg, 0f);
                    if (b.scale.sqrMagnitude > 0.01f) go.transform.localScale = b.scale;
                    props++;
                    // city perf round 1: a pack tower hands over to its far proxy past
                    // the squeeze's start, as the tile's own buildings do (TowerProxies)
                    if (CityMeshes.TowerProxies && Proxies != null && CityProps.IsTower(b.kind) && (CityTileFade.On || Proxies != null))
                        CityTileFade.MarkTile(go, CityTileFade.ModeBuildings, null);
                }
            }
            double tProps = clock.Elapsed.TotalMilliseconds;

            // THE TREES (WP-08) come after everything they must keep clear of
            // (the tile's roads, fill houses and lamps are in tm, the lots in
            // the building table) and on a later frame: queued here with the
            // lattice this build cached, planted by PlantTrees.
            if (CityTrees.Enabled || CitySigns.Enabled || CityPoles.Enabled || tm.lamps.Count > 0 || tm.mastHeads.Count > 0) treesPending[key] = (tm, CityMeshes.TakeLattice());
            double tTrees = clock.Elapsed.TotalMilliseconds;

            // THE LIGHT the street lamps throw (the posts are Attach's). A
            // NightGlow of its own per tile, under its own child, so the posts
            // are not among what it switches, and Init'd by hand: it is added
            // to an empty object, and in edit mode (the night-look shots stand
            // tiles up without play) AddComponent runs no Awake at all. It
            // registers every head with StreetLights and builds the halos;
            // dropping the tile destroys it, which takes both back.
            if (tm.lamps.Count > 0 || tm.mastHeads.Count > 0)
            {
                int nl = tm.lamps.Count;
                var heads = new Vector3[nl + tm.mastHeads.Count];
                var gains = new float[heads.Length];
                var mercs = new bool[heads.Length];
                for (int i = 0; i < nl; i++) { heads[i] = tm.origin + tm.lamps[i].head; gains[i] = LampGain(tm.lamps[i]); mercs[i] = tm.lamps[i].mercury; }
                // the bridges pass: a high mast's ring of sodium floods
                for (int i = 0; i < tm.mastHeads.Count; i++) { heads[nl + i] = tm.origin + tm.mastHeads[i]; gains[nl + i] = MastLightGain; }
                var lights = new GameObject("LampLights");
                lights.transform.SetParent(root.transform, false);
                var glow = lights.AddComponent<NightGlow>();
                glow.cityField = true;
                glow.Init(heads, gains, mercs);
            }

            int colliders = root.GetComponentsInChildren<Collider>(true).Length;
            // city-popin: every renderer of the tile reads its fade (its
            // buildings their own mode: they hand over to their proxies), and a
            // tile built a slice a frame fades in; one built whole (under a car,
            // a tool's ring) stands at once
            if (CityTileFade.On || Proxies != null) CityTileFade.MarkTile(root, CityTileFade.ModeObject, "Buildings");
            CityTileFade.TileUp(tx, tz, slices > 0);
            live[key] = new Tile { go = root, meshes = meshes, colliders = colliders };
            if (Skyline != null) Skyline.LiveVersion++;   // its towers' live flags re-read
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
                // city-popin (the flicker finder): the barrier wins the line its
                // foot stands on - it traded pixels with the road all along it
                var bmr = g.GetComponent<MeshRenderer>();
                if (bmr != null) bmr.sharedMaterial = BarrierMat(bmr.sharedMaterial);
                CookCollider(g, tm.barriers);
                meshes.Add(tm.barriers);
            }
            if (tm.guardrails != null)
            {
                // Leftover item 2: the W-beam lead-ins at parapet ends are
                // solid here, on the Solid layer like every barrier, and DRAWN
                // here too, on the furniture atlas. Until 2026-10-24 they were
                // drawn with the tile's furniture, which plants only on a frame
                // no tile builds or once a car is IN the tile: in a race tiles
                // build nearly every frame, so a rail at the edge of the tile
                // ahead stood solid and undrawn when the car met it (owner:
                // "almost every race on 277 I hit invisible walls").
                var g = Child(root, "Guardrail", SolidLayer);
                CookCollider(g, tm.guardrails);
                meshes.Add(tm.guardrails);
                var wbm = CityPoles.WBeamMesh(tm);
                if (wbm != null)
                {
                    var wg = Child(root, "GuardrailDrawn", 0);
                    wg.AddComponent<MeshFilter>().sharedMesh = wbm;
                    var mr = wg.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = CityPoles.Material();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    SunShadows.Exclude(wg);
                    meshes.Add(wbm);
                }
            }
            if (tm.structure != null)
            {
                // THE BRIDGES PASS (2026-10-29): girders, piers, caps, bearings,
                // abutments, object markers, high masts - RENDER-ONLY on the
                // structure atlas. Nothing here is cooked: the columns' and
                // masts' solids are capsules in tm.solids, the decks' surfaces
                // and rails are the Roads and Barriers meshes they always were.
                var g = Child(root, "Structure", 0);
                g.AddComponent<MeshFilter>().sharedMesh = tm.structure;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = matFor != null ? matFor(CityMeshes.Slot.Structure) : null;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                // no sun-map caster: the deck above already throws the bridge's
                // shadow, and a caster a tile is a draw a tile in the sun's map
                // (the perf probe: SetPass +24 a frame with it)
                SunShadows.Exclude(g);
                meshes.Add(tm.structure);
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
                // (Uptown C: a landmark's crown and a stadium's stands are
                // drawn but left out of the collider)
                CookCollider(g, tm.buildingCollider != null ? tm.buildingCollider : tm.buildings);
                meshes.Add(tm.buildings);
                if (tm.buildingCollider != null) meshes.Add(tm.buildingCollider);
            }
            foreach (var box in tm.solids)
            {
                var s = new GameObject("Solid");
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = box.center;
                s.transform.localRotation = Quaternion.Euler(0f, box.yawDeg, 0f);
                s.layer = SolidLayer;
                if (box.round)
                {
                    // the bridges pass: a round column, solid as drawn
                    var cc = s.AddComponent<CapsuleCollider>();
                    cc.direction = 1;
                    cc.radius = box.radius;
                    cc.height = Mathf.Max(box.size.y, 2f * box.radius);
                    continue;
                }
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
                    // the drawn post's own width (CityPoles.EmitLamp): an
                    // acorn's 0.14 m post, a cobra-head's 0.26 m (turned to its
                    // road; square to the world it stays inside the drawn post
                    // within 4 cm) - never a box wider than what is drawn
                    var bc = c.AddComponent<BoxCollider>();
                    float pw = l.kind == CityMeshes.LampAcorn ? AcornPostColliderM : LampColliderM;
                    bc.center = l.foot + Vector3.up * (l.height * 0.5f);
                    bc.size = new Vector3(pw, l.height, pw);
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
        /// <summary>
        /// The trees already standing, re-dressed for the day the calendar now
        /// says (the debug bench's date, 2026-10-02). A tile plants in the
        /// dress of its day and keeps it; the dresses differ only in the kit's
        /// material - the cards are sized for the widest - so the live tiles
        /// swap materials and nothing is planted again. Tiles built later
        /// plant in the new dress on their own.
        /// </summary>
        public void Redress()
        {
            var mat = CityTrees.MaterialFor(CityTrees.DressNow());
            foreach (var kv in live)
            {
                var go = kv.Value.go;
                if (go == null) continue;
                var t = go.transform.Find("Trees");
                var mr = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (mr == null) continue;
                mr.sharedMaterial = mat;
                mr.enabled = mat != null;
            }
        }

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
            if (st == null || (st.mesh == null && st.guideMesh == null)) return null;
            GameObject g = null;
            if (st.mesh != null)
            {
                g = Child(root, "Signs", 0);
                g.AddComponent<MeshFilter>().sharedMesh = st.mesh;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.enabled = mat != null;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                SunShadows.Exclude(g);
            }
            // the freeway guide signs (the highway signs pass): their own atlas, one more draw
            if (st.guideMesh != null)
            {
                var gm = CitySigns.GuideMaterial();
                var gg = Child(root, "GuideSigns", 0);
                gg.AddComponent<MeshFilter>().sharedMesh = st.guideMesh;
                var gr = gg.AddComponent<MeshRenderer>();
                gr.sharedMaterial = gm;
                gr.enabled = gm != null;
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gr.receiveShadows = false;
                SunShadows.Exclude(gg);
                if (g == null) g = gg;
            }
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
        static float LampGain(CityMeshes.Lamp l) => l.kind == CityMeshes.LampAcorn ? AcornLightGain : l.kind == CityMeshes.LampDeck ? DeckLightGain : 1f;
        /// <summary>A parking deck's ceiling fixture or roof pole (2026-10-05):
        /// a fluorescent strip, dimmer than a cobra-head; 0.6 with the
        /// ceilings concrete burnt the night interior white (the field is flat:
        /// every floor's lights land on the same texels).</summary>
        const float DeckLightGain = 0.07f;
        /// <summary>One flood of a high mast's ring (the bridges pass): six of
        /// them stand over one foot, so each throws a share.</summary>
        const float MastLightGain = 0.45f;
        /// <summary>(signs round 2) A guide sign's luminaire as a halo: a 175 W
        /// sign light is a fraction of a cobra-head (NightGlow.HaloSizeFor: a
        /// little over half the size).</summary>
        const float GuideLampHalo = 0.2f;

        /// <summary>A lamp post collider's square side: the drawn post's own
        /// (a cobra-head's 0.26 m, an acorn's 0.14 m). It was 0.3 m for both,
        /// 8 cm of solid round every uptown acorn post with nothing drawn
        /// there (INVISIBLE COLLIDERS, 2026-10-04).</summary>
        const float LampColliderM = 0.26f, AcornPostColliderM = 0.14f;

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

        static readonly Dictionary<Material, Material> barrierMats = new Dictionary<Material, Material>();
        /// <summary>The median barriers' concrete with a slope-scaled depth
        /// offset (PSX/Lit _ZOffF/_ZOffU): one copy per source material.</summary>
        static Material BarrierMat(Material src)
        {
            if (src == null || !src.HasProperty("_ZOffF")) return src;
            if (barrierMats.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + " (barrier)", hideFlags = HideFlags.DontSave };
            m.SetFloat("_ZOffF", -1f);
            m.SetFloat("_ZOffU", -1f);
            barrierMats[src] = m;
            return m;
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
            // a tile's roads and pavements mirror the world in the rain
            WetReflections.Tag(mr);
        }

        Material MatFor(CityMeshes.Slot slot)
        {
            int i = (int)slot;
            var table = materials;
            if (table == null) { var kit = CityKit.Get(); table = kit != null ? kit.slots : null; }
            if (table != null && i < table.Length && table[i] != null)
                return SeasonDress.Substitute(table[i]);
            // a kit built before the sidewalks: they wear the pavement concrete
            if (slot == CityMeshes.Slot.Sidewalk) return MatFor(CityMeshes.Slot.Pavement);
            // ...and one built before the bridges pass: the structure wears the concrete
            if (slot == CityMeshes.Slot.Structure) return MatFor(CityMeshes.Slot.Concrete);
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
