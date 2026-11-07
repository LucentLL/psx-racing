using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// MEDIAN NOSES, CITY-WIDE (night 11-06). At Queens Road n1230 the nose
    /// of the median - where a divided street's two carriageways meet a
    /// junction (one in, one out) - was the bare lattice 13-19 cm under the
    /// pavement for ~8 m. This finds every such nose (arm A one-way in, arm B
    /// one-way out, one road, under 120 deg apart: the game's own nose test,
    /// CurbRadiusAt), stands the tiles up as the game does, and maps the
    /// colliders over the nose: the fan's ring round it (CityMeshes
    /// NoseRingLog) and each carriageway's median edge
    /// <see cref="UpM"/> back from its drawn end, every <see cref="CellM"/>.
    /// A cell's top surface is read against the pavement edge nearest it
    /// (the Roads collider 0.3 m in from that edge): ground more than
    /// <see cref="HoleM"/> under it is a hole. Per nose: cells, holes, the
    /// deepest, the strip that laid the ground there (CityMeshes.groundLog).
    /// PSX_NOSE_BOX "x0,z0,x1,z1" limits it; PSX_NOSE_NODES "n,n" too.
    /// Writes city_nose_census.txt and .csv at the project root.
    /// </summary>
    public static class CityNoseCensus
    {
        const float UpM = 8f, CellM = 0.5f, HoleM = 0.03f, LipM = 1.0f, InM = 0.3f;

        class Nose
        {
            public int n, A, B; public float phi; public bool split; public int arms;
            public List<Vector3> poly; public List<float> refY; public bool ring;
            public string line;
        }

        /// <summary>The nose tiles' build cost: every tile holding a nose node
        /// built once untimed, then three timed passes; the median pass's
        /// milliseconds and the ground meshes' size. A/B by PSX_CITY_NOSE_ALL
        /// in the same sandbox. Writes city_nose_tilecost.txt.</summary>
        [MenuItem("PSX Racing/Nose Tiles Build Cost")]
        public static void TileCost()
        {
            var map = CityMap.Get();
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var tiles = new HashSet<long>();
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var ne = map.nodeEdges[n];
                if (ne == null || ne.Count < 2) continue;
                bool nose = false;
                foreach (int i in ne) foreach (int j in ne)
                {
                    var a = map.edges[i]; var b = map.edges[j];
                    if (a != b && !a.link && !b.link && !string.IsNullOrEmpty(a.name) && a.name == b.name && a.oneway && b.oneway && a.b == n && b.a == n && Vector2.Angle(Out(a, n), Out(b, n)) < 120f) nose = true;
                }
                if (!nose) continue;
                int tx = Mathf.FloorToInt(map.nodes[n].x / CityMeshes.TileSize), tz = Mathf.FloorToInt(map.nodes[n].y / CityMeshes.TileSize);
                tiles.Add(((long)tx << 32) ^ (uint)tz);
            }
            var list = new List<long>(tiles);
            list.Sort();
            double[] pass = new double[3]; long verts = 0, tris = 0;
            for (int p = -1; p < 3; p++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var key in list)
                {
                    int tx = (int)(key >> 32), tz = (int)(uint)(key & 0xffffffffL);
                    var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                    if (p == 0 && tm.ground != null) { verts += tm.ground.vertexCount; tris += tm.ground.triangles.Length / 3; }
                    foreach (var fi in typeof(CityMeshes.TileMeshes).GetFields())
                        if (fi.FieldType == typeof(Mesh) && fi.GetValue(tm) is Mesh m && m != null) Object.DestroyImmediate(m);
                }
                if (p >= 0) pass[p] = sw.Elapsed.TotalMilliseconds;
            }
            System.Array.Sort(pass);
            string line = $"NOSE TILES BUILD COST (PSX_CITY_NOSE_ALL={(CityMeshes.NoseAllOn ? "on" : "0")}): {list.Count} tiles, passes {pass[0]:0} / {pass[1]:0} / {pass[2]:0} ms (median {pass[1] / list.Count:0.00} ms a tile); ground meshes {verts} vertices, {tris} triangles";
            string rootDir = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(rootDir, "city_nose_tilecost.txt"), line + "\n");
            Debug.Log("[NoseTileCost] " + line);
        }

        [MenuItem("PSX Racing/Census Charlotte Median Noses")]
        public static void Run()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var map = CityMap.Get();
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var inv = CultureInfo.InvariantCulture;
            Rect box = Rect.MinMaxRect(-1e9f, -1e9f, 1e9f, 1e9f);
            var bx = System.Environment.GetEnvironmentVariable("PSX_NOSE_BOX");
            if (!string.IsNullOrEmpty(bx)) { var f = bx.Split(','); box = Rect.MinMaxRect(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv), float.Parse(f[3], inv)); }
            HashSet<int> only = null;
            var on = System.Environment.GetEnvironmentVariable("PSX_NOSE_NODES");
            if (!string.IsNullOrEmpty(on)) { only = new HashSet<int>(); foreach (var t in on.Split(',')) if (int.TryParse(t.Trim().TrimStart('n'), out int v)) only.Add(v); }

            // the noses, by the tile their node stands in
            var byTile = new Dictionary<long, List<Nose>>();
            int tx0 = int.MaxValue, tx1 = int.MinValue, tz0 = int.MaxValue, tz1 = int.MinValue, nNoses = 0, skippedDeck = 0, pairsAny = 0;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var ne = map.nodeEdges[n];
                if (ne == null || ne.Count < 2 || !box.Contains(map.nodes[n]) || (only != null && !only.Contains(n))) continue;
                foreach (int i in ne)
                    foreach (int j in ne)
                    {
                        var a = map.edges[i]; var b = map.edges[j];
                        if (a == b || a.link || b.link || string.IsNullOrEmpty(a.name) || a.name != b.name || !a.oneway || !b.oneway || a.a == a.b || b.a == b.b) continue;
                        if (a.b != n || b.a != n) continue;
                        pairsAny++;
                        var da = Out(a, n); var db = Out(b, n);
                        float phi = Vector2.Angle(da, db);
                        if (phi >= 120f) continue;
                        if (a.tunnel || b.tunnel || a.bridge || b.bridge || a.ElevatedAt(a.length) || b.ElevatedAt(0f)) { skippedDeck++; continue; }
                        bool split = false;
                        foreach (int k in ne) { var o = map.edges[k]; if (o != a && o != b && !o.oneway && o.name == a.name) split = true; }
                        var nose = new Nose { n = n, A = i, B = j, phi = phi, split = split, arms = ne.Count };
                        int tx = Mathf.FloorToInt(map.nodes[n].x / CityMeshes.TileSize), tz = Mathf.FloorToInt(map.nodes[n].y / CityMeshes.TileSize);
                        long key = ((long)tx << 32) ^ (uint)tz;
                        if (!byTile.TryGetValue(key, out var l)) byTile[key] = l = new List<Nose>();
                        l.Add(nose); nNoses++;
                        tx0 = Mathf.Min(tx0, tx); tx1 = Mathf.Max(tx1, tx); tz0 = Mathf.Min(tz0, tz); tz1 = Mathf.Max(tz1, tz);
                    }
            }
            var sb = new StringBuilder();
            var csv = new StringBuilder("node,A,B,name,kind,arms,cls,barriered,ring,phi,x,z,gap8,cells,paved,ground,raised,other,none,holes,lipHoles,maxDepth,deepX,deepZ,deepWhere,deepTag,deepSlot,islandA,islandB,flatA,flatB,tags,sheet\n");
            sb.AppendLine($"MEDIAN NOSE CENSUS: {nNoses} noses (one road's one-way carriageways, one in and one out, under 120 deg apart) in {byTile.Count} tiles; {skippedDeck} on decks/tunnels skipped; one-in/one-out pairs at any angle {pairsAny}");
            sb.AppendLine($"  region: the fan's ring round the nose + each median edge {UpM:0} m back from its drawn end; {CellM:0.0} m cells; a hole = ground over {HoleM * 100f:0} cm under both carriageways' nearest pavement edges (the lower of the two)");
            CityMeshes.NoseRingLog = new Dictionary<string, List<Vector3>>();
            CityMeshes.NoseSheetLog.Clear(); CityMeshes.NoseSheets = CityMeshes.NoseSheetsNoIsland = CityMeshes.NoseFillTangled = CityMeshes.NoseSheetsCut = 0;
            CityMeshes.CurbMedianByEdge.Clear(); CityMeshes.CurbMedianFlatByEdge.Clear();
            var root = new GameObject("~nosecensus");
            var live = new Dictionary<long, (GameObject go, Mesh[] meshes, TriIndex log)>();
            var slotsOf = new Dictionary<Collider, CityMeshes.Slot[]>();
            var meshOf = new Dictionary<Collider, Mesh>();
            var done = new List<(float depth, int holes, Nose nose)>();
            try
            {
                for (int z = tz0 - 1; z <= tz1 + 1; z++)
                {
                    for (int x = tx0 - 1; x <= tx1 + 1; x++)
                    {
                        long key = ((long)x << 32) ^ (uint)z;
                        bool need = false;
                        for (int dz = -1; dz <= 1 && !need; dz++) for (int dx = -1; dx <= 1 && !need; dx++) need = byTile.ContainsKey(((long)(x + dx) << 32) ^ (uint)(z + dz));
                        if (!need) continue;
                        var log = new List<(string, Vector3, Vector3, Vector3)>();
                        CityMeshes.groundLog = log;
                        CityMeshes.TileMeshes tm;
                        try { tm = CityMeshes.Build(map, trims, buildings, x, z); }
                        finally { CityMeshes.groundLog = null; }
                        var go = new GameObject($"tile_{x}_{z}");
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = tm.origin;
                        var ms = CityWorld.Attach(go, tm, null);
                        foreach (var c in go.GetComponentsInChildren<MeshCollider>())
                        {
                            meshOf[c] = c.sharedMesh;
                            if (c.name == "Ground") slotsOf[c] = tm.groundSlots;
                            else if (c.name == "Roads") slotsOf[c] = tm.roadSlots;
                        }
                        live[key] = (go, ms, new TriIndex(log));
                    }
                    Physics.SyncTransforms();
                    int rz = z - 1;
                    for (int x = tx0; x <= tx1; x++)
                    {
                        if (!byTile.TryGetValue(((long)x << 32) ^ (uint)rz, out var noses)) continue;
                        foreach (var nose in noses)
                        {
                            Measure(map, trims, nose, live, slotsOf, meshOf, out float depth, out int holes);
                            done.Add((depth, holes, nose));
                        }
                    }
                    int dropZ = z - 2;
                    for (int x = tx0 - 1; x <= tx1 + 1; x++)
                    {
                        long key = ((long)x << 32) ^ (uint)dropZ;
                        if (!live.TryGetValue(key, out var t)) continue;
                        foreach (var c in t.go.GetComponentsInChildren<MeshCollider>()) { slotsOf.Remove(c); meshOf.Remove(c); }
                        foreach (var m in t.meshes) if (m != null) Object.DestroyImmediate(m);
                        Object.DestroyImmediate(t.go);
                        live.Remove(key);
                    }
                }
            }
            finally
            {
                CityMeshes.NoseRingLog = null;
                foreach (var t in live.Values) { foreach (var m in t.meshes) if (m != null) Object.DestroyImmediate(m); if (t.go != null) Object.DestroyImmediate(t.go); }
                Object.DestroyImmediate(root);
            }
            done.Sort((p, q) => q.depth.CompareTo(p.depth));
            int withHole = 0, withHole2 = 0, withLip = 0, noRing = 0;
            foreach (var d in done) { if (d.holes >= 1) withHole++; if (d.holes >= 2) withHole2++; if (d.nose.line.Contains(",lip+")) withLip++; if (!d.nose.ring) noRing++; }
            sb.AppendLine($"noses with a hole cell: {withHole} of {done.Count} (2+ cells: {withHole2}); no fan ring found (chord used): {noRing}");
            sb.AppendLine($"nose sheets laid {CityMeshes.NoseSheets} ({CityMeshes.NoseSheetsNoIsland} with no island behind, {CityMeshes.NoseSheetsCut} cut short at other pavement); outlines crossing themselves (left out) {CityMeshes.NoseFillTangled}");
            sb.AppendLine("worst 60 (node A B name kind ... maxDepth deepest at, tag):");
            for (int i = 0; i < Mathf.Min(60, done.Count); i++) sb.AppendLine("  " + done[i].nose.line);
            foreach (var d in done) csv.AppendLine(d.nose.line);
            sb.AppendLine($"{clock.Elapsed.TotalSeconds:0} s");
            string rootDir = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(rootDir, "city_nose_census.txt"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(rootDir, "city_nose_census.csv"), csv.ToString());
            Debug.Log("[NoseCensus]\n" + sb);
        }

        /// <summary>The arm's direction out of node n, read 6 m out.</summary>
        static Vector2 Out(CityMap.Edge e, int n)
        {
            float s = e.a == n ? Mathf.Min(6f, e.length) : Mathf.Max(0f, e.length - 6f);
            var v = e.PointAt(s) - map0(e, n);
            return v.sqrMagnitude > 1e-6f ? v.normalized : (e.a == n ? e.TangentAt(0f) : -e.TangentAt(e.length));
        }
        static Vector2 map0(CityMap.Edge e, int n) => e.a == n ? e.PointAt(0f) : e.PointAt(e.length);

        static Vector3 EdgeAt(CityMap.Edge e, float s, out Vector2 inward)
        {
            LineModel.Extents(e, s, out _, out float eP);
            var left = LineModel.SectionLeft(e, s);
            var p = e.PointAt(s) + left * eP;
            inward = -left;
            return new Vector3(p.x, e.YAt(s), p.y);
        }

        static void Measure(CityMap map, CityMeshes.Trims trims, Nose nose,
                            Dictionary<long, (GameObject go, Mesh[] meshes, TriIndex log)> live,
                            Dictionary<Collider, CityMeshes.Slot[]> slotsOf, Dictionary<Collider, Mesh> meshOf, out float maxDepth, out int holes)
        {
            var inv = CultureInfo.InvariantCulture;
            var A = map.edges[nose.A]; var B = map.edges[nose.B]; int n = nose.n;
            float sA = A.length - trims.atB[A.index], sB = trims.atA[B.index];
            var poly = new List<Vector3>(); var inw = new List<Vector2>();
            for (float d = UpM; d > 0.05f; d -= CellM) { poly.Add(EdgeAt(A, Mathf.Max(0f, sA - d), out var iw)); inw.Add(iw); }
            poly.Add(EdgeAt(A, sA, out var iwa)); inw.Add(iwa);
            int nA0 = poly.Count - 1;
            nose.ring = CityMeshes.NoseRingLog.TryGetValue($"{n}:{A.index}:{B.index}", out var ring) && ring.Count >= 2;
            var c2 = map.nodes[n];
            if (nose.ring)
                foreach (var r in ring)
                {
                    var iw = new Vector2(c2.x - r.x, c2.y - r.z);
                    poly.Add(r); inw.Add(iw.sqrMagnitude > 1e-6f ? iw.normalized : Vector2.zero);
                }
            int mid = (nA0 + poly.Count) / 2;   // A's side of the outline .. mid, B's from mid
            poly.Add(EdgeAt(B, sB, out var iwb)); inw.Add(iwb);
            for (float d = CellM; d <= UpM + 1e-3f; d += CellM) { poly.Add(EdgeAt(B, Mathf.Min(B.length, sB + d), out var iw)); inw.Add(iw); }
            // each boundary point's pavement height, read off the Roads 0.3 m in
            var refY = new List<float>(poly.Count);
            for (int i = 0; i < poly.Count; i++)
            {
                var q = new Vector2(poly[i].x, poly[i].z) + inw[i] * InM;
                float y = poly[i].y;
                foreach (var h in Physics.RaycastAll(new Vector3(q.x, poly[i].y + 3f, q.y), Vector3.down, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    if (h.collider.name == "Roads" && Mathf.Abs(h.point.y - poly[i].y) < 0.5f) { y = h.point.y; break; }
                refY.Add(y);
            }
            // the cells
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in poly) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            int cells = 0, paved = 0, ground = 0, raised = 0, other = 0, none = 0, lip = 0, planeHoles = 0; holes = 0; maxDepth = 0f;
            Vector3 deep = default; string deepTag = "", deepSlot = ""; int deepSeg = -1;
            var tagCount = new Dictionary<string, int>();
            for (float qx = Mathf.Floor(minX / CellM) * CellM + CellM * 0.5f; qx < maxX; qx += CellM)
                for (float qz = Mathf.Floor(minZ / CellM) * CellM + CellM * 0.5f; qz < maxZ; qz += CellM)
                {
                    var q = new Vector2(qx, qz);
                    if (!Inside(poly, q)) continue;
                    cells++;
                    // against the median's plane between its two edges: each side's
                    // nearest pavement edge, weighted by distance (a median on a
                    // cross slope is at grade when it lies between them)
                    float dA = Nearest(poly, refY, q, 0, mid, out float yA, out int segA);
                    float dB = Nearest(poly, refY, q, mid, poly.Count - 1, out float yB, out int segB);
                    float ryPlane = (yA * dB + yB * dA) / Mathf.Max(1e-4f, dA + dB);
                    // a hole: under BOTH carriageways' edges (a median on a cross
                    // slope may lie anywhere between them); the plane between
                    // them is counted too (plane:N in the tags)
                    float ry = Mathf.Min(yA, yB);
                    float bd = Mathf.Min(dA, dB); int seg = dA <= dB ? segA : segB;
                    RaycastHit best = default; bool found = false;
                    foreach (var h in Physics.RaycastAll(new Vector3(qx, ry + 3f, qz), Vector3.down, 12f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        if (!found || h.point.y > best.point.y) { best = h; found = true; }
                    if (!found) { none++; holes++; continue; }
                    string nm = best.collider.name;
                    if (nm == "Roads") { paved++; continue; }
                    if (nm != "Ground") { other++; continue; }
                    float depth = ry - best.point.y;
                    if (ryPlane - best.point.y > HoleM) planeHoles++;
                    if (depth < -0.05f && ryPlane - best.point.y < -0.05f) { raised++; continue; }
                    ground++;
                    if (depth <= HoleM) continue;
                    holes++;
                    if (bd <= LipM) lip++;
                    string tag = TagAt(live, best.point);
                    string tk = TagKind(tag);
                    tagCount.TryGetValue(tk, out int tc); tagCount[tk] = tc + 1;
                    if (depth > maxDepth) { maxDepth = depth; deep = best.point; deepTag = tag; deepSlot = SlotAt(best, slotsOf, meshOf); deepSeg = seg; }
                }
            int nA = Mathf.RoundToInt(UpM / CellM) + 1, nB = nA;
            string where = deepSeg < 0 ? "" : deepSeg < nA - 1 ? $"A-{UpM - deepSeg * CellM:0.0}m" : deepSeg >= poly.Count - nB ? $"B+{(deepSeg - (poly.Count - nB)) * CellM:0.0}m" : "ring";
            float gap8 = Vector2.Distance(new Vector2(poly[0].x, poly[0].z), new Vector2(poly[poly.Count - 1].x, poly[poly.Count - 1].z));
            var tags = new StringBuilder();
            foreach (var kv in tagCount) tags.Append($"{kv.Key}:{kv.Value} ");
            tags.Append($"plane:{planeHoles}");
            CityMeshes.CurbMedianByEdge.TryGetValue(A.index, out float iA); CityMeshes.CurbMedianByEdge.TryGetValue(B.index, out float iB);
            CityMeshes.CurbMedianFlatByEdge.TryGetValue(A.index, out float fA); CityMeshes.CurbMedianFlatByEdge.TryGetValue(B.index, out float fB);
            string Clean(string s) => s.Replace(',', ';').Replace('\n', ' ');
            nose.line = string.Format(inv, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9:0},{10:0.0},{11:0.0},{12:0.0},{13},{14},{15},{16},{17},{18},{19},{20},{21:0.000},{22:0.0},{23:0.0},{31},{24},{25},{26:0},{27:0},{28:0},{29:0},{30},{32}",
                n, A.index, B.index, Clean(A.name), nose.split ? "split" : "junction", nose.arms, A.cls, CityMeshes.Barriered(A) || CityMeshes.Barriered(B) ? 1 : 0, nose.ring ? 1 : 0, nose.phi,
                map.nodes[n].x, map.nodes[n].y, gap8, cells, paved, ground, raised, other, none, holes, lip > 0 ? "lip+" + lip : "0", maxDepth, deep.x, deep.z, Clean(deepTag), deepSlot, iA, iB, fA, fB, Clean(tags.ToString().Trim()), where, CityMeshes.NoseSheetLog.TryGetValue($"{n}:{A.index}:{B.index}", out var sheetLine) ? sheetLine : "");
        }

        static string TagKind(string tag)
        {
            if (tag == "the lattice") return "lattice";
            if (tag.StartsWith("fan chord verge")) return "fanverge";
            if (tag.StartsWith("corner fill")) return "cornerfill";
            if (tag.StartsWith("nose fill")) return "nosefill";
            if (tag.StartsWith("nose sheet")) return "nosesheet";
            if (tag.StartsWith("gore")) return "gore";
            if (tag.StartsWith("corner walk")) return "cornerwalk";
            if (tag.Contains("curb median")) return "curbmedian";
            if (tag.StartsWith("e") && tag.Contains(" side ")) return tag.Contains("verge") || tag.Contains("band") ? "ribbonverge" : "ribbonside";
            return "other";
        }

        static bool Inside(List<Vector3> poly, Vector2 p)
        {
            int wn = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                if (a.z <= p.y) { if (b.z > p.y && ((b.x - a.x) * (p.y - a.z) - (p.x - a.x) * (b.z - a.z)) > 0f) wn++; }
                else if (b.z <= p.y && ((b.x - a.x) * (p.y - a.z) - (p.x - a.x) * (b.z - a.z)) < 0f) wn--;
            }
            return wn != 0;
        }

        static float Nearest(List<Vector3> poly, List<float> refY, Vector2 p, int from, int to, out float y, out int seg)
        {
            float best = float.MaxValue; y = refY[from]; seg = from;
            for (int i = from; i < to; i++)
            {
                int j = i + 1;
                var a = new Vector2(poly[i].x, poly[i].z); var b = new Vector2(poly[j].x, poly[j].z);
                var ab = b - a; float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                // the closing line between the two 8 m points is not a pavement edge
                if (j == 0) continue;
                if (d < best) { best = d; y = Mathf.Lerp(refY[i], refY[j], t); seg = i; }
            }
            return best;
        }

        /// <summary>A tile's ground log with a 4 m grid over it, built on first use.</summary>
        class TriIndex
        {
            public readonly List<(string tag, Vector3 a, Vector3 b, Vector3 c)> log;
            Dictionary<long, List<int>> grid;
            public TriIndex(List<(string, Vector3, Vector3, Vector3)> l) { log = l; }
            const float G = 4f;
            static long K(int x, int z) => ((long)x << 32) ^ (uint)z;
            public List<int> At(Vector2 q)
            {
                if (grid == null)
                {
                    grid = new Dictionary<long, List<int>>();
                    for (int i = 0; i < log.Count; i++)
                    {
                        var (_, a, b, c) = log[i];
                        int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / G), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / G);
                        int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / G), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / G);
                        if ((x1 - x0 + 1) * (z1 - z0 + 1) > 400) continue;
                        for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
                        {
                            if (!grid.TryGetValue(K(x, z), out var l)) grid[K(x, z)] = l = new List<int>();
                            l.Add(i);
                        }
                    }
                }
                return grid.TryGetValue(K(Mathf.FloorToInt(q.x / G), Mathf.FloorToInt(q.y / G)), out var r) ? r : null;
            }
        }

        static string TagAt(Dictionary<long, (GameObject go, Mesh[] meshes, TriIndex log)> live, Vector3 p)
        {
            string best = "the lattice"; float bestDy = 0.03f;
            int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.z / CityMeshes.TileSize);
            var q = new Vector2(p.x, p.z);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!live.TryGetValue(((long)(tx + dx) << 32) ^ (uint)(tz + dz), out var t) || t.log == null) continue;
                    var cand = t.log.At(q);
                    if (cand == null) continue;
                    foreach (int ti in cand)
                    {
                        var (tag, a, b, c) = t.log.log[ti];
                        Vector2 v0 = new Vector2(b.x - a.x, b.z - a.z), v1 = new Vector2(c.x - a.x, c.z - a.z), v2 = new Vector2(q.x - a.x, q.y - a.z);
                        float den = v0.x * v1.y - v1.x * v0.y;
                        if (Mathf.Abs(den) < 1e-9f) continue;
                        float u = (v2.x * v1.y - v1.x * v2.y) / den, v = (v0.x * v2.y - v2.x * v0.y) / den;
                        if (u < -1e-4f || v < -1e-4f || u + v > 1f + 1e-4f) continue;
                        float y = a.y + (b.y - a.y) * u + (c.y - a.y) * v;
                        float d = Mathf.Abs(y - p.y);
                        if (d < bestDy) { bestDy = d; best = tag; }
                    }
                }
            return best;
        }

        static string SlotAt(RaycastHit h, Dictionary<Collider, CityMeshes.Slot[]> slotsOf, Dictionary<Collider, Mesh> meshOf)
        {
            if (!slotsOf.TryGetValue(h.collider, out var slots) || !meshOf.TryGetValue(h.collider, out var mesh) || mesh == null) return "";
            int ti = h.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var d = mesh.GetSubMesh(s);
                if (ti >= d.indexStart && ti < d.indexStart + d.indexCount) return s < slots.Length ? slots[s].ToString() : "sub" + s;
            }
            return "?";
        }
    }
}
