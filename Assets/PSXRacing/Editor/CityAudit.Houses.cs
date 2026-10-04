using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// HOUSES (leftover item 6, 2026-10-03; the owner: "houses lack driveways
    /// and many houses are above ground with their concrete foundations").
    /// Every house in a box, measured on the ground the tile DREW (its
    /// lattice, read right after the tile's build):
    ///   kinds   a real footprint's gabled box (1) or house polygon (2), a
    ///           frontage gable (3), a fill house (4) - all procedural, siding
    ///           to the ground - and the prefab house (5) and trailers (6),
    ///           which stand on the baked concrete skirt
    ///   fall    the drawn ground's high minus low along the walls (every
    ///           metre) and at every lattice corner inside
    ///   EXPOSED FOUNDATION (prefabs): the model's lowest course (its pivot
    ///           sits the def's sink under the seat) down to the lowest ground
    ///           at its walls - the concrete a driver sees on the downhill side
    ///   BURIED (procedural): the drawn ground over the floor line at its
    ///           highest; GAP: the wall bottom over the ground anywhere
    ///   DRIVEWAYS: the house's driveway, if any
    /// Headless on its own: -executeMethod PSXRacing.EditorTools.CityAudit.HousesOnly
    /// (houses_audit.txt). PSX_HOUSE_SUBURB_BOX=x0,z0,x1,z1 sets the suburban box
    /// (default: the 1.5 km round the 1 km cell with the most prefab houses
    /// on falling lots, from a city-wide scan of the natural ground).
    /// </summary>
    public static partial class CityAudit
    {
        static readonly string[] HouseKindNames = { "?", "osm gable", "osm house polygon", "frontage gable", "fill", "prefab house", "trailer" };

        sealed class HouseRow
        {
            public int kind; public Vector2 c, u; public float hu, hv;
            public float lo, hi, fall, expose, bury, gapM, roadDist; public int roadEdge = -1;
            public bool dropped;
        }

        sealed class HouseBoxResult
        {
            public string name; public Rect box;
            public readonly List<HouseRow> rows = new List<HouseRow>();
            public int tiles, dropped; public double buildMs;
            public readonly List<double> tileMs = new List<double>();
            public int driveConflicts; public readonly List<string> conflictRows = new List<string>();
            // driveways, over CityHouses' own table (standing homes only)
            public int homes, drives, objectsChecked; public long pavePieces; public float paveM2;
            public readonly int[] homesByKind = new int[7], drivesByKind = new int[7], streetByKind = new int[7];
            public readonly List<float> driveLen = new List<float>();
            public readonly Dictionary<string, int> why = new Dictionary<string, int>();
            public readonly Dictionary<string, int> whyKind = new Dictionary<string, int>();
            public readonly List<string> blockRows = new List<string>();
        }

        /// <summary>The box the suburban census runs in (see the class).</summary>
        static Rect HouseSuburbBox(CityMap map, Dictionary<long, List<CityBuildings.B>> proc, List<string> log)
        {
            string s = System.Environment.GetEnvironmentVariable("PSX_HOUSE_SUBURB_BOX");
            if (!string.IsNullOrWhiteSpace(s) && TryParseBox(s, out var b)) { log.Add($"suburban box from PSX_HOUSE_SUBURB_BOX={s}"); return b; }
            // the natural ground (GroundY, no tile build) at each prefab's
            // corners and centre: which 1 km cells hold the most falling lots
            var cells = new Dictionary<long, int>();
            int prefabs = 0, falling = 0;
            foreach (var kv in proc)
                foreach (var pb in kv.Value)
                {
                    if (!CityProps.IsHome(pb.kind)) continue;
                    prefabs++;
                    float cy = Mathf.Cos(pb.yaw), sy = Mathf.Sin(pb.yaw);
                    var hw = new Vector2(cy, -sy) * (pb.w * 0.5f); var hd = new Vector2(sy, cy) * (pb.d * 0.5f);
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var q in new[] { pb.pos, pb.pos + hw + hd, pb.pos - hw + hd, pb.pos - hw - hd, pb.pos + hw - hd })
                    {
                        float g = CityElevation.GroundY(map, q.x, q.y);
                        lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                    }
                    if (hi - lo < 1f) continue;
                    falling++;
                    long k = ((long)Mathf.FloorToInt(pb.pos.x / 1000f) << 24) ^ (Mathf.FloorToInt(pb.pos.y / 1000f) & 0xFFFFFF);
                    cells.TryGetValue(k, out int n); cells[k] = n + 1;
                }
            long best = 0; int bestN = -1;
            foreach (var kv in cells) if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best)) { bestN = kv.Value; best = kv.Key; }
            int cx = (int)(best >> 24), cz = (int)(((best & 0xFFFFFF) ^ 0x800000) - 0x800000);
            var box = Rect.MinMaxRect(cx * 1000f - 250f, cz * 1000f - 250f, cx * 1000f + 1250f, cz * 1000f + 1250f);
            log.Add(string.Format(CultureInfo.InvariantCulture, "suburban box (city scan of the natural ground): {0} prefab homes, {1} on lots falling 1 m or more; " +
                "the most in the 1 km cell ({2:0},{3:0}) ({4}), box x {5:0}..{6:0}, z {7:0}..{8:0} {9}",
                prefabs, falling, cx * 1000f, cz * 1000f, bestN, box.xMin, box.xMax, box.yMin, box.yMax, LatLon(box.center.x, box.center.y)));
            return box;
        }

        /// <summary>Lowest and highest drawn ground along an oriented box's
        /// walls (every metre at most) and at every lattice corner inside it.</summary>
        static void HouseGroundRange(CityMap map, Vector2 c, Vector2 u, float hu, float hv, out float lo, out float hi)
        {
            var v = new Vector2(-u.y, u.x);
            lo = float.MaxValue; hi = float.MinValue;
            var k = new[] { c + u * hu + v * hv, c - u * hu + v * hv, c - u * hu - v * hv, c + u * hu - v * hv };
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = k[i], b = k[(i + 1) % 4];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int j = 0; j < n; j++)
                {
                    var p = Vector2.Lerp(a, b, j / (float)n);
                    float g = CityMeshes.LatticeAt(map, p.x, p.y);
                    if (g < lo) lo = g; if (g > hi) hi = g;
                }
            }
            float cell = CityMeshes.TileSize / CityMeshes.GroundRes;
            float r = Mathf.Sqrt(hu * hu + hv * hv);
            for (int z = Mathf.CeilToInt((c.y - r) / cell); z <= Mathf.FloorToInt((c.y + r) / cell); z++)
                for (int x = Mathf.CeilToInt((c.x - r) / cell); x <= Mathf.FloorToInt((c.x + r) / cell); x++)
                {
                    var q = new Vector2(x * cell, z * cell) - c;
                    if (Mathf.Abs(Vector2.Dot(q, u)) > hu || Mathf.Abs(Vector2.Dot(q, v)) > hv) continue;
                    float g = CityMeshes.LatticeAt(map, x * cell, z * cell);
                    if (g < lo) lo = g; if (g > hi) hi = g;
                }
        }

        static HouseBoxResult HouseCensus(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> proc, string name, Rect box)
        {
            var R = new HouseBoxResult { name = name, box = box };
            int tx0 = Mathf.FloorToInt(box.xMin / CityMeshes.TileSize), tx1 = Mathf.FloorToInt((box.xMax - 0.01f) / CityMeshes.TileSize);
            int tz0 = Mathf.FloorToInt(box.yMin / CityMeshes.TileSize), tz1 = Mathf.FloorToInt((box.yMax - 0.01f) / CityMeshes.TileSize);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CityMeshes.DriveStats.Reset();
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var t0 = System.Diagnostics.Stopwatch.StartNew();
                    var tm = CityMeshes.Build(map, trims, proc, tx, tz);
                    R.tileMs.Add(t0.Elapsed.TotalMilliseconds);
                    R.tiles++;
                    foreach (var hs in tm.houseSeats)
                    {
                        if (!box.Contains(hs.c)) continue;
                        var row = new HouseRow { kind = hs.kind, c = hs.c, u = hs.u, hu = hs.hu, hv = hs.hv };
                        HouseGroundRange(map, hs.c, hs.u, hs.hu, hs.hv, out row.lo, out row.hi);
                        row.fall = row.hi - row.lo;
                        row.bury = row.hi - hs.floor;
                        row.gapM = hs.y0 - row.lo;
                        R.rows.Add(row);
                    }
                    long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
                    if (proc.TryGetValue(key, out var lots))
                        foreach (var b in lots)
                        {
                            if (!CityProps.IsHome(b.kind) || !box.Contains(b.pos)) continue;
                            var def = CityProps.Defs[b.kind];
                            float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                            var row = new HouseRow { kind = b.kind == CityProps.House ? 5 : 6, c = b.pos, u = new Vector2(cy, -sy), hu = b.w * 0.5f, hv = b.d * 0.5f };
                            bool stands = CityBuildings.PropSeat(map, b, def, out float seat, out _);
                            HouseGroundRange(map, b.pos, row.u, row.hu, row.hv, out row.lo, out row.hi);
                            row.fall = row.hi - row.lo;
                            row.dropped = !stands;
                            if (!stands) { R.dropped++; }
                            float pivot = seat - def.sink;
                            row.expose = pivot + def.baseM - row.lo;
                            row.bury = row.hi - (pivot + def.baseM);
                            row.gapM = (pivot - CityProps.SkirtDepthM) - row.lo;
                            R.rows.Add(row);
                        }
                    HouseDriveways(map, trims, proc, tm, tx, tz, R);
                    foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.guardrails, tm.kerbs, tm.lampPosts, tm.banks, tm.water, tm.buildings })
                        if (m != null) Object.DestroyImmediate(m);
                }
            R.buildMs = sw.Elapsed.TotalMilliseconds;
            R.pavePieces = CityMeshes.DriveStats.pieces; R.paveM2 = CityMeshes.DriveStats.m2;
            foreach (var row in R.rows)
            {
                if (map.NearestRoadPoint(row.c, 250f, true, out int ei, out _, out float d)) { row.roadEdge = ei; row.roadDist = d; }
                else row.roadDist = float.PositiveInfinity;
            }
            return R;
        }

        /// <summary>Driveway stats for the houses a tile owns, and every
        /// pole, sign post, signal pole, STOP sign, street lamp and tree the
        /// tile stands, tested against the driveways that reach it.</summary>
        static void HouseDriveways(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> proc,
                                   CityMeshes.TileMeshes tm, int tx, int tz, HouseBoxResult R)
        {
            var I = CultureInfo.InvariantCulture;
            foreach (var h in CityHouses.HousesOf(map, trims, proc, tx, tz))
            {
                if (!h.IsHome || !R.box.Contains(h.c)) continue;
                if (h.kind >= 5 && R.rows.Exists(r => r.kind >= 5 && r.dropped && (r.c - h.c).sqrMagnitude < 1e-4f)) continue;
                R.homes++; R.homesByKind[h.kind]++;
                var d = CityHouses.DrivewayOf(h);
                if (d != null) { R.drives++; R.drivesByKind[h.kind]++; R.driveLen.Add(d.len); }
                else
                {
                    string w = h.why ?? "?"; R.why.TryGetValue(w, out int c); R.why[w] = c + 1;
                    string wk = HouseKindNames[h.kind] + ": " + w; R.whyKind.TryGetValue(wk, out int ck); R.whyKind[wk] = ck + 1;
                    if (h.blockedBy != null && R.blockRows.Count < 15)
                        R.blockRows.Add(string.Format(I, "{0} at ({1:0.0}, {2:0.0}) {3} ({4:0.0} x {5:0.0} m): {6}; first blocked by {7}",
                            HouseKindNames[h.kind], h.c.x, h.c.y, LatLon(h.c.x, h.c.y), 2f * h.hu, 2f * h.hv, w, h.blockedBy));
                }
                if (d != null || (h.why != "no street within reach" && h.why != "street too far")) R.streetByKind[h.kind]++;
            }
            if (!CityHouses.DrivewaysOn) return;
            var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            var drives = new List<CityHouses.Driveway>();
            CityHouses.DrivewaysNearBound(map, min, min + Vector2.one * CityMeshes.TileSize, drives);
            if (drives.Count == 0) return;
            bool treesWas = CityTrees.Enabled;
            CityTrees.Enabled = true;
            var tt = CityTrees.Build(map, trims, proc, tm, tx, tz);
            CityTrees.Enabled = treesWas;
            void Test(Vector2 p, float r, string what)
            {
                R.objectsChecked++;
                if (!CityHouses.OnAny(drives, p, r, false)) return;
                R.driveConflicts++;
                if (R.conflictRows.Count < 10) R.conflictRows.Add(string.Format(I, "{0} at ({1:0.0}, {2:0.0}) {3} on a driveway", what, p.x, p.y, LatLon(p.x, p.y)));
            }
            foreach (var t in tt.trees) Test(new Vector2(t.foot.x, t.foot.z), t.r, "tree");
            if (tt.poles != null) foreach (var p in tt.poles.poles) Test(new Vector2(p.foot.x, p.foot.z), 0.15f, "utility pole");
            if (tt.signs != null) foreach (var p in tt.signs.posts) Test(new Vector2(p.centre.x, p.centre.z), 0.5f * Mathf.Max(p.size.x, p.size.z), "sign post");
            if (tt.signals != null)
            {
                foreach (var p in tt.signals.poles) Test(new Vector2(p.foot.x, p.foot.z), 0.16f, "signal pole");
                foreach (var s in tt.signals.stops) Test(new Vector2(s.foot.x, s.foot.z), 0.05f, "STOP sign");
            }
            foreach (var l in tm.lamps) Test(new Vector2(l.foot.x + tm.origin.x, l.foot.z + tm.origin.z), 0.12f, "street lamp");
            foreach (var m in new Mesh[] { tt.mesh, tt.poles?.mesh, tt.signs?.mesh, tt.signals?.mesh, tt.signals?.lamps, tt.signals?.halos })
                if (m != null) Object.DestroyImmediate(m);
        }

        static float Pct(List<float> v, float p)
        {
            if (v.Count == 0) return float.NaN;
            v.Sort();
            return v[Mathf.Clamp(Mathf.CeilToInt(p * v.Count) - 1, 0, v.Count - 1)];
        }

        static void HouseBoxReport(CityMap map, HouseBoxResult R, bool gate)
        {
            var I = CultureInfo.InvariantCulture;
            var byKind = new int[7];
            foreach (var r in R.rows) byKind[r.kind]++;
            int homes = R.rows.Count;
            R.tileMs.Sort();
            Line(string.Format(I, "HOUSES {0} (x {1:0}..{2:0}, z {3:0}..{4:0}, {5}): {6} houses - {7} {8}, {9} {10}, {11} {12}, {13} {14}, {15} {16}, {17} {18}; " +
                "prefab lots left empty (ground falls past the skirt) {19}; {20} tiles built, p50 {21:0.0} / max {22:0.0} ms",
                R.name, R.box.xMin, R.box.xMax, R.box.yMin, R.box.yMax, LatLon(R.box.center.x, R.box.center.y), homes,
                byKind[1], HouseKindNames[1], byKind[2], HouseKindNames[2], byKind[3], HouseKindNames[3], byKind[4], HouseKindNames[4],
                byKind[5], HouseKindNames[5], byKind[6], HouseKindNames[6], R.dropped, R.tiles,
                R.tileMs.Count > 0 ? R.tileMs[R.tileMs.Count / 2] : 0, R.tileMs.Count > 0 ? R.tileMs[R.tileMs.Count - 1] : 0));
            // fall, by group
            var fallP = new List<float>(); var fallS = new List<float>();
            var expose = new List<float>(); var bury = new List<float>();
            int gaps = 0, over06 = 0, over10 = 0; float gapWorst = 0f;
            foreach (var r in R.rows)
            {
                bool prefab = r.kind >= 5;
                if (prefab && r.dropped) continue;
                (prefab ? fallP : fallS).Add(r.fall);
                if (prefab)
                {
                    expose.Add(r.expose);
                    if (r.expose > 0.6f) over06++;
                    if (r.expose > 1.0f) over10++;
                }
                else bury.Add(r.bury);
                if (r.gapM > 0.02f) { gaps++; gapWorst = Mathf.Max(gapWorst, r.gapM); }
            }
            Line(string.Format(I, "  fall across the lot (drawn ground, high - low at the walls): procedural houses p50 {0:0.00} / p95 {1:0.00} / max {2:0.00} m; prefab homes p50 {3:0.00} / p95 {4:0.00} / max {5:0.00} m",
                Pct(fallS, 0.5f), Pct(fallS, 0.95f), Pct(fallS, 1f), Pct(fallP, 0.5f), Pct(fallP, 0.95f), Pct(fallP, 1f)));
            Line(string.Format(I, "  EXPOSED FOUNDATION (prefab homes standing: the model's lowest course down to the lowest ground at its walls): {0} homes, p50 {1:0.00} / p95 {2:0.00} / max {3:0.00} m; over 0.6 m {4}, over 1.0 m {5}",
                expose.Count, Pct(expose, 0.5f), Pct(expose, 0.95f), Pct(expose, 1f), over06, over10));
            Line(string.Format(I, "  BURIED (procedural houses: ground over the floor line at its highest; the siding runs into the ground): p50 {0:0.00} / p95 {1:0.00} / max {2:0.00} m",
                Pct(bury, 0.5f), Pct(bury, 0.95f), Pct(bury, 1f)));
            Line(string.Format(I, "  GAP (daylight under a house: wall bottom or skirt bottom over the ground anywhere): {0} houses (worst {1:0.00} m)", gaps, gapWorst));
            // street access
            var dist = new List<float>(); int within45 = 0, far = 0;
            foreach (var r in R.rows) { dist.Add(r.roadDist); if (r.roadDist <= 45f) within45++; if (float.IsInfinity(r.roadDist)) far++; }
            Line(string.Format(I, "  nearest street (centre to centreline, links skipped): p50 {0:0} / p95 {1:0} m; within 45 m {2} of {3}; none within 250 m {4}",
                Pct(dist, 0.5f), Pct(dist, 0.95f), within45, homes, far));
            for (int k = 1; k <= 6; k++)
            {
                var dk = new List<float>(); int w45 = 0, n = 0;
                foreach (var r in R.rows) if (r.kind == k) { n++; dk.Add(r.roadDist); if (r.roadDist <= 45f) w45++; }
                if (n > 0) Line(string.Format(I, "    {0}: {1}, nearest street p50 {2:0} / p95 {3:0} m, within 45 m {4}", HouseKindNames[k], n, Pct(dk, 0.5f), Pct(dk, 0.95f), w45));
            }
            // driveways
            int drives = R.drives, standing = R.homes;
            var whyS = new System.Text.StringBuilder();
            foreach (var kv in R.why) whyS.Append($"{kv.Key} {kv.Value}; ");
            int withStreet = 0; foreach (var n in R.streetByKind) withStreet += n;
            Line(string.Format(I, "  DRIVEWAYS ({7}): {0} of {1} standing houses ({2:0.0} %; {8} of the {9} with a mapped street within reach, {10:0.0} %), length p50 {3:0.0} / max {4:0.0} m; without: {5}; " +
                "furniture standing on a driveway (trees, utility poles, sign posts, signal poles, STOP signs, street lamps): {6} of {11} checked",
                drives, standing, standing > 0 ? 100.0 * drives / standing : 0.0, Pct(R.driveLen, 0.5f), Pct(R.driveLen, 1f), whyS, R.driveConflicts,
                CityHouses.DrivewaysOn ? "ON" : "OFF (PSX_CITY_DRIVEWAYS=0)", drives, withStreet, withStreet > 0 ? 100.0 * drives / withStreet : 0.0, R.objectsChecked));
            for (int k = 1; k <= 6; k++)
                if (R.homesByKind[k] > 0) Line(string.Format(I, "    {0}: {1} of {2} ({3} with a street within reach)", HouseKindNames[k], R.drivesByKind[k], R.homesByKind[k], R.streetByKind[k]));
            Line(string.Format(I, "    drawn: {0} concrete pieces cut into the ground cells of the tiles built, {1:0} m2", R.pavePieces, R.paveM2));
            var wk = new System.Text.StringBuilder();
            foreach (var kv in R.whyKind) wk.Append($"{kv.Key} {kv.Value}; ");
            Line("    without, by kind: " + wk);
            foreach (var b in R.blockRows) Line("      " + b);
            foreach (var c in R.conflictRows) Line("    CONFLICT " + c);
            // the worst, for the photographs
            var pre = R.rows.FindAll(r => r.kind >= 5 && !r.dropped);
            pre.Sort((a, b) => b.expose.CompareTo(a.expose));
            for (int i = 0; i < Mathf.Min(8, pre.Count); i++)
            {
                var r = pre[i];
                string road = r.roadEdge >= 0 ? $"e{r.roadEdge} '{map.edges[r.roadEdge].name}' cls{map.edges[r.roadEdge].cls} {r.roadDist:0} m" : "no road";
                Line(string.Format(I, "    worst exposed {0} at ({1:0.0}, {2:0.0}) {3}: {4:0.00} m (fall {5:0.00}); {6}", HouseKindNames[r.kind], r.c.x, r.c.y, LatLon(r.c.x, r.c.y), r.expose, r.fall, road));
                if (i < 3 && System.Environment.GetEnvironmentVariable("PSX_HOUSE_EXPLAIN") == "1")
                {
                    // the lattice corners in and round the lot: what graded each
                    var v = new Vector2(-r.u.y, r.u.x);
                    float cell = CityMeshes.TileSize / CityMeshes.GroundRes, reach = Mathf.Sqrt(r.hu * r.hu + r.hv * r.hv) + 8f;
                    for (int z = Mathf.CeilToInt((r.c.y - reach) / cell); z <= Mathf.FloorToInt((r.c.y + reach) / cell); z++)
                        for (int x = Mathf.CeilToInt((r.c.x - reach) / cell); x <= Mathf.FloorToInt((r.c.x + reach) / cell); x++)
                        {
                            var q = new Vector2(x * cell, z * cell) - r.c;
                            float du = Vector2.Dot(q, r.u), dv = Vector2.Dot(q, v);
                            if (Mathf.Abs(du) > r.hu + 8f || Mathf.Abs(dv) > r.hv + 8f) continue;
                            Line(string.Format(I, "      corner u {0:0.0} v {1:0.0}{2}: drawn {3:0.00}; {4}", du, dv,
                                Mathf.Abs(du) <= r.hu && Mathf.Abs(dv) <= r.hv ? " IN" : "", CityMeshes.LatticeAt(map, x * cell, z * cell), CityHouses.Explain(map, x, z)));
                        }
                }
            }
            var pro = R.rows.FindAll(r => r.kind < 5);
            pro.Sort((a, b) => b.bury.CompareTo(a.bury));
            for (int i = 0; i < Mathf.Min(5, pro.Count); i++)
            {
                var r = pro[i];
                string road = r.roadEdge >= 0 ? $"e{r.roadEdge} '{map.edges[r.roadEdge].name}' cls{map.edges[r.roadEdge].cls} {r.roadDist:0} m" : "no road";
                Line(string.Format(I, "    most buried {0} at ({1:0.0}, {2:0.0}) {3}: {4:0.00} m (fall {5:0.00}, gap {6:0.00}); {7}", HouseKindNames[r.kind], r.c.x, r.c.y, LatLon(r.c.x, r.c.y), r.bury, r.fall, r.gapM, road));
            }
            // streets for the photographs: the houses grouped by their street
            var byEdge = new Dictionary<int, List<HouseRow>>();
            foreach (var r in R.rows)
            {
                if (r.roadEdge < 0 || r.roadDist > 40f || r.dropped) continue;
                if (!byEdge.TryGetValue(r.roadEdge, out var l)) byEdge[r.roadEdge] = l = new List<HouseRow>();
                l.Add(r);
            }
            var streets = new List<(int e, int n, float mean, float meanFall)>();
            foreach (var kv in byEdge)
            {
                if (kv.Value.Count < 5) continue;
                float m = 0f, mf = 0f;
                foreach (var r in kv.Value) { m += r.kind >= 5 ? r.expose : r.bury; mf += r.fall; }
                streets.Add((kv.Key, kv.Value.Count, m / kv.Value.Count, mf / kv.Value.Count));
            }
            streets.Sort((a, b) => b.meanFall.CompareTo(a.meanFall));
            foreach (var pass in new[] { 0, 1 })
            {
                var list = new List<(int e, int n, float mean, float meanFall)>(streets);
                if (pass == 1) list.Sort((a, b) => a.meanFall.CompareTo(b.meanFall));
                for (int i = 0; i < Mathf.Min(4, list.Count); i++)
                {
                    var s = list[i]; var e = map.edges[s.e];
                    var mid = e.PointAt(e.length * 0.5f); var t = e.TangentAt(e.length * 0.5f);
                    float hdg = Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg; if (hdg < 0f) hdg += 360f;
                    Line(string.Format(I, "    {0} street e{1} '{2}' cls{3} len {4:0}: {5} houses, mean fall {6:0.00} m, mean exposed/buried {7:0.00} m; mid ({8:0.0}, {9:0.0}) heading {10:0} y {11:0.0}",
                        pass == 0 ? "SLOPED" : "FLAT", s.e, e.name, e.cls, e.length, s.n, s.meanFall, s.mean, mid.x, mid.y, hdg, e.YAt(e.length * 0.5f)));
                }
            }
            if (gate)
            {
                Check(over10 == 0 || Pct(expose, 1f) <= 1.0f, $"HOUSES {R.name}: no prefab home shows more than 1.0 m of foundation (leftover item 6)", string.Format(I, "max {0:0.00} m, {1} over", Pct(expose, 1f), over10));
                Check(expose.Count == 0 || Pct(expose, 0.95f) <= 0.6f, $"HOUSES {R.name}: exposed foundation p95 <= 0.6 m (leftover item 6)", string.Format(I, "p95 {0:0.00} m", Pct(expose, 0.95f)));
                Check(gaps == 0, $"HOUSES {R.name}: no house with daylight under it (leftover item 6)", gaps);
                Check(standing == 0 || drives >= 0.95f * standing, $"HOUSES {R.name}: 95 % of houses have a driveway (leftover item 6)", $"{drives} of {standing}");
                Check(R.driveConflicts == 0, $"HOUSES {R.name}: no driveway through a lot, building, tree, pole, sign, signal, culvert or junction mouth (leftover item 6)", R.driveConflicts);
            }
        }

        /// <summary>The house census in the default box and the suburban box
        /// (the full audit's HOUSES block).</summary>
        static void HouseCensusBoth(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> proc, bool gate)
        {
            var log = new List<string>();
            var sub = HouseSuburbBox(map, proc, log);
            foreach (var l in log) Line("HOUSES " + l);
            var sc = ScopeFor("HOUSE");
            HouseBoxReport(map, HouseCensus(map, trims, proc, "owner box", sc.Full ? OwnerBox : sc.Box), gate);
            HouseBoxReport(map, HouseCensus(map, trims, proc, "suburban box", sub), gate);
        }

        /// <summary>THE HOUSES REPORT ALONE (leftover item 6): houses_audit.txt.</summary>
        public static void HousesOnly()
        {
            outLog = new System.Text.StringBuilder();
            var map = CityMap.Get();
            if (map == null) Line("charlotte_city.bytes missing from Resources");
            else
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var trims = CityMeshes.NodeTrims(map);
                var proc = CityBuildings.Precompute(map);
                Line($"trims + buildings in {sw.ElapsedMilliseconds} ms");
                HouseCensusBoth(map, trims, proc, false);
                Line($"houses report in {sw.ElapsedMilliseconds} ms");
                // PSX_HOUSE_SHOTS=1: the named views PSX_PREVIEW_SPOTS asks for,
                // in the same job (the photographs of the build just measured)
                if (System.Environment.GetEnvironmentVariable("PSX_HOUSE_SHOTS") == "1") CityPreview.Run();
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "houses_audit.txt"), outLog.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
