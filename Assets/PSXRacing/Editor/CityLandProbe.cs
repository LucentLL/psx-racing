using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// HOW FAR THE LAND BESIDE A ROAD IS HELD TO IT (WP-14's before/after).
    /// The owner: the hills read as a road on a flat strip. Along every
    /// grounded, non-ramp edge of the three race routes and of the named
    /// streets below, every 10 m, both sides: the graded ground
    /// (CityElevation.GroundY) against the road at 2, 5, 10, 15, 20 and 30 m
    /// past the pavement's edge, and against the natural land (the DEM there):
    /// how much of the difference the grading kept. Uses only what every
    /// Charlotte build since WP-04 has, so the same file measures the code
    /// before a package and after it. Output: city_land_probe.txt in the
    /// project root.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.CityLandProbe.Run
    /// </summary>
    public static class CityLandProbe
    {
        static readonly float[] Past = { 2f, 5f, 10f, 15f, 20f, 30f };
        static readonly string[] Streets = { "Queens Road", "Central Avenue", "Providence Road", "Beatties Ford", "Archdale", "Rozzelles", "State Street", "Trade" };

        [MenuItem("PSX Racing/City Land Probe")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityLandProbe] no city data"); return; }
            var sb = new StringBuilder();
            sb.AppendLine("CITY LAND PROBE: the graded ground beside the roads, against the road and against the natural land (DEM), every 10 m, both sides, grounded non-ramp edges");
            sb.AppendLine("  d = metres past the pavement's edge; |land-road| p50/p90; kept = (land - road) / (DEM - road), the share of the natural rise or fall the grading leaves, p50 where the DEM is 1 m or more off the road; flat = the land within 0.25 m of the road");
            var routeEdges = new HashSet<int>();
            if (map.routes != null) foreach (var r in map.routes) foreach (int ei in r.edges) routeEdges.Add(ei);
            var streetEdges = new HashSet<int>();
            for (int i = 0; i < map.edges.Length; i++)
            {
                var e = map.edges[i];
                if (e.link || string.IsNullOrEmpty(e.name)) continue;
                foreach (var n in Streets) if (e.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) { streetEdges.Add(i); break; }
            }
            Measure(map, "the three race routes", routeEdges, sb);
            Measure(map, "the named streets (" + string.Join(", ", Streets) + ")", streetEdges, sb);
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_land_probe.txt"), sb.ToString());
            Debug.Log("[CityLandProbe]\n" + sb);
        }

        static void Measure(CityMap map, string label, HashSet<int> edges, StringBuilder sb)
        {
            int nd = Past.Length;
            var diff = new List<float>[nd]; var kept = new List<float>[nd]; var flat = new int[nd]; var n = new int[nd];
            for (int k = 0; k < nd; k++) { diff[k] = new List<float>(); kept[k] = new List<float>(); }
            float km = 0f;
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                if (e.link) continue;
                float hw = e.width * 0.5f;
                for (float s = 5f; s < e.length - 5f; s += 10f)
                {
                    if (e.ElevatedAt(s)) continue;
                    km += 0.01f;
                    var c = e.PointAt(s); var t = e.TangentAt(s); var nr = new Vector2(-t.y, t.x);
                    float y = e.YAt(s);
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = 0; k < nd; k++)
                        {
                            var q = c + nr * side * (hw + Past[k]);
                            float g = CityElevation.GroundY(map, q.x, q.y) - y;
                            float dem = CityElevation.BaseY(q.x, q.y) - y;
                            diff[k].Add(Mathf.Abs(g));
                            if (Mathf.Abs(g) < 0.25f) flat[k]++;
                            n[k]++;
                            if (Mathf.Abs(dem) >= 1f) kept[k].Add(Mathf.Clamp(g / dem, -1f, 2f));
                        }
                }
            }
            sb.AppendLine($"{label}: {edges.Count} edges, {km:0.0} km grounded");
            for (int k = 0; k < nd; k++)
            {
                diff[k].Sort(); kept[k].Sort();
                float P(List<float> v, float p) => v.Count == 0 ? float.NaN : v[Mathf.Min(v.Count - 1, (int)(v.Count * p))];
                sb.AppendLine($"  d {Past[k],2:0} m: |land-road| p50 {P(diff[k], 0.5f):0.00} m, p90 {P(diff[k], 0.9f):0.00} m; kept p50 {P(kept[k], 0.5f):0.00} ({kept[k].Count} points off the road 1 m+); flat {100f * flat[k] / Mathf.Max(1, n[k]):0}% of {n[k]}");
            }
        }
    }
}
