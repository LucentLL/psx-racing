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
    /// c7roads round 4, RULE 6 (owner 11-26: "Why is everything angular when the real roads have smooth curves
    /// only?"): THE KINK COUNT. Walks every junction fan's drawn outline (its cluster's ring, the free kerb edges
    /// only: never a mouth to a ribbon) city-wide, and every ribbon's drawn outline (both sides) within SpotR of the
    /// owner's spots, and counts KINKS: vertices that are not on a curve by rule 4 - the drawn pieces more than 1 cm
    /// off the curve through them (sagitta estimate L x turn / 8, L the shorter neighbouring piece) or, where that
    /// curve's radius is 6 m or more, a turn of more than 6 degrees at the joint.
    /// Classes: "fan: return facet" (a curb return's own arc points), "fan: corner, no return" (a fan corner that is
    /// no arc: CurbRadiusAt gave 0 - links meeting, a skew crossing, one road going on, a clipped arm - or the return
    /// did not fit), "ribbon outline" (a ribbon's own side, near the spots: a bay's taper, a slip's bend, a cut end).
    /// Not walked: the curbed medians' and islands' own outlines, lots, walks (their pieces follow these).
    /// Output: Screenshots/c7/c7_kinks_{PSX_C7_TAG}.txt (+ .tsv of every kink near the spots).
    /// </summary>
    public static class CityKinkProbe
    {
        public const float SagMaxM = 0.01f, TurnMaxDeg = 6f, RBigM = 6f, SpotR = 60f, MinPieceM = 0.05f;
        static readonly (string id, Vector2 p)[] Spots =
        {
            ("77", new Vector2(5587.78f, 11806.86f)), ("78", new Vector2(5564.06f, 11839.02f)), ("79", new Vector2(6246.80f, 13380.12f)),
            ("80", new Vector2(6355.66f, 13212.55f)), ("81", new Vector2(6600.40f, 12318.50f)),
        };

        static bool Kink(Vector2 a, Vector2 b, Vector2 c, out float turnDeg)
        {
            var u = b - a; var v = c - b;
            float lu = u.magnitude, lv = v.magnitude;
            turnDeg = 0f;
            if (lu < MinPieceM || lv < MinPieceM) return false;
            float th = Vector2.Angle(u, v) * Mathf.Deg2Rad;
            turnDeg = th * Mathf.Rad2Deg;
            if (th < 1e-4f) return false;
            float L = Mathf.Min(lu, lv);
            float sag = L * th / 8f, r = L / th;
            return sag > SagMaxM || (r >= RBigM && turnDeg > TurnMaxDeg);
        }

        static string NearSpot(Vector2 p) { foreach (var s in Spots) if ((s.p - p).magnitude < SpotR) return s.id; return null; }

        public static void Run()
        {
            var inv = CultureInfo.InvariantCulture;
            string tag = System.Environment.GetEnvironmentVariable("PSX_C7_TAG") ?? "run";
            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Screenshots", "c7");
            Directory.CreateDirectory(dir);
            var log = new StringBuilder($"C7 KINKS {System.DateTime.Now:yyyy-MM-dd HH:mm} tag {tag} (sagitta over {SagMaxM * 100f:0} cm, or over {TurnMaxDeg:0} deg where the radius is {RBigM:0} m or more)\n");
            var tsv = new StringBuilder("spot\tclass\tx\tz\tturnDeg\tnode\tedge\n");
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.ComputeTrims(map);
                log.Append($"trims {clock.ElapsedMilliseconds} ms; SMOOTHCURB {(CityMeshes.SmoothCurbOn ? 1 : 0)}, BAYLANES {(CityMeshes.BayLanesOn ? 1 : 0)}\n");
                var city = new SortedDictionary<string, int>();
                var spot = new SortedDictionary<string, int>();
                void Count(string cls, string sp, Vector2 p, float deg, int node, int edge)
                {
                    city.TryGetValue(cls, out int c); city[cls] = c + 1;
                    if (sp == null) return;
                    string k = sp + " | " + cls; spot.TryGetValue(k, out int c2); spot[k] = c2 + 1;
                    tsv.Append(string.Format(inv, "{0}\t{1}\t{2:0.00}\t{3:0.00}\t{4:0.0}\t{5}\t{6}\n", sp, cls, p.x, p.y, deg, node, edge));
                }
                var ring = new List<(Vector2 p, bool mouthNext, bool arc, int edge)>(64);
                var seen = new HashSet<int>();
                int rings = 0, verts = 0;
                for (int n = 0; n < map.nodes.Length; n++)
                {
                    if (!trims.patch[n]) continue;
                    int key = CityMeshes.FanRingCorners(map, trims, n, ring);
                    if (key < 0 || !seen.Add(key) || ring.Count < 3) continue;
                    rings++;
                    int m = ring.Count;
                    for (int i = 0; i < m; i++)
                    {
                        var prev = ring[(i - 1 + m) % m]; var cur = ring[i]; var next = ring[(i + 1) % m];
                        // only a vertex between two free (kerb) pieces: a mouth is where a ribbon carries on
                        if (prev.mouthNext || cur.mouthNext) continue;
                        verts++;
                        if (!Kink(prev.p, cur.p, next.p, out float deg)) continue;
                        Count(cur.arc ? "fan: return facet" : "fan: corner, no return", NearSpot(cur.p), cur.p, deg, n, cur.edge);
                    }
                }
                log.Append($"fan rings {rings}, free vertices {verts}\n");
                int ribs = 0, ribFail = 0;
                var segs = new HashSet<int>();
                foreach (var s in Spots)
                {
                    segs.Clear();
                    map.EdgeSegsInRect(s.p - Vector2.one * SpotR, s.p + Vector2.one * SpotR, segs);
                    foreach (int ei in segs.Select(pk => pk >> 12).Distinct().ToList())
                    {
                        Vector2[] L, R;
                        try { if (!CityMeshes.RibbonOutline(map, trims, ei, out L, out R)) continue; }
                        catch (System.Exception) { ribFail++; continue; }
                        ribs++;
                        foreach (var side in new[] { L, R })
                            for (int i = 1; i + 1 < side.Length; i++)
                            {
                                if ((side[i] - s.p).magnitude > SpotR) continue;
                                if (Kink(side[i - 1], side[i], side[i + 1], out float deg)) Count("ribbon outline", s.id, side[i], deg, -1, ei);
                            }
                    }
                }
                log.Append($"ribbons read near the spots {ribs} (failed {ribFail})\n");
                log.Append("CITY-WIDE (fan rings; ribbons only near the spots):\n");
                foreach (var kv in city) log.Append($"  {kv.Key}: {kv.Value}\n");
                log.Append($"WITHIN {SpotR:0} m OF THE SPOTS:\n");
                foreach (var kv in spot) log.Append($"  {kv.Key}: {kv.Value}\n");
            }
            catch (System.Exception ex) { log.Append("FAILED: " + ex + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(dir, $"c7_kinks_{tag}.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, $"c7_kinks_{tag}.tsv"), tsv.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
        }
    }
}
