using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE EDGE OF EVERY DRIVABLE SURFACE IN A CHARLOTTE TILE, as a car meets it.
    ///
    /// The drive audit walks EDGES of the graph, so it only ever looks at the
    /// ribbon between two trims: a junction fan, a gore wedge, a clipped mouth
    /// or anything else the tile draws between ribbons is never probed. This
    /// works from the MESH instead. Every drivable triangle of a tile's road
    /// mesh (a road-surface submesh, facing up) is welded into a surface, its
    /// BOUNDARY edges are found (an edge only one drivable triangle owns), and
    /// each boundary edge is asked, from just outside it and in this order:
    ///
    ///   GUARDED  does a barrier stand across it? A Solid-layer box from 0.6 m
    ///            inboard to 0.3 m outboard at wheel-to-hip height, or a wall-
    ///            like face met (back faces included) by a ray starting 0.6 m
    ///            inboard. Checked FIRST: a rail on a grounded approach used
    ///            to be filed as a lip.
    ///   OPEN     is there nothing to land on within RoadsideRules.OpenDropM
    ///            below, 1 m and 2 m out? Then a car falls off.
    ///   LIP      where there IS land, how far below the tarmac it sits 1 m out.
    ///
    /// The first cut fired its rail ray from exactly RailW inside the edge —
    /// ON the rail's traffic face, whose far face is a back face to an
    /// outward ray — so at 2-20 km from the origin float jitter decided
    /// whether a rail was seen: adjacent spans of one railed deck flipped
    /// OPEN/guarded 36% of the time, and its 11.2 km OPEN could not size
    /// anything. It also called a drop OPEN only past 2.5 m.
    ///
    /// Tiles: the drive audit's trouble spots plus the tiles with the most
    /// metres of elevated road in the city. Writes city_edge_probe.txt and
    /// city_edge_probe.csv at the project root.
    /// </summary>
    public static class CityEdgeProbe
    {
        const int TopTiles = 36;
        const float OpenDrop = RoadsideRules.OpenDropM;
        /// <summary>How far inboard of the edge the barrier checks start: past
        /// a rail's traffic face (CityMeshes.RailW) with room to spare.</summary>
        const float RailInboard = 0.6f;
        const float RailReach = 1.5f;

        [MenuItem("PSX Racing/Probe Charlotte Edges")]
        public static void Run()
        {
            var log = new StringBuilder();
            var csv = new StringBuilder("tile,x,z,y,len,kind,lip1,nearEdge,name,link,cls,elev,bridge,nodeDist,clip\n");
            var map = CityMap.Get();
            if (map == null) { Write(log.AppendLine("no city data"), csv); return; }
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);

            // tiles ranked by elevated metres
            var elevM = new Dictionary<long, float>();
            foreach (var e in map.edges)
                for (int i = 0; i + 1 < e.stS.Length; i++)
                {
                    if (!e.stElev[i] && !e.stElev[i + 1]) continue;
                    var p = e.PointAt((e.stS[i] + e.stS[i + 1]) * 0.5f);
                    long k = Key(Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                    elevM.TryGetValue(k, out float m); elevM[k] = m + (e.stS[i + 1] - e.stS[i]);
                }
            var ranked = new List<KeyValuePair<long, float>>(elevM);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
            var tiles = new List<(int tx, int tz, string why)>();
            void AddTile(int tx, int tz, string why) { if (!tiles.Exists(t => t.tx == tx && t.tz == tz)) tiles.Add((tx, tz, why)); }
            AddTile(Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize), Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize), "uptown");
            foreach (var e in map.edges)
                if (e.bridge && e.name == "West 5th Street")
                { var p = e.PointAt(e.length * 0.5f); AddTile(Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize), "w5th"); break; }
            foreach (var ic in CityAudit.Interchanges(map, "I-277", "I-77"))
                AddTile(Mathf.FloorToInt(ic.at.x / CityMeshes.TileSize), Mathf.FloorToInt(ic.at.y / CityMeshes.TileSize), "i277_i77");
            for (int r = 0; r < ranked.Count && tiles.Count < TopTiles; r++)
                AddTile((int)(ranked[r].Key >> 24), (int)((ranked[r].Key << 40) >> 40), "elev " + ranked[r].Value.ToString("0") + " m");

            float openM = 0f, guardedM = 0f, edgeM = 0f;
            var lipHist = new float[7];
            var openByKind = new Dictionary<string, float>();
            var worstOpen = new List<(float len, string what)>();
            var root = new GameObject("~cityEdgeProbe");
            try
            {
                foreach (var (tx, tz, why) in tiles)
                {
                    var made = new List<GameObject>();
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            made.Add(Stand(map, trims, buildings, tx + dx, tz + dz, root));
                        }
                    var centreTm = CityMeshes.Build(map, trims, buildings, tx, tz);   // LAST: DescribeClip reads its table
                    made.Add(StandTm(centreTm, tx, tz, root));
                    Physics.SyncTransforms();

                    float tileOpen = 0f;
                    var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
                    var max = min + Vector2.one * CityMeshes.TileSize;
                    foreach (var (a, b, inward) in BoundaryEdges(centreTm))
                    {
                        Vector3 wa = a + centreTm.origin, wb = b + centreTm.origin;
                        Vector3 mid = (wa + wb) * 0.5f;
                        if (mid.x < min.x + 0.5f || mid.x > max.x - 0.5f || mid.z < min.y + 0.5f || mid.z > max.y - 0.5f) continue;
                        float len = Vector3.Distance(wa, wb);
                        if (len < 0.05f) continue;
                        var along = new Vector3(wb.x - wa.x, 0f, wb.z - wa.z).normalized;
                        var outw = new Vector3(-along.z, 0f, along.x);
                        if (Vector3.Dot(outw, inward) > 0f) outw = -outw;
                        edgeM += len;

                        bool land = Surface(mid + outw * 1.0f + Vector3.up * 0.6f, 0.6f + OpenDrop, out float landY, out var landCol);
                        bool land2 = Surface(mid + outw * 2.0f + Vector3.up * 0.6f, 0.6f + OpenDrop, out _, out _);
                        bool rail = Guarded(mid, outw, along, len);
                        string kind;
                        if (rail) { kind = "guarded"; guardedM += len; }
                        else if (land && land2)
                        {
                            float lip = mid.y - landY;
                            kind = "grounded";
                            if (landCol != null && landCol.name == "Roads") kind = "roadside";   // another road piece: a seam, not a lip
                            else lipHist[lip < 0.025f ? 0 : lip < 0.05f ? 1 : lip < 0.10f ? 2 : lip < 0.20f ? 3 : lip < 0.35f ? 4 : lip < 0.60f ? 5 : 6] += len;
                        }
                        else { kind = "OPEN"; openM += len; tileOpen += len; }

                        if (kind == "grounded" || kind == "OPEN" || kind == "guarded")
                        {
                            // attribution: the nearest graph edge
                            var p2 = new Vector2(mid.x, mid.z);
                            var near = new HashSet<int>();
                            map.EdgeSegsInRect(p2 - Vector2.one * 30f, p2 + Vector2.one * 30f, near);
                            int bi = -1; float bd = float.MaxValue, bs = 0f;
                            foreach (var packed in near)
                            {
                                int oi = packed >> 12, si = packed & 0xFFF;
                                var o = map.edges[oi];
                                Vector2 q0 = o.pts[si], dq = o.pts[si + 1] - q0;
                                float L2 = dq.sqrMagnitude;
                                float tt = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p2 - q0, dq) / L2) : 0f;
                                float dd = Vector2.Distance(p2, q0 + dq * tt);
                                if (dd < bd) { bd = dd; bi = oi; bs = o.s[si] + Mathf.Sqrt(L2) * tt; }
                            }
                            string name = "", clip = ""; bool link = false, elev = false, br = false; int cls = -1; float nodeDist = -1f;
                            if (bi >= 0)
                            {
                                var ne = map.edges[bi];
                                name = ne.name; link = ne.link; cls = ne.cls; elev = ne.ElevatedAt(bs); br = ne.bridge;
                                nodeDist = Mathf.Min(bs, ne.length - bs);
                                clip = CityMeshes.DescribeClip(map, trims, ne, bs).Trim();
                            }
                            if (kind == "OPEN")
                            {
                                string cat = nodeDist >= 0f && nodeDist < 25f ? "near a node (fan/mouth)" : clip.Length > 0 ? "clipped branch" : link ? "ramp ribbon" : "ribbon";
                                openByKind.TryGetValue(cat, out float om); openByKind[cat] = om + len;
                                worstOpen.Add((len, $"({mid.x:0},{mid.z:0}) y {mid.y:0.0} tile {tx},{tz} e{bi} '{name}'{(link ? " L" : "")} cls{cls} elev {elev} node {nodeDist:0} m {clip}"));
                            }
                            csv.Append($"{tx}_{tz},{mid.x:0.0},{mid.z:0.0},{mid.y:0.00},{len:0.00},{kind},{(land ? (mid.y - landY).ToString("0.000") : "")},{bi},\"{name}\",{(link ? 1 : 0)},{cls},{(elev ? 1 : 0)},{(br ? 1 : 0)},{nodeDist:0.0},\"{clip}\"\n");
                        }
                    }
                    log.AppendLine($"tile {tx},{tz} ({why}): open edge {tileOpen:0.0} m");
                    foreach (var g in made) Object.DestroyImmediate(g);
                }
            }
            finally { Object.DestroyImmediate(root); }

            log.AppendLine($"boundary edges probed: {edgeM:0} m; OPEN over a {OpenDrop} m drop with no rail: {openM:0} m; guarded: {guardedM:0} m");
            foreach (var kv in openByKind) log.AppendLine($"  open {kv.Key}: {kv.Value:0} m");
            float lipN = 0f; foreach (var h in lipHist) lipN += h;
            string[] names = { "<2.5cm", "2.5-5", "5-10", "10-20", "20-35", "35-60", ">60" };
            var sb = new StringBuilder("grounded edges: land 1 m out sits below the tarmac by");
            for (int k = 0; k < lipHist.Length; k++) sb.Append($"  {names[k]} {(lipN > 0 ? 100f * lipHist[k] / lipN : 0f):0}%");
            sb.Append($"  ({lipN:0} m)");
            log.AppendLine(sb.ToString());
            worstOpen.Sort((x, y) => y.len.CompareTo(x.len));
            var seen = new HashSet<string>();
            int shown = 0;
            foreach (var (len, what) in worstOpen)
            {
                // one line per graph edge and clip state, longest first
                if (!seen.Add(what.Substring(what.IndexOf(" e") >= 0 ? what.IndexOf(" e") : 0))) continue;
                log.AppendLine($"  OPEN {len:0.0} m at {what}");
                if (++shown >= 40) break;
            }
            Write(log, csv);
        }

        static void Write(StringBuilder log, StringBuilder csv)
        {
            string root = System.IO.Path.GetDirectoryName(Application.dataPath);
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_edge_probe.txt"), log.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_edge_probe.csv"), csv.ToString());
            Debug.Log(log.ToString());
        }

        static long Key(int tx, int tz) => ((long)tx << 24) | (uint)(tz & 0xFFFFFF);

        static GameObject Stand(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz, GameObject root) =>
            StandTm(CityMeshes.Build(map, trims, buildings, tx, tz), tx, tz, root);

        static GameObject StandTm(CityMeshes.TileMeshes tm, int tx, int tz, GameObject root)
        {
            var go = new GameObject($"tile_{tx}_{tz}");
            go.transform.SetParent(root.transform, false);
            go.transform.position = tm.origin;
            CityWorld.Attach(go, tm, null);
            return go;
        }

        /// <summary>Boundary edges of the drivable surface of a tile's road mesh
        /// (every submesh but Concrete, triangles facing up), tile-local, with
        /// a plan vector pointing INTO the surface.</summary>
        static List<(Vector3 a, Vector3 b, Vector3 inward)> BoundaryEdges(CityMeshes.TileMeshes tm)
        {
            var result = new List<(Vector3, Vector3, Vector3)>();
            var mesh = tm.roads;
            if (mesh == null) return result;
            var verts = mesh.vertices;
            var id = new Dictionary<long, int>();
            int Weld(Vector3 v)
            {
                long k = ((long)Mathf.RoundToInt(v.x * 50f) * 73856093L) ^ ((long)Mathf.RoundToInt(v.y * 50f) * 19349663L) ^ ((long)Mathf.RoundToInt(v.z * 50f) * 83492791L);
                if (!id.TryGetValue(k, out int w)) { w = id.Count; id[k] = w; }
                return w;
            }
            var weld = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++) weld[i] = Weld(verts[i]);
            var edges = new Dictionary<long, (int count, int va, int vb, Vector3 third)>();
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                if (tm.roadSlots != null && sm < tm.roadSlots.Length && tm.roadSlots[sm] == CityMeshes.Slot.Concrete) continue;
                var tris = mesh.GetTriangles(sm);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    var n = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
                    if (n.sqrMagnitude < 1e-10f) continue;
                    if (Mathf.Abs(n.normalized.y) < 0.8f) continue;
                    AddEdge(edges, weld[i0], weld[i1], i0, i1, verts[i2]);
                    AddEdge(edges, weld[i1], weld[i2], i1, i2, verts[i0]);
                    AddEdge(edges, weld[i2], weld[i0], i2, i0, verts[i1]);
                }
            }
            foreach (var kv in edges)
            {
                if (kv.Value.count != 1) continue;
                var a = verts[kv.Value.va]; var b = verts[kv.Value.vb];
                var mid = (a + b) * 0.5f;
                var inward = kv.Value.third - mid; inward.y = 0f;
                result.Add((a, b, inward));
            }
            return result;
        }

        static void AddEdge(Dictionary<long, (int count, int va, int vb, Vector3 third)> edges, int wa, int wb, int ia, int ib, Vector3 third)
        {
            long k = wa < wb ? ((long)wa << 32) | (uint)wb : ((long)wb << 32) | (uint)wa;
            if (edges.TryGetValue(k, out var e)) edges[k] = (e.count + 1, e.va, e.vb, e.third);
            else edges[k] = (1, ia, ib, third);
        }

        static bool Surface(Vector3 from, float reach, out float y, out Collider on)
        {
            y = 0f; on = null; bool found = false;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.normal.y < 0.5f) continue;
                if (!found || h.point.y > y) { y = h.point.y; on = h.collider; found = true; }
            }
            return found;
        }

        /// <summary>A barrier across a boundary edge: a Solid-layer collider in
        /// a box from RailInboard inside the edge to 0.3 m past the end of any
        /// flush road surface beyond it, between RoadsideRules' two barrier ray
        /// heights, or any wall-like face (back faces too) met outward from
        /// RailInboard inside at either height.</summary>
        static bool Guarded(Vector3 mid, Vector3 outw, Vector3 along, float len)
        {
            float lo = RoadsideRules.BarrierRayHeights[0];
            float hi = RoadsideRules.BarrierRayHeights[RoadsideRules.BarrierRayHeights.Length - 1];
            // A boundary edge of one road mesh piece is not always an edge of
            // the drivable surface: a host's edge in its gore gap meets the
            // branch's pavement, drawn from other vertices and flush with it,
            // and the branch's outer rail stands at the far side of that
            // sliver. The first cut boxed only 0.3 m past the edge and called
            // those seams OPEN (9.8 m at e1164, 8.3 m at e252). The box now
            // runs out over road surface within a decimetre of the edge's
            // height, as far as that surface goes (the roadside audit's rail
            // census walks the same way).
            float reach = 0.3f;
            for (float d = 0.1f; d < RailReach - 0.05f; d += 0.1f)
            {
                if (!Surface(mid + outw * d + Vector3.up * 0.3f, 0.6f, out float sy, out var sc) || sc == null || sc.name != "Roads" ||
                    Mathf.Abs(sy - mid.y) > 0.1f) break;
                reach = d + 0.3f;
            }
            float halfLat = (RailInboard + reach) * 0.5f;
            var centre = mid - outw * (RailInboard - halfLat) + Vector3.up * ((lo + hi) * 0.5f);
            var rot = Quaternion.LookRotation(along.sqrMagnitude > 1e-6f ? along : Vector3.forward, Vector3.up);
            if (Physics.CheckBox(centre, new Vector3(halfLat, (hi - lo) * 0.5f, Mathf.Max(0.05f, len * 0.5f)), rot,
                                 1 << CityWorld.SolidLayer, QueryTriggerInteraction.Ignore))
                return true;
            bool back = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                foreach (float h in RoadsideRules.BarrierRayHeights)
                    foreach (var hit in Physics.RaycastAll(mid - outw * RailInboard + Vector3.up * h, outw, RailReach + RailInboard, ~0, QueryTriggerInteraction.Ignore))
                        if (Mathf.Abs(hit.normal.y) < 0.6f) return true;
            }
            finally { Physics.queriesHitBackfaces = back; }
            return false;
        }
    }
}
