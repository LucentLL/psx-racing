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
    /// grounded, non-ramp edge of the three race routes, of the named streets
    /// below and of the freeways, every 10 m, both sides: the graded ground
    /// (CityElevation.GroundY) against the road at 2, 5, 10, 15, 20 and 30 m
    /// past the pavement's edge, and against the natural land (the DEM there):
    /// how much of the difference the grading kept.
    ///
    /// And THE BANKS the grading made: the ground every metre out to 48 m past
    /// the edge, its slope over each 4 m (half a lattice cell), against the
    /// DEM's over the same 4 m. A FACE is a stretch steeper than 1V:2H
    /// (RoadsideRules.CityBankSlope) that the grading made - steeper than the
    /// natural land there by 1V:20H or more. Two kinds: where this road's own
    /// section decides the ground (its fill or its cut, alone), and where two
    /// roads' grading meets (another road's floor, cap or deck sets it - the
    /// rail warrant's business). Printed with the worst spots.
    ///
    /// Reads the pavement edge through CityMap.Edge.PaveEdgeM (the accessor
    /// R4 replaces); measuring an older build needs only that accessor added.
    /// Output: city_land_probe.txt in the project root.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.CityLandProbe.Run
    /// </summary>
    public static class CityLandProbe
    {
        static readonly float[] Past = { 2f, 5f, 10f, 15f, 20f, 30f };
        static readonly string[] Streets = { "Queens Road", "Central Avenue", "Providence Road", "Beatties Ford", "Archdale", "Rozzelles", "State Street", "Trade" };
        static readonly string[] Freeways = { "I-77", "I-277", "I-485", "I-85", "John Belk", "Brookshire" };
        const int BankOutM = 48, BankWinM = 4;
        const float FaceSlope = 0.5f, FaceOverDem = 0.05f;

        [MenuItem("PSX Racing/City Land Probe")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityLandProbe] no city data"); return; }
            var sb = new StringBuilder();
            sb.AppendLine("CITY LAND PROBE: the graded ground beside the roads, against the road and against the natural land (DEM), every 10 m, both sides, grounded non-ramp edges");
            sb.AppendLine("  d = metres past the pavement's edge; |land-road| p50/p90; kept = (land - road) / (DEM - road), the share of the natural rise or fall the grading leaves, p50 where the DEM is 1 m or more off the road; flat = the land within 0.25 m of the road");
            sb.AppendLine($"  faces = {BankWinM} m stretches, 0-{BankOutM} m past the edge, steeper than 1V:{1f / FaceSlope:0}H and than the DEM there by {FaceOverDem:0.00}: 'section' where this road's own fill or cut sets the ground, 'two roads' where another road's floor, cap or deck does");
            var routeEdges = new HashSet<int>();
            if (map.routes != null) foreach (var r in map.routes) foreach (int ei in r.edges) routeEdges.Add(ei);
            Measure(map, "the three race routes", routeEdges, sb);
            Measure(map, "the named streets (" + string.Join(", ", Streets) + ")", Named(map, Streets), sb);
            Measure(map, "the freeways (" + string.Join(", ", Freeways) + ")", Named(map, Freeways), sb);
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_land_probe.txt"), sb.ToString());
            Debug.Log("[CityLandProbe]\n" + sb);
        }

        static HashSet<int> Named(CityMap map, string[] names)
        {
            var set = new HashSet<int>();
            for (int i = 0; i < map.edges.Length; i++)
            {
                var e = map.edges[i];
                if (e.link || string.IsNullOrEmpty(e.name)) continue;
                foreach (var n in names) if (e.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) { set.Add(i); break; }
            }
            return set;
        }

        static void Measure(CityMap map, string label, HashSet<int> edges, StringBuilder sb)
        {
            int nd = Past.Length;
            var diff = new List<float>[nd]; var kept = new List<float>[nd]; var flat = new int[nd]; var n = new int[nd];
            for (int k = 0; k < nd; k++) { diff[k] = new List<float>(); kept[k] = new List<float>(); }
            float km = 0f;
            int sides = 0, faceOwn = 0, faceTwo = 0, faceOwnM = 0, faceTwoM = 0;
            // the steepest face per (edge, 100 m of it, side)
            var worst = new Dictionary<long, (float slope, string where)>();
            int nb = BankOutM + 1;
            var gy = new float[nb]; var dy = new float[nb]; var own = new bool[nb];
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                if (e.link) continue;
                for (float s = 5f; s < e.length - 5f; s += 10f)
                {
                    if (e.ElevatedAt(s)) continue;
                    km += 0.01f;
                    var c = e.PointAt(s); var t = e.TangentAt(s); var nr = new Vector2(-t.y, t.x);
                    float y = e.YAt(s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float hw = e.PaveEdgeM(s, e.SideAt(s, c + nr * side * 5f));
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
                        // the banks: every metre out, slopes over 4 m
                        sides++;
                        for (int j = 0; j < nb; j++)
                        {
                            var q = c + nr * side * (hw + j);
                            gy[j] = CityElevation.Ground(map, q.x, q.y, out var gt);
                            dy[j] = CityElevation.BaseY(q.x, q.y);
                            // this road's own section sets it: not another road's
                            // floor, cap or deck
                            bool otherFloor = gt.floorEdge >= 0 && gt.floorEdge != ei && !float.IsNaN(gt.floor) && gt.result <= gt.floor + 1e-3f && gt.floor > gt.dem + 1e-3f;
                            bool otherCap = gt.protectEdge >= 0 && gt.protectEdge != ei && !float.IsNaN(gt.protect) && gt.result >= gt.protect - 1e-3f;
                            bool deck = !float.IsNaN(gt.deckProtect) && gt.result >= gt.deckProtect - 1e-3f || !float.IsNaN(gt.deckCap) && gt.result >= gt.deckCap - 1e-3f;
                            own[j] = !otherFloor && !otherCap && !deck;
                        }
                        bool inOwn = false, inTwo = false;
                        for (int j = 0; j + BankWinM < nb; j++)
                        {
                            float gs = Mathf.Abs(gy[j + BankWinM] - gy[j]) / BankWinM, ds = Mathf.Abs(dy[j + BankWinM] - dy[j]) / BankWinM;
                            bool graded = Mathf.Abs(gy[j] - dy[j]) > 0.05f || Mathf.Abs(gy[j + BankWinM] - dy[j + BankWinM]) > 0.05f;
                            if (!(gs > FaceSlope && gs > ds + FaceOverDem && graded)) { continue; }
                            bool mine = own[j] && own[j + BankWinM];
                            if (mine) { faceOwnM++; if (!inOwn) { faceOwn++; inOwn = true; } }
                            else { faceTwoM++; if (!inTwo) { faceTwo++; inTwo = true; } }
                            long key = ((long)ei << 24) | ((long)Mathf.FloorToInt(s / 100f) << 2) | (side < 0 ? 0L : 1L) | (mine ? 2L : 0L);
                            if (!worst.TryGetValue(key, out var have) || gs > have.slope)
                            {
                                var q = c + nr * side * (hw + j);
                                worst[key] = (gs, $"1V:{1f / gs:0.0}H ({(mine ? "section" : "two roads")}) {j}-{j + BankWinM} m past the edge of e{ei} '{e.name}' s={s:0} side {(side < 0 ? "L" : "R")} at ({q.x:0},{q.y:0}): ground {gy[j] - y:+0.0;-0.0} -> {gy[j + BankWinM] - y:+0.0;-0.0} m, DEM {dy[j] - y:+0.0;-0.0} -> {dy[j + BankWinM] - y:+0.0;-0.0} m on the road");
                            }
                        }
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
            sb.AppendLine($"  FACES steeper than 1V:2H the grading made, 0-{BankOutM} m out: section {faceOwn} of {sides} station-sides ({100f * faceOwn / Mathf.Max(1, sides):0.00}%, {faceOwnM} m of window); two roads {faceTwo} ({100f * faceTwo / Mathf.Max(1, sides):0.00}%, {faceTwoM} m)");
            // the worst of each kind, one per 100 m of road and side
            foreach (string kind in new[] { "(section)", "(two roads)" })
            {
                var list = new List<(float slope, string where)>();
                foreach (var v in worst.Values) if (v.where.Contains(kind)) list.Add(v);
                list.Sort((a, b) => b.slope.CompareTo(a.slope));
                for (int i = 0; i < list.Count && i < 6; i++) sb.AppendLine("    " + list[i].where);
            }
        }
    }
}
