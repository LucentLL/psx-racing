using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    // =====================================================================
    //  THE COVERAGE REPORT (roads pass A1, 2026-10-02): the CityAudit hook.
    // =====================================================================
    public static partial class CityAudit
    {
        /// <summary>
        /// COVERAGE (plan A1): holes, coplanar double cover, pavement outside
        /// its outline, branch overshoot and underlaps, rasterised off the
        /// BUILT tile meshes (<see cref="CityCoverage"/>), plus the data-gap
        /// census of dead ends just short of another road (critic C9) and the
        /// tap identity check (critic C15). Boxed unless PSX_AUDIT_FULL=1
        /// (<see cref="ScopeFor"/>, NAME = COVER). Every number is REPORT
        /// state; the one Check is the tap identity, an invariant of the
        /// instrument itself (the tap must never change what a tile draws).
        /// </summary>
        static partial void CoverageReport(CityMap map, CityMeshes.Trims trims)
        {
            var sc = ScopeFor("COVER");
            Line("");
            Line("COVERAGE (roads pass A1: the built road meshes rasterised by owner; report only) - " + sc.Describe());
            bool tapSame = CityCoverage.TapIdentity(map, trims, AuditBuildings, out string tapDetail);
            Check(tapSame, "COVERAGE TAP IDENTITY: tiles built with CityMeshes.RecordTap on and off are identical", tapDetail);
            var res = CityCoverage.Run(map, trims, AuditBuildings, sc, Line);
            CityCoverage.DeadEnds(map, trims, Line, res);
            CityCoverage.WriteOutputs(res, null);
            JunctionsReport(map, trims, sc);   // roads pass L7
        }
    }

    /// <summary>
    /// JUNCTION CLUSTERS before the plan's A11 (one intersection drawn as
    /// several fans): fan (patch) nodes joined by an edge the two fans
    /// swallow (its ribbon shorter than 0.6 m) or by a MEDIAN CROSSING of
    /// <see cref="MedianCrossM"/> m or less - an edge whose two ends each sit
    /// between the two one-way carriageways of one named divided road
    /// running opposite ways (diag/junctions cluster.mjs, the replica the
    /// counts are compared with). The coverage outline takes the convex hull
    /// of a cluster's fan rings as its junction area; the turning-movement
    /// audit drives from every arm entering a cluster to every arm leaving it.
    /// </summary>
    public static class CityJunctionClusters
    {
        /// <summary>The plan's A1 bound (the exporter's junction box is 30 m;
        /// the replica cluster.mjs used 40 m).</summary>
        public const float MedianCrossM = 30f;

        public sealed class Set
        {
            /// <summary>Cluster index of each node, or -1.</summary>
            public int[] clusterOf;
            /// <summary>Each cluster's member nodes (2 or more fans).</summary>
            public List<int>[] members;
            /// <summary>Each cluster's tier (its best member's).</summary>
            public int[] tier;
            /// <summary>Tier of each node: the best (lowest) of its arms'.</summary>
            public int[] nodeTier;
        }

        static Set cached; static CityMap cachedMap; static CityMeshes.Trims cachedTrims;

        public static Set Of(CityMap map, CityMeshes.Trims trims)
        {
            if (cached != null && cachedMap == map && cachedTrims == trims) return cached;
            int nn = map.nodes.Length;
            var set = new Set { clusterOf = new int[nn], nodeTier = new int[nn] };
            for (int n = 0; n < nn; n++)
            {
                int t = 3;
                foreach (int ei in map.nodeEdges[n]) t = Mathf.Min(t, CityTier.Of(map.edges[ei]));
                set.nodeTier[n] = t;
            }
            var parent = new int[nn];
            for (int i = 0; i < nn; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var e in map.edges)
            {
                if (e.a == e.b || !trims.patch[e.a] || !trims.patch[e.b]) continue;
                float rib = e.length - trims.atA[e.index] - trims.atB[e.index];
                if (rib < 0.6f || (e.length <= MedianCrossM && MedianCrossing(map, e)))
                    parent[Find(e.a)] = Find(e.b);
            }
            var groups = new Dictionary<int, List<int>>();
            for (int n = 0; n < nn; n++)
            {
                if (!trims.patch[n]) continue;
                int r = Find(n);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                g.Add(n);
            }
            var list = new List<List<int>>();
            for (int n = 0; n < nn; n++) set.clusterOf[n] = -1;
            foreach (var g in groups.Values)
            {
                if (g.Count < 2) continue;
                g.Sort();
                foreach (int n in g) set.clusterOf[n] = list.Count;
                list.Add(g);
            }
            list.Sort((a, b) => a[0].CompareTo(b[0]));
            for (int c = 0; c < list.Count; c++) foreach (int n in list[c]) set.clusterOf[n] = c;
            set.members = list.ToArray();
            set.tier = new int[list.Count];
            for (int c = 0; c < list.Count; c++)
            {
                int t = 3;
                foreach (int n in list[c]) t = Mathf.Min(t, set.nodeTier[n]);
                set.tier[c] = t;
            }
            cached = set; cachedMap = map; cachedTrims = trims;
            return set;
        }

        static Vector2 OutDir(CityMap.Edge e, int node) => e.a == node ? e.TangentAt(0f) : -e.TangentAt(e.length);

        /// <summary>The one-way carriageway pairs of one road through node n
        /// (one in, one out, opposite), the crossing edge M left out.</summary>
        static void CarriagewayPairs(CityMap map, int n, CityMap.Edge M, List<(CityMap.Edge a, CityMap.Edge b)> into)
        {
            into.Clear();
            var arms = map.nodeEdges[n];
            for (int i = 0; i < arms.Count; i++)
                for (int j = i + 1; j < arms.Count; j++)
                {
                    var a = map.edges[arms[i]]; var b = map.edges[arms[j]];
                    if (a == M || b == M || !a.oneway || !b.oneway || a.link || b.link || a.a == a.b || b.a == b.b) continue;
                    if (Vector2.Dot(OutDir(a, n), OutDir(b, n)) < -0.85f && ((a.b == n) != (b.b == n))) into.Add((a, b));
                }
        }

        static readonly List<(CityMap.Edge, CityMap.Edge)> pa = new List<(CityMap.Edge, CityMap.Edge)>(), pb = new List<(CityMap.Edge, CityMap.Edge)>();

        /// <summary>Does edge M cross the median of one divided road (both its
        /// ends between that road's two carriageways, which run opposite ways)?</summary>
        public static bool MedianCrossing(CityMap map, CityMap.Edge M)
        {
            CarriagewayPairs(map, M.a, M, pa);
            if (pa.Count == 0) return false;
            CarriagewayPairs(map, M.b, M, pb);
            foreach (var p in pa)
                foreach (var q in pb)
                {
                    if (p.Item1.name != q.Item1.name) continue;
                    var d1 = OutDir(p.Item1.a == M.a ? p.Item1 : p.Item2, M.a);
                    var d2 = OutDir(q.Item1.a == M.b ? q.Item1 : q.Item2, M.b);
                    return Vector2.Dot(d1, d2) <= -0.7f;
                }
            return false;
        }
    }

    /// <summary>
    /// THE PAVEMENT COVERAGE AUDIT (roads pass A1, 2026-10-02). The owner:
    /// "There should be no gaps in roads. Connections should be intentional.
    /// Not short of merging, not going too far where texture pop-up happens
    /// later." Nothing measured either: the drive audit probes lanes along
    /// edges on nine tiles, the overlap census ignores anything within 25 cm
    /// in height, and the launch audit left samples on grass out of its counts.
    ///
    /// Every tile in scope is BUILT (CityMeshes.Build, the tap on so each road
    /// triangle has an OWNER: the edge whose ribbon or deck it is, the node
    /// whose fan, the host whose gore quad) and its up-facing road and ground
    /// triangles are rasterised in plan, 0.25 m cells, 0.10 m inside junction
    /// and gore neighbourhoods (a fan node's trim + the arm's reach + 5 m; a
    /// gore quad + 5 m), keeping the top four surfaces of each cell. Against
    /// them, the INTENDED OUTLINE: each edge's LaneExtents footprint between
    /// its trims (squeeze, ease and clip applied, the sections on the polyline
    /// vertex bisectors as the builder cuts them), every fan's triangles, every
    /// gore quad, and each junction cluster's convex hull
    /// (<see cref="CityJunctionClusters"/>; before A11 one intersection is
    /// several fans, so the hull stands for its junction box).
    ///
    ///   COPLANAR   two different owners within 0.05 m in height (the same
    ///              edge's consecutive spans and paint columns are one owner),
    ///              by relation: fan/fan (same cluster, other), fan over its
    ///              own arm, over a cluster mate's arm, over a foreign road;
    ///              arm/arm (two ribbons of one node), branch/host (a clip
    ///              pair), ribbon/ribbon; gore/road. Z-fight and texture pop.
    ///   HOLES      outline cells (1-cell erosion) with no road surface within
    ///              0.6 m of the outline's height, by outline kind (ribbon,
    ///              deck, fan, gore, cluster box).
    ///   OUTSIDE    road surface more than 0.25 m beyond every outline at its
    ///              height, by surface kind.
    ///   OVERSHOOT  a clipped branch's surface over its host's (within 0.6 m),
    ///              and of it the cells past the host's far lane edge.
    ///   UNDERLAP   a surface 0.005-0.08 m under a road surface (road piece,
    ///              verge/seam strip or the lattice): flicker at range.
    ///
    /// Every count by tier (CityTier; a pair takes the better tier). Writes
    /// city_coverage.txt / .json at the project root. In CityAudit.Run as the
    /// CoverageReport hook (boxed unless PSX_AUDIT_FULL=1); headless:
    /// -executeMethod PSXRacing.EditorTools.CityCoverage.RunHeadless.
    /// </summary>
    public static class CityCoverage
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- constants -----------------------------------------------------
        const float CoarseCell = 0.25f, FineCell = 0.10f, BlockM = 8f;
        const int Apron = 3, K = 6, OL = 4;
        public const float CoplanarDy = 0.05f, LevelTol = 0.6f, OutsideM = 0.25f;
        public const float UnderMin = 0.005f, UnderMax = 0.08f, HoodPadM = 5f;

        // surface / outline kinds
        public const int KRibbon = 0, KDeck = 1, KFan = 2, KGore = 3, KStrip = 4, KLattice = 5, KCluster = 6;
        static readonly string[] KindName = { "ribbon", "deck", "fan", "gore", "strip", "lattice", "cluster box" };
        static bool IsRoad(int kind) => kind <= KGore;

        // packed owner key: kind (3 bits) | tier (2 bits) | id (26 bits)
        static int Key(int kind, int tier, int id) => (kind << 28) | ((tier & 3) << 26) | (id & 0x3FFFFFF);
        static int KindOf(int key) => (key >> 28) & 7;
        static int TierOf(int key) => (key >> 26) & 3;
        static int IdOf(int key) => key & 0x3FFFFFF;
        /// <summary>Ribbon and deck of one edge are one owner.</summary>
        static int OwnerClass(int key) { int k = KindOf(key); return k == KDeck ? KRibbon : k; }
        static bool SameOwner(int a, int b) => OwnerClass(a) == OwnerClass(b) && IdOf(a) == IdOf(b);

        // COPLANAR relations
        public const int RFanFanCluster = 0, RFanFan = 1, RFanOwnArm = 2, RFanClusterArm = 3, RFanForeign = 4,
                         RArmArm = 5, RBranchHost = 6, RRibbon = 7, RGore = 8, RMitre = 9, NRel = 10;
        static readonly string[] RelName = { "fan/fan same cluster", "fan/fan", "fan over own arm", "fan over cluster arm", "fan over foreign road",
                                             "arm/arm (one node)", "branch/host (clip)", "ribbon/ribbon", "gore/road", "mitre seam (through pair)" };
        static readonly string[] RelKey = { "fanFanCluster", "fanFan", "fanOwnArm", "fanClusterArm", "fanForeign", "armArm", "branchHost", "ribbonRibbon", "goreRoad", "mitreSeam" };
        static readonly string[] HoleKey = { "ribbon", "deck", "fan", "gore", "", "", "cluster" };
        static readonly string[] UnderName = { "road piece", "verge/seam strip", "lattice" };
        static readonly string[] UnderKey = { "road", "strip", "lattice" };

        // ---- results -------------------------------------------------------
        public sealed class Tallies
        {
            public readonly double[,] coplanar = new double[4, NRel];
            public readonly double[] coplanarDeck = new double[4];
            public readonly double[,] holes = new double[4, 7];
            public readonly double[,] outside = new double[4, 4];
            public readonly double[,] underlap = new double[4, 3];
            public readonly double[] overshoot = new double[4], overshootFar = new double[4];
            public readonly double[] road = new double[4], outline = new double[4];
        }

        public sealed class Acc { public double area; public float x, z; public int tier; public int a, b; public float depth; }

        public sealed class Result
        {
            public string scope; public bool full;
            public int tilesEval, tilesBuilt, untapped, fineBlocks, coarseBlocks;
            public double seconds; public long cells;
            public readonly Tallies all = new Tallies();
            public readonly Dictionary<string, Tallies> boxes = new Dictionary<string, Tallies>();
            public readonly Dictionary<long, Acc> holeObj = new Dictionary<long, Acc>();
            public readonly Dictionary<long, Acc> copObj = new Dictionary<long, Acc>();
            public readonly Dictionary<long, Acc> outObj = new Dictionary<long, Acc>();
            public readonly Dictionary<long, Acc> underObj = new Dictionary<long, Acc>();
            public readonly Dictionary<long, Acc> overObj = new Dictionary<long, Acc>();
            public int clusters, clusterT1, clusterT2, clusterT3;
            public int[] deadShort = new int[4], deadInside = new int[4], deadEnds = new int[4];
            public readonly List<string> deadRows = new List<string>();
            public readonly List<string> lines = new List<string>();
        }

        /// <summary>The sub-boxes counted apart in a city-wide run, so a
        /// package's boxed run (the default box is CityAudit.OwnerBox) has a
        /// baseline to compare with.</summary>
        static readonly (string name, Rect box)[] SubBoxes =
        {
            ("OwnerBox", CityAudit.OwnerBox),
            ("W5th", Rect.MinMaxRect(-3550f, 5750f, -3150f, 6150f)),
        };

        // ---- per-tile record ----------------------------------------------
        sealed class TileRec
        {
            public int tx, tz;
            public float[] t = new float[9 * 1024]; public int[] key = new int[1024]; public int n;      // surfaces
            public float[] o = new float[9 * 512]; public int[] okey = new int[512]; public float[] oslack = new float[512]; public int on;   // outline
            public readonly List<Vector3> hoods = new List<Vector3>();   // x, z, r (gore neighbourhoods)
            public void Add(Vector3 a, Vector3 b, Vector3 c, int k)
            {
                if (n == key.Length) { Array.Resize(ref key, n * 2); Array.Resize(ref t, 9 * n * 2); }
                int i = 9 * n;
                t[i] = a.x; t[i + 1] = a.y; t[i + 2] = a.z; t[i + 3] = b.x; t[i + 4] = b.y; t[i + 5] = b.z; t[i + 6] = c.x; t[i + 7] = c.y; t[i + 8] = c.z;
                key[n++] = k;
            }
            public void AddO(Vector3 a, Vector3 b, Vector3 c, int k, float slack)
            {
                if (on == okey.Length) { Array.Resize(ref okey, on * 2); Array.Resize(ref oslack, on * 2); Array.Resize(ref o, 9 * on * 2); }
                int i = 9 * on;
                o[i] = a.x; o[i + 1] = a.y; o[i + 2] = a.z; o[i + 3] = b.x; o[i + 4] = b.y; o[i + 5] = b.z; o[i + 6] = c.x; o[i + 7] = c.y; o[i + 8] = c.z;
                oslack[on] = slack; okey[on++] = k;
            }
        }

        // ---- context for one run -------------------------------------------
        sealed class Ctx
        {
            public CityMap map; public CityMeshes.Trims trims; public Dictionary<long, List<CityBuildings.B>> buildings;
            public CityAudit.AuditScope sc; public Result res;
            public int[] edgeTier; public CityJunctionClusters.Set cl;
            public readonly HashSet<long> clipPairs = new HashSet<long>();
            public readonly Dictionary<long, List<Vector3>> nodeHoods = new Dictionary<long, List<Vector3>>();   // per tile: x, z, r
            public readonly Dictionary<long, List<int>> hullByTile = new Dictionary<long, List<int>>();
            public readonly List<(Vector2[] tri, float y, float slack, int key)> hulls = new List<(Vector2[], float, float, int)>();
        }

        static long TK(int tx, int tz) => ((long)tz << 32) | (uint)(tx + 100000);
        static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        // =====================================================================
        //  Headless
        // =====================================================================
        [MenuItem("PSX Racing/City Coverage Audit")]
        public static void RunHeadless()
        {
            var log = new StringBuilder();
            void Line(string s) { log.AppendLine(s); Debug.Log("[Coverage] " + s); }
            int status = 1;
            Result res = null;
            try
            {
                var map = CityMap.Get();
                if (map == null) throw new Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                var sc = CityAudit.ScopeFor("COVER");
                Line("CITY COVERAGE AUDIT - graph " + map.graphHash.ToString("x8") + " - " + sc.Describe());
                bool same = TapIdentity(map, trims, buildings, out string detail);
                Line((same ? "  ok  " : "  FAIL ") + "COVERAGE TAP IDENTITY: tiles built with CityMeshes.RecordTap on and off are identical - " + detail);
                res = Run(map, trims, buildings, sc, Line);
                DeadEnds(map, trims, Line, res);
                status = same ? 0 : 1;
            }
            catch (Exception ex) { Line("THREW " + ex); Debug.LogException(ex); }
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "city_coverage.txt"), log.ToString());
            if (res != null) WriteOutputs(res, null);
            // leftover item 3 (internal): the built meshes under PSX_DUMP_BOX, in the same run
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PSX_DUMP_BOX"))) CityAudit.DumpRoads();
            if (Application.isBatchMode) EditorApplication.Exit(status);
        }

        // =====================================================================
        //  The tap identity check (critic C15)
        // =====================================================================
        /// <summary>Four tiles of the owner's box (W 5th, uptown, I-277's
        /// twin viaduct, the e2437 pair) built with the tap off and on: every
        /// mesh's vertices, UVs and triangles must be the same numbers.</summary>
        public static bool TapIdentity(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, out string detail)
        {
            var spots = new[] { new Vector2(-3336f, 5913f), map.uptown, new Vector2(-2125f, 5962f), new Vector2(-984f, 4472f) };
            bool was = CityMeshes.RecordTap;
            int same = 0, n = 0; var bad = new List<string>();
            try
            {
                foreach (var p in spots)
                {
                    int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
                    CityMeshes.RecordTap = false;
                    var a = CityMeshes.Build(map, trims, buildings, tx, tz);
                    CityMeshes.RecordTap = true;
                    var b = CityMeshes.Build(map, trims, buildings, tx, tz);
                    n++;
                    string why = MeshesDiffer(a, b);
                    if (why == null) same++; else bad.Add($"tile {tx},{tz}: {why}");
                    Destroy(a); Destroy(b);
                }
            }
            finally { CityMeshes.RecordTap = was; }
            detail = $"{same} of {n} tiles identical" + (bad.Count > 0 ? "; " + string.Join("; ", bad) : "");
            return same == n;
        }

        static string MeshesDiffer(CityMeshes.TileMeshes a, CityMeshes.TileMeshes b)
        {
            foreach (var (ma, mb, name) in new[] { (a.roads, b.roads, "roads"), (a.ground, b.ground, "ground"), (a.barriers, b.barriers, "barriers"),
                                                   (a.kerbs, b.kerbs, "kerbs"), (a.water, b.water, "water"), (a.buildings, b.buildings, "buildings") })
            {
                if ((ma == null) != (mb == null)) return name + " present in one build only";
                if (ma == null) continue;
                if (ma.vertexCount != mb.vertexCount || ma.subMeshCount != mb.subMeshCount) return $"{name} {ma.vertexCount}/{mb.vertexCount} verts, {ma.subMeshCount}/{mb.subMeshCount} submeshes";
                var va = ma.vertices; var vb = mb.vertices;
                for (int i = 0; i < va.Length; i++) if (va[i] != vb[i]) return $"{name} vertex {i} differs";
                var ua = ma.uv; var ub = mb.uv;
                for (int i = 0; i < ua.Length; i++) if (ua[i] != ub[i]) return $"{name} uv {i} differs";
                for (int s = 0; s < ma.subMeshCount; s++)
                {
                    var ta = ma.GetTriangles(s); var tb = mb.GetTriangles(s);
                    if (ta.Length != tb.Length) return $"{name} submesh {s} {ta.Length}/{tb.Length} indices";
                    for (int i = 0; i < ta.Length; i++) if (ta[i] != tb[i]) return $"{name} submesh {s} index {i} differs";
                }
            }
            if (a.roadSlots != null && b.roadSlots != null && a.roadSlots.Length != b.roadSlots.Length) return "road slot lists differ";
            return null;
        }

        static void Destroy(CityMeshes.TileMeshes tm)
        {
            if (tm == null) return;
            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings, tm.lampPosts })
                if (m != null) UnityEngine.Object.DestroyImmediate(m);
        }

        // =====================================================================
        //  The run
        // =====================================================================
        public static Result Run(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                 CityAudit.AuditScope sc, Action<string> Line)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var res = new Result { scope = sc.Describe(), full = sc.Full };
            if (sc.Full) foreach (var (name, _) in SubBoxes) res.boxes[name] = new Tallies();
            var ctx = new Ctx { map = map, trims = trims, buildings = buildings ?? CityBuildings.Precompute(map), sc = sc, res = res };
            ctx.edgeTier = new int[map.edges.Length];
            foreach (var e in map.edges) ctx.edgeTier[e.index] = CityTier.Of(e);
            ctx.cl = CityJunctionClusters.Of(map, trims);
            res.clusters = ctx.cl.members.Length;
            foreach (int t in ctx.cl.tier) { if (t == 1) res.clusterT1++; else if (t == 2) res.clusterT2++; else res.clusterT3++; }
            PrepareNodeHoods(ctx);
            PrepareClusterHulls(ctx);

            var tiles = new List<Vector2Int>();
            sc.Tiles(map, tiles);
            // only tiles with a road near: the bbox of a city-wide run is mostly empty corners
            var segs = new HashSet<int>();
            var eval = new List<Vector2Int>();
            foreach (var t in tiles) if (RoadsNear(map, t.x, t.y, segs)) eval.Add(t);
            eval.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            var recs = new Dictionary<long, TileRec>();
            bool tapWas = CityMeshes.RecordTap;
            var g = new Gather();
            try
            {
                CityMeshes.RecordTap = true;
                int done = 0;
                foreach (var t in eval)
                {
                    // evict rows two back
                    if (recs.Count > 0)
                    {
                        var drop = new List<long>();
                        foreach (var kv in recs) if (kv.Value.tz < t.y - 1) drop.Add(kv.Key);
                        foreach (var k in drop) recs.Remove(k);
                    }
                    g.Clear();
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long k = TK(t.x + dx, t.y + dz);
                            if (!recs.TryGetValue(k, out var r))
                            {
                                // a ring tile with no road within 30 m adds no road and no underlay: not built
                                if (RoadsNear(map, t.x + dx, t.y + dz, segs)) { r = BuildRec(ctx, t.x + dx, t.y + dz); res.tilesBuilt++; }
                                else r = new TileRec { tx = t.x + dx, tz = t.y + dz };
                                recs[k] = r;
                            }
                            g.Take(r, t.x, t.y);
                        }
                    EvaluateTile(ctx, g, t.x, t.y);
                    res.tilesEval++;
                    if ((++done & 63) == 0) Debug.Log($"[Coverage] {done}/{eval.Count} tiles, {clock.Elapsed.TotalSeconds:0} s");
                }
            }
            finally { CityMeshes.RecordTap = tapWas; }
            res.seconds = clock.Elapsed.TotalSeconds;
            Report(ctx, Line);
            return res;
        }

        static bool RoadsNear(CityMap map, int tx, int tz, HashSet<int> segs)
        {
            float T = CityMeshes.TileSize;
            segs.Clear();
            map.EdgeSegsInRect(new Vector2(tx * T - 30f, tz * T - 30f), new Vector2((tx + 1) * T + 30f, (tz + 1) * T + 30f), segs);
            return segs.Count > 0;
        }

        // ---- neighbourhoods -------------------------------------------------
        static void PrepareNodeHoods(Ctx ctx)
        {
            var map = ctx.map; var trims = ctx.trims;
            float T = CityMeshes.TileSize;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (!trims.patch[n]) continue;
                float r = 0f;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float tr = trims.TrimAt(e, n);
                    float at = e.a == n ? Mathf.Min(tr, e.length) : Mathf.Max(0f, e.length - tr);
                    r = Mathf.Max(r, tr + trims.ReachAt(e, at));
                }
                r += HoodPadM;
                var p = map.nodes[n];
                for (int tz = Mathf.FloorToInt((p.y - r) / T); tz <= Mathf.FloorToInt((p.y + r) / T); tz++)
                    for (int tx = Mathf.FloorToInt((p.x - r) / T); tx <= Mathf.FloorToInt((p.x + r) / T); tx++)
                    {
                        long k = TK(tx, tz);
                        if (!ctx.nodeHoods.TryGetValue(k, out var l)) ctx.nodeHoods[k] = l = new List<Vector3>();
                        l.Add(new Vector3(p.x, p.y, r));
                    }
            }
        }

        static readonly List<(Vector3 a, Vector3 b, Vector2 outward)> chordScratch = new List<(Vector3, Vector3, Vector2)>();

        static void PrepareClusterHulls(Ctx ctx)
        {
            var map = ctx.map; var cl = ctx.cl;
            float T = CityMeshes.TileSize;
            var pts = new List<Vector2>();
            var ringPts = new List<Vector3>();
            for (int c = 0; c < cl.members.Length; c++)
            {
                pts.Clear();
                float yLo = float.MaxValue, yHi = float.MinValue;
                foreach (int n in cl.members[c])
                {
                    yLo = Mathf.Min(yLo, map.nodeY[n]); yHi = Mathf.Max(yHi, map.nodeY[n]);
                    CityMeshes.FanPerimeter(map, ctx.trims, n, chordScratch);
                    foreach (var (a, b, _) in chordScratch) { pts.Add(new Vector2(a.x, a.z)); pts.Add(new Vector2(b.x, b.z)); }
                }
                if (pts.Count < 3) continue;
                // Roads pass L7 (A11): a cluster drawn as ONE paved area is
                // judged against its own ring - the outside arms' mouths in
                // order, straight across the median opening, round its curb
                // returns - not the convex hull, which since the curb returns
                // (A10) takes in the corner lots between the arcs and the
                // chords that cut them.
                var rc = ctx.trims.ClusterOfNode(cl.members[c][0]);
                bool one = rc != null;
                foreach (int n in cl.members[c]) if (ctx.trims.ClusterOfNode(n) != rc) one = false;
                if (one)
                {
                    CityMeshes.FanRingPoints(map, ctx.trims, rc.owner, ringPts);
                    if (ringPts.Count >= 3)
                    {
                        CityMeshes.FanCentre(map, ctx.trims, rc.owner, out var cc, out float ccY);
                        pts.Clear();
                        foreach (var q in ringPts) pts.Add(new Vector2(q.x, q.z));
                        Vector2 rmn = pts[0], rmx = pts[0];
                        foreach (var q in pts) { rmn = Vector2.Min(rmn, q); rmx = Vector2.Max(rmx, q); }
                        if (rmx.x - rmn.x > 200f || rmx.y - rmn.y > 200f) continue;
                        int rkey = Key(KCluster, cl.tier[c], c);
                        for (int i = 0; i < pts.Count; i++)
                        {
                            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                            if (Cross2(a - cc, b - cc) <= 1e-4f) continue;   // a piece turning back: no area of its own
                            // each wedge at the heights it spans (the members' and the arms' at their trims)
                            float ya = ringPts[i].y, yb = ringPts[(i + 1) % pts.Count].y;
                            float wLo = Mathf.Min(Mathf.Min(ya, yb), Mathf.Min(ccY, yLo)), wHi = Mathf.Max(Mathf.Max(ya, yb), Mathf.Max(ccY, yHi));
                            float ry = 0.5f * (wLo + wHi), rslack = 0.5f * (wHi - wLo) + 0.3f;
                            int hi = ctx.hulls.Count;
                            ctx.hulls.Add((new[] { cc, a, b }, ry, rslack, rkey));
                            for (int tz = Mathf.FloorToInt(rmn.y / T); tz <= Mathf.FloorToInt(rmx.y / T); tz++)
                                for (int tx = Mathf.FloorToInt(rmn.x / T); tx <= Mathf.FloorToInt(rmx.x / T); tx++)
                                {
                                    long k = TK(tx, tz);
                                    if (!ctx.hullByTile.TryGetValue(k, out var l)) ctx.hullByTile[k] = l = new List<int>();
                                    l.Add(hi);
                                }
                        }
                        continue;
                    }
                }
                var hull = Hull(pts);
                if (hull.Count < 3) continue;
                Vector2 mn = hull[0], mx = hull[0];
                foreach (var q in hull) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                if (mx.x - mn.x > 200f || mx.y - mn.y > 200f) continue;   // as the replica: a runaway chain is not a junction box
                float y = 0.5f * (yLo + yHi), slack = 0.5f * (yHi - yLo) + 0.3f;
                int key = Key(KCluster, cl.tier[c], c);
                for (int i = 1; i + 1 < hull.Count; i++)
                {
                    int hi = ctx.hulls.Count;
                    ctx.hulls.Add((new[] { hull[0], hull[i], hull[i + 1] }, y, slack, key));
                    for (int tz = Mathf.FloorToInt(mn.y / T); tz <= Mathf.FloorToInt(mx.y / T); tz++)
                        for (int tx = Mathf.FloorToInt(mn.x / T); tx <= Mathf.FloorToInt(mx.x / T); tx++)
                        {
                            long k = TK(tx, tz);
                            if (!ctx.hullByTile.TryGetValue(k, out var l)) ctx.hullByTile[k] = l = new List<int>();
                            l.Add(hi);
                        }
                }
            }
        }

        static float Cross2(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        static List<Vector2> Hull(List<Vector2> pts)
        {
            var p = new List<Vector2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var lo = new List<Vector2>(); var up = new List<Vector2>();
            foreach (var q in p) { while (lo.Count >= 2 && Cross(lo[lo.Count - 2], lo[lo.Count - 1], q) <= 0f) lo.RemoveAt(lo.Count - 1); lo.Add(q); }
            for (int i = p.Count - 1; i >= 0; i--) { var q = p[i]; while (up.Count >= 2 && Cross(up[up.Count - 2], up[up.Count - 1], q) <= 0f) up.RemoveAt(up.Count - 1); up.Add(q); }
            lo.RemoveAt(lo.Count - 1); up.RemoveAt(up.Count - 1);
            lo.AddRange(up);
            return lo;
        }

        // ---- building a tile's record ----------------------------------------
        static readonly List<CityMeshes.AuditView.ClipView> clipScratch = new List<CityMeshes.AuditView.ClipView>();
        static readonly List<int> intScratch = new List<int>(), hostScratch = new List<int>();

        static TileRec BuildRec(Ctx ctx, int tx, int tz)
        {
            var map = ctx.map; var trims = ctx.trims;
            var r = new TileRec { tx = tx, tz = tz };
            var tm = CityMeshes.Build(map, trims, ctx.buildings, tx, tz);
            try
            {
                var o = tm.origin;
                // ROAD surfaces, each triangle with its owner from the tap
                if (tm.roads != null && tm.tap != null && tm.tap.slotBase != null)
                {
                    var v = tm.roads.vertices;
                    var own = new int[v.Length];
                    for (int i = 0; i < own.Length; i++) own[i] = -1;
                    int BaseOf(int slot)
                    {
                        for (int i = 0; i < tm.roadSlots.Length; i++) if ((int)tm.roadSlots[i] == slot) return tm.tap.slotBase[i];
                        return -1;
                    }
                    void Mark(int from, int count, int key)
                    {
                        for (int i = Mathf.Max(0, from); i < Mathf.Min(own.Length, from + count); i++) own[i] = key;
                    }
                    foreach (var sp in tm.tap.spans)
                    {
                        int b = BaseOf(sp.slot); if (b < 0) continue;
                        bool deck = ((sp.flagsA | sp.flagsB) & CityMeshes.RoadTap.FElevated) != 0;
                        Mark(b + sp.bucketV, 4 * Mathf.Max(1, sp.strips), Key(deck ? KDeck : KRibbon, ctx.edgeTier[sp.edge], sp.edge));
                    }
                    foreach (var sp in tm.tap.gores)
                    {
                        int b = BaseOf(sp.slot); if (b < 0) continue;
                        Mark(b + sp.bucketV, 4, Key(KGore, ctx.edgeTier[sp.edge], sp.edge));
                    }
                    foreach (var fa in tm.tap.fans)
                    {
                        int b = BaseOf(fa.slot); if (b < 0) continue;
                        Mark(b + fa.bucketV, fa.count, Key(KFan, ctx.cl.nodeTier[fa.node], fa.node));
                    }
                    for (int si = 0; si < tm.roads.subMeshCount && si < tm.roadSlots.Length; si++)
                    {
                        if (tm.roadSlots[si] == CityMeshes.Slot.Concrete) continue;
                        var t = tm.roads.GetTriangles(si);
                        for (int k = 0; k + 2 < t.Length; k += 3)
                        {
                            Vector3 a = v[t[k]] + o, b = v[t[k + 1]] + o, c = v[t[k + 2]] + o;
                            if (!UpFacing(a, b, c)) continue;
                            int key = own[t[k]];
                            if (key < 0) { ctx.res.untapped++; continue; }
                            r.Add(a, b, c, key);
                            int kind = KindOf(key);
                            // gores are their own outline and their own neighbourhood
                            if (kind == KGore)
                            {
                                r.AddO(a, b, c, key, 0f);
                                var cen = (a + b + c) / 3f;
                                float rad = Mathf.Max(Vector3.Distance(cen, a), Mathf.Max(Vector3.Distance(cen, b), Vector3.Distance(cen, c)));
                                r.hoods.Add(new Vector3(cen.x, cen.z, rad + HoodPadM));
                            }
                            else if (kind == KFan) r.AddO(a, b, c, key, 0f);
                        }
                    }
                }
                // GROUND surfaces: the lattice (all three corners on the 8 m grid) and the strips laid off it
                if (tm.ground != null)
                {
                    var v = tm.ground.vertices;
                    for (int si = 0; si < tm.ground.subMeshCount; si++)
                    {
                        var t = tm.ground.GetTriangles(si);
                        for (int k = 0; k + 2 < t.Length; k += 3)
                        {
                            Vector3 la = v[t[k]], lb = v[t[k + 1]], lc = v[t[k + 2]];
                            Vector3 a = la + o, b = lb + o, c = lc + o;
                            if (!UpFacing(a, b, c)) continue;
                            bool lattice = OnLattice(la) && OnLattice(lb) && OnLattice(lc);
                            r.Add(a, b, c, Key(lattice ? KLattice : KStrip, 0, 0));
                        }
                    }
                }
                // the clip pairs this build drew (branch, each host of its chain)
                CityMeshes.AuditView.ClippedEdges(intScratch);
                foreach (int be in intScratch)
                {
                    CityMeshes.AuditView.ClipsOf(be, clipScratch);
                    var B = map.edges[be];
                    foreach (var cv in clipScratch)
                    {
                        CityMeshes.AuditView.HostChainOf(B, Mathf.Clamp(0.5f * (cv.sFrom + cv.sTo), 0f, B.length), hostScratch);
                        foreach (int h in hostScratch) if (h != be) ctx.clipPairs.Add(PairKey(be, h));
                    }
                }
                // the OUTLINE of every edge near the tile, from this build's sections
                EdgeOutline(ctx, r, tx, tz);
            }
            finally { Destroy(tm); }
            return r;
        }

        static bool UpFacing(Vector3 a, Vector3 b, Vector3 c)
        {
            float ny = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);
            return ny > 1e-6f && !float.IsNaN(a.y + b.y + c.y);
        }

        static bool OnLattice(Vector3 v)
        {
            float cell = CityMeshes.TileSize / CityMeshes.GroundRes;
            float fx = v.x / cell, fz = v.z / cell;
            return Mathf.Abs(fx - Mathf.Round(fx)) < 1e-3f && Mathf.Abs(fz - Mathf.Round(fz)) < 1e-3f;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly Dictionary<int, List<(float s0, float s1)>> edgeRanges = new Dictionary<int, List<(float, float)>>();
        static readonly List<float> sampleScratch = new List<float>();

        /// <summary>Each edge's footprint between its trims near the tile:
        /// LaneExtents (this build's drawn sections where it has them), sampled
        /// at every polyline vertex (on the bisector, as the builder cuts it)
        /// and every metre between; at a mitred node on the mitre.</summary>
        static void EdgeOutline(Ctx ctx, TileRec r, int tx, int tz)
        {
            var map = ctx.map; var trims = ctx.trims;
            float T = CityMeshes.TileSize;
            var mn = new Vector2(tx * T - 2f, tz * T - 2f); var mx = new Vector2((tx + 1) * T + 2f, (tz + 1) * T + 2f);
            segScratch.Clear();
            map.EdgeSegsInRect(mn, mx, segScratch);
            foreach (var l in edgeRanges.Values) l.Clear();
            var used = new List<int>();
            foreach (int packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (si + 1 >= e.pts.Length) continue;
                Vector2 p0 = e.pts[si], p1 = e.pts[si + 1];
                if (Mathf.Max(p0.x, p1.x) < mn.x - 30f || Mathf.Min(p0.x, p1.x) > mx.x + 30f || Mathf.Max(p0.y, p1.y) < mn.y - 30f || Mathf.Min(p0.y, p1.y) > mx.y + 30f) continue;
                // the part of the segment within the tile (+5 m): Liang-Barsky
                float t0 = 0f, t1 = 1f; var d = p1 - p0;
                if (!Clip(-d.x, p0.x - (mn.x - 5f), ref t0, ref t1) || !Clip(d.x, (mx.x + 5f) - p0.x, ref t0, ref t1) ||
                    !Clip(-d.y, p0.y - (mn.y - 5f), ref t0, ref t1) || !Clip(d.y, (mx.y + 5f) - p0.y, ref t0, ref t1)) continue;
                if (!edgeRanges.TryGetValue(ei, out var list)) edgeRanges[ei] = list = new List<(float, float)>();
                if (list.Count == 0) used.Add(ei);
                float L = e.s[si + 1] - e.s[si];
                // whole segments keep their vertex ends exactly (the bisector sections)
                list.Add((t0 <= 1e-4f ? e.s[si] : e.s[si] + L * t0, t1 >= 1f - 1e-4f ? e.s[si + 1] : e.s[si] + L * t1));
            }
            foreach (int ei in used)
            {
                var e = map.edges[ei];
                if (e.length < 0.3f) continue;   // a loop edge (a == b: a crescent) is drawn like any other
                float sA = trims.atA[ei], sB = e.length - trims.atB[ei];
                if (sB - sA < 0.3f) continue;
                int tier = ctx.edgeTier[ei];
                foreach (var (r0, r1) in edgeRanges[ei])
                {
                    float s0 = Mathf.Max(sA, r0), s1 = Mathf.Min(sB, r1);
                    if (s1 - s0 < 0.05f) continue;
                    sampleScratch.Clear();
                    int nstep = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 1f));
                    for (int k = 0; k <= nstep; k++) sampleScratch.Add(Mathf.Lerp(s0, s1, k / (float)nstep));
                    Vector3 pL = default, pR = default; bool have = false; bool prevDeck = false;
                    foreach (float s in sampleScratch)
                    {
                        var p = e.PointAt(s);
                        var n = NormalAt(ctx, e, s);
                        CityMeshes.LaneExtents(map, trims, e, s, out float hwL, out float hwR);
                        if (hwL + hwR < 0.05f) { have = false; continue; }
                        float y = e.YAt(s);
                        var L = new Vector3(p.x - n.x * hwL, y, p.y - n.y * hwL);
                        var R = new Vector3(p.x + n.x * hwR, y, p.y + n.y * hwR);
                        bool deck = e.ElevatedAt(s);
                        if (have)
                        {
                            int key = Key(deck || prevDeck ? KDeck : KRibbon, tier, ei);
                            r.AddO(pL, L, R, key, 0f);
                            r.AddO(pL, R, pR, key, 0f);
                        }
                        pL = L; pR = R; have = true; prevDeck = deck;
                    }
                }
            }
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-9f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        /// <summary>The left-of-travel normal the builder cuts a section on:
        /// the segment's, the bisector at an interior polyline vertex, the
        /// mitre at a mitred node.</summary>
        static Vector2 NormalAt(Ctx ctx, CityMap.Edge e, float s)
        {
            var map = ctx.map; var trims = ctx.trims;
            Vector2 t;
            if (s <= 1e-3f || s >= e.length - 1e-3f)
            {
                int node = s <= 1e-3f ? e.a : e.b;
                t = e.TangentAt(s);
                if (trims.mitre[node])
                {
                    int other = trims.throughA[node] == e.index ? trims.throughB[node] : trims.throughB[node] == e.index ? trims.throughA[node] : -1;
                    if (other >= 0)
                    {
                        var o = map.edges[other];
                        // the partner's direction of travel continuing this edge's
                        var to = o.a == node ? o.TangentAt(0f) : -o.TangentAt(o.length);
                        var te = s <= 1e-3f ? -e.TangentAt(0f) : e.TangentAt(e.length);   // pointing INTO the node
                        var bis = te + to;
                        if (bis.sqrMagnitude > 1e-6f) t = s <= 1e-3f ? -bis.normalized : bis.normalized;
                    }
                }
            }
            else
            {
                // an interior vertex within 1 mm: the bisector
                int k = Array.BinarySearch(e.s, s);
                if (k < 0) { int ins = ~k; if (ins > 0 && ins < e.s.Length && Mathf.Abs(e.s[ins] - s) < 1e-3f) k = ins; else if (ins - 1 > 0 && Mathf.Abs(e.s[ins - 1] - s) < 1e-3f) k = ins - 1; }
                if (k > 0 && k < e.pts.Length - 1)
                {
                    var t0 = (e.pts[k] - e.pts[k - 1]).normalized; var t1 = (e.pts[k + 1] - e.pts[k]).normalized;
                    var bis = t0 + t1;
                    t = bis.sqrMagnitude > 1e-6f ? bis.normalized : e.TangentAt(s);
                }
                else t = e.TangentAt(s);
            }
            return new Vector2(-t.y, t.x);
        }

        // ---- gathering one tile's pieces from its ring ------------------------
        sealed class Gather
        {
            public float[] t = new float[9 * 65536]; public int[] key = new int[65536]; public int n;
            public float[] o = new float[9 * 32768]; public int[] okey = new int[32768]; public float[] oslack = new float[32768]; public int on;
            public readonly List<Vector3> hoods = new List<Vector3>();
            public void Clear() { n = 0; on = 0; hoods.Clear(); }
            public void Take(TileRec r, int tx, int tz)
            {
                float T = CityMeshes.TileSize, pad = 2f;
                float x0 = tx * T - pad, z0 = tz * T - pad, x1 = (tx + 1) * T + pad, z1 = (tz + 1) * T + pad;
                for (int i = 0; i < r.n; i++)
                {
                    int b = 9 * i;
                    float mnx = Mathf.Min(r.t[b], Mathf.Min(r.t[b + 3], r.t[b + 6])), mxx = Mathf.Max(r.t[b], Mathf.Max(r.t[b + 3], r.t[b + 6]));
                    float mnz = Mathf.Min(r.t[b + 2], Mathf.Min(r.t[b + 5], r.t[b + 8])), mxz = Mathf.Max(r.t[b + 2], Mathf.Max(r.t[b + 5], r.t[b + 8]));
                    if (mxx < x0 || mnx > x1 || mxz < z0 || mnz > z1) continue;
                    if (n == key.Length) { Array.Resize(ref key, n * 2); Array.Resize(ref t, 9 * n * 2); }
                    Array.Copy(r.t, b, t, 9 * n, 9); key[n++] = r.key[i];
                }
                for (int i = 0; i < r.on; i++)
                {
                    int b = 9 * i;
                    float mnx = Mathf.Min(r.o[b], Mathf.Min(r.o[b + 3], r.o[b + 6])), mxx = Mathf.Max(r.o[b], Mathf.Max(r.o[b + 3], r.o[b + 6]));
                    float mnz = Mathf.Min(r.o[b + 2], Mathf.Min(r.o[b + 5], r.o[b + 8])), mxz = Mathf.Max(r.o[b + 2], Mathf.Max(r.o[b + 5], r.o[b + 8]));
                    if (mxx < x0 || mnx > x1 || mxz < z0 || mnz > z1) continue;
                    AddO(r.o, b, r.okey[i], r.oslack[i]);
                }
                foreach (var h in r.hoods) hoods.Add(h);
            }
            public void AddO(float[] src, int b, int k, float slack)
            {
                if (on == okey.Length) { Array.Resize(ref okey, on * 2); Array.Resize(ref oslack, on * 2); Array.Resize(ref o, 9 * on * 2); }
                Array.Copy(src, b, o, 9 * on, 9); okey[on] = k; oslack[on] = slack; on++;
            }
        }

        // =====================================================================
        //  Rasterising one tile, block by block
        // =====================================================================
        // the block grid, reused (the fine one is the larger)
        static readonly int GMax = Mathf.CeilToInt(BlockM / FineCell) + 2 * Apron;
        static readonly byte[] sN = new byte[GMax * GMax];
        static readonly float[] sY = new float[GMax * GMax * K];
        static readonly int[] sKey = new int[GMax * GMax * K];
        static readonly byte[] oN = new byte[GMax * GMax];
        static readonly float[] oY = new float[GMax * GMax * OL], oS = new float[GMax * GMax * OL];
        static readonly int[] oKey = new int[GMax * GMax * OL];
        static readonly List<int>[] binRoad = NewBins(), binGround = NewBins(), binOut = NewBins();
        static List<int>[] NewBins() { var b = new List<int>[32 * 32]; for (int i = 0; i < b.Length; i++) b[i] = new List<int>(16); return b; }
        static readonly float[] hullBuf = new float[9];

        static void EvaluateTile(Ctx ctx, Gather g, int tx, int tz)
        {
            float T = CityMeshes.TileSize;
            float x0 = tx * T, z0 = tz * T;
            int NB = Mathf.RoundToInt(T / BlockM);
            for (int i = 0; i < NB * NB; i++) { binRoad[i].Clear(); binGround[i].Clear(); binOut[i].Clear(); }
            // the cluster hulls touching this tile join the outline
            int hullFrom = g.on;
            if (ctx.hullByTile.TryGetValue(TK(tx, tz), out var hl))
                foreach (int hi in hl)
                {
                    var (tri, y, slack, key) = ctx.hulls[hi];
                    hullBuf[0] = tri[0].x; hullBuf[1] = y; hullBuf[2] = tri[0].y;
                    hullBuf[3] = tri[1].x; hullBuf[4] = y; hullBuf[5] = tri[1].y;
                    hullBuf[6] = tri[2].x; hullBuf[7] = y; hullBuf[8] = tri[2].y;
                    g.AddO(hullBuf, 0, key, slack);
                }
            float pad = Apron * CoarseCell + 0.01f;
            void Bin(float[] arr, int b, List<int>[] bins, int idx)
            {
                float mnx = Mathf.Min(arr[b], Mathf.Min(arr[b + 3], arr[b + 6])) - pad, mxx = Mathf.Max(arr[b], Mathf.Max(arr[b + 3], arr[b + 6])) + pad;
                float mnz = Mathf.Min(arr[b + 2], Mathf.Min(arr[b + 5], arr[b + 8])) - pad, mxz = Mathf.Max(arr[b + 2], Mathf.Max(arr[b + 5], arr[b + 8])) + pad;
                int bx0 = Mathf.Max(0, Mathf.FloorToInt((mnx - x0) / BlockM)), bx1 = Mathf.Min(NB - 1, Mathf.FloorToInt((mxx - x0) / BlockM));
                int bz0 = Mathf.Max(0, Mathf.FloorToInt((mnz - z0) / BlockM)), bz1 = Mathf.Min(NB - 1, Mathf.FloorToInt((mxz - z0) / BlockM));
                for (int bz = bz0; bz <= bz1; bz++) for (int bx = bx0; bx <= bx1; bx++) bins[bz * NB + bx].Add(idx);
            }
            for (int i = 0; i < g.n; i++) Bin(g.t, 9 * i, IsRoad(KindOf(g.key[i])) ? binRoad : binGround, i);
            for (int i = 0; i < g.on; i++) Bin(g.o, 9 * i, binOut, i);
            // the neighbourhoods: fan nodes (global) and gore quads (the ring's)
            ctx.nodeHoods.TryGetValue(TK(tx, tz), out var nh);
            for (int bz = 0; bz < NB; bz++)
                for (int bx = 0; bx < NB; bx++)
                {
                    int bi = bz * NB + bx;
                    if (binRoad[bi].Count == 0 && binOut[bi].Count == 0) continue;
                    float bxMin = x0 + bx * BlockM, bzMin = z0 + bz * BlockM;
                    if (!ctx.sc.Overlaps(new Vector2(bxMin, bzMin), new Vector2(bxMin + BlockM, bzMin + BlockM))) continue;
                    bool fine = false;
                    if (nh != null) foreach (var h in nh) if (DiskHitsRect(h, bxMin, bzMin)) { fine = true; break; }
                    if (!fine) foreach (var h in g.hoods) if (DiskHitsRect(h, bxMin, bzMin)) { fine = true; break; }
                    if (fine) ctx.res.fineBlocks++; else ctx.res.coarseBlocks++;
                    EvaluateBlock(ctx, g, bi, bxMin, bzMin, fine ? FineCell : CoarseCell);
                }
            g.on = hullFrom;
        }

        static bool DiskHitsRect(Vector3 h, float x0, float z0)
        {
            float cx = Mathf.Clamp(h.x, x0, x0 + BlockM), cz = Mathf.Clamp(h.y, z0, z0 + BlockM);
            float dx = h.x - cx, dz = h.y - cz;
            return dx * dx + dz * dz <= h.z * h.z;
        }

        // scanline output
        static int[] scCell = new int[8192]; static float[] scY = new float[8192];

        /// <summary>Cells of the block grid (origin gx0, gz0 = the apron's
        /// corner, G x G cells of size c) whose centres lie in the triangle,
        /// with the triangle's height there. Count in the return.</summary>
        static int Scan(float[] t, int b, float gx0, float gz0, int G, float c)
        {
            float ax = t[b], ay = t[b + 1], az = t[b + 2], bx = t[b + 3], by = t[b + 4], bz = t[b + 5], cx = t[b + 6], cy = t[b + 7], cz = t[b + 8];
            // plane y = A x + B z + C
            float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (Mathf.Abs(ny) < 1e-9f) return 0;
            float A = -nx / ny, B = -nz / ny, C = ay - A * ax - B * az;
            float mnz = Mathf.Min(az, Mathf.Min(bz, cz)), mxz = Mathf.Max(az, Mathf.Max(bz, cz));
            int r0 = Mathf.Max(0, Mathf.CeilToInt((mnz - gz0) / c - 0.5f)), r1 = Mathf.Min(G - 1, Mathf.FloorToInt((mxz - gz0) / c - 0.5f));
            int cnt = 0;
            for (int r = r0; r <= r1; r++)
            {
                float z = gz0 + (r + 0.5f) * c;
                float xl = float.MaxValue, xr = float.MinValue;
                Edge(ax, az, bx, bz, z, ref xl, ref xr);
                Edge(bx, bz, cx, cz, z, ref xl, ref xr);
                Edge(cx, cz, ax, az, z, ref xl, ref xr);
                if (xl > xr) continue;
                int c0 = Mathf.Max(0, Mathf.CeilToInt((xl - gx0) / c - 0.5f)), c1 = Mathf.Min(G - 1, Mathf.FloorToInt((xr - gx0) / c - 0.5f));
                for (int col = c0; col <= c1; col++)
                {
                    if (cnt == scCell.Length) { Array.Resize(ref scCell, cnt * 2); Array.Resize(ref scY, cnt * 2); }
                    float x = gx0 + (col + 0.5f) * c;
                    scCell[cnt] = r * G + col; scY[cnt] = A * x + B * z + C; cnt++;
                }
            }
            return cnt;
        }

        static void Edge(float px, float pz, float qx, float qz, float z, ref float xl, ref float xr)
        {
            if ((pz <= z && qz > z) || (qz <= z && pz > z))
            {
                float x = px + (z - pz) / (qz - pz) * (qx - px);
                if (x < xl) xl = x;
                if (x > xr) xr = x;
            }
        }

        static void InsertSurface(int cell, float y, int key)
        {
            int n = sN[cell], b = cell * K;
            for (int k = 0; k < n; k++) if (sKey[b + k] == key && Mathf.Abs(sY[b + k] - y) < 0.03f) return;   // the same piece twice (paint columns overlap 2 mm)
            int at = n;
            while (at > 0 && sY[b + at - 1] < y) at--;
            if (at >= K) return;
            int last = Mathf.Min(n, K - 1);
            for (int k = last; k > at; k--) { sY[b + k] = sY[b + k - 1]; sKey[b + k] = sKey[b + k - 1]; }
            sY[b + at] = y; sKey[b + at] = key;
            if (n < K) sN[cell] = (byte)(n + 1);
        }

        static int Prio(int kind) => kind == KRibbon || kind == KDeck ? 0 : kind == KFan ? 1 : kind == KGore ? 2 : 3;

        static void InsertOutline(int cell, float y, float slack, int key)
        {
            int n = oN[cell], b = cell * OL;
            for (int k = 0; k < n; k++)
            {
                float lo = oY[b + k] - oS[b + k], hi = oY[b + k] + oS[b + k];
                if (y + slack + 1f >= lo && y - slack - 1f <= hi)
                {
                    float nlo = Mathf.Min(lo, y - slack), nhi = Mathf.Max(hi, y + slack);
                    oY[b + k] = 0.5f * (nlo + nhi); oS[b + k] = 0.5f * (nhi - nlo);
                    int ok = oKey[b + k];
                    int tier = Mathf.Min(TierOf(ok) == 0 ? 3 : TierOf(ok), TierOf(key) == 0 ? 3 : TierOf(key));
                    int win = Prio(KindOf(key)) < Prio(KindOf(ok)) ? key : ok;
                    oKey[b + k] = Key(KindOf(win), tier, IdOf(win));
                    return;
                }
            }
            if (n < OL) { oY[b + n] = y; oS[b + n] = slack; oKey[b + n] = key; oN[cell] = (byte)(n + 1); }
        }

        static void EvaluateBlock(Ctx ctx, Gather g, int bi, float bxMin, float bzMin, float c)
        {
            int n = Mathf.RoundToInt(BlockM / c), G = n + 2 * Apron;
            float gx0 = bxMin - Apron * c, gz0 = bzMin - Apron * c;
            int cells = G * G;
            Array.Clear(sN, 0, cells); Array.Clear(oN, 0, cells);
            foreach (int i in binRoad[bi])
            {
                int cnt = Scan(g.t, 9 * i, gx0, gz0, G, c);
                int key = g.key[i];
                for (int k = 0; k < cnt; k++) InsertSurface(scCell[k], scY[k], key);
            }
            foreach (int i in binGround[bi])
            {
                int cnt = Scan(g.t, 9 * i, gx0, gz0, G, c);
                int key = g.key[i];
                for (int k = 0; k < cnt; k++)
                {
                    int cell = scCell[k];
                    int m = sN[cell];
                    if (m == 0) continue;
                    // only a ground surface just under a road surface (an underlay)
                    float y = scY[k]; bool near = false;
                    for (int q = 0; q < m; q++) { float d = sY[cell * K + q] - y; if (d >= 0f && d <= UnderMax + 0.02f && IsRoad(KindOf(sKey[cell * K + q]))) { near = true; break; } }
                    if (near) InsertSurface(cell, y, key);
                }
            }
            foreach (int i in binOut[bi])
            {
                int cnt = Scan(g.o, 9 * i, gx0, gz0, G, c);
                int key = g.okey[i]; float slack = g.oslack[i];
                for (int k = 0; k < cnt; k++) InsertOutline(scCell[k], scY[k], slack, key);
            }
            float area = c * c;
            int dr = Mathf.CeilToInt(OutsideM / c - 1e-4f);
            var res = ctx.res; var sc = ctx.sc;
            for (int j = Apron; j < Apron + n; j++)
                for (int i = Apron; i < Apron + n; i++)
                {
                    int ci = j * G + i;
                    int ns = sN[ci], no = oN[ci];
                    if (ns == 0 && no == 0) continue;
                    float wx = gx0 + (i + 0.5f) * c, wz = gz0 + (j + 0.5f) * c;
                    if (!sc.Full && !sc.Contains(wx, wz)) continue;
                    res.cells++;
                    Tallies box0 = null, box1 = null;
                    if (sc.Full)
                    {
                        if (SubBoxes[0].box.Contains(new Vector2(wx, wz))) box0 = res.boxes[SubBoxes[0].name];
                        if (SubBoxes[1].box.Contains(new Vector2(wx, wz))) box1 = res.boxes[SubBoxes[1].name];
                    }
                    int sb = ci * K;
                    // context: road and outline area by tier
                    int topRoad = -1;
                    for (int q = 0; q < ns; q++) if (IsRoad(KindOf(sKey[sb + q]))) { topRoad = q; break; }
                    if (topRoad >= 0) { int t = TierOf(sKey[sb + topRoad]); if (sc.HasTier(t)) Add(res.all.road, box0?.road, box1?.road, t, area); }
                    if (no > 0) { int t = TierOf(oKey[ci * OL]); if (t == 0) t = 3; if (sc.HasTier(t)) Add(res.all.outline, box0?.outline, box1?.outline, t, area); }

                    // HOLES: an outline level with no road surface at its height (eroded by one cell)
                    if (no > 0 && oN[ci - 1] > 0 && oN[ci + 1] > 0 && oN[ci - G] > 0 && oN[ci + G] > 0)
                        for (int q = 0; q < no; q++)
                        {
                            float oy = oY[ci * OL + q], os = oS[ci * OL + q] + LevelTol;
                            bool hit = false;
                            for (int p = 0; p < ns; p++) if (IsRoad(KindOf(sKey[sb + p])) && Mathf.Abs(sY[sb + p] - oy) <= os) { hit = true; break; }
                            if (hit) continue;
                            int ok = oKey[ci * OL + q], t = TierOf(ok); if (t == 0) t = 3;
                            if (!sc.HasTier(t)) continue;
                            int kind = KindOf(ok);
                            Add2(res.all.holes, box0?.holes, box1?.holes, t, kind, area);
                            Obj(res.holeObj, ((long)kind << 40) | (uint)IdOf(ok), area, wx, wz, t, ok, 0);
                        }

                    // OUTSIDE: a road surface with no outline at its height within 0.25 m
                    for (int p = 0; p < ns; p++)
                    {
                        int sk = sKey[sb + p]; int kind = KindOf(sk);
                        if (!IsRoad(kind)) continue;
                        float y = sY[sb + p];
                        bool inside = false;
                        for (int q = 0; q < no; q++) if (Mathf.Abs(oY[ci * OL + q] - y) <= oS[ci * OL + q] + LevelTol) { inside = true; break; }
                        for (int dz = -dr; dz <= dr && !inside; dz++)
                            for (int dx = -dr; dx <= dr && !inside; dx++)
                            {
                                int cj = ci + dz * G + dx;
                                for (int q = 0; q < oN[cj]; q++) if (Mathf.Abs(oY[cj * OL + q] - y) <= oS[cj * OL + q] + LevelTol) { inside = true; break; }
                            }
                        if (inside) continue;
                        int t = TierOf(sk); if (!sc.HasTier(t)) continue;
                        Add2(res.all.outside, box0?.outside, box1?.outside, t, kind, area);
                        Obj(res.outObj, sk, area, wx, wz, t, sk, 0);
                    }

                    // COPLANAR: the closest pair of different owners within 5 cm
                    float bestDy = float.MaxValue; int pa = -1, pb = -1;
                    for (int p = 0; p < ns; p++)
                    {
                        int ka = sKey[sb + p]; if (!IsRoad(KindOf(ka))) continue;
                        for (int q = p + 1; q < ns; q++)
                        {
                            int kb = sKey[sb + q]; if (!IsRoad(KindOf(kb))) continue;
                            float dy = sY[sb + p] - sY[sb + q];
                            if (dy > CoplanarDy) break;   // sorted by height
                            if (SameOwner(ka, kb)) continue;
                            if (dy < bestDy) { bestDy = dy; pa = ka; pb = kb; }
                        }
                    }
                    if (pa != -1)
                    {
                        int t = Mathf.Min(TierOf(pa), TierOf(pb));
                        if (sc.HasTier(t))
                        {
                            int rel = Relation(ctx, pa, pb);
                            Add2(res.all.coplanar, box0?.coplanar, box1?.coplanar, t, rel, area);
                            if (KindOf(pa) == KDeck || KindOf(pb) == KDeck) Add(res.all.coplanarDeck, box0?.coplanarDeck, box1?.coplanarDeck, t, area);
                            int lo = Math.Min(pa, pb), hi = Math.Max(pa, pb);
                            Obj(res.copObj, ((long)lo << 32) | (uint)hi, area, wx, wz, t, lo, hi);
                        }
                    }

                    // UNDERLAP: the surface right under a road surface, 0.5-8 cm down
                    for (int p = 0; p + 1 < ns; p++)
                    {
                        int ka = sKey[sb + p]; if (!IsRoad(KindOf(ka))) continue;
                        int kb = sKey[sb + p + 1];
                        float dy = sY[sb + p] - sY[sb + p + 1];
                        if (dy <= UnderMin || dy > UnderMax || SameOwner(ka, kb)) continue;
                        int t = TierOf(ka); if (!sc.HasTier(t)) break;
                        int kindB = KindOf(kb), under = IsRoad(kindB) ? 0 : kindB == KStrip ? 1 : 2;
                        Add2(res.all.underlap, box0?.underlap, box1?.underlap, t, under, area);
                        Obj(res.underObj, ((long)under << 40) | (uint)ka, area, wx, wz, t, ka, kb);
                        break;
                    }

                    // OVERSHOOT: a clipped branch's surface over its host's
                    for (int p = 0; p < ns; p++)
                    {
                        int ka = sKey[sb + p]; if (OwnerClass(ka) != KRibbon) continue;
                        bool found = false;
                        for (int q = 0; q < ns; q++)
                        {
                            if (q == p) continue;
                            int kb = sKey[sb + q]; if (OwnerClass(kb) != KRibbon) continue;
                            if (Mathf.Abs(sY[sb + p] - sY[sb + q]) > LevelTol) continue;
                            int ea = IdOf(ka), eb = IdOf(kb);
                            if (ea == eb || !ctx.clipPairs.Contains(PairKey(ea, eb))) continue;
                            // which is the branch: the link, else the narrower
                            var A = ctx.map.edges[ea]; var B = ctx.map.edges[eb];
                            bool aBranch = A.link != B.link ? A.link : A.width <= B.width;
                            int br = aBranch ? ea : eb, host = aBranch ? eb : ea;
                            int t = Mathf.Min(TierOf(ka), TierOf(kb));
                            if (!sc.HasTier(t)) { found = true; break; }
                            Add(res.all.overshoot, box0?.overshoot, box1?.overshoot, t, area);
                            float depth = Penetration(ctx.map, ctx.map.edges[host], ctx.map.edges[br], new Vector2(wx, wz), out bool far);
                            if (far) Add(res.all.overshootFar, box0?.overshootFar, box1?.overshootFar, t, area);
                            var acc = Obj(res.overObj, br, area, wx, wz, t, br, host);
                            if (depth > acc.depth) acc.depth = depth;
                            found = true; break;
                        }
                        if (found) break;
                    }
                }
        }

        static void Add(double[] all, double[] b0, double[] b1, int t, double a) { all[t] += a; if (b0 != null) b0[t] += a; if (b1 != null) b1[t] += a; }
        static void Add2(double[,] all, double[,] b0, double[,] b1, int t, int k, double a) { all[t, k] += a; if (b0 != null) b0[t, k] += a; if (b1 != null) b1[t, k] += a; }

        static Acc Obj(Dictionary<long, Acc> d, long key, double area, float x, float z, int tier, int a, int b)
        {
            if (!d.TryGetValue(key, out var acc)) d[key] = acc = new Acc { x = x, z = z, tier = tier, a = a, b = b };
            acc.area += area;
            return acc;
        }

        static int Relation(Ctx ctx, int ka, int kb)
        {
            int A = OwnerClass(ka), B = OwnerClass(kb);
            if (A == KGore || B == KGore) return RGore;
            var map = ctx.map; var cl = ctx.cl.clusterOf;
            if (A == KFan && B == KFan)
            {
                int ca = cl[IdOf(ka)], cb = cl[IdOf(kb)];
                return ca >= 0 && ca == cb ? RFanFanCluster : RFanFan;
            }
            if (A == KFan || B == KFan)
            {
                int node = A == KFan ? IdOf(ka) : IdOf(kb);
                var e = map.edges[A == KFan ? IdOf(kb) : IdOf(ka)];
                if (e.a == node || e.b == node) return RFanOwnArm;
                int c = cl[node];
                if (c >= 0 && (cl[e.a] == c || cl[e.b] == c)) return RFanClusterArm;
                return RFanForeign;
            }
            int ea = IdOf(ka), eb = IdOf(kb);
            if (ctx.clipPairs.Contains(PairKey(ea, eb))) return RBranchHost;
            var E = map.edges[ea]; var F = map.edges[eb];
            int shared = E.a == F.a || E.a == F.b ? E.a : E.b == F.a || E.b == F.b ? E.b : -1;
            if (shared < 0) return RRibbon;
            var tr = ctx.trims;
            // the two through ribbons of a mitred node meet on one section: a seam, not two arms
            if (tr.mitre[shared] && ((tr.throughA[shared] == ea && tr.throughB[shared] == eb) || (tr.throughA[shared] == eb && tr.throughB[shared] == ea))) return RMitre;
            return RArmArm;
        }

        /// <summary>How far into its host a branch's pavement reaches at q,
        /// from the host's edge on the branch's side; far = past the host's
        /// far lane edge (the whole host crossed).</summary>
        static float Penetration(CityMap map, CityMap.Edge host, CityMap.Edge branch, Vector2 q, out bool far)
        {
            far = false;
            CityElevation.ProjectOn(host, q, out float sh);
            var p = host.PointAt(sh); var t = host.TangentAt(sh); var left = new Vector2(-t.y, t.x);
            float lat = Vector2.Dot(q - p, left);
            LineModel.Extents(host, sh, out float eM, out float eP);
            CityElevation.ProjectOn(branch, q, out float sb);
            float latB = Vector2.Dot(branch.PointAt(sb) - p, left);
            int side = Mathf.Abs(latB) > 0.5f ? (latB > 0f ? 1 : -1) : (lat >= 0f ? 1 : -1);
            float depth = side > 0 ? eP - lat : lat + eM;
            far = depth > eM + eP - 0.25f;
            return Mathf.Max(0f, depth);
        }

        // =====================================================================
        //  The data gaps: dead ends just short of another road (critic C9)
        // =====================================================================
        /// <summary>
        /// DATA GAPS. COVERAGE's outline is built from the same edges, so a
        /// road that stops short of another road shows no hole. Every
        /// degree-1 node inside the map (the world edge's 0.004 deg margin
        /// left out), the nearest other edge at the same OSM layer not
        /// touching it, the distance from the node to that edge's pavement
        /// (its centreline less half its width): 0.3-6 m = SHORT (the gap a
        /// weld would close), under 0.3 m = INSIDE another road. By the dead
        /// end's tier. City-wide always: graph only, under a second.
        /// diag/critic/nearmiss.mjs is the same census offline (T1 3, T2 3,
        /// T3 18 short on 2026-10-02).
        /// </summary>
        public static void DeadEnds(CityMap map, CityMeshes.Trims trims, Action<string> Line, Result res)
        {
            var b0 = CityAuditLL(35.03 + 0.004, -81.03 + 0.004); var b1 = CityAuditLL(35.42 - 0.004, -80.62 - 0.004);
            var near = new HashSet<int>();
            var rows = new List<(int tier, bool shortGap, float d, string text)>();
            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (map.nodeEdges[n].Count != 1) continue;
                var p = map.nodes[n];
                if (p.x < b0.x || p.x > b1.x || p.y < b0.y || p.y > b1.y) continue;
                var e = map.edges[map.nodeEdges[n][0]];
                int tier = CityTier.Of(e);
                res.deadEnds[tier]++;
                near.Clear();
                map.EdgeSegsInRect(p - Vector2.one * 64f, p + Vector2.one * 64f, near);
                float best = float.MaxValue; CityMap.Edge bo = null;
                foreach (int packed in near)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    var o = map.edges[oi];
                    if (o == e || o.a == n || o.b == n || o.layer != e.layer || si + 1 >= o.pts.Length) continue;
                    Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                    float L2 = dq.sqrMagnitude;
                    float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - q0, dq) / L2) : 0f;
                    float d = Vector2.Distance(p, q0 + dq * tt) - o.width * 0.5f;
                    if (d < best) { best = d; bo = o; }
                }
                if (bo == null || best > 6f) continue;
                bool shortGap = best > 0.3f;
                if (shortGap) res.deadShort[tier]++; else res.deadInside[tier]++;
                rows.Add((tier, shortGap, best, string.Format(Inv, "{0} T{1} node {2} e{3} '{4}'{5} cls{6} way {7}: {8:0.00} m from e{9} '{10}'{11} cls{12} at {13}",
                    shortGap ? "SHORT " : "INSIDE", tier, n, e.index, e.name, e.link ? " L" : "", e.cls, e.wayId, best, bo.index, bo.name, bo.link ? " L" : "", bo.cls, CityAudit.LatLon(p.x, p.y))));
            }
            rows.Sort((a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier) : a.shortGap != b.shortGap ? (a.shortGap ? -1 : 1) : a.d.CompareTo(b.d));
            Line("");
            Line("COVERAGE DATA GAPS (critic C9; report only): interior dead ends near another road at the same layer - CITY-WIDE (graph only)");
            for (int t = 1; t <= 3; t++)
                Line($"  {CityTier.Short(t)}: {res.deadEnds[t]} dead ends; SHORT of a road by 0.3-6 m {res.deadShort[t]}, INSIDE a road's pavement {res.deadInside[t]}");
            int shown = 0;
            foreach (var r in rows) { if (shown++ >= 30) break; Line("    " + r.text); res.deadRows.Add(r.text); }
        }

        static Vector2 CityAuditLL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * Math.Cos(Lat0 * Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        // =====================================================================
        //  The report
        // =====================================================================
        static string M2(double a) => a >= 100 ? a.ToString("0", Inv) : a.ToString("0.0", Inv);

        static void Report(Ctx ctx, Action<string> Line)
        {
            var res = ctx.res; var map = ctx.map; var sc = ctx.sc;
            Line($"  {res.tilesEval} tiles rasterised ({res.tilesBuilt} built with their rings), {res.fineBlocks} 8 m blocks at {FineCell:0.00} m (junction + gore neighbourhoods) and {res.coarseBlocks} at {CoarseCell:0.00} m, {res.cells} cells, {res.seconds:0} s; untapped road triangles {res.untapped}; junction clusters {res.clusters} (T1 {res.clusterT1}, T2 {res.clusterT2}, T3 {res.clusterT3}); clip pairs {ctx.clipPairs.Count}");
            var a = res.all;
            Line($"  road surface m2 by tier: T1 {M2(a.road[1])}, T2 {M2(a.road[2])}, T3 {M2(a.road[3])}; outline m2: T1 {M2(a.outline[1])}, T2 {M2(a.outline[2])}, T3 {M2(a.outline[3])}");

            Line("COVERAGE COPLANAR (two owners within 5 cm: z-fight, texture pop; m2 by tier, a pair takes the better tier):");
            for (int r = 0; r < NRel; r++)
                Line($"    {RelName[r],-24} T1 {M2(a.coplanar[1, r]),8}  T2 {M2(a.coplanar[2, r]),8}  T3 {M2(a.coplanar[3, r]),8}");
            Line($"    {"of which a deck",-24} T1 {M2(a.coplanarDeck[1]),8}  T2 {M2(a.coplanarDeck[2]),8}  T3 {M2(a.coplanarDeck[3]),8}");
            Line($"    pairs over 2 m2: T1 {Count(res.copObj, 1, 2)}, T2 {Count(res.copObj, 2, 2)}, T3 {Count(res.copObj, 3, 2)}");
            Worst(Line, res.copObj, acc => $"{Who(map, acc.a)} / {Who(map, acc.b)} - {RelName[Relation(ctx, acc.a, acc.b)]}");

            Line("COVERAGE HOLES (outline cells, eroded one cell, with no road surface within 0.6 m of the outline's height; m2):");
            foreach (int k in new[] { KRibbon, KDeck, KFan, KGore, KCluster })
                Line($"    in a {KindName[k],-12} T1 {M2(a.holes[1, k]),8}  T2 {M2(a.holes[2, k]),8}  T3 {M2(a.holes[3, k]),8}");
            Line($"    objects with a hole over 2 m2: T1 {Count(res.holeObj, 1, 2)}, T2 {Count(res.holeObj, 2, 2)}, T3 {Count(res.holeObj, 3, 2)}; junction clusters among them: T1 {CountKind(res.holeObj, 1, 2, KCluster)}, T2 {CountKind(res.holeObj, 2, 2, KCluster)}, T3 {CountKind(res.holeObj, 3, 2, KCluster)}");
            Worst(Line, res.holeObj, acc => $"{KindName[KindOf(acc.a)]} {Who(map, acc.a)}");

            Line("COVERAGE OUTSIDE (road surface more than 0.25 m beyond every outline at its height; m2):");
            foreach (int k in new[] { KRibbon, KDeck, KFan, KGore })
                Line($"    {KindName[k],-12} T1 {M2(a.outside[1, k]),8}  T2 {M2(a.outside[2, k]),8}  T3 {M2(a.outside[3, k]),8}");
            Worst(Line, res.outObj, acc => $"{KindName[KindOf(acc.a)]} {Who(map, acc.a)}");

            Line("COVERAGE OVERSHOOT (a clipped branch's surface over its host's, within 0.6 m; m2, and of it past the host's far lane edge):");
            Line($"    branch over host   T1 {M2(a.overshoot[1]),8}  T2 {M2(a.overshoot[2]),8}  T3 {M2(a.overshoot[3]),8}");
            Line($"    past the far edge  T1 {M2(a.overshootFar[1]),8}  T2 {M2(a.overshootFar[2]),8}  T3 {M2(a.overshootFar[3]),8}");
            Line($"    branches over 1 m2: T1 {Count(res.overObj, 1, 1)}, T2 {Count(res.overObj, 2, 1)}, T3 {Count(res.overObj, 3, 1)}");
            Worst(Line, res.overObj, acc => $"branch {Who(map, Key(KRibbon, acc.tier, acc.a))} into host {Who(map, Key(KRibbon, acc.tier, acc.b))}, deepest {acc.depth:0.00} m");

            Line("COVERAGE UNDERLAP (a surface 0.5-8 cm under a road surface: flicker at range; m2 by what is under):");
            for (int u = 0; u < 3; u++)
                Line($"    {UnderName[u],-18} T1 {M2(a.underlap[1, u]),8}  T2 {M2(a.underlap[2, u]),8}  T3 {M2(a.underlap[3, u]),8}");
            Worst(Line, res.underObj, acc => $"{KindName[KindOf(acc.a)]} {Who(map, acc.a)} over {KindName[KindOf(acc.b)]}{(IsRoad(KindOf(acc.b)) ? " " + Who(map, acc.b) : "")}");
            // roads pass L8: the lattice's own worst (the shimmer the owner sees at range)
            var latUnder = new Dictionary<long, Acc>();
            foreach (var kv in res.underObj) if ((kv.Key >> 40) == 2) latUnder[kv.Key] = kv.Value;
            Line("    over the lattice:");
            Worst(Line, latUnder, acc => $"{KindName[KindOf(acc.a)]} {Who(map, acc.a)} over the lattice");

            if (res.full)
                foreach (var kv in res.boxes)
                {
                    var b = kv.Value;
                    Line($"  sub-box {kv.Key}: coplanar T1 {M2(Sum(b.coplanar, 1))} T2 {M2(Sum(b.coplanar, 2))} T3 {M2(Sum(b.coplanar, 3))}; holes T1 {M2(Sum(b.holes, 1))} T2 {M2(Sum(b.holes, 2))} T3 {M2(Sum(b.holes, 3))}; outside T1 {M2(Sum(b.outside, 1))}; overshoot T1 {M2(b.overshoot[1])}; underlap T1 {M2(Sum(b.underlap, 1))} T2 {M2(Sum(b.underlap, 2))}");
                }
        }

        static double Sum(double[,] a, int t) { double s = 0; for (int k = 0; k < a.GetLength(1); k++) s += a[t, k]; return s; }
        static int Count(Dictionary<long, Acc> d, int tier, double over) { int n = 0; foreach (var v in d.Values) if (v.tier == tier && v.area > over) n++; return n; }
        static int CountKind(Dictionary<long, Acc> d, int tier, double over, int kind) { int n = 0; foreach (var v in d.Values) if (v.tier == tier && v.area > over && KindOf(v.a) == kind) n++; return n; }

        static void Worst(Action<string> Line, Dictionary<long, Acc> d, Func<Acc, string> what)
        {
            var l = new List<Acc>(d.Values);
            l.Sort((p, q) => p.tier != q.tier ? p.tier.CompareTo(q.tier) : q.area.CompareTo(p.area));
            var per = new int[4];
            foreach (var acc in l)
            {
                if (acc.tier < 1 || acc.tier > 3 || per[acc.tier]++ >= (acc.tier == 1 ? 8 : 4)) continue;
                Line(string.Format(Inv, "      {0} {1,7} m2 at ({2:0},{3:0}) {4}: {5}", CityTier.Short(acc.tier), M2(acc.area), acc.x, acc.z, CityAudit.LatLon(acc.x, acc.z), what(acc)));
            }
        }

        static string Who(CityMap map, int key)
        {
            int kind = KindOf(key), id = IdOf(key);
            if (kind == KFan) return $"n{id}";
            if (kind == KCluster) return $"cluster c{id}";
            if (kind == KStrip || kind == KLattice) return KindName[kind];
            if (id < 0 || id >= map.edges.Length) return "?";
            var e = map.edges[id];
            return $"e{id} '{e.name}'{(e.link ? " L" : "")} cls{e.cls}";
        }

        // =====================================================================
        //  Outputs
        // =====================================================================
        /// <summary>city_coverage.json at the project root: every number of
        /// the report, for tools/city/baseline/mesh_audit_baseline.json.</summary>
        public static void WriteOutputs(Result res, string path)
        {
            if (res == null) return;
            string root = Directory.GetParent(Application.dataPath).FullName;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"scope\": \"{res.scope.Replace("\"", "'")}\",\n  \"full\": {(res.full ? "true" : "false")},\n");
            sb.Append($"  \"tiles\": {res.tilesEval}, \"seconds\": {res.seconds.ToString("0", Inv)}, \"untapped\": {res.untapped},\n");
            sb.Append($"  \"clusters\": {{ \"T1\": {res.clusterT1}, \"T2\": {res.clusterT2}, \"T3\": {res.clusterT3} }},\n");
            sb.Append("  \"all\": "); Json(sb, res.all, res, "  "); sb.Append(",\n");
            sb.Append("  \"boxes\": {");
            bool first = true;
            foreach (var kv in res.boxes) { sb.Append(first ? "\n" : ",\n"); first = false; sb.Append($"    \"{kv.Key}\": "); Json(sb, kv.Value, null, "    "); }
            sb.Append(first ? "},\n" : "\n  },\n");
            sb.Append("  \"deadEnds\": {");
            for (int t = 1; t <= 3; t++) sb.Append($"{(t > 1 ? ", " : " ")}\"T{t}\": {{ \"ends\": {res.deadEnds[t]}, \"short\": {res.deadShort[t]}, \"inside\": {res.deadInside[t]} }}");
            sb.Append(" }\n}\n");
            File.WriteAllText(path ?? Path.Combine(root, "city_coverage.json"), sb.ToString());
        }

        static void Json(StringBuilder sb, Tallies a, Result res, string ind)
        {
            string R(double v) => v.ToString("0.00", Inv);
            sb.Append("{\n");
            for (int t = 1; t <= 3; t++)
            {
                sb.Append($"{ind}  \"T{t}\": {{\n");
                sb.Append($"{ind}    \"roadM2\": {R(a.road[t])}, \"outlineM2\": {R(a.outline[t])},\n");
                sb.Append($"{ind}    \"coplanarM2\": {{");
                for (int r = 0; r < NRel; r++) sb.Append($"{(r > 0 ? ", " : " ")}\"{RelKey[r]}\": {R(a.coplanar[t, r])}");
                sb.Append($", \"deck\": {R(a.coplanarDeck[t])} }},\n");
                sb.Append($"{ind}    \"holesM2\": {{");
                bool f = true;
                foreach (int k in new[] { KRibbon, KDeck, KFan, KGore, KCluster }) { sb.Append($"{(f ? " " : ", ")}\"{HoleKey[k]}\": {R(a.holes[t, k])}"); f = false; }
                sb.Append(" },\n");
                sb.Append($"{ind}    \"outsideM2\": {{ \"ribbon\": {R(a.outside[t, KRibbon])}, \"deck\": {R(a.outside[t, KDeck])}, \"fan\": {R(a.outside[t, KFan])}, \"gore\": {R(a.outside[t, KGore])} }},\n");
                sb.Append($"{ind}    \"overshootM2\": {R(a.overshoot[t])}, \"overshootFarM2\": {R(a.overshootFar[t])},\n");
                sb.Append($"{ind}    \"underlapM2\": {{ \"road\": {R(a.underlap[t, 0])}, \"strip\": {R(a.underlap[t, 1])}, \"lattice\": {R(a.underlap[t, 2])} }}");
                if (res != null)
                    sb.Append($",\n{ind}    \"objects\": {{ \"coplanarPairsOver2m2\": {Count(res.copObj, t, 2)}, \"holesOver2m2\": {Count(res.holeObj, t, 2)}, \"clusterHolesOver2m2\": {CountKind(res.holeObj, t, 2, KCluster)}, \"branchesOver1m2\": {Count(res.overObj, t, 1)} }}");
                sb.Append($"\n{ind}  }}{(t < 3 ? "," : "")}\n");
            }
            sb.Append($"{ind}}}");
        }
    }
}
