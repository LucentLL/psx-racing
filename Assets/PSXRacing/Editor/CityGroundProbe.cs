using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Why is the ground THAT height here? For a world point (env
    /// PSX_PROBE=x,z, or the uptown spawn), print the ground lattice vertices
    /// round it with every road corridor that has a say in each: distance,
    /// weight, the road's solved height, whether it is structure, the crest
    /// allowance, the pin target. The drive audit says WHERE the land stands
    /// on the tarmac; this says which corridor put it there.
    /// </summary>
    public static class CityGroundProbe
    {
        [MenuItem("PSX Racing/City Ground Probe")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { sb.AppendLine("no map"); Done(sb); return; }

            var at = map.uptown;
            var env = System.Environment.GetEnvironmentVariable("PSX_PROBE");
            if (!string.IsNullOrEmpty(env))
            {
                var parts = env.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float px) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pz))
                    at = new Vector2(px, pz);
            }
            sb.AppendLine($"probe at ({at.x:0.0},{at.y:0.0})  base DEM {CityElevation.BaseY(at.x, at.y):0.00}  ground {CityElevation.GroundY(map, at.x, at.y):0.00}");
            if (map.NearestRoadPoint(at, 40f, false, out int nei, out float nat, out float nd))
            {
                var ne = map.edges[nei];
                sb.AppendLine($"  nearest road e{nei} '{ne.name}'{(ne.link ? " L" : "")} at s={nat:0.0} dist {nd:0.0}: y {ne.YAt(nat):0.00} elev {ne.ElevatedAt(nat)} crest {ne.CrestAt(nat):0.00} hw {ne.width * 0.5f:0.0}");
            }

            float cell = CityMeshes.TileSize / CityMeshes.GroundRes;
            int cx = Mathf.FloorToInt(at.x / cell), cz = Mathf.FloorToInt(at.y / cell);
            var segs = new HashSet<int>();
            for (int dz = 0; dz <= 1; dz++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    float vx = (cx + dx) * cell, vz = (cz + dz) * cell;
                    float g = CityElevation.GroundY(map, vx, vz);
                    sb.AppendLine($"vertex ({vx:0},{vz:0}): ground {g:0.00}  base {CityElevation.BaseY(vx, vz):0.00}");
                    float reach = CityElevation.MaxCorridorHalf + CityElevation.CorridorBlend;
                    segs.Clear();
                    map.EdgeSegsInRect(new Vector2(vx - reach, vz - reach), new Vector2(vx + reach, vz + reach), segs);
                    var seen = new HashSet<string>();
                    var rows = new List<(float dist, string line)>();
                    foreach (var packed in segs)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(new Vector2(vx, vz) - a, d) / L2) : 0f;
                        float dist = Vector2.Distance(new Vector2(vx, vz), a + d * t);
                        float ch = Mathf.Min(e.CorridorHalf, CityElevation.MaxCorridorHalf);
                        if (dist > ch + CityElevation.CorridorBlend) continue;
                        float s = e.s[si] + Mathf.Sqrt(L2) * t;
                        float w = dist <= ch ? 1f : 1f - (dist - ch) / CityElevation.CorridorBlend;
                        w = w * w * (3f - 2f * w);
                        bool el = e.ElevatedAt(s);
                        string key = ei + ":" + Mathf.RoundToInt(s);
                        if (!seen.Add(key)) continue;
                        rows.Add((dist, $"    e{ei} '{e.name}'{(e.link ? " L" : "")}{(e.bridge ? " B" : "")} cls{e.cls} s={s:0.0}/{e.length:0} dist {dist:0.0} ch {ch:0.0} w {w:0.00} y {e.YAt(s):0.00} {(el ? "STRUCTURE cap " + (e.YAt(s) - CityElevation.DeckThick - CityElevation.UnderDeckAir).ToString("0.00") : "pin " + (e.YAt(s) - CityElevation.CorridorSink - e.CrestAt(s)).ToString("0.00") + " crest " + e.CrestAt(s).ToString("0.00"))}"));
                    }
                    rows.Sort((p, q) => p.dist.CompareTo(q.dist));
                    foreach (var r in rows) sb.AppendLine(r.line);
                }
            Done(sb);
        }

        static void Done(StringBuilder sb)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(
                System.IO.Directory.GetParent(Application.dataPath).FullName, "city_ground_probe.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
