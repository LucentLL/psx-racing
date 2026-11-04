using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Five Points round 5, item 4: WHERE A LOCAL DATA CHANGE REACHES FAR
    /// ROADS. Loads the city (CityMap.Get) with a hook on every traced step of
    /// the elevation solve (CityElevation.SolveHook) and writes, per step, a
    /// hash of each edge's station heights and each node's height; then the
    /// final station / drawn profiles, the junction trims and the lane line
    /// offsets. Two runs (two byte sets, the same code) compared by
    /// tools/elevdiff.mjs give the first step at which a road far from the
    /// changed edges moves. Writes PSX_ELEVDIFF_OUT (default
    /// &lt;project&gt;/elevdiff.txt).
    /// </summary>
    public static class ElevDiffProbe
    {
        static uint H(float[] a)
        {
            if (a == null) return 0u;
            uint h = 2166136261u;
            foreach (var f in a) { uint b = (uint)System.BitConverter.SingleToInt32Bits(f); for (int k = 0; k < 4; k++) { h ^= (b >> (8 * k)) & 0xFF; h *= 16777619u; } }
            return h;
        }

        public static void Run()
        {
            var outPath = System.Environment.GetEnvironmentVariable("PSX_ELEVDIFF_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(Directory.GetCurrentDirectory(), "elevdiff.txt");
            var sw = new StreamWriter(outPath, false, new UTF8Encoding(false));
            int step = 0;
            CityElevation.SolveHook = (map, where) =>
            {
                if (map.edges[0].stY == null) return;
                var sb = new StringBuilder();
                sb.Append("STEP ").Append(step++).Append(' ').Append(where.Replace(' ', '_')).Append("\nE");
                foreach (var e in map.edges) sb.Append(' ').Append(H(e.stY).ToString("x8"));
                sb.Append("\nN");
                if (map.nodeY != null) foreach (var y in map.nodeY) sb.Append(' ').Append(((uint)System.BitConverter.SingleToInt32Bits(y)).ToString("x8"));
                sw.WriteLine(sb.ToString());
            };
            try
            {
                var m = CityMap.Get();
                CityElevation.SolveHook = null;
                var sb = new StringBuilder();
                sb.Append("FINAL\nE");
                foreach (var e in m.edges) sb.Append(' ').Append(H(e.stY).ToString("x8"));
                sb.Append("\nV");
                foreach (var e in m.edges) sb.Append(' ').Append(H(e.vY).ToString("x8"));
                sb.Append("\nN");
                foreach (var y in m.nodeY) sb.Append(' ').Append(((uint)System.BitConverter.SingleToInt32Bits(y)).ToString("x8"));
                // the drawn height at each edge's ends, quarters and middle (mm), for sizes
                sb.Append("\nY");
                foreach (var e in m.edges)
                {
                    sb.Append(' ');
                    if (e.stY == null || e.stY.Length < 2) { sb.Append('-'); continue; }
                    for (int q = 0; q <= 4; q++) sb.Append(q > 0 ? "," : "").Append(Mathf.RoundToInt(e.YAt(0.25f * q * e.length) * 1000f));
                }
                // the lanes' centre line, at the edge's ends and middle
                sb.Append("\nL");
                foreach (var e in m.edges)
                {
                    var p0 = LineModel.LanePoint(e, 0f); var p1 = LineModel.LanePoint(e, 0.5f * e.length); var p2 = LineModel.LanePoint(e, e.length);
                    sb.Append(' ').Append(H(new[] { p0.x, p0.y, p1.x, p1.y, p2.x, p2.y }).ToString("x8"));
                }
                var trims = CityMeshes.ComputeTrims(m);
                sb.Append("\nT");
                for (int i = 0; i < m.edges.Length; i++) sb.Append(' ').Append(H(new[] { trims.atA[i], trims.atB[i] }).ToString("x8"));
                // where each edge is (its midpoint), for distances
                sb.Append("\nP");
                foreach (var e in m.edges)
                {
                    var c = 0.5f * (m.nodes[e.a] + m.nodes[e.b]);
                    sb.Append(' ').Append(c.x.ToString("0")).Append(',').Append(c.y.ToString("0"));
                }
                sw.WriteLine(sb.ToString());
                // the drawn profile's report and every edge end it left off its node
                sw.WriteLine("REPORT " + CityElevation.VSmoothReport);
                foreach (var o in CityElevation.VsEndsOff)
                {
                    var p = m.nodes[o.node]; var e = m.edges[o.edge];
                    sw.WriteLine($"ENDOFF n{o.node} ({p.x:0.0},{p.y:0.0}) e{o.edge} '{e.name}' end {o.end} off {o.off * 100f:+0.0;-0.0} cm chain {o.chain} pos {o.pos} prio {o.prio:0.0}");
                }
                // PSX_ELEVDIFF_NODES="3917,7585": each arm's end there against the node
                foreach (var t in (System.Environment.GetEnvironmentVariable("PSX_ELEVDIFF_NODES") ?? "").Split(','))
                {
                    if (!int.TryParse(t.Trim().TrimStart('n'), out int n) || n < 0 || n >= m.nodes.Length) continue;
                    sw.WriteLine($"NODE n{n} ({m.nodes[n].x:0.0},{m.nodes[n].y:0.0}) y {m.nodeY[n]:0.000}");
                    foreach (int ei in m.nodeEdges[n])
                    {
                        var e = m.edges[ei]; if (e.stY == null || e.stY.Length < 2) continue;
                        float s = e.a == n ? 0f : e.length;
                        sw.WriteLine($"  e{ei} '{e.name}' cls {e.cls} w {e.width:0.0} link {e.link} end {(e.a == n ? "a" : "b")} drawn {e.YAt(s):0.000} station {e.StationYAt(s):0.000} vS {(e.vS == null ? "none" : e.vS.Length.ToString())}");
                    }
                }
                sw.WriteLine("DONE");
            }
            finally { CityElevation.SolveHook = null; sw.Close(); }
            Debug.Log("[ELEVDIFF] wrote " + outPath);
        }

        /// <summary>FNV-1a 64 of charlotte_city.bytes (as CityAudit.CityHash).</summary>
        public static string CityHash()
        {
            var ta = CharlotteData.Load("charlotte_city");
            if (ta == null) return "missing";
            ulong h = 14695981039346656037UL;
            foreach (byte b in ta.bytes) { h ^= b; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        static List<long> ReadOrderDump(string path)
        {
            var keys = new List<long>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                int sp = line.IndexOf(' ');
                keys.Add(long.Parse(sp < 0 ? line : line.Substring(0, sp), System.Globalization.CultureInfo.InvariantCulture));
            }
            return keys;
        }

        /// <summary>
        /// THE DRAWN PROFILE'S ORDER TABLE (Five Points round 6). Input:
        /// PSX_VSORDER_FROM, an order dump (PSX_CITY_VS_ORDER_DUMP) of the sort
        /// to keep - live's: live's bytes loaded with PSX_CITY_VS_ORDER=0, the
        /// unstable sort live draws with. This job must itself run with
        /// PSX_CITY_VS_ORDER_DUMP set (its own chains' keys, read back here).
        /// Writes &lt;project&gt;/vs_order_list.generated.cs for THIS export: the
        /// FROM keys, all of them in FROM's order (a changed chain takes the rank
        /// of the gone chain whose lowest edge it holds, so the gone keys stay)
        /// (copy it over Scripts/City/CityElevation.VsOrderList.cs), and
        /// vs_order_bake.txt: how many of this export's chains it places and
        /// which it does not (they draw after live's of their priority).
        /// PSX_VSORDER_FROM_NOTE: a provenance line for the file's header.
        /// After a city regeneration the table no longer fits (the medians gate
        /// fails): rebake from a dump of the new export's own sort.
        /// </summary>
        public static void BakeVsOrder()
        {
            string from = System.Environment.GetEnvironmentVariable("PSX_VSORDER_FROM");
            string cur = System.Environment.GetEnvironmentVariable("PSX_CITY_VS_ORDER_DUMP");
            string note = System.Environment.GetEnvironmentVariable("PSX_VSORDER_FROM_NOTE") ?? "";
            string root = Directory.GetParent(Application.dataPath).FullName;
            var rep = new StringBuilder();
            if (string.IsNullOrEmpty(from) || !File.Exists(from) || string.IsNullOrEmpty(cur)) { Debug.LogError("[VSORDER] needs PSX_VSORDER_FROM (a dump) and PSX_CITY_VS_ORDER_DUMP"); return; }
            if (File.Exists(cur)) File.Delete(cur);
            var map = CityMap.Get();
            if (!File.Exists(cur)) { Debug.LogError("[VSORDER] the solve wrote no order dump (PSX_CITY_VS_ORDER_DUMP)"); return; }
            var live = ReadOrderDump(from);
            var mine = ReadOrderDump(cur);
            var mineSet = new HashSet<long>(mine);
            var liveSet = new HashSet<long>(live);
            var keep = new List<long>(live.Count);
            foreach (long k in live) if (mineSet.Contains(k)) keep.Add(k);
            string hash = CityHash();
            rep.AppendLine($"order bake for city {hash} ({map.edges.Length} edges, {map.nodes.Length} nodes): FROM {from} ({live.Count} chains) {note}");
            rep.AppendLine($"  this export: {mine.Count} chains; placed by the table {keep.Count}; not in it {mine.Count - keep.Count}; FROM chains gone {live.Count - keep.Count}");
            foreach (long k in mine)
                if (!liveSet.Contains(k))
                {
                    int e0 = (int)(k >> 32); var e = map.edges[e0]; var c = 0.5f * (map.nodes[e.a] + map.nodes[e.b]);
                    rep.AppendLine($"  NEW chain: lowest edge e{e0} '{e.name}' at ({c.x:0},{c.y:0}) key {k}");
                }
            foreach (long k in live)
                if (!mineSet.Contains(k))
                {
                    int e0 = (int)(k >> 32);
                    var e = e0 < map.edges.Length ? map.edges[e0] : null;
                    rep.AppendLine(e == null ? $"  GONE chain key {k}" : $"  GONE chain: lowest edge e{e0} '{e.name}' at ({0.5f * (map.nodes[e.a].x + map.nodes[e.b].x):0},{0.5f * (map.nodes[e.a].y + map.nodes[e.b].y):0}) key {k}");
                }
            var sb = new StringBuilder();
            sb.Append("// GENERATED by PSXRacing.EditorTools.ElevDiffProbe.BakeVsOrder - do not edit by hand.\r\n");
            sb.Append("// The drawn profile's chain order (CityElevation.VSmooth.cs, VsOrderMode): live's\r\n");
            sb.Append($"// sorted list ({live.Count} chains); {keep.Count} of this export's {mine.Count} chains are live's, the rest take the rank of the chain they replace. {note.Replace("\r", " ").Replace("\n", " ")}\r\n");
            sb.Append("namespace PSXRacing.City\r\n{\r\n    public static partial class CityElevation\r\n    {\r\n");
            sb.Append($"        const int VsOrderEdges = {map.edges.Length}, VsOrderNodes = {map.nodes.Length};\r\n");
            sb.Append("        /// <summary>FNV-1a 64 of charlotte_city.bytes the table was baked for (the medians gate checks it).</summary>\r\n");
            sb.Append($"        public const string VsOrderCity = \"{hash}\";\r\n");
            sb.Append("        /// <summary>Chain keys (VsChainKey) in live's sorted order: the index is the rank.</summary>\r\n");
            sb.Append("        static readonly long[] VsOrderData = {\r\n");
            for (int i = 0; i < live.Count; i++)
            {
                if (i % 8 == 0) sb.Append("            ");
                sb.Append(live[i]).Append("L,");
                sb.Append(i % 8 == 7 || i == live.Count - 1 ? "\r\n" : " ");
            }
            sb.Append("        };\r\n    }\r\n}\r\n");
            File.WriteAllText(Path.Combine(root, "vs_order_list.generated.cs"), sb.ToString());
            File.WriteAllText(Path.Combine(root, "vs_order_bake.txt"), rep.ToString());
            Debug.Log("[VSORDER] " + rep.ToString().Split('\n')[1]);
        }
    }
}
