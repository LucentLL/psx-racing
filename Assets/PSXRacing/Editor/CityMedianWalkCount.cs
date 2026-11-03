using System.Collections.Generic;
using System.Text;
using PSXRacing.City;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FIVE POINTS / PUBLISH 1 (2026-11-03): how many walk sections the median
    /// test (CityMeshes.Walks InDividedMedian) keeps out of a divided road's
    /// median, city-wide. Builds every tile within reach of two one-way
    /// carriageways of one name running opposite ways (the only places the
    /// test can say yes) and sums CityMeshes.WalkStats; per tile counts go to
    /// the csv so two code versions can be diffed tile by tile. The
    /// per-place list (WalkStats.medianSkipAt) is read by reflection, so the
    /// tool compiles against a tree without it. Writes city_median_walks.txt
    /// and city_median_walks.csv in the project root.
    /// </summary>
    public static class CityMedianWalkCount
    {
        [MenuItem("PSX Racing/Count Walks Kept Out Of Medians")]
        public static void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var map = CityMap.Get();
            var trims = CityMeshes.ComputeTrims(map);
            var tiles = new SortedSet<long>();
            var segs = new HashSet<int>();
            int pairsSeen = 0;
            foreach (var e in map.edges)
            {
                if (e == null || !e.oneway || e.link || string.IsNullOrEmpty(e.name) || e.a == e.b || e.pts == null) continue;
                for (int i = 0; i + 1 < e.pts.Length; i++)
                {
                    var d = e.pts[i + 1] - e.pts[i];
                    if (d.sqrMagnitude < 1e-4f) continue;
                    d.Normalize();
                    var m = 0.5f * (e.pts[i] + e.pts[i + 1]);
                    segs.Clear();
                    map.EdgeSegsInRect(m - Vector2.one * 30f, m + Vector2.one * 30f, segs);
                    bool pair = false;
                    foreach (int packed in segs)
                    {
                        var o = map.edges[packed >> 12];
                        if (o == e || !o.oneway || o.link || o.name != e.name) continue;
                        int s = packed & 0xFFF;
                        if (s + 1 >= o.pts.Length) continue;
                        var od = o.pts[s + 1] - o.pts[s];
                        if (od.sqrMagnitude < 1e-4f) continue;
                        if (Vector2.Dot(od.normalized, d) < -0.5f) { pair = true; break; }
                    }
                    if (!pair) continue;
                    pairsSeen++;
                    for (int dx = -1; dx <= 1; dx += 2)
                        for (int dz = -1; dz <= 1; dz += 2)
                        {
                            int tx = Mathf.FloorToInt((m.x + dx * 24f) / CityMeshes.TileSize), tz = Mathf.FloorToInt((m.y + dz * 24f) / CityMeshes.TileSize);
                            tiles.Add(((long)tx << 32) | (uint)tz);
                        }
                }
            }
            var noBld = new Dictionary<long, List<CityBuildings.B>>();
            CityMeshes.WalkStats.Reset();
            var csv = new StringBuilder("tx,tz,skips\n");
            int prev = 0, tilesWith = 0;
            float findMs = (float)sw.Elapsed.TotalMilliseconds;
            foreach (long k in tiles)
            {
                int tx = (int)(k >> 32), tz = (int)(uint)k;
                var tm = CityMeshes.Build(map, trims, noBld, tx, tz);
                foreach (var mm in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings })
                    if (mm != null) Object.DestroyImmediate(mm);
                int now = CityMeshes.WalkStats.medianSkips;
                if (now > prev) { csv.Append($"{tx},{tz},{now - prev}\n"); tilesWith++; }
                prev = now;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"MEDIAN WALK COUNT: median walk test {(CityMeshes.MedianNoWalkOn ? "ON" : "OFF (PSX_CITY_MEDIAN_NOWALK=0)")}; {tiles.Count} tiles built (within reach of {pairsSeen} opposite same-name one-way segments), find {findMs / 1000f:0.0} s, total {sw.Elapsed.TotalSeconds:0.0} s");
            sb.AppendLine($"  sections kept out of medians: {CityMeshes.WalkStats.medianSkips} in {tilesWith} tiles");
            sb.AppendLine("  " + CityMeshes.WalkStats.Line());
            var f = typeof(CityMeshes.WalkStats).GetField("medianSkipAt", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f != null && f.GetValue(null) is Dictionary<long, string> at)
            {
                sb.AppendLine($"  places ({at.Count}):");
                foreach (var kv in at) sb.AppendLine("    " + kv.Value);
            }
            else sb.AppendLine("  (no per-place list in this tree)");
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_median_walks.txt"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_median_walks.csv"), csv.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
