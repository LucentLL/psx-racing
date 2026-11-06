using System.Collections.Generic;
using System.Linq;
using System.Text;
using PSXRacing.City;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE GROUND BEHIND A WALK (curbs pass, 2026-11-05): the city-wide census
    /// of CityMeshes.WalkBack. Builds every tile a walk street (CityMeshes
    /// .WalkEdge) runs through, once with the backing off (the verge as live)
    /// and once on, and writes per pass the tile build time and the backing's
    /// counts, and per edge the land behind its walk (sections, trench
    /// sections where it dips more than PitM under the walk's back and comes
    /// back up, fall sections where it stays down, the deepest dip under the
    /// walk's back and under the lane edge). PSX_WALKBACK_MAX limits the tiles
    /// (a quick run). Writes city_walkback.txt and city_walkback_edges.csv in
    /// the project root.
    /// </summary>
    public static class CityWalkBackCount
    {
        [MenuItem("PSX Racing/Count The Ground Behind Walks")]
        public static void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var map = CityMap.Get();
            var trims = CityMeshes.ComputeTrims(map);
            var tiles = new SortedSet<long>();
            int walkEdges = 0;
            foreach (var e in map.edges)
            {
                if (e == null || e.pts == null || e.pts.Length < 2 || !CityMeshes.WalkEdge(map, e)) continue;
                walkEdges++;
                for (int i = 0; i + 1 < e.pts.Length; i++)
                {
                    var a = e.pts[i]; var b = e.pts[i + 1];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 16f));
                    for (int k = 0; k <= n; k++)
                    {
                        var p = Vector2.Lerp(a, b, (float)k / n);
                        tiles.Add(((long)Mathf.FloorToInt(p.x / CityMeshes.TileSize) << 32) | (uint)Mathf.FloorToInt(p.y / CityMeshes.TileSize));
                    }
                }
            }
            if (int.TryParse(System.Environment.GetEnvironmentVariable("PSX_WALKBACK_MAX"), out int max) && max > 0 && tiles.Count > max)
                { int stride = (tiles.Count + max - 1) / max; tiles = new SortedSet<long>(tiles.Where((t, i) => i % stride == 0)); }   // spread over the city
            float findS = (float)sw.Elapsed.TotalSeconds;
            var noBld = new Dictionary<long, List<CityBuildings.B>>();
            bool wasOn = CityMeshes.WalkBackOn;
            var sb = new StringBuilder();
            sb.AppendLine($"WALK BACKING CENSUS: {walkEdges} walk edges, {tiles.Count} tiles, find {findS:0.0} s");
            // warm the caches and the JIT on a few tiles first, so both passes are timed alike
            CityMeshes.WalkBackOn = false;
            foreach (long k in tiles.Take(8)) Destroy(CityMeshes.Build(map, trims, noBld, (int)(k >> 32), (int)(uint)k));
            Dictionary<int, (int n, int trench, int left, int fall, float dip, float laneDip, float fallDip, Vector2 at)> census = null;
            string censusLine = "";
            var passMs = new double[2]; var passN = new int[2];
            // OFF (with the census, untimed), ON, OFF, ON...: the build time in both orders. PSX_WALKBACK_CENSUS=0: no census, every pass timed; PSX_WALKBACK_PASSES: how many
            bool doCensus = System.Environment.GetEnvironmentVariable("PSX_WALKBACK_CENSUS") != "0";
            int passes = int.TryParse(System.Environment.GetEnvironmentVariable("PSX_WALKBACK_PASSES"), out int np) && np >= 2 ? np : 4;
            for (int pass = 0; pass < passes; pass++)
            {
                // (launch 7) PSX_WALKBACK_TOGGLE=wings: the passes toggle the curb cut wings (backing on)
                if (System.Environment.GetEnvironmentVariable("PSX_WALKBACK_TOGGLE") == "wings") { CityMeshes.WalkBackOn = true; CityMeshes.CurbWingsOn = (pass & 1) == 1; CityMeshes.CurbWingSpans = 0; }
                else CityMeshes.WalkBackOn = (pass & 1) == 1;
                CityMeshes.WalkBackCensus = doCensus && pass == 0;
                CityMeshes.WalkBackStats.Reset();
                CityMeshes.WalkStats.Reset();
                var t0 = System.Diagnostics.Stopwatch.StartNew();
                long gTris = 0;
                foreach (long k in tiles)
                {
                    var tm = CityMeshes.Build(map, trims, noBld, (int)(k >> 32), (int)(uint)k);
                    if (tm.ground != null) gTris += tm.ground.triangles.Length / 3;
                    Destroy(tm);
                }
                double ms = t0.Elapsed.TotalMilliseconds;
                if (!(doCensus && pass == 0)) { passMs[pass & 1] += ms; passN[pass & 1]++; }   // the census pass is not timed
                sb.AppendLine($"PASS {pass} backing {((pass & 1) == 1 ? "ON" : "OFF")}: tiles built in {ms / 1000.0:0.0} s ({ms / tiles.Count:0.0} ms a tile), ground triangles {gTris}");
                sb.AppendLine("  " + CityMeshes.WalkBackStats.Line());
                sb.AppendLine("  " + CityMeshes.WalkStats.Line()); sb.AppendLine($"  curb cut wings {(CityMeshes.CurbWingsOn ? "on" : "off")}: {CityMeshes.CurbWingSpans} spans with a cut and its wings");
                if (pass == 0) { census = new Dictionary<int, (int, int, int, int, float, float, float, Vector2)>(CityMeshes.WalkBackStats.byEdge); censusLine = CityMeshes.WalkBackStats.CensusLine(); }
            }
            CityMeshes.WalkBackOn = wasOn; CityMeshes.CurbWingsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_CURBWINGS") != "0";
            CityMeshes.WalkBackCensus = false;
            double offMs = passMs[0] / passN[0], onMs = passMs[1] / passN[1];
            sb.AppendLine($"BUILD TIME (these tiles): OFF {offMs / 1000.0:0.00} s a pass, ON {onMs / 1000.0:0.00} s a pass ({100.0 * (onMs - offMs) / offMs:+0.0;-0.0}%)");
            sb.AppendLine("CENSUS (backing off = live) " + censusLine);

            var csv = new StringBuilder("edge,name,sections,trench,trenchLeft,fall,maxTrenchDipUnderWalkBack,maxTrenchDipUnderLaneEdge,maxFallDip,x,z\n");
            int eTrench = 0, eFall = 0, eAny = 0, eLane = 0, eLane10 = 0, eLeft = 0, eLeftAll = 0, sTrench = 0, sFall = 0, sAll = 0;
            foreach (var kv in census.OrderByDescending(kv => kv.Value.dip))
            {
                var r = kv.Value; var e = map.edges[kv.Key];
                sAll += r.n; sTrench += r.trench; sFall += r.fall;
                if (r.trench > 0) eTrench++;
                if (r.fall > 0) eFall++;
                if (r.trench > 0 || r.fall > 0) eAny++;
                if (r.trench > 0 && r.laneDip > 0f) eLane++;
                if (r.trench > 0 && r.laneDip > 0.10f) eLane10++;
                if (r.left > 0) eLeft++;
                if (r.trench > 0 && r.left == r.trench) eLeftAll++;
                csv.Append($"{kv.Key},\"{e.name}\",{r.n},{r.trench},{r.left},{r.fall},{r.dip:0.000},{r.laneDip:0.000},{r.fallDip:0.000},{r.at.x:0.0},{r.at.y:0.0}\n");
            }
            sb.AppendLine($"EDGES (backing off = live): {census.Count} walk edges with backing sections ({sAll} sections)");
            sb.AppendLine($"  land behind the walk dips > {CityMeshes.PitM:0.00} m under the walk's back: {eAny} edges ({eTrench} with a trench = dips and comes back up, {eFall} with a fall = stays lower); sections {sTrench} trench, {sFall} fall");
            sb.AppendLine($"  trench edges whose trench bottoms under the LANE edge: {eLane}, more than 0.10 m under it: {eLane10}");
            sb.AppendLine($"  after (backing on): trench edges with some trench left {eLeft} (all of it left: {eLeftAll}); trench edges filled entirely {eTrench - eLeft}");
            sb.AppendLine($"total {sw.Elapsed.TotalSeconds:0.0} s");
            string root = System.IO.Directory.GetParent(Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_walkback.txt"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "city_walkback_edges.csv"), csv.ToString());
            Debug.Log(sb.ToString());
        }

        static void Destroy(CityMeshes.TileMeshes tm)
        {
            foreach (var mm in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings })
                if (mm != null) Object.DestroyImmediate(mm);
        }
    }
}
