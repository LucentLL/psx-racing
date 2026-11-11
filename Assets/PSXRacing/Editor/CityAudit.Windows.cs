using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CUT WINDOWS (the owner, 2026-11-11: "windows going through roofs"). Every
    /// window of the house siding (<c>city_facade_house</c>: one per repeat cell,
    /// u 0.3125-0.6875, v 0.25-0.75 - the Classic, Enhanced and HD textures all
    /// put it there) that a tile DRAWS, read back off the built mesh: the
    /// FacadeHouse submesh's triangles grouped by plane (a wall, a gable), each
    /// window cell their UVs touch clipped against them. A window is
    ///   WHOLE  the wall's triangles cover all of it,
    ///   CUT    they cover part of it (a roof line, a gable slope, a corner,
    ///          the wall's foot), or
    ///   BURIED whole on the wall but the drawn ground (the tile's lattice)
    ///          stands over its sill.
    /// Not measured: a window behind a neighbour's wall (no house in the sample
    /// was found touching another; the census says so if one is).
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.WindowsOnly
    ///   PSX_WIN_SAMPLE = tiles in the city-wide sample (default 64);
    ///   PSX_WIN_TILES = "x,z;x,z" world points whose tiles are added (the shot places)
    ///   PSX_WIN_HOUSES = 1: the house census (CityAudit.Houses) in the same job
    /// Output: windows_audit.txt in the project folder (last line WINDOWS OK / CUT).
    /// </summary>
    public static partial class CityAudit
    {
        public static void WindowsOnly() => WindowsAudit(true);

        public static int WindowsAudit(bool exit)
        {
            outLog = new System.Text.StringBuilder();
            var I = CultureInfo.InvariantCulture;
            int code = 0;
            try
            {
                var map = CityMap.Get();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                Line(string.Format(I, "WINDOWS AUDIT {0}: HOUSEWALLS {1} HOUSECLASS {2} HOUSEEAVES {3}",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), CityMeshes.HouseWallsOn, CityMeshes.HouseClassOn, CityMeshes.HouseEavesOn));

                // ---- item 1: the class rule, city-wide (no build)
                int houses = 0, blocks = 0, blocksGabled = 0;
                var byArea = new SortedDictionary<int, int>(); var byStorey = new SortedDictionary<int, int>();
                foreach (var f in map.footprints)
                {
                    if (f == null || f.style != 3 || f.hidden) continue;
                    houses++;
                    bool was = CityMeshes.HouseClassOn; CityMeshes.HouseClassOn = true;
                    bool b = CityMeshes.HouseBlock(f);
                    CityMeshes.HouseClassOn = was;
                    if (!b) continue;
                    blocks++; if (f.gable) blocksGabled++;
                    float a = 0f; for (int i = 0; i < f.pts.Length; i++) { var p = f.pts[i]; var q = f.pts[(i + 1) % f.pts.Length]; a += p.x * q.y - q.x * p.y; }
                    a = Mathf.Abs(a) * 0.5f;
                    int ab = a < 90 ? 0 : a < 250 ? 90 : a < 450 ? 250 : a < 1000 ? 450 : a < 3000 ? 1000 : 3000;
                    byArea.TryGetValue(ab, out int na); byArea[ab] = na + 1;
                    int st = Mathf.Clamp(Mathf.RoundToInt((f.h - 0.6f) / 2.9f), 1, 9);
                    byStorey.TryGetValue(st, out int ns); byStorey[st] = ns + 1;
                }
                var sa = new System.Text.StringBuilder(); foreach (var kv in byArea) sa.Append($"{kv.Key}+ m2: {kv.Value}; ");
                var ss = new System.Text.StringBuilder(); foreach (var kv in byStorey) ss.Append($"{kv.Key}: {kv.Value}; ");
                Line($"CLASS: {houses} footprints the exporter calls a house (style 3); {blocks} are blocks by the rule (area >= {CityMeshes.BlockMinAreaM2} m2 or h >= {CityMeshes.BlockMinH} m; {blocksGabled} of them were drawn as gables) - by area {sa}by storeys {ss}");

                // ---- the sample of tiles
                var tiles = new List<(int tx, int tz, string why)>();
                var seen = new HashSet<long>();
                void Add(int tx, int tz, string why) { long k = ((long)tx << 24) ^ (tz & 0xFFFFFF); if (seen.Add(k)) tiles.Add((tx, tz, why)); }
                string extra = System.Environment.GetEnvironmentVariable("PSX_WIN_TILES");
                if (!string.IsNullOrEmpty(extra))
                    foreach (var s in extra.Split(';'))
                    {
                        var p = s.Split(',');
                        if (p.Length >= 2 && float.TryParse(p[0], NumberStyles.Float, I, out float x) && float.TryParse(p[1], NumberStyles.Float, I, out float z))
                            Add(Mathf.FloorToInt(x / CityMeshes.TileSize), Mathf.FloorToInt(z / CityMeshes.TileSize), "shot place");
                    }
                int want = 64;
                if (int.TryParse(System.Environment.GetEnvironmentVariable("PSX_WIN_SAMPLE"), out int w) && w > 0) want = w;
                // two thirds from the tiles holding OSM house footprints (core), one third fill suburbs
                var fpTiles = new SortedDictionary<long, (int, int)>();
                foreach (var f in map.footprints)
                {
                    if (f == null || f.style != 3 || f.hidden) continue;
                    int tx = Mathf.FloorToInt(f.centre.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(f.centre.y / CityMeshes.TileSize);
                    long k = (((long)tx * 73856093L) ^ ((long)tz * 19349663L)) & 0x7FFFFFFFL;   // a hash order: spread over the city
                    fpTiles[(k << 20) ^ ((tx & 1023) << 10) ^ (tz & 1023)] = (tx, tz);
                }
                int coreWant = want * 2 / 3, n = 0;
                foreach (var kv in fpTiles) { if (n++ >= coreWant) break; Add(kv.Value.Item1, kv.Value.Item2, "OSM houses"); }
                int fillWant = want - coreWant, got = 0;
                for (int k = 0; k < 400 && got < fillWant; k++)
                {
                    float ang = k * 2.39996f, r = 3500f + (k * 977 % 7000);
                    var p = map.uptown + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    int tx = Mathf.FloorToInt(p.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(p.y / CityMeshes.TileSize);
                    var fill = CityHouses.FillOf(map, tx, tz);
                    if (fill == null || fill.Count < 8) continue;
                    Add(tx, tz, "fill suburb"); got++;
                }
                Line($"SAMPLE: {tiles.Count} tiles ({coreWant} with OSM houses, {got} fill suburbs, the rest the shot places)");

                // ---- per tile: build, read the FacadeHouse submesh back
                long whole = 0, cut = 0, buried = 0, tileTris = 0, houseTris = 0, roofTris = 0;
                int tilesWithHouses = 0;
                var worst = new List<string>();
                foreach (var t in tiles)
                {
                    CityMeshes.HouseWinLaid = 0; CityMeshes.HouseWinGroundDropped = 0;
                    var tm = CityMeshes.Build(map, trims, proc, t.tx, t.tz);
                    int tw = 0, tc = 0, tb = 0;
                    if (tm.buildings != null && tm.buildingSlots != null)
                    {
                        var mesh = tm.buildings;
                        var V = mesh.vertices; var U = mesh.uv;
                        for (int si = 0; si < tm.buildingSlots.Length; si++)
                        {
                            var tri = mesh.GetTriangles(si);
                            tileTris += tri.Length / 3;
                            if (tm.buildingSlots[si] == CityMeshes.Slot.RoofTiles) roofTris += tri.Length / 3;
                            if (tm.buildingSlots[si] != CityMeshes.Slot.FacadeHouse) continue;
                            houseTris += tri.Length / 3;
                            WindowCells(map, tm.origin, V, U, tri, ref tw, ref tc, ref tb, worst);
                        }
                    }
                    if (tw + tc + tb > 0) tilesWithHouses++;
                    whole += tw; cut += tc; buried += tb;
                    Line(string.Format(I, "  tile {0},{1} ({2}, {3}): windows whole {4}, CUT {5}, BURIED {6}; laid {7}, bands left plain for the ground {8}",
                        t.tx, t.tz, t.why, LatLon((t.tx + 0.5f) * CityMeshes.TileSize, (t.tz + 0.5f) * CityMeshes.TileSize), tw, tc, tb,
                        CityMeshes.HouseWinLaid, CityMeshes.HouseWinGroundDropped));
                    foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings, tm.structure, tm.medianPlants })
                        if (m != null) Object.DestroyImmediate(m);
                }
                Line(string.Format(I, "WINDOWS: {0} tiles ({1} with siding): whole {2}, CUT {3}, BURIED {4}; building triangles {5} (siding {6}, roof tiles {7}), {8:0} a tile",
                    tiles.Count, tilesWithHouses, whole, cut, buried, tileTris, houseTris, roofTris, tiles.Count > 0 ? (double)tileTris / tiles.Count : 0));
                for (int i = 0; i < Mathf.Min(25, worst.Count); i++) Line("    " + worst[i]);
                if (System.Environment.GetEnvironmentVariable("PSX_WIN_HOUSES") == "1") HouseCensusBoth(map, trims, proc, false);
                Line($"audit in {sw.ElapsedMilliseconds} ms");
                Line(cut + buried == 0 ? "WINDOWS OK" : $"WINDOWS CUT {cut + buried}");
            }
            catch (System.Exception ex) { Line("WINDOWS AUDIT FAILED " + ex); code = 1; }
            string tagW = System.Environment.GetEnvironmentVariable("PSX_BLDG_TAG");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, string.IsNullOrEmpty(tagW) ? "windows_audit.txt" : $"windows_audit_{tagW}.txt"), outLog.ToString());
            if (exit && Application.isBatchMode) EditorApplication.Exit(code);
            return code;
        }

        static readonly List<Vector2> winClip = new List<Vector2>(8), winTmp = new List<Vector2>(8);

        /// <summary>The window cells of one FacadeHouse submesh: whole, cut, buried.</summary>
        static void WindowCells(CityMap map, Vector3 origin, Vector3[] V, Vector2[] U, int[] tri,
                                ref int whole, ref int cut, ref int buried, List<string> worst)
        {
            const float u0 = CityMeshes.HouseWinU0, u1 = CityMeshes.HouseWinU1, v0 = CityMeshes.HouseWinV0, v1 = CityMeshes.HouseWinV1;
            float rectA = (u1 - u0) * (v1 - v0);
            // the planes: a wall or a gable is one plane, its UVs one frame
            var groups = new Dictionary<(int, int, int, int), List<int>>();
            var frames = new Dictionary<(int, int, int, int), List<(Vector2, List<int>)>>();
            for (int k = 0; k + 2 < tri.Length; k += 3)
            {
                Vector3 a = V[tri[k]], b = V[tri[k + 1]], c = V[tri[k + 2]];
                var nr = Vector3.Cross(b - a, c - a);
                if (nr.sqrMagnitude < 1e-10f) continue;
                nr.Normalize();
                float d = Vector3.Dot(nr, a);
                // the UV FRAME too: a gable on its end wall's plane is its own frame
                // (texture coordinates as an affine function of the plane's s, t)
                var e1 = Mathf.Abs(nr.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, nr).normalized;
                var e2 = Vector3.Cross(nr, e1);
                Vector2 q0 = new Vector2(Vector3.Dot(a, e1), Vector3.Dot(a, e2)), q1 = new Vector2(Vector3.Dot(b, e1), Vector3.Dot(b, e2)), q2 = new Vector2(Vector3.Dot(c, e1), Vector3.Dot(c, e2));
                Vector2 ua = U[tri[k]], ub = U[tri[k + 1]], uc = U[tri[k + 2]];
                float det = (q1.x - q0.x) * (q2.y - q0.y) - (q2.x - q0.x) * (q1.y - q0.y);
                if (Mathf.Abs(det) < 1e-8f) continue;
                Vector2 du = ((ub - ua) * (q2.y - q0.y) - (uc - ua) * (q1.y - q0.y)) / det;   // d(uv)/ds
                Vector2 dt = ((uc - ua) * (q1.x - q0.x) - (ub - ua) * (q2.x - q0.x)) / det;   // d(uv)/dt
                Vector2 off = ua - du * q0.x - dt * q0.y;                                      // uv at s = t = 0
                // (the offset by tolerance, not by rounding: one wall's panels share it to float noise)
                var key4 = (Mathf.RoundToInt(nr.x * 50f) * 1000 + Mathf.RoundToInt(nr.y * 50f), Mathf.RoundToInt(nr.z * 50f), Mathf.RoundToInt(d * 20f),
                            Mathf.RoundToInt(du.x * 500f) * 7919 + Mathf.RoundToInt(du.y * 500f) * 131 + Mathf.RoundToInt(dt.x * 500f) * 523 + Mathf.RoundToInt(dt.y * 500f) * 1031);
                if (!frames.TryGetValue(key4, out var fl)) frames[key4] = fl = new List<(Vector2, List<int>)>();
                List<int> l = null;
                foreach (var fr in fl) if (Mathf.Abs(fr.Item1.x - off.x) < 0.03f && Mathf.Abs(fr.Item1.y - off.y) < 0.03f) { l = fr.Item2; break; }
                if (l == null) { l = new List<int>(); fl.Add((off, l)); groups[(groups.Count, 0, 0, 0)] = l; }
                l.Add(k);
            }
            foreach (var g in groups.Values)
            {
                var cover = new Dictionary<(int, int), float>();
                var holder = new Dictionary<(int, int), int>();
                foreach (int k in g)
                {
                    Vector2 a = U[tri[k]], b = U[tri[k + 1]], c = U[tri[k + 2]];
                    float uMin = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), uMax = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float vMin = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), vMax = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    for (int i = Mathf.FloorToInt(uMin) - 1; i <= Mathf.FloorToInt(uMax); i++)
                        for (int j = Mathf.FloorToInt(vMin) - 1; j <= Mathf.FloorToInt(vMax); j++)
                        {
                            float rx0 = i + u0, rx1 = i + u1, ry0 = j + v0, ry1 = j + v1;
                            if (rx1 <= uMin || rx0 >= uMax || ry1 <= vMin || ry0 >= vMax) continue;
                            float ar = ClipTriRect(a, b, c, rx0, ry0, rx1, ry1);
                            if (ar < 1e-6f) continue;
                            cover.TryGetValue((i, j), out float s); cover[(i, j)] = s + ar;
                            if (!holder.ContainsKey((i, j))) holder[(i, j)] = k;
                        }
                }
                foreach (var kv in cover)
                {
                    float frac = kv.Value / rectA;
                    if (frac < 0.985f)
                    {
                        if (frac < 0.01f) continue;   // a corner's touch of a frame pixel
                        cut++;
                        if (worst.Count < 60)
                        {
                            int k = holder[kv.Key];
                            var p = V[tri[k]] + origin;
                            worst.Add(string.Format(CultureInfo.InvariantCulture, "CUT window cell {0},{1} {2:0}% shown, wall at ({3:0.0}, {4:0.0}, {5:0.0}) {6}",
                                kv.Key.Item1, kv.Key.Item2, frac * 100f, p.x, p.y, p.z, LatLon(p.x, p.z)));
                        }
                        continue;
                    }
                    // whole: is its sill under the drawn ground?
                    var sillUv = new Vector2(kv.Key.Item1 + 0.5f, kv.Key.Item2 + v0 + 0.01f);
                    bool found = false; Vector3 at = default;
                    foreach (int k in g)
                    {
                        Vector2 a = U[tri[k]], b = U[tri[k + 1]], c = U[tri[k + 2]];
                        if (!Bary(a, b, c, sillUv, out float wa, out float wb, out float wc)) continue;
                        at = V[tri[k]] * wa + V[tri[k + 1]] * wb + V[tri[k + 2]] * wc + origin;
                        found = true; break;
                    }
                    if (found && CityMeshes.LatticeAt(map, at.x, at.z) > at.y + 0.02f)
                    {
                        buried++;
                        if (worst.Count < 60) worst.Add(string.Format(CultureInfo.InvariantCulture, "BURIED window, sill at ({0:0.0}, {1:0.00}, {2:0.0}) {3}, ground {4:0.00}",
                            at.x, at.y, at.z, LatLon(at.x, at.z), CityMeshes.LatticeAt(map, at.x, at.z)));
                    }
                    else whole++;
                }
            }
        }

        static bool Bary(Vector2 a, Vector2 b, Vector2 c, Vector2 p, out float wa, out float wb, out float wc)
        {
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            wa = wb = wc = 0f;
            if (Mathf.Abs(d) < 1e-9f) return false;
            wa = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
            wb = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
            wc = 1f - wa - wb;
            return wa >= -1e-4f && wb >= -1e-4f && wc >= -1e-4f;
        }

        /// <summary>Area of triangle abc (UV space) inside the rectangle.</summary>
        static float ClipTriRect(Vector2 a, Vector2 b, Vector2 c, float x0, float y0, float x1, float y1)
        {
            winClip.Clear(); winClip.Add(a); winClip.Add(b); winClip.Add(c);
            ClipHalf(0, x0, 1f); ClipHalf(0, x1, -1f); ClipHalf(1, y0, 1f); ClipHalf(1, y1, -1f);
            float ar = 0f;
            for (int i = 0; i < winClip.Count; i++) { var p = winClip[i]; var q = winClip[(i + 1) % winClip.Count]; ar += p.x * q.y - q.x * p.y; }
            return Mathf.Abs(ar) * 0.5f;
        }

        /// <summary>Keep the part of winClip where sign * (axis - at) >= 0.</summary>
        static void ClipHalf(int axis, float at, float sign)
        {
            winTmp.Clear();
            int n = winClip.Count;
            for (int i = 0; i < n; i++)
            {
                var p = winClip[i]; var q = winClip[(i + 1) % n];
                float dp = sign * ((axis == 0 ? p.x : p.y) - at), dq = sign * ((axis == 0 ? q.x : q.y) - at);
                if (dp >= 0f) winTmp.Add(p);
                if ((dp >= 0f) != (dq >= 0f)) winTmp.Add(Vector2.Lerp(p, q, dp / (dp - dq)));
            }
            winClip.Clear(); winClip.AddRange(winTmp);
        }
    }
}
