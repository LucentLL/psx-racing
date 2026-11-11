using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SEAM AUDIT (owner 2026-11-11, North Graham Street: "I see through
    /// gaps in the road at every intersection"). Each scanned tile is built on
    /// its own (CityMeshes.Build, CityWorld.Attach; every drawn mesh given a
    /// collider for the probe, back faces never hit, as the PSX shaders cull
    /// them). The Roads mesh's LYING triangles (the sheets: ribbons, fans,
    /// decks, lots in the fans' slot; the slab's own tongues left out) are
    /// welded at 0.5 mm - two sheets share an edge only where their vertices
    /// are the same to half a millimetre, so a 2 mm weld's sliver or a
    /// T-junction's vertex counts as unshared - and every edge one triangle
    /// uses is walked every 0.25 m (off the tile's border):
    /// <list type="bullet">
    /// <item>JOIN: pavement lies 5 cm past it within 2 mm under it to 10 cm
    /// over it (another sheet carries on: a fan's mouth, a ribbon's end, a
    /// lane drop, a lot). From just under the edge, 2 cm out, a ray straight
    /// down must meet paving within 6 cm (the crack between the two sheets
    /// shows asphalt). Else an OPEN JOIN metre: the eye sees through.</item>
    /// <item>STEP: whatever lies 5 cm past is lower by more than 2 mm (the
    /// fan's 12 mm over a ribbon's end; a verge 2 cm under; the land). A ray
    /// from 6 cm out, half way down the step, back at the edge must meet a
    /// face turned out within 7 cm. Else an OPEN STEP metre: under the sheet
    /// the eye sees ground or sky.</item>
    /// </list>
    /// The gate: open join metres + open step metres = 0. Also: the joins'
    /// height offsets (what kind of crack), the live tile against its baked
    /// copy (CityTileCache Encode/Write/Read/Apply, vertex and index exact),
    /// and road vertices on a border shared by two scanned tiles that the
    /// other tile has no vertex for (world, 0.1 mm).
    ///
    ///   -executeMethod PSXRacing.EditorTools.CitySeamAudit.Run
    ///   PSX_SEAM_SCOPE=owner,uptown,sample (default all three); PSX_SEAM_SAMPLE=60;
    ///   PSX_SEAM_MODES=before,after (PSX_CITY_ROADSLAB off / on; default both)
    /// Writes city_seam_audit.txt at the project root.
    /// </summary>
    public static class CitySeamAudit
    {
        const float StepM = 0.25f, BorderPadM = 0.4f;
        const float WeldM = 0.0005f;   // a gap under half a millimetre is under a tenth of a pixel at 5 m on 720 lines

        struct TileStat
        {
            public float joinM, joinOpenM, stepM, stepOpenM;
            public float joinLevelM, joinRiserM, joinHighM;   // dy past: within 2 mm, 2-20 mm, more
            public int tris, slabTris; public double buildMs, slabMs;
        }

        public static void Run()
        {
            var sb = new StringBuilder();
            string file = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_seam_audit.txt");
            int code = 0;
            bool slabWas = CityMeshes.RoadSlabOn;
            if (CityMeshes.slabProbeAt.x > -1e8f) CityMeshes.slabProbeLog = new List<string>();
            try
            {
                var map = CityMap.Get();
                if (map == null) throw new System.Exception("no city data");
                var trims = CityMeshes.ComputeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                float T = CityMeshes.TileSize;
                string scopeEnv = System.Environment.GetEnvironmentVariable("PSX_SEAM_SCOPE");
                var scope = new HashSet<string>((string.IsNullOrEmpty(scopeEnv) ? "owner,uptown,sample" : scopeEnv).Split(','));
                string modesEnv = System.Environment.GetEnvironmentVariable("PSX_SEAM_MODES");
                var modes = (string.IsNullOrEmpty(modesEnv) ? "before,after" : modesEnv).Split(',');
                int sampleN = int.TryParse(System.Environment.GetEnvironmentVariable("PSX_SEAM_SAMPLE"), out int sn) ? sn : 60;

                // the sets of tiles
                var sets = new List<(string name, List<(int x, int z)> tiles)>();
                if (scope.Contains("owner"))
                {
                    var pl = CitySeamPlaces.Find(map, trims, sb);
                    var ownerTiles = new List<(int, int)>();
                    foreach (var p in pl)
                    {
                        if (p.id != "owner_graham") continue;
                        var c = p.seam;
                        ownerTiles.Add((Mathf.FloorToInt(c.x / T), Mathf.FloorToInt(c.z / T)));
                    }
                    sets.Add(("owner tile", ownerTiles));
                    var places = new List<(int, int)>();
                    foreach (var p in pl) { var t = (Mathf.FloorToInt(p.seam.x / T), Mathf.FloorToInt(p.seam.z / T)); if (!places.Contains(t)) places.Add(t); }
                    sets.Add(("the six junction places' tiles", places));
                }
                if (scope.Contains("uptown"))
                {
                    var up = new List<(int, int)>();
                    int ux = Mathf.FloorToInt(map.uptown.x / T), uz = Mathf.FloorToInt(map.uptown.y / T);
                    for (int dz = -2; dz <= 1; dz++) for (int dx = -2; dx <= 1; dx++) up.Add((ux + dx, uz + dz));
                    sets.Add(("Uptown box (4 x 4 tiles round Trade & Tryon)", up));
                }
                if (scope.Contains("sample"))
                {
                    var all = new SortedSet<long>();
                    foreach (var e in map.edges)
                    {
                        if (e == null || e.pts == null) continue;
                        foreach (var p in e.pts) all.Add(((long)Mathf.FloorToInt(p.x / T) << 32) ^ (uint)Mathf.FloorToInt(p.y / T));
                    }
                    var list = new List<long>(all);
                    var smp = new List<(int, int)>();
                    int stride = Mathf.Max(1, list.Count / Mathf.Max(1, sampleN));
                    for (int i = stride / 2; i < list.Count && smp.Count < sampleN; i += stride)
                        smp.Add(((int)(list[i] >> 32), (int)(uint)(list[i] & 0xFFFFFFFF)));
                    sets.Add(($"city-wide sample ({smp.Count} of {list.Count} road tiles, every {stride}th)", smp));
                }

                sb.AppendLine($"SEAM AUDIT {System.DateTime.Now:yyyy-MM-dd HH:mm}  weld {WeldM * 1000f:0.0} mm, walked every {StepM} m");
                bool bakeDone = false;
                foreach (var mode in modes)
                {
                    CityMeshes.RoadSlabOn = mode != "before";
                    sb.AppendLine($"=== MODE {mode} (PSX_CITY_ROADSLAB={(CityMeshes.RoadSlabOn ? 1 : 0)}) ===");
                    var built = new HashSet<long>();
                    var borderV = new Dictionary<long, List<Vector3d>>();
                    foreach (var (name, tiles) in sets)
                    {
                        var tot = new TileStat();
                        var worst = new List<(float m, string line)>();
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        foreach (var (tx, tz) in tiles)
                        {
                            bool bake = !bakeDone && name == "owner tile";
                            var st = AuditTile(map, trims, buildings, tx, tz, worst, bake ? sb : null, borderV);
                            if (bake) bakeDone = mode == modes[modes.Length - 1];
                            tot.joinM += st.joinM; tot.joinOpenM += st.joinOpenM; tot.stepM += st.stepM; tot.stepOpenM += st.stepOpenM;
                            tot.joinLevelM += st.joinLevelM; tot.joinRiserM += st.joinRiserM; tot.joinHighM += st.joinHighM;
                            tot.tris += st.tris; tot.slabTris += st.slabTris; tot.buildMs += st.buildMs; tot.slabMs += st.slabMs;
                            if (tiles.Count <= 16) sb.AppendLine($"   tile {tx},{tz}: joins {st.joinM:0} m open {st.joinOpenM:0.0} | steps {st.stepM:0} m open {st.stepOpenM:0.0} | road tris {st.tris} (slab {st.slabTris}: {CityMeshes.slabTongues} tongues {CityMeshes.slabTongueM:0} m, {CityMeshes.slabFaces} faces {CityMeshes.slabFaceM:0} m) build {st.buildMs:0} ms (slab {st.slabMs:0.0} ms)");
                        }
                        int n = Mathf.Max(1, tiles.Count);
                        sb.AppendLine($"-- {name}: {tiles.Count} tiles in {clock.Elapsed.TotalSeconds:0} s");
                        sb.AppendLine($"   JOINS (pavement past within -2 mm..+10 cm) {tot.joinM:0} m: level (|dy| <= 2 mm: a T-junction / sliver) {tot.joinLevelM:0} m, pavement past 2-20 mm higher (a fan's FanProudM over a ribbon's end) {tot.joinRiserM:0} m, higher still {tot.joinHighM:0} m");
                        sb.AppendLine($"   STEPS (the land or pavement past lower by > 2 mm) {tot.stepM:0} m");
                        sb.AppendLine($"   GATE {name}: OPEN JOIN {tot.joinOpenM:0.0} m + OPEN STEP {tot.stepOpenM:0.0} m = {tot.joinOpenM + tot.stepOpenM:0.0} m {(tot.joinOpenM + tot.stepOpenM < 0.01f ? "PASS" : "FAIL")}");
                        sb.AppendLine($"   cost: road triangles {tot.tris / n} a tile, of them slab {tot.slabTris / n} ({100f * tot.slabTris / Mathf.Max(1, tot.tris):0.0} %); build {tot.buildMs / n:0} ms a tile, slab pass {tot.slabMs / n:0.00} ms a tile");
                        int shownJ = 0, shownS = 0;
                        foreach (var w in worst)
                        {
                            bool j = w.line.StartsWith("OPEN JOIN");
                            if (j ? shownJ++ >= 15 : shownS++ >= 15) continue;
                            sb.AppendLine("     " + w.line);
                        }
                    }
                    // borders two scanned tiles share: a road vertex on one that the other lacks
                    int bv = 0, bmiss = 0; double bworst = 0;
                    foreach (var kv in borderV)
                    {
                        long key = kv.Key;
                        int side = (int)(key & 3); long tk = key >> 2;
                        int tx = (int)(tk >> 24), tz = (int)((tk << 40) >> 40);
                        if (side != 1 && side != 3) continue;   // east (1) against the next tile's west (0); north (3) against the south (2)
                        int nx = side == 1 ? tx + 1 : tx, nz = side == 3 ? tz + 1 : tz;
                        long nk = (BKey(nx, nz) << 2) | (long)(side - 1);
                        if (!borderV.TryGetValue(nk, out var other)) continue;
                        foreach (var p in kv.Value)
                        {
                            bv++;
                            double best = double.MaxValue;
                            foreach (var q in other) { double d = p.Dist(q); if (d < best) best = d; }
                            if (best > WeldM) bmiss++;
                            else if (best > bworst) bworst = best;
                        }
                    }
                    sb.AppendLine($"-- TILE BORDERS shared by two scanned tiles: {bv} road vertices on them, {bmiss} with no vertex of the other tile within {WeldM * 1000f:0.0} mm (world); matched ones differ by at most {bworst * 1000.0:0.0000} mm");
                    if (CityMeshes.slabProbeLog != null) { foreach (var l in CityMeshes.slabProbeLog) sb.AppendLine(l); CityMeshes.slabProbeLog.Clear(); }
                }
            }
            catch (System.Exception ex) { sb.AppendLine("SEAM AUDIT FAILED: " + ex); Debug.LogException(ex); code = 1; }
            finally
            {
                CityMeshes.RoadSlabOn = slabWas;
                File.WriteAllText(file, sb.ToString());
                Debug.Log("[SeamAudit]\n" + sb);
            }
            if (Application.isBatchMode && !NoExit) EditorApplication.Exit(code);
        }
        /// <summary>CitySeamShots.RunAll: the audit, then the pictures, in one editor.</summary>
        public static bool NoExit;

        struct Vector3d
        {
            public double x, y, z;
            public double Dist(Vector3d o) { double dx = x - o.x, dy = y - o.y, dz = z - o.z; return System.Math.Sqrt(dx * dx + dy * dy + dz * dz); }
        }
        static long BKey(int tx, int tz) => ((long)tx << 24) | (uint)(tz & 0xFFFFFF);

        static TileStat AuditTile(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz,
                                  List<(float, string)> worst, StringBuilder bakeLog, Dictionary<long, List<Vector3d>> borderV)
        {
            var st = new TileStat();
            float T = CityMeshes.TileSize;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
            st.buildMs = clock.Elapsed.TotalMilliseconds;
            st.slabMs = CityMeshes.slabMs; st.slabTris = CityMeshes.slabTris;
            if (tm == null || tm.roads == null) return st;
            if (bakeLog != null) BakeCheck(tm, tx, tz, bakeLog);
            var go = new GameObject($"~seam_{tx}_{tz}");
            try
            {
                go.transform.position = tm.origin;
                CityWorld.Attach(go, tm, null);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                    // the render-only closing faces (paint, trees and furniture would only stand in the probes' way)
                    string nm = mf.gameObject.name;
                    if (nm != "Kerbs" && nm != "Banks" && nm != "Structure" && nm != "Water") continue;
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
                Physics.SyncTransforms();
                var m = tm.roads;
                var v = m.vertices; var nv = m.normals;
                var o = tm.origin;
                // border vertices (world, double)
                if (borderV != null)
                    for (int i = 0; i < v.Length; i++)
                    {
                        var p = v[i];
                        int side = Mathf.Abs(p.x) < 0.001f ? 0 : Mathf.Abs(p.x - T) < 0.001f ? 1 : Mathf.Abs(p.z) < 0.001f ? 2 : Mathf.Abs(p.z - T) < 0.001f ? 3 : -1;
                        if (side < 0) continue;
                        long k = (BKey(tx, tz) << 2) | (long)side;
                        if (!borderV.TryGetValue(k, out var l)) borderV[k] = l = new List<Vector3d>();
                        l.Add(new Vector3d { x = (double)o.x + p.x, y = (double)o.y + p.y, z = (double)o.z + p.z });
                    }
                // weld at 0.5 mm, on the slab pass's own grid (CityMeshes.Key3Fine), so a
                // pair of vertices one calls the same the other does too
                var ids = new int[v.Length];
                var weld = new Dictionary<(long, long, long), int>(v.Length);
                for (int i = 0; i < v.Length; i++)
                {
                    var q = ((long)((v[i].x + 16f) / WeldM + 0.5f), (long)((v[i].y + 200f) / WeldM + 0.5f), (long)((v[i].z + 16f) / WeldM + 0.5f));
                    if (!weld.TryGetValue(q, out int id)) weld[q] = id = i;
                    ids[i] = id;
                }
                var count = new Dictionary<long, int>();
                var third = new Dictionary<long, int>();
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    var t = m.GetTriangles(sm);
                    st.tris += t.Length / 3;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                        var n = Vector3.Cross(b - a, c - a);
                        float mag = n.magnitude;
                        if (mag < 1e-7f || n.y < 0.5f * mag) continue;   // the lying, upward sheets only
                        // the slab's own tongue (its normals set straight up, its plane not level)
                        if (nv.Length == v.Length && nv[t[i]] == Vector3.up && nv[t[i + 1]] == Vector3.up && nv[t[i + 2]] == Vector3.up && n.y < 0.9994f * mag) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int ia = ids[t[i + k]], ib = ids[t[i + (k + 1) % 3]];
                            if (ia == ib) continue;
                            long ek = ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;
                            count.TryGetValue(ek, out int cn);
                            count[ek] = cn + 1;
                            third[ek] = t[i + (k + 2) % 3];
                        }
                    }
                }
                int mask = Physics.DefaultRaycastLayers;
                bool wasBack = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = false;
                foreach (var kv in count)
                {
                    if (kv.Value != 1) continue;
                    Vector3 A = v[(int)(kv.Key >> 32)], B = v[(int)(kv.Key & 0xFFFFFFFF)], C = v[third[kv.Key]];
                    bool OnB(Vector3 p) => Mathf.Abs(p.x) < 0.02f || Mathf.Abs(p.z) < 0.02f || Mathf.Abs(p.x - T) < 0.02f || Mathf.Abs(p.z - T) < 0.02f;
                    if (OnB(A) && OnB(B) && (Mathf.Abs(A.x - B.x) < 0.02f || Mathf.Abs(A.z - B.z) < 0.02f)) continue;
                    var d2 = new Vector2(B.x - A.x, B.z - A.z);
                    float len = d2.magnitude;
                    if (len < 0.02f) continue;
                    var nrm = new Vector2(-d2.y, d2.x) / len;
                    var mid = new Vector2((A.x + B.x) * 0.5f, (A.z + B.z) * 0.5f);
                    if (Vector2.Dot(new Vector2(C.x, C.z) - mid, nrm) > 0f) nrm = -nrm;
                    var n3 = new Vector3(nrm.x, 0f, nrm.y);
                    int steps = Mathf.Max(1, Mathf.RoundToInt(len / StepM));
                    float segM = len / steps;
                    for (int s = 0; s < steps; s++)
                    {
                        var pl = Vector3.Lerp(A, B, (s + 0.5f) / steps);
                        float px = pl.x + nrm.x * 0.35f, pz = pl.z + nrm.y * 0.35f;
                        if (pl.x < BorderPadM || pl.z < BorderPadM || pl.x > T - BorderPadM || pl.z > T - BorderPadM) continue;
                        if (px < 0f || pz < 0f || px > T || pz > T) continue;
                        var P = o + pl;
                        // what lies 5 cm past it, from above
                        if (!Physics.Raycast(P + n3 * 0.05f + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
                        float dy = hit.point.y - P.y;
                        bool paving = hit.collider.name == "Roads" && hit.normal.y > 0.95f;
                        if (paving && dy >= -0.002f && dy <= 0.10f)
                        {
                            st.joinM += segM;
                            if (dy <= 0.002f) st.joinLevelM += segM; else if (dy <= 0.02f) st.joinRiserM += segM; else st.joinHighM += segM;
                            // the crack: straight down from just under the edge, 2 cm out
                            bool ok = Physics.Raycast(P + n3 * 0.02f + Vector3.down * 0.001f, Vector3.down, out var h2, 0.06f, mask, QueryTriggerInteraction.Ignore)
                                      && h2.collider.name == "Roads";
                            if (!ok)
                            {
                                st.joinOpenM += segM;
                                worst.Add((segM, $"OPEN JOIN tile {tx},{tz} at ({P.x:0.0},{P.y:0.000},{P.z:0.0}) pavement past {dy * 1000f:+0;-0} mm, edge {len:0.0} m facing ({nrm.x:+0.00;-0.00},{nrm.y:+0.00;-0.00}){Near(map, P)}"));
                            }
                        }
                        else if (dy < -0.002f)
                        {
                            st.stepM += segM;
                            float depth = Mathf.Min(-dy, 0.3f);
                            // from 4 mm out (the land past may rise from there: a ray from further
                            // out met its slope, not the edge), half way down the step, back at the edge
                            var from = P + n3 * 0.004f + Vector3.down * (depth * 0.5f);
                            bool any = Physics.Raycast(from, -n3, out var h3, 0.012f, mask, QueryTriggerInteraction.Ignore);
                            bool ok = any && Vector3.Dot(h3.normal, n3) > 0.3f;
                            if (!ok)
                            {
                                // ...or the eye's view of the slot: from 30 cm out and 5 cm up at the slot's middle
                                // under the edge. Closed when something blocks the view before it, or what it
                                // meets at or past the slot is paving or a closing face (a shallow step's slab
                                // tongue lies just outside the edge, over the lower sheet)
                                var eye = P + n3 * 0.3f + Vector3.up * 0.05f;
                                var aim = P + Vector3.down * (depth * 0.5f) - n3 * 0.001f;
                                float L = (aim - eye).magnitude;
                                if (Physics.Raycast(eye, (aim - eye) / L, out var h4, L + 0.5f, mask, QueryTriggerInteraction.Ignore))
                                    ok = h4.distance < L - 0.003f || h4.collider.name == "Roads" || h4.collider.name == "Kerbs" || Vector3.Dot(h4.normal, n3) > 0.3f;
                            }
                            if (!ok)
                            {
                                st.stepOpenM += segM;
                                worst.Add((segM * 0.5f, $"OPEN STEP tile {tx},{tz} at ({P.x:0.0},{P.y:0.000},{P.z:0.0}) {-dy * 1000f:0} mm down onto {hit.collider.name} (n.y {hit.normal.y:0.00}), edge {len:0.0} m facing ({nrm.x:+0.00;-0.00},{nrm.y:+0.00;-0.00}); back ray {(any ? $"met {h3.collider.name} {Vector3.Dot(h3.normal, n3):+0.00;-0.00} at {h3.distance:0.000}" : "met nothing")}{Near(map, P)}"));
                            }
                        }
                    }
                }
                Physics.queriesHitBackfaces = wasBack;
            }
            finally { Object.DestroyImmediate(go); }
            // keep the worst list short: merge by place later; cap here
            if (worst.Count > 4000) { worst.Sort((a, b) => b.Item1.CompareTo(a.Item1)); worst.RemoveRange(4000, worst.Count - 4000); }
            return st;
        }

        static string Near(CityMap map, Vector3 P)
        {
            if (!map.NearestRoadPoint(new Vector2(P.x, P.z), 30f, false, out int ei, out float at, out float d)) return "";
            var e = map.edges[ei];
            return $" near e{ei} '{e.name}' s={at:0} ({d:0.0} m)";
        }

        /// <summary>The live tile against its baked copy: written as the bake
        /// writes it, read back as the game reads it.</summary>
        static void BakeCheck(CityMeshes.TileMeshes tm, int tx, int tz, StringBuilder sb)
        {
            string dir = Path.Combine(Path.GetTempPath(), "psx_seam_bake");
            Directory.CreateDirectory(dir);
            try
            {
                var body = CityTileCache.Encode(tm);
                CityTileCache.Write(CityTileCache.PathOf(dir, tx, tz), tx, tz, body, null, null);
                var d = CityTileCache.Read(dir, tx, tz, false);
                var tb = CityTileCache.Apply(d);
                if (tb == null) { sb.AppendLine($"BAKE CHECK tile {tx},{tz}: the read failed"); return; }
                foreach (var (name, a, b) in new[] { ("roads", tm.roads, tb.roads), ("ground", tm.ground, tb.ground), ("kerbs", tm.kerbs, tb.kerbs), ("barriers", tm.barriers, tb.barriers) })
                {
                    if (a == null || b == null) { sb.AppendLine($"BAKE CHECK tile {tx},{tz} {name}: {(a == null ? "no live mesh" : "no baked mesh")}"); continue; }
                    var va = a.vertices; var vb = b.vertices; var na = a.normals; var nb = b.normals;
                    int diffV = 0, diffN = 0, diffI = 0;
                    if (va.Length != vb.Length) { sb.AppendLine($"BAKE CHECK tile {tx},{tz} {name}: vertex count {va.Length} live, {vb.Length} baked"); continue; }
                    for (int i = 0; i < va.Length; i++)
                    {
                        if (System.BitConverter.SingleToInt32Bits(va[i].x) != System.BitConverter.SingleToInt32Bits(vb[i].x) ||
                            System.BitConverter.SingleToInt32Bits(va[i].y) != System.BitConverter.SingleToInt32Bits(vb[i].y) ||
                            System.BitConverter.SingleToInt32Bits(va[i].z) != System.BitConverter.SingleToInt32Bits(vb[i].z)) diffV++;
                        if (na.Length == va.Length && nb.Length == vb.Length && na[i] != nb[i]) diffN++;
                    }
                    if (a.subMeshCount != b.subMeshCount) diffI = -1;
                    else for (int s = 0; s < a.subMeshCount; s++)
                        {
                            var ta = a.GetTriangles(s); var tbi = b.GetTriangles(s);
                            if (ta.Length != tbi.Length) { diffI += System.Math.Abs(ta.Length - tbi.Length); continue; }
                            for (int i = 0; i < ta.Length; i++) if (ta[i] != tbi[i]) diffI++;
                        }
                    sb.AppendLine($"BAKE CHECK tile {tx},{tz} {name}: {va.Length} vertices, {diffV} differ bit for bit, {diffN} normals differ, {diffI} indices differ ({a.indexFormat} / {b.indexFormat}) {(diffV == 0 && diffI == 0 ? "EXACT" : "NOT EXACT")}");
                }
            }
            catch (System.Exception ex) { sb.AppendLine($"BAKE CHECK tile {tx},{tz} FAILED: {ex.Message}"); }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }

    /// <summary>The six junction places of the seam pictures and audit,
    /// found in the map by kind: the owner's (a four-leg junction on North
    /// Graham Street, two lanes each way, toward Uptown), an Uptown signalised
    /// four-leg, a T on a residential street, a divided arterial's crossing
    /// (a junction cluster, median noses), a freeway ramp's terminal, a
    /// junction on a slope. Each: the approach arm, the seam (the fan's mouth
    /// on it: the arm's trim point), the travel direction.</summary>
    public static class CitySeamPlaces
    {
        public struct Place { public string id, what; public int node, edge; public Vector3 seam, fwd, right; public float width; public bool oneway; }

        public static List<Place> Find(CityMap map, CityMeshes.Trims trims, StringBuilder log)
        {
            var list = new List<Place>();
            void Add(string id, string what, int n, int ei)
            {
                if (n < 0 || ei < 0) { log?.AppendLine($"PLACE {id}: none found"); return; }
                var e = map.edges[ei];
                float trim = trims.TrimAt(e, n);
                float s = e.a == n ? trim : e.length - trim;
                var p = e.PointAt(s);
                var t = e.TangentAt(s);
                var f = e.a == n ? -t : t;   // toward the node
                var fwd = new Vector3(f.x, 0f, f.y).normalized;
                var pl = new Place { id = id, what = what, node = n, edge = ei, seam = new Vector3(p.x, e.YAt(s), p.y), fwd = fwd, right = Vector3.Cross(Vector3.up, fwd), width = e.width, oneway = e.oneway };
                list.Add(pl);
                log?.AppendLine($"PLACE {id}: node {n} at ({map.nodes[n].x:0.0},{map.nodes[n].y:0.0}) arm e{ei} '{e.name}' cls {e.cls}{(e.link ? " link" : "")}{(e.oneway ? " one-way" : "")} width {e.width:0.0} lanes {e.lanes}; seam at ({pl.seam.x:0.0},{pl.seam.y:0.00},{pl.seam.z:0.0}) trim {trim:0.0} m; {Vector2.Distance(map.nodes[n], map.uptown):0} m from Uptown; {what}");
            }
            int Degree(int n) { int d = 0; foreach (var ei in map.nodeEdges[n]) if (map.edges[ei].a != map.edges[ei].b) d++; return d; }
            bool Patch(int n) => trims.patch != null && trims.patch[n];
            // an arm into n long enough to stand 45 m back from its trim
            bool Long(int ei, int n) { var e = map.edges[ei]; return e.length - trims.TrimAt(e, n) - trims.TrimAt(e, e.a == n ? e.b : e.a) > 46f && !e.ElevatedAt(e.a == n ? trims.TrimAt(e, n) + 20f : e.length - trims.TrimAt(e, n) - 20f); }
            bool Into(int ei, int n) { var e = map.edges[ei]; return !e.oneway || e.b == n; }

            // 1. the owner's: North Graham Street, four legs, 1-3 km out, approaching toward Uptown
            {
                int bn = -1, be = -1; float bestScore = float.MaxValue;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (map.nodeEdges[n] == null || Degree(n) != 4 || !Patch(n)) continue;
                    float du = Vector2.Distance(map.nodes[n], map.uptown);
                    if (du < 900f || du > 3500f) continue;
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.name == null || !e.name.Contains("North Graham") || e.oneway || !Long(ei, n)) continue;
                        // approaching toward Uptown: the arm's far end further out than the node
                        int far = e.a == n ? e.b : e.a;
                        if (Vector2.Distance(map.nodes[far], map.uptown) < du) continue;
                        float score = Mathf.Abs(du - 1700f) + (e.lanes >= 4 || e.width >= 12f ? 0f : 2000f);
                        log?.AppendLine($"  candidate n{n} e{ei} '{e.name}' lanes {e.lanes} width {e.width:0.0} {du:0} m from Uptown");
                        if (score < bestScore) { bestScore = score; bn = n; be = ei; }
                    }
                }
                Add("owner_graham", "the owner's: N Graham St four-leg junction, toward Uptown", bn, be);
            }
            // 2. Uptown signalised four-leg: the four-leg patch nearest Trade & Tryon, its longest two-way arm
            {
                int bn = -1, be = -1; float best = float.MaxValue;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (map.nodeEdges[n] == null || Degree(n) != 4 || !Patch(n)) continue;
                    float du = Vector2.Distance(map.nodes[n], map.uptown);
                    if (du >= best) continue;
                    int arm = -1;
                    foreach (var ei in map.nodeEdges[n]) if (Into(ei, n) && Long(ei, n) && map.edges[ei].cls >= 2) { arm = ei; break; }
                    if (arm < 0) continue;
                    best = du; bn = n; be = arm;
                }
                Add("uptown_4leg", "Uptown signalised four-leg (nearest Trade & Tryon)", bn, be);
            }
            // 3. a T on a residential street, 2-5 km out
            {
                int bn = -1, be = -1;
                for (int n = 0; n < map.nodes.Length && bn < 0; n++)
                {
                    if (map.nodeEdges[n] == null || Degree(n) != 3 || !Patch(n)) continue;
                    float du = Vector2.Distance(map.nodes[n], map.uptown);
                    if (du < 2500f || du > 5000f) continue;
                    bool res = true; foreach (var ei in map.nodeEdges[n]) if (map.edges[ei].cls != 0 || map.edges[ei].link) res = false;
                    if (!res) continue;
                    // the stem: the arm most across the other two
                    int stem = -1; float bestDot = 1f;
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei]; var ta = e.a == n ? e.TangentAt(0f) : -e.TangentAt(e.length);
                        float worstDot = 0f;
                        foreach (var ej in map.nodeEdges[n]) { if (ej == ei) continue; var f = map.edges[ej]; var tb = f.a == n ? f.TangentAt(0f) : -f.TangentAt(f.length); worstDot = Mathf.Max(worstDot, Mathf.Abs(Vector2.Dot(ta, tb))); }
                        if (worstDot < bestDot && Long(ei, n)) { bestDot = worstDot; stem = ei; }
                    }
                    if (stem >= 0 && bestDot < 0.3f) { bn = n; be = stem; }
                }
                Add("res_t", "a T on a residential street (approach on the stem)", bn, be);
            }
            // 4. a divided arterial's crossing: a junction cluster with a one-way arterial arm in
            {
                int bn = -1, be = -1; float best = float.MaxValue;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (map.nodeEdges[n] == null || !Patch(n) || trims.ClusterOfNode(n) == null) continue;
                    float du = Vector2.Distance(map.nodes[n], map.uptown);
                    if (du < 2000f || du > 8000f || du >= best) continue;
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (!e.oneway || e.b != n || e.cls < 3 || e.cls > 4 || e.link || !Long(ei, n)) continue;
                        best = du; bn = n; be = ei; break;
                    }
                }
                Add("divided_x", "a divided arterial's crossing (junction cluster, median noses)", bn, be);
            }
            // 5. a freeway ramp's terminal on an arterial
            {
                int bn = -1, be = -1; float best = float.MaxValue;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (map.nodeEdges[n] == null || !Patch(n)) continue;
                    bool ramp = false; int art = -1;
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.link && e.cls >= 4) ramp = true;
                        else if (!e.link && e.cls >= 2 && e.cls <= 3 && !e.oneway && Long(ei, n)) art = ei;
                    }
                    if (!ramp || art < 0) continue;
                    float du = Vector2.Distance(map.nodes[n], map.uptown);
                    if (du < 1500f || du >= best) continue;
                    best = du; bn = n; be = art;
                }
                Add("ramp_term", "a freeway ramp's terminal (approach on the arterial)", bn, be);
            }
            // 6. a junction on a slope: the steepest approach of 6 % or more, 1.5-6 km out
            {
                int bn = -1, be = -1; float best = 0.06f;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (map.nodeEdges[n] == null || Degree(n) < 3 || !Patch(n) || trims.ClusterOfNode(n) != null) continue;
                    float du = Vector2.Distance(map.nodes[n], map.uptown);
                    if (du < 1500f || du > 6000f) continue;
                    foreach (var ei in map.nodeEdges[n])
                    {
                        var e = map.edges[ei];
                        if (e.link || e.oneway || !Long(ei, n) || e.stY == null || e.stY.Length == 0) continue;
                        float tr = trims.TrimAt(e, n);
                        float s0 = e.a == n ? tr : e.length - tr, s1 = e.a == n ? tr + 40f : e.length - tr - 40f;
                        float g = Mathf.Abs(e.YAt(s1) - e.YAt(s0)) / 40f;
                        if (g > best && g < 0.15f) { best = g; bn = n; be = ei; }
                    }
                }
                Add("slope", $"a junction on a slope (approach grade {best * 100f:0.0} %)", bn, be);
            }
            return list;
        }
    }
}
