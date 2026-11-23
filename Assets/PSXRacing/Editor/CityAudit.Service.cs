using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// SERVICE WAYS (2026-11-15; CityMeshes.ServiceWays: the extract's access
    /// drives, alleys, drive-throughs and loose parking aisles as pavement,
    /// no graph node or edge). One job, the state the environment sets
    /// (PSX_CITY_SERVICE=0: the base):
    ///   1. the pass's own line (pieces, km by kind, quads, kerb openings,
    ///      stalls and islands taken off, mouths, load ms) and the graph hash;
    ///   2. the lots' entrance audit as built (CityMeshes.LotEntranceAudit):
    ///      the lots with no usable entrance, and their list by index (the
    ///      base's list against the list with the ways in = lots that gain a
    ///      way in);
    ///   3. the cost on the tiles of PSX_SERVICE_TILES ("label:tx,tz;..."):
    ///      triangles, build ms (median of 4) and baked bytes
    ///      (CityTileCache.Encode);
    ///   4. the ways' own ground (the lattice the pavement is cut into): the
    ///      steepest 2 m along each way and the worst fall across its width,
    ///      for PSX_SERVICE_HEIGHT_S seconds (default 150) in file order;
    ///      PSX_SERVICE_STEEP_OUT: a file of every place steeper than 20%
    ///      along or 25% across (x, z, along, across), which the exporter
    ///      step reads (--steep) to stop the ways short of a bank.
    /// -executeMethod PSXRacing.EditorTools.CityAudit.ServiceOnly; writes
    /// PSXRacing_service.txt (PSX_SERVICE_OUT: another name) in the project root.
    /// </summary>
    public static partial class CityAudit
    {
        public static void ServiceOnly()
        {
            outLog = new System.Text.StringBuilder();
            var I = CultureInfo.InvariantCulture;
            string outName = System.Environment.GetEnvironmentVariable("PSX_SERVICE_OUT");
            if (string.IsNullOrEmpty(outName)) outName = "PSXRacing_service.txt";
            try
            {
                var map = CityMap.Get();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                Line($"SERVICE WAYS AUDIT: graph hash {map.graphHash:x8} (GHSH), matches {map.GraphHashMatches}, computed {CityMap.GraphHashOf(map.edges):x8}; {map.nodes.Length} nodes, {map.edges.Length} edges; trims + buildings in {sw.ElapsedMilliseconds} ms");
                Line($"parse {CityMap.LastParseMs:0} ms, solve {CityMap.LastSolveMs:0} ms; lots {map.lots.Length} (stations' from {map.stationLotsFrom}, lot entrances' from {CityMeshes.LotEntLotsFrom}, service ways' from {CityMeshes.ServiceLotsFrom}: {CityMeshes.ServiceLotsCount}), " +
                     $"entrances {map.lotEntrances.Length} (lot entrances' from {CityMeshes.LotEntEntrancesFrom}, service mouths from {CityMeshes.ServiceEntrancesFrom}: {CityMeshes.ServiceEntrancesCount})");
                Line(CityMeshes.ServiceStats.Line());
                Line(CityMeshes.LotEntStats.Line());
                {
                    // the invented buildings on the ways (taken out by Precompute's last step), and what is left on them
                    int left = 0, food = 0, all = 0; var rows = new List<string>();
                    foreach (var kv in proc)
                        foreach (var b in kv.Value)
                        {
                            all++;
                            if (!CityMeshes.ServiceUnderBuilding(map, b)) continue;
                            if (CityProps.IsFood(b.kind)) food++; else left++;
                            if (rows.Count < 12) rows.Add(string.Format(I, "kind {0} {1:0}x{2:0} m at ({3:0}, {4:0})", b.kind, b.w, b.d, b.pos.x, b.pos.y));
                        }
                    Line($"BUILDINGS ON THE WAYS: {CityMeshes.ServiceStats.buildingsCleared} invented frontage buildings taken off the ways; left standing on a way: {left} (+{food} restaurant landmarks) of {all} invented buildings" + (rows.Count > 0 ? ": " + string.Join("; ", rows) : ""));
                }

                // ---- 2. the lots
                if (System.Environment.GetEnvironmentVariable("PSX_SERVICE_LOTS") != "0")
                {
                    sw.Restart();
                    var A = CityMeshes.LotEntranceAudit(map, trims, proc);
                    Line(string.Format(I, "LOTS ({0} ms): {1} drawn lots, {2} entrances in the map; {3} with an entrance a car can use, {4} more reached through a lot that has one; {5} LOTS WITH NO USABLE ENTRANCE ({6:0.0} ha), {7} of them shut all round",
                        sw.ElapsedMilliseconds, A.lots, A.dataEntrances, A.usable, A.served, A.none, A.noneM2 / 1e4, A.noneCurbedAllRound));
                    foreach (var kv in A.whyCount) Line($"  why none: {kv.Value,5}  {kv.Key}");
                    var none = new System.Text.StringBuilder("NONE LOTS");
                    for (int li = 0; li < A.state.Length; li++) if (A.state[li] == 3) none.Append(' ').Append(li);
                    Line(none.ToString());
                    var by2 = new System.Text.StringBuilder("SERVED LOTS");
                    for (int li = 0; li < A.state.Length; li++) if (A.state[li] == 2) by2.Append(' ').Append(li);
                    Line(by2.ToString());
                }

                // ---- 3. the cost
                string tiles = System.Environment.GetEnvironmentVariable("PSX_SERVICE_TILES");
                if (!string.IsNullOrEmpty(tiles))
                    foreach (var spec in tiles.Split(';'))
                    {
                        var kv = spec.Split(':');
                        if (kv.Length < 2) continue;
                        var xy = kv[1].Split(',');
                        int tx = int.Parse(xy[0], I), tz = int.Parse(xy[1], I);
                        var ms = new List<double>(); long tris = 0, baked = 0; int svcLots = 0, allLots = 0;
                        var ll = CityMeshes.LotsInTile(map, tx, tz);
                        if (ll != null) foreach (int li in ll) { allLots++; if (CityMeshes.ServiceLotsFrom >= 0 && li >= CityMeshes.ServiceLotsFrom) svcLots++; }
                        for (int rep = 0; rep < 5; rep++)
                        {
                            var t0 = System.Diagnostics.Stopwatch.StartNew();
                            var tm = CityMeshes.Build(map, trims, proc, tx, tz);
                            if (rep > 0) ms.Add(t0.Elapsed.TotalMilliseconds);
                            if (rep == 4) baked = CityTileCache.Encode(tm).Length;
                            tris = 0;
                            foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings })
                                if (m != null) { for (int sm = 0; sm < m.subMeshCount; sm++) tris += (long)m.GetIndexCount(sm) / 3; Object.DestroyImmediate(m); }
                        }
                        ms.Sort();
                        Line(string.Format(I, "COST tile ({0}, {1}) {2}: {3} triangles, build median {4:0.0} ms (min {5:0.0}), baked {6} bytes; lots in the tile {7} ({8} service quads)", tx, tz, kv[0], tris, ms[ms.Count / 2], ms[0], baked, allLots, svcLots));
                    }

                // ---- 4. the ways' ground
                float budget = 150f;
                string hb = System.Environment.GetEnvironmentVariable("PSX_SERVICE_HEIGHT_S");
                if (!string.IsNullOrEmpty(hb)) float.TryParse(hb, NumberStyles.Float, I, out budget);
                if (CityMeshes.ServiceWays.Length > 0 && budget > 0f)
                {
                    sw.Restart();
                    int done = 0, g12 = 0, g20 = 0, c12 = 0, c20 = 0; float kmDone = 0f, m15 = 0f;
                    var worst = new List<(float g, float c, int way, Vector2 at)>();
                    var ys = new List<float>(256); var ps = new List<Vector2>(256);
                    // every place steeper than the limits, for the exporter step (tools/city/service_ways.mjs --steep): x, z, along, across
                    var steepRows = new System.Text.StringBuilder();
                    const float SteepAlong = 0.20f, SteepAcross = 0.25f;
                    for (int wi = 0; wi < CityMeshes.ServiceWays.Length && sw.Elapsed.TotalSeconds < budget; wi++)
                    {
                        var w = CityMeshes.ServiceWays[wi];
                        ys.Clear(); ps.Clear();
                        float maxG = 0f, maxC = 0f; var atG = w.pts[0];
                        float skipA = w.mouthA >= 0 ? 8f : 0f, skipB = w.mouthB >= 0 ? 8f : 0f, acc = 0f;
                        for (int k = 0; k + 1 < w.pts.Length; k++)
                        {
                            var d = w.pts[k + 1] - w.pts[k]; float len = d.magnitude;
                            if (len < 0.05f) continue;
                            int n = Mathf.Max(1, Mathf.RoundToInt(len));
                            var left = new Vector2(-d.y, d.x) / len;
                            for (int j = 0; j < n; j++)
                            {
                                float u = (j + 0.5f) / n; float s = acc + len * u;
                                if (s < skipA || s > w.length - skipB) continue;
                                var p = w.pts[k] + d * u;
                                ps.Add(p); ys.Add(CityMeshes.SharedGroundAt(map, trims, proc, p.x, p.y));
                                if ((ps.Count & 3) == 0)
                                {
                                    float h = Mathf.Lerp(w.half[k], w.half[k + 1], u);
                                    float yl = CityMeshes.SharedGroundAt(map, trims, proc, p.x + left.x * h, p.y + left.y * h), yr = CityMeshes.SharedGroundAt(map, trims, proc, p.x - left.x * h, p.y - left.y * h);
                                    float cf = Mathf.Abs(yl - yr) / (2f * h);
                                    maxC = Mathf.Max(maxC, cf);
                                    if (cf > SteepAcross) steepRows.AppendLine(string.Format(I, "{0:0.0} {1:0.0} 0 {2:0.00}", p.x, p.y, cf));
                                }
                            }
                            acc += len;
                        }
                        for (int i = 2; i < ys.Count; i++)
                        {
                            float run = (ps[i] - ps[i - 2]).magnitude;
                            if (run < 1.2f || run > 3.5f) continue;
                            float g = Mathf.Abs(ys[i] - ys[i - 2]) / run;
                            if (g > 0.15f) m15 += run * 0.5f;
                            if (g > maxG) { maxG = g; atG = ps[i - 1]; }
                            if (g > SteepAlong) steepRows.AppendLine(string.Format(I, "{0:0.0} {1:0.0} {2:0.00} 0", ps[i - 1].x, ps[i - 1].y, g));
                        }
                        done++; kmDone += w.length / 1000f;
                        if (maxG > 0.12f) g12++; if (maxG > 0.20f) g20++; if (maxC > 0.12f) c12++; if (maxC > 0.20f) c20++;
                        if (maxG > 0.20f || maxC > 0.20f) worst.Add((maxG, maxC, wi, atG));
                    }
                    Line(string.Format(I, "GROUND under the ways ({0} of {1} pieces, {2:0.0} km, {3:0} s; the first 8 m behind a mouth left to the apron's ramp): steepest 2 m over 12% on {4} pieces, over 20% on {5}; {6:0} m of way steeper than 15%; fall across the width over 12% on {7}, over 20% on {8}",
                        done, CityMeshes.ServiceWays.Length, kmDone, sw.Elapsed.TotalSeconds, g12, g20, m15, c12, c20));
                    string steepOut = System.Environment.GetEnvironmentVariable("PSX_SERVICE_STEEP_OUT");
                    if (!string.IsNullOrEmpty(steepOut))
                    {
                        // ADDED to the file's own rows (by the metre): the ways already stopped short of a bank are
                        // not measured there again, so a run on cut data must not drop the places that cut them
                        var seen = new HashSet<long>(); var all = new System.Text.StringBuilder(); int was = 0;
                        void AddRow(string r)
                        {
                            var f = r.Trim().Split(' ');
                            if (f.Length < 2 || !float.TryParse(f[0], NumberStyles.Float, I, out float sx) || !float.TryParse(f[1], NumberStyles.Float, I, out float sz)) return;
                            if (seen.Add(((long)Mathf.RoundToInt(sx) << 32) ^ (uint)Mathf.RoundToInt(sz))) all.AppendLine(r.Trim());
                        }
                        if (System.IO.File.Exists(steepOut)) { foreach (var r in System.IO.File.ReadAllLines(steepOut)) AddRow(r); was = seen.Count; }
                        foreach (var r in steepRows.ToString().Split('\n')) AddRow(r);
                        System.IO.File.WriteAllText(steepOut, all.ToString());
                        Line($"steep places (over {SteepAlong * 100f:0}% along or {SteepAcross * 100f:0}% across): {seen.Count} in {steepOut} ({was} were in it)");
                    }
                    worst.Sort((a, b) => Mathf.Max(b.g, b.c).CompareTo(Mathf.Max(a.g, a.c)));
                    for (int k = 0; k < Mathf.Min(30, worst.Count); k++)
                    {
                        var r = worst[k]; var w = CityMeshes.ServiceWays[r.way];
                        Line(string.Format(I, "  steep #{0}: piece {1} (way {2}, {3}, {4:0} m): along {5:0}%, across {6:0}% at ({7:0}, {8:0}) = {9:0.00000},{10:0.00000}", k + 1, r.way, w.osmId, CityMeshes.ServiceKinds[w.kind], w.length, r.g * 100f, r.c * 100f, r.at.x, r.at.y,
                            35.18456015184093 + r.at.y / 111132.0, -80.81770185962013 + r.at.x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0))));
                    }
                }
                Line("SERVICE WAYS AUDIT DONE");
            }
            catch (System.Exception ex) { Line("SERVICE WAYS AUDIT FAILED: " + ex); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, outName), outLog.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
