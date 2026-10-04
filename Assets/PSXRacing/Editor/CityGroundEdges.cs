using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// GROUND THAT ENDS IN THE AIR (2026-10-04, the owner on West Trade Street:
    /// "a thin layer of dirt and I can see under the dirt and the road to the
    /// right"). Every ground sheet edge that no other ground triangle shares
    /// (welded by position) is walked a metre at a time; just past it a ray
    /// goes down. Where the first surface under it is more than
    /// <see cref="DropM"/> lower and no drawn face closes the drop (a ray
    /// across from outside meets no face turned toward it), the ground ends
    /// in an OPEN EDGE: from below the driver sees under the sheet, and the
    /// collider's edge stops the car like a wall nobody built.
    ///
    /// Scope: PSX_GEDGE_BOX=x0,z0,x1,z1 (game metres), else the OwnerBox;
    /// PSX_GEDGE_ROUTES=1 adds every tile the race routes cross and counts the
    /// open edges within <see cref="RouteReachM"/> of a route edge apart.
    /// Writes city_ground_edges.txt at the project root.
    /// </summary>
    public static class CityGroundEdges
    {
        public const float DropM = 0.15f;
        public const float RouteReachM = 30f;
        const float StepM = 1f;
        static readonly bool LandCheck = System.Environment.GetEnvironmentVariable("PSX_GEDGE_LAND") == "1";
        static readonly string[] ScanMeshes = (System.Environment.GetEnvironmentVariable("PSX_GEDGE_MESHES") ?? "Ground").Split(',');
        static readonly int ShowMax = int.TryParse(System.Environment.GetEnvironmentVariable("PSX_GEDGE_SHOW"), out int sm) ? sm : 40;
        static readonly bool KeepCovered = System.Environment.GetEnvironmentVariable("PSX_GEDGE_COVERED") == "1";

        struct Hit { public Vector3 p; public Vector2 n; public float drop; public string under; public int tile; }

        [MenuItem("PSX Racing/Audit City Ground Open Edges")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { Done(sb.AppendLine("no city data")); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var boxEnv = System.Environment.GetEnvironmentVariable("PSX_GEDGE_BOX");
            Rect box = CityAudit.OwnerBox;
            if (!string.IsNullOrWhiteSpace(boxEnv))
            {
                var f = boxEnv.Split(',');
                box = Rect.MinMaxRect(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv), float.Parse(f[3], inv));
            }
            bool routes = System.Environment.GetEnvironmentVariable("PSX_GEDGE_ROUTES") == "1";
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            float T = CityMeshes.TileSize;

            // the tiles to scan: the box's, and the routes' when asked
            var scan = new HashSet<long>();
            var routeTiles = new HashSet<long>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            for (int tx = Mathf.FloorToInt(box.xMin / T); tx <= Mathf.FloorToInt(box.xMax / T); tx++)
                for (int tz = Mathf.FloorToInt(box.yMin / T); tz <= Mathf.FloorToInt(box.yMax / T); tz++)
                    scan.Add(Key(tx, tz));
            var routeEdges = new HashSet<int>();
            if (routes)
                foreach (var r in map.routes)
                    foreach (int ei in r.edges)
                    {
                        routeEdges.Add(ei);
                        var e = map.edges[ei];
                        for (float s = 0f; s <= e.length + 5f; s += 5f)
                        {
                            var p = e.PointAt(Mathf.Min(s, e.length));
                            for (int dz = -1; dz <= 1; dz++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    var q = p + new Vector2(dx, dz) * RouteReachM;
                                    long k = Key(Mathf.FloorToInt(q.x / T), Mathf.FloorToInt(q.y / T));
                                    routeTiles.Add(k); scan.Add(k);
                                }
                        }
                    }
            sb.AppendLine($"GROUND OPEN EDGES: box ({box.xMin:0},{box.yMin:0})-({box.xMax:0},{box.yMax:0}){(routes ? $", routes ({map.routes.Length}, {routeEdges.Count} edges, {routeTiles.Count} tiles)" : "")}; {scan.Count} tiles scanned; a sheet edge over a drop past {DropM:0.00} m with no face closing it");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var all = new List<Hit>();
            int tilesDone = 0, edgesSeen = 0;
            // a tile and its 8 neighbours stood up at a time, the scanned tile
            // in the middle; tiles kept while a neighbour still needs them
            var live = new Dictionary<long, GameObject>();
            var logs = new Dictionary<long, List<(string tag, Vector3 a, Vector3 b, Vector3 c)>>();
            var root = new GameObject("~groundEdges");
            CityMeshes.shortDropLog = new List<string>();
            var order = new List<long>(scan);
            order.Sort();
            try
            {
                foreach (long key in order)
                {
                    int tx = (int)(key >> 32), tz = (int)(uint)(key & 0xFFFFFFFF);
                    var want = new HashSet<long>();
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) want.Add(Key(tx + dx, tz + dz));
                    foreach (var k in new List<long>(live.Keys))
                        if (!want.Contains(k)) { Object.DestroyImmediate(live[k]); live.Remove(k); logs.Remove(k); }
                    foreach (var k in want)
                    {
                        if (live.ContainsKey(k)) continue;
                        int kx = (int)(k >> 32), kz = (int)(uint)(k & 0xFFFFFFFF);
                        CityMeshes.groundLog = new List<(string, Vector3, Vector3, Vector3)>();
                        var tm = CityMeshes.Build(map, trims, buildings, kx, kz);
                        logs[k] = CityMeshes.groundLog; CityMeshes.groundLog = null;
                        var go = new GameObject($"tile_{kx}_{kz}");
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = tm.origin;
                        CityWorld.Attach(go, tm, null);
                        // render-only faces close a drop too (the kerb under a
                        // grounded edge, a creek's banks): colliders for the probe
                        foreach (var nm in new[] { "Kerbs", "Banks" })
                        {
                            var c = go.transform.Find(nm);
                            if (c == null) continue;
                            var mf = c.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null && c.GetComponent<MeshCollider>() == null)
                                c.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                        }
                        live[k] = go;
                    }
                    Physics.SyncTransforms();
                    tilesDone++;
                    if (LandCheck)
                    {
                        var rd = live[key].transform.Find("Roads");
                        var rm = rd != null ? rd.GetComponent<MeshFilter>()?.sharedMesh : null;
                        if (rm != null) LandOverRoad(rm, live[key].transform.position, all);
                    }
                    foreach (var layerName in ScanMeshes)
                    {
                        var me = live[key].transform.Find(layerName);
                        if (me == null) continue;
                        var mesh = me.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        int before = all.Count;
                        edgesSeen += Scan(mesh, live[key].transform.position, tx, tz, T, key, all);
                        for (int h = before; h < all.Count; h++)
                        {
                            var hh = all[h];
                            hh.under += layerName == "Ground" ? " by " + Owner(logs[key], hh) : " by the " + layerName + " mesh";
                            all[h] = hh;
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
            var dropped = new SortedSet<string>(CityMeshes.shortDropLog);
            CityMeshes.shortDropLog = null;

            // the reported open edges: in the box, or near a route edge
            bool InBox(Vector3 p) => box.Contains(new Vector2(p.x, p.z));
            bool NearRoute(Vector3 p)
            {
                if (!routes) return false;
                if (!map.NearestRoadPoint(new Vector2(p.x, p.z), RouteReachM, false, out int ei, out _, out _)) return false;
                // the nearest road within reach, or any route edge within reach
                if (routeEdges.Contains(ei)) return true;
                var near = new HashSet<int>();
                map.EdgeSegsInRect(new Vector2(p.x, p.z) - Vector2.one * RouteReachM, new Vector2(p.x, p.z) + Vector2.one * RouteReachM, near);
                foreach (int packed in near) if (routeEdges.Contains(packed >> 12)) return true;
                return false;
            }
            var boxHits = new List<Hit>(); var routeHits = new List<Hit>();
            foreach (var h in all)
            {
                if (InBox(h.p)) boxHits.Add(h);
                if (routes && NearRoute(h.p)) routeHits.Add(h);
            }
            sb.AppendLine($"  {tilesDone} tiles, {edgesSeen} open sheet edge samples walked in {clock.Elapsed.TotalSeconds:0} s");
            sb.AppendLine($"-- short barrier runs the side-flag pass dropped on the tiles built (MinMedianRunM): {dropped.Count}");
            foreach (var d in dropped) sb.AppendLine("  DROPPED " + d);
            Report(sb, map, "box", boxHits);
            if (routes) Report(sb, map, "routes", routeHits);
            Done(sb);
        }

        /// <summary>PSX_GEDGE_LAND=1: land standing on a road's pavement -
        /// every road triangle's centre (and its corners pulled 20 % in),
        /// a ray down from 3 m over it: the first surface met is the ground,
        /// more than 5 cm over the pavement.</summary>
        static void LandOverRoad(Mesh mesh, Vector3 origin, List<Hit> outHits)
        {
            var v = mesh.vertices;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                var t = mesh.GetTriangles(sm);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    var n = Vector3.Cross(b - a, c - a);
                    if (n.y <= 0f || n.y < 0.7f * n.magnitude) continue;   // pavement faces up
                    var cen = (a + b + c) / 3f;
                    foreach (var q in new[] { cen, Vector3.Lerp(a, cen, 0.2f), Vector3.Lerp(b, cen, 0.2f), Vector3.Lerp(c, cen, 0.2f) })
                    {
                        var p = origin + q;
                        if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 3.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (hit.collider.name != "Ground" || hit.point.y - p.y < 0.05f) continue;
                        outHits.Add(new Hit { p = p, n = Vector2.zero, drop = hit.point.y - p.y, under = "LAND over the Roads mesh", tile = 2 });
                    }
                }
            }
        }

        /// <summary>The strip that laid the sheet at the open edge (from
        /// CityMeshes.groundLog), or the lattice.</summary>
        static string Owner(List<(string tag, Vector3 a, Vector3 b, Vector3 c)> log, Hit h)
        {
            if (log == null) return "?";
            var q = new Vector2(h.p.x - h.n.x * 0.05f, h.p.z - h.n.y * 0.05f);
            foreach (var (tag, a, b, c) in log)
            {
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float wa = ((b.z - c.z) * (q.x - c.x) + (c.x - b.x) * (q.y - c.z)) / d;
                float wb = ((c.z - a.z) * (q.x - c.x) + (a.x - c.x) * (q.y - c.z)) / d;
                float wc = 1f - wa - wb;
                if (wa < -1e-3f || wb < -1e-3f || wc < -1e-3f) continue;
                float y = wa * a.y + wb * b.y + wc * c.y;
                if (Mathf.Abs(y - h.p.y) < 0.05f) return tag;
            }
            return "the lattice";
        }

        static long TileOf(Vector3 p, float T) =>
            ((long)Mathf.FloorToInt(p.x / T) << 32) ^ (uint)Mathf.FloorToInt(p.z / T);

        /// <summary>The ground mesh's open edges (one triangle, welded by
        /// position to 2 mm), walked every metre: a ray down just past the
        /// edge, and across from outside at half the drop.</summary>
        static int Scan(Mesh mesh, Vector3 origin, int tx, int tz, float T, long key, List<Hit> outHits)
        {
            var v = mesh.vertices;
            var ids = new int[v.Length];
            var weld = new Dictionary<(int, int, int), int>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                var q = (Mathf.RoundToInt(v[i].x * 500f), Mathf.RoundToInt(v[i].y * 500f), Mathf.RoundToInt(v[i].z * 500f));
                if (!weld.TryGetValue(q, out int id)) weld[q] = id = i;
                ids[i] = id;
            }
            var count = new Dictionary<long, int>();
            var third = new Dictionary<long, int>();
            int sub = mesh.subMeshCount;
            for (int sm = 0; sm < sub; sm++)
            {
                var t = mesh.GetTriangles(sm);
                for (int i = 0; i < t.Length; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = ids[t[i + k]], b = ids[t[i + (k + 1) % 3]], c = ids[t[i + (k + 2) % 3]];
                        if (a == b) continue;
                        long ek = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        count.TryGetValue(ek, out int n);
                        count[ek] = n + 1;
                        third[ek] = c;
                    }
            }
            int walked = 0;
            int mask = ~0;
            foreach (var kv in count)
            {
                if (kv.Value != 1) continue;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                Vector3 A = v[a], B = v[b], C = v[third[kv.Key]];
                // the tile's own border: the neighbour carries on
                bool OnBorder(Vector3 p) => Mathf.Abs(p.x) < 0.02f || Mathf.Abs(p.z) < 0.02f || Mathf.Abs(p.x - T) < 0.02f || Mathf.Abs(p.z - T) < 0.02f;
                if (OnBorder(A) && OnBorder(B) && (Mathf.Abs(A.x - B.x) < 0.02f || Mathf.Abs(A.z - B.z) < 0.02f)) continue;
                var d2 = new Vector2(B.x - A.x, B.z - A.z);
                float len = d2.magnitude;
                if (len < 0.05f) continue;
                var nrm = new Vector2(-d2.y, d2.x) / len;
                var mid = new Vector2((A.x + B.x) * 0.5f, (A.z + B.z) * 0.5f);
                if (Vector2.Dot(new Vector2(C.x, C.z) - mid, nrm) > 0f) nrm = -nrm;   // away from its triangle
                int steps = Mathf.Max(1, Mathf.RoundToInt(len / StepM));
                for (int s = 0; s < steps; s++)
                {
                    float f = (s + 0.5f) / steps;
                    var p = origin + Vector3.Lerp(A, B, f);
                    walked++;
                    var o = new Vector3(nrm.x, 0f, nrm.y);
                    var start = p + o * 0.10f + Vector3.up * 0.02f;
                    float drop;
                    string under;
                    if (Physics.Raycast(start, Vector3.down, out var hit, 30f, mask, QueryTriggerInteraction.Ignore))
                    { drop = p.y - hit.point.y; under = hit.collider.name; }
                    else continue;   // nothing under it: a building's footprint (its walls are not stood up here) or the world's edge
                    if (drop <= DropM) continue;
                    // a drawn face standing in the drop, turned out: closed
                    float probeY = p.y - Mathf.Min(drop, 1.0f) * 0.5f;
                    var from = new Vector3(p.x, probeY, p.z) + o * 0.6f;
                    if (Physics.Raycast(from, -o, out var side, 0.75f, mask, QueryTriggerInteraction.Ignore)
                        && Vector3.Dot(side.normal, o) > 0.3f) continue;
                    // hidden: the sheet's edge under another surface (the
                    // lattice carried on under a road), or the gap roofed
                    bool was = Physics.queriesHitBackfaces;
                    Physics.queriesHitBackfaces = true;
                    string cov = null;
                    if (Physics.Raycast(p - o * 0.15f + Vector3.up * 0.01f, Vector3.up, out var c1, 1.2f, mask, QueryTriggerInteraction.Ignore))
                        cov = $"sheet under {c1.collider.name} +{c1.point.y - p.y:0.00}";
                    else if (Physics.Raycast(hit.point + Vector3.up * 0.02f, Vector3.up, out var c2, drop + 1.0f, mask, QueryTriggerInteraction.Ignore))
                        cov = $"gap roofed by {c2.collider.name} {c2.point.y - p.y:+0.00;-0.00}";
                    Physics.queriesHitBackfaces = was;
                    if (cov != null && !KeepCovered) continue;
                    outHits.Add(new Hit { p = p, n = nrm, drop = drop, under = under + (cov != null ? " [" + cov + "]" : ""), tile = 1 });
                }
            }
            return walked;
        }

        static void Report(StringBuilder sb, CityMap map, string label, List<Hit> hits)
        {
            // places: hits within 4 m of each other
            var places = new List<List<Hit>>();
            var used = new bool[hits.Count];
            for (int i = 0; i < hits.Count; i++)
            {
                if (used[i]) continue;
                var pl = new List<Hit> { hits[i] }; used[i] = true;
                for (int g = 0; g < pl.Count; g++)
                    for (int j = 0; j < hits.Count; j++)
                        if (!used[j] && (hits[j].p - pl[g].p).sqrMagnitude < 16f) { used[j] = true; pl.Add(hits[j]); }
                places.Add(pl);
            }
            places.Sort((x, y) => Score(y).CompareTo(Score(x)));
            int bump = 0; foreach (var h in hits) if (h.drop > 0.3f && h.drop < 1.5f) bump++;
            sb.AppendLine($"-- {label}: {hits.Count} open metres over a drop past {DropM:0.00} m ({bump} with a 0.3-1.5 m drop: a car's bumper meets the edge), {places.Count} places; the worst 40:");
            int shown = 0;
            foreach (var pl in places)
            {
                if (shown++ >= ShowMax) break;
                Hit worst = pl[0]; foreach (var h in pl) if (h.drop > worst.drop) worst = h;
                var c = Vector3.zero; foreach (var h in pl) c += h.p; c /= pl.Count;
                string road = "";
                if (map.NearestRoadPoint(new Vector2(c.x, c.z), 40f, false, out int ei, out float at, out float dist))
                    road = $" nearest e{ei} '{map.edges[ei].name}' s={at:0} {dist:0.0} m (road y {map.edges[ei].YAt(at):0.00})";
                sb.AppendLine($"  OPEN {pl.Count} m at ({c.x:0.0},{c.z:0.0}) y {c.y:0.00}, drop max {worst.drop:0.00} m at ({worst.p.x:0.0},{worst.p.z:0.0}) facing ({worst.n.x:+0.00;-0.00},{worst.n.y:+0.00;-0.00}) onto {worst.under}{road}");
            }
        }

        static float Score(List<Hit> pl) { float s = 0f; foreach (var h in pl) s += Mathf.Min(h.drop, 1.5f); return s; }

        static void Done(StringBuilder sb)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, "city_ground_edges.txt"), sb.ToString());
            Debug.Log("[CityGroundEdges] wrote city_ground_edges.txt");
        }
    }
}
