using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LEFT-TURN POCKETS' CENSUS (2026-11-10, CityMeshes.TurnPockets): from
    /// the map and its trims alone (no tiles), every pocket the rule lays -
    /// per road, then each one (node, approach edge, lat/lon, lane width,
    /// storage, taper, the median beside the storage, the run it was fitted
    /// to) - and the approaches that open on the median side and get none,
    /// by reason. To PSXRacing_turn_pockets.txt.
    /// Headless: -executeMethod PSXRacing.EditorTools.TurnPocketProbe.Run
    /// </summary>
    public static class TurnPocketProbe
    {
        [MenuItem("PSX Racing/Probe Turn Pockets")]
        public static void Run()
        {
            var sb = new StringBuilder();
            string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_turn_pockets.txt");
            var map = CityMap.Get();
            if (map == null) { File.WriteAllText(outPath, "no city data\n"); return; }
            CityMeshes.ComputeTrims(map);
            sb.AppendLine("TURN POCKETS  " + CityMeshes.TurnPocketLine());
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var byRoad = new Dictionary<string, (int n, float m)>();
            foreach (var p in CityMeshes.TurnPockets)
            {
                var e = map.edges[p.edge];
                byRoad.TryGetValue(e.name, out var v);
                byRoad[e.name] = (v.n + 1, v.m + p.storage + p.taper);
            }
            sb.AppendLine($"BY ROAD ({byRoad.Count} roads):");
            foreach (var kv in byRoad.OrderByDescending(k => k.Value.n)) sb.AppendLine($"  {kv.Value.n,4}  {kv.Value.m,7:0} m  {kv.Key}");
            sb.AppendLine("EACH:");
            foreach (var p in CityMeshes.TurnPockets)
            {
                var e = map.edges[p.edge];
                double lat = 35.18456015184093 + p.at.y / 111132.0, lon = -80.81770185962013 + p.at.x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0));
                sb.AppendLine(string.Format(ci, "  n{0} e{1} '{2}' cls {3} ({4:0},{5:0}) {6:0.00000},{7:0.00000} w {8:0.00} storage {9:0} taper {10:0} trim {11:0.0} median {12:0.0} run {13:0} twin e{14} edges {15}",
                    p.node, p.edge, e.name, e.cls, p.at.x, p.at.y, lat, lon, p.w, p.storage, p.taper, p.trim, p.gapMin, p.run, p.twin, string.Join("+", p.chain.Select(c => "e" + c.edge))));
            }
            // PSX_DESCRIBE=edge:s:side;... - what stands on a side of an edge there, as its tile decides it
            string desc = System.Environment.GetEnvironmentVariable("PSX_DESCRIBE");
            if (!string.IsNullOrWhiteSpace(desc))
            {
                var trims = CityMeshes.ComputeTrims(map);
                foreach (var item in desc.Split(';'))
                {
                    var f = item.Split(':');
                    if (f.Length < 3) continue;
                    var e = map.edges[int.Parse(f[0], ci)];
                    float s = float.Parse(f[1], ci); int side = int.Parse(f[2], ci);
                    string d;
                    try { d = CityMeshes.DescribeSideAt(map, trims, e, s, side); } catch (System.Exception ex) { d = "EXCEPTION " + ex.Message; }
                    sb.AppendLine($"DESCRIBE e{e.index} '{e.name}' s {s:0.0} side {side}: {d}");
                }
            }
            sb.AppendLine($"LEFT AS THEY WERE ({CityMeshes.TurnPocketLeftAt.Count}):");
            foreach (var l in CityMeshes.TurnPocketLeftAt) sb.AppendLine("  " + l);
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[TurnPocketProbe] " + CityMeshes.TurnPocketLine());
        }
    }
}
