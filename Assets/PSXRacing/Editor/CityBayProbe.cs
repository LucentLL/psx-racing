using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// c7roads round 3, R0 (owner 11-26 on spot 80: "What are these roads?"): the turn bays the map draws as
    /// their own diagonal roads. Loads the city graph and the trims (no scene, no frames), classifies every
    /// non-motorway link way, and traces the merge-zone decision of the edges named in PSX_ZONETRACE.
    /// Output: Screenshots/c7/c7_bays_{PSX_C7_TAG}.txt and .tsv.
    ///
    /// THE TEST (a bay, not a slip): a link way, not a motorway link, both of whose ends lie on edges of the
    /// same named road (one carriageway or its twin), under 250 m, leaving at 35 degrees or less, of whose
    /// stations (every 2 m) at least 60 % lie within one lane width plus a metre of that road's drawn pavement
    /// (its travelled way's extents). Otherwise a slip / ramp (it has land of its own beside its host).
    /// </summary>
    public static class CityBayProbe
    {
        public const float BayMaxLenM = 250f, BayMaxDeg = 35f, BayShare = 0.6f, BayBesideM = 3.66f + 1f;

        public static void Run()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_C7_TAG") ?? "run";
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "c7");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder($"C7 BAYS {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag}\n");
            var tsv = new StringBuilder("way\tedges\tcls\tlanes\tturn\tlen\tdeg\tshare\tsameName\tname\tclass\tx\tz\tzone\n");
            int code = 0;
            try
            {
                var trace = (System.Environment.GetEnvironmentVariable("PSX_ZONETRACE") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(int.Parse).ToList();
                CityMeshes.ZoneTraceEdges = new HashSet<int>(trace);
                var map = CityMap.Get();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.ComputeTrims(map);
                log.Append($"trims {clock.ElapsedMilliseconds} ms; zones {CityMeshes.Zones.Count}; GORESPLIT {(LineModel.GoreSplitOn ? 1 : 0)} pairs {LineModel.GorePairs} overlapping {LineModel.GoreOverlapBefore} still {LineModel.GoreOverlapAfter}\n");
                log.Append(CityMeshes.ZoneTrace.ToString());
                foreach (var z in CityMeshes.Zones)
                    if (trace.Contains(z.branch))
                        log.Append(string.Format(inv, "ZONE e{0} n{1} host e{2} style {3} k {4} side {5} D {6:0.0} wN {7:0.00} theta {8:0.0} merge {9}\n", z.branch, z.node, z.host, z.style, z.k, z.side, z.D, z.wN, z.theta, z.merge));
                var zoneBranches = new HashSet<int>(CityMeshes.Zones.Select(z => z.branch));
                // group link edges by way
                var ways = new Dictionary<uint, List<int>>();
                foreach (var e in map.edges) if (e.link && e.cls < 5 && e.a != e.b) { if (!ways.TryGetValue(e.wayId, out var l)) ways[e.wayId] = l = new List<int>(); l.Add(e.index); }
                var segs = new HashSet<int>();
                int nBay = 0, nSlip = 0, nBayLeft = 0;
                var counts = new SortedDictionary<string, int>();
                foreach (var kv in ways)
                {
                    var es = kv.Value;
                    float len = es.Sum(i => map.edges[i].length);
                    // the way's end nodes: nodes used once
                    var deg = new Dictionary<int, int>();
                    foreach (int i in es) { var e = map.edges[i]; deg[e.a] = deg.TryGetValue(e.a, out int c1) ? c1 + 1 : 1; deg[e.b] = deg.TryGetValue(e.b, out int c2) ? c2 + 1 : 1; }
                    var ends = deg.Where(p => p.Value == 1).Select(p => p.Key).ToList();
                    string nameA = null, nameB = null; float angA = 180f;
                    if (ends.Count == 2)
                    {
                        nameA = HostName(map, ends[0], es); nameB = HostName(map, ends[1], es);
                        angA = Mathf.Min(LeaveDeg(map, ends[0], es), LeaveDeg(map, ends[1], es));
                    }
                    bool same = nameA != null && nameA.Length > 0 && nameA == nameB;
                    // the share of stations beside that road's pavement
                    int nS = 0, nIn = 0;
                    if (same)
                        foreach (int i in es)
                        {
                            var e = map.edges[i];
                            for (float s = 1f; s < e.length - 1f; s += 2f)
                            {
                                var p = e.PointAt(s); nS++;
                                segs.Clear(); map.EdgeSegsInRect(p - Vector2.one * 25f, p + Vector2.one * 25f, segs);
                                float best = 1e9f;
                                foreach (int pk in segs)
                                {
                                    var h = map.edges[pk >> 12];
                                    if (h.link || h.name != nameA) continue;
                                    int k = pk & 0xFFF; var a = h.pts[k]; var b = h.pts[k + 1]; var ab = b - a;
                                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                                    float sh = h.s[k] + t * ab.magnitude;
                                    LineModel.Extents(h, sh, out float eM, out float eP);
                                    var q = a + ab * t; var tg = ab.normalized; var lf = new Vector2(-tg.y, tg.x);
                                    float lat = Vector2.Dot(p - q, lf);
                                    float outside = lat > eP ? lat - eP : lat < -eM ? -eM - lat : 0f;
                                    if (outside < best) best = outside;
                                }
                                if (best <= BayBesideM) nIn++;
                            }
                        }
                    float share = nS > 0 ? (float)nIn / nS : 0f;
                    var e0 = map.edges[es[0]];
                    bool bay = same && len < BayMaxLenM && angA <= BayMaxDeg && share >= BayShare;
                    string cls = bay ? "BAY" : "SLIP";
                    if (bay) { nBay++; if ((e0.lsTurnOnly & 1) != 0) nBayLeft++; } else nSlip++;
                    string why = bay ? "bay" : !same ? "ends on different roads" : len >= BayMaxLenM ? "long" : angA > BayMaxDeg ? "leaves steep" : "own land beside";
                    counts.TryGetValue(why, out int cc); counts[why] = cc + 1;
                    var mid = e0.PointAt(e0.length * 0.5f);
                    tsv.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3}\t0x{4:X}\t{5:0}\t{6:0}\t{7:0.00}\t{8}\t{9}\t{10}\t{11:0}\t{12:0}\t{13}\n", kv.Key, string.Join(",", es), e0.cls, e0.lanes, e0.lsTurnOnly, len, angA, share, same ? 1 : 0, nameA, cls, mid.x, mid.y, es.Any(zoneBranches.Contains) ? 1 : 0));
                }
                log.Append($"link ways (not motorway): {ways.Count}; BAY {nBay} (turnOnly left bit {nBayLeft}), SLIP {nSlip}\n");
                foreach (var kv in counts) log.Append($"  {kv.Key}: {kv.Value}\n");
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(dir, $"c7_bays_{tag}.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, $"c7_bays_{tag}.tsv"), tsv.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }

        static string HostName(CityMap map, int node, List<int> es)
        {
            foreach (int i in map.nodeEdges[node]) { var h = map.edges[i]; if (!es.Contains(i) && !h.link && !string.IsNullOrEmpty(h.name)) return h.name; }
            return "";
        }

        static float LeaveDeg(CityMap map, int node, List<int> es)
        {
            int li = es.First(i => map.edges[i].a == node || map.edges[i].b == node);
            var L = map.edges[li];
            Vector2 dL = L.PointAt(L.a == node ? Mathf.Min(10f, L.length) : Mathf.Max(0f, L.length - 10f)) - map.nodes[node];
            float best = 180f;
            foreach (int i in map.nodeEdges[node])
            {
                if (es.Contains(i)) continue;
                var h = map.edges[i];
                if (h.link) continue;
                Vector2 dH = h.PointAt(h.a == node ? Mathf.Min(10f, h.length) : Mathf.Max(0f, h.length - 10f)) - map.nodes[node];
                best = Mathf.Min(best, Vector2.Angle(dL, dH));
            }
            return best;
        }
    }
}
