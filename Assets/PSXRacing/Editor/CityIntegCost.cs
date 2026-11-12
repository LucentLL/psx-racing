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
    /// CITY INTEGRATION COST (2026-11-11, cityinteg): triangles per tile (by
    /// TileMeshes mesh, read by reflection so the same file runs on older code)
    /// and tile build ms, on 3x3 rings: the owner's N Graham tile (node 14254),
    /// Uptown, a dense house street and a freeway. Each ring is built twice; the
    /// second (warm) pass is reported. PSX_COST_RINGS = "name:lat,lon;..." adds rings.
    /// Writes city_integ_cost.txt in the project folder.
    /// </summary>
    public static class CityIntegCost
    {
        static Vector2 LL(double lat, double lon) =>
            new Vector2((float)((lon + 80.81770185962013) * 111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)),
                        (float)((lat - 35.18456015184093) * 111132.0));

        public static void Run()
        {
            var I = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                var rings = new List<(string name, Vector2 c)>
                {
                    ("graham_n14254", map.nodes[14254]),
                    ("uptown", map.uptown),
                    ("houses_plaza_midwood", LL(35.2235, -80.8040)),
                    ("freeway_i77", LL(35.2310, -80.8570)),
                };
                string extra = System.Environment.GetEnvironmentVariable("PSX_COST_RINGS");
                if (!string.IsNullOrEmpty(extra))
                    foreach (var s in extra.Split(';'))
                    {
                        var kv = s.Split(':'); if (kv.Length != 2) continue;
                        var p = kv[1].Split(',');
                        if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, I, out double la) && double.TryParse(p[1], NumberStyles.Float, I, out double lo))
                            rings.Add((kv[0], LL(la, lo)));
                    }
                var meshFields = typeof(CityMeshes.TileMeshes).GetFields().Where(f => f.FieldType == typeof(Mesh)).ToArray();
                sb.AppendLine("CITY INTEG COST " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " head " + (System.Environment.GetEnvironmentVariable("PSX_BUILD_HEAD") ?? "?"));
                // warm the statics once
                { var w = CityMeshes.Build(map, trims, proc, Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize), Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize)); Kill(w, meshFields); }
                foreach (var r in rings)
                {
                    int cx = Mathf.FloorToInt(r.c.x / CityMeshes.TileSize), cz = Mathf.FloorToInt(r.c.y / CityMeshes.TileSize);
                    // what roads the ring holds (to confirm what kind of ring it is)
                    var byName = new Dictionary<string, float>(); var byCls = new SortedDictionary<int, float>();
                    foreach (var e in map.edges)
                    {
                        var m = e.PointAt(e.length * 0.5f);
                        if (Mathf.Abs(Mathf.FloorToInt(m.x / CityMeshes.TileSize) - cx) > 1 || Mathf.Abs(Mathf.FloorToInt(m.y / CityMeshes.TileSize) - cz) > 1) continue;
                        string nm = (e.name ?? "") + " c" + e.cls;
                        byName.TryGetValue(nm, out float a); byName[nm] = a + e.length;
                        byCls.TryGetValue(e.cls, out float b); byCls[e.cls] = b + e.length;
                    }
                    double msSum = 0, msMax = 0; long trisAll = 0;
                    var trisBy = new Dictionary<string, long>();
                    for (int pass = 0; pass < 2; pass++)
                    {
                        msSum = 0; msMax = 0; trisAll = 0; trisBy.Clear();
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                var tm = CityMeshes.Build(map, trims, proc, cx + dx, cz + dz);
                                double ms = sw.Elapsed.TotalMilliseconds;
                                msSum += ms; if (ms > msMax) msMax = ms;
                                foreach (var f in meshFields)
                                {
                                    var mesh = f.GetValue(tm) as Mesh; if (mesh == null) continue;
                                    long t = 0; for (int s = 0; s < mesh.subMeshCount; s++) t += (long)mesh.GetIndexCount(s) / 3;
                                    trisAll += t; trisBy.TryGetValue(f.Name, out long o); trisBy[f.Name] = o + t;
                                }
                                Kill(tm, meshFields);
                            }
                    }
                    sb.AppendLine(string.Format(I, "RING {0} tile {1},{2}: build {3:0} ms a tile (max {4:0}, 9 tiles {5:0} ms); triangles {6:0} a tile", r.name, cx, cz, msSum / 9, msMax, msSum, trisAll / 9.0));
                    sb.AppendLine("   tris a tile by mesh: " + string.Join(", ", trisBy.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value / 9}")));
                    sb.AppendLine("   roads (m by class): " + string.Join(", ", byCls.Select(k => $"c{k.Key} {k.Value:0}")) + " | top: " +
                                  string.Join("; ", byName.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key} {k.Value:0} m")));
                }
            }
            catch (System.Exception ex) { sb.AppendLine("COST FAILED " + ex); code = 1; }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_integ_cost.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Kill(CityMeshes.TileMeshes tm, System.Reflection.FieldInfo[] fields)
        {
            foreach (var f in fields) { var m = f.GetValue(tm) as Mesh; if (m != null) Object.DestroyImmediate(m); }
        }
    }
}
