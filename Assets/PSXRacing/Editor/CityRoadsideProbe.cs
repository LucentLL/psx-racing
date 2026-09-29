using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHAT THE ROADSIDE AUDIT SAW, ONE POINT AT A TIME (WP-04). The audit's
    /// LEDGE / LIP / FACE / OPEN notes name an edge, an arc position and a
    /// side; this stands the same tiles up (the 3x3 round the point, the
    /// centre built last, as the audit does), and prints, for each spot in
    /// PSX_RSPROBE ("edge:s:side;edge:s:side", side -1 = left, 1 = right):
    ///
    ///   * the edge there: height, structure, seats, the tile's sections;
    ///   * the first surface met walking out from the drawn lane edge every
    ///     5 cm to 2.5 m (height against the road, and whose collider);
    ///   * the ground function's terms 0.5-6 m out (which rule set it);
    ///   * the roads within 25 m: their heights at their nearest point.
    ///
    /// Writes city_roadside_probe.txt at the project root. A diagnostic: it
    /// judges nothing.
    /// </summary>
    public static class CityRoadsideProbe
    {
        [MenuItem("PSX Racing/Probe Charlotte Roadside Spots")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { Done(sb.AppendLine("no city data")); return; }
            var spec = System.Environment.GetEnvironmentVariable("PSX_RSPROBE") ?? "";
            var trims = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var item in spec.Split(';'))
            {
                var f = item.Trim().Split(':');
                if (f.Length < 3 || !int.TryParse(f[0], out int ei) || ei < 0 || ei >= map.edges.Length) continue;
                float s = float.Parse(f[1], inv);
                int side = int.Parse(f[2]) < 0 ? -1 : 1;
                Probe(map, trims, buildings, ei, s, side, sb);
            }
            Done(sb);
        }

        static void Probe(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                          int ei, float s, int side, StringBuilder sb)
        {
            var e = map.edges[ei];
            var p = e.PointAt(s);
            int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
            var root = new GameObject("~rsprobe");
            var built = new List<Mesh>();
            try
            {
                for (int pass = 0; pass < 2; pass++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            bool centre = dx == 0 && dz == 0;
                            if (centre != (pass == 1)) continue;   // the centre LAST
                            var tm = CityMeshes.Build(map, trims, buildings, tx + dx, tz + dz);
                            var go = new GameObject($"tile_{tx + dx}_{tz + dz}");
                            go.transform.SetParent(root.transform, false);
                            go.transform.position = tm.origin;
                            built.AddRange(CityWorld.Attach(go, tm, null));
                        }
                Physics.SyncTransforms();

                var tan = e.TangentAt(s);
                var right = new Vector2(-tan.y, tan.x);
                var outw = right * side;
                float y = e.YAt(s);
                CityMeshes.LaneExtents(map, trims, e, s, out float hwL, out float hwR);
                float hw = side < 0 ? hwL : hwR;
                sb.AppendLine($"=== e{ei} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} cls{e.cls} s={s:0.0}/{e.length:0} side {(side < 0 ? "L" : "R")} at ({p.x:0.0},{p.y:0.0}) tile {tx},{tz}: " +
                              $"y {y:0.000} elev {e.ElevatedAt(s)} hw {e.width * 0.5f:0.00} lanes to {hw:0.00}; trims {trims.atA[ei]:0.0}/{trims.atB[ei]:0.0}; nodes {e.a} y {map.nodeY[e.a]:0.00} / {e.b} y {map.nodeY[e.b]:0.00}");
                sb.AppendLine("  seats:" + CityElevation.DescribeSeats(ei) + CityMeshes.DescribeClip(map, trims, e, s) + CityMeshes.DescribeSide(map, trims, e, s, side));
                var st = new StringBuilder("  stations:");
                for (int i = 0; i < e.stS.Length; i++)
                    if (Mathf.Abs(e.stS[i] - s) < 25f)
                        st.Append($" {e.stS[i]:0}:{e.stY[i]:0.00}{(e.stElev[i] ? "^" : "")}{(e.SeatedAt(i) ? "s" : "")}");
                sb.AppendLine(st.ToString());
                foreach (var line in CityMeshes.DescribeSections(map, trims, e).Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"s=(-?[\d.]+)");
                    if (!m.Success || Mathf.Abs(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - s) < 20f)
                        if (line.Trim().Length > 0) sb.AppendLine("  " + line.TrimEnd());
                }

                // the first surface met, walking out from the lane edge
                var edge = new Vector3(p.x + outw.x * hw, y, p.y + outw.y * hw);
                var o3 = new Vector3(outw.x, 0f, outw.y);
                var walk = new StringBuilder("  walk out (d: y-road, what):");
                for (float d = -0.3f; d <= 2.501f; d += 0.05f)
                {
                    var from = edge + o3 * d + Vector3.up * 3f;
                    RaycastHit best = default; bool found = false;
                    foreach (var h in Physics.RaycastAll(from, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
                        if (!found || h.point.y > best.point.y) { best = h; found = true; }
                    string what = found ? ((best.collider.transform.parent != null ? best.collider.transform.parent.name + "/" : "") + best.collider.name) : "-";
                    walk.Append($"\n    {d:+0.00;-0.00}: {(found ? (best.point.y - y).ToString("+0.000;-0.000") : "none")} {what}");
                }
                sb.AppendLine(walk.ToString());

                // the ground function's terms out from the edge
                foreach (float d in new[] { 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f })
                {
                    var q = new Vector2(edge.x, edge.z) + outw * d;
                    float g = CityElevation.Ground(map, q.x, q.y, out var t);
                    sb.AppendLine($"  ground {d:0.0} m out ({q.x:0.0},{q.y:0.0}): {g - y:+0.000;-0.000} = dem {t.dem - y:+0.00;-0.00} carve {t.carve:0.00} blended {t.blended - y:+0.00;-0.00}" +
                                  $" floor {(float.IsNaN(t.floor) ? "-" : (t.floor - y).ToString("+0.00;-0.00"))} e{t.floorEdge} protect {(float.IsNaN(t.protect) ? "-" : (t.protect - y).ToString("+0.00;-0.00"))} e{t.protectEdge}" +
                                  $" deckProtect {(float.IsNaN(t.deckProtect) ? "-" : (t.deckProtect - y).ToString("+0.00;-0.00"))} deckCap {(float.IsNaN(t.deckCap) ? "-" : (t.deckCap - y).ToString("+0.00;-0.00"))} e{t.deckEdge}");
                }

                // the roads round it
                var segs = new HashSet<int>();
                map.EdgeSegsInRect(p - Vector2.one * 25f, p + Vector2.one * 25f, segs);
                var near = new Dictionary<int, (float d, float at)>();
                foreach (int packed in segs)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    var o = map.edges[oi];
                    Vector2 a = o.pts[si], dd = o.pts[si + 1] - a;
                    float L2 = dd.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, dd) / L2) : 0f;
                    float dist = Vector2.Distance(p, a + dd * t);
                    float at = o.s[si] + Mathf.Sqrt(L2) * t;
                    if (!near.TryGetValue(oi, out var cur) || dist < cur.d) near[oi] = (dist, at);
                }
                var rows = new List<KeyValuePair<int, (float d, float at)>>(near);
                rows.Sort((a, b) => a.Value.d.CompareTo(b.Value.d));
                foreach (var kv in rows)
                {
                    var o = map.edges[kv.Key];
                    sb.AppendLine($"  road e{kv.Key} '{o.name}'{(o.link ? " L" : "")}{(o.bridge ? " B" : "")} cls{o.cls} {kv.Value.d:0.0} m off at s={kv.Value.at:0.0}/{o.length:0}: y {o.YAt(kv.Value.at) - y:+0.00;-0.00} hw {o.width * 0.5f:0.0} elev {o.ElevatedAt(kv.Value.at)}");
                }
                sb.AppendLine();
            }
            finally
            {
                foreach (var m in built) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(root);
            }
        }

        static void Done(StringBuilder sb)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, "city_roadside_probe.txt"), sb.ToString());
            Debug.Log("[CityRoadsideProbe] wrote city_roadside_probe.txt");
        }
    }
}
