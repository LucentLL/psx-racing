using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE DECK-PLATE PROBE (2026-11-15, agent "spui"): dumps the graph, the
    /// solved heights and the trims round given spots as plain rows, for the
    /// offline look at the places where ramps meet on one deck with no shared
    /// node (CityDeckPlates). PSX_SPUI_SPOTS = "name,x,z,r;..." (world metres),
    /// PSX_SPUI_OUT = the file. -executeMethod PSXRacing.EditorTools.CitySpuiProbe.Dump
    /// </summary>
    public static class CitySpuiProbe
    {
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static void Dump()
        {
            int code = 0;
            try
            {
                string spots = Environment.GetEnvironmentVariable("PSX_SPUI_SPOTS");
                string outFile = Environment.GetEnvironmentVariable("PSX_SPUI_OUT");
                if (string.IsNullOrEmpty(outFile)) outFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_spui_dump.txt");
                if (File.Exists(outFile)) File.Delete(outFile);
                var map = CityMap.Get();
                if (map == null) throw new Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var sb = new StringBuilder();
                sb.AppendLine($"HASH {map.graphHash:x8} plates {(CityDeckPlates.On ? 1 : 0)}");
                sb.Append(CityDeckPlates.Report(map));
                foreach (var pl in CityDeckPlates.Plates)
                {
                    if (pl.hull == null) continue;
                    sb.Append($"HULL {pl.id} {F(pl.H)}");
                    foreach (var p in pl.hull) sb.Append(' ').Append(F(p.x)).Append(' ').Append(F(p.y));
                    sb.AppendLine();
                    sb.AppendLine($"LEVEL {pl.id} {string.Join(" ", pl.levelEdges)}");
                    sb.AppendLine($"NODES {pl.id} {string.Join(" ", pl.nodes)}");
                }
                foreach (string spot in (spots ?? "").Split(';'))
                {
                    var f = spot.Split(',');
                    if (f.Length < 4) continue;
                    float x = float.Parse(f[1], CultureInfo.InvariantCulture), z = float.Parse(f[2], CultureInfo.InvariantCulture), r = float.Parse(f[3], CultureInfo.InvariantCulture);
                    var c = new Vector2(x, z);
                    sb.AppendLine($"SPOT {f[0]} {F(x)} {F(z)} {F(r)}");
                    var nodes = new HashSet<int>();
                    foreach (var e in map.edges)
                    {
                        bool near = false;
                        for (int i = 0; i < e.pts.Length && !near; i++) near = (e.pts[i] - c).sqrMagnitude <= r * r;
                        if (!near) continue;
                        nodes.Add(e.a); nodes.Add(e.b);
                        LineModel.Extents(e, e.length * 0.5f, out float eM, out float eP);
                        sb.AppendLine($"E {e.index} {e.a} {e.b} {e.cls} {(e.link ? 1 : 0)} {(e.oneway ? 1 : 0)} {(e.bridge ? 1 : 0)} {(e.tunnel ? 1 : 0)} {e.layer} {e.lanes} {F(e.width)} {F(eP)} {F(eM)} {F(e.lmOff)} {F(e.length)} {F(trims.atA[e.index])} {F(trims.atB[e.index])} {trims.branchA[e.index]} {trims.branchB[e.index]} {(trims.Internal(e.index) ? 1 : 0)} {e.speedKmh} {e.structId} {(string.IsNullOrEmpty(e.name) ? "-" : e.name.Replace(' ', '_'))}");
                        sb.Append($"P {e.index}");
                        foreach (var p in e.pts) sb.Append(' ').Append(F(p.x)).Append(' ').Append(F(p.y));
                        sb.AppendLine();
                        sb.Append($"S {e.index}");
                        for (int i = 0; i < e.stS.Length; i++)
                            sb.Append(' ').Append(F(e.stS[i])).Append(' ').Append(F(e.stY[i])).Append(' ').Append(e.stElev[i] ? 1 : 0).Append(' ').Append(e.SeatedAt(i) ? 1 : 0);
                        sb.AppendLine();
                        if (e.vS != null)
                        {
                            sb.Append($"V {e.index}");
                            for (int i = 0; i < e.vS.Length; i++) sb.Append(' ').Append(F(e.vS[i])).Append(' ').Append(F(e.vY[i]));
                            sb.AppendLine();
                        }
                        // the drawn extents every 2 m: s, eMinus, ePlus
                        sb.Append($"W {e.index}");
                        for (float s = 0f; ; s += 2f)
                        {
                            float ss = Mathf.Min(s, e.length);
                            LineModel.Extents(e, ss, out float m, out float p2);
                            sb.Append(' ').Append(F(ss)).Append(' ').Append(F(m)).Append(' ').Append(F(p2));
                            if (ss >= e.length) break;
                        }
                        sb.AppendLine();
                    }
                    foreach (int n in nodes)
                    {
                        var cl = trims.ClusterOfNode(n);
                        sb.AppendLine($"N {n} {F(map.nodes[n].x)} {F(map.nodes[n].y)} {F(map.nodeY[n])} {(trims.patch[n] ? 1 : 0)} {(trims.mitre[n] ? 1 : 0)} {(cl != null ? cl.id : -1)} {trims.throughA[n]} {trims.throughB[n]} {(map.nodeControl != null ? map.nodeControl[n] : 0)} {map.nodeEdges[n].Count}");
                    }
                    if (map.crossings != null)
                        foreach (var x2 in map.crossings)
                            if ((x2.at - c).sqrMagnitude <= r * r)
                                sb.AppendLine($"X {x2.over} {x2.under} {F(x2.at.x)} {F(x2.at.y)} {(x2.forced ? 1 : 0)}");
                }
                File.WriteAllText(outFile, sb.ToString());
                Debug.Log("[SpuiProbe] wrote " + outFile);
            }
            catch (Exception ex) { Debug.LogError("[SpuiProbe] " + ex); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>
        /// ONE SURFACE, COUNTED. Builds the tiles each plate lies on, three
        /// ways - the plates off, their heights only, the heights and the
        /// slab - and lays a 0.25 m grid over each plate: at every grid point
        /// inside it, how many upward road sheets at the deck's level lie
        /// over one another (LAYERS) and how far apart their heights stand
        /// (SPREAD). One surface is 1 layer and 0 spread everywhere.
        /// PSX_SPUI_MODES (default "off,heights,full"), PSX_SPUI_PLATES
        /// (ids, default all), PSX_SPUI_OUT.
        /// -executeMethod PSXRacing.EditorTools.CitySpuiProbe.Tiles
        /// </summary>
        public static void Tiles()
        {
            int code = 0;
            string outFile = Environment.GetEnvironmentVariable("PSX_SPUI_OUT");
            if (string.IsNullOrEmpty(outFile)) outFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_spui_tiles.txt");
            if (File.Exists(outFile)) File.Delete(outFile);
            var sb = new StringBuilder();
            void Flush() => File.WriteAllText(outFile, sb.ToString());
            try
            {
                var map = CityMap.Get();
                if (map == null) throw new Exception("no city data");
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                sb.AppendLine($"HASH {map.graphHash:x8}");
                sb.Append(CityDeckPlates.Report(map));
                Flush();
                string modes = Environment.GetEnvironmentVariable("PSX_SPUI_MODES");
                if (string.IsNullOrWhiteSpace(modes)) modes = "off,heights,full";
                var wantPlates = new HashSet<string>((Environment.GetEnvironmentVariable("PSX_SPUI_PLATES") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                bool cutWas = CityDeckPlates.CutOn;
                const float Cell = 0.25f;
                foreach (var pl in CityDeckPlates.Plates)
                {
                    if (pl.skipped != null || pl.hull == null) continue;
                    if (wantPlates.Count > 0 && !wantPlates.Contains(pl.id.ToString())) continue;
                    int tx0 = Mathf.FloorToInt(pl.bbMin.x / CityMeshes.TileSize), tx1 = Mathf.FloorToInt(pl.bbMax.x / CityMeshes.TileSize);
                    int tz0 = Mathf.FloorToInt(pl.bbMin.y / CityMeshes.TileSize), tz1 = Mathf.FloorToInt(pl.bbMax.y / CityMeshes.TileSize);
                    int gw = Mathf.CeilToInt((pl.bbMax.x - pl.bbMin.x) / Cell) + 1, gh = Mathf.CeilToInt((pl.bbMax.y - pl.bbMin.y) / Cell) + 1;
                    var inHull = new bool[gw * gh];
                    int hullCells = 0;
                    for (int j = 0; j < gh; j++)
                        for (int i = 0; i < gw; i++)
                        {
                            inHull[j * gw + i] = CityDeckPlates.InHull(pl.hull, new Vector2(pl.bbMin.x + (i + 0.5f) * Cell, pl.bbMin.y + (j + 0.5f) * Cell));
                            if (inHull[j * gw + i]) hullCells++;
                        }
                    foreach (string mode0 in modes.Split(','))
                    {
                        string mode = mode0.Trim();
                        CityDeckPlates.SetOn(map, mode != "off");
                        CityDeckPlates.CutOn = mode == "full";
                        var layers = new byte[gw * gh];
                        var yLo = new float[gw * gh]; var yHi = new float[gw * gh];
                        for (int i = 0; i < yLo.Length; i++) { yLo[i] = float.MaxValue; yHi[i] = float.MinValue; }
                        int roadTris = 0, levelTris = 0; double ms = 0;
                        CityMeshes.DeckPlateCutTris = CityMeshes.DeckPlateSlabTris = CityMeshes.DeckPlateLeftTris = CityMeshes.DeckPlateTileCuts = 0; CityMeshes.DeckPlateMs = 0;
                        for (int tz = tz0; tz <= tz1; tz++)
                            for (int tx = tx0; tx <= tx1; tx++)
                            {
                                var clock = System.Diagnostics.Stopwatch.StartNew();
                                var tm = CityMeshes.Build(map, trims, buildings, tx, tz);
                                ms += clock.Elapsed.TotalMilliseconds;
                                if (tm.roads == null) continue;
                                var vs = tm.roads.vertices; var o = tm.origin;
                                for (int sm = 0; sm < tm.roads.subMeshCount; sm++)
                                {
                                    var idx = tm.roads.GetTriangles(sm);
                                    roadTris += idx.Length / 3;
                                    for (int k = 0; k + 2 < idx.Length; k += 3)
                                    {
                                        Vector3 a = vs[idx[k]] + o, b = vs[idx[k + 1]] + o, c = vs[idx[k + 2]] + o;
                                        if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < pl.bbMin.x || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > pl.bbMax.x ||
                                            Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < pl.bbMin.y || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > pl.bbMax.y) continue;
                                        if (Mathf.Abs(a.y - pl.yRef) > 2.5f || Mathf.Abs(b.y - pl.yRef) > 2.5f || Mathf.Abs(c.y - pl.yRef) > 2.5f) continue;
                                        var n = Vector3.Cross(b - a, c - a);
                                        // an upward sheet (Unity: clockwise from its front, Cross(b - a, c - a) out of it)
                                        if (n.y <= 0f || n.y < 0.9f * n.magnitude) continue;
                                        levelTris++;
                                        float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                                        if (Mathf.Abs(den) < 1e-9f) continue;
                                        int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - pl.bbMin.x) / Cell)), i1 = Mathf.Min(gw - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - pl.bbMin.x) / Cell));
                                        int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - pl.bbMin.y) / Cell)), j1 = Mathf.Min(gh - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - pl.bbMin.y) / Cell));
                                        for (int j = j0; j <= j1; j++)
                                            for (int i = i0; i <= i1; i++)
                                            {
                                                if (!inHull[j * gw + i]) continue;
                                                float px = pl.bbMin.x + (i + 0.5f) * Cell, pz = pl.bbMin.y + (j + 0.5f) * Cell;
                                                float w0 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / den;
                                                float w1 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / den;
                                                float w2 = 1f - w0 - w1;
                                                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                                                float y = w0 * a.y + w1 * b.y + w2 * c.y;
                                                int q = j * gw + i;
                                                if (layers[q] < 255) layers[q]++;
                                                if (y < yLo[q]) yLo[q] = y;
                                                if (y > yHi[q]) yHi[q] = y;
                                            }
                                    }
                                }
                                foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.structure, tm.kerbs, tm.lampPosts, tm.banks, tm.medianPlants, tm.water, tm.buildings, tm.buildingCollider })
                                    if (m != null) UnityEngine.Object.DestroyImmediate(m);
                            }
                        int covered = 0, multi = 0, maxLayers = 0, spread2 = 0, spread05 = 0; float maxSpread = 0f, lo = float.MaxValue, hi = float.MinValue;
                        for (int q = 0; q < layers.Length; q++)
                        {
                            if (layers[q] == 0) continue;
                            covered++;
                            if (layers[q] > 1) multi++;
                            if (layers[q] > maxLayers) maxLayers = layers[q];
                            float sp = yHi[q] - yLo[q];
                            if (sp > 0.02f) spread2++;
                            if (sp > 0.005f) spread05++;
                            if (sp > maxSpread) maxSpread = sp;
                            lo = Mathf.Min(lo, yLo[q]); hi = Mathf.Max(hi, yHi[q]);
                        }
                        float m2 = Cell * Cell;
                        sb.AppendLine($"TILES plate {pl.id} mode {mode}: tiles {tx0}..{tx1},{tz0}..{tz1} built in {ms:0} ms; road tris {roadTris}, upward level tris in the plate's box {levelTris}; plate {hullCells * m2:0} m2, paved {covered * m2:0} m2, under 2+ sheets {multi * m2:0} m2 (most {maxLayers}), sheets over 0.5 cm apart {spread05 * m2:0} m2, over 2 cm apart {spread2 * m2:0} m2, worst {maxSpread * 100f:0.0} cm; surface y {lo:0.000}..{hi:0.000} (H {pl.H:0.000}); cut {CityMeshes.DeckPlateCutTris} slab {CityMeshes.DeckPlateSlabTris} kept {CityMeshes.DeckPlateLeftTris} in {CityMeshes.DeckPlateMs:0} ms; slab pieces {(pl.slab != null ? pl.slab.Count : -1)} of {(pl.cover != null ? pl.cover.Count : -1)} pavements{(pl.cutFailed ? " CUT FAILED" : "")}; managed heap {GC.GetTotalMemory(false) >> 20} MB");
                        Flush();
                    }
                }
                CityDeckPlates.SetOn(map, CityDeckPlates.EnvOn);
                CityDeckPlates.CutOn = cutWas;
                sb.AppendLine("TILES DONE");
                Flush();
            }
            catch (Exception ex) { sb.AppendLine("TILES FAILED: " + ex); Flush(); Debug.LogError("[SpuiProbe] " + ex); code = 1; }
            EditorApplication.Exit(code);
        }
    }
}
