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
            // THE LANE SURVEY'S SOLIDS (PSX_LANEPROBE): "edge:s:lane" as the
            // audit's LANE lines name them (lane 0 = left, 1 = middle, 2 =
            // right), or "pt:x:z:y" for a world point (a FAN note).
            var lanes = System.Environment.GetEnvironmentVariable("PSX_LANEPROBE") ?? "";
            // PSX_ARMLOG "edge,edge": RailOverArms' reading of those edges'
            // rails, piece by piece, printed under each lane probe
            CityMeshes.armLogEdges.Clear();
            foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_ARMLOG") ?? "").Split(','))
                if (int.TryParse(t.Trim(), out int ae)) CityMeshes.armLogEdges.Add(ae);
            if (lanes.Length > 0)
            {
                CityMeshes.railLog = new List<CityMeshes.RailRecord>();
                if (CityMeshes.armLogEdges.Count > 0) CityMeshes.armLog = new List<string>();
                try
                {
                    foreach (var item in lanes.Split(';'))
                    {
                        var f = item.Trim().Split(':');
                        if (f.Length == 6 && f[0] == "grid")
                            SurfaceGrid(map, trims, buildings, float.Parse(f[1], inv), float.Parse(f[2], inv), float.Parse(f[3], inv),
                                        float.Parse(f[4], inv), float.Parse(f[5], inv), sb);
                        else if (f.Length == 4 && f[0] == "pt")
                            LaneProbe(map, trims, buildings, -1, 0f, 0, new Vector3(float.Parse(f[1], inv), float.Parse(f[3], inv), float.Parse(f[2], inv)), sb);
                        else if (f.Length == 5 && f[0] == "fan")
                        {
                            // "fan:node:edge:inset:lat", the FAN note's point as the audit places it
                            int n = int.Parse(f[1]); var fe = map.edges[int.Parse(f[2])];
                            float inset = float.Parse(f[3], inv), flat = float.Parse(f[4], inv);
                            float trim = trims.TrimAt(fe, n);
                            float at = fe.a == n ? trim : fe.length - trim;
                            var tan = fe.TangentAt(at);
                            var outDir = fe.a == n ? tan : -tan;
                            var w2 = fe.PointAt(at) + new Vector2(-tan.y, tan.x) * flat - outDir * inset;
                            float yExp = Mathf.Lerp(fe.YAt(at), map.nodeY[n], inset / Mathf.Max(trim, 0.01f));
                            sb.AppendLine($"(fan node {n} arm e{fe.index} '{fe.name}' at {at:0.0} of {fe.length:0}, trim {trim:0.0}, inset {inset:0.0}, lane line {flat:+0.0;-0.0})");
                            LaneProbe(map, trims, buildings, -1, 0f, 0, new Vector3(w2.x, yExp, w2.y), sb, new Vector2(-tan.y, tan.x) * (flat < 0f ? -1f : 1f));
                        }
                        else if (f.Length == 3 && int.TryParse(f[0], out int ei) && ei >= 0 && ei < map.edges.Length)
                            LaneProbe(map, trims, buildings, ei, float.Parse(f[1], inv), int.Parse(f[2]), default, sb);
                    }
                }
                finally { CityMeshes.railLog = null; CityMeshes.armLog = null; CityMeshes.armLogEdges.Clear(); }
            }
            Done(sb);
        }

        /// <summary>One lane-survey probe as the audit asks it (the lane line
        /// 0.55 m in from the lane extent, the 0.2 m column over the car
        /// band), and what stands there: the colliders in the column, a walk
        /// across the lane edge of the solids in the band, the first surface
        /// across it (land named by the strip that laid it, at the probe
        /// itself triangle by triangle), every rail the tiles emitted within
        /// 4 m, by owner, with where its traffic face stands against the lane
        /// line, and each road round it with its drawn outline's height at
        /// the probe (as the strips read it).</summary>
        static void LaneProbe(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                              int ei, float s, int lane, Vector3 point, StringBuilder sb, Vector2 ptOut = default)
        {
            Vector2 p2; float y;
            Vector2 outw;   // toward the edge the lane line is nearest
            CityMap.Edge e = ei >= 0 ? map.edges[ei] : null;
            float hwL = 0f, hwR = 0f, lat = 0f;
            if (e != null)
            {
                var p = e.PointAt(s);
                var tan = e.TangentAt(s);
                var right = new Vector2(-tan.y, tan.x);
                y = e.YAt(s);
                CityMeshes.LaneExtents(map, trims, e, s, out hwL, out hwR);
                lat = lane == 0 ? -(hwL - 0.55f) : lane == 2 ? hwR - 0.55f : (hwR - hwL) * 0.5f;
                p2 = p + right * lat;
                outw = lane == 0 ? -right : right;
            }
            else { p2 = new Vector2(point.x, point.z); y = point.y; outw = ptOut.sqrMagnitude > 0f ? ptOut.normalized : Vector2.right; }
            int tx = Mathf.FloorToInt(p2.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p2.y / CityMeshes.TileSize);
            var root = new GameObject("~laneprobe");
            var built = new List<Mesh>();
            CityMeshes.railLog.Clear();
            CityMeshes.armLog?.Clear();
            CityMeshes.groundLog = new List<(string, Vector3, Vector3, Vector3)>();
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
                int solidMask = 1 << CityWorld.SolidLayer;
                if (e != null)
                {
                    int side = lane == 0 ? -1 : 1;
                    sb.AppendLine($"=== LANE e{ei} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} cls{e.cls} s={s:0.0}/{e.length:0} lane{lane} at ({p2.x:0.00},{p2.y:0.00}) tile {tx},{tz}: y {y:0.000} elev {e.ElevatedAt(s)} " +
                                  $"hw {e.width * 0.5f:0.00} lanes L {hwL:0.00} R {hwR:0.00} lane line {lat:+0.00;-0.00}");
                    sb.AppendLine("  side:" + CityMeshes.DescribeClip(map, trims, e, s) + CityMeshes.DescribeSide(map, trims, e, s, side));
                    foreach (var line in CityMeshes.DescribeSections(map, trims, e).Split('\n'))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"s=(-?[\d.]+)");
                        if (!m.Success || Mathf.Abs(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - s) < 16f)
                            if (line.Trim().Length > 0) sb.AppendLine("  " + line.TrimEnd());
                    }
                    // the lane extent every metre round the probe: where the
                    // squeeze is, as the audit sees it
                    var ext = new StringBuilder("  lane extents (s: L R):");
                    for (float q = Mathf.Max(0f, s - 6f); q <= Mathf.Min(e.length, s + 6f) + 1e-3f; q += 1f)
                    {
                        CityMeshes.LaneExtents(map, trims, e, q, out float l, out float r);
                        ext.Append($" {q:0}:{l:0.00}/{r:0.00}");
                    }
                    sb.AppendLine(ext.ToString());
                }
                else sb.AppendLine($"=== POINT ({p2.x:0.00},{p2.y:0.00}) y {y:0.000} tile {tx},{tz}");

                // the audit's column
                var bandC = new Vector3(p2.x, y + (0.15f + RoadsideRules.CarBandM) * 0.5f, p2.y);
                var bandH = new Vector3(0.1f, (RoadsideRules.CarBandM - 0.15f) * 0.5f, 0.1f);
                var cols = Physics.OverlapBox(bandC, bandH, Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore);
                var cl = new StringBuilder("  column:");
                foreach (var c in cols) cl.Append($" {(c.transform.parent != null ? c.transform.parent.name + "/" : "")}{c.name}");
                sb.AppendLine(cols.Length == 0 ? "  column: clear" : cl.ToString());

                // across the lane edge: the solids in the band, every 5 cm
                var walk = new StringBuilder("  walk toward the edge (d from the lane line: solid top-low over the lane, whose):");
                bool back = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                try
                {
                    for (float d = -0.4f; d <= 1.2f + 1e-3f; d += 0.05f)
                    {
                        var q = p2 + outw * d;
                        float top = float.NaN, low = float.NaN; string who = "";
                        foreach (var hb in Physics.RaycastAll(new Vector3(q.x, y + RoadsideRules.CarBandM + 1.5f, q.y), Vector3.down,
                                                              RoadsideRules.CarBandM + 1.5f - 0.15f, solidMask, QueryTriggerInteraction.Ignore))
                        {
                            if (float.IsNaN(top) || hb.point.y > top) { top = hb.point.y; who = (hb.collider.transform.parent != null ? hb.collider.transform.parent.name + "/" : "") + hb.collider.name; }
                            if (float.IsNaN(low) || hb.point.y < low) low = hb.point.y;
                        }
                        walk.Append(float.IsNaN(top) ? $"\n    {d:+0.00;-0.00}: -" : $"\n    {d:+0.00;-0.00}: {low - y:+0.00;-0.00}..{top - y:+0.00;-0.00} {who}");
                    }
                }
                finally { Physics.queriesHitBackfaces = back; }
                sb.AppendLine(walk.ToString());

                // across the lane edge: the first SURFACE met from above, any
                // layer, every 5 cm (a hole in the lane, or land over it)
                var surf = new StringBuilder("  surface across the lane line (d: first surface from 3 m up, y-lane, whose):");
                for (float d = -1.0f; d <= 1.2f + 1e-3f; d += 0.05f)
                {
                    var q = p2 + outw * d;
                    RaycastHit best = default; bool found = false;
                    foreach (var h in Physics.RaycastAll(new Vector3(q.x, y + 3f, q.y), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
                        if (!found || h.point.y > best.point.y) { best = h; found = true; }
                    string what = found ? ((best.collider.transform.parent != null ? best.collider.transform.parent.name + "/" : "") + best.collider.name) : "-";
                    // on ground: the lattice's own height there, so a verge
                    // strip (off the lattice) tells from the lattice (on it)
                    string lat3 = found && best.collider.name == "Ground"
                        ? $" (lattice {CityMeshes.LatticeAt(map, q.x, q.y) - y:+0.000;-0.000}){GroundOwner(q, best.point.y)}" : "";
                    surf.Append($"\n    {d:+0.00;-0.00}: {(found ? (best.point.y - y).ToString("+0.000;-0.000") : "none")} {what}{lat3}");
                    // at the probe itself, the triangles that laid it, vertex by vertex
                    if (Mathf.Abs(d) < 0.01f && lat3.Length > 0) surf.Append(GroundTris(q, best.point.y, y));
                }
                sb.AppendLine(surf.ToString());

                // the rails within 4 m, by owner
                foreach (var r in CityMeshes.railLog)
                {
                    var a = new Vector2(r.a.x, r.a.z); var ab = new Vector2(r.b.x, r.b.z) - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    var near = a + ab * t;
                    if (Vector2.Distance(near, p2) > 4f) continue;
                    var inw = Vector2.Lerp(r.inA, r.inB, t).normalized;
                    var face = near + inw * CityMeshes.RailW;          // the traffic face
                    var outer = near - inw * r.overhang;               // the outer face
                    float yr = Mathf.Lerp(r.a.y, r.b.y, t);
                    string owner = r.node >= 0 ? $"fan chord at node {r.node}" : r.node == -2 ? $"gore nose rail (e{r.edge})" :
                                   $"e{r.edge} '{map.edges[r.edge].name}' side {(r.side < 0 ? "L" : "R")} span {r.s0:0.0}..{r.s1:0.0}";
                    sb.AppendLine($"  rail {owner}: {Vector2.Distance(near, p2):0.00} m off, traffic face {Vector2.Dot(face - p2, outw):+0.00;-0.00} outer {Vector2.Dot(outer - p2, outw):+0.00;-0.00} along the lane's outward, " +
                                  $"base {yr - y:+0.00;-0.00} top {yr + CityMeshes.RailH - y:+0.00;-0.00}, at t {t:0.00} of ({r.a.x:0.0},{r.a.z:0.0})-({r.b.x:0.0},{r.b.z:0.0})");
                }

                // RailOverArms' reading of the PSX_ARMLOG edges (every tile
                // build that drew them; repeats dropped)
                if (CityMeshes.armLog != null)
                {
                    var seenArm = new HashSet<string>();
                    foreach (var line in CityMeshes.armLog)
                        if (seenArm.Add(line)) sb.AppendLine("  arm " + line);
                }

                // the roads round it
                var segs = new HashSet<int>();
                map.EdgeSegsInRect(p2 - Vector2.one * 12f, p2 + Vector2.one * 12f, segs);
                var nearE = new Dictionary<int, (float d, float at)>();
                foreach (int packed in segs)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    var o = map.edges[oi];
                    Vector2 a = o.pts[si], dd = o.pts[si + 1] - a;
                    float L2 = dd.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p2 - a, dd) / L2) : 0f;
                    float dist = Vector2.Distance(p2, a + dd * t);
                    float at = o.s[si] + Mathf.Sqrt(L2) * t;
                    if (!nearE.TryGetValue(oi, out var cur) || dist < cur.d) nearE[oi] = (dist, at);
                }
                var rows = new List<KeyValuePair<int, (float d, float at)>>(nearE);
                rows.Sort((a, b) => a.Value.d.CompareTo(b.Value.d));
                foreach (var kv in rows)
                {
                    var o = map.edges[kv.Key];
                    CityMeshes.LaneExtents(map, trims, o, kv.Value.at, out float ol, out float orr);
                    float oh = CityMeshes.OutlineHeight(map, trims, kv.Key, p2.x, p2.y);
                    sb.AppendLine($"  road e{kv.Key} '{o.name}'{(o.link ? " L" : "")}{(o.bridge ? " B" : "")} cls{o.cls} {kv.Value.d:0.0} m off at s={kv.Value.at:0.0}/{o.length:0}: y {o.YAt(kv.Value.at) - y:+0.00;-0.00} hw {o.width * 0.5f:0.0} lanes {ol:0.00}/{orr:0.00} elev {o.ElevatedAt(kv.Value.at)} trims {trims.atA[kv.Key]:0.0}/{trims.atB[kv.Key]:0.0} nodes {o.a}/{o.b}" +
                                  $" outline {(float.IsNaN(oh) ? "off" : (oh - y).ToString("+0.000;-0.000"))}");
                }
                sb.AppendLine();
            }
            finally
            {
                CityMeshes.groundLog = null;
                foreach (var m in built) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>What laid the ground met at a plan point at a height:
        /// the strips and fills in <see cref="CityMeshes.groundLog"/> whose
        /// triangle is there at that height, or the lattice.</summary>
        static string GroundOwner(Vector2 q, float yHit)
        {
            if (CityMeshes.groundLog == null) return "";
            var tags = new List<string>();
            foreach (var (tag, a, b, c) in CityMeshes.groundLog)
            {
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float wa = ((b.z - c.z) * (q.x - c.x) + (c.x - b.x) * (q.y - c.z)) / d;
                float wb = ((c.z - a.z) * (q.x - c.x) + (a.x - c.x) * (q.y - c.z)) / d;
                float wc = 1f - wa - wb;
                if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                float h = wa * a.y + wb * b.y + wc * c.y;
                if (Mathf.Abs(h - yHit) < 0.02f && !tags.Contains(tag)) tags.Add(tag);
            }
            return tags.Count == 0 ? " [the lattice]" : " [" + string.Join("; ", tags) + "]";
        }

        static string GroundTris(Vector2 q, float yHit, float y)
        {
            var sb = new StringBuilder();
            if (CityMeshes.groundLog == null) return "";
            foreach (var (tag, a, b, c) in CityMeshes.groundLog)
            {
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float wa = ((b.z - c.z) * (q.x - c.x) + (c.x - b.x) * (q.y - c.z)) / d;
                float wb = ((c.z - a.z) * (q.x - c.x) + (a.x - c.x) * (q.y - c.z)) / d;
                float wc = 1f - wa - wb;
                if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                float h = wa * a.y + wb * b.y + wc * c.y;
                if (Mathf.Abs(h - yHit) >= 0.02f) continue;
                sb.Append($"\n        tri {tag}: ({a.x:0.00},{a.z:0.00},{a.y - y:+0.000;-0.000}) ({b.x:0.00},{b.z:0.00},{b.y - y:+0.000;-0.000}) ({c.x:0.00},{c.z:0.00},{c.y - y:+0.000;-0.000})");
            }
            return sb.ToString();
        }

        /// <summary>"grid:x:z:y:half:step": the first surface met from 3 m
        /// over <paramref name="y"/>, in a square round a world point, north
        /// up: '=' a road, 'B' a barrier or rail, 'X' anything else solid,
        /// ground 'o' within 0.3 m of y, 'v' lower (a hole), '^' higher, ' '
        /// nothing. For mapping a hole in a lane or land over one.</summary>
        static void SurfaceGrid(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                float x, float z, float y, float half, float step, StringBuilder sb)
        {
            int tx = Mathf.FloorToInt(x / CityMeshes.TileSize), tz = Mathf.FloorToInt(z / CityMeshes.TileSize);
            var root = new GameObject("~surfgrid");
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
                int n = Mathf.Clamp(Mathf.RoundToInt(half / step), 1, 80);
                sb.AppendLine($"=== GRID round ({x:0.00},{z:0.00}) y {y:0.000}, {step:0.00} m cells, x {x - n * step:0.00}..{x + n * step:0.00} left to right, z north at the top");
                float gMin = float.PositiveInfinity, gMax = float.NegativeInfinity;
                for (int j = n; j >= -n; j--)
                {
                    var row = new StringBuilder($"  {z + j * step,9:0.00} ");
                    for (int i = -n; i <= n; i++)
                    {
                        float qx = x + i * step, qz = z + j * step;
                        RaycastHit best = default; bool found = false;
                        foreach (var h in Physics.RaycastAll(new Vector3(qx, y + 3f, qz), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
                            if (!found || h.point.y > best.point.y) { best = h; found = true; }
                        char c = ' ';
                        if (found)
                        {
                            string nm = best.collider.name;
                            float dy = best.point.y - y;
                            if (nm == "Roads") c = '=';
                            else if (nm == "Barriers") c = 'B';
                            else if (nm == "Ground")
                            {
                                c = dy < -0.3f ? 'v' : dy > 0.3f ? '^' : 'o';
                                gMin = Mathf.Min(gMin, dy); gMax = Mathf.Max(gMax, dy);
                            }
                            else c = 'X';
                        }
                        row.Append(c);
                    }
                    sb.AppendLine(row.ToString() + (j == 0 ? "  <- the point's row" : ""));
                }
                sb.AppendLine(new string(' ', 12 + n) + "^ the point's column");
                if (gMin <= gMax) sb.AppendLine($"  ground cells {gMin:+0.00;-0.00}..{gMax:+0.00;-0.00} m against y");
                sb.AppendLine();
            }
            finally
            {
                foreach (var m in built) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(root);
            }
        }

        static void Probe(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                          int ei, float s, int side, StringBuilder sb)
        {
            var e = map.edges[ei];
            var p = e.PointAt(s);
            int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
            var root = new GameObject("~rsprobe");
            var built = new List<Mesh>();
            CityMeshes.groundLog = new List<(string, Vector3, Vector3, Vector3)>();
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
                    if (found && best.collider.name == "Ground") what += GroundOwner(new Vector2(from.x, from.z), best.point.y);
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
                CityMeshes.groundLog = null;
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
