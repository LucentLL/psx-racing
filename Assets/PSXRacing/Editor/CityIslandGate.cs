using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ISLAND GATE (c7roads, 2026-11-25). The owner on the University City loop: "Some turn lanes have
    /// curbs in the middle of the turn lane. Dividers have holes and transparencies. Many of these features are
    /// lazily layered over one another". Four classes, read from the tiles as the game builds them
    /// (CityMeshes.Build, every mesh the game draws), along the routes of the city's races:
    ///   RAISED     something raised (a curb, an island, a verge, a slab: any non-road triangle) more than
    ///              5 cm over the drawn pavement more than half a metre inside its edge, or inside a lane's
    ///              driven band (the ribbon less its shoulders, 0.3 m in) of any road of the corridor
    ///   SEETHRU    a car's eye (bumper 0.6 m, cockpit 1.1 m, chase 1.8 m over each lane centre of the route,
    ///              every 6 m) looking down at the ground ahead and to the sides (pitch -2..-35 degrees, yaw
    ///              +-70) meets no front face within 120 m: a hole, a gap between sheets, or a single-sided
    ///              face seen from behind (the renderer culls it as the ray does)
    ///   OVERLAY    a non-road surface from 3 mm under to 5 cm over the drawn pavement half a metre inside its edge (coplanar or a flat
    ///              layer laid on the asphalt: the two fight, or a slab lies on the road)
    ///   LINEUNDER  painted line texels (a road triangle whose U lies in a line column of its profile's
    ///              texture) with anything non-road over them
    /// A place is a 4 m cell of the plan within 25 m of the route with one or more hits of the class.
    ///   -executeMethod PSXRacing.EditorTools.CityIslandGate.Run   (-nographics is fine)
    ///   PSX_ISL_ROUTES=c7,c1,...   route ids (map.routes; default c7)
    ///   PSX_ISL_TAG=name           names the outputs
    ///   PSX_ISL_MAX=raised=0,...   fail (exit 1) when a class has more places than this, summed over the routes
    /// Writes city_islandgate_&lt;tag&gt;.txt (totals per route and class) and .tsv (every place).
    /// </summary>
    public static class CityIslandGate
    {
        const float Cell = 0.25f;
        const float PlaceM = 4f, NearRouteM = 25f;
        static readonly string[] ClassNames = { "RAISED", "SEETHRU", "OVERLAY", "LINEUNDER" };

        sealed class TileHits
        {
            public readonly List<Vector3>[] hits = { new List<Vector3>(), new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            public GameObject go;
            public int tris, verts, draws;
        }

        public static void Run()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_ISL_TAG") ?? "run";
            string[] routeIds = (System.Environment.GetEnvironmentVariable("PSX_ISL_ROUTES") ?? "c7").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            string root = Directory.GetParent(Application.dataPath).FullName;
            var log = new StringBuilder($"ISLAND GATE {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}; ISLANDSOLID {(CityMeshes.IslandSolidsOn ? "on" : "OFF")}, MEDIANRUNS {(CityMeshes.MedianRunsOn ? "on" : "OFF")}\n");
            var tsv = new StringBuilder("route\tclass\tx\tz\thits\n");
            int code = 0;
            var totals = new int[4];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var map = CityMap.Get();
                if (map == null) throw new System.Exception("no city data");
                CityTracks.Register(map);   // the races of Resources/CityTracks, by id (as every city scene does)
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                var cache = new Dictionary<long, TileHits>();
                bool oldQ = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = false;
                CityMeshes.IslandCount = 0; CityMeshes.IslandAreaM2 = 0f; CityMeshes.IslandTipCells = 0; CityMeshes.IslandOverlayTris = 0; CityMeshes.IslandInsideTris = 0; CityMeshes.IslandSkippedBorder = 0; CityMeshes.IslandSkippedBig = 0; CityMeshes.IslandPokeVerts = 0;
                double buildMs = 0; int built = 0;
                foreach (string rid in routeIds)
                {
                    var route = map.routes?.FirstOrDefault(r => r.id == rid);
                    if (route == null) { log.Append($"route {rid}: NOT FOUND\n"); code = 2; continue; }
                    // the route's lane samples (each edge forward as driven), its corridor edges
                    var samples = new List<(Vector3 p, Vector2 fwd)>();
                    var corridor = new HashSet<int>();
                    for (int q = 0; q < route.edges.Length; q++)
                    {
                        var e = map.edges[route.edges[q]];
                        corridor.Add(e.index);
                        foreach (int ne in map.nodeEdges[e.a]) corridor.Add(ne);
                        foreach (int ne in map.nodeEdges[e.b]) corridor.Add(ne);
                        int dir = route.dirs[q];
                        for (float s = 3f; s < e.length - 3f; s += 6f)
                        {
                            float ss = dir > 0 ? s : e.length - s;
                            var lp = LineModel.OwnLanePoint(e, ss);
                            var t = e.TangentAt(ss) * dir;
                            samples.Add((new Vector3(lp.x, e.YAt(ss), lp.y), t.normalized));
                        }
                    }
                    var near = new List<Vector2>();
                    foreach (var sm in samples) near.Add(new Vector2(sm.p.x, sm.p.z));
                    // tiles: those within 60 m of a sample, in route order
                    var order = new List<long>();
                    var seen = new HashSet<long>();
                    foreach (var sm in samples)
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int tx = Mathf.FloorToInt((sm.p.x + dx * 60f) / CityMeshes.TileSize), tz = Mathf.FloorToInt((sm.p.z + dz * 60f) / CityMeshes.TileSize);
                                long k = Key(tx, tz);
                                if (seen.Add(k)) order.Add(k);
                            }
                    var placeSets = new HashSet<long>[4];
                    for (int c = 0; c < 4; c++) placeSets[c] = new HashSet<long>();
                    var placeHits = new Dictionary<(int, long), int>();
                    // the rays need the tiles round each sample standing (colliders)
                    int si = 0;
                    var live = new Dictionary<long, TileHits>();
                    foreach (var sm in samples)
                    {
                        int tx0 = Mathf.FloorToInt(sm.p.x / CityMeshes.TileSize), tz0 = Mathf.FloorToInt(sm.p.z / CityMeshes.TileSize);
                        var want = new HashSet<long>();
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                long k = Key(tx0 + dx, tz0 + dz);
                                // only what is within 70 m of the sample
                                float cx = Mathf.Clamp(sm.p.x, (tx0 + dx) * CityMeshes.TileSize, (tx0 + dx + 1) * CityMeshes.TileSize);
                                float cz = Mathf.Clamp(sm.p.z, (tz0 + dz) * CityMeshes.TileSize, (tz0 + dz + 1) * CityMeshes.TileSize);
                                if ((new Vector2(cx, cz) - new Vector2(sm.p.x, sm.p.z)).magnitude > 70f) continue;
                                want.Add(k);
                            }
                        foreach (long k in want)
                        {
                            if (live.ContainsKey(k)) continue;
                            if (!cache.TryGetValue(k, out var th))
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                var tm = CityMeshes.Build(map, trims, buildings, (int)(k >> 32), (int)(k & 0xffffffff));
                                buildMs += sw.Elapsed.TotalMilliseconds; built++;
                                th = Analyse(map, trims, tm, corridor);
                                th.go = Stand(tm, k);
                                cache[k] = th;
                            }
                            else if (th.go == null) th.go = Stand(CityMeshes.Build(map, trims, buildings, (int)(k >> 32), (int)(k & 0xffffffff)), k);
                            live[k] = th;
                        }
                        // drop the tiles no longer needed (keep the cache's hits)
                        foreach (long k in live.Keys.ToList())
                            if (!want.Contains(k)) { var th = live[k]; if (th.go != null) Object.DestroyImmediate(th.go); th.go = null; live.Remove(k); }
                        Physics.SyncTransforms();
                        SeeThrough(map, sm.p, sm.fwd, cache);
                        si++;
                    }
                    foreach (var th in live.Values) if (th.go != null) { Object.DestroyImmediate(th.go); th.go = null; }
                    // gather the places near the route
                    foreach (long k in order)
                    {
                        if (!cache.TryGetValue(k, out var th)) continue;
                        for (int c = 0; c < 4; c++)
                            foreach (var h in th.hits[c])
                            {
                                var p2 = new Vector2(h.x, h.z);
                                if (!NearAny(near, p2, NearRouteM)) continue;
                                long pk = Key(Mathf.FloorToInt(h.x / PlaceM), Mathf.FloorToInt(h.z / PlaceM));
                                placeSets[c].Add(pk);
                                placeHits.TryGetValue((c, pk), out int n); placeHits[(c, pk)] = n + 1;
                            }
                    }
                    log.Append(string.Format(inv, "route {0}: {1} lane samples, {2} tiles | RAISED {3} SEETHRU {4} OVERLAY {5} LINEUNDER {6}\n",
                        rid, samples.Count, order.Count, placeSets[0].Count, placeSets[1].Count, placeSets[2].Count, placeSets[3].Count));
                    for (int c = 0; c < 4; c++)
                    {
                        totals[c] += placeSets[c].Count;
                        foreach (long pk in placeSets[c])
                        {
                            int px = (int)(pk >> 32), pz = (int)(pk & 0xffffffff);
                            tsv.Append(string.Format(inv, "{0}\t{1}\t{2:0}\t{3:0}\t{4}\n", rid, ClassNames[c], (px + 0.5f) * PlaceM, (pz + 0.5f) * PlaceM, placeHits[(c, pk)]));
                        }
                    }
                }
                Physics.queriesHitBackfaces = oldQ;
                log.Append(string.Format(inv, "TOTAL | RAISED {0} SEETHRU {1} OVERLAY {2} LINEUNDER {3}\n", totals[0], totals[1], totals[2], totals[3]));
                log.Append(string.Format(inv, "tiles built {0}, {1:0} ms a tile; islands {2} ({3:0} m2), tips paved {4} cells, overlays out {5}, inside out {6}, poke verts {7}, skipped at a border {8}, too big {9}; median runs filled {10} sections, dropped {11} runs; {12:0} s\n",
                    built, built > 0 ? buildMs / built : 0, CityMeshes.IslandCount, CityMeshes.IslandAreaM2, CityMeshes.IslandTipCells, CityMeshes.IslandOverlayTris, CityMeshes.IslandInsideTris, CityMeshes.IslandPokeVerts,
                    CityMeshes.IslandSkippedBorder, CityMeshes.IslandSkippedBig, CityMeshes.MedianRunsFilled, CityMeshes.MedianRunsDropped, clock.Elapsed.TotalSeconds));
                // the limits
                string mx = System.Environment.GetEnvironmentVariable("PSX_ISL_MAX");
                if (!string.IsNullOrEmpty(mx))
                    foreach (var kv in mx.Split(','))
                    {
                        var p = kv.Split('=');
                        if (p.Length != 2) continue;
                        int c = System.Array.FindIndex(ClassNames, n => n.Equals(p[0].Trim(), System.StringComparison.OrdinalIgnoreCase));
                        if (c < 0 || !int.TryParse(p[1], out int lim)) continue;
                        if (totals[c] > lim) { log.Append($"FAIL {ClassNames[c]} {totals[c]} > {lim}\n"); code = 1; }
                    }
                log.Append(code == 0 ? "ISLAND GATE OK\n" : "ISLAND GATE FAILED\n");
            }
            catch (System.Exception ex) { log.Append("ISLAND GATE FAILED: " + ex + "\n"); Debug.LogException(ex); code = 2; }
            finally
            {
                File.WriteAllText(Path.Combine(root, $"city_islandgate_{tag}.txt"), log.ToString());
                File.WriteAllText(Path.Combine(root, $"city_islandgate_{tag}.tsv"), tsv.ToString());
                Debug.Log("[IslandGate]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        static bool NearAny(List<Vector2> pts, Vector2 p, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < pts.Count; i++) if ((pts[i] - p).sqrMagnitude < r2) return true;
            return false;
        }

        static GameObject Stand(CityMeshes.TileMeshes tm, long k)
        {
            var go = new GameObject($"~isl_{k >> 32}_{k & 0xffffffff}");
            go.transform.position = tm.origin;
            foreach (var m in new[] { tm.ground, tm.roads, tm.kerbs, tm.barriers, tm.structure, tm.guardrails, tm.buildings, tm.banks, tm.water })
            {
                if (m == null || m.vertexCount == 0) continue;
                var c = new GameObject(m.name);
                c.transform.SetParent(go.transform, false);
                var mc = c.AddComponent<MeshCollider>();
                mc.sharedMesh = m;
            }
            return go;
        }

        static readonly float[] Heights = { 0.6f, 1.1f, 1.8f };
        static readonly float[] Pitches = { -2f, -4f, -7f, -12f, -20f, -35f };

        static void SeeThrough(CityMap map, Vector3 p, Vector2 fwd, Dictionary<long, TileHits> cache)
        {
            var f3 = new Vector3(fwd.x, 0f, fwd.y);
            foreach (float h in Heights)
            {
                var o = p + Vector3.up * h;
                for (int yaw = -70; yaw <= 70; yaw += 7)
                {
                    var dYaw = Quaternion.AngleAxis(yaw, Vector3.up) * f3;
                    foreach (float pitch in Pitches)
                    {
                        var dir = (dYaw * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
                        float expect = h / Mathf.Sin(-pitch * Mathf.Deg2Rad);
                        if (expect > 60f) continue;
                        if (Physics.Raycast(o, dir, 120f)) continue;
                        // nothing in front: a hole only where the ray went under the land (the terrain may fall away)
                        Vector3 at = Vector3.zero; bool under = false;
                        for (float d = 1f; d <= 60f; d += 1f)
                        {
                            var q = o + dir * d;
                            if (q.y < CityElevation.GroundY(map, q.x, q.z) - 0.5f) { at = q; under = true; break; }
                        }
                        if (!under) continue;
                        long k = Key(Mathf.FloorToInt(at.x / CityMeshes.TileSize), Mathf.FloorToInt(at.z / CityMeshes.TileSize));
                        if (cache.TryGetValue(k, out var th)) th.hits[1].Add(at);
                    }
                }
            }
        }

        /// <summary>RAISED, OVERLAY, LINEUNDER of one tile, from its meshes.</summary>
        static TileHits Analyse(CityMap map, CityMeshes.Trims trims, CityMeshes.TileMeshes tm, HashSet<int> corridor)
        {
            var th = new TileHits();
            var o = tm.origin;
            int N = Mathf.CeilToInt((CityMeshes.TileSize + 16f) / Cell);
            float m0 = -8f;
            var pave = new float[N * N];
            var paint = new bool[N * N];
            var high = new float[N * N];
            for (int k = 0; k < pave.Length; k++) { pave[k] = float.NaN; high[k] = float.NegativeInfinity; }
            // the cost (what the tile draws)
            foreach (var m in new[] { tm.ground, tm.roads, tm.kerbs, tm.barriers, tm.structure, tm.guardrails, tm.buildings, tm.banks, tm.water, tm.medianPlants, tm.lampPosts })
                if (m != null) { th.verts += m.vertexCount; th.draws += m.subMeshCount; for (int s = 0; s < m.subMeshCount; s++) th.tris += (int)m.GetIndexCount(s) / 3; }
            // the pavement and its paint
            if (tm.roads != null && tm.roadSlots != null)
            {
                var v = tm.roads.vertices; var uv = tm.roads.uv;
                for (int sm = 0; sm < tm.roads.subMeshCount && sm < tm.roadSlots.Length; sm++)
                {
                    if (!CityMeshes.RoadSlot((int)tm.roadSlots[sm], out int prof, out _)) continue;
                    LineModel.Layout lay = prof != CityMeshes.JunctionProfile && prof >= 0 && prof < RoadProfiles.ProfileCount ? LineModel.LayoutOf(prof) : null;
                    var t = tm.roads.GetTriangles(sm);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                        bool isPaint = false;
                        if (lay != null && uv != null && uv.Length == v.Length && lay.x0 != null)
                        {
                            float ua = uv[t[i]].x, ub = uv[t[i + 1]].x, uc = uv[t[i + 2]].x;
                            for (int j = 0; j < lay.x0.Length; j++)
                            {
                                float lo = lay.x0[j] / (float)lay.texW - 1e-4f, hi = (lay.x1[j] + 1) / (float)lay.texW + 1e-4f;
                                if (ua >= lo && ua <= hi && ub >= lo && ub <= hi && uc >= lo && uc <= hi) { isPaint = true; break; }
                            }
                        }
                        Raster(a, b, c, N, m0, (k, y) => { if (float.IsNaN(pave[k]) || y < pave[k]) pave[k] = y; if (isPaint) paint[k] = true; });
                    }
                }
            }
            // everything else that stands on the ground
            foreach (var m in new[] { tm.ground, tm.kerbs, tm.barriers, tm.banks })
            {
                if (m == null) continue;
                var v = m.vertices;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    var t = m.GetTriangles(sm);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                        Raster(v[t[i]], v[t[i + 1]], v[t[i + 2]], N, m0, (k, y) => { if (y > high[k]) high[k] = y; });
                }
            }
            // interior pavement: half a metre in every way
            int r = 2;
            for (int j = r; j < N - r; j++)
                for (int i = r; i < N - r; i++)
                {
                    int k = j * N + i;
                    if (float.IsNaN(pave[k]) || float.IsNegativeInfinity(high[k])) continue;
                    float rise = high[k] - pave[k];
                    if (rise <= -0.003f) continue;   // within 3 mm under it: coplanar, the two fight (OVERLAY)
                    bool inside = !float.IsNaN(pave[k - r]) && !float.IsNaN(pave[k + r]) && !float.IsNaN(pave[k - r * N]) && !float.IsNaN(pave[k + r * N])
                               && !float.IsNaN(pave[k - r * N - r]) && !float.IsNaN(pave[k - r * N + r]) && !float.IsNaN(pave[k + r * N - r]) && !float.IsNaN(pave[k + r * N + r]);
                    var w = new Vector3(o.x + m0 + (i + 0.5f) * Cell, pave[k], o.z + m0 + (j + 0.5f) * Cell);
                    if (w.x < o.x || w.z < o.z || w.x >= o.x + CityMeshes.TileSize || w.z >= o.z + CityMeshes.TileSize) continue;
                    if (paint[k] && rise < 2.5f) th.hits[3].Add(w);
                    if (!inside || rise > 2.5f) continue;   // a deck or a building over the road is not this
                    th.hits[rise > 0.05f ? 0 : 2].Add(w);
                }
            // the lanes' driven bands of the corridor's roads
            foreach (int ei in corridor)
            {
                var e = map.edges[ei];
                for (float s = 0.25f; s < e.length; s += 0.5f)
                {
                    var pt = e.PointAt(s);
                    if (pt.x < o.x - 20f || pt.y < o.z - 20f || pt.x > o.x + CityMeshes.TileSize + 20f || pt.y > o.z + CityMeshes.TileSize + 20f) continue;
                    LineModel.Extents(e, s, out float eM, out float eP);
                    var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                    float lo = -(eM - e.shr) + 0.3f, hi = (eP - e.shl) - 0.3f;
                    float yLane = e.YAt(s);
                    for (float l = lo; l <= hi; l += 0.5f)
                    {
                        var q = pt + left * l;
                        int i = Mathf.FloorToInt((q.x - o.x - m0) / Cell), j = Mathf.FloorToInt((q.y - o.z - m0) / Cell);
                        if (i < 0 || j < 0 || i >= N || j >= N) continue;
                        if (q.x < o.x || q.y < o.z || q.x >= o.x + CityMeshes.TileSize || q.y >= o.z + CityMeshes.TileSize) continue;
                        int k = j * N + i;
                        if (float.IsNegativeInfinity(high[k])) continue;
                        float baseY = float.IsNaN(pave[k]) ? yLane : pave[k];
                        float rise = high[k] - baseY;
                        if (rise > 0.05f && rise < 2.5f) th.hits[0].Add(new Vector3(q.x, baseY, q.y));
                    }
                }
            }
            // the paths through the corridor's junctions: from each arm's lanes at the fan to each other arm's
            // (through and gentle turns, under 50 degrees: a sharp turn's corner is the slip lane's), 1.5 m each side
            var nodes = new HashSet<int>();
            foreach (int ei in corridor) { nodes.Add(map.edges[ei].a); nodes.Add(map.edges[ei].b); }
            foreach (int n in nodes)
            {
                var np = map.nodes[n];
                if (np.x < o.x - 40f || np.y < o.z - 40f || np.x > o.x + CityMeshes.TileSize + 40f || np.y > o.z + CityMeshes.TileSize + 40f) continue;
                var arms = map.nodeEdges[n];
                if (arms == null || arms.Count < 2) continue;
                foreach (int ia in arms)
                {
                    var ein = map.edges[ia];
                    if (ein.a == ein.b) continue;
                    bool inAtB = ein.b == n;
                    if (ein.oneway && !inAtB) continue;   // a one-way arm must arrive
                    float sIn = inAtB ? ein.length - (trims.atB != null ? trims.atB[ia] : 0f) : (trims.atA != null ? trims.atA[ia] : 0f);
                    sIn = Mathf.Clamp(sIn, 0f, ein.length);
                    var tIn = ein.TangentAt(sIn) * (inAtB ? 1f : -1f);
                    var pIn = ArmLane(ein, sIn, inAtB ? 1 : -1);
                    foreach (int oa in arms)
                    {
                        if (oa == ia) continue;
                        var eo = map.edges[oa];
                        if (eo.a == eo.b) continue;
                        bool outAtA = eo.a == n;
                        if (eo.oneway && !outAtA) continue;   // a one-way arm must leave
                        float sOut = outAtA ? (trims.atA != null ? trims.atA[oa] : 0f) : eo.length - (trims.atB != null ? trims.atB[oa] : 0f);
                        sOut = Mathf.Clamp(sOut, 0f, eo.length);
                        var tOut = eo.TangentAt(sOut) * (outAtA ? 1f : -1f);
                        if (Vector2.Angle(tIn, tOut) > 50f) continue;
                        var pOut = ArmLane(eo, sOut, outAtA ? 1 : -1);
                        float L = (pOut - pIn).magnitude;
                        if (L < 0.5f || L > 120f) continue;
                        Vector2 c1 = pIn + tIn.normalized * (L / 3f), c2 = pOut - tOut.normalized * (L / 3f);
                        float yIn = ein.YAt(sIn), yOut = eo.YAt(sOut);
                        int steps = Mathf.CeilToInt(L / 0.5f);
                        for (int q = 0; q <= steps; q++)
                        {
                            float u = q / (float)steps, w0 = (1 - u) * (1 - u) * (1 - u), w1 = 3 * u * (1 - u) * (1 - u), w2 = 3 * u * u * (1 - u), w3 = u * u * u;
                            var pt = pIn * w0 + c1 * w1 + c2 * w2 + pOut * w3;
                            var dv = 3 * (1 - u) * (1 - u) * (c1 - pIn) + 6 * (1 - u) * u * (c2 - c1) + 3 * u * u * (pOut - c2);
                            if (dv.sqrMagnitude < 1e-8f) continue;
                            dv.Normalize(); var lf = new Vector2(-dv.y, dv.x);
                            float yL = Mathf.Lerp(yIn, yOut, u);
                            for (float l = -1.5f; l <= 1.51f; l += 0.5f)
                            {
                                var qq = pt + lf * l;
                                if (qq.x < o.x || qq.y < o.z || qq.x >= o.x + CityMeshes.TileSize || qq.y >= o.z + CityMeshes.TileSize) continue;
                                int i = Mathf.FloorToInt((qq.x - o.x - m0) / Cell), j = Mathf.FloorToInt((qq.y - o.z - m0) / Cell);
                                if (i < 0 || j < 0 || i >= N || j >= N) continue;
                                int k = j * N + i;
                                if (float.IsNegativeInfinity(high[k])) continue;
                                float baseY = float.IsNaN(pave[k]) ? yL : pave[k];
                                float rise = high[k] - baseY;
                                if (rise > 0.05f && rise < 2.5f) th.hits[0].Add(new Vector3(qq.x, baseY, qq.y));
                            }
                        }
                    }
                }
            }
            return th;
        }

        /// <summary>The lanes' middle of an arm at s, for traffic moving along +dir: a one-way road's lanes, a
        /// two-way road's half on the right of travel.</summary>
        static Vector2 ArmLane(CityMap.Edge e, float s, int dir)
        {
            var lp = LineModel.OwnLanePoint(e, s);
            if (e.oneway) return lp;
            var t = e.TangentAt(s) * dir;
            var right = new Vector2(t.y, -t.x);
            LineModel.Extents(e, s, out float eM, out float eP);
            float quarter = 0.25f * ((eP - e.shl) + (eM - e.shr));
            return lp + right * quarter;
        }

        static void Raster(Vector3 a, Vector3 b, Vector3 c, int N, float m0, System.Action<int, float> put)
        {
            float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
            int i0 = Mathf.Max(0, Mathf.CeilToInt((minx - m0) / Cell - 0.5f)), i1 = Mathf.Min(N - 1, Mathf.FloorToInt((maxx - m0) / Cell - 0.5f));
            int j0 = Mathf.Max(0, Mathf.CeilToInt((minz - m0) / Cell - 0.5f)), j1 = Mathf.Min(N - 1, Mathf.FloorToInt((maxz - m0) / Cell - 0.5f));
            if (i0 > i1 || j0 > j1) return;
            float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(den) < 1e-9f) return;
            for (int j = j0; j <= j1; j++)
            {
                float z = m0 + (j + 0.5f) * Cell;
                for (int i = i0; i <= i1; i++)
                {
                    float x = m0 + (i + 0.5f) * Cell;
                    float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / den;
                    float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / den;
                    float l3 = 1f - l1 - l2;
                    if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                    put(j * N + i, l1 * a.y + l2 * b.y + l3 * c.y);
                }
            }
        }
    }
}
