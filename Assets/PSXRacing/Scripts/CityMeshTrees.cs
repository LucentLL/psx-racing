using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S TREES AS MESH TREES (PC profile, 2026-11-11). Owner: "Why are
    /// you still using old tree cards when I've specifically asked to use
    /// Mountain/Meadow dynamic trees, plus free Poplar and Oak from assets."
    ///
    /// The city still plants (or reads off the bake) every tree where it always
    /// did - street trees in their grates, median rows, yards, lots, verges, the
    /// canopy woods and the freeway tree walls (CityTrees) - and the trunk table
    /// still stops the car at each solid one. What changes is how they are
    /// DRAWN: as the owner's pack models through the mountain forest's GPU path
    /// (MeshForest, streamed: MeshForest.Pool.cs), one slot a live tile, with
    /// each model's own LODs and its own impostor at distance. The tile's card
    /// mesh stays as the chunk's cards: drawn only where the slot is not (out
    /// of the wide view cone, past the far range, and until the tile's fit is
    /// done) and still the sun shadow's caster, as on the stages.
    ///
    /// WHICH MODEL: by what the planting chose for the spot (CityTrees.Species,
    /// a street tree's clear stem, a young understory tree) and a hash of the
    /// foot - Big Oak for a share of the old city's willow oaks, the Forest
    /// beeches for woods and mixed hardwood, the Meadow poplars for the
    /// tulip-tree share, small beeches for young trees, grey willow for crape
    /// myrtles, Big Poplar's column at 3% of the hardwoods. Pines have no owned
    /// model: a Meadow poplar kept in leaf all year, darker, stands in.
    /// ROUND 2: every STREET tree is a street form (CityTreeKit.Role.Street:
    /// an owned poplar or beech with its lowest branch rings cut, its trunk
    /// drawn taller and its crown narrowed - vase or column), sized to its
    /// gap; any other tree that meets a wall tries those forms before a
    /// smaller tree, a bush or nothing (round 1 dropped 72% of Uptown's trees).
    ///
    /// NOTHING THROUGH SOLIDS (psx-racing-no-foliage-through-solids): each tree
    /// is tested as a VOLUME - its model's crown radius in twelve height bands,
    /// at the scale it would stand - against the roads' clearance envelope
    /// (nothing within 4.6 m over a pavement, nothing into a deck from below),
    /// building footprints with their roofs (houses with a porch/roof margin),
    /// utility poles, their wires and cobra-heads, signal poles and mast arms,
    /// and every collider standing round it (lamp posts, sign posts, walls,
    /// skybridges). If none fits, the tree is not planted - its trunk leaves
    /// the collider table with it (nothing solid that is not drawn).
    ///
    /// ROUND 2: THE FIT IS SLICED. A tile's frame is queued when it stands up
    /// (its cards and trunks stand as they always did) and fitted a few
    /// milliseconds a frame (<see cref="SliceMs"/>); when it is done its slot
    /// is filled, its cards hand over, and its trunk table is replaced if a
    /// tree moved or went. PSX_CT_SLICE_MS=0: the whole tile in its frame (round 1).
    ///
    /// PSX_CITY_MESHTREES=0: the cards as before. Phones and the web never have
    /// the kit (CityTreeKit is PC content) and keep the cards.
    /// </summary>
    [DefaultExecutionOrder(1400)]
    public class CityMeshTrees : MonoBehaviour
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_CITY_MESHTREES") != "0";
        public static CityMeshTrees Instance { get; private set; }

        /// <summary>Milliseconds of fitting a frame may spend (play mode); 0 = a
        /// whole tile in the frame it stands up (round 1).</summary>
        public static float SliceMs = ReadF("PSX_CT_SLICE_MS", 3f);
        /// <summary>Slots filled a frame at most (a pool's growth refills every
        /// live tile: spread over frames, cards meanwhile).</summary>
        public static int FillsPerFrame = 4;
        /// <summary>ROUND 1's fit, for the before/after frames: no street forms,
        /// the old collider skip, the old capsule test.</summary>
        public static bool Round1Mode = System.Environment.GetEnvironmentVariable("PSX_CT_ROUND1") == "1";

        static float ReadF(string k, float d)
        {
            var s = System.Environment.GetEnvironmentVariable(k);
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : d;
        }

        /// <summary>Plants a slot holds (a tile plants at most CityTrees.MaxPerTile,
        /// plus its street trees).</summary>
        const int PerSlot = 480;
        const int FirstSlots = 128;   // the baked city's 9 x 9 ring (PlatformProfile.CityRingBaked), its anchors and the tiles leaving

        CityTreeKit kit;
        CityWorld world;
        MeshForest forest;
        readonly Dictionary<long, List<MeshForest.PoolPlant>> plantsOf = new Dictionary<long, List<MeshForest.PoolPlant>>();
        readonly Dictionary<long, int> slotOf = new Dictionary<long, int>();
        readonly Dictionary<long, Transform> tileOf = new Dictionary<long, Transform>();
        readonly List<long> pending = new List<long>();
        readonly Dictionary<long, CityTrees.TreeTile> frames = new Dictionary<long, CityTrees.TreeTile>();
        /// <summary>The tree frame a tile was fitted from (tools: its poles and signals).</summary>
        public CityTrees.TreeTile FrameOf(long key) => frames.TryGetValue(key, out var f) ? f : null;
        public CityWorld World => world;
        public CityTreeKit Kit => kit;
        int[][] byRole;

        /// <summary>A tile's fit in progress (sliced across frames).</summary>
        sealed class Job
        {
            public long key;
            public CityTrees.TreeTile tt;
            public List<CityTrees.Tree> src;
            public int next;
            public List<MeshForest.PoolPlant> plants;
            public List<CityTrees.Tree> keep;
            public int solids;
            public bool changed;
            public double ms;
            public int frames;
        }
        readonly List<Job> jobs = new List<Job>();
        public int JobsPending => jobs.Count;

        // ---- what the fitting did, since load (the tools print it) ----
        public static int Placed, Grown, Shrunk, Swapped, Dropped, DroppedSolid, Fitted;
        public static int StreetIn, StreetDropped, StreetAsStreetForm, StreetAsShrub, StreetAsOther, NarrowedOther;
        public static readonly int[] RejectBy = new int[6];
        public static readonly string[] RejectNames = { "road envelope", "building", "pole/wire", "signal", "collider", "deck" };
        /// <summary>Dropped trees by the last thing that refused them: [street 0/1][why 0-5, 6 = no size meets the clear stem].</summary>
        public static readonly int[,] DropWhy = new int[2, 7];
        public static readonly List<string> DropSamples = new List<string>();
        public static double FitMs, FitMsMaxFrame, FitMsMaxTile, TreeMsMax;
        /// <summary>Volume tests one tree may take (sizes x species x places) before it
        /// is given up: what bounds a frame's fitting past its budget.</summary>
        public const int MaxTests = 160;
        public static int TestsMax, CappedTrees;
        int tests;
        bool awayKnown;
        double frameSyncMs;
        public static int[] PlacedByRole = new int[16];

        // ---- the per-frame trace (play mode, PSX_CT_TRACE=path or a perf tour) ----
        static System.Text.StringBuilder trace;
        static string tracePath;
        double frameFitMs;
        int frameFills;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            CityWorld.TreeFrameReady -= OnTreeFrame;
            CityWorld.TreeFrameReady += OnTreeFrame;
        }

        /// <summary>This machine draws the city's trees as meshes.</summary>
        public static bool On =>
            Enabled && PlatformProfile.PC && !Application.isMobilePlatform && SystemInfo.supportsComputeShaders &&
            CityTreeKit.Get() != null;

        /// <summary>For an edit-mode tool: hook up to this world now (play mode
        /// does it by itself on the first tree frame).</summary>
        public static CityMeshTrees EnsureFor(CityWorld w)
        {
            Hook();
            if (!On || w == null) return null;
            if (Instance != null && Instance.world == w) return Instance;
            if (Instance != null) DestroyImmediate(Instance.gameObject);
            var go = new GameObject("CityMeshTrees") { hideFlags = HideFlags.DontSave };
            var c = go.AddComponent<CityMeshTrees>();
            c.Setup(w);
            return c.forest != null ? c : null;
        }

        static void OnTreeFrame(long key, CityTrees.TreeTile tt)
        {
            if (tt == null || !On) return;
            if (Instance == null)
            {
                var w = CityWorld.Active;
                if (w == null || !Application.isPlaying) return;
                EnsureFor(w);
                if (Instance == null) return;
            }
            Instance.Queue(key, tt);
        }

        void Setup(CityWorld w)
        {
            Instance = this;
            world = w;
            kit = CityTreeKit.Get();
            int nr = System.Enum.GetValues(typeof(CityTreeKit.Role)).Length;
            var lists = new List<int>[nr];
            for (int r = 0; r < nr; r++) lists[r] = new List<int>();
            for (int v = 0; v < kit.variants.Length; v++) lists[(int)kit.roles[v]].Add(v);
            byRole = new int[nr][];
            for (int r = 0; r < nr; r++) byRole[r] = lists[r].ToArray();
            MakeForest(FirstSlots);
            string tp = System.Environment.GetEnvironmentVariable("PSX_CT_TRACE");
            if (string.IsNullOrEmpty(tp) && PerfTour.Active)
            {
                string od = System.Environment.GetEnvironmentVariable("PSX_PERFOUT");
                if (!string.IsNullOrEmpty(od)) tp = System.IO.Path.Combine(od, "ctrees_trace.csv");
            }
            if (!string.IsNullOrEmpty(tp) && Application.isPlaying)
            {
                tracePath = tp;
                trace = new System.Text.StringBuilder("frame,time,dt_ms,fit_ms,fills,jobs,live_tiles,slots,slots_used,plants,car_x,car_z,mesh_on,sync_ms\n");
            }
        }

        void MakeForest(int slots)
        {
            if (forest != null)
            {
                // the old forest's slots given back first: each tile's cards stand up again
                foreach (var kv in slotOf) forest.PoolRelease(kv.Value);
                if (Application.isPlaying) Destroy(forest.gameObject); else DestroyImmediate(forest.gameObject);
            }
            float l0 = kit.lod0To, l1 = kit.lod1To, mr = kit.meshRange, fr = kit.farRange;
            if (LodOverride.x > 0f) { l0 = LodOverride.x; l1 = LodOverride.y; mr = LodOverride.z; }
            forest = MeshForest.CreatePool(null, kit.variants, slots, PerSlot, l0, l1, mr, fr, CityTrees.DressNow, kit.cull);
            slotOf.Clear();
            if (forest == null) { Debug.LogWarning("[CityMeshTrees] no GPU forest: the city keeps its card trees."); return; }
            foreach (var k in plantsOf.Keys) if (!pending.Contains(k)) pending.Add(k);
        }

        void OnDestroy()
        {
            if (forest != null) { if (Application.isPlaying) Destroy(forest.gameObject); else DestroyImmediate(forest.gameObject); }
            if (Instance == this) Instance = null;
            WriteTrace();
        }

        void OnApplicationQuit() => WriteTrace();

        static bool traceStarted;
        /// <summary>The trace so far appended to its file (every few seconds:
        /// a tour may end the editor without a quit message).</summary>
        static void WriteTrace()
        {
            if (trace == null || string.IsNullOrEmpty(tracePath) || trace.Length == 0) return;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tracePath));
                if (!traceStarted) System.IO.File.WriteAllText(tracePath, trace.ToString());
                else System.IO.File.AppendAllText(tracePath, trace.ToString());
                traceStarted = true;
                trace.Clear();
            }
            catch (System.Exception e) { Debug.Log("[CityMeshTrees] trace write failed: " + e.Message); }
        }

        /// <summary>PSX_CT_FLIP=seconds: F8's A/B flipped on a timer (a tour's
        /// pictures show both).</summary>
        static readonly float FlipEvery = ReadF("PSX_CT_FLIP", 0f);
        float nextFlip = -1f;

        void LateUpdate()
        {
            Work(SliceMs > 0f ? SliceMs : float.MaxValue);
            Flush(FillsPerFrame);
            if (frameFitMs > FitMsMaxFrame) FitMsMaxFrame = frameFitMs;
            if (FlipEvery > 0f && Application.isPlaying)
            {
                if (nextFlip < 0f) nextFlip = Time.realtimeSinceStartup + FlipEvery;
                else if (Time.realtimeSinceStartup > nextFlip)
                {
                    nextFlip = Time.realtimeSinceStartup + FlipEvery;
                    MeshForest.MeshTreesOn = !MeshForest.MeshTreesOn;
                    Debug.Log("[CityMeshTrees] F8 flip: mesh trees " + (MeshForest.MeshTreesOn ? "ON" : "OFF (cards)") + " at frame " + Time.frameCount);
                }
            }
            if (trace != null)
            {
                var car = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                trace.Append(Time.frameCount).Append(',').Append(Time.realtimeSinceStartup.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                     .Append((Time.unscaledDeltaTime * 1000f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                     .Append(frameFitMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                     .Append(frameFills).Append(',').Append(jobs.Count).Append(',').Append(slotOf.Count).Append(',')
                     .Append(forest != null ? forest.PoolSlots : 0).Append(',').Append(forest != null ? forest.PoolUsed : 0).Append(',')
                     .Append(LivePlants()).Append(',').Append(car.x.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                     .Append(car.z.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(MeshForest.MeshTreesOn ? 1 : 0).Append(',').Append(frameSyncMs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                if (Time.frameCount % 300 == 0) WriteTrace();
            }
            // (reset after the record: an unsliced fit runs in the city's own update, before this)
            frameFitMs = 0; frameFills = 0; frameSyncMs = 0;
        }

        /// <summary>A tile's tree frame stood up: its fit queued (or, unsliced,
        /// done now - before the frame stands up, as round 1 did).</summary>
        public void Queue(long key, CityTrees.TreeTile tt)
        {
            for (int i = jobs.Count - 1; i >= 0; i--) if (jobs[i].key == key) jobs.RemoveAt(i);
            var job = new Job
            {
                key = key, tt = tt, src = new List<CityTrees.Tree>(tt.trees),
                plants = new List<MeshForest.PoolPlant>(tt.trees.Count), keep = new List<CityTrees.Tree>(tt.trees.Count),
            };
            frames[key] = tt;
            // unsliced (round 1, play mode): the whole tile now, before it stands up. An
            // edit-mode tool queues it like play mode and Flush() fits it after the
            // stand-up: the tile's sign posts and lamps are colliders by then (the
            // audit's one tree through a CityPost was fitted before its post stood)
            if (SliceMs <= 0f && Application.isPlaying)
            {
                Step(job, float.MaxValue);
                Finish(job, false);
                return;
            }
            jobs.Add(job);
        }

        /// <summary>Fit queued tiles for up to <paramref name="budgetMs"/>.</summary>
        void Work(float budgetMs)
        {
            if (jobs.Count == 0 || world == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // the physics scene brought up to date once a frame (a tile that just stood
            // up dirties thousands of transforms: its cost is the stand-up's, timed apart)
            Physics.SyncTransforms();
            double sync = clock.Elapsed.TotalMilliseconds;
            frameSyncMs += sync;
            clock.Restart();
            while (jobs.Count > 0 && clock.Elapsed.TotalMilliseconds < budgetMs)
            {
                var job = jobs[0];
                if (!TileAlive(job.key)) { jobs.RemoveAt(0); frames.Remove(job.key); continue; }
                Step(job, budgetMs - (float)clock.Elapsed.TotalMilliseconds);
                if (job.next >= job.src.Count) { jobs.RemoveAt(0); Finish(job, true); }
            }
        }

        bool TileAlive(long key)
        {
            int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
            return world != null && world.transform.Find("Tile_" + tx + "_" + tz) != null;
        }

        /// <summary>Fit trees of a job until done or out of time.</summary>
        void Step(Job job, float budgetMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var map = world.Map;
            if (!Application.isPlaying || SliceMs <= 0f) Physics.SyncTransforms();
            TileBuildings(job.tt.tx, job.tt.tz);
            job.frames++;
            while (job.next < job.src.Count)
            {
                // (a frame's budget half gone: the next tree waits - one tree is at most MaxTests tests)
                if (budgetMs < float.MaxValue && clock.Elapsed.TotalMilliseconds > budgetMs * 0.6f && job.next > 0) break;
                var t = job.src[job.next++];
                if (t.street) StreetIn++;
                var tc = System.Diagnostics.Stopwatch.StartNew();
                tests = 0;
                bool ok = TryTree(map, job.tt, t, out var p);
                double tms = tc.Elapsed.TotalMilliseconds;
                if (tms > TreeMsMax) TreeMsMax = tms;
                if (tests > TestsMax) TestsMax = tests;
                if (ok)
                {
                    job.plants.Add(p);
                    var kt = t; kt.foot = p.pos;
                    if ((p.pos - t.foot).sqrMagnitude > 1e-4f) job.changed = true;
                    job.keep.Add(kt);
                    if (t.solid) job.solids++;
                    Placed++;
                    var role = kit.roles[p.variant];
                    PlacedByRole[(int)role]++;
                    if (t.street)
                    {
                        if (role == CityTreeKit.Role.Street) StreetAsStreetForm++;
                        else if (role == CityTreeKit.Role.Shrub) StreetAsShrub++;
                        else StreetAsOther++;
                    }
                    else if (role == CityTreeKit.Role.Street) NarrowedOther++;
                }
                else
                {
                    job.changed = true;
                    Dropped++; if (t.solid) DroppedSolid++;
                    if (t.street) StreetDropped++;
                    int why = lastWhy >= 0 ? lastWhy : 6;
                    DropWhy[t.street ? 1 : 0, why]++;
                    if (t.street && DropSamples.Count < 60)
                    {
                        // where a grate tree stands against its road: off the pavement's edge by (negative: on it)
                        float off = 99f; string rn = "";
                        foreach (var r in roads) { float d = SegDist(new Vector2(t.foot.x, t.foot.z), r.a, r.b) - (r.hw - 0.3f); if (d < off) { off = d; rn = $"w {2f * (r.hw - 0.3f):0.0} m, y {r.y - t.foot.y:+0.0;-0.0}"; } }
                        DropSamples.Add($"  grate {off:0.00} m off the nearest pavement ({rn})");
                    }
                    if (DropSamples.Count < 60)
                        DropSamples.Add($"{(t.street ? "STREET" : t.species.ToString())} h{t.h:0.0} at ({t.foot.x:0.0}, {t.foot.y:0.0}, {t.foot.z:0.0}): {(why < 6 ? RejectNames[why] : "no size meets the clear stem")}{(lastHit != null && why == 4 ? " (" + lastHit + ")" : "")}");
                }
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
            double ms = clock.Elapsed.TotalMilliseconds;
            job.ms += ms; FitMs += ms; frameFitMs += ms;
        }

        /// <summary>A tile's fit done: its frame rewritten (trees that went,
        /// trees that moved), its trunks replaced if it already stood, its
        /// plants queued for a slot.</summary>
        void Finish(Job job, bool stood)
        {
            var tt = job.tt;
            tt.trees.Clear();
            tt.trees.AddRange(job.keep);
            int oldSolids = tt.solids;
            tt.solids = job.solids;
            if (stood && (job.changed || oldSolids != job.solids) && world.Trunks != null && TileAlive(job.key))
                world.Trunks.AddTable(job.key, tt.Trunks());
            plantsOf[job.key] = job.plants;
            if (!pending.Contains(job.key)) pending.Add(job.key);
            Fitted++;
            if (job.ms > FitMsMaxTile) FitMsMaxTile = job.ms;
        }

        /// <summary>Tiles given back, tiles stood up: the pool kept in step
        /// with the live city (every frame; an edit-mode tool calls it itself,
        /// which also finishes every fit still queued).</summary>
        public void Flush() { Work(float.MaxValue); Flush(int.MaxValue); }

        void Flush(int maxFills)
        {
            if (forest == null || world == null) return;
            // a tile dropped: its card renderer went with it
            List<long> gone = null;
            foreach (var kv in slotOf)
                if (!tileOf.TryGetValue(kv.Key, out var tr) || tr == null) (gone ??= new List<long>()).Add(kv.Key);
            if (gone != null)
                foreach (var k in gone) { forest.PoolRelease(slotOf[k]); slotOf.Remove(k); plantsOf.Remove(k); frames.Remove(k); tileOf.Remove(k); }
            int fills = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (fills >= maxFills) break;
                long key = pending[i];
                if (!plantsOf.TryGetValue(key, out var plants)) { pending.RemoveAt(i); continue; }
                int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
                var tile = world.transform.Find("Tile_" + tx + "_" + tz);
                if (tile == null) { if (Application.isPlaying && Time.frameCount % 120 == 0) { pending.RemoveAt(i); plantsOf.Remove(key); frames.Remove(key); } continue; }
                var cardT = tile.Find("Trees");
                var cards = cardT != null ? cardT.GetComponents<Renderer>() : null;
                if (plants.Count == 0) { pending.RemoveAt(i); continue; }
                tileOf[key] = tile;   // (the slot is given back when the tile is destroyed)
                if (slotOf.TryGetValue(key, out int old)) { forest.PoolRelease(old); slotOf.Remove(key); }
                var c = new Vector3((tx + 0.5f) * CityMeshes.TileSize, 0f, (tz + 0.5f) * CityMeshes.TileSize);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in plants) { lo = Mathf.Min(lo, p.pos.y); hi = Mathf.Max(hi, p.pos.y + kit.variants[p.variant].height * p.scale); }
                c.y = (lo + hi) * 0.5f;
                float rad = Mathf.Sqrt(2f) * CityMeshes.TileSize * 0.5f + 20f + (hi - lo) * 0.5f;
                int slot = forest.PoolFill(c, rad, cards, plants, out int kept);
                if (slot < 0)
                {
                    // full: twice the slots, every live tile again (spread over the frames to come)
                    int want = forest.PoolSlots * 2;
                    Debug.Log($"[CityMeshTrees] pool full at {forest.PoolSlots} tiles: growing to {want}");
                    PoolGrowths++;
                    MakeForest(want);
                    if (forest == null) return;
                    i = pending.Count;   // start over (MakeForest queued every tile)
                    continue;
                }
                fills++; frameFills++;
                slotOf[key] = slot;
                pending.RemoveAt(i);
            }
        }

        public static int PoolGrowths;
        /// <summary>The MEDIUM level distances in place of the kit's (tools'
        /// pop measure; x = 0: the kit's own).</summary>
        public static Vector3 LodOverride = Vector3.zero;

        /// <summary>The forest made again (new distances or FOREST DETAIL),
        /// every fitted tile refilled.</summary>
        public void Rebuild() { MakeForest(forest != null ? forest.PoolSlots : FirstSlots); Flush(); }
        public MeshForest Forest => forest;
        public int LiveTiles => slotOf.Count;

        /// <summary>Plants in the live tiles.</summary>
        public int LivePlants() { int n = 0; foreach (var kv in plantsOf) if (slotOf.ContainsKey(kv.Key)) n += kv.Value.Count; return n; }

        // ==================================================================
        //  THE FIT
        // ==================================================================

        public const float RoadMargin = 0.05f;
        /// <summary>CityMeshes.StreetDress's TreeLaneClearM: a grate's trunk stands at
        /// least this far off the DRAWN pavement of every road near it.</summary>
        public const float GrateLaneClearM = 0.95f;

        /// <summary>The pavement's half width as drawn, for a GRATE tree whose
        /// trunk is <paramref name="trunkD"/> from the road's line: the map's
        /// width is the road's model, and where its drawn edge is narrower (trims,
        /// sections) the street dress stepped the grate out from the drawn curb, not
        /// the model's - 17% of Uptown/Graham grates stand 0.4-0.8 m inside the
        /// model's half width. The dress kept the trunk GrateLaneClearM off the
        /// drawn pavement, so the drawn edge is no farther out than that. (The
        /// city foliage audit takes the same edge for grate trees.)</summary>
        public static float GratePave(float modelHalf, float trunkD) => Mathf.Min(modelHalf, trunkD - GrateLaneClearM);
        /// <summary>Street trees refused by the road envelope, by the crown band that met it.</summary>
        public static readonly int[] RoadBand = new int[32];
        int lastBand;
        bool fitStreet;
        public const float RoadClearM = 4.6f, DeckUnderM = 1.5f, Margin = 0.35f, BuildingMargin = 0.6f, HouseMargin = 1.6f;
        static readonly float[] StreetScales = { 1f, 1.15f, 1.3f, 0.9f, 0.8f, 0.7f, 0.6f, 0.52f, 1.45f, 1.6f };   // (last: taller, its crown higher over a wide street)
        static readonly float[] StreetScalesR1 = { 1f, 1.15f, 1.3f, 1.45f, 0.9f, 0.8f, 0.7f };
        static readonly float[] Scales = { 1f, 0.86f, 0.74f, 0.62f, 0.52f, 0.44f };
        static readonly Collider[] hits = new Collider[256];

        struct RoadSeg { public Vector2 a, b; public float hw, y; }
        struct VSeg { public Vector2 a, b; public float y0a, y1a, y0b, y1b, r; public int why; }
        struct Box { public Vector2 c, u, v; public float hu, hv, y0, y1; public int why; public string name; }
        /// <summary>Candidates refused by a collider, by its object's name (the tools print it).</summary>
        public static readonly Dictionary<string, int> RejectColliders = new Dictionary<string, int>();
        string lastHit;
        int lastWhy;
        Vector2 lastAway;
        public static readonly List<string> DiagLines = new List<string>();

        readonly List<RoadSeg> roads = new List<RoadSeg>(64);
        readonly List<VSeg> segs = new List<VSeg>(64);
        readonly List<Box> boxes = new List<Box>(64);
        readonly List<Collider> bigSolids = new List<Collider>(8);
        static readonly Collider[] bandHits = new Collider[64];
        readonly HashSet<int> segScratch = new HashSet<int>();

        /// <summary>The plants a tile frame was given (tools).</summary>
        public List<MeshForest.PoolPlant> PlantsOf(long key) => plantsOf.TryGetValue(key, out var l) ? l : null;
        public IEnumerable<long> FittedKeys => plantsOf.Keys;

        readonly List<CityBuildings.B> tileBlds = new List<CityBuildings.B>(256);

        /// <summary>The buildings any crown of tile (tx, tz) could reach: its own
        /// and its neighbours' within 40 m of it.</summary>
        void TileBuildings(int tx, int tz)
        {
            tileBlds.Clear();
            var blds = world.Buildings;
            if (blds == null) return;
            float x0 = tx * CityMeshes.TileSize - 40f, z0 = tz * CityMeshes.TileSize - 40f, x1 = x0 + CityMeshes.TileSize + 80f, z1 = z0 + CityMeshes.TileSize + 80f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    long k = ((long)(tx + dx) << 24) ^ ((tz + dz) & 0xFFFFFF);
                    if (!blds.TryGetValue(k, out var list)) continue;
                    foreach (var b in list)
                    {
                        float r = Mathf.Max(b.w, b.d);
                        if (b.pos.x + r < x0 || b.pos.x - r > x1 || b.pos.y + r < z0 || b.pos.y - r > z1) continue;
                        tileBlds.Add(b);
                    }
                }
        }

        static float H(Vector3 f, int salt) => CityTrees.Hash01(Mathf.RoundToInt(f.x * 4f), Mathf.RoundToInt(f.z * 4f), salt);

        bool TryTree(CityMap map, CityTrees.TreeTile tt, in CityTrees.Tree t, out MeshForest.PoolPlant plant)
        {
            plant = default;
            lastAway = Vector2.zero;
            lastWhy = -1; lastHit = null;
            fitStreet = t.street;
            bool young = t.dbh <= 0.125f && t.species != CityTrees.Species.Myrtle && t.species != CityTrees.Species.Bare && t.stem <= 0f;
            float roll = H(t.foot, 11);
            CityTreeKit.Role role; float want;
            bool streetForms = !Round1Mode && byRole.Length > (int)CityTreeKit.Role.Street && byRole[(int)CityTreeKit.Role.Street].Length > 0;
            if (t.stem > 0f) { role = streetForms ? CityTreeKit.Role.Street : CityTreeKit.Role.Poplar; want = streetForms ? t.h * 1.1f : t.h; }
            else if (young) { role = CityTreeKit.Role.Small; want = t.h * 1.2f; }
            else switch (t.species)
            {
                case CityTrees.Species.Oak: role = roll < 0.2f ? CityTreeKit.Role.Oak : roll < 0.78f ? CityTreeKit.Role.Broad : CityTreeKit.Role.Poplar; want = t.h * 1.35f; break;   // (Big Oak is the heaviest: a share, not every willow oak)
                case CityTrees.Species.Hardwood: role = roll < 0.6f ? CityTreeKit.Role.Broad : roll < 0.95f ? CityTreeKit.Role.Poplar : roll < 0.98f ? CityTreeKit.Role.Oak : CityTreeKit.Role.Columnar; want = t.h * 1.3f; break;
                case CityTrees.Species.Pine: role = CityTreeKit.Role.PineStandIn; want = t.h * 1.1f; break;
                case CityTrees.Species.Myrtle: role = CityTreeKit.Role.Shrub; want = t.h * 0.9f; break;
                case CityTrees.Species.Sycamore: role = roll < 0.7f ? CityTreeKit.Role.Broad : CityTreeKit.Role.Poplar; want = t.h * 1.3f; break;
                default: role = CityTreeKit.Role.Snag; want = t.h * 1.3f; break;
            }
            float yaw = H(t.foot, 13) * 360f;
            // MOVE before giving up: up to 3.5 m away from the wall the crown met
            // (the trunk, and its collider, go with it). A street tree stays in its grate.
            var away = Vector2.zero;
            int moves = t.street && !Round1Mode ? 1 : 7;
            int firstWhy = -1; string firstHit = null;
            for (int mv = 0; mv < moves; mv++)
            {
                var foot = t.foot;
                if (mv > 0)
                {
                    if (away.sqrMagnitude < 0.5f) break;
                    // straight away, then 45 degrees either side; 2 m, then 3.5 m
                    float ang = mv == 1 || mv == 4 ? 0f : mv == 2 || mv == 5 ? 45f : -45f;
                    var dir2 = (Vector2)(Quaternion.Euler(0f, 0f, ang) * (Vector3)away.normalized);
                    var q = new Vector2(t.foot.x, t.foot.z) + dir2 * (mv < 4 ? 2f : 3.5f);
                    if (!GroundAt(q, t.foot.y, out float gy)) continue;
                    foot = new Vector3(q.x, gy - CityTrees.SinkM, q.y);
                }
                if (TryAt(map, tt, t, role, want, yaw, foot, out plant)) { if (mv > 0) Moved++; return true; }
                if (mv == 0) { away = lastAway; firstWhy = lastWhy; firstHit = lastHit; }
            }
            // (the drop is booked on what refused the tree where it was planted)
            if (firstWhy >= 0) { lastWhy = firstWhy; lastHit = firstHit; }
            return false;
        }

        /// <summary>The ground (not a road, not a roof) under a point near the
        /// height <paramref name="nearY"/>.</summary>
        static bool GroundAt(Vector2 q, float nearY, out float y)
        {
            y = nearY;
            int n = Physics.RaycastNonAlloc(new Vector3(q.x, nearY + 12f, q.y), Vector3.down, rayHits, 30f, 1 | (1 << CityWorld.RoadLayer) | (1 << CityWorld.SolidLayer), QueryTriggerInteraction.Ignore);
            float best = float.MinValue; Collider what = null;
            for (int i = 0; i < n; i++) if (rayHits[i].point.y > best) { best = rayHits[i].point.y; what = rayHits[i].collider; }
            // the topmost thing there must be ground: not a road, not a building, wall or post
            if (what == null || what.gameObject.layer == CityWorld.SolidLayer || what.gameObject.layer == CityWorld.RoadLayer ||
                what.gameObject.name == "Roads" || what.gameObject.name == "Kerbs" || Mathf.Abs(best - nearY) > 2.5f) return false;
            y = best;
            return true;
        }
        static readonly RaycastHit[] rayHits = new RaycastHit[16];
        public static int Moved;

        Vector2 AwayFromBox(Vector2 p)
        {
            foreach (var bx in boxes)
            {
                var q = p - bx.c;
                float du = Mathf.Abs(Vector2.Dot(q, bx.u)) - bx.hu, dv = Mathf.Abs(Vector2.Dot(q, bx.v)) - bx.hv;
                if (du > 6f || dv > 6f) continue;
                return du > dv ? bx.u * Mathf.Sign(Vector2.Dot(q, bx.u)) : bx.v * Mathf.Sign(Vector2.Dot(q, bx.v));
            }
            return Vector2.zero;
        }

        bool TryAt(CityMap map, CityTrees.TreeTile tt, in CityTrees.Tree t, CityTreeKit.Role role, float want, float yaw, Vector3 foot, out MeshForest.PoolPlant plant)
        {
            plant = default;
            Gather(map, tt, foot, 16f);
            awayKnown = false;
            bool first = true;
            foreach (var r in RoleChain(role, t.street))
            {
                var list = byRole[(int)r];
                if (list.Length == 0) continue;
                int start = Mathf.Min(list.Length - 1, (int)(H(t.foot, 17 + (int)r) * list.Length));
                // a street form stands for whatever was planted: as tall as the card wanted
                float w = r == CityTreeKit.Role.Street && role != CityTreeKit.Role.Street ? t.h * 1.1f : want;
                for (int k = 0; k < list.Length; k++)
                {
                    int v = list[(start + k) % list.Length];
                    float s0 = w / Mathf.Max(0.5f, kit.variants[v].height);
                    s0 = Mathf.Clamp(s0, 0.45f, 1.7f);
                    var scales = t.stem > 0f ? (Round1Mode ? StreetScalesR1 : StreetScales) : Scales;
                    foreach (float f in scales)
                    {
                        float s = s0 * f;
                        if (s < 0.3f || s > 2.2f) continue;
                        if (t.stem > 0f && kit.crownBase[v] * kit.variants[v].height * s < t.stem - StemSlack) continue;
                        if (!Round1Mode && ++tests > MaxTests) { CappedTrees++; return false; }
                        int why = Conflict(v, s, foot);
                        if (why == 1 || why == 4) { if (why == 1) lastAway = AwayFromBox(new Vector2(foot.x, foot.z)); }
                        if (why >= 0)
                        {
                            RejectBy[why]++; lastWhy = why;
                            if (why == 0 && t.street) RoadBand[Mathf.Clamp(lastBand, 0, RoadBand.Length - 1)]++;
                            if (why == 4 && lastHit != null) { RejectColliders.TryGetValue(lastHit, out int rc); RejectColliders[lastHit] = rc + 1; }
                            continue;
                        }
                        plant = new MeshForest.PoolPlant { pos = foot, yaw = yaw, scale = s, variant = v };
                        if (!first || k > 0) Swapped++;
                        if (f > 1f) Grown++; else if (f < 1f) Shrunk++;
                        return true;
                    }
                }
                first = false;
            }
            return false;
        }

        /// <summary>A street tree's crown may start this much under the card's clear stem
        /// (the road envelope still holds 4.6 m over the pavement on the lane side).</summary>
        const float StemSlack = 1.2f;
        static int BigMask => (1 << CityWorld.SolidLayer) | (1 << CityWorld.RoadLayer) | 1;

        static IEnumerable<CityTreeKit.Role> RoleChain(CityTreeKit.Role r, bool street)
        {
            yield return r;
            if (r == CityTreeKit.Role.Shrub || r == CityTreeKit.Role.Snag) yield break;
            if (Round1Mode)
            {
                if (r != CityTreeKit.Role.Poplar && r != CityTreeKit.Role.PineStandIn) yield return CityTreeKit.Role.Poplar;
                if (r != CityTreeKit.Role.Small) yield return CityTreeKit.Role.Small;
                yield return CityTreeKit.Role.Shrub;
                yield break;
            }
            // a street tree: its forms first (r), then a poplar, and only then smaller
            if (r != CityTreeKit.Role.Poplar && r != CityTreeKit.Role.PineStandIn && r != CityTreeKit.Role.Small && r != CityTreeKit.Role.Street) yield return CityTreeKit.Role.Poplar;
            // any other tree against a wall: the narrow, limbed-up forms before a smaller tree
            if (r != CityTreeKit.Role.Street) yield return CityTreeKit.Role.Street;
            if (street && r == CityTreeKit.Role.Street) yield return CityTreeKit.Role.Poplar;
            if (r != CityTreeKit.Role.Small) yield return CityTreeKit.Role.Small;
            // last: a multi-stem bush where no tree's crown clears what stands round it
            yield return CityTreeKit.Role.Shrub;
        }

        /// <summary>Everything a crown standing at <paramref name="foot"/> could
        /// meet within <paramref name="reach"/>: road pieces, building boxes,
        /// pole and wire segments, signal poles and arms, colliders.</summary>
        void Gather(CityMap map, CityTrees.TreeTile tt, Vector3 foot, float reach)
        {
            roads.Clear(); segs.Clear(); boxes.Clear(); bigSolids.Clear();
            var p = new Vector2(foot.x, foot.z);
            // ROADS: every segment within reach, with its pavement half width and height there
            segScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * (reach + 25f), p + Vector2.one * (reach + 25f), segScratch);
            foreach (int packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.tunnel) continue;
                Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                float L2 = d.sqrMagnitude;
                float tt0 = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float hw = e.width * 0.5f + 0.3f;
                if (Vector2.Distance(p, a + d * tt0) > reach + hw) continue;
                roads.Add(new RoadSeg { a = a, b = b, hw = hw, y = e.YAt(e.s[si] + Mathf.Sqrt(L2) * tt0) });
            }
            // BUILDINGS of this tile and its neighbours (data: drawn or not yet;
            // the tile's own short list - TileBuildings)
            foreach (var b in tileBlds)
            {
                float r = Mathf.Max(b.w, b.d) * 0.75f;
                if ((b.pos - p).sqrMagnitude > (reach + r + 3f) * (reach + r + 3f)) continue;
                float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                bool house = b.gable || CityProps.IsHome(b.kind);
                float m = house ? HouseMargin : BuildingMargin;
                float roof = b.gable ? 0.45f * Mathf.Min(b.w, b.d) : 0f;
                boxes.Add(new Box
                {
                    c = b.pos, u = new Vector2(cy, -sy), v = new Vector2(sy, cy), hu = b.w * 0.5f + m, hv = b.d * 0.5f + m,
                    y0 = foot.y - 3f, y1 = foot.y + b.h + roof + (house ? 1.5f : 1f), why = 1,
                });
            }
            // POLES, WIRES AND COBRA-HEADS (this tile's)
            if (tt.poles != null)
            {
                foreach (var pl in tt.poles.poles)
                {
                    var f2 = new Vector2(pl.foot.x, pl.foot.z);
                    if ((f2 - p).sqrMagnitude > (reach + 4f) * (reach + 4f)) continue;
                    segs.Add(new VSeg { a = f2, b = f2, y0a = pl.foot.y, y1a = pl.top + 0.3f, y0b = pl.foot.y, y1b = pl.top + 0.3f, r = 0.45f, why = 2 });
                    if (pl.lens != Vector3.zero)
                        segs.Add(new VSeg { a = f2, b = new Vector2(pl.lens.x, pl.lens.z), y0a = pl.lens.y - 0.6f, y1a = pl.top + 0.3f, y0b = pl.lens.y - 0.6f, y1b = pl.lens.y + 0.8f, r = 0.6f, why = 2 });
                }
                foreach (var (pa, pb) in tt.poles.spans)
                {
                    Vector2 a = new Vector2(pa.foot.x, pa.foot.z), b = new Vector2(pb.foot.x, pb.foot.z);
                    if (SegDist(p, a, b) > reach + 1f) continue;
                    float drop = 0.4f + 1.2f * Mathf.Max(1, pa.levels - 1) + 1.2f;   // the levels and the sag
                    segs.Add(new VSeg { a = a, b = b, y0a = pa.top - drop, y1a = pa.top + 0.2f, y0b = pb.top - drop, y1b = pb.top + 0.2f, r = 0.4f, why = 2 });
                }
            }
            // SIGNAL POLES AND THEIR MAST ARMS (each head to its nearest pole)
            if (tt.signals != null)
            {
                foreach (var (sf, top) in tt.signals.poles)
                {
                    var f2 = new Vector2(sf.x, sf.z);
                    if ((f2 - p).sqrMagnitude > (reach + 20f) * (reach + 20f)) continue;
                    segs.Add(new VSeg { a = f2, b = f2, y0a = sf.y, y1a = top + 0.5f, y0b = sf.y, y1b = top + 0.5f, r = 0.5f, why = 3 });
                }
                foreach (var h in tt.signals.heads)
                {
                    var h2 = new Vector2(h.pos.x, h.pos.z);
                    if ((h2 - p).sqrMagnitude > (reach + 20f) * (reach + 20f)) continue;
                    float best = 22f; Vector3 bf = Vector3.zero; float btop = 0f;
                    foreach (var (sf, top) in tt.signals.poles)
                    {
                        float dd = Vector2.Distance(new Vector2(sf.x, sf.z), h2);
                        if (dd < best) { best = dd; bf = sf; btop = top; }
                    }
                    var a = best < 22f ? new Vector2(bf.x, bf.z) : h2;
                    float y1 = Mathf.Max(btop, h.pos.y + 0.8f) + 0.4f;
                    segs.Add(new VSeg { a = a, b = h2, y0a = h.pos.y - 1.4f, y1a = y1, y0b = h.pos.y - 1.4f, y1b = y1, r = 0.9f, why = 3 });
                }
            }
            // COLLIDERS standing round it (lamp posts, sign posts, walls, skybridges ...)
            int mask = (1 << CityWorld.SolidLayer) | (1 << CityWorld.RoadLayer) | 1;
            int n = Physics.OverlapCapsuleNonAlloc(foot + Vector3.down * 2f, foot + Vector3.up * 45f, reach, hits, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = hits[i];
                if (col == null || col.name == CityTrees.TrunkName) continue;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                var bb = col.bounds;
                string nm = col.gameObject.name;
                if (nm == "Ground" || nm == "Kerbs" || nm == "Roads" || nm == "Banks" || nm == "Water") continue;   // (the road envelope is the data's)
                // a tile-wide collider (its buildings, barriers, guardrails, structures):
                // its box would be the whole tile - asked of the physics, band by band
                if ((bb.extents.x > 14f && bb.extents.z > 14f) || bb.extents.x > 40f || bb.extents.z > 40f)
                {
                    if (!bigSolids.Contains(col)) bigSolids.Add(col);
                    continue;
                }
                // the ground under the tree itself (a small lattice piece, a verge strip).
                // ROUND 2 (THE AUDIT'S 11): round 1 let every small collider lower than
                // 0.6 m over the foot go, and the audit counts crown points from 0.4 m -
                // Big Oak's low limbs and flare met a low wall or kerb block standing
                // 0.4-0.6 m high. Now only what lies under LowestCrown is ground.
                if (bb.max.y < foot.y + (Round1Mode ? 0.6f : LowestCrown)) continue;
                boxes.Add(new Box
                {
                    c = new Vector2(bb.center.x, bb.center.z), u = Vector2.right, v = Vector2.up,
                    hu = bb.extents.x + Margin, hv = bb.extents.z + Margin, y0 = bb.min.y - Margin, y1 = bb.max.y + Margin, why = 4, name = nm + "/" + LayerMask.LayerToName(col.gameObject.layer),
                });
            }
        }

        /// <summary>A crown's points this low over the foot are its roots and
        /// flare in the ground (the audit skips under 0.4 m).</summary>
        public const float LowestCrown = 0.3f;

        /// <summary>The first thing (RejectNames) a crown of species
        /// <paramref name="v"/> at scale <paramref name="s"/> standing at
        /// <paramref name="foot"/> passes into, or -1.</summary>
        int Conflict(int v, float s, Vector3 foot)
        {
            var p = new Vector2(foot.x, foot.z);
            float hgt = kit.variants[v].height * s;
            for (int b = 0; b < CityTreeKit.Bands; b++)
            {
                float R = kit.CrownR(v, b) * s;
                if (R < 0.02f) continue;
                float y0 = foot.y + hgt * b / CityTreeKit.Bands, y1 = foot.y + hgt * (b + 1) / CityTreeKit.Bands;
                foreach (var r in roads)
                {
                    if (y0 > r.y + RoadClearM || y1 < r.y - DeckUnderM) continue;
                    // (hw is already 0.3 m past the pavement: the trunk of a grate tree a metre off the curb fits)
                    // a GRATE tree's trunk stands on the walk by design: held off the pavement
                    // itself by 0.1 m (the audit's line), not by the general 0.35 m past it
                    float dd = SegDist(p, r.a, r.b);
                    float clear = Round1Mode ? r.hw + Margin : fitStreet ? GratePave(r.hw - 0.3f, dd) + 0.1f : r.hw + RoadMargin;
                    if (dd < clear + R) { lastBand = b; return y1 < r.y ? 5 : 0; }
                }
                foreach (var bx in boxes)
                {
                    if (y0 > bx.y1 || y1 < bx.y0) continue;
                    var q = p - bx.c;
                    float du = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, bx.u)) - bx.hu), dv = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, bx.v)) - bx.hv);
                    if (du * du + dv * dv < R * R) { lastHit = bx.name; return bx.why; }
                }
                foreach (var sg in segs)
                {
                    float t = SegT(p, sg.a, sg.b);
                    float sy0 = Mathf.Lerp(sg.y0a, sg.y0b, t), sy1 = Mathf.Lerp(sg.y1a, sg.y1b, t);
                    if (y0 > sy1 || y1 < sy0) continue;
                    if (SegDist(p, sg.a, sg.b) < R + sg.r + Margin) return sg.why;
                }
                if (bigSolids.Count > 0 && y1 > foot.y + LowestCrown && BigHit(foot, R + Margin, Mathf.Max(y0, foot.y + LowestCrown), y1, v, s, b, out var hit))
                {
                    lastHit = "BIG " + hit.gameObject.name + "/" + LayerMask.LayerToName(hit.gameObject.layer);
                    return 4;
                }
            }
            return -1;
        }

        static readonly Quaternion Eighth = Quaternion.Euler(0f, 45f, 0f);

        /// <summary>A tile-wide solid in the crown's band: the band is a
        /// cylinder (radius <paramref name="rr"/>, <paramref name="lo"/> to
        /// <paramref name="hi"/>). ROUND 2: asked as two boxes, one turned 45
        /// degrees - their common part is an octagon round the cylinder, so
        /// nothing in the band is missed (round 1's capsule was a sphere where
        /// the band was shorter than its width: it missed the band's rim).</summary>
        bool BigHit(Vector3 foot, float rr, float lo, float hi, int v, float s, int b, out Collider hit)
        {
            hit = null;
            if (Round1Mode)
            {
                var c0 = new Vector3(foot.x, lo + rr, foot.z);
                var c1 = new Vector3(foot.x, Mathf.Max(lo + rr, hi - rr), foot.z);
                int n0 = Physics.OverlapCapsuleNonAlloc(c0, c1, rr, bandHits, BigMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n0; i++) if (bigSolids.Contains(bandHits[i])) { hit = bandHits[i]; break; }
            }
            else
            {
                var centre = new Vector3(foot.x, (lo + hi) * 0.5f, foot.z);
                var half = new Vector3(rr, Mathf.Max(0.01f, (hi - lo) * 0.5f), rr);
                int n1 = Physics.OverlapBoxNonAlloc(centre, half, bandHits, Quaternion.identity, BigMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n1 && hit == null; i++)
                {
                    var c = bandHits[i];
                    if (!bigSolids.Contains(c)) continue;
                    // the same solid in the turned box too (an octagon about the cylinder)
                    int n2 = Physics.OverlapBoxNonAlloc(centre, half, bandHits2, Eighth, BigMask, QueryTriggerInteraction.Ignore);
                    for (int j = 0; j < n2; j++) if (bandHits2[j] == c) { hit = c; break; }
                }
            }
            if (hit == null) return false;
            // the way away from it: the nearest of 16 rays out from the band's middle
            // (once a place: 16 rays into a tile-wide mesh collider were most of a dear tree's cost)
            if (awayKnown && DiagLines.Count >= 40) return true;
            awayKnown = true;
            float bd = 99f; Vector3 bdir = Vector3.zero; float hy = (lo + hi) * 0.5f;
            for (int k = 0; k < 16; k++)
            {
                var dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                if (hit.Raycast(new Ray(new Vector3(foot.x, hy, foot.z), dir), out var rh0, rr + 1.5f) && rh0.distance < bd) { bd = rh0.distance; bdir = dir; }
            }
            if (bd < 99f) lastAway = new Vector2(-bdir.x, -bdir.z);
            if (DiagLines.Count < 40)
                DiagLines.Add($"BIG {hit.name} foot ({foot.x:0.0},{foot.y:0.0},{foot.z:0.0}) v{v} s{s:0.00} band {b} y {lo - foot.y:0.0}-{hi - foot.y:0.0} rr {rr:0.0}: nearest ray hit {bd:0.0} m");
            return true;
        }
        static readonly Collider[] bandHits2 = new Collider[64];

        static float SegT(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float L2 = d.sqrMagnitude;
            return L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, a + (b - a) * SegT(p, a, b));

        public static void ResetCounts()
        {
            Placed = Grown = Shrunk = Swapped = Dropped = DroppedSolid = Fitted = Moved = 0; FitMs = 0; FitMsMaxFrame = 0; FitMsMaxTile = 0; TreeMsMax = 0; TestsMax = 0; CappedTrees = 0;
            StreetIn = StreetDropped = StreetAsStreetForm = StreetAsShrub = StreetAsOther = NarrowedOther = 0;
            System.Array.Clear(RejectBy, 0, RejectBy.Length);
            System.Array.Clear(PlacedByRole, 0, PlacedByRole.Length);
            System.Array.Clear(DropWhy, 0, DropWhy.Length);
            System.Array.Clear(RoadBand, 0, RoadBand.Length);
            DropSamples.Clear();
            RejectColliders.Clear();
        }
    }
}
