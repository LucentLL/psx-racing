using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Road-surface stage 1, edit-mode batch tools (no mesh, no play mode):
    ///   DumpNear     PSX_RS_DUMP="name:x,z,r;name:x,z,r": the map round each point - every node (index, position, patch,
    ///                fan key, the junction surface that owns it) and edge (index, ends, name, class, lanes, one-way, link,
    ///                length, the rules' trims, the line model's extents every 5 m, its points). Screenshots/rs/rs_dump_TAG.txt
    ///   RunMeasured  every junction of the measures source (PSX_CITY_MEASURES) solved as the tile build solves it
    ///                (JunctionSurfaces.Ensure): the result as JSON for the overlays, the rule's cluster as a fixture and
    ///                its arms' lengths and directions for the offline harness. Screenshots/rs/rs_measured_TAG.*
    /// PSX_RS_TAG names the outputs.
    /// </summary>
    public static class JunctionSurfaceTools
    {
        static string Dir()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "rs");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void DumpNear()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_RS_TAG") ?? "run";
            var sb = new StringBuilder($"RS DUMP {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var trims = CityMeshes.ComputeTrims(map);
                foreach (var item in (System.Environment.GetEnvironmentVariable("PSX_RS_DUMP") ?? "").Split(';'))
                {
                    var nv = item.Split(':'); if (nv.Length < 2) continue;
                    var c = nv[1].Split(',');
                    var p = new Vector2(float.Parse(c[0], inv), float.Parse(c[1], inv)); float r = float.Parse(c[2], inv);
                    sb.Append($"== {nv[0]} ({p.x:0.0}, {p.y:0.0}) r {r:0}\n");
                    var edges = new HashSet<int>();
                    for (int n = 0; n < map.nodes.Length; n++)
                    {
                        if ((map.nodes[n] - p).magnitude > r) continue;
                        sb.Append(string.Format(inv, "node {0} {1:0.00} {2:0.00} y {3:0.00} patch {4} mitre {5} fankey {6} edges {7}\n", n, map.nodes[n].x, map.nodes[n].y, map.nodeY[n],
                            trims.patch[n] ? 1 : 0, trims.mitre[n] ? 1 : 0, trims.patch[n] ? CityMeshes.FanKey(trims, n) : -1, string.Join(",", map.nodeEdges[n])));
                        foreach (int ei in map.nodeEdges[n]) edges.Add(ei);
                    }
                    foreach (int ei in edges.OrderBy(x => x))
                    {
                        var e = map.edges[ei];
                        var ext = new List<string>();
                        for (float s = 0f; s <= e.length + 1e-3f; s += 5f) { LineModel.Extents(e, s, out float eM, out float eP); ext.Add(string.Format(inv, "{0:0}:{1:0.00}/{2:0.00}", s, eP, eM)); }
                        sb.Append(string.Format(inv, "edge {0} a {1} b {2} cls {3} lanes {4} oneway {5} link {6} len {7:0.00} atA {8:0.00} atB {9:0.00} name '{10}' ext(s:left/right) {11} pts {12}\n",
                            ei, e.a, e.b, e.cls, e.lanes, e.oneway ? 1 : 0, e.link ? 1 : 0, e.length, trims.atA[ei], trims.atB[ei], e.name ?? "", string.Join(" ", ext),
                            string.Join(" ", e.pts.Select(q => string.Format(inv, "{0:0.00},{1:0.00}", q.x, q.y)))));
                    }
                }
            }
            catch (System.Exception ex) { sb.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(Dir(), $"rs_dump_{tag}.txt"), sb.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }

        public static void RunMeasured()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_RS_TAG") ?? "run";
            var log = new StringBuilder($"RS MEASURED {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}; measures {System.Environment.GetEnvironmentVariable("PSX_CITY_MEASURES")}\n");
            var json = new StringBuilder("[\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                bool was = CityMeshes.RoadSurfaceOn; CityMeshes.RoadSurfaceOn = true;
                JunctionMeasures.Reset(); JunctionSurfaces.Reset();
                var rule = CityMeshes.ComputeTrims(map);
                var trims = CityMeshes.SurfaceTrims(map, rule);
                CityMeshes.RoadSurfaceOn = was;
                bool first = true;
                foreach (var b in JunctionSurfaces.All)
                {
                    var r = b.res; var o = b.origin;
                    string chk = r.refused != null ? "refused: " + r.refused : (JunctionOutlineTests.Check(r, 0.5f) ?? "clean");
                    log.Append($"{b.name} nodes {string.Join(" ", b.nodes.OrderBy(x => x))}: {chk}; corners {string.Join(", ", r.corners.Select(c => $"e{c.a}/e{c.b} {c.shape} r{c.r:0.0}{(c.note.Length > 0 ? " (" + c.note + ")" : "")}"))}; outline {r.outline.Count} vertices, {r.holes.Count} islands, fill {r.tris.Count / 3} triangles; solve {r.solveMs:0.00} ms, fill {r.fillMs:0.00} ms, in all {b.ms:0.0} ms; plane dev {r.planeDev * 1000f:0.0} mm, warp {r.warp:0.00} m\n");
                    foreach (var kv in b.rowOf) { var e = map.edges[kv.Key]; bool atA = b.nodes.Contains(e.a); log.Append(string.Format(inv, "  e{0}: ribbon trim {1:0.0} m (the rule's {2:0.0}), edge {3:0.0} m\n", kv.Key, kv.Value, atA ? rule.atA[kv.Key] : rule.atB[kv.Key], e.length)); }
                    foreach (var l in b.log) log.Append("  " + l + "\n");
                    foreach (var l in b.laneConflicts) log.Append("  LANE CHECK " + l + "\n");
                    if (b.laneConflicts.Count == 0) log.Append("  LANE CHECK clear\n");
                    string P(IEnumerable<Vector2> l) => "[" + string.Join(",", l.Select(p => string.Format(inv, "[{0:0.000},{1:0.000}]", p.x + o.x, p.y + o.y))) + "]";
                    if (!first) json.Append(",\n"); first = false;
                    json.Append("{\"name\":\"" + b.name + "\",\"provider\":\"" + r.provider + "\",\"check\":\"" + chk.Replace("\"", "'") + "\",\"outline\":" + P(r.outline));
                    json.Append(",\"corners\":[" + string.Join(",", r.corners.Select(c => "{\"a\":" + c.a + ",\"b\":" + c.b + ",\"shape\":\"" + c.shape + "\",\"r\":" + c.r.ToString("0.00", inv) + ",\"pts\":" + P(c.pts) + "}")) + "]");
                    if (r.rowR != null) json.Append(",\"rows\":[" + string.Join(",", Enumerable.Range(0, r.rowR.Length).Select(i => "{\"id\":" + r.arms[i].id + ",\"pts\":" + P(new[] { r.rowR[i], r.rowL[i] }) + "}")) + "]");
                    json.Append(",\"holes\":[" + string.Join(",", r.holes.Select(h => P(h))) + "],\"holeKind\":[" + string.Join(",", r.holeKind) + "]}");
                    // the rule's cluster as a fixture (no measure applied) and the arms' lengths / directions
                    JunctionSurfaces.BuildCluster(map, b.nodes, null, (e, node) => e.a == node ? rule.atA[e.index] : rule.atB[e.index], out var arms, out var links, out var org);
                    File.WriteAllText(Path.Combine(Dir(), b.name + ".txt"), CityOutlineProto.FixtureText(b.name, "measured junction (rules' borders)", org, arms.ConvertAll(a => Shift(a, org)), links.ConvertAll(l => Shift(l, org))));
                    File.WriteAllText(Path.Combine(Dir(), "meta_" + b.name + ".txt"), string.Join("\n", arms.Select(a => string.Format(inv, "{0} {1:0.000} {2}", a.id, map.edges[a.id].length, b.nodes.Contains(map.edges[a.id].a) ? 1 : 0))) + "\n");
                }
                json.Append("\n]\n");
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(Dir(), $"rs_measured_{tag}.json"), json.ToString());
            File.WriteAllText(Path.Combine(Dir(), $"rs_measured_{tag}.txt"), log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }

        // FixtureText subtracts the origin: hand it absolute coordinates
        static JunctionOutline.Arm Shift(JunctionOutline.Arm a, Vector2 o) => new JunctionOutline.Arm { id = a.id, oneway = a.oneway, radius = a.radius, name = a.name, Y = a.Y, L = a.L.ConvertAll(p => p + o), R = a.R.ConvertAll(p => p + o), C = a.C.ConvertAll(p => p + o) };
        static JunctionOutline.Link Shift(JunctionOutline.Link l, Vector2 o) => new JunctionOutline.Link { id = l.id, L = l.L.ConvertAll(p => p + o), R = l.R.ConvertAll(p => p + o) };
    }
}
