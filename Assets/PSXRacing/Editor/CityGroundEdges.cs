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
            foreach (var fs in (System.Environment.GetEnvironmentVariable("PSX_GEDGE_FANS") ?? "").Split(','))
                if (int.TryParse(fs.Trim(), out int fnode)) foreach (var l in CityMeshes.DebugFanRing(map, trims, fnode)) sb.AppendLine("FAN " + l);
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
            // PSX_GEDGE_EDGES=e1,e2: the tiles those edges cross too
            foreach (var es in (System.Environment.GetEnvironmentVariable("PSX_GEDGE_EDGES") ?? "").Split(','))
                if (int.TryParse(es.Trim().TrimStart('e'), out int gei) && gei >= 0 && gei < map.edges.Length)
                {
                    var e = map.edges[gei];
                    for (float s = 0f; s <= e.length + 5f; s += 5f)
                    {
                        var p = e.PointAt(Mathf.Min(s, e.length));
                        scan.Add(Key(Mathf.FloorToInt(p.x / T), Mathf.FloorToInt(p.y / T)));
                    }
                    sb.AppendLine($"EDGE e{gei} '{e.name}' {e.length:0} m from ({e.PointAt(0f).x:0},{e.PointAt(0f).y:0}) to ({e.PointAt(e.length).x:0},{e.PointAt(e.length).y:0})");
                }
            sb.AppendLine($"GROUND OPEN EDGES: box ({box.xMin:0},{box.yMin:0})-({box.xMax:0},{box.yMax:0}){(routes ? $", routes ({map.routes.Length}, {routeEdges.Count} edges, {routeTiles.Count} tiles)" : "")}; {scan.Count} tiles scanned; a sheet edge over a drop past {DropM:0.00} m with no face closing it");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var all = new List<Hit>();
            int tilesDone = 0, edgesSeen = 0, skirtN = 0, slopeN = 0; float skirtM = 0f, fasciaM = 0f, softM = 0f, slopeM = 0f;
            var passMs = new List<double>(); var buildMs = new List<double>();
            // a tile and its 8 neighbours stood up at a time, the scanned tile
            // in the middle; tiles kept while a neighbour still needs them
            var live = new Dictionary<long, GameObject>();
            var logs = new Dictionary<long, List<(string tag, Vector3 a, Vector3 b, Vector3 c)>>();
            var ledgeAll = new List<(Vector3 at, float step, float len, bool road, Vector2 nrm, int why)>();
            var ledgeBuilt = new HashSet<long>();
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
                        var bclock = System.Diagnostics.Stopwatch.StartNew();
                        CityMeshes.skirtLedges = scan.Contains(k) && ledgeBuilt.Add(k) ? new List<(Vector3, float, float, bool, Vector2, int)>() : null;
                        var tm = CityMeshes.Build(map, trims, buildings, kx, kz);
                        if (CityMeshes.skirtLedges != null) ledgeAll.AddRange(CityMeshes.skirtLedges);
                        CityMeshes.skirtLedges = null;
                        if (scan.Contains(k)) { skirtM += CityMeshes.skirtMetres; fasciaM += CityMeshes.fasciaMetres; skirtN += CityMeshes.skirtCount; softM += CityMeshes.skirtSoftMetres; slopeN += CityMeshes.skirtSlopeCount; slopeM += CityMeshes.skirtSlopeMetres; passMs.Add(CityMeshes.skirtMs); buildMs.Add(bclock.Elapsed.TotalMilliseconds); }
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
            sb.AppendLine($"  closing faces the scanned tiles laid: {skirtN} skirt pieces, {skirtM:0} m ({softM:0} m of them steps, not drops: render-only with the kerbs); deck fascias {fasciaM:0} m; raised sheet ends laid as 1:4 foreslopes {slopeN} pieces {slopeM:0} m");
            { var ph = CityMeshes.skirtPhase; sb.AppendLine($"  closing pass phases, ms summed over every build: sets {ph[0]:0} ground weld {ph[1]:0} ground faces {ph[2]:0} roads weld {ph[3]:0} grid {ph[4]:0} roads faces {ph[5]:0}; road open edges {CityMeshes.skirtRoadCounts[0]}, unverged {CityMeshes.skirtRoadCounts[1]}, over the lattice {CityMeshes.skirtRoadCounts[2]}, not paved past {CityMeshes.skirtRoadCounts[3]}"); }
            passMs.Sort(); buildMs.Sort();
            if (passMs.Count > 0) sb.AppendLine($"  closing pass per tile build: p50 {passMs[passMs.Count / 2]:0.0} p95 {passMs[(int)(passMs.Count * 0.95f)]:0.0} max {passMs[passMs.Count - 1]:0.0} ms; whole build p50 {buildMs[buildMs.Count / 2]:0.0} p95 {buildMs[(int)(buildMs.Count * 0.95f)]:0.0} ms ({passMs.Count} builds)");
            sb.AppendLine($"-- short barrier runs the side-flag pass dropped on the tiles built (MinMedianRunM): {dropped.Count}");
            foreach (var d in dropped) sb.AppendLine("  DROPPED " + d);
            // RAISED SHEET ENDS (leftovers 2026-10-05): render-only steps of
            // LedgeStepM..OpenDropM - the sheet's own edge is the ledge
            {
                // REACH: within 3 m of a road's paved edge (a car off the road
                // meets it); END: the open edge runs across the road, not along
                var named = new HashSet<int>();
                foreach (var es in (System.Environment.GetEnvironmentVariable("PSX_GEDGE_EDGES") ?? "").Split(','))
                    if (int.TryParse(es.Trim().TrimStart('e'), out int nei)) named.Add(nei);
                float lm = 0f, worst = 0f, reachM = 0f; int nRoad = 0, nReach = 0, nEnd = 0; float endM = 0f;
                var rows = new List<(float step, string line, bool reach)>();
                foreach (var l in ledgeAll)
                {
                    lm += l.len; worst = Mathf.Max(worst, l.step); if (l.road) nRoad++;
                    bool reach = false, end = false; string road = "";
                    if (map.NearestRoadPoint(new Vector2(l.at.x, l.at.z), 40f, false, out int ei, out float at, out float dist))
                    {
                        var ed = map.edges[ei];
                        float off = dist - ed.width * 0.5f;
                        reach = off <= 3f;
                        end = Mathf.Abs(Vector2.Dot(ed.TangentAt(at), l.nrm)) > 0.7f;
                        road = $" nearest e{ei} '{ed.name}' s={at:0} {off:+0.0;-0.0} m past its edge";
                        if (named.Contains(ei))
                        {
                            // the roads within 6 m of it, and their heights there
                            var near = new HashSet<int>(); var seenE = new HashSet<int>();
                            var p2 = new Vector2(l.at.x, l.at.z);
                            map.EdgeSegsInRect(p2 - Vector2.one * 6f, p2 + Vector2.one * 6f, near);
                            foreach (int packed in near)
                            {
                                int ne = packed >> 12;
                                if (!seenE.Add(ne)) continue;
                                var nd = map.edges[ne];
                                float best = float.MaxValue, bs = 0f;
                                for (float ss = 0f; ss <= nd.length; ss += 0.5f) { float dd = Vector2.Distance(nd.PointAt(ss), p2); if (dd < best) { best = dd; bs = ss; } }
                                road += $" [e{ne} '{nd.name}' cls{nd.cls}{(nd.link ? " L" : "")} w{nd.width:0.0} {best - nd.width * 0.5f:+0.0;-0.0} m dy {nd.YAt(bs) - l.at.y:+0.00;-0.00}]";
                            }
                            road += " NAMED";
                        }
                    }
                    if (reach) { nReach++; reachM += l.len; if (end) { nEnd++; endM += l.len; } }
                    rows.Add((l.step, $"  RAISED {l.step:0.00} m {l.len:0.0} m at ({l.at.x:0.0},{l.at.z:0.0}) y {l.at.y:0.00} {(l.road ? "road" : "ground")} sheet {(end ? "END" : "along")}{(l.why > 0 ? new[] { "", " kept: tile edge", $" kept: road under at {(l.why / 10 % 100) * 0.25f:0.00} m out {(l.why / 1000) * 0.01f:0.00} m under the slope", " kept: no land" }[l.why % 10] : "")}{road}", reach));
                }
                sb.AppendLine($"-- RAISED SHEET ENDS on the scanned tiles: {ledgeAll.Count} pieces, {lm:0.0} m ({nRoad} from road sheets), worst {worst:0.00} m: render-only steps {RoadsideRules.LedgeStepM}-{RoadsideRules.OpenDropM} m deep whose sheet edge a car meets; within 3 m of a road's edge {nReach} pieces {reachM:0.0} m, of them across the road (ENDS) {nEnd} pieces {endM:0.0} m");
                foreach (var r in rows) if (r.line.EndsWith(" NAMED")) sb.AppendLine(r.line);
                rows.Sort((x, y) => y.step.CompareTo(x.step));
                int shownR = 0;
                foreach (var r in rows) if (r.reach && shownR++ < ShowMax) sb.AppendLine(r.line);
            }
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
            // the classes: what laid the sheet, and whether its open edge runs
            // along the nearest road (a strip's far edge) or across it (an end)
            var cls = new SortedDictionary<string, (int m, float drop)>();
            foreach (var h in hits)
            {
                string kind = h.under;
                int by = kind.IndexOf(" by ", System.StringComparison.Ordinal);
                kind = by >= 0 ? kind.Substring(by + 4) : "?";
                kind = System.Text.RegularExpressions.Regex.Replace(kind, @"e\d+ '[^']*' side ([LR]) span [\d.]+\.\.[\d.]+ ", "edge side $1 ");
                kind = System.Text.RegularExpressions.Regex.Replace(kind, @"node \d+ (arm )?e\d+(-e\d+)?", "");
                kind = System.Text.RegularExpressions.Regex.Replace(kind, @"band\d", "").Trim();
                string along = "?";
                if (map.NearestRoadPoint(new Vector2(h.p.x, h.p.z), 40f, false, out int ei, out float at, out _))
                {
                    var t = map.edges[ei].TangentAt(at);
                    along = Mathf.Abs(Vector2.Dot(t, h.n)) > 0.7f ? "across" : "along";
                }
                string key = $"{kind} | {along}";
                cls.TryGetValue(key, out var v);
                cls[key] = (v.m + 1, Mathf.Max(v.drop, h.drop));
            }
            foreach (var kv in cls) sb.AppendLine($"   CLASS {label}: {kv.Value.m,5} m  max drop {kv.Value.drop:0.00}  {kv.Key}");
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
