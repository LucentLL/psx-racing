using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Road-surface round 2: THE MEDIAN ALONG THE RIBBONS NEAR A MEASURED JUNCTION. The junction's own median land (a
    /// measured nose: grass or concrete) ends at the rows; beyond them the rule's ground lies between the two
    /// carriageways in 8 m cells of whatever the cell is (a lot's concrete beside a grass median, grass beside a concrete
    /// one). So each carriageway that reaches a measured nose carries its half of the median on, from its row out to
    /// where its measure (or the opposite carriageway's) ends: the kerb on its measured left edge, the top of the nose's
    /// kind out to the middle between the two measured edges, an end face. The two halves meet in the middle; the
    /// junction's land and the ribbon's median are one median, of one kind, with no face seen from behind.
    /// </summary>
    public static partial class CityMeshes
    {
        public static int RsMedianTris; public static float RsMedianM;
        const float RsMedianLift = 0.015f;   // over a rule's top that may lie at the kerb's height in the same place
        static readonly Dictionary<int, Vector2[]> rsLeftBorder = new Dictionary<int, Vector2[]>();
        static object rsLeftBorderFor;

        /// <summary>The measured left edge of a fitted one-way (world points every 2 m where the measure is in force).</summary>
        static Vector2[] RsLeftBorder(CityMap map, JunctionMeasures.FitE f)
        {
            if (!ReferenceEquals(rsLeftBorderFor, JunctionMeasures.Current)) { rsLeftBorder.Clear(); rsLeftBorderFor = JunctionMeasures.Current; }
            if (rsLeftBorder.TryGetValue(f.edge, out var got)) return got;
            var e = map.edges[f.edge]; var pts = new List<Vector2>(64);
            for (float s = 0f; ; s += 2f)
            {
                float sc = Mathf.Min(s, e.length);
                if (f.Lerp(f.wgt, sc) >= 0.99f) { var t = e.TangentAt(sc); pts.Add(e.PointAt(sc) + new Vector2(-t.y, t.x) * f.Lerp(f.leftRaw, sc)); }
                if (s >= e.length) break;
            }
            return rsLeftBorder[f.edge] = pts.ToArray();
        }

        /// <summary>The gap from p leftward (along left) to the measured left edge of another fitted one-way: the
        /// opposite carriageway's median kerb.</summary>
        static bool RsOpposite(CityMap map, int self, Vector2 p, Vector2 left, float maxD, out float gap)
        {
            gap = maxD; bool any = false;
            var tan = new Vector2(left.y, -left.x);
            foreach (var g in JunctionMeasures.Fits.Values)
            {
                if (g.edge == self || !g.oneway) continue;
                var pl = RsLeftBorder(map, g);
                for (int i = 0; i + 1 < pl.Length; i++)
                {
                    var a = pl[i] - p; var b = pl[i + 1] - p;
                    if ((b - a).sqrMagnitude > 9f) continue;   // a break in the measured stretch
                    float va = Vector2.Dot(a, tan), vb = Vector2.Dot(b, tan);
                    if ((va > 0f) == (vb > 0f)) continue;
                    float u = Vector2.Dot(a + (b - a) * (va / (va - vb)), left);
                    if (u > 0.3f && u < gap) { gap = u; any = true; }
                }
            }
            return any;
        }

        static void EmitRsMedians(CityMap map, TileMeshes tm, JunctionSurfaces.Built js)
        {
            var set = JunctionMeasures.Current;
            if (set == null) return;
            var jm = set.For(js.nodes);
            if (jm == null) return;
            var o = tm.origin;
            foreach (var ns in jm.noses)
                for (int q = 0; q < 2; q++)
                {
                    int ei = q == 0 ? ns.a : ns.b;
                    var f = JunctionMeasures.FitOf(ei);
                    if (f == null || !f.oneway || !js.rowOf.TryGetValue(ei, out float row)) continue;
                    if (!set.edges.TryGetValue(ei, out var em) || em.kindL != 0) continue;
                    var e = map.edges[ei]; bool atA = js.nodes.Contains(e.a);
                    bool paved = ns.kind == 1;
                    var edge = new List<Vector3>(32); var back = new List<Vector3>(32); var mid = new List<Vector3>(32);
                    for (float d = row; d <= e.length + 1e-3f; d += 2f)
                    {
                        float s = atA ? d : e.length - d;
                        if (f.Lerp(f.wgt, s) < 0.99f) break;
                        var t = e.TangentAt(s); var left = new Vector2(-t.y, t.x);
                        var p = e.PointAt(s) + left * f.Lerp(f.leftRaw, s);
                        if (!RsOpposite(map, ei, p, left, 16f, out float gap) || 0.5f * gap < RsKerbW + 0.1f) break;
                        float y = e.YAt(s);
                        var b2 = p + left * RsKerbW; var m2 = p + left * (0.5f * gap + 0.03f);
                        edge.Add(new Vector3(p.x - o.x, y, p.y - o.z));
                        back.Add(new Vector3(b2.x - o.x, y + RsKerbH + RsMedianLift, b2.y - o.z));
                        mid.Add(new Vector3(m2.x - o.x, y + RsKerbH + RsMedianLift, m2.y - o.z));
                    }
                    int n = edge.Count;
                    if (n < 2) continue;
                    var gb = GroundBucket(paved);
                    float run = 0f;
                    for (int k = 0; k + 1 < n; k++)
                    {
                        var d2 = new Vector2(edge[k + 1].x - edge[k].x, edge[k + 1].z - edge[k].z); float len = d2.magnitude;
                        if (len < 1e-4f) continue;
                        d2 /= len;
                        // the pavement lies on the right of the edge's own direction along its left edge
                        var into = atA ? new Vector2(d2.y, -d2.x) : new Vector2(-d2.y, d2.x);
                        float u0 = run * 0.3f, u1 = (run + len) * 0.3f;
                        float y0 = edge[k].y, y1 = edge[k + 1].y, h = RsKerbH + RsMedianLift;
                        kerbBucket.WallSloped(new Vector3(edge[k].x, 0f, edge[k].z), new Vector3(edge[k + 1].x, 0f, edge[k + 1].z), y0, y0 + h, y1, y1 + h, into, u0, u1, 0f, 0.05f);
                        kerbBucket.Up(new Vector3(edge[k].x, y0 + h, edge[k].z), new Vector3(edge[k + 1].x, y1 + h, edge[k + 1].z), back[k + 1], back[k],
                                      new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.05f), new Vector2(u0, 0.05f));
                        gb.Up(back[k], back[k + 1], mid[k + 1], mid[k],
                              GroundUV(paved, back[k].x + o.x, back[k].z + o.z), GroundUV(paved, back[k + 1].x + o.x, back[k + 1].z + o.z),
                              GroundUV(paved, mid[k + 1].x + o.x, mid[k + 1].z + o.z), GroundUV(paved, mid[k].x + o.x, mid[k].z + o.z));
                        run += len; RsMedianTris += 6;
                    }
                    RsMedianM += run;
                    // the far end: a face down into the ground, turned away from the junction
                    {
                        var a = edge[n - 1]; var m = mid[n - 1];
                        var along = new Vector2(edge[n - 1].x - edge[n - 2].x, edge[n - 1].z - edge[n - 2].z).normalized;
                        kerbBucket.WallSloped(new Vector3(a.x, 0f, a.z), new Vector3(m.x, 0f, m.z), m.y - 0.6f, m.y, m.y - 0.6f, m.y, along, 0f, 0.3f * Vector3.Distance(a, m), 0f, 0.05f);
                        RsMedianTris += 2;
                    }
                }
        }
    }
}
