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
    /// of the wide view cone, past the far range) and still the sun shadow's
    /// caster, as on the stages.
    ///
    /// WHICH MODEL: by what the planting chose for the spot (CityTrees.Species,
    /// a street tree's clear stem, a young understory tree) and a hash of the
    /// foot - Big Oak for the old city's willow oaks and park/yard hero trees,
    /// the Forest beeches for woods and mixed hardwood, the Meadow poplars for
    /// the tulip-tree share and every street and median tree (a clear stem, the
    /// crown high), small beeches for young trees, grey willow for crape
    /// myrtles, Big Poplar's column at 3% of the hardwoods. Pines have no owned
    /// model: a Meadow poplar kept in leaf all year, darker, stands in.
    ///
    /// NOTHING THROUGH SOLIDS (psx-racing-no-foliage-through-solids): each tree
    /// is tested as a VOLUME - its model's crown radius in twelve height bands,
    /// at the scale it would stand - against the roads' clearance envelope
    /// (nothing within 4.6 m over a pavement, nothing into a deck from below),
    /// building footprints with their roofs (houses with a porch/roof margin),
    /// utility poles, their wires and cobra-heads, signal poles and mast arms,
    /// and every collider standing round it (lamp posts, sign posts, walls,
    /// skybridges). Larger first for a street tree (its crown climbs above the
    /// lane), then smaller; then another model of its kind, a poplar, a small
    /// beech; and if none fits, the tree is not planted - its trunk leaves the
    /// collider table with it (nothing solid that is not drawn).
    ///
    /// PSX_CITY_MESHTREES=0: the cards as before. Phones and the web never have
    /// the kit (CityTreeKit is PC content) and keep the cards.
    /// </summary>
    [DefaultExecutionOrder(1400)]
    public class CityMeshTrees : MonoBehaviour
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_CITY_MESHTREES") != "0";
        public static CityMeshTrees Instance { get; private set; }

        /// <summary>Plants a slot holds (a tile plants at most CityTrees.MaxPerTile,
        /// plus its street trees).</summary>
        const int PerSlot = 480;
        const int FirstSlots = 96;   // the baked city's 9 x 9 ring (PlatformProfile.CityRingBaked) and its anchors

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

        // ---- what the fitting did, since load (the tools print it) ----
        public static int Placed, Grown, Shrunk, Swapped, Dropped, DroppedSolid, Fitted;
        public static readonly int[] RejectBy = new int[6];
        public static readonly string[] RejectNames = { "road envelope", "building", "pole/wire", "signal", "collider", "deck" };
        public static double FitMs;
        public static int[] PlacedByRole = new int[8];

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
            Instance.Fit(key, tt);
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
        }

        void MakeForest(int slots)
        {
            if (forest != null) { if (Application.isPlaying) Destroy(forest.gameObject); else DestroyImmediate(forest.gameObject); }
            forest = MeshForest.CreatePool(null, kit.variants, slots, PerSlot, kit.lod0To, kit.lod1To, kit.meshRange, kit.farRange,
                                           CityTrees.DressNow, kit.cull);
            slotOf.Clear();
            if (forest == null) { Debug.LogWarning("[CityMeshTrees] no GPU forest: the city keeps its card trees."); return; }
            foreach (var k in plantsOf.Keys) if (!pending.Contains(k)) pending.Add(k);
        }

        void OnDestroy()
        {
            if (forest != null) { if (Application.isPlaying) Destroy(forest.gameObject); else DestroyImmediate(forest.gameObject); }
            if (Instance == this) Instance = null;
        }

        void LateUpdate() => Flush();

        /// <summary>Tiles given back, tiles stood up: the pool kept in step
        /// with the live city (every frame; an edit-mode tool calls it itself).</summary>
        public void Flush()
        {
            if (forest == null || world == null) return;
            // a tile dropped: its card renderer went with it
            List<long> gone = null;
            foreach (var kv in slotOf)
                if (!tileOf.TryGetValue(kv.Key, out var tr) || tr == null) (gone ??= new List<long>()).Add(kv.Key);
            if (gone != null)
                foreach (var k in gone) { forest.PoolRelease(slotOf[k]); slotOf.Remove(k); plantsOf.Remove(k); frames.Remove(k); tileOf.Remove(k); }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                long key = pending[i];
                if (!plantsOf.TryGetValue(key, out var plants)) { pending.RemoveAt(i); continue; }
                int tx = (int)(key >> 24), tz = (int)((key << 40) >> 40);
                var tile = world.transform.Find("Tile_" + tx + "_" + tz);
                if (tile == null) { if (Application.isPlaying && Time.frameCount % 120 == 0) { pending.RemoveAt(i); plantsOf.Remove(key); } continue; }
                var cardT = tile.Find("Trees");
                var cards = cardT != null ? cardT.GetComponents<Renderer>() : null;
                if (plants.Count == 0) { pending.RemoveAt(i); continue; }
                tileOf[key] = tile;   // (the slot is given back when the tile is destroyed)
                if (slotOf.TryGetValue(key, out int old)) { forest.PoolRelease(old); slotOf.Remove(key); }
                var c = new Vector3((tx + 0.5f) * CityMeshes.TileSize, 0f, (tz + 0.5f) * CityMeshes.TileSize);
                float lo = float.MaxValue, hi = float.MinValue, rad = 0f;
                foreach (var p in plants) { lo = Mathf.Min(lo, p.pos.y); hi = Mathf.Max(hi, p.pos.y + 30f * p.scale); }
                if (plants.Count == 0) { lo = 0f; hi = 1f; }
                c.y = (lo + hi) * 0.5f;
                rad = Mathf.Sqrt(2f) * CityMeshes.TileSize * 0.5f + 20f + (hi - lo) * 0.5f;
                int slot = forest.PoolFill(c, rad, cards, plants, out int kept);
                if (slot < 0)
                {
                    // full: twice the slots, every live tile again
                    int want = forest.PoolSlots * 2;
                    Debug.Log($"[CityMeshTrees] pool full at {forest.PoolSlots} tiles: growing to {want}");
                    MakeForest(want);
                    if (forest == null) return;
                    i = pending.Count;   // start over (MakeForest queued every tile)
                    continue;
                }
                slotOf[key] = slot;
                pending.RemoveAt(i);
            }
        }

        public MeshForest Forest => forest;
        public int LiveTiles => slotOf.Count;

        /// <summary>Plants in the live tiles, and their triangles at LOD0 (a bound).</summary>
        public int LivePlants() { int n = 0; foreach (var l in plantsOf.Values) n += l.Count; return n; }

        // ==================================================================
        //  THE FIT
        // ==================================================================

        public const float RoadClearM = 4.6f, DeckUnderM = 1.5f, Margin = 0.35f, BuildingMargin = 0.6f, HouseMargin = 1.6f;
        static readonly float[] StreetScales = { 1f, 1.15f, 1.3f, 1.45f, 0.9f };
        static readonly float[] Scales = { 1f, 0.86f, 0.74f, 0.62f };
        static readonly Collider[] hits = new Collider[256];

        struct RoadSeg { public Vector2 a, b; public float hw, y; }
        struct VSeg { public Vector2 a, b; public float y0a, y1a, y0b, y1b, r; public int why; }
        struct Box { public Vector2 c, u, v; public float hu, hv, y0, y1; public int why; }

        readonly List<RoadSeg> roads = new List<RoadSeg>(64);
        readonly List<VSeg> segs = new List<VSeg>(64);
        readonly List<Box> boxes = new List<Box>(64);
        readonly List<Collider> bigSolids = new List<Collider>(8);
        static readonly Collider[] bandHits = new Collider[64];
        readonly HashSet<int> segScratch = new HashSet<int>();
        readonly List<MeshForest.PoolPlant> cand = new List<MeshForest.PoolPlant>();

        /// <summary>Choose, size and fit every tree of a tile frame, and hold
        /// the result for the pool; trees that cannot be fitted leave the
        /// frame (and with them their trunks) before it stands up.</summary>
        public void Fit(long key, CityTrees.TreeTile tt)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var map = world.Map;
            Physics.SyncTransforms();
            TileBuildings(tt.tx, tt.tz);
            var plants = new List<MeshForest.PoolPlant>(tt.trees.Count);
            var keep = new List<CityTrees.Tree>(tt.trees.Count);
            int solids = 0;
            foreach (var t in tt.trees)
            {
                if (TryTree(map, tt, t, out var p)) { plants.Add(p); keep.Add(t); if (t.solid) solids++; Placed++; PlacedByRole[(int)kit.roles[p.variant]]++; }
                else { Dropped++; if (t.solid) DroppedSolid++; }
            }
            if (keep.Count != tt.trees.Count)
            {
                tt.trees.Clear();
                tt.trees.AddRange(keep);
                tt.solids = solids;
            }
            plantsOf[key] = plants;
            frames[key] = tt;
            if (!pending.Contains(key)) pending.Add(key);
            FitMs += clock.Elapsed.TotalMilliseconds;
            Fitted++;
        }

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
            bool young = t.dbh <= 0.125f && t.species != CityTrees.Species.Myrtle && t.species != CityTrees.Species.Bare && t.stem <= 0f;
            float roll = H(t.foot, 11);
            CityTreeKit.Role role; float want;
            if (t.stem > 0f) { role = CityTreeKit.Role.Poplar; want = t.h; }
            else if (young) { role = CityTreeKit.Role.Small; want = t.h * 1.2f; }
            else switch (t.species)
            {
                case CityTrees.Species.Oak: role = roll < 0.2f ? CityTreeKit.Role.Oak : roll < 0.78f ? CityTreeKit.Role.Broad : CityTreeKit.Role.Poplar; want = t.h * 1.35f; break;   // (Big Oak is 37.6k tris at its nearest: a share, not every willow oak)
                case CityTrees.Species.Hardwood: role = roll < 0.6f ? CityTreeKit.Role.Broad : roll < 0.95f ? CityTreeKit.Role.Poplar : roll < 0.98f ? CityTreeKit.Role.Oak : CityTreeKit.Role.Columnar; want = t.h * 1.3f; break;
                case CityTrees.Species.Pine: role = CityTreeKit.Role.PineStandIn; want = t.h * 1.1f; break;
                case CityTrees.Species.Myrtle: role = CityTreeKit.Role.Shrub; want = t.h * 0.9f; break;
                case CityTrees.Species.Sycamore: role = roll < 0.7f ? CityTreeKit.Role.Broad : CityTreeKit.Role.Poplar; want = t.h * 1.3f; break;
                default: role = CityTreeKit.Role.Snag; want = t.h * 1.3f; break;
            }
            Gather(map, tt, t.foot, 16f);
            float yaw = H(t.foot, 13) * 360f;
            bool first = true;
            foreach (var r in RoleChain(role))
            {
                var list = byRole[(int)r];
                if (list.Length == 0) continue;
                int start = Mathf.Min(list.Length - 1, (int)(H(t.foot, 17 + (int)r) * list.Length));
                for (int k = 0; k < list.Length; k++)
                {
                    int v = list[(start + k) % list.Length];
                    float s0 = want / Mathf.Max(0.5f, kit.variants[v].height);
                    s0 = Mathf.Clamp(s0, 0.45f, 1.7f);
                    var scales = t.stem > 0f ? StreetScales : Scales;
                    foreach (float f in scales)
                    {
                        float s = s0 * f;
                        if (s < 0.3f || s > 2.2f) continue;
                        if (t.stem > 0f && kit.crownBase[v] * kit.variants[v].height * s < t.stem - SinkFix) continue;
                        int why = Conflict(v, s, t.foot);
                        if (why >= 0) { RejectBy[why]++; continue; }
                        plant = new MeshForest.PoolPlant { pos = t.foot, yaw = yaw, scale = s, variant = v };
                        if (!first || k > 0) Swapped++;
                        if (f > 1f) Grown++; else if (f < 1f) Shrunk++;
                        return true;
                    }
                }
                first = false;
            }
            return false;
        }

        const float SinkFix = 0.3f;
        static int BigMask => (1 << CityWorld.SolidLayer) | (1 << CityWorld.RoadLayer) | 1;

        static IEnumerable<CityTreeKit.Role> RoleChain(CityTreeKit.Role r)
        {
            yield return r;
            if (r == CityTreeKit.Role.Shrub || r == CityTreeKit.Role.Snag) yield break;
            if (r != CityTreeKit.Role.Poplar && r != CityTreeKit.Role.PineStandIn) yield return CityTreeKit.Role.Poplar;
            if (r != CityTreeKit.Role.Small) yield return CityTreeKit.Role.Small;
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
            // the tile's own short list, made once a frame - TileBuildings)
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
                // the ground under the tree itself (a small lattice piece, a verge strip)
                if (bb.max.y < foot.y + 0.6f) continue;
                boxes.Add(new Box
                {
                    c = new Vector2(bb.center.x, bb.center.z), u = Vector2.right, v = Vector2.up,
                    hu = bb.extents.x + Margin, hv = bb.extents.z + Margin, y0 = bb.min.y - Margin, y1 = bb.max.y + Margin, why = 4,
                });
            }
        }

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
                    if (SegDist(p, r.a, r.b) < r.hw + R + Margin) return y1 < r.y ? 5 : 0;
                }
                foreach (var bx in boxes)
                {
                    if (y0 > bx.y1 || y1 < bx.y0) continue;
                    var q = p - bx.c;
                    float du = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, bx.u)) - bx.hu), dv = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(q, bx.v)) - bx.hv);
                    if (du * du + dv * dv < R * R) return bx.why;
                }
                foreach (var sg in segs)
                {
                    float t = SegT(p, sg.a, sg.b);
                    float sy0 = Mathf.Lerp(sg.y0a, sg.y0b, t), sy1 = Mathf.Lerp(sg.y1a, sg.y1b, t);
                    if (y0 > sy1 || y1 < sy0) continue;
                    if (SegDist(p, sg.a, sg.b) < R + sg.r + Margin) return sg.why;
                }
                if (bigSolids.Count > 0 && y1 > foot.y + 0.8f)
                {
                    float rr = R + Margin, lo = Mathf.Max(y0, foot.y + 0.8f);
                    var c0 = new Vector3(foot.x, lo + rr, foot.z);
                    var c1 = new Vector3(foot.x, Mathf.Max(lo + rr, y1 - rr), foot.z);
                    int n = Physics.OverlapCapsuleNonAlloc(c0, c1, rr, bandHits, BigMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++) if (bigSolids.Contains(bandHits[i])) return 4;
                }
                if (bigSolids.Count > 0 && y1 > foot.y + 0.8f)
                {
                    float rr = R + Margin, lo = Mathf.Max(y0, foot.y + 0.8f);
                    var c0 = new Vector3(foot.x, lo + rr, foot.z);
                    var c1 = new Vector3(foot.x, Mathf.Max(lo + rr, y1 - rr), foot.z);
                    int n = Physics.OverlapCapsuleNonAlloc(c0, c1, rr, bandHits, BigMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++) if (bigSolids.Contains(bandHits[i])) return 4;
                }
            }
            return -1;
        }

        static float SegT(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float L2 = d.sqrMagnitude;
            return L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, a + (b - a) * SegT(p, a, b));

        public static void ResetCounts()
        {
            Placed = Grown = Shrunk = Swapped = Dropped = DroppedSolid = Fitted = 0; FitMs = 0;
            System.Array.Clear(RejectBy, 0, RejectBy.Length);
            System.Array.Clear(PlacedByRole, 0, PlacedByRole.Length);
        }
    }
}
