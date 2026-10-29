using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    public static partial class CityPreview
    {
        /// <summary>
        /// STREETS PASS 4 (2026-10-28): the owner's Tryon Sprint start from
        /// the chase camera (every 100 m of its first 500), the junctions it
        /// passes from above, an uptown crosswalk corner at the windscreen, a
        /// lot from the street and a sidewalk at a walker's eye - each shot
        /// BEFORE (CityMeshes.WalksOn off, PSX_CITY_WALKS=0) and AFTER in one
        /// run, with each view's centre tile's triangles and draws.
        /// PSX_STREETS_ONLY=a,b limits it to the views whose names start so.
        /// Writes Screenshots/City/streets/*_0.png (before) and *_1.png.
        ///   -executeMethod PSXRacing.EditorTools.CityPreview.RunStreets
        /// </summary>
        public static void RunStreets()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[Streets] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "streets");
            Directory.CreateDirectory(dir);
            PSXRacingBuilder.EnsureCityTextures();
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var views = new List<(string name, Vector3 eye, Vector3 look, float ortho, float fov, Vector2 ringAt)>();
            var route = map.RouteById("tryon");
            if (route != null)
            {
                for (int d = 0; d <= 400; d += 100)
                    if (RoutePose(map, route, d, out var pos, out var fwd, out _, out _))
                    {
                        var eye = pos - fwd * 6.2f + Vector3.up * 2.1f;
                        views.Add(($"tryon_chase_{d:000}", eye, pos + Vector3.up * 0.9f + fwd * 22f, 0f, 60f, new Vector2(pos.x, pos.z) + new Vector2(fwd.x, fwd.z) * 60f));
                    }
                // the junctions it passes in its first 520 m, from above
                float acc = 0f;
                for (int k = 0; k < route.edges.Length && k < route.dirs.Length; k++)
                {
                    var e = map.edges[route.edges[k]];
                    int endNode = route.dirs[k] > 0 ? e.b : e.a;
                    acc += e.length;
                    float d = acc - route.startM;
                    if (d < -40f) continue;
                    if (d > 520f) break;
                    if (!trims.patch[endNode]) continue;
                    var np = map.nodes[endNode];
                    float y = map.nodeY[endNode];
                    views.Add(($"tryon_top_{Mathf.Max(0f, d):000}_n{endNode}", new Vector3(np.x, y + 300f, np.y), new Vector3(np.x, y, np.y), 34f, 60f, np));
                }
                // a walker's eye on the right-hand walk 40 m past the start, along the street
                if (RoutePose(map, route, 40f, out var wp, out var wf, out var we, out float ws))
                {
                    var right = new Vector3(wf.z, 0f, -wf.x).normalized;
                    float hw = we.width * 0.5f;
                    var foot = wp - Vector3.up * 0.35f + right * (hw + 1.4f - (we.oneway ? 0f : we.width * 0.25f));
                    views.Add(("tryon_walk_eye", foot + Vector3.up * (CityMeshes.CurbH + 1.6f), foot + Vector3.up * 1.2f + wf * 40f - right * 2f, 0f, 60f, new Vector2(foot.x, foot.z) + new Vector2(wf.x, wf.z) * 30f));
                }
            }
            {
                // uptown: South Tryon 38 m before Trade (the Square's crosswalks), the windscreen
                const float h = 50f * Mathf.Deg2Rad;
                var at = new Vector2(-2338.2f, 4710.4f);
                if (SnapNamed(map, at, "Tryon", out var te, out float tsn))
                {
                    var eye = new Vector3(at.x, te.YAt(tsn) + 1.2f, at.y);
                    var d = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
                    views.Add(("uptown_square_eye", eye, eye + d * 60f + Vector3.down * 1.0f, 0f, 60f, at + new Vector2(d.x, d.z) * 30f));
                    views.Add(("uptown_square_top", new Vector3(-2316f, te.YAt(tsn) + 300f, 4733f), new Vector3(-2316f, te.YAt(tsn), 4733f), 40f, 60f, new Vector2(-2316f, 4733f)));
                }
                // a suburban shopping centre across its lot from the street
                var ls = new Vector2(-2960f, -1201f);
                if (SnapNamed(map, ls, "", out var le, out float lsn))
                {
                    var eye = new Vector3(ls.x, le.YAt(lsn) + 2.1f, ls.y);
                    views.Add(("lot_street_eye", eye, new Vector3(-2760f, le.YAt(lsn) + 0.4f, -1204f), 0f, 60f, new Vector2(-2860f, -1203f)));
                }
            }
            string only = System.Environment.GetEnvironmentVariable("PSX_STREETS_ONLY");
            var onlyList = string.IsNullOrWhiteSpace(only) ? null : only.Split(',');

            var world = new GameObject("~StreetsWorld");
            var log = new System.Text.StringBuilder("view\tpass\ttile\ttris\tdraws\twalks\n");
            bool was = CityMeshes.WalksOn;
            try
            {
                foreach (var v in views)
                {
                    if (onlyList != null && System.Array.FindIndex(onlyList, o => v.name.StartsWith(o.Trim())) < 0) continue;
                    for (int pass = 0; pass <= 1; pass++)
                    {
                        CityMeshes.WalksOn = pass == 1;
                        CityMeshes.WalkStats.Reset();
                        var tiles = BuildRing(map, trims, buildings, world, v.ringAt, 1, out _);
                        var rot = v.ortho > 0f ? Quaternion.LookRotation(Vector3.down, Vector3.forward) : Quaternion.LookRotation(v.look - v.eye);
                        Shoot(dir, $"{v.name}_{pass}", v.eye, rot, v.ortho, 3000f, 1280, 720, v.fov, 0.2f);
                        int ctx = Mathf.FloorToInt(v.ringAt.x / CityMeshes.TileSize), ctz = Mathf.FloorToInt(v.ringAt.y / CityMeshes.TileSize);
                        foreach (var t in tiles)
                        {
                            if (t == null || t.name != $"~tile_{ctx}_{ctz}") continue;
                            TileCost(t, out int tris, out int draws);
                            log.Append($"{v.name}\t{pass}\t{ctx},{ctz}\t{tris}\t{draws}\t{CityMeshes.WalkStats.Line()}\n");
                        }
                        foreach (var t in tiles) if (t != null) Object.DestroyImmediate(t);
                    }
                }
            }
            finally
            {
                CityMeshes.WalksOn = was;
                Object.DestroyImmediate(world);
            }
            File.WriteAllText(Path.Combine(dir, "streets_cost.txt"), log.ToString());
            Debug.Log("[Streets] " + log.ToString().Replace("\n", " | "));
        }

        /// <summary>The street shots, then the drive audit along the race
        /// routes (CityAudit.RunDrive, city_drive.txt): one Unity start.</summary>
        public static void RunStreetsAndDrive()
        {
            RunStreets();
            CityAudit.RunDrive();
        }

        /// <summary>The collider probe, then the roadside lane probe
        /// (CityRoadsideProbe, PSX_LANEPROBE): one Unity start.</summary>
        public static void RunStreetsProbes()
        {
            RunColliderProbe();
            CityRoadsideProbe.Run();
        }

        /// <summary>The street shots, the collider probe, then the drive audit
        /// with the walks ON (city_drive.txt, also kept as city_drive_walks.txt)
        /// and OFF (city_drive_nowalks.txt, the same code's control): one Unity start.</summary>
        public static void RunStreetsDriveAB()
        {
            RunStreets();
            RunColliderProbe();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string drive = Path.Combine(root, "city_drive.txt"), on = Path.Combine(root, "city_drive_walks.txt"), off = Path.Combine(root, "city_drive_nowalks.txt");
            CityAudit.RunDrive();
            if (File.Exists(drive)) File.Copy(drive, on, true);
            bool was = CityMeshes.WalksOn;
            try { CityMeshes.WalksOn = false; CityAudit.RunDrive(); }
            finally { CityMeshes.WalksOn = was; }
            if (File.Exists(drive)) File.Copy(drive, off, true);
            if (File.Exists(on)) File.Copy(on, drive, true);
        }

        /// <summary>
        /// THE STREETS COLLIDER PROBE (a diagnostic): the hit tile's own Ground
        /// and Roads collider triangles round each PSX_CPROBE point ("x,z,y;...",
        /// world metres, y the hit's height), the tile built (its 3x3, the
        /// centre last, as the lane probe does) with the walks ON and OFF; each
        /// triangle marked + (walks only), - (no walks only) or = (both), with
        /// its slot, normal, corners from the point and what laid it
        /// (CityMeshes.groundLog). Writes Screenshots/City/streets/collider_probe.txt.
        ///   -executeMethod PSXRacing.EditorTools.CityPreview.RunColliderProbe
        /// </summary>
        public static void RunColliderProbe()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "streets");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, "collider_probe.txt");
            var sb = new System.Text.StringBuilder();
            var map = CityMap.Get();
            if (map == null) { File.WriteAllText(outPath, "no city data"); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            bool was = CityMeshes.WalksOn;
            try
            {
                foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_CPROBE") ?? "").Split(';'))
                {
                    var f = item.Trim().Split(',');
                    if (f.Length < 3) continue;
                    var P = new Vector3(float.Parse(f[0], inv), float.Parse(f[2], inv), float.Parse(f[1], inv));
                    int tx = Mathf.FloorToInt(P.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(P.z / CityMeshes.TileSize);
                    sb.AppendLine($"=== ({P.x:0.00},{P.z:0.00}) y {P.y:0.00} tile {tx},{tz}");
                    var rows = new Dictionary<string, (string text, int mask)>();
                    for (int pass = 1; pass >= 0; pass--)
                    {
                        CityMeshes.WalksOn = pass == 1;
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (dx != 0 || dz != 0) CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                        CityMeshes.WalkStats.Reset();
                        CityMeshes.groundLog = new List<(string, Vector3, Vector3, Vector3)>();
                        var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                        ProbeTris(tm.ground, tm.groundSlots, "Ground", tm.origin, P, pass, rows);
                        ProbeTris(tm.roads, tm.roadSlots, "Roads", tm.origin, P, pass, rows);
                        sb.AppendLine($"  walks {(pass == 1 ? "on" : "off")}: {CityMeshes.WalkStats.Line()}");
                        CityMeshes.groundLog = null;
                    }
                    var keys = new List<string>(rows.Keys);
                    keys.Sort(System.StringComparer.Ordinal);
                    foreach (var k in keys)
                        sb.AppendLine("  " + (rows[k].mask == 3 ? "=" : rows[k].mask == 2 ? "+" : "-") + " " + rows[k].text);
                }
            }
            finally { CityMeshes.WalksOn = was; CityMeshes.groundLog = null; }
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[Streets] collider probe: " + outPath);
        }

        static void ProbeTris(Mesh m, CityMeshes.Slot[] slots, string name, Vector3 o, Vector3 P, int pass,
                              Dictionary<string, (string text, int mask)> rows)
        {
            if (m == null) return;
            var v = m.vertices;
            var p2 = new Vector2(P.x, P.z);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                string slot = slots != null && s < slots.Length ? slots[s].ToString() : "s" + s;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]] + o, b = v[t[i + 1]] + o, c = v[t[i + 2]] + o;
                    float lo = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), hi = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    if (hi < P.y - 0.3f || lo > P.y + 0.3f || PlanDist(p2, a, b, c) > 0.6f) continue;
                    var n = Vector3.Cross(b - a, c - a).normalized;
                    string key = $"{name} {slot} {CornerKey(a, b, c)}";
                    int bit = pass == 1 ? 2 : 1;
                    if (rows.TryGetValue(key, out var r)) { rows[key] = (r.text, r.mask | bit); continue; }
                    rows[key] = ($"{name}/{slot} n({n.x:+0.00;-0.00},{n.y:+0.00;-0.00},{n.z:+0.00;-0.00}) {Rel(a, P)} {Rel(b, P)} {Rel(c, P)} [{TagOf(a, b, c)}]", bit);
                }
            }
        }

        static string Rel(Vector3 a, Vector3 P) => $"({a.x - P.x:+0.00;-0.00},{a.z - P.z:+0.00;-0.00},{a.y - P.y:+0.00;-0.00})";

        static string CornerKey(Vector3 a, Vector3 b, Vector3 c)
        {
            var k = new[] { $"{Mathf.RoundToInt(a.x * 50f)}:{Mathf.RoundToInt(a.z * 50f)}:{Mathf.RoundToInt(a.y * 50f)}",
                            $"{Mathf.RoundToInt(b.x * 50f)}:{Mathf.RoundToInt(b.z * 50f)}:{Mathf.RoundToInt(b.y * 50f)}",
                            $"{Mathf.RoundToInt(c.x * 50f)}:{Mathf.RoundToInt(c.z * 50f)}:{Mathf.RoundToInt(c.y * 50f)}" };
            System.Array.Sort(k, System.StringComparer.Ordinal);
            return string.Join("|", k);
        }

        /// <summary>Plan distance from p to the triangle (0 inside it).</summary>
        static float PlanDist(Vector2 p, Vector3 a3, Vector3 b3, Vector3 c3)
        {
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z), c = new Vector2(c3.x, c3.z);
            float d1 = Cross2(b - a, p - a), d2 = Cross2(c - b, p - b), d3 = Cross2(a - c, p - c);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            if (!(neg && pos) && Mathf.Abs(Cross2(b - a, c - a)) > 1e-6f) return 0f;
            return Mathf.Min(SegDist(p, a, b), Mathf.Min(SegDist(p, b, c), SegDist(p, c, a)));
        }

        static float Cross2(Vector2 u, Vector2 w) => u.x * w.y - u.y * w.x;

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.sqrMagnitude;
            float u = l2 > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return Vector2.Distance(p, a + ab * u);
        }

        /// <summary>What laid a triangle: a groundLog entry with the same
        /// corners, else one whose plan holds its centre at its height.</summary>
        static string TagOf(Vector3 a, Vector3 b, Vector3 c)
        {
            var log = CityMeshes.groundLog;
            if (log == null) return "";
            string key = CornerKey(a, b, c);
            foreach (var (tag, la, lb, lc) in log) if (CornerKey(la, lb, lc) == key) return tag;
            var m = (a + b + c) / 3f;
            foreach (var (tag, la, lb, lc) in log)
            {
                if (PlanDist(new Vector2(m.x, m.z), la, lb, lc) > 0.001f) continue;
                Vector2 pa = new Vector2(la.x, la.z), pb = new Vector2(lb.x, lb.z), pc = new Vector2(lc.x, lc.z);
                float den = Cross2(pb - pa, pc - pa);
                if (Mathf.Abs(den) < 1e-6f) continue;
                float wb = Cross2(new Vector2(m.x, m.z) - pa, pc - pa) / den, wc = Cross2(pb - pa, new Vector2(m.x, m.z) - pa) / den;
                float h = la.y + wb * (lb.y - la.y) + wc * (lc.y - la.y);
                if (Mathf.Abs(h - m.y) < 0.02f) return "~" + tag;
            }
            return "";
        }

        /// <summary>The tile's own meshes (not its props): triangles and submeshes (draws).</summary>
        static void TileCost(GameObject tile, out int tris, out int draws)
        {
            tris = 0; draws = 0;
            foreach (Transform c in tile.transform)
            {
                var mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || c.name != mf.sharedMesh.name && c.name != "WBeams") continue;
                var m = mf.sharedMesh;
                for (int s = 0; s < m.subMeshCount; s++) tris += (int)m.GetIndexCount(s) / 3;
                draws += m.subMeshCount;
            }
        }

        /// <summary>A point <paramref name="dist"/> metres past the route's
        /// start, on the right-hand lane of a two-way street, its heading.</summary>
        static bool RoutePose(CityMap map, CityMap.Route r, float dist, out Vector3 pos, out Vector3 fwd, out CityMap.Edge edge, out float sOn)
        {
            pos = default; fwd = Vector3.forward; edge = null; sOn = 0f;
            if (r.edges == null || r.dirs == null) return false;
            float want = r.startM + dist, acc = 0f;
            for (int k = 0; k < r.edges.Length && k < r.dirs.Length; k++)
            {
                var e = map.edges[r.edges[k]];
                if (acc + e.length < want) { acc += e.length; continue; }
                float into = Mathf.Clamp(want - acc, 0f, e.length);
                bool f = r.dirs[k] > 0;
                float s = f ? into : e.length - into;
                Vector2 p = e.PointAt(s), t2 = e.TangentAt(s) * (f ? 1f : -1f);
                fwd = new Vector3(t2.x, 0f, t2.y).normalized;
                var right = new Vector3(t2.y, 0f, -t2.x).normalized;
                float lateral = e.oneway ? 0f : e.width * 0.25f;
                pos = new Vector3(p.x, e.YAt(s) + 0.35f, p.y) + right * lateral;
                edge = e; sOn = s;
                return true;
            }
            return false;
        }
    }
}
