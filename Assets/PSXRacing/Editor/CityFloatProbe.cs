using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// FLOATING TIERS CENSUS (2026-10-26; the owner on South Tryon Street: "I
    /// see floating buildings"). Builds every tile that holds a floored
    /// building:part (and its eight neighbours) the way the game does, with
    /// <see cref="CityMeshes.PartFillDown"/> at each mode in PSX_FLOAT_MODES
    /// (default "0,1"), and reads <see cref="CityMeshes.FootAudit"/>: a
    /// floored part HANGS where, inside one of its walls, nothing drawn under
    /// it (any footprint, at its drawn plan and height) reaches its floor; it
    /// is fixed where that wall runs to the ground. Each hanging wall is
    /// classed OSM (nothing mapped under it in the data either: a canopy, a
    /// bridge, a cantilever) or DRAWN (mapped, but cut away or left out by
    /// the tile). Writes city_float.txt; then, when PSX_ROAD_SPOTS is set,
    /// CityPreview.RunRoadSpots once per mode (tag fill&lt;mode&gt;).
    /// Headless: -executeMethod PSXRacing.EditorTools.CityFloatProbe.Run
    /// </summary>
    public static class CityFloatProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { File.WriteAllText("city_float.txt", "no map\n"); return; }
            var trims = CityMeshes.NodeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var fs = map.footprints;
            var tiles = new HashSet<long>();
            var tileList = new List<Vector2Int>();
            int floored = 0;
            for (int i = 0; i < fs.Length; i++)
            {
                var f = fs[i];
                if (f.hidden || !f.part || f.minH <= 0.05f) continue;
                floored++;
                int tx = Mathf.FloorToInt(f.centre.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(f.centre.y / CityMeshes.TileSize);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (tiles.Add(((long)(tx + dx) << 24) ^ ((tz + dz) & 0xFFFFFF))) tileList.Add(new Vector2Int(tx + dx, tz + dz));
            }
            sb.AppendLine($"floored parts {floored} on {tileList.Count} tiles (with neighbours)");
            string modes = System.Environment.GetEnvironmentVariable("PSX_FLOAT_MODES") ?? "0,1";
            int keep = CityMeshes.PartFillDown;
            try
            {
                foreach (var ms in modes.Split(','))
                {
                    if (!int.TryParse(ms.Trim(), out int mode)) continue;
                    CityMeshes.PartFillDown = mode;
                    CityMeshes.FootAudit = new List<CityMeshes.FootRec>();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var t in tileList)
                    {
                        var tm = CityMeshes.Build(map, trims, buildings, t.x, t.y);
                        foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings, tm.buildingCollider, tm.lampPosts, tm.guardrails })
                            if (m != null) Object.DestroyImmediate(m);
                    }
                    sb.AppendLine($"== MODE {mode}: {tileList.Count} tiles built in {clock.ElapsedMilliseconds} ms, {CityMeshes.FootAudit.Count} footprints recorded");
                    Census(map, CityMeshes.FootAudit, sb);
                }
            }
            finally { CityMeshes.FootAudit = null; CityMeshes.PartFillDown = keep; }
            File.WriteAllText("city_float.txt", sb.ToString());
            Debug.Log("[CityFloat] " + sb.ToString().Replace("\n", " | "));

            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PSX_ROAD_SPOTS")))
            {
                try
                {
                    foreach (var ms in modes.Split(','))
                    {
                        if (!int.TryParse(ms.Trim(), out int mode)) continue;
                        CityMeshes.PartFillDown = mode;
                        System.Environment.SetEnvironmentVariable("PSX_ROAD_TAG", "fill" + mode);
                        CityPreview.RunRoadSpots();
                    }
                }
                finally { CityMeshes.PartFillDown = keep; }
            }
        }

        struct Col { public Vector2[] plan; public float b, t; }

        static void Census(CityMap map, List<CityMeshes.FootRec> recs, StringBuilder sb)
        {
            var fs = map.footprints;
            var byFi = new Dictionary<int, CityMeshes.FootRec>();
            foreach (var r in recs) byFi[r.fi] = r;
            // drawn columns in a 32 m grid
            const float Cell = 32f;
            var grid = new Dictionary<long, List<Col>>();
            long Key(int cx, int cz) => ((long)cx << 24) ^ (cz & 0xFFFFFF);
            void Add(Col c)
            {
                Vector2 lo = c.plan[0], hi = c.plan[0];
                foreach (var p in c.plan) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
                for (int cx = Mathf.FloorToInt(lo.x / Cell); cx <= Mathf.FloorToInt(hi.x / Cell); cx++)
                    for (int cz = Mathf.FloorToInt(lo.y / Cell); cz <= Mathf.FloorToInt(hi.y / Cell); cz++)
                    {
                        long k = Key(cx, cz);
                        if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Col>();
                        l.Add(c);
                    }
            }
            foreach (var r in recs)
            {
                var f = fs[r.fi];
                if (r.kind == 1) continue;
                if (r.kind == 0) { Add(new Col { plan = r.plan, b = r.baseY, t = r.topY }); continue; }
                float g = float.MaxValue;
                foreach (var p in f.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                Add(new Col { plan = f.pts, b = g, t = g + f.h });
            }
            // the data's columns (relative: OSM's floors and tops), every footprint
            var dgrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < fs.Length; i++)
            {
                if (fs[i].hidden) continue;
                Vector2 lo = fs[i].pts[0], hi = fs[i].pts[0];
                foreach (var p in fs[i].pts) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
                for (int cx = Mathf.FloorToInt(lo.x / Cell); cx <= Mathf.FloorToInt(hi.x / Cell); cx++)
                    for (int cz = Mathf.FloorToInt(lo.y / Cell); cz <= Mathf.FloorToInt(hi.y / Cell); cz++)
                    {
                        long k = Key(cx, cz);
                        if (!dgrid.TryGetValue(k, out var l)) dgrid[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            var iv = new List<Vector2>();
            bool Filled(Vector2 q, float from, float floor, Col self)
            {
                iv.Clear();
                if (grid.TryGetValue(Key(Mathf.FloorToInt(q.x / Cell), Mathf.FloorToInt(q.y / Cell)), out var l))
                    foreach (var c in l) if (!ReferenceEquals(c.plan, self.plan) && CityMap.PointInPoly(c.plan, q)) iv.Add(new Vector2(c.b, c.t));
                iv.Sort((a, b) => a.x.CompareTo(b.x));
                float cur = from;
                foreach (var v in iv) { if (v.x > cur + 0.6f) break; cur = Mathf.Max(cur, v.y); }
                return cur >= floor - 0.6f;
            }
            bool DataFilled(Vector2 q, int self, float floor)
            {
                iv.Clear();
                if (dgrid.TryGetValue(Key(Mathf.FloorToInt(q.x / Cell), Mathf.FloorToInt(q.y / Cell)), out var l))
                    foreach (int j in l) if (j != self && CityMap.PointInPoly(fs[j].pts, q)) iv.Add(new Vector2(fs[j].part ? fs[j].minH : 0f, fs[j].h));
                iv.Sort((a, b) => a.x.CompareTo(b.x));
                float cur = 0f;
                foreach (var v in iv) { if (v.x > cur + 0.6f) break; cur = Mathf.Max(cur, v.y); }
                return cur >= floor - 0.6f;
            }
            int hangingParts = 0, osmParts = 0, drawnParts = 0, fixedParts = 0, wallsHang = 0, wallsFixed = 0, wallsOsm = 0;
            var lines = new List<(float score, string s)>();
            foreach (var r in recs)
            {
                if (r.kind != 0 || r.plan == null) continue;
                var f = fs[r.fi];
                if (!f.part || f.minH <= 0.05f || r.baseY <= r.ground + 0.5f) continue;
                var self = new Col { plan = r.plan };
                int n = r.plan.Length, hang = 0, fixedW = 0, osm = 0, open = 0;
                float hangLen = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = r.plan[i], c = r.plan[(i + 1) % n], d = c - a;
                    float len = d.magnitude;
                    if (len < 0.2f) continue;
                    var inward = new Vector2(-d.y, d.x) / len;
                    int k = Mathf.Clamp(Mathf.CeilToInt(len / 4f), 2, 16);
                    bool h = false, dataHolds = true;
                    for (int s = 0; s < k; s++)
                    {
                        var q = Vector2.Lerp(a, c, (s + 0.5f) / k) + inward * 0.3f;
                        if (Filled(q, r.ground + 1.5f, r.baseY, self)) continue;
                        h = true;
                        if (!DataFilled(q, r.fi, f.minH)) dataHolds = false;
                    }
                    if (!h) continue;
                    hang++; hangLen += len;
                    bool fx = r.filled != null && i < r.filled.Length && r.filled[i];
                    if (fx) fixedW++;
                    else { open++; if (!dataHolds) osm++; }
                }
                if (hang == 0) continue;
                wallsHang += hang; wallsFixed += fixedW; wallsOsm += osm;
                if (open == 0) { fixedParts++; continue; }
                hangingParts++;
                bool isOsm = osm == open;
                if (isOsm) osmParts++; else drawnParts++;
                // its building as the tile drew it
                var st = new StringBuilder();
                int ol = f.outline;
                for (int j = 0; j < fs.Length && st.Length < 300; j++)
                {
                    if (j == r.fi || !(j == ol || (fs[j].part && fs[j].outline == ol && fs[j].minH < f.minH - 0.05f))) continue;
                    string how = !byFi.TryGetValue(j, out var rj) ? (fs[j].hidden ? "hidden" : "not built")
                        : rj.kind == 1 ? "LEFT OUT" : rj.kind != 0 ? "kind" + rj.kind
                        : (Area(rj.plan) < Area(fs[j].pts) - 1f ? $"cut to {100f * Area(rj.plan) / Area(fs[j].pts):0}%" : "whole");
                    st.Append($" #{j}{(fs[j].part ? "p" : "")} {(fs[j].part ? fs[j].minH : 0f):0.#}-{fs[j].h:0.#} {how};");
                }
                lines.Add((hangLen * (isOsm ? 1f : 10f),
                    $"  #{r.fi} ol {ol} {f.minH:0.#}-{f.h:0.#} m {(isOsm ? "OSM" : "DRAWN")} walls hang {open}/{n} ({hangLen:0} m) at {f.centre.x:0},{f.centre.y:0} {CityAudit.LatLon(f.centre.x, f.centre.y)} | under:{st}"));
            }
            sb.AppendLine($"FLOAT CENSUS: {hangingParts} floored parts hang over air ({drawnParts} DRAWN: mapped under them but not drawn; {osmParts} OSM: nothing mapped under them), {fixedParts} run to the ground; walls hanging {wallsHang}, run down {wallsFixed}, OSM {wallsOsm}");
            lines.Sort((a, b) => b.score.CompareTo(a.score));
            for (int i = 0; i < lines.Count && i < 60; i++) sb.AppendLine(lines[i].s);
        }

        static float Area(Vector2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++) { var u = p[i]; var v = p[(i + 1) % p.Length]; a += u.x * v.y - v.x * u.y; }
            return Mathf.Abs(a) * 0.5f;
        }
    }
}
