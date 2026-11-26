using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE JUNCTION TURNS OF A RACE LINE AND THE MAP ROUND THEM, as text (cityai round 2): the course line's turns of
    /// 30 degrees or more (deflection over +-24 m, local peaks), and round each the map's edges (line, class, lanes, the
    /// pavement either side at every point) and the fan kerbs of every node within 70 m. Runtime API only, so the
    /// perf tour writes it from a built exe (PerfTour.FieldTrace) and the editor's turn trace from the play check.
    /// </summary>
    public static class TurnMapDump
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("0.###", CI);

        public static void Turns(TrackPath p, List<Vector3> centres, List<float> degs)
        {
            centres.Clear(); degs.Clear();
            if (p == null || p.Count < 8) return;
            int n = p.Count, win = Mathf.Max(2, Mathf.RoundToInt(24f / p.spacing));
            var defl = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = p.GetPoint(i) - p.GetPoint(i - win), b = p.GetPoint(i + win) - p.GetPoint(i);
                a.y = 0f; b.y = 0f;
                defl[i] = a.sqrMagnitude > 1e-4f && b.sqrMagnitude > 1e-4f ? Vector3.Angle(a, b) : 0f;
            }
            for (int i = 0; i < n; i++)
            {
                if (defl[i] < 30f) continue;
                bool peak = true;
                for (int k = -win; k <= win && peak; k++) { int j = (i + k + n) % n; if (j != i && (defl[j] > defl[i] || (defl[j] == defl[i] && j < i))) peak = false; }
                if (peak) { centres.Add(p.GetPoint(i)); degs.Add(defl[i]); }
            }
        }

        public static string TurnsCsv(List<Vector3> c, List<float> d, float planM = 100f)
        {
            var sb = new StringBuilder("k,x,y,z,deg,planx0,planz0,planm\n");
            for (int k = 0; k < c.Count; k++)
                sb.Append(k).Append(',').Append(F(c[k].x)).Append(',').Append(F(c[k].y)).Append(',').Append(F(c[k].z)).Append(',').Append(F(d[k])).Append(',')
                  .Append(F(c[k].x - planM / 2)).Append(',').Append(F(c[k].z - planM / 2)).Append(',').Append(F(planM)).Append('\n');
            return sb.ToString();
        }

        public static void Map(List<Vector3> turns, out string edges, out string kerbs)
        {
            var esb = new StringBuilder("turn,edge,cls,link,oneway,lanes,width,i,x,z,padL,padR\n");
            var ksb = new StringBuilder("turn,node,x0,z0,x1,z1\n");
            var map = CityMap.Get();
            if (map != null)
            {
                var tr = CityWorld.TrimsFor(map);
                var segs = new List<Vector2>();
                for (int k = 0; k < turns.Count; k++)
                {
                    var c2 = new Vector2(turns[k].x, turns[k].z);
                    foreach (var e in map.edges)
                    {
                        if (e.pts == null) continue;
                        bool near = false;
                        foreach (var q in e.pts) if ((q - c2).sqrMagnitude < 150f * 150f) { near = true; break; }
                        if (!near) continue;
                        for (int i = 0; i < e.pts.Length; i++)
                            esb.Append(k).Append(',').Append(e.index).Append(',').Append(e.cls).Append(',').Append(e.link ? 1 : 0).Append(',').Append(e.oneway ? 1 : 0).Append(',')
                               .Append(e.lanes).Append(',').Append(F(e.width)).Append(',').Append(i).Append(',').Append(F(e.pts[i].x)).Append(',').Append(F(e.pts[i].y)).Append(',')
                               .Append(F(e.PaveEdgeM(e.s[i], -1))).Append(',').Append(F(e.PaveEdgeM(e.s[i], 1))).Append('\n');
                    }
                    for (int nd = 0; nd < map.nodes.Length; nd++)
                    {
                        if ((map.nodes[nd] - c2).sqrMagnitude > 70f * 70f) continue;
                        CityMeshes.FanKerbs(map, tr, nd, segs);
                        for (int i = 0; i + 1 < segs.Count; i += 2)
                            ksb.Append(k).Append(',').Append(nd).Append(',').Append(F(segs[i].x)).Append(',').Append(F(segs[i].y)).Append(',').Append(F(segs[i + 1].x)).Append(',').Append(F(segs[i + 1].y)).Append('\n');
                    }
                }
            }
            edges = esb.ToString(); kerbs = ksb.ToString();
        }
    }
}
