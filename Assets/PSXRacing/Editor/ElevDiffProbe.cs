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
                sw.WriteLine("DONE");
            }
            finally { CityElevation.SolveHook = null; sw.Close(); }
            Debug.Log("[ELEVDIFF] wrote " + outPath);
        }
    }
}
