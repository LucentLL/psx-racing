using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>Throwaway diagnostics for the tile builder: why is a bucket
    /// empty, where do stations sit vs the ground. Writes city_probe.txt.</summary>
    public static class CityDebugProbe
    {
        [MenuItem("PSX Racing/City Debug Probe")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { sb.AppendLine("no map"); Done(sb); return; }

            int tx = Mathf.FloorToInt(map.uptown.x / CityMeshes.TileSize);
            int tz = Mathf.FloorToInt(map.uptown.y / CityMeshes.TileSize);
            sb.AppendLine($"uptown {map.uptown}  tile {tx},{tz}  rect [{tx * CityMeshes.TileSize},{tz * CityMeshes.TileSize}]");

            var trims = CityMeshes.NodeTrims(map);
            var tm = CityMeshes.Build(map, trims, null, tx, tz);
            void Dump(string name, Mesh m, CityMeshes.Slot[] slots)
            {
                if (m == null) { sb.AppendLine($"{name}: NULL"); return; }
                sb.Append($"{name}: {m.vertexCount} verts, subs [");
                for (int i = 0; i < m.subMeshCount; i++)
                    sb.Append($"{slots[i]}={m.GetSubMesh(i).indexCount / 3}tri ");
                sb.AppendLine("]");
            }
            Dump("ground", tm.ground, new[] { CityMeshes.Slot.Ground });
            Dump("roads", tm.roads, tm.roadSlots);
            Dump("water", tm.water, new[] { CityMeshes.Slot.Water });
            Dump("buildings", tm.buildings, tm.buildingSlots);

            // how many edges touch this tile, and what do their trims/spans say
            var segs = new System.Collections.Generic.HashSet<int>();
            var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            var max = min + Vector2.one * CityMeshes.TileSize;
            map.EdgeSegsInRect(min - Vector2.one * 40f, max + Vector2.one * 40f, segs);
            var edges = new System.Collections.Generic.HashSet<int>();
            foreach (var p in segs) edges.Add(p >> 12);
            sb.AppendLine($"edges near tile: {edges.Count}");

            int shown = 0;
            foreach (var ei in edges)
            {
                if (shown++ >= 12) break;
                var e = map.edges[ei];
                float sMin = trims[e.a], sMax = e.length - trims[e.b];
                float midS = (sMin + sMax) * 0.5f;
                var p = e.PointAt(midS);
                float roadY = e.YAt(midS);
                float gy = CityElevation.GroundY(map, p.x, p.y);
                float by = CityElevation.BaseY(p.x, p.y);
                sb.AppendLine($"  e{ei} '{e.name}' cls{e.cls} len {e.length:0.0} trims {sMin:0.0}/{e.length - sMax:0.0} " +
                    $"stations {e.stS.Length} elevMid {e.ElevatedAt(midS)} roadY {roadY:0.00} groundY {gy:0.00} baseY {by:0.00} " +
                    $"mid ({p.x:0},{p.y:0}) inTile {(p.x >= min.x && p.x < max.x && p.y >= min.y && p.y < max.y)}");
            }

            // the roads mesh itself: where do its vertices actually sit
            if (tm.roads != null)
            {
                var v = tm.roads.vertices;
                var b = tm.roads.bounds;
                sb.AppendLine($"roads bounds c={b.center} e={b.extents}");
                for (int si = 0; si < tm.roads.subMeshCount; si++)
                {
                    var sub = tm.roads.GetSubMesh(si);
                    float mnY = float.MaxValue, mxY = float.MinValue;
                    var tris = tm.roads.GetTriangles(si);
                    foreach (var t in tris)
                    {
                        mnY = Mathf.Min(mnY, v[t].y);
                        mxY = Mathf.Max(mxY, v[t].y);
                    }
                    sb.AppendLine($"  sub {tm.roadSlots[si]}: tris {tris.Length / 3}, y [{mnY:0.00}..{mxY:0.00}], v0 {(tris.Length > 0 ? v[tris[0]].ToString() : "-")}");
                }
            }
            // and one specific in-tile edge, station by station
            foreach (var ei in edges)
            {
                var e = map.edges[ei];
                float mS = (trims[e.a] + e.length - trims[e.b]) * 0.5f;
                var mp = e.PointAt(mS);
                if (!(mp.x >= min.x && mp.x < max.x && mp.y >= min.y && mp.y < max.y)) continue;
                sb.AppendLine($"stations of e{ei} '{e.name}':");
                for (int i = 0; i < e.stS.Length; i++)
                    sb.AppendLine($"    s={e.stS[i]:0.0} y={e.stY[i]:0.00} elev={e.stElev[i]}");
                break;
            }

            // the South Tryon z4 window cluster the audit flags
            foreach (var ei in new[] { 5284, 5285, 5286 })
            {
                if (ei >= map.edges.Length) continue;
                var e = map.edges[ei];
                sb.AppendLine($"e{ei} '{e.name}' z{e.z} len {e.length:0.0} nodes {e.a}(y={map.nodeY[e.a]:0.00} deg{map.nodeEdges[e.a].Count})" +
                              $" -> {e.b}(y={map.nodeY[e.b]:0.00} deg{map.nodeEdges[e.b].Count})");
                var line = new StringBuilder("   y: ");
                for (int i = 0; i < e.stY.Length; i++) line.Append($"{e.stY[i]:0.0} ");
                sb.AppendLine(line.ToString());
                foreach (var n in new[] { e.a, e.b })
                    foreach (var oi in map.nodeEdges[n])
                        if (oi != ei)
                        {
                            var o = map.edges[oi];
                            float endY = o.a == n ? o.stY[0] : o.stY[o.stY.Length - 1];
                            sb.AppendLine($"     nbr@{n}: e{oi} '{o.name}' z{o.z} len {o.length:0} endY {endY:0.00}");
                        }
            }

            // count elevated stations across the whole map
            long elev = 0, total = 0;
            foreach (var e in map.edges)
            {
                total += e.stElev.Length;
                foreach (var b in e.stElev) if (b) elev++;
            }
            sb.AppendLine($"elevated stations: {elev}/{total} ({100f * elev / total:0.0}%)");
            Done(sb);
        }

        static void Done(StringBuilder sb)
        {
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_probe.txt"), sb.ToString());
            Debug.Log("[CityDebugProbe]\n" + sb);
        }
    }
}
