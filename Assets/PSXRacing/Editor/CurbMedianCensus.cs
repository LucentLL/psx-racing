using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PSXRacing.City;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CURBED MEDIANS, COUNTED CITY-WIDE (curbs city-wide, 2026-11-05).
    /// Every one-way named carriageway is sorted by why it gets no median
    /// island by class (CityMeshes.CurbMedianExcluded: ramp, deck, tunnel,
    /// motorway, Barriered trunk, refuge pair, no twin), then every tile any
    /// island candidate touches is built by the one generator
    /// (CityMeshes.Build, as the tile bake does) and timed, and the islands
    /// the build laid are summed per edge: curbed (narrow medians) and flat
    /// grass at grade (wide ones). Run it once per PSX_CITY_CURBMEDIAN value
    /// in one sandbox: the same tiles, so the build times compare.
    /// Writes curb_median_census.txt (+ _edges.tsv) at the project root.
    ///   -executeMethod PSXRacing.EditorTools.CurbMedianCensus.Run
    /// </summary>
    public static class CurbMedianCensus
    {
        public static void Run()
        {
            var map = CityMap.Get();
            var root = Directory.GetParent(Application.dataPath).FullName;
            var sb = new StringBuilder();
            if (map == null) { File.WriteAllText(Path.Combine(root, "curb_median_census.txt"), "no city data"); return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine($"CURB MEDIAN CENSUS graph {map.graphHash:x8} PSX_CITY_CURBMEDIAN='{System.Environment.GetEnvironmentVariable("PSX_CITY_CURBMEDIAN")}' on={CityMeshes.CurbMedianOn}");

            // ---- by class ----
            var why = new SortedDictionary<string, (int n, float m)>();
            var cands = new List<CityMap.Edge>();
            foreach (var e in map.edges)
            {
                if (e == null || !e.oneway || string.IsNullOrEmpty(e.name) || e.a == e.b) continue;
                string w = CityMeshes.CurbMedianExcluded(map, e) ?? "CANDIDATE (island class, twin on the left)";
                bool cand = w.StartsWith("CANDIDATE") || w == "switched off";
                why.TryGetValue(w, out var v); why[w] = (v.n + 1, v.m + e.length);
                if (cand) cands.Add(e);
            }
            sb.AppendLine("one-way named carriageways by class:");
            foreach (var kv in why) sb.AppendLine($"  {kv.Key}: {kv.Value.n} edges, {kv.Value.m / 1000f:0.0} km");

            // ---- the tiles they touch ----
            float T = CityMeshes.TileSize;
            var tiles = new SortedSet<long>();
            foreach (var e in cands)
                for (float s = 0f; s <= e.length + 1e-3f; s += Mathf.Min(40f, Mathf.Max(1f, e.length)))
                {
                    var p = e.PointAt(Mathf.Min(s, e.length));
                    int tx = Mathf.FloorToInt(p.x / T), tz = Mathf.FloorToInt(p.y / T);
                    tiles.Add(((long)tx << 32) | (uint)tz);
                }
            string only = System.Environment.GetEnvironmentVariable("PSX_CMC_MAXTILES");
            int maxTiles = int.TryParse(only, out int mt) ? mt : int.MaxValue;

            var go = new GameObject("~curbCensus") { hideFlags = HideFlags.DontSave };
            var world = go.AddComponent<CityWorld>();
            typeof(CityWorld).GetMethod("EnsureInit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(world, null);
            var trims = CityWorld.SharedTrims;
            var blds = CityWorld.SharedBuildings;
            bool wasOn = CityTileCache.Enabled;
            CityTileCache.Enabled = false;
            lock (CityMeshes.CurbMedianByEdge) { CityMeshes.CurbMedianByEdge.Clear(); CityMeshes.CurbMedianFlatByEdge.Clear(); }
            CityMeshes.CurbMedianSpans = 0; CityMeshes.CurbMedianM = 0f; CityMeshes.CurbMedianFlatSpans = 0; CityMeshes.CurbMedianFlatM = 0f;
            double buildMs = 0, worst = 0; string worstAt = ""; long verts = 0, tris = 0; int built = 0;
            var meshFields = new List<FieldInfo>();
            foreach (var f in typeof(CityMeshes.TileMeshes).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (f.FieldType == typeof(Mesh)) meshFields.Add(f);
            try
            {
                // one warm-up build (first-call costs out of the timing)
                {
                    long k0 = tiles.Min;
                    var w = CityMeshes.Build(map, trims, blds, (int)(k0 >> 32), (int)(uint)k0);
                    foreach (var f in meshFields) if (f.GetValue(w) is Mesh m && m != null) Object.DestroyImmediate(m);
                    lock (CityMeshes.CurbMedianByEdge) { CityMeshes.CurbMedianByEdge.Clear(); CityMeshes.CurbMedianFlatByEdge.Clear(); }
                    CityMeshes.CurbMedianSpans = 0; CityMeshes.CurbMedianM = 0f; CityMeshes.CurbMedianFlatSpans = 0; CityMeshes.CurbMedianFlatM = 0f;
                }
                foreach (long k in tiles)
                {
                    if (built >= maxTiles) break;
                    int tx = (int)(k >> 32), tz = (int)(uint)k;
                    var c = System.Diagnostics.Stopwatch.StartNew();
                    var tm = CityMeshes.Build(map, trims, blds, tx, tz);
                    double b = c.Elapsed.TotalMilliseconds;
                    buildMs += b; built++;
                    if (b > worst) { worst = b; worstAt = tx + "," + tz; }
                    foreach (var f in meshFields)
                        if (f.GetValue(tm) is Mesh m && m != null)
                        {
                            verts += m.vertexCount;
                            for (int si = 0; si < m.subMeshCount; si++) tris += m.GetIndexCount(si) / 3;
                            Object.DestroyImmediate(m);
                        }
                    if (built % 250 == 0) Debug.Log($"[CurbCensus] {built}/{tiles.Count} tiles, {buildMs / 1000.0:0} s");
                }
            }
            finally { CityTileCache.Enabled = wasOn; Object.DestroyImmediate(go); }

            // ---- the islands laid ----
            int nC = 0, nF = 0; float mC = 0f, mF = 0f;
            var byName = new Dictionary<string, float>();
            var tsv = new StringBuilder("edge\tname\tcls\tlength\tisland_m\tflat_m\n");
            lock (CityMeshes.CurbMedianByEdge)
                foreach (var kv in CityMeshes.CurbMedianByEdge)
                {
                    var e = map.edges[kv.Key];
                    float all = Mathf.Min(kv.Value, e.length);
                    CityMeshes.CurbMedianFlatByEdge.TryGetValue(kv.Key, out float flat);
                    flat = Mathf.Min(flat, all);
                    float curb = all - flat;
                    if (curb > 0.5f) { nC++; mC += curb; }
                    if (flat > 0.5f) { nF++; mF += flat; }
                    byName.TryGetValue(e.name, out float bn); byName[e.name] = bn + curb;
                    tsv.AppendLine($"{e.index}\t{e.name}\t{e.cls}\t{e.length.ToString("0.0", ci)}\t{curb.ToString("0.0", ci)}\t{flat.ToString("0.0", ci)}");
                }
            sb.AppendLine($"tiles built {built} of {tiles.Count} (the tiles the candidates touch): build {buildMs / 1000.0:0.0} s, avg {buildMs / System.Math.Max(1, built):0.0} ms, worst {worst:0} ms at {worstAt}; {verts / 1e6:0.000} M vertices, {tris / 1e6:0.000} M triangles");
            sb.AppendLine($"islands laid: CURBED {nC} edges {mC / 1000f:0.0} km; FLAT GRASS (wide) {nF} edges {mF / 1000f:0.0} km; spans {CityMeshes.CurbMedianSpans} ({CityMeshes.CurbMedianFlatSpans} flat), raw {CityMeshes.CurbMedianM / 1000f:0.0} km");
            var top = new List<KeyValuePair<string, float>>(byName);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var tl = new StringBuilder("curbed by road (top 25):");
            for (int i = 0; i < top.Count && i < 25; i++) tl.Append($" {top[i].Key} {top[i].Value / 1000f:0.00};");
            sb.AppendLine(tl.ToString());
            File.WriteAllText(Path.Combine(root, "curb_median_census.txt"), sb.ToString());
            File.WriteAllText(Path.Combine(root, "curb_median_census_edges.tsv"), tsv.ToString());
            Debug.Log("[CurbCensus] " + sb);
        }
    }
}
